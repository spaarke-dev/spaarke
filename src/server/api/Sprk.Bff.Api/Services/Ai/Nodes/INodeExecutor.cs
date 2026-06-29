using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Interface for node executors that process specific action types.
/// Each executor handles one or more ExecutorTypes and produces NodeOutput.
/// </summary>
/// <remarks>
/// <para>
/// Executors bridge the node-based orchestration system to actual
/// implementation. For example, AiAnalysisNodeExecutor delegates to
/// existing IAnalysisToolHandler implementations.
/// </para>
/// <para>
/// Follows the registry pattern per ADR-010 - executors register with
/// INodeExecutorRegistry for ExecutorType-based dispatch.
/// </para>
/// </remarks>
public interface INodeExecutor
{
    /// <summary>
    /// Gets the ExecutorTypes this executor can handle.
    /// Most executors handle a single type; some may handle multiple.
    /// </summary>
    IReadOnlyList<ExecutorType> SupportedActionTypes { get; }

    /// <summary>
    /// Executes the node and produces output.
    /// </summary>
    /// <param name="context">Execution context with node, scopes, and previous outputs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Node output containing results and metrics.</returns>
    Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates that the node configuration is valid for execution.
    /// Called before ExecuteAsync to fail fast on invalid inputs.
    /// </summary>
    /// <param name="context">Execution context to validate.</param>
    /// <returns>Validation result with success/failure and any error messages.</returns>
    NodeValidationResult Validate(NodeExecutionContext context);
}

/// <summary>
/// Coarse node category stored as a choice/option set on sprk_playbooknode.
/// Determines which scopes the orchestrator resolves before execution.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>AI — requires Action record + resolves Skills, Knowledge, Tools scopes</description></item>
/// <item><description>Output — structural; no Action or scopes needed</description></item>
/// <item><description>Control — structural; no Action or scopes needed</description></item>
/// <item><description>Workflow — future; rule-based actions, scope TBD</description></item>
/// </list>
/// </remarks>
public enum NodeType
{
    /// <summary>AI-powered node (analysis, completion, embedding). Requires Action + all scopes.</summary>
    AIAnalysis = 100_000_000,

    /// <summary>Delivery/output node. No Action or scopes — assembles previous outputs.</summary>
    Output = 100_000_001,

    /// <summary>Control flow node (condition, parallel, wait). No Action or scopes.</summary>
    Control = 100_000_002,

    /// <summary>Workflow action node (create task, send email, etc.). Future — scope TBD.</summary>
    Workflow = 100_000_003,

    /// <summary>
    /// Multi-section composite delivery node (FR-52 / Phase 5R Wave 5-C). Accepts N upstream
    /// Action node outputs keyed by declared <c>sectionName</c> (in <c>sprk_configjson</c>) and
    /// composes them into a single section map for the consumer (workspace widget, form prefill,
    /// or chat). Replaces the 5-coordination-point schema-on-action + schema-aware widget pattern
    /// with a 2-coordination-point (section name + section state) composition.
    /// <para>
    /// Backward-compat invariant: existing single-action <see cref="Output"/> nodes are UNCHANGED.
    /// This is a NEW node type; legacy playbooks with <see cref="Output"/> nodes continue to use
    /// <see cref="ExecutorType.DeliverOutput"/> and <c>DeliverOutputNodeExecutor</c>.
    /// </para>
    /// <para>
    /// Per-section SSE streaming (<c>section_started</c> / <c>section_data</c> / <c>section_completed</c>)
    /// is task 114a's territory; 114R (this addition) emits one composite output in-process and
    /// leaves a TODO for the per-section streaming integration point.
    /// </para>
    /// </summary>
    DeliverComposite = 100_000_004
}

/// <summary>
/// Executor types available in the node-based playbook system.
/// Maps to sprk_analysisaction.sprk_actiontype choice values.
/// </summary>
public enum ExecutorType
{
    /// <summary>AI analysis using tool handlers (existing pipeline).</summary>
    AiAnalysis = 0,

    /// <summary>Raw LLM completion with prompt template.</summary>
    AiCompletion = 1,

    /// <summary>Generate embeddings for text.</summary>
    AiEmbedding = 2,

    /// <summary>Business rules evaluation.</summary>
    RuleEngine = 10,

