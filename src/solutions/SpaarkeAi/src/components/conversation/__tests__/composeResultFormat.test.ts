/**
 * composeResultFormat.ts — formatter-level tests (task 112).
 *
 * SUPPLEMENTARY to ConversationPane.compose-action-format.test.tsx (the
 * binding real-PaneEventBus E2E DoD, which exercises explain-clause +
 * defined-terms + the unknown-shape fallback through `dispatchComposeAction`).
 * These pure-function tests cover the remaining 3 Compose shapes
 * (compare-to-playbook, draft-alternative, summarize-word-changes) plus the
 * "must NOT misfire" negative cases — a formatter-only test does not by
 * itself satisfy the project's binding E2E DoD (see the sibling E2E test),
 * but is useful for exhaustive shape coverage the E2E test doesn't need to
 * duplicate.
 */

import {
  formatComposeActionResultMarkdown,
  formatExplainClauseResult,
  formatCompareToPlaybookResult,
  formatDraftAlternativeResult,
  formatSummarizeWordChangesResult,
  formatDefinedTermsResult,
} from '../composeResultFormat';

describe('formatExplainClauseResult', () => {
  it('renders explanation + key concepts + related playbook ids', () => {
    const md = formatExplainClauseResult({
      explanation: 'Caps liability at contract value.',
      keyConcepts: ['indemnification', 'liability cap'],
      relatedPlaybookIds: ['pb-1', 'pb-2'],
    });
    expect(md).toContain('**Explanation:** Caps liability at contract value.');
    expect(md).toContain('**Key concepts:** indemnification, liability cap');
    expect(md).toContain('**Related playbook references:** pb-1, pb-2');
  });

  it('tolerates an empty keyConcepts array (schema-allowed) without a bogus label', () => {
    const md = formatExplainClauseResult({ explanation: 'Insufficient selection.', keyConcepts: [] });
    expect(md).toContain('**Explanation:** Insufficient selection.');
    expect(md).not.toContain('**Key concepts:**');
  });

  it('returns null for a non-matching shape (missing keyConcepts array)', () => {
    expect(formatExplainClauseResult({ explanation: 'x' })).toBeNull();
    expect(formatExplainClauseResult(null)).toBeNull();
    expect(formatExplainClauseResult('a string')).toBeNull();
  });
});

describe('formatCompareToPlaybookResult', () => {
  it('renders overall risk + each match with risk score, rationale, deviations', () => {
    const md = formatCompareToPlaybookResult({
      overallRisk: 'high',
      matches: [
        {
          playbookEntryId: 'entry-1',
          clauseText: 'Uncapped indemnity.',
          deviations: ['no cap', 'broad scope'],
          riskScore: 0.8,
          rationale: 'Deviates significantly from firm standard.',
        },
      ],
    });
    expect(md).toContain('**Overall risk:** high');
    expect(md).toContain('entry-1');
    expect(md).toContain('risk 0.80');
    expect(md).toContain('Deviates significantly from firm standard.');
    expect(md).toContain('Deviations: no cap; broad scope.');
  });

  it('renders a no-matches note while still stating overall risk', () => {
    const md = formatCompareToPlaybookResult({ overallRisk: 'low', matches: [] });
    expect(md).toContain('**Overall risk:** low');
    expect(md).toContain('No relevant playbook entries were supplied for comparison.');
  });

  it('returns null when overallRisk or matches is missing', () => {
    expect(formatCompareToPlaybookResult({ matches: [] })).toBeNull();
    expect(formatCompareToPlaybookResult({ overallRisk: 'low' })).toBeNull();
  });
});

describe('formatDraftAlternativeResult', () => {
  it('renders rationale + proposed text + sources', () => {
    const md = formatDraftAlternativeResult({
      target_text: 'The Vendor shall have no liability.',
      new_text: 'The Vendor liability shall be capped at the fees paid in the preceding 12 months.',
      match_mode: 'strict',
      rationale: 'Provides a bounded, market-standard cap instead of a full carve-out.',
      sources: [{ type: 'playbook', id: 'pb-9', snippet: 'Cap liability at 12-month fees.' }],
    });
    expect(md).toContain('**Rationale:** Provides a bounded, market-standard cap instead of a full carve-out.');
    expect(md).toContain('capped at the fees paid in the preceding 12 months');
    expect(md).toContain('playbook: pb-9');
  });

  it('returns null when the required triad (target_text/new_text/match_mode) is incomplete', () => {
    expect(formatDraftAlternativeResult({ new_text: 'x', match_mode: 'strict' })).toBeNull();
  });
});

describe('formatSummarizeWordChangesResult', () => {
  it('renders the summary + each change entry', () => {
    const md = formatSummarizeWordChangesResult({
      summary: 'The reviewer tightened the indemnity language and added a notice requirement.',
      changes: [
        { kind: 'insertion', location: 'Section 7.3', description: 'Added a 30-day notice requirement.' },
        { kind: 'deletion', location: 'Section 9.1', description: 'Removed the unilateral termination right.' },
      ],
    });
    expect(md).toContain('The reviewer tightened the indemnity language');
    expect(md).toContain('**[insertion]** Section 7.3: Added a 30-day notice requirement.');
    expect(md).toContain('**[deletion]** Section 9.1: Removed the unilateral termination right.');
  });

  it('does NOT misfire on the unrelated Event-path summarize schema (tldr/summary/keywords, no changes)', () => {
    expect(
      formatSummarizeWordChangesResult({ tldr: 'T.', summary: 'S.', keywords: ['a'] })
    ).toBeNull();
  });
});

describe('formatDefinedTermsResult', () => {
  it('returns null for a non-matching shape', () => {
    expect(formatDefinedTermsResult({ terms: [] })).toBeNull();
    expect(formatDefinedTermsResult({ inconsistencies: [] })).toBeNull();
  });

  it('renders "no defined terms" when terms is empty', () => {
    const md = formatDefinedTermsResult({ terms: [], inconsistencies: [] });
    expect(md).toContain('No defined terms were found.');
  });
});

describe('formatComposeActionResultMarkdown (dispatcher)', () => {
  it('returns null for the general Event-path summarize schema (falls through to formatEventOutputMarkdown)', () => {
    expect(
      formatComposeActionResultMarkdown({ tldr: 'T.', summary: 'S.', keywords: ['a'] })
    ).toBeNull();
  });

  it('returns null for a genuinely unrecognized shape', () => {
    expect(formatComposeActionResultMarkdown({ someFutureField: 1 })).toBeNull();
  });

  it('returns null for non-object payloads (string/number/null)', () => {
    expect(formatComposeActionResultMarkdown('plain string')).toBeNull();
    expect(formatComposeActionResultMarkdown(42)).toBeNull();
    expect(formatComposeActionResultMarkdown(null)).toBeNull();
  });

  it('picks the explain-clause formatter for its shape', () => {
    const md = formatComposeActionResultMarkdown({ explanation: 'x', keyConcepts: ['y'] });
    expect(md).toContain('**Explanation:** x');
  });
});
