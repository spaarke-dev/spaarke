/**
 * workAssignmentService.ts
 * Service for the Work Assignment wizard.
 *
 * Creates sprk_workassignment records through the BFF (ontology task 046, D-21/D-113): the wizard builds the Web API
 * payload and POSTs it to `/api/v1/child-records/sprk_workassignment`; the server writes it (WP-3). Reads still go through
 * IDataService.
 * Follows the nav-prop discovery pattern from MatterService/EventService.
 *
 * Dependencies are injected via constructor -- no solution-specific imports.
 * authenticatedFetch and bffBaseUrl are passed at construction time.
 *
 * Reuses search helpers from the shared CreateMatterWizard matterService for:
 *   - searchMatterTypes, searchPracticeAreas
 *   - searchContactsAsLookup, searchOrganizationsAsLookup, searchUsersAsLookup
 */

import { describeWebApiCreateError } from '../../utils/webApiCreateError';
import { withBffChildWrites, createRecordViaBffWithWarnings } from '../../utils/adapters/bffChildWriteAdapter';
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
import {
  applyResolverFields,
  findNavProp,
  discoverNavProps,
  toNavPropMap,
  cleanGuid,
} from '../../services/PolymorphicResolverService';

// Re-export shared search helpers for use by step components
export {
  searchMatterTypes,
  searchPracticeAreas,
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
} from '../CreateMatterWizard/matterService';
import { sendComposedEmail } from '../EmailComposer/composeOnlySend';
import type { IComposedEmail } from '../EmailComposer/composeOnlySend';
import { createWebApiFromDataService } from '../../utils/adapters/dataServiceWebApiAdapter';

// ---------------------------------------------------------------------------
// Metadata discovery (same pattern as MatterService/EventService)
// ---------------------------------------------------------------------------

// Nav-prop discovery, findNavProp, resolveRecordType, buildRecordUrl,
// applyResolverFields are imported from the shared PolymorphicResolverService.
// `discoverNavProps` was consolidated from a private per-service copy on
// 2026-07-09 (task 011, Path A).

/** Shown when a chosen Assigned To contact could not be sent (task 038 fix round 1). */
const ASSIGNED_TO_NOT_SET_WARNING = 'Assigned To could not be set; the record uses its Assigned To Internal value.';

// ---------------------------------------------------------------------------
// WorkAssignmentService class
// ---------------------------------------------------------------------------

export class WorkAssignmentService {
  private readonly _dataService: IDataService;
  private readonly _entityService: EntityCreationService;
  private readonly _tenantId: string;
  private readonly _authenticatedFetch: AuthenticatedFetchFn;
  private readonly _bffBaseUrl: string;

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
    // UAC-r2 task 147 r1 (owner round 28 item 1): every CHILD create / re-file this service makes (and the file step's
    // documents) goes through the BFF (G5) — the server decides the owner; nothing is created as the user.
    this._dataService = withBffChildWrites(dataService, authenticatedFetch, bffBaseUrl);
    // Stored (not just forwarded to EntityCreationService): the work assignment and its follow-on event are created on the
    // BFF route directly (they name a mapping source, which IDataService.createRecord cannot carry).
    this._authenticatedFetch = authenticatedFetch;
    this._bffBaseUrl = bffBaseUrl;
    // EntityCreationService expects IWebApiWithCreate which has createRecord returning { id: string }.
    // Wrap IDataService to adapt createRecord return type.
    const webApiAdapter = createWebApiFromDataService(this._dataService);
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

    const navProps = await discoverNavProps('sprk_workassignment');

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

    // 🔴 DELETED 2026-09-03 by task 076 (harmful write "W1", second half) — same shape and same
    // reasoning as the CreateInvoiceWizard and CreateMatterWizard twins. The client does not choose
    // where a record's content is stored; the server derives it from the record at upload time.

    // Helper: bind a lookup if the nav-prop exists on the entity
    const bindLookup = (referencedEntity: string, entitySet: string, guid: string, columnHint?: string) => {
      const navProp = findNavProp(navProps, referencedEntity, columnHint);
      if (navProp) {
        entity[`${navProp}@odata.bind`] = `/${entitySet}(${cleanGuid(guid)})`;
      } else {
        console.warn(
          `[WorkAssignmentService] No nav-prop found for ${referencedEntity} (hint: ${columnHint}), skipping binding`
        );
      }
    };

