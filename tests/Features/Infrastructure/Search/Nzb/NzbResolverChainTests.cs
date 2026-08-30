using Listenarr.Application.Search.Nzb;
using Listenarr.Infrastructure.Search.Nzb;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Search.Nzb;

/// <summary>
/// The ordering and reporting behaviour of the resolver chain.
/// </summary>
[Trait("Name", "NzbResolverChainTests")]
[Trait("Category", "NzbResolvers")]
public sealed class NzbResolverChainTests : BaseTests
{
    private sealed class StubResolver(string name, int order, NzbResolverResult result) : INzbResolver
    {
        public string Name => name;
        public int Order => order;
        public int Calls { get; private set; }

        public Task<NzbResolverResult> ResolveAsync(string searchString, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static NzbResolverChain Chain(params INzbResolver[] resolvers) =>
        new(resolvers, Mock.Of<ILogger<NzbResolverChain>>());

    [Fact]
    public async Task TheFreeIndexIsAskedBeforeTheMeteredOne()
    {
        // The metered index must never be spent on a release a free one already has.
        var free = new StubResolver("Free", 10,
            NzbResolverResult.Found("Free", "https://example.test/a.nzb", []));
        var metered = new StubResolver("Metered", 30,
            NzbResolverResult.Found("Metered", "https://example.test/b.nzb", []));

        var resolution = await Chain(metered, free).ResolveAsync("token");

        Assert.True(resolution.Succeeded);
        Assert.Equal("Free", resolution.ResolvedBy);
        Assert.Equal(1, free.Calls);
        Assert.Equal(0, metered.Calls);
    }

    [Fact]
    public async Task EveryAnswerIsRecordedEvenWhenOneSucceeds()
    {
        var first = new StubResolver("First", 10,
            NzbResolverResult.Failed("First", NzbResolutionFailure.NotIndexed, "nothing"));
        var second = new StubResolver("Second", 20,
            NzbResolverResult.Found("Second", "https://example.test/a.nzb", []));

        var resolution = await Chain(first, second).ResolveAsync("token");

        // "Nothing was found" is not actionable; which index said what is.
        Assert.Equal(2, resolution.Attempts.Count);
        Assert.Equal("First", resolution.Attempts[0].Resolver);
        Assert.Equal(NzbResolutionFailure.NotIndexed, resolution.Attempts[0].Failure);
    }

    [Fact]
    public async Task AnIncompleteOnlyResultIsWorthRetrying()
    {
        // The release exists and may finish propagating, which is a different situation
        // from nothing having it — and needs a different message.
        var resolver = new StubResolver("Index", 10,
            NzbResolverResult.Failed("Index", NzbResolutionFailure.OnlyIncomplete, "missing parts"));

        var resolution = await Chain(resolver).ResolveAsync("token");

        Assert.False(resolution.Succeeded);
        Assert.True(resolution.WorthRetrying);
    }

    [Fact]
    public async Task NothingIndexedAnywhereIsNotWorthRetrying()
    {
        var resolver = new StubResolver("Index", 10,
            NzbResolverResult.Failed("Index", NzbResolutionFailure.NotIndexed, "nothing"));

        var resolution = await Chain(resolver).ResolveAsync("token");

        Assert.False(resolution.WorthRetrying);
    }

    [Fact]
    public async Task AnAbsentSearchStringAsksNobody()
    {
        var resolver = new StubResolver("Index", 10,
            NzbResolverResult.Found("Index", "https://example.test/a.nzb", []));

        var resolution = await Chain(resolver).ResolveAsync(null);

        Assert.False(resolution.Succeeded);
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(NzbResolutionFailure.NothingToResolve, resolution.Attempts[0].Failure);
    }

    [Fact]
    public void IncompleteCandidatesAreRejectedRatherThanDownloaded()
    {
        var candidates = new List<NzbCandidate>
        {
            new("a", "part one", SizeBytes: 100, Complete: false)
        };

        var result = NzbResolverChain.SelectBest("Index", candidates, id => $"https://example.test/{id}");

        Assert.False(result.Succeeded);
        Assert.Equal(NzbResolutionFailure.OnlyIncomplete, result.Failure);
    }

    [Fact]
    public void TheLargestCompleteCandidateIsChosen()
    {
        // A split release lists its fragments alongside the collection; the collection is
        // the one worth taking.
        var candidates = new List<NzbCandidate>
        {
            new("small", "part", SizeBytes: 100, Complete: true),
            new("big", "collection", SizeBytes: 9000, Complete: true),
            new("broken", "collection", SizeBytes: 99000, Complete: false)
        };

        var result = NzbResolverChain.SelectBest("Index", candidates, id => $"https://example.test/{id}");

        Assert.True(result.Succeeded);
        Assert.Equal("https://example.test/big", result.NzbUrl);
    }

    [Fact]
    public void ACandidateWithNoCompletenessFlagIsAllowed()
    {
        // Not every index reports completeness. Absent must mean "did not say", not "no".
        var candidates = new List<NzbCandidate> { new("a", "thing", SizeBytes: 100) };

        var result = NzbResolverChain.SelectBest("Index", candidates, id => $"https://example.test/{id}");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void BinsearchCombinesEveryCompletePartIntoOneNzb()
    {
        // Taking a single hit would fetch a fragment of a multi-part release.
        var candidates = new List<NzbCandidate>
        {
            new("aWQx", "part 1", Complete: true),
            new("aWQy", "part 2", Complete: true),
            new("aWQz", "broken", Complete: false)
        };

        var url = BinsearchResolver.BuildNzbUrl(candidates, "the token");

        Assert.Contains("aWQx=on", url);
        Assert.Contains("aWQy=on", url);
        Assert.DoesNotContain("aWQz", url);
        Assert.Contains("q=the%20token", url);
    }
}
