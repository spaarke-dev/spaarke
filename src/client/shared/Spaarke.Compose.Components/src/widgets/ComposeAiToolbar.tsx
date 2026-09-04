/**
 * ComposeAiToolbar.tsx — inline AI toolbar (FR-14 / task 030).
 *
 * Project: spaarkeai-compose-r2, task 030 (Phase 3 Inline Editing UX).
 * Mounted INSIDE ComposeEditor's existing `BubbleMenu` render prop (see the
 * "AI TOOLBAR MOUNT" region in ComposeEditor.tsx) — same floating surface as
 * the formatting toolbar, one BubbleMenu / one tippy position lifecycle.
 * Appears on any non-collapsed selection; dismiss-on-clear and
 * reposition-to-stay-in-viewport are inherited for free from the parent
 * BubbleMenu's tippy instance (`tippyOptions={{ placement: 'top' }}` —
 * tippy.js's default popper modifiers include `flip`, so no extra flip
 * config is needed here). This component ALSO independently guards on
 * `editor.state.selection` (collapsed => renders null) so it is correct even
 * if ever mounted outside a BubbleMenu (e.g. in isolation for a test).
 *
 * ---------------------------------------------------------------------------
 * DISPATCH MECHANISM (binding — Spike 0 correction, 2026-07-08; design.md
 * §2.1/§7.2): a button click calls the shipped Click-path session-dispatch
 * seam DIRECTLY via a bound `dispatchConsumer(bindingId, { slots })` — the
 * same helper `useConsumerChips.tsx` (SpaarkeAi) uses for chip clicks. There
 * is NO `compose_action_request` PaneEventBus event — that discriminant does
 * not exist in the shipped `compose-contracts.ts` and must NOT be invented
 * (see `projects/spaarkeai-compose-r2/notes/spikes/spike-0-dispatch-path.md`
 * §4). `dispatchConsumer` itself POSTs to the existing, unconditional
 * `POST /api/ai/chat/sessions/{sessionId}/dispatch` endpoint and bridges its
 * SSE stream onto the `workspace` PaneEventBus channel internally — this
 * component's ONLY PaneEventBus duty is forwarding that bridge (via the
 * `dispatch` prop, reused from ComposeEditor's existing
 * `useDispatchPaneEvent()` instance — no second dispatcher is created).
 * The parallel `conversation.compose_selection_offer` (Flow 2) event keeps
 * firing from ComposeEditor's `useSelectionEventDispatch` for pane
 * choreography (Assistant/Context awareness) — it is NOT the dispatch
 * trigger and this component does not touch it.
 *
 * ---------------------------------------------------------------------------
 * PHASE-4 STUB BOUNDARY (binding — read before wiring a real Binding here):
 * `DEFAULT_ACTIONS` below ships with `bindingId: ''` for all FIVE actions
 * (three primary clause-selection actions + two overflow whole-document
 * actions). The real `sprk_playbookconsumer` Binding GUIDs are authored
 * mirror-first (`infra/dataverse/sprk_playbookconsumer-rows.json`) and MINTED
 * PER-ENVIRONMENT AT CATALOG SEED TIME (task 047 — owner/live-env). They are
 * NOT knowable at build/seed time (portable actionCode lookups resolve to a
 * fresh row GUID per environment), and the `/dispatch` route is GUID-only
 * (no client-side consumer→GUID resolver), so button ENABLEMENT genuinely
 * depends on that deploy (E2E-pending on 047 — see spaarkeai-compose-r2
 * task 101 escalation).
 * An empty `bindingId` renders the button DISABLED (honest stub — clicking a
 * disabled button cannot silently no-op or 400) rather than wired-but-broken.
 * Activation is ONE call per action to
 * `registerComposeAiToolbarAction({ ...same id..., bindingId: '<seeded GUID>' })`
 * from the real mount path once the host can resolve the deployed GUIDs — no
 * edit to this file required (the registration-extensibility guarantee,
 * design.md §2.0).
 *
 * ---------------------------------------------------------------------------
 * EXTENSIBLE ACTION REGISTRY (design.md §2.0 extensibility guarantee): the
 * three primary buttons + the "More actions…" overflow are both driven by
 * `getComposeAiToolbarActions()`, which merges `DEFAULT_ACTIONS` with
 * whatever `registerComposeAiToolbarAction()` has registered (by `id`;
 * registering an existing id REPLACES it — e.g. Phase 4 swapping in a real
 * bindingId; registering a new id APPENDS it, landing in the overflow menu
 * by default unless `placement: 'primary'` is set). This is how a follow-on
 * project adds a clause-insertion action (design.md §2.0 "canonical deferred
 * extension") without touching this component's source.
 *
 * ---------------------------------------------------------------------------
 * TOOLTIP SOURCING (FR-23 / task 024, ADR-021's "tool descriptions ARE the
 * prompt" pattern): the tooltip copy below is a concise excerpt of the SAME
 * authored source that primes the LLM — the enriched `compose-selection`
 * scope description at
 * `projects/spaarkeai-compose-r1/notes/jps-scopes/compose-selection.scope.json`
 * (enriched 2026-07-08 by task 024). That scope-level description is a long
 * paragraph (GOTCHA / RECOVERY PATH / EXAMPLE guidance for the LLM); these
 * are NOT an independently-authored second copy, they are hover-length
 * excerpts of it. When Phase 4 authors the four `compose-*` Action rows
 * (040-044), each Action's own `description` becomes the more precise
 * per-action source — swap the tooltip text at that time via
 * `registerComposeAiToolbarAction` (one line, no rearchitecting).
 *
 * @see ADR-021 — Fluent v9 semantic tokens, dark-mode-correct
 * @see ADR-030 — PaneEventBus (choreography only; not the dispatch mechanism)
 * @see ADR-039 — grounded execution; one dispatch protocol, three entry paths
 * @see ADR-040 — ledger-before-render (dispatchConsumer renders what the
 *                stream delivers; the server already wrote the ledger entry)
 * @see projects/spaarkeai-compose-r2/notes/spikes/spike-0-dispatch-path.md
 * @see src/solutions/SpaarkeAi/src/components/conversation/useConsumerChips.tsx
 *      (the pattern this component mirrors for the dispatch call shape)
 * @see ./ComposeEditor.tsx ("AI TOOLBAR MOUNT" region — host wiring)
 * @see ./ComposeFormatToolbar.tsx (the PERSISTENT top toolbar; unrelated surface)
 *
 * ---------------------------------------------------------------------------
 * TASK 111 (UAT-R2, 2026-07-10) — layout fix + two mount paths: the
 * selection-triggered BubbleMenu popup (unchanged mechanism) is now AI-ACTIONS
 * ONLY (the sibling formatting Toolbar that used to live alongside this
 * component in ComposeEditor's BubbleMenu was removed — see ComposeEditor.tsx
 * "AI TOOLBAR MOUNT" region). This component owns its OWN single `<Toolbar>`
 * (previously a Fragment of `<ToolbarDivider/>` + a second `<Toolbar>`, both
 * mounted as siblings inside the host's flex row — the divider had no Toolbar
 * context (collapsed/misaligned) and two Toolbars each imposed their own
 * padding). (GAP FIX, UAT 2026-07-15: the in-Toolbar dividers were subsequently
 * REMOVED — Fluent's ToolbarDivider `padding: 0 12px` produced a large visual gap
 * between the primary AI icons and the trailing Email / overflow icons; every
 * action now sits in one tightly-spaced row at the single `columnGap` token. See
 * the "GAP FIX" note in the render body.) A new
 * `forceVisible` prop lets ComposeEditor's right-click (context-menu) trigger
 * render this toolbar even on a COLLAPSED selection (point-insertion) — see
 * `forceVisible` doc below; `handleActionClick`'s existing collapsed-selection
 * dispatch guard is UNCHANGED (a right-click with no selection still no-ops
 * on click rather than dispatching).
 */

