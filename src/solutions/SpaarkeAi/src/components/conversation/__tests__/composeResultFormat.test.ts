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
import { createConsumerDispatcher } from '@spaarke/ui-components';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';

// jsdom does not polyfill TextEncoder/TextDecoder — Node's are needed by readSseStream.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (globalThis as any).TextEncoder === 'undefined') (globalThis as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (globalThis as any).TextDecoder === 'undefined') (globalThis as any).TextDecoder = NodeTextDecoder;

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

// ---------------------------------------------------------------------------
// FULL-LOOP end-to-end (spaarkeai-compose-r2 UAT wire-loss fix): drive the EXACT
// server SSE wire chunk (as emitted by the BFF DeserializeResultChunk CompletedRaw
// path — compose fields carried verbatim at `result`, NOT coerced to an empty
// DocumentAnalysisResult) through the REAL @spaarke/ui-components dispatchConsumer
// SSE parse, then through the REAL composeResultFormat formatter. This closes the
// gap ConversationPane.compose-action-format.test.tsx left: THAT test mocks the
// dispatcher's return value with a hand-authored correct object, so it stayed green
// while the wire actually delivered an empty DAR blob. THIS test proves the formatter
// is no longer starved — the object it shape-detects on is the one the wire delivers.
// ---------------------------------------------------------------------------

describe('compose wire → dispatchConsumer parse → formatter (full loop)', () => {
  const originalFetch = (globalThis as { fetch?: unknown }).fetch;
  afterEach(() => {
    (globalThis as { fetch?: unknown }).fetch = originalFetch;
  });

  /** A Response whose body streams the given SSE `data:` frames (mirrors dispatchConsumer.test.ts). */
  function sseResponse(chunks: string[]): Response {
    const wire = chunks.map((c) => `data: ${c}\n\n`).join('');
    const payload = new NodeTextEncoder().encode(wire);
    let pulled = false;
    const reader = {
      async read(): Promise<{ done: boolean; value?: Uint8Array }> {
        if (pulled) return { done: true, value: undefined };
        pulled = true;
        return { done: false, value: payload };
      },
      releaseLock() {
        /* noop */
      },
    };
    return {
      ok: true,
      status: 200,
      json: async () => ({}),
      text: async () => wire,
      body: { getReader: () => reader },
      headers: new Headers(),
    } as unknown as Response;
  }

  function dispatcherOverWire(chunks: string[]) {
    const fetchMock = jest.fn(async () => sseResponse(chunks));
    (globalThis as { fetch?: unknown }).fetch = fetchMock;
    return createConsumerDispatcher({
      bffBaseUrl: 'https://bff.test',
      getSessionId: () => '11111111-2222-3333-4444-555555555555',
      getAccessToken: async () => 'test-token',
      publishPaneEvent: () => {
        /* noop — pane bridging is covered by dispatchConsumer.test.ts */
      },
      sectionRevealDelayMs: 0,
    });
  }

  it('an explain-clause complete chunk survives the wire and renders PROSE (not an empty-DAR blob)', async () => {
    // THE EXACT wire the BFF now emits for a compose dispatch: AnalysisChunk.CompletedRaw
    // serialized camelCase — `result` carries the raw compose payload verbatim, `summary` is null.
    const wireChunk = JSON.stringify({
      type: 'complete',
      content: '',
      done: true,
      summary: null,
      result: {
        explanation: 'This clause caps aggregate liability at the fees paid in the prior year.',
        keyConcepts: ['liability cap', 'indemnification'],
        relatedPlaybookIds: [],
      },
      error: null,
    });

    const dispatch = dispatcherOverWire([wireChunk]);
    const dispatched = await dispatch('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');

    // The compose fields survived the wire → the formatter is NOT starved.
    const result = dispatched.result as Record<string, unknown>;
    expect(result.explanation).toBe(
      'This clause caps aggregate liability at the fees paid in the prior year.'
    );
    expect(result.keyConcepts).toEqual(['liability cap', 'indemnification']);

    // And the formatter (composeResultFormat) renders grounded PROSE from the wire object.
    const md = formatComposeActionResultMarkdown(dispatched.result);
    expect(md).not.toBeNull();
    expect(md).toContain('**Explanation:** This clause caps aggregate liability');
    expect(md).toContain('**Key concepts:** liability cap, indemnification');
    expect(md).not.toContain('```json');
  });

  it('REGRESSION GUARD: the pre-fix empty-DAR wire blob would starve the formatter (returns null)', () => {
    // What the wire delivered BEFORE the fix: every compose payload coerced to an empty
    // DocumentAnalysisResult (unknown props dropped by System.Text.Json). Proves the
    // formatter genuinely depends on the compose fields surviving — the fix is load-bearing.
    const preFixEmptyDarBlob = {
      summary: '',
      tldr: [],
      keywords: '',
      entities: { organizations: [], persons: [], amounts: [], dates: [], references: [] },
      rawResponse: null,
      parsedSuccessfully: true,
      emailMetadata: null,
    };
    expect(formatComposeActionResultMarkdown(preFixEmptyDarBlob)).toBeNull();
  });
});
