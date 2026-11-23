using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 032b-1, T-M1 (design notes/task-032-design.md §5.3, §11): the app-only mapping write
/// gates MOVED out of <c>RecordCreationService.Repair.cs</c>, called directly. The cases are Repair's own expectations (the Repair
/// suites <c>RecordRepairColumnMetadataTests</c>, <c>RecordRepairColumnValueTests</c> and <c>RecordRepairModeTests</c> pass
/// unchanged through the wrappers); here each gate answers them without Repair around it, so 032b-2's invoice step gets the same
/// answers.
/// </summary>
public class MappingWriteGatesTests
{
    private const string Matter = "sprk_matter"; // a secure-flaggable root
    private const string Event = "sprk_event";   // not a root
    private static readonly Guid SecureTeam = Guid.Parse("03200000-0000-4000-8000-000000000001");
    private static readonly Guid OtherTeam = Guid.Parse("03200000-0000-4000-8000-000000000002");
    private static readonly Guid User = Guid.Parse("03200000-0000-4000-8000-000000000003");

    // ═══════════════════════════════ MappingSourceCopyGate ═══════════════════════════════

    [Fact]
    public void SourceGateColumns_AreTheOwner_AndARootsSecureFlag()
    {
        MappingSourceCopyGate.SourceGateColumns(Matter).Should().Equal("ownerid", AccessFollowsParent.SecureFlagColumn);
        MappingSourceCopyGate.SourceGateColumns(Event).Should().Equal("ownerid");
    }

    public static TheoryData<string, bool?, EntityReference, EntityReference?, string> CopyCases() => new()
    {
        // must-not-fire: an ordinary source
        { Event, null, new("systemuser", User), null, nameof(MappingSourceCopyVerdict.Allowed) },
        { Matter, false, new("systemuser", User), new("team", OtherTeam), nameof(MappingSourceCopyVerdict.Allowed) },
        { Matter, false, new("team", OtherTeam), new("systemuser", User), nameof(MappingSourceCopyVerdict.Allowed) },
        // a flagged root (an EMPTY flag reads as flagged): secure unless the row is owned by the Secure team
        { Matter, true, new("systemuser", User), new("systemuser", User), nameof(MappingSourceCopyVerdict.Secure) },
        { Matter, null, new("systemuser", User), null, nameof(MappingSourceCopyVerdict.Secure) },
        { Matter, true, new("systemuser", User), new("team", OtherTeam), nameof(MappingSourceCopyVerdict.Secure) },
        { Matter, true, new("systemuser", User), new("team", SecureTeam), nameof(MappingSourceCopyVerdict.Allowed) },
        // a source owned by the Secure team: secure unless the row is owned by that same team
        { Event, null, new("team", SecureTeam), new("team", OtherTeam), nameof(MappingSourceCopyVerdict.Secure) },
        { Event, null, new("team", SecureTeam), null, nameof(MappingSourceCopyVerdict.Secure) },
        { Event, null, new("team", SecureTeam), new("team", SecureTeam), nameof(MappingSourceCopyVerdict.Allowed) },
    };

    [Theory]
    [MemberData(nameof(CopyCases))]
    public async Task DecideAsync_AnswersAsRepairDid(
        string table, bool? flag, EntityReference sourceOwner, EntityReference? rowOwner, string expected)
    {
        var decision = await MappingSourceCopyGate.DecideAsync(table, Source(table, flag, sourceOwner), rowOwner, Team(SecureTeam), CancellationToken.None);

        decision.Verdict.ToString().Should().Be(expected);
    }

    [Fact]
    public async Task DecideAsync_AnOrdinaryUserOwnedSource_NeverAsksForTheSecureTeam()
    {
        var asked = 0;
        Task<(Guid?, string?)> Count(CancellationToken _) { asked++; return Task.FromResult<(Guid?, string?)>((SecureTeam, null)); }

        await MappingSourceCopyGate.DecideAsync(Event, Source(Event, null, new("systemuser", User)), new("team", OtherTeam), Count, CancellationToken.None);

        asked.Should().Be(0);
    }

    [Fact]
    public async Task DecideAsync_AnOwnerThatDidNotComeBack_IsUnknown()
    {
        var read = new Entity(Event, Guid.NewGuid());

        var decision = await MappingSourceCopyGate.DecideAsync(Event, read, null, Team(SecureTeam), CancellationToken.None);

        decision.Should().Be(MappingSourceCopyDecision.Unknown(MappingSourceCopyGate.ReasonSourceOwnerMissing));
    }

