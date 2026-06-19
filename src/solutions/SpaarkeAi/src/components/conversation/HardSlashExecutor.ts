/**
 * HardSlashExecutor.ts — R6 task 081 / D-D-02 (Pillar 8 hard-slash executor).
 *
 * Deterministic dispatcher for the SIX hard-slash commands defined by Q6:
 *
 *   `/clear`            — clear current session conversation history
 *                         (frontend state + DELETE backend session)
 *   `/new-session`      — end current session, mint new session id,
 *                         reset PaneEventBus state
 *   `/help`             — open the in-place help panel (CommandHelpPanel)
 *   `/export`           — download conversation history as markdown blob
 *   `/save-to-matter`   — persist a conversation summary as a pinned memory
 *                         (matter-fact) via existing `/api/memory/pins`
 *   `/pin`              — pin the currently-focused workspace tab via the
 *                         existing PATCH `/api/ai/chat/sessions/{sessionId}/tabs`
 *
 * Per spec FR-49 + Phase D exit criterion 2:
 *   - Bypasses the LLM entirely. ZERO Azure OpenAI calls.
 *   - <100ms p99 latency. UI commands are pure-frontend; persistence
 *     commands (`/save-to-matter`, `/pin`) await a single short BFF call.
 *
 * Per ADR-013: this module touches only EXISTING BFF endpoints.
 *   - DELETE  `/api/ai/chat/sessions/{sessionId}`           (existing)
 *   - POST    `/api/ai/chat/sessions`                       (existing)
 *   - POST    `/api/memory/pins`                            (existing — task 070-A)
 *   - PATCH   `/api/ai/chat/sessions/{sessionId}/tabs`      (existing — task 065)
 * No new BFF surface; ADR-029 publish-size delta = 0 MB.
 *
 * Per ADR-015: telemetry emission uses `logTelemetryError` (event-only sink).
 *   - We log COMMAND NAME + DECISION + TIMESTAMP. We NEVER log the user's
 *     raw input text. The `intent.rawText` is consumed for execution but
 *     never serialised to telemetry.
 *
 * Per ADR-030: NO new PaneEventBus channel is introduced. The executor only
 *   reuses existing additive event types on the `workspace` channel
 *   (`session_reset`, `tab_edited`).
 *
 * Per ADR-031: NO new shell stage. Hard slashes operate inside the existing
 *   4-stage lifecycle by reusing the existing `session_reset` event.
 *
 * Per NFR-01 / NFR-11: this module is invoked ONLY when
 * `Intent.isHardSlash === true`. Soft slashes and natural language continue
 * to flow through the existing `CapabilityRouter` path unchanged.
 *
 * This module is FRONTEND-ONLY. Zero .cs touched.
 *
 * @see CommandRouter.ts — produces the `Intent` consumed here
 * @see ConversationPane.tsx — integrates via `executeHardSlash(intent, ctx)`
 * @see CommandHelpPanel.tsx — the panel surface opened by `/help`
 * @see projects/spaarke-ai-platform-unification-r6/spec.md FR-48..FR-54
 */

import {
  buildBffApiUrl,
  type AuthenticatedFetchFn,
} from '@spaarke/auth';
import type { PaneEventBus } from '@spaarke/ai-widgets/events';

import type { Intent } from './CommandRouter';
import { HardSlashes } from './CommandRouter';

// ---------------------------------------------------------------------------
// Telemetry event-name constants (ADR-015 — name + decision + timestamp only)
// ---------------------------------------------------------------------------

/**
 * Hard-slash invocation telemetry. The event-name prefix matches the
 * convention enforced by `errorTelemetry.ts` (per FR-24 the helper also
 * accepts non-error names; we reuse the sink because Pillar 8 explicitly
 * routes through the existing telemetry channel per ADR-015).
 */
export const TELEMETRY_HARD_SLASH_INVOKED =
  'spaarke-ai-hard-slash.invoked';

/**
 * Emitted when a hard slash fails (network, validation, persistence). The
 * `error` payload field carries a SAFE error code (e.g. `"network"`,
 * `"missing-matter-id"`, `"no-focused-tab"`) — never the user's raw text.
 */
