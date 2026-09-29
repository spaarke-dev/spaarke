/**
 * todoService.ts
 * To Do creation service for the Create New To Do wizard (R3 — targets `sprk_todo`).
 *
 * Per smart-todo-decoupling-r3 spec FR-15 / OS-1:
 *   - Creates a first-class `sprk_todo` Dataverse record.
 *   - The legacy `sprk_event` + `sprk_todoflag=true` model has been retired.
 *   - No backward-compat shim — `sprk_eventtodo` is not written under any circumstances.
 *
 * Regarding (multi-entity resolution per ADR-024):
 *   - When the wizard's AssociateToStep returns a triple, this service calls
 *     `applyResolverFields` to atomically populate the entity-specific lookup
 *     + four resolver fields (sprk_regardingrecordtype, sprk_regardingrecordid,
 *     sprk_regardingrecordname, sprk_regardingrecordurl).
 *   - When the user skips AssociateToStep, no regarding fields are written —
 *     all 11 lookups remain null and all 4 resolver fields remain null.
 *
 * Dependencies are injected via constructor — no solution-specific imports.
 *
 * @see src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 */

import type { ICreateTodoFormState, AssociationResult } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import {
  applyResolverFields,
  discoverNavProps,
  toNavPropMap,
  cleanGuid,
  _resetNavPropCacheForTests,
} from '../../services/PolymorphicResolverService';
import type { IPolymorphicWebApi } from '../../services/PolymorphicResolverService';
import { TODO_REGARDING_CATALOG } from '../../services/TodoRegardingUpdateBuilder';
import { applyFieldMappings } from '../../services/FieldMappingService';
import { EntityCreationService } from '../../services/EntityCreationService';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';

// ---------------------------------------------------------------------------
// Result type
// ---------------------------------------------------------------------------

export interface ICreateTodoResult {
  todoId?: string;
  todoName?: string;
  success: boolean;
  errorMessage?: string;
  /**
   * Non-fatal diagnostics accumulated during creation — currently only the
   * Field Mapping Framework engine (task 021 / FR-12). Empty/undefined when
   * nothing warned.
   */
  warnings?: string[];
}

// ---------------------------------------------------------------------------
// Nav-prop discovery for sprk_todo
// ---------------------------------------------------------------------------

// Nav-prop discovery is provided by the shared `discoverNavProps`
// (PolymorphicResolverService) — consolidated 2026-07-09 (task 011, Path A).
// The shared function preserves this service's `fetchImpl` test seam via its
// optional `fetchImpl` parameter (default global `fetch`, resolved at call
// time); the todoService tests stub `globalThis.fetch`, which the default picks up.

/**
 * @internal — for tests, reset the shared module-level nav-prop cache between
 * cases. Delegates to the shared `_resetNavPropCacheForTests` (clears the whole
 * shared cache — a superset of the previous `sprk_todo` clear; safe for test
 * isolation).
 */
export function _resetTodoServiceNavPropCacheForTests(): void {
  _resetNavPropCacheForTests();
}

// ---------------------------------------------------------------------------
// TodoService
// ---------------------------------------------------------------------------

export class TodoService {
  constructor(
    private readonly _dataService: IDataService,
    /**
     * Injected BFF-authenticated fetch — required only for the Field Mapping
     * Framework engine call (task 021 / FR-12). Optional for back-compat with
     * existing callers (e.g. the follow-on `createTodoRegardingChild` path)
     * that don't yet thread these deps through; when omitted, the engine call
     * is skipped (graceful no-op — same as "no profile configured").
     */
    private readonly _authenticatedFetch?: AuthenticatedFetchFn,
    /** BFF API base URL — see `_authenticatedFetch` doc above. */
    private readonly _bffBaseUrl?: string
  ) {}

  /**
   * Lazily-built `EntityCreationService` used for the upload → link sequence (task 119 /
   * ISS-027). Built on first use, not in the constructor, because `_authenticatedFetch` /
   * `_bffBaseUrl` are optional (the `createTodoRegardingChild` follow-on path constructs this
   * service with neither, and never attaches files). Returns `undefined` when either dependency
   * is missing — the caller treats that as a graceful "upload not configured" no-op, the same
   * contract the field-mapping engine call already uses below.
   */
  private _entityService?: EntityCreationService;
  private _getEntityCreationService(): EntityCreationService | undefined {
    if (!this._authenticatedFetch || !this._bffBaseUrl) {
      return undefined;
    }
    if (!this._entityService) {
      const dataService = this._dataService;
      // EntityCreationService expects IWebApiWithCreate (createRecord returning { id }).
      // Same adapter shape as MatterService's constructor (matterService.ts:142-159) and
      // TodoWizardDialog's existing webApiAdapter for the send-email path.
      const webApiAdapter = {
        createRecord: async (entityName: string, data: Record<string, unknown>) => {
          const id = await dataService.createRecord(entityName, data);
          return { id };
        },
        retrieveRecord: (entityName: string, id: string, options?: string) =>
          dataService.retrieveRecord(entityName, id, options),
        retrieveMultipleRecords: (entityName: string, options?: string) =>
          dataService.retrieveMultipleRecords(entityName, options),
      };
      this._entityService = new EntityCreationService(webApiAdapter, this._authenticatedFetch, this._bffBaseUrl);
    }
    return this._entityService;
  }

