/**
 * dispatchConsumer — the ONE client capability-dispatch helper (Click path).
 *
 * ai-architecture-redesign-r1 task 023 / FR-P1-04 / ADR-039.
 *
 * `dispatchConsumer(bindingId, args)` is the ONLY place SSE consumption +
 * PaneEventBus bridging for capability dispatch lives on the client. Chips
 * (and later wizard/ribbon/card launchers, per canonical §7.2) carry a
 * `binding_id`; clicking one calls this helper; the SERVER resolves the
 * Binding row (`sprk_playbookconsumer`) via `IConsumerRoutingService.
 * ResolveBindingAsync` and executes the bound capability. The client carries
 * ZERO routing logic and ZERO intent detection — bindingId in, stream out.
 *
 * Replaces (hard cutover, NFR-08 — deleted, not shimmed):
 *   - `executeSummarizeIntent.ts` (SpaarkeAi) — per-capability orchestrator
 *     with a hand-rolled SSE parser
 *   - `intentMatcher.ts` (SpaarkeAi) — client-side intent detection (the
 *     second intent mechanism ADR-039 forbids)
 *   - `sseToPaneEventBridge.ts` (SpaarkeAi) — its AnalysisChunk→PaneEventBus
 *     mapping is encapsulated INSIDE this helper (sole consumer was the
 *     deleted orchestrator)
 *
 * P3 (FR-P3-06) migrates LegalWorkspace `summarizeService` and Compose
 * `executeComposeSummarize` onto this same helper — which is why it lives in
 * the shared `@spaarke/ui-components` package all three surfaces consume.
 *
 * SSE consumption: the canonical `readSseStream` + `parseSseEvent` primitives
 * from `hooks/useSseStream.ts` (the ONE SSE parse path client-wide). No
 * hand-rolled parser is introduced here.
 *
 * PaneEventBus: the publisher is INJECTED (`publishPaneEvent`) because
 * `@spaarke/ai-widgets` (which owns PaneEventBus) depends on this package,
 * not vice versa. The emitted event shapes are structurally assignable to
 * the `workspace` channel's `WorkspacePaneEvent` union (streaming_started /
 * field_delta / streaming_complete / widget_load — all pre-existing
 * discriminants; ADR-030 four-channel invariant untouched).
 *
 * Server contract (BUILT — task 023b `DispatchSessionEndpoint`, BFF):
 *   POST {bffBaseUrl}/api/ai/chat/sessions/{sessionId}/dispatch
 *   body: { bindingId: string, args: Record<string, unknown> }
 *     - `bindingId` MUST be the Binding row GUID (`sprk_playbookconsumer` id)
 *       carried by the chip — the ONLY resolution vocabulary (ADR-039; non-GUID
 *       → 400 `dispatch.binding-id-invalid`; unknown/disabled GUID → 404
 *       `dispatch.binding-not-found`)
 *   → 200 text/event-stream of AnalysisChunk events
 *     (`data: {"type":"delta"|"complete"|"error"|"text", ...}\n\n`,
 *     camelCase, serialized by the SAME writer as SummarizeSessionEndpoint —
 *     one wire shape)
 *   → non-OK: ProblemDetails with stable `errorCode` extension (ADR-019)
 *
 * Empty-attachments guard (Click precondition, re-homed from the deleted
 * modules per task 025 handoff): a dispatch whose chip declares
 * `requiresAttachments` throws a DispatchPreconditionError BEFORE any network
 * call or bus event when the session has zero attachments.
 *
 * @see ADR-039 — grounded execution & closed catalogs (three entry paths)
 * @see ADR-040 — ledger-before-render (the server writes SessionOutput before
 *                streaming; this helper renders what the stream delivers)
 * @see ADR-028 — Auth v2; `getAccessToken` re-invoked per stream open by
 *                `readSseStream`, never snapshotted
 * @see ADR-019 — ProblemDetails: only stable errorCode strings surface
 * @see ADR-030 — PaneEventBus channels closed at 4; additive event types only
 */

