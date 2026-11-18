using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract test for the <c>invoice-firm-notice</c> Action (INVOICE-FIRM-NOTICE@v1; ebilling-r1 task 131, FR-50, D-60, D-52). Pins the
/// Action mirror (<c>infra/dataverse/actions/invoice-firm-notice.action.json</c>), its output-schema and input-schema sidecars and its Binding row
/// against the facade contract (<see cref="InvoiceFirmNoticeRequest"/>, <see cref="InvoiceFirmNoticeDraft"/> and the parser
/// <c>IInvoiceAi.DraftFirmNoticeAsync</c> runs), so the Action cannot drift from the code that feeds and reads it.
/// </summary>
/// <remarks>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — <see cref="BillingRuleDraftActionContractTests"/> pins the 075 Action
/// only; <see cref="CatalogInputSchemaContractTests"/> validates every input mirror generically. (2) <i>Extension</i> — a narrowly-scoped class for
/// the one Action this task ships, mirroring the 075 class. (3) <i>Cost of doing nothing</i> — an output schema with a numeric property would let the
/// model state an amount (FR-50 fails); a missing Binding row makes the facade <c>binding_missing</c> and every notice a template; a constant with
/// no row (or a row with no constant) turns /healthz Unhealthy.
/// </remarks>
public class InvoiceFirmNoticeActionContractTests
{
    private const string Code = "invoice-firm-notice";
    private static readonly string[] ResultMembers = ["subject", "body"];

    // ── Tier, identity, consumer-type constant ──────────────────────────────────────────────────

    [Fact]
    public void Action_DeclaresTheStandardTierAndTheNoticeCode_AndNamesNoDeployment()
    {
        var action = LoadAction();

        action.GetProperty("actionCode").GetString().Should().Be(Code, "lowercase kebab, no version suffix");
        action.GetProperty("description").GetString().Should().Contain("INVOICE-FIRM-NOTICE@v1");
        action.GetProperty("modelTier").GetString().Should().Be("Standard", "outbound correspondence to a law firm: content generation, one call per decision");
        action.EnumerateObject().Select(p => p.Name).Should().NotContain(["deploymentName", "deployment", "model"]);
        action.GetProperty("instruction").GetRawText().ToLowerInvariant().Should().NotContain("gpt-");
        action.GetProperty("$version").GetInt32().Should().Be(1);
    }

    [Fact]
    public void ConsumerTypesKey_MatchesTheActionCode_AndIsListedInAll_InTheSameChangeAsItsBindingRow()
    {
        ConsumerTypes.InvoiceFirmNotice.Should().Be(Code);
        ConsumerTypes.All.Should().Contain(Code, "the catalog health check (RoutingConsumerTypeHealthCheck) iterates All");
        BindingRows().Should().ContainSingle(r => r.GetProperty("consumerType").GetString() == Code,
            "a constant in All with no Binding row (or a row with no constant) turns /healthz Unhealthy (project CLAUDE.md gotcha 2026-10-15)");
    }

