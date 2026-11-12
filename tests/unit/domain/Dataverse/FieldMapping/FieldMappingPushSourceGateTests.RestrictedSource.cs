using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 022c — the Restricted-source rule on a push: values mapped from a source whose
/// <c>sprk_accesspermission</c> is Restricted, or cannot be read, are written only into a child that is Restricted (its stored
/// value, <see cref="RecordCreationService.StoredRowIsRestricted"/>). Such a source never changes a child's filing.
/// </summary>
public partial class FieldMappingPushSourceGateTests
{
    private const string AccessColumn = InheritedAccessPermission.Column;

    /// <summary>A matter that is not secure (flag false, owned by an ordinary team), with the Access Permission given.</summary>
    private static Entity OrdinaryMatter(object? accessPermission)
    {
        var matter = TeamOwnedUnflaggedMatter();
        if (accessPermission is not null)
            matter[AccessColumn] = accessPermission;
        return matter;
    }

    private static OptionSetValue Level(int value) => new(value);

    private static Dictionary<string, object> ChildAccess(object value) => new() { [AccessColumn] = value };

    private static readonly Dictionary<string, object?> TextOnly = new() { ["sprk_target"] = "x" };

    // ───────────────────────── must fire (AC3) ─────────────────────────

    public static TheoryData<string, object?> ChildrenThatAreNotRestricted => new()
    {
        { "Standard", Level(InheritedAccessPermission.Standard) },
        { "Limited", Level(InheritedAccessPermission.Limited) },
        { "no value", null },
        { "an unknown option", Level(999) },
        { "a value of another type", InheritedAccessPermission.Restricted },
    };

    [Theory]
    [MemberData(nameof(ChildrenThatAreNotRestricted))]
    public async Task ARestrictedSource_AndAChildThatIsNotRestricted_SkipsTheChild_WithTheRestrictedCode_AndWritesNothing(
        string because, object? childValue)
    {
        var world = new World(OrdinaryMatter(Level(InheritedAccessPermission.Restricted)),
            childOwner: new EntityReference("team", OrdinaryTeam),
            childColumns: childValue is null ? null : ChildAccess(childValue));

        var (updated, failed, _, errors, _) = await world.PushAsync();

        (updated, failed).Should().Be((0, 1), because);
        var error = errors.Should().ContainSingle().Which;
        error.RecordId.Should().Be(ChildId);
        error.ReasonCode.Should().Be(FieldMappingPushSourceGate.RestrictedSourceNotRestrictedReasonCode);
        error.Error.Should().Be(FieldMappingPushSourceGate.RestrictedSourceNotRestrictedMessage);
        error.Error.Should().NotContain(MatterId.ToString()).And.NotContain(ChildId.ToString());
        world.VerifyNothingWritten();
    }

    [Fact]
    public void TheRestrictedCodesText_NamesNoRecord_AndIsWhatMessageForGives()
    {
        FieldMappingPushSourceGate.MessageFor(FieldMappingPushSourceGate.RestrictedSourceNotRestrictedReasonCode)
            .Should().Be(FieldMappingPushSourceGate.RestrictedSourceNotRestrictedMessage)
            .And.StartWith("No fields were copied to this record");
        FieldMappingPushSourceGate.RestrictedSourceNotRestrictedReasonCode
            .Should().Be("field_mapping.push.restricted_source_not_restricted");
    }

