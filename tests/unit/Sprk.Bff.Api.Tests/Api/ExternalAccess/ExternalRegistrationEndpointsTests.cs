using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// r3 task 047 — <c>POST /api/v1/external/registration</c> (ADR-028 A9; design §4 and §6) through the real route, the real
/// 046 resolver, the real binder and decision, over the in-memory row store (ADR-038: no HTTP doubles). The test scheme
/// stamps the identity's <c>AuthenticationType</c> from a header, so the WorkforceHome-only rule is exercised for real.
/// </summary>
public sealed class ExternalRegistrationEndpointsTests : IClassFixture<ExternalRegistrationEndpointsTests.ConfiguredFixture>
{
    private const string Route = "/api/v1/external/registration";
    private const string Email = "mike.okafor@customer.example";

    internal static readonly Guid BusinessUnit = Guid.Parse("b0b0b0b0-0000-4000-8000-b0b0b0b0b0b0");
    internal static readonly Guid OwnerTeam = Guid.Parse("7ea70000-0000-4000-8000-7ea700007ea7");
    private static readonly Guid HomeTenant = Guid.Parse("99999999-eeee-4eee-8eee-999999999999");

    private readonly ConfiguredFixture _fixture;
    private readonly InMemoryContactIdentityStore _store;
    private readonly Guid _oid = Guid.NewGuid();

    public ExternalRegistrationEndpointsTests(ConfiguredFixture fixture)
    {
        _fixture = fixture;
        _store = fixture.Store;
        _store.Reset();
        _store.DefaultTeams[BusinessUnit] = new List<Guid> { OwnerTeam };
        fixture.Identity.Reset();
    }

    // ── Create ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoMatch_Creates_OneWorkforceHomeContact_OwnedByTheBusinessUnitDefaultTeam()
    {
        var response = await Post(Body("  Mike ", " Okafor  ", " Legal "));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var contactId = await ContactIdOf(response);
        var created = _store.Contacts[contactId];
        created.Email.Should().Be(Email, "the email comes from the token");
        created.ExternalTenantId.Should().Be(HomeTenant.ToString("D"));
        created.Oid.Should().Be(_oid.ToString("D"));
        created.KeyMirror.Should().Be(_oid.ToString("D"));
        created.Plane.Should().Be(ContactBindingDecision.WorkforceHomePlaneValue);
        created.OwnerTeamId.Should().Be(OwnerTeam);
        created.FirstName.Should().Be("Mike");
        created.LastName.Should().Be("Okafor");
        created.Department.Should().Be("Legal");
        _store.Writes.Select(w => w.Op).Should().Equal("create");
        _store.Reads.Should().Contain(new[] { "probe", "home-probe", "licensed-email", "email", "owner-team" });
    }

