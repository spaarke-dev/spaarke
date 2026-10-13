/**
 * R3 task 057 (FR-03 client) - events and to-dos on the Matter and Work Assignment detail views, generalised from the
 * Project section by root kind.
 *
 * What runs for real: `RecordDetailModal` (every section: fields, documents, events and tasks, messages), `EventsCalendar`,
 * `SmartTodo`, `DocumentLibrary`, `RecordMessages`, the shared `SprkModal` / `ConfirmModal`, `BffAccessState`, `delete-own`,
 * `web-api-client` (the routes) and Fluent v9. What is substituted: the network seam (`bffApiCall`, answering the external
 * routes and throwing `ApiError` exactly as bff-client builds it) and MSAL's `useMsal` (BffAccessState reads it).
 *
 * The SERVER is the authority (task 039: the record's own rights plus the every-root rule, which withholds a row filed to
 * a root the caller cannot read). These tests prove the SPA reads the record's own routes, shows what comes back without
 * filtering it itself, shows the empty / denied / error states inside the section only, and keeps Project, Invoice and
 * Service Request exactly as they were.
 */
import * as React from 'react';
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  // `getExternalUserContext` calls the module's own bffApiCall, so it is routed to the substituted seam as well.
  return { ...actual, bffApiCall, getExternalUserContext: () => bffApiCall('/api/v1/external/me') };
});

const { loginRedirect } = vi.hoisted(() => ({ loginRedirect: vi.fn() }));
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ instance: { loginRedirect }, accounts: [], inProgress: 'none' }),
}));

import { AccessLevel, ApiError } from '../src/types';
import { EventsCalendar } from '../src/components/EventsCalendar';
import { SmartTodo } from '../src/components/SmartTodo';
import { RecordDetailModal } from '../src/components/detail/RecordDetailModal';
import { getRootEvents, getRootTodos, getEvents, getProjectTodos } from '../src/api/web-api-client';
import type { CallerIdentity } from '../src/api/delete-own';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SLOW = { timeout: 60000 };
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

const ME = guid(7);
const SOMEONE = guid(8);

const PROJECT = guid(101);
const MATTER = guid(201);
const WORK_ASSIGNMENT = guid(301);
const SERVICE_REQUEST = guid(601);
const INVOICE = guid(501);

const asContact: CallerIdentity = { contactId: ME, systemUserId: null };

const problem = (status: number, extra: Record<string, unknown> = {}): string =>
  JSON.stringify({ status, title: 'refused', ...extra });

/** The six detail entities this view can open, with the id column the id-filtered fetch reads and a row for it. */
const ENTITY = {
  project: {
    entity: 'sprk_project',
    id: PROJECT,
    idAttribute: 'sprk_projectid',
    row: { sprk_projectname: 'Project P' },
  },
  matter: { entity: 'sprk_matter', id: MATTER, idAttribute: 'sprk_matterid', row: { sprk_mattername: 'Matter M' } },
  wa: {
    entity: 'sprk_workassignment',
    id: WORK_ASSIGNMENT,
    idAttribute: 'sprk_workassignmentid',
    row: { sprk_name: 'Work W' },
  },
  sr: {
    entity: 'sprk_servicerequest',
    id: SERVICE_REQUEST,
    idAttribute: 'sprk_servicerequestid',
    row: { sprk_name: 'Request R' },
  },
  invoice: { entity: 'sprk_invoice', id: INVOICE, idAttribute: 'sprk_invoiceid', row: { sprk_name: 'Invoice I' } },
} as const;
type Key = keyof typeof ENTITY;

/** The URL segment each events/to-dos-bearing record kind reads from. */
const ROOT_OF: Record<'project' | 'matter' | 'wa', string> = {
  project: 'projects',
  matter: 'matters',
  wa: 'workassignments',
};

const event = (n: number, name: string, stamps: Record<string, string> = {}) => ({
  sprk_eventid: guid(800 + n),
  sprk_name: name,
  sprk_duedate: '2026-03-01',
  sprk_status: 659490001,
  ...stamps,
});

const todo = (n: number, name: string, stamps: Record<string, string> = {}) => ({
  sprk_todoid: guid(700 + n),
  sprk_name: name,
  statuscode: 1,
  ...stamps,
});

