/**
 * aiSearchIndexService — Direct Dataverse Web API access to the
 * `sprk_aisearchindex` catalog table.
 *
 * Phase G (Lookup-driven multi-index) — see spec §6.
 *
 * Why a direct Dataverse call (not via BFF):
 *   The code page already runs inside the Dataverse host session, so the
 *   user's session cookie authorizes the OData WebAPI. Adding a BFF
 *   enumeration endpoint would duplicate logic and require a second auth
 *   surface (BFF OBO) for no gain. Per spec §6 (decision log 2026-06-09).
 *
 * Auth contract:
 *   - Uses `credentials: 'include'` to forward the Dataverse session cookie.
 *   - DOES NOT use `@spaarke/auth.authenticatedFetch` — that helper is for
 *     BFF calls (per ADR-028). Dataverse OAuth in a code-page IS the user's
 *     session cookie.
 *
 * Choice-field handling:
 *   - Request includes the `Prefer: odata.include-annotations` header so the
 *     `@OData.Community.Display.V1.FormattedValue` annotation comes through
 *     for the `sprk_targetentitytype` Choice column. The label string is
 *     then read into `sprk_targetentitytypeLabel`.
 *   - Code MUST NOT depend on the Choice integer (`sprk_targetentitytype`)
 *     — it can be renumbered freely in Dataverse without breaking the wire
 *     format. See `targetEntityNormalize.ts` for the normalize+map step.
 *
 * @see projects/spaarke-multi-container-multi-index-r1/notes/phase-g/spec.md §6
 * @see targetEntityNormalize.ts for the Choice-label → BFF wire-form mapping
 * @see DataverseWebApiService.getOrgUrl for the org URL resolution pattern
 */

import { getOrgUrl } from './DataverseWebApiService';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * One row from the `sprk_aisearchindex` catalog table.
 *
 * `sprk_targetentitytype` is the Choice INTEGER value (not depended upon by
 * code — kept on the type for completeness). The corresponding human label
 * is in `sprk_targetentitytypeLabel`, sourced via the OData FormattedValue
 * annotation. All routing decisions go through the label + normalize step
 * (see `targetEntityNormalize.buildSearchRequestFragment`).
 */
export interface AiSearchIndexRow {
  /** Catalog table primary key (GUID, lower-case dashed form). */
  sprk_aisearchindexid: string;
  /** Human-friendly label shown in the dropdown (e.g., "Development Files 2"). */
  sprk_displayname: string;
  /** Azure AI Search physical index name — the BFF wire value. */
  sprk_searchindexname: string;
  /**
   * Choice INTEGER value of `sprk_targetentitytype`. NOT used for routing
   * decisions — kept on the row for diagnostics only. Routing uses the
   * label + normalize step in `targetEntityNormalize.ts`.
   */
  sprk_targetentitytype: number | null;
  /**
   * Choice LABEL string (e.g., "All", "Matter", "Work Assignment").
   * Sourced from the OData FormattedValue annotation. THIS is the value
   * routing depends on (via `normalizeTargetEntityLabel`).
   */
  sprk_targetentitytypeLabel: string;
  /** True if this row is the tenant fallback when no record/BU specifies. */
  sprk_isdefault: boolean;
  /** Stable sort order for the dropdown (lower first). Missing → 999. */
  sprk_displayorder: number;
}

/**
 * Raw OData response row shape — used internally for the response.value
 * mapping step. Properties intentionally use `unknown` so we don't trust the
 * server response shape at the type-system level.
 */
interface RawOdataRow {
  sprk_aisearchindexid?: string;
  sprk_displayname?: string;
  sprk_searchindexname?: string;
  sprk_targetentitytype?: number;
  sprk_isdefault?: boolean;
  sprk_displayorder?: number;
  // Annotation key — bracket access only (key contains a `@`).
  'sprk_targetentitytype@OData.Community.Display.V1.FormattedValue'?: string;
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Fetch all active (`statecode eq 0`) rows from `sprk_aisearchindex`,
 * ordered by `sprk_displayorder` then `sprk_displayname`.
 *
 * Returns `[]` on network failure / non-OK response / unexpected shape so
 * the UI can render the "no indexes configured" disabled state (spec §6
 * Authorization) without throwing. Errors are logged to console for
 * operator visibility.
 *
 * @returns Promise resolving to the row array (possibly empty).
 */
export async function listActiveSearchIndexes(): Promise<AiSearchIndexRow[]> {
  const url =
    `${getOrgUrl()}/api/data/v9.2/sprk_aisearchindexes` +
    '?$select=sprk_aisearchindexid,sprk_displayname,sprk_searchindexname,' +
    'sprk_targetentitytype,sprk_isdefault,sprk_displayorder' +
    '&$filter=statecode eq 0' +
    '&$orderby=sprk_displayorder,sprk_displayname';

  try {
    const response = await fetch(url, {
      headers: {
        Accept: 'application/json',
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        // The Prefer header is REQUIRED so the FormattedValue annotation
        // (the Choice's human label) flows into the response. Without it
        // the response only carries the integer value of the Choice — and
        // we explicitly do NOT depend on the integer for routing (see
        // targetEntityNormalize.ts and spec §6.5).
        Prefer: 'odata.include-annotations="OData.Community.Display.V1.FormattedValue"',
      },
      credentials: 'include',
    });

    if (!response.ok) {
      console.error(`[aiSearchIndexService] HTTP ${response.status} fetching sprk_aisearchindex.`);
      return [];
    }

    const data = (await response.json()) as { value?: RawOdataRow[] };
    const rawRows = data?.value;
    if (!Array.isArray(rawRows)) {
      console.warn('[aiSearchIndexService] Unexpected response shape — no `value` array.');
      return [];
    }

    return rawRows.map(row => mapRawRow(row));
  } catch (err) {
    console.error('[aiSearchIndexService] Network error fetching sprk_aisearchindex:', err);
    return [];
  }
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/**
 * Map a raw OData row into the strongly-typed `AiSearchIndexRow`.
 *
 * Defensive defaults:
 *   - Missing displayorder → 999 (sorts to end)
 *   - Missing FormattedValue → empty string (downstream normalize treats as
 *     null → no entity scope, no special-case handling — surfaces to UI as
 *     a row with the wrong-looking label, which is a data-quality signal)
 *   - Missing `sprk_isdefault` → false
 */
function mapRawRow(row: RawOdataRow): AiSearchIndexRow {
  return {
    sprk_aisearchindexid: row.sprk_aisearchindexid ?? '',
    sprk_displayname: row.sprk_displayname ?? '',
    sprk_searchindexname: row.sprk_searchindexname ?? '',
    sprk_targetentitytype: typeof row.sprk_targetentitytype === 'number' ? row.sprk_targetentitytype : null,
    sprk_targetentitytypeLabel: row['sprk_targetentitytype@OData.Community.Display.V1.FormattedValue'] ?? '',
    sprk_isdefault: row.sprk_isdefault === true,
    sprk_displayorder: typeof row.sprk_displayorder === 'number' ? row.sprk_displayorder : 999,
  };
}
