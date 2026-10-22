using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
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
        Guid? customerBusinessUnitId, IDictionary<string, string?>? configuration = null, InMemoryContactIdentityStore? store = null)
    {
        var external = new Mock<IOptionsMonitor<ExternalAccessOptions>>();
        external.SetupGet(o => o.CurrentValue).Returns(new ExternalAccessOptions { CustomerBusinessUnitId = customerBusinessUnitId });
        return new ContactIdentityBinder(
            store ?? _store, Tenants(CustomerTenant), new FakeTimeProvider(Now), new BindingLogCapture<ContactIdentityBinder>(),
            external.Object, new ConfigurationBuilder().AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()).Build());
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

    [Fact]
    public async Task TheReportOnlyLinkPass_WritesNothing_AndReadsNoOwner()
    {
        // applyWrites = false never creates, so it never asks for an owner either (no refusal in a dry run).
        var binder = OwnedBinder(customerBusinessUnitId: null);
        var userId = GivenALicensedUserWithNoContact(out _);

        var result = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: false, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.CreatedAndLinked, "the plan is unchanged");
        result.WritesApplied.Should().BeFalse();
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().NotContain(new[] { "owner-team", "business-unit" });
    }

    // ── Must not fire: an existing contact needs no owner ──────────────────────────────────────────

    [Fact]
    public async Task BindingAnExistingContact_NeedsNoOwner_EvenWithTheSettingUnset()
    {
        var contactId = Guid.NewGuid();
        _store.AddContact(contactId, email: Email);
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

    // ── The binder over ANOTHER environment is unchanged (the setting names a unit of THIS environment) ────

    [Fact]
    public async Task ABinderOverAnotherEnvironment_StillCreatesWithoutAnOwner_AndReadsNoOwnerTeam()
    {
        var oid = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var other = new InMemoryContactIdentityStore();
        other.AddSystemUser(userId, oid, "new.user@demo.spaarke.com");
        var factory = new StoreBinderFactory(other);

        var binder = factory.CreateBinder("https://spaarke-demo.crm.dynamics.com", _ => Task.FromResult("token"));
        var result = await binder.EnsureSystemUserLinkAsync(userId, applyWrites: true, CancellationToken.None);

        result.Outcome.Should().Be(SystemUserLinkOutcome.CreatedAndLinked);
        var payload = other.CreatePayloads.Should().ContainSingle().Subject;
        payload.Should().NotContainKey(OwnerBind);
        other.Reads.Should().NotContain(new[] { "owner-team", "business-unit" });
    }

    [Fact]
    public void TheCustomerOwnerInputs_ComeTogether()
    {
        var external = Mock.Of<IOptionsMonitor<ExternalAccessOptions>>();
        var configuration = new ConfigurationBuilder().Build();

        FluentActions.Invoking(() => new ContactIdentityBinder(
                _store, Tenants(CustomerTenant), new FakeTimeProvider(Now), new BindingLogCapture<ContactIdentityBinder>(), external))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new ContactIdentityBinder(
                _store, Tenants(CustomerTenant), new FakeTimeProvider(Now), new BindingLogCapture<ContactIdentityBinder>(),
                externalAccess: null, configuration: configuration))
            .Should().Throw<ArgumentException>();
    }

    // ── The DI-registered binder is the one over this BFF's own environment: it carries the setting ────────

    private ContactIdentityBinder ResolveFromDi(Guid? customerBusinessUnitId)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddSingleton<IContactIdentityStore>(_store);
        services.AddSingleton(Tenants(CustomerTenant));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.Configure<ExternalAccessOptions>(o => o.CustomerBusinessUnitId = customerBusinessUnitId);
        services.AddSingleton<ContactIdentityBinder>();
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

    private sealed class StoreBinderFactory(IContactIdentityStore store) : ContactIdentityBinderFactory(
        Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, Tenants(CustomerTenant), new FakeTimeProvider(Now))
    {
        public override IContactIdentityStore CreateStore(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
            => store;
    }
}
