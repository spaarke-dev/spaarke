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
 *  - Placement is by DETERMINISTIC ANCHOR (`target_para_id` / `target_ref`). Prose matching is NOT a
 *    placement channel for an AI edit (ADR-049 I-7). Task 052 completed that demotion: the four
 *    compose EDIT Actions no longer ask the model for `target_text` or `match_mode` at all, so the
 *    text leg is reachable ONLY by a LEGACY ledger entry produced before that catalog change.
 *    Task 053 BOUNDED that leg: it now lives in `./anchorlessReplayFallback.ts`, it cannot be called
 *    with an anchored payload (its input type has no public constructor), it has no `applied` outcome
 *    to return, and every placement it reaches goes through the user's explicit confirmation
 *    ({@link UsePendingRedlineResult.legacyProposal}). Prose can PROPOSE here; it can never PLACE.
 *
 * FR-C05 — the three deterministic outcomes an anchored edit can now produce (task 052):
 *  - SUB-PARAGRAPH EDIT → the redline covers only the words that changed, diffed LOCALLY inside the
 *    anchored paragraph (`./redlineLocalDiff.ts`). Never a document-scoped search under a new name.
 *  - STALE TARGET → the paragraph is not the text the suggestion was written against; NOTHING is
 *    placed and {@link UsePendingRedlineResult.staleTarget} asks "apply anyway?". The user's answer is
 *    made durable by the host's FR-17 supersession write, not by this hook (assessment §4.4 O-2/O-3).
 *  - DELETED TARGET → the anchored paragraph is gone from the live document; its own `target_deleted`
 *    outcome and its own copy, distinct from an unresolvable citation.
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
import type { Transaction } from '@tiptap/pm/state';
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
//
// Task 053: THIS FILE NO LONGER IMPORTS THE SEARCH AS A VALUE. The only value import left is the
// `RedlineSpan` shape; `resolveTargetSpans` survives here purely as a re-export for the module's
// existing importers (`ComposeEditor`, `useDocQaHighlight`, `hooks/index.ts` — annotation and
// decoration consumers, not placements). The hook's own route to prose now runs exclusively through
// `./anchorlessReplayFallback`, which cannot be handed an anchored edit and cannot return a placement.
import type { RedlineSpan } from './redlineTextSearch';
// Task 052 (FR-C05) — the two collaborators the three deterministic outcomes need. Both are pure and
// paragraph-scoped: `redlineLocalDiff` cannot address a position outside the paragraph it is handed,
// and `redlineProposalBaseline` only remembers text, it never locates anything.
import { narrowAnchoredSpan, readRangeText } from './redlineLocalDiff';
import { compareProposalBaseline, recordProposalBaseline } from './redlineProposalBaseline';
// Task 053 (FR-C06) — the BOUNDED confirmable fallback for replayed/legacy anchorless entries. Its
// input type has no public constructor (an anchored payload cannot be classified into it) and its
// outcome vocabulary has no `applied` member, so neither bound is a convention this file must keep.
//
// Task 053b — `classifyUnidentifiedTarget` is the SECOND anchorless classifier in that same module:
// an edit that WAS asked for an identifier and returned an explicit null. It is a pure classifier
// (nothing to resolve), so it adds no outcome type and therefore cannot add an `applied` path.
import {
  classifyAnchorlessReplay,
  classifyUnidentifiedTarget,
  resolveAnchorlessReplay,
} from './anchorlessReplayFallback';

export { resolveTargetSpans } from './redlineTextSearch';
export type { RedlineMatchMode, RedlineSpan, ResolveResult } from './redlineTextSearch';

/**
 * FR-13 (spaarkeai-compose-r3 task 031, amendment 2026-07-18 §6.5 Path B) — coarse, qualitative
 * confidence cue for a pending redline. NEVER a numeric/percentage score (design §6.2 anti-false-
 * precision; ADR-039). Rendered as a SECONDARY badge behind the rationale (the primary trust cue).
 */
export type ConfidenceBand = 'high' | 'medium' | 'low';

/**
 * FR-C05 residual (spaarkeai-compose-r8 task 052b) — WHERE this materialize came from, and therefore
 * whether the anchored paragraph as it reads right now can be trusted as the CAPTURE-TIME text.
 *
 *  - `'live'` — driven by the dispatch that produced the entry (the Flow-5
 *    `compose_assistant_insert` leg, emitted by `ConversationPane` the moment the compose output is
 *    written). The model wrote this proposal against the document as it reads now, so recording the
 *    paragraph is recording the capture-time text.
 *  - `'replay'` — a re-materialize from durable ledger state at an unknown later time (the reopen /
 *    refresh-durability pass). An arbitrary amount of editing may have happened in between, so the
 *    live paragraph proves nothing about what the model saw.
 *
 * WHY THIS IS THE LOAD-BEARING DISCRIMINATOR, not the store. Before 052b the hook inferred "this must
 * be the first materialize" from the ABSENCE of a recorded baseline — which is also what a different
 * tab, an evicted entry and a disabled store look like. On those paths it re-baselined against a
 * possibly-drifted paragraph and applied silently: the pre-052 overwrite, restored. Origin is a fact
 * about the CALL, so it stays true where a store cannot follow (another tab, another device, private
 * browsing).
 *
 * FAIL-CLOSED: omitting it means `'replay'` (see {@link ComposeDraftProvenance.origin}). A caller that
 * does not declare freshness cannot be granted it — the worst an omission can cost is a confirmation,
 * where the worst a wrong `'live'` can cost is the user's text.
 */
export type MaterializeOrigin = 'live' | 'replay';

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
export type MaterializeStatus =
  | 'applied'
  | 'ambiguous'
  | 'not_found'
  /**
   * FR-C05 (task 052) — the anchored paragraph is GONE from the live document. Distinct from
   * `not_found` (an anchor that never resolved: an unknown citation, a paraId outside the closed set)
   * because the user-facing answer is different: the text this suggestion referred to no longer exists.
   */
  | 'target_deleted'
  /**
   * FR-C05 (task 052) — the anchored paragraph resolved, but its text is not the text the suggestion
   * was written against. NOTHING is placed; {@link UsePendingRedlineResult.staleTarget} carries the
   * "apply anyway?" question. Silently overwriting here is the data loss this outcome exists to close.
   */
  | 'stale'
  /**
   * FR-C06 (task 053) — a REPLAYED/LEGACY anchorless suggestion was located by prose and is being
   * PROPOSED for the user's confirmation. NOTHING has been placed; {@link UsePendingRedlineResult.legacyProposal}
   * carries the question. This status can only be produced by the bounded fallback
   * (`./anchorlessReplayFallback.ts`), which has no `applied` outcome of its own — a prose match can
   * propose, it can never place.
   *
   * FR-C05/C06 residual (task 053b) — the SECOND producer of this status: an edit whose
   * `target_para_id` arrived explicitly NULL (the model was asked for an identifier and had none) and
   * which carries no prose either. It used to fall through to the insertion-at-cursor branch and
   * return `applied`, so a revised indemnity clause could land in the recitals and be reported as a
   * success. It is now proposed at the passage the USER selected, with an honest reason, and placed
   * only once they confirm — the owner's 2026-08-25 bar ("whatever ensures the document updates and
   * saves") met without a silent mis-placement.
   */
  | 'proposed'
  | 'already_present'
  | 'noop'
  | 'retracted';

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

