using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract test for the <c>invoice-line-code</c> Action (INVOICE-LINE-CODE@v1; ebilling-r1 task 047, FR-22,
/// D-46, D-52). Pins the Action mirror (<c>infra/dataverse/actions/invoice-line-code.action.json</c>), its output-schema
/// and input-schema sidecars and its Binding row against the facade contract (<see cref="InvoiceCodeLinesRequest"/>,
/// <see cref="InvoiceLineCoding"/> and the parser <c>IInvoiceAi.CodeLinesAsync</c> runs), so the Action cannot drift from
/// the code that reads and feeds it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — <c>InvoiceAiContractTests</c> pins the parser
/// against inline JSON and <see cref="CatalogInputSchemaContractTests"/> validates every input mirror, but nothing reads
/// the authored <c>invoice-line-code</c> Action or its output schema. (2) <i>Extension</i> — a narrowly-scoped class for
/// the one Action this task ships, mirroring <see cref="InvoiceClassifyActionContractTests"/>. (3) <i>Cost of doing
/// nothing</i> — an invalid output schema 400-fails the first live Structured Outputs call; a schema whose required set
/// or enum drifts from the DTO makes every chunk <c>output_invalid</c> and fails every invoice's coding step; an input
/// mirror whose operand names differ from what the facade sends documents a prompt the model never receives; a missing
/// Binding row makes the facade <c>binding_missing</c>, and a Binding with no <c>ConsumerTypes.All</c> entry is not seen
/// by the health check.
/// </para>
/// </remarks>
public class InvoiceLineCodeActionContractTests
{
    private const string Code = "invoice-line-code";
    private const string ActionFileName = "invoice-line-code.action.json";
    private const string OutputSchemaFileName = "invoice-line-code.schema.json";
    private const string InputSchemaFileName = "invoice-line-code.input.schema.json";

    private static readonly string[] LineMembers =
        ["lineKey", "taskCode", "activityCode", "expenseCode", "confidence", "observations"];

    private static readonly string[] ObservationMembers = ["type", "confidence", "quotedText"];

    // ── Tier and identity ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Action_DeclaresTheFastTierAndTheLineCodeCode_AndNamesNoDeployment()
    {
        var action = LoadAction();

        action.GetProperty("actionCode").GetString().Should().Be(Code);
        action.GetProperty("description").GetString().Should().Contain("INVOICE-LINE-CODE@v1");
        action.GetProperty("modelTier").GetString().Should().Be("Fast",
            "D-51 wants a small, cheap model; the catalog stores tier INTENT (ADR-016) and the platform maps Fast to a deployment");
        action.EnumerateObject().Select(p => p.Name).Should().NotContain(["deploymentName", "deployment", "model"],
            "a model deployment name is configuration (ModelTierDeploymentResolver), never an Action column");
        action.GetProperty("instruction").GetRawText().ToLowerInvariant().Should().NotContain("gpt-",
            "the prompt must not name a model deployment");
        action.GetProperty("temperature").GetDecimal().Should().Be(0m, "coding must be repeatable");
    }

    [Fact]
    public void ConsumerTypesKey_MatchesTheActionCode_IsListedInAll_AndIsTheServicesActionCode()
    {
        ConsumerTypes.InvoiceLineCode.Should().Be(Code);
        ConsumerTypes.All.Should().Contain(Code, "the catalog health check (RoutingConsumerTypeHealthCheck) iterates All");
        LineCodingService.ActionCode.Should().Be(Code);
        LineCodingService.ActionVersion.Should().Be(1, "INVOICE-LINE-CODE@v1");
    }

    // ── The authored prompt ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Action_IsAJpsPrompt_WithClosedCodeListConstraints_AndStructuredOutput()
    {
        var action = LoadAction();

        action.GetProperty("$schema").GetString().Should().Be("https://spaarke.com/schemas/prompt/v1");
        var instruction = action.GetProperty("instruction");
        instruction.GetProperty("role").GetString().Should().NotBeNullOrWhiteSpace();

        var task = instruction.GetProperty("task").GetString()!;
        task.Should().Contain("allowedTaskCodes").And.Contain("allowedActivityCodes").And.Contain("allowedExpenseCodes",
            "the operand names the facade sends");

        var constraints = string.Join('\n', instruction.GetProperty("constraints").EnumerateArray().Select(c => c.GetString()));
        constraints.Should().Contain("closed", "FR-22: the model picks only from the supplied lists");
        constraints.Should().Contain("BlockBilling").And.Contain("VagueDescription").And.Contain("quotedText");
        constraints.Should().Contain("never instructions", "the narrative is third-party text (prompt-injection guard)");

        action.GetProperty("output").GetProperty("structuredOutput").GetBoolean().Should().BeTrue();
        action.GetProperty("output").GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .Should().Equal("lines");
    }

    [Fact]
    public void Examples_ParseThroughTheFacadeParser_AndShowBothObservationTypesAndANullCode()
    {
        var action = LoadAction();
        var types = new HashSet<InvoiceLineObservationType>();
        var sawNullTask = false;
        var sawExpenseCode = false;

        foreach (var example in action.GetProperty("examples").EnumerateArray())
        {
            var parsed = InvoiceAiOutputParser.ParseCodeLines(example.GetProperty("output"));
            parsed.Lines.Should().NotBeEmpty();
            foreach (var line in parsed.Lines)
            {
                line.Confidence.Should().BeInRange(0m, 1m);
                sawNullTask |= line.TaskCode is null;
                sawExpenseCode |= line.ExpenseCode is not null;
                foreach (var observation in line.Observations)
                {
                    types.Add(observation.Type);
                }
            }
        }

        types.Should().BeEquivalentTo(Enum.GetValues<InvoiceLineObservationType>(),
            "the examples show the model each observation type");
        sawNullTask.Should().BeTrue("the examples show that null is a legitimate answer when no code fits");
        sawExpenseCode.Should().BeTrue("the examples show an Expense line carrying an expense code");
    }

    // ── The output schema sidecar ───────────────────────────────────────────────────────────────

    [Fact]
    public void OutputSchema_IsValidOpenAiStructuredOutputSchema_WithoutPropertyLevelRequired()
    {
        var path = OutputSchemaPath();
        File.Exists(path).Should().BeTrue($"{OutputSchemaFileName}: the output-schema mirror is the repo-first authored source");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var error = OpenAiFunctionSchemaValidator.FindFirstError(doc.RootElement.GetRawText());
        error.Should().BeNull($"an invalid schema would 400-fail the first live call. Validator said: {error}");
        FindPropertyLevelRequired(doc.RootElement, "$").Should().BeNull("use the object-level required array");
    }

    [Fact]
    public void OutputSchema_MatchesTheFacadeResultContract_FieldForField()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(OutputSchemaPath()));
        var schema = doc.RootElement;

        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        Names(schema.GetProperty("required")).Should().BeEquivalentTo(["lines"]);

        var line = schema.GetProperty("properties").GetProperty("lines").GetProperty("items");
        line.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        Names(line.GetProperty("required")).Should().BeEquivalentTo(LineMembers,
            "OpenAI strict mode requires every property in required; optionality is a nullable type");
        line.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(LineMembers);

        foreach (var member in new[] { "taskCode", "activityCode", "expenseCode" })
        {
            var codeSchema = line.GetProperty("properties").GetProperty(member);
            Names(codeSchema.GetProperty("type")).Should().BeEquivalentTo(["string", "null"],
                $"{member} is null when no allowed code fits");
            codeSchema.TryGetProperty("enum", out _).Should().BeFalse(
                "the allowed codes differ per invoice (the resolved code set); a static enum would be stale and unenforceable. "
                + "LineCodingGuard is the control");
        }

        var observation = line.GetProperty("properties").GetProperty("observations").GetProperty("items");
        observation.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        Names(observation.GetProperty("required")).Should().BeEquivalentTo(ObservationMembers);
        observation.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(ObservationMembers);
        Names(observation.GetProperty("properties").GetProperty("type").GetProperty("enum"))
            .Should().BeEquivalentTo(Enum.GetNames<InvoiceLineObservationType>(),
                "the enum values are InvoiceLineObservationType names verbatim (the parser matches by name)");

        // The DTO members are the schema names, Pascal-cased.
        typeof(InvoiceLineCoding).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .Should().BeEquivalentTo(LineMembers);
        typeof(InvoiceLineObservation).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .Should().BeEquivalentTo(ObservationMembers);
    }

    // ── The input schema sidecar ────────────────────────────────────────────────────────────────

    [Fact]
    public void InputSchema_NamesTheOperandsTheFacadeSends_AndTheLineMembersOfTheRequestDto()
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "dataverse", "inputschemas", InputSchemaFileName);
        File.Exists(path).Should().BeTrue($"{InputSchemaFileName}: the input-schema mirror must exist");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.GetProperty("actionCode").GetString().Should().Be(Code);
        var schema = doc.RootElement.GetProperty("inputSchema");

        // The operand is { lines, allowedTaskCodes, allowedActivityCodes, allowedExpenseCodes } (InvoiceAi.CodeLinesAsync).
        var operandNames = new[] { "lines", "allowedTaskCodes", "allowedActivityCodes", "allowedExpenseCodes" };
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(operandNames);
        Names(schema.GetProperty("required")).Should().BeEquivalentTo(operandNames);
        typeof(InvoiceCodeLinesRequest).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .Should().BeEquivalentTo(["lines", "allowedTaskCodes", "allowedActivityCodes", "allowedExpenseCodes", "tenantId"],
                "the DTO's other member, tenantId, travels in the run context and is deliberately absent from the operand");

        var lineProps = schema.GetProperty("properties").GetProperty("lines").GetProperty("items").GetProperty("properties");
        lineProps.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            typeof(InvoiceLineToCode).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]),
            "the facade sends every InvoiceLineToCode member under its camelCase name");
        Names(lineProps.GetProperty("lineType").GetProperty("enum")).Should().BeEquivalentTo(Enum.GetNames<InvoiceLineType>());

        foreach (var list in operandNames.Skip(1))
        {
            schema.GetProperty("properties").GetProperty(list).GetProperty("items").GetProperty("properties")
                .EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
                    typeof(InvoiceCodeOption).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]),
                    "an allowed code is {code, description, guidance}");
        }
    }

    // ── The Binding row ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BindingMirror_HasAnEnabledGlobalRowRoutingTheConsumerTypeToTheActionCode()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json")));
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("consumerType").GetString() == Code)
            .ToList();

        var row = rows.Should().ContainSingle("exactly one Binding row for the consumer type").Subject;
        row.GetProperty("enabled").GetBoolean().Should().BeTrue();
        row.GetProperty("actionCode").GetString().Should().Be(Code);
        row.GetProperty("consumerCode").GetString().Should().Be("default");
        row.GetProperty("environment").GetString().Should().Be("*");
        row.GetProperty("toolDescription").ValueKind.Should().Be(JsonValueKind.Null,
            "a Binding with a tool description is projected into the chat loop; this Action is reachable only through the facade");
        File.Exists(Path.Combine(FindRepoRoot(), "infra", "dataverse", "actions", $"{row.GetProperty("actionCode").GetString()}.action.json"))
            .Should().BeTrue("a Binding routes to an Action that exists in the mirror");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<string?> Names(JsonElement array)
        => array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(e => e.GetString())
            : [array.GetString()];

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
        File.Exists(path).Should().BeTrue($"{ActionFileName}: the Action mirror must exist");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static string OutputSchemaPath()
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
