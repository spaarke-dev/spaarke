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
import type { AttachmentChip, ChatAttachment, IChatMessage } from "@spaarke/ui-components";
import { useAiSession, usePaneEvent, useDispatchPaneEvent } from "@spaarke/ai-widgets";
// R6 Pillar 8 (task 081): HardSlashExecutor needs the full bus instance to
// dispatch on multiple channels. `usePaneEventBus` is promoted to the public
// events barrel for this seam (preferred public hooks remain
// useDispatchPaneEvent / usePaneEvent for single-channel components).
import { usePaneEventBus } from "@spaarke/ai-widgets/events";
import type { WorkspacePaneEvent, ContextPaneEvent } from "@spaarke/ai-widgets";
// R4 task 042 (W-4): the DocumentViewerWidget dispatch from this file was
// disabled in R5 SC-18 cycle 6 (see handleAttachmentReady). Import will be
// reinstated when R5 task 022 upgrades the widget.
import type { IChatSession } from "@spaarke/ai-context";
import { WelcomePanel } from "../WelcomePanel";
import {
  useShellStage,
  useRestoreContext,
  usePaneCollapseContext,
} from "../shell/ThreePaneShell";
import { HistoryMenu } from "./HistoryOverlay";
// R5 task 036 / P2-CLOSEOUT-05: deterministic intent matching + Summarize
// promotion. The matcher is a pure module; the executor handles atomic
// /documents promotion + /summarize SSE streaming + PaneEventBus bridging.
// See notes/task-036-design-2026-06-05.md for design rationale.
import { matchIntent } from "./intentMatcher";
// R6 closeout (Pillar 8 / task 097): /new-session needs to POST /api/ai/chat/sessions
// and return the new session id so HardSlashExecutor.execNewSession can complete.
import { buildBffApiUrl } from "@spaarke/auth";
// R6 task 080 / D-D-01 (Pillar 8 foundation): CommandRouter parser is wired
// into the send-message boundary so downstream Phase D tasks (081 hard-slash
// executor, 082 soft-slash agent routing, 083 reference resolver) can fan out.
// This wire-up is INTENT-CAPTURE ONLY — no behavior branch lands here per the
// POML acceptance criteria. NFR-11 binding: natural-language input still falls
// through to the existing CapabilityRouter path unchanged (parse() returns
// command:null for any non-slash input).
import { parse as parseCommandIntent } from "./CommandRouter";
// R6 Phase D Wave D-G1 — Pillar 8 Command Router wired via the new
// onDecorateOutboundBody seam in SprkChat (ADR-012 context-agnostic prop).
// Hard slashes (081) dispatch client-side + cancel the BFF send by returning null.
// Soft slashes (082) decorate the outbound body with `intentHint` for
// CapabilityRouter Layer 0.5 strong-intent routing. (Wire field renamed
// `commandIntent` → `intentHint` per FR-07 / task 022, 2026-06-22.)
// References (083) resolve `#scope` / `@<entity>` / `#<filename>` at parse time
// and attach `resolvedReferences` to the body. NFR-11 binding: natural-language
// input (no slash, no references) passes through unchanged.
import {
  executeHardSlash,
  defaultTelemetrySink,
  defaultDownloadBlob,
  type ExecutorContext as HardSlashExecutorContext,
  type ConversationMessage as HardSlashConversationMessage,
} from "./HardSlashExecutor";
import { CommandHelpPanel } from "./CommandHelpPanel";
import { HelpAffordance } from "./HelpAffordance";
import { decorateBody as decorateSoftSlashBody } from "./SoftSlashRouter";
import ReferenceResolver, {
  createScopeFetch,
  createFileLookupFromSessionMap,
  type ResolverContext,
} from "./ReferenceResolver";
import {
  executeSummarizeIntent,
  type HeldFile,
} from "./executeSummarizeIntent";

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
// Inline confirmation + interjection helpers (R5 task 020 / D2-11)
// ---------------------------------------------------------------------------
//
// All three helpers below are PURE functions exported at module scope so the
// chat-pane orchestration UX is trivially testable with plain inputs (no React
// testing infrastructure required). The ConversationPane component composes
// them with effects + refs to produce the operator-visible behaviour.

/**
 * Maximum number of filenames listed inline in the file-confirmation message.
 * Names beyond this cap collapse into "...and N more" suffix to avoid blowing
 * out the chat message width on long file lists.
 */
export const FILE_CONFIRMATION_MAX_NAMES = 3;

/**
 * Build the deterministic inline file-confirmation message body emitted when
 * one or more files transition to `status === 'ready'`. The string format is
 * spec-driven (R5 task 020 POML goal §4 example: "I have your 3 files: a.pdf,
 * b.docx, c.md"):
 *
 *   1 file  → "I have your file: a.pdf"
 *   2+ files → "I have your N files: a.pdf, b.docx, c.md"
 *   >FILE_CONFIRMATION_MAX_NAMES files → "...and N more" suffix
 *
 * Pure / total: every non-empty filename list yields exactly one message body.
 * Returns `null` when the filenames array is empty so callers can short-circuit.
 */
export function buildFileConfirmationMessage(filenames: readonly string[]): string | null {
  if (filenames.length === 0) return null;
  if (filenames.length === 1) {
    return `I have your file: ${filenames[0]}`;
  }
  const visible = filenames.slice(0, FILE_CONFIRMATION_MAX_NAMES);
  const remaining = filenames.length - visible.length;
  const list = visible.join(", ");
  if (remaining > 0) {
    return `I have your ${filenames.length} files: ${list}, and ${remaining} more`;
  }
  return `I have your ${filenames.length} files: ${list}`;
}

/**
 * Build the deterministic Assistant interjection emitted on a multi-file
 * combined-summary turn (R5 task 020 POML goal §5; R5 FR-03 prompt-first
 * semantics extended to the session-files branch). The string format is
 * spec-driven (POML example: "I'll combine all 3 files into a single
 * summary."):
 *
 *   N=2 → "I'll combine all 2 files into a single summary."
 *   N>=3 → "I'll combine all 3 files into a single summary."
 *
 * The helper does NOT fire for N=1 — single-file Summarize uses the per-file
 * affordance (R5 task 021) and does NOT emit a combined-summary interjection.
 * Returns `null` when fileCount &lt; 2 so callers can short-circuit.
 *
 * Pure / total / deterministic across renders so a `useRef`-based once-per-turn
 * guard at the call site produces exactly-once semantics.
 */
export function buildMultiFileSummarizeInterjection(fileCount: number): string | null {
  if (fileCount < 2) return null;
  return `I'll combine all ${fileCount} files into a single summary.`;
}

