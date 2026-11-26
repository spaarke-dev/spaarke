/**
 * derivePrimaryReviewWrittenRegarding.test.ts — spaarkeai-word-add-in-r1 task 140 (ISS-043, #3194).
 *
 * A communication that is NOT Resolved but has its regarding WRITTEN (a filed typed lookup, or the denorm primary) shows
 * that record as its primary in 'needs-confirmation' — never "No confident match". The dev row that surfaced it: status
 * Suggested, a matter written (the grid's link chip), and a provenance trail whose `candidates` is EMPTY (the trail ran
 * past the column cap and the writer dropped every candidate). Ids here are synthetic.
 *
 * Kept: a row with no regarding written is still 'requires-review'; an Ambiguous conflict still stays red; Resolved is
 * still 'confirmed'.
 */
import {
  derivePrimaryReview,
  summarizePrimaryReview,
  ASSOCIATION_STATUS_RESOLVED_VALUE,
  type FiledAssociation,
} from '../provenance';

const SUGGESTED = 100000003;
const AMBIGUOUS = 100000004;
const MATTER_ID = 'aaaaaaaa-0000-4000-8000-000000000001';
const OTHER_MATTER_ID = 'aaaaaaaa-0000-4000-8000-000000000002';

/** The ISS-043 provenance shape (dev row, ids removed): Suggested, not auto-filed, four rungs fired, NO candidates. */
const ISS043_PROVENANCE = JSON.stringify({
  version: 1,
  direction: 'Incoming',
  decision: {
    status: 'Suggested',
    autoFiled: false,
    killSwitchEnabled: true,
    autoFileThreshold: 0.85,
    topDeterministicConfidence: 0.65,
    topConfidence: 0.9895,
    aiInvolved: false,
    reason:
      'Reinforced confidence 0.99 ≥ threshold 0.85 only via rung(s) outside the auto-file-eligible set (e.g. participant/structural correlation, record/contact name match, or affinity); those rungs do not clear the auto-file bar alone ⇒ Suggested.',
  },
  rungsFired: ['AiClassification', 'ExplicitReference', 'ParticipantCorrelation', 'RecordNameMatch'],
  candidates: [],
  signals: [],
});

const FILED_MATTER: FiledAssociation[] = [
  { entityType: 'sprk_matter', recordId: MATTER_ID, recordName: 'Acme v Beta' },
];
const DENORM = { recordName: 'Acme v Beta', recordNumber: 'MAT-000123', recordTypeLabel: 'Matter' };

function candidate(targetId: string, confidence: number, extra: Record<string, unknown> = {}) {
  return {
    field: 'sprk_regardingmatter',
    targetEntity: 'sprk_matter',
    targetId,
    reinforcedConfidence: confidence,
    deterministicConfidence: 0.65,
    written: false,
    conflict: false,
    contributors: [{ rung: 'RecordNameMatch', confidence, provenance: 'record-name-match:sprk_matter:where=subject' }],
    ...extra,
  };
}

function provenanceWith(candidates: unknown[], status = 'Suggested'): string {
  const doc = JSON.parse(ISS043_PROVENANCE);
  doc.candidates = candidates;
  doc.decision.status = status;
  return JSON.stringify(doc);
}

