/**
 * R4 task 006 (FR-06, FR-07, NFR-04) — what the record template offers, from what the BFF answered for THIS caller on
 * THIS record: hide, don't disable; no offered action may answer 403 by design; "Your access" in the header; one "why"
 * line per read-only section.
 *
 * What runs for real: `RecordTemplateModal` with every real section (Documents, Messages, Tasks & events, Invoices), the
 * toolbar (`RecordCommandBar`), `DocumentLibrary`, `RecordMessages`, `EventsCalendar`, `SmartTodo`, their create dialogs,
 * `recordAccess.ts`, `web-api-client` (the routes and bodies) and Fluent v9. What is substituted: the network seam
 * (`bffApiCall`, answering the external routes and throwing `ApiError` exactly as bff-client builds it), MSAL's `useMsal`
 * (BffAccessState reads it) and jsdom's missing layout (`installLayout`).
 *
 * THE CONTRACT THESE TESTS PIN (task 002 made each list's `canCreate` the matching POST's own decision for this caller):
 *   list `canCreate: true`  -> the action is offered, and the POST it makes answers 201 (ExternalRootEventCreateContractTests
 *                              "list canCreate = POST" cases; the to-do list's `canCreate` tests);
 *   list `canCreate: false` -> the action is NOT rendered (never disabled), and the section says why;
 *   list not answered (failed, still loading, `canCreate` omitted) -> not rendered, and nothing is said (fail closed).
 * Every "offered" assertion has a "not offered" twin below, and every negative is asserted only after the list has
 * answered (or after a settle long enough for it to have), so an absent control cannot pass merely because it is early.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, getExternalUserContext: () => bffApiCall('/api/v1/external/me') };
});

const { loginRedirect } = vi.hoisted(() => ({ loginRedirect: vi.fn() }));
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ instance: { loginRedirect }, accounts: [], inProgress: 'none' }),
}));

import { AccessLevel, ApiError } from '../src/types';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import { RECORD_TEMPLATES } from '../src/components/record/recordTemplates';
import { RECORD_ACCESS_TEXT } from '../src/components/record/recordAccess';
import type { RecordSectionContext, RecordSectionId } from '../src/components/record/recordTemplate.types';
import type { CreateActionOffer } from '../src/components/createAction';
import { SmartTodo } from '../src/components/SmartTodo';
import { EventsCalendar } from '../src/components/EventsCalendar';
import { RecordMessages } from '../src/components/detail/RecordMessages';
import { installLayout, uninstallLayout } from './jsdomLayout';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SLOW = { timeout: 60000 };
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const ME = guid(7);
const SOMEONE = guid(8);

const RECORDS = {
  matter: {
    entity: 'sprk_matter',
    id: guid(201),
    idAttribute: 'sprk_matterid',
    root: 'matters',
    row: { sprk_mattername: 'Matter M' },
  },
  wa: {
    entity: 'sprk_workassignment',
    id: guid(301),
    idAttribute: 'sprk_workassignmentid',
    root: 'workassignments',
    row: { sprk_name: 'Work W' },
  },
  project: {
    entity: 'sprk_project',
    id: guid(101),
    idAttribute: 'sprk_projectid',
    root: 'projects',
    row: { sprk_projectname: 'Project P' },
  },
  invoice: {
    entity: 'sprk_invoice',
    id: guid(401),
    idAttribute: 'sprk_invoiceid',
    root: 'invoices',
    row: { sprk_name: 'Invoice I', sprk_invoicenumber: 'INV-1' },
  },
} as const;
type RecordKey = keyof typeof RECORDS;

/** One list's answer: rows and `canCreate` (absent = the BFF said nothing), a thrown failure, or a read that never ends. */
type ListAnswer = { value: Array<Record<string, unknown>>; canCreate?: boolean } | { throws: ApiError } | 'pending';

interface Net {
  documents: ListAnswer;
  messages: ListAnswer;
  todos: ListAnswer;
  events: ListAnswer;
  /** `/me`: the caller context, or 'fail'. */
  me?: Record<string, unknown> | 'fail';
  onCreateTodo?: (path: string, body: Record<string, unknown>) => unknown;
  onCreateEvent?: (path: string, body: Record<string, unknown>) => unknown;
  onDelete?: (path: string) => unknown;
}

const may = (rows: Array<Record<string, unknown>> = []): ListAnswer => ({ value: rows, canCreate: true });
const mayNot = (rows: Array<Record<string, unknown>> = []): ListAnswer => ({ value: rows, canCreate: false });
const silent = (rows: Array<Record<string, unknown>> = []): ListAnswer => ({ value: rows });
const fails: ListAnswer = { throws: new ApiError(500, JSON.stringify({ status: 500, title: 'broken' })) };

const all = (a: ListAnswer): Pick<Net, 'documents' | 'messages' | 'todos' | 'events'> => ({
  documents: a,
  messages: a,
  todos: a,
  events: a,
});

const meOf = (extra: Record<string, unknown> = {}): Record<string, unknown> => ({
  contactId: ME,
  email: 'me@example.test',
  projects: [],
  ...extra,
});

