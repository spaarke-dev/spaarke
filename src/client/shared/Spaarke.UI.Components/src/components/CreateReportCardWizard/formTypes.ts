/**
 * formTypes.ts
 * Form state types for the Create New Report Card wizard.
 *
 * Field set is the owner-provided, Phase-0-validated manifest — see
 * `projects/visual-host-create-button-r1/notes/field-manifests/reportcard.md`
 * (sprk_reportcard is FULLY ADR-024 resolver-ready; zero schema gap for the
 * resolver itself).
 *
 * Enter Info manifest (owner decision 2026-07-08): sprk_name (REQUIRED),
 * sprk_narrative, sprk_duedate, plus 8 assigned-resource lookups collected AT
 * CREATION (all optional):
 *   sprk_assignedattorney1 / sprk_assignedattorney2   (contact)
 *   sprk_assignedparalegal1 / sprk_assignedparalegal2 (contact)
 *   sprk_assignedtolawfirm1 / sprk_assignedlawfirm2   (sprk_organization —
 *     NOTE the asymmetric naming: "assignedtolawfirm1" vs "assignedlawfirm2".
 *     Both are the REAL schema field names — do not "fix" them.)
 *   sprk_assignedtoexternal / sprk_assignedtointernal (contact)
 *
 * Out of scope for Enter Info (owner decision): sprk_acceptdate,
 * sprk_requestdate, sprk_submitdate (workflow-progression dates set later),
 * sprk_reportcardnumber (not set client-side — see reportCardService.ts).
 */

export interface ICreateReportCardFormState {
  /** Name — free text (required; sprk_name is NOT NULL on the schema). Maps to sprk_name. */
  name: string;
  /** Narrative — free text, multi-line (optional). Maps to sprk_narrative. */
  narrative: string;
  /** Due Date — ISO date string (optional). Maps to sprk_duedate. */
  dueDate: string;

  /** sprk_assignedattorney1 lookup (-> contact). */
  assignedAttorney1Id: string;
  assignedAttorney1Name: string;
  /** sprk_assignedattorney2 lookup (-> contact). */
  assignedAttorney2Id: string;
  assignedAttorney2Name: string;
  /** sprk_assignedparalegal1 lookup (-> contact). */
  assignedParalegal1Id: string;
  assignedParalegal1Name: string;
  /** sprk_assignedparalegal2 lookup (-> contact). */
  assignedParalegal2Id: string;
  assignedParalegal2Name: string;
  /** sprk_assignedtolawfirm1 lookup (-> sprk_organization). Note asymmetric naming vs. assignedLawFirm2. */
  assignedToLawFirm1Id: string;
  assignedToLawFirm1Name: string;
  /** sprk_assignedlawfirm2 lookup (-> sprk_organization). */
  assignedLawFirm2Id: string;
  assignedLawFirm2Name: string;
  /** sprk_assignedtoexternal lookup (-> contact). */
  assignedToExternalId: string;
  assignedToExternalName: string;
  /** sprk_assignedtointernal lookup (-> contact). */
  assignedToInternalId: string;
  assignedToInternalName: string;
}

export function buildEmptyReportCardForm(): ICreateReportCardFormState {
  return {
    name: '',
    narrative: '',
    dueDate: '',
    assignedAttorney1Id: '',
    assignedAttorney1Name: '',
    assignedAttorney2Id: '',
    assignedAttorney2Name: '',
    assignedParalegal1Id: '',
    assignedParalegal1Name: '',
    assignedParalegal2Id: '',
    assignedParalegal2Name: '',
    assignedToLawFirm1Id: '',
    assignedToLawFirm1Name: '',
    assignedLawFirm2Id: '',
    assignedLawFirm2Name: '',
    assignedToExternalId: '',
    assignedToExternalName: '',
    assignedToInternalId: '',
    assignedToInternalName: '',
  };
}

/** Snapshot constant — most callers should prefer {@link buildEmptyReportCardForm} for a fresh instance per open. */
export const EMPTY_REPORTCARD_FORM: ICreateReportCardFormState = buildEmptyReportCardForm();
