/**
 * reviewMemoFormatting.test.ts — pure unit tests for the "Create Summary Memo" toolbar's negative-path
 * message selection (agreements-r1 UAT round-1 #2: SPLIT the two formerly-conflated conditions).
 *
 * These assert the CLIENT half of the split — `selectMemoNegativeMessage` picks an honest, actionable
 * banner from the HTTP status + the server's ProblemDetails `code` extension. The SERVER half (the two
 * distinct titles + codes) is covered by ReviewMemoEndpointContractTests.cs.
 */
import {
  selectMemoNegativeMessage,
  buildGenerateReviewSummaryRequest,
  MEMO_NO_MEMO_MESSAGE,
  MEMO_NO_FINDINGS_MESSAGE,
  MEMO_SESSION_NOT_BOUND_MESSAGE,
  UNSPECIFIED_OVERALL_RISK,
} from './reviewMemoFormatting';

describe('selectMemoNegativeMessage — split negative conditions (UAT round-1 #2)', () => {
  it('404 (no code) → "generate first" message', () => {
    expect(selectMemoNegativeMessage(404, null)).toBe(MEMO_NO_MEMO_MESSAGE);
  });

  it('code "no-memo" → "generate first" message (code-driven, status-agnostic)', () => {
    expect(selectMemoNegativeMessage(404, 'no-memo')).toBe(MEMO_NO_MEMO_MESSAGE);
  });

  it('400 + "session-not-bound" → "promote to an Analysis first" message', () => {
    expect(selectMemoNegativeMessage(400, 'session-not-bound')).toBe(MEMO_SESSION_NOT_BOUND_MESSAGE);
  });

  it('the not-bound message does NOT claim there is no completed review (the fixed conflation)', () => {
    // The bug: the old server message said "there is no completed review to memo-ize" even when a
    // review HAD completed. The replacement message must not repeat that false claim.
    expect(MEMO_SESSION_NOT_BOUND_MESSAGE.toLowerCase()).not.toContain('no completed review');
    // UAT (2026-08-18, owner): the flow is SAVE-driven and History-free — the message must tell the user
    // to SAVE the document (which creates its Analysis), NOT to promote via Assistant History.
    expect(MEMO_SESSION_NOT_BOUND_MESSAGE.toLowerCase()).toContain('save the document');
    expect(MEMO_SESSION_NOT_BOUND_MESSAGE).not.toContain('Promote to Analysis');
    expect(MEMO_SESSION_NOT_BOUND_MESSAGE).not.toContain('History');
    // ...and reassure the review is preserved.
    expect(MEMO_SESSION_NOT_BOUND_MESSAGE.toLowerCase()).toContain('preserved');
  });

  it('the not-bound and no-memo messages are DISTINCT (the two conditions are no longer conflated)', () => {
    expect(MEMO_SESSION_NOT_BOUND_MESSAGE).not.toBe(MEMO_NO_MEMO_MESSAGE);
  });

  it('an unknown 400 (no recognized code) → null (falls through to generic error, never masked)', () => {
    expect(selectMemoNegativeMessage(400, null)).toBeNull();
    expect(selectMemoNegativeMessage(400, 'something-nobody-declared')).toBeNull();
  });

  // R8 §GAPS-5 Phase 3 — `no-completed-review` MOVED out of the assertion above. It was grouped with
  // "unknown codes" because nothing handled it, but it was never unknown: `MemoProblemCode` has
  // declared it since task 051. It was unreachable only because no caller POSTed. Now that Phase 3
  // adds the write call, the server's guided 400 must map to a guided message — leaving it null would
  // degrade it to the generic "Failed (400)" this function exists to prevent. The assertion above
  // keeps its real intent (a genuinely unrecognized code is never masked).
  it('a 400 / no-completed-review → the "run a review first" guidance, not a generic failure', () => {
    expect(selectMemoNegativeMessage(400, 'no-completed-review')).toBe(MEMO_NO_FINDINGS_MESSAGE);
  });

  it('a 500/transport error → null (never surfaced as a guided negative state)', () => {
    expect(selectMemoNegativeMessage(500, null)).toBeNull();
    expect(selectMemoNegativeMessage(503, undefined)).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// buildGenerateReviewSummaryRequest — the WRITE payload (R8 §GAPS-5 Phase 3)
// ---------------------------------------------------------------------------

describe('buildGenerateReviewSummaryRequest', () => {
  const grounded = {
    sectionRef: '4.2',
    quotedText: 'Either party may assign this agreement without consent.',
    riskLevel: 'High',
    standardRef: 'B5 — Assignment',
    flaggedClause: 'The agreement permits assignment without the counterparty consenting.',
    assessment: 'This deviates from the firm standard, which requires prior written consent.',
  };

  it('carries every grounding field through unchanged', () => {
    const { request, droppedCount } = buildGenerateReviewSummaryRequest([grounded], 'High');
    expect(droppedCount).toBe(0);
    expect(request.overallRisk).toBe('High');
    expect(request.sections).toEqual([
      {
        sectionRef: '4.2',
        quotedText: grounded.quotedText,
        assessment: grounded.assessment,
        standardRef: grounded.standardRef,
        flaggedClause: grounded.flaggedClause,
        riskLevel: 'High',
      },
    ]);
  });

  // Decision D3: a partially-grounded finding is SENT with its gaps, never excluded. The server
  // relaxed the four fields from `required` and renders an em dash for each absent one. Excluding it
  // would silently shrink the summary relative to what the panel shows.
  it('sends a partially-grounded finding rather than dropping it', () => {
    const { request, droppedCount } = buildGenerateReviewSummaryRequest(
      [{ quotedText: 'A clause with no other grounding.' }],
      'Medium'
    );
    expect(droppedCount).toBe(0);
    expect(request.sections).toHaveLength(1);
    expect(request.sections[0]).toEqual({
      sectionRef: undefined,
      quotedText: 'A clause with no other grounding.',
      assessment: undefined,
      standardRef: undefined,
      flaggedClause: undefined,
      riskLevel: undefined,
    });
  });

  // Decision D2: per-finding accept/reject is not tracked durably anywhere, and the server assembler
  // already treats an absent afterText as "the original text stands" — correct for BOTH a rejected
  // suggestion and an untouched clause. Sending a guessed value would be worse than omitting it.
  it('never invents afterText', () => {
    const { request } = buildGenerateReviewSummaryRequest([grounded], 'High');
    expect(request.sections[0]).not.toHaveProperty('afterText', expect.anything());
    expect(request.sections[0].afterText).toBeUndefined();
  });

  it('drops a finding with no quoted text and REPORTS the count rather than swallowing it', () => {
    const { request, droppedCount } = buildGenerateReviewSummaryRequest(
      [grounded, { quotedText: '   ' }, { quotedText: '' }],
      'High'
    );
    expect(request.sections).toHaveLength(1);
    expect(droppedCount).toBe(2);
  });

  it('prefers the server-asserted overall risk, then the derived band, then names the absence', () => {
    expect(buildGenerateReviewSummaryRequest([grounded], 'Critical', 'High').request.overallRisk).toBe('Critical');
    expect(buildGenerateReviewSummaryRequest([grounded], undefined, 'High').request.overallRisk).toBe('High');
    expect(buildGenerateReviewSummaryRequest([grounded], '  ', undefined).request.overallRisk).toBe(
      UNSPECIFIED_OVERALL_RISK
    );
  });

  it('yields zero sections for an empty finding list — the caller refuses on that, never posts an empty summary', () => {
    const { request } = buildGenerateReviewSummaryRequest([], 'High');
    expect(request.sections).toEqual([]);
  });
});
