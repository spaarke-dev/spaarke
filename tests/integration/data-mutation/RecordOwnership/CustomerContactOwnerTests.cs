using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Extensions;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 052b (owner decision 2026-11-08): every contact the server decides an owner for — the
/// chat create (step 7) and the repair pass (Repair's owner step) — is owned by the single default Owner team of
/// <c>ExternalAccess:CustomerBusinessUnitId</c>, answered by the identity-contact creates' own lookup
/// (<see cref="ContactIdentityBinder"/>), composed into the real <see cref="RecordOwnershipResolver"/>. When that team cannot be
/// resolved the resolver refuses (<see cref="RecordOwnerRefusal.CustomerContactTeamUnresolved"/>); it never answers the
/// creator's or the acting user's team. An unreadable lookup is a fault and propagates.
/// </summary>
/// <remarks>
/// The resolver, the binder and the binder's lookup are the real code; only the binder's store is in memory
/// (<see cref="InMemoryContactIdentityStore"/>) and the resolver's directory is <see cref="Directory"/>. Without the rule, every
/// contact question below is answered with the caller's unit's team (<see cref="Directory.GeneralTeam"/>), as any unfiled child is.
/// </remarks>
[Trait("status", "new")]
public sealed class CustomerContactOwnerTests
{
    internal static readonly Guid CustomerBu = Guid.Parse("a052b000-0000-4000-8000-0000000000b1");
    internal static readonly Guid CustomerTeam = Guid.Parse("a052b000-0000-4000-8000-0000000000b2");
    private const string SecureBusinessUnitName = "Secure Record";

    private readonly Directory _directory = Directory.Standard();
    private readonly InMemoryContactIdentityStore _store = new();

    public enum Unresolvable
    {
        SettingUnset,
        SettingEmptyGuid,
        CustomerUnitIsTheSecureRecordUnit,
        SecureRecordUnitAmbiguous,
        NoDefaultOwnerTeam,
        TwoDefaultOwnerTeams,
    }

    /// <summary>A contact question as the chat create and Repair ask it: no parent, the caller (or the row's owner) acting.</summary>
    private static RecordOwnershipContext AContact(string table = "contact") => new()
    {
        ChildEntityLogicalName = table,
        CallerSystemUserId = Directory.CallerUserId,
    };

    private RecordOwnershipResolver Resolver(Guid? customerBusinessUnit, bool composed = true) =>
        _directory.Resolver(customerContactOwner: composed
            ? IdentityBindingTestKit.OwnerAwareBinder(_store, customerBusinessUnit)
            : null);

    /// <summary>Arranges the store for an unresolvable team and returns the setting's value.</summary>
    private Guid? Arrange(Unresolvable cause)
    {
        switch (cause)
        {
            case Unresolvable.SettingUnset:
                _store.DefaultTeams[CustomerBu] = [CustomerTeam];
                return null;
            case Unresolvable.SettingEmptyGuid:
                _store.DefaultTeams[CustomerBu] = [CustomerTeam];
                return Guid.Empty;
            case Unresolvable.CustomerUnitIsTheSecureRecordUnit:
                _store.DefaultTeams[CustomerBu] = [CustomerTeam];
                _store.BusinessUnitsByName[SecureBusinessUnitName] = [CustomerBu];
                return CustomerBu;
            case Unresolvable.SecureRecordUnitAmbiguous:
                _store.DefaultTeams[CustomerBu] = [CustomerTeam];
                _store.BusinessUnitsByName[SecureBusinessUnitName] = [Guid.NewGuid(), Guid.NewGuid()];
                return CustomerBu;
            case Unresolvable.NoDefaultOwnerTeam:
                return CustomerBu;
            case Unresolvable.TwoDefaultOwnerTeams:
                _store.DefaultTeams[CustomerBu] = [CustomerTeam, Guid.NewGuid()];
                return CustomerBu;
            default:
                throw new ArgumentOutOfRangeException(nameof(cause));
        }
    }

    // ── Must not fire: the customer team owns every contact ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AContact_IsOwnedByTheCustomerUnitsDefaultTeam_NotTheActingUsersTeam()
    {
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];

        var answer = await Resolver(CustomerBu).ResolveOwnerAsync(AContact(), CancellationToken.None);

