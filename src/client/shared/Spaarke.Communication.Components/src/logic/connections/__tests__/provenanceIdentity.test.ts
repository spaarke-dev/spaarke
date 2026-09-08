/**
 * Provenance identity + full-candidate-list tests (email-communication-intelligence-r2).
 *
 *  - R3-CARD-1: `looksLikeGuid` classifies a raw Dataverse GUID vs a real name, and
 *    `deriveConnections` falls back to a contributor's embedded `name="…"` when the flat
 *    candidate carries no `targetName` (so a card shows the name, never the opaque GUID).
 *  - R3-CARD-2: `derivePrimaryReview` exposes the FULL above-floor candidate set
 *    (`allCandidates`) behind the top-3 (`PRIMARY_CANDIDATE_SLOTS`) strip cap — the data
 *    the "See all" modal renders.
 */
import {
  looksLikeGuid,
  deriveConnections,
  derivePrimaryReview,
  parseProvenance,
  type ProvenanceCandidate,
} from '../provenance';

const GUID = '11111111-2222-3333-4444-555555555555';
const STATUS_PENDING = 100000001;

/** Raw provenance candidate. `name`/`number` (when given) land inside a RecordNameMatch contributor string. */
function rawCand(
  field: string,
  entity: string,
  id: string,
  confidence: number,
  opts: { targetName?: string; name?: string; number?: string } = {}
): ProvenanceCandidate {
  const contributors = [];
  if (opts.name || opts.number) {
    contributors.push({
      rung: 'RecordNameMatch',
      confidence,
      provenance: `record-name-match:${entity}:where=subject:matched=name:name="${opts.name ?? ''}":number="${
        opts.number ?? ''
      }":reason="name in subject"`,
    });
  }
  return {
    field,
    targetEntity: entity,
    targetId: id,
    targetName: opts.targetName,
    reinforcedConfidence: confidence,
    deterministicConfidence: confidence,
    written: false,
    conflict: false,
    contributors,
  };
}

function provenanceJson(candidates: ProvenanceCandidate[]): string {
  return JSON.stringify({
    version: 1,
    direction: 'inbound',
    decision: {
      status: '',
      autoFiled: false,
      killSwitchEnabled: false,
      autoFileThreshold: 0.85,
      topDeterministicConfidence: 0,
      topConfidence: 0,
      aiInvolved: false,
      reason: '',
    },
    rungsFired: [],
    candidates,
    signals: [],
  });
}

describe('R3-CARD-1 · looksLikeGuid', () => {
  it('is TRUE for a bare 8-4-4-4-12 GUID and a brace-wrapped one', () => {
    expect(looksLikeGuid(GUID)).toBe(true);
    expect(looksLikeGuid(`{${GUID}}`)).toBe(true);
    expect(looksLikeGuid(GUID.toUpperCase())).toBe(true);
  });

  it('is FALSE for a human record name, a record number, empty, null and undefined', () => {
    expect(looksLikeGuid('Acme v Beta')).toBe(false);
    expect(looksLikeGuid('MAT-2026-000123')).toBe(false);
    expect(looksLikeGuid('')).toBe(false);
    expect(looksLikeGuid(null)).toBe(false);
    expect(looksLikeGuid(undefined)).toBe(false);
  });
});

describe('R3-CARD-1 · deriveConnections name fallback', () => {
  it('uses a contributor\'s embedded name="…" when the flat candidate has no targetName (never the raw GUID)', () => {
    const doc = parseProvenance(
      provenanceJson([
        rawCand('sprk_regardingmatter', 'sprk_matter', GUID, 0.9, { name: 'Acme v Beta', number: 'MAT-1' }),
      ])
    )!;
    const [conn] = deriveConnections(doc, false);
    expect(conn.targetName).toBe('Acme v Beta');
    expect(conn.targetName).not.toBe(GUID);
    expect(looksLikeGuid(conn.targetName)).toBe(false);
  });

  it('falls back to the raw targetId only when NO name is recoverable (documents the GUID-only case the card guards)', () => {
    const doc = parseProvenance(
      // thread/attachment match: a GUID target, no targetName, no name="…" contributor.
      provenanceJson([
        {
          field: 'sprk_regardingmatter',
          targetEntity: 'sprk_matter',
          targetId: GUID,
          reinforcedConfidence: 0.9,
          deterministicConfidence: 0.9,
          written: false,
          conflict: false,
          contributors: [{ rung: 'ThreadContinuity', confidence: 0.9, provenance: 'thread-continuity' }],
        },
      ])
    )!;
    const [conn] = deriveConnections(doc, false);
    expect(conn.targetName).toBe(GUID);
    expect(looksLikeGuid(conn.targetName)).toBe(true); // the card renders type+reason instead of this
  });
});

describe('R3-CARD-2 · derivePrimaryReview.allCandidates (full set behind the top-3 cap)', () => {
  it('caps `candidates` at 3 but exposes ALL above-floor candidates in `allCandidates`, ranked', () => {
    const json = provenanceJson([
      rawCand('sprk_regardingmatter', 'sprk_matter', 'mtr-1', 0.95, { name: 'Alpha', number: 'MAT-1' }),
      rawCand('sprk_regardingmatter', 'sprk_matter', 'mtr-2', 0.9, { name: 'Bravo', number: 'MAT-2' }),
      rawCand('sprk_regardingmatter', 'sprk_matter', 'mtr-3', 0.85, { name: 'Charlie', number: 'MAT-3' }),
      rawCand('sprk_regardingmatter', 'sprk_matter', 'mtr-4', 0.8, { name: 'Delta', number: 'MAT-4' }),
    ]);
    const model = derivePrimaryReview(json, STATUS_PENDING, []);

    expect(model.candidates).toHaveLength(3);
    expect(model.allCandidates).toHaveLength(4);
    // Ranked highest-first; the hidden 4th is the lowest above-floor candidate.
    expect(model.allCandidates.map(c => c.targetName)).toEqual(['Alpha', 'Bravo', 'Charlie', 'Delta']);
    expect(model.candidates.map(c => c.targetName)).toEqual(['Alpha', 'Bravo', 'Charlie']);
    expect(model.candidates.map(c => c.targetName)).not.toContain('Delta');
  });

  it('drops below-floor (<70%) candidates from BOTH lists', () => {
    const json = provenanceJson([
      rawCand('sprk_regardingmatter', 'sprk_matter', 'mtr-1', 0.95, { name: 'Alpha' }),
      rawCand('sprk_regardingmatter', 'sprk_matter', 'mtr-weak', 0.4, { name: 'TooWeak' }),
    ]);
    const model = derivePrimaryReview(json, STATUS_PENDING, []);
    expect(model.allCandidates.map(c => c.targetName)).toEqual(['Alpha']);
  });

  it('leaves allCandidates empty when there are no candidates', () => {
    const model = derivePrimaryReview(provenanceJson([]), STATUS_PENDING, []);
    expect(model.allCandidates).toEqual([]);
    expect(model.candidates).toEqual([]);
  });
});
