using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 099b (D-11, #1010) — every NEW <c>sprk_externalrecordaccess</c> row is created team-owned,
/// record-first, through the ONE ownership resolver: a record in a child business unit gives that unit's default Owner team, a
/// secure record gives the named "Secure Record Owners" team, and the application identity never owns a grant. Through the
/// PRODUCTION <c>/grant</c> (contact and organization grantees), <c>/invite-and-grant</c> and Assigned-To materializer over the
/// task-142 harness, with the REAL <see cref="Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver"/> over an in-memory
/// directory. The contact-side route (<c>/contact-grants</c>) is covered in
/// <c>ContactGrantAuthorizationTests.GrantRowOwnership.cs</c>.
/// </summary>
/// <remarks>
/// <para>Faults follow the <c>ExternalIntakeEndpoints</c> step-4 idiom: a resolver read fault is a 503
/// (<c>owner_unreadable</c>), no team is a 500 (<c>owner_unresolved</c>), and in both cases NO grant row is written. The
/// materializer reports the same refusal as a failure, exactly as it reports a failed grant create.</para>
/// <para>KEEP path: <c>tests/integration/auth/**</c> (ADR-038 §2, security-auth).</para>
/// </remarks>
public class GrantRowOwnershipTests
{
    private const ExternalGrantRootType Matter = ExternalGrantRootType.Matter;
    private const string Attorney1 = "sprk_assignedattorney1";
    private const string Email = "new@outside.test";

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();

    public GrantRowOwnershipTests()
    {
        _h.Store.Root(Matter, _matter);
        _h.Grants.RootNames[_matter] = "Smith v. Smith";
    }

    // ── Worlds ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The matter is an ordinary record owned in the CHILD business unit.</summary>
    private void ChildBuMatter(OwnershipDirectory? directory = null) =>
        _h.Ownership = (directory ?? OwnershipDirectory.Standard()).WithOrdinaryRoot("sprk_matter", _matter).Resolver();

    /// <summary>The matter is a secure record, isolated in the Secure Record business unit.</summary>
    private void SecureMatter()
    {
        _h.Ownership = OwnershipDirectory.Standard().WithSecureRoot("sprk_matter", _matter).Resolver();
        _h.Participations.Flags[_matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
    }

    /// <summary>Every directory read throws — a Dataverse fault, which the resolver propagates.</summary>
    private void OwnerReadFaults() =>
        _h.Ownership = OwnershipDirectory.Standard().WithOrdinaryRoot("sprk_matter", _matter)
            .Resolver(fault: new HttpRequestException("Simulated Dataverse 503 on the owner read."));

    // ── Routes ───────────────────────────────────────────────────────────────────────────────────────────────

    private static HttpContext Context() => new DefaultHttpContext
    {
        TraceIdentifier = "trace-099b",
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", "0a0a0a0a-1111-2222-3333-444444444444"), new Claim("name", "Gina Granter") }, "Test")),
    };

    private Task<IResult> Grant(Guid contactId, Guid? organizationId = null) =>
        GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contactId, Guid.Empty, ExternalAccessLevel.Collaborate, null, organizationId, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, _h.Ownership, _h.OwnerPrivileges, new EverythingProbe(),
            _h.Materializer, _h.Notifier, _h.Invites, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

    private Task<IResult> InviteAndGrant() =>
        InviteAndGrantExternalUserEndpoint.InviteAndGrantAsync(
            new InviteExternalUserRequest(Email, Guid.Empty, (int)ExternalAccessLevel.Collaborate, "Pat", "Partner", null, null,
                "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, _h.Ownership, _h.OwnerPrivileges, new EverythingProbe(),
            _h.Ciam, _h.Emails, IdentityBinding.IdentityBindingTestKit.Binder(_h.Identities),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = "https://external.test" })
                .Build(),
            TestSpeOwnership.Customer("acme"),
            _h.Materializer, _h.Notifier, Context(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

    private Task<AssignedAccessOutcome> Sync() => _h.SyncAsync(Matter, _matter);

    // ── Assertions ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The <c>ownerid@odata.bind</c> of the ONE grant row created.</summary>
    private string OwnerOfTheOneCreatedRow()
    {
        var create = _h.Grants.Creates.Should().ContainSingle("exactly one grant row is created").Subject;
        create.Set.Should().Be("sprk_externalrecordaccesses");
        using var payload = JsonDocument.Parse(create.Payload);
        payload.RootElement.TryGetProperty("ownerid@odata.bind", out var owner)
            .Should().BeTrue("a grant row is never created without its owner (D-11) — absent, it is the BFF identity's");
        return owner.GetString()!;
    }

    private static string Team(Guid teamId) => $"/teams({teamId:D})";

    private static void AssertTeamOwnedNeverAnApplicationUser(string ownerBind)
    {
        ownerBind.Should().StartWith("/teams(", "a grant row is team-owned, never a user (D-11)");
        ownerBind.Should().NotContain("systemusers", "never the BFF application user");
    }

    private static (int Status, string? ReasonCode) Problem(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.ProblemDetails.Detail.Should().NotBeNullOrWhiteSpace();
        return (problem.StatusCode,
            problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var code) ? code as string : null);
    }

    // =========================================================================================================
    // /grant — a contact grantee
    // =========================================================================================================

    [Fact]
    public async Task Grant_OnAChildBusinessUnitRecord_IsOwnedByThatUnitsDefaultOwnerTeam()
    {
        ChildBuMatter();

        var result = await Grant(_h.Contact());

        result.Should().BeOfType<Ok<GrantAccessResponse>>();
        var owner = OwnerOfTheOneCreatedRow();
        owner.Should().Be(Team(OwnershipDirectory.ChildTeam), "record-first: the matter's business unit's default Owner team");
        AssertTeamOwnedNeverAnApplicationUser(owner);
    }

    [Fact]
    public async Task Grant_OnASecureRecord_IsOwnedBySecureRecordOwners_NeverTheSecureUnitsDefaultTeam()
    {
        SecureMatter();

        var result = await Grant(_h.Contact());

        result.Should().BeOfType<Ok<GrantAccessResponse>>();
        var owner = OwnerOfTheOneCreatedRow();
        owner.Should().Be(Team(OwnershipDirectory.SecureNamedTeam), "a secure record's grants go to the named Secure Record Owners team");
        owner.Should().NotBe(Team(OwnershipDirectory.SecureDefaultTeam), "never the Secure Record BU's default team (task 144)");
        AssertTeamOwnedNeverAnApplicationUser(owner);
    }

    [Fact]
    public async Task Grant_WhenTheOwnerReadFaults_Is503OwnerUnreadable_AndWritesNoRow()
    {
        OwnerReadFaults();

        var result = await Grant(_h.Contact());

        Problem(result).Should().Be((StatusCodes.Status503ServiceUnavailable, ExternalGrantLifecycle.GrantOwnerUnreadableReasonCode));
        _h.Grants.Creates.Should().BeEmpty("no row is written when its owner could not be read — never an application-owned row");
        _h.Grants.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_WhenTheUnitHasNoDefaultOwnerTeam_Is500OwnerUnresolved_AndWritesNoRow()
    {
        ChildBuMatter(OwnershipDirectory.Standard(withDefaultTeams: false));

        var result = await Grant(_h.Contact());

        Problem(result).Should().Be((StatusCodes.Status500InternalServerError, ExternalGrantLifecycle.GrantOwnerUnresolvedReasonCode));
        _h.Grants.Creates.Should().BeEmpty("no team resolved, so nothing is written — never an application-owned row");
    }

    [Fact]
    public async Task Grant_OnARecordFlaggedSecureButNotIsolated_Is500OwnerUnresolved_AndWritesNoRow()
    {
        // Flagged sprk_issecure but owned in the child unit (an interrupted provisioning): the resolver refuses rather than
        // hand the grant to an ordinary team (task 146 C11).
        _h.Ownership = OwnershipDirectory.Standard()
            .WithRecord("sprk_matter", _matter, OwnershipDirectory.ChildBu, isSecure: true, owningTeam: OwnershipDirectory.ChildTeam)
            .Resolver();

        var result = await Grant(_h.Contact());

        Problem(result).Should().Be((StatusCodes.Status500InternalServerError, ExternalGrantLifecycle.GrantOwnerUnresolvedReasonCode));
        _h.Grants.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task ReGrant_OnAnExistingRow_ChangesTheLevelInPlace_WithoutAskingForAnOwner()
    {
        // The owner is decided on CREATE only; an update in place never moves a row's owner (the backfill re-owns old rows).
        // So a resolver fault does not block a level change on an existing row.
        var contact = _h.Contact();
        var existing = _h.Grants.Seed(Matter, _matter, contact, null, (int)ExternalAccessLevel.ViewOnly, Today.AddDays(30));
        OwnerReadFaults();

        var result = await Grant(contact);

        result.Should().BeOfType<Ok<GrantAccessResponse>>();
        _h.Grants.Creates.Should().BeEmpty();
        _h.Grants.Updates.Should().ContainSingle(u => u.Id == existing.Id);
    }

    // =========================================================================================================
    // /grant — an organization grantee (org grants reach the same core)
    // =========================================================================================================

    [Fact]
    public async Task OrganizationGrant_OnAChildBusinessUnitRecord_IsOwnedByThatUnitsDefaultOwnerTeam()
    {
        ChildBuMatter();

        var result = await Grant(Guid.Empty, organizationId: Guid.NewGuid());

        result.Should().BeOfType<Ok<GrantAccessResponse>>();
        OwnerOfTheOneCreatedRow().Should().Be(Team(OwnershipDirectory.ChildTeam));
    }

    [Fact]
    public async Task OrganizationGrant_WhenTheOwnerReadFaults_Is503_AndWritesNoRow()
    {
        OwnerReadFaults();

        var result = await Grant(Guid.Empty, organizationId: Guid.NewGuid());

        Problem(result).Should().Be((StatusCodes.Status503ServiceUnavailable, ExternalGrantLifecycle.GrantOwnerUnreadableReasonCode));
        _h.Grants.Creates.Should().BeEmpty();
    }

    // =========================================================================================================
    // /invite-and-grant
    // =========================================================================================================

    [Fact]
    public async Task InviteAndGrant_OnAChildBusinessUnitRecord_IsOwnedByThatUnitsDefaultOwnerTeam()
    {
        ChildBuMatter();
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = Email;

        var result = await InviteAndGrant();

        result.Should().BeOfType<Ok<InviteAndGrantResponse>>();
        var owner = OwnerOfTheOneCreatedRow();
        owner.Should().Be(Team(OwnershipDirectory.ChildTeam));
        AssertTeamOwnedNeverAnApplicationUser(owner);
    }

    [Fact]
    public async Task InviteAndGrant_WhenTheOwnerReadFaults_Is503_AndWritesNoGrantRow()
    {
        OwnerReadFaults();
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = Email;

        var result = await InviteAndGrant();

        Problem(result).Should().Be((StatusCodes.Status503ServiceUnavailable, ExternalGrantLifecycle.GrantOwnerUnreadableReasonCode));
        _h.Grants.Creates.Should().BeEmpty("no grant row is written; the onboarding is idempotent, so a retry re-issues only the grant");
    }

    // Review round 1, F2: a NEW person's owner is resolved BEFORE onboarding: "a refused request must leave no Contact and no
    // CIAM account behind" (InviteAndGrantExternalUserEndpoint). Before the fix the owner was decided in the core, after the
    // Contact, the CIAM account and the onboarding email.

    [Fact]
    public async Task InviteAndGrant_ANewPerson_WhenNoOwnerTeamResolves_Is500OwnerUnresolved_AndOnboardsNothing()
    {
        ChildBuMatter(OwnershipDirectory.Standard(withDefaultTeams: false));
        _h.Grants.AcceptContactCreates = true; // the email matches no contact: onboarding WOULD create one

        var result = await InviteAndGrant();

        Problem(result).Should().Be((StatusCodes.Status500InternalServerError, ExternalGrantLifecycle.GrantOwnerUnresolvedReasonCode));
        AssertNothingOnboardedOrGranted();
    }

    [Fact]
    public async Task InviteAndGrant_ANewPerson_WhenTheOwnerReadFaults_Is503OwnerUnreadable_AndOnboardsNothing()
    {
        OwnerReadFaults();
        _h.Grants.AcceptContactCreates = true;

        var result = await InviteAndGrant();

        Problem(result).Should().Be((StatusCodes.Status503ServiceUnavailable, ExternalGrantLifecycle.GrantOwnerUnreadableReasonCode));
        AssertNothingOnboardedOrGranted();
    }

    [Fact]
    public async Task InviteAndGrant_AnExistingContact_ResolvesTheOwnerOnce_BeforeOnboarding_AndTheCoreReusesIt()
    {
        ChildBuMatter();
        var counting = new CountingResolver(_h.Ownership);
        _h.Ownership = counting;
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = Email;

        var result = await InviteAndGrant();

        result.Should().BeOfType<Ok<InviteAndGrantResponse>>();
        OwnerOfTheOneCreatedRow().Should().Be(Team(OwnershipDirectory.ChildTeam));
        counting.ResolveOwnerCalls.Should().Be(1, "the owner resolved before onboarding is handed to the core, not resolved twice");
    }

    // Review round 2 (F1): the resolved team must also be ALLOWED to own the row. On a secure record, before the Secure Record
    // Owner role holds prvReadsprk_ExternalRecordAccess, Dataverse refuses the create ("Read Privilege Check For Owner
    // failed") — and before the fix that refusal came after the Contact, the CIAM account and the email.

    [Fact]
    public async Task InviteAndGrant_ANewPerson_WhenTheOwnerTeamLacksRead_Is500OwnerPrivilegeMissing_AndOnboardsNothing()
    {
        SecureMatter();
        _h.OwnerPrivileges = TeamPrivilegeCheckerDouble.Lacking(OwnershipDirectory.SecureNamedTeam);
        _h.Grants.GrantCreateFailure = OwnerPrivilegeRefusal(); // what Dataverse would answer to the create
        _h.Grants.AcceptContactCreates = true; // the email matches no contact: onboarding WOULD create one

        var result = await InviteAndGrant();

        Problem(result).Should().Be((StatusCodes.Status500InternalServerError, ExternalGrantLifecycle.GrantOwnerPrivilegeMissingReasonCode));
        AssertNothingOnboardedOrGranted();
        _h.OwnerPrivileges.TeamQuestions.Should().ContainSingle().Which.Should().Be(
            (OwnershipDirectory.SecureNamedTeam, "sprk_externalrecordaccess", false),
            "the resolved owner team is asked, read-only, before any write (a cached YES may answer it)");
    }

    [Fact]
    public async Task InviteAndGrant_ANewPerson_WhenTheOwnerTeamsPrivilegesCannotBeRead_Is503OwnerUnreadable_AndOnboardsNothing()
    {
        SecureMatter();
        _h.OwnerPrivileges = new TeamPrivilegeCheckerDouble { Fault = new HttpRequestException("Simulated Dataverse 503.") };
        _h.Grants.AcceptContactCreates = true;

        var result = await InviteAndGrant();

        Problem(result).Should().Be((StatusCodes.Status503ServiceUnavailable, ExternalGrantLifecycle.GrantOwnerUnreadableReasonCode));
        AssertNothingOnboardedOrGranted();
    }

    [Fact]
    public async Task InviteAndGrant_OnASecureRecord_WhenTheOwnerTeamHoldsRead_GrantsAndOwnsTheRowBySecureRecordOwners()
    {
        // The twin: a team that holds Read is not refused.
        SecureMatter();
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = Email;

        var result = await InviteAndGrant();

        result.Should().BeOfType<Ok<InviteAndGrantResponse>>();
        OwnerOfTheOneCreatedRow().Should().Be(Team(OwnershipDirectory.SecureNamedTeam));
        _h.OwnerPrivileges.TeamQuestions.Should().ContainSingle().Which.TeamId.Should().Be(OwnershipDirectory.SecureNamedTeam);
    }

    private void AssertNothingOnboardedOrGranted()
    {
        _h.Grants.ContactCreates.Should().BeEmpty("a refused request leaves no Contact behind");
        _h.Ciam.Attempts.Should().BeEmpty("a refused request leaves no CIAM account behind");
        _h.Emails.Onboarding.Should().BeEmpty("no onboarding email for a refused request");
        _h.Emails.ExistingAccount.Should().BeEmpty();
        _h.Emails.Sent.Should().BeEmpty();
        _h.Identities.Writes.Should().BeEmpty("no invite marker and no binding is written");
        _h.Grants.Creates.Should().BeEmpty("no grant row");
    }

    // =========================================================================================================
    // Review round 1: Dataverse refuses the owner on a privilege check (the owner team's role lacks Read on the table)
    // =========================================================================================================

    [Fact]
    public async Task Grant_WhenDataverseRefusesTheOwnerOnAPrivilegeCheck_Is500OwnerPrivilegeMissing_NotTheGenericFailure()
    {
        SecureMatter();
        _h.OwnerPrivileges = TeamPrivilegeCheckerDouble.Lacking(OwnershipDirectory.SecureNamedTeam); // review round 2: confirmed
        _h.Grants.GrantCreateFailure = OwnerPrivilegeRefusal();

        var result = await Grant(_h.Contact());

        Problem(result).Should().Be((StatusCodes.Status500InternalServerError, ExternalGrantLifecycle.GrantOwnerPrivilegeMissingReasonCode));
        result.As<ProblemHttpResult>().ProblemDetails.Detail.Should().Contain("Read on External Record Access",
            "the detail tells an administrator what to fix, not the generic 'Failed to create external access record'");
        _h.OwnerPrivileges.TeamQuestions.Should().ContainSingle().Which.Fresh.Should().BeTrue(
            "the classifier reads the team's privileges fresh (review round 3, K2)");
        _h.Grants.Creates.Should().BeEmpty();
    }

    // Review round 2 (K1 -> fix): a bare 403 names no privilege. It is owner_privilege_missing only when the owner team really
    // lacks Read; otherwise the generic 500, as before 099b.

    [Fact]
    public async Task Grant_WhenTheCreateIsRefused403_ButTheOwnerTeamHoldsRead_IsTheGeneric500_NotOwnerPrivilegeMissing()
    {
        SecureMatter(); // the team holds Read (the double's default)
        _h.Grants.GrantCreateFailure = OwnerPrivilegeRefusal();

        var result = await Grant(_h.Contact());

        Problem(result).Should().Be((StatusCodes.Status500InternalServerError, null));
        result.As<ProblemHttpResult>().ProblemDetails.Detail.Should().Be("Failed to create external access record in Dataverse.");
        _h.OwnerPrivileges.TeamQuestions.Should().ContainSingle().Which.Should().Be(
            (OwnershipDirectory.SecureNamedTeam, "sprk_externalrecordaccess", true),
            "the 403 is confirmed by asking the owner team FRESH (review round 3, K2): a cached YES is not a confirmation");
        _h.Grants.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_WhenTheCreateIsRefused403_AndTheOwnerTeamsPrivilegesCannotBeRead_IsTheGeneric500()
    {
        SecureMatter();
        _h.OwnerPrivileges = new TeamPrivilegeCheckerDouble { Fault = new HttpRequestException("Simulated Dataverse 503.") };
        _h.Grants.GrantCreateFailure = OwnerPrivilegeRefusal();

        var result = await Grant(_h.Contact());

        Problem(result).Should().Be((StatusCodes.Status500InternalServerError, null), "an unconfirmed 403 is not named");
        _h.Grants.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_WhenTheCreateFailsForAnotherReason_IsStillTheGeneric500()
    {
        // The twin: a fault that is not a privilege refusal is not classified as the owner's privilege.
        ChildBuMatter();
        _h.Grants.GrantCreateFailure = new HttpRequestException("Simulated 500.", null, System.Net.HttpStatusCode.InternalServerError);

        var result = await Grant(_h.Contact());

        Problem(result).Should().Be((StatusCodes.Status500InternalServerError, null));
        result.As<ProblemHttpResult>().ProblemDetails.Detail.Should().Be("Failed to create external access record in Dataverse.");
    }

    [Fact]
    public async Task AssignedToGrant_WhenDataverseRefusesTheOwnerOnAPrivilegeCheck_IsAFailure_AndIsNotRecordedGranted()
    {
        ChildBuMatter();
        _h.OwnerPrivileges = TeamPrivilegeCheckerDouble.Lacking(OwnershipDirectory.ChildTeam);
        _h.Grants.GrantCreateFailure = OwnerPrivilegeRefusal();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Sync();

        outcome.Failures.Should().ContainSingle().Which.Kind.Should().Be("subject-failed");
        _h.Store.RowsOf(_matter, contact).Should().NotContain(r => r.State == AssignedAccessState.Granted);
    }

    /// <summary>What <c>DataverseWebApiClient.CreateAsync</c> throws on Dataverse's 403 "Read Privilege Check For Owner failed"
    /// (0x80040299): <c>EnsureSuccessStatusCode</c>'s bare exception, status only.</summary>
    private static HttpRequestException OwnerPrivilegeRefusal() =>
        new("Response status code does not indicate success: 403 (Forbidden).", null, System.Net.HttpStatusCode.Forbidden);

    // =========================================================================================================
    // The Assigned-To materializer (a background writer — no exception for the rule's own grants)
    // =========================================================================================================

    [Fact]
    public async Task AssignedToGrant_OnAChildBusinessUnitRecord_IsOwnedByThatUnitsDefaultOwnerTeam()
    {
        ChildBuMatter();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Sync();

        outcome.Complete.Should().BeTrue();
        var owner = OwnerOfTheOneCreatedRow();
        owner.Should().Be(Team(OwnershipDirectory.ChildTeam));
        AssertTeamOwnedNeverAnApplicationUser(owner);
    }

    [Fact]
    public async Task AssignedToGrant_WhenTheOwnerReadFaults_IsAFailure_WritesNoRow_AndIsNotRecordedGranted()
    {
        OwnerReadFaults();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Sync();

        outcome.Complete.Should().BeFalse("a grant whose owner could not be resolved is reported, like a failed grant create");
        outcome.Failures.Should().ContainSingle().Which.Kind.Should().Be("subject-failed");
        _h.Grants.Creates.Should().BeEmpty("never an application-owned row");
        _h.Store.RowsOf(_matter, contact).Should().NotContain(r => r.State == AssignedAccessState.Granted);
    }

    [Fact]
    public async Task AssignedToGrant_WhenNoTeamResolves_IsAFailure_AndWritesNoRow()
    {
        ChildBuMatter(OwnershipDirectory.Standard(withDefaultTeams: false));
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        var outcome = await Sync();

        outcome.Failures.Should().ContainSingle().Which.Kind.Should().Be("subject-failed");
        _h.Grants.Creates.Should().BeEmpty();
        _h.Store.RowsOf(_matter, contact).Should().NotContain(r => r.State == AssignedAccessState.Granted);
    }

    [Fact]
    public async Task AssignedToRenewal_ThatReachesACreate_WhenTheOwnerReadFaults_IsAFailure_NotOnlyLogged()
    {
        // Review round 1, K2: a renewal (owner A5) whose row vanished between the rule's read and the core's goes to a CREATE,
        // which needs an owner. An owner refusal there is reported as a failure, the same as on the grant path.
        ChildBuMatter();
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        (await Sync()).Complete.Should().BeTrue();
        var granted = _h.Grants.ActiveRowsOf(_matter, contact).Single();
        _h.Time.Now = _h.Time.Now.AddDays(70); // 20 days left: inside the 30-day renewal window
        OwnerReadFaults();
        var reads = 0;
        _h.Grants.BeforeGrantQuery = _ =>
        {
            // Read 1 is the rule's (the row is there, so it renews); read 2 is the grant core's: the row is gone by then.
            if (++reads == 2)
                granted.StateCode = 1;
        };

        var outcome = await _h.SyncAsync(Matter, _matter, trigger: AssignedAccessTrigger.Job);

        outcome.Complete.Should().BeFalse("a renewal whose owner could not be resolved is a failure, never only a log line");
        outcome.Failures.Should().ContainSingle().Which.Kind.Should().Be("subject-failed");
        _h.Grants.Creates.Should().ContainSingle("only the original grant: the renewal wrote no application-owned row");
    }

    /// <summary>Counts <c>ResolveOwnerAsync</c> calls over an inner resolver.</summary>
    private sealed class CountingResolver : Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver
    {
        private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _inner;

        public CountingResolver(Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver inner) => _inner = inner;

        public int ResolveOwnerCalls { get; private set; }

        public Task<Guid?> ResolveOwningTeamAsync(Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext context, CancellationToken ct)
            => _inner.ResolveOwningTeamAsync(context, ct);

        public Task<Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution> ResolveOwnerAsync(
            Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext context, CancellationToken ct)
        {
            ResolveOwnerCalls++;
            return _inner.ResolveOwnerAsync(context, ct);
        }

        public Task<Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution> ReparentAsync(
            Sprk.Bff.Api.Services.Dataverse.RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct)
            => _inner.ReparentAsync(request, applyChange, ct);
    }

    /// <summary>A caller who holds every right on the record, so the grantor ceiling passes.</summary>
    private sealed class EverythingProbe : CallerRecordAccessProbe
    {
        public EverythingProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo
                               | AccessRights.Share | AccessRights.Delete);
    }
}
