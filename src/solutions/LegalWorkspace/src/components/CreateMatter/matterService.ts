/**
 * matterService.ts
 * Matter creation flow orchestrator for the Create New Matter wizard.
 *
 * Responsibilities:
 *   1. Create sprk_matter Dataverse record via Xrm.WebApi
 *   2. Upload files to SPE via BFF SpeFileStore endpoint
 *   3. Execute selected follow-on actions (assign counsel, draft summary, send email)
 *
 * Partial failure handling:
 *   - Matter record creation failure → hard error (abort, return error result)
 *   - File upload failure → soft warning (matter created, return warning result)
 *   - Follow-on action failure → soft warning (matter created, log per-action error)
 *
 * All methods return ICreateMatterResult — never throw.
 */

import type { ICreateMatterFormState } from './formTypes';
import type { IUploadedFile } from './wizardTypes';
import type { IContact, ILookupItem } from '../../types/entities';
import type { IWebApi, WebApiEntity } from '../../types/xrm';
import { EntityCreationService } from '../../services/EntityCreationService';
import type { IUploadProgress } from '../../services/EntityCreationService';
import { getBffBaseUrl } from '../../config/bffConfig';
import { authenticatedFetch } from '../../services/bffAuthProvider';

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

export type CreateMatterResultStatus = 'success' | 'partial' | 'error';

export interface ICreateMatterResult {
  /** Overall status. */
  status: CreateMatterResultStatus;
  /** The GUID of the created sprk_matter record (present on success and partial). */
  matterId?: string;
  /** Display name of the created matter. */
  matterName?: string;
  /** Human-readable error message (set on error status). */
  errorMessage?: string;
  /** Non-fatal warnings (e.g. file upload failed after record was created). */
  warnings: string[];
}

// ---------------------------------------------------------------------------
// Follow-on action inputs
// ---------------------------------------------------------------------------

export interface IAssignCounselInput {
  /** Contact GUID to assign as lead counsel. */
  contactId: string;
  /** Display name (used for optimistic UI, not sent to server). */
  contactName: string;
}

export interface IDraftSummaryInput {
  /** Recipient email addresses for distribution. */
  recipientEmails: string[];
}

export interface ISendEmailInput {
  /** Email recipient address(es). */
  to: string;
  /** Email subject line. */
  subject: string;
  /** Email body. */
  body: string;
}

export interface IFollowOnActions {
  assignCounsel?: IAssignCounselInput;
  draftSummary?: IDraftSummaryInput;
  sendEmail?: ISendEmailInput;
}

// ---------------------------------------------------------------------------
// BFF endpoints — file upload uses existing SPE endpoint via SdapApiClient (Task 6),
// draft summary handled by front-end Xrm.WebApi email activity (no BFF needed).
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Dataverse entity helpers
// ---------------------------------------------------------------------------

/**
 * Build the Dataverse entity payload for a new sprk_matter record.
 * Maps ICreateMatterFormState fields to Dataverse attribute names.
 */
function buildMatterEntity(
  form: ICreateMatterFormState
): WebApiEntity {
  const entity: WebApiEntity = {
    sprk_name: form.matterName.trim(),
  };

  if (form.matterTypeId) {
    entity['sprk_mattertype_ref@odata.bind'] = `/sprk_mattertype_refs(${form.matterTypeId})`;
  }

  if (form.practiceAreaId) {
    entity['sprk_practicearea_ref@odata.bind'] = `/sprk_practicearea_refs(${form.practiceAreaId})`;
  }

  if (form.assignedAttorneyId) {
    entity['sprk_assignedattorney@odata.bind'] = `/contacts(${form.assignedAttorneyId})`;
  }

  if (form.assignedParalegalId) {
    entity['sprk_assignedparalegal@odata.bind'] = `/contacts(${form.assignedParalegalId})`;
  }

  if (form.summary && form.summary.trim() !== '') {
    entity['sprk_description'] = form.summary.trim();
  }

  return entity;
}

// ---------------------------------------------------------------------------
// MatterService class
// ---------------------------------------------------------------------------

export class MatterService {
  private readonly _entityService: EntityCreationService;

  constructor(
    private readonly _webApi: IWebApi,
    private readonly _containerId?: string
  ) {
    this._entityService = new EntityCreationService(_webApi);
  }

  /**
   * Full matter creation flow:
   *   1. Create sprk_matter record
   *   2. Upload files to SPE via BFF (using EntityCreationService)
   *   3. Create sprk_document records linking files to the matter
   *   4. Execute selected follow-on actions
   *
   * Returns ICreateMatterResult — never throws.
   */
  async createMatter(
    form: ICreateMatterFormState,
    uploadedFiles: IUploadedFile[],
    followOnActions: IFollowOnActions,
    onUploadProgress?: (progress: IUploadProgress) => void
  ): Promise<ICreateMatterResult> {
    const warnings: string[] = [];

    // ── Step 1: Create Dataverse record ───────────────────────────────────
    let matterId: string;

    try {
      const entity = buildMatterEntity(form);
      const result = await this._webApi.createRecord('sprk_matter', entity);
      matterId = result.id;
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        status: 'error',
        errorMessage: `Failed to create matter record: ${message}`,
        warnings: [],
      };
    }

