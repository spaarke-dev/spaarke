/**
 * @spaarke/ai-widgets — InsightSummaryCard props (types only)
 *
 * Public prop contract per FR-01 (spec.md) — scaffold from Task 030.
 *
 * Q-U3 — owner deferral: `onFeedback` is intentionally ABSENT from this
 * interface. Feedback affordance is deferred to r2+ pending AIPU2 Cosmos
 * `feedback` container landing on master per ADR-015. Do NOT add an
 * `onFeedback` prop here without revisiting Q-U3 + ADR-015 + the owner.
 *
 * Q-U1 — owner ban: No `@v1`/`@vN` identifier-suffix vernacular anywhere
 * in this file (topic / subject / mode are bare identifiers; envelope
 * versioning is handled by `schemaVersion` strings or `sprk_version`
 * columns, NOT here).
 *
 * @see ADR-021 — Fluent v9 + semantic tokens (consumed by useInsightSummaryCardStyles)
 * @see ADR-012 — Component ships in @spaarke/ai-widgets per DR-001
 * @see projects/ai-spaarke-insights-engine-widgets-r1/decisions/DR-001-component-reuse.md
 * @see projects/ai-spaarke-insights-engine-widgets-r1/spec.md FR-01
 */

import type { ReactNode } from 'react';
import type { Theme } from '@fluentui/react-components';
import type { InsightEnvelope } from './state';
import type { Citation } from './Citation.types';

// Re-export envelope for consumer convenience (host components import the
// envelope shape when authoring their `onFetchInsight` callback).
export type { InsightEnvelope } from './state';

// Re-export the discriminated Citation union + per-variant interfaces +
// type guards from Citation.types — consumers import everything citation-
// related from `@spaarke/ai-widgets` via the package barrel.
export type {
  Citation,
  AssessmentCitation,
  DocumentCitation,
  UnknownCitation,
} from './Citation.types';
export { isAssessmentCitation, isDocumentCitation } from './Citation.types';

// ---------------------------------------------------------------------------
// Citation reference payload — passed to onCitationClick when the card
// surfaces an inline citation. Task 033 promotes this to the discriminated
// `Citation` union (FR-07: `type` discriminator; extensible without new
// components). The legacy minimal shape (`{ id, label?, documentId? }`)
// remains assignment-compatible with `DocumentCitation` for consumers
// authored before Task 033.
// ---------------------------------------------------------------------------

/**
 * Citation reference payload supplied to `onCitationClick`.
 *
 * Per FR-07 (Task 033) this is now an alias of the discriminated
 * {@link Citation} union — `assessment` | `document` | unknown string.
 *
 * Forward compatibility: r2+ may add new known variants (e.g. `passage`)
 * by extending `Citation`; consumers should match the `type` discriminator
 * non-exhaustively (covered by `UnknownCitation`).
 */
export type InsightCitationRef = Citation;

// ---------------------------------------------------------------------------
// Topic registry row contract (FR-05 — Task 032)
// ---------------------------------------------------------------------------

/**
 * Subset of `sprk_aitopicregistry` row fields the component depends on for the
 * FR-05 "no orphan sparkles" mount-time check (Task 032).
 *
 * Only the columns the component itself needs are modelled here; the data
 * adapter is free to read additional columns (e.g. `sprk_displayname` for
 * future header labelling per Task 040+) but those are NOT part of this
 * contract.
 *
 * Field names match the Dataverse logical names per Task 013 schema and the
 * FR-04 acceptance criteria; the adapter is responsible for projecting the
 * Web API record shape into this TS type.
 */
export interface InsightRegistryEntry {
  /** Mirrors `sprk_aitopicregistry.sprk_enabled` — the soft on/off toggle. */
  enabled: boolean;

  /**
   * Mirrors `sprk_aitopicregistry.sprk_cachettlminutes`. Whole-minutes value,
   * default 60 per FR-04. The component forwards this to host-side TTL timers
   * via {@link InsightSummaryCardProps.onRegistryResolved} so consumers can
   * wire `MARK_STALE` dispatch off the registry's TTL.
   */
  cacheTtlMinutes: number;
}

/**
 * Async callback the host supplies for the FR-05 registry mount-check.
 *
 * The adapter receives the `(topic, mode)` tuple the card was constructed
 * with and resolves to:
 *   - `InsightRegistryEntry` when a matching row exists (regardless of
 *     `enabled` value — the component itself decides whether to render);
 *   - `null` when no row matches.
 *
 * Errors should be thrown (or returned via rejected Promise); the component
 * surfaces them as the `error` state per the existing state machine.
 *
 * Per the Q-U2 evidence-precedent (`ConfigurationService.ts`), production
 * hosts implement this with a thin `Xrm.WebApi` adapter; tests pass an
 * in-memory stub. The component MUST NOT call `Xrm.WebApi` directly — the
 * shared library is host-agnostic per ADR-012.
 *
 * Optional in the type so the scaffold + dev playgrounds render without
 * wiring; when absent, the component skips the registry check and renders
 * normally (back-compat for pre-Task-032 consumers).
 */
