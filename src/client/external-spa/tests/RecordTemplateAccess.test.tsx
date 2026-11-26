/**
 * R4 task 006 (FR-06, FR-07, NFR-04) — what the record template offers, from what the BFF answered for THIS caller on
 * THIS record: hide, don't disable; no offered action may answer 403 by design; "Your access" in the header; one "why"
 * line per read-only section.
 *
 * What runs for real: `RecordTemplateModal` with every real section (Documents, Messages, Events, Invoices), the
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

vi.mock('../src/services/gridDataverseClient', async () => (await import('./documentsGridHarness')).gridClientModule());

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
import { EventsCalendar } from '../src/components/EventsCalendar';
import { RecordMessages } from '../src/components/detail/RecordMessages';
import { RecordSectionCard } from '../src/components/record/sections/RecordSectionCard';
import { installLayout, uninstallLayout } from './jsdomLayout';
import { EVENT_TYPES, MEETING_TYPE, TASK_TYPE } from './eventFixtures';

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
  | { value: Array<Record<string, unknown>>; canCreate?: boolean; creatableEventTypes?: unknown }
  | { throws: ApiError }
  | 'pending';

interface Net {
  documents: ListAnswer;
  messages: ListAnswer;
  events: ListAnswer;
  /** `/me`: the caller context, or 'fail'. */
  me?: Record<string, unknown> | 'fail';
  /** The record's assignable people (task 014 `…/assignable-people`); default: Sam Other, then the caller ("Me"). */
  people?: ListAnswer;
  onCreateEvent?: (path: string, body: Record<string, unknown>) => unknown;
  onDelete?: (path: string) => unknown;
}

// An events list names the types the caller may create (`creatableEventTypes`, task 014); a documents or messages list
// ignores the member, so every answer carries it. `may` = the right AND two types; `silent` = the BFF said nothing.
const may = (rows: Array<Record<string, unknown>> = []): ListAnswer => ({
  value: rows,
  canCreate: true,
  creatableEventTypes: EVENT_TYPES,
});
const mayNot = (rows: Array<Record<string, unknown>> = []): ListAnswer => ({
  value: rows,
  canCreate: false,
  creatableEventTypes: EVENT_TYPES,
});
const silent = (rows: Array<Record<string, unknown>> = []): ListAnswer => ({ value: rows });
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

/** The people an events list's `assignable-people` answers by default (not "Me" first, so "Me first" is the SPA's doing). */
const DEFAULT_PEOPLE: ListAnswer = {
  value: [
    { contactId: SOMEONE, name: 'Sam Other', isMe: false },
    { contactId: ME, name: 'Pat Partner', isMe: true },
  ],
};

