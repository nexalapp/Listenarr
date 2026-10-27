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
using System.Diagnostics;
using System.Text.Json;
using Listenarr.Application.Audiobooks.Repair;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Repair
{
    /// <summary>
    /// Resemble Enhance as a lazily provisioned local subprocess.
    ///
    /// <para>
    /// The production image carries no Python, deliberately: an install that never turns
    /// repairs on never pays the four gigabytes. First enable bootstraps everything into
    /// the config volume — the <c>uv</c> binary (sha-pinned), a standalone Python, a venv
    /// of pinned packages, and the model weights at a pinned revision — exactly as ffmpeg
    /// and the whisper models are fetched on demand. The install survives image
    /// redeploys because the config volume does.
    /// </para>
    /// <para>
    /// Everything is installed under <c>install-*</c> and <c>current</c> is pointed at it,
    /// as a link, only after a real inference has passed through it, with the manifest
    /// written last inside it. The install never moves: a venv is not relocatable —
    /// its interpreter link, its <c>pyvenv.cfg</c> and every script's shebang name the
    /// directory it was made in — and the first install, renamed into place, passed its
    /// smoke test and then could not start a single repair. A directory without a
    /// manifest is a failed provision and is redone; a manifest whose pins differ from
    /// this build's is an upgrade and is redone the same way.
    /// </para>
    /// </summary>
    public sealed partial class ResembleAudioEnhancer(
        IApplicationPathService paths,
        IProcessRunner processRunner,
        ILogger<ResembleAudioEnhancer> logger) : IAudioEnhancer
    {
        /// <summary>Env override naming a pre-provisioned install directory; trusted as Ready, like ffmpeg's.</summary>
        public const string PathOverrideVariable = "LISTENARR_ENHANCER_PATH";

        /// <summary>
        /// Changes whenever the model, its packages or the chunk pipeline change audible
        /// behaviour — never for a refactor. A resume under a different version redoes
        /// its intermediates, so one book never mixes two models.
        /// </summary>
        public const string PipelineVersion = "v1";

        internal const string UvVersion = "0.13.1";
        private const string UvShaX64 = "eadd9d27c134ab5ac1ffd4473b97a741407f3ea238288f03121fc4deddf90220";
        private const string UvShaArm64 = "9041c73c99fdc5892cf128140beb354eca9231d502068cc36e6cb53a8e59ad14";
        internal const string PythonVersion = "3.11.15";
        private const string ModelRepo = "ResembleAI/resemble-enhance";
        internal const string ModelRevision = "4e3510ce4a8391159f665903544c5150bee7b2cb";

        /// <summary>
        /// Solver steps per chunk. Sixteen runs at ~1.2× realtime on eight threads where
        /// sixty-four costs ~2.1× for two more kilohertz of bandwidth; on jobs measured
        /// in days, the cheaper setting is the difference between one day and two.
        /// </summary>
        public const int SolverSteps = 16;

        private const int Threads = 8;

        /// <summary>
        /// The packages installed beside torch, with the load-bearing ones pinned to the
        /// versions a real inference was proven against; uv resolves the remainder.
        /// deepspeed is pinned although inference never calls it — resemble-enhance
        /// imports it at module load, and its sdist installs cleanly only because torch
        /// is already in the venv, which is why torch goes in alone first.
        /// </summary>
        private static readonly string[] EnhancementPackages =
        [
            "resemble-enhance==0.0.1",
            "deepspeed==0.12.4",
            "numpy==1.26.2",
            "scipy==1.11.4",
            "librosa==0.10.1",
            "soundfile==0.12.1",
            "huggingface_hub"
        ];

        private readonly object _stateLock = new();
        private EnhancerStatus _status = new(EnhancerState.NotProvisioned, 0, null, null, null, null);
        private Task? _provisioning;

        private string EnhancerRoot => paths.ResolveFromConfig("enhancer");
        private string CurrentRoot => Path.Combine(EnhancerRoot, "current");

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ResolveInstall() != null);

        public Task<EnhancerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            var install = ResolveInstall();
            if (install != null)
            {
                return Task.FromResult(new EnhancerStatus(
                    EnhancerState.Ready, 100, null, install.Value.Manifest.PipelineVersion,
                    install.Value.Manifest.DiskBytes, null));
            }

            lock (_stateLock)
            {
                return Task.FromResult(_status);
            }
        }

        public Task<EnhancerStatus> ProvisionAsync(CancellationToken cancellationToken = default)
        {
            lock (_stateLock)
            {
                if (ResolveInstall() != null)
                {
                    return GetStatusAsync(cancellationToken);
                }

                if (_provisioning is { IsCompleted: false })
                {
                    return Task.FromResult(_status);
                }

                _status = new EnhancerStatus(EnhancerState.Provisioning, 0, "Starting", null, null, null);
                // Deliberately not the caller's token: the download belongs to the
                // install, not to whichever HTTP request happened to kick it off.
                _provisioning = Task.Run(() => ProvisionCoreAsync(CancellationToken.None), CancellationToken.None);
                return Task.FromResult(_status);
            }
        }

        public async Task EnhanceChunkAsync(
            string inputWavPath,
            string outputWavPath,
            TimeSpan chunkDuration,
            CancellationToken cancellationToken = default)
        {
            var install = ResolveInstall()
                ?? throw new EnhancerUnavailableException("The enhancement model is not provisioned.");

            // The CLI processes a directory, so each chunk gets a private pair of them.
            // The input is linked rather than copied — a chunk is a quarter-gigabyte WAV.
            var workRoot = outputWavPath + ".work";
            var inDir = Path.Combine(workRoot, "in");
            var outDir = Path.Combine(workRoot, "out");
            // A restart mid-chunk leaves the last attempt's pair behind, link included.
            // Linking over it fails, and the copy fallback then copies the chunk onto the
            // link to itself — which failed a 20-hour repair on resume. Start clean.
            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, recursive: true);
            }

            Directory.CreateDirectory(inDir);
            Directory.CreateDirectory(outDir);
            var linkedInput = Path.Combine(inDir, "chunk.wav");
            try
            {
                try
                {
                    File.CreateSymbolicLink(linkedInput, Path.GetFullPath(inputWavPath));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    File.Copy(inputWavPath, linkedInput, overwrite: true);
                }

                // Four times realtime plus slack: the model runs one to two times
                // realtime on these threads, so anything past this is a stuck process,
                // not a slow one.
                var timeout = chunkDuration * 4 + TimeSpan.FromMinutes(10);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linked.CancelAfter(timeout);

                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(install.Root, "venv", "bin", "resemble-enhance"),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var argument in new[]
                         {
                             inDir, outDir,
                             "--device", "cpu",
                             "--nfe", SolverSteps.ToString(),
                             "--run_dir", Path.Combine(install.Root, "models", "enhancer_stage2")
                         })
                {
                    startInfo.ArgumentList.Add(argument);
                }

                GiveAHome(startInfo);
                startInfo.EnvironmentVariables["OMP_NUM_THREADS"] = Threads.ToString();
                startInfo.EnvironmentVariables["MKL_NUM_THREADS"] = Threads.ToString();
                // The weights were fetched at provision time; inference never phones home.
                startInfo.EnvironmentVariables["HF_HOME"] = Path.Combine(install.Root, "models");
                startInfo.EnvironmentVariables["HF_HUB_OFFLINE"] = "1";
                startInfo.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";

                using var process = StartInference(startInfo);
                var stderr = process.StandardError.ReadToEndAsync(linked.Token);
                var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
                try
                {
                    await process.WaitForExitAsync(linked.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // Exited between the timeout and the kill.
                    }

                    throw new TimeoutException(
                        $"The enhancement model spent more than {timeout} on a {chunkDuration} chunk and was stopped.");
                }

                var produced = Path.Combine(outDir, "chunk.wav");
                if (process.ExitCode != 0 || !File.Exists(produced) || new FileInfo(produced).Length == 0)
                {
                    var tail = Tail(await stderr) is { Length: > 0 } errorTail ? errorTail : Tail(await stdout);
                    throw new InvalidOperationException(
                        $"The enhancement model failed on a chunk (exit {process.ExitCode}): {tail}");
                }

                File.Move(produced, outputWavPath, overwrite: true);
            }
            finally
            {
                try
                {
                    Directory.Delete(workRoot, recursive: true);
                }
                catch (IOException)
                {
                    // A straggler handle; the job's scratch sweep gets it later.
                }
            }
        }

        private Process StartInference(ProcessStartInfo startInfo)
        {
            try
            {
                return processRunner.StartProcess(startInfo);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                // The install is there but cannot run: not this chunk's fault, and
                // retrying the chunk will not help.
                throw new EnhancerUnavailableException(
                    $"The enhancement model is installed but cannot start: {exception.Message}");
            }
        }

        // ------------------------------------------------------------------ provisioning

        private readonly record struct Install(string Root, InstallManifest Manifest);

        private sealed record InstallManifest(
            int Schema,
            string PipelineVersion,
            string UvVersion,
            string PythonVersion,
            string ModelRevision,
            DateTime CompletedAtUtc,
            long DiskBytes);

        private Install? ResolveInstall()
        {
            var overridePath = Environment.GetEnvironmentVariable(PathOverrideVariable);
            if (!string.IsNullOrWhiteSpace(overridePath)
                && File.Exists(Path.Combine(overridePath, "venv", "bin", "resemble-enhance")))
            {
                // An operator-supplied install is trusted as given, like LISTENARR_FFMPEG_PATH.
                return new Install(
                    overridePath,
                    new InstallManifest(1, PipelineVersion, "external", "external", ModelRevision, DateTime.MinValue, 0));
            }

            var manifestPath = Path.Combine(CurrentRoot, "install.json");
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(manifestPath));
                if (manifest == null
                    || manifest.PipelineVersion != PipelineVersion
                    || manifest.UvVersion != UvVersion
                    || manifest.PythonVersion != PythonVersion
                    || manifest.ModelRevision != ModelRevision)
                {
                    // An install made by an older build: not broken, just not this
                    // pipeline. Re-provisioning replaces it wholesale.
                    return null;
                }

                return File.Exists(Path.Combine(CurrentRoot, "venv", "bin", "resemble-enhance"))
                       && Resolves(Path.Combine(CurrentRoot, "venv", "bin", "python"))
                    ? new Install(CurrentRoot, manifest)
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Whether a path, following every link, ends at a file. The venv's interpreter is
        /// a link into the Python install; an install moved after it was made keeps every
        /// file and loses that link's target, and nothing in it can start.
        /// </summary>
        internal static bool Resolves(string path)
        {
            var file = new FileInfo(path);
            if (file.LinkTarget == null)
            {
                return file.Exists;
            }

            try
            {
                return file.ResolveLinkTarget(returnFinalTarget: true) is { Exists: true };
            }
            catch (IOException)
            {
                return false;
            }
        }

        private void Report(double progress, string step)
        {
            lock (_stateLock)
            {
                _status = new EnhancerStatus(EnhancerState.Provisioning, progress, step, null, null, null);
            }

            logger.LogInformation("Provisioning the enhancement model: {Step} ({Progress:0}%)", step, progress);
        }

        private const string UvCacheDirectory = ".uv-cache";

        /// <summary>
        /// Point every tool's home and caches into the enhancer's own directory. The app
        /// runs as a service user whose home is <c>/nonexistent</c>, and uv, numba,
        /// matplotlib and torch all write under the home by default — uv refused to start
        /// at all on the first real install. Measured, not hypothetical.
        /// </summary>
        private void GiveAHome(ProcessStartInfo startInfo)
        {
            var home = Path.Combine(EnhancerRoot, "home");
            var cache = Path.Combine(home, ".cache");
            Directory.CreateDirectory(cache);
            startInfo.EnvironmentVariables["HOME"] = home;
            startInfo.EnvironmentVariables["XDG_CACHE_HOME"] = cache;
            startInfo.EnvironmentVariables["NUMBA_CACHE_DIR"] = Path.Combine(cache, "numba");
            startInfo.EnvironmentVariables["MPLCONFIGDIR"] = Path.Combine(cache, "matplotlib");
            startInfo.EnvironmentVariables["TORCH_HOME"] = Path.Combine(cache, "torch");
        }

        private async Task RunToolAsync(
            string fileName,
            string[] arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            GiveAHome(startInfo);
            // uv puts its Python installs beside the venv rather than in the image's
            // home directory, so the whole install moves as one directory. Its download
            // cache goes in the staging directory too and is deleted before the install
            // is sized: two gigabytes of wheels nothing will read again.
            startInfo.EnvironmentVariables["UV_PYTHON_INSTALL_DIR"] = Path.Combine(workingDirectory, "python");
            startInfo.EnvironmentVariables["UV_CACHE_DIR"] = Path.Combine(workingDirectory, UvCacheDirectory);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            using var process = processRunner.StartProcess(startInfo);
            var stderr = process.StandardError.ReadToEndAsync(linked.Token);
            _ = process.StandardOutput.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(fileName)} {arguments.FirstOrDefault()} failed (exit {process.ExitCode}): {Tail(await stderr)}");
            }
        }

        private static long DirectorySize(string root) =>
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);

        private static string Tail(string text)
        {
            var trimmed = text.Trim();
            return trimmed.Length <= 400 ? trimmed : trimmed[^400..];
        }
    }
}