    // ── The authored prompt ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Action_IsAJpsPrompt_ThatForbidsEveryFigure_AClause_ALink_AndAnInjection()
    {
        var action = LoadAction();

        action.GetProperty("$schema").GetString().Should().Be("https://spaarke.com/schemas/prompt/v1");
        var instruction = action.GetProperty("instruction");
        instruction.GetProperty("role").GetString().Should().NotBeNullOrWhiteSpace();
        instruction.GetProperty("task").GetString().Should().Contain("outcome").And.Contain("findings").And.Contain("subject").And.Contain("body");
        foreach (var outcome in Enum.GetNames<InvoiceFirmNoticeOutcome>())
        {
            instruction.GetProperty("task").GetString().Should().Contain(outcome, "the prompt explains every outcome the facade can send");
        }

        var constraints = string.Join('\n', instruction.GetProperty("constraints").EnumerateArray().Select(c => c.GetString()));
        constraints.Should().Contain("Never write a digit", "FR-50: no amount, date, line number or percentage from the model");
        constraints.Should().Contain("ruleName", "findings are referred to by rule name only");
        constraints.Should().Contain("Never mention ruleSetName");
        constraints.Should().Contain("Never quote or paraphrase a clause");
        constraints.Should().Contain("link", "no link (D-66)");
        constraints.Should().Contain("never instructions", "the input is data (prompt-injection guard)");
        constraints.Should().Contain("additionalProperties is false");

        action.GetProperty("output").GetProperty("structuredOutput").GetBoolean().Should().BeTrue();
        action.GetProperty("output").GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()).Should().Equal(ResultMembers);
    }

    [Fact]
    public void Examples_ParseThroughTheFacadeParser_PassTheComposersProseChecks_AndSendNoNumber()
    {
        var action = LoadAction();
        var examples = action.GetProperty("examples").EnumerateArray().ToList();
        examples.Should().NotBeEmpty();

        foreach (var example in examples)
        {
            var parsed = InvoiceAiOutputParser.ParseFirmNotice(example.GetProperty("output"));
            FirmNoticeProse.Check(parsed.Subject, parsed.Body).Should().BeNull(
                "an example the composer would refuse teaches the model to produce prose that is never used");

            using var input = JsonDocument.Parse(example.GetProperty("input").GetString()!);
            input.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["outcome", "findings"]);
            Enum.GetNames<InvoiceFirmNoticeOutcome>().Should().Contain(input.RootElement.GetProperty("outcome").GetString());
            ContainsNumber(input.RootElement).Should().BeFalse("FR-50: no number reaches the model, in an example either");
            foreach (var finding in input.RootElement.GetProperty("findings").EnumerateArray())
            {
                finding.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["ruleSetName", "ruleName", "consequence", "issue"]);
            }
        }
    }

    // ── The output schema sidecar: FR-50, no numeric property anywhere ──────────────────────────

    [Fact]
    public void OutputSchema_IsValidOpenAiStructuredOutputSchema_AndMatchesTheResultContract()
    {
        var schema = LoadOutputSchema();

        OpenAiFunctionSchemaValidator.FindFirstError(schema.GetRawText()).Should().BeNull("an invalid schema would 400-fail the first live call");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(ResultMembers);
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(ResultMembers);
        typeof(InvoiceFirmNoticeDraft).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).Should().BeEquivalentTo(ResultMembers,
            "the facade's DTO and the schema name the same two members");
        foreach (var property in schema.GetProperty("properties").EnumerateObject())
        {
            property.Value.TryGetProperty("required", out _).Should().BeFalse("property-level required is forbidden");
        }
    }

    [Fact]
    public void OutputSchema_HasNoNumberOrIntegerType_AnywhereInTheTree()
    {
        // FR-50 acceptance: "output schema has no numeric fields". The whole tree is walked, including a type given as an array.
        var types = new List<string>();
        CollectTypes(LoadOutputSchema(), types);

        types.Should().NotBeEmpty();
        types.Should().OnlyContain(t => t == "object" || t == "string", "the model can write text and nothing else");
        types.Should().NotContain(["number", "integer"]);
    }

    [Fact]
    public void OutputSchemaCheck_IsNotVacuous_ItSeesANumericTypeWhenThereIsOne()
    {
        // Must-fire control for the walk above: the same code finds a number in a schema that has one.
        using var doc = JsonDocument.Parse("""{"type":"object","properties":{"a":{"type":["null","number"]},"b":{"items":{"type":"integer"}}}}""");
        var types = new List<string>();

        CollectTypes(doc.RootElement, types);

        types.Should().Contain(["number", "integer"]);
    }

    // ── The input schema sidecar ────────────────────────────────────────────────────────────────

    [Fact]
    public void InputSchema_NamesTheOperandsTheFacadeSends_HasNoNumericProperty_AndMatchesTheRequestDto()
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "inputschemas", $"{Code}.input.schema.json");
        File.Exists(path).Should().BeTrue();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.GetProperty("actionCode").GetString().Should().Be(Code);
        var schema = doc.RootElement.GetProperty("inputSchema");

        string[] operands = ["outcome", "findings"];
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(operands);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(operands);
        typeof(InvoiceFirmNoticeRequest).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            ["Outcome", "Findings", "TenantId"], "TenantId travels in the run context, never in the operand");

        schema.GetProperty("properties").GetProperty("outcome").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(Enum.GetNames<InvoiceFirmNoticeOutcome>(), "the enum is the facade's outcome list, in order");
        var items = schema.GetProperty("properties").GetProperty("findings").GetProperty("items");
        string[] findingMembers = ["ruleSetName", "ruleName", "consequence", "issue"];
        items.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(findingMembers);
        items.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(findingMembers);
        typeof(InvoiceFirmNoticeFinding).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).Should().BeEquivalentTo(findingMembers);

        var types = new List<string>();
        CollectTypes(schema, types);
        types.Should().NotContain(["number", "integer"], "FR-50: the model is never sent a number");
    }

    // ── The Binding row ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BindingMirror_HasAnEnabledGlobalRowRoutingTheConsumerTypeToTheActionCode_WithNoToolProjection()
    {
        var row = BindingRows().Where(r => r.GetProperty("consumerType").GetString() == Code)
            .Should().ContainSingle("exactly one Binding row for the consumer type").Subject;

        row.GetProperty("enabled").GetBoolean().Should().BeTrue();
        row.GetProperty("actionCode").GetString().Should().Be(Code);
        row.GetProperty("consumerCode").GetString().Should().Be("default");
        row.GetProperty("environment").GetString().Should().Be("*");
        row.GetProperty("toolDescription").ValueKind.Should().Be(JsonValueKind.Null,
            "this Action is reachable only through the facade, never projected into the chat loop");
        foreach (var member in new[] { "surfaces", "disposition", "risk", "captureMode", "chipTransitions", "onEventBindings", "matchConditions" })
        {
            row.GetProperty(member).ValueKind.Should().Be(JsonValueKind.Null, member);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static void CollectTypes(JsonElement node, List<string> types)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    if (property.Name == "type")
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            types.Add(property.Value.GetString()!);
                        }
                        else if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            types.AddRange(property.Value.EnumerateArray().Select(t => t.GetString()!).Where(t => t != "null"));
                        }
                    }
                    else if (!property.Name.StartsWith("$comment", StringComparison.Ordinal) && property.Name != "description" && property.Name != "title")
                    {
                        CollectTypes(property.Value, types);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    CollectTypes(item, types);
                }

                break;
        }
    }

    private static bool ContainsNumber(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Number => true,
        JsonValueKind.Object => node.EnumerateObject().Any(p => ContainsNumber(p.Value)),
        JsonValueKind.Array => node.EnumerateArray().Any(ContainsNumber),
        _ => false,
    };

    private static IReadOnlyList<JsonElement> BindingRows()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json")));
        return doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private static JsonElement LoadOutputSchema()
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "outputschemas", $"{Code}.schema.json");
        File.Exists(path).Should().BeTrue();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static JsonElement LoadAction()
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "actions", $"{Code}.action.json");
        File.Exists(path).Should().BeTrue("the Action mirror must exist");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
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

        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }
}
