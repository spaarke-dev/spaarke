/**
 * docxBridge.editorNodeCoverage.test.ts — task 047 (spaarkeai-compose-r8).
 *
 * WHY THIS FILE EXISTS. The published residual-loss list covers one direction: constructs we IMPORT from
 * a .docx and cannot carry back out. Reviewing the TipTap extension inventory surfaced a second direction
 * nobody had measured — content the user creates IN OUR OWN EDITOR that the content model has no kind for.
 *
 * `ComposeBlockKind` is exactly four values (Paragraph, Heading, ListItem, Table). The editor's locked
 * extension set offers rather more than that: Image, HorizontalRule, CodeBlock, Blockquote, TaskList and
 * TaskItem all have schema nodes. `BLOCK_NODE_TYPES` in the mapper is `{paragraph, heading}`, and
 * `forEachBlock` only ever calls back for those — so anything else contributes whatever paragraphs happen
 * to be nested inside it, and nothing else.
 *
 * That is a worse failure than the import direction when it bites: the user typed it, saw it on screen,
 * and it silently is not in the saved file. It is reachable by PASTE even where a toolbar button does not
 * exist — pasting a web page or a Word selection brings images and rules with it.
 *
 * This file MEASURES that surface rather than assuming it. It asserts only what is certain (the mapper
 * runs, and text-bearing nodes keep their text); every node's actual fate is reported so the numbers can
 * drive the fix order, exactly as the residual-loss measurement did.
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Underline from '@tiptap/extension-underline';
import Link from '@tiptap/extension-link';
import Image from '@tiptap/extension-image';
import Table from '@tiptap/extension-table';
import TableRow from '@tiptap/extension-table-row';
import TableHeader from '@tiptap/extension-table-header';
import TableCell from '@tiptap/extension-table-cell';
import TaskList from '@tiptap/extension-task-list';
import TaskItem from '@tiptap/extension-task-item';
import { COMPOSE_R3_PARAID } from '../widgets/paraIdExtension';
import { buildContentModel } from './docxBridge';

/** Mirrors the editor's locked extension set closely enough to exercise the same schema nodes. */
function makeEditor(content: string): Editor {
  return new Editor({
    extensions: [
      StarterKit.configure({ heading: { levels: [1, 2, 3, 4, 5, 6] as const } }),
      Underline,
      Link.configure({ openOnClick: false, autolink: true }),
      Image.configure({ inline: false, allowBase64: true }),
      Table.configure({ resizable: true }),
      TableRow,
      TableHeader,
      TableCell,
      TaskList,
      TaskItem.configure({ nested: true }),
      COMPOSE_R3_PARAID,
    ],
    content,
  });
}

/** A 1x1 transparent PNG as a data URI — a real image the editor will accept. */
const PNG_1X1 =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk' +
  'YPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';

interface Probe {
  readonly name: string;
  readonly html: string;
  /** Text the user can see and would expect to survive. Empty = the node carries no text of its own. */
  readonly visibleText: string;
}

const PROBES: readonly Probe[] = [
  { name: 'image', html: `<p>Before</p><img src="${PNG_1X1}" alt="seal"><p>After</p>`, visibleText: '' },
  { name: 'horizontalRule', html: '<p>Before</p><hr><p>After</p>', visibleText: '' },
  { name: 'codeBlock', html: '<p>Before</p><pre><code>SELECT 1;</code></pre><p>After</p>', visibleText: 'SELECT 1;' },
  {
    name: 'blockquote',
    html: '<p>Before</p><blockquote><p>Quoted clause.</p></blockquote><p>After</p>',
    visibleText: 'Quoted clause.',
  },
  {
    name: 'taskList',
    html: '<p>Before</p><ul data-type="taskList"><li data-type="taskItem" data-checked="true"><p>Signed</p></li></ul><p>After</p>',
    visibleText: 'Signed',
  },
];

describe('editor-node coverage — what the content model can represent of what the editor offers', () => {
  it.each(PROBES.map(p => [p.name, p] as const))(
    '%s: reports whether the node survives buildContentModel',
    (_name, probe) => {
      const editor = makeEditor(probe.html);
      const model = buildContentModel(editor);

      const kinds = model.blocks.map(b => b.kind);
      const allText = model.blocks
        .flatMap(b => b.runs ?? [])
        .map(r => r.text)
        .join('');

      const textSurvived = probe.visibleText.length === 0 ? null : allText.includes(probe.visibleText);

      // eslint-disable-next-line no-console
      console.log(
        `${probe.name.padEnd(16)} blocks=${model.blocks.length} kinds=[${kinds.join(',')}] ` +
          `visibleText=${probe.visibleText ? (textSurvived ? 'KEPT' : 'LOST') : 'n/a (no text)'}`
      );

      // The sentinel paragraphs bracket the probe, so a mapper that silently swallowed EVERYTHING would
      // be caught here rather than reported as a clean result.
      expect(allText).toContain('Before');
      expect(allText).toContain('After');

      // The model has four block kinds; anything the editor emits beyond them cannot be named.
      expect(kinds.every(k => ['Paragraph', 'Heading', 'ListItem', 'Table'].includes(k))).toBe(true);

      editor.destroy();
    }
  );

  it('image content is not representable at all — the model has no run field or block kind for it', () => {
    // The clearest case, called out on its own because it is the one a user is most likely to hit by
    // PASTE and the one where the loss is total: no text, no placeholder, no warning. If a future change
    // gives images a representation, this test should FAIL and be rewritten to assert the round trip —
    // exactly what task 046 did for the soft line break.
    const editor = makeEditor(`<p>Before</p><img src="${PNG_1X1}" alt="seal"><p>After</p>`);
    const model = buildContentModel(editor);

    const serialized = JSON.stringify(model);
    expect(serialized).not.toContain('data:image');
    expect(serialized).not.toContain('seal');

    editor.destroy();
  });
});