    // ── Already resolved / idempotency ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlreadyResolved_ByItsWorkforceHomeContact_Returns200_AndWritesNothing()
    {
        var existing = AddHomeContact(Guid.NewGuid());

        var response = await Post(Body());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ContactIdOf(response)).Should().Be(existing.Id);
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().NotContain(new[] { "probe", "licensed-email", "email" }, "step 1 answers before any registration step");
    }

    [Fact]
    public async Task AlreadyResolved_ByAStoredSystemUserLink_Returns200_AndWritesNothing()
    {
        var suid = Guid.NewGuid();
        var linkedContact = Guid.NewGuid();
        var user = _store.AddSystemUser(suid, Guid.NewGuid(), "licensed@spaarke.example");
        user.HomeTenantId = user.HomeTenantIdKey = HomeTenant.ToString("D");
        user.HomeObjectId = user.HomeObjectIdKey = _oid.ToString("D");
        _fixture.Identity.Setup(x => x.ResolveAsync(suid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(suid, ContactId: linkedContact));

        var response = await Post(Body());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await Json(response);
        json.GetProperty("systemUserId").GetGuid().Should().Be(suid);
        json.GetProperty("outcome").GetString().Should().Be("resolved");
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Resubmit_Returns200_WithTheSameContact_AndExactlyOneContactExists()
    {
        var first = await Post(Body());
        var second = await Post(Body());

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ContactIdOf(second)).Should().Be(await ContactIdOf(first));
        _store.ContactsBoundTo(_oid).Should().ContainSingle();
        _store.Writes.Count(w => w.Op == "create").Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentLoser_TheKeys412_ReReads_Returns200_WithTheWinnersContact()
    {
        Guid winner = Guid.Empty;
        _store.BeforeCreate = _ =>
        {
            winner = AddHomeContact(Guid.NewGuid()).Id; // a concurrent submit lands first
            return Task.CompletedTask;
        };

        var response = await Post(Body());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ContactIdOf(response)).Should().Be(winner);
        _store.ContactsBoundTo(_oid).Should().ContainSingle();
        _store.Writes.Should().NotContain(w => w.Op == "create");
    }

    // ── Licensed user (step 2) ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("internal")]
    [InlineData("domainname")]
    public async Task TokenEmail_OfAnEnabledSystemUser_InEitherField_Is403LicensedUserLinkRequired_NoWrite(string field)
    {
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(),
            email: field == "internal" ? Email.ToUpperInvariant() : "someone.else@customer.example",
            domainName: field == "domainname" ? Email.ToUpperInvariant() : "upn@customer.example");

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalRegistrationEndpoints.ReasonLicensedUserLinkRequired);
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task TokenEmail_OfADisabledSystemUser_IsNotRefused_AndRegisters()
    {
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), Email, disabled: true);

        var response = await Post(Body());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Existing contact (step 3) ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OneBindableContact_WithConfirmation_IsBound_NoNewContact()
    {
        var existing = _store.AddContact(Guid.NewGuid(), email: Email);

        var response = await Post(Body(confirm: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ContactIdOf(response)).Should().Be(existing.Id);
        existing.Oid.Should().Be(_oid.ToString("D"));
        existing.KeyMirror.Should().Be(_oid.ToString("D"));
        existing.ExternalTenantId.Should().Be(HomeTenant.ToString("D"));
        existing.Plane.Should().Be(ContactBindingDecision.WorkforceHomePlaneValue);
        _store.Contacts.Should().ContainSingle();
        _store.Writes.Select(w => w.Op).Should().Equal("bind");
    }

    /// <summary>
    /// UAC review F1 (task 185's invariant): a bound contact never holds an unused invite-code hash. A contact with a pending
    /// invite that the registration binds ends with the hash cleared (so an operator unbind later cannot reopen the
    /// forwarded invite link). The marker is left as it was.
    /// </summary>
    [Fact]
    public async Task OneBindableContact_WithAPendingInvite_WithConfirmation_IsBound_AndTheInviteCodeHashIsCleared()
    {
        var invitedOn = DateTimeOffset.Parse("2026-10-14T09:30:00Z");
        var existing = _store.AddContact(Guid.NewGuid(), email: Email);
        existing.InvitedOn = invitedOn;
        existing.InviteCodeHash = "pending-invite-code-hash";

        var response = await Post(Body(confirm: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        existing.Oid.Should().Be(_oid.ToString("D"));
        existing.InviteCodeHash.Should().BeNull("a bound contact must not keep an unused invite-code hash");
        existing.InvitedOn.Should().Be(invitedOn, "only the hash is consumed; the marker is untouched");
        _store.Writes.Select(w => w.Op).Should().Equal("bind");
    }

    [Fact]
    public async Task OneBindableContact_WithoutConfirmation_Is409ExistingRecordFound_NoWrite()
    {
        var existing = _store.AddContact(Guid.NewGuid(), email: Email);

        var response = await Post(Body(confirm: null));

        await ShouldBeProblem(response, HttpStatusCode.Conflict, ExternalRegistrationEndpoints.ReasonExistingRecordFound);
        _store.Writes.Should().BeEmpty();
        existing.Oid.Should().BeNull();
    }

    [Theory]
    [InlineData("two", IdentityCollisionReason.EmailAmbiguous)]
    [InlineData("bound", IdentityCollisionReason.BoundToDifferentOid)]
    [InlineData("linked", IdentityCollisionReason.LinkedToOtherUser)]
    [InlineData("external", IdentityCollisionReason.BoundToDifferentOid)]
    [InlineData("workforcehome-other", IdentityCollisionReason.BoundToDifferentOid)]
    [InlineData("unreadable", IdentityCollisionReason.BindingUnreadable)]
    public async Task UnbindableMatch_Is403RegistrationContactConflict_FlaggedByCause_NothingBoundOrCreated(
        string shape, IdentityCollisionReason expectedReason)
    {
        var a = _store.AddContact(Guid.NewGuid(), email: Email);
        var flagged = new List<InMemoryContactIdentityStore.Contact> { a };
        switch (shape)
        {
            case "two":
                flagged.Add(_store.AddContact(Guid.NewGuid(), email: Email));
                break;
            case "bound":
                a.Oid = a.KeyMirror = Guid.NewGuid().ToString("D");
                a.Plane = (int)IdentityPlaneMarker.Workforce;
                break;
            case "linked":
                _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), "lawyer@spaarke.example", primaryContactId: a.Id);
                break;
            case "external":
                a.Oid = a.KeyMirror = Guid.NewGuid().ToString("D");
                a.Plane = (int)IdentityPlaneMarker.External;
                break;
            case "workforcehome-other":
                // A WorkforceHome contact bound to a DIFFERENT home oid (its tenant is readable, so the probe passes).
                a.Oid = a.KeyMirror = Guid.NewGuid().ToString("D");
                a.Plane = (int)IdentityPlaneMarker.WorkforceHome;
                a.ExternalTenantId = HomeTenant.ToString("D");
                break;
            case "unreadable":
                // A plane marker with no oid is unreadable. A second marked contact keeps the masking probe Readable.
                a.Oid = a.KeyMirror = null;
                a.Plane = (int)IdentityPlaneMarker.Workforce;
                _store.AddContact(Guid.NewGuid(), oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.Workforce,
                    email: "someone.else@customer.example");
                break;
        }

        var contactsBefore = _store.Contacts.Count;
        var response = await Post(Body(confirm: true));

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalRegistrationEndpoints.ReasonContactConflict);
        flagged.Should().AllSatisfy(c => c.Flag!.Reason.Should().Be(expectedReason));
        _store.Writes.Select(w => w.Op).Should().OnlyContain(op => op == "flag");
        _store.Contacts.Count.Should().Be(contactsBefore);
        _store.ContactsBoundTo(_oid).Should().BeEmpty();
    }

    // ── A deactivated contact carries the email (fix round 2; owner 2026-10-15 "Refuse, flag for admin") ──────────

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task InactiveOnlyMatch_Is403RegistrationContactConflict_FlagsTheInactiveContact_NothingCreatedOrBound(
        bool confirm, bool twoInactive)
    {
        // The match is case-insensitive (the token email is not normalised); confirmation never turns a refusal into a bind.
        var inactive = _store.AddContact(Guid.NewGuid(), email: Email.ToUpperInvariant(), stateCode: 1);
        var others = twoInactive
            ? new[] { _store.AddContact(Guid.NewGuid(), email: Email, stateCode: 1) }
            : Array.Empty<InMemoryContactIdentityStore.Contact>();

        var response = await Post(Body(confirm: confirm));

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalRegistrationEndpoints.ReasonContactConflict);
        foreach (var c in others.Append(inactive))
        {
            c.Flag!.Reason.Should().Be(IdentityCollisionReason.InactiveContactEmailMatch);
            c.Flag.CollidingOid.Should().Be(_oid);
            c.Flag.CollidingPlane.Should().Be(IdentityPlaneMarker.WorkforceHome);
            c.StateCode.Should().Be(1, "reactivation is the operator's decision");
            c.Oid.Should().BeNull("the inactive contact is never bound");
        }

        _store.Writes.Select(w => w.Op).Should().OnlyContain(op => op == "flag");
        _store.Contacts.Count.Should().Be(1 + others.Length, "no new contact beside the deactivated one");
        _store.ContactsBoundTo(_oid).Should().BeEmpty();
        _store.Reads.Should().Contain("inactive-email");
    }

    [Fact]
    public async Task InactiveAndOneActiveBindableMatch_ActiveDecides_AsToday_InactiveIsLeftAlone()
    {
        var inactive = _store.AddContact(Guid.NewGuid(), email: Email, stateCode: 1);
        var active = _store.AddContact(Guid.NewGuid(), email: Email);

        var ask = await Post(Body(confirm: null));
        await ShouldBeProblem(ask, HttpStatusCode.Conflict, ExternalRegistrationEndpoints.ReasonExistingRecordFound);
        _store.Writes.Should().BeEmpty();

        var bind = await Post(Body(confirm: true));

        bind.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ContactIdOf(bind)).Should().Be(active.Id);
        active.Oid.Should().Be(_oid.ToString("D"));
        inactive.Flag.Should().BeNull();
        inactive.Oid.Should().BeNull();
        _store.Reads.Should().NotContain("inactive-email", "an active match decides; the inactive read is not reached");
        _store.Writes.Select(w => w.Op).Should().Equal("bind");
    }

    [Fact]
    public async Task InactiveAndOneActiveBoundMatch_ConflictsByTheActiveCause_NotTheInactiveOne()
    {
        var inactive = _store.AddContact(Guid.NewGuid(), email: Email, stateCode: 1);
        var active = _store.AddContact(Guid.NewGuid(), email: Email,
            oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.Workforce);

        var response = await Post(Body(confirm: true));

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalRegistrationEndpoints.ReasonContactConflict);
        active.Flag!.Reason.Should().Be(IdentityCollisionReason.BoundToDifferentOid);
        inactive.Flag.Should().BeNull();
    }

    [Fact]
    public async Task InactiveMatchReadFailed_Is503Unavailable_NothingCreated()
    {
        _store.AddContact(Guid.NewGuid(), email: Email, stateCode: 1);
        _store.FailInactiveEmailLookup = true;

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.ServiceUnavailable, ExternalRegistrationEndpoints.ErrorUnavailable);
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Should().ContainSingle();
    }

    [Fact]
    public async Task InactiveMatchOnADifferentEmail_StillRegisters()
    {
        _store.AddContact(Guid.NewGuid(), email: "someone.else@customer.example", stateCode: 1);

        var response = await Post(Body());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task InactiveMatch_WhenTheFlagOptionIsNotProvisioned_StillRefuses403_NothingCreated_AndWarnsNamingTheSchemaScript()
    {
        // Dataverse rejects a reason value its choice lacks (an invalid option value): the flag write FAILS. The refusal must
        // not depend on it, and the operator must learn which script adds the option.
        var inactive = _store.AddContact(Guid.NewGuid(), email: Email, stateCode: 1);
        _store.FailFlagWrites = true;
        _store.FlagWriteError = "400: {\"error\":{\"message\":\"The value 100000012 of 'sprk_identitycollisionreason' is outside the valid range.\"}}";
        var log = new BindingLogCapture<ContactIdentityBinder>();
        var binder = IdentityBindingTestKit.Binder(_store, log);

        // Through the REAL route (the same store, nothing written) ...
        var response = await Post(Body());
        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalRegistrationEndpoints.ReasonContactConflict);

        // ... and through a binder whose log is captured (the route's logger is not).
        var result = await binder.RegisterHomeContactAsync(
            new HomeRegistrationRequest(HomeTenant, _oid, Email, "Mike", "Okafor", null, false, BusinessUnit, "Secure Record"),
            CancellationToken.None);
        result.Outcome.Should().Be(HomeRegistrationOutcome.Conflict);
        result.Wrote.Should().BeFalse();
        inactive.Flag.Should().BeNull("the flag could not be written");
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Should().ContainSingle();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning
            && e.Message.Contains("scripts/Set-ContactIdentityBindingSchema.ps1")
            && e.Message.Contains("InactiveContactEmailMatch"));
    }

    [Fact]
    public async Task InactiveMatch_WhenTheFlagWriteFailsForAnotherReason_StillRefuses403_ButGivesNoSchemaHint()
    {
        // The other side: a throttle or a 5xx on the flag write is not a schema fault, so the schema script is not blamed.
        var inactive = _store.AddContact(Guid.NewGuid(), email: Email, stateCode: 1);
        _store.FailFlagWrites = true;
        _store.FlagWriteError = "503: service unavailable";
        var log = new BindingLogCapture<ContactIdentityBinder>();
        var binder = IdentityBindingTestKit.Binder(_store, log);

        var response = await Post(Body());
        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalRegistrationEndpoints.ReasonContactConflict);
        var result = await binder.RegisterHomeContactAsync(
            new HomeRegistrationRequest(HomeTenant, _oid, Email, "Mike", "Okafor", null, false, BusinessUnit, "Secure Record"),
            CancellationToken.None);

        result.Outcome.Should().Be(HomeRegistrationOutcome.Conflict);
        inactive.Flag.Should().BeNull();
        _store.Contacts.Should().ContainSingle();
        log.Entries.Should().NotContain(e => e.Message.Contains("Set-ContactIdentityBindingSchema"));
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("503: service unavailable"),
            "the generic error log still carries the status and text");
    }

    // ── The bind's own faults (fix round 1 leftover) ───────────────────────────────────────────────

    [Fact]
    public async Task Bind_RefusedForAMissingPrivilege_Is500Environment_NotRetryable_NothingBound()
    {
        var existing = _store.AddContact(Guid.NewGuid(), email: Email);
        _store.BindStatusOverride = StoreWriteStatus.Forbidden;

        var response = await Post(Body(confirm: true));

        await ShouldBeProblem(response, HttpStatusCode.InternalServerError, ExternalRegistrationEndpoints.ErrorEnvironment);
        existing.Oid.Should().BeNull();
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Bind_FailedTransiently_StaysA503Unavailable()
    {
        var existing = _store.AddContact(Guid.NewGuid(), email: Email);
        _store.FailBind = true;

        var response = await Post(Body(confirm: true));

        await ShouldBeProblem(response, HttpStatusCode.ServiceUnavailable, ExternalRegistrationEndpoints.ErrorUnavailable);
        existing.Oid.Should().BeNull();
    }

    // ── Probe (046 review K, carried) ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("binding-masked")]
    [InlineData("home-masked")]
    [InlineData("home-column-missing")]
    public async Task MaskedBindingOrMissingA9Columns_Is500Environment_AndNeverCreates(string shape)
    {
        // A registered WorkforceHome contact whose tenant column is masked looks unregistered to the 046 read.
        var other = AddHomeContact(Guid.NewGuid(), oid: Guid.NewGuid());
        switch (shape)
        {
            case "binding-masked": _store.MaskBindingColumn = true; break;
            case "home-masked": _store.MaskHomeColumns = true; break;
            default: _store.HomeProbeColumnMissing = true; break;
        }

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.InternalServerError, ExternalRegistrationEndpoints.ErrorEnvironment);
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Keys.Should().Equal(other.Id);
    }

    [Theory]
    [InlineData("home-probe-failed")]
    [InlineData("binding-probe-failed")]
    public async Task TransientProbeFailure_Is503Unavailable_Retryable_AndNeverCreates(string shape)
    {
        var other = AddHomeContact(Guid.NewGuid(), oid: Guid.NewGuid());
        if (shape == "home-probe-failed") _store.FailHomeProbe = true; else _store.FailProbe = true;

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.ServiceUnavailable, ExternalRegistrationEndpoints.ErrorUnavailable);
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Keys.Should().Equal(other.Id);
    }

    // ── Failed reads before a create (pass 1 review) ───────────────────────────────────────────────

    [Fact]
    public async Task FailedLicensedUserRead_Is503Unavailable_NothingWritten()
    {
        _store.FailLicensedUserLookup = true;

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.ServiceUnavailable, ExternalRegistrationEndpoints.ErrorUnavailable);
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedOwnerTeamRead_Is503Unavailable_NothingCreated()
    {
        _store.FailTeamLookup = true;

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.ServiceUnavailable, ExternalRegistrationEndpoints.ErrorUnavailable);
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Should().BeEmpty();
    }

    // ── The create's own faults (pass 2 K3) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_RefusedForAMissingPrivilege_Is500Environment_NotRetryable()
    {
        _store.CreateStatusOverride = StoreWriteStatus.Forbidden;

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.InternalServerError, ExternalRegistrationEndpoints.ErrorEnvironment);
        _store.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_FailedTransiently_StaysA503Unavailable()
    {
        _store.FailCreate = true;

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.ServiceUnavailable, ExternalRegistrationEndpoints.ErrorUnavailable);
        _store.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task CustomerBusinessUnit_IsTheSecureRecordBusinessUnit_Is500OwnerUnconfigured_NothingCreated()
    {
        _store.BusinessUnitsByName["Secure Record"] = new List<Guid> { BusinessUnit };

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.InternalServerError, ExternalRegistrationEndpoints.ReasonOwnerUnconfigured);
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Should().BeEmpty();
        _store.Reads.Should().NotContain("owner-team", "the refusal comes before the owner team is read");
    }

    [Fact]
    public async Task SecureRecordBusinessUnit_Ambiguous_Is500OwnerUnconfigured_NothingCreated()
    {
        _store.BusinessUnitsByName["Secure Record"] = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.InternalServerError, ExternalRegistrationEndpoints.ReasonOwnerUnconfigured);
        _store.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task SecureRecordBusinessUnitRead_Failed_Is503Unavailable_NothingCreated()
    {
        _store.FailBusinessUnitLookup = true;

        var response = await Post(Body());

        await ShouldBeProblem(response, HttpStatusCode.ServiceUnavailable, ExternalRegistrationEndpoints.ErrorUnavailable);
        _store.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task CustomerBusinessUnit_DifferentFromTheSecureRecordBusinessUnit_Registers()
    {
        _store.BusinessUnitsByName["Secure Record"] = new List<Guid> { Guid.NewGuid() };

        var response = await Post(Body());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Email and body ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TokenWithoutEmail_EvenWithPreferredUsername_Is403RegistrationEmailMissing()
    {
        var response = await Post(Body(), email: null, preferredUsername: Email);

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalRegistrationEndpoints.ReasonEmailMissing);
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().NotContain("email");
    }

    [Theory]
    [InlineData("""{"firstName":"Mike","lastName":"Okafor","email":"attacker@evil.example"}""")]
    [InlineData("""{"firstName":"Mike","lastName":"Okafor","ownerId":"00000000-0000-0000-0000-000000000001"}""")]
    [InlineData("""{"lastName":"Okafor"}""")]
    [InlineData("""{"firstName":"   ","lastName":"Okafor"}""")]
    [InlineData("""{"firstName":"Mike"}""")]
    [InlineData("""{"firstName":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","lastName":"Okafor"}""")]
    [InlineData("""{"firstName":"Mike","lastName":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"firstName":"Mi\u0007ke","lastName":"Okafor"}""")]
    [InlineData("""{"firstName":"Mike","lastName":"Oka\nfor"}""")]
    public async Task InvalidBody_Is400_NoWrite(string json)
    {
        var response = await Post(json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _store.Writes.Should().BeEmpty();
        _store.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task OverlongDepartment_Is400()
    {
        var response = await Post(Body(department: new string('D', ExternalRegistrationEndpoints.MaxDepartmentLength + 1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _store.Writes.Should().BeEmpty();
    }

    // ── Schemes ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Bearer")]          // a Spaarke-tenant workforce token
    [InlineData(AuthSchemes.Ciam)]  // a CIAM partner token
    public async Task NonHomePrincipal_Is403_AndNothingIsRead(string scheme)
    {
        var response = await Post(Body(), scheme: scheme);

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalRegistrationEndpoints.ReasonHomeIdentityRequired);
        _store.Reads.Should().BeEmpty();
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Anonymous_Is401()
    {
        using var client = _fixture.CreateClient();
        var response = await client.PostAsync(Route, new StringContent(Body(), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _store.Reads.Should().BeEmpty();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    private InMemoryContactIdentityStore.Contact AddHomeContact(Guid id, Guid? oid = null)
    {
        var c = _store.AddContact(id, oid: (oid ?? _oid).ToString("D"), plane: IdentityPlaneMarker.WorkforceHome);
        c.ExternalTenantId = HomeTenant.ToString("D");
        return c;
    }

    private static string Body(string first = "Mike", string last = "Okafor", string? department = null, bool? confirm = null)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["firstName"] = first,
            ["lastName"] = last,
            ["department"] = department,
            ["confirmExistingRecord"] = confirm,
        });

    private Task<HttpResponseMessage> Post(string json, string scheme = AuthSchemes.WorkforceHome, string? email = Email,
        string? preferredUsername = null)
        => PostAs(_fixture, json, _oid, scheme, email, preferredUsername);

    internal static async Task<HttpResponseMessage> PostAs(
        RegistrationFixtureBase fixture, string json, Guid oid, string scheme = AuthSchemes.WorkforceHome,
        string? email = Email, string? preferredUsername = null)
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer test");
        request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.SchemeHeader, scheme);
        request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.OidHeader, oid.ToString("D"));
        request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.TidHeader, HomeTenant.ToString("D"));
        if (email is not null) request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.EmailHeader, email);
        if (preferredUsername is not null)
        {
            request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.PreferredUsernameHeader, preferredUsername);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<Guid> ContactIdOf(HttpResponseMessage response)
        => (await Json(response)).GetProperty("contactId").GetGuid();

    internal static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string reasonCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        JsonDocument.Parse(body).RootElement.GetProperty("reasonCode").GetString().Should().Be(reasonCode);
    }

    // ── Hosts ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The route's host: the ExternalCollaboration policy re-pointed at <see cref="RegistrationTestAuthHandler"/> (as
    /// <c>ExternalCollaborationTestFixture</c> does for FakeAuth), the in-memory row store behind the REAL binder and
    /// resolver, and the identity-normalization seam mocked (only a stored-link resolution reaches it).
    /// </summary>
    public abstract class RegistrationFixtureBase : WorkspaceTestFixture
    {
        public InMemoryContactIdentityStore Store { get; } = new();

        public Mock<IIdentityNormalizationService> Identity { get; } = new();

        protected abstract Guid? CustomerBusinessUnitId { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, RegistrationTestAuthHandler>(RegistrationTestAuthHandler.SchemeName, _ => { });
                services.Configure<AuthorizationOptions>(options =>
                    options.AddPolicy(AuthPolicies.ExternalCollaboration, policy =>
                    {
                        policy.AuthenticationSchemes = new[] { RegistrationTestAuthHandler.SchemeName };
                        policy.RequireAuthenticatedUser();
                    }));

                services.RemoveAll<IContactIdentityStore>();
                services.AddSingleton<IContactIdentityStore>(Store);
                services.RemoveAll<IIdentityNormalizationService>();
                services.AddSingleton(Identity.Object);
                services.PostConfigure<ExternalAccessOptions>(o => o.CustomerBusinessUnitId = CustomerBusinessUnitId);

                // The base host runs as Development, where Minimal API THROWS on a body it cannot bind (an unknown
                // property of the closed DTO) instead of answering 400. Production does not throw; match it.
                services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);
            });
        }
    }

    /// <summary>ExternalAccess:CustomerBusinessUnitId configured.</summary>
    public sealed class ConfiguredFixture : RegistrationFixtureBase
    {
        protected override Guid? CustomerBusinessUnitId => BusinessUnit;
    }

    /// <summary>ExternalAccess:CustomerBusinessUnitId absent.</summary>
    public sealed class UnconfiguredFixture : RegistrationFixtureBase
    {
        protected override Guid? CustomerBusinessUnitId => null;
    }
}

