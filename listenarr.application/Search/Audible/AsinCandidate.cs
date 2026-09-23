
namespace Listenarr.Application.Search.Audible
{
    /// <summary>
    /// Contains collected ASIN candidates and associated metadata.
    /// </summary>
    public class AsinCandidateCollection
    {
        public List<string> AsinCandidates { get; } = new List<string>();
        public Dictionary<string, (string Title, string Author, string? ImageUrl, string? Language)> AsinToRawResult { get; } = new Dictionary<string, (string, string, string?, string?)>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> AsinToSource { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, OpenLibraryBook> AsinToOpenLibrary { get; } = new Dictionary<string, OpenLibraryBook>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Editions found somewhere that does not use ASINs - OpenLibrary, a library
        /// lending catalogue - which are offered as candidates in their own right.
        /// </summary>
        public List<SearchResult> CatalogueDerivedResults { get; } = new List<SearchResult>();
    }
}