/** What the network answers for the events and to-do lists: rows, or a refusal / failure to throw. */
type ListAnswer = { value: Array<Record<string, unknown>>; truncated?: boolean } | { throws: ApiError };

interface Net {
  events: ListAnswer;
  todos: ListAnswer;
  /** Answers for a retry: the first read of a list is `events` / `todos`, later reads are these (when set). */
  eventsNext?: ListAnswer;
  todosNext?: ListAnswer;
  onDelete?: (path: string) => unknown;
}

const answer = (a: ListAnswer): unknown => {
  if ('throws' in a) throw a.throws;
  return a;
};

const listCalls = (suffix: 'events' | 'todos'): string[] =>
  bffApiCall.mock.calls.map(c => String(c[0])).filter(p => new RegExp(`/${suffix}$`).test(p));

/** The reads every detail view makes besides the lists: the record, who the caller is, its documents and its thread. */
function installNet(key: Key, net: Net): void {
  const e = ENTITY[key];
  const reads = { events: 0, todos: 0 };
  bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    if (options?.method === 'DELETE') return net.onDelete ? net.onDelete(path) : undefined;
    if (path === '/api/v1/external/me') return { contactId: ME, email: 'me@example.test', projects: [] };
    if (path.endsWith('/api/dataverse/fetch')) {
      const body = JSON.parse(String(options?.body)) as { entityName: string };
      return body.entityName === e.entity
        ? { entities: [{ [e.idAttribute]: e.id, ...e.row }], moreRecords: false }
        : { entities: [], moreRecords: false };
    }
    if (/\/documents$/.test(path)) return { value: [], canCreate: false };
    if (/\/messages$/.test(path)) return { value: [] };
    if (/\/events$/.test(path)) {
      reads.events += 1;
      return answer(reads.events > 1 && net.eventsNext ? net.eventsNext : net.events);
    }
    if (/\/todos$/.test(path)) {
      reads.todos += 1;
      return answer(reads.todos > 1 && net.todosNext ? net.todosNext : net.todos);
    }
    throw new Error(`unexpected BFF call ${options?.method ?? 'GET'} ${path}`);
  });
}

const empty: ListAnswer = { value: [] };

function renderModal(key: Key): ReturnType<typeof render> {
  const e = ENTITY[key];
  return render(
    <FluentProvider theme={webLightTheme}>
      <RecordDetailModal
        open
        onClose={() => undefined}
        entityName={e.entity}
        recordIds={[e.id]}
        index={0}
        onIndexChange={() => undefined}
      />
    </FluentProvider>
  );
}

async function openDetail(key: Key): Promise<HTMLElement> {
  renderModal(key);
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-detail-fields');
  return dialog;
}

/** jsdom does no layout; Fluent's focus manager needs geometry to leave the nested confirmation reachable. */
const realOffsetParent = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetParent');
function installLayout(): void {
  Object.defineProperty(HTMLElement.prototype, 'offsetParent', {
    configurable: true,
    get(this: HTMLElement) {
      return this.parentNode;
    },
  });
  vi.spyOn(document.body, 'getBoundingClientRect').mockReturnValue({
    x: 0,
    y: 0,
    top: 0,
    left: 0,
    right: 1024,
    bottom: 768,
    width: 1024,
    height: 768,
    toJSON: () => ({}),
  });
}

beforeEach(() => {
  installLayout();
  bffApiCall.mockReset();
  loginRedirect.mockReset();
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  if (realOffsetParent) Object.defineProperty(HTMLElement.prototype, 'offsetParent', realOffsetParent);
});

const STATIC_EMPTY = 'No events or tasks to show for this record yet.';

// ---------------------------------------------------------------------------
// The client's routes
// ---------------------------------------------------------------------------

