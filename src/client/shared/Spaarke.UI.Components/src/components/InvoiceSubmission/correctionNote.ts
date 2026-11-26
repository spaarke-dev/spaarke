/**
 * The correction note of a corrected invoice (ebilling-r1 task 095f-2e2b; EB-D-245).
 *
 * The note rules are the server's (`InvoiceCorrectionNote`, and the multipart route's `correctionNote` field of 095f-2e2a): a Correction
 * NEEDS a note saying why the invoice is corrected; the server trims it, counts at most 2000 characters after trimming, and refuses a
 * control character other than a line break or a tab. The same limit and the same control-character rule drive the optional Note box of
 * Invoice Review's decision forms (its `services/noteModel.ts`, copied rather than imported across the two packages: that file lives in a
 * code page, this one in the shared library). The box here never cuts what is typed (no `maxLength`, ISS-027): it counts, says when the
 * note is over the limit, and holds Submit off. The server's 400 stays the backstop.
 *
 * Pure functions, no React: the wizard and the tests read the same words.
 */

/** The most characters a note may have after trimming (`InvoiceCorrectionNote.MaxLength`). */
export const CORRECTION_NOTE_MAX_CHARS = 2000;

/** Control characters other than a line break (LF, CR) or a tab, which the server refuses in a note (its `char.IsControl`: C0, DEL and C1). */
// eslint-disable-next-line no-control-regex
const FORBIDDEN_CONTROL = /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]/;

/** How many characters count against the limit (the server counts after trimming). */
export function correctionNoteCount(note: string | null | undefined): number {
  return (note ?? '').trim().length;
}

/** The note as it is sent: trimmed. An empty result means "no note", which a Correction cannot do without. */
export function correctionNoteToSend(note: string | null | undefined): string {
  return (note ?? '').trim();
}

/**
 * Why the note cannot be sent, or null when it can. A Correction's note is REQUIRED, so a blank one is a problem too (the box shows
 * the message only after something was typed, never on an untouched box).
 */
export function correctionNoteProblem(note: string | null | undefined): string | null {
  const count = correctionNoteCount(note);
  if (count === 0) return 'Say why this invoice is corrected.';
  if (count > CORRECTION_NOTE_MAX_CHARS)
    return `The note may be at most ${CORRECTION_NOTE_MAX_CHARS.toLocaleString('en-US')} characters.`;
  if (FORBIDDEN_CONTROL.test(note ?? '')) {
    return 'The note contains a control character that cannot be saved. Remove it and try again.';
  }
  return null;
}

/** The counter under the box, for example `12 / 2,000 characters`. */
export function correctionNoteCounterText(note: string | null | undefined): string {
  return `${correctionNoteCount(note).toLocaleString('en-US')} / ${CORRECTION_NOTE_MAX_CHARS.toLocaleString('en-US')} characters`;
}