    // ── Step 2: Upload files to SPE via BFF + create document records ─────
    if (uploadedFiles.length > 0 && this._containerId) {
      const uploadResult = await this._entityService.uploadFilesToSpe(
        this._containerId,
        uploadedFiles,
        onUploadProgress
      );

      if (!uploadResult.success) {
        warnings.push(
          `File upload failed (${uploadResult.failureCount} of ${uploadedFiles.length}). ` +
          'Files can be added from the matter record.'
        );
      } else if (uploadResult.uploadedFiles.length > 0) {
        // Create sprk_document records linking uploaded files to the matter
        const linkResult = await this._entityService.createDocumentRecords(
          'sprk_matters',
          matterId,
          'sprk_matter',
          uploadResult.uploadedFiles
        );
        if (linkResult.warnings.length > 0) {
          warnings.push(...linkResult.warnings);
        }
      }

      if (uploadResult.failureCount > 0 && uploadResult.successCount > 0) {
        warnings.push(
          `${uploadResult.failureCount} file(s) failed to upload: ` +
          uploadResult.errors.map((e) => e.fileName).join(', ')
        );
      }
    } else if (uploadedFiles.length > 0 && !this._containerId) {
      warnings.push('File upload skipped — no SPE container configured. Files can be added later.');
    }

    // ── Step 3: Follow-on actions ─────────────────────────────────────────
    if (followOnActions.assignCounsel) {
      const counselResult = await this._assignCounsel(
        matterId,
        followOnActions.assignCounsel
      );
      if (!counselResult.success) {
        warnings.push(counselResult.warning ?? 'Failed to assign counsel. Please assign manually.');
      }
    }

    if (followOnActions.draftSummary) {
      const summaryResult = await this._distributeSummary(
        matterId,
        form.matterName,
        followOnActions.draftSummary
      );
      if (!summaryResult.success) {
        warnings.push(summaryResult.warning ?? 'Failed to distribute summary email. Please send manually.');
      }
    }

    if (followOnActions.sendEmail) {
      const emailResult = await this._createEmailActivity(
        matterId,
        followOnActions.sendEmail
      );
      if (!emailResult.success) {
        warnings.push(emailResult.warning ?? 'Failed to create email activity. Please send manually.');
      }
    }

    return {
      status: warnings.length > 0 ? 'partial' : 'success',
      matterId,
      matterName: form.matterName.trim(),
      warnings,
    };
  }

  // ── Private helpers ────────────────────────────────────────────────────────

  private async _assignCounsel(
    matterId: string,
    input: IAssignCounselInput
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      const updatePayload: WebApiEntity = {
        'sprk_leadattorney@odata.bind': `/contacts(${input.contactId})`,
      };
      await this._webApi.updateRecord('sprk_matter', matterId, updatePayload);
      return { success: true };
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        success: false,
        warning: `Could not assign counsel (${message}). Please assign from the matter record.`,
      };
    }
  }

  private async _distributeSummary(
    matterId: string,
    matterName: string,
    input: IDraftSummaryInput
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      if (input.recipientEmails.length === 0) {
        return { success: true };
      }

      // Create a draft email activity in Dataverse linked to the matter.
      // No BFF endpoint needed — handled directly via Xrm.WebApi.
      const emailEntity: WebApiEntity = {
        subject: `Matter Summary: ${matterName}`,
        description: `Summary distribution for matter "${matterName}" to: ${input.recipientEmails.join(', ')}`,
        'regardingobjectid_sprk_matter@odata.bind': `/sprk_matters(${matterId})`,
      };

      await this._webApi.createRecord('email', emailEntity);
      return { success: true };
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        success: false,
        warning: `Could not create summary email (${message}). Please distribute manually.`,
      };
    }
  }

  private async _createEmailActivity(
    matterId: string,
    input: ISendEmailInput
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      const emailEntity: WebApiEntity = {
        subject: input.subject,
        description: input.body,
        'regardingobjectid_sprk_matter@odata.bind': `/sprk_matters(${matterId})`,
      };

      await this._webApi.createRecord('email', emailEntity);
      return { success: true };
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        success: false,
        warning: `Could not create email activity (${message}). Please send manually.`,
      };
    }
  }
}

// ---------------------------------------------------------------------------
// Contact search helper (for AssignCounselStep and lookup fields)
// ---------------------------------------------------------------------------

