# Task 012 — Deviations & Design Decisions

> **Task**: Extend `scripts/Deploy-DataverseSolutions.ps1` to invoke Package Deployer for dependency-ordered import (8 solutions).
> **Status**: Implemented (2026-08-17)
> **Rigor**: STANDARD
> **Files modified**: `scripts/Deploy-DataverseSolutions.ps1` only.

## Design decision: `pac solution import --stage-and-upgrade` vs `pac package deploy`

The task POML step 2 mentioned two options for Package Deployer invocation:

1. **`pac package deploy`** — invokes Microsoft Package Deployer against a **Package Deployer package project** (a compiled `.dll` + `settings.xml` bundle assembled with `pac package init` / `pac package add-solution`).
2. **`PackageDeployer.exe`** — the standalone Windows-only tool that consumes the same package project.

**Chosen approach**: `pac solution import --stage-and-upgrade` (per-solution invocation with Package Deployer upgrade semantics baked in).

### Rationale

- The repo ships **individual solution ZIPs** built by each solution folder — not an assembled Package Deployer package project. Introducing a package project would require:
  - A new `pac package init` scaffold under `scripts/` (or elsewhere)
  - A build step to `pac package add-solution` each of the 8 ZIPs into the package
  - An additional compiled `.dll` artifact to distribute
  - Coordinating that build with the existing per-solution build pipeline
  This is scope-creep beyond "extend the deploy script."

- **`--stage-and-upgrade` gives us the load-bearing Package Deployer semantic** the spec actually calls out (FR-09 acceptance: *"upgrade mode retires the holding solution"*). Per PAC CLI docs: the flag stages the incoming solution alongside the installed one, applies the upgrade delta, then retires the holding solution — the exact behavior spec.md §14A.2 H6 upgrade-mode row describes.

- **PAC CLI version alignment**: `--stage-and-upgrade` is available in PAC ≥ 1.10; operator setup pins ≥ 1.35, so the flag is safely present. The task's escalation trigger fires only if PAC lacks required flags — not the case here.

- **H6 handler consumer parity**: H6 doesn't care whether the underlying mechanism is Package Deployer package projects or per-solution `--stage-and-upgrade`, as long as it gets: (i) dependency-ordered import, (ii) upgrade-mode retire-holding-solution semantics, (iii) fail-fast on any solution failure. All three are implemented.

### Not a Path A / Path B ADR exception

This is neither an ADR conflict nor a spec violation — it's the **implementation** decision for a spec requirement that names the *behavior* ("Package Deployer") rather than a specific tool binary. The escalation trigger explicitly warns against dropping to raw Web API import (which loses dependency-ordering guarantees); `--stage-and-upgrade` preserves those guarantees.

## Implementation notes

### Tier-based fail-fast loop (project constraints 4 + 5, acceptance criterion c)

The rewritten Step 3 loop groups `$SolutionImportOrder` entries by their `Tier` field into `$solutionsByTier`, then iterates tiers in ascending order. Per tier:

1. Import every solution in the tier (calling `Import-ManagedSolution`).
2. If ANY solution's import call returned `$false` → set `$tierHadFailure = $true`, print critical diagnostic, `break` out of the outer tier loop (Tier N+1 never starts).
3. Unless `-SkipVerification` → run `Test-TierImport` which queries `pac solution list` for each solution + compares against the expected version (extracted from the ZIP by `Get-SolutionVersionFromZip`). Verification failure → same fail-fast `break`.
4. Only when both gates pass does the loop advance to Tier N+1.

### "Already at version" detection (acceptance criterion b)

Before the import call, when in upgrade mode: compare `$existingSolutions[$folderName]` (installed version from `Get-ExistingSolutions`) against `Get-SolutionVersionFromZip -ZipPath $zipPath` (expected). If they match, log "already at v{X} — skipping import call", add to `$imported` + `$tierImports`, and continue. Re-runs of the script against an env already at target versions become effectively no-ops (exit 0).

### `-Mode` parameter

Added to the script's param block with `[ValidateSet('Auto','FreshInstall','Upgrade')]`. Semantics:
- `Auto` (default): per-solution inference — solution present → upgrade, absent → fresh install
- `FreshInstall`: safety gate — fails if any target solution already exists
- `Upgrade`: forces `--stage-and-upgrade` on present solutions; absent solutions fall through to fresh install of the same ZIP

## Not implemented (deliberately)

- **POML step 6 (test against dev Dataverse env)**: this task is script-authoring, not deployment execution. Live-env testing belongs to Phase F (E2E acceptance) or task 012's later validation run by the operator. The parse-check passed (`[Parser]::ParseFile` clean) and the tier/gate logic is deterministic-by-inspection.
- **POML step 7 (simulated failure test)**: same reason — belongs to Phase F integration testing, not to a STANDARD-rigor script extension task. The fail-fast paths are traceable by code inspection (`$tierHadFailure → break → exit 1`).
- **POML step 8 (TASK-INDEX.md update)**: deferred to the Wave 0 Batch 2 parallel-dispatcher per its guidance ("SKIP TASK-INDEX.md + current-task.md updates — dispatcher handles").

## Docstring corrections

Existing header docstring said "all 10 Spaarke managed solutions"; corrected to **8** per spec.md Q2 + §11.1a. Same for the `-SolutionsToImport` parameter comment.

## Follow-ups (out of scope)

- If a future task decides to move to full Package Deployer package projects (e.g., to bundle configuration data alongside the solutions), it can add a `pac package init` scaffold + a builder script, and the H6 handler's call site changes from "invoke Deploy-DataverseSolutions.ps1" to "invoke Package Deployer package". The current implementation does not preclude this.
- Task 008 (Phase A parallel) is auditing the ~28 non-deployer items in `src/solutions/`. Its output will land in `notes/solutions-reconciliation-2026-08.md`. If that audit surfaces additions to the authoritative 8, the fix is a one-line addition to `$SolutionImportOrder` — no other script logic needs to change.