import * as React from 'react';
import { type Editor } from '@tiptap/react';
import {
  Toolbar,
  ToolbarButton,
  Button,
  Tooltip,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  Info24Regular,
  ArrowSwapRegular,
  DocumentEdit24Regular,
  TextGrammarArrowLeft24Regular,
  Wand24Regular,
  MoreVertical20Regular,
  Dismiss16Regular,
} from '@fluentui/react-icons';
import { useAuth } from '@spaarke/auth';
import {
  createConsumerDispatcher,
  type DispatchConsumer,
  type DispatchConsumerArgs,
  type DispatchConsumerResult,
} from '@spaarke/ui-components';
import type { DispatchPaneEvent, WorkspacePaneEvent } from '@spaarke/ai-widgets/events';
import type { ComposeDocumentRef } from '../types/compose-contracts';
import type { TipTapNode } from '../utils/docxBridge';
import type { AiGenerateBookmarkController } from './hooks/useAiGenerateBookmark';
import type { AiApplyReviewReason, AiApplyValidationController } from './hooks/useAiApplyValidation';

/**
 * spaarkeai-compose-r2 — clean-body extraction for the Email stub (FIX #10b).
 *
 * A plain `doc.textBetween(...)` concatenates BOTH halves of a pending redline — the struck original
 * AND the proposed insertion — yielding a garbled email body ("old textnew text"). This walks the
 * TipTap JSON and renders pending redlines as their ACCEPTED text: struck (deletion-marked) originals
 * are DROPPED, AI insertions are kept as ordinary text, so the emailed draft matches what the author
 * is composing. It is the ACCEPT-view, NOT the {@link buildRejectBaselineJson} reject baseline — an
 * email of a chat-drafted document must carry the drafted content, which the reject baseline (which
 * drops all insertions) would strip to empty. Textblocks are joined with newlines to keep paragraphs.
 */
export function extractCleanDraftText(doc: TipTapNode): string {
  const TEXTBLOCK_TYPES = new Set(['paragraph', 'heading', 'blockquote', 'codeBlock']);
  const blocks: string[] = [];
  const inlineText = (n: TipTapNode): string => {
    if (n.type === 'text') {
      if (n.marks?.some(m => m.type === 'deletion')) return ''; // struck original — drop
      return n.text ?? '';
    }
    return (n.content ?? []).map(inlineText).join('');
  };
  const walk = (n: TipTapNode): void => {
    if (n.type && TEXTBLOCK_TYPES.has(n.type)) {
      blocks.push(inlineText(n)); // textblock: collect its inline run, do not double-descend
      return;
    }
    (n.content ?? []).forEach(walk);
  };
  walk(doc);
  return blocks.join('\n').trim();
}

/**
 * FR-18 host-provided serialization seam (spaarkeai-compose-r2 tasks 030 + 032).
 *
 * When the host threads this callback (SpaarkeAi wires it to ConversationPane's
 * `useSerialActionQueue`-backed `dispatchComposeAction`), the toolbar routes
 * every action dispatch THROUGH the host's FIFO queue so rapid, distinct actions
 * (e.g. Compare then Draft) run strictly one-at-a-time — no interleaved SSE
 * streams, no out-of-order ledger writes. When ABSENT (standalone Path-A mount
 * with no separate Assistant queue), the toolbar falls back to its own bound
 * `dispatchConsumer` (unserialized — acceptable when there is no concurrent
 * queue to coordinate with).
 *
 * The request shape mirrors `ComposeActionRequest` in
 * `src/solutions/SpaarkeAi/.../useSerialActionQueue.ts` structurally; it is
 * re-declared here (not imported) because the shared lib MUST NOT depend on a
 * solution-local module (unidirectional dependency).
 */
export type ComposeActionEnqueue = (request: {
  /** Caller-assigned correlation id (opaque; surfaced by the queue for UI affordance). */
  id: string;
  /** Binding row GUID — the ONLY routing datum (ADR-039); forwarded verbatim. */
  bindingId: string;
  /** Forwarded verbatim to `dispatchConsumer` as its second argument. */
  args?: DispatchConsumerArgs;
  /**
   * DEF-09: the editor's DOCUMENT session id, set ONLY for editor-materializing
   * compose EDIT actions (`materializesInEditor`). Its presence tells the host
   * (ConversationPane) to (a) route this dispatch to the DOCUMENT session — so its
   * `compose` SessionOutput lands where `ComposeWorkspace` reads `compose-outputs`
   * to materialize the inline redline — and (b) emit a CONFIRMATION-only Assistant
   * line instead of the full proposed-text prose. Absent for informational actions
   * (explain/compare/defined-terms/summarize-changes), which keep chat-session
   * dispatch + Assistant-rendered prose.
   */
  documentSessionId?: string;
  /**
   * DEF-11: present ⇒ this edit action revises the WHOLE open document (`compose-revise-document`),
   * not just a selection. The host uses it ONLY to pick the Assistant confirmation copy variant;
   * routing/materialize/Accept-all are unaffected (mirrors `ComposeActionRequest.revisionScope`).
   */
  revisionScope?: 'selection' | 'whole-document';
}) => Promise<DispatchConsumerResult>;