/**
 * Search sprk_contact records by name fragment.
 * Returns up to 10 matching contacts.
 * Throws on error — callers should handle gracefully.
 */
export async function searchContacts(
  webApi: IWebApi,
  nameFilter: string
): Promise<IContact[]> {
  if (!nameFilter || nameFilter.trim().length < 2) {
    return [];
  }

  const encodedFilter = encodeURIComponent(nameFilter.trim());
  const query =
    `?$select=sprk_contactid,sprk_name,sprk_email` +
    `&$filter=contains(sprk_name,'${encodedFilter}')` +
    `&$orderby=sprk_name asc` +
    `&$top=10`;

  const result = await webApi.retrieveMultipleRecords('sprk_contact', query, 10);
  return result.entities as unknown as IContact[];
}

/**
 * Search contacts and return as ILookupItem[] (for LookupField compatibility).
 */
export async function searchContactsAsLookup(
  webApi: IWebApi,
  nameFilter: string
): Promise<ILookupItem[]> {
  const contacts = await searchContacts(webApi, nameFilter);
  return contacts.map((c) => ({
    id: c.sprk_contactid,
    name: c.sprk_name + (c.sprk_email ? ` (${c.sprk_email})` : ''),
  }));
}

// ---------------------------------------------------------------------------
// Matter Type search helper
// ---------------------------------------------------------------------------

/**
 * Search sprk_mattertype_ref records by name fragment.
 * Returns up to 10 matching matter types as ILookupItem.
 */
export async function searchMatterTypes(
  webApi: IWebApi,
  nameFilter: string
): Promise<ILookupItem[]> {
  if (!nameFilter || nameFilter.trim().length < 1) {
    return [];
  }

  const encodedFilter = encodeURIComponent(nameFilter.trim());
  const query =
    `?$select=sprk_mattertype_refid,sprk_name` +
    `&$filter=contains(sprk_name,'${encodedFilter}')` +
    `&$orderby=sprk_name asc` +
    `&$top=10`;

  const result = await webApi.retrieveMultipleRecords('sprk_mattertype_ref', query, 10);
  return result.entities.map((e) => ({
    id: e['sprk_mattertype_refid'] as string,
    name: e['sprk_name'] as string,
  }));
}

// ---------------------------------------------------------------------------
// Practice Area search helper
// ---------------------------------------------------------------------------

/**
 * Search sprk_practicearea_ref records by name fragment.
 * Returns up to 10 matching practice areas as ILookupItem.
 */
export async function searchPracticeAreas(
  webApi: IWebApi,
  nameFilter: string
): Promise<ILookupItem[]> {
  if (!nameFilter || nameFilter.trim().length < 1) {
    return [];
  }

  const encodedFilter = encodeURIComponent(nameFilter.trim());
  const query =
    `?$select=sprk_practicearea_refid,sprk_name` +
    `&$filter=contains(sprk_name,'${encodedFilter}')` +
    `&$orderby=sprk_name asc` +
    `&$top=10`;

  const result = await webApi.retrieveMultipleRecords('sprk_practicearea_ref', query, 10);
  return result.entities.map((e) => ({
    id: e['sprk_practicearea_refid'] as string,
    name: e['sprk_name'] as string,
  }));
}

// ---------------------------------------------------------------------------
// AI draft summary helper (for DraftSummaryStep)
// ---------------------------------------------------------------------------

export interface IAiDraftSummaryResponse {
  /** The generated summary text. */
  summary: string;
}

/**
 * Calls the BFF AI endpoint to generate a draft matter summary.
 * Uses authenticated fetch with Bearer token for BFF API.
 * Returns a fallback response if the endpoint is unavailable (graceful degradation).
 */
export async function fetchAiDraftSummary(
  matterName: string,
  matterType: string,
  practiceArea: string
): Promise<IAiDraftSummaryResponse> {
  try {
    const bffBaseUrl = getBffBaseUrl();
    const response = await authenticatedFetch(
      `${bffBaseUrl}/workspace/matters/ai-summary`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ matterName, matterType, practiceArea }),
      }
    );

    if (!response.ok) {
      return buildFallbackSummary(matterName, matterType, practiceArea);
    }

    const data: IAiDraftSummaryResponse = await response.json();
    return data;
  } catch {
    return buildFallbackSummary(matterName, matterType, practiceArea);
  }
}

function buildFallbackSummary(
  matterName: string,
  matterType: string,
  practiceArea: string
): IAiDraftSummaryResponse {
  const type = matterType || 'general';
  const area = practiceArea || 'legal services';
  return {
    summary:
      `This ${type.toLowerCase()} matter, "${matterName}", has been created in the ${area} practice area. ` +
      `Key stakeholders have been identified and initial documentation has been uploaded for review. ` +
      `Next steps include counsel assignment and matter planning. ` +
      `Please review and update this summary with specific matter objectives and timeline.`,
  };
}
