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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Listenarr.Application.Audiobooks.Repair;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Repair
{
    public sealed partial class ResembleAudioEnhancer
    {
        private async Task ProvisionCoreAsync(CancellationToken cancellationToken)
        {
            var staging = Path.Combine(EnhancerRoot, $"staging-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(staging);
                SweepStaleStaging(except: staging);

                Report(5, "Downloading uv");
                var uv = await DownloadUvAsync(staging, cancellationToken);

                Report(15, "Installing Python");
                await RunToolAsync(
                    uv, ["python", "install", PythonVersion],
                    staging, TimeSpan.FromMinutes(10), cancellationToken);

                Report(25, "Creating the environment");
                await RunToolAsync(
                    uv, ["venv", "venv", "--python", PythonVersion],
                    staging, TimeSpan.FromMinutes(5), cancellationToken);

                // Torch goes in alone first: it is the one platform-specific piece (the
                // CPU wheel index exists only for x86_64) and other packages' builds
                // import it.
                Report(35, "Downloading the inference runtime (~2 GB)");
                var venvPython = Path.Combine(staging, "venv");
                if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    await RunToolAsync(
                        uv,
                        ["pip", "install", "--python", venvPython,
                         "--index-url", "https://download.pytorch.org/whl/cpu",
                         "torch==2.1.1+cpu", "torchaudio==2.1.1+cpu"],
                        staging, TimeSpan.FromMinutes(30), cancellationToken);
                }
                else
                {
                    await RunToolAsync(
                        uv,
                        ["pip", "install", "--python", venvPython, "torch==2.1.1", "torchaudio==2.1.1"],
                        staging, TimeSpan.FromMinutes(30), cancellationToken);
                }

                Report(55, "Downloading the enhancement packages");
                await RunToolAsync(
                    uv,
                    ["pip", "install", "--python", venvPython, .. EnhancementPackages],
                    staging, TimeSpan.FromMinutes(30), cancellationToken);

                Report(70, "Downloading the model weights (~1.6 GB)");
                await RunToolAsync(
                    Path.Combine(staging, "venv", "bin", "hf"),
                    ["download", ModelRepo, "--revision", ModelRevision,
                     "--local-dir", Path.Combine(staging, "models")],
                    staging, TimeSpan.FromMinutes(60), cancellationToken);

                Report(90, "Verifying with a real inference");
                await SmokeTestAsync(staging, cancellationToken);

                Report(97, "Finishing");
                var diskBytes = DirectorySize(staging);
                var manifest = new InstallManifest(
                    1, PipelineVersion, UvVersion, PythonVersion, ModelRevision, DateTime.UtcNow, diskBytes);
                await File.WriteAllTextAsync(
                    Path.Combine(staging, "install.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                    cancellationToken);

                if (Directory.Exists(CurrentRoot))
                {
                    // An install with stale pins; it was already judged not-Ready.
                    Directory.Delete(CurrentRoot, recursive: true);
                }

                Directory.Move(staging, CurrentRoot);
                lock (_stateLock)
                {
                    _status = new EnhancerStatus(EnhancerState.Ready, 100, null, PipelineVersion, diskBytes, null);
                }

                logger.LogInformation(
                    "Enhancement model provisioned: pipeline {Version}, {Bytes:N0} bytes", PipelineVersion, diskBytes);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
            {
                logger.LogError(exception, "Provisioning the enhancement model failed");
                lock (_stateLock)
                {
                    _status = new EnhancerStatus(EnhancerState.Failed, 0, null, null, null, exception.Message);
                }

                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private async Task<string> DownloadUvAsync(string staging, CancellationToken cancellationToken)
        {
            var (arch, sha) = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => ("x86_64", UvShaX64),
                Architecture.Arm64 => ("aarch64", UvShaArm64),
                var other => throw new PlatformNotSupportedException($"No uv build for {other}.")
            };

            var url = $"https://github.com/astral-sh/uv/releases/download/{UvVersion}/uv-{arch}-unknown-linux-gnu.tar.gz";
            var archive = Path.Combine(staging, "uv.tar.gz");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            await using (var download = await http.GetStreamAsync(url, cancellationToken))
            await using (var file = File.Create(archive))
            {
                await download.CopyToAsync(file, cancellationToken);
            }

            var actual = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive, cancellationToken)));
            if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The uv download does not match its pinned checksum (got {actual}).");
            }

            await RunToolAsync(
                "tar", ["xzf", archive, "--strip-components=1", "-C", staging],
                staging, TimeSpan.FromMinutes(2), cancellationToken);
            File.Delete(archive);
            return Path.Combine(staging, "uv");
        }

        /// <summary>
        /// Enhance two seconds of generated 22.05 kHz tone through the staged install.
        /// The rate is the point: 22.05 kHz forces the resample path, whose lazily loaded
        /// packages are exactly what a missing dependency would take down on day three of
        /// a repair rather than now.
        /// </summary>
        private async Task SmokeTestAsync(string staging, CancellationToken cancellationToken)
        {
            var smokeDir = Path.Combine(staging, "smoke");
            var inDir = Path.Combine(smokeDir, "in");
            var outDir = Path.Combine(smokeDir, "out");
            Directory.CreateDirectory(inDir);
            Directory.CreateDirectory(outDir);
            WriteToneWav(Path.Combine(inDir, "tone.wav"), sampleRate: 22_050, seconds: 2);

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(staging, "venv", "bin", "resemble-enhance"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
                     {
                         inDir, outDir, "--device", "cpu", "--nfe", "4",
                         "--run_dir", Path.Combine(staging, "models", "enhancer_stage2")
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.EnvironmentVariables["OMP_NUM_THREADS"] = "4";
            startInfo.EnvironmentVariables["HF_HUB_OFFLINE"] = "1";

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var process = processRunner.StartProcess(startInfo);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            _ = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            var produced = Path.Combine(outDir, "tone.wav");
            if (process.ExitCode != 0 || !File.Exists(produced) || new FileInfo(produced).Length == 0)
            {
                throw new InvalidOperationException(
                    $"The staged install failed its smoke inference (exit {process.ExitCode}): {Tail(await stderr)}");
            }

            Directory.Delete(smokeDir, recursive: true);
        }

        private static void WriteToneWav(string path, int sampleRate, int seconds)
        {
            var samples = sampleRate * seconds;
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write("RIFF"u8);
            writer.Write(36 + samples * 2);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(samples * 2);
            for (var i = 0; i < samples; i++)
            {
                writer.Write((short)(12_000 * Math.Sin(2 * Math.PI * 220 * i / sampleRate)));
            }
        }

        // ------------------------------------------------------------------ resolution

        private void SweepStaleStaging(string except)
        {
            if (!Directory.Exists(EnhancerRoot))
            {
                return;
            }

            foreach (var directory in Directory.GetDirectories(EnhancerRoot, "staging-*"))
            {
                if (string.Equals(directory, except, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    // Anything still here is a previous attempt that died; a live one
                    // would have been renamed to current within the hour.
                    if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddHours(-6))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
