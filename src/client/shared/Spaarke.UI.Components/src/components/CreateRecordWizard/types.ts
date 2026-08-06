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
import type { INavigationService, IDataService } from '../../types/serviceInterfaces';
import type { FollowOnCardConfig } from '../WizardFollowOns';

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
  /**
   * Optional pre-filled association applied when the wizard opens (smart-todo-decoupling-r3
   * task 032 / FR-16 — launch-context pre-fill). When supplied, the AssociateToStep starts
   * with this record pre-selected; the user may still change it or clear it before advancing.
   *
   * Three canonical launch contexts (per spec FR-16):
   *   1. Kanban "Add To Do"                                  → undefined (no pre-fill)
   *   2. Parent-form ribbon (Matter / Project / Event / …)   → pre-filled to the launch record
   *   3. Outlook add-in "Create To Do"                       → pre-filled to sprk_regardingcommunication
   *
   * See `projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md` for the full
   * contract.
   */
  initialAssociation?: AssociationResult;

  /**
   * When `true`, the Associate-To step is hidden and the `initialAssociation`
   * is treated as a fixed parent that flows straight through to `onFinish`
   * (via `context.association`) without ever showing the picker
   * (visual-host-create-button-r1 task 014 / design §5.5).
   *
   * Used when the parent record is unambiguous — e.g. a wizard launched from a
   * Visual Host visual, where the host record is always the parent. The step is
   * removed from the wizard sequence entirely (not merely disabled), so the
   * user cannot re-target or clear the association.
   *
   * Requires `initialAssociation` to be meaningful; when `lockAssociation` is
   * `true` but no `initialAssociation` is supplied, the wizard simply proceeds
   * with no association (equivalent to the user having skipped the step).
   *
   * Defaults to `false` — the step remains visible and skip-able exactly as
   * before for every other launch context.
   */
  lockAssociation?: boolean;
}

// ---------------------------------------------------------------------------
// Follow-on action identifiers
// ---------------------------------------------------------------------------

/**
 * Identifiers for the optional follow-on action cards shown in "Next Steps".
 *
 * Widened (visual-host-create-button-r1 task 030) to accept any string via
 * the `(string & {})` idiom — the three named literals remain for
 * autocomplete/back-compat with the built-in card set, but a wizard supplying
 * a `config.followOnCards` override (e.g. `'assign-work' | 'add-todo'` from
 * the shared `WizardFollowOns` canonical vocabulary) is not rejected by the
 * type system. Purely a type-level relaxation — `CreateRecordWizard.tsx`
 * already passed arbitrary card ids straight through via `as FollowOnActionId[]`
 * before this change; this just removes the lie.
 */
export type FollowOnActionId = 'assign-counsel' | 'create-event' | 'send-email' | (string & {});

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

// ---------------------------------------------------------------------------
// Create Event follow-on state
// ---------------------------------------------------------------------------

/**
 * State collected from the "Create Event" follow-on step.
 * These field values are passed to onFinish so the entity wizard can create
 * the sprk_event record after the parent matter/project has been created.
 */
export interface ICreateEventFollowOnState {
  /** Event name (required). Maps to sprk_eventname. */
  createEventName: string;
  /** Event type lookup GUID (optional). */
  createEventTypeId: string;
  /** Event type display name. */
  createEventTypeName: string;
  /** Due date as ISO date string (optional). Maps to sprk_duedate. */
  createEventDueDate: string;
  /**
   * Priority option set value.
   * 100000000=Low, 100000001=Normal (default), 100000002=High, 100000003=Urgent
   */
  createEventPriority: number;
  /** Description free text (optional). Maps to sprk_description. */
  createEventDescription: string;
}

