/**
 * DocumentUploadedEventStream — client leg of the Event entry path (FR-P1-03).
 *
 * ai-architecture-redesign-r1 task 022b / ADR-039 / ADR-040.
 *
 * When an attach gesture completes in the ConversationPane (EVERY chip queued
 * in the gesture has received its `POST /sessions/{id}/documents` 202 —
 * count-complete batching per the G-P1 UAT round-1 fix, 2026-07-05, with a
 * 30 s stuck-promotion fallback), the host calls
 * {@link runDocumentUploadedEvent} ONCE with the batch's session-file
 * document ids (upload order) plus any command the user typed alongside the
 * upload. The SERVER owns every routing decision — rule lookup,
 * explicit-command supersede, opt-out, daily cap, M4 confidence policy
 * (ADR-039: the client never pre-filters; `typedCommand` is passed verbatim
 * and the server decides).
 *
 * SSE consumption: the canonical `readSseStream` + `parseSseEvent` primitives
 * from `@spaarke/ui-components` (hooks/useSseStream.ts) — the ONE SSE parse
 * path client-wide. No hand-rolled reader/parser is introduced here (task
 * 023b invariant).
 *
 * Server contract (task 022 — `EventRuleSseContract.cs`; handoff note
 * `projects/spaarke-ai-architecture-redesign-r1/notes/task-022-event-sse-contract.md`):
 *
 *   POST {bffBaseUrl}/api/ai/chat/sessions/{sessionId}/events/document-uploaded
 *   body: { fileIds: string[], typedCommand: string | null }
 *   → 200 text/event-stream of ChatSseEvent envelopes ({type, content?, data?},
 *     camelCase, `data: {json}\n\n`):
 *       event_classification → {fileId, fileName, docType, confidence, bindingId, ucid, ledgerKey}
 *       event_output         → {bindingId, ucid, ledgerKey, disposition, payload}   (payload = STORED ledger entry, ADR-040)
 *       event_confirmation   → {fileId, docType, confidence, threshold, message, chips}  (M4 gate)
 *       event_notice         → {reason, message, chips?}   (graceful non-execution — never a silent drop)
 *       chips                → {sourceBindingId, chips}    (next-step Click-path chips)
 *       error                → content = safe message      (standard chat error shape)
 *       done                 → terminal
 *   → non-OK: ProblemDetails with stable `errorCode` extension (ADR-019)
 *
 * Chips arrive on THREE carrier events (`chips`, `event_confirmation`,
 * `event_notice`). This module centralizes the carrier logic: every non-empty
 * chip array is forwarded through the single `onChips` handler (raw wire
 * shape — the host parses with the shared `parseConsumerChips` and renders
 * via <ConsumerChips>; clicks flow through the ONE `dispatchConsumer`).
 *
 * @see ADR-039 — grounded execution; Event/Click/Text are the only entry paths
 * @see ADR-040 — ledger-before-render (event_output.payload IS the stored entry)
 * @see ADR-028 — Auth v2; `getAccessToken` re-invoked per stream open by readSseStream
 * @see ADR-019 — ProblemDetails: only stable errorCode strings surface in errors
 */

import { readSseStream, parseSseEvent } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Wire shapes (BFF EventRuleSseContract.cs records, serialized camelCase)
// ---------------------------------------------------------------------------

/** `event_classification` data — a classify member's result. */
export interface EventClassificationData {
  fileId?: string;
  fileName?: string;
  docType?: string;
  /** Calibrated confidence in [0,1]. */
  confidence?: number;
  bindingId?: string;
  ucid?: string | null;
  ledgerKey?: string;
}

/** `event_output` data — a member's STORED ledger output (ADR-040). */
export interface EventOutputData {
  bindingId?: string;
  ucid?: string | null;
  ledgerKey?: string;
  disposition?: string;
  /** The stored entry's payload, verbatim (render follows store). */
  payload?: unknown;
}

/** `event_confirmation` data — the M4 low-confidence gate turn. */
export interface EventConfirmationData {
  fileId?: string;
  docType?: string;
  confidence?: number;
  threshold?: number;
  message?: string;
  /** Confirm/continue chips (Click path) — raw EventChip wire shape. */
  chips?: unknown;
}

/** `event_notice` data — graceful non-execution (never a silent drop). */
export interface EventNoticeData {
  /** Bounded vocabulary: superseded | opted-out | daily-cap | no-attachments | no-rule. */
  reason?: string;
  message?: string;
  /** Optional manual-run / defer chips — raw EventChip wire shape. */
  chips?: unknown;
}

/** `chips` data — next-step chips after the rule completes. */
export interface EventChipsData {
  sourceBindingId?: string;
  chips?: unknown;
}

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