    [Fact]
    public async Task ARestrictedSource_AndAChildWhoseTableHasNoColumn_IsRefused_WithoutReadingTheChild()
    {
        var world = new World(OrdinaryMatter(Level(InheritedAccessPermission.Restricted)), new EntityReference("team", OrdinaryTeam));
        InheritedAccessPermission.CarriesColumn("sprk_memo").Should().BeFalse();

        var refusal = await world.Gate().RefusalForChildAsync("sprk_memo", ChildId, TextOnly, CancellationToken.None);

        refusal.Should().Be(FieldMappingPushSourceGate.RestrictedSourceNotRestrictedReasonCode);
        world.Entities.Verify(e => e.RetrieveAsync("sprk_memo", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "nothing the answer needs is on the child");
    }

    public static TheoryData<string, object> UnreadableSourceValues => new()
    {
        { "an unknown option", Level(999) },
        { "a value of another type", "Restricted" },
    };

    [Theory]
    [MemberData(nameof(UnreadableSourceValues))]
    public async Task AnUnreadableSource_AndAChildThatIsNotRestricted_SkipsTheChild_WithTheUndeterminedCode(string because, object value)
    {
        var world = new World(OrdinaryMatter(value), new EntityReference("team", OrdinaryTeam),
            childColumns: ChildAccess(Level(InheritedAccessPermission.Standard)));

        var (updated, failed, _, errors, _) = await world.PushAsync();

        (updated, failed).Should().Be((0, 1), because);
        errors.Should().ContainSingle().Which.ReasonCode.Should().Be(FieldMappingPushSourceGate.UndeterminedReasonCode);
        world.VerifyNothingWritten();
    }

    [Theory]
    [InlineData("Restricted", true)]
    [InlineData("unknown option", true)]
    [InlineData("Restricted", false)]
    [InlineData("unknown option", false)]
    public async Task ARestrictedOrUnreadableSource_ChangingAChildsFiling_IsRefusedWithTheRefileCode_EvenIntoARestrictedChild(
        string source, bool clear)
    {
        var columns = ChildAccess(Level(InheritedAccessPermission.Restricted));
        columns[FilingColumn] = new EntityReference("sprk_matter", MatterId);
        var world = new World(OrdinaryMatter(Level(source == "Restricted" ? InheritedAccessPermission.Restricted : 999)),
            new EntityReference("team", OrdinaryTeam), childColumns: columns);

        var refusal = await world.Gate().RefusalForChildAsync(Target, ChildId,
            new Dictionary<string, object?> { [FilingBind] = clear ? null : $"/sprk_matters({OtherMatterId:D})" }, CancellationToken.None);

        refusal.Should().Be(FieldMappingPushSourceGate.RefileNotAllowedReasonCode);
        FieldMappingPushSourceGate.MessageFor(refusal!).Should().Be(FieldMappingPushSourceGate.RefileNotAllowedMessage);
    }

    [Fact]
    public async Task ARestrictedSecureSource_AndAnIsolatedChildThatIsNotRestricted_GetsTheRestrictedCode()
    {
        var source = SecureMatter();
        source[AccessColumn] = Level(InheritedAccessPermission.Restricted);
        var world = new World(source, new EntityReference("team", SecureTeam),
            childColumns: ChildAccess(Level(InheritedAccessPermission.Standard)));

        (await world.Gate().RefusalForChildAsync(Target, ChildId, TextOnly, CancellationToken.None))
            .Should().Be(FieldMappingPushSourceGate.RestrictedSourceNotRestrictedReasonCode);
    }

    [Fact]
    public async Task ARestrictedSecureSource_AndAChildNotIsolated_KeepsTheSecureGatesCode()
    {
        var source = SecureMatter();
        source[AccessColumn] = Level(InheritedAccessPermission.Restricted);
        var world = new World(source, new EntityReference("team", OrdinaryTeam),
            childColumns: ChildAccess(Level(InheritedAccessPermission.Standard)));

        (await world.Gate().RefusalForChildAsync(Target, ChildId, TextOnly, CancellationToken.None))
            .Should().Be(FieldMappingPushSourceGate.NotIsolatedReasonCode);
    }

    // ───────────────────────── must not fire (AC4) ─────────────────────────

    [Theory]
    [InlineData("Restricted")]
    [InlineData("unknown option")]
    public async Task ARestrictedOrUnreadableSource_AndARestrictedChild_WithNoFilingChange_IsWritten(string source)
    {
        var world = new World(OrdinaryMatter(Level(source == "Restricted" ? InheritedAccessPermission.Restricted : 999)),
            new EntityReference("team", OrdinaryTeam), childColumns: ChildAccess(Level(InheritedAccessPermission.Restricted)));

        var (updated, failed, _, errors, _) = await world.PushAsync();

        (updated, failed).Should().Be((1, 0));
        errors.Should().BeEmpty();
        world.Writes.Should().ContainSingle().Which.Should().ContainKey("sprk_target");
        world.Entities.Verify(e => e.RetrieveAsync(Target, ChildId,
                It.Is<string[]>(c => c.SequenceEqual(new[] { AccessColumn })), It.IsAny<CancellationToken>()),
            Times.Once, "a source that is not secure reads only the child's Access Permission (no owner)");
    }

    [Fact]
    public async Task ARestrictedSecureSource_AndAnIsolatedRestrictedChild_IsWritten()
    {
        var source = SecureMatter();
        source[AccessColumn] = Level(InheritedAccessPermission.Restricted);
        var world = new World(source, new EntityReference("team", SecureTeam),
            childColumns: ChildAccess(Level(InheritedAccessPermission.Restricted)));

        (await world.Gate().RefusalForChildAsync(Target, ChildId, TextOnly, CancellationToken.None)).Should().BeNull();
    }

    public static TheoryData<string, object?> SourcesThatAreNotRestricted => new()
    {
        { "Standard", Level(InheritedAccessPermission.Standard) },
        { "Limited", Level(InheritedAccessPermission.Limited) },
        { "no value", null },
    };

    [Theory]
    [MemberData(nameof(SourcesThatAreNotRestricted))]
    public async Task AnOrdinarySourceThatIsNotRestricted_IsWritten_AndReadsNoChild(string because, object? value)
    {
        var world = new World(OrdinaryMatter(value), new EntityReference("team", OrdinaryTeam),
            childColumns: ChildAccess(Level(InheritedAccessPermission.Standard)));

        var (updated, failed, _, errors, _) = await world.PushAsync();

        (updated, failed).Should().Be((1, 0), because);
        errors.Should().BeEmpty();
        world.Entities.Verify(e => e.RetrieveAsync(Target, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "a source that is not secure and not Restricted reads no child");
    }

    [Fact]
    public async Task ASourceTableWithoutTheColumn_IsNotAskedForIt_AndReadsNoChild()
    {
        const string sourceTable = "sprk_memo";
        var sourceId = Guid.NewGuid();
        InheritedAccessPermission.SourceCarriesColumn(sourceTable).Should().BeFalse();
        var world = new World(SecureMatter(), new EntityReference("team", OrdinaryTeam));
        string[]? asked = null;
        world.Entities.Setup(e => e.RetrieveAsync(sourceTable, sourceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, string[] columns, CancellationToken _) => asked = columns)
            .ReturnsAsync(new Entity(sourceTable, sourceId)
            {
                ["ownerid"] = new EntityReference("team", OrdinaryTeam),
                [AccessColumn] = Level(InheritedAccessPermission.Restricted), // a value the table cannot hold: ignored
            });
        var gate = new FieldMappingPushSourceGate(world.Entities.Object, sourceTable, sourceId,
            _ => Task.FromResult<(Guid? TeamId, string? Refusal)>((SecureTeam, null)));

        (await gate.RefusalForChildAsync(Target, ChildId, TextOnly, CancellationToken.None)).Should().BeNull();

        asked.Should().NotContain(AccessColumn);
        world.Entities.Verify(e => e.RetrieveAsync(Target, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TheSourceRead_AsksForTheAccessPermission_InItsOneRead()
    {
        var world = new World(OrdinaryMatter(null), new EntityReference("team", OrdinaryTeam));
        string[]? asked = null;
        world.Entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, string[] columns, CancellationToken _) => asked = columns)
            .ReturnsAsync(OrdinaryMatter(null));

        await world.Gate().RefusalForChildAsync(Target, ChildId, TextOnly, CancellationToken.None);

        asked.Should().BeEquivalentTo(new[] { "ownerid", "sprk_issecure", AccessColumn });
        world.Entities.Verify(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ───────────────────────── fail closed (AC5) ─────────────────────────

    [Theory]
    [InlineData("the child read faults")]
    [InlineData("the child does not come back")]
    public async Task ARestrictedSource_AndAChildThatCannotBeRead_SkipsTheChild_WithTheUndeterminedCode(string fault)
    {
        var world = new World(OrdinaryMatter(Level(InheritedAccessPermission.Restricted)), new EntityReference("team", OrdinaryTeam));
        var read = world.Entities.Setup(e => e.RetrieveAsync(Target, ChildId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (fault == "the child read faults")
            read.ThrowsAsync(new InvalidOperationException("read failed"));
        else
            read.ReturnsAsync((Entity)null!);

        var (updated, failed, _, errors, _) = await world.PushAsync();

        (updated, failed).Should().Be((0, 1));
        var error = errors.Should().ContainSingle().Which;
        error.ReasonCode.Should().Be(FieldMappingPushSourceGate.UndeterminedReasonCode);
        error.Error.Should().NotContain("read failed");
        world.VerifyNothingWritten();
    }

    [Fact]
    public async Task ARestrictedSource_ACancelledChildRead_IsRethrown()
    {
        using var cts = new CancellationTokenSource();
        var world = new World(OrdinaryMatter(Level(InheritedAccessPermission.Restricted)), new EntityReference("team", OrdinaryTeam));
        world.Entities.Setup(e => e.RetrieveAsync(Target, ChildId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<Entity>(new OperationCanceledException(cts.Token));
            });

        var act = () => world.Gate().RefusalForChildAsync(Target, ChildId, TextOnly, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
