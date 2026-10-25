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
using Listenarr.Application.Audiobooks.AutoGrab;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.AutoGrab
{
    /// <summary>
    /// What may be stored as an auto-grab choice. This is the document a background
    /// worker reads before adding books to someone's library, so what gets in matters
    /// more than it looks.
    /// </summary>
    [Trait("Name", "AutoGrabSettingsJsonTests")]
    [Trait("Category", "Application")]
    public sealed class AutoGrabSettingsJsonTests : BaseTests
    {
        [Fact]
        public void Read_OfNothingStored_IsOff()
        {
            var settings = AutoGrabSettingsJson.Read(null);

            Assert.False(settings.Enabled);
            Assert.Empty(settings.Sources);
            Assert.Equal(AutoGrabSettings.DefaultPerRun, settings.PerRun);
        }

        [Fact]
        public void Read_OfSomethingThatIsNotJson_IsOffRatherThanThrowing()
        {
            Assert.False(AutoGrabSettingsJson.Read("{not json").Enabled);
        }

        [Fact]
        public void WriteThenRead_KeepsTheListsAndTheirOrder()
        {
            var settings = new AutoGrabSettings(true, 25, 30,
            [
                new AutoGrabSource(AutoGrabSources.Series),
                new AutoGrabSource(AutoGrabSources.Discover, "18580606011"),
                new AutoGrabSource(AutoGrabSources.AbookLink, "7", Enabled: false)
            ]);

            var read = AutoGrabSettingsJson.Read(AutoGrabSettingsJson.Write(settings));

            Assert.True(read.Enabled);
            Assert.Equal(25, read.PerRun);
            Assert.Equal(30, read.IntervalMinutes);
            Assert.Equal(
                ["series", "discover:18580606011", "abook:7"],
                read.Sources.Select(source => source.Key));

            // Order is the whole point of the list: it decides whose books a capped run
            // spends its allowance on.
            Assert.Equal(AutoGrabSources.Series, read.Sources[0].Kind);
            Assert.False(read.Sources[2].Enabled);
        }

        [Fact]
        public void Sanitize_DropsAListNobodyOffers()
        {
            var settings = AutoGrabSettingsJson.Sanitize(
                new AutoGrabSettings(true, 10, 60, [new AutoGrabSource("goodreads")]));

            Assert.Empty(settings.Sources);
        }

        [Theory]
        [InlineData("../../etc/passwd")]
        [InlineData("7;rm")]
        [InlineData("")]
        public void Sanitize_DropsASectionThatIsNotAnId(string sectionId)
        {
            var settings = AutoGrabSettingsJson.Sanitize(new AutoGrabSettings(
                true, 10, 60, [new AutoGrabSource(AutoGrabSources.Discover, sectionId)]));

            Assert.Empty(settings.Sources);
        }

        [Fact]
        public void Sanitize_IgnoresASectionOnAListThatHasNone()
        {
            var settings = AutoGrabSettingsJson.Sanitize(new AutoGrabSettings(
                true, 10, 60, [new AutoGrabSource(AutoGrabSources.Authors, "7")]));

            Assert.Null(Assert.Single(settings.Sources).SectionId);
        }

        [Fact]
        public void Sanitize_KeepsOneEntryPerList()
        {
            var settings = AutoGrabSettingsJson.Sanitize(new AutoGrabSettings(true, 10, 60,
            [
                new AutoGrabSource(AutoGrabSources.Discover, "1"),
                new AutoGrabSource(AutoGrabSources.Discover, "1", Enabled: false),
                new AutoGrabSource(AutoGrabSources.Discover, "2")
            ]));

            Assert.Equal(["discover:1", "discover:2"], settings.Sources.Select(source => source.Key));
        }

        [Theory]
        [InlineData(0, AutoGrabSettings.DefaultPerRun)]
        [InlineData(-5, AutoGrabSettings.DefaultPerRun)]
        [InlineData(5, 5)]
        [InlineData(10_000, AutoGrabSettings.MaxPerRun)]
        public void Sanitize_HoldsTheRunSizeInRange(int requested, int expected)
        {
            var settings = AutoGrabSettingsJson.Sanitize(new AutoGrabSettings(true, requested, 60, []));

            // A mistake in what is followed should cost ten books, not nine hundred.
            Assert.Equal(expected, settings.PerRun);
        }

        [Fact]
        public void Active_IsTheTickedListsOnly()
        {
            var settings = new AutoGrabSettings(true, 10, 60,
            [
                new AutoGrabSource(AutoGrabSources.Series),
                new AutoGrabSource(AutoGrabSources.Authors, Enabled: false)
            ]);

            Assert.Equal([AutoGrabSources.Series], settings.Active.Select(source => source.Kind));
        }
    }
}
