/** Dataverse sprk_matter entity */
export interface IMatter {
  sprk_matterid: string;
  sprk_name: string;
  sprk_mattername?: string;
  sprk_matternumber?: string;
  sprk_matterdescription?: string;
  /** Display name from sprk_mattertype_ref lookup (populated via formatted value mapping). */
  matterTypeName?: string;
  /** Display name from sprk_practicearea_ref lookup (populated via formatted value mapping). */
  practiceAreaName?: string;
  sprk_practicearea?: string;
  sprk_totalbudget: number;
  sprk_totalspend: number;
  sprk_utilizationpercent: number;
  sprk_budgetcontrols_grade?: string;  // A-F
  sprk_guidelinescompliance_grade?: string;  // A-F
  sprk_outcomessuccess_grade?: string;  // A-F
  sprk_overdueeventcount: number;
  _sprk_organization_value?: string;  // Lookup GUID
  _sprk_leadattorney_value?: string;  // Lookup GUID
  _modifiedby_value?: string;  // Lookup GUID
  _sprk_assignedattorney1_value?: string;  // Lookup GUID (contact)
  _sprk_assignedparalegal1_value?: string;  // Lookup GUID (contact)
  sprk_status?: number;  // Option set
  statuscode?: number;  // Standard Dataverse statuscode
  /** Display name from statuscode (populated via formatted value mapping). */
  statuscodeName?: string;
  createdon: string;  // ISO date
  modifiedon: string;  // ISO date
}

/**
 * Dataverse sprk_event entity — calendar-only after R3.
 *
 * Per R3 FR-29 / OS-1, the four legacy event-todo fields (`sprk_todoflag`,
 * `sprk_todostatus`, `sprk_todocolumn`, `sprk_todopinned`) are removed from
 * `sprk_event` in Phase 1. LegalWorkspace's SmartToDo widget no longer
 * references this interface for todo state; it remains here for the
 * ActivityFeed (Updates Feed) which renders sprk_event records.
 */
export interface IEvent {
  sprk_eventid: string;
  sprk_eventname: string;
  /** Display name from sprk_eventtype_ref lookup (populated via formatted value mapping). */
  eventTypeName?: string;
  sprk_description?: string;  // Multiline Text, 2000 chars
  sprk_priority?: number;  // Choice: 0=Low, 1=Normal, 2=High, 3=Urgent
  sprk_priorityscore?: number;  // 0-100
  sprk_effortscore?: number;  // 0-100
  sprk_estimatedminutes?: number;
  sprk_priorityreason?: string;
  sprk_effortreason?: string;
  sprk_regardingrecordid?: string;   // Text field: GUID of associated matter/project
  sprk_regardingrecordname?: string; // Text field: display name of associated matter/project
  /** Display name from sprk_regardingrecordtype lookup (populated via formatted value mapping). */
  regardingRecordTypeName?: string;
  /** Display name from sprk_assignedto contact lookup (populated via formatted value mapping). */
  assignedToName?: string;
  sprk_duedate?: string;  // ISO date
  createdon: string;
  modifiedon: string;
}

/**
 * Dataverse sprk_todo entity — first-class To Do entity (R3 FR-09, FR-11).
 *
 * Replaces the legacy `IEvent`-with-`sprk_todoflag` shape from R1/R2. Mirrors
 * `src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md` and the
 * canonical `ITodo` in `src/solutions/SmartTodo/src/types/entities.ts`.
 *
 * statuscode semantics (per task 009):
 *   - 1          = Open       (statecode 0 / Active)
 *   - 659490001  = In Progress(statecode 0 / Active)
 *   - 2          = Completed  (statecode 1 / Inactive)
 *   - 659490002  = Dismissed  (statecode 1 / Inactive)
 *
 * sprk_todocolumn (per entity schema):
 *   - 100000000  = Today
 *   - 100000001  = Tomorrow
 *   - 100000002  = Future
 */
