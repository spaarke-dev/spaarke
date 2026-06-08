/**
 * TodoDetail — single-entity load/save tests (smart-todo-decoupling-r3 FR-09).
 *
 * Covers:
 *   - loading / error / empty render states
 *   - record render (sprk_todo fields: name, description, notes, due date, sliders)
 *   - dirty save: single onSaveTodo call with diff-only fields
 *   - completed inactive state (statecode=1 + statuscode=2)
 *   - dismissed inactive state (statecode=1 + statuscode=659490002)
 *   - complete action sets statecode/statuscode/sprk_completedon in ONE call
 *   - dismiss action invokes onDismissTodo callback
 *   - no legacy two-entity props/state remain (compile-time enforced by TS)
 */

import * as React from 'react';
import { screen, fireEvent, waitFor, act } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { TodoDetail } from '../TodoDetail';
import type { ITodoRecord, ITodoFieldUpdates } from '../types';

// ─────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────

const baseRecord: ITodoRecord = {
  sprk_todoid: 'todo-1',
  sprk_name: 'Test To Do',
  sprk_description: 'Do the thing',
  sprk_notes: 'Some rich notes',
  sprk_duedate: '2026-12-31',
  sprk_priorityscore: 70,
  sprk_effortscore: 30,
  sprk_todocolumn: 100000000,
  sprk_todopinned: false,
  statecode: 0,
  statuscode: 1,
  _sprk_assignedto_value: 'user-1',
  '_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue': 'Alice User',
};

const noop = async () => ({ success: true });
const noopSearch = async () => [];

// ─────────────────────────────────────────────────────────────────────
// Render states
// ─────────────────────────────────────────────────────────────────────

describe('TodoDetail — render states', () => {
  it('renders the loading spinner when isLoading is true', () => {
    renderWithProviders(
      <TodoDetail
        record={null}
        isLoading
        error={null}
        onSaveTodo={noop}
        onSearchContacts={noopSearch}
      />
    );
    expect(screen.getByText(/Loading/i)).toBeInTheDocument();
  });

  it('renders the error text when error is set', () => {
    renderWithProviders(
      <TodoDetail
        record={null}
        isLoading={false}
        error="Boom"
        onSaveTodo={noop}
        onSearchContacts={noopSearch}
      />
    );
    expect(screen.getByText('Boom')).toBeInTheDocument();
  });

  it('renders the empty state when no record is selected', () => {
    renderWithProviders(
      <TodoDetail
        record={null}
        isLoading={false}
        error={null}
        onSaveTodo={noop}
        onSearchContacts={noopSearch}
      />
    );
    expect(screen.getByText(/No to-do selected/i)).toBeInTheDocument();
  });

  it('renders sprk_todo fields when a record is provided', () => {
    renderWithProviders(
      <TodoDetail
        record={baseRecord}
        isLoading={false}
        error={null}
        onSaveTodo={noop}
        onSearchContacts={noopSearch}
      />
    );
    // description value drives the description textarea
    expect(screen.getByPlaceholderText(/Add a description/i)).toHaveValue('Do the thing');
    // notes from sprk_notes (native; was sprk_eventtodo.sprk_todonotes in R1/R2)
    expect(screen.getByPlaceholderText(/Add notes/i)).toHaveValue('Some rich notes');
    // assigned to display
    expect(screen.getByText('Alice User')).toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────
// Save — single updateRecord("sprk_todo", id, fields)
// ─────────────────────────────────────────────────────────────────────

describe('TodoDetail — single-entity save', () => {
  it('invokes onSaveTodo exactly once with only the dirty fields', async () => {
    const onSaveTodo = jest.fn().mockResolvedValue({ success: true });
    renderWithProviders(
      <TodoDetail
        record={baseRecord}
        isLoading={false}
        error={null}
        onSaveTodo={onSaveTodo}
        onSearchContacts={noopSearch}
      />
    );

    // Edit description only
    const desc = screen.getByPlaceholderText(/Add a description/i) as HTMLTextAreaElement;
    fireEvent.change(desc, { target: { value: 'Updated description' } });

    // Click Save
    const saveBtn = screen.getByRole('button', { name: /Save/i });
    await act(async () => {
      fireEvent.click(saveBtn);
    });

    await waitFor(() => expect(onSaveTodo).toHaveBeenCalledTimes(1));
    expect(onSaveTodo).toHaveBeenCalledWith(
      'todo-1',
      expect.objectContaining({ sprk_description: 'Updated description' })
    );
    // Diff-only: untouched fields must NOT be in the payload
    const payload = onSaveTodo.mock.calls[0][1] as ITodoFieldUpdates;
    expect(payload.sprk_notes).toBeUndefined();
    expect(payload.sprk_duedate).toBeUndefined();
    expect(payload.sprk_priorityscore).toBeUndefined();
    expect(payload.sprk_effortscore).toBeUndefined();
  });

  it('does not call onSaveTodo when nothing is dirty', async () => {
    const onSaveTodo = jest.fn().mockResolvedValue({ success: true });
    renderWithProviders(
      <TodoDetail
        record={baseRecord}
        isLoading={false}
        error={null}
        onSaveTodo={onSaveTodo}
        onSearchContacts={noopSearch}
      />
    );

    // Save button is disabled while not dirty
    const saveBtn = screen.getByRole('button', { name: /Save/i }) as HTMLButtonElement;
    expect(saveBtn).toBeDisabled();
    expect(onSaveTodo).not.toHaveBeenCalled();
  });

  it('surfaces save failure as an error banner', async () => {
    const onSaveTodo = jest.fn().mockResolvedValue({ success: false, error: 'Network down' });
    renderWithProviders(
      <TodoDetail
        record={baseRecord}
        isLoading={false}
        error={null}
        onSaveTodo={onSaveTodo}
        onSearchContacts={noopSearch}
      />
    );

    const desc = screen.getByPlaceholderText(/Add a description/i) as HTMLTextAreaElement;
    fireEvent.change(desc, { target: { value: 'Something else' } });

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Save/i }));
    });

    await waitFor(() => {
      expect(screen.getByText('Network down')).toBeInTheDocument();
    });
  });
});

