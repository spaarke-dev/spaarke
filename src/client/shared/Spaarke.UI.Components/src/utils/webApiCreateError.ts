/**
 * Turning what a create wizard's save THROWS into text for the user (issue #2382).
 *
 * `Xrm.WebApi` rejects with `{ errorCode, message, ... }`. For a missing privilege (0x80040220) its `message` is
 * Dataverse's template — "{0} with id {1} does not have {2} right(s) for record with id {3} of entity {4} ..." — with
 * the placeholders unfilled, which tells the user nothing. A privilege or access error therefore becomes one plain
 * sentence that names no ids, users or privilege names; any other error keeps its own message, cleaned of an unfilled
 * template. The caller logs the full error object with `console.error` (the detail lives there, not on screen).
 */

/** Dataverse's "principal lacks the privilege / access right" code, as `errorCode` carries it (signed or unsigned). */
const ACCESS_DENIED_CODES: ReadonlySet<number> = new Set([0x80040220, 0x80040220 - 0x100000000]);

/** Message shapes of a privilege or access refusal, for errors that carry no numeric code. */
const ACCESS_DENIED_PATTERNS: readonly RegExp[] = [
  /does not have .{0,40}right\(s\)/i,
  /\bis missing prv\w+ privilege/i,
  /\bSecLib::/i,
  /\bprincipal (user|team) .{0,80}\bmissing\b/i,
];

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

  if ((code !== undefined && ACCESS_DENIED_CODES.has(code)) || ACCESS_DENIED_PATTERNS.some(p => p.test(raw))) {
    return {
      message: `You don't have permission to link one of the selected people or organizations to this ${recordNoun}. Ask your administrator.`,
      isAccessDenied: true,
    };
  }

  if (!raw || UNFILLED_PLACEHOLDER.test(raw)) {
    return { message: 'Unknown error', isAccessDenied: false };
  }

  return { message: raw, isAccessDenied: false };
}
