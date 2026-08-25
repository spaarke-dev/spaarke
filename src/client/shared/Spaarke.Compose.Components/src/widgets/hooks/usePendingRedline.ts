/**
 * usePendingRedline — FR-16 pending track-change materialization from the session ledger
 * (spaarkeai-compose-r2 task 033).
 *
 * Turns a stored `compose`-disposition ledger entry (ADR-040) into a PENDING insertion/deletion
 * redline pair rendered with the FR-15 marks (task 031), carrying `{bindingId}@t{n}` provenance,
 * with ledger-aware accept/reject. This hook is the "materialize FROM stored state" half that the
 * marks (rendering primitives only) deliberately do NOT do.
 *
 * WHERE THIS SITS IN THE FLOW (grounded in the shipped code, not the POML's guessed contract):
 *  - ComposeWorkspace already fetches the compose-outputs read projection (task 016 HOOK #1),
 *    picks the CURRENT (highest-turn) `compose` output — which is how supersession/refresh-
 *    durability resolve — and calls the editor handle to materialize it (render-follows-store).
 *  - The POML referenced a `compose_edit_apply_request` PaneEventBus event; that event does NOT
 *    exist (spike-0 correction — ComposeEditor's JSDoc bans adding it). The real seam is Flow 5
 *    `compose_assistant_insert` + the additive `ledgerRef`, already wired in ComposeWorkspace.
 *    So this hook is driven imperatively by ComposeEditor's handle, not by a new bus event.
 *  - The POML said "reuse the FR-19 validator semantics" to resolve the target span. No client-
 *    side validator ships yet (FR-19 / task 020 is BFF-side), so the strict/first/all match +
 *    ambiguity semantics are implemented here (mirroring the adeu `match_mode` contract from
 *    Spike 2). Matching is tolerant of typographic divergence via a STRICTLY 1:1 character fold
 *    (curly quotes → straight, NBSP → space, en/em/figure dashes → hyphen) applied to BOTH the doc
 *    index and the target — this closes the common "Word stores smart quotes / NBSP / typographic
 *    dashes but the model straightens them in its echoed target_text" mismatch (round-3 UAT Test #4).
 *    Fuzzy/typo matching (edit-distance, whitespace collapse, ligature expansion) is still out
 *    (Phase-2 deferred, per Spike 2) — those folds are non-1:1 and would desync the position map.
 *
 * ACCEPT / REJECT are ledger-aware doc operations keyed by `ledgerRef` (they scan the document for
 * marks addressed by that key — never a raw selection). At the FR-16 level they commit/revert the
 * pending redline in document state; the true ledger-supersession WRITE for undo/replace is FR-17
 * / task 034, which builds on this hook.
 *
 * FR-15 formatted AI insertions (task 032, client-only per §6.5 Path B amendment): `buildInsertionHtml`
 * parses a lightweight, SANITIZED inline-markup subset (bold/italic/underline) out of `new_text` so an
 * AI-*inserted* suggestion that should be formatted actually renders formatted, instead of flattening
 * to plain text. See {@link sanitizeInlineMarkup} for the allow-list + security rationale. This is a
 * CLIENT-render-only enrichment — no server `ComposeDraftPayload` change (the compose ledger payload
 * ships opaque end-to-end per `docs/architecture/COMPOSE-REDLINE-DERIVED-VIEWS.md`).
 *
 * @see ./ ../marks/InsertionMark.ts · ../marks/DeletionMark.ts (task 031 rendering primitives)
 * @see ../ComposeEditor.tsx (imperative handle: materializePendingRedline)
 * @see projects/spaarkeai-compose-r2/notes/HANDOFF-core-r2-A0-contract-requirements.md §1
 */
import * as React from 'react';
import type { Editor } from '@tiptap/core';
import type { Mapping } from '@tiptap/pm/transform';
import type { ComposeDraftPayload, ComposeDraftProvenance } from '../ComposeEditor';
// Task 051 (FR-C01/C02/C03) — the deterministic anchor branch. `collectBlocks` is the SAME paraId→live-span
// primitive `applyImportedCommentAnchors`/`applyImportedRevisions` already use (no second paraId walk).
// Task 055 — the paraId-vs-citation PRECEDENCE moved to `resolveAnchorParaIds`, shared with
// `ComposeEditor.placeAdvisoryComments` and `ComposeWorkspace.registerAiReviewComments` so all three
// consumers of a deterministic anchor cannot drift apart. This module keeps only its SPAN policy.
import { collectBlocks } from '../importedRevisions';
import { resolveAnchorParaIds } from '../composeAnchorResolution';
import type { ParaIdMapEntry } from '../../types/compose-contracts';
// Task 055 (FR-C03) — the prose-matching leg, MOVED (not changed, not retired) into its own module so
// it is a REPLACEABLE collaborator. That is what lets a test assert STRUCTURALLY that an anchored edit
// never reaches it — the client twin of the server's `ThrowIfTextSearched` `IComposeEditValidator`
// double. Re-exported below so every existing importer of `resolveTargetSpans` is unaffected.
import { resolveTargetSpans } from './redlineTextSearch';
import type { RedlineMatchMode, RedlineSpan, ResolveResult } from './redlineTextSearch';

export { resolveTargetSpans } from './redlineTextSearch';
export type { RedlineMatchMode, RedlineSpan, ResolveResult } from './redlineTextSearch';

