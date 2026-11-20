/**
 * eventDetail — an event as the record template shows it (spaarke-SPA-external-access-platform-r4 task 015, FR-36).
 *
 * The `sprk_event` template reads an event from the BFF's event DTO (`GET /events/{id}`, or the row of the list it was opened
 * from), not from the module fetch seam: the DTO carries the type, assignee and creator NAMES and the caller's rights, none of
 * which the seam can give. `toEventDetailRecord` turns that DTO into the same `DetailRecord` shape every other entity has, so
 * the one Information section (`RecordDetailFields`) shows it:
 *   - the columns of `RECORD_DETAIL_ENTITIES.sprk_event` (raw values: dates, text);
 *   - `@formattedValues` for the coded and lookup fields (status, priority, type, assignee, creator, regarding), so
 *     `formatDetailValue` shows a NAME, never an id, and "—" when empty;
 *   - the raw DTO under `__event` (and the regarding record under `__regarding`), which the action hosts read: `canEdit`,
 *     `canChangeStatus`, `allowedStatuses`, `canReassign` and the creator stamps. The record itself carries no contact id.
 */
import { getEvent, type EventsRoot, type ODataEvent, type ODataEventRegarding } from '../../api/web-api-client';
import type { DetailFieldLink, DetailRecord } from '../detail/RecordDetailFields';
import { getEventPriorityLabel, getEventStatusLabel } from './eventFields';

/** Where the raw event DTO rides on a `DetailRecord`. */
export const EVENT_RECORD_KEY = '__event';
/** Where the regarding record (kind, id, name) rides on a `DetailRecord`. */
export const EVENT_REGARDING_KEY = '__regarding';

/** The record template entity of each regarding kind (`ODataEventRegarding.kind`), and how its noun reads in a sentence. */
const REGARDING_TARGETS: Readonly<Record<string, { entity: string; noun: string }>> = {
  project: { entity: 'sprk_project', noun: 'project' },
  matter: { entity: 'sprk_matter', noun: 'matter' },
  workassignment: { entity: 'sprk_workassignment', noun: 'work assignment' },
};

function regardingTargetOf(kind: string): { entity: string; noun: string } | undefined {
  return Object.prototype.hasOwnProperty.call(REGARDING_TARGETS, kind) ? REGARDING_TARGETS[kind] : undefined;
}

/** The raw event DTO a record was built from, or null when it is not an event record. */
export function eventOf(record: DetailRecord | null | undefined): ODataEvent | null {
  const raw = record?.[EVENT_RECORD_KEY];
  return raw && typeof raw === 'object' ? (raw as ODataEvent) : null;
}

/** The record the event is regarding, or null when the record does not know it. */
export function regardingOf(record: DetailRecord | null | undefined): ODataEventRegarding | null {
  const raw = record?.[EVENT_REGARDING_KEY];
  if (!raw || typeof raw !== 'object') return null;
  const { kind, id } = raw as Partial<ODataEventRegarding>;
  return typeof kind === 'string' && typeof id === 'string' && id ? (raw as ODataEventRegarding) : null;
}

/**
 * The link of the Regarding field: the parent record, as a record template target. Null when the event does not say what it
 * is regarding or names a kind the SPA has no template for (a plain "—" then, never a dead link).
 */
export function eventRegardingLink(record: DetailRecord): DetailFieldLink | null {
  const regarding = regardingOf(record);
  const target = regarding ? regardingTargetOf(regarding.kind) : undefined;
  return regarding && target ? { entity: target.entity, id: regarding.id, noun: target.noun } : null;
}

const ROOT_OF_KIND: Readonly<Record<string, EventsRoot>> = {
  project: 'projects',
  matter: 'matters',
  workassignment: 'workassignments',
};

/** The `{root}` of the record's own routes (assignable people, events) for what the event is regarding; null when unknown. */
export function eventsRootOfRegarding(regarding: ODataEventRegarding | null | undefined): EventsRoot | null {
  return regarding && Object.prototype.hasOwnProperty.call(ROOT_OF_KIND, regarding.kind)
    ? ROOT_OF_KIND[regarding.kind]
    : null;
}

function formatted(entries: Record<string, string | null | undefined>): Record<string, string> {
  const map: Record<string, string> = {};
  for (const [attribute, value] of Object.entries(entries)) {
    if (typeof value === 'string' && value.trim()) map[attribute] = value;
  }
  return map;
}

/** Builds the record the `sprk_event` template shows from an event DTO and the record it is regarding (when known). */
export function toEventDetailRecord(event: ODataEvent, regarding?: ODataEventRegarding | null): DetailRecord {
  const target = regarding ? regardingTargetOf(regarding.kind) : undefined;
  const priorityLabel = getEventPriorityLabel(event.sprk_priority);
  const status = event.sprk_status ?? null;
  return {
    sprk_eventid: event.sprk_eventid,
    sprk_name: event.sprk_name,
    sprk_duedate: event.sprk_duedate ?? null,
    createdon: event.createdon ?? null,
    sprk_description: event.sprk_description ?? null,
    // Coded and lookup columns carry NO raw value: the display text is in `@formattedValues`, and a raw id must never show.
    sprk_eventtype: null,
    statuscode: status,
    sprk_priority: priorityLabel === null ? null : event.sprk_priority,
    sprk_assignedto: null,
    sprk_createdby: null,
    sprk_regarding: null,
    '@formattedValues': formatted({
      sprk_eventtype: event.eventTypeName,
      statuscode: status === null ? null : getEventStatusLabel(status),
      sprk_priority: priorityLabel,
      sprk_assignedto: event.assignedToName,
      sprk_createdby: event.createdByName,
      sprk_regarding: regarding ? regarding.name?.trim() || (target ? target.noun : null) : null,
    }),
    [EVENT_RECORD_KEY]: event,
    [EVENT_REGARDING_KEY]: regarding ?? null,
  };
}

/** The `sprk_event` entity's default reader: `GET /events/{id}` mapped to a record (also what `?record=event:{id}` reads). */
export async function readEventRecord(eventId: string): Promise<DetailRecord> {
  const event = await getEvent(eventId);
  return toEventDetailRecord(event, event.regarding);
}
