/**
 * R4 D-9 (#2716), a task 006 follow-up (FR-07 / NFR-04: no action the UI offers may answer 403).
 *
 * On the standalone ProjectPage a project caller WITHOUT `writeAccessPerRecord` used to be offered Add Task, Create Event
 * and Upload on the `/me` level alone. The create routes also apply the workforce role ceiling, which the level does not
 * show, and each project list's `canCreate` is the POST's own decision (task 002). So the page now follows the record
 * view's rule (`listCreateRightOf` / `createRightOf`, recordAccess.ts): offered only when the level allows AND the list
 * has answered and allows. The kept exception: a list that answered WITHOUT `canCreate` follows the level.
 *
 * Rendered for real: the page, its Overview and Calendar tabs, `DocumentLibrary`, `EventsCalendar`, `SmartTodo` and
 * `useAccessLevel`. Substituted: the network seam (`bffApiCall`), the project read and the `/me` data source.
 */
import * as React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ApiError } from '../src/types';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

vi.mock('../src/api/web-api-client', async () => {
  const actual = await vi.importActual<typeof import('../src/api/web-api-client')>('../src/api/web-api-client');
  return {
    ...actual,
    getProjectById: vi.fn().mockResolvedValue({
      sprk_projectid: 'p-d9',
      sprk_name: 'Project D9',
      sprk_referencenumber: 'PRJ-D9',
      sprk_status: 1,
      sprk_issecure: false,
    }),
  };
});

// `/me`: a partner (no `writeAccessPerRecord`) with a level on this project. useAccessLevel runs for real.
let level: 'ViewOnly' | 'Collaborate' | 'FullAccess' = 'Collaborate';
vi.mock('../src/hooks/useExternalContext', () => ({
  useExternalContext: () => ({
    context: {
      contactId: 'c-d9',
      email: 'partner@firm.example',
      projects: [{ projectId: 'p-d9', accessLevel: level }],
    },
    isLoading: false,
    error: null,
  }),
}));

import { ProjectPage } from '../src/pages/ProjectPage';

/** How every project list (documents, events, to-dos) answers. */
type ListAnswer = { canCreate: boolean | undefined } | 'pending' | 'failed';

function answerLists(answer: ListAnswer): void {
  bffApiCall.mockImplementation((path: string) => {
    if (!/\/projects\/p-d9\/(documents|events|todos)/.test(path)) {
      return Promise.reject(new Error(`unexpected request ${path}`));
    }
    if (answer === 'pending') return new Promise(() => undefined);
    if (answer === 'failed') return Promise.reject(new ApiError(500, 'Server error'));
    return Promise.resolve({
      value: [],
      ...(answer.canCreate === undefined ? {} : { canCreate: answer.canCreate }),
    });
  });
}

function renderProject(): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={['/project/p-d9']}>
        <Routes>
          <Route path="/project/:id" element={<ProjectPage />} />
        </Routes>
      </MemoryRouter>
    </FluentProvider>
  );
}

const UPLOAD = /Upload Document/i;
const CREATE_EVENT = /Create Event/i;
const ADD_TASK = /Add Task|Create new task/i;

/** Not in the DOM at all: no button by that name, and no disabled placeholder carrying it (hide, don't disable). */
function expectNotOffered(name: RegExp): void {
  expect(screen.queryAllByRole('button', { name })).toHaveLength(0);
  const disabled = Array.from(document.querySelectorAll('button[disabled], [aria-disabled="true"]'));
  expect(
    disabled.filter(el => name.test(el.textContent ?? '') || name.test(el.getAttribute('aria-label') ?? ''))
  ).toEqual([]);
}

/** The Overview tab with its document list settled the given way. */
async function overview(answer: ListAnswer): Promise<void> {
  answerLists(answer);
  renderProject();
  await screen.findByRole('tab', { name: /Overview/ });
  if (answer === 'pending') {
    await screen.findByText('Loading documents...');
  } else if (answer === 'failed') {
    await screen.findByText(/Failed to load documents/);
  } else {
    await screen.findByText('No Documents');
  }
}

/** The Calendar tab with its event and to-do lists settled the given way. */
async function calendar(answer: ListAnswer): Promise<void> {
  answerLists(answer);
  renderProject();
  fireEvent.click(await screen.findByRole('tab', { name: /Calendar/ }));
  if (answer === 'pending') {
    await screen.findByText('Loading events...');
    await screen.findByText('Loading tasks...');
  } else if (answer === 'failed') {
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Try again' })).toHaveLength(2));
  } else {
    await screen.findByText('No events yet');
    await screen.findByText('No tasks yet');
  }
}

beforeEach(() => {
  bffApiCall.mockReset();
  level = 'Collaborate';
});

describe('ProjectPage (D-9): a partner without writeAccessPerRecord is offered a create only when level AND list allow', () => {
  describe('Collaborate, the list says canCreate: true (must fire)', () => {
    it('Upload is offered', async () => {
      await overview({ canCreate: true });
      expect(await screen.findByRole('button', { name: UPLOAD })).toBeTruthy();
    });

    it('Create Event and Add Task are offered', async () => {
      await calendar({ canCreate: true });
      expect(screen.getByRole('button', { name: CREATE_EVENT })).toBeTruthy();
      expect(screen.getByRole('button', { name: ADD_TASK })).toBeTruthy();
    });
  });

  describe('Collaborate, the list says canCreate: false (the workforce-ceiling case; must not fire)', () => {
    it('Upload is not in the DOM, and no disabled placeholder', async () => {
      await overview({ canCreate: false });
      expectNotOffered(UPLOAD);
    });

    it('Create Event and Add Task are not in the DOM, and no disabled placeholder', async () => {
      await calendar({ canCreate: false });
      expectNotOffered(CREATE_EVENT);
      expectNotOffered(ADD_TASK);
    });
  });

  describe('Collaborate, the list is still loading (no offer before the answer)', () => {
    it('Upload is not offered', async () => {
      await overview('pending');
      expectNotOffered(UPLOAD);
    });

    it('Create Event and Add Task are not offered', async () => {
      await calendar('pending');
      expectNotOffered(CREATE_EVENT);
      expectNotOffered(ADD_TASK);
    });
  });

  describe('Collaborate, the list read failed', () => {
    it('Upload is not offered', async () => {
      await overview('failed');
      expectNotOffered(UPLOAD);
    });

    it('Create Event and Add Task are not offered', async () => {
      await calendar('failed');
      expectNotOffered(CREATE_EVENT);
      expectNotOffered(ADD_TASK);
    });
  });

  describe('Collaborate, the loaded list sent no canCreate (the kept exception: the level decides)', () => {
    it('Upload is offered', async () => {
      await overview({ canCreate: undefined });
      expect(await screen.findByRole('button', { name: UPLOAD })).toBeTruthy();
    });

    it('Create Event and Add Task are offered', async () => {
      await calendar({ canCreate: undefined });
      expect(screen.getByRole('button', { name: CREATE_EVENT })).toBeTruthy();
      expect(screen.getByRole('button', { name: ADD_TASK })).toBeTruthy();
    });
  });

  describe('View Only, the list says canCreate: true (the level must allow too; must not fire)', () => {
    beforeEach(() => {
      level = 'ViewOnly';
    });

    it('Upload is not offered', async () => {
      await overview({ canCreate: true });
      expectNotOffered(UPLOAD);
    });

    it('Create Event and Add Task are not offered', async () => {
      await calendar({ canCreate: true });
      expectNotOffered(CREATE_EVENT);
      expectNotOffered(ADD_TASK);
    });
  });
});
