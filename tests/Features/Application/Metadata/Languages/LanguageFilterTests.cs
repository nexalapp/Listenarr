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
using Listenarr.Application.Metadata.Languages;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Metadata.Languages
{
    /// <summary>
    /// The one reading of a language filter that search, catalogs and suggestions share.
    /// </summary>
    [Trait("Name", "LanguageFilterTests")]
    [Trait("Category", "Application")]
    public sealed class LanguageFilterTests : BaseTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("all")]
        [InlineData("english,all")]
        public void Parse_TreatsEmptyAndAllAsNoFilter(string? filter)
        {
            Assert.Null(LanguageFilter.Parse(filter));
            Assert.True(LanguageFilter.Matches(filter, "klingon", acceptUnknown: false));
        }

        [Fact]
        public void Matches_AdmitsEveryLanguageInAList_AndNoOther()
        {
            const string filter = "English, german";

            Assert.True(LanguageFilter.Matches(filter, "english", acceptUnknown: false));
            Assert.True(LanguageFilter.Matches(filter, "German", acceptUnknown: false));
            Assert.False(LanguageFilter.Matches(filter, "italian", acceptUnknown: false));
        }

        [Fact]
        public void Matches_LetsTheCallerDecideAboutAnUnknownLanguage()
        {
            // A catalog listing keeps a book with no language recorded; a strict match
            // - the kind that decides what a file is - does not guess.
            Assert.True(LanguageFilter.Matches("english", null, acceptUnknown: true));
            Assert.False(LanguageFilter.Matches("english", null, acceptUnknown: false));
        }

        [Fact]
        public void FromSettings_PrefersTheLibraryList_ThenTheDefault_ThenNothing()
        {
            var settings = new ApplicationSettings
            {
                DefaultSearchLanguage = "german",
                LibraryLanguagesJson = """["english","french"]"""
            };
            Assert.Equal("english,french", LanguageFilter.FromSettings(settings));

            settings.LibraryLanguagesJson = "[]";
            Assert.Equal("german", LanguageFilter.FromSettings(settings));

            settings.DefaultSearchLanguage = "all";
            Assert.Null(LanguageFilter.FromSettings(settings));
        }
    }
}
