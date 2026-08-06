/**
 * TrackChangesExtension.ts — live Track Changes DECORATION overlay (spaarkeai-compose-r3, UAT round-4
 * item 4).
 *
 * WHY A DECORATION, NOT A MARK (binding design rationale): item 4 requires user edits on a
 * server-managed document to render as live tracked-change redlines AND to PERSIST on save. Rendering
 * edits as content marks (insertion/deletion) would break persistence — `rejectStateText`
 * (docxBridge.ts) strips insertion marks and keeps deletion-marked text, so a marked paragraph's
 * reject-state snapshot would equal the original and the op-log would capture no operation for it →
 * the edit is lost. Instead, this extension leaves the document content equal to the user's REAL edited
 * text (so the ID-anchored operation-log path — the step interceptor → `ComposeShadowPatchEngine`,
 * tasks 020/022/032 — synthesizes the `w:ins`/`w:del` tracked changes server-side, exactly as it already
 * does; this replaced the retired R3 `collectEditedParagraphs` → `ComposeParagraphRedlineSynthesizer`
 * path) and renders the live redline purely as a ProseMirror DECORATION diffed against the load-time
 * baseline. A decoration is a VIEW layer: it cannot change content, cannot desync positions, cannot
 * corrupt the `.docx`. Toggle off → decorations vanish, content untouched.
 *
 * NFR-03: no TipTap product feature — a plain MIT `@tiptap/core` Extension + `@tiptap/pm` Plugin +
 * DecorationSet. No `@tiptap-pro/*`, no AGPL.
 *
 * Enabled state lives in PLUGIN STATE (toggled via a transaction meta) so a toggle re-runs
 * `decorations` deterministically. The baseline (paraId → load-time reject-state text) is read live via
 * the `getBaseline` option so decorations track edits without re-registering the plugin.
 *
 * @see ./InsertionMark.ts · ./DeletionMark.ts — the `compose-mark-insertion` / `compose-mark-deletion`
 *      token classes this reuses for consistent redline styling (ADR-021 dark-mode-correct)
 * @see ../hooks/trackChangesDiff.ts — the pure word-diff → positioned-region engine
 * @see ../../utils/docxBridge.ts — `captureParaIdSnapshot` (the baseline)
 * @see ../stepOperationInterceptor.ts — the ID-anchored operation-log capture (the persistence path
 *      this design deliberately rides)
 */
import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import type { Node as PMNode } from '@tiptap/pm/model';
import { diffTokens, diffToRegions } from '../hooks/trackChangesDiff';

export interface TrackChangesOptions {
  /** Live read of the toggle's initial state when the plugin inits (the toggle then drives it via meta). */
  initialEnabled: boolean;
  /** Live read of the baseline: `paraId → load-time reject-state text`. Empty map ⇒ no decorations. */
  getBaseline: () => ReadonlyMap<string, string>;
}

interface TrackChangesPluginState {
  enabled: boolean;
}

/** Plugin key — also the meta channel the toggle uses to flip `enabled`. */
export const trackChangesPluginKey = new PluginKey<TrackChangesPluginState>('composeTrackChanges');

/** The paraId node attribute (mirrors `paraIdExtension` COMPOSE_R3_PARAID's attribute name). */
const PARA_ID_ATTR = 'paraId';

/** Block node types that carry text + a paraId and participate in the diff. */
const TEXT_BLOCK_TYPES = new Set(['paragraph', 'heading']);

/**
 * Build the decoration set for the current doc vs the baseline. For each text block carrying a paraId
 * present in the baseline, word-diff the baseline (reject-state) text against the block's current text
 * and emit: an INLINE `compose-mark-insertion` decoration over each added span, and a WIDGET
 * `compose-mark-deletion` (struck text) at each removed span's offset. A block whose paraId is not in
 * the baseline (a brand-new / split paragraph) is skipped — there is no baseline to diff it against.
 */
