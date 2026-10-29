using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract test for the <c>billing-rule-draft</c> Action (BILLING-RULE-DRAFT@v1; ebilling-r1 task 075, FR-27, D-27,
/// D-52). Pins the Action mirror (<c>infra/dataverse/actions/billing-rule-draft.action.json</c>), its output-schema and input-schema
/// sidecars and its Binding row against the facade contract (<see cref="BillingRuleDraftRequest"/>,
/// <see cref="BillingRuleDraftResult"/> and the parser <c>IInvoiceAi.DraftRulesAsync</c> runs) and against the billing rule
/// catalogue (the eleven types and the parameters of their body schemas), so the Action cannot drift from the code that feeds and
/// reads it.
/// </summary>
/// <remarks>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — <see cref="InvoiceCodeSetMatchActionContractTests"/> pins the
/// 054 Action only; <see cref="CatalogInputSchemaContractTests"/> validates every input mirror generically. (2) <i>Extension</i> — a
/// narrowly-scoped class for the one Action this task ships, mirroring the 054 class. (3) <i>Cost of doing nothing</i> — a
/// <c>ruleType</c> enum that drifts from the catalogue makes the model propose types the validator refuses (or never propose a new
/// one); a <c>parameters</c> member that is missing from the output schema can never be drafted; a missing Binding row makes the
/// facade <c>binding_missing</c> and rule drafting do nothing.
/// </remarks>
public class BillingRuleDraftActionContractTests
{
    private const string Code = "billing-rule-draft";
    private static readonly string[] ResultMembers = ["proposals", "notConverted"];
    private static readonly string[] ProposalMembers = ["ruleType", "parameters", "consequence", "clauseQuote", "clauseLocation"];
    private static readonly string[] NotConvertedMembers = ["text", "location", "reason"];

    // ── Tier and identity ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Action_DeclaresTheStandardTierAndTheRuleDraftCode_AndNamesNoDeployment()
    {
        var action = LoadAction();

        action.GetProperty("actionCode").GetString().Should().Be(Code);
        action.GetProperty("description").GetString().Should().Contain("BILLING-RULE-DRAFT@v1");
        action.GetProperty("modelTier").GetString().Should().Be("Standard", "the existing tier map, unchanged (D-51)");
        action.EnumerateObject().Select(p => p.Name).Should().NotContain(["deploymentName", "deployment", "model"],
            "a model deployment name is configuration, never an Action column");
        action.GetProperty("instruction").GetRawText().ToLowerInvariant().Should().NotContain("gpt-");
        action.GetProperty("temperature").GetDecimal().Should().Be(0m, "the same text gets the same proposals on every run");
    }

    [Fact]
    public void ConsumerTypesKey_MatchesTheActionCode_AndIsListedInAll()
    {
        ConsumerTypes.BillingRuleDraft.Should().Be(Code);
        ConsumerTypes.All.Should().Contain(Code, "the catalog health check (RoutingConsumerTypeHealthCheck) iterates All");
    }

    // ── The authored prompt ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Action_IsAJpsPrompt_WithAClosedTypeList_NotConverted_VerbatimQuotes_AndAnInjectionGuard()
    {
        var action = LoadAction();

        action.GetProperty("$schema").GetString().Should().Be("https://spaarke.com/schemas/prompt/v1");
        var instruction = action.GetProperty("instruction");
        instruction.GetProperty("role").GetString().Should().NotBeNullOrWhiteSpace();
        instruction.GetProperty("task").GetString().Should().Contain("documentText").And.Contain("ruleTypes",
            "the operand names the facade sends");

        var constraints = string.Join('\n', instruction.GetProperty("constraints").EnumerateArray().Select(c => c.GetString()));
        constraints.Should().Contain("closed", "the model proposes only the offered rule types (D-27)");
        constraints.Should().Contain("notConverted", "text no type can check is listed, never forced into the nearest type");
        constraints.Should().Contain("word for word", "the clause quote is the evidence the administrator reads (FR-27)");
        constraints.Should().Contain("never instructions", "the document is data supplied by a person (prompt-injection guard)");
        constraints.Should().Contain("Never guess a number", "a rule parameter the clause does not state is not invented");

        action.GetProperty("output").GetProperty("structuredOutput").GetBoolean().Should().BeTrue();
        action.GetProperty("output").GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .Should().Equal(ResultMembers);
    }

    [Fact]
    public void Examples_ParseThroughTheFacadeParser_AndShowAProposalAndANotConverted()
    {
        var action = LoadAction();
        var sawProposal = false;
        var sawNotConverted = false;
        var offered = BillingRuleCatalogue.Types.Select(t => t.ToString()).ToList();

        foreach (var example in action.GetProperty("examples").EnumerateArray())
        {
            var parsed = InvoiceAiOutputParser.ParseDraftRules(example.GetProperty("output"));

            using var input = JsonDocument.Parse(example.GetProperty("input").GetString()!);
            input.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["documentText", "ruleTypes"]);
            var documentText = input.RootElement.GetProperty("documentText").GetString()!;
            var offeredInExample = input.RootElement.GetProperty("ruleTypes").EnumerateArray()
                .Select(t => t.GetProperty("ruleType").GetString()).ToList();

            foreach (var proposal in parsed.Proposals)
            {
                offeredInExample.Should().Contain(proposal.RuleType, "an example never answers outside its own offered list");
                offered.Should().Contain(proposal.RuleType);
                documentText.Should().Contain(proposal.ClauseQuote, "the clause quote is verbatim text from the document");
                IsKnownConsequence(proposal.Consequence).Should().BeTrue();
                sawProposal = true;
            }

            foreach (var entry in parsed.NotConverted)
            {
                documentText.Should().Contain(entry.Text, "a not-converted clause is verbatim text from the document");
                entry.Reason.Should().NotBeNullOrWhiteSpace();
                sawNotConverted = true;
            }
        }

        sawProposal.Should().BeTrue();
        sawNotConverted.Should().BeTrue("the examples show that leaving a clause out of the proposals is the right answer when no type fits");
    }

    [Fact]
    public void ThePrompt_NeverSuggestsAConsequenceTheCatalogueDisallows_ForTheType_F1b()
    {
        // ebilling-r1 073 review F1-b: a consequence the type may not carry is refused at save, so the prompt must not teach it. The
        // examples use only allowed consequences, and the constraints tell the model to read them from each type's description.
        var action = LoadAction();
        var constraints = string.Join('\n', action.GetProperty("instruction").GetProperty("constraints").EnumerateArray().Select(c => c.GetString()));
        constraints.Should().Contain("Allowed consequences", "the model reads each type's allowed consequences from its description")
            .And.Contain("does not allow, use Flag");

        foreach (var example in action.GetProperty("examples").EnumerateArray())
        {
            foreach (var proposal in InvoiceAiOutputParser.ParseDraftRules(example.GetProperty("output")).Proposals)
            {
                var type = Enum.Parse<RuleType>(proposal.RuleType);
                var consequence = Enum.Parse<Sprk.Bff.Api.Services.Finance.BillingConsequence>(proposal.Consequence);
                BillingRuleCatalogue.AllowedConsequences(type).Should().Contain(
                    consequence, $"the example proposes {proposal.Consequence} for {proposal.RuleType}");
            }
        }
    }

    // ── The output schema sidecar ───────────────────────────────────────────────────────────────

    [Fact]
    public void OutputSchema_IsValidOpenAiStructuredOutputSchema_AndMatchesTheResultContract()
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "outputschemas", $"{Code}.schema.json");
        File.Exists(path).Should().BeTrue("the output-schema mirror is the repo-first authored source");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var schema = doc.RootElement;

        OpenAiFunctionSchemaValidator.FindFirstError(schema.GetRawText()).Should().BeNull("an invalid schema would 400-fail the first live call");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(ResultMembers);
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(ResultMembers);

        var proposal = schema.GetProperty("properties").GetProperty("proposals").GetProperty("items");
        proposal.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        proposal.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(ProposalMembers);
        proposal.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(ProposalMembers);
        typeof(BillingRuleProposal).GetProperties().Select(Camel).Should().BeEquivalentTo(
            ["ruleType", "parametersJson", "consequence", "clauseQuote", "clauseLocation"],
            "the facade surfaces parameters as raw JSON text under ParametersJson");

        var notConverted = schema.GetProperty("properties").GetProperty("notConverted").GetProperty("items");
        notConverted.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        notConverted.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(NotConvertedMembers);
        notConverted.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(NotConvertedMembers);
        typeof(BillingRuleNotConverted).GetProperties().Select(Camel).Should().BeEquivalentTo(NotConvertedMembers);

        foreach (var property in proposal.GetProperty("properties").EnumerateObject().Concat(notConverted.GetProperty("properties").EnumerateObject()))
        {
            property.Value.TryGetProperty("required", out var required).Should().Be(
                property.Name == "parameters",
                $"{property.Name}: only the nested parameters OBJECT has an object-level required array; no property uses the boolean form");
            if (property.Name != "parameters")
            {
                required.ValueKind.Should().Be(JsonValueKind.Undefined);
            }
        }

        typeof(BillingRuleDraftResult).GetProperties().Select(Camel).Should().BeEquivalentTo(ResultMembers);
    }

    [Fact]
    public void OutputSchema_RuleTypeEnum_IsTheElevenCatalogueTypes_InCatalogueOrder()
    {
        var proposal = LoadOutputSchema().GetProperty("properties").GetProperty("proposals").GetProperty("items");
        proposal.GetProperty("properties").GetProperty("ruleType").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(BillingRuleCatalogue.Types.Select(t => t.ToString()),
                "a type the validator would refuse, or one missing from the enum, makes drafting wrong silently");
        proposal.GetProperty("properties").GetProperty("consequence").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Reject", "Flag", "SuggestAdjustment");
    }

    [Fact]
    public void OutputSchema_Parameters_AreTheUnionOfTheMembersOfTheElevenBodySchemas_EachNullable()
    {
        var parameters = LoadOutputSchema().GetProperty("properties").GetProperty("proposals").GetProperty("items")
            .GetProperty("properties").GetProperty("parameters");
        parameters.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

        var union = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in BillingRuleCatalogue.Types)
        {
            var path = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals", "Schemas",
                $"billing-{Kebab(type.ToString())}.schema.json");
            File.Exists(path).Should().BeTrue($"the body schema of {type}");
            using var body = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in body.RootElement.GetProperty("properties").EnumerateObject().Where(p => p.Name != "type"))
            {
                union.Add(property.Name);
            }
        }

        parameters.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(union,
            "a body member the output schema lacks can never be drafted; one it has extra is refused by the body schema");
        parameters.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(union,
            "Structured Outputs requires every property; absent parameters are null");
        foreach (var property in parameters.GetProperty("properties").EnumerateObject())
        {
            property.Value.GetProperty("type").EnumerateArray().Select(t => t.GetString()).Should().Contain("null", property.Name);
        }
    }

    // ── The input schema sidecar ────────────────────────────────────────────────────────────────

    [Fact]
    public void InputSchema_NamesTheOperandsTheFacadeSends_AndTheMembersOfTheRequestDtos()
    {
        var path = Path.Combine(RepoRoot(), "infra", "dataverse", "inputschemas", $"{Code}.input.schema.json");
        File.Exists(path).Should().BeTrue();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.GetProperty("actionCode").GetString().Should().Be(Code);
        var schema = doc.RootElement.GetProperty("inputSchema");

        // The operand is { documentText, ruleTypes[{ruleType,description,parameterSchema}] } (InvoiceAi.DraftRulesAsync).
        string[] operands = ["documentText", "ruleTypes"];
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(operands);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(operands);
        typeof(BillingRuleDraftRequest).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            ["DocumentText", "DocumentId", "RuleTypes", "TenantId"],
            "DocumentId and TenantId travel in the run context, never in the operand");

        var items = schema.GetProperty("properties").GetProperty("ruleTypes").GetProperty("items");
        items.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["ruleType", "description", "parameterSchema"]);
        items.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(["ruleType", "description", "parameterSchema"]);
        typeof(BillingRuleTypeOption).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["RuleType", "Description", "ParameterSchemaJson"]);
    }

    // ── The Binding row ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BindingMirror_HasAnEnabledGlobalRowRoutingTheConsumerTypeToTheActionCode_WithNoToolProjection()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json")));
        var row = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("consumerType").GetString() == Code)
            .Should().ContainSingle("exactly one Binding row for the consumer type").Subject;

        row.GetProperty("enabled").GetBoolean().Should().BeTrue();
        row.GetProperty("actionCode").GetString().Should().Be(Code);
        row.GetProperty("consumerCode").GetString().Should().Be("default");
        row.GetProperty("environment").GetString().Should().Be("*");
        row.GetProperty("toolDescription").ValueKind.Should().Be(JsonValueKind.Null,
            "this Action is reachable only through the facade, never projected into the chat loop");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static bool IsKnownConsequence(string text) => text is "Reject" or "Flag" or "SuggestAdjustment";

    private static string Camel(System.Reflection.PropertyInfo p) => char.ToLowerInvariant(p.Name[0]) + p.Name[1..];

    private static string Kebab(string pascal) =>
        string.Concat(pascal.Select((c, i) => char.IsUpper(c) ? (i == 0 ? "" : "-") + char.ToLowerInvariant(c) : c.ToString()));

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
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }
}
