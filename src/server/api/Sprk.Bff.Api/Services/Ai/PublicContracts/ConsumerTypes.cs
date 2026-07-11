namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Canonical set of consumer-type identifiers used by
/// <see cref="IConsumerRoutingService.ResolveAsync"/>. Each constant is the
/// stable string key that BFF consumers pass when resolving their playbook
/// and that admins set in the <c>sprk_consumertype</c> column of the
/// <c>sprk_playbookconsumer</c> Dataverse table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class exists</b>: <c>sprk_consumertype</c> is free-text on the
/// Dataverse side (NVARCHAR(250)) — admins can type anything in Power Apps.
/// The 2026-06-24 UAT-2 incident (Matter pre-fill broken because the Power
/// Apps form received <c>matter-pre-fil</c> missing the final l) is the
/// failure mode this class defends against on the BFF side. By referencing
/// <c>ConsumerTypes.MatterPreFill</c> rather than the literal string
/// <c>"matter-pre-fill"</c>, callers cannot typo the consumer-type code
/// — the compiler catches it.
/// </para>
/// <para>
/// <b>The Dataverse-side typo class is NOT prevented by this code</b>. A
/// future enhancement (suggestion S-5C from the 2026-06-24 code review)
/// is a startup health log that compares the Dataverse-side consumertypes
/// against this constant list and warns on mismatch. That is queued for
/// task 028e (Phase 1R exit gate).
/// </para>
/// <para>
/// <b>Adding a new consumer type</b>:
/// </para>
/// <list type="number">
///   <item>Add a <c>public const string</c> here.</item>
///   <item>Create the corresponding <c>sprk_playbookconsumer</c> row(s) in
///         Dataverse (or extend <c>scripts/dataverse/Seed-PlaybookConsumers.ps1</c>).</item>
///   <item>Update the relevant consumer (or new consumer) to inject
///         <see cref="IConsumerRoutingService"/> and call
///         <c>ResolveAsync(ConsumerTypes.YourNewType)</c>.</item>
/// </list>
/// </remarks>
public static class ConsumerTypes
{
    /// <summary>
    /// <c>MatterPreFillService</c> (workspace) — pre-fills a new Matter form
    /// from uploaded documents (NFR-07 contract preserved).
    /// </summary>
    public const string MatterPreFill = "matter-pre-fill";

    /// <summary>
    /// <c>ProjectPreFillService</c> (workspace) — pre-fills a new Project
    /// form from uploaded documents (NFR-07 contract preserved).
    /// </summary>
    public const string ProjectPreFill = "project-pre-fill";

    /// <summary>
    /// <c>WorkspaceAiService</c> — generates the workspace tile AI summary
    /// (Document Profile playbook).
    /// </summary>
    public const string AiSummary = "ai-summary";

    /// <summary>
    /// <c>WorkspaceFileEndpoints</c> — file summarization endpoint behind
    /// the Workspace summarize button (Summarize File playbook).
    /// </summary>
    public const string SummarizeFile = "summarize-file";

    /// <summary>
    /// the /summarize dispatch path — chat-side summarize-document
    /// flow (summarize-document-for-chat@v1 playbook).
    /// </summary>
    public const string ChatSummarize = "chat-summarize";

    /// <summary>
    /// <c>EventRulesService</c> — document_uploaded event-rule member order 1
    /// (UC-A-7 Layer-0 classification; CLS-CHAT@v1 prompted Action). Added by
    /// spaarke-ai-architecture-redesign-r1 task 022 (FR-P1-03); the Binding row's
    /// <c>sprk_oneventbindings</c> carries <c>[{"event":"document_uploaded","order":1}]</c>.
    /// </summary>
    public const string ChatClassify = "chat-classify";

    /// <summary>
    /// <c>AppOnlyAnalysisService</c> — email analysis pipeline (Email
    /// Analysis playbook, app-only execution context).
    /// </summary>
    public const string EmailAnalysis = "email-analysis";

