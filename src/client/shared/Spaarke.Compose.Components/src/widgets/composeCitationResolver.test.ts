/**
 * composeCitationResolver.test.ts — ai-advanced-capabilities-agreements-r1 task 011 (spec FR-03).
 *
 * Two groups of coverage:
 *
 *  (1) PARITY with the server `CitationResolver` — the "structured in-memory map" cases are ported
 *      VERBATIM (same maps, same citation strings, same expected paraIds) from
 *      `tests/integration/seam/Compose/ComposeCitationResolverSeamTests.cs`: letter/roman sub-item +
 *      decoy-neighbor, section-prefix tolerance, and bullet-exclusion. These are the cases that build
 *      an in-memory `ParaIdMapEntry[]` rather than loading a real corpus `.docx` via the server-only
 *      fixture loader, so they translate directly into a client-side parity assertion. Additional
 *      synthetic-but-structurally-equivalent fixtures cover single-label, decimal sub-item depth, and
 *      contiguous range resolution (the corpus-derived cases — same ListPath shapes, synthetic paraIds)
 *      plus the negative/malformed-input table.
 *
 *  (2) PARAID STABILITY (acceptance criterion #4) — `resolveCitation` always returns a paraId (never a
 *      raw position), and `collectBlocks` always re-walks the LIVE document, so resolving the SAME
 *      `sectionRef` again after an edit inserted ABOVE the target still lands on the target's
 *      (now-shifted) live position. Uses the SAME headless `@tiptap/core` `Editor` + `stampParaIds` +
 *      `COMPOSE_R3_PARAID` convention `ComposeEditor.paraId.test.tsx` established for this kind of
 *      schema-level suite — no React/Fluent/PaneEventBus mounting needed.
 *
 * @see ./composeCitationResolver.ts — the module under test (client mirror of CitationResolver.cs).
 * @see ./ComposeEditor.advisoryComments.test.tsx — the integration-level tests exercising
 *      `placeAdvisoryComments`'s deterministic-first wiring through the REAL ComposeEditor.
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { COMPOSE_R3_PARAID } from './paraIdExtension';
import { collectBlocks } from './importedRevisions';
import { stampParaIds } from '../utils/docxBridge';
import { resolveCitation } from './composeCitationResolver';
import type { ParaIdMapEntry } from '../types/compose-contracts';

function entry(
  index: number,
  paraId: string,
  computedNumber: string,
  listPath: number[]
): ParaIdMapEntry {
  return { index, paraId, isMinted: false, computedNumber, numberingLevel: listPath.length - 1, listPath };
}

describe('resolveCitation — parity with CitationResolver.cs (structured-map cases)', () => {
  // ═══════════════════════════════════════════════════════════════════════════════════════════
  // (1) SINGLE LABEL — synthetic analogue of heading-style-numbering.docx ordinal 9 ("4.2").
  // ═══════════════════════════════════════════════════════════════════════════════════════════

  const headingStyleMap: ParaIdMapEntry[] = [
    entry(6, 'HEAD0004', '4', [4]), // Heading1 "Confidentiality"
    entry(9, 'HEAD0042', '4.2', [4, 2]), // Heading2 "Confidentiality" (the FR-12 example)
  ];

  it.each([
    'Section 4.2',
    'section 4.2',
    '4.2',
    '§ 4.2',
    '§4.2',
    'Article 4.2',
    '  Section   4.2  ',
  ])('resolves %s to the exact [4,2] clause paraId', citation => {
    const result = resolveCitation(citation, headingStyleMap);
    expect(result.shape).toBe('single');
    expect(result.matches.map(m => m.paraId)).toEqual(['HEAD0042']);
  });

  it('resolves "Section 4" to the top-level [4] heading, not the [4,2] sub-heading', () => {
    const result = resolveCitation('Section 4', headingStyleMap);
    expect(result.matches.map(m => m.paraId)).toEqual(['HEAD0004']);
  });

  // ═══════════════════════════════════════════════════════════════════════════════════════════
  // (2) SUB-ITEM DEPTH — decimal (synthetic analogue of multilevel-1-1-1.docx) + letter/roman
  //     (ported verbatim from the C# structured-map tests).
  // ═══════════════════════════════════════════════════════════════════════════════════════════

  it('resolves decimal sub-item depth "1.1.1" to the deepest clause, not the top-level "1"', () => {
    const map: ParaIdMapEntry[] = [
      entry(0, 'MLVL0000', '1', [1]),
      entry(1, 'MLVL0001', '1.1', [1, 1]),
      entry(2, 'MLVL0002', '1.1.1', [1, 1, 1]),
    ];
    const result = resolveCitation('1.1.1', map);
    expect(result.matches.map(m => m.paraId)).toEqual(['MLVL0002']);
  });

  it('parses letter/roman sub-item "4.2(b)(iii)" and matches EXACTLY [4,2,2,3], not a prefix or neighbor (ported from ComposeCitationResolverSeamTests.cs)', () => {
    // b = 2nd lower-letter, iii = 3rd lower-roman → [4, 2, 2, 3]. The decoy (iv) neighbor proves the
    // parse lands on the exact chain, not a prefix.
    const map: ParaIdMapEntry[] = [
      entry(0, 'AAAA0001', '4.2', [4, 2]),
      entry(1, 'AAAA0002', '4.2(a)', [4, 2, 1]),
      entry(2, 'AAAA0003', '4.2(b)', [4, 2, 2]),
      entry(3, 'AAAA0004', '4.2(b)(iii)', [4, 2, 2, 3]),
      entry(4, 'AAAA0005', '4.2(b)(iv)', [4, 2, 2, 4]),
    ];

    const result = resolveCitation('4.2(b)(iii)', map);

    expect(result.shape).toBe('subItem');
    expect(result.matches.map(m => m.paraId)).toEqual(['AAAA0004']);
  });

  it('tolerates a "Section" prefix on a letter/roman sub-item citation (ported from ComposeCitationResolverSeamTests.cs)', () => {
    const map: ParaIdMapEntry[] = [entry(0, 'BBBB0001', '4.2(b)(iii)', [4, 2, 2, 3])];
    expect(resolveCitation('Section 4.2(b)(iii)', map).matches.map(m => m.paraId)).toEqual(['BBBB0001']);
  });

  // ═══════════════════════════════════════════════════════════════════════════════════════════
  // (3) CONTIGUOUS RANGE — synthetic analogue of nda-interrupted-clauses.docx (6 top-level clauses).
  // ═══════════════════════════════════════════════════════════════════════════════════════════

  const interruptedClausesMap: ParaIdMapEntry[] = [
    entry(2, 'IC00001', '1', [1]),
    entry(3, 'IC00002', '2', [2]),
    entry(4, 'IC00003', '3', [3]),
    entry(12, 'IC00004', '4', [4]),
    entry(13, 'IC00005', '5', [5]),
    entry(14, 'IC00006', '6', [6]),
  ];

  it.each(['Sections 4–7', 'Sections 4-7', '4-7', '§§ 4-7', 'Sections 7–4'])(
    'resolves range %s to top-level ordinals {4,5,6} in document order (7 does not exist)',
    citation => {
      const result = resolveCitation(citation, interruptedClausesMap);
      expect(result.shape).toBe('range');
      expect(result.matches.map(m => m.paraId)).toEqual(['IC00004', 'IC00005', 'IC00006']);
    }
  );

  it('a range spanning the whole document includes sub-items under each top-level section (synthetic analogue of multilevel-1-1-1.docx)', () => {
    const map: ParaIdMapEntry[] = [
      entry(0, 'ML0', '1', [1]),
      entry(1, 'ML1', '1.1', [1, 1]),
      entry(2, 'ML2', '1.1.1', [1, 1, 1]),
      entry(3, 'ML3', '1.1.2', [1, 1, 2]),
      entry(4, 'ML4', '1.2', [1, 2]),
      entry(5, 'ML5', '2', [2]),
      entry(6, 'ML6', '2.1', [2, 1]),
    ];
    const result = resolveCitation('Sections 1-2', map);
    expect(result.matches.map(m => m.paraId)).toEqual(['ML0', 'ML1', 'ML2', 'ML3', 'ML4', 'ML5', 'ML6']);
  });

  // ═══════════════════════════════════════════════════════════════════════════════════════════
  // (4) NEGATIVE — unresolvable/malformed citations return explicit not-found, never a throw.
  // ═══════════════════════════════════════════════════════════════════════════════════════════

  it('returns an explicit not-found (never a fabricated paraId) for a nonexistent section', () => {
    const result = resolveCitation('Section 99', headingStyleMap);
    expect(result.matches).toEqual([]);
  });

  it('returns an explicit not-found for a range with no members', () => {
    expect(resolveCitation('Sections 20-30', headingStyleMap).matches).toEqual([]);
  });

  it.each([null, undefined, '', '   ', 'not a citation', 'Section', 'Section abc', '4.2(', '4.2(!!)', '4.2 xyz'])(
    'never throws and returns zero matches for malformed input %s',
    citation => {
      expect(() => resolveCitation(citation, headingStyleMap)).not.toThrow();
      expect(resolveCitation(citation, headingStyleMap).matches).toEqual([]);
    }
  );

  it('a bullet paragraph (non-numeric ComputedNumber, numeric ListPath) is NOT reachable by a numeric citation (ported from ComposeCitationResolverSeamTests.cs)', () => {
    const map: ParaIdMapEntry[] = [entry(0, 'CCCC0001', '•', [4])];
    expect(resolveCitation('Section 4', map).matches).toEqual([]);
    expect(resolveCitation('Sections 1-9', map).matches).toEqual([]);
  });

  it('returns zero matches when the reference map is empty (pre-WS-4 caller)', () => {
    expect(resolveCitation('Section 4.2', []).matches).toEqual([]);
  });
});

describe('resolveCitation + collectBlocks — paraId stability across edits (acceptance criterion #4)', () => {
  function makeEditor(content: string): Editor {
    return new Editor({ extensions: [StarterKit, ...COMPOSE_R3_PARAID], content });
  }

  it('resolves the SAME sectionRef to the SAME paraId (at its new, shifted position) after a paragraph is inserted above it', () => {
    const editor = makeEditor(
      '<p>Clause 4.1: Confidentiality obligations survive termination for three years.</p>' +
        '<p>Clause 4.2: The receiving party shall indemnify the disclosing party for any breach.</p>'
    );
    const referenceMap: ParaIdMapEntry[] = [
      entry(0, 'STAB0041', '4.1', [4, 1]),
      entry(1, 'STAB0042', '4.2', [4, 2]),
    ];
    stampParaIds(editor, referenceMap);

    const before = resolveCitation('Section 4.2', referenceMap);
    expect(before.matches).toHaveLength(1);
    const targetParaId = before.matches[0].paraId;
    expect(targetParaId).toBe('STAB0042');

    const blockBefore = collectBlocks(editor).find(b => b.paraId === targetParaId);
    expect(blockBefore).toBeDefined();
    expect(editor.state.doc.textBetween(blockBefore!.from, blockBefore!.to, ' ')).toContain('indemnify');

    // Insert a brand-new, unnumbered paragraph ABOVE the target — every subsequent position shifts.
    editor.chain().insertContentAt(0, '<p>Newly inserted preamble paragraph.</p>').run();

    const blockAfter = collectBlocks(editor).find(b => b.paraId === targetParaId);
    expect(blockAfter).toBeDefined();
    // The position genuinely shifted (proves the edit actually moved the target)...
    expect(blockAfter!.from).toBeGreaterThan(blockBefore!.from);
    // ...but the SAME paraId still carries the SAME clause text.
    expect(editor.state.doc.textBetween(blockAfter!.from, blockAfter!.to, ' ')).toContain('indemnify');

    // The deterministic resolution itself (referenceMap is position-free — computedNumber/listPath do
    // not change from an unnumbered insert) still resolves the SAME sectionRef to the SAME paraId.
    const after = resolveCitation('Section 4.2', referenceMap);
    expect(after.matches).toHaveLength(1);
    expect(after.matches[0].paraId).toBe(targetParaId);

    editor.destroy();
  });
});
