using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22, Q5: a private message is never filed). A generic BFF update of an
/// existing <c>sprk_communication</c> that sets only a typed regarding that is no ownership parent (<c>sprk_regardingperson</c>) or only
/// the ADR-024 regarding pair is no filing ReparentAsync's own rule sees (the person takes the plain PATCH; the pair reaches the
/// re-file only through its display-column clears), so each writer asks <see cref="PrivateMessageWriteGuard.RefuseUpdateAsync"/>
/// before either path writes: the output mapping (<see cref="DataverseUpdateHandler"/>),
/// the Update Record core (<see cref="UpdateRecordActionCore"/>) and the field-mapping push
/// (<see cref="FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync"/>, per child). Per site: a private row is refused and nothing
/// is written; an ordinary Direct row is written as before; a subject-only change does no guard read; a guard read fault writes nothing.
/// </summary>
public class PrivateMessageGenericUpdateGuardTests
{
    private const string Communication = "sprk_communication";
    private static readonly Guid Row = Guid.Parse("05600000-0000-4000-8000-000000000001");
    private static readonly Guid Caller = Guid.Parse("05600000-0000-4000-8000-0000000000ca");
    private static readonly Guid Contact = Guid.Parse("05600000-0000-4000-8000-0000000000c1");
    private static readonly Guid Matter = Guid.Parse("05600000-0000-4000-8000-0000000000a1");
    private static readonly Guid RecordType = Guid.Parse("05600000-0000-4000-8000-0000000000b1");

    private readonly Mock<IFieldMappingDataverseService> _writes = new();
    private readonly Mock<IGenericEntityService> _entities = new();

    /// <summary>What the guard's read of the row answers: the row, or a fault.</summary>
    private Entity _row = new(Communication, Row);

    private Exception? _guardReadFault;
    private int _guardReads;

