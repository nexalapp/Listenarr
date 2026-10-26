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
using Listenarr.Infrastructure.Library.Conversion;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Conversion
{
    /// <summary>
    /// Which cover an encode embeds. Every converted and repaired book used to come out
    /// with none, while the original archived beside it still had its art.
    /// </summary>
    [Trait("Name", "ConversionCoverTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class ConversionCoverTests : BaseTests
    {
        private readonly string _work = Path.Join(Path.GetTempPath(), $"conversion-cover-{Guid.NewGuid():N}");
        private readonly ApplicationSettings _settings = new() { EmbedCoverArtInTags = true };

        private string Book => Path.Combine(_work, "book");
        private string Scratch => Path.Combine(_work, "scratch");

        private async Task<ConversionJobProcessor.CoverForEncode?> ResolveAsync(params string[] sources)
        {
            Directory.CreateDirectory(Scratch);
            var ffmpeg = new Mock<IFfmpegService>();
            ffmpeg.Setup(f => f.GetFfmpegPathAsync()).ReturnsAsync(EncoderFactAttribute.FindOnPath("ffmpeg"));
            var images = new Mock<IImageCacheService>();
            images.Setup(i => i.GetCachedImagePathAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
            var services = new ServiceCollection()
                .AddSingleton(ffmpeg.Object)
                .AddSingleton(images.Object)
                .AddSingleton(new Mock<IApplicationPathService>().Object)
                .BuildServiceProvider();

            var audiobook = new AudiobookBuilder().WithId(1).WithTitle("Zero").WithBasePath(Book).Build();
            var originals = sources
                .Select(path => new ConversionSource(path, Path.GetFileName(path), TimeSpan.FromSeconds(2), 64_000, 44_100, 1))
                .ToList();
            return await new ConversionJobProcessor(new Mock<IServiceScopeFactory>().Object, NullLogger<ConversionJobProcessor>.Instance)
                .ResolveCoverForEncodeAsync(audiobook, originals, Scratch, Guid.NewGuid(), _settings, services, CancellationToken.None);
        }

        private static async Task FfmpegAsync(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = EncoderFactAttribute.FindOnPath("ffmpeg")!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await stderr);
        }

        private async Task<string> GivenImageAsync(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await FfmpegAsync("-f", "lavfi", "-i", "color=red:s=64x64", "-frames:v", "1", path);
            return path;
        }

        private async Task<string> GivenMp3Async(string name, string? embeddedCover = null)
        {
            Directory.CreateDirectory(Book);
            var path = Path.Combine(Book, name);
            if (embeddedCover == null)
            {
                await FfmpegAsync("-f", "lavfi", "-i", "sine=duration=2", "-c:a", "libmp3lame", path);
            }
            else
            {
                await FfmpegAsync(
                    "-f", "lavfi", "-i", "sine=duration=2", "-i", embeddedCover,
                    "-map", "0:a", "-map", "1:v", "-c:a", "libmp3lame", "-c:v", "copy",
                    "-id3v2_version", "3", "-disposition:v", "attached_pic", path);
            }

            return path;
        }

        [EncoderFact]
        public async Task TheBooksOwnArt_IsTakenFromItsFiles()
        {
            var art = await GivenImageAsync(Path.Combine(_work, "art.jpg"));
            var source = await GivenMp3Async("01.mp3", embeddedCover: art);
            // A folder image is there too; the book's own art still wins.
            await GivenImageAsync(Path.Combine(Book, "cover.png"));

            var cover = await ResolveAsync(source);

            Assert.NotNull(cover);
            Assert.True(cover!.IsScratch);
            Assert.StartsWith(Scratch, cover.Path);
            Assert.Equal(".jpg", Path.GetExtension(cover.Path));
            Assert.Equal(await File.ReadAllBytesAsync(art), await File.ReadAllBytesAsync(cover.Path));
        }

        [EncoderFact]
        public async Task AFileWithoutArt_FallsBackToAnImageInTheFolder()
        {
            var source = await GivenMp3Async("01.mp3");
            await GivenImageAsync(Path.Combine(Book, "Zero.jpg"));

            var cover = await ResolveAsync(source);

            Assert.Equal(Path.Combine(Book, "Zero.jpg"), cover?.Path);
            Assert.False(cover!.IsScratch);
        }

        [EncoderFact]
        public async Task TheFolderIsNotSearched_WhenEmbeddingCoversIsOff()
        {
            _settings.EmbedCoverArtInTags = false;
            var source = await GivenMp3Async("01.mp3");
            await GivenImageAsync(Path.Combine(Book, "Zero.jpg"));

            Assert.Null(await ResolveAsync(source));
        }

        [Fact]
        public void TheFolderCover_PrefersACoverNamedSo_OverTheLargestImage()
        {
            Directory.CreateDirectory(Book);
            File.WriteAllBytes(Path.Combine(Book, "back.jpg"), new byte[5000]);
            File.WriteAllBytes(Path.Combine(Book, "folder.jpg"), new byte[10]);
            File.WriteAllText(Path.Combine(Book, "notes.txt"), "not an image");
            var audiobook = new AudiobookBuilder().WithBasePath(Book).Build();

            Assert.Equal(Path.Combine(Book, "folder.jpg"), ConversionJobProcessor.FindFolderCover(audiobook, []));
        }

        public override async Task DisposeAsync()
        {
            try
            {
                Directory.Delete(_work, recursive: true);
            }
            catch (IOException)
            {
            }

            await base.DisposeAsync();
        }
    }
}
