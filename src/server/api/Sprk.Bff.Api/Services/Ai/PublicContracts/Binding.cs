using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// The full Binding contract — one resolved <c>sprk_playbookconsumer</c> row
/// (the invocation unit, canonical doc §6.2) joined with the execution fields
/// of its target <c>sprk_analysisaction</c> row (the execution unit, §6.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 single-routing-surface rule</b>: the Binding table is THE only
/// routing surface on the platform, and this record is its only contract.
/// Every later redesign phase consumes it — Event Rules read
/// <see cref="OnEventBindings"/> (P1), the Output Router reads
/// <see cref="Disposition"/> (P1), loop tool-projection reads
/// <see cref="ToolDescription"/> + <see cref="Surfaces"/> (P2), the
/// confirmation gate reads <see cref="Risk"/> (P2), chips read
/// <see cref="ChipTransitions"/> (Click path). Do NOT introduce a second
/// routing contract or side-channel routing config.
/// </para>
/// <para>
/// <b>Legacy-row tolerance (FR-P0-03)</b>: every task-003 column is optional
/// on the table; rows created before the schema extension carry nulls. The
/// mapping supplies the documented safe defaults — see each property. Choice
/// columns with unknown (future) option values also fall back to the same
/// defaults rather than throwing.
/// </para>
/// <para>
/// <b>JSON columns</b>: <c>sprk_chiptransitions</c> and
/// <c>sprk_oneventbindings</c> have shapes pinned by the column dictionary and
/// are parsed into typed lists (malformed maker JSON degrades to an empty list
/// — routing must never throw). <c>sprk_inputschema</c> is surfaced raw as
/// <see cref="InputSchemaJson"/> because its per-argument key vocabulary is
/// owned by the P1 prompted executor (ActionRunner), which performs the typed
/// parse at the point of use.
/// </para>
/// </remarks>
public sealed record Binding
{
    /// <summary>The <c>sprk_playbookconsumer</c> row id — the Binding's identity. Ledger outputs key on <c>{bindingId}@t{n}</c> (ADR-040).</summary>
    public required Guid BindingId { get; init; }

    /// <summary>Stable consumer-type code (<c>sprk_consumertype</c>), e.g. <c>chat-summarize</c>.</summary>
    public required string ConsumerType { get; init; }

    /// <summary>Sub-discriminator (<c>sprk_consumercode</c>); null/empty is treated as <c>"default"</c> by resolution.</summary>
    public string? ConsumerCode { get; init; }

    /// <summary>Environment scope (<c>sprk_environment</c>); null/empty/<c>*</c> = all environments.</summary>
    public string? Environment { get; init; }

    /// <summary>Resolution priority (<c>sprk_priority</c>); lower wins. Defaults to 500 when null.</summary>
    public int Priority { get; init; } = 500;

    /// <summary>Raw <c>sprk_matchconditions</c> JSON predicate (flat key → string | string[]). Null/empty always matches.</summary>
    public string? MatchConditionsJson { get; init; }

    /// <summary>Target <c>sprk_analysisplaybook</c> id (<c>sprk_playbook</c> lookup); null on pure-Linear rows.</summary>
    public Guid? PlaybookId { get; init; }

    /// <summary>Target <c>sprk_analysisaction</c> id (<c>sprk_action</c> lookup); null on pure-engine legacy rows.</summary>
    public Guid? ActionId { get; init; }

    // ── task-003 Binding columns (canonical §6.2) ───────────────────────────

    /// <summary>Use-case id (<c>sprk_ucid</c>, e.g. <c>UC-A-1</c>) tying the Binding to the §3 vocabulary. Null on legacy rows.</summary>
    public string? Ucid { get; init; }

    /// <summary>Maker-editable intent surface (<c>sprk_tooldescription</c>) the agent loop sees when this Binding is projected as a capability tool. Null on legacy rows.</summary>
    public string? ToolDescription { get; init; }

    /// <summary>Output routing disposition (<c>sprk_disposition</c>). Null/unknown defaults to <see cref="BindingDisposition.Informational"/> (canonical §5: informational is the platform default rendering).</summary>
    public BindingDisposition Disposition { get; init; } = BindingDisposition.Informational;

