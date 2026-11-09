using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 022 fix round 4 — the mapping-source rule on a push: values copied from a secure
/// source are written only into a row owned by that source's isolation owner. The predicate is
/// <c>MappingSourceCopyGate.DecideAsync</c>, asked per child before anything is written to it.
/// </summary>
/// <remarks>
/// The apply loop (<see cref="FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync"/>) is driven directly with a
/// <see cref="FieldMappingPushSourceGate"/> over a mocked app-only service. The route and the preview are covered in
/// <c>FieldMappingPushFamilyContractTests</c>.
/// </remarks>
public class FieldMappingPushSourceGateTests
{
    private static readonly Guid MatterId = Guid.Parse("02200000-0000-4000-8000-000000000001");
    private static readonly Guid ChildId = Guid.Parse("02200000-0000-4000-8000-0000000000c1");
    private static readonly Guid CallerId = Guid.Parse("02200000-0000-4000-8000-0000000000ca");
    private static readonly Guid SecureTeam = Guid.Parse("02200000-0000-4000-8000-00000000005e");
    private static readonly Guid OrdinaryTeam = Guid.Parse("02200000-0000-4000-8000-0000000000de");

    private const string Target = "sprk_event";

    // ───────────────────────── must fire ─────────────────────────

    [Fact]
    public async Task ASecureSource_AndAChildAnOrdinaryTeamOwns_SkipsTheChild_WithTheCode_AndWritesNothing()
    {
        var world = new World(source: SecureMatter(), childOwner: new EntityReference("team", OrdinaryTeam));

        var (updated, failed, _, errors, _) = await world.PushAsync();

        (updated, failed).Should().Be((0, 1));
        var error = errors.Should().ContainSingle().Which;
        error.RecordId.Should().Be(ChildId);
        error.ReasonCode.Should().Be(FieldMappingPushSourceGate.NotIsolatedReasonCode);
        error.Error.Should().Be(FieldMappingPushSourceGate.NotIsolatedMessage);
        error.Error.Should().StartWith("No fields were copied").And.Contain("source record is secure");
        world.VerifyNothingWritten();
    }

    [Fact]
    public async Task ASecureFlaggedSource_AndAChildAUserOwns_SkipsTheChild_WithTheCode()
    {
        var source = new Entity("sprk_matter", MatterId)
        {
            ["ownerid"] = new EntityReference("systemuser", Guid.NewGuid()), ["sprk_issecure"] = true,
        };
        var world = new World(source, childOwner: new EntityReference("systemuser", Guid.NewGuid()));

        var (updated, _, _, errors, _) = await world.PushAsync();

        updated.Should().Be(0);
        errors.Should().ContainSingle().Which.ReasonCode.Should().Be(FieldMappingPushSourceGate.NotIsolatedReasonCode);
        world.VerifyNothingWritten();
    }

    // ───────────────────────── must not fire ─────────────────────────

    [Fact]
    public async Task ASecureSource_AndAChildItsIsolationOwnerOwns_IsWritten()
    {
        var world = new World(source: SecureMatter(), childOwner: new EntityReference("team", SecureTeam));

        var (updated, failed, _, errors, _) = await world.PushAsync();

        (updated, failed).Should().Be((1, 0));
        errors.Should().BeEmpty();
        world.Writes.Should().ContainSingle().Which.Should().ContainKey("sprk_target").WhoseValue.Should().Be("from the source");
    }