// ─────────────────────────────────────────────────────────────────────
// Complete — single updateRecord with statecode/statuscode/sprk_completedon
// ─────────────────────────────────────────────────────────────────────

describe('TodoDetail — complete (single updateRecord)', () => {
  it('Complete button issues ONE onSaveTodo call with statecode=1, statuscode=2, sprk_completedon', async () => {
    const onSaveTodo = jest.fn().mockResolvedValue({ success: true });
    renderWithProviders(
      <TodoDetail
        record={baseRecord}
        isLoading={false}
        error={null}
        onSaveTodo={onSaveTodo}
        onSearchContacts={noopSearch}
      />
    );

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Complete/i }));
    });

    await waitFor(() => expect(onSaveTodo).toHaveBeenCalledTimes(1));
    const [todoId, payload] = onSaveTodo.mock.calls[0] as [string, ITodoFieldUpdates];
    expect(todoId).toBe('todo-1');
    expect(payload.statecode).toBe(1);
    expect(payload.statuscode).toBe(2);
    expect(payload.sprk_completedon).toBeDefined();
  });

  it('shows "Completed" badge when record is already Completed (statecode=1, statuscode=2)', () => {
    const completed: ITodoRecord = {
      ...baseRecord,
      statecode: 1,
      statuscode: 2,
    };
    renderWithProviders(
      <TodoDetail
        record={completed}
        isLoading={false}
        error={null}
        onSaveTodo={noop}
        onSearchContacts={noopSearch}
      />
    );
    expect(screen.getByRole('button', { name: /Completed/i })).toBeDisabled();
  });
});

// ─────────────────────────────────────────────────────────────────────
// Dismiss — onDismissTodo callback (sets statuscode=659490002 via host)
// ─────────────────────────────────────────────────────────────────────

describe('TodoDetail — dismiss', () => {
  it('Dismiss button invokes onDismissTodo with the todo id', async () => {
    const onDismissTodo = jest.fn().mockResolvedValue({ success: true });
    renderWithProviders(
      <TodoDetail
        record={baseRecord}
        isLoading={false}
        error={null}
        onSaveTodo={noop}
        onDismissTodo={onDismissTodo}
        onSearchContacts={noopSearch}
      />
    );

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Dismiss/i }));
    });

    await waitFor(() => expect(onDismissTodo).toHaveBeenCalledTimes(1));
    expect(onDismissTodo).toHaveBeenCalledWith('todo-1');
  });

  it('does NOT render the Dismiss button when onDismissTodo prop is omitted', () => {
    renderWithProviders(
      <TodoDetail
        record={baseRecord}
        isLoading={false}
        error={null}
        onSaveTodo={noop}
        onSearchContacts={noopSearch}
      />
    );
    expect(screen.queryByRole('button', { name: /^Dismiss$/i })).not.toBeInTheDocument();
  });

  it('does NOT render the Dismiss button when the record is already inactive', () => {
    const dismissed: ITodoRecord = {
      ...baseRecord,
      statecode: 1,
      statuscode: 659490002,
    };
    const onDismissTodo = jest.fn().mockResolvedValue({ success: true });
    renderWithProviders(
      <TodoDetail
        record={dismissed}
        isLoading={false}
        error={null}
        onSaveTodo={noop}
        onDismissTodo={onDismissTodo}
        onSearchContacts={noopSearch}
      />
    );
    expect(screen.queryByRole('button', { name: /^Dismiss$/i })).not.toBeInTheDocument();
    // The disabled "Dismissed" indicator IS shown
    expect(screen.getByRole('button', { name: /Dismissed/i })).toBeDisabled();
  });
});