    /// <summary>Curated next-step chips (<c>sprk_chiptransitions</c> JSON, D4). Empty on legacy rows or malformed JSON.</summary>
    public IReadOnlyList<ChipTransition> ChipTransitions { get; init; } = Array.Empty<ChipTransition>();

    /// <summary>Confirmation-gate risk posture (<c>sprk_risk</c>). Null/unknown defaults to <see cref="BindingRisk.None"/> — legacy rows are informational capabilities; side effects are additionally gated by tool <c>side_effect_class</c> (the ONE gate), so this default is not the last line of defense.</summary>
    public BindingRisk Risk { get; init; } = BindingRisk.None;

    /// <summary>Missing-required-args capture mode (<c>sprk_capturemode</c>). Null/unknown defaults to <see cref="BindingCaptureMode.LoopElicitation"/> (per column dictionary).</summary>
    public BindingCaptureMode CaptureMode { get; init; } = BindingCaptureMode.LoopElicitation;

    /// <summary>Event-path memberships (<c>sprk_oneventbindings</c> JSON, e.g. <c>[{"event":"document_uploaded","order":2}]</c>). Empty on legacy rows or malformed JSON.</summary>
    public IReadOnlyList<OnEventBinding> OnEventBindings { get; init; } = Array.Empty<OnEventBinding>();

    /// <summary>Placement surface tokens parsed from comma-separated <c>sprk_surfaces</c> (§4.1 vocabulary: <c>assistant</c>, <c>record-form</c>, <c>wizard</c>, <c>office</c>, <c>external-spa</c>, <c>scheduler</c>, <c>inbound-email</c>). Empty = offered on ALL surfaces (per column dictionary).</summary>
    public IReadOnlyList<string> Surfaces { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Grounding precondition (<c>sprk_requiresnoattachedrecord</c>, FR-H1): when <c>true</c>, this
    /// capability is offered ONLY when the host chat context has NO attached/regarding record. The
    /// deterministic <c>AgentToolProjection.PreFilter</c> (task 044) removes the capability when the
    /// session's <see cref="Chat.AgentToolFilterContext.HasAttachedRecord"/> fact is true — e.g. it
    /// hides "Create matter" when already inside a matter. A pure predicate (removes-the-impossible),
    /// NOT a ranking weight and NEVER a model call or a tool-name list (ADR-039 §3.2). Null/unset =
    /// <c>false</c> (offered regardless of host record — the default for the vast majority of rows).
    /// </summary>
    public bool RequiresNoAttachedRecord { get; init; }

    /// <summary>Per-Binding model-tier override (<c>sprk_modeltieroverride</c>, global set <c>sprk_aimodeltier</c>). Null = use the Action's default tier.</summary>
    public AiModelTier? ModelTierOverride { get; init; }

    // ── Action-side execution fields (left-join to sprk_analysisaction, §6.1) ──

    /// <summary>Execution kind of the target Action (<c>sprk_analysisaction.sprk_kind</c>). Null/unknown defaults to <see cref="ActionKind.Prompted"/> (per column dictionary).</summary>
    public ActionKind ActionKind { get; init; } = ActionKind.Prompted;

    /// <summary>For <see cref="ActionKind.Coded"/>: the registered <c>ICodedWorkflow</c> class reference (<c>sprk_workflowclass</c>). Null for prompted Actions and legacy rows.</summary>
    public string? WorkflowClass { get; init; }

    /// <summary>Raw typed-argument schema JSON (<c>sprk_analysisaction.sprk_inputschema</c>); the prompted executor owns the typed parse. Null on legacy rows.</summary>
    public string? InputSchemaJson { get; init; }

    /// <summary>The Action's default model tier (<c>sprk_analysisaction.sprk_modeltier</c>). Null = platform default.</summary>
    public AiModelTier? ActionModelTier { get; init; }

    /// <summary>Effective model tier: Binding override wins over Action default (canonical §6.2). Null = platform default (ModelSelector decides per ADR-016).</summary>
    public AiModelTier? EffectiveModelTier => ModelTierOverride ?? ActionModelTier;
}

/// <summary>
/// Output routing disposition (canonical §6.2:
/// <c>informational | work_product | overlay | email | record | notification | compose | surface_launch</c>).
/// Enum values are the raw <c>sprk_disposition</c> option-set values.
/// </summary>
/// <remarks>
/// This enum is the disposition VOCABULARY. Disposition CAPABILITY — the ledger wire value, whether
/// <c>OutputRouter</c> can route it, and (therefore, per ADR-043 §3) whether the dispatch admit-gate
/// admits it — is single-sourced in <see cref="Sprk.Bff.Api.Services.Ai.DispositionRoutability"/>.
/// Do NOT re-list which dispositions are routable/admissible anywhere else; read the registry.
/// </remarks>
public enum BindingDisposition
{
    /// <summary>Rendered to the Assistant pane only (platform default).</summary>
    Informational = 100000000,