    [Theory]
    [InlineData(SecureChildShareSynchronizer.SecureOwnerTeamNotFoundRefusal, MappingSourceCopyGate.ReasonSecureTeamMissing)]
    [InlineData("secure_owner_team_ambiguous", MappingSourceCopyGate.ReasonSecureTeamAmbiguous)]
    public async Task DecideAsync_ASecureTeamThatCannotBeTold_IsUnknown(string? refusal, string reason)
    {
        Task<(Guid?, string?)> Missing(CancellationToken _) => Task.FromResult<(Guid?, string?)>((null, refusal));

        var decision = await MappingSourceCopyGate.DecideAsync(
            Event, Source(Event, null, new("team", OtherTeam)), null, Missing, CancellationToken.None);

        decision.Verdict.Should().Be(MappingSourceCopyVerdict.Unknown);
        decision.Reason.Should().Be(reason);
    }

    // ── Task 022 fix round 6: no Secure Record setup is not "cannot be told" ──

    public static TheoryData<string, bool?, EntityReference, EntityReference?, string> NotSetUpCases() => new()
    {
        // must not fire: an unflagged team-owned source, whatever owns the row
        { Event, null, new("team", OtherTeam), null, nameof(MappingSourceCopyVerdict.Allowed) },
        { Event, null, new("team", OtherTeam), new("team", OtherTeam), nameof(MappingSourceCopyVerdict.Allowed) },
        { Matter, false, new("team", OtherTeam), new("team", OtherTeam), nameof(MappingSourceCopyVerdict.Allowed) },
        // must fire: a flagged source is still secure, and no row is isolated with it
        { Matter, true, new("team", OtherTeam), new("team", OtherTeam), nameof(MappingSourceCopyVerdict.Secure) },
        { Matter, true, new("team", SecureTeam), new("team", SecureTeam), nameof(MappingSourceCopyVerdict.Secure) },
        { Matter, null, new("systemuser", User), new("team", OtherTeam), nameof(MappingSourceCopyVerdict.Secure) },
        { Matter, true, new("team", OtherTeam), null, nameof(MappingSourceCopyVerdict.Secure) },
    };

    [Theory]
    [MemberData(nameof(NotSetUpCases))]
    public async Task DecideAsync_WithNoSecureRecordSetUp_TheFlagAloneDecides(
        string table, bool? flag, EntityReference sourceOwner, EntityReference? rowOwner, string expected)
    {
        Task<(Guid?, string?)> NotSetUp(CancellationToken _) => Task.FromResult<(Guid?, string?)>((null, null));

        var decision = await MappingSourceCopyGate.DecideAsync(table, Source(table, flag, sourceOwner), rowOwner, NotSetUp, CancellationToken.None);

        decision.Verdict.ToString().Should().Be(expected);
    }

    [Fact]
    public async Task ResolveForCopyGate_TellsNotSetUp_FromSetUpButNotFound_AndAmbiguous()
    {
        var configuration = SecureChildShareWorld.Configuration();
        Task<(Guid? TeamId, string? Refusal)> Resolve(SecureChildShareWorld world) =>
            SecureChildShareSynchronizer.ResolveSecureOwnerTeamForCopyGateAsync(
                SecureChildShareWorld.EntitiesOver(() => world).Object, configuration, CancellationToken.None);

        (await Resolve(SecureChildShareWorld.Standard())).Should().Be(((Guid?)SecureChildShareWorld.SecureTeam, (string?)null));
        (await Resolve(SecureChildShareWorld.WithoutSecureBusinessUnit())).Should().Be(((Guid?)null, (string?)null),
            "no Secure Record business unit: not set up");
        (await Resolve(SecureChildShareWorld.Standard().Remove("team", SecureChildShareWorld.SecureTeam)))
            .Should().Be(((Guid?)null, (string?)SecureChildShareSynchronizer.SecureOwnerTeamNotFoundRefusal), "set up, the team not found");
        (await Resolve(SecureChildShareWorld.Standard().Team(Guid.NewGuid(), SecureChildShareWorld.SecureBu,
                SecureChildShareWorld.SecureOwnerTeamName, isDefault: false, teamType: 0)))
            .Refusal.Should().NotBeNull("two named teams: ambiguous");
        (await Resolve(SecureChildShareWorld.Standard().Add("businessunit", Guid.NewGuid(), ("name", SecureChildShareWorld.SecureBuName))))
            .Refusal.Should().NotBeNull("two Secure Record business units: ambiguous");
    }

