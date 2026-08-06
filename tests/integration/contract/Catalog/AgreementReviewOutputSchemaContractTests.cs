using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract test for the generalized <c>agreement-review</c> Action's OutputSchemaJson
/// mirror (<c>infra/dataverse/outputschemas/agreement-review.schema.json</c>), authored by
/// ai-advanced-capabilities-agreements-r1 task 002 (FR-05 output-schema split;
/// FR-06 generalization of nda-review).
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> —
/// <see cref="ComposeR2OutputSchemaContractTests"/> validates the five R2 Compose output-schema
/// mirrors, and <see cref="CatalogInputSchemaContractTests"/> validates every INPUT-schema mirror
/// via a directory-wide Theory, but NOTHING validates the <c>agreement-review</c> OUTPUT-schema
/// mirror (ComposeR2's list is hardcoded to the compose rows; agreement-review is not a compose row).
/// (2) <i>Extension</i> — a narrowly-scoped class targeting exactly the one output schema this task
/// changes, mirroring ComposeR2's own §11 reasoning for not widening a sibling test's list to a row
/// it was never scoped to cover. (3) <i>Cost-of-doing-nothing</i>: the FR-05 split
/// (flaggedClause/assessment) is consumed as a CLOSED contract by the memo (FR-13), Word export
/// (FR-15), and the FR-16 findings materializer; an invalid or drifted schema 400-fails the first
/// live Structured Outputs call (the exact incident class <see cref="OpenAiFunctionSchemaValidator"/>
/// exists to catch before deploy) OR silently reverts the split downstream consumers depend on. This
/// is the CI-gated, offline-runnable arm of the ADR-039 output-schema eval obligation for this task.
/// </para>
/// </remarks>
public class AgreementReviewOutputSchemaContractTests
{
    private const string SchemaFileName = "agreement-review.schema.json";

    [Fact]
    public void OutputSchemaMirror_IsValidOpenAiStructuredOutputSchema()
    {
        var path = SchemaPath();
        File.Exists(path).Should().BeTrue(
            $"{SchemaFileName}: the output-schema mirror is the repo-first authored source for OutputSchemaJson");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var error = OpenAiFunctionSchemaValidator.FindFirstError(doc.RootElement.GetRawText());
        error.Should().BeNull(
            $"{SchemaFileName}: an invalid authored OutputSchemaJson must fail CI here, not 400-fail the first live " +
            $"Structured Outputs call (ADR-039 output-schema eval obligation). Validator said: {error}");
    }

    [Fact]
    public void OutputSchemaMirror_NeverUsesPropertyLevelRequired()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath()));

        FindPropertyLevelRequired(doc.RootElement, "$").Should().BeNull(
            $"{SchemaFileName}: property-level boolean 'required' is banned (OpenAiFunctionSchemaValidator) — " +
            "use the object-level required array instead");
    }

    [Fact]
    public void OutputSchemaMirror_DeclaresObjectLevelRequiredArray_AndRejectsAdditionalProperties()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath()));
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("object",
            $"{SchemaFileName}: Structured Outputs root schema must be an object envelope");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            $"{SchemaFileName}: invented fields must be rejected (ADR-039 grounded outputs)");
        root.GetProperty("required").ValueKind.Should().Be(JsonValueKind.Array,
            $"{SchemaFileName}: required must be an array of property names, never a boolean");
        root.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "overallRisk", "flaggedSections" },
                $"{SchemaFileName}: the closed top-level contract is exactly {{overallRisk, flaggedSections}}");
    }

    [Fact]
    public void OutputSchemaMirror_FlaggedSectionItems_CarryTheFr05Split_AndDropExplanation()
    {
        // FR-05: the single 'explanation' blob is split into discrete flaggedClause (grounded fact)
        // + assessment (reasoned judgment). Every downstream consumer (memo/export/materializer)
        // depends on the CLOSED 6-field item contract; this pins it.
        using var doc = JsonDocument.Parse(File.ReadAllText(SchemaPath()));

        var items = doc.RootElement
            .GetProperty("properties").GetProperty("flaggedSections").GetProperty("items");

        items.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            "flaggedSections items reject invented fields");

        var required = items.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        required.Should().BeEquivalentTo(
            new[] { "sectionRef", "quotedText", "riskLevel", "flaggedClause", "assessment", "standardRef" },
            "the FR-05 closed item contract is exactly these 6 fields (flaggedClause + assessment replace explanation)");

        var propNames = items.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        propNames.Should().Contain("flaggedClause").And.Contain("assessment");
        propNames.Should().NotContain("explanation",
            "the legacy single 'explanation' blob is removed by the FR-05 split — no consumer string-parses markers");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string? FindPropertyLevelRequired(JsonElement schema, string path)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (schema.TryGetProperty("required", out var req) &&
            req.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return $"{path}.required";
        }

        if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
            {
                if (FindPropertyLevelRequired(prop.Value, $"{path}.properties.{prop.Name}") is { } hit)
                {
                    return hit;
                }
            }
        }

        if (schema.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            if (FindPropertyLevelRequired(items, $"{path}.items") is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    private static string SchemaPath() =>
        Path.Combine(FindRepoRoot(), "infra", "dataverse", "outputschemas", SchemaFileName);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repo root (Spaarke.sln) from AppContext.BaseDirectory — " +
            "the output-schema contract assertions require an in-repo test run.");
    }
}
