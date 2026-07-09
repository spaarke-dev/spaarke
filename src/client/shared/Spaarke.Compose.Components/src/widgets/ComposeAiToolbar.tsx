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
 * `DEFAULT_ACTIONS` below ships with `bindingId: ''` for all three primary
 * actions. The real `sprk_playbookconsumer` Binding GUIDs are authored by
 * Phase 4 catalog-row tasks (040-044), which are core-A0-gated (blocked on
 * core task 020, the triple-twin description hoist) — see
 * `projects/spaarkeai-compose-r2/CLAUDE.md` "Core Phase A0 dependency".
 * An empty `bindingId` renders the button DISABLED (honest stub — clicking a
 * disabled button cannot silently no-op or 400) rather than wired-but-broken.
 * Phase 4 activates each action with ONE call to
 * `registerComposeAiToolbarAction({ ...same id..., bindingId: '<real GUID>' })`
 * — no edit to this file required (the registration-extensibility guarantee,
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
 * @see ./ComposeFormatToolbar.tsx (sibling BubbleMenu content; NOT an AI-action surface)
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
const DRAFT_TOOLTIP =
  'Draft an alternative version of this clause as a pending suggestion you can accept or reject.';

/**
 * The three R2-committed primary actions (design.md §2.0/§2.1-§2.3). All
 * ship with `bindingId: ''` — see file header "PHASE-4 STUB BOUNDARY".
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
];

/** Module-level registration store — additive/replace-by-id (see file header). */
let registeredActions: ComposeAiToolbarAction[] = [];

/**
 * Register (or replace, by `id`) an inline AI toolbar action. Phase 4 calls
 * this once per catalog row to swap a stub's `bindingId: ''` for the real
 * Binding GUID; a follow-on project calls this to add a wholly new action
 * (e.g. clause-insertion, design.md §2.0's "canonical deferred extension")
 * WITHOUT editing this file.
 */
export function registerComposeAiToolbarAction(action: ComposeAiToolbarAction): void {
  registeredActions = [...registeredActions.filter(a => a.id !== action.id), action];
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
}

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021 dark-mode compliant)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXXS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function ComposeAiToolbar(props: ComposeAiToolbarProps): React.JSX.Element | null {
  const { editor, documentRef, sessionId, bffBaseUrl, dispatch, actions, enqueueComposeAction, dispatchConsumerOverride } = props;
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
  if (from === to) return null; // NEGATIVE case — collapsed/empty selection shows no toolbar

  const allActions = actions ?? getComposeAiToolbarActions();
  const primaryActions = allActions.filter(a => a.placement === 'primary');
  const overflowActions = allActions.filter(a => a.placement === 'overflow');

  return (
    <>
      <ToolbarDivider />
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
    default:
      return <DocumentEdit24Regular />;
  }
}
