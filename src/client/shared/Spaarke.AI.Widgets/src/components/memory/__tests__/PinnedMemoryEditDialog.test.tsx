/**
 * PinnedMemoryEditDialog — unit tests
 *
 * Covers:
 *   - Create mode: empty form, default pinType, submit yields typed
 *     PinUpsertRequest.
 *   - Edit mode: form pre-fills from `initial`.
 *   - Validation: title required, matter-fact requires matterId.
 *   - Submit is gated by isSubmitting (no double-submit).
 *   - Cancel callback fires.
 *
 * Task: R6-070 PART B.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import PinnedMemoryEditDialog from '../PinnedMemoryEditDialog';
import type { PinDto } from '../pinned-memory-contracts';

function renderWithTheme(ui: React.ReactElement): void {
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

const samplePin: PinDto = {
  pinId: 'pin-1',
  pinType: 'user-preference',
  title: 'Pre-fill style',
  content: 'Use short bullet points.',
  matterId: null,
  createdAt: '2026-06-18T00:00:00Z',
  updatedAt: '2026-06-18T00:00:00Z',
  createdBy: 'oid-1',
};

describe('PinnedMemoryEditDialog (create)', () => {
  it('renders empty form with "Create pin" submit label', () => {
    renderWithTheme(
      <PinnedMemoryEditDialog
        open={true}
        mode="create"
        onSubmit={() => undefined}
        onCancel={() => undefined}
      />
    );
    expect(screen.getByText('New pinned memory')).toBeInTheDocument();
    expect(screen.getByText('Create pin')).toBeInTheDocument();
    const title = screen.getByTestId('pinned-memory-edit-title') as HTMLInputElement;
    expect(title.value).toBe('');
  });

  it('submits typed PinUpsertRequest with user-input values', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn();
    renderWithTheme(
      <PinnedMemoryEditDialog
        open={true}
        mode="create"
        onSubmit={onSubmit}
        onCancel={() => undefined}
      />
    );
    // Note: we use fireEvent.change rather than user.type because Fluent v9
    // Input + React 19 controlled-input reconciliation races user.type's
    // per-keystroke events under jsdom — characters get dropped. fireEvent
    // pushes the entire string in one synthetic event, exercising the same
    // onChange callback the component declares.
    fireEvent.change(screen.getByTestId('pinned-memory-edit-title'), {
      target: { value: 'Style' },
    });
    fireEvent.change(screen.getByTestId('pinned-memory-edit-content'), {
      target: { value: 'Use shorter sentences' },
    });
    await user.click(screen.getByTestId('pinned-memory-edit-submit'));
    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledWith({
      title: 'Style',
      content: 'Use shorter sentences',
      pinType: 'user-preference',
      matterId: undefined,
    });
  });

  it('shows validation error when title is empty', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn();
    renderWithTheme(
      <PinnedMemoryEditDialog
        open={true}
        mode="create"
        onSubmit={onSubmit}
        onCancel={() => undefined}
      />
    );
    // Try to submit without filling anything.
    await user.click(screen.getByTestId('pinned-memory-edit-submit'));
    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByText('Title is required.')).toBeInTheDocument();
  });

  it('requires matterId when pinType is matter-fact', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn();
    renderWithTheme(
      <PinnedMemoryEditDialog
        open={true}
        mode="create"
        onSubmit={onSubmit}
        onCancel={() => undefined}
      />
    );
    fireEvent.change(screen.getByTestId('pinned-memory-edit-title'), {
      target: { value: 'X' },
    });
    fireEvent.change(screen.getByTestId('pinned-memory-edit-content'), {
      target: { value: 'Y' },
    });
    // Fluent v9 Radio renders an <input type="radio" value="..."> with the
    // accessible name supplied by the `label` prop. Query by role + value to
    // reach the right radio robustly across Fluent v9 minor versions.
    const radioInput = screen.getByRole('radio', { name: /Matter fact/i });
    await user.click(radioInput);
    await user.click(screen.getByTestId('pinned-memory-edit-submit'));
    expect(onSubmit).not.toHaveBeenCalled();
    expect(
      screen.getByText('Matter is required when pin type is "Matter fact".')
    ).toBeInTheDocument();
  });

  it('fires onCancel when the Cancel button is clicked', async () => {
    const user = userEvent.setup();
    const onCancel = jest.fn();
    renderWithTheme(
      <PinnedMemoryEditDialog
        open={true}
        mode="create"
        onSubmit={() => undefined}
        onCancel={onCancel}
      />
    );
    await user.click(screen.getByTestId('pinned-memory-edit-cancel'));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('disables submit when isSubmitting and shows "Saving…"', () => {
    renderWithTheme(
      <PinnedMemoryEditDialog
        open={true}
        mode="create"
        isSubmitting={true}
        onSubmit={() => undefined}
        onCancel={() => undefined}
      />
    );
    expect(screen.getByTestId('pinned-memory-edit-submit')).toBeDisabled();
    expect(screen.getByText(/Saving…/i)).toBeInTheDocument();
  });

  it('surfaces serverError inline', () => {
    renderWithTheme(
      <PinnedMemoryEditDialog
        open={true}
        mode="create"
        serverError="Rate limit exceeded."
        onSubmit={() => undefined}
        onCancel={() => undefined}
      />
    );
    expect(screen.getByTestId('pinned-memory-edit-server-error')).toBeInTheDocument();
    expect(screen.getByText('Rate limit exceeded.')).toBeInTheDocument();
  });
});

describe('PinnedMemoryEditDialog (edit)', () => {
  it('pre-fills the form from `initial` and shows "Save changes"', () => {
    renderWithTheme(
      <PinnedMemoryEditDialog
        open={true}
        mode="edit"
        initial={samplePin}
        onSubmit={() => undefined}
        onCancel={() => undefined}
      />
    );
    expect(screen.getByText('Edit pinned memory')).toBeInTheDocument();
    expect(screen.getByText('Save changes')).toBeInTheDocument();
    const title = screen.getByTestId('pinned-memory-edit-title') as HTMLInputElement;
    expect(title.value).toBe(samplePin.title);
  });

  it('submits with the edited values', async () => {
    const user = userEvent.setup();
    const onSubmit = jest.fn();
    renderWithTheme(
      <PinnedMemoryEditDialog
        open={true}
        mode="edit"
        initial={samplePin}
        onSubmit={onSubmit}
        onCancel={() => undefined}
      />
    );
    const title = screen.getByTestId('pinned-memory-edit-title') as HTMLInputElement;
    fireEvent.change(title, { target: { value: 'Pre-fill style v2' } });
    await user.click(screen.getByTestId('pinned-memory-edit-submit'));
    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledWith({
      title: 'Pre-fill style v2',
      content: samplePin.content,
      pinType: samplePin.pinType,
      matterId: undefined,
    });
  });
});
