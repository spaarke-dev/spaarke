/**
 * The optional Note on a decision (ebilling-r1 task 095f-2ui; design 095f-part2 section 1.2 and 7). One place for what every Note box and
 * every decision call shares, so the page and the services read the same words:
 *
 *   - a blank or whitespace-only note is no note: the key is left out of the request, never sent as null or "";
 *   - a note that is sent is the text the user typed (the server trims it, counts at most 2000 characters after trimming, and refuses a
 *     control character other than a line break or tab with 400 `ebilling.note_invalid_characters`);
 *   - the box never cuts what is typed or pasted (no `maxLength`, ISS-027): it counts, and says when the note is over the limit. The
 *     server's 400 is the backstop, and both of its note codes are shown under the Note box, not as a page error.
 *
 * The limit and the two codes are pinned to the server's source by `__tests__/serverWordsPin.test.ts`.
 */

import type { ApiFailure } from './invoiceReviewApi';

/** The most characters a note may have after trimming (`InvoiceCorrectionNote.MaxLength`, the reason limit every reviewer text shares). */
export const NOTE_MAX_CHARS = 2000;

/** 400: the note is longer than the limit (`InvoiceCorrectionNote.NoteTooLongCode`). */
export const CODE_NOTE_TOO_LONG = 'ebilling.note_too_long';
/** 400: the note holds a control character the server refuses (`InvoiceCorrectionNote.NoteInvalidCharactersCode`). */
export const CODE_NOTE_INVALID_CHARACTERS = 'ebilling.note_invalid_characters';
export const NOTE_ERROR_CODES: readonly string[] = [CODE_NOTE_TOO_LONG, CODE_NOTE_INVALID_CHARACTERS];

/** The note to send: the text as typed, or undefined when it is blank (a blank note is absent, never null or ""). The trim is for the blank check only. */
export function noteToSend(note: string | null | undefined): string | undefined {
  return note != null && note.trim().length > 0 ? note : undefined;
}

/** The request body members for a note: `{ note }` when there is one, else nothing (so the key is absent from the JSON). */
export function noteMember(note: string | null | undefined): { note?: string } {
  const n = noteToSend(note);
  return n === undefined ? {} : { note: n };
}

/** Trailing call arguments for a note: `[note]` when there is one, else none (an old call shape stays exactly as it was). */
export function noteArgs(note: string | null | undefined): [] | [string] {
  const n = noteToSend(note);
  return n === undefined ? [] : [n];
}

/** How many characters count against the limit (the server counts after trimming). */
export function noteCount(note: string | null | undefined): number {
  return (note ?? '').trim().length;
}

/** Why the note cannot be sent (the limit is the only thing the page checks; the server owns the rest), or null. */
export function noteProblem(note: string | null | undefined): string | null {
  return noteCount(note) > NOTE_MAX_CHARS ? `The note may be at most ${NOTE_MAX_CHARS} characters.` : null;
}

/** The counter under every Note box, for example `12 / 2,000 characters`. */
export function noteCounterText(note: string | null | undefined): string {
  return `${noteCount(note).toLocaleString('en-US')} / ${NOTE_MAX_CHARS.toLocaleString('en-US')} characters`;
}

/** True when the server refused the note itself (400 with one of its two note codes). */
export function isNoteRefusal(f: ApiFailure): boolean {
  return f.status === 400 && f.errorCode !== null && NOTE_ERROR_CODES.includes(f.errorCode);
}

/** The field message for a refused note: the server's own detail, or a fixed sentence for its code. */
export function noteRefusalMessage(f: ApiFailure): string {
  if (f.detail) return f.detail;
  return f.errorCode === CODE_NOTE_INVALID_CHARACTERS
    ? 'The note contains a control character that cannot be saved. Remove it and try again.'
    : `The note may be at most ${NOTE_MAX_CHARS} characters.`;
}
