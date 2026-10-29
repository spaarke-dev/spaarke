import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';
import type { EntitySearchResult, EntityType } from '../hooks/useEntitySearch';
import { describeFetchFailure } from '../utils/errorMessages';

/**
 * The pane's record-picker search: one type, `GET /api/office/search/entities` (task 133).
 *
 * Moved out of `SaveFlow.relatedSearch` unchanged so the Save picker and the Invoice view (task 133) run the same
 * search rather than two copies of it. SaveFlow calls it with `access: 'file'`; its request URL, errors and field
 * map are exactly what they were inside SaveFlow (pinned by `SaveFlow.filingAccess.test.tsx` and
 * `SaveFlow.quickCreateErrorSurfacing.test.tsx`).
 *
 * Task 053: a failure THROWS a descriptive Error (the server's `detail` when the response is ProblemDetails-shaped, a
 * status-aware fallback otherwise, via `describeFetchFailure`); `RelatedToPicker.runSearch` shows it distinctly from
 * an empty result set, with a Retry. A genuinely empty search resolves to `[]`.
 *
 * Task 084 (#1037, "pickable equals savable"): `access: 'file'` asks the server whether the caller may FILE to each
 * row; each row's `canFile` is carried to the picker, which shows a `canFile === false` row disabled with the reason.
 */
export interface OfficeEntitySearchOptions {
  apiBaseUrl: string | undefined;
  getAccessToken: (() => Promise<string>) | undefined;
  /** `'file'` adds `access=file` (filing access per row). Absent → the plain read search. */
  access?: 'file';
}

export async function searchOfficeEntities(
  query: string,
  type: EntityType,
  { apiBaseUrl, getAccessToken, access }: OfficeEntitySearchOptions
): Promise<EntitySearchResult[]> {
  if (!apiBaseUrl || !getAccessToken) {
    throw new Error("Couldn't search: the pane isn't fully configured. Reload and try again.");
  }

  let res: Response;
  try {
    const token = await getAccessToken();
    res = await authenticatedJsonFetch(
      `${apiBaseUrl}/api/office/search/entities?q=${encodeURIComponent(query)}&type=${type}&top=10${
        access === 'file' ? '&access=file' : ''
      }`,
      { headers: { 'Content-Type': 'application/json' } },
      token,
      { getRetryToken: getAccessToken }
    );
  } catch {
    // Network/token failure before any response existed — never silently "no matches".
    throw new Error("Couldn't reach Spaarke to search. Check your connection and try again.");
  }

  if (!res.ok) {
    const errorMsg = await describeFetchFailure(res);
    throw new Error(errorMsg.message);
  }

  const data = await res.json();
  const rows = (data.results ?? []) as Array<{
    id: string;
    entityType: string;
    logicalName: string;
    name: string;
    displayInfo?: string;
    canFile?: boolean | null;
  }>;
  // An EXPLICIT field map: any field not listed here is dropped. `canFile` must stay listed, or a
  // record the save would refuse renders as selectable again (task 084 pins this with a test).
  return rows.map(item => ({
    id: item.id,
    entityType: item.entityType as EntityType,
    logicalName: item.logicalName,
    name: item.name,
    ...(item.displayInfo ? { displayInfo: item.displayInfo } : {}),
    ...(item.canFile !== undefined ? { canFile: item.canFile } : {}),
  }));
}
