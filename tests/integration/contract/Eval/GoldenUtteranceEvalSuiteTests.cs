using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
/// <b>P1 ACTIVATED (task 026, FR-P1-07)</b>: the UC-A-1 families' P1 dispatch
/// assertions are LIVE (see the "P1 live dispatch assertions" region) — they
/// drive the REAL <see cref="ConsumerRoutingService"/> reads the three entry
/// paths use at P1 (<c>ResolveBindingAsync</c> for the Text-path summarize
/// boundary, <c>ResolveEventBindingsAsync</c> for the Event path) plus the
/// SUM-CHAT@v1 schema-conformance contract (NFR-06). Typo-tolerant NL matching
/// ("any phrasing of summarize") is a property of the bounded function-calling
/// loop and activates at P2 (task 037, FR-P2-08) — at P1 the utterances are
/// the traceability record; the assertable dispatch behavior is
/// utterance-family → Binding → Action resolution through the closed catalog.
/// </para>
/// <para>
/// <b>Merge-gate wiring (NFR-02 — ACTIVE since task 026)</b>: this class
/// compiles into <c>Sprk.Bff.Api.Tests</c> via the contract-path Compile glob
/// and runs inside the root <c>dotnet test</c> of
/// <c>.github/workflows/sdap-ci.yml</c> (build-test job, pass 1) on every PR.
/// In addition, the dedicated <c>eval-gate</c> job runs
/// <c>dotnet test --filter "Category=GoldenUtteranceEval"</c> with NO
/// <c>continue-on-error</c> — a red eval suite fails the workflow (the
/// build-test job's job-level informational posture cannot swallow it). See
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
                    AssertConsumerTypeGrounding(c);
                    break;

                case "clarify":
                    // P2 evolution (tasks 032/033, 2026-07-06): loop-native elicitation
                    // (FR-P2-03) makes clarify a capability-TARGETED outcome — a clarify
                    // case MAY name the capability whose declared required args are being
                    // elicited (conversational or capture_mode:modal). When it does, the
                    // same closed-catalog grounding as dispatch applies. Cases with no
                    // target (M4 event confirmation) keep consumerType null.
                    if (c.Expected.ConsumerType is not null)
                    {
                        AssertConsumerTypeGrounding(c);
                    }

                    break;

                case "refuse":
                    c.Expected.ConsumerType.Should().BeNull(
                        $"case {c.CaseId}: refuse outcomes resolve no user capability — the tenant's " +
                        "no_match_handler Binding renders the refusal server-side (FR-P2-04); the CASE " +
                        "declares no target");
                    break;
            }
        }
    }

    /// <summary>
    /// Closed-catalog grounding for a case's expected consumer type (dispatch cases
    /// always; clarify cases when they name the capability under elicitation):
    /// <c>existing</c> must be a member of <see cref="ConsumerTypes.All"/>;
    /// <c>planned</c> must cite the introducing FR and must NOT already exist.
    /// </summary>
    private static void AssertConsumerTypeGrounding(GoldenUtteranceCase c)
    {
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
                $"case {c.CaseId}: catalogStatus is a closed existing|planned vocabulary for capability-targeted cases");
            c.Expected.PlannedBy.Should().NotBeNullOrWhiteSpace(
                $"case {c.CaseId}: planned consumer types must cite the FR that introduces them " +
                "(closed-catalog doctrine — no invented capability names)");
            ConsumerTypes.All.Should().NotContain(c.Expected.ConsumerType,
                $"case {c.CaseId}: '{c.Expected.ConsumerType}' is declared planned but already exists " +
                "in ConsumerTypes.All — flip catalogStatus to existing");
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

        var routing = CreateRoutingService(entityService);

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
    // P1 live dispatch assertions (task 026, FR-P1-07) — the UC-A-1 families.
    //
    // What "live" means at P1 (no NL loop until P2): every P1-phase case's
    // dispatch route is exercised against the REAL routing reads the shipped
    // entry paths use — no dispatcher is invented here:
    //   • Text path  — SessionSummarizeOrchestrator resolves chat-summarize via
    //     IConsumerRoutingService.ResolveBindingAsync and fail-fasts unless the
    //     Binding targets a Prompted Action (SUM-CHAT@v1). These tests assert
    //     exactly those preconditions through the real selection algorithm.
    //   • Event path — EventRulesService resolves document_uploaded members via
    //     ResolveEventBindingsAsync (chat-classify order 1 → chat-summarize
    //     order 2). These tests assert the ordered membership resolution.
    //   • M4 clarify — the low-confidence confirmation policy dial (the
    //     surviving D1 dial) is pinned; the suspend behavior itself is proven
    //     in EventRulesServiceTests (task 022).
    // Dataverse is stubbed at the module boundary with rows shaped like the
    // seeded spaarkedev1 catalog rows (ADR-038: no Mock<HttpMessageHandler>,
    // no DI-registration assertions).
    // -------------------------------------------------------------------------

    /// <summary>The SUM-CHAT@v1 <c>sprk_analysisaction</c> target the stub catalog rows point at.</summary>
    private static readonly Guid SumChatActionId = new("eeb05bfd-1260-f111-ab0b-70a8a59455f4");

    /// <summary>The CLS-CHAT@v1 <c>sprk_analysisaction</c> target (task 022 event-rule member 1).</summary>
    private static readonly Guid ClsChatActionId = new("aaaa1111-0022-4022-9022-000000000022");

    [Fact]
    public async Task P1LiveDispatch_TextChatSummarizeCases_ResolveBindingToPromptedSumChatAction()
    {
        var suite = LoadSuite();
        var cases = suite.Cases
            .Where(c => IsPhase(c, "P1") && IsOutcome(c, "dispatch") && IsChannel(c, "text"))
            .ToList();

        cases.Should().NotBeEmpty("FR-P1-07: the UC-A-1 family carries P1 text-channel dispatch cases");
        cases.Should().OnlyContain(
            c => string.Equals(c.Family, "chat-summarize", StringComparison.OrdinalIgnoreCase),
            "chat-summarize is the ONLY live text-dispatch family at P1 (FR-P1-01); a new P1 text " +
            "family needs its own live assertion before declaring dispatchAssertPhase=P1");

        // Dataverse boundary stub shaped like the seeded spaarkedev1 row: enabled
        // chat-summarize Binding, sprk_action lookup → SUM-CHAT@v1, kind Prompted.
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                var requestedType = query.Criteria.Conditions
                    .First(c => c.AttributeName == "sprk_consumertype")
                    .Values[0] as string;
                return new EntityCollection(new List<Entity>
                {
                    BuildActionTargetRow(requestedType!, SumChatActionId, ucid: "UC-A-1"),
                });
            });

        var routing = CreateRoutingService(entityService);

        _output.WriteLine("P1 LIVE dispatch assertions — Text path (UC-A-1 chat-summarize family):");
        foreach (var c in cases)
        {
            var binding = await routing.ResolveBindingAsync(c.Expected.ConsumerType!, c.Expected.ConsumerCode);

            binding.Should().NotBeNull(
                $"case {c.CaseId} (\"{c.Utterance}\"): the chat-summarize Binding must resolve through " +
                "the real FR-1R-03 selection algorithm — this is the exact read SessionSummarizeOrchestrator performs");
            binding!.ConsumerType.Should().Be(ConsumerTypes.ChatSummarize,
                $"case {c.CaseId}: UC-A-1 text dispatch lands on the chat-summarize capability binding");
            binding.ActionId.Should().Be(SumChatActionId,
                $"case {c.CaseId}: the Binding's sprk_action lookup targets the SUM-CHAT@v1 Action — " +
                "SessionSummarizeOrchestrator fail-fasts (no engine/config fallback, NFR-08) without it");
            binding.ActionKind.Should().Be(ActionKind.Prompted,
                $"case {c.CaseId}: the chat /summarize path executes prompted Actions only (FR-P1-01)");
            binding.Ucid.Should().Be("UC-A-1",
                $"case {c.CaseId}: the seeded Binding row carries the §3 UC id, tying catalog data to this eval family");

            // NFR-06 — every UC-A-1 dispatch case pins its output schema id.
            c.Assertions.Should().NotBeNull(
                $"case {c.CaseId}: P1 dispatch cases must carry a schema-conformance assertion (NFR-06)");
            c.Assertions!.SchemaConformance.Should().Be("SUM-CHAT@v1",
                $"case {c.CaseId}: P1 schema-conformance is live — the SUM-CHAT@v1 contract is " +
                "pinned by P1SchemaConformance_SumChatOutputSchemaContract_PinsLoadBearingShape");

            _output.WriteLine(
                $"  {c.CaseId}  \"{c.Utterance}\"  ->  {binding.ConsumerType} binding {binding.BindingId} " +
                $"-> action {binding.ActionId} (SUM-CHAT@v1, {binding.ActionKind})");
        }
    }

    [Fact]
    public async Task P1LiveDispatch_DocumentUploadedEventAndChipClickCases_ResolveCatalogRoutes()
    {
        var suite = LoadSuite();
        var eventCases = suite.Cases
            .Where(c => IsPhase(c, "P1") && IsOutcome(c, "dispatch") && IsChannel(c, "event"))
            .ToList();
        var clickCases = suite.Cases
            .Where(c => IsPhase(c, "P1") && IsOutcome(c, "dispatch") && IsChannel(c, "click"))
            .ToList();

        eventCases.Should().NotBeEmpty("FR-P1-03/FR-P1-07: task 022 seeded P1 event-channel dispatch cases");
        clickCases.Should().NotBeEmpty(
            "the 2026-07-05 operator ruling ('auto-classify, chip-offered summarize') moved GU-038 " +
            "to the click channel — the chip → dispatch leg needs a live case");

        // Dataverse boundary stub shaped like the POST-RULING seeded catalog rows
        // (G-P1 UAT round-1 fix, 2026-07-05):
        //   chat-classify — SOLE document_uploaded member (order 1, CLS-CHAT@v1),
        //     sprk_chiptransitions → Summarize (targets the chat-summarize row GUID).
        //   chat-summarize — NO event membership (sprk_oneventbindings = []),
        //     reached ONLY via the chip's binding id (Click path).
        var summarizeRowId = Guid.NewGuid();
        var classifyRow = BuildActionTargetRow(
            ConsumerTypes.ChatClassify, ClsChatActionId, ucid: "UC-A-7",
            onEventBindingsJson: """[{"event":"document_uploaded","order":1}]""",
            chipTransitionsJson: $$"""[{"target_binding_id":"{{summarizeRowId}}","chip_label":"Summarize","requires_attachments":true}]""");
        var summarizeRow = BuildActionTargetRow(
            ConsumerTypes.ChatSummarize, SumChatActionId, ucid: "UC-A-1",
            rowId: summarizeRowId);

        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                // Binding-by-id query (the Click leg) → the matching row only;
                // event-candidates query (sprk_oneventbindings NotNull) → the classify row.
                var byId = query.Criteria.Conditions
                    .FirstOrDefault(c => c.AttributeName == "sprk_playbookconsumerid");
                if (byId is not null)
                {
                    var requestedId = (Guid)byId.Values[0];
                    var match = new[] { classifyRow, summarizeRow }.FirstOrDefault(e => e.Id == requestedId);
                    return new EntityCollection(match is null ? new List<Entity>() : new List<Entity> { match });
                }
                return new EntityCollection(new List<Entity> { classifyRow });
            });

        var routing = CreateRoutingService(entityService);

        // ── Event leg (GU-037): classify is the SOLE resolved member ─────────────
        var members = await routing.ResolveEventBindingsAsync("document_uploaded");

        members.Should().HaveCount(1,
            "post-ruling the FR-P1-03 launch rule declares exactly ONE member for document_uploaded " +
            "(summarize is chip-offered, not auto-run)");
        members[0].ConsumerType.Should().Be(ConsumerTypes.ChatClassify,
            "the sole member is the Layer-0 classification step (CLS-CHAT@v1, GU-037)");
        members[0].ActionId.Should().NotBeNull();
        members[0].ActionKind.Should().Be(ActionKind.Prompted,
            "every event-rule member executes through the prompted executor at P1 (thinness invariant)");
        members[0].ChipTransitions.Should().ContainSingle(t =>
                t.TargetBindingId == summarizeRowId.ToString() && t.ChipLabel == "Summarize" && t.RequiresAttachments == true,
            "the classify Binding's curated transition IS the summarize entry (chips carry the row GUID + " +
            "requires_attachments — G-P1 Defect-1 fix)");

        _output.WriteLine("P1 LIVE dispatch assertions — Event path (document_uploaded, classify-only rule):");
        foreach (var c in eventCases)
        {
            members.Select(m => m.ConsumerType).Should().Contain(c.Expected.ConsumerType,
                $"case {c.CaseId}: its expected capability must be a resolved member of the document_uploaded rule");
            c.Assertions.Should().NotBeNull(
                $"case {c.CaseId}: P1 dispatch cases must carry a schema-conformance assertion (NFR-06)");
            c.Assertions!.SchemaConformance.Should().Be("CLS-CHAT@v1",
                $"case {c.CaseId}: NFR-06 — event-path outputs are schema-validated structured outputs");
            _output.WriteLine($"  {c.CaseId}  \"{c.Utterance}\"  ->  {c.Expected.ConsumerType} (CLS-CHAT@v1)");
        }

        // ── Click leg (GU-038): the chip's target_binding_id resolves BY ID to the
        // summarize Binding — the exact read SessionDispatchOrchestrator performs.
        var chipTarget = Guid.Parse(members[0].ChipTransitions[0].TargetBindingId!);
        var clickBinding = await routing.GetBindingByIdAsync(chipTarget);

        clickBinding.Should().NotBeNull(
            "the Summarize chip's binding id must resolve through the by-id read (ADR-039: the id IS the routing decision)");
        clickBinding!.ConsumerType.Should().Be(ConsumerTypes.ChatSummarize,
            "GU-038: the chip-offered capability is chat-summarize");
        clickBinding.ActionId.Should().Be(SumChatActionId,
            "the Binding's sprk_action lookup targets the SUM-CHAT@v1 Action");
        clickBinding.ActionKind.Should().Be(ActionKind.Prompted);

        _output.WriteLine("P1 LIVE dispatch assertions — Click path (chip target_binding_id → by-id resolution):");
        foreach (var c in clickCases)
        {
            c.Expected.ConsumerType.Should().Be(ConsumerTypes.ChatSummarize,
                $"case {c.CaseId}: chat-summarize is the only chip-offered P1 click capability");
            c.Assertions.Should().NotBeNull(
                $"case {c.CaseId}: P1 dispatch cases must carry a schema-conformance assertion (NFR-06)");
            c.Assertions!.SchemaConformance.Should().Be("SUM-CHAT@v1",
                $"case {c.CaseId}: NFR-06 — the dispatched output is schema-validated (SUM-CHAT@v1)");
            _output.WriteLine($"  {c.CaseId}  \"{c.Utterance}\"  ->  {c.Expected.ConsumerType} (SUM-CHAT@v1)");
        }
    }

    [Fact]
    public void P1LiveDispatch_EventClarifyCase_M4ConfidencePolicyDialIsPinned()
    {
        var suite = LoadSuite();
        var clarifyCases = suite.Cases
            .Where(c => IsPhase(c, "P1") && IsOutcome(c, "clarify"))
            .ToList();

        clarifyCases.Should().NotBeEmpty(
            "GU-040 (low classify confidence → M4 confirmation turn) activates at P1 with the event rule");
        clarifyCases.Should().OnlyContain(c => IsChannel(c, "event"),
            "the only P1 clarify surface is the Event path's M4 confirmation — text-path clarify " +
            "requires the P2 loop (Binding.CaptureMode LoopElicitation)");

        // The deterministic policy surface: confidence below the operator dial
        // suspends member order 2 into an event_confirmation turn. Pinning the
        // default makes a silent policy-default change a dispatch-behavior
        // regression caught here (NFR-06); a deliberate change updates this
        // case in the same PR.
        var options = new Sprk.Bff.Api.Services.Ai.EventRules.EventRulesOptions();
        options.ClassifyConfidenceThreshold.Should().Be(0.85,
            "canonical §3.10 step 3 — the surviving D1 dial's default");
        options.ClassifyConfidenceThreshold.Should().BeInRange(0.0, 1.0,
            "the threshold is a calibrated probability bound");

        _output.WriteLine(
            "P1 LIVE clarify assertion — M4 policy dial pinned at " +
            $"{options.ClassifyConfidenceThreshold}. Suspend/confirmation BEHAVIOR proven in " +
            "EventRulesServiceTests.FireAsync_ClassifyConfidenceBelowThreshold_FiresM4Confirmation_SummarizeSuspended (task 022).");
    }

    /// <summary>
    /// NFR-06 schema-conformance, live at P1: the SUM-CHAT@v1 output-schema
    /// contract pinned in the repo must keep its load-bearing shape. A schema
    /// edit that degrades summarize output (dropped field, reordered streaming
    /// properties, opened additionalProperties) fails CI here — not UAT.
    /// </summary>
    [Fact]
    public void P1SchemaConformance_SumChatOutputSchemaContract_PinsLoadBearingShape()
    {
        var schemaPath = Path.Combine(
            FindRepoRoot(), "infra", "dataverse", "outputschemas", "sum-chat-v1.schema.json");
        File.Exists(schemaPath).Should().BeTrue(
            $"the SUM-CHAT@v1 output-schema contract must exist at {schemaPath} " +
            "(it mirrors sprk_analysisaction.sprk_outputschemajson on spaarkedev1)");

        using var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = doc.RootElement;

        root.GetProperty("title").GetString().Should().Contain("SUM-CHAT@v1",
            "the contract file names the action code the eval cases reference");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            "structured outputs are closed — the model cannot invent fields");
        root.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "tldr", "summary", "keywords", "entities" },
                "all four SUM-CHAT@v1 output fields are required");
        root.GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .Should().Equal(new[] { "tldr", "summary", "keywords", "entities" },
                "property DECLARATION ORDER is load-bearing — Azure OpenAI structured outputs " +
                "stream in declaration order; tldr must stream first (R5 FR-02 TL;DR-first UX)");

        _output.WriteLine($"P1 LIVE schema-conformance — SUM-CHAT@v1 contract pinned at {schemaPath}");
    }

    // -------------------------------------------------------------------------
    // P2 live refusal-surface assertions (task 033, FR-P2-04) — the honest-refusal
    // family's ROUTING + PROJECTION legs are live; the NL-loop dispatch assertion
    // (off-catalog utterance → the model invoking the refusal tool) activates with
    // task 037 (FR-P2-08) per each case's activation declaration.
    //
    // What "live" means here (mirrors the P1 pattern — no dispatcher invented):
    //   • The tenant's no_match_handler Binding resolves through the REAL
    //     ListTextProjectableBindingsAsync — the EXACT projection read
    //     SprkChatAgentFactory performs to put the refusal tool in the loop's
    //     tool list. Dataverse stubbed at the module boundary with a row shaped
    //     like the seeded spaarkedev1 catalog row (task 033).
    //   • The resolved Binding constructs the RefusalCapabilityTool projection
    //     (deterministic name, maker-authored description, catalog schema).
    //   • The REF-CHAT@v1 output-schema contract file is pinned (NFR-06) —
    //     the single required `refusal` string the loop relays verbatim.
    // -------------------------------------------------------------------------

    /// <summary>The REF-CHAT@v1 <c>sprk_analysisaction</c> target seeded on spaarkedev1 (task 033).</summary>
    private static readonly Guid RefChatActionId = new("8d337be2-3d79-f111-ab0e-7ced8ddc4cc6");

    [Fact]
    public async Task P2RefusalSurface_NoMatchHandlerBinding_ResolvesAndProjectsThroughTheClosedCatalog()
    {
        var suite = LoadSuite();
        var refusalCases = suite.Cases
            .Where(c => string.Equals(c.Family, "refusal", StringComparison.OrdinalIgnoreCase))
            .ToList();
        refusalCases.Should().NotBeEmpty("FR-P2-04: the refusal family carries text-channel refuse cases");
        refusalCases.Should().OnlyContain(c => IsOutcome(c, "refuse"),
            "the refusal family's outcome class is honest refusal — the fourth outcome of the G-P2 contract");

        // Dataverse boundary stub shaped like the seeded spaarkedev1 row (task 033):
        // enabled no_match_handler Binding, sprk_action → REF-CHAT@v1 (Prompted),
        // maker-authored sprk_tooldescription (the projection opt-in), assistant surface.
        var refusalRow = BuildActionTargetRow(
            ConsumerTypes.NoMatchHandler, RefChatActionId, ucid: "L4-REFUSAL");
        refusalRow["sprk_tooldescription"] =
            "REFUSAL — call this when the user's request matches no other available capability.";
        refusalRow["sprk_surfaces"] = "assistant";

        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { refusalRow }));

        var routing = CreateRoutingService(entityService);

        // ── Projection leg: the EXACT read SprkChatAgentFactory performs (FR-P2-01/FR-P2-04).
        var projectable = await routing.ListTextProjectableBindingsAsync();

        var binding = projectable.Should().ContainSingle(
            b => string.Equals(b.ConsumerType, ConsumerTypes.NoMatchHandler, StringComparison.OrdinalIgnoreCase),
            "the tenant's no_match_handler Binding is text-projectable — the refusal template is CATALOG data " +
            "(ADR-039: routing config lives only in the Binding table)").Subject;
        binding.ActionId.Should().Be(RefChatActionId,
            "the Binding's sprk_action lookup targets the REF-CHAT@v1 prompted Action (the refusal template)");
        binding.ActionKind.Should().Be(ActionKind.Prompted,
            "the refusal renders through the prompted executor — prompt-controlled honest copy");
        binding.ToolDescription.Should().NotBeNullOrWhiteSpace(
            "the maker-authored sprk_tooldescription is the projection opt-in AND the loop's intent surface");

        // ── Tool-projection leg: the resolved Binding constructs the refusal tool the
        // loop invokes (deterministic name — the system-prompt directive references it).
        var tool = new Sprk.Bff.Api.Services.Ai.Chat.RefusalCapabilityTool(
            binding,
            new ServiceCollection().BuildServiceProvider(),
            "tenant-eval", "session-eval",
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        tool.Name.Should().Be("capability_no_match_handler");
        tool.Description.Should().Be(binding.ToolDescription,
            "no hardcoded refusal copy — description comes from the catalog row");

        // ── NFR-06 schema-conformance leg: the REF-CHAT@v1 contract file is pinned.
        var schemaPath = Path.Combine(
            FindRepoRoot(), "infra", "dataverse", "outputschemas", "ref-chat-v1.schema.json");
        File.Exists(schemaPath).Should().BeTrue(
            $"the REF-CHAT@v1 output-schema contract must exist at {schemaPath} " +
            "(it mirrors sprk_analysisaction.sprk_outputschemajson on spaarkedev1)");
        using var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        doc.RootElement.GetProperty("title").GetString().Should().Contain("REF-CHAT@v1");
        doc.RootElement.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            "structured outputs are closed — the model cannot invent fields");
        doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "refusal" },
                "the refusal payload's single required field is what RefusalCapabilityTool relays verbatim");

        _output.WriteLine("P2 LIVE refusal-surface assertions (FR-P2-04):");
        foreach (var c in refusalCases)
        {
            _output.WriteLine($"  {c.CaseId}  \"{c.Utterance}\"  ->  refuse via {binding.ConsumerType} " +
                              $"binding {binding.BindingId} -> action {binding.ActionId} (REF-CHAT@v1)");
        }
    }

    // -------------------------------------------------------------------------
    // Pending-by-design declaration (P2/P3): dispatch assertions not yet
    // activated are stubbed, not silently skipped. This test PASSES while
    // making the remaining pending inventory visible in the run output with
    // its activating task per family. P1 was ACTIVATED by task 026 (FR-P1-07)
    // — its cases are asserted live above and are guarded here so no new
    // P1-phase case can be added without a live assertion covering it.
    // -------------------------------------------------------------------------

    [Fact]
    public void PendingDispatchAssertions_AreExplicitlyDeclaredWithActivation_NotSilentlySkipped()
    {
        var suite = LoadSuite();

        // Activation guard (task 026): every P1-phase case MUST be covered by one
        // of the live P1 assertions above. A case added with dispatchAssertPhase=P1
        // outside these selectors fails here until a live assertion exists —
        // pending-by-design never silently returns at an activated phase.
        foreach (var c in suite.Cases.Where(c => IsPhase(c, "P1")))
        {
            var covered =
                (IsOutcome(c, "dispatch") && IsChannel(c, "text")
                    && string.Equals(c.Family, "chat-summarize", StringComparison.OrdinalIgnoreCase))
                || (IsOutcome(c, "dispatch") && IsChannel(c, "event"))
                || (IsOutcome(c, "dispatch") && IsChannel(c, "click")
                    && string.Equals(c.Expected.ConsumerType, ConsumerTypes.ChatSummarize, StringComparison.OrdinalIgnoreCase))
                || (IsOutcome(c, "clarify") && IsChannel(c, "event"));
            covered.Should().BeTrue(
                $"case {c.CaseId} declares dispatchAssertPhase=P1, but no live P1 assertion in this " +
                "suite covers its (channel, outcomeClass, family) — add one before declaring P1");
        }

        _output.WriteLine("PENDING dispatch-assertion inventory (P2/P3 — P1 activated live by task 026):");
        _output.WriteLine("");
        _output.WriteLine($"{"family",-22} {"cases",5}  {"phase",-6} activated by");
        _output.WriteLine(new string('-', 90));

        foreach (var group in suite.Cases
                     .Where(c => !IsPhase(c, "P1"))
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

    private static bool IsPhase(GoldenUtteranceCase c, string phase)
        => string.Equals(c.Activation.DispatchAssertPhase, phase, StringComparison.OrdinalIgnoreCase);

    private static bool IsChannel(GoldenUtteranceCase c, string channel)
        => string.Equals(c.Channel, channel, StringComparison.OrdinalIgnoreCase);

    private static bool IsOutcome(GoldenUtteranceCase c, string outcomeClass)
        => string.Equals(c.Expected.OutcomeClass, outcomeClass, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Construct the REAL <see cref="ConsumerRoutingService"/> over the stubbed
    /// Dataverse boundary — the single shared harness seam every routing-surface
    /// test (P0 smoke + P1 live) plugs into. Cache is fresh per call so cases
    /// never observe each other's resolutions.
    /// </summary>
    private static ConsumerRoutingService CreateRoutingService(Mock<IGenericEntityService> entityService)
    {
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(e => e.EnvironmentName).Returns("Development");

        return new ConsumerRoutingService(
            entityService.Object,
            new MemoryCache(new MemoryCacheOptions()),
            hostEnvironment.Object,
            NullLogger<ConsumerRoutingService>.Instance);
    }

    /// <summary>
    /// One enabled <c>sprk_playbookconsumer</c> row with an <c>sprk_action</c>
    /// TARGET, shaped like the P1 catalog rows seeded on spaarkedev1 (task 020
    /// chat-summarize → SUM-CHAT@v1; task 022 chat-classify → CLS-CHAT@v1 with
    /// <c>sprk_oneventbindings</c> membership). The aliased <c>action.sprk_kind</c>
    /// mirrors the left-outer Action link the real query projects.
    /// </summary>
    private static Entity BuildActionTargetRow(
        string consumerType,
        Guid actionId,
        string ucid,
        string? onEventBindingsJson = null,
        string? chipTransitionsJson = null,
        Guid? rowId = null)
    {
        var entity = new Entity("sprk_playbookconsumer", rowId ?? Guid.NewGuid());
        entity["sprk_playbookconsumerid"] = entity.Id;
        entity["sprk_consumertype"] = consumerType;
        entity["sprk_consumercode"] = "default";
        entity["sprk_priority"] = 100;
        entity["sprk_enabled"] = true;
        entity["sprk_ucid"] = ucid;
        entity["sprk_action"] = new EntityReference("sprk_analysisaction", actionId);
        entity["action.sprk_kind"] = new AliasedValue(
            "sprk_analysisaction", "sprk_kind", new OptionSetValue((int)ActionKind.Prompted));
        if (onEventBindingsJson is not null)
        {
            entity["sprk_oneventbindings"] = onEventBindingsJson;
        }
        if (chipTransitionsJson is not null)
        {
            entity["sprk_chiptransitions"] = chipTransitionsJson;
        }

        return entity;
    }

    /// <summary>
    /// Walk up from the test bin directory to the repo root (identified by
    /// <c>Spaarke.sln</c>) — the established pattern for repo-pinned contract
    /// files (see WorkspaceFileEndpointsTests, Phase2EndToEndTests).
    /// </summary>
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
            "the SUM-CHAT@v1 schema-contract assertion requires an in-repo test run.");
    }

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
