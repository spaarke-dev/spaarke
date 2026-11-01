using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 039 (FR-38, #2399; part E) — the caller-driven UPDATE paths of the BFF that can
/// reach a search-indexed table: <c>PATCH /api/v1/child-records/{table}/{id}</c> (the REAL <c>ChildRecordEndpoints.UpdateAsync</c>)
/// and the chat update tool (<c>DataverseUpdateRecordHandler</c>). On a search-indexed table both index columns are the
/// server's, so a body that names either has it dropped, in either bind spelling, never refused; the caller's own PATCH carries
/// the rest. A table that is not search-indexed is untouched.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid UpdatedRow = Guid.Parse("a0390000-0000-4000-8000-000000000001");
    private static readonly Guid ClientsIndex = Guid.Parse("a0390000-0000-4000-8000-0000000000a1");

    private const string IndexBind = "sprk_AI_Search_Index@odata.bind";
    private const string IndexName = "sprk_searchindexname";

    public static TheoryData<string> UpdatedSearchIndexedTables => new() { "sprk_workassignment", "sprk_event", "sprk_invoice", "sprk_matter" };

    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value);

    /// <summary>The body the caller's own PATCH carried (the last one), as the keys it named.</summary>
    private HashSet<string> PatchedKeys()
    {
        var (_, body) = _user.Patches.Should().ContainSingle().Subject;
        using var document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    // ── PATCH /api/v1/child-records/sprk_invoice/{id} ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(IndexBind)]
    [InlineData("sprk_ai_search_index@odata.bind")]
    public async Task ChildRecordPatch_AnInvoiceNamingTheSearchIndex_HasBothColumnsDropped_AndTheRestWritten_AndIsLogged(string bindKey)
    {
        var log = new CapturingLog<Program>();
        var result = await ChildRecordEndpoints.UpdateAsync(
            "sprk_invoice", UpdatedRow,
            Payload(new()
            {
                ["sprk_description"] = "Corrected",
                [IndexName] = "client-index",
                [bindKey] = $"/sprk_aisearchindexes({ClientsIndex:D})",
            }),
            _user, _world.Resolver(), Restamper(), Children(), HttpContextOfCaller(), log, CancellationToken.None,
            appOnlyEntities: AppEntities());

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        var keys = PatchedKeys();
        keys.Should().Contain("sprk_description", "the rest of the caller's own PATCH is written");
        keys.Should().NotContain(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)
            || string.Equals(k, IndexName, StringComparison.OrdinalIgnoreCase));
        var lines = log.Messages.Where(m => m.Contains("client_search_index_dropped")).ToList();
        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(m => m.Contains("table=sprk_invoice") && m.Contains($"record={UpdatedRow}")
            && m.Contains("origin=caller") && m.Contains("unchanged"));
        lines.Should().OnlyContain(m => !m.Contains('@'), "ids and catalog names only");
    }

    /// <summary>Review R1: the update paths share the one log site, so free text in the name never reaches their log either.</summary>
    [Fact]
    public async Task ChildRecordPatch_ADroppedNameThatIsFreeText_NeverReachesTheLog()
    {
        var log = new CapturingLog<Program>();
        var result = await ChildRecordEndpoints.UpdateAsync(
            "sprk_invoice", UpdatedRow,
            Payload(new() { ["sprk_description"] = "Corrected", [IndexName] = "jane.doe@contoso.com" }),
            _user, _world.Resolver(), Restamper(), Children(), HttpContextOfCaller(), log, CancellationToken.None,
            appOnlyEntities: AppEntities());

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        var line = log.Messages.Should().ContainSingle(m => m.Contains("client_search_index_dropped")).Subject;
        line.Should().NotContain("jane").And.NotContain("contoso").And.NotContain("@");
        line.Should().Contain("<not an index name> (length 20)");
    }

    /// <summary>Review R2 (NFR-07: drop, never refuse): a body that names only the index is answered as it was before task 039 (204), with nothing written.</summary>
    [Fact]
    public async Task ChildRecordPatch_AnInvoiceNamingOnlyTheSearchIndex_AnswersNoContent_AndWritesNothing()
    {
        var log = new CapturingLog<Program>();
        var result = await ChildRecordEndpoints.UpdateAsync(
            "sprk_invoice", UpdatedRow,
            Payload(new() { [IndexName] = "client-index", [IndexBind] = $"/sprk_aisearchindexes({ClientsIndex:D})" }),
            _user, _world.Resolver(), Restamper(), Children(), HttpContextOfCaller(), log, CancellationToken.None,
            appOnlyEntities: AppEntities());

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().BeEmpty("the index is never written by a client, and there is nothing else to write");
        log.Messages.Where(m => m.Contains("client_search_index_dropped")).Should().HaveCount(2, "the drop is still logged");
    }

    /// <summary>Review R2 control: a body that was empty BEFORE any drop keeps the mapper's empty-update refusal (the refusal is unchanged).</summary>
    [Theory]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_todo")]
    public async Task ChildRecordPatch_ABodyThatWasEmptyBeforeAnyDrop_KeepsItsExistingRefusal(string table)
    {
        var result = await ChildRecordEndpoints.UpdateAsync(
            table, UpdatedRow, Payload(new()),
            _user, _world.Resolver(), Restamper(), Children(), HttpContextOfCaller(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance, CancellationToken.None,
            appOnlyEntities: AppEntities());

        Status(result).Should().Be(StatusCodes.Status400BadRequest, "an update with no column is the mapper's empty-update answer");
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRecordPatch_ATableThatIsNotSearchIndexed_KeepsAColumnOfThatName()
    {
        var log = new CapturingLog<Program>();
        var result = await ChildRecordEndpoints.UpdateAsync(
            "sprk_todo", UpdatedRow,
            Payload(new() { ["sprk_description"] = "Corrected", [IndexName] = "kept" }),
            _user, _world.Resolver(), Restamper(), Children(), HttpContextOfCaller(), log, CancellationToken.None);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        PatchedKeys().Should().Contain(IndexName, "the drop is scoped by the registry");
        log.Messages.Should().NotContain(m => m.Contains("client_search_index_dropped"));
    }

    // ── The chat update tool ─────────────────────────────────────────────────────────────────────────────────────────

    private Task<Sprk.Bff.Api.Services.Ai.ToolResult> ChatUpdate(
        string table, CapturingLog<DataverseUpdateRecordHandler> log, params (string Column, JsonElement Value)[] item)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Children());
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new DataverseUpdateRecordHandler(
                _user,
                new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp,
                log,
                _world.Resolver(),
                Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
                scopes)
            .ExecuteChatAsync(
                BuildChatInvocationContext(
                    toolArgumentsJson: JsonSerializer.Serialize(new
                    {
                        tablename = table,
                        recordId = UpdatedRow,
                        item = item.ToDictionary(i => i.Column, i => i.Value),
                    })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(DataverseUpdateRecordHandler)), CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(UpdatedSearchIndexedTables))]
    public async Task ChatUpdate_ASearchIndexedTable_HasBothIndexColumnsDropped_AndTheRestWritten_AndIsLogged(string table)
    {
        var log = new CapturingLog<DataverseUpdateRecordHandler>();

        var result = await ChatUpdate(table, log,
            ("sprk_description", Text("Corrected")),
            (IndexName, Text("client-index")),
            Lookup("sprk_ai_search_index", "sprk_aisearchindex", ClientsIndex));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var keys = PatchedKeys();
        keys.Should().Contain("sprk_description");
        keys.Should().NotContain(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)
            || string.Equals(k, IndexName, StringComparison.OrdinalIgnoreCase));
        log.Messages.Where(m => m.Contains("client_search_index_dropped")).Should().HaveCount(2)
            .And.OnlyContain(m => m.Contains($"table={table}") && m.Contains("origin=caller") && m.Contains("unchanged"));
    }

    /// <summary>Review R2: an item that names only the index is a success that says so, with nothing written (drop, never refuse).</summary>
    [Fact]
    public async Task ChatUpdate_ASearchIndexedTable_NamingOnlyTheIndex_Succeeds_WithANote_AndWritesNothing()
    {
        var log = new CapturingLog<DataverseUpdateRecordHandler>();
        var result = await ChatUpdate("sprk_event", log, Lookup("sprk_ai_search_index", "sprk_aisearchindex", ClientsIndex));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Summary.Should().Contain("search index").And.Contain("server");
        _user.Patches.Should().BeEmpty();
        log.Messages.Where(m => m.Contains("client_search_index_dropped")).Should().HaveCount(1);
    }

    /// <summary>Review R2 control: an item that was empty BEFORE any drop keeps the mapper's empty-update refusal.</summary>
    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_todo")]
    public async Task ChatUpdate_AnItemThatWasEmptyBeforeAnyDrop_KeepsItsExistingRefusal(string table)
    {
        var result = await ChatUpdate(table, new CapturingLog<DataverseUpdateRecordHandler>());

        result.Success.Should().BeFalse("an update with no column is the mapper's empty-update answer");
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChatUpdate_ATableThatIsNotSearchIndexed_KeepsAColumnOfThatName()
    {
        var log = new CapturingLog<DataverseUpdateRecordHandler>();

        var result = await ChatUpdate("sprk_todo", log, ("sprk_description", Text("Corrected")), (IndexName, Text("kept")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        PatchedKeys().Should().Contain(IndexName);
        log.Messages.Should().NotContain(m => m.Contains("client_search_index_dropped"));
    }
}
