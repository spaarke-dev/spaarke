using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// field-mapping-server-write-path-r1 task 048 (owner decision 2026-11-04, D-204 extended to updates): a Signal, a Decision Record
/// and a Budget Revision are changed only by their writers. The playbook update paths refuse the three tables whole, with the one
/// message and code of <see cref="RecordTableRules.RefuseWriterOnlyTableUpdate"/>, before any row is read or written, for every
/// identity: the Update Record node (a validation failure of its configuration), the core it shares with the action seam
/// (<see cref="UpdateRecordActionCore"/>, a configured payload and a confirmed proposal written as its user) and a playbook's output
/// mapping (<see cref="DataverseUpdateHandler"/>, app-only and as the run's user, with or without optimistic concurrency). Every
/// other table is written as before. The chat update tool is covered by <c>SecureChildOwnershipAiToolTests.WriterOnlyTableUpdate</c>,
/// the field-mapping push by <c>FieldMappingPushAuthorizationContractTests</c>; the writers' own updates keep their behaviour tests
/// (the Revise Budget commit in <c>SecureChildOwnershipAiToolTests.DecisionCommit</c>, <c>DecisionCommitServiceTests</c>, the
/// Signal close in <c>SignalWriterTests.AutomatedClose</c>) and never reach these paths (<c>WriterOnlyTablesWritersGuardTests</c>).
/// </summary>
public class WriterOnlyTablesUpdateRefusalTests
{
    private static readonly Guid Row = Guid.Parse("c0e00000-0000-4000-8000-000000000481");
    private static readonly Guid MissingRow = Guid.Parse("c0e00000-0000-4000-8000-000000000482");
    private static readonly Guid Person = Guid.Parse("c0e00000-0000-4000-8000-0000000004ca");

    // Strict: any call (a read or a write) the refusal should have prevented fails the test with the call named.
    private readonly Mock<IFieldMappingDataverseService> _writes = new(MockBehavior.Strict);

    public static TheoryData<string> WriterOnlyTables => new() { "sprk_signal", "sprk_decisionrecord", "sprk_budgetrevision" };

