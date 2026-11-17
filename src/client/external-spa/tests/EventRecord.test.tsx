/**
 * R4 task 015 (FR-35, FR-36, FR-37; owner Q-2, Q-5, Q-6, Q-7, S-1, S-3, S-4) — the Event record.
 *
 * Opening: inside a record's Events list the event title is a button that opens the event as a child record over the record,
 * from the ROW's data (no `GET /events/{id}`: a multi-parent event can be listed yet 404 there, 014 review K5); its Refresh
 * re-reads the PARENT LIST and finds the row by id. The deep link `?record=event:{id}` (and any top-level event) reads
 * `GET /events/{id}`. Outside a template, and inside a child record, the title is plain text. Regarding is a button in a
 * top-level event and plain text in a child event (one level only).
 *
 * Actions (hide, don't disable; the BFF's own per-event answers decide): Edit on `canEdit`, Change status on `canChangeStatus`
 * with exactly `allowedStatuses`, Delete event only for the creator. A 204 closes the dialog and re-reads the record; a 403 says
 * the right is gone and re-reads; 404 re-reads and keeps the dialog open with "This event is no longer available."; 503 is retryable; a 400 shows the server's own message.
 *
 * What runs for real: `RecordTemplateModal` (the record and its children), the real Events section and `EventsCalendar`, the
 * event action hosts, `FormModal` / `ConfirmModal`, `web-api-client` and Fluent v9. What is substituted: the network seam
 * (`bffApiCall`, `getExternalUserContext`) and jsdom's missing layout.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return {
    ...actual,
    bffApiCall: h.bffApiCall,
    bffApiBlob: vi.fn(),
    getExternalUserContext: () => h.bffApiCall('/api/v1/external/me'),
  };
});

const { loginRedirect } = vi.hoisted(() => ({ loginRedirect: vi.fn() }));
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ instance: { loginRedirect }, accounts: [], inProgress: 'none' }),
}));

import { ApiError } from '../src/types';
import { EventsCalendar } from '../src/components/EventsCalendar';
import { parseRecordLink } from '../src/components/detail/record-targets';
import { RECORD_TEMPLATES } from '../src/components/record/recordTemplates';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import { installLayout, uninstallLayout } from './jsdomLayout';
import { EVENT_TYPES, MEETING_TYPE, TASK_TYPE } from './eventFixtures';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const ME = guid(7);
const SOMEONE = guid(8);
const MATTER = guid(201);
const EVT = guid(801);
const EVT_2 = guid(802);
const SLOW = { timeout: 60000 };
const WAIT = { timeout: 12000 };

type Row = Record<string, unknown>;

/** An event as `GET /events/{id}` and the list send it: no rights unless a test grants them. */
const eventDto = (over: Row = {}): Row => ({
  sprk_eventid: EVT,
  sprk_name: 'Hearing',
  sprk_duedate: '2026-05-01',
  sprk_status: 659490001, // Open
  sprk_priority: 100000002, // High
  createdon: '2026-04-01T09:00:00Z',
  eventTypeId: MEETING_TYPE.id,
  eventTypeName: 'Meeting',
  assignedToName: 'Sam Other',
  createdByName: 'Pat Partner',
  sprk_description: 'Bring the exhibits',
  ...over,
});

/** The rights of a Collaborate caller on an open event. */
const COLLABORATE: Row = {
  canEdit: true,
  canChangeStatus: true,
  allowedStatuses: [659490002, 659490003, 659490004],
  canReassign: true,
};
const MINE: Row = { _sprk_createdbycontact_value: ME };

const PEOPLE = {
  value: [
    { contactId: SOMEONE, name: 'Sam Other', isMe: false },
    { contactId: ME, name: 'Pat Partner', isMe: true },
  ],
};

/** What the fake BFF holds. */
let current: Row | null; // GET /events/{id}
let rows: Row[]; // the matter's events list
let patches: Row[];
let patchAnswer: ((body: Row) => unknown) | null;
let deleteAnswer: (() => unknown) | null;
let singleReads: number; // GET /events/{id} calls

const eventReads = (): number => singleReads;
const listReads = (): number =>
  h.bffApiCall.mock.calls.filter(
    c => new RegExp(`/matters/${MATTER}/events$`).test(String(c[0])) && !(c[1] as RequestInit | undefined)?.method
  ).length;

function installNet(): void {
  h.bffApiCall.mockReset();
  singleReads = 0;
  patches = [];
  patchAnswer = null;
  deleteAnswer = null;
  h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    const method = options?.method ?? 'GET';
    if (path === '/api/v1/external/me') {
      return {
        contactId: ME,
        email: 'me@example.test',
        projects: [],
        records: [{ recordType: 'matter', recordId: MATTER, accessLevel: 'Collaborate' }],
      };
    }
    if (path.endsWith('/api/dataverse/fetch')) {
      const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
      const id = /operator="eq" value="([^"]+)"/.exec(body.fetchXml)?.[1] ?? '';
      return body.entityName === 'sprk_matter'
        ? {
            entities: [{ sprk_matterid: id, sprk_mattername: 'Matter name', sprk_matternumber: 'MAT-1' }],
            moreRecords: false,
          }
        : { entities: [], moreRecords: false };
    }
    const single = /\/api\/v1\/external\/events\/([^/]+)$/.exec(path);
    if (single) {
      if (method === 'PATCH') {
        const body = JSON.parse(String(options?.body)) as Row;
        patches.push(body);
        if (patchAnswer) return patchAnswer(body);
        if (current) {
          // The server applied it: a re-read sees the new values (a status change also moves the state).
          if ('sprk_name' in body) current = { ...current, sprk_name: body.sprk_name };
          if ('sprk_status' in body) current = { ...current, sprk_status: body.sprk_status };
          if ('sprk_priority' in body) current = { ...current, sprk_priority: body.sprk_priority };
        }
        return undefined;
      }
      if (method === 'DELETE') return deleteAnswer ? deleteAnswer() : undefined;
      singleReads += 1;
      if (!current) throw new ApiError(404, '');
      return { ...current, regarding: { kind: 'matter', id: MATTER, name: 'Matter MAT-1' } };
    }
    if (/\/assignable-people$/.test(path)) return PEOPLE;
    if (new RegExp(`/matters/${MATTER}/events$`).test(path)) {
      return { value: rows, canCreate: true, creatableEventTypes: EVENT_TYPES };
    }
    return { value: [], canCreate: false };
  });
}