import { readSseStream, parseSseEvent } from '../hooks/useSseStream';
import type { AccessTokenGetter } from '../components/SprkChat/types';

// ---------------------------------------------------------------------------
// Chip contract (Click path, canonical §7.2 / Binding `sprk_chiptransitions`)
// ---------------------------------------------------------------------------

/**
 * Wire shape of one chip as delivered by the server. THE shipped shape
 * (task 022 Event SSE contract, resolved at task 023b) is the BFF `EventChip`
 * record serialized camelCase: `{targetBindingId, label, args?}`. It appears
 * on THREE stream events — the top-level `chips` event
 * (`data: {sourceBindingId, chips: EventChip[]}`), and inside
 * `event_confirmation.data.chips` + `event_notice.data.chips` — so it is the
 * ONE chip wire vocabulary (single-wire-shape decision, task 023b: mapping
 * EventChip here was strictly smaller than re-emitting the final chips as a
 * `context_event`, which would have left the confirmation/notice chips as a
 * second shape).
 *
 * The parser ALSO tolerates the Binding column's authored snake_case JSON
 * (`{target_binding_id, chip_label, prefill_slots?, requires_attachments?}`)
 * and its camelCase twins — maker-authored transitions parse identically if a
 * host ever receives them raw.
 */
export interface ConsumerChipWire {
  target_binding_id?: string;
  chip_label?: string;
  prefill_slots?: Record<string, unknown>;
  requires_attachments?: boolean;
  // camelCase twins (BFF JsonNamingPolicy.CamelCase without JsonPropertyName)
  targetBindingId?: string;
  chipLabel?: string;
  prefillSlots?: Record<string, unknown>;
  requiresAttachments?: boolean;
  // BFF EventChip serialization (task 022 — the shipped Event-path shape):
  // `label` is the user-facing text; `args` forwards verbatim as dispatch args
  // (e.g. `{fileIds: [...]}` on manual-run / summarize-all / M4 confirm chips).
  label?: string;
  args?: Record<string, unknown>;
}

/**
 * A rendered next-step chip. `bindingId` IS the routing decision (ADR-039 D4)
 * — the client never re-detects intent from the label.
 */
export interface ConsumerChip {
  /** The target Binding row id — the ONLY routing datum the client carries. */
  readonly bindingId: string;
  /** Maker-authored chip label (Binding `chip_transitions[].chip_label`). */
  readonly label: string;
  /** Optional pre-filled capability arguments forwarded verbatim as `args`. */
  readonly prefillSlots?: Record<string, unknown>;
  /**
   * Whether the target capability requires session attachments. Drives the
   * empty-attachments Click precondition (disabled chip UI + helper guard).
   */
  readonly requiresAttachments?: boolean;
}

/**
 * Tolerant parser for the chip wire payload. Malformed input (non-array,
 * entries missing `target_binding_id` or `chip_label`) degrades to skipping
 * the entry — chip rendering must never throw (mirrors the Binding contract's
 * malformed-JSON tolerance on the server side).
 */
export function parseConsumerChips(raw: unknown): ConsumerChip[] {
  if (!Array.isArray(raw)) {
    return [];
  }
  const chips: ConsumerChip[] = [];
  for (const entry of raw) {
    if (entry === null || typeof entry !== 'object') continue;
    const wire = entry as ConsumerChipWire;
    const bindingId = wire.target_binding_id ?? wire.targetBindingId;
    // `label` last: the EventChip serialization (the shipped Event-path shape,
    // task 022/023b) uses bare `label`; authored chip transitions use chip_label.
    const label = wire.chip_label ?? wire.chipLabel ?? wire.label;
    if (typeof bindingId !== 'string' || bindingId.length === 0) continue;
    if (typeof label !== 'string' || label.length === 0) continue;
    // EventChip `args` and chip-transition `prefill_slots` are the same datum:
    // capability args forwarded VERBATIM to dispatchConsumer (the server owns
    // the typed parse — ADR-039; the client never interprets them).
    const slots = wire.prefill_slots ?? wire.prefillSlots ?? wire.args;
    chips.push({
      bindingId,
      label,
      prefillSlots: slots && typeof slots === 'object' ? slots : undefined,
      requiresAttachments: wire.requires_attachments === true || wire.requiresAttachments === true,
    });
  }
  return chips;
}

