using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Eval.GoldenUtterances;

/// <summary>
/// Golden-utterance eval suite — FR-P0-09 scaffold
/// (<c>spaarke-ai-architecture-redesign-r1</c> task 011).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is</b>: the quality spine for the AI architecture redesign. Each
/// case in <c>tests/integration/contract/Eval/golden-utterances.json</c> is
/// {utterance, §3 UC id, expected binding/capability, expected outcome class
/// (dispatch / clarify / refuse), optional schema-conformance + citation-integrity
/// assertions}. Cases are DATA — business analysts add or edit cases without any
/// code change (the G-M maker-gate story), and CI validates the inventory on every
/// PR that touches it.
/// </para>
/// <para>
/// <b>What runs at P0</b> (this task): inventory integrity (≥30 seed cases, unique
/// ids, §3 UC traceability, closed outcome-class/channel vocabularies), grounding
/// of expected consumer types against the REAL closed catalog
/// (<see cref="ConsumerTypes.All"/>), NFR-06 case-schema round-trip for the
/// schema-conformance + citation-integrity extension points, and a routing-surface
/// smoke that drives the real <see cref="ConsumerRoutingService"/> selection
/// algorithm (Dataverse boundary stubbed) for every existing consumer type the
/// suite references. There is NO dispatch loop at P0 — utterance→binding dispatch
/// assertions are explicitly declared pending with their activating task
/// (see <see cref="PendingDispatchAssertions_AreExplicitlyDeclaredWithActivation_NotSilentlySkipped"/>);
/// they are NOT silently skipped tests.
/// </para>
/// <para>
/// <b>Merge-gate wiring (NFR-02)</b>: this class compiles into
/// <c>Sprk.Bff.Api.Tests</c> via the contract-path Compile glob and therefore runs
/// inside the root <c>dotnet test</c> of <c>.github/workflows/sdap-ci.yml</c>
/// (build-test job, pass 1) on every PR. ACTIVATION as a blocking gate happens at
/// P1 task 026 (FR-P1-07): flip the informational <c>continue-on-error</c> posture
/// for the eval subset — the <c>[Trait("Category", "GoldenUtteranceEval")]</c>
/// filter exists precisely so 026 can add a dedicated required step
/// (<c>dotnet test --filter "Category=GoldenUtteranceEval"</c>) without
/// restructuring the workflow. Full activation plan:
/// <c>tests/integration/contract/Eval/README.md</c>.
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class GoldenUtteranceEvalSuiteTests
{
    private readonly ITestOutputHelper _output;

    public GoldenUtteranceEvalSuiteTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // -------------------------------------------------------------------------
    // Closed vocabularies (P0 contract of the case schema)
    // -------------------------------------------------------------------------

    private static readonly IReadOnlySet<string> OutcomeClasses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dispatch", "clarify", "refuse" };

    /// <summary>
    /// The redesign's technical constraint: every AI invocation routes through
    /// Event / Click / Text — nothing else. The eval schema enforces the same
    /// closed set so no case can describe a fourth invocation route.
    /// </summary>
    private static readonly IReadOnlySet<string> Channels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text", "click", "event" };

    /// <summary>
    /// Canonical §3 UC ids from
    /// <c>docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md</c>
    /// (families A document intelligence, B matter workflow, C interactive Q&amp;A,
    /// D proactive/scheduled, E content generation, F data enrichment,
    /// G composition, H task orchestration). Update alongside the canonical doc.
    /// </summary>
    private static readonly IReadOnlySet<string> CanonicalUcIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UC-A-1", "UC-A-2", "UC-A-3", "UC-A-4", "UC-A-5", "UC-A-6", "UC-A-7",
        "UC-B-1", "UC-B-2", "UC-B-3", "UC-B-4", "UC-B-5",
        "UC-C-1", "UC-C-2", "UC-C-3", "UC-C-4",
        "UC-D-1", "UC-D-2", "UC-D-3", "UC-D-4",
        "UC-E-1", "UC-E-2", "UC-E-3",
        "UC-F-1", "UC-F-2",
        "UC-G-1", "UC-G-2", "UC-G-3", "UC-G-4",
        "UC-H-1", "UC-H-2", "UC-H-3", "UC-H-4", "UC-H-5",
    };

    private static readonly IReadOnlySet<string> DispatchAssertPhases =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "P1", "P2", "P3" };

    // -------------------------------------------------------------------------
    // Inventory integrity (P0-active assertions)
    // -------------------------------------------------------------------------

    [Fact]
    public void Suite_Loads_WithAtLeast30SeedCases_AndUniqueCaseIds()
    {
        var suite = LoadSuite();

        suite.SchemaVersion.Should().NotBeNullOrWhiteSpace("the seed file declares its schema version");
        suite.Cases.Should().HaveCountGreaterOrEqualTo(30,
            "FR-P0-09 acceptance: ~30 seed utterances derived from §3 UC triggers");

        suite.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability and CI diffs");
        suite.Cases.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Utterance),
            "every case carries an utterance (for click/event channels: the affordance/event descriptor)");
    }

    [Fact]
    public void EveryCase_TracesToCanonicalSection3UcId()
    {
        var suite = LoadSuite();

        foreach (var c in suite.Cases)
        {
            CanonicalUcIds.Should().Contain(c.UcId,
                $"case {c.CaseId} must trace to a §3 UC trigger id in the canonical architecture doc");
        }
    }

    [Fact]
    public void EveryCase_DeclaresChannel_OutcomeClass_AndActivation()
    {
        var suite = LoadSuite();

        foreach (var c in suite.Cases)
        {
            Channels.Should().Contain(c.Channel,
                $"case {c.CaseId}: every AI invocation routes through Event / Click / Text — nothing else");
            OutcomeClasses.Should().Contain(c.Expected.OutcomeClass,
                $"case {c.CaseId}: outcome class is a closed vocabulary");
            DispatchAssertPhases.Should().Contain(c.Activation.DispatchAssertPhase,
                $"case {c.CaseId}: every case declares WHEN its dispatch assertion goes live");
            c.Activation.ActivatedBy.Should().NotBeNullOrWhiteSpace(
                $"case {c.CaseId}: pending-by-design requires naming the activating task/wave");
        }
    }

    [Fact]
    public void DispatchCases_ExpectedConsumerTypes_AreGroundedInClosedCatalogOrNamedFr()
    {
        var suite = LoadSuite();

        foreach (var c in suite.Cases)
        {
            switch (c.Expected.OutcomeClass.ToLowerInvariant())
            {
                case "dispatch":
                    c.Expected.ConsumerType.Should().NotBeNullOrWhiteSpace(
                        $"case {c.CaseId}: dispatch cases must name their expected capability binding");
                    if (string.Equals(c.Expected.CatalogStatus, "existing", StringComparison.OrdinalIgnoreCase))
                    {
                        // Grounding against the REAL closed catalog: renaming or removing a
                        // consumer type in ConsumerTypes.cs fails the eval inventory here.
                        ConsumerTypes.All.Should().Contain(c.Expected.ConsumerType,
                            $"case {c.CaseId}: consumer type '{c.Expected.ConsumerType}' is declared " +
                            "catalogStatus=existing, so it must be a member of ConsumerTypes.All");
                    }
                    else
                    {
                        c.Expected.CatalogStatus.Should().Be("planned",
                            $"case {c.CaseId}: catalogStatus is a closed existing|planned vocabulary for dispatch cases");
                        c.Expected.PlannedBy.Should().NotBeNullOrWhiteSpace(
                            $"case {c.CaseId}: planned consumer types must cite the FR that introduces them " +
                            "(closed-catalog doctrine — no invented capability names)");
                        ConsumerTypes.All.Should().NotContain(c.Expected.ConsumerType,
                            $"case {c.CaseId}: '{c.Expected.ConsumerType}' is declared planned but already exists " +
                            "in ConsumerTypes.All — flip catalogStatus to existing");
                    }

                    break;

                case "clarify":
                case "refuse":
                    c.Expected.ConsumerType.Should().BeNull(
                        $"case {c.CaseId}: clarify/refuse outcomes resolve no binding");
                    break;
            }
        }
    }

    /// <summary>
    /// NFR-06 forward-compat: the case schema must carry per-capability
    /// schema-conformance + citation-integrity assertion slots so P2+ families
    /// extend the DATA, not the schema. This round-trips both extension points.
    /// </summary>
    [Fact]
    public void CaseSchema_SupportsSchemaConformanceAndCitationIntegrityAssertions()
    {
        var suite = LoadSuite();

        var schemaCases = suite.Cases
            .Where(c => c.Assertions?.SchemaConformance is not null)
            .ToList();
        schemaCases.Should().NotBeEmpty(
            "NFR-06: at least the chat-summarize family declares its output schema (SUM-CHAT@v1 per FR-P1-01)");
        schemaCases.Should().OnlyContain(
            c => !string.IsNullOrWhiteSpace(c.Assertions!.SchemaConformance),
            "declared schema-conformance refs must be non-empty schema ids");

        suite.Cases.Should().Contain(c => c.Assertions != null && c.Assertions.CitationIntegrity == true,
            "NFR-06: citation-integrity assertion slot must be exercised by at least one grounded-answer family");
    }

    // -------------------------------------------------------------------------
    // Routing-surface smoke (P0-active): the harness can drive the REAL
    // ConsumerRoutingService selection algorithm for every existing consumer
    // type the suite references. This is the layer the P1 dispatch assertions
    // plug into — no dispatcher is invented here.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RoutingSurface_ResolveBindingAsync_ResolvesEveryExistingConsumerTypeInSuite()
    {
        var suite = LoadSuite();
        var existingConsumerTypes = suite.Cases
            .Where(c => string.Equals(c.Expected.CatalogStatus, "existing", StringComparison.OrdinalIgnoreCase)
                        && c.Expected.ConsumerType is not null)
            .Select(c => c.Expected.ConsumerType!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        existingConsumerTypes.Should().NotBeEmpty("the seed set covers capabilities that exist today");

        // Real service; Dataverse boundary stubbed with one enabled binding row
        // per requested consumer type (mirrors the sprk_playbookconsumer rows the
        // seed script provisions). ADR-038: module-boundary stub, no
        // Mock<HttpMessageHandler>, no DI-registration assertions.
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                var requestedType = query.Criteria.Conditions
                    .First(c => c.AttributeName == "sprk_consumertype")
                    .Values[0] as string;
                return new EntityCollection(new List<Entity> { BuildConsumerRow(requestedType!) });
            });

        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(e => e.EnvironmentName).Returns("Development");

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var routing = new ConsumerRoutingService(
            entityService.Object,
            cache,
            hostEnvironment.Object,
            NullLogger<ConsumerRoutingService>.Instance);

        foreach (var consumerType in existingConsumerTypes)
        {
            var binding = await routing.ResolveBindingAsync(consumerType);

            binding.Should().NotBeNull(
                $"an enabled catalog row for '{consumerType}' must resolve through the FR-1R-03 selection algorithm");
            binding!.ConsumerType.Should().Be(consumerType);
            binding.PlaybookId.Should().NotBeNull("the stub row targets a playbook");
            _output.WriteLine($"resolved  {consumerType,-24} -> binding {binding.BindingId}");
        }
    }

    // -------------------------------------------------------------------------
    // Pending-by-design declaration (P0): dispatch assertions are stubbed, not
    // silently skipped. This test PASSES while making the pending inventory
    // visible in the run output with its activating task per family.
    // -------------------------------------------------------------------------

    [Fact]
    public void PendingDispatchAssertions_AreExplicitlyDeclaredWithActivation_NotSilentlySkipped()
    {
        var suite = LoadSuite();

        _output.WriteLine("PENDING dispatch-assertion inventory (P0 scaffold — no dispatch loop exists yet):");
        _output.WriteLine("");
        _output.WriteLine($"{"family",-22} {"cases",5}  {"phase",-6} activated by");
        _output.WriteLine(new string('-', 90));

        foreach (var group in suite.Cases
                     .GroupBy(c => c.Family, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Min(c => c.Activation.DispatchAssertPhase), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var phases = string.Join(",", group
                .Select(c => c.Activation.DispatchAssertPhase)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            var activatedBy = group
                .Select(c => c.Activation.ActivatedBy)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .First();
            _output.WriteLine($"{group.Key,-22} {group.Count(),5}  {phases,-6} {activatedBy}");
        }

        // The P1 merge-gate task (026) greens the UC-A-1 chat-summarize family
        // first — assert the seed data agrees so 026 finds its cases waiting.
        var chatSummarize = suite.Cases
            .Where(c => string.Equals(c.Family, "chat-summarize", StringComparison.OrdinalIgnoreCase))
            .ToList();
        chatSummarize.Should().NotBeEmpty("chat-summarize is the first proving capability (FR-P1-01)");
        chatSummarize.Should().OnlyContain(
            c => string.Equals(c.Activation.DispatchAssertPhase, "P1", StringComparison.OrdinalIgnoreCase),
            "the UC-A-1 family activates with the P1 merge gate (task 026)");

        // Refusal + prompt-injection families extend at P2 (FR-P2-08, task 037).
        suite.Cases
            .Where(c => c.Expected.OutcomeClass.Equals("refuse", StringComparison.OrdinalIgnoreCase))
            .Should().NotBeEmpty("P0 seeds refusal cases so task 037 extends an existing family")
            .And.OnlyContain(
                c => string.Equals(c.Activation.DispatchAssertPhase, "P2", StringComparison.OrdinalIgnoreCase),
                "refusal outcomes require the P2 loop + content-safety path");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static GoldenUtteranceSuite LoadSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "golden-utterances.json");
        File.Exists(path).Should().BeTrue(
            $"golden-utterances.json must be copied to test output at {path} " +
            "(Content include in Sprk.Bff.Api.Tests.csproj — see the contract-Eval ItemGroup)");

        var suite = JsonSerializer.Deserialize<GoldenUtteranceSuite>(File.ReadAllText(path), JsonOptions);
        suite.Should().NotBeNull("the seed file must deserialize into the case schema");
        return suite!;
    }

    /// <summary>
    /// One enabled <c>sprk_playbookconsumer</c> row shaped like the rows
    /// provisioned by <c>scripts/dataverse/Seed-PlaybookConsumers.ps1</c>:
    /// default consumer code, wildcard environment, playbook target. task-003
    /// Binding columns are intentionally left null to exercise the documented
    /// legacy-row safe defaults.
    /// </summary>
    private static Entity BuildConsumerRow(string consumerType)
    {
        var entity = new Entity("sprk_playbookconsumer", Guid.NewGuid());
        entity["sprk_playbookconsumerid"] = entity.Id;
        entity["sprk_consumertype"] = consumerType;
        entity["sprk_consumercode"] = "default";
        entity["sprk_priority"] = 100;
        entity["sprk_enabled"] = true;
        entity["sprk_playbook"] = new EntityReference("sprk_playbook", Guid.NewGuid());
        return entity;
    }
}