/**
 * Per-event callbacks invoked as the stream delivers events. All optional —
 * hosts wire what they render. Chip arrays from ALL carrier events
 * (`chips`, `event_confirmation`, `event_notice`) are forwarded through
 * `onChips` (raw wire shape; parse with `parseConsumerChips`).
 */
export interface DocumentUploadedEventHandlers {
  onClassification?: (data: EventClassificationData) => void;
  onOutput?: (data: EventOutputData) => void;
  onConfirmation?: (data: EventConfirmationData) => void;
  onNotice?: (data: EventNoticeData) => void;
  /** Raw chip wire array from any carrier event (parse with parseConsumerChips). */
  onChips?: (chips: unknown) => void;
  /** Server `error` event — `message` is the safe content (never raw detail). */
  onError?: (message: string) => void;
  /** Terminal `done` event (also invoked defensively if the stream ends without one). */
  onDone?: () => void;
}

/** Options for {@link runDocumentUploadedEvent}. */
export interface RunDocumentUploadedEventOptions {
  /** BFF API base URL (e.g. `https://spaarke-bff-dev.azurewebsites.net`). */
  bffBaseUrl: string;
  /** Active chat session id. */
  sessionId: string;
  /** Upload batch's session-file document ids, in upload order. */
  fileIds: readonly string[];
  /**
   * Command text the user typed alongside the upload, if any. Passed verbatim
   * — the SERVER enforces explicit-command supersede (ADR-039; the client
   * does NOT pre-filter).
   */
  typedCommand: string | null;
  /** Fresh access-token getter (Auth v2 / ADR-028). Never snapshotted. */
  getAccessToken: () => Promise<string>;
  /** Optional AbortSignal forwarded to the SSE fetch. */
  signal?: AbortSignal;
  handlers: DocumentUploadedEventHandlers;
}

// ---------------------------------------------------------------------------
// URL + error mapping
// ---------------------------------------------------------------------------

/** Build the Event endpoint URL — single seam for the task-022 server contract. */
export function buildDocumentUploadedEventUrl(bffBaseUrl: string, sessionId: string): string {
  return `${bffBaseUrl.replace(/\/$/, '')}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/events/document-uploaded`;
}

/**
 * Map a non-OK response to an Error carrying the ADR-019 stable `errorCode`
 * extension (falls back to a generic code on non-JSON bodies). Mirrors the
 * dispatchConsumer helper's mapping so both entry paths fail identically.
 */
export async function mapDocumentUploadedEventHttpError(response: Response): Promise<Error> {
  let errorCode = 'event-rules.failed';
  try {
    const problem = (await response.json()) as { errorCode?: string };
    if (problem && typeof problem.errorCode === 'string') {
      errorCode = problem.errorCode;
    }
  } catch {
    // Non-JSON body — keep the fallback errorCode.
  }
  return new Error(
    `documentUploadedEvent: request failed (status=${response.status}, errorCode=${errorCode})`
  );
}

// ---------------------------------------------------------------------------
// Message formatters (pure — exported for tests + the ConversationPane host)
// ---------------------------------------------------------------------------

/**
 * Deterministic classification line rendered as an Assistant message when an
 * `event_classification` arrives, e.g.:
 *   `Classified "engagement-letter.pdf" as **Engagement Letter** (92% confidence).`
 * Pure/total: tolerates missing fields (falls back to neutral wording).
 */
export function formatClassificationMessage(data: EventClassificationData): string {
  const name = typeof data.fileName === 'string' && data.fileName.length > 0 ? data.fileName : 'your document';
  const docType = typeof data.docType === 'string' && data.docType.length > 0 ? data.docType : 'a document';
  const confidencePart =
    typeof data.confidence === 'number' && Number.isFinite(data.confidence)
      ? ` (${Math.round(Math.min(Math.max(data.confidence, 0), 1) * 100)}% confidence)`
      : '';
  return `Classified "${name}" as **${docType}**${confidencePart}.`;
}

/**
 * Render an `event_output` payload (the STORED ledger entry, ADR-040 — render
 * follows store) as chat markdown.
 *
 * For the P1 summarize member the payload is the SUM-CHAT@v1
 * `DocumentAnalysisResult`-shaped JSON (`tldr/summary/keywords/entities`) —
 * rendered as a structured summary. Unknown payload shapes degrade to a
 * fenced JSON block (the payload IS the grounded output; we never invent
 * prose around it — ADR-039 no ungrounded free-form output).
 */