export interface ITodo {
  sprk_todoid: string;
  sprk_name: string;
  sprk_description?: string;
  sprk_notes?: string;
  /** 0-100 native priority score on sprk_todo. */
  sprk_priorityscore?: number;
  /** 0-100 native effort score on sprk_todo. */
  sprk_effortscore?: number;
  sprk_duedate?: string;
  sprk_completedon?: string;
  /** Choice: 100000000=Today, 100000001=Tomorrow, 100000002=Future. */
  sprk_todocolumn?: number;
  /** Lock item in assigned Kanban column. */
  sprk_todopinned?: boolean;
  /** 0=Active, 1=Inactive. */
  statecode?: number;
  /** 1=Open, 659490001=In Progress, 2=Completed, 659490002=Dismissed. */
  statuscode?: number;
  /** Display name from statuscode (populated via formatted value mapping). */
  statuscodeName?: string;
  /** Display name from sprk_assignedto systemuser lookup (populated via formatted value mapping). */
  assignedToName?: string;
  _sprk_assignedto_value?: string;
  _ownerid_value?: string;
  createdon: string;
  modifiedon: string;
}

/** Dataverse sprk_project entity */
export interface IProject {
  sprk_projectid: string;
  sprk_name: string;
  sprk_projectnumber?: string;
  sprk_projectname?: string;
  sprk_projectdescription?: string;
  /** Display name from sprk_projecttype_ref lookup (populated via formatted value mapping). */
  projectTypeName?: string;
  sprk_practicearea?: string;
  _sprk_owner_value?: string;
  _modifiedby_value?: string;  // Lookup GUID
  _sprk_assignedattorney1_value?: string;  // Lookup GUID (contact)
  _sprk_assignedparalegal1_value?: string;  // Lookup GUID (contact)
  sprk_status?: number;
  statuscode?: number;  // Standard Dataverse statuscode
  /** Display name from statuscode (populated via formatted value mapping). */
  statuscodeName?: string;
  sprk_budgetused?: number;
  createdon: string;
  modifiedon: string;
}

/** Dataverse sprk_document entity */
export interface IDocument {
  sprk_documentid: string;
  sprk_documentname: string;
  sprk_documentdescription?: string;
  /** Display name from sprk_documenttype choice field (populated via formatted value mapping). */
  documentTypeName?: string;
  sprk_description?: string;
  sprk_filetype?: string;           // Text: "pdf", "docx", "xlsx", etc.
  sprk_workspaceflag?: boolean;
  sprk_filesummary?: string;        // AI-generated summary
  sprk_filetldr?: string;           // AI-generated TL;DR
  _sprk_checkedoutby_value?: string; // Contact lookup GUID
  /** Display name from sprk_checkedoutby contact lookup (populated via formatted value mapping). */
  checkedOutByName?: string;
  statuscode?: number;              // 1=Draft, 421500001=Checked Out, 421500002=Check In, 2=Locked
  /** Display name from statuscode (populated via formatted value mapping). */
  statuscodeName?: string;
  _ownerid_value?: string;          // Lookup GUID
  _createdby_value?: string;        // Lookup GUID
  _modifiedby_value?: string;       // Lookup GUID
  _sprk_matter_value?: string;      // Lookup GUID
  createdon?: string;               // ISO date
  modifiedon: string;
}

/** Dataverse sprk_invoice entity */
export interface IInvoice {
  sprk_invoiceid: string;
  sprk_name: string;
  sprk_invoicenumber?: string;
  sprk_invoicedate?: string;  // ISO date
  sprk_vendororg?: string;
  sprk_description?: string;
  statuscode?: number;  // Standard Dataverse statuscode
  /** Display name from statuscode (populated via formatted value mapping). */
  statuscodeName?: string;
  _ownerid_value?: string;  // Lookup GUID
  _modifiedby_value?: string;  // Lookup GUID
  _sprk_assignedattorney1_value?: string;  // Lookup GUID (contact)
  _sprk_assignedparalegal1_value?: string;  // Lookup GUID (contact)
  createdon: string;
  modifiedon: string;
}

/** Dataverse sprk_organization entity */
export interface IOrganization {
  sprk_organizationid: string;
  sprk_name: string;
}

/** Dataverse sprk_contact entity */
export interface IContact {
  sprk_contactid: string;
  sprk_name: string;
  sprk_email?: string;
}

/** Dataverse sprk_userpreference entity for storing user-specific settings. */
export interface IUserPreference {
  sprk_userpreferenceid: string;
  sprk_preferencetype: number;   // Choice field discriminator
  sprk_preferencevalue: string;  // JSON text
  _sprk_user_value?: string;     // Lookup to systemuser
  createdon: string;
  modifiedon: string;
}

/** Generic lookup item for Dataverse reference table searches. */
export interface ILookupItem {
  id: string;
  name: string;
}
