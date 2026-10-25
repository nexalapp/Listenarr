using Listenarr.Application.Search.NzbKing;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.NzbKing;

/// <summary>
/// Which of several keys pays for a request.
///
/// NZBKing issues one key per CAPTCHA a person solves, each with its own allowance, so a
/// pool is several allowances and the choice between them is not arbitrary: a key at its
/// cap earns nothing, so the pool only regenerates for the keys below theirs.
/// </summary>
[Trait("Name", "NzbKingKeyPoolTests")]
[Trait("Category", "NzbKingKeyPool")]
public sealed class NzbKingKeyPoolTests : BaseTests
{
    private static NzbKingPoolMember Key(string fingerprint, int balance, bool deleted = false)
        => new(fingerprint, balance, deleted);

    [Fact]
    public void TheFullestKeyPaysFirst()
    {
        var order = NzbKingKeyPool.SpendOrder([Key("b", 40), Key("a", 90), Key("c", 70)]);

        Assert.Equal(["a", "c", "b"], order.Select(member => member.Fingerprint));
    }

    [Fact]
    public void ADeletedKeySortsLastRatherThanBeingDropped()
    {
        // It cannot be spent, but attempting it is what writes the ledger row and what
        // tells an operator the pool is gone rather than merely empty.
        var order = NzbKingKeyPool.SpendOrder([Key("dead", 100, deleted: true), Key("live", 6)]);

        Assert.Equal(["live", "dead"], order.Select(member => member.Fingerprint));
    }

    [Fact]
    public void EqualKeysAreOrderedDeterministically()
    {
        // Otherwise the order follows dictionary enumeration and a pool of equal keys
        // spends from whichever one happens to come out first.
        var first = NzbKingKeyPool.SpendOrder([Key("c", 100), Key("a", 100), Key("b", 100)]);
        var again = NzbKingKeyPool.SpendOrder([Key("b", 100), Key("c", 100), Key("a", 100)]);

        Assert.Equal(first.Select(member => member.Fingerprint), again.Select(member => member.Fingerprint));
    }

    [Fact]
    public void SpendingFromTheFullestKeyKeepsThePoolLevel()
    {
        // The reason for the rule. Refills are forfeit at the cap, so the pool earns one
        // token an hour for each key *below* its cap: a policy that drains one key and
        // parks the rest at 100 buys a burst and no throughput. Always taking the maximum
        // equalises by itself, which keeps every key earning.
        var balances = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a"] = 100,
            ["b"] = 100,
            ["c"] = 100
        };

        for (var spend = 0; spend < 30; spend++)
        {
            var next = NzbKingKeyPool.SpendOrder(
                balances.Select(entry => Key(entry.Key, entry.Value))).First();

            balances[next.Fingerprint] -= 1;
        }

        Assert.Equal(90, balances["a"]);
        Assert.Equal(90, balances["b"]);
        Assert.Equal(90, balances["c"]);
        Assert.All(balances.Values, balance => Assert.True(balance < NzbKingTokenPolicy.MaxTokens));
    }

    [Fact]
    public void AnEmptyPoolOrdersToNothing()
    {
        Assert.Empty(NzbKingKeyPool.SpendOrder([]));
    }
}
