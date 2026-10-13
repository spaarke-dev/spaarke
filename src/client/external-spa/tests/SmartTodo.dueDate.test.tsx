/**
 * spaarke-ontology-platform-r1 task 106 — the external app's to-do due date is a calendar date.
 *
 * sprk_todo.sprk_duedate is Dataverse Date Only: the BFF returns "yyyy-MM-dd" and Dataverse refuses a timestamp on
 * write (HTTP 400). Before task 106 this view read the date with `new Date("yyyy-MM-dd")` — UTC midnight, the PREVIOUS
 * day in New York — called a to-do due today "Overdue" from local midnight, and created a to-do with toISOString() of
 * noon UTC.
 *
 * Runs in America/New_York: vitest runs each test file in its own forked process, where assigning TZ before any Date is
 * made switches Node's zone. The first test guards that, so a UTC run cannot pass trivially.
 */
process.env.TZ = 'America/New_York';

import * as React from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessLevel } from '../src/types';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

import { SmartTodo } from '../src/components/SmartTodo';

const PROJECT = 'p-106';

function answerTodos(todos: Array<Record<string, unknown>>): void {
  bffApiCall.mockImplementation(async (path: string, init?: { method?: string; body?: string }) => {
    if (path.endsWith('/todos') && (!init?.method || init.method === 'GET')) return { value: todos };
    if (path.endsWith('/todos') && init?.method === 'POST') {
      return { sprk_todoid: 't-new', ...JSON.parse(init.body ?? '{}'), statuscode: 1 };
    }
    throw new Error(`unexpected ${init?.method ?? 'GET'} ${path}`);
  });
}

function renderTodos(accessLevel = AccessLevel.ViewOnly): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <SmartTodo projectId={PROJECT} accessLevel={accessLevel} />
    </FluentProvider>
  );
}

beforeEach(() => {
  bffApiCall.mockReset();
  // 21:00 on 2026-10-05 in New York = 01:00Z on 2026-10-06: the UTC date is already tomorrow.
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(Date.parse('2026-10-06T01:00:00Z')));
});

afterEach(() => {
  vi.useRealTimers();
});

describe('external to-dos: the due date is the calendar day (New York, 21:00)', () => {
  it('the harness is behind UTC (guard is meaningful)', () => {
    expect(new Date('2026-10-02').getDate()).toBe(1);
  });

  it('shows a due date of 2026-10-05 as Oct 5, and a to-do due today is not overdue', async () => {
    answerTodos([{ sprk_todoid: 't-1', sprk_name: 'Review draft', sprk_duedate: '2026-10-05', statuscode: 1 }]);
    renderTodos();

    await waitFor(() => expect(screen.getByText('Review draft')).toBeTruthy());
    expect(screen.getByText(/Oct 5, 2026/)).toBeTruthy();
    expect(screen.queryByText(/Oct 4, 2026/)).toBeNull();
    expect(screen.queryByText(/Overdue/)).toBeNull();
  });

  it('a to-do due yesterday is overdue', async () => {
    answerTodos([{ sprk_todoid: 't-2', sprk_name: 'File reply', sprk_duedate: '2026-10-04', statuscode: 1 }]);
    renderTodos();

    await waitFor(() => expect(screen.getByText(/Overdue/)).toBeTruthy());
    expect(screen.getByText(/Oct 4, 2026/)).toBeTruthy();
  });

  it('creates a to-do with the picked day as "yyyy-MM-dd", never a timestamp', async () => {
    answerTodos([]);
    renderTodos(AccessLevel.Collaborate);

    fireEvent.click(await screen.findByLabelText('Create new task'));
    fireEvent.change(screen.getByPlaceholderText('Enter task title...'), { target: { value: 'zz-106 SPA create' } });
    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement;
    fireEvent.change(dateInput, { target: { value: '2026-10-07' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create Task' }));

    await waitFor(() =>
      expect(bffApiCall).toHaveBeenCalledWith(
        `/api/v1/external/projects/${PROJECT}/todos`,
        expect.objectContaining({ method: 'POST' })
      )
    );
    const post = bffApiCall.mock.calls.find(([, init]) => init?.method === 'POST')!;
    expect(JSON.parse(post[1].body).sprk_duedate).toBe('2026-10-07');
  });
});
