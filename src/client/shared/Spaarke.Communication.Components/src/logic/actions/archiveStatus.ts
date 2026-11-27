/**
 * The status line for a successful "Save to SharePoint" (POST /communications/{id}/archive).
 *
 * The archive refuses an attachment whose SharePoint Embedded item it cannot verify (#2246, PR #2248) and counts
 * it in `attachmentsRefusedUnverified`. A refusal is never shown as a plain success: the line says how many
 * attachments were not saved, and the bar turns to a warning. One copy for both "Save to SharePoint" buttons: the
 * `CommunicationActions` PCF and `EmailWorkspace`.
 */

/** The fields of `ArchiveCommunicationResult` the status line reads (camelCase JSON). */
export interface IArchiveResult {
  alreadyArchived?: boolean;
  attachmentDocumentsCreated?: number;
  attachmentsRefusedUnverified?: number;
}

export interface IArchiveStatus {
  message: string;
  intent: 'success' | 'warning';
}

/** "1 attachment could not be verified and was not saved." / "N attachments … were not saved." — or null for none. */
export function refusedAttachmentsNotice(result: IArchiveResult): string | null {
  const refused = result.attachmentsRefusedUnverified ?? 0;
  if (!(refused > 0)) return null;
  return refused === 1
    ? '1 attachment could not be verified and was not saved.'
    : `${refused} attachments could not be verified and were not saved.`;
}

export function archiveStatus(result: IArchiveResult): IArchiveStatus {
  const created = result.attachmentDocumentsCreated ?? 0;
  const saved = result.alreadyArchived
    ? 'Already saved to SharePoint.'
    : `Saved to SharePoint${created ? ` (+${created} attachment${created === 1 ? '' : 's'})` : ''}.`;
  const refused = refusedAttachmentsNotice(result);
  return refused ? { message: `${saved} ${refused}`, intent: 'warning' } : { message: saved, intent: 'success' };
}

/**
 * The reason code of a "Save to SharePoint" refused while another archive of the same email runs (409; ISS-010 part E: the
 * post-send job archives a decision email in the background).
 */
export const ARCHIVE_IN_PROGRESS_CODE = 'archive_in_progress';

/** What a 409 `archive_in_progress` says when the server sent no detail. Not a failure: the email is being saved. */
export const ARCHIVE_IN_PROGRESS_TEXT = 'This email is already being saved to SharePoint. Try again shortly.';

export interface IArchiveFailureNotice {
  message: string;
  intent: 'info' | 'error';
}

/** The problem's reason code, wherever the server put it (`reasonCode`, `errorCode`, `code`, `extensions.code`, or the type URI). */
function problemCode(problem: Record<string, unknown>): string | undefined {
  const extensions = problem.extensions as Record<string, unknown> | undefined;
  for (const value of [problem.reasonCode, problem.errorCode, problem.code, extensions?.code]) {
    if (typeof value === 'string' && value) return value;
  }
  return typeof problem.type === 'string' ? problem.type.split('/').pop() : undefined;
}

/**
 * The line for a refused "Save to SharePoint" (any non-success answer): the server's problem `detail` when it sent one. A 409
 * `archive_in_progress` is an info line that says the email is already being saved; anything else is an error line.
 */
export function archiveFailureNotice(status: number | undefined, problem: unknown): IArchiveFailureNotice {
  const body = typeof problem === 'object' && problem !== null ? (problem as Record<string, unknown>) : {};
  const detail = typeof body.detail === 'string' && body.detail.trim() ? body.detail.trim() : undefined;
  if (status === 409 && problemCode(body) === ARCHIVE_IN_PROGRESS_CODE) {
    return { message: detail ?? ARCHIVE_IN_PROGRESS_TEXT, intent: 'info' };
  }
  const code = status ? ` (${status})` : '';
  return {
    message: detail ? `Save to SharePoint failed${code}: ${detail}` : `Save to SharePoint failed${code}.`,
    intent: 'error',
  };
}