const levelByLabel: Readonly<Record<string, AccessLevel>> = {
  ViewOnly: AccessLevel.ViewOnly,
  Collaborate: AccessLevel.Collaborate,
  FullAccess: AccessLevel.FullAccess,
};

const message = (n: number, body: string): Record<string, unknown> => ({
  sprk_communicationid: guid(950 + n),
  sprk_body: body,
  sprk_bodyformat: 100000000,
  createdon: '2026-03-01T10:00:00Z',
});

const todo = (n: number, name: string, stamps: Record<string, unknown> = {}): Record<string, unknown> => ({
  sprk_todoid: guid(700 + n),
  sprk_name: name,
  statuscode: 1,
  ...stamps,
});
const event = (n: number, name: string, stamps: Record<string, unknown> = {}): Record<string, unknown> => ({
  sprk_eventid: guid(800 + n),
  sprk_name: name,
  sprk_duedate: '2026-03-01',
  sprk_status: 659490001,
  ...stamps,
});

const answer = (a: ListAnswer): unknown => {
  if (a === 'pending') return new Promise(() => undefined);
  if ('throws' in a) throw a.throws;
  return a;
};

function installNet(key: RecordKey, net: Net): void {
  const r = RECORDS[key];
  bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    const method = options?.method ?? 'GET';
    if (method === 'DELETE') return net.onDelete ? net.onDelete(path) : undefined;
    if (path === '/api/v1/external/me') {
      if (net.me === 'fail') throw new ApiError(500, '');
      return net.me ?? meOf();
    }
    if (path.endsWith('/api/dataverse/fetch')) {
      const body = JSON.parse(String(options?.body)) as { entityName: string };
      return body.entityName === r.entity
        ? { entities: [{ [r.idAttribute]: r.id, ...r.row }], moreRecords: false }
        : { entities: [], moreRecords: false };
    }
    if (/\/documents$/.test(path)) return answer(net.documents);
    if (/\/messages$/.test(path)) return answer(net.messages);
    if (/\/todos$/.test(path)) {
      if (method === 'POST') {
        if (!net.onCreateTodo) throw new Error(`unexpected to-do POST ${path}`);
        return net.onCreateTodo(path, JSON.parse(String(options?.body)));
      }
      return answer(net.todos);
    }
    if (/\/events$/.test(path)) {
      if (method === 'POST') {
        if (!net.onCreateEvent) throw new Error(`unexpected event POST ${path}`);
        return net.onCreateEvent(path, JSON.parse(String(options?.body)));
      }
      return answer(net.events);
    }
    throw new Error(`unexpected BFF call ${method} ${path}`);
  });
}

const postsTo = (pattern: RegExp): Array<[string, Record<string, unknown>]> =>
  bffApiCall.mock.calls
    .filter(c => (c[1] as RequestInit | undefined)?.method === 'POST' && pattern.test(String(c[0])))
    .map(c => [String(c[0]), JSON.parse(String((c[1] as RequestInit).body)) as Record<string, unknown>]);

const settle = (ms = 60): Promise<void> =>
  act(async () => void (await new Promise(resolve => setTimeout(resolve, ms))));

beforeEach(() => {
  installLayout();
  bffApiCall.mockReset();
  loginRedirect.mockReset();
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  uninstallLayout();
});

// ---------------------------------------------------------------------------
// Harness
// ---------------------------------------------------------------------------

async function openRecord(key: RecordKey): Promise<HTMLElement> {
  const r = RECORDS[key];
  render(
    <FluentProvider theme={webLightTheme}>
      <RecordTemplateModal
        open
        onClose={() => undefined}
        config={RECORD_TEMPLATES[r.entity]}
        recordIds={[r.id]}
        index={0}
        onIndexChange={() => undefined}
      />
    </FluentProvider>
  );
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-detail-fields');
  return dialog;
}

/** The ids of the toolbar buttons that are rendered, in order. */
const toolbarOf = (dialog: HTMLElement): string[] =>
  within(dialog)
    .queryAllByTestId(/^header-toolbar-button-/)
    .map(b => (b.getAttribute('data-testid') ?? '').replace('header-toolbar-button-', ''));

const yourAccess = (dialog: HTMLElement): string | null =>
  within(dialog).queryByTestId('record-template-access')?.querySelector('dd')?.textContent ?? null;

const noteOf = (dialog: HTMLElement, section: 'documents' | 'files' | 'messages' | 'tasksEvents'): string | null =>
  within(dialog).queryByTestId(`record-access-note-${section}`)?.textContent ?? null;

/**
 * No create control is on the record, under any of its names (the toolbar's, and the lists' own: DocumentLibrary's
 * "Upload Document", EventsCalendar's "Create Event", SmartTodo's "Add Task" / "Create new task"), and nothing anywhere in
 * the record dialog is merely disabled (FR-07: hide, don't disable). The one exception is the modal's own record
 * navigation (‹ ›), which SprkModal disables at the ends of the browse set: it is paging, not an action on the record.
 */