/**
 * FR-13 (spaarkeai-compose-r3 task 031, amendment 2026-07-18 §6.5 Path B) — coarse, qualitative
 * confidence cue for a pending redline. NEVER a numeric/percentage score (design §6.2 anti-false-
 * precision; ADR-039). Rendered as a SECONDARY badge behind the rationale (the primary trust cue).
 */
export type ConfidenceBand = 'high' | 'medium' | 'low';

/**
 * FR-13 (client-derived) — deterministic confidence-band derivation, ported from the retired server
 * `ComposeDraftDisposition.DeriveConfidenceBand` (task 030, removed per §6.5 Path B / commit
 * `675d2d161`). Same both/one/neither truth table as the server version, over two grounding signals:
 *
 *  - `hasSources` — the payload cited grounding sources (`sources` non-empty).
 *  - `targetResolves` — the redline's target anchor actually resolves against the LIVE editor
 *    document RIGHT NOW (stronger than the retired server check, which could only see a match_mode
 *    "claim" — the server never had the live document; the client does, so it verifies for real).
 *
 * `high` when BOTH signals hold, `medium` when exactly one holds, `low` when neither holds. A pure
 * function over these two booleans — NOT a model self-report, and structurally incapable of reading
 * any `confidence_band` value a hostile/buggy model payload might smuggle in (this function never
 * sees the raw payload, only the two derived signals `usePendingRedline` computes from real grounding
 * evidence + a real live-document resolution check).
 */
export function deriveConfidenceBand(hasSources: boolean, targetResolves: boolean): ConfidenceBand {
  if (hasSources && targetResolves) return 'high';
  if (hasSources || targetResolves) return 'medium';
  return 'low';
}

/**
 * Status returned by {@link UsePendingRedlineResult.materialize}.
 *
 * `retracted` (FR-17 / task 034) is the distinguished outcome of materializing a SUPERSEDING
 * compose entry whose payload is empty (a retraction): the prior pending redline for that binding
 * is stripped and NOTHING new is rendered. It is deliberately distinct from a bare `noop` (nothing
 * to do) so undo/replace can observe that a retraction actually removed a prior suggestion.
 */
export type MaterializeStatus = 'applied' | 'ambiguous' | 'not_found' | 'already_present' | 'noop' | 'retracted';

/** A pending redline currently rendered in the document (one per stored compose output). */
export interface PendingRedline {
  /** Addressable ledger key `{bindingId}@t{n}` (provenance + accept/reject addressing). */
  ledgerRef: string;
  /** `sprk_playbookconsumer` Binding id that produced the suggestion. */
  bindingId: string;
  /** 1-based session turn the output was produced on. */
  turn: number;
  /** Optional model rationale (Tier 3 — shown in the accept/reject affordance, never logged). */
  rationale?: string;
  /** True when the suggestion replaced existing text (has a deletion half); false for a pure insertion. */
  hasDeletion: boolean;
  /**
   * FR-13 (client-derived, §6.5 Path B) — coarse confidence cue, secondary to the rationale.
   * See {@link deriveConfidenceBand}. Recomputed REACTIVELY as the document changes (a target the
   * user deletes outside accept/reject drops this redline out of the live-resolves signal).
   */
  confidenceBand: ConfidenceBand;
  /**
   * Whether the source payload cited grounding sources at materialize time (retained, alongside the
   * live doc, to support the reactive {@link confidenceBand} recompute — `sources` is a durable payload
   * field, so this half of the signal never changes after materialize; only `targetResolves` does).
   */
  hasSources: boolean;
}

/** Surfaced when a `target_text` cannot be resolved to a unique span — FR-19 "do not guess" rule. */
export interface PendingRedlineError {
  ledgerRef: string;
  kind: 'not_found' | 'ambiguous';
  /** Tier 3 — the target snippet; shown truncated in UI, never logged. */
  targetText: string;
  matchCount: number;
  /**
   * Item 1 (UAT round-4): for a whole-document change list (materializeMany), how many edits could
   * NOT be placed and how many were attempted. Lets the banner surface a CALM batched summary
   * ("N of M suggestions couldn't be placed automatically") instead of an alarming single-edit
   * message when a table-heavy / cross-extractor document leaves several exact-but-cross-cell targets
   * unplaceable. Absent (undefined) for the single-materialize path, which keeps its one-edit copy.
   */
  failedCount?: number;
  totalCount?: number;
}

export interface UsePendingRedlineResult {
  /** Pending redlines currently rendered (drives the accept/reject affordances). */
  pending: PendingRedline[];
  /** Last unresolved-target error (ambiguous / not-found), or null. */
  error: PendingRedlineError | null;
  /**
   * Materialize a stored compose output as a pending redline. Idempotent per `ledgerRef`; a newer
   * output for the same binding supersedes (removes) the prior one's marks (FR-17 alignment).
   */
  materialize: (payload: ComposeDraftPayload, provenance: ComposeDraftProvenance) => MaterializeStatus;
  /**
   * DEF-11 whole-document revision: materialize a CHANGE LIST (many `{target_text,new_text}` edits)
   * from ONE stored compose output as a MULTI-change redline. Each edit `i` renders as its own
   * insertion/deletion pair addressed by the sub-key `{baseLedgerRef}#{i}` (so per-change on-click
   * accept/reject stays granular), and each becomes its own {@link PendingRedline}. A newer
   * whole-doc output for the same binding (different base) supersedes the prior set. Unresolved
   * targets (FR-19 "do not guess") are skipped and surfaced via {@link error}; the rest still apply.
   * Returns one {@link MaterializeStatus} per edit (index-aligned).
   */
  materializeMany: (edits: ComposeDraftPayload[], baseProvenance: ComposeDraftProvenance) => MaterializeStatus[];
  /**
   * Commit the redline: remove the struck original(s), keep the inserted alternative as normal text.
   * A BASE key (`{bindingId}@t{n}`, no `#`) commits EVERY sub-change of a whole-doc edit (Accept-all);
   * an exact sub-key (`…#{i}`) commits just that one change (per-change on-click).
   */
  accept: (ledgerRef: string) => void;
  /**
   * Revert the redline: remove the inserted alternative, restore the struck original(s) to normal
   * text. A BASE key reverts EVERY sub-change (Reject-all); an exact sub-key reverts just that one.
   */
  reject: (ledgerRef: string) => void;
  /** Clear the current unresolved-target error banner. */
  clearError: () => void;
}

