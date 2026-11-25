/**
 * attachmentsAddedNotice.ts
 *
 * Task 138 (#2209): the one place the pane turns the server's `SaveResponse.attachmentsAdded` and
 * `SaveResponse.attachmentsNotStored` into words. A re-save of an email already saved to the same record, whose selection
 * names attachments that were not saved the first time, QUEUES their creation under the saved email (the server lists their
 * file names in `attachmentsAdded`): the documents are made by a background job after the answer, so the pane says they are
 * being added and never that they are done. A selection may also name attachments the stored email does not hold at all
 * (`attachmentsNotStored`): those cannot be added, and the pane says so. Both properties are OMITTED on every other answer,
 * so an absent or malformed value means "nothing" and the pane keeps its usual wording.
 */

/** The names in a server list: its non-empty strings, or none when it is absent or not a list. Accepts `unknown` (a server body). */
function readNames(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  return value.filter((name): name is string => typeof name === 'string' && name.length > 0);
}

/**
 * How many attachments the save is adding to the already-saved email: the number of names in `attachmentsAdded`, or 0 when
 * it is absent or not a list.
 */
export function readAttachmentsAddedCount(value: unknown): number {
  return readNames(value).length;
}

/** The attachments the stored email does not hold (`attachmentsNotStored`): their names, or none when absent or malformed. */
export function readAttachmentsNotStored(value: unknown): string[] {
  return readNames(value);
}

/**
 * The confirmation title for an add that is QUEUED, or `null` when nothing is being added (the caller keeps its own title).
 * It never says "Added": the documents are created after this answer, and if they fail the user saves the email again.
 */
export function describeAttachmentsAdded(count: number | undefined): string | null {
  if (!count || count < 1) return null;
  return count === 1
    ? "Adding 1 attachment to the saved email. If it doesn't appear on the record in a few minutes, save the email again."
    : `Adding ${count} attachments to the saved email. If they don't appear on the record in a few minutes, save the email again.`;
}

/** The warning for attachments the stored email does not hold, or `null` when there are none. */
export function describeAttachmentsNotStored(names: readonly string[] | undefined): string | null {
  if (!names || names.length < 1) return null;
  const list = names.join(', ');
  return names.length === 1
    ? `This attachment could not be added because it was not saved with the email: ${list}. Save the email again as a new copy to include it.`
    : `These attachments could not be added because they were not saved with the email: ${list}. Save the email again as a new copy to include them.`;
}
