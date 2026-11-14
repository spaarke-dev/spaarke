/**
 * R4 task 006 (FR-06, FR-07, NFR-04) — what the record template offers, from what the BFF answered for THIS caller on
 * THIS record: hide, don't disable; no offered action may answer 403 by design; "Your access" in the header; one "why"
 * line per read-only section.
 *
 * What runs for real: `RecordTemplateModal` with every real section (Documents, Messages, Tasks & events, Invoices), the
 * record header (`RecordCommandBar`), the section headers (`HeaderToolbar`, where each create action sits since task 009),
 * `DocumentLibrary`, `RecordMessages`, `EventsCalendar`, their create dialogs,
 * `recordAccess.ts`, `web-api-client` (the routes and bodies) and Fluent v9. What is substituted: the network seam
 * (`bffApiCall`, answering the external routes and throwing `ApiError` exactly as bff-client builds it), MSAL's `useMsal`
 * (BffAccessState reads it) and jsdom's missing layout (`installLayout`).
 *
 * THE CONTRACT THESE TESTS PIN (task 002 made each list's `canCreate` the matching POST's own decision for this caller):
 *   list `canCreate: true`  -> the action is offered, and the POST it makes answers 201 (ExternalRootEventCreateContractTests
 *                              "list canCreate = POST" cases);
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

import { ApiError } from '../src/types';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import { RECORD_TEMPLATES } from '../src/components/record/recordTemplates';
import type { RecordSectionContext, RecordSectionId } from '../src/components/record/recordTemplate.types';
import type { CreateActionOffer } from '../src/components/createAction';
import { EventsCalendar, createBusyReporters } from '../src/components/EventsCalendar';
import { RecordMessages } from '../src/components/detail/RecordMessages';
import { RecordSectionCard } from '../src/components/record/sections/RecordSectionCard';
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
type ListAnswer =
  | { value: Array<Record<string, unknown>>; canCreate?: boolean; canCreateTask?: boolean }
  | { throws: ApiError }
  | 'pending';

interface Net {
  documents: ListAnswer;
  messages: ListAnswer;
  events: ListAnswer;
  /** `/me`: the caller context, or 'fail'. */
  me?: Record<string, unknown> | 'fail';
  onCreateEvent?: (path: string, body: Record<string, unknown>) => unknown;
  onDelete?: (path: string) => unknown;
}

const may = (rows: Array<Record<string, unknown>> = []): ListAnswer => ({ value: rows, canCreate: true });
const mayNot = (rows: Array<Record<string, unknown>> = []): ListAnswer => ({ value: rows, canCreate: false });
const silent = (rows: Array<Record<string, unknown>> = []): ListAnswer => ({ value: rows });
/** An event list that also says the caller may create a Task (task 008 canCreateTask). */
const mayTask = (rows: Array<Record<string, unknown>> = []): ListAnswer => ({
  value: rows,
  canCreate: true,
  canCreateTask: true,
});
const fails: ListAnswer = { throws: new ApiError(500, JSON.stringify({ status: 500, title: 'broken' })) };

const all = (a: ListAnswer): Pick<Net, 'documents' | 'messages' | 'events'> => ({
  documents: a,
  messages: a,
  events: a,
});

const meOf = (extra: Record<string, unknown> = {}): Record<string, unknown> => ({
  contactId: ME,
  email: 'me@example.test',
  projects: [],
  ...extra,
});

