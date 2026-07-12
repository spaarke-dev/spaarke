/**
 * composeReviseRouting.test.ts — DEF-11 whole-document revise disambiguation routing.
 *
 * Pure-function coverage only (no React, no IO) — mirrors the `routeSummarizeIntent` test style.
 */
import {
  routeReviseIntent,
  REVISE_SLASH_PREFIX,
  REVISE_DISAMBIGUATION_MESSAGE,
  REVISION_INTENTS,
  REVISION_INTENT_SUGGESTIONS,
} from '../composeReviseRouting';

describe('routeReviseIntent', () => {
  it('not-revise: a plain message passes through unchanged', () => {
    const decision = routeReviseIntent('please summarize this document');
    expect(decision).toEqual({ kind: 'not-revise', messageText: 'please summarize this document' });
  });

  it('not-revise: a message merely containing the word "revise" without the slash prefix passes through', () => {
    const decision = routeReviseIntent('can you revise this document for me?');
    expect(decision.kind).toBe('not-revise');
  });

  it('disambiguate: bare "/revise" with no intent or instruction', () => {
    const decision = routeReviseIntent('/revise');
    expect(decision).toEqual({
      kind: 'disambiguate',
      messageText: '/revise',
      interjection: REVISE_DISAMBIGUATION_MESSAGE,
    });
  });

  it('disambiguate: "/revise" with only trailing whitespace is still bare', () => {
    const decision = routeReviseIntent('/revise   ');
    expect(decision.kind).toBe('disambiguate');
  });

  it('is case-insensitive on the slash prefix', () => {
    const decision = routeReviseIntent('/Revise');
    expect(decision.kind).toBe('disambiguate');
    const decision2 = routeReviseIntent('/REVISE align-clauses');
    expect(decision2).toMatchObject({ kind: 'intent-specified', revisionIntent: 'align-clauses' });
  });

  it.each(['align-clauses', 'flag-risks', 'improve-clarity'] as const)(
    'intent-specified: "/revise %s" with no further instruction',
    intent => {
      const decision = routeReviseIntent(`/revise ${intent}`);
      expect(decision).toEqual({
        kind: 'intent-specified',
        messageText: `/revise ${intent}`,
        revisionIntent: intent,
        instruction: undefined,
      });
    }
  );

  it('intent-specified: a named intent carries a trailing instruction verbatim', () => {
    const decision = routeReviseIntent('/revise improve-clarity make section 4 easier to read');
    expect(decision).toEqual({
      kind: 'intent-specified',
      messageText: '/revise improve-clarity make section 4 easier to read',
      revisionIntent: 'improve-clarity',
      instruction: 'make section 4 easier to read',
    });
  });

  it('intent-specified (custom): free text after /revise that is not a named intent routes as custom', () => {
    const decision = routeReviseIntent('/revise change all references to "Buyer" to "Purchaser"');
    expect(decision).toEqual({
      kind: 'intent-specified',
      messageText: '/revise change all references to "Buyer" to "Purchaser"',
      revisionIntent: 'custom',
      instruction: 'change all references to "Buyer" to "Purchaser"',
    });
  });

  it('never returns disambiguate once ANY text follows /revise (only the bare form is ambiguous)', () => {
    const decision = routeReviseIntent('/revise x');
    expect(decision.kind).toBe('intent-specified');
  });

  it('is deterministic and total: same input always yields the same decision', () => {
    const inputs = ['/revise', '/revise flag-risks', 'not a revise message', '/revise custom text here'];
    for (const input of inputs) {
      expect(routeReviseIntent(input)).toEqual(routeReviseIntent(input));
    }
  });
});

describe('REVISION_INTENTS / REVISION_INTENT_SUGGESTIONS (contract-closed enum)', () => {
  it('carries exactly the four DEF-11 contract intents, in presentation order', () => {
    expect(REVISION_INTENTS).toEqual(['align-clauses', 'flag-risks', 'improve-clarity', 'custom']);
  });

  it('one suggestion per intent, same order, each with a non-empty label', () => {
    expect(REVISION_INTENT_SUGGESTIONS.map(s => s.revisionIntent)).toEqual(REVISION_INTENTS);
    for (const s of REVISION_INTENT_SUGGESTIONS) {
      expect(s.label.length).toBeGreaterThan(0);
    }
  });
});

describe('REVISE_SLASH_PREFIX', () => {
  it('is the literal "/revise"', () => {
    expect(REVISE_SLASH_PREFIX).toBe('/revise');
  });
});
