import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';

/**
 * matterTypeLookupService.ts
 *
 * spaarkeai-word-add-in-r1 task 038: the pane's Matter quick-create must always collect a Matter Type
 * (owner decision 2026-09-11) and send it as `matterTypeId`. The type list comes from
 * `GET /api/office/search/matter-types` — a small, load-once reference list (5 rows in dev), NOT the
 * 2-character-minimum typeahead `/api/office/search/entities` uses. See
 * `projects/spaarkeai-word-add-in-r1/notes/038-quick-create-matter-type.md` §11 for the full
 * extend-vs-create analysis.
 *
 * @see src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs (GetMatterTypesAsync)
 * @see src/server/api/Sprk.Bff.Api/Models/Office/MatterTypeListResponse.cs
 */

/** One `sprk_mattertype_ref` row for the Matter Type dropdown. */
export interface MatterTypeChoice {
  id: string;
  name: string;
  code?: string;
}

/** Wire shape of one row in `GET /api/office/search/matter-types` (camelCase). */
interface MatterTypeOptionWire {
  id: string;
  name: string;
  code?: string;
}

/** Wire shape of the endpoint's response body. */
interface MatterTypeListResponseWire {
  results: MatterTypeOptionWire[];
}

/**
 * Fetches the active Matter Type reference rows, once, for the required dropdown on Matter
 * quick-create. Never throws for a non-2xx response or malformed body — treated as "no matter types
 * available" so the caller can render the honest "couldn't load matter types" state rather than an
 * unhandled rejection; the field's required-ness is still enforced client-side (an empty list simply
 * means the create can never be submitted until the list loads).
 *
 * @param apiBaseUrl BFF base URL.
 * @param token Already-acquired access token for the first attempt.
 * @param getRetryToken Re-acquires a token for the single 401 retry (`authenticatedJsonFetch` contract).
 * @returns The active matter types, ordered by name (server-ordered; not re-sorted here), or `[]`.
 */
export async function fetchMatterTypes(
  apiBaseUrl: string,
  token: string,
  getRetryToken: () => Promise<string>
): Promise<MatterTypeChoice[]> {
  try {
    const res = await authenticatedJsonFetch(
      `${apiBaseUrl}/api/office/search/matter-types`,
      { headers: { 'Content-Type': 'application/json' } },
      token,
      { getRetryToken }
    );
    if (!res.ok) return [];

    const data = (await res.json()) as MatterTypeListResponseWire;
    if (!Array.isArray(data.results)) return [];

    return data.results
      .filter((r): r is MatterTypeOptionWire => typeof r?.id === 'string' && typeof r?.name === 'string')
      .map(r => ({ id: r.id, name: r.name, ...(r.code ? { code: r.code } : {}) }));
  } catch {
    return [];
  }
}
