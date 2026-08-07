/**
 * readerReferenceMap.test.ts — quoted-text citation anchor (task 054, NFR-11).
 *
 * Covers the closed acceptance set at the pure-logic layer: resolve a body-sourced
 * citation, resolve an attachment-sourced citation, source-scoping (same words in
 * two segments resolve to the named one), whitespace/markup-normalized matching,
 * and the NEGATIVE cases — a forged/absent quote and a quote-less proposal resolve
 * an explicit NOT-LOCATED (never a nearest/wrong-passage guess). Path A: the
 * legal-number `composeCitationResolver` is NOT the mechanism here (it cannot
 * express an email QuotedText anchor) — see the module doc comment.
 */
import {
  buildReaderReferenceMap,
  resolveQuotedCitation,
  normalizeForAnchor,
  type ReaderReferenceMapInput,
} from '../readerReferenceMap';

const INPUT: ReaderReferenceMapInput = {
  body: '<p>Please review the <b>quarterly filing</b> before the September 30 deadline.</p>',
  attachments: [
    {
      attachmentId: 'att-1',
      name: 'Q3-filing.pdf',
      text: 'SECTION 1. The registrant hereby files its quarterly report for the period ended September 30.',
    },
    {
      attachmentId: 'att-2',
      name: 'cover.pdf',
      text: 'Enclosed please find the quarterly filing referenced above.',
    },
    // Unextractable attachment — no text ⇒ contributes NO segment.
    { attachmentId: 'att-img', name: 'exhibit.png', text: '' },
  ],
};

describe('buildReaderReferenceMap', () => {
  it('emits a body segment + one segment per extractable attachment; skips empty ones', () => {
    const map = buildReaderReferenceMap(INPUT);
    expect(map.segments.map(s => s.segmentId)).toEqual(['body', 'att-1', 'att-2']);
    // Body segment is normalized (markup stripped, whitespace collapsed).
    const body = map.segments.find(s => s.segmentId === 'body')!;
    expect(body.kind).toBe('body');
    expect(body.text).toBe('Please review the quarterly filing before the September 30 deadline.');
    // Unextractable attachment (no text) is omitted entirely.
    expect(map.segments.find(s => s.segmentId === 'att-img')).toBeUndefined();
  });

  it('emits no body segment when the body normalizes to empty', () => {
    const map = buildReaderReferenceMap({ body: '<p>   </p>', attachments: [] });
    expect(map.segments).toHaveLength(0);
  });
});

describe('resolveQuotedCitation', () => {
  const map = buildReaderReferenceMap(INPUT);

  it('locates a body-sourced citation at the exact normalized span', () => {
    const res = resolveQuotedCitation(
      { source: 'body', locator: 'body: sentence 1', quotedText: 'quarterly filing' },
      map
    );
    expect(res.located).toBe(true);
    if (!res.located) throw new Error('expected located');
    expect(res.segmentId).toBe('body');
    expect(res.kind).toBe('body');
    expect(res.matchedText).toBe('quarterly filing');
    expect(map.segments[0].text.slice(res.start, res.end)).toBe('quarterly filing');
  });

  it('locates an attachment-sourced citation in the folded attachment text', () => {
    const res = resolveQuotedCitation(
      { source: 'Q3-filing.pdf', locator: 'Q3-filing.pdf p.1', quotedText: 'registrant hereby files' },
      map
    );
    expect(res.located).toBe(true);
    if (!res.located) throw new Error('expected located');
    expect(res.segmentId).toBe('att-1');
    expect(res.kind).toBe('attachment');
    expect(res.label).toBe('Q3-filing.pdf');
  });

  it('scopes by source — the same phrase in two segments resolves to the NAMED segment', () => {
    // "quarterly filing" appears in the body AND in cover.pdf.
    const toBody = resolveQuotedCitation({ source: 'body', quotedText: 'quarterly filing' }, map);
    const toCover = resolveQuotedCitation({ source: 'cover.pdf', quotedText: 'quarterly filing' }, map);
    expect(toBody.located && toBody.segmentId).toBe('body');
    expect(toCover.located && toCover.segmentId).toBe('att-2');
  });

  it('matches across markup/whitespace normalization (reader HTML vs plain quote)', () => {
    // The body's raw text is "<b>quarterly filing</b>"; the AI quote is plain + spaced.
    const res = resolveQuotedCitation({ source: 'body', quotedText: '  quarterly   filing ' }, map);
    expect(res.located).toBe(true);
  });

  it('falls back to all segments when the named source matches no segment', () => {
    const res = resolveQuotedCitation({ source: 'unknown-source.txt', quotedText: 'registrant hereby files' }, map);
    expect(res.located && res.segmentId).toBe('att-1');
  });

  it('NEGATIVE — a forged/absent quote resolves not-found, never a nearest guess', () => {
    const res = resolveQuotedCitation({ source: 'body', quotedText: 'this text was never in the email' }, map);
    expect(res).toEqual({ located: false, reason: 'not-found' });
  });

  it('NEGATIVE — a quote-less proposal resolves no-quoted-text', () => {
    expect(resolveQuotedCitation({ source: 'body', quotedText: '' }, map)).toEqual({
      located: false,
      reason: 'no-quoted-text',
    });
    expect(resolveQuotedCitation({ source: 'body' }, map)).toEqual({
      located: false,
      reason: 'no-quoted-text',
    });
  });

  it('NEGATIVE — a citation into an unextractable (segment-less) attachment resolves not-found', () => {
    const res = resolveQuotedCitation({ source: 'exhibit.png', quotedText: 'anything' }, map);
    expect(res).toEqual({ located: false, reason: 'not-found' });
  });
});

describe('normalizeForAnchor', () => {
  it('strips markup, collapses whitespace, trims; empty for nullish', () => {
    expect(normalizeForAnchor('<p>a   b\n\tc</p>')).toBe('a b c');
    expect(normalizeForAnchor(null)).toBe('');
    expect(normalizeForAnchor(undefined)).toBe('');
  });
});