export const TELEMETRY_HARD_SLASH_FAILED =
  'spaarke-ai-hard-slash.failed';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Outcome enum returned by `executeHardSlash`. The caller (ConversationPane)
 * uses this to decide whether to:
 *   - swallow the user message (the deterministic action is the WHOLE turn)
 *   - show a transient inline assistant chip
 *   - surface an error toast
 */
export type ExecutorOutcome =
  | 'executed'             // command completed successfully
  | 'executed-async'       // command kicked off an awaitable async path that's still in-flight
  | 'failed-validation'    // caller-side precondition unmet (e.g. no matter id, no focused tab)
  | 'failed-network'       // BFF call failed
  | 'failed-unknown';      // unexpected exception (caught + logged)

/**
 * Structured result the caller consumes. `message` is a short
 * user-facing string suitable for an inline assistant chip; `null`
 * when the command needs no acknowledgement (e.g. `/clear` already
 * empties the surface).
 */
export interface ExecutorResult {
  outcome: ExecutorOutcome;
  /** Short user-facing message (may be null on no-ack commands). */
  message: string | null;
  /** Stable error code if outcome is a failure; else undefined. */
  errorCode?: string;
}

/**
 * Telemetry sink interface — injected so the executor stays trivially
 * testable. The default production sink wraps the existing App Insights
 * helper from `errorTelemetry.ts`; tests pass a mock.
 *
 * Per ADR-015: `properties` MUST NOT contain raw user input text. Callers
 * pass only command + outcome + stable codes.
 */
export interface TelemetrySink {
  emit: (eventName: string, properties: Record<string, unknown>) => void;
}

/**
 * Conversation history entry shape consumed by `/export`. Deliberately
 * loose — the executor only needs `role` + `content` + `timestamp` to build
 * the markdown export. The host (ConversationPane) maps its native message
 * shape into this slim DTO at the call site.
 */
export interface ConversationMessage {
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp?: string; // ISO-8601
}

/**
 * Function the caller provides for `/help` to surface the CommandHelpPanel.
 * The caller owns the open/closed state via `useState`; the executor calls
 * `setHelpOpen(true)` and returns immediately.
 */
export type SetHelpOpenFn = (open: boolean) => void;

/**
 * Function the caller provides for `/clear` and `/new-session` to clear
 * the local conversation surface BEFORE the executor signals the backend.
 * The host clears `conversationMessages` state, attachment chips, etc.
 */
export type ClearLocalConversationFn = () => void;

/**
 * Function the caller provides for `/new-session` to mint and store a new
 * session id after the executor signals backend reset. The host updates
 * `chatSessionId` state.
 */
export type CreateNewSessionFn = () => Promise<string | null>;

/**
 * Function the caller provides for `/save-to-matter` to access the current
 * conversation history (used to build the pinned-memory content).
 */
export type GetConversationHistoryFn = () => ConversationMessage[];

/**
 * Function the caller provides for `/pin` to retrieve the currently-focused
 * workspace tab id. Returns null if no tab is focused.
 */
export type GetFocusedTabIdFn = () => string | null;

/**
 * Function the caller provides for `/export` so the executor can trigger
 * a browser-side file download. Default production impl uses
 * `URL.createObjectURL` + `<a download>`; tests pass a spy.
 */
export type DownloadBlobFn = (blob: Blob, filename: string) => void;

/**
 * Context bag the executor needs. All side-effect surfaces are injected so
 * the module stays trivially unit-testable. The ConversationPane assembles
 * this object once per render via `React.useMemo`.
 *
 * EVERY field is required at the type level — callers pass no-op stubs for
 * commands they aren't actively wiring (e.g. an evaluation harness can pass
 * `() => null` for `getFocusedTabId`). This keeps the executor's body free
 * of optional-chaining noise.
 */
