using System.Globalization;

namespace Listenarr.Application.Downloads.Import;

public partial class DownloadImportService
{
    private static AudioMetadata BuildNamingMetadata(
        Audiobook? audiobook,
        AudioMetadata? extractedMetadata,
        string fallbackTitle,
        IReadOnlyDictionary<string, int>? seriesPositionWidths = null)
    {
        if (audiobook != null)
        {
            var author = audiobook.Authors is { Count: > 0 }
                ? string.Join(", ", audiobook.Authors)
                : FirstNonEmpty(
                    ChooseAuthorFromMetadata(extractedMetadata),
                    "Unknown Author");

            return new AudioMetadata
            {
                SeriesPositionWidths = seriesPositionWidths,
                Title = FirstNonEmpty(
                    audiobook.Title,
                    extractedMetadata?.Title,
                    fallbackTitle,
                    "Unknown Title"),
                Subtitle = FirstNonEmpty(
                    audiobook.Subtitle,
                    extractedMetadata?.Subtitle),
                Edition = FirstNonEmpty(
                    audiobook.Edition,
                    extractedMetadata?.Edition),
                Artist = author,
                AlbumArtist = author,
                Album = FirstNonEmpty(
                    extractedMetadata?.Album,
                    audiobook.Title,
                    fallbackTitle),
                Narrator = audiobook.Narrators is { Count: > 0 }
                    ? string.Join(", ", audiobook.Narrators.Where(
                        narrator => !string.IsNullOrWhiteSpace(narrator)))
                    : extractedMetadata?.Narrator,
                Publisher = FirstNonEmpty(
                    audiobook.Publisher,
                    extractedMetadata?.Publisher),
                Language = FirstNonEmpty(
                    audiobook.Language,
                    extractedMetadata?.Language),
                Asin = FirstNonEmpty(
                    audiobook.Asin,
                    extractedMetadata?.Asin),
                Series = FirstNonEmpty(
                    audiobook.Series,
                    extractedMetadata?.Series),
                // Parsed with InvariantCulture: the source value always uses '.' as the
                // decimal separator, so parsing under the server's culture would read a
                // position of "1.5" as 15 wherever '.' is the group separator.
                SeriesPosition = !string.IsNullOrWhiteSpace(audiobook.SeriesNumber)
                    && decimal.TryParse(audiobook.SeriesNumber, NumberStyles.Number, CultureInfo.InvariantCulture, out var seriesPosition)
                        ? seriesPosition
                        : extractedMetadata?.SeriesPosition,
                SeriesPositionRaw = FirstNonEmpty(
                    audiobook.SeriesNumber,
                    extractedMetadata?.SeriesPositionRaw),
                Year = !string.IsNullOrWhiteSpace(audiobook.PublishYear)
                    && int.TryParse(audiobook.PublishYear, out var year)
                        ? year
                        : extractedMetadata?.Year,
                TrackNumber = extractedMetadata?.TrackNumber,
                DiscNumber = extractedMetadata?.DiscNumber,
                BitRate = extractedMetadata?.BitRate,
                Format = extractedMetadata?.Format
            };
        }

        if (extractedMetadata != null)
        {
            if (string.IsNullOrWhiteSpace(extractedMetadata.Title))
            {
                extractedMetadata.Title = fallbackTitle;
            }

            if (string.IsNullOrWhiteSpace(extractedMetadata.Artist))
            {
                extractedMetadata.Artist = FirstNonEmpty(
                    ChooseAuthorFromMetadata(extractedMetadata),
                    "Unknown Author");
            }

            if (string.IsNullOrWhiteSpace(extractedMetadata.AlbumArtist))
            {
                extractedMetadata.AlbumArtist = extractedMetadata.Artist;
            }

            return extractedMetadata;
        }

        return new AudioMetadata
        {
            SeriesPositionWidths = seriesPositionWidths,
            Title = fallbackTitle,
            Artist = "Unknown Author",
            AlbumArtist = "Unknown Author"
        };
    }

