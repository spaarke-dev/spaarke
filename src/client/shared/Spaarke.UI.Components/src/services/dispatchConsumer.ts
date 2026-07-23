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
 * P3 (FR-P3-06, task 045) outcome — no further dispatch migration happened:
 * Compose's `executeComposeSummarize` was already deleted 2026-07-02 by
 * spaarkeai-compose-r1 (commit 0420562e6); LegalWorkspace's local
 * `summarizeService` is deleted in task 045 as an orphaned duplicate of the
 * shared SummarizeFilesWizard service; and that shared summarize wizard KEEPS
 * its `/api/workspace/files/summarize` multipart contract (server-side Binding
 * resolution by consumerType) — its parse loop is consolidated onto
 * `readSseStream`, not routed through this helper. This helper remains the
 * ONE Click-path dispatch entry, in the shared `@spaarke/ui-components`
 * package every surface consumes.
 *
 * SSE consumption: the canonical `readSseStream` + `parseSseEvent` primitives
 * from `hooks/useSseStream.ts` (the ONE SSE parse path client-wide). No
 * hand-rolled parser is introduced here.
 *
 * PaneEventBus: the publisher is INJECTED (`publishPaneEvent`) because
 * `@spaarke/ai-widgets` (which owns PaneEventBus) depends on this package,
 * not vice versa. The emitted event shapes are structurally assignable to
 * the `workspace` channel's `WorkspacePaneEvent` union (streaming_started /
 * section_started / section_completed / streaming_complete / widget_load —
 * all pre-existing discriminants; ADR-030 four-channel invariant untouched).
 *
 * SECTION-KEYED RENDER CONTRACT (ai-architecture-redesign-r1 task 046 /
 * FR-P3-07, amended ADR-037 2026-07-05): the legacy per-field-delta render
 * vocabulary was DELETED at the last-playbook cutover — no server path emits
 * `"delta"` chunks anymore (verified: `AnalysisChunk.FromDelta` has zero call
 * sites), and the widget layer renders section-name-keyed events only. This
 * helper bridges the terminal `complete` chunk's structured result onto
 * `section_started` / `section_completed` pairs (one per top-level result
 * key, in declaration order).
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
import { extractRevealableSections, revealSectionsProgressively } from './progressiveSectionReveal';

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
  /** Event discriminator: "text" | "complete" | "error" | "chips". */
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
  /**
   * Next-step consumer chips on "chips" (G-P1 UAT fix, 2026-07-05 — the
   * dispatched Binding's `sprk_chiptransitions`, unified EventChip wire shape).
   * Raw wire value; parse with {@link parseConsumerChips}.
   */
  chips?: unknown;
  /**
   * The dispatched Binding's disposition ledger value on the terminal `complete`
   * chunk (assistant-enhancements-r1 task 013a — additive). `surface_launch` for a
   * create capability whose drafted output opens a pre-seeded surface; the client
   * branches on it WITHOUT any second intent mechanism (the server's disposition IS
   * the routing decision — ADR-039 / ADR-040). Absent on non-create dispatches.
   */
  disposition?: string;
  /**
   * The dispatched Binding's `sprk_consumertype` on the terminal `complete` chunk
   * (task 013a — additive). The static-registry key the client resolves the launch
   * surface from (`create-matter` / `create-task` / `create-todo`). Absent when the
   * disposition is not `surface_launch`.
   */
  consumerType?: string;
}

// ---------------------------------------------------------------------------
// PaneEventBus publisher seam (injected — see module JSDoc)
// ---------------------------------------------------------------------------

/**
 * The subset of `workspace`-channel events this helper emits. Structurally
 * assignable to `@spaarke/ai-widgets` `WorkspacePaneEvent` (all discriminants
 * pre-exist on that union; no new event type is introduced).
 *
 * Section events are keyed by section NAME (the result's top-level key) per
 * amended ADR-037 — the ONLY streaming render vocabulary since the task 046
 * cutover.
 */
