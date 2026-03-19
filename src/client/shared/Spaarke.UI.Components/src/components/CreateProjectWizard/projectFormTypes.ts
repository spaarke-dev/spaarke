/**
 * projectFormTypes.ts
 * Form state types for Create New Project wizard.
 *
 * Mirrors the pattern from CreateMatter/formTypes.ts, adapted for
 * the sprk_project entity fields.
 */

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

/** Mutable form field values for the Create Project wizard. */
export interface ICreateProjectFormState {
  /** sprk_projecttype_ref lookup — GUID of the selected record. */
  projectTypeId: string;
  /** Display name of the selected project type. */
  projectTypeName: string;
  /** sprk_practicearea_ref lookup — GUID of the selected record. */
  practiceAreaId: string;
  /** Display name of the selected practice area. */
  practiceAreaName: string;
  /** Project Name — free text (required). Maps to sprk_name. */
  projectName: string;
  /** Assigned Attorney — contact GUID. */
  assignedAttorneyId: string;
  /** Display name of the assigned attorney. */
  assignedAttorneyName: string;
  /** Assigned Paralegal — contact GUID. */
  assignedParalegalId: string;
  /** Display name of the assigned paralegal. */
  assignedParalegalName: string;
  /** Assigned Outside Counsel — sprk_organization GUID. */
  assignedOutsideCounselId: string;
  /** Display name of the assigned outside counsel organization. */
  assignedOutsideCounselName: string;
  /** Description — free text, multi-line (optional). Maps to sprk_projectdescription. */
  description: string;
  /**
   * Secure Project flag — maps to sprk_issecure on the sprk_project record.
   *
   * When true the project will have a dedicated SharePoint Embedded container,
   * a Dataverse Business Unit, and an external access portal provisioned.
   * This designation is IRREVERSIBLE after project creation.
   */
  isSecure: boolean;
}

/** Empty default for initializing useReducer or useState. */
export const EMPTY_PROJECT_FORM: ICreateProjectFormState = {
  projectTypeId: '',
  projectTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  projectName: '',
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedOutsideCounselId: '',
  assignedOutsideCounselName: '',
  description: '',
  isSecure: false,
};
