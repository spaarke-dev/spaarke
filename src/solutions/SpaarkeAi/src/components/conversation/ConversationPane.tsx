/**
 * ConversationPane.tsx — R3 left pane for the SpaarkeAi three-pane shell.
 *
 * Replaces R1's LeftPane + ChatPanel combination. Composes:
 *   - Pane header: shared <PaneHeader> primitive from @spaarke/ui-components
 *     (FR-02, task 021) — "Assistant" title + ChatRegular brand-color icon.
 *     The header's rightSlot is reserved for the History side-overlay trigger
 *     (FR-03 / OC-01) wired by task 022.
 *   - Welcome state: WelcomePanel (no session, no entity, no pending message)
 *   - Active chat: SprkChat (session active, entity context, or playbook selected)
 *
 * Key R1 → R2 migration changes:
 *   - Auth and session state consumed from useAiSession() (R2 AiSessionProvider)
 *     instead of useStandaloneAi() (R1 StandaloneAiProvider).
 *   - SprkChat's onPaneEvent callback bridges to AiSessionProvider's
 *     streaming.onPaneEvent, which routes SSE events to the typed PaneEventBus.
 *     Multiple panes (WorkspacePane, ContextPaneController) subscribe independently.
 *   - onSessionCreated and onPlaybookChange update AiSessionProvider state,
 *     which persists to sessionStorage identically to the R1 behaviour.
 *   - ShellStageContext transitions are driven from here:
 *       first message sent     → toLoading()
 *       stream starts          → (bus handles active-chat via widget_load)
 *       welcome prompt click   → toLoading()
 *       playbook-selected bus  → toLoading() (Stage 1 → Stage 2, AIPU2-102)
 *
 * Cross-pane playbook selection (AIPU2-102):
 *   PlaybookGalleryWidget dispatches 'playbook-selected' on the 'conversation'
 *   PaneEventBus channel when the user picks a playbook from the gallery in the
 *   Context pane. ConversationPane subscribes and:
 *     1. Calls setPlaybookId() to update AiSessionProvider (persisted to sessionStorage).
 *     2. Advances the shell stage: welcome → loading (Stage 1 → Stage 2).
 *     3. Shows a brief confirmation toast (auto-dismissed after 3 s).
 *     4. Tracks the active playbook name for the header strip.
 *   A "Change playbook" button in the header strip resets to Stage 1 (gallery).
 *
 * SprkChat prop preservation (R1 → R2 mapping):
 *   apiBaseUrl         ← bffBaseUrl (same value, same meaning)
 *   accessToken        ← token
 *   sessionId          ← chatSessionId
 *   playbookId         ← playbookId
 *   onSessionCreated   ← handleSessionCreated (updates setChatSessionId)
 *   onPlaybookChange   ← handlePlaybookChange (updates setPlaybookId)
 *   predefinedPrompts  ← from pendingMessage (welcome flow)
 *   hostContext        ← derived from entityContext (same mapping as R1)
 *   onPaneEvent        ← streaming.onPaneEvent (routes to PaneEventBus channels)
 *
 * Stage-aware rendering:
 *   No session + no entity + no pending message + no playbook → WelcomePanel
 *   Otherwise → SprkChat
 *
 * @see ChatPanel.tsx (R1) — the component this replaces
 * @see LeftPane.tsx (R1) — the tab wrapper this replaces
 * @see AiSessionProvider.tsx — session + streaming + PaneEventBus routing (R2)
 * @see PlaybookGalleryWidget.tsx — dispatches playbook-selected (AIPU2-086/102)
 * @see WelcomePanel.tsx — welcome experience (unchanged from R1)
 * @see ChatHistoryPanel.tsx — rewired to a side-overlay by task 022 (FR-03 / OC-01)
 * @see PaneHeader.tsx (@spaarke/ui-components) — shared header primitive (FR-01, task 010)
 * @see ADR-012 — Shared component library (PaneHeader lives in @spaarke/ui-components)
 * @see ADR-021 — Fluent v9, dark mode via FluentProvider (no hardcoded colors)
 * @see ADR-022 — React 19 Code Pages (hooks, functional components, bundled)
 */

