using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
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
