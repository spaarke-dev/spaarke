/**
 * Issue #853 — the copy shown when a suggested edit cannot be placed.
 *
 * WHY THESE TESTS EXIST. During UAT on 2026-08-26 a user selected a clause, asked the assistant to
 * make it more concise, and was told: "This suggestion came from an earlier session, before
 * suggestions carried a paragraph reference." Every word of that was true of the PAYLOAD and false
 * about the USER — they had selected the text a second earlier.
 *
 * The wrong sentence shipped for months and nothing could catch it, because both strings were
 * unreachable except by rendering a heavy component. That is the actual defect these tests close:
 * the copy is now a pure function of the failure, so a false claim about the user's own action is a
 * failing assertion rather than a UAT anecdote.
 *
 * The load-bearing assertions here are the NEGATIVE ones — "does not say 'earlier session'". A test
 * that only pins exact strings would pass just as happily with all three causes collapsed back into
 * one, which is precisely the bug.
 */

import { describeAnchorlessProposal, describeRedlineError } from './redlineFailureCopy';
import type { PendingRedlineError } from './hooks/usePendingRedline';

function err(over: Partial<PendingRedlineError> = {}): PendingRedlineError {
  return {
    ledgerRef: 'b1@t1',
    kind: 'not_found',
    source: 'anchored',
    targetText: 'thirty days notice',
    matchCount: 0,
    ...over,
  };
}

describe('describeRedlineError — the three causes stay three', () => {
  it('a LIVE anchorless failure never blames an earlier session', () => {
    const copy = describeRedlineError(err({ source: 'live-anchorless' }));

    expect(copy).not.toMatch(/earlier session/i);
    expect(copy).not.toMatch(/re-run/i); // re-running is not the remedy — they just did
    expect(copy).toMatch(/without saying which paragraph/i);
  });

  it('a genuine replay still says what it always said', () => {
    // The replay population is real (pre-052 ledger entries, 90-day TTL) and its copy was correct.
    // #853 must not "fix" it into vagueness.
    const copy = describeRedlineError(err({ source: 'legacy-replay' }));

    expect(copy).toMatch(/earlier session/i);
    expect(copy).toMatch(/re-run/i);
  });

  it('an ANCHORED failure blames neither prose nor history — no text was compared', () => {
    // FR-C07: this is the branch that used to fabricate "its wording differs slightly".
    const copy = describeRedlineError(err({ source: 'anchored' }));

    expect(copy).not.toMatch(/earlier session/i);
    expect(copy).not.toMatch(/wording/i);
    expect(copy).toMatch(/named a paragraph or section this document doesn't have/i);
  });

  it('all three causes produce DIFFERENT sentences', () => {
    // The regression is collapse. If a future refactor folds two branches together this fails,
    // even if every individual string still reads plausibly.
    const sentences = (['anchored', 'legacy-replay', 'live-anchorless'] as const).map(source =>
      describeRedlineError(err({ source }))
    );

    expect(new Set(sentences).size).toBe(3);
  });

  describe('ambiguous — the same split applies when prose matched too many places', () => {
    it('live-anchorless names the assistant, not the past', () => {
      const copy = describeRedlineError(err({ kind: 'ambiguous', source: 'live-anchorless', matchCount: 3 }));

      expect(copy).not.toMatch(/earlier session/i);
      expect(copy).toContain('3 places');
      expect(copy).toMatch(/didn't say which paragraph/i);
    });

    it('legacy-replay keeps its history explanation', () => {
      const copy = describeRedlineError(err({ kind: 'ambiguous', source: 'legacy-replay', matchCount: 3 }));

      expect(copy).toMatch(/earlier session/i);
      expect(copy).toContain('3 places');
    });

    it('all three ambiguous sentences stay distinct', () => {
      const sentences = (['anchored', 'legacy-replay', 'live-anchorless'] as const).map(source =>
        describeRedlineError(err({ kind: 'ambiguous', source, matchCount: 3 }))
      );

      expect(new Set(sentences).size).toBe(3);
    });
  });

  describe('batched (whole-document) copy keeps the distinction', () => {
    it('live-anchorless batch does not blame an earlier session', () => {
      const copy = describeRedlineError(err({ source: 'live-anchorless', failedCount: 4, totalCount: 9 }));

      expect(copy).not.toMatch(/earlier session/i);
      expect(copy).toContain('4 of 9');
    });

    it('legacy-replay batch does', () => {
      const copy = describeRedlineError(err({ source: 'legacy-replay', failedCount: 4, totalCount: 9 }));

      expect(copy).toMatch(/earlier session/i);
      expect(copy).toContain('4 of 9');
    });
  });

  it('target_deleted is source-independent — the text is gone either way', () => {
    // Deliberate: when the target text no longer exists, WHY it had no anchor changes nothing the
    // user can act on. Collapsing here is correct; collapsing above is the bug.
    const sentences = (['anchored', 'legacy-replay', 'live-anchorless'] as const).map(source =>
      describeRedlineError(err({ kind: 'target_deleted', source }))
    );

    expect(new Set(sentences).size).toBe(1);
  });
});

describe('describeAnchorlessProposal — the confirmation dialog', () => {
  it('a LIVE anchorless proposal never blames an earlier session', () => {
    const copy = describeAnchorlessProposal({
      source: 'live-anchorless',
      proposedCount: 1,
      totalCount: 1,
    });

    expect(copy).not.toMatch(/earlier session/i);
    expect(copy).toMatch(/without saying which paragraph/i);
    // The uncertainty must survive: we located prose, we did NOT verify it is the right clause.
    expect(copy).toMatch(/cannot confirm/i);
  });

  it('a replay proposal keeps its history explanation', () => {
    const copy = describeAnchorlessProposal({ source: 'legacy-replay', proposedCount: 1, totalCount: 1 });

    expect(copy).toMatch(/earlier session/i);
    expect(copy).toMatch(/cannot confirm/i);
  });

  it('the two sources produce different sentences, batched and single', () => {
    const single = (['legacy-replay', 'live-anchorless'] as const).map(source =>
      describeAnchorlessProposal({ source, proposedCount: 1, totalCount: 1 })
    );
    const batched = (['legacy-replay', 'live-anchorless'] as const).map(source =>
      describeAnchorlessProposal({ source, proposedCount: 3, totalCount: 7 })
    );

    expect(new Set(single).size).toBe(2);
    expect(new Set(batched).size).toBe(2);
    batched.forEach(s => expect(s).toContain('3 of 7'));
  });
});
