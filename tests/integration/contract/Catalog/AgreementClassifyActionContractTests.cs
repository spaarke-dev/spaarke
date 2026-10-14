using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract test for the <c>agreement-classify</c> document-classifier Action
/// (ai-advanced-capabilities-agreements-r1 task 020, FR-07 / design Lens 3d). Pins the closed output
/// contract, the Reasoning model tier (criterion #5), and the REGISTRY-DRIVEN prompt mechanism
/// (criterion #4) against both the Action mirror and its output-schema mirror.
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> —
/// <see cref="AgreementReviewOutputSchemaContractTests"/> validates the <c>agreement-review</c> output
/// mirror and <see cref="CatalogInputSchemaContractTests"/> validates every input mirror, but nothing
/// covers the NEW <c>agreement-classify</c> Action (its distinct closed contract, its Reasoning tier, or
/// its registry-driven prompt). (2) <i>Extension</i> — a narrowly-scoped class targeting exactly the one
/// new capability this task ships. (3) <i>Cost-of-doing-nothing</i>: an invalid classifier output schema
/// 400-fails the first live Structured Outputs call; a drifted closed contract breaks task 021's
/// orientation gate / task 023's sanity check (they pin {isAgreement, candidates, composite, reasoning});
/// and a hardcoded key set (placeholder removed / an enum added on subDomainKey) silently defeats the
/// type-agnostic promise. This is the CI-gated, offline-runnable arm of the ADR-039 output-schema eval
/// obligation for the classifier (live LLM grading is env-blocked — no Reasoning deployment yet).
/// </para>
/// </remarks>
public class AgreementClassifyActionContractTests
{
    private const string ActionFileName = "agreement-classify.action.json";
    private const string OutputSchemaFileName = "agreement-classify.schema.json";

    // The registry keys must NEVER be hardcoded in the classifier prompt (they come from the registry at
    // assembly time). These hyphenated/distinctive keys are a clean "hardcoded key list" smell test —
    // none is an ordinary English word the prompt would use in prose.
    private static readonly string[] RegistryKeysThatMustNotBeHardcoded =
        { "asset-purchase", "partnership", "loan", "vendor" };

    // ── Criterion #5: routes to the Reasoning tier (asserted via catalog config, not assumption). ──

    [Fact]
    public void Action_DeclaresReasoningModelTier()
    {
        var action = LoadAction();

        action.GetProperty("modelTier").GetString().Should().Be("Reasoning",
            "FR-07 / Lens 3d: classification is accuracy-first (a wrong route wastes a full review), so it " +
            "runs on the Reasoning tier — resolved to a deployment by ModelTierDeploymentResolver in ActionRunner " +
            "(ADR-016: the catalog stores tier INTENT, not a deployment name)");
    }

    // ── Criterion #4: the candidate key set is registry-driven (prompt placeholder), never hardcoded. ──

    [Fact]
    public void SystemPrompt_IsRegistryDriven_CarriesThePlaceholder_AndHardcodesNoKeySet()
    {
        var action = LoadAction();
        var systemPrompt = action.GetProperty("systemPrompt").GetString()!;

        systemPrompt.Should().Contain("{{agreementTypeRegistry}}",
            "the classifier prompt MUST carry the {{agreementTypeRegistry}} placeholder — the live sprk_agreementtype " +
            "rows are injected there at assembly time (AgreementTypeRegistryPromptAssembler), so a new registered type " +
            "extends the candidate set with zero code change");

        foreach (var key in RegistryKeysThatMustNotBeHardcoded)
        {
            systemPrompt.Should().NotContain(key,
                $"the registry key '{key}' must NOT be hardcoded in the prompt — hardcoding the key set is the defect " +
                "the registry-driven mechanism exists to prevent");
        }
    }

    [Fact]
    public void OutputSchema_SubDomainKey_IsAPlainString_WithNoEnum_BecauseTheValidSetIsPromptData()
    {
        var candidateItem = CandidateItemsSchema(LoadEmbeddedOutputSchema());

        var subDomainKey = candidateItem.GetProperty("properties").GetProperty("subDomainKey");
        subDomainKey.GetProperty("type").GetString().Should().Be("string");
        subDomainKey.TryGetProperty("enum", out _).Should().BeFalse(
            "subDomainKey must NOT be a schema enum — its valid set is the registry, supplied as PROMPT data " +
            "(a $choices/enum cannot ride the static-OutputSchemaJson ActionRunner path nor reach a nested array-item " +
            "field). Adding a schema enum here would re-hardcode the key set.");
    }

    // ── Closed output contract {isAgreement, candidates[{subDomainKey, confidence}], composite, reasoning}. ──

