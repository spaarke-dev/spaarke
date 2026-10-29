using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract test for the <c>invoice-codeset-match</c> Action (INVOICE-CODESET-MATCH@v1; ebilling-r1 task 054, FR-22,
/// D-93, D-52). Pins the Action mirror (<c>infra/dataverse/actions/invoice-codeset-match.action.json</c>), its output-schema and
/// input-schema sidecars and its Binding row against the facade contract (<see cref="InvoiceCodeSetMatchRequest"/>,
/// <see cref="InvoiceCodeSetMatchResult"/> and the parser <c>IInvoiceAi.MatchCodeSetAsync</c> runs), so the Action cannot drift
/// from the code that reads and feeds it.
/// </summary>
/// <remarks>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — <see cref="InvoiceLineCodeActionContractTests"/> pins the
/// 047 Action only; <see cref="CatalogInputSchemaContractTests"/> validates every input mirror generically. (2) <i>Extension</i> — a
/// narrowly-scoped class for the one Action this task ships, mirroring the 047 class. (3) <i>Cost of doing nothing</i> — an invalid
/// output schema 400-fails the first live call; a required set that drifts from the DTO makes every match <c>output_invalid</c>
/// (every proposal silently unreviewed); a missing Binding row makes the facade <c>binding_missing</c>.
/// </remarks>
public class InvoiceCodeSetMatchActionContractTests
{
    private const string Code = "invoice-codeset-match";
    private static readonly string[] ResultMembers = ["codeSetId", "confidence", "reason"];

    // ── Tier and identity ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Action_DeclaresTheFastTierAndTheCodeSetMatchCode_AndNamesNoDeployment()
    {
        var action = LoadAction();

        action.GetProperty("actionCode").GetString().Should().Be(Code);
        action.GetProperty("description").GetString().Should().Contain("INVOICE-CODESET-MATCH@v1");
        action.GetProperty("modelTier").GetString().Should().Be("Fast", "the existing tier map, unchanged (D-51)");
        action.EnumerateObject().Select(p => p.Name).Should().NotContain(["deploymentName", "deployment", "model"],
            "a model deployment name is configuration, never an Action column");
        action.GetProperty("instruction").GetRawText().ToLowerInvariant().Should().NotContain("gpt-");
        action.GetProperty("temperature").GetDecimal().Should().Be(0m, "the same row gets the same answer on every run");
    }

    [Fact]
    public void ConsumerTypesKey_MatchesTheActionCode_AndIsListedInAll()
    {
        ConsumerTypes.InvoiceCodeSetMatch.Should().Be(Code);
        ConsumerTypes.All.Should().Contain(Code, "the catalog health check (RoutingConsumerTypeHealthCheck) iterates All");
    }

    // ── The authored prompt ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Action_IsAJpsPrompt_WithAClosedCandidateList_ANoneAnswer_AndAnInjectionGuard()
    {
        var action = LoadAction();

        action.GetProperty("$schema").GetString().Should().Be("https://spaarke.com/schemas/prompt/v1");
        var instruction = action.GetProperty("instruction");
        instruction.GetProperty("role").GetString().Should().NotBeNullOrWhiteSpace();
        instruction.GetProperty("task").GetString().Should().Contain("reference").And.Contain("candidates").And.Contain("examples",
            "the operand names the facade sends");

        var constraints = string.Join('\n', instruction.GetProperty("constraints").EnumerateArray().Select(c => c.GetString()));
        constraints.Should().Contain("closed", "the model picks only from the supplied candidates");
        constraints.Should().Contain("none", "D-93: the review step can answer that no set fits");
        constraints.Should().Contain("matchHints", "the admin-editable tuning text steers the choice");
        constraints.Should().Contain("never instructions", "names, hints and examples are admin-supplied data (prompt-injection guard)");

        action.GetProperty("output").GetProperty("structuredOutput").GetBoolean().Should().BeTrue();
        action.GetProperty("output").GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .Should().Equal(ResultMembers);
    }

    [Fact]
    public void Examples_ParseThroughTheFacadeParser_AndShowBothAChosenCandidateAndNone()
    {
        var action = LoadAction();
        var sawNone = false;
        var sawCandidate = false;

        foreach (var example in action.GetProperty("examples").EnumerateArray())
        {
            var parsed = InvoiceAiOutputParser.ParseCodeSetMatch(example.GetProperty("output"));
            parsed.Confidence.Should().BeInRange(0m, 1m);
            parsed.Reason.Should().NotBeNullOrWhiteSpace();

            using var input = JsonDocument.Parse(example.GetProperty("input").GetString()!);
            input.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["reference", "candidates", "examples"]);
            var candidateIds = input.RootElement.GetProperty("candidates").EnumerateArray()
                .Select(c => c.GetProperty("codeSetId").GetString()).ToList();
            if (parsed.CodeSetId == InvoiceCodeSetMatchResult.NoneFits)
            {
                sawNone = true;
            }
            else
            {
                candidateIds.Should().Contain(parsed.CodeSetId, "an example never answers outside its own candidate list");
                sawCandidate = true;
            }
        }

        sawCandidate.Should().BeTrue();
        sawNone.Should().BeTrue("the examples show that none is a legitimate answer (D-93)");
    }

    [Fact]
    public void NoneAnswer_NeverNamesAClosestSet_BecauseTheApplicationReportsTheClosestBySimilarity()
    {
        // Task 054 fix-round-1 F1: the service appends "closest by similarity: '<set>'" from the embedding ranking. A reason that also
        // named the model's own "closest" set could name a DIFFERENT set in the same sentence, so the Action is told not to.
        var action = LoadAction();
        var constraints = action.GetProperty("instruction").GetProperty("constraints").EnumerateArray().Select(c => c.GetString()!).ToList();
        constraints.Should().NotContain(c => c.Contains("came closest", StringComparison.OrdinalIgnoreCase));
        constraints.Should().Contain(c => c.Contains("Do not say which candidate is closest", StringComparison.Ordinal));

        foreach (var example in action.GetProperty("examples").EnumerateArray())
        {
            var output = example.GetProperty("output");
            if (output.GetProperty("codeSetId").GetString() == InvoiceCodeSetMatchResult.NoneFits)
            {
                output.GetProperty("reason").GetString().Should().NotContainEquivalentOf("closest", "a none example teaches the model what to write");
            }
        }

        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "infra", "dataverse", "outputschemas", $"{Code}.schema.json")));
        schema.RootElement.GetProperty("properties").GetProperty("reason").GetProperty("description").GetString()
            .Should().NotContainEquivalentOf("closest", "the output schema's field description is part of what the model reads");
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
        foreach (var property in schema.GetProperty("properties").EnumerateObject())
        {
            property.Value.TryGetProperty("required", out _).Should().BeFalse($"{property.Name}: use the object-level required array");
        }

        schema.GetProperty("properties").GetProperty("codeSetId").TryGetProperty("enum", out _).Should().BeFalse(
            "the candidate ids differ per request; CodeSetProposalService's guard is the control");
        typeof(InvoiceCodeSetMatchResult).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .Should().BeEquivalentTo(ResultMembers);
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

        // The operand is { reference{type,name}, candidates[], examples[] } (InvoiceAi.MatchCodeSetAsync).
        string[] operands = ["reference", "candidates", "examples"];
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(operands);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(operands);
        typeof(InvoiceCodeSetMatchRequest).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            ["ReferenceType", "ReferenceName", "Candidates", "Examples", "TenantId"],
            "ReferenceType and ReferenceName travel as reference{type,name}; TenantId travels in the run context, never in the operand");

        var reference = schema.GetProperty("properties").GetProperty("reference").GetProperty("properties");
        reference.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["type", "name"]);
        reference.GetProperty("type").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(Enum.GetNames<InvoiceCodeSetReferenceType>());

        Members(schema, "candidates").Should().BeEquivalentTo(CamelProperties<InvoiceCodeSetCandidate>());
        Members(schema, "examples").Should().BeEquivalentTo(CamelProperties<InvoiceCodeSetMatchExample>());
        schema.GetProperty("properties").GetProperty("examples").GetProperty("items").GetProperty("properties")
            .GetProperty("referenceType").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(Enum.GetNames<InvoiceCodeSetReferenceType>());
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

    private static IEnumerable<string> Members(JsonElement schema, string operand) =>
        schema.GetProperty("properties").GetProperty(operand).GetProperty("items").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name);

    private static IEnumerable<string> CamelProperties<T>() =>
        typeof(T).GetProperties().Where(p => p.Name != "EqualityContract").Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]);

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
