# FR-10 Migration Strategy — Section Registry Extraction

> **Owner**: `spaarke-dataset-grid-framework-r2` (task 020 scaffolding → task 021 migration)
> **Author**: task 020 (scaffolding session, 2026-07-02)
> **Consumed by**: task 021 (migration), task 022 (SpaarkeAi alias update), task 024 (LegalWorkspace consumer cleanup)

---

## TL;DR

**Strategy: (b) RE-EXPORT** — the new shared package `@spaarke/legal-workspace` (folder `Spaarke.LegalWorkspace`) starts with an empty `src/index.ts`. Task 021 populates it with **re-exports** of files that stay under `src/solutions/LegalWorkspace/src/`. SpaarkeAi's existing alias `@spaarke/legal-workspace → ../LegalWorkspace/src` gets re-pointed to `../../client/shared/Spaarke.LegalWorkspace/src` — but since the shared package's index re-exports from LegalWorkspace, the behavioral change is invisible to SpaarkeAi.

MOVE (physical relocation of `sectionRegistry.ts` + 6 registration files) is deferred to a future project. In R2 the goal is to eliminate the alias trap by inserting a proper package boundary — not to relocate code.

---

## Two strategies considered

### (a) MOVE — physically relocate content

- Move `src/solutions/LegalWorkspace/src/sectionRegistry.ts` → `src/client/shared/Spaarke.LegalWorkspace/src/sectionRegistry.ts`.
- Move 6 section registrations (Communications, Documents, Invoices, Matters, Projects, Work Assignments) into the new package.
- Update all LegalWorkspace `main.tsx`/`App.tsx` internal imports to consume from `@spaarke/legal-workspace`.
- SpaarkeAi + LegalWorkspace both consume from the shared package via alias.

**Pros**: Cleaner long-term boundary. Section registry ceases to be LegalWorkspace-owned code; it becomes a shared library asset.

**Cons**:
- LegalWorkspace becomes a **self-consumer** of the shared package (imports `@spaarke/legal-workspace` from within `src/solutions/LegalWorkspace/`) — creates a circular reference to reason about + non-obvious import direction.
- Blast radius: dozens of files must be moved + import-updated + re-verified. Higher regression risk against 6 entity-list widgets + 5 single-section full-page layouts (both LegalWorkspace + SpaarkeAi).
- Rolling back = git revert of a 40+ file diff.

### (b) RE-EXPORT — package as a thin facade over LegalWorkspace src

- `Spaarke.LegalWorkspace/src/index.ts` re-exports the symbols consumers need from `../../../solutions/LegalWorkspace/src/*`.
- LegalWorkspace source files STAY at `src/solutions/LegalWorkspace/src/`.
- SpaarkeAi's alias `@spaarke/legal-workspace` re-points to `../../client/shared/Spaarke.LegalWorkspace/src` (the facade).
- LegalWorkspace itself continues to consume `sectionRegistry.ts` via local relative import (unchanged) — no self-consumption of `@spaarke/legal-workspace`.

**Pros**:
- Smaller blast radius: task 021 only creates re-export files; no source code moves. Task 022 flips one alias path.
- Package boundary is preserved: SpaarkeAi consumes from `Spaarke.LegalWorkspace` (a package), not from `LegalWorkspace/src` (a solution). The alias trap is broken.
- Rollback = git revert of a small diff.
- Future MOVE-style relocation remains possible without disturbing this project.

**Cons**:
- Section registry code still physically lives under `src/solutions/LegalWorkspace/` — slight cognitive dissonance ("why does a shared package re-export from a solution?"). Answer: because the R2 goal is to insert a boundary, not to relocate code.
- If LegalWorkspace itself is later deprecated (see LEGALWORKSPACE-RETIREMENT.md), the re-exports would break — but that project is out-of-scope here, and retirement work would naturally include hoisting the registry.

---

## Decision: RE-EXPORT (b)

**Rationale**:
1. R2 owner clarification (2026-07-02, applied in spec.md): "smaller blast radius, easier rollback" is the R2 goal for FR-10.
2. RE-EXPORT satisfies the primary success criterion (elimination of the SpaarkeAi ← LegalWorkspace source-alias trap) without moving any files.
3. Scaffolding (task 020) is trivial under RE-EXPORT — empty `src/index.ts` builds cleanly and gets populated in task 021.
4. Preserves optionality: if a future project retires the standalone LegalWorkspace code page, it can then hoist the registry files into `Spaarke.LegalWorkspace/src/` and update the re-exports to become primary exports. Zero blocking of that path.
5. Task 021's POML expects an existing strategy document at this path (`notes/fr10-migration-strategy.md`) — this file is that contract.

---

## Package identity confirmed

- **Folder**: `src/client/shared/Spaarke.LegalWorkspace/` (matches Spaarke.{Domain} convention without `.Components` suffix, per Spaarke.Auth precedent — this package exports registry factories + shell orchestration, not raw components).
- **npm name**: `@spaarke/legal-workspace` (matches SpaarkeAi's existing tsconfig + vite.config alias — zero rename needed).
- **Version**: `0.1.0` (initial scaffold; task 021 does NOT bump — bumps are for behavior changes, not migration to package).

---

## Compatibility with SpaarkeAi's current alias

SpaarkeAi's `vite.config.ts` (lines 201-202) and `tsconfig.json` (lines 24-25) currently alias `@spaarke/legal-workspace` and `@spaarke/legal-workspace/*` to `../LegalWorkspace/src`. Task 022 re-points those to `../../client/shared/Spaarke.LegalWorkspace/src` after task 021's re-exports are in place. Because our package name **matches the existing alias**, no consumer import changes are needed in SpaarkeAi source files (`main.tsx` line 70 keeps its `from "@spaarke/legal-workspace"` unchanged).

---

## Task 021 handoff

Task 021 should:
1. Determine the exact set of exports SpaarkeAi consumes today (grep `src/solutions/SpaarkeAi/src/**` for `from "@spaarke/legal-workspace"`).
2. Populate `Spaarke.LegalWorkspace/src/index.ts` with re-exports (`export * from "../../../solutions/LegalWorkspace/src/..."` or per-symbol equivalents).
3. Verify `Spaarke.LegalWorkspace` builds cleanly with the re-exports in place.
4. Do NOT flip SpaarkeAi's alias yet (that's task 022).

---

## References

- `projects/spaarke-dataset-grid-framework-r2/spec.md` FR-10
- `projects/spaarke-dataset-grid-framework-r2/plan.md` (Issue 12 Option B adoption)
- `src/client/shared/Spaarke.DailyBriefing.Components/` (structural analogue)
- `.claude/adr/ADR-012-shared-components.md` (SSOT rule)
- `docs/architecture/LEGALWORKSPACE-RETIREMENT.md` (future context — not blocking R2)
