
namespace Listenarr.Application.Downloads.Contracts
{
    /// <summary>
    /// Generates file paths using configured naming patterns
    /// </summary>
    public interface IFileNamingService
    {
        /// <summary>
        /// Apply the configured file naming pattern to generate the final file path
        /// </summary>
        /// <param name="metadata">Audiobook metadata</param>
        /// <param name="originalExtension">File extension (e.g., ".m4b", ".mp3")</param>
        /// <returns>Full file path using the naming pattern</returns>
        Task<string> GenerateFilePathAsync(AudioMetadata metadata, string originalExtension = ".m4b");

        /// <summary>
        /// Apply the configured file naming pattern to generate the final file path with a specific output path
        /// </summary>
        /// <param name="metadata">Audiobook metadata</param>
        /// <param name="outputPath">Specific output path to use</param>
        /// <param name="originalExtension">File extension (e.g., ".m4b", ".mp3")</param>
        /// <returns>Full file path using the naming pattern</returns>
        Task<string> GenerateFilePathAsync(AudioMetadata metadata, string outputPath, string originalExtension = ".m4b");

        /// <summary>
        /// Parse a naming pattern and replace variables with actual values
        /// </summary>
        /// <param name="pattern">The naming pattern template</param>
        /// <param name="variables">Dictionary of variable values</param>
        /// <param name="treatAsFilename">Whether to treat as filename (sanitize invalid chars)</param>
        /// <returns>Final path with variables replaced</returns>
        string ApplyNamingPattern(string pattern, Dictionary<string, object> variables, bool treatAsFilename = false); // FIXME: Should be private
        string ApplyNamingPattern(string pattern, AudioMetadata metadata, bool treatAsFilename = false);
        string ApplyNamingPattern(string pattern, AudibleBookMetadata metadata, bool treatAsFilename = false);

        /// <summary>
        /// Shorten any component of a finished path that is over the filesystem's
        /// 255-byte name limit, keeping the extension. A pattern's own output is
        /// already fitted, but a sequence suffix or extension appended afterwards can
        /// push a fitted name back over; call this last, on the whole path.
        /// </summary>
        string EnsurePathWithinLimits(string fullPath);

        /// <summary>
        /// Render one metadata tag's value from its configured pattern.
        /// </summary>
        /// <remarks>
        /// The same template language and the same empty-token collapse as the naming
        /// patterns, so an album tag can mirror the folder name without a second syntax.
        /// What differs is the output: a tag keeps colons, slashes and — for a blurb —
        /// paragraph breaks, none of which survive a path component. Returns an empty
        /// string when the pattern holds tokens and every one of them resolved empty.
        /// </remarks>
        string RenderTagValue(string pattern, AudioMetadata metadata);

        /// <summary>
        /// A series name as it is written into a path or a tag: with the configured
        /// trailing words ("Series", "Trilogy", …) dropped. Every builder of a
        /// <c>{Series}</c> token goes through this so folders and album tags agree.
        /// </summary>
        string RenderSeriesName(string? name);

        /// <summary>
        /// The author a book files under, for <c>{Author}</c>: its series' first author
        /// when that setting is on and the series is known, otherwise the first of its own.
        /// </summary>
        string RenderAuthor(string? series, IEnumerable<string>? authors);

        /// <summary>The narrator list as written into a name, capped per the setting.</summary>
        string RenderNarrators(string? narrators);
    }
}