    [Fact]
    public async Task AnOrdinarySource_IsUnchanged_AndNoChildOwnerIsRead()
    {
        var source = new Entity("sprk_matter", MatterId)
        {
            ["ownerid"] = new EntityReference("team", OrdinaryTeam), ["sprk_issecure"] = false,
        };
        var world = new World(source, childOwner: new EntityReference("team", OrdinaryTeam));

        var (updated, failed, _, errors, _) = await world.PushAsync();

        (updated, failed).Should().Be((1, 0));
        errors.Should().BeEmpty();
        world.Writes.Should().ContainSingle();
        world.Entities.Verify(e => e.RetrieveAsync(Target, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "a source that is not secure is allowed for every child");
    }

    [Fact]
    public async Task WithNothingToWrite_TheGateIsNotAsked()
    {
        var world = new World(source: SecureMatter(), childOwner: new EntityReference("team", OrdinaryTeam));

        var (_, failed, skipped, errors, _) = await world.PushAsync(sourceValues: new() { ["sprk_source"] = null });

        (failed, skipped).Should().Be((0, 1));
        errors.Should().BeEmpty();
        world.Entities.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ───────────────────────── fail closed ─────────────────────────

    public static TheoryData<string> Faults => new()
    {
        "source read faults",
        "source does not come back",
        "source owner missing",
        "child owner read faults",
        "child owner missing",
        "secure team faults",
        "secure team ambiguous",
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task AnAnswerThatCannotBeTold_SkipsTheChild_WithTheUndeterminedCode_AndWritesNothing(string fault)
    {
        var source = SecureMatter();
        EntityReference? childOwner = new("team", OrdinaryTeam);
        Func<CancellationToken, Task<(Guid? TeamId, string? Refusal)>> secureTeam =
            _ => Task.FromResult<(Guid? TeamId, string? Refusal)>((SecureTeam, null));
        var world = new World(source, childOwner);
        switch (fault)
        {
            case "source read faults":
                world.Entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("read failed"));
                break;
            case "source does not come back":
                world.Entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Entity)null!);
                break;
            case "source owner missing":
                source.Attributes.Remove("ownerid");
                break;
            case "child owner read faults":
                world.Entities.Setup(e => e.RetrieveAsync(Target, ChildId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("read failed"));
                break;
            case "child owner missing":
                world.Entities.Setup(e => e.RetrieveAsync(Target, ChildId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Entity(Target, ChildId));
                break;
            case "secure team faults":
                secureTeam = _ => throw new InvalidOperationException("team read failed");
                break;
            case "secure team ambiguous":
                secureTeam = _ => Task.FromResult<(Guid? TeamId, string? Refusal)>((null, "two teams"));
                break;
        }

        var (updated, failed, _, errors, _) = await world.PushAsync(secureTeam: secureTeam);

        (updated, failed).Should().Be((0, 1));
        var error = errors.Should().ContainSingle().Which;
        error.ReasonCode.Should().Be(FieldMappingPushSourceGate.UndeterminedReasonCode);
        error.Error.Should().Be(FieldMappingPushSourceGate.UndeterminedMessage);
        error.Error.Should().NotContain("read failed").And.NotContain(MatterId.ToString());
        world.VerifyNothingWritten();
    }

    [Fact]
    public async Task TheSourceIsReadOnce_ForEveryChildOfAPush()
    {
        var other = Guid.Parse("02200000-0000-4000-8000-0000000000c2");
        var world = new World(source: SecureMatter(), childOwner: new EntityReference("team", OrdinaryTeam));

        var (_, failed, _, errors, _) = await world.PushAsync(children: [ChildId, other]);

        failed.Should().Be(2);
        errors.Select(e => e.ReasonCode).Should().AllBe(FieldMappingPushSourceGate.NotIsolatedReasonCode);
        world.Entities.Verify(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ───────────────────────── a secure source never changes a child's filing (UAC-r2 (a)) ─────────────────────────

    private static readonly Guid OtherMatterId = Guid.Parse("02200000-0000-4000-8000-0000000000f2");
    private const string FilingColumn = "sprk_regardingmatter";
    private const string FilingBind = "sprk_RegardingMatter@odata.bind";

    [Fact]
    public void TheFilingColumnsOfAPush_AreTheRegistrysFilingLookups()
    {
        var payload = new Dictionary<string, object?>
        {
            [FilingBind] = $"/sprk_matters({OtherMatterId:D})",
            ["sprk_target"] = "a value",
        };

        var filing = FieldMappingPushSourceGate.FilingWrites(Target, payload);

        Sprk.Bff.Api.Services.RecordCreation.RecordCreationService.IsFilingLookup(Target, FilingColumn).Should().BeTrue();
        filing.Keys.Should().Equal(FilingColumn);
    }

    public static TheoryData<object?, Guid?, bool> FilingChanges => new()
    {
        { $"/sprk_matters({OtherMatterId:D})", MatterId, true },
        { $"/sprk_matters({MatterId:D})", MatterId, false },
        { MatterId, MatterId, false },
        { $"/sprk_matters({OtherMatterId:D})", null, true },
        { null, MatterId, true },
        { null, null, false },
        { "not a bind", MatterId, true },
        { "not a bind", null, true },
    };

    [Theory]
    [MemberData(nameof(FilingChanges))]
    public void APushedValue_ChangesTheFiling_UnlessItNamesWhatTheChildHolds(object? pushed, Guid? held, bool changes) =>
        FieldMappingPushSourceGate.ChangesFiling(pushed, held).Should().Be(changes);

    [Fact]
    public async Task ASecureSource_PushingADifferentFiling_IntoAChildItsIsolationOwnerOwns_IsRefusedWithTheRefileCode()
    {
        var world = new World(SecureMatter(), new EntityReference("team", SecureTeam), filedUnderMatter: MatterId);

        var refusal = await world.Gate().RefusalForChildAsync(Target, ChildId,
            new Dictionary<string, object?> { [FilingBind] = $"/sprk_matters({OtherMatterId:D})", ["sprk_target"] = "x" },
            CancellationToken.None);

        refusal.Should().Be(FieldMappingPushSourceGate.RefileNotAllowedReasonCode);
        FieldMappingPushSourceGate.MessageFor(refusal!).Should().Be(FieldMappingPushSourceGate.RefileNotAllowedMessage);
    }

    [Fact]
    public async Task ASecureSource_ClearingAFilingTheChildHolds_IsRefusedWithTheRefileCode()
    {
        var world = new World(SecureMatter(), new EntityReference("team", SecureTeam), filedUnderMatter: MatterId);

        var refusal = await world.Gate().RefusalForChildAsync(Target, ChildId,
            new Dictionary<string, object?> { [FilingBind] = null }, CancellationToken.None);

        refusal.Should().Be(FieldMappingPushSourceGate.RefileNotAllowedReasonCode);
    }

    [Fact]
    public async Task ASecureSource_PushingTheFilingTheChildHolds_GoesOnToTheOwnerQuestion()
    {
        var isolated = new World(SecureMatter(), new EntityReference("team", SecureTeam), filedUnderMatter: MatterId);
        var notIsolated = new World(SecureMatter(), new EntityReference("team", OrdinaryTeam), filedUnderMatter: MatterId);
        var payload = new Dictionary<string, object?> { [FilingBind] = $"/sprk_matters({MatterId:D})" };

        (await isolated.Gate().RefusalForChildAsync(Target, ChildId, payload, CancellationToken.None))
            .Should().BeNull("an equal value is no change, and the child is owned by the source's isolation owner");
        (await notIsolated.Gate().RefusalForChildAsync(Target, ChildId, payload, CancellationToken.None))
            .Should().Be(FieldMappingPushSourceGate.NotIsolatedReasonCode);
    }

    [Fact]
    public async Task AnOrdinarySource_PushingADifferentFiling_IsAllowed_AndReadsNoChild()
    {
        var source = new Entity("sprk_matter", MatterId)
        {
            ["ownerid"] = new EntityReference("team", OrdinaryTeam), ["sprk_issecure"] = false,
        };
        var world = new World(source, new EntityReference("team", OrdinaryTeam), filedUnderMatter: MatterId);

        var refusal = await world.Gate().RefusalForChildAsync(Target, ChildId,
            new Dictionary<string, object?> { [FilingBind] = $"/sprk_matters({OtherMatterId:D})" }, CancellationToken.None);

        refusal.Should().BeNull();
        world.Entities.Verify(e => e.RetrieveAsync(Target, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── the pair's agreement columns are filing too: the update planner moves the pair with a lookup it repeats ──

    private static readonly Guid AccountA = Guid.Parse("02200000-0000-4000-8000-0000000000a1");
    private static readonly Guid AccountB = Guid.Parse("02200000-0000-4000-8000-0000000000b2");
    private static readonly Guid AccountType = Guid.Parse("02200000-0000-4000-8000-0000000000a7");
    private const string AccountColumn = "sprk_regardingaccount";
    private const string AccountBind = "sprk_RegardingAccount@odata.bind";

    /// <summary>An event filed by its pair under account A, its party lookup naming the same account.</summary>
    private static IReadOnlyDictionary<string, object> PairOnAccountA() => new Dictionary<string, object>
    {
        [AccountColumn] = new EntityReference("account", AccountA),
        [Sprk.Bff.Api.Services.Access.SecureRootInheritance.PairIdColumn] = AccountA.ToString("D"),
        [Sprk.Bff.Api.Services.Access.SecureRootInheritance.PairTypeColumn] = new EntityReference("sprk_recordtype_ref", AccountType),
    };

    private static FieldMappingRuleDto AccountRule() => new()
    {
        Id = Guid.NewGuid(), SourceField = "sprk_client", SourceFieldType = "Lookup", TargetField = AccountColumn,
        TargetFieldType = "Lookup", Priority = 1, MappingType = "Copy",
    };

    [Fact]
    public void ThePairsAgreementColumns_AreFilingWrites()
    {
        Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite.PairAgreementColumns(Target).Should().ContainKey(AccountColumn);

        FieldMappingPushSourceGate.FilingWrites(Target, new Dictionary<string, object?> { [AccountBind] = $"/accounts({AccountB:D})" })
            .Keys.Should().Equal(AccountColumn);
    }

    [Fact]
    public async Task ASecureSource_PushingAnotherAccountIntoTheLookupThePairRepeats_SkipsTheChildWhole_WithTheRefileCode()
    {
        var world = new World(SecureMatter(), new EntityReference("team", SecureTeam), childColumns: PairOnAccountA());
        var rule = AccountRule();

        var (updated, failed, _, errors, _) = await world.PushAsync(
            sourceValues: new() { ["sprk_client"] = AccountB },
            rule: rule,
            lookupBinds: new Dictionary<FieldMappingRuleDto, LookupBindPlan>(ReferenceEqualityComparer.Instance)
            {
                [rule] = new(AccountBind, $"/accounts({AccountB:D})", null),
            });

        (updated, failed).Should().Be((0, 1));
        errors.Should().ContainSingle().Which.ReasonCode.Should().Be(FieldMappingPushSourceGate.RefileNotAllowedReasonCode);
        world.VerifyNothingWritten();
    }

    [Fact]
    public async Task ASecureSource_PushingTheAccountThePairRepeats_GoesOnToTheOwnerQuestion()
    {
        var world = new World(SecureMatter(), new EntityReference("team", SecureTeam), childColumns: PairOnAccountA());

        (await world.Gate().RefusalForChildAsync(Target, ChildId,
                new Dictionary<string, object?> { [AccountBind] = $"/accounts({AccountA:D})" }, CancellationToken.None))
            .Should().BeNull("an equal party value is no change, and the child is owned by the source's isolation owner");
    }

    [Fact]
    public async Task AnOrdinarySource_PushingAnotherAccountIntoTheLookupThePairRepeats_IsAllowed_AndReadsNoChild()
    {
        var source = new Entity("sprk_matter", MatterId)
        {
            ["ownerid"] = new EntityReference("team", OrdinaryTeam), ["sprk_issecure"] = false,
        };
        var world = new World(source, new EntityReference("team", OrdinaryTeam), childColumns: PairOnAccountA());

        (await world.Gate().RefusalForChildAsync(Target, ChildId,
                new Dictionary<string, object?> { [AccountBind] = $"/accounts({AccountB:D})" }, CancellationToken.None))
            .Should().BeNull();
        world.Entities.Verify(e => e.RetrieveAsync(Target, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ───────────────────────── fixtures ─────────────────────────

    /// <summary>A secure matter as Dataverse holds one: flagged, owned by the Secure Record owner team.</summary>
    private static Entity SecureMatter() => new("sprk_matter", MatterId)
    {
        ["ownerid"] = new EntityReference("team", SecureTeam), ["sprk_issecure"] = true,
    };

    private sealed class World
    {
        public Mock<IGenericEntityService> Entities { get; } = new();

        public Mock<IFieldMappingDataverseService> Writer { get; } = new();

        public List<Dictionary<string, object?>> Writes { get; } = [];

        public World(Entity source, EntityReference? childOwner, Guid? filedUnderMatter = null,
            IReadOnlyDictionary<string, object>? childColumns = null)
        {
            Entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(source);
            Entities.Setup(e => e.RetrieveAsync(Target, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
                {
                    var row = new Entity(table, id);
                    if (childOwner is not null)
                        row["ownerid"] = childOwner;
                    if (filedUnderMatter is { } matter)
                        row[FilingColumn] = new EntityReference("sprk_matter", matter);
                    foreach (var (column, value) in childColumns ?? new Dictionary<string, object>())
                        row[column] = value;
                    return row;
                });
            Writer.Setup(w => w.UpdateExistingRecordFieldsAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Callback((string _, Guid _, Dictionary<string, object?> payload, Guid? _, CancellationToken _) =>
                    Writes.Add(new Dictionary<string, object?>(payload)))
                .Returns(Task.CompletedTask);
        }

        public FieldMappingPushSourceGate Gate(Func<CancellationToken, Task<(Guid? TeamId, string? Refusal)>>? secureTeam = null) =>
            new(Entities.Object, "sprk_matter", MatterId,
                secureTeam ?? (_ => Task.FromResult<(Guid? TeamId, string? Refusal)>((SecureTeam, null))));

        public Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> PushAsync(
            Dictionary<string, object?>? sourceValues = null,
            Func<CancellationToken, Task<(Guid? TeamId, string? Refusal)>>? secureTeam = null,
            Guid[]? children = null,
            FieldMappingRuleDto? rule = null,
            IReadOnlyDictionary<FieldMappingRuleDto, LookupBindPlan>? lookupBinds = null)
        {
            return FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
                Gate(secureTeam),
                Writer.Object,
                new StampWorld().Restamper,
                [rule ?? Rule()],
                sourceValues ?? new Dictionary<string, object?> { ["sprk_source"] = "from the source" },
                Target,
                children ?? [ChildId],
                CallerId,
                NullLogger.Instance,
                CancellationToken.None,
                lookupBinds: lookupBinds,
                appOnlyEntities: Entities.Object);
        }

        public void VerifyNothingWritten()
        {
            Writes.Should().BeEmpty("nothing is written to a child the rule refuses");
            Writer.Verify(w => w.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    private static FieldMappingRuleDto Rule() => new()
    {
        Id = Guid.NewGuid(),
        SourceField = "sprk_source",
        SourceFieldType = "Text",
        TargetField = "sprk_target",
        TargetFieldType = "Text",
        Priority = 1,
        MappingType = "Copy",
    };
}
