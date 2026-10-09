// Regression: sweep AI-01 + PB-02 (spaarke-ontology-platform-r1 task 135, D-96 / D-98 / D-101).
// matter-health-single, the only node-engine playbook in production (insights-ask), could never answer:
//   (1) queryKpiAssessments selected columns sprk_kpiassessment does not have (sprk_grade, sprk_notes,
//       sprk_assessmentdate), so the query failed;
//   (2) retrieveObservations filtered on a top-level `matterId`, which spaarke-insights-index does not have (HTTP 400);
//   (3) synthesize (AgentService) had no `prompt`, and the executor never read its Action's prompt;
//   (4) `from` fields named NODES (synthesize, retrieveObservations, checkSufficiency, groundCitations) where the
//       engine looks up OUTPUT VARIABLES, and sourceChunksJsonPath was "documents" (IndexRetrieve emits Artifacts);
//   (5) the synthesis citations ({type,id,label,excerpt}) did not fit GroundingVerify's EvidenceRef shape;
//   (6) persistEnvelope's template used a node-name root and run.startedAtIso, which does not exist;
//   (7) on the decline path groundCitations still ran, because a skipped node's output was never stored (PB-02).
// These tests run the REPO definition through the production orchestrator, template engine and executors. Only the
// boundaries are doubles: Dataverse (a fake that rejects a column sprk_kpiassessment lacks), the live-fact resolver,
// AI Search (the filter is checked against the repo index schema) and the Foundry transport (the real executor's
// prompt resolution and output parsing run; only the network reply is canned).
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.CitationVerification;
using Sprk.Bff.Api.Services.Ai.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.Ai;

[Trait("status", "regression-ai01")]
public class AI01_MatterHealthSinglePlaybookTests
{
    private const string PlaybookPath = "src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/matter-health-single.playbook.json";
    private const string IndexSchemaPath = "infrastructure/ai-search/spaarke-insights-index.json";
    private const string TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private static readonly Guid MatterId = Guid.Parse("b68299c6-bafb-f011-8407-7c1e520aa4df");
    private static readonly Guid SynthesisActionId = Guid.Parse("5981632e-4165-f111-ab0c-7ced8ddc4cc6");

    // sprk_kpiassessment columns in spaarkedev1 (MCP describe tables/sprk_kpiassessment, 2026-10-09).
    private static readonly HashSet<string> KpiAssessmentColumns = new(StringComparer.Ordinal)
    {
        "createdby", "createdon", "createdonbehalfby", "importsequencenumber", "modifiedby", "modifiedon",
        "modifiedonbehalfby", "overriddencreatedon", "ownerid", "owningbusinessunit", "owningteam", "owninguser",
        "sprk_assessmentcriteria", "sprk_assessmentnotes", "sprk_createdbyperson", "sprk_kpiassessmentid",
        "sprk_kpigradescore", "sprk_kpiname", "sprk_matter", "sprk_performancearea", "sprk_project",
        "sprk_recordsummary", "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordnumber",
        "sprk_regardingrecordtype", "sprk_regardingrecordurl", "sprk_reportcard", "statecode", "statuscode",
        "timezoneruleversionnumber", "utcconversiontimezonecode", "versionnumber",
    };

    private const string GuidelineNotes = "Outside counsel missed the status update deadline for the third consecutive period.";
    private const string BudgetNotes = "Invoices stayed inside the approved budget with no write-downs this quarter.";

    // A trimmed copy of the live matter-health-synthesis JPS: same placeholders, same structured output fields.
    private const string SynthesisJps = """
        {"$schema":"https://spaarke.com/schemas/prompt/v1","$version":1,
         "instruction":{"role":"You are the Spaarke Insights Engine synthesizer.",
           "task":"Synthesize a diagnostic narrative for the matter identified by {{matterId}} (current composite grade {{currentGrade}}) by composing {{matterContext}}, {{assessments}}, and {{observations}}.",
           "constraints":["Cite assessments with a verbatim excerpt of their notes."],
           "context":"Placeholders are resolved by the playbook."},
         "output":{"fields":[
           {"name":"schemaVersion","type":"string","enum":["1.0"]},
           {"name":"body","type":"string"},
           {"name":"citations","type":"array","items":{"type":"object","properties":{"type":{"type":"string"},"id":{"type":"string"},"label":{"type":"string"},"excerpt":{"type":"string"}},"required":["type","id","label","excerpt"]}},
           {"name":"generatedAt","type":"string"},
           {"name":"playbookName","type":"string"},
           {"name":"tenantId","type":"string"},
           {"name":"dimensions","type":"array","items":{"type":"string"}}],
          "structuredOutput":true}}
        """;