/// <summary>AC: CustomerBusinessUnitId absent → 500 registration_owner_unconfigured, nothing created.</summary>
public sealed class ExternalRegistrationOwnerUnconfiguredTests
    : IClassFixture<ExternalRegistrationEndpointsTests.UnconfiguredFixture>
{
    private readonly ExternalRegistrationEndpointsTests.UnconfiguredFixture _fixture;

    public ExternalRegistrationOwnerUnconfiguredTests(ExternalRegistrationEndpointsTests.UnconfiguredFixture fixture)
    {
        _fixture = fixture;
        _fixture.Store.Reset();
        _fixture.Store.DefaultTeams[ExternalRegistrationEndpointsTests.BusinessUnit] =
            new List<Guid> { ExternalRegistrationEndpointsTests.OwnerTeam };
    }

    [Fact]
    public async Task NoMatch_WithoutTheBusinessUnitSetting_Is500OwnerUnconfigured_NothingCreated()
    {
        var response = await ExternalRegistrationEndpointsTests.PostAs(
            _fixture, """{"firstName":"Mike","lastName":"Okafor"}""", Guid.NewGuid());

        await ExternalRegistrationEndpointsTests.ShouldBeProblem(
            response, HttpStatusCode.InternalServerError, ExternalRegistrationEndpoints.ReasonOwnerUnconfigured);
        _fixture.Store.Writes.Should().BeEmpty();
        _fixture.Store.Contacts.Should().BeEmpty();
    }
}

