using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract tests for the cataloged create-matter capability
/// (spaarke-ai-architecture-redesign-r2 task 042, FR-A1-13 / R19).
/// </summary>
/// <remarks>
/// <para>
/// create-matter is authored EXACTLY like the shipped create-task capability (a Binding +
/// prompted Action riding the EXISTING <c>dataverse.create_record</c> handler — no new
/// handler, no new dispatch path, per CLAUDE.md §11 / ADR-039). Because the underlying write
/// mechanism (gate + handler + OutcomeCard record-link composition) is already proven
/// table-agnostic and already regression-tested against <c>sprk_matter</c> specifically (see
/// <c>TypedHandlerResumeExecutorTests.ExecuteAsync_HandlerEmitsUserSummaryAndRecord_OutcomeCarriesBothAndComposesMdaLink</c>
/// and <c>DataverseCreateRecordHandlerTests</c>'s sprk_matter write-contract assertions), this
/// class protects the NEW surface this task actually adds: the authored catalog artifacts
/// (JPS Action, input/output schema mirrors) and the staged Binding row shape, kept consistent
/// with the create-task pattern it mirrors.
/// </para>
/// <para>
/// <b>Deferred live seeding</b>: the create-matter Binding row is deliberately NOT yet in
/// <c>infra/dataverse/sprk_playbookconsumer-rows.json</c> — that mirror's own header states
/// "Direction of truth: table -> mirror... do NOT hand-author routing here that does not exist
/// in a live table". The ready-to-seed row lives at
/// <c>projects/spaarke-ai-architecture-redesign-r2/notes/jps/create-matter-binding-row-pending-seed.json</c>
/// instead (see that file's <c>deploySequence</c>). This test asserts BOTH halves of that
/// decision: the staged row is well-formed and create-task-consistent, AND the live mirror does
/// not yet carry it (a loud reminder to update this test at the deploy step, per task 020's
/// precedent of deferring live catalog changes that would trip
/// <c>RoutingConsumerTypeHealthCheck</c>).
/// </para>
/// </remarks>
public class CreateMatterCapabilityContractTests
{
    private const string ActionCode = "CREATE-MATTER@v1";

