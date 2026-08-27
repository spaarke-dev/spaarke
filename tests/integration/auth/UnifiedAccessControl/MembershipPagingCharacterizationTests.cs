using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Membership paging determinism + completeness — finding A-10 (spec FR-14, NFR-03), closed by
/// task 015.
///
/// <para><b>What was wrong.</b> Three defects compounding into one silent under-grant.
/// (i) The membership FetchXml carried no <c>&lt;order&gt;</c>, so which rows a capped or paged
/// query returned was whatever Dataverse felt like — pages could overlap or skip.
/// (ii) <c>top='{limit+1}'</c> was used as a has-more sentinel, but the sentinel row was then
/// DISCARDED while the cursor advanced past it, so exactly one row fell down the crack at every
/// page boundary — and <c>top</c> was illegally mixed with <c>page</c>/<c>count</c> besides.
/// (iii) Both composers called the resolver with <c>options: null</c> and read only
/// <c>response.Ids</c>, throwing away <c>ContinuationToken</c> — so a systemuser on 900 matters
/// was granted 500 and DENIED 400, silently.</para>
///
/// <para><b>Why task 001 could not pin this.</b> A-10 was found by reading code, not by any failing
/// test, and the reason is instructive: the pre-existing membership doubles returned a fixed
/// <c>EntityCollection</c> regardless of the query — they ignored <c>count</c>/<c>page</c> entirely
/// and handed back rows in fixture order. A double like that is MORE DETERMINATE THAN THE PLATFORM:
/// it cannot express "the server picked a different arbitrary page this time", so the ordering defect
/// was invisible to it by construction, and a cap assertion against it could not distinguish "the
/// page filled up" from "there happen to be exactly N rows".</para>
///
/// <para><b>What makes these tests able to fail.</b> <see cref="FetchXmlPagingSimulator"/> is
/// deliberately adversarial rather than accommodating. It <i>throws</i> on anything it was not
/// explicitly taught — a paged query with no <c>&lt;order&gt;</c>, an order on a column it does not
/// hold to be unique, <c>top</c> mixed with <c>page</c>/<c>count</c>, an unknown condition operator,
/// a page-2 request whose paging cookie it never issued. And it orders rows by a total order that
/// is deliberately NOT .NET's <see cref="Guid"/> ordering, mirroring the fact that Dataverse sorts
/// <c>uniqueidentifier</c> under a SQL Server collation which does not match
/// <see cref="Guid.CompareTo(Guid)"/> — so any attempt to re-derive a page boundary client-side
/// breaks here instead of in production.</para>
///
/// <para>Each guard is perturbed on its own: the ordering, the boundary arithmetic, the
/// continuation-following, and the cap flag each have a test that fails if only that one guard
/// regresses.</para>
/// </summary>
public class MembershipPagingCharacterizationTests
{
    private const string EntityType = "sprk_matter";
    private const string PrimaryIdAttribute = "sprk_matterid";

    private static readonly Guid SystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Oid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Tenant = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    private const int PageSize = AccessibleRecordSetService.MembershipPageSize;
    private const int CapLimit = MembershipResolveOptions.MaxLimit;

    // ═════════════════════════════════════════════════════════════════════════════
    // GUARD 1 — the query carries a stable total order (FR-14 acceptance, defect i)
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ✅ FLIPPED BY TASK 015 (FR-14) — the built FetchXml had no <c>&lt;order&gt;</c> at all.
    ///
    /// <para>Asserted on the emitted query, not on returned rows: a test that only checked the
    /// returned ids were sorted would pass against the broken build, because the resolver sorted
    /// its output in memory the whole time. In-memory sorting of an arbitrarily-chosen page does
    /// not make paging deterministic — it makes a non-deterministic subset look tidy.</para>
    /// </summary>
    [Fact]
    public async Task BuildFetchXml_CarriesStableOrderOnThePrimaryId()
    {
        var captured = new List<string>();
        var sut = ResolverWith(SingleRow(NewId(1)), captured);

        await sut.ResolveAsync(SystemUserId, EntityType, options: null, CancellationToken.None);

        captured.Should().ContainSingle();
        var fetch = XElement.Parse(captured[0]);
        var order = fetch.Element("entity")!.Elements("order").ToList();

        order.Should().ContainSingle(
            "A-10: with no <order>, Dataverse is free to return a different arbitrary subset per " +
            "page — pages may overlap or skip rows entirely");
        order[0].Attribute("attribute")!.Value.Should().Be(
            PrimaryIdAttribute,
            "the sort key must be TOTAL and unique; ordering by a non-unique column leaves ties " +
            "free to reorder between pages, which re-opens the same defect");
        order[0].Attribute("descending")?.Value.Should().Be("false");
    }

