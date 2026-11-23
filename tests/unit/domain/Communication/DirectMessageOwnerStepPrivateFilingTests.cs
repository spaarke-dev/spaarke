using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22: a private message "always stays 1:1 - no filing"). A member of the
/// Direct team can file a private message outside the BFF (a form, the Web API). <see cref="DirectMessageOwnerStep.ClearPrivateFilingAsync"/>
/// clears exactly the filing columns such a row holds, in one update conditional on its version, and writes nothing else; a row that
/// is not private, or holds no filing, is never written; a fault writes nothing.
/// </summary>
public class DirectMessageOwnerStepPrivateFilingTests
{
    private const string Communication = "sprk_communication";
    private const long Version = 41;

    private static readonly Guid MessageId = Guid.Parse("b056b056-0000-0000-0000-000000000001");
    private static readonly Guid Thread = Guid.Parse("b056b056-0000-0000-0000-000000000002");
    private static readonly Guid Matter = Guid.Parse("b056b056-0000-0000-0000-000000000003");
    private static readonly Guid OtherMatter = Guid.Parse("b056b056-0000-0000-0000-000000000004");
    private static readonly Guid Contact = Guid.Parse("b056b056-0000-0000-0000-000000000005");
    private static readonly Guid Account = Guid.Parse("b056b056-0000-0000-0000-000000000006");
    private static readonly Guid RecordType = Guid.Parse("b056b056-0000-0000-0000-000000000007");

    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly DirectMessageOwnerStepTests.RecordingLogger _log = new();
    private readonly List<(string Table, Guid Id, string Version, Dictionary<string, object> Fields)> _updates = new();