function installNet(key: RecordKey, net: Net): void {
  const r = RECORDS[key];
  // What a successful create added: a re-read of the list returns it (the BFF's list is the truth, not the POST's echo).
  const created: Array<Record<string, unknown>> = [];
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
    // An invoice's Files list (R4 task 021): ebilling's read-only files route, no create right of any kind.
    if (/\/files$/.test(path)) return { value: [] };
    if (/\/messages$/.test(path)) return answer(net.messages);

    if (/\/assignable-people$/.test(path)) return answer(net.people ?? DEFAULT_PEOPLE);

    if (/\/events$/.test(path)) {
      if (method === 'POST') {
        if (!net.onCreateEvent) throw new Error(`unexpected event POST ${path}`);
        const row = (await net.onCreateEvent(path, JSON.parse(String(options?.body)))) as Record<string, unknown>;
        if (row && typeof row === 'object') created.push(row);
        return row;
      }
      const list = answer(net.events);
      return created.length > 0 && list && typeof list === 'object' && 'value' in list
        ? { ...list, value: [...(list as { value: unknown[] }).value, ...created] }
        : list;
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

/**
 * Task 009: each section's header actions, by section id (sections without one are left out). Task 034: the Messages thread's
 * own Refresh is a READ action offered to every caller (FR-06), not one of the record's rights-gated actions this file is about,
 * so it is left out here, for the Messages section only (a stray `refresh` in any other section still fails);
 * `RecordMessages.test.tsx` covers it.
 */
const sectionActionsOf = (dialog: HTMLElement): Record<string, string[]> =>
  Object.fromEntries(
    Array.from(dialog.querySelectorAll('[data-testid^="record-section-"]'))
      .map(s => {
        const testId = s.getAttribute('data-testid') ?? '';
        const ids = actionIdsIn(s);
        return [
          testId.replace('record-section-', ''),
          testId === 'record-section-messages' ? ids.filter(id => id !== 'refresh') : ids,
        ] as const;
      })
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

/** `actionsOf` order: the record header (Refresh, Copy link), then Documents, Messages, Events (task 009). */
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
      // Task 024: the must-not-fire pair of "empty states point at the section action" - with nothing offered, no empty
      // state names an action or points at one.
      for (const [section, heading] of [
        ['documents', 'No Documents'],
        ['messages', 'No messages yet'],
        ['tasksEvents', 'No events yet'],
      ] as const) {
        const region = (
          await within(within(dialog).getByTestId(`record-section-${section}`)).findByText(heading)
        ).closest('[role="status"]');
        const text = region?.textContent ?? '';
        expect({ section, empty: text.length > 0 }).toEqual({ section, empty: true });
        expect({ section, namesAction: /\bupload\b|\babove\b|\bbelow\b|new message|add event/i.test(text) }).toEqual({
          section,
          namesAction: false,
        });
      }
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
    'a matter whose lists may create and are empty: Documents, Messages and Events say "above"; none mentions toolbar or to-do',
    async () => {
      installNet('matter', { ...all(may()), me: meOf({ writeAccessPerRecord: true }) });
      const dialog = await openRecord('matter');
      await waitFor(() => expect(actionsOf(dialog)).toEqual(ALL_CREATE));

      // Task 024: each empty list is the shared EmptyState (heading + hint); its whole text is the status region's.
      const sectionText = async (section: RecordSectionId, heading: string): Promise<string> =>
        (await within(within(dialog).getByTestId(`record-section-${section}`)).findByText(heading)).closest(
          '[role="status"]'
        )?.textContent ?? '';
      const texts = {
        documents: await sectionText('documents', 'No Documents'),
        messages: await sectionText('messages', 'No messages yet'),
        tasksEvents: await sectionText('tasksEvents', 'No events yet'),
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
      await within(dialog).findByText('No Documents');
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

/** The Event Type select of the Create Event dialog. */
const typeSelectOf = (child: HTMLElement): HTMLSelectElement =>
  within(child).getByRole('combobox', { name: /Event Type/ }) as HTMLSelectElement;

/** Picks an option of a native select by its visible text. */
function pickOption(select: HTMLSelectElement, text: string): void {
  const option = Array.from(select.options).find(o => o.text === text);
  if (!option)
    throw new Error(
      `no option "${text}" in [${Array.from(select.options)
        .map(o => o.text)
        .join(', ')}]`
    );
  fireEvent.change(select, { target: { value: option.value } });
}

/** Fills the Create Event dialog's title and Event Type (a Meeting by default: it needs no due date). Does not submit. */
function fillCreate(child: HTMLElement, title: string, typeName: string = MEETING_TYPE.name): void {
  fireEvent.change(within(child).getByPlaceholderText('Enter event title'), { target: { value: title } });
  pickOption(typeSelectOf(child), typeName);
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
      fillCreate(child, 'Hearing');
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));

      await waitFor(() => expect(within(dialog).getByText('Hearing')).toBeTruthy());
      const posts = postsTo(/\/events$/);
      expect(posts.map(p => p[0])).toEqual([`/api/v1/external/${r.root}/${r.id}/events`]);
      expect(Object.keys(posts[0][1]).sort()).toEqual(['eventTypeId', 'sprk_name', 'sprk_priority']);
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
      fillCreate(child, 'Refused');
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
      expect(within(section).queryByRole('button', { name: /^Delete event Their/ })).toBeNull();
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
      canCreate === undefined
        ? { value: [row], creatableEventTypes: EVENT_TYPES }
        : { value: [row], canCreate, creatableEventTypes: EVENT_TYPES }
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
   * An Events section that offers Add event to the template exactly as given (visible: true), with this right, and
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
// The invoice (R4 task 021): Files is read-only and owns no create right; the thread's line is neutral
// ---------------------------------------------------------------------------

describe('invoice: Files is read-only, so only the thread carries a create right', () => {
  it(
    'the thread may not post: no "Your access" (/me has no level for an invoice), the thread line is neutral, no Files line, no toolbar Upload',
    async () => {
      // A partner (no writeAccessPerRecord): /me has no level for an invoice, so the thread's "no" names no grant level.
      installNet('invoice', { ...all(may()), messages: mayNot(), me: meOf() });
      const dialog = await openRecord('invoice');

      await waitFor(() => expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.'));
      await within(dialog).findByTestId('record-detail-files');
      expect(yourAccess(dialog)).toBeNull();
      expect(noteOf(dialog, 'files')).toBeNull();
      expect(actionsOf(dialog)).toEqual(READ_ONLY_TOOLBAR);
    },
    SLOW
  );

  it(
    'a documents list saying "may create" adds no Upload to an invoice, and the header follows the thread alone',
    async () => {
      // The documents list is not read for an invoice at all (task 021): whatever it would say changes nothing.
      installNet('invoice', { ...all(may()), documents: may(), me: meOf() });
      const dialog = await openRecord('invoice');

      await waitFor(() => expect(actionsOf(dialog)).toEqual(['refresh', 'copyLink', 'newMessage']));
      await within(dialog).findByTestId('record-detail-files');
      expect(noteOf(dialog, 'files')).toBeNull();
      expect(within(dialog).queryByRole('button', { name: /^upload/i, hidden: true })).toBeNull();
      expect(yourAccess(dialog)).toBeNull();
      expect(bffApiCall.mock.calls.map(c => String(c[0])).filter(p => /\/invoices\/[^/]+\/documents/.test(p))).toEqual(
        []
      );
    },
    SLOW
  );

  it(
    'the thread says no: its line shows, Files says nothing about uploading, and no create control is rendered or disabled',
    async () => {
      installNet('invoice', { ...all(mayNot()), me: meOf() });
      const dialog = await openRecord('invoice');

      await waitFor(() => expect(noteOf(dialog, 'messages')).toBe('You can read this thread, but not post to it.'));
      await within(dialog).findByTestId('record-detail-files');
      expect(noteOf(dialog, 'files')).toBeNull();
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
          if (path.includes(A)) return { value: [], canCreate: true, creatableEventTypes: EVENT_TYPES };
          // B's lists answer only when the test releases them.
          return new Promise(resolve =>
            release.push(() => resolve({ value: [], canCreate: true, creatableEventTypes: EVENT_TYPES }))
          );
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
// Task 015 (FR-35, FR-37; owner Q-2): every event has a type. ONE Events list whose rows show the type, status, priority,
// assignee and a short description; ONE "Add event" whose dialog takes Title, Event Type (exactly the list's
// `creatableEventTypes`), Due Date, Priority, Assigned To and Description. (Task 012's Event/Task radio and `canCreateTask`
// are gone: the type is a required choice among the BFF's own list.)
// ---------------------------------------------------------------------------

const listOf = (dialog: HTMLElement): HTMLElement => within(dialog).getByTestId('events-list');

/** A row of a given event type (`typeName`: the name the BFF sends for it). */
const typed = (
  n: number,
  name: string,
  typeName: string,
  stamps: Record<string, unknown> = {}
): Record<string, unknown> => event(n, name, { eventTypeId: `type-${typeName}`, eventTypeName: typeName, ...stamps });

/** The title row of the list row named `name` (the row's title, its type badge, status and priority are its children). */
const titleRowOf = (list: HTMLElement, name: string): HTMLElement =>
  within(list).getByText(name).parentElement as HTMLElement;
const typeBadgeOf = (list: HTMLElement, name: string): string | null =>
  within(titleRowOf(list, name)).queryByTestId('event-type-badge')?.textContent ?? null;

/** What the BFF answers a create: the created row (the list is read again, so only the list's own copy is shown). */
const createdRow = (_path: string, body: Record<string, unknown>): unknown =>
  typed(9, String(body.sprk_name), MEETING_TYPE.name, { sprk_priority: body.sprk_priority });

/** Opens Add event, fills the title and type, and creates. Returns the dialog. */
async function createFromAddEvent(dialog: HTMLElement, title: string, typeName?: string): Promise<HTMLElement> {
  const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');
  fillCreate(child, title, typeName);
  fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));
  return child;
}

describe.each(['matter', 'wa', 'project'] as const)('%s: one Events list, one Add event (task 015)', key => {
  const me = (): Record<string, unknown> =>
    key === 'project'
      ? meOf({ projects: [{ projectId: RECORDS.project.id, accessLevel: 'Collaborate' }] })
      : meOf({ writeAccessPerRecord: true });

  it(
    'a list is ONE Events list in the order the BFF sent; each row shows its type, status, priority, assignee and a short description',
    async () => {
      installNet(key, {
        ...all(may()),
        events: may([
          typed(1, 'Hearing', 'Meeting', { sprk_duedate: '2026-03-01', sprk_priority: 100000000 }),
          typed(2, 'Draft memo', 'Task', {
            sprk_duedate: '2026-03-02',
            sprk_priority: 100000002,
            assignedToName: 'Sam Other',
            sprk_description: 'Draft the memo for the partners',
          }),
          event(3, 'Older untyped event', { sprk_duedate: '2026-03-03' }),
        ]),
        me: me(),
      });
      const dialog = await openRecord(key);
      await within(dialog).findByText('Draft memo');

      // One list, not groups; the section is titled "Events" and there is no "Tasks" heading anywhere.
      expect(within(dialog).getAllByTestId('events-list')).toHaveLength(1);
      expect(within(dialog).getByRole('region', { name: 'Events' })).toBeTruthy();
      expect(within(dialog).queryByRole('region', { name: /tasks/i })).toBeNull();
      expect(within(dialog).queryByText('Tasks')).toBeNull();

      const list = listOf(dialog);
      expect(
        within(list)
          .getAllByText(/Hearing|Draft memo|Older untyped event/)
          .map(e => e.textContent)
      ).toEqual(['Hearing', 'Draft memo', 'Older untyped event']);

      // The type badge (every typed event), the status and the priority.
      expect(typeBadgeOf(list, 'Hearing')).toBe('Meeting');
      expect(typeBadgeOf(list, 'Draft memo')).toBe('Task');
      expect(within(titleRowOf(list, 'Draft memo')).getByText('Open')).toBeTruthy();
      expect(within(titleRowOf(list, 'Draft memo')).getByTestId('event-priority').textContent).toBe('High');
      expect(within(titleRowOf(list, 'Hearing')).getByTestId('event-priority').textContent).toBe('Low');
      // Assignee and description are on the typed row; the row that has none shows none (must-not-fire).
      expect(within(list).getByText(/Assigned to Sam Other/)).toBeTruthy();
      expect(within(list).getByText('Draft the memo for the partners')).toBeTruthy();
      expect(within(list).getAllByTestId('event-description')).toHaveLength(1);
      // The untyped older event has no type badge, and no row has the old Task badge.
      expect(typeBadgeOf(list, 'Older untyped event')).toBeNull();
      expect(within(list).queryByTestId('event-task-badge')).toBeNull();
    },
    SLOW
  );

  it(
    'Add event is the ONE create action, in the Events header, with nothing for a task (must fire / must not fire)',
    async () => {
      installNet(key, { ...all(may()), me: me() });
      const dialog = await openRecord(key);

      await waitFor(() => expect(sectionActionsOf(dialog).tasksEvents).toEqual(['addEvent']));
      expect(toolbarOf(dialog)).not.toContain('addEvent');
      await settle(100);
      expect(within(dialog).queryByRole('button', { name: /add task/i, hidden: true })).toBeNull();
      expect(within(dialog).queryByRole('button', { name: /create task/i, hidden: true })).toBeNull();
    },
    SLOW
  );

  it(
    'the dialog has Title, Event Type (no radio, nothing preselected), Due Date, Priority (Normal), Assigned To (Nobody) and Description',
    async () => {
      installNet(key, { ...all(may()), me: me() });
      const dialog = await openRecord(key);
      const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');

      expect(within(child).getByPlaceholderText('Enter event title')).toBeTruthy();
      // Event Type: exactly the list's creatableEventTypes (after the placeholder), nothing chosen.
      const typeSelect = typeSelectOf(child);
      expect(Array.from(typeSelect.options).map(o => o.text)).toEqual(['Select a type', 'Task', 'Meeting']);
      expect(typeSelect.value).toBe('');
      expect(within(child).getByLabelText(/Due Date/)).toBeTruthy();
      expect(
        (within(child).getByRole('combobox', { name: 'Priority' }) as HTMLSelectElement).selectedOptions[0].text
      ).toBe('Normal');
      expect(within(child).getByRole('textbox', { name: 'Description' })).toBeTruthy();
      // Assigned To: "Me" first, then the others; Nobody is the default (the BFF sent Sam Other first).
      const assignee = await within(child).findByRole('combobox', { name: 'Assigned To' });
      expect(Array.from((assignee as HTMLSelectElement).options).map(o => o.text)).toEqual([
        'Nobody',
        'Pat Partner (Me)',
        'Sam Other',
      ]);
      expect((assignee as HTMLSelectElement).value).toBe('');
      // The old Event / Task radio is gone.
      expect(within(child).queryByRole('radio')).toBeNull();
      expect(within(child).queryByText('Type')).toBeNull();
    },
    SLOW
  );

  it(
    'a type the list did not name is not an option (the options are exactly creatableEventTypes), and nothing is posted until a type is chosen',
    async () => {
      installNet(key, {
        ...all(may()),
        events: { value: [], canCreate: true, creatableEventTypes: [MEETING_TYPE] },
        me: me(),
      });
      const dialog = await openRecord(key);
      const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');

      expect(Array.from(typeSelectOf(child).options).map(o => o.text)).toEqual(['Select a type', 'Meeting']);
      // A title but no type: Create stays off, and a click posts nothing.
      fireEvent.change(within(child).getByPlaceholderText('Enter event title'), { target: { value: 'No type yet' } });
      const create = within(child).getByRole('button', { name: 'Create Event' }) as HTMLButtonElement;
      expect(create.disabled).toBe(true);
      fireEvent.click(create);
      await settle(100);
      expect(postsTo(/\/events$/)).toHaveLength(0);
    },
    SLOW
  );

  it(
    'a type that needs a due date asks for one: Create is off with the message until it is given; a type that does not, creates without',
    async () => {
      installNet(key, { ...all(may()), me: me(), onCreateEvent: createdRow });
      const dialog = await openRecord(key);
      const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');

      const create = (): HTMLButtonElement =>
        within(child).getByRole('button', { name: 'Create Event' }) as HTMLButtonElement;
      fillCreate(child, 'Needs a date', TASK_TYPE.name);
      expect(within(child).getByText('A due date is required for Task events.')).toBeTruthy();
      expect(create().disabled).toBe(true);
      fireEvent.click(create());
      await settle(100);
      expect(postsTo(/\/events$/)).toHaveLength(0);

      fireEvent.change(within(child).getByLabelText(/Due Date/), { target: { value: '2026-05-01' } });
      expect(within(child).queryByText(/due date is required/)).toBeNull();
      expect(create().disabled).toBe(false);

      // Switch to a type with no due-date rule and clear the date: the message is gone and the create is allowed.
      fireEvent.change(within(child).getByLabelText(/Due Date/), { target: { value: '' } });
      expect(within(child).getByText('A due date is required for Task events.')).toBeTruthy();
      pickOption(typeSelectOf(child), MEETING_TYPE.name);
      expect(within(child).queryByText(/due date is required/)).toBeNull();
      expect(create().disabled).toBe(false);
    },
    SLOW
  );

  it(
    'a create posts eventTypeId, priority, due date, description and the chosen assignee to the record route - and NO eventType',
    async () => {
      installNet(key, {
        ...all(may()),
        events: may([typed(1, 'Hearing', 'Meeting')]),
        me: me(),
        onCreateEvent: createdRow,
      });
      const dialog = await openRecord(key);
      await within(dialog).findByText('Hearing');
      const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');
      fillCreate(child, 'Draft memo', TASK_TYPE.name);
      fireEvent.change(within(child).getByLabelText(/Due Date/), { target: { value: '2026-05-01' } });
      pickOption(within(child).getByRole('combobox', { name: 'Priority' }) as HTMLSelectElement, 'High');
      pickOption(
        (await within(child).findByRole('combobox', { name: 'Assigned To' })) as HTMLSelectElement,
        'Pat Partner (Me)'
      );
      fireEvent.change(within(child).getByRole('textbox', { name: 'Description' }), {
        target: { value: ' For the partners ' },
      });
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));

      await waitFor(() => expect(within(listOf(dialog)).getByText('Draft memo')).toBeTruthy());
      const posts = postsTo(/\/events$/);
      expect(posts.map(p => p[0])).toEqual([`/api/v1/external/${RECORDS[key].root}/${RECORDS[key].id}/events`]);
      const body = posts[0][1];
      expect(body.sprk_name).toBe('Draft memo');
      expect(body.eventTypeId).toBe(TASK_TYPE.id);
      expect(body.sprk_priority).toBe(100000002);
      expect(body.sprk_duedate).toBe('2026-05-01');
      expect(body.sprk_description).toBe('For the partners');
      expect(body.assignedToContactId).toBe(ME);
      expect('eventType' in body).toBe(false);
      // The list was read again after the create (the POST's own row has no names or rights).
      expect(
        bffApiCall.mock.calls.filter(c => /\/events$/.test(String(c[0])) && !(c[1] as RequestInit | undefined)?.method)
      ).toHaveLength(2);
    },
    SLOW
  );

  it(
    'Assigned To nobody (the default) sends no assignee, no description and no due date when none were given',
    async () => {
      installNet(key, { ...all(may()), me: me(), onCreateEvent: createdRow });
      const dialog = await openRecord(key);
      await createFromAddEvent(dialog, 'Plain', MEETING_TYPE.name);

      await waitFor(() => expect(postsTo(/\/events$/)).toHaveLength(1));
      expect(Object.keys(postsTo(/\/events$/)[0][1]).sort()).toEqual(
        [
          ...(key === 'project' ? ['sprk_RegardingProject@odata.bind'] : []),
          'eventTypeId',
          'sprk_name',
          'sprk_priority',
        ].sort()
      );
    },
    SLOW
  );

  it(
    'a failed assignable-people read hides Assigned To with one short line and never blocks the create',
    async () => {
      installNet(key, { ...all(may()), me: me(), people: fails, onCreateEvent: createdRow });
      const dialog = await openRecord(key);
      const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');

      expect(await within(child).findByTestId('event-assignee-unavailable')).toBeTruthy();
      expect(within(child).getAllByTestId('event-assignee-unavailable')).toHaveLength(1);
      expect(within(child).queryByRole('combobox', { name: 'Assigned To' })).toBeNull();
      fillCreate(child, 'Still created');
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));
      await waitFor(() => expect(postsTo(/\/events$/)).toHaveLength(1));
      expect('assignedToContactId' in postsTo(/\/events$/)[0][1]).toBe(false);
    },
    SLOW
  );

  it(
    'a list that offers nobody to assign to shows no Assigned To field and no failure line (must-not-fire of the line above)',
    async () => {
      installNet(key, { ...all(may()), me: me(), people: { value: [] } });
      const dialog = await openRecord(key);
      const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');
      await settle(100);

      expect(within(child).queryByRole('combobox', { name: 'Assigned To' })).toBeNull();
      expect(within(child).queryByTestId('event-assignee-unavailable')).toBeNull();
    },
    SLOW
  );

  it.each([
    ['creatableEventTypes is empty', { value: [], canCreate: true, creatableEventTypes: [] } as ListAnswer, true],
    ['creatableEventTypes is omitted (an older BFF)', { value: [], canCreate: true } as ListAnswer, false],
  ])(
    'with canCreate: true but %s there is no Add event (must-not-fire); an empty list says why, an omitted one says nothing',
    async (_name, events, says) => {
      installNet(key, { ...all(may()), events, me: me() });
      const dialog = await openRecord(key);
      await waitFor(() => expect(actionsOf(dialog)).toContain('upload'));
      await within(listOf(dialog)).findByText('No events yet');
      await settle(100);

      expect(actionsOf(dialog)).not.toContain('addEvent');
      expect(within(dialog).queryByRole('button', { name: /^add event/i, hidden: true })).toBeNull();
      expect(within(dialog).queryByRole('button', { name: /^create event/i, hidden: true })).toBeNull();
      if (says)
        await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      else expect(noteOf(dialog, 'tasksEvents')).toBeNull();
    },
    SLOW
  );

  it.each([
    ['canCreate: false (types never turn a no into a yes)', mayNot()],
    ['canCreate: false and no types', { value: [], canCreate: false } as ListAnswer],
  ])(
    'with %s there is no Add event at all, and the section says why (must-not-fire)',
    async (_name, events) => {
      installNet(key, { ...all(may()), events, me: me() });
      const dialog = await openRecord(key);
      await waitFor(() => expect(actionsOf(dialog)).toContain('upload'));
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));

      expect(actionsOf(dialog)).not.toContain('addEvent');
      expect(within(dialog).queryByRole('button', { name: /^add event/i, hidden: true })).toBeNull();
      expect(within(dialog).queryByRole('button', { name: /^create event/i, hidden: true })).toBeNull();
    },
    SLOW
  );

  it.each([
    ['the list failed', fails],
    ['the list never answers', 'pending' as const],
    // A level-decided project caller's level decides where the list says nothing (createRightOf), but the types are still needed.
    ['the list says neither canCreate nor creatableEventTypes', { value: [] } as ListAnswer],
  ])(
    'Add event is hidden, not disabled, when %s (fail closed)',
    async (_name, events) => {
      installNet(key, { ...all(may()), events, me: me() });
      const dialog = await openRecord(key);
      await waitFor(() => expect(actionsOf(dialog)).toContain('upload'));
      await settle(300);

      expect(actionsOf(dialog)).not.toContain('addEvent');
      expect(within(dialog).queryByRole('button', { name: /^add event/i, hidden: true })).toBeNull();
    },
    SLOW
  );

  it(
    'a 403 on a create shows the error in the dialog, keeps the draft and adds no row',
    async () => {
      installNet(key, {
        ...all(may()),
        me: me(),
        onCreateEvent: () => {
          throw new ApiError(403, 'Forbidden');
        },
      });
      const dialog = await openRecord(key);
      const child = await createFromAddEvent(dialog, 'Refused', TASK_TYPE.name);
      // A Task needs a due date, so the create above did not post; give it one and submit again.
      fireEvent.change(within(child).getByLabelText(/Due Date/), { target: { value: '2026-05-01' } });
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));

      expect(await within(child).findByText('Failed to create event: Forbidden')).toBeTruthy();
      expect(within(dialog).queryByText('Refused')).toBeNull();
      expect((within(child).getByPlaceholderText('Enter event title') as HTMLInputElement).value).toBe('Refused');
    },
    SLOW
  );

  it(
    'the empty state is ONE "No events yet", pointing at Add event above - never at a task, the toolbar or a to-do',
    async () => {
      installNet(key, { ...all(may()), me: me() });
      const dialog = await openRecord(key);
      await waitFor(() => expect(sectionActionsOf(dialog).tasksEvents).toEqual(['addEvent']));

      const list = listOf(dialog);
      await within(list).findByText('No events yet');
      expect(within(dialog).getAllByText('No events yet')).toHaveLength(1);
      expect(within(dialog).queryByText(/No tasks/)).toBeNull();
      const text = (await within(list).findByText(/^No events have been added/)).textContent ?? '';
      expect(text).toContain('Use Add event above');
      expect(/task|toolbar|to-?do/i.test(text)).toBe(false);
    },
    SLOW
  );

  it(
    'a read-only caller sees the one list with its type badges and no create control; the Events section says so plainly',
    async () => {
      installNet(key, {
        ...all(mayNot()),
        events: mayNot([typed(1, 'Hearing', 'Meeting'), typed(2, 'Draft memo', 'Task')]),
        me: me(),
      });
      const dialog = await openRecord(key);

      await within(dialog).findByText('Draft memo');
      expect(typeBadgeOf(listOf(dialog), 'Draft memo')).toBe('Task');
      expect(typeBadgeOf(listOf(dialog), 'Hearing')).toBe('Meeting');
      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      expectNoDisabledCreateControl(dialog);
    },
    SLOW
  );

  it(
    'delete-own is "Delete event X" on every row, whatever its type, and only on the caller\'s own',
    async () => {
      installNet(key, {
        ...all(mayNot()),
        events: mayNot([
          typed(1, 'My hearing', 'Meeting', { _sprk_createdbycontact_value: ME }),
          typed(2, 'My memo', 'Task', { _sprk_createdbycontact_value: ME }),
          typed(3, 'Their memo', 'Task', { _sprk_createdbycontact_value: SOMEONE }),
        ]),
        me: me(),
      });
      const dialog = await openRecord(key);

      await within(dialog).findByText('My memo');
      const list = listOf(dialog);
      expect(within(list).getByRole('button', { name: 'Delete event My memo' })).toBeTruthy();
      expect(within(list).getByRole('button', { name: 'Delete event My hearing' })).toBeTruthy();
      expect(within(dialog).queryByRole('button', { name: /^Delete event Their memo/ })).toBeNull();
      expect(within(dialog).queryByRole('button', { name: /Delete task/ })).toBeNull();
    },
    SLOW
  );
});

