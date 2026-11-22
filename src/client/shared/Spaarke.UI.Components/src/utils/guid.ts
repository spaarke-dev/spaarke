/**
 * guid.ts — Dataverse GUID canonicalization (ADR-044).
 *
 * This is the canonical, discoverable home for `cleanGuid` (C-7,
 * spaarke-ontology-platform-r1 reuse audit, item U1). It was previously
 * implemented inside `services/PolymorphicResolverService.ts` under a comment
 * that called itself "the ONE place braces get stripped" — a claim the audit
 * found false: the identical one-liner was independently reimplemented at
 * ~70 call sites, ~25 of them inside this package's own `services/` and
 * `components/` directories. Relocating it here (and re-exporting it from
 * `services/PolymorphicResolverService.ts` for backward compatibility — see
 * below) makes it findable without already knowing to look inside a resolver
 * service.
 *
 * ADR-044 MUST: normalize any Dataverse GUID to bare-lowercase before it
 * crosses a system boundary (Xrm host, `@odata.bind`, Azure AI Search
 * `Edm.String eq`). ADR-044 MUST NOT: hand-roll a per-file normalizer
 * (`id.replace(/[{}]/g, '')`, ad-hoc `.toLowerCase()`) — reuse this function.
 *
 * Null/undefined-safe: returns '' for a falsy input. No-op on an already-bare
 * GUID, so it is always safe to apply uniformly.
 */
export function cleanGuid(id: string | null | undefined): string {
  if (!id) return '';
  return id.replace(/[{}]/g, '').trim().toLowerCase();
}

const BARE_GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const LOGICAL_NAME = /^[a-z][a-z0-9_]*$/;

/**
 * Validate and normalize a record reference that comes from an untrusted source: a URL parameter, an event payload or a
 * plain-JS caller (#2908: the compose envelope's "Related to" record and the conversation-panel open request).
 *
 * Returns the table's logical name (lower-case) and the bare-lowercase GUID, or `undefined` unless both are strings
 * of the right shape. One validator, so every caller agrees on which references are valid.
 */
export function normalizeRecordRef(entityType: unknown, id: unknown): { entityType: string; id: string } | undefined {
  if (typeof entityType !== 'string' || typeof id !== 'string') return undefined;
  const normalizedType = entityType.trim().toLowerCase();
  const normalizedId = cleanGuid(id);
  if (!LOGICAL_NAME.test(normalizedType) || !BARE_GUID.test(normalizedId)) return undefined;
  return { entityType: normalizedType, id: normalizedId };
}
