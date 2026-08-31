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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Tagging
{
    /// <summary>
    /// The single place a tag value is decided, for both tag writing and conversion.
    ///
    /// These cover the four things the planner has to get right: what each mapping
    /// produces, which of those may overwrite what the file already has, what the
    /// operator sees before approving it, and that running it twice is a no-op.
    /// </summary>
    [Trait("Name", "AudiobookTagPlannerTests")]
    [Trait("Category", "Tagging")]
    public class AudiobookTagPlannerTests : BaseTests
    {
        private static AudiobookTagPlanner CreatePlanner() =>
            new(new FileNamingService(
                new Mock<IConfigurationService>().Object,
                new Mock<ILogger<FileNamingService>>().Object));

        private static AudioMetadata Book() => new()
        {
            Title = "Drive",
            Artist = "James S. A. Corey",
            AlbumArtist = "James S. A. Corey",
            Narrator = "Jefferson Mays",
            Genre = "Science Fiction",
            Description = "A short story of the Expanse.",
            Series = "The Expanse",
            SeriesPositionRaw = "2.7",
            AllSeries = [new SeriesReference("The Expanse", "2.7")],
            Asin = "B00A2M2XPO",
            Year = 2012
        };

        private static Dictionary<string, string> Tags(params (string Key, string Value)[] tags) =>
            tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.OrdinalIgnoreCase);

        private static TagChange Change(TagPlan plan, string tag) =>
            plan.Changes.Single(change => change.Tag == tag);

        // ---- what the shipped mapping produces --------------------------------------

        [Fact]
        public void ShippedDefaults_ProduceTheLibrarysOwnAlbumConvention()
        {
            var plan = CreatePlanner().Plan(Book(), TagCatalog.CreateDefaultMappings(), null);

            Assert.Equal("[The Expanse 2.7] Drive", plan.FinalTags[TagCatalog.Album]);
            Assert.Equal("The Expanse 2.7 - Drive", plan.FinalTags[TagCatalog.SortAlbum]);
            Assert.Equal("Drive", plan.FinalTags[TagCatalog.Title]);
            // The narrator lives in composer: MP4 has no narrator atom.
            Assert.Equal("Jefferson Mays", plan.FinalTags[TagCatalog.Composer]);
            Assert.Equal("James S. A. Corey", plan.FinalTags[TagCatalog.Artist]);
            Assert.Equal("The Expanse", plan.FinalTags[TagCatalog.Series]);
            Assert.Equal("2.7", plan.FinalTags[TagCatalog.SeriesPosition]);
            Assert.Equal("2012", plan.FinalTags[TagCatalog.Date]);
        }

        [Fact]
        public void ShippedDefaults_WriteTheDescriptionThatReachesPlex()
        {
            // The desc atom is the whole reason this exists.
            var plan = CreatePlanner().Plan(Book(), TagCatalog.CreateDefaultMappings(), null);

            Assert.Equal(
                "A short story of the Expanse.",
                plan.FinalTags[TagCatalog.Description]);
        }

        [Fact]
        public void TagsWithNothingBehindThem_AreOffByDefault()
        {
            // Listenarr holds no copyright field, and MP4 does not carry language as a
            // book-level tag. Offering them is fine; writing them by default is not.
            var plan = CreatePlanner().Plan(Book(), TagCatalog.CreateDefaultMappings(), null);

            Assert.Equal(TagChangeAction.NotConfigured, Change(plan, TagCatalog.Copyright).Action);
            Assert.Equal(TagChangeAction.NotConfigured, Change(plan, TagCatalog.Language).Action);
            Assert.False(plan.FinalTags.ContainsKey(TagCatalog.Copyright));
        }

        // ---- the overwrite modes -----------------------------------------------------

        [Fact]
        public void Never_LeavesTheFilesValueAlone()
        {
            var plan = CreatePlanner().Plan(
                Book(),
                [new TagMapping(TagCatalog.Album, "{Title}", TagWriteMode.Never)],
                Tags((TagCatalog.Album, "Whatever the release called it")));

            var change = Change(plan, TagCatalog.Album);
            Assert.Equal(TagChangeAction.NotConfigured, change.Action);
            Assert.Equal("Whatever the release called it", plan.FinalTags[TagCatalog.Album]);
        }

        [Fact]
        public void WhenEmpty_KeepsAValueTheFileAlreadyHas()
        {
            var plan = CreatePlanner().Plan(
                Book(),
                [new TagMapping(TagCatalog.Album, "{Title}", TagWriteMode.WhenEmpty)],
                Tags((TagCatalog.Album, "A hand-corrected name")));

            var change = Change(plan, TagCatalog.Album);
            Assert.Equal(TagChangeAction.Preserved, change.Action);
            Assert.Equal("A hand-corrected name", plan.FinalTags[TagCatalog.Album]);
            // The proposal is still reported, so the operator can see what was withheld.
            Assert.Equal("Drive", change.Proposed);
        }

        [Fact]
        public void WhenEmpty_FillsAGap()
        {
            var plan = CreatePlanner().Plan(
                Book(),
                [new TagMapping(TagCatalog.Album, "{Title}", TagWriteMode.WhenEmpty)],
                Tags());

            Assert.Equal(TagChangeAction.Write, Change(plan, TagCatalog.Album).Action);
            Assert.Equal("Drive", plan.FinalTags[TagCatalog.Album]);
        }

        [Fact]
        public void Always_ReplacesWhatTheFileHas()
        {
            var plan = CreatePlanner().Plan(
                Book(),
                [new TagMapping(TagCatalog.Album, "{SeriesBrackets} {Title}", TagWriteMode.Always)],
                Tags((TagCatalog.Album, "Drive")));

            var change = Change(plan, TagCatalog.Album);
            Assert.Equal(TagChangeAction.Write, change.Action);
            Assert.Equal("Will be replaced.", change.Reason);
            Assert.Equal("[The Expanse 2.7] Drive", plan.FinalTags[TagCatalog.Album]);
        }

        [Fact]
        public void APatternThatResolvesToNothing_WritesNothing()
        {
            var book = Book();
            book.Subtitle = null;

            var plan = CreatePlanner().Plan(
                book,
                [new TagMapping(TagCatalog.Subtitle, "{Subtitle}", TagWriteMode.Always)],
                Tags((TagCatalog.Subtitle, "Something the release wrote")));

            var change = Change(plan, TagCatalog.Subtitle);
            Assert.Equal(TagChangeAction.NoValue, change.Action);
            // Absence of a value is never a reason to clear one.
            Assert.Equal("Something the release wrote", plan.FinalTags[TagCatalog.Subtitle]);
        }

        // ---- per-field selection for one run ----------------------------------------

        [Fact]
        public void DeselectedTags_AreLeftAloneWithoutChangingTheMapping()
        {
            var selection = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TagCatalog.Description
            };

            var plan = CreatePlanner().Plan(
                Book(),
                TagCatalog.CreateDefaultMappings(),
                Tags((TagCatalog.Album, "Old album")),
                selection);

            Assert.Equal(TagChangeAction.Write, Change(plan, TagCatalog.Description).Action);
            Assert.Equal(TagChangeAction.Deselected, Change(plan, TagCatalog.Album).Action);
            Assert.Equal("Old album", plan.FinalTags[TagCatalog.Album]);
        }

        // ---- values the operator typed over the proposal ------------------------------

        [Fact]
        public void AnOperatorsValue_ReplacesWhatThePatternWouldProduce()
        {
            // The preview is where a provider's mistake becomes visible. Correcting it
            // for one book must not mean editing the mapping every book shares.
            var plan = CreatePlanner().Plan(
                Book(),
                TagCatalog.CreateDefaultMappings(),
                Tags((TagCatalog.Album, "Drive")),
                selectedTags: null,
                overrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [TagCatalog.Album] = "[The Expanse 0.5] Drive"
                });

            var album = Change(plan, TagCatalog.Album);
            Assert.Equal(TagChangeAction.Write, album.Action);
            Assert.True(album.WasEdited);
            Assert.Equal("[The Expanse 0.5] Drive", plan.FinalTags[TagCatalog.Album]);

            // And only that tag: everything else still comes from its pattern.
            Assert.Equal("The Expanse 2.7 - Drive", plan.FinalTags[TagCatalog.SortAlbum]);
        }

        [Fact]
        public void AnOperatorsValue_WritesATagThisBookHasNothingFor()
        {
            // "No value" is a statement about what Listenarr knows, not about what the
            // operator knows.
            var book = Book();
            book.Subtitle = null;

            var plan = CreatePlanner().Plan(
                book,
                TagCatalog.CreateDefaultMappings(),
                null,
                selectedTags: null,
                overrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [TagCatalog.Subtitle] = "An Expanse Short Story"
                });

            Assert.Equal(TagChangeAction.Write, Change(plan, TagCatalog.Subtitle).Action);
            Assert.Equal("An Expanse Short Story", plan.FinalTags[TagCatalog.Subtitle]);
        }

        [Fact]
        public void AnOperatorsValue_PassesTheWhenEmptyGuard()
        {
            // That guard stops an *automatic* run replacing a hand-corrected value.
            // Somebody typing a replacement is the opposite case.
            var plan = CreatePlanner().Plan(
                Book(),
                [new TagMapping(TagCatalog.Album, "{Title}", TagWriteMode.WhenEmpty)],
                Tags((TagCatalog.Album, "A hand-corrected name")),
                selectedTags: null,
                overrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [TagCatalog.Album] = "A better name"
                });

            Assert.Equal(TagChangeAction.Write, Change(plan, TagCatalog.Album).Action);
            Assert.Equal("A better name", plan.FinalTags[TagCatalog.Album]);
        }

        [Fact]
        public void AnOperatorsValue_DoesNotReverseANeverWriteMapping()
        {
            // A standing decision that this tag is not Listenarr's to touch. A preview is
            // not the place to undo it by accident.
            var plan = CreatePlanner().Plan(
                Book(),
                [new TagMapping(TagCatalog.Copyright, string.Empty, TagWriteMode.Never)],
                Tags((TagCatalog.Copyright, "\u00a92012")),
                selectedTags: null,
                overrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [TagCatalog.Copyright] = "Something else"
                });

            Assert.Equal(TagChangeAction.NotConfigured, Change(plan, TagCatalog.Copyright).Action);
            Assert.Equal("\u00a92012", plan.FinalTags[TagCatalog.Copyright]);
        }

        [Fact]
        public void AnOperatorsValue_MatchingTheFile_IsStillNoChange()
        {
            var plan = CreatePlanner().Plan(
                Book(),
                TagCatalog.CreateDefaultMappings(),
                Tags((TagCatalog.Album, "Whatever they typed")),
                selectedTags: null,
                overrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [TagCatalog.Album] = "Whatever they typed"
                });

            Assert.Equal(TagChangeAction.Unchanged, Change(plan, TagCatalog.Album).Action);
        }

        [Fact]
        public void AnEmptyOperatorValue_FallsBackToThePattern()
        {
            // Clearing the box is not an instruction to clear the tag: nothing here ever
            // writes an empty value, and reading it as "delete this" would be a
            // destructive interpretation of a keystroke.
            var plan = CreatePlanner().Plan(
                Book(),
                TagCatalog.CreateDefaultMappings(),
                null,
                selectedTags: null,
                overrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [TagCatalog.Album] = "   "
                });

            var album = Change(plan, TagCatalog.Album);
            Assert.False(album.WasEdited);
            Assert.Equal("[The Expanse 2.7] Drive", plan.FinalTags[TagCatalog.Album]);
        }

        [Fact]
        public void AnOperatorsValue_IsStrippedOfWhatWouldCorruptTheAtom()
        {
            var plan = CreatePlanner().Plan(
                Book(),
                TagCatalog.CreateDefaultMappings(),
                null,
                selectedTags: null,
                overrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [TagCatalog.Album] = "  Drive\u0000\u0007  "
                });

            Assert.Equal("Drive", plan.FinalTags[TagCatalog.Album]);
        }

        // ---- re-runnable -------------------------------------------------------------

        [Fact]
        public void RunningAgainstItsOwnOutput_ChangesNothing()
        {
            // The acceptance condition for running this on every import: an already
            // correct book must cost a read and no rewrite at all.
            var planner = CreatePlanner();
            var mappings = TagCatalog.CreateDefaultMappings();

            var first = planner.Plan(Book(), mappings, null);
            var second = planner.Plan(Book(), mappings, first.FinalTags);

            Assert.False(second.HasChanges);
            Assert.Equal(first.FinalTags.Count, second.FinalTags.Count);
        }

        [Fact]
        public void TrailingWhitespaceAndCarriageReturns_AreNotChanges()
        {
            // A file round-tripped through another tagger comes back with CRLF where the
            // source had LF. Treating that as a difference would rewrite every file on
            // every run and never converge.
            var plan = CreatePlanner().Plan(
                Book(),
                [new TagMapping(TagCatalog.Description, "{Description}", TagWriteMode.Always)],
                Tags((TagCatalog.Description, "A short story of the Expanse.  \r\n")));

            Assert.Equal(TagChangeAction.Unchanged, Change(plan, TagCatalog.Description).Action);
        }

        // ---- duplicates and unmanaged tags -------------------------------------------

        [Fact]
        public void ManagedTagsCollapseCaseVariants_SoARewriteCannotLeaveTwo()
        {
            // ffprobe reports duplicate SERIES atoms on at least two files in the real
            // library. The final set is keyed case-insensitively and the catalog's casing
            // wins, so the pair becomes one on the first rewrite rather than three on the
            // second.
            var plan = CreatePlanner().Plan(
                Book(),
                TagCatalog.CreateDefaultMappings(),
                Tags(("series", "The Expanse (lowercase copy)")));

            Assert.Equal("The Expanse", plan.FinalTags[TagCatalog.Series]);
            Assert.Single(
                plan.FinalTags,
                tag => string.Equals(tag.Key, TagCatalog.Series, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void UnmanagedTags_SurviveTheRewrite()
        {
            // A release's own totaltracks, or a tagger's custom key, is not ours to drop
            // just because nothing here maps to it.
            var plan = CreatePlanner().Plan(
                Book(),
                TagCatalog.CreateDefaultMappings(),
                Tags(("totaltracks", "37")));

            Assert.Equal("37", plan.FinalTags["totaltracks"]);
        }

        [Fact]
        public void ContainerFacts_AreNotCarriedForwardAsTags()
        {
            // ffprobe reports these under a file's tags, but they belong to the muxer.
            // Writing them back would turn container facts into iTunes freeform atoms.
            var plan = CreatePlanner().Plan(
                Book(),
                TagCatalog.CreateDefaultMappings(),
                Tags(("major_brand", "isom"), ("encoder", "Lavf60.16.100")));

            Assert.False(plan.FinalTags.ContainsKey("major_brand"));
            Assert.False(plan.FinalTags.ContainsKey("encoder"));
        }

        // ---- settings reconciliation -------------------------------------------------

        [Fact]
        public void SettingsWrittenBeforeThisFeature_GetTheShippedDefaults()
        {
            // A null mapping is an install that predates tag writing, not an instruction
            // to write nothing.
            var reconciled = TagCatalog.Reconcile(null);

            Assert.Equal(TagCatalog.Definitions.Count, reconciled.Count);
            Assert.Equal(
                "{SeriesBrackets} {Title}",
                reconciled.Single(mapping => mapping.Tag == TagCatalog.Album).Pattern);
        }

        [Fact]
        public void AStoredMappingKeepsItsEdits_AndGainsTagsAddedSince()
        {
            var stored = new List<TagMapping>
            {
                new(TagCatalog.Album, "{Title}", TagWriteMode.Never)
            };

            var reconciled = TagCatalog.Reconcile(stored);

            var album = reconciled.Single(mapping => mapping.Tag == TagCatalog.Album);
            Assert.Equal("{Title}", album.Pattern);
            Assert.Equal(TagWriteMode.Never, album.Mode);
            Assert.Equal(TagCatalog.Definitions.Count, reconciled.Count);
        }

        [Fact]
        public void AStoredMappingForAnUnknownTag_IsDropped()
        {
            var reconciled = TagCatalog.Reconcile(
                [new TagMapping("something_invented", "{Title}", TagWriteMode.Always)]);

            Assert.DoesNotContain(reconciled, mapping => mapping.Tag == "something_invented");
        }
    }
}
