# Plugin Structure Pattern → RETIRED (use the Server-Side Write Path)

> **Last Reviewed**: 2026-09-25
> **Reviewed By**: ADR-002 plugin review (owner-approved)
> **Status**: Verified — pattern retired

**Spaarke ships no Dataverse plugins (ADR-002, reaffirmed 2026-09-25).** There is no plugin pattern to follow. The `Spaarke.CustomApiProxy` / `BaseProxyPlugin` code was the counter-example and has been removed.

## When
You were about to write a plugin, or a feature needs a rule enforced when a record is saved (default, stamp, isolation, derived field).

## Do This Instead
1. `.claude/constraints/plugins.md` — decision table: where the rule goes
2. `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` — layers + invariant registry
3. `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs` — reference server-side invariant owner (stamp resolved in the BFF write path, fail-closed)
4. `src/server/api/Sprk.Bff.Api/Services/Jobs/` + `MembershipReconciliationJob` — reference reconciliation (WP-5)

## Constraints
- **ADR-002 WP-1…WP-8**: one server owner per invariant; client previews only; tables with invariants written via BFF; inline for security/on-load UX; async fix-up for non-product writes; security fails closed
- Any plugin proposal → root CLAUDE.md §6.5 against the ADR-002 reopen criteria