        answer.IsOwned.Should().BeTrue(answer.Reason);
        answer.OwningTeamId.Should().Be(CustomerTeam, "a contact is owned by the customer business unit's default Owner team");
        answer.OwningTeamId.Should().NotBe(Directory.GeneralTeam, "never the acting user's team");
        _store.Reads.Should().Contain("owner-team", "the identity-contact creates' own lookup answered");
    }

    [Fact]
    public async Task AnotherUnfiledChild_StillGetsTheActingUsersTeam_TheRuleIsTheContactsOnly()
    {
        // Control: the contact rule does not widen to other tables.
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];

        var answer = await Resolver(CustomerBu).ResolveOwnerAsync(AContact("task"), CancellationToken.None);

        answer.OwningTeamId.Should().Be(Directory.GeneralTeam, "the acting user's unit's team, as before");
        _store.Reads.Should().NotContain("owner-team");
    }

    // ── The secure decision comes first: a contact filed under a secure record keeps today's answer ─────────────────────

    internal static readonly Guid SecureInvoice = Guid.Parse("a052b000-0000-4000-8000-0000000001c1");
    internal static readonly Guid OrdinaryInvoice = Guid.Parse("a052b000-0000-4000-8000-0000000001c2");

    /// <summary>An invoice owned in the Secure Record business unit by its named team (secure), and an ordinary one.</summary>
    internal static Directory WithTheInvoices(Directory directory) => directory
        .WithRecord("sprk_invoice", SecureInvoice, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam)
        .WithRecord("sprk_invoice", OrdinaryInvoice, Directory.ChildBu, owningTeam: Directory.ChildTeam);

    private static RecordOwnershipContext AContactUnder(Guid invoice) =>
        AContact() with { Parents = [new RecordOwnershipParent("sprk_invoice", invoice)] };

    [Fact]
    public async Task AContactUnderASecureInvoice_KeepsTodaysSecureAnswer_NeverTheCustomerTeam()
    {
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];
        WithTheInvoices(_directory);

        var answer = await Resolver(CustomerBu).ResolveOwnerAsync(AContactUnder(SecureInvoice), CancellationToken.None);

        answer.IsSecureOwner.Should().BeTrue(answer.Reason);
        answer.OwningTeamId.Should().NotBe(CustomerTeam, "the customer-team rule never takes a contact out of a secure record's isolation");
        _store.Reads.Should().NotContain("owner-team", "the secure decision answered first");
    }

    internal static readonly Guid UnitWithNoDefaultTeam = Guid.Parse("a052b000-0000-4000-8000-0000000001b0");
    internal static readonly Guid InvoiceInAUnitWithNoDefaultTeam = Guid.Parse("a052b000-0000-4000-8000-0000000001c3");

    [Fact]
    public async Task AContactUnderAnOrdinaryInvoice_WhoseUnitHasNoDefaultOwnerTeam_IsRefused_EveryParentRefusalWins()
    {
        // Review F4 (agreed with UAC-r2: every parent refusal wins, fail closed): the invoice is not secure, but its own filing
        // cannot be decided, so the contact is refused — never given the customer team in its place.
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];
        _directory
            .WithBusinessUnit(UnitWithNoDefaultTeam, "Unit with no default Owner team", parent: Directory.GeneralBu)
            .WithRecord("sprk_invoice", InvoiceInAUnitWithNoDefaultTeam, UnitWithNoDefaultTeam);

        var answer = await Resolver(CustomerBu).ResolveOwnerAsync(AContactUnder(InvoiceInAUnitWithNoDefaultTeam), CancellationToken.None);

        answer.IsRefused.Should().BeTrue(answer.Reason);
        answer.RefusalCode.Should().Be(RecordOwnerRefusal.NoDefaultOwnerTeam);
        answer.OwningTeamId.Should().NotBe(CustomerTeam, "a parent's refusal is never replaced by the customer team");
    }

    [Fact]
    public async Task AContactUnderAnOrdinaryInvoice_IsOwnedByTheCustomerTeam()
    {
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];
        WithTheInvoices(_directory);

        var answer = await Resolver(CustomerBu).ResolveOwnerAsync(AContactUnder(OrdinaryInvoice), CancellationToken.None);

        answer.OwningTeamId.Should().Be(CustomerTeam, "a contact under records that are not secure takes the customer team");
        answer.IsSecureOwner.Should().BeFalse();
    }

    [Fact]
    public async Task AContactUnderAnInvoiceThatCannotBeRead_IsRefusedAsToday_NeverTheCustomerTeam()
    {
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];

        var answer = await Resolver(CustomerBu).ResolveOwnerAsync(AContactUnder(Guid.NewGuid()), CancellationToken.None);

        answer.IsRefused.Should().BeTrue("a named parent that cannot be read is undecided: never read as not secure");
        answer.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        _store.Reads.Should().NotContain("owner-team");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReFilingAContactUnderAnInvoice_DecidesTheSameAnswer(bool secure)
    {
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];
        var contact = Guid.NewGuid();
        var invoice = secure ? SecureInvoice : OrdinaryInvoice;
        WithTheInvoices(_directory).WithRecord("contact", contact, Directory.GeneralBu, extra: new()
        {
            ["owninguser"] = new Microsoft.Xrm.Sdk.EntityReference("systemuser", Directory.CallerUserId),
            ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("systemuser", Directory.CallerUserId),
        });
        var change = new Microsoft.Xrm.Sdk.EntityReference("sprk_invoice", invoice);
        var request = new RecordReparent
        {
            EntityLogicalName = "contact",
            RecordId = contact,
            ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?> { ["sprk_invoice"] = change },
            CallerSystemUserId = Directory.CallerUserId,
        };

        var answer = await Resolver(CustomerBu).ReparentAsync(
            request,
            _ =>
            {
                _directory.Write("contact", contact, new Dictionary<string, object?> { ["sprk_invoice"] = change });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        if (secure)
        {
            answer.OwningTeamId.Should().NotBe(CustomerTeam, "re-filed under a secure invoice: today's secure answer");
            _store.Reads.Should().NotContain("owner-team");
        }
        else
        {
            answer.OwningTeamId.Should().Be(CustomerTeam, answer.Reason);
        }
    }

    // ── Must fire: no team, no owner — and never the creator's ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(Unresolvable.SettingUnset)]
    [InlineData(Unresolvable.SettingEmptyGuid)]
    [InlineData(Unresolvable.CustomerUnitIsTheSecureRecordUnit)]
    [InlineData(Unresolvable.SecureRecordUnitAmbiguous)]
    [InlineData(Unresolvable.NoDefaultOwnerTeam)]
    [InlineData(Unresolvable.TwoDefaultOwnerTeams)]
    public async Task AContact_WhoseCustomerTeamCannotBeResolved_IsRefused_NeverGivenTheCreatorsTeam(Unresolvable cause)
    {
        var setting = Arrange(cause);

        var answer = await Resolver(setting).ResolveOwnerAsync(AContact(), CancellationToken.None);

        answer.IsRefused.Should().BeTrue();
        answer.IsOwned.Should().BeFalse();
        answer.OwningTeamId.Should().BeNull("no team is named, so the acting user's team cannot be applied");
        answer.RefusalCode.Should().Be(RecordOwnerRefusal.CustomerContactTeamUnresolved);
    }

    [Fact]
    public async Task WithTheSettingUnset_TheRefusalIsLogged_AndNoTeamIsNamed()
    {
        // ExternalAccess:CustomerBusinessUnitId is unset on dev today. The store has a default team: only the setting decides.
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];
        var log = new Sprk.Bff.Api.Tests.Services.Communication.CapturingLogger<RecordOwnershipResolver>();
        var resolver = _directory.Resolver(
            logger: log, customerContactOwner: IdentityBindingTestKit.OwnerAwareBinder(_store, customerBusinessUnitId: null));

        var answer = await resolver.ResolveOwnerAsync(AContact(), CancellationToken.None);

        answer.RefusalCode.Should().Be(RecordOwnerRefusal.CustomerContactTeamUnresolved);
        answer.OwningTeamId.Should().BeNull("neither the creator's team nor any default team");
        log.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error
                                          && e.Message.Contains("ExternalAccess:CustomerBusinessUnitId"));
        _store.Reads.Should().NotContain("owner-team");
    }

    [Fact]
    public async Task AResolverComposedWithoutTheLookup_RefusesEveryContact()
    {
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];

        var answer = await Resolver(CustomerBu, composed: false).ResolveOwnerAsync(AContact(), CancellationToken.None);

        answer.IsRefused.Should().BeTrue();
        answer.RefusalCode.Should().Be(RecordOwnerRefusal.CustomerContactTeamUnresolved);
    }

    [Fact]
    public async Task ABinderOverAnotherEnvironment_BindsNoOwner_SoTheContactIsRefused()
    {
        var foreign = IdentityBindingTestKit.Binder(_store);

        var answer = await _directory.Resolver(customerContactOwner: foreign).ResolveOwnerAsync(AContact(), CancellationToken.None);

        answer.RefusalCode.Should().Be(RecordOwnerRefusal.CustomerContactTeamUnresolved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnUnreadableLookup_IsAFault_NotARefusal(bool businessUnitRead)
    {
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];
        if (businessUnitRead)
            _store.FailBusinessUnitLookup = true;
        else
            _store.FailTeamLookup = true;

        var act = () => Resolver(CustomerBu).ResolveOwnerAsync(AContact(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>("a read fault propagates, as every resolver read's does");
    }

    [Fact]
    public async Task TheProductionRegistration_ComposesTheRegisteredLookupIntoTheResolver()
    {
        // AddRecordOwnershipResolver registers the resolver by type: the container fills its optional lookup from the
        // ContactIdentityBinder singleton ExternalAccessModule registers. Without that, every contact would be refused.
        _store.DefaultTeams[CustomerBu] = [CustomerTeam];
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Mock.Of<IGenericEntityService>())
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(IdentityBindingTestKit.OwnerAwareBinder(_store, CustomerBu))
            .AddRecordOwnershipResolver();

        var resolver = services.BuildServiceProvider().GetRequiredService<IRecordOwnershipResolver>();
        var answer = await resolver.ResolveOwnerAsync(AContact(), CancellationToken.None);

        answer.OwningTeamId.Should().Be(CustomerTeam);
    }

    [Fact]
    public void TheRefusalCode_IsStable()
    {
        RecordOwnerRefusal.CustomerContactTeamUnresolved.Should().Be("record_owner_customer_contact_team_unresolved");
    }
}
