using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// ISS-005 (#1673, R3) — every contact the binder creates on the Workforce plane is owned at creation by the default owner
/// team of <c>ExternalAccess:CustomerBusinessUnitId</c>, resolved by the SAME code the registration create uses
/// (<c>ContactIdentityBinder.ResolveCustomerOwnerTeamAsync</c>), and refused the same way when that team cannot be resolved:
/// the first sign-in create (<c>CreateByOid</c> on the workforce token plane) and the licensed-user link create. Registration
/// itself is unchanged and is the oracle for the refusals.
/// </summary>
public class ContactIdentityBinderOwnerTeamTests
{
    private const string OwnerBind = "ownerid@odata.bind";
    private const string SecureBusinessUnitName = "Secure Record";
    private const string Email = "pat.example@customer.example";
    private static readonly Guid CustomerBu = Guid.Parse("cb15f587-1111-4111-8111-00000000000a");
    private static readonly Guid CustomerTeam = Guid.Parse("7ea70000-2222-4222-8222-00000000000b");
    private static readonly Guid HomeTenant = Guid.Parse("99999999-eeee-4eee-8eee-999999999999");
    private static readonly string Stamp = SpaarkeTenant.ToString("D");

    private readonly InMemoryContactIdentityStore _store = new();

    /// <summary>A binder over THIS BFF's own environment: the setting and the configuration are handed in, as DI does.</summary>
    private ContactIdentityBinder OwnedBinder(
        Guid? customerBusinessUnitId, IDictionary<string, string?>? configuration = null, InMemoryContactIdentityStore? store = null,
        BindingLogCapture<ContactIdentityBinder>? log = null)
    {
        var external = new Mock<IOptionsMonitor<ExternalAccessOptions>>();
        external.SetupGet(o => o.CurrentValue).Returns(new ExternalAccessOptions { CustomerBusinessUnitId = customerBusinessUnitId });
        return new ContactIdentityBinder(
            store ?? _store, Tenants(CustomerTenant), new FakeTimeProvider(Now), log ?? new BindingLogCapture<ContactIdentityBinder>(),
            ContactCreateOwner.FromCustomerBusinessUnit(
                external.Object,
                new ConfigurationBuilder().AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()).Build()));
    }

    private static System.Security.Claims.ClaimsPrincipal Member(Guid oid)
        => WorkforceUser(oid, CustomerTenant, acct: "0", email: Email);

    private void GivenOneDefaultTeam() => _store.DefaultTeams[CustomerBu] = new List<Guid> { CustomerTeam };

    private Guid GivenALicensedUserWithNoContact(out Guid oid)
    {
        var userId = Guid.NewGuid();
        oid = Guid.NewGuid();
        _store.AddSystemUser(userId, oid, "licensed.user@customer.example");
        return userId;
    }

    // ── Must fire: the owner is bound ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFirstSignInCreate_IsOwnedByTheCustomerBusinessUnitsDefaultTeam()
    {
        GivenOneDefaultTeam();
        var oid = Guid.NewGuid();

        var result = await OwnedBinder(CustomerBu).ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        result.Action.Should().Be(BindingAction.CreateByOid);
        result.IsResolved.Should().BeTrue();
        var payload = _store.CreatePayloads.Should().ContainSingle().Subject;
        payload[OwnerBind].Should().Be($"/teams({CustomerTeam:D})");
        payload["sprk_identityplane"].Should().Be((int)IdentityPlaneMarker.Workforce);
        _store.Contacts.Values.Should().ContainSingle().Which.OwnerTeamId.Should().Be(CustomerTeam);
    }

    [Fact]
    public async Task TheLicensedUserLinkCreate_IsOwnedByTheCustomerBusinessUnitsDefaultTeam()
    {
        GivenOneDefaultTeam();
        var userId = GivenALicensedUserWithNoContact(out var oid);

        var result = await OwnedBinder(CustomerBu).EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.CreatedAndLinked);
        result.IsLinked.Should().BeTrue();
        var payload = _store.CreatePayloads.Should().ContainSingle().Subject;
        payload[OwnerBind].Should().Be($"/teams({CustomerTeam:D})");
        payload["sprk_identityplane"].Should().Be((int)IdentityPlaneMarker.Workforce);
        var created = _store.ContactsBoundTo(oid).Should().ContainSingle().Subject;
        created.OwnerTeamId.Should().Be(CustomerTeam);
        _store.SystemUsers[userId].PrimaryContactId.Should().Be(created.Id);
    }

    [Fact]
    public async Task ACustomerBusinessUnit_ThatIsNotTheSecureRecordUnit_IsNotRefused()
    {
        // Must-not-fire for the Secure Record refusal: a Secure Record unit exists, and it is another unit.
        GivenOneDefaultTeam();
        _store.BusinessUnitsByName[SecureBusinessUnitName] = new List<Guid> { Guid.NewGuid() };
        var oid = Guid.NewGuid();

        var result = await OwnedBinder(CustomerBu).ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        _store.Contacts.Values.Should().ContainSingle().Which.OwnerTeamId.Should().Be(CustomerTeam);
    }

    [Fact]
    public async Task TheSecureRecordUnitName_IsTheConfiguredOne()
    {
        // The binder reads the same name registration is handed (SecureRecord:BusinessUnitName), not a constant.
        GivenOneDefaultTeam();
        _store.BusinessUnitsByName["Restricted"] = new List<Guid> { CustomerBu };
        var oid = Guid.NewGuid();
        var binder = OwnedBinder(CustomerBu, new Dictionary<string, string?> { ["SecureRecord:BusinessUnitName"] = "Restricted" });

        var result = await binder.ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        _store.Contacts.Should().BeEmpty();
    }

    // ── Must not fire: nothing is created, and the reason code is registration's ──────────────────

    public enum Refused
    {
        SettingUnset,
        SettingEmptyGuid,
        CustomerUnitIsTheSecureRecordUnit,
        SecureRecordUnitAmbiguous,
        NoDefaultOwnerTeam,
        TwoDefaultOwnerTeams,
    }

    public enum Unreadable
    {
        SecureRecordUnitRead,
        DefaultOwnerTeamRead,
    }

    /// <summary>Arranges the store for a refusal and returns the value of the setting.</summary>
    private Guid? Arrange(Refused cause)
    {
        switch (cause)
        {
            case Refused.SettingUnset:
                GivenOneDefaultTeam();
                return null;
            case Refused.SettingEmptyGuid:
                GivenOneDefaultTeam();
                return Guid.Empty;
            case Refused.CustomerUnitIsTheSecureRecordUnit:
                GivenOneDefaultTeam();
                _store.BusinessUnitsByName[SecureBusinessUnitName] = new List<Guid> { CustomerBu };
                return CustomerBu;
            case Refused.SecureRecordUnitAmbiguous:
                GivenOneDefaultTeam();
                _store.BusinessUnitsByName[SecureBusinessUnitName] = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
                return CustomerBu;
            case Refused.NoDefaultOwnerTeam:
                return CustomerBu;
            case Refused.TwoDefaultOwnerTeams:
                _store.DefaultTeams[CustomerBu] = new List<Guid> { CustomerTeam, Guid.NewGuid() };
                return CustomerBu;
            default:
                throw new ArgumentOutOfRangeException(nameof(cause));
        }
    }

    private Guid? Arrange(Unreadable cause)
    {
        GivenOneDefaultTeam();
        if (cause == Unreadable.SecureRecordUnitRead) _store.FailBusinessUnitLookup = true;
        else _store.FailTeamLookup = true;
        return CustomerBu;
    }

    /// <summary>What registration (unchanged) answers for this arrangement, as a reason code - the oracle.</summary>
    private async Task<string> RegistrationsReasonAsync(ContactIdentityBinder binder, Guid? setting)
    {
        var result = await binder.RegisterHomeContactAsync(
            new HomeRegistrationRequest(HomeTenant, Guid.NewGuid(), Email, "Pat", "Example", null, false, setting, SecureBusinessUnitName),
            CancellationToken.None);
        return result.Outcome switch
        {
            HomeRegistrationOutcome.OwnerUnconfigured => ContactBindingDecision.DenyOwnerUnconfigured,
            HomeRegistrationOutcome.Denied => result.DenyCode!,
            _ => throw new InvalidOperationException($"registration did not refuse: {result.Outcome}"),
        };
    }

    private void AssertNothingWasCreated()
    {
        _store.Contacts.Should().BeEmpty();
        _store.Writes.Should().BeEmpty();
        _store.CreatePayloads.Should().BeEmpty();
    }

    [Fact]
    public void TheRefusalReasonCode_IsRegistrationsOwn()
    {
        // ONE code for every contact create: the registration route's constant is this value (not a second copy).
        ContactBindingDecision.DenyOwnerUnconfigured.Should().Be("sdap.access.registration_owner_unconfigured");
        ExternalRegistrationEndpoints.ReasonOwnerUnconfigured.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
    }

    [Theory]
    [InlineData(Refused.SettingUnset)]
    [InlineData(Refused.SettingEmptyGuid)]
    [InlineData(Refused.CustomerUnitIsTheSecureRecordUnit)]
    [InlineData(Refused.SecureRecordUnitAmbiguous)]
    [InlineData(Refused.NoDefaultOwnerTeam)]
    [InlineData(Refused.TwoDefaultOwnerTeams)]
    public async Task TheFirstSignInCreate_WhenNoOwnerTeamResolves_CreatesNothing_AndRefusesWithRegistrationsReasonCode(Refused cause)
    {
        var setting = Arrange(cause);
        var binder = OwnedBinder(setting);
        var oid = Guid.NewGuid();

        var result = await binder.ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        result.Wrote.Should().BeFalse();
        AssertNothingWasCreated();
        (await RegistrationsReasonAsync(binder, setting)).Should().Be(result.DenyCode, "one refusal, one reason code");
    }

    [Theory]
    [InlineData(Refused.SettingUnset)]
    [InlineData(Refused.SettingEmptyGuid)]
    [InlineData(Refused.CustomerUnitIsTheSecureRecordUnit)]
    [InlineData(Refused.SecureRecordUnitAmbiguous)]
    [InlineData(Refused.NoDefaultOwnerTeam)]
    [InlineData(Refused.TwoDefaultOwnerTeams)]
    public async Task TheLicensedUserLinkCreate_WhenNoOwnerTeamResolves_CreatesNothing_AndRefusesWithRegistrationsReasonCode(Refused cause)
    {
        var setting = Arrange(cause);
        var binder = OwnedBinder(setting);
        var userId = GivenALicensedUserWithNoContact(out _);

        var result = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.Denied);
        result.IsLinked.Should().BeFalse();
        result.DenyCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        AssertNothingWasCreated();
        _store.SystemUsers[userId].PrimaryContactId.Should().BeNull("no contact, so no link");
        (await RegistrationsReasonAsync(binder, setting)).Should().Be(result.DenyCode);
    }

    [Theory]
    [InlineData(Unreadable.SecureRecordUnitRead)]
    [InlineData(Unreadable.DefaultOwnerTeamRead)]
    public async Task AnUnreadableOwnerLookup_RefusesBothCreates_WithRegistrationsReasonCode(Unreadable cause)
    {
        var setting = Arrange(cause);
        var binder = OwnedBinder(setting);
        var oid = Guid.NewGuid();
        var userId = GivenALicensedUserWithNoContact(out _);

        var signIn = await binder.ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);
        var link = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None);

        signIn.IsResolved.Should().BeFalse();
        signIn.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        link.Outcome.Should().Be(SystemUserLinkOutcome.Denied);
        link.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        AssertNothingWasCreated();
        (await RegistrationsReasonAsync(binder, setting)).Should().Be(ContactBindingDecision.DenyContactLookupFailed);
    }

    // ── The report-only pass (K1) decides the owner too: it reports the refusal, and still writes nothing ──────

    [Fact]
    public async Task TheReportOnlyLinkPass_WithAnOwnerTeam_PlansTheCreate_SaysOwnerSet_AndWritesNothing()
    {
        GivenOneDefaultTeam();
        var binder = OwnedBinder(CustomerBu);
        var userId = GivenALicensedUserWithNoContact(out _);

        var result = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: false, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.CreatedAndLinked, "the plan is unchanged when an owner resolves");
        result.WritesApplied.Should().BeFalse();
        var change = result.Changes.Should().ContainSingle(c => c.Entity == "contact").Subject;
        change.After.Should().Contain("ownerid=(set)", "the #2130 change line names the column, never the team")
            .And.NotContain(CustomerTeam.ToString("D"));
        AssertNothingWasCreated();
        _store.SystemUsers[userId].PrimaryContactId.Should().BeNull("a dry run links nothing");
        _store.Reads.Should().Contain(new[] { "business-unit", "owner-team" }, "the dry run resolves the owner (reads only)");
    }

    [Theory]
    [InlineData(Refused.SettingUnset)]
    [InlineData(Refused.SettingEmptyGuid)]
    [InlineData(Refused.CustomerUnitIsTheSecureRecordUnit)]
    [InlineData(Refused.SecureRecordUnitAmbiguous)]
    [InlineData(Refused.NoDefaultOwnerTeam)]
    [InlineData(Refused.TwoDefaultOwnerTeams)]
    public async Task TheReportOnlyLinkPass_ReportsTheRefusal_TheWritePassWouldMake_AndWritesNothing(Refused cause)
    {
        var binder = OwnedBinder(Arrange(cause));
        var userId = GivenALicensedUserWithNoContact(out _);

        var dryRun = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: false, CancellationToken.None);

        dryRun.Outcome.Should().Be(SystemUserLinkOutcome.Denied, "the dry run must not promise a create the write pass refuses");
        dryRun.IsLinked.Should().BeFalse();
        dryRun.DenyCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        dryRun.WritesApplied.Should().BeFalse();
        dryRun.Changes.Should().BeEmpty();
        AssertNothingWasCreated();

        var writePass = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None);
        writePass.Outcome.Should().Be(dryRun.Outcome, "one decision, two passes");
        writePass.DenyCode.Should().Be(dryRun.DenyCode);
    }

    [Theory]
    [InlineData(Unreadable.SecureRecordUnitRead)]
    [InlineData(Unreadable.DefaultOwnerTeamRead)]
    public async Task TheReportOnlyLinkPass_ReportsAnUnreadableOwnerLookup_AsTheWritePassDoes(Unreadable cause)
    {
        var binder = OwnedBinder(Arrange(cause));
        var userId = GivenALicensedUserWithNoContact(out _);

        var dryRun = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: false, CancellationToken.None);

        dryRun.Outcome.Should().Be(SystemUserLinkOutcome.Denied);
        dryRun.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        AssertNothingWasCreated();
        (await binder.EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None)).DenyCode
            .Should().Be(dryRun.DenyCode);
    }

    [Fact]
    public async Task TheReportOnlyLinkPass_StillNeverProbesTheBindingColumn_AndTheWritePassStillRefusesAMaskedOneFirst()
    {
        // Precedence is unchanged: the write pass probes the (possibly masked) binding column BEFORE it reads the owner; the
        // report-only pass never probes it.
        GivenOneDefaultTeam();
        _store.FailProbe = true;
        var binder = OwnedBinder(customerBusinessUnitId: null);
        var userId = GivenALicensedUserWithNoContact(out _);

        var dryRun = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: false, CancellationToken.None);

        dryRun.DenyCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured, "the dry run does not probe the column");
        _store.Reads.Should().NotContain("probe");

        _store.Reads.Clear();
        var writePass = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None);

        writePass.DenyCode.Should().Be(ContactBindingDecision.DenyBindingColumnMasked);
        _store.Reads.Should().NotContain(new[] { "owner-team", "business-unit" }, "a masked column refuses before any owner read");
        AssertNothingWasCreated();
    }

    // ── K4 (UAC re-check): an unreadable owner lookup is logged (Warning), with no email and no secret ──────────

    [Theory]
    [InlineData(Unreadable.SecureRecordUnitRead, "Secure Record business unit could not be read")]
    [InlineData(Unreadable.DefaultOwnerTeamRead, "default owner team of business unit")]
    public async Task AnUnreadableOwnerLookup_IsLoggedAtWarning_NamingTheBusinessUnit_NeverAnEmail(Unreadable cause, string expected)
    {
        var log = new BindingLogCapture<ContactIdentityBinder>();
        var binder = OwnedBinder(Arrange(cause), log: log);
        var oid = Guid.NewGuid();

        await binder.ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        var warning = log.Entries.Should().ContainSingle(e => e.Message.Contains(expected)).Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain(CustomerBu.ToString("D"), "the business unit id is configuration, not personal data")
            .And.Contain("Nothing was created");
        log.Messages.Should().NotContain(m => m.Contains(Email) || m.Contains("customer.example"), "no email in the log");
    }

    [Fact]
    public async Task AReadableOwnerLookup_LogsNoLookupWarning()
    {
        // Must not fire: the happy path and the configuration refusals are not "could not be read".
        GivenOneDefaultTeam();
        var log = new BindingLogCapture<ContactIdentityBinder>();
        var oid = Guid.NewGuid();

        await OwnedBinder(CustomerBu, log: log).ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        log.Messages.Should().NotContain(m => m.Contains("could not be read"));
    }

    // ── K1 (UAC re-check): Dataverse refuses the owner of the create (0x80040299) -> the owner-unconfigured refusal ──

    private void GivenDataverseRefusesTheOwner() => _store.CreateStatusOverride = StoreWriteStatus.OwnerPrivilegeMissing;

    [Fact]
    public async Task TheFirstSignInCreate_RefusedForItsOwnersPrivilege_IsTheOwnerUnconfiguredRefusal_AndLogsWhyAtError()
    {
        GivenOneDefaultTeam();
        GivenDataverseRefusesTheOwner();
        var log = new BindingLogCapture<ContactIdentityBinder>();
        var oid = Guid.NewGuid();

        var result = await OwnedBinder(CustomerBu, log: log).ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        _store.Contacts.Should().BeEmpty();
        var entry = log.Entries.Should().ContainSingle(e => e.Message.Contains("Read Privilege Check For Owner")).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain("security role with Read on contact").And.NotContain(Email);
    }

    [Fact]
    public async Task TheLicensedUserLinkCreate_RefusedForItsOwnersPrivilege_IsDeniedOwnerPrivilegeMissing_NotFailed()
    {
        GivenOneDefaultTeam();
        GivenDataverseRefusesTheOwner();
        var userId = GivenALicensedUserWithNoContact(out _);

        var result = await OwnedBinder(CustomerBu).EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.Denied, "a configuration fault, counted apart from the unset setting");
        result.DenyCode.Should().Be(ContactBindingDecision.DenyOwnerPrivilegeMissing);
        result.IsLinked.Should().BeFalse();
        _store.Contacts.Should().BeEmpty();
        _store.SystemUsers[userId].PrimaryContactId.Should().BeNull();
    }

    [Fact]
    public async Task TheLicensedUserLinkCreate_RefusedForItsOwnersPrivilege_LogsTheSameCodeItReturns()
    {
        GivenOneDefaultTeam();
        GivenDataverseRefusesTheOwner();
        var userId = GivenALicensedUserWithNoContact(out _);
        var log = new BindingLogCapture<ContactIdentityBinder>();

        var result = await OwnedBinder(CustomerBu, log: log).EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyOwnerPrivilegeMissing);
        var entry = log.Entries.Should().ContainSingle(e => e.Message.Contains("Read Privilege Check For Owner")).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain($"({ContactBindingDecision.DenyOwnerPrivilegeMissing})")
            .And.NotContain(ContactBindingDecision.DenyOwnerUnconfigured);
    }

    [Fact]
    public async Task Registration_RefusedForItsOwnersPrivilege_IsTheOwnerUnconfiguredOutcome()
    {
        GivenOneDefaultTeam();
        GivenDataverseRefusesTheOwner();

        var reason = await RegistrationsReasonAsync(OwnedBinder(CustomerBu), CustomerBu);

        reason.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        _store.Contacts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(StoreWriteStatus.Failed)]
    [InlineData(StoreWriteStatus.Forbidden)]
    public async Task AnyOtherCreateFault_KeepsItsExistingMapping_AndIsNotTheOwnerRefusal(StoreWriteStatus fault)
    {
        // Must not fire: only the owner's privilege fault is remapped. The expectations are what the code answered before.
        GivenOneDefaultTeam();
        _store.CreateStatusOverride = fault;
        var oid = Guid.NewGuid();
        var userId = GivenALicensedUserWithNoContact(out _);
        var binder = OwnedBinder(CustomerBu);

        var signIn = await binder.ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);
        var link = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None);
        var registration = await RegistrationsReasonAsync(binder, CustomerBu);

        signIn.DenyCode.Should().Be(ContactBindingDecision.DenyContactCreateFailed);
        link.Outcome.Should().Be(SystemUserLinkOutcome.Failed);
        link.DenyCode.Should().Be(ContactBindingDecision.DenyContactCreateFailed);
        registration.Should().Be(fault == StoreWriteStatus.Forbidden
            ? ContactBindingDecision.DenyContactCreateUnavailable
            : ContactBindingDecision.DenyContactCreateFailed);
    }

    // ── Must not fire: an existing contact needs no owner ──────────────────────────────────────────

    [Fact]
    public async Task BindingAnExistingContact_NeedsNoOwner_EvenWithTheSettingUnset()
    {
        var contactId = Guid.NewGuid();
        _store.AddContact(contactId, email: Email);
        _store.StampInvite(contactId, Email, CustomerTenant);
        var oid = Guid.NewGuid();

        var result = await OwnedBinder(customerBusinessUnitId: null)
            .ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        result.Should().Be(ContactBindingResult.Resolved(contactId, BindingAction.BindByEmail, wrote: true));
        _store.Reads.Should().NotContain(new[] { "owner-team", "business-unit" });
        _store.CreatePayloads.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvingByOid_NeedsNoOwner_EvenWithTheSettingUnset()
    {
        var oid = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        _store.AddContact(contactId, email: Email, oid: oid.ToString("D"), plane: IdentityPlaneMarker.Workforce);

        var result = await OwnedBinder(customerBusinessUnitId: null)
            .ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        result.Should().Be(ContactBindingResult.Resolved(contactId, BindingAction.ResolveByOid));
        _store.Reads.Should().NotContain(new[] { "owner-team", "business-unit" });
    }

    // ── A binder over ANOTHER environment binds no owner, by design (#2270): the setting names a unit of THIS environment ──
    // These tests do not lock a gap in: the own environment is owned (the DI tests below; RegistrationContactLinkTests asserts the
    // registration approval's own-environment contact carries ownerid), and this is the factory's explicit, named policy.

    [Fact]
    public async Task TheReportOnlyLinkPass_OfABinderOverAnotherEnvironment_PlansTheCreate_SaysOwnerNone_AndReadsNoOwnerTeam()
    {
        var userId = Guid.NewGuid();
        var other = new InMemoryContactIdentityStore();
        other.AddSystemUser(userId, Guid.NewGuid(), "new.user@demo.spaarke.com");
        var binder = new StoreBinderFactory(other).CreateBinder("https://spaarke-demo.crm.dynamics.com", _ => Task.FromResult("token"));

        var result = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: false, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.CreatedAndLinked);
        result.Changes.Should().ContainSingle(c => c.Entity == "contact").Which.After.Should().Contain("ownerid=(none)");
        other.Writes.Should().BeEmpty();
        other.Reads.Should().NotContain(new[] { "owner-team", "business-unit" });
    }

    [Fact]
    public async Task ABinderOverAnotherEnvironment_StillCreatesWithoutAnOwner_AndReadsNoOwnerTeam()
    {
        // By design and tracked in #2270: the setting names a business unit of THIS environment, so a binder the factory
        // builds over another one (demo / customer targets) creates ownerless contacts. Its owner policy says so, and why.
        var oid = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var other = new InMemoryContactIdentityStore();
        other.AddSystemUser(userId, oid, "new.user@demo.spaarke.com");
        var log = new BindingLogCapture<ContactIdentityBinder>();
        var factory = new StoreBinderFactory(other, log);

        var binder = factory.CreateBinder("https://spaarke-demo.crm.dynamics.com", _ => Task.FromResult("token"));
        var result = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.CreatedAndLinked);
        var payload = other.CreatePayloads.Should().ContainSingle().Subject;
        payload.Should().NotContainKey(OwnerBind);
        other.Reads.Should().NotContain(new[] { "owner-team", "business-unit" });
        var reason = log.Entries.Should().ContainSingle(e => e.Message.Contains("binds no owner")).Subject;
        reason.Level.Should().Be(LogLevel.Debug, "the reason is a diagnostic, once per create");
        reason.Message.Should().Contain("foreign Dataverse environment spaarke-demo.crm.dynamics.com; see #2270");
    }

    // ── The owner policy is required, and says why when there is none ─────────────────────────────────

    [Fact]
    public void ABinder_CannotBeBuiltWithoutAnOwnerPolicy()
        => FluentActions.Invoking(() => new ContactIdentityBinder(
                _store, Tenants(CustomerTenant), new FakeTimeProvider(Now), new BindingLogCapture<ContactIdentityBinder>(),
                createOwner: null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("createOwner");

    [Fact]
    public void TheCustomerBusinessUnitPolicy_NeedsItsInputs()
    {
        FluentActions.Invoking(() => ContactCreateOwner.FromCustomerBusinessUnit(null!, new ConfigurationBuilder().Build()))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        FluentActions.Invoking(() => ContactCreateOwner.FromCustomerBusinessUnit(Mock.Of<IOptionsMonitor<ExternalAccessOptions>>(), null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TheNoOwnerPolicy_NeedsAReason(string? reason)
        => FluentActions.Invoking(() => ContactCreateOwner.NoneForForeignEnvironment(reason!))
            .Should().Throw<ArgumentException>();

    [Fact]
    public async Task TheNoOwnerPolicy_LogsItsReasonOncePerCreate_AtDebug()
    {
        var log = new BindingLogCapture<ContactIdentityBinder>();
        var store = new InMemoryContactIdentityStore();
        var binder = new ContactIdentityBinder(
            store, Tenants(CustomerTenant), new FakeTimeProvider(Now), log,
            ContactCreateOwner.NoneForForeignEnvironment("test reason"));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await binder.ResolveWorkforceCallerAsync(
            WorkforceUser(first, CustomerTenant, acct: "0", email: "first@customer.example"), first, Stamp, CancellationToken.None);
        await binder.ResolveWorkforceCallerAsync(
            WorkforceUser(second, CustomerTenant, acct: "0", email: "second@customer.example"), second, Stamp, CancellationToken.None);

        log.Entries.Where(e => e.Message.Contains("binds no owner: test reason")).Should().HaveCount(2)
            .And.OnlyContain(e => e.Level == LogLevel.Debug);
    }

    // ── The DI-registered binder is the one over this BFF's own environment: it carries the setting ────────

    /// <summary>
    /// The binder as the container builds it: the REAL <c>ExternalAccessModule</c> registration (<c>AddExternalAccess</c>), over
    /// configuration keys the app reads (<c>ExternalAccess:CustomerBusinessUnitId</c>, <c>WorkforceIdentity:CustomerTenantIds</c>).
    /// Only the Dataverse-backed store is replaced, by the in-memory one: the registration under test is not copied here, so a
    /// registration that passed the wrong owner policy would fail these tests.
    /// </summary>
    private ContactIdentityBinder ResolveFromDi(Guid? customerBusinessUnitId)
    {
        var settings = new Dictionary<string, string?>
        {
            ["WorkforceIdentity:CustomerTenantIds:0"] = CustomerTenant.ToString("D"),
        };
        if (customerBusinessUnitId is { } unit)
        {
            settings["ExternalAccess:CustomerBusinessUnitId"] = unit.ToString("D");
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddExternalAccess();
        services.AddSingleton<IContactIdentityStore>(_store);
        return services.BuildServiceProvider().GetRequiredService<ContactIdentityBinder>();
    }

    [Fact]
    public async Task TheDiRegisteredBinder_RefusesTheFirstSignInCreate_WhenTheSettingIsUnset()
    {
        GivenOneDefaultTeam();
        var oid = Guid.NewGuid();

        var result = await ResolveFromDi(customerBusinessUnitId: null)
            .ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        _store.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task TheDiRegisteredBinder_OwnsTheFirstSignInCreate_WhenTheSettingIsSet()
    {
        GivenOneDefaultTeam();
        var oid = Guid.NewGuid();

        var result = await ResolveFromDi(CustomerBu)
            .ResolveWorkforceCallerAsync(Member(oid), oid, Stamp, CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        _store.CreatePayloads.Should().ContainSingle().Which[OwnerBind].Should().Be($"/teams({CustomerTeam:D})");
    }

    // ── K3: WithOwner copies every property of NewContactDetails ─────────────────────────────────────────
    //
    // WithOwner is a positional copy, not a `with` expression (the task 146 b2 census flags every `with` that sets an owning team
    // in server code). A property added to NewContactDetails and not copied there would be dropped from a Workforce-plane create
    // without a compile error or a failing behaviour test (those creates set only the three positional values). This test builds
    // a details object with EVERY public settable/init property set to a non-default value, runs WithOwner, and names any
    // property whose value did not survive.

    private static MethodInfo WithOwnerMethod()
        => typeof(ContactIdentityBinder).GetMethod("WithOwner", BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException(
               "ContactIdentityBinder.WithOwner was renamed or removed: update the K3 test (and keep a copy-completeness check) with it.");

    /// <summary>The public properties a caller can set (setter or init accessor): what a copy must carry.</summary>
    private static IReadOnlyList<PropertyInfo> CopiableProperties()
        => typeof(NewContactDetails).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .ToList();

    /// <summary>A non-default value for a property; a type this does not know fails the test, so the next property is not skipped.</summary>
    private static object NonDefaultValue(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(string)) return $"sample-{property.Name}";
        if (type == typeof(Guid)) return Guid.NewGuid();
        if (type == typeof(bool)) return true;
        if (type == typeof(int)) return 7;
        if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        if (type == typeof(DateTime)) return new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        if (type.IsEnum) return Enum.GetValues(type).Cast<object>().First(v => !Equals(v, Activator.CreateInstance(type)));
        throw new NotSupportedException(
            $"NewContactDetails.{property.Name} is a {property.PropertyType}: teach NonDefaultValue() this type so the K3 copy check covers it.");
    }

    /// <summary>A details object with every copiable property set to a non-default value (the primary constructor, then the init setters).</summary>
    private static NewContactDetails DetailsWithEveryPropertySet()
    {
        var constructor = typeof(NewContactDetails).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single();
        var byName = CopiableProperties().ToDictionary(p => p.Name, p => NonDefaultValue(p), StringComparer.OrdinalIgnoreCase);
        var details = (NewContactDetails)constructor.Invoke(
            constructor.GetParameters().Select(parameter => byName[parameter.Name!]).ToArray());
        foreach (var property in CopiableProperties())
        {
            property.SetValue(details, byName[property.Name]);
            if (!Equals(property.GetValue(details), byName[property.Name]))
            {
                throw new InvalidOperationException($"NewContactDetails.{property.Name} did not take its sample value.");
            }
        }

        return details;
    }

    /// <summary>The names of the properties whose value in <paramref name="copy"/> is not the original's (the owner's, for OwningTeamId).</summary>
    private static IReadOnlyList<string> NotCopied(NewContactDetails original, NewContactDetails copy, Guid owningTeam)
        => CopiableProperties()
            .Where(p =>
            {
                var expected = p.Name == nameof(NewContactDetails.OwningTeamId) ? owningTeam : p.GetValue(original);
                return !Equals(p.GetValue(copy), expected);
            })
            .Select(p => p.Name)
            .ToList();

    private static NewContactDetails InvokeWithOwner(NewContactDetails details, string status, Guid team)
    {
        var method = WithOwnerMethod();
        var resolutionType = method.GetParameters()[1].ParameterType;
        var statusType = resolutionType.GetProperty("Status")!.PropertyType;
        var resolution = Activator.CreateInstance(resolutionType, Enum.Parse(statusType, status), team)!;
        return (NewContactDetails)method.Invoke(null, new[] { details, resolution })!;
    }

    [Fact]
    public void WithOwner_CopiesEveryPropertyOfNewContactDetails_AndSetsTheOwner()
    {
        var details = DetailsWithEveryPropertySet();
        var properties = CopiableProperties().Select(p => p.Name).ToList();
        properties.Should().Contain(new[] { "FirstName", "LastName", "Email", "Department", "ExternalTenantId", "OwningTeamId" },
            "the check must see the properties it is about (it would be vacuous over an empty set)");
        var team = Guid.NewGuid();

        var copy = InvokeWithOwner(details, "Resolved", team);

        NotCopied(details, copy, team).Should().BeEmpty(
            "WithOwner is a positional copy: a property added to NewContactDetails must be copied there (K3)");
        copy.OwningTeamId.Should().Be(team, "the resolved owner replaces whatever the details carried");
    }

    [Theory]
    [InlineData("NotApplicable")]
    [InlineData("Unconfigured")]
    [InlineData("LookupFailed")]
    public void WithOwner_ReturnsTheDetailsUnchanged_WhenNoOwnerResolved(string status)
    {
        var details = DetailsWithEveryPropertySet();

        InvokeWithOwner(details, status, Guid.NewGuid()).Should().Be(details);
    }

    [Fact]
    public void TheCopyCheck_FiresOnACopyThatDropsAProperty_AndNotOnACompleteOne()
    {
        // Controls for the detector itself: a positional copy that forgets Department and ExternalTenantId is caught, and a
        // complete copy is not.
        var details = DetailsWithEveryPropertySet();
        var team = Guid.NewGuid();
        var partial = new NewContactDetails(details.FirstName, details.LastName, details.Email) { OwningTeamId = team };
        var complete = details with { OwningTeamId = team };

        NotCopied(details, partial, team).Should().BeEquivalentTo(
            new[] { nameof(NewContactDetails.Department), nameof(NewContactDetails.ExternalTenantId) });
        NotCopied(details, complete, team).Should().BeEmpty();
    }

    private sealed class StoreBinderFactory(IContactIdentityStore store, ILogger? binderLog = null) : ContactIdentityBinderFactory(
        Mock.Of<IHttpClientFactory>(), new CaptureLoggerFactory(binderLog), Tenants(CustomerTenant), new FakeTimeProvider(Now), FactoryConfiguration())
    {
        public override IContactIdentityStore CreateStore(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
            => store;
    }

    /// <summary>A logger factory that hands every category the one capture (or discards, when none is given).</summary>
    private sealed class CaptureLoggerFactory(ILogger? capture) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => capture ?? NullLogger.Instance;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }
}
