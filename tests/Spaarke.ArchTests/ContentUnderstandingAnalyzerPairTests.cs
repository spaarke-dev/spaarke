using System.Text.Json.Nodes;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// <b>D-81 analyzer pair kept in step</b> (ebilling-r1 task 043). The preview analyzer (<c>sprk-invoice-v2p.json</c>,
/// created on <c>2026-06-01-preview</c>) is primary; the GA analyzer (<c>sprk-invoice-v2.json</c>, <c>2025-11-01</c>) is
/// the fallback that a configuration change alone switches to. That switch is only safe while both read the SAME fields
/// the SAME way, so this test fails when the two definition files drift: field schema, models and base analyzer must be
/// identical, and the configs may differ only in preview-only keys. It also pins the BFF's built-in default pair to the
/// preview file. Enforced by: this test (per-PR ArchTests job). Deploy: <c>scripts/content-understanding/Deploy-Analyzers.ps1</c>.
/// </summary>
/// <remarks>KEEP path (ADR-038 §7): structural fitness function.</remarks>
public sealed class ContentUnderstandingAnalyzerPairTests
{
    /// <summary>
    /// Config keys only the preview API has: <c>allowInputTruncation</c> (kept false: a long document is refused, not cut)
    /// and <c>workflow</c> (server-resolved; a PUT only accepts 'default' or 'agentic').
    /// </summary>
    internal static readonly string[] PreviewOnlyConfigKeys = { "allowInputTruncation", "workflow" };

    private static readonly string Folder = Path.Combine(SourceScan.RepoRoot, "infrastructure", "content-understanding");

    private static JsonObject Load(string file) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, file)))!.AsObject();

    [Fact]
    public void PreviewAndGaAnalyzers_ReadTheSameFieldsTheSameWay()
    {
        var drift = Differences(Load("sprk-invoice-v2.json"), Load("sprk-invoice-v2p.json"));

        Assert.True(drift.Count == 0,
            "sprk-invoice-v2.json (GA fallback) and sprk-invoice-v2p.json (preview primary) have drifted (D-81 keeps them in step): "
            + string.Join("; ", drift));
    }

    [Fact]
    public void AnalyzerFiles_DeclareTheApiVersionTheyAreCreatedOn()
    {
        Assert.Equal("2025-11-01", Load("sprk-invoice-v2.json")["tags"]?["apiVersion"]?.GetValue<string>());
        Assert.Equal("2026-06-01-preview", Load("sprk-invoice-v2p.json")["tags"]?["apiVersion"]?.GetValue<string>());
        Assert.Equal("sprk_invoice_v2", Load("sprk-invoice-v2.json")["analyzerId"]?.GetValue<string>());
    }

    [Fact]
    public void BffDefaultPair_IsThePreviewAnalyzerFile()
    {
        var preview = Load("sprk-invoice-v2p.json");

        Assert.Equal(ContentUnderstandingOptions.PreviewAnalyzerId, preview["analyzerId"]?.GetValue<string>());
        Assert.Equal(ContentUnderstandingOptions.PreviewApiVersion, preview["tags"]?["apiVersion"]?.GetValue<string>());
    }

    [Fact]
    public void PreviewAnalyzer_NeverTruncatesInput()
    {
        Assert.False(Load("sprk-invoice-v2p.json")["config"]?["allowInputTruncation"]?.GetValue<bool>() ?? true,
            "allowInputTruncation must be present and false: a document over the page limit is refused (invoice.read.page_limit), never cut.");
    }

    // ── controls ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Control_FieldDescriptionChangedInOneFile_IsReported()
    {
        var ga = Load("sprk-invoice-v2.json");
        var preview = Load("sprk-invoice-v2p.json");
        preview["fieldSchema"]!["fields"]!["InvoiceDate"]!["description"] = "changed";

        Assert.Contains(Differences(ga, preview), d => d.StartsWith("fieldSchema", StringComparison.Ordinal));
    }

    [Fact]
    public void Control_ModelOrSharedConfigChangedInOneFile_IsReported()
    {
        var ga = Load("sprk-invoice-v2.json");
        var preview = Load("sprk-invoice-v2p.json");
        preview["models"]!["completion"] = "other-model";
        preview["config"]!["enableOcr"] = false;

        var drift = Differences(ga, preview);

        Assert.Contains(drift, d => d.StartsWith("models", StringComparison.Ordinal));
        Assert.Contains(drift, d => d.Contains("enableOcr", StringComparison.Ordinal));
    }

    [Fact]
    public void Control_PreviewOnlyConfigKey_IsNotDrift()
    {
        var ga = Load("sprk-invoice-v2.json");
        var preview = Load("sprk-invoice-v2p.json");
        preview["config"]!["workflow"] = "default";

        Assert.Empty(Differences(ga, preview));
    }

    internal static List<string> Differences(JsonObject ga, JsonObject preview)
    {
        var drift = new List<string>();
        foreach (var key in new[] { "fieldSchema", "models", "baseAnalyzerId", "processingLocation" })
        {
            if (!JsonNode.DeepEquals(ga[key], preview[key]))
            {
                drift.Add($"{key} differs");
            }
        }

        var gaConfig = ga["config"]?.AsObject() ?? new JsonObject();
        var previewConfig = preview["config"]?.AsObject() ?? new JsonObject();
        foreach (var key in gaConfig.Select(p => p.Key).Union(previewConfig.Select(p => p.Key)).Distinct())
        {
            if (PreviewOnlyConfigKeys.Contains(key, StringComparer.Ordinal))
            {
                if (gaConfig.ContainsKey(key))
                {
                    drift.Add($"config.{key} is preview-only but is in the GA file");
                }

                continue;
            }

            if (!JsonNode.DeepEquals(gaConfig[key], previewConfig[key]))
            {
                drift.Add($"config.{key} differs");
            }
        }

        return drift;
    }
}
