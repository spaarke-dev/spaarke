/**
 * RedlineCueExtension.ts — the AI-rationale "lightbulb" cue on a pending redline
 * (spaarkeai-compose-r8, UAT 2026-08-26 item 5).
 *
 * WHAT THIS REPLACES. The cue used to be a CSS `::before` pseudo-element on
 * `.compose-mark-insertion` / `.compose-mark-deletion` (ComposeEditor.tsx, deleted with this file's
 * introduction). That had three defects, all reported in one UAT round:
 *
 *   1. STRUCK THROUGH. `.compose-mark-deletion` sets `text-decoration-line: line-through`, which makes
 *      it a DECORATING BOX. Per CSS Text Decoration L3 §2.2 a decorating box paints its line across
 *      every in-flow inline descendant, and a descendant CANNOT switch it off — `text-decoration: none`
 *      on the pseudo-element was a silent no-op. The bulb was drawn with a line through it, reading as
 *      deleted content. (The insertion half got an underline through it for the same reason.)
 *   2. TOO SMALL. `0.8em` against document body text is ~9-11px — the user reported it as "hidden".
 *   3. DUPLICATED AND MIS-PLACED. The old rule suppressed the pair's second bulb with the sibling
 *      selector `.compose-mark-deletion + .compose-mark-insertion::before`. That only holds when the
 *      insertion is a single span AND is the deletion's immediate sibling. Neither survives the FR-15
 *      inline-markup subset: `new_text` containing `<strong>` SPLITS the insertion into several mark
 *      spans (one bulb each), and `new_text` STARTING with `<strong>` makes the deletion's next sibling
 *      a `<strong>`, so `+` never matches and the suppression fails entirely — three bulbs. This is not
 *      an edge case: `redlineLocalDiff.ts` deliberately falls back to whole-paragraph replacement when
 *      `new_text` carries markup, so formatted AI legal edits take exactly that path BY DESIGN.
 *
 * WHY A WIDGET DECORATION FIXES ALL THREE. A widget is a SIBLING of the mark spans, not a descendant,
 * so it sits outside every decorating box and can never inherit strikethrough or underline — this is
 * structural, not a CSS override that a future style could defeat. It is emitted at a computed position
 * (the leading edge of the change), so placement no longer depends on DOM sibling shape. And it is
 * emitted once per change regardless of how many spans the marks fragment into.
 *
 * GROUPING RULE: one cue per CONTIGUOUS RUN of insertion/deletion marks sharing a ledgerRef — not one
 * per ledgerRef. A single AI edit can legitimately touch several separated places in a paragraph; those
 * are distinct visual changes and each deserves its own cue. Collapsing them to one marker would hide
 * changes from the reviewer. A deletion span immediately followed by its insertion span IS contiguous
 * (next.from === prev.to), so the canonical replace-pair still yields exactly ONE cue, at the front.
 *
 * VIEW-ONLY, SO IT CANNOT REACH THE .docx. `getHTML()`, `docxBridge` and `collectMarkedRanges` all read
 * the DOCUMENT MODEL, never the view — a decoration is invisible to every one of them. The widget's
 * `data-compose-mark="redline-cue"` value also matches neither InsertionMark's nor DeletionMark's
 * `parseHTML` selector (`span[data-compose-mark="insertion"|"deletion"]`), so even a hypothetical
 * re-parse of rendered view HTML could not turn a cue into a mark.
 *
 * NOT FOLDED INTO TrackChangesExtension (CLAUDE.md §11 justification): that extension's plugin returns
 * `DecorationSet.empty` whenever the Track Changes toggle is off (TrackChangesExtension.ts). The AI
 * redline cue must render regardless of that toggle — an AI suggestion is document content, not the
 * user's own-edit overlay. Folding it in would couple the cue to a switch it must not obey, and the
 * concrete failure is: user turns Track Changes off, every AI rationale becomes unreachable.
 *
 * NFR-03: plain MIT `@tiptap/core` Extension + `@tiptap/pm` Plugin. No `@tiptap-pro/*`, no AGPL.
 *
 * @see ./InsertionMark.ts · ./DeletionMark.ts — the marks this observes (never modifies)
 * @see ./TrackChangesExtension.ts — the widget-decoration pattern this mirrors
 * @see ../ComposeEditor.tsx — `.compose-redline-cue` styling + the click handler that opens the popover
 */
