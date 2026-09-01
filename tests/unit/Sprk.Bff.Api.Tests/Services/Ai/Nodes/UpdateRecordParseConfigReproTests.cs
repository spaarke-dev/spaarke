using System.Text.Json;
using FluentAssertions;
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
}
