# Coord-PR Spec — Nightly Graph App-Role Parity CI Wiring

> **Status (2026-08-18)**: **PRESERVED AS DETAIL SOURCE** for §3 of the umbrella coord PR at [`phase-h-ci-wiring-coord-pr.md`](./phase-h-ci-wiring-coord-pr.md) (r1 task 088). This file is the canonical source for the nightly-Graph-parity YAML + secrets contract + failure modes + rollback. The umbrella spec bundles this §3 with §1 (naming-conformance) + §2 (I1–I5 ArchTests) into a single coord PR to `ci-cd-unit-test-remediation-r1`. Ci-cd-r1 reviewers should read the umbrella FIRST for the overall context, then this file for §3 detail.
>
> **Author**: customer-provisioning-orchestration-r1 task 067 (Wave C6 Batch 4E)
> **Date**: 2026-08-18
> **Target worktree**: `ci-cd-unit-test-remediation-r1` (28-day owner of `.github/workflows/**`)
> **Resolution path**: CLAUDE.md §6.5 **Path A** (documented project-scoped deferral) — matches the r3 precedent set by [`projects/code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md`](../../code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md)
> **Coord contract**: r1 authors + compile-verifies the test project in isolation; ci-cd-r1 applies the workflow-file diff below in a coordinated PR after `/conflict-check`.
> **Consumed by**: r1 task 088 (Phase H CI-workflows coord PR) — 088 references this file as its §3 detail source (see 088 spec §3.1).

---

## 1. What r1 landed (this PR is not required for these to be in place)

- **Test project**: `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/Sprk.Provisioning.ControlPlane.NightlyTests.csproj`
  - `TargetFramework=net10.0`; `IsPackable=false`; not deployed
  - `PackageReference`s: `Microsoft.NET.Test.Sdk 17.11.1`, `xunit 2.9.0`, `xunit.runner.visualstudio 2.8.2`, `FluentAssertions 6.12.0`, `Xunit.SkippableFact 1.5.61`, `Microsoft.Graph 6.5.0` (v6 per NFR-09), `Azure.Identity 1.21.0`, `Microsoft.Extensions.Logging.Abstractions 10.0.11`
  - `ProjectReference`s: `Sprk.Bff.Api.csproj`, `Sprk.Provisioning.ControlPlane.csproj`
  - Added to `spaarke.sln` via `dotnet sln add`
  - **Compile-clean**: `dotnet build -c Release` → `0 Warning(s), 0 Error(s)` (task 067 acceptance)
- **Test file**: `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/GraphAppRoleParityTest.cs`
  - **Test 1 (`[SkippableFact]`)** — live UAMI SP ↔ GraphAppRoles.cs parity via Graph SDK v6 with explicit `TenantId`; skips cleanly when `AZURE_TENANT_ID` or `UAMI_SP_OBJECT_ID` is absent; catches `ODataError` (NFR-09); diff-formatted failure listing missing/extra with displayName + GUID.
  - **Test 2 (`[Fact]`)** — pure BFF↔L2 mirror parity (`Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.All` vs `Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity.L2GraphAppRolesRegistry.GetAll()`); runs unconditionally, no live creds required. This satisfies the L2 mirror's own `IGraphAppRolesRegistry.cs` file-header contract that names task 067 as the drift guard.

**r1 did NOT edit `.github/workflows/**`** per POML SCOPE constraint (`source="CLAUDE.md-§6.5"`) + `parallel-safe=false`. The workflow diff is described here for ci-cd-r1 to apply.

---

## 2. Rationale — why nightly, not per-PR

Ruled by **spec.md ADR-038 §7** rationale + the r3 coord deferral note. Both preclude per-PR wiring:

