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
