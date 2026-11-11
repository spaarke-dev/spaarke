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
    [InlineData("sprk_externalidentifier")] // ebilling-r1 2026-10-30 (task 017 2d-3)
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
    [InlineData("sprk_externalidentifier")] // ebilling-r1 2026-10-30 (task 017 2d-3)
    public void ValidateChat_AServerWrittenInvoiceFamilyTable_IsRefusedBeforeTheConfirmation(string table)
    {
        // The pre-suspend leg: no confirm dialog is shown for a create that can never run.
        var result = Handler().ValidateChat(CreateContext(table, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter)),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)));

        result.IsValid.Should().BeFalse();
        string.Join(" ", result.Errors).Should().Contain(table).And.Contain("REJECTED");
    }

    // ── field-mapping-server-write-path-r1 task 042c fix round 4 (owner decision 2026-11-04, ontology D-204) ─────────────
    // A Signal, a Decision Record and a Budget Revision are created only by their writers: the same hard block, in both legs.

    [Theory(DisplayName = "042c fix round 4 (D-204, must fire): chat execute refuses a table only its writer creates, before any Dataverse call")]
    [InlineData("sprk_signal")]
    [InlineData("sprk_decisionrecord")]
    [InlineData("sprk_budgetrevision")]
    public async Task CreateRecord_ATableOnlyItsWriterCreates_IsRefused_WithNoCreateCall(string table)
    {
        _user.Held.UnionWith([$"prvCreate{table}", $"prvAppend{table}"]);

        var result = await CreateRecord(table, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed, "the sprk_document hard block's refusal shape");
        result.ErrorMessage.Should().Be(DataverseCreateRecordHandler.CreatedOnlyByItsWriterCreateBlockedMessage(table));
        result.ErrorMessage.Should().Contain(table).And.Contain("REJECTED").And.Contain("Do NOT retry");
        _appCreates.Should().BeEmpty("the row is never created by the application");
        _user.Posts.Should().BeEmpty("nor as the caller");
        _reads.Should().BeEmpty("refused before any Dataverse call, metadata included");
        _user.PrivilegeQuestions.Should().BeEmpty();
    }

    [Theory(DisplayName = "042c fix round 4 (D-204, must fire): chat validate refuses a table only its writer creates, before the confirmation")]
    [InlineData("sprk_signal")]
    [InlineData("sprk_decisionrecord")]
    [InlineData("sprk_budgetrevision")]
    [InlineData("SPRK_Signal")]
    public void ValidateChat_ATableOnlyItsWriterCreates_IsRefusedBeforeTheConfirmation(string table)
    {
        var result = Handler().ValidateChat(CreateContext(table, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter)),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(DataverseCreateRecordHandler.CreatedOnlyByItsWriterCreateBlockedMessage(table.ToLowerInvariant()));
        _reads.Should().BeEmpty();
    }

    [Theory(DisplayName = "042c fix round 4 (D-204, must not fire): the invoice-family block keeps its own words, and an ordinary table is not writer-only")]
    [InlineData("sprk_invoicesubmission")]
    [InlineData("sprk_invoiceapproval")]
    [InlineData("sprk_invoicecorrection")]
    [InlineData("sprk_externalidentifier")]
    public async Task CreateRecord_AServerWrittenInvoiceFamilyTable_KeepsItsOwnRefusal(string table)
    {
        var result = await CreateRecord(table, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter));

        DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter(table).Should().BeFalse();
        DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter("sprk_todo").Should().BeFalse();
        DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter("sprk_event").Should().BeFalse();
        result.ErrorMessage.Should().Be(DataverseCreateRecordHandler.ServerWrittenInvoiceFamilyCreateBlockedMessage(table));
        _appCreates.Should().BeEmpty();
    }

    // ── ebilling-r1 task 164a-1 (uac-r2 Q6): an invoice line's generic create alone is refused ─────────────────────────
    // The same hard block, in both legs, with the line's own words; its update and delete are not refused.

    [Fact(DisplayName = "164a-1 (must fire): chat execute refuses an invoice line, before any Dataverse call")]
    public async Task CreateRecord_AnInvoiceLine_IsRefused_WithNoCreateCall()
    {
        _user.Held.UnionWith(["prvCreatesprk_billingevent", "prvAppendsprk_billingevent"]);

        var result = await CreateRecord("sprk_billingevent", Lookup("sprk_matter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed, "the sprk_document hard block's refusal shape");
        result.ErrorMessage.Should().Be(DataverseCreateRecordHandler.CreatedOnlyByItsWriterCreateBlockedMessage("sprk_billingevent"));
        result.ErrorMessage.Should().Contain("sprk_billingevent").And.Contain("REJECTED").And.Contain("Do NOT retry")
            .And.Contain("Invoice lines").And.NotContain("signal");
        _appCreates.Should().BeEmpty("the row is never created by the application");
        _user.Posts.Should().BeEmpty("nor as the caller");
        _reads.Should().BeEmpty("refused before any Dataverse call, metadata included");
        _user.PrivilegeQuestions.Should().BeEmpty();
    }

    [Theory(DisplayName = "164a-1 (must fire): chat validate refuses an invoice line, before the confirmation")]
    [InlineData("sprk_billingevent")]
    [InlineData("SPRK_BillingEvent")]
    public void ValidateChat_AnInvoiceLine_IsRefusedBeforeTheConfirmation(string table)
    {
        var result = Handler().ValidateChat(CreateContext(table, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter)),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(DataverseCreateRecordHandler.CreatedOnlyByItsWriterCreateBlockedMessage("sprk_billingevent"));
        _reads.Should().BeEmpty();
    }

    [Fact(DisplayName = "164a-1 (must not fire): the invoice line is create-only — its update and delete are not refused")]
    public void AnInvoiceLine_IsRefusedForCreateOnly()
    {
        DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter("sprk_billingevent").Should().BeTrue();
        Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.RefuseWriterOnlyTableUpdate("sprk_billingevent").Should().BeNull();
        Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.RefuseWriterOnlyTableDelete("sprk_billingevent").Should().BeNull();
        // And the writer-only tables keep their own words.
        DataverseCreateRecordHandler.CreatedOnlyByItsWriterCreateBlockedMessage("sprk_signal").Should().Contain("signal");
    }

    // ── ebilling-r1 DEF-061 fix round 1: a Procedure Version is created and published only by ProcedureService; its own words ─────

    [Fact(DisplayName = "DEF-061 (must fire): chat execute refuses a Procedure Version in its own words, before any Dataverse call")]
    public async Task CreateRecord_AProcedureVersion_IsRefused_WithItsOwnWords_AndNoCreateCall()
    {
        _user.Held.UnionWith(["prvCreatesprk_procedureversion", "prvAppendsprk_procedureversion"]);

        var result = await CreateRecord("sprk_procedureversion", Lookup("sprk_matter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Be(DataverseCreateRecordHandler.CreatedOnlyByItsWriterCreateBlockedMessage("sprk_procedureversion"));
        result.ErrorMessage.Should().Contain("REJECTED").And.Contain("Do NOT retry")
            .And.Contain("Procedure versions are created and published only from the Procedure's versions page")
            .And.NotContain("signal").And.NotContain("decision").And.NotContain("Invoice lines");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        _reads.Should().BeEmpty();
    }

    [Fact(DisplayName = "DEF-061 (must not fire): the other writer-only tables keep their own words next to the Procedure Version's")]
    public void TheOtherWriterOnlyTables_KeepTheirWords()
    {
        DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter("sprk_procedureversion").Should().BeTrue();
        DataverseCreateRecordHandler.CreatedOnlyByItsWriterCreateBlockedMessage("sprk_signal").Should().Contain("signal").And.NotContain("versions page");
        DataverseCreateRecordHandler.CreatedOnlyByItsWriterCreateBlockedMessage("sprk_billingevent").Should().Contain("Invoice lines").And.NotContain("versions page");
    }

    // ── ebilling-r1 task 096b (EB-D-201): an invoice is created only by submission; this tool never creates one ──────────────
    // The block lives in the handler, not in the registry's GenericCreateOnlyRefused flag (which would also refuse the Office
    // quick create and the service seams), so the registry rule is asserted unchanged below.

    [Fact(DisplayName = "096b (must fire): chat execute refuses an invoice, before any Dataverse call")]
    public async Task CreateRecordTool_AnInvoice_IsRefused()
    {
        _user.Held.UnionWith(["prvCreatesprk_invoice", "prvAppendsprk_invoice", "prvAppendTosprk_invoice"]);

        var result = await CreateRecord("sprk_invoice", Lookup("sprk_matter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed, "the sprk_document hard block's refusal shape");
        result.ErrorMessage.Should().Be(DataverseCreateRecordHandler.InvoiceCreateBlockedMessage("sprk_invoice"));
        result.ErrorMessage.Should().Contain("sprk_invoice").And.Contain("REJECTED").And.Contain("Do NOT retry")
            .And.Contain("Create Invoice wizard");
        _appCreates.Should().BeEmpty("the row is never created by the application");
        _user.Posts.Should().BeEmpty("nor as the caller");
        _reads.Should().BeEmpty("refused before any Dataverse call, metadata included");
        _user.PrivilegeQuestions.Should().BeEmpty();
    }

    [Theory(DisplayName = "096b (must fire): chat validate refuses an invoice, before the confirmation")]
    [InlineData("sprk_invoice")]
    [InlineData("SPRK_Invoice")]
    public void CreateRecordTool_AnInvoice_IsRefusedBeforeTheConfirmation(string table)
    {
        var result = Handler().ValidateChat(CreateContext(table, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter)),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(DataverseCreateRecordHandler.InvoiceCreateBlockedMessage("sprk_invoice"));
        _reads.Should().BeEmpty();
    }

    [Fact(DisplayName = "096b (must not fire): the registry rule for the invoice is unchanged (the service still creates it as the caller); only this tool refuses")]
    public void TheInvoiceRegistryRule_IsNotMarkedCreateRefused()
    {
        var rule = Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.Find("sprk_invoice");
        rule.Should().NotBeNull();
        rule!.GenericCreateRefused.Should().BeFalse();
        DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter("sprk_invoice").Should().BeFalse();
        DataverseCreateRecordHandler.IsInvoiceCreate("sprk_invoice").Should().BeTrue();
        DataverseCreateRecordHandler.IsInvoiceCreate("sprk_invoicesubmission").Should().BeFalse();
        DataverseCreateRecordHandler.IsInvoiceCreate("sprk_billingevent").Should().BeFalse();
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
            _world.Resolver(), IdentityNormalizationFixtures.NoLinkedContact(), Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.RouteRecordCreation.Default(_world.Resolver(), _appOnly.Object, SecureRootFilingGateFixtures.NothingSecure()));

    private Task<ToolResult> CreateRecord(string table, params (string Column, JsonElement Value)[] item) =>
        Handler().ExecuteChatAsync(CreateContext(table, item), BuildAnalysisTool(nameof(DataverseCreateRecordHandler)),
            CancellationToken.None);

    private static ChatInvocationContext CreateContext(string table, params (string Column, JsonElement Value)[] item) =>
        BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
        {
            tablename = table,
            item = item.ToDictionary(i => i.Column, i => i.Value),
        })) with
        { UserId = CallerOid.ToString() };

    private static (string Column, JsonElement Value) Lookup(string column, string table, Guid id) =>
        (column, JsonSerializer.SerializeToElement(new { relatedTable = table, recordId = id }));
}
