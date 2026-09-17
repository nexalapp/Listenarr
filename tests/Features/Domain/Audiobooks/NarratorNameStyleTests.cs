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
using System.Text;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks
{
    [Trait("Name", "NarratorNameStyleTests")]
    [Trait("Category", "Domain")]
    public sealed class NarratorNameStyleTests : BaseTests
    {
        [Fact]
        public void Render_ZeroNamesEveryNarrator()
        {
            Assert.Equal("A, B, C", NarratorNameStyle.Render("A, B, C", 0));
        }

        [Fact]
        public void Render_CapsTheListAndSaysSo()
        {
            Assert.Equal("A, B et al.", NarratorNameStyle.Render("A, B, C", 2));
            Assert.Equal("A, B, C", NarratorNameStyle.Render("A, B, C", 3));
        }

        /// <summary>
        /// The real case: a full-cast filename that Linux cannot create. Narrators are
        /// dropped from the end until it fits, and the extension survives.
        /// </summary>
        [Fact]
        public void FitComponent_DropsNarratorsUntilTheNameFits()
        {
            var cast = string.Join(", ", Enumerable.Range(1, 15).Select(i => $"Narrator Number {i:00}"));
            var name = $"J.K. Rowling - [Harry Potter 1] Harry Potter and the Sorcerer’s Stone (Full-Cast Edition) {{{cast}}} (2025).m4b";
            Assert.True(Encoding.UTF8.GetByteCount(name) > NarratorNameStyle.MaxComponentBytes);

            var fitted = NarratorNameStyle.FitComponent(name, ".m4b");

            Assert.True(Encoding.UTF8.GetByteCount(fitted) <= NarratorNameStyle.MaxComponentBytes);
            Assert.EndsWith(" et al.} (2025).m4b", fitted);
            Assert.StartsWith("J.K. Rowling - [Harry Potter 1] Harry Potter and the Sorcerer’s Stone (Full-Cast Edition) {Narrator Number 01", fitted);
        }

        [Fact]
        public void FitComponent_LeavesAFittingNameAlone()
        {
            var name = "Arthur C. Clarke - Earthlight {Brian Holsopple} (2012).m4b";
            Assert.Same(name, NarratorNameStyle.FitComponent(name, ".m4b"));
        }

        [Fact]
        public void FitComponent_CutsTextWhenThereIsNoListToShorten()
        {
            var name = new string('é', 200) + ".m4b";
            var fitted = NarratorNameStyle.FitComponent(name, ".m4b");
            Assert.True(Encoding.UTF8.GetByteCount(fitted) <= NarratorNameStyle.MaxComponentBytes);
            Assert.EndsWith(".m4b", fitted);
        }
    }
}
