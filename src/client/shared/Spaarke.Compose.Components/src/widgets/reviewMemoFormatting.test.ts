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
  MEMO_NO_MEMO_MESSAGE,
  MEMO_SESSION_NOT_BOUND_MESSAGE,
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
    // ...and must point the user at the existing promote affordance.
    expect(MEMO_SESSION_NOT_BOUND_MESSAGE).toContain('Promote to Analysis');
    // ...and reassure the review is preserved.
    expect(MEMO_SESSION_NOT_BOUND_MESSAGE.toLowerCase()).toContain('preserved');
  });

  it('the not-bound and no-memo messages are DISTINCT (the two conditions are no longer conflated)', () => {
    expect(MEMO_SESSION_NOT_BOUND_MESSAGE).not.toBe(MEMO_NO_MEMO_MESSAGE);
  });

  it('an unknown 400 (no recognized code) → null (falls through to generic error, never masked)', () => {
    expect(selectMemoNegativeMessage(400, null)).toBeNull();
    expect(selectMemoNegativeMessage(400, 'no-completed-review')).toBeNull();
  });

  it('a 500/transport error → null (never surfaced as a guided negative state)', () => {
    expect(selectMemoNegativeMessage(500, null)).toBeNull();
    expect(selectMemoNegativeMessage(503, undefined)).toBeNull();
  });
});
