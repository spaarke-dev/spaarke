/**
 * composeAnchoredDocumentText — the whole-document CLOSED SET, as one artifact (task 054, FR-C03).
 *
 * WHAT THIS SOLVES. The whole-document revision pass (`compose-revise-document`) has no selection, so
 * there is no single `w14:paraId` to hand the model the way the selection-scoped Actions do (task 051).
 * It needs the document's paragraphs as an enumerated CLOSED SET, and it needs the model to answer with
 * an id drawn from that set instead of quoting prose back.
 *
 * WHY THE SET IS NOT A SEPARATE LIST. If the ids arrive in a list BESIDE an unannotated document, the
 * model still has to work out which list entry corresponds to the paragraph it just read — by number, by
 * heading, or by quoting its opening words. That is prose matching MOVED, not removed, and it
 * reintroduces the lossy generation step this project exists to delete. Ids must appear where the content
 * is read, so that naming one is a COPY rather than a GENERATION. So the set IS the document text, with
 * every paragraph prefixed by its own identifier:
 *
 *     [A1B2C3D4] 1. Definitions
 *     [B2C3D4E5] "Confidential Information" means ...
 *
 * It is also ~10x cheaper than the alternative: the prefix costs ~12 characters per paragraph (+5-6% on
 * the text), whereas a parallel reference-map list runs 100-150 bytes per entry and would cross
 * `ContextBinder`'s 32 KB companion cap on a large document — where an oversize companion is SKIPPED and
 * logged, silently presenting an incomplete set to the model as if it were complete.
 *
 * WHY THE EDITOR PRODUCES IT, NOT THE SERVER. Only the live editor holds the COMPLETE current set. The
 * server's `ChatSession.ReferenceMap` is a Load-time snapshot: it omits paragraphs the user has typed
 * since, and it carries no text to annotate with (`ParaReferenceMapEntry` is ids + numbering only), so
 * building this server-side would mean re-projecting the document — re-deriving information already in
 * hand at capture time, which project invariant 7 forbids. It also makes the text the model quotes FROM
 * and the text the edit is placed INTO the same text; today they are not (the model sees a RAG extract,
 * placement happens against the editor).
 *
 * NO SECOND ENUMERATION (project invariant 3). This walks {@link collectBlocks} — the same block walk
 * `usePendingRedline` places against and the same one the server's `<w:p>` count mirrors. The set the
 * model is given and the set placement resolves against are therefore the same set, by construction.
 *
 * NEVER TRUNCATES. A truncated "closed" set is a contradiction: the model would be refused on ids that
 * genuinely exist. The file-operand path this replaces caps nothing either, so honesty here also avoids
 * introducing a ceiling that did not previously exist. A document with no stamped ids yields an EMPTY
 * closed set, and the caller is expected to omit the annotated text entirely rather than send an
 * id-free document that claims to carry one.
 *
 * @see ./importedRevisions.ts — `collectBlocks`, the single block walk
 * @see ./hooks/usePendingRedline.ts — `resolveAnchoredSpans`, which places what the model returns
 * @see projects/spaarkeai-compose-r8/notes/054-closed-set-supply-decision.md
 */

import type { Editor } from '@tiptap/react';
import { collectBlocks } from './importedRevisions';

/** The annotated document text plus the closed set of ids it presents. */
export interface AnchoredDocumentText {
  /** The document text with each id-bearing paragraph prefixed `[PARAID] `, in document order. */
  readonly text: string;
  /**
   * The paraIds present in {@link text}, in document order — the CLOSED SET. An id the model returns
   * that is not in here is refused rather than searched for (UAT-21).
   */
  readonly paraIds: readonly string[];
  /** Total blocks walked, including any that carry no id. */
  readonly totalBlocks: number;
}

/** The prefix a paragraph's id is presented in. Kept here so the prompt wording and the parser agree. */
const ID_PREFIX_OPEN = '[';
const ID_PREFIX_CLOSE = '] ';

/**
 * Build the annotated whole-document text and its closed set from the LIVE editor.
 *
 * Blocks with no stamped `paraId` are still emitted, unprefixed: dropping them would hand the model a
 * document that silently disagrees with the one on screen, and an edit targeting the surrounding text
 * would then be positioned against prose the model never saw. They are simply not members of the closed
 * set, so the model falls back to `target_text` for them — the same treatment an anchorless selection
 * edit already gets.
 *
 * Empty paragraphs are kept for the same reason: completeness of the set outranks tidiness of the
 * prompt, and an empty paragraph is a legitimate insertion target.
 */
export function buildAnchoredDocumentText(editor: Editor): AnchoredDocumentText {
  const blocks = collectBlocks(editor);
  const paraIds: string[] = [];
  const lines: string[] = [];

  for (const block of blocks) {
    const paraId = block.paraId;
    if (paraId) {
      paraIds.push(paraId);
      lines.push(`${ID_PREFIX_OPEN}${paraId}${ID_PREFIX_CLOSE}${block.text}`);
    } else {
      lines.push(block.text);
    }
  }

  return { text: lines.join('\n'), paraIds, totalBlocks: blocks.length };
}