// ---------------------------------------------------------------------------
// Selection-text sizing — mirrors the compose-selection scope's authored cap
// ---------------------------------------------------------------------------

/**
 * Cap for the `selectionText` slot forwarded to `dispatchConsumer`. Sourced
 * from the compose-selection scope's `inputs.fields[selectionText].maxLength`
 * (16000) — NOT the smaller 2000-char PaneEventBus Flow 1/2 cap in
 * ComposeEditor.tsx, which exists for a different concern (telemetry-safe
 * event-payload sizing). The action dispatch payload is allowed the full
 * scope-authored budget.
 */
const TOOLBAR_SELECTION_TEXT_CAP = 16000;

// ---------------------------------------------------------------------------
// Extensible action registry
// ---------------------------------------------------------------------------

/**
 * The UI surfaces on which a tool can appear (Contextual AI Tool Library —
 * see `projects/ai-advanced-capabilities-nda-r1/notes/contextual-ai-tool-library-design.md`).
 * A tool's `surfaces` is the FIRST of the two library dimensions (the second is
 * `workTypes`). `selection` = the BubbleMenu; `review-note` = a Review Note's ⋮ menu;
 * `whole-document` / `assistant-chip` are declared for future surfaces.
 */
export type ToolSurface = 'selection' | 'review-note' | 'whole-document' | 'assistant-chip';

/**
 * Optional runtime context a tool's `appliesTo` predicate may inspect. Kept minimal
 * for now (no predicate ships yet); a future tool can gate on the selection text
 * or the active document.
 */
export interface ToolContext {
  readonly selectionText?: string;
  readonly activeWorkType?: string;
}

/**
 * One inline AI toolbar action. `bindingId: ''` is the Phase-4 stub sentinel
 * (see file header "PHASE-4 STUB BOUNDARY") — a stubbed action renders
 * disabled rather than silently failing on click.
 */
export interface ComposeAiToolbarAction {
  /** Stable action id — also the registry replace-by-id key. */
  readonly id: string;
  /** Visible button / menu-item text. */
  readonly label: string;
  /** Hover tooltip — see file header "TOOLTIP SOURCING". */
  readonly tooltip: string;
  /** `sprk_playbookconsumer` Binding row GUID. `''` = not yet wired (Phase 4 gate). */
  readonly bindingId: string;
  /** `'primary'` renders as an always-visible button; `'overflow'` lands in "More actions…". Selection-surface ORDERING hint only. */
  readonly placement: 'primary' | 'overflow';
  /**
   * DEF-09: `true` for a compose-DISPOSITION EDIT action whose result materializes
   * as an inline redline IN the Compose document (Draft alternative today; a future
   * clause-insertion action later). Drives two host behaviors when routed through the
   * Assistant queue: the dispatch targets the editor's DOCUMENT session (so the write
   * and the redline-materialize read coincide), and the Assistant shows a CONFIRMATION
   * only — never the proposed-text prose (that lives in the redline). Omitted/false for
   * the four INFORMATIONAL actions (explain/compare/summarize-changes/defined-terms),
   * which keep chat-session dispatch + Assistant-rendered prose.
   */
  readonly materializesInEditor?: boolean;

  // --- Contextual AI Tool Library — surfacing dimensions ---
  /**
   * WHICH UI surfaces render this tool. Defaults to `['selection']` when omitted
   * (= today's behavior: BubbleMenu only). A single definition may list several
   * surfaces (e.g. Draft alternative on `['selection','review-note']`). An empty
   * array RETIRES the tool from every surface without deleting its definition — used
   * to remove non-functional tools (round-8 #6) so they can be re-tagged later.
   */
  readonly surfaces?: readonly ToolSurface[];
  /**
   * WHICH WORK TYPES surface this tool — the product surface the user chose by intent
   * (`'agreement-analysis'`, `'legal-research'`, …), NOT the knowledge sub-domain (NDA
   * vs MSA is a grounding difference within a work type, not a tool-scoping axis).
   * `['*']` (default) = a shared edit primitive shown in every work type (e.g. Draft
   * alternative / Make concise); `['agreement-analysis']` = shown only in that surface.
   * The active work type narrows the surface's tool set to `workTypes ∋ '*' || activeWorkType`.
   */
  readonly workTypes?: readonly string[];
  /** Optional runtime predicate — return `false` to hide the tool for the given context. */
  readonly appliesTo?: (ctx: ToolContext) => boolean;
  /** Free-text prompt seed for instruction-style tools (e.g. "Describe a change…"). */
  readonly inputPrompt?: string;
}

/**
 * Tooltip copy — concise excerpts of the FR-23 enriched `compose-selection`
 * scope description (task 024). See file header "TOOLTIP SOURCING".
 */
const EXPLAIN_TOOLTIP =
  'Explain this clause in plain language, using matter playbook and precedent context where available.';
const COMPARE_TOOLTIP =
  'Compare this clause against the matter/firm playbook — surfaces matches, deviations, and a risk score.';
const DRAFT_TOOLTIP = 'Draft an alternative version of this clause as a pending suggestion you can accept or reject.';
// Tooltip copy for the whole-document overflow action (excerpt of the authored
// `sprk_playbookconsumer` toolDescription — same "tool descriptions ARE the
// prompt" sourcing rule as the primary three).
const DEFINED_TERMS_TOOLTIP =
  'Scan the document for defined terms and check they are used consistently. Read-only; results render in the Context pane.';

