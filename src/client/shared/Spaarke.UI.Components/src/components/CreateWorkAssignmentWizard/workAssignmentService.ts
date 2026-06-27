/**
 * workAssignmentService.ts
 * Service for the Work Assignment wizard.
 *
 * Creates sprk_workassignment records in Dataverse via IDataService.
 * Follows the nav-prop discovery pattern from MatterService/EventService.
 *
 * Dependencies are injected via constructor -- no solution-specific imports.
 * authenticatedFetch and bffBaseUrl are passed at construction time.
 *
 * Reuses search helpers from the shared CreateMatterWizard matterService for:
 *   - searchMatterTypes, searchPracticeAreas
 *   - searchContactsAsLookup, searchOrganizationsAsLookup, searchUsersAsLookup
 */

import type {
  ICreateWorkAssignmentFormState,
  IAssignWorkState,
  ICreateFollowOnEventState,
  ICreateWorkAssignmentResult,
} from './formTypes';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import { EntityCreationService } from '../../services/EntityCreationService';
import type { IUploadProgress, AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import { applyResolverFields, findNavProp } from '../../services/PolymorphicResolverService';
import type { INavPropEntry } from '../../services/PolymorphicResolverService';

// Re-export shared search helpers for use by step components
export {
  searchMatterTypes,
  searchPracticeAreas,
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
} from '../CreateMatterWizard/matterService';

// ---------------------------------------------------------------------------
// Metadata discovery (same pattern as MatterService/EventService)
// ---------------------------------------------------------------------------

const _navPropCache: Record<string, INavPropEntry[]> = {};

async function _discoverNavProps(entityLogicalName: string): Promise<INavPropEntry[]> {
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

    const entries: INavPropEntry[] = rels.map(r => ({
      columnName: r.ReferencingAttribute,
      navPropName: r.ReferencingEntityNavigationPropertyName,
      referencedEntity: r.ReferencedEntity,
    }));

    console.info(
      `[WorkAssignmentService] Nav-props for ${entityLogicalName}:`,
      entries.map(e => `${e.columnName} -> ${e.navPropName} (${e.referencedEntity})`)
    );
    _navPropCache[entityLogicalName] = entries;
    return entries;
  } catch (err) {
    console.warn(`[WorkAssignmentService] Nav-prop discovery error for ${entityLogicalName}:`, err);
    return [];
  }
}

// findNavProp, resolveRecordType, buildRecordUrl, applyResolverFields
// are imported from the shared PolymorphicResolverService.

/**
 * Resolve the current Dataverse user ID from the host Xrm global.
 *
 * Walks `window` → `window.parent` → `window.top` to find an `Xrm.Utility.getGlobalContext()`
 * (Code Page hosted in a Power App iframe) or `Xrm.Utility.getUserId()` (PCF / direct host).
 * Returns `''` (empty) when no Xrm context is reachable — caller treats that as "skip cascade".
 *
 * Kept module-private (not exported) so consumers cannot pass an arbitrary user ID; this
 * preserves the "current user" semantics of FR-WIZ-04.
 */
function _getCurrentUserId(): string {
  const frames: Window[] = [window];
  try {
    if (window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window) frames.push(window.top);
  } catch {
    /* cross-origin */
  }

  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (frame as any).Xrm;
      if (xrm?.Utility?.getGlobalContext) {
        const ctx = xrm.Utility.getGlobalContext();
        const userId = ctx?.userSettings?.userId;
        if (typeof userId === 'string' && userId.trim() !== '') {
          return userId.replace(/^\{|\}$/g, '').toLowerCase();
        }
      }
      if (typeof xrm?.Utility?.getUserId === 'function') {
        const userId = xrm.Utility.getUserId();
        if (typeof userId === 'string' && userId.trim() !== '') {
          return userId.replace(/^\{|\}$/g, '').toLowerCase();
        }
      }
    } catch {
      /* cross-origin */
    }
  }
  return '';
}

/**
 * Resolve navigation property name for a document lookup by referenced entity.
 * Used when creating sprk_document records linked to a parent entity.
 */
function _resolveDocNavProp(entries: INavPropEntry[], referencedEntity: string): string {
  const match = entries.find(e => e.referencedEntity === referencedEntity);
  if (match) {
    console.info(`[WorkAssignmentService] Resolved doc nav-prop: ${match.navPropName} for ${referencedEntity}`);
    return match.navPropName;
  }
  console.warn(`[WorkAssignmentService] No nav-prop found on sprk_document for ${referencedEntity}`);
  return referencedEntity;
}

