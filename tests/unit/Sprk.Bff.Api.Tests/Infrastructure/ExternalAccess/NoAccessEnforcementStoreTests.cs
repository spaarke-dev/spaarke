// unified-access-control-r2 task 143 r1 — the No Access enforcement store's two multi-query reads, driven through their
// wire seams (the store's own internal-virtual query methods; ADR-038: no Mock<HttpMessageHandler>). The production
// chunking and paging loops run.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class NoAccessEnforcementStoreTests
{
    private static readonly Guid Record = Guid.Parse("14301430-0000-4000-8000-0000000000a1");

    // ── Record-scoped re-apply: every referenced organization is asked about (verifier finding 5) ───────────────

    [Fact]
    public async Task CoveringEntries_AskAboutEveryReferencedOrganization_PastTheFirstChunk()
    {
        // 120 referenced organizations — more than two chunks. An entry on the 101st must be found: before r1 the
        // organization list was cut at the first 50 with no signal.
        var orgs = Enumerable.Range(0, 120).Select(_ => Guid.NewGuid()).ToList();
        var store = new RecordingStore();
        var entryOnALateOrganization = Guid.NewGuid();
        store.EntriesByObjectClause[$"_sprk_objectorganization_value eq {orgs[100]}"] = entryOnALateOrganization;

        var (ids, truncated) = await store.ReadActiveEntryIdsCoveringAsync(Record, orgs, max: 100, CancellationToken.None);

        ids.Should().Equal(entryOnALateOrganization);
        truncated.Should().BeFalse();
        store.Filters.Should().HaveCount(3, "120 organizations go out in chunks of 50");
        foreach (var org in orgs)
        {
            store.Filters.Count(f => f.Contains($"_sprk_objectorganization_value eq {org}", StringComparison.Ordinal))
                .Should().Be(1, $"organization {org} is asked about exactly once");
        }

        store.Filters.Count(f => f.Contains($"sprk_objectrecordid eq '{Record}'", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task CoveringEntries_WithNoReferencedOrganization_AskAboutTheRecordOnly()
    {
        var store = new RecordingStore();
        var onRecord = Guid.NewGuid();
        store.EntriesByObjectClause[$"sprk_objectrecordid eq '{Record}'"] = onRecord;

        var (ids, _) = await store.ReadActiveEntryIdsCoveringAsync(Record, Array.Empty<Guid>(), max: 100, CancellationToken.None);

        ids.Should().Equal(onRecord);
        store.Filters.Should().ContainSingle();
    }

    [Fact]
    public async Task CoveringEntries_PastTheMaximum_ReportTruncated_NeverASilentPrefix()
    {
        var orgs = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var store = new RecordingStore();
        foreach (var org in orgs)
        {
            store.EntriesByObjectClause[$"_sprk_objectorganization_value eq {org}"] = Guid.NewGuid();
        }

        var (ids, truncated) = await store.ReadActiveEntryIdsCoveringAsync(Record, orgs, max: 2, CancellationToken.None);

        ids.Should().HaveCount(2);
        truncated.Should().BeTrue();
    }

    // ── The job's scan: every page is read (verifier finding 7) ─────────────────────────────────────────────────

    [Fact]
    public async Task ActiveEntryScan_FollowsEveryNextLink()
    {
        var pages = new[]
        {
            Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList(),
            Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList(),
            Enumerable.Range(0, 2).Select(_ => Guid.NewGuid()).ToList(),
        };
        var store = new RecordingStore { Pages = pages };

        var (ids, truncated) = await store.ReadActiveEntryIdsAsync(max: 100, CancellationToken.None);

        ids.Should().Equal(pages.SelectMany(p => p));
        truncated.Should().BeFalse();
        store.PageRequests.Should().Equal(NoAccessEnforcementStore.ActiveEntryScanPath, "next-1", "next-2");
    }

    [Fact]
    public async Task ActiveEntryScan_PastItsCeiling_ReportsTruncated()
    {
        var store = new RecordingStore
        {
            Pages = new[]
            {
                Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList(),
                Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList(),
            },
        };

        var (ids, truncated) = await store.ReadActiveEntryIdsAsync(max: 4, CancellationToken.None);

        ids.Should().HaveCount(4);
        truncated.Should().BeTrue();
    }

    // ── Where a read is sent: the next-page origin rule (task 143 r2, verifier finding 4) ───────────────────────

    private const string ApiRoot = "https://org.crm.dynamics.com/api/data/v9.2";

    [Fact]
    public void ARelativePath_IsSentUnderTheWebApiRoot()
    {
        NoAccessEnforcementStore.ResolveRequestUrl(ApiRoot, NoAccessEnforcementStore.ActiveEntryScanPath)
            .Should().Be($"{ApiRoot}/{NoAccessEnforcementStore.ActiveEntryScanPath}");
    }

    [Theory]
    [InlineData("https://org.crm.dynamics.com/api/data/v9.2/sprk_noaccessentries?$skiptoken=abc")]
    [InlineData("HTTPS://ORG.CRM.DYNAMICS.COM/api/data/v9.2/sprk_noaccessentries?$skiptoken=abc")]
    public void ANextLinkUnderTheWebApiRoot_IsFollowedAsIs(string nextLink)
    {
        NoAccessEnforcementStore.ResolveRequestUrl(ApiRoot, nextLink).Should().Be(nextLink);
    }

    [Theory]
    [InlineData("https://evil.example.com/api/data/v9.2/sprk_noaccessentries?$skiptoken=abc")] // another host
    [InlineData("https://org.crm.dynamics.com.evil.example.com/api/data/v9.2/sprk_noaccessentries")] // a look-alike host
    [InlineData("https://org.crm.dynamics.com/api/data/v9.1/sprk_noaccessentries?$skiptoken=abc")] // another version
    [InlineData("https://org.crm.dynamics.com/api/data/v9.2.evil/sprk_noaccessentries")] // the root as a mere prefix
    [InlineData("http://org.crm.dynamics.com/api/data/v9.2/sprk_noaccessentries?$skiptoken=abc")] // downgraded scheme
    public void ANextLinkOutsideTheWebApiRoot_IsNeverFollowed_TheReadFailsClosed(string nextLink)
    {
        // The app token rides on every read; a link elsewhere is refused, and the scan that met it fails (the job then
        // records "error" — the entries already read are NOT enforced as a silent prefix).
        var act = () => NoAccessEnforcementStore.ResolveRequestUrl(ApiRoot, nextLink);

        act.Should().Throw<InvalidOperationException>().WithMessage("*outside the Web API root*");
    }

    /// <summary>The PRODUCTION store with only its two wire queries answered from memory.</summary>
    private sealed class RecordingStore : NoAccessEnforcementStore
    {
        public RecordingStore()
            : base(new HttpClient(), configuration: null!, credential: null!, logger: NullLogger<NoAccessEnforcementStore>.Instance)
        {
        }

        /// <summary>An entry id returned by every query whose object filter carries the clause.</summary>
        public Dictionary<string, Guid> EntriesByObjectClause { get; } = new();

        public List<string> Filters { get; } = new();

        public IReadOnlyList<List<Guid>> Pages { get; init; } = Array.Empty<List<Guid>>();

        public List<string> PageRequests { get; } = new();

        internal override Task<IReadOnlyList<Guid>> QueryActiveEntryIdsAsync(string objectFilter, int top, CancellationToken ct)
        {
            Filters.Add(objectFilter);
            var clauses = objectFilter.Split(" or ", StringSplitOptions.TrimEntries);
            IReadOnlyList<Guid> hits = EntriesByObjectClause
                .Where(kv => clauses.Contains(kv.Key))
                .Select(kv => kv.Value)
                .Take(top)
                .ToList();
            return Task.FromResult(hits);
        }

        internal override Task<(IReadOnlyList<Guid> Ids, string? NextLink)> ReadActiveEntryIdPageAsync(
            string pathOrNextLink, CancellationToken ct)
        {
            PageRequests.Add(pathOrNextLink);
            var index = PageRequests.Count - 1;
            var next = index + 1 < Pages.Count ? $"next-{index + 1}" : null;
            return Task.FromResult<(IReadOnlyList<Guid>, string?)>((Pages[index], next));
        }
    }
}