/**
 * The R2-committed AI actions (design.md §2.0/§2.1-§2.5). The three
 * clause-selection actions are `primary` (always-visible buttons); the
 * whole-document defined-terms action is `overflow` (in the "More actions…"
 * menu). All dispatch through the SAME 046 seam (`handleActionClick` →
 * `enqueueComposeAction` / `dispatchConsumer`) — no new route or discriminant.
 *
 * All ship with `bindingId: ''` — see file header "PHASE-4 STUB BOUNDARY".
 * The empty bindingId renders every action DISABLED until the deployed
 * `sprk_playbookconsumer` Binding GUID is registered via
 * `registerComposeAiToolbarAction` (see file header). Those GUIDs are minted
 * per-environment at catalog SEED time (task 047, owner/live-env), so button
 * ENABLEMENT is E2E-pending on that deploy — but the trigger ENTRY POINTS
 * (this registry) exist now.
 *
 * NOTE (FIX #5, UAT): `compose-summarize-word-changes` was REMOVED from this
 * selection-toolbar registry. It is a RETURN-FROM-WORD action requiring real
 * tracked-change data (`changesText`); on the selection toolbar it has no change
 * data, so the LLM fabricates a phantom "[Insertion]". Its legitimate trigger is
 * the return-from-Word reanchor UI (Cluster 3 / task 054), not this toolbar.
 */
const DEFAULT_ACTIONS: readonly ComposeAiToolbarAction[] = [
  {
    id: 'compose-explain-clause',
    label: 'Explain',
    tooltip: EXPLAIN_TOOLTIP,
    bindingId: '',
    placement: 'primary',
    // Round-8 #6 (UAT): RETIRED from the selection surface — the explain output was
    // not useful in practice. Definition kept so a future context can re-tag it
    // (`surfaces: ['selection']`) without re-authoring. `workTypes: ['*']` when re-enabled.
    surfaces: [],
  },
  {
    id: 'compose-compare-to-playbook',
    label: 'Compare to playbook',
    tooltip: COMPARE_TOOLTIP,
    bindingId: '',
    placement: 'primary',
    // Round-8 #6 (UAT): RETIRED from the selection surface — redundant with the NDA
    // Review Notes (which already carry per-clause playbook comparison). Re-tag per
    // work type if a non-advisory Compose context wants inline clause comparison.
    surfaces: [],
  },
  {
    // R8 UAT item 8 — RESTORED to the registry, but NOT to any surface.
    //
    // FIX #5 removed this definition outright, which also removed the only place its deployed
    // bindingId could land: `useComposeToolbarActivation` registers a capability onto the DEFAULT
    // action whose `id === consumerType`, and with no such action the binding was silently skipped.
    // The removal was right about the SURFACE (dispatched from the selection toolbar it has no change
    // data, so the model fabricates a phantom "[Insertion]") and wrong about the DEFINITION.
    //
    // `surfaces: []` is the established shape for exactly this — see `compose-explain-clause` and
    // `compose-compare-to-playbook` above, both retired from the selection surface with their
    // definitions kept. The Word menu's "Summarise changes" reads the bindingId from here at click
    // time; it never renders as a selection tool.
    id: 'compose-summarize-word-changes',
    label: 'Summarise changes',
    tooltip: 'Summarise the tracked changes made in Word',
    bindingId: '',
    placement: 'overflow',
    surfaces: [],
  },
  {
    id: 'compose-draft-alternative',
    label: 'Draft alternative',
    tooltip: DRAFT_TOOLTIP,
    bindingId: '',
    placement: 'primary',
    // DEF-09: the ONLY compose-disposition EDIT action — its alternative renders as an
    // inline redline in the document, so route to the DOCUMENT session + confirm-only.
    materializesInEditor: true,
    // Contextual AI Tool Library: the reusable edit primitive — shown on BOTH the
    // BubbleMenu and each Review Note's ⋮ menu, from this ONE definition (round-8 #4).
    // `workTypes` defaults to ['*'] (a shared primitive, available in every work type).
    surfaces: ['selection', 'review-note'],
  },
  {
    // Contextual AI Tool Library phase 3 (round-8 #4) — one-click "make more concise".
    // Same structured-edit → redline path as draft-alternative; different prompt intent.
    // Catalog: infra/dataverse/actions/compose-make-concise.action.json (+ binding row).
    id: 'compose-make-concise',
    label: 'Make more concise',
    tooltip:
      'Rewrite this clause to be more concise, preserving its exact legal meaning. Produces a pending track-change you can accept or reject.',
    bindingId: '',
    placement: 'primary',
    materializesInEditor: true,
    surfaces: ['selection', 'review-note'],
  },
  {
    // Contextual AI Tool Library phase 3 (round-8 #4) — free-text "describe a change".
    // The ONLY current tool with an `inputPrompt`: the host collects the user's
    // instruction before dispatch and passes it as the `instruction` slot.
    // Catalog: infra/dataverse/actions/compose-rewrite-instruction.action.json (+ binding row).
    id: 'compose-rewrite-instruction',
    label: 'Describe a change',
    tooltip:
      'Describe a change in your own words (e.g. "make this mutual", "add a 30-day cure period"). Produces a pending track-change you can accept or reject.',
    bindingId: '',
    placement: 'primary',
    materializesInEditor: true,
    surfaces: ['selection', 'review-note'],
    inputPrompt: 'Describe the change you’d like to make to this clause.',
  },
  {
    // gap 2.3 — the defined-terms scan (FR-11).
    id: 'compose-defined-terms',
    label: 'Defined terms',
    tooltip: DEFINED_TERMS_TOOLTIP,
    bindingId: '',
    placement: 'overflow',
    // Round-8 #6 (UAT): RETIRED from the selection surface — it did not work usefully
    // from the clause toolbar. Belongs to a future `whole-document` surface; re-tag
    // `surfaces: ['whole-document']` when that surface ships.
    surfaces: [],
  },
];

/** Module-level registration store — additive/replace-by-id (see file header). */
let registeredActions: ComposeAiToolbarAction[] = [];

/**
 * Re-render subscribers. `registerComposeAiToolbarAction` is called from the
 * HOST mount path (an ANCESTOR of this component — see
 * `useComposeToolbarActivation`), so a registration mutates MODULE state
 * without re-rendering an already-mounted toolbar via props. Each mounted
 * toolbar subscribes here so a LATE registration (the async
 * capability-discovery fetch resolves AFTER first paint) flips the matching
 * buttons disabled → enabled WITHOUT a remount and WITHOUT waiting for an
 * unrelated selection/transaction to force a re-read. This is a private module
 * signal — NOT a PaneEventBus event (ADR-030 closed union untouched); one
 * listener per mounted toolbar, removed on unmount.
 */
const registrationListeners = new Set<() => void>();

