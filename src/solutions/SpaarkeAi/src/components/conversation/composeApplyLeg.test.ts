/**
 * composeApplyLeg.test.ts — FR-13 (task 046) draft-alternative APPLY leg.
 *
 * Covers AC3: the Assistant emits `workspace.compose_assistant_insert`
 * REFERENCING the ledger entry (`ledgerRef`), never the payload; and only for a
 * binding that actually wrote a `compose`-disposition output (draft-alternative),
 * not for informational actions.
 */

import { resolveCurrentComposeLedgerRef, buildComposeApplyEvent } from './composeApplyLeg';

describe('resolveCurrentComposeLedgerRef', () => {
  const outputs = [
    { key: 'binding-1@t1', bindingId: 'binding-1', turn: 1, disposition: 'compose', payload: {} },
    { key: 'binding-1@t2', bindingId: 'binding-1', turn: 2, disposition: 'compose', payload: {} },
    { key: 'binding-2@t1', bindingId: 'binding-2', turn: 1, disposition: 'informational', payload: {} },
  ];

  it('returns the highest-turn compose key for the binding (supersession → current)', () => {
    expect(resolveCurrentComposeLedgerRef(outputs, 'binding-1')).toBe('binding-1@t2');
  });

  it('returns null for a binding with no compose output (informational action → no apply leg)', () => {
    expect(resolveCurrentComposeLedgerRef(outputs, 'binding-2')).toBeNull();
  });

  it('returns null for an unknown binding', () => {
    expect(resolveCurrentComposeLedgerRef(outputs, 'no-such-binding')).toBeNull();
  });

  it('is tolerant of non-array / malformed input and an empty bindingId (never throws)', () => {
    expect(resolveCurrentComposeLedgerRef(null, 'binding-1')).toBeNull();
    expect(resolveCurrentComposeLedgerRef(undefined, 'binding-1')).toBeNull();
    expect(resolveCurrentComposeLedgerRef('nope' as unknown, 'binding-1')).toBeNull();
    expect(resolveCurrentComposeLedgerRef([null, 42, {}, { key: '', bindingId: 'binding-1', turn: 1, disposition: 'compose' }], 'binding-1')).toBeNull();
    expect(resolveCurrentComposeLedgerRef(outputs, '')).toBeNull();
  });
});

describe('buildComposeApplyEvent', () => {
  it('emits the EXISTING compose_assistant_insert discriminant referencing the ledger entry', () => {
    const evt = buildComposeApplyEvent('binding-1@t2', 'binding-1', 'sess-1');
    expect(evt.type).toBe('compose_assistant_insert');
    expect(evt.ledgerRef).toBe('binding-1@t2');
    expect(evt.sessionId).toBe('sess-1');
    expect(evt.sourceNodeId).toBe('binding-1');
  });

  it('NEVER carries the edit payload — contentHtml is empty (ADR-040 reference-not-payload)', () => {
    const evt = buildComposeApplyEvent('binding-1@t2', 'binding-1', 'sess-1');
    expect(evt.contentHtml).toBe('');
    // The load-bearing field is the ledger reference, not any inline body.
    expect(evt.ledgerRef).toBeTruthy();
  });
});