describe('the events and to-do reads, by root kind', () => {
  beforeEach(() => {
    bffApiCall.mockImplementation(async () => ({ value: [], truncated: true }));
  });

  it.each([
    ['projects', PROJECT],
    ['matters', MATTER],
    ['workassignments', WORK_ASSIGNMENT],
  ] as const)(
    '%s: getRootEvents / getRootTodos call /api/v1/external/%s/{id}/events and /todos and carry `truncated`',
    async (root, id) => {
      const events = await getRootEvents(root, id);
      const todos = await getRootTodos(root, id);
      expect(bffApiCall.mock.calls.map(c => c[0])).toEqual([
        `/api/v1/external/${root}/${id}/events`,
        `/api/v1/external/${root}/${id}/todos`,
      ]);
      expect(events).toEqual({ items: [], truncated: true });
      expect(todos).toEqual({ items: [], truncated: true });
    }
  );

  it('the project functions keep their URLs byte for byte', async () => {
    await getEvents(PROJECT);
    await getProjectTodos(PROJECT);
    expect(bffApiCall.mock.calls.map(c => c[0])).toEqual([
      `/api/v1/external/projects/${PROJECT}/events`,
      `/api/v1/external/projects/${PROJECT}/todos`,
    ]);
  });
});

// ---------------------------------------------------------------------------
// Matter and Work Assignment: the rows
// ---------------------------------------------------------------------------