const settle = (ms = 60): Promise<void> =>
  act(async () => void (await new Promise(resolve => setTimeout(resolve, ms))));

beforeEach(() => {
  installLayout();
  localStorage.clear();
  current = eventDto();
  rows = [];
  installNet();
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  uninstallLayout();
  Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
});

// ---------------------------------------------------------------------------
// Harness
// ---------------------------------------------------------------------------

const Provider: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

/** A matter record (its Events section is the real one). */
async function openMatter(): Promise<HTMLElement> {
  render(
    <Provider>
      <RecordTemplateModal
        open
        onClose={() => undefined}
        config={RECORD_TEMPLATES.sprk_matter}
        recordIds={[MATTER]}
        index={0}
        onIndexChange={() => undefined}
      />
    </Provider>
  );
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-template-columns', undefined, WAIT);
  // The ~250 ms aria-hidden pass of the parent (tabster) must have run before a child is opened.
  await settle(350);
  return dialog;
}

/** An event shown top-level (the deep link `?record=event:{id}`): read from `GET /events/{id}`. */
async function openEvent(props: { onRecordDeleted?: () => void; id?: string } = {}): Promise<HTMLElement> {
  render(
    <Provider>
      <RecordTemplateModal
        open
        onClose={() => undefined}
        config={RECORD_TEMPLATES.sprk_event}
        recordIds={[props.id ?? EVT]}
        index={0}
        onIndexChange={() => undefined}
        onRecordDeleted={props.onRecordDeleted}
      />
    </Provider>
  );
  const dialog = await screen.findByRole('dialog');
  // The record, or its not-found state (an event the BFF answers 404 for).
  await waitFor(
    () =>
      expect(
        within(dialog).queryByTestId('record-template-columns') ??
          within(dialog).queryByTestId('record-detail-notfound')
      ).not.toBeNull(),
    WAIT
  );
  await settle(350);
  return dialog;
}

/** The titles of every record dialog in the document, outermost first. */
const dialogTitles = (): string[] =>
  Array.from(document.querySelectorAll('[id^="sprk-modal-title-"]')).map(el => el.textContent ?? '');

const fieldOf = (scope: HTMLElement, attribute: string): string =>
  within(scope).getByTestId(`record-detail-field-${attribute}`).textContent ?? '';

const rowTitleOf = (id: string): HTMLElement | null =>
  document.querySelector<HTMLElement>(`[data-child-record="sprk_event:${id}"]`);

/** The record header's button names, in order. */
const toolbarNames = (dialog: HTMLElement): string[] =>
  within(within(dialog).getByTestId('record-command-bar'))
    .getAllByRole('button')
    .map(b => b.getAttribute('aria-label') ?? '');

async function openChildFromRow(parent: HTMLElement, name: string): Promise<HTMLElement> {
  const title = await within(parent).findByRole('button', { name: `Open event ${name}` }, WAIT);
  title.focus();
  fireEvent.click(title);
  const child = await screen.findByRole('dialog', { name: `Event ${name}` }, WAIT);
  await within(child).findByTestId('record-template-columns', undefined, WAIT);
  await settle(350);
  return child;
}

/** Opens a toolbar dialog (Edit / Change status) from the record header and returns it. */
async function openToolbarDialog(record: HTMLElement, button: RegExp, title: string): Promise<HTMLElement> {
  await waitFor(() => expect(within(record).getByRole('button', { name: button })).toBeTruthy(), WAIT);
  fireEvent.click(within(record).getByRole('button', { name: button }));
  const dialog = await screen.findByRole('dialog', { name: title }, WAIT);
  await settle(350);
  return dialog;
}

const saveButton = (dialog: HTMLElement): HTMLButtonElement =>
  within(dialog).getByRole('button', { name: 'Save' }) as HTMLButtonElement;

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

// ---------------------------------------------------------------------------
// Opening a row (FR-36; decision 3)
// ---------------------------------------------------------------------------