export function formatEventOutputMarkdown(payload: unknown): string {
  if (payload === null || payload === undefined) {
    return 'Analysis complete.';
  }
  if (typeof payload === 'string') {
    return payload.length > 0 ? payload : 'Analysis complete.';
  }
  if (typeof payload !== 'object') {
    return String(payload);
  }

  const record = payload as Record<string, unknown>;
  const tldr = typeof record.tldr === 'string' && record.tldr.length > 0 ? record.tldr : null;
  const summary = typeof record.summary === 'string' && record.summary.length > 0 ? record.summary : null;
  const keywords = Array.isArray(record.keywords)
    ? (record.keywords as unknown[]).filter((k): k is string => typeof k === 'string' && k.length > 0)
    : [];

  if (tldr === null && summary === null) {
    // Unknown stored shape — render verbatim (grounded, never paraphrased).
    try {
      return '```json\n' + JSON.stringify(payload, null, 2) + '\n```';
    } catch {
      return 'Analysis complete.';
    }
  }

  const parts: string[] = [];
  if (tldr !== null) {
    parts.push(`**TL;DR:** ${tldr}`);
  }
  if (summary !== null) {
    parts.push(summary);
  }
  if (keywords.length > 0) {
    parts.push(`**Keywords:** ${keywords.join(', ')}`);
  }
  return parts.join('\n\n');
}

/**
 * Render an `event_notice` as a subtle inline Assistant line. The server
 * always supplies a user-renderable `message` (NFR-09 — never a silent drop);
 * the italics keep the notice visually quieter than a normal assistant turn.
 * Falls back to a neutral line when `message` is absent (defensive).
 */
export function formatNoticeMessage(data: EventNoticeData): string {
  const message =
    typeof data.message === 'string' && data.message.length > 0
      ? data.message
      : 'The automatic document workflow did not run.';
  return `_${message}_`;
}

// ---------------------------------------------------------------------------
// Stream consumption
// ---------------------------------------------------------------------------

/** Minimal envelope view of a parsed ChatSseEvent line for this stream. */
interface EventSseEnvelope {
  type: string;
  content?: string;
  data?: Record<string, unknown>;
}

/**
 * POST the Event endpoint and consume its SSE stream via the canonical
 * `readSseStream`/`parseSseEvent` path, dispatching each event to the typed
 * handlers.
 *
 * Terminal handling (consistent with existing chat streams):
 *   - `error` → `onError(safe message)`; the stream keeps reading (the server
 *     may still send `done`).
 *   - `done`  → `onDone()`.
 *   - Stream end WITHOUT a terminal event → `onDone()` is invoked defensively
 *     so hosts can clear transient UI state.
 *   - HTTP / network failures reject with the mapped Error (ADR-019
 *     errorCode); the caller renders its own stable failure message.
 */
export async function runDocumentUploadedEvent(options: RunDocumentUploadedEventOptions): Promise<void> {
  const { bffBaseUrl, sessionId, fileIds, typedCommand, getAccessToken, signal, handlers } = options;

  let sawTerminal = false;

  const forwardChips = (chips: unknown): void => {
    if (Array.isArray(chips) && chips.length > 0) {
      handlers.onChips?.(chips);
    }
  };

  await readSseStream({
    url: buildDocumentUploadedEventUrl(bffBaseUrl, sessionId),
    body: { fileIds: [...fileIds], typedCommand },
    getAccessToken,
    signal,
    mapHttpError: mapDocumentUploadedEventHttpError,
    onLine: (line: string) => {
      const parsed = parseSseEvent(line) as unknown as EventSseEnvelope | null;
      if (!parsed) return;

      const data = (parsed.data ?? {}) as Record<string, unknown>;

      switch (parsed.type) {
        case 'event_classification':
          handlers.onClassification?.(data as EventClassificationData);
          return;

        case 'event_output':
          handlers.onOutput?.(data as EventOutputData);
          return;

        case 'event_confirmation':
          handlers.onConfirmation?.(data as EventConfirmationData);
          forwardChips((data as EventConfirmationData).chips);
          return;

        case 'event_notice':
          handlers.onNotice?.(data as EventNoticeData);
          forwardChips((data as EventNoticeData).chips);
          return;

        case 'chips':
          forwardChips((data as EventChipsData).chips);
          return;

        case 'error':
          sawTerminal = true;
          handlers.onError?.(
            typeof parsed.content === 'string' && parsed.content.length > 0
              ? parsed.content
              : 'Stream error'
          );
          return;

        case 'done':
          sawTerminal = true;
          handlers.onDone?.();
          return;

        default:
          // Unknown discriminant — defensive ignore (additive server evolution).
          return;
      }
    },
  });

  if (!sawTerminal) {
    // Defensive: server SHOULD always emit done/error; give hosts a terminal
    // signal regardless (mirrors dispatchConsumer's `empty` completion).
    handlers.onDone?.();
  }
}
