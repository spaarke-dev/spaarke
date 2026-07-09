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
  _resetNavPropCacheForTests,
} from '../../services/PolymorphicResolverService';
import type { IPolymorphicWebApi } from '../../services/PolymorphicResolverService';
import { TODO_REGARDING_CATALOG } from '../../services/TodoRegardingUpdateBuilder';
import { applyFieldMappings } from '../../services/FieldMappingService';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';

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
   * Create a `sprk_todo` Dataverse record.
   *
   * Builds a `sprk_todo` entity payload from the wizard form state and,
   * when a regarding triple is supplied, atomically populates the 11+4
   * regarding fields per ADR-024 before issuing the create call.
   *
   * @param formValues — Captured form state from the wizard.
   * @param regarding — Optional AssociateToStep selection (null/undefined means "skipped").
   *
   * @returns A `ICreateTodoResult` — never throws.
   */
  async createTodo(formValues: ICreateTodoFormState, regarding?: AssociationResult | null): Promise<ICreateTodoResult> {
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
    entity['sprk_priorityscore'] = formValues.priorityScore;
    entity['sprk_effortscore'] = formValues.effortScore;

    // 2. Assignee lookup (sprk_assignedto → contact). UAT 2026-06-21:
    //    sprk_todo.sprk_assignedto was migrated from a systemuser lookup to
    //    the OOB `contact` (Person) entity. The wizard's Assignee picker
    //    now feeds a CONTACT GUID via `searchContactsAsLookup`. Bind set
    //    name is `contacts` (plural of the OOB contact table).
    if (formValues.assignedToId) {
      try {
        const navProps = await discoverNavProps('sprk_todo');
        const assignedNav = navProps.find(
          n => n.referencedEntity === 'contact' && n.columnName.toLowerCase().includes('assignedto')
        );
        if (assignedNav) {
          entity[`${assignedNav.navPropName}@odata.bind`] = `/contacts(${formValues.assignedToId})`;
        } else {
          // Fallback: use the lookup attribute name directly
          // UAT 2026-06-22 round 13: PascalCase nav-prop name required
          entity['sprk_AssignedTo@odata.bind'] = `/contacts(${formValues.assignedToId})`;
        }
      } catch (err) {
        console.warn('[TodoService] Failed to resolve sprk_assignedto nav-prop, using fallback:', err);
        // UAT 2026-06-22 round 13: PascalCase nav-prop name required
        entity['sprk_AssignedTo@odata.bind'] = `/contacts(${formValues.assignedToId})`;
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
    try {
      const todoId = await this._dataService.createRecord('sprk_todo', entity);
      return {
        todoId,
        todoName: formValues.title.trim(),
        success: true,
        warnings,
      };
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
  }
}