    /// <summary>
    /// <c>DailyBriefingCompositeService</c> — the Daily Briefing coded composite
    /// (FR-P3-04, spaarke-ai-architecture-redesign-r1 task 043; the platform's FIRST
    /// full <c>coded</c> Action). Two Binding rows: <c>default</c> (informational —
    /// widget render via /render + /narrate) and <c>email</c> (email disposition —
    /// Communication-service delivery via /email; scheduled trigger declared in
    /// <c>sprk_oneventbindings</c>). Resolves the Action's <c>sprk_workflowclass</c>
    /// through <see cref="IConsumerRoutingService.ResolveBindingAsync"/>.
    /// </summary>
    public const string DailyBriefingNarrate = "daily-briefing-narrate";

    /// <summary>
    /// the AnalysisEndpoints document-profile pipeline — Document Upload / Profile Document
    /// linear consumer. R7 Wave 12 (2026-07-02) — migrated off the Playbook
    /// Engine per <c>docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md</c>.
    /// Routes via the <c>sprk_playbookconsumer</c> routing table
    /// (Wave 12.3 retired the config-map lookup). FR-P3-01 (task 040) deleted
    /// the <c>LinearConsumers:PlaybookIds</c> reverse-lookup config too:
    /// <c>AnalysisEndpoints.ExecuteAnalysis</c> now detects a Document Profile
    /// dispatch by comparing the incoming legacy <c>PlaybookId</c> against this
    /// Binding row's <c>sprk_playbook</c> lookup (preserving the client
    /// contract with the Binding table as the only routing surface).
    /// </summary>
    public const string DocumentProfile = "document-profile";

    /// <summary>
    /// <c>ComposeSummarize</c> — Compose drafting-workspace summarize flow
    /// (spaarkeai-compose-r1; `ConsumerTypes.ComposeSummarize` in that
    /// project's branch). Constant added here 2026-07-05 by
    /// spaarke-ai-architecture-redesign-r1 gate 014: the Binding row exists on
    /// spaarkedev1 and the FR-P0-04 boot reconciliation requires constants ↔
    /// rows parity. Identical declaration expected from the compose-r1 merge.
    /// </summary>
    public const string ComposeSummarize = "compose-summarize";

    /// <summary>
    /// The per-tenant honest-refusal capability (FR-P2-04 / ADR-039 grounded-execution
    /// clause (d); canonical doc §3.10.7.2 Layer 4). When the agent-turn loop matches
    /// nothing in the closed catalog and cannot ground a cited ad-hoc answer, it
    /// invokes THIS Binding — the refusal template is catalog data (the Binding's
    /// REF-CHAT@v1 prompted Action), never hardcoded copy. Projected into the loop
    /// as <c>RefusalCapabilityTool</c>; emits <c>dispatch_refused</c> telemetry
    /// (the refusal-backlog product signal). Added by
    /// spaarke-ai-architecture-redesign-r1 task 033.
    /// </summary>
    public const string NoMatchHandler = "no_match_handler";

    /// <summary>
    /// The draft-correspondence proving capability (FR-P3-02 / spec §Owner
    /// Clarifications): a prompted Action (DRAFT-CORR@v1) that composes professional
    /// correspondence grounded in the session's documents and ledger outputs, projected
    /// into the agent loop as <c>capability_draft-correspondence</c>. The companion
    /// <c>email.draft</c> typed tool (declared <c>side_effect_class: communicate</c> —
    /// confirmation-gated) materializes the reviewed draft as a Spaarke
    /// <c>sprk_communication</c> DRAFT record in the Communication (Email) service —
    /// NOT an Outlook draft; sending stays user-initiated there (DRAFT-ONLY; FR-P4-07
    /// defers assistant-initiated send). Added by spaarke-ai-architecture-redesign-r1
    /// task 041.
    /// </summary>
    public const string DraftCorrespondence = "draft-correspondence";

