/**
 * types.ts
 * Configuration and context interfaces for the reusable CreateRecordWizard.
 *
 * The CreateRecordWizard extracts common multi-step wizard boilerplate
 * (file upload, follow-on steps, state management) so that each entity
 * wizard provides only entity-specific config and a finish handler.
 *
 * @see WizardShell — the underlying generic dialog shell
 * @see ADR-012 — Shared Component Library
 */
import type * as React from 'react';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IWizardSuccessConfig } from '../Wizard/wizardShellTypes';

// ---------------------------------------------------------------------------
// Follow-on action identifiers
// ---------------------------------------------------------------------------

/** Identifiers for the optional follow-on action cards shown in "Next Steps". */
export type FollowOnActionId = 'assign-counsel' | 'draft-summary' | 'send-email';

// ---------------------------------------------------------------------------
// Recipient item (used by Draft Summary and Send Email)
// ---------------------------------------------------------------------------

/** A recipient entry in the DraftSummary distribute-to / CC fields. */
export interface IRecipientItem {
  /** Unique key for deduplication (contact GUID or freeform email). */
  key: string;
  /** Display name shown in the chip. */
  displayName: string;
  /** Email address (extracted from contact or freeform entry). */
  email: string;
  /** Whether this was manually entered (true) or from contact lookup (false). */
  isManual?: boolean;
}

// ---------------------------------------------------------------------------
// Entity-specific step configuration
// ---------------------------------------------------------------------------

/** Configuration for the entity-specific "Enter Info" step. */
export interface IEntityInfoStep {
  /** Step ID (e.g. "enter-info", "create-record"). */
  id: string;
  /** Step label shown in the sidebar (e.g. "Enter Info"). */
  label: string;
  /** Returns true when the user may advance past this step. */
  canAdvance: () => boolean;
  /** Renders the entity-specific form content. Receives the uploaded files from Step 1. */
  renderContent: (uploadedFiles: import('../FileUpload/fileUploadTypes').IUploadedFile[]) => React.ReactNode;
}

// ---------------------------------------------------------------------------
// Follow-on state (passed to onFinish and used by dynamic steps)
// ---------------------------------------------------------------------------

/** State collected from the optional follow-on steps. */
export interface IFollowOnState {
  /** IDs of attorney/paralegal/outside-counsel assignments. */
  assignedAttorneyId: string;
  assignedAttorneyName: string;
  assignedParalegalId: string;
  assignedParalegalName: string;
  assignedOutsideCounselId: string;
  assignedOutsideCounselName: string;
  /** Whether "Notify assigned resources" is checked. */
  notifyResources: boolean;
  /** AI-generated or user-edited summary text. */
  summaryText: string;
  /** "Distribute to" recipients for draft summary. */
  recipients: IRecipientItem[];
  /** CC recipients for draft summary. */
  ccRecipients: IRecipientItem[];
  /** Email "To" address (resolved from user lookup). */
  emailTo: string;
  /** Email subject line. */
  emailSubject: string;
  /** Email body text. */
  emailBody: string;
}

// ---------------------------------------------------------------------------
// Finish context (everything the entity onFinish needs from the wizard)
// ---------------------------------------------------------------------------

/** Context passed to the entity's onFinish callback. */
export interface IFinishContext {
  /** Files uploaded in step 0 (may be empty if skipped). */
  uploadedFiles: IUploadedFile[];
  /** SPE container ID resolved from user's Business Unit. */
  speContainerId: string;
  /** Which follow-on actions the user selected in "Next Steps". */
  selectedActions: FollowOnActionId[];
  /** State from follow-on steps (assign, draft, email). */
  followOn: IFollowOnState;
}

// ---------------------------------------------------------------------------
// Wizard configuration (provided by each entity wrapper)
// ---------------------------------------------------------------------------

/** Full configuration for a CreateRecordWizard instance. */
export interface ICreateRecordWizardConfig {
  /** Dialog title (e.g. "Create New Matter", "Create New Event"). */
  title: string;
  /** Entity label for Next Steps text (e.g. "matter", "event", "to-do"). */
  entityLabel: string;
  /** Subtitle for the Add Files step (optional override). */
  filesStepSubtitle?: string;
  /** The entity-specific form step definition. */
  infoStep: IEntityInfoStep;
  /**
   * Called when the wizard finishes. The entity creates its record here
   * and returns an IWizardSuccessConfig to show the success screen.
   */
  onFinish: (context: IFinishContext) => Promise<IWizardSuccessConfig>;
  /** Label shown on the button while finishing (e.g. "Creating matter…"). */
  finishingLabel?: string;
  /**
   * Optional callback to build a default email subject from the entity name.
   * Receives the entity name string. If not provided, a generic default is used.
   */
  buildEmailSubject?: (entityName: string) => string;
  /**
   * Optional callback to build a default email body.
   * Receives an object with common form fields. If not provided, a generic
   * default is used.
   */
  buildEmailBody?: (fields: Record<string, string>) => string;
  /**
   * Optional callback providing the entity name for email pre-fill.
   * Called when the "send-email" follow-on action is selected.
   * Returns the current entity name from the form state.
   */
  getEntityName?: () => string;
  /**
   * Optional callback providing form fields for email body pre-fill.
   * Returns a key-value object of form field names and values.
   */
  getFormFields?: () => Record<string, string>;
  /**
   * Optional callback to fetch AI draft summary text.
   * If not provided, the DraftSummary step shows a manual-entry textarea.
   */
  fetchAiSummary?: () => Promise<{ summary: string }>;
  /**
   * Optional callback to resolve the SPE container ID for file uploads.
   * Called when the wizard opens. If not provided, speContainerId will be
   * empty (acceptable for entities that don't need file uploads).
   */
  resolveSpeContainerId?: () => Promise<string>;

  // ── Search callbacks for follow-on steps ─────────────────────────────
  /** Search contacts for attorney/paralegal assignment. */
  searchContacts?: SearchCallback;
  /** Search organizations for outside counsel assignment. */
  searchOrganizations?: SearchCallback;
  /** Search system users for email "To" field. */
  searchUsers?: SearchCallback;
}

// ---------------------------------------------------------------------------
// Component props
// ---------------------------------------------------------------------------

/** Props for the CreateRecordWizard component. */
export interface ICreateRecordWizardProps {
  /** Whether the wizard dialog is open. */
  open: boolean;
  /** Callback when user closes / cancels the wizard. */
  onClose: () => void;
  /** Dataverse WebApi for search callbacks and SPE container resolution. */
  webApi: {
    retrieveMultipleRecords(
      entityLogicalName: string,
      options?: string,
      maxPageSize?: number
    ): Promise<{ entities: Record<string, unknown>[]; nextLink?: string }>;
    retrieveRecord(
      entityLogicalName: string,
      id: string,
      options?: string
    ): Promise<Record<string, unknown>>;
    createRecord(
      entityLogicalName: string,
      data: Record<string, unknown>
    ): Promise<{ id: string }>;
  };
  /** Entity-specific wizard configuration. */
  config: ICreateRecordWizardConfig;
}

// ---------------------------------------------------------------------------
// Search callback type (used by AssignResources and DraftSummary)
// ---------------------------------------------------------------------------

/** Standard search callback signature for lookup fields. */
export type SearchCallback = (query: string) => Promise<ILookupItem[]>;
