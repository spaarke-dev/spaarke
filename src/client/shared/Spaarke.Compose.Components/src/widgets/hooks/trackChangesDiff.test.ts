/**
 * trackChangesDiff.test.ts — item 4 (UAT round-4) live Track Changes word-diff engine.
 *
 * Verifies the pure diff (baseline → current) and its reduction to positioned decoration regions. The
 * decoration layer (TrackChangesExtension) maps these to inline (insertion) + widget (deletion)
 * decorations, so getting the ops + offsets right here is what makes the live redline correct.
 */
import { diffTokens, diffToRegions } from './trackChangesDiff';

describe('diffTokens (word-level baseline → current)', () => {
  it('reports no change for identical text', () => {
    expect(diffTokens('the quick brown fox', 'the quick brown fox')).toEqual([
      { type: 'equal', text: 'the quick brown fox' },
    ]);
  });

  it('detects a pure insertion', () => {
    const ops = diffTokens('the brown fox', 'the quick brown fox');
    // equal "the ", insert "quick ", equal "brown fox"
    expect(ops.filter(o => o.type === 'insert').map(o => o.text.trim())).toEqual(['quick']);
    expect(ops.some(o => o.type === 'delete')).toBe(false);
    // Concatenating equal+insert reproduces the current text.
    expect(
      ops
        .filter(o => o.type !== 'delete')
        .map(o => o.text)
        .join('')
    ).toBe('the quick brown fox');
  });

  it('detects a pure deletion', () => {
    const ops = diffTokens('the quick brown fox', 'the brown fox');
    expect(ops.filter(o => o.type === 'delete').map(o => o.text.trim())).toEqual(['quick']);
    expect(ops.some(o => o.type === 'insert')).toBe(false);
    // Concatenating equal+delete reproduces the baseline text.
    expect(
      ops
        .filter(o => o.type !== 'insert')
        .map(o => o.text)
        .join('')
    ).toBe('the quick brown fox');
  });

  it('detects a word replacement as a delete + insert', () => {
    const ops = diffTokens('the quick brown fox', 'the swift brown fox');
    expect(ops.some(o => o.type === 'delete' && o.text.includes('quick'))).toBe(true);
    expect(ops.some(o => o.type === 'insert' && o.text.includes('swift'))).toBe(true);
    expect(
      ops
        .filter(o => o.type !== 'delete')
        .map(o => o.text)
        .join('')
    ).toBe('the swift brown fox');
    expect(
      ops
        .filter(o => o.type !== 'insert')
        .map(o => o.text)
        .join('')
    ).toBe('the quick brown fox');
  });

  it('handles text appended to the end', () => {
    const ops = diffTokens('hello', 'hello world');
    expect(
      ops
        .filter(o => o.type !== 'delete')
        .map(o => o.text)
        .join('')
    ).toBe('hello world');
    expect(ops.some(o => o.type === 'insert' && o.text.includes('world'))).toBe(true);
  });
});

describe('diffToRegions (positioned decoration regions over CURRENT text)', () => {
  it('positions a pure insertion at the right current-text offset', () => {
    const regions = diffToRegions(diffTokens('the brown fox', 'the quick brown fox'));
    // "the " = 4 chars, then the inserted "quick " span.
    expect(regions).toHaveLength(1);
    expect(regions[0]).toMatchObject({ offset: 4, deleteText: '' });
    expect(regions[0].insertLength).toBe('quick '.length);
  });

  it('positions a deletion as a zero-length insert region carrying the removed text', () => {
    const regions = diffToRegions(diffTokens('the quick brown fox', 'the brown fox'));
    expect(regions).toHaveLength(1);
    // The removed "quick " attaches at the offset where it used to begin (after "the ").
    expect(regions[0]).toMatchObject({ offset: 4, insertLength: 0 });
    expect(regions[0].deleteText.trim()).toBe('quick');
  });

  it('merges a replacement into a single region (delete + insert at one offset)', () => {
    const regions = diffToRegions(diffTokens('the quick fox', 'the swift fox'));
    expect(regions).toHaveLength(1);
    expect(regions[0].offset).toBe(4);
    expect(regions[0].insertLength).toBeGreaterThan(0);
    expect(regions[0].deleteText.trim()).toBe('quick');
  });

  it('returns no regions when nothing changed', () => {
    expect(diffToRegions(diffTokens('unchanged text', 'unchanged text'))).toEqual([]);
  });

  it('an insertion region maps to a substring of the current text at its offset', () => {
    const current = 'the quick brown fox';
    const regions = diffToRegions(diffTokens('the brown fox', current));
    const r = regions[0];
    expect(current.slice(r.offset, r.offset + r.insertLength)).toBe('quick ');
  });
});
