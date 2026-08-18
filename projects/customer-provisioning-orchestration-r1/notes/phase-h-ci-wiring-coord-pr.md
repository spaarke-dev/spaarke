# Coord-PR Spec — Phase H CI-Workflows Wiring (naming-conformance + tenant-isolation + Graph parity)

> **Author**: customer-provisioning-orchestration-r1 task 088 (Wave 4 Batch 4E — serial after 067)
> **Date**: 2026-08-18
> **Target worktree**: `ci-cd-unit-test-remediation-r1` (28-day owner of `.github/workflows/**`)
> **Resolution path**: CLAUDE.md §6.5 **Path A** (documented project-scoped deferral) — matches the r3 precedent set by [`projects/code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md`](../../code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md).
> **Consolidates**: task 067's partial coord-PR spec at [`graph-app-role-parity-coord-pr.md`](./graph-app-role-parity-coord-pr.md) — §3 of this file references that file as the canonical Section-3 detail source (nightly Graph parity); this file is the umbrella coord PR that bundles all three r1-authored gates into ONE PR to the ci-cd-r1 worktree.
> **Coord contract**: r1 authors + compile-verifies each test/script in isolation; ci-cd-r1 applies the workflow diffs below in a coordinated PR after `/conflict-check`. This task (088) does **NOT** edit `.github/workflows/**` — verified by `git diff .github/workflows/` = empty at commit time.

---

## 0. TL;DR

r1 has landed three r3-fitness-function-style gates on its branch. None of them are wired into `.github/workflows/**` because that path is owned by the `ci-cd-unit-test-remediation-r1` worktree for the current 28-day window (per r3 task-042-063-ci-gate-wiring-deferral.md + `projects/INDEX.md` hot-path declaration). This PR proposes the three additive workflow diffs and packages them for a single coord PR:

| # | Gate | Layer | Posture (rollout) | Test/script location | Spec ref |
|---|---|---|---|---|---|
| 1 | Naming-conformance | **PR** (new dedicated `code-quality` job step) | ADVISORY (`continue-on-error: true`) until r1's live-drift remediation lands, then flip to blocking | `scripts/naming-conformance-check.ps1` | FR-35 + NFR-08 |
| 2 | Tenant-isolation I1–I5 ArchTests | **PR** (new dedicated `tenant-isolation` job) | BLOCKING from day 1 (audit sweep 065 zero-findings baseline established) | `tests/Spaarke.ArchTests/TenantIsolation/I{1..5}_*.cs` (5 files) | FR-28 + FR-29 + FR-30 + FR-31 + FR-32 |
| 3 | Nightly Graph app-role parity | **`nightly-health.yml`** (new dedicated job) | ADVISORY (`continue-on-error: true`) for ≥7 nightly runs, then flip to blocking | `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/GraphAppRoleParityTest.cs` | FR-13 + T3 (design.md §4B) |

All three are additive. All three fail cleanly (rollback = delete the job/step; no state to unwind). All three follow the r3 per-surface activation ramp (advisory → clean baseline → blocking) rather than a big-bang flip.

---

## 1. Section 1 — Naming-conformance ArchTest per PR (FR-35 + NFR-08)

### 1.1 What r1 landed (this PR is not required for these to be in place)

