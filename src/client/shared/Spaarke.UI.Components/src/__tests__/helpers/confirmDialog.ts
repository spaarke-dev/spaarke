/**
 * Clicks a button inside the open confirm dialog (`role="alertdialog"`).
 *
 * FAILURE-MODES G-21: in jsdom, Fluent's focus manager (tabster) can leave a stacked confirm dialog, or the modal under
 * it, `aria-hidden`. Testing Library's accessible-only role queries then miss the confirm, and "the last Revoke button
 * on screen" resolves to the row's own button instead of the confirm's. Query the confirm hidden-inclusive and scope
 * the button lookup to it, so nothing in the background can match. jsdom-only: in a real browser the surface is not
 * aria-hidden.
 */
import { fireEvent, screen, within } from '@testing-library/react';

export async function clickInConfirmDialog(buttonName: string): Promise<void> {
  const confirm = await screen.findByRole('alertdialog', { hidden: true });
  fireEvent.click(within(confirm).getByRole('button', { name: buttonName, hidden: true }));
}

/** Confirms a pending revoke: clicks "Revoke" inside the open confirm dialog (G-21-safe). */
export function confirmRevoke(): Promise<void> {
  return clickInConfirmDialog('Revoke');
}
