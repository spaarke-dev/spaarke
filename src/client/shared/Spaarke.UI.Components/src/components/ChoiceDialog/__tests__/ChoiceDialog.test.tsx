/**
 * ChoiceDialog.test.tsx — the re-based `ChoiceDialog` (spaarke-modal-system task 041,
 * spec FR-13). Verifies the public `IChoiceDialogProps`/`IChoiceDialogOption` contract
 * (options/onSelect/onDismiss/cancelText) still preserves the 2-4 rich-choice
 * choice-dialog-pattern (formerly ADR-023) behavior EXACTLY now that chrome + per-choice
 * rendering are delegated to the `ChoiceModal` preset: choice count 2-4, icon+title+
 * description per option, selection returns the chosen id, Cancel returns none, and the
 * dialog chrome (window controls) now comes from `ChoiceModal`/`SprkModal` rather than a
 * local `Dialog`. `ChoiceModal`'s OWN preset-level suite (`SprkModal/presets/__tests__/
 * ChoiceModal.test.tsx`) already covers keyboard operability + preset internals in depth;
 * this file focuses on the ADAPTER contract (the original `ChoiceDialog` prop shape).
 */
import * as React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { HistoryRegular, DocumentAddRegular, DeleteRegular, ArchiveRegular } from '@fluentui/react-icons';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { ChoiceDialog, type IChoiceDialogOption } from '../ChoiceDialog';

const noop = () => {};

const twoOptions: IChoiceDialogOption[] = [
  { id: 'resume', icon: <HistoryRegular />, title: 'Resume', description: 'Continue where you left off.' },
  { id: 'fresh', icon: <DocumentAddRegular />, title: 'Start Fresh', description: 'Begin a new session.' },
];

const threeOptions: IChoiceDialogOption[] = [
  ...twoOptions,
  { id: 'discard', icon: <DeleteRegular />, title: 'Discard', description: 'Delete the existing draft.' },
];

const fourOptions: IChoiceDialogOption[] = [
  ...threeOptions,
  { id: 'archive', icon: <ArchiveRegular />, title: 'Archive', description: 'Store it for later.' },
];

