/**
 * documentProfileChoices.ts — add-in-local mirror of the `sprk_document.sprk_filesummarystatus`
 * choice table (spaarkeai-word-add-in-r1 task 021, FR-07).
 *
 * ⚠️ SANCTIONED DUPLICATION (CLAUDE.md §11): the same rationale as
 * `shared/taskpane/services/todoChoices.ts` — there is no Xrm in an Office host, so the add-in
 * cannot read Dataverse choice metadata at runtime and mirrors the option-set values as literals
 * instead. `todoChoices.ts` is the sanctioned precedent for exactly this reason.
 *
 * Values verified live against `sprk_document.sprk_filesummarystatus` (2026-09 discovery, recorded
 * in `projects/spaarkeai-word-add-in-r1/CLAUDE.md`): the column carries exactly SEVEN options. If a
 * new option is ever added in Dataverse, this table (and the rendering in `DocumentProfileSection`)
 * must be updated in lockstep — an unmapped numeric value is treated as `'Unrecognized'` by
 * {@link summaryStatusFromCode}, never silently folded into an existing state.
 */

/** The seven `sprk_filesummarystatus` states, by name. */
export type DocumentSummaryStatusName =
  | 'None'
  | 'Pending'
  | 'Completed'
  | 'OptedOut'
  | 'Failed'
  | 'NotSupported'
  | 'Skipped';

/** `sprk_filesummarystatus` Dataverse option value → state name. */
const STATUS_CODE_TO_NAME: Readonly<Record<number, DocumentSummaryStatusName>> = {
  100000000: 'None',
  100000001: 'Pending',
  100000002: 'Completed',
  100000003: 'OptedOut',
  100000004: 'Failed',
  100000005: 'NotSupported',
  100000006: 'Skipped',
};

/**
 * Resolves a raw `sprk_filesummarystatus` option value to its state name.
 *
 * `null`/`undefined` (the column was never set on this row — Dataverse applies no implicit
 * default) is treated identically to `'None'`: both mean "profiling has not been attempted",
 * so the pane must not distinguish "no value yet" from the explicit None option with two
 * different messages.
 *
 * A numeric value outside the seven known options returns `'Unrecognized'` — this is NOT an
 * eighth UI state folded into an existing one; it is the fail-safe the escalation trigger in
 * task 021's POML names ("if `sprk_filesummarystatus` returns a value outside the seven
 * enumerated options, escalate rather than adding a catch-all branch"). `DocumentProfileSection`
 * renders it as an explicit error, never as blank fields.
 */
export function summaryStatusFromCode(code: number | null | undefined): DocumentSummaryStatusName | 'Unrecognized' {
  if (code === null || code === undefined) {
    return 'None';
  }
  return STATUS_CODE_TO_NAME[code] ?? 'Unrecognized';
}

/**
 * User-facing message for each non-Completed state. `Completed` has no entry here — that state
 * renders the four profile fields instead of a message (see `DocumentProfileSection`).
 */
export const DOCUMENT_SUMMARY_STATUS_MESSAGES: Readonly<
  Record<Exclude<DocumentSummaryStatusName, 'Completed'>, string>
> = {
  None: 'This document has not been profiled yet.',
  Pending: 'AI profiling is in progress. Check back shortly.',
  OptedOut: 'AI profiling was opted out for this document.',
  Failed: 'AI profiling failed for this document.',
  NotSupported: 'This file type does not support AI profiling.',
  Skipped: 'AI profiling was skipped for this document.',
};