- **Script**: `scripts/naming-conformance-check.ps1` (r3 task 063; `-SelfTest` passes on this branch; real-file scan reports the live FR-29 drift by design — that drift is r1's Phase G/H remediation backlog).
- **Standard**: `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` § "KV-Secret & Resource Naming Standard (Conformance-Gated)".
- **Deploy-time invocation**: `scripts/Validate-DeployedEnvironment.ps1` §H13 already calls the script as an environment-validation gate (r1 task 021). This PR wires the same script into per-PR CI so drift is caught pre-merge rather than post-deploy.
- **Manifest generator**: `scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1` (r1 task 084) — the naming-conformance gate scans against the canonical secret-name sources this generator emits (single source of truth: seeder + Configure script + tokens doc + Bicep param default all consume `manifest.yaml`).

### 1.2 Rationale — why per-PR (not nightly)

| Concern | Detail |
|---|---|
| **Failure is deterministic** | The gate is a static regex scan (no live Azure calls, no creds, no rate limits). Runtime ~1 s on a warm shell. Per-PR is free. |
| **Feedback loop matters** | A drift-introducing PR (e.g. someone adds `SPRK-DEV-DATAVERSE-URL` to a Bicep template) is easier to fix pre-merge than post-merge. Nightly-only would let 24 h of PRs stack drift before catching. |
| **Advisory rollout is safe** | The gate currently reports real drift in live templates (FR-29 remediation backlog). Advisory posture (`continue-on-error: true`) surfaces the count as a warning without blocking merges until r1 finishes remediation. |
| **r3 precedent** | `task-042-063-ci-gate-wiring-deferral.md` places naming-conformance on the PR layer ("Naming-conformance … PR — advisory until r1 remediates live drift"). This section implements that guidance. |

### 1.3 Workflow wiring diff — new step in the existing `code-quality` job

**Preferred target**: `.github/workflows/sdap-ci.yml`, the `code-quality` job (currently runs the ADR ArchTests and dep audit; naming is the same architectural-invariant category). This keeps repo-quality gates in one job — no new job needed.

### 1.3.a Insertion diff (append AFTER the existing `ADR architecture tests (NetArchTest)` step, BEFORE `Plugin size validation`)

```yaml
      # -----------------------------------------------------------------------
      # customer-provisioning-orchestration-r1 task 088 (r3 task 063 gate wiring)
      # — Naming-conformance gate (FR-35 + NFR-08). Static regex scan for KV-
      # secret / Azure-resource naming drift. Read-only. ~1 s runtime.
      #
      # Advisory (continue-on-error: true) during r1's Phase G/H remediation
      # window — flip to blocking once r1 wires the follow-on flip signal
      # (see notes/phase-h-ci-wiring-coord-pr.md §1.4).
      # -----------------------------------------------------------------------
      - name: Naming-conformance gate (FR-35, advisory until r1 remediation lands)
        id: naming-conformance
        continue-on-error: true
        shell: pwsh
        run: ./scripts/naming-conformance-check.ps1
```

**Optional add-on** (recommended for surfacing in the same PR comment the ADR-Violations job posts): extend the `adr-pr-comment` job's Compose step to include a naming-conformance section next to ADR violations. Deferred to a follow-on if the ci-cd-r1 owner prefers to ship §1.3.a first.

### 1.3.b Required GH Actions secrets

**NONE**. The script is a static repo scan — no Azure, no Graph, no creds.

### 1.4 Failure mode + rollback + flip-to-blocking trigger

| Symptom | Cause | Response |
|---|---|---|
| Gate ADVISORY warning `env-token-in-name` (R1) | A PR added a KV-secret name containing an environment token (e.g. `SPRK-DEV-*`) | Rename to env-agnostic form per the naming standard; the script prints the offending file + name |
| Gate ADVISORY warning `casing-drift` (R2) | The same logical secret appears under >1 casing across scanned files | Reconcile to one canonical casing; the script prints all variants + files |
| Gate ADVISORY warning `vault-name-drift` (R3) | A vault name other than `sprk-{env}-kv` (except the codified `spaarke-spekvcert` legacy exception) appears | Rename to the canonical vault form; the script prints the file + name |
| Gate PASS | No drift | No action |

**Flip-to-blocking trigger** (r1 → ci-cd-r1 handoff): when r1 completes Phase G/H remediation and `pwsh scripts/naming-conformance-check.ps1` returns exit 0 in a clean commit, r1 files a follow-up PR to ci-cd-r1 removing the `continue-on-error: true` line. Do NOT flip earlier — the current live drift would immediately red every PR.

**Rollback**: delete the step. Fully additive. No secrets to clean up.

---

## 2. Section 2 — Tenant-isolation I1–I5 ArchTests per PR (FR-28..FR-32 + NFR-08)

### 2.1 What r1 landed (this PR is not required for these to be in place)

- **5 ArchTest files** (r1 task 064, namespace `Spaarke.ArchTests.TenantIsolation`, all in `tests/Spaarke.ArchTests/TenantIsolation/`):
  - `I1_NoHardcodedTenantTests.cs` — FR-28 (no hardcoded default tenant in provisioning scripts)
  - `I2_AiSearchTenantIdFilterTests.cs` — FR-29 (all AI Search queries include unconditional `tenantId eq` filter)
  - `I3_CosmosPartitionKeyTests.cs` — FR-30 (all Cosmos reads/writes include partition-key predicate)
  - `I4_SpeContainerIdLiteralTests.cs` — FR-31 (SPE container IDs derived from tenant context via `ITenantContainerResolver`)
  - `I5_GraphPerTenantTokenTests.cs` — FR-32 (Graph tokens acquired per-tenant scoped, no ambient default)
- **Audit sweep** (r1 task 065): zero findings in the current BFF/L2 baseline. This means the gate can be **BLOCKING from day 1** (no advisory rollout needed).
- **Pre-commit scan** (r1 task 066): `Register-EntraAppRegistrations.ps1:63` fix verified + pre-commit tenant-shaped GUID scan ArchTest added.

### 2.2 Rationale — why per-PR AND why blocking-from-day-1

| Concern | Detail |
|---|---|
| **Cross-tenant identity leak severity** | I1–I5 protect against silent cross-tenant data exposure — the highest-blast-radius bug class in the tenancy model (§4D). A regression here is a customer-security incident, not a code-style annoyance. Per-PR blocking is the correct posture. |
| **Failure is deterministic + fast** | All 5 tests are NetArchTest / regex-over-source scans. Runtime ~1 s each on a warm shell. Per-PR is free. No live Azure calls. |
| **Zero-findings baseline exists** | r1 task 065 audit sweep found zero violations across BFF + L2 code as of this branch's HEAD. The gate can red only on a NEW regression — no pre-existing noise to manage. |
| **Already partly covered by existing ADR run** | The existing `code-quality > ADR architecture tests (NetArchTest)` step runs the entire `Spaarke.ArchTests` project unfiltered — including I1–I5. But that step has `continue-on-error: true` AND the `adr-pr-comment` job's regex extracts only `ADR-\d+` from test names, so I1–I5 failures render as generic warnings, not the flagged blockers they should be. This section adds a **dedicated blocking job** so I1–I5 have their own PR status check. |
| **r3 precedent** | Same as §1: `task-042-063-ci-gate-wiring-deferral.md` treats fitness functions as PR-layer gates. This section adds the labeled/blocking wiring on top of the already-running ArchTest infrastructure. |

### 2.3 Workflow wiring diff — new dedicated job

**Preferred target**: `.github/workflows/sdap-ci.yml`, a NEW top-level job named `tenant-isolation`. Rationale: clean separation of the tenant-isolation status check from the noisier "ADR architecture" grouping keeps the PR check UI actionable; no `continue-on-error` because this class of failure must block merge.

### 2.3.a Insertion diff (append AFTER the `code-quality` job, BEFORE `integration-readiness`)

```yaml
  # ──────────────────────────────────────────────────────────────────────────
  # customer-provisioning-orchestration-r1 task 088 (r1 task 064 gate wiring)
  # — Tenant-isolation invariants I1..I5 (FR-28..FR-32 + design.md §4D).
  # BLOCKING from day 1 (r1 task 065 audit sweep = 0 findings baseline).
  #
  # Deliberately a SEPARATE self-contained job (same rationale as eval-gate
  # + compose-fidelity-gate in this file): existing jobs carry
  # continue-on-error: true (2026-06-24 informational posture) which would
  # swallow a tenant-isolation red. This gate must NEVER be advisory —
  # cross-tenant leak is a security incident, not a warning.
  # ──────────────────────────────────────────────────────────────────────────
  tenant-isolation:
    name: Tenant Isolation (I1–I5 invariants)
    runs-on: windows-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.x'

      - name: Cache NuGet packages
        uses: actions/cache@v5
        continue-on-error: true  # cache is an optimization, never a gate
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/Directory.Packages.props', '**/*.csproj') }}
          restore-keys: |
            ${{ runner.os }}-nuget-

      - name: Tenant-isolation invariants (I1..I5 — merge-blocking)
        # Filter by namespace: all 5 files live in
        # Spaarke.ArchTests.TenantIsolation.* — this catches I1..I5 as a
        # cohesive set + auto-picks-up any new I{N}_* tests r1 adds in the
        # future without a workflow edit.
        shell: pwsh
        run: |
          dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj `
            -c Release `
            --filter "FullyQualifiedName~Spaarke.ArchTests.TenantIsolation" `
            --logger "trx;LogFileName=tenant-isolation-results.trx" `
            --results-directory ./TestResults

      - name: Upload tenant-isolation TRX
        if: always()
        uses: actions/upload-artifact@v6
        with:
          name: tenant-isolation-results
          path: ./TestResults/tenant-isolation-results.trx
          if-no-files-found: warn
```

**Update the `summary` job's `needs:`** to include `tenant-isolation` so the CI Summary table renders the new status:

```yaml
  summary:
    name: CI Summary
    runs-on: ubuntu-latest
    needs: [security-scan, build-test, code-quality, client-quality, integration-readiness, tenant-isolation]  # ADDED tenant-isolation
    if: always()
```

...and add a `| Tenant Isolation | ${{ needs.tenant-isolation.result }} |` row to the summary markdown block in that job.

### 2.3.b Required GH Actions secrets

**NONE**. All 5 tests are static NetArchTest / regex-over-source scans — no Azure, no creds.

### 2.4 Failure mode + rollback

| Symptom | Cause | Response |
|---|---|---|
| `I1_NoHardcodedTenantTests` RED | A `*.ps1` under `scripts/` has a `[string]$TenantId = '<GUID>'` default | Remove the default; use `[Parameter(Mandatory=$true)][string]$TenantId` |
| `I2_AiSearchTenantIdFilterTests` RED | An AI Search call was added without an unconditional `tenantId eq` filter | Add the filter; refer to `docs/architecture/AI-ARCHITECTURE.md` for the canonical query builder |
| `I3_CosmosPartitionKeyTests` RED | A Cosmos read/write was added without a partition-key predicate | Add the predicate; refer to the ADR-014 partition-key pattern |
| `I4_SpeContainerIdLiteralTests` RED | A SPE container ID was added as a literal instead of via `ITenantContainerResolver` | Route through the resolver |
| `I5_GraphPerTenantTokenTests` RED | A Graph token was acquired without explicit `TenantId` | Add explicit tenant scoping per §4D I5 |

**Rollback**: delete the job + remove the `needs:` addition to `summary`. Fully additive. No secrets to clean up.

### 2.5 Interaction with the existing `code-quality > ADR architecture tests` step

The existing ADR step **already runs** I1–I5 as part of unfiltered `Spaarke.ArchTests` execution. Adding this dedicated job **does not remove** them from the ADR step — it adds a blocking, labeled check ON TOP so the failure mode is unambiguous. Two ways to run the same tests is fine (fast, deterministic, and belt-and-suspenders); no code duplication, only workflow duplication of ~5 s.

**Optional simplification** (post-flip): once this dedicated job is proven stable, ci-cd-r1 can add `--filter "FullyQualifiedName!~Spaarke.ArchTests.TenantIsolation"` to the ADR step to avoid double-running. Not required.

---

## 3. Section 3 — Nightly Graph app-role parity (FR-13 + T3)

### 3.1 Consolidation with task 067's spec

Task 067 authored the detailed coord PR spec at [`graph-app-role-parity-coord-pr.md`](./graph-app-role-parity-coord-pr.md) — a 194-line document covering the full test contract, YAML diff, GH Actions secrets contract, failure modes, rollback, coord message, and acceptance criteria for the nightly Graph app-role parity gate. This section §3 **preserves 067's spec as the canonical detail source** and provides an umbrella summary here for the ci-cd-r1 reviewer working from this single file.

> **Rule**: for exact YAML + secrets contract + full rollback procedure for §3, read [`graph-app-role-parity-coord-pr.md`](./graph-app-role-parity-coord-pr.md) §3, §4, §5. This document (§3.2 below) summarizes but does not replace.

### 3.2 What r1 landed + summary of the wiring

- **Test project**: `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/` (r1 task 067; net10.0; compile-clean; `IsPackable=false`; not deployed).
- **Test file**: `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/GraphAppRoleParityTest.cs` — Two tests:
  1. `[SkippableFact]` live UAMI SP ↔ `GraphAppRoles.cs` parity via Graph SDK v6 (skips cleanly when `AZURE_TENANT_ID` or `UAMI_SP_OBJECT_ID` missing).
  2. `[Fact]` pure BFF↔L2 mirror drift check (`Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.All` vs `Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity.L2GraphAppRolesRegistry.GetAll()`) — no creds required.
- **Target workflow**: `.github/workflows/nightly-health.yml` — verified to EXIST in this branch (contains flake-hunt, bundle-size, vuln-scan, full-integration, coverage-observation, trivy-fs, dep-audit, report jobs). This coord PR appends the new `graph-app-role-parity` job per §3.a of the 067 spec.
- **Posture**: ADVISORY (`continue-on-error: true`) for ≥7 consecutive nightly runs, then flip to blocking (matches r3 per-surface activation ramp).
- **Secrets required**: 4 (`NIGHTLY_UAMI_SP_OBJECT_ID`, `NIGHTLY_AZURE_TENANT_ID`, `NIGHTLY_GRAPH_READER_CLIENT_ID`, `NIGHTLY_SUBSCRIPTION_ID`) — see 067 spec §3.b for exact values, rotation policy, and the "read-only Graph scopes MUST NOT include `AppRoleAssignment.ReadWrite.All`" security guardrail.

### 3.3 Optional per-PR add-on (from 067 spec §4.b)

If ci-cd-r1 wants per-PR coverage for the BFF↔L2 mirror drift check (Test 2 — no live creds required), append the single-step invocation from 067 spec §4.b to the existing per-PR test job. Alternatively, this per-PR mirror-drift check could be folded into the Section-2 `tenant-isolation` job as an additional filter (since the L2 mirror is a form of tenant-scoping-invariant drift). Ci-cd-r1's choice.

### 3.4 Failure mode + rollback + flip-to-blocking trigger

Full detail: [`graph-app-role-parity-coord-pr.md`](./graph-app-role-parity-coord-pr.md) §5.

**Flip-to-blocking trigger**: same as §1.4 — after ≥7 consecutive nightly runs return GREEN or SKIPPED (never RED, never job-CANCELLED), r1 (or the follow-on r2 project) files a follow-up PR removing the `continue-on-error: true` line.

**Rollback**: delete the job from `nightly-health.yml` + delete the 4 GH Actions secrets. Fully additive. No production surface impact.

### 3.5 Update the `nightly-health.yml` `report` job

If ci-cd-r1 adds the `graph-app-role-parity` job, they should also:
1. Add `graph-app-role-parity` to the `report` job's `needs:` array (line 663 of `nightly-health.yml`).
2. Add a corresponding `GRAPH_PARITY_RESULT: ${{ needs.graph-app-role-parity.result }}` env line and a section to the report body block (heredoc at line 703).

This keeps the rolling nightly tracking issue accurate. Full YAML fragment available in 067 spec §3.a (surrounding context).

---

## 4. Coord message body (paste-ready — mention to ci-cd-r1 in the coord PR)

> **Owner**: whoever picks this up in the `ci-cd-unit-test-remediation-r1` worktree.
>
> **What**: apply THREE additive workflow diffs to CI:
> 1. `sdap-ci.yml > code-quality` job: add naming-conformance step per §1.3.a of `projects/customer-provisioning-orchestration-r1/notes/phase-h-ci-wiring-coord-pr.md` (from r1's branch).
> 2. `sdap-ci.yml`: add a new `tenant-isolation` job per §2.3.a of the same file (BLOCKING from day 1).
> 3. `nightly-health.yml`: add a new `graph-app-role-parity` job per §3 of the same file (delegates to `graph-app-role-parity-coord-pr.md` §3.a for the exact YAML).
>
> **Why**: r1 has landed three r3-fitness-function-style gates (r3 task 063 naming-conformance + r1 tasks 064/067). Per `parallel-safe=false` + CLAUDE.md §6.5 Path A + `projects/INDEX.md` hot-path declaration (`ci-workflows=Y`), r1 does not edit `.github/workflows/**` in isolation — the wiring is your worktree's territory for the 28-day window.
>
> **Prereqs**:
> - `/conflict-check` on `sdap-ci.yml` + `nightly-health.yml` before your PR — r1 has NOT touched either file.
> - Confirm the 4 GH Actions secrets in §3 (Graph parity) are populated (or accept that the nightly job SKIPs cleanly until they are).
> - r1's script (`scripts/naming-conformance-check.ps1`) + 5 ArchTest files (`tests/Spaarke.ArchTests/TenantIsolation/I{1..5}_*.cs`) + nightly test project (`tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/`) are ALL present on r1's branch — the workflow diffs only reference them, r1 has authored them.
>
> **Landing posture**:
> - Section 1 (naming-conformance): ADVISORY. Flip to blocking after r1 completes Phase G/H remediation (a subsequent small PR to your worktree).
> - Section 2 (I1–I5): BLOCKING from day 1 (r1 task 065 audit sweep = 0 findings baseline).
> - Section 3 (Graph parity): ADVISORY. Flip to blocking after ≥7 consecutive GREEN nightlies.
>
> **Cross-references**:
> - r1 spec: `projects/customer-provisioning-orchestration-r1/spec.md` FR-13, FR-28..FR-32, FR-35, NFR-08
> - r3 precedent: `projects/code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md`
> - Detailed §3 source: `projects/customer-provisioning-orchestration-r1/notes/graph-app-role-parity-coord-pr.md` (from r1 task 067; this doc §3 references but does not replace)
> - r1 downstream: task 088 authored THIS coord spec; when your PR merges, please tag @r1-owner so 088's downstream is closable.

---

## 5. Verification of referenced paths (r1 task 088 acceptance criterion)

Every path referenced above has been verified against `work/customer-provisioning-orchestration-r1` HEAD (2026-08-18) using `Bash > ls`:

| Referenced path | Exists? | Notes |
|---|---|---|
| `scripts/naming-conformance-check.ps1` | ✅ | r3 task 063; `-SelfTest` passes |
| `scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1` | ✅ | r1 task 084 |
| `tests/Spaarke.ArchTests/TenantIsolation/I1_NoHardcodedTenantTests.cs` | ✅ | r1 task 064; namespace `Spaarke.ArchTests.TenantIsolation` |
| `tests/Spaarke.ArchTests/TenantIsolation/I2_AiSearchTenantIdFilterTests.cs` | ✅ | r1 task 064 |
| `tests/Spaarke.ArchTests/TenantIsolation/I3_CosmosPartitionKeyTests.cs` | ✅ | r1 task 064 |
| `tests/Spaarke.ArchTests/TenantIsolation/I4_SpeContainerIdLiteralTests.cs` | ✅ | r1 task 064 |
| `tests/Spaarke.ArchTests/TenantIsolation/I5_GraphPerTenantTokenTests.cs` | ✅ | r1 task 064 |
| `tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj` | ✅ | Existing test project — no new csproj needed |
| `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/GraphAppRoleParityTest.cs` | ✅ | r1 task 067; compile-clean |
| `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/Sprk.Provisioning.ControlPlane.NightlyTests.csproj` | ✅ | r1 task 067; net10.0; `IsPackable=false` |
| `.github/workflows/sdap-ci.yml` | ✅ | Target for §1 + §2 diffs |
| `.github/workflows/nightly-health.yml` | ✅ | Target for §3 diff; contains report job pattern §3.5 references |
| `projects/customer-provisioning-orchestration-r1/notes/graph-app-role-parity-coord-pr.md` | ✅ | r1 task 067 partial spec (this doc §3 references) |
| `projects/code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md` | ✅ | r3 precedent doc |

All paths resolve. No missing artifacts. Ci-cd-r1 can proceed on the diffs without waiting for r1 to author anything additional (with the exception of the future flip-to-blocking follow-up PRs per §1.4 and §3.4).

---

## 6. Acceptance for this coord PR (ci-cd-r1's PR, not r1's)

- [ ] `sdap-ci.yml > code-quality` job contains the naming-conformance step exactly as §1.3.a (or equivalent, retaining `continue-on-error: true` during rollout).
- [ ] `sdap-ci.yml` contains the new `tenant-isolation` job exactly as §2.3.a (no `continue-on-error`; BLOCKING).
- [ ] `sdap-ci.yml > summary` job's `needs:` includes `tenant-isolation` + the summary markdown block includes a Tenant Isolation row.
- [ ] `nightly-health.yml` contains the `graph-app-role-parity` job per §3 (delegates to 067 spec §3.a for exact YAML).
- [ ] `nightly-health.yml > report` job's `needs:` + env + body updated per §3.5.
- [ ] 4 GH Actions secrets for §3 exist (or are documented as intentionally deferred, causing the job to SKIP cleanly).
- [ ] `/conflict-check` output attached in the PR description showing zero conflicts with in-flight ci-cd-r1 work.
- [ ] First per-PR run completes; results recorded (naming-conformance ADVISORY warning list, tenant-isolation GREEN, Graph parity nightly GREEN or SKIPPED).
- [ ] Advisory posture held ≥7 nightly runs for §3 before flipping to blocking; r1 files a follow-on PR for §1's blocking flip after Phase G/H remediation.
- [ ] `projects/customer-provisioning-orchestration-r1/notes/phase-h-ci-wiring-coord-pr.md` (this file) referenced in the PR description.
- [ ] r1 tasks 088 + 067 notified when merged so both can mark their downstream coord as satisfied.

---

## 7. What this task did NOT touch (r1 task 088 negative acceptance)

Per POML constraint (`source="CLAUDE.md-§6.5"` + `parallel-safe=false` + `<constraint source="project">SCOPE: Coord-PR spec is DOCUMENTATION — this task does NOT edit .github/workflows/** files.</constraint>`):

- `.github/workflows/**` — verified empty diff at commit time (`git diff .github/workflows/` returns nothing).
- Any file under `.claude/**` — sub-agent write boundary per root CLAUDE.md §3.
- Test files in `tests/**` — read-only for path verification.
- BFF or L2 source — this is a pure doc task.

The three workflow diffs described in §1.3.a, §2.3.a, and §3 (deferring to 067 spec §3.a) are applied by ci-cd-r1's worktree, not by r1.
