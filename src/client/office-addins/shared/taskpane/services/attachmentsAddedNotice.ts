/**
 * attachmentsAddedNotice.ts
 *
 * Task 138 (#2209): the one place the pane turns the server's `SaveResponse.attachmentsAdded` into words. A re-save of an
 * email already saved to the same record, whose selection names attachments that were not saved the first time, adds
 * exactly those attachments under the saved email; the server lists their file names. The property is OMITTED on every
 * other answer (a new save, a duplicate), so an absent or malformed value means "nothing was added" and the pane keeps
 * its usual "Saved to Spaarke" wording.
 */

/**
 * How many attachments the save added to the already-saved email: the number of names in `attachmentsAdded`, or 0 when it
 * is absent or not a list. Accepts `unknown` because it reads a server body.
 */
export function readAttachmentsAddedCount(value: unknown): number {
  if (!Array.isArray(value)) return 0;
  return value.filter(name => typeof name === 'string' && name.length > 0).length;
}

/** The confirmation title for an add, or `null` when nothing was added (the caller keeps its own title). */
export function describeAttachmentsAdded(count: number | undefined): string | null {
  if (!count || count < 1) return null;
  return count === 1 ? 'Added 1 attachment to the saved email' : `Added ${count} attachments to the saved email`;
}