    [Fact]
    public void EmbeddedOutputSchema_DeclaresTheClosedTopLevelContract()
    {
        var schema = LoadEmbeddedOutputSchema();

        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "isAgreement", "candidates", "composite", "reasoning" },
                "the closed classifier contract is exactly these four fields (consumed by task 021 gate + task 023)");

        var props = schema.GetProperty("properties");
        props.GetProperty("isAgreement").GetProperty("type").GetString().Should().Be("boolean");
        props.GetProperty("candidates").GetProperty("type").GetString().Should().Be("array");
        props.GetProperty("composite").GetProperty("type").GetString().Should().Be("boolean");
        props.GetProperty("reasoning").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void EmbeddedOutputSchema_CandidateItems_CarryExactlySubDomainKeyAndConfidence()
    {
        var item = CandidateItemsSchema(LoadEmbeddedOutputSchema());

        item.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        item.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "subDomainKey", "confidence" },
                "each candidate is exactly {subDomainKey, confidence} — the per-candidate calibrated confidence the gate reads");
        item.GetProperty("properties").GetProperty("confidence").GetProperty("type").GetString().Should().Be("number");
    }

    // ── OpenAI Structured Outputs subset validity + the mirror matches the embedded schema. ──

    [Fact]
    public void OutputSchemaMirror_IsValidOpenAiStructuredOutputSchema()
    {
        var mirrorPath = OutputSchemaMirrorPath();
        File.Exists(mirrorPath).Should().BeTrue($"{OutputSchemaFileName}: the output-schema mirror is the repo-first authored source");

        using var doc = JsonDocument.Parse(File.ReadAllText(mirrorPath));
        var error = OpenAiFunctionSchemaValidator.FindFirstError(doc.RootElement.GetRawText());
        error.Should().BeNull(
            $"{OutputSchemaFileName}: an invalid authored classifier schema must fail CI here, not 400-fail the first " +
            $"live Structured Outputs call. Validator said: {error}");
    }

    [Fact]
    public void OutputSchemaMirror_NeverUsesPropertyLevelRequired()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(OutputSchemaMirrorPath()));
        FindPropertyLevelRequired(doc.RootElement, "$").Should().BeNull(
            $"{OutputSchemaFileName}: property-level boolean 'required' is banned — use the object-level required array");
    }

    [Fact]
    public void OutputSchemaMirror_StructurallyMatchesTheActionEmbeddedSchema()
    {
        var embedded = Canonicalize(LoadEmbeddedOutputSchema());
        using var mirrorDoc = JsonDocument.Parse(File.ReadAllText(OutputSchemaMirrorPath()));
        var mirror = Canonicalize(mirrorDoc.RootElement);

        // The mirror carries $schema/$id/title/description metadata the embedded copy omits; compare the
        // load-bearing schema body (type/additionalProperties/required/properties) which BOTH must share.
        embedded["required"].Should().Be(mirror["required"],
            "the output mirror and the Action's embedded outputSchema must declare the same closed required set");
        embedded["propertyNames"].Should().Be(mirror["propertyNames"],
            "the output mirror and the Action's embedded outputSchema must declare the same top-level properties");
    }

    // ── Helpers ──

    private static JsonElement CandidateItemsSchema(JsonElement outputSchema)
        => outputSchema.GetProperty("properties").GetProperty("candidates").GetProperty("items");

    /// <summary>Extracts a small canonical fingerprint of a schema's closed contract for cross-file comparison.</summary>
    private static Dictionary<string, string> Canonicalize(JsonElement schema)
    {
        var required = string.Join(",", schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).OrderBy(x => x));
        var propertyNames = string.Join(",", schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        return new Dictionary<string, string> { ["required"] = required, ["propertyNames"] = propertyNames };
    }

    private static string? FindPropertyLevelRequired(JsonElement schema, string path)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return null;

        if (schema.TryGetProperty("required", out var req) && req.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return $"{path}.required";

        if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            foreach (var prop in props.EnumerateObject())
                if (FindPropertyLevelRequired(prop.Value, $"{path}.properties.{prop.Name}") is { } hit)
                    return hit;

        if (schema.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
            if (FindPropertyLevelRequired(items, $"{path}.items") is { } hit)
                return hit;

        return null;
    }

    private static JsonElement LoadAction()
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "dataverse", "actions", ActionFileName);
        File.Exists(path).Should().BeTrue($"{ActionFileName}: the classifier Action mirror must exist");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static JsonElement LoadEmbeddedOutputSchema() => LoadAction().GetProperty("outputSchema");

    private static string OutputSchemaMirrorPath()
        => Path.Combine(FindRepoRoot(), "infra", "dataverse", "outputschemas", OutputSchemaFileName);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }
}
