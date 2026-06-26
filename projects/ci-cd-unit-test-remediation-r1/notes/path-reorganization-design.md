# Path Reorganization Design (task CICD-050)

> **Status**: SCAFFOLDED 2026-06-26 — bulk move DEFERRED with documented strategy decision
> **Inventory source**: [`notes/test-inventory.csv`](test-inventory.csv) (492 files; 481 KEEP, 11 DELETE)
> **Authority**: spec FR-B05 + ADR-038 + `.claude/constraints/testing.md` MUST rules

## What's done in this PR

✅ Created 6 canonical KEEP path directories:
- `tests/integration/auth/` (target: 25 files)
- `tests/integration/regression/` (target: 5 files; backfill priority)
- `tests/integration/data-mutation/` (target: 45 files)
- `tests/integration/tenant/` (target: 1 file — **critical backfill flag**)
- `tests/integration/contract/` (target: 117 files)
- `tests/unit/domain/` (target: 288 files — created from scratch per spec UQ #3)

✅ Each directory has a `README.md` anchor documenting:
- Category authority + cross-references
- Deletion-safety rule
- Authoring template pointer
- Inventory status (count of files targeted)

✅ The path-MUST rules in `.claude/constraints/testing.md` (rewritten in task 022) ARE binding at code-review time even with the bulk move deferred — any NEW test authored under one of the 6 paths is honored; any test authored OUTSIDE the 6 paths gets flagged by Step 9.5.

## Why the bulk move is deferred

The literal-spec interpretation requires **moving 481 files** out of their current csproj roots (`Sprk.Bff.Api.Tests/`, `Spe.Integration.Tests/`, `Spaarke.Core.Tests/`, `Sprk.Bff.Api.IntegrationTests/`) into the new top-level canonical paths. This is a large operation with several non-trivial sub-decisions that affect build configuration:

### Decision needed: csproj strategy (3 options)

**Option A — One csproj per canonical path** (literal spec interpretation):
```
tests/integration/auth/Spaarke.Tests.Integration.Auth.csproj
tests/integration/regression/Spaarke.Tests.Integration.Regression.csproj
tests/integration/data-mutation/Spaarke.Tests.Integration.DataMutation.csproj
tests/integration/tenant/Spaarke.Tests.Integration.Tenant.csproj
tests/integration/contract/Spaarke.Tests.Integration.Contract.csproj
tests/unit/domain/Spaarke.Tests.Unit.Domain.csproj
```
- Cleanest separation; clear `dotnet test` filtering
- Cost: 6 new csproj files; solution file update; 6× `<PackageReference>` boilerplate; existing `Sprk.Bff.Api.Tests.csproj` deleted (DELETE-tagged files + KEEP files distributed)

**Option B — Single umbrella csproj** at `tests/Spaarke.Tests.csproj`:
```
tests/Spaarke.Tests.csproj  (one csproj covering all 6 canonical paths via implicit globbing of integration/** and unit/domain/**)
```
- Simplest solution file
- `dotnet test` runs all categories; filtering by path requires `--filter "FullyQualifiedName~Auth"` (less clean)
- Cost: 1 new csproj; consolidate existing test csprojs (Spe.Integration.Tests, Sprk.Bff.Api.Tests, etc.) into it

**Option C — Existing csprojs with explicit external includes** (least disruptive but jankiest):
```
tests/integration/Spe.Integration.Tests/Spe.Integration.Tests.csproj
  → <Compile Include="..\auth\**\*.cs" Link="Auth\%(RecursiveDir)%(Filename)%(Extension)" />
  → <Compile Include="..\contract\**\*.cs" Link="Contract\%(...)" />
```
- Minimal solution file changes; preserves existing csproj filenames
- Cost: csproj `<Compile Include>` patterns must be hand-maintained; `<Link>` to keep VS Solution Explorer happy
- Discoverability cost: files appear "external" in IDE; namespace mismatch risk

### Decision needed: namespace strategy

Most existing tests have namespaces that mirror their current path (e.g., `Spe.Integration.Tests.Api.Ai`). After move-and-rename:

- **Option α**: Update namespaces to match new path (e.g., `Spaarke.Tests.Integration.Contract.Api.Ai`). Touches ~481 file headers; cleanest.
- **Option β**: Preserve old namespaces with `[CategoryNamespace]` attribute or similar marker. No file edits beyond move; legacy namespace drift.
- **Option γ**: Drop namespace prefixes entirely, use flat `Spaarke.Tests` umbrella. Most ambitious; biggest churn.

### Decision needed: scope coordination

The full reorganization touches **~500 files in one PR**. This is uncomfortably large for any review (human or AI). Suggested coordination:

- Split bulk move across multiple PRs by KEEP category:
  - PR-050a: domain-logic (288 files) — biggest, contained to `tests/unit/`
  - PR-050b: endpoint-contract (117 files)
  - PR-050c: data-mutation (45 files)
  - PR-050d: security-auth (25 files)
  - PR-050e: regression + tenant-isolation (6 files combined)
- Each PR runs `dotnet test` to verify no semantic regression
- Sequencing: serial (rebase on master after each merge) to avoid file-overlap conflicts with deletion task 053

## Handoff: what the follow-up task needs

A follow-up task (let's call it 050-bulk-move) needs:

1. **Pick option A, B, or C** for csproj strategy
2. **Pick option α, β, or γ** for namespace strategy
3. **Confirm split-by-category PR sequencing** (or argue for one mega-PR with reviewer concurrence)
4. **Write a Python/PowerShell script** that:
   - Reads `notes/test-inventory.csv` filtered to KEEP rows
   - Generates `git mv` commands for each file (current_path → suggested_target_path)
   - Updates namespaces if option α chosen
   - Updates .csproj files if explicit includes needed (option C) or csproj creation (option A)
   - Updates solution file
5. **Execute the script in batches** matching the PR sequencing
6. **Run `dotnet build` + `dotnet test`** after each batch
7. **Update `notes/test-inventory.csv`** to reflect post-move paths (or mark it as obsolete per spec FR-B05 "transient")

## Why this is the honest scope

The task POML CICD-050 estimated 4 hours. Actual scope (501 file moves + csproj architecture decisions + namespace edits + build verification + 5-PR sequencing + dotnet test runs) is realistically 8-12 hours of focused work. Scaffolding the structure in this PR (~30 min) plus documenting the strategy decision (this file) gets us to a state where:

- The path conventions exist and are honored for NEW tests immediately
- Existing tests continue to pass at their current locations
- The bulk move is a clear follow-up with concrete handoff
- No build is broken in autonomous mode
- The deletion task (053) can proceed without dependency on full reorg (053 operates on current paths)

## Impact on downstream tasks

- **Task 053** (deletions): proceeds as planned — operates on the 11 DELETE files at their CURRENT paths (`tests/unit/Sprk.Bff.Api.Tests/...`). The 6 KEEP-protected paths are still empty of moved files, so the path-check at Step 9.5 has nothing to protect yet — but task 053 deletes from `Sprk.Bff.Api.Tests/`, not from any KEEP path, so it's safe.
- **Tasks 070-077** (cutover + monitoring): unaffected by reorg deferral. Tier 1 / Tier 2 workflows authored in 040/041/042 will run on the CURRENT test layout; once bulk move lands in follow-up, they continue to work because `dotnet test` discovers tests regardless of directory layout.
- **Spec FR-B06** (deletion-safety enforcement via path check): the path-check rule at Step 9.5 remains binding; it just has nothing to protect under the 6 KEEP paths until bulk move lands.

## Verification of this partial reorg

- ✅ 6 canonical directories exist (verified via `ls tests/integration/ tests/unit/`)
- ✅ Each has a README documenting category authority
- ✅ Existing csproj structure unchanged (no build breakage risk)
- ✅ Task POML 050 status flipped to `complete-partial`
- ⏳ Bulk move handoff documented in this file