import * as React from "react";
import {
  makeStyles,
  mergeClasses,
  tokens,
  Button,
  Spinner,
  Tag,
  Text,
  Tooltip,
} from "@fluentui/react-components";
import {
  ChatRegular,
  EditRegular,
  DismissRegular,
  ArrowResetRegular,
  CheckmarkCircleRegular,
  HistoryRegular,
} from "@fluentui/react-icons";
// PaneHeader is the canonical pane-header primitive lifted into the shared
// library in Phase A task 010 (ADR-012). It owns the icon brand-color treatment
// and the right-slot container — see PaneHeader.tsx in @spaarke/ui-components.
import { PaneHeader, SprkChat } from "@spaarke/ui-components";
import { useAiSession, usePaneEvent, useDispatchPaneEvent } from "@spaarke/ai-widgets";
import type { WorkspacePaneEvent } from "@spaarke/ai-widgets";
import type { IChatSession } from "@spaarke/ai-context";
import { WelcomePanel } from "../WelcomePanel";
import {
  useShellStage,
  useRestoreContext,
  usePaneCollapseContext,
} from "../shell/ThreePaneShell";
import { HistoryOverlay } from "./HistoryOverlay";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

// NOTE (task 021, FR-02): The legacy `LeftPaneView` ("chat" | "history") tab
// model was removed when the Chat/History tab buttons were replaced by the
// shared <PaneHeader>. History becomes a side-overlay (OC-01) wired in task 022.

/**
 * State for the "Refine this?" selection chip shown above the SprkChat input.
 *
 * `null` means no chip is currently shown. When non-null the chip displays a
 * truncated preview of the selected text; clicking the chip injects the full
 * selectedText into the SprkChat input as a predefined prompt context block.
 */
interface SelectionChipState {
  /** Full text the user selected in the workspace widget. */
  selectedText: string;
  /** Human-readable origin label from the widget (e.g. "Document viewer"). */
  contextLabel: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },

  // NOTE (task 021, FR-02): The legacy tab-bar styles (`tabBar`, `tabButton`,
  // `tabButtonActive`) were removed when the Chat/History tab buttons were
  // replaced by the shared <PaneHeader> primitive. Visual treatment now lives
  // inside @spaarke/ui-components/PaneHeader (matches ContextPaneController
  // header — canonical reference per plan §2).

  // ── Pane content area ─────────────────────────────────────────────────────
  //
  // task 068 (Bug 1): now a flex column so the (optional) welcome heading
  // and the always-mounted chat region stack correctly. The chat region
  // grows to fill remaining vertical space via `chatWrapper.flex: 1`.
  content: {
    flex: 1,
    minHeight: 0,
    overflow: "hidden",
    display: "flex",
    flexDirection: "column",
  },

  // ── Auth loading state ────────────────────────────────────────────────────
  loadingContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },

  // ── Chat region wrapper ───────────────────────────────────────────────────
  //
  // task 068 (Bug 1): chatWrapper is the always-rendered chat region. It
  // grows to fill the remaining height below the optional welcome heading
  // via `flex: 1`. The legacy `welcomeWrapper` (previously a 100%-height
  // shell around WelcomePanel's Recent Conversations list) was removed
  // when WelcomePanel became a heading-only shell.
  chatWrapper: {
    flex: 1,
    minHeight: 0,
    overflow: "hidden",
    display: "flex",
    flexDirection: "column",
  },

  // ── Playbook header strip (AIPU2-102) ────────────────────────────────────
  //
  // Shown when a playbook is active (selected from the gallery, Stage 2+).
  // Displays the playbook name and a "Change playbook" button that returns
  // the user to Stage 1 (welcome / gallery). Fluent v9 tokens only (ADR-021).
  playbookHeader: {
    flexShrink: 0,
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorBrandBackground2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorBrandStroke2,
    minHeight: "32px",
  },

  playbookHeaderName: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    flex: "1",
    minWidth: "0",
  },

  changePlaybookButton: {
    flexShrink: 0,
    fontSize: tokens.fontSizeBase100,
    height: "24px",
    minWidth: "0",
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
  },

  // ── Playbook confirmation toast (AIPU2-102) ───────────────────────────────
  //
  // Brief confirmation strip at the bottom of the pane after a playbook is
  // selected from the gallery. Auto-dismissed after 3 s. Fluent v9 tokens only.
  toastStrip: {
    flexShrink: 0,
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorStatusSuccessBackground1,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorStatusSuccessForeground3,
  },

  toastIcon: {
    color: tokens.colorStatusSuccessForeground1,
    fontSize: tokens.fontSizeBase300,
    flexShrink: 0,
  },

  toastText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorStatusSuccessForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },

  // ── "Refine this?" selection chip ─────────────────────────────────────────
  //
  // The chip strip sits above the SprkChat input bar. It appears only when a
  // workspace widget dispatches a selection_changed event with non-null text.
  // Fluent v9 tokens only — no hard-coded colors (ADR-021).
  refinementChipBar: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  refinementChipLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  refinementChipTag: {
    cursor: "pointer",
    maxWidth: "220px",
  },
  refinementChipTagText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    fontSize: tokens.fontSizeBase200,
  },
  refinementChipDismiss: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    ":hover": {
      color: tokens.colorNeutralForeground1,
    },
  },
  sprkChatFlex: {
    flex: 1,
    minHeight: 0,
    overflow: "hidden",
  },

  // ── Conversation restore summary block (AIPU2-106) ──────────────────────
  restoreSummaryBlock: {
    flexShrink: 0,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground3,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    cursor: "pointer",
  },
  restoreSummaryHeader: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  restoreSummaryContent: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    whiteSpace: "pre-wrap",
    maxHeight: "120px",
    overflowY: "auto",
    lineHeight: tokens.lineHeightBase200,
  },
  restoreStaleWarning: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorStatusWarningBackground1,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorStatusWarningForeground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorStatusWarningForeground3,
  },
});

