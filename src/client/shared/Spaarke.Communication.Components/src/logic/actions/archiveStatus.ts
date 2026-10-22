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