describe('an event row opens the event record over the record', () => {
  beforeEach(() => {
    rows = [
      eventDto({ ...COLLABORATE, ...MINE }),
      eventDto({ sprk_eventid: EVT_2, sprk_name: 'Filing', eventTypeName: 'Task', assignedToName: null }),
    ];
  });

  it(
    'the title is a button named "Open event ...", and the child shows the ROW data without reading GET /events/{id}',
    async () => {
      const parent = await openMatter();
      const title = await within(parent).findByRole('button', { name: 'Open event Hearing' }, WAIT);
      expect(title.tagName).toBe('BUTTON');
      expect(title.getAttribute('data-child-record')).toBe(`sprk_event:${EVT}`);

      const child = await openChildFromRow(parent, 'Hearing');
      expect(dialogTitles()).toEqual(['Matter MAT-1', 'Event Hearing']);
      expect(parent.isConnected).toBe(true);
      expect(fieldOf(child, 'sprk_name')).toBe('Hearing');
      expect(fieldOf(child, 'sprk_eventtype')).toBe('Meeting');
      expect(fieldOf(child, 'statuscode')).toBe('Open');
      expect(fieldOf(child, 'sprk_priority')).toBe('High');
      expect(fieldOf(child, 'sprk_assignedto')).toBe('Sam Other');
      expect(fieldOf(child, 'sprk_description')).toBe('Bring the exhibits');
      expect(fieldOf(child, 'sprk_createdby')).toBe('Pat Partner');
      // The seed is the row: the single-event read was never made (a multi-parent event can 404 there, 014 K5).
      expect(eventReads()).toBe(0);
      // One record: no browsing in the child.
      expect(within(child).queryByText('1 of 1')).toBeNull();
    },
    SLOW
  );

  it(
    'Regarding names the matter as plain text in a child event (one level only); no second level is offered',
    async () => {
      const parent = await openMatter();
      const child = await openChildFromRow(parent, 'Hearing');

      expect(fieldOf(child, 'sprk_regarding')).toBe('Matter MAT-1');
      expect(within(child).queryByRole('button', { name: /^Open matter/ })).toBeNull();
      // Nothing in the child opens another record: no row links, no events section.
      expect(within(child).queryByRole('button', { name: /^Open event/ })).toBeNull();
      expect(within(child).queryByTestId('record-section-tasksEvents')).toBeNull();
      expect(dialogTitles()).toEqual(['Matter MAT-1', 'Event Hearing']);
    },
    SLOW
  );

  it(
    'Close returns to the matter with focus on the activating title, and the Events list is read again',
    async () => {
      const parent = await openMatter();
      await within(parent).findByRole('button', { name: 'Open event Hearing' }, WAIT);
      const readsBefore = listReads();
      const child = await openChildFromRow(parent, 'Hearing');

      fireEvent.click(within(child).getAllByRole('button', { name: 'Close' })[0]);
      await waitFor(() => expect(dialogTitles()).toEqual(['Matter MAT-1']), WAIT);
      await waitFor(() => {
        const title = rowTitleOf(EVT);
        expect(title).not.toBeNull();
        expect(document.activeElement).toBe(title);
      }, WAIT);
      await waitFor(() => expect(listReads()).toBeGreaterThan(readsBefore), WAIT);
    },
    SLOW
  );

  it(
    'Refresh in the child re-reads the PARENT list and finds the row by id: a changed row shows its new values',
    async () => {
      const parent = await openMatter();
      const child = await openChildFromRow(parent, 'Hearing');
      expect(fieldOf(child, 'statuscode')).toBe('Open');

      rows = [eventDto({ ...COLLABORATE, ...MINE, sprk_status: 659490002 }), rows[1]]; // Completed
      const before = listReads();
      fireEvent.click(within(child).getByRole('button', { name: 'Refresh' }));
      await waitFor(() => expect(fieldOf(child, 'statuscode')).toBe('Completed'), WAIT);
      expect(listReads()).toBeGreaterThan(before);
      expect(eventReads()).toBe(0);
    },
    SLOW
  );

  it(
    'Refresh when the row is no longer in the list shows the not-found state (must-not-fire of the case above)',
    async () => {
      const parent = await openMatter();
      const child = await openChildFromRow(parent, 'Hearing');

      rows = [rows[1]];
      fireEvent.click(within(child).getByRole('button', { name: 'Refresh' }));
      expect(await within(child).findByTestId('record-detail-notfound', undefined, WAIT)).toBeTruthy();
      expect(eventReads()).toBe(0);
    },
    SLOW
  );

  it(
    'an event opened from a row offers Edit and Change status from the ROW rights, and Edit saves then re-reads the list',
    async () => {
      const parent = await openMatter();
      const child = await openChildFromRow(parent, 'Hearing');
      expect(toolbarNames(child)).toEqual(['Refresh', 'Edit event', 'Change status', 'Copy link', 'Delete event']);

      const dialog = await openToolbarDialog(child, /^Edit/, 'Edit event');
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), {
        target: { value: 'Hearing (moved)' },
      });
      rows = [eventDto({ ...COLLABORATE, ...MINE, sprk_name: 'Hearing (moved)' }), rows[1]];
      const before = listReads();
      fireEvent.click(saveButton(dialog));

      await waitFor(() => expect(patches).toEqual([{ sprk_name: 'Hearing (moved)' }]), WAIT);
      await waitFor(() => expect(fieldOf(child, 'sprk_name')).toBe('Hearing (moved)'), WAIT);
      expect(listReads()).toBeGreaterThan(before);
      expect(eventReads()).toBe(0);
    },
    SLOW
  );

  it(
    'after Edit saves in the child and the child is closed, the parent Events row shows the NEW title and priority',
    async () => {
      const parent = await openMatter();
      const child = await openChildFromRow(parent, 'Hearing');
      expect(within(parent).getAllByTestId('event-priority')[0].textContent).toBe('High');

      const dialog = await openToolbarDialog(child, /^Edit/, 'Edit event');
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), {
        target: { value: 'Hearing (moved)' },
      });
      pickOption(within(dialog).getByRole('combobox', { name: 'Priority' }) as HTMLSelectElement, 'Low');
      // What the server now holds: a re-read of the list returns the edited event.
      rows = [eventDto({ ...COLLABORATE, ...MINE, sprk_name: 'Hearing (moved)', sprk_priority: 100000000 }), rows[1]];
      fireEvent.click(saveButton(dialog));
      await waitFor(() => expect(patches).toEqual([{ sprk_name: 'Hearing (moved)', sprk_priority: 100000000 }]), WAIT);
      await waitFor(() => expect(fieldOf(child, 'sprk_name')).toBe('Hearing (moved)'), WAIT);

      fireEvent.click(within(child).getAllByRole('button', { name: 'Close' })[0]);
      await waitFor(() => expect(dialogTitles()).toEqual(['Matter MAT-1']), WAIT);
      // The parent list was re-read on close: the row carries the new title and priority, not the old ones.
      await within(parent).findByRole('button', { name: 'Open event Hearing (moved)' }, WAIT);
      expect(within(parent).queryByRole('button', { name: 'Open event Hearing' })).toBeNull();
      await waitFor(() => expect(within(parent).getAllByTestId('event-priority')[0].textContent).toBe('Low'), WAIT);
    },
    SLOW
  );

  it(
    'closing the child WITHOUT saving leaves the parent Events row on the old title and priority (must-not-fire pair)',
    async () => {
      const parent = await openMatter();
      const child = await openChildFromRow(parent, 'Hearing');

      const dialog = await openToolbarDialog(child, /^Edit/, 'Edit event');
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), {
        target: { value: 'Hearing (draft)' },
      });
      pickOption(within(dialog).getByRole('combobox', { name: 'Priority' }) as HTMLSelectElement, 'Low');
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Edit event' })).toBeNull(), WAIT);
      expect(patches).toEqual([]);

      fireEvent.click(within(child).getAllByRole('button', { name: 'Close' })[0]);
      await waitFor(() => expect(dialogTitles()).toEqual(['Matter MAT-1']), WAIT);
      await within(parent).findByRole('button', { name: 'Open event Hearing' }, WAIT);
      expect(within(parent).queryByRole('button', { name: 'Open event Hearing (draft)' })).toBeNull();
      expect(within(parent).getAllByTestId('event-priority')[0].textContent).toBe('High');
    },
    SLOW
  );

  it(
    "a row with no rights offers the child only Refresh and Copy link (hide, don't disable)",
    async () => {
      const parent = await openMatter();
      const child = await openChildFromRow(parent, 'Filing');
      expect(toolbarNames(child)).toEqual(['Refresh', 'Copy link']);
      expect(within(child).queryByRole('button', { name: /^Edit/ })).toBeNull();
      expect(within(child).queryByRole('button', { name: /^Delete event/ })).toBeNull();
    },
    SLOW
  );

  it(
    'outside a record template the title is plain text: no button, nothing opens (must-not-fire)',
    async () => {
      rows = [eventDto()];
      render(
        <Provider>
          <EventsCalendar projectId={MATTER} root="matters" writeAccessPerRecord />
        </Provider>
      );
      await screen.findByText('Hearing');
      expect(screen.queryByRole('button', { name: /^Open event/ })).toBeNull();
      expect(document.querySelector('[data-child-record]')).toBeNull();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// A top-level event: the deep link, Regarding, Copy link
// ---------------------------------------------------------------------------

describe('an event shown top-level (?record=event:{id})', () => {
  it(
    'reads GET /events/{id} once and shows its fields; Regarding is a button that opens the matter, and focus comes back',
    async () => {
      const dialog = await openEvent();
      expect(eventReads()).toBe(1);
      expect(fieldOf(dialog, 'sprk_name')).toBe('Hearing');
      expect(fieldOf(dialog, 'sprk_eventtype')).toBe('Meeting');
      expect(fieldOf(dialog, 'statuscode')).toBe('Open');
      expect(fieldOf(dialog, 'sprk_priority')).toBe('High');
      expect(fieldOf(dialog, 'sprk_regarding')).toBe('Matter MAT-1');

      const link = within(dialog).getByRole('button', { name: 'Open matter Matter MAT-1' });
      link.focus();
      fireEvent.click(link);
      const matter = await screen.findByRole('dialog', { name: 'Matter MAT-1' }, WAIT);
      await settle(350);
      expect(dialogTitles()).toEqual(['Event Hearing', 'Matter MAT-1']);
      // The matter is one level over the event: nothing in it opens a third.
      expect(within(matter).queryByRole('button', { name: /^Open event/ })).toBeNull();

      fireEvent.click(within(matter).getAllByRole('button', { name: 'Close' })[0]);
      await waitFor(() => expect(dialogTitles()).toEqual(['Event Hearing']), WAIT);
      await waitFor(() => expect(document.activeElement).toBe(link), WAIT);
    },
    SLOW
  );

  it(
    'an event that is not found (or not readable: one answer) shows "no longer available", with no event actions',
    async () => {
      current = null;
      const dialog = await openEvent();
      expect(await within(dialog).findByTestId('record-detail-notfound')).toBeTruthy();
      expect(within(dialog).queryByRole('button', { name: /^Edit/ })).toBeNull();
      expect(within(dialog).queryByRole('button', { name: /^Change status/ })).toBeNull();
    },
    SLOW
  );

  it(
    'Copy link writes ?record=event:{id}, which the deep link parses back to the event',
    async () => {
      const writeText = vi.fn().mockResolvedValue(undefined);
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
      const dialog = await openEvent();

      fireEvent.click(within(dialog).getByRole('button', { name: 'Copy link' }));
      await within(dialog).findByText('Link copied.');
      const expected = `${window.location.origin}${window.location.pathname}?record=event:${EVT}`;
      expect(writeText).toHaveBeenCalledWith(expected);
      expect(parseRecordLink(new URL(expected).search)).toEqual({ entityName: 'sprk_event', recordId: EVT });
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// The toolbar: what is offered follows the BFF's answers for THIS event
// ---------------------------------------------------------------------------

describe("the event toolbar follows the BFF rights (hide, don't disable)", () => {
  it.each([
    ['no rights (View Only on a parent, or an older BFF)', {}, ['Refresh', 'Copy link']],
    ['canEdit only', { canEdit: true }, ['Refresh', 'Edit event', 'Copy link']],
    [
      'canChangeStatus only',
      { canChangeStatus: true, allowedStatuses: [659490002] },
      ['Refresh', 'Change status', 'Copy link'],
    ],
    [
      'canEdit and canChangeStatus',
      { canEdit: true, canChangeStatus: true, allowedStatuses: [659490002] },
      ['Refresh', 'Edit event', 'Change status', 'Copy link'],
    ],
    // must-not-fire: a flag that is not exactly true, or a change with nothing to change to
    [
      'canEdit: false, canChangeStatus: false',
      { canEdit: false, canChangeStatus: false, allowedStatuses: [659490002] },
      ['Refresh', 'Copy link'],
    ],
    [
      'canChangeStatus with an empty allowedStatuses',
      { canChangeStatus: true, allowedStatuses: [] },
      ['Refresh', 'Copy link'],
    ],
    ['canChangeStatus with no allowedStatuses', { canChangeStatus: true }, ['Refresh', 'Copy link']],
    ['canEdit as a string', { canEdit: 'true' }, ['Refresh', 'Copy link']],
    ['the creator (Delete event), no other right', MINE, ['Refresh', 'Copy link', 'Delete event']],
    ['another person created it', { _sprk_createdbycontact_value: SOMEONE }, ['Refresh', 'Copy link']],
  ] as Array<[string, Row, string[]]>)(
    '%s',
    async (_name, rights, expected) => {
      current = eventDto(rights);
      const dialog = await openEvent();
      expect(toolbarNames(dialog)).toEqual(expected);
      const disabled = Array.from(
        within(within(dialog).getByTestId('record-command-bar')).getAllByRole('button')
      ).filter(b => (b as HTMLButtonElement).disabled);
      expect(disabled).toEqual([]);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Edit
// ---------------------------------------------------------------------------

describe('Edit event', () => {
  beforeEach(() => {
    current = eventDto(COLLABORATE);
  });

  it(
    'opens a FormModal with Cancel on the left, the type shown read-only, and Save off until something changes; only the changed field is sent; a 204 closes it and re-reads the record',
    async () => {
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');

      const buttons = within(dialog).getAllByRole('button');
      const cancel = buttons.find(b => b.textContent === 'Cancel');
      const save = buttons.find(b => b.textContent === 'Save');
      expect(cancel && save && cancel.compareDocumentPosition(save) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      // The type is read-only; nothing changed yet.
      const type = within(dialog).getByRole('textbox', { name: 'Event Type' }) as HTMLInputElement;
      expect(type.value).toBe('Meeting');
      expect(type.readOnly).toBe(true);
      expect(saveButton(dialog).disabled).toBe(true);

      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), {
        target: { value: 'Hearing (moved)' },
      });
      expect(saveButton(dialog).disabled).toBe(false);
      const readsBefore = eventReads();
      fireEvent.click(saveButton(dialog));

      await waitFor(() => expect(patches).toEqual([{ sprk_name: 'Hearing (moved)' }]), WAIT);
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Edit event' })).toBeNull(), WAIT);
      await waitFor(() => expect(fieldOf(record, 'sprk_name')).toBe('Hearing (moved)'), WAIT);
      expect(eventReads()).toBeGreaterThan(readsBefore);
    },
    SLOW
  );

  it(
    'changing priority and description sends exactly those members; clearing the description sends null; a priority back to its own value is no change',
    async () => {
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');

      pickOption(within(dialog).getByRole('combobox', { name: 'Priority' }) as HTMLSelectElement, 'Urgent');
      fireEvent.change(within(dialog).getByRole('textbox', { name: 'Description' }), { target: { value: '' } });
      fireEvent.click(saveButton(dialog));
      await waitFor(() => expect(patches).toEqual([{ sprk_priority: 100000003, sprk_description: null }]), WAIT);
      await waitFor(() => expect(fieldOf(record, 'sprk_priority')).toBe('Urgent'), WAIT);
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Edit event' })).toBeNull(), WAIT);

      // must-not-fire: the event is Urgent now; changing the priority and putting it back leaves nothing to save
      const again = await openToolbarDialog(record, /^Edit/, 'Edit event');
      pickOption(within(again).getByRole('combobox', { name: 'Priority' }) as HTMLSelectElement, 'High');
      expect(saveButton(again).disabled).toBe(false);
      pickOption(within(again).getByRole('combobox', { name: 'Priority' }) as HTMLSelectElement, 'Urgent');
      expect(saveButton(again).disabled).toBe(true);
    },
    SLOW
  );

  it(
    'Assigned To is offered only when the BFF says canReassign: "Me" first, default no change; choosing someone sends assignedToContactId',
    async () => {
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');

      const assignee = (await within(dialog).findByRole('combobox', { name: 'Assigned To' })) as HTMLSelectElement;
      expect(Array.from(assignee.options).map(o => o.text)).toEqual([
        'No change (Sam Other)',
        'Pat Partner (Me)',
        'Sam Other',
      ]);
      expect(assignee.value).toBe('');
      expect(saveButton(dialog).disabled).toBe(true);
      pickOption(assignee, 'Pat Partner (Me)');
      fireEvent.click(saveButton(dialog));
      await waitFor(() => expect(patches).toEqual([{ assignedToContactId: ME }]), WAIT);
    },
    SLOW
  );

  it(
    'without canReassign (a closed event, or a role that cannot read contacts) there is no Assigned To and no people read (must-not-fire)',
    async () => {
      current = eventDto({ canEdit: true, canReassign: false });
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');
      await settle(150);

      expect(within(dialog).queryByRole('combobox', { name: 'Assigned To' })).toBeNull();
      expect(h.bffApiCall.mock.calls.filter(c => /\/assignable-people$/.test(String(c[0])))).toHaveLength(0);
    },
    SLOW
  );

  it(
    'a failed people read hides Assigned To with one line and never blocks the save',
    async () => {
      const base = h.bffApiCall.getMockImplementation()!;
      h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (/\/assignable-people$/.test(path)) throw new ApiError(503, '');
        return base(path, options);
      });
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');

      expect(await within(dialog).findByTestId('event-assignee-unavailable')).toBeTruthy();
      expect(within(dialog).queryByRole('combobox', { name: 'Assigned To' })).toBeNull();
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), { target: { value: 'Still saved' } });
      fireEvent.click(saveButton(dialog));
      await waitFor(() => expect(patches).toEqual([{ sprk_name: 'Still saved' }]), WAIT);
    },
    SLOW
  );

  it(
    'a 403 on the people read (a role that may not read contacts) is "nobody assignable": no Assigned To and NO failure line, and the save still works',
    async () => {
      const base = h.bffApiCall.getMockImplementation()!;
      h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (/\/assignable-people$/.test(path)) throw new ApiError(403, '');
        return base(path, options);
      });
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');
      // The people read was made (and refused), then given time to settle into its state.
      await waitFor(
        () => expect(h.bffApiCall.mock.calls.filter(c => /\/assignable-people$/.test(String(c[0])))).toHaveLength(1),
        WAIT
      );
      await settle(150);

      expect(within(dialog).queryByRole('combobox', { name: 'Assigned To' })).toBeNull();
      expect(within(dialog).queryByTestId('event-assignee-unavailable')).toBeNull();
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), { target: { value: 'Still saved' } });
      fireEvent.click(saveButton(dialog));
      await waitFor(() => expect(patches).toEqual([{ sprk_name: 'Still saved' }]), WAIT);
    },
    SLOW
  );

  it(
    'a 500 on the people read is a failure, not "nobody assignable": no Assigned To, but the line shows (must-not-fire pair of the 403 case)',
    async () => {
      const base = h.bffApiCall.getMockImplementation()!;
      h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (/\/assignable-people$/.test(path)) throw new ApiError(500, '');
        return base(path, options);
      });
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');

      expect(await within(dialog).findByTestId('event-assignee-unavailable', undefined, WAIT)).toBeTruthy();
      expect(within(dialog).queryByRole('combobox', { name: 'Assigned To' })).toBeNull();
    },
    SLOW
  );

  it(
    'Esc does nothing in the dialog (the draft is kept); Cancel closes it without a request',
    async () => {
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), { target: { value: 'Draft title' } });

      fireEvent.keyDown(dialog, { key: 'Escape' });
      await settle(150);
      expect(screen.getByRole('dialog', { name: 'Edit event' })).toBeTruthy();
      expect((within(dialog).getByRole('textbox', { name: /Title/ }) as HTMLInputElement).value).toBe('Draft title');

      fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Edit event' })).toBeNull(), WAIT);
      expect(patches).toEqual([]);
    },
    SLOW
  );

  it(
    'the save in flight holds the record (Close is off) and is released when it settles',
    async () => {
      let release: () => void = () => undefined;
      patchAnswer = () =>
        new Promise<void>(resolve => {
          release = resolve;
        });
      const record = await openEvent();
      const closeButton = (): HTMLButtonElement | null =>
        within(record).getByText('Close', { selector: 'button, button *' }).closest('button');
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), { target: { value: 'Held' } });
      fireEvent.click(saveButton(dialog));

      await waitFor(() => expect(closeButton()?.disabled).toBe(true), WAIT);
      await act(async () => release());
      await waitFor(() => expect(closeButton()?.disabled).toBe(false), WAIT);
    },
    SLOW
  );

  it.each([
    [
      '403: says the right is gone, keeps the dialog, and re-reads the record',
      () =>
        new ApiError(
          403,
          JSON.stringify({ status: 403, title: 'Forbidden', detail: 'internal detail not to be echoed' })
        ),
      'You can no longer edit this event.',
      true,
      true,
    ],
    [
      '503: retryable, nothing re-read',
      () => new ApiError(503, ''),
      'The event could not be saved just now, and nothing was changed. Please try again in a moment.',
      true,
      false,
    ],
    [
      "400: the server's own message",
      () =>
        new ApiError(
          400,
          JSON.stringify({ status: 400, title: 'Bad Request', detail: 'A due date is required for this event type.' })
        ),
      'A due date is required for this event type.',
      true,
      false,
    ],
    [
      'a multi-parent assignee refusal is shown as its message (accepted limit)',
      () =>
        new ApiError(
          400,
          JSON.stringify({
            status: 400,
            title: 'Bad Request',
            detail: 'That person cannot see every record this event is on.',
            errorCode: 'assignee_not_allowed',
          })
        ),
      'That person cannot see every record this event is on.',
      true,
      false,
    ],
  ] as Array<[string, () => ApiError, string, boolean, boolean]>)(
    '%s',
    async (_name, makeError, message, stays, rereads) => {
      patchAnswer = () => {
        throw makeError();
      };
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), { target: { value: 'Refused' } });
      const before = eventReads();
      fireEvent.click(saveButton(dialog));

      const error = await within(dialog).findByTestId('event-edit-error', undefined, WAIT);
      expect(error.textContent).toBe(message);
      expect(error.textContent).not.toContain('internal detail');
      expect(!!screen.queryByRole('dialog', { name: 'Edit event' })).toBe(stays);
      if (rereads) await waitFor(() => expect(eventReads()).toBeGreaterThan(before), WAIT);
      else {
        await settle(150);
        expect(eventReads()).toBe(before);
      }
      // The draft is kept in the dialog.
      expect((within(dialog).getByRole('textbox', { name: /Title/ }) as HTMLInputElement).value).toBe('Refused');
    },
    SLOW
  );

  it(
    '404: the dialog stays open with "This event is no longer available." and the record is re-read',
    async () => {
      patchAnswer = () => {
        current = null; // the event is gone for this caller
        throw new ApiError(404, '');
      };
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), { target: { value: 'Gone' } });
      const before = eventReads();
      fireEvent.click(saveButton(dialog));

      const error = await within(dialog).findByTestId('event-edit-error', undefined, WAIT);
      expect(error.textContent).toBe('This event is no longer available.');
      // The record was re-read (it answered 404) and the dialog is NOT closed under the message.
      await waitFor(() => expect(eventReads()).toBeGreaterThan(before), WAIT);
      await settle(300);
      // Behind the dialog the record now shows its not-found state, and the dialog is still there with the same line.
      expect(await screen.findByTestId('record-detail-notfound', undefined, WAIT)).toBeTruthy();
      expect(screen.getByRole('dialog', { name: 'Edit event' })).toBeTruthy();
      expect(within(dialog).getByTestId('event-edit-error').textContent).toBe('This event is no longer available.');
    },
    SLOW
  );

  it(
    '404 twice: a second Save that also answers 404 keeps the dialog and its line, the record stays not-found, and nothing is re-read again',
    async () => {
      patchAnswer = () => {
        current = null;
        throw new ApiError(404, '');
      };
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), { target: { value: 'Gone' } });
      fireEvent.click(saveButton(dialog));
      await within(dialog).findByTestId('event-edit-error', undefined, WAIT);
      expect(await screen.findByTestId('record-detail-notfound', undefined, WAIT)).toBeTruthy();
      await settle(300);

      const reads = eventReads();
      fireEvent.click(saveButton(dialog));
      await waitFor(() => expect(patches).toHaveLength(2), WAIT);
      await settle(400);

      expect(screen.getByRole('dialog', { name: 'Edit event' })).toBeTruthy();
      expect(within(dialog).getByTestId('event-edit-error').textContent).toBe('This event is no longer available.');
      expect(screen.getByTestId('record-detail-notfound')).toBeTruthy();
      // A re-read of a record that is already not-found learns nothing: none is made.
      expect(eventReads()).toBe(reads);
    },
    SLOW
  );

  it(
    'after a 403 the re-read withdraws the right: Edit is no longer offered, and the refusal line stays in the open dialog',
    async () => {
      patchAnswer = () => {
        current = eventDto({}); // the rights are gone
        throw new ApiError(403, '');
      };
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Edit/, 'Edit event');
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), { target: { value: 'Refused' } });
      fireEvent.click(saveButton(dialog));

      await within(dialog).findByText('You can no longer edit this event.', undefined, WAIT);
      await waitFor(
        () =>
          expect(
            within(within(record).getByTestId('record-command-bar')).queryByRole('button', { name: /^Edit/ })
          ).toBeNull(),
        WAIT
      );
      // The dialog (a snapshot taken at click time) is still there with its line; Cancel closes it.
      expect(screen.getByRole('dialog', { name: 'Edit event' })).toBeTruthy();
      expect(within(dialog).getByText('You can no longer edit this event.')).toBeTruthy();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Change status
