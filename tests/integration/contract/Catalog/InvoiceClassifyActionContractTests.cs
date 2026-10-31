using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract test for the <c>invoice-classify</c> Action (INVOICE-CLASSIFY@v1; ebilling-r1 task 042,
/// FR-46, D-52). Pins the Action mirror (<c>infra/dataverse/actions/invoice-classify.action.json</c>), its
/// output-schema sidecar and its Binding row against the facade contract (<see cref="InvoiceClassifyResult"/> and
/// the parser <c>IInvoiceAi.ClassifyAsync</c> runs), so the Action cannot drift from the code that reads it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — <c>InvoiceAiContractTests</c> pins the
/// parser against inline JSON and <see cref="CatalogInputSchemaContractTests"/> validates every input mirror, but
/// nothing reads the authored <c>invoice-classify</c> Action or its output schema. (2) <i>Extension</i> — a
/// narrowly-scoped class for the one Action this task ships, mirroring
/// <see cref="AgreementClassifyActionContractTests"/>. (3) <i>Cost of doing nothing</i> — an invalid output schema
/// 400-fails the first live Structured Outputs call; a schema whose required set or enum drifts from the DTO makes
/// every classification <c>output_invalid</c> and poisons every attachment job; a missing Binding row makes the
/// facade <c>binding_missing</c>, and a Binding with no <c>ConsumerTypes.All</c> entry is not seen by the health check.
/// </para>
/// </remarks>
public class InvoiceClassifyActionContractTests
{
    private const string Code = "invoice-classify";
    private const string ActionFileName = "invoice-classify.action.json";
    private const string OutputSchemaFileName = "invoice-classify.schema.json";

    private static readonly string[] HintMembers =
        ["vendorName", "invoiceNumber", "invoiceDate", "totalAmount", "currency", "matterReference"];

    // ── Tier and identity ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Action_DeclaresTheFastTierAndTheInvoiceClassifyCode_AndNamesNoDeployment()
    {
        var action = LoadAction();

        action.GetProperty("actionCode").GetString().Should().Be(Code);
        action.GetProperty("description").GetString().Should().Contain("INVOICE-CLASSIFY@v1");
        action.GetProperty("modelTier").GetString().Should().Be("Fast",
            "the replaced FinanceOptions.ClassificationDeploymentName was gpt-4o-mini; the catalog stores tier INTENT (ADR-016)");
        action.EnumerateObject().Select(p => p.Name).Should().NotContain(["deploymentName", "deployment", "model"],
            "a model deployment name is configuration (ModelTierDeploymentResolver), never an Action column");
        action.GetProperty("instruction").GetRawText().ToLowerInvariant().Should().NotContain("gpt-",
            "the prompt must not name a model deployment");
    }

    [Fact]
    public void ConsumerTypesKey_MatchesTheActionCode_AndIsListedInAll()
    {
        ConsumerTypes.InvoiceClassify.Should().Be(Code);
        ConsumerTypes.All.Should().Contain(Code, "the catalog health check (RoutingConsumerTypeHealthCheck) iterates All");
    }

    // ── The authored prompt ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Action_IsAJpsPrompt_WithRoleTaskInputAndStructuredOutput()
    {
        var action = LoadAction();

        action.GetProperty("$schema").GetString().Should().Be("https://spaarke.com/schemas/prompt/v1");
        var instruction = action.GetProperty("instruction");
        instruction.GetProperty("role").GetString().Should().NotBeNullOrWhiteSpace();
        instruction.GetProperty("task").GetString().Should().Contain("documentText",
            "the operand the facade sends is named documentText");
        action.GetProperty("input").TryGetProperty("documentText", out _).Should().BeTrue();
        action.GetProperty("output").GetProperty("structuredOutput").GetBoolean().Should().BeTrue();
        action.GetProperty("output").GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .Should().Equal("classification", "confidence", "hints", "reasoning");
    }

    [Fact]
    public void Examples_ParseThroughTheFacadeParser_AndCoverAllThreeVerdicts()
    {
        var action = LoadAction();
        var verdicts = new List<InvoiceClassification>();

        foreach (var example in action.GetProperty("examples").EnumerateArray())
        {
            var parsed = InvoiceAiOutputParser.ParseClassify(example.GetProperty("output"));
            parsed.Confidence.Should().BeInRange(0m, 1m);
            verdicts.Add(parsed.Classification);
        }

        verdicts.Should().BeEquivalentTo(Enum.GetValues<InvoiceClassification>(),
            "the examples show the model each verdict, including NotInvoice with null hints");
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
        Names(schema.GetProperty("required")).Should().BeEquivalentTo(["classification", "confidence", "hints", "reasoning"],
            "OpenAI strict mode requires every property in required; optionality is a nullable type");
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["classification", "confidence", "hints", "reasoning"]);

        Names(schema.GetProperty("properties").GetProperty("classification").GetProperty("enum"))
            .Should().BeEquivalentTo(Enum.GetNames<InvoiceClassification>(),
                "the enum values are InvoiceClassification names verbatim (the parser matches by name)");

        var hints = schema.GetProperty("properties").GetProperty("hints");
        Names(hints.GetProperty("type")).Should().BeEquivalentTo(["object", "null"], "NotInvoice returns null hints");
        hints.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        Names(hints.GetProperty("required")).Should().BeEquivalentTo(HintMembers);
        hints.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(HintMembers);
        foreach (var member in HintMembers)
        {
            Names(hints.GetProperty("properties").GetProperty(member).GetProperty("type")).Should().Contain("null",
                $"hints.{member} is best-effort: null when not determinable");
        }

        // The DTO members are the hint names, Pascal-cased (InvoiceClassifyHints).
        typeof(InvoiceClassifyHints).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .Should().BeEquivalentTo(HintMembers);
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