    /// <summary>
    /// The order element must sit before the filter — FetchXml's schema sequences an entity's
    /// children as attribute*, order*, filter*, link-entity*. A query the platform rejects is a
    /// different bug wearing the same fix.
    /// </summary>
    [Fact]
    public async Task BuildFetchXml_PlacesOrderBeforeFilter()
    {
        var captured = new List<string>();
        var sut = ResolverWith(SingleRow(NewId(1)), captured);

        await sut.ResolveAsync(SystemUserId, EntityType, options: null, CancellationToken.None);

        var children = XElement.Parse(captured[0]).Element("entity")!.Elements().ToList();
        var orderIndex = children.FindIndex(e => e.Name == "order");
        var filterIndex = children.FindIndex(e => e.Name == "filter");

        orderIndex.Should().BeGreaterThan(-1);
        filterIndex.Should().BeGreaterThan(-1);
        orderIndex.Should().BeLessThan(filterIndex, "FetchXml sequences order* before filter*");
    }

    /// <summary>
    /// The transitive (includeRelated) query is single-page, so ordering does not affect
    /// completeness — but without it, <c>top</c> picks an arbitrary MaxLimit-sized subset and the
    /// same request can answer differently twice. Same defect class, second query.
    /// </summary>
    [Fact]
    public async Task BuildTransitiveFetchXml_CarriesStableOrderOnTheRelatedPrimaryId()
    {
        var captured = new List<string>();
        var discovery = DiscoveryMock();
        discovery
            .Setup(d => d.DiscoverLookupsTargetingAsync("sprk_document", EntityType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "sprk_matter" });

        var sut = ResolverWith(SingleRow(NewId(1)), captured, discovery);

        await sut.ResolveAsync(
            SystemUserId,
            EntityType,
            new MembershipResolveOptions(IncludeRelated: new[] { "sprk_document" }),
            CancellationToken.None);

        captured.Should().HaveCount(2, "one primary query plus one transitive query");
        var transitive = XElement.Parse(captured[1]).Element("entity")!;
        transitive.Attribute("name")!.Value.Should().Be("sprk_document");
        transitive.Elements("order").Should().ContainSingle()
            .Which.Attribute("attribute")!.Value.Should().Be("sprk_documentid");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GUARD 2 — no data row is consumed to answer "is there more?" (defect ii)
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ✅ FLIPPED BY TASK 015 (FR-14) — <c>top</c> was emitted alongside <c>page</c>/<c>count</c>,
    /// which is not a paging scheme Dataverse defines. The simulator refuses the combination
    /// outright rather than quietly honouring one of them, because quietly honouring one is
    /// exactly how this survived review.
    /// </summary>
    [Fact]
    public async Task BuildFetchXml_UsesPageCountOnly_NeverTopAsAPagingSentinel()
    {
        var captured = new List<string>();
        var sut = ResolverWith(SingleRow(NewId(1)), captured);

        await sut.ResolveAsync(SystemUserId, EntityType, options: null, CancellationToken.None);

        var fetch = XElement.Parse(captured[0]);
        fetch.Attribute("top").Should().BeNull(
            "top and page/count are mutually exclusive; top was also being used to over-fetch one " +
            "row as a has-more sentinel, and that extra row was then dropped");
        fetch.Attribute("count")!.Value.Should().Be(
            MembershipResolveOptions.DefaultLimit.ToString(CultureInfo.InvariantCulture));
        fetch.Attribute("page")!.Value.Should().Be("1");
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 015 (FR-14) — THE boundary regression, stated as the acceptance criterion:
    /// page N+1 returns no rows skipped by page N.
    ///
    /// <para>The count straddles a page edge by exactly one row, which is the smallest input that
    /// distinguishes the fixed build from the broken one: the old sentinel discarded the
    /// (limit+1)th row of page 1 while the cursor advanced past it, so that single row was served
    /// by no page at all. Asserted as set equality against the source, so a build that lost a row
    /// AND a build that served one twice both fail.</para>
    /// </summary>
    [Fact]
    public async Task Paging_AcrossAStraddlingBoundary_LosesNoRowAndDuplicatesNone()
    {
        var all = Ids(PageSize + 1);
        var simulator = new FetchXmlPagingSimulator(all, PrimaryIdAttribute);
        var sut = ResolverWith(simulator);

        var seen = new List<Guid>();
        string? token = null;
        var pages = 0;

        do
        {
            var page = await sut.ResolveAsync(
                SystemUserId,
                EntityType,
                new MembershipResolveOptions(Limit: PageSize, ContinuationToken: token),
                CancellationToken.None);
            seen.AddRange(page.Ids);
            token = page.ContinuationToken;
            pages++;
        }
        while (token is not null && pages < 10);

        seen.Should().HaveCount(all.Count, "no row may be served twice across page boundaries");
        seen.Should().BeEquivalentTo(all,
            "the (limit+1)th row was dropped by the has-more sentinel and then skipped by the next " +
            "page's cursor — served by no page at all");
    }

    /// <summary>
    /// The exact-multiple case: a full final page must not be mistaken for "there is more, and I
    /// consumed a row to find out". Distinct from the straddling case above, and the case a
    /// sentinel-based implementation gets wrong in the opposite direction.
    /// </summary>
    [Fact]
    public async Task Paging_WhenTotalIsAnExactMultipleOfPageSize_StillReturnsEveryRowExactlyOnce()
    {
        var all = Ids(PageSize * 2);
        var sut = ResolverWith(new FetchXmlPagingSimulator(all, PrimaryIdAttribute));

        var seen = new List<Guid>();
        string? token = null;
        var pages = 0;

        do
        {
            var page = await sut.ResolveAsync(
                SystemUserId,
                EntityType,
                new MembershipResolveOptions(Limit: PageSize, ContinuationToken: token),
                CancellationToken.None);
            seen.AddRange(page.Ids);
            token = page.ContinuationToken;
            pages++;
        }
        while (token is not null && pages < 10);

        seen.Should().HaveCount(all.Count);
        seen.Should().BeEquivalentTo(all);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GUARD 3 — the composers follow the cursor to the end (defect iii)
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ✅ FLIPPED BY TASK 015 (FR-14 acceptance: "a caller with &gt;500 memberships resolves all of
    /// them") — <c>ComposeForSystemUserAsync</c> called the resolver with <c>options: null</c>
    /// (clamping to 500) and read only <c>Ids</c>, discarding the continuation token that the
    /// resolver had already computed for it.
    ///
    /// <para>The membership count is deliberately NOT a multiple of the page size, so a build that
    /// returned a whole number of pages but stopped early still fails.</para>
    /// </summary>
    [Fact]
    public async Task ComposeForSystemUser_WithMoreMembershipsThanOnePage_ResolvesAllOfThem()
    {
        var all = Ids(PageSize + 401); // 901 — straddles two boundaries, multiple of neither
        var simulator = new FetchXmlPagingSimulator(all, PrimaryIdAttribute);
        var sut = ComposerWith(ResolverWith(simulator));

        var set = await sut.ComposeAsync(SystemUserPrincipal(), EntityType, CancellationToken.None);

        set.Count.Should().Be(all.Count,
            "the composer took the first page and denied the rest — a fail-closed under-grant that " +
            "nothing reported");
        set.RecordIds.Should().BeEquivalentTo(all);
        set.Capped.Should().BeFalse("the stream was read to exhaustion, so the set is complete");
        simulator.PagesServed.Should().BeGreaterThan(1, "resolving 901 rows at 500/page must page");
    }

    /// <summary>
    /// The specific denial the finding describes, asserted at the enforcement primitive rather than
    /// on a count: a record beyond the first page is ACCESSIBLE, not denied.
    /// </summary>
    [Fact]
    public async Task IsRecordAccessible_ForARecordBeyondTheFirstPage_GrantsRatherThanDenies()
    {
        var all = Ids(PageSize + 401);
        var simulator = new FetchXmlPagingSimulator(all, PrimaryIdAttribute);

        // Ask the simulator which record it places LAST in ITS OWN order — the one guaranteed to
        // fall on the final page. Picking `all[^1]` instead would be picking by construction order,
        // which the simulator deliberately does not use: that record can land on page 1 by luck,
        // and then this test passes even against a build that only ever reads page 1. (It did,
        // until the single-page perturbation exposed it.)
        var onTheLastPage = simulator.LastRecordInServerOrder;
        all.Should().Contain(onTheLastPage);

        var sut = ComposerWith(ResolverWith(simulator));

        (await sut.IsRecordAccessibleAsync(SystemUserPrincipal(), EntityType, onTheLastPage, CancellationToken.None))
            .Should().BeTrue(
                "membership exists; the record was denied only because composition stopped at 500");
    }

    /// <summary>
    /// A caller who fits inside one page must still cost exactly ONE round trip. The completeness
    /// fix must not tax the overwhelmingly common case — if it did, the honest response would be to
    /// escalate the trade-off rather than absorb it.
    /// </summary>
    [Fact]
    public async Task ComposeForSystemUser_WhenMembershipFitsInOnePage_CostsExactlyOneRoundTrip()
    {
        var all = Ids(PageSize - 1);
        var simulator = new FetchXmlPagingSimulator(all, PrimaryIdAttribute);
        var sut = ComposerWith(ResolverWith(simulator));

        var set = await sut.ComposeAsync(SystemUserPrincipal(), EntityType, CancellationToken.None);

        set.Count.Should().Be(all.Count);
        simulator.PagesServed.Should().Be(1, "no extra Dataverse round trip for the common case");
    }

    /// <summary>
    /// The contact plane pages identically. The standing-grant term ran through the same
    /// <c>options: null</c> call, so a standing-grant contact was capped at 500 the same way —
    /// the same finding wearing a different entry point, and invisible because the two composers
    /// were reviewed separately.
    /// </summary>
    [Fact]
    public async Task ComposeForContact_WithStandingGrant_FollowsContinuationTokensToo()
    {
        var all = Ids(PageSize + 250);
        var simulator = new FetchXmlPagingSimulator(all, PrimaryIdAttribute);
        var sut = ComposerWith(ResolverWith(simulator), standingGrant: true);

        var set = await sut.ComposeAsync(ContactPrincipal(), EntityType, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(all);
        set.Sources.StandingGrantMembership.Should().BeTrue();
        set.Capped.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GUARD 4 — the cap is surfaced, never silent (NFR-03)
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ✅ NEW COVERAGE (NFR-03) — at the ceiling with more records behind it, the set is flagged.
    ///
    /// <para>Paired deliberately with the control below. Both cases return EXACTLY
    /// <see cref="CapLimit"/> ids, so a count assertion alone cannot tell them apart — which is the
    /// vacuity this pairing exists to prevent. The distinguishing fact is whether the source held
    /// more, and only <c>Capped</c> reports it.</para>
    /// </summary>
    [Fact]
    public async Task ComposeForSystemUser_AtTheCeilingWithMoreBehindIt_FlagsCapped()
    {
        var all = Ids(CapLimit + 1);
        var sut = ComposerWith(ResolverWith(new FetchXmlPagingSimulator(all, PrimaryIdAttribute)));

        var set = await sut.ComposeAsync(SystemUserPrincipal(), EntityType, CancellationToken.None);

        set.Count.Should().Be(CapLimit, "composition stops at the documented ceiling");
        set.Capped.Should().BeTrue(
            "NFR-03: a truncated set must announce itself — the user is owed \"Only 5,000 records " +
            "displayed\", never a quietly short list");
        set.CapLimit.Should().Be(CapLimit, "so the caller can render the message without hard-coding it");
    }

    /// <summary>
    /// The control for the test above: EXACTLY the ceiling, nothing behind it. Same returned count,
    /// opposite flag.
    ///
    /// <para>This is the case a naive implementation gets wrong, and the reason the composer spends
    /// one confirmation read: the final page comes back full, so a token is in hand, and "token
    /// present" alone would mean crying wolf on every caller whose membership lands on an exact
    /// multiple. Reaching the limit is not the same as being cut by it.</para>
    /// </summary>
    [Fact]
    public async Task ComposeForSystemUser_AtExactlyTheCeilingWithNothingBehindIt_DoesNotFlagCapped()
    {
        var all = Ids(CapLimit);
        var sut = ComposerWith(ResolverWith(new FetchXmlPagingSimulator(all, PrimaryIdAttribute)));

        var set = await sut.ComposeAsync(SystemUserPrincipal(), EntityType, CancellationToken.None);

        set.Count.Should().Be(CapLimit);
        set.Capped.Should().BeFalse(
            "the set is complete; flagging it capped would tell the user records are hidden that " +
            "are not");
    }

    /// <summary>
    /// A resolver that keeps claiming another page while returning nothing new must terminate and be
    /// reported as incomplete — never spin. The id ceiling cannot catch this on its own, because a
    /// loop that adds no ids never reaches any id ceiling.
    /// </summary>
    [Fact]
    public async Task ComposeForSystemUser_WhenTheCursorStopsAdvancing_TerminatesAndFlagsCapped()
    {
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var calls = 0;
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, EntityType, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                calls++;
                // Always the same single id, always "there is more" — a cursor going nowhere.
                return ResponseOf(new[] { NewId(1) }, continuationToken: "stuck");
            });

        var sut = ComposerWith(membership.Object);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), EntityType, CancellationToken.None);

        set.Capped.Should().BeTrue("a set that could not be read to the end is not complete");
        calls.Should().BeLessThan(5, "the loop must stop on no-forward-progress, not grind on");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GUARD 5 — a broken cursor denies; it never returns a short set as if whole
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ✅ NEW COVERAGE (FR-14 negative) — a mid-stream failure must propagate.
    ///
    /// <para>The tempting alternative — catch, keep the pages already read, carry on — is worse than
    /// the bug it papers over: it hands back a partial set carrying no indication that it is
    /// partial, so every downstream <c>Contains</c> check denies records the caller can actually
    /// reach, and nothing anywhere says why. Failing loudly denies everything, which is recoverable;
    /// a plausible-looking short set is not.</para>
    /// </summary>
    [Fact]
    public async Task ComposeForSystemUser_WhenAPageFails_ThrowsRatherThanReturningAPartialSet()
    {
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var calls = 0;
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, EntityType, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                if (calls == 1)
                {
                    return Task.FromResult(ResponseOf(Ids(PageSize).ToArray(), continuationToken: "page-2"));
                }
                throw new InvalidOperationException("Dataverse paging failure on page 2.");
            });

        var sut = ComposerWith(membership.Object);

        var act = () => sut.ComposeAsync(SystemUserPrincipal(), EntityType, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a partial set presented as complete is a silent under-grant; denying is the safe failure");
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 015 — a malformed continuation token used to decode to "skip 0", silently
    /// restarting at page 1. A caller paging through their memberships would then loop over page 1
    /// forever, or stop believing they had seen everything. An unusable cursor is a caller error and
    /// must be reported (400 via the endpoint's ArgumentException mapping), never guessed at.
    /// </summary>
    [Theory]
    [InlineData("!!!not-base64!!!")]
    [InlineData("dGhpcy1pcy1ub3QtYS1jdXJzb3I")]   // valid base64url, wrong payload
    [InlineData("djF8NTAw")]                       // a v1-format token: "v1|500"
    public async Task ResolveAsync_WithAMalformedContinuationToken_ThrowsRatherThanSilentlyRestarting(string token)
    {
        var sut = ResolverWith(SingleRow(NewId(1)));

        var act = () => sut.ResolveAsync(
            SystemUserId,
            EntityType,
            new MembershipResolveOptions(ContinuationToken: token),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>(
            "decoding an unreadable cursor to 'start over' turns a caller error into an infinite " +
            "loop or a short read, and reports neither");
    }

    /// <summary>
    /// A token the resolver itself emitted must round-trip. Guards the inverse failure of the test
    /// above — a decoder strict enough to reject its own output would break paging entirely.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_TokenItEmitted_RoundTripsToTheNextPage()
    {
        var all = Ids(PageSize + 1);
        var simulator = new FetchXmlPagingSimulator(all, PrimaryIdAttribute);
        var sut = ResolverWith(simulator);

        var first = await sut.ResolveAsync(
            SystemUserId, EntityType,
            new MembershipResolveOptions(Limit: PageSize), CancellationToken.None);

        first.ContinuationToken.Should().NotBeNull();

        var second = await sut.ResolveAsync(
            SystemUserId, EntityType,
            new MembershipResolveOptions(Limit: PageSize, ContinuationToken: first.ContinuationToken),
            CancellationToken.None);

        second.Ids.Should().NotBeEmpty();
        second.Ids.Should().NotIntersectWith(first.Ids, "page 2 must not re-serve page 1's rows");
        simulator.LastRequestedPage.Should().Be(2, "the cursor must carry the page forward");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private static Guid NewId(int n) =>
        Guid.Parse($"0000{n:D4}-0000-0000-0000-{n:D12}");

    private static List<Guid> Ids(int count) =>
        Enumerable.Range(1, count).Select(NewId).ToList();

    private static FetchXmlPagingSimulator SingleRow(Guid id) =>
        new(new List<Guid> { id }, PrimaryIdAttribute);

    private static Mock<IMembershipFieldDiscoveryService> DiscoveryMock()
    {
        var descriptors = new[]
        {
            new MembershipDescriptor(
                Field: "ownerid", Role: "owner", IdentityType: "SystemUser",
                TargetTable: "systemuser", Source: "auto"),
            new MembershipDescriptor(
                Field: "sprk_assignedattorney1", Role: "assignedAttorney", IdentityType: "Contact",
                TargetTable: "contact", Source: "auto"),
        };

        var mock = new Mock<IMembershipFieldDiscoveryService>();
        mock.Setup(d => d.DiscoverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult(
                EntityType: EntityType,
                DiscoveredAt: DateTimeOffset.UtcNow,
                DiscoveredFields: descriptors,
                ExcludedFields: Array.Empty<IgnoredField>(),
                IgnoredFields: Array.Empty<IgnoredField>()));
        return mock;
    }

    private static MembershipResolverService ResolverWith(
        FetchXmlPagingSimulator simulator,
        List<string>? capturedQueries = null,
        Mock<IMembershipFieldDiscoveryService>? discovery = null)
    {
        // MockBehavior.Strict: any member the production code calls that the test did not model
        // throws, instead of handing back a permissive default.
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression fe, CancellationToken _) =>
            {
                capturedQueries?.Add(fe.Query);
                return Task.FromResult(simulator.Execute(fe.Query));
            });

        var identity = new Mock<IIdentityNormalizationService>();
        identity
            .Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(SystemUserId, ContactId: ContactId));

        return new MembershipResolverService(
            (discovery ?? DiscoveryMock()).Object,
            identity.Object,
            dataverse.Object,
            new NoOpTenantCache(),
            Options.Create(new MembershipOptions()),
            NullLogger<MembershipResolverService>.Instance);
    }

    private static AccessibleRecordSetService ComposerWith(
        IMembershipResolverService membership, bool standingGrant = false)
    {
        var standing = new Mock<IContactStandingGrantReader>();
        standing
            .Setup(s => s.HasStandingGrantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(standingGrant);

        return new AccessibleRecordSetService(
            membership,
            new NoGrantsParticipationService(),
            standing.Object,
            NullLogger<AccessibleRecordSetService>.Instance);
    }

    private static MembershipResponse ResponseOf(IReadOnlyList<Guid> ids, string? continuationToken) => new(
        EntityType: EntityType,
        PersonIdentity: new PersonIdentity(SystemUserId, ContactId: ContactId),
        Ids: ids,
        ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
        Count: ids.Count,
        CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
        ContinuationToken: continuationToken);

    private static WorkforcePrincipal SystemUserPrincipal() => new()
    {
        Kind = WorkforcePrincipalKind.SystemUser,
        SystemUserId = SystemUserId,
        ContactId = ContactId,
        Oid = Oid.ToString("D"),
        TenantId = Tenant,
    };

    private static WorkforcePrincipal ContactPrincipal() => new()
    {
        Kind = WorkforcePrincipalKind.ContactOnly,
        ContactId = ContactId,
        Oid = Oid.ToString("D"),
        TenantId = Tenant,
    };

    /// <summary>
    /// Grant-free participation double — these tests isolate the MEMBERSHIP term, so the grant term
    /// must contribute nothing and must not reach Dataverse.
    /// </summary>
    private sealed class NoGrantsParticipationService : ExternalParticipationService
    {
        public NoGrantsParticipationService()
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance)
        {
        }

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(new ExternalGrantSet
            {
                Projects = Array.Empty<ExternalParticipation>(),
                Matters = new HashSet<Guid>(),
                WorkAssignments = new HashSet<Guid>(),
            });

        public override Task<Guid?> ResolveExternalContactAsync(string? oid, string? email, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);
    }

    /// <summary>
    /// Cache double that never stores. Paging tests must exercise the query path on EVERY call —
    /// a caching double would serve page 1 back for page 2 and make the whole suite vacuous.
    /// </summary>
    private sealed class NoOpTenantCache : ITenantCache
    {
        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => Task.FromResult<T?>(default);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
            => factory(ct);
    }

    /// <summary>
    /// A FetchXml page/count paging simulator that is deliberately ADVERSARIAL, not accommodating.
    ///
    /// <para>The doubles that let A-10 through were permissive: they returned a fixed row set no
    /// matter what the query said, which made them MORE determinate than Dataverse — the platform's
    /// freedom to return an arbitrary page for an unordered query simply had no representation, so
    /// no test written against them could have failed. This one inverts that: every paging
    /// assumption the production code makes is either modelled explicitly or throws.</para>
    ///
    /// <para>It throws on: a query with no <c>&lt;order&gt;</c>; an order on any attribute it does
    /// not hold to be a unique key; <c>top</c> mixed with <c>page</c>/<c>count</c>; a missing or
    /// non-positive <c>count</c>; a condition operator other than <c>eq</c>; an empty filter; and a
    /// page ≥ 2 whose <c>paging-cookie</c> it did not itself issue.</para>
    ///
    /// <para>Row order is a total order deliberately DIFFERENT from <see cref="Guid"/>'s natural
    /// ordering (it sorts on the reversed string form), standing in for the fact that Dataverse
    /// sorts <c>uniqueidentifier</c> under a SQL Server collation that does not match
    /// <see cref="Guid.CompareTo(Guid)"/>. Any implementation that re-derived a page boundary from
    /// .NET ordering would disagree with this simulator and fail here.</para>
    /// </summary>
    private sealed class FetchXmlPagingSimulator
    {
        private readonly List<Guid> _serverOrder;
        private readonly string _uniqueSortKey;
        private readonly Dictionary<int, string> _issuedCookies = new();

        public int PagesServed { get; private set; }
        public int LastRequestedPage { get; private set; }

        /// <summary>
        /// The record this simulator places last in ITS order — guaranteed to fall on the final
        /// page. Tests that need "a record beyond page 1" must ask for it rather than assume
        /// construction order matches server order; it deliberately does not.
        /// </summary>
        public Guid LastRecordInServerOrder => _serverOrder[^1];

        public FetchXmlPagingSimulator(IReadOnlyList<Guid> rows, string uniqueSortKey)
        {
            _uniqueSortKey = uniqueSortKey;
            _serverOrder = rows
                .OrderBy(g => new string(g.ToString("D").Reverse().ToArray()), StringComparer.Ordinal)
                .ToList();
        }

        public EntityCollection Execute(string fetchXml)
        {
            var fetch = XElement.Parse(fetchXml);
            var entity = fetch.Element("entity")
                ?? throw new NotSupportedException("Simulator: <fetch> has no <entity>.");

            // The defect itself: a query with no defined order has no defined row selection, so the
            // simulator refuses to invent one rather than quietly returning fixture order. Checked
            // for EVERY query, paged or capped-by-top — an unordered `top` query picks an arbitrary
            // subset just as an unordered page does.
            var order = entity.Elements("order").ToList();
            if (order.Count == 0)
            {
                throw new NotSupportedException(
                    "Simulator: query has no <order>. Dataverse would return an arbitrary subset — " +
                    "pages could overlap or skip rows (finding A-10, defect i).");
            }

            // A query against a different table (the 1-hop transitive expansion). This simulator
            // holds rows for exactly one table; for any other it enforces the structural invariant
            // above and returns empty. It does NOT model the transitive filter — see the task notes'
            // "what these tests cannot falsify" list.
            var entityName = entity.Attribute("name")?.Value ?? string.Empty;
            if (!string.Equals(entityName, EntityType, StringComparison.OrdinalIgnoreCase))
            {
                return new EntityCollection(new List<Entity>());
            }

            if (fetch.Attribute("top") is not null &&
                (fetch.Attribute("page") is not null || fetch.Attribute("count") is not null))
            {
                throw new NotSupportedException(
                    "Simulator: 'top' cannot be combined with 'page'/'count' — that is not a paging " +
                    "scheme Dataverse defines (finding A-10, defect ii).");
            }

            if (fetch.Attribute("count") is not { } countAttr ||
                !int.TryParse(countAttr.Value, out var count) || count <= 0)
            {
                throw new NotSupportedException("Simulator: paged query needs a positive 'count'.");
            }

            var page = fetch.Attribute("page") is { } pageAttr && int.TryParse(pageAttr.Value, out var p)
                ? p
                : throw new NotSupportedException("Simulator: paged query needs a 'page'.");

            var orderAttribute = order[0].Attribute("attribute")?.Value;
            if (!string.Equals(orderAttribute, _uniqueSortKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    $"Simulator: <order attribute='{orderAttribute}'> is not a key this simulator " +
                    $"holds to be unique ('{_uniqueSortKey}'). A non-unique sort key leaves ties free " +
                    "to reorder between pages, which re-opens the paging defect.");
            }

            ValidateFilter(entity);

            if (page >= 2)
            {
                var cookie = fetch.Attribute("paging-cookie")?.Value;
                if (cookie is null || !_issuedCookies.TryGetValue(page - 1, out var expected) ||
                    !string.Equals(Unescape(cookie), expected, StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        $"Simulator: page {page} presented a paging cookie this simulator never " +
                        "issued for the previous page — the cursor was fabricated or dropped.");
                }
            }

            LastRequestedPage = page;
            PagesServed++;

            var skip = (page - 1) * count;
            var slice = _serverOrder.Skip(skip).Take(count).ToList();
            var more = skip + slice.Count < _serverOrder.Count;

            var issued = $"<cookie page=\"{page}\" last=\"{(slice.Count > 0 ? slice[^1] : Guid.Empty)}\" />";
            _issuedCookies[page] = issued;

            var collection = new EntityCollection(
                slice.Select(id =>
                {
                    var row = new Entity(EntityType) { Id = id };
                    row["ownerid"] = new EntityReference("systemuser", SystemUserId);
                    return row;
                }).ToList())
            {
                MoreRecords = more,
                PagingCookie = issued,
            };

            return collection;
        }

        private static void ValidateFilter(XElement entity)
        {
            var filter = entity.Element("filter")
                ?? throw new NotSupportedException("Simulator: query has no <filter>.");

            var conditions = filter.Elements("condition").ToList();
            if (conditions.Count == 0)
            {
                throw new NotSupportedException(
                    "Simulator: an empty OR-filter matches nothing — the resolver must not issue it.");
            }

            foreach (var condition in conditions)
            {
                var op = condition.Attribute("operator")?.Value;
                if (!string.Equals(op, "eq", StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        $"Simulator: condition operator '{op}' is not modelled. Teach the simulator " +
                        "before relying on it — do not let it default to permissive.");
                }
            }
        }

        private static string Unescape(string value) => value
            .Replace("&apos;", "'", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);
    }
}
