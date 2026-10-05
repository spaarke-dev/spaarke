/**
 * eventDueDate — Dataverse event record → due-date card data (PCF-side).
 *
 * One copy for the two containers (`DueDateCard.tsx`, `DueDateCardList.tsx`,
 * which held identical `mapEventToCardProps` copies) and `ViewDataService.ts`.
 *
 * Task 081 (H1 + H2):
 *   - `sprk_duedate` / `sprk_finalduedate` are DateOnly ("YYYY-MM-DD"). They are
 *     parsed with the shared `parseDueDate` (LOCAL midnight). The former
 *     `new Date("YYYY-MM-DD")` is UTC midnight — the previous evening in every
 *     US zone — so a due date of today read as 1 day overdue, and the
 *     "active date" selection below skipped a `sprk_duedate` of today.
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
 * v1.4.15 — the "active" due date, honouring the chart's "5-day window":
 *   - `sprk_duedate` if it is today or later (the planned date is still the deadline);
 *   - else `sprk_finalduedate` if it is today or later (the extended date is now
 *     the deadline — `sprk_duedate` has passed but the event still qualifies);
 *   - else whichever date exists (overdue edge case), else `now`.
 * This matches the FetchXML `next-x-days` OR filter so the card shows and
 * counts down to whichever date kept the event qualified.
 */
export function selectActiveDueDate(
  duedate: string | null | undefined,
  finalduedate: string | null | undefined,
  now: Date = new Date()
): Date {
  const due = parseDueDate(duedate);
  const finalDue = parseDueDate(finalduedate);
  if (due && daysBetweenLocalMidnight(now, due) >= 0) return due;
  if (finalDue && daysBetweenLocalMidnight(now, finalDue) >= 0) return finalDue;
  return due || finalDue || now;
}

/** Map a Dataverse event record to `EventDueDateCard` props. */
export function mapEventToCardProps(record: Record<string, unknown>, now: Date = new Date()): IEventDueDateCardProps {
  const dueDate = selectActiveDueDate(
    record.sprk_duedate as string | undefined,
    record.sprk_finalduedate as string | undefined,
    now
  );
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
