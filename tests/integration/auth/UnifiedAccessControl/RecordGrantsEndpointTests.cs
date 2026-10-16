using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// <c>GET /api/v1/external-access/record-grants</c> — the record's active external grants for Manage Access's Current Access
/// (unified-access-control-r2 task 099a, amendment 4 / #1010(a)).
/// </summary>
/// <remarks>
/// <para><b>What these tests protect.</b> The route discloses who can reach a record, through an APP-ONLY read, so the
/// delegation filter is its entire authorization: a caller with Write and Share on the record sees its grants (whatever
/// business unit owns the rows — which is the defect this route fixes), and every other caller gets the filter's 403 with
/// no read made. A read fault is a typed 503, never an empty 200 the dialog would show as "No active grants".</para>
/// <para><b>Positive/negative twins.</b> Each refusal has a twin that differs only in the caller's rights
/// (<see cref="DelegationRuleTestFixture"/>'s rights-in-the-token probe), so a route that always refused — or a filter that
/// was detached — cannot pass.</para>
/// <para>ADR-038 KEEP path #1 (<c>tests/integration/auth/**</c>) — authorization behaviour.</para>
/// </remarks>
public class RecordGrantsEndpointTests : IClassFixture<DelegationRuleTestFixture>
{
    private const string GrantsPath = "/api/v1/external-access/record-grants";
    private const string Delegator = "ReadAccess,WriteAccess,ShareAccess";
    private const string ReadOnly = "ReadAccess";
    private const string WriteWithoutShare = "ReadAccess,WriteAccess";

    private readonly DelegationRuleTestFixture _fixture;

    public RecordGrantsEndpointTests(DelegationRuleTestFixture fixture) => _fixture = fixture;

    // ─────────────────────────────────────────────────────────────────────────────
    // The pairs: Write and Share → the grants / no Write → 403 / no Share → 403
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The positive, and the #1010(a) fix: a caller with Write and Share on the record gets EVERY active grant row on it —
    /// a contact grant, an organization grant, and a lapsed one — each with its expiry and whether it has lapsed. The rows
    /// carry no owner here because the read is app-only: who owns a grant row no longer decides whether Manage Access shows it.
    /// </summary>
    [Theory]
    [InlineData("project", ExternalGrantRootType.Project)]
    [InlineData("matter", ExternalGrantRootType.Matter)]
    [InlineData("workassignment", ExternalGrantRootType.WorkAssignment)]
    public async Task GetRecordGrants_ForCallerWithWriteAndShare_ListsEveryActiveGrantWithItsExpiry(
        string recordType, ExternalGrantRootType rootType)
    {
        var recordId = Guid.NewGuid();
        var live = Row(rootType, recordId, contactId: Guid.NewGuid(), expires: new DateOnly(2099, 1, 31));
        live.ContactName = "Ada Lovelace";
        live.GrantedBySystemUserName = "Root BU Grantor";
        live.GrantedDate = new DateOnly(2026, 10, 1);
        var org = Row(rootType, recordId, organizationId: Guid.NewGuid(), expires: new DateOnly(2099, 6, 30));
        org.OrganizationName = "Contoso LLP";
        var lapsed = Row(rootType, recordId, contactId: Guid.NewGuid(), expires: new DateOnly(2020, 1, 1));
        _fixture.SeedRootGrantRows(recordId, live, org, lapsed);
        using var client = _fixture.CreateClientWithRights(Delegator);

        var response = await client.GetAsync($"{GrantsPath}?recordType={recordType}&recordId={recordId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("recordId").GetGuid().Should().Be(recordId,
            "the client discards an answer that does not name the record it is bound to");
        document.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();

        var grants = document.RootElement.GetProperty("grants").EnumerateArray()
            .ToDictionary(g => g.GetProperty("accessRecordId").GetGuid());
        grants.Should().HaveCount(3);

        var liveJson = grants[live.Id];
        liveJson.GetProperty("contactName").GetString().Should().Be("Ada Lovelace");
        liveJson.GetProperty("grantedByName").GetString().Should().Be("Root BU Grantor");
        liveJson.GetProperty("grantedDate").GetString().Should().Be("2026-10-01");
        liveJson.GetProperty("expiresDate").GetString().Should().Be("2099-01-31");
        liveJson.GetProperty("isExpired").GetBoolean().Should().BeFalse();

        grants[org.Id].GetProperty("organizationName").GetString().Should().Be("Contoso LLP");
        grants[org.Id].GetProperty("isExpired").GetBoolean().Should().BeFalse();

        grants[lapsed.Id].GetProperty("expiresDate").GetString().Should().Be("2020-01-01");
        grants[lapsed.Id].GetProperty("isExpired").GetBoolean().Should().BeTrue(
            "a lapsed share must show as lapsed, never as live access (amendment 2)");
    }