import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import type { Node as PMNode } from '@tiptap/pm/model';

export const redlineCuePluginKey = new PluginKey('composeRedlineCue');

/** The mark names this cue attaches to. Both halves of a redline carry a rationale. */
const REDLINE_MARK_NAMES = new Set(['insertion', 'deletion']);

/** Class + attribute contract shared with ComposeEditor's styling and click handler. */
export const REDLINE_CUE_CLASS = 'compose-redline-cue';
export const REDLINE_CUE_MARK_VALUE = 'redline-cue';

interface MarkedRun {
  from: number;
  to: number;
  /** `{bindingId}@t{n}`, or '' when the mark carries no provenance. */
  ledgerRef: string;
}

/**
 * Collect maximal contiguous runs of redline-marked text, keyed by ledgerRef.
 * Exported for test: this is the whole of the grouping logic, and it is pure.
 */
export function collectRedlineRuns(doc: PMNode): MarkedRun[] {
  const runs: MarkedRun[] = [];

  doc.descendants((node, pos) => {
    if (!node.isText) return true;
    const redline = node.marks.find(m => REDLINE_MARK_NAMES.has(m.type.name));
    if (!redline) return true;

    const ledgerRef = (redline.attrs?.ledgerRef as string | null | undefined) ?? '';
    const from = pos;
    const to = pos + node.nodeSize;

    // Merge into the previous run when this span BUTTS UP AGAINST it and shares provenance. The
    // deletion→insertion pair and every `<strong>`-induced fragment satisfy both, so each collapses
    // to one run. A gap of even one unmarked character starts a new run — a genuinely separate change.
    const prev = runs[runs.length - 1];
    if (prev && prev.to === from && prev.ledgerRef === ledgerRef) {
      prev.to = to;
      return true;
    }

    runs.push({ from, to, ledgerRef });
    return true;
  });

  return runs;
}

function buildRedlineCueDecorations(doc: PMNode): DecorationSet {
  const decorations = collectRedlineRuns(doc).map(run =>
    Decoration.widget(
      run.from,
      () => {
        const span = document.createElement('span');
        span.className = REDLINE_CUE_CLASS;
        // Both attributes are required by ComposeEditor's click handler, which resolves the rationale
        // popover via `closest('[data-compose-mark][data-ledger-ref]')`. The OLD `::before` was a
        // pseudo-element and therefore never an event target — the click landed on the host mark span.
        // A widget IS a real element, so it must carry the contract itself.
        span.setAttribute('data-compose-mark', REDLINE_CUE_MARK_VALUE);
        span.setAttribute('data-ledger-ref', run.ledgerRef);
        span.setAttribute('contenteditable', 'false');
        span.setAttribute('aria-hidden', 'true'); // decorative; the rationale is reachable via the span
        span.textContent = '\u{1F4A1}'; // 💡
        return span;
      },
      // `side: -1` pins the cue BEFORE content at this position — the leading edge of the change.
      // `ignoreSelection` keeps it out of selection/caret arithmetic. The key must vary with the run's
      // extent so ProseMirror rebuilds rather than reuses a stale widget when the change grows.
      { side: -1, ignoreSelection: true, key: `cue-${run.ledgerRef}-${run.from}-${run.to}` }
    )
  );

  return DecorationSet.create(doc, decorations);
}

/**
 * Renders one lightbulb cue at the leading edge of each pending AI redline. Purely additive and
 * view-only: it observes insertion/deletion marks and never modifies the document.
 */
export const RedlineCueExtension = Extension.create({
  name: 'composeRedlineCue',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: redlineCuePluginKey,
        props: {
          decorations(state) {
            return buildRedlineCueDecorations(state.doc);
          },
        },
      }),
    ];
  },
});

export default RedlineCueExtension;
