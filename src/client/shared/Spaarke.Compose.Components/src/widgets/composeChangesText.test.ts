/**
 * composeChangesText.test.ts — the `changesText` producer (spaarkeai-compose-r8, UAT item 8).
 *
 * The load-bearing assertions here are the REFUSALS. `compose-summarize-word-changes` was pulled from the
 * selection toolbar because dispatching it without real change data makes the LLM fabricate a phantom
 * "[Insertion]" — so "returns null when there is nothing to summarize" is the behaviour that keeps the
 * action safe to re-trigger, and the formatting assertions only matter given it holds.
 */

import type { ImportedComment, ImportedRevision } from '../types/compose-contracts';
import { COMPOSE_CHANGES_TEXT_CAP, buildComposeChangesText, hasComposeChangeData } from './composeChangesText';

function revision(overrides: Partial<ImportedRevision> = {}): ImportedRevision {
  return {
    kind: 'insertion',
    id: 'r1',
    author: 'A. Reviewer',
    date: '2026-09-01T10:00:00Z',
    text: 'and its affiliates',
    anchorText: 'The Company shall indemnify the Client.',
    paragraphHint: 11,
    ...overrides,
  };
}

function comment(overrides: Partial<ImportedComment> = {}): ImportedComment {
  return {
    id: 'c1',
    author: 'B. Counsel',
    date: '2026-09-02T09:30:00Z',
    commentText: 'Should this be mutual?',
    anchorText: 'The Company shall indemnify the Client.',
    paragraphHint: 11,
    ...overrides,
  };
}

describe('buildComposeChangesText — refusal (the binding rule)', () => {
  it('returns null when there are no revisions and no comments', () => {
    expect(buildComposeChangesText({})).toBeNull();
    expect(buildComposeChangesText({ revisions: [], comments: [] })).toBeNull();
  });

  it('returns null when every supplied annotation has an empty body — a set of blanks is not change data', () => {
    const result = buildComposeChangesText({
      revisions: [revision({ text: '' }), revision({ id: 'r2', text: '   ' })],
      comments: [comment({ commentText: '' })],
    });

    expect(result).toBeNull();
  });

  it('still produces an operand when a change has a body but no context', () => {
    // The inverse of the case above — an empty ANCHOR is not an empty change. Asserted so the
    // empty-body filter cannot be widened into "drop anything with a blank field" without a red test.
    const result = buildComposeChangesText({ revisions: [revision({ anchorText: '' })] });

    expect(result).not.toBeNull();
    expect(result).toContain('and its affiliates');
    expect(result).not.toContain('In context');
  });

  it('hasComposeChangeData agrees with buildComposeChangesText on both answers', () => {
    // One implementation, so a trigger can never be enabled for a dispatch that then refuses.
    expect(hasComposeChangeData({})).toBe(false);
    expect(hasComposeChangeData({ revisions: [revision({ text: '' })] })).toBe(false);
    expect(hasComposeChangeData({ revisions: [revision()] })).toBe(true);
  });
});

describe('buildComposeChangesText — content', () => {
  it('renders kind, location, attribution, body and context for a revision', () => {
    const result = buildComposeChangesText({ revisions: [revision()] })!;

    expect(result).toContain('1 change by 1 reviewer');
    expect(result).toContain('INSERTION in paragraph 12'); // paragraphHint is 0-based; humans count from 1
    expect(result).toContain('A. Reviewer on 2026-09-01');
    expect(result).toContain('Inserted: "and its affiliates"');
    expect(result).toContain('In context: "The Company shall indemnify the Client."');
  });

  it('renders a deletion with the deletion verb rather than the insertion one', () => {
    const result = buildComposeChangesText({
      revisions: [revision({ kind: 'deletion', text: 'sole and exclusive' })],
    })!;

    expect(result).toContain('DELETION');
    expect(result).toContain('Deleted: "sole and exclusive"');
    expect(result).not.toContain('Inserted:');
  });

  it('includes comments alongside revisions and counts distinct reviewers', () => {
    const result = buildComposeChangesText({ revisions: [revision()], comments: [comment()] })!;

    expect(result).toContain('2 changes by 2 reviewers');
    expect(result).toContain('Commented: "Should this be mutual?"');
    expect(result).toContain('B. Counsel');
  });

  it('omits the context line when it merely repeats the body', () => {
    const result = buildComposeChangesText({
      comments: [comment({ commentText: 'Mutual?', anchorText: 'Mutual?' })],
    })!;

    expect(result).toContain('Commented: "Mutual?"');
    expect(result).not.toContain('In context');
  });

  it('never emits a structural change — no recovered annotation carries that kind', () => {
    // The Action's prompt names four kinds; only three have data behind them. Inventing the fourth is
    // the same failure the refusal rule exists to prevent, so its absence is pinned.
    const result = buildComposeChangesText({ revisions: [revision()], comments: [comment()] })!;

    expect(result).not.toContain('STRUCTURAL');
  });
});

