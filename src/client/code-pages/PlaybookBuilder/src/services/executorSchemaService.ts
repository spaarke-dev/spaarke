/**
 * Executor Schema Service — fetches typed config schemas from the BFF and caches them.
 *
 * Consumes the Wave 3 endpoint introduced by R7 task 033:
 *   GET /api/ai/playbook-builder/executor-config-schemas
 *
 * The wire DTO is defined server-side in
 *   src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ExecutorConfigSchema.cs
 * (records: ExecutorConfigSchema, ConfigSchemaField, enum SchemaFieldType serialized as string).
 *
 * Drives the schema-driven `TypedConfigForm` renderer (R7 FR-23, task 083) that replaces
 * free-form JSON editing of `sprk_configjson` on the PlaybookBuilder canvas.
 *
 * Caching strategy (per task 083 POML Step 3):
 *   - In-memory cache for the SPA lifetime (singleton module map)
 *   - sessionStorage cache with 5-minute TTL across page reloads / Code Page reopens
 *   - Race-safe inflight promise — concurrent callers share a single fetch
 *   - clearCache() escape hatch for tests + maker dev workflows
 *
 * Auth v2 (D-AUTH-7): uses `authenticatedFetch` from @spaarke/auth (per templateStore.ts
 * pattern). The function-based contract injects a fresh BFF-audience Bearer token at call
 * time — no React state snapshot, no token reuse across stale closures.
 *
 * @see projects/spaarke-ai-platform-unification-r7/notes/spikes/getconfigschema-design.md §3/§6
 * @see ADR-029 — endpoint payload deliberately uncached server-side, so client cache matters
 */

import { authenticatedFetch } from './authInit';

// ---------------------------------------------------------------------------
// Wire DTO mirrors of Sprk.Bff.Api.Services.Ai.Nodes.* records
// ---------------------------------------------------------------------------

/**
 * Allowed field kinds in an `ExecutorConfigSchema`. Serialized server-side as the C# enum
 * member name (string) via `JsonStringEnumConverter` — design doc §3 forward-compat strategy.
 * New members MUST be appended at the end on the server; this union mirrors that order.
 */
export type SchemaFieldType = 'String' | 'Number' | 'Boolean' | 'Object' | 'Array' | 'Enum';

/**
 * Single field in an executor's config schema. Wire-compatible with the C# record
 * `ConfigSchemaField`. `default` and `enumValues` are optional + nullable per the server.
 */
export interface ConfigSchemaField {
  /** Property name in `sprk_configjson` (camelCase). */
  name: string;
  /** Field kind — drives which Fluent v9 input widget the renderer chooses. */
  type: SchemaFieldType;
  /** Whether the canvas must require a value before save. */
  required: boolean;
  /** Plain-text help text shown alongside the widget. */
  description: string;
  /** Optional default value, serialized as raw JSON (string | number | boolean | object | array | null). */
  default?: unknown;
  /** Optional dropdown options — populated only when `type === 'Enum'`. */
  enumValues?: string[];
}

/**
 * Typed configuration schema for one `INodeExecutor`. Wire-compatible with the C# record
 * `ExecutorConfigSchema`. An empty `fields` array is the placeholder convention
 * (design doc §4) — distinguishes "no maker-editable config" from "schema not yet defined."
 */
export interface ExecutorConfigSchema {
  /** Server-side `ExecutorType` enum member name (e.g., "AiCompletion"). */
  executorTypeName: string;
  /** Server-side `ExecutorType` enum numeric value — keyed against `node.sprk_executortype`. */
  executorTypeValue: number;
  /** Human-readable description rendered above the typed form. */
  description: string;
  /** All maker-editable fields. Empty array = placeholder per design doc §4. */
  fields: ConfigSchemaField[];
}

/** Response envelope from `GET /api/ai/playbook-builder/executor-config-schemas`. */
interface ExecutorConfigSchemasResponse {
  schemas: ExecutorConfigSchema[];
}

// ---------------------------------------------------------------------------
// Cache + fetch
// ---------------------------------------------------------------------------

const LOG_PREFIX = '[PlaybookBuilder:ExecutorSchemaService]';
const SESSION_STORAGE_KEY = 'sprk.r7.executorConfigSchemas.v1';
const CACHE_TTL_MS = 5 * 60 * 1000; // 5 minutes per task 083 POML Step 3.

interface SessionCacheEntry {
  fetchedAt: number;
  schemas: ExecutorConfigSchema[];
}

/** In-memory map keyed by `executorTypeValue` for O(1) lookup. */
let inMemoryMap: Map<number, ExecutorConfigSchema> | null = null;

/** Race guard so concurrent callers share one network fetch. */
let inflight: Promise<Map<number, ExecutorConfigSchema>> | null = null;

function buildMap(schemas: ExecutorConfigSchema[]): Map<number, ExecutorConfigSchema> {
  const map = new Map<number, ExecutorConfigSchema>();
  for (const schema of schemas) {
    map.set(schema.executorTypeValue, schema);
  }
  return map;
}

