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
    /// <c>SessionSummarizeOrchestrator</c> — chat-side summarize-document
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
    /// <c>DocumentProfileService</c> — Document Upload / Profile Document
    /// linear consumer. R7 Wave 12 (2026-07-02) — migrated off the Playbook
    /// Engine per <c>docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md</c>.
    /// Routes via the <c>sprk_playbookconsumer</c> routing table
    /// (Wave 12.3 retired the config-map lookup); the corresponding entry in
    /// <see cref="LinearConsumers.LinearConsumersOptions.PlaybookIds"/> is
    /// used by <c>AnalysisEndpoints.ExecuteAnalysis</c> to dispatch when the
    /// incoming request's <c>PlaybookId</c> matches (preserving the client
    /// contract during migration).
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
    };
}
