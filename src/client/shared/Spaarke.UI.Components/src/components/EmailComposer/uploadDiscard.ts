/**
 * uploadDiscard.ts - the one gate every "remove the document this composer uploaded" call goes through
 * (spaarke-ontology-platform-r1 task 220, #2813).
 *
 * A local file is uploaded as a `sprk_document` as soon as it is picked; the upload hands back a `discard` bound to that
 * one row (`IUploadedLocalAttachment.discard`), and the attachment item carries it (`IAttachmentItem.discard`) wherever
 * the item travels: a compose-only wizard step lifts it into the wizard's state and restores it when the step reopens.
 * Picked and host-passed documents have no `discard`, so they can never be deleted through here.
 *
 * Rules, keyed on the `discard` function itself (it travels with the item through every copy of the state):
 * - **kept** (for good): a send that carried the document and went out, or may have (no response, 5xx, success without
 *   an id), keeps it. A sent email's `sprk_communicationattachment` row is RemoveLink on document delete — the document
 *   would be silently unlinked, not refused — and its body may link to the document. A keep is never undone: a later
 *   attempt that provably sent nothing does not release what an earlier attempt kept.
 * - **held** (while the outcome is unknown): a wizard's Finish holds its email's uploads from its first moment (record
 *   creation runs before the send), and a send holds what it carries. A delete asked for while held (the wizard closed
 *   during "Creating…") is deferred; when the last hold ends it runs, unless the document was kept meanwhile.
 * - **once**: a delete runs at most once, however many paths ask for it (removal in the step, the wizard closing).
 *
 * Deletes are best effort: a rejected delete is logged and the document stays, exactly as before this cleanup existed.
 */

import * as React from 'react';
import type { IComposedEmail } from './composeOnlySend';

type Discard = () => Promise<void>;

const kept = new WeakSet<Discard>();
const done = new WeakSet<Discard>();
const holds = new WeakMap<Discard, number>();
const deferred = new WeakSet<Discard>();

/** Keep these uploads for good: a send that carried them went out or may have. */
export function keepUploads(discards: ReadonlyArray<Discard | undefined>): void {
  for (const d of discards) if (d) kept.add(d);
}

/** A hold on some uploads; {@link IUploadHold.release} ends it (call it once, in a `finally`). */
export interface IUploadHold {
  release(): void;
}

/** Hold these uploads until `release()`: no delete runs meanwhile; one asked for is run at release unless kept. */
export function holdUploads(discards: ReadonlyArray<Discard | undefined>): IUploadHold {
  const held = discards.filter((d): d is Discard => !!d);
  for (const d of held) holds.set(d, (holds.get(d) ?? 0) + 1);
  let released = false;
  return {
    release() {
      if (released) return;
      released = true;
      for (const d of held) {
        const left = (holds.get(d) ?? 1) - 1;
        if (left > 0) {
          holds.set(d, left);
          continue;
        }
        holds.delete(d);
        if (deferred.has(d)) {
          deferred.delete(d);
          discardUpload(d);
        }
      }
    },
  };
}

/** Remove one upload's document, unless a send kept it or it was already removed; deferred while it is held. */
export function discardUpload(discard: Discard | undefined): void {
  if (!discard || kept.has(discard) || done.has(discard)) return;
  if ((holds.get(discard) ?? 0) > 0) {
    deferred.add(discard);
    return;
  }
  done.add(discard);
  void Promise.resolve()
    .then(discard)
    .catch((err: unknown) => console.warn('[EmailComposer] could not remove an uploaded attachment document:', err));
}

/** Every upload this email carries (attached or only linked: a body link names the document too). */
function emailDiscards(email: IComposedEmail): Array<Discard | undefined> {
  return email.attachments.filter(a => !!a.documentId).map(a => a.discard);
}

/**
 * A wizard's Finish: hold the Send Email step's uploads from its very first moment until it ends (task 220, #2813).
 * Record creation runs before the send, and the wizard can be closed meanwhile; without the hold, the close would delete
 * documents the send is about to carry. Call it synchronously as the Finish starts: `onFinish={() =>
 * holdComposedEmailUploadsDuring(emailRef.current, handleFinish)}`. When the Finish ends without sending (or the send
 * provably sent nothing) and the wizard was closed meanwhile, the deferred delete runs then.
 */
export async function holdComposedEmailUploadsDuring<T>(email: IComposedEmail, run: () => Promise<T>): Promise<T> {
  const hold = holdUploads(emailDiscards(email));
  try {
    return await run();
  } finally {
    hold.release();
  }
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