const message = (n: number, body: string): Record<string, unknown> => ({
  sprk_communicationid: guid(950 + n),
  sprk_body: body,
  sprk_bodyformat: 100000000,
  createdon: '2026-03-01T10:00:00Z',
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

/** The ids of the `HeaderToolbar` buttons rendered inside `el`, in order. */
const actionIdsIn = (el: Element | null): string[] =>
  Array.from(el?.querySelectorAll('[data-testid^="header-toolbar-button-"]') ?? []).map(b =>
    (b.getAttribute('data-testid') ?? '').replace('header-toolbar-button-', '')
  );

/** The ids of the record header's buttons (`RecordCommandBar`), in order. */
const toolbarOf = (dialog: HTMLElement): string[] => actionIdsIn(within(dialog).queryByTestId('record-command-bar'));

/** Task 009: each section's header actions, by section id (sections without one are left out). */
const sectionActionsOf = (dialog: HTMLElement): Record<string, string[]> =>
  Object.fromEntries(
    Array.from(dialog.querySelectorAll('[data-testid^="record-section-"]'))
      .map(s => [(s.getAttribute('data-testid') ?? '').replace('record-section-', ''), actionIdsIn(s)] as const)
      .filter(([, ids]) => ids.length > 0)
  );

/** Every action the record offers: the header's, then each section's (in page order). */
const actionsOf = (dialog: HTMLElement): string[] => [
  ...toolbarOf(dialog),
  ...Object.values(sectionActionsOf(dialog)).flat(),
];

const yourAccess = (dialog: HTMLElement): string | null =>
  within(dialog).queryByTestId('record-template-access')?.querySelector('dd')?.textContent ?? null;

const noteOf = (dialog: HTMLElement, section: 'documents' | 'files' | 'messages' | 'tasksEvents'): string | null =>
  within(dialog).queryByTestId(`record-access-note-${section}`)?.textContent ?? null;

/**
 * No create control is on the record, under any of its names (the section headers', and the lists' own: DocumentLibrary's
 * "Upload Document", EventsCalendar's "Create Event"; and the retired to-do list's "Add to-do" / "Add Task"), and nothing anywhere in
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

/** `actionsOf` order: the record header (Refresh, Copy link), then Documents, Messages, Tasks & events (task 009). */
const ALL_CREATE = ['refresh', 'copyLink', 'upload', 'newMessage', 'addEvent'];
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
    'Create holder (every list canCreate: true), %s: Upload, New message and Add event are offered, each in its own section; /me has no level for the record, so "Your access" shows nothing (never "Can contribute"); no read-only line',
    async (_caller, me) => {
      installNet(key, { ...all(may()), me });
      const dialog = await openRecord(key);

      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));
      // Task 009: a create action is in the header of the section it adds to, never in the record header.
      expect(toolbarOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expect(sectionActionsOf(dialog)).toEqual({
        documents: ['upload'],
        messages: ['newMessage'],
        tasksEvents: ['addEvent'],
      });
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
      expect(noteOf(dialog, 'documents')).toBeNull();
      expect(noteOf(dialog, 'messages')).toBeNull();
      expect(noteOf(dialog, 'tasksEvents')).toBeNull();
      // Submit invoice (task 007) needs the caller's firms linked to the record, which this harness does not give: not shown.
      expect(actionsOf(dialog)).not.toContain('submitInvoice');
    },
    SLOW
  );

  it(
    'View-only (every list canCreate: false): only Refresh and Copy link; nothing disabled; no "Your access" (no /me level, no "Read only" guess); each section says why (no grant level named for a per-record caller)',
    async () => {
      installNet(key, { ...all(mayNot()), me: meOf({ writeAccessPerRecord: true }) });
      const dialog = await openRecord(key);

      // Every list has answered once its line is there, so the hidden assertions below run after all of them.
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      await waitFor(() => expect(noteOf(dialog, 'messages')).not.toBeNull());
      await waitFor(() => expect(noteOf(dialog, 'documents')).not.toBeNull());
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expect(sectionActionsOf(dialog)).toEqual({});
      expectNoDisabledCreateControl(dialog);
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents, but not upload to this record.');
      expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.');
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
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

      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      await waitFor(() => expect(noteOf(dialog, 'messages')).not.toBeNull());
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents, but not upload to this record.');
      expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.');
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
      expect(within(dialog).queryByText(/Collaborate/)).toBeNull();
    },
    SLOW
  );

  it(
    'each list decides its own action: messages yes, documents and events no -> New message only, and the two lines',
    async () => {
      installNet(key, {
        documents: mayNot(),
        messages: may(),
        events: mayNot(),
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord(key);

      await waitFor(() => expect(actionsOf(dialog)).toEqual(['refresh', 'copyLink', 'newMessage']));
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents, but not upload to this record.');
      expect(noteOf(dialog, 'messages')).toBeNull();
      expect(yourAccess(dialog)).toBeNull();
    },
    SLOW
  );

  it(
    'and the mirror: documents and events yes, messages no -> Upload and Add event, and the thread line',
    async () => {
      installNet(key, {
        documents: may(),
        messages: mayNot(),
        events: may(),
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord(key);

      await waitFor(() => expect(actionsOf(dialog)).toEqual(['refresh', 'copyLink', 'upload', 'addEvent']));
      await waitFor(() => expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.'));
      expect(noteOf(dialog, 'tasksEvents')).toBeNull();
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

      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
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
        events: unanswered,
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord('wa');

      await waitFor(() => expect(actionsOf(dialog)).toEqual(['refresh', 'copyLink', 'upload', 'newMessage']));
      await settle(300);
      expect(actionsOf(dialog)).not.toContain('addEvent');
      // Not known is not "read-only": the section says nothing about events.
      expect(noteOf(dialog, 'tasksEvents')).toBeNull();
      expect(yourAccess(dialog)).toBeNull();
    },
    SLOW
  );

  it(
    'one unknown list leaves the header empty (it never carried a "Read only" guess), and only the answered section explains',
    async () => {
      installNet('matter', {
        documents: mayNot(),
        messages: 'pending',
        events: mayNot(),
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord('matter');
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      await settle(300);
      expect(noteOf(dialog, 'messages')).toBeNull();

      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents, but not upload to this record.');
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
    },
    SLOW
  );

  it(
    'with /me unknown (its read failed) the template says nothing about access, and offers only what the lists answered',
    async () => {
      installNet('matter', { ...all(may()), me: 'fail' });
      const dialog = await openRecord('matter');

      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));
      await settle(200);
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Task 009 review: the empty states name the section action, not the toolbar and not a to-do
// ---------------------------------------------------------------------------

describe('empty states point at the section action (task 009)', () => {
  it(
    'a matter whose lists may create and are empty: Documents, Messages and Tasks & events each say "above", never toolbar or to-do',
    async () => {
      installNet('matter', { ...all(may()), me: meOf({ writeAccessPerRecord: true }) });
      const dialog = await openRecord('matter');
      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));

      const sectionText = async (section: RecordSectionId, text: RegExp): Promise<string> =>
        (await within(within(dialog).getByTestId(`record-section-${section}`)).findByText(text)).textContent ?? '';
      const texts = {
        documents: await sectionText('documents', /^No documents have been uploaded/),
        messages: await sectionText('messages', /^No messages yet/),
        tasksEvents: await sectionText('tasksEvents', /^No events have been added/),
      };
      for (const [section, text] of Object.entries(texts)) {
        expect({ section, says: /\babove\b/.test(text) }).toEqual({ section, says: true });
        expect({ section, misleads: /toolbar|to-?do/i.test(text) }).toEqual({ section, misleads: false });
      }
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

      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));
      expect(yourAccess(dialog)).toBe('Collaborate');
    },
    SLOW
  );

  it(
    'View Only, lists may: the level refuses Upload and Add event; the header shows "View Only" and those sections say why',
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
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expectNoDisabledCreateControl(dialog);
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents. Uploading needs Collaborate access.');
      expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events. Adding one needs Collaborate access.');
    },
    SLOW
  );

  it(
    'New message follows the thread on every root: a View Only project caller whose thread says "may post" is offered it',
    async () => {
      // The thread's `canCreate` is the message POST's own decision (task 002, CanPostAsync, every root); /me carries no
      // level for posting, so the project level does not gate it. The other two actions are still refused by the level.
      installNet('project', {
        ...all(may()),
        me: meOf({ projects: [{ projectId: project.id, accessLevel: 'ViewOnly' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(actionsOf(dialog)).toEqual(['refresh', 'copyLink', 'newMessage']));
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
        events: mayNot(),
        me: meOf({ projects: [{ projectId: project.id, accessLevel: 'Collaborate' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(actionsOf(dialog)).toEqual(['refresh', 'copyLink', 'upload', 'newMessage']));
      // The events list has answered once its line is there (no fixed wait). The caller holds Collaborate, so the line
      // must not say "needs Collaborate access": the list's "no" is not the level's (fix round 1 B).
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      expect(actionsOf(dialog)).not.toContain('addEvent');
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
      // Fix round 1 C: `decidingLevelOf` falls back to View Only (fail safe), so Upload / Add event are hidden;
      // but that View Only is a guess, not an answer (FR-07: unknown shows nothing). The thread still decides New message.
      installNet('project', {
        ...all(may()),
        events: may([event(1, 'Hearing')]),
        me: meOf({ projects }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(actionsOf(dialog)).toEqual(['refresh', 'copyLink', 'newMessage']));
      // Every list has answered: the documents empty state and the event row.
      await within(dialog).findByText(/No documents have been uploaded/);
      await within(dialog).findByText('Hearing');
      expect(actionsOf(dialog)).toEqual(['refresh', 'copyLink', 'newMessage']);
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
    'a per-record caller on a project (uac-r2 A2): no "Your access" line (the composed READ level would contradict the creates), and the level never decides a write action: the lists do, so every create is still offered',
    async () => {
      installNet('project', {
        ...all(may()),
        me: meOf({ writeAccessPerRecord: true, projects: [{ projectId: project.id, accessLevel: 'ViewOnly' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));
      expect(yourAccess(dialog)).toBeNull();
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Creates post to the record's own route; a refusal shows the existing error and changes nothing
// ---------------------------------------------------------------------------

/** Opens a create dialog from its section's header action (task 009), checking the button is in that section. */
async function openFromSection(
  dialog: HTMLElement,
  action: string,
  title: string,
  section: RecordSectionId
): Promise<HTMLElement> {
  await waitFor(() => expect(within(dialog).getByRole('button', { name: action })).toBeTruthy());
  const button = within(dialog).getByRole('button', { name: action });
  expect(button.closest('[data-testid^="record-section-"]')?.getAttribute('data-testid')).toBe(
    `record-section-${section}`
  );
  expect(within(dialog).getByTestId('record-command-bar').contains(button)).toBe(false);
  fireEvent.click(button);
  const child = await screen.findByRole('dialog', { name: title });
  await settle(350);
  return child;
}

describe.each(['matter', 'wa'] as const)('%s: creates go to the record route', key => {
  const r = RECORDS[key];

  it(
    'Add event posts to /{root}/{id}/events with no project bind (only the fields the server accepts)',
    async () => {
      installNet(key, {
        ...all(may()),
        me: meOf({ writeAccessPerRecord: true }),
        onCreateEvent: (_path, body) => event(9, String(body.sprk_name)),
      });
      const dialog = await openRecord(key);
      const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');
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
      const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');
      fireEvent.change(within(child).getByPlaceholderText('Enter event title'), { target: { value: 'Refused' } });
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));

      expect(await within(child).findByText('Failed to create event: Forbidden')).toBeTruthy();
      expect(postsTo(/\/events$/)).toHaveLength(1);
      expect(within(dialog).queryByText('Refused')).toBeNull();
      expect(within(dialog).getByText('Existing hearing')).toBeTruthy();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Delete-own is not a create right
// ---------------------------------------------------------------------------

describe('delete-own', () => {
  it(
    'a read-only caller still deletes only their own rows (delete-own is the creator stamp, not canCreate)',
    async () => {
      installNet('matter', {
        ...all(mayNot()),
        events: mayNot([
          event(1, 'My hearing', { _sprk_createdbycontact_value: ME }),
          event(2, 'Their hearing', { _sprk_createdbycontact_value: SOMEONE }),
        ]),
        me: meOf({ writeAccessPerRecord: true }),
      });
      const dialog = await openRecord('matter');
      const section = within(dialog).getByTestId('record-detail-events');

      await waitFor(() => expect(within(section).queryAllByRole('button', { name: /^Delete event / })).toHaveLength(1));
      expect(within(section).getByRole('button', { name: 'Delete event My hearing' })).toBeTruthy();
      expect(within(section).queryByRole('button', { name: /Their/ })).toBeNull();
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
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

  /**
   * A Tasks & events section that offers Add event to the template exactly as given (visible: true), with this right, and
   * draws its card with the header actions the template hands back (task 009).
   */
  const offering =
    (right: boolean | null | undefined) =>
    (id: RecordSectionId, ctx: RecordSectionContext): React.ReactNode =>
      id === 'tasksEvents' ? <OfferingProbe ctx={ctx} right={right} /> : null;

  const OfferingProbe: React.FC<{ ctx: RecordSectionContext; right: boolean | null | undefined }> = ({
    ctx,
    right,
  }) => {
    const report = ctx.offerAction('addEvent');
    React.useEffect(() => {
      report?.({ visible: true, onClick: () => undefined, right });
    }, [report, right]);
    return (
      <RecordSectionCard id="tasksEvents" actions={ctx.headerActions}>
        <div data-testid="offering-probe" />
      </RecordSectionCard>
    );
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
      expect(actionsOf(dialog).includes('addEvent')).toBe(shown);
      expect(sectionActionsOf(dialog).tasksEvents ?? []).toEqual(shown ? ['addEvent'] : []);
      expect(toolbarOf(dialog)).not.toContain('addEvent');
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Fix round 1: the invoice (Files keeps its own Upload; the thread's line is neutral)
// ---------------------------------------------------------------------------

describe('invoice: the Files list counts for "Your access" without a toolbar action', () => {
  it(
    'Files may upload, the thread may not: no "Your access" (/me has no level for an invoice), the thread line is neutral, no toolbar Upload',
    async () => {
      // A partner (no writeAccessPerRecord): /me has no level for an invoice, so the thread's "no" names no grant level.
      installNet('invoice', { ...all(may()), messages: mayNot(), me: meOf() });
      const dialog = await openRecord('invoice');

      await waitFor(() => expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.'));
      await within(dialog).findByRole('button', { name: /^upload document/i });
      expect(yourAccess(dialog)).toBeNull();
      expect(noteOf(dialog, 'files')).toBeNull();
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
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
      await waitFor(() => expect(actionsOf(dialog)).toEqual(['refresh', 'copyLink', 'newMessage']));
      expect(within(dialog).queryByRole('button', { name: /^upload/i, hidden: true })).toBeNull();
      expect(yourAccess(dialog)).toBeNull();
    },
    SLOW
  );

  it(
    'both say no: both lines, and still no "Your access" (never "Read only")',
    async () => {
      installNet('invoice', { ...all(mayNot()), me: meOf() });
      const dialog = await openRecord('invoice');

      await waitFor(() =>
        expect(noteOf(dialog, 'files')).toBe('You can view these files, but not upload to this record.')
      );
      await waitFor(() => expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.'));
      expect(yourAccess(dialog)).toBeNull();
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
      await waitFor(() => expect(actionsOf(dialog)).toContain('newMessage'));

      net.messages = { throws: failure };
      fireEvent.click(within(dialog).getByRole('button', { name: 'Refresh' }));

      await within(dialog).findByTestId('record-messages-refresh-failed');
      await waitFor(() => expect(actionsOf(dialog)).not.toContain('newMessage'));
      expect(within(dialog).getByText('First message')).toBeTruthy();
      // Unknown is not "read-only": no line, and the other lists still answer (Upload, Add event).
      expect(noteOf(dialog, 'messages')).toBeNull();
      await waitFor(() => expect(actionsOf(dialog)).toEqual(['refresh', 'copyLink', 'upload', 'addEvent']));
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
        // /me lists a level for matter A only (task 011): B has none, so B must not inherit A's label while it loads.
        if (path === '/api/v1/external/me')
          return meOf({ records: [{ recordType: 'matter', recordId: A, accessLevel: 'Collaborate' }] });
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
        if (/\/(documents|messages|events)$/.test(path)) {
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
      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));
      await waitFor(() => expect(yourAccess(dialog)).toBe('Collaborate'));

      fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
      await waitFor(() => expect(release.length).toBe(3));
      await within(dialog).findByTestId('record-detail-fields');
      await settle(200);
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
      expect(noteOf(dialog, 'documents')).toBeNull();
      expect(noteOf(dialog, 'messages')).toBeNull();
      expect(noteOf(dialog, 'tasksEvents')).toBeNull();

      // B answers: its own rights now show.
      await act(async () => release.forEach(r => r()));
      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));
      // B is not in /me: its answers decide the actions, and "Your access" stays empty (not A's "Collaborate").
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Task 011: "Your access" is the caller's /me level (matters and work assignments: /me `records`, task 008)
// ---------------------------------------------------------------------------

/** `/me` with one `records` entry for the record under test (override the type or id to build a near miss). */
const recordsMe = (
  key: 'matter' | 'wa',
  accessLevel: string,
  extra: Record<string, unknown> = {},
  entry: { recordType?: string; recordId?: string } = {}
): Record<string, unknown> =>
  meOf({
    ...extra,
    records: [
      {
        recordType: entry.recordType ?? (key === 'matter' ? 'matter' : 'workassignment'),
        recordId: entry.recordId ?? RECORDS[key].id,
        accessLevel,
      },
    ],
  });

describe.each(['matter', 'wa'] as const)('%s: "Your access" is the /me records level (task 011)', key => {
  it.each([
    ['ViewOnly', 'View Only'],
    ['Collaborate', 'Collaborate'],
    ['FullAccess', 'Full Access'],
  ])(
    'a partner whose /me lists %s sees "%s" (must fire)',
    async (level, label) => {
      installNet(key, { ...all(may()), me: recordsMe(key, level) });
      const dialog = await openRecord(key);

      await waitFor(() => expect(yourAccess(dialog)).toBe(label));
      expect(within(dialog).queryByText(/Can contribute|Read only/)).toBeNull();
    },
    SLOW
  );

  it.each([
    ['/me lists no records (a BFF that predates them)', meOf()],
    ['/me lists another record only', recordsMe(key, 'Collaborate', {}, { recordId: guid(999) })],
    [
      'the id is listed under the other record type',
      recordsMe(key, 'Collaborate', {}, { recordType: key === 'matter' ? 'workassignment' : 'matter' }),
    ],
    ['the level is one the SPA does not know', recordsMe(key, 'Owner')],
  ])(
    'a record /me does not list shows no value, whatever the lists say: %s (must-not-fire)',
    async (_name, me) => {
      installNet(key, { ...all(may()), me });
      const dialog = await openRecord(key);

      // The lists have answered once their actions are there; the header must still be empty.
      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));
      await settle(200);
      expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
    },
    SLOW
  );

  it(
    'uac-r2 A2, a writeAccessPerRecord caller whose /me level is "View Only": no level line, and the level never hides a write action the lists allow',
    async () => {
      installNet(key, { ...all(may()), me: recordsMe(key, 'ViewOnly', { writeAccessPerRecord: true }) });
      const dialog = await openRecord(key);

      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));
      expect(yourAccess(dialog)).toBeNull();
      expect(within(dialog).queryByText('View Only')).toBeNull();
      expect(noteOf(dialog, 'documents')).toBeNull();
      expect(noteOf(dialog, 'tasksEvents')).toBeNull();
    },
    SLOW
  );

  it(
    'uac-r2 A2, and the mirror: a writeAccessPerRecord caller whose /me level is "Full Access" is offered nothing the lists refuse, sees no level line, and the read-only line is neutral',
    async () => {
      installNet(key, { ...all(mayNot()), me: recordsMe(key, 'FullAccess', { writeAccessPerRecord: true }) });
      const dialog = await openRecord(key);

      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      await waitFor(() => expect(noteOf(dialog, 'documents')).not.toBeNull());
      expect(yourAccess(dialog)).toBeNull();
      expect(within(dialog).queryByText('Full Access')).toBeNull();
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents, but not upload to this record.');
      expectNoDisabledCreateControl(dialog);
    },
    SLOW
  );

  it(
    'a partner labelled "View Only" whose lists say no: the read-only lines name the level that is needed',
    async () => {
      installNet(key, { ...all(mayNot()), me: recordsMe(key, 'ViewOnly') });
      const dialog = await openRecord(key);

      await waitFor(() => expect(yourAccess(dialog)).toBe('View Only'));
      await waitFor(() =>
        expect(noteOf(dialog, 'documents')).toBe('You can view these documents. Uploading needs Collaborate access.')
      );
      expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events. Adding one needs Collaborate access.');
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
    },
    SLOW
  );

  it(
    'a partner labelled "Collaborate" whose lists say no (a role ceiling) is not told to ask for Collaborate',
    async () => {
      installNet(key, { ...all(mayNot()), me: recordsMe(key, 'Collaborate') });
      const dialog = await openRecord(key);

      await waitFor(() => expect(yourAccess(dialog)).toBe('Collaborate'));
      await waitFor(() => expect(noteOf(dialog, 'documents')).not.toBeNull());
      expect(noteOf(dialog, 'documents')).toBe('You can view these documents, but not upload to this record.');
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
    },
    SLOW
  );

  it(
    'the label does not decide on a matter or work assignment: a "View Only" label with lists that say yes still offers them',
    async () => {
      // The lists are the POST's own decision (task 002); /me's matter level is for display only.
      installNet(key, { ...all(may()), me: recordsMe(key, 'ViewOnly') });
      const dialog = await openRecord(key);

      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));
      expect(yourAccess(dialog)).toBe('View Only');
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Task 011: Tasks are events of type Task: "Add task", the Tasks / Events groups, "Add event" without a type
// ---------------------------------------------------------------------------

const groupOf = (dialog: HTMLElement, kind: 'tasks' | 'events'): HTMLElement =>
  within(dialog).getByTestId(`events-group-${kind}`);
const task = (n: number, name: string, stamps: Record<string, unknown> = {}): Record<string, unknown> =>
  event(n, name, { isTask: true, ...stamps });

/** What the BFF answers a create: the created row, `isTask` true exactly for a typed "task" (task 008). */
const createdRow = (_path: string, body: Record<string, unknown>): unknown => ({
  ...event(9, String(body.sprk_name)),
  isTask: body.eventType === 'task',
});

describe.each(['matter', 'wa', 'project'] as const)(
  '%s: Tasks & events lists tasks and events apart (task 011)',
  key => {
    const me = (): Record<string, unknown> =>
      key === 'project'
        ? meOf({ projects: [{ projectId: RECORDS.project.id, accessLevel: 'Collaborate' }] })
        : meOf({ writeAccessPerRecord: true });

    it(
      'rows the BFF flags isTask are the Tasks; every other row is an Event',
      async () => {
        installNet(key, {
          ...all(may()),
          events: mayTask([
            event(1, 'Hearing'),
            task(2, 'Draft memo'),
            event(3, 'Filing deadline'),
            task(4, 'Send notice'),
          ]),
          me: me(),
        });
        const dialog = await openRecord(key);

        await within(dialog).findByText('Draft memo');
        expect(
          within(groupOf(dialog, 'tasks'))
            .getAllByText(/Draft memo|Send notice|Hearing|Filing deadline/)
            .map(e => e.textContent)
        ).toEqual(['Draft memo', 'Send notice']);
        expect(
          within(groupOf(dialog, 'events'))
            .getAllByText(/Draft memo|Send notice|Hearing|Filing deadline/)
            .map(e => e.textContent)
        ).toEqual(['Hearing', 'Filing deadline']);
      },
      SLOW
    );

    it(
      'Add task is offered (in the Tasks & events header, before Add event) only when the list says canCreateTask: true (must fire)',
      async () => {
        installNet(key, { ...all(may()), events: mayTask(), me: me() });
        const dialog = await openRecord(key);

        await waitFor(() => expect(sectionActionsOf(dialog).tasksEvents).toEqual(['addTask', 'addEvent']));
        expect(toolbarOf(dialog)).not.toContain('addTask');
      },
      SLOW
    );

    it.each([
      ['canCreateTask: false', { value: [], canCreate: true, canCreateTask: false } as ListAnswer],
      ['canCreateTask omitted (unknown)', may()],
      [
        'canCreate: false even with canCreateTask: true',
        { value: [], canCreate: false, canCreateTask: true } as ListAnswer,
      ],
      ['the list failed', fails],
      ['the list never answers', 'pending' as const],
    ])(
      'Add task is hidden, not disabled, when the list says %s (must-not-fire)',
      async (_name, events) => {
        installNet(key, { ...all(may()), events, me: me() });
        const dialog = await openRecord(key);
        // Documents and Messages have answered once their actions are there; the event list is then settled or never will be.
        await waitFor(() => expect(actionsOf(dialog)).toContain('upload'));
        await settle(300);

        expect(actionsOf(dialog)).not.toContain('addTask');
        expect(within(dialog).queryByRole('button', { name: /^add task/i, hidden: true })).toBeNull();
        expect(within(dialog).queryByRole('button', { name: /^create task/i, hidden: true })).toBeNull();
      },
      SLOW
    );

    it(
      'Add task posts eventType "task" to the record route, and the new task lands under Tasks (not Events)',
      async () => {
        installNet(key, {
          ...all(may()),
          events: mayTask([event(1, 'Hearing')]),
          me: me(),
          onCreateEvent: createdRow,
        });
        const dialog = await openRecord(key);
        await within(dialog).findByText('Hearing');
        const child = await openFromSection(dialog, 'Add task', 'Create Task', 'tasksEvents');
        fireEvent.change(within(child).getByPlaceholderText('Enter task title'), { target: { value: 'Draft memo' } });
        fireEvent.click(within(child).getByRole('button', { name: 'Create Task' }));

        await waitFor(() => expect(within(groupOf(dialog, 'tasks')).getByText('Draft memo')).toBeTruthy());
        expect(within(groupOf(dialog, 'events')).queryByText('Draft memo')).toBeNull();
        expect(within(groupOf(dialog, 'events')).getByText('Hearing')).toBeTruthy();

        const posts = postsTo(/\/events$/);
        expect(posts.map(p => p[0])).toEqual([`/api/v1/external/${RECORDS[key].root}/${RECORDS[key].id}/events`]);
        expect(posts[0][1].eventType).toBe('task');
        expect(posts[0][1].sprk_name).toBe('Draft memo');
      },
      SLOW
    );

    it(
      'Add event sends NO eventType, and the new event lands under Events (not Tasks)',
      async () => {
        installNet(key, {
          ...all(may()),
          events: mayTask([task(1, 'Existing task')]),
          me: me(),
          onCreateEvent: createdRow,
        });
        const dialog = await openRecord(key);
        await within(dialog).findByText('Existing task');
        const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');
        fireEvent.change(within(child).getByPlaceholderText('Enter event title'), { target: { value: 'Hearing' } });
        fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));

        await waitFor(() => expect(within(groupOf(dialog, 'events')).getByText('Hearing')).toBeTruthy());
        expect(within(groupOf(dialog, 'tasks')).queryByText('Hearing')).toBeNull();
        expect(within(groupOf(dialog, 'tasks')).getByText('Existing task')).toBeTruthy();

        const posts = postsTo(/\/events$/);
        expect(posts).toHaveLength(1);
        expect('eventType' in posts[0][1]).toBe(false);
      },
      SLOW
    );

    it(
      'neither create dialog has the old Type select (it was never sent)',
      async () => {
        installNet(key, { ...all(may()), events: mayTask(), me: me() });
        const dialog = await openRecord(key);

        for (const [action, title] of [
          ['Add task', 'Create Task'],
          ['Add event', 'Create Event'],
        ] as const) {
          const child = await openFromSection(dialog, action, title, 'tasksEvents');
          expect(within(child).queryByRole('combobox')).toBeNull();
          expect(within(child).queryByText('Type')).toBeNull();
          expect(within(child).queryByText(/Meeting|Deadline|Milestone|Reminder|Filing/)).toBeNull();
          fireEvent.click(within(child).getByRole('button', { name: 'Cancel' }));
          await waitFor(() => expect(screen.queryByRole('dialog', { name: title })).toBeNull());
          await settle(100);
        }
      },
      SLOW
    );

    it(
      'a 403 on Add task shows the error in the dialog and adds no task',
      async () => {
        installNet(key, {
          ...all(may()),
          events: mayTask(),
          me: me(),
          onCreateEvent: () => {
            throw new ApiError(403, 'Forbidden');
          },
        });
        const dialog = await openRecord(key);
        const child = await openFromSection(dialog, 'Add task', 'Create Task', 'tasksEvents');
        fireEvent.change(within(child).getByPlaceholderText('Enter task title'), { target: { value: 'Refused' } });
        fireEvent.click(within(child).getByRole('button', { name: 'Create Task' }));

        expect(await within(child).findByText('Failed to create task: Forbidden')).toBeTruthy();
        expect(within(dialog).queryByText('Refused')).toBeNull();
      },
      SLOW
    );

    it(
      'both empty states point at the section action above, never the toolbar or a to-do',
      async () => {
        installNet(key, { ...all(may()), events: mayTask(), me: me() });
        const dialog = await openRecord(key);
        await waitFor(() => expect(sectionActionsOf(dialog).tasksEvents).toEqual(['addTask', 'addEvent']));

        const tasks =
          (await within(groupOf(dialog, 'tasks')).findByText(/^No tasks have been added/)).textContent ?? '';
        const events =
          (await within(groupOf(dialog, 'events')).findByText(/^No events have been added/)).textContent ?? '';
        expect(tasks).toContain('Use Add task above');
        expect(events).toContain('Use Add event above');
        for (const text of [tasks, events]) expect(/toolbar|to-?do/i.test(text)).toBe(false);
      },
      SLOW
    );

    it(
      'a read-only caller sees the groups and no create control; the Tasks group says so plainly',
      async () => {
        installNet(key, {
          ...all(mayNot()),
          events: mayNot([event(1, 'Hearing'), task(2, 'Draft memo')]),
          me: me(),
        });
        const dialog = await openRecord(key);

        await within(dialog).findByText('Draft memo');
        expect(within(groupOf(dialog, 'tasks')).getByText('Draft memo')).toBeTruthy();
        expect(within(groupOf(dialog, 'events')).getByText('Hearing')).toBeTruthy();
        await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
        expectNoDisabledCreateControl(dialog);
      },
      SLOW
    );

    it(
      'delete-own names what the row is: "Delete task X" on a task, "Delete event X" on an event',
      async () => {
        installNet(key, {
          ...all(mayNot()),
          events: mayNot([
            event(1, 'My hearing', { _sprk_createdbycontact_value: ME }),
            task(2, 'My memo', { _sprk_createdbycontact_value: ME }),
            task(3, 'Their memo', { _sprk_createdbycontact_value: SOMEONE }),
          ]),
          me: me(),
        });
        const dialog = await openRecord(key);

        await within(dialog).findByText('My memo');
        expect(within(groupOf(dialog, 'tasks')).getByRole('button', { name: 'Delete task My memo' })).toBeTruthy();
        expect(within(groupOf(dialog, 'events')).getByRole('button', { name: 'Delete event My hearing' })).toBeTruthy();
        expect(within(dialog).queryByRole('button', { name: /Their memo/ })).toBeNull();
      },
      SLOW
    );
  }
);

describe('project: Add task needs the /me level too (task 011)', () => {
  it(
    'a View Only project caller whose list says canCreateTask: true is offered neither Add task nor Add event',
    async () => {
      installNet('project', {
        ...all(may()),
        events: mayTask(),
        me: meOf({ projects: [{ projectId: RECORDS.project.id, accessLevel: 'ViewOnly' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(yourAccess(dialog)).toBe('View Only'));
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).not.toBeNull());
      expect(sectionActionsOf(dialog).tasksEvents ?? []).toEqual([]);
    },
    SLOW
  );

  it(
    'a Collaborate project caller whose list says canCreateTask: false is offered Add event only',
    async () => {
      installNet('project', {
        ...all(may()),
        events: { value: [], canCreate: true, canCreateTask: false },
        me: meOf({ projects: [{ projectId: RECORDS.project.id, accessLevel: 'Collaborate' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(sectionActionsOf(dialog).tasksEvents).toEqual(['addEvent']));
      await settle(200);
      expect(sectionActionsOf(dialog).tasksEvents).toEqual(['addEvent']);
    },
    SLOW
  );

  it(
    'a per-record project caller (/me level "View Only", no level line) follows the list alone: Add task is offered on canCreateTask: true (A2)',
    async () => {
      installNet('project', {
        ...all(may()),
        events: mayTask(),
        me: meOf({
          writeAccessPerRecord: true,
          projects: [{ projectId: RECORDS.project.id, accessLevel: 'ViewOnly' }],
        }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(sectionActionsOf(dialog).tasksEvents).toEqual(['addTask', 'addEvent']));
      expect(yourAccess(dialog)).toBeNull();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Task 011: the hold covers BOTH create dialogs (the per-list busy hold of task 004, restored for two kinds)
// ---------------------------------------------------------------------------

describe('EventsCalendar: the busy hold follows whichever create is in flight (task 011)', () => {
  const MATTER = RECORDS.matter.id;

  /** A list that answers `mayTask`, and creates that wait until released. */
  function installHeldCreates(): { release: () => void } {
    let release: () => void = () => undefined;
    bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
      if ((options?.method ?? 'GET') === 'POST') {
        await new Promise<void>(resolve => {
          release = resolve;
        });
        return createdRow(path, JSON.parse(String(options?.body)));
      }
      return { value: [], canCreate: true, canCreateTask: true };
    });
    return { release: () => release() };
  }

  it.each([
    ['Create Task', 'Enter task title', 'task'],
    ['Create Event', 'Enter event title', 'event'],
  ] as const)(
    '%s in flight holds the host until it settles; idle again afterwards',
    async (button, placeholder, kind) => {
      const held = installHeldCreates();
      const onBusyChange = vi.fn();
      render(
        <FluentProvider theme={webLightTheme}>
          <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord onBusyChange={onBusyChange} />
        </FluentProvider>
      );
      fireEvent.click(await screen.findByRole('button', { name: button }));
      const child = await screen.findByRole('dialog', { name: button });
      await settle(350);
      fireEvent.change(within(child).getByPlaceholderText(placeholder), { target: { value: 'Held' } });
      fireEvent.click(within(child).getByRole('button', { name: button }));

      await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(true));
      // Still posting: the host has not been released.
      await settle(100);
      expect(onBusyChange).toHaveBeenLastCalledWith(true);

      await act(async () => held.release());
      await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(false));
      const group = screen.getByTestId(kind === 'task' ? 'events-group-tasks' : 'events-group-events');
      await waitFor(() => expect(within(group).getByText('Held')).toBeTruthy());
    },
    SLOW
  );

  it(
    'the list unmounting mid-post releases the hold (never left on)',
    async () => {
      installHeldCreates();
      const onBusyChange = vi.fn();
      const view = render(
        <FluentProvider theme={webLightTheme}>
          <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord onBusyChange={onBusyChange} />
        </FluentProvider>
      );
      fireEvent.click(await screen.findByRole('button', { name: 'Create Task' }));
      const child = await screen.findByRole('dialog', { name: 'Create Task' });
      await settle(350);
      fireEvent.change(within(child).getByPlaceholderText('Enter task title'), { target: { value: 'Held' } });
      fireEvent.click(within(child).getByRole('button', { name: 'Create Task' }));
      await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(true));

      view.unmount();
      expect(onBusyChange).toHaveBeenLastCalledWith(false);
    },
    SLOW
  );

  it(
    'outside the record template the list keeps its own buttons: "Create Task" only on canCreateTask: true, "Create Event" on canCreate',
    async () => {
      bffApiCall.mockImplementation(async () => ({ value: [], canCreate: true }));
      const withoutTask = render(
        <FluentProvider theme={webLightTheme}>
          <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord />
        </FluentProvider>
      );
      expect(await screen.findByRole('button', { name: 'Create Event' })).toBeTruthy();
      expect(screen.queryByRole('button', { name: 'Create Task' })).toBeNull();
      withoutTask.unmount();

      bffApiCall.mockImplementation(async () => ({ value: [], canCreate: true, canCreateTask: true }));
      render(
        <FluentProvider theme={webLightTheme}>
          <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord />
        </FluentProvider>
      );
      expect(await screen.findByRole('button', { name: 'Create Task' })).toBeTruthy();
      expect(screen.getByRole('button', { name: 'Create Event' })).toBeTruthy();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Task 011 review round 1 (F1): a truncated list keeps the group's own create button
// ---------------------------------------------------------------------------

describe("EventsCalendar outside the template: a truncated list keeps each group's own create button (review round 1)", () => {
  const MATTER = RECORDS.matter.id;

  const renderList = (answer: Record<string, unknown>): ReturnType<typeof render> => {
    bffApiCall.mockImplementation(async () => answer);
    return render(
      <FluentProvider theme={webLightTheme}>
        <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord />
      </FluentProvider>
    );
  };

  it(
    'empty Tasks group in a truncated list: "Create Task" stays on canCreateTask true, the empty-state text goes',
    async () => {
      renderList({ value: [event(1, 'Plain event')], truncated: true, canCreate: true, canCreateTask: true });
      expect(await screen.findByRole('button', { name: 'Create Task' })).toBeTruthy();
      const tasks = screen.getByTestId('events-group-tasks');
      expect(within(tasks).getByText('Tasks')).toBeTruthy();
      expect(within(tasks).queryByText('No tasks yet')).toBeNull();
      expect(screen.getByTestId('truncated-list-notice')).toBeTruthy();
    },
    SLOW
  );

  it(
    'empty Tasks group in a truncated list: no "Create Task" and no group at all on canCreateTask false (must-not-fire)',
    async () => {
      renderList({ value: [event(1, 'Plain event')], truncated: true, canCreate: true, canCreateTask: false });
      await screen.findByText('Plain event');
      expect(screen.queryByRole('button', { name: 'Create Task' })).toBeNull();
      expect(screen.queryByTestId('events-group-tasks')).toBeNull();
      expect(screen.getByRole('button', { name: 'Create Event' })).toBeTruthy();
    },
    SLOW
  );

  it(
    'empty Events group in a truncated list: "Create Event" stays on canCreate true',
    async () => {
      renderList({ value: [task(1, 'A task')], truncated: true, canCreate: true, canCreateTask: true });
      expect(await screen.findByRole('button', { name: 'Create Event' })).toBeTruthy();
      const events = screen.getByTestId('events-group-events');
      expect(within(events).queryByText('No events yet')).toBeNull();
    },
    SLOW
  );

  it(
    'empty Events group in a truncated list: no "Create Event" and no group on canCreate false (must-not-fire)',
    async () => {
      renderList({ value: [task(1, 'A task')], truncated: true, canCreate: false, canCreateTask: true });
      await screen.findByText('A task');
      expect(screen.queryByRole('button', { name: 'Create Event' })).toBeNull();
      expect(screen.queryByTestId('events-group-events')).toBeNull();
    },
    SLOW
  );

  it(
    '0 rows and truncated (the invoice-hiding fault): both create buttons stay when the caller may create, with no empty-state claim',
    async () => {
      renderList({ value: [], truncated: true, canCreate: true, canCreateTask: true });
      expect(await screen.findByRole('button', { name: 'Create Event' })).toBeTruthy();
      expect(screen.getByRole('button', { name: 'Create Task' })).toBeTruthy();
      expect(screen.queryByText('No events yet')).toBeNull();
      expect(screen.queryByText('No tasks yet')).toBeNull();
      expect(screen.getByTestId('truncated-list-notice')).toBeTruthy();
    },
    SLOW
  );

  it(
    '0 rows and truncated, a caller who may not create: nothing but the notice (must-not-fire)',
    async () => {
      renderList({ value: [], truncated: true, canCreate: false });
      await screen.findByTestId('truncated-list-notice');
      expect(screen.queryByRole('button', { name: /^Create / })).toBeNull();
      expect(screen.queryByTestId('events-group-tasks')).toBeNull();
      expect(screen.queryByTestId('events-group-events')).toBeNull();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Task 011 review round 1 (F2): the combiner behind the two-dialog busy hold
// ---------------------------------------------------------------------------

describe('createBusyReporters: the host is busy while EITHER dialog is posting (review round 1)', () => {
  it('overlap: task true, event true, event false -> still true; task false -> false', () => {
    const report = vi.fn();
    const reporters = createBusyReporters(report);

    reporters.task(true);
    expect(report).toHaveBeenLastCalledWith(true);
    reporters.event(true);
    reporters.event(false);
    expect(report).toHaveBeenLastCalledWith(true);
    reporters.task(false);
    expect(report).toHaveBeenLastCalledWith(false);
  });

  it('the mirror: event true, task true, task false -> still true; event false -> false', () => {
    const report = vi.fn();
    const reporters = createBusyReporters(report);

    reporters.event(true);
    reporters.task(true);
    reporters.task(false);
    expect(report).toHaveBeenLastCalledWith(true);
    reporters.event(false);
    expect(report).toHaveBeenLastCalledWith(false);
  });

  it('a repeated value from one dialog reports nothing; a lone dialog going idle reports false (must-not-fire pair)', () => {
    const report = vi.fn();
    const reporters = createBusyReporters(report);

    reporters.event(false);
    expect(report).not.toHaveBeenCalled();
    reporters.event(true);
    reporters.event(true);
    expect(report).toHaveBeenCalledTimes(1);
    reporters.event(false);
    expect(report).toHaveBeenLastCalledWith(false);
  });
});