    /// <summary>
    /// The read is the grant core's own row read, aimed at THIS record: the root lookup of the record the client named plus
    /// <c>statecode eq 0</c>, and nothing narrower — an expiry predicate copied from the read path would hide the lapsed
    /// grants the user needs to see. It also selects the grant date the list shows.
    /// </summary>
    [Fact]
    public async Task GetRecordGrants_ReadsTheActiveRowsOfTheNamedRecord_WithoutAnExpiryPredicate()
    {
        var recordId = Guid.NewGuid();
        _fixture.SeedRootGrantRows(recordId, Row(ExternalGrantRootType.Matter, recordId, contactId: Guid.NewGuid(), expires: null));
        using var client = _fixture.CreateClientWithRights(Delegator);

        var response = await client.GetAsync($"{GrantsPath}?recordType=matter&recordId={recordId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var query = _fixture.RootGrantQueries.Should()
            .Contain(q => q.Filter != null && q.Filter.Contains(recordId.ToString())).Subject;
        query.Filter.Should().Be(ExternalGrantLifecycle.ActiveRowsForRootFilter(ExternalGrantRootType.Matter, recordId));
        query.Select!.Split(',').Should().Contain(new[] { "sprk_expiresdate", "sprk_granteddate", "_sprk_contact_value" });
        _fixture.ProbedTargets.Should().Contain(("sprk_matters", recordId),
            "the delegation check is aimed at the record whose grants are listed");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("grants")[0].GetProperty("isExpired").GetBoolean().Should().BeTrue(
            "an unbounded legacy row confers nothing on the read path (task 107), so it is listed as lapsed");
    }

    /// <summary>
    /// Verifier F2 (pass 1): past the cap the answer states it. A record with <c>MaxGrantsListed + 1</c> active rows gets
    /// exactly <c>MaxGrantsListed</c> grants and <c>truncated: true</c> — the flag the client turns into Current Access's
    /// "Only the first N grants are shown." (NFR-03: a cap stated, never a silent under-listing). Its twin is the positive
    /// above (three rows, <c>truncated: false</c>).
    /// </summary>
    [Fact]
    public async Task GetRecordGrants_PastTheCap_ListsExactlyTheCapAndSaysTruncated()
    {
        var recordId = Guid.NewGuid();
        var rows = Enumerable.Range(0, RecordGrantsEndpoint.MaxGrantsListed + 1)
            .Select(_ => Row(ExternalGrantRootType.Matter, recordId, contactId: Guid.NewGuid(), expires: new DateOnly(2099, 1, 31)))
            .ToArray();
        _fixture.SeedRootGrantRows(recordId, rows);
        using var client = _fixture.CreateClientWithRights(Delegator);

        var response = await client.GetAsync($"{GrantsPath}?recordType=matter&recordId={recordId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue(
            "a record with more rows than the cap must say its list is partial");
        document.RootElement.GetProperty("grants").GetArrayLength().Should().Be(RecordGrantsEndpoint.MaxGrantsListed);
    }

    /// <summary>
    /// The boundary twin (verifier pass 2, F3): exactly <c>MaxGrantsListed</c> rows is the whole list — every row listed and
    /// <c>truncated: false</c>, so a record AT the cap is never told its list is partial (a <c>&gt;=</c> comparison fails here).
    /// </summary>
    [Fact]
    public async Task GetRecordGrants_AtExactlyTheCap_ListsEveryRowAndIsNotTruncated()
    {
        var recordId = Guid.NewGuid();
        var rows = Enumerable.Range(0, RecordGrantsEndpoint.MaxGrantsListed)
            .Select(_ => Row(ExternalGrantRootType.Matter, recordId, contactId: Guid.NewGuid(), expires: new DateOnly(2099, 1, 31)))
            .ToArray();
        _fixture.SeedRootGrantRows(recordId, rows);
        using var client = _fixture.CreateClientWithRights(Delegator);

        var response = await client.GetAsync($"{GrantsPath}?recordType=matter&recordId={recordId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse(
            "a record with exactly the cap's number of rows has its whole list shown");
        document.RootElement.GetProperty("grants").GetArrayLength().Should().Be(RecordGrantsEndpoint.MaxGrantsListed);
    }

    /// <summary>The negative twin: a caller who can read the record but not write it is refused, and no grant row is read.</summary>
    [Fact]
    public async Task GetRecordGrants_ForCallerWithoutWrite_Is403AndReadsNothing()
    {
        var recordId = Guid.NewGuid();
        _fixture.SeedRootGrantRows(recordId, Row(ExternalGrantRootType.Matter, recordId, contactId: Guid.NewGuid(), expires: null));
        using var client = _fixture.CreateClientWithRights(ReadOnly);

        var response = await client.GetAsync($"{GrantsPath}?recordType=matter&recordId={recordId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired);
        _fixture.RootGrantQueries.Should().NotContain(q => q.Filter != null && q.Filter.Contains(recordId.ToString()),
            "a refused caller must not cause the app-only read");
    }

    /// <summary>The Share twin (owner round 89): Write without the Share privilege is not Manage Access, so no list.</summary>
    [Fact]
    public async Task GetRecordGrants_ForCallerWithWriteButNoShare_Is403()
    {
        var recordId = Guid.NewGuid();
        _fixture.SeedRootGrantRows(recordId, Row(ExternalGrantRootType.Project, recordId, contactId: Guid.NewGuid(), expires: null));
        using var client = _fixture.CreateClientWithRights(WriteWithoutShare);

        var response = await client.GetAsync($"{GrantsPath}?recordType=project&recordId={recordId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyShareRequired);
    }

    /// <summary>An unresolvable record is denied by authorization, as on every route of the group.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("?recordType=project")]
    [InlineData("?recordType=notarecordtype&recordId=8C6A5F35-3C55-4A0E-9E0E-7B1D2C3F4A5B")]
    public async Task GetRecordGrants_WithNoResolvableRecord_IsDeniedByAuthorization(string queryString)
    {
        using var client = _fixture.CreateClientWithRights(Delegator);

        var response = await client.GetAsync($"{GrantsPath}{queryString}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyTargetUnresolved);
    }

    /// <summary>With no credential at all the route is 401, like every other route on the group.</summary>
    [Fact]
    public async Task GetRecordGrants_WithNoCredential_Is401()
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync($"{GrantsPath}?recordType=project&recordId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // A read fault is a typed 503 — never an empty 200
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The read fails: the answer is 503 ProblemDetails with the stable reason code and the sentence the dialog shows
    /// verbatim — never a 200 with no grants, which the dialog would render as "No active grants" on a record that has them.
    /// Its twin is the positive above (same caller, a readable record).
    /// </summary>
    [Fact]
    public async Task GetRecordGrants_WhenTheGrantReadFails_Is503WithATypedProblem()
    {
        var recordId = Guid.NewGuid();
        _fixture.FaultingGrantRoots[recordId] = true;
        using var client = _fixture.CreateClientWithRights(Delegator);

        var response = await client.GetAsync($"{GrantsPath}?recordType=matter&recordId={recordId}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("reasonCode").GetString().Should().Be(RecordGrantsEndpoint.ReadFailedReasonCode);
        document.RootElement.GetProperty("detail").GetString().Should().Be(RecordGrantsEndpoint.ReadFailedDetail);
        document.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The row shape Dataverse returns
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The display names are Dataverse's formatted-value annotations and the grant date is a TimeZoneIndependent timestamp
    /// on the wire (the expiry's shape, task 140 §12). The row must read both — a name that is not read shows as
    /// "(unknown contact)" in the dialog, and a date that cannot be parsed would fail every list.
    /// </summary>
    [Fact]
    public void GrantRow_ReadsTheDisplayAnnotationsAndTheGrantDate_FromTheWebApiShape()
    {
        const string json = """
        {
          "sprk_externalrecordaccessid": "0a99a000-0000-0000-0000-000000000001",
          "statecode": 0,
          "sprk_accesslevel": 100000001,
          "sprk_expiresdate": "2026-12-10T00:00:00Z",
          "sprk_granteddate": "2026-09-11T00:00:00Z",
          "_sprk_contact_value": "0a99a000-0000-0000-0000-0000000000c1",
          "_sprk_contact_value@OData.Community.Display.V1.FormattedValue": "Ada Lovelace",
          "_sprk_organization_value@OData.Community.Display.V1.FormattedValue": "Contoso LLP",
          "_sprk_grantedby_value@OData.Community.Display.V1.FormattedValue": "Root BU Grantor",
          "_sprk_grantedbycontact_value@OData.Community.Display.V1.FormattedValue": "Grace Hopper"
        }
        """;

        var row = JsonSerializer.Deserialize<ExternalGrantRow>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        row.ContactName.Should().Be("Ada Lovelace");
        row.OrganizationName.Should().Be("Contoso LLP");
        row.GrantedBySystemUserName.Should().Be("Root BU Grantor");
        row.GrantedByContactName.Should().Be("Grace Hopper");
        row.GrantedDate.Should().Be(new DateOnly(2026, 9, 11));
        row.ExpiresDate.Should().Be(new DateOnly(2026, 12, 10));
    }

    // ─────────────────────────────────────────────────────────────────────────────

    private static ExternalGrantRow Row(
        ExternalGrantRootType rootType, Guid rootId, Guid? contactId = null, Guid? organizationId = null, DateOnly? expires = null)
    {
        var row = new ExternalGrantRow
        {
            Id = Guid.NewGuid(),
            ContactId = contactId,
            OrganizationId = organizationId,
            AccessLevel = 100000001,
            StateCode = 0,
            ExpiresDate = expires,
        };

        switch (rootType)
        {
            case ExternalGrantRootType.Project: row.ProjectId = rootId; break;
            case ExternalGrantRootType.Matter: row.MatterId = rootId; break;
            case ExternalGrantRootType.WorkAssignment: row.WorkAssignmentId = rootId; break;
        }

        return row;
    }

    private static async Task<string?> ReasonCodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return null;

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
    }
}
