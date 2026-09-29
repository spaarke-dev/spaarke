/**
 * ComposeSaveNameDialog.test.tsx — FR-02 (spaarkeai-compose-r7 task 030 / UC-3) coverage for the
 * "name this document" dialog: field gating, trimmed/sanitized submit, mode-specific title +
 * submit label (first-save vs Save As), derived `<name>.docx` preview, and light/dark render
 * (ADR-021 — semantic tokens via the host FluentProvider). Also unit-covers the exported name
 * helpers (`sanitizeComposeName`, `deriveComposeFileName`) that keep the client name in lockstep
 * with the server's `ComposeService.ResolveFileName`.
 *
 * `@spaarke/ui-components` resolves via the sibling package's `dist/` (not built in this test
 * environment — the KNOWN sibling-dist baseline), so `FormModal` is stubbed with a behavioral mock
 * that honors the preset contract (open gate, submit gated by submitDisabled/busy) — the SAME
 * per-file mock convention `ComposeApplyTemplateDialog.test.tsx` uses.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

jest.mock('@spaarke/ui-components', () => ({
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
        <span data-testid="mock-form-modal-title">{props.title}</span>
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
}));

// eslint-disable-next-line import/first
import {
  ComposeSaveNameDialog,
  type ComposeSaveNameDialogProps,
  sanitizeComposeName,
  deriveComposeFileName,
} from './ComposeSaveNameDialog';

function renderDialog(
  overrides: Partial<ComposeSaveNameDialogProps> = {},
  theme: typeof webLightTheme = webLightTheme
) {
  const props: ComposeSaveNameDialogProps = {
    open: true,
    mode: 'first-save',
    onSubmit: jest.fn(),
    onClose: jest.fn(),
    ...overrides,
  };
  return {
    props,
    ...render(
      <FluentProvider theme={theme}>
        <ComposeSaveNameDialog {...props} />
      </FluentProvider>
    ),
  };
}

/** The Fluent Input renders role="textbox" on its native <input>. */
function nameInput(): HTMLInputElement {
  return screen.getByRole('textbox') as HTMLInputElement;
}

describe('ComposeSaveNameDialog — FR-02 name capture', () => {
  it('renders nothing when closed', () => {
    renderDialog({ open: false });
    expect(screen.queryByTestId('mock-form-modal')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-save-name-dialog')).not.toBeInTheDocument();
  });

  it('first-save mode shows the "Name this document" title + "Save" submit label', () => {
    renderDialog({ mode: 'first-save' });
    expect(screen.getByTestId('mock-form-modal-title')).toHaveTextContent('Name this document');
    expect(screen.getByTestId('mock-form-modal-submit')).toHaveTextContent('Save');
  });

  it('save-as mode shows the "Save a copy as" title + "Save copy" submit label', () => {
    renderDialog({ mode: 'save-as' });
    expect(screen.getByTestId('mock-form-modal-title')).toHaveTextContent('Save a copy as');
    expect(screen.getByTestId('mock-form-modal-submit')).toHaveTextContent('Save copy');
  });

  it('disables submit when the name is empty and enables it once a name is entered', () => {
    renderDialog();
    // Empty by default in first-save mode → submit disabled.
    expect(screen.getByTestId('mock-form-modal-submit')).toBeDisabled();
    fireEvent.change(nameInput(), { target: { value: 'Master Services Agreement' } });
    expect(screen.getByTestId('mock-form-modal-submit')).toBeEnabled();
  });

  it('submits the trimmed name (spaces inside preserved)', () => {
    const onSubmit = jest.fn();
    renderDialog({ onSubmit });
    fireEvent.change(nameInput(), { target: { value: '  Master Services Agreement  ' } });
    fireEvent.click(screen.getByTestId('mock-form-modal-submit'));
    expect(onSubmit).toHaveBeenCalledWith('Master Services Agreement');
  });

  it('strips illegal file-name characters before submitting (spaces/hyphens kept)', () => {
    const onSubmit = jest.fn();
    renderDialog({ onSubmit });
    fireEvent.change(nameInput(), { target: { value: 'Q3 Report: v2/final?*' } });
    fireEvent.click(screen.getByTestId('mock-form-modal-submit'));
    expect(onSubmit).toHaveBeenCalledWith('Q3 Report v2final');
  });

  it('does not submit when the entered name sanitizes to empty', () => {
    const onSubmit = jest.fn();
    renderDialog({ onSubmit });
    fireEvent.change(nameInput(), { target: { value: '///' } });
    expect(screen.getByTestId('mock-form-modal-submit')).toBeDisabled();
    fireEvent.click(screen.getByTestId('mock-form-modal-submit'));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('seeds from defaultName on open (Save As) and can submit it directly', () => {
    const onSubmit = jest.fn();
    renderDialog({ mode: 'save-as', defaultName: 'Contract.docx', onSubmit });
    expect(nameInput().value).toBe('Contract.docx');
    fireEvent.click(screen.getByTestId('mock-form-modal-submit'));
    expect(onSubmit).toHaveBeenCalledWith('Contract.docx');
  });

  it('shows a live "Saved as <name>.docx" preview and does not double-append .docx', () => {
    renderDialog();
    fireEvent.change(nameInput(), { target: { value: 'Contract' } });
    expect(screen.getByTestId('compose-save-name-preview')).toHaveTextContent('Saved as: Contract.docx');
    fireEvent.change(nameInput(), { target: { value: 'Report.docx' } });
    expect(screen.getByTestId('compose-save-name-preview')).toHaveTextContent('Saved as: Report.docx');
  });

  it('submits on Enter in the name field', () => {
    const onSubmit = jest.fn();
    renderDialog({ onSubmit });
    fireEvent.change(nameInput(), { target: { value: 'Brief' } });
    fireEvent.keyDown(nameInput(), { key: 'Enter' });
    expect(onSubmit).toHaveBeenCalledWith('Brief');
  });

  it('renders under the dark theme without throwing (ADR-021 semantic tokens)', () => {
    renderDialog({ defaultName: 'Dark Doc' }, webDarkTheme);
    expect(screen.getByTestId('compose-save-name-dialog')).toBeInTheDocument();
    expect(screen.getByTestId('compose-save-name-preview')).toHaveTextContent('Saved as: Dark Doc.docx');
  });
});

describe('ComposeSaveNameDialog name helpers', () => {
  it('sanitizeComposeName trims + strips illegal chars, keeping spaces and hyphens', () => {
    expect(sanitizeComposeName('  Master Services Agreement  ')).toBe('Master Services Agreement');
    expect(sanitizeComposeName('a/b\\c:d*e?f"g<h>i|j')).toBe('abcdefghij');
    expect(sanitizeComposeName('Well-Formed Name')).toBe('Well-Formed Name');
    expect(sanitizeComposeName('////')).toBe('');
  });

  it('deriveComposeFileName mirrors ResolveFileName (.docx appended once, empty stays empty)', () => {
    expect(deriveComposeFileName('Contract')).toBe('Contract.docx');
    expect(deriveComposeFileName('Report.docx')).toBe('Report.docx');
    expect(deriveComposeFileName('Report.DOCX')).toBe('Report.DOCX');
    expect(deriveComposeFileName('   ')).toBe('');
    expect(deriveComposeFileName('a/b')).toBe('ab.docx');
  });
});
