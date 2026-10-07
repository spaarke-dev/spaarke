using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Task 077 (spaarkeai-word-add-in-r1, FR-16 gap (a)) — the NEGATIVE authorization case for the route the
/// Find tab's records half now depends on: a record the caller cannot read does not appear in
/// <c>POST /api/ai/search/records</c>'s response.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test had to be written, not cited.</b> Task 077's constraint is that Find's records MUST come
/// from a path with real per-row authorization, and this route was chosen because it has one
/// (<c>RecordSearchEndpoints.AuthorizeRowsAsync</c>). But before this file nothing exercised that method:
/// <c>RecordSearchEndpointsTests</c> covers request/response DTO shape only, and the only row-trim test in the
/// suite (<see cref="VisualizationBudgetWarningContractTests"/>) is for the visualization route's sibling of
/// the same name. The authorization claim the records half rests on was, until now, untested.
/// </para>
/// <para>
/// <b>Why the handler is invoked directly.</b> Same reasoning and precedent as
/// <see cref="VisualizationBudgetWarningContractTests"/>: <c>PostRecordSearch</c> was made <c>internal</c> by
/// task 077 so a test proves something about the SHIPPED handler rather than a re-implementation of its
/// branches. Only <see cref="IRecordSearchService"/> (the search engine) and <see cref="IAccessDataSource"/>
/// (what Dataverse would answer for the caller) are substituted — the real <see cref="AuthorizationService"/>,
/// the real fail-closed checks and the real trim all run. Helpers are local to this file, per the project's
/// "configure test classes locally" boundary.
/// </para>
/// <para>
/// <b>What this also demonstrates, for the client.</b> Two rows in, one row out: the trim SHORTENS the page.
/// That is the premise of <c>useFindRecordMatches</c>' paging rule (a non-empty page means "maybe more", not
/// "page fullness"), and this test is the server-side evidence that a short page is not the end.
/// </para>
/// </remarks>
[Trait("category", "authorization")]
public sealed class RecordSearchRowAuthorizationContractTests
{
    private const string TenantId = "aaaaaaaa-2222-3333-4444-555555555555";
    private const string CallerOid = "cccccccc-8888-7777-6666-555555555555";

    private static readonly Guid ReadableMatter = Guid.Parse("11111111-0000-0000-0000-0000000000a1");
    private static readonly Guid UnreadableMatter = Guid.Parse("22222222-0000-0000-0000-0000000000d1");

    [Fact]
    public async Task ARecordTheCallerCannotRead_IsNotReturned_AndIsNotCounted()
    {
        var access = new PerRecordAccessDataSource();
        access.Deny(UnreadableMatter);
        var search = new StubRecordSearchService(
            Row(ReadableMatter, "Acme v. Globex"),
            Row(UnreadableMatter, "Confidential Matter"));

        var result = await InvokeAsync(search, access);
        var body = OkBody(result);

        body.Results.Select(r => r.RecordId).Should().NotContain(UnreadableMatter.ToString(),
            "a record the caller cannot read must never reach the Find tab — not its id");
        body.Results.Select(r => r.RecordName).Should().NotContain("Confidential Matter",
            "nor its name, which is the part that actually discloses something");
        body.Results.Select(r => r.RecordId).Should().ContainSingle()
            .Which.Should().Be(ReadableMatter.ToString(),
                "the readable record must survive — a trim that drops everything would pass the lines above");

        body.Metadata.TotalCount.Should().Be(1,
            "the count must not leak the unreadable row either: the pre-filter total would reveal how many "
            + "matters match a query, which is the same kind of disclosure as the rows themselves");

        access.CheckedRecordIds.Should().BeEquivalentTo(new[] { ReadableMatter, UnreadableMatter },
            "BOTH rows must have been evaluated as the caller — proving the unreadable row was dropped by an "
            + "access decision, not skipped for some unrelated reason such as an unresolvable type");
    }

    // =====================================================================================
    // Helpers — local to this file (project boundary rule: no shared fixture/factory edited)
    // =====================================================================================

    private static RecordSearchResult Row(Guid id, string name) => new()
    {
        RecordId = id.ToString(),
        RecordType = RecordEntityType.Matter,
        RecordName = name,
        ConfidenceScore = 0.8,
    };

    private static async Task<IResult> InvokeAsync(StubRecordSearchService search, PerRecordAccessDataSource access)
    {
        var request = new RecordSearchRequest
        {
            Query = "indemnification, governing law",
            RecordTypes = [RecordEntityType.Matter],
        };

        return await RecordSearchEndpoints.PostRecordSearch(
            request,
            search,
            new AuthorizationService(access, Array.Empty<IAuthorizationRule>(), NullLogger<AuthorizationService>.Instance),
            CallerContext(),
            NullLoggerFactory.Instance,
            CancellationToken.None);
    }

    private static DefaultHttpContext CallerContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tid", TenantId), new Claim(CallerResolution.ObjectIdClaim, CallerOid)], "Test")),
        };

        // REQUIRED, not incidental: AuthorizationService fails closed with no caller token (finding A-2), so
        // without it every row would be denied before IAccessDataSource is consulted, and the test would
        // pass while measuring the wrong guard.
        httpContext.Request.Headers.Authorization = "Bearer test-caller-token";

        // What RecordSearchAuthorizationFilter hands the handler. Without it the handler refuses outright
        // (its forcing function) — this test is about the trim, so the filter's decision is supplied.
        httpContext.Items[RecordSearchAuthorization.HttpContextItemsKey] = new RecordSearchAuthorization
        {
            RequiresPerRowRecordAuthorization = true,
            RequestedEntitySets = new Dictionary<string, string> { [RecordEntityType.Matter] = "sprk_matters" },
        };
        return httpContext;
    }

    private static RecordSearchResponse OkBody(IResult result)
    {
        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(200,
            "an unreadable row is a trim, not a request-level denial");
        return result.Should().BeAssignableTo<IValueHttpResult>().Which.Value
            .Should().BeOfType<RecordSearchResponse>().Subject;
    }

    /// <summary>
    /// Answers as Dataverse would for the CALLER: Read on every record unless explicitly denied. Records each
    /// record id it is asked about, so a test can prove a row was dropped by a decision rather than skipped.
    /// </summary>
    private sealed class PerRecordAccessDataSource : IAccessDataSource
    {
        private readonly HashSet<Guid> _denied = [];

        public List<Guid> CheckedRecordIds { get; } = [];

        public void Deny(Guid recordId) => _denied.Add(recordId);

        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            throw new NotSupportedException(
                "Record-search rows are Matter/Project/Invoice records and must be authorized as records.");

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken,
            CancellationToken ct = default)
        {
            CheckedRecordIds.Add(recordId);
            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = recordId.ToString(),
                AccessRights = _denied.Contains(recordId) ? AccessRights.None : AccessRights.Read,
            });
        }
    }

    /// <summary>Stands in for the search engine at the module boundary — returns the seeded rows as-is.</summary>
    private sealed class StubRecordSearchService(params RecordSearchResult[] rows) : IRecordSearchService
    {
        public Task<RecordSearchResponse> SearchAsync(
            RecordSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecordSearchResponse
            {
                Results = rows,
                Metadata = new RecordSearchMetadata
                {
                    TotalCount = rows.Length,
                    SearchTime = 1,
                    HybridMode = RecordHybridSearchMode.Rrf,
                },
            });
    }
}
