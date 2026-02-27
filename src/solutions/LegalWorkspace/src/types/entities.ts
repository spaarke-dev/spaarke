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
  _sprk_assignedattorney_value?: string;  // Lookup GUID (contact)
  _sprk_assignedparalegal_value?: string;  // Lookup GUID (contact)
  sprk_status?: number;  // Option set
  statuscode?: number;  // Standard Dataverse statuscode
  /** Display name from statuscode (populated via formatted value mapping). */
  statuscodeName?: string;
  createdon: string;  // ISO date
  modifiedon: string;  // ISO date
}

/** Dataverse sprk_event entity */
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
  sprk_todoflag: boolean;
  sprk_todostatus?: number;  // Choice: 100000000=Open, 100000001=Completed, 100000002=Dismissed
  sprk_todosource?: number;  // Choice: 100000000=System, 100000001=User, 100000002=AI
  sprk_regardingrecordid?: string;   // Text field: GUID of associated matter/project
  sprk_regardingrecordname?: string; // Text field: display name of associated matter/project
  /** Display name from sprk_regardingrecordtype lookup (populated via formatted value mapping). */
  regardingRecordTypeName?: string;
  /** Display name from sprk_assignedto contact lookup (populated via formatted value mapping). */
  assignedToName?: string;
  sprk_todocolumn?: number;  // Choice: 0=Today, 1=Tomorrow, 2=Future
  sprk_todopinned?: boolean; // Lock item in assigned Kanban column
  sprk_duedate?: string;  // ISO date
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
  _sprk_assignedattorney_value?: string;  // Lookup GUID (contact)
  _sprk_assignedparalegal_value?: string;  // Lookup GUID (contact)
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
  sprk_name: string;
  /** Display name from sprk_documenttype choice field (populated via formatted value mapping). */
  documentTypeName?: string;
  sprk_description?: string;
  _sprk_matter_value?: string;  // Lookup GUID
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
  _sprk_assignedattorney_value?: string;  // Lookup GUID (contact)
  _sprk_assignedparalegal_value?: string;  // Lookup GUID (contact)
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