export interface ExecutorContext {
  /** BFF base URL — from `useAiSession()` in the host. */
  bffBaseUrl: string;
  /** Auth-tagged fetch from `useAiSession()` (ADR-028 §H-4). */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Current chat session id (null when no session has been created yet). */
  sessionId: string | null;
  /** PaneEventBus instance for dispatching workspace events. */
  paneEventBus: PaneEventBus;
  /** Open/close the CommandHelpPanel. */
  setHelpOpen: SetHelpOpenFn;
  /** Wipe local conversation state (messages, chips, pending injections). */
  clearLocalConversation: ClearLocalConversationFn;
  /** Mint a new session via POST /api/ai/chat/sessions. */
  createNewSession: CreateNewSessionFn;
  /** Snapshot of the conversation history (consumed by /export + /save-to-matter). */
  getConversationHistory: GetConversationHistoryFn;
  /** Focused workspace tab id (consumed by /pin). */
  getFocusedTabId: GetFocusedTabIdFn;
  /** Active matter context (consumed by /save-to-matter when no matterId arg). */
  activeMatterId: string | null;
  /** Trigger a browser-side blob download (consumed by /export). */
  downloadBlob: DownloadBlobFn;
  /** Telemetry sink (production: errorTelemetry; tests: mock). */
  telemetry: TelemetrySink;
}

// ---------------------------------------------------------------------------
// Type guards / helpers
// ---------------------------------------------------------------------------

/**
 * Read-only set of recognised hard-slash commands. Re-exported from
 * `CommandRouter.ts` HARD_SLASHES for callers that want to introspect the
 * vocabulary (e.g. CommandHelpPanel).
 */
const HARD_SLASH_SET: ReadonlySet<string> = new Set(HardSlashes);

/**
 * Narrow `Intent` to a hard-slash invocation. Throws synchronously if the
 * caller passed a soft-slash or natural-language Intent — guards against
 * mis-wiring in `ConversationPane.tsx`.
 */
function assertHardSlash(intent: Intent): void {
  if (!intent.isHardSlash) {
    throw new Error(
      `HardSlashExecutor invoked with non-hard-slash Intent (command=${String(
        intent.command,
      )}, isHardSlash=${intent.isHardSlash}). Caller MUST branch on intent.isHardSlash.`,
    );
  }
  if (intent.command === null || !HARD_SLASH_SET.has(intent.command)) {
    throw new Error(
      `HardSlashExecutor invoked with unknown hard slash: ${String(intent.command)}`,
    );
  }
}

/**
 * Emit the "invoked" telemetry event. Stable shape — command name + outcome
 * + timestamp ONLY. Never user text.
 */
function emitInvoked(
  telemetry: TelemetrySink,
  command: string,
  outcome: ExecutorOutcome,
  extras: Record<string, unknown> = {},
): void {
  telemetry.emit(TELEMETRY_HARD_SLASH_INVOKED, {
    command,
    outcome,
    timestamp: new Date().toISOString(),
    ...extras,
  });
}

/**
 * Emit the "failed" telemetry event with a stable error code.
 */
function emitFailed(
  telemetry: TelemetrySink,
  command: string,
  errorCode: string,
): void {
  telemetry.emit(TELEMETRY_HARD_SLASH_FAILED, {
    command,
    errorCode,
    timestamp: new Date().toISOString(),
  });
}

// ---------------------------------------------------------------------------
// Per-command executors
// ---------------------------------------------------------------------------

/**
 * `/clear` — wipe local conversation state + signal backend to drop the
 * cached session (DELETE /api/ai/chat/sessions/{sessionId}).
 *
 * Pure-frontend latency is <1ms (state setter). The DELETE call happens
 * in the background; we DO NOT await it — the user sees the surface
 * cleared instantly. Network failure on DELETE is non-fatal (backend
 * caches expire on TTL); we log telemetry and move on.
 */
async function execClear(ctx: ExecutorContext): Promise<ExecutorResult> {
  // 1) Wipe local state — instant.
  ctx.clearLocalConversation();

  // 2) Background DELETE to drop the backend session cache. Fire-and-forget.
  if (ctx.sessionId !== null && ctx.sessionId.length > 0) {
    const url = buildBffApiUrl(
      ctx.bffBaseUrl,
      `/api/ai/chat/sessions/${encodeURIComponent(ctx.sessionId)}`,
    );
    // Intentionally fire-and-forget — we don't block the user's perception of
    // "instant clear" on network round-trip. Errors are logged via telemetry.
    void ctx.authenticatedFetch(url, { method: 'DELETE' }).catch((err) => {
      const code = err instanceof Error ? 'network' : 'unknown';
      emitFailed(ctx.telemetry, '/clear', code);
    });
  }

  emitInvoked(ctx.telemetry, '/clear', 'executed');
  return { outcome: 'executed', message: null };
}

