/**
 * a11y.test.tsx — the family-wide accessibility + dismiss-semantics gate for the
 * SprkModal shell + presets (spec NFR-02, WCAG 2.1 AA). Verifies `aria-modal`,
 * keyboard-operable window controls + browse nav, and ESC honored PER dismiss mode
 * (light closes; explicit/alert do not).
 */
import * as React from 'react';
import { render, fireEvent, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { SprkModal } from '../SprkModal';
import { ConfirmModal } from '../presets/ConfirmModal';
import { FormModal } from '../presets/FormModal';
import { WizardModal } from '../presets/WizardModal';
import { BrowseModal } from '../presets/BrowseModal';

const noop = () => {};

describe('SprkModal family — a11y + dismiss semantics (NFR-02)', () => {
  it('the shell sets aria-modal and exposes keyboard-operable window controls', () => {
    renderWithProviders(
      <SprkModal open onClose={noop} title="A11y">
        <div>x</div>
      </SprkModal>
    );
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    const close = screen.getByRole('button', { name: /^close$/i });
    const maximize = screen.getByRole('button', { name: /maximize dialog/i });
    close.focus();
    expect(close).toHaveFocus();
    expect(maximize).not.toBeDisabled();
  });

  it("ESC closes a 'light'-dismiss modal but NOT an 'alert' modal", () => {
    const onCloseLight = jest.fn();
    const { unmount } = renderWithProviders(
      <SprkModal open onClose={onCloseLight} title="Light" dismiss="light">
        <div>x</div>
      </SprkModal>
    );
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape' });
    expect(onCloseLight).toHaveBeenCalledTimes(1);
    unmount();

    const onCloseAlert = jest.fn();
    renderWithProviders(<ConfirmModal open onClose={onCloseAlert} onConfirm={noop} title="Delete?" message="Sure?" />);
    fireEvent.keyDown(screen.getByRole('alertdialog'), { key: 'Escape', code: 'Escape' });
    expect(onCloseAlert).not.toHaveBeenCalled();
  });

  it("'explicit'-dismiss modals (Form, Wizard) do not close on ESC", () => {
    const onCloseForm = jest.fn();
    const { unmount } = renderWithProviders(
      <FormModal open onClose={onCloseForm} onSubmit={noop} title="Form">
        <div>f</div>
      </FormModal>
    );
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape' });
    expect(onCloseForm).not.toHaveBeenCalled();
    unmount();

    const onCloseWiz = jest.fn();
    renderWithProviders(
      <WizardModal open onClose={onCloseWiz} title="Wiz" steps={['A', 'B']} active={0} onBack={noop} onNext={noop}>
        <div>w</div>
      </WizardModal>
    );
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape' });
    expect(onCloseWiz).not.toHaveBeenCalled();
  });

  it('BrowseModal announces "N of M" and its nav controls are keyboard-operable', () => {
    const onNavigate = jest.fn();
    renderWithProviders(<BrowseModal open onClose={noop} title="Doc" nav={{ index: 1, total: 3, onNavigate }} />);
    expect(screen.getByText('2 of 3')).toBeInTheDocument();
    const next = screen.getByRole('button', { name: /next record/i });
    next.focus();
    expect(next).toHaveFocus();
    fireEvent.click(next);
    expect(onNavigate).toHaveBeenCalledWith('next');
  });

  it('renders the family under a dark theme (parity)', () => {
    render(
      <FluentProvider theme={webDarkTheme}>
        <ConfirmModal open onClose={noop} onConfirm={noop} title="Dark" message="m" />
      </FluentProvider>
    );
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });
});
