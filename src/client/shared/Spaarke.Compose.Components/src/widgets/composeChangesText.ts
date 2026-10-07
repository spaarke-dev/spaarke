/**
 * composeChangesText.ts — the `changesText` PRODUCER for `compose-summarize-word-changes` (UAT item 8).
 *
 * Project: spaarkeai-compose-r8 (UAT round-1 item 8).
 *
 * WHY THIS EXISTS. Every other piece of item 8 already shipped: the consumer type, the Action + input /
 * output schemas (`infra/dataverse/actions/compose-summarize-word-changes.action.json`), the server
 * operand binding (`ContextBinder`'s `changesText` → `OperandKind.ChangesText`), and the result renderer.
 * A repo-wide search found exactly one thing missing — **nothing ever produced the operand**. This module
 * is that producer, and nothing else: it is a pure function from the tracked-change data the Load response
 * already carries to the string the Action declares.
 *
 * THE BINDING RULE THIS MODULE ENFORCES (spaarkeai-compose-r2 FIX #5, restated in R8's UAT notes):
 * `compose-summarize-word-changes` was PULLED from the selection toolbar because, dispatched without real
 * change data, **the LLM fabricates a phantom "[Insertion]"**. So the load-bearing behaviour here is the
 * REFUSAL, not the formatting: {@link buildComposeChangesText} returns `null` when there is nothing real to
 * summarize, and callers MUST treat `null` as "do not dispatch" rather than sending an empty operand. The
 * Action's input schema declares `changesText` as `required`, which makes an empty string a contract
 * violation as well as a hallucination risk.
 *
 * WHAT COUNTS AS A CHANGE. The Action's own system prompt names four kinds — insertions, deletions,
 * comments, structural edits. Three of those are carried by the data the server already recovers via
 * `DocxAnnotationReader` and projects onto the Load response:
 *  - {@link ImportedRevision} → `insertion` / `deletion` (native `w:ins` / `w:del`, any authorship)
 *  - {@link ImportedComment} → `comment` (native `w:comment`)
 * `structural` has no recovered-annotation counterpart today, so this producer never emits one. That is a
 * deliberate omission rather than an oversight: inventing a structural-change line from data we do not
 * have is the same failure the refusal above exists to prevent. The model may still classify a described
 * change as structural from the text it is given.
 *
 * ORDERING is document order (`paragraphHint`), then kind, then id — a total order with no ties left to
 * array-arrival chance, so the same document produces the same operand on every run. That determinism is
 * what makes the summary reproducible and the eval case stable.
 *
 * PRIVACY (ADR-015 Tier 3). The returned string is document content by construction — it concatenates
 * `text` / `anchorText` / `commentText`. It is built in memory for a single dispatch and MUST NOT be
 * logged, put on an SSE frame, or published to the PaneEventBus. This mirrors the `selectionText`
 * contract the toolbar already follows.
 *
 * @see ./importedRevisions.ts — the sibling consumer of the same data (renders it as editor marks)
 * @see ../types/compose-contracts.ts — `ImportedRevision` / `ImportedComment` (mirrors of the server records)
 * @see infra/dataverse/inputschemas/compose-summarize-word-changes.input.schema.json — `maxLength: 200000`
 */

import type { ImportedComment, ImportedRevision } from '../types/compose-contracts';

/**
 * Cap for the produced operand, sourced from the Action's authored
 * `inputSchema.properties.changesText.maxLength` (200000) — not invented here. Mirrors how
 * `ComposeAiToolbar`'s `TOOLBAR_SELECTION_TEXT_CAP` is sourced from the compose-selection scope.
 */
export const COMPOSE_CHANGES_TEXT_CAP = 200000;

/** Appended when the change set is too large for the authored cap, so the model is told rather than silently given a truncated set. */
const TRUNCATION_NOTICE = '\n\n[Change list truncated — too many changes to include in full.]';

/** The tracked-change data a summary is built from — exactly what the Load response already carries. */
export interface ComposeChangeSources {
  /** Native `w:ins` / `w:del` revisions recovered on Load. */
  readonly revisions?: readonly ImportedRevision[];
  /** Native `w:comment` comments recovered on Load. */
  readonly comments?: readonly ImportedComment[];
}

/** One normalized change line, ordered and rendered deterministically. */
interface ChangeEntry {
  readonly kind: 'insertion' | 'deletion' | 'comment';
  readonly paragraphHint: number;
  readonly id: string;
  readonly author: string;
  readonly date: string;
  /** The change's own content — inserted/deleted text, or the comment body. */
  readonly body: string;
  /** The containing/anchored paragraph's settled text — context for locating the change. */
  readonly context: string;
}

/** Kind ordering within a paragraph — stable and independent of arrival order. */
const KIND_RANK: Record<ChangeEntry['kind'], number> = { insertion: 0, deletion: 1, comment: 2 };

/**
 * An unlocated annotation carries `paragraphHint: -1`. Sorting on the raw value would float those to the
 * TOP, ahead of located changes, implying they come first in the document — which is precisely what "we
 * could not locate this" means we do not know. They sort LAST instead.
 */
function positionRank(paragraphHint: number): number {
  return paragraphHint < 0 ? Number.MAX_SAFE_INTEGER : paragraphHint;
}

/** Collapses whitespace so a multi-line OOXML run renders as one readable line. */
function flatten(text: string): string {
  return text.replace(/\s+/g, ' ').trim();
}

