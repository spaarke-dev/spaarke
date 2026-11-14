/**
 * Manage Access's notes bell (unified-access-control-r2 task 196): the informational messages are behind a header bell,
 * so a test that reads one opens the bell first and reads it inside the popover.
 */
import { fireEvent, screen, within } from '@testing-library/react';

/** The bell button (its accessible name carries the note count, e.g. "Access notes (2)"). */
export function accessNotesBell(): HTMLElement {
  return screen.getByRole('button', { name: /^Access notes \(\d+\)$/ });
}

/** Opens the bell once the access data has loaded (the bell reads "Access notes (loading)" until then) and returns the
 * popover. */
export async function openAccessNotes(): Promise<HTMLElement> {
  fireEvent.click(await screen.findByRole('button', { name: /^Access notes \(\d+\)$/ }));
  return screen.findByTestId('access-notes-popover');
}

/** The note in the open popover whose title is `title`, or `null`. */
export function accessNote(popover: HTMLElement, title: string): HTMLElement | null {
  const el = within(popover).queryByText(title, { selector: '.fui-MessageBarTitle' });
  return el ? (el.closest('.fui-MessageBar') as HTMLElement) : null;
}
