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
using System.Reflection;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

/// <summary>
/// Whether two persisted identities are taken to name the same object.
/// </summary>
/// <remarks>
/// The strings here are the real ones from a library on an unraid FUSE share, taken from
/// the journal of an organize that moved a file and was then refused: same device, same
/// inode, same file — and a <c>name_to_handle_at</c> handle that shfs had rotated in the
/// forty minutes between capturing the evidence and checking it.
/// </remarks>
[Trait("Name", "PersistedObjectIdentityMatchingTests")]
[Trait("Category", "Infrastructure")]
public sealed class PersistedObjectIdentityMatchingTests : BaseTests
{
    private const string RecordedBeforeTheMove =
        "linux-generation:00000000:00000039:0037000005d6fac8:fh:00000081:00000000e6a0900000000000";

    private const string SeenAfterTheMove =
        "linux-generation:00000000:00000039:0037000005d6fac8:fh:00000081:00000000ac00990000000000";

    private static bool SameObject(string expected, string candidate) =>
        (bool)typeof(Listenarr.Infrastructure.FileSystem.PinnedDirectoryCreation)
            .GetMethod(
                "ArePersistedObjectIdentitiesSameObject",
                BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [expected, candidate])!;

    [Fact]
    public void ARotatedHandleOverTheSameDeviceAndInode_IsTheSameObject()
    {
        Assert.True(SameObject(RecordedBeforeTheMove, SeenAfterTheMove));
    }

    [Fact]
    public void ADifferentInode_IsNotTheSameObject()
    {
        // The case the generation suffix was added for still refuses: a different object
        // at the same path is a different inode, and no amount of handle rotation makes
        // these two agree.
        const string otherFile =
            "linux-generation:00000000:00000039:0037000005d6fac9:fh:00000081:00000000e6a0900000000000";

        Assert.False(SameObject(RecordedBeforeTheMove, otherFile));
    }

    [Fact]
    public void ADifferentDevice_IsNotTheSameObject()
    {
        const string otherDevice =
            "linux-generation:00000000:00000037:0037000005d6fac8:fh:00000081:00000000e6a0900000000000";

        Assert.False(SameObject(RecordedBeforeTheMove, otherDevice));
    }

    [Theory]
    [InlineData("")]
    [InlineData("persisted-known-generation")]
    [InlineData("linux-generation:zz:00000039:0037000005d6fac8:fh:00000081:00")]
    [InlineData("linux-generation:00000000:00000039:0037000005d6fac8")]
    public void AnIdentityThisCannotRead_IsNeverTakenAsAMatch(string unreadable)
    {
        // Falling back to device and inode is only safe while both are actually known.
        // Anything unparseable has to refuse rather than be treated as evidence.
        Assert.False(SameObject(RecordedBeforeTheMove, unreadable));
        Assert.False(SameObject(unreadable, RecordedBeforeTheMove));
    }

    [Fact]
    public void TheLegacySpelling_ComparesOnTheSameEvidence()
    {
        // The birth-time spelling carries device and inode in the same three positions.
        const string legacy =
            "linux:00000000:00000039:0037000005d6fac8:0000000068c1a1b2:00000000:fh:00000081:00000000ac00990000000000";

        Assert.True(SameObject(RecordedBeforeTheMove, legacy));
    }
}
