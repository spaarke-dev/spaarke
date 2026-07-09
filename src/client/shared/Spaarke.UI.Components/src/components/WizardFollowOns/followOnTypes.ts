/**
 * followOnTypes.ts
 * Config contract for the shared, config-driven WizardFollowOns module.
 *
 * A wizard's "Next Steps" card set is expressed as a `FollowOnCardConfig[]`
 * rather than a hand-forked component. Selecting a card injects a dynamic
 * wizard step via `WizardShell.addDynamicStep`; deselecting removes it.
 *
 * This module is the single generalized home for the follow-on card grid +
 * reusable follow-on steps that were previously duplicated across four wizard
 * families (CreateRecordWizard, CreateWorkAssignmentWizard, CreateMatterWizard,
 * SummarizeFilesWizard). See design.md §5.9 (visual-host-create-button-r1).
 *
 * Design intent — fit all four divergent card sets WITHOUT a fork:
 *   - `id` / `label` / `icon` / `description` cover the pure-presentation
 *     differences between card sets.
 *   - `renderStep` lets each wizard supply an arbitrary step body (form,
 *     picker, launcher). The wizard owns the step's form state via closure/refs
 *     exactly as the pre-consolidation orchestrators did.
 *   - `canAdvance` / `isSkippable` express the per-step advancement gate that
 *     differs between a required-field step (Send Email) and an always-optional
 *     one (Assign Work, Create Event).
 *   - `renderSelectedExtra` supports the SummarizeFiles case where a card
 *     carries inline grid UI (the "include only short summary" toggle) — so
 *     that summary-oriented card can be expressed as config, not a grid fork.
 *
 * @see ADR-012 — Shared Component Library (context-agnostic, prop-driven)
 * @see ADR-021 — Fluent UI v9 design system (semantic tokens, dark-mode safe)
 */
import type * as React from 'react';
import type { IWizardShellHandle } from '../Wizard/wizardShellTypes';

// ---------------------------------------------------------------------------
// Canonical follow-on identifiers
// ---------------------------------------------------------------------------

/**
 * The canonical set of follow-on card identifiers offered across Spaarke
 * create wizards. Wizard-specific card sets MAY use arbitrary string ids
 * beyond this union (e.g. SummarizeFiles' `create-project` /
 * `work-on-analysis`) — `FollowOnCardConfig.id` is a plain `string`, so the
 * grid is not limited to these. This union exists to give the shared, reusable
 * cards a stable vocabulary + canonical ordering.
 */
export type FollowOnId = 'assign-work' | 'add-todo' | 'create-event' | 'draft-summary' | 'send-email';

/**
 * Derive the sidebar step id for a card. Kept as a single function so the grid,
 * the maps below, and any consumer compute step ids identically.
 */
export function followOnStepId(cardId: string): string {
  return `followon-${cardId}`;
}

/** Map a canonical follow-on id → its sidebar (dynamic) step id. */
export const FOLLOW_ON_ID_MAP: Record<FollowOnId, string> = {
  'assign-work': followOnStepId('assign-work'),
  'add-todo': followOnStepId('add-todo'),
  'create-event': followOnStepId('create-event'),
  'draft-summary': followOnStepId('draft-summary'),
  'send-email': followOnStepId('send-email'),
};

/** Map a canonical follow-on id → its default sidebar step label. */
export const FOLLOW_ON_LABEL_MAP: Record<FollowOnId, string> = {
  'assign-work': 'Assign Work',
  'add-todo': 'Add To Do',
  'create-event': 'Create Event',
  'draft-summary': 'Draft Summary',
  'send-email': 'Send Email',
};

/**
 * Canonical ordering of the reusable follow-on steps in the wizard sidebar,
 * expressed as STEP ids (matching the pre-consolidation
 * `FOLLOW_ON_CANONICAL_ORDER` semantics that `WizardShell`'s reducer consumes).
 *
 * `FollowOnGrid` derives its own canonical order from the `cards` array order
 * by default, so a wizard controls ordering simply by ordering its card array.
 * This constant is a convenience default for consumers that use the canonical
 * card set and want the standard order.
 */
export const FOLLOW_ON_CANONICAL_ORDER: readonly string[] = [
  FOLLOW_ON_ID_MAP['assign-work'],
  FOLLOW_ON_ID_MAP['add-todo'],
  FOLLOW_ON_ID_MAP['create-event'],
  FOLLOW_ON_ID_MAP['draft-summary'],
  FOLLOW_ON_ID_MAP['send-email'],
];

// ---------------------------------------------------------------------------
// Card configuration
// ---------------------------------------------------------------------------

/**
 * Configuration for a single follow-on action card + the dynamic wizard step
 * it injects when selected.
 */
export interface FollowOnCardConfig {
  /**
   * Stable identifier for this card. Drives the derived dynamic step id
   * (`followon-{id}`), the React key, and the selection array. Any string is
   * valid — the grid is not limited to {@link FollowOnId}.
   */
  id: string;
  /** Card title shown on the checkbox card. */
  label: string;
  /** Fluent v9 icon element rendered in the card's top-left. */
  icon: React.ReactNode;
  /** One- or two-line description shown under the card title. */
  description: string;
  /**
   * Renders the dynamic wizard step's content when this card is selected.
   * Receives the {@link IWizardShellHandle} so the step can drive further
   * dynamic steps or call `requestUpdate()` when its own `canAdvance` inputs
   * change. The consuming wizard owns the step's form state (typically via
   * refs read inside this callback, matching the pre-consolidation pattern).
   */
  renderStep: (handle: IWizardShellHandle) => React.ReactNode;
  /**
   * Optional sidebar step label. Defaults to {@link label} when omitted.
   * Use when the card label and the sidebar label should differ (e.g. a long
   * card label but a short stepper label).
   */
  stepLabel?: string;
  /**
   * Optional predicate gating advancement past the injected step. Defaults to
   * always-advance — follow-on steps are optional by nature. Supply this for
   * steps with required fields (e.g. Send Email requires To/Subject/Body).
   */
  canAdvance?: () => boolean;
  /**
   * When `true`, the injected step shows a Skip button in the footer. Defaults
   * to `false`.
   */
  isSkippable?: boolean;
  /**
   * Optional inline content rendered inside the grid, directly below the card
   * row, ONLY while this card is selected. Domain-free: the wizard owns the
   * state whatever this renders (e.g. SummarizeFiles' "include only short
   * summary" toggle). Present so a summary-oriented card set can be expressed
   * as config rather than forking the grid.
   */
  renderSelectedExtra?: () => React.ReactNode;
}

// ---------------------------------------------------------------------------
// Recipient item (used by DraftSummaryFollowOnStep + RecipientField)
// ---------------------------------------------------------------------------

/**
 * A recipient entry in the DraftSummary distribute-to / CC fields.
 *
 * Defined here so the WizardFollowOns module is self-contained and does not
 * depend on `CreateRecordWizard/types` (whose follow-on copies are removed in
 * the per-wizard migrations, tasks 021–024).
 */
export interface IRecipientItem {
  /** Unique key for deduplication (contact GUID or freeform email). */
  key: string;
  /** Display name shown in the chip. */
  displayName: string;
  /** Email address (extracted from contact or freeform entry). */
  email: string;
  /** Whether this was manually entered (true) or from contact lookup (false). */
  isManual?: boolean;
}
