using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Eval.AssistantR4;

/// <summary>
/// spaarkeai-assistant-enhancements-r4 task 013 (FR-10 — the E1 eval-case guardrail). The R4 eval family:
/// golden-utterance cases for the P1 task-agenda capability (the advisory grounded-recommend upgrade of
/// <c>list-tasks</c> authored by task 012), wired into the <c>Category=GoldenUtteranceEval</c> merge gate.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is</b>: cases are DATA (<c>assistant-r4-eval-cases.json</c>, seeded by task 001); this class
/// is the mechanical assertion layer. Following the established R1 precedent
/// (<c>AssistantEnhancementsR1EvalTests</c>), it does NOT run a live LLM — the underlying dispatch selection
/// is DETERMINISTIC (ADR-039: one Text-path decider over authored tool descriptions, no classifier), so the
/// P1 DoD is proven by STRUCTURALLY grounding the advisory <c>list-tasks</c> Action + the two grounded-tool
/// rows + the Binding against the REAL catalog. A revert to the ack-only tier (empty groundedToolAllowList,
/// non-advisory determinism, a thin-ack outputSchema, or dropped grounding rules) FAILS this suite — that is
/// the FR-10 regression guard for the P1 behavior ("what do I need to do today" → grounded + cited summary +
/// recommendation + Tasks-opened, never a thin ack, never fabricated data).
/// </para>
/// <para>
/// <b>Merge-gate wiring</b> (identical to <c>AssistantEnhancementsR1EvalTests</c>): the
/// <c>Category=GoldenUtteranceEval</c> trait joins the dedicated <c>eval-gate</c> job in
/// <c>.github/workflows/sdap-ci.yml</c> with zero CI-YAML change — the trait IS the registration. E2
/// (task 024) + E3 (task 033) extend THIS family with their FR-04/06/09 cases; they add case data + the
/// families/vocabularies they need, never a parallel harness (§11 reuse-first).
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class AssistantEnhancementsR4EvalTests
{
    private readonly ITestOutputHelper _output;

    public AssistantEnhancementsR4EvalTests(ITestOutputHelper output) => _output = output;

    // The two grounded READ tools the advisory task-agenda capability is allowed to mount (task 010/012).
    private const string GridOverviewToolId = "spaarke.grid_overview";
    private const string DailyBriefingOverviewToolId = "spaarke.daily_briefing_overview";

    // -------------------------------------------------------------------------
    // Closed vocabularies (this family's case-schema contract)
    // -------------------------------------------------------------------------

    private static readonly IReadOnlySet<string> Families = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // E1 (task 013). E3 (task 033) adds "preference-loop". E2 (task 024) adds its families when it extends the suite.
        "task-agenda-advisory",
        "preference-loop",
    };

    private static readonly IReadOnlySet<string> Channels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text", "click" };

    private static readonly IReadOnlySet<string> OutcomeClasses =
        // "dispatch" = a capability runs (E1). "preference-bias" = a confirmed standing directive biases an
        // already-available capability's DEFAULT (E3, task 032) — it does NOT dispatch by itself.
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dispatch", "preference-bias" };

    private static readonly IReadOnlySet<string> CatalogStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "existing", "mirrored", "live-catalog" };

    /// <summary>The §3 UC trigger family the E1 task-agenda capability derives from (task orchestration).</summary>
    private static readonly IReadOnlySet<string> R4UcIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UC-H-1" };

    // Per-family coverage floor (FR-10: each behavior owes its golden utterances).
    private static readonly IReadOnlyDictionary<string, int> FamilyFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["task-agenda-advisory"] = 3, // E1: the 'today' ask, a phrasing variant, a prioritization ask.
        ["preference-loop"] = 1,      // E3: the P3 feedback→memory→bias loop.
    };

    // -------------------------------------------------------------------------
    // Inventory integrity
    // -------------------------------------------------------------------------

    [Fact]
    public void Suite_Loads_WithNamespacedUniqueCaseIds_AndClosedVocabularies()
    {
        var suite = LoadSuite();

        suite.SchemaVersion.Should().Be("assistant-r4-eval@v1", "the family declares its schema version");
        suite.Cases.Should().NotBeEmpty();
        suite.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability + CI diffs");
        suite.Cases.Should().OnlyContain(c => c.CaseId.StartsWith("AR4-", StringComparison.Ordinal),
            "R4 cases are namespaced AR4-### so they never collide with the golden GU-### or the AR1-### ids");

        foreach (var c in suite.Cases)
        {
            Families.Should().Contain(c.Family, $"case {c.CaseId}: family is a closed vocabulary");
            Channels.Should().Contain(c.Channel, $"case {c.CaseId}: channel is text|click");
            OutcomeClasses.Should().Contain(c.Expected.OutcomeClass, $"case {c.CaseId}: outcome class is closed");
            R4UcIds.Should().Contain(c.UcId, $"case {c.CaseId}: ucId must trace to an R4 §3 UC trigger family (UC-H-1 task orchestration)");
            c.Expected.ConsumerType.Should().NotBeNullOrWhiteSpace($"case {c.CaseId}: every R4 case names its expected capability");
            c.Expected.CatalogStatus.Should().NotBeNull($"case {c.CaseId}: every case declares its catalog-grounding status");
            CatalogStatuses.Should().Contain(c.Expected.CatalogStatus!, $"case {c.CaseId}: catalogStatus is a closed vocabulary");
        }
    }

    [Fact]
    public void TaskAgendaAdvisoryFamily_HasEvalCoverage_AtItsFloor()
    {
        var suite = LoadSuite();

        foreach (var (family, floor) in FamilyFloors)
        {
            suite.Cases.Count(c => string.Equals(c.Family, family, StringComparison.OrdinalIgnoreCase))
                .Should().BeGreaterOrEqualTo(floor,
                    $"FR-10: the '{family}' behavior owes at least {floor} golden-utterance case(s)");
        }
    }

    [Fact]
    public void EveryDispatchConsumerType_IsGroundedPerItsDeclaredCatalogStatus()
    {
        var suite = LoadSuite();
        var mirrorConsumerTypes = LoadMirrorConsumerTypes();

        foreach (var c in suite.Cases)
        {
            var consumerType = c.Expected.ConsumerType!;
            switch (c.Expected.CatalogStatus!.ToLowerInvariant())
            {
                case "existing":
                    ConsumerTypes.All.Should().Contain(consumerType,
                        $"case {c.CaseId}: catalogStatus=existing so '{consumerType}' must be a ConsumerTypes constant");
                    break;

                case "mirrored":
                    mirrorConsumerTypes.Should().Contain(consumerType,
                        $"case {c.CaseId}: catalogStatus=mirrored so '{consumerType}' must be a row in sprk_playbookconsumer-rows.json");
                    break;

                case "live-catalog":
                    c.Expected.SeededBy.Should().NotBeNullOrWhiteSpace(
                        $"case {c.CaseId}: a live-catalog capability MUST cite the task that seeded it (honest grounding — no invented names)");
                    break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // The P1 DoD structural proof — the advisory grounded-recommend tier (FR-01/02/03)
    // -------------------------------------------------------------------------

    /// <summary>
    /// The heart of the FR-10 guardrail: the <c>list-tasks</c> Action mirror declares the ADVISORY
    /// grounded-recommend tier — output_determinism:advisory, a non-empty bounded grounded-tool allow-list
    /// (the two grounded READ tools), the Reasoning tier, the ADVISORY GROUNDING RULES prompt (cite every
    /// fact, never fabricate, never ask the user's identity), and a RICH acknowledgement outputSchema (the
    /// ack-only maxLength of 200 was widened for the summary). A revert to the ack-only tier — the exact P1
    /// UAT defect ("thin ack, no summary") — fails one of these assertions.
    /// </summary>
    [Fact]
    public void ListTasksAction_DeclaresAdvisoryGroundedRecommendTier_NotAckOnly()
    {
        var actionPath = Path.Combine(FindRepoRoot(), "infra", "dataverse", "actions", "list-tasks.action.json");
        File.Exists(actionPath).Should().BeTrue($"the advisory list-tasks Action mirror must exist at {actionPath}");
        using var doc = JsonDocument.Parse(File.ReadAllText(actionPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;

        root.GetProperty("outputDeterminism").GetString().Should().Be("advisory",
            "the P1 fix authors list-tasks at the ADVISORY end (grounded reasoning + recommendation), not the ack-only fact end");

        var allowList = root.GetProperty("groundedToolAllowList").EnumerateArray().Select(e => e.GetString()).ToList();
        allowList.Should().BeEquivalentTo(new[] { GridOverviewToolId, DailyBriefingOverviewToolId },
            "the advisory capability opts into EXACTLY the two shipped grounded READ tools — a non-empty allow-list " +
            "is the advisory routing signal AND the task-011 PreFilter's mount set (no thin ack, no whole-catalog)");

        root.GetProperty("modelTier").GetString().Should().Be("Reasoning",
            "advisory prioritization over grounded task/briefing data runs on the ADR-016 Reasoning tier");

        var systemPrompt = root.GetProperty("systemPrompt").GetString();
        systemPrompt.Should().NotBeNullOrWhiteSpace();
        systemPrompt!.Should().ContainEquivalentOf("ADVISORY GROUNDING RULES",
            "the advisory obligations are enforced in the prompt (the runtime grounding contract)");
        systemPrompt.Should().MatchRegex("(?i)hallucinat|fabricat",
            "the prompt forbids fabrication — every narrated fact must come from a tool result");
        systemPrompt.Should().MatchRegex("(?i)cite",
            "the prompt mandates citing counts/names/dates to their record ids (no-fabrication grounding)");
        systemPrompt.Should().MatchRegex("(?i)never ask the user for their user id|do not ask the user for their user id",
            "the prompt forbids the P2 identity-ask dead-end — the tools scope to the caller over OBO automatically");

        var acknowledgementMaxLength = root
            .GetProperty("outputSchema").GetProperty("properties").GetProperty("acknowledgement")
            .GetProperty("maxLength").GetInt32();
        acknowledgementMaxLength.Should().BeGreaterThan(1000,
            "the acknowledgement holds the grounded summary + recommendation now — the ack-only maxLength (200) " +
            "was widened; a revert to the thin-ack length fails here");

        _output.WriteLine(
            $"P1 DoD grounded: list-tasks advisory (determinism=advisory, tier=Reasoning, allowList=[{string.Join(", ", allowList)}], " +
            $"acknowledgement.maxLength={acknowledgementMaxLength}).");
    }

    /// <summary>
    /// The "Tasks opens" half of the P1 DoD: the <c>list-tasks</c> Binding still declares the
    /// <c>surface_launch</c> disposition (opens the My Tasks workspace grid tab) and stays read-only (risk
    /// None) — the advisory upgrade added grounded narration WITHOUT changing the launch/routing contract.
    /// </summary>
    [Fact]
    public void ListTasksBinding_DeclaresSurfaceLaunch_ForTasksOpened()
    {
        var mirrorPath = Path.Combine(FindRepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json");
        using var mirror = JsonDocument.Parse(File.ReadAllText(mirrorPath));

        var row = mirror.RootElement.GetProperty("rows").EnumerateArray()
            .First(r => r.GetProperty("consumerType").GetString() == "list-tasks");

        row.GetProperty("enabled").GetBoolean().Should().BeTrue("the list-tasks Binding is live");
        row.GetProperty("disposition").GetInt32().Should().Be((int)BindingDisposition.SurfaceLaunch,
            "list-tasks dispatches a surface_launch SSE that opens the My Tasks workspace grid tab — the advisory " +
            "narration is emitted alongside the launch (ADR-040), not instead of it");
        row.GetProperty("risk").GetInt32().Should().Be((int)BindingRisk.None,
            "a read/advisory capability never gates a confirmation turn");
    }

    /// <summary>
    /// The grounded + cited + no-fabrication + no-identity-ask contract: the two tools the advisory Action
    /// allow-lists exist in the catalog as Chat-context READ tools whose authored descriptions mandate citing
    /// record ids, forbid inventing numbers, and forbid asking the user's identity (OBO). This grounds the
    /// "grounded + cited summary" and the P2 "never asks for the user's id" halves of the DoD against the real
    /// tool catalog rather than an LLM judge.
    /// </summary>
    [Fact]
    public void AdvisoryGroundedTools_ExistInCatalog_AndAssertGroundingAndObo()
    {
        var grid = LoadToolRow("sprk_analysistool-grid-overview-row.json");
        var briefing = LoadToolRow("sprk_analysistool-daily-briefing-overview-row.json");

        grid.GetProperty("sprk_toolid").GetString().Should().Be(GridOverviewToolId);
        briefing.GetProperty("sprk_toolid").GetString().Should().Be(DailyBriefingOverviewToolId);

        foreach (var (name, row) in new[] { (GridOverviewToolId, grid), (DailyBriefingOverviewToolId, briefing) })
        {
            row.GetProperty("sprk_availableincontexts").GetInt32().Should().Be(100000001,
                $"{name} is a Chat-context (agent-loop) tool — it is what the advisory nested turn mounts");

            var description = row.GetProperty("sprk_description").GetString();
            description.Should().NotBeNullOrWhiteSpace();
            description!.Should().MatchRegex("(?i)cite",
                $"{name}'s description mandates citing record ids — the advisory summary's facts are grounded + cited");
            description.Should().MatchRegex("(?i)do not invent|never ask the user for their user id",
                $"{name}'s description forbids fabrication AND the identity-ask (OBO auto-scopes to the caller) — the P1/P2 grounding contract");
        }
    }

    // -------------------------------------------------------------------------
    // E3 — the P3 feedback→memory→bias loop grounding (FR-08/09, task 033)
    // -------------------------------------------------------------------------

    /// <summary>
    /// The E3 (preference-loop) golden case, grounded in the merge gate: a CONFIRMED standing directive biases
    /// an already-cataloged capability's DEFAULT (task 032), ONLY when confirmed (task 031's dormant-candidate
    /// rule), never off-allow-list, via a prompt hint that never grants a capability — and the biased
    /// capability is a REAL mirror binding, never a phantom. A regression that opened the preference-steering
    /// boundary (off-allow-list steering, unconfirmed steering, or a phantom target) fails this.
    /// </summary>
    [Fact]
    public void PreferenceLoop_BiasesARealCataloguedCapability_ConfirmedOnly_OffAllowListInert()
    {
        // The E3 bias points at a REAL cataloged capability (the same list-tasks the E1 family grounds) — the
        // producer's closed allow-list can never bias a capability that does not exist as a Binding.
        PreferenceDirectiveProducer.AllowList.Should().Contain(
            d => d.TargetCapability.Contains("list-tasks", StringComparison.OrdinalIgnoreCase),
            "the task-agenda directive biases the FR-01 list-tasks capability");
        LoadMirrorConsumerTypes().Should().Contain("list-tasks",
            "the biased capability is a real sprk_playbookconsumer binding, not an invented one");

        // A CONFIRMED allow-listed directive produces the server-authored bias hint for that capability.
        var hint = PreferenceDirectiveProducer.Produce(new[] { Preference("always summarize my tasks", confirmed: true) });
        hint.Should().NotBeNull();
        hint!.Should().Contain("task-agenda capability");

        // CONFIRMED-ONLY: the same directive UNCONFIRMED (a task-031 dormant candidate) does not steer.
        PreferenceDirectiveProducer.Produce(new[] { Preference("always summarize my tasks", confirmed: false) })
            .Should().BeNull("an unconfirmed inference must not bias tool selection until acknowledged (ADR-042 / task 031)");

        // OFF-ALLOW-LIST: a confirmed directive outside the closed set is inert (owner Q2: no free-text steering).
        PreferenceDirectiveProducer.Produce(new[] { Preference("always use a very formal tone", confirmed: true) })
            .Should().BeNull("off-allow-list directives have no tool-selection effect");

        // NEVER grants a capability / alters a fact — the DATA-guard states the hard bound (defense-in-depth
        // atop the structural guarantee that the hint is prompt-only and never reaches AgentToolFilterContext).
        hint.Should().Contain("NEVER grant a capability, change a grounded fact");
    }

    private static MemoryFact Preference(string directive, bool confirmed) =>
        new()
        {
            Type = MemoryFactType.Preference,
            Key = directive,
            Value = directive,
            Source = confirmed ? MemoryOrigin.User : MemoryOrigin.AiDerived,
            ConfirmedByUser = confirmed,
            Confidence = confirmed ? 1.0 : 0.5,
        };

    // -------------------------------------------------------------------------
    // Helpers (mirror AssistantEnhancementsR1EvalTests)
    // -------------------------------------------------------------------------

    private static JsonElement LoadToolRow(string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "dataverse", fileName);
        File.Exists(path).Should().BeTrue($"the grounded-tool mirror row must exist at {path}");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static IReadOnlyCollection<string> LoadMirrorConsumerTypes()
    {
        var mirrorPath = Path.Combine(FindRepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json");
        using var mirror = JsonDocument.Parse(File.ReadAllText(mirrorPath));
        return mirror.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("consumerType").GetString()!)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static AssistantR4EvalSuite LoadSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "assistant-r4-eval-cases.json");
        File.Exists(path).Should().BeTrue(
            $"assistant-r4-eval-cases.json must be copied to test output at {path} " +
            "(Content include in Sprk.Bff.Api.Tests.csproj — the contract-Eval ItemGroup)");
        var suite = JsonSerializer.Deserialize<AssistantR4EvalSuite>(File.ReadAllText(path), JsonOptions);
        suite.Should().NotBeNull("the seed file must deserialize into the R4 case schema");
        return suite!;
    }

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
            "the R4 eval catalog-grounding assertions require an in-repo test run.");
    }
}

// -----------------------------------------------------------------------------
// R4 eval case schema (mirrors the R1 family shape — assistant-r1-eval-cases.json)
// -----------------------------------------------------------------------------

public sealed record AssistantR4EvalSuite
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("cases")]
    public List<AssistantR4EvalCase> Cases { get; init; } = new();
}

public sealed record AssistantR4EvalCase
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("family")]
    public string Family { get; init; } = string.Empty;

    [JsonPropertyName("ucId")]
    public string UcId { get; init; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    [JsonPropertyName("utterance")]
    public string Utterance { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("expected")]
    public AssistantR4Expected Expected { get; init; } = new();
}

public sealed record AssistantR4Expected
{
    /// <summary>dispatch (closed set for E1; E2/E3 extend as needed).</summary>
    [JsonPropertyName("outcomeClass")]
    public string OutcomeClass { get; init; } = string.Empty;

    [JsonPropertyName("consumerType")]
    public string? ConsumerType { get; init; }

    /// <summary>existing (ConsumerTypes constant) | mirrored (rows mirror) | live-catalog (seeded, parity pending).</summary>
    [JsonPropertyName("catalogStatus")]
    public string? CatalogStatus { get; init; }

    /// <summary>For live-catalog / mirrored capabilities: the task that seeded the Binding/Action.</summary>
    [JsonPropertyName("seededBy")]
    public string? SeededBy { get; init; }
}