/**
 * `/new-session` — end current session, mint a new id, reset PaneEventBus
 * state via the existing `workspace.session_reset` event.
 *
 * Calls `createNewSession()` which POSTs `/api/ai/chat/sessions` and returns
 * the new session id. Local conversation surface is cleared in the same beat.
 */
async function execNewSession(ctx: ExecutorContext): Promise<ExecutorResult> {
  // Clear local conversation state instantly so the user sees the wipe.
  ctx.clearLocalConversation();

  // Dispatch session_reset on workspace channel — existing additive event
  // type (no new channel per ADR-030). WorkspacePane subscribers will reset
  // tabs back to Stage 1.
  ctx.paneEventBus.dispatch('workspace', {
    type: 'session_reset',
  });

  // Mint a new session via the existing endpoint. The host's
  // createNewSession returns the new session id (or null on failure).
  let newSessionId: string | null = null;
  try {
    newSessionId = await ctx.createNewSession();
  } catch {
    emitFailed(ctx.telemetry, '/new-session', 'network');
    emitInvoked(ctx.telemetry, '/new-session', 'failed-network');
    return {
      outcome: 'failed-network',
      message: 'Could not start a new session — please try again.',
      errorCode: 'network',
    };
  }

  if (newSessionId === null) {
    emitFailed(ctx.telemetry, '/new-session', 'unknown');
    emitInvoked(ctx.telemetry, '/new-session', 'failed-unknown');
    return {
      outcome: 'failed-unknown',
      message: 'Could not start a new session — please try again.',
      errorCode: 'unknown',
    };
  }

  emitInvoked(ctx.telemetry, '/new-session', 'executed');
  return {
    outcome: 'executed',
    message: 'New session started.',
  };
}

/**
 * `/help` — open the CommandHelpPanel. Pure UI; no backend call.
 *
 * The CommandHelpPanel is mounted as a sibling of the chat surface; the
 * host owns its open/closed state via `useState`. The executor simply
 * flips it open. <1ms latency.
 */
async function execHelp(ctx: ExecutorContext): Promise<ExecutorResult> {
  ctx.setHelpOpen(true);
  emitInvoked(ctx.telemetry, '/help', 'executed');
  return { outcome: 'executed', message: null };
}

/**
 * `/export` — serialise the conversation history as markdown and trigger
 * a browser-side download via `downloadBlob`.
 *
 * Pure-frontend. <5ms for typical conversations (<200 messages). No
 * network call. The markdown format is deliberately simple — easy to read
 * + easy to round-trip into other tools.
 */
async function execExport(ctx: ExecutorContext): Promise<ExecutorResult> {
  const history = ctx.getConversationHistory();

  if (history.length === 0) {
    emitInvoked(ctx.telemetry, '/export', 'failed-validation', {
      reason: 'empty-history',
    });
    return {
      outcome: 'failed-validation',
      message: 'There is no conversation to export yet.',
      errorCode: 'empty-history',
    };
  }

  const markdown = serializeConversationMarkdown(history, ctx.sessionId);
  const blob = new Blob([markdown], { type: 'text/markdown;charset=utf-8' });
  const filename = buildExportFilename(ctx.sessionId);

  ctx.downloadBlob(blob, filename);

  emitInvoked(ctx.telemetry, '/export', 'executed', {
    messageCount: history.length,
  });
  return {
    outcome: 'executed',
    message: `Exported ${history.length} message${history.length === 1 ? '' : 's'}.`,
  };
}

/**
 * `/save-to-matter [matterId]` — persist a conversation summary to the
 * matter via the EXISTING `/api/memory/pins` endpoint (task 070-A).
 *
 * The matter id resolves in this order:
 *   1. Explicit positional arg in `intent.rawText` (e.g.
 *      `/save-to-matter 00000000-0000-0000-0000-000000000001`)
 *   2. Active matter context from `ctx.activeMatterId`
 *
 * Falls with `missing-matter-id` if neither is present. The persisted pin
 * uses `pinType: "matter-fact"` since the conversation is tied to the matter.
 */
