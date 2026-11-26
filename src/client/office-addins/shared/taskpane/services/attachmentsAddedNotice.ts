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

/** Task 141 review: the names in `attachmentsAdded` (the server's answer, never what was sent), or none when absent or malformed. */
export function readAttachmentsAdded(value: unknown): string[] {
  return readNames(value);
}

/**
 * Task 141 review: the attachments no email save ever creates a document for (`attachmentsNotSaveable`): their names, or
 * none when absent or malformed.
 */
export function readAttachmentsNotSaveable(value: unknown): string[] {
  return readNames(value);
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

/**
 * How the adds of this pane session ended, once their jobs have finished and the identity was re-read: `total` = the names
 * the adds queued, `notAdded` = how many of them the server does not list as saved (`null` = not known: no re-read answered).
 */
export interface AttachmentsAddOutcome {
  total: number;
  notAdded: number | null;
}

/** The sum of two outcomes (adds that overlapped); one that is not known makes the sum not known. */
export function combineAttachmentsAddOutcomes(
  first: AttachmentsAddOutcome | undefined,
  second: AttachmentsAddOutcome
): AttachmentsAddOutcome {
  if (!first) return second;
  return {
    total: first.total + second.total,
    notAdded: first.notAdded === null || second.notAdded === null ? null : first.notAdded + second.notAdded,
  };
}

/**
 * The confirmation title once an add's job has FINISHED: what happened, from what the server then lists as saved. `null` when
 * the outcome is not known (the caller keeps its plain title) — it never claims "added" for what it cannot confirm.
 */
export function describeAttachmentsAddFinished(outcome: AttachmentsAddOutcome | undefined): string | null {
  if (!outcome || outcome.total < 1 || outcome.notAdded === null) return null;
  const { total, notAdded } = outcome;
  const added = total - notAdded;
  if (notAdded <= 0) {
    return total === 1 ? '1 attachment added to the saved email' : `${total} attachments added to the saved email`;
  }
  if (added <= 0) {
    return notAdded === 1
      ? '1 attachment could not be added to the saved email.'
      : `${notAdded} attachments could not be added to the saved email.`;
  }
  return `${added} of ${total} attachments added to the saved email. ${notAdded} could not be added.`;
}

/** The warning for attachments the stored email does not hold, or `null` when there are none. */
export function describeAttachmentsNotStored(names: readonly string[] | undefined): string | null {
  if (!names || names.length < 1) return null;
  const list = names.join(', ');
  return names.length === 1
    ? `This attachment could not be added because it was not saved with the email: ${list}. To include it, choose "Save again as a new document", which saves a second copy of the email.`
    : `These attachments could not be added because they were not saved with the email: ${list}. To include them, choose "Save again as a new document", which saves a second copy of the email.`;
}

/**
 * Task 141 review: the warning for attachments no email save ever creates a document for (a calendar file, a blocked type,
 * an inline or signature image, an empty file), or `null` when there are none. Saving the email again would not save them,
 * so it is not suggested.
 */
export function describeAttachmentsNotSaveable(names: readonly string[] | undefined): string | null {
  if (!names || names.length < 1) return null;
  const list = names.join(', ');
  return names.length === 1
    ? `This attachment can't be saved to Spaarke (its type or content is not saved with emails): ${list}.`
    : `These attachments can't be saved to Spaarke (their type or content is not saved with emails): ${list}.`;
}