describe('derivePrimaryReview — a written regarding that no human confirmed (task 140)', () => {
  it('ISS-043 fixture: Suggested, matter written, empty candidates → needs-confirmation with the matter as primary', () => {
    const model = derivePrimaryReview(ISS043_PROVENANCE, SUGGESTED, FILED_MATTER, DENORM);

    expect(model.state).toBe('needs-confirmation');
    expect(model.primary).toMatchObject({ entity: 'sprk_matter', targetId: MATTER_ID, targetName: 'Acme v Beta' });
    expect(model.primary?.recordNumber).toBe('MAT-000123');
    // The panel renders the primary as its (green, confirmable) card, so it never falls back to "No confident match".
    expect(model.candidates).toHaveLength(1);
    expect(model.candidates[0]).toMatchObject({ entity: 'sprk_matter', targetId: MATTER_ID });
    expect(model.allCandidates[0]).toMatchObject({ entity: 'sprk_matter', targetId: MATTER_ID });
    expect(summarizePrimaryReview(model)).toEqual({ tone: 'yellow', label: 'Needs confirmation' });
  });

  it('a denorm-only regarding (no typed lookup read) is the primary too, with no card it cannot confirm from', () => {
    const model = derivePrimaryReview(ISS043_PROVENANCE, SUGGESTED, [], DENORM);

    expect(model.state).toBe('needs-confirmation');
    expect(model.primary).toMatchObject({ targetName: 'Acme v Beta', recordNumber: 'MAT-000123', typeLabel: 'Matter' });
    expect(model.candidates).toHaveLength(0);
  });

  it('a written matter that IS a candidate is the primary, and the candidate list is not duplicated', () => {
    const json = provenanceWith([candidate(MATTER_ID, 0.93, { written: true }), candidate(OTHER_MATTER_ID, 0.81)]);

    const model = derivePrimaryReview(json, SUGGESTED, FILED_MATTER, DENORM);

    expect(model.state).toBe('needs-confirmation');
    expect(model.primary).toMatchObject({ targetId: MATTER_ID, confidence: 0.93 });
    expect(model.candidates.map(c => c.targetId)).toEqual([MATTER_ID, OTHER_MATTER_ID]);
  });

  it('a status left empty (never evaluated) with a written regarding is also needs-confirmation', () => {
    const model = derivePrimaryReview(null, null, FILED_MATTER, undefined);

    expect(model.state).toBe('needs-confirmation');
    expect(model.primary).toMatchObject({ targetId: MATTER_ID });
  });

  // ── Must not change ──────────────────────────────────────────────────────────────────────────────────────────────

  it('no regarding written (no filed lookup, no denorm) → still requires-review with no primary', () => {
    const model = derivePrimaryReview(ISS043_PROVENANCE, SUGGESTED, [], undefined);

    expect(model.state).toBe('requires-review');
    expect(model.primary).toBeUndefined();
    expect(summarizePrimaryReview(model).tone).toBe('red');
  });

  it('no regarding written but candidates present → still requires-review over those candidates', () => {
    const json = provenanceWith([candidate(MATTER_ID, 0.9), candidate(OTHER_MATTER_ID, 0.8)]);

    const model = derivePrimaryReview(json, SUGGESTED, [], { recordName: null });

    expect(model.state).toBe('requires-review');
    expect(model.candidates.map(c => c.targetId)).toEqual([MATTER_ID, OTHER_MATTER_ID]);
  });

  it('an Ambiguous conflict stays red even with a regarding written on another field', () => {
    const json = provenanceWith(
      [
        candidate(MATTER_ID, 0.95, { conflict: true, field: 'sprk_regardingproject', targetEntity: 'sprk_project' }),
        candidate(OTHER_MATTER_ID, 0.92, {
          conflict: true,
          field: 'sprk_regardingproject',
          targetEntity: 'sprk_project',
        }),
      ],
      'Ambiguous'
    );

    const model = derivePrimaryReview(json, AMBIGUOUS, FILED_MATTER, undefined);

    expect(model.state).toBe('requires-review');
    expect(summarizePrimaryReview(model).tone).toBe('red');
  });

  it('an Ambiguous status with an empty (capped) trail stays red too', () => {
    const model = derivePrimaryReview(provenanceWith([], 'Ambiguous'), AMBIGUOUS, FILED_MATTER, DENORM);

    expect(model.state).toBe('requires-review');
  });

  it('Resolved is still confirmed, with the filed record as primary', () => {
    const model = derivePrimaryReview(ISS043_PROVENANCE, ASSOCIATION_STATUS_RESOLVED_VALUE, FILED_MATTER, DENORM);

    expect(model.state).toBe('confirmed');
    expect(model.primary).toMatchObject({ targetId: MATTER_ID });
  });
});