// ---------------------------------------------------------------------------
// WorkAssignmentService class
// ---------------------------------------------------------------------------

export class WorkAssignmentService {
  private readonly _dataService: IDataService;
  private readonly _entityService: EntityCreationService;
  private readonly _tenantId: string;

  constructor(
    dataService: IDataService,
    authenticatedFetch: AuthenticatedFetchFn,
    bffBaseUrl: string,
    private readonly _containerId?: string,
    /**
     * AAD tenant ID for post-upload RAG indexing routing. When omitted,
     * indexing is skipped (files still upload to SPE successfully).
     */
    tenantId?: string
  ) {
    this._tenantId = tenantId ?? '';
    this._dataService = dataService;
    // EntityCreationService expects IWebApiWithCreate which has createRecord returning { id: string }.
    // Wrap IDataService to adapt createRecord return type.
    const webApiAdapter = {
      createRecord: async (entityName: string, data: Record<string, unknown>) => {
        const id = await dataService.createRecord(entityName, data);
        return { id };
      },
      retrieveRecord: (entityName: string, id: string, options?: string) =>
        dataService.retrieveRecord(entityName, id, options),
      retrieveMultipleRecords: (entityName: string, options?: string) =>
        dataService.retrieveMultipleRecords(entityName, options),
      updateRecord: async (entityName: string, id: string, data: Record<string, unknown>) => {
        await dataService.updateRecord(entityName, id, data);
        return { id };
      },
      deleteRecord: async (entityName: string, id: string) => {
        await dataService.deleteRecord(entityName, id);
        return { id };
      },
    };
    this._entityService = new EntityCreationService(webApiAdapter, authenticatedFetch, bffBaseUrl);
  }

