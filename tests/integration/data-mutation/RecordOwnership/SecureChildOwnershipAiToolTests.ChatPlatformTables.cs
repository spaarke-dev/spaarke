using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 017-d2 (owner decision 2026-10-30, option b): the chat create's offered platform
/// tables — <c>task</c>, <c>contact</c> and <c>account</c> — are registered in <see cref="RecordTableRules"/>, so the ONE
/// server create (<see cref="RecordCreationService"/>) creates each with exactly what the owned create the chat uses today
/// (<see cref="OwnedChildWrite.CreateAsync"/>, <see cref="OwnedChildWrite.PathFor"/>'s Owned path) gives it: the resolver's
/// team owner, the same written row, and the same refusal under a secure matter.
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

    private Task<RecordCreationOutcome> ThroughTheService(string table, DataverseWriteItemMapper.MappedItem item) =>
        RouteRecordCreation
            .Over(_world.Resolver(), _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure())
            .CreateAsync(
                new RecordCreationRequest(table, new RecordCreationPayload.Mapped(item)),
                RecordCreationPrincipal.ForCaller(_user, CallerOid), CancellationToken.None);

    [Theory]
    [InlineData("task regarding a matter")]
    [InlineData("task regarding nothing")]
    [InlineData("contact")]
    [InlineData("account")]
    public async Task AChatPlatformTable_ThroughTheService_GetsTheOwnerAndRowTheOwnedCreateGives(string shape)
    {
        ChatPlatformCallerMayCreate();
        var table = TableOf(shape);
        OwnedChildWrite.PathFor(table, ChatItem(shape), "UserOwned").Should().Be(OwnedChildWrite.WritePath.Owned,
            "today the chat create sends this table through the owned create");

        var owned = await OwnedChildWrite.CreateAsync(
            _user, _world.Resolver(), _appOnly.Object, table, ChatItem(shape),
            serverSetLookupColumns: null, CallerOid, CancellationToken.None);
        var unified = await ThroughTheService(table, ChatItem(shape));

        owned.CreatedId.Should().NotBeNull(owned.Denied ?? owned.OwnerRefusal?.Reason ?? owned.SecureFilingRefused);
        unified.Succeeded.Should().BeTrue(unified.Refusal?.Detail);
        _appCreates.Should().HaveCount(2).And.OnlyContain(c => c.Table == table);
        var (viaOwned, viaService) = (_appCreates[0].Fields, _appCreates[1].Fields);
        Owner(viaOwned).Should().Be(Directory.ChildTeam, "the resolver's team: the regarding matter's, else the caller's unit's");
        Owner(viaService).Should().Be(Owner(viaOwned), "option b: the registry entry keeps today's owner");
        Written(viaService).Should().Be(Written(viaOwned), "the service writes the row the owned create writes");
    }

    [Fact]
    public async Task ATaskRegardingASecureMatter_IsRefusedOnBothPaths_AsTheSecureFilingRefusal_AndNothingIsCreated()
    {
        var owned = await OwnedChildWrite.CreateAsync(
            _user, _world.Resolver(), _appOnly.Object, "task", ChatTask(SecureMatter),
            serverSetLookupColumns: null, CallerOid, CancellationToken.None);
        var unified = await ThroughTheService("task", ChatTask(SecureMatter));

        owned.CreatedId.Should().BeNull();
        owned.SecureFilingRefused.Should().NotBeNull("a task is no reparentable child: the Secure team may not own it");
        unified.Succeeded.Should().BeFalse();
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
