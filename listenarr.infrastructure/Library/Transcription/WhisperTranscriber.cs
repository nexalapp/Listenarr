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
using System.Globalization;
using Listenarr.Application.Audiobooks.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Whisper.net;
using Whisper.net.Ggml;

namespace Listenarr.Infrastructure.Library.Transcription
{
    /// <summary>
    /// Transcribes with whisper.cpp in-process through Whisper.net.
    ///
    /// <para>
    /// The model lives beside ffmpeg under the config folder and is downloaded on first
    /// use, so the image ships no model and a bigger one is a setting away. ffmpeg cuts
    /// the stretch to hear and resamples it to the 16kHz mono WAV whisper wants — it is
    /// the one decoder this app already trusts, and it seeks a multi-hour file in
    /// milliseconds. One transcription runs at a time: the NAS has no GPU, and two
    /// whisper runs on one CPU are slower than one after the other.
    /// </para>
    /// <para>
    /// A singleton because the loaded model is the expensive part: a factory per model
    /// file is kept for the life of the process and processors are built per call.
    /// </para>
    /// </summary>
    public sealed class WhisperTranscriber(
        IServiceScopeFactory scopeFactory,
        IApplicationPathService paths,
        ILogger<WhisperTranscriber> logger) : ITranscriber, IDisposable
    {
        private const int DecodeTimeoutMs = 120_000;
        private static readonly TimeSpan MaxWindow = TimeSpan.FromMinutes(3);

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<string, WhisperFactory> _factories = new(StringComparer.Ordinal);

        public string ModelRoot => paths.ResolveFromConfig("whisper");

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            var (enabled, model) = await ReadSettingsAsync();
            if (!enabled)
            {
                return false;
            }

            try
            {
                await EnsureModelAsync(model, cancellationToken);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "The whisper model {Model} is not available", model);
                return false;
            }
        }

        public async Task<Transcript> TranscribeAsync(
            string path,
            TimeSpan start,
            TimeSpan length,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (length <= TimeSpan.Zero || length > MaxWindow)
            {
                throw new ArgumentOutOfRangeException(nameof(length), $"A transcription window must be between 0 and {MaxWindow}.");
            }

            var (enabled, model) = await ReadSettingsAsync();
            if (!enabled)
            {
                throw new InvalidOperationException("Transcription is switched off.");
            }

            var modelPath = await EnsureModelAsync(model, cancellationToken);
            var audio = await DecodeAsync(path, start, length, cancellationToken);
            if (audio.Length == 0)
            {
                return Transcript.Empty;
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                var factory = GetFactory(modelPath);
                using var processor = factory.CreateBuilder()
                    .WithLanguage("en")
                    .WithThreads(Math.Max(1, Environment.ProcessorCount - 1))
                    .Build();

                using var stream = new MemoryStream(audio, writable: false);
                var segments = new List<string>();
                await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
                {
                    var text = segment.Text.Trim();
                    if (text.Length > 0)
                    {
                        segments.Add(text);
                    }
                }

                return Transcript.FromSegments(segments);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<(bool Enabled, string Model)> ReadSettingsAsync()
        {
            using var scope = scopeFactory.CreateScope();
            var settings = await scope.ServiceProvider.GetRequiredService<IConfigurationService>().GetApplicationSettingsAsync();
            var model = string.IsNullOrWhiteSpace(settings.TranscriptionModel) ? "base.en" : settings.TranscriptionModel.Trim();
            return (settings.TranscriptionEnabled, model);
        }

        /// <summary>The model file, downloading it the first time it is asked for.</summary>
        private async Task<string> EnsureModelAsync(string model, CancellationToken cancellationToken)
        {
            var type = ModelType(model);
            var fileName = $"ggml-{model}.bin";
            Directory.CreateDirectory(ModelRoot);
            var modelPath = Path.Combine(ModelRoot, fileName);
            if (File.Exists(modelPath) && new FileInfo(modelPath).Length > 0)
            {
                return modelPath;
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (File.Exists(modelPath) && new FileInfo(modelPath).Length > 0)
                {
                    return modelPath;
                }

                logger.LogInformation("Downloading whisper model {Model} to {Path}", model, ModelRoot);
                var partial = modelPath + ".part";
                await using (var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, QuantizationType.NoQuantization, cancellationToken))
                await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await source.CopyToAsync(target, cancellationToken);
                }

                File.Move(partial, modelPath, overwrite: true);
                logger.LogInformation("Whisper model {Model} ready ({Size} MB)", model, new FileInfo(modelPath).Length / 1_048_576);
                return modelPath;
            }
            finally
            {
                _gate.Release();
            }
        }

        private static GgmlType ModelType(string model) => model.ToLowerInvariant() switch
        {
            "tiny" => GgmlType.Tiny,
            "tiny.en" => GgmlType.TinyEn,
            "base" => GgmlType.Base,
            "base.en" => GgmlType.BaseEn,
            "small" => GgmlType.Small,
            "small.en" => GgmlType.SmallEn,
            "medium" => GgmlType.Medium,
            "medium.en" => GgmlType.MediumEn,
            _ => throw new ArgumentException($"'{model}' is not a whisper model this app knows.")
        };

        private WhisperFactory GetFactory(string modelPath)
        {
            lock (_factories)
            {
                if (!_factories.TryGetValue(modelPath, out var factory))
                {
                    factory = WhisperFactory.FromPath(modelPath);
                    _factories[modelPath] = factory;
                }

                return factory;
            }
        }

        /// <summary>The stretch as 16kHz mono PCM WAV, straight from ffmpeg's stdout.</summary>
        private async Task<byte[]> DecodeAsync(string path, TimeSpan start, TimeSpan length, CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var ffmpeg = await scope.ServiceProvider.GetRequiredService<IFfmpegService>().GetFfmpegPathAsync();
            if (string.IsNullOrEmpty(ffmpeg))
            {
                throw new InvalidOperationException("No ffmpeg is installed, so audio cannot be decoded for transcription.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
                     {
                         "-hide_banner", "-nostdin", "-loglevel", "error",
                         "-ss", start.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
                         "-t", length.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
                         "-i", path,
                         "-vn", "-ac", "1", "-ar", "16000", "-f", "wav", "pipe:1"
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            var runner = scope.ServiceProvider.GetRequiredService<IProcessRunner>();
            using var process = runner.StartProcess(startInfo);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DecodeTimeoutMs);

            var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                await copy;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                throw;
            }

            if (process.ExitCode != 0)
            {
                throw new FfmpegException($"ffmpeg could not decode {LogRedaction.SanitizeFilePath(path)} for transcription: {(await stderr).Trim()}");
            }

            return output.ToArray();
        }

        public void Dispose()
        {
            lock (_factories)
            {
                foreach (var factory in _factories.Values)
                {
                    factory.Dispose();
                }

                _factories.Clear();
            }

            _gate.Dispose();
        }
    }
}