    /// <summary>Formula/computation.</summary>
    Calculation = 11,

    /// <summary>JSON/XML transformation.</summary>
    DataTransform = 12,

    /// <summary>Create Dataverse task.</summary>
    CreateTask = 20,

    /// <summary>Send email via Microsoft Graph.</summary>
    SendEmail = 21,

    /// <summary>Update Dataverse entity.</summary>
    UpdateRecord = 22,

    /// <summary>External HTTP webhook call.</summary>
    CallWebhook = 23,

    /// <summary>Teams notification.</summary>
    SendTeamsMessage = 24,

    /// <summary>Conditional branching.</summary>
    Condition = 30,

    /// <summary>Fork into parallel paths.</summary>
    Parallel = 31,

    /// <summary>Wait for human approval.</summary>
    Wait = 32,

    /// <summary>Start node — canvas anchor, pass-through with no execution logic.</summary>
    Start = 33,

    /// <summary>Render and deliver final output.</summary>
    DeliverOutput = 40,

    /// <summary>Queue document for RAG semantic indexing.</summary>
    DeliverToIndex = 41,

    /// <summary>
    /// Composite delivery (FR-52 / Phase 5R Wave 5-C task 114R). Gathers N upstream Action node
    /// outputs keyed by <c>sectionName</c> declared in the node's <c>sprk_configjson</c> and
    /// emits one composite output containing a section map plus destination + widget routing.
    /// Pairs with <see cref="NodeType.DeliverComposite"/> and
    /// <c>DeliverCompositeNodeExecutor</c>. Existing <see cref="DeliverOutput"/> behavior is
    /// UNCHANGED — this is a separate executor on a separate ExecutorType.
    /// </summary>
    DeliverComposite = 42,

    /// <summary>Create an in-app notification via the Dataverse appnotification entity.</summary>
    CreateNotification = 50,

    /// <summary>Execute a FetchXML query against Dataverse and return results.</summary>
    QueryDataverse = 51,

    /// <summary>
    /// Resolves the current user's record memberships for a given entity type via
    /// <c>IMembershipResolverService</c> (FR-1B.1). Emits the memberships into
    /// <c>NodeOutput.StructuredData</c> for downstream filter/template consumption.
    /// </summary>
    LookupUserMembership = 52,

    /// <summary>Routes the playbook node to Azure AI Foundry Agent Service (Phase 2).</summary>
    AgentService = 60,

    /// <summary>
    /// Mechanical zero-LLM citation verification — checks that quoted evidence from prior
    /// AI nodes matches the source chunks. Wraps <c>IGroundingVerifier</c> per D-P9 / D-47 /
    /// LAVERN ADR 10.6. Used in Insights synthesis playbooks (D-P14) and the ingest pipeline.
    /// </summary>
    GroundingVerify = 70,

    /// <summary>
    /// Resolves a deterministic Live Fact about a Dataverse subject (e.g.,
    /// <c>matter:M-1234.totalSpend</c>) via <c>ILiveFactResolver</c> and emits a
    /// <see cref="Models.Insights.FactArtifact"/> per design.md §2.1. Confidence is always 1.0.
    /// Used in Insights synthesis playbooks (D-P14) and the ingest pipeline per D-P12.
    /// </summary>
    LiveFact = 80,

    /// <summary>
    /// Retrieves Observations and Precedents from <c>spaarke-insights-index</c> via filter +
    /// vector search per D-P12 / SPEC §3.4.3 worked-example queries. Returns the retrieved
    /// artifacts in <c>NodeOutput.StructuredData</c> for downstream synthesis nodes
    /// (typically <see cref="AiCompletion"/>). Per D-A23 / D-48, validates non-empty result
    /// at runtime when the playbook config marks the retrieval as evidence-bearing.
    /// </summary>
    IndexRetrieve = 90,

    /// <summary>
    /// Reads prior node outputs and applies a configured evidence rule
    /// (e.g., <c>{minComparableMatters: 12}</c>); emits <c>sufficient</c> / <c>insufficient</c>
    /// verdict + structured gap analysis per D-P12 + D-49 (LAVERN Pattern #7). Used as the
    /// pre-condition gate before a <see cref="DeclineToFind"/> branch in synthesis playbooks.
    /// </summary>
    EvidenceSufficiency = 100,

