/**
 * eventDueDate — Dataverse event record → due-date card data (PCF-side).
 *
 * One copy for the two containers (`DueDateCard.tsx`, `DueDateCardList.tsx`,
 * which held identical `mapEventToCardProps` copies) and `ViewDataService.ts`.
 *
 * Task 081 (H1 + H2):
 *   - `sprk_duedate` is DateOnly ("YYYY-MM-DD"). It is
 *     parsed with the shared `parseDueDate` (LOCAL midnight). The former
 *     `new Date("YYYY-MM-DD")` is UTC midnight — the previous evening in every
 *     US zone — so a due date of today read as 1 day overdue, and the
 *     due-date selection skipped a `sprk_duedate` of today.
 *   - Days are CALENDAR days between local midnights (`daysBetweenLocalMidnight`).
 *   - The tier comes from THE shared tier function `dueUrgencyForDays` and is
 *     passed to `@spaarke/visuals`' `EventDueDateCard` as `urgency`; that
 *     package keeps only tier → colour.
 */

import type { IEventDueDateCardProps } from '@spaarke/visuals';
// React-free, dependency-free module imported from source (same precedent as this
// PCF's `oobModalSizes` / `xrmContext` imports) so it does not pull the whole
// `@spaarke/ui-components` barrel into the PCF bundle or its jest graph.
import {
  daysBetweenLocalMidnight,
  dueUrgencyForDays,
  parseDueDate,
  type DueUrgency,
} from '../../../../shared/Spaarke.UI.Components/src/utils/dateLocal';

export interface IEventDueDays {
  /** Calendar days from today to the due date; negative when overdue. */
  daysUntilDue: number;
  isOverdue: boolean;
  urgency: DueUrgency;
}

/** Days until `dueDate` (calendar days, local midnight) and its tier, relative to `now`. */
export function computeEventDueDays(dueDate: Date, now: Date = new Date()): IEventDueDays {
  const daysUntilDue = daysBetweenLocalMidnight(now, dueDate);
  return { daysUntilDue, isOverdue: daysUntilDue < 0, urgency: dueUrgencyForDays(daysUntilDue) };
}

/**
 * The card's due date: `sprk_duedate` ONLY (D-27 / D-63, 2026-10-07). `sprk_finalduedate` is informational and never
 * decides what the card shows or counts down to — a rescheduled task shows its new `sprk_duedate`, and a task whose
 * old final due date has passed is not shown as overdue by it. (Before D-63 this fell back to `sprk_finalduedate`
 * once `sprk_duedate` had passed.) Returns null when there is no `sprk_duedate`: the card never invents a date.
 */
export function selectDueDate(duedate: string | null | undefined): Date | null {
  return parseDueDate(duedate);
}

/**
 * Map a Dataverse event record to `EventDueDateCard` props, or `null` when the event has no `sprk_duedate` — it is
 * left out of the due-date countdown (a record with only the informational `sprk_finalduedate`, which the view can
 * still return, must not show today's date with a "Today" badge). `DueDateCard` already renders `null` as its
 * no-record state; `DueDateCardList` drops the entry.
 */
export function mapEventToCardProps(
  record: Record<string, unknown>,
  now: Date = new Date()
): IEventDueDateCardProps | null {
  const dueDate = selectDueDate(record.sprk_duedate as string | undefined);
  if (!dueDate) return null;
  const { daysUntilDue, isOverdue, urgency } = computeEventDueDays(dueDate, now);

  // Event type from FetchXML link-entity alias or formatted value
  const eventTypeColor = (record['eventtype.sprk_eventtypecolor'] as string) || undefined;
  const eventTypeName =
    (record['_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue'] as string) ||
    (record['eventtype.sprk_name'] as string) ||
    'Event';

  return {
    eventId: (record.sprk_eventid as string) || '',
    eventName: (record.sprk_eventname as string) || 'Untitled Event',
    eventTypeName,
    dueDate,
    daysUntilDue,
    isOverdue,
    urgency,
    eventTypeColor: eventTypeColor || undefined,
    description: record.sprk_description as string | undefined,
    assignedTo: (record['_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue'] as string) || undefined,
  };
}