/**
 * Register (or replace, by `id`) an inline AI toolbar action. Task 048 (the
 * capability-discovery activation hook) calls this once per matching compose
 * capability to swap a stub's `bindingId: ''` for the real deployed Binding
 * GUID; a follow-on project calls this to add a wholly new action (e.g.
 * clause-insertion, design.md §2.0's "canonical deferred extension") WITHOUT
 * editing this file. Notifies subscribed toolbars so the change is visible
 * immediately.
 */
export function registerComposeAiToolbarAction(action: ComposeAiToolbarAction): void {
  registeredActions = [...registeredActions.filter(a => a.id !== action.id), action];
  registrationListeners.forEach(listener => listener());
}

/**
 * Subscribe to registry mutations; returns an unsubscribe fn. Consumed by
 * `ComposeAiToolbar` so an async host registration re-renders the mounted
 * toolbar.
 */
export function subscribeComposeAiToolbarActions(listener: () => void): () => void {
  registrationListeners.add(listener);
  return () => {
    registrationListeners.delete(listener);
  };
}

/** Merges `DEFAULT_ACTIONS` with registrations (registrations win by id). */
export function getComposeAiToolbarActions(): readonly ComposeAiToolbarAction[] {
  const overriddenIds = new Set(registeredActions.map(a => a.id));
  return [...DEFAULT_ACTIONS.filter(a => !overriddenIds.has(a.id)), ...registeredActions];
}

/**
 * Contextual AI Tool Library selector. Returns the tools that render on a given
 * `surface` for the `activeWorkType`, applying both library dimensions plus the
 * optional `appliesTo` predicate:
 *
 *   surfaces ∋ surface   AND   (workTypes ∋ '*' OR workTypes ∋ activeWorkType)   AND   appliesTo(ctx) !== false
 *
 * Defaults preserve today's behavior: a tool with no `surfaces` is treated as
 * `['selection']`; a tool with no `workTypes` is treated as `['*']` (a shared primitive).
 * This is how the BubbleMenu (`'selection'`) and each Review Note's ⋮ menu
 * (`'review-note'`) draw from ONE registry — a single definition surfaces in the
 * contexts it declares. `activeWorkType` is the product surface the user chose
 * (`'agreement-analysis'` / `'legal-research'`), NOT the knowledge sub-domain.
 */
export function getToolsForSurface(
  surface: ToolSurface,
  activeWorkType: string,
  ctx?: ToolContext
): readonly ComposeAiToolbarAction[] {
  return getComposeAiToolbarActions().filter(a => {
    const surfaces = a.surfaces ?? ['selection'];
    if (!surfaces.includes(surface)) return false;
    const workTypes = a.workTypes ?? ['*'];
    if (!workTypes.includes('*') && !workTypes.includes(activeWorkType)) return false;
    if (a.appliesTo && a.appliesTo(ctx ?? { activeWorkType }) === false) return false;
    return true;
  });
}