export type InsightRegistryFetchFn = (
  topic: string,
  mode: string,
) => Promise<InsightRegistryEntry | null>;

// ---------------------------------------------------------------------------
// Public prop contract (FR-01)
// ---------------------------------------------------------------------------

/**
 * Props for {@link InsightSummaryCard}.
 *
 * Per FR-01 + DR-001:
 *   `{ topic, subject, mode?, parameters?, kpiSlot?, onCitationClick? }`
 *
 * NO `onFeedback` (Q-U3 deferral). NO version-suffix syntax (Q-U1 ban).
 */
export interface InsightSummaryCardProps {
  /**
   * Insight topic identifier (registry key from `sprk_aitopicregistry`).
   *
   * Bare identifier — no `@v1`/`@vN` suffix per Q-U1. Versioning lives on
   * the registry row (`sprk_version` column) and on the envelope
   * (`schemaVersion`), NOT on this prop.
   *
   * r1 ships with `'matter-health'` as the only proven topic.
   */
  topic: string;

  /**
   * Insight subject scope — the entity (or entity collection) the topic
   * applies to. r1 uses single-entity matter form: `matter:GUID`.
   *
   * r2+ multi-entity subject schemes (`matter-collection:`, `cohort:`) are
   * framework-shape-compatible but not implemented in r1.
   */
  subject: string;

  /**
   * Topic mode. r1 defaults to `'single'` (per-record card). r2+ may
   * introduce `'multi'` / `'cohort'` modes; the prop is reserved here so
   * downstream hosts can pass it through ahead of r2 landing.
   */
  mode?: string;

  /**
   * Optional topic-specific parameters forwarded to the insight invocation
   * payload (e.g. date ranges, threshold overrides). Kept as `object` to
   * remain forward-compatible with topic-specific schemas; per-topic
   * narrowing happens at the playbook layer, not at this prop boundary.
   */
  parameters?: Record<string, unknown>;

  /**
   * Optional slot for caller-provided KPI rendering (Matter Health KPIs:
   * Guideline Compliance / Budget Compliance / Outcomes Achievement per
   * FR-13). Hosts inject the KPI block; the card composes it into its
   * header / summary region.
   *
   * Slot pattern (per `.claude/patterns/ui/fluent-v9-component-authoring.md`)
   * is preferred over hooks-API composition; this prop is the canonical
   * KPI extension point.
   */
  kpiSlot?: ReactNode;

  /**
   * Optional callback fired when the user clicks an inline citation
   * reference in the card body or modal-expanded view.
   *
   * Wiring is host-responsibility (e.g. open document viewer, scroll to
   * passage). The card does NOT navigate on its own — it surfaces the
   * intent and lets the host decide.
   */
  onCitationClick?: (citation: InsightCitationRef) => void;

  /**
   * Async lazy-load callback. Invoked **once on first Popover open** and
   * again on explicit refresh (e.g. when the card is in the `stale` state
   * and the user clicks "Refresh", or when the user clicks the manual
   * refresh button per FR-20).
   *
   * Shape mirrors `AiSummaryPopover.onFetchSummary` (callback returns a
   * Promise of the envelope payload). The component owns state; the host
   * owns service injection.
   *
   * **FR-20 — `options.force` cache-bypass signal (Task 034)**: When the
   * user clicks the manual refresh button in the Popover footer, the
   * component invokes this callback with `{ force: true }`. The host MUST
   * propagate this to the BFF as a force-cache-bypass parameter (e.g. the
   * `force=true` query string on `/api/insights/ask`). When called from the
   * initial on-open-fetch-once gate, `options` is absent / `force` is
   * falsy — the host should honour normal cache semantics in that case.
   *
   * The argument is OPTIONAL so existing consumers authored before Task 034
   * remain assignment-compatible: a `() => Promise<InsightEnvelope>` is
   * still a valid implementation of this contract (TypeScript ignores
   * extra arguments at the call site).
   *
   * If the BFF responds with an insufficient-evidence outcome, the host
   * should `reject(new InsightDeclineError(message, recommendedAction))` —
   * the component recognises declines via a `kind: 'decline'` property on
   * the thrown error and transitions to the `decline` state. Plain errors
   * transition to the `error` state.
   *
   * Optional in the type so the scaffold + dev playgrounds can render
   * without wiring; production hosts MUST supply it.
   */
  onFetchInsight?: (options?: { force?: boolean }) => Promise<InsightEnvelope>;

