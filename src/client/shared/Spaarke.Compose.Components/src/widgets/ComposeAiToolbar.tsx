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
 * padding). The divider now lives INSIDE this single Toolbar (between the
 * primary buttons and the overflow trigger) so it always has context. A new
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
  ToolbarDivider,
  Tooltip,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Info24Regular, ArrowSwapRegular, DocumentEdit24Regular, MoreHorizontal20Regular } from '@fluentui/react-icons';
import { useAuth } from '@spaarke/auth';
import {
  createConsumerDispatcher,
  type DispatchConsumer,
  type DispatchConsumerArgs,
  type DispatchConsumerResult,
} from '@spaarke/ui-components';
import type { DispatchPaneEvent, WorkspacePaneEvent } from '@spaarke/ai-widgets/events';
import type { ComposeDocumentRef } from '../types/compose-contracts';

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
  /** `'primary'` renders as an always-visible button; `'overflow'` lands in "More actions…". */
  readonly placement: 'primary' | 'overflow';
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
// Tooltip copy for the two whole-document overflow actions (excerpts of the
// authored `sprk_playbookconsumer` toolDescription for each — same "tool
// descriptions ARE the prompt" sourcing rule as the primary three).
const SUMMARIZE_WORD_CHANGES_TOOLTIP =
  'Summarize the tracked changes a reviewer made in Word — insertions, deletions, comments, and structural edits — in plain language. Read-only.';
const DEFINED_TERMS_TOOLTIP =
  'Scan the document for defined terms and check they are used consistently. Read-only; results render in the Context pane.';

/**
 * The five R2-committed AI actions (design.md §2.0/§2.1-§2.5). The three
 * clause-selection actions are `primary` (always-visible buttons); the two
 * whole-document actions (summarize-word-changes, defined-terms) are
 * `overflow` (in the "More actions…" menu) — closing gap 2.3 (they previously
 * had NO client trigger at all: absent from DEFAULT_ACTIONS, the overflow was
 * empty). All five dispatch through the SAME 046 seam (`handleActionClick` →
 * `enqueueComposeAction` / `dispatchConsumer`) — no new route or discriminant.
 *
 * All ship with `bindingId: ''` — see file header "PHASE-4 STUB BOUNDARY".
 * The empty bindingId renders every action DISABLED until the deployed
 * `sprk_playbookconsumer` Binding GUID is registered via
 * `registerComposeAiToolbarAction` (see file header). Those GUIDs are minted
 * per-environment at catalog SEED time (task 047, owner/live-env), so button
 * ENABLEMENT is E2E-pending on that deploy — but the trigger ENTRY POINTS
 * (this registry) exist now, which is what gap 2.3 required.
 *
 * NOTE (summarize-word-changes): the overflow gives it a working trigger now.
 * Its dedicated "return-from-Word" entry (fired when a reviewer's edits come
 * back from Word) is hosted by the return-from-Word reanchor UI (Cluster 3 /
 * task 054), which is not yet mounted — that additional entry lands with 054.
 */
