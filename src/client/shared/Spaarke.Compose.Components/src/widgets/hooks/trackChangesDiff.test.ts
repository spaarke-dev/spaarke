/**
 * trackChangesDiff.test.ts — item 4 (UAT round-4) live Track Changes word-diff engine.
 *
 * Verifies the pure diff (baseline → current) and its reduction to positioned decoration regions. The
 * decoration layer (TrackChangesExtension) maps these to inline (insertion) + widget (deletion)
 * decorations, so getting the ops + offsets right here is what makes the live redline correct.
 */
import { diffTokens, diffToRegions, cleanupSemantic } from './trackChangesDiff';

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

describe('cleanupSemantic (item 4 diff-quality — no salt-and-pepper fragmentation)', () => {
  // Helper: baseline reconstruction = equal+delete; current reconstruction = equal+insert.
  const rebuildBaseline = (ops: { type: string; text: string }[]) =>
    ops
      .filter(o => o.type !== 'insert')
      .map(o => o.text)
      .join('');
  const rebuildCurrent = (ops: { type: string; text: string }[]) =>
    ops
      .filter(o => o.type !== 'delete')
      .map(o => o.text)
      .join('');

  it('collapses a rewrite with a COINCIDENTAL short common word into ONE strike + ONE insert', () => {
    // "item" recurs, so a naive LCS keeps it as a mid-phrase equality → fragmentation. Cleanup absorbs it.
    const baseline = 'alpha item beta item gamma';
    const current = 'alpha changed item swapped gamma';
    const ops = diffTokens(baseline, current);

    // The changed region between the "alpha " and " gamma" anchors is a SINGLE delete + SINGLE insert.
    const deletes = ops.filter(o => o.type === 'delete');
    const inserts = ops.filter(o => o.type === 'insert');
    expect(deletes.length).toBeLessThanOrEqual(1);
    expect(inserts.length).toBeLessThanOrEqual(1);
    // And it still round-trips both sides exactly.
    expect(rebuildBaseline(ops)).toBe(baseline);
    expect(rebuildCurrent(ops)).toBe(current);
  });

  it('PRESERVES a genuinely-unchanged LONG anchor phrase (does not over-strike)', () => {
    const baseline = 'Please review the indemnification clause carefully today';
    const current = 'Kindly review the indemnification clause carefully now';
    const ops = diffTokens(baseline, current);
    // "review the indemnification clause carefully" is a long unchanged anchor → stays equal, unstruck.
    expect(ops.some(o => o.type === 'equal' && o.text.includes('indemnification clause carefully'))).toBe(true);
    expect(rebuildBaseline(ops)).toBe(baseline);
    expect(rebuildCurrent(ops)).toBe(current);
  });

  it('leaves a clean single-word replacement unchanged (already one strike + one insert)', () => {
    const ops = cleanupSemantic(diffTokens('the quick brown fox', 'the swift brown fox'));
    expect(ops.filter(o => o.type === 'delete').map(o => o.text.trim())).toEqual(['quick']);
    expect(ops.filter(o => o.type === 'insert').map(o => o.text.trim())).toEqual(['swift']);
  });

  it('is idempotent (running cleanup again changes nothing)', () => {
    const once = diffTokens('alpha item beta item gamma', 'alpha changed item swapped gamma');
    expect(cleanupSemantic(once)).toEqual(once);
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