// ---------------------------------------------------------------------------

describe('Change status', () => {
  beforeEach(() => {
    current = eventDto({ canChangeStatus: true, allowedStatuses: [659490002, 659490003, 659490004, 659490006] });
  });

  it(
    'offers EXACTLY the BFF allowedStatuses (labelled), nothing chosen; Save is off until one is; a 204 sends only sprk_status, closes, and re-reads',
    async () => {
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Change status/, 'Change status');

      const select = within(dialog).getByRole('combobox', { name: /Status/ }) as HTMLSelectElement;
      expect(Array.from(select.options).map(o => o.text)).toEqual([
        'Select a status',
        'Completed',
        'Closed',
        'Cancelled',
        'On Hold',
      ]);
      // Not offered: the current status, and every status the BFF did not name.
      expect(Array.from(select.options).map(o => o.text)).not.toContain('Open');
      expect(Array.from(select.options).map(o => o.text)).not.toContain('Reassigned');
      expect(saveButton(dialog).disabled).toBe(true);

      pickOption(select, 'Completed');
      const readsBefore = eventReads();
      fireEvent.click(saveButton(dialog));
      await waitFor(() => expect(patches).toEqual([{ sprk_status: 659490002 }]), WAIT);
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Change status' })).toBeNull(), WAIT);
      await waitFor(() => expect(fieldOf(record, 'statuscode')).toBe('Completed'), WAIT);
      expect(eventReads()).toBeGreaterThan(readsBefore);
    },
    SLOW
  );

  it(
    '404: the dialog stays open with "This event is no longer available.", the record is re-read and shows not-found; a second 404 changes nothing',
    async () => {
      patchAnswer = () => {
        current = null;
        throw new ApiError(404, '');
      };
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Change status/, 'Change status');
      pickOption(within(dialog).getByRole('combobox', { name: /Status/ }) as HTMLSelectElement, 'Completed');
      const before = eventReads();
      fireEvent.click(saveButton(dialog));

      const error = await within(dialog).findByTestId('event-status-error', undefined, WAIT);
      expect(error.textContent).toBe('This event is no longer available.');
      await waitFor(() => expect(eventReads()).toBeGreaterThan(before), WAIT);
      expect(await screen.findByTestId('record-detail-notfound', undefined, WAIT)).toBeTruthy();
      await settle(300);
      expect(screen.getByRole('dialog', { name: 'Change status' })).toBeTruthy();

      const reads = eventReads();
      fireEvent.click(saveButton(dialog));
      await waitFor(() => expect(patches).toHaveLength(2), WAIT);
      await settle(400);
      expect(screen.getByRole('dialog', { name: 'Change status' })).toBeTruthy();
      expect(within(dialog).getByTestId('event-status-error').textContent).toBe('This event is no longer available.');
      expect(screen.getByTestId('record-detail-notfound')).toBeTruthy();
      expect(eventReads()).toBe(reads);
    },
    SLOW
  );

  it(
    'a refusal stays in the dialog with the same rules as Edit (403: right gone, re-read; the draft choice is kept)',
    async () => {
      patchAnswer = () => {
        throw new ApiError(403, '');
      };
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Change status/, 'Change status');
      pickOption(within(dialog).getByRole('combobox', { name: /Status/ }) as HTMLSelectElement, 'Closed');
      const before = eventReads();
      fireEvent.click(saveButton(dialog));

      const error = await within(dialog).findByTestId('event-status-error', undefined, WAIT);
      expect(error.textContent).toBe('You can no longer edit this event.');
      await waitFor(() => expect(eventReads()).toBeGreaterThan(before), WAIT);
      expect(screen.getByRole('dialog', { name: 'Change status' })).toBeTruthy();
    },
    SLOW
  );

  it(
    'Cancel closes it without a request',
    async () => {
      const record = await openEvent();
      const dialog = await openToolbarDialog(record, /^Change status/, 'Change status');
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Change status' })).toBeNull(), WAIT);
      expect(patches).toEqual([]);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Delete event (the creator only)
// ---------------------------------------------------------------------------

describe('Delete event', () => {
  it(
    'is offered only to the creator; it asks first, deletes through DELETE /events/{id}, shows the record as deleted and tells its opener',
    async () => {
      current = eventDto(MINE);
      const onRecordDeleted = vi.fn();
      const record = await openEvent({ onRecordDeleted });

      fireEvent.click(within(record).getByRole('button', { name: 'Delete event' }));
      const heading = await screen.findByText('Delete event', { selector: '[id^="sprk-modal-title-"]' }, WAIT);
      const confirm = heading.closest('[role="dialog"], [role="alertdialog"]') as HTMLElement;
      await settle(350);
      // Asked first: nothing deleted yet.
      expect(h.bffApiCall.mock.calls.filter(c => (c[1] as RequestInit | undefined)?.method === 'DELETE')).toHaveLength(
        0
      );
      fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));

      await waitFor(() => expect(onRecordDeleted).toHaveBeenCalledTimes(1), WAIT);
      expect(h.bffApiCall.mock.calls.filter(c => (c[1] as RequestInit | undefined)?.method === 'DELETE')).toEqual([
        [`/api/v1/external/events/${EVT}`, { method: 'DELETE' }],
      ]);
      expect(await within(record).findByTestId('record-detail-deleted', undefined, WAIT)).toBeTruthy();
    },
    SLOW
  );

  it(
    'a refused delete (403) shows the existing refusal and the record stays',
    async () => {
      current = eventDto(MINE);
      deleteAnswer = () => {
        throw new ApiError(
          403,
          JSON.stringify({ status: 403, title: 'Forbidden', reasonCode: 'sdap.access.deny.delete_own' })
        );
      };
      const onRecordDeleted = vi.fn();
      const record = await openEvent({ onRecordDeleted });

      fireEvent.click(within(record).getByRole('button', { name: 'Delete event' }));
      const heading = await screen.findByText('Delete event', { selector: '[id^="sprk-modal-title-"]' }, WAIT);
      const confirm = heading.closest('[role="dialog"], [role="alertdialog"]') as HTMLElement;
      await settle(350);
      fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));

      await screen.findByText(/cannot delete this event.*Nothing was deleted/, undefined, WAIT);
      expect(onRecordDeleted).not.toHaveBeenCalled();
      expect(within(record).queryByTestId('record-detail-deleted')).toBeNull();
      expect(fieldOf(record, 'sprk_name')).toBe('Hearing');
    },
    SLOW
  );

  it(
    'is not offered to someone who did not create the event, whatever else they may do (must-not-fire)',
    async () => {
      current = eventDto({ ...COLLABORATE, _sprk_createdbycontact_value: SOMEONE });
      const record = await openEvent();
      expect(within(record).queryByRole('button', { name: 'Delete event' })).toBeNull();
      expect(toolbarNames(record)).toEqual(['Refresh', 'Edit event', 'Change status', 'Copy link']);
    },
    SLOW
  );

  it('the service request keeps its own "Delete request" (the one delete id resolves per record type)', () => {
    // The dispatcher is keyed by entity, so a type with no delete host mounts none.
    expect(RECORD_TEMPLATES.sprk_servicerequest.toolbar).toContain('delete');
    expect(RECORD_TEMPLATES.sprk_event.toolbar).toContain('delete');
    expect(RECORD_TEMPLATES.sprk_matter.toolbar).not.toContain('delete');
  });
});

// ---------------------------------------------------------------------------
// The task's second type: a Task is just an event type
// ---------------------------------------------------------------------------

describe('event types in the record', () => {
  it(
    'a typed event shows its type by name, and an event with no type shows a dash, never an id',
    async () => {
      current = eventDto({ eventTypeId: TASK_TYPE.id, eventTypeName: 'Task' });
      const first = await openEvent();
      expect(fieldOf(first, 'sprk_eventtype')).toBe('Task');
      cleanup();

      current = eventDto({ eventTypeId: null, eventTypeName: null });
      const second = await openEvent();
      expect(fieldOf(second, 'sprk_eventtype')).toBe('—');
      expect(second.textContent).not.toContain(MEETING_TYPE.id);
    },
    SLOW
  );
});
