/**
 * typeIconMap — maps sprk_eventtype_ref display names to Fluent UI v9 icon
 * components for use in FeedItemCard.
 *
 * Event types come from the sprk_eventtype_ref lookup table.
 * Current values: Action, Approval, Communication, Deadline, Filing,
 * Meeting, Milestone, Notification, Reminder, Status Change, Task, To Do.
 *
 * Import the icon component rather than returning JSX so callers can control
 * sizing and className themselves.
 */

import {
  MailRegular,
  DocumentRegular,
  TaskListSquareLtrRegular,
  CalendarRegular,
  PeopleRegular,
  AlertRegular,
  CheckmarkCircleRegular,
  ClockRegular,
} from "@fluentui/react-icons";
import type { FluentIcon } from "@fluentui/react-icons";

/**
 * Returns the Fluent UI v9 icon component for a given event type display name.
 * Matching is case-insensitive. Unmapped types fall back to AlertRegular.
 *
 * @param eventType - The formatted display value from sprk_eventtype_ref (e.g. "Task", "Filing")
 * @returns A Fluent UI v9 FluentIcon component (not yet rendered)
 */
export function getTypeIcon(eventType: string | undefined): FluentIcon {
  const normalised = (eventType ?? "").toLowerCase();

  switch (normalised) {
    case "communication":
      return MailRegular;

    case "filing":
      return DocumentRegular;

    case "task":
    case "to do":
    case "action":
      return TaskListSquareLtrRegular;

    case "deadline":
    case "reminder":
      return ClockRegular;

    case "meeting":
      return PeopleRegular;

    case "milestone":
      return CheckmarkCircleRegular;

    case "approval":
      return CalendarRegular;

    case "notification":
    case "status change":
    default:
      return AlertRegular;
  }
}

/**
 * Returns an accessible label for the icon based on event type.
 * Used as aria-label on the icon wrapper for screen-reader context.
 */
export function getTypeIconLabel(eventType: string | undefined): string {
  const normalised = (eventType ?? "").toLowerCase();

  const labelMap: Record<string, string> = {
    action: "Action",
    approval: "Approval",
    communication: "Communication",
    deadline: "Deadline",
    filing: "Filing",
    meeting: "Meeting",
    milestone: "Milestone",
    notification: "Notification",
    reminder: "Reminder",
    "status change": "Status Change",
    task: "Task",
    "to do": "To Do",
  };

  return labelMap[normalised] ?? "Event";
}
