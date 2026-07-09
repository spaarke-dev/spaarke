/**
 * executionTraceNarration — unit tests (AIR2-038 / FR-A1-09).
 *
 * The load-bearing invariant: live plan narration streams from REAL trace
 * events ONLY — never model prose. These tests pin that structurally:
 *  - a narration line is produced for each real event, ordered by sequence;
 *  - a step the model merely CLAIMED (absent from the event stream) never
 *    appears in the narration (the negative case);
 *  - narration text carries identifiers/counts only (NFR-07) — no content.
 */

import {
  narrateTrace,
  narrateTraceEvent,
  TRACE_EVENT_KIND,
  type TraceEventDto,
} from '../executionTraceNarration';

describe('executionTraceNarration', () => {
  const context = (turn: number, slices: number): TraceEventDto => ({
    sequence: 0,
    turn,
    kind: TRACE_EVENT_KIND.context,
    fingerprintId: `fp-${turn}`,
    contextSliceCount: slices,
  });
  const toolChain = (turn: number, n: number, seq: number): TraceEventDto => ({
    sequence: seq,
    turn,
    kind: TRACE_EVENT_KIND.toolChain,
    toolCallCount: n,
  });
  const toolCall = (turn: number, toolId: string, seq: number): TraceEventDto => ({
    sequence: seq,
    turn,
    kind: TRACE_EVENT_KIND.toolCall,
    toolId,
    resultCount: 3,
    durationMs: 42,
  });
  const gate = (turn: number, status: string, seq: number): TraceEventDto => ({
    sequence: seq,
    turn,
    kind: TRACE_EVENT_KIND.gate,
    gateId: `g-${turn}`,
    gateKind: 'confirmation',
    status,
    sideEffectClass: 'write',
  });

  it('narrates one line per real event, ordered by sequence (full lineage)', () => {
    const events: TraceEventDto[] = [
      context(1, 4),
      toolChain(1, 1, 1),
      toolCall(1, 'sprk_document_search', 2),
      gate(1, 'pending', 3),
    ];
    const lines = narrateTrace(events);
    expect(lines.map(l => l.kind)).toEqual(['context', 'tool_chain', 'tool_call', 'gate']);
    expect(lines[0].text).toBe('Consulted 4 context slices');
    expect(lines[1].text).toBe('Selected 1 tool');
    expect(lines[2].text).toContain('Ran sprk_document_search');
    expect(lines[3].text).toBe('Awaiting your approval to write');
    // Every line ties back to a real event's sequence (provenance anchor).
    expect(lines.map(l => l.sourceSequence)).toEqual([0, 1, 2, 3]);
  });

  it('NEGATIVE: a model-claimed step with no corresponding event never appears', () => {
    // The agent REALLY ran only a document search. The model prose may have
    // "claimed" it also sent an email — but no send_email event exists, so it
    // must be structurally absent from the narration.
    const realEvents: TraceEventDto[] = [
      toolChain(1, 1, 0),
      toolCall(1, 'sprk_document_search', 1),
    ];
    const lines = narrateTrace(realEvents);
    const joined = lines.map(l => l.text).join(' | ');
    expect(joined).toContain('sprk_document_search');
    expect(joined).not.toMatch(/email/i);
    expect(joined).not.toMatch(/send/i);
    // There is no API surface through which un-evented text could enter:
    // narrateTrace accepts only TraceEventDto[]. Passing the exact real events
    // yields exactly two lines — no fabricated third step.
    expect(lines).toHaveLength(2);
  });

  it('renders gate states honestly (pending / confirmed / rejected)', () => {
    expect(narrateTraceEvent(gate(1, 'pending', 0))?.text).toBe('Awaiting your approval to write');
    expect(narrateTraceEvent(gate(1, 'confirmed', 0))?.text).toBe('Approved — write');
    expect(narrateTraceEvent(gate(1, 'rejected', 0))?.text).toBe('Rejected — write');
  });

  it('handles a turn that selected NO tools (partial state, not dropped)', () => {
    expect(narrateTraceEvent(toolChain(2, 0, 0))?.text).toBe('Selected no tools this turn');
  });

  it('skips an unknown future kind (tolerant reader — never fabricates)', () => {
    const unknown = { sequence: 0, turn: 1, kind: 'quantum_leap' } as unknown as TraceEventDto;
    expect(narrateTraceEvent(unknown)).toBeNull();
    expect(narrateTrace([unknown, toolCall(1, 'sprk_x', 1)])).toHaveLength(1);
  });

  it('NFR-07: narration carries identifiers/counts only — no content field leaks', () => {
    // Even if a misbehaving producer attaches a content-bearing field, the
    // narration is built from typed fields ONLY (never a spread), so it cannot
    // surface. The wire type has no content member; assert the text is clean.
    const rogue = {
      ...toolCall(1, 'sprk_document_search', 0),
      // @ts-expect-error — content member is not on the contract; simulate a leak
      responseText: 'SECRET privileged attorney work product',
    } as TraceEventDto;
    const line = narrateTraceEvent(rogue);
    expect(line?.text).not.toContain('SECRET');
    expect(line?.text).not.toContain('privileged');
  });

  it('returns empty for empty / non-array input', () => {
    expect(narrateTrace([])).toEqual([]);
    expect(narrateTrace(undefined as unknown as TraceEventDto[])).toEqual([]);
  });
});