export function buildTrackChangeDecorations(doc: PMNode, baseline: ReadonlyMap<string, string>): DecorationSet {
  if (baseline.size === 0) return DecorationSet.empty;
  const decorations: Decoration[] = [];

  doc.descendants((node, pos) => {
    if (!TEXT_BLOCK_TYPES.has(node.type.name)) return true;
    const paraId = node.attrs?.[PARA_ID_ATTR] as string | undefined;
    if (!paraId) return true;
    // A block that already carries an AI-suggestion redline (insertion/deletion mark) renders via those
    // marks — with the 💡 rationale cue + accept/reject. The live Track Changes overlay is for the user's
    // OWN free-typed edits only, so it must NOT also decorate an AI-suggestion block (that double-drew the
    // redline and hid the lightbulb). Skip such blocks.
    let hasRedlineMark = false;
    node.descendants(child => {
      if (child.isText && child.marks.some(m => m.type.name === 'insertion' || m.type.name === 'deletion')) {
        hasRedlineMark = true;
      }
      return !hasRedlineMark;
    });
    if (hasRedlineMark) return true;
    const baselineText = baseline.get(paraId);
    if (baselineText === undefined) return true; // no baseline for this block — skip (new/split para)
    const currentText = node.textContent;
    if (baselineText === currentText) return true; // unchanged — nothing to decorate

    const contentStart = pos + 1; // inside the block: char offset o → doc position contentStart + o
    const regions = diffToRegions(diffTokens(baselineText, currentText));
    for (const region of regions) {
      const at = contentStart + region.offset;
      if (region.insertLength > 0) {
        decorations.push(
          Decoration.inline(at, at + region.insertLength, {
            // A DISTINCT class from `compose-mark-insertion` (the AI-redline mark) so the live
            // track-change redline gets the same green-underline look WITHOUT the AI-rationale
            // lightbulb `::before` that class carries — a user's own edit has no rationale popover.
            class: 'compose-track-insertion',
            'data-compose-track': 'insertion',
          })
        );
      }
      if (region.deleteText) {
        // Widget: the removed text is absent from the current doc, so render it as a struck span at the
        // change offset. `side: -1` places it before content at the same position; `ignoreSelection`
        // keeps it non-interactive. Marked stale-on-content-change so ProseMirror rebuilds it correctly.
        const deleteText = region.deleteText;
        decorations.push(
          Decoration.widget(
            at,
            () => {
              const span = document.createElement('span');
              span.className = 'compose-track-deletion';
              span.setAttribute('data-compose-track', 'deletion');
              span.setAttribute('contenteditable', 'false');
              span.textContent = deleteText;
              return span;
            },
            { side: -1, ignoreSelection: true, key: `del-${paraId}-${region.offset}-${deleteText.length}` }
          )
        );
      }
    }
    // A text block has no nested text blocks to recurse into.
    return false;
  });

  return DecorationSet.create(doc, decorations);
}

export const TrackChangesExtension = Extension.create<TrackChangesOptions>({
  name: 'composeTrackChanges',

  addOptions() {
    return {
      initialEnabled: false,
      getBaseline: () => new Map<string, string>(),
    };
  },

  addProseMirrorPlugins() {
    const options = this.options;
    return [
      new Plugin<TrackChangesPluginState>({
        key: trackChangesPluginKey,
        state: {
          init: () => ({ enabled: options.initialEnabled }),
          apply(tr, value) {
            const meta = tr.getMeta(trackChangesPluginKey) as { enabled?: boolean } | undefined;
            if (meta && typeof meta.enabled === 'boolean') return { enabled: meta.enabled };
            return value;
          },
        },
        props: {
          decorations(state) {
            const pluginState = trackChangesPluginKey.getState(state);
            // G11 (FR-10, task 032 — UAT BUG-B): the toggle flips ONLY this DECORATION overlay (the
            // user's own free-typed edits). Imported/AI redlines are first-class schema MARKS
            // (insertion/deletion), which are document content and render independently of this plugin —
            // so returning DecorationSet.empty here hides ONLY the user's overlay, never an imported/AI
            // redline. buildTrackChangeDecorations additionally SKIPS any mark-bearing block, so the
            // overlay never touches an imported redline even when enabled. Locked by
            // TrackChangesExtension.test.ts "G11 (task 032)".
            if (!pluginState?.enabled) return DecorationSet.empty;
            return buildTrackChangeDecorations(state.doc, options.getBaseline());
          },
        },
      }),
    ];
  },
});

export default TrackChangesExtension;
