/**
 * targetEntityNormalize — Future-proof Choice label → BFF wire-form mapping.
 *
 * Phase G (Lookup-driven multi-index) — see spec §6.5.
 *
 * Design invariant: code MUST NEVER depend on the `sprk_targetentitytype`
 * Choice integer values. Dataverse Choice integers can be renumbered freely
 * (already done once during Phase G design). The only stable reference is
 * the Choice LABEL string, sourced via the OData FormattedValue annotation
 * in `aiSearchIndexService.listActiveSearchIndexes`.
 *
 * The normalize step (`lowercase + strip whitespace`) produces the BFF wire
 * form (e.g., "Work Assignment" → "workassignment"). The BFF tolerates both
 * the short form and the `sprk_` prefixed form on the request body — see
 * `SearchFilterBuilder.cs` for the backward-compat clause.
 *
 * Adding a new entity to the table:
 *   1. Add a Choice option in Dataverse with a URL/identifier-safe label
 *      (letters + spaces only — no special chars).
 *   2. Add a row to `sprk_aisearchindex` with that Choice option selected.
 *   3. No code changes anywhere.
 *
 * @see projects/spaarke-multi-container-multi-index-r1/notes/phase-g/spec.md §6.5
 * @see aiSearchIndexService.ts for the row source
 */

import type { AiSearchIndexRow } from './aiSearchIndexService';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * The single special-case Choice label. When the selected dropdown row has
 * a `sprk_targetentitytypeLabel` that normalizes to this value, the search
 * request fragment uses `scope: 'all'` (tenant-wide) with NO `entityType`
 * filter. All other normalized labels become `scope: 'entity'` +
 * `entityType: <normalized>` per spec §6.5.
 *
 * Exported so tests can reference the same constant.
 */
export const ALL_SENTINEL = 'all';

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Build the BFF wire form from a Choice label.
 *
 *   - `null` / `undefined` / empty / whitespace-only → `null` (no scope info)
 *   - "Matter" → "matter"
 *   - "Work Assignment" → "workassignment"
 *   - "All" → "all" (use `ALL_SENTINEL` to compare)
 *   - "Sub-Type With-Dashes" → "sub-typewith-dashes" (caller's responsibility
 *     to keep labels URL/identifier-safe; this function only strips spaces)
 *
 * The function preserves all characters EXCEPT whitespace and case.
 * Whitespace is collapsed (all runs stripped, not converted to single
 * spaces) to produce a tight identifier.
 *
 * @returns The normalized wire-form string, or `null` for empty input.
 */
export function normalizeTargetEntityLabel(label: string | undefined | null): string | null {
  if (label == null) return null;
  if (typeof label !== 'string') return null;
  const stripped = label.toLowerCase().replace(/\s+/g, '');
  return stripped.length > 0 ? stripped : null;
}

/**
 * Shape of the BFF request fragment derived from a dropdown row.
 *
 * The fragment is meant to be spread (`...fragment`) into the search
 * request-body construction. `entityType` is absent for the `scope: 'all'`
 * case (tenant-wide), present for the entity-specific case. `searchIndexName`
 * is always present (the whole point of the lookup is to set it).
 */
export interface SearchRequestFragment {
  /** Either `'all'` (tenant-wide) or `'entity'` (entity-scoped). */
  scope: 'all' | 'entity';
  /** Present only when `scope === 'entity'`. Wire form (e.g., "matter"). */
  entityType?: string;
  /** Azure AI Search physical index name. */
  searchIndexName: string;
}

/**
 * Build the BFF request fragment from a selected dropdown row.
 *
 * Rules (per spec §6.5):
 *   - Normalized label = `ALL_SENTINEL` → `{ scope: 'all', searchIndexName }`.
 *   - Normalized label is any other non-empty value → `{ scope: 'entity',
 *     entityType: <normalized>, searchIndexName }`.
 *   - Normalized label is `null` (empty/missing label — data-quality issue) →
 *     conservative default `{ scope: 'all', searchIndexName }`. The dropdown
 *     UI already disallows empty labels (rows are seeded with valid labels),
 *     so this branch is a defensive net rather than a designed-for case.
 *
 * @param row The selected row from `listActiveSearchIndexes`.
 * @returns The fragment to spread into the search request body.
 */
export function buildSearchRequestFragment(row: AiSearchIndexRow): SearchRequestFragment {
  const normalized = normalizeTargetEntityLabel(row.sprk_targetentitytypeLabel);
  const searchIndexName = row.sprk_searchindexname;

  // The "All" sentinel — tenant-wide search, no entity scope.
  if (normalized === ALL_SENTINEL) {
    return { scope: 'all', searchIndexName };
  }

  // Empty/missing label — defensive default (data-quality issue).
  //
  // unified-access-control-r2 task 080: this branch is no longer SILENT. A blank
  // `sprk_targetentitytype` label on an `sprk_aisearchindex` row is a configuration
  // defect, and falling back to the widest scope without saying so meant a
  // mis-seeded row quietly changed what the page searched. The fallback itself is
  // retained — throwing here would break the whole page over one bad row, and the
  // BFF now authorizes `scope: 'all'` results row by row against each document's
  // parent, so the widening is filtered rather than tenant-wide. What changes is
  // that the misconfiguration is now visible to whoever is looking.
  if (normalized === null) {
    console.warn(
      '[SemanticSearch] sprk_aisearchindex row has a blank sprk_targetentitytype label; ' +
        `falling back to cross-record scope for index "${searchIndexName}". ` +
        'Fix the Choice label on that row to restore the intended entity scope.'
    );
    return { scope: 'all', searchIndexName };
  }

  // Entity-specific row.
  return {
    scope: 'entity',
    entityType: normalized,
    searchIndexName,
  };
}
