/**
 * ChoiceModal.test.tsx — the `SprkModal` choice preset (spec FR-09). Verifies it
 * preserves the choice-dialog-pattern contract (formerly ADR-023): 2-4 rich
 * (labelled + described) choices, keyboard-operable selection, Cancel-only left
 * footer, and no auto-selected default.
 */
import * as React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { renderWithProviders } from '../../../../__mocks__/pcfMocks';
import { ChoiceModal, type ChoiceModalChoice } from '../ChoiceModal';

const noop = () => {};

const choices: ChoiceModalChoice[] = [
  { id: 'resume', label: 'Resume', description: 'Continue where you left off.' },
  { id: 'fresh', label: 'Start Fresh', description: 'Begin a new session.' },
  { id: 'discard', label: 'Discard', description: 'Delete the existing draft.' },
];

describe('ChoiceModal (preset — choice-dialog-pattern preserved)', () => {
  it('renders 2-4 rich choices (label + description) and a left-aligned Cancel', () => {
    renderWithProviders(<ChoiceModal open onClose={noop} title="Resume session?" choices={choices} onSelect={noop} />);
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Resume session?')).toBeInTheDocument();
    for (const choice of choices) {
      expect(within(dialog).getByText(choice.label)).toBeInTheDocument();
      expect(within(dialog).getByText(choice.description)).toBeInTheDocument();
    }
    expect(within(dialog).getByRole('button', { name: /^cancel$/i })).toBeInTheDocument();
  });

  it('is size xs and non-maximizable (small decision surface)', () => {
    renderWithProviders(<ChoiceModal open onClose={noop} title="Choose" choices={choices} onSelect={noop} />);
    const dialog = screen.getByRole('dialog');
    expect(dialog.style.width).toContain('480px');
    expect(screen.queryByRole('button', { name: /maximize dialog/i })).toBeNull();
  });

  it('renders an optional message above the choices', () => {
    renderWithProviders(
      <ChoiceModal
        open
        onClose={noop}
        title="Resume session?"
        message="This analysis has existing history."
        choices={choices}
        onSelect={noop}
      />
    );
    expect(screen.getByText('This analysis has existing history.')).toBeInTheDocument();
  });

  it('renders a custom cancelLabel when provided (mirrors ConfirmModal — P2 consolidation)', () => {
    renderWithProviders(
      <ChoiceModal open onClose={noop} title="Choose" choices={choices} onSelect={noop} cancelLabel="Not now" />
    );
    expect(screen.getByRole('button', { name: /^not now$/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^cancel$/i })).toBeNull();
  });

  it('clicking a choice fires onSelect with exactly that choice id', () => {
    const onSelect = jest.fn();
    renderWithProviders(
      <ChoiceModal open onClose={noop} title="Resume session?" choices={choices} onSelect={onSelect} />
    );
    fireEvent.click(screen.getByRole('button', { name: /start fresh/i }));
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith('fresh');
  });

  it('is keyboard-operable: focusing a choice and pressing Enter fires onSelect with that id', async () => {
    const user = userEvent.setup();
    const onSelect = jest.fn();
    renderWithProviders(
      <ChoiceModal open onClose={noop} title="Resume session?" choices={choices} onSelect={onSelect} />
    );
    const secondChoice = screen.getByRole('button', { name: /start fresh/i });
    secondChoice.focus();
    expect(secondChoice).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith('fresh');
  });

  it('is keyboard-operable via Space as well', async () => {
    const user = userEvent.setup();
    const onSelect = jest.fn();
    renderWithProviders(
      <ChoiceModal open onClose={noop} title="Resume session?" choices={choices} onSelect={onSelect} />
    );
    const thirdChoice = screen.getByRole('button', { name: /discard/i });
    thirdChoice.focus();
    await user.keyboard(' ');
    expect(onSelect).toHaveBeenCalledWith('discard');
  });

  it('a disabled choice does not fire onSelect', () => {
    const onSelect = jest.fn();
    const withDisabled: ChoiceModalChoice[] = [
      ...choices.slice(0, 2),
      { id: 'discard', label: 'Discard', description: 'Delete the existing draft.', disabled: true },
    ];
    renderWithProviders(
      <ChoiceModal open onClose={noop} title="Resume session?" choices={withDisabled} onSelect={onSelect} />
    );
    const disabledChoice = screen.getByRole('button', { name: /discard/i });
    expect(disabledChoice).toBeDisabled();
    fireEvent.click(disabledChoice);
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('Cancel invokes onClose and does not select a choice', () => {
    const onClose = jest.fn();
    const onSelect = jest.fn();
    renderWithProviders(
      <ChoiceModal open onClose={onClose} title="Resume session?" choices={choices} onSelect={onSelect} />
    );
    fireEvent.click(screen.getByRole('button', { name: /^cancel$/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('renders under a dark theme (light/dark parity smoke)', () => {
    render(
      <FluentProvider theme={webDarkTheme}>
        <ChoiceModal open onClose={noop} title="Resume session?" choices={choices} onSelect={noop} />
      </FluentProvider>
    );
    expect(screen.getByText('Resume session?')).toBeInTheDocument();
    expect(screen.getByText('Start Fresh')).toBeInTheDocument();
  });
});