/// <summary>
/// Authenticates from test headers and stamps the identity's <c>AuthenticationType</c> with the scheme the header names
/// (as the real WorkforceHome / Bearer / Ciam handlers do), so the route's WorkforceHome-only rule is tested for real.
/// No Authorization header → no identity (401).
/// </summary>
internal sealed class RegistrationTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "RegistrationTest";
    public const string SchemeHeader = "X-Test-Scheme";
    public const string OidHeader = "X-Test-Oid";
    public const string TidHeader = "X-Test-Tid";
    public const string EmailHeader = "X-Test-Email";
    public const string PreferredUsernameHeader = "X-Test-Upn";

    public RegistrationTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("scp", "user_impersonation"),
            new("acct", "0"),
            new("oid", Request.Headers[OidHeader].ToString()),
            new("tid", Request.Headers[TidHeader].ToString()),
            new("given_name", "Mike"),
            new("family_name", "Okafor"),
        };
        if (Request.Headers.TryGetValue(EmailHeader, out var email)) claims.Add(new Claim("email", email.ToString()));
        if (Request.Headers.TryGetValue(PreferredUsernameHeader, out var upn))
        {
            claims.Add(new Claim("preferred_username", upn.ToString()));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: Request.Headers[SchemeHeader].ToString());
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
