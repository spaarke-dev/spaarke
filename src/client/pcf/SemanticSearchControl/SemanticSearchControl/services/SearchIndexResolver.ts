/**
 * SearchIndexResolver — Phase G (v1.1.75) replacement for the v1.1.74 bound
 * manifest property `searchIndexName`.
 *
 * The host record's `sprk_ai_search_index` lookup column points at a row in
 * the `sprk_aisearchindex` catalog table. This module reads the linked
 * `sprk_searchindexname` value via `context.webAPI` so the PCF does not need
 * a manifest binding.
 *
 * Lives in `services/` (NOT inline in `SemanticSearchControl.tsx`) so the
 * resolver can be unit-tested in isolation — importing
 * `SemanticSearchControl.tsx` transitively pulls ESM-only `@spaarke/ui-components`
 * dependencies that ts-jest cannot transform.
 *
 * @see ../SemanticSearchControl.tsx (calls resolveSearchIndexNameAsync on mount)
 * @see projects/spaarke-multi-container-multi-index-r1/notes/phase-g/spec.md §3.1, §5
 */

import { IInputs } from '../generated/ManifestTypes';

/**
 * Resolve the Azure AI Search index name from the host record's
 * `sprk_ai_search_index` lookup column.
 *
 * Replaces the v1.1.74 bound-property approach (manifest `searchIndexName` was
 * a SingleLine.Text bound property fed from `sprk_searchindexname`). In v1.1.75
 * the host entities expose a **lookup** column (`sprk_ai_search_index`) to the
 * `sprk_aisearchindex` catalog table; the PCF resolves the linked
 * `sprk_searchindexname` itself via `context.webAPI` on init.
 *
 * @param context PCF context. `context.mode.contextInfo` provides
 *   `entityTypeName` (e.g. `sprk_matter`) and `entityId` (GUID). Either being
 *   missing → returns `null`, which the downstream consumers treat as "use BFF
 *   tenant default" (omit-on-empty contract per tasks 031/032).
 * @returns The Azure AI Search index name (e.g. `spaarke-knowledge-index-v2`),
 *   or `null` if the lookup is unset, the record can't be read, or the host
 *   isn't on an entity form.
 *
 * Behaviour contract:
 *   - Reads the lookup via `_sprk_ai_search_index_value` +
 *     `$expand=sprk_ai_search_index($select=sprk_searchindexname)`.
 *   - Returns `null` on any failure — BFF tenant default takes over.
 *   - Pure read; no writes; no side-effects beyond the WebAPI call.
 *
 * @see projects/spaarke-multi-container-multi-index-r1/notes/phase-g/spec.md §3.1, §5
 */
export async function resolveSearchIndexNameAsync(
  context: ComponentFramework.Context<IInputs>
): Promise<string | null> {
  // `context.mode.contextInfo` exists at runtime but is not in the
  // @types/powerapps-component-framework typings — cast through unknown.
  const contextInfo = (
    context.mode as unknown as {
      contextInfo?: { entityTypeName?: string; entityId?: string };
    }
  ).contextInfo;
  const entityType = contextInfo?.entityTypeName ?? null;
  const entityId = contextInfo?.entityId ?? null;
  if (!entityType || !entityId) {
    // Not on a record form (e.g. dashboard or 'all' scope) → no host record
    // to derive from. BFF tenant default applies.
    return null;
  }

  try {
    // Phase G — Schema note (spec §3.1): the column on host entities is
    // `sprk_ai_search_index` (NOT `sprk_aisearchindexid`, which was the spec's
    // placeholder text). The OData wire form for a lookup column is
    // `_<schemaname>_value`; the navigation property used in $expand is the
    // schema name itself.
    const record = (await context.webAPI.retrieveRecord(
      entityType,
      entityId,
      '?$select=_sprk_ai_search_index_value' + '&$expand=sprk_ai_search_index($select=sprk_searchindexname)'
    )) as unknown as {
      sprk_ai_search_index?: { sprk_searchindexname?: string } | null;
    };

    return record?.sprk_ai_search_index?.sprk_searchindexname ?? null;
  } catch {
    // Lookup column may not exist yet (mid-Phase-G migration), the user may
    // lack read on the linked catalog row, or the WebAPI call may fail
    // transiently. Any of these → null → BFF tenant default applies.
    return null;
  }
}
