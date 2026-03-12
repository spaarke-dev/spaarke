/**
 * workAssignmentService.ts
 * Service for the Work Assignment wizard.
 *
 * Creates sprk_workassignment records in Dataverse via Xrm.WebApi.
 * Follows the nav-prop discovery pattern from MatterService/EventService.
 *
 * Reuses search helpers from matterService for:
 *   - searchMatterTypes, searchPracticeAreas
 *   - searchContactsAsLookup, searchOrganizationsAsLookup, searchUsersAsLookup
 */

import type {
  ICreateWorkAssignmentFormState,
  IAssignWorkState,
  ICreateFollowOnEventState,
  ICreateWorkAssignmentResult,
} from './formTypes';
import type { ILookupItem } from '../../types/entities';
import type { IWebApi, WebApiEntity } from '../../types/xrm';
import { EntityCreationService } from '../../services/EntityCreationService';
import type { IUploadProgress } from '../../services/EntityCreationService';
import type { IUploadedFile } from '../CreateMatter/wizardTypes';
import { getBffBaseUrl } from '../../config/bffConfig';
import { authenticatedFetch } from '../../services/authInit';

// Re-export shared search helpers for use by step components
export {
  searchMatterTypes,
  searchPracticeAreas,
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
} from '../CreateMatter/matterService';

// ---------------------------------------------------------------------------
// Metadata discovery (same pattern as MatterService/EventService)
// ---------------------------------------------------------------------------

interface NavPropEntry {
  columnName: string;
  navPropName: string;
  referencedEntity: string;
}

const _navPropCache: Record<string, NavPropEntry[]> = {};