  /**
   * Create a `sprk_todo` Dataverse record.
   *
   * Builds a `sprk_todo` entity payload from the wizard form state and,
   * when a regarding triple is supplied, atomically populates the 11+4
   * regarding fields per ADR-024 before issuing the create call.
   *
   * After the record exists, uploads any attached files to SPE and creates one
   * `sprk_document` row per file, linked through the discovered `sprk_relatedtodo`
   * nav-prop (task 119 / ISS-027). Mirrors `MatterService.createMatter`'s
   * upload → discover nav-prop → createDocumentRecords sequence
   * (matterService.ts:356-408): create-first (`uploadFilesToSpe` requires the
   * record id — EntityCreationService.ts:479-484), never throws on a failed
   * upload or a failed document-record create — both degrade to entries in
   * `warnings` instead.
   *
   * @param formValues — Captured form state from the wizard.
   * @param regarding — Optional AssociateToStep selection (null/undefined means "skipped").
   * @param uploadedFiles — Files attached in the wizard's "Add file(s)" step. Omitted/empty
   *   means the upload → link sequence is skipped entirely (zero-files path is unchanged).
   *
   * @returns A `ICreateTodoResult` — never throws.
   */
  async createTodo(
    formValues: ICreateTodoFormState,
    regarding?: AssociationResult | null,
    uploadedFiles: IUploadedFile[] = []
  ): Promise<ICreateTodoResult> {
    // Non-fatal diagnostics accumulated during creation (currently just the
    // field-mapping engine, task 021).
    const warnings: string[] = [];

    // 1. Build core sprk_todo entity body (scalar fields only)
    const entity: Record<string, unknown> = {
      sprk_name: formValues.title.trim(),
      // Open / Active (per entity-schema.md: statuscode=1 Open, statecode=0 Active)
      statecode: 0,
      statuscode: 1,
    };

    if (formValues.notes?.trim()) {
      entity['sprk_notes'] = formValues.notes.trim();
    }
    if (formValues.dueDate) {
      entity['sprk_duedate'] = formValues.dueDate;
    }
    // priority / effort scores are 0-100 integers — write only if non-default
    // (we always write so the column reflects the form, even at default 50)
    // Values originate from `CreateTodoStep.tsx`'s Priority/Effort choice
    // dropdowns resolved through the shared `todoScoreMappings.ts` table
    // (smart-todo-r5 task 011 / FR-02/FR-03) — this write path is otherwise
    // UNCHANGED (still plain `priorityScore`/`effortScore` numbers on
    // `ICreateTodoFormState`; the composite `todoScoring.ts` formula is not
    // touched here).
    entity['sprk_priorityscore'] = formValues.priorityScore;
    entity['sprk_effortscore'] = formValues.effortScore;

    // 2. Assignee lookup (sprk_assignedto → contact). UAT 2026-06-21:
    //    sprk_todo.sprk_assignedto was migrated from a systemuser lookup to
    //    the OOB `contact` (Person) entity. The wizard's Assignee picker
    //    now feeds a CONTACT GUID via `searchContactsAsLookup`. Bind set
    //    name is `contacts` (plural of the OOB contact table).
    if (formValues.assignedToId) {
      // cleanGuid: normalize brace-wrapped GUIDs (Xrm pickers / pre-fill) so the
      // @odata.bind key predicate is valid (Dataverse 400 "query syntax" otherwise).
      const assignedToGuid = cleanGuid(formValues.assignedToId);
      try {
        const navProps = await discoverNavProps('sprk_todo');
        const assignedNav = navProps.find(
          n => n.referencedEntity === 'contact' && n.columnName.toLowerCase().includes('assignedto')
        );
        if (assignedNav) {
          entity[`${assignedNav.navPropName}@odata.bind`] = `/contacts(${assignedToGuid})`;
        } else {
          // Fallback: use the lookup attribute name directly
          // UAT 2026-06-22 round 13: PascalCase nav-prop name required
          entity['sprk_AssignedTo@odata.bind'] = `/contacts(${assignedToGuid})`;
        }
      } catch (err) {
        console.warn('[TodoService] Failed to resolve sprk_assignedto nav-prop, using fallback:', err);
        // UAT 2026-06-22 round 13: PascalCase nav-prop name required
        entity['sprk_AssignedTo@odata.bind'] = `/contacts(${assignedToGuid})`;
      }
    }

    // 3. Regarding (multi-entity resolution per ADR-024) — only when supplied
    if (regarding && regarding.entityType && regarding.recordId) {
      const catalogEntry = TODO_REGARDING_CATALOG.find(c => c.entityType === regarding.entityType);
      if (!catalogEntry) {
        return {
          success: false,
          errorMessage:
            `Unsupported regarding entity type "${regarding.entityType}". ` +
            `Must be one of: ${TODO_REGARDING_CATALOG.map(c => c.entityType).join(', ')}.`,
        };
      }

      try {
        const navProps = await discoverNavProps('sprk_todo');

        // Wrap IDataService to match IPolymorphicWebApi shape expected by applyResolverFields.
        const polyWebApi: IPolymorphicWebApi = {
          retrieveMultipleRecords: (entityLogicalName: string, query: string) =>
            this._dataService.retrieveMultipleRecords(entityLogicalName, query),
        };

        await applyResolverFields(
          polyWebApi,
          entity,
          navProps,
          catalogEntry.entityType,
          catalogEntry.entitySet,
          regarding.recordId,
          regarding.recordName,
          catalogEntry.navPropHint
        );
      } catch (err) {
        console.error('[TodoService] applyResolverFields failed:', err);
        const message = err instanceof Error ? err.message : 'Unknown error';
        return {
          success: false,
          errorMessage: `Failed to apply regarding fields: ${message}`,
        };
      }

      // 3b. Field-mapping engine (task 021 / FR-12): apply any configured
      // `sprk_fieldmappingprofile` rules for {parent → sprk_todo} onto the
      // entity payload. Runs AFTER applyResolverFields and BEFORE
      // createRecord, on the SAME `entity` payload object — same insertion
      // point + warning-append + graceful no-op contract as task 020.
      // Orthogonal to the To Do regarding-catalog above (a different
      // mechanism — spec §7 note 6); deliberately NOT wrapped in a try/catch
      // here — `applyFieldMappings` never throws, and skipped entirely when
      // the host hasn't wired `authenticatedFetch`/`bffBaseUrl` (constructor
      // optional deps), which is itself a graceful no-op.
      if (this._authenticatedFetch && this._bffBaseUrl) {
        const mappingResult = await applyFieldMappings({
          sourceEntity: catalogEntry.entityType,
          sourceId: regarding.recordId,
          targetEntity: 'sprk_todo',
          payload: entity,
          dataService: this._dataService,
          authenticatedFetch: this._authenticatedFetch,
          bffBaseUrl: this._bffBaseUrl,
        });
        warnings.push(...mappingResult.warnings);
      }
    }

    // 4. Create the record — strictly `sprk_todo` (NEVER `sprk_event`)
    let todoId: string;
    try {
      todoId = await this._dataService.createRecord('sprk_todo', entity);
    } catch (err) {
      console.error('[TodoService] createRecord error:', err);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const errObj = err as any;
      const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
      return {
        success: false,
        errorMessage: `Failed to create to do: ${message}`,
      };
    }

    // 5. Upload attached files to SPE + create linked sprk_document records (ISS-027 / task
    //    119). Deliberately OUTSIDE the try/catch above — the to do already exists at this
    //    point, so an upload/link problem must degrade to a warning, never to a report that
    //    the to do failed to create. Create-first, always: uploadFilesToSpe requires the
    //    record id (EntityCreationService.ts:479-484). Skipped entirely when there are no
    //    files — the zero-files path stays byte-identical. Mirrors
    //    MatterService.createMatter's upload → discover nav-prop → createDocumentRecords
    //    sequence (matterService.ts:356-408), which places this step outside its own create
    //    try/catch for the same reason.
    if (uploadedFiles.length > 0) {
      const entityService = this._getEntityCreationService();
      if (!entityService) {
        warnings.push('File upload skipped -- upload is not configured for this to do creation path.');
      } else {
        const uploadResult = await entityService.uploadFilesToSpe('sprk_todo', todoId, uploadedFiles);

        if (!uploadResult.success) {
          warnings.push(
            `File upload failed (${uploadResult.failureCount} of ${uploadedFiles.length}). ` +
              'Files can be added from the to do record.'
          );
        } else if (uploadResult.uploadedFiles.length > 0) {
          // Discover the sprk_document -> sprk_todo nav-prop at runtime. The column is
          // sprk_relatedtodo (Models.cs DocumentLinkFields.All), nav-prop sprk_RelatedToDo —
          // there is no bare `sprk_todo` column and no existing TypeScript call site to copy
          // the string from, so this mirrors matterService.ts:371-372 with a literal fallback.
          const docNavProps = toNavPropMap(await discoverNavProps('sprk_document'));
          const docTodoNavProp = docNavProps['sprk_relatedtodo'] ?? 'sprk_RelatedToDo';

          const linkResult = await entityService.createDocumentRecords(
            'sprk_todos',
            todoId,
            docTodoNavProp,
            uploadResult.uploadedFiles,
            {
              parentRecordName: formValues.title.trim(),
            }
          );
          if (linkResult.warnings.length > 0) {
            warnings.push(...linkResult.warnings);
          }
        }

        if (uploadResult.failureCount > 0 && uploadResult.successCount > 0) {
          warnings.push(
            `${uploadResult.failureCount} file(s) failed to upload: ` +
              uploadResult.errors.map(e => e.fileName).join(', ')
          );
        }
      }
    }

    return {
      todoId,
      todoName: formValues.title.trim(),
      success: true,
      warnings,
    };
  }
}
