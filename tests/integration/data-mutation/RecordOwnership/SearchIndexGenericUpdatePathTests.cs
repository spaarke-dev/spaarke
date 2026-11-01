using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 039 (FR-38, #2399; part E) — the generic server update paths that do not go through
/// <c>OwnedChildWrite</c>: the output orchestrator (<see cref="DataverseUpdateHandler"/>), the playbook update
/// (<see cref="UpdateRecordActionCore"/>, also the action seam's) and the field-mapping push
/// (<c>ApplyMappingsToChildRecordsAsync</c>). On a search-indexed table both index columns are the server's: a write that names
/// either has it dropped (logged as <c>client_search_index_dropped</c>, applied "unchanged"), the rest of the write is made, and
/// a table that is not search-indexed is untouched.
/// </summary>
public sealed class SearchIndexGenericUpdatePathTests
{
    private static readonly Guid Row = Guid.Parse("a0390000-0000-4000-8000-0000000000b1");
    private static readonly Guid Index = Guid.Parse("a0390000-0000-4000-8000-0000000000b2");
    private static readonly Guid Matter = Guid.Parse("a0390000-0000-4000-8000-0000000000b3");
    private static readonly Guid Caller = Guid.Parse("a0390000-0000-4000-8000-0000000000aa");
    private const string NameColumn = "sprk_searchindexname";
    private const string LookupColumn = "sprk_ai_search_index";

    public static TheoryData<string> IndexedTables => new() { "sprk_workassignment", "sprk_event", "sprk_matter", "sprk_project", "sprk_document" }; // an invoice write reads the stored row for the family guard: the invoice is covered by the PATCH tests

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>The columns of every write the update sent (upsert or update-only), in order.</summary>
    private List<Dictionary<string, object?>> Written() =>
        _writes.Invocations
            .Where(i => i.Method.Name is nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync)
                or nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync))
            .Select(i => new Dictionary<string, object?>((IDictionary<string, object?>)i.Arguments[2], StringComparer.OrdinalIgnoreCase))
            .ToList();

    private static void NoIndexColumn(Dictionary<string, object?> fields) =>
        fields.Keys.Should().NotContain(k => k.StartsWith(LookupColumn, StringComparison.OrdinalIgnoreCase)
            || string.Equals(k, NameColumn, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Drops(IEnumerable<string> messages) => messages.Where(m => m.Contains("client_search_index_dropped"));

    // ── The output orchestrator: DataverseUpdateHandler ────────────────────────────────────────────────────────────

    private DataverseUpdateHandler Handler(CapturingLog<DataverseUpdateHandler> log) =>
        new(_writes.Object, new Mock<IGenericEntityService>().Object, new StampWorld().Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), log, SecureChildReconcilerFixtures.OverEmptyWorld());

    [Theory]
    [MemberData(nameof(IndexedTables))]
    public async Task DataverseUpdateHandler_AnOutputMappingNamingTheSearchIndex_HasBothColumnsDropped_AndTheRestWritten(string table)
    {
        var log = new CapturingLog<DataverseUpdateHandler>();
        var fields = new Dictionary<string, object?>
        {
            ["sprk_description"] = "Corrected",
            [NameColumn] = "client-index",
            [LookupColumn] = new EntityReference("sprk_aisearchindex", Index),
            ["sprk_AI_Search_Index@odata.bind"] = $"/sprk_aisearchindexes({Index:D})",
        };

        await Handler(log).UpdateAsync(table, Row, fields, ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        var written = Written().Should().ContainSingle().Subject;
        written.Should().ContainKey("sprk_description");
        NoIndexColumn(written);
        fields.Should().HaveCount(4, "the caller's dictionary is not changed: the drop is made on a copy");
        Drops(log.Messages).Should().HaveCount(2, "one per column (the lookup in two spellings is one column)").And.OnlyContain(m => m.Contains($"table={table}") && m.Contains($"record={Row}")
            && m.Contains("origin=mapping") && m.Contains("unchanged"));
    }

    /// <summary>Round 2 (S5): a mapping left with only the index writes nothing: the writer is never asked for an empty update.</summary>
    [Theory]
    [MemberData(nameof(IndexedTables))]
    public async Task DataverseUpdateHandler_AnOutputMappingNamingOnlyTheSearchIndex_AsksForNoWrite(string table)
    {
        var log = new CapturingLog<DataverseUpdateHandler>();
        var fields = new Dictionary<string, object?> { [NameColumn] = "client-index", [LookupColumn] = new EntityReference("sprk_aisearchindex", Index) };

        await Handler(log).UpdateAsync(table, Row, fields, ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        Written().Should().BeEmpty("nothing is left to write, so no write (not even an empty one) is asked of Dataverse");
        Drops(log.Messages).Should().HaveCount(2);
    }

    [Fact]
    public async Task DataverseUpdateHandler_ATableThatIsNotSearchIndexed_KeepsAColumnOfThatName()
    {
        var log = new CapturingLog<DataverseUpdateHandler>();

        await Handler(log).UpdateAsync("sprk_todo", Row,
            new Dictionary<string, object?> { ["sprk_description"] = "Corrected", [NameColumn] = "kept" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        Written().Should().ContainSingle().Which.Should().ContainKey(NameColumn);
        Drops(log.Messages).Should().BeEmpty();
    }

    // ── The playbook update: UpdateRecordActionCore ────────────────────────────────────────────────────────────────

    private UpdateRecordActionCore Core(CapturingLog<UpdateRecordActionCore> log)
    {
        var app = new Mock<IGenericEntityService>();
        app.Setup(a => a.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, CancellationToken _) => EntitySetNameFixtures.SetOf(table));
        return new(_writes.Object,
            new ServiceCollection()
                .AddSingleton(app.Object)
                .AddSingleton(new StampWorld().Restamper)
                .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            log);
    }

    [Theory]
    [InlineData("sprk_workassignment", false, "origin=mapping")]
    [InlineData("sprk_event", false, "origin=mapping")]
    [InlineData("sprk_matter", false, "origin=mapping")]
    [InlineData("sprk_event", true, "origin=caller")]
    public async Task UpdateRecordActionCore_ASearchIndexedTable_HasBothColumnsDropped_AndTheRestWritten(
        string table, bool confirmedProposal, string origin)
    {
        var log = new CapturingLog<UpdateRecordActionCore>();

        await Core(log).UpdateAsync(
            new UpdateRecordActionInput(table, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_description"] = "Corrected", [NameColumn] = "client-index" },
                Lookups: [new RenderedLookup(LookupColumn, "sprk_aisearchindex", Index.ToString())],
                ImpersonateSystemUserId: confirmedProposal ? Caller : null,
                PayloadSource: confirmedProposal ? UpdateRecordPayloadSource.ConfirmedProposal : UpdateRecordPayloadSource.Configuration),
            CancellationToken.None);

        var written = Written().Should().ContainSingle().Subject;
        written.Should().ContainKey("sprk_description");
        NoIndexColumn(written);
        Drops(log.Messages).Should().HaveCount(2).And.OnlyContain(m => m.Contains($"table={table}") && m.Contains(origin) && m.Contains("unchanged"));
    }

    /// <summary>Round 2 (S5): a node or proposal left with only the index writes nothing: the writer is never asked for an empty update.</summary>
    [Theory]
    [InlineData("sprk_event", false)]
    [InlineData("sprk_matter", false)]
    [InlineData("sprk_event", true)]
    public async Task UpdateRecordActionCore_AnUpdateNamingOnlyTheSearchIndex_AsksForNoWrite(string table, bool confirmedProposal)
    {
        var log = new CapturingLog<UpdateRecordActionCore>();

        await Core(log).UpdateAsync(
            new UpdateRecordActionInput(table, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { [NameColumn] = "client-index" },
                Lookups: [new RenderedLookup(LookupColumn, "sprk_aisearchindex", Index.ToString())],
                ImpersonateSystemUserId: confirmedProposal ? Caller : null,
                PayloadSource: confirmedProposal ? UpdateRecordPayloadSource.ConfirmedProposal : UpdateRecordPayloadSource.Configuration),
            CancellationToken.None);

        Written().Should().BeEmpty("nothing is left to write, so no write (not even an empty one) is asked of Dataverse");
        Drops(log.Messages).Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateRecordActionCore_ATableThatIsNotSearchIndexed_KeepsAColumnOfThatName()
    {
        var log = new CapturingLog<UpdateRecordActionCore>();

        await Core(log).UpdateAsync(
            new UpdateRecordActionInput("sprk_todo", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_description"] = "Corrected", [NameColumn] = "kept" }, Lookups: null),
            CancellationToken.None);

        Written().Should().ContainSingle().Which.Should().ContainKey(NameColumn);
        Drops(log.Messages).Should().BeEmpty();
    }

    // ── The field-mapping push: ApplyMappingsToChildRecordsAsync ─────────────────────────────────────────────────

    private static FieldMappingRuleDto TextRule(string source, string target) => new()
    {
        SourceField = source, TargetField = target, SourceFieldType = "Text", TargetFieldType = "Text", Priority = 1,
    };

    private static readonly FieldMappingRuleDto IndexLookupRule = new()
    {
        SourceField = "sprk_sourceindex", TargetField = LookupColumn, SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 2,
    };

    [Theory]
    [InlineData("sprk_workassignment")]
    [InlineData("sprk_event")]
    public async Task FieldMappingPush_ARuleAimedAtEitherIndexColumn_IsSkipped_NotWritten_AndLogged(string table)
    {
        var world = new StampWorld().Row(table, Row, [("sprk_regardingmatter", "sprk_matter", Matter)]);
        var log = new CapturingLog<object>();
        var descriptionRule = TextRule("sprk_note", "sprk_description");
        var nameRule = TextRule("sprk_indexname", NameColumn);

        var (updated, failed, _, errors, results) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object, world.Restamper, [descriptionRule, nameRule, IndexLookupRule],
            new Dictionary<string, object?>
            {
                ["sprk_note"] = "From the source",
                ["sprk_indexname"] = "source-index",
                ["sprk_sourceindex"] = Index,
            },
            table, [Row], Caller, log, CancellationToken.None,
            lookupBinds: new Dictionary<FieldMappingRuleDto, LookupBindPlan>
            {
                [IndexLookupRule] = new("sprk_AI_Search_Index@odata.bind", $"/sprk_aisearchindexes({Index:D})", null),
            },
            appOnlyEntities: new Mock<IGenericEntityService>(MockBehavior.Strict).Object);

        updated.Should().Be(1);
        failed.Should().Be(0);
        errors.Should().BeEmpty();
        var written = Written().Should().ContainSingle().Subject;
        written.Should().ContainKey("sprk_description");
        NoIndexColumn(written);
        results.Where(r => r.TargetField is NameColumn or LookupColumn).Should().OnlyContain(r => r.Status == FieldMappingStatus.Skipped);
        Drops(log.Messages).Should().HaveCount(2).And.OnlyContain(m => m.Contains($"table={table}") && m.Contains($"record={Row}")
            && m.Contains("origin=mapping") && m.Contains("unchanged"));
    }

    /// <summary>Round 2 (S5): a push whose only rules aim at the index asks for no write (the push already writes only a non-empty payload).</summary>
    [Fact]
    public async Task FieldMappingPush_RulesAimedOnlyAtTheSearchIndex_AskForNoWrite()
    {
        var world = new StampWorld().Row("sprk_event", Row, [("sprk_regardingmatter", "sprk_matter", Matter)]);

        var (_, failed, _, errors, results) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object, world.Restamper, [TextRule("sprk_indexname", NameColumn), IndexLookupRule],
            new Dictionary<string, object?> { ["sprk_indexname"] = "source-index", ["sprk_sourceindex"] = Index },
            "sprk_event", [Row], Caller, new CapturingLog<object>(), CancellationToken.None,
            lookupBinds: new Dictionary<FieldMappingRuleDto, LookupBindPlan>
            {
                [IndexLookupRule] = new("sprk_AI_Search_Index@odata.bind", $"/sprk_aisearchindexes({Index:D})", null),
            },
            appOnlyEntities: new Mock<IGenericEntityService>(MockBehavior.Strict).Object);

        failed.Should().Be(0);
        errors.Should().BeEmpty();
        Written().Should().BeEmpty();
        results.Should().OnlyContain(r => r.Status == FieldMappingStatus.Skipped);
    }

    [Fact]
    public async Task FieldMappingPush_ATableThatIsNotSearchIndexed_WritesARuleOfThatName()
    {
        var world = new StampWorld().Row("sprk_todo", Row, [("sprk_regardingmatter", "sprk_matter", Matter)]);
        var log = new CapturingLog<object>();

        var (updated, _, _, _, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object, world.Restamper, [TextRule("sprk_indexname", NameColumn)],
            new Dictionary<string, object?> { ["sprk_indexname"] = "kept" },
            "sprk_todo", [Row], Caller, log, CancellationToken.None,
            appOnlyEntities: new Mock<IGenericEntityService>(MockBehavior.Strict).Object);

        updated.Should().Be(1);
        Written().Should().ContainSingle().Which.Should().ContainKey(NameColumn);
        Drops(log.Messages).Should().BeEmpty();
    }
}
