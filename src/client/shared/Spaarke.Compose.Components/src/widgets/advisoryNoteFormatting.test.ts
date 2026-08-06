/**
 * advisoryNoteFormatting.test.ts — ai-advanced-capabilities-agreements-r1 task 052 (FR-15,
 * Word-comment export mirror).
 *
 * Unit coverage for the shared source both `ComposeCommentGutter` and the Word-comment export
 * mapping consume (`getAdvisoryNoteSegments` / `composeAdvisoryCommentExportText`):
 *  1. Discrete task-002 fields (`flaggedClause`/`assessment`) compose directly — no string-parsing.
 *  2. Legacy (pre-002) marker-parsed `text` degrades gracefully — including the no-markers case
 *     (never fabricates structure).
 *  3. A plain (non-advisory) session comment's text exports completely unchanged.
 *  4. `standardRef` (+ optional `standardText`) is lifted into the export — the task 052 scope lift.
 *  5. A "recalled" thread fixture (same discrete fields, different provenance) composes byte-
 *     identical export text to an equivalent live thread — the durable-recall payload-driven proof
 *     (tasks 030-032 coordination).
 *
 * `parseAdvisoryNote`'s own marker-splitting behavior is covered exhaustively in
 * `ComposeCommentGutter.test.tsx` (moved here, re-exported there) — not duplicated below.
 */
import {
  composeAdvisoryCommentExportText,
  getAdvisoryNoteSegments,
  isAdvisoryCommentThread,
} from './advisoryNoteFormatting';

// ---------------------------------------------------------------------------
// 1. getAdvisoryNoteSegments — discrete fields win, legacy degrades
// ---------------------------------------------------------------------------

describe('getAdvisoryNoteSegments', () => {
  it('builds segments directly from discrete flaggedClause/assessment fields — no parsing', () => {
    const segments = getAdvisoryNoteSegments({
      text: 'ignored when discrete fields are present',
      flaggedClause: 'The clause imposes a best-efforts standard.',
      assessment: 'This is materially weaker than the firm standard.',
    });
    expect(segments).toEqual([
      { label: 'Flagged clause', body: 'The clause imposes a best-efforts standard.' },
      { label: 'Assessment says', body: 'This is materially weaker than the firm standard.' },
    ]);
  });

  it('omits the Assessment segment when only flaggedClause is present', () => {
    const segments = getAdvisoryNoteSegments({ text: 'ignored', flaggedClause: 'Just the fact.' });
    expect(segments).toEqual([{ label: 'Flagged clause', body: 'Just the fact.' }]);
  });

  it('degrades to legacy marker-parsing when no discrete fields are present', () => {
    const segments = getAdvisoryNoteSegments({
      text: 'Grounded fact: A best-efforts standard. Advisory judgment: Needs reasonable care.',
    });
    expect(segments).toEqual([
      { label: 'Flagged clause', body: 'A best-efforts standard.' },
      { label: 'Assessment says', body: 'Needs reasonable care.' },
    ]);
  });

  it('legacy text with no recognized markers returns a single unlabelled segment (no fabricated structure)', () => {
    const segments = getAdvisoryNoteSegments({ text: 'Just plain explanation prose, no markers.' });
    expect(segments).toEqual([{ body: 'Just plain explanation prose, no markers.' }]);
  });
});

// ---------------------------------------------------------------------------
// 2. isAdvisoryCommentThread — the plain-vs-advisory discriminant
// ---------------------------------------------------------------------------