function expectNoDisabledCreateControl(dialog: HTMLElement): void {
  for (const name of [
    /^upload/i,
    /^new message/i,
    /^add to-do/i,
    /^add event/i,
    /^create event/i,
    /^add task/i,
    /^create new task/i,
  ]) {
    expect(within(dialog).queryByRole('button', { name, hidden: true })).toBeNull();
  }
  const nav = dialog.querySelector('[role="group"][aria-label="Record navigation"]');
  const disabled = Array.from(dialog.querySelectorAll('button[disabled], [aria-disabled="true"]')).filter(
    el => !nav?.contains(el)
  );
  expect(disabled.map(el => el.outerHTML.slice(0, 120))).toEqual([]);
}

const ALL_CREATE = ['refresh', 'upload', 'newMessage', 'addTodo', 'addEvent', 'copyLink'];
const READ_ONLY_TOOLBAR = ['refresh', 'copyLink'];

// ---------------------------------------------------------------------------
// Matter and work assignment: the list's canCreate decides (no /me level exists for them)
// ---------------------------------------------------------------------------

describe.each(['matter', 'wa'] as const)('%s: the toolbar follows each list canCreate', key => {
  // Both caller kinds: a partner / workforce contact (no `writeAccessPerRecord`: /me has no level for a matter or work
  // assignment, so the list alone decides) and a per-record (Dataverse-decided) caller. Fix round 1 H: every "offered"
  // case used to run only with the per-record flag, which left a level applied to a non-project root unpinned.
  it.each([
    ['a partner (no writeAccessPerRecord)', meOf()],
    ['a per-record caller', meOf({ writeAccessPerRecord: true })],
  ])(
    'Create holder (every list canCreate: true), %s: Upload, New message, Add to-do and Add event are offered; "Can contribute"; no read-only line',
    async (_caller, me) => {
      installNet(key, { ...all(may()), me });
      const dialog = await openRecord(key);

      await waitFor(() => expect(toolbarOf(dialog)).toEqual(ALL_CREATE));
      expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.canContribute);
      expect(noteOf(dialog, 'documents')).toBeNull();
      expect(noteOf(dialog, 'messages')).toBeNull();
      expect(noteOf(dialog, 'tasksEvents')).toBeNull();
      // Submit invoice is in the toolbar config but has no handler: never shown (not part of this task).
      expect(toolbarOf(dialog)).not.toContain('submitInvoice');
    },
    SLOW
  );

  it(
    'View-only (every list canCreate: false): only Refresh and Copy link; nothing disabled; "Read only"; each section says why (no grant level named for a per-record caller)',
    async () => {
      installNet(key, { ...all(mayNot()), me: meOf({ writeAccessPerRecord: true }) });
      const dialog = await openRecord(key);

      // "Read only" needs every list to have answered `false`, so the hidden assertions below run after all four.
      await waitFor(() => expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.readOnly));
      expect(toolbarOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expectNoDisabledCreateControl(dialog);
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents, but not upload to this record.');
      expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.');
      expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these tasks and events, but not add to them.');
      expect(within(dialog).queryByText(/Collaborate/)).toBeNull();
    },
    SLOW
  );

  it(
    'a partner (no writeAccessPerRecord) also gets the neutral lines: no /me level is known to be the reason on this record',
    async () => {
      // Fix round 1 B: a list says only `false`, not why (a View Only grant, a role ceiling, ...). A grant level is named
      // only where the deciding /me level is known to be View Only (a project).
      installNet(key, { ...all(mayNot()), me: meOf() });
      const dialog = await openRecord(key);

      await waitFor(() => expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.readOnly));
      expect(toolbarOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents, but not upload to this record.');
      expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.');
      expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these tasks and events, but not add to them.');
      expect(within(dialog).queryByText(/Collaborate/)).toBeNull();
    },
    SLOW
  );

  it(
    'each list decides its own action: to-dos yes, events no -> Add to-do only, and the events line',
    async () => {
      installNet(key, {
        documents: mayNot(),
        messages: may(),
        todos: may(),
        events: mayNot(),
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord(key);

      await waitFor(() => expect(toolbarOf(dialog)).toEqual(['refresh', 'newMessage', 'addTodo', 'copyLink']));
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents, but not upload to this record.');
      expect(noteOf(dialog, 'messages')).toBeNull();
      expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.canContribute);
    },
    SLOW
  );

  it(
    'and the mirror: events yes, to-dos no -> Add event only, and the to-dos line',
    async () => {
      installNet(key, {
        documents: may(),
        messages: mayNot(),
        todos: mayNot(),
        events: may(),
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord(key);

      await waitFor(() => expect(toolbarOf(dialog)).toEqual(['refresh', 'upload', 'addEvent', 'copyLink']));
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these tasks, but not add one.'));
      expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.');
      expect(noteOf(dialog, 'documents')).toBeNull();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Fail closed: a list that has not answered offers nothing and says nothing
// ---------------------------------------------------------------------------

describe('fail closed: no answer = not offered, nothing said', () => {
  it.each([
    ['fails', fails],
    ['is still loading', 'pending' as const],
    ['omits canCreate', silent()],
  ])(
    'a matter whose every list %s: no create action, no read-only line, no "Your access"',
    async (_label, unanswered) => {
      installNet('matter', { ...all(unanswered), me: meOf({ writeAccessPerRecord: true }) });
      const dialog = await openRecord('matter');
      // Long enough for every list (and /me) to have answered, had they answered.
      await settle(400);

      expect(toolbarOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expectNoDisabledCreateControl(dialog);
      expect(noteOf(dialog, 'documents')).toBeNull();
      expect(noteOf(dialog, 'messages')).toBeNull();
      expect(noteOf(dialog, 'tasksEvents')).toBeNull();
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
    },
    SLOW
  );

  it.each([
    ['fails', fails],
    ['is still loading', 'pending' as const],
    ['omits canCreate', silent()],
  ])(
    'one list that %s hides only its own action; the others still follow their answers',
    async (_label, unanswered) => {
      installNet('wa', {
        documents: may(),
        messages: may(),
        todos: unanswered,
        events: may(),
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord('wa');

      await waitFor(() =>
        expect(toolbarOf(dialog)).toEqual(['refresh', 'upload', 'newMessage', 'addEvent', 'copyLink'])
      );
      await settle(300);
      expect(toolbarOf(dialog)).not.toContain('addTodo');
      // Not known is not "read-only": the section says nothing about to-dos.
      expect(noteOf(dialog, 'tasksEvents')).toBeNull();
      expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.canContribute);
    },
    SLOW
  );

  it(
    '"Read only" needs every list to have said no: one unknown list leaves the header empty, and only the answered section explains',
    async () => {
      installNet('matter', {
        documents: mayNot(),
        messages: mayNot(),
        todos: mayNot(),
        events: 'pending',
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord('matter');
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these tasks, but not add one.'));
      await settle(300);

      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents, but not upload to this record.');
      expect(toolbarOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
    },
    SLOW
  );

  it(
    'with /me unknown (its read failed) the template says nothing about access, and offers only what the lists answered',
    async () => {
      installNet('matter', { ...all(may()), me: 'fail' });
      const dialog = await openRecord('matter');

      await waitFor(() => expect(toolbarOf(dialog)).toEqual(ALL_CREATE));
      await settle(200);
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Project: the /me level, which must agree with the list
// ---------------------------------------------------------------------------

describe('project: "Your access" is the /me level; a create needs the level and the list', () => {
  const project = RECORDS.project;

  it(
    'Collaborate, lists may: every create offered; the header shows "Collaborate"',
    async () => {
      installNet('project', {
        ...all(may()),
        me: meOf({ projects: [{ projectId: project.id, accessLevel: 'Collaborate' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(toolbarOf(dialog)).toEqual(ALL_CREATE));
      expect(yourAccess(dialog)).toBe('Collaborate');
    },
    SLOW
  );

  it(
    'View Only, lists may: the level refuses Upload, Add to-do and Add event; the header shows "View Only" and those sections say why',
    async () => {
      installNet('project', {
        ...all(may()),
        messages: mayNot(),
        me: meOf({ projects: [{ projectId: project.id, accessLevel: 'ViewOnly' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(yourAccess(dialog)).toBe('View Only'));
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).not.toBeNull());
      await waitFor(() => expect(noteOf(dialog, 'messages')).not.toBeNull());
      expect(toolbarOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expectNoDisabledCreateControl(dialog);
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents. Uploading needs Collaborate access.');
      expect(noteOf(dialog, 'tasksEvents')).toBe(
        'You can view these tasks and events. Adding them needs Collaborate access.'
      );
    },
    SLOW
  );

  it(
    'New message follows the thread on every root: a View Only project caller whose thread says "may post" is offered it',
    async () => {
      // The thread's `canCreate` is the message POST's own decision (task 002, CanPostAsync, every root); /me carries no
      // level for posting, so the project level does not gate it. The other three actions are still refused by the level.
      installNet('project', {
        ...all(may()),
        me: meOf({ projects: [{ projectId: project.id, accessLevel: 'ViewOnly' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(toolbarOf(dialog)).toEqual(['refresh', 'newMessage', 'copyLink']));
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).not.toBeNull());
      expect(noteOf(dialog, 'messages')).toBeNull();
      expect(yourAccess(dialog)).toBe('View Only');
    },
    SLOW
  );

  it(
    'Collaborate, but the list says no (the role ceiling): both must allow, so that action is hidden',
    async () => {
      installNet('project', {
        documents: may(),
        messages: may(),
        todos: may(),
        events: mayNot(),
        me: meOf({ projects: [{ projectId: project.id, accessLevel: 'Collaborate' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() =>
        expect(toolbarOf(dialog)).toEqual(['refresh', 'upload', 'newMessage', 'addTodo', 'copyLink'])
      );
      // The events list has answered once its line is there (no fixed wait). The caller holds Collaborate, so the line
      // must not say "needs Collaborate access": the list's "no" is not the level's (fix round 1 B).
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      expect(toolbarOf(dialog)).not.toContain('addEvent');
      expect(yourAccess(dialog)).toBe('Collaborate');
    },
    SLOW
  );

  it.each([
    ['has no entry for the project', []],
    ['has an entry with an unrecognised level', [{ projectId: project.id, accessLevel: 'SomethingNew' }]],
  ])(
    'a level-decided caller whose /me %s: the level-gated actions stay hidden, but nothing is said about them and "Your access" is empty',
    async (_label, projects) => {
      // Fix round 1 C: `decidingLevelOf` falls back to View Only (fail safe), so Upload / Add to-do / Add event are hidden;
      // but that View Only is a guess, not an answer (FR-07: unknown shows nothing). The thread still decides New message.
      installNet('project', {
        ...all(may()),
        todos: may([todo(1, 'Open task')]),
        events: may([event(1, 'Hearing')]),
        me: meOf({ projects }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(toolbarOf(dialog)).toEqual(['refresh', 'newMessage', 'copyLink']));
      // Every list has answered: the documents empty state, the to-do and the event rows.
      await within(dialog).findByText(/No documents have been uploaded/);
      await within(dialog).findByText('Open task');
      await within(dialog).findByText('Hearing');
      expect(toolbarOf(dialog)).toEqual(['refresh', 'newMessage', 'copyLink']);
      for (const name of [/^upload/i, /^add to-do/i, /^add event/i, /^create event/i, /^add task/i]) {
        expect(within(dialog).queryByRole('button', { name, hidden: true })).toBeNull();
      }
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
      expect(noteOf(dialog, 'documents')).toBeNull();
      expect(noteOf(dialog, 'tasksEvents')).toBeNull();
      expect(noteOf(dialog, 'messages')).toBeNull();
    },
    SLOW
  );

  it(
    'a per-record caller on a project: the level is not theirs to show; the lists decide and the header says "Can contribute"',
    async () => {
      installNet('project', {
        ...all(may()),
        me: meOf({ writeAccessPerRecord: true, projects: [{ projectId: project.id, accessLevel: 'ViewOnly' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(toolbarOf(dialog)).toEqual(ALL_CREATE));
      expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.canContribute);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Creates post to the record's own route; a refusal shows the existing error and changes nothing
// ---------------------------------------------------------------------------

async function openFromToolbar(dialog: HTMLElement, action: string, title: string): Promise<HTMLElement> {
  await waitFor(() => expect(within(dialog).getByRole('button', { name: action })).toBeTruthy());
  fireEvent.click(within(dialog).getByRole('button', { name: action }));
  const child = await screen.findByRole('dialog', { name: title });
  await settle(350);
  return child;
}

describe.each(['matter', 'wa'] as const)('%s: creates go to the record route', key => {
  const r = RECORDS[key];

  it(
    'Add to-do posts to /{root}/{id}/todos and the new to-do joins the list',
    async () => {
      installNet(key, {
        ...all(may()),
        todos: may([todo(1, 'Existing task')]),
        me: meOf({ writeAccessPerRecord: true }),
        onCreateTodo: (_path, body) => todo(9, String(body.sprk_name)),
      });
      const dialog = await openRecord(key);
      const child = await openFromToolbar(dialog, 'Add to-do', 'Create Task');
      fireEvent.change(within(child).getByPlaceholderText('Enter task title...'), { target: { value: 'Draft memo' } });
      fireEvent.click(within(child).getByRole('button', { name: 'Create Task' }));

      await waitFor(() => expect(within(dialog).getByText('Draft memo')).toBeTruthy());
      const posts = postsTo(/\/todos$/);
      expect(posts.map(p => p[0])).toEqual([`/api/v1/external/${r.root}/${r.id}/todos`]);
      expect(posts[0][1]).toMatchObject({ sprk_name: 'Draft memo' });
      // The client never sends a level or a parent bind: the route carries the record.
      expect(Object.keys(posts[0][1]).some(k => /bind|access|level/i.test(k))).toBe(false);
    },
    SLOW
  );

  it(
    'Add event posts to /{root}/{id}/events with no project bind (only the fields the server accepts)',
    async () => {
      installNet(key, {
        ...all(may()),
        me: meOf({ writeAccessPerRecord: true }),
        onCreateEvent: (_path, body) => event(9, String(body.sprk_name)),
      });
      const dialog = await openRecord(key);
      const child = await openFromToolbar(dialog, 'Add event', 'Create Event');
      fireEvent.change(within(child).getByPlaceholderText('Enter event title'), { target: { value: 'Hearing' } });
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));

      await waitFor(() => expect(within(dialog).getByText('Hearing')).toBeTruthy());
      const posts = postsTo(/\/events$/);
      expect(posts.map(p => p[0])).toEqual([`/api/v1/external/${r.root}/${r.id}/events`]);
      expect(Object.keys(posts[0][1]).every(k => ['sprk_name', 'sprk_duedate', 'sprk_status'].includes(k))).toBe(true);
    },
    SLOW
  );

  it(
    'a 403 on Add event shows the existing error in the dialog and leaves the list as it was',
    async () => {
      installNet(key, {
        ...all(may()),
        events: may([event(1, 'Existing hearing')]),
        me: meOf({ writeAccessPerRecord: true }),
        onCreateEvent: () => {
          throw new ApiError(403, 'Forbidden');
        },
      });
      const dialog = await openRecord(key);
      await within(dialog).findByText('Existing hearing');
      const child = await openFromToolbar(dialog, 'Add event', 'Create Event');
      fireEvent.change(within(child).getByPlaceholderText('Enter event title'), { target: { value: 'Refused' } });
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));

      expect(await within(child).findByText('Failed to create event: Forbidden')).toBeTruthy();
      expect(postsTo(/\/events$/)).toHaveLength(1);
      expect(within(dialog).queryByText('Refused')).toBeNull();
      expect(within(dialog).getByText('Existing hearing')).toBeTruthy();
    },
    SLOW
  );

  it(
    'a 403 on Add to-do shows the existing error in the dialog and leaves the list as it was',
    async () => {
      installNet(key, {
        ...all(may()),
        todos: may([todo(1, 'Existing task')]),
        me: meOf({ writeAccessPerRecord: true }),
        onCreateTodo: () => {
          throw new ApiError(403, 'Forbidden');
        },
      });
      const dialog = await openRecord(key);
      await within(dialog).findByText('Existing task');
      const child = await openFromToolbar(dialog, 'Add to-do', 'Create Task');
      fireEvent.change(within(child).getByPlaceholderText('Enter task title...'), { target: { value: 'Refused' } });
      fireEvent.click(within(child).getByRole('button', { name: 'Create Task' }));

      expect(await within(child).findByText('Forbidden')).toBeTruthy();
      expect(postsTo(/\/todos$/)).toHaveLength(1);
      expect(within(dialog).queryByText('Refused')).toBeNull();
      expect(within(dialog).getByText('Existing task')).toBeTruthy();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Delete-own and the status toggle are not create rights
// ---------------------------------------------------------------------------

describe('delete-own and the to-do status toggle', () => {
  it(
    'a read-only caller still deletes only their own rows (delete-own is the creator stamp, not canCreate)',
    async () => {
      installNet('matter', {
        ...all(mayNot()),
        todos: mayNot([
          todo(1, 'My task', { _sprk_createdbycontact_value: ME }),
          todo(2, 'Their task', { _sprk_createdbycontact_value: SOMEONE }),
        ]),
        events: mayNot([
          event(1, 'My hearing', { _sprk_createdbycontact_value: ME }),
          event(2, 'Their hearing', { _sprk_createdbycontact_value: SOMEONE }),
        ]),
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord('matter');
      const section = within(dialog).getByTestId('record-detail-events');

      await waitFor(() =>
        expect(within(section).queryAllByRole('button', { name: /^Delete (task |event )/ })).toHaveLength(2)
      );
      expect(within(section).getByRole('button', { name: 'Delete task My task' })).toBeTruthy();
      expect(within(section).getByRole('button', { name: 'Delete event My hearing' })).toBeTruthy();
      expect(within(section).queryByRole('button', { name: /Their/ })).toBeNull();
      expect(toolbarOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
    },
    SLOW
  );

  it(
    'a matter to-do offers no status toggle even to a Create holder (nothing answers whether they may PATCH)',
    async () => {
      installNet('matter', {
        ...all(may()),
        todos: may([todo(1, 'Open task')]),
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord('matter');
      await within(dialog).findByText('Open task');
      await waitFor(() => expect(toolbarOf(dialog)).toContain('addTodo'));

      expect(within(dialog).queryByRole('button', { name: 'Mark task complete' })).toBeNull();
    },
    SLOW
  );

  it(
    'in the record template a project to-do offers no status toggle either, not even to a Collaborate or Full Access caller',
    async () => {
      // Fix round 1 A: PATCH /todos/{id} also checks the workforce role ceiling (`WorkforceRoleCeilingGate.UpdateTodo`)
      // and refuses an ambiguous-root to-do; the /me level does not answer that, and no list or row does. So the template
      // never offers the toggle, on any root.
      for (const accessLevel of ['Collaborate', 'FullAccess', 'ViewOnly']) {
        installNet('project', {
          ...all(may()),
          todos: may([todo(1, 'Open task')]),
          me: meOf({ projects: [{ projectId: RECORDS.project.id, accessLevel }] }),
        });
        const dialog = await openRecord('project');
        await within(dialog).findByText('Open task');
        await waitFor(() => expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.level[levelByLabel[accessLevel]]));
        expect(within(dialog).queryByRole('button', { name: /^Mark task (complete|incomplete)$/ })).toBeNull();
        cleanup();
      }
    },
    SLOW
  );

  it(
    'outside the template (the project page) the toggle is unchanged: offered to a Collaborate caller, not to View Only',
    async () => {
      // The must-fire twin of the test above: the same SmartTodo, without `onCreateActionChange`, still offers it.
      bffApiCall.mockImplementation(async () => ({ value: [todo(1, 'Open task')], canCreate: true }));
      render(
        <FluentProvider theme={webLightTheme}>
          <SmartTodo projectId={RECORDS.project.id} accessLevel={AccessLevel.Collaborate} />
        </FluentProvider>
      );
      expect(await screen.findByRole('button', { name: 'Mark task complete' })).toBeTruthy();
      cleanup();

      render(
        <FluentProvider theme={webLightTheme}>
          <SmartTodo projectId={RECORDS.project.id} accessLevel={AccessLevel.ViewOnly} />
        </FluentProvider>
      );
      await screen.findByText('Open task');
      expect(screen.queryByRole('button', { name: 'Mark task complete' })).toBeNull();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Each gate holds on its own (the list's offer, and the template's check of the reported right)
// ---------------------------------------------------------------------------

describe('each gate holds on its own', () => {
  const MATTER = RECORDS.matter.id;

  /** Renders a list in template mode and returns its latest offer to the toolbar. */
  function offerOf(
    ui: (report: (a: CreateActionOffer | null) => void) => React.ReactElement
  ): () => CreateActionOffer | null {
    let last: CreateActionOffer | null = null;
    render(<FluentProvider theme={webLightTheme}>{ui(a => (last = a))}</FluentProvider>);
    return () => last;
  }

  /** Every read of the list answers one row and this `canCreate` (`undefined` = the BFF said nothing). */
  function listAnswers(row: Record<string, unknown>, canCreate: boolean | undefined): void {
    bffApiCall.mockImplementation(async () =>
      canCreate === undefined ? { value: [row] } : { value: [row], canCreate }
    );
  }

  const lists = [
    [
      'SmartTodo',
      todo(1, 'Row task'),
      'Row task',
      (report: (a: CreateActionOffer | null) => void) => (
        <SmartTodo projectId={MATTER} root="matters" writeAccessPerRecord onCreateActionChange={report} />
      ),
    ],
    [
      'EventsCalendar',
      event(1, 'Row event'),
      'Row event',
      (report: (a: CreateActionOffer | null) => void) => (
        <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord onCreateActionChange={report} />
      ),
    ],
    [
      'RecordMessages',
      {
        sprk_communicationid: guid(951),
        sprk_body: 'Row message body',
        sprk_bodyformat: 100000000,
        createdon: '2026-03-01T10:00:00Z',
      },
      'Row message body',
      (report: (a: CreateActionOffer | null) => void) => (
        <RecordMessages entity="sprk_matter" recordId={MATTER} embedded onCreateActionChange={report} />
      ),
    ],
  ] as const;

  it.each(lists)(
    '%s offers its create action only on canCreate: true (false and silent are not offered)',
    async (_name, row, rowText, ui) => {
      for (const [canCreate, visible, right] of [
        [true, true, true],
        [false, false, false],
        [undefined, false, null],
      ] as const) {
        listAnswers(row, canCreate);
        const offer = offerOf(ui);
        await screen.findByText(rowText);
        await settle(150);
        expect({ canCreate, visible: offer()?.visible, right: offer()?.right }).toEqual({ canCreate, visible, right });
        cleanup();
        bffApiCall.mockReset();
      }
    },
    SLOW
  );

  /** A section that offers Add to-do to the template exactly as given (visible: true), with this right. */
  const offering =
    (right: boolean | null | undefined) =>
    (id: RecordSectionId, ctx: RecordSectionContext): React.ReactNode =>
      id === 'tasksEvents' ? <OfferingProbe ctx={ctx} right={right} /> : null;

  const OfferingProbe: React.FC<{ ctx: RecordSectionContext; right: boolean | null | undefined }> = ({
    ctx,
    right,
  }) => {
    const report = ctx.offerAction('addTodo');
    React.useEffect(() => {
      report?.({ visible: true, onClick: () => undefined, right });
    }, [report, right]);
    return <div data-testid="offering-probe" />;
  };

  it.each([
    [true, true],
    [false, false],
    [null, false],
    [undefined, false],
  ] as const)(
    'the template shows a create action only when its list reported the right as true (right %s -> shown %s)',
    async (right, shown) => {
      installNet('matter', { ...all(silent()), me: meOf() });
      render(
        <FluentProvider theme={webLightTheme}>
          <RecordTemplateModal
            open
            onClose={() => undefined}
            config={RECORD_TEMPLATES.sprk_matter}
            recordIds={[MATTER]}
            index={0}
            onIndexChange={() => undefined}
            renderSection={offering(right)}
          />
        </FluentProvider>
      );
      const dialog = await screen.findByRole('dialog');
      await within(dialog).findByTestId('offering-probe');
      await settle(100);
      expect(toolbarOf(dialog).includes('addTodo')).toBe(shown);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Fix round 1: the invoice (Files keeps its own Upload; the thread's line is neutral)
// ---------------------------------------------------------------------------

describe('invoice: the Files list counts for "Your access" without a toolbar action', () => {
  it(
    'Files may upload, the thread may not: header "Can contribute" (not "Read only"), the thread line is neutral, no toolbar Upload',
    async () => {
      // A partner (no writeAccessPerRecord): /me has no level for an invoice, so the thread's "no" names no grant level.
      installNet('invoice', { ...all(may()), messages: mayNot(), me: meOf() });
      const dialog = await openRecord('invoice');

      await waitFor(() => expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.'));
      await within(dialog).findByRole('button', { name: /^upload document/i });
      await waitFor(() => expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.canContribute));
      expect(noteOf(dialog, 'files')).toBeNull();
      expect(toolbarOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
    },
    SLOW
  );

  it(
    'Files may not upload: the Files card says why, its Upload is not rendered, and the header follows the thread',
    async () => {
      installNet('invoice', { ...all(may()), documents: mayNot(), me: meOf() });
      const dialog = await openRecord('invoice');

      await waitFor(() =>
        expect(noteOf(dialog, 'files')).toBe('You can view these files, but not upload to this record.')
      );
      await waitFor(() => expect(toolbarOf(dialog)).toEqual(['refresh', 'newMessage', 'copyLink']));
      expect(within(dialog).queryByRole('button', { name: /^upload/i, hidden: true })).toBeNull();
      expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.canContribute);
    },
    SLOW
  );

  it(
    'both say no: "Read only", and both lines',
    async () => {
      installNet('invoice', { ...all(mayNot()), me: meOf() });
      const dialog = await openRecord('invoice');

      await waitFor(() => expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.readOnly));
      expect(noteOf(dialog, 'files')).toBe('You can view these files, but not upload to this record.');
      expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.');
      expectNoDisabledCreateControl(dialog);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Fix round 1: a failed re-read of the thread withdraws New message
// ---------------------------------------------------------------------------

describe('a thread re-read that fails forgets the posting right', () => {
  it.each([
    [500, new ApiError(500, JSON.stringify({ status: 500, title: 'broken' }))],
    [403, new ApiError(403, '')],
  ])(
    'thread canCreate: true, then Refresh answers %i: New message is withdrawn, the messages stay, nothing is said about posting',
    async (_status, failure) => {
      const net: Net = { ...all(may()), messages: may([message(1, 'First message')]), me: meOf() };
      installNet('matter', net);
      const dialog = await openRecord('matter');
      await within(dialog).findByText('First message');
      await waitFor(() => expect(toolbarOf(dialog)).toContain('newMessage'));

      net.messages = { throws: failure };
      fireEvent.click(within(dialog).getByRole('button', { name: 'Refresh' }));

      await within(dialog).findByTestId('record-messages-refresh-failed');
      await waitFor(() => expect(toolbarOf(dialog)).not.toContain('newMessage'));
      expect(within(dialog).getByText('First message')).toBeTruthy();
      // Unknown is not "read-only": no line, and the other lists still answer (Upload, Add to-do, Add event).
      expect(noteOf(dialog, 'messages')).toBeNull();
      await waitFor(() => expect(toolbarOf(dialog)).toEqual(['refresh', 'upload', 'addTodo', 'addEvent', 'copyLink']));
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Fix round 1: paging to another record forgets the last record's answers
// ---------------------------------------------------------------------------

describe('paging to another record', () => {
  it(
    'from matter A (every list may) to matter B (lists pending): no create action and no "Your access" until B answers',
    async () => {
      const A = RECORDS.matter.id;
      const B = guid(202);
      const release: Array<() => void> = [];
      bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (path === '/api/v1/external/me') return meOf();
        if (path.endsWith('/api/dataverse/fetch')) {
          const body = String(options?.body);
          const id = body.includes(B) ? B : A;
          return body.includes('sprk_matter')
            ? {
                entities: [{ sprk_matterid: id, sprk_mattername: id === B ? 'Matter B' : 'Matter A' }],
                moreRecords: false,
              }
            : { entities: [], moreRecords: false };
        }
        if (/\/(documents|messages|todos|events)$/.test(path)) {
          if (path.includes(A)) return { value: [], canCreate: true };
          // B's lists answer only when the test releases them.
          return new Promise(resolve => release.push(() => resolve({ value: [], canCreate: true })));
        }
        throw new Error(`unexpected BFF call ${path}`);
      });

      const Pager: React.FC = () => {
        const [index, setIndex] = React.useState(0);
        return (
          <FluentProvider theme={webLightTheme}>
            <RecordTemplateModal
              open
              onClose={() => undefined}
              config={RECORD_TEMPLATES.sprk_matter}
              recordIds={[A, B]}
              index={index}
              onIndexChange={setIndex}
            />
          </FluentProvider>
        );
      };
      render(<Pager />);
      const dialog = await screen.findByRole('dialog');
      await within(dialog).findByTestId('record-detail-fields');
      await waitFor(() => expect(toolbarOf(dialog)).toEqual(ALL_CREATE));
      expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.canContribute);

      fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
      await waitFor(() => expect(release.length).toBe(4));
      await within(dialog).findByTestId('record-detail-fields');
      await settle(200);
      expect(toolbarOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
      expect(noteOf(dialog, 'documents')).toBeNull();
      expect(noteOf(dialog, 'messages')).toBeNull();
      expect(noteOf(dialog, 'tasksEvents')).toBeNull();

      // B answers: its own rights now show.
      await act(async () => release.forEach(r => r()));
      await waitFor(() => expect(toolbarOf(dialog)).toEqual(ALL_CREATE));
      expect(yourAccess(dialog)).toBe(RECORD_ACCESS_TEXT.canContribute);
    },
    SLOW
  );
});
