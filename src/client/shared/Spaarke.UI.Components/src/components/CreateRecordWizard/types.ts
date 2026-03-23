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
import type { AssociationResult, EntityTypeOption } from '../AssociateToStep/types';
import type { INavigationService } from '../../types/serviceInterfaces';

// Re-export for convenience
export type { AssociationResult, EntityTypeOption };

// ---------------------------------------------------------------------------
// Associate-To step configuration (optional first step)
// ---------------------------------------------------------------------------

/**
 * Configuration for the optional AssociateToStep prepended to the wizard as step 1.
 *
 * When present, the step sequence becomes:
 *   1. Associate To  (new — optional, skip-able)
 *   2. Add file(s)   (was step 1)
 *   3. Entity info   (was step 2)
 *   4. Next Steps    (was step 3)
 */
export interface IAssociateToStepConfig {
  /**
   * Entity types the user may associate the new record with.
   * At least one entry is required.
   */
  entityTypes: EntityTypeOption[];
  /**
   * Navigation service used to open the Dataverse lookup side pane.
   * Typically injected by the consuming wizard from a PCF or Code Page adapter.
   */
  navigationService: INavigationService;
}

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

/**
 * State collected from the "Assign Work" follow-on step.
 * Used to create a sprk_workassignment record linked to the parent entity.
 */
export interface IAssignWorkFollowOnState {
  /** Work assignment name (required to create the record). */
  assignWorkName: string;
  /** Work assignment description (optional). */
  assignWorkDescription: string;
  /** Matter Type lookup ID (auto-filled from parent matter/project). */
  assignWorkMatterTypeId: string;
  /** Matter Type display name. */
  assignWorkMatterTypeName: string;
  /** Practice Area lookup ID (auto-filled from parent record). */
  assignWorkPracticeAreaId: string;
  /** Practice Area display name. */
  assignWorkPracticeAreaName: string;
  /**
   * Priority option set value.
   * 100000000=Low, 100000001=Normal (default), 100000002=High, 100000003=Critical
   */
  assignWorkPriority: number;
  /** Response Due Date as ISO date string (e.g. "2026-04-15"), or empty string. */
  assignWorkResponseDueDate: string;
  /** Assigned Attorney contact GUID. */
  assignedAttorneyId: string;
  /** Assigned Attorney display name. */
  assignedAttorneyName: string;
  /** Assigned Paralegal contact GUID. */
  assignedParalegalId: string;
  /** Assigned Paralegal display name. */
  assignedParalegalName: string;
  /** Assigned Outside Counsel organization GUID. */
  assignedOutsideCounselId: string;
  /** Assigned Outside Counsel display name. */
  assignedOutsideCounselName: string;
}

/** State collected from the optional follow-on steps. */
export interface IFollowOnState extends IAssignWorkFollowOnState {
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
  /**
   * Association selected in the optional AssociateToStep (step 1).
   * `null` when AssociateToStep is not configured or the user skipped it.
   */
  association: AssociationResult | null;
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
  /**
   * Optional configuration for an AssociateToStep prepended as step 1.
   *
   * When provided, the step sequence becomes:
   *   Step 1: Associate To  (optional, skip-able)
   *   Step 2: Add file(s)
   *   Step 3: Entity info
   *   Step 4: Next Steps
   *
   * The selected association is passed to `onFinish` via `context.association`.
   */
  associateToStep?: IAssociateToStepConfig;
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
  /** Search matter type reference records (for Assign Work step). */
  searchMatterTypes?: SearchCallback;
  /** Search practice area reference records (for Assign Work step). */
  searchPracticeAreas?: SearchCallback;

  /**
   * Optional callback to get initial (auto-fill) values for the Assign Work step.
   * Called when the assign-work follow-on card is first selected. Allows entity
   * wizards to seed Matter Type and Practice Area from the parent record form.
   *
   * @returns Partial IAssignWorkFollowOnState to merge as defaults. Only non-empty
   * fields are applied — the user can still override everything.
   */
  getAssignWorkDefaults?: () => Partial<IAssignWorkFollowOnState>;
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
    retrieveRecord(entityLogicalName: string, id: string, options?: string): Promise<Record<string, unknown>>;
    createRecord(entityLogicalName: string, data: Record<string, unknown>): Promise<{ id: string }>;
  };
  /** Entity-specific wizard configuration. */
  config: ICreateRecordWizardConfig;
  /** When true, renders without Dialog wrapper (Dataverse modal provides chrome). */
  embedded?: boolean;
}

// ---------------------------------------------------------------------------
// Search callback type (used by AssignResources and DraftSummary)
// ---------------------------------------------------------------------------

/** Standard search callback signature for lookup fields. */
export type SearchCallback = (query: string) => Promise<ILookupItem[]>;