    [Fact]
    public async Task ResolveForCopyGate_AFault_Propagates()
    {
        var world = SecureChildShareWorld.Standard().FailingQueriesOf("team");

        var act = () => SecureChildShareSynchronizer.ResolveSecureOwnerTeamForCopyGateAsync(
            SecureChildShareWorld.EntitiesOver(() => world).Object, SecureChildShareWorld.Configuration(), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DecideAsync_OverTheResolution_SetUpButNotFound_OrFaulting_IsUnknown_NotSetUp_IsDecidedByTheFlag()
    {
        var configuration = SecureChildShareWorld.Configuration();
        Func<CancellationToken, Task<(Guid? TeamId, string? Refusal)>> Over(SecureChildShareWorld world) =>
            ct => SecureChildShareSynchronizer.ResolveSecureOwnerTeamForCopyGateAsync(
                SecureChildShareWorld.EntitiesOver(() => world).Object, configuration, ct);
        var teamOwned = Source(Event, null, new("team", OtherTeam));

        (await MappingSourceCopyGate.DecideAsync(Event, teamOwned, null, Over(SecureChildShareWorld.WithoutSecureBusinessUnit()), CancellationToken.None))
            .Verdict.Should().Be(MappingSourceCopyVerdict.Allowed);
        (await MappingSourceCopyGate.DecideAsync(Event, teamOwned, null,
                Over(SecureChildShareWorld.Standard().Remove("team", SecureChildShareWorld.SecureTeam)), CancellationToken.None))
            .Should().Be(MappingSourceCopyDecision.Unknown(MappingSourceCopyGate.ReasonSecureTeamMissing));
        (await MappingSourceCopyGate.DecideAsync(Event, teamOwned, null,
                Over(SecureChildShareWorld.Standard().FailingQueriesOf("team")), CancellationToken.None))
            .Reason.Should().Be(MappingSourceCopyGate.ReasonFault);
        (await MappingSourceCopyGate.DecideAsync(Matter, Source(Matter, true, new("team", OtherTeam)), new("team", OtherTeam),
                Over(SecureChildShareWorld.WithoutSecureBusinessUnit()), CancellationToken.None))
            .Verdict.Should().Be(MappingSourceCopyVerdict.Secure);
    }

    [Fact]
    public async Task DecideAsync_AFaultDecidingTheTeam_IsUnknown_WithTheFault()
    {
        var fault = new InvalidOperationException("Dataverse timed out");
        Task<(Guid?, string?)> Throws(CancellationToken _) => throw fault;

        var decision = await MappingSourceCopyGate.DecideAsync(
            Event, Source(Event, null, new("team", OtherTeam)), null, Throws, CancellationToken.None);

        decision.Verdict.Should().Be(MappingSourceCopyVerdict.Unknown);
        decision.Reason.Should().Be(MappingSourceCopyGate.ReasonFault);
        decision.Fault.Should().BeSameAs(fault);
    }

    [Fact]
    public async Task DecideAsync_Cancellation_IsRethrown()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Task<(Guid?, string?)> Cancelled(CancellationToken ct) => Task.FromCanceled<(Guid?, string?)>(ct);

        var act = () => MappingSourceCopyGate.DecideAsync(Event, Source(Event, null, new("team", OtherTeam)), null, Cancelled, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═══════════════════════════════ MappedTargetFit ═══════════════════════════════

    private const string Column = "sprk_x";

    private static IReadOnlyDictionary<string, ColumnMetadata> Columns(ColumnMetadata metadata) =>
        new Dictionary<string, ColumnMetadata>(StringComparer.OrdinalIgnoreCase) { [metadata.LogicalName] = metadata };

    private static ColumnMetadata Typed(string type) => new(Column, type, IsValidForUpdate: true, IsSecured: false, MaxLength: null,
        MinValue: -100_000m, MaxValue: 100_000m);

    public static TheoryData<string, object, object> Converted() => new()
    {
        { "Decimal", 1500, 1500m },
        { "BigInt", 1500, 1500L },
        { "Double", 1500m, 1500d },
        { "Integer", 1500m, 1500 },
        { "Integer", 1500, 1500 }, // must-not-fire: already the column's type
    };

    [Theory]
    [MemberData(nameof(Converted))]
    public void Check_ANumber_IsWrittenAsItsColumnsType(string type, object value, object expected)
    {
        var (refusal, written) = MappedTargetFit.Check(Columns(Typed(type)), Column, value);

        refusal.Should().BeNull();
        written.Should().BeOfType(expected.GetType()).And.Be(expected);
    }

    [Fact]
    public void Check_AMoneyColumn_TakesAMoney()
    {
        MappedTargetFit.Check(Columns(Typed("Money")), Column, 2500.50m).Value
            .Should().BeOfType<Money>().Which.Value.Should().Be(2500.50m);
    }

    [Theory]
    [InlineData("Money")]
    [InlineData("Decimal")]
    public void Check_ADoubleNoDecimalCanHold_IsATypeMismatch(string type)
    {
        MappedTargetFit.Check(Columns(Typed(type)), Column, (double)decimal.MaxValue).Refusal
            .Should().Be(RecordRepairReason.MappedValueTypeMismatch);
    }

    [Theory]
    [InlineData(100_001, RecordRepairReason.MappedValueOutOfRange)]
    [InlineData(-100_001, RecordRepairReason.MappedValueOutOfRange)]
    [InlineData(100_000, null)] // must-not-fire: at the bound
    public void Check_ANumberOutsideItsRange_IsRefused(int value, string? reason)
    {
        MappedTargetFit.Check(Columns(Typed("Integer")), Column, value).Refusal.Should().Be(reason);
    }

    [Fact]
    public void Check_ARangeNotStated_FailsClosed()
    {
        MappedTargetFit.Check(Columns(Typed("Decimal") with { MaxValue = null }), Column, 1m).Refusal
            .Should().Be(RecordRepairReason.MappedValueOutOfRange);
    }

    [Fact]
    public void Check_AnUnknownOrNotUpdatableColumn_IsNotUpdatable()
    {
        MappedTargetFit.Check(Columns(Typed("String")), "sprk_other", "x").Refusal.Should().Be(RecordRepairReason.MappedColumnNotUpdatable);
        MappedTargetFit.Check(Columns(Typed("String") with { IsValidForUpdate = null }), Column, "x").Refusal
            .Should().Be(RecordRepairReason.MappedColumnNotUpdatable, "unknown counts as not");
    }

    [Fact]
    public void Check_AValueOfTheWrongKind_OrNoStatedType_IsATypeMismatch()
    {
        MappedTargetFit.Check(Columns(Typed("Boolean")), Column, "yes").Refusal.Should().Be(RecordRepairReason.MappedValueTypeMismatch);
        MappedTargetFit.Check(Columns(Typed("String") with { AttributeType = null }), Column, "x").Refusal
            .Should().Be(RecordRepairReason.MappedValueTypeMismatch);
    }

    [Theory]
    [InlineData(5, null)]
    [InlineData(4, RecordRepairReason.MappedValueTooLong)]
    public void Check_TextLongerThanItsColumn_IsRefused(int maxLength, string? reason)
    {
        MappedTargetFit.Check(Columns(Typed("Memo") with { MaxLength = maxLength }), Column, "12345").Refusal.Should().Be(reason);
    }

    [Theory]
    [InlineData("sprk_organization", null)]
    [InlineData("account", RecordRepairReason.MappedLookupTableNotAccepted)]
    public void Check_ALookup_IsWrittenOnlyToATableItsColumnTakes(string table, string? reason)
    {
        var lookup = Typed("Lookup") with { Targets = ["sprk_organization"] };

        MappedTargetFit.Check(Columns(lookup), Column, new EntityReference(table, Guid.NewGuid())).Refusal.Should().Be(reason);
        MappedTargetFit.Check(Columns(lookup with { Targets = null }), Column, new EntityReference("sprk_organization", Guid.NewGuid()))
            .Refusal.Should().Be(RecordRepairReason.MappedLookupTableNotAccepted, "no targets stated: fail closed");
    }

    [Fact]
    public void Check_AnOptionTheColumnDoesNotHave_IsRefused()
    {
        var choice = Typed("Picklist") with { OptionValues = [100000000, 100000001] };

        MappedTargetFit.Check(Columns(choice), Column, new OptionSetValue(100000001)).Refusal.Should().BeNull();
        MappedTargetFit.Check(Columns(choice), Column, new OptionSetValue(7)).Refusal.Should().Be(RecordRepairReason.MappedOptionNotAccepted);
        MappedTargetFit.Check(Columns(choice with { OptionValues = null }), Column, new OptionSetValue(100000001)).Refusal
            .Should().Be(RecordRepairReason.MappedOptionNotAccepted, "no choices stated: fail closed");
    }

    [Fact]
    public void Check_AMultiSelect_NeedsEveryValue_AndAtLeastOne()
    {
        var multi = Typed("Virtual") with { OptionValues = [1, 2, 3] };

        MappedTargetFit.Check(Columns(multi), Column, new OptionSetValueCollection([new(1), new(2)])).Refusal.Should().BeNull();
        MappedTargetFit.Check(Columns(multi with { OptionValues = [1] }), Column, new OptionSetValueCollection([new(1), new(2)]))
            .Refusal.Should().Be(RecordRepairReason.MappedOptionNotAccepted);
        MappedTargetFit.Check(Columns(multi), Column, new OptionSetValueCollection()).Refusal
            .Should().Be(RecordRepairReason.MappedOptionNotAccepted);
    }

    // ═══════════════════════════════ MappingColumnMetadata ═══════════════════════════════

    [Fact]
    public async Task ReadAsync_ReturnsTheColumns_ReadOncePerCall()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetColumnMetadataAsync(Matter, It.IsAny<CancellationToken>())).ReturnsAsync(Columns(Typed("String")));

        (await MappingColumnMetadata.ReadAsync(entities.Object, Matter, CancellationToken.None)).Columns.Should().ContainKey(Column);
        (await MappingColumnMetadata.ReadAsync(entities.Object, Matter, CancellationToken.None)).Columns.Should().ContainKey(Column);

        entities.Verify(e => e.GetColumnMetadataAsync(Matter, It.IsAny<CancellationToken>()), Times.Exactly(2), "never cached (Q-U4 c)");
    }

    [Fact]
    public async Task ReadAsync_AnEmptyAnswerOrAFault_GivesNoColumns()
    {
        var empty = new Mock<IGenericEntityService>();
        empty.Setup(e => e.GetColumnMetadataAsync(Matter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ColumnMetadata>());
        var fault = new InvalidOperationException("metadata unavailable");
        var faulted = new Mock<IGenericEntityService>();
        faulted.Setup(e => e.GetColumnMetadataAsync(Matter, It.IsAny<CancellationToken>())).ThrowsAsync(fault);

        (await MappingColumnMetadata.ReadAsync(empty.Object, Matter, CancellationToken.None)).Should().Be(new MappingColumnMetadataRead(null, null));
        (await MappingColumnMetadata.ReadAsync(faulted.Object, Matter, CancellationToken.None)).Should().Be(new MappingColumnMetadataRead(null, fault));
    }

    [Fact]
    public async Task ReadAsync_Cancellation_IsRethrown()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetColumnMetadataAsync(Matter, It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => MappingColumnMetadata.ReadAsync(entities.Object, Matter, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void CopyableSourceColumns_LeavesOutSecuredAndAbsentColumns()
    {
        var columns = new Dictionary<string, ColumnMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_open"] = new("sprk_open", "String", true, IsSecured: false, 100),
            ["sprk_secret"] = new("sprk_secret", "String", true, IsSecured: true, 100),
        };

        var answer = MappingColumnMetadata.CopyableSourceColumns(columns, ["sprk_open", "sprk_secret", "sprk_absent"]);

        answer.Copyable.Should().Equal("sprk_open");
        answer.Secured.Should().Equal("sprk_secret");
        answer.UnknownColumn.Should().BeNull();
    }

    [Fact]
    public void CopyableSourceColumns_AColumnWhoseSecurityIsNotStated_MapsNothing()
    {
        var columns = new Dictionary<string, ColumnMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_open"] = new("sprk_open", "String", true, IsSecured: false, 100),
            ["sprk_unsaid"] = new("sprk_unsaid", "String", true, IsSecured: null, 100),
        };

        var answer = MappingColumnMetadata.CopyableSourceColumns(columns, ["sprk_open", "sprk_unsaid"]);

        answer.Copyable.Should().BeNull("metadata that does not say maps nothing (UAC-r2 condition 1)");
        answer.UnknownColumn.Should().Be("sprk_unsaid");
    }

    // ── helpers ──

    private static Entity Source(string table, bool? flag, EntityReference owner)
    {
        var read = new Entity(table, Guid.NewGuid()) { ["ownerid"] = owner };
        if (flag is { } value)
            read[AccessFollowsParent.SecureFlagColumn] = value;
        return read;
    }

    private static Func<CancellationToken, Task<(Guid? TeamId, string? Refusal)>> Team(Guid team) =>
        _ => Task.FromResult<(Guid?, string?)>((team, null));
}