    /// <summary>
    /// The {SeriesNumber} token for a file being imported.
    /// <para>
    /// Prefers the position exactly as the source gave it. A real but non-numeric position
    /// (an omnibus at "1-4") does not survive the decimal parse, and falling through to the
    /// chapter number would write that into the filename as if it were the series number.
    /// </para>
    /// <para>
    /// A parsed position is formatted with InvariantCulture, matching FileNamingService:
    /// ToString() under the server's culture would put a comma into the filename.
    /// </para>
    /// </summary>
    /// <summary>
    /// The naming variables for one imported file.
    /// </summary>
    /// <remarks>
    /// Beside the rest of the naming code rather than inline in the import flow, because
    /// what a token resolves to is a naming decision and the import is only its caller.
    /// </remarks>
    private static Dictionary<string, object> BuildFileNamingVariables(
        AudioMetadata namingMetadata,
        string file,
        int? effectiveDiskNumber,
        int? effectiveChapterNumber) =>
        new Dictionary<string, object>
        {
            { "Author", namingMetadata.Artist ?? "Unknown Author" },
            { "Series", string.IsNullOrWhiteSpace(namingMetadata.Series) ? string.Empty : namingMetadata.Series },
            { "Title", namingMetadata.Title ?? Path.GetFileNameWithoutExtension(file) },
            { "Subtitle", string.IsNullOrWhiteSpace(namingMetadata.Subtitle) ? string.Empty : namingMetadata.Subtitle },
            { "Edition", string.IsNullOrWhiteSpace(namingMetadata.Edition) ? string.Empty : namingMetadata.Edition },
            { "Narrator", string.IsNullOrWhiteSpace(namingMetadata.Narrator) ? string.Empty : namingMetadata.Narrator },
            { "Publisher", string.IsNullOrWhiteSpace(namingMetadata.Publisher) ? string.Empty : namingMetadata.Publisher },
            { "Language", string.IsNullOrWhiteSpace(namingMetadata.Language) ? string.Empty : namingMetadata.Language },
            { "Asin", string.IsNullOrWhiteSpace(namingMetadata.Asin) ? string.Empty : namingMetadata.Asin },
            { "SeriesNumber", SeriesNumberToken(namingMetadata, effectiveChapterNumber) },
            { "Year", namingMetadata.Year?.ToString() ?? string.Empty },
            { "Quality", (namingMetadata.BitRate.HasValue ? $"{namingMetadata.BitRate}kbps" : null) ?? namingMetadata.Format ?? string.Empty },
            { "DiskNumber", effectiveDiskNumber?.ToString() ?? string.Empty },
            { "ChapterNumber", effectiveChapterNumber?.ToString() ?? string.Empty }
        };

    private static string SeriesNumberToken(
        AudioMetadata metadata,
        int? fallbackChapterNumber) =>
        // Widened the same way naming widens it, or an imported book lands unpadded and
        // reads as misorganized the moment it arrives.
        SeriesNumberFormatting.Pad(
            FirstNonEmpty(
                metadata.SeriesPositionRaw,
                metadata.SeriesPosition?.ToString(CultureInfo.InvariantCulture),
                fallbackChapterNumber?.ToString()),
            metadata.SeriesPositionWidthFor(metadata.Series)) ?? string.Empty;

    private static string ChooseAuthorFromMetadata(AudioMetadata? metadata)
    {
        if (metadata == null)
        {
            return string.Empty;
        }

        var primary = NonNarratorAuthorCandidate(metadata.Artist, metadata.Narrator);
        var alternate = NonNarratorAuthorCandidate(
            metadata.AlbumArtist,
            metadata.Narrator);

        if (string.IsNullOrWhiteSpace(primary))
        {
            return alternate;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Title)
            && (primary.Contains(metadata.Title, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(metadata.Series)
                    && string.Equals(
                        primary,
                        metadata.Series,
                        StringComparison.OrdinalIgnoreCase))
                || string.Equals(
                    primary,
                    metadata.Title,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return string.IsNullOrWhiteSpace(alternate) ? primary : alternate;
        }

        return primary;
    }

    private static string NonNarratorAuthorCandidate(
        string? candidate,
        string? narrator)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var trimmedCandidate = candidate.Trim();
        if (!string.IsNullOrWhiteSpace(narrator)
            && string.Equals(
                trimmedCandidate,
                narrator.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return trimmedCandidate;
    }

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))
        ?? string.Empty;
}
