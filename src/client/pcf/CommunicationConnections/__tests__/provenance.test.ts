/**
 * Provenance-derivation tests for the CommunicationConnections PCF.
 *
 * Verifies the multi-association model: candidates group into typed slots,
 * ambiguity surfaces as competing alternatives, and structural signals become
 * create-from-email suggestions. These are the contract the ported ConnectionsEditor
 * renders against (FR-17).
 */

import {
  parseProvenance,
  deriveConnections,
  deriveCreateActions,
  connectionTarget,
  confidenceBand,
  type ProvenanceDoc,
} from '../CommunicationConnections/provenance';

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

describe('parseProvenance', () => {
  it('returns null for empty / malformed JSON', () => {
    expect(parseProvenance(null)).toBeNull();
    expect(parseProvenance('')).toBeNull();
    expect(parseProvenance('{not json')).toBeNull();
  });
});

describe('deriveConnections', () => {
  it('groups candidates by field into typed slots ordered by SLOT_META', () => {
    const doc = docWith({
      candidates: [
        mkCand('sprk_regardingorganization', 'sprk_organization', 'org-1', 'Acme Corp', 0.8),
        mkCand('sprk_regardingmatter', 'sprk_matter', 'mtr-1', 'Acme v. Beta', 0.92),
        mkCand('sprk_regardingperson', 'contact', 'con-1', 'Jane Doe', 0.7),
      ],
    });

    const conns = deriveConnections(doc, false);
    expect(conns.map(c => c.slotLabel)).toEqual(['Matter', 'Organization', 'Contact']); // ordered
    expect(conns.every(c => c.status === 'suggested')).toBe(true);
    expect(conns[0].targetName).toBe('Acme v. Beta');
  });

  it('marks a slot ambiguous when two conflicting candidates share a field', () => {
    const doc = docWith({
      candidates: [
        { ...mkCand('sprk_regardingmatter', 'sprk_matter', 'mtr-1', 'Matter A', 0.88), conflict: true },
        { ...mkCand('sprk_regardingmatter', 'sprk_matter', 'mtr-2', 'Matter B', 0.86), conflict: true },
      ],
    });

    const conns = deriveConnections(doc, false);
    expect(conns).toHaveLength(1);
    expect(conns[0].status).toBe('ambiguous');
    expect(conns[0].alternatives).toHaveLength(2);
  });

  it('renders confirmed slots when the record is Resolved', () => {
    const doc = docWith({
      candidates: [mkCand('sprk_regardingmatter', 'sprk_matter', 'mtr-1', 'Matter A', 0.92)],
    });
    const conns = deriveConnections(doc, true);
    expect(conns[0].status).toBe('confirmed');
  });
});

describe('deriveCreateActions', () => {
  it('flags engine-suggested actions from signals and always offers the three', () => {
    const doc = docWith({
      signals: [
        { category: 'invoice', confidence: 0.9, provenance: 'INV-2025-01', obligations: [] },
        { category: 'other', confidence: 0.6, provenance: 'x', obligations: ['calendar-response'] },
      ],
    });
    const actions = deriveCreateActions(doc);
    expect(actions.map(a => a.kind).sort()).toEqual(['event', 'invoice', 'todo']);
    expect(actions.find(a => a.kind === 'invoice')?.suggested).toBe(true);
    expect(actions.find(a => a.kind === 'event')?.suggested).toBe(true);
    expect(actions.find(a => a.kind === 'todo')?.suggested).toBe(false);
  });
});

describe('connectionTarget', () => {
  it('maps a slot to the (entityType, recordId, recordName) the write path uses', () => {
    const doc = docWith({
      candidates: [mkCand('sprk_regardingmatter', 'sprk_matter', 'mtr-1', 'Matter A', 0.92)],
    });
    const [conn] = deriveConnections(doc, false);
    expect(connectionTarget(conn)).toEqual({
      entityType: 'sprk_matter',
      recordId: 'mtr-1',
      recordName: 'Matter A',
    });
  });
});

describe('confidenceBand', () => {
  it('bands on the FR-11 thresholds (0.85 high, 0.5 medium)', () => {
    expect(confidenceBand(0.9)).toBe('high');
    expect(confidenceBand(0.85)).toBe('high');
    expect(confidenceBand(0.6)).toBe('medium');
    expect(confidenceBand(0.49)).toBe('low');
  });
});

function mkCand(
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