describe.each([
  ['matter', 'matters', MATTER],
  ['wa', 'workassignments', WORK_ASSIGNMENT],
] as const)('%s detail view', (key, root, id) => {
  it(
    `reads /${root}/{id}/events and /todos, renders the rows, and no longer shows the static empty state`,
    async () => {
      installNet(key, {
        events: {
          value: [
            // Events of other roots carry a null project lookup here; the client shows whatever the server returned.
            { ...event(1, 'Hearing'), _sprk_regardingproject_value: null },
            { ...event(2, 'Filing deadline'), _sprk_regardingproject_value: guid(999) },
          ],
        },
        todos: { value: [todo(1, 'Draft the response'), todo(2, 'Collect exhibits')] },
      });
      const dialog = await openDetail(key);

      const section = within(dialog).getByTestId('record-detail-events');
      await within(section).findByText('Hearing');
      await within(section).findByText('Filing deadline');
      await within(section).findByText('Draft the response');
      await within(section).findByText('Collect exhibits');

      expect(within(dialog).queryByText(STATIC_EMPTY)).toBeNull();
      expect(within(dialog).queryByTestId('record-detail-events-unavailable')).toBeNull();
      expect(listCalls('events')).toEqual([`/api/v1/external/${root}/${id}/events`]);
      expect(listCalls('todos')).toEqual([`/api/v1/external/${root}/${id}/todos`]);
      // Never the project's routes for these records.
      expect(bffApiCall.mock.calls.some(c => String(c[0]).includes('/projects/'))).toBe(false);
    },
    SLOW
  );

  it(
    'read-only: no create or status toggle is offered, whatever the rows are',
    async () => {
      installNet(key, { events: { value: [event(1, 'Hearing')] }, todos: { value: [todo(1, 'Draft the response')] } });
      const dialog = await openDetail(key);
      await within(dialog).findByText('Hearing');
      await within(dialog).findByText('Draft the response');

      expect(within(dialog).queryByRole('button', { name: /create event/i })).toBeNull();
      expect(within(dialog).queryByRole('button', { name: /create new task|add task/i })).toBeNull();
      // The status toggle is the same gate: a read-only list has no checkbox-style control on its rows.
      expect(within(dialog).queryByRole('button', { name: /mark .* (complete|open)/i })).toBeNull();
      // Nothing is written to the lists (the id-filtered record fetch is also a POST, so only the list routes count).
      const writes = bffApiCall.mock.calls.filter(
        c => (c[1] as RequestInit | undefined)?.method === 'POST' && /\/(events|todos)$/.test(String(c[0]))
      );
      expect(writes).toHaveLength(0);
    },
    SLOW
  );

  it(
    'empty lists show the same empty states as the project section, naming the record',
    async () => {
      installNet(key, { events: empty, todos: empty });
      const dialog = await openDetail(key);

      const section = within(dialog).getByTestId('record-detail-events');
      await within(section).findByText('No events yet');
      await within(section).findByText('No tasks yet');
      const noun = key === 'matter' ? 'matter' : 'work assignment';
      expect(within(section).getByText(`No events have been added to this ${noun} yet.`)).toBeTruthy();
      expect(within(dialog).queryByText(STATIC_EMPTY)).toBeNull();
    },
    SLOW
  );

  it(
    'a cut-short empty read says it could not be read, not "no events" (task 105)',
    async () => {
      installNet(key, { events: { value: [], truncated: true }, todos: { value: [], truncated: true } });
      const dialog = await openDetail(key);

      const section = within(dialog).getByTestId('record-detail-events');
      await waitFor(() => expect(within(section).getAllByTestId('truncated-list-notice')).toHaveLength(2));
      expect(within(section).queryByText('No events yet')).toBeNull();
      expect(within(section).queryByText('No tasks yet')).toBeNull();
    },
    SLOW
  );

  it(
    'a 403 on the lists shows the denied state in the section only; the rest of the detail view is intact',
    async () => {
      const refused = { throws: new ApiError(403, problem(403, { title: 'Forbidden' })) };
      installNet(key, { events: refused, todos: refused });
      const dialog = await openDetail(key);

      const section = within(dialog).getByTestId('record-detail-events');
      await waitFor(() => expect(within(section).getAllByTestId('bff-access-denied')).toHaveLength(2));
      expect(section.textContent).toContain(`permission to view events on this`);
      expect(section.textContent).toContain(`permission to view tasks on this`);
      // None of the rest is affected: fields, documents and messages are all still there.
      expect(within(dialog).getByTestId('record-detail-fields')).toBeTruthy();
      expect(within(dialog).getByTestId('record-detail-documents')).toBeTruthy();
      expect(within(dialog).getByTestId('record-detail-messages')).toBeTruthy();
      expect(within(dialog).queryByTestId('record-detail-denied')).toBeNull();
      expect(within(dialog).queryByRole('button', { name: /try again/i })).toBeNull();
    },
    SLOW
  );

  it(
    'a 5xx shows a retryable error in the section only; Try again re-reads the same route and lists the rows',
    async () => {
      const failed = { throws: new ApiError(503, problem(503)) };
      installNet(key, {
        events: failed,
        todos: failed,
        eventsNext: { value: [event(1, 'Hearing')] },
        todosNext: { value: [todo(1, 'Draft the response')] },
      });
      const dialog = await openDetail(key);

      const section = within(dialog).getByTestId('record-detail-events');
      const retries = await within(section).findAllByRole('button', { name: /try again/i });
      expect(retries).toHaveLength(2);
      expect(within(section).queryByText(STATIC_EMPTY)).toBeNull();
      expect(within(dialog).getByTestId('record-detail-fields')).toBeTruthy();
      expect(within(dialog).getByTestId('record-detail-documents')).toBeTruthy();
      expect(within(dialog).queryByTestId('record-detail-error')).toBeNull();

      for (const button of retries) fireEvent.click(button);
      await within(section).findByText('Hearing');
      await within(section).findByText('Draft the response');
      expect(listCalls('events')).toEqual([
        `/api/v1/external/${root}/${id}/events`,
        `/api/v1/external/${root}/${id}/events`,
      ]);
      expect(listCalls('todos')).toEqual([
        `/api/v1/external/${root}/${id}/todos`,
        `/api/v1/external/${root}/${id}/todos`,
      ]);
    },
    SLOW
  );

  it(
    'a 5xx with a non-problem body (an HTML 503 page) shows a plain message, never the raw body',
    async () => {
      const html = { throws: new ApiError(503, '<!DOCTYPE html><html><body>Service Unavailable</body></html>') };
      installNet(key, { events: html, todos: html });
      const dialog = await openDetail(key);

      const section = within(dialog).getByTestId('record-detail-events');
      await within(section).findByText('Events could not be loaded just now.');
      await within(section).findByText('Tasks could not be loaded just now.');
      expect(section.textContent).not.toMatch(/DOCTYPE|<html|Service Unavailable/);
    },
    SLOW
  );

  it(
    'a 5xx ProblemDetails body shows its detail, not the JSON document',
    async () => {
      const failed = { throws: new ApiError(500, problem(500, { detail: 'The record context is missing.' })) };
      installNet(key, { events: failed, todos: { value: [] } });
      const dialog = await openDetail(key);

      const section = within(dialog).getByTestId('record-detail-events');
      await within(section).findByText('The record context is missing.');
      expect(section.textContent).not.toMatch(/"status"|"title"/);
    },
    SLOW
  );

  it(
    'delete-own on a to-do removes the row on a 204',
    async () => {
      installNet(key, {
        events: empty,
        todos: {
          value: [
            todo(1, 'My task', { _sprk_createdbycontact_value: ME }),
            todo(2, 'Their task', { _sprk_createdbycontact_value: SOMEONE }),
          ],
        },
      });
      const dialog = await openDetail(key);
      const section = within(dialog).getByTestId('record-detail-events');
      fireEvent.click(await within(section).findByRole('button', { name: 'Delete task My task' }));
      const heading = await screen.findByText('Delete task');
      const confirm = heading.closest('[role="dialog"], [role="alertdialog"]') as HTMLElement;
      await new Promise(resolve => setTimeout(resolve, 350));
      fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));

      await waitFor(() => expect(within(section).queryByText('My task')).toBeNull());
      const deletes = bffApiCall.mock.calls.filter(c => (c[1] as RequestInit | undefined)?.method === 'DELETE');
      expect(deletes).toHaveLength(1);
      expect(deletes[0][0]).toMatch(/^\/api\/v1\/external\/todos\//);
      expect(within(section).getByText('Their task')).toBeTruthy();
    },
    SLOW
  );

  it(
    'a 401 on the lists is the shared sign-in state, not an error message',
    async () => {
      const expired = { throws: new ApiError(401, '') };
      installNet(key, { events: expired, todos: expired });
      const dialog = await openDetail(key);

      const section = within(dialog).getByTestId('record-detail-events');
      await waitFor(() => expect(within(section).getAllByTestId('bff-access-signin')).toHaveLength(2));
      expect(within(dialog).getByTestId('record-detail-fields')).toBeTruthy();
    },
    SLOW
  );

  it(
    'delete-own is offered only on rows whose creator stamp is the caller, and removes the row on a 204',
    async () => {
      installNet(key, {
        events: {
          value: [
            event(1, 'My hearing', { _sprk_createdbycontact_value: ME }),
            event(2, 'Their hearing', { _sprk_createdbycontact_value: SOMEONE }),
            event(3, 'Unstamped hearing'),
          ],
        },
        todos: {
          value: [
            todo(1, 'My task', { _sprk_createdbycontact_value: ME }),
            todo(2, 'Their task', { _sprk_createdbycontact_value: SOMEONE }),
          ],
        },
      });
      const dialog = await openDetail(key);
      const section = within(dialog).getByTestId('record-detail-events');
      await within(section).findByText('My hearing');
      await within(section).findByText('My task');

      // Once /me has answered, exactly the caller's own rows offer Delete.
      await waitFor(() =>
        expect(within(section).queryAllByRole('button', { name: /^Delete (task |event )/ })).toHaveLength(2)
      );
      expect(within(section).getByRole('button', { name: 'Delete event My hearing' })).toBeTruthy();
      expect(within(section).getByRole('button', { name: 'Delete task My task' })).toBeTruthy();
      expect(within(section).queryByRole('button', { name: /Their|Unstamped/ })).toBeNull();

      // The server decides: a 204 removes the event; the other rows stay.
      fireEvent.click(within(section).getByRole('button', { name: 'Delete event My hearing' }));
      const heading = await screen.findByText('Delete event');
      const confirm = heading.closest('[role="dialog"], [role="alertdialog"]') as HTMLElement;
      await new Promise(resolve => setTimeout(resolve, 350));
      fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));
      await waitFor(() => expect(within(section).queryByText('My hearing')).toBeNull());
      expect(bffApiCall.mock.calls.filter(c => (c[1] as RequestInit | undefined)?.method === 'DELETE')).toEqual([
        [`/api/v1/external/events/${guid(801)}`, { method: 'DELETE' }],
      ]);
      expect(within(section).getByText('Their hearing')).toBeTruthy();
    },
    SLOW
  );

  it(
    'a refused delete (403) shows an error and the row stays',
    async () => {
      installNet(key, {
        events: { value: [event(1, 'My hearing', { _sprk_createdbycontact_value: ME })] },
        todos: empty,
        onDelete: () => {
          throw new ApiError(403, problem(403, { reasonCode: 'sdap.access.deny.delete_own' }));
        },
      });
      const dialog = await openDetail(key);
      const section = within(dialog).getByTestId('record-detail-events');
      fireEvent.click(await within(section).findByRole('button', { name: 'Delete event My hearing' }));
      const heading = await screen.findByText('Delete event');
      const confirm = heading.closest('[role="dialog"], [role="alertdialog"]') as HTMLElement;
      await new Promise(resolve => setTimeout(resolve, 350));
      fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));

      await screen.findByText(/cannot delete this event.*Nothing was deleted/);
      expect(within(section).getByText('My hearing')).toBeTruthy();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Negative: Project unchanged; Invoice and Service Request make no events/to-dos calls
