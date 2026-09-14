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
namespace Listenarr.Tests.Features.Application.Security.Redaction
{
    public class LogRedactionTests
    {
        [Fact]
        public void RedactText_ReplacesSensitiveEnvironmentValues()
        {
            var key = "LISTENARR_API_KEY";
            var secret = "supersecret-TEST-123";
            try
            {
                Environment.SetEnvironmentVariable(key, secret);

                var inputs = new[]
                {
                    $"This is a log line containing the secret: {secret}",
                    $"Multiple {secret} occurrences {secret}"
                };

                foreach (var input in inputs)
                {
                    var redacted = LogRedaction.RedactText(input, LogRedaction.GetSensitiveValuesFromEnvironment());
                    Assert.DoesNotContain(secret, redacted);
                    Assert.Contains("<redacted>", redacted);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        [Fact]
        public void GetSensitiveValuesFromEnvironment_ReturnsSetVariables()
        {
            var key = "LISTENARR_API_KEY";
            var secret = "env-secret-XYZ";
            try
            {
                Environment.SetEnvironmentVariable(key, secret);
                var vals = LogRedaction.GetSensitiveValuesFromEnvironment();
                Assert.Contains(secret, vals);
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        [Fact]
        public void SanitizeDirectoryPath_KeepsTheParentSoTwoLikeNamedFoldersDiffer()
        {
            // The case the helper exists for: a library folder and the folder a file was
            // imported from, both named after the book. Leaf-only redaction renders the
            // two identically, which is what made the log line comparing them useless.
            var library = LogRedaction.SanitizeDirectoryPath(
                Path.Combine("srv", "books", "Roger Zelazny", "Jack of Shadows"));
            var source = LogRedaction.SanitizeDirectoryPath(
                Path.Combine("downloads", "complete", "Jack of Shadows"));

            Assert.Equal("Roger Zelazny/Jack of Shadows", library);
            Assert.Equal("complete/Jack of Shadows", source);
            Assert.NotEqual(library, source);
        }

        [Fact]
        public void SanitizeDirectoryPath_StillHidesEverythingAboveTheParent()
        {
            var sanitized = LogRedaction.SanitizeDirectoryPath(
                Path.Combine("home", "someone", "media", "books", "Author", "Title"));

            Assert.Equal("Author/Title", sanitized);
            Assert.DoesNotContain("someone", sanitized);
        }

        [Fact]
        public void SanitizeDirectoryPath_SurvivesATrailingSeparator()
        {
            // A directory path is routinely stored with one, and GetFileName returns
            // empty for it - which is how the name went missing from the log line.
            var withSeparator = Path.Combine("books", "Author", "Title")
                + Path.DirectorySeparatorChar;

            Assert.Equal("Author/Title", LogRedaction.SanitizeDirectoryPath(withSeparator));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void SanitizeDirectoryPath_ReportsAnEmptyPathRatherThanAnEmptyString(string? path)
        {
            Assert.Equal("[empty-path]", LogRedaction.SanitizeDirectoryPath(path));
        }

        [Fact]
        public void SanitizeDirectoryPath_KeepsALoneSegmentAsItself()
        {
            Assert.Equal("Title", LogRedaction.SanitizeDirectoryPath("Title"));
        }
    }
}
