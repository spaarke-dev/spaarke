using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// <c>GET /api/v1/external-access/can-manage-access</c> — the route that lets a client ASK the delegation
/// question instead of guessing at it (unified-access-control-r2 task 118, spec FR-07 / owner decision D-1
/// option C).
/// </summary>
/// <remarks>
/// <para><b>What these tests are protecting.</b> The Manage Access affordance in the
/// <c>TrackingFieldTrio</c> PCF gates on this route's answer, and it treats ANYTHING other than
/// <c>200 + canManageAccess: true</c> as a denial. So the properties that matter are not only "a caller
/// with Write gets a yes" but the whole shape of the no: a caller without Write, a caller with every right
/// except Write, a caller whose rights cannot be established, and a request naming no resolvable record
/// must each fail to produce that exact shape.</para>
///
/// <para><b>Why every negative has a positive twin.</b> This route's honest answer when nothing works is
/// 403, and offline everything fails — so a lone 403 assertion would pass equally against a route that
/// denied unconditionally, against the filter being detached and some other layer refusing, and against
/// the route not existing at all. <see cref="DelegationRuleTestFixture"/> substitutes the probe so a
/// caller can genuinely hold Write, which is what makes the negatives mean something: the pairs differ
/// ONLY in the caller's rights.</para>
///
/// <para><b>The failure mode with the largest blast radius is a route that always says yes.</b> Detached
/// from <c>AddDelegationRuleFilter()</c> this handler returns <c>canManageAccess: true</c> to everyone —
/// it has no rights logic of its own, by design — and every client gate built on it would fail OPEN,
/// which is the defect task 118 closed. <see cref="GetCanManageAccess_ForCallerWithoutWriteOnTarget_IsDenied"/>
/// and its siblings are what red when that happens.</para>
///
/// <para>ADR-038 KEEP path #1 (<c>tests/integration/auth/**</c>) — authorization behaviour.</para>
/// </remarks>
public class RecordAccessGateTests : IClassFixture<DelegationRuleTestFixture>
{
    private const string GatePath = "/api/v1/external-access/can-manage-access";

    /// <summary>Dataverse's own wire spelling for a caller who can read but not write.</summary>
    private const string ReadOnly = "ReadAccess";

    /// <summary>A caller who can write — and therefore may delegate (owner decision B-14).</summary>
    private const string ReadWrite = "ReadAccess,WriteAccess";

    /// <summary>
    /// Everything EXCEPT Write. Guards against the gate being weakened to "has any rights at all", which
    /// would readmit exactly the read-only caller this route exists to keep out of the affordance.
    /// </summary>
    private const string EveryRightExceptWrite =
        "ReadAccess,DeleteAccess,CreateAccess,AppendAccess,AppendToAccess,ShareAccess";

    private readonly DelegationRuleTestFixture _fixture;

    public RecordAccessGateTests(DelegationRuleTestFixture fixture) => _fixture = fixture;

    // ─────────────────────────────────────────────────────────────────────────────
    // The pair: Write yes / Write no
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The positive. A caller holding Write gets the one shape the client accepts — and the answer names
    /// the record it is about, which is what lets the client discard an answer that arrives after its form
    /// has rebound to a different record.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task GetCanManageAccess_ForCallerWithWriteOnTarget_AnswersYesForThatRecord(string recordType)
    {
        var recordId = Guid.NewGuid();
        using var client = _fixture.CreateClientWithRights(ReadWrite);

        var response = await client.GetAsync($"{GatePath}?recordType={recordType}&recordId={recordId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("canManageAccess").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("recordId").GetGuid().Should().Be(recordId,
            "the client discards an answer that does not name the record it is currently bound to");
    }