// ---------------------------------------------------------------------------
// AnalysisChunk wire shape (BFF Models/Ai/AnalysisChunk.cs, camelCase)
// ---------------------------------------------------------------------------

/**
 * TypeScript counterpart of the BFF `Sprk.Bff.Api.Models.Ai.AnalysisChunk`
 * record (camelCase per the endpoint's JsonSerializerOptions). Formerly
 * declared in the deleted SpaarkeAi `sseToPaneEventBridge.ts`.
 */
export interface AnalysisChunk {
  /** Event discriminator: "text" | "complete" | "error" | "delta" | "chips". */
  type: string;
  /** Token chunk for "text" events (legacy free-form streaming). */
  content?: string;
  /** Whether this is the terminal chunk. True on "complete" + "error". */
  done?: boolean;
  /** Structured result on "complete". */
  result?: unknown;
  /** Legacy full-text summary on "complete" (Completed(string) server overload). */
  summary?: string;
  /** Error message on "error". Never forwarded to the bus (ADR-019). */
  error?: string;
  /** Structured-field delta payload on "delta". */
  delta?: AnalysisFieldDelta;
  /**
   * Next-step consumer chips on "chips" (G-P1 UAT fix, 2026-07-05 — the
   * dispatched Binding's `sprk_chiptransitions`, unified EventChip wire shape).
   * Raw wire value; parse with {@link parseConsumerChips}.
   */
  chips?: unknown;
}

/** The `type: "delta"` payload (BFF `FieldDelta`). */
export interface AnalysisFieldDelta {
  /** JSON path of the target field, e.g. "tldr" or "$.summary". */
  path: string;
  /** Token chunk to append to the field. */
  content: string;
  /** Monotonic sequence number for ordering. */
  sequence: number;
}

// ---------------------------------------------------------------------------
// PaneEventBus publisher seam (injected — see module JSDoc)
// ---------------------------------------------------------------------------

/**
 * The subset of `workspace`-channel events this helper emits. Structurally
 * assignable to `@spaarke/ai-widgets` `WorkspacePaneEvent` (all discriminants
 * pre-exist on that union; no new event type is introduced).
 */
export interface DispatchWorkspaceEvent {
  type: 'streaming_started' | 'field_delta' | 'streaming_complete' | 'widget_load';
  streamId?: string;
  fieldPath?: string;
  fieldContent?: string;
  sequence?: number;
  completionStatus?: 'complete' | 'declined' | 'empty';
  widgetType?: string;
  widgetData?: unknown;
  displayName?: string;
}

/**
 * Publisher for the `workspace` PaneEventBus channel. Hosts wire this from
 * `usePaneEventBus().dispatch` / `useDispatchPaneEvent()`.
 */
export type DispatchPaneEventPublisher = (channel: 'workspace', event: DispatchWorkspaceEvent) => void;

// ---------------------------------------------------------------------------
// Public dispatch contract
// ---------------------------------------------------------------------------

/**
 * Host dependencies bound ONCE via {@link createConsumerDispatcher}. Keeping
 * them out of the per-click signature preserves the spec'd two-argument
 * `dispatchConsumer(bindingId, args)` call shape at chip click sites.
 */
export interface ConsumerDispatchDeps {
  /** BFF API base URL (e.g. `https://spaarke-bff-dev.azurewebsites.net`). */
  readonly bffBaseUrl: string;
  /**
   * Active chat session id getter — read per dispatch so a stable dispatcher
   * instance follows session changes. Returning null/undefined/'' fails the
   * dispatch precondition (no session, nothing to dispatch against).
   */
  readonly getSessionId: () => string | null | undefined;
  /** Fresh access-token getter (Auth v2 / ADR-028). Never snapshotted. */
  readonly getAccessToken: AccessTokenGetter;
  /** `workspace`-channel PaneEventBus publisher. */
  readonly publishPaneEvent: DispatchPaneEventPublisher;
}

