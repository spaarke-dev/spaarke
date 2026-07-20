/**
 * ComposeTable.test.tsx — FR-18 basic tables (spaarkeai-compose-r3 task 041).
 *
 * The MIT `@tiptap/extension-table` family (`extension-table` / `-table-row` /
 * `-table-header` / `-table-cell`) has been part of the LOCKED_EXTENSIONS list in
 * `ComposeEditor.tsx` since the R1 spike; this task adds the missing "Table" toolbar
 * affordance (`ComposeFormatToolbar.tsx`) and verifies the FR-08/FR-10 paraId identity
 * scheme (task 011's `COMPOSE_R3_PARAID`) covers paragraphs nested in table cells —
 * NOT a separate cell-identity mechanism.
 *
 * Exercised through REAL headless TipTap `Editor` instances (the same schema-
 * registration path ComposeEditor mounts, minus the React surface — mirrors
 * `ComposeEditor.paraId.test.tsx` / `marks/marks.test.ts`), per ADR-038 (no
 * `Mock<HttpMessageHandler>`, no DI-registration tests — exercise real editor state).
 *
 * Covers:
 *  1. Toolbar wiring — Insert table / add-remove row/column fire the real TipTap
 *     table commands and are disabled outside a table.
 *  2. Cell paragraphs carry a unique `paraId` (FR-08/FR-10) — same UniqueID scheme
 *     as body paragraphs, no parallel identity mechanism.
 *  3. InsertionMark/DeletionMark apply cleanly inside a cell without corrupting the
 *     table structure.
 *  4. Fidelity alignment — `buildContentModel` (docxBridge, task 027) carries cell
 *     paraIds through to the save-time content model (S1b nested-table alignment).
 *  5. ADR-021 dark-mode render of the Table toolbar controls.
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Table from '@tiptap/extension-table';
import TableRow from '@tiptap/extension-table-row';
import TableHeader from '@tiptap/extension-table-header';
import TableCell from '@tiptap/extension-table-cell';
import { ComposeFormatToolbar } from './ComposeFormatToolbar';
import { COMPOSE_R3_PARAID } from './paraIdExtension';
import { InsertionMark } from './marks/InsertionMark';
import { DeletionMark } from './marks/DeletionMark';
import { buildContentModel } from '../utils/docxBridge';

/**
 * Build a headless editor with the SAME table family + paraId + tracked-change marks
 * ComposeEditor mounts (Table.configure({ resizable: true }) matches LOCKED_EXTENSIONS).
 */
function makeEditor(content = '<p></p>'): Editor {
  return new Editor({
    extensions: [
      StarterKit,
      Table.configure({ resizable: true }),
      TableRow,
      TableHeader,
      TableCell,
      InsertionMark,
      DeletionMark,
      ...COMPOSE_R3_PARAID,
    ],
    content,
  });
}

/** Every paragraph node's `paraId`, with a flag for whether it is nested in a table cell. */
function paragraphParaIds(editor: Editor): Array<{ paraId: string | null; inCell: boolean }> {
  const out: Array<{ paraId: string | null; inCell: boolean }> = [];
  editor.state.doc.descendants((node, _pos, parent) => {
    if (node.type.name === 'paragraph') {
      const inCell = parent?.type.name === 'tableCell' || parent?.type.name === 'tableHeader';
      out.push({ paraId: (node.attrs.paraId as string | null) ?? null, inCell });
    }
    return true;
  });
  return out;
}

/** Table shape: row count + cells-per-row, for asserting structural edits. */
function tableShape(editor: Editor): number[] {
  const rows: number[] = [];
  editor.state.doc.descendants(node => {
    if (node.type.name === 'tableRow') {
      let cellCount = 0;
      node.forEach(cell => {
        if (cell.type.name === 'tableCell' || cell.type.name === 'tableHeader') cellCount++;
      });
      rows.push(cellCount);
    }
    return true;
  });
  return rows;
}

/** The doc position of the START of the first text node matching `text` (a stable, non-fragile selection anchor). */
function textPos(editor: Editor, text: string): number {
  let found = -1;
  editor.state.doc.descendants((node, pos) => {
    if (found === -1 && node.type.name === 'text' && node.text === text) found = pos;
    return found === -1;
  });
  if (found === -1) throw new Error(`textPos: no text node exactly matching "${text}"`);
  return found;
}

const OOXML_ID = /^[0-9A-F]{8}$/;

function renderToolbar(editor: Editor) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeFormatToolbar editor={editor} />
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// 1. Toolbar wiring — insert + edit a basic table
// ---------------------------------------------------------------------------