// ---------------------------------------------------------------------------
// ConversationPane
// ---------------------------------------------------------------------------

/**
 * ConversationPane — left slot of ThreePaneLayout for the SpaarkeAi Code Page (R3).
 *
 * Renders a shared <PaneHeader> ("Assistant" + ChatRegular brand-color icon).
 * Below the header the chat region (SprkChat) is ALWAYS mounted — when in the
 * welcome stage a small WelcomePanel heading ("How can I help you today?")
 * sits above it (task 068, Bug 1 fix). History is reached via the PaneHeader
 * rightSlot HistoryOverlay (task 022, OC-01). All session and streaming state
 * is consumed from useAiSession() — this component contains no auth or SSE
 * logic of its own.
 *
 * Welcome → ActiveChat transition (task 068):
 *   1. Cold load: SprkChat is mounted with the WelcomePanel heading above.
 *      The user types directly into the chat input — there are no prompt
 *      buttons or Recent Conversations cards (UX-A removed).
 *   2. SprkChat sends the first message → onSessionCreated fires → chatSessionId
 *      becomes non-null → WelcomePanel heading disappears; chat continues.
 *   3. Session resume: HistoryOverlay (PaneHeader history icon) calls
 *      setChatSessionId → chatSessionId becomes non-null → SprkChat loads
 *      the prior session's messages.
 */