    // ── The end-to-end runs ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SufficientEvidence_ProducesAGroundedArtifact_WithOnlyVerifiedCitations_AndPersistsAValidEnvelope()
    {
        var harness = new Harness(assessmentNotes: [GuidelineNotes, BudgetNotes, "Matter plan agreed with the client."]);

        var (events, result) = await harness.RunAsync();

        events.Should().NotContain(e => e.Type == PlaybookEventType.RunFailed, Describe(events));
        events.Should().ContainSingle(e => e.Type == PlaybookEventType.RunCompleted, Describe(events));
        events.Where(e => e.Type == PlaybookEventType.NodeSkipped).Select(e => e.NodeName)
            .Should().BeEquivalentTo(["declineInsufficient"]);

        // The API-level outcome (what /api/insights/ask returns): an artifact, not a decline.
        result.HasArtifact.Should().BeTrue(Describe(events));
        result.HasDecline.Should().BeFalse();
        var artifact = result.Artifact.Should().BeOfType<InferenceArtifact>().Subject;
        artifact.Subject.Should().Be($"matter:{MatterId}");
        artifact.Value.Raw.GetProperty("body").GetString().Should().Be(Harness.ReplyBody);
        artifact.Evidence.Should().ContainSingle("the fabricated citation is stripped; the verbatim one survives")
            .Which.Should().BeEquivalentTo(new EvidenceRef { RefType = "assessment", Ref = harness.AssessmentIds[0].ToString(), Quote = Harness.VerbatimExcerpt });
        artifact.Value.Raw.GetProperty("citations").GetArrayLength().Should().Be(1);
        artifact.Confidence.Should().Be(0.5, "the synthesis emits no confidence, so it is the share of citations verified (1 of 2)");
        artifact.Reasoning.Should().Contain("1 of 2 citation(s) verified");

        // The synthesis prompt came from the Action and carried the real data, not literal placeholders.
        harness.SentPrompt.Should().NotBeNull();
        harness.SentPrompt.Should().Contain(GuidelineNotes).And.Contain("Guideline Compliance").And.Contain(MatterId.ToString());
        harness.SentPrompt.Should().NotContain("{{", "every placeholder is filled");
        harness.SentPrompt.Should().Contain("current composite grade (not supplied: derive it from the assessments)");

        // The index filter only uses fields the deployed index has (no top-level matterId).
        harness.IndexFilter.Should().Contain($"scope/matterId eq '{MatterId}'");
        harness.IndexFilterFieldsMissingFromSchema().Should().BeEmpty();

        // persistEnvelope writes a valid FR-14 envelope with exactly the seven fields.
        var value = harness.PersistedValue.Should().NotBeNull().And.Subject;
        var envelope = JsonNode.Parse(value!)!.AsObject();
        envelope.Select(p => p.Key).Should().BeEquivalentTo(
            ["schemaVersion", "body", "citations", "generatedAt", "playbookName", "tenantId", "dimensions"]);
        envelope["schemaVersion"]!.GetValue<string>().Should().Be("1.0");
        envelope["body"]!.GetValue<string>().Should().Be(Harness.ReplyBody, "UpdateRecord's own render must not evaluate {{ }} inside the reply");
        envelope["citations"]!.AsArray().Should().HaveCount(1);
        DateTimeOffset.TryParse(envelope["generatedAt"]!.GetValue<string>(), out _).Should().BeTrue();
        envelope["tenantId"]!.GetValue<string>().Should().Be(TenantId);
        envelope["dimensions"]!.AsArray().Select(d => d!.GetValue<string>()).Should().Contain("trend");
    }

    [Fact]
    public async Task InsufficientEvidence_DeclinesWithRealNumbers_AndSkipsTheWholeSynthesisBranch()
    {
        var harness = new Harness(assessmentNotes: [GuidelineNotes]);

        var (events, result) = await harness.RunAsync();

        events.Should().NotContain(e => e.Type == PlaybookEventType.RunFailed, Describe(events));
        events.Should().ContainSingle(e => e.Type == PlaybookEventType.RunCompleted, Describe(events));
        events.Where(e => e.Type == PlaybookEventType.NodeSkipped).Select(e => e.NodeName)
            .Should().BeEquivalentTo(["synthesize", "groundCitations", "ReturnInsightArtifactNode", "persistEnvelope"],
                "PB-02: everything downstream of the unselected branch is skipped, not run on empty input");
        harness.SentPrompt.Should().BeNull("no LLM call on the decline path");
        harness.PersistedValue.Should().BeNull("sprk_performancesummary is not overwritten on a decline");

        result.HasArtifact.Should().BeFalse();
        result.HasDecline.Should().BeTrue(Describe(events));
        result.Decline!.Reason.Should().Be("insufficient-evidence");
        result.Decline.Explanation.Should().Contain("only 1 KPI assessments").And.Contain("need 2");
        result.Decline.Explanation.Should().NotContain("{have}").And.NotContain("{need}");
    }

    // ── Engine: skip propagation (PB-02) on a minimal graph ──────────────────────────────────

    [Fact]
    public async Task Engine_ANodeWhoseOnlyDependencyWasBranchSkipped_IsSkipped_ButAJoinWithARunDependencyStillRuns()
    {
        var gate = Node("gate", "gate", ExecutorType.EvidenceSufficiency);
        var skippedBranch = Node("onSufficient", "a", ExecutorType.AiCompletion, gate.Id);
        var afterSkipped = Node("afterSkipped", "b", ExecutorType.GroundingVerify, skippedBranch.Id);
        var join = Node("join", "j", ExecutorType.ObservationEmit, afterSkipped.Id, gate.Id);

        var ran = new List<string>();
        var registry = new Mock<INodeExecutorRegistry>();
        registry.Setup(r => r.GetExecutor(ExecutorType.EvidenceSufficiency))
            .Returns(Recording(ran, ctx => new { SelectedBranch = "join" }));
        foreach (var type in new[] { ExecutorType.AiCompletion, ExecutorType.GroundingVerify, ExecutorType.ObservationEmit })
            registry.Setup(r => r.GetExecutor(type)).Returns(Recording(ran, _ => new { ok = true }));

        var events = await RunGraphAsync(registry.Object, gate, skippedBranch, afterSkipped, join);

        ran.Should().Equal("gate", "join");
        events.Where(e => e.Type == PlaybookEventType.NodeSkipped).Select(e => e.NodeName)
            .Should().BeEquivalentTo(["onSufficient", "afterSkipped"]);
        var completed = events.Single(e => e.Type == PlaybookEventType.RunCompleted);
        completed.Metrics!.SkippedNodes.Should().Be(2);
        completed.Metrics.CompletedNodes.Should().Be(2, "a skipped node counts as skipped, not as completed");
    }

    // ── AgentService takes its prompt from the linked Action (D-98) ──────────────────────────