describe('isAdvisoryCommentThread', () => {
  it('is false for a plain thread with no advisory-specific metadata', () => {
    expect(isAdvisoryCommentThread({ text: 'Just a regular comment.' })).toBe(false);
  });

  it('is true when any advisory field is present', () => {
    expect(isAdvisoryCommentThread({ text: 't', sectionRef: '3.2' })).toBe(true);
    expect(isAdvisoryCommentThread({ text: 't', riskLevel: 'High' })).toBe(true);
    expect(isAdvisoryCommentThread({ text: 't', standardRef: 'B5' })).toBe(true);
    expect(isAdvisoryCommentThread({ text: 't', flaggedClause: 'x' })).toBe(true);
    expect(isAdvisoryCommentThread({ text: 't', assessment: 'x' })).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// 3. composeAdvisoryCommentExportText — the Word-comment export composition
// ---------------------------------------------------------------------------

describe('composeAdvisoryCommentExportText', () => {
  it('exports a plain (non-advisory) session comment completely unchanged', () => {
    const text = composeAdvisoryCommentExportText({ text: 'Please clarify this paragraph.' });
    expect(text).toBe('Please clarify this paragraph.');
  });

  it('composes structured export text from discrete fields + standardRef (the task-002 no-parsing path)', () => {
    const text = composeAdvisoryCommentExportText({
      text: 'ignored',
      sectionRef: '3.2',
      riskLevel: 'High',
      flaggedClause: 'The clause imposes a best-efforts standard.',
      assessment: 'This is materially weaker than the firm standard.',
      standardRef: 'B5 - Use & disclosure obligations',
    });
    expect(text).toBe(
      'Flagged clause: The clause imposes a best-efforts standard.\n\n' +
        'Assessment says: This is materially weaker than the firm standard.\n\n' +
        'Standard: B5 - Use & disclosure obligations'
    );
  });

  it('includes the full standard clause text when the thread carries standardText ("full clause text when available")', () => {
    const text = composeAdvisoryCommentExportText({
      text: 'ignored',
      sectionRef: '3.2',
      flaggedClause: 'The clause imposes a best-efforts standard.',
      assessment: 'Weaker than firm standard.',
      standardRef: 'B5',
      standardText: 'The receiving party shall use reasonable care, being no less than industry-standard diligence.',
    });
    expect(text).toContain(
      'Standard: B5 — The receiving party shall use reasonable care, being no less than industry-standard diligence.'
    );
  });

  it('degrades a legacy advisory thread (marker-parsed text, no discrete fields) and still lifts standardRef', () => {
    const text = composeAdvisoryCommentExportText({
      text: 'Grounded fact: A best-efforts standard. Advisory judgment: Needs reasonable care.',
      sectionRef: '3.2', // present ⇒ advisory, even though flaggedClause/assessment are absent
      standardRef: 'B5',
    });
    expect(text).toBe(
      'Flagged clause: A best-efforts standard.\n\nAssessment says: Needs reasonable care.\n\nStandard: B5'
    );
  });

  it('a legacy advisory thread with unrecognized text (no markers) exports the raw text plus Standard — never crashes or fabricates structure', () => {
    const text = composeAdvisoryCommentExportText({
      text: 'A plain explanation with no markers at all.',
      riskLevel: 'Medium', // marks it advisory
      standardRef: 'B2',
    });
    expect(text).toBe('A plain explanation with no markers at all.\n\nStandard: B2');
  });

  it('an advisory thread with no standardRef omits the Standard line entirely', () => {
    const text = composeAdvisoryCommentExportText({
      text: 'ignored',
      flaggedClause: 'Fact.',
      assessment: 'Judgment.',
    });
    expect(text).not.toContain('Standard:');
  });

  // -------------------------------------------------------------------------
  // 4. Durable-recall payload-driven parity (tasks 030-032 coordination)
  // -------------------------------------------------------------------------
  it('a re-materialized (recalled) thread with the same discrete fields composes BYTE-IDENTICAL export text to a live one', () => {
    const liveThread = {
      id: 'thread-live-1',
      text: 'ignored',
      author: 'AI Advisory Review',
      timestamp: '2026-07-31T00:00:00.000Z',
      sectionRef: '4.1',
      riskLevel: 'High',
      flaggedClause: 'The clause allows unilateral termination without notice.',
      assessment: 'This removes the standard 30-day cure period.',
      standardRef: 'B9 - Termination rights',
    };
    // Same discrete fields, different id/timestamp/provenance (simulating a durable-recall
    // re-materialization via placeAdvisoryComments on document reopen — task 030-032).
    const recalledThread = {
      ...liveThread,
      id: 'thread-recalled-1',
      timestamp: '2026-08-01T09:00:00.000Z',
    };

    expect(composeAdvisoryCommentExportText(recalledThread)).toBe(composeAdvisoryCommentExportText(liveThread));
  });
});
