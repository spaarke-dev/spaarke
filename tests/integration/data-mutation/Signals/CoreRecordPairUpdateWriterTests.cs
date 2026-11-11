using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// The core record pair (<c>sprk_corerecordtype</c> + <c>sprk_corerecordid</c>) of a Signal and a Decision Record is fixed after
/// create on BFF update paths (<c>RecordTableRule.FixedAfterCreateColumns</c>). Driven here: the Update Record playbook node and
/// the action seam's confirmed-proposal apply (<see cref="UpdateRecordActionCore"/>), a playbook's output mapping
/// (<see cref="DataverseUpdateHandler"/>) and the field-mapping push (<see cref="FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync"/>).
/// Each refuses in its own style with nothing written: the first two throw <see cref="RecordOwnerUnresolvedException"/> before
/// any read or write, the push skips the rule. The check is shape-only, so it answers the same for any record id. The chat update
/// tool is covered by <c>SecureChildOwnershipAiToolTests.CoreRecordPairUpdate</c>.
/// </summary>
/// <remarks>
/// Precedence: on a Signal, the app-only playbook writers already refuse the pair as an access column
/// (<see cref="InheritedAccessPermission.AccessColumns"/>, pinned by <c>AccessTableAppOnlyWriterTests</c>) and keep that code;
/// the update-only rule answers where that refusal is not asked (a Decision Record, a confirmed proposal, the push).
/// </remarks>
public class CoreRecordPairUpdateWriterTests
{
    private const string FixedAfterCreateCode = "record_update.fixed_after_create";

