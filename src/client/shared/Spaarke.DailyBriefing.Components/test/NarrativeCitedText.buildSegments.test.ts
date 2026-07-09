/**
 * Unit tests for `buildSegments` (NarrativeCitedText.tsx) — the regex-free
 * inline-citation segment splitter that wraps mentioned entity names in the
 * narrative text with clickable link segments.
 *
 * Task 035 (R5 Phase B.6 hardening, per notes/inbound-from-r7/03 item 2):
 * this helper shipped without tests in R7 W12. Non-trivial, regression-prone
 * logic — added `export` to the previously-module-private function (minimal
 * change per task constraint; no behavior change) so it can be unit tested
 * directly instead of through full component rendering.
 *
 * Covers:
 *   - Happy path: a single mentioned ref is wrapped as a link segment.
 *   - Empty narrative input → [] (short-circuit, regardless of refs).
 *   - No mentioned refs (non-empty narrative) → single plain-text segment.
 *   - Malformed/overlapping segment: two refs whose match ranges overlap
 *     (e.g. "Acme Corp" and "Acme") — the later, overlapping ref is skipped.
 *   - Missing/unresolvable itemId (empty entityId) → ref is skipped entirely,
 *     falls back to a plain-text segment.
 *   - Entity name too short (<3 chars) → skipped (avoids false-positive
 *     matches on tokens like "of"/"a").
 */

import { buildSegments } from '../src/components/NarrativeCitedText';
import type { NarrativeBulletReferenceResult } from '../src/services/briefingService';

function makeRef(overrides: Partial<NarrativeBulletReferenceResult> = {}): NarrativeBulletReferenceResult {
  return {
    index: 1,
    entityType: 'sprk_matter',
    entityId: 'm-1',
    entityName: 'Acme Corp',
    mentioned: true,
    ...overrides,
  };
}

describe('buildSegments', () => {
  it('happy path: wraps the mentioned entity name as a link segment surrounded by text', () => {
    const narrative = 'Acme Corp signed the amendment.';
    const ref = makeRef();

    const segments = buildSegments(narrative, [ref]);

    expect(segments).toEqual([
      { kind: 'link', display: 'Acme Corp', entityType: 'sprk_matter', entityId: 'm-1' },
      { kind: 'text', text: ' signed the amendment.' },
    ]);
  });

  it('empty input string: returns [] regardless of the mentioned refs supplied', () => {
    expect(buildSegments('', [makeRef()])).toEqual([]);
    expect(buildSegments('', [])).toEqual([]);
  });

  it('no mentioned refs with non-empty narrative: returns a single plain-text segment', () => {
    const narrative = 'No entities mentioned here.';
    expect(buildSegments(narrative, [])).toEqual([{ kind: 'text', text: narrative }]);
  });

  it('malformed/overlapping segment: a later ref whose match range overlaps an earlier match is skipped', () => {
    // "Acme" (ref2) first occurs at index 0, which overlaps the "Acme Corp"
    // (ref1) match at [0,9) — ref2 is dropped by the overlap-dedupe guard,
    // leaving one link segment (Acme Corp) plus the trailing text.
    const narrative = 'Acme Corp discussed Acme.';
    const refs = [
      makeRef({ index: 1, entityId: 'a1', entityName: 'Acme Corp' }),
      makeRef({ index: 2, entityId: 'a2', entityName: 'Acme' }),
    ];

    const segments = buildSegments(narrative, refs);

    expect(segments).toEqual([
      { kind: 'link', display: 'Acme Corp', entityType: 'sprk_matter', entityId: 'a1' },
      { kind: 'text', text: ' discussed Acme.' },
    ]);
  });

  it('missing/unresolvable itemId: a ref with an empty entityId is skipped, falling back to plain text', () => {
    const narrative = 'Acme Corp signed the amendment.';
    const ref = makeRef({ entityId: '' });

    const segments = buildSegments(narrative, [ref]);

    expect(segments).toEqual([{ kind: 'text', text: narrative }]);
  });

  it('entity name shorter than 3 characters is skipped (avoids false-positive token matches)', () => {
    const narrative = 'A brief note of no consequence.';
    const ref = makeRef({ entityName: 'of' });

    const segments = buildSegments(narrative, [ref]);

    expect(segments).toEqual([{ kind: 'text', text: narrative }]);
  });

  it('case-insensitive match: entity name casing in the ref differs from the narrative text', () => {
    const narrative = 'ACME CORP filed the motion.';
    const ref = makeRef({ entityName: 'Acme Corp' });

    const segments = buildSegments(narrative, [ref]);

    expect(segments[0]).toEqual({ kind: 'link', display: 'ACME CORP', entityType: 'sprk_matter', entityId: 'm-1' });
  });
});