| Concern | Detail |
|---|---|
| **Graph API latency** | UAMI SP `appRoleAssignments` requires a Graph SP resolve (~200 ms) + assignment list (~200-500 ms). N concurrent PRs → per-tenant rate limiting. |
| **Graph tenant-level throttling** | The Graph `application/appRoleAssignments` endpoint has a per-tenant baseline of ~2000 req/10-min. A per-PR gate + branch churn burst = self-DOS. |
| **Drift signal is slow-moving** | Portal-side role revocation or a `GraphAppRoles.cs` add without an H10 run is a hours-to-days drift; 24 h detection is sufficient. |
| **Live creds required** | Per-PR runs on public forks / dependabot cannot receive workflow secrets — the test would skip 100 % of the time, defeating the point. |
| **r3 precedent** | `task-042-063-ci-gate-wiring-deferral.md` § "The coordinated-PR checklist" already lists this exact gate as **nightly only** (row: `Graph app-role parity … nightly only (live-Graph; NOT per-PR) … 062 landed ✅ + Graph creds present; skip/no-op cleanly otherwise`). |

**Per-PR fallback**: Test 2 (BFF↔L2 mirror drift) is a pure structural check with no live-cred dependency. If ci-cd-r1 wants to catch mirror drift per-PR without live Graph, add ONE `--filter` invocation to an existing per-PR job (see § 4.b optional add-on below).

---

## 3. Workflow wiring diff — RECOMMENDED (new dedicated nightly job)

**Preferred**: extend the existing `nightly-health.yml` workflow (r3 task CICD-043 augmented Tier-3) with a new job. Rationale: keeps all nightly Graph/live-tenant checks in one workflow that already carries the shared credential setup + concurrency guard. Assumes standard OIDC federated MI login pattern already in use for other nightly Azure operations.

### 3.a — Insertion diff (append AFTER the last existing job in `nightly-health.yml`)

```yaml
  # -----------------------------------------------------------------------
  # customer-provisioning-orchestration-r1 task 067 — Nightly Graph app-role
  # parity for UAMI SP ↔ GraphAppRoles.cs (14 roles). T3 silent-fail-trap
  # safety net. Coord-PR spec: projects/customer-provisioning-orchestration-r1
  # /notes/graph-app-role-parity-coord-pr.md.
  # -----------------------------------------------------------------------
  graph-app-role-parity:
    name: Graph App-Role Parity (UAMI SP vs GraphAppRoles.cs)
    runs-on: ubuntu-latest
    # Advisory during rollout window (per r3 deferral doc — do NOT flip
    # blocking until zero-findings baseline holds ≥ 7 nightly runs).
    continue-on-error: true
    permissions:
      id-token: write         # OIDC federated credential for Azure login
      contents: read
    env:
      # UAMI_SP_OBJECT_ID: SERVICE PRINCIPAL OBJECT ID (not appId) of the
      # target customer's UAMI. Stored per-environment in GH Actions secrets.
      UAMI_SP_OBJECT_ID: ${{ secrets.NIGHTLY_UAMI_SP_OBJECT_ID }}
      # AZURE_TENANT_ID: the Entra tenant hosting the UAMI (§4D I5 —
      # explicit tenantId; no ambient default).
      AZURE_TENANT_ID: ${{ secrets.NIGHTLY_AZURE_TENANT_ID }}
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET SDK (pin to global.json)
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Azure login (OIDC federated MI)
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.NIGHTLY_GRAPH_READER_CLIENT_ID }}
          tenant-id: ${{ secrets.NIGHTLY_AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.NIGHTLY_SUBSCRIPTION_ID }}

      - name: Restore + build nightly test project
        run: |
          dotnet restore tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/
          dotnet build tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/ \
            -c Release --no-restore

      - name: Run Graph app-role parity test
        # xunit forwards the diff-formatted Assert.Fail message into the
        # workflow log — no --logger-html needed for triage.
        run: |
          dotnet test tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/ \
            -c Release --no-build --logger "console;verbosity=normal"
```

### 3.b — Required GH Actions secrets

Add these to the repository (or org) secrets. **All three** are needed for the test to run; if any is missing, the test SKIPS cleanly and the job still passes (advisory posture).

