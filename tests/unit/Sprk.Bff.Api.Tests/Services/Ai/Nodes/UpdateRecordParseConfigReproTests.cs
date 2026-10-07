using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Reproduces the document-profile playbook "Update Record" node config parse failure observed in
/// dev (App Insights 2026-08-31: "Failed to parse update record configuration" → filesummarystatus
/// = Failed on every profiled document). The node's sprk_configjson is the Code-Page wrapper format
/// (outer {{__canvasNodeId,__actionType,isConfigured,validationErrors,configJson}} whose nested
/// configJson string holds the real config).
/// </summary>
public class UpdateRecordParseConfigReproTests
{
    private readonly ITestOutputHelper _out;
    public UpdateRecordParseConfigReproTests(ITestOutputHelper o) => _out = o;

    // The nested config exactly as stored (sprk_playbooknode 0fa4e8db-…, "Update Record").
    private const string NestedConfig =
        "{\"entityLogicalName\":\"sprk_document\",\"recordId\":\"{{document.id}}\",\"fieldMappings\":[" +
        "{\"field\":\"sprk_filesummary\",\"type\":\"string\",\"value\":\"{{output_aiAnalysis.output.sprk_filesummary}}\"}," +
        "{\"field\":\"sprk_documenttype\",\"type\":\"choice\",\"options\":{\"Contract\":100000000,\"Agreement\":100000007,\"Other\":100000012},\"value\":\"{{output_aiAnalysis.output.sprk_documenttype}}\"}" +
        "]}";

    private static string WrapperConfig() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["__canvasNodeId"] = "node_1772509260018_8qwhl3xe0",
        ["__actionType"] = 22,
        ["isConfigured"] = true,
        ["validationErrors"] = Array.Empty<object>(),
        ["configJson"] = NestedConfig
    });

    [Fact]
    public void ParseConfig_WrapperFormat_ParsesNestedConfig()
    {
        var result = UpdateRecordNodeExecutor.ParseConfig(WrapperConfig());

        // If this is null we have reproduced the production failure.
        result.Should().NotBeNull("the profile playbook's Update Record node uses this exact wrapper format");
        result!.EntityLogicalName.Should().Be("sprk_document");
        result.FieldMappings.Should().NotBeNull();
    }

    [Fact]
    public void DirectDeserialize_NestedConfig_SurfacesAnyThrow()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        Exception? thrown = null;
        UpdateRecordNodeConfig? cfg = null;
        try { cfg = JsonSerializer.Deserialize<UpdateRecordNodeConfig>(NestedConfig, options); }
        catch (Exception ex) { thrown = ex; }

        _out.WriteLine(thrown is null
            ? $"No throw. EntityLogicalName={cfg?.EntityLogicalName}, mappings={cfg?.FieldMappings?.Length}"
            : $"THROW: {thrown.GetType().Name}: {thrown.Message}");

        // Report, don't assert — the WriteLine reveals the real cause.
        thrown.Should().BeNull($"nested config should deserialize (throw was: {thrown?.Message})");
    }

    [Fact]
    public void RenderThenParse_WrapperFormatWithMultilineValue_ProducesParseableConfig()
    {
        // GitHub #919: the AI summary is long, MULTI-LINE text. Before the fix, Layer-1 rendering
        // escaped its newlines only at the OUTER wrapper level, so UpdateRecordNodeExecutor.ParseConfig
        // re-parsing the nested configJson string threw
        // "'0x0A' is invalid within a JSON string. Path: $.fieldMappings[0].value" and the node failed.
        const string multilineSummary = "Line one of the summary.\nLine two.\nLine three with a \"quoted\" clause.";
        var context = new Dictionary<string, object?>
        {
            ["document"] = new Dictionary<string, object?> { ["id"] = Guid.NewGuid().ToString() },
            ["output_aiAnalysis"] = new Dictionary<string, object?>
            {
                ["output"] = new Dictionary<string, object?>
                {
                    ["sprk_filesummary"] = multilineSummary,
                    ["sprk_documenttype"] = "Agreement"
                }
            }
        };

        // Layer-1 render (the fix): the nested wrapper configJson is rendered structurally so the
        // multi-line value is escaped at the NESTED level.
        var rendered = PlaybookOrchestrationService.RenderConfigJsonStructurally(
            WrapperConfig(), context, new TemplateEngine(NullLogger<TemplateEngine>.Instance));

        _out.WriteLine(rendered);

        // The rendered outer wrapper must still be valid JSON.
        Action parseOuter = () => { using var _ = JsonDocument.Parse(rendered); };
        parseOuter.Should().NotThrow("the rendered wrapper config must be valid JSON at the outer level");

        // The re-parse that used to throw 0x0A must now succeed, with the multi-line value intact.
        var parsed = UpdateRecordNodeExecutor.ParseConfig(rendered);
        parsed.Should().NotBeNull("the rendered nested config must survive ParseConfig's re-parse (#919)");
        parsed!.EntityLogicalName.Should().Be("sprk_document");
        parsed.FieldMappings.Should().NotBeNull();

        var summary = parsed.FieldMappings!.Single(m => m.Field == "sprk_filesummary");
        summary.Value.Should().Contain("Line two", "the summary content must be preserved");
        summary.Value.Should().Contain("\n", "the multi-line newlines must be preserved, not corrupt the JSON");
    }
}