    /// <summary>
    /// Deterministic exit that emits a structured <see cref="Models.Insights.DeclineResponse"/>
    /// (typed, not freely-composed prose) per D-49 / D-P12. Invoked when
    /// <see cref="EvidenceSufficiency"/> returns insufficient. Zero LLM — composes the response
    /// from upstream gap analysis + a config-driven template.
    /// </summary>
    DeclineToFind = 110,

    /// <summary>
    /// Final node of an Insights synthesis playbook. Serializes upstream node outputs into an
    /// <see cref="Models.Insights.InsightArtifact"/> envelope (typically an
    /// <see cref="Models.Insights.InferenceArtifact"/>) per D-P12 / D-P1 / design.md §2.2.
    /// Validates non-empty evidence (D-A23 / D-48 EvidenceGuard) before return — throws
    /// <c>EvidenceRequiredException</c> on empty evidence.
    /// </summary>
    ReturnInsightArtifact = 120,

    /// <summary>
    /// Sanitizes raw document text for downstream LLM consumption — strips control characters,
    /// retrieval blocks, and other noise per D-50 / D-A25 LAVERN sanitizer. Used as the first
    /// node of the universal-ingest@v1 JPS playbook (Wave C1 task 020). Wraps
    /// <c>IInsightsContentSanitizer</c>; pass-through for raw chunks (grounding needs them
    /// unmodified). NEW in Wave C1 per design-a5 §4 Node 1.
    /// </summary>
    Sanitization = 130,

    /// <summary>
    /// Emits N observations (one per surviving L2 candidate after grounding) + the L1
    /// classification observation. Used as the final node of the universal-ingest@v1 JPS
    /// playbook (Wave C1 task 020). Wraps <c>IObservationEmitter</c> +
    /// <c>IObservationIndexUpserter</c> + <c>IObservationMirror</c>. Replaced the
    /// observation-emission portion of the code-defined <c>IngestOrchestrator</c>
    /// (retired Wave C-G4 / task 022). NEW in Wave C1 per design-a5 §4 Node 6.
    /// </summary>
    ObservationEmit = 140,

    /// <summary>
    /// Post-LLM output scrubber. Validates entity names in LLM output against an allow-list
    /// and removes hallucinated names. Logs <c>hallucination_detected</c> event per removal.
    /// Slots into the post-LLM cluster alongside <see cref="Sanitization"/> (130) and
    /// <see cref="ObservationEmit"/> (140). NEW in R4 per spaarke-daily-update-service-r4
    /// FR-3 / AC-3a (PR 1 / W0).
    /// </summary>
    EntityNameValidator = 141,

    /// <summary>
    /// Canvas-only Control node — pass-through knowledge binding. For R4, evaluates
    /// optional <c>configJson.passthroughBinding</c> templates against scope variables
    /// and binds the resolved object map to the node's OutputVariable (default
    /// "channelRegistry"). Forward-compat for R5: when
    /// <c>configJson.r5BindingPlan.knowledgeSourceCode</c> is set, an info log is
    /// emitted so future R5 wiring can pick up the AI Search binding. Pairs with
    /// <see cref="Nodes.LoadKnowledgeNodeExecutor"/>. NEW in R4 daily-update-service-r4
    /// (control-flow-executors task, 2026-06-26) — closes UAT "LoadKnowledge"
    /// Condition-fallback validation failure.
    /// </summary>
    LoadKnowledge = 142,

    /// <summary>
    /// Canvas-only Control node — terminal "return response" projection. Reads
    /// <c>configJson.responseBinding</c> (a name→template map, plus optional
    /// <c>_validationMetadata</c> sidecar) and binds the resolved object to the
    /// node's OutputVariable (default "response"). The InvokePlaybookAi facade /
    /// playbook-execution method reads the final scope variable as the run return
    /// value. Missing template variables resolve to empty strings (does not throw).
    /// Pairs with <see cref="Nodes.ReturnResponseNodeExecutor"/>. NEW in R4
    /// daily-update-service-r4 (control-flow-executors task, 2026-06-26) — closes
    /// UAT "ReturnResponse" Condition-fallback validation failure.
    /// </summary>
    ReturnResponse = 143
}
