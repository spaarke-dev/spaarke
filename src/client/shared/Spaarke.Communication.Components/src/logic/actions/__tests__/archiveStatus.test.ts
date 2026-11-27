/**
 * "Save to SharePoint" status line — built from the archive response (ArchiveCommunicationResult).
 *
 * PR #2248 (#2246): the archive refuses an attachment whose item it cannot verify and counts it in
 * `attachmentsRefusedUnverified`. Before the fix the button said "Saved to SharePoint." or "Already saved to
 * SharePoint." regardless, so a refused attachment was a silent success.
 */
import {
  ARCHIVE_IN_PROGRESS_TEXT,
  archiveFailureNotice,
  archiveStatus,
  refusedAttachmentsNotice,
} from '../archiveStatus';

describe('archiveStatus', () => {
  it('a plain save is a success line', () => {
    expect(archiveStatus({ alreadyArchived: false, attachmentDocumentsCreated: 0 })).toEqual({
      message: 'Saved to SharePoint.',
      intent: 'success',
    });
  });

  it('counts the attachments saved', () => {
    expect(archiveStatus({ attachmentDocumentsCreated: 1 }).message).toBe('Saved to SharePoint (+1 attachment).');
    expect(archiveStatus({ attachmentDocumentsCreated: 3 }).message).toBe('Saved to SharePoint (+3 attachments).');
  });

  it('an already-archived communication with nothing refused is a success line', () => {
    expect(
      archiveStatus({ alreadyArchived: true, attachmentDocumentsCreated: 0, attachmentsRefusedUnverified: 0 })
    ).toEqual({
      message: 'Already saved to SharePoint.',
      intent: 'success',
    });
  });

  it('a refused attachment is said, and the line is a warning', () => {
    expect(
      archiveStatus({ alreadyArchived: true, attachmentDocumentsCreated: 0, attachmentsRefusedUnverified: 1 })
    ).toEqual({
      message: 'Already saved to SharePoint. 1 attachment could not be verified and was not saved.',
      intent: 'warning',
    });
  });

  it('several refused alongside saved attachments', () => {
    expect(
      archiveStatus({ alreadyArchived: false, attachmentDocumentsCreated: 2, attachmentsRefusedUnverified: 3 })
    ).toEqual({
      message: 'Saved to SharePoint (+2 attachments). 3 attachments could not be verified and were not saved.',
      intent: 'warning',
    });
  });

  it('a server without the field (older BFF) reads as none refused', () => {
    expect(refusedAttachmentsNotice({ alreadyArchived: true })).toBeNull();
  });
});

describe('archiveFailureNotice (ISS-010 part E run 3)', () => {
  it('a 409 archive_in_progress is an info line with the server detail', () => {
    expect(
      archiveFailureNotice(409, {
        detail: 'This email is being saved to SharePoint already. Try again shortly.',
        extensions: { code: 'archive_in_progress' },
      })
    ).toEqual({ message: 'This email is being saved to SharePoint already. Try again shortly.', intent: 'info' });
  });

  it('a 409 archive_in_progress without a detail still says it is being saved, not failed', () => {
    expect(archiveFailureNotice(409, { type: 'https://spaarke.com/errors/archive_in_progress' })).toEqual({
      message: ARCHIVE_IN_PROGRESS_TEXT,
      intent: 'info',
    });
  });

  it('another 409 is a failure', () => {
    expect(
      archiveFailureNotice(409, { detail: 'Owner refused.', extensions: { code: 'record_owner_unresolved' } })
    ).toEqual({
      message: 'Save to SharePoint failed (409): Owner refused.',
      intent: 'error',
    });
  });

  it('no answer and no detail is a plain failure line', () => {
    expect(archiveFailureNotice(undefined, null)).toEqual({ message: 'Save to SharePoint failed.', intent: 'error' });
  });
});