// ---------------------------------------------------------------------------

describe('the other detail views', () => {
  it(
    'Project still calls /projects/{id}/events and /todos, and nothing on a matter or work assignment route',
    async () => {
      installNet('project', {
        events: { value: [event(1, 'Signing call')] },
        todos: { value: [todo(1, 'Review draft')] },
      });
      const dialog = await openDetail('project');
      const section = within(dialog).getByTestId('record-detail-events');
      await within(section).findByText('Signing call');
      await within(section).findByText('Review draft');

      expect(listCalls('events')).toEqual([`/api/v1/external/projects/${PROJECT}/events`]);
      expect(listCalls('todos')).toEqual([`/api/v1/external/projects/${PROJECT}/todos`]);
      expect(bffApiCall.mock.calls.some(c => /\/(matters|workassignments)\//.test(String(c[0])))).toBe(false);
      // Read-only in the detail view, as before.
      expect(within(dialog).queryByRole('button', { name: /create event/i })).toBeNull();
      expect(within(dialog).queryByRole('button', { name: /create new task|add task/i })).toBeNull();
    },
    SLOW
  );

  it(
    'Project: the empty states still name the project',
    async () => {
      installNet('project', { events: empty, todos: empty });
      const dialog = await openDetail('project');
      await within(dialog).findByText('No events have been added to this project yet.');
      await within(dialog).findByText('No tasks yet');
    },
    SLOW
  );

  it.each(['invoice', 'sr'] as const)(
    '%s: no events section and no events or to-do call at all',
    async key => {
      installNet(key, { events: empty, todos: empty });
      const dialog = await openDetail(key);
      await within(dialog).findByTestId('record-detail-messages');
      await within(dialog).findByTestId('record-detail-documents');

      expect(within(dialog).queryByTestId('record-detail-events')).toBeNull();
      expect(within(dialog).queryByText(STATIC_EMPTY)).toBeNull();
      expect(listCalls('events')).toHaveLength(0);
      expect(listCalls('todos')).toHaveLength(0);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// The shared components on a non-project root
// ---------------------------------------------------------------------------

describe('root-aware EventsCalendar and SmartTodo', () => {
  it(
    'a matter or work assignment list is read-only even at Collaborate; a project at Collaborate can still create',
    async () => {
      bffApiCall.mockImplementation(async () => ({ value: [] }));
      const { rerender } = render(
        <FluentProvider theme={webLightTheme}>
          <EventsCalendar projectId={MATTER} root="matters" accessLevel={AccessLevel.Collaborate} caller={asContact} />
          <SmartTodo projectId={MATTER} root="matters" accessLevel={AccessLevel.Collaborate} caller={asContact} />
        </FluentProvider>
      );
      await screen.findByText('No events yet');
      await screen.findByText('No tasks yet');
      expect(screen.queryByRole('button', { name: /create event/i })).toBeNull();
      expect(screen.queryByRole('button', { name: /create new task/i })).toBeNull();
      expect(screen.queryByText(/Use the Create Event button/)).toBeNull();

      rerender(
        <FluentProvider theme={webLightTheme}>
          <EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.Collaborate} caller={asContact} />
          <SmartTodo projectId={PROJECT} accessLevel={AccessLevel.Collaborate} caller={asContact} />
        </FluentProvider>
      );
      await screen.findByRole('button', { name: /create event/i });
      await screen.findByRole('button', { name: /create new task/i });
    },
    SLOW
  );
});
