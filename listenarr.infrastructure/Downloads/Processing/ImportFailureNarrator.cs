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
namespace Listenarr.Infrastructure.Downloads.Processing
{
    /// <summary>
    /// Turns a failed import into words an operator can act on.
    ///
    /// These read the same records the processor already has, and exist as their own
    /// type because describing a failure is a separate concern from performing the
    /// work — and because the reasons here are the whole of what the UI can show
    /// about why an import stopped.
    /// </summary>
    internal static class ImportFailureNarrator
    {
        /// <summary>
        /// Summarise what the job actually tried, so a blocked import explains itself
        /// without the operator needing access to the job log.
        /// </summary>
        internal static IEnumerable<string> DescribeAttempts(DownloadProcessingJob job)
        {
            ArgumentNullException.ThrowIfNull(job);

            if (!string.IsNullOrWhiteSpace(job.SourcePath))
            {
                yield return $"Looked for files in {job.SourcePath}";
            }

            if (job.RetryCount > 0)
            {
                yield return $"Gave up after {job.RetryCount} of {job.MaxRetries} attempts";
            }
        }

        /// <summary>
        /// Name what failed, rather than reporting a count and pointing at a log the UI
        /// does not expose.
        /// </summary>
        internal static string DescribeFailedImports(IReadOnlyCollection<ImportResult> results)
        {
            ArgumentNullException.ThrowIfNull(results);

            var reasons = results
                .Where(result => !result.Success && !string.IsNullOrWhiteSpace(result.Message))
                .Select(result => result.Message!.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToList();

            var failed = results.Count(result => !result.Success);
            if (reasons.Count == 0)
            {
                return $"Unable to import {failed} of {results.Count} file(s); the import reported no reason";
            }

            return $"Unable to import {failed} of {results.Count} file(s): {string.Join("; ", reasons)}";
        }
    }
}
