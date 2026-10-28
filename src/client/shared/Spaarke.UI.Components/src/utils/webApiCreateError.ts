/**
 * Turning what a create wizard's save THROWS into text for the user (issue #2382).
 *
 * `Xrm.WebApi` rejects with `{ errorCode, message, ... }`. For a missing privilege (0x80040220) its `message` is
 * Dataverse's template — "{0} with id {1} does not have {2} right(s) for record with id {3} of entity {4} ..." — with
 * the placeholders unfilled, which tells the user nothing. A privilege or access error therefore becomes one plain
 * sentence that names no ids, users, tables or privilege names; any other error keeps its own message, cleaned of an
 * unfilled template. The caller logs the full error object with `console.error` (the detail lives there, not on screen).
 *
 * Which sentence: the privilege the raw message names decides it, and the raw text is never shown.
 * - a Create privilege             -> the user cannot create this record;
 * - an AppendTo or Append privilege -> the user cannot link one of the selected values;
 * - none named (an unfilled template, a bare code) -> a neutral sentence about saving with the selected values.
 *
 * The BFF-routed wizards (event, invoice, report card, to do, work assignment) get the BFF's refusal instead: a 403
 * ProblemDetails with `reasonCode: 'child_record.denied'`, thrown as a {@link ChildRecordWriteError} (`reasonCode` on the
 * error) or, through `@spaarke/auth`'s `authenticatedFetch`, as an `ApiError` (`problemDetails.reasonCode`). Its detail
 * names the table and privilege, so it gets the neutral sentence.
 */

import type { ChildRecordWriteError } from './adapters/bffChildWriteAdapter';

/** Dataverse's "principal lacks the privilege / access right" code, as `errorCode` carries it (signed or unsigned). */
const ACCESS_DENIED_CODES: ReadonlySet<number> = new Set([0x80040220, 0x80040220 - 0x100000000]);

/** Message shapes of a privilege or access refusal, for errors that carry no numeric code. */
const ACCESS_DENIED_PATTERNS: readonly RegExp[] = [
  /does not have .{0,40}right\(s\)/i,
  /\bis missing prv\w+ privilege/i,
  /\bSecLib::/i,
  /\bprincipal (user|team) .{0,80}\bmissing\b/i,
];

/** The BFF child-record route's reasonCode for a refusal by the caller's own rights (ChildRecordEndpoints.DeniedCode). */
const BFF_CHILD_RECORD_DENIED = 'child_record.denied';

/**
 * The privilege a refusal names, as Dataverse writes it: `prvAppendToContact`, `prvCreatesprk_Project`, or the filled
 * "does not have AppendToAccess right(s)" form. AppendTo is listed before Append (it starts with it).
 */
const PRIVILEGE_TOKEN =
  /\bprv(AppendTo|Append|Create)[A-Za-z_]|\bdoes not have (AppendTo|Append|Create)(?:Access)? right\(s\)/i;

/** An unfilled .NET format placeholder ("{0}", "{6}") — the message is a template, not a sentence. */
const UNFILLED_PLACEHOLDER = /\{\d+\}/;

/** What the wizard shows, and whether it was a privilege or access refusal. */
export interface IWebApiCreateErrorText {
  /** The text for the user. */
  message: string;
  /** True for a privilege or access refusal; `message` is then the full sentence (no "Failed to create" prefix needed). */
  isAccessDenied: boolean;
}

function codeOf(err: unknown): number | undefined {
  if (typeof err !== 'object' || err === null) return undefined;
  const e = err as { errorCode?: unknown; code?: unknown };
  for (const value of [e.errorCode, e.code]) {
    if (typeof value === 'number') return value;
    if (typeof value === 'string' && /^0x[0-9a-f]+$/i.test(value.trim())) return parseInt(value.trim(), 16);
  }
  return undefined;
}

/** The `reasonCode` of a BFF refusal: on a ChildRecordWriteError, or in an ApiError's ProblemDetails. */
function reasonCodeOf(err: unknown): string | undefined {
  if (typeof err !== 'object' || err === null) return undefined;
  const e = err as Partial<Pick<ChildRecordWriteError, 'reasonCode'>> & { problemDetails?: unknown };
  if (typeof e.reasonCode === 'string') return e.reasonCode;
  const pd = e.problemDetails;
  if (typeof pd === 'object' && pd !== null) {
    const code = (pd as { reasonCode?: unknown }).reasonCode;
    if (typeof code === 'string') return code;
  }
  return undefined;
}

function neutralSentence(recordNoun: string): string {
  return `You don't have permission to save this ${recordNoun} with the values you selected. Ask your administrator.`;
}

/** The sentence for a Dataverse privilege refusal, chosen from the privilege its raw message names. */
function privilegeSentence(raw: string, recordNoun: string): string {
  const m = PRIVILEGE_TOKEN.exec(raw);
  const kind = (m?.[1] ?? m?.[2] ?? '').toLowerCase();
  if (kind === 'create') return `You don't have permission to create this ${recordNoun}. Ask your administrator.`;
  if (kind === 'appendto' || kind === 'append') {
    return `You don't have permission to link one of the selected values to this ${recordNoun}. Ask your administrator.`;
  }
  return neutralSentence(recordNoun);
}

function messageOf(err: unknown): string {
  if (typeof err === 'string') return err;
  if (typeof err === 'object' && err !== null) {
    const m = (err as { message?: unknown }).message;
    if (typeof m === 'string') return m;
  }
  return '';
}

/**
 * Describe a create (or update) failure for the user.
 *
 * @param err        What the save threw (an `Xrm.WebApi` error object, an `Error`, or anything else).
 * @param recordNoun The record being saved, as the sentence names it ("project", "matter", "to do").
 */
export function describeWebApiCreateError(err: unknown, recordNoun: string): IWebApiCreateErrorText {
  const raw = messageOf(err).trim();
  const code = codeOf(err);

  if (reasonCodeOf(err) === BFF_CHILD_RECORD_DENIED) {
    return { message: neutralSentence(recordNoun), isAccessDenied: true };
  }

  if ((code !== undefined && ACCESS_DENIED_CODES.has(code)) || ACCESS_DENIED_PATTERNS.some(p => p.test(raw))) {
    return { message: privilegeSentence(raw, recordNoun), isAccessDenied: true };
  }

  if (!raw || UNFILLED_PLACEHOLDER.test(raw)) {
    return { message: 'Unknown error', isAccessDenied: false };
  }

  return { message: raw, isAccessDenied: false };
}