| Secret name | Value | Rotation |
|---|---|---|
| `NIGHTLY_UAMI_SP_OBJECT_ID` | Service principal OBJECT id of the target customer's UAMI (from `mi-bff-api-{env}`) | Per new customer / stamp |
| `NIGHTLY_AZURE_TENANT_ID` | Entra tenant id hosting the UAMI (currently `a221a95e-6abc-4434-aecc-e48338a1b2f2` for `spaarkedev1`) | Rare |
| `NIGHTLY_GRAPH_READER_CLIENT_ID` | Client id of a federated-MI-backed app registration with `Application.Read.All` + `Directory.Read.All` app roles on Microsoft Graph, and federated credential for this repo's `main` branch nightly workflow | Per rotation policy |
| `NIGHTLY_SUBSCRIPTION_ID` | Subscription id containing the target UAMI (needed by `azure/login@v2` even for Graph-only ops) | Rare |

**Note**: the app registration behind `NIGHTLY_GRAPH_READER_CLIENT_ID` needs **read-only** Graph roles — it must NEVER be granted app-role-write scopes (adding `AppRoleAssignment.ReadWrite.All` to this SP would let a compromised nightly workflow grant itself arbitrary Graph permissions).

---

## 4. Alternative wirings (choose ONE of §3 or §4.a; §4.b is an optional add-on)

### 4.a — Alternative: add to `sdap-ci.yml`'s existing `nightly-*` job (if one exists)

If `sdap-ci.yml` already has a `schedule:`-triggered job (r3 CICD-043 mentions Tier-3 augmentations), append the four steps above as additional steps rather than creating a new job. Coord check: `/conflict-check` ci-cd-r1's current `sdap-ci.yml` layout first.

### 4.b — Optional per-PR add-on: BFF↔L2 mirror drift check (no live creds)

If ci-cd-r1 wants per-PR coverage for BFF↔L2 mirror drift (Test 2 in `GraphAppRoleParityTest.cs`), append this ONE step to the existing per-PR test job in `ci-tier1-blocking.yml`:

```yaml
      - name: L2 GraphAppRoles mirror parity (structural — no live Graph)
        run: |
          dotnet test tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/ \
            -c Release --no-build \
            --filter "FullyQualifiedName~L2GraphAppRolesRegistry_MirrorsBffGraphAppRolesConstant"
```

This is compile-time + reflection-only (no Graph, no creds, ~1 s runtime). Value: catches BFF↔L2 mirror drift at PR-time, complementing the nightly UAMI SP check. Cost: adds ~10 s to the tier-1 per-PR job (build + one test).

---

## 5. Failure mode + rollback

### 5.a — Failure modes

| Symptom | Cause | Response |
|---|---|---|
| Nightly job RED with `Missing roles (N)` | Someone revoked a role in Portal, or `GraphAppRoles.cs` added a role that H10 hasn't propagated | (i) Run `scripts/Grant-GraphAppRoles.ps1` against the target UAMI SP; OR (ii) run H10 for the target customer. The `Missing roles` list is copy-paste-actionable. |
| Nightly job RED with `Extra roles (N)` | `GraphAppRoles.cs` is missing a role that scripts/H10 have granted, OR a stale role from a superseded module | Compare against `docs/guides/auth-deployment-setup.md` §5 runbook; either add to `GraphAppRoles.cs` (and re-verify with `az ad sp show`) or revoke the extra grant. |
| Nightly job RED with `Graph SDK v6 query failed` | Federated MI missing `Application.Read.All` / `Directory.Read.All`, wrong `UAMI_SP_OBJECT_ID`, wrong `AZURE_TENANT_ID` | Fix the misconfig; the diagnostic in `Assert.Fail` message names all three suspects explicitly. |
| Nightly job RED with `BFF ↔ L2 mirror DRIFT DETECTED` | Someone edited `GraphAppRoles.cs` without updating `L2GraphAppRolesRegistry.cs` (or vice versa) | Reconcile per the diff — the failure message names the file paths + the specific bff-only / l2-only / GUID-mismatch entries. |
| Job SKIPPED (no failure) | Secrets not set — expected during rollout / on forks | No action; SKIP is intended behavior. |

