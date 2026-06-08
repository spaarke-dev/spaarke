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
import { applyResolverFields } from '../../services/PolymorphicResolverService';
import type { INavPropEntry, IPolymorphicWebApi } from '../../services/PolymorphicResolverService';
import { TODO_REGARDING_CATALOG } from '../../services/TodoRegardingUpdateBuilder';

// ---------------------------------------------------------------------------
// Result type
// ---------------------------------------------------------------------------

export interface ICreateTodoResult {
  todoId?: string;
  todoName?: string;
  success: boolean;
  errorMessage?: string;
}

// ---------------------------------------------------------------------------
// Nav-prop discovery for sprk_todo
// ---------------------------------------------------------------------------

/**
 * Cache of discovered ManyToOne nav-props for `sprk_todo`. Keyed by entity
 * logical name. Lifetime = page session.
 */
const _navPropCache: Record<string, INavPropEntry[]> = {};

/**
 * Discover ManyToOne navigation properties for `sprk_todo` via the
 * Dataverse metadata endpoint. Pattern mirrors `WorkAssignmentService`
 * and `TodoRegardingUpdateBuilder`.
 *
 * Internal — exported only via `TodoService.createTodo` invocations.
 */
async function _discoverNavProps(
  entityLogicalName: string,
  fetchImpl: typeof fetch = globalThis.fetch
): Promise<INavPropEntry[]> {
  if (_navPropCache[entityLogicalName]) {
    return _navPropCache[entityLogicalName];
  }

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;

    const resp = await fetchImpl(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[TodoService] Nav-prop discovery failed for ${entityLogicalName}: HTTP ${resp.status}`);
      return [];
    }

    const json = (await resp.json()) as {
      value?: Array<{
        ReferencingAttribute: string;
        ReferencingEntityNavigationPropertyName: string;
        ReferencedEntity: string;
      }>;
    };

    const entries: INavPropEntry[] = (json.value ?? []).map(r => ({
      columnName: r.ReferencingAttribute,
      navPropName: r.ReferencingEntityNavigationPropertyName,
      referencedEntity: r.ReferencedEntity,
    }));

    _navPropCache[entityLogicalName] = entries;
    return entries;
  } catch (err) {
    console.warn(`[TodoService] Nav-prop discovery error for ${entityLogicalName}:`, err);
    return [];
  }
}

/** @internal — for tests, reset the cache between cases. */
export function _resetTodoServiceNavPropCacheForTests(): void {
  delete _navPropCache['sprk_todo'];
}

// ---------------------------------------------------------------------------
// TodoService
// ---------------------------------------------------------------------------

export class TodoService {
  constructor(private readonly _dataService: IDataService) {}

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
  async createTodo(
    formValues: ICreateTodoFormState,
    regarding?: AssociationResult | null
  ): Promise<ICreateTodoResult> {
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

    // 2. Assignee lookup (sprk_assignedto → systemuser)
    if (formValues.assignedToId) {
      try {
        const navProps = await _discoverNavProps('sprk_todo');
        const assignedNav = navProps.find(
          n => n.referencedEntity === 'systemuser' && n.columnName.toLowerCase().includes('assignedto')
        );
        if (assignedNav) {
          entity[`${assignedNav.navPropName}@odata.bind`] = `/systemusers(${formValues.assignedToId})`;
        } else {
          // Fallback: use the lookup attribute name directly
          entity['sprk_assignedto@odata.bind'] = `/systemusers(${formValues.assignedToId})`;
        }
      } catch (err) {
        console.warn('[TodoService] Failed to resolve sprk_assignedto nav-prop, using fallback:', err);
        entity['sprk_assignedto@odata.bind'] = `/systemusers(${formValues.assignedToId})`;
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
        const navProps = await _discoverNavProps('sprk_todo');

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
    }

    // 4. Create the record — strictly `sprk_todo` (NEVER `sprk_event`)
    try {
      const todoId = await this._dataService.createRecord('sprk_todo', entity);
      return {
        todoId,
        todoName: formValues.title.trim(),
        success: true,
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
