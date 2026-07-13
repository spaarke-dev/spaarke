/**
 * Catalog types — the two closed catalogs a BA authors (FR-P4-04).
 *
 * Mirrors the server contract in
 * `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/Binding.cs`
 * (ADR-039 single-routing-surface rule). Enum numeric values ARE the raw
 * Dataverse option-set values — do not renumber.
 *
 *   - Action row  → `sprk_analysisaction`   (execution unit, canonical §6.1)
 *   - Binding row → `sprk_playbookconsumer` (invocation unit, canonical §6.2)
 */

// ─────────────────────────────────────────────────────────────────────────────
// Option sets (values = Dataverse option-set values, per Binding.cs)
// ─────────────────────────────────────────────────────────────────────────────

export enum ActionKind {
  Prompted = 100000000,
  Coded = 100000001,
}

export enum AiModelTier {
  Fast = 100000000,
  Standard = 100000001,
  Reasoning = 100000002,
}

export enum BindingDisposition {
  Informational = 100000000,
  WorkProduct = 100000001,
  Overlay = 100000002,
  Email = 100000003,
  Record = 100000004,
  Notification = 100000005,
  Compose = 100000006,
}

export enum BindingRisk {
  None = 100000000,
  ConfirmWhenUncertain = 100000001,
  AlwaysConfirm = 100000002,
}

export enum BindingCaptureMode {
  LoopElicitation = 100000000,
  Modal = 100000001,
}

// ─────────────────────────────────────────────────────────────────────────────
// Closed vocabularies
// ─────────────────────────────────────────────────────────────────────────────

/** §4.1 placement-surface tokens (`sprk_surfaces`, comma-separated). Empty = ALL surfaces. */
export const SURFACE_TOKENS = [
  'assistant',
  'record-form',
  'wizard',
  'office',
  'external-spa',
  'scheduler',
  'inbound-email',
] as const;
export type SurfaceToken = (typeof SURFACE_TOKENS)[number];

/**
 * Known platform event tokens for `sprk_oneventbindings` (closed vocabulary,
 * canonical §7.1). Source of truth: `IEventRulesService` constants server-side.
 * Unknown tokens never fire — the editor warns but does not block (the
 * vocabulary can grow server-side ahead of this list).
 */
export const KNOWN_EVENT_TOKENS = ['document_uploaded'] as const;

// ─────────────────────────────────────────────────────────────────────────────
// JSON column shapes (pinned by the column dictionary / Binding.cs)
// ─────────────────────────────────────────────────────────────────────────────

/** One curated next-step chip (`sprk_chiptransitions` entry, D4 / Click path). */
export interface ChipTransition {
  target_binding_id?: string;
  chip_label?: string;
  /** Optional SHORT verb form for server-derived composite chip labels (G-P2 finding-1). */
  bulk_chip_label?: string;
  /** Client disables the chip at zero session attachments (G-P1 round-1). */
  requires_attachments?: boolean;
  /** Pre-filled capability args forwarded verbatim; server owns the typed parse. */
  prefill_slots?: Record<string, unknown>;
}

/** One Event-path membership (`sprk_oneventbindings` entry, canonical §7.1). */
export interface OnEventBinding {
  event: string;
  order: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Editor row models
// ─────────────────────────────────────────────────────────────────────────────

/** BA-authorable fields of one `sprk_analysisaction` row (execution unit). */
export interface ActionRow {
  /** `sprk_analysisactionid` — undefined until first save. */
  id?: string;
  /** `sprk_name` */
  name: string;
  /** `sprk_actioncode` — stable versioned code, e.g. `SUM-CHAT@v1`. */
  actionCode: string;
  /** `sprk_description` */
  description: string;
  /** `sprk_kind` */
  kind: ActionKind;
  /** `sprk_workflowclass` — registered ICodedWorkflow class ref; Coded kind only. */
  workflowClass: string;
  /** `sprk_systemprompt` — JPS JSON (starts with `{`) or flat prompt text. */
  systemPrompt: string;
  /** `sprk_inputschema` — typed-argument JSON Schema (OpenAI function-parameters subset). */
  inputSchema: string;
  /** `sprk_outputschemajson` — Structured-Outputs JSON Schema. */
  outputSchema: string;
  /** `sprk_modeltier` — Action default tier; null = platform default (ADR-016). */
  modelTier: AiModelTier | null;
}

/** BA-authorable fields of one `sprk_playbookconsumer` row (invocation unit). */
export interface BindingRow {
  /** `sprk_playbookconsumerid` — undefined until first save. */
  id?: string;
  /** `sprk_name` */
  name: string;
  /** `sprk_consumertype` — stable consumer-type code, e.g. `chat-summarize`. */
  consumerType: string;
  /** `sprk_consumercode` — sub-discriminator; empty = `default`. */
  consumerCode: string;
  /** `sprk_environment` — empty or `*` = all environments. */
  environment: string;
  /** `sprk_priority` — lower wins; 500 default. */
  priority: number;
  /** `sprk_enabled` */
  enabled: boolean;
  /** `sprk_action` lookup target (`sprk_analysisaction` id); null = not yet bound. */
  actionId: string | null;
  /** `sprk_ucid` — use-case id, e.g. `UC-A-1`. */
  ucid: string;
  /** `sprk_tooldescription` — the intent surface the agent loop matches on. */
  toolDescription: string;
  /** `sprk_disposition` */
  disposition: BindingDisposition;
  /** `sprk_risk` */
  risk: BindingRisk;
  /** `sprk_capturemode` */
  captureMode: BindingCaptureMode;
  /** `sprk_chiptransitions` — raw JSON (authored via ChipTransitionsEditor). */
  chipTransitionsJson: string;
  /** `sprk_oneventbindings` — raw JSON (authored via OnEventBindingsEditor). */
  onEventBindingsJson: string;
  /** `sprk_matchconditions` — flat key → string | string[] predicate JSON. */
  matchConditionsJson: string;
  /** `sprk_surfaces` tokens; empty = offered on ALL surfaces. */
  surfaces: string[];
  /** `sprk_modeltieroverride` — Binding override; null = use Action default. */
  modelTierOverride: AiModelTier | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Factories
// ─────────────────────────────────────────────────────────────────────────────

export function newActionRow(): ActionRow {
  return {
    name: '',
    actionCode: '',
    description: '',
    kind: ActionKind.Prompted,
    workflowClass: '',
    systemPrompt: '',
    inputSchema: '',
    outputSchema: '',
    modelTier: null,
  };
}

export function newBindingRow(): BindingRow {
  return {
    name: '',
    consumerType: '',
    consumerCode: 'default',
    environment: '*',
    priority: 500,
    enabled: true,
    actionId: null,
    ucid: '',
    toolDescription: '',
    disposition: BindingDisposition.Informational,
    risk: BindingRisk.None,
    captureMode: BindingCaptureMode.LoopElicitation,
    chipTransitionsJson: '',
    onEventBindingsJson: '',
    matchConditionsJson: '',
    surfaces: [],
    modelTierOverride: null,
  };
}