### 5.b — Rollback

The nightly wiring is fully additive:
1. **Remove the workflow job** — delete the `graph-app-role-parity:` job from `nightly-health.yml`. No other job depends on it.
2. **Retain the test project** — leaving the compiled artifact under `tests/integration/**` costs nothing at runtime; only affects local `dotnet test` invocations at the solution level.
3. **Remove the secrets** — optional; unreferenced GH Actions secrets are dormant.

No production surface, no BFF publish size delta, no ADR-030 kill-switch needed.

---

## 6. Coord message body (paste-ready — mention to ci-cd-r1 in the coord PR)

> **Owner**: whoever picks this up in the `ci-cd-unit-test-remediation-r1` worktree
>
> **What**: apply the workflow diff in § 3.a of `projects/customer-provisioning-orchestration-r1/notes/graph-app-role-parity-coord-pr.md` (from the customer-provisioning-orchestration-r1 branch) to `.github/workflows/nightly-health.yml`. r1's test project + test are already landed on r1's branch — this PR only touches `.github/workflows/**`.
>
> **Why**: r1 task 067 authored the nightly UAMI SP ↔ `GraphAppRoles.cs` parity test (T3 silent-fail-trap safety net per spec.md FR-13 + FR-33). Per `parallel-safe=false` + CLAUDE.md §6.5 Path A, r1 does not edit `.github/workflows/**` in isolation — the wiring is your worktree's territory.
>
> **Prereqs**:
> - `/conflict-check` on `nightly-health.yml` before your PR — r1 has not touched this file.
> - Confirm the 4 GH Actions secrets in § 3.b are populated (or accept that the job SKIPs cleanly until they are).
> - Ensure the federated-MI app-reg behind `NIGHTLY_GRAPH_READER_CLIENT_ID` has ONLY the read-only Graph scopes listed in § 3.b — no `AppRoleAssignment.ReadWrite.All`.
>
> **Landing**: recommend advisory (`continue-on-error: true`) for ≥ 7 consecutive nightly runs, then flip to blocking. This matches the r3 deferral doc's per-surface activation ramp (`… never red before their owning task lands … each gate flips blocking per-surface as it reaches zero findings`).
>
> **Optional add-on**: § 4.b describes a per-PR structural check (BFF ↔ L2 mirror drift, no live Graph, ~10 s runtime). Take it or leave it — the nightly job alone satisfies r1 task 067's acceptance criteria.
>
> **Cross-references**:
> - r1 spec: `projects/customer-provisioning-orchestration-r1/spec.md` FR-13 + FR-33 + NFR-09 + §4D I5
> - r3 precedent: `projects/code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md`
> - r1 downstream consumer: task 088 (Phase H CI-workflows coord PR — will bundle this wiring alongside 084-087 secret-catalog work if we prefer one coord PR over two)

---

## 7. Acceptance for this coord PR (ci-cd-r1's PR, not r1's)

- [ ] `nightly-health.yml` (or `sdap-ci.yml` per § 4.a) contains the `graph-app-role-parity` job exactly as in § 3.a (or equivalent).
- [ ] 4 GH Actions secrets in § 3.b exist (or are documented as intentionally deferred, causing the job to SKIP).
- [ ] First nightly run completes; result recorded (RED with clean diff / GREEN / SKIPPED).
- [ ] Advisory posture (`continue-on-error: true`) held ≥ 7 nightly runs before flipping to blocking.
- [ ] `projects/customer-provisioning-orchestration-r1/notes/graph-app-role-parity-coord-pr.md` referenced in the PR description.
- [ ] r1 task 088 notified when merged so it can mark 067's downstream coord as satisfied.