const DEFAULT_ACTIONS: readonly ComposeAiToolbarAction[] = [
  {
    id: 'compose-explain-clause',
    label: 'Explain',
    tooltip: EXPLAIN_TOOLTIP,
    bindingId: '',
    placement: 'primary',
  },
  {
    id: 'compose-compare-to-playbook',
    label: 'Compare to playbook',
    tooltip: COMPARE_TOOLTIP,
    bindingId: '',
    placement: 'primary',
  },
  {
    id: 'compose-draft-alternative',
    label: 'Draft alternative',
    tooltip: DRAFT_TOOLTIP,
    bindingId: '',
    placement: 'primary',
  },
  {
    // gap 2.3 — the return-from-Word summarization trigger (FR-10). Overflow
    // entry now; the dedicated return-from-Word entry lands with task 054.
    id: 'compose-summarize-word-changes',
    label: 'Summarize Word changes',
    tooltip: SUMMARIZE_WORD_CHANGES_TOOLTIP,
    bindingId: '',
    placement: 'overflow',
  },
  {
    // gap 2.3 — the defined-terms scan (FR-11). Overflow-menu trigger per the
    // authored Binding row ("overflow-menu trigger"); results surface read-only
    // in the Context pane.
    id: 'compose-defined-terms',
    label: 'Defined terms',
    tooltip: DEFINED_TERMS_TOOLTIP,
    bindingId: '',
    placement: 'overflow',
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
}

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021 dark-mode compliant)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Task 111: the redundant `display: 'flex'` / `columnGap` override was
  // dropped — Fluent's own `<Toolbar>` already applies its layout (fighting
  // it caused double-spacing). `flexWrap` is an ADDITION (not a duplicate of
  // Toolbar's own behavior): it lets the AI buttons wrap onto a second row
  // instead of overflowing the popup width when the host (ComposeEditor's
  // `styles.bubbleMenu`) width-caps the container.
  toolbar: {
    flexWrap: 'wrap',
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
    enqueueComposeAction,
    dispatchConsumerOverride,
    forceVisible,
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
    (action: ComposeAiToolbarAction): void => {
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
        },
      };

      // ADR-019: no raw server detail surfaces from here. Once a real Binding is
      // wired, the Assistant pane's existing dispatch-failure line (mirrors
      // useConsumerChips.tsx) is the user-visible surface.
      const swallow = (): void => undefined;

      // FR-18: route through the host serial queue when threaded (serializes
      // rapid Compare→Draft clicks); else fall back to the toolbar's own bound
      // dispatcher (standalone mount, no concurrent queue to coordinate with).
      if (enqueueComposeAction) {
        const requestId = `${action.id}#${(clickSeqRef.current += 1)}`;
        void enqueueComposeAction({ id: requestId, bindingId: action.bindingId, args }).catch(swallow);
      } else {
        void dispatchConsumer(action.bindingId, args).catch(swallow);
      }
    },
    [editor, dispatchConsumer, enqueueComposeAction, documentRef, sessionId]
  );

  if (!editor) return null;

  const { from, to } = editor.state.selection;
  // NEGATIVE case — collapsed/empty selection shows no toolbar, UNLESS the
  // host force-opened it (task 111 right-click / point-insertion trigger).
  if (from === to && !forceVisible) return null;

  const allActions = actions ?? getComposeAiToolbarActions();
  const primaryActions = allActions.filter(a => a.placement === 'primary');
  const overflowActions = allActions.filter(a => a.placement === 'overflow');

  // Task 111 — SINGLE Toolbar (no wrapping Fragment, no orphaned top-level
  // divider): the divider is a DIRECT CHILD of this Toolbar (between the
  // primary buttons and the overflow trigger), so it always has Toolbar
  // context (see file header "TASK 111" note).
  return (
    <Toolbar size="small" className={styles.toolbar} aria-label="AI actions" data-testid="compose-ai-toolbar">
      {primaryActions.map(action => (
        <Tooltip key={action.id} content={action.tooltip} relationship="description" withArrow>
          <ToolbarButton
            appearance="subtle"
            icon={actionIcon(action.id)}
            disabled={!action.bindingId}
            aria-label={action.label}
            data-testid={`compose-ai-toolbar-${action.id}`}
            onClick={() => handleActionClick(action)}
          >
            {action.label}
          </ToolbarButton>
        </Tooltip>
      ))}

      <ToolbarDivider />

      <Menu positioning="below-end">
        <MenuTrigger disableButtonEnhancement>
          <ToolbarButton
            appearance="subtle"
            icon={<MoreHorizontal20Regular />}
            aria-label="More actions"
            data-testid="compose-ai-toolbar-more"
          />
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
                  onClick={() => handleActionClick(action)}
                >
                  {action.label}
                </MenuItem>
              ))
            )}
          </MenuList>
        </MenuPopover>
      </Menu>
    </Toolbar>
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
    default:
      return <DocumentEdit24Regular />;
  }
}
