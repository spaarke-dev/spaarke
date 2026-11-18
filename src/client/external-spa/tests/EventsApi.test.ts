/**
 * R4 task 015 — the event routes of `web-api-client`: what is sent, and what the list says about types.
 *
 * `creatableEventTypes` rides on the list answer ONLY when the BFF sent an array (a missing member is "not said", never an
 * empty list, so an older BFF cannot be mistaken for "no type may be created"); malformed entries are dropped, never guessed.
 * `getEvent` / `updateEvent` / `getAssignablePeople` and the create body use the 014 routes. Also the one status / priority
 * label module (`events/eventFields.ts`).
 *
 * Substituted: the network seam (`bffApiCall`).
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall };
});

import { createRootEvent, getAssignablePeople, getEvent, getRootEvents, updateEvent } from '../src/api/web-api-client';
import {
  DEFAULT_EVENT_PRIORITY,
  EVENT_PRIORITY_OPTIONS,
  EVENT_STATUS_LABELS,
  getEventPriorityLabel,
  getEventStatusLabel,
} from '../src/components/events/eventFields';
import { EVENT_TYPES, MEETING_TYPE, TASK_TYPE } from './eventFixtures';

const ID = '00000000-0000-4000-8000-000000000201';
const EVT = '00000000-0000-4000-8000-000000000801';

beforeEach(() => {
  h.bffApiCall.mockReset();
});

describe('getRootEvents: creatableEventTypes', () => {
  it.each([
    ['an array of types', EVENT_TYPES, EVENT_TYPES],
    ['an empty array (the BFF said: none may be created)', [], []],
  ])('%s is carried on the result', async (_name, sent, expected) => {
    h.bffApiCall.mockResolvedValue({ value: [], canCreate: true, creatableEventTypes: sent });
    const result = await getRootEvents('matters', ID);
    expect(result.creatableEventTypes).toEqual(expected);
  });

  it.each([
    ['omitted (an older BFF)', {}],
    ['null', { creatableEventTypes: null }],
    ['a string', { creatableEventTypes: 'task' }],
    ['an object', { creatableEventTypes: { id: 'x' } }],
  ])('%s is NOT on the result (must-not-fire: never read as an empty list)', async (_name, extra) => {
    h.bffApiCall.mockResolvedValue({ value: [], ...extra });
    const result = await getRootEvents('matters', ID);
    expect('creatableEventTypes' in result).toBe(false);
  });

  it('drops malformed entries and reads requiresDueDate strictly', async () => {
    h.bffApiCall.mockResolvedValue({
      value: [],
      creatableEventTypes: [
        { id: TASK_TYPE.id, name: 'Task', requiresDueDate: true },
        { id: MEETING_TYPE.id, name: 'Meeting', requiresDueDate: 'yes' },
        { id: '', name: 'No id' },
        { id: 'no-name', name: '  ' },
        { name: 'Missing id' },
        null,
        'Task',
      ],
    });
    const result = await getRootEvents('matters', ID);
    expect(result.creatableEventTypes).toEqual([
      { id: TASK_TYPE.id, name: 'Task', requiresDueDate: true },
      { id: MEETING_TYPE.id, name: 'Meeting', requiresDueDate: false },
    ]);
  });

  it('no longer reads or returns canCreateTask', async () => {
    h.bffApiCall.mockResolvedValue({ value: [], canCreate: true, canCreateTask: true });
    const result = await getRootEvents('matters', ID);
    expect('canCreateTask' in result).toBe(false);
  });
});

describe('the 014 event routes', () => {
  it('getEvent reads GET /events/{id}', async () => {
    h.bffApiCall.mockResolvedValue({ sprk_eventid: EVT, sprk_name: 'Hearing', regarding: { kind: 'matter', id: ID } });
    const event = await getEvent(EVT);
    expect(h.bffApiCall).toHaveBeenCalledWith(`/api/v1/external/events/${EVT}`);
    expect(event.regarding).toEqual({ kind: 'matter', id: ID });
  });

  it('updateEvent PATCHes the partial body as given, to the event route', async () => {
    h.bffApiCall.mockResolvedValue(undefined);
    await expect(updateEvent(EVT, { sprk_name: 'New', sprk_description: null })).resolves.toBeUndefined();
    expect(h.bffApiCall).toHaveBeenCalledWith(`/api/v1/external/events/${EVT}`, {
      method: 'PATCH',
      body: JSON.stringify({ sprk_name: 'New', sprk_description: null }),
    });
  });

  it("getAssignablePeople reads the record's own route and keeps only well-formed people", async () => {
    h.bffApiCall.mockResolvedValue({
      value: [
        { contactId: ID, name: 'Pat Partner', isMe: true },
        { contactId: '', name: 'No id', isMe: false },
        { contactId: EVT, name: '', isMe: false },
      ],
    });
    const people = await getAssignablePeople('workassignments', ID);
    expect(h.bffApiCall).toHaveBeenCalledWith(`/api/v1/external/workassignments/${ID}/assignable-people`);
    expect(people).toEqual([{ contactId: ID, name: 'Pat Partner', isMe: true }]);
  });

  it('getAssignablePeople answers an empty list when the BFF sent no value', async () => {
    h.bffApiCall.mockResolvedValue({});
    expect(await getAssignablePeople('matters', ID)).toEqual([]);
  });

  it('a matter create posts eventTypeId and the new members with no project bind and NO eventType', async () => {
    h.bffApiCall.mockResolvedValue({ sprk_eventid: EVT, sprk_name: 'Hearing' });
    await createRootEvent('matters', ID, {
      sprk_name: 'Hearing',
      eventTypeId: MEETING_TYPE.id,
      sprk_priority: 100000001,
      sprk_description: 'Bring the exhibits',
      assignedToContactId: EVT,
    });
    const [path, options] = h.bffApiCall.mock.calls[0] as [string, RequestInit];
    expect(path).toBe(`/api/v1/external/matters/${ID}/events`);
    expect(JSON.parse(String(options.body))).toEqual({
      sprk_name: 'Hearing',
      eventTypeId: MEETING_TYPE.id,
      sprk_priority: 100000001,
      sprk_description: 'Bring the exhibits',
      assignedToContactId: EVT,
    });
  });

  it("a project create keeps the project route's body, project bind included", async () => {
    h.bffApiCall.mockResolvedValue({ sprk_eventid: EVT, sprk_name: 'Hearing' });
    await createRootEvent('projects', ID, { sprk_name: 'Hearing', eventTypeId: MEETING_TYPE.id });
    const [, options] = h.bffApiCall.mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(String(options.body))).toEqual({
      sprk_name: 'Hearing',
      eventTypeId: MEETING_TYPE.id,
      'sprk_RegardingProject@odata.bind': `/sprk_projects(${ID})`,
    });
  });
});

describe('eventFields: the one home of the status and priority words', () => {
  it('names every statuscode the BFF can send', () => {
    expect(EVENT_STATUS_LABELS).toMatchObject({
      1: 'Draft',
      2: 'No Further Action',
      659490001: 'Open',
      659490002: 'Completed',
      659490003: 'Closed',
      659490004: 'Cancelled',
      659490005: 'Transferred',
      659490006: 'On Hold',
      659490007: 'Reassigned',
    });
    expect(getEventStatusLabel(659490001)).toBe('Open');
  });

  it('names the four priorities in order, with Normal the default', () => {
    expect(EVENT_PRIORITY_OPTIONS).toEqual([
      { value: 100000000, label: 'Low' },
      { value: 100000001, label: 'Normal' },
      { value: 100000002, label: 'High' },
      { value: 100000003, label: 'Urgent' },
    ]);
    expect(DEFAULT_EVENT_PRIORITY).toBe(100000001);
    expect(getEventPriorityLabel(100000003)).toBe('Urgent');
  });

  it('an unknown or missing priority has no label (never a number), and an unknown status never shows a raw code', () => {
    expect(getEventPriorityLabel(null)).toBeNull();
    expect(getEventPriorityLabel(undefined)).toBeNull();
    expect(getEventPriorityLabel(5)).toBeNull();
    expect(getEventStatusLabel(123)).not.toMatch(/123/);
  });
});
