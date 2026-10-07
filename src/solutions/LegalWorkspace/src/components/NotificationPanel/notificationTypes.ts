import { INotificationItem, NotificationCategory } from "../../types";

/** Re-export for convenience within this module */
export type { INotificationItem, NotificationCategory };

/** Category display metadata */
export interface INotificationCategoryMeta {
  key: NotificationCategory;
  label: string;
}

export const NOTIFICATION_CATEGORIES: INotificationCategoryMeta[] = [
  { key: "Documents", label: "Documents" },
  { key: "Invoices", label: "Invoices" },
  { key: "Status", label: "Status" },
  { key: "Analysis", label: "Analysis" },
];

/** Mock notification data for R1 (no backend yet) */
export const MOCK_NOTIFICATIONS: INotificationItem[] = [
  {
    id: "n-001",
    title: "Contract Amendment Uploaded",
    description: "A new version of the MSA Amendment has been uploaded to the Henderson matter.",
    category: "Documents",
    timestamp: new Date(Date.now() - 2 * 60 * 1000).toISOString(),
    isRead: false,
    entityType: "sprk_document",
    entityId: "doc-001",
  },
  {
    id: "n-002",
    title: "Invoice Pending Approval",
    description: "Billing invoice #INV-2024-0841 for $12,500 is awaiting your approval.",
    category: "Invoices",
    timestamp: new Date(Date.now() - 38 * 60 * 1000).toISOString(),
    isRead: false,
    entityType: "sprk_event",
    entityId: "evt-002",
  },
  {
    id: "n-003",
    title: "Matter Status Changed",
    description: "The Acme Corp Litigation matter has moved to Active — Discovery phase.",
    category: "Status",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
    isRead: true,
    entityType: "sprk_matter",
    entityId: "matter-003",
  },
  {
    id: "n-004",
    title: "AI Analysis Complete",
    description: "Risk analysis for the Johnson IP portfolio has finished. 3 high-priority items found.",
    category: "Analysis",
    timestamp: new Date(Date.now() - 4 * 60 * 60 * 1000).toISOString(),
    isRead: false,
    entityType: "sprk_event",
    entityId: "evt-004",
  },
  {
    id: "n-005",
    title: "NDA Document Reviewed",
    description: "The non-disclosure agreement for Project Titan has been reviewed and marked complete.",
    category: "Documents",
    timestamp: new Date(Date.now() - 6 * 60 * 60 * 1000).toISOString(),
    isRead: true,
    entityType: "sprk_document",
    entityId: "doc-005",
  },
  {
    id: "n-006",
    title: "Invoice Overdue",
    description: "Invoice #INV-2024-0799 ($8,200) is now 14 days overdue. Action required.",
    category: "Invoices",
    timestamp: new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString(),
    isRead: false,
    entityType: "sprk_event",
    entityId: "evt-006",
  },
  {
    id: "n-007",
    title: "Matter Budget Threshold Reached",
    description: "The Garcia Employment matter has reached 85% of its $50,000 budget allocation.",
    category: "Status",
    timestamp: new Date(Date.now() - 28 * 60 * 60 * 1000).toISOString(),
    isRead: true,
    entityType: "sprk_matter",
    entityId: "matter-007",
  },
  {
    id: "n-008",
    title: "Briefing Document Generated",
    description: "AI briefing summary for the quarterly portfolio review is ready for your review.",
    category: "Analysis",
    timestamp: new Date(Date.now() - 2 * 24 * 60 * 60 * 1000).toISOString(),
    isRead: true,
    entityType: "sprk_event",
    entityId: "evt-008",
  },
];

// formatRelativeTime hoisted to @spaarke/ui-components (task 081 / C-13) —
// this previously hand-rolled its own minute/hour/day/month bucketing (and,
// unlike the shared formatter, never handled a future timestamp — see that
// module's header), one of five independently reimplemented copies of the
// same idiom.