/** Surfaced when an edit's target cannot be resolved to a unique span — FR-19 "do not guess" rule. */
export interface PendingRedlineError {
  ledgerRef: string;
  kind: 'not_found' | 'ambiguous' | 'target_deleted';
  /**
   * FR-C07 (task 053) — WHICH targeting channel failed, so the banner can say something TRUE.
   *
   *  - `'anchored'` — the suggestion named a `target_para_id` / `target_ref` and that anchor did not
   *    resolve. Nothing about the document's WORDING is involved: no text was compared, so copy that
   *    blames a wording difference would be a fabrication. This is the branch that used to render
   *    "its wording differs slightly from this document".
   *  - `'legacy-replay'` — a REPLAYED pre-anchor ledger entry (FR-C06) whose quoted prose could not be
   *    located. Here prose really was compared, and the honest answer is that the entry predates
   *    paragraph anchoring and should be re-run.
   *
   * Defaulted at every construction site rather than left optional-and-forgotten: the banner switches
   * on it, and an absent value would silently pick one of the two stories.
   */
  source: 'anchored' | 'legacy-replay';
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

/**
 * FR-C05 (task 052) — the "this clause changed since the suggestion — apply anyway?" question, raised
 * when an anchored edit's paragraph no longer reads the way it did when the model proposed the change.
 *
 * NOT an ADR-041 Gate (task-050 assessment §4.2: the Action has already run, there is no invocation to
 * suspend, and the trigger is a runtime document fact no catalog datum can declare). No
 * `PendingPlanManager`, no `SessionGate`, no `gateId`, no `BindingRisk`. What IS binding is O-2: the
 * user's ANSWER must be ledger-durable, which the host satisfies with the shipped FR-17 supersession
 * write — this hook only raises the question and applies/discards the placement.
 */
export interface PendingRedlineStaleTarget {
  /**
   * The ledger entry this question belongs to: `{bindingId}@t{n}` — the BASE key for a whole-document
   * batch. This is the key the host supersedes to make the answer durable (O-4 append-only, keyed by
   * the edit's ledger key).
   */
  ledgerRef: string;
  bindingId: string;
  /**
   * Task 052b — WHY the question is being asked, and therefore what the copy is allowed to claim.
   * Required, not optional, for the same reason {@link PendingRedlineError.source} is: the host
   * switches on it, and an absent value would silently pick one of two very different stories.
   *
   *  - `'changed'` — we HAVE the capture-time record and the clause does not match it. The document
   *    definitely drifted. This is task 052's original question, unchanged.
   *  - `'unverifiable'` — a REPLAYED suggestion for which no capture-time record exists anywhere this
   *    browser can see (a different tab's session, a different device, an evicted entry, a disabled
   *    store). We do NOT know that the clause changed, and the copy must not say we do — but we also
   *    cannot rule it out, and applying silently is exactly the pre-052 overwrite. So we ask.
   *
   * For a batch this is `'changed'` whenever ANY held-back suggestion is genuinely changed (the
   * stronger claim is then true of at least one), and the detail fields describe that same one.
   */
  reason: 'changed' | 'unverifiable';
  /** The `w14:paraId` of the clause that drifted (the FIRST one, when a batch has several). */
  paraId: string;
  /** Tier 3 — the clause as it reads NOW. Shown truncated; never logged. */
  currentText: string;
  /**
   * Tier 3 — the clause as it read when the suggestion was proposed. Shown truncated; never logged.
   *
   * `null` when the capture-time WORDING is not available: always for `reason: 'unverifiable'`, and
   * for `'changed'` when only the durable fingerprint survived (a different tab). A fingerprint
   * proves the change; it cannot reproduce the words, and this path does not invent them. The host
   * omits the "when suggested" line rather than rendering an empty quote.
   */
  proposedAgainst: string | null;
  /** How many suggestions in this entry are stale (1 for a single-edit draft). */
  staleCount: number;
  /** How many suggestions the entry carried in total. */
  totalCount: number;
}

/**
 * FR-C06 (spaarkeai-compose-r8 task 053) — the "we think this suggestion belongs here — is that right?"
 * question, raised for a REPLAYED/LEGACY suggestion that carries no deterministic anchor.
 *
 * WHEN THIS EXISTS AT ALL. Since task 052 the four compose EDIT Actions do not ask the model for
 * `target_text`, so no newly-produced edit can be anchorless. A payload with prose and no anchor is a
 * `compose`-disposition ledger entry written BEFORE that catalog change and replayed afterwards — by
 * the reopen pass, a refresh, or an undo/try-another re-render of an older turn. Compose sessions are
 * durable (ADR-040; 90-day default, indefinite when filed), so that population is real, bounded, and
 * shrinking. It is the ONLY producer of this question.
 *
 * WHY A PROPOSAL AND NOT A PLACEMENT. The prose match tells us where that wording occurs today; it
 * does not tell us that the paragraph containing it is the paragraph the model was looking at. Under
 * the UAT-21 charter ("never silently mis-place, never a false 'applied'") the honest move is to show
 * the user exactly what would be struck and let them decide. NOTHING is placed while this is non-null.
 *
 * NOT an ADR-041 Gate, for the same reason FR-C05's question is not (task-050 assessment §4.2 / O-1):
 * the Action has already run, there is no invocation to suspend, and the trigger is a runtime document
 * fact no catalog datum can declare. No `PendingPlanManager`, no `SessionGate`, no `gateId`, no
 * `BindingRisk`. What IS binding is O-2 — the ANSWER must be ledger-durable — which the host satisfies
 * with the shipped FR-17 supersession write, exactly as it does for the stale question (O-3 reuse).
 */
export interface PendingRedlineLegacyProposal {
  /**
   * The ledger entry this question belongs to: `{bindingId}@t{n}` — the BASE key for a batch. This is
   * the key the host supersedes to make the answer durable (O-4 append-only, keyed by the edit's key).
   */
  ledgerRef: string;
  bindingId: string;
  /**
   * WHY the suggestion has no anchor, and therefore WHICH question the user is being asked. Required,
   * not optional, for the same reason {@link PendingRedlineError.source} is: the host switches on it,
   * and an absent value would silently pick one of two very different stories.
   *
   *  - `'legacy-replay'` (task 053) — a pre-anchor ledger entry whose quoted prose we LOCATED. The
   *    question is "we found this wording; is that the clause you meant?" — we have a candidate.
   *  - `'unidentified-target'` (task 053b) — a post-052 edit whose `target_para_id` arrived NULL. We
   *    have no candidate at all: the question is "the assistant couldn't identify which paragraph to
   *    change — place it here?", answered against the passage the USER selected.
   */
  reason: 'legacy-replay' | 'unidentified-target';
  /**
   * Tier 3 — the document text the proposal would strike, as it reads NOW. Shown truncated; never
   * logged. EMPTY when {@link placement} is `'insert-at-cursor'`: nothing would be struck.
   */
  matchedText: string;
  /**
   * Tier 3 — the prose the replayed suggestion quoted as its target. Shown truncated; never logged.
   * EMPTY for `'unidentified-target'`: that payload quoted nothing, which is the whole problem.
   */
  quotedTarget: string;
  /**
   * Tier 3 — the content the user is being asked to place. Shown truncated; never logged. The
   * `'unidentified-target'` question is unanswerable without it: with no matched wording to show,
   * the proposed text IS what the user judges.
   */
  proposedText: string;
  /**
   * WHERE confirming would put it — so the confirmation names a real position rather than implying one.
   *
   *  - `'matched-span'`       — over the wording the prose match located (task 053's leg).
   *  - `'replace-selection'`  — over the passage the user has selected (they asked for THIS clause).
   *  - `'insert-at-cursor'`   — at the caret, striking nothing, because there is no selection to
   *                             replace. Honest but weak; the user sees {@link contextText} and decides.
   */
  placement: 'matched-span' | 'replace-selection' | 'insert-at-cursor';
  /**
   * Tier 3 — the paragraph the placement sits in, as it reads NOW, so the user can recognise WHERE
   * before answering. Shown truncated; never logged. Empty when the position is not inside a block.
   */
  contextText: string;
  /** How many suggestions in this entry are awaiting confirmation (1 for a single-edit draft). */
  proposedCount: number;
  /** How many target-bearing suggestions the entry carried in total. */
  totalCount: number;
}

/**
 * The reason-and-position half of {@link PendingRedlineLegacyProposal} — everything the confirmation
 * copy needs, without the ledger identity or the counts. Named so `materializeMany` can carry ONE
 * value for "what the first held-back suggestion looked like" instead of six parallel locals.
 */
type LegacyProposalCopy = Pick<
  PendingRedlineLegacyProposal,
  'reason' | 'matchedText' | 'quotedTarget' | 'proposedText' | 'placement' | 'contextText'
>;

export interface UsePendingRedlineResult {
  /** Pending redlines currently rendered (drives the accept/reject affordances). */
  pending: PendingRedline[];
  /** Last unresolved-target error (ambiguous / not-found / target-deleted), or null. */
  error: PendingRedlineError | null;
  /**
   * FR-C05 — the pending "apply anyway?" question, or null. While non-null NOTHING has been placed for
   * the stale suggestion(s); {@link applyStaleTargetAnyway} places them, {@link dismissStaleTarget}
   * discards them. Either way the HOST must write the durable resolution (O-2).
   */
  staleTarget: PendingRedlineStaleTarget | null;
  /**
   * FR-C06 — the pending "is this the right place?" question for a REPLAYED/LEGACY anchorless
   * suggestion, or null. While non-null NOTHING has been placed for that suggestion;
   * {@link applyLegacyProposal} places it, {@link dismissLegacyProposal} discards it. Either way the
   * HOST must write the durable resolution (O-2), exactly as for {@link staleTarget}.
   */
  legacyProposal: PendingRedlineLegacyProposal | null;
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
  /**
   * FR-C05 — "apply anyway": place the deferred stale suggestion(s) despite the drift, and re-record
   * the proposal baseline so the same tab does not re-ask before the host's durable write lands.
   */
  applyStaleTargetAnyway: () => void;
  /** FR-C05 — "skip this suggestion": discard the deferred stale suggestion(s) without placing them. */
  dismissStaleTarget: () => void;
  /**
   * FR-C06 — "yes, place it there": the user CONFIRMED the proposed location for the replayed/legacy
   * suggestion(s). This is the ONLY route from a prose match to a placement; nothing else in this hook
   * can turn an {@link PendingRedlineLegacyProposal} into marks in the document.
   */
  applyLegacyProposal: () => void;
  /** FR-C06 — "no, skip it": discard the proposed replayed/legacy suggestion(s) without placing them. */
  dismissLegacyProposal: () => void;
}

/** Options for {@link usePendingRedline}. */
export interface UsePendingRedlineOptions {
  /**
   * FR-C05 stale detection scope — the DOCUMENT session id. The proposal baseline is recorded per
   * `{scope}::{ledgerRef}`, so two documents' suggestions can never compare against each other.
   * OMITTED ⇒ the stale check is inert and behaviour is exactly as before task 052 (fail-open: the
   * absence of a baseline can only cost a prompt, never cause a wrong placement).
   */
  proposalScope?: string;
}

const INSERTION = 'insertion';
const DELETION = 'deletion';

function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function escapeAttr(value: string): string {
  return escapeHtml(value).replace(/"/g, '&quot;');
}

/**
 * The outcome of resolving a DETERMINISTIC anchor. Deliberately a separate type from the text leg's
 * {@link ResolveResult}: an anchor can fail in a way a text search cannot — `target_deleted`, the
 * paragraph the suggestion named having been removed from the document (FR-C05 outcome 3). It also
 * carries the resolved `paraId`, which the local diff and the stale check both need.
 */
export type AnchorResolveResult =
  | { ok: true; spans: RedlineSpan[]; paraId: string }
  | { ok: false; kind: 'not_found' | 'ambiguous' | 'target_deleted'; matchCount: number; paraId?: string };

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
  // Task 053b — `string | null` is the WIRE shape, not defensiveness: the four compose EDIT Actions
  // declare `target_para_id` as `["string","null"]` and REQUIRE the key, so an unidentified target
  // arrives as an explicit null. `resolveAnchorParaIds` reads it with `?.trim()`, which treats null
  // as "named nothing" — correct HERE (it answers "which paragraphs does this name"), and the reason
  // the null case has to be discriminated by key PRESENCE further down rather than by truthiness.
  anchor: { target_para_id?: string | null; target_ref?: string | null } | undefined,
  referenceMap: readonly ParaIdMapEntry[] | undefined
): AnchorResolveResult | null {
  const resolution = resolveAnchorParaIds({ paraId: anchor?.target_para_id, ref: anchor?.target_ref }, referenceMap);
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
  // FR-C05 outcome 3 (task 052) — the anchor RESOLVED to a paragraph identity, and that paragraph is
  // not in the live document: it was deleted (or the edit was built against another document state).
  // This is NOT `not_found` (an anchor that never resolved at all, e.g. an unknown citation): the
  // suggestion referred to something real that no longer exists, and it gets its own copy. Refuse
  // either way — repairing it would be a guess.
  if (!block) return { ok: false, kind: 'target_deleted', matchCount: 0, paraId: target };

  return { ok: true, spans: [{ from: block.from + 1, to: block.to - 1 }], paraId: target };
}

/**
 * What to call the thing that couldn't be placed, in the user-facing banner. An anchored edit has no
 * `target_text` to quote, so quoting an empty string would make the banner say nothing was targeted when
 * something very specific was.
 *
 * TRUTHINESS IS CORRECT HERE (task 053b audit). This function asks "what did the suggestion NAME?",
 * and a null identifier named nothing — there is no string to put in the sentence, so falling through
 * to `''` (rendered as "no target given") is the right answer for a null exactly as it is for an
 * absent key. Nothing downstream of this function distinguishes the two, and nothing should: by the
 * time a label is needed the placement decision has already been made elsewhere. As of 053b a
 * null-identifier edit no longer reaches this function at all — it becomes a PROPOSAL, not an error —
 * so the `''` branch survives only for a payload that names nothing in any vocabulary.
 */
function unplaceableLabel(
  payload: { target_text?: string; target_ref?: string | null; target_para_id?: string | null } | undefined
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
 * Apply the redline for ONE resolved placement: strike each span, insert the replacement after it.
 * A span may legitimately be EMPTY (`to === from`) when the local diff found a pure in-paragraph
 * insertion — nothing is struck there, the replacement is simply inserted at that point.
 */
function applyRedlineSpans(
  editor: Editor,
  spans: readonly RedlineSpan[],
  insertText: string,
  bindingId: string,
  ledgerRef: string
): void {
  const insertionHtml = buildInsertionHtml(insertText, bindingId, ledgerRef);
  let chain = editor.chain();
  // Deletion halves first — positions are pre-insert, and `setMark` does not shift them.
  for (const span of spans) {
    if (span.to <= span.from) continue;
    chain = chain
      .setTextSelection({ from: span.from, to: span.to })
      .setMark(DELETION, { binding: bindingId, ledgerRef });
  }
  // Insert after EACH struck span, high→low so an earlier span's position is not shifted by a later insert.
  const insertPoints = spans.map(s => s.to).sort((a, b) => b - a);
  for (const at of insertPoints) chain = chain.insertContentAt(at, insertionHtml);
  chain.run();
}

/**
 * A PROPOSED placement — nothing is in the document, the user is being asked. Two shapes, because the
 * two anchorless legs know different things: the replay leg LOCATED a candidate span and can show the
 * wording it found; the unidentified leg has no candidate at all and can only offer the position the
 * user themselves supplied. Collapsing them into one shape would force the second to fake a
 * `matchedText` it never matched.
 */
type TargetedProposal =
  | { status: 'proposed'; reason: 'legacy-replay'; matchedText: string; quotedTarget: string }
  | {
      status: 'proposed';
      reason: 'unidentified-target';
      /** The user's selected text — `''` when there is only a caret. */
      matchedText: string;
      /** The paragraph the placement sits in, so the confirmation can name a real position. */
      contextText: string;
      placement: 'replace-selection' | 'insert-at-cursor';
      /** The range confirming would place into, already clamped to the live document. */
      intendedRange: RedlineSpan;
    };

/** The outcome of {@link planAndApplyTargeted}; `null` means the payload named no target at all. */
type TargetedPlacement =
  | { status: 'applied'; hasDeletion: boolean }
  | {
      status: 'stale';
      /** Task 052b — see {@link PendingRedlineStaleTarget.reason}. */
      reason: 'changed' | 'unverifiable';
      paraId: string;
      currentText: string;
      /** `null` when only a fingerprint (or nothing at all) was available — never a fabricated quote. */
      proposedAgainst: string | null;
    }
  | TargetedProposal
  | { status: 'not_found' | 'ambiguous' | 'target_deleted'; matchCount: number; source: 'anchored' | 'legacy-replay' };

/**
 * Clamp a position into the live document's addressable interior. Positions handed to this hook come
 * from a selection snapshot that may have been remapped across several transactions; a stale end-of-doc
 * position would throw inside ProseMirror rather than mis-place, but an honest clamp keeps the
 * confirmation's promise ("it goes where I showed you") true at the edges too.
 */
function clampToDoc(editor: Editor, pos: number): number {
  const last = Math.max(1, editor.state.doc.content.size - 1);
  return Math.min(Math.max(pos, 1), last);
}

/**
 * Which held-back question the user has ALREADY answered. Passing one is the only way past the
 * corresponding gate below, and each value has exactly one producer:
 *
 *  - `'stale-clause'` ← {@link UsePendingRedlineResult.applyStaleTargetAnyway} (FR-C05);
 *  - `'anchorless-replay'` ← {@link UsePendingRedlineResult.applyLegacyProposal} (FR-C06);
 *  - `'unidentified-target'` ← {@link UsePendingRedlineResult.applyLegacyProposal} (task 053b), for a
 *    deferred edit whose identifier arrived null. It shares the FR-C06 confirmation MECHANISM (one
 *    question surface, one answer path — root §11) but not its value: each deferred edit replays with
 *    the question IT was held back by, so confirming one kind can never release the other.
 *
 * All producers are reached only from the host's `ConfirmModal` confirm handler, so "the user said
 * yes" is a value that has to travel from a click to here — it cannot be defaulted into existence.
 */
type ConfirmedQuestion = 'stale-clause' | 'anchorless-replay' | 'unidentified-target';

/**
 * THE ONE targeted-placement path, shared by `materialize`, `materializeMany` and both confirmation
 * replays, so they cannot drift apart (project invariant 6 — one edit-capture mechanism). Resolution
 * order is FIXED and never the reverse:
 *
 *   1. DETERMINISTIC ANCHOR (`target_para_id` / `target_ref`) → the paragraph, then the LOCAL diff
 *      inside it (FR-C05 outcome 1) — bounded by that paragraph by construction.
 *   2. Only when the payload named NO anchor at all: the FR-C06 BOUNDED FALLBACK
 *      (`./anchorlessReplayFallback.ts`) for a replayed/legacy entry. An anchor that is PRESENT and
 *      does not resolve is REFUSED, never searched.
 *
 * The two legs are NOT symmetric, and that asymmetry is the point (FR-C06 / ADR-049 I-7):
 *
 *   - the ANCHORED leg can return `applied` — it resolved an address, not a resemblance;
 *   - the FALLBACK leg returns `proposed` and cannot return `applied` unless the caller hands in
 *     `confirmed: 'anchorless-replay'`, which only a user click produces. A prose match PROPOSES.
 *
 * Returns `null` when the payload has neither an anchor nor replayable prose — the caller's signal to
 * run its insertion-at-cursor branch.
 */
function planAndApplyTargeted(args: {
  editor: Editor;
  payload: ComposeDraftPayload;
  referenceMap: readonly ParaIdMapEntry[] | undefined;
  ledgerRef: string;
  bindingId: string;
  newText: string;
  proposalScope: string | undefined;
  /**
   * Task 052b — whether this materialize is driven by the dispatch that produced the entry (`'live'`)
   * or is a re-materialize from durable ledger state (`'replay'`). Decides whether the live paragraph
   * may be recorded as the capture-time text. See {@link MaterializeOrigin}.
   */
  origin: MaterializeOrigin;
  /** The question the user already answered, if any. See {@link ConfirmedQuestion}. */
  confirmed?: ConfirmedQuestion;
  /**
   * Task 053b — where the USER is: the selection snapshot captured before this pass mutated anything,
   * kept valid across intervening transactions by the hook's remapper. Used ONLY by the
   * unidentified-target leg, and only to describe/complete a placement the user confirms. Omitted ⇒
   * the live selection is read instead (the single-edit path always supplies it).
   */
  intendedRange?: RedlineSpan;
}): TargetedPlacement | null {
  const { editor, payload, referenceMap, ledgerRef, bindingId, newText, proposalScope, origin, confirmed } = args;

  const anchored = resolveAnchoredSpans(editor, payload, referenceMap);

  if (anchored === null) {
    // FR-C06 — the bounded fallback. `classifyAnchorlessReplay` is the only mint of the argument type
    // `resolveAnchorlessReplay` takes, and it refuses any payload carrying an anchor; reaching here
    // with an anchored payload is therefore not a branch to guard but a value that cannot be built.
    const replayTarget = classifyAnchorlessReplay(payload);
    if (replayTarget !== null) {
      const outcome = resolveAnchorlessReplay(editor, replayTarget);
      if (outcome.kind === 'unresolved') {
        return { status: outcome.reason, matchCount: outcome.matchCount, source: 'legacy-replay' };
      }
      if (confirmed !== 'anchorless-replay') {
        // NOTHING is placed. The user is shown what would be struck and decides.
        return {
          status: 'proposed',
          reason: 'legacy-replay',
          matchedText: outcome.matchedText,
          quotedTarget: outcome.quotedTarget,
        };
      }
      applyRedlineSpans(editor, outcome.spans, newText, bindingId, ledgerRef);
      return { status: 'applied', hasDeletion: true };
    }

    // Task 053b — no anchor, no prose. Two payloads look identical to `payload?.target_para_id`
    // truthiness and mean opposite things, so the discriminator is key PRESENCE:
    //
    //   key ABSENT  ⇒ never edit-shaped (`compose-draft-document`, Flow-3 `compose_context_insert`)
    //                 ⇒ `null` here ⇒ the caller's insertion-at-cursor branch, byte-for-byte as before.
    //   key PRESENT ⇒ an EDIT that was asked for an identifier and had none. Before this task it took
    //                 the SAME branch and returned `applied`, so a revised clause could land at
    //                 whatever the caret happened to be and the status told the user it succeeded.
    const unidentified = classifyUnidentifiedTarget(payload);
    if (unidentified === null) return null;

    // The user's own position is the only honest candidate: there is nothing to match on, and
    // inventing something to match on is what ADR-049 I-7 forbids and task 052 retired.
    const requested = args.intendedRange ?? { from: editor.state.selection.from, to: editor.state.selection.to };
    const from = clampToDoc(editor, requested.from);
    const to = clampToDoc(editor, Math.max(requested.to, requested.from));
    const range: RedlineSpan = { from, to };

    if (confirmed !== 'unidentified-target') {
      // NOTHING is placed. Describe the position so the confirmation names somewhere real.
      const hasSelection = to > from;
      const block = collectBlocks(editor).find(b => b.from <= from && to <= b.to);
      return {
        status: 'proposed',
        reason: 'unidentified-target',
        matchedText: hasSelection ? editor.state.doc.textBetween(from, to, ' ', ' ') : '',
        contextText: block ? block.text : '',
        placement: hasSelection ? 'replace-selection' : 'insert-at-cursor',
        intendedRange: range,
      };
    }
    // Confirmed. `applyRedlineSpans` strikes a non-empty span and inserts after it, and for an EMPTY
    // span (`to === from`, a caret) simply inserts at that point — one call covers both, so "replace
    // what I selected" and "insert where I am" are the same mechanism, not two.
    applyRedlineSpans(editor, [range], newText, bindingId, ledgerRef);
    return { status: 'applied', hasDeletion: to > from };
  }
  if (!anchored.ok) return { status: anchored.kind, matchCount: anchored.matchCount, source: 'anchored' };

  const paragraphSpan = anchored.spans[0];
  // FR-C05 outcome 1 — narrow to the words that actually changed. `null` ⇒ one of the three defined
  // whole-paragraph fall-backs (inline markup / multi-paragraph replacement / nothing changed).
  const local = narrowAnchoredSpan(editor, paragraphSpan, newText);
  const currentText = local ? local.currentText : readRangeText(editor, paragraphSpan);

  // FR-C05 outcome 2 — has this clause moved since the model wrote the suggestion?
  //
  // Task 052b restructured this from "is a baseline recorded?" into the four cases that actually
  // exist. The old shape read the ABSENCE of a record as "this must be the first materialize", which
  // is also what a different tab, an evicted entry and a disabled store look like — so on those paths
  // it re-baselined against a possibly-drifted paragraph and applied silently. The discriminator is
  // now {@link MaterializeOrigin}, a fact about the CALL, which stays true where a store cannot reach.
  //
  // | origin | recorded comparison | outcome |
  // |---|---|---|
  // | live   | (any)      | record + place — nothing can have drifted since the model wrote it |
  // | replay | unchanged  | place — we KNOW the clause is what the suggestion was written against |
  // | replay | changed    | ASK, `reason: 'changed'` — task 052's question, now durable across tabs |
  // | replay | unrecorded | ASK, `reason: 'unverifiable'` — the hole 052b closes (see below) |
  //
  // The last row is the whole task. "No prompt" reads as safe and is not: it IS the pre-052 behaviour,
  // and the pre-052 behaviour in the stale case is a silent overwrite of the user's newer text. Where
  // detection cannot be established the outcome is a confirmation, never a silent apply (project
  // invariant 1: a defined, honest outcome).
  //
  // Note that `live` only suppresses the UNRECORDED question; a recorded-and-changed clause still
  // asks on either origin. This task can only ADD questions 052 would not have raised, never remove
  // one it would.
  if (proposalScope) {
    const comparison = compareProposalBaseline(proposalScope, ledgerRef, currentText);
    if (confirmed === 'stale-clause') {
      // Already answered by the user. Re-baseline so a later re-render in this browser does not
      // re-ask before the host's durable FR-17 supersession write lands (the answer's real home).
      recordProposalBaseline(proposalScope, ledgerRef, currentText);
    } else if (comparison.status === 'changed') {
      return {
        status: 'stale',
        reason: 'changed',
        paraId: anchored.paraId,
        currentText,
        proposedAgainst: comparison.proposedAgainst,
      };
    } else if (comparison.status === 'unrecorded') {
      if (origin === 'live') {
        // The model produced this proposal against the live document seconds ago, so the paragraph as
        // it reads NOW *is* the capture-time text. This is the ONE place a baseline is minted.
        recordProposalBaseline(proposalScope, ledgerRef, currentText);
      } else {
        return {
          status: 'stale',
          reason: 'unverifiable',
          paraId: anchored.paraId,
          currentText,
          proposedAgainst: null,
        };
      }
    }
  }

  const spans = local ? [local.span] : [paragraphSpan];
  applyRedlineSpans(editor, spans, local ? local.replacement : newText, bindingId, ledgerRef);
  return { status: 'applied', hasDeletion: spans.some(s => s.to > s.from) };
}

/**
 * One suggestion held back by a confirmation question, awaiting the user's answer. NOTHING is in the
 * document for a deferred edit — it is held here precisely so it is not.
 */
interface DeferredEdit {
  readonly payload: ComposeDraftPayload;
  readonly ledgerRef: string;
  readonly bindingId: string;
  readonly turn: number;
  /**
   * Task 053b — the question THIS edit was held back by, carried per-item rather than inferred from
   * the banner. A batch can in principle hold both anchorless kinds (a mixed-vintage ledger entry);
   * replaying each with its own answer is what stops one confirmation releasing the other's edits.
   */
  readonly question: ConfirmedQuestion;
  /**
   * Task 053b — the range the proposal promised, for an `'unidentified-target'` edit only. Kept valid
   * by the hook's transaction remapper: a batched pass places OTHER edits between the question and the
   * answer, and an unremapped position would land the confirmed edit somewhere the user never saw.
   */
  readonly intendedRange?: RedlineSpan;
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
  referenceMap?: readonly ParaIdMapEntry[],
  options?: UsePendingRedlineOptions
): UsePendingRedlineResult {
  const [pending, setPending] = React.useState<PendingRedline[]>([]);
  const [error, setError] = React.useState<PendingRedlineError | null>(null);
  const [staleTarget, setStaleTarget] = React.useState<PendingRedlineStaleTarget | null>(null);
  const [legacyProposal, setLegacyProposal] = React.useState<PendingRedlineLegacyProposal | null>(null);
  const proposalScope = options?.proposalScope;
  // The suggestions held back by the CURRENT stale question. A ref (not state) because it is written
  // and read inside the same user gesture and must never drive a render on its own.
  const deferredStaleRef = React.useRef<DeferredEdit[]>([]);
  // FR-C06 — the same holding pattern for the CURRENT anchorless-replay proposal. A SEPARATE ref, not
  // a shared queue with a discriminant: the two questions are answered by two different buttons, and a
  // shared queue would let one answer release the other's suggestions.
  //
  // Task 053b — the unidentified-target proposals share THIS queue, because they share the question
  // surface and the answer button (root §11: one confirmation path, not two). What is NOT shared is
  // the answer itself: each entry carries its own {@link DeferredEdit.question}.
  const deferredProposalRef = React.useRef<DeferredEdit[]>([]);
  /**
   * Task 053b — the user's intended position for THIS pass, captured before the pass mutates anything
   * and kept valid by the transaction remapper below. `materializeMany` needs it because it places
   * other edits between the capture and the deferral; reading the live selection at that later moment
   * would report wherever the LAST placement left the caret, not where the user is.
   */
  const trackedIntentRef = React.useRef<RedlineSpan | null>(null);

  const clearError = React.useCallback(() => setError(null), []);

  const materialize = React.useCallback(
    (payload: ComposeDraftPayload, provenance: ComposeDraftProvenance): MaterializeStatus => {
      if (!editor) return 'noop';
      const { ledgerRef, bindingId, turn } = provenance;
      // Task 052b — FAIL-CLOSED. A caller that does not declare freshness does not get it: the worst
      // an omitted `origin` can cost is one confirmation, where the worst a wrong `'live'` can cost is
      // the user's own newer wording, silently. See {@link MaterializeOrigin}.
      const origin: MaterializeOrigin = provenance.origin ?? 'replay';
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

      // Task 051/052/053 — FIXED ordering, never the reverse: (1) DETERMINISTIC anchor + local diff,
      // (2) the BOUNDED anchorless-replay fallback, which can only PROPOSE, (3) task 053b's
      // unidentified-target leg, which also only proposes. `null` ⇒ no target key at all.
      // `intendedRange` is the PRE-supersession selection snapshot already remapped above — the same
      // value the insertion branch below uses, so a proposal and an insertion agree about "where".
      trackedIntentRef.current = { from: intendedFrom, to: intendedTo };
      const placement = planAndApplyTargeted({
        editor,
        payload,
        referenceMap,
        ledgerRef,
        bindingId,
        newText,
        proposalScope,
        origin,
        intendedRange: trackedIntentRef.current,
      });
      trackedIntentRef.current = null;
      let hasDeletion = false;

      if (placement !== null) {
        if (placement.status === 'proposed') {
          // FR-C06 — NOTHING is placed. A replayed/legacy suggestion located by prose is a PROPOSAL:
          // the match says where that wording occurs today, not that this is the clause the model was
          // looking at. Hold it and ask.
          //
          // Task 053b — the SAME hold for an edit whose identifier arrived null: there is nothing to
          // match on at all, so the question becomes "place it over what you selected?" rather than
          // "is this the clause?". One surface, one answer path, two honest reasons.
          deferredProposalRef.current = [
            placement.reason === 'unidentified-target'
              ? {
                  payload,
                  ledgerRef,
                  bindingId,
                  turn,
                  question: 'unidentified-target',
                  intendedRange: placement.intendedRange,
                }
              : { payload, ledgerRef, bindingId, turn, question: 'anchorless-replay' },
          ];
          setLegacyProposal({
            ledgerRef,
            bindingId,
            proposedText: newText,
            proposedCount: 1,
            totalCount: 1,
            ...(placement.reason === 'unidentified-target'
              ? {
                  reason: 'unidentified-target' as const,
                  matchedText: placement.matchedText,
                  quotedTarget: '',
                  placement: placement.placement,
                  contextText: placement.contextText,
                }
              : {
                  reason: 'legacy-replay' as const,
                  matchedText: placement.matchedText,
                  quotedTarget: placement.quotedTarget,
                  placement: 'matched-span' as const,
                  contextText: '',
                }),
          });
          if (superseded.length > 0) {
            setPending(prev => prev.filter(p => !superseded.some(s => s.ledgerRef === p.ledgerRef)));
          }
          return 'proposed';
        }
        if (placement.status === 'stale') {
          // FR-C05 outcome 2 — NOTHING is placed. Hold the suggestion and ask; the alternative is
          // silently overwriting the user's newer edit, which is the data loss this closes.
          deferredStaleRef.current = [{ payload, ledgerRef, bindingId, turn, question: 'stale-clause' }];
          setStaleTarget({
            ledgerRef,
            bindingId,
            reason: placement.reason,
            paraId: placement.paraId,
            currentText: placement.currentText,
            proposedAgainst: placement.proposedAgainst,
            staleCount: 1,
            totalCount: 1,
          });
          if (superseded.length > 0) {
            setPending(prev => prev.filter(p => !superseded.some(s => s.ledgerRef === p.ledgerRef)));
          }
          return 'stale';
        }
        if (placement.status !== 'applied') {
          // UAT-21 (2026-08-18, owner: "highest trust priority") — DO NOT fall back to the user's
          // live selection when the target can't be located. The prior Round-3 UAT Test #4
          // fallback anchored the redline on whatever range happened to be selected and returned
          // `applied`, but that selection can be STALE (the caret moved during the round-trip) or
          // wholly IRRELEVANT (a ledger/refresh replay where the user never selected anything for
          // THIS edit) — so it could strike-and-replace the WRONG text and present it as success: a
          // SILENT mis-placement of a legal edit. Under the R7 honest/safe charter ("never lie — no
          // silent mis-placement, no false 'applied'") every unresolved target — `not_found`,
          // `ambiguous` AND `target_deleted` — surfaces via the banner and renders NOTHING for this
          // entry. The user re-selects the exact passage and re-runs, or edits the clause manually.
          setError({
            ledgerRef,
            kind: placement.status,
            source: placement.source,
            targetText: unplaceableLabel(payload),
            matchCount: placement.matchCount,
          });
          if (superseded.length > 0) {
            setPending(prev => prev.filter(p => !superseded.some(s => s.ledgerRef === p.ledgerRef)));
          }
          return placement.status;
        }
        hasDeletion = placement.hasDeletion;
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
    [editor, pending, referenceMap, proposalScope]
  );

  const materializeMany = React.useCallback(
    (edits: ComposeDraftPayload[], baseProvenance: ComposeDraftProvenance): MaterializeStatus[] => {
      if (!editor) return edits.map(() => 'noop');
      const { ledgerRef: baseRef, bindingId, turn } = baseProvenance;
      // Task 052b — FAIL-CLOSED, same rule as `materialize`. See {@link MaterializeOrigin}.
      const origin: MaterializeOrigin = baseProvenance.origin ?? 'replay';

      // Idempotent: this whole-doc set already rendered (refresh / duplicate Flow-5 signal) → no-op.
      if (pending.some(p => ledgerRefMatches(p.ledgerRef, baseRef)) || isPresent(editor, baseRef)) {
        return edits.map(() => 'already_present');
      }

      // Supersession (FR-17 alignment): a newer whole-doc output for this binding (a DIFFERENT base)
      // replaces any prior pending suggestion(s) for that binding — the superseded marks MUST go.
      const superseded = pending.filter(p => p.bindingId === bindingId && !ledgerRefMatches(p.ledgerRef, baseRef));
      for (const prior of superseded) stripRedlineMarks(editor, prior.ledgerRef);

      // Task 053b — capture WHERE THE USER IS once, AFTER the strips and BEFORE the first placement.
      // Read per-edit inside the loop instead and an unidentified edit at index 3 would be proposed at
      // wherever `applyRedlineSpans` left the selection after edit 2 — i.e. inside the previous
      // suggestion's paragraph. The transaction remapper keeps this value valid as the loop mutates
      // the document, so a batched proposal still points at the passage the user actually had.
      trackedIntentRef.current = { from: editor.state.selection.from, to: editor.state.selection.to };

      const statuses: MaterializeStatus[] = [];
      const newPending: PendingRedline[] = [];
      // Item 1 (UAT round-4): collect unplaceable target-bearing edits so the banner can surface a calm
      // batched summary (N of M) rather than a single alarming per-edit message. An array (vs a closure-
      // assigned `let`) keeps the type narrowable after the forEach.
      const failures: PendingRedlineError[] = [];
      // FR-C05 outcome 2 — suggestions whose clause drifted. Held back (NOT placed) until the user
      // answers one batched question, rather than raising a modal storm per change.
      const deferredStale: DeferredEdit[] = [];
      /**
       * Task 052b — the item whose detail the batched question shows. A batch can hold BOTH kinds
       * (some clauses provably changed, others merely unverifiable), and the copy must not over- or
       * under-claim: a genuinely CHANGED item wins, because "at least one of these definitely changed"
       * is then true and is the stronger warning; the shown paraId/text/wording is that same item's,
       * so the reason and the detail always describe one and the same clause.
       */
      let firstStale: {
        reason: 'changed' | 'unverifiable';
        paraId: string;
        currentText: string;
        proposedAgainst: string | null;
      } | null = null;
      // FR-C06 — replayed/legacy suggestions located by prose. Held back (NOT placed) until the user
      // confirms ONE batched proposal, for the same reason the stale set is batched.
      //
      // Task 053b — accumulated THROUGH THE REF, not a plain local, so the transaction remapper below
      // sees each deferred entry as soon as it is deferred and keeps its `intendedRange` valid across
      // the placements that follow it in this same pass.
      deferredProposalRef.current = [];
      let firstProposal: LegacyProposalCopy | null = null;
      let targetedCount = 0;

      edits.forEach((payload, i) => {
        // Sub-key per change so per-change on-click accept/reject stays granular (DEF-12), while the
        // BASE key addresses the whole set (Accept-all/Reject-all).
        const subRef = `${baseRef}#${i}`;
        const newText = payload?.new_text ?? '';
        // FR-13 (client-derived, §6.5 Path B) — per-edit grounding signal 1 (each change-list entry
        // is its own PendingRedline with its own confidence band).
        const editHasSources = Array.isArray(payload?.sources) && payload.sources.length > 0;

        // Task 051/052 — same fixed ordering, same one placement path as the single-edit route. Each
        // call re-reads the CURRENT doc, so it already accounts for earlier edits' struck (still
        // present) originals + appended insertions — positions stay valid per edit.
        const placement = planAndApplyTargeted({
          editor,
          payload,
          referenceMap,
          ledgerRef: subRef,
          bindingId,
          newText,
          proposalScope,
          origin,
          intendedRange: trackedIntentRef.current ?? undefined,
        });

        if (placement === null) {
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

        targetedCount += 1;

        if (placement.status === 'proposed') {
          deferredProposalRef.current = [
            ...deferredProposalRef.current,
            placement.reason === 'unidentified-target'
              ? {
                  payload,
                  ledgerRef: subRef,
                  bindingId,
                  turn,
                  question: 'unidentified-target',
                  intendedRange: placement.intendedRange,
                }
              : { payload, ledgerRef: subRef, bindingId, turn, question: 'anchorless-replay' },
          ];
          if (firstProposal === null) {
            firstProposal =
              placement.reason === 'unidentified-target'
                ? {
                    reason: 'unidentified-target',
                    matchedText: placement.matchedText,
                    quotedTarget: '',
                    proposedText: newText,
                    placement: placement.placement,
                    contextText: placement.contextText,
                  }
                : {
                    reason: 'legacy-replay',
                    matchedText: placement.matchedText,
                    quotedTarget: placement.quotedTarget,
                    proposedText: newText,
                    placement: 'matched-span',
                    contextText: '',
                  };
          }
          statuses.push('proposed');
          return;
        }

        if (placement.status === 'stale') {
          deferredStale.push({ payload, ledgerRef: subRef, bindingId, turn, question: 'stale-clause' });
          // Task 052b — first item wins, EXCEPT that a `'changed'` item promotes over a previously
          // captured `'unverifiable'` one (see `firstStale`'s note). A `'changed'` item is never
          // displaced, so this settles after at most one promotion.
          if (firstStale === null || (firstStale.reason === 'unverifiable' && placement.reason === 'changed')) {
            firstStale = {
              reason: placement.reason,
              paraId: placement.paraId,
              currentText: placement.currentText,
              proposedAgainst: placement.proposedAgainst,
            };
          }
          statuses.push('stale');
          return;
        }

        if (placement.status !== 'applied') {
          // FR-19 "do not guess": skip this one, record the failure, keep going.
          failures.push({
            ledgerRef: subRef,
            kind: placement.status,
            source: placement.source,
            targetText: unplaceableLabel(payload),
            matchCount: placement.matchCount,
          });
          statuses.push(placement.status);
          return;
        }

        newPending.push({
          ledgerRef: subRef,
          bindingId,
          turn,
          rationale: payload?.rationale,
          hasDeletion: placement.hasDeletion,
          hasSources: editHasSources,
          confidenceBand: deriveConfidenceBand(editHasSources, placement.hasDeletion),
        });
        statuses.push('applied');
      });

      // Item 1: surface the FIRST failure with batched counts so the banner can say "N of M couldn't be
      // placed" calmly (array access → cleanly narrowable type, unlike a closure-assigned `let`).
      const firstFailure = failures[0];
      setError(firstFailure ? { ...firstFailure, failedCount: failures.length, totalCount: targetedCount } : null);
      deferredStaleRef.current = deferredStale;
      if (firstStale !== null) {
        const stale: {
          reason: 'changed' | 'unverifiable';
          paraId: string;
          currentText: string;
          proposedAgainst: string | null;
        } = firstStale;
        setStaleTarget({
          ledgerRef: baseRef,
          bindingId,
          reason: stale.reason,
          paraId: stale.paraId,
          currentText: stale.currentText,
          proposedAgainst: stale.proposedAgainst,
          staleCount: deferredStale.length,
          totalCount: targetedCount,
        });
      } else {
        setStaleTarget(null);
      }
      // Task 053b — `deferredProposalRef.current` was populated in the loop (see the remapper note
      // above), so it is read here rather than assigned. A batch whose entries were held back by
      // DIFFERENT questions renders the FIRST one's reason; each entry still replays with its own
      // answer, so the copy can be imprecise about cause but the PLACEMENT never is.
      const deferredProposals = deferredProposalRef.current;
      if (firstProposal !== null) {
        // Explicitly annotated for the SAME reason `firstStale` is above: TypeScript resets the
        // narrowing of a `let` assigned inside a callback, so the union collapses at this `if` and the
        // annotation is what re-states the real type.
        const proposal: LegacyProposalCopy = firstProposal;
        setLegacyProposal({
          ledgerRef: baseRef,
          bindingId,
          ...proposal,
          proposedCount: deferredProposals.length,
          totalCount: targetedCount,
        });
      } else {
        setLegacyProposal(null);
      }
      trackedIntentRef.current = null;
      setPending(prev => {
        const kept = prev.filter(p => !superseded.some(s => s.ledgerRef === p.ledgerRef));
        return [...kept, ...newPending];
      });
      return statuses;
    },
    [editor, pending, referenceMap, proposalScope]
  );

  /**
   * FR-C05 — "apply anyway". Replays the deferred suggestion(s) through the SAME placement path with
   * the stale check forced open, so nothing about HOW they are placed differs from a normal apply.
   *
   * This is NOT the durable resolution. Per the task-050 assessment §4.4 O-2/O-3 the host writes the
   * FR-17 supersession for {@link PendingRedlineStaleTarget.ledgerRef}, which is what stops the reopen
   * pass re-raising the question after a refresh. The baseline re-record inside
   * {@link planAndApplyTargeted} only covers the window before that write lands.
   */
  const applyStaleTargetAnyway = React.useCallback(() => {
    const deferred = deferredStaleRef.current;
    deferredStaleRef.current = [];
    setStaleTarget(null);
    if (!editor || deferred.length === 0) return;

    const placed: PendingRedline[] = [];
    const failures: PendingRedlineError[] = [];
    for (const item of deferred) {
      const placement = planAndApplyTargeted({
        editor,
        payload: item.payload,
        referenceMap,
        ledgerRef: item.ledgerRef,
        bindingId: item.bindingId,
        newText: item.payload?.new_text ?? '',
        proposalScope,
        // Task 052b — `'replay'` is the honest value AND the inert one: `confirmed: 'stale-clause'`
        // short-circuits the baseline gate ahead of any origin test, so this replay re-baselines and
        // places whichever way it is labelled. Declaring the safe value keeps that true if the gate is
        // ever reordered.
        origin: 'replay',
        confirmed: item.question,
      });
      // Unreachable with `confirmed: 'stale-clause'` — the stale gate is the only producer of both,
      // and an anchored payload (the only kind that can be stale) never reaches the fallback.
      if (placement === null || placement.status === 'stale' || placement.status === 'proposed') continue;
      if (placement.status !== 'applied') {
        failures.push({
          ledgerRef: item.ledgerRef,
          kind: placement.status,
          source: placement.source,
          targetText: unplaceableLabel(item.payload),
          matchCount: placement.matchCount,
        });
        continue;
      }
      const hasSources = Array.isArray(item.payload?.sources) && item.payload.sources.length > 0;
      placed.push({
        ledgerRef: item.ledgerRef,
        bindingId: item.bindingId,
        turn: item.turn,
        rationale: item.payload?.rationale,
        hasDeletion: placement.hasDeletion,
        hasSources,
        confidenceBand: deriveConfidenceBand(hasSources, placement.hasDeletion),
      });
    }
    if (failures.length > 0) {
      setError({ ...failures[0], failedCount: failures.length, totalCount: deferred.length });
    }
    if (placed.length > 0) {
      setPending(prev => [...prev.filter(p => !placed.some(n => n.ledgerRef === p.ledgerRef)), ...placed]);
    }
  }, [editor, referenceMap, proposalScope]);

  /**
   * FR-C05 — "skip this suggestion". Discards the deferred suggestion(s) without placing anything. The
   * host still writes the durable supersession, so the question is not re-raised after a refresh.
   */
  const dismissStaleTarget = React.useCallback(() => {
    deferredStaleRef.current = [];
    setStaleTarget(null);
  }, []);

  /**
   * FR-C06 — "yes, place it there". THE ONLY ROUTE from a prose match to marks in the document.
   *
   * Replays the deferred suggestion(s) through the SAME placement path with `confirmed:
   * 'anchorless-replay'`, which is the single value that lets the bounded fallback's `proposed`
   * outcome become an `applied` one. Nothing about HOW they are placed differs from a normal apply —
   * what differs is that a human said yes first.
   *
   * Like FR-C05's twin, this is NOT the durable resolution: per the task-050 assessment §4.4 O-2/O-3
   * the host writes the FR-17 supersession for {@link PendingRedlineLegacyProposal.ledgerRef}, which is
   * what stops the reopen pass re-raising the question after a refresh (O-5).
   */
  const applyLegacyProposal = React.useCallback(() => {
    const deferred = deferredProposalRef.current;
    deferredProposalRef.current = [];
    setLegacyProposal(null);
    if (!editor || deferred.length === 0) return;

    const placed: PendingRedline[] = [];
    const failures: PendingRedlineError[] = [];
    for (const item of deferred) {
      const placement = planAndApplyTargeted({
        editor,
        payload: item.payload,
        referenceMap,
        ledgerRef: item.ledgerRef,
        bindingId: item.bindingId,
        newText: item.payload?.new_text ?? '',
        proposalScope,
        // Task 052b — a deferred PROPOSAL is anchorless by construction, so it never reaches the
        // anchored baseline gate at all. `'replay'` is declared rather than defaulted because that is
        // what this call actually is: a re-placement long after the entry was produced.
        origin: 'replay',
        // Task 053b — the answer is per-ITEM, never the button's own label. A batch can hold both
        // anchorless kinds; passing one blanket value would replay the other kind un-confirmed, which
        // would re-propose it forever (harmless) or, worse, invite someone to "fix" that by widening
        // the gate. The item carries what it was held back by.
        confirmed: item.question,
        // Task 053b — the range the PROPOSAL promised, remapped across everything that happened since.
        // Omitting it would fall back to the live selection, which by now sits wherever the last
        // placement or the modal left it — the user would be shown one position and given another.
        intendedRange: item.intendedRange,
      });
      // Unreachable with either anchorless answer: a deferred proposal is anchorless by construction
      // (minted by `classifyAnchorlessReplay` or `classifyUnidentifiedTarget`), so it can be neither
      // `stale` (an anchored-only outcome) nor `proposed` again.
      if (placement === null || placement.status === 'stale' || placement.status === 'proposed') continue;
      if (placement.status !== 'applied') {
        // The document changed between the proposal and the answer (the quoted prose was edited or
        // duplicated in the meantime). Refuse rather than re-guess — same rule as the first pass.
        failures.push({
          ledgerRef: item.ledgerRef,
          kind: placement.status,
          source: placement.source,
          targetText: unplaceableLabel(item.payload),
          matchCount: placement.matchCount,
        });
        continue;
      }
      const hasSources = Array.isArray(item.payload?.sources) && item.payload.sources.length > 0;
      placed.push({
        ledgerRef: item.ledgerRef,
        bindingId: item.bindingId,
        turn: item.turn,
        rationale: item.payload?.rationale,
        hasDeletion: placement.hasDeletion,
        hasSources,
        confidenceBand: deriveConfidenceBand(hasSources, placement.hasDeletion),
      });
    }
    if (failures.length > 0) {
      setError({ ...failures[0], failedCount: failures.length, totalCount: deferred.length });
    }
    if (placed.length > 0) {
      setPending(prev => [...prev.filter(p => !placed.some(n => n.ledgerRef === p.ledgerRef)), ...placed]);
    }
  }, [editor, referenceMap, proposalScope]);

  /**
   * FR-C06 — "no, skip it". Discards the proposed suggestion(s) without placing anything. The host
   * still writes the durable supersession, so the question is not re-raised after a refresh (O-5).
   */
  const dismissLegacyProposal = React.useCallback(() => {
    deferredProposalRef.current = [];
    setLegacyProposal(null);
  }, []);

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
  /**
   * Task 053b — REBASE the positions a pending question promised, through every document change.
   *
   * The unidentified-target proposal is the first thing this hook holds that is addressed by POSITION
   * rather than by paraId or by prose, and a raw position goes stale the moment anything shifts it:
   * a batched pass places other edits between the question and the answer, and a host that lets the
   * user keep typing would do the same. Confirming against a stale position is a silent mis-placement
   * with a confirmation dialog in front of it — precisely the UAT-21 failure this task closes.
   *
   * The primitive is the editor's own transaction `Mapping` — the SAME one `stripRedlineMarks`,
   * `RebasedOperationLog` and `useAiGenerateBookmark` rebase with (root §11: reuse the position system
   * that exists, do not introduce a second one). Bias `from` left and `to` right so an insertion
   * exactly at the boundary widens the promised range rather than escaping it.
   *
   * Cheap by construction: it returns immediately unless a question is actually open AND the document
   * actually changed, so selection-only transactions (the overwhelming majority) cost one comparison.
   */
  React.useEffect(() => {
    if (!editor) return undefined;
    const rebaseHeldPositions = ({ transaction }: { transaction: Transaction }): void => {
      if (!transaction.docChanged) return;
      const held = deferredProposalRef.current;
      const intent = trackedIntentRef.current;
      if (held.length === 0 && intent === null) return;
      const remap = (span: RedlineSpan): RedlineSpan => ({
        from: transaction.mapping.map(span.from, -1),
        to: transaction.mapping.map(span.to, 1),
      });
      if (intent !== null) trackedIntentRef.current = remap(intent);
      if (held.length > 0) {
        deferredProposalRef.current = held.map(item =>
          item.intendedRange ? { ...item, intendedRange: remap(item.intendedRange) } : item
        );
      }
    };
    editor.on('transaction', rebaseHeldPositions);
    return () => {
      editor.off('transaction', rebaseHeldPositions);
    };
  }, [editor]);

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
    () => ({
      pending,
      error,
      staleTarget,
      legacyProposal,
      materialize,
      materializeMany,
      accept,
      reject,
      clearError,
      applyStaleTargetAnyway,
      dismissStaleTarget,
      applyLegacyProposal,
      dismissLegacyProposal,
    }),
    [
      pending,
      error,
      staleTarget,
      legacyProposal,
      materialize,
      materializeMany,
      accept,
      reject,
      clearError,
      applyStaleTargetAnyway,
      dismissStaleTarget,
      applyLegacyProposal,
      dismissLegacyProposal,
    ]
  );
}

export default usePendingRedline;
