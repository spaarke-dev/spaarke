using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// G-P3 UAT round-1 H4 — the schema-validity regression net. Every input-schema
/// mirror seeded/authored in the repo (<c>infra/dataverse/inputschemas/*.json</c> —
/// the re-creation source for <c>sprk_analysisaction.sprk_inputschema</c> rows)
/// MUST pass the OpenAI function-parameters subset validator
/// (<see cref="OpenAiFunctionSchemaValidator"/>, the same validator the H1
/// projection guard runs) so an invalid authored schema fails CI BEFORE it
/// reaches an environment.
/// </summary>
/// <remarks>
/// Root incident (2026-07-07): the CREATE-TASK@v1 row was seeded with
/// property-level <c>"required": true</c> — invalid JSON Schema that Azure
/// OpenAI rejects with <c>invalid_function_parameters</c>, 400-failing EVERY
/// text-path turn on the environment. No repo-side check existed; the schema
/// only lived in a task-notes seed block. The mirrors + this contract test
/// close that gap: author the schema in <c>infra/dataverse/inputschemas/</c>
/// FIRST, let CI validate it, then write it to Dataverse.
/// </remarks>
public class CatalogInputSchemaContractTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Every repo-mirrored input schema is valid OpenAI function parameters
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> MirrorFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(MirrorDirectory(), "*.json"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Fact]
    public void InputSchemaMirrors_DirectoryExists_AndCoversTheSeededCatalog()
    {
        var files = Directory.EnumerateFiles(MirrorDirectory(), "*.json")
            .Select(Path.GetFileName)
            .ToList();

        files.Should().NotBeEmpty("the inputschemas mirror dir is the authoring surface for sprk_inputschema rows");
        files.Should().Contain(new[]
        {
            "create-task-v1.input.schema.json",
            "sum-chat-v1.input.schema.json",
            "cls-chat-v1.input.schema.json",
            "daily-briefing-v1.input.schema.json",
            "ref-chat-v1.input.schema.json",
            "draft-corr-v1.input.schema.json",
        }, "the six active spaarkedev1 rows carrying an input schema at the G-P3 catalog survey are all mirrored");
    }

    [Theory]
    [MemberData(nameof(MirrorFiles))]
    public void InputSchemaMirror_IsValidOpenAiFunctionParameters(string fileName)
    {
        var path = Path.Combine(MirrorDirectory(), fileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        doc.RootElement.TryGetProperty("actionCode", out var actionCode).Should().BeTrue(
            $"{fileName}: every mirror declares which sprk_analysisaction row it mirrors");
        actionCode.GetString().Should().NotBeNullOrWhiteSpace();

        doc.RootElement.TryGetProperty("inputSchema", out var schema).Should().BeTrue(
            $"{fileName}: the inputSchema member IS the sprk_inputschema row value");

        var error = OpenAiFunctionSchemaValidator.FindFirstError(schema.GetRawText());
        error.Should().BeNull(
            $"{fileName} ({actionCode.GetString()}): an invalid authored schema must fail CI here, " +
            "not 400-fail every text-path turn on the environment (G-P3 UAT round-1). " +
            $"Validator said: {error}");
    }

    [Theory]
    [MemberData(nameof(MirrorFiles))]
    public void InputSchemaMirror_NeverUsesPropertyLevelRequired(string fileName)
    {
        // Belt-and-braces beyond the validator: the exact G-P3 failure keyword shape is
        // banned in authored mirrors even where a future validator revision might
        // tolerate it. Property-level "required": true/false is NOT JSON Schema —
        // required-ness belongs in the OBJECT-level required array.
        var path = Path.Combine(MirrorDirectory(), fileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.TryGetProperty("inputSchema", out var schema).Should().BeTrue();

        FindPropertyLevelRequired(schema, "$").Should().BeNull(
            $"{fileName}: property-level boolean 'required' (the exact G-P3 UAT payload) is banned — " +
            "use the object-level required array");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The incident payload stays invalid (validator regression pin)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validator_ExactGp3UatPayload_IsRejected()
    {
        // Verbatim from the task-042 seed of sprk_analysisaction b66c8dda-8279-f111-ab0e-7ced8ddc4cc6.
        const string uatPayload =
            """{"type":"object","properties":{"fileIds":{"type":"array","items":{"type":"string"},"description":"Optional subset of session file ids the task should be grounded in. Omit to use all session files."},"due_date":{"type":"string","required":true,"elicitation_prompt":"What's the due date for this task?","description":"The task's due date as the user stated it (e.g. 7/9/2026)."},"assign_to":{"type":"string","required":true,"elicitation_prompt":"Should I assign it to you or someone else?","description":"Who the task is assigned to — 'me' or a person's name."}},"required":["due_date","assign_to"]}""";

        OpenAiFunctionSchemaValidator.FindFirstError(uatPayload).Should().NotBeNull(
            "the schema that 400-failed every G-P3 text-path turn must stay caught forever");
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

    private static string MirrorDirectory() =>
        Path.Combine(FindRepoRoot(), "infra", "dataverse", "inputschemas");

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
            "the input-schema contract assertions require an in-repo test run.");
    }
}