    [Fact]
    public void AgentService_WithoutANodePrompt_RendersTheLinkedActionsPrompt_WithTheNodesInputs()
    {
        var renderer = new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance);
        var context = AgentContext(
            """{"tenantId":"t","templateParameters":{"matterId":"M-1","currentGrade":"B","matterContext":"ctx (see Input)","assessments":"rows (see Input)","observations":"obs (see Input)"},"inputBinding":{"assessments":[{"sprk_assessmentnotes":"late filings"}]}}""",
            SynthesisJps);
        var config = JsonSerializer.Deserialize<AgentServiceNodeConfig>(context.Node.ConfigJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var prompt = AgentServiceNodeExecutor.ResolvePrompt(context, config, renderer, NullLogger.Instance);

        prompt.Should().Contain("Spaarke Insights Engine synthesizer", "the Action's prompt is the message");
        prompt.Should().Contain("matter identified by M-1 (current composite grade B)");
        prompt.Should().Contain("## Input").And.Contain("late filings");
        prompt.Should().Contain("\"citations\"", "the structured-output schema is appended for an agent without constrained decoding");
        prompt.Should().NotContain("{{");
    }

    [Fact]
    public void AgentService_ANodeAuthoredPrompt_StillWins()
    {
        var renderer = new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance);
        var context = AgentContext("""{"tenantId":"t","prompt":"Say hello."}""", SynthesisJps);
        var config = new AgentServiceNodeConfig { TenantId = "t", Prompt = "Say hello." };

        AgentServiceNodeExecutor.ResolvePrompt(context, config, renderer, NullLogger.Instance).Should().Be("Say hello.");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AgentService_Validate_AcceptsAMissingPrompt_OnlyWhenTheActionHasOne(bool actionHasPrompt, bool expectedValid)
    {
        var executor = new AgentServiceNodeExecutor(DisabledAgentClient(),
            new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger<AgentServiceNodeExecutor>.Instance);
        var context = AgentContext("""{"tenantId":"t"}""", actionHasPrompt ? SynthesisJps : null);

        var result = executor.Validate(context);

        result.IsValid.Should().Be(expectedValid);
        if (!expectedValid)
            result.Errors.Should().ContainSingle().Which.Should().Contain("linked Action with a system prompt");
    }

    [Fact]
    public void AgentService_AJsonReply_BecomesTheStructuredOutput_AndAPlainReplyKeepsTheOldShape()
    {
        var context = AgentContext("""{"tenantId":"t"}""", SynthesisJps);
        var started = DateTimeOffset.UtcNow;

        var fenced = AgentServiceNodeExecutor.BuildOutput(context, "thread-1", "```json\n{\"body\":\"hi\",\"citations\":[]}\n```", started);
        fenced.StructuredData!.Value.GetProperty("body").GetString().Should().Be("hi");

        var plain = AgentServiceNodeExecutor.BuildOutput(context, "thread-1", "just text", started);
        plain.StructuredData!.Value.GetProperty("threadId").GetString().Should().Be("thread-1");
        plain.TextContent.Should().Be("just text");
    }

    [Fact]
    public async Task SuppliedCurrentGrade_ReachesThePrompt()
    {
        var harness = new Harness(assessmentNotes: [GuidelineNotes, BudgetNotes], currentGrade: "B+");

        await harness.RunAsync();

        harness.SentPrompt.Should().Contain("current composite grade B+");
    }

    // ── Engine: the other skip paths and what a skip looks like downstream (review round 1) ──

    [Fact]
    public async Task Engine_AFailedDependency_StopsTheRun_AndItsDependentNeverRuns()
    {
        var failing = Node("failing", "f", ExecutorType.AiCompletion);
        var after = Node("after", "a", ExecutorType.GroundingVerify, failing.Id);
        var ran = new List<string>();
        var registry = new Mock<INodeExecutorRegistry>();
        registry.Setup(r => r.GetExecutor(ExecutorType.AiCompletion)).Returns(Fake((ctx, _) =>
            Task.FromResult(NodeOutput.Error(ctx.Node.Id, ctx.Node.OutputVariable, "boom", NodeErrorCodes.InternalError))));
        registry.Setup(r => r.GetExecutor(ExecutorType.GroundingVerify)).Returns(Recording(ran, _ => new { ok = true }));

        var events = await RunGraphAsync(registry.Object, failing, after);

        ran.Should().BeEmpty();
        events.Should().ContainSingle(e => e.Type == PlaybookEventType.RunFailed).Which.Error.Should().Contain("failing");
    }

    [Fact]
    public async Task Engine_AFalseConditionWithAFalseBranch_SkipsTheTrueBranchChain_AndRunsTheJoin()
    {
        var condition = Node("condition", "cond", ExecutorType.Condition) with
        {
            ConfigJson = """{"condition":{"operator":"eq","left":"a","right":"b"},"trueBranch":"onTrue","falseBranch":"join"}""",
        };
        var onTrue = Node("onTrue", "t", ExecutorType.AiCompletion, condition.Id);
        var afterTrue = Node("afterTrue", "t2", ExecutorType.GroundingVerify, onTrue.Id);
        var join = Node("join", "j", ExecutorType.ObservationEmit, afterTrue.Id, condition.Id);
        var ran = new List<string>();
        var registry = new Mock<INodeExecutorRegistry>();
        registry.Setup(r => r.GetExecutor(ExecutorType.Condition))
            .Returns(new ConditionNodeExecutor(new TemplateEngine(NullLogger<TemplateEngine>.Instance), NullLogger<ConditionNodeExecutor>.Instance));
        foreach (var type in new[] { ExecutorType.AiCompletion, ExecutorType.GroundingVerify, ExecutorType.ObservationEmit })
            registry.Setup(r => r.GetExecutor(type)).Returns(Recording(ran, _ => new { ok = true }));

        var events = await RunGraphAsync(registry.Object, condition, onTrue, afterTrue, join);

        ran.Should().Equal("join");
        events.Where(e => e.Type == PlaybookEventType.NodeSkipped).Select(e => e.NodeName)
            .Should().BeEquivalentTo(["onTrue", "afterTrue"]);
    }