  // -- Record Search (Step 1) ------------------------------------------------

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
      // IDataService.retrieveMultipleRecords has no maxPageSize param
      const result = await this._dataService.retrieveMultipleRecords(cfg.entity, query);
      return result.entities.map(e => ({
        id: e[cfg.idField] as string,
        name: e[cfg.nameField] as string,
      }));
    } catch (err) {
      console.error(`[WorkAssignmentService] searchRecordsByType(${recordType}) error:`, err);
      throw err;
    }
  }

  // -- Record Pre-fill (Step 3 -- read selected record fields) ---------------

  /**
   * Read the selected record's fields for pre-filling the Enter Info step.
   * Maps entity-specific field names to form state fields.
   */
  async readRecordForPrefill(
    recordType: 'matter' | 'project' | 'invoice' | 'event',
    recordId: string
  ): Promise<Partial<ICreateWorkAssignmentFormState>> {
    const fieldMaps: Record<
      string,
      {
        entity: string;
        nameField: string;
        descField?: string;
        matterTypeField?: string;
        practiceAreaField?: string;
        priorityField?: string;
      }
    > = {
      matter: {
        entity: 'sprk_matter',
        nameField: 'sprk_mattername',
        descField: 'sprk_matterdescription',
        matterTypeField: '_sprk_mattertype_value',
        practiceAreaField: '_sprk_practicearea_value',
      },
      project: {
        entity: 'sprk_project',
        nameField: 'sprk_projectname',
        descField: 'sprk_projectdescription',
        matterTypeField: '_sprk_mattertype_value',
        practiceAreaField: '_sprk_practicearea_value',
      },
      invoice: {
        entity: 'sprk_invoice',
        nameField: 'sprk_name',
        descField: 'sprk_description',
      },
      event: {
        entity: 'sprk_event',
        nameField: 'sprk_eventname',
        descField: 'sprk_description',
        priorityField: 'sprk_priority',
      },
    };

    const mapping = fieldMaps[recordType];
    if (!mapping) return {};

    const result: Partial<ICreateWorkAssignmentFormState> = {};

    // Fetch basic scalar fields first (name, description, priority)
    try {
      const basicFields = [mapping.nameField];
      if (mapping.descField) basicFields.push(mapping.descField);
      if (mapping.priorityField) basicFields.push(mapping.priorityField);

      const basicQuery = `?$select=${basicFields.join(',')}`;
      console.info(`[WorkAssignmentService] readRecordForPrefill basic: ${mapping.entity}(${recordId})${basicQuery}`);
      const record = await this._dataService.retrieveRecord(mapping.entity, recordId, basicQuery);

      const nameVal = record[mapping.nameField] as string | undefined;
      if (nameVal) result.name = nameVal;

      if (mapping.descField) {
        const descVal = record[mapping.descField] as string | undefined;
        if (descVal) result.description = descVal;
      }

      if (mapping.priorityField) {
        const prioVal = record[mapping.priorityField] as number | undefined;
        if (prioVal) result.priority = prioVal;
      }
    } catch (err) {
      console.warn(
        `[WorkAssignmentService] readRecordForPrefill basic fields failed for ${recordType}(${recordId}):`,
        err
      );
      return result;
    }

    // Fetch each lookup field individually (if one column doesn't exist, the other still works)
    if (mapping.matterTypeField) {
      try {
        const mtQuery = `?$select=${mapping.matterTypeField}`;
        console.info(
          `[WorkAssignmentService] readRecordForPrefill matterType: ${mapping.entity}(${recordId})${mtQuery}`
        );
        const mtRecord = await this._dataService.retrieveRecord(mapping.entity, recordId, mtQuery);
        const mtId = mtRecord[mapping.matterTypeField] as string | undefined;
        if (mtId) {
          result.matterTypeId = mtId;
          const formattedKey = `${mapping.matterTypeField}@OData.Community.Display.V1.FormattedValue`;
          result.matterTypeName = (mtRecord[formattedKey] as string) ?? '';
        }
      } catch (err) {
        console.warn(
          `[WorkAssignmentService] readRecordForPrefill matterType failed for ${recordType}(${recordId}):`,
          err
        );
      }
    }

    if (mapping.practiceAreaField) {
      try {
        const paQuery = `?$select=${mapping.practiceAreaField}`;
        console.info(
          `[WorkAssignmentService] readRecordForPrefill practiceArea: ${mapping.entity}(${recordId})${paQuery}`
        );
        const paRecord = await this._dataService.retrieveRecord(mapping.entity, recordId, paQuery);
        const paId = paRecord[mapping.practiceAreaField] as string | undefined;
        if (paId) {
          result.practiceAreaId = paId;
          const formattedKey = `${mapping.practiceAreaField}@OData.Community.Display.V1.FormattedValue`;
          result.practiceAreaName = (paRecord[formattedKey] as string) ?? '';
        }
      } catch (err) {
        console.warn(
          `[WorkAssignmentService] readRecordForPrefill practiceArea failed for ${recordType}(${recordId}):`,
          err
        );
      }
    }

    console.info(`[WorkAssignmentService] readRecordForPrefill result for ${recordType}:`, JSON.stringify(result));
    return result;
  }

  // -- Contacts filtered by Organization (Follow-on: Assign Work) ------------

  /**
   * Search contacts filtered by parent organization.
   * Used for "Law Firm Attorney" lookup, filtered by the selected law firm.
   */
  async searchContactsByOrganization(orgId: string, nameFilter: string): Promise<ILookupItem[]> {
    if (!nameFilter || nameFilter.trim().length < 1) return [];

    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query =
      `?$select=contactid,fullname,emailaddress1` +
      `&$filter=contains(fullname,'${safeFilter}') and _parentcustomerid_value eq '${orgId}'` +
      `&$orderby=fullname asc` +
      `&$top=10`;

    try {
      // IDataService.retrieveMultipleRecords has no maxPageSize param
      const result = await this._dataService.retrieveMultipleRecords('contact', query);
      return result.entities.map(e => ({
        id: e['contactid'] as string,
        name: (e['fullname'] as string) + (e['emailaddress1'] ? ` (${e['emailaddress1']})` : ''),
      }));
    } catch (err) {
      console.error('[WorkAssignmentService] searchContactsByOrganization error:', err);
      throw err;
    }
  }

  // -- Create Work Assignment ------------------------------------------------

  /**
   * Full work assignment creation flow:
   *   1. Create sprk_workassignment record
   *   2. Upload files to SPE + create sprk_document records
   *   3. Apply Assign Work data if provided
   *
   * Returns ICreateWorkAssignmentResult -- never throws.
   */
  async createWorkAssignment(
    form: ICreateWorkAssignmentFormState,
    _linkedDocIds: string[],
    uploadedFiles: IUploadedFile[],
    assignWork?: IAssignWorkState,
    onUploadProgress?: (progress: IUploadProgress) => void
  ): Promise<ICreateWorkAssignmentResult> {
    const warnings: string[] = [];

    // -- Step 1: Create Dataverse record -------------------------------------
    let workAssignmentId: string;

    const navProps = await _discoverNavProps('sprk_workassignment');

    const entity: Record<string, unknown> = {
      sprk_name: form.name.trim(),
      sprk_priority: form.priority,
    };

    if (form.description?.trim()) {
      entity['sprk_description'] = form.description.trim();
    }
    if (form.responseDueDate) {
      entity['sprk_responseduedate'] = form.responseDueDate;
    }

    // Store the host-resolved SPE container ID on the work assignment record (enables Documents tab).
    // Applied FIRST so it acts as an explicit override during the subsequent BU cascade (INV-5).
    if (this._containerId) {
      entity['sprk_containerid'] = this._containerId;
    }

    // Helper: bind a lookup if the nav-prop exists on the entity
    const bindLookup = (referencedEntity: string, entitySet: string, guid: string, columnHint?: string) => {
      const navProp = findNavProp(navProps, referencedEntity, columnHint);
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/${entitySet}(${guid})`;
      } else {
        console.warn(
          `[WorkAssignmentService] No nav-prop found for ${referencedEntity} (hint: ${columnHint}), skipping binding`
        );
      }
    };

    // FR-WIZ-04: Cascade `sprk_containerid` + `sprk_searchindexname` + Phase G `sprk_ai_search_index`
    // lookup from the current user's owning Business Unit. INV-5 guards each scalar field
    // independently. If the user's BU has any field unset, the corresponding payload field is
    // left untouched and the BFF tenant-default chain takes over server-side.
    try {
      const currentUserId = _getCurrentUserId();
      if (currentUserId) {
        // IDataService is a structural superset of IWebApiLike (retrieveRecord, retrieveMultipleRecords).
        const defaults = await EntityCreationService.resolveUserBuDefaults(this._dataService, currentUserId);
        EntityCreationService.applyUserBuDefaults(entity, defaults);
        if (defaults.searchIndexId) {
          bindLookup('sprk_aisearchindex', 'sprk_aisearchindexes', defaults.searchIndexId);
        }
      } else {
        console.warn('[WorkAssignmentService] BU cascade skipped: current user ID could not be resolved.');
      }
    } catch (err) {
      // Non-fatal: log and continue. BFF tenant-default chain handles routing if all fields are unset.
      console.warn('[WorkAssignmentService] BU cascade failed (non-fatal):', err);
    }

    // Related record (matter, project, invoice, event)
    // Uses the shared Polymorphic Resolver pattern (ADR-024):
    //   Entity-specific lookup + 4 denormalized resolver fields
    if (form.recordId && form.recordType) {
      const refMap: Record<string, { refEntity: string; entitySet: string; hint: string }> = {
        matter: { refEntity: 'sprk_matter', entitySet: 'sprk_matters', hint: 'matter' },
        project: { refEntity: 'sprk_project', entitySet: 'sprk_projects', hint: 'project' },
        invoice: { refEntity: 'sprk_invoice', entitySet: 'sprk_invoices', hint: 'invoice' },
        event: { refEntity: 'sprk_event', entitySet: 'sprk_events', hint: 'event' },
      };
      const refMapping = refMap[form.recordType];
      if (refMapping) {
        // applyResolverFields accepts IPolymorphicWebApi which has optional maxPageSize.
        // Wrap IDataService to match.
        const polyWebApi = {
          retrieveMultipleRecords: (entityLogicalName: string, query: string) =>
            this._dataService.retrieveMultipleRecords(entityLogicalName, query),
        };
        await applyResolverFields(
          polyWebApi,
          entity,
          navProps,
          refMapping.refEntity,
          refMapping.entitySet,
          form.recordId,
          form.recordName,
          refMapping.hint
        );
      }
    }

    if (form.matterTypeId) bindLookup('sprk_mattertype_ref', 'sprk_mattertype_refs', form.matterTypeId, 'mattertype');
    if (form.practiceAreaId)
      bindLookup('sprk_practicearea_ref', 'sprk_practicearea_refs', form.practiceAreaId, 'practicearea');

    // Assign Work lookups (if provided)
    if (assignWork) {
      if (assignWork.assignedAttorneyId) bindLookup('contact', 'contacts', assignWork.assignedAttorneyId, 'attorney');
      if (assignWork.assignedParalegalId)
        bindLookup('contact', 'contacts', assignWork.assignedParalegalId, 'paralegal');
      if (assignWork.assignedLawFirmId)
        bindLookup('sprk_organization', 'sprk_organizations', assignWork.assignedLawFirmId, 'lawfirm');
      if (assignWork.assignedLawFirmAttorneyId)
        bindLookup('contact', 'contacts', assignWork.assignedLawFirmAttorneyId, 'lawfirmattorney');
    }

    try {
      console.info('[WorkAssignmentService] createRecord payload:', JSON.stringify(entity, null, 2));
      // IDataService.createRecord returns Promise<string> (just the id)
      workAssignmentId = await this._dataService.createRecord('sprk_workassignment', entity);
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

    // -- Step 2: Upload files to SPE -----------------------------------------
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
        // Discover nav-prop for sprk_document -> sprk_workassignment lookup
        try {
          const docNavProps = await _discoverNavProps('sprk_document');
          const waNavProp = _resolveDocNavProp(docNavProps, 'sprk_workassignment');

          const createResult = await this._entityService.createDocumentRecords(
            'sprk_workassignments',
            workAssignmentId,
            waNavProp,
            uploadResult.uploadedFiles,
            {
              containerId: this._containerId,
              parentRecordName: form.name.trim(),
            }
          );
          if (createResult.warnings.length > 0) {
            warnings.push(...createResult.warnings);
          }

          // Trigger RAG indexing per file (canonical sync OBO path via
          // @spaarke/sdap-client.SdapApiClient.indexFile). Non-fatal.
          const indexingWarnings = await this._entityService.indexUploadedFiles(
            uploadResult.uploadedFiles,
            this._tenantId,
            {
              entityType: 'sprk_workassignment',
              entityId: workAssignmentId,
              entityName: form.name.trim(),
            },
            createResult.createdDocumentIds
          );
          if (indexingWarnings.length > 0) {
            warnings.push(...indexingWarnings);
          }
        } catch (err) {
          console.warn('[WorkAssignmentService] Document record creation failed:', err);
          warnings.push('Uploaded files saved to storage but document records could not be created.');
        }
      }

      if (uploadResult.failureCount > 0 && uploadResult.successCount > 0) {
        warnings.push(
          `${uploadResult.failureCount} file(s) failed to upload: ` +
            uploadResult.errors.map(e => e.fileName).join(', ')
        );
      }
    } else if (uploadedFiles.length > 0 && !this._containerId) {
      warnings.push('File upload skipped -- no SPE container configured. Files can be added later.');
    }

    return {
      status: warnings.length > 0 ? 'partial' : 'success',
      workAssignmentId,
      workAssignmentName: form.name.trim(),
      warnings,
    };
  }

  // -- Create Follow-On Event ------------------------------------------------

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

      const entity: Record<string, unknown> = {
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

      // R3 (smart-todo-decoupling-r3, task 031): The legacy "Add a To Do"
      // checkbox that wrote `entity['sprk_todoflag'] = true` was removed here
      // per FR-15 / OS-1. The `sprk_event.sprk_todoflag` column is being
      // dropped from the schema; To Dos are now first-class `sprk_todo`
      // records created via the CreateTodoWizard. Consumers who want a
      // companion To Do for a work assignment's follow-on event should
      // launch the CreateTodoWizard separately and select the event as
      // the regarding record.

      // Link to work assignment
      const waNavProp = findNavProp(navProps, 'sprk_workassignment', 'workassignment');
      if (waNavProp) {
        entity[`${waNavProp}@odata.bind`] = `/sprk_workassignments(${workAssignmentId})`;
      }

      // Assigned To (systemuser)
      if (eventState.assignedToId) {
        const assignedNavProp = findNavProp(navProps, 'systemuser', 'assignedto');
        if (assignedNavProp) {
          entity[`${assignedNavProp}@odata.bind`] = `/systemusers(${eventState.assignedToId})`;
        }
      }

      // IDataService.createRecord returns Promise<string>
      await this._dataService.createRecord('sprk_event', entity);
      return { success: true };
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        success: false,
        warning: `Could not create follow-on event (${message}). Please create manually.`,
      };
    }
  }

  // -- Send Email via BFF ----------------------------------------------------

  /**
   * Send an email via the BFF communications endpoint.
   */
  async sendEmail(
    workAssignmentId: string,
    workAssignmentName: string,
    to: string,
    subject: string,
    body: string,
    cc?: string
  ): Promise<{ success: boolean; warning?: string }> {
    return this._entityService.sendEmail({
      to,
      cc,
      subject,
      body,
      bodyFormat: 'PlainText', // matches server enum BodyFormat (2026-05-25)
      associations: [{ entityType: 'sprk_workassignment', entityId: workAssignmentId, entityName: workAssignmentName }],
    });
  }
}
