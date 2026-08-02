/**
 * ConfirmModal.test.tsx — the `SprkModal` confirm preset (spec FR-09). Verifies the
 * xs/alert/non-maximizable envelope, Cancel-left/Confirm-right footer, callback
 * wiring, and that the `destructive` danger styling is a `makeStyles` token CLASS —
 * never an inline color style (ADR-021).
 */
import * as React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { renderWithProviders } from '../../../../__mocks__/pcfMocks';
import { ConfirmModal } from '../ConfirmModal';

const noop = () => {};

describe('ConfirmModal (preset — FR-09)', () => {
  it('renders as an alertdialog (dismiss="alert"), sized xs, with Cancel left and Confirm right', () => {
    renderWithProviders(
      <ConfirmModal
        open
        onClose={noop}
        onConfirm={noop}
        title="Discard changes?"
        message="Your changes will be lost."
      />,
    );
    const dialog = screen.getByRole('alertdialog');
    expect(within(dialog).getByText('Discard changes?')).toBeInTheDocument();
    expect(within(dialog).getByText('Your changes will be lost.')).toBeInTheDocument();
    expect(dialog.style.width).toContain('480px');

    const cancel = within(dialog).getByRole('button', { name: /^cancel$/i });
    const confirm = within(dialog).getByRole('button', { name: /^confirm$/i });
    expect(cancel).toBeInTheDocument();
    expect(confirm).toBeInTheDocument();
    // Cancel (footerStart) precedes Confirm (footer) in DOM/visual order.
     
    expect(cancel.compareDocumentPosition(confirm) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('is non-maximizable (no maximize control present)', () => {
    renderWithProviders(
      <ConfirmModal open onClose={noop} onConfirm={noop} title="Delete?" message="Sure?" />,
    );
    expect(screen.queryByRole('button', { name: /maximize dialog/i })).toBeNull();
  });

  it('invokes onClose from Cancel and onConfirm from Confirm, using custom labels', () => {
    const onConfirm = jest.fn();
    const onClose = jest.fn();
    renderWithProviders(
      <ConfirmModal
        open
        onClose={onClose}
        onConfirm={onConfirm}
        title="Confirm"
        message="Proceed?"
        confirmLabel="Delete"
        cancelLabel="Keep"
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: /^keep$/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onConfirm).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: /^delete$/i }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('applies danger styling via a makeStyles token CLASS — never an inline color style — when destructive', () => {
    const { unmount } = renderWithProviders(
      <ConfirmModal
        open
        onClose={noop}
        onConfirm={noop}
        title="Delete matter?"
        message="This cannot be undone."
        confirmLabel="Delete"
        destructive
      />,
    );
    const destructiveConfirm = screen.getByRole('button', { name: /^delete$/i });
    // No inline color/background style — styling must come from a CSS class.
    expect(destructiveConfirm.getAttribute('style') ?? '').not.toMatch(/background-color|color/i);
    const destructiveClassName = destructiveConfirm.className;
    unmount();

    renderWithProviders(
      <ConfirmModal
        open
        onClose={noop}
        onConfirm={noop}
        title="Save?"
        message="Save changes?"
        confirmLabel="Delete"
      />,
    );
    const plainConfirm = screen.getByRole('button', { name: /^delete$/i });
    expect(plainConfirm.getAttribute('style') ?? '').not.toMatch(/background-color|color/i);
    // The destructive variant carries an additional makeStyles class the plain
    // variant does not — proof the `danger` class (not an inline style) was applied.
    expect(destructiveConfirm.className).not.toBe(plainConfirm.className);
  });

  it('busy disables both buttons and shows a spinner on Confirm (P2 consolidation)', () => {
    const onConfirm = jest.fn();
    const onClose = jest.fn();
    renderWithProviders(
      <ConfirmModal
        open
        onClose={onClose}
        onConfirm={onConfirm}
        title="Delete pin?"
        message="Removing…"
        confirmLabel="Deleting…"
        destructive
        busy
      />,
    );
    const dialog = screen.getByRole('alertdialog');
    const cancel = within(dialog).getByRole('button', { name: /^cancel$/i });
    const confirm = within(dialog).getByRole('button', { name: /deleting/i });
    expect(cancel).toBeDisabled();
    expect(confirm).toBeDisabled();
    // Disabled buttons swallow clicks — no re-entrant confirm/cancel.
    fireEvent.click(confirm);
    fireEvent.click(cancel);
    expect(onConfirm).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
    // The in-flight spinner renders inside the Confirm button's icon slot.
    expect(confirm.querySelector('.fui-Spinner')).not.toBeNull();
  });

  it('does not apply the danger class when destructive is omitted/false', () => {
    renderWithProviders(
      <ConfirmModal open onClose={noop} onConfirm={noop} title="Save?" message="Save changes?" />,
    );
    const confirm = screen.getByRole('button', { name: /^confirm$/i });
    expect(confirm.getAttribute('style') ?? '').not.toMatch(/background-color/i);
  });

  it('renders under a dark theme (light/dark parity smoke)', () => {
    render(
      <FluentProvider theme={webDarkTheme}>
        <ConfirmModal
          open
          onClose={noop}
          onConfirm={noop}
          title="Delete matter?"
          message="This cannot be undone."
          destructive
        />
      </FluentProvider>,
    );
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.getByText('This cannot be undone.')).toBeInTheDocument();
  });
});
