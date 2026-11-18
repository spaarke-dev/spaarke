/**
 * Cross-frame side-pane signal (ontology task 194).
 *
 * A page running in an iframe (the Document Relationship Viewer code page) opens the host's lookup side pane
 * (`Xrm.Utility.lookupObjects`) in the window that owns Xrm, above the iframe. When that iframe sits inside a modal of
 * its parent page (Semantic Search's Find Similar dialog, rendered by a PCF in the Dataverse page), the pane opens
 * BEHIND that outer modal. The framed page says when a lookup opens and settles; the parent's modal then yields with
 * the existing `SprkModal` `yieldToSidePane` (`sidePaneLayering.ts`). This file is only the message: the layering stays
 * in one place.
 *
 * Same origin only: the message is posted to the frame's own origin, and the receiver accepts it only from its own
 * iframe's window.
 */

/** `type` of the message a framed page posts to its parent while a host lookup pane is open. */
export const SIDE_PANE_LOOKUP_MESSAGE_TYPE = 'sprk:side-pane-lookup';

/** The message: `open` is `true` when the lookup opens and `false` when it settles (pick, cancel or failure). */
export interface ISidePaneLookupMessage {
  type: typeof SIDE_PANE_LOOKUP_MESSAGE_TYPE;
  open: boolean;
}

/** True when `data` is a {@link ISidePaneLookupMessage}. */
export function isSidePaneLookupMessage(data: unknown): data is ISidePaneLookupMessage {
  if (!data || typeof data !== 'object') return false;
  const message = data as Partial<ISidePaneLookupMessage>;
  return message.type === SIDE_PANE_LOOKUP_MESSAGE_TYPE && typeof message.open === 'boolean';
}

/**
 * Tells the parent frame that a host lookup pane opened (`true`) or settled (`false`). Does nothing in a top-level
 * window (a page opened in its own tab) and never throws: a parent that does not listen (a Dataverse dialog opened
 * with `navigateTo`, where the pane already opens above the page) simply ignores the message.
 */
export function postSidePaneLookupToParent(open: boolean, win: Window = window): void {
  const parent = win.parent;
  if (parent === win) return;
  const message: ISidePaneLookupMessage = { type: SIDE_PANE_LOOKUP_MESSAGE_TYPE, open };
  try {
    parent.postMessage(message, win.location.origin);
  } catch (err) {
    // A detached parent cannot be told; the parent's modal then stays where it is, as before this signal existed.
    // eslint-disable-next-line no-restricted-syntax -- best-effort signal: a lookup must never fail because the parent frame cannot be told
    console.warn('[sidePaneFrameSignal] could not notify the parent frame', err);
  }
}
