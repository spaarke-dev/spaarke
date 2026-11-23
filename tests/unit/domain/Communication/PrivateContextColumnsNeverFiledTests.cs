using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22: a private message is never filed; UAC-r2: fail closed wherever a
/// filing is read). The one predicate (<see cref="PrivateContextColumns.IsPrivate"/>), the columns a read adds so it can answer
/// (<see cref="PrivateContextColumns.PresentColumnsOf"/>) and the OData clause that keeps private messages out of a read
/// (<see cref="PrivateContextColumns.NotPrivateODataFilterAsync"/>), over a mocked <see cref="IGenericEntityService"/> metadata read.
/// </summary>
public sealed class PrivateContextColumnsNeverFiledTests
{
    private const string Communication = "sprk_communication";

    private static Entity Row(params (string Column, object? Value)[] columns)
    {
        var row = new Entity(Communication, Guid.NewGuid());
        foreach (var (column, value) in columns)
            row[column] = value;
        return row;
    }

    // ── IsPrivate ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PrivateContextColumns.Matter, "sprk_matter")]
    [InlineData(PrivateContextColumns.Project, "sprk_project")]
    [InlineData(PrivateContextColumns.WorkAssignment, "sprk_workassignment")]
    public void IsPrivate_AnyReferenceColumnHoldingARecord_IsPrivate(string column, string table) =>
        PrivateContextColumns.IsPrivate(Row((column, new EntityReference(table, Guid.NewGuid())))).Should().BeTrue();

    [Fact]
    public void IsPrivate_NoReferenceColumn_IsNotPrivate() =>
        PrivateContextColumns.IsPrivate(Row(("sprk_regardingmatter", new EntityReference("sprk_matter", Guid.NewGuid()))))
            .Should().BeFalse("a filed ordinary message holds no reference");

    [Fact]
    public void IsPrivate_NullAndEmptyReferences_AreNotPrivate() =>
        PrivateContextColumns.IsPrivate(Row(
                (PrivateContextColumns.Matter, null),
                (PrivateContextColumns.Project, new EntityReference("sprk_project", Guid.Empty))))
            .Should().BeFalse("Guid.Empty is no reference");

    [Fact]
    public void IsPrivate_AValueThatIsNotALookup_CountsAsAReference() =>
        PrivateContextColumns.IsPrivate(Row((PrivateContextColumns.Matter, "not a lookup")))
            .Should().BeTrue("a value that cannot be read as 'no reference' fails closed");

    // ── PresentColumnsOf ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PresentColumnsOf_ACommunication_GivesTheReferenceColumnsTheTableHas()
    {
        var host = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PrivateContextColumns.Matter, "sprk_regardingmatter" };

        PrivateContextColumns.PresentColumnsOf("sprk_communication", host).Should().Equal(PrivateContextColumns.Matter);
    }

    [Fact]
    public void PresentColumnsOf_AnotherTable_GivesNone() =>
        PrivateContextColumns.PresentColumnsOf("sprk_event", new HashSet<string>(PrivateContextColumns.All)).Should().BeEmpty();

    // ── NotPrivateODataFilterAsync ───────────────────────────────────────────────────────────────────────────────────────────

    private static Mock<IGenericEntityService> Metadata(Func<IReadOnlyDictionary<string, ColumnMetadata>?> columns)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => columns()!);
        return entities;
    }

    private static IReadOnlyDictionary<string, ColumnMetadata> Columns(params string[] names) =>
        names.Append("sprk_communicationid")
            .ToDictionary(c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task NotPrivateODataFilter_EveryColumnPresent_ExcludesEachReference()
    {
        var entities = Metadata(() => Columns(PrivateContextColumns.All.ToArray()));

        var clause = await PrivateContextColumns.NotPrivateODataFilterAsync(entities.Object, CancellationToken.None);

        clause.Should().Be(
            "(_sprk_privatecontextmatter_value eq null and _sprk_privatecontextproject_value eq null and " +
            "_sprk_privatecontextworkassignment_value eq null)");
    }

    [Fact]
    public async Task NotPrivateODataFilter_SomeColumnsPresent_ExcludesThoseOnly()
    {
        var entities = Metadata(() => Columns(PrivateContextColumns.Project));

        var clause = await PrivateContextColumns.NotPrivateODataFilterAsync(entities.Object, CancellationToken.None);

        clause.Should().Be("(_sprk_privatecontextproject_value eq null)", "a clause on a column the table lacks would fault the read");
    }

    [Fact]
    public async Task NotPrivateODataFilter_NoColumnPresent_LeavesTheReadAsItIs()
    {
        var entities = Metadata(() => Columns("sprk_regardingmatter"));

        (await PrivateContextColumns.NotPrivateODataFilterAsync(entities.Object, CancellationToken.None))
            .Should().BeNull("no private message can exist without the columns");
    }

    [Fact]
    public async Task NotPrivateODataFilter_AMetadataReadThatFaults_FaultsTheCallersRead()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("metadata unavailable"));

        var act = () => PrivateContextColumns.NotPrivateODataFilterAsync(entities.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("metadata unavailable");
    }

    [Fact]
    public async Task NotPrivateODataFilter_AMetadataReadWithNoAnswer_FaultsTheCallersRead()
    {
        var entities = Metadata(() => null);

        var act = () => PrivateContextColumns.NotPrivateODataFilterAsync(entities.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>("an unknown schema is never read as 'no private message'");
    }
}