/**
 * Wrap a plain text body in an `IChatMessage` shape suitable for the
 * `injectLocalMessage` prop on SprkChat (R5 task 020 / D2-11).
 *
 * Convention:
 *   - `role: 'Assistant'` — renders in the assistant message slot with the
 *     existing styles + a11y treatment.
 *   - `metadata.responseType: 'markdown'` — plain-text rendering (no card).
 *   - `timestamp` — current ISO timestamp (matches the streamed-turn shape).
 *
 * Per R5 spec FR-03 + ADR-012 these messages are CLIENT-RENDERED only — they
 * are NOT persisted server-side as model-generated turns. The host emits them
 * deterministically; the BFF chat history does NOT contain them.
 */
export function makeLocalAssistantMessage(content: string): IChatMessage {
  return {
    role: "Assistant",
    content,
    timestamp: new Date().toISOString(),
    metadata: { responseType: "markdown" },
  };
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
    // R6 task 085 / D-D-06: anchor for the absolutely-positioned
    // HelpAffordance (Pillar 8 `/help` discovery button). Keeps the
    // button in the chat region without disturbing SprkChat's internal
    // input bar layout (NFR-11: additive UX; existing behavior unchanged).
    position: "relative",
  },

  // ── R5 task 020 / D2-11: "N files attached" indicator strip ──────────────
  //
  // Persistent indicator rendered ABOVE the SprkChat chip strip (which sits
  // inside SprkChat's input zone). Visible whenever the session has one or
  // more uploaded files (chip count > 0). Fluent v9 semantic tokens only —
  // no hard-coded colors (ADR-021). Hidden via conditional render when
  // `uploadedFileCount === 0`.
  filesAttachedIndicator: {
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
  filesAttachedIndicatorText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
  },
  filesAttachedIndicatorHint: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
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
  // R6 closeout (Pillar 8 / task 097c): track the currently-focused workspace
  // tab id via PaneEventBus `tab_change` events. The HardSlashExecutor's
  // `/pin` command reads this via `getFocusedTabId` to know which tab to pin.
  // A ref (not state) avoids re-rendering ConversationPane on every tab focus
  // change — only the synchronous callback consumes the value.
  const focusedTabIdRef = React.useRef<string | null>(null);

  // R6 task 097b / TIER-C surface completion — track latest SprkChat messages
  // via ref so `/export` (and future affordances) can read conversation history.
  // Ref pattern matches focusedTabIdRef above — avoids re-rendering on every
  // streamed token; only the synchronous getConversationHistory callback reads it.
  const messagesRef = React.useRef<IChatMessage[]>([]);

  usePaneEvent("workspace", (event: WorkspacePaneEvent): void => {
    if (event.type === "tab_change") {
      focusedTabIdRef.current = event.tabId ?? null;
      return;
    }

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

  // ── R5 task 020 / D2-11: chat-pane orchestration UX state ──────────────
  //
  // The chat-pane orchestration UX builds on the existing
  // `useChatFileAttachment` chip lifecycle (in SprkChat) by adding:
  //   - A persistent "N files attached" indicator (count derived here from
  //     the SprkChat `onAttachmentsChanged` callback).
  //   - A per-file remove cascade (via `onAttachmentRemoved` callback) that
  //     calls the cleanup pathway (manifest + AI Search index).
  //   - Debounced inline file-confirmation messages on ready transitions.
  //   - A deterministic multi-file combined-summary interjection emitted
  //     exactly once per multi-file Summarize turn (via `onBeforeSendMessage`).
  //   - A `context.files_staged` PaneEventBus dispatch on ready transitions.
  //
  // The chip state mirror is `AttachmentChip[]`. We DON'T duplicate the chip
  // lifecycle here — SprkChat still owns it via `useChatFileAttachment`. We
  // just maintain a local read-only copy keyed off the `onAttachmentsChanged`
  // callback so the indicator + dispatchSummarizeIntent + ready-transition
  // tracking can react to chip lifecycle events.
  const [attachmentChips, setAttachmentChips] = React.useState<AttachmentChip[]>([]);

  // Inline-confirmation injection state. SprkChat watches `pendingInjection`
  // and appends to its thread on null→non-null transition. `onLocalMessageInjected`
  // clears the prop back to null so re-renders do not re-inject.
  const [pendingInjection, setPendingInjection] = React.useState<IChatMessage | null>(null);

  // R6 task 081 / Pillar 8 — CommandHelpPanel open state. `/help` flips this on;
  // the panel's onClose flips it off. Lives alongside `pendingInjection` because
  // both are local UI affordances dispatched by HardSlashExecutor.
  const [helpPanelOpen, setHelpPanelOpen] = React.useState<boolean>(false);

  // R6 hotfix 2026-06-19 (UAT) — SprkChat remount key. `/clear` increments this,
  // which forces SprkChat to unmount + remount and wipes its internal message
  // list. This was previously a TODO stub on `clearLocalConversation` that
  // shipped uncovered; the BFF DELETE session call still fires (it clears the
  // server-side cache) but the UI message list was not being cleared, producing
  // the "conversation didn't clear after /clear" UAT bug. The remount pattern
  // is pragmatic (no new SprkChat API surface) and surgical to /clear.
  const [sprkChatRemountKey, setSprkChatRemountKey] = React.useState<number>(0);

  // chat-routing-redesign-r1 task 117b — track the user's most recent outbound
  // message text so the playbook_options click handler can forward it to the
  // dispatcher endpoint as `originalMessage`. Captured in `handleBeforeSendMessage`
  // (synchronous BEFORE-send hook). Ref (not state) — never rendered.
  // ADR-015: never logged.
  const lastSentMessageRef = React.useRef<string>('');

  // ── R5 task 036 / P2-CLOSEOUT-05: held-files + promoted-chip tracking ─────
  //
  // `heldFilesRef` maps chip id → original `File` for chips that have reached
  // `status === 'ready'`. The map is populated in `handleAttachmentReady`.
  //
  // CROSS-PACKAGE GAP (flagged in notes/task-036-implementation-notes.md):
  //  SprkChat's `onAttachmentReady` callback today delivers
  //  `{ filename, contentType, textContent }` — NOT the original `File`. The
  //  shared lib's `useChatFileAttachment` hook consumes the File during
  //  extraction and does NOT retain it. For atomic promotion of PDF/DOCX
  //  binaries via `POST /documents` (multipart binary required) the shared
  //  lib must forward the File reference too. Until that lands, we keep the
  //  HeldFile capture sites here but the ref will be empty — the promotion
  //  step will throw a descriptive error informing the user to retry via the
  //  `[action:upload]` prompt-button flow (Path B). For TXT/MD files we
  //  reconstruct a File from `textContent` as a best-effort fallback so the
  //  end-to-end flow can be exercised in dev.
  const heldFilesRef = React.useRef<Map<string, File>>(new Map());

  // Chip ids that have been successfully promoted via `executeSummarizeIntent`.
  // The render reads this to flip the per-chip status badge "Held" → "Indexed".
  const [promotedChipIds, setPromotedChipIds] = React.useState<ReadonlySet<string>>(
    () => new Set<string>()
  );

  // Per-id tracking of which chip IDs have already triggered an inline
  // file-confirmation message — guarantees one consolidated confirmation
  // per ready batch (debounced) and prevents re-emission on re-render.
  const confirmedReadyIdsRef = React.useRef<Set<string>>(new Set());

  // Debounce timer for ready-batch coalescing — files that arrive within
  // ~250ms of each other (the operator-visible "I uploaded a batch" gesture)
  // produce a single consolidated "I have your N files: ..." message rather
  // than N separate single-file confirmations.
  const readyConfirmationTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const pendingConfirmFilenamesRef = React.useRef<string[]>([]);
  const READY_CONFIRMATION_DEBOUNCE_MS = 250;

  // Per-id tracking of which chip IDs have already been dispatched on the
  // `context.files_staged` PaneEventBus channel — prevents re-dispatch on
  // chip status mutations unrelated to the ready transition.
  const dispatchedReadyIdsRef = React.useRef<Set<string>>(new Set());

  // Per-turn tracking of multi-file combined-summary interjection emission.
  // Keyed by a stable hash of the chip IDs + message text so retries /
  // stream-resumption of the SAME turn do not re-emit the interjection. The
  // ref is reset on session change (handleSessionCreated below).
  const emittedSummarizeInterjectionKeysRef = React.useRef<Set<string>>(new Set());

  // FR-03 prompt-first interjection state (R5 task 019). Surfaced via the
  // existing predefinedPrompts chip surface when no files are uploaded and
  // no active document is in scope. Cleared on session creation.
  const [pendingSummarizeInterjection, setPendingSummarizeInterjection] =
    React.useState<string | null>(null);

  // Derived: count of chips that are present (extracting + ready + error).
  // Mirrors the SprkChat chip strip's visible count. NOT just ready chips —
  // the indicator surfaces "files attached" intent immediately so the user
  // knows the session has them before extraction completes.
  const uploadedFileCount = attachmentChips.length;

  // Cleanup the debounce timer + interjection ref set on unmount.
  React.useEffect(() => {
    return () => {
      if (readyConfirmationTimerRef.current !== null) {
        clearTimeout(readyConfirmationTimerRef.current);
      }
    };
  }, []);

  /**
   * onAttachmentsChanged — SprkChat fires this on every chip lifecycle change
   * (add, remove, status transition). The host (this component) mirrors the
   * chip array locally so the indicator + tri-mode routing input
   * + ready-transition tracking can react.
   */
  const handleAttachmentsChanged = React.useCallback(
    (chips: AttachmentChip[]) => {
      setAttachmentChips(chips);

      // Detect ready transitions for inline confirmation + PaneEventBus dispatch.
      // We can't observe transitions purely from the chip array (a chip is
      // 'ready' from this callback's perspective on its FIRST ready render);
      // the per-id ref sets handle the once-per-id semantics.
      const readyChipsThisTick: AttachmentChip[] = [];
      for (const chip of chips) {
        if (chip.status !== "ready") continue;
        if (dispatchedReadyIdsRef.current.has(chip.id)) continue;
        readyChipsThisTick.push(chip);
        dispatchedReadyIdsRef.current.add(chip.id);
      }

      // Prune dispatched IDs for chips that have been removed so re-add re-fires.
      const currentIds = new Set(chips.map(c => c.id));
      for (const id of Array.from(dispatchedReadyIdsRef.current)) {
        if (!currentIds.has(id)) dispatchedReadyIdsRef.current.delete(id);
      }
      for (const id of Array.from(confirmedReadyIdsRef.current)) {
        if (!currentIds.has(id)) confirmedReadyIdsRef.current.delete(id);
      }

      // Side effect 1: inline confirmation message (debounced).
      // Queue the filenames; on debounce expiry emit one consolidated message.
      if (readyChipsThisTick.length > 0) {
        for (const chip of readyChipsThisTick) {
          if (confirmedReadyIdsRef.current.has(chip.id)) continue;
          confirmedReadyIdsRef.current.add(chip.id);
          pendingConfirmFilenamesRef.current.push(chip.filename);
        }

        // Reset the debounce timer — coalesce arrivals within the window.
        if (readyConfirmationTimerRef.current !== null) {
          clearTimeout(readyConfirmationTimerRef.current);
        }
        readyConfirmationTimerRef.current = setTimeout(() => {
          const filenames = pendingConfirmFilenamesRef.current;
          pendingConfirmFilenamesRef.current = [];
          readyConfirmationTimerRef.current = null;
          const body = buildFileConfirmationMessage(filenames);
          if (body !== null) {
            setPendingInjection(makeLocalAssistantMessage(body));
          }
        }, READY_CONFIRMATION_DEBOUNCE_MS);
      }

      // Side effect 2: PaneEventBus dispatch on the `context` channel
      // (R5 task 016 additive event type). Carries the session-scoped file
      // IDs so subscribers (FilePreviewContextWidget — task 018) can surface
      // preview affordances for the newly-available files. NOT the same as
      // the existing R4 `workspace.widget_load` dispatch (handleAttachmentReady
      // below) — both fire on the SAME trigger but on DIFFERENT channels per
      // the typed PaneEventBus contract (ADR-030).
      if (readyChipsThisTick.length > 0) {
        // Typed cast to the additive context-channel discriminant from
        // task 016's PaneEventTypes. The `as ContextPaneEvent` cast at the
        // dispatch boundary is the ADR-030 prescribed shape (no `any`).
        const payload: ContextPaneEvent = {
          type: "files_staged",
          stagedFileIds: readyChipsThisTick.map(c => c.id),
        };
        dispatch("context", payload);
      }
    },
    [dispatch]
  );

  /**
   * onAttachmentRemoved — host cascade for a per-file dismiss click.
   *
   * Step 3 cleanup pathway decision (per task 020 POML):
   *   - Manifest removal (`ChatSession.UploadedFiles[]`): NO BFF endpoint
   *     exists yet at task 004's landing scope. Task 020 surfaces this as a
   *     deferred-to-Phase-3 backlog item (R5 lessons-learned candidate).
   *     For now the host LOGS the intent and relies on the session-end
   *     cleanup HostedService (R5 task 007) to reconcile the manifest at
   *     session lifecycle end. Orphaned manifest entries are BOUNDED by
   *     session lifetime, so the user-visible state remains consistent
   *     within a session even though a stricter per-file cleanup endpoint
   *     would be preferred.
   *   - Index removal (`spaarke-session-files` AI Search index): same
   *     cascade — R5 task 007's HostedService is the authoritative cleanup
   *     path. Per-file index-document removal endpoint is NOT exposed.
   *     RagIndexingPipeline.DeleteSessionFileChunksAsync exists as a private
   *     helper for the indexing idempotency path; exposing it would require
   *     a small endpoint addition (BFF publish-size delta) which task 020
   *     defers per the BFF hygiene rule (no BFF code in this task).
   *
   * The host therefore:
   *   1. Captures the chip metadata for telemetry / future endpoint wiring.
   *   2. Updates the local PaneEventBus dispatched-IDs ref so a future
   *      re-add of the same file re-fires the staging event.
   *   3. Logs a structured warning so the gap is observable in dev tools +
   *      analytics during Phase 2 evaluation.
   *
   * The local chip removal proceeds immediately (SprkChat splices on
   * `removeFile(index)` after this callback returns) — the user-visible UX
   * is unaffected by the deferred backend cleanup.
   */
  const handleAttachmentRemoved = React.useCallback(
    (chip: AttachmentChip, _index: number) => {
      // Free the per-id ref entries so re-adding the same file re-fires
      // ready-transition + confirmation + dispatch logic.
      dispatchedReadyIdsRef.current.delete(chip.id);
      confirmedReadyIdsRef.current.delete(chip.id);
      // R5 task 036: release the captured File-ref + promoted-chip status.
      heldFilesRef.current.delete(chip.filename);
      setPromotedChipIds(prev => {
        if (!prev.has(chip.id)) return prev;
        const next = new Set(prev);
        next.delete(chip.id);
        return next;
      });

      // TODO(r5/phase-3-backend): wire DELETE /api/ai/chat/sessions/{sessionId}/files/{fileId}
      // when the endpoint exists; until then session-end cleanup
      // (R5 task 007 HostedService) reconciles the manifest + index.
      // Logged so the gap is observable + measurable.
      if (chatSessionId !== null) {
        // eslint-disable-next-line no-console
        console.info(
          "[ConversationPane] file-chip dismissed; awaiting per-file cleanup endpoint",
          { sessionId: chatSessionId, fileId: chip.id, filename: chip.filename }
        );
      }
    },
    [chatSessionId]
  );

  /**
   * onLocalMessageInjected — SprkChat fires this after `pendingInjection`
   * has been appended to the thread. The host clears the prop back to null
   * so re-renders do not re-inject the same message.
   */
  const handleLocalMessageInjected = React.useCallback(() => {
    setPendingInjection(null);
  }, []);

  /**
   * onBeforeSendMessage — fires synchronously BEFORE SprkChat starts a
   * stream. The host inspects the message text + the current chip state to
   * decide whether to emit a deterministic interjection (multi-file
   * combined-summary case, R5 FR-03).
   *
   * The interjection emission is guarded by `emittedSummarizeInterjectionKeysRef`
   * so retries / stream-resumption of the SAME turn do not re-emit. The key
   * is a stable hash of the message text + ready chip IDs.
   */
  const handleBeforeSendMessage = React.useCallback(
    (messageText: string): void => {
      // chat-routing-redesign-r1 task 117b — capture the most recent outbound
      // message text so the playbook_options click handler can forward it as
      // `originalMessage` when the user picks a candidate.
      // ADR-015: kept in a ref (never rendered, never logged).
      lastSentMessageRef.current = messageText;

      // ── R5 task 036 / P2-CLOSEOUT-05: deterministic intent dispatch ─────
      //
      // BEFORE the multi-file interjection block (existing task 020 logic):
      // try to match a registered intent (slash / pattern / button-id). If a
      // matcher returns 'summarize-session' AND we have ready files, we run
      // the deterministic promote-and-execute orchestrator IN PARALLEL with
      // the default SprkChat send. The default send still proceeds (per
      // SprkChat contract, onBeforeSendMessage is INFORMATIONAL — it cannot
      // cancel the send; see Spaarke.UI.Components/SprkChat/types.ts line
      // 658-661). We acknowledge this via an inline Assistant chip so the
      // user knows the deterministic action is in flight.
      //
      // This is the chat-pane half of the FR-03 / task-036 contract. The
      // workspace-pane half (structured output → Summary tab) lives in
      // tasks 037 + 038; this task is the publisher (PaneEventBus events).

      // ── R6 task 080 / D-D-01 (Pillar 8 foundation) ──────────────────────
      // Capture the structured CommandRouter Intent at the send-message
      // boundary. The Intent is currently CAPTURE-ONLY — there is NO
      // behavior branch here. Downstream Phase D tasks (081 hard-slash
      // executor, 082 soft-slash agent routing, 083 reference resolver)
      // will read this Intent and dispatch. NFR-11 binding: when the user
      // typed natural language (no slash), the parsed intent's `command === null`
      // and the existing R5-task-036 matcher + SprkChat send funnel runs
      // UNCHANGED. See projects/.../CLAUDE.md §Pillar 8 + spec FR-48.
      // void-cast suppresses the "declared but never read" lint until tasks
      // 081/082/083 wire branching behavior to this value.
      const parsedIntent = parseCommandIntent(messageText);
      void parsedIntent;

      const readyChips = attachmentChips.filter(c => c.status === "ready");
      const intent = matchIntent(messageText, readyChips.length > 0, undefined);
      // R6 Hotfix Wave B-G9c3 (B9) — slash-to-NL rewire (2026-06-10):
      //
      // When the intent matched via the `/summarize` SLASH command (as opposed
      // to a natural-language pattern or button-id), DO NOT fire the
      // deterministic `executeSummarizeIntent` orchestrator. Let the message
      // flow through the SprkChat default send funnel only — the LLM agent
      // (SprkChatAgent) sees the literal "/summarize" text and routes it
      // through the natural-language path (CapabilityRouter → invoke_playbook
      // tool → InvokePlaybookHandler → IPlaybookOrchestrationService.ExecuteAsync).
      //
      // This satisfies the user's B9 decision: "/summarize in the Assistant
      // chat should produce the SAME output as 'summarize this document'."
      // Both now route through the SAME NL primitives (richer
      // PromptSchemaRenderer templates, conversational LLM-driven output)
      // instead of the JPS-template streaming path
      // (PlaybookExecutionEngine.ExecuteChatSummarizeAsync) used by the
      // direct endpoint.
      //
      // NL pattern matches ("summarize…", "please summarize…") and button-id
      // matches (`action:summarize`) STILL fire executeSummarizeIntent — they
      // preserve the R5 task 036 / P2-CLOSEOUT-05 "deterministic intent
      // dispatch" operator-UX contract. The slash command is the only path
      // that bypasses the orchestrator entry — it's purely a typing-affordance
      // synonym for natural language.
      //
      // The Document Profile context's SummarizeFilesWizard
      // (`/api/workspace/files/summarize` via summarizeService.ts) is a
      // SEPARATE endpoint and is UNAFFECTED by this change.
      if (
        intent &&
        intent.id === "summarize-session" &&
        intent.via !== "slash" &&
        chatSessionId !== null
      ) {
        // Build the HeldFile list from the ready chips. The File-equivalents
        // were captured in handleAttachmentReady (keyed by filename). Chips
        // without a captured File fall through — the orchestrator will throw
        // with a descriptive error the user can act on.
        const heldFiles: HeldFile[] = [];
        for (const chip of readyChips) {
          const file = heldFilesRef.current.get(chip.filename);
          if (file) {
            heldFiles.push({ id: chip.id, file });
          }
        }

        if (heldFiles.length > 0) {
          // Visual acknowledgement BEFORE the user's send lands (per task spec:
          // "fall back to clearing the textarea + injecting a local assistant
          // chip 'I'll summarize that for you' so the user sees the outbound
          // message land"). The default SprkChat send still proceeds —
          // suppression requires a cross-package change to SprkChat (flagged).
          setPendingInjection(
            makeLocalAssistantMessage(
              `I'll summarize ${heldFiles.length === 1 ? "that file" : `those ${heldFiles.length} files`} for you.`
            )
          );

          // Fire-and-await-internally — promote then stream. On success, mark
          // the chips Indexed (badge flip). On failure, surface an inline
          // error message. We don't await here because handleBeforeSendMessage
          // is synchronous; the orchestrator runs in parallel with the chat
          // send funnel.
          void (async () => {
            try {
              const result = await executeSummarizeIntent({
                bffBaseUrl,
                sessionId: chatSessionId,
                heldFiles,
                authenticatedFetch,
                getAccessToken,
                publishPaneEvent: dispatch,
                // R6 Hotfix Wave B-G9c2 (B8): each summarize invocation gets
                // its own unique streamId so the workspace.widget_load +
                // workspace.streaming_* events flow into a NEW Summary tab
                // (FR-06 restoration; tab title includes the source filename).
                // R5 task 038's reuse of `chatSessionId` as the streamId
                // caused all subsequent runs to overwrite the original
                // Summary tab — that behavior is reverted here. Defaulting
                // to `undefined` lets executeSummarizeIntent generate a
                // unique id via generateStreamId().
                streamId: undefined,
              });
              // Flip Held → Indexed badges on the promoted chip ids.
              setPromotedChipIds(prev => {
                const next = new Set(prev);
                for (const chip of readyChips) {
                  if (result.documentIds.length > 0) {
                    next.add(chip.id);
                  }
                }
                return next;
              });
            } catch (err) {
              const message =
                err instanceof Error
                  ? `I couldn't summarize that — ${err.message}`
                  : "I couldn't summarize that. Please try again.";
              setPendingInjection(makeLocalAssistantMessage(message));
            }
          })();
        }
        // Fall through to the multi-file interjection block below (it's
        // additive — if both apply we get the chip + the interjection).
      }

      // ── Existing task 020 multi-file interjection (untouched) ───────────
      //
      // Tri-mode router: deterministic, side-effect-free decision.
      const hasActiveWorkspaceDocument = entityContext !== null;
      const decision = routeSummarizeIntent(messageText, {
        uploadedFileCount,
        hasActiveWorkspaceDocument,
      });

      // Only the session-files branch (a) with multi-file payload emits the
      // combined-summary interjection. Single-file Summarize uses the
      // per-file affordance (R5 task 021) and does NOT emit this interjection.
      if (decision.kind !== "session-files") return;
      if (uploadedFileCount < 2) return;

      // Build the once-per-turn key — stable across retries / resumption of
      // the SAME submission.
      const readyIds = attachmentChips
        .filter(c => c.status === "ready")
        .map(c => c.id)
        .sort()
        .join("|");
      const turnKey = `${messageText.trim().toLowerCase()}::${readyIds}`;
      if (emittedSummarizeInterjectionKeysRef.current.has(turnKey)) return;
      emittedSummarizeInterjectionKeysRef.current.add(turnKey);

      const interjectionBody = buildMultiFileSummarizeInterjection(uploadedFileCount);
      if (interjectionBody === null) return;

      setPendingInjection(makeLocalAssistantMessage(interjectionBody));
    },
    [
      entityContext,
      uploadedFileCount,
      attachmentChips,
      chatSessionId,
      bffBaseUrl,
      authenticatedFetch,
      getAccessToken,
      dispatch,
    ]
  );

  // ── R6 Phase D Wave D-G1 — Pillar 8 Command Router integration ────────────
  //
  // The decoration callback below is the SINGLE seam through which tasks 081
  // (hard slashes), 082 (soft slashes), and 083 (references) dispatch. It
  // runs INSIDE SprkChat's handleSend, between body construction and stream
  // start (see ISprkChatProps.onDecorateOutboundBody JSDoc). Hard slashes
  // return null → cancel the BFF send. Soft slashes decorate the body with
  // `intentHint` for CapabilityRouter Layer 0.5. References attach
  // `resolvedReferences` to the body so the BFF prompt builder can use them.
  // Natural-language input (no slash, no refs) passes through unchanged
  // (NFR-11 backward compat).
  //
  // Some executor capabilities (conversation-history serialization for
  // `/export`, focused-tab tracking for `/pin`) require deeper plumbing
  // through @spaarke/ui-components surfaces. They are stubbed here so the
  // seam is functional; richer contexts land via follow-up tasks 084 (full
  // composition tests) and 085 (/help UI affordance polish).
  const paneEventBus = usePaneEventBus();
  const hardSlashContext = React.useMemo<HardSlashExecutorContext>(
    () => ({
      bffBaseUrl,
      authenticatedFetch,
      sessionId: chatSessionId ?? "",
      paneEventBus,
      setHelpOpen: setHelpPanelOpen,
      clearLocalConversation: () => {
        // R6 hotfix 2026-06-19 (UAT): increment the SprkChat key to force a
        // remount + state reset. Replaces the prior TODO no-op. The BFF
        // session DELETE called by the executor handles server-side state;
        // this handles client-side.
        setSprkChatRemountKey((k) => k + 1);
      },
      createNewSession: async (): Promise<string | null> => {
        // R6 closeout (Pillar 8 / task 097): POST /api/ai/chat/sessions with an
        // empty body to mint a fresh session. Body fields (DocumentId, PlaybookId,
        // HostContext) are all optional per ChatCreateSessionRequest. After the
        // BFF returns the new session id we push it into AiSessionProvider via
        // setChatSessionId — the remounted SprkChat sees the new id as its
        // sessionId prop and continues with it (no second create round-trip).
        try {
          const url = buildBffApiUrl(bffBaseUrl, "/api/ai/chat/sessions");
          const response = await authenticatedFetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({}),
          });
          if (!response.ok) return null;
          const json = (await response.json()) as { sessionId?: string };
          const newId =
            typeof json?.sessionId === "string" && json.sessionId.length > 0
              ? json.sessionId
              : null;
          if (newId !== null) {
            setChatSessionId(newId);
          }
          return newId;
        } catch {
          return null;
        }
      },
      // R6 task 097b — return a snapshot of the SprkChat conversation by reading
      // messagesRef (kept in sync via SprkChat.onMessagesChange below). Maps
      // SprkChat's IChatMessage shape (role: 'User'|'Assistant'|'System',
      // timestamp: required) to the HardSlashExecutor's slim shape
      // (role: lowercase, timestamp: optional ISO-8601). Filters system messages
      // out per HardSlashExecutor contract — only user + assistant turns are
      // exported as conversation transcript.
      getConversationHistory: (): HardSlashConversationMessage[] =>
        messagesRef.current
          .filter((m) => m.role === "User" || m.role === "Assistant")
          .map((m) => ({
            role: m.role === "User" ? "user" : "assistant",
            content: m.content,
            timestamp: m.timestamp,
          })),
      // R6 closeout (Pillar 8 / task 097c): return the most-recently-focused
      // workspace tab id tracked by the usePaneEvent('workspace', tab_change)
      // subscription above. Returns null if no tab has been focused yet.
      getFocusedTabId: (): string | null => focusedTabIdRef.current,
      activeMatterId: entityContext?.matterId ?? null,
      downloadBlob: defaultDownloadBlob,
      telemetry: defaultTelemetrySink,
    }),
    [bffBaseUrl, authenticatedFetch, chatSessionId, entityContext, paneEventBus, setChatSessionId]
  );

  const referenceResolverContext = React.useMemo<ResolverContext>(
    () => ({
      // TODO(task 084): thread real tenantId once host exposes it; empty string
      // turns OFF the resolver's caching (degraded mode) but resolution still
      // works.
      tenantId: "",
      sessionId: chatSessionId ?? "",
      entityContext: entityContext
        ? {
            entityType: entityContext.entityType,
            entityId: entityContext.entityId,
            displayName: entityContext.entityName ?? entityContext.entityType,
          }
        : undefined,
      openTabs: [],
      scopeFetch: createScopeFetch(bffBaseUrl, authenticatedFetch),
      fileLookup: createFileLookupFromSessionMap(new Map()),
    }),
    [bffBaseUrl, authenticatedFetch, chatSessionId, entityContext]
  );

  const handleDecorateOutboundBody = React.useCallback(
    async (
      body: Record<string, unknown>
    ): Promise<Record<string, unknown> | null> => {
      const msg = typeof body.message === "string" ? body.message : "";
      const intent = parseCommandIntent(msg);

      if (intent.isHardSlash) {
        try {
          const result = await executeHardSlash(intent, hardSlashContext);
          if (result.message) {
            setPendingInjection({
              role: "Assistant",
              content: result.message,
              timestamp: new Date().toISOString(),
            });
          }
        } catch (err) {
          console.error("[R6 Pillar 8] HardSlashExecutor failed:", err);
        }
        return null;
      }

      let decorated: Record<string, unknown> = intent.isSoftSlash
        ? (decorateSoftSlashBody(intent, body as Parameters<typeof decorateSoftSlashBody>[1]) as Record<string, unknown>)
        : body;

      if (intent.references.length > 0) {
        try {
          const resolved = await ReferenceResolver.resolveAll(
            intent.references,
            referenceResolverContext
          );
          decorated = { ...decorated, resolvedReferences: resolved };
        } catch (err) {
          console.error("[R6 Pillar 8] ReferenceResolver failed:", err);
        }
      }

      return decorated;
    },
    [hardSlashContext, referenceResolverContext]
  );

  // ─────────────────────────────────────────────────────────────────────────
  // chat-routing-redesign-r1 task 117b — playbook_options chat-side handlers
  // (FR-50 + FR-51). On a `playbook_options` SSE event the host (this
  // component) appends a structured Assistant chat message containing the
  // top-N candidates. SprkChatMessageRenderer renders inline link buttons +
  // an Open Library link. Click handlers below dispatch the chosen playbook
  // execution and Library modal launch.
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * onPlaybookOptions — fired by SprkChat for each `playbook_options` SSE event.
   * Synthesizes an Assistant chat message via the existing `injectLocalMessage`
   * mechanism (R5 task 020 contract). The message carries
   * `metadata.responseType='playbook_options'` so `SprkChatMessageRenderer`
   * renders the candidates as inline link buttons (FR-50) + "Open Library" link (FR-51).
   *
   * ADR-015: payload is tier-1 safe by BFF construction (controlled-vocabulary
   * reasons, admin display names, opaque IDs only). The handler MUST NOT log
   * the payload — only structural counts.
   */
  const handlePlaybookOptions = React.useCallback(
    (payload: {
      candidates: Array<{
        playbookId: string;
        playbookCode: string;
        displayName: string;
        confidence: number;
        reason: string;
      }>;
      libraryModalCta: boolean;
      sessionAttachmentIds: string[];
      rerankInvoked: boolean;
      rerankReason?: string | null;
    }): void => {
      // ADR-015 telemetry: emit ONLY counts + boolean signals — never payload contents.
      console.log(
        '[ConversationPane] playbook_options received — candidates:%d libraryModalCta:%s rerankInvoked:%s',
        payload.candidates.length,
        payload.libraryModalCta,
        payload.rerankInvoked,
      );

      setPendingInjection({
        role: 'Assistant',
        // `content` carries a tiny fallback text in case the renderer falls back
        // to markdown (defensive — should never happen). Tier-1 safe.
        content: payload.candidates.length > 0
          ? 'Which playbook would you like me to use?'
          : "I couldn't find a confident match for your files.",
        timestamp: new Date().toISOString(),
        metadata: {
          responseType: 'playbook_options',
          data: {
            candidates: payload.candidates,
            libraryModalCta: payload.libraryModalCta,
            sessionAttachmentIds: payload.sessionAttachmentIds,
            rerankInvoked: payload.rerankInvoked,
            rerankReason: payload.rerankReason ?? null,
          },
        },
      });
    },
    []
  );

  /**
   * onSelectPlaybook — user clicked a candidate playbook link button (FR-50).
   *
   * POSTs to `/api/ai/playbook-dispatch/execute` with `{ playbookId,
   * sessionAttachmentIds, originalMessage, sessionId }`. The orchestrator runs
   * the chosen playbook against the same session context.
   *
   * NOTE: as of task 117b shipping, the orchestrator emit point for
   * `playbook_options` is NOT yet wired (the 117a builder is registered in DI
   * but not yet invoked from `ChatEndpoints`), and the `/playbook-dispatch/execute`
   * endpoint is NOT yet implemented in the BFF. This handler will hit a 404
   * until both arrive. We surface a console error + a brief inline confirmation
   * so failure is visible during development.
   *
   * ADR-028: uses `authenticatedFetch` from `useAuth()` — never raw fetch +
   * Authorization header. ADR-015: payload is tier-1 (opaque IDs); we DO carry
   * `originalMessage` because the dispatcher needs it for routing — that's
   * exempted user content sent server-side, NOT logged.
   */
  const handleSelectPlaybook = React.useCallback(
    (playbookId: string, sessionAttachmentIds: string[]): void => {
      // Fire-and-forget; the chat thread reflects the outcome via the next
      // assistant turn (when the orchestrator runs the chosen playbook).
      void (async () => {
        try {
          // Use buildBffApiUrl-style concatenation; the dispatch endpoint name
          // is per spec FR-50 even though it is not yet implemented on the BFF.
          const url = `${bffBaseUrl.replace(/\/$/, '')}/api/ai/playbook-dispatch/execute`;
          const response = await authenticatedFetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              playbookId,
              sessionAttachmentIds,
              originalMessage: lastSentMessageRef.current,
              sessionId: chatSessionId ?? null,
            }),
          });
          if (!response.ok) {
            console.error(
              '[ConversationPane] playbook-dispatch failed — status:%d',
              response.status,
            );
            setPendingInjection({
              role: 'Assistant',
              content:
                response.status === 404
                  ? "I'm not able to run that playbook yet — the dispatcher endpoint is still being wired up."
                  : 'I couldn\'t start that playbook. Please try again.',
              timestamp: new Date().toISOString(),
            });
          }
        } catch (err) {
          // Network / auth failures — log structurally only, never include the error message
          // verbatim because some error objects can leak headers or URLs.
          console.error('[ConversationPane] playbook-dispatch threw:', err instanceof Error ? err.name : 'unknown');
        }
      })();
    },
    [authenticatedFetch, bffBaseUrl, chatSessionId]
  );

  /**
   * onOpenLibraryModal — user clicked the "Open Library" link (FR-51).
   *
   * Opens the `sprk_playbooklibrary` Code Page via Xrm.Navigation.navigateTo
   * (target: 2 modal). When `sessionAttachmentIds` are present we pass them
   * through the `data` envelope so the Library can pre-filter by attachment
   * classification (when available upstream).
   *
   * ADR-021 + ADR-028: dialog launch follows the existing
   * `SemanticSearchCriteriaTool.launchSemanticSearch` pattern (proven Xrm
   * frame-walk + navigateTo with target:2, percent-sized modal).
   */
  const handleOpenLibraryModal = React.useCallback(
    (sessionAttachmentIds: string[]): void => {
      // Resolve Xrm.Navigation via frame walk (handles iframe nesting in MDA).
      let nav: { navigateTo?: (...args: unknown[]) => Promise<unknown> } | null = null;
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const w = window as any;
        const xrm = w?.Xrm ?? w?.parent?.Xrm ?? w?.top?.Xrm ?? null;
        nav = xrm?.Navigation ?? null;
      } catch {
        nav = null;
      }

      if (!nav?.navigateTo) {
        console.warn(
          '[ConversationPane] Open Library: Xrm.Navigation unavailable — running outside Dataverse host.',
        );
        return;
      }

      // Build the `data` query string. The Library Code Page accepts
      // `sessionAttachmentIds` as a comma-separated opt-in pre-filter; when
      // absent the modal opens unfiltered (per FR-51).
      const parts: string[] = [];
      if (sessionAttachmentIds.length > 0) {
        parts.push(
          `sessionAttachmentIds=${encodeURIComponent(sessionAttachmentIds.join(','))}`,
        );
      }
      const data = parts.join('&');

      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        (nav.navigateTo as any)(
          {
            pageType: 'webresource',
            webresourceName: 'sprk_playbooklibrary',
            data,
          },
          {
            target: 2,
            width: { value: 85, unit: '%' },
            height: { value: 85, unit: '%' },
            title: 'Playbook Library',
          },
        ).catch?.((err: unknown) => {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const code = (err as any)?.errorCode;
          // errorCode 2 = user-cancelled (modal closed); ignore.
          if (code !== 2) {
            console.warn('[ConversationPane] Open Library: navigateTo error:', code ?? 'unknown');
          }
        });
      } catch (err) {
        console.warn('[ConversationPane] Open Library: navigateTo threw synchronously:', err instanceof Error ? err.name : 'unknown');
      }
    },
    []
  );

  /**
   * dispatchSummarizeIntent — pure routing decision helper, retained from
   * task 019 as a public surface for tests + future call sites. Branch (c)
   * (FR-03 prompt-first) is emitted via the predefinedPrompts surface;
   * branches (a) + (b) fall through to the default SprkChat send funnel
   * because the existing send path routes correctly:
   *
   *   - Branch (a) (session-files): SprkChat sends the message with the
   *     ready attachments in the outbound payload (FR-07 attachments
   *     contract). The BFF chat agent has access to task 015's
   *     `InvokeSummarizePlaybookTool` and will route the LLM call through
   *     the session-files Summarize path. The deterministic interjection
   *     (multi-file case) is emitted by `handleBeforeSendMessage` ABOVE.
   *
   *   - Branch (b) (active-document): the SpaarkeAi shell currently does
   *     NOT host the R3 SummarizeFilesDialog wizard (LegalWorkspace owns
   *     it). Falling through to the default SprkChat send funnel produces
   *     a sensible chat response via the default playbook routing for the
   *     active document context — back-compat preserved for LegalWorkspace
   *     consumers (they invoke the wizard outside the SpaarkeAi shell).
   *
   *   - Branch (c) (prompt-first): owned end-to-end by task 019 — surface
   *     the deterministic interjection via the existing predefinedPrompts
   *     suggestion surface.
   */
  const dispatchSummarizeIntent = React.useCallback(
    (messageText: string): boolean => {
      const hasActiveWorkspaceDocument = entityContext !== null;
      const decision = routeSummarizeIntent(messageText, {
        uploadedFileCount,
        hasActiveWorkspaceDocument,
      });

      switch (decision.kind) {
        case "not-summarize":
        case "session-files":
        case "active-document":
          // Branches (a) + (b): fall through to the default SprkChat send.
          // Branch (a) multi-file interjection is emitted by
          // `handleBeforeSendMessage` (synchronously, before the user's
          // message is appended).
          return false;

        case "prompt-first":
          // Branch (c): surface interjection via predefinedPrompts.
          setPendingSummarizeInterjection(decision.interjection);
          return true;
      }
    },
    [entityContext, uploadedFileCount]
  );

  // Mark `dispatchSummarizeIntent` as referenced so the TypeScript no-unused-
  // locals rule does not flag it. It is a stable public surface for module-
  // level tests + future direct call sites (e.g., a future slash-command
  // suggestion chip click handler).
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
        // R5 task 020 / D2-11: reset per-session refs so the same chat
        // surface in a new session does not carry stale interjection /
        // dispatch / confirmation guards from the prior session.
        emittedSummarizeInterjectionKeysRef.current.clear();
        dispatchedReadyIdsRef.current.clear();
        confirmedReadyIdsRef.current.clear();
        pendingConfirmFilenamesRef.current = [];
        // R5 task 036: reset held-file File-refs + promoted-chip set so a
        // new session does not carry the previous session's promotion state.
        heldFilesRef.current.clear();
        setPromotedChipIds(new Set());
        if (readyConfirmationTimerRef.current !== null) {
          clearTimeout(readyConfirmationTimerRef.current);
          readyConfirmationTimerRef.current = null;
        }
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
    (attachment: ChatAttachment) => {
      // R5 SC-18 cycle-6 (2026-06-05): the DocumentViewerWidget shows
      // "Preview not available" for chat-uploaded files because no SharePoint
      // Embedded preview URL exists (the file is held client-side until the
      // user triggers an intent like /summarize that promotes it). The empty
      // preview tab is misleading — operator feedback: "preview but seems too
      // fast for an actual preview to be generated, says 'Preview not
      // available'". Suppressing the dispatch until R5 task 022 upgrades the
      // widget to render text content as a fallback OR a real previewUrl
      // pipeline is wired for client-staged files. Until then, the chip strip
      // above the input bar is the visible confirmation that the file was
      // received; the structured Summarize output will appear in the
      // Workspace-pane Summary tab (task 038) when /summarize fires.
      //
      // Original dispatch (kept commented for reversibility — uncomment when
      // task 022 ships):
      //   const widgetData: DocumentViewerWidgetData = {
      //     filename: attachment.filename,
      //     contentType: attachment.contentType,
      //     textContent: attachment.textContent,
      //   };
      //   dispatch("workspace", {
      //     type: "widget_load",
      //     widgetType: DOCUMENT_VIEWER_WIDGET_TYPE,
      //     widgetData,
      //     displayName: attachment.filename,
      //   });

      // R5 task 036: capture the File so the promote-and-execute
      // orchestrator (`executeSummarizeIntent`) can POST multipart binary
      // to `/api/ai/chat/sessions/{id}/documents`.
      //
      // PREFERRED PATH (R5 task 036 sub-task — additive shared-lib change):
      // SprkChat now forwards the ORIGINAL `File` reference through
      // `ChatAttachment.file`. Binary uploads (PDF/DOCX) round-trip
      // correctly through BFF Document Intelligence using these bytes.
      //
      // FALLBACK PATH (defense in depth): if `attachment.file` is absent
      // (older shared-lib build, edge case, or some upstream consumer that
      // didn't populate it), reconstruct a synthetic File from
      // `textContent`. This works for TXT/MD but NOT for PDF/DOCX — the
      // promotion step will then surface a descriptive content-type error.
      try {
        const heldFile: File =
          attachment.file ??
          new File(
            [attachment.textContent],
            attachment.filename,
            { type: attachment.contentType || "text/plain" }
          );
        // Match by filename — the chip id from `onAttachmentsChanged` arrives
        // separately; we resolve the binding in `handleBeforeSendMessage`
        // when assembling the HeldFile list.
        heldFilesRef.current.set(attachment.filename, heldFile);
      } catch {
        // Defensive: if File construction fails (e.g. older runtime), the
        // promotion step will throw a descriptive error and the user can
        // fall back to the [action:upload] prompt-button path.
      }
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

          {/* ── R5 task 020 / D2-11: "N files attached" indicator ────── */}
          {/*
            Persistent indicator rendered ABOVE the SprkChat chip strip
            whenever the session has one or more uploaded files. Drives off
            the local `attachmentChips` mirror state populated via SprkChat's
            `onAttachmentsChanged` callback. Hidden when count = 0 to keep
            the input area uncluttered.

            Accessibility: `role="status"` + `aria-live="polite"` so screen
            readers announce count changes without interrupting the user's
            current focus.
          */}
          {uploadedFileCount > 0 && (
            <div
              className={styles.filesAttachedIndicator}
              role="status"
              aria-live="polite"
              data-testid="files-attached-indicator"
            >
              <Text className={styles.filesAttachedIndicatorText}>
                {uploadedFileCount === 1
                  ? "1 file attached"
                  : `${uploadedFileCount} files attached`}
              </Text>
              <Text className={styles.filesAttachedIndicatorHint}>
                {uploadedFileCount === 1
                  ? "available for this session"
                  : "available for this session — combined Summarize will fold all into one"}
              </Text>
              {/* R5 task 036: surface Held vs Indexed counts so the operator
                  sees promotion status without opening the workspace pane. */}
              {promotedChipIds.size > 0 && (
                <Text
                  className={styles.filesAttachedIndicatorHint}
                  data-testid="files-promoted-indicator"
                >
                  {`(${promotedChipIds.size} indexed)`}
                </Text>
              )}
            </div>
          )}

          {/* ── SprkChat — fills remaining height below the chip bar ── */}
          {/*
            Spaarke Auth v2 §H-4: pass `authenticatedFetch` (for one-shot BFF
            calls) and `getAccessToken` (escape hatch for SSE ReadableStream)
            instead of a snapshotted `accessToken: string`. Task 023 owns the
            SprkChat API change that consumes these props.

            R5 task 020 / D2-11 wires the new chat-pane orchestration UX
            props (all optional; existing consumers ignore them):
              - onAttachmentsChanged → mirror chip lifecycle for indicator +
                routing + ready-transition tracking
              - onAttachmentRemoved → per-file cleanup cascade (manifest +
                AI Search index — see handleAttachmentRemoved docstring for
                Phase 3 backend gap rationale)
              - injectLocalMessage + onLocalMessageInjected → deterministic
                inline file-confirmation + multi-file Summarize interjection
              - onBeforeSendMessage → synchronous interjection emission point
                for FR-03 multi-file combined-summary semantics
          */}
          <div className={mergeClasses(styles.sprkChatFlex)}>
            <SprkChat
              key={sprkChatRemountKey}
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
              onAttachmentsChanged={handleAttachmentsChanged}
              onAttachmentRemoved={handleAttachmentRemoved}
              injectLocalMessage={pendingInjection}
              onLocalMessageInjected={handleLocalMessageInjected}
              onBeforeSendMessage={handleBeforeSendMessage}
              // R6 task 097b / TIER-C — maintain a ref of conversation messages
              // for /export markdown generation (consumed by HardSlashExecutor
              // via getConversationHistory above).
              onMessagesChange={(messages) => {
                messagesRef.current = messages;
              }}
              onDecorateOutboundBody={handleDecorateOutboundBody}
              // chat-routing-redesign-r1 task 117b (FR-49 + FR-50 + FR-51)
              onPlaybookOptions={handlePlaybookOptions}
              onSelectPlaybook={handleSelectPlaybook}
              onOpenLibraryModal={handleOpenLibraryModal}
            />
            {/*
              R6 task 085 / D-D-06 (Pillar 8 `/help` UI affordance) — a
              discoverable button anchored top-right of the chat region.
              Clicking opens the same CommandHelpPanel as the `/help` hard
              slash so users who don't know slash syntax can discover the
              closed Pillar 8 vocabulary. Additive UX — does NOT modify
              SprkChat's internal input bar (NFR-11).
            */}
            <HelpAffordance onClick={() => setHelpPanelOpen(true)} />
            <CommandHelpPanel
              open={helpPanelOpen}
              onClose={() => setHelpPanelOpen(false)}
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
