/**
 * Dataverse GUID canonicalization (ADR-044).
 *
 * ADR-044 designates a single canonical `cleanGuid` — `@spaarke/ui-components`
 * `PolymorphicResolverService.cleanGuid` — and forbids hand-rolled per-file normalizers. This
 * package cannot import `@spaarke/ui-components` at all (ADR-012 Path A project-scoped exception,
 * project CLAUDE.md Decisions 2026-09-04: `@spaarke/ui-components` assumes React 19 APIs this
 * add-in doesn't share and some of its components are Xrm-bound, which does not exist in an Office
 * host — NFR-03). ADR-044's own documented deep-import fallback
 * (`@spaarke/ui-components/dist/services/PolymorphicResolverService`) is still an import FROM
 * `@spaarke/ui-components`, so it is blocked by the same exception.
 *
 * This is a **sanctioned local duplicate** — the same resolution already established in this exact
 * package for the same class of problem (see `shared/taskpane/services/todoChoices.ts`, documented
 * in project CLAUDE.md's Decisions Made table as "a sanctioned duplicate... the add-in has no Xrm,
 * so it mirrors the mapping (§11 justified)"). The implementation below is character-for-character
 * the same algorithm as the canonical `cleanGuid` (strip braces + whitespace, lowercase; a no-op on
 * an already-bare GUID) — it does not diverge in behavior, only in where the one function body
 * lives, so ADR-044's actual invariant (bare-lowercase at every client/server boundary) still holds.
 *
 * Null/undefined-safe: returns '' for a falsy input.
 */
export function cleanGuid(id: string | null | undefined): string {
  if (!id) return '';
  return id.replace(/[{}]/g, '').trim().toLowerCase();
}