async function execSaveToMatter(
  intent: Intent,
  ctx: ExecutorContext,
): Promise<ExecutorResult> {
  // Parse the optional positional argument from rawText. Pattern:
  // `/save-to-matter <id> [optional rest]`. We strip the leading command
  // and take the first whitespace-bounded token.
  const matterIdFromArg = parseFirstArg(intent.rawText, '/save-to-matter');
  const matterId =
    (matterIdFromArg && matterIdFromArg.length > 0
      ? matterIdFromArg
      : ctx.activeMatterId) ?? '';

  if (matterId.length === 0) {
    emitFailed(ctx.telemetry, '/save-to-matter', 'missing-matter-id');
    emitInvoked(ctx.telemetry, '/save-to-matter', 'failed-validation');
    return {
      outcome: 'failed-validation',
      message:
        'No matter to save to — provide a matter id (`/save-to-matter <id>`) or open this chat from a matter.',
      errorCode: 'missing-matter-id',
    };
  }

  const history = ctx.getConversationHistory();
  if (history.length === 0) {
    emitFailed(ctx.telemetry, '/save-to-matter', 'empty-history');
    emitInvoked(ctx.telemetry, '/save-to-matter', 'failed-validation');
    return {
      outcome: 'failed-validation',
      message: 'There is no conversation to save yet.',
      errorCode: 'empty-history',
    };
  }

  // Build the pin payload. `title` + `content` MUST respect the backend
  // caps (200 / 1000 chars) declared in `pinned-memory-contracts.ts`.
  const title = `Conversation — ${new Date().toLocaleDateString()}`.slice(0, 200);
  const content = serializeConversationCompact(history).slice(0, 1000);

  const url = buildBffApiUrl(ctx.bffBaseUrl, '/api/memory/pins');
  let response: Response;
  try {
    response = await ctx.authenticatedFetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        title,
        content,
        pinType: 'matter-fact',
        matterId,
      }),
    });
  } catch {
    emitFailed(ctx.telemetry, '/save-to-matter', 'network');
    emitInvoked(ctx.telemetry, '/save-to-matter', 'failed-network');
    return {
      outcome: 'failed-network',
      message: 'Could not save to matter — network error. Please try again.',
      errorCode: 'network',
    };
  }

  if (!response.ok) {
    emitFailed(ctx.telemetry, '/save-to-matter', `http-${response.status}`);
    emitInvoked(ctx.telemetry, '/save-to-matter', 'failed-network');
    return {
      outcome: 'failed-network',
      message: `Could not save to matter (HTTP ${response.status}).`,
      errorCode: `http-${response.status}`,
    };
  }

  emitInvoked(ctx.telemetry, '/save-to-matter', 'executed');
  return {
    outcome: 'executed',
    message: 'Saved to matter.',
  };
}

/**
 * `/pin` — pin the currently-focused workspace tab via the existing
 * PATCH `/api/ai/chat/sessions/{sessionId}/tabs` endpoint (task 065).
 *
 * The host owns the focused-tab state; we read it via
 * `ctx.getFocusedTabId()`. Failure modes:
 *   - no session  → `failed-validation` ("no-session")
 *   - no focus    → `failed-validation` ("no-focused-tab")
 *   - HTTP error  → `failed-network`
 *
 * On success, we ALSO dispatch the additive `workspace.tab_edited` event so
 * subscribers (trace widget, conflict resolver) observe the change. Per
 * ADR-015 the event carries field NAMES only.
 */