    public PrivateMessageGenericUpdateGuardTests()
    {
        _entities.Setup(e => e.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivateContextColumns.All.Concat(new[] { "sprk_communicationid", "sprk_subject", "sprk_regardingperson" })
                .ToDictionary(c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase));
        _entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, CancellationToken _) => EntitySetNameFixtures.SetOf(table));
        _entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new Entity(table, id));
        _entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (!IsTheGuardRead(query))
                    return Task.FromResult(new EntityCollection());
                _guardReads++;
                if (_guardReadFault is not null)
                    throw _guardReadFault;
                return Task.FromResult(new EntityCollection(new List<Entity> { _row }));
            });
    }

    private static bool IsTheGuardRead(QueryExpression query) =>
        query.EntityName == Communication
        && query.ColumnSet.Columns.Any(PrivateContextColumns.All.Contains)
        && query.Criteria.Conditions.Any(c => c.AttributeName == Communication + "id" && c.Values.Contains(Row));

    private void APrivateMessage() =>
        _row = new Entity(Communication, Row) { [PrivateContextColumns.Matter] = new EntityReference("sprk_matter", Matter) };

    private void AGuardReadFault() => _guardReadFault = new InvalidOperationException("Test: Dataverse unavailable.");

    private bool Written => _writes.Invocations.Any(i => i.Method.Name.StartsWith("Update", StringComparison.Ordinal));

    private static string PersonBind => $"/contacts({Contact:D})";

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Output mapping: DataverseUpdateHandler (app-only), refusal thrown as RecordOwnerUnresolvedException
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The re-file core, as the real one answers a change it does not see as a filing: it applies the change and records the
    /// request. A pair-only change reaches it when the pair planner clears the pair's display columns (candidate clears), and
    /// its own private-message rule sees no filing there, so the guard asks the rule first, on both paths.
    /// </summary>
    private readonly Mock<IRecordOwnershipResolver> _resolver = new();

    private readonly List<RecordReparent> _reparents = new();

    private void TheReparentWritesWhatItIsGiven() =>
        _resolver.Setup(r => r.ReparentAsync(It.IsAny<RecordReparent>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (RecordReparent request, Func<CancellationToken, Task> apply, CancellationToken ct) =>
            {
                _reparents.Add(request);
                await apply(ct);
                return RecordOwnerResolution.Unchanged("the change sets no parent and clears none it holds");
            });

    private DataverseUpdateHandler Handler() => new(
        _writes.Object, _entities.Object, new StampWorld().Restamper, _resolver.Object,
        SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
        SecureChildReconcilerFixtures.OverEmptyWorld());

    private Task HandlerUpdate(Dictionary<string, object?> fields) =>
        Handler().UpdateAsync(Communication, Row, fields, ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

    private static Dictionary<string, object?> PersonOnly() => new() { ["sprk_regardingperson@odata.bind"] = PersonBind };

    private static Dictionary<string, object?> PairOnly() => new()
    {
        [CoreAncestorResolver.RegardingRecordIdColumn] = Matter.ToString("D"),
        [CoreAncestorResolver.RegardingRecordTypeColumn + "@odata.bind"] = $"/sprk_recordtype_refs({RecordType:D})",
    };

    private static Dictionary<string, object?> PairIdOnly() => new() { [CoreAncestorResolver.RegardingRecordIdColumn] = Matter.ToString("D") };

    public static TheoryData<string> Changes() => new() { "person", "pair", "pair id" };

    private static Dictionary<string, object?> Change(string change) => change switch
    {
        "person" => PersonOnly(),
        "pair" => PairOnly(),
        _ => PairIdOnly(),
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task OutputMapping_APrivateMessage_IsRefusedByTheGuard_AndNothingIsWritten(string change)
    {
        APrivateMessage();
        TheReparentWritesWhatItIsGiven();

        var act = () => HandlerUpdate(Change(change));

        var refused = (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
        refused.RefusalCode.Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
        refused.Message.Should().Contain("was not updated").And.Contain("private message")
            .And.NotContain("a new", "an update of an existing row is never worded as a create");
        Written.Should().BeFalse();
        _guardReads.Should().Be(1);
        _reparents.Should().BeEmpty("refused before either path");
    }

    [Fact]
    public async Task OutputMapping_AnOrdinaryDirectMessage_WithTheSameChange_IsWritten()
    {
        await HandlerUpdate(PersonOnly());

        Written.Should().BeTrue();
        _guardReads.Should().Be(1);
    }

    [Fact]
    public async Task OutputMapping_ASubjectOnlyChange_DoesNoGuardRead()
    {
        APrivateMessage();

        await HandlerUpdate(new Dictionary<string, object?> { ["sprk_subject"] = "Re: hello" });

        Written.Should().BeTrue();
        _guardReads.Should().Be(0);
    }

    [Fact]
    public async Task OutputMapping_AGuardReadFault_WritesNothing()
    {
        AGuardReadFault();

        var act = () => HandlerUpdate(PersonOnly());

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(PrivateMessageWriteGuard.CheckUnavailable);
        Written.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Update Record core (the node and the AI action seam), refusal thrown as RecordOwnerUnresolvedException
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private UpdateRecordActionCore Core() => new(
        _writes.Object,
        new ServiceCollection().AddSingleton(_entities.Object).AddSingleton(_resolver.Object)
            .BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger.Instance);

    private static Dictionary<string, string?> Legacy(Dictionary<string, object?> fields) =>
        fields.ToDictionary(f => f.Key, f => (string?)f.Value);

    private Task CoreUpdate(Dictionary<string, object?> fields, UpdateRecordPayloadSource source = UpdateRecordPayloadSource.Configuration) =>
        Core().UpdateAsync(
            new UpdateRecordActionInput(
                Communication, Row, FieldMappings: null, LegacyFields: Legacy(fields), Lookups: null,
                ImpersonateSystemUserId: source == UpdateRecordPayloadSource.ConfirmedProposal ? Caller : null, PayloadSource: source),
            CancellationToken.None);

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task UpdateRecord_APrivateMessage_IsRefusedByTheGuard_AndNothingIsWritten(string change)
    {
        APrivateMessage();
        TheReparentWritesWhatItIsGiven();

        var act = () => CoreUpdate(Change(change));

        var refused = (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
        refused.RefusalCode.Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
        refused.Message.Should().Contain("was not updated").And.Contain("private message")
            .And.NotContain("a new", "an update of an existing row is never worded as a create");
        Written.Should().BeFalse();
        _guardReads.Should().Be(1);
        _reparents.Should().BeEmpty("refused before either path");
    }

    [Fact]
    public async Task UpdateRecord_AConfirmedProposalWrittenAsItsPerson_OnAPrivateMessage_IsRefusedToo()
    {
        APrivateMessage();

        var act = () => CoreUpdate(PersonOnly(), UpdateRecordPayloadSource.ConfirmedProposal);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
        Written.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateRecord_AnOrdinaryDirectMessage_WithTheSameChange_IsWritten()
    {
        await CoreUpdate(PersonOnly());

        Written.Should().BeTrue();
        _guardReads.Should().Be(1);
    }

    [Fact]
    public async Task UpdateRecord_ASubjectOnlyChange_DoesNoGuardRead()
    {
        APrivateMessage();

        await CoreUpdate(new Dictionary<string, object?> { ["sprk_subject"] = "Re: hello" });

        Written.Should().BeTrue();
        _guardReads.Should().Be(0);
    }

    [Fact]
    public async Task UpdateRecord_AGuardReadFault_WritesNothing()
    {
        AGuardReadFault();

        var act = () => CoreUpdate(PersonOnly());

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(PrivateMessageWriteGuard.CheckUnavailable);
        Written.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Field-mapping push: a refused child is not written, carries the code, and the push goes on
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly FieldMappingRuleDto SubjectRule = new()
    {
        SourceField = "sprk_name",
        TargetField = "sprk_subject",
        SourceFieldType = "Text",
        TargetFieldType = "Text",
    };

    private static readonly FieldMappingRuleDto PersonRule = new()
    {
        SourceField = "sprk_contact",
        TargetField = "sprk_regardingperson",
        SourceFieldType = "Lookup",
        TargetFieldType = "Lookup",
    };

    private static readonly FieldMappingRuleDto PairIdRule = new()
    {
        SourceField = "sprk_name",
        TargetField = CoreAncestorResolver.RegardingRecordIdColumn,
        SourceFieldType = "Text",
        TargetFieldType = "Text",
    };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?>
            {
                ["sprk_name"] = Matter.ToString("D"),
                ["sprk_contact"] = new SourceLookupValue(Contact, "contact", "A person"),
            },
            Communication,
            new[] { Row },
            Caller,
            NullLogger.Instance,
            CancellationToken.None,
            lookupBinds: new Dictionary<FieldMappingRuleDto, LookupBindPlan>(ReferenceEqualityComparer.Instance)
            {
                [PersonRule] = new("sprk_regardingperson@odata.bind", PersonBind, null),
            },
            appOnlyEntities: _entities.Object);

    [Fact]
    public async Task Push_APrivateMessage_IsRefused_ForThatChild_AndNothingIsWritten()
    {
        APrivateMessage();

        var (updated, failed, _, errors, _) = await Push(PersonRule);

        (updated, failed).Should().Be((0, 1));
        errors.Should().ContainSingle().Which.Should().Match<PushFieldMappingsError>(e =>
            e.RecordId == Row && e.ReasonCode == RecordOwnerRefusal.PrivateMessageNotFileable
            && e.Error.Contains(RecordOwnerRefusal.PrivateMessageNotFileable));
        errors[0].Error.Should().Contain("is a private message, which is never filed").And.NotContain("re-filed");
        Written.Should().BeFalse();
        _guardReads.Should().Be(1);
    }

    [Fact]
    public async Task Push_ARefusedChild_DoesNotStopThePush_TheNextChildIsWritten()
    {
        APrivateMessage();
        var ordinary = Guid.Parse("05600000-0000-4000-8000-000000000002");

        var (updated, failed, _, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object, new StampWorld().Restamper, [PersonRule],
            new Dictionary<string, object?> { ["sprk_contact"] = new SourceLookupValue(Contact, "contact", "A person") },
            Communication, new[] { Row, ordinary }, Caller, NullLogger.Instance, CancellationToken.None,
            lookupBinds: new Dictionary<FieldMappingRuleDto, LookupBindPlan>(ReferenceEqualityComparer.Instance)
            {
                [PersonRule] = new("sprk_regardingperson@odata.bind", PersonBind, null),
            },
            appOnlyEntities: _entities.Object);

        (updated, failed).Should().Be((1, 1));
        errors.Should().ContainSingle().Which.RecordId.Should().Be(Row);
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            Communication, ordinary, It.IsAny<Dictionary<string, object?>>(), Caller, It.IsAny<CancellationToken>()), Times.Once);
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            Communication, Row, It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Push_ARuleAimedAtTheRegardingPair_IsSkippedBeforeTheGuard_AndNothingIsWritten()
    {
        // A push never writes the pair: a rule aimed at it is skipped per rule (an existing rule of the push), so a pair-only
        // push sends nothing and the guard is never asked.
        APrivateMessage();

        var (updated, failed, _, _, results) = await Push(PairIdRule);

        (updated, failed).Should().Be((0, 0));
        results.Should().ContainSingle().Which.Status.Should().Be(FieldMappingStatus.Skipped);
        Written.Should().BeFalse();
        _guardReads.Should().Be(0);
    }

    [Fact]
    public async Task Push_AnOrdinaryDirectMessage_WithTheSameChange_IsWritten()
    {
        var (updated, failed, _, errors, _) = await Push(PersonRule);

        errors.Should().BeEmpty();
        (updated, failed).Should().Be((1, 0));
        Written.Should().BeTrue();
        _guardReads.Should().Be(1);
    }

    [Fact]
    public async Task Push_ASubjectOnlyChange_DoesNoGuardRead()
    {
        APrivateMessage();

        var (updated, failed, _, _, _) = await Push(SubjectRule);

        (updated, failed).Should().Be((1, 0));
        _guardReads.Should().Be(0);
    }

    [Fact]
    public async Task Push_AGuardReadFault_WritesNothing_ForThatChild()
    {
        AGuardReadFault();

        var (updated, failed, _, errors, _) = await Push(PersonRule);

        (updated, failed).Should().Be((0, 1));
        errors.Should().ContainSingle().Which.ReasonCode.Should().Be(PrivateMessageWriteGuard.CheckUnavailable);
        errors[0].Error.Should().Contain("could not be checked now").And.Contain(PrivateMessageWriteGuard.CheckUnavailable)
            .And.NotContain("re-filed");
        Written.Should().BeFalse();
    }
}
