using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Identity;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Task-003 vertical-slice seam tests (ADR-038 "DoD for dispatch/read-spine changes") for the R3 list-all-threads
/// read (<see cref="CommunicationThreadReadService.ListThreadsAsync"/> / FR-16, Surface 2). This is the correctness-
/// critical NFR-01 surface: the list MUST equal the IMPERSONATED <c>sprk_communicationthreads</c> set for the caller
/// (Dataverse row-level security is the ONLY gate), MUST include record-less (Direct) threads (no regarding scoping),
/// MUST NOT over-disclose a thread the caller cannot see, and MUST NOT hand-compute a membership-union
/// (retired 2026-07-16, <c>../messaging-communication-app-r1/notes/access-model-decision.md</c>).
///
/// <para><b>Boundary mocks only (ADR-038):</b> <see cref="IImpersonatedCommunicationQuery"/> (the Dataverse/
/// impersonation read boundary) and <see cref="ICallerSystemUserResolver"/> (the caller-resolution boundary). The
/// class-under-test <see cref="CommunicationThreadReadService"/> and the REAL <see cref="CommunicationAccessFilter"/>
/// are production code, unmocked. No <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration test, no ctor null-check
/// test.</para>
/// </summary>
public class CommunicationListThreadsSeamTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private const string ThreadSet = "sprk_communicationthreads";
    private const int ThreadTypeRecordAnchored = 100000000;
    private const int ThreadTypeDirect = 100000001;

    private readonly Mock<IImpersonatedCommunicationQuery> _query = new();
    private readonly Mock<ICallerSystemUserResolver> _resolver = new();

    private CommunicationThreadReadService Sut(bool resolves = true)
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolves
                ? CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D"))
                : CallerSystemUserResolution.Unresolved("no-matching-systemuser"));

        return new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            Mock.Of<ISystemUserIdentityResolver>(), // #675: default IsExternalAsync ⇒ false (internal caller) — preserves pre-fix behavior
            Mock.Of<ILogger<CommunicationThreadReadService>>());
    }

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // A. Record-less (Direct) thread appears for a caller who can see it (FR-16 record-less inclusion)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListThreadsAsync_RecordLessDirectThread_AppearsForCallerWhoCanSeeIt()
    {
        var directThreadId = Guid.NewGuid();
        string? threadQuery = null;
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => threadQuery = odata)
              .ReturnsAsync(new[] { ThreadRow(directThreadId, "Alice ↔ Bob", ThreadTypeDirect, "2026-07-19T12:00:00.000Z") });

        var result = await Sut().ListThreadsAsync(Caller(), search: null, top: null, pageToken: null, CancellationToken.None);

        result.Threads.Should().ContainSingle(t => t.ThreadId == directThreadId)
            .Which.ThreadType.Should().Be(ThreadTypeDirect, "a Direct/record-less thread must be listed like any other");
        // The impersonated thread query's FILTER is NOT scoped to any regarding lookup — that is HOW a record-less
        // thread (which carries no sprk_regarding{type} anchor) is included at all. (round-8.4 item 3: the $SELECT now
        // DOES project the regarding lookups for the open-record affordance, so the check is filter-scoped, not the
        // whole query.)
        threadQuery.Should().NotBeNull();
        var filterOnward = threadQuery!.Contains("$filter=", StringComparison.Ordinal)
            ? threadQuery.Substring(threadQuery.IndexOf("$filter=", StringComparison.Ordinal))
            : string.Empty;
        filterOnward.Should().NotContain("_sprk_regarding",
            "the list-all FILTER must not be scoped to any regarding lookup or record-less threads would be excluded");
    }

    [Fact]
    public async Task ListThreadsAsync_RecordAnchoredThread_ResolvesRegardingFromTypedLookup()
    {
        // round-8.4 item 3: a record-anchored thread carries a typed ADR-024 regarding lookup; the list projects it so
        // the message-pane "open associated record" affordance can navigate to it.
        var threadId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var row = ThreadRow(threadId, "Acme v. Beta", ThreadTypeRecordAnchored, "2026-07-19T12:00:00.000Z");
        row["_sprk_regardingmatter_value"] = El(matterId.ToString());
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { row });

        var result = await Sut().ListThreadsAsync(Caller(), search: null, top: null, pageToken: null, CancellationToken.None);

        var thread = result.Threads.Should().ContainSingle(t => t.ThreadId == threadId).Subject;
        thread.RegardingEntityType.Should().Be("sprk_matter",
            "the typed sprk_regardingmatter lookup resolves to its entity logical name (RegardingFieldMap)");
        thread.RegardingId.Should().Be(matterId);
    }

    [Fact]
    public async Task ListThreadsAsync_ThreadRegardingSelect_ExcludesReportCard_WhichIsNotOnTheThreadEntity()
    {
        // Regression (round-8.4 fix, 2026-08-03): the thread projection must NOT $select sprk_regardingreportcard —
        // that lookup exists on sprk_communication but NOT on sprk_communicationthread, so selecting it 400s the whole
        // OData query and blanks the thread list (client HttpRequestException). It MUST still select a valid regarding
        // lookup (sprk_regardingmatter) so the open-record affordance keeps working.
        string? threadQuery = null;
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => threadQuery = odata)
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());

        await Sut().ListThreadsAsync(Caller(), search: null, top: null, pageToken: null, CancellationToken.None);

        threadQuery.Should().NotBeNull();
        threadQuery!.Should().NotContain("regardingreportcard",
            "sprk_regardingreportcard is not a column on sprk_communicationthread — selecting it 400s the query");
        threadQuery!.Should().Contain("_sprk_regardingmatter_value",
            "the thread projection must still carry the valid typed regarding lookups for the open-record affordance");
    }

    [Fact]
    public async Task ListThreadsAsync_RecordLessThread_HasNullRegarding()
    {
        // A Direct/record-less thread has no typed regarding lookup → both regarding fields stay null (no open-record).
        var threadId = Guid.NewGuid();
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { ThreadRow(threadId, "Alice ↔ Bob", ThreadTypeDirect, "2026-07-19T12:00:00.000Z") });

        var result = await Sut().ListThreadsAsync(Caller(), search: null, top: null, pageToken: null, CancellationToken.None);

        var thread = result.Threads.Should().ContainSingle(t => t.ThreadId == threadId).Subject;
        thread.RegardingEntityType.Should().BeNull();
        thread.RegardingId.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // B. Access parity — the list equals the impersonated set, no post-hoc regarding scoping
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListThreadsAsync_ReturnsExactlyTheImpersonatedThreadSet_NoPostRegardingScoping()
    {
        var anchored = Guid.NewGuid();
        var direct = Guid.NewGuid();
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[]
              {
                  ThreadRow(anchored, "Acme v Widgets", ThreadTypeRecordAnchored, "2026-07-19T12:00:00.000Z"),
                  ThreadRow(direct, "Alice ↔ Bob", ThreadTypeDirect, "2026-07-18T09:00:00.000Z"),
              });

        var result = await Sut().ListThreadsAsync(Caller(), search: null, top: null, pageToken: null, CancellationToken.None);

        // The result is EXACTLY the impersonated set (both a record-anchored and a record-less thread), 1:1 — no row
        // dropped by a second filter, no row added.
        result.Count.Should().Be(2);
        result.Threads.Select(t => t.ThreadId).Should().BeEquivalentTo(new[] { anchored, direct });
        // Only ONE query is issued — to the thread set. No membership/grant/second query composes the answer.
        _query.Verify(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()), Times.Once);
        _query.VerifyNoOtherCalls();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // C. NEGATIVE — a thread the caller cannot see is absent (no over-disclosure, no union)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListThreadsAsync_ThreadCallerCannotSee_IsAbsentAndNoUnionComputed()
    {
        var visibleThreadId = Guid.NewGuid();
        var hiddenThreadId = Guid.NewGuid();

        // Dataverse impersonation returns ONLY the thread the caller may see — the private/hidden one is simply not
        // in the impersonated set. The service returns exactly that; it never issues a second query to "add back"
        // membership, and there is no BFF post-filter that could surface the hidden thread.
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { ThreadRow(visibleThreadId, "Visible", ThreadTypeRecordAnchored, "2026-07-19T12:00:00.000Z") });

        var result = await Sut().ListThreadsAsync(Caller(), search: null, top: null, pageToken: null, CancellationToken.None);

        result.Threads.Should().ContainSingle(t => t.ThreadId == visibleThreadId);
        result.Threads.Should().NotContain(t => t.ThreadId == hiddenThreadId,
            "a thread absent from the impersonated set must never be surfaced (no over-disclosure, no membership-union)");
        _query.Verify(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()), Times.Once);
        _query.VerifyNoOtherCalls();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // D. Stable, non-overlapping keyset paging over createdon desc
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListThreadsAsync_MoreThanPageSize_DistinctCreatedOn_PagesAreStableAndNonOverlapping()
    {
        // Three threads, distinct createdon-descending. Page size 2 ⇒ over-fetch 3, page1 = first 2 + cursor;
        // page2 (via the cursor) = the remaining 1. The mock emulates Dataverse's composite ordering + the
        // (createdon, id) keyset predicate, so this is a genuine keyset-paging proof, not a canned two-call script.
        var t1 = ThreadRow(Guid.NewGuid(), "newest", ThreadTypeRecordAnchored, "2026-07-19T12:00:00.000Z");
        var t2 = ThreadRow(Guid.NewGuid(), "middle", ThreadTypeRecordAnchored, "2026-07-18T12:00:00.000Z");
        var t3 = ThreadRow(Guid.NewGuid(), "oldest", ThreadTypeDirect, "2026-07-17T12:00:00.000Z");
        var all = new[] { t1, t2, t3 };

        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync((string _, string? odata, Guid _, CancellationToken _) => EmulateDataverse(all, odata));

        var sut = Sut();

        var page1 = await sut.ListThreadsAsync(Caller(), search: null, top: 2, pageToken: null, CancellationToken.None);
        page1.Count.Should().Be(2);
        page1.HasMore.Should().BeTrue();
        page1.NextPageToken.Should().NotBeNullOrEmpty();
        page1.Threads.Select(t => t.Name).Should().ContainInOrder("newest", "middle");

        var page2 = await sut.ListThreadsAsync(Caller(), search: null, top: 2, pageToken: page1.NextPageToken, CancellationToken.None);
        page2.Count.Should().Be(1);
        page2.HasMore.Should().BeFalse("the third thread is the last one");
        page2.NextPageToken.Should().BeNull();
        page2.Threads.Single().Name.Should().Be("oldest");

        // Non-overlapping: no id appears on both pages.
        page1.Threads.Select(t => t.ThreadId).Should().NotIntersectWith(page2.Threads.Select(t => t.ThreadId));
    }

    [Fact]
    public async Task ListThreadsAsync_ThreadsSharingOneCreatedOn_StraddlingBoundary_NoRowDroppedOrDuplicated()
    {
        // C1 REGRESSION: three threads sharing the EXACT SAME createdon straddle a page boundary. A createdon-only
        // `lt` cursor would permanently skip the tied rows past the cut (silent thread loss — breaks FR-16). The
        // composite (createdon, sprk_communicationthreadid) cursor must page through ALL THREE with no drop, no
        // duplicate. Ids are chosen so their ordinal order is a < b < c ⇒ createdon-desc,id-desc order is c,b,a.
        const string tiedCreatedOn = "2026-07-19T12:00:00.000Z";
        var idA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var idB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var idC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var all = new[]
        {
            ThreadRow(idA, "tie-a", ThreadTypeRecordAnchored, tiedCreatedOn),
            ThreadRow(idB, "tie-b", ThreadTypeDirect, tiedCreatedOn),
            ThreadRow(idC, "tie-c", ThreadTypeRecordAnchored, tiedCreatedOn),
        };

        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync((string _, string? odata, Guid _, CancellationToken _) => EmulateDataverse(all, odata));

        var sut = Sut();
        var seen = new List<Guid>();

        // Page through the WHOLE list two-at-a-time until exhausted (bounded loop guards against a paging bug hang).
        string? token = null;
        for (var guard = 0; guard < 10; guard++)
        {
            var page = await sut.ListThreadsAsync(Caller(), search: null, top: 2, pageToken: token, CancellationToken.None);
            seen.AddRange(page.Threads.Select(t => t.ThreadId));
            token = page.NextPageToken;
            if (!page.HasMore)
                break;
        }

        // All three tied rows are reachable exactly once — nothing dropped, nothing duplicated.
        seen.Should().BeEquivalentTo(new[] { idA, idB, idC });
        seen.Should().OnlyHaveUniqueItems("a tied row must never be returned on two pages");
        // Composite order: createdon equal ⇒ id desc ⇒ c, b, a.
        seen.Should().ContainInOrder(idC, idB, idA);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // E. Name search filters on sprk_name, value safely escaped (OData injection)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListThreadsAsync_SearchWithApostrophe_DoublesQuoteThenPercentEncodesLiteral()
    {
        string? threadQuery = null;
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => threadQuery = odata)
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());

        await Sut().ListThreadsAsync(Caller(), search: "O'Brien", top: null, pageToken: null, CancellationToken.None);

        threadQuery.Should().NotBeNull();
        // Two-stage escape: the single quote is DOUBLED (OData literal breakout defense) → O''Brien, then the whole
        // literal is percent-encoded for transport → each ' becomes %27, so O''Brien → O%27%27Brien.
        threadQuery!.Should().Contain("contains(sprk_name,'O%27%27Brien')");
    }

    [Fact]
    public async Task ListThreadsAsync_SearchWithSpaceAndAmpersand_IsPercentEncoded_NoQueryInjection()
    {
        // W1 REGRESSION: the impersonated-query seam concatenates the value RAW into the URL (no Uri.EscapeDataString
        // there), so a search containing a space or '&' — a two-word or email-like term, or a crafted injection like
        // "x&$top=999" — must be percent-encoded at the point it is embedded, or it truncates/injects the query
        // string (malformed query → 500, or an attacker-controlled $top).
        string? threadQuery = null;
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => threadQuery = odata)
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());

        await Sut().ListThreadsAsync(Caller(), search: "north & south", top: 5, pageToken: null, CancellationToken.None);

        threadQuery.Should().NotBeNull();
        // Space → %20, ampersand → %26: the value stays INSIDE the contains() literal, well-formed.
        threadQuery!.Should().Contain("contains(sprk_name,'north%20%26%20south')");
        // The value must NOT introduce a raw '&' (which would start a new query-string parameter). The ONLY raw '&'
        // in the whole query are the structural separators before $filter / $orderby / $top.
        threadQuery!.Split('&').Should().OnlyContain(seg =>
            seg.StartsWith("$select") || seg.StartsWith("$filter") || seg.StartsWith("$orderby") || seg.StartsWith("$top"),
            "no user-supplied '&' may split the query string into an extra parameter");
        // The real $top is the requested page size + 1 over-fetch (6) — a crafted value cannot inject its own $top.
        System.Text.RegularExpressions.Regex.Matches(threadQuery!, @"\$top=").Count.Should().Be(1);
        threadQuery!.Should().Contain("$top=6");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // F. Fail-closed on an unresolved caller (403, no app-only fallback)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListThreadsAsync_UnresolvedCaller_ThrowsForbiddenAndIssuesNoImpersonatedQuery()
    {
        var act = async () => await Sut(resolves: false)
            .ListThreadsAsync(Caller(), search: null, top: null, pageToken: null, CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(403);
        // Fail-closed: not even an app-only query is issued when the caller has no systemuserid.
        _query.Verify(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // G. Malformed page token is a 400 (graceful degradation — never a silent full-list dump)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListThreadsAsync_MalformedPageToken_Returns400NotAFullDump()
    {
        var act = async () => await Sut()
            .ListThreadsAsync(Caller(), search: null, top: null, pageToken: "!!!not-base64!!!", CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(400);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // H. Structural no-union guard for the NEW list read (NFR-01)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoMembershipUnionRegression_ListThreadsAsync_TakesNoMembershipOrGrantSeamParameter()
    {
        // NetArchTest-style architecture guard on the NEW read method's OWN signature (ADR-038 §6 sanctioned
        // replacement for a wiring test). ListThreadsAsync must reach its answer through the impersonated query +
        // caller resolver ONLY — never a membership-resolution / private-grant seam. If a future edit tried to
        // resurrect the retired union it would show up as one of these parameter types here.
        var bannedSeamTypeNames = new[]
        {
            "IThreadPrivateGrantProvider",
            "IMembershipResolverService",
            "IThreadMembershipDerivationService",
            "IThreadExplicitParticipantReader",
        };

        var listThreads = typeof(CommunicationThreadReadService)
            .GetMethod(nameof(CommunicationThreadReadService.ListThreadsAsync), BindingFlags.Public | BindingFlags.Instance);
        listThreads.Should().NotBeNull();

        var paramTypeNames = listThreads!.GetParameters().Select(p => p.ParameterType.Name).ToList();
        foreach (var banned in bannedSeamTypeNames)
        {
            paramTypeNames.Should().NotContain(banned,
                $"ListThreadsAsync must not take a membership/grant union dependency ({banned}) — reads are impersonation-only");
        }

        // The read service's dependency shape is the access-model-decision collaborators (impersonated query, the
        // shared 2-rule filter, the caller resolver, a logger) PLUS the #675/ISS-006 ISystemUserIdentityResolver —
        // the sanctioned per-caller internal/external source that replaced the hardcoded IsInternalUser:true (5 total).
        // The substantive guard is that NONE of the banned membership/grant UNION seam types creep into the ctor — a
        // reintroduced union path would show up as one of those, not as the identity resolver.
        var ctorParamTypeNames = typeof(CommunicationThreadReadService).GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType.Name).ToList();
        ctorParamTypeNames.Should().HaveCount(5);
        foreach (var banned in bannedSeamTypeNames)
        {
            ctorParamTypeNames.Should().NotContain(banned,
                $"the read service must not take a membership/grant union dependency ({banned}) — reads are impersonation-only");
        }
        ctorParamTypeNames.Should().Contain("ISystemUserIdentityResolver",
            "the #675 fix injects the authoritative per-caller internal/external resolver");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // Row builder + paging helpers (OData JSON shape — mirrors the sibling read seam builders)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    private static Dictionary<string, JsonElement> ThreadRow(Guid id, string name, int threadType, string createdOnIso) => new()
    {
        ["sprk_communicationthreadid"] = El(id.ToString()),
        ["sprk_name"] = El(name),
        ["sprk_threadtype"] = El(threadType),
        ["createdon"] = El(createdOnIso),
    };

    private static DateTimeOffset CreatedOn(Dictionary<string, JsonElement> row)
        => DateTimeOffset.Parse(row["createdon"].GetString()!, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

    private static string IdStr(Dictionary<string, JsonElement> row) => row["sprk_communicationthreadid"].GetString()!;

    /// <summary>
    /// Faithfully emulates the Dataverse response for the list-all-threads query: applies the COMPOSITE
    /// <c>(createdon, sprk_communicationthreadid)</c> keyset predicate the service emits, orders by
    /// <c>createdon desc, sprk_communicationthreadid desc</c> (ordinal on the id "D" string — internally consistent
    /// with the predicate below), and honors <c>$top</c> (the pageSize+1 over-fetch). Because the mock filters AND
    /// orders on the same comparison the service's cursor encodes, this is a genuine round-trip paging proof.
    /// </summary>
    private static Dictionary<string, JsonElement>[] EmulateDataverse(
        IEnumerable<Dictionary<string, JsonElement>> all, string? odata)
    {
        var (cursorCreated, cursorId) = ParseCompositeCursor(odata);
        var top = ParseTop(odata);
        return all
            .Where(r => cursorCreated is null
                || CreatedOn(r) < cursorCreated
                || (CreatedOn(r) == cursorCreated && string.CompareOrdinal(IdStr(r), cursorId) < 0))
            .OrderByDescending(CreatedOn)
            .ThenByDescending(IdStr, StringComparer.Ordinal)
            .Take(top)
            .ToArray();
    }

    /// <summary>Parses the composite keyset predicate <c>(createdon lt V or (createdon eq V and sprk_communicationthreadid lt Vid))</c>.</summary>
    private static (DateTimeOffset? Created, string? Id) ParseCompositeCursor(string? odata)
    {
        if (string.IsNullOrEmpty(odata))
            return (null, null);
        var m = System.Text.RegularExpressions.Regex.Match(
            odata,
            @"createdon lt (?<c>[0-9T:\.\-Z]+)( or \(createdon eq [0-9T:\.\-Z]+ and sprk_communicationthreadid lt (?<id>[0-9a-fA-F\-]+)\))?");
        if (!m.Success)
            return (null, null);
        var created = DateTimeOffset.Parse(m.Groups["c"].Value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        return (created, m.Groups["id"].Success ? m.Groups["id"].Value : null);
    }

    private static int ParseTop(string? odata)
    {
        var m = System.Text.RegularExpressions.Regex.Match(odata ?? string.Empty, @"\$top=(?<n>\d+)");
        return m.Success ? int.Parse(m.Groups["n"].Value) : int.MaxValue;
    }

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);
}
