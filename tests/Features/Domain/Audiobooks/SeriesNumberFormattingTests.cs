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

namespace Listenarr.Tests.Features.Domain.Audiobooks;

/// <summary>
/// Widening a series position so a plain string sort keeps a series in reading order.
/// </summary>
[Trait("Name", "SeriesNumberFormattingTests")]
[Trait("Category", "Naming")]
public sealed class SeriesNumberFormattingTests : BaseTests
{
    [Fact]
    public void ASeriesThatReachesTen_WidensEveryPositionInIt()
    {
        // Shadows of the Apt, which is what this was built for: ten books, so book one
        // has to be written 01 or a folder listing puts book 10 second.
        var positions = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
        var width = SeriesNumberFormatting.WidthFor(positions);

        var sorted = positions
            .Select(position => SeriesNumberFormatting.Pad(position, width)!)
            .OrderBy(padded => padded, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, width);
        Assert.Equal(
            ["01", "02", "03", "04", "05", "06", "07", "08", "09", "10"],
            sorted);
    }

    [Fact]
    public void ASeriesThatDoesNot_IsLeftAlone()
    {
        // A trilogy reads correctly already, and widening it would propose spurious
        // renames across a library for no gain.
        var positions = new[] { "1", "2", "3" };
        var width = SeriesNumberFormatting.WidthFor(positions);

        Assert.Equal(1, width);
        Assert.Equal("1", SeriesNumberFormatting.Pad("1", width));
    }

    [Theory]
    // Only the leading run of digits moves, so everything after it survives intact.
    [InlineData("1.5", 2, "01.5")]
    [InlineData("1-3", 2, "01-3")]
    [InlineData("0.1 - 0.5", 2, "00.1 - 0.5")]
    [InlineData("10", 2, "10")]
    [InlineData("100", 2, "100")]
    public void APositionThatIsNotAWholeNumber_KeepsEverythingAfterItsLeadingDigits(
        string position,
        int width,
        string expected)
    {
        Assert.Equal(expected, SeriesNumberFormatting.Pad(position, width));
    }

    [Fact]
    public void ANovella_SortsBesideTheBookItFollows()
    {
        // The reason only the leading run is padded: 1.5 has to land between 01 and 02,
        // not between 09 and 10 where an unpadded "1.5" would go.
        var positions = new[] { "1", "1.5", "2", "9", "10" };
        var width = SeriesNumberFormatting.WidthFor(positions);

        var sorted = positions
            .Select(position => SeriesNumberFormatting.Pad(position, width)!)
            .OrderBy(padded => padded, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["01", "01.5", "02", "09", "10"], sorted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Book One")]
    public void APositionWithNoLeadingNumber_IsReturnedAsItCame(string? position)
    {
        Assert.Equal(position, SeriesNumberFormatting.Pad(position, 3));
    }

    [Fact]
    public void TheSeriesKey_IgnoresCasingAndSurroundingSpace()
    {
        Assert.Equal(
            SeriesNumberFormatting.SeriesKey("Shadows of the Apt"),
            SeriesNumberFormatting.SeriesKey("  shadows of the APT  "));
    }

    /// <summary>
    /// A novella at 2.6 makes the whole numbers around it read 1.0, 2.0, 3.0, so the
    /// positions line up. A series with no novella stays plain, and a range is never
    /// given a decimal because "1-3.0" would say something else.
    /// </summary>
    [Fact]
    public void Style_GivesWholeNumbersADecimal_OnlyWhenTheSeriesHasOne()
    {
        var mixed = SeriesPositionStyle.For(["1", "1.5", "2", "2.6", "3", "1-3"]);
        Assert.Equal(new SeriesPositionStyle(1, true), mixed);
        Assert.Equal("1.0", SeriesNumberFormatting.Pad("1", mixed));
        Assert.Equal("2.6", SeriesNumberFormatting.Pad("2.6", mixed));
        Assert.Equal("1-3", SeriesNumberFormatting.Pad("1-3", mixed));

        var plain = SeriesPositionStyle.For(["1", "2", "3"]);
        Assert.Equal(SeriesPositionStyle.Plain, plain);
        Assert.Equal("2", SeriesNumberFormatting.Pad("2", plain));
    }

    [Fact]
    public void Style_WidensAndDecimalisesTogether()
    {
        var style = SeriesPositionStyle.For(["1", "7.5", "12"]);
        Assert.Equal(new SeriesPositionStyle(2, true), style);
        Assert.Equal("01.0", SeriesNumberFormatting.Pad("1", style));
        Assert.Equal("07.5", SeriesNumberFormatting.Pad("7.5", style));
        Assert.Equal("12.0", SeriesNumberFormatting.Pad("12", style));
    }

    [Fact]
    public void Style_GrowsOnePositionAtATime()
    {
        var style = SeriesPositionStyle.Plain.Widen("10").Widen("2.5");
        Assert.Equal(new SeriesPositionStyle(2, true), style);
    }
}
