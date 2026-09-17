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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks
{
    [Trait("Name", "AuthorAliasesTests")]
    [Trait("Category", "Domain")]
    public sealed class AuthorAliasesTests : BaseTests
    {
        private static readonly IReadOnlyList<AuthorAlias> Aliases =
            AuthorAliases.Parse("""[{"variant":"B.V. Larson","canonical":"B. V. Larson"},{"variant":"刘慈欣","canonical":"Cixin Liu"}]""");

        [Fact]
        public void Apply_ReplacesAVariantCaseInsensitively()
        {
            var result = AuthorAliases.Apply(["b.v. larson", "Gentry Lee"], Aliases);

            Assert.Equal(["B. V. Larson", "Gentry Lee"], result);
        }

        [Fact]
        public void Apply_CollapsesADuplicateTheReplacementProduces()
        {
            var result = AuthorAliases.Apply(["B. V. Larson", "B.V. Larson"], Aliases);

            Assert.Equal(["B. V. Larson"], result);
        }

        /// <summary>
        /// Same instance back when nothing matched, so a save path can skip the write.
        /// </summary>
        [Fact]
        public void Apply_ReturnsTheSameListWhenNothingChanges()
        {
            var authors = new List<string> { "Iain M. Banks" };

            Assert.Same(authors, AuthorAliases.Apply(authors, Aliases));
            Assert.Same(authors, AuthorAliases.Apply(authors, []));
        }

        [Fact]
        public void Parse_DropsBlankEntriesAndBadJson()
        {
            Assert.Empty(AuthorAliases.Parse(null));
            Assert.Empty(AuthorAliases.Parse("not json"));
            Assert.Empty(AuthorAliases.Parse("""[{"variant":"","canonical":"X"}]"""));
            Assert.Single(AuthorAliases.Parse("""[{"variant":" A ","canonical":" B "}]"""), alias => alias is { Variant: "A", Canonical: "B" });
        }
    }
}
