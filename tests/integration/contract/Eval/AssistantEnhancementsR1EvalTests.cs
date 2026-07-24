using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Eval.AssistantR1;

/// <summary>
/// spaarkeai-assistant-enhancements-r1 task 051 (NFR-06 merge gate) — the R1 eval family.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is</b>: the operational eval coverage every R1 catalog change owes the suite
/// (owed-eval-cases.md) PLUS the two R1-specific proofs the golden-utterance schema does not model:
/// the FR-E4 <b>profile-injection non-flip</b> and the AC3 <b>incoherent practice-area × matter-type</b>
/// case. Cases are DATA (<c>assistant-r1-eval-cases.json</c>); this harness is the mechanical assertion
/// layer.
/// </para>
/// <para>
/// <b>Merge-gate wiring</b> (identical pattern to <c>ResourcefulnessEvalSuiteTests</c> /
/// <c>OriginClassificationEvalSuiteTests</c>): the <c>Category=GoldenUtteranceEval</c> trait joins this
/// class to the dedicated <c>eval-gate</c> job in <c>.github/workflows/sdap-ci.yml</c>
/// (<c>dotnet test --filter "Category=GoldenUtteranceEval"</c>, NO <c>continue-on-error</c>) with zero
/// CI-YAML change — the trait IS the registration. It deliberately does NOT touch the shared
/// <c>golden-utterances.json</c> (whose closed §3-UC set + P1/P2 activation guards are owned by the
/// ai-architecture-redesign project); a net-new family is the established way a project adds gate coverage.
/// </para>
/// <para>
/// <b>catalogStatus grounding</b> (honest — no invented capability names): <c>existing</c> ⇒ a
/// <see cref="ConsumerTypes"/> constant; <c>mirrored</c> ⇒ a row in
/// <c>infra/dataverse/sprk_playbookconsumer-rows.json</c>; <c>live-catalog</c> ⇒ a capability seeded live
/// on spaarkedev1 whose mirror/constant parity is still pending (create-todo / create-project — the
/// surface-launch create capabilities have no server-side <c>ConsumerTypes</c> dependency, so they carry
/// no constant by design; the case must cite its <c>seededBy</c> task).
/// </para>
/// <para>
/// <b>Relationship to task 031</b>: <c>PreferenceNotPermissionInvariantTests</c> proves preference≠permission
/// STRUCTURALLY (reflection over <see cref="AgentToolFilterContext"/>) as a unit test OUTSIDE the gate.
/// <see cref="ProfileInjection_DoesNotFlipTheGroundedR1CapabilitySet_OperationalEvalInMergeGate"/> is the
/// OPERATIONAL proof over the R1 capability surface, INSIDE the merge gate — additive, not a copy.
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class AssistantEnhancementsR1EvalTests
{
    private readonly ITestOutputHelper _output;

    public AssistantEnhancementsR1EvalTests(ITestOutputHelper output) => _output = output;

    // -------------------------------------------------------------------------
    // Closed vocabularies (this family's case-schema contract)
    // -------------------------------------------------------------------------

    private static readonly IReadOnlySet<string> Families = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "create-todo", "create-project", "list-tasks", "view-vs-create", "profile-injection", "incoherent-combo",
    };

    private static readonly IReadOnlySet<string> Channels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text", "click" };

    private static readonly IReadOnlySet<string> OutcomeClasses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dispatch", "profile-neutral", "labels-only" };

    private static readonly IReadOnlySet<string> CatalogStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "existing", "mirrored", "live-catalog" };

    /// <summary>The two §3 UC trigger families the R1 capabilities derive from (closed subset).</summary>
    private static readonly IReadOnlySet<string> R1UcIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UC-H-1", "UC-B-6" };

    // Per-catalog-change coverage floors (AC1 — "each R1 catalog change has ≥1 eval case").
    private static readonly IReadOnlyDictionary<string, int> FamilyFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["create-todo"] = 2,
        ["create-project"] = 3,
        ["list-tasks"] = 4,
        ["view-vs-create"] = 6,
        ["profile-injection"] = 2,
        ["incoherent-combo"] = 1,
    };

    // -------------------------------------------------------------------------
    // Inventory integrity (AC1)
    // -------------------------------------------------------------------------

    [Fact]
    public void Suite_Loads_WithNamespacedUniqueCaseIds_AndClosedVocabularies()
    {
        var suite = LoadSuite();

        suite.SchemaVersion.Should().Be("assistant-r1-eval@v1", "the family declares its schema version");
        suite.Cases.Should().NotBeEmpty();
        suite.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability + CI diffs");
        suite.Cases.Should().OnlyContain(c => c.CaseId.StartsWith("AR1-", StringComparison.Ordinal),
            "R1 cases are namespaced AR1-### so they never collide with the golden GU-### ids");

        foreach (var c in suite.Cases)
        {
            Families.Should().Contain(c.Family, $"case {c.CaseId}: family is a closed vocabulary");
            Channels.Should().Contain(c.Channel, $"case {c.CaseId}: channel is text|click");
            OutcomeClasses.Should().Contain(c.Expected.OutcomeClass, $"case {c.CaseId}: outcome class is closed");
            R1UcIds.Should().Contain(c.UcId,
                $"case {c.CaseId}: ucId must trace to an R1 §3 UC trigger family (UC-H-1 task orchestration / UC-B-6 conversational creation)");
            c.Expected.ConsumerType.Should().NotBeNullOrWhiteSpace($"case {c.CaseId}: every R1 case names its expected capability");
            c.Expected.CatalogStatus.Should().NotBeNull($"case {c.CaseId}: every case declares its catalog-grounding status");
            CatalogStatuses.Should().Contain(c.Expected.CatalogStatus!, $"case {c.CaseId}: catalogStatus is a closed vocabulary");
        }
    }

    [Fact]
    public void EachR1CatalogChange_HasEvalCoverage_AtItsFloor()
    {
        var suite = LoadSuite();

        foreach (var (family, floor) in FamilyFloors)
        {
            suite.Cases.Count(c => string.Equals(c.Family, family, StringComparison.OrdinalIgnoreCase))
                .Should().BeGreaterOrEqualTo(floor,
                    $"AC1/NFR-06: the '{family}' R1 catalog change owes at least {floor} eval case(s) (owed-eval-cases.md)");
        }

        // view-vs-create must exercise BOTH directions (the authored disambiguation is symmetric).
        var viewVsCreate = suite.Cases.Where(c => string.Equals(c.Family, "view-vs-create", StringComparison.OrdinalIgnoreCase)).ToList();
        viewVsCreate.Should().Contain(c => c.Expected.ConsumerType == "list-tasks",
            "a VIEW-selecting disambiguation case must exist");
        viewVsCreate.Should().Contain(c => c.Expected.NotConsumerType == "list-tasks",
            "a case where a create/generic ask must NOT select list-tasks must exist");
    }

    // -------------------------------------------------------------------------
    // Catalog grounding (AC1) — no invented capability names; the surface-launch
    // disposition + the VIEW-vs-CREATE cue are grounded against the REAL catalog.
    // -------------------------------------------------------------------------

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
                    ConsumerTypes.All.Should().NotContain(consumerType,
                        $"case {c.CaseId}: '{consumerType}' is a surface-launch create capability with no server-side " +
                        "ConsumerTypes dependency — it must NOT be a constant (flip to existing only if a constant is added)");
                    c.Expected.SeededBy.Should().NotBeNullOrWhiteSpace(
                        $"case {c.CaseId}: a live-catalog capability MUST cite the task that seeded it on spaarkedev1 " +
                        "(honest grounding — no invented capability names)");
                    break;
            }

            if (c.Expected.NotConsumerType is not null)
            {
                c.Expected.NotConsumerType.Should().NotBe(consumerType,
                    $"case {c.CaseId}: the disambiguation target and the expected capability must differ");
            }
        }
    }

    /// <summary>
    /// The VIEW-vs-CREATE distinction is made by ADR-039 ambiguity-in-descriptions, NOT a classifier.
    /// Ground that mechanism against the REAL list-tasks Binding row: it declares the surface_launch
    /// disposition and its authored tool description names create-task + create-todo as the DISTINCT
    /// create capabilities and declares itself read-only. A catalog edit that drops the cue fails here.
    /// </summary>
    [Fact]
    public void ListTasksBindingRow_DeclaresSurfaceLaunch_AndCarriesTheViewVsCreateCue()
    {
        var mirrorPath = Path.Combine(FindRepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json");
        using var mirror = JsonDocument.Parse(File.ReadAllText(mirrorPath));

        var row = mirror.RootElement.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("consumerType").GetString() == "list-tasks"
                         && r.GetProperty("consumerCode").GetString() == "default");

        row.GetProperty("enabled").GetBoolean().Should().BeTrue("the list-tasks Binding is live");
        row.GetProperty("disposition").GetInt32().Should().Be((int)BindingDisposition.SurfaceLaunch,
            "list-tasks dispatches a surface_launch SSE (100000007) that opens the My Tasks workspace grid tab — " +
            "no server-side write, no BFF query handler (E-050-11)");
        row.GetProperty("risk").GetInt32().Should().Be((int)BindingRisk.None,
            "a read-only VIEW never gates a confirmation turn (E-050-12)");
        row.GetProperty("surfaces").GetString().Should().Contain("assistant",
            "list-tasks is agent-selectable in the chat loop");

        var toolDescription = row.GetProperty("toolDescription").GetString();
        toolDescription.Should().NotBeNullOrWhiteSpace();
        toolDescription!.Should().ContainAll(new[] { "create-task", "create-todo" },
            "the authored disambiguation cue names BOTH create capabilities as DISTINCT from the VIEW capability " +
            "(ADR-039: ambiguity is authored into tool descriptions, not a second decider)");
        toolDescription.Should().MatchRegex("(?i)do not use|read-only|read only",
            "the cue tells the model NOT to use list-tasks to create, and that it is read-only");
    }

    // -------------------------------------------------------------------------
    // AC2 — profile injection does NOT flip dispatch (operational, in the gate)
    // -------------------------------------------------------------------------

    [Fact]
    public void ProfileInjection_DoesNotFlipTheGroundedR1CapabilitySet_OperationalEvalInMergeGate()
    {
        var suite = LoadSuite();
        var profileInjectionCases = suite.Cases
            .Where(c => string.Equals(c.Family, "profile-injection", StringComparison.OrdinalIgnoreCase))
            .ToList();
        profileInjectionCases.Should().NotBeEmpty("FR-E4 owes at least one operational profile-injection case");
        profileInjectionCases.Should().OnlyContain(c => c.Expected.OutcomeClass == "profile-neutral",
            "profile-injection cases assert dispatch NEUTRALITY, not a positive selection");

        // Real R1 capability tools on the Assistant surface, plus a record-form-only decoy that grounding
        // must exclude on BOTH the profiled and unprofiled sides identically.
        var createTaskTool = BuildCapabilityTool("create-task", "assistant");
        var listTasksTool = BuildCapabilityTool("list-tasks", "assistant");
        var recordFormOnlyTool = BuildCapabilityTool("chat-record-form-only", "record-form");
        var tools = new AIFunction[] { createTaskTool, listTasksTool, recordFormOnlyTool };

        // Two materially different STATED profiles + the unprofiled caller. AgentToolFilterContext accepts
        // NO profile parameter (proven structurally in task 031), so a builder that "consults" the profile
        // has nothing to consult — the contexts are record-equal and the grounded sets are byte-identical.
        var profileA = new StatedProfile
        {
            RoleLabel = "Partner",
            PracticeAreaNames = new[] { "Litigation" },
            OfficeLocation = "New York",
            AssistantPreferences = "Concise; cite sources",
        };
        var profileB = new StatedProfile
        {
            RoleLabel = "Paralegal",
            PracticeAreaNames = new[] { "Corporate", "Real Estate" },
            OfficeLocation = "London",
            AssistantPreferences = "Detailed",
        };

        static AgentToolFilterContext BuildContextIgnoringProfile(StatedProfile? _) =>
            new(AgentToolFilterContext.AssistantSurface,
                HasSessionFiles: true, HasActiveDocument: false, HasAnalysisBinding: false);

        var groundedA = Ground(tools, BuildContextIgnoringProfile(profileA));
        var groundedB = Ground(tools, BuildContextIgnoringProfile(profileB));
        var groundedNone = Ground(tools, BuildContextIgnoringProfile(null));

        groundedA.Should().Equal(groundedNone,
            "the grounded R1 capability set for a profiled caller (A) is identical to the unprofiled caller — " +
            "preference never grants or revokes a capability (ADR-039 preference≠permission)");
        groundedB.Should().Equal(groundedNone,
            "a materially different profile (B) yields the same grounded set — the profile has no channel into grounding");
        groundedNone.Should().Equal(new[] { createTaskTool.Name, listTasksTool.Name },
            "grounding admits the Assistant-surface R1 capabilities and excludes the record-form-only tool on ALL " +
            "sides identically — the exact create-task/list-tasks selection the profile-injection cases assert stays put");

        _output.WriteLine("AC2 operational profile-injection non-flip (in merge gate):");
        foreach (var c in profileInjectionCases)
        {
            groundedNone.Should().Contain($"capability_{c.Expected.ConsumerType}",
                $"case {c.CaseId}: '{c.Expected.ConsumerType}' is in the grounded set regardless of profile");
            _output.WriteLine($"  {c.CaseId}  \"{c.Utterance}\"  ->  {c.Expected.ConsumerType} (profile-neutral)");
        }
    }

    // -------------------------------------------------------------------------
    // AC3 — the incoherent practice-area × matter-type case cannot commit
    // -------------------------------------------------------------------------

    /// <summary>
    /// Task 011 closed SATISFIED-BY-DESIGN: matter-type is not FK-constrained by practice-area, so there is
    /// no incoherent GUID pair to prevent at the data layer. The operational proof of "cannot commit an
    /// incoherent combo" is structural: CREATE-MATTER@v1 emits practice_area_suggestion + matter_type_suggestion
    /// as INDEPENDENT string LABELS (never enum/const/GUID), with additionalProperties=false, and the Action is
    /// allowstools=false — so the model emits NO resolved closed-set value at all; each ref is resolved
    /// deterministically per-field downstream (task 010 resolver). A schema edit that turned either field into a
    /// closed enum, or coupled the two, would fail here.
    /// </summary>
    [Fact]
    public void IncoherentPracticeAreaMatterType_CannotCommit_ModelEmitsIndependentLabelsNotClosedSetValues()
    {
        var suite = LoadSuite();
        suite.Cases.Should().Contain(
            c => string.Equals(c.Family, "incoherent-combo", StringComparison.OrdinalIgnoreCase)
                 && c.Expected.OutcomeClass == "labels-only" && c.Expected.ConsumerType == "create-matter",
            "AC3: the incoherent practice-area × matter-type case is authored (create-matter, labels-only)");

        var schemaPath = Path.Combine(FindRepoRoot(), "infra", "dataverse", "outputschemas", "create-matter-v1.schema.json");
        File.Exists(schemaPath).Should().BeTrue(
            $"the CREATE-MATTER@v1 output-schema mirror must exist at {schemaPath}");
        using var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var props = doc.RootElement.GetProperty("properties");

        foreach (var field in new[] { "practice_area_suggestion", "matter_type_suggestion" })
        {
            var prop = props.GetProperty(field);
            prop.GetProperty("type").GetString().Should().Be("string",
                $"{field} is a free-text LABEL — the model never emits a closed-set value here");
            prop.TryGetProperty("enum", out _).Should().BeFalse(
                $"{field} MUST NOT be a closed enum — if it were, the model could emit an incoherent closed-set member");
            prop.TryGetProperty("const", out _).Should().BeFalse($"{field} is a best-guess label, never a fixed value");
            prop.GetProperty("description").GetString().Should().Contain("LABEL",
                $"{field}'s description pins the label-not-GUID contract");
        }

        doc.RootElement.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            "structured output is closed — the model cannot smuggle in a resolved practicearea/mattertype GUID field");

        // Non-vacuous guard: prove the two fields are INDEPENDENT — there is no compound
        // practice-area×matter-type property that could encode (and thus commit) a coupled pair.
        var propertyNames = props.EnumerateObject().Select(p => p.Name).ToList();
        propertyNames.Should().NotContain(n => n.Contains("practicearea", StringComparison.OrdinalIgnoreCase)
                                               && n.Contains("mattertype", StringComparison.OrdinalIgnoreCase),
            "no compound field couples the two suggestions — each is resolved independently per-field (task 010), " +
            "so an incoherent pair has no representation the model could emit or the write could commit");

        _output.WriteLine("AC3 incoherent-combo cannot commit — CREATE-MATTER@v1 emits independent string LABELS, " +
                          "allowstools=false, additionalProperties=false; no closed-set value is ever model-emitted.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static List<string> Ground(IEnumerable<AIFunction> tools, AgentToolFilterContext context) =>
        AgentToolProjection.PreFilter(tools, context)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    private static BindingCapabilityTool BuildCapabilityTool(string consumerType, string surface) =>
        new(new Binding
            {
                BindingId = Guid.NewGuid(),
                ConsumerType = consumerType,
                ToolDescription = $"{consumerType} capability (R1 eval fixture).",
                Surfaces = new[] { surface },
            },
            new ServiceCollection().BuildServiceProvider(),
            "tenant-eval", "session-eval",
            NullLogger.Instance);

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

    private static AssistantR1EvalSuite LoadSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "assistant-r1-eval-cases.json");
        File.Exists(path).Should().BeTrue(
            $"assistant-r1-eval-cases.json must be copied to test output at {path} " +
            "(Content include in Sprk.Bff.Api.Tests.csproj — the contract-Eval ItemGroup)");
        var suite = JsonSerializer.Deserialize<AssistantR1EvalSuite>(File.ReadAllText(path), JsonOptions);
        suite.Should().NotBeNull("the seed file must deserialize into the R1 case schema");
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
            "the R1 eval catalog-mirror assertions require an in-repo test run.");
    }
}