    /// <summary>
    /// The negative twin, and the one the whole task turns on: a caller who can READ the record but not
    /// write it is refused, with the delegation deny code — so the refusal is attributable to THIS rule
    /// rather than to some unrelated failure further down.
    /// </summary>
    [Fact]
    public async Task GetCanManageAccess_ForCallerWithoutWriteOnTarget_IsDenied()
    {
        using var client = _fixture.CreateClientWithRights(ReadOnly);

        var response = await client.GetAsync($"{GatePath}?recordType=project&recordId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired);
    }

    /// <summary>
    /// Read is not licence to grant, and neither is Create. This is the case the RETIRED client gate got
    /// wrong: it asked for table-level <c>Create</c> on <c>sprk_externalrecordaccess</c>, so a caller
    /// holding Create but no Write on a confidential matter was offered the affordance. Here that caller
    /// holds every Dataverse right except Write and is still refused.
    /// </summary>
    [Fact]
    public async Task GetCanManageAccess_ForCallerHoldingEveryRightExceptWrite_IsStillDenied()
    {
        using var client = _fixture.CreateClientWithRights(EveryRightExceptWrite);

        var response = await client.GetAsync($"{GatePath}?recordType=project&recordId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Fail closed — the answers that are not "no" but must still read as "no"
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The gate asks about the record the client named, and it asks Dataverse about the right ENTITY SET.
    /// Asserted because the client's whole claim is "this is the server's verdict on THIS record"; a gate
    /// that authorized some other record would be worse than no gate, and a status code alone cannot
    /// distinguish the two.
    /// </summary>
    [Theory]
    [InlineData("project", "sprk_projects")]
    [InlineData("matter", "sprk_matters")]
    [InlineData("workassignment", "sprk_workassignments")]
    public async Task GetCanManageAccess_ChecksWriteOnTheRecordTheClientNamed(string recordType, string expectedEntitySet)
    {
        var recordId = Guid.NewGuid();
        using var client = _fixture.CreateClientWithRights(ReadOnly);

        await client.GetAsync($"{GatePath}?recordType={recordType}&recordId={recordId}");

        _fixture.ProbedTargets.Should().Contain((expectedEntitySet, recordId));
    }

    /// <summary>
    /// When the rights check itself THROWS, the answer is a denial — never a degraded "probably fine".
    /// This is the server-side half of the fail-direction inversion: the client disables the affordance on
    /// any non-200, so the server must not manufacture a 200 out of an unanswerable question.
    /// </summary>
    [Fact]
    public async Task GetCanManageAccess_WhenTheRightsCheckThrows_IsDeniedNotAllowed()
    {
        using var client = _fixture.CreateClientWithRights("THROW");

        var response = await client.GetAsync($"{GatePath}?recordType=project&recordId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyCheckFailed);
    }

    /// <summary>
    /// No caller credential means the question cannot be evaluated AS the caller, and an app-only
    /// evaluation would answer for the application (finding A-2). Denied.
    /// </summary>
    [Fact]
    public async Task GetCanManageAccess_WithNoBearerToken_IsDenied()
    {
        using var client = _fixture.CreateClientWithoutBearerToken();

        var response = await client.GetAsync($"{GatePath}?recordType=project&recordId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyNoCallerToken);
    }

    /// <summary>
    /// A request naming no resolvable record is denied by AUTHORIZATION, not answered. Both cases below
    /// carry Write, so the refusal cannot be mistaken for a rights outcome: there is simply no record to
    /// have rights on, and an unresolvable target is one the filter will not vouch for.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("?recordType=project")]
    [InlineData("?recordType=notarecordtype&recordId=8C6A5F35-3C55-4A0E-9E0E-7B1D2C3F4A5B")]
    public async Task GetCanManageAccess_WithNoResolvableRecord_IsDeniedByAuthorization(string queryString)
    {
        using var client = _fixture.CreateClientWithRights(ReadWrite);

        var response = await client.GetAsync($"{GatePath}{queryString}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyTargetUnresolved);
    }

    /// <summary>With no credential at all the route is 401, like every other route on the group.</summary>
    [Fact]
    public async Task GetCanManageAccess_WithNoCredential_Is401()
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync($"{GatePath}?recordType=project&recordId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Task 150, round 46 item 4 — includeOwner: the record's owning team, and whether it is the Secure Record Owners team
    // (the Access ribbon's "did this secure transition finish?"). Round 53 item 2 adds whether that team owns it INSIDE
    // the Secure Record business unit (another team there is already isolated: the ribbon hides Make Secure). Facts about
    // the RECORD; the delegation answer and its gate are unchanged, and a caller without Write still gets the filter's 403
    // with no owner read.
    // ─────────────────────────────────────────────────────────────────────────────

    private static readonly Guid SecureBusinessUnit = Guid.Parse("d9ec0b6f-0000-0000-0000-0000000000b0");
    private static readonly Guid SecureOwnerTeam = Guid.Parse("daec0b6f-0000-0000-0000-0000000000f1");

    /// <summary>
    /// The record's owner read (the gate's ONE read of the record) answers <paramref name="row"/> for this record — only
    /// when it selects every column the answer is built from (both owner columns and the owning business unit), so a read
    /// that stops asking for one answers nothing, and the definite answers below fail.
    /// </summary>
    private void OwnerReadAnswers(Guid recordId, RecordAccessGateEndpoint.OwnerRow? row, Exception? fault = null)
    {
        var setup = _fixture.DataverseClient.Setup(c => c.QueryAsync<RecordAccessGateEndpoint.OwnerRow>(
            It.IsAny<string>(), It.Is<string?>(f => f != null && f.Contains(recordId.ToString())),
            It.Is<string?>(s => s != null && s.Split(',', StringSplitOptions.None).Contains("_owningteam_value")
                && s.Split(',', StringSplitOptions.None).Contains("_owninguser_value")
                && s.Split(',', StringSplitOptions.None).Contains("_owningbusinessunit_value")),
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()));
        if (fault is not null)
            setup.ThrowsAsync(fault);
        else
            setup.ReturnsAsync(row is null
                ? new List<RecordAccessGateEndpoint.OwnerRow>()
                : new List<RecordAccessGateEndpoint.OwnerRow> { row });
    }

    /// <summary>
    /// Which team is the Secure Record Owners team: the configured business unit (one row), then its named owner team(s)
    /// — <paramref name="teams"/> rows, so two make it ambiguous (SecureRecordOwnerTeam.IdentifyAsync's own reads).
    /// </summary>
    private void SecureOwnerTeamsAre(params Guid[] teams)
    {
        _fixture.DataverseClient
            .Setup(c => c.QueryAsync<SecureRecordOwnerTeam.IdRow>(
                "businessunits", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecureRecordOwnerTeam.IdRow> { new() { businessunitid = SecureBusinessUnit } });
        _fixture.DataverseClient
            .Setup(c => c.QueryAsync<SecureRecordOwnerTeam.IdRow>(
                "teams", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(teams.Select(t => new SecureRecordOwnerTeam.IdRow { teamid = t, name = "Secure Record Owners" }).ToList());
    }

    private static (JsonElement OwningTeamId, JsonElement OwnedBySecureOwnerTeam, JsonElement InSecureBusinessUnit)
        OwnerFactsOf(JsonDocument document) =>
        (document.RootElement.GetProperty("owningTeamId"), document.RootElement.GetProperty("ownedBySecureOwnerTeam"),
            document.RootElement.GetProperty("owningTeamInSecureBusinessUnit"));

    /// <summary>
    /// Asked for the owner, the gate names it: a USER-owned record is not owned by the Secure team (owningTeamId null, and
    /// no team to place); a record the Secure Record Owners team owns is, inside the Secure Record business unit; ANOTHER
    /// team inside that business unit (the retired default team before task 144's migration — round 53 item 2: already
    /// isolated) is not the Secure team but IS inside; a team in another business unit (a reassignment outside Spaarke) is
    /// neither — the cases the client cannot tell apart on its own.
    /// </summary>
    [Theory]
    [InlineData("user", false, null)]
    [InlineData("secure-team", true, true)]
    [InlineData("other-team-inside", false, true)]
    [InlineData("other-team-outside", false, false)]
    public async Task GetCanManageAccess_WithIncludeOwner_NamesTheOwningTeam_WhetherItIsTheSecureOwnerTeam_AndWhetherItIsInside(
        string owner, bool expectedSecureTeam, bool? expectedInside)
    {
        var recordId = Guid.NewGuid();
        var otherTeam = Guid.NewGuid();
        using var client = _fixture.CreateClientWithRights(ReadWrite);
        SecureOwnerTeamsAre(SecureOwnerTeam);
        OwnerReadAnswers(recordId, owner switch
        {
            "user" => new RecordAccessGateEndpoint.OwnerRow
            {
                _owninguser_value = Guid.NewGuid(), _owningbusinessunit_value = Guid.NewGuid()
            },
            "secure-team" => new RecordAccessGateEndpoint.OwnerRow
            {
                _owningteam_value = SecureOwnerTeam, _owningbusinessunit_value = SecureBusinessUnit
            },
            "other-team-inside" => new RecordAccessGateEndpoint.OwnerRow
            {
                _owningteam_value = otherTeam, _owningbusinessunit_value = SecureBusinessUnit
            },
            _ => new RecordAccessGateEndpoint.OwnerRow
            {
                _owningteam_value = otherTeam, _owningbusinessunit_value = Guid.NewGuid()
            },
        });

        var response = await client.GetAsync($"{GatePath}?recordType=matter&recordId={recordId}&includeOwner=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("canManageAccess").GetBoolean().Should().BeTrue();
        var (owningTeamId, ownedBySecure, inside) = OwnerFactsOf(document);
        ownedBySecure.GetBoolean().Should().Be(expectedSecureTeam);
        if (expectedInside is { } expected)
            inside.GetBoolean().Should().Be(expected);
        else
            inside.ValueKind.Should().Be(JsonValueKind.Null, "a user owner has no owning team to place");
        if (owner == "user")
            owningTeamId.ValueKind.Should().Be(JsonValueKind.Null);
        else
            owningTeamId.GetGuid().Should().Be(owner == "secure-team" ? SecureOwnerTeam : otherTeam);
    }

    /// <summary>
    /// Round 53 item 2: a team-owned record read WITHOUT its owning business unit cannot be placed inside or outside the
    /// Secure Record business unit — <c>owningTeamInSecureBusinessUnit: null</c>, never "outside" (which the ribbon would
    /// read as an unfinished transition and offer Make Secure on a record that may already be isolated).
    /// </summary>
    [Fact]
    public async Task GetCanManageAccess_WithIncludeOwner_ATeamOwnerReadWithoutItsBusinessUnit_IsNotPlaced()
    {
        var recordId = Guid.NewGuid();
        using var client = _fixture.CreateClientWithRights(ReadWrite);
        SecureOwnerTeamsAre(SecureOwnerTeam);
        OwnerReadAnswers(recordId, new RecordAccessGateEndpoint.OwnerRow { _owningteam_value = Guid.NewGuid() });

        var response = await client.GetAsync($"{GatePath}?recordType=project&recordId={recordId}&includeOwner=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var (_, ownedBySecure, inside) = OwnerFactsOf(document);
        ownedBySecure.GetBoolean().Should().BeFalse();
        inside.ValueKind.Should().Be(JsonValueKind.Null, "a fact that could not be established is never reported as either answer");
    }

    /// <summary>
    /// Unknown is never an answer: the owner read fails, the record reads with no owner, or which team is the Secure Record
    /// Owners team is ambiguous — each answers <c>ownedBySecureOwnerTeam: null</c> (the client then offers nothing on that
    /// basis), and the delegation answer is still the filter's 200.
    /// </summary>
    [Theory]
    [InlineData("owner-read-fails")]
    [InlineData("no-owner")]
    [InlineData("secure-team-ambiguous")]
    public async Task GetCanManageAccess_WithIncludeOwner_WhenTheOwnerCannotBeTold_SaysUnknown(string shape)
    {
        var recordId = Guid.NewGuid();
        using var client = _fixture.CreateClientWithRights(ReadWrite);
        switch (shape)
        {
            case "owner-read-fails":
                SecureOwnerTeamsAre(SecureOwnerTeam);
                OwnerReadAnswers(recordId, null, new HttpRequestException("Dataverse 503: simulated owner read failure."));
                break;
            case "no-owner":
                SecureOwnerTeamsAre(SecureOwnerTeam);
                OwnerReadAnswers(recordId, new RecordAccessGateEndpoint.OwnerRow());
                break;
            default:
                SecureOwnerTeamsAre(SecureOwnerTeam, Guid.NewGuid());
                OwnerReadAnswers(recordId, new RecordAccessGateEndpoint.OwnerRow { _owningteam_value = SecureOwnerTeam });
                break;
        }

        var response = await client.GetAsync($"{GatePath}?recordType=project&recordId={recordId}&includeOwner=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("canManageAccess").GetBoolean().Should().BeTrue();
        OwnerFactsOf(document).OwnedBySecureOwnerTeam.ValueKind.Should().Be(JsonValueKind.Null,
            "a fact that could not be established is never reported as either answer");
        OwnerFactsOf(document).InSecureBusinessUnit.ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Not asked, the gate reads nothing: the Manage Access gates' form-load path stays one rights probe.</summary>
    [Fact]
    public async Task GetCanManageAccess_WithoutIncludeOwner_ReadsNoOwner()
    {
        var recordId = Guid.NewGuid();
        using var client = _fixture.CreateClientWithRights(ReadWrite);

        var response = await client.GetAsync($"{GatePath}?recordType=workassignment&recordId={recordId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        OwnerFactsOf(document).OwnedBySecureOwnerTeam.ValueKind.Should().Be(JsonValueKind.Null);
        _fixture.DataverseClient.Verify(c => c.QueryAsync<RecordAccessGateEndpoint.OwnerRow>(
            It.IsAny<string>(), It.Is<string?>(f => f != null && f.Contains(recordId.ToString())), It.IsAny<string?>(),
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Task 175 (owner round 84): a 200 for a work assignment filed under a matter names that DIRECT parent in
    /// <c>followsParents</c> (the locked state — distinct from "no permission", which is the filter's 403); a parentless one
    /// answers an empty list; one whose filing cannot be read answers <c>parentUnverifiable</c>; a matter never has parents.
    /// </summary>
    [Fact]
    public async Task GetCanManageAccess_NamesTheParentsAChildFollows_AndSaysWhenThatCannotBeRead()
    {
        var (matter, child, parentless, unreadable) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var world = Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard();
        world.Add("sprk_matter", matter, ("sprk_issecure", false), ("sprk_mattername", "Falcon"));
        world.Add("sprk_workassignment", child, ("sprk_regardingmatter", new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter)));
        world.Add("sprk_workassignment", parentless);
        world.Add("sprk_workassignment", unreadable, ("sprk_regardingmatter", new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter)));
        world.FailingRowReadsOf("sprk_workassignment", unreadable);
        _fixture.FilingWorld = world;
        using var client = _fixture.CreateClientWithRights(ReadWrite);

        async Task<JsonElement> GateOf(string type, Guid id)
        {
            var response = await client.GetAsync($"{GatePath}?recordType={type}&recordId={id}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        var locked = await GateOf("workassignment", child);
        var parent = locked.GetProperty("followsParents").EnumerateArray().Single();
        parent.GetProperty("recordType").GetString().Should().Be("matter");
        parent.GetProperty("recordId").GetGuid().Should().Be(matter);
        parent.GetProperty("name").GetString().Should().Be("Falcon");
        locked.GetProperty("parentUnverifiable").GetBoolean().Should().BeFalse();

        (await GateOf("workassignment", parentless)).GetProperty("followsParents").EnumerateArray().Should().BeEmpty();

        var unknown = await GateOf("workassignment", unreadable);
        unknown.GetProperty("followsParents").EnumerateArray().Should().BeEmpty();
        unknown.GetProperty("parentUnverifiable").GetBoolean().Should().BeTrue("never 'no parent' on a guess");

        (await GateOf("matter", matter)).GetProperty("followsParents").EnumerateArray().Should().BeEmpty();
    }

    /// <summary>A caller without Write gets the filter's 403 even when asking for the owner — and no owner read is made.</summary>
    [Fact]
    public async Task GetCanManageAccess_WithIncludeOwner_ForCallerWithoutWrite_IsDeniedAndReadsNoOwner()
    {
        var recordId = Guid.NewGuid();
        using var client = _fixture.CreateClientWithRights(ReadOnly);

        var response = await client.GetAsync($"{GatePath}?recordType=project&recordId={recordId}&includeOwner=true");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired);
        _fixture.DataverseClient.Verify(c => c.QueryAsync<RecordAccessGateEndpoint.OwnerRow>(
            It.IsAny<string>(), It.Is<string?>(f => f != null && f.Contains(recordId.ToString())), It.IsAny<string?>(),
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The ProblemDetails <c>reasonCode</c>, or <c>null</c> when the response carries none.</summary>
    private static async Task<string?> ReasonCodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