    public DirectMessageOwnerStepPrivateFilingTests()
    {
        _dataverse
            .Setup(d => d.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) =>
                _updates.Add((table, id, version, fields)))
            .Returns(Task.CompletedTask);
    }

    // ── rows ──

    public static TheoryData<string> ReferenceColumns() => new() { PrivateContextColumns.Matter, PrivateContextColumns.Project, PrivateContextColumns.WorkAssignment };

    private static readonly IReadOnlyDictionary<string, string> TableOfReference = new Dictionary<string, string>
    {
        [PrivateContextColumns.Matter] = "sprk_matter",
        [PrivateContextColumns.Project] = "sprk_project",
        [PrivateContextColumns.WorkAssignment] = "sprk_workassignment",
    };

    /// <summary>A Direct message in its pair's thread, read as the share reconcile reads it; <paramref name="reference"/> makes it private.</summary>
    private static Entity Message(string? reference, params (string Column, object Value)[] filing)
    {
        var row = new Entity(Communication, MessageId)
        {
            ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", Thread),
            ["sprk_sentby"] = new EntityReference("systemuser", Guid.NewGuid()),
            ["sprk_directrecipient"] = new EntityReference("systemuser", Guid.NewGuid()),
            ["versionnumber"] = Version,
        };
        if (reference is not null)
            row[reference] = new EntityReference(TableOfReference[reference], Matter);
        foreach (var (column, value) in filing)
            row[column] = value;
        return row;
    }

    /// <summary>The kinds of filing set outside the BFF, each with the columns the clear must name.</summary>
    public static IEnumerable<object[]> PrivateRowsFiledOutOfBand()
    {
        var filings = new (string Name, (string Column, object Value)[] Set)[]
        {
            ("ownership lookup", new[] { ("sprk_regardingmatter", (object)new EntityReference("sprk_matter", OtherMatter)) }),
            ("typed contact regarding", new[] { ("sprk_regardingperson", (object)new EntityReference("contact", Contact)) }),
            ("typed account regarding", new[] { ("sprk_regardingaccount", (object)new EntityReference("account", Account)) }),
            ("regarding pair", new[]
            {
                (CoreAncestorResolver.RegardingRecordIdColumn, (object)OtherMatter.ToString()),
                (CoreAncestorResolver.RegardingRecordTypeColumn, (object)new EntityReference("sprk_recordtype_ref", RecordType)),
            }),
            ("all of them", new[]
            {
                ("sprk_regardingproject", (object)new EntityReference("sprk_project", OtherMatter)),
                ("sprk_regardingorganization", (object)new EntityReference("sprk_organization", Account)),
                (CoreAncestorResolver.RegardingRecordIdColumn, (object)OtherMatter.ToString()),
            }),
        };

        foreach (var reference in new[] { PrivateContextColumns.Matter, PrivateContextColumns.Project, PrivateContextColumns.WorkAssignment })
            foreach (var (name, set) in filings)
                yield return new object[] { reference, name, set };
    }

    private Task<PrivateFilingClearOutcome> ClearAsync(Entity row) =>
        DirectMessageOwnerStep.ClearPrivateFilingAsync(_dataverse.Object, row, _log, CancellationToken.None);

    // ── the clear ──

    [Theory]
    [MemberData(nameof(PrivateRowsFiledOutOfBand))]
    public async Task APrivateRowFiledOutsideTheBff_HasExactlyThoseColumnsCleared_InOneConditionalUpdate(
        string reference, string filing, (string Column, object Value)[] set)
    {
        var row = Message(reference, set);

        DirectMessageOwnerStep.NeedsPrivateFilingClear(row).Should().BeTrue(filing);
        var outcome = await ClearAsync(row);

        outcome.Should().Be(PrivateFilingClearOutcome.Cleared);
        var update = _updates.Should().ContainSingle("one update clears every filing column the row holds").Subject;
        update.Table.Should().Be(Communication);
        update.Id.Should().Be(MessageId);
        update.Version.Should().Be(Version.ToString(System.Globalization.CultureInfo.InvariantCulture), "conditional on the version read");
        update.Fields.Keys.Should().BeEquivalentTo(set.Select(s => s.Column), "exactly the filing columns that were set: " + filing);
        update.Fields.Values.Should().AllSatisfy(v => v.Should().Be(DBNull.Value), "each is cleared");
        update.Fields.Keys.Should().NotContain(PrivateContextColumns.All, "the reference stays as written at create");
        update.Fields.Keys.Should().NotContain(new[] { "ownerid", "sprk_communicationthread", "sprk_sentby", "sprk_directrecipient" },
            "no owner, thread or pair change");

        var warning = _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Event.Id.Should().Be(DirectMessageOwnerStep.PrivateFilingClearedEvent.Id);
        warning.Message.Should().Contain(MessageId.ToString());
        foreach (var (column, _) in set)
            warning.Message.Should().Contain(column);
        warning.Message.Should().NotContain(OtherMatter.ToString(), "the log names columns, never a value")
            .And.NotContain(Contact.ToString()).And.NotContain(Account.ToString()).And.NotContain(RecordType.ToString())
            .And.NotContain(Matter.ToString());
        _log.Errors.Should().BeEmpty("the cleared row is filed under nothing");
    }

    [Fact]
    public async Task APrivateRowWithTheRegardingPair_HasThePairsDisplayColumnsCleared_InTheSameUpdate()
    {
        var row = Message(PrivateContextColumns.Matter,
            (CoreAncestorResolver.RegardingRecordIdColumn, (object)OtherMatter.ToString()),
            (CoreAncestorResolver.RegardingRecordTypeColumn, new EntityReference("sprk_recordtype_ref", RecordType)),
            ("sprk_regardingrecordname", "Acme v. Example"),
            ("sprk_regardingrecordnumber", "MAT-0042"),
            ("sprk_regardingrecordurl", "https://example.invalid/main.aspx?etn=sprk_matter"));

        (await ClearAsync(row)).Should().Be(PrivateFilingClearOutcome.Cleared);

        var update = _updates.Should().ContainSingle("the display columns go in the same conditional update").Subject;
        update.Version.Should().Be(Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        update.Fields.Keys.Should().BeEquivalentTo(new[]
        {
            CoreAncestorResolver.RegardingRecordIdColumn, CoreAncestorResolver.RegardingRecordTypeColumn,
            "sprk_regardingrecordname", "sprk_regardingrecordnumber", "sprk_regardingrecordurl",
        });
        update.Fields.Values.Should().AllSatisfy(v => v.Should().Be(DBNull.Value));
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Which.Message
            .Should().NotContain("Acme").And.NotContain("MAT-0042", "the log names columns, never a value");
    }

    [Fact]
    public async Task APrivateRowHoldingOnlyADisplayColumn_HasItCleared()
    {
        // A row whose pair an earlier clear removed still shows the record's name until its display columns go.
        var row = Message(PrivateContextColumns.Project, ("sprk_regardingrecordname", (object)"Acme v. Example"));

        DirectMessageOwnerStep.NeedsPrivateFilingClear(row).Should().BeTrue();
        (await ClearAsync(row)).Should().Be(PrivateFilingClearOutcome.Cleared);
        _updates.Should().ContainSingle().Which.Fields.Keys.Should().BeEquivalentTo(new[] { "sprk_regardingrecordname" });
    }

    [Fact]
    public async Task AnOrdinaryDirectRow_WithDisplayColumns_IsLeftAlone()
    {
        var row = Message(reference: null, ("sprk_regardingrecordname", (object)"Acme v. Example"));

        DirectMessageOwnerStep.NeedsPrivateFilingClear(row).Should().BeFalse();
        (await ClearAsync(row)).Should().Be(PrivateFilingClearOutcome.NotPrivate);
        _updates.Should().BeEmpty();
    }

    [Fact]
    public void TheDisplayColumns_AreTheOnesAPairChangeClears()
    {
        DirectMessageOwnerStep.PrivateDisplayColumns.Should().BeEquivalentTo(OwnedChildWrite.PairDisplayColumns);
        DirectMessageOwnerStep.PrivateDisplayColumns.Should().BeEquivalentTo(
            new[] { "sprk_regardingrecordname", "sprk_regardingrecordnumber", "sprk_regardingrecordurl" });
    }

    [Fact]
    public async Task AnOrdinaryDirectRow_WithAFilingLookup_IsLeftAlone()
    {
        var row = Message(reference: null, ("sprk_regardingmatter", new EntityReference("sprk_matter", OtherMatter)));

        DirectMessageOwnerStep.NeedsPrivateFilingClear(row).Should().BeFalse();
        (await ClearAsync(row)).Should().Be(PrivateFilingClearOutcome.NotPrivate);

        _updates.Should().BeEmpty("a row without a private reference is never written");
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEmptyReference_IsNotPrivate_AndIsLeftAlone()
    {
        var row = Message(reference: null, ("sprk_regardingmatter", new EntityReference("sprk_matter", OtherMatter)));
        row[PrivateContextColumns.Matter] = new EntityReference("sprk_matter", Guid.Empty);

        DirectMessageOwnerStep.NeedsPrivateFilingClear(row).Should().BeFalse();
        (await ClearAsync(row)).Should().Be(PrivateFilingClearOutcome.NotPrivate);
        _updates.Should().BeEmpty();
    }

    [Fact]
    public async Task AnotherTable_IsNeverWritten()
    {
        var row = Message(PrivateContextColumns.Matter, ("sprk_regardingmatter", new EntityReference("sprk_matter", OtherMatter)));
        var other = new Entity("sprk_event", row.Id);
        foreach (var attribute in row.Attributes)
            other[attribute.Key] = attribute.Value;

        DirectMessageOwnerStep.NeedsPrivateFilingClear(other).Should().BeFalse();
        (await ClearAsync(other)).Should().Be(PrivateFilingClearOutcome.NotPrivate);
        _updates.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ReferenceColumns))]
    public async Task APrivateRowWithNoFiling_IsLeftAlone(string reference)
    {
        // Blank or empty values name nothing.
        var row = Message(reference,
            (CoreAncestorResolver.RegardingRecordIdColumn, "  "),
            ("sprk_regardingmatter", new EntityReference("sprk_matter", Guid.Empty)));

        DirectMessageOwnerStep.NeedsPrivateFilingClear(row).Should().BeFalse();
        (await ClearAsync(row)).Should().Be(PrivateFilingClearOutcome.NotFiled);

        _updates.Should().BeEmpty();
        _log.Entries.Should().BeEmpty();
    }

    // ── faults write nothing ──

    [Fact]
    public async Task AVersionMismatch_WritesNothing_AndIsReturned()
    {
        _dataverse.Reset();
        _dataverse
            .Setup(d => d.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Data.DBConcurrencyException("Test: changed since read."));
        var row = Message(PrivateContextColumns.Project, ("sprk_regardingmatter", new EntityReference("sprk_matter", OtherMatter)));

        (await ClearAsync(row)).Should().Be(PrivateFilingClearOutcome.VersionMismatch);

        var warning = _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Event.Id.Should().Be(DirectMessageOwnerStep.PrivateFilingNotClearedEvent.Id);
        warning.Message.Should().Contain("sprk_regardingmatter").And.NotContain(OtherMatter.ToString());
    }

    [Fact]
    public async Task AnUpdateThatThrows_IsReturnedAsAFault_AndDoesNotThrow()
    {
        _dataverse.Reset();
        _dataverse
            .Setup(d => d.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test: Dataverse refused the write."));
        var row = Message(PrivateContextColumns.WorkAssignment, ("sprk_regardingperson", new EntityReference("contact", Contact)));

        (await ClearAsync(row)).Should().Be(PrivateFilingClearOutcome.Faulted);
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Event.Id == DirectMessageOwnerStep.PrivateFilingNotClearedEvent.Id);
    }

    [Fact]
    public async Task ARowReadWithoutItsVersion_IsNotWritten()
    {
        var row = Message(PrivateContextColumns.Matter, ("sprk_regardingmatter", new EntityReference("sprk_matter", OtherMatter)));
        row.Attributes.Remove("versionnumber");

        (await ClearAsync(row)).Should().Be(PrivateFilingClearOutcome.Faulted);
        _updates.Should().BeEmpty("an unconditional clear could overwrite a newer change");
    }

    [Fact]
    public async Task TheCallersCancellation_Propagates()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        _dataverse.Reset();
        _dataverse
            .Setup(d => d.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelled.Token));
        var row = Message(PrivateContextColumns.Matter, ("sprk_regardingmatter", new EntityReference("sprk_matter", OtherMatter)));

        var act = () => DirectMessageOwnerStep.ClearPrivateFilingAsync(_dataverse.Object, row, _log, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── the column lists ──

    [Fact]
    public void TheFilingColumns_AreTheCanonicalLists_AndNeverTheReferenceOrTheThread()
    {
        var lineage = SecureChildLineage.Children[Communication].LineageColumns.Where(c => c != "sprk_communicationthread");
        var expected = lineage
            .Concat(RegardingFieldMap.AllRegardingFields)
            .Concat(new[] { CoreAncestorResolver.RegardingRecordIdColumn, CoreAncestorResolver.RegardingRecordTypeColumn })
            .Distinct(StringComparer.OrdinalIgnoreCase);

        DirectMessageOwnerStep.PrivateFilingColumns.Should().BeEquivalentTo(expected);
        DirectMessageOwnerStep.PrivateFilingColumns.Should().Contain(new[]
        {
            "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment", "sprk_regardingperson",
            "sprk_regardingaccount", "sprk_regardingrecordid", "sprk_regardingrecordtype",
        });
        DirectMessageOwnerStep.PrivateFilingColumns.Should().NotContain(PrivateContextColumns.All);
        DirectMessageOwnerStep.PrivateFilingColumns.Should().NotContain("sprk_communicationthread");
    }

    [Fact]
    public async Task TheSelection_IsEmpty_WhenTheReferenceColumnsAreAbsent()
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Columns(DirectMessageOwnerStep.PrivateFilingColumns));

        (await DirectMessageOwnerStep.PrivateFilingSelectionAsync(dataverse.Object, _log, CancellationToken.None)).Should().BeEmpty();
        _log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning, "absent columns are the normal state before the schema script");
    }

    [Fact]
    public async Task TheSelection_IsEmpty_WhenTheMetadataCannotBeRead()
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test: metadata unavailable."));

        (await DirectMessageOwnerStep.PrivateFilingSelectionAsync(dataverse.Object, _log, CancellationToken.None)).Should().BeEmpty();
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task TheSelection_StillNamesTheOtherFilingColumns_WhenOneFilingColumnIsAbsent()
    {
        var absent = DirectMessageOwnerStep.PrivateFilingColumns[0];
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Columns(PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns.Skip(1))));

        var selection = await DirectMessageOwnerStep.PrivateFilingSelectionAsync(dataverse.Object, _log, CancellationToken.None);

        selection.Should().BeEquivalentTo(
            PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns.Skip(1)).Append("versionnumber"),
            "the columns that exist are selected, so they are cleared; the absent one is not named");
        selection.Should().NotContain(absent);
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains(absent));
    }

    [Fact]
    public async Task TheSelection_NamesOnlyTheDisplayColumnsThatExist()
    {
        var display = DirectMessageOwnerStep.PrivateDisplayColumns;
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Columns(PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns).Concat(display.Skip(1))));

        var selection = await DirectMessageOwnerStep.PrivateFilingSelectionAsync(dataverse.Object, _log, CancellationToken.None);

        selection.Should().Contain(display.Skip(1)).And.NotContain(display[0]);
    }

    [Fact]
    public async Task TheSelection_RunsOnTheReferenceColumnsThatExist_WhenOnlySomeAreThere()
    {
        // A column absent from metadata holds no reference (the rule every reader of the reference columns follows), so a private
        // message that can exist is judged on the columns that do.
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Columns(new[] { PrivateContextColumns.Matter }.Concat(DirectMessageOwnerStep.PrivateFilingColumns)));

        var selection = await DirectMessageOwnerStep.PrivateFilingSelectionAsync(dataverse.Object, _log, CancellationToken.None);

        selection.Should().BeEquivalentTo(new[] { PrivateContextColumns.Matter }.Concat(DirectMessageOwnerStep.PrivateFilingColumns).Append("versionnumber"));
    }

    [Fact]
    public async Task TheSelection_IsEmpty_WhenNoReferenceColumnExists_EvenWithEveryFilingColumn()
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Columns(DirectMessageOwnerStep.PrivateFilingColumns.Concat(DirectMessageOwnerStep.PrivateDisplayColumns)));

        (await DirectMessageOwnerStep.PrivateFilingSelectionAsync(dataverse.Object, _log, CancellationToken.None)).Should().BeEmpty();
        _log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task TheSelection_NamesTheReference_TheFilingColumns_AndTheVersion_WhenTheyExist()
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Columns(PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns)));

        var selection = await DirectMessageOwnerStep.PrivateFilingSelectionAsync(dataverse.Object, _log, CancellationToken.None);

        selection.Should().BeEquivalentTo(PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns).Append("versionnumber"),
            "the display columns are not selected while any is absent: a select naming a missing column fails the whole read");
    }

    [Fact]
    public async Task TheSelection_AlsoNamesThePairsDisplayColumns_WhenTheyExist()
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Columns(PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns)
                .Concat(DirectMessageOwnerStep.PrivateDisplayColumns)));

        var selection = await DirectMessageOwnerStep.PrivateFilingSelectionAsync(dataverse.Object, _log, CancellationToken.None);

        selection.Should().BeEquivalentTo(PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns)
            .Concat(DirectMessageOwnerStep.PrivateDisplayColumns).Append("versionnumber"));
    }

    private static IReadOnlyDictionary<string, ColumnMetadata> Columns(IEnumerable<string> names) =>
        names.Append("sprk_communicationid").Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase);
}