describe('ChoiceDialog (re-based onto ChoiceModal — choice-dialog-pattern preserved, task 041)', () => {
  it('renders 2 choices with icon + title + description, and a Cancel button', () => {
    renderWithProviders(
      <ChoiceDialog
        open
        title="Resume session?"
        message="This analysis has existing history."
        options={twoOptions}
        onSelect={noop}
        onDismiss={noop}
      />
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Resume session?')).toBeInTheDocument();
    expect(within(dialog).getByText('This analysis has existing history.')).toBeInTheDocument();
    for (const option of twoOptions) {
      expect(within(dialog).getByText(option.title)).toBeInTheDocument();
      expect(within(dialog).getByText(option.description)).toBeInTheDocument();
    }
    expect(within(dialog).getByRole('button', { name: /^cancel$/i })).toBeInTheDocument();
  });

  it('renders 3 choices', () => {
    renderWithProviders(
      <ChoiceDialog open title="Choose" message="Pick one." options={threeOptions} onSelect={noop} onDismiss={noop} />
    );
    for (const option of threeOptions) {
      expect(screen.getByText(option.title)).toBeInTheDocument();
    }
  });

  it('renders 4 choices (the ADR-023 upper bound)', () => {
    renderWithProviders(
      <ChoiceDialog open title="Choose" message="Pick one." options={fourOptions} onSelect={noop} onDismiss={noop} />
    );
    for (const option of fourOptions) {
      expect(screen.getByText(option.title)).toBeInTheDocument();
    }
  });

  it('selecting a choice calls onSelect with exactly that option id, never onDismiss', () => {
    const onSelect = jest.fn();
    const onDismiss = jest.fn();
    renderWithProviders(
      <ChoiceDialog
        open
        title="Resume session?"
        message="This analysis has existing history."
        options={threeOptions}
        onSelect={onSelect}
        onDismiss={onDismiss}
      />
    );
    fireEvent.click(screen.getByRole('button', { name: /start fresh/i }));
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith('fresh');
    expect(onDismiss).not.toHaveBeenCalled();
  });

  it('a disabled option does not fire onSelect', () => {
    const onSelect = jest.fn();
    const withDisabled: IChoiceDialogOption[] = [
      ...twoOptions,
      {
        id: 'discard',
        icon: <DeleteRegular />,
        title: 'Discard',
        description: 'Delete the existing draft.',
        disabled: true,
      },
    ];
    renderWithProviders(
      <ChoiceDialog
        open
        title="Choose"
        message="Pick one."
        options={withDisabled}
        onSelect={onSelect}
        onDismiss={noop}
      />
    );
    const disabledOption = screen.getByRole('button', { name: /discard/i });
    expect(disabledOption).toBeDisabled();
    fireEvent.click(disabledOption);
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('Cancel invokes onDismiss and selects nothing (ADR-023 cancel semantics)', () => {
    const onSelect = jest.fn();
    const onDismiss = jest.fn();
    renderWithProviders(
      <ChoiceDialog
        open
        title="Resume session?"
        message="This analysis has existing history."
        options={twoOptions}
        onSelect={onSelect}
        onDismiss={onDismiss}
      />
    );
    fireEvent.click(screen.getByRole('button', { name: /^cancel$/i }));
    expect(onDismiss).toHaveBeenCalledTimes(1);
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('is keyboard-operable: focusing a choice and pressing Enter selects it', async () => {
    const user = userEvent.setup();
    const onSelect = jest.fn();
    renderWithProviders(
      <ChoiceDialog open title="Choose" message="Pick one." options={twoOptions} onSelect={onSelect} onDismiss={noop} />
    );
    const choice = screen.getByRole('button', { name: /start fresh/i });
    choice.focus();
    expect(choice).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(onSelect).toHaveBeenCalledWith('fresh');
  });

  it('chrome (window controls) comes from ChoiceModal/SprkModal, not a local Dialog', () => {
    renderWithProviders(
      <ChoiceDialog
        open
        title="Resume session?"
        message="Has history."
        options={twoOptions}
        onSelect={noop}
        onDismiss={noop}
      />
    );
    // The standard Spaarke close (×) affordance is distinct from the footer Cancel button —
    // proof the header cluster is supplied by ModalWindowControls via ChoiceModal/SprkModal,
    // matching AC#1 ("chrome supplied by ChoiceModal, not a local Dialog").
    expect(screen.getByRole('button', { name: /^close$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^cancel$/i })).toBeInTheDocument();
  });

  it('the × close control invokes onDismiss (same handler as Cancel)', () => {
    const onDismiss = jest.fn();
    renderWithProviders(
      <ChoiceDialog
        open
        title="Resume session?"
        message="Has history."
        options={twoOptions}
        onSelect={noop}
        onDismiss={onDismiss}
      />
    );
    fireEvent.click(screen.getByRole('button', { name: /^close$/i }));
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it('renders a ReactNode message (not just a plain string)', () => {
    renderWithProviders(
      <ChoiceDialog
        open
        title="Resume session?"
        message={
          <>
            This analysis has <strong>12 messages</strong>.
          </>
        }
        options={twoOptions}
        onSelect={noop}
        onDismiss={noop}
      />
    );
    expect(screen.getByText('12 messages')).toBeInTheDocument();
  });

  it('renders the legacy cancelText prop as the Cancel label (wired via ChoiceModal.cancelLabel, P2 consolidation)', () => {
    renderWithProviders(
      <ChoiceDialog
        open
        title="Resume session?"
        message="Has history."
        options={twoOptions}
        onSelect={noop}
        onDismiss={noop}
        cancelText="Not now"
      />
    );
    // The task-041 no-op gap is closed: ChoiceModal now accepts cancelLabel and
    // ChoiceDialog forwards cancelText into it.
    expect(screen.getByRole('button', { name: /^not now$/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^cancel$/i })).toBeNull();
  });

  it('renders correctly under the dark theme (ADR-021 semantic tokens only)', () => {
    render(
      <FluentProvider theme={webDarkTheme}>
        <ChoiceDialog
          open
          title="Resume session?"
          message="This analysis has existing history."
          options={threeOptions}
          onSelect={noop}
          onDismiss={noop}
        />
      </FluentProvider>
    );
    expect(screen.getByText('Resume session?')).toBeInTheDocument();
    for (const option of threeOptions) {
      expect(screen.getByText(option.title)).toBeInTheDocument();
    }
    expect(screen.getByRole('button', { name: /^cancel$/i })).toBeInTheDocument();
  });
});
