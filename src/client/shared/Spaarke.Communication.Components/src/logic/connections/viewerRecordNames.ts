/**
 * Viewer-side record names for association candidates — one shared, batched resolver.
 *
 * A stored association trace (`sprk_associationprovenance`) holds ids and reason codes only, so a review card's
 * name comes from a read made AS THE VIEWER. This module makes that read: per entity, one Web API query per chunk of
 * ids (`$filter=<pk> eq … or <pk> eq …`, selecting only the key and the primary name field), through the host's own
 * Web API, which runs as the signed-in user. A row the viewer cannot read is simply not returned, so it resolves to
 * `null` — the same answer as a failed read. Results are cached for the life of the resolver (one per page session:
 * `getViewerRecordNameResolver`).
 *
 * Generalized from the CommunicationConnections PCF's per-record resolver (display-name field from the
 * `sprk_recordtype_ref` catalog, else the per-entity fallback below).
 *
 * Use with the provenance reducers (`deriveConnections`, `flattenPrimaryCandidates`, `derivePrimaryReview`,
 * `groupCandidatesByName`) by passing `resolver.nameOf` as their `viewerName` argument:
 *
 *   const resolver = getViewerRecordNameResolver(webApi);
 *   await resolver.resolve(doc.candidates.map(c => ({ entity: c.targetEntity, id: c.targetId })));
 *   const slots = deriveConnections(doc, isResolved, resolver.nameOf);
 *
 * React-free (Layer 1); a host re-renders on `subscribe`.
 */

import { cleanGuid, resolveRecordDisplayNameFieldName, type IPolymorphicWebApi } from '@spaarke/ui-components';
import type { ViewerTargetName } from './provenance';

/**
 * A record to resolve: entity logical name + id (any GUID spelling). Refs usually come from
 * stored JSON, so a missing value is accepted and resolves to null without a read.
 */
export interface ViewerRecordRef {
  entity: string | null | undefined;
  id: string | null | undefined;
}

/** The viewer-side name resolver for one page session. */
export interface ViewerRecordNameResolver {
  /**
   * Synchronous lookup, shaped as the reducers' `viewerName`: the name (readable), `null` (not readable, or the read
   * failed), or `undefined` (not resolved yet).
   */
  readonly nameOf: ViewerTargetName;
  /** Reads, as the viewer, every ref not already known or in flight. Never rejects; failures resolve to `null`. */
  resolve(refs: readonly ViewerRecordRef[]): Promise<void>;
  /** Called after each `resolve` that learned something. Returns an unsubscribe function. */
  subscribe(listener: () => void): () => void;
}

/** Options for `createViewerRecordNameResolver`. */
export interface ViewerRecordNameResolverOptions {
  /** Ids per query (bounds the URL length). Default 25. */
  chunkSize?: number;
  /** Name field to use for an entity, ahead of the catalog. Defaults to the catalog, then `PRIMARY_NAME_FALLBACK`. */
  nameFieldOf?: (entity: string) => Promise<string | null | undefined>;
}

/**
 * Fallback primary-name field per entity, used ONLY when the `sprk_recordtype_ref` catalog has no
 * `sprk_recorddisplaynamefield` row. The catalog is authoritative (SRFR-052).
 */
export const PRIMARY_NAME_FALLBACK: Readonly<Record<string, string>> = {
  contact: 'fullname',
  account: 'name',
  sprk_matter: 'sprk_mattername',
  sprk_organization: 'sprk_name',
  sprk_project: 'sprk_name',
  sprk_invoice: 'sprk_name',
  sprk_event: 'sprk_name',
  sprk_servicerequest: 'sprk_name',
  sprk_workassignment: 'sprk_name',
};

const DEFAULT_CHUNK_SIZE = 25;
const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const ENTITY_RE = /^[a-z_][a-z0-9_]*$/;
const FIELD_RE = /^[a-z_][a-z0-9_]*$/;

function keyOf(entity: string, id: string): string {
  return `${entity.toLowerCase()}:${cleanGuid(id)}`;
}