    /// <summary>
    /// The Insights Engine playbook-path catalog (FR-P3-01,
    /// spaarke-ai-architecture-redesign-r1 task 040). Replaces the deleted
    /// Insights playbook-name appsettings map (+ its default-name config
    /// key — both retired at the cutover): the canonical playbook name
    /// (e.g. <c>matter-health-single</c>, <c>universal-ingest@v1</c>) is the
    /// Binding row's <c>sprk_consumercode</c>; the per-environment
    /// <c>sprk_analysisplaybook</c> GUID is its <c>sprk_playbook</c> lookup.
    /// Readers (<c>InsightEndpoints</c>, <c>AssistantToolCallHandler</c>,
    /// <c>InsightsOrchestrator</c>, <c>TopicRegistryTtlLookup</c>) resolve via
    /// <see cref="IConsumerRoutingService.ResolveBindingAsync"/> with EXACT
    /// consumer-code matching (the default-row fallback is rejected so unknown
    /// canonical names still fail clean). The <c>default</c> row is the
    /// Assistant-path default when the classifier supplies no hint.
    /// </summary>
    public const string InsightsAsk = "insights-ask";

    /// <summary>
    /// The Insights Engine RAG search surface (FR-P3-01, task 040 — catalog
    /// registration). <c>POST /api/insights/search</c> wraps <c>IRagService</c>
    /// directly (no engine target), so no code resolves this Binding today; the
    /// row exists for FR-P0-04 constants ↔ rows parity and anchors the later
    /// loop-side insights capability projection.
    /// </summary>
    public const string InsightsSearch = "insights-search";

    /// <summary>
    /// The create-task capability (FR-P3-03 / UC-H-1, canonical walkthrough steps
    /// 10-14): a prompted Action (CREATE-TASK@v1) that drafts a well-formed follow-up
    /// task proposal grounded in the session's documents and ledger outputs, projected
    /// into the agent loop as <c>capability_create-task</c>. The Action's input schema
    /// declares <c>due_date</c> + <c>assign_to</c> REQUIRED, so missing values produce
    /// the FR-P2-03 loop-native clarifying turn. The WRITE leg is the EXISTING
    /// <c>dataverse.create_record</c> typed handler (declared
    /// <c>side_effect_class: write</c> — confirmation-gated; executed on confirm by
    /// <c>TypedHandlerResumeExecutor</c>) creating <c>sprk_event</c> with
    /// <c>sprk_eventtype_ref = Task</c>, carrying provenance refs (source document +
    /// source analysis <c>{bindingId}@t{n}</c>) per the Binding's tool description.
    /// Added by spaarke-ai-architecture-redesign-r1 task 042.
    /// </summary>
    public const string CreateTask = "create-task";

    /// <summary>
    /// <c>ComposeEditor</c> BubbleMenu toolbar (Compose R2 FR-07, task 040) —
    /// read-only clause explanation grounded in matter/firm playbook and
    /// precedent context. Consumes the <c>compose-selection</c> scope;
    /// Informational disposition (no edit). Constant registered by core task
    /// AIR2-E42 per the compose-r2 handoff (B5,
    /// <c>notes/HANDOFF-to-redesign-r2-ai-execution-foundation.md</c>); the
    /// live Binding row (<c>sprk_playbookconsumer</c>) is staged mirror-first
    /// at <c>infra/dataverse/sprk_playbookconsumer-rows.json</c> and deployed
    /// by compose-r2 task 047 (deploy gate). Registering the constant here
    /// ahead of that deploy is deliberate: it lets the deployed row land
    /// without a code change, at the cost of an interim
    /// <see cref="RoutingConsumerTypeHealthCheck"/> <c>ConstantsWithoutRows</c>
    /// finding on any environment where the row is not yet live.
    /// </summary>
    public const string ComposeExplainClause = "compose-explain-clause";

    /// <summary>
    /// <c>ComposeEditor</c> BubbleMenu toolbar (Compose R2 FR-08, task 041) —
    /// read-only comparison of the selected clause against the matter/firm
    /// playbook (matches, deviations, risk score). Consumes
    /// <c>compose-selection</c>; Informational disposition. See
    /// <see cref="ComposeExplainClause"/> remarks for the registration
    /// provenance shared by all five compose-r2 constants.
    /// </summary>
    public const string ComposeCompareToPlaybook = "compose-compare-to-playbook";

