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
 *     instead of the deleted R1 standalone provider hook.
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
 * @see HistoryOverlay.tsx — session history side-overlay (task 022, FR-03 / OC-01)
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
// ai-architecture-redesign-r1 task 023 (FR-P1-04 / ADR-039): the Click entry
// path. Next-step chips carry a binding_id sourced from the Binding row's
// `sprk_chiptransitions`; a chip click flows through the ONE shared
// `dispatchConsumer(bindingId, args)` helper (canonical SSE consumption +
// PaneEventBus bridging INSIDE it). The R5/R7 per-capability dispatch modules
// and the client-side intent matcher were deleted here (hard cutover, NFR-08)
// — the server resolves the Binding; the client never detects intent.
import {
  createConsumerDispatcher,
  parseConsumerChips,
  type ConsumerChip,
  type DispatchWorkspaceEvent,
} from "@spaarke/ui-components";
import { ConsumerChips } from "./ConsumerChips";
// ai-architecture-redesign-r1 task 022b (FR-P1-03 / ADR-039): the Event entry
// path client leg. When an attach gesture completes (EVERY chip of the gesture
// has its /documents 202 — count-complete batching, G-P1 UAT fix 2026-07-05;
// 30 s stuck-promotion fallback), the pane POSTs the document-uploaded Event
// endpoint ONCE via the canonical SSE path and renders the rule's stream:
// event_classification (classification line), event_output (the STORED ledger
// entry — ADR-040 render-follows-store), event_confirmation / event_notice
// (message + chips), chips (next-step Click-path chips). The server owns
// every routing decision — typedCommand is passed verbatim (no pre-filter).
import {
  runDocumentUploadedEvent,
  formatClassificationMessage,
  formatEventOutputMarkdown,
  formatNoticeMessage,
} from "./DocumentUploadedEventStream";
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
// References (083) resolve `#scope` / `@<entity>` / `#<filename>` at parse time
// and attach `resolvedReferences` to the body. NFR-11 binding: natural-language
// input (no slash, no references) passes through unchanged.
//
// FR-P2-05 hard cutover (task 034): the soft-slash intent-bias decoration
// (formerly `SoftSlashRouter.decorateBody`) is RETIRED end-to-end (NFR-08). No
// client-to-server intent-bias hint is sent; soft-slash text now enters the
// agent-turn loop like any NL utterance. Turning the four soft slashes into
// Click-path deterministic direct invocations (dispatchConsumer by binding id)
// is deferred to the P3 binding-id-carrying launcher work (FR-P3-06) — see the
// escalation note in this task's integration notes.
import {
  executeHardSlash,
  defaultTelemetrySink,
  defaultDownloadBlob,
  type ExecutorContext as HardSlashExecutorContext,
  type ConversationMessage as HardSlashConversationMessage,
} from "./HardSlashExecutor";
import { CommandHelpPanel } from "./CommandHelpPanel";
import { HelpAffordance } from "./HelpAffordance";
import ReferenceResolver, {
  createScopeFetch,
  createFileLookupFromSessionMap,
  type ResolverContext,
} from "./ReferenceResolver";
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
    clearChatSession,
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

  /**
   * R7 Wave 12.3 Phase 12.3a UAT fix (2026-07-03) — normalize SessionRestoreMessage[]
   * to IChatMessage[] for the `initialMessages` prop on SprkChat.
   *
   * SessionRestoreMessage.role is a bare `string` because the server returns the raw
   * enum name. IChatMessage.role is a strict `'User' | 'Assistant' | 'System'` union.
   * Unrecognized roles fall back to `'User'` (defensive — the server can't emit values
   * outside the enum today, but we don't want a future backend addition to break the UI).
   */
  const restoredInitialMessages = React.useMemo<IChatMessage[] | undefined>(() => {
    if (!restoreCtx?.recentMessages || restoreCtx.recentMessages.length === 0) return undefined;
    return restoreCtx.recentMessages.map(m => ({
      role:
        m.role === 'User' || m.role === 'Assistant' || m.role === 'System'
          ? m.role
          : 'User',
      content: m.content,
      timestamp: m.timestamp,
    }));
  }, [restoreCtx?.recentMessages]);
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

  // R7 task 094 / FR-18 — forward-declared ref for the Playbook Library modal
  // opener. The actual `handleOpenLibraryModal` useCallback is declared below
  // and depends on entityContext / Xrm.Navigation, but the
  // `hardSlashContext` useMemo above needs to reference it BEFORE
  // it's lexically defined. Solution: a ref that `handleOpenLibraryModal`
  // assigns itself to via a useEffect side-effect after each render. The
  // `/playbooks` hard slash invokes `openLibraryModalRef.current?.([])`.
  // Pattern mirrors `messagesRef` / `focusedTabIdRef` already in this file.
  const openLibraryModalRef = React.useRef<((ids: string[]) => void) | null>(null);

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

  // Attachment-chip ids that have been successfully promoted to server-side
  // session files (auto-promote flow in handleAttachmentReady, R7 12.3a).
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

  // ── task 022b / FR-P1-03: Event-path upload-batch state ───────────────────
  //
  // The Event entry path fires ONCE per ATTACH GESTURE, after EVERY file queued
  // in the gesture has received its server-issued documentId (count-complete
  // batching — G-P1 UAT round-1 Defect 2 fix, 2026-07-05). The original 250 ms
  // post-202 debounce fired one event POST per file when real promotions landed
  // seconds apart, so the server saw partial batches (per-file summaries + the
  // "files not available yet" notice). Semantics now:
  //
  //   - MEMBERSHIP: every attachment chip visible in the strip that is not in a
  //     terminal error state and has not been accounted to a previously-fired
  //     batch belongs to the current gesture batch (`eventBatchExpectedRef`).
  //   - SETTLEMENT: a chip settles when its `/documents` promotion 202 lands
  //     (queued into `pendingEventFilesRef`) or permanently fails
  //     (`eventBatchFailedChipIdsRef`, from the auto-promote effect) or is
  //     removed from the strip.
  //   - FIRE: the instant every expected chip is settled — exactly ONE event
  //     POST per gesture. A 30 s fallback timer (anchored at the first settled
  //     promotion) bounds stuck promotions: on expiry the batch fires with
  //     whatever settled.
  //
  //   - `pendingEventFilesRef` — promoted-but-not-yet-event-fired docs.
  //   - `eventBatchExpectedRef`— chip ids belonging to the open gesture batch.
  //   - `eventBatchFailedChipIdsRef` — chips whose promotion permanently failed
  //     (or whose 202 body carried no documentId) — settled without a file.
  //   - `eventAccountedChipIdsRef` — chips consumed by an already-fired batch
  //     (or terminal-error chips); they never (re-)join a batch unless re-added.
  //   - `eventBatchTimerRef`   — the 30 s stuck-promotion fallback timer.
  //   - `eventBatchOpenedAtRef`— when the gesture batch OPENED (first chip
  //     joined). A message sent at-or-after this instant is the "typed command
  //     accompanying the upload" and is passed verbatim as `typedCommand`
  //     (the SERVER decides supersede — ADR-039: no client pre-filtering).
  //   - `lastSentAtRef`        — timestamp twin of `lastSentMessageRef`.
  //   - `attachmentChipsRef`   — render-free chip mirror so the fire callback
  //     can order fileIds by upload order (index 0 stays deterministic).
  //   - `fireEventBatchRef`    — indirection so callbacks declared ABOVE the
  //     fire callback (handleAttachmentsChanged et al.) can trigger the check
  //     without a TDZ/dependency cycle.
  const pendingEventFilesRef = React.useRef<Array<{ chipId: string; documentId: string }>>([]);
  const eventBatchExpectedRef = React.useRef<Set<string>>(new Set());
  const eventBatchFailedChipIdsRef = React.useRef<Set<string>>(new Set());
  const eventAccountedChipIdsRef = React.useRef<Set<string>>(new Set());
  const eventBatchTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const eventBatchOpenedAtRef = React.useRef<number | null>(null);
  const lastSentAtRef = React.useRef<number | null>(null);
  const attachmentChipsRef = React.useRef<AttachmentChip[]>([]);
  const fireEventBatchRef = React.useRef<() => void>(() => undefined);
  const EVENT_BATCH_FALLBACK_MS = 30_000;

  /**
   * Count-complete check: fires the Event batch the moment EVERY expected chip
   * of the current gesture is settled (documentId received, permanently failed,
   * or removed). Reads refs only — safe to call from any chip-lifecycle seam.
   */
  const maybeFireEventBatch = React.useCallback((): void => {
    const expected = eventBatchExpectedRef.current;
    if (expected.size === 0) return;
    const settledIds = new Set(pendingEventFilesRef.current.map((p) => p.chipId));
    for (const id of expected) {
      if (!settledIds.has(id) && !eventBatchFailedChipIdsRef.current.has(id)) {
        return; // at least one promotion still in flight — keep waiting.
      }
    }
    fireEventBatchRef.current();
  }, []);

  /**
   * Settle a chip WITHOUT a documentId (permanent promotion failure, or a 202
   * whose body carried no documentId). The batch must not wait for it.
   */
  const markEventFilePromotionFailed = React.useCallback(
    (chipId: string): void => {
      if (!eventBatchExpectedRef.current.has(chipId)) return;
      eventBatchFailedChipIdsRef.current.add(chipId);
      maybeFireEventBatch();
    },
    [maybeFireEventBatch]
  );

  // Assistant-message injection queue (task 022b). `pendingInjection` is a
  // single one-shot slot; the Event stream can deliver several renderable
  // events in one network chunk (classification → output → notice), so
  // Event-path messages enqueue and drain through SprkChat's
  // onLocalMessageInjected acknowledgement (see enqueueAssistantMessage).
  const injectionQueueRef = React.useRef<IChatMessage[]>([]);

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

  // Cleanup the debounce timers + interjection ref set on unmount.
  React.useEffect(() => {
    return () => {
      if (readyConfirmationTimerRef.current !== null) {
        clearTimeout(readyConfirmationTimerRef.current);
      }
      if (eventBatchTimerRef.current !== null) {
        clearTimeout(eventBatchTimerRef.current);
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
      // task 022b: render-free mirror for the Event-path fire callback
      // (upload-order fileIds sorting without re-subscribing the effect).
      attachmentChipsRef.current = chips;

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
      for (const id of Array.from(eventAccountedChipIdsRef.current)) {
        if (!currentIds.has(id)) eventAccountedChipIdsRef.current.delete(id);
      }
      for (const id of Array.from(eventBatchFailedChipIdsRef.current)) {
        if (!currentIds.has(id)) eventBatchFailedChipIdsRef.current.delete(id);
      }

      // task 022b (FR-P1-03) + G-P1 Defect-2 fix: gesture-batch MEMBERSHIP.
      // Every chip in the strip that is not accounted to a previously-fired
      // batch and is not terminally errored belongs to the CURRENT gesture
      // batch — including chips still 'extracting' (they joined the same
      // attach gesture; the batch waits for their promotion). The first chip
      // joining OPENS the batch window: any message the user sends at or
      // after this instant counts as the command that accompanied the upload
      // (passed verbatim as `typedCommand` — the server decides supersede).
      const expected = eventBatchExpectedRef.current;
      let membershipChanged = false;
      for (const chip of chips) {
        if (eventAccountedChipIdsRef.current.has(chip.id)) continue;
        if (chip.status === "error") {
          // Extraction failed — the chip can never promote; settle it out.
          if (expected.delete(chip.id)) membershipChanged = true;
          eventAccountedChipIdsRef.current.add(chip.id);
          continue;
        }
        if (!expected.has(chip.id)) {
          expected.add(chip.id);
          membershipChanged = true;
        }
      }
      for (const id of Array.from(expected)) {
        if (!currentIds.has(id)) {
          expected.delete(id);
          eventBatchFailedChipIdsRef.current.delete(id);
          membershipChanged = true;
        }
      }
      if (expected.size > 0 && eventBatchOpenedAtRef.current === null) {
        eventBatchOpenedAtRef.current = Date.now();
      }
      if (membershipChanged) {
        maybeFireEventBatch();
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
    [dispatch, maybeFireEventBatch]
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
      // G-P1 Defect-2 fix: a dismissed chip leaves the gesture batch — the
      // count-complete check must not wait for its promotion.
      eventBatchExpectedRef.current.delete(chip.id);
      eventBatchFailedChipIdsRef.current.delete(chip.id);
      eventAccountedChipIdsRef.current.delete(chip.id);
      pendingEventFilesRef.current = pendingEventFilesRef.current.filter(
        (p) => p.chipId !== chip.id
      );
      maybeFireEventBatch();
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
    [chatSessionId, maybeFireEventBatch]
  );

  /**
   * enqueueAssistantMessage — ordered, loss-free injection of Assistant
   * messages through the single-slot `pendingInjection` prop (task 022b).
   *
   * The Event-path SSE stream can deliver several renderable events within
   * one React batch (e.g. event_classification + event_output arriving in the
   * same network chunk). Direct `setPendingInjection` calls would clobber one
   * another under state batching, so Event-path messages go through this
   * queue: when the slot is free the message occupies it immediately;
   * otherwise it waits in `injectionQueueRef` and drains one-per-injection
   * via SprkChat's onLocalMessageInjected acknowledgement below.
   */
  const enqueueAssistantMessage = React.useCallback((message: IChatMessage): void => {
    setPendingInjection((prev) => {
      if (prev === null) return message;
      injectionQueueRef.current.push(message);
      return prev;
    });
  }, []);

  /**
   * onLocalMessageInjected — SprkChat fires this after `pendingInjection`
   * has been appended to the thread. The host clears the prop back to null
   * (or promotes the next queued Event-path message — task 022b) so
   * re-renders do not re-inject the same message.
   */
  const handleLocalMessageInjected = React.useCallback(() => {
    setPendingInjection(() => injectionQueueRef.current.shift() ?? null);
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
      // task 022b (FR-P1-03): timestamp twin — a message sent at/after the
      // current upload batch opened is the batch's accompanying typed
      // command; the Event-path fire callback passes it verbatim as
      // `typedCommand` (the SERVER decides supersede — ADR-039).
      lastSentAtRef.current = Date.now();

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

      // ── ADR-039 (task 023 / FR-P1-04): NO client-side intent detection ────
      // Natural language flows to the agent turn (Text path) unchanged; chips
      // dispatch deterministically by binding_id through dispatchConsumer
      // (Click path); upload events fire Event-path rules server-side. The
      // former client intent matcher + per-capability orchestrator were
      // deleted (hard cutover, NFR-08) — held-file promotion is the server's
      // job (SessionFileTextSource reads session-file rows populated at
      // upload time via the shared paperclip flow).

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
  // (hard slashes) and 083 (references) dispatch. It runs INSIDE SprkChat's
  // handleSend, between body construction and stream start (see
  // ISprkChatProps.onDecorateOutboundBody JSDoc). Hard slashes return null →
  // cancel the BFF send. References attach `resolvedReferences` to the body so
  // the BFF prompt builder can use them. Natural-language input (no slash, no
  // refs) passes through unchanged (NFR-11 backward compat).
  //
  // FR-P2-05 hard cutover (task 034): the former soft-slash body decoration
  // (intent-bias wire field) is retired — soft slashes are no longer special-
  // cased here (see the import-block note above).
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
      // R7 task 094 / FR-18 — `/playbooks` hard-slash opener. Indirected
      // through `openLibraryModalRef` because `handleOpenLibraryModal`
      // (the underlying Xrm.Navigation thunk) is declared LATER in this
      // function body and is in the Temporal Dead Zone at memo-factory time.
      // The ref is assigned by a useEffect immediately below the
      // `handleOpenLibraryModal` declaration. Browse-mode opens with an empty
      // sessionAttachmentIds array (no pre-filter) per task 093 audit Q6.
      openLibraryModal: (): void => {
        openLibraryModalRef.current?.([]);
      },
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

      // FR-P2-05 hard cutover (task 034): soft slashes are NO LONGER decorated
      // with an intent-bias wire field (retired end-to-end, NFR-08). The body
      // passes through unchanged; the utterance enters the agent-turn loop. The
      // Click-path deterministic direct invocation for the four soft slashes
      // (dispatchConsumer by binding id) awaits the P3 launcher work (FR-P3-06).
      let decorated: Record<string, unknown> = body;

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

  // ── Click path (task 023 / FR-P1-04 / ADR-039) ───────────────────────────
  //
  // Next-step chips from the just-completed Binding's `sprk_chiptransitions`
  // (delivered via the task-022 `context_event` SSE contract, discriminant
  // `consumer_chips`). Chips CARRY binding_id; a click calls the ONE shared
  // dispatchConsumer(bindingId, args) helper — no intent detection, no
  // capability branching. A new chip set replaces the previous one; the set
  // clears on click (single dispatch decision per turn — the r7 lesson) and
  // on session change.
  const [consumerChips, setConsumerChips] = React.useState<ReadonlyArray<ConsumerChip>>([]);

  // Session-level attachment count for the empty-attachments Click precondition
  // (G-P1 UAT round-2 hardening, 2026-07-06). The composer chip strip is the
  // WRONG proxy on its own: SprkChat clears it on every stream completion
  // (FR-07) while the session manifest still holds the promoted files — an
  // attachment-requiring chip must stay dispatchable as long as the SESSION
  // has files. Union of manifest-promoted chip ids (pruned on removal + reset
  // on session change) and composer chips already ready but not yet promoted.
  const sessionAttachmentCount = React.useMemo(() => {
    const ids = new Set<string>(promotedChipIds);
    for (const chip of attachmentChips) {
      if (chip.status === "ready") ids.add(chip.id);
    }
    return ids.size;
  }, [attachmentChips, promotedChipIds]);

  // The bound dispatcher. Stable per (bffBaseUrl, session, auth, bus) — the
  // helper re-reads the session id per dispatch via the getter.
  const chatSessionIdRef = React.useRef<string | null>(chatSessionId);
  chatSessionIdRef.current = chatSessionId;
  const dispatchConsumer = React.useMemo(
    () =>
      createConsumerDispatcher({
        bffBaseUrl,
        getSessionId: () => chatSessionIdRef.current,
        getAccessToken,
        publishPaneEvent: (channel, event: DispatchWorkspaceEvent) =>
          dispatch(channel, event as WorkspacePaneEvent),
      }),
    [bffBaseUrl, getAccessToken, dispatch]
  );

  // ── Event path (task 022b / FR-P1-03 / ADR-039) ──────────────────────────
  //
  // fireDocumentUploadedEvent — the batch timer callback. Drains the pending
  // promoted-document queue, orders fileIds by the chip strip's upload order
  // (index 0 = deterministic top-1 for the bulk bound), resolves the
  // accompanying typed command (sent at/after the batch opened — passed
  // VERBATIM; the server enforces supersede, opt-out, daily cap, M4 policy),
  // then consumes the Event SSE stream through the canonical
  // runDocumentUploadedEvent helper (readSseStream inside — the ONE SSE path):
  //   event_classification → classification line (assistant message)
  //   event_output         → the STORED ledger payload rendered as the summary
  //                          (ADR-040 render-follows-store)
  //   event_confirmation   → confirmation message + its chips
  //   event_notice         → subtle inline notice (+ chips when present)
  //   chips (any carrier)  → <ConsumerChips> via the shared parseConsumerChips
  //   error                → safe server message as an assistant line
  // Stream/HTTP failures render ONE stable failure line (ADR-019 — never raw
  // server detail). ADR-015: logs carry structural counts/flags only.
  const fireDocumentUploadedEvent = React.useCallback((): void => {
    // Cancel the stuck-promotion fallback (count-complete fired first, or the
    // fallback IS what invoked us — either way the timer is spent).
    if (eventBatchTimerRef.current !== null) {
      clearTimeout(eventBatchTimerRef.current);
      eventBatchTimerRef.current = null;
    }

    const pending = pendingEventFilesRef.current;
    pendingEventFilesRef.current = [];
    // Account every chip of this gesture so late/duplicate promotions open a
    // NEW batch instead of re-firing this one (count-complete bookkeeping).
    for (const id of eventBatchExpectedRef.current) {
      eventAccountedChipIdsRef.current.add(id);
    }
    for (const p of pending) {
      eventAccountedChipIdsRef.current.add(p.chipId);
    }
    eventBatchExpectedRef.current = new Set();
    eventBatchFailedChipIdsRef.current = new Set();
    const openedAt = eventBatchOpenedAtRef.current;
    eventBatchOpenedAtRef.current = null;

    const sessionId = chatSessionIdRef.current;
    if (pending.length === 0 || !sessionId) return;

    // fileIds in upload order — promotions complete out of order, so sort by
    // the chip strip's order (attachmentChipsRef mirrors SprkChat's array).
    const chipOrder = new Map<string, number>(
      attachmentChipsRef.current.map((c, index) => [c.id, index])
    );
    const fileIds = pending
      .slice()
      .sort(
        (a, b) =>
          (chipOrder.get(a.chipId) ?? Number.MAX_SAFE_INTEGER) -
          (chipOrder.get(b.chipId) ?? Number.MAX_SAFE_INTEGER)
      )
      .map((p) => p.documentId);

    const typedCommand =
      openedAt !== null &&
      lastSentAtRef.current !== null &&
      lastSentAtRef.current >= openedAt &&
      lastSentMessageRef.current.trim().length > 0
        ? lastSentMessageRef.current
        : null;

    // ADR-015: structural signals only — never file ids or command text.
    console.log(
      "[ConversationPane] document-uploaded event dispatch — files:%d typedCommand:%s",
      fileIds.length,
      typedCommand !== null
    );

    const eventFailureMessage =
      "Sorry — I couldn't process those files automatically. You can still ask me about them.";

    void runDocumentUploadedEvent({
      bffBaseUrl,
      sessionId,
      fileIds,
      typedCommand,
      getAccessToken,
      handlers: {
        onClassification: (data) => {
          enqueueAssistantMessage(
            makeLocalAssistantMessage(formatClassificationMessage(data))
          );
        },
        onOutput: (data) => {
          enqueueAssistantMessage(
            makeLocalAssistantMessage(formatEventOutputMarkdown(data.payload))
          );
        },
        onConfirmation: (data) => {
          if (typeof data.message === "string" && data.message.length > 0) {
            enqueueAssistantMessage(makeLocalAssistantMessage(data.message));
          }
        },
        onNotice: (data) => {
          enqueueAssistantMessage(
            makeLocalAssistantMessage(formatNoticeMessage(data))
          );
        },
        onChips: (raw) => {
          // Chips are conversation-surface UI — same tolerant parse + render
          // path as the Click-path consumer_chips context_event above.
          // G-P1 Defect-1 fix: a non-empty chip set REPLACES the strip; an
          // empty/malformed payload never clears previously-rendered chips
          // (chips vanish only on click consumption or session change).
          const parsed = parseConsumerChips(raw);
          if (parsed.length > 0) {
            setConsumerChips(parsed);
          }
        },
        onError: (message) => {
          // Server `error` content is the safe message by contract (same
          // shape existing chat streams render).
          enqueueAssistantMessage(
            makeLocalAssistantMessage(message || eventFailureMessage)
          );
        },
      },
    }).catch(() => {
      enqueueAssistantMessage(makeLocalAssistantMessage(eventFailureMessage));
    });
  }, [bffBaseUrl, getAccessToken, enqueueAssistantMessage]);

  // Ref indirection: chip-lifecycle callbacks declared ABOVE (membership /
  // removal / failure seams) trigger the fire through this ref.
  fireEventBatchRef.current = fireDocumentUploadedEvent;

  /**
   * queueDocumentUploadedEvent — called once per successful per-file
   * `/documents` promotion (202) with the server-issued documentId. Settles
   * the chip in the count-complete gesture batch (G-P1 Defect-2 fix): the
   * Event endpoint fires exactly ONCE per attach gesture, the moment ALL
   * expected chips have settled. A 30 s fallback (anchored at the first
   * settled promotion) bounds stuck promotions.
   */
  const queueDocumentUploadedEvent = React.useCallback(
    (chipId: string, documentId: string): void => {
      pendingEventFilesRef.current.push({ chipId, documentId });
      // Defensive membership: a promotion arriving for an already-fired batch
      // (e.g. retry succeeding late) re-opens a fresh single-file batch.
      eventAccountedChipIdsRef.current.delete(chipId);
      eventBatchExpectedRef.current.add(chipId);
      if (eventBatchOpenedAtRef.current === null) {
        eventBatchOpenedAtRef.current = Date.now();
      }
      if (eventBatchTimerRef.current === null) {
        eventBatchTimerRef.current = setTimeout(() => {
          eventBatchTimerRef.current = null;
          // ADR-015: structural signal only.
          console.warn(
            "[ConversationPane] event batch fallback fired — settled:%d expected:%d",
            pendingEventFilesRef.current.length,
            eventBatchExpectedRef.current.size
          );
          fireEventBatchRef.current();
        }, EVENT_BATCH_FALLBACK_MS);
      }
      maybeFireEventBatch();
    },
    [maybeFireEventBatch]
  );

  /**
   * Chip click → dispatchConsumer(chip.bindingId, args). The chip's
   * prefill_slots forward verbatim as capability args; the empty-attachments
   * Click precondition is enforced both here (disabled chip UI in
   * ConsumerChips) and inside the helper (throws before any network call).
   * Failures surface as a local Assistant message (ADR-019: stable
   * error text only — never raw server detail).
   */
  const handleConsumerChipClick = React.useCallback(
    (chip: ConsumerChip): void => {
      // Single dispatch decision per turn: consume the chip set on click.
      setConsumerChips([]);

      // ADR-015: log structural signals only — never the label/binding values.
      console.log("[ConversationPane] consumer chip dispatched");

      void dispatchConsumer(chip.bindingId, {
        slots: chip.prefillSlots,
        requiresAttachments: chip.requiresAttachments,
        // Session-level count (manifest-promoted ∪ composer-ready) — the
        // composer strip alone empties on stream completion (round-2 fix).
        attachmentCount: sessionAttachmentCount,
      })
        .then((dispatched) => {
          // G-P1 Defect-1 fix (2026-07-05): render the dispatched capability's
          // STORED output in the conversation surface (ADR-040 render-follows-
          // store — `result` IS the ledger payload) and re-arm the chip strip
          // from the stream's next-step chips (the dispatched Binding's
          // sprk_chiptransitions, e.g. summarize → "Summarize again").
          // Previously a chip click rendered nothing in the conversation and
          // permanently emptied the strip.
          if (dispatched.result !== undefined && dispatched.result !== null) {
            enqueueAssistantMessage(
              makeLocalAssistantMessage(formatEventOutputMarkdown(dispatched.result))
            );
          }
          if (dispatched.chips && dispatched.chips.length > 0) {
            setConsumerChips(dispatched.chips);
          }
        })
        .catch(() => {
          setPendingInjection(
            makeLocalAssistantMessage(
              "Sorry — I couldn't run that action. Please try again."
            )
          );
        });
    },
    [sessionAttachmentCount, dispatchConsumer, enqueueAssistantMessage]
  );

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
  /**
   * R6 Pillar 6c / task 095 — trace bridge handler.
   *
   * Receives `context_event` SSE payloads from SprkChat and dispatches each one
   * verbatim to the `context` PaneEventBus channel where ExecutionTraceWidget
   * renders it. The payload carries the BFF ContextEventEmitter's 6 typed
   * sub-shapes (tool_call_started, tool_call_completed, knowledge_retrieved,
   * playbook_node_executing, playbook_node_completed, decision_made) discriminated
   * by `data.contextEventType`.
   *
   * ADR-015: log STRUCTURAL signals (event type discriminant) only — NEVER any
   * of the typed field values (toolName, decisionId, etc.) which carry session
   * identifiers. The widget renders the typed fields with its own
   * tier-1-safe discipline.
   * ADR-030: additive event types on the existing `context` channel — no new
   * channel introduced.
   */
  const handleContextEvent = React.useCallback(
    (data: {
      contextEventType?: string;
      contextTimestamp?: string;
      contextToolName?: string;
      contextDecisionId?: string;
      contextOutcome?: string;
      contextDurationMs?: number;
      contextKnowledgeSourceId?: string;
      contextRelevanceScore?: number;
      contextResultCount?: number;
      contextPlaybookId?: string;
      contextNodeId?: string;
      contextNodeType?: string;
      contextLayer?: string;
      contextDecision?: string;
      contextCapabilityName?: string;
      contextChips?: ReadonlyArray<Record<string, unknown>>;
    }): void => {
      const eventType = data.contextEventType;
      if (!eventType) return;

      // ADR-015 telemetry: log discriminant only — never typed-field values.
      console.log("[ConversationPane] context_event received — type:%s", eventType);

      // task 023 / FR-P1-04 — Click-path chips (task-022 chip SSE contract).
      // Chips are conversation-surface UI (rendered by <ConsumerChips>), not a
      // bus payload — handled locally; tolerant parse never throws.
      if (eventType === "consumer_chips") {
        // G-P1 Defect-1 fix: replace-only-when-non-empty — a chip-less carrier
        // must never blank an already-rendered next-step strip.
        const parsedChips = parseConsumerChips(data.contextChips);
        if (parsedChips.length > 0) {
          setConsumerChips(parsedChips);
        }
        return;
      }

      const timestamp = data.contextTimestamp ?? new Date().toISOString();

      // Map the SSE payload to the matching ContextPaneEvent discriminated union
      // declared in @spaarke/ai-widgets/events/PaneEventTypes (R6 task 059).
      // We use `dispatch('context', ...)` so any payload field omission is
      // caught by the TS discriminant; widgets that don't recognise the type
      // ignore the event per their own switch (additive-discriminant
      // ADR-030 invariant).
      switch (eventType) {
        case "tool_call_started":
          dispatch("context", {
            type: "tool_call_started",
            timestamp,
            toolName: data.contextToolName ?? "",
            decisionId: data.contextDecisionId ?? "",
          } as ContextPaneEvent);
          break;
        case "tool_call_completed":
          dispatch("context", {
            type: "tool_call_completed",
            timestamp,
            toolName: data.contextToolName ?? "",
            decisionId: data.contextDecisionId ?? "",
            outcome: data.contextOutcome ?? "",
            durationMs: data.contextDurationMs ?? 0,
          } as ContextPaneEvent);
          break;
        case "knowledge_retrieved":
          dispatch("context", {
            type: "knowledge_retrieved",
            timestamp,
            knowledgeSourceId: data.contextKnowledgeSourceId ?? "",
            relevanceScore: data.contextRelevanceScore ?? 0,
            resultCount: data.contextResultCount ?? 0,
          } as ContextPaneEvent);
          break;
        case "playbook_node_executing":
          dispatch("context", {
            type: "playbook_node_executing",
            timestamp,
            playbookId: data.contextPlaybookId ?? "",
            nodeId: data.contextNodeId ?? "",
            nodeType: data.contextNodeType ?? "",
          } as ContextPaneEvent);
          break;
        case "playbook_node_completed":
          dispatch("context", {
            type: "playbook_node_completed",
            timestamp,
            playbookId: data.contextPlaybookId ?? "",
            nodeId: data.contextNodeId ?? "",
            durationMs: data.contextDurationMs ?? 0,
          } as ContextPaneEvent);
          break;
        case "decision_made":
          dispatch("context", {
            type: "decision_made",
            timestamp,
            layer: data.contextLayer ?? "",
            decision: data.contextDecision ?? "",
            capabilityName: data.contextCapabilityName,
          } as ContextPaneEvent);
          break;
        default:
          // Unknown discriminant — defensive ignore per ADR-030 additive policy.
          return;
      }
    },
    [dispatch],
  );

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

  // R7 task 094 / FR-18 — keep `openLibraryModalRef` in lock-step with the
  // useCallback above so the `/playbooks` hard slash dispatched from
  // `hardSlashContext` (declared earlier in this body) can invoke the live
  // implementation. The useCallback has empty deps so the assignment is
  // effectively once-per-component-instance, but using useEffect keeps the
  // assignment explicit + survives any future ref-identity changes.
  React.useEffect(() => {
    openLibraryModalRef.current = handleOpenLibraryModal;
    return () => {
      openLibraryModalRef.current = null;
    };
  }, [handleOpenLibraryModal]);

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
  /**
   * onSessionStale — SprkChat reports the resumed `initialSessionId` is no
   * longer valid on the server (Redis TTL expired, environment rebuild, etc.).
   *
   * R7 Wave 12.3 Phase 12.3a UAT fix (2026-07-03). Clears persisted localStorage
   * BEFORE SprkChat creates a fresh session so parallel widgets don't briefly
   * read the stale id. `handleSessionCreated` below then re-populates it with
   * the newly-created id from SprkChat's `onSessionCreated` callback.
   *
   * ADR-015: structural log only (id count, no id content — the id is opaque
   * but by convention we don't log identifiers verbatim in production paths).
   */
  const handleSessionStale = React.useCallback(
    (_staleSessionId: string): void => {
      console.warn('[ConversationPane] chat session stale — clearing persisted id, awaiting fresh session');
      clearChatSession();
    },
    [clearChatSession]
  );

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
        // task 023 / FR-P1-04: next-step chips are session-scoped — a fresh
        // session must not render the prior session's Binding transitions.
        setConsumerChips([]);
        if (readyConfirmationTimerRef.current !== null) {
          clearTimeout(readyConfirmationTimerRef.current);
          readyConfirmationTimerRef.current = null;
        }
        // task 022b / FR-P1-03: promoted document ids are session-scoped —
        // a queued Event batch must not fire against a different session.
        // The batch-opened timestamp AND the expected-chip membership are
        // KEPT: in the cold-start gesture (attach → type → first send creates
        // the session) promotion runs AFTER this callback, the count-complete
        // batch still needs its membership, and the accompanying typed
        // command must still resolve against the pre-session batch-open
        // instant.
        pendingEventFilesRef.current = [];
        eventBatchFailedChipIdsRef.current = new Set();
        if (eventBatchTimerRef.current !== null) {
          clearTimeout(eventBatchTimerRef.current);
          eventBatchTimerRef.current = null;
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
  /**
   * R7 Wave 12.3 Phase 12.3a UAT fix (2026-07-03) — auto-promote ready chips.
   *
   * Pre-2026-07-03, the ONLY code that POSTed to `/api/ai/chat/sessions/{id}/documents`
   * (which populates server-side `session.UploadedFiles`) was the retired R5
   * per-capability summarize orchestrator's step-1 file-promotion phase —
   * invoked from `handleBeforeSendMessage`'s NL summarize branch. When that
   * branch was retired (Wave 12.3 D-13 convergence), file promotion was lost
   * with it: chips would show 'ready' after client-side extraction, but the
   * BFF never saw them.
   *
   * This effect closes the gap by promoting each ready chip once, as soon as both
   * (a) a chat session id exists and (b) we have a File-ref stored in `heldFilesRef`.
   * `promotedChipIds` guards against re-promotion; `pendingPromotionIdsRef` guards
   * against concurrent effect runs racing on the same chip while an upload is in
   * flight.
   *
   * ADR-015: log ONLY chip id + filename + status code — never the extracted text or
   * arbitrary error message (server ProblemDetails carries errorCode which is safe).
   * ADR-028: uses `authenticatedFetch` — no bare fetch + Authorization header.
   */
  const pendingPromotionIdsRef = React.useRef<Set<string>>(new Set());
  // R7 Wave 12.3 Phase 12.3a UAT hardening (2026-07-04 — following Schedule 13A.pdf
  // silent-failure incident). Attempt counter for retry-on-fail (max 2 attempts).
  const promotionAttemptCountRef = React.useRef<Map<string, number>>(new Map());

  React.useEffect(() => {
    if (chatSessionId === null) return;

    // 2026-07-04 UAT hardening: log every effect run so we can see WHY a chip is
    // skipped (not-ready / already-promoted / pending / missing heldFile). ADR-015
    // tier-1 safe — filenames + status codes + session-id-len only.
    const skipReasons = attachmentChips.map(c => {
      if (c.status !== "ready") return { id: c.id, name: c.filename, skip: `status=${c.status}` };
      if (promotedChipIds.has(c.id)) return { id: c.id, name: c.filename, skip: "already-promoted" };
      if (pendingPromotionIdsRef.current.has(c.id)) return { id: c.id, name: c.filename, skip: "pending" };
      if (!heldFilesRef.current.has(c.filename)) return { id: c.id, name: c.filename, skip: "no-heldFile" };
      return { id: c.id, name: c.filename, skip: null as string | null };
    });
    const eligible = skipReasons.filter(r => r.skip === null);
    if (attachmentChips.length > 0) {
      // Only log when there are chips — reduces noise on empty effect runs.
      console.log(
        "[ConversationPane] auto-promote scan: chips=%d eligible=%d skipped=%o",
        attachmentChips.length,
        eligible.length,
        skipReasons.filter(r => r.skip !== null).map(r => ({ file: r.name, why: r.skip }))
      );
    }
    if (eligible.length === 0) return;

    const documentsUrl = `${bffBaseUrl.replace(/\/$/, "")}/api/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/documents`;
    // Retry policy: max 2 total attempts (1 initial + 1 retry) with 1s backoff.
    // Transient failures we want to survive: token race, brief network hiccup.
    // Permanent failures (413 payload too large, 422 wrong MIME) still fail after
    // retry, but at least both attempts are visible in the console + App Insights.
    const MAX_ATTEMPTS = 2;
    const RETRY_DELAY_MS = 1000;

    // G-P1 UAT round-1 Defect-2/3 hardening (2026-07-05): promotions run
    // SEQUENTIALLY, not in parallel. Each /documents handler read-modify-writes
    // the session manifest (UploadedFiles append + UpdateSessionCacheAsync);
    // parallel POSTs from one gesture raced last-writer-wins and could drop a
    // concurrently-added file from the manifest — the observed "No uploaded
    // files were available yet" notice. Serializing the client's own uploads
    // removes the primary race; the server-side manifest readiness probe
    // (EventRulesService) covers residual propagation lag.
    //
    // All eligible chips are marked pending UP FRONT so an overlapping effect
    // run (attachmentChips changing mid-sequence) cannot double-start one.
    const queue: Array<{ chipId: string; chipFilename: string; attemptNumber: number }> = [];
    for (const { id: chipId, name: chipFilename } of eligible) {
      if (!heldFilesRef.current.has(chipFilename)) continue; // defensive re-check
      pendingPromotionIdsRef.current.add(chipId);
      const attemptNumber = (promotionAttemptCountRef.current.get(chipId) ?? 0) + 1;
      promotionAttemptCountRef.current.set(chipId, attemptNumber);
      queue.push({ chipId, chipFilename, attemptNumber });
    }

    const promoteOne = async (
      chipId: string,
      chipFilename: string,
      attemptNumber: number
    ): Promise<void> => {
      const heldFile = heldFilesRef.current.get(chipFilename);
      if (!heldFile) {
        pendingPromotionIdsRef.current.delete(chipId);
        return;
      }
      {
        try {
          console.log(
            "[ConversationPane] /documents promote attempt %d/%d — filename:%s heldFileSize:%d heldFileType:%s",
            attemptNumber,
            MAX_ATTEMPTS,
            chipFilename,
            heldFile.size,
            heldFile.type
          );
          const form = new FormData();
          form.append("file", heldFile, heldFile.name);
          const response = await authenticatedFetch(documentsUrl, {
            method: "POST",
            body: form,
          });
          if (!response.ok) {
            console.error(
              "[ConversationPane] /documents promote failed — attempt:%d status:%d filename:%s",
              attemptNumber,
              response.status,
              chipFilename
            );
            // Retry on 5xx or 0 (network) — do NOT retry on 4xx (client-side failure
            // like 413 payload too large won't succeed on retry).
            const shouldRetry = attemptNumber < MAX_ATTEMPTS && (response.status >= 500 || response.status === 0);
            if (shouldRetry) {
              await new Promise(resolve => setTimeout(resolve, RETRY_DELAY_MS));
              // Clear pending flag so the next effect run picks it up; keep the
              // attempt count so we don't loop indefinitely.
              pendingPromotionIdsRef.current.delete(chipId);
              return;
            }
            // Permanent failure — settle the chip out of the Event gesture
            // batch so count-complete does not wait for it (G-P1 Defect 2).
            markEventFilePromotionFailed(chipId);
            return;
          }
          // Success — mark chip promoted so subsequent effect runs skip it.
          console.log(
            "[ConversationPane] /documents promote OK — attempt:%d filename:%s",
            attemptNumber,
            chipFilename
          );
          promotionAttemptCountRef.current.delete(chipId);
          setPromotedChipIds(prev => {
            if (prev.has(chipId)) return prev;
            const next = new Set(prev);
            next.add(chipId);
            return next;
          });
          // task 022b (FR-P1-03): the 202 body is DocumentUploadResponse —
          // its documentId is the session-file id the Event endpoint's
          // `fileIds` contract requires. Queue it into the batch coalescer;
          // the batch fires the document-uploaded Event ONCE after the last
          // promotion (+250 ms). Tolerant parse: a missing/malformed body
          // skips the Event leg for this file (promotion itself succeeded).
          try {
            const uploadJson = (await response.json()) as { documentId?: string };
            const documentId =
              typeof uploadJson?.documentId === "string" && uploadJson.documentId.length > 0
                ? uploadJson.documentId
                : null;
            if (documentId !== null) {
              queueDocumentUploadedEvent(chipId, documentId);
            } else {
              // No documentId — Event leg skipped; settle the chip so the
              // count-complete batch does not wait for it.
              markEventFilePromotionFailed(chipId);
            }
          } catch {
            // Non-JSON body — promotion stands; Event leg skipped for this file.
            markEventFilePromotionFailed(chipId);
          }
        } catch (err) {
          // ADR-015: log error name + basic classification only, never the message
          // (may contain URLs / headers). "Failed to fetch" is a TypeError from the
          // browser fetch API; use it to distinguish transport failure from server rejection.
          const errName = err instanceof Error ? err.name : "unknown";
          const errKind = err instanceof TypeError ? "network-or-cors" : errName;
          console.error(
            "[ConversationPane] /documents promote threw — attempt:%d filename:%s errName:%s errKind:%s",
            attemptNumber,
            chipFilename,
            errName,
            errKind
          );
          const shouldRetry = attemptNumber < MAX_ATTEMPTS && errKind === "network-or-cors";
          if (shouldRetry) {
            await new Promise(resolve => setTimeout(resolve, RETRY_DELAY_MS));
            pendingPromotionIdsRef.current.delete(chipId);
            return;
          }
          // Permanent transport failure — settle the chip out of the batch.
          markEventFilePromotionFailed(chipId);
        } finally {
          pendingPromotionIdsRef.current.delete(chipId);
        }
      }
    };

    void (async () => {
      for (const item of queue) {
        await promoteOne(item.chipId, item.chipFilename, item.attemptNumber);
      }
    })();
  }, [chatSessionId, attachmentChips, promotedChipIds, authenticatedFetch, bffBaseUrl, queueDocumentUploadedEvent, markEventFilePromotionFailed]);

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

      // R5 task 036: capture the File so the auto-promote effect (R7 12.3a)
      // can POST multipart binary to `/api/ai/chat/sessions/{id}/documents`.
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

          {/* ── SprkChat — fills remaining height ── */}
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
              // R7 Wave 12.3 Phase 12.3a UAT fix (2026-07-03) — pair with the new
              // resume-not-recreate session flow. When SessionRestoreManager has
              // populated the restore context with prior conversation messages
              // (AIPU2-106), forward them so the chat thread shows recovered
              // history immediately without waiting for a server round-trip.
              // Empty array is the safe default — SprkChat treats undefined
              // the same as an empty history.
              initialMessages={restoredInitialMessages}
              playbookId={playbookId}
              onSessionCreated={handleSessionCreated}
              // R7 Wave 12.3 Phase 12.3a UAT fix (2026-07-03) — clear the
              // persisted chatSessionId when SprkChat resumed a stale id.
              onSessionStale={handleSessionStale}
              // ── Click-path next-step chips (task 023 / FR-P1-04 / ADR-039) ──
              // G-P1 UAT round-2 fix (2026-07-06): the strip renders ABOVE THE
              // INPUT ZONE (below the transcript) via SprkChat's aboveInputSlot
              // — round-2 found it stranded at the top of the pane, detached
              // from the conversation flow. Chips carry binding_id from the
              // completed Binding's chip transitions; a click dispatches
              // through the ONE shared dispatchConsumer helper. Attachment-
              // requiring chips gate on SESSION files (manifest-promoted ∪
              // composer-ready) — not the composer chip strip alone, which
              // SprkChat clears on stream completion (FR-07) even though the
              // session manifest still holds the files. ADR-021: Fluent v9
              // tokens only.
              aboveInputSlot={
                <ConsumerChips
                  chips={consumerChips}
                  attachmentCount={sessionAttachmentCount}
                  onChipClick={handleConsumerChipClick}
                />
              }
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
              // R6 Pillar 6c / task 095 — trace bridge. Forward each
              // context_event SSE payload to the `context` PaneEventBus channel
              // so ExecutionTraceWidget renders in real time. ADR-015: payload
              // already tier-1 safe by BFF construction.
              onContextEvent={handleContextEvent}
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
