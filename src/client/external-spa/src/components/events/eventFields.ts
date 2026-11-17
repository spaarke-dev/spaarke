/**
 * eventFields — the labels of an event's coded fields, in one place
 * (spaarke-SPA-external-access-platform-r4 task 015, FR-35 / FR-36).
 *
 * The Events list, the Event record (`sprk_event` template) and the Add / Edit / Change status dialogs all show the same two
 * coded fields, so their words and colours live here and nowhere else:
 *   - the event STATUS (Dataverse `statuscode`; the wire name is `sprk_status`);
 *   - the event PRIORITY (`sprk_priority`: Low 100000000, Normal 100000001, High 100000002, Urgent 100000003).
 * An unknown or missing value is shown as such, never defaulted ("Open" / "Normal").
 */

/**
 * Event status wire value -> label. The BFF reads it from the event's status of record, Dataverse `statuscode` (task 097
 * review F2). Live option set, verified 2026-10-05.
 */
export const EVENT_STATUS_LABELS: Readonly<Record<number, string>> = {
  1: 'Draft',
  659490001: 'Open',
  659490002: 'Completed',
  659490003: 'Closed',
  659490004: 'Cancelled',
  659490005: 'Transferred',
  659490006: 'On Hold',
  659490007: 'Reassigned',
  2: 'No Further Action',
};

export function getEventStatusLabel(status: number | null | undefined): string {
  if (status === null || status === undefined) return 'Unknown';
  return EVENT_STATUS_LABELS[status] ?? 'Unknown';
}

/** Status -> Fluent Badge colour. */
export function getEventStatusColor(
  status: number | null | undefined
): 'brand' | 'success' | 'warning' | 'danger' | 'informative' | undefined {
  switch (status) {
    case 659490001: // Open
      return 'brand';
    case 659490006: // On Hold
    case 659490007: // Reassigned
      return 'warning';
    case 659490002: // Completed
    case 659490003: // Closed
      return 'success';
    case 659490004: // Cancelled
      return 'danger';
    default: // Draft, Transferred, No Further Action, unknown
      return 'informative';
  }
}

/** Priority wire value -> label, in display order (Low to Urgent). */
export const EVENT_PRIORITY_OPTIONS: ReadonlyArray<{ value: number; label: string }> = [
  { value: 100000000, label: 'Low' },
  { value: 100000001, label: 'Normal' },
  { value: 100000002, label: 'High' },
  { value: 100000003, label: 'Urgent' },
];

/** The priority a new event starts with (owner Q-4: "Normal"). */
export const DEFAULT_EVENT_PRIORITY = 100000001;

/** The label of a priority value; `null` for a missing or unknown one (the caller shows its own empty text). */
export function getEventPriorityLabel(priority: number | null | undefined): string | null {
  if (priority === null || priority === undefined) return null;
  return EVENT_PRIORITY_OPTIONS.find(o => o.value === priority)?.label ?? null;
}
