/**
 * recordReads — the record detail reads, shared by the R3 `RecordDetailModal` and the R4 `RecordTemplateModal`
 * (spaarke-SPA-external-access-platform-r4 task 003, FR-01 / FR-02).
 *
 * MOVED, NOT CHANGED: `buildFetchXml`, `postFetch`, `useRecord` and `useRelatedRows` (and the private helpers they need)
 * are the code that lived in `detail/RecordDetailModal.tsx` up to R4 task 003, byte for byte apart from `export`. No new
 * server read and no change to what is fetched: every call is the external module read seam
 * (POST /api/v1/external/api/dataverse/fetch) through `bffApiCall`, single-entity, join-free, allow-listed columns only.
 * Why the fetch seam and not the record route: see `detail/RecordDetailFields.tsx`.
 */
import * as React from 'react';
import { bffApiCall } from '../../auth/bff-client';
import { ApiError } from '../../types';
import type { DetailRecord, RecordDetailEntity } from '../detail/RecordDetailFields';

/** The module read seam, mounted under the external group (`ExternalModuleDataEndpoints`). */
const EXTERNAL_DATA_PATH = '/api/v1/external/api/dataverse';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The module fetch response (`FetchResponseDto`). */
export interface FetchResult {
  entities?: DetailRecord[];
  moreRecords?: boolean;
}

function xmlAttr(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/**
 * Single-entity, join-free FetchXML over allow-listed columns only — the only shape the seam's guard admits.
 * Every interpolated value is a GUID checked by the caller or a constant from this file, and is escaped anyway.
 */
export function buildFetchXml(
  entity: string,
  attributes: readonly string[],
  where: { attribute: string; value: string },
  orderBy?: { attribute: string; descending: boolean }
): string {
  const attrs = attributes.map(a => `<attribute name="${xmlAttr(a)}"/>`).join('');
  const order = orderBy
    ? `<order attribute="${xmlAttr(orderBy.attribute)}" descending="${orderBy.descending ? 'true' : 'false'}"/>`
    : '';
  return (
    `<fetch><entity name="${xmlAttr(entity)}">${attrs}` +
    `<filter type="and"><condition attribute="${xmlAttr(where.attribute)}" operator="eq" value="${xmlAttr(where.value)}"/></filter>` +
    `${order}</entity></fetch>`
  );
}

export function postFetch(entity: string, fetchXml: string): Promise<FetchResult> {
  return bffApiCall<FetchResult>(`${EXTERNAL_DATA_PATH}/fetch`, {
    method: 'POST',
    body: JSON.stringify({ entityName: entity, fetchXml }),
  });
}

/** A thrown read that means "the caller may not see this record". */
class DeniedError extends Error {}

/** Reads one record's fields through the id-filtered module fetch. Throws DeniedError / ApiError. */
async function readRecord(cfg: RecordDetailEntity, id: string): Promise<DetailRecord> {
  const columns = Array.from(new Set([...cfg.fields.map(f => f.attribute), ...(cfg.extraColumns ?? [])]));
  const result = await postFetch(
    cfg.entity,
    buildFetchXml(cfg.entity, [cfg.idAttribute, ...columns], { attribute: cfg.idAttribute, value: id })
  );
  const row = result.entities?.[0];
  // The fetch seam returns only rows inside the caller's Tier-2 set, so no row means "not yours" (or not there).
  if (!row) throw new DeniedError('record not in the caller set');
  return row;
}

export type RecordState =
  | { status: 'loading' }
  | { status: 'ready'; record: DetailRecord }
  | { status: 'denied' }
  | { status: 'notfound' }
  | { status: 'error'; message: string; retryable: boolean };

/** Loads the record for `id`; a state is only ever shown for the id it was read for (no flash of the previous record). */
export function useRecord(cfg: RecordDetailEntity | undefined, id: string | undefined, attempt: number): RecordState {
  const [loaded, setLoaded] = React.useState<{ key: string; state: RecordState } | null>(null);
  const key = `${cfg?.entity ?? ''}:${id ?? ''}:${attempt}`;

  React.useEffect(() => {
    if (!cfg || !id) return;
    let cancelled = false;
    const done = (state: RecordState): void => {
      if (!cancelled) setLoaded({ key, state });
    };
    if (!GUID.test(id)) {
      // A malformed id is never sent anywhere, and retrying cannot make it valid.
      done({ status: 'error', message: 'This record could not be opened.', retryable: false });
      return () => {
        cancelled = true;
      };
    }
    readRecord(cfg, id).then(
      record => done({ status: 'ready', record }),
      (err: unknown) => {
        if (err instanceof DeniedError || (err instanceof ApiError && err.statusCode === 403)) {
          done({ status: 'denied' });
        } else if (err instanceof ApiError && err.statusCode === 404) {
          done({ status: 'notfound' });
        } else {
          done({ status: 'error', message: 'This record could not be loaded just now.', retryable: true });
        }
      }
    );
    return () => {
      cancelled = true;
    };
  }, [cfg, id, key]);

  if (!cfg || !id) return { status: 'notfound' };
  return loaded && loaded.key === key ? loaded.state : { status: 'loading' };
}

export type ListState =
  | { status: 'loading' }
  | { status: 'ready'; rows: DetailRecord[]; truncated: boolean }
  | { status: 'error' };

/** The Tier-2-scoped module read the matching tab runs, narrowed to the rows tied to this record. */
export function useRelatedRows(
  childEntity: 'sprk_invoice',
  attributes: readonly string[],
  parentAttribute: string | undefined,
  parentId: string,
  orderBy: { attribute: string; descending: boolean }
): ListState {
  const [loaded, setLoaded] = React.useState<{ key: string; state: ListState } | null>(null);
  const key = `${childEntity}:${parentAttribute ?? ''}:${parentId}`;
  const attrsKey = attributes.join(',');

  React.useEffect(() => {
    if (!parentAttribute) return;
    let cancelled = false;
    const done = (state: ListState): void => {
      if (!cancelled) setLoaded({ key, state });
    };
    postFetch(
      childEntity,
      buildFetchXml(childEntity, attrsKey.split(','), { attribute: parentAttribute, value: parentId }, orderBy)
    ).then(
      result => done({ status: 'ready', rows: result.entities ?? [], truncated: result.moreRecords === true }),
      () => done({ status: 'error' })
    );
    return () => {
      cancelled = true;
    };
    // orderBy is a module constant at every call site.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [childEntity, parentAttribute, parentId, attrsKey, key]);

  return loaded && loaded.key === key ? loaded.state : { status: 'loading' };
}