    // ── The Update Record node ───────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "048 (D-204, must fire): an Update Record node aimed at a table only its writer changes fails as a validation failure, nothing read or written")]
    [MemberData(nameof(WriterOnlyTables))]
    public async Task Node_ATableOnlyItsWriterChanges_FailsAsValidation_AndNothingIsReadOrWritten(string table)
    {
        // Strict scope factory: the node opens no scope (no metadata, owner, filing or isolation read) before the refusal.
        var result = await Node(new Mock<IServiceScopeFactory>(MockBehavior.Strict).Object)
            .ExecuteAsync(NodeContext(table), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Be(RecordTableRules.ChangedOnlyByItsWriterMessage(table));
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "048 (D-204, must not fire): an Update Record node aimed at an event is written")]
    public async Task Node_AnEvent_IsWritten()
    {
        AcceptUpdates();

        var result = await Node(new Mock<IServiceScopeFactory>(MockBehavior.Strict).Object)
            .ExecuteAsync(NodeContext("sprk_event"), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            "sprk_event", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_name")), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── The core the node and the action seam share ──────────────────────────────────────────────────────────────

    public static TheoryData<string, string> EveryTableAndSource()
    {
        var data = new TheoryData<string, string>();
        foreach (var table in new[] { "sprk_signal", "sprk_decisionrecord", "sprk_budgetrevision" })
        {
            data.Add(table, "configuration-app-only");
            data.Add(table, "configuration-as-person");
            data.Add(table, "confirmed-proposal-as-person");
        }

        return data;
    }

    [Theory(DisplayName = "048 (D-204, must fire): the Update Record core refuses a table only its writer changes for every payload source and identity")]
    [MemberData(nameof(EveryTableAndSource))]
    public async Task Core_ATableOnlyItsWriterChanges_IsRefused_ForEverySourceAndIdentity(string table, string source)
    {
        foreach (var record in new[] { Row, MissingRow })
        {
            var update = () => Core().UpdateAsync(CoreInput(table, record, source), CancellationToken.None);

            var refused = (await update.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
            (refused.RefusalCode, refused.EntityLogicalName, refused.Message)
                .Should().Be((RecordTableRules.ChangedOnlyByItsWriterReasonCode, table, RecordTableRules.ChangedOnlyByItsWriterMessage(table)));
        }

        _writes.Invocations.Should().BeEmpty("refused before any row is read or written, for any record id");
    }

    [Theory(DisplayName = "048 (D-204, must not fire): the Update Record core writes an event for every payload source and identity")]
    [InlineData("configuration-app-only")]
    [InlineData("configuration-as-person")]
    [InlineData("confirmed-proposal-as-person")]
    public async Task Core_AnEvent_IsWritten(string source)
    {
        AcceptUpdates();

        var written = await Core().UpdateAsync(CoreInput("sprk_event", Row, source), CancellationToken.None);

        written.Should().Equal("sprk_name");
    }

    // ── A playbook's output mapping ──────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, ConcurrencyMode, bool> EveryTableModeAndIdentity()
    {
        var data = new TheoryData<string, ConcurrencyMode, bool>();
        foreach (var table in new[] { "sprk_signal", "sprk_decisionrecord", "sprk_budgetrevision" })
        {
            foreach (var mode in new[] { ConcurrencyMode.None, ConcurrencyMode.Optimistic })
            {
                data.Add(table, mode, false);
                data.Add(table, mode, true);
            }
        }

        return data;
    }

    [Theory(DisplayName = "048 (D-204, must fire): a playbook's output mapping refuses a table only its writer changes, app-only and as the run's user, before any read")]
    [MemberData(nameof(EveryTableModeAndIdentity))]
    public async Task OutputMapping_ATableOnlyItsWriterChanges_IsRefused_BeforeAnyReadOrWrite(string table, ConcurrencyMode mode, bool asPerson)
    {
        var world = World(table);

        foreach (var record in new[] { Row, MissingRow })
        {
            var update = () => Handler(world).UpdateAsync(
                table, record, new Dictionary<string, object?> { ["sprk_name"] = "Renamed" }, mode, maxRetries: 1, CancellationToken.None,
                impersonateSystemUserId: asPerson ? Person : null);

            var refused = (await update.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
            (refused.RefusalCode, refused.EntityLogicalName, refused.Message)
                .Should().Be((RecordTableRules.ChangedOnlyByItsWriterReasonCode, table, RecordTableRules.ChangedOnlyByItsWriterMessage(table)));
        }

        _writes.Invocations.Should().BeEmpty("refused before the row version is read or anything is written");
        (await world.Service.RetrieveAsync(table, Row, ["sprk_name"], CancellationToken.None))
            .Contains("sprk_name").Should().BeFalse("the stored row is unchanged");
    }

    [Theory(DisplayName = "048 (D-204, must not fire): a playbook's output mapping writes an event, app-only and as the run's user")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutputMapping_AnEvent_IsWritten(bool asPerson)
    {
        AcceptUpdates();

        await Handler(World("sprk_event")).UpdateAsync(
            "sprk_event", Row, new Dictionary<string, object?> { ["sprk_name"] = "Renamed" }, ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None, impersonateSystemUserId: asPerson ? Person : null);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            "sprk_event", Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_name")),
            asPerson ? Person : null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The field-mapping push's child writer (task 048 fix round 1) ─────────────────────────────────────────────

    [Theory(DisplayName = "048 fix 1 (D-204, must fire): the push's child writer fails every child of a table only its writer changes, in a plain sentence, writing nothing")]
    [MemberData(nameof(WriterOnlyTables))]
    public async Task PushChildWriter_ATableOnlyItsWriterChanges_FailsEveryChild_AndNothingIsWritten(string table)
    {
        var world = World(table);

        var (updated, failed, skipped, errors, _) = await Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object, world.Restamper,
            [new Sprk.Bff.Api.Models.FieldMapping.FieldMappingRuleDto
            {
                SourceField = "sprk_name", TargetField = "sprk_name", SourceFieldType = "String", TargetFieldType = "String", Priority = 1,
            }],
            new Dictionary<string, object?> { ["sprk_name"] = "Renamed" },
            table, [Row, MissingRow], Person, NullLogger.Instance, CancellationToken.None);

        (updated, failed, skipped).Should().Be((0, 2, 0));
        errors.Select(e => (e.RecordId, e.Error)).Should().Equal(
            (Row, RecordTableRules.ChangedOnlyByItsWriterMessage(table)), (MissingRow, RecordTableRules.ChangedOnlyByItsWriterMessage(table)));
        _writes.Invocations.Should().BeEmpty();
    }

    // ── The wording: a plain sentence on the paths people read; the model's wording only on the chat tool ─────────

    [Theory(DisplayName = "048 fix 1: the refusal people read is a plain sentence with the code; the chat tool's wording is the model's")]
    [MemberData(nameof(WriterOnlyTables))]
    public void TheMessage_IsPlainOffChat_AndTheModelsOnChat(string table)
    {
        var plain = RecordTableRules.ChangedOnlyByItsWriterMessage(table);
        var chat = Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler.ChangedOnlyByItsWriterChatMessage(table);

        plain.Should().Be($"'{table}' records are changed only by Spaarke itself and can't be changed by this update; nothing was written. (record_update.changed_only_by_its_writer)");
        plain.Should().NotContain("Do NOT").And.NotContain("REJECTED");
        chat.Should().Contain("Do NOT retry this update").And.Contain("(record_update.changed_only_by_its_writer)");
        RecordTableRules.RefuseWriterOnlyTableUpdate(table)!.RefusalCode.Should().Be("record_update.changed_only_by_its_writer");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private void AcceptUpdates() =>
        _writes.Setup(w => w.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

    private static UpdateRecordActionInput CoreInput(string table, Guid record, string source) => new(
        table, record, FieldMappings: null,
        LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "Renamed" }, Lookups: null,
        ImpersonateSystemUserId: source == "configuration-app-only" ? null : Person,
        PayloadSource: source == "confirmed-proposal-as-person" ? UpdateRecordPayloadSource.ConfirmedProposal : UpdateRecordPayloadSource.Configuration);

    private UpdateRecordActionCore Core() => new(
        _writes.Object,
        new ServiceCollection().AddSingleton(EntitySetNameFixtures.Service()).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        NullLogger.Instance);

    private UpdateRecordNodeExecutor Node(IServiceScopeFactory scopes) => new(
        new TemplateEngine(NullLogger<TemplateEngine>.Instance), _writes.Object, scopes, NullLogger<UpdateRecordNodeExecutor>.Instance);

    private static NodeExecutionContext NodeContext(string table)
    {
        var actionId = Guid.NewGuid();
        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Update Record Node",
                ExecutionOrder = 1,
                OutputVariable = "updateResult",
                ConfigJson = $$"""{ "entityLogicalName": "{{table}}", "recordId": "{{Row}}", "fields": { "sprk_name": "Renamed" } }""",
                IsActive = true,
            },
            Action = new AnalysisAction { Id = actionId, Name = "Update Record" },
            ExecutorType = ExecutorType.UpdateRecord,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant",
            StartedBy = PlaybookRunStartedBy.System,
        };
    }

    /// <summary>A world holding <see cref="Row"/> of <paramref name="table"/>; <see cref="MissingRow"/> is not in it.</summary>
    private static StampWorld World(string table) => new StampWorld().Row(table, Row);

    private DataverseUpdateHandler Handler(StampWorld world) => new(
        _writes.Object, world.Service, world.Restamper, new RecordOwnershipResolverDouble(),
        SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
        SecureChildReconcilerFixtures.OverEmptyWorld());
}
