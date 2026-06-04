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
} from "@fluentui/react-icons";
// PaneHeader is the canonical pane-header primitive lifted into the shared
// library in Phase A task 010 (ADR-012). It owns the icon brand-color treatment
// and the right-slot container — see PaneHeader.tsx in @spaarke/ui-components.
import { PaneHeader, SprkChat } from "@spaarke/ui-components";
import { useAiSession, usePaneEvent, useDispatchPaneEvent } from "@spaarke/ai-widgets";
import type { WorkspacePaneEvent } from "@spaarke/ai-widgets";
// R4 task 042 (W-4): symbolic widget type ID + payload shape for the
// Assistant-pane PDF-upload → DocumentViewer demo. We import the constant
// (NOT the literal "document-viewer") so a rename in the registration file
// surfaces at compile time.
import {
  DOCUMENT_VIEWER_WIDGET_TYPE,
  type DocumentViewerWidgetData,
} from "@spaarke/ai-widgets";
import type { IChatSession } from "@spaarke/ai-context";
import { WelcomePanel } from "../WelcomePanel";
import {
  useShellStage,
  useRestoreContext,
  usePaneCollapseContext,
} from "../shell/ThreePaneShell";
import { HistoryMenu } from "./HistoryOverlay";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

// NOTE (task 021, FR-02): The legacy `LeftPaneView` ("chat" | "history") tab
// model was removed when the Chat/History tab buttons were replaced by the
// shared <PaneHeader>. History becomes a side-overlay (OC-01) wired in task 022.

// ---------------------------------------------------------------------------
// /summarize tri-mode intent routing (R5 task 019 / D2-10)
// ---------------------------------------------------------------------------
//
// R5 turns the `/summarize` slash command into a tri-mode dispatcher per FR-03.
// The routing decision is a PURE function of the input message + the host
// context the ConversationPane already knows about:
//   1. Has the active chat session received uploaded files this session?
//   2. Does the host have an active workspace document context (R3 wizard)?
//
// The helper returns a stable, testable decision shape that the dispatcher
// site consumes. The actual orchestrator wiring for branches (a) and (b) is
// owned by sibling tasks 014 (POST /api/ai/chat/sessions/{id}/summarize
// endpoint), 015 (InvokeSummarizePlaybookTool agent-tool), and 020 (chat-pane
// orchestration UX — the dispatch wiring site that consumes the routing
// decision). Branch (c) is owned end-to-end by THIS task — the interjection
// text is rendered as an Assistant message via the existing predefinedPrompts
// suggestion surface (no SprkChat API change required).
//
// Spec wording (NFR-12 + plan D2-10):
//   - description: "Summarize uploaded files or the active document"
//   - interjection: "Upload the file(s) you'd like me to summarize"
// Both strings are spec-driven; do NOT change them without updating spec.md.

/**
 * Trigger prefix for the /summarize slash command. Lowercased; the slash
 * command menu writes the canonical trigger verbatim into the textarea so
 * a case-sensitive prefix match is safe.
 */
export const SUMMARIZE_SLASH_PREFIX = '/summarize';

/**
 * Deterministic Assistant interjection emitted on branch (c) — the FR-03
 * prompt-first ordering. Rendered locally as an Assistant message; NO
 * playbook invocation, NO BFF round-trip.
 */
export const SUMMARIZE_PROMPT_FIRST_INTERJECTION =
  "Upload the file(s) you'd like me to summarize";

/**
 * Discriminated routing decision returned by {@link routeSummarizeIntent}.
 *
 * - `session-files`  → branch (a). The active session has uploaded files. The
 *   dispatcher invokes the session-files Summarize path: either the agent-
 *   tool path (LLM tool-call via InvokeSummarizePlaybookTool, task 015) for
 *   natural-language flows, or the direct endpoint path (POST /api/ai/chat/
 *   sessions/{id}/summarize, task 014) for explicit slash dispatch.
 *
 * - `active-document` → branch (b). No uploaded files but the host has an
 *   active workspace document context. Falls through to the existing R3
 *   SummarizeFilesDialog wizard flow (back-compat). The dispatcher opens the
 *   wizard the same way it does today; this routing helper just signals the
 *   decision.
 *
 * - `prompt-first` → branch (c). Neither uploaded files nor active workspace
 *   document. Renders the deterministic Assistant interjection inline in the
 *   chat thread; NO playbook invocation. Owned end-to-end by task 019 via
 *   the `predefinedPrompts` surface.
 *
 * - `not-summarize` → the message is not a /summarize invocation; the
 *   dispatcher MUST pass the message through unchanged to the default
 *   SprkChat send funnel. This lets the helper sit on the hot path without
 *   forcing every send through tri-mode logic.
 */