/**
 * A change whose own body is empty carries nothing to summarize — it would render as `Inserted: ""`, which
 * is an invitation to invent content. Dropped at normalization so it cannot reach the model, and so an
 * all-empty set correctly produces a refusal rather than a list of blanks.
 */
function hasSubstance(entry: ChangeEntry): boolean {
  return entry.body.length > 0;
}

function normalize(sources: ComposeChangeSources): ChangeEntry[] {
  const entries: ChangeEntry[] = [];

  for (const r of sources.revisions ?? []) {
    entries.push({
      kind: r.kind === 'deletion' ? 'deletion' : 'insertion',
      paragraphHint: r.paragraphHint,
      id: r.id ?? '',
      author: r.author ?? '',
      date: r.date ?? '',
      body: flatten(r.text ?? ''),
      context: flatten(r.anchorText ?? ''),
    });
  }

  for (const c of sources.comments ?? []) {
    entries.push({
      kind: 'comment',
      paragraphHint: c.paragraphHint,
      id: c.id ?? '',
      author: c.author ?? '',
      date: c.date ?? '',
      body: flatten(c.commentText ?? ''),
      context: flatten(c.anchorText ?? ''),
    });
  }

  return entries.filter(hasSubstance).sort((a, b) => {
    const byPosition = positionRank(a.paragraphHint) - positionRank(b.paragraphHint);
    if (byPosition !== 0) return byPosition;
    const byKind = KIND_RANK[a.kind] - KIND_RANK[b.kind];
    if (byKind !== 0) return byKind;
    return a.id.localeCompare(b.id);
  });
}

/** `para 12` / `an unlocated paragraph` — a location pointer the model can turn into "Section 7.2". */
function locationOf(entry: ChangeEntry): string {
  return entry.paragraphHint < 0 ? 'an unlocated paragraph' : `paragraph ${entry.paragraphHint + 1}`;
}

/** `A. Reviewer on 2026-09-01` — attribution, omitted entirely rather than rendered as an empty label. */
function attributionOf(entry: ChangeEntry): string {
  const author = entry.author.trim();
  const day = entry.date.slice(0, 10);
  if (author && day) return `${author} on ${day}`;
  if (author) return author;
  return day || 'an unattributed reviewer';
}

const VERB: Record<ChangeEntry['kind'], string> = {
  insertion: 'Inserted',
  deletion: 'Deleted',
  comment: 'Commented',
};

function renderEntry(entry: ChangeEntry, index: number): string {
  const lines = [
    `[${index + 1}] ${entry.kind.toUpperCase()} in ${locationOf(entry)}, by ${attributionOf(entry)}`,
    `    ${VERB[entry.kind]}: "${entry.body}"`,
  ];
  // The anchor text is the model's only means of naming WHERE a change is ("the indemnification clause"),
  // so it is included — but never when it merely repeats the body, which happens when a comment anchors
  // exactly the text it is about and would otherwise print the same string twice.
  if (entry.context && entry.context !== entry.body) {
    lines.push(`    In context: "${entry.context}"`);
  }
  return lines.join('\n');
}

/** Human-readable count preamble — gives the model the total so it cannot under- or over-report the scale. */
function renderHeader(entries: readonly ChangeEntry[]): string {
  const authors = new Set(entries.map(e => e.author.trim()).filter(a => a.length > 0));
  const changeWord = entries.length === 1 ? 'change' : 'changes';
  const reviewerNote = authors.size === 1 ? ' by 1 reviewer' : authors.size > 1 ? ` by ${authors.size} reviewers` : '';
  return `Tracked changes returned from Word: ${entries.length} ${changeWord}${reviewerNote}.`;
}

/**
 * Builds the `changesText` operand for `compose-summarize-word-changes`.
 *
 * @returns the operand string, or **`null` when there is no real change data** — the caller MUST NOT
 * dispatch on `null`. An empty operand is what makes the model fabricate a phantom "[Insertion]", and the
 * Action declares `changesText` as required, so refusing is both the safe and the contract-correct
 * outcome. `null` is returned when there are no revisions and no comments, and also when every supplied
 * annotation has an empty body (a set of blanks is not change data).
 */
export function buildComposeChangesText(sources: ComposeChangeSources): string | null {
  const entries = normalize(sources);
  if (entries.length === 0) return null;

  const header = renderHeader(entries);
  const rendered: string[] = [header];
  let length = header.length;
  let truncated = false;

  for (let i = 0; i < entries.length; i += 1) {
    const block = renderEntry(entries[i], i);
    // +2 for the blank line separating entries; the notice must also still fit, so its length is
    // reserved up front rather than discovered after the cap has already been exceeded.
    if (length + block.length + 2 + TRUNCATION_NOTICE.length > COMPOSE_CHANGES_TEXT_CAP) {
      truncated = true;
      break;
    }
    rendered.push(block);
    length += block.length + 2;
  }

  // The header alone is not change data. If the cap left room for no entries at all, refuse rather than
  // dispatch a preamble that promises changes the operand does not contain.
  if (rendered.length === 1) return null;

  return rendered.join('\n\n') + (truncated ? TRUNCATION_NOTICE : '');
}

/**
 * Convenience predicate for gating a trigger's enabled state — `true` exactly when
 * {@link buildComposeChangesText} would produce an operand. Kept as one implementation so a button can
 * never be enabled for a dispatch that will then refuse.
 */
export function hasComposeChangeData(sources: ComposeChangeSources): boolean {
  return buildComposeChangesText(sources) !== null;
}

export default buildComposeChangesText;
