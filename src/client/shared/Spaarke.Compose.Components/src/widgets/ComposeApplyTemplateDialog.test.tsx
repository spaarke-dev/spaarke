/**
 * ComposeApplyTemplateDialog.test.tsx — FR-05 (spaarkeai-compose-r6 task 032) coverage for the
 * "Apply firm template" dialog: input gating, trimmed submit, busy state, error surface, and
 * light/dark render (ADR-021 — semantic tokens via the host FluentProvider).
 *
 * `@spaarke/ui-components` resolves via the sibling package's `dist/` (not built in this test
 * environment — the KNOWN sibling-dist baseline), so `FormModal` is stubbed with a behavioral
 * mock that honors the preset's contract (open gate, Cancel/Submit buttons, busy/submitDisabled)
 * — the SAME per-file mock convention every ComposeWorkspace.*.test.tsx suite uses.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

jest.mock(
  '@spaarke/ui-components',
  () => ({
    // Behavioral FormModal stub mirroring the preset contract (SprkModal envelope not under test).
    FormModal: (props: {
      open: boolean;
      onClose: () => void;
      onSubmit: () => void;
      title: string;
      submitLabel?: string;
      cancelLabel?: string;
      submitDisabled?: boolean;
      busy?: boolean;
      children: React.ReactNode;
    }) =>
      props.open ? (
        <div role="dialog" aria-label={props.title} data-testid="mock-form-modal">
          <span>{props.title}</span>
          {props.children}
          <button onClick={props.onClose} disabled={props.busy} data-testid="mock-form-modal-cancel">
            {props.cancelLabel ?? 'Cancel'}
          </button>
          <button
            onClick={props.onSubmit}
            disabled={props.busy || props.submitDisabled}
            data-testid="mock-form-modal-submit"
          >
            {props.submitLabel ?? 'Save'}
          </button>
        </div>
      ) : null,
  }),
  // virtual: the sibling package's dist/ is not built in this environment (KNOWN baseline) —
  // the raw specifier cannot resolve, so the mock must be registered against it directly.
  { virtual: true }
);

// eslint-disable-next-line import/first
import { ComposeApplyTemplateDialog, type ComposeApplyTemplateDialogProps } from './ComposeApplyTemplateDialog';

function renderDialog(
  overrides: Partial<ComposeApplyTemplateDialogProps> = {},
  theme: typeof webLightTheme = webLightTheme
) {
  const props: ComposeApplyTemplateDialogProps = {
    open: true,
    onApply: jest.fn(),
    onClose: jest.fn(),
    ...overrides,
  };
  return {
    props,
    ...render(
      <FluentProvider theme={theme}>
        <ComposeApplyTemplateDialog {...props} />
      </FluentProvider>
    ),
  };
}

describe('ComposeApplyTemplateDialog — FR-05 apply-firm-template affordance', () => {
  it('renders nothing while closed', () => {
    renderDialog({ open: false });
    expect(screen.queryByTestId('compose-apply-template-dialog')).not.toBeInTheDocument();
  });

  it('renders the template input with Apply disabled until a name is typed', async () => {
    const user = userEvent.setup();
    renderDialog();

    expect(screen.getByTestId('compose-apply-template-dialog')).toBeInTheDocument();
    const submit = screen.getByTestId('mock-form-modal-submit');
    expect(submit).toBeDisabled();

    await user.type(screen.getByTestId('compose-apply-template-input'), 'Firm Standard');
    expect(submit).toBeEnabled();
  });

  it('submits the TRIMMED template name via onApply', async () => {
    const user = userEvent.setup();
    const onApply = jest.fn();
    renderDialog({ onApply });

    await user.type(screen.getByTestId('compose-apply-template-input'), '  Firm Standard  ');
    await user.click(screen.getByTestId('mock-form-modal-submit'));

    expect(onApply).toHaveBeenCalledTimes(1);
    expect(onApply).toHaveBeenCalledWith('Firm Standard');
  });

  it('whitespace-only input keeps Apply disabled and never calls onApply', async () => {
    const user = userEvent.setup();
    const onApply = jest.fn();
    renderDialog({ onApply });

    await user.type(screen.getByTestId('compose-apply-template-input'), '   ');
    expect(screen.getByTestId('mock-form-modal-submit')).toBeDisabled();
    expect(onApply).not.toHaveBeenCalled();
  });

  it('disables both buttons and the input while the apply is in flight (busy)', async () => {
    renderDialog({ isApplying: true });

    expect(screen.getByTestId('mock-form-modal-submit')).toBeDisabled();
    expect(screen.getByTestId('mock-form-modal-cancel')).toBeDisabled();
    expect(screen.getByTestId('compose-apply-template-input')).toBeDisabled();
  });

  it('surfaces the host error message (e.g. template not found) inside the dialog', () => {
    renderDialog({ errorMessage: 'Template "Nope" was not found. Check the template name or ID.' });

    const error = screen.getByTestId('compose-apply-template-error');
    expect(error).toBeInTheDocument();
    expect(error).toHaveTextContent('Template "Nope" was not found');
  });

  it('Cancel routes to onClose', async () => {
    const user = userEvent.setup();
    const onClose = jest.fn();
    renderDialog({ onClose });

    await user.click(screen.getByTestId('mock-form-modal-cancel'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('renders in the DARK theme without error (ADR-021 — tokens resolve via the provider)', () => {
    renderDialog({ errorMessage: 'boom' }, webDarkTheme);
    expect(screen.getByTestId('compose-apply-template-dialog')).toBeInTheDocument();
    expect(screen.getByTestId('compose-apply-template-error')).toBeInTheDocument();
  });
});
