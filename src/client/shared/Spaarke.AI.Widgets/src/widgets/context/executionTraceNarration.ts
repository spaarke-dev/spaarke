/**
 * executionTraceNarration — the TraceEvent v1 client contract + the HONEST
 * plan-narration derivation (AIR2-038 / FR-A1-09, design D-F4).
 *
 * ## Why this module exists (CLAUDE.md §11 justification)
 *
 *  - Existing: the widget rendered live `tool_chain` events only; no module read
 *    the server `TraceEvent` v1 stream, and no function produced plan narration.
 *  - Extension: the narration derivation is a PURE function so the truthfulness
 *    invariant (below) is unit-testable in isolation — it cannot live inside the
 *    React component without burying the one rule that matters most.
 *  - Cost-of-doing-nothing: without a server-stream client type the mount-gap
 *    backfill has nothing to parse; without an isolated narration function the
 *    "narration never fabricates" invariant has no test target.
 *
 * ## The truthfulness invariant (ADR-039 / project constraint)
 *
 * Live plan narration streams from REAL tool-chain events ONLY — never from
 * model prose. This module enforces that STRUCTURALLY: {@link narrateTrace} and
 * {@link narrateTraceEvent} accept `TraceEventDto`s exclusively. Each such event
 * is a projection of a persisted ADR-040 ledger marker (a ToolChain the agent
 * actually ran, a Gate that was actually raised, a ContextFingerprint that was
 * actually recorded) OR a live `context.tool_chain` SSE frame emitted strictly
 * AFTER its ledger append. There is deliberately NO parameter through which
 * model-authored plan text could enter — so a "step" the model merely CLAIMED,
 * with no corresponding real event, can never appear in the narration.
 *
 * ## NFR-07 / ADR-015
 *
 * Every field consumed here is an identifier / filter summary / count / duration.
 * The narration lines are assembled from those typed fields only — never from any
 * free-text/content member (the wire type has none).
 */

// ---------------------------------------------------------------------------
// TraceEvent v1 client contract (mirrors the BFF
// Services/Ai/PublicContracts/TraceEvent.cs wire shape — camelCase, tolerant).
// ---------------------------------------------------------------------------

/** Stable TraceEvent kind discriminators (mirror `TraceEventKind` on the server). */
export const TRACE_EVENT_KIND = {
  context: 'context',
  toolChain: 'tool_chain',
  toolCall: 'tool_call',
  gate: 'gate',
} as const;

export type TraceEventKind = (typeof TRACE_EVENT_KIND)[keyof typeof TRACE_EVENT_KIND];

/**
 * One TraceEvent v1 record as returned by
 * `GET /api/ai/chat/sessions/{id}/trace`. Tolerant reader: every field is
 * optional except the ordering/discriminator core, mirroring the server's
 * additive-only versioning. Identifiers / counts / durations only (NFR-07) —
 * there is intentionally NO content member.
 */
export interface TraceEventDto {
  /** Contract version stamp (e.g. `trace-event/v1`). */
  version?: string;
  /** Monotonic 0-based position in the projected stream. Consumers order by this alone. */
  sequence: number;
  /** 1-based session turn this event belongs to. */
  turn: number;
  /** Discriminator selecting which fields are populated. */
  kind: string;
  /** UTC timestamp copied from the source ledger marker. */
  timestamp?: string;

  // context kind
  fingerprintId?: string;
  contextSliceCount?: number;

  // tool_chain / tool_call kinds
  toolCallCount?: number;
  toolId?: string;
  argsSummary?: string;
  resultCount?: number;
  citations?: readonly string[];
  durationMs?: number;

  // gate kind
  gateId?: string;
  gateKind?: string;
  status?: string;
  sideEffectClass?: string;
  bindingId?: string;
  missingFields?: readonly string[];
  outputKey?: string;
}

/**
 * One honest narration line, derived from exactly one real {@link TraceEventDto}.
 * `sourceSequence` ties the line back to the trace event it was derived from —
 * a line can never exist without a backing event (the truthfulness invariant).
 */
