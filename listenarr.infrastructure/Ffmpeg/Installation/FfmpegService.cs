/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Ffmpeg.Installation
{
    public partial class FfmpegService : IFfmpegService
    {
        private readonly string _baseDir;
        private readonly string _ffprobeName;
        private readonly string _ffprobePath;
        private readonly string _ffmpegName;
        private readonly string _ffmpegPath;
        private readonly ILogger<FfmpegService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IStartupConfigService _startupConfigService;
        private readonly IProcessRunner _processRunner;
        private readonly FfprobeGithubAssetDiscoverer _githubAssetDiscoverer;
        // Allow disabling auto-download via environment variable
        private readonly bool _autoInstall;

        public FfmpegService(
            ILogger<FfmpegService> logger,
            HttpClient httpClient,
            IStartupConfigService startupConfigService,
            IProcessRunner processRunner,
            IApplicationPathService applicationPathService)
        {
            _logger = logger;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            // Use a longer timeout than HttpClient's 100s default because static ffprobe archives
            // can be large and hosts may have slow links. Allow override via environment variable.
            var timeoutSeconds = 300;
            var timeoutEnv = Environment.GetEnvironmentVariable("LISTENARR_FFPROBE_DOWNLOAD_TIMEOUT_SECONDS");
            if (!string.IsNullOrWhiteSpace(timeoutEnv)
                && int.TryParse(timeoutEnv, out var parsedSeconds)
                && parsedSeconds > 0)
            {
                timeoutSeconds = parsedSeconds;
            }
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            _githubAssetDiscoverer = new FfprobeGithubAssetDiscoverer(_httpClient, _logger);
            _autoInstall = Environment.GetEnvironmentVariable("LISTENARR_AUTO_INSTALL_FFPROBE")?.ToLower() != "false"; // default true
            _startupConfigService = startupConfigService;
            _processRunner = processRunner;

            _baseDir = applicationPathService.FfmpegRootPath;
            _ffprobeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";
            _ffprobePath = Path.Join(_baseDir, _ffprobeName);
            _ffmpegName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
            _ffmpegPath = Path.Join(_baseDir, _ffmpegName);
        }

        private static async Task TryDeleteFileAsync(string path, int retries = 3, int delayMs = 100, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path)) return;
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                catch (Exception caughtEx_1) when (caughtEx_1 is not OperationCanceledException && caughtEx_1 is not OutOfMemoryException && caughtEx_1 is not StackOverflowException)
                {
                    if (i == retries - 1) return;
                    try { await Task.Delay(delayMs, cancellationToken); } catch (OperationCanceledException) { return; }
                }
            }
        }

        /// <summary>
        /// Return the ffprobe path if it exists in the configured bundled directory. This method
        /// does NOT attempt to download or install ffprobe.
        /// </summary>
        public Task<string?> GetFfprobePathAsync()
        {
            if (File.Exists(_ffprobePath))
            {
                _logger.LogInformation("Found bundled ffprobe at {Path}", _ffprobePath);
                return Task.FromResult<string?>(_ffprobePath);
            }

            _logger.LogInformation("No bundled ffprobe found at {Path}", _ffprobePath);
            return Task.FromResult<string?>(null);
        }

        /// <summary>
        /// Ensure ffprobe is installed into the bundled directory. This performs the download
        /// and extraction flow when no bundled binary exists. Intended to be called once at startup.
        /// </summary>
        public Task<string?> EnsureFfprobeInstalledAsync() =>
            EnsureBundledBinariesAsync(requireEncoder: false);

        /// <summary>
        /// The shared install flow. <paramref name="requireEncoder"/> makes an existing ffprobe
        /// insufficient to skip the download, which is what lets a config directory left behind
        /// by an ffprobe-only release acquire an encoder.
        /// </summary>
        private async Task<string?> EnsureBundledBinariesAsync(bool requireEncoder)
        {
            // On every platform whose archive carries both binaries, the encoder arrives with
            // ffprobe - but only on the run that actually downloads it. Older releases extracted
            // ffprobe alone, so an install carried forward from one has ffprobe and no ffmpeg,
            // and returning here on the strength of ffprobe alone left it that way permanently.
            if (await GetFfprobePathAsync() != null
                && !(requireEncoder && !File.Exists(_ffmpegPath)))
            {
                return _ffprobePath;
            }

            if (!_autoInstall)
            {
                _logger.LogInformation("Auto-install of ffprobe is disabled via LISTENARR_AUTO_INSTALL_FFPROBE");
                return null;
            }

            Directory.CreateDirectory(_baseDir);

            try
            {
                // The original installation logic has been preserved here. It will only run when
                // EnsureFfprobeInstalledAsync is called (e.g., at program startup).
                string? downloadUrl = GetDownloadUrlForPlatform();
                string? discoveredChecksum = null;
                try
                {
                    var cfg = _startupConfigService.GetConfig();
                    if (cfg?.Ffmpeg?.Provider != null && cfg.Ffmpeg.Provider.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = cfg.Ffmpeg.Provider.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            var repo = parts[1];
                            var assetInfo = await _githubAssetDiscoverer.TryDiscoverAsync(repo, cfg.Ffmpeg.ReleaseOverride, cfg.Ffmpeg.Arch);
                            if (!string.IsNullOrEmpty(assetInfo.AssetUrl))
                            {
                                downloadUrl = assetInfo.AssetUrl;
                                if (!string.IsNullOrEmpty(assetInfo.ChecksumContent))
                                {
                                    discoveredChecksum = assetInfo.ChecksumContent;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogWarning(ex, "Error while reading startup ffmpeg provider config");
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    _logger.LogWarning("No ffprobe download URL configured for this platform");
                    return null;
                }

                _logger.LogInformation("Downloading ffprobe from {Url}", downloadUrl);

                using var resp = await _httpClient.GetAsync(downloadUrl);
                resp.EnsureSuccessStatusCode();

                var tmpFile = Path.Join(_baseDir, "ffprobe-download.tmp");
                if (!FileSystemSafety.TryValidateMutationTarget(tmpFile, [_baseDir], out tmpFile, out var tmpReason))
                {
                    _logger.LogWarning("Blocked ffprobe download temp path: {Reason}", LogRedaction.SanitizeText(tmpReason));
                    return null;
                }

                await using (var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write))
                {
                    await resp.Content.CopyToAsync(fs);
                }

                // Compute SHA256 for logging / future verification
                string? computedHash = null;
                try
                {
                    using var sha = SHA256.Create();
                    await using var fs2 = File.OpenRead(tmpFile);
                    var hash = await sha.ComputeHashAsync(fs2);
                    var hashHex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    computedHash = hashHex;
                    _logger.LogInformation("Downloaded ffprobe archive SHA256={Hash}", hashHex);
                }
                catch (Exception caughtEx_2) when (caughtEx_2 is not OperationCanceledException && caughtEx_2 is not OutOfMemoryException && caughtEx_2 is not StackOverflowException)
                { /* non-fatal */
                    System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
                }

                var expected = GetChecksumForPlatform();
                if (string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(discoveredChecksum))
                {
                    var parsed = FfprobeChecksumParser.ParseForAsset(discoveredChecksum, Path.GetFileName(downloadUrl));
                    if (!string.IsNullOrEmpty(parsed)) expected = parsed;
                }

                if (string.IsNullOrEmpty(expected))
                {
                    try
                    {
                        var checksumFiles = Directory.GetFiles(_baseDir, "*checksum*", SearchOption.TopDirectoryOnly)
                            .Concat(Directory.GetFiles(_baseDir, "SHA256*", SearchOption.TopDirectoryOnly));
                        foreach (var cf in checksumFiles)
                        {
                            try
                            {
                                var content = await File.ReadAllTextAsync(cf);
                                var parsed = FfprobeChecksumParser.ParseForAsset(content, Path.GetFileName(downloadUrl));
                                if (!string.IsNullOrEmpty(parsed)) { expected = parsed; break; }
                            }
                            catch (Exception caughtEx_3) when (caughtEx_3 is not OperationCanceledException && caughtEx_3 is not OutOfMemoryException && caughtEx_3 is not StackOverflowException)
                            {
                                System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
                            }
                        }
                    }
                    catch (Exception caughtEx_4) when (caughtEx_4 is not OperationCanceledException && caughtEx_4 is not OutOfMemoryException && caughtEx_4 is not StackOverflowException)
                    {
                        System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
                    }
                }

                if (!string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(computedHash) && !string.Equals(expected, computedHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Downloaded ffprobe checksum mismatch (expected {Expected} != actual {Actual}). Aborting install.", expected, computedHash);
                    await TryDeleteFileAsync(tmpFile);
                    return null;
                }

                if (!await FfprobeArchiveExtractor.ExtractAsync(downloadUrl, tmpFile, _baseDir, _ffprobePath, _logger))
                {
                    return null;
                }

                // Promote both binaries out of the archive's own layout. ffmpeg used to be
                // left behind here: extracted, not executable, and referenced by nothing,
                // which is why no encoder was available despite a successful "install".
                await FfmpegBinaryPromoter.PromoteAsync(
                    _baseDir,
                    _ffprobeName,
                    _ffprobePath,
                    _processRunner,
                    _logger);
                await FfmpegBinaryPromoter.PromoteAsync(
                    _baseDir,
                    _ffmpegName,
                    _ffmpegPath,
                    _processRunner,
                    _logger);

                // Some platforms publish the encoder as a separate archive, so a second
                // download is the only way to get one. A failure here is not fatal:
                // ffprobe-only operation is still the pre-existing behaviour.
                if (!File.Exists(_ffmpegPath))
                {
                    await TryInstallSeparateFfmpegArchiveAsync();
                }

                if (!File.Exists(_ffprobePath))
                {
                    _logger.LogWarning("ffprobe install did not produce the expected binary at {Path}", _ffprobePath);
                    return null;
                }

                var licensePath = Path.Join(_baseDir, "LICENSE_NOTICE.txt");
                await File.WriteAllTextAsync(licensePath, "ffprobe binaries downloaded. Review FFmpeg licensing (LGPL/GPL) at https://ffmpeg.org/legal.html\nSource: " + downloadUrl + "\n");

                _logger.LogInformation("ffprobe installed to {Path}", _ffprobePath);
                return _ffprobePath;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "ffprobe download/install timed out or was canceled");
                return null;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "ffprobe download/install was canceled");
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to download or install ffprobe");
                return null;
            }
        }

        /// <summary>
        /// Resolve an ffmpeg encoder. Unlike ffprobe this prefers a host-provided binary:
        /// the production image ships none, so an operator override and PATH are the only
        /// resolutions available before the bundled download has succeeded.
        /// </summary>
        public Task<string?> GetFfmpegPathAsync()
        {
            var configured = FfmpegPathLocator.FromEnvironment("LISTENARR_FFMPEG_PATH");
            if (configured != null)
            {
                _logger.LogInformation("Using operator-configured ffmpeg at {Path}", configured);
                return Task.FromResult<string?>(configured);
            }

            var onPath = FfmpegPathLocator.FromSearchPath(_ffmpegName);
            if (onPath != null)
            {
                _logger.LogInformation("Found ffmpeg on PATH at {Path}", onPath);
                return Task.FromResult<string?>(onPath);
            }

            if (File.Exists(_ffmpegPath))
            {
                _logger.LogInformation("Found bundled ffmpeg at {Path}", _ffmpegPath);
                return Task.FromResult<string?>(_ffmpegPath);
            }

            _logger.LogInformation("No ffmpeg found on PATH or bundled at {Path}", _ffmpegPath);
            return Task.FromResult<string?>(null);
        }

        /// <summary>
        /// Ensure an ffmpeg encoder is available, downloading the bundled archive when
        /// the host provides none. Returns the resolved path or null.
        /// </summary>
        public async Task<string?> EnsureFfmpegInstalledAsync()
        {
            var existing = await GetFfmpegPathAsync();
            if (existing != null)
            {
                return existing;
            }

            // The archive that carries ffprobe carries the encoder too, so running the install
            // is what makes a bundled ffmpeg appear. Ask for it explicitly: an ffprobe that is
            // already in place must not be taken as proof the encoder came with it.
            await EnsureBundledBinariesAsync(requireEncoder: true);
            return await GetFfmpegPathAsync();
        }

        /// <summary>
        /// Download an encoder-only archive for platforms whose ffprobe archive carries no
        /// ffmpeg. Best effort: every failure leaves the ffprobe install intact.
        /// </summary>
        private async Task TryInstallSeparateFfmpegArchiveAsync()
        {
            var url = FfprobePlatformDefaults.GetFfmpegDownloadUrl();
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            try
            {
                _logger.LogInformation("Downloading ffmpeg from {Url}", url);
                using var resp = await _httpClient.GetAsync(url);
                resp.EnsureSuccessStatusCode();

                var tmpFile = Path.Join(_baseDir, "ffmpeg-download.tmp");
                if (!FileSystemSafety.TryValidateMutationTarget(tmpFile, [_baseDir], out tmpFile, out var tmpReason))
                {
                    _logger.LogWarning("Blocked ffmpeg download temp path: {Reason}", LogRedaction.SanitizeText(tmpReason));
                    return;
                }

                await using (var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write))
                {
                    await resp.Content.CopyToAsync(fs);
                }

                if (!await FfprobeArchiveExtractor.ExtractAsync(url, tmpFile, _baseDir, _ffmpegPath, _logger))
                {
                    return;
                }

                await FfmpegBinaryPromoter.PromoteAsync(
                    _baseDir,
                    _ffmpegName,
                    _ffmpegPath,
                    _processRunner,
                    _logger);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to download or install a separate ffmpeg archive");
            }
        }

        private string? GetDownloadUrlForPlatform()
        {
            return FfprobePlatformDefaults.GetDownloadUrl();
        }

        private string? GetChecksumForPlatform()
        {
            return FfprobePlatformDefaults.GetChecksum();
        }

    }
}