async function execPin(ctx: ExecutorContext): Promise<ExecutorResult> {
  if (ctx.sessionId === null || ctx.sessionId.length === 0) {
    emitFailed(ctx.telemetry, '/pin', 'no-session');
    emitInvoked(ctx.telemetry, '/pin', 'failed-validation');
    return {
      outcome: 'failed-validation',
      message: 'Start a chat session before pinning a tab.',
      errorCode: 'no-session',
    };
  }

  const tabId = ctx.getFocusedTabId();
  if (tabId === null || tabId.length === 0) {
    emitFailed(ctx.telemetry, '/pin', 'no-focused-tab');
    emitInvoked(ctx.telemetry, '/pin', 'failed-validation');
    return {
      outcome: 'failed-validation',
      message: 'Focus a workspace tab to pin it.',
      errorCode: 'no-focused-tab',
    };
  }

  // PATCH endpoint shape: { tabs: [{ tabId, isPinned: true }], activeTabId? }
  // We send a minimal patch — server merges with existing state.
  const url = buildBffApiUrl(
    ctx.bffBaseUrl,
    `/api/ai/chat/sessions/${encodeURIComponent(ctx.sessionId)}/tabs`,
  );

  let response: Response;
  try {
    response = await ctx.authenticatedFetch(url, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        tabs: [{ tabId, isPinned: true }],
      }),
    });
  } catch {
    emitFailed(ctx.telemetry, '/pin', 'network');
    emitInvoked(ctx.telemetry, '/pin', 'failed-network');
    return {
      outcome: 'failed-network',
      message: 'Could not pin the tab — network error.',
      errorCode: 'network',
    };
  }

  if (!response.ok) {
    emitFailed(ctx.telemetry, '/pin', `http-${response.status}`);
    emitInvoked(ctx.telemetry, '/pin', 'failed-network');
    return {
      outcome: 'failed-network',
      message: `Could not pin the tab (HTTP ${response.status}).`,
      errorCode: `http-${response.status}`,
    };
  }

  // Dispatch tab_edited so subscribers update. Field NAMES only (ADR-015).
  ctx.paneEventBus.dispatch('workspace', {
    type: 'tab_edited',
    tabId,
    sessionId: ctx.sessionId,
    editedFields: ['isPinned'],
    timestamp: new Date().toISOString(),
  });

  emitInvoked(ctx.telemetry, '/pin', 'executed');
  return {
    outcome: 'executed',
    message: 'Tab pinned.',
  };
}

// ---------------------------------------------------------------------------
// Public dispatcher
// ---------------------------------------------------------------------------

/**
 * Execute the hard-slash `intent` against `ctx`. Caller MUST have already
 * verified `intent.isHardSlash === true` via CommandRouter.
 *
 * Latency targets (Phase D exit criterion 2):
 *   - `/clear`, `/new-session`, `/help`, `/export` — <100ms p99 (pure
 *     frontend or background-only network).
 *   - `/save-to-matter`, `/pin` — <100ms p99 (single short BFF call;
 *     network jitter accepted but typical p99 well under).
 *
 * The dispatcher catches ANY synchronous or async throw from per-command
 * executors and degrades to `failed-unknown` with telemetry — the
 * conversation pane MUST NOT crash on a bad slash.
 */
export async function executeHardSlash(
  intent: Intent,
  ctx: ExecutorContext,
): Promise<ExecutorResult> {
  try {
    assertHardSlash(intent);
    switch (intent.command) {
      case '/clear':
        return await execClear(ctx);
      case '/new-session':
        return await execNewSession(ctx);
      case '/help':
        return await execHelp(ctx);
      case '/export':
        return await execExport(ctx);
      case '/save-to-matter':
        return await execSaveToMatter(intent, ctx);
      case '/pin':
        return await execPin(ctx);
      default:
        // Defensive: assertHardSlash already rejected this case. Belt-and-
        // braces for runtime safety.
        emitFailed(ctx.telemetry, String(intent.command), 'unknown-command');
        return {
          outcome: 'failed-unknown',
          message: 'Unknown command.',
          errorCode: 'unknown-command',
        };
    }
  } catch (err) {
    // Any uncaught exception inside a per-command executor: degrade
    // gracefully + telemetry. The caller's UI surface stays intact.
    const code = err instanceof Error ? err.name : 'unknown';
    emitFailed(ctx.telemetry, String(intent.command), code);
    return {
      outcome: 'failed-unknown',
      message: 'Something went wrong running that command.',
      errorCode: code,
    };
  }
}

// ---------------------------------------------------------------------------
// Pure helpers (exported for tests)
// ---------------------------------------------------------------------------

/**
 * Parse the first whitespace-bounded argument after `commandPrefix` in
 * `rawText`. Returns null when no argument is present.
 *
 * Example: `parseFirstArg("/save-to-matter MAT-123 #notes.md", "/save-to-matter")`
 *          → `"MAT-123"`
 *
 * Exported for tests. Pure / synchronous.
 */
