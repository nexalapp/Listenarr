namespace Listenarr.Application.Metadata.Audible
{
    public class AudibleBookResponse
    {
        public string? Asin { get; set; }
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public List<AudibleAuthor>? Authors { get; set; }
        public List<AudibleNarrator>? Narrators { get; set; }
        public string? Publisher { get; set; }
        public string? PublishDate { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int? LengthMinutes { get; set; }
        public string? Language { get; set; }
        public List<AudibleGenre>? Genres { get; set; }
        public List<AudibleSeries>? Series { get; set; }
        public bool? Explicit { get; set; }
        public string? ReleaseDate { get; set; }
        public string? Isbn { get; set; }
        public string? Region { get; set; }
        public string? BookFormat { get; set; }
        public string? ContentType { get; set; }
        public string? ContentDeliveryType { get; set; }
        public string? EpisodeType { get; set; }
        public string? Sku { get; set; }

        /// <summary>
        /// Listener ratings, from the "rating" response group.
        /// </summary>
        public AudibleRating? Rating { get; set; }

        /// <summary>
        /// Audnexus' own rating, carried here because this type doubles as the shared
        /// carrier for an Audnexus lookup (see <c>AudiobookMetadataService</c>).
        ///
        /// <para>
        /// Kept separate from <see cref="Rating"/> rather than folded into its overall
        /// distribution: Audnexus publishes one rounded number and no count, so a value
        /// parked in an Audible-shaped distribution would claim a precision and a
        /// provenance it does not have.
        /// </para>
        /// </summary>
        public double? AudnexusRating { get; set; }
    }

    /// <summary>
    /// The "rating" response group. Audible scores three things independently — the book
    /// overall, the narration, and the writing — and an audiobook library cares about the
    /// split: it is what separates a good book read badly from the reverse.
    /// </summary>
    public class AudibleRating
    {
        public AudibleRatingDistribution? Overall { get; set; }
        public AudibleRatingDistribution? Performance { get; set; }
        public AudibleRatingDistribution? Story { get; set; }

        /// <summary>
        /// Written reviews, which is a different and much smaller population than the
        /// star ratings in each distribution — 47,698 against 310,988 on B08G9PRS1K at
        /// the time of writing. Not a substitute for
        /// <see cref="AudibleRatingDistribution.NumRatings"/>.
        /// </summary>
        public int? NumReviews { get; set; }
    }

    public class AudibleRatingDistribution
    {
        public double? AverageRating { get; set; }
        public int? NumRatings { get; set; }
    }

    public class AudibleAuthor { public string? Asin { get; set; } public string? Name { get; set; } public string? Region { get; set; } }
    public class AudibleNarrator { public string? Name { get; set; } }
    public class AudibleGenre { public string? Asin { get; set; } public string? Name { get; set; } public string? Type { get; set; } }
    public class AudibleSeries { public string? Asin { get; set; } public string? Name { get; set; } public string? Position { get; set; } }

    public class AudibleSearchResponse { public List<AudibleSearchResult>? Results { get; set; } public int? TotalResults { get; set; } }

    public class AudibleSearchResult
    {
        public string? Asin { get; set; }
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public List<AudibleAuthor>? Authors { get; set; }
        public string? ImageUrl { get; set; }
        // Runtime fields: audible may return different names (runtimeLengthMin, lengthMinutes, runtimeMinutes)
        public int? RuntimeLengthMin { get; set; }
        public int? LengthMinutes { get; set; }
        public int? RuntimeMinutes { get; set; }
        public string? Language { get; set; }
        public string? ContentType { get; set; }
        public string? ContentDeliveryType { get; set; }
        public string? EpisodeType { get; set; }
        public string? Sku { get; set; }
        public string? BookFormat { get; set; }
        public List<AudibleGenre>? Genres { get; set; }
        public List<AudibleSeries>? Series { get; set; }
        public string? Publisher { get; set; }
        public List<AudibleNarrator>? Narrators { get; set; }
        public string? ReleaseDate { get; set; }
        public string? Link { get; set; }
        public string? Isbn { get; set; }
    }

    // Helper types for simple author lookup parsing
    public class AuthorLookupItem { public string? Asin { get; set; } public string? Name { get; set; } public string? Image { get; set; } public string? Region { get; set; } public string? Description { get; set; } }
    public class AuthorLookupEnvelope
    {
        public string? Asin { get; set; }
        public string? Name { get; set; }
        public string? Image { get; set; }
        public string? Region { get; set; }
        public string? Description { get; set; }
        public List<AuthorLookupItem>? Results { get; set; }
    }
    public class SeriesLookupItem
    {
        public string? Asin { get; set; }
        public string? Name { get; set; }
        public string? Region { get; set; }
        public string? Description { get; set; }
        public string? Position { get; set; }
        public string? Image { get; set; }
    }
    public class SeriesLookupEnvelope
    {
        public string? Asin { get; set; }
        public string? Name { get; set; }
        public string? Region { get; set; }
        public string? Description { get; set; }
        public string? Position { get; set; }
        public List<SeriesLookupItem>? Results { get; set; }
    }
    public class AudibleAuthorTileMetadata { public List<AudibleAuthorTileAuthor>? Authors { get; set; } }
    public class AudibleAuthorTileAuthor { public string? Name { get; set; } }
}
