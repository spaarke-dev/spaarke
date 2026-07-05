/**
 * discoverMemoNavProps — Query Dataverse metadata for `sprk_memo`'s
 * ManyToOne relationships and cache the resulting nav-prop entries.
 *
 * Why this file exists
 * ────────────────────
 * `PolymorphicResolverService.applyResolverFields()` (from
 * `@spaarke/ui-components/services/PolymorphicResolverService`) requires an
 * `INavPropEntry[]` describing the child entity's ManyToOne relationships.
 * The shared lib does NOT export a `discoverNavProps` helper (a module-private
 * `_discoverNavProps` exists inside `workAssignmentService.ts` / `matterService.ts`
 * / etc., following an identical fetch-based pattern).
 *
 * Per CLAUDE.md §11 (Component Justification — Default to Reuse) I copy the
 * proven pattern into this Notepad-scoped helper rather than duplicate the
 * "consumer must build its own" shape. If a second consumer emerges we can
 * lift this into the shared lib.
 *
 * Pattern lifted directly from
 * `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts`
 * lines 45–87 (identical query, identical mapping, identical caching). The only
 * intentional differences:
 *   - Cache keyed by entity logical name (same shape, module-local Map).
 *   - Exported for testability (workAssignmentService keeps it private).
 *   - Test-only cache-reset export so unit tests can force re-discovery.
 *
 * Non-goals
 * ─────────
 *   - Does NOT depend on `Xrm.WebApi` — metadata queries against
 *     `EntityDefinitions(...)/ManyToOneRelationships` are NOT exposed through
 *     `Xrm.WebApi` in the model-driven-app client library (confirmed by
 *     existing services all using `fetch(url, { credentials: 'include' })`).
 *   - Does NOT throw on failure — returns an empty array. `applyResolverFields`
 *     emits a `console.warn` when a nav-prop isn't found. Downstream `createMemo`
 *     still surfaces the underlying `createRecord` failure to the user, so the
 *     hook layer will detect any real problem.
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts
 */

// DEF-10 (2026-07-04) — deep-path import to skip the services barrel (which
// re-exports `EntityCreationService` → `mammoth` docx parser, ~300 KB).
import type { INavPropEntry } from '@spaarke/ui-components/services/PolymorphicResolverService';

// ---------------------------------------------------------------------------
// Module-local cache
// ---------------------------------------------------------------------------

const _navPropCache: Map<string, INavPropEntry[]> = new Map();

// ---------------------------------------------------------------------------
// Test-only cache control (mirrors TodoRegardingUpdateBuilder's approach)
// ---------------------------------------------------------------------------

/**
 * Reset the module-local nav-prop cache. Test-only surface — production code
 * should never need this. Prefixed with `_` per Spaarke convention for
 * "not-part-of-public-API".
 */
export function _resetMemoNavPropCacheForTests(): void {
  _navPropCache.clear();
}

// ---------------------------------------------------------------------------
// Public helper
// ---------------------------------------------------------------------------

/**
 * Response shape from Dataverse Web API metadata endpoint.
 * Only the three properties we consume are typed; the endpoint returns more.
 */
interface IManyToOneRelationshipRaw {
  ReferencingAttribute: string;
  ReferencingEntityNavigationPropertyName: string;
  ReferencedEntity: string;
}

/**
 * Discover ManyToOne relationships for the given entity via the Dataverse
 * Web API metadata endpoint. Results are cached per-entity for the lifetime
 * of the page.
 *
 * @param entityLogicalName - e.g. `"sprk_memo"`.
 * @returns Array of `INavPropEntry` for use with `applyResolverFields`. Empty
 *          on any error (logged; not thrown).
 */
export async function discoverMemoNavProps(
  entityLogicalName: string
): Promise<INavPropEntry[]> {
  const cached = _navPropCache.get(entityLogicalName);
  if (cached) return cached;

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;

    const resp = await fetch(url, { credentials: 'include' });
    if (!resp.ok) {
      // eslint-disable-next-line no-console
      console.warn(
        `[useSprkMemoRepository] Nav-prop discovery failed for ${entityLogicalName}:`,
        resp.status
      );
      return [];
    }

    const json = (await resp.json()) as { value?: IManyToOneRelationshipRaw[] };
    const rels: IManyToOneRelationshipRaw[] = json.value ?? [];

    const entries: INavPropEntry[] = rels.map(r => ({
      columnName: r.ReferencingAttribute,
      navPropName: r.ReferencingEntityNavigationPropertyName,
      referencedEntity: r.ReferencedEntity,
    }));

    _navPropCache.set(entityLogicalName, entries);
    return entries;
  } catch (err) {
    // eslint-disable-next-line no-console
    console.warn(
      `[useSprkMemoRepository] Nav-prop discovery error for ${entityLogicalName}:`,
      err
    );
    return [];
  }
}
