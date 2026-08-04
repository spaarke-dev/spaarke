/**
 * useNdaReviewAdvisoryCommentsBridge.test.ts — NDA-REVIEW advisory comments
 * (ai-advanced-capabilities-nda-r1 task 031).
 *
 * Proves the shape-detection + projection boundary:
 *   - a review result ({ overallRisk, flaggedSections[] }) dispatches ONE
 *     `compose_advisory_comments` event on the `workspace` channel, with one
 *     projected item per USABLE flagged section (quotedText + at least one of
 *     explanation [pre-split] / flaggedClause / assessment [agreements-r1
 *     task-002 split] present, non-empty);
 *   - a flagged section missing `quotedText` or `explanation` is skipped (never
 *     thrown), and if EVERY section is unusable, nothing is dispatched;
 *   - a result that structurally matches an UNRELATED Compose action shape
 *     (e.g. `compose-compare-to-playbook`'s `{ overallRisk, matches[] }` — no
 *     `flaggedSections`) does NOT dispatch anything (disambiguation);
 *   - a non-object / null / undefined result does NOT dispatch anything.
 */
import { renderHook } from '@testing-library/react';
import { useNdaReviewAdvisoryCommentsBridge, isNdaReviewResult } from '../useNdaReviewAdvisoryCommentsBridge';

function setup(sessionId: string | null = 'sess-1') {
  const dispatch = jest.fn();
  const getSessionId = jest.fn(() => sessionId);
  const { result } = renderHook(() => useNdaReviewAdvisoryCommentsBridge({ dispatch, getSessionId }));
  return { dispatch, getSessionId, result };
}

const NDA_REVIEW_RESULT = {
  overallRisk: 'medium',
  flaggedSections: [
    {
      sectionRef: '3.2',
      quotedText: 'The receiving party shall retain confidential information indefinitely.',
      riskLevel: 'high',
      explanation: 'Indefinite retention deviates from the standard 3-year term.',
      standardRef: 'nda-standard-retention',
    },
    {
      sectionRef: '5.1',
      quotedText: 'Either party may assign this agreement without consent.',
      riskLevel: 'medium',
      explanation: 'Free assignment is broader than the playbook default (consent required).',
    },
  ],
};