// -----------------------------------------------------------------------------
// R1 eval case schema
// -----------------------------------------------------------------------------

public sealed record AssistantR1EvalSuite
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("cases")]
    public List<AssistantR1EvalCase> Cases { get; init; } = new();
}

public sealed record AssistantR1EvalCase
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
    public AssistantR1Expected Expected { get; init; } = new();
}

public sealed record AssistantR1Expected
{
    /// <summary>dispatch | profile-neutral | labels-only (closed set).</summary>
    [JsonPropertyName("outcomeClass")]
    public string OutcomeClass { get; init; } = string.Empty;

    [JsonPropertyName("consumerType")]
    public string? ConsumerType { get; init; }

    /// <summary>The capability this utterance must NOT select (disambiguation cases).</summary>
    [JsonPropertyName("notConsumerType")]
    public string? NotConsumerType { get; init; }

    /// <summary>existing (ConsumerTypes constant) | mirrored (in the rows mirror) | live-catalog (seeded, parity pending).</summary>
    [JsonPropertyName("catalogStatus")]
    public string? CatalogStatus { get; init; }

    /// <summary>For live-catalog capabilities: the task that seeded the Binding/Action on spaarkedev1.</summary>
    [JsonPropertyName("seededBy")]
    public string? SeededBy { get; init; }
}