    [Fact]
    public async Task Engine_AnInsightsLayer2GateFail_IsASkipLikeAnyOther_CountedOnce_AndPropagated()
    {
        var extract = Node("layer2Extract", "layer2", ExecutorType.AiCompletion);
        var verify = Node("groundingVerify", "grounded", ExecutorType.GroundingVerify, extract.Id);
        var ran = new List<string>();
        var registry = new Mock<INodeExecutorRegistry>();
        foreach (var type in new[] { ExecutorType.AiCompletion, ExecutorType.GroundingVerify })
            registry.Setup(r => r.GetExecutor(type)).Returns(Recording(ran, _ => new { ok = true }));

        var events = await RunGraphAsync(registry.Object, gateFailLayer2: true, extract, verify);

        ran.Should().BeEmpty();
        events.Where(e => e.Type == PlaybookEventType.NodeSkipped).Select(e => e.NodeName)
            .Should().BeEquivalentTo(["layer2Extract", "groundingVerify"]);
        var completed = events.Single(e => e.Type == PlaybookEventType.RunCompleted).Metrics!;
        completed.SkippedNodes.Should().Be(2);
        completed.CompletedNodes.Should().Be(0, "the gate-fail skip was counted as completed too");
    }

    [Fact]
    public async Task Engine_ASkippedNode_ReadsAsNotSucceededAndSkipped_InTemplates_AndRunDetail()
    {
        var gate = Node("gate", "gate", ExecutorType.EvidenceSufficiency);
        var skipped = Node("onSufficient", "a", ExecutorType.AiCompletion, gate.Id);
        var join = Node("join", "j", ExecutorType.ObservationEmit, skipped.Id, gate.Id) with
        {
            ConfigJson = """{"success":"{{a.success}}","skipped":"{{a.skipped}}","flag":"{{#if a.success}}ran{{else}}not-ran{{/if}}"}""",
        };
        string? seenConfig = null;
        var registry = new Mock<INodeExecutorRegistry>();
        registry.Setup(r => r.GetExecutor(ExecutorType.EvidenceSufficiency)).Returns(Recording([], _ => new { SelectedBranch = "join" }));
        registry.Setup(r => r.GetExecutor(ExecutorType.AiCompletion)).Returns(Recording([], _ => new { ok = true }));
        registry.Setup(r => r.GetExecutor(ExecutorType.ObservationEmit)).Returns(Fake((ctx, _) =>
        {
            seenConfig = ctx.Node.ConfigJson;
            return Task.FromResult(NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { ok = true }));
        }));

        var (events, orchestrator) = await RunGraphWithServiceAsync(registry.Object, false, gate, skipped, join);

        var config = JsonNode.Parse(seenConfig!)!;
        config["success"]!.GetValue<bool>().Should().BeFalse();
        config["skipped"]!.GetValue<bool>().Should().BeTrue();
        config["flag"]!.GetValue<string>().Should().Be("not-ran");
        var detail = await orchestrator.GetRunDetailAsync(events[0].RunId, CancellationToken.None);
        var skippedDetail = detail!.NodeDetails.Single(d => d.OutputVariable == "a");
        skippedDetail.Skipped.Should().BeTrue();
        skippedDetail.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Engine_ASkippedBranch_NeverReplacesTheOutputABranchThatRanStoredUnderTheSameVariable()
    {
        var gate = Node("gate", "gate", ExecutorType.EvidenceSufficiency);
        var chosen = Node("chosen", "result", ExecutorType.AiCompletion, gate.Id);
        // "other" waits for "chosen", so its skip is stored AFTER chosen's output: a skip that overwrote would win.
        var other = Node("other", "result", ExecutorType.AiCompletion, gate.Id, chosen.Id);
        var reader = Node("reader", "r", ExecutorType.ObservationEmit, other.Id, chosen.Id) with
        {
            ConfigJson = """{"value":"{{result.value}}"}""",
        };
        string? seenConfig = null;
        var registry = new Mock<INodeExecutorRegistry>();
        registry.Setup(r => r.GetExecutor(ExecutorType.EvidenceSufficiency)).Returns(Recording([], _ => new { SelectedBranch = "chosen" }));
        registry.Setup(r => r.GetExecutor(ExecutorType.AiCompletion)).Returns(Recording([], _ => new { value = "from-chosen" }));
        registry.Setup(r => r.GetExecutor(ExecutorType.ObservationEmit)).Returns(Fake((ctx, _) =>
        {
            seenConfig = ctx.Node.ConfigJson;
            return Task.FromResult(NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new { ok = true }));
        }));

        await RunGraphAsync(registry.Object, gate, chosen, other, reader);

