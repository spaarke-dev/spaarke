using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 017-d2 (owner decision 2026-10-30, option b): the chat create's offered platform
/// tables — <c>task</c>, <c>contact</c> and <c>account</c> — are registered in <see cref="RecordTableRules"/>, so the ONE
/// server create (<see cref="RecordCreationService"/>) creates each with exactly what the owned create the chat used then
/// (<c>OwnedChildWrite.CreateAsync</c>, <see cref="OwnedChildWrite.PathFor"/>'s Owned path) gave it: the resolver's team
/// owner, the same written row, and the same refusal under a secure matter. Task 017 (2d-5) deleted that owned create; what
/// it wrote and its refusal are pinned as recorded constants, so these tests assert the same facts.
/// </summary>
/// <remarks>
/// A partial of <see cref="SecureChildOwnershipAiToolTests"/>, as item 9's service-request parity is: both paths run over the
/// SAME harness (the caller's scripted Dataverse, the REAL resolver over its directory, the app-only create it records).
/// Each test fails with its table's registry entry removed (the service refuses <c>record_creation.table_unsupported</c>).
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    /// <summary>A task regarding <paramref name="matter"/> (the polymorphic activity regarding), or regarding nothing.</summary>
    private static DataverseWriteItemMapper.MappedItem ChatTask(Guid? matter) =>
        matter is { } id
            ? new DataverseWriteItemMapper.MappedItem(
                $$"""{"subject":"Call the firm","regardingobjectid_sprk_matter@odata.bind":"/sprk_matters({{id:D}})"}""",
                ["subject", "regardingobjectid"])
            {
                Lookups = [new("regardingobjectid", "sprk_matter", "sprk_matters", id)],
            }
            : new DataverseWriteItemMapper.MappedItem("""{"subject":"Call the firm"}""", ["subject"]);

    private static DataverseWriteItemMapper.MappedItem ChatContact() =>
        new("""{"firstname":"Ada","lastname":"Lovelace"}""", ["firstname", "lastname"]);

    private static DataverseWriteItemMapper.MappedItem ChatAccount() =>
        new("""{"name":"Babbage & Co"}""", ["name"]);

    private static DataverseWriteItemMapper.MappedItem ChatItem(string shape) => shape switch
    {
        "task regarding a matter" => ChatTask(OrdinaryMatter),
        "task regarding nothing" => ChatTask(null),
        "contact" => ChatContact(),
        "account" => ChatAccount(),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private static string TableOf(string shape) => shape.StartsWith("task", StringComparison.Ordinal) ? "task" : shape;

    /// <summary>Every column the create wrote, in a stable order — the owner, the creator stamp and the caller's values.</summary>
    private static string Written(IReadOnlyDictionary<string, object?> fields) =>
        string.Join("; ", fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));

    private void ChatPlatformCallerMayCreate() =>
        _user.Held.UnionWith(["prvCreatecontact", "prvAppendcontact", "prvCreateaccount", "prvAppendaccount"]);

    private Task<RecordCreationOutcome> ThroughTheService(
        string table, DataverseWriteItemMapper.MappedItem item, ContactIdentityBinder? contactOwner = null) =>
        RouteRecordCreation
            .Over(_world.Resolver(customerContactOwner: contactOwner), _appOnly.Object, Restamper(), Shares(),
                SecureRootFilingGateFixtures.NothingSecure())
            .CreateAsync(
                new RecordCreationRequest(table, new RecordCreationPayload.Mapped(item)),
                RecordCreationPrincipal.ForCaller(_user, CallerOid), CancellationToken.None);

    /// <summary>
    /// Every column the owned create (the deleted <c>OwnedChildWrite.CreateAsync</c>) wrote for <paramref name="shape"/>, in
    /// <see cref="Written"/>'s order, recorded from a run of it before task 017 (2d-5) deleted it: the resolver's team
    /// (<see cref="Directory.ChildTeam"/>: the regarding matter's, else the caller's unit's) as owner, no creator person (none
    /// of these is a stamped root), and the caller's values unchanged.
    /// </summary>
    private static string OwnedCreateWrote(string shape)
    {
        var owner = $"ownerid@odata.bind=/teams({Directory.ChildTeam:D})";
        return shape switch
        {
            "task regarding a matter" =>
                $"{owner}; regardingobjectid_sprk_matter@odata.bind=/sprk_matters({OrdinaryMatter:D}); subject=Call the firm",
            "task regarding nothing" => $"{owner}; subject=Call the firm",
            "account" => $"name=Babbage & Co; {owner}",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    /// <summary>The owned create's refusal of a task regarding the secure matter (its <c>SecureFilingRefused</c>), recorded
    /// before task 017 (2d-5) deleted it.</summary>
    private const string OwnedCreateTaskSecureFilingRefusal =
        "A 'task' record cannot be filed under a secure record from chat, and was NOT created. Create it from the record itself.";

    [Theory]
    [InlineData("task regarding a matter")]
    [InlineData("task regarding nothing")]
    [InlineData("account")]
    public async Task AChatPlatformTable_ThroughTheService_GetsTheOwnerAndRowTheOwnedCreateGives(string shape)
    {
        // Task 017 (2d-5): the owned create this compared against is deleted; what it wrote is pinned (OwnedCreateWrote).
        ChatPlatformCallerMayCreate();
        var table = TableOf(shape);
        OwnedChildWrite.PathFor(table, ChatItem(shape), "UserOwned").Should().Be(OwnedChildWrite.WritePath.Owned,
            "the chat create sends this table down its owned path (now the service)");

        var unified = await ThroughTheService(table, ChatItem(shape));

        unified.Succeeded.Should().BeTrue(unified.Refusal?.Detail);
        var (createdTable, _, viaService) = _appCreates.Should().ContainSingle().Subject;
        createdTable.Should().Be(table);
        Owner(viaService).Should().Be(Directory.ChildTeam,
            "option b: the registry entry keeps the owned create's owner, the resolver's team");
        Written(viaService).Should().Be(OwnedCreateWrote(shape), "the service writes the row the owned create wrote");
    }

    // ── Task 052b (owner decision 2026-11-08): a chat contact takes the customer business unit's default Owner team ──────

    /// <summary>A binder over this environment whose customer business unit has exactly one default Owner team (or none).</summary>
    private static ContactIdentityBinder CustomerContactOwner(bool oneDefaultTeam)
    {
        var store = new InMemoryContactIdentityStore();
        if (oneDefaultTeam)
            store.DefaultTeams[CustomerContactOwnerTests.CustomerBu] = [CustomerContactOwnerTests.CustomerTeam];
        return IdentityBindingTestKit.OwnerAwareBinder(store, CustomerContactOwnerTests.CustomerBu);
    }

    [Fact]
    public async Task AChatContact_IsOwnedByTheCustomerUnitsDefaultTeam()
    {
        ChatPlatformCallerMayCreate();
        _world.WithTeam(CustomerContactOwnerTests.CustomerTeam, CustomerContactOwnerTests.CustomerBu, isDefault: true, teamType: 0);

        var created = await ThroughTheService("contact", ChatContact(), CustomerContactOwner(oneDefaultTeam: true));

        created.Succeeded.Should().BeTrue(created.Refusal?.Detail);
        var (table, _, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("contact");
        Owner(fields).Should().Be(CustomerContactOwnerTests.CustomerTeam, "the customer business unit's default Owner team");
        Written(fields).Should().Be(
            $"firstname=Ada; lastname=Lovelace; ownerid@odata.bind=/teams({CustomerContactOwnerTests.CustomerTeam:D})",
            "nothing is mapped onto a contact; only the owner is added");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AChatContact_WhoseCustomerTeamCannotBeResolved_IsRefused_AndNothingIsCreated(bool lookupComposed)
    {
        ChatPlatformCallerMayCreate();

        var created = await ThroughTheService(
            "contact", ChatContact(), lookupComposed ? CustomerContactOwner(oneDefaultTeam: false) : null);

        created.Succeeded.Should().BeFalse();
        created.Refusal!.Code.Should().Be(RecordOwnerRefusal.CustomerContactTeamUnresolved);
        _appCreates.Should().BeEmpty("never created with the caller's team in its place");
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task AChatContact_WhoseCustomerTeamCannotBeRead_IsRefusedAsUnavailable_AndNothingIsCreated()
    {
        ChatPlatformCallerMayCreate();
        var store = new InMemoryContactIdentityStore { FailTeamLookup = true };
        store.DefaultTeams[CustomerContactOwnerTests.CustomerBu] = [CustomerContactOwnerTests.CustomerTeam];

        var created = await ThroughTheService(
            "contact", ChatContact(), IdentityBindingTestKit.OwnerAwareBinder(store, CustomerContactOwnerTests.CustomerBu));

        created.Succeeded.Should().BeFalse();
        created.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Unavailable, "a fault is retryable, never a configuration refusal");
        _appCreates.Should().BeEmpty();
    }

    // UAC-r2 condition 1: each refusal reaches the chat as the tool's plain error (its stable code and a sentence), never a
    // thrown fault the host turns into a 500.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AChatContactRefusal_ReachesTheUserAsAPlainMessage_NeverABareFault(bool lookupFails)
    {
        ChatPlatformCallerMayCreate();
        var store = new InMemoryContactIdentityStore { FailTeamLookup = lookupFails };
        store.DefaultTeams[CustomerContactOwnerTests.CustomerBu] = lookupFails ? [CustomerContactOwnerTests.CustomerTeam] : [];

        var created = await ThroughTheService(
            "contact", ChatContact(), IdentityBindingTestKit.OwnerAwareBinder(store, CustomerContactOwnerTests.CustomerBu));

        var refusal = created.Refusal!;
        var code = Sprk.Bff.Api.Services.Ai.Handlers.DataverseCreateRecordHandler.RefusalErrorCode(refusal);
        var text = Sprk.Bff.Api.Services.Ai.Handlers.DataverseCreateRecordHandler.RefusalText(refusal, "contact", "corr-052b");
        text.Should().StartWith("The record ", "a plain sentence the chat shows");
        text.Should().NotContain("Exception").And.NotContain("InvalidOperation").And.NotContain(" at Sprk.");
        if (lookupFails)
        {
            refusal.Kind.Should().Be(RecordCreationRefusalKind.Unavailable);
            code.Should().Be(Sprk.Bff.Api.Services.Ai.ToolErrorCodes.InternalError, "a fault keeps the tool's fault code");
            text.Should().NotContain("CustomerBusinessUnitId", "a fault is not blamed on the configuration");
        }
        else
        {
            code.Should().Be(RecordOwnerRefusal.CustomerContactTeamUnresolved, "the resolver's own stable code");
            text.Should().Contain("owner could not be decided");
        }

        _appCreates.Should().BeEmpty();
    }

    // ── Review F1-b: the secure decision comes first for a chat contact filed under an invoice ──────────────────────

    private static DataverseWriteItemMapper.MappedItem ChatContactUnder(Guid invoice) =>
        new($$"""{"firstname":"Ada","lastname":"Lovelace","sprk_Invoice@odata.bind":"/sprk_invoices({{invoice:D}})"}""",
            ["firstname", "lastname", "sprk_invoice"])
        {
            Lookups = [new("sprk_invoice", "sprk_invoice", "sprk_invoices", invoice)],
        };

    [Fact]
    public async Task AChatContactUnderASecureInvoice_IsRefusedAsTheSecureFilingRefusal_AsToday()
    {
        ChatPlatformCallerMayCreate();
        CustomerContactOwnerTests.WithTheInvoices(_world);

        var created = await ThroughTheService(
            "contact", ChatContactUnder(CustomerContactOwnerTests.SecureInvoice), CustomerContactOwner(oneDefaultTeam: true));

        created.Succeeded.Should().BeFalse("never owned by the customer team out of the invoice's isolation");
        created.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.OwnerUnresolved);
        created.Refusal.OwnerDecision!.IsSecureOwner.Should().BeTrue("the resolver named the Secure team, as it does today");
        _appCreates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CustomerContactOwnerTests.SecureUnitParent.OwnedByTheSecureTeam_FlagUnset)]
    [InlineData(CustomerContactOwnerTests.SecureUnitParent.OwnedByTheSecureUnitsDefaultTeam)]
    [InlineData(CustomerContactOwnerTests.SecureUnitParent.OwnedByAnotherSecureUnitTeam)]
    public async Task AChatContactUnderAParentSecureByOwnership_IsNeverOwnedByTheCustomerTeam(
        CustomerContactOwnerTests.SecureUnitParent kind)
    {
        // UAC-r2 pin: the parent is secure by ownership only (its sprk_issecure unset); the contact stays in isolation.
        ChatPlatformCallerMayCreate();
        var invoice = CustomerContactOwnerTests.InvoiceInTheSecureUnit(_world, kind);

        var created = await ThroughTheService("contact", ChatContactUnder(invoice), CustomerContactOwner(oneDefaultTeam: true));

        created.Succeeded.Should().BeFalse("never owned by the customer team out of the invoice's isolation");
        created.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.OwnerUnresolved);
        created.Refusal.OwnerDecision!.IsSecureOwner.Should().BeTrue("the resolver named the Secure team");
        created.Refusal.OwnerDecision.OwningTeamId.Should().NotBe(CustomerContactOwnerTests.CustomerTeam);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task AChatContactUnderAnOrdinaryInvoice_IsOwnedByTheCustomerTeam()
    {
        ChatPlatformCallerMayCreate();
        CustomerContactOwnerTests.WithTheInvoices(_world);

        var created = await ThroughTheService(
            "contact", ChatContactUnder(CustomerContactOwnerTests.OrdinaryInvoice), CustomerContactOwner(oneDefaultTeam: true));

        created.Succeeded.Should().BeTrue(created.Refusal?.Detail);
        Owner(_appCreates.Should().ContainSingle().Subject.Fields).Should().Be(CustomerContactOwnerTests.CustomerTeam);
    }

    [Fact]
    public async Task ATaskRegardingASecureMatter_IsRefusedOnBothPaths_AsTheSecureFilingRefusal_AndNothingIsCreated()
    {
        // Task 017 (2d-5): the owned create refused this with OwnedCreateTaskSecureFilingRefusal (a task is no reparentable
        // child: the Secure team may not own it); the service refuses it at step 7 with the same words.
        var unified = await ThroughTheService("task", ChatTask(SecureMatter));

        unified.Succeeded.Should().BeFalse();
        unified.Refusal!.Detail.Should().Be(OwnedCreateTaskSecureFilingRefusal, "the owned create's words");
        unified.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.OwnerUnresolved, "step 7's secure-filing refusal");
        unified.Refusal.Code.Should().Be(RecordOwnerRefusal.NoOwnerSource);
        unified.Refusal.OwnerDecision!.IsSecureOwner.Should().BeTrue("the resolver named the Secure team, as it does today");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnregisteredChatTable_IsStillRefusedByTheService_AsTableUnsupported()
    {
        // Must-not-widen control: only the three offered tables were registered.
        var unified = await ThroughTheService("phonecall", new DataverseWriteItemMapper.MappedItem("""{"subject":"x"}""", ["subject"]));

        unified.Succeeded.Should().BeFalse();
        unified.Refusal!.Code.Should().Be(RecordCreationRefusal.TableUnsupportedCode);
        _appCreates.Should().BeEmpty();
    }
}