    /// <summary>
    /// The return-from-Word summarization flow (Compose R2 FR-10, task 043) —
    /// plain-language summary of a reviewer's Word tracked changes
    /// (insertions/deletions/comments). Consumes <c>compose-document</c>;
    /// Informational disposition; invoked by the return-from-Word flow
    /// (compose-r2 task 054). See <see cref="ComposeExplainClause"/> remarks.
    /// </summary>
    public const string ComposeSummarizeWordChanges = "compose-summarize-word-changes";

    /// <summary>
    /// The Compose defined-terms scan (Compose R2 FR-11, task 044) —
    /// read-only consistency check of a document's defined terms, rendered
    /// in the Context pane. Consumes <c>compose-document</c>; Informational
    /// disposition. See <see cref="ComposeExplainClause"/> remarks.
    /// </summary>
    public const string ComposeDefinedTerms = "compose-defined-terms";

    /// <summary>
    /// The ONE edit-producing Compose R2 capability (FR-09, task 042) —
    /// proposes an alternative rewrite of the selected clause as a pending
    /// track-change (structured edit + rationale + sources) the attorney
    /// accepts, replaces, or undoes. Declares the <c>compose</c> Binding
    /// disposition (<c>BindingDisposition.Compose</c>, 100000006) — the
    /// dispatched output is ledger-written before render (ADR-040) and
    /// undo/replace is ledger supersession, never client DOM undo. Consumes
    /// <c>compose-selection</c>. See <see cref="ComposeExplainClause"/>
    /// remarks for the registration provenance.
    /// </summary>
    public const string ComposeDraftAlternative = "compose-draft-alternative";

    /// <summary>
    /// The create-matter capability (FR-A1-13 / UC-B-6, DEF-003 / #593,
    /// spaarke-ai-architecture-redesign-r2 task 042): a prompted Action
    /// (CREATE-MATTER@v1) that DRAFTS a proposed matter (name, description,
    /// practice-area/matter-type LABEL suggestions, source citations) grounded in
    /// the session's documents, projected into the agent loop as
    /// <c>capability_create-matter</c>. Authored EXACTLY like create-task (R19
    /// minimal shape — no forced-elicitation required fields; the model may still
    /// ask conversationally per the Binding's tool description). The WRITE leg is
    /// the EXISTING <c>dataverse.create_record</c> typed handler (declared
    /// <c>side_effect_class: write</c> — confirmation-gated; executed on confirm by
    /// <c>TypedHandlerResumeExecutor</c>) creating <c>sprk_matter</c>, resolving the
    /// practice-area/matter-type LABELS to <c>sprk_practicearea_ref</c> /
    /// <c>sprk_mattertype_ref</c> lookup GUIDs in-turn and carrying a provenance line
    /// naming the source document. Registered at the G-R2-A gate-deploy step together
    /// with its live Binding/Action row (per
    /// <c>projects/spaarke-ai-architecture-redesign-r2/notes/jps/create-matter-binding-row-pending-seed.json</c>
    /// deploySequence); flipping GU-065/066/067 to catalogStatus=existing is the
    /// atomic golden-utterance half of the same change.
    /// </summary>
    public const string CreateMatter = "create-matter";

    /// <summary>
    /// Read-only list of all consumer-type constants. Intended for startup
    /// health-log diffing against Dataverse (chat-routing-redesign-r1 task
    /// 028e exit gate).
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        MatterPreFill,
        ProjectPreFill,
        AiSummary,
        SummarizeFile,
        ChatSummarize,
        ChatClassify,
        EmailAnalysis,
        DailyBriefingNarrate,
        DocumentProfile,
        ComposeSummarize,
        NoMatchHandler,
        DraftCorrespondence,
        InsightsAsk,
        InsightsSearch,
        CreateTask,
        ComposeExplainClause,
        ComposeCompareToPlaybook,
        ComposeSummarizeWordChanges,
        ComposeDefinedTerms,
        ComposeDraftAlternative,
        CreateMatter,
    };
}
