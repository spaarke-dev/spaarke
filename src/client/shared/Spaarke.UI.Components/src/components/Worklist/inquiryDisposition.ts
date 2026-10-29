/**
 * inquiryDisposition - what the worklist knows about the D-111 disposition picker: which Do-lane row can have one, the four
 * dispositions with their option values, and the plain-words reason for each way recording one can fail.
 *
 * A Do row is a candidate only when the route says its subject is a To Do (`subjectRecordType`, task 154). Whether that To Do is an
 * inquiry-outcome To Do is the server's to say (`GET /api/v1/inquiries/for-todo/{todoId}`): the widget derives nothing from the To Do's
 * name or notes and reads no Dataverse (D-15). The disposition is always a person's choice: nothing here preselects or infers one (ADR-013).
 *
 * Task: spaarke-ontology-platform-r1, task 155.
 */

import { InquiryRequestError } from './worklistService';
import type { WorklistRouteItem } from './worklistService';

/** The table (logical name) of the To Do an inquiry-outcome Signal's subject is. The one table the picker is offered for. */
export const INQUIRY_TODO_SUBJECT_TYPE = 'sprk_todo';

export interface DispositionOption {
  /** `sprk_servicerequest.sprk_disposition` option value. */
  value: number;
  label: string;
}

/** The four dispositions (task 071), in the order they are offered. */
export const DISPOSITION_OPTIONS: readonly DispositionOption[] = [
  { value: 100000000, label: 'Write-off' },
  { value: 100000001, label: 'Budget Revised' },
  { value: 100000002, label: 'Scope Approved' },
  { value: 100000003, label: 'No Action' },
];

/**
 * The To Do id to look an inquiry up for, or null when this row can never have a picker: it is not a Do-lane row, or its subject is
 * not a To Do (a Decide row carries no subject reference at all).
 */
export function inquiryTodoIdOf(item: WorklistRouteItem): string | null {
  if (item.lane !== 'Do') return null;
  if (item.subjectRecordType !== INQUIRY_TODO_SUBJECT_TYPE) return null;
  return item.subjectRecordId ?? null;
}

/** Shown when the inquiry lookup failed with anything but "no inquiry": the To Do is not opened, because it might be an inquiry's. */
export const INQUIRY_LOOKUP_FAILED = 'This To Do could not be opened because its inquiry could not be read. Try again.';

/**
 * Shown after a recorded outcome whose To Do was completed. The row stays until the nightly check closes its Signal.
 * Stopgap: #1991 (DI-101) closes the Signal in the same call; remove this wording and the session set in WorklistWidget then.
 */
export const OUTCOME_RECORDED_TODO_DONE =
  "Outcome recorded. This item stays in the list until tonight's check closes it.";

/** Shown after a recorded outcome whose To Do the server could not complete. */
export const OUTCOME_RECORDED_TODO_OPEN =
  'Outcome recorded, but the To Do could not be completed. Complete it from the To Do.';

/** The 409 reason code for an inquiry that already has an outcome: trying again can never succeed. */
export const ALREADY_RESOLVED_REASON = 'inquiry.already_resolved';

/** True when a failed record is "an outcome already exists": the picker offers close-and-refresh instead of another try. */
export function isAlreadyResolved(err: unknown): boolean {
  return err instanceof InquiryRequestError && err.httpStatus === 409 && err.reasonCode === ALREADY_RESOLVED_REASON;
}

const GENERIC_FAILURE = "The outcome couldn't be recorded. Try again.";

/** Plain words for a failed `POST .../disposition`: 403, 404 and each 409 reason code (task 071); anything else is a generic try-again. */
export function dispositionFailureMessage(err: unknown): string {
  if (!(err instanceof InquiryRequestError)) return GENERIC_FAILURE;
  switch (err.httpStatus) {
    case 403:
      return "You don't have permission to record the outcome of this inquiry.";
    case 404:
      return "This inquiry couldn't be found, or you no longer have access to it. Refresh the worklist.";
    case 400:
      return "That outcome or reply isn't valid. Check your choices and try again.";
    case 409:
      switch (err.reasonCode) {
        case ALREADY_RESOLVED_REASON:
          return 'An outcome has already been recorded for this inquiry.';
        case 'inquiry.reply_mismatch':
          return "That reply isn't filed against this inquiry. Choose another reply.";
        case 'inquiry.not_a_reply':
          return "That message isn't a reply received from the other side. Choose another reply.";
        case 'inquiry.not_an_inquiry':
          return "This isn't an open inquiry we sent, so no outcome can be recorded for it.";
        default:
          return "The outcome couldn't be recorded because the inquiry has changed. Refresh the worklist.";
      }
    default:
      return GENERIC_FAILURE;
  }
}
