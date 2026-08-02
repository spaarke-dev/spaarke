/**
 * FormModal.test.tsx — thin `SprkModal` config for light-edit forms (spec FR-09).
 * Verifies the standard Cancel/Save footer, the `md` default (overridable to `sm`),
 * `explicit` dismiss (no light-dismiss on ESC/backdrop), children rendering inside
 * the form body, and `bodyScroll` forwarding to the shell.
 */
import * as React from 'react';
import { fireEvent, screen, within } from '@testing-library/react';
import { renderWithProviders } from '../../../../__mocks__/pcfMocks';
import { FormModal } from '../FormModal';

const noop = () => {};

describe('FormModal (preset — FR-09)', () => {
  it('renders the standard Cancel (left) / Save (right) footer and the title', () => {
    renderWithProviders(
      <FormModal open onClose={noop} onSubmit={noop} title="Edit Matter">
        <div>field</div>
      </FormModal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Edit Matter')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /^cancel$/i })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /^save$/i })).toBeInTheDocument();
  });

  it('renders children inside the form body', () => {
    renderWithProviders(
      <FormModal open onClose={noop} onSubmit={noop} title="Edit">
        <div>Name field</div>
        <div>Email field</div>
      </FormModal>,
    );
    expect(screen.getByText('Name field')).toBeInTheDocument();
    expect(screen.getByText('Email field')).toBeInTheDocument();
  });

  it('submitDisabled disables Save only; busy disables both with a spinner; dismiss="alert" renders an alertdialog (P3 consolidation)', () => {
    const onSubmit = jest.fn();
    const onClose = jest.fn();
    const { unmount } = renderWithProviders(
      <FormModal open onClose={onClose} onSubmit={onSubmit} title="Edit" submitDisabled>
        <div>x</div>
      </FormModal>,
    );
    expect(screen.getByRole('button', { name: /^save$/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /^cancel$/i })).not.toBeDisabled();
    unmount();

    const second = renderWithProviders(
      <FormModal open onClose={onClose} onSubmit={onSubmit} title="Edit" busy submitLabel="Saving…">
        <div>x</div>
      </FormModal>,
    );
    const save = screen.getByRole('button', { name: /saving/i });
    const cancel = screen.getByRole('button', { name: /^cancel$/i });
    expect(save).toBeDisabled();
    expect(cancel).toBeDisabled();
    expect(save.querySelector('.fui-Spinner')).not.toBeNull();
    fireEvent.click(save);
    fireEvent.click(cancel);
    expect(onSubmit).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
    second.unmount();

    renderWithProviders(
      <FormModal open onClose={noop} onSubmit={noop} title="Compose" dismiss="alert">
        <div>x</div>
      </FormModal>,
    );
    // alert dismiss maps to Fluent modalType="alert" → role=alertdialog.
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });

  it('Cancel invokes onClose', () => {
    const onClose = jest.fn();
    renderWithProviders(
      <FormModal open onClose={onClose} onSubmit={noop} title="Edit">
        <div>x</div>
      </FormModal>,
    );
    fireEvent.click(screen.getByRole('button', { name: /^cancel$/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('Save invokes onSubmit, and submitLabel overrides the default label', () => {
    const onSubmit = jest.fn();
    const { unmount } = renderWithProviders(
      <FormModal open onClose={noop} onSubmit={onSubmit} title="Edit">
        <div>x</div>
      </FormModal>,
    );
    fireEvent.click(screen.getByRole('button', { name: /^save$/i }));
    expect(onSubmit).toHaveBeenCalledTimes(1);
    unmount();

    renderWithProviders(
      <FormModal open onClose={noop} onSubmit={noop} title="Edit" submitLabel="Send">
        <div>x</div>
      </FormModal>,
    );
    expect(screen.getByRole('button', { name: /^send$/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^save$/i })).toBeNull();
  });

  it('defaults to size md (1040px cap); size="sm" uses the 560px cap', () => {
    const { unmount } = renderWithProviders(
      <FormModal open onClose={noop} onSubmit={noop} title="Default size">
        <div>x</div>
      </FormModal>,
    );
    expect(screen.getByRole('dialog')).toHaveStyle({ width: 'min(1040px, 92vw)' });
    unmount();

    renderWithProviders(
      <FormModal open onClose={noop} onSubmit={noop} title="Small" size="sm">
        <div>x</div>
      </FormModal>,
    );
    expect(screen.getByRole('dialog')).toHaveStyle({ width: 'min(560px, 92vw)' });
  });

  it('uses explicit dismiss — ESC does not invoke onClose (only Cancel/× do)', () => {
    const onClose = jest.fn();
    renderWithProviders(
      <FormModal open onClose={onClose} onSubmit={noop} title="Explicit dismiss">
        <div>x</div>
      </FormModal>,
    );
    const dialog = screen.getByRole('dialog');
    fireEvent.keyDown(dialog, { key: 'Escape', code: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    // The × control still closes explicitly.
    fireEvent.click(within(dialog).getByRole('button', { name: /^close$/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('forwards bodyScroll to the shell (arrows variant renders the scroll-pane wrapper)', () => {
    const { baseElement } = renderWithProviders(
      <FormModal open onClose={noop} onSubmit={noop} title="Scrollable" bodyScroll="arrows">
        <div>field</div>
      </FormModal>,
    );
    // The Dialog surface portals to `document.body`, so query `baseElement` (not
    // `container`) for the `ModalScrollArea` scroll pane — its `tabIndex={0}` div
    // is unique to the `arrows` variant (the default `native` body has none).
    expect(baseElement.querySelector('[tabindex="0"]')).not.toBeNull();
    expect(screen.getByText('field')).toBeInTheDocument();
  });
});