export function ConversationPane(): React.JSX.Element {
  const styles = useStyles();

  // ── R2 session state — from AiSessionProvider (function-based auth, §H-4) ──
  //
  // No `token: string` is destructured. SprkChat receives `authenticatedFetch`
  // and `getAccessToken` instead — the token never crosses a component boundary.
  const {
    isAuthenticated,
    authenticatedFetch,
    getAccessToken,
    bffBaseUrl,
    chatSessionId,
    setChatSessionId,
    playbookId,
    setPlaybookId,
    entityContext,
    streaming,
  } = useAiSession();

  // ── Shell stage transitions ─────────────────────────────────────────────
  const { toLoading, reset } = useShellStage();

  // ── Pane collapse (Task 094) ────────────────────────────────────────────
  //
  // The Assistant pane participates in the three-pane collapse/expand
  // feature owned by the shell. Clicking the PaneHeader (anywhere except
  // the History icon) toggles collapse via `paneCollapse.toggle('assistant')`.
  // When the context is null (e.g. ConversationPane rendered in isolation
  // by tests) collapse is simply disabled.
  const paneCollapse = usePaneCollapseContext();
  const handleHeaderCollapse = React.useCallback(() => {
    paneCollapse?.toggle("assistant");
  }, [paneCollapse]);
  const isAssistantExpanded = !(paneCollapse?.isCollapsed("assistant") ?? false);

  // ── Session restore context (AIPU2-106) ─────────────────────────────────
  const restoreCtx = useRestoreContext();
  const [summaryExpanded, setSummaryExpanded] = React.useState(false);

  // ── PaneEventBus dispatch — conversation channel ────────────────────────
  // Used to broadcast first_message events so ShellStageManager can advance
  // the stage from welcome → loading via the bus subscriber path. This is the
  // bus-driven equivalent of the direct toLoading() call below.
  const dispatch = useDispatchPaneEvent();

  // NOTE (task 021, FR-02): The legacy `activeView` tab state ("chat" | "history")
  // was removed when the Chat/History tab buttons were replaced by <PaneHeader>.
  // The History UI becomes a side-overlay (OC-01) wired below via the
  // <PaneHeader> rightSlot — see HistoryOverlay and historyOpen state.

  // ── History side-overlay state (task 022, FR-03 / OC-01) ────────────────
  //
  // Toggled by the HistoryRegular button in the PaneHeader rightSlot. When
  // true, <HistoryOverlay> slides in from the trailing edge of the viewport
  // (Claude-Code-style). Selecting a session calls setChatSessionId which
  // resumes the conversation via the existing AiSessionProvider flow.
  const [historyOpen, setHistoryOpen] = React.useState<boolean>(false);
  const handleOpenHistory = React.useCallback(() => setHistoryOpen(true), []);
  const handleCloseHistory = React.useCallback(() => setHistoryOpen(false), []);

  // ── Playbook selection state (AIPU2-102) ────────────────────────────────
  //
  // activePlaybookName: display name of the playbook currently selected from the
  // gallery (via the playbook-selected bus event). Drives the header strip.
  // null when no gallery selection has been made this session.
  const [activePlaybookName, setActivePlaybookName] = React.useState<string | null>(null);

  // toastPlaybookName: the playbook name shown in the bottom confirmation toast.
  // Cleared after TOAST_DURATION_MS by a timer started on each gallery selection.
  const [toastPlaybookName, setToastPlaybookName] = React.useState<string | null>(null);
  const toastTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  /** Duration (ms) for which the confirmation toast strip is visible after playbook selection. */
  const TOAST_DURATION_MS = 3000;

  // Subscribe to 'conversation' channel — handle playbook-selected events from
  // PlaybookGalleryWidget (Context pane). On receipt:
  //   1. Persist the new playbookId to AiSessionProvider + sessionStorage.
  //   2. Advance shell stage: welcome → loading (Stage 1 → Stage 2).
  //   3. Track the display name for the header strip.
  //   4. Show a brief confirmation toast (auto-dismiss after 3 s).
  // Also handle legacy playbook_change (in-SprkChat switch) to keep session state in sync.
  usePaneEvent("conversation", (event) => {
    if (event.type === "playbook-selected") {
      const { playbookId: newId, playbookName: newName } = event;
      if (!newId) return;

      // 1. Persist to AiSessionProvider (also writes to sessionStorage).
      setPlaybookId(newId);

      // 2. Advance shell stage — welcome → loading (Stage 1 → Stage 2).
      toLoading();

      // 3. Update the header strip with the selected playbook name.
      setActivePlaybookName(newName ?? newId);

      // 4. Show the confirmation toast, replacing any prior timer.
      if (toastTimerRef.current !== null) {
        clearTimeout(toastTimerRef.current);
      }
      setToastPlaybookName(newName ?? newId);
      toastTimerRef.current = setTimeout(() => {
        setToastPlaybookName(null);
        toastTimerRef.current = null;
      }, TOAST_DURATION_MS);
    } else if (event.type === "playbook_change") {
      // Legacy in-SprkChat playbook switch — keep session state in sync.
      if (event.playbookId) {
        setPlaybookId(event.playbookId);
        setActivePlaybookName(event.playbookName ?? event.playbookId);
      }
    }
  });

  // Cleanup toast timer on unmount to avoid setState-on-unmounted-component.
  React.useEffect(() => {
    return () => {
      if (toastTimerRef.current !== null) {
        clearTimeout(toastTimerRef.current);
      }
    };
  }, []);

  // ── Selection chip state (AIPU2-101) ────────────────────────────────────
  //
  // Populated when a workspace widget dispatches a selection_changed event
  // with non-null selectedText. Cleared when:
  //   - The workspace widget dispatches selection_changed with null selectedText
  //   - The user clicks the chip (text is injected into SprkChat)
  //   - The user clicks the dismiss button on the chip
  const [selectionChip, setSelectionChip] =
    React.useState<SelectionChipState | null>(null);

  // Subscribe to workspace channel — listen for selection_changed events from
  // workspace widgets. usePaneEvent is stable: the handler ref is kept current
  // internally without tearing down the subscription on each render.
  usePaneEvent("workspace", (event: WorkspacePaneEvent): void => {
    if (event.type !== "selection_changed") return;

    if (event.selectedText == null || event.selectedText.length === 0) {
      // Null or empty selectedText = selection cleared — hide the chip.
      setSelectionChip(null);
    } else {
      // Non-null selectedText = new selection — show the chip.
      setSelectionChip({
        selectedText: event.selectedText,
        contextLabel: event.contextLabel ?? event.widgetType ?? "Workspace",
      });
    }
  });

  // ── Welcome → Chat transition state ────────────────────────────────────
  //
  // pendingMessage: set when the user clicks a prompt button in WelcomePanel.
  // Triggers the switch from WelcomePanel to SprkChat with the message pre-set
  // as a predefined prompt. Cleared once onSessionCreated fires.
  const [pendingMessage, setPendingMessage] = React.useState<string | null>(null);

  // ── Refinement prompts (AIPU2-101) ──────────────────────────────────────
  //
  // Set when the user clicks the "Refine this?" chip. SprkChat renders these
  // as clickable suggestion chips above the input bar. Cleared when SprkChat
  // fires onSessionCreated (welcome flow complete) or when the user dismisses
  // the chip. Separate from pendingMessage to allow both to coexist.
  const [refinementPrompts, setRefinementPrompts] = React.useState<
    Array<{ key: string; label: string; prompt: string }>
  >([]);

  // ── SprkChat callbacks ──────────────────────────────────────────────────

  /**
   * onSessionCreated — fires when SprkChat creates a new chat session.
   *
   * Persists the session ID to AiSessionProvider (and sessionStorage).
   * Clears pendingMessage since the welcome flow is now complete.
   */
  const handleSessionCreated = React.useCallback(
    (session: IChatSession) => {
      if (session?.sessionId) {
        setChatSessionId(session.sessionId);
        setPendingMessage(null);
        // Clear refinement prompts once a session is established — the
        // suggestion chip in SprkChat is no longer needed.
        setRefinementPrompts([]);
      }
    },
    [setChatSessionId]
  );

  /**
   * onPlaybookChange — fires when the user switches playbooks in SprkChat.
   *
   * Persists the new playbook ID to AiSessionProvider (and sessionStorage).
   */
  const handlePlaybookChange = React.useCallback(
    (newPlaybookId: string) => {
      setPlaybookId(newPlaybookId);
    },
    [setPlaybookId]
  );

  // ── WelcomePanel callbacks ──────────────────────────────────────────────

  // ── Removed handlers (task 068, Bug 1 + UX-A) ───────────────────────────
  //
  // `handlePromptSelected` (welcome prompt buttons) and `handleResumeSession`
  // (Recent Conversations card click) were removed when WelcomePanel was
  // reduced to a heading-only shell. The chat input is now the cold-load
  // discoverability surface (FR-06) and session resume is reached via the
  // PaneHeader history icon → HistoryOverlay (task 022, FR-03 / OC-01).
  // HistoryOverlay's `onSelectSession` wires directly to `setChatSessionId`
  // (see render below), so no callback wrapper is required.

  // ── Selection chip handlers (AIPU2-101) ─────────────────────────────────

  /**
   * handleChipClick — injects the selected text as a refinement context block
   * into the SprkChat input, then dismisses the chip.
   *
   * The selected text is prepended to predefinedPrompts as a prompt that
   * contains both a descriptive label and the raw selection as the message
   * body. SprkChat will display it as a clickable suggestion chip and include
   * the text in the SSE request payload when sent.
   *
   * The chip is cleared immediately so the user is not confused by stale state
   * while SprkChat renders the new suggestion.
   */
  const handleChipClick = React.useCallback((): void => {
    if (selectionChip === null) return;

    const { selectedText, contextLabel } = selectionChip;
    const truncated = selectedText.length > 80
      ? `${selectedText.slice(0, 77)}…`
      : selectedText;

    // Build the refinement prompt — the label prefix helps the model understand
    // what the user wants. The full selectedText is the prompt body so the
    // backend SSE request includes the complete selection as additional context.
    const refinementPrompt = `Refine this from ${contextLabel}: "${selectedText}"`;

    setRefinementPrompts([
      {
        key: "refine-selection",
        label: `Refine: "${truncated}"`,
        prompt: refinementPrompt,
      },
    ]);

    // Dismiss the chip — the predefined prompt chip in SprkChat now carries
    // the selection context. Clearing here prevents double-chip confusion.
    setSelectionChip(null);
  }, [selectionChip]);

  /**
   * handleChipDismiss — hides the chip without injecting any text.
   * Called when the user clicks the X button on the chip.
   */
  const handleChipDismiss = React.useCallback(
    (e: React.MouseEvent): void => {
      e.stopPropagation();
      setSelectionChip(null);
    },
    []
  );

  /**
   * handleChangePlaybook — "Change playbook" button in the playbook header strip.
   *
   * Clears the active playbook selection and resets the shell to Stage 1
   * (welcome / gallery view) so the user can pick a different playbook.
   * Does NOT clear the chat session — the user may continue prior work after
   * selecting a new playbook.
   *
   * Broadcasts session_reset on the workspace bus channel so ShellStageManager's
   * bus subscriber resets its SessionState snapshot in addition to the direct
   * reset() call. Belt-and-braces: both paths must agree.
   */
  const handleChangePlaybook = React.useCallback(() => {
    setActivePlaybookName(null);
    setToastPlaybookName(null);
    if (toastTimerRef.current !== null) {
      clearTimeout(toastTimerRef.current);
      toastTimerRef.current = null;
    }
    // Direct reset — updates ShellStageContext immediately.
    reset();
    // Bus broadcast — ShellStageManager bus subscriber also resets SessionState.
    dispatch("workspace", { type: "session_reset" });
  }, [reset, dispatch]);

  // ── Auth loading guard ──────────────────────────────────────────────────
  //
  // Show a loading spinner while auth is resolving. This mirrors R1 ChatPanel.tsx
  // behaviour (spinner with "Initializing AI Chat..." label).
  // Spaarke Auth v2: gate purely on `isAuthenticated` (sync getter against the
  // provider's in-memory cache) — never on a snapshotted token string.
  if (!isAuthenticated) {
    return (
      <div className={styles.root}>
        <div className={styles.loadingContainer}>
          <Spinner size="medium" label="Initializing AI Chat..." labelPosition="below" />
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            Connecting to Dataverse...
          </Text>
        </div>
      </div>
    );
  }

  // ── Welcome vs SprkChat decision ────────────────────────────────────────
  //
  // Show WelcomePanel when ALL of the following are true:
  //   1. No active chat session (chatSessionId is null)
  //   2. No entity context (entityless / no-context launch from main nav)
  //   3. No pending message selected from WelcomePanel
  //   4. No playbook selected from gallery (playbookId undefined) [AIPU2-102]
  //
  // Condition 4 is new in R2: selecting a playbook from the gallery transitions
  // to SprkChat immediately so the agent is initialized before the first message.
  const showWelcomePanel =
    chatSessionId === null &&
    entityContext === null &&
    pendingMessage === null &&
    playbookId === undefined;

  // Build predefinedPrompts for SprkChat.
  //
  // Two sources can contribute:
  //   1. pendingMessage  — from the WelcomePanel prompt-button click
  //   2. refinementPrompts — from the "Refine this?" chip click (AIPU2-101)
  //
  // SprkChat shows these as clickable suggestion chips above the input bar.
  // The welcome prompt takes priority (index 0) so it is always visible first.
  // Refinement prompts follow. An undefined value means no chips (SprkChat
  // does not render the chip bar when predefinedPrompts is undefined or empty).
  const welcomePromptEntry = pendingMessage
    ? [{ key: "welcome-prompt", label: pendingMessage, prompt: pendingMessage }]
    : [];
  const allPredefinedPrompts = [...welcomePromptEntry, ...refinementPrompts];
  const predefinedPrompts = allPredefinedPrompts.length > 0 ? allPredefinedPrompts : undefined;

  // Build SprkChat hostContext from entityContext (same mapping as R1 ChatPanel.tsx).
  const hostContext = entityContext
    ? {
        entityType: entityContext.entityType as string,
        entityId: entityContext.entityId,
        workspaceType: "spaarke-ai",
      }
    : undefined;

  // ── Render ──────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Pane header — shared <PaneHeader> primitive (FR-02, task 021) ───── */}
      {/*
       * Replaces the legacy Chat/History tab-bar with the canonical pane-header
       * lifted to @spaarke/ui-components in Phase A task 010 (ADR-012). Icon
       * color is applied internally by PaneHeader via tokens.colorBrandForeground1
       * (ADR-021 — no hex / no rgba literals).
       *
       * task 022 (FR-03 / OC-01): rightSlot now hosts a HistoryRegular icon
       * button that toggles the <HistoryOverlay> side overlay (Claude-Code
       * style). Selecting a session in the overlay calls setChatSessionId,
       * which resumes the conversation via the existing AiSessionProvider
       * flow.
       */}
      <PaneHeader
        title="Assistant"
        icon={<ChatRegular />}
        onCollapse={paneCollapse ? handleHeaderCollapse : undefined}
        expanded={isAssistantExpanded}
        rightSlot={
          <Tooltip content="Show chat history" relationship="label" positioning="below">
            <Button
              appearance="subtle"
              icon={<HistoryRegular />}
              onClick={(e) => {
                // Task 094: prevent the header's collapse handler from
                // firing when clicking the History icon button.
                e.stopPropagation();
                handleOpenHistory();
              }}
              aria-label="Show chat history"
              aria-haspopup="dialog"
              aria-expanded={historyOpen}
            />
          </Tooltip>
        }
      />

      {/* ── History side-overlay (task 022, FR-03 / OC-01) ─────────────────── */}
      {/*
       * Claude-Code-style overlay that slides in from the trailing edge.
       * Hidden when historyOpen===false (OverlayDrawer manages its own
       * visibility). On session select, setChatSessionId resumes the
       * conversation in SprkChat and the overlay closes itself.
       */}
      <HistoryOverlay
        open={historyOpen}
        onClose={handleCloseHistory}
        onSelectSession={setChatSessionId}
        bffBaseUrl={bffBaseUrl}
        authenticatedFetch={authenticatedFetch}
      />

      {/* ── Playbook header strip (AIPU2-102) ──────────────────────────────── */}
      {/*
       * Shown once a playbook is active (selected from the gallery, Stage 2+).
       * Displays the playbook name and a "Change playbook" button that returns
       * to Stage 1 so the user can pick a different playbook from the gallery.
       */}
      {activePlaybookName !== null && (
        <div
          className={styles.playbookHeader}
          role="status"
          aria-label={`Active playbook: ${activePlaybookName}`}
        >
          <Text className={styles.playbookHeaderName} title={activePlaybookName}>
            {activePlaybookName}
          </Text>
          <Button
            appearance="subtle"
            size="small"
            icon={<ArrowResetRegular />}
            className={styles.changePlaybookButton}
            onClick={handleChangePlaybook}
            title="Select a different playbook"
            aria-label="Change playbook"
          >
            Change
          </Button>
        </div>
      )}

      {/* ── Active panel content ─────────────────────────────────────────── */}
      {/*
       * task 068 (Bug 1 — smoke remediation):
       *   The previous welcome ⇄ active ternary mounted WelcomePanel OR SprkChat
       *   but NEVER both, so the chat input was missing on cold load. SprkChat
       *   is now ALWAYS rendered; the welcome heading (WelcomePanel — reduced
       *   to a heading-only shell in task 068) sits ABOVE the chat region when
       *   `showWelcomePanel === true`. This satisfies FR-06 (input editable on
       *   cold load) and matches operator-validated behaviour from the smoke.
       *
       * task 021 (FR-02): the previous `activeView === "history"` branch was
       *   removed. History is no longer a tab — it becomes a side-overlay
       *   wired via the <PaneHeader> rightSlot in task 022 (OC-01).
       */}
      <div
        className={styles.content}
        role="region"
        aria-label="AI Chat"
      >
        {/* Welcome heading — visible only when no session, no entity, no
            pending message, and no playbook. Sits above SprkChat. */}
        {showWelcomePanel && <WelcomePanel />}

        {/* Chat region — ALWAYS rendered. Hosts the restore banners,
            "Refine this?" chip bar, and SprkChat itself. */}
        <div className={styles.chatWrapper}>
          {/* ── Stale entity warning (AIPU2-106) ── */}
          {restoreCtx?.hasStaleEntities && (
            <div className={styles.restoreStaleWarning} role="alert">
              Some referenced entities have changed since this session was saved.
              Results may differ from the original analysis.
            </div>
          )}

          {/* ── Conversation restore summary (AIPU2-106) ── */}
          {restoreCtx?.conversationSummary && (
            <div
              className={styles.restoreSummaryBlock}
              role="region"
              aria-label="Previous conversation summary"
              onClick={() => setSummaryExpanded((prev) => !prev)}
            >
              <div className={styles.restoreSummaryHeader}>
                {summaryExpanded ? "▼" : "▶"} Previous conversation
              </div>
              {summaryExpanded && (
                <div className={styles.restoreSummaryContent}>
                  {restoreCtx.conversationSummary}
                </div>
              )}
            </div>
          )}

          {/* ── "Refine this?" chip bar — visible only when workspace text is selected ── */}
          {selectionChip !== null && (
            <div className={styles.refinementChipBar} role="region" aria-label="Refinement suggestion">
              <Text className={styles.refinementChipLabel}>Refine this?</Text>
              <Tooltip
                content={selectionChip.selectedText}
                relationship="description"
                positioning="above-start"
              >
                <Tag
                  className={styles.refinementChipTag}
                  appearance="brand"
                  icon={<EditRegular />}
                  onClick={handleChipClick}
                  role="button"
                  aria-label={`Refine selected text from ${selectionChip.contextLabel}`}
                >
                  <span className={styles.refinementChipTagText}>
                    {selectionChip.selectedText.length > 40
                      ? `${selectionChip.selectedText.slice(0, 37)}…`
                      : selectionChip.selectedText}
                  </span>
                </Tag>
              </Tooltip>
              <Button
                appearance="subtle"
                size="small"
                icon={<DismissRegular />}
                className={styles.refinementChipDismiss}
                aria-label="Dismiss refinement suggestion"
                onClick={handleChipDismiss}
              />
            </div>
          )}

          {/* ── SprkChat — fills remaining height below the chip bar ── */}
          {/*
            Spaarke Auth v2 §H-4: pass `authenticatedFetch` (for one-shot BFF
            calls) and `getAccessToken` (escape hatch for SSE ReadableStream)
            instead of a snapshotted `accessToken: string`. Task 023 owns the
            SprkChat API change that consumes these props.
          */}
          <div className={mergeClasses(styles.sprkChatFlex)}>
            <SprkChat
              apiBaseUrl={bffBaseUrl}
              authenticatedFetch={authenticatedFetch}
              getAccessToken={getAccessToken}
              sessionId={chatSessionId ?? undefined}
              playbookId={playbookId}
              onSessionCreated={handleSessionCreated}
              onPlaybookChange={handlePlaybookChange}
              predefinedPrompts={predefinedPrompts}
              hostContext={hostContext}
              onPaneEvent={streaming.onPaneEvent ?? null}
            />
          </div>
        </div>
      </div>

      {/* ── Playbook confirmation toast (AIPU2-102, auto-dismissed after 3 s) ── */}
      {/*
       * Brief confirmation strip rendered below the content area after a playbook
       * is selected from the gallery. Auto-dismissed via a setTimeout in state logic.
       * Uses Fluent v9 status-success tokens — no hard-coded colors (ADR-021).
       */}
      {toastPlaybookName !== null && (
        <div
          className={styles.toastStrip}
          role="status"
          aria-live="polite"
          aria-label={`Playbook switched to ${toastPlaybookName}`}
        >
          <CheckmarkCircleRegular className={styles.toastIcon} />
          <Text className={styles.toastText}>
            Switched to <strong>{toastPlaybookName}</strong>
          </Text>
        </div>
      )}
    </div>
  );
}
