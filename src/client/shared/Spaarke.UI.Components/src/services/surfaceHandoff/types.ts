/**
 * surfaceHandoff/types.ts — the Assistant → launched-surface entry-payload
 * envelope contract (Class 2b hand-off).
 *
 * spaarkeai-assistant-enhancements-r1 task 012 / spec FR-A1 / FR-A3.
 * Blueprint: `projects/spaarkeai-assistant-enhancements-r1/notes/
 * 012-assistant-surface-handoff-design.md` §3 (the envelope shape) +
 * `surface-launch-mechanism.md` §3 (the consumertype → surface routing table).
 *
 * The Assistant drafts create-flow field values in chat (disposition
 * `surface_launch`, task 002); the CLIENT re-materializes that stored draft
 * into a pre-seeded create surface (custom wizard OR OOB form modal). This
 * envelope is the transport contract: it rides `sessionStorage` keyed by a
 * minted `handoffId`, carrying files **by reference** (never inline binary),
 * the drafted free-text values, and — filled later by the constrained-field
 * resolver (task 010 → populated by task 013) — the resolved lookup GUIDs.
 *
 * INVARIANTS (binding):
 *  - Files ride as `fileIds` (SPE references) — the surface fetches on load;
 *    the binary NEVER rides the URL or storage (design §3, project constraint).
 *  - `resolvedLookups` is the resolver's slot: DEFINED here, POPULATED by
 *    task 013. The LLM never authors a closed-set value (ADR-039).
 *  - `draftValues` are only the free-text fields the LLM is good at
 *    (name / description + provenance) — never a closed-set final value.
 *
 * @see ADR-039 — grounded execution & closed catalogs
 * @see ADR-040 — ledger-before-render (the durable copy stays the SessionOutput;
 *                sessionStorage is the transient client rendezvous, discarded on read)
 */

/**
 * The kind of surface a capability's draft launches into. Discriminates the
 * launch adapter (design §3 / surface-launch-mechanism §3).
 *  - `wizard`        — a custom Create*Wizard Code Page (web resource) via
 *                      `Xrm.Navigation.navigateTo({pageType:'webresource'})`.
 *  - `oob-form`      — an OOB entity create form in a modal via
 *                      `navigateTo({pageType:'entityrecord'})` (thinner pre-seed).
 *  - `workspace-tab` — (future, Class 2a) an in-app widget tab; transport is the
 *                      in-app event bus, NOT this sessionStorage rendezvous.
 *  - `layout`        — (future, Class 2a) a workspace layout apply.
 */
export type SurfaceKind = 'wizard' | 'oob-form' | 'workspace-tab' | 'layout';

/**
 * One resolved (or candidate) closed-set lookup value produced by the
 * constrained-field resolver (task 010). The wizard pre-selects dropdowns from
 * this: `high` → pre-select; `low`/`none` → picker defaulted to top candidate.
 *
 * DEFINED now (task 012); POPULATED by task 013. Until then the map is `{}`.
 */
export interface ResolvedLookup {
  /** The resolved Dataverse record id (bare GUID). Absent when unresolved. */
  readonly recordId?: string;
  /** Resolver confidence — drives pre-select vs picker-default behavior. */
  readonly confidence: 'high' | 'low' | 'none';
  /** Alternative candidates when confidence is not `high`. */
  readonly candidates?: ReadonlyArray<{ readonly recordId: string; readonly label?: string }>;
}

/** Origin metadata — which chat session / capability / turn drafted the values. */
export interface SurfaceHandoffSource {
  /** Chat session id that produced the draft. */
  readonly sessionId?: string;
  /** The dispatched capability's Binding id (`sprk_playbookconsumer`). */
  readonly bindingId?: string;
  /** The chat turn the draft was produced on. */
  readonly turn?: number;
  /**
   * The capability's `sprk_consumertype` — the registry key the client used to
   * resolve the target surface. Echoed so the launched surface can self-identify.
   */
  readonly consumerType?: string;
}

/** The target surface this draft launches into. */
export interface SurfaceHandoffTarget {
  /**
   * The concrete surface identifier — a web-resource name (`kind:'wizard'`,
   * e.g. `sprk_creatematterwizard`) or an entity logical name
   * (`kind:'oob-form'`, e.g. `sprk_todo`). Client-side only per BFF hygiene §10.
   */
  readonly surface: string;
  /** Launch-adapter discriminator. */
  readonly kind: SurfaceKind;
}

/** Provenance — what the drafted values were grounded on. */
export interface SurfaceHandoffProvenance {
  /** Source document file names the draft was grounded on (display only). */
  readonly sourceFiles?: ReadonlyArray<string>;
  /** Session-ledger keys the draft came from (e.g. `<binding>@t3`). */
  readonly ledgerKeys?: ReadonlyArray<string>;
}

/**
 * THE entry-payload envelope (design §3). Serialized to
 * `sessionStorage["sprk.handoff.<handoffId>"]` at launch; read by the target
 * surface on boot; discarded on read (transient rendezvous, not a store).
 */
export interface SurfaceHandoffEnvelope {
  /** The minted correlation id: `"h-<guid>"`. The whole correlation. */
  readonly handoffId: string;
  /** Origin metadata. */
  readonly source: SurfaceHandoffSource;
  /** Target surface + launch-adapter kind. */
  readonly target: SurfaceHandoffTarget;
  /** Session-held SPE file references — the surface fetches on load (NEVER binary). */
  readonly fileIds: ReadonlyArray<string>;
  /** The Assistant-drafted free-text field values (name/description/provenance). */
  readonly draftValues: Record<string, unknown>;
  /**
   * Constrained-field resolver output (task 010). DEFINED now, POPULATED by
   * task 013 — `{}` until then. Keyed by the target field's logical name.
   */
  readonly resolvedLookups: Record<string, ResolvedLookup>;
  /** Grounding provenance. */
  readonly provenance: SurfaceHandoffProvenance;
  /** ISO-8601 mint timestamp. */
  readonly createdAt: string;
  /** Time-to-live (seconds) — a stale envelope is ignored on read (design §3). */
  readonly ttlSeconds: number;
}

/**
 * The OUTCOME envelope written back by the surface (design §1 step 9 / §3).
 * Written to `sessionStorage["sprk.handoff.<handoffId>.result"]` by the wizard
 * on commit/cancel; read by the Assistant when the `navigateTo` promise
 * resolves (modal close). Gates the honest "✅ Created …" claim (P5 / task 020).
 */
export interface SurfaceHandoffResult {
  /** True only when the surface committed a real record. */
  readonly committed: boolean;
  /** The created record id (bare GUID) when `committed`. */
  readonly recordId?: string;
  /** True when the user cancelled/dismissed without committing. */
  readonly cancelled?: boolean;
  /** A stable error signal when the surface failed to commit. */
  readonly error?: string;
}