describe('useNdaReviewAdvisoryCommentsBridge — shape detection + projection (task 031)', () => {
  it('NDA-REVIEW result: dispatches ONE compose_advisory_comments event with one item per flagged section', () => {
    const { dispatch, result } = setup();

    result.current.emitFromResult(NDA_REVIEW_RESULT);

    expect(dispatch).toHaveBeenCalledTimes(1);
    const [channel, event] = dispatch.mock.calls[0];
    expect(channel).toBe('workspace');
    expect(event).toMatchObject({
      type: 'compose_advisory_comments',
      sessionId: 'sess-1',
      // task 032 (right-gutter comment layout): the Action's own top-level overallRisk now rides the
      // wire verbatim (previously typed on NdaReviewResult but dropped at dispatch time).
      overallRisk: 'medium',
      advisoryComments: [
        {
          targetText: 'The receiving party shall retain confidential information indefinitely.',
          explanation: 'Indefinite retention deviates from the standard 3-year term.',
          sectionRef: '3.2',
          riskLevel: 'high',
          standardRef: 'nda-standard-retention',
        },
        {
          targetText: 'Either party may assign this agreement without consent.',
          explanation: 'Free assignment is broader than the playbook default (consent required).',
          sectionRef: '5.1',
          riskLevel: 'medium',
        },
      ],
    });
    expect(typeof event.timestamp).toBe('string');
  });

  it('POST-SPLIT shape (agreements-r1 task 002: flaggedClause/assessment, NO explanation): projects every section — composed explanation + discrete fields carried through', () => {
    const { dispatch, result } = setup();

    result.current.emitFromResult({
      overallRisk: 'high',
      flaggedSections: [
        {
          sectionRef: '4.1',
          quotedText: 'Confidentiality obligations survive for ten years after termination.',
          riskLevel: 'high',
          flaggedClause: 'The clause imposes a ten-year survival period on confidentiality.',
          assessment: 'Materially longer than the standard three-year term.',
          standardRef: 'B8',
        },
        {
          sectionRef: '7.2',
          quotedText: 'The disclosing party makes no warranty as to accuracy.',
          riskLevel: 'low',
          flaggedClause: 'The clause disclaims accuracy warranties.',
          // no assessment — explanation composes from flaggedClause alone
        },
      ],
    });

    expect(dispatch).toHaveBeenCalledTimes(1);
    const [, event] = dispatch.mock.calls[0];
    expect(event.advisoryComments).toHaveLength(2);
    expect(event.advisoryComments[0]).toMatchObject({
      targetText: 'Confidentiality obligations survive for ten years after termination.',
      explanation:
        'The clause imposes a ten-year survival period on confidentiality.\n\nMaterially longer than the standard three-year term.',
      flaggedClause: 'The clause imposes a ten-year survival period on confidentiality.',
      assessment: 'Materially longer than the standard three-year term.',
      standardRef: 'B8',
    });
    expect(event.advisoryComments[1]).toMatchObject({
      explanation: 'The clause disclaims accuracy warranties.',
      flaggedClause: 'The clause disclaims accuracy warranties.',
    });
    expect(event.advisoryComments[1].assessment).toBeUndefined();
  });

  it('skips a flagged section missing quotedText or explanation, without throwing', () => {
    const { dispatch, result } = setup();

    result.current.emitFromResult({
      overallRisk: 'low',
      flaggedSections: [
        { sectionRef: '1.1', explanation: 'missing quotedText' },
        { sectionRef: '1.2', quotedText: 'missing explanation' },
        { sectionRef: '1.3', quotedText: '', explanation: '' },
        { sectionRef: '1.4', quotedText: 'usable text', explanation: 'usable explanation' },
      ],
    });

    expect(dispatch).toHaveBeenCalledTimes(1);
    const [, event] = dispatch.mock.calls[0];
    expect(event.advisoryComments).toHaveLength(1);
    expect(event.advisoryComments[0]).toMatchObject({ targetText: 'usable text', explanation: 'usable explanation' });
  });

  it('every flagged section unusable: still dispatches (empty items) so completion observers fire', () => {
    const { dispatch, result } = setup();

    result.current.emitFromResult({
      overallRisk: 'low',
      flaggedSections: [{ sectionRef: '1.1' }, { quotedText: '', explanation: '' }],
    });

    expect(dispatch).toHaveBeenCalledTimes(1);
    const [, event] = dispatch.mock.calls[0];
    expect(event.advisoryComments).toEqual([]);
    expect(event.overallRisk).toBe('low');
  });

  it('an empty flaggedSections array (clean review): dispatches with empty items — the zero-findings completion signal (task 071)', () => {
    const { dispatch, result } = setup();

    result.current.emitFromResult({ overallRisk: 'none', flaggedSections: [] });

    expect(dispatch).toHaveBeenCalledTimes(1);
    const [, event] = dispatch.mock.calls[0];
    expect(event.advisoryComments).toEqual([]);
    expect(event.overallRisk).toBe('none');
  });

  it('a DIFFERENT Compose action shape (compose-compare-to-playbook: overallRisk + matches[]) dispatches NOTHING', () => {
    const { dispatch, result } = setup();

    result.current.emitFromResult({
      overallRisk: 'medium',
      matches: [{ playbookEntryId: 'entry-1', riskScore: 0.4, rationale: 'r' }],
    });

    expect(dispatch).not.toHaveBeenCalled();
  });

  it.each([null, undefined, 'a string', 42, ['array', 'not', 'object']])(
    'a non-matching payload (%p) dispatches NOTHING',
    (payload) => {
      const { dispatch, result } = setup();
      result.current.emitFromResult(payload);
      expect(dispatch).not.toHaveBeenCalled();
    }
  );
});

describe('isNdaReviewResult — structural shape guard', () => {
  it('true for { overallRisk: string, flaggedSections: array }', () => {
    expect(isNdaReviewResult(NDA_REVIEW_RESULT)).toBe(true);
    expect(isNdaReviewResult({ overallRisk: 'none', flaggedSections: [] })).toBe(true);
  });

  it('false when flaggedSections is missing or overallRisk is not a string', () => {
    expect(isNdaReviewResult({ overallRisk: 'medium', matches: [] })).toBe(false);
    expect(isNdaReviewResult({ overallRisk: 1, flaggedSections: [] })).toBe(false);
    expect(isNdaReviewResult({ flaggedSections: [] })).toBe(false);
  });

  it('false for non-object payloads', () => {
    expect(isNdaReviewResult(null)).toBe(false);
    expect(isNdaReviewResult(undefined)).toBe(false);
    expect(isNdaReviewResult('x')).toBe(false);
    expect(isNdaReviewResult([1, 2, 3])).toBe(false);
  });
});