async function _discoverNavProps(entityLogicalName: string): Promise<NavPropEntry[]> {
  if (_navPropCache[entityLogicalName]) {
    return _navPropCache[entityLogicalName];
  }

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;

    const resp = await fetch(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[WorkAssignmentService] Nav-prop discovery failed for ${entityLogicalName}:`, resp.status);
      return [];
    }

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const json: any = await resp.json();
    const rels: Array<{
      ReferencingAttribute: string;
      ReferencingEntityNavigationPropertyName: string;
      ReferencedEntity: string;
    }> =
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (json as any).value ?? [];

    const entries: NavPropEntry[] = rels.map((r) => ({
      columnName: r.ReferencingAttribute,
      navPropName: r.ReferencingEntityNavigationPropertyName,
      referencedEntity: r.ReferencedEntity,
    }));

    console.info(`[WorkAssignmentService] Nav-props for ${entityLogicalName}:`, entries.map(e => `${e.columnName} → ${e.navPropName} (${e.referencedEntity})`));
    _navPropCache[entityLogicalName] = entries;
    return entries;
  } catch (err) {
    console.warn(`[WorkAssignmentService] Nav-prop discovery error for ${entityLogicalName}:`, err);
    return [];
  }
}

/**
 * Find a navigation property by referenced entity and optional column hint.
 * Returns the nav-prop name or undefined if not found.
 */
function _findNavProp(
  entries: NavPropEntry[],
  referencedEntity: string,
  columnHint?: string,
): string | undefined {
  const matches = entries.filter((e) => e.referencedEntity === referencedEntity);
  if (matches.length === 0) return undefined;
  if (matches.length === 1) return matches[0].navPropName;
  if (columnHint) {
    const hinted = matches.find((e) => e.columnName.includes(columnHint));
    if (hinted) return hinted.navPropName;
  }
  return matches[0].navPropName;
}


// ---------------------------------------------------------------------------
// WorkAssignmentService class
// ---------------------------------------------------------------------------

export class WorkAssignmentService {
  private readonly _entityService: EntityCreationService;

  constructor(
    private readonly _webApi: IWebApi,
    private readonly _containerId?: string
  ) {
    this._entityService = new EntityCreationService(_webApi);
  }

  // ── Record Search (Step 1) ──────────────────────────────────────────────

  /**
   * Search records by entity type for the "Work to Assign" step.
   * Supports: matter, project, invoice, event.
   */
  async searchRecordsByType(
    recordType: 'matter' | 'project' | 'invoice' | 'event',
    nameFilter: string
  ): Promise<ILookupItem[]> {
    if (!nameFilter || nameFilter.trim().length < 1) return [];

    const safeFilter = nameFilter.trim().replace(/'/g, "''");

    const configs: Record<string, { entity: string; idField: string; nameField: string; nameFilter: string }> = {
      matter: {
        entity: 'sprk_matter',
        idField: 'sprk_matterid',
        nameField: 'sprk_mattername',
        nameFilter: `contains(sprk_mattername,'${safeFilter}')`,
      },
      project: {
        entity: 'sprk_project',
        idField: 'sprk_projectid',
        nameField: 'sprk_projectname',
        nameFilter: `contains(sprk_projectname,'${safeFilter}')`,
      },
      invoice: {
        entity: 'sprk_invoice',
        idField: 'sprk_invoiceid',
        nameField: 'sprk_name',
        nameFilter: `contains(sprk_name,'${safeFilter}')`,
      },
      event: {
        entity: 'sprk_event',
        idField: 'sprk_eventid',
        nameField: 'sprk_eventname',
        nameFilter: `contains(sprk_eventname,'${safeFilter}')`,
      },
    };

    const cfg = configs[recordType];
    if (!cfg) return [];

    const query =
      `?$select=${cfg.idField},${cfg.nameField}` +
      `&$filter=${cfg.nameFilter}` +
      `&$orderby=${cfg.nameField} asc` +
      `&$top=10`;

    try {
      const result = await this._webApi.retrieveMultipleRecords(cfg.entity, query, 10);
      return result.entities.map((e) => ({
        id: e[cfg.idField] as string,
        name: e[cfg.nameField] as string,
      }));
    } catch (err) {
      console.error(`[WorkAssignmentService] searchRecordsByType(${recordType}) error:`, err);
      throw err;
    }
  }

  // ── Document Search (Step 2) ────────────────────────────────────────────

  /**
   * Search sprk_document records by name fragment for the "Share Documents" step.
   */
  async searchDocuments(nameFilter: string): Promise<ILookupItem[]> {
    if (!nameFilter || nameFilter.trim().length < 1) return [];

    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query =
      `?$select=sprk_documentid,sprk_documentname` +
      `&$filter=contains(sprk_documentname,'${safeFilter}')` +
      `&$orderby=sprk_documentname asc` +
      `&$top=10`;

    try {
      const result = await this._webApi.retrieveMultipleRecords('sprk_document', query, 10);
      return result.entities.map((e) => ({
        id: e['sprk_documentid'] as string,
        name: e['sprk_documentname'] as string,
      }));
    } catch (err) {
      console.error('[WorkAssignmentService] searchDocuments error:', err);
      throw err;
    }
  }

  // ── Contacts filtered by Organization (Follow-on: Assign Work) ──────────

  /**
   * Search contacts filtered by parent organization.
   * Used for "Law Firm Attorney" lookup, filtered by the selected law firm.
   */
  async searchContactsByOrganization(
    orgId: string,
    nameFilter: string
  ): Promise<ILookupItem[]> {
    if (!nameFilter || nameFilter.trim().length < 1) return [];

    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query =
      `?$select=contactid,fullname,emailaddress1` +
      `&$filter=contains(fullname,'${safeFilter}') and _parentcustomerid_value eq '${orgId}'` +
      `&$orderby=fullname asc` +
      `&$top=10`;

    try {
      const result = await this._webApi.retrieveMultipleRecords('contact', query, 10);
      return result.entities.map((e) => ({
        id: e['contactid'] as string,
        name: (e['fullname'] as string) + (e['emailaddress1'] ? ` (${e['emailaddress1']})` : ''),
      }));
    } catch (err) {
      console.error('[WorkAssignmentService] searchContactsByOrganization error:', err);
      throw err;
    }
  }

  // ── Create Work Assignment ──────────────────────────────────────────────

  /**
   * Full work assignment creation flow:
   *   1. Create sprk_workassignment record
   *   2. Upload files to SPE + create sprk_document records
   *   3. Link existing documents
   *   4. Apply Assign Work data if provided
   *
   * Returns ICreateWorkAssignmentResult — never throws.
   */
  async createWorkAssignment(
    form: ICreateWorkAssignmentFormState,
    linkedDocIds: string[],
    uploadedFiles: IUploadedFile[],
    assignWork?: IAssignWorkState,
    onUploadProgress?: (progress: IUploadProgress) => void
  ): Promise<ICreateWorkAssignmentResult> {
    const warnings: string[] = [];

    // ── Step 1: Create Dataverse record ─────────────────────────────────
    let workAssignmentId: string;

    const navProps = await _discoverNavProps('sprk_workassignment');

    const entity: WebApiEntity = {
      sprk_name: form.name.trim(),
      sprk_priority: form.priority,
    };

    if (form.description?.trim()) {
      entity['sprk_description'] = form.description.trim();
    }
    if (form.responseDueDate) {
      entity['sprk_responseduedate'] = form.responseDueDate;
    }

    // Helper: bind a lookup if the nav-prop exists on the entity
    const bindLookup = (
      referencedEntity: string,
      entitySet: string,
      guid: string,
      columnHint?: string
    ) => {
      const navProp = _findNavProp(navProps, referencedEntity, columnHint);
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/${entitySet}(${guid})`;
      } else {
        console.warn(`[WorkAssignmentService] No nav-prop found for ${referencedEntity} (hint: ${columnHint}), skipping binding`);
      }
    };

    // Related record (matter, project, invoice, event)
    if (form.recordId && form.recordType) {
      const refMap: Record<string, { refEntity: string; entitySet: string; hint: string }> = {
        matter: { refEntity: 'sprk_matter', entitySet: 'sprk_matters', hint: 'matter' },
        project: { refEntity: 'sprk_project', entitySet: 'sprk_projects', hint: 'project' },
        invoice: { refEntity: 'sprk_invoice', entitySet: 'sprk_invoices', hint: 'invoice' },
        event: { refEntity: 'sprk_event', entitySet: 'sprk_events', hint: 'event' },
      };
      const mapping = refMap[form.recordType];
      if (mapping) {
        bindLookup(mapping.refEntity, mapping.entitySet, form.recordId, mapping.hint);
      }
    }

    if (form.matterTypeId) bindLookup('sprk_mattertype_ref', 'sprk_mattertype_refs', form.matterTypeId, 'mattertype');
    if (form.practiceAreaId) bindLookup('sprk_practicearea_ref', 'sprk_practicearea_refs', form.practiceAreaId, 'practicearea');

    // Assign Work lookups (if provided)
    if (assignWork) {
      if (assignWork.assignedAttorneyId) bindLookup('contact', 'contacts', assignWork.assignedAttorneyId, 'attorney');
      if (assignWork.assignedParalegalId) bindLookup('contact', 'contacts', assignWork.assignedParalegalId, 'paralegal');
      if (assignWork.assignedLawFirmId) bindLookup('sprk_organization', 'sprk_organizations', assignWork.assignedLawFirmId, 'lawfirm');
      if (assignWork.assignedLawFirmAttorneyId) bindLookup('contact', 'contacts', assignWork.assignedLawFirmAttorneyId, 'lawfirmattorney');
    }

    try {
      console.info('[WorkAssignmentService] createRecord payload:', JSON.stringify(entity, null, 2));
      const result = await this._webApi.createRecord('sprk_workassignment', entity);
      workAssignmentId = result.id;
      console.info('[WorkAssignmentService] createRecord success, workAssignmentId:', workAssignmentId);
    } catch (err) {
      console.error('[WorkAssignmentService] createRecord error:', err);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const errObj = err as any;
      const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
      return {
        status: 'error',
        errorMessage: `Failed to create work assignment record: ${message}`,
        warnings: [],
      };
    }

    // ── Step 2: Upload files to SPE ─────────────────────────────────────
    if (uploadedFiles.length > 0 && this._containerId) {
      const uploadResult = await this._entityService.uploadFilesToSpe(
        this._containerId,
        uploadedFiles,
        onUploadProgress
      );

      if (!uploadResult.success) {
        warnings.push(
          `File upload failed (${uploadResult.failureCount} of ${uploadedFiles.length}). ` +
          'Files can be added from the work assignment record.'
        );
      } else if (uploadResult.uploadedFiles.length > 0) {
        const docNavProps = await _discoverNavProps('sprk_document');
        const docWaNavProp = _findNavProp(docNavProps, 'sprk_workassignment');

        if (docWaNavProp) {
          const linkResult = await this._entityService.createDocumentRecords(
            'sprk_workassignments',
            workAssignmentId,
            docWaNavProp,
            uploadResult.uploadedFiles,
            {
              containerId: this._containerId,
              parentRecordName: form.name.trim(),
            }
          );
          if (linkResult.warnings.length > 0) {
            warnings.push(...linkResult.warnings);
          }
        } else {
          warnings.push('Could not link uploaded documents — relationship not found. Link them manually.');
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

    // ── Step 3: Link existing documents ─────────────────────────────────
    if (linkedDocIds.length > 0) {
      const docNavProps = await _discoverNavProps('sprk_document');
      const docWaNavProp = _findNavProp(docNavProps, 'sprk_workassignment');

      if (docWaNavProp) {
        for (const docId of linkedDocIds) {
          try {
            const updatePayload: WebApiEntity = {
              [`${docWaNavProp}@odata.bind`]: `/sprk_workassignments(${workAssignmentId})`,
            };
            await this._webApi.updateRecord('sprk_document', docId, updatePayload);
          } catch (err) {
            console.warn(`[WorkAssignmentService] Failed to link document ${docId}:`, err);
            warnings.push(`Could not link document (${docId}). Link it manually from the record.`);
          }
        }
      } else {
        warnings.push('Could not link existing documents — relationship not found. Link them manually.');
      }
    }

    return {
      status: warnings.length > 0 ? 'partial' : 'success',
      workAssignmentId,
      workAssignmentName: form.name.trim(),
      warnings,
    };
  }

  // ── Create Follow-On Event ────────────────────────────────────────────

  /**
   * Create a sprk_event record linked to the work assignment.
   * Event type is "Assign Work" by default.
   */
  async createFollowOnEvent(
    workAssignmentId: string,
    eventState: ICreateFollowOnEventState
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      const navProps = await _discoverNavProps('sprk_event');

      const entity: WebApiEntity = {
        sprk_eventname: eventState.eventName.trim(),
        sprk_priority: eventState.eventPriority,
      };

      if (eventState.eventDescription?.trim()) {
        entity['sprk_description'] = eventState.eventDescription.trim();
      }
      if (eventState.eventDueDate) {
        entity['sprk_duedate'] = eventState.eventDueDate;
      }
      if (eventState.eventFinalDueDate) {
        entity['sprk_finalduedate'] = eventState.eventFinalDueDate;
      }

      // Link to work assignment
      const waNavProp = _findNavProp(navProps, 'sprk_workassignment', 'workassignment');
      if (waNavProp) {
        entity[`${waNavProp}@odata.bind`] = `/sprk_workassignments(${workAssignmentId})`;
      }

      // Assigned To (systemuser)
      if (eventState.assignedToId) {
        const assignedNavProp = _findNavProp(navProps, 'systemuser', 'assignedto');
        if (assignedNavProp) {
          entity[`${assignedNavProp}@odata.bind`] = `/systemusers(${eventState.assignedToId})`;
        }
      }

      await this._webApi.createRecord('sprk_event', entity);
      return { success: true };
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        success: false,
        warning: `Could not create follow-on event (${message}). Please create manually.`,
      };
    }
  }

  // ── Send Email via BFF ────────────────────────────────────────────────

  /**
   * Send an email via the BFF communications endpoint.
   */
  async sendEmail(
    workAssignmentId: string,
    workAssignmentName: string,
    to: string,
    subject: string,
    body: string
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      const bffBaseUrl = getBffBaseUrl();
      const response = await authenticatedFetch(
        `${bffBaseUrl}/api/communications/send`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            to: to.split(/[;,]/).map((a: string) => a.trim()).filter(Boolean),
            subject,
            body,
            bodyFormat: 'HTML',
            associations: [
              {
                entityType: 'sprk_workassignment',
                entityId: workAssignmentId,
                entityName: workAssignmentName,
              },
            ],
          }),
        }
      );

      if (!response.ok) {
        const errorText = await response.text().catch(() => 'Unknown error');
        console.warn('[WorkAssignmentService] Email send failed:', response.status, errorText);
        return {
          success: false,
          warning: `Could not send email (${response.status}). Please send manually.`,
        };
      }

      return { success: true };
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        success: false,
        warning: `Could not send email (${message}). Please send manually.`,
      };
    }
  }
}
