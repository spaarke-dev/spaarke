/** Dataverse sprk_matter entity */
export interface IMatter {
  sprk_matterid: string;
  sprk_name: string;
  sprk_type?: string;
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
  sprk_status?: number;  // Option set
  createdon: string;  // ISO date
  modifiedon: string;  // ISO date
}

/** Dataverse sprk_event entity */
export interface IEvent {
  sprk_eventid: string;
  sprk_subject: string;
  sprk_type?: string;
  sprk_priority?: number;
  sprk_priorityscore?: number;  // 0-100
  sprk_effortscore?: number;  // 0-100
  sprk_estimatedminutes?: number;
  sprk_priorityreason?: string;
  sprk_effortreason?: string;
  sprk_todoflag: boolean;
  sprk_todostatus?: number;  // Option set: Open, Completed, Dismissed
  sprk_todosource?: string;  // System, User, Flagged
  _sprk_regarding_value?: string;  // Lookup GUID (matter or project)
  sprk_duedate?: string;  // ISO date
  createdon: string;
  modifiedon: string;
}

/** Dataverse sprk_project entity */
export interface IProject {
  sprk_projectid: string;
  sprk_name: string;
  sprk_type?: string;
  sprk_practicearea?: string;
  _sprk_owner_value?: string;
  sprk_status?: number;
  sprk_budgetused?: number;
  createdon: string;
  modifiedon: string;
}

/** Dataverse sprk_document entity */
export interface IDocument {
  sprk_documentid: string;
  sprk_name: string;
  sprk_type?: string;
  sprk_description?: string;
  _sprk_matter_value?: string;  // Lookup GUID
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
