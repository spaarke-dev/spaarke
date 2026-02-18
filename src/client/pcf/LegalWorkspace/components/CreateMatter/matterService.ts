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
import type { IContact } from '../../types/entities';

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
// BFF endpoints
// ---------------------------------------------------------------------------

const BFF_FILE_UPLOAD_ENDPOINT = '/api/workspace/matters/files';
const BFF_DRAFT_SUMMARY_ENDPOINT = '/api/workspace/matters/draft-summary';

// ---------------------------------------------------------------------------
// Dataverse entity helpers
// ---------------------------------------------------------------------------

/**
 * Build the Dataverse entity payload for a new sprk_matter record.
 * Maps ICreateMatterFormState fields to Dataverse attribute names.
 */
function buildMatterEntity(
  form: ICreateMatterFormState
): ComponentFramework.WebApi.Entity {
  const entity: ComponentFramework.WebApi.Entity = {
    sprk_name: form.matterName.trim(),
  };

  if (form.matterType) {
    entity['sprk_type'] = form.matterType;
  }

  if (form.practiceArea) {
    entity['sprk_practicearea'] = form.practiceArea;
  }

  if (form.estimatedBudget && form.estimatedBudget !== '') {
    const budget = parseFloat(form.estimatedBudget);
    if (!isNaN(budget)) {
      entity['sprk_totalbudget'] = budget;
    }
  }

  if (form.summary && form.summary.trim() !== '') {
    entity['sprk_description'] = form.summary.trim();
  }

  // Note: organization and keyParties do not map to simple text fields —
  // organization is a lookup; keyParties is stored in description/notes.
  // For this wizard we store keyParties as part of the description.
  if (form.keyParties && form.keyParties.trim() !== '') {
    const existingDesc = (entity['sprk_description'] as string) ?? '';
    const keyPartiesSection = `\n\nKey Parties:\n${form.keyParties.trim()}`;
    entity['sprk_description'] = existingDesc + keyPartiesSection;
  }

  return entity;
}

// ---------------------------------------------------------------------------
// MatterService class
// ---------------------------------------------------------------------------

export class MatterService {
  constructor(private readonly _webApi: ComponentFramework.WebApi) {}

  /**
   * Full matter creation flow:
   *   1. Create sprk_matter record
   *   2. Upload files to SPE via BFF
   *   3. Execute selected follow-on actions
   *
   * Returns ICreateMatterResult — never throws.
   */
  async createMatter(
    form: ICreateMatterFormState,
    uploadedFiles: IUploadedFile[],
    followOnActions: IFollowOnActions
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

    // ── Step 2: Upload files to SPE via BFF ───────────────────────────────
    if (uploadedFiles.length > 0) {
      const uploadResult = await this._uploadFiles(matterId, uploadedFiles);
      if (!uploadResult.success) {
        warnings.push(uploadResult.warning ?? 'File upload failed. Files can be added later.');
      }
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

  private async _uploadFiles(
    matterId: string,
    files: IUploadedFile[]
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      const formData = new FormData();
      formData.append('matterId', matterId);
      files.forEach((f) => formData.append('files', f.file, f.name));

      const response = await fetch(BFF_FILE_UPLOAD_ENDPOINT, {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        return {
          success: false,
          warning: `File upload failed (HTTP ${response.status}). Files can be added from the matter record.`,
        };
      }

      return { success: true };
    } catch {
      return {
        success: false,
        warning: 'File upload could not complete. Files can be added from the matter record.',
      };
    }
  }

  private async _assignCounsel(
    matterId: string,
    input: IAssignCounselInput
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      const updatePayload: ComponentFramework.WebApi.Entity = {
        'sprk_leadattorney@odata.bind': `/sprk_contacts(${input.contactId})`,
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

      const response = await fetch(BFF_DRAFT_SUMMARY_ENDPOINT, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          matterId,
          matterName,
          recipientEmails: input.recipientEmails,
        }),
      });

      if (!response.ok) {
        return {
          success: false,
          warning: `Summary distribution failed (HTTP ${response.status}). Please send manually.`,
        };
      }

      return { success: true };
    } catch {
      return {
        success: false,
        warning: 'Could not send summary email. Please distribute manually.',
      };
    }
  }

  private async _createEmailActivity(
    matterId: string,
    input: ISendEmailInput
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      const emailEntity: ComponentFramework.WebApi.Entity = {
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
// Contact search helper (for AssignCounselStep)
// ---------------------------------------------------------------------------

/**
 * Search sprk_contact records by name fragment.
 * Returns up to 10 matching contacts.
 * Throws on error — callers should handle gracefully.
 */
export async function searchContacts(
  webApi: ComponentFramework.WebApi,
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

// ---------------------------------------------------------------------------
// AI draft summary helper (for DraftSummaryStep)
// ---------------------------------------------------------------------------

export interface IAiDraftSummaryResponse {
  /** The generated summary text. */
  summary: string;
}

const BFF_AI_SUMMARY_ENDPOINT = '/api/workspace/matters/ai-summary';

/**
 * Calls the BFF AI endpoint to generate a draft matter summary.
 * Returns a mock response if the endpoint is unavailable (graceful fallback).
 */
export async function fetchAiDraftSummary(
  matterName: string,
  matterType: string,
  practiceArea: string
): Promise<IAiDraftSummaryResponse> {
  try {
    const response = await fetch(BFF_AI_SUMMARY_ENDPOINT, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ matterName, matterType, practiceArea }),
    });

    if (!response.ok) {
      // Fallback to mock summary
      return buildFallbackSummary(matterName, matterType, practiceArea);
    }

    const data: IAiDraftSummaryResponse = await response.json();
    return data;
  } catch {
    // Network error — return fallback
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