/** State collected from the optional follow-on steps. */
export interface IFollowOnState extends IAssignWorkFollowOnState, ICreateEventFollowOnState {
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
  /**
   * Record picked via `config.existingRecordPicker`'s "Select Existing"
   * button in the Add file(s) step. `null` when `existingRecordPicker` is
   * not configured, or the user uploaded files instead of picking a record.
   */
  selectedExistingRecord: AssociationResult | null;
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
   * Files to PRE-SEED into the "Add file(s)" step (assistant-enhancements-r1 UAT
   * W-2/W-5 — the create-flow file leg). The launching surface (the Assistant
   * create-flow) fetches the drafted-from session document(s) and passes them here
   * so they ride the wizard's EXISTING upload+link+index pipeline into the new
   * record's container. Seeded via an effect keyed on this array (it may arrive
   * ASYNC after open); the reducer dedups by name+size and a user removal is not
   * undone. Omitted by every wizard except the Assistant-launched Matter flow —
   * fully backward compatible.
   */
  initialFiles?: IUploadedFile[];
  /**
   * When `true`, the "Add file(s)" step is omitted entirely from the wizard's
   * step sequence — not merely skippable, but never shown.
   *
   * Added by visual-host-create-button-r1 task 040 for `CreateReportCardWizard`
   * (owner decision: `sprk_reportcard` has a `sprk_containerid` column but the
   * wizard doesn't populate it or offer document upload — no files step at
   * all). Every other consumer omits this field, preserving the exact
   * pre-existing step sequence (`[Associate To?] → Add file(s) → Entity info
   * → Next Steps`) — fully backward compatible, opt-in only.
   *
   * Defaults to `false`/undefined (Add file(s) step present, as before).
   */
  hideFilesStep?: boolean;
  /**
   * When `true`, the "Add file(s)" step is REQUIRED: the Skip button is hidden and
   * Next stays disabled until the user uploads at least one file OR picks an existing
   * record (via `existingRecordPicker`). Use for wizards whose `onFinish` cannot
   * proceed without a document (e.g. the Analysis wizard — the analysis IS a document
   * review). Defaults to `false`/undefined — the step stays skip-able + Next always
   * enabled (the grounding-optional default for every other wizard, FR-A5). Mutually
   * sensible with `hideFilesStep` OFF; ignored when `hideFilesStep` is `true`.
   */
  requireFilesStep?: boolean;
  /**
   * Optional "select an existing record" affordance rendered inside the
   * built-in "Add file(s)" step, alongside the upload dropzone
   * (ai-advanced-capabilities-analysis-hub-r1 task 040 — the Analysis
   * wizard's Step 1 "upload OR select existing Document"). When provided, a
   * "Select Existing" button opens `navigationService.openLookup` scoped to
   * `entityType`; the picked record is exposed to `onFinish` via
   * `IFinishContext.selectedExistingRecord`.
   *
   * Selecting an existing record and uploading new files are mutually
   * exclusive in the UI — picking one clears the other, so `onFinish` only
   * ever needs to branch on whichever is populated.
   *
   * Omitted by every other consumer — fully backward compatible, opt-in only.
   */
  existingRecordPicker?: {
    /** Navigation service used to open the Dataverse lookup side pane. */
    navigationService: INavigationService;
    /** Logical name of the entity to search (e.g. `"sprk_document"`). */
    entityType: string;
    /** Human-readable label used in button/hint text (e.g. `"Document"`). */
    entityLabel: string;
    /** Optional saved view id to scope the lookup dialog. */
    defaultViewId?: string;
  };
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
  // ── Create Event follow-on configuration ─────────────────────────────────

  /**
   * Data service passed to the CreateEventFollowOnStep for event type lookups.
   * Required when the 'create-event' follow-on card is used.
   * Actual event record creation occurs inside onFinish after the parent
   * record (matter/project) has been created and its GUID is available.
   */
  eventDataService?: IDataService;
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
  /**
   * Optional "Assign to me" resolver for the Assign Work follow-on step's
   * Assigned Attorney field (spaarkeai-assistant-enhancements-r1 task 014 /
   * FR-A4). When supplied, `AssignWorkFollowOnStep` renders an "Assign to me"
   * button next to Assigned Attorney that calls this resolver and applies the
   * result via `onAttorneyChange`. The entity wizard supplies this using its
   * own `IDataService` (e.g. `() => resolveCurrentUserAsContactAssignee(dataService)`
   * from `services/userLookup.ts`) — `CreateRecordWizard` never resolves
   * identity itself. Omitting this field hides the button entirely
   * (fully backward compatible, opt-in only).
   *
   * @returns The current user resolved onto the target assignee entity
   * (`ILookupItem`), or `null` when unresolvable — the caller degrades
   * gracefully (field stays empty for manual search).
   */
  resolveCurrentUserAssignee?: () => Promise<ILookupItem | null>;

  /**
   * Optional override for the built-in "Next Steps" card set (default:
   * Assign Work / Create Event / Send Email — see `CreateRecordWizard.tsx`'s
   * internal `followOnCards`). When supplied, `CreateRecordWizard` renders
   * THIS array instead of its default 3 cards.
   *
   * Introduced by visual-host-create-button-r1 task 030: `CreateInvoiceWizard`
   * (and `CreateKPIAssessmentWizard`) offer **Send Email / Add To Do / Assign
   * Work** per design.md §5.9 — a different card set than the legacy default
   * (which has Create Event instead of Add To Do). Rather than fork
   * `CreateRecordWizard` or hand-roll a new wizard directly on `WizardShell`
   * (design.md §6.3 — rejected as the default), the wizard supplies its own
   * `FollowOnCardConfig[]` and owns 100% of the follow-on state itself
   * (mirroring the established `WorkAssignmentWizardDialog`/
   * `SummarizeFilesDialog` pattern of closures over local refs read inside
   * `renderStep`). Consequently `IFinishContext.followOn` (populated from
   * `CreateRecordWizard`'s OWN internal assign-work/create-event/email
   * state) is NOT meaningful when this override is supplied — the wizard's
   * `onFinish` must read its follow-on field values from its own local state
   * instead. `IFinishContext.selectedActions` (via `FollowOnActionId`,
   * widened above) still faithfully reports whichever card ids were
   * selected.
   *
   * Omitting this field preserves the exact pre-existing behavior for every
   * other consumer (`CreateEventWizard`, `CreateMatterWizard`,
   * `CreateProjectWizard`) — fully backward compatible, opt-in only.
   */
  followOnCards?: FollowOnCardConfig[];
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
  /**
   * Optional modal max-width, forwarded to `WizardShell` (non-embedded mode only).
   * Defaults to WizardShell's `95vw`. Set a fixed px value (e.g. `'640px'`) so an
   * in-app modal (`embedded={false}`) matches the Dataverse Create-wizard modal size
   * instead of filling the viewport. Ignored in embedded mode.
   */
  maxWidth?: string;
  /**
   * Optional modal height, forwarded to `WizardShell` (non-embedded mode only).
   * Defaults to WizardShell's `70vh`. Ignored in embedded mode.
   */
  height?: string;
}

// ---------------------------------------------------------------------------
// Search callback type (used by AssignResources and DraftSummary)
// ---------------------------------------------------------------------------

/** Standard search callback signature for lookup fields. */
export type SearchCallback = (query: string) => Promise<ILookupItem[]>;