    // ─────────────────────────────────────────────────────────────────────────
    // Input schema mirror (infra/dataverse/inputschemas/create-matter-v1.input.schema.json)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InputSchemaMirror_DeclaresExpectedActionCodeAndOptionalFields()
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "inputschemas", "create-matter-v1.input.schema.json");
        File.Exists(path).Should().BeTrue("the input-schema mirror is the repo-first authored source (author here, CI-validate, then write to Dataverse)");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.GetProperty("actionCode").GetString().Should().Be(ActionCode);
        doc.RootElement.GetProperty("environment").GetString().Should().Be("spaarkedev1");

        var schema = doc.RootElement.GetProperty("inputSchema");
        var error = OpenAiFunctionSchemaValidator.FindFirstError(schema.GetRawText());
        error.Should().BeNull($"the authored create-matter input schema must be valid OpenAI function parameters. Validator said: {error}");

        var properties = schema.GetProperty("properties");
        properties.TryGetProperty("fileIds", out _).Should().BeTrue("mirrors create-task's optional fileIds grounding slot");
        properties.TryGetProperty("practice_area", out _).Should().BeTrue("conversational practice-area slot, resolved to sprk_practicearea_ref at write time");
        properties.TryGetProperty("matter_type", out _).Should().BeTrue("conversational matter-type slot, resolved to sprk_mattertype_ref at write time");

        // R19 minimal shape: no field is REQUIRED — the capability never forces a loop-native
        // elicitation turn (unlike create-task's due_date/assign_to); the model may still ask
        // conversationally per the Binding's tool description.
        if (schema.TryGetProperty("required", out var required))
        {
            required.GetArrayLength().Should().Be(0,
                "create-matter deliberately declares no forced-elicitation required fields (R19 minimal shape)");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Output schema mirror (infra/dataverse/outputschemas/create-matter-v1.schema.json)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OutputSchemaMirror_DeclaresFieldsInStreamingOrder_AndCitedRefsRequiresAtLeastOne()
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "outputschemas", "create-matter-v1.schema.json");
        File.Exists(path).Should().BeTrue();

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            "structured output must reject invented fields (ADR-039 grounded outputs)");

        var required = doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        required.Should().Equal(
            new[] { "matter_name", "matter_description", "practice_area_suggestion", "matter_type_suggestion", "cited_refs" },
            "declaration order pins the streaming order the assistant renders");

        var citedRefs = doc.RootElement.GetProperty("properties").GetProperty("cited_refs");
        citedRefs.GetProperty("minItems").GetInt32().Should().Be(1,
            "an uncited matter proposal is invalid (ADR-039 grounded outputs) — mirrors create-task's cited_refs contract");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JPS Action definition (notes/jps/CREATE-MATTER-v1.jps.json)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void JpsActionDefinition_DeclaresFiveOutputFieldsMatchingTheOutputSchema()
    {
        var jpsPath = Path.Combine(RepoRoot(), "projects", "spaarke-ai-architecture-redesign-r2", "notes", "jps", "CREATE-MATTER-v1.jps.json");
        var schemaPath = Path.Combine(RepoRoot(), "infra", "dataverse", "outputschemas", "create-matter-v1.schema.json");
        File.Exists(jpsPath).Should().BeTrue();

        using var jps = JsonDocument.Parse(File.ReadAllText(jpsPath));
        jps.RootElement.GetProperty("$schema").GetString().Should().Be("https://spaarke.com/schemas/prompt/v1");
        jps.RootElement.GetProperty("instruction").GetProperty("role").GetString().Should().NotBeNullOrWhiteSpace();
        jps.RootElement.GetProperty("instruction").GetProperty("task").GetString().Should().NotBeNullOrWhiteSpace();
        jps.RootElement.GetProperty("output").GetProperty("structuredOutput").GetBoolean().Should().BeTrue();
        jps.RootElement.GetProperty("examples").GetArrayLength().Should().BeGreaterThan(0,
            "at least one worked example — matches every other shipped Action's JPS");

        var jpsFieldNames = jps.RootElement.GetProperty("output").GetProperty("fields")
            .EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToArray();

        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var schemaFieldNames = schema.RootElement.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).ToArray();

        jpsFieldNames.Should().Equal(schemaFieldNames,
            "the JPS output.fields declaration order must match the output-schema mirror's required array " +
            "(both encode the SAME streaming contract — divergence here is the exact class of drift task 020's " +
            "triple-twin hoist protects against for tool descriptions)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Staged Binding row — create-task-consistent shape, NOT YET in the live mirror
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StagedBindingRow_MirrorsCreateTaskConventions_ForDisposition_Risk_AndCaptureMode()
    {
        var stagedPath = Path.Combine(RepoRoot(), "projects", "spaarke-ai-architecture-redesign-r2", "notes", "jps", "create-matter-binding-row-pending-seed.json");
        File.Exists(stagedPath).Should().BeTrue();

        using var staged = JsonDocument.Parse(File.ReadAllText(stagedPath));
        var row = staged.RootElement.GetProperty("row");
        row.GetProperty("consumerType").GetString().Should().Be("create-matter");
        row.GetProperty("actionCode").GetString().Should().Be(ActionCode);
        row.GetProperty("enabled").GetBoolean().Should().BeTrue();

        // Read the LIVE create-task row from the governed mirror and assert the staged
        // create-matter row follows the SAME disposition/risk/captureMode/surfaces
        // conventions — a drafting-only prompted Action bridging to a gated write.
        var mirrorPath = Path.Combine(RepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json");
        using var mirror = JsonDocument.Parse(File.ReadAllText(mirrorPath));
        var createTaskRow = mirror.RootElement.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("consumerType").GetString() == "create-task"
                         && r.GetProperty("consumerCode").GetString() == "default");

        row.GetProperty("disposition").GetInt32().Should().Be(createTaskRow.GetProperty("disposition").GetInt32(),
            "create-matter drafts informationally, same as create-task, before the gated write executes");
        row.GetProperty("risk").GetInt32().Should().Be(createTaskRow.GetProperty("risk").GetInt32(),
            "the Binding-level risk posture complements (never replaces) the tool-level side_effect_class gate — same as create-task");
        row.GetProperty("captureMode").GetInt32().Should().Be(createTaskRow.GetProperty("captureMode").GetInt32(),
            "loop-native elicitation, same as create-task");
        row.GetProperty("surfaces").GetString().Should().Be(createTaskRow.GetProperty("surfaces").GetString());
    }

    [Fact]
    public void LiveBindingMirror_DoesNotYetContainCreateMatter()
    {
        // Documents the deliberate defer-live-seed decision (task 020 precedent): adding this
        // row to the LIVE mirror ahead of the live sprk_analysisaction row would misrepresent
        // it as already-seeded and would report MISSING IN ENV drift on the next
        // Seed-PlaybookConsumers.ps1 -DiffOnly run. When the deploy step (gate G-R2-A / task
        // 049) lands the live row and refreshes this mirror via -Export, THIS test should be
        // updated/removed in the SAME PR — its failure is the intended signal that the staged
        // artifact graduated to live.
        var mirrorPath = Path.Combine(RepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json");
        using var mirror = JsonDocument.Parse(File.ReadAllText(mirrorPath));

        mirror.RootElement.GetProperty("rows").EnumerateArray()
            .Any(r => r.GetProperty("consumerType").GetString() == "create-matter")
            .Should().BeFalse(
                "create-matter is staged (not yet live) — see create-matter-binding-row-pending-seed.json's deploySequence");
    }

    private static string RepoRoot()
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
            "this contract test requires an in-repo test run.");
    }
}