/** Per-dispatch arguments (all optional — a bare chip click passes none). */
export interface DispatchConsumerArgs {
  /**
   * Capability arguments forwarded VERBATIM to the server as `args` (e.g. a
   * chip's `prefill_slots`). The server-side executor owns the typed parse
   * against the Action's `sprk_inputschema` — the client never interprets.
   */
  readonly slots?: Record<string, unknown>;
  /** Whether the invoked capability requires session attachments (from the chip). */
  readonly requiresAttachments?: boolean;
  /** Current session attachment count — input to the empty-attachments guard. */
  readonly attachmentCount?: number;
  /**
   * Optional workspace render target. When present, ONE `widget_load` event
   * is published BEFORE the stream opens, with `correlationId: streamId`
   * merged into `widgetData` so the mounted widget binds to THIS run's
   * stream. View configuration only — never routing (ADR-039).
   */
  readonly workspaceTarget?: {
    readonly widgetType: string;
    readonly widgetData?: Record<string, unknown>;
    readonly displayName?: string;
  };
  /** Optional stream correlation id; defaults to a generated one. */
  readonly streamId?: string;
  /** Optional AbortSignal forwarded to the SSE fetch. */
  readonly signal?: AbortSignal;
}

/** Result of a successful dispatch (the stream reached a terminal state). */
export interface DispatchConsumerResult {
  /** Correlation id carried on every published `workspace.streaming_*` event. */
  readonly streamId: string;
  /** Terminal stream status. */
  readonly status: 'complete' | 'empty';
  /**
   * The terminal `complete` chunk's structured result (the STORED ledger payload
   * — ADR-040 render-follows-store) or its legacy `summary` string. Hosts that
   * render dispatched output in the conversation surface read THIS (G-P1 UAT
   * fix, 2026-07-05); undefined when the stream ended without a result.
   */
  readonly result?: unknown;
  /**
   * Next-step chips delivered by the stream's `chips` chunk (the dispatched
   * Binding's `sprk_chiptransitions`), parsed. Undefined when none arrived.
   */
  readonly chips?: ReadonlyArray<ConsumerChip>;
}

/**
 * THE client dispatch entry: `dispatchConsumer(bindingId, args)`.
 * Rejects on precondition failure, HTTP error, stream error chunk, or
 * network failure (after publishing a terminal `streaming_complete`
 * `declined` event when a stream lifecycle had begun).
 */
export type DispatchConsumer = (bindingId: string, args?: DispatchConsumerArgs) => Promise<DispatchConsumerResult>;

/**
 * Thrown when a dispatch is refused BEFORE any network call or bus event
 * (Click preconditions). `code` is a stable machine discriminant.
 */
export class DispatchPreconditionError extends Error {
  public readonly code: 'binding-id-required' | 'no-session' | 'attachments-required';

  constructor(code: DispatchPreconditionError['code'], message: string) {
    super(message);
    this.name = 'DispatchPreconditionError';
    this.code = code;
  }
}

// ---------------------------------------------------------------------------
// Implementation
// ---------------------------------------------------------------------------