export interface NarrationLine {
  /** The `sequence` of the trace event this line narrates (provenance anchor). */
  sourceSequence: number;
  /** 1-based session turn. */
  turn: number;
  /** The kind of the backing event. */
  kind: string;
  /** Human-readable narration text — identifiers/counts only (NFR-07). */
  text: string;
}

// ---------------------------------------------------------------------------
// Narration derivation (pure)
// ---------------------------------------------------------------------------

/**
 * Narrate ONE real trace event as a single honest line, or `null` when the event
 * kind is unknown (tolerant reader — an unrecognized future kind is skipped, never
 * invented). The text is built from typed identifier/count fields ONLY.
 */
export function narrateTraceEvent(event: TraceEventDto): NarrationLine | null {
  if (event === null || typeof event !== 'object' || typeof event.kind !== 'string') {
    return null;
  }
  const base = {
    sourceSequence: typeof event.sequence === 'number' ? event.sequence : 0,
    turn: typeof event.turn === 'number' ? event.turn : 0,
    kind: event.kind,
  };

  switch (event.kind) {
    case TRACE_EVENT_KIND.context: {
      const slices = typeof event.contextSliceCount === 'number' ? event.contextSliceCount : 0;
      const noun = slices === 1 ? 'context slice' : 'context slices';
      return { ...base, text: `Consulted ${slices} ${noun}` };
    }
    case TRACE_EVENT_KIND.toolChain: {
      const n = typeof event.toolCallCount === 'number' ? event.toolCallCount : 0;
      if (n === 0) return { ...base, text: 'Selected no tools this turn' };
      const noun = n === 1 ? 'tool' : 'tools';
      return { ...base, text: `Selected ${n} ${noun}` };
    }
    case TRACE_EVENT_KIND.toolCall: {
      const tool = typeof event.toolId === 'string' && event.toolId.length > 0 ? event.toolId : 'a tool';
      const parts: string[] = [];
      if (typeof event.resultCount === 'number') {
        parts.push(`${event.resultCount} result${event.resultCount === 1 ? '' : 's'}`);
      }
      if (typeof event.durationMs === 'number' && Number.isFinite(event.durationMs)) {
        parts.push(event.durationMs < 1000 ? `${Math.round(event.durationMs)} ms` : `${(event.durationMs / 1000).toFixed(2)} s`);
      }
      const suffix = parts.length > 0 ? ` (${parts.join(', ')})` : '';
      return { ...base, text: `Ran ${tool}${suffix}` };
    }
    case TRACE_EVENT_KIND.gate: {
      const effect = typeof event.sideEffectClass === 'string' && event.sideEffectClass.length > 0
        ? event.sideEffectClass
        : 'action';
      switch (event.status) {
        case 'pending':
          return { ...base, text: `Awaiting your approval to ${effect}` };
        case 'confirmed':
          return { ...base, text: `Approved — ${effect}` };
        case 'rejected':
          return { ...base, text: `Rejected — ${effect}` };
        case 'expired':
          return { ...base, text: `Approval expired — ${effect}` };
        case 'superseded':
          return { ...base, text: `Approval superseded — ${effect}` };
        default:
          return { ...base, text: `Approval gate — ${effect}` };
      }
    }
    default:
      // Unknown kind: skip (tolerant reader). Never fabricate a narration.
      return null;
  }
}

/**
 * Narrate an ORDERED trace stream. One line per real event (unknown kinds skipped),
 * ordered by `sequence`. This is the ENTIRE narration surface — there is no path by
 * which text not backed by a trace event enters the result, so a model-claimed step
 * with no corresponding event is structurally absent.
 */
export function narrateTrace(events: readonly TraceEventDto[]): NarrationLine[] {
  if (!Array.isArray(events) || events.length === 0) return [];
  return events
    .slice()
    .sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0))
    .map(narrateTraceEvent)
    .filter((line): line is NarrationLine => line !== null);
}
