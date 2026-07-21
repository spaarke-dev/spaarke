/**
 * trackChangesDiff.ts — token-level diff for the live Track Changes view (spaarkeai-compose-r3, UAT
 * round-4 item 4).
 *
 * Item 4 asks for user edits on a (server-managed) document to render as LIVE tracked-change redlines.
 * The chosen design renders those redlines as a ProseMirror DECORATION overlay — NOT as content marks —
 * so the editor's document content stays equal to the user's REAL edited text. That matters because
 * persistence rides the EXISTING save path: `collectEditedParagraphs` (docxBridge.ts) sends the
 * paraId-keyed new settled text and the BFF `ComposeParagraphRedlineSynthesizer` (FR-02) diffs it
 * against the retained-original baseline to emit `w:ins`/`w:del` tracked changes in place. Content
 * marks would be stripped by `rejectStateText` and never persist; a decoration is a pure VIEW layer
 * that cannot change content or corrupt the document.
 *
 * This module is the pure diff engine behind that overlay: given a paragraph's BASELINE (load-time
 * reject-state) text and its CURRENT text, it returns an ordered op list the decoration builder maps to
 * inline (insertion) + widget (deletion) decorations. It intentionally mirrors, on the CLIENT and at the
 * WORD level, the same shape of change the server synthesizes — so the live preview matches what a save
 * will actually persist.
 *
 * Algorithm: a classic LCS over WHITESPACE-PRESERVING tokens (`\S+` word runs and `\s+` gap runs, so
 * exact character offsets are recoverable for decoration positioning). Word-level (not char-level) keeps
 * the redline visually clean ("the ~~quick~~ swift fox", not a per-character churn).
 */

/** One diff span. `equal` text is unchanged; `insert` is new (added) text; `delete` is removed text. */
export interface TrackChangeDiffOp {
  type: 'equal' | 'insert' | 'delete';
  text: string;
}

/** Tokenize into whitespace-preserving runs so concatenation is loss-less and offsets are recoverable. */
function tokenize(text: string): string[] {
  return text.match(/\S+|\s+/g) ?? [];
}

/**
 * Word-level diff of `baseline` → `current`. Returns an ordered op list whose concatenated
 * `equal`+`insert` text reproduces `current`, and whose `equal`+`delete` text reproduces `baseline`.
 * Adjacent same-type ops are coalesced so the decoration layer emits one span per contiguous change.
 */
export function diffTokens(baseline: string, current: string): TrackChangeDiffOp[] {
  const a = tokenize(baseline);
  const b = tokenize(current);

  // LCS length table (rows = a, cols = b). Small paragraph texts — O(n*m) is fine.
  const n = a.length;
  const m = b.length;
  const lcs: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      lcs[i][j] = a[i] === b[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
    }
  }

  // Backtrack into raw ops.
  const raw: TrackChangeDiffOp[] = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (a[i] === b[j]) {
      raw.push({ type: 'equal', text: a[i] });
      i++;
      j++;
    } else if (lcs[i + 1][j] >= lcs[i][j + 1]) {
      raw.push({ type: 'delete', text: a[i] });
      i++;
    } else {
      raw.push({ type: 'insert', text: b[j] });
      j++;
    }
  }
  while (i < n) raw.push({ type: 'delete', text: a[i++] });
  while (j < m) raw.push({ type: 'insert', text: b[j++] });

  // Coalesce adjacent same-type ops (one span per contiguous change region).
  const ops: TrackChangeDiffOp[] = [];
  for (const op of raw) {
    const last = ops[ops.length - 1];
    if (last && last.type === op.type) last.text += op.text;
    else ops.push({ ...op });
  }
  return ops;
}

/**
 * A decoration-ready change region within a paragraph's CURRENT text. `insertFrom`/`insertTo` are
 * character offsets (into the current paragraph text) of an ADDED span → an inline "insertion"
 * decoration. `deleteAt` + `deleteText` describe a REMOVED span → a widget "deletion" decoration
 * (struck text rendered at that offset; the text is not in the current doc). A region carries one or
 * both (a replace is a delete + insert at the same offset).
 */
export interface TrackChangeRegion {
  /** Char offset in the CURRENT paragraph text where this region begins. */
  offset: number;
  /** Inserted (added) span length, in chars; 0 when this region only deletes. */
  insertLength: number;
  /** Deleted (removed) text to render struck at `offset`; empty when this region only inserts. */
  deleteText: string;
}

/**
 * Reduce a {@link diffTokens} op list to positioned {@link TrackChangeRegion}s over the CURRENT
 * paragraph text. `equal` and `insert` ops advance the current-text cursor; `delete` ops do not (the
 * deleted text is absent from the current doc) — they attach as a struck widget at the current cursor.
 * Contiguous insert/delete runs are already coalesced by {@link diffTokens}.
 */
export function diffToRegions(ops: readonly TrackChangeDiffOp[]): TrackChangeRegion[] {
  const regions: TrackChangeRegion[] = [];
  let cursor = 0;
  for (let k = 0; k < ops.length; k++) {
    const op = ops[k];
    if (op.type === 'equal') {
      cursor += op.text.length;
      continue;
    }
    if (op.type === 'delete') {
      // A delete immediately followed by an insert is a REPLACE — merge into one region at `cursor`.
      const next = ops[k + 1];
      if (next && next.type === 'insert') {
        regions.push({ offset: cursor, insertLength: next.text.length, deleteText: op.text });
        cursor += next.text.length;
        k++; // consume the paired insert
      } else {
        regions.push({ offset: cursor, insertLength: 0, deleteText: op.text });
      }
      continue;
    }
    // Pure insert.
    regions.push({ offset: cursor, insertLength: op.text.length, deleteText: '' });
    cursor += op.text.length;
  }
  return regions;
}