// -----------------------------------------------------------------------------
// Case schema (NFR-06: schema-conformance + citation-integrity slots are
// first-class so P2+ families extend data, not code)
// -----------------------------------------------------------------------------

public sealed record GoldenUtteranceSuite
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("cases")]
    public List<GoldenUtteranceCase> Cases { get; init; } = new();
}

public sealed record GoldenUtteranceCase
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    /// <summary>Capability family for reporting/grouping (e.g. chat-summarize, refusal).</summary>
    [JsonPropertyName("family")]
    public string Family { get; init; } = string.Empty;

    /// <summary>§3 UC trigger id in the canonical architecture doc (traceability).</summary>
    [JsonPropertyName("ucId")]
    public string UcId { get; init; } = string.Empty;

    /// <summary>Invocation route: text | click | event (closed set).</summary>
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    /// <summary>The utterance (text channel) or affordance/event descriptor (click/event).</summary>
    [JsonPropertyName("utterance")]
    public string Utterance { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("context")]
    public GoldenUtteranceContext? Context { get; init; }

    [JsonPropertyName("expected")]
    public GoldenUtteranceExpected Expected { get; init; } = new();

    [JsonPropertyName("assertions")]
    public GoldenUtteranceAssertions? Assertions { get; init; }

    [JsonPropertyName("activation")]
    public GoldenUtteranceActivation Activation { get; init; } = new();
}

