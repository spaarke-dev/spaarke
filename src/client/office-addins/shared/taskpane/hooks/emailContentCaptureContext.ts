/**
 * How `useSaveFlow` gets the open email's content (spaarkeai-word-add-in-r1 task 116a).
 *
 * The Outlook save reads the body and the attachments in the add-in at the moment it submits
 * (`services/emailContentCapture.ts` says why). The reader needs the live host adapter, which `SaveView` holds;
 * `useSaveFlow` runs inside `SaveFlow`, one component below. This context carries the capture function across that
 * component, so `SaveView` provides it and the hook calls it — the same "live function, invoked at submit" shape as
 * `SaveFlowContext.captureDocumentContent` for Word (task 045), without a pass-through prop on `SaveFlow`.
 *
 * Absent (the default — every caller that provides nothing, an Outlook client without Mailbox 1.8 read-mode
 * attachment access, and Word): the save sends no content and the server fetches the email from Graph, exactly as
 * before this task.
 */
import { createContext } from 'react';
import type { AttachmentInfo } from '@shared/adapters/types';
import type { EmailContentCapture } from '../services/emailContentCapture';

/**
 * Reads the email for ONE save attempt: the body and every attachment (for the complete `.eml`); `selectedIds` are the
 * ticked ones — they get the budget first and are the only ones named to become documents. Rejects (with a
 * user-readable message) when the body or a ticked attachment could not be read; the save then sends nothing.
 */
export type CaptureEmailContent = (
  attachments: readonly AttachmentInfo[],
  selectedIds: ReadonlySet<string>
) => Promise<EmailContentCapture>;

export const EmailContentCaptureContext = createContext<CaptureEmailContent | undefined>(undefined);
