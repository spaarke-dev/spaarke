/**
 * quickStartConfig.ts
 *
 * Per-card wizard step configurations for all Quick Start action cards.
 * Maps intent strings to wizard titles, step labels, file types, and follow-up actions.
 */
import type { IFollowUpAction } from "../Playbook/types";

// ---------------------------------------------------------------------------
// Config interface
// ---------------------------------------------------------------------------

export interface IQuickStartConfig {
  /** Wizard dialog title. */
  title: string;
  /** Label for Step 1 (upload/select). */
  uploadLabel: string;
  /** Label for Step 2 (analyze/execute). */
  analyzeLabel: string;
  /** Accepted file types for upload step. */
  acceptedFileTypes: string[];
  /** Whether multiple files can be uploaded. */
  allowMultiple: boolean;
  /** The playbook intent to execute. */
  playbookIntent: string;
  /** Available follow-up actions for Step 3. */
  followUpActions: IFollowUpAction[];
}

// ---------------------------------------------------------------------------
// Placeholder action handlers
// ---------------------------------------------------------------------------

// These are placeholder handlers — real navigation will be wired in integration tasks.
const noop = (_analysisId: string): void => {
  console.log("[QuickStart] Action clicked — will be wired in integration task");
};

// ---------------------------------------------------------------------------
// Config map
// ---------------------------------------------------------------------------

export const QUICKSTART_CONFIGS: Record<string, IQuickStartConfig> = {
  "document-search": {
    title: "Search Document Files",
    uploadLabel: "Upload Documents",
    analyzeLabel: "Search Documents",
    acceptedFileTypes: ["application/pdf", ".docx", ".xlsx"],
    allowMultiple: true,
    playbookIntent: "document-search",
    followUpActions: [
      {
        id: "share-results",
        label: "Share Results",
        description: "Share the search results with your team.",
        icon: "share",
        onClick: noop,
      },
    ],
  },

  "email-compose": {
    title: "Send Email Message",
    uploadLabel: "Upload Attachments",
    analyzeLabel: "Compose Email",
    acceptedFileTypes: ["application/pdf", ".docx", ".xlsx", ".msg"],
    allowMultiple: true,
    playbookIntent: "email-compose",
    followUpActions: [
      {
        id: "send-email",
        label: "Send Email",
        description: "Send the composed email to recipients.",
        icon: "mail",
        onClick: noop,
      },
    ],
  },

  "assign-counsel": {
    title: "Assign to Counsel",
    uploadLabel: "Select Document",
    analyzeLabel: "Assign Counsel",
    acceptedFileTypes: ["application/pdf", ".docx"],
    allowMultiple: false,
    playbookIntent: "assign-counsel",
    followUpActions: [
      {
        id: "notify-counsel",
        label: "Notify Counsel",
        description: "Send a notification to the assigned counsel.",
        icon: "people",
        onClick: noop,
      },
    ],
  },

  "meeting-schedule": {
    title: "Schedule New Meeting",
    uploadLabel: "Select Context",
    analyzeLabel: "Schedule Meeting",
    acceptedFileTypes: ["application/pdf", ".docx"],
    allowMultiple: false,
    playbookIntent: "meeting-schedule",
    followUpActions: [
      {
        id: "send-invite",
        label: "Send Invite",
        description: "Send the meeting invite to participants.",
        icon: "calendar",
        onClick: noop,
      },
    ],
  },
};