/** Test-only — clears registrations between test files/cases. */
export function __resetComposeAiToolbarActionsForTests(): void {
  registeredActions = [];
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ComposeAiToolbarProps {
  /** The mounted TipTap editor instance (from ComposeEditor's `useEditor`). */
  editor: Editor | null;
  /** Document pointer — forwarded as dispatch slots (`documentSpeId`/`documentRecordId`). */
  documentRef?: ComposeDocumentRef;
  /** ChatSession id — forwarded as a dispatch slot AND read by the dispatcher's session getter. */
  sessionId: string;
  /** BFF base URL — required for a real (non-stubbed) dispatch. */
  bffBaseUrl?: string;
  /** The host's `workspace`-channel PaneEventBus dispatcher (ComposeEditor's existing `useDispatchPaneEvent()` instance — no second one is created here). */
  dispatch: DispatchPaneEvent;
  /**
   * Action list override. Defaults to `getComposeAiToolbarActions()`. Tests
   * pass a fixed list so registry mutation in one test can't leak into
   * another; hosts should generally leave this undefined.
   */
  actions?: ReadonlyArray<ComposeAiToolbarAction>;
  /**
   * Contextual AI Tool Library — the ACTIVE work type (the product surface the user
   * chose), used to narrow the selection surface's tools (`getToolsForSurface('selection', …)`).
   * Defaults to `'*'`: only shared `workTypes: ['*']` primitives show. A host running a
   * specific work type (e.g. Agreement Analysis) passes its id (`'agreement-analysis'`)
   * so work-type-scoped tools also appear. Knowledge sub-domain (NDA vs MSA) does NOT
   * belong here — it only affects grounding. Ignored when `actions` is supplied.
   */
  activeWorkType?: string;
  /**
   * Contextual AI Tool Library (phase 3) — free-text tool prompt. When an action declares
   * an `inputPrompt` (e.g. "Describe a change…"), the toolbar calls this to collect the
   * user's free-text `instruction` BEFORE dispatch; the host renders the input UI and
   * resolves the entered text (or `null` if cancelled). Absent ⇒ an `inputPrompt` tool
   * cannot dispatch (no-op) — the host that wants free-text tools MUST provide this.
   */
  onRequestInstruction?: (action: ComposeAiToolbarAction) => Promise<string | null>;
  /**
   * FR-18 host serialization seam (see `ComposeActionEnqueue`). When provided,
   * every action dispatch routes through the host's serial queue instead of the
   * toolbar's own bound `dispatchConsumer`. Optional — omit for a standalone
   * mount with no concurrent Assistant queue.
   */
  enqueueComposeAction?: ComposeActionEnqueue;
  /**
   * Test/injection escape hatch for the bound dispatch helper — bypasses the
   * internal `createConsumerDispatcher` + `useAuth()` wiring so a test can
   * assert `dispatchConsumer(bindingId, args)` call shape without mocking
   * fetch/SSE/MSAL. Production hosts must NOT set this.
   */
  dispatchConsumerOverride?: DispatchConsumer;
  /**
   * Task 111 — bypasses the collapsed-selection render guard so the toolbar
   * renders even with NO selection (a caret / point-insertion). Used by
   * ComposeEditor's right-click (context-menu) trigger, which must open the
   * toolbar at the click point regardless of whether text is selected. Does
   * NOT affect `handleActionClick`'s dispatch guard — clicking an action on a
   * collapsed selection still no-ops defensively (unchanged). Defaults to
   * `false` (the selection-triggered BubbleMenu mount path, which keeps the
   * original "renders nothing on collapsed selection" behavior).
   */
  forceVisible?: boolean;
  /**
   * FR-07 (task 040) drift-proof AI anchoring — the generate-window bookmark controller
   * (`useAiGenerateBookmark`). OPT-IN + additive: when provided, clicking a
   * `materializesInEditor` (Generate) action drops a request-scoped bookmark at the current
   * selection, rebases it through concurrent user edits, and adds the resolved `targetParaId`
   * to the dispatch slots as model context; on the dispatch result it calls `resolveOnReturn`
   * so the returned JSON operations land at the REBASED selection (the apply/validate is
   * task 041, driven by the controller's surface callbacks). When ABSENT (the default, and
   * every existing mount/test), dispatch behavior is UNCHANGED — no bookmark, no extra slot.
   * The dispatch itself is unchanged (envelope-only, `Services/Ai/PublicContracts` via
   * `dispatchConsumer`; no new endpoint — ADR-039).
   */
  aiGenerateBookmark?: AiGenerateBookmarkController;
  /**
   * FR-07 (task 041) apply-side gate — the validate-before-apply + fuzzy-as-comment last-resort
   * controller (`useAiApplyValidation`). OPT-IN + additive, mirroring `aiGenerateBookmark`: when
   * BOTH `aiGenerateBookmark` and `aiApplyValidation` are supplied, a successful (`status:
   * 'operations'`) `resolveOnReturn` result is handed to `aiApplyValidation.validateAndApply` —
   * every returned operation's anchor is validated against the live document; a valid one applies
   * cleanly, an unvalidatable one surfaces as a review item in this toolbar's review banner
   * (rendered below the toolbar, dark-mode-correct — ADR-021). NEVER silently placed, NEVER
   * silently dropped (FR-07 / NFR-02 / I-7). When ABSENT, behavior is UNCHANGED from task 040 —
   * `resolveOnReturn`'s result is resolved but nothing further happens here.
   */
  aiApplyValidation?: AiApplyValidationController;
}

/** Short, human-readable label for a surfaced review item's reason (the toolbar's review banner). */
function reviewReasonLabel(reason: AiApplyReviewReason): string {
  switch (reason) {
    case 'unknown-paraId':
      return 'the target paragraph could no longer be found';
    case 'unknown-target-paraId':
      return 'the merge target paragraph could no longer be found';
    case 'out-of-range':
      return 'the target position moved out of range';
    case 'atom-interior':
      return 'the target overlaps non-editable content';
    case 'not-applied':
      return 'the suggestion could not be applied automatically';
    default:
      return 'needs review';
  }
}

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021 dark-mode compliant)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // DEF-17 (UAT-R3): the inline AI bubble renders on ONE line. `flexWrap: 'nowrap'`
  // keeps the primary buttons + in-context divider + overflow (⋯) trigger on a
  // single row; any action that does not fit belongs in the "More actions…"
  // overflow menu (placement: 'overflow'), NOT on a second wrapped row.
  //
  // FIX #9 (UAT): the elevated SURFACE now lives on THIS Toolbar (previously on
  // ComposeEditor's `bubbleMenu` wrapper, which produced the partial-background
  // finding). Putting the background + shadow on the Toolbar makes it span the
  // WHOLE menu — every item sits on the one surface. A light-grey elevated neutral
  // (`colorNeutralBackground3`) reads clearly on the white page and stays legible
  // in dark mode (the token flips); `shadow16` keeps the elevation. `columnGap`
  // gives a comfortable, even spacing between the icon-only buttons — not cramped,
  // not spread. Semantic tokens only (ADR-021 dark-mode-correct).
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'nowrap',
    whiteSpace: 'nowrap',
    columnGap: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground3,
    boxShadow: tokens.shadow16,
    borderRadius: tokens.borderRadiusMedium,
    paddingInline: tokens.spacingHorizontalS,
    paddingBlock: tokens.spacingVerticalXS,
  },
  // FR-07 (task 041) — the review banner surfacing unvalidatable AI operations. The SAME warning
  // semantic tokens ComposeEditor.tsx's redline/reanchor "needs attention" surfaces already use
  // (colorStatusWarning*) — dark-mode-correct, no hex literals (ADR-021).
  reviewBanner: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorStatusWarningBackground1,
    border: `1px solid ${tokens.colorStatusWarningBorder1}`,
  },
  reviewItem: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalS,
  },
  reviewItemText: {
    color: tokens.colorStatusWarningForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function ComposeAiToolbar(props: ComposeAiToolbarProps): React.JSX.Element | null {
  const {
    editor,
    documentRef,
    sessionId,
    bffBaseUrl,
    dispatch,
    actions,
    activeWorkType = '*',
    onRequestInstruction,
    enqueueComposeAction,
    dispatchConsumerOverride,
    forceVisible,
    aiGenerateBookmark,
    aiApplyValidation,
  } = props;
  const styles = useStyles();

  // Monotonic per-instance click counter → stable correlation ids for the FR-18
  // queue (avoids Date.now()/random; the queue surfaces `id` for UI affordance).
  const clickSeqRef = React.useRef(0);

  // Re-render on selection/transaction so the collapsed-selection guard below
  // stays in sync (mirrors ComposeFormatToolbar's identical pattern).
  const [, forceUpdate] = React.useReducer((x: number) => x + 1, 0);
  React.useEffect(() => {
    if (!editor) return;
    const handler = (): void => forceUpdate();
    editor.on('selectionUpdate', handler);
    editor.on('transaction', handler);
    return () => {
      editor.off('selectionUpdate', handler);
      editor.off('transaction', handler);
    };
  }, [editor]);

  // Re-render when the module action registry changes — a host's async
  // capability-discovery registration (task 048) lands AFTER first paint, so
  // an already-mounted toolbar must re-read `getComposeAiToolbarActions()` to
  // flip the matching buttons from stub-disabled to enabled. `forceUpdate` is
  // stable (useReducer dispatch), so an empty dep set is correct.
  React.useEffect(() => subscribeComposeAiToolbarActions(() => forceUpdate()), []);

  const { getAccessToken } = useAuth();

  // Bound dispatcher — created once per (bffBaseUrl, sessionId, auth, bus)
  // unless the host injects an override (tests only). Mirrors
  // useConsumerChips.tsx's `useMemo`-bound `createConsumerDispatcher` call.
  const dispatchConsumer = React.useMemo<DispatchConsumer>(
    () =>
      dispatchConsumerOverride ??
      createConsumerDispatcher({
        bffBaseUrl: bffBaseUrl ?? '',
        getSessionId: () => sessionId,
        getAccessToken,
        publishPaneEvent: (channel, event) => dispatch(channel, event as unknown as WorkspacePaneEvent),
      }),
    [dispatchConsumerOverride, bffBaseUrl, sessionId, getAccessToken, dispatch]
  );

  const handleActionClick = React.useCallback(
    async (action: ComposeAiToolbarAction): Promise<void> => {
      if (!editor) return;
      if (!action.bindingId) {
        // Phase-4 catalog gate — see file header "PHASE-4 STUB BOUNDARY".
        // eslint-disable-next-line no-console
        console.info(`[ComposeAiToolbar] action '${action.id}' has no Binding wired yet (Phase 4 catalog pending)`);
        return;
      }
      const { from, to } = editor.state.selection;
      if (from === to) return; // defensive — button is hidden on collapsed selection

      const rawText = editor.state.doc.textBetween(from, to, ' ');
      const selectionText =
        rawText.length > TOOLBAR_SELECTION_TEXT_CAP ? rawText.slice(0, TOOLBAR_SELECTION_TEXT_CAP) : rawText;

      // Contextual AI Tool Library (phase 3): a free-text tool (`inputPrompt`, e.g. "Describe a
      // change…") collects the user's instruction via the host dialog BEFORE dispatch. Selection
      // is already captured above (from/to/selectionText), so it survives the modal. Cancel/empty
      // ⇒ abort. The `instruction` becomes a dispatch slot the Action's inputSchema declares.
      let instruction: string | undefined;
      if (action.inputPrompt) {
        const entered = onRequestInstruction ? await onRequestInstruction(action) : null;
        if (!entered || !entered.trim()) return;
        instruction = entered.trim();
      }

      // FR-07 (task 040) — GENERATE-WINDOW BOOKMARK (opt-in via `aiGenerateBookmark`; drops only
      // for a `materializesInEditor` Generate action). Drop a request-scoped bookmark at the
      // current selection, rebased through concurrent edits, and send the resolved target paraId
      // as model context. `resolveOnReturn` (below) resolves it to the CURRENT position so the
      // returned ops land at the rebased selection (apply/validate = task 041, via the controller's
      // surface callbacks). Absent controller ⇒ nothing added (unchanged dispatch shape).
      const useBookmark = !!aiGenerateBookmark && action.materializesInEditor === true;
      const bookmarkRequestId = useBookmark ? `${action.id}#bm${(clickSeqRef.current += 1)}` : undefined;
      const bookmarkContext = useBookmark
        ? aiGenerateBookmark!.beginGenerate({ requestId: bookmarkRequestId })
        : undefined;

      // Slots mirror the SHIPPED compose-selection scope's authored field
      // list (task 024) — not invented (see Spike 0 §3a: the server owns the
      // typed parse against the Action's sprk_inputschema; this is the
      // authored scope contract the toolbar exists to satisfy).
      const args: DispatchConsumerArgs = {
        slots: {
          selectionText,
          selectionAnchorStart: from,
          selectionAnchorEnd: to,
          documentSpeId: documentRef?.speDriveItemId,
          documentRecordId: documentRef?.sprkDocumentId,
          sessionId,
          // FR-07: the durable target paraId as model context (only when a Generate bookmark was
          // dropped AND the caret sat in a paraId-bearing block). The model returns JSON operations
          // referencing this paraId, not free text to search (I-7).
          ...(useBookmark && bookmarkContext?.paraId ? { targetParaId: bookmarkContext.paraId } : {}),
          // Contextual AI Tool Library (phase 3): the free-text instruction for an `inputPrompt` tool.
          ...(instruction ? { instruction } : {}),
        },
      };

      // ADR-019: no raw server detail surfaces from here. Once a real Binding is
      // wired, the Assistant pane's existing dispatch-failure line (mirrors
      // useConsumerChips.tsx) is the user-visible surface.
      const swallow = (): void => undefined;

      // FR-07: on the dispatch RESULT, resolve the bookmark to its current (rebased) position and
      // hand the returned JSON operations to the apply path (task 041, via the controller's surface
      // callbacks — free text is refused, a deleted-content bookmark is surfaced for review). Only
      // when a bookmark was dropped; a rejected dispatch clears it so no stale bookmark leaks.
      // Task 041: when an `aiApplyValidation` controller is ALSO supplied, a successful
      // ('operations') resolution is handed to `validateAndApply` — every returned op's anchor is
      // validated against the live doc before it applies; an unvalidatable op surfaces via the
      // review banner below, never silently placed. Absent controller ⇒ behavior UNCHANGED (040).
      const resolveReturn = (result: DispatchConsumerResult): void => {
        if (!useBookmark || !bookmarkRequestId) return;
        const outcome = aiGenerateBookmark!.resolveOnReturn(bookmarkRequestId, result?.result);
        if (outcome?.status === 'operations' && aiApplyValidation) {
          void aiApplyValidation.validateAndApply(outcome);
        }
      };
      const onDispatchError = (): void => {
        if (useBookmark && bookmarkRequestId) aiGenerateBookmark!.clearBookmark(bookmarkRequestId);
      };

      // FR-18: route through the host serial queue when threaded (serializes
      // rapid Compare→Draft clicks); else fall back to the toolbar's own bound
      // dispatcher (standalone mount, no concurrent queue to coordinate with).
      if (enqueueComposeAction) {
        const requestId = `${action.id}#${(clickSeqRef.current += 1)}`;
        // DEF-09: for a compose-disposition EDIT action, thread the editor's DOCUMENT
        // session so the host routes the dispatch there (write ⇄ redline-read coincide)
        // and shows a confirmation-only line. Informational actions omit it (chat session
        // + Assistant prose). The standalone `dispatchConsumer` fallback below already
        // targets the document session via its own `getSessionId: () => sessionId`.
        void enqueueComposeAction({
          id: requestId,
          bindingId: action.bindingId,
          args,
          ...(action.materializesInEditor ? { documentSessionId: sessionId } : {}),
        })
          .then(resolveReturn)
          .catch(() => {
            onDispatchError();
            swallow();
          });
      } else {
        void dispatchConsumer(action.bindingId, args)
          .then(resolveReturn)
          .catch(() => {
            onDispatchError();
            swallow();
          });
      }
    },
    [
      editor,
      dispatchConsumer,
      enqueueComposeAction,
      documentRef,
      sessionId,
      aiGenerateBookmark,
      aiApplyValidation,
      onRequestInstruction,
    ]
  );

  // Round-8 #6 (UAT): the "Email" split-menu was REMOVED from the selection toolbar —
  // it launched an email hand-off from the clause BubbleMenu, which the review UX did not
  // want. (The "Email — coming soon" stub it used to launch was deleted once the full email
  // widget shipped — email-communication-solution-r5.) `extractCleanDraftText` stays exported
  // for any future email path.

  if (!editor) return null;

  const { from, to } = editor.state.selection;
  // NEGATIVE case — collapsed/empty selection hides the ACTION toolbar, UNLESS the host
  // force-opened it (task 111 right-click / point-insertion trigger). Task 041: the review banner
  // (surfaced unvalidatable AI operations) is independent of selection state — a surfaced item
  // must stay visible even after the selection that triggered it changes, so it is NOT gated here.
  const showActionToolbar = !(from === to && !forceVisible);
  const reviewItems = aiApplyValidation?.reviewQueue ?? [];
  if (!showActionToolbar && reviewItems.length === 0) return null;

  // Contextual AI Tool Library: the BubbleMenu IS the `selection` surface, so draw from
  // `getToolsForSurface('selection', …)` — this is what drops the tools retired via
  // `surfaces: []` (round-8 #6: Explain / Compare / Defined-terms) and narrows to the
  // active work type. `placement` remains the intra-surface primary-vs-overflow ORDERING.
  // Tests may still inject a fixed `actions` list.
  const allActions = actions ?? getToolsForSurface('selection', activeWorkType);
  const primaryActions = allActions.filter(a => a.placement === 'primary');
  const overflowActions = allActions.filter(a => a.placement === 'overflow');

  // Task 111 — SINGLE Toolbar (no wrapping Fragment, no orphaned top-level
  // divider). GAP FIX (UAT, 2026-07-15): the two ToolbarDividers were removed.
  // Fluent's ToolbarDivider ships `padding: 0 12px`, which (with the toolbar's
  // columnGap on each side) produced a ~40px "large gap" between the primary AI
  // icons and the trailing Email / overflow (⋮) affordances. Removing them lets
  // every action sit in ONE tightly-spaced, evenly-spaced row at the single
  // `columnGap` token — no large gap, all actions still reachable, all
  // aria-labels preserved. Grouping is carried by distinct glyphs + tooltips.
  return (
    <>
      {showActionToolbar ? (
        <Toolbar size="small" className={styles.toolbar} aria-label="AI actions" data-testid="compose-ai-toolbar">
          {/* FIX #9 — ICON-ONLY primary buttons (the tool WORDS were removed); the
          hover Tooltip names each tool. Names come from `action.label`. */}
          {primaryActions.map(action => (
            <Tooltip key={action.id} content={action.label} relationship="description" withArrow>
              <ToolbarButton
                appearance="subtle"
                icon={actionIcon(action.id)}
                disabled={!action.bindingId}
                aria-label={action.label}
                data-testid={`compose-ai-toolbar-${action.id}`}
                onClick={() => void handleActionClick(action)}
              />
            </Tooltip>
          ))}

          {/* Round-8 #6 (UAT): the Email split-menu was removed from this toolbar. */}
          <Menu positioning="below-end">
            <MenuTrigger disableButtonEnhancement>
              <Tooltip content="More actions" relationship="description" withArrow>
                <ToolbarButton
                  appearance="subtle"
                  // FIX #9 — VERTICAL three-dots overflow affordance.
                  icon={<MoreVertical20Regular />}
                  aria-label="More actions"
                  data-testid="compose-ai-toolbar-more"
                />
              </Tooltip>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {overflowActions.length === 0 ? (
                  <MenuItem disabled data-testid="compose-ai-toolbar-more-empty">
                    No additional actions yet
                  </MenuItem>
                ) : (
                  overflowActions.map(action => (
                    <MenuItem
                      key={action.id}
                      disabled={!action.bindingId}
                      title={action.tooltip}
                      data-testid={`compose-ai-toolbar-overflow-${action.id}`}
                      onClick={() => void handleActionClick(action)}
                    >
                      {action.label}
                    </MenuItem>
                  ))
                )}
              </MenuList>
            </MenuPopover>
          </Menu>
        </Toolbar>
      ) : null}

      {/* FR-07 (task 041) — review banner: unvalidatable AI operations, surfaced never silently
        placed/dropped (FR-07 / NFR-02 / I-7). Dark-mode-correct semantic tokens only (ADR-021). */}
      {reviewItems.length > 0 ? (
        <div className={styles.reviewBanner} role="status" data-testid="compose-ai-review-banner">
          {reviewItems.map(item => (
            <div key={item.id} className={styles.reviewItem} data-testid={`compose-ai-review-item-${item.id}`}>
              <Text size={200} className={styles.reviewItemText}>
                An AI suggestion needs review — {reviewReasonLabel(item.reason)}
                {item.fuzzy?.matchedParagraphPreview
                  ? ` (possible match: "${item.fuzzy.matchedParagraphPreview}")`
                  : ''}
                .
              </Text>
              <Tooltip content="Dismiss" relationship="description" withArrow>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<Dismiss16Regular />}
                  aria-label="Dismiss review item"
                  data-testid={`compose-ai-review-dismiss-${item.id}`}
                  onClick={() => aiApplyValidation?.dismissReview(item.id)}
                />
              </Tooltip>
            </div>
          ))}
        </div>
      ) : null}
    </>
  );
}

ComposeAiToolbar.displayName = 'ComposeAiToolbar';

/** Icon per default action id; a registered follow-on action without a match falls back to the edit icon. */
function actionIcon(actionId: string): React.JSX.Element {
  switch (actionId) {
    case 'compose-explain-clause':
      return <Info24Regular />;
    case 'compose-compare-to-playbook':
      return <ArrowSwapRegular />;
    case 'compose-draft-alternative':
      return <DocumentEdit24Regular />;
    case 'compose-make-concise':
      return <TextGrammarArrowLeft24Regular />;
    case 'compose-rewrite-instruction':
      return <Wand24Regular />;
    default:
      return <DocumentEdit24Regular />;
  }
}