describe('project: Add event needs the /me level too (task 015)', () => {
  it(
    'a View Only project caller whose list says canCreate: true with types is offered no Add event (the level refuses)',
    async () => {
      installNet('project', {
        ...all(may()),
        events: may(),
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
    'a Collaborate project caller whose list names no creatable type is offered no Add event, and the section says why',
    async () => {
      installNet('project', {
        ...all(may()),
        events: { value: [], canCreate: true, creatableEventTypes: [] },
        me: meOf({ projects: [{ projectId: RECORDS.project.id, accessLevel: 'Collaborate' }] }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(noteOf(dialog, 'tasksEvents')).toBe('You can view these events, but not add one.'));
      expect(sectionActionsOf(dialog).tasksEvents ?? []).toEqual([]);
    },
    SLOW
  );

  it(
    'a per-record project caller (/me level "View Only", no level line) follows the list alone: Add event with the listed types (A2)',
    async () => {
      installNet('project', {
        ...all(may()),
        events: may(),
        me: meOf({
          writeAccessPerRecord: true,
          projects: [{ projectId: RECORDS.project.id, accessLevel: 'ViewOnly' }],
        }),
      });
      const dialog = await openRecord('project');

      await waitFor(() => expect(sectionActionsOf(dialog).tasksEvents).toEqual(['addEvent']));
      expect(yourAccess(dialog)).toBeNull();
      const child = await openFromSection(dialog, 'Add event', 'Create Event', 'tasksEvents');
      expect(Array.from(typeSelectOf(child).options).map(o => o.text)).toEqual(['Select a type', 'Task', 'Meeting']);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// The busy hold: the record stays open while the one create is in flight
// ---------------------------------------------------------------------------

describe('EventsCalendar: the busy hold follows the create in flight (task 012, 015)', () => {
  const MATTER = RECORDS.matter.id;

  /** A list that answers `may()`, and creates that wait until released; a released create joins the list. */
  function installHeldCreates(): { release: () => void } {
    let release: () => void = () => undefined;
    const rows: unknown[] = [];
    bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
      if (/\/assignable-people$/.test(path)) return { value: [] };
      if ((options?.method ?? 'GET') === 'POST') {
        await new Promise<void>(resolve => {
          release = resolve;
        });
        const row = createdRow(path, JSON.parse(String(options?.body)));
        rows.push(row);
        return row;
      }
      return { value: [...rows], canCreate: true, creatableEventTypes: EVENT_TYPES };
    });
    return { release: () => release() };
  }

  it(
    'a create in flight holds the host until it settles; idle again afterwards, and the new row is in the list',
    async () => {
      const held = installHeldCreates();
      const onBusyChange = vi.fn();
      render(
        <FluentProvider theme={webLightTheme}>
          <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord onBusyChange={onBusyChange} />
        </FluentProvider>
      );
      fireEvent.click(await screen.findByRole('button', { name: 'Create Event' }));
      const child = await screen.findByRole('dialog', { name: 'Create Event' });
      await settle(350);
      fillCreate(child, 'Held');
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));

      await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(true));
      // Still posting: the host has not been released.
      await settle(100);
      expect(onBusyChange).toHaveBeenLastCalledWith(true);

      await act(async () => held.release());
      await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(false));
      await waitFor(() => expect(within(screen.getByTestId('events-list')).getByText('Held')).toBeTruthy());
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
      fireEvent.click(await screen.findByRole('button', { name: 'Create Event' }));
      const child = await screen.findByRole('dialog', { name: 'Create Event' });
      await settle(350);
      fillCreate(child, 'Held');
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));
      await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(true));

      view.unmount();
      expect(onBusyChange).toHaveBeenLastCalledWith(false);
    },
    SLOW
  );

  it(
    'inside the record template the modal cannot be closed while the create is in flight, and can be once it settles',
    async () => {
      let release: () => void = () => undefined;
      installNet('matter', {
        ...all(may()),
        events: may(),
        me: meOf({ writeAccessPerRecord: true }),
        onCreateEvent: (path, body) =>
          new Promise<unknown>(resolve => {
            release = () => resolve(createdRow(path, body));
          }),
      });
      const dialog = await openRecord('matter');
      const closeButton = (): HTMLButtonElement | null =>
        within(dialog).getByText('Close', { selector: 'button, button *' }).closest('button');
      expect(closeButton()?.disabled).toBe(false);

      await createFromAddEvent(dialog, 'Held in the record');
      await waitFor(() => expect(closeButton()?.disabled).toBe(true));
      await settle(100);
      expect(closeButton()?.disabled).toBe(true);

      await act(async () => release());
      await waitFor(() => expect(closeButton()?.disabled).toBe(false));
      await waitFor(() => expect(within(listOf(dialog)).getByText('Held in the record')).toBeTruthy());
    },
    SLOW
  );

  it(
    'after a create the list is read again WITHOUT replacing the rows by a spinner; a failed re-read shows the error bar',
    async () => {
      let reads = 0;
      let releaseReread: (v: unknown) => void = () => undefined;
      bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (/\/assignable-people$/.test(path)) return { value: [] };
        if ((options?.method ?? 'GET') === 'POST') return createdRow(path, JSON.parse(String(options?.body)));
        reads += 1;
        if (reads === 1)
          return { value: [typed(1, 'Existing', 'Meeting')], canCreate: true, creatableEventTypes: EVENT_TYPES };
        return new Promise(resolve => {
          releaseReread = resolve;
        });
      });
      render(
        <FluentProvider theme={webLightTheme}>
          <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord />
        </FluentProvider>
      );
      await screen.findByText('Existing');
      fireEvent.click(screen.getByRole('button', { name: 'Create Event' }));
      const child = await screen.findByRole('dialog', { name: 'Create Event' });
      await settle(350);
      fillCreate(child, 'Second');
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));

      await waitFor(() => expect(reads).toBe(2));
      // The re-read is in flight: the first row stays on screen and there is no loading state.
      expect(screen.getByText('Existing')).toBeTruthy();
      expect(screen.queryByText('Loading events...')).toBeNull();

      await act(async () =>
        releaseReread({
          value: [typed(1, 'Existing', 'Meeting'), typed(9, 'Second', 'Meeting')],
          canCreate: true,
          creatableEventTypes: EVENT_TYPES,
        })
      );
      await waitFor(() => expect(within(screen.getByTestId('events-list')).getByText('Second')).toBeTruthy());
      expect(screen.getByText('Existing')).toBeTruthy();
    },
    SLOW
  );

  it(
    'a re-read after a create that FAILS keeps the rows and shows the error with Try again (the create itself succeeded); Try again re-reads',
    async () => {
      let reads = 0;
      bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (/\/assignable-people$/.test(path)) return { value: [] };
        if ((options?.method ?? 'GET') === 'POST') return createdRow(path, JSON.parse(String(options?.body)));
        reads += 1;
        if (reads === 1)
          return { value: [typed(1, 'Existing', 'Meeting')], canCreate: true, creatableEventTypes: EVENT_TYPES };
        if (reads === 2) throw new ApiError(500, JSON.stringify({ status: 500, title: 'broken' }));
        return {
          value: [typed(1, 'Existing', 'Meeting'), typed(9, 'Second', 'Meeting')],
          canCreate: true,
          creatableEventTypes: EVENT_TYPES,
        };
      });
      render(
        <FluentProvider theme={webLightTheme}>
          <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord />
        </FluentProvider>
      );
      await screen.findByText('Existing');
      fireEvent.click(screen.getByRole('button', { name: 'Create Event' }));
      const child = await screen.findByRole('dialog', { name: 'Create Event' });
      await settle(350);
      fillCreate(child, 'Second');
      fireEvent.click(within(child).getByRole('button', { name: 'Create Event' }));

      const bar = await screen.findByTestId('events-refresh-error');
      expect(bar.textContent).toContain('Events could not be loaded just now.');
      // The rows stay (not replaced by the error), and the create was posted exactly once.
      expect(screen.getByText('Existing')).toBeTruthy();
      expect(postsTo(/\/events$/)).toHaveLength(1);
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

      fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));
      await waitFor(() => expect(within(screen.getByTestId('events-list')).getByText('Second')).toBeTruthy());
      expect(screen.queryByTestId('events-refresh-error')).toBeNull();
      expect(postsTo(/\/events$/)).toHaveLength(1);
    },
    SLOW
  );

  it(
    'outside the record template the list keeps ONE create button, "Create Event", only on canCreate AND a creatable type',
    async () => {
      const oneButton = async (answer: Record<string, unknown>, shown: boolean): Promise<void> => {
        bffApiCall.mockImplementation(async () => answer);
        const view = render(
          <FluentProvider theme={webLightTheme}>
            <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord />
          </FluentProvider>
        );
        await screen.findByText('No events yet');
        if (shown) {
          expect(screen.getAllByRole('button', { name: /^Create / })).toHaveLength(1);
          expect(screen.getByRole('button', { name: 'Create Event' })).toBeTruthy();
        } else {
          expect(screen.queryByRole('button', { name: /^Create / })).toBeNull();
        }
        expect(screen.queryByRole('button', { name: /task/i })).toBeNull();
        view.unmount();
      };
      await oneButton({ value: [], canCreate: true, creatableEventTypes: EVENT_TYPES }, true);
      // must-not-fire: the right without a type, the types without the right, and a list that said neither
      await oneButton({ value: [], canCreate: true, creatableEventTypes: [] }, false);
      await oneButton({ value: [], canCreate: true }, false);
      await oneButton({ value: [], canCreate: false, creatableEventTypes: EVENT_TYPES }, false);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// A truncated list never hides the create action (task 011 F1, kept by tasks 012 and 015)
// ---------------------------------------------------------------------------

describe('EventsCalendar outside the template: a truncated list keeps the create button (task 011 F1, 015)', () => {
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
    '0 rows and truncated (the invoice-hiding fault): the ONE Create Event stays when the caller may create, with no empty-state claim',
    async () => {
      renderList({ value: [], truncated: true, canCreate: true, creatableEventTypes: EVENT_TYPES });
      expect(await screen.findByRole('button', { name: 'Create Event' })).toBeTruthy();
      expect(screen.getAllByRole('button', { name: /^Create / })).toHaveLength(1);
      expect(screen.queryByText('No events yet')).toBeNull();
      expect(screen.getByTestId('truncated-list-notice')).toBeTruthy();
    },
    SLOW
  );

  it(
    'rows and truncated: the rows, their type badges and Create Event all show',
    async () => {
      renderList({
        value: [event(1, 'Plain event'), typed(2, 'A task', 'Task')],
        truncated: true,
        canCreate: true,
        creatableEventTypes: EVENT_TYPES,
      });
      expect(await screen.findByRole('button', { name: 'Create Event' })).toBeTruthy();
      const list = screen.getByTestId('events-list');
      expect(typeBadgeOf(list, 'A task')).toBe('Task');
      expect(typeBadgeOf(list, 'Plain event')).toBeNull();
      expect(screen.getByTestId('truncated-list-notice')).toBeTruthy();
    },
    SLOW
  );

  it(
    '0 rows and truncated, a caller with the right but no creatable type: no Create Event and no empty-state claim (must-not-fire)',
    async () => {
      renderList({ value: [], truncated: true, canCreate: true, creatableEventTypes: [] });
      await screen.findByTestId('truncated-list-notice');
      expect(screen.queryByRole('button', { name: /^Create / })).toBeNull();
      expect(screen.queryByText('No events yet')).toBeNull();
    },
    SLOW
  );

  it(
    '0 rows and truncated, a caller who may not create: nothing but the notice (must-not-fire)',
    async () => {
      renderList({ value: [], truncated: true, canCreate: false });
      await screen.findByTestId('truncated-list-notice');
      expect(screen.queryByRole('button', { name: /^Create / })).toBeNull();
      expect(screen.queryByTestId('events-list')).toBeNull();
    },
    SLOW
  );

  it(
    '0 rows, not truncated: "No events yet" and the Create Event button',
    async () => {
      renderList({ value: [], canCreate: true, creatableEventTypes: EVENT_TYPES });
      expect(await screen.findByText('No events yet')).toBeTruthy();
      expect(screen.getByRole('button', { name: 'Create Event' })).toBeTruthy();
      expect(screen.queryByText(/add task|create task/i)).toBeNull();
    },
    SLOW
  );
});