        JsonNode.Parse(seenConfig!)!["value"]!.GetValue<string>().Should().Be("from-chosen");
    }

    [Fact]
    public async Task DeliverComposite_DropsTheSectionOfASkippedUpstream()
    {
        var executor = new DeliverCompositeNodeExecutor(NullLogger<DeliverCompositeNodeExecutor>.Instance);
        var context = AgentContext(
            """{"destination":"workspace","sections":[{"sectionName":"ran","inputVariable":"x"},{"sectionName":"skipped","inputVariable":"y"}]}""",
            null) with
        {
            PreviousOutputs = new Dictionary<string, NodeOutput>
            {
                ["x"] = NodeOutput.Ok(Guid.NewGuid(), "x", new { v = 1 }, "text"),
                ["y"] = NodeOutput.Skipped(Guid.NewGuid(), "y", "Branch not selected"),
            },
        };

        var output = await executor.ExecuteAsync(context, CancellationToken.None);

        output.Success.Should().BeTrue();
        var sections = output.StructuredData!.Value.GetProperty("sections");
        sections.GetArrayLength().Should().Be(1);
        sections[0].GetProperty("sectionName").GetString().Should().Be("ran");
    }

    // ── GroundingVerify: back-compat, targeting, validation, duplicate keys ─────────────────

    [Fact]
    public async Task GroundingVerify_EvidenceRefCitations_AndChunkRefSources_WorkAsBefore()
    {
        var context = GroundingContext(
            """{"citationsFrom":"layer2","sourceChunksFrom":"sanitization"}""",
            ("layer2", """{"evidence":[{"refType":"document","ref":"spe://d/1","quote":"the lease term is ten years"},{"refType":"document","ref":"spe://d/1","quote":"a sentence nobody wrote"}]}"""),
            ("sanitization", """{"chunks":[{"chunkId":"c1","text":"Clause 4. The lease term is ten years from signing."}]}"""));

        var data = (await Grounding().ExecuteAsync(context, CancellationToken.None)).StructuredData!.Value;

        data.GetProperty("VerifiedCount").GetInt32().Should().Be(1);
        data.GetProperty("NotFoundCount").GetInt32().Should().Be(1);
        data.GetProperty("VerifiedEvidence")[0].GetProperty("ref").GetString().Should().Be("spe://d/1");
    }

    [Fact]
    public async Task GroundingVerify_ACitationThatNamesARow_IsVerifiedAgainstThatRowOnly()
    {
        var context = GroundingContext(
            """{"citationsFrom":"synthesis","citationsJsonPath":"citations","sources":[{"from":"assessments","jsonPath":"items"}]}""",
            ("synthesis", """{"body":"b","citations":[{"type":"assessment","id":"A1","label":"l","excerpt":"budget overrun on discovery"}]}"""),
            ("assessments", """{"items":[{"sprk_kpiassessmentid":"A1","sprk_assessmentnotes":"All deadlines met."},{"sprk_kpiassessmentid":"A2","sprk_assessmentnotes":"A budget overrun on discovery."}]}"""));

        var data = (await Grounding().ExecuteAsync(context, CancellationToken.None)).StructuredData!.Value;

        data.GetProperty("NotFoundCount").GetInt32().Should().Be(1, "the quote is in A2's notes, not in A1's, which the citation names");
        data.GetProperty("GroundedOutput").GetProperty("citations").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("""{"citationsFrom":"synthesis"}""", "sourceChunksFrom (or a non-empty 'sources' list) is required")]
    [InlineData("""{"citationsFrom":"synthesis","sources":[{"jsonPath":"items"}]}""", "Every ConfigJson.sources entry requires 'from'")]
    public void GroundingVerify_Validate_RejectsAMissingSource(string config, string expected)
    {
        Grounding().Validate(GroundingContext(config)).Errors.Should().ContainSingle().Which.Should().Contain(expected);
    }

    [Fact]
    public async Task GroundingVerify_AReplyWithADuplicateKey_StillGrounds()
    {
        var context = GroundingContext(
            """{"citationsFrom":"synthesis","citationsJsonPath":"citations","sources":[{"from":"assessments","jsonPath":"items"}]}""",
            ("synthesis", """{"body":"first","body":"second","citations":[{"type":"assessment","id":"A1","label":"l","excerpt":"all deadlines were met"}]}"""),
            ("assessments", """{"items":[{"sprk_kpiassessmentid":"A1","sprk_assessmentnotes":"All deadlines were met."}]}"""));

        var output = await Grounding().ExecuteAsync(context, CancellationToken.None);

        output.Success.Should().BeTrue(output.ErrorMessage);
        output.StructuredData!.Value.GetProperty("GroundedOutput").GetProperty("citations").GetArrayLength().Should().Be(1);
    }

    // ── AgentService: an unresolved $ref prompt, and a placeholder left unfilled ─────────────

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AgentService_ALiteralRefPrompt_CountsAsUnset(bool actionHasPrompt, bool expectedValid)
    {
        var executor = new AgentServiceNodeExecutor(DisabledAgentClient(),
            new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger<AgentServiceNodeExecutor>.Instance);
        var context = AgentContext("""{"tenantId":"t","prompt":"$ref:Services/Ai/Insights/Prompts/predict.txt"}""",
            actionHasPrompt ? SynthesisJps : null);

        executor.Validate(context).IsValid.Should().Be(expectedValid);
        if (actionHasPrompt)
        {
            var config = new AgentServiceNodeConfig { TenantId = "t", Prompt = "$ref:Services/Ai/Insights/Prompts/predict.txt" };
            AgentServiceNodeExecutor.ResolvePrompt(context, config, new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger.Instance)
                .Should().Contain("Spaarke Insights Engine synthesizer", "the Action's prompt is used instead of the literal reference");
        }
    }

    [Fact]
    public async Task AgentService_APlaceholderLeftInThePrompt_FailsBeforeAnyAgentCall()
    {
        var executor = new AgentServiceNodeExecutor(DisabledAgentClient(),
            new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger<AgentServiceNodeExecutor>.Instance);
        // No templateParameters: the Action's {{matterId}} etc. stay in the rendered prompt.
        var context = AgentContext("""{"tenantId":"t"}""", SynthesisJps);

        var output = await executor.ExecuteAsync(context, CancellationToken.None);

        output.Success.Should().BeFalse();
        output.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed,
            "the disabled client would have answered NODE_AGENT_FEATURE_DISABLED had the agent been called");
        output.ErrorMessage.Should().Contain("unrendered {{placeholder}}");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public const string VerbatimExcerpt = "missed the status update deadline for the third consecutive period";
        public const string ReplyBody = "## Matter Health\n\nGuideline Compliance is slipping: \"deadlines\" were missed; the notes quote {{templateLike}} braces.";

        private readonly IReadOnlyList<string> _notes;
        private readonly Mock<INodeExecutorRegistry> _registry = new();
        private readonly Mock<IScopeResolverService> _scopes = new();
        private readonly Mock<INodeService> _nodes = new();

        private readonly string? _currentGrade;

        public Harness(IReadOnlyList<string> assessmentNotes, string? currentGrade = null)
        {
            _notes = assessmentNotes;
            _currentGrade = currentGrade;
            AssessmentIds = assessmentNotes.Select(_ => Guid.NewGuid()).ToList();
        }

        public List<Guid> AssessmentIds { get; }
        public string? SentPrompt { get; private set; }
        public string? IndexFilter { get; private set; }
        public string? PersistedValue { get; private set; }

        public async Task<(List<PlaybookStreamEvent> Events, InsightsEngineRunResult Result)> RunAsync()
        {
            var playbookId = Guid.NewGuid();
            var nodes = LoadRepoNodes();
            _nodes.Setup(n => n.GetNodesAsync(playbookId, It.IsAny<CancellationToken>())).ReturnsAsync(nodes);
            _scopes.Setup(s => s.GetActionAsync(SynthesisActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction { Id = SynthesisActionId, Name = "Matter Health Synthesis (Single Mode)", SystemPrompt = SynthesisJps });
            RegisterExecutors();

            var router = new Mock<IInsightsActionRouter>();
            router.Setup(r => r.ResolveLayer1ActionAsync(It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? _, AnalysisAction a, CancellationToken _) => a);
            router.Setup(r => r.ResolveLayer2ActionAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? _, string? __, AnalysisAction a, CancellationToken _) => InsightsLayer2RoutingResult.PassThrough(a));

            var orchestrator = new PlaybookOrchestrationService(
                _nodes.Object, _registry.Object, _scopes.Object, new Mock<IAnalysisOrchestrationService>().Object,
                router.Object, new TemplateEngine(NullLogger<TemplateEngine>.Instance),
                NullLogger<PlaybookOrchestrationService>.Instance);

            var request = new PlaybookRunRequest
            {
                PlaybookId = playbookId,
                DocumentIds = [],
                Parameters = _currentGrade is null
                    ? new Dictionary<string, string> { ["matterId"] = MatterId.ToString(), ["tenantId"] = TenantId }
                    : new Dictionary<string, string> { ["matterId"] = MatterId.ToString(), ["tenantId"] = TenantId, ["currentGrade"] = _currentGrade },
            };
            var http = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim("tid", TenantId)], "Test")),
            };

            var events = new List<PlaybookStreamEvent>();
            var cache = new InsightsPlaybookExecutionCache(new Mock<ITenantCache>().Object, NullLogger<InsightsPlaybookExecutionCache>.Instance);
            var result = await cache.GetOrExecuteAsync(
                new InsightsPlaybookExecutionRequest(playbookId, $"matter:{MatterId}", request.Parameters, "scope-hash", TenantId),
                ct => Capture(orchestrator.ExecuteAsync(request, http, ct), events),
                CancellationToken.None);

            return (events, result);
        }

        private static async IAsyncEnumerable<PlaybookStreamEvent> Capture(
            IAsyncEnumerable<PlaybookStreamEvent> source, List<PlaybookStreamEvent> sink)
        {
            await foreach (var e in source)
            {
                sink.Add(e);
                yield return e;
            }
        }

        public IReadOnlyList<string> IndexFilterFieldsMissingFromSchema()
        {
            var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), IndexSchemaPath)))!;
            var filterable = new HashSet<string>(StringComparer.Ordinal);
            void Walk(JsonArray fields, string prefix)
            {
                foreach (var f in fields)
                {
                    var name = prefix + f!["name"]!.GetValue<string>();
                    if (f["filterable"]?.GetValue<bool>() == true) filterable.Add(name);
                    if (f["fields"] is JsonArray nested) Walk(nested, name + "/");
                }
            }
            Walk(schema["fields"]!.AsArray(), "");

            var used = System.Text.RegularExpressions.Regex.Matches(IndexFilter ?? "", @"([A-Za-z][\w/]*)\s+(?:eq|ne|gt|lt|ge|le)\s")
                .Select(m => m.Groups[1].Value).Distinct();
            return used.Where(f => !filterable.Contains(f)).ToList();
        }

        private PlaybookNodeDto[] LoadRepoNodes()
        {
            var definition = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), PlaybookPath)))!;
            var raw = definition["nodes"]!.AsArray().Select(n => n!.AsObject()).ToList();
            var ids = raw.ToDictionary(n => n["name"]!.GetValue<string>(), _ => Guid.NewGuid());
            return raw.Select((n, i) =>
            {
                var name = n["name"]!.GetValue<string>();
                var type = (ExecutorType)n["actionType"]!.GetValue<int>();
                return new PlaybookNodeDto
                {
                    Id = ids[name],
                    Name = name,
                    OutputVariable = n["outputVariable"]!.GetValue<string>(),
                    ExecutionOrder = i + 1,
                    DependsOn = (n["dependsOn"] as JsonArray)?.Select(d => ids[d!.GetValue<string>()]).ToArray() ?? [],
                    IsActive = true,
                    NodeType = NodeType.Workflow,
                    SprkExecutortype = type,
                    ActionId = type == ExecutorType.AgentService ? SynthesisActionId : Guid.Empty,
                    ConfigJson = n["configJson"]!.ToJsonString(),
                };
            }).ToArray();
        }

        private void RegisterExecutors()
        {
            // Live facts (boundary: Dataverse matter read).
            _registry.Setup(r => r.GetExecutor(ExecutorType.LiveFact)).Returns(Fake((ctx, _) =>
                Task.FromResult(NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new FactArtifact
                {
                    Id = "fact:1", Subject = $"matter:{MatterId}", Predicate = "currentMatterFacts",
                    Value = new Value { Raw = JsonSerializer.SerializeToElement(new { matterType = "Patent Negotiation", attorney = new { name = "J. Singh" } }), DisplayHint = "text" },
                    Evidence = [], AsOf = DateTimeOffset.UtcNow, ProducedBy = new ProducedBy { Kind = "resolver", Id = "matter" },
                    Scope = new Scope { TenantId = TenantId }, TenantId = TenantId,
                }))));

            // Real QueryDataverse executor over a Dataverse fake that rejects columns the table does not have.
            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FetchExpression fetch, CancellationToken _) => Assessments(fetch.Query));
            _registry.Setup(r => r.GetExecutor(ExecutorType.QueryDataverse)).Returns(new QueryDataverseNodeExecutor(
                new TemplateEngine(NullLogger<TemplateEngine>.Instance), entities.Object, NullLogger<QueryDataverseNodeExecutor>.Instance));

            // AI Search (boundary): record the filter IndexRetrieveNode would send; return no rows.
            _registry.Setup(r => r.GetExecutor(ExecutorType.IndexRetrieve)).Returns(Fake((ctx, _) =>
            {
                var config = JsonSerializer.Deserialize<IndexRetrieveNodeConfig>(ctx.Node.ConfigJson!)!;
                IndexFilter = IndexRetrieveNode.BuildFilter(ctx.TenantId, config);
                return Task.FromResult(NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, new IndexRetrieveOutput
                {
                    IndexName = "spaarke-insights-index", Count = 0, TotalCount = 0, Artifacts = [],
                }));
            }));

            // Foundry transport (boundary): the real executor's validation, prompt resolution and reply parsing.
            var renderer = new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance);
            var agent = new AgentServiceNodeExecutor(DisabledAgentClient(), renderer, NullLogger<AgentServiceNodeExecutor>.Instance);
            _registry.Setup(r => r.GetExecutor(ExecutorType.AgentService)).Returns(Fake((ctx, _) =>
            {
                agent.Validate(ctx).IsValid.Should().BeTrue(string.Join("; ", agent.Validate(ctx).Errors));
                var config = JsonSerializer.Deserialize<AgentServiceNodeConfig>(ctx.Node.ConfigJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                SentPrompt = AgentServiceNodeExecutor.ResolvePrompt(ctx, config, renderer, NullLogger.Instance);
                return Task.FromResult(AgentServiceNodeExecutor.BuildOutput(ctx, "thread-1", Reply(), DateTimeOffset.UtcNow));
            }));

            _registry.Setup(r => r.GetExecutor(ExecutorType.EvidenceSufficiency)).Returns(new EvidenceSufficiencyNode(NullLogger<EvidenceSufficiencyNode>.Instance));
            _registry.Setup(r => r.GetExecutor(ExecutorType.GroundingVerify)).Returns(new GroundingVerifyNode(
                new GroundingVerifier(NullLogger<GroundingVerifier>.Instance), NullLogger<GroundingVerifyNode>.Instance));
            _registry.Setup(r => r.GetExecutor(ExecutorType.ReturnInsightArtifact)).Returns(new ReturnInsightArtifactNode(NullLogger<ReturnInsightArtifactNode>.Instance));
            _registry.Setup(r => r.GetExecutor(ExecutorType.DeclineToFind)).Returns(new DeclineToFindNode(NullLogger<DeclineToFindNode>.Instance));

            // The REAL UpdateRecord executor (it renders the value again, sweep PB-25); the Dataverse write is the fake.
            var dataverse = new Mock<IDataverseService>();
            dataverse.Setup(d => d.UpdateRecordFieldsAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
                .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((entity, id, fields, _, _) =>
                {
                    entity.Should().Be("sprk_matter");
                    id.Should().Be(MatterId);
                    PersistedValue = fields["sprk_performancesummary"] as string;
                })
                .Returns(Task.CompletedTask);
            _registry.Setup(r => r.GetExecutor(ExecutorType.UpdateRecord)).Returns(new UpdateRecordNodeExecutor(
                new TemplateEngine(NullLogger<TemplateEngine>.Instance), dataverse.Object, MatterMetadataScope(dataverse.Object),
                NullLogger<UpdateRecordNodeExecutor>.Instance));
        }

        // MetadataService (sealed, no seam) over a cache that already holds sprk_matter's metadata: the column is a Memo.
        private static IServiceScopeFactory MatterMetadataScope(IDataverseService dataverse)
        {
            var dto = new EntityMetadataDto(
                LogicalName: "sprk_matter", PrimaryIdAttribute: "sprk_matterid", PrimaryNameAttribute: "sprk_mattername",
                Attributes: [new AttributeDto("sprk_performancesummary", "Memo", null, false, false, null)]);
            var cache = new Mock<IDistributedCache>();
            cache.Setup(c => c.GetAsync("sdap:dv:entitymetadata:sprk_matter", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })));
            var metadata = new MetadataService(dataverse, cache.Object, NullLogger<MetadataService>.Instance);
            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(MetadataService))).Returns(metadata);
            var scope = new Mock<IServiceScope>();
            scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
            var factory = new Mock<IServiceScopeFactory>();
            factory.Setup(f => f.CreateScope()).Returns(scope.Object);
            return factory.Object;
        }

        private string Reply() => JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            body = ReplyBody,
            citations = new object[]
            {
                new { type = "assessment", id = AssessmentIds[0].ToString(), label = "Guideline Compliance", excerpt = VerbatimExcerpt },
                new { type = "assessment", id = Guid.NewGuid().ToString(), label = "Invented", excerpt = "the client praised the outstanding responsiveness" },
            },
            generatedAt = "2026-10-09T00:00:00Z",
            playbookName = "matter-health-single",
            tenantId = TenantId,
            dimensions = new[] { "composite-grade", "trend", "themes" },
        });

        private EntityCollection Assessments(string fetchXml)
        {
            var entity = XDocument.Parse(fetchXml).Descendants("entity").Single();
            entity.Attribute("name")!.Value.Should().Be("sprk_kpiassessment");
            var referenced = entity.Descendants("attribute").Select(a => a.Attribute("name")!.Value)
                .Concat(entity.Descendants("condition").Select(c => c.Attribute("attribute")!.Value))
                .Concat(entity.Descendants("order").Select(o => o.Attribute("attribute")!.Value));
            foreach (var column in referenced)
            {
                if (!KpiAssessmentColumns.Contains(column))
                    throw new InvalidOperationException($"'sprk_kpiassessment' entity doesn't contain attribute with Name = '{column}'");
            }

            var areas = new[] { 100000000, 100000001, 100000002 };
            var rows = _notes.Select((notes, i) =>
            {
                var row = new Entity("sprk_kpiassessment", AssessmentIds[i]);
                row["sprk_kpiassessmentid"] = AssessmentIds[i];
                row["sprk_kpiname"] = $"KPI {i + 1}";
                row["sprk_performancearea"] = new OptionSetValue(areas[i % 3]);
                row["sprk_kpigradescore"] = new OptionSetValue(100000003 + i);
                row["sprk_assessmentnotes"] = notes;
                row["createdon"] = new DateTime(2026, 2, 1 + i, 0, 0, 0, DateTimeKind.Utc);
                return row;
            });
            return new EntityCollection(rows.ToList());
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static PlaybookNodeDto Node(string name, string outputVariable, ExecutorType type, params Guid[] dependsOn) => new()
    {
        Id = Guid.NewGuid(), Name = name, OutputVariable = outputVariable, ExecutionOrder = 1, DependsOn = dependsOn,
        IsActive = true, NodeType = NodeType.Workflow, SprkExecutortype = type, ActionId = Guid.Empty,
    };

    private static INodeExecutor Recording(List<string> ran, Func<NodeExecutionContext, object> data) => Fake((ctx, _) =>
    {
        lock (ran) { ran.Add(ctx.Node.Name); }
        return Task.FromResult(NodeOutput.Ok(ctx.Node.Id, ctx.Node.OutputVariable, data(ctx)));
    });

    private static INodeExecutor Fake(Func<NodeExecutionContext, CancellationToken, Task<NodeOutput>> execute)
    {
        var executor = new Mock<INodeExecutor>();
        executor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>())).Returns(NodeValidationResult.Success());
        executor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns((NodeExecutionContext ctx, CancellationToken ct) => execute(ctx, ct));
        return executor.Object;
    }

    private static async Task<List<PlaybookStreamEvent>> RunGraphAsync(INodeExecutorRegistry registry, params PlaybookNodeDto[] nodes) =>
        (await RunGraphWithServiceAsync(registry, false, nodes)).Events;

    private static async Task<List<PlaybookStreamEvent>> RunGraphAsync(
        INodeExecutorRegistry registry, bool gateFailLayer2, params PlaybookNodeDto[] nodes) =>
        (await RunGraphWithServiceAsync(registry, gateFailLayer2, nodes)).Events;

    private static async Task<(List<PlaybookStreamEvent> Events, PlaybookOrchestrationService Orchestrator)> RunGraphWithServiceAsync(
        INodeExecutorRegistry registry, bool gateFailLayer2, params PlaybookNodeDto[] nodes)
    {
        var playbookId = Guid.NewGuid();
        var nodeService = new Mock<INodeService>();
        nodeService.Setup(n => n.GetNodesAsync(playbookId, It.IsAny<CancellationToken>())).ReturnsAsync(nodes);
        var router = new Mock<IInsightsActionRouter>();
        router.Setup(r => r.ResolveLayer2ActionAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, string? __, AnalysisAction a, CancellationToken _) => gateFailLayer2
                ? new InsightsLayer2RoutingResult(InsightsLayer2RoutingDecision.GateFailNullActionCode, a)
                : InsightsLayer2RoutingResult.PassThrough(a));
        router.Setup(r => r.ResolveLayer1ActionAsync(It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, AnalysisAction a, CancellationToken _) => a);
        var orchestrator = new PlaybookOrchestrationService(
            nodeService.Object, registry, new Mock<IScopeResolverService>().Object, new Mock<IAnalysisOrchestrationService>().Object,
            router.Object, new TemplateEngine(NullLogger<TemplateEngine>.Instance), NullLogger<PlaybookOrchestrationService>.Instance);

        var events = new List<PlaybookStreamEvent>();
        await foreach (var e in orchestrator.ExecuteAsync(
                           new PlaybookRunRequest { PlaybookId = playbookId, DocumentIds = [] }, new DefaultHttpContext(), CancellationToken.None))
        {
            events.Add(e);
        }

        return (events, orchestrator);
    }

    private static GroundingVerifyNode Grounding() =>
        new(new GroundingVerifier(NullLogger<GroundingVerifier>.Instance), NullLogger<GroundingVerifyNode>.Instance);

    private static NodeExecutionContext GroundingContext(string configJson, params (string Variable, string Json)[] upstreams)
    {
        var previous = new Dictionary<string, NodeOutput>();
        foreach (var (variable, json) in upstreams)
        {
            using var doc = JsonDocument.Parse(json);
            previous[variable] = new NodeOutput
            {
                NodeId = Guid.NewGuid(), OutputVariable = variable, Success = true,
                StructuredData = doc.RootElement.Clone(), Metrics = NodeExecutionMetrics.Empty,
            };
        }

        return AgentContext(configJson, null) with { PreviousOutputs = previous };
    }

    private static NodeExecutionContext AgentContext(string configJson, string? actionPrompt)
    {
        var actionId = Guid.NewGuid();
        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(), Name = "synthesize", OutputVariable = "synthesis", ActionId = actionId,
                ExecutionOrder = 1, IsActive = true, ConfigJson = configJson, SprkExecutortype = ExecutorType.AgentService,
            },
            Action = new AnalysisAction { Id = actionId, Name = "synthesis", SystemPrompt = actionPrompt ?? string.Empty },
            ExecutorType = ExecutorType.AgentService,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = TenantId,
            PreviousOutputs = new Dictionary<string, NodeOutput>(),
            Parameters = new Dictionary<string, string>(),
        };
    }

    private static Sprk.Bff.Api.Services.Ai.Foundry.AgentServiceClient DisabledAgentClient() => new(
        Microsoft.Extensions.Options.Options.Create(new Sprk.Bff.Api.Services.Ai.Foundry.AgentServiceOptions { Enabled = false }),
        new Mock<ITenantCache>().Object,
        new Mock<Azure.Core.TokenCredential>().Object,
        NullLogger<Sprk.Bff.Api.Services.Ai.Foundry.AgentServiceClient>.Instance);

    private static string Describe(IEnumerable<PlaybookStreamEvent> events) =>
        string.Join(" | ", events.Select(e => $"{e.Type}:{e.NodeName}:{e.Error}"));

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