describe('buildComposeChangesText — ordering', () => {
  it('orders by document position regardless of arrival order', () => {
    const result = buildComposeChangesText({
      revisions: [
        revision({ id: 'late', text: 'LATE', paragraphHint: 40 }),
        revision({ id: 'early', text: 'EARLY', paragraphHint: 2 }),
      ],
    })!;

    expect(result.indexOf('EARLY')).toBeLessThan(result.indexOf('LATE'));
  });

  it('sorts unlocated changes LAST rather than first', () => {
    // paragraphHint -1 means "we could not locate this". Sorting on the raw value would float it above
    // every located change and assert a document position we do not know.
    const result = buildComposeChangesText({
      revisions: [
        revision({ id: 'unlocated', text: 'UNLOCATED', paragraphHint: -1 }),
        revision({ id: 'located', text: 'LOCATED', paragraphHint: 5 }),
      ],
    })!;

    expect(result.indexOf('LOCATED')).toBeLessThan(result.indexOf('UNLOCATED'));
    expect(result).toContain('an unlocated paragraph');
  });

  it('is deterministic — the same set in a different order produces the identical operand', () => {
    // Reproducibility is what makes the summary stable across runs and the eval case meaningful.
    const a = revision({ id: 'a', text: 'alpha', paragraphHint: 3 });
    const b = revision({ id: 'b', text: 'bravo', paragraphHint: 3 });
    const c = comment({ id: 'c', commentText: 'charlie', paragraphHint: 3 });

    const forward = buildComposeChangesText({ revisions: [a, b], comments: [c] });
    const reversed = buildComposeChangesText({ revisions: [b, a], comments: [c] });

    expect(forward).toBe(reversed);
    // Same paragraph ⇒ kind decides: insertions, then deletions, then comments.
    expect(forward!.indexOf('alpha')).toBeLessThan(forward!.indexOf('charlie'));
  });
});

describe('buildComposeChangesText — the authored cap', () => {
  it('stays within the Action-declared maxLength and says so when it drops changes', () => {
    const many = Array.from({ length: 4000 }, (_, i) =>
      revision({
        id: `r${i}`,
        paragraphHint: i,
        text: `inserted clause ${i} `.repeat(20),
        anchorText: `context for clause ${i} `.repeat(20),
      })
    );

    const result = buildComposeChangesText({ revisions: many })!;

    expect(result.length).toBeLessThanOrEqual(COMPOSE_CHANGES_TEXT_CAP);
    expect(result).toContain('[Change list truncated');
    expect(result).toContain('inserted clause 0'); // it kept the earliest changes, not a random window
  });

  it('refuses rather than emitting a header that promises changes it could not fit', () => {
    // A single change larger than the whole budget leaves room for no entries. Emitting just the count
    // preamble would be an operand that describes changes it does not contain.
    const huge = revision({ text: 'x'.repeat(COMPOSE_CHANGES_TEXT_CAP + 1000) });

    expect(buildComposeChangesText({ revisions: [huge] })).toBeNull();
  });
});
