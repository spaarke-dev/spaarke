using System.Text.Json;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ebilling-r1 review of f758480f3c (2026-10-30), field-mapping-server-write-path-r1 task 017c: the invoice submission,
/// approval and correction rows are written by e-billing's services as the record of a decision or a correction, so the chat
/// <c>dataverse.create_record</c> tool refuses them outright — the <c>sprk_document</c> hard block's mechanism and shape
/// (<see cref="ToolErrorCodes.ValidationFailed"/> before any Dataverse call) — although the record-creation registry now
/// has entries for them. Driven through the REAL handler over the scripted caller's Dataverse
/// (<see cref="SecureChildOwnershipAiToolTests.ScriptedUserClient"/>) and the real ownership resolver; the app-only create
/// seam records what it is asked to write.
/// </summary>
[Trait("status", "new")]
public sealed class ChatServerWrittenTableBlockTests : TypedToolHandlerTestFixture
{
    private static readonly Guid OrdinaryMatter = Guid.Parse("a2460000-0000-4000-8000-0000000017c2");
    private static readonly Guid Caller = Guid.Parse("a2460000-0000-4000-8000-0000000017ca");
    private static readonly Guid CallerOid = Guid.Parse("a2460000-0000-4000-8000-0000000017cb");

    private readonly Directory _world = Directory.Standard()
        .WithUser(Caller, CallerOid, Directory.ChildBu)
        .WithOrdinaryRoot("sprk_matter", OrdinaryMatter);

    private readonly SecureChildOwnershipAiToolTests.ScriptedUserClient _user = new(Caller);
    private readonly Mock<IFieldMappingDataverseService> _appOnly = new(MockBehavior.Strict);
    private readonly List<(string Table, Guid Id)> _appCreates = new();
    private readonly List<string> _reads = new();

    public ChatServerWrittenTableBlockTests()
    {
        // Every read the caller's Dataverse is asked (answered as usual: the override returns null).
        _user.GetOverride = path =>
        {
            _reads.Add(path);
            return null;
        };
        _appOnly
            .Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((t, id, _, _, _) => _appCreates.Add((t, id)))
            .Returns(Task.CompletedTask);
    }

    [Theory]
    [InlineData("sprk_invoicesubmission")]
    [InlineData("sprk_invoiceapproval")]
    [InlineData("sprk_invoicecorrection")]
    public async Task CreateRecord_AServerWrittenInvoiceFamilyTable_IsRefused_WithNoCreateCall(string table)
    {
        var result = await CreateRecord(table, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed, "the sprk_document hard block's refusal shape");
        result.ErrorMessage.Should().Contain(table).And.Contain("REJECTED").And.Contain("Do NOT retry");
        _appCreates.Should().BeEmpty("the row is never created by the application");
        _user.Posts.Should().BeEmpty("nor as the caller");
        _reads.Should().BeEmpty("refused before any Dataverse call, metadata included");
        _user.PrivilegeQuestions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_invoicesubmission")]
    [InlineData("sprk_invoiceapproval")]
    [InlineData("sprk_invoicecorrection")]
    public void ValidateChat_AServerWrittenInvoiceFamilyTable_IsRefusedBeforeTheConfirmation(string table)
    {
        // The pre-suspend leg: no confirm dialog is shown for a create that can never run.
        var result = Handler().ValidateChat(CreateContext(table, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter)),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)));

        result.IsValid.Should().BeFalse();
        string.Join(" ", result.Errors).Should().Contain(table).And.Contain("REJECTED");
    }

    [Fact]
    public async Task CreateRecord_AnOrdinaryRegistryTable_IsStillCreated_MustNotFire()
    {
        var validation = Handler().ValidateChat(
            CreateContext("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)));
        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        validation.IsValid.Should().BeTrue();
        result.Success.Should().BeTrue(result.ErrorMessage);
        _appCreates.Should().ContainSingle().Which.Table.Should().Be("sprk_todo");
        _reads.Should().NotBeEmpty("the ordinary create reached Dataverse (the read log is live)");
    }

    private DataverseCreateRecordHandler Handler() =>
        new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            _world.Resolver(), _appOnly.Object, IdentityNormalizationFixtures.NoLinkedContact(),
            SecureRootFilingGateFixtures.NothingSecure());

    private Task<ToolResult> CreateRecord(string table, params (string Column, JsonElement Value)[] item) =>
        Handler().ExecuteChatAsync(CreateContext(table, item), BuildAnalysisTool(nameof(DataverseCreateRecordHandler)),
            CancellationToken.None);

    private static ChatInvocationContext CreateContext(string table, params (string Column, JsonElement Value)[] item) =>
        BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
        {
            tablename = table,
            item = item.ToDictionary(i => i.Column, i => i.Value),
        })) with { UserId = CallerOid.ToString() };

    private static (string Column, JsonElement Value) Lookup(string column, string table, Guid id) =>
        (column, JsonSerializer.SerializeToElement(new { relatedTable = table, recordId = id }));
}
