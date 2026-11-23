/**
 * recordReads — the record detail reads, used by the R4 `RecordTemplateModal` and its sections
 * (spaarke-SPA-external-access-platform-r4 task 003, FR-01 / FR-02).
 *
 * MOVED, NOT CHANGED: `buildFetchXml`, `postFetch`, `useRecord` and `useRelatedRows` (and the private helpers they need)
 * are the code that lived in R3's record-detail modal (retired in R4 task 005) up to R4 task 003, byte for byte apart
 * from `export`. No new server read and no change to what is fetched: every call is the external module read seam
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

/**
 * A reader's "this record does not exist for the caller" (R4 task 015): the record state is `notfound`. A per-opening reader
 * throws it when the row it looks for is no longer in the list it re-reads (an event deleted or filed away). An
 * `ApiError` 404 means the same; this is for a reader that has no HTTP answer to map.
 */
export class RecordNotFoundError extends Error {}

/** Reads one record by id. The entity's own (`RecordDetailEntity.read`) or one supplied for a single opening. */
export type RecordReader = (id: string) => Promise<DetailRecord>;

export interface UseRecordOptions {
  /**
   * A per-opening reader (R4 task 015): wins over the entity's `read` and the module fetch. A child record opened from a list
   * row re-reads through the list it was opened from (a multi-parent event can be in a readable list and 404 on its own GET).
   */
  read?: RecordReader;
  /**
   * The record as the opener already has it: shown as ready on the first attempt with no read at all. Refresh (a later
   * attempt) reads through the reader as usual.
   */
  seed?: DetailRecord;
}

/** Loads the record for `id`; a state is only ever shown for the id it was read for (no flash of the previous record). */
export function useRecord(
  cfg: RecordDetailEntity | undefined,
  id: string | undefined,
  attempt: number,
  options: UseRecordOptions = {}
): RecordState {
  const [loaded, setLoaded] = React.useState<{ key: string; state: RecordState } | null>(null);
  const key = `${cfg?.entity ?? ''}:${id ?? ''}:${attempt}`;
  const { read, seed } = options;
  // The reader and the seed are read through a ref: a new function identity from the opener must not re-fire the read.
  const latest = React.useRef({ read, seed });
  latest.current = { read, seed };
  const seeded = seed !== undefined && attempt === 0;

  React.useEffect(() => {
    if (!cfg || !id || (latest.current.seed !== undefined && attempt === 0)) return;
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
    const reader: RecordReader = latest.current.read ?? cfg.read ?? (recordId => readRecord(cfg, recordId));
    // A reader may throw synchronously or reject; both are mapped the same way. It is called at once (no extra tick).
    let pending: Promise<DetailRecord>;
    try {
      pending = reader(id);
    } catch (err) {
      pending = Promise.reject(err);
    }
    pending.then(
      record => done({ status: 'ready', record }),
      (err: unknown) => {
        if (err instanceof DeniedError || (err instanceof ApiError && err.statusCode === 403)) {
          done({ status: 'denied' });
        } else if (err instanceof RecordNotFoundError || (err instanceof ApiError && err.statusCode === 404)) {
          done({ status: 'notfound' });
        } else {
          done({ status: 'error', message: 'This record could not be loaded just now.', retryable: true });
        }
      }
    );
    return () => {
      cancelled = true;
    };
  }, [cfg, id, key, attempt]);

  if (!cfg || !id) return { status: 'notfound' };
  if (seeded && seed) return { status: 'ready', record: seed };
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
