# Spaarke Solution Release Process

> **Purpose**: The repeatable ISV publisher-based discipline for identifying, packaging,
> and releasing the Spaarke Dataverse content — for both initial customer provisioning
> AND for subsequent release updates.
>
> **Established**: 2026-08-20 during customer-provisioning-orchestration-r1 Wave H-3
> pre-check, after the 5-Fable-reviewer audit surfaced that the current CanonicalSolutionCatalog
> expected 8 solutions but only 2 had scaffolding. Microsoft's Power Platform ALM guidance
> (Pattern #1 Single Solution) recommends one managed solution for small-medium ISVs.

---

## What this replaces

Before: aspirational 8-solution catalog with mostly-missing scaffolding. Each new customer
required manual solution assembly + no drift detection + tribal knowledge of what belongs
where.

After: **one packaged managed solution (`SpaarkeMaster`)** produced by three scripts that
enforce publisher-based identification, OOB customization capture, and drift detection.

---

## The one solution: SpaarkeMaster

Single managed solution containing everything a Spaarke customer environment needs:

- All `sprk_*` custom entities (per the current audit: 122 in SpaarkeMaster baseline)
- All custom attributes on those entities (69 baseline)
- All web resources (196 baseline + delta from SpaarkeFeatures)
- All PCF custom controls (7 baseline; excludable per release via `-ExcludedPCFs`)
- All environment variable **declarations** (21 baseline — values set per-customer at
  provisioning time by H7)
- All environment variable **default values** (6 baseline)
- All global option sets (29 baseline)
- All entity relationships (24 baseline)
- All security roles (7 baseline)
- All saved queries / system views (7 baseline)
- All system forms (8 baseline)
- Site map(s) (2 baseline)
- Model-Driven App(s) — need to be added (currently in `SpaarkeCorporateCounselApp`)
- OOB entity customizations for account, contact, systemuser, businessunit (21 sprk_
  columns total; see [docs/data-model/oob-customizations.yaml](../data-model/oob-customizations.yaml))
- Canvas Apps (5 in SpaarkeCore 1.1.0.0)

Explicitly EXCLUDED:
- `Spaarke.Plugins` assembly — REMOVED from spaarkedev1 on 2026-08-20 during Wave H-3
  audit (unregistered `DocumentEventPlugin` + Delete/Create SDK message steps + assembly).
  Any future Spaarke plugin registration is a policy decision, not a default.
- Test / scratch / temp solutions (`SpaarkeMasterTest*`, `PowerAppsToolsTemp_sprk`,
  `TemplatePCFImport`, `SPRKMAINDEV1250801`).
- Microsoft-authored tooling installed to spaarkedev1 (`CreatorKit*`, `DataverseAccelerator*`).

---

## The three scripts

Location: `scripts/solution-authoring/`

### 1. `Get-SpaarkeComponents.ps1` (read-only)

Queries the target Dataverse environment (default `spaarkedev1`) for every solution
owned by the `Spaarke` publisher, enumerates every component inside those solutions,
and emits a deterministic JSON inventory to `docs/data-model/spaarke-components-inventory.json`.

**When to run**:
- Whenever a Spaarke publisher-owned solution changes in the dev environment
- Before every release (freshens the baseline snapshot)
- During drift-detection CI (called by `Test-SolutionCompleteness.ps1`)

**Publisher anchor**: `WHERE publisherid = '6aeef721-ba73-f011-b4cb-6045bdd6a665'`
catches every Spaarke-owned component deterministically. Test/scratch solutions
matching the exclude pattern `Test$|Temp|SCRATCH|MasterTest|TestSpaarke` are filtered out.

**Output**: `docs/data-model/spaarke-components-inventory.json` — checked into git as the
release-scope-of-record.

### 2. `Assemble-SpaarkeMasterSolution.ps1` (read + write)

Reads the inventory + `oob-customizations.yaml`, computes deltas against the current
state of the `SpaarkeMaster` solution in the dev environment, adds any missing components
via `AddSolutionComponent` Web API action, bumps the solution version, and exports as a
managed ZIP.

**Idempotent**: safe to re-run. Existing components are skipped.

**Flags**:
- `-WhatIf` — dry run; reports what WOULD be added without touching Dataverse
- `-ExcludedPCFs @('name1','name2')` — exclude specific PCFs (deferred to follow-on;
  currently logged as a warning)
- `-VersionBumpKind Build|Revision|Minor|Major` — segment to bump (default Build)
- `-SkipExport` — augment only, do not export
- `-OutputZipPath <path>` — where to write the managed ZIP (default `./out/SpaarkeMaster.zip`)

**Output**: managed solution ZIP at `-OutputZipPath` — ready to hand off to the customer-provisioning
H6 pipeline (upload to provisioning-artifacts storage; H6 imports it into each customer's env).

### 3. `Test-SolutionCompleteness.ps1` (read-only, CI-friendly)

Drift detection: runs `Get-SpaarkeComponents.ps1` fresh against dev, compares to the committed
`spaarke-components-inventory.json`, exits 1 if drift is found.

**When to run**: CI on every PR (fails the build if a dev change hasn't been captured in
the release manifest).

**Two drift classes**:
- **NEW in dev, not in committed** — new content added in spaarkedev1 that must be added to
  the release manifest before release
- **MISSING from dev, in committed** — content that was in the last release but is gone from
  dev now (may indicate accidental deletion; investigate before proceeding)

---

## Standard release workflow

For each Spaarke release (initial customer OR update to existing customers):

1. **Ensure dev is release-ready**: all in-flight Spaarke customizations are complete + tested in spaarkedev1
2. **Refresh inventory**:
   ```powershell
   ./scripts/solution-authoring/Get-SpaarkeComponents.ps1
   ```
3. **Review the diff** (against last committed inventory in git):
   ```powershell
   git diff docs/data-model/spaarke-components-inventory.json
   ```
4. **Update OOB manifest** if any new sprk_ columns were added to OOB entities:
   Edit [docs/data-model/oob-customizations.yaml](../data-model/oob-customizations.yaml)
5. **Commit the inventory + OOB manifest** (this locks the release scope)
6. **Dry-run the assembly**:
   ```powershell
   ./scripts/solution-authoring/Assemble-SpaarkeMasterSolution.ps1 -WhatIf
   ```
7. **Apply the assembly** (adds any missing components to SpaarkeMaster + bumps version + exports ZIP):
   ```powershell
   ./scripts/solution-authoring/Assemble-SpaarkeMasterSolution.ps1
   ```
8. **Verify drift-free** (must return exit 0):
   ```powershell
   ./scripts/solution-authoring/Test-SolutionCompleteness.ps1
   ```
9. **Hand off ZIP** to customer-provisioning: upload `./out/SpaarkeMaster.zip` to the
   provisioning-artifacts blob storage (per Wave H-3 backlog); update
   `dataverse-solutions-latest.json` manifest via the CI workflow
   `publish-dataverse-solutions-manifest.yml`. H6 picks it up automatically.

---

## Governance rules

1. **Publisher `Spaarke` is the only publisher for Spaarke content.** No component
   authored under any other publisher prefix ships to customers.
2. **Test/scratch solutions never ship.** The exclude pattern in
   `Get-SpaarkeComponents.ps1` is authoritative. Test solutions must follow the
   naming convention (contain `Test` at end, `Temp`, `SCRATCH`, `MasterTest`).
3. **No custom plugins ship without an explicit policy decision.** Current policy:
   Spaarke doesn't use plugins (all server-side logic lives in the BFF). If a plugin
   is ever added, it must be documented + approved + then included via manifest addition.
4. **OOB customizations MUST be documented** in `oob-customizations.yaml` in the same
   PR that adds the sprk_ column. Test-SolutionCompleteness catches drift.
5. **Version bumps are semver**: Major on breaking schema changes; Minor on new entity or
   feature additions; Build on additive content (attributes, web resources, PCFs).
   Revision reserved for patches to a specific customer stamp.

---

## Governance sequencing

- **Repo-wide tool** (this document + the three scripts) is NOT owned by any single project.
  It's shared infrastructure used by every project that ships Dataverse content.
- **Ownership**: the Spaarke platform / ALM team (nominal — refine when project structure evolves).
- **Change discipline**: PRs modifying `SPAARKE-SOLUTION-RELEASE-PROCESS.md` or the three scripts
  should be reviewed by 2+ team members; changes to `oob-customizations.yaml` follow
  normal PR review (drift check gates it in CI).

---

## History

| Date | Event |
|---|---|
| 2026-08-20 | Established. Baseline audit: 217 authored components in SpaarkeMaster; 21 OOB columns across 4 entities; `Spaarke.Plugins` orphan removed from spaarkedev1. |

---

## Related

- [ADR-039: Grounded execution + closed catalogs](../../.claude/adr/ADR-039-grounded-execution-closed-catalogs.md) — publisher discipline principle
- [Microsoft Learn: Organize your solutions in Power Platform](https://learn.microsoft.com/en-us/power-platform/alm/organize-solutions)
- [Microsoft Learn: ALM basics with Microsoft Power Platform](https://learn.microsoft.com/en-us/power-platform/alm/basics-alm)
- [customer-provisioning-orchestration-r1](../../projects/customer-provisioning-orchestration-r1/) — the L2 control-plane that consumes SpaarkeMaster.zip via H6
