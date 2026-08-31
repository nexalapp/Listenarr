
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
    }
}