export interface DispatchWorkspaceEvent {
  type: 'streaming_started' | 'section_started' | 'section_completed' | 'streaming_complete' | 'widget_load';
  streamId?: string;
  /** Section name (= top-level result key) on `section_started` / `section_completed`. */
  sectionName?: string;
  /** Declaration-order index of the section on `section_started`. */
  sectionIndex?: number;
  /** Final text content for a string-valued section on `section_completed`. */
  finalContent?: string;
  /** Final structured payload for a non-string section on `section_completed`. */
  finalStructuredData?: unknown;
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
  /**
   * Progressive-reveal pacing (task 039 / D-F5): delay in ms awaited between
   * revealing successive sections of the terminal result — see
   * {@link revealSectionsProgressively}. Defaults to that function's own
   * default (120ms) when omitted. Pass 0 for tests that don't care about
   * pacing.
   */
  readonly sectionRevealDelayMs?: number;
  /**
   * Compose-surface scoping (spaarkeai-compose-r2 task 112, UAT-R3 defect
   * #3c): when `true`, this dispatcher instance does NOT publish `workspace`
   * -channel events (`widget_load` / `streaming_started` / `section_started`
   * / `section_completed` / `streaming_complete`) for ANY of its dispatches,
   * and skips the paced section-reveal entirely (no artificial delay before
   * the returned Promise settles). The Compose editor tab has no
   * section-renderer subscriber for these discriminants
   * (`useComposeWorkspaceReceivers` only reacts to `compose_context_insert` /
   * `compose_assistant_insert` / `compose_qa_highlight`) — publishing them
   * was dead output. Additive + default-false: other host surfaces (e.g.
   * `useConsumerChips`'s own `createConsumerDispatcher` instance) are
   * unaffected — this does NOT alter the shared dispatchConsumer contract for
   * other surfaces (ADR-030).
   */
  readonly suppressWorkspaceSectionBridge?: boolean;
}

/** Per-dispatch arguments (all optional — a bare chip click passes none). */
export interface DispatchConsumerArgs {
  /**
   * Capability arguments forwarded VERBATIM to the server as `args` (e.g. a
   * chip's `prefill_slots`). The server-side executor owns the typed parse
   * against the Action's `sprk_inputschema` — the client never interprets.
   */
  readonly slots?: Record<string, unknown>;
  /**
   * Per-dispatch session-id override (spaarkeai-compose-r2 DEF-09). When present,
   * this dispatch targets THIS session (`/api/ai/chat/sessions/{sessionIdOverride}
   * /dispatch`) instead of the dispatcher's bound `getSessionId()`. Additive +
   * default-undefined: every existing caller omits it and keeps its bound-session
   * behavior unchanged. The Compose inline toolbar sets it to the editor's DOCUMENT
   * session so a compose-disposition EDIT dispatch writes its `SessionOutput` into
   * the SAME session `ComposeWorkspace` reads `compose-outputs` from — otherwise the
   * write (chat session) and the materialize read (document session) diverge and the
   * inline redline never appears. This is WHICH session-ledger the dispatch writes
   * to (already part of the endpoint URL) — NOT routing (bindingId remains the sole
   * binding-resolution datum; ADR-039).
   */
  readonly sessionIdOverride?: string;
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
  /**
   * The dispatched Binding's disposition (assistant-enhancements-r1 task 013),
   * surfaced verbatim from the terminal `complete` chunk. `surface_launch` tells the
   * host to open a pre-seeded create surface via `launchSurface`; undefined when the
   * stream carried no disposition. The client never re-derives this (ADR-039).
   */
  readonly disposition?: string;
  /**
   * The dispatched Binding's `sprk_consumertype` (task 013), surfaced from the
   * terminal `complete` chunk — the static-registry key for the launch surface.
   * Undefined unless `disposition === 'surface_launch'`.
   */
  readonly consumerType?: string;
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
  const {
    bffBaseUrl,
    getSessionId,
    getAccessToken,
    publishPaneEvent,
    sectionRevealDelayMs,
    suppressWorkspaceSectionBridge,
  } = deps;

