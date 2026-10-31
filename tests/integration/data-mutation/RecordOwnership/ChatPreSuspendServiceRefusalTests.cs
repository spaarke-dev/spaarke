using System.Text.Json;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 017 review round 1 (F-2): <c>dataverse.create_record</c> pre-checked only the
/// creator column, <c>sprk_document</c> and the server-written e-billing tables, so a chat create the service refuses at step
/// 2 for a column it owns (the autonumber, a root's container / secure business unit / Access Permission) suspended for the
/// user's confirmation and THEN refused (AIR2-034: a create that can never succeed is refused before it suspends).
/// <see cref="DataverseCreateRecordHandler.ValidateChat"/> — which the confirmation gate runs before it suspends — now asks
/// the service's own pure rule and words the refusal as the post-confirm refusal is, so only the timing changed. Driven
/// through the REAL handler over the scripted caller's Dataverse and the real service.
/// </summary>
/// <remarks>
/// <b>Not covered, and why:</b> an unregistered table (<c>table_unsupported</c>) is refused by the service only when it is
/// team- or user-owned; that is table metadata, which a validation with no I/O does not have (an organisation-owned table is
/// a valid run-as-user create), so such a table still reaches its confirmation (pinned below as a must-not-fire).
/// </remarks>
[Trait("status", "new")]
public sealed class ChatPreSuspendServiceRefusalTests : TypedToolHandlerTestFixture
{
    private static readonly Guid Caller = Guid.Parse("a2460000-0000-4000-8000-0000000017f1");
    private static readonly Guid CallerOid = Guid.Parse("a2460000-0000-4000-8000-0000000017f2");

    private readonly Directory _world = Directory.Standard().WithUser(Caller, CallerOid, Directory.ChildBu);
    private readonly SecureChildOwnershipAiToolTests.ScriptedUserClient _user = new(Caller);
    private readonly Mock<IFieldMappingDataverseService> _appOnly = new(MockBehavior.Strict);
    private readonly List<string> _appCreates = new();
    private readonly List<string> _reads = new();

    public ChatPreSuspendServiceRefusalTests()
    {
        _user.GetOverride = path =>
        {
            _reads.Add(path);
            return null;
        };
        _appOnly
            .Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((t, _, _, _, _) => _appCreates.Add(t))
            .Returns(Task.CompletedTask);
    }

    public static TheoryData<string, string> TheServerOwnedColumnsTheServiceRefuses => new()
    {
        { "sprk_matter", "sprk_matternumber" },
        { "sprk_project", "sprk_projectnumber" },
        { "sprk_budget", "sprk_budgetnumber" },
        { "sprk_matter", "sprk_containerid" },
        { "sprk_matter", SecurableEntityRegistry.SecurityBusinessUnitAttribute },
        { "sprk_project", Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.Column },
        { "sprk_todo", "ownerid" },
    };

    [Theory]
    [MemberData(nameof(TheServerOwnedColumnsTheServiceRefuses))]
    public void ValidateChat_AColumnTheServiceOwns_IsRefusedBeforeTheConfirmation_InTheWordsOfThePostConfirmRefusal(
        string table, string column)
    {
        var args = Args(table, (column, JsonSerializer.SerializeToElement("x")));

        var validation = Handler().ValidateChat(Context(args), Tool());

        validation.IsValid.Should().BeFalse("a create the service will always refuse is refused before the dialog");
        string.Join(" ", validation.Errors).Should().Contain(column);
        _reads.Should().BeEmpty("the pre-check is pure: no Dataverse call");
        _appCreates.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(TheServerOwnedColumnsTheServiceRefuses))]
    public async Task APreSuspendRefusal_IsExactlyThePostConfirmRefusal_CodeAndText(string table, string column)
    {
        var args = Args(table, (column, JsonSerializer.SerializeToElement("x")));

        var validation = Handler().ValidateChat(Context(args), Tool());
        var afterConfirm = await Handler().ExecuteChatAsync(Context(args), Tool(), CancellationToken.None);

        afterConfirm.Success.Should().BeFalse();
        afterConfirm.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            "a BlockedColumn refusal keeps its tool code (NFR-07)");
        validation.IsValid.Should().BeFalse();
        string.Join(" ", validation.Errors).Should().Be(afterConfirm.ErrorMessage, "only the timing changed");
        _appCreates.Should().BeEmpty("nothing is created after the confirmation either");
    }

    [Fact]
    public void ValidateChat_AValidCreateOnTheSameTable_StillPassesToItsConfirmation_MustNotFire()
    {
        var args = Args("sprk_matter", ("sprk_mattername", JsonSerializer.SerializeToElement("Acme v. Beta")));

        Handler().ValidateChat(Context(args), Tool()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("sprk_userpreferences", "sprk_matternumber")] // a per-user table: the run-as-user path never reaches the service
    [InlineData("sprk_communication", "ownerid")] // unfiled, it keeps its creator: the run-as-user path
    [InlineData("phonecall", "sprk_matternumber")] // outside the registry: ownership is metadata, unknown to a pure validation
    public void ValidateChat_ATableThatMayTakeTheRunAsUserPath_IsNotPreRefusedByThisCheck_MustNotFire(string table, string column)
    {
        var args = Args(table, (column, JsonSerializer.SerializeToElement("x")));

        Handler().ValidateChat(Context(args), Tool()).IsValid.Should().BeTrue();
    }

    private DataverseCreateRecordHandler Handler() =>
        new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            _world.Resolver(), IdentityNormalizationFixtures.NoLinkedContact(),
            RouteRecordCreation.Default(_world.Resolver(), _appOnly.Object, SecureRootFilingGateFixtures.NothingSecure()));

    private static AnalysisTool Tool() => BuildAnalysisTool(nameof(DataverseCreateRecordHandler));

    private static ChatInvocationContext Context(string argumentsJson) =>
        BuildChatInvocationContext(toolArgumentsJson: argumentsJson) with { UserId = CallerOid.ToString() };

    private static string Args(string table, params (string Column, JsonElement Value)[] item) =>
        JsonSerializer.Serialize(new { tablename = table, item = item.ToDictionary(i => i.Column, i => i.Value) });
}