export type SummarizeRouteDecision =
  | { kind: 'session-files'; messageText: string }
  | { kind: 'active-document'; messageText: string }
  | { kind: 'prompt-first'; messageText: string; interjection: string }
  | { kind: 'not-summarize'; messageText: string };

/**
 * Minimal host-context inputs for {@link routeSummarizeIntent}.
 *
 * Decoupled from `IChatSession` (frontend session shape) and `entityContext`
 * (host workspace context) so the helper is trivially testable with plain
 * objects. The dispatcher binds these from `useAiSession()` + SprkChat's
 * internal attachment state (the task-004 bridge analog — see notes/
 * task-019-slash-command-evidence.md for the bridge decision).
 */
export interface SummarizeIntentInputs {
  /**
   * Count of files uploaded into THIS chat session. Maps to
   * `ChatSession.UploadedFiles.length` on the BFF model (task 004). Until
   * the frontend AiSessionProvider surfaces that property end-to-end (task
   * 020 territory), the dispatcher passes the closest analog — the count of
   * `chatAttachments` chips in SprkChat's local in-memory state. Both yield
   * the same routing decision for the operator-visible flow.
   */
  uploadedFileCount: number;

  /**
   * Whether the host has an active workspace document context. True when
   * SpaarkeAi's host context carries an entity/document the existing R3
   * wizard would consume. The dispatcher binds this from `entityContext`
   * + any `documentId` surfaced through SprkChat props.
   */
  hasActiveWorkspaceDocument: boolean;
}

/**
 * Pure tri-mode routing decision for `/summarize` per FR-03.
 *
 * Inputs are positional and side-effect-free; the helper performs no IO and
 * no state mutation. The caller is responsible for executing the chosen
 * branch.
 *
 * Test contract: this function MUST be deterministic and total — every
 * combination of inputs yields exactly one of the four decision kinds.
 */
