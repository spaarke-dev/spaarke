using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 045f on the two app-only update paths that write a record URL they are sent (the
/// playbook update and the output orchestrator): on a pair table outside the invoice family, the URL names the record the row's
/// regarding pair names after the write — the pair the same write sends, else the stored one, read app-only — and is written in
/// canonical form. A URL naming another table or record, or no record, is refused through the path's existing form (a
/// <see cref="RecordOwnerUnresolvedException"/> carrying <see cref="RegardingPairWrite.RecordUrlMismatchCode"/>), and nothing is
/// written; a stored-pair read that fails writes nothing. The field-mapping push never maps the URL (the mapping engine skips a
/// rule aimed at it), so it is not run here. Driven through the real paths (<see cref="RunAsync"/>).
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    private static readonly Guid Todo = Guid.Parse("02200000-0000-4000-8000-0000000000c1");

    private const string UrlColumn = "sprk_regardingrecordurl";

    public static TheoryData<GenericPath, string> EveryUrlWritingPathAndRuleTable()
    {
        var data = new TheoryData<GenericPath, string>();
        foreach (var path in new[] { GenericPath.PlaybookUpdate, GenericPath.OutputOrchestrator })
            foreach (var table in new[] { "sprk_todo", "sprk_event" })
                data.Add(path, table);
        return data;
    }

    /// <summary>
    /// The app-only world of one <paramref name="table"/> row whose stored pair names <paramref name="pairTable"/>
    /// <paramref name="pairId"/> (or none), and the catalog rows of the matter and the project. <paramref name="rowFault"/> makes
    /// the stored-row read fail.
    /// </summary>
    private static Mock<IGenericEntityService> StoredPair(
        string table, string? pairTable, Guid? pairId, Exception? rowFault = null)
    {
        var app = new Mock<IGenericEntityService>();
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        var stored = new Entity(table, Todo);
        if (pairId is { } id)
            stored["sprk_regardingrecordid"] = id.ToString("D");
        if (pairTable is not null)
            stored["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", CatalogRowOf(pairTable));
        var row = app.Setup(a => a.RetrieveAsync(table, Todo, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (rowFault is not null)
            row.ThrowsAsync(rowFault);
        else
            row.ReturnsAsync(stored);
        foreach (var core in new[] { "sprk_matter", "sprk_project" })
        {
            app.Setup(a => a.RetrieveAsync("sprk_recordtype_ref", CatalogRowOf(core), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_recordtype_ref", CatalogRowOf(core))
                {
                    [OwnedChildWrite.RecordTypeLogicalNameColumn] = core,
                    [RegardingRecordType.RecordNumberFieldColumn] = core + "number",
                });
        }

        return app;
    }

    private static string CanonicalUrl(string table, Guid id) =>
        RegardingRecordType.BuildRecordUrl(table, id.ToString("D").ToLowerInvariant());

    private static IReadOnlyDictionary<string, object?> SendsUrl(string? url, Guid? pairId = null)
    {
        var plain = new Dictionary<string, object?> { [UrlColumn] = url };
        if (pairId is { } id)
            plain["sprk_regardingrecordid"] = id.ToString("D");
        return plain;
    }

    private void RefusedAsUrlMismatch(PathOutcome outcome)
    {
        outcome.Code.Should().Be(RegardingPairWrite.RecordUrlMismatchCode, outcome.Message);
        outcome.Message.Should().Contain(OwnedChildWrite.RegardingRecordUrlRefusalText);
        NoGuid(outcome);
        NothingWritten();
    }

    // ── Must fire ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryUrlWritingPathAndRuleTable))]
    public async Task AUrlOfAnotherRecordThanTheStoredPair_IsRefused_AndNothingIsWritten(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, Todo, NoLookups, SendsUrl(CanonicalUrl("sprk_matter", OtherMatter)),
            StoredPair(table, "sprk_matter", Matter).Object);

        RefusedAsUrlMismatch(outcome);
    }

    [Theory]
    [MemberData(nameof(EveryUrlWritingPathAndRuleTable))]
    public async Task AUrlOfAnotherTableThanTheStoredPair_IsRefused_AndNothingIsWritten(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, Todo, NoLookups, SendsUrl(CanonicalUrl("sprk_project", Matter)),
            StoredPair(table, "sprk_matter", Matter).Object);

        RefusedAsUrlMismatch(outcome);
    }

    [Theory]
    [MemberData(nameof(EveryUrlWritingPathAndRuleTable))]
    public async Task AUrlThatNamesNoRecord_IsRefused_AndNothingIsWritten(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, Todo, NoLookups, SendsUrl("/main.aspx?etn=sprk_matter"),
            StoredPair(table, "sprk_matter", Matter).Object);

        RefusedAsUrlMismatch(outcome);
    }

    [Theory]
    [MemberData(nameof(EveryUrlWritingPathAndRuleTable))]
    public async Task APairMoveWithAUrlOfTheOldRecord_IsRefused_AndNothingIsWritten(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, Todo, Lookups(PairType(CatalogRowOf("sprk_project"))),
            SendsUrl(CanonicalUrl("sprk_matter", Matter), pairId: Project), StoredPair(table, "sprk_matter", Matter).Object);

        RefusedAsUrlMismatch(outcome);
    }

    [Theory]
    [MemberData(nameof(EveryUrlWritingPathAndRuleTable))]
    public async Task AUrlOnARowWithNoPair_IsRefused_AndNothingIsWritten(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, Todo, NoLookups, SendsUrl(CanonicalUrl("sprk_matter", Matter)),
            StoredPair(table, pairTable: null, pairId: null).Object);

        RefusedAsUrlMismatch(outcome);
    }

    [Theory]
    [MemberData(nameof(EveryUrlWritingPathAndRuleTable))]
    public async Task AStoredPairReadFault_WritesNothing(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, Todo, NoLookups, SendsUrl(CanonicalUrl("sprk_matter", Matter)),
            StoredPair(table, "sprk_matter", Matter, rowFault: new InvalidOperationException("unavailable")).Object);

        outcome.Code.Should().Be(RecordOwnerRefusal.ParentUndetermined, outcome.Message);
        NoGuid(outcome);
        NothingWritten();
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<GenericPath, string> AdmittedUrlForms()
    {
        var data = new TheoryData<GenericPath, string>();
        foreach (var path in new[] { GenericPath.PlaybookUpdate, GenericPath.OutputOrchestrator })
        {
            data.Add(path, $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={Matter:D}");
            data.Add(path, $"https://other.example/main.aspx?appid=0a1b2c3d-0000-4000-8000-000000000001&pagetype=entityrecord&etn=sprk_matter&id={Matter:D}");
            data.Add(path, $"http://other.example/main.aspx?etn=SPRK_MATTER&id={Matter.ToString("D").ToUpperInvariant()}");
            data.Add(path, $"/main.aspx?etn=sprk_matter&id={Matter:B}");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AdmittedUrlForms))]
    public async Task AUrlOfTheStoredPairsRecord_IsWritten_InCanonicalForm(GenericPath path, string url)
    {
        var outcome = await RunAsync(path, "sprk_todo", Todo, NoLookups, SendsUrl(url), StoredPair("sprk_todo", "sprk_matter", Matter).Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_todo", Todo);
        SentPayload("sprk_todo", Todo)[UrlColumn].Should().Be(CanonicalUrl("sprk_matter", Matter));
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task AUrlClear_IsWritten_AndReadsNoPair(GenericPath path)
    {
        var app = StoredPair("sprk_todo", "sprk_matter", Matter);

        var outcome = await RunAsync(path, "sprk_todo", Todo, NoLookups, SendsUrl(null), app.Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_todo", Todo);
        SentPayload("sprk_todo", Todo)[UrlColumn].Should().BeNull();
        app.Verify(a => a.RetrieveAsync("sprk_todo", Todo, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task APairMoveWithNoUrl_KeepsThePlannersDerivedUrl(GenericPath path)
    {
        // A run as a person: the planner derives the moved pair's display columns as that person (045b/045d).
        var outcome = await RunAsync(path, "sprk_todo", Todo, Lookups(PairType(CatalogRowOf("sprk_project"))),
            PairId(Project), StoredPair("sprk_todo", "sprk_matter", Matter).Object, playbookActsAs: Caller);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_todo", Todo);
        SentPayload("sprk_todo", Todo)[UrlColumn].Should().Be(CanonicalUrl("sprk_project", Project));
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task APairMoveWithAUrlOfTheNewRecord_IsWritten_InCanonicalForm(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_todo", Todo, Lookups(PairType(CatalogRowOf("sprk_project"))),
            SendsUrl($"https://other.example/main.aspx?etn=sprk_project&id={Project:B}", pairId: Project),
            StoredPair("sprk_todo", "sprk_matter", Matter).Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_todo", Todo);
        SentPayload("sprk_todo", Todo)[UrlColumn].Should().Be(CanonicalUrl("sprk_project", Project));
    }
}
