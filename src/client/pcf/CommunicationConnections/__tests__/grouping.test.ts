/**
 * Group-by-action tests for the rebuilt CommunicationConnections review surface
 * (task 105 / UAT R2 C1).
 *
 * The rebuild groups matches by the ACTION the reviewer must take — "Needs your
 * decision" (conflicts), "Filed automatically" (auto-linked), "Suggested"
 * (confirm/dismiss) — off the SAME provenance candidates + statuses. These tests
 * lock the pure partition (`groupConnectionsByAction`) and the confirm / dismiss
 * interaction effects (confirmedFields moves a row to Filed; a dismissed key hides
 * a suggestion) without rendering.
 */

import {
  parseProvenance,
  deriveConnections,
  deriveAiSuggestedTypes,
  groupConnectionsByAction,
  isConnectionConfirmed,
  type Connection,
  type AiSuggestedType,
  type ProvenanceDoc,
} from '../CommunicationConnections/provenance';

function mkConn(partial: Partial<Connection> & Pick<Connection, 'field' | 'status'>): Connection {
  return {
    field: partial.field,
    entity: partial.entity ?? 'sprk_matter',
    slotLabel: partial.slotLabel ?? 'Matter',
    targetName: partial.targetName ?? 'Some Record',
    targetId: partial.targetId ?? 'rec-1',
    confidence: partial.confidence ?? 0.9,
    status: partial.status,
    order: partial.order ?? 1,
    alternatives: partial.alternatives,
    otherCandidates: partial.otherCandidates,
    matchReason: partial.matchReason,
    recordNumber: partial.recordNumber,
  };
}

function docWith(partial: Partial<ProvenanceDoc>): ProvenanceDoc {
  return {
    version: 1,
    direction: 'inbound',
    decision: {
      status: 'Suggested',
      autoFiled: false,
      killSwitchEnabled: true,
      autoFileThreshold: 0.85,
      topDeterministicConfidence: 0.7,
      topConfidence: 0.7,
      aiInvolved: false,
      reason: 'test',
    },
    rungsFired: [],
    candidates: [],
    signals: [],
    ...partial,
  };
}

const NO_DISMISS = new Set<string>();
const NO_CONFIRM = new Set<string>();

describe('isConnectionConfirmed', () => {
  it('is confirmed by engine status', () => {
    expect(isConnectionConfirmed(mkConn({ field: 'f', status: 'confirmed' }), NO_CONFIRM)).toBe(true);
  });
  it('is confirmed by the App-controlled confirmedFields set (confirm interaction)', () => {
    expect(isConnectionConfirmed(mkConn({ field: 'f', status: 'suggested' }), new Set(['f']))).toBe(true);
  });
  it('is not confirmed otherwise', () => {
    expect(isConnectionConfirmed(mkConn({ field: 'f', status: 'suggested' }), NO_CONFIRM)).toBe(false);
  });
});