/** Creates a resolver over `webApi` (the host's Web API, which reads as the signed-in viewer). */
export function createViewerRecordNameResolver(
  webApi: IPolymorphicWebApi,
  options: ViewerRecordNameResolverOptions = {}
): ViewerRecordNameResolver {
  const chunkSize = Math.max(1, options.chunkSize ?? DEFAULT_CHUNK_SIZE);
  const known = new Map<string, string | null>();
  const inFlight = new Set<string>();
  const listeners = new Set<() => void>();

  const nameFieldOf = async (entity: string): Promise<string | undefined> => {
    const fromOption = options.nameFieldOf ? await options.nameFieldOf(entity) : undefined;
    const field =
      fromOption ?? (await resolveRecordDisplayNameFieldName(webApi, entity)) ?? PRIMARY_NAME_FALLBACK[entity];
    return field && FIELD_RE.test(field) ? field : undefined;
  };

  const readChunk = async (entity: string, field: string, ids: string[]): Promise<void> => {
    const pk = `${entity}id`;
    const filter = ids.map(id => `${pk} eq ${id}`).join(' or ');
    try {
      const result = await webApi.retrieveMultipleRecords(entity, `?$select=${pk},${field}&$filter=${filter}`);
      const names = new Map<string, string>();
      for (const row of result.entities) {
        const rowId = row[pk];
        const name = row[field];
        if (typeof rowId === 'string' && typeof name === 'string' && name.trim().length > 0) {
          names.set(cleanGuid(rowId), name.trim());
        }
      }
      for (const id of ids) known.set(`${entity}:${id}`, names.get(id) ?? null);
    } catch {
      for (const id of ids) known.set(`${entity}:${id}`, null);
    }
  };

  const nameOf: ViewerTargetName = (entity, id) => {
    if (!entity || !id) return null;
    const key = keyOf(entity, id);
    return known.has(key) ? (known.get(key) as string | null) : undefined;
  };

  const resolve = async (refs: readonly ViewerRecordRef[]): Promise<void> => {
    const byEntity = new Map<string, string[]>();
    for (const ref of refs) {
      const entity = (ref.entity ?? '').toLowerCase();
      const id = cleanGuid(ref.id ?? '');
      const key = `${entity}:${id}`;
      if (known.has(key) || inFlight.has(key)) continue;
      if (!ENTITY_RE.test(entity) || !GUID_RE.test(id)) {
        known.set(key, null);
        continue;
      }
      inFlight.add(key);
      const list = byEntity.get(entity) ?? [];
      list.push(id);
      byEntity.set(entity, list);
    }
    if (byEntity.size === 0) return;

    await Promise.all(
      Array.from(byEntity.entries()).map(async ([entity, ids]) => {
        let field: string | undefined;
        try {
          field = await nameFieldOf(entity);
        } catch {
          field = undefined;
        }
        if (!field) {
          for (const id of ids) known.set(`${entity}:${id}`, null);
        } else {
          for (let i = 0; i < ids.length; i += chunkSize) {
            await readChunk(entity, field, ids.slice(i, i + chunkSize));
          }
        }
        for (const id of ids) inFlight.delete(`${entity}:${id}`);
      })
    );
    listeners.forEach(l => l());
  };

  const subscribe = (listener: () => void): (() => void) => {
    listeners.add(listener);
    return () => listeners.delete(listener);
  };

  return { nameOf, resolve, subscribe };
}

const sessionResolvers = new WeakMap<object, ViewerRecordNameResolver>();

/** The page-session resolver for `webApi` — one per Web API object, so every surface on a page shares one cache. */
export function getViewerRecordNameResolver(webApi: IPolymorphicWebApi): ViewerRecordNameResolver {
  let resolver = sessionResolvers.get(webApi);
  if (!resolver) {
    resolver = createViewerRecordNameResolver(webApi);
    sessionResolvers.set(webApi, resolver);
  }
  return resolver;
}
