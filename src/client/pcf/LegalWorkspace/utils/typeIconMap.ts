/**
 * typeIconMap — maps sprk_event.sprk_type strings to Fluent UI v9 icon
 * components for use in FeedItemCard.
 *
 * Icon selection follows the task 011 spec:
 *   Email        → MailRegular
 *   Document     → DocumentRegular
 *   Task         → TaskListRegular
 *   Invoice      → ReceiptRegular
 *   Alert/Other  → AlertRegular
 *
 * Import the icon component rather than returning JSX so callers can control
 * sizing and className themselves.
 */

import {
  MailRegular,
  DocumentRegular,
  TaskListSquareLtrRegular,
  ReceiptRegular,
  AlertRegular,
} from "@fluentui/react-icons";
import type { FluentIcon } from "@fluentui/react-icons";

/**
 * Returns the Fluent UI v9 icon component for a given event type string.
 * Matching is case-insensitive.  Unmapped types fall back to AlertRegular.
 *
 * @param eventType - The value of sprk_event.sprk_type (e.g. "Email", "Invoice")
 * @returns A Fluent UI v9 FluentIcon component (not yet rendered)
 */
export function getTypeIcon(eventType: string | undefined): FluentIcon {
  const normalised = (eventType ?? "").toLowerCase();

  switch (normalised) {
    case "email":
      return MailRegular;

    case "document":
    case "documentreview":
      return DocumentRegular;

    case "task":
      return TaskListSquareLtrRegular;

    case "invoice":
      return ReceiptRegular;

    case "alertresponse":
    case "financial-alert":
    case "alert":
    case "meeting":
    case "analysis":
    case "status-change":
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
    email: "Email",
    document: "Document",
    documentreview: "Document review",
    task: "Task",
    invoice: "Invoice",
    alertresponse: "Alert",
    "financial-alert": "Financial alert",
    alert: "Alert",
    meeting: "Meeting",
    analysis: "Analysis",
    "status-change": "Status change",
  };

  return labelMap[normalised] ?? "Event";
}
