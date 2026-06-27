/**
 * PinnedMemoryDeleteConfirmation — unit tests
 *
 * Covers POML acceptance criterion: "Delete confirmation shows cross-session
 * impact warning."
 *
 * Task: R6-070 PART B.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import PinnedMemoryDeleteConfirmation from '../PinnedMemoryDeleteConfirmation';

function renderWithTheme(ui: React.ReactElement): void {
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

describe('PinnedMemoryDeleteConfirmation', () => {
  it('does not render the dialog body when open=false', () => {
    renderWithTheme(
      <PinnedMemoryDeleteConfirmation
        open={false}
        pinTitle="Some pin"
        onConfirm={() => undefined}
        onCancel={() => undefined}
      />
    );
    expect(screen.queryByTestId('pinned-memory-delete-confirmation')).not.toBeInTheDocument();
  });

  it('renders the cross-session impact warning callout when open', () => {
    renderWithTheme(
      <PinnedMemoryDeleteConfirmation
        open={true}
        pinTitle="My preference"
        onConfirm={() => undefined}
        onCancel={() => undefined}
      />
    );
    expect(screen.getByTestId('pinned-memory-delete-impact')).toBeInTheDocument();
    expect(screen.getByText(/shared across all your chat sessions/i)).toBeInTheDocument();
    expect(screen.getByText(/This action affects every chat session\./i)).toBeInTheDocument();
  });

  it('renders the pin title in the body', () => {
    renderWithTheme(
      <PinnedMemoryDeleteConfirmation
        open={true}
        pinTitle="Engagement-letter style"
        onConfirm={() => undefined}
        onCancel={() => undefined}
      />
    );
    // The title is wrapped in curly quotes — assert via title-attribute on the span.
    expect(screen.getByText(/Engagement-letter style/)).toBeInTheDocument();
  });

  it('invokes onConfirm when the Delete button is clicked', async () => {
    const user = userEvent.setup();
    const onConfirm = jest.fn();
    renderWithTheme(
      <PinnedMemoryDeleteConfirmation open={true} pinTitle="X" onConfirm={onConfirm} onCancel={() => undefined} />
    );
    await user.click(screen.getByTestId('pinned-memory-delete-confirm'));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('invokes onCancel when the Cancel button is clicked', async () => {
    const user = userEvent.setup();
    const onCancel = jest.fn();
    renderWithTheme(
      <PinnedMemoryDeleteConfirmation open={true} pinTitle="X" onConfirm={() => undefined} onCancel={onCancel} />
    );
    await user.click(screen.getByTestId('pinned-memory-delete-cancel'));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('disables both buttons and shows "Deleting…" while in flight', () => {
    renderWithTheme(
      <PinnedMemoryDeleteConfirmation
        open={true}
        pinTitle="X"
        isDeleting={true}
        onConfirm={() => undefined}
        onCancel={() => undefined}
      />
    );
    expect(screen.getByTestId('pinned-memory-delete-confirm')).toBeDisabled();
    expect(screen.getByTestId('pinned-memory-delete-cancel')).toBeDisabled();
    expect(screen.getByText(/Deleting…/i)).toBeInTheDocument();
  });
});