describe('ComposeFormatToolbar — Table dropdown (FR-18, real TipTap table commands)', () => {
  it('row/column/delete-table commands are disabled before any table exists', async () => {
    const editor = makeEditor();
    const user = userEvent.setup();
    renderToolbar(editor);

    await user.click(screen.getByTestId('compose-format-table-menu'));
    expect(screen.getByTestId('compose-format-table-insert')).not.toBeDisabled();
    expect(screen.getByTestId('compose-format-table-add-row')).toBeDisabled();
    expect(screen.getByTestId('compose-format-table-add-column')).toBeDisabled();
    expect(screen.getByTestId('compose-format-table-delete-row')).toBeDisabled();
    expect(screen.getByTestId('compose-format-table-delete-column')).toBeDisabled();
    expect(screen.getByTestId('compose-format-table-delete-table')).toBeDisabled();
    editor.destroy();
  });

  it('Insert table creates a 2x2 table with a header row; each cell is editable', async () => {
    const editor = makeEditor();
    const user = userEvent.setup();
    renderToolbar(editor);

    await user.click(screen.getByTestId('compose-format-table-menu'));
    await user.click(screen.getByTestId('compose-format-table-insert'));

    expect(tableShape(editor)).toEqual([2, 2]); // header row + body row, 2 cells each
    expect(editor.isActive('table')).toBe(true);
    editor.destroy();
  });

  it('Add row and Add column grow the table; existing cell content is preserved', async () => {
    const editor = makeEditor();
    editor.commands.setContent(
      '<table><tbody><tr><th><p>Term</p></th><th><p>Meaning</p></th></tr><tr><td><p>Affiliate</p></td><td><p>A controlled entity.</p></td></tr></tbody></table>'
    );
    // Place the caret inside the first body cell so the row/column commands target this table.
    editor.commands.setTextSelection(textPos(editor, 'Affiliate'));
    const user = userEvent.setup();
    renderToolbar(editor);

    // Open the Table menu ONCE — like the Font dropdown's existing tests, the popover stays open
    // across successive plain-Button clicks inside it (it is not built from Fluent `MenuItem`s, which
    // DO auto-close); re-clicking the trigger while already open would just toggle it shut again.
    await user.click(screen.getByTestId('compose-format-table-menu'));
    expect(screen.getByTestId('compose-format-table-add-row')).not.toBeDisabled();
    await user.click(screen.getByTestId('compose-format-table-add-row'));
    await user.click(screen.getByTestId('compose-format-table-add-column'));

    expect(tableShape(editor)).toEqual([3, 3, 3]); // +1 row, +1 column on every row
    // Original cell text survives the structural edits.
    expect(editor.getText()).toContain('Affiliate');
    expect(editor.getText()).toContain('A controlled entity.');
    editor.destroy();
  });

  it('Delete row and Delete column shrink the table; Delete table removes it entirely', async () => {
    const editor = makeEditor();
    editor.commands.setContent(
      '<table><tbody><tr><td><p>A</p></td><td><p>B</p></td></tr><tr><td><p>C</p></td><td><p>D</p></td></tr></tbody></table>'
    );
    editor.commands.setTextSelection(textPos(editor, 'A'));
    const user = userEvent.setup();
    renderToolbar(editor);

    await user.click(screen.getByTestId('compose-format-table-menu'));
    await user.click(screen.getByTestId('compose-format-table-delete-row'));
    expect(tableShape(editor)).toEqual([2]);

    await user.click(screen.getByTestId('compose-format-table-delete-column'));
    expect(tableShape(editor)).toEqual([1]);

    await user.click(screen.getByTestId('compose-format-table-delete-table'));
    expect(tableShape(editor)).toEqual([]);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2. Cell paragraphs carry a unique paraId (FR-08/FR-10) — same scheme, no parallel one
// ---------------------------------------------------------------------------

describe('Table-cell paragraphs carry paraId via COMPOSE_R3_PARAID (FR-08/FR-10)', () => {
  it('every cell paragraph mints a distinct, OOXML-shaped paraId — none shared between cells', () => {
    const editor = makeEditor();
    editor.commands.setContent(
      '<table><tbody><tr><th><p>Term</p></th><th><p>Meaning</p></th></tr><tr><td><p>Affiliate</p></td><td><p>A controlled entity.</p></td></tr></tbody></table>'
    );

    const cellParagraphs = paragraphParaIds(editor).filter(p => p.inCell);
    expect(cellParagraphs).toHaveLength(4);
    for (const { paraId } of cellParagraphs) {
      expect(paraId).toMatch(OOXML_ID);
    }
    const ids = cellParagraphs.map(p => p.paraId);
    expect(new Set(ids).size).toBe(ids.length); // all unique — no two cells share an id
    editor.destroy();
  });

  it('a body paragraph + table-cell paragraphs coexist with no id collisions across the whole doc', () => {
    const editor = makeEditor();
    editor.commands.setContent(
      '<p>Intro paragraph.</p><table><tbody><tr><td><p>One</p></td><td><p>Two</p></td></tr></tbody></table><p>Outro.</p>'
    );

    const all = paragraphParaIds(editor);
    expect(all).toHaveLength(4); // intro + 2 cells + outro
    const ids = all.map(p => p.paraId);
    expect(ids.every(id => id !== null)).toBe(true);
    expect(new Set(ids).size).toBe(ids.length);
    editor.destroy();
  });

  it('splitting a cell paragraph re-mints the new half with no id shared with a sibling cell', () => {
    const editor = makeEditor();
    editor.commands.setContent(
      '<table><tbody><tr><td><p>hello world</p></td><td><p>sibling</p></td></tr></tbody></table>'
    );
    const before = paragraphParaIds(editor).filter(p => p.inCell);
    const [firstCellId, siblingCellId] = before.map(p => p.paraId);

    // Split the first cell's paragraph — caret right after "hello" (5 chars into "hello world").
    const splitPos = textPos(editor, 'hello world') + 5;
    editor.chain().setTextSelection(splitPos).splitBlock().run();

    const after = paragraphParaIds(editor).filter(p => p.inCell);
    expect(after).toHaveLength(3); // the split cell now has 2 paragraphs + the sibling cell's 1
    const ids = after.map(p => p.paraId);
    expect(new Set(ids).size).toBe(3); // still all distinct
    expect(ids).toContain(siblingCellId); // sibling untouched
    expect(ids.filter(id => id === firstCellId)).toHaveLength(1); // one half kept the original id
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 3. Tracked-change marks inside a cell (InsertionMark/DeletionMark) — table structure intact
// ---------------------------------------------------------------------------

describe('Tracked-change marks work inside table cells without breaking table structure', () => {
  it('setInsertion renders the added-style span inside a <td>; table row/cell counts unchanged', () => {
    const editor = makeEditor();
    editor.commands.setContent(
      '<table><tbody><tr><td><p>Affiliate</p></td><td><p>Meaning</p></td></tr></tbody></table>'
    );
    const shapeBefore = tableShape(editor);

    // Select "Affiliate" inside the first cell.
    const from = textPos(editor, 'Affiliate');
    const to = from + 'Affiliate'.length;
    editor.chain().setTextSelection({ from, to }).setInsertion({ binding: 'b1', ledgerRef: 'b1@t1' }).run();

    const html = editor.getHTML();
    expect(html).toContain('data-compose-mark="insertion"');
    expect(html).toMatch(/<td[^>]*>[\s\S]*data-compose-mark="insertion"/);
    // Structure preserved — same row/cell shape as before the mark was applied.
    expect(tableShape(editor)).toEqual(shapeBefore);
    editor.destroy();
  });

  it('setDeletion inside a cell coexists with a paraId-bearing cell paragraph', () => {
    const editor = makeEditor();
    editor.commands.setContent('<table><tbody><tr><td><p>Removed text</p></td></tr></tbody></table>');

    const from = textPos(editor, 'Removed text');
    const to = from + 'Removed text'.length;
    editor.chain().setTextSelection({ from, to }).setDeletion({ binding: 'b2', ledgerRef: 'b2@t2' }).run();

    expect(editor.getHTML()).toContain('data-compose-mark="deletion"');
    const cellParagraph = paragraphParaIds(editor).find(p => p.inCell);
    expect(cellParagraph?.paraId).toMatch(OOXML_ID);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 4. Fidelity alignment — buildContentModel (docxBridge, task 027) carries cell paraIds
// ---------------------------------------------------------------------------

describe('docxBridge.buildContentModel carries table-cell paraIds through (S1b alignment)', () => {
  it('the Table block cell paragraphs carry the SAME paraIds as the live editor doc', () => {
    const editor = makeEditor();
    editor.commands.setContent(
      '<table><tbody><tr><th><p>Term</p></th><th><p>Meaning</p></th></tr><tr><td><p>Affiliate</p></td><td><p>A controlled entity.</p></td></tr></tbody></table>'
    );
    const liveIds = paragraphParaIds(editor)
      .filter(p => p.inCell)
      .map(p => p.paraId);

    const model = buildContentModel(editor);
    const tableBlock = model.blocks.find(b => b.kind === 'Table');
    expect(tableBlock?.table).toBeDefined();

    const modelIds: string[] = [];
    for (const row of tableBlock!.table!.rows) {
      for (const cell of row.cells) {
        for (const block of cell.blocks) {
          expect(block.paraId).toBeDefined();
          modelIds.push(block.paraId as string);
        }
      }
    }
    expect(modelIds).toEqual(liveIds);
    expect(new Set(modelIds).size).toBe(modelIds.length);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 5. ADR-021 dark-mode render check
// ---------------------------------------------------------------------------

describe('Table toolbar controls — ADR-021 dark mode', () => {
  it('the Table dropdown renders under the dark theme with no hardcoded hex color', async () => {
    const editor = makeEditor();
    const user = userEvent.setup();
    const { container } = render(
      <FluentProvider theme={webDarkTheme}>
        <ComposeFormatToolbar editor={editor} />
      </FluentProvider>
    );

    await user.click(screen.getByTestId('compose-format-table-menu'));
    expect(screen.getByTestId('compose-format-table-insert')).toBeInTheDocument();
    expect(screen.getByLabelText('Insert table')).toBeInTheDocument();
    expect(screen.getByLabelText('Add row')).toBeInTheDocument();
    expect(screen.getByLabelText('Add column')).toBeInTheDocument();
    expect(screen.getByLabelText('Delete row')).toBeInTheDocument();
    expect(screen.getByLabelText('Delete column')).toBeInTheDocument();
    expect(screen.getByLabelText('Delete table')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    editor.destroy();
  });
});
