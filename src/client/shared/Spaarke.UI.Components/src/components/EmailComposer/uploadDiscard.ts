/**
 * uploadDiscard.ts - the one gate every "remove the document this composer uploaded" call goes through
 * (spaarke-ontology-platform-r1 task 220, #2813).
 *
 * A local file is uploaded as a `sprk_document` as soon as it is picked; the upload hands back a `discard` bound to that
 * one row (`IUploadedLocalAttachment.discard`), and the attachment item carries it (`IAttachmentItem.discard`) wherever
 * the item travels: a compose-only wizard step lifts it into the wizard's state and restores it when the step reopens.
 * Picked and host-passed documents have no `discard`, so they can never be deleted through here.
 *
 * Two rules, keyed on the `discard` function itself (it travels with the item through every copy of the state):
 * - **kept**: a send that carried the document and succeeded, or may have gone out (no response, 5xx, success without
 *   an id), keeps it for good (a sent email's `sprk_communicationattachment` row is RemoveLink on document delete — it
 *   would be silently unlinked, not refused). A send that provably sent nothing releases it again.
 * - **once**: a delete runs at most once, however many paths ask for it (removal in the step, the wizard closing).
 *
 * Deletes are best effort: a rejected delete is logged and the document stays, exactly as before this cleanup existed.
 */

import * as React from 'react';
import type { IComposedEmail } from './composeOnlySend';

type Discard = () => Promise<void>;

const kept = new WeakSet<Discard>();
const done = new WeakSet<Discard>();

/** Keep these uploads for good, or until {@link releaseUploads} (a send that carried them may have delivered them). */
export function keepUploads(discards: ReadonlyArray<Discard | undefined>): void {
  for (const d of discards) if (d) kept.add(d);
}

/** A send that provably sent nothing: its uploads may be removed again. */
export function releaseUploads(discards: ReadonlyArray<Discard | undefined>): void {
  for (const d of discards) if (d) kept.delete(d);
}

/** Remove one upload's document, unless a send kept it or it was already removed. Never throws. */
export function discardUpload(discard: Discard | undefined): void {
  if (!discard || kept.has(discard) || done.has(discard)) return;
  done.add(discard);
  void Promise.resolve()
    .then(discard)
    .catch((err: unknown) => console.warn('[EmailComposer] could not remove an uploaded attachment document:', err));
}

/**
 * For a wizard that holds an {@link IComposedEmail} for its Send Email step: when the wizard closes (`open` turns false,
 * or it unmounts while open) — with or without Finish — delete the documents the step uploaded that no sent email
 * references (task 220, #2813; the same rule as `discardComposedEmailUploads`). It runs on close, never on reopen, so a
 * wizard that resets its state when it opens never loses the last email's uploads to the reset.
 */
export function useDiscardComposedEmailUploadsOnClose(
  open: boolean,
  emailRef: React.MutableRefObject<IComposedEmail>
): void {
  const wasOpen = React.useRef(open);
  React.useEffect(() => {
    if (wasOpen.current && !open) discardEmail(emailRef.current);
    wasOpen.current = open;
  }, [open, emailRef]);
  React.useEffect(
    () => () => {
      if (wasOpen.current) discardEmail(emailRef.current);
    },
    [emailRef]
  );
}

function discardEmail(email: IComposedEmail): void {
  for (const a of email.attachments) discardUpload(a.discard);
}
