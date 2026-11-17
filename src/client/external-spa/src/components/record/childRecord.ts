/**
 * childRecord — the seam through which a record-template section opens ANOTHER record in a child modal over the shown one
 * (spaarke-SPA-external-access-platform-r4 task 013, owner retest 2026-11-14: an invoice row and a document name open their
 * record "on top of this modal").
 *
 * A section never imports the modal that hosts the child (that would make the section registry and `RecordTemplateModal`
 * import each other at load time): it reads the opener from this context, which `RecordTemplateModal` provides. The opener is
 * `null` in two places, and rows then behave as before (plain text): outside a template (a section drawn on its own, the
 * Project page's `DocumentLibrary`), and INSIDE a child record, so there is one child level only.
 */
import * as React from 'react';
import type { RecordSectionId } from './recordTemplate.types';

/**
 * Opens `recordId` of `entity` as a child record over the record being shown. `trigger` is the row control that was
 * activated (focus returns to it on close); `section` is the section to re-read on close. Leave it out when the child cannot
 * change what the parent section lists (a document record: its actions are Refresh, Download and Copy link), so the
 * parent's list keeps its state (a sorted grid is not remounted).
 */
export type OpenChildRecord = (
  entity: string,
  recordId: string,
  trigger: HTMLElement | null,
  section?: RecordSectionId
) => void;

export const ChildRecordContext = React.createContext<OpenChildRecord | null>(null);

/** The opener of the surrounding record, or `null` when rows must not open a record (see above). */
export function useChildRecordOpener(): OpenChildRecord | null {
  return React.useContext(ChildRecordContext);
}

/** Attribute on a row control that opens a child record, so focus can find the same row again after a re-read. */
export const CHILD_RECORD_ATTRIBUTE = 'data-child-record';

/** The attribute value of one row: `{entity}:{id}`. */
export const childRecordKeyOf = (entity: string, recordId: string): string => `${entity}:${recordId}`;

/** Spreadable attributes for the row control that opens `recordId` of `entity`. */
export function childRecordAttributesOf(entity: string, recordId: string): { 'data-child-record': string } {
  return { 'data-child-record': childRecordKeyOf(entity, recordId) };
}

/** How long a closed child waits for its row to be drawn again after the section re-read. */
const REFOCUS_WINDOW_MS = 8000;

/**
 * Puts focus back on the row that opened a child record. A child's close re-reads the parent section, which draws its rows
 * anew, so the activating element may be gone for a moment: this waits (up to a few seconds) for the row with the same key
 * and focuses it, unless the user has moved focus to something of their own meanwhile (only the page body, nothing, or a
 * dialog frame counts as "not moved").
 */
export function refocusChildRecordRow(key: string): void {
  if (typeof document === 'undefined') return;
  const selector = `[${CHILD_RECORD_ATTRIBUTE}="${key.replace(/"/g, '\\"')}"]`;
  let observer: MutationObserver | undefined;
  let timer: number | undefined;
  const stop = (): void => {
    observer?.disconnect();
    if (timer !== undefined) window.clearTimeout(timer);
  };
  const attempt = (): boolean => {
    const row = document.querySelector<HTMLElement>(selector);
    if (!row) return false;
    const active = document.activeElement;
    const unmoved = !active || active === document.body || active.matches('[role=dialog], [role=alertdialog]');
    if (active === row) return true;
    if (!unmoved) return true; // the user went elsewhere: leave focus alone
    row.focus();
    return document.activeElement === row;
  };
  // Let the close's own focus handling finish first, so this is the last word.
  window.setTimeout(() => {
    if (attempt()) return;
    if (typeof MutationObserver === 'undefined') return;
    observer = new MutationObserver(() => {
      if (attempt()) stop();
    });
    observer.observe(document.body, { childList: true, subtree: true });
    timer = window.setTimeout(stop, REFOCUS_WINDOW_MS);
  }, 0);
}
