using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract tests for the five R2 Compose catalog rows' OutputSchemaJson
/// mirrors (spaarkeai-compose-r2 tasks 040-044, FR-07..FR-11; eval-gated by task 045
/// per FR-12/NFR-09).
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> —
/// <see cref="CatalogInputSchemaContractTests"/> already validates every mirror under
/// <c>infra/dataverse/inputschemas/</c> against <see cref="OpenAiFunctionSchemaValidator"/>
/// via a directory-wide Theory, but NOTHING validates <c>infra/dataverse/outputschemas/</c>
/// the same way — the H1 UAT incident (property-level boolean <c>required</c>) applies
/// equally to Structured Outputs schemas, and the five compose OutputSchemaJson files
/// explicitly declare "$comment-required" banning the same shape. (2) <i>Extension</i> —
/// this class targets exactly the five NEW output schemas this task's acceptance
/// criteria name (NFR-09), rather than widening <c>CatalogInputSchemaContractTests</c>'
/// directory Theory to a sibling directory it was never scoped to cover. (3)
/// <i>Cost-of-doing-nothing</i>: an invalid OutputSchemaJson 400-fails the Structured
/// Outputs request for the SPECIFIC compose row at first live invocation (the exact
/// class of incident <see cref="OpenAiFunctionSchemaValidator"/> exists to catch before
/// deploy, per its own remarks).
/// </para>
/// </remarks>
public class ComposeR2OutputSchemaContractTests
{
    public static TheoryData<string> ComposeOutputSchemaFileNames() => new()
    {
        "compose-explain-clause.schema.json",
        "compose-compare-to-playbook.schema.json",
        "compose-draft-alternative.schema.json",
        "compose-summarize-word-changes.schema.json",
        "compose-defined-terms.schema.json",
    };

    [Theory]
    [MemberData(nameof(ComposeOutputSchemaFileNames))]
    public void OutputSchemaMirror_IsValidOpenAiStructuredOutputSchema(string fileName)
    {
        var path = Path.Combine(OutputSchemaDirectory(), fileName);
        File.Exists(path).Should().BeTrue($"{fileName}: the output-schema mirror is the repo-first authored source for OutputSchemaJson");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var error = OpenAiFunctionSchemaValidator.FindFirstError(doc.RootElement.GetRawText());
        error.Should().BeNull(
            $"{fileName}: an invalid authored OutputSchemaJson must fail CI here, not 400-fail the first live " +
            $"Structured Outputs call for this row (H1 incident class, NFR-09). Validator said: {error}");
    }

    [Theory]
    [MemberData(nameof(ComposeOutputSchemaFileNames))]
    public void OutputSchemaMirror_NeverUsesPropertyLevelRequired(string fileName)
    {
        var path = Path.Combine(OutputSchemaDirectory(), fileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        FindPropertyLevelRequired(doc.RootElement, "$").Should().BeNull(
            $"{fileName}: property-level boolean 'required' is banned (OpenAiFunctionSchemaValidator) — " +
            "use the object-level required array instead");
    }

    [Theory]
    [MemberData(nameof(ComposeOutputSchemaFileNames))]
    public void OutputSchemaMirror_DeclaresObjectLevelRequiredArray_AndRejectsAdditionalProperties(string fileName)
    {
        var path = Path.Combine(OutputSchemaDirectory(), fileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        doc.RootElement.GetProperty("type").GetString().Should().Be("object",
            $"{fileName}: Structured Outputs root schema must be an object envelope");
        doc.RootElement.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            $"{fileName}: invented fields must be rejected (ADR-039 grounded outputs)");
        doc.RootElement.TryGetProperty("required", out var required).Should().BeTrue(
            $"{fileName}: an object-level required array must declare which fields are mandatory");
        required.ValueKind.Should().Be(JsonValueKind.Array,
            $"{fileName}: required must be an array of property names, never a boolean");
        required.GetArrayLength().Should().BeGreaterThan(0,
            $"{fileName}: at least one field must be declared required at the object level");
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

    private static string OutputSchemaDirectory() =>
        Path.Combine(FindRepoRoot(), "infra", "dataverse", "outputschemas");

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