    /// <summary>Persisted work product (Workspace tab / record-persisted envelope).</summary>
    WorkProduct = 100000001,

    /// <summary>Applied as widget overlay annotations (bidirectional widget contract).</summary>
    Overlay = 100000002,

    /// <summary>Routed to the Communication (Email) service (DRAFT-only).</summary>
    Email = 100000003,

    /// <summary>Written to a Dataverse record.</summary>
    Record = 100000004,

    /// <summary>Delivered as a notification.</summary>
    Notification = 100000005,

    /// <summary>
    /// Draft-into-editor compose output (ComposeDisposition v1 seam — spaarkeai-compose-r2,
    /// production routing promotion of the task-010 contract). Rides the existing SSE/ledger
    /// surface (ADR-039 — no second dispatch protocol); the stored <see cref="Sprk.Bff.Api.Models.Ai.Chat.SessionOutput"/>
    /// carries the Compose-owned structured-edit payload and the client re-materializes it from
    /// the ledger (render-follows-store, ADR-040). See <see cref="ComposeDisposition"/>.
    /// </summary>
    Compose = 100000006,

    /// <summary>
    /// Pre-seeded surface-launch output (spaarkeai-assistant-enhancements-r1 — the create-flow fix).
    /// A ROUTABLE PASS-THROUGH mirroring <see cref="Compose"/>: the capability drafted the fields for a
    /// launched Class-2 surface (Create-Matter/Event wizard, OOB To-Do form, or an in-app workspace-tab
    /// view), the stored <see cref="Sprk.Bff.Api.Models.Ai.Chat.SessionOutput"/> carries that launch
    /// payload (ledger value <c>surface_launch</c>), and the CLIENT re-materializes it into a pre-seeded
    /// wizard/form launch — no server side-effect (render-follows-store, ADR-040). Rides the existing
    /// SSE/ledger dispatch surface (ADR-039 — no second dispatch protocol). The client owns the
    /// hand-off id + <c>sessionStorage</c> rendezvous + target-surface mapping (task 012); the
    /// dispatch leg only stores the drafted payload. Matches the live Dataverse <c>sprk_disposition</c>
    /// option value exactly.
    /// </summary>
    SurfaceLaunch = 100000007,
}

/// <summary>
/// Confirmation-gate risk posture (canonical §6.2:
/// <c>none | confirm-when-uncertain | always-confirm</c>).
/// Enum values are the raw <c>sprk_risk</c> option-set values.
/// </summary>
public enum BindingRisk
{
    /// <summary>No Binding-level confirmation (tool-level side-effect gating still applies).</summary>
    None = 100000000,

    /// <summary>Insert a confirmation turn when the dispatch decision is uncertain.</summary>
    ConfirmWhenUncertain = 100000001,

    /// <summary>Always insert a confirmation turn before executing.</summary>
    AlwaysConfirm = 100000002,
}

/// <summary>
/// Missing-required-args capture mode (canonical §6.2). Enum values are the
/// raw <c>sprk_capturemode</c> option-set values.
/// </summary>
public enum BindingCaptureMode
{
    /// <summary>Clarifying loop turns elicit missing args (default, OQ-3).</summary>
    LoopElicitation = 100000000,

