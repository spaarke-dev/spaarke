using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Task 061 — the share plane on <c>/provision-project</c>, and the reverse path on
/// <c>/unsecure-project</c>.
/// </summary>
/// <remarks>
/// <para><b>The defect these pin.</b> Task 021 assigned secure projects to the Secure Project business
/// unit's default owner team, which has no members — correct isolation. It issued no shares. design.md
/// §5.1 says <i>"All human access is by explicit Dataverse share, including the creating attorney's"</i>,
/// so provisioning completed and left a record **no human could open**: isolated, and unreachable. The
/// assertions below are about that round trip — a secure project must end up reachable by exactly the
/// person who created it, and by nobody else.</para>
///
/// <para><b>Why the creator's share is fatal and a colleague's is not.</b> The creator's share is the
/// only thing standing between "provisioned" and "locked box", so failing to issue it must fail the
/// provision. A named colleague can be added afterwards through the Manage Access surface, so failing
/// one of those must NOT throw away a provision that otherwise succeeded.</para>
/// </remarks>
public class SecureProjectShareTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";
    private const string UnsecureRoute = "/api/v1/external-access/unsecure-project";
    private const string ProjectEntitySet = "sprk_projects";

    private readonly ProvisionProjectTestFixture _fixture;

    public SecureProjectShareTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static async Task<string?> ReasonCodeOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("reasonCode", out var reason) ? reason.GetString() : null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Provisioning: the creator's share is what makes the project reachable
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Provisioning_SharesTheProjectBackToItsCreator()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var creatorShare = _fixture.Grants.Should().ContainSingle(
            g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId,
            "the creating attorney is the one human who must be able to open the project they just made")
            .Subject;

        creatorShare.EntitySet.Should().Be(ProjectEntitySet);
        creatorShare.RecordId.Should().Be(projectId);
        creatorShare.Principal.Kind.Should().Be(DataversePrincipalKind.SystemUser);
        creatorShare.AccessRightsCsv.Should().Be(ProvisionProjectEndpoint.CreatorAccessRights);
    }

    [Fact]
    public async Task Provisioning_GivesTheCreatorShareAccess_SoTheyCanAddColleaguesWithoutAnAdministrator()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        _fixture.Grants
            .Single(g => g.Principal.Id == ProvisionProjectTestFixture.CallerSystemUserId)
            .AccessRightsCsv.Should().Contain("ShareAccess",
                "FR-29's '+ User' picker is the creator re-sharing their own project; without ShareAccess "
                + "every addition would need an administrator");
    }

    [Fact]
    public async Task Provisioning_SharesToNamedPrincipals_WithoutShareAccess()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var colleagueShare = _fixture.Grants.Should()
            .ContainSingle(g => g.Principal.Id == colleague).Subject;

        colleagueShare.AccessRightsCsv.Should().Be(ProvisionProjectEndpoint.CollaboratorAccessRights);
        colleagueShare.AccessRightsCsv.Should().NotContain("ShareAccess",
            "re-sharing stays with the creator so the access list cannot widen through a chain nobody reviewed");
    }

    [Fact]
    public async Task Provisioning_SharesToNobodyElse()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        _fixture.Grants.Select(g => g.Principal.Id).Should().BeEquivalentTo(
            new[] { ProvisionProjectTestFixture.CallerSystemUserId },
            "a secure project that provisioning quietly shared with anyone else would defeat its own point");
    }

    [Fact]
    public async Task Provisioning_WhenTheCallersIdentityCannotBeEstablished_FailsRatherThanLeavingALockedBox()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.CallerSystemUserIdResolves = false;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorUnresolved);

        _fixture.Grants.Should().BeEmpty();
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty(
            "the share step runs BEFORE container creation precisely so a share failure orphans nothing");
    }

    [Fact]
    public async Task Provisioning_WhenTheCreatorsShareFails_FailsAndCreatesNoContainer()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.FailShareForPrincipal = ProvisionProjectTestFixture.CallerSystemUserId;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ReasonCodeOf(response)).Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        _fixture.CreatedContainerDisplayNames.Should().BeEmpty();
    }

    [Fact]
    public async Task Provisioning_WhenAColleaguesShareFails_StillSucceeds()
    {
        var projectId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.FailShareForPrincipal = colleague;
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            ProvisionRoute, new { projectId, sharePrincipalIds = new[] { colleague } });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a mistyped colleague id must not throw away a provision whose creator share succeeded");

        _fixture.Grants.Select(g => g.Principal.Id).Should()
            .BeEquivalentTo(new[] { ProvisionProjectTestFixture.CallerSystemUserId });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The reverse path
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unsecure_ReassignsOwnershipAndRevokesEveryShare()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });
        _fixture.Grants.Should().NotBeEmpty();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Revokes.Select(r => r.Principal.Id).Should().Contain(
            ProvisionProjectTestFixture.CallerSystemUserId,
            "a leftover POA row on a no-longer-secure project is an access path no secure-project UI "
            + "would show");

        _fixture.Updates.Should().Contain(
            u => u.EntitySet == ProjectEntitySet
                 && u.RecordId == projectId
                 && u.Payload.ContainsKey("sprk_issecure"),
            "the designation itself has to be cleared, not just the isolation");

        // ISS-018 regression: a readable record still reports its sweep as COMPLETE, and the count
        // still matches what was revoked. Without this the strict read could start refusing every
        // record and the happy path would go on passing.
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("sharesRevoked").GetInt32()
            .Should().Be(_fixture.Revokes.Count).And.BeGreaterThan(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ISS-018 (#995) — a failed share read must not read as a clean sweep
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unsecure_OnARecordWithNoShares_ReportsACompleteSweepOfZero()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        // No provisioning call, so the record genuinely carries no shares.
        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sharesRevoked").GetInt32().Should().Be(0);
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeTrue(
            "zero shares removed from a readable record IS a complete sweep — this is the half of the "
            + "distinction that a failed read must not be able to imitate");
    }

    [Fact]
    public async Task Unsecure_WhenTheShareReadCannotBeCompleted_ReportsAnIncompleteSweep()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });
        _fixture.Grants.Should().NotBeEmpty();

        // The strict read refuses (more than one page, or an unreadable row). The soft read still
        // returns what it can parse, so partial progress is possible.
        _fixture.StrictShareReadSucceeds = false;

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "ownership has already moved, so the flow stays non-fatal");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeFalse(
            "a sweep driven by an incomplete enumeration must never claim to have removed everything");

        _fixture.Revokes.Should().NotBeEmpty(
            "refusing outright would leave EVERY share in place; sweeping what can be enumerated "
            + "removes some stale access");

        body.RootElement.GetProperty("sharesRevoked").GetInt32()
            .Should().Be(_fixture.Revokes.Count,
                "the reported count must match what was actually revoked — an implementation that "
                + "returned 0 alongside a non-empty sweep would otherwise pass");

        // Anchored on the endpoint's own log prefix: provisioning also writes Warning-level lines
        // interpolating this same project id, so a guid-only predicate could be satisfied by a log
        // the code under test never wrote.
        _fixture.Logs.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("[UNSECURE]")
                 && e.Message.Contains(projectId.ToString()),
            "the warning naming the failure was unreachable before this fix — the soft read swallowed "
            + "the failure and answered an empty list, so the catch could not fire for the case it named");
    }

    [Fact]
    public async Task Unsecure_WhenNoSharesCanBeEnumeratedAtAll_ReportsZeroRevokedAndAnIncompleteSweep()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });
        _fixture.Grants.Should().NotBeEmpty();

        // Neither read can answer — the transport itself is failing.
        _fixture.StrictShareReadSucceeds = false;
        _fixture.SoftShareReadSucceeds = false;

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sharesRevoked").GetInt32().Should().Be(0);
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeFalse(
            "THIS is the defect: zero-revoked-because-unreadable answered 200 with sharesRevoked = 0, "
            + "indistinguishable from a record that had no shares, so an unsecure could leave every "
            + "share in place silently");

        _fixture.Revokes.Should().BeEmpty("nothing could be enumerated, so nothing was removed");
    }

    [Fact]
    public async Task Unsecure_WhenAShareRevokeFails_StaysNonFatalAndReportsAnIncompleteSweep()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        _fixture.FailRevokeForPrincipal = ProvisionProjectTestFixture.CallerSystemUserId;

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "best-effort per share and never fatal — a reassignment that already succeeded is not "
            + "thrown away by one stubborn POA row");

        _fixture.Updates.Should().Contain(
            u => u.RecordId == projectId && u.Payload.ContainsKey("sprk_issecure"),
            "the flag is still cleared: per ADR-003 sprk_issecure suppresses the derived-member and "
            + "org-expansion terms, NOT Dataverse's own answer, so leaving it set would buy no "
            + "protection against the surviving share while half-applying the unsecure");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeFalse(
            "a share we failed to remove means the record is demonstrably not fully revoked");

        _fixture.Logs.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("[UNSECURE]")
                 && e.Message.Contains(ProvisionProjectTestFixture.CallerSystemUserId.ToString()),
            "each failure is logged with its principal so an operator can finish the job");
    }

    [Fact]
    public async Task Unsecure_OnAProjectThatIsNotSecure_MakesNoCompletenessClaim()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, isSecure: false);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("sweepComplete").ValueKind.Should().Be(JsonValueKind.Null,
            "no sweep is attempted on the idempotent path, so the response must not vouch that the "
            + "record is free of POA rows — a project that was never secure can still carry shares "
            + "issued by another surface");
    }

    [Fact]
    public async Task Unsecure_RetriedAfterAnIncompleteSweep_DoesNotThenClaimACleanSweep()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        // First call: the sweep cannot account for every share. The flag is cleared regardless —
        // per ADR-003 it suppresses derived/org terms, not the POA row, so holding it back would
        // protect nothing.
        _fixture.StrictShareReadSucceeds = false;
        var first = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });
        using (var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            firstBody.RootElement.GetProperty("sweepComplete").GetBoolean().Should().BeFalse();
        }

        // Retrying is the ONLY remediation a sweepComplete:false response affords — and because the
        // flag is now clear, the retry lands on the idempotent path, which sweeps nothing.
        var second = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        second.StatusCode.Should().Be(HttpStatusCode.OK);

        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        secondBody.RootElement.GetProperty("alreadyUnsecure").GetBoolean().Should().BeTrue();
        secondBody.RootElement.GetProperty("sweepComplete").ValueKind.Should().Be(JsonValueKind.Null,
            "the retry must NOT answer 'complete sweep of zero' over a record whose shares are still "
            + "in place — that is ISS-018's exact shape reintroduced on the very path an incomplete "
            + "sweep invites the operator onto. A defaulted true here is what review caught");
    }

    [Fact]
    public async Task Unsecure_AssignsOwnershipBeforeClearingTheFlag()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();
        await client.PostAsJsonAsync(ProvisionRoute, new { projectId });

        await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        // Ordering is read off RecordedUpdate.Sequence, NOT off the bag's enumeration order —
        // Updates is a ConcurrentBag, which does not preserve insertion order.
        var writes = _fixture.Updates.Where(u => u.RecordId == projectId).ToList();

        var ownerWrite = writes
            .Where(u => u.Payload.Keys.Any(k => k.StartsWith("ownerid", StringComparison.Ordinal)))
            .Select(u => u.Sequence)
            .DefaultIfEmpty(-1)
            .Min();

        var flagWrite = writes
            .Where(u => u.Payload.ContainsKey("sprk_issecure"))
            .Select(u => u.Sequence)
            .DefaultIfEmpty(-1)
            .Min();

        ownerWrite.Should().BeGreaterThan(0, "ownership must actually be reassigned");
        flagWrite.Should().BeGreaterThan(ownerWrite,
            "clearing the flag first would advertise a normal project while it was still team-owned and "
            + "share-gated");
    }

    [Fact]
    public async Task Unsecure_OnAProjectThatIsNotSecure_IsAnIdempotentNoOp()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId, isSecure: false);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a repeat is not a conflict — the caller's "
            + "intent is already satisfied");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("alreadyUnsecure").GetBoolean().Should().BeTrue();

        _fixture.Revokes.Should().BeEmpty();
        _fixture.Updates.Should().BeEmpty("an idempotent no-op writes nothing");
    }

    [Fact]
    public async Task Unsecure_OnAMissingProject_Is404()
    {
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonProjectNotFound);
    }

    [Fact]
    public async Task Unsecure_WhenNoOwnerCanBeResolved_RefusesRatherThanStrandingTheRecord()
    {
        var projectId = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        _fixture.CallerSystemUserIdResolves = false; // no request nomination, no config, no caller
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(UnsecureRoute, new { projectId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(UnsecureProjectEndpoint.ReasonOwnerUnresolved);
        _fixture.Revokes.Should().BeEmpty("nothing is torn down until a destination owner exists");
    }

    [Fact]
    public async Task Unsecure_HonoursAnExplicitlyNominatedOwner()
    {
        var projectId = Guid.NewGuid();
        var steward = Guid.NewGuid();
        _fixture.SeedProject(projectId);
        var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            UnsecureRoute, new { projectId, reassignToSystemUserId = steward });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("newOwnerSystemUserId").GetGuid().Should().Be(steward);
    }
}