const INSERTION = 'insertion';
const DELETION = 'deletion';

/** Only these three are honored; anything else (incl. undefined) falls back to strict. */
function normalizeMatchMode(mode: string | undefined): RedlineMatchMode {
  return mode === 'first' || mode === 'all' || mode === 'strict' ? mode : 'strict';
}

function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function escapeAttr(value: string): string {
  return escapeHtml(value).replace(/"/g, '&quot;');
}

/**
 * FR-C01/C02/C03 (spaarkeai-compose-r8 task 051) — the DETERMINISTIC anchor branch, and the client half
 * of the same contract `ComposeAnchorResolver`/`ComposeEditAnchorPass` enforce on the server. An edit that
 * names its target by `target_para_id` (captured at selection time, or returned by the model from the
 * enumerated closed set) or by `target_ref` (a legal citation) resolves through the paraId reference map
 * — the projection's coordinate system — and NEVER through `resolveTargetSpans`' text search.
 *
 * Returns `null` when the payload carries NO anchor at all, which is the caller's signal to use the
 * legacy text path. That is the only route back to text: an anchor that is present and does not resolve
 * is REFUSED, exactly like an unresolvable `target_text` (the UAT-21 "never silently mis-place" charter).
 * Falling back to a search here would defeat the anchor's entire purpose — it would re-introduce the
 * wrong-occurrence risk for precisely the edits that had already named their target exactly.
 *
 * Ordering matches the server: paraId first (it IS the address), then citation. Both present and
 * disagreeing is `ambiguous` — neither is preferred.
 *
 * Task 055: the PRECEDENCE itself now lives in {@link resolveAnchorParaIds}, shared with the advisory
 * comment path and the whole-document review-flag path so it cannot drift between the three. What
 * stays HERE is this path's own SPAN POLICY, which the other two do not share: an edit addresses
 * exactly ONE paragraph (a range citation is refused, never narrowed to its first clause), and the
 * named paragraph must be present in the LIVE document — a stronger check than the reference map,
 * because the map is the load-time projection and the document is what the user is looking at.
 *
 * Exported for direct unit testing.
 */
export function resolveAnchoredSpans(
  editor: Editor,
  anchor: { target_para_id?: string; target_ref?: string } | undefined,
  referenceMap: readonly ParaIdMapEntry[] | undefined
): ResolveResult | null {
  const resolution = resolveAnchorParaIds(
    { paraId: anchor?.target_para_id, ref: anchor?.target_ref },
    referenceMap
  );
  // No anchor at all — the ONLY route back to the text path.
  if (resolution.kind === 'none') return null;
  if (resolution.kind === 'not_found') return { ok: false, kind: 'not_found', matchCount: 0 };
  if (resolution.kind === 'ambiguous') return { ok: false, kind: 'ambiguous', matchCount: resolution.matchCount };
  // A single edit addresses ONE paragraph; a range ("Sections 4-7") is refused, not narrowed to the
  // first — picking one would be the silently-wrong-target failure this task removes.
  if (resolution.paraIds.length !== 1) {
    return { ok: false, kind: 'ambiguous', matchCount: resolution.paraIds.length };
  }

  /**
   * paraId → its LIVE span, via the same primitive imported comments/revisions anchor through.
   * `BlockInfo.from`/`to` are the block NODE's boundaries; the redline marks and the insertion point
   * both need the block's CONTENT range, which is one position inside each boundary. Using the node
   * boundaries directly would insert the replacement AFTER the paragraph instead of within it.
   */
  const target = resolution.paraIds[0];
  const block = collectBlocks(editor).find(b => b.paraId?.toUpperCase() === target.toUpperCase());
  // The named paragraph is not in the live document (deleted, or the edit was built against another
  // document state). Refuse — repairing it would be a guess.
  if (!block) return { ok: false, kind: 'not_found', matchCount: 0 };

  return { ok: true, spans: [{ from: block.from + 1, to: block.to - 1 }] };
}

/**
 * What to call the thing that couldn't be placed, in the user-facing banner. An anchored edit has no
 * `target_text` to quote, so quoting an empty string would make the banner say nothing was targeted when
 * something very specific was.
 */
function unplaceableLabel(
  payload: { target_text?: string; target_ref?: string; target_para_id?: string } | undefined
): string {
  const text = payload?.target_text ?? '';
  if (text.length > 0) return text;
  if (payload?.target_ref) return payload.target_ref;
  if (payload?.target_para_id) return `paragraph ${payload.target_para_id}`;
  return '';
}

/**
 * Collect the document ranges carrying `markName` addressed by `ledgerRef`. Adjacent text nodes may
 * split a logical span into several ranges; callers apply deletions high→low to keep positions valid.
 * Exported for direct unit testing.
 */
export function collectMarkedRanges(
  editor: Editor,
  markName: typeof INSERTION | typeof DELETION,
  ledgerRef: string
): RedlineSpan[] {
  const ranges: RedlineSpan[] = [];
  editor.state.doc.descendants((node, pos) => {
    if (node.isText && node.marks.some(m => m.type.name === markName && m.attrs.ledgerRef === ledgerRef)) {
      ranges.push({ from: pos, to: pos + node.nodeSize });
    }
    return true;
  });
  return ranges;
}

/** True when the document already carries any mark (insertion or deletion) for this `ledgerRef`. */
function isPresent(editor: Editor, ledgerRef: string): boolean {
  return (
    collectMarkedRanges(editor, INSERTION, ledgerRef).length > 0 ||
    collectMarkedRanges(editor, DELETION, ledgerRef).length > 0
  );
}

/**
 * DEF-11: true when a mark's `ledgerRef` IS `target` exactly, or is a whole-doc sub-change
 * `{target}#{i}` of it. Lets accept/reject/supersede address a whole-doc edit by its BASE key
 * (all sub-changes) while an exact sub-key still addresses a single change.
 */
function ledgerRefMatches(markRef: unknown, target: string): boolean {
  return typeof markRef === 'string' && (markRef === target || markRef.startsWith(`${target}#`));
}

/**
 * Collect ranges carrying `markName` whose `ledgerRef` MATCHES `target` (exact, or any `{target}#{i}`
 * sub-change). The whole-doc-aware sibling of {@link collectMarkedRanges}.
 */
function collectMatchingRanges(
  editor: Editor,
  markName: typeof INSERTION | typeof DELETION,
  target: string
): RedlineSpan[] {
  const ranges: RedlineSpan[] = [];
  editor.state.doc.descendants((node, pos) => {
    if (node.isText && node.marks.some(m => m.type.name === markName && ledgerRefMatches(m.attrs.ledgerRef, target))) {
      ranges.push({ from: pos, to: pos + node.nodeSize });
    }
    return true;
  });
  return ranges;
}

/**
 * FR-15 (task 032, client-only per §6.5 Path B amendment — see
 * `docs/architecture/COMPOSE-REDLINE-DERIVED-VIEWS.md`) — the whitelist of inline formatting tags an
 * AI-authored `new_text` may carry inline. Keys are the bare tag NAMES this sanitizer recognizes as
 * open/close tags (case-insensitive, NO attributes); values are the canonical tag emitted (`b`→`strong`,
 * `i`→`em` — StarterKit's Bold/Italic marks accept either spelling on parse, so canonicalizing keeps
 * the emitted fragment deterministic). `u` rides through as-is (the editor's `Underline` extension,
 * `@tiptap/extension-underline`, MIT — already LOCKED_EXTENSIONS in `ComposeEditor.tsx`).
 */
const ALLOWED_INLINE_TAGS: Readonly<Record<string, string>> = {
  strong: 'strong',
  b: 'strong',
  em: 'em',
  i: 'em',
  u: 'u',
};

/**
 * FR-15 sanitizing inline-markup parser (task 032). An AI-authored `new_text` may carry a lightweight
 * inline-markup subset — bare `<strong>`/`<b>`, `<em>`/`<i>`, `<u>` open/close tags, NO attributes — so
 * a suggestion that should render bold/italic/underline actually does once inserted, instead of being
 * flattened to plain text (the FR-15 gap this task closes).
 *
 * SECURITY: this is an ALLOW-list, not a deny-list. Every `<...>` sequence that is not an EXACT,
 * attribute-free match against {@link ALLOWED_INLINE_TAGS} — an unknown tag, `<script>`, `<a href="…">`
 * (the editor's `Link` extension IS loaded in `ComposeEditor.tsx`, so an unsanitized anchor would parse
 * into a REAL link mark with an attacker-controlled `href`), an allowed tag name carrying an attribute
 * (e.g. `<strong onclick="…">`), or a stray/mismatched closing tag — is treated as inert literal text
 * and HTML-escaped via {@link escapeHtml}. It can therefore NEVER reach the editor's HTML parser as
 * markup, regardless of how the surrounding characters are crafted (no tag-splitting / nesting trick
 * can smuggle a non-whitelisted element through, because only an exact whitelisted match is ever
 * emitted as a real tag). A plain string with no recognized tags round-trips byte-for-byte through
 * {@link escapeHtml} — identical output to the pre-032 behavior (backward compatible). Unclosed
 * allowed tags at the end of the string are auto-closed (reverse nesting order) so the emitted
 * fragment is always well-formed HTML.
 *
 * Deliberately a tiny hand-rolled whitelist, not a general HTML sanitizer/parser dependency — NFR-03
 * (MIT TipTap base only) disfavors a new dependency for a 5-tag allow-list this narrow.
 *
 * Exported for direct unit testing.
 */
export function sanitizeInlineMarkup(text: string): string {
  const TAG_RE = /<(\/?)([a-zA-Z][a-zA-Z0-9]*)\s*\/?>/g;
  let out = '';
  let lastIndex = 0;
  const openStack: string[] = [];
  let match: RegExpExecArray | null;

  while ((match = TAG_RE.exec(text)) !== null) {
    const [full, closing, rawName] = match;
    const canonical = ALLOWED_INLINE_TAGS[rawName.toLowerCase()];

    // Emit the text run before this tag, escaped.
    out += escapeHtml(text.slice(lastIndex, match.index));
    lastIndex = match.index + full.length;

    if (!canonical) {
      // Unknown tag, or a disallowed tag (script/a/img/on*-bearing/…) — inert literal text, never markup.
      out += escapeHtml(full);
      continue;
    }

    if (closing) {
      const stackIdx = openStack.lastIndexOf(canonical);
      if (stackIdx === -1) {
        // Stray closing tag with no matching open — literal text; never desyncs well-formedness.
        out += escapeHtml(full);
        continue;
      }
      // Close every tag opened after the matched one too, keeping the emitted fragment well-nested.
      while (openStack.length > stackIdx) out += `</${openStack.pop()}>`;
    } else {
      openStack.push(canonical);
      out += `<${canonical}>`;
    }
  }

  out += escapeHtml(text.slice(lastIndex));
  // Auto-close any tags left open at the end of the string.
  while (openStack.length > 0) out += `</${openStack.pop()}>`;
  return out;
}

/**
 * An insertion `<span>` that parses back to InsertionMark with provenance (task 031 parseHTML).
 * FR-15 (task 032): `newText` may carry the {@link sanitizeInlineMarkup} inline-formatting subset
 * (bold/italic/underline) so an AI-inserted redline renders formatted instead of flattened to plain
 * text — sanitized so no disallowed markup (script, anchors, event handlers, unknown tags) ever
 * reaches the editor. Exported for direct unit testing.
 */
export function buildInsertionHtml(newText: string, bindingId: string, ledgerRef: string): string {
  const body = sanitizeInlineMarkup(newText).replace(/\r?\n/g, '<br>');
  return (
    `<span data-compose-mark="${INSERTION}" ` +
    `data-binding="${escapeAttr(bindingId)}" ` +
    `data-ledger-ref="${escapeAttr(ledgerRef)}">${body}</span>`
  );
}

/**
 * The union span `[from,to]` covering every mark (both halves) of `ledgerRef` in the current doc,
 * or `null` if none are present. Used to decide whether a NEW draft's selection addresses the SAME
 * region as a prior pending redline (→ supersede) or a DIFFERENT one (→ accumulate).
 */
function redlineSpan(editor: Editor, ledgerRef: string): { from: number; to: number } | null {
  const ranges = [
    ...collectMarkedRanges(editor, INSERTION, ledgerRef),
    ...collectMarkedRanges(editor, DELETION, ledgerRef),
  ];
  if (ranges.length === 0) return null;
  let from = Infinity;
  let to = -Infinity;
  for (const r of ranges) {
    if (r.from < from) from = r.from;
    if (r.to > to) to = r.to;
  }
  return { from, to };
}

/**
 * Remove every mark (both halves) for `ledgerRef` from the document without committing/reverting
 * content decisions — used on supersession. Returns the transaction's position {@link Mapping} so a
 * caller can remap positions it captured BEFORE the strip (e.g. the user's intended selection):
 * dropping the inserted text shifts every later position left, and the strip also relocates the
 * editor's live selection onto the stripped range (UAT 2026-07-14 #3).
 */
function stripRedlineMarks(editor: Editor, ledgerRef: string): Mapping {
  const insRanges = collectMarkedRanges(editor, INSERTION, ledgerRef).sort((a, b) => b.from - a.from);
  const delRanges = collectMarkedRanges(editor, DELETION, ledgerRef);
  const tr = editor.state.tr;
  const delMark = editor.state.schema.marks[DELETION];
  // Superseding a prior suggestion discards it: restore struck text to normal (remove the deletion
  // mark), then drop the inserted alternative text. High→low + mapping so positions stay valid.
  if (delMark) {
    for (const r of delRanges) tr.removeMark(r.from, r.to, delMark);
  }
  for (const r of insRanges) tr.delete(tr.mapping.map(r.from), tr.mapping.map(r.to));
  if (tr.steps.length > 0) editor.view.dispatch(tr);
  return tr.mapping;
}

/**
 * The hook. Operates over a single TipTap {@link Editor} instance (or null before mount). All
 * document mutation goes through TipTap chains, so `onUpdate` fires and the editor's dirty state
 * tracks automatically.
 */
/**
 * @param referenceMap Task 051 (FR-C02) — the document's `paraId` map from the Load response, the
 * coordinate system a `target_ref` citation resolves through. Optional: without it, citation-anchored
 * edits are refused (never text-searched) and everything else behaves exactly as before.
 */
export function usePendingRedline(
  editor: Editor | null,
  referenceMap?: readonly ParaIdMapEntry[]
): UsePendingRedlineResult {
  const [pending, setPending] = React.useState<PendingRedline[]>([]);
  const [error, setError] = React.useState<PendingRedlineError | null>(null);

  const clearError = React.useCallback(() => setError(null), []);

  const materialize = React.useCallback(
    (payload: ComposeDraftPayload, provenance: ComposeDraftProvenance): MaterializeStatus => {
      if (!editor) return 'noop';
      const { ledgerRef, bindingId, turn } = provenance;
      const newText = payload?.new_text ?? '';
      // FR-13 (client-derived, §6.5 Path B) — grounding signal 1: the payload cites sources. Durable
      // payload field; does not change after materialize (only the live-doc resolve signal does).
      const hasSources = Array.isArray(payload?.sources) && payload.sources.length > 0;

      // Idempotent: a re-signal for content already rendered (e.g. a duplicate Flow-5 event) is a no-op.
      if (isPresent(editor, ledgerRef)) {
        // Reconstruct `hasDeletion` from the ACTUAL marks in the doc — a redline reconstructed here
        // (e.g. after a refresh re-materialize hits already-present marks) carries a deletion half iff
        // the doc still holds a deletion mark for this ledgerRef. Hardcoding false mislabeled every
        // strike+replace redline as insertion-only.
        const reconstructedHasDeletion = collectMarkedRanges(editor, DELETION, ledgerRef).length > 0;
        const reconstructedHasSources = hasSources;
        setPending(prev =>
          prev.some(p => p.ledgerRef === ledgerRef)
            ? prev
            : [
                ...prev,
                {
                  ledgerRef,
                  bindingId,
                  turn,
                  rationale: payload?.rationale,
                  hasDeletion: reconstructedHasDeletion,
                  hasSources: reconstructedHasSources,
                  confidenceBand: deriveConfidenceBand(reconstructedHasSources, reconstructedHasDeletion),
                },
              ]
        );
        return 'already_present';
      }

      // Capture the user's INTENDED selection BEFORE supersession mutates the document. Superseding a
      // prior KEPT redline strips its marks (stripRedlineMarks), which both shifts later positions and
      // relocates the editor's live selection onto the stripped range — so the not-found fallback
      // below must NOT re-read the live selection (UAT 2026-07-14 #3). Remap the snapshot through each
      // strip so it keeps pointing at the text the user actually selected.
      let intendedFrom = editor.state.selection.from;
      let intendedTo = editor.state.selection.to;
      const hasSelection = intendedTo > intendedFrom;

      // Supersession (FR-17 alignment), RANGE-SCOPED (UAT 2026-07-14 #3, owner: accumulate):
      // a newer output for the same binding replaces a prior pending suggestion ONLY when it
      // addresses the SAME region. When the user redlines one section and then drafts a DIFFERENT
      // section, the prior redline MUST be preserved so independent redlines accumulate across the
      // document — ONLY a re-draft of the same/overlapping selection supersedes. With no live
      // selection (ledger replay / retraction / insertion-at-caret) we cannot range-scope, so keep
      // the full-binding supersession those paths rely on (FR-17 retraction, refresh-durability).
      const sameBinding = pending.filter(p => p.bindingId === bindingId && p.ledgerRef !== ledgerRef);
      const superseded = hasSelection
        ? sameBinding.filter(p => {
            const span = redlineSpan(editor, p.ledgerRef);
            return span !== null && span.from < intendedTo && intendedFrom < span.to; // interval overlap
          })
        : sameBinding;
      for (const prior of superseded) {
        const mapping = stripRedlineMarks(editor, prior.ledgerRef);
        intendedFrom = mapping.map(intendedFrom);
        intendedTo = mapping.map(intendedTo);
      }

      const targetText = payload?.target_text ?? '';
      // Task 051 — FIXED ordering, never the reverse: (1) DETERMINISTIC anchor, (2) legacy text search.
      // `anchored === null` means the payload named no anchor at all, which is the only path back to text.
      const anchored = resolveAnchoredSpans(editor, payload, referenceMap);
      const hasTarget = anchored !== null || targetText.length > 0;
      let hasDeletion = false;

      if (hasTarget) {
        const matchMode = normalizeMatchMode(payload?.match_mode);
        const resolved = anchored ?? resolveTargetSpans(editor, targetText, matchMode);
        let spans: RedlineSpan[];
        if (resolved.ok) {
          spans = resolved.spans;
        } else {
          // UAT-21 (2026-08-18, owner: "highest trust priority") — DO NOT fall back to the user's
          // live selection when the target text can't be located. The prior Round-3 UAT Test #4
          // fallback anchored the redline on whatever range happened to be selected and returned
          // `applied`, but that selection can be STALE (the caret moved during the round-trip) or
          // wholly IRRELEVANT (a ledger/refresh replay where the user never selected anything for
          // THIS edit) — so it could strike-and-replace the WRONG text and present it as success: a
          // SILENT mis-placement of a legal edit. Under the R7 honest/safe charter ("never lie — no
          // silent mis-placement, no false 'applied'") every unresolved target — `not_found` AND
          // `ambiguous` — now surfaces via the banner and renders NOTHING for this entry. The user
          // re-selects the exact passage and re-runs, or edits the clause manually. This is the
          // "propose, don't auto-place" resolution (UAT-24) applied at the placement boundary; it
          // deliberately reverses the Round-3 fallback (an honest dead-end beats a silent wrong edit).
          setError({
            ledgerRef,
            kind: resolved.kind,
            targetText: unplaceableLabel(payload),
            matchCount: resolved.matchCount,
          });
          if (superseded.length > 0) {
            setPending(prev => prev.filter(p => !superseded.some(s => s.ledgerRef === p.ledgerRef)));
          }
          return resolved.kind;
        }

        const insertionHtml = buildInsertionHtml(newText, bindingId, ledgerRef);
        let chain = editor.chain();
        // Apply the deletion half to every resolved span first (positions are pre-insert; setMark
        // does not shift them).
        for (const span of spans) {
          chain = chain
            .setTextSelection({ from: span.from, to: span.to })
            .setMark(DELETION, { binding: bindingId, ledgerRef });
        }
        // Insert the alternative after EACH deleted span so accept yields a true replace (in `all`
        // mode every occurrence becomes the alternative, not just the first). High→low so an earlier
        // span's position is not shifted by a later insert.
        const insertPoints = spans.map(s => s.to).sort((a, b) => b - a);
        for (const at of insertPoints) {
          chain = chain.insertContentAt(at, insertionHtml);
        }
        chain.run();
        hasDeletion = true;
      } else {
        // Insertion-style draft (no target): insert the alternative at the cursor as a pending insertion.
        if (newText.length === 0) {
          if (superseded.length > 0) {
            // FR-17 RETRACTION (task 034): an empty superseding compose entry re-materialized from
            // the ledger. The prior redline's marks were already stripped above (stripRedlineMarks);
            // drop it from pending, clear any stale unresolved-target banner, and report `retracted`
            // so undo/replace can observe the removal (this is a ledger supersession, NOT a DOM undo).
            setPending(prev => prev.filter(p => !superseded.some(s => s.ledgerRef === p.ledgerRef)));
            setError(null);
            return 'retracted';
          }
          return 'noop';
        }
        const insertionHtml = buildInsertionHtml(newText, bindingId, ledgerRef);
        // Insert at the caret captured BEFORE supersession (remapped), not the post-strip live
        // selection which a strip may have relocated (UAT 2026-07-14 #3).
        const at = intendedTo;
        editor.chain().insertContentAt(at, insertionHtml).run();
      }

      setError(null);
      setPending(prev => {
        const kept = prev.filter(p => !superseded.some(s => s.ledgerRef === p.ledgerRef));
        return [
          ...kept,
          {
            ledgerRef,
            bindingId,
            turn,
            rationale: payload?.rationale,
            hasDeletion,
            hasSources,
            confidenceBand: deriveConfidenceBand(hasSources, hasDeletion),
          },
        ];
      });
      return 'applied';
    },
    [editor, pending, referenceMap]
  );

  const materializeMany = React.useCallback(
    (edits: ComposeDraftPayload[], baseProvenance: ComposeDraftProvenance): MaterializeStatus[] => {
      if (!editor) return edits.map(() => 'noop');
      const { ledgerRef: baseRef, bindingId, turn } = baseProvenance;

      // Idempotent: this whole-doc set already rendered (refresh / duplicate Flow-5 signal) → no-op.
      if (pending.some(p => ledgerRefMatches(p.ledgerRef, baseRef)) || isPresent(editor, baseRef)) {
        return edits.map(() => 'already_present');
      }

      // Supersession (FR-17 alignment): a newer whole-doc output for this binding (a DIFFERENT base)
      // replaces any prior pending suggestion(s) for that binding — the superseded marks MUST go.
      const superseded = pending.filter(p => p.bindingId === bindingId && !ledgerRefMatches(p.ledgerRef, baseRef));
      for (const prior of superseded) stripRedlineMarks(editor, prior.ledgerRef);

      const statuses: MaterializeStatus[] = [];
      const newPending: PendingRedline[] = [];
      // Item 1 (UAT round-4): collect unplaceable target-bearing edits so the banner can surface a calm
      // batched summary (N of M) rather than a single alarming per-edit message. An array (vs a closure-
      // assigned `let`) keeps the type narrowable after the forEach.
      const failures: PendingRedlineError[] = [];
      let targetedCount = 0;

      edits.forEach((payload, i) => {
        // Sub-key per change so per-change on-click accept/reject stays granular (DEF-12), while the
        // BASE key addresses the whole set (Accept-all/Reject-all).
        const subRef = `${baseRef}#${i}`;
        const newText = payload?.new_text ?? '';
        const targetText = payload?.target_text ?? '';
        // FR-13 (client-derived, §6.5 Path B) — per-edit grounding signal 1 (each change-list entry
        // is its own PendingRedline with its own confidence band).
        const editHasSources = Array.isArray(payload?.sources) && payload.sources.length > 0;

        // Task 051 — same fixed ordering as the single-edit path: deterministic anchor, then legacy text.
        const anchored = resolveAnchoredSpans(editor, payload, referenceMap);

        if (anchored === null && targetText.length === 0) {
          // Insertion-style edit (no target) — insert at the current caret as a pending insertion.
          if (newText.length === 0) {
            statuses.push('noop');
            return;
          }
          const insertionHtml = buildInsertionHtml(newText, bindingId, subRef);
          editor.chain().insertContentAt(editor.state.selection.to, insertionHtml).run();
          newPending.push({
            ledgerRef: subRef,
            bindingId,
            turn,
            rationale: payload?.rationale,
            hasDeletion: false,
            hasSources: editHasSources,
            confidenceBand: deriveConfidenceBand(editHasSources, false),
          });
          statuses.push('applied');
          return;
        }

        // resolveTargetSpans re-reads the CURRENT doc, so it already accounts for earlier edits'
        // struck (still-present) originals + appended insertions — positions stay valid per edit.
        targetedCount += 1;
        const matchMode = normalizeMatchMode(payload?.match_mode);
        const resolved = anchored ?? resolveTargetSpans(editor, targetText, matchMode);
        if (!resolved.ok) {
          // FR-19 "do not guess": skip this one, record the failure, keep going.
          failures.push({
            ledgerRef: subRef,
            kind: resolved.kind,
            targetText: unplaceableLabel(payload),
            matchCount: resolved.matchCount,
          });
          statuses.push(resolved.kind);
          return;
        }

        const insertionHtml = buildInsertionHtml(newText, bindingId, subRef);
        let chain = editor.chain();
        for (const span of resolved.spans) {
          chain = chain
            .setTextSelection({ from: span.from, to: span.to })
            .setMark(DELETION, { binding: bindingId, ledgerRef: subRef });
        }
        const insertPoints = resolved.spans.map(s => s.to).sort((a, b) => b - a);
        for (const at of insertPoints) chain = chain.insertContentAt(at, insertionHtml);
        chain.run();
        newPending.push({
          ledgerRef: subRef,
          bindingId,
          turn,
          rationale: payload?.rationale,
          hasDeletion: true,
          hasSources: editHasSources,
          confidenceBand: deriveConfidenceBand(editHasSources, true),
        });
        statuses.push('applied');
      });

      // Item 1: surface the FIRST failure with batched counts so the banner can say "N of M couldn't be
      // placed" calmly (array access → cleanly narrowable type, unlike a closure-assigned `let`).
      const firstFailure = failures[0];
      setError(firstFailure ? { ...firstFailure, failedCount: failures.length, totalCount: targetedCount } : null);
      setPending(prev => {
        const kept = prev.filter(p => !superseded.some(s => s.ledgerRef === p.ledgerRef));
        return [...kept, ...newPending];
      });
      return statuses;
    },
    [editor, pending, referenceMap]
  );

  const accept = React.useCallback(
    (ledgerRef: string) => {
      if (!editor) return;
      // Base key ⇒ Accept-all (every `{ledgerRef}#{i}` sub-change); exact sub-key ⇒ just that one.
      const insRanges = collectMatchingRanges(editor, INSERTION, ledgerRef);
      const delRanges = collectMatchingRanges(editor, DELETION, ledgerRef).sort((a, b) => b.from - a.from);
      let chain = editor.chain();
      // Commit: keep the inserted alternative (unset its mark → normal text); remove struck originals.
      for (const r of insRanges) chain = chain.setTextSelection(r).unsetMark(INSERTION);
      for (const r of delRanges) chain = chain.deleteRange(r);
      chain.run();
      setPending(prev => prev.filter(p => !ledgerRefMatches(p.ledgerRef, ledgerRef)));
    },
    [editor]
  );

  const reject = React.useCallback(
    (ledgerRef: string) => {
      if (!editor) return;
      // Base key ⇒ Reject-all; exact sub-key ⇒ just that one.
      const insRanges = collectMatchingRanges(editor, INSERTION, ledgerRef).sort((a, b) => b.from - a.from);
      const delRanges = collectMatchingRanges(editor, DELETION, ledgerRef);
      let chain = editor.chain();
      // Revert: restore struck originals (unset deletion mark → normal text); remove the insertion.
      for (const r of delRanges) chain = chain.setTextSelection(r).unsetMark(DELETION);
      for (const r of insRanges) chain = chain.deleteRange(r);
      chain.run();
      setPending(prev => prev.filter(p => !ledgerRefMatches(p.ledgerRef, ledgerRef)));
    },
    [editor]
  );

  // FR-13 (client-derived confidence band, §6.5 Path B) — REACTIVE recompute. `deriveConfidenceBand`'s
  // second signal (does the target still resolve?) is a live-document fact, so it can go stale the
  // instant the user edits the doc OUTSIDE accept/reject (e.g. manually deleting a redline's struck
  // original). Re-derive every pending item's band on every editor transaction against the CURRENT
  // doc state; `pendingRef` lets the listener stay registered once per editor instance instead of
  // re-subscribing on every `pending` change (mirrors the FIX #9 scroll-measure effect's `editor.on`
  // pattern above). A no-op update (no band actually changed) skips the `setPending` call.
  const pendingRef = React.useRef(pending);
  pendingRef.current = pending;

  React.useEffect(() => {
    if (!editor) return undefined;
    const recomputeConfidenceBands = (): void => {
      const current = pendingRef.current;
      if (current.length === 0) return;
      let changed = false;
      const next = current.map(p => {
        const targetResolves = collectMarkedRanges(editor, DELETION, p.ledgerRef).length > 0;
        const band = deriveConfidenceBand(p.hasSources, targetResolves);
        if (band === p.confidenceBand) return p;
        changed = true;
        return { ...p, confidenceBand: band };
      });
      if (changed) setPending(next);
    };
    editor.on('update', recomputeConfidenceBands);
    return () => {
      editor.off('update', recomputeConfidenceBands);
    };
  }, [editor]);

  // Memoize the result object so consumers (ComposeEditor's useImperativeHandle keys on `redline`)
  // get a STABLE reference across renders that don't change the underlying values — without this the
  // fresh object literal rebuilt the editor handle every render. Identity changes only when `pending`
  // / `error` (state) or a callback identity changes, which is exactly when the handle should refresh.
  return React.useMemo(
    () => ({ pending, error, materialize, materializeMany, accept, reject, clearError }),
    [pending, error, materialize, materializeMany, accept, reject, clearError]
  );
}

export default usePendingRedline;