    private static readonly Guid Row = Guid.Parse("c0e00000-0000-4000-8000-000000000101");
    private static readonly Guid MissingRow = Guid.Parse("c0e00000-0000-4000-8000-000000000102");
    private static readonly Guid Caller = Guid.Parse("c0e00000-0000-4000-8000-0000000001ca");
    private static readonly string Value = Guid.Parse("c0e00000-0000-4000-8000-0000000001ff").ToString("D");

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>
    /// The doubles answer for <see cref="Row"/> as an existing row and for <see cref="MissingRow"/> as the real service answers
    /// for a row that does not exist: a read comes back with no columns (<c>DataverseWebApiService.RetrieveRecordFieldsAsync</c>
    /// on a 404); <see cref="World"/> holds <see cref="Row"/> only, and its read of any other id faults as Dataverse's does. So
    /// "the same refusal for any record id" can fail. A Signal or Decision Record update is sent through
    /// <c>UpdateExistingRecordFieldsAsync</c> (update-only, <c>If-Match: *</c>); it is not faked to throw for a missing id —
    /// the any-id tests assert instead that no call reached <see cref="_writes"/> for either id.
    /// </summary>
    public CoreRecordPairUpdateWriterTests()
    {
        _writes.Setup(w => w.RetrieveRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] columns, CancellationToken _) => id == MissingRow
                ? new Dictionary<string, object?>()
                : columns.ToDictionary(c => c, _ => (object?)"Existing"));
    }

    /// <summary>The refusal's exact words for <c>sprk_corerecordid</c> of <paramref name="table"/>.</summary>
    private static string FixedAfterCreateMessage(string table) =>
        $"The update was NOT written: sprk_corerecordid of a {table} is set when the record is created and cannot be changed "
        + $"({FixedAfterCreateCode}).";

    /// <summary>Every core-pair payload shape: one column, the other, both, mixed case, the bind form, and the pair with an ordinary column.</summary>
    public static readonly string[][] CorePairPayloads =
    [
        ["sprk_corerecordid"],
        ["sprk_corerecordtype"],
        ["sprk_corerecordid", "sprk_corerecordtype"],
        ["SPRK_CoreRecordId"],
        ["Sprk_CoreRecordType"],
        ["sprk_CoreRecordType@odata.bind"],
        ["_sprk_corerecordtype_value"],
        ["sprk_name", "sprk_corerecordid", "sprk_corerecordtype"],
    ];

    public static TheoryData<string, int> EveryTableAndPayload()
    {
        var data = new TheoryData<string, int>();
        foreach (var table in new[] { "sprk_signal", "sprk_decisionrecord" })
        {
            for (var i = 0; i < CorePairPayloads.Length; i++)
                data.Add(table, i);
        }

        return data;
    }

    public static TheoryData<int> EveryPayload()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < CorePairPayloads.Length; i++)
            data.Add(i);
        return data;
    }

    private static Dictionary<string, object?> Fields(int payload) =>
        CorePairPayloads[payload].ToDictionary(k => k, _ => (object?)Value);

    private static Dictionary<string, string?> LegacyFields(int payload) =>
        CorePairPayloads[payload].ToDictionary(k => k, _ => (string?)Value);

    // ── The Update Record node / action seam (UpdateRecordActionCore) ─────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryPayload))]
    public async Task UpdateRecordNode_TheCorePairOfADecisionRecord_IsRefused_AndNothingIsWritten(int payload)
    {
        var node = () => Core().UpdateAsync(
            new UpdateRecordActionInput("sprk_decisionrecord", Row, FieldMappings: null, LegacyFields(payload), Lookups: null),
            CancellationToken.None);

        (await node.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(FixedAfterCreateCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(EveryPayload))]
    public async Task UpdateRecordNode_TheCorePairOfASignal_IsRefused_AndNothingIsWritten(int payload)
    {
        // On a Signal the existing app-only access-column refusal answers first on this path (its order is pinned by
        // AccessTableAppOnlyWriterTests.AnAppOnlyWriteOfASignalsCoreRecordRegardingPairOrInvoiceSubject_IsRefused_ByTheOutputMappingAndTheUpdateRecordNode).
        var node = () => Core().UpdateAsync(
            new UpdateRecordActionInput("sprk_signal", Row, FieldMappings: null, LegacyFields(payload), Lookups: null),
            CancellationToken.None);

        (await node.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(EveryTableAndPayload))]
    public async Task ConfirmedProposal_TheCorePairOfASignalOrDecisionRecord_IsRefused_AndNothingIsWritten(string table, int payload)
    {
        var apply = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields(payload), Lookups: null,
                ImpersonateSystemUserId: Caller, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        (await apply.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(FixedAfterCreateCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_signal")]
    [InlineData("sprk_decisionrecord")]
    public async Task ConfirmedProposal_TheRefusal_SaysTheUpdateWasNotWritten_NotThatNoOwnerWasResolved(string table)
    {
        var apply = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields(0), Lookups: null,
                ImpersonateSystemUserId: Caller, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        var refused = (await apply.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
        refused.Message.Should().Be(FixedAfterCreateMessage(table)).And.NotContain("No owner could be resolved");
        (refused.RefusalCode, refused.EntityLogicalName).Should().Be((FixedAfterCreateCode, table));
        _writes.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_signal")]
    [InlineData("sprk_decisionrecord")]
    public async Task UpdateRecordNode_TheRefusal_IsTheSameForAnyRecordId(string table)
    {
        async Task<string> RefusalFor(Guid id)
        {
            var apply = () => Core().UpdateAsync(
                new UpdateRecordActionInput(
                    table, id, FieldMappings: null, LegacyFields(2), Lookups: null,
                    ImpersonateSystemUserId: Caller, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
                CancellationToken.None);
            var refused = (await apply.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
            return $"{refused.RefusalCode}|{refused.Message}";
        }

        (await RefusalFor(MissingRow)).Should().Be(await RefusalFor(Row));
        _writes.Invocations.Should().BeEmpty("nothing is read or written for either id");

        // Control: the double tells the two ids apart, so a refusal that depended on the row would differ.
        (await _writes.Object.RetrieveRecordFieldsAsync(table, Row, ["sprk_name"])).Should().NotBeEmpty();
        (await _writes.Object.RetrieveRecordFieldsAsync(table, MissingRow, ["sprk_name"])).Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_signal", null)]
    [InlineData("sprk_decisionrecord", null)]
    [InlineData("sprk_signal", "confirmed")]
    [InlineData("sprk_decisionrecord", "confirmed")]
    public async Task UpdateRecordNode_AnotherColumnOfASignalOrDecisionRecord_IsNotTheCorePairRefusal(string table, string? source)
    {
        // Task 048 (owner decision 2026-11-04, D-204 extended to updates): a Signal and a Decision Record are changed only by
        // their writers, so another column is refused whole by that rule (WriterOnlyTablesUpdateRefusalTests), never by this one.
        Guid? user = source is null ? null : Caller;
        var update = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "Renamed" },
                Lookups: null, ImpersonateSystemUserId: user,
                PayloadSource: source is null ? UpdateRecordPayloadSource.Configuration : UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        (await update.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.ChangedOnlyByItsWriterReasonCode);
        _writes.Invocations.Where(i => i.Method.Name != nameof(IFieldMappingDataverseService.RetrieveRecordFieldsAsync))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecordNode_AColumnNamedLikeTheCorePair_OnAnotherTable_IsWritten()
    {
        // No other table carries the pair (SpaarkeMaster Entity.xml); a table without it is not asked.
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_todo", Row, FieldMappings: null, LegacyFields: new Dictionary<string, string?> { ["sprk_corerecordid"] = Value },
                Lookups: null),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            "sprk_todo", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_corerecordid")),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── A playbook's output mapping (DataverseUpdateHandler) ──────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryPayload))]
    public async Task OutputMapping_TheCorePairOfADecisionRecord_IsRefused_AndNothingIsWritten(int payload)
    {
        var mapping = () => Handler().UpdateAsync(
            "sprk_decisionrecord", Row, Fields(payload), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await mapping.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(FixedAfterCreateCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(EveryPayload))]
    public async Task OutputMapping_TheCorePairOfASignal_IsRefused_AndNothingIsWritten(int payload)
    {
        // On a Signal the existing app-only access-column refusal answers first on this path (its order is pinned by
        // AccessTableAppOnlyWriterTests.AnAppOnlyWriteOfASignalsCoreRecordRegardingPairOrInvoiceSubject_IsRefused_ByTheOutputMappingAndTheUpdateRecordNode).
        var mapping = () => Handler().UpdateAsync(
            "sprk_signal", Row, Fields(payload), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await mapping.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task OutputMapping_TheRefusalOfADecisionRecord_SaysTheUpdateWasNotWritten_NotThatNoOwnerWasResolved()
    {
        var mapping = () => Handler().UpdateAsync(
            "sprk_decisionrecord", Row, Fields(0), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        var refused = (await mapping.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
        refused.Message.Should().Be(FixedAfterCreateMessage("sprk_decisionrecord")).And.NotContain("No owner could be resolved");
        (refused.RefusalCode, refused.EntityLogicalName).Should().Be((FixedAfterCreateCode, "sprk_decisionrecord"));
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task OutputMapping_TheRefusal_IsTheSameForAnyRecordId()
    {
        async Task<string> RefusalFor(Guid id)
        {
            var mapping = () => Handler().UpdateAsync(
                "sprk_decisionrecord", id, Fields(2), ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);
            var refused = (await mapping.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
            return $"{refused.RefusalCode}|{refused.Message}";
        }

        (await RefusalFor(MissingRow)).Should().Be(await RefusalFor(Row));
        _writes.Invocations.Should().BeEmpty("nothing is read or written for either id");

        // Control: the world tells the two ids apart, so a refusal that depended on the row would differ.
        var world = World();
        (await world.Service.RetrieveAsync("sprk_decisionrecord", Row, ["sprk_name"], CancellationToken.None)).Id.Should().Be(Row);
        await world.Service.Invoking(s => s.RetrieveAsync("sprk_decisionrecord", MissingRow, ["sprk_name"], CancellationToken.None))
            .Should().ThrowAsync<System.ServiceModel.FaultException<OrganizationServiceFault>>();
    }

    [Theory]
    [InlineData("sprk_signal", "sprk_name")]
    [InlineData("sprk_decisionrecord", "sprk_name")]
    public async Task OutputMapping_AnotherColumnOfASignalOrDecisionRecord_IsNotTheCorePairRefusal(string table, string column)
    {
        // Task 048 (owner decision 2026-11-04, D-204 extended to updates): refused whole by the writer-only rule
        // (WriterOnlyTablesUpdateRefusalTests), never by this one.
        var update = () => Handler().UpdateAsync(
            table, Row, new Dictionary<string, object?> { [column] = "Renamed" }, ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None);

        (await update.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.ChangedOnlyByItsWriterReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_todo", "sprk_corerecordid")]
    public async Task OutputMapping_TheSameNameOnAnotherTable_IsWritten(string table, string column)
    {
        await Handler().UpdateAsync(
            table, Row, new Dictionary<string, object?> { [column] = "Renamed" }, ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey(column)),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The field-mapping push's child writer (a Signal and a Decision Record are refused whole since task 048) ───────

    [Theory]
    [InlineData("sprk_signal", "sprk_corerecordid")]
    [InlineData("sprk_signal", "sprk_corerecordtype")]
    [InlineData("sprk_signal", "SPRK_CoreRecordId")]
    [InlineData("sprk_decisionrecord", "sprk_corerecordid")]
    [InlineData("sprk_decisionrecord", "sprk_corerecordtype")]
    [InlineData("sprk_decisionrecord", "Sprk_CoreRecordType")]
    public async Task Push_ARuleOntoTheCorePair_OfASignalOrDecisionRecord_IsRefusedWithTheWholeTable_AndNeverWritten(string table, string target)
    {
        // Task 048 fix round 1 (owner decision 2026-11-04, D-204 extended to updates): the child writer refuses a Signal and a
        // Decision Record whole, as the push's request check does, so the other rule is no longer written (#2568 had it written and
        // only the core-pair rule skipped). Every child fails, with the plain message; nothing is written.
        var (updated, failed, _, errors, _) = await Push(
            table, Rule("sprk_name", "sprk_description"), Rule("sprk_name", target));

        (updated, failed).Should().Be((0, 1));
        errors.Should().ContainSingle().Which.Should().Be(new PushFieldMappingsError
        {
            RecordId = Row,
            Error = Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.ChangedOnlyByItsWriterMessage(table),
        });
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Push_ARuleOntoAColumnNamedLikeTheCorePair_OnAnotherTable_IsWritten()
    {
        var (updated, failed, _, errors, _) = await Push("sprk_todo", Rule("sprk_name", "sprk_corerecordid"));

        (updated, failed).Should().Be((1, 0));
        errors.Should().BeEmpty();
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            "sprk_todo", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_corerecordid")),
            Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static FieldMappingRuleDto Rule(string source, string target) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = "Text", TargetFieldType = "Text" };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string table, params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object,
            World().Restamper,
            rules,
            new Dictionary<string, object?> { ["sprk_name"] = "Matter" },
            table,
            new[] { Row },
            Caller,
            NullLogger.Instance,
            CancellationToken.None);

    private UpdateRecordActionCore Core() => new(
        _writes.Object,
        new ServiceCollection().AddSingleton(EntitySetNameFixtures.Service()).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        NullLogger.Instance);

    /// <summary>A world holding <see cref="Row"/> on every table these tests write; <see cref="MissingRow"/> is not in it.</summary>
    private static StampWorld World() =>
        new StampWorld().Row("sprk_signal", Row).Row("sprk_decisionrecord", Row).Row("sprk_todo", Row);

    private DataverseUpdateHandler Handler()
    {
        var world = World();
        return new DataverseUpdateHandler(
            _writes.Object, world.Service, world.Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld());
    }
}