  /**
   * Active Fluent v9 theme — required for the portal re-wrap rule per
   * `.claude/patterns/ui/fluent-v9-portal-gotcha.md`.
   *
   * Both the Popover and Dialog render via React Portal and escape the
   * outer FluentProvider's DOM subtree. We re-wrap each portal surface in
   * its own `<FluentProvider theme={theme}>` so dark mode (and any custom
   * tenant theme) propagates correctly.
   *
   * If omitted, the component falls back to `webLightTheme` — acceptable
   * for dev playgrounds but NOT for the Matter form host (which must pass
   * the active MDA theme so dark mode honours the user preference per
   * ADR-021).
   */
  theme?: Theme;

  /**
   * Async callback the component invokes on mount to check the
   * `sprk_aitopicregistry` row for the `(topic, mode)` combination per FR-05
   * ("no orphan sparkles"). Resolves to a matching {@link InsightRegistryEntry}
   * or `null` when no row matches.
   *
   * Per FR-05, the component renders nothing when the callback resolves to
   * `null` OR when the resolved row has `enabled === false`. A rejected
   * Promise transitions the component to the `error` state with the rejection
   * message (no early-return — the error needs to be visible).
   *
   * The component MUST NOT call `Xrm.WebApi` directly per ADR-012 (the shared
   * library is host-agnostic). Hosts wire a thin adapter — see
   * `ConfigurationService.ts` for the precedent.
   *
   * If omitted, the registry check is skipped (back-compat for pre-Task-032
   * consumers and for dev playgrounds where no Dataverse host is available).
   */
  onFetchRegistry?: InsightRegistryFetchFn;

  /**
   * Optional notification fired AFTER a registry row resolves to `enabled`.
   * Receives the resolved {@link InsightRegistryEntry} so the host can wire
   * downstream behaviour (e.g. start a TTL timer that dispatches `MARK_STALE`
   * after `cacheTtlMinutes`).
   *
   * Not fired when the registry row is absent / disabled (the component does
   * not render in those cases).
   */
  onRegistryResolved?: (entry: InsightRegistryEntry) => void;

  /**
   * Label for the trigger button that opens the inline Popover.
   * Defaults to "View Insight". Localisation is host-responsibility.
   */
  triggerLabel?: string;

  /**
   * Optional host-supplied initial envelope to hydrate the card's state on mount.
   *
   * Per spec FR-19: existing stored envelope must render immediately (no spinner)
   * when the host already has the data. Task 042 mount glue reads
   * `sprk_performancesummary` from the Matter record and supplies it here so the
   * card initialises to `loaded` (or `stale` when {@link initialEnvelopeStale} is
   * true) without going through `idle → loading`.
   *
   * Dispatches an internal `HYDRATE` action — see `state.ts` reducer.
   *
   * @added Task 042 contract gap fix (2026-06-11)
   */
  initialEnvelope?: InsightEnvelope;

  /**
   * When true, treats {@link initialEnvelope} as stale (host signals that the
   * stored envelope is past its TTL and a background refresh is in flight).
   * The card hydrates to `stale` instead of `loaded`, showing the "Refresh
   * available" indicator per FR-06.
   *
   * @added Task 042 contract gap fix (2026-06-11)
   */
  initialEnvelopeStale?: boolean;

  /** Optional root class name override (`mergeClasses` applies LAST per Fluent v9 convention). */
  className?: string;
}

// ---------------------------------------------------------------------------
// Decline-error helper — hosts use this to signal an insufficient-evidence
// outcome via the lazy-load callback's rejected Promise.
// ---------------------------------------------------------------------------

/**
 * Error subtype the lazy-load callback throws/rejects with when the BFF
 * returns an insufficient-evidence outcome (per FR-06 `decline` state).
 *
 * Hosts:
 * ```ts
 * onFetchInsight={async () => {
 *   const resp = await bff.invokeInsight(...);
 *   if (resp.outcome === 'declined') {
 *     throw new InsightDeclineError(resp.message, resp.recommendedAction);
 *   }
 *   return resp.envelope;
 * }}
 * ```
 *
 * Component:
 *   catches Promise rejection → checks `err.kind === 'decline'` →
 *   dispatches `FETCH_DECLINE` (NOT `FETCH_ERROR`).
 */
export class InsightDeclineError extends Error {
  readonly kind = 'decline' as const;
  readonly recommendedAction?: string;
  constructor(message: string, recommendedAction?: string) {
    super(message);
    this.name = 'InsightDeclineError';
    if (recommendedAction !== undefined) {
      this.recommendedAction = recommendedAction;
    }
    // Restore prototype chain (TypeScript built-in class extension caveat).
    Object.setPrototypeOf(this, InsightDeclineError.prototype);
  }
}
