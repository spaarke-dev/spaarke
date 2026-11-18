/**
 * Shared fixtures for the event tests (spaarke-SPA-external-access-platform-r4 task 015): the creatable event types a list
 * answers, and an event row with the 014 fields. Test data only - the SPA never has a type list of its own.
 */
import type { CreatableEventType, ODataEvent } from '../src/api/web-api-client';

export const TASK_TYPE: CreatableEventType = {
  id: '11111111-1111-4111-8111-111111111111',
  name: 'Task',
  requiresDueDate: true,
};
export const MEETING_TYPE: CreatableEventType = {
  id: '22222222-2222-4222-8222-222222222222',
  name: 'Meeting',
  requiresDueDate: false,
};
/** What an events list answers as `creatableEventTypes`. */
export const EVENT_TYPES: CreatableEventType[] = [TASK_TYPE, MEETING_TYPE];

/** A GUID for test row `n` (matches the shape the BFF sends). */
export const eventGuid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

/** A list row with the 014 fields; `extra` overrides any of them. */
export function eventRow(n: number, name: string, extra: Partial<ODataEvent> = {}): ODataEvent {
  return {
    sprk_eventid: eventGuid(n),
    sprk_name: name,
    sprk_duedate: '2026-05-01',
    sprk_status: 659490001,
    createdon: '2026-04-01T09:00:00Z',
    eventTypeId: TASK_TYPE.id,
    eventTypeName: TASK_TYPE.name,
    sprk_priority: 100000001,
    ...extra,
  };
}