    // No client defaults. The BU cascade this wizard used to write (FR-WIZ-04: `sprk_searchindexname` and the
    // `sprk_ai_search_index` bind) is gone: `sprk_workassignment` is `SearchIndexed` in RecordTableRules, so both index
    // columns are the server's (FR-38, FR-40) and a caller's value is dropped. The container was removed by task 076.
    // Field mapping, owner, owning team and the creator-person stamp are applied by the server (FR-02, FR-09, FR-10).

    // Related record (matter, project, invoice, event)
    // The server maps from this record only when the create NAMES it as the mapping source (sprk_workassignment is not in
    // ChildRecordEndpoints.InferMappingSourceTables); no related record means no source and no mapping.
    let mappingSource: { entity: string; id: string } | undefined;
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

        mappingSource = { entity: refMapping.refEntity, id: form.recordId };
      }
    }

    if (form.matterTypeId)
      bindLookup('sprk_mattertype_ref', 'sprk_mattertype_refs', form.matterTypeId, 'sprk_mattertype');
    if (form.practiceAreaId)
      bindLookup('sprk_practicearea_ref', 'sprk_practicearea_refs', form.practiceAreaId, 'sprk_practicearea');

    // Assigned To (contact) -- task 038 / FR-37. `sprk_workassignment.sprk_assignedto` targets CONTACT in the live
    // schema (nav prop `sprk_AssignedTo`); the entity also has look-alike contact lookups (assigned attorney /
    // paralegal, `sprk_assignedto1`, `sprk_assignedtointernal`), so it is resolved by exact column, never by
    // substring or by referenced entity. A BLANK picker writes NOTHING -- no bind and no null: the server (task 037,
    // FR-36) fills the column from Assigned To Internal only when the create leaves it UNSET, and an explicit null
    // would block that default. The default is never computed here.
    if (form.assignedToId) {
      const assignedToNavProp = toNavPropMap(navProps)['sprk_assignedto'];
      if (assignedToNavProp) {
        entity[`${assignedToNavProp}@odata.bind`] = `/contacts(${cleanGuid(form.assignedToId)})`;
      } else {
        console.warn(
          '[WorkAssignmentService] sprk_workassignment.sprk_assignedto nav-prop not found; Assigned To not set'
        );
        // The person chose a contact that cannot be sent: say so (the dialog shows result.warnings) instead of
        // silently giving the record its server default.
        warnings.push(ASSIGNED_TO_NOT_SET_WARNING);
      }
    }

    // Assign Work lookups (if provided). The wizard has ONE of each, so each binds the PRIMARY slot (`1`) by its FULL
    // logical name: the table also has Assigned Attorney 2 / Paralegal 2 / Law Firm 2, and findNavProp binds the first
    // column CONTAINING a fragment, which would be whichever Dataverse lists first (task 024 found this on sprk_todo).
    if (assignWork) {
      if (assignWork.assignedAttorneyId)
        bindLookup('contact', 'contacts', assignWork.assignedAttorneyId, 'sprk_assignedattorney1');
      if (assignWork.assignedParalegalId)
        bindLookup('contact', 'contacts', assignWork.assignedParalegalId, 'sprk_assignedparalegal1');
      if (assignWork.assignedLawFirmId)
        bindLookup('sprk_organization', 'sprk_organizations', assignWork.assignedLawFirmId, 'sprk_assignedlawfirm1');
      if (assignWork.assignedLawFirmAttorneyId)
        bindLookup('contact', 'contacts', assignWork.assignedLawFirmAttorneyId, 'sprk_assignedlawfirmattorney1');
    }

    try {
      console.info('[WorkAssignmentService] create payload:', JSON.stringify(entity, null, 2));
      // Ontology task 046 (D-21; uac-r2 D-113): the SAME payload goes to the BFF instead of Xrm.WebApi — checked as the
      // user there (Create/Append, AppendTo on every record it binds), created by the server owned by the business-unit
      // team with the user as its creator person, and created secure when filed under a secure matter or project. The
      // server also gives the "Assigned *" contacts their access, so the client's syncAssignedAccess call (task 142) is no
      // longer made for this create. A server warning (a secure create not yet complete) is shown like any other.
      const created = await createRecordViaBffWithWarnings(
        this._authenticatedFetch,
        this._bffBaseUrl,
        'sprk_workassignment',
        entity,
        mappingSource ? { mappingSource } : undefined
      );
      workAssignmentId = created.id;
      warnings.push(...created.warnings);
      console.info('[WorkAssignmentService] create success, workAssignmentId:', workAssignmentId);
    } catch (err) {
      console.error('[WorkAssignmentService] createRecord error:', err);
      // #2382: a privilege refusal becomes one plain sentence; the full error is logged above.
      const described = describeWebApiCreateError(err, 'work assignment');
      const message = described.message;
      return {
        status: 'error',
        errorMessage: described.isAccessDenied ? message : `Failed to create work assignment record: ${message}`,
        warnings: [],
      };
    }

    // -- Step 2: Upload files to SPE -----------------------------------------
    //
    // Task 076: keyed on the WORK ASSIGNMENT. `sprk_workassignment` was added to
    // `EntityAccessFilter.EntitySetByType` by item 7 (`f85796f70`) with its `sprk_document` lookup
    // column verified present against live Dataverse metadata, so the record-keyed route resolves
    // rather than denying for this type.
    if (uploadedFiles.length > 0) {
      const uploadResult = await this._entityService.uploadFilesToSpe(
        'sprk_workassignment',
        workAssignmentId,
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
          const docNavProps = await discoverNavProps('sprk_document');
          // The parent column `sprk_workassignment`, by exact column: sprk_document also has
          // `sprk_relatedworkassignment`, and the Work Assignment form's Documents list reads the parent column.
          const waNavProp = findNavProp(docNavProps, 'sprk_workassignment', 'sprk_workassignment');
          if (!waNavProp) {
            console.warn('[WorkAssignmentService] No nav-prop found on sprk_document for sprk_workassignment');
          }

          const createResult = await this._entityService.createDocumentRecords(
            'sprk_workassignments',
            workAssignmentId,
            waNavProp ?? 'sprk_WorkAssignment',
            uploadResult.uploadedFiles,
            {
              // No `containerId` — `sprk_graphdriveid` comes from the server's upload response.
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
    }
    // (Ontology task 046: a former `else if (uploadedFiles.length > 0 && !this._containerId)` branch was unreachable — its
    // condition is covered by the `if` above, eslint no-dupe-else-if, which blocks a commit of this file — and lost its
    // meaning when task 076 made the server pick the container. Removed; no behaviour changes.)

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
  ): Promise<{ success: boolean; warning?: string; warnings?: string[] }> {
    try {
      const navProps = await discoverNavProps('sprk_event');

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
      const waNavProp = findNavProp(navProps, 'sprk_workassignment', 'sprk_regardingworkassignment');
      if (waNavProp) {
        entity[`${waNavProp}@odata.bind`] = `/sprk_workassignments(${cleanGuid(workAssignmentId)})`;
      }

      // Assigned To (contact) -- task 097. `sprk_event.sprk_assignedto` targets CONTACT
      // in the live schema (nav prop `sprk_AssignedTo`). The former
      // `findNavProp(navProps, 'systemuser', 'assignedto')` matched no assignedto
      // column, fell back to the FIRST systemuser lookup (`createdby`) and bound the
      // picked user there -- the assignee was silently dropped on every follow-on
      // event. Resolved by exact column (sprk_event has five contact lookups whose
      // names contain "assignedto"), never by substring.
      if (eventState.assignedToId) {
        const assignedNavProp = toNavPropMap(navProps)['sprk_assignedto'];
        if (assignedNavProp) {
          entity[`${assignedNavProp}@odata.bind`] = `/contacts(${cleanGuid(eventState.assignedToId)})`;
        } else {
          console.warn(
            '[WorkAssignmentService] sprk_event.sprk_assignedto nav-prop not found; follow-on event assignee not set'
          );
        }
      }

      // On the BFF route like the work assignment (FR-16): the event is filed under the work assignment, which is also its
      // mapping source (sprk_event is not in InferMappingSourceTables, so the source must be named). The event is created
      // AFTER the work assignment, in the same order as before. A server warning (a mapped value not written) comes back.
      const created = await createRecordViaBffWithWarnings(
        this._authenticatedFetch,
        this._bffBaseUrl,
        'sprk_event',
        entity,
        { mappingSource: { entity: 'sprk_workassignment', id: workAssignmentId } }
      );
      return { success: true, warnings: created.warnings };
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
   * Send the composed email (task 182, D-219) through the composer's request path — From, To, Cc, Bcc, attachments and
   * the composed body format — filed under the work assignment. Never throws; a failure is the returned warning.
   */
  async sendEmail(
    workAssignmentId: string,
    workAssignmentName: string,
    email: IComposedEmail,
    onResolveShareLink?: (documentId: string) => Promise<string | null>
  ): Promise<{ success: boolean; warning?: string }> {
    return sendComposedEmail(email, {
      associations: [{ entityType: 'sprk_workassignment', entityId: workAssignmentId, entityName: workAssignmentName }],
      authenticatedFetch: this._authenticatedFetch,
      bffBaseUrl: this._bffBaseUrl,
      onResolveShareLink,
    });
  }
}