function generateStreamId(): string {
  // Unique-enough correlation id; not security-sensitive.
  return `dispatch-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

/**
 * Build the binding-dispatch endpoint URL. Single seam for the server
 * contract declared in the module JSDoc (task 022+ owns the server surface).
 */
export function buildDispatchUrl(bffBaseUrl: string, sessionId: string): string {
  return `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/dispatch`;
}

/**
 * Map a non-OK dispatch response to an Error carrying the ADR-019 stable
 * `errorCode` extension (falls back to a generic code on non-JSON bodies).
 */
async function mapDispatchHttpError(response: Response): Promise<Error> {
  let errorCode = 'dispatch.failed';
  try {
    const problem = (await response.json()) as { errorCode?: string };
    if (problem && typeof problem.errorCode === 'string') {
      errorCode = problem.errorCode;
    }
  } catch {
    // Non-JSON body — keep fallback errorCode.
  }
  return new Error(`dispatchConsumer: dispatch failed (status=${response.status}, errorCode=${errorCode})`);
}

/**
 * Bind host dependencies and return the ONE `dispatchConsumer(bindingId,
 * args)` helper. Create once per host surface (e.g. via `useMemo`); the
 * session id is re-read per dispatch through `deps.getSessionId`.
 */
export function createConsumerDispatcher(deps: ConsumerDispatchDeps): DispatchConsumer {
  const { bffBaseUrl, getSessionId, getAccessToken, publishPaneEvent } = deps;

  return async function dispatchConsumer(
    bindingId: string,
    args?: DispatchConsumerArgs
  ): Promise<DispatchConsumerResult> {
    // ── Click preconditions (no network, no bus events) ─────────────────────
    if (!bindingId || bindingId.trim().length === 0) {
      throw new DispatchPreconditionError('binding-id-required', 'dispatchConsumer: bindingId is required');
    }

    const sessionId = getSessionId();
    if (!sessionId) {
      throw new DispatchPreconditionError('no-session', 'dispatchConsumer: no active chat session');
    }

    // Empty-attachments guard (Event/Click precondition per task 025 handoff):
    // a capability that requires attachments MUST NOT dispatch with none.
    if (args?.requiresAttachments === true && (args.attachmentCount ?? 0) === 0) {
      throw new DispatchPreconditionError(
        'attachments-required',
        'dispatchConsumer: this action requires at least one attached file'
      );
    }

    const streamId = args?.streamId ?? generateStreamId();

    // ── Optional workspace render target (view config, not routing) ─────────
    if (args?.workspaceTarget) {
      publishPaneEvent('workspace', {
        type: 'widget_load',
        widgetType: args.workspaceTarget.widgetType,
        widgetData: {
          correlationId: streamId,
          ...(args.workspaceTarget.widgetData ?? {}),
        },
        displayName: args.workspaceTarget.displayName,
      });
    }

    // ── Stream state (AnalysisChunk → workspace.* bridging, one-shot start) ──
    let started = false;
    let sawComplete = false;
    let sawError = false;
    // G-P1 UAT fix (2026-07-05): capture the terminal result + next-step chips so
    // hosts can render the dispatched output in the CONVERSATION surface and keep
    // the chip strip alive after a click (chips previously arrived only on the
    // Event path — every chip click permanently emptied the strip).
    let terminalResult: unknown;
    let capturedChips: ConsumerChip[] | undefined;

    const publishStartedOnce = (): void => {
      if (started) return;
      started = true;
      publishPaneEvent('workspace', { type: 'streaming_started', streamId });
    };

    /**
     * Bridge ONE AnalysisChunk to zero-or-more workspace events. Formerly
     * `sseToPaneEventBridge.consume` — semantics preserved verbatim:
     *  - "delta"    → field_delta (with `$.`-prefix path normalization)
     *  - "complete" → synthesized per-field field_delta events from the
     *                 terminal `result` payload (non-streaming executors emit
     *                 ONE terminal chunk carrying the whole structured result)
     *                 followed by streaming_complete/complete
     *  - "error"    → streaming_complete/declined (no error text on the bus,
     *                 ADR-019) + the helper rejects after the stream ends
     *  - "text"/unknown → ignored (structured streams use delta + complete)
     * `streaming_started` is emitted once before the first mapped event.
     */
    const consumeChunk = (chunk: AnalysisChunk): void => {
      if (!chunk || typeof chunk.type !== 'string') return;

      switch (chunk.type) {
        case 'delta': {
          const delta = chunk.delta;
          if (
            !delta ||
            typeof delta.path !== 'string' ||
            typeof delta.content !== 'string' ||
            typeof delta.sequence !== 'number'
          ) {
            return;
          }
          // BFF's IncrementalJsonParser emits JSONPath-style keys ($.tldr);
          // widget schemas declare bare top-level keys — normalize.
          const normalizedPath = delta.path.startsWith('$.') ? delta.path.slice(2) : delta.path;
          publishStartedOnce();
          publishPaneEvent('workspace', {
            type: 'field_delta',
            streamId,
            fieldPath: normalizedPath,
            fieldContent: delta.content,
            sequence: delta.sequence,
          });
          return;
        }

        case 'chips': {
          // Next-step chips (unified EventChip wire shape). Conversation-surface
          // UI, not a bus payload — captured for the resolved result; tolerant
          // parse; an empty/malformed payload never clobbers earlier chips.
          const parsedChips = parseConsumerChips(chunk.chips);
          if (parsedChips.length > 0) {
            capturedChips = parsedChips;
          }
          return;
        }

        case 'complete': {
          terminalResult = chunk.result ?? chunk.summary ?? undefined;
          if (chunk.result && typeof chunk.result === 'object') {
            publishStartedOnce();
            const result = chunk.result as Record<string, unknown>;
            let seq = 0;
            for (const [key, value] of Object.entries(result)) {
              if (value === null || value === undefined) continue;
              // Widget-internal metadata fields are not renderable content.
              if (key === 'parsedSuccessfully' || key === 'rawResponse') continue;
              const content = typeof value === 'string' ? value : JSON.stringify(value);
              if (content === '') continue;
              publishPaneEvent('workspace', {
                type: 'field_delta',
                streamId,
                fieldPath: key,
                fieldContent: content,
                sequence: seq++,
              });
            }
          }
          publishStartedOnce();
          publishPaneEvent('workspace', {
            type: 'streaming_complete',
            streamId,
            completionStatus: 'complete',
          });
          sawComplete = true;
          return;
        }

        case 'error': {
          // No `streaming_error` discriminant exists (PaneEventTypes) —
          // terminal declined event; the helper rejects after the stream ends.
          publishStartedOnce();
          publishPaneEvent('workspace', {
            type: 'streaming_complete',
            streamId,
            completionStatus: 'declined',
          });
          sawError = true;
          return;
        }

        case 'text':
        default:
          // Legacy free-form / unknown chunk types — ignored.
          return;
      }
    };

    // ── Open the stream via the canonical SSE primitives ────────────────────
    try {
      await readSseStream({
        url: buildDispatchUrl(bffBaseUrl, sessionId),
        body: { bindingId, args: args?.slots ?? {} },
        getAccessToken,
        signal: args?.signal,
        mapHttpError: mapDispatchHttpError,
        onLine: (line: string) => {
          // parseSseEvent is the canonical `data:` line parser (any JSON with
          // a string `type` — AnalysisChunk satisfies it). Non-data lines and
          // malformed JSON return null and are skipped.
          const parsed = parseSseEvent(line);
          if (parsed) {
            consumeChunk(parsed as unknown as AnalysisChunk);
          }
        },
      });
    } catch (err) {
      // HTTP error, network failure, or abort. Emit a terminal declined event
      // so subscribers clear UI state (only meaningful if a lifecycle began —
      // subscribers key on streamId either way), then reject to the caller.
      publishPaneEvent('workspace', {
        type: 'streaming_complete',
        streamId,
        completionStatus: 'declined',
      });
      throw err;
    }

    if (sawError) {
      throw new Error('dispatchConsumer: the capability stream reported an error');
    }

    // Defensive: stream ended without a terminal chunk — emit `empty` so
    // subscribers can clear UI state (server SHOULD always emit complete/error).
    if (!sawComplete) {
      publishPaneEvent('workspace', {
        type: 'streaming_complete',
        streamId,
        completionStatus: 'empty',
      });
      return { streamId, status: 'empty', result: terminalResult, chips: capturedChips };
    }

    return { streamId, status: 'complete', result: terminalResult, chips: capturedChips };
  };
}