export function routeSummarizeIntent(
  messageText: string,
  inputs: SummarizeIntentInputs
): SummarizeRouteDecision {
  // Normalize once; the slash command menu writes the trigger in lower case
  // and the user CAN type it manually, so accept the canonical lowercase
  // form OR a case-insensitive variant ("/Summarize", "/SUMMARIZE"). The
  // textarea is whitespace-trimmed at the dispatcher edge; we re-trim here
  // so the helper is robust on its own.
  const trimmed = messageText.trim();
  const isSummarize =
    trimmed.length >= SUMMARIZE_SLASH_PREFIX.length &&
    trimmed.slice(0, SUMMARIZE_SLASH_PREFIX.length).toLowerCase() ===
      SUMMARIZE_SLASH_PREFIX;

  if (!isSummarize) {
    return { kind: 'not-summarize', messageText };
  }

  // Branch (a): uploaded files take precedence — the user expressed intent
  // by uploading files into the chat session before invoking the command.
  if (inputs.uploadedFileCount > 0) {
    return { kind: 'session-files', messageText };
  }

  // Branch (b): no uploaded files but an active workspace document is the
  // R3 wizard's natural input. Fall through to the existing flow unchanged.
  if (inputs.hasActiveWorkspaceDocument) {
    return { kind: 'active-document', messageText };
  }

  // Branch (c) — FR-03 prompt-first path: no uploads, no document; emit the
  // deterministic interjection so the user knows what to do next.
  return {
    kind: 'prompt-first',
    messageText,
    interjection: SUMMARIZE_PROMPT_FIRST_INTERJECTION,
  };
}

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

  // ── History dropdown (task 097 — was OverlayDrawer in task 022) ─────────
  //
  // Operator smoke 2026-05-22 flagged the icon-only History button + slide-in
  // OverlayDrawer as inconsistent with Workspace + Context panes which use a
  // Fluent v9 `<Menu>` dropdown in the PaneHeader rightSlot. Task 097 replaces
  // the overlay with `<HistoryMenu>` — a self-contained Menu+MenuPopover that
  // renders the session list inline. The Menu manages its own open/close
  // state so there's no `historyOpen` boolean here anymore. Selecting a
  // session still calls setChatSessionId, which resumes the conversation via
  // the existing AiSessionProvider flow.

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

  // ── /summarize tri-mode dispatcher (R5 task 019 / D2-10) ────────────────
  //
  // dispatchSummarizeIntent is the scaffolding call site for the tri-mode
  // routing helper {@link routeSummarizeIntent} declared at module scope. It
  // is wired up here so the routing decision is available the moment the
  // host needs it; the SprkChat-side interception (the actual call site that
  // observes "/summarize" being sent) is owned by sibling task 020 (D2-11
  // chat-pane orchestration UX) since that task already touches the chat
  // send flow.
  //
  // Cross-task coordination decisions (see notes/task-019-slash-command-evidence.md):
  //   - Branch (a) (`session-files`): downstream wiring binds to the agent-
  //     tool path (task 015 — InvokeSummarizePlaybookTool, LLM tool-call) OR
  //     the direct endpoint path (task 014 — POST /api/ai/chat/sessions/
  //     {sessionId}/summarize). For explicit slash dispatch we prefer the
  //     direct endpoint (no LLM round-trip needed). Neither endpoint exists
  //     yet at the time of this scaffolding — the call site is a TODO
  //     marker that compiles and surfaces the gap as a downstream concern.
  //   - Branch (b) (`active-document`): falls through to the EXISTING R3
  //     SummarizeFilesDialog wizard via the host's existing wizard-opening
  //     mechanism (back-compat). ConversationPane does NOT host the wizard
  //     itself today (LegalWorkspace does), so this branch in the SpaarkeAi
  //     shell currently has no in-tree consumer — task 020 wires the
  //     wizard dispatch into the shell.
  //   - Branch (c) (`prompt-first`): owned end-to-end by task 019. The
  //     interjection is surfaced via the existing predefinedPrompts
  //     suggestion surface — no SprkChat API change required. The
  //     dispatcher pushes the interjection into pendingSummarizeInterjection
  //     state; the predefinedPrompts builder below merges it into the chips
  //     SprkChat already renders.
  const [pendingSummarizeInterjection, setPendingSummarizeInterjection] =
    React.useState<string | null>(null);

  /**
   * dispatchSummarizeIntent — invoke the tri-mode router and apply the
   * chosen branch.
   *
   * Stable across renders so a downstream wiring site (task 020) can pass
   * this as a prop or callback into SprkChat without re-subscribing.
   *
   * Returns `true` when the helper handled the message (branches a/b/c),
   * `false` when the message is not a /summarize invocation (the caller
   * should let SprkChat handle it normally).
   */
  const dispatchSummarizeIntent = React.useCallback(
    (messageText: string): boolean => {
      // The task-004 bridge: ChatSession.UploadedFiles is not yet surfaced
      // to the frontend AiSessionProvider at the time of this scaffolding
      // (task 004 ships BFF-side only; the frontend bridge is owned by
      // task 020). We default to 0 here so the helper falls through to
      // branch (b) or (c) until task 020 wires the real source. Documented
      // in notes/task-019-slash-command-evidence.md.
      // TODO(r5/task-020): replace `0` with the real uploaded-file count
      // from useAiSession() once the AiSessionProvider exposes it.
      const uploadedFileCount = 0;

      // Branch (b) gate: an active workspace document is one the host can
      // pass to the existing R3 wizard. In the SpaarkeAi shell that
      // corresponds to a non-null entityContext (matter/project/invoice
      // record context) OR an explicit documentId in the host context.
      const hasActiveWorkspaceDocument = entityContext !== null;

      const decision = routeSummarizeIntent(messageText, {
        uploadedFileCount,
        hasActiveWorkspaceDocument,
      });

      switch (decision.kind) {
        case 'not-summarize':
          return false;

        case 'session-files':
          // TODO(r5/tasks-014-015-020): dispatch to the session-files
          // Summarize path. The agent-tool path (task 015) and direct
          // endpoint path (task 014) both converge on
          // SessionSummarizeOrchestrator (task 012). Until those land,
          // we fall through to the default SprkChat send so the chat
          // surface remains functional — the slash command still produces
          // a chat response via the default playbook routing. This is
          // an intentional graceful-degradation stub; the routing
          // decision is still computed and observable for diagnostics.
          return false;

        case 'active-document':
          // TODO(r5/task-020): open the existing SummarizeFilesDialog wizard
          // through the host. In the SpaarkeAi shell the wizard host is not
          // yet wired (LegalWorkspace owns it today); task 020's chat-pane
          // orchestration UX work decides whether to mount the wizard in
          // the shell or dispatch to a different surface. For now the
          // default SprkChat send handles the message — back-compat is
          // preserved because LegalWorkspace consumers continue to invoke
          // the wizard directly (NOT via ConversationPane), so this
          // ConversationPane-side fallthrough does NOT affect them.
          return false;

        case 'prompt-first':
          // Branch (c) is owned by THIS task: surface the deterministic
          // interjection as an Assistant message via the predefinedPrompts
          // suggestion surface. The chip carries the interjection text so
          // the user sees "Upload the file(s) you'd like me to summarize"
          // immediately. No playbook invocation, no BFF round-trip.
          setPendingSummarizeInterjection(decision.interjection);
          return true;
      }
    },
    [entityContext]
  );

  // Mark `dispatchSummarizeIntent` as referenced so the TypeScript no-unused-
  // locals rule does not flag the scaffolding. Downstream wiring (task 020)
  // will consume it from the same module reference; until then it sits as
  // an intentional stable API surface for the chat-pane orchestration site.
  void dispatchSummarizeIntent;

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
        // R5 task 019 / D2-10: clear the FR-03 prompt-first interjection
        // once the user has acted on it (sending any message creates the
        // session). The chip should not linger across turns.
        setPendingSummarizeInterjection(null);
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

  /**
   * onAttachmentReady — R4 task 042 (W-4) Assistant → Workspace mount source.
   *
   * Demo scenario per OC-R4-07 (2026-05-26 operator confirmation): when the
   * user attaches a file in the chat input and SprkChat's
   * `useChatFileAttachment` hook finishes client-side text extraction, this
   * callback fires once per ready file. We dispatch a typed `widget_load`
   * event on the `workspace` PaneEventBus channel, which the WorkspacePane
   * (subscribed via usePaneEvent) resolves through WorkspaceWidgetRegistry
   * and mounts as a new tab.
   *
   * Per Risk R-7 in plan.original.md §8: dispatch + ONE viewer widget only;
   * broader coverage (image preview, RecordViewer, etc.) is deferred to a
   * follow-up. The PDF case is the primary path; non-PDF MIME types still
   * open as workspace tabs but the DocumentViewerWidget falls back to the
   * extracted text preview (no PDF binary render — see widget docstring).
   *
   * Per ADR-030: the payload is typed end-to-end. `widgetData` is cast to
   * `DocumentViewerWidgetData` at the dispatch boundary (NOT `any`). The
   * payload shape is reusable for W-5 (task 043 Context-pane dispatch).
   *
   * Per ADR-028: no auth context flows through this callback — text was
   * extracted client-side before this point. NO BFF call is made here.
   */
  const handleAttachmentReady = React.useCallback(
    (attachment: { filename: string; contentType: string; textContent: string }) => {
      const widgetData: DocumentViewerWidgetData = {
        filename: attachment.filename,
        contentType: attachment.contentType,
        textContent: attachment.textContent,
      };
      dispatch("workspace", {
        type: "widget_load",
        widgetType: DOCUMENT_VIEWER_WIDGET_TYPE,
        widgetData,
        // Use the filename as the tab label — operator-visible behaviour per
        // OC-R4-07. WorkspacePane.tsx prefers event.displayName over the
        // registry metadata's generic "Document Viewer" label.
        displayName: attachment.filename,
      });
    },
    [dispatch]
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
  // Three sources can contribute:
  //   1. pendingMessage  — from the WelcomePanel prompt-button click
  //   2. refinementPrompts — from the "Refine this?" chip click (AIPU2-101)
  //   3. pendingSummarizeInterjection — from the /summarize tri-mode router's
  //      branch (c) FR-03 prompt-first path (R5 task 019 / D2-10). Surfaces
  //      "Upload the file(s) you'd like me to summarize" as a clickable chip
  //      above the input bar so the user knows what to do next. No playbook
  //      invocation; pure chat-layer interjection.
  //
  // SprkChat shows these as clickable suggestion chips above the input bar.
  // The welcome prompt takes priority (index 0) so it is always visible first.
  // The Summarize interjection follows; refinement prompts last. An undefined
  // value means no chips (SprkChat does not render the chip bar when
  // predefinedPrompts is undefined or empty).
  const welcomePromptEntry = pendingMessage
    ? [{ key: "welcome-prompt", label: pendingMessage, prompt: pendingMessage }]
    : [];
  const summarizeInterjectionEntry = pendingSummarizeInterjection
    ? [
        {
          key: "summarize-interjection",
          label: pendingSummarizeInterjection,
          prompt: pendingSummarizeInterjection,
        },
      ]
    : [];
  const allPredefinedPrompts = [
    ...welcomePromptEntry,
    ...summarizeInterjectionEntry,
    ...refinementPrompts,
  ];
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
       * task 097 (operator smoke 2026-05-22): rightSlot now hosts <HistoryMenu>
       * — a Fluent v9 dropdown matching the Workspace ("Workspace ▾") and
       * Context ("Tools ▾") pane menus. Replaces the prior HistoryRegular
       * icon-only button + OverlayDrawer (task 022) which read as MDA-style
       * and broke pane-trigger consistency. The session list renders inline
       * in the MenuPopover; selecting a session calls setChatSessionId, which
       * resumes the conversation via the existing AiSessionProvider flow.
       */}
      <PaneHeader
        title="Assistant"
        icon={<ChatRegular />}
        onCollapse={paneCollapse ? handleHeaderCollapse : undefined}
        expanded={isAssistantExpanded}
        rightSlot={
          <HistoryMenu
            onSelectSession={setChatSessionId}
            bffBaseUrl={bffBaseUrl}
            authenticatedFetch={authenticatedFetch}
          />
        }
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
              onAttachmentReady={handleAttachmentReady}
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