    /// <summary>Modal escape hatch collects missing args.</summary>
    Modal = 100000001,
}

/// <summary>
/// Execution kind of an Action (canonical §6.1: <c>prompted | coded</c>).
/// Enum values are the raw <c>sprk_analysisaction.sprk_kind</c> option-set values.
/// </summary>
public enum ActionKind
{
    /// <summary>JPS prompt executed via the prompted executor (ActionRunner). Default.</summary>
    Prompted = 100000000,

    /// <summary>Registered <c>ICodedWorkflow</c> C# workflow (composite capability).</summary>
    Coded = 100000001,
}

/// <summary>
/// Model tier vocabulary — the global <c>sprk_aimodeltier</c> option set shared
/// by <c>sprk_analysisaction.sprk_modeltier</c> (Action default) and
/// <c>sprk_playbookconsumer.sprk_modeltieroverride</c> (Binding override).
/// Concrete deployment mapping (tier → model deployment name) stays in
/// code/config per ADR-016 — the catalog stores intent, not deployment names.
/// </summary>
public enum AiModelTier
{
    /// <summary>Cheap/fast models (e.g. gpt-4o-mini) — classification, validation, entity resolution.</summary>
    Fast = 100000000,

    /// <summary>Capable models (e.g. gpt-4o) — high-quality content generation.</summary>
    Standard = 100000001,

    /// <summary>Reasoning models (o-series) — complex multi-step planning.</summary>
    Reasoning = 100000002,
}

/// <summary>
/// One curated next-step chip parsed from <c>sprk_chiptransitions</c> JSON
/// (D4; canonical §7.2 Click path). Shape:
/// <c>[{target_binding_id, chip_label, requires_attachments?, prefill_slots?}]</c>.
/// The two optional members were formalized by the G-P1 UAT round-1 fix
/// (2026-07-05): authored transitions can now declare the empty-attachments
/// Click precondition and pre-filled capability args; both were previously
/// silently dropped by the transition → chip mapping.
/// </summary>
public sealed record ChipTransition
{
    /// <summary>Target Binding id token as authored (raw string; the Click path resolves it). </summary>
    [JsonPropertyName("target_binding_id")]
    public string? TargetBindingId { get; init; }

    /// <summary>Chip label rendered to the user.</summary>
    [JsonPropertyName("chip_label")]
    public string? ChipLabel { get; init; }

    /// <summary>
    /// Optional SHORT verb form used inside server-derived composite chip labels
    /// ("{bulk} all N files?", "{bulk}: {fileName}") when the primary
    /// <see cref="ChipLabel"/> is a full phrase (e.g. chip_label "Summarize this
    /// document" + bulk_chip_label "Summarize"). Added by the G-P2 UAT round-1
    /// finding-1 fix (2026-07-06); when absent the Event path deterministically falls
    /// back to the first whitespace-delimited token of <see cref="ChipLabel"/>.
    /// </summary>
    [JsonPropertyName("bulk_chip_label")]
    public string? BulkChipLabel { get; init; }

    /// <summary>Whether the target capability requires session attachments (client disables the chip at zero attachments).</summary>
    [JsonPropertyName("requires_attachments")]
    public bool? RequiresAttachments { get; init; }

    /// <summary>Optional pre-filled capability args forwarded verbatim as the chip's <c>args</c> (the server-side dispatch boundary owns the typed parse).</summary>
    [JsonPropertyName("prefill_slots")]
    public System.Text.Json.JsonElement? PrefillSlots { get; init; }
}

/// <summary>
/// One Event-path membership parsed from <c>sprk_oneventbindings</c> JSON
/// (canonical §7.1). Shape: <c>[{event, order}]</c>, e.g.
/// <c>{"event":"document_uploaded","order":2}</c>.
/// </summary>
public sealed record OnEventBinding
{
    /// <summary>Platform event token (closed vocabulary, e.g. <c>document_uploaded</c>).</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>Execution order within the event's composite (lower runs first).</summary>
    [JsonPropertyName("order")]
    public int Order { get; init; }
}