function readSessionCache(): ExecutorConfigSchema[] | null {
  try {
    const raw = window.sessionStorage.getItem(SESSION_STORAGE_KEY);
    if (!raw) return null;
    const entry = JSON.parse(raw) as SessionCacheEntry;
    if (!entry || typeof entry.fetchedAt !== 'number' || !Array.isArray(entry.schemas)) return null;
    if (Date.now() - entry.fetchedAt > CACHE_TTL_MS) return null;
    return entry.schemas;
  } catch (err) {
    // Corrupt cache entry — drop it silently and re-fetch.
    console.warn(`${LOG_PREFIX} sessionStorage read failed; cache will be rebuilt`, err);
    return null;
  }
}

function writeSessionCache(schemas: ExecutorConfigSchema[]): void {
  try {
    const entry: SessionCacheEntry = { fetchedAt: Date.now(), schemas };
    window.sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(entry));
  } catch (err) {
    // QuotaExceeded / disabled storage / etc. — non-fatal, just skip caching.
    console.warn(`${LOG_PREFIX} sessionStorage write failed; continuing without cross-reload cache`, err);
  }
}

/**
 * Fetch executor schemas from the BFF, populating both in-memory and sessionStorage caches.
 * Subsequent calls within the TTL window return the cached map without a network round-trip.
 *
 * @param apiBaseUrl Base URL of the BFF (e.g., `https://sprk-bff-api.azurewebsites.net`).
 *                   Trailing slash tolerated; mirrors the convention in `templateStore.setApiBaseUrl`.
 * @returns Map of `executorTypeValue` → `ExecutorConfigSchema`.
 *
 * @remarks
 * Concurrent callers share a single inflight request. On HTTP failure the inflight promise
 * is released so the next call can retry (no negative caching — server is the source of truth
 * and may briefly be unavailable during deploy).
 */
export async function fetchExecutorSchemas(apiBaseUrl: string): Promise<Map<number, ExecutorConfigSchema>> {
  // Fast path: in-memory cache hit.
  if (inMemoryMap !== null) return inMemoryMap;

  // Race guard: share inflight request across concurrent callers.
  if (inflight !== null) return inflight;

  // Try cross-reload sessionStorage cache before hitting the network.
  const sessionCached = readSessionCache();
  if (sessionCached !== null) {
    inMemoryMap = buildMap(sessionCached);
    return inMemoryMap;
  }

  const trimmedBase = apiBaseUrl.replace(/\/$/, '');
  const url = `${trimmedBase}/api/ai/playbook-builder/executor-config-schemas`;

  inflight = (async (): Promise<Map<number, ExecutorConfigSchema>> => {
    try {
      const response = await authenticatedFetch(url, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        const errorBody = await response.text().catch(() => '');
        throw new Error(`Schema endpoint ${response.status}: ${response.statusText} ${errorBody}`.trim());
      }

      const body = (await response.json()) as ExecutorConfigSchemasResponse;
      const schemas = Array.isArray(body?.schemas) ? body.schemas : [];

      writeSessionCache(schemas);
      const map = buildMap(schemas);
      inMemoryMap = map;
      return map;
    } finally {
      inflight = null;
    }
  })();

  return inflight;
}

/**
 * Lookup a single schema by `executorTypeValue`. Returns `undefined` if no schema has
 * been registered for that executor type — callers should treat undefined as the
 * "unknown executor type" case (paves for R7 FR-27 / task 089 warning UX).
 *
 * @param value Numeric `ExecutorType` enum value matching `node.sprk_executortype`.
 * @returns The schema if cached, otherwise `undefined`. Does NOT trigger a fetch —
 *          call `fetchExecutorSchemas` first to populate the cache.
 */
export function getSchemaForExecutorType(value: number): ExecutorConfigSchema | undefined {
  if (inMemoryMap === null) return undefined;
  return inMemoryMap.get(value);
}

/**
 * Lookup a single schema by `executorTypeName` (PascalCase, e.g., `"AiCompletion"`).
 *
 * The canvas currently stores the executor as a camelCase `PlaybookNodeType` string on
 * `node.data.type` (e.g., `"aiCompletion"`). Wave 8 tasks 081 + 088 are introducing a
 * numeric `sprk_executortype` column on the canvas — until then, callers use this name-
 * based lookup with a Pascal-cased version of the canvas node type. Once 081 + 088 land,
 * prefer `getSchemaForExecutorType(value: number)` for stability.
 *
 * @param name PascalCase enum member name (e.g., `"AiCompletion"`, `"Wait"`).
 * @returns The schema if cached, otherwise `undefined`. Does NOT trigger a fetch.
 */
export function getSchemaForExecutorTypeName(name: string): ExecutorConfigSchema | undefined {
  if (inMemoryMap === null) return undefined;
  for (const schema of inMemoryMap.values()) {
    if (schema.executorTypeName === name) return schema;
  }
  return undefined;
}

/**
 * Whether the in-memory schema cache has been populated (i.e., `fetchExecutorSchemas`
 * has resolved at least once). Useful for renderers to decide between "still loading"
 * and "loaded but no schema for this type" states.
 */
export function isSchemaCacheReady(): boolean {
  return inMemoryMap !== null;
}

/**
 * Clear in-memory + sessionStorage caches. Intended for test reset hooks and the
 * "Reload Schemas" maker affordance (future enhancement).
 */
export function clearSchemaCache(): void {
  inMemoryMap = null;
  inflight = null;
  try {
    window.sessionStorage.removeItem(SESSION_STORAGE_KEY);
  } catch {
    /* ignore — storage unavailable */
  }
}