export function parseFirstArg(
  rawText: string,
  commandPrefix: string,
): string | null {
  const trimmed = rawText.trim();
  if (!trimmed.toLowerCase().startsWith(commandPrefix.toLowerCase())) {
    return null;
  }
  const remainder = trimmed.slice(commandPrefix.length).trim();
  if (remainder.length === 0) return null;
  const firstToken = remainder.split(/\s+/, 1)[0];
  return firstToken.length > 0 ? firstToken : null;
}

/**
 * Build a SAFE filename for `/export` downloads. Uses session id + date.
 *
 * Exported for tests. Pure / synchronous.
 */
export function buildExportFilename(sessionId: string | null): string {
  const dateStamp = new Date().toISOString().slice(0, 10); // yyyy-mm-dd
  // Sanitize sessionId — fall back to "session" if absent/invalid.
  const sid =
    sessionId && sessionId.length > 0 && /^[A-Za-z0-9_\-]+$/.test(sessionId)
      ? sessionId
      : 'session';
  return `spaarke-chat-${sid}-${dateStamp}.md`;
}

/**
 * Serialize the conversation history to a human-readable Markdown document.
 *
 * Layout:
 *   # Spaarke Chat Export
 *   _Session: {sessionId}_
 *   _Exported: {timestamp}_
 *
 *   ## User
 *   {content}
 *
 *   ## Assistant
 *   {content}
 *
 * Exported for tests. Pure / synchronous.
 */
export function serializeConversationMarkdown(
  history: ConversationMessage[],
  sessionId: string | null,
): string {
  const lines: string[] = [];
  lines.push('# Spaarke Chat Export');
  lines.push('');
  lines.push(`_Session: ${sessionId ?? 'unknown'}_`);
  lines.push(`_Exported: ${new Date().toISOString()}_`);
  lines.push('');

  for (const msg of history) {
    const heading =
      msg.role === 'user'
        ? '## User'
        : msg.role === 'assistant'
          ? '## Assistant'
          : '## System';
    lines.push(heading);
    if (msg.timestamp) {
      lines.push(`_${msg.timestamp}_`);
      lines.push('');
    }
    lines.push(msg.content);
    lines.push('');
  }

  return lines.join('\n');
}

/**
 * Compact serializer used by `/save-to-matter` to fit within the
 * 1000-char `content` cap on `PinUpsertRequest`. Truncates per-message
 * content + total length.
 *
 * Exported for tests. Pure / synchronous.
 */
export function serializeConversationCompact(
  history: ConversationMessage[],
): string {
  const PER_MESSAGE_CAP = 200;
  const parts: string[] = [];
  for (const msg of history) {
    const prefix =
      msg.role === 'user' ? 'U:' : msg.role === 'assistant' ? 'A:' : 'S:';
    const body =
      msg.content.length > PER_MESSAGE_CAP
        ? `${msg.content.slice(0, PER_MESSAGE_CAP)}…`
        : msg.content;
    parts.push(`${prefix} ${body}`);
  }
  return parts.join('\n');
}

// ---------------------------------------------------------------------------
// Default telemetry sink (production)
// ---------------------------------------------------------------------------

/**
 * Default production telemetry sink. Wraps `logTelemetryError` so all
 * Pillar 8 telemetry flows through the same App Insights channel as
 * existing FR-24 error telemetry. Per ADR-015: NEVER log raw user text.
 *
 * Imported lazily to avoid pulling `@microsoft/applicationinsights-web`
 * into the hard-slash executor's compile-time graph if the host hasn't
 * already loaded it.
 */
export const defaultTelemetrySink: TelemetrySink = {
  emit(eventName, properties) {
    // Dynamically resolve to keep tree-shaking happy. The actual sink is
    // resolved at call time so tests that don't import this default keep
    // a zero-dep graph.
    try {
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      const mod = require('../../telemetry/errorTelemetry') as typeof import('../../telemetry/errorTelemetry');
      mod.logTelemetryError(eventName, properties);
    } catch {
      // Telemetry is best-effort; never propagate to caller.
    }
  },
};

/**
 * Default browser-side blob downloader. Production hosts use this; tests
 * pass a spy.
 */
export const defaultDownloadBlob: DownloadBlobFn = (blob, filename) => {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  // Revoke on next tick to ensure the download has been initiated.
  setTimeout(() => URL.revokeObjectURL(url), 0);
};