describe('groupConnectionsByAction', () => {
  it('partitions ambiguous → needsDecision, confirmed → filed, suggested → suggested', () => {
    const conns = [
      mkConn({ field: 'sprk_regardingmatter', status: 'ambiguous', slotLabel: 'Matter' }),
      mkConn({ field: 'sprk_regardingproject', status: 'confirmed', slotLabel: 'Project', entity: 'sprk_project' }),
      mkConn({ field: 'sprk_regardingperson', status: 'suggested', slotLabel: 'Contact', entity: 'contact' }),
    ];
    const g = groupConnectionsByAction(conns, [], NO_CONFIRM, NO_DISMISS);
    expect(g.needsDecision.map(c => c.field)).toEqual(['sprk_regardingmatter']);
    expect(g.filed.map(c => c.field)).toEqual(['sprk_regardingproject']);
    expect(g.suggested.map(c => c.field)).toEqual(['sprk_regardingperson']);
  });

  it('moves a suggested row into Filed once the App confirms it (confirmedFields)', () => {
    const conns = [mkConn({ field: 'sprk_regardingperson', status: 'suggested', entity: 'contact' })];
    const before = groupConnectionsByAction(conns, [], NO_CONFIRM, NO_DISMISS);
    expect(before.suggested).toHaveLength(1);
    expect(before.filed).toHaveLength(0);

    const after = groupConnectionsByAction(conns, [], new Set(['sprk_regardingperson']), NO_DISMISS);
    expect(after.suggested).toHaveLength(0);
    expect(after.filed.map(c => c.field)).toEqual(['sprk_regardingperson']);
  });

  it('a confirmed field wins even when the slot is ambiguous (resolved conflict → Filed, not Needs decision)', () => {
    const conns = [mkConn({ field: 'sprk_regardingmatter', status: 'ambiguous' })];
    const g = groupConnectionsByAction(conns, [], new Set(['sprk_regardingmatter']), NO_DISMISS);
    expect(g.needsDecision).toHaveLength(0);
    expect(g.filed.map(c => c.field)).toEqual(['sprk_regardingmatter']);
  });

  it('hides a dismissed suggestion (dismiss interaction) but keeps other suggestions', () => {
    const conns = [
      mkConn({ field: 'sprk_regardingperson', status: 'suggested', entity: 'contact' }),
      mkConn({ field: 'sprk_regardingorganization', status: 'suggested', entity: 'sprk_organization' }),
    ];
    const g = groupConnectionsByAction(conns, [], NO_CONFIRM, new Set(['sprk_regardingperson']));
    expect(g.suggested.map(c => c.field)).toEqual(['sprk_regardingorganization']);
  });

  it('includes AI create-suggestions and hides dismissed ones (ai:<entityType> key)', () => {
    const ai: AiSuggestedType[] = [
      { entityType: 'sprk_matter', label: 'Matter', reason: 'AI flagged a new matter' },
      { entityType: 'sprk_invoice', label: 'Invoice', reason: 'AI flagged an invoice' },
    ];
    const g = groupConnectionsByAction([], ai, NO_CONFIRM, new Set(['ai:sprk_matter']));
    expect(g.aiSuggested.map(a => a.entityType)).toEqual(['sprk_invoice']);
  });

  it('groups end-to-end off real provenance (ambiguous matter + suggested contact + AI type)', () => {
    const doc = docWith({
      candidates: [
        { ...cand('sprk_regardingmatter', 'sprk_matter', 'm1', 'Matter A', 0.88), conflict: true },
        { ...cand('sprk_regardingmatter', 'sprk_matter', 'm2', 'Matter B', 0.86), conflict: true },
        cand('sprk_regardingperson', 'contact', 'c1', 'Jane Doe', 0.6),
      ],
      signals: [{ category: 'ai', confidence: 0.7, provenance: 'types=[sprk_invoice]', obligations: [] }],
    });
    const conns = deriveConnections(doc, false);
    const ai = deriveAiSuggestedTypes(doc, new Set(conns.map(c => c.entity)));
    const g = groupConnectionsByAction(conns, ai, NO_CONFIRM, NO_DISMISS);

    expect(g.needsDecision.map(c => c.slotLabel)).toEqual(['Matter']);
    expect(g.needsDecision[0].alternatives).toHaveLength(2);
    expect(g.suggested.map(c => c.slotLabel)).toEqual(['Contact']);
    expect(g.aiSuggested.map(a => a.entityType)).toEqual(['sprk_invoice']);
    expect(g.filed).toHaveLength(0);
  });
});

function cand(
  field: string,
  targetEntity: string,
  targetId: string,
  targetName: string,
  confidence: number
): ProvenanceDoc['candidates'][number] {
  return {
    field,
    targetEntity,
    targetId,
    targetName,
    reinforcedConfidence: confidence,
    deterministicConfidence: confidence,
    written: false,
    conflict: false,
    contributors: [{ rung: 'ParticipantCorrelation', confidence, provenance: 'test' }],
  };
}

// Sanity: the doc-based fixtures parse as expected (guards the fixture shape).
describe('fixture sanity', () => {
  it('parseProvenance round-trips the fixture', () => {
    const doc = docWith({});
    expect(parseProvenance(JSON.stringify(doc))).toEqual(doc);
  });
});