public sealed record GoldenUtteranceContext
{
    [JsonPropertyName("surface")]
    public string? Surface { get; init; }

    [JsonPropertyName("sessionHasDocument")]
    public bool? SessionHasDocument { get; init; }

    [JsonPropertyName("recordType")]
    public string? RecordType { get; init; }
}

public sealed record GoldenUtteranceExpected
{
    /// <summary>dispatch | clarify | refuse (closed set).</summary>
    [JsonPropertyName("outcomeClass")]
    public string OutcomeClass { get; init; } = string.Empty;

    /// <summary>Expected capability binding key; null for clarify/refuse.</summary>
    [JsonPropertyName("consumerType")]
    public string? ConsumerType { get; init; }

    [JsonPropertyName("consumerCode")]
    public string? ConsumerCode { get; init; }

    /// <summary>existing (validated against ConsumerTypes.All) | planned (must cite plannedBy FR).</summary>
    [JsonPropertyName("catalogStatus")]
    public string? CatalogStatus { get; init; }

    [JsonPropertyName("plannedBy")]
    public string? PlannedBy { get; init; }
}

public sealed record GoldenUtteranceAssertions
{
    /// <summary>Output schema id the capability's result must conform to (e.g. SUM-CHAT@v1). Asserted from P1/P2.</summary>
    [JsonPropertyName("schemaConformance")]
    public string? SchemaConformance { get; init; }

    /// <summary>When true, the rendered answer's citations must resolve to real grounded sources. Asserted from P2.</summary>
    [JsonPropertyName("citationIntegrity")]
    public bool? CitationIntegrity { get; init; }
}

public sealed record GoldenUtteranceActivation
{
    /// <summary>Phase at which this case's dispatch assertion goes live: P1 | P2 | P3.</summary>
    [JsonPropertyName("dispatchAssertPhase")]
    public string DispatchAssertPhase { get; init; } = string.Empty;

    /// <summary>The task/wave (and FR) that activates the assertion — pending-by-design, never silently skipped.</summary>
    [JsonPropertyName("activatedBy")]
    public string ActivatedBy { get; init; } = string.Empty;
}