  // Compose-surface scoping (task 112) — see ConsumerDispatchDeps JSDoc.
  // Additive no-op wrapper; the shared `publishPaneEvent` contract itself is
  // untouched, and every OTHER dispatcher instance (suppressWorkspaceSectionBridge
  // unset) publishes exactly as before.
  const emitWorkspaceEvent: DispatchPaneEventPublisher = suppressWorkspaceSectionBridge
    ? () => {
        /* Compose surface: no section-renderer subscriber — see JSDoc above. */
      }
    : publishPaneEvent;

  return async function dispatchConsumer(
    bindingId: string,
    args?: DispatchConsumerArgs
  ): Promise<DispatchConsumerResult> {
    // ── Click preconditions (no network, no bus events) ─────────────────────
    if (!bindingId || bindingId.trim().length === 0) {
      throw new DispatchPreconditionError('binding-id-required', 'dispatchConsumer: bindingId is required');
    }

    // DEF-09: a per-dispatch `sessionIdOverride` wins over the bound getSessionId()
    // so a Compose edit dispatch can target the editor's DOCUMENT session (see
    // DispatchConsumerArgs.sessionIdOverride). Falls back to the bound session for
    // every other caller (chips, wizards) — additive, default-undefined.
    const sessionId = args?.sessionIdOverride ?? getSessionId();
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
      emitWorkspaceEvent('workspace', {
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
    // task 013: the terminal chunk's disposition + consumerType (013a additive
    // fields) — the SERVER's create-flow routing decision. Surfaced verbatim to
    // the host, which branches `surface_launch` → the pre-seeded surface launch
    // (ADR-039 — the client re-derives nothing).
    let terminalDisposition: string | undefined;
    let terminalConsumerType: string | undefined;
    // task 039 / D-F5: the terminal chunk's section reveal is staggered (see
    // progressiveSectionReveal.ts), so it outlives the synchronous consumeChunk call.
    // Tracked here so the dispatch doesn't resolve until pacing + streaming_complete
    // have both actually happened.
    let pendingReveal: Promise<void> | null = null;

    const publishStartedOnce = (): void => {
      if (started) return;
      started = true;
      emitWorkspaceEvent('workspace', { type: 'streaming_started', streamId });
    };

    /**
     * Bridge ONE AnalysisChunk to zero-or-more workspace events (formerly
     * `sseToPaneEventBridge.consume`; section-keyed since the task 046
     * cutover per amended ADR-037):
     *  - "complete" → synthesized per-key `section_started` +
     *                 `section_completed` pairs from the terminal `result`
     *                 payload (executors emit ONE terminal chunk carrying the
     *                 whole STORED ledger payload — ADR-040 render-follows-
     *                 store), PACED via `revealSectionsProgressively` (task 039
     *                 / D-F5 — a small delay between sections so the output
     *                 renders in ≥2 visible steps instead of one batched
     *                 paint), followed by streaming_complete/complete. String
     *                 values ride `finalContent`; arrays/objects ride
     *                 `finalStructuredData` so the section renderer's typed
     *                 paths (lists, labeled blocks) apply.
     *  - "error"    → streaming_complete/declined (no error text on the bus,
     *                 ADR-019) + the helper rejects after the stream ends
     *  - "text"/unknown → ignored (structured streams use the terminal
     *                 complete chunk; no server path emits per-field "delta"
     *                 chunks anymore — verified at cutover)
     * `streaming_started` is emitted once before the first mapped event.
     */
    const consumeChunk = (chunk: AnalysisChunk): void => {
      if (!chunk || typeof chunk.type !== 'string') return;

      switch (chunk.type) {
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
          // task 013a: capture the create-flow routing decision off the terminal
          // chunk (additive; absent on non-create dispatches → stays undefined).
          if (typeof chunk.disposition === 'string' && chunk.disposition.length > 0) {
            terminalDisposition = chunk.disposition;
          }
          if (typeof chunk.consumerType === 'string' && chunk.consumerType.length > 0) {
            terminalConsumerType = chunk.consumerType;
          }
          sawComplete = true;

          if (
            !suppressWorkspaceSectionBridge &&
            chunk.result &&
            typeof chunk.result === 'object' &&
            !Array.isArray(chunk.result)
          ) {
            // Section-keyed bridge (task 046 / amended ADR-037), now PACED (task 039 /
            // D-F5): one section per top-level result key, in declaration order,
            // revealed with a stagger so the output arrives in ≥2 visible steps rather
            // than one synchronous-batch paint. `chunk.result` is the STORED terminal
            // payload (ADR-040 — the BFF only emits this chunk after the ledger write);
            // this bridge never sees pre-store state, so pacing cannot introduce a
            // render-ahead-of-store violation. SKIPPED ENTIRELY when
            // `suppressWorkspaceSectionBridge` (task 112) — no renderer subscribes to
            // these events on the Compose surface, so there is no reason to pay the
            // paced-reveal latency before this dispatch's Promise can settle.
            const sections = extractRevealableSections(chunk.result as Record<string, unknown>);
            pendingReveal = (async () => {
              publishStartedOnce();
              await revealSectionsProgressively(
                sections,
                (section, index) => {
                  emitWorkspaceEvent('workspace', {
                    type: 'section_started',
                    streamId,
                    sectionName: section.name,
                    sectionIndex: index,
                  });
                  emitWorkspaceEvent('workspace', {
                    type: 'section_completed',
                    streamId,
                    sectionName: section.name,
                    // Strings render as section text; arrays/objects ride the
                    // structured payload so the section renderer's typed paths
                    // (bulleted lists, labeled blocks) apply.
                    ...(typeof section.value === 'string'
                      ? { finalContent: section.value }
                      : { finalStructuredData: section.value }),
                  });
                },
                { delayMs: sectionRevealDelayMs }
              );
              emitWorkspaceEvent('workspace', {
                type: 'streaming_complete',
                streamId,
                completionStatus: 'complete',
              });
            })();
            return;
          }

          publishStartedOnce();
          emitWorkspaceEvent('workspace', {
            type: 'streaming_complete',
            streamId,
            completionStatus: 'complete',
          });
          return;
        }

        case 'error': {
          // No `streaming_error` discriminant exists (PaneEventTypes) —
          // terminal declined event; the helper rejects after the stream ends.
          publishStartedOnce();
          emitWorkspaceEvent('workspace', {
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
      emitWorkspaceEvent('workspace', {
        type: 'streaming_complete',
        streamId,
        completionStatus: 'declined',
      });
      throw err;
    }

    // task 039 / D-F5: wait for the paced section reveal (+ its trailing
    // streaming_complete) to actually finish before resolving — a caller awaiting
    // dispatchConsumer() should observe the FULL reveal, not just its kickoff.
    if (pendingReveal) {
      await pendingReveal;
    }

    if (sawError) {
      throw new Error('dispatchConsumer: the capability stream reported an error');
    }

    // Defensive: stream ended without a terminal chunk — emit `empty` so
    // subscribers can clear UI state (server SHOULD always emit complete/error).
    if (!sawComplete) {
      emitWorkspaceEvent('workspace', {
        type: 'streaming_complete',
        streamId,
        completionStatus: 'empty',
      });
      return {
        streamId,
        status: 'empty',
        result: terminalResult,
        chips: capturedChips,
        disposition: terminalDisposition,
        consumerType: terminalConsumerType,
      };
    }

    return {
      streamId,
      status: 'complete',
      result: terminalResult,
      chips: capturedChips,
      disposition: terminalDisposition,
      consumerType: terminalConsumerType,
    };
  };
}
