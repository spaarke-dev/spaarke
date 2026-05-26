# CI Workflow Inventory (Task 018)

> **Captured**: 2026-05-24
> **Source**: `.github/workflows/sdap-ci.yml` + `.github/workflows/deploy-bff-api.yml`
> **Feeds**: Phase 6 tasks 071-075 (CI guards) + task 075 (workflow alignment with G-2/G-3)

---

## `sdap-ci.yml` — step inventory

| # | Step name | Action used (if any) | Phase 6 plug-in point? |
|---|---|---|---|
| 1 | Checkout | `actions/checkout@v6` | — |
| 2 | Run Trivy vulnerability scanner | `aquasecurity/trivy-action@master` | Related to **FR-C3** (HIGH vuln transitive fail); may need integration with `dotnet list --vulnerable` |
| 3 | Upload Trivy scan results | `github/codeql-action/upload-sarif@v4` | — |
| 4 | Checkout (build job) | `actions/checkout@v6` | — |
| 5 | Setup .NET 8 | `actions/setup-dotnet@v4` | — |
| 6 | Setup .NET Framework targeting pack | (inline) | — |
| 7 | Cache NuGet packages | `actions/cache@v5` | — |
| 8 | Restore dependencies | (inline) | — |
| 9 | Build | (inline) | — |
| 10 | Test with coverage | (inline) | — |
| 11 | Upload test results | `actions/upload-artifact@v6` | — |
| 12 | Upload coverage reports | `actions/upload-artifact@v6` | — |
| 13 | Configure git line endings | (inline) | — |
| 14 | Checkout (frontend job) | `actions/checkout@v6` | — |
| 15 | Setup Node.js | `actions/setup-node@v4` | — |
| 16 | Cache root npm dependencies | `actions/cache@v5` | — |
| 17 | Cache PCF npm dependencies | `actions/cache@v5` | — |
| 18 | Install root dependencies (Prettier) | (inline) | — |
| 19 | Prettier format check | (inline) | — |
| 20 | Install PCF dependencies (ESLint) | (inline) | — |
| 21 | ESLint strict check | (inline) | — |
| 22 | Checkout (ADR/format job) | `actions/checkout@v6` | — |
| 23 | Setup .NET 8 | `actions/setup-dotnet@v4` | — |
| 24 | Restore dependencies | (inline) | — |
| 25 | Format verification | (inline) | — |
| 26 | ADR policy check (Legacy PowerShell) | (inline) | — |

**Phase 6 plug-in points (sdap-ci.yml)**:
- After step 9 (Build): add **FR-C1** non-Linux RID detection (task 071)
- After step 9 (Build): add **FR-C2** `*.js.map` exclusion check (task 072)
- Replace/extend step 2 (Trivy) or add new step: **FR-C3** HIGH-severity vuln transitive fail (task 073)
- New step: **FR-C5** publish size guard (task 070)
- New step: **FR-C6** direct CRUD→AI injection grep (task 082)
- New step: **FR-C4** PR-label escape hatches `[allow-size-growth]` / `[allow-vuln]` / `[allow-direct-ai-inject]` (task 074)

---

## `deploy-bff-api.yml` — step inventory

| # | Step name | Action used (if any) | Phase 6 plug-in point? |
|---|---|---|---|
| 1 | Checkout | `actions/checkout@v6` | — |
| 2 | Setup .NET | `actions/setup-dotnet@v4` | — |
| 3 | Cache NuGet packages | `actions/cache@v5` | — |
| 4 | Restore dependencies | (inline) | — |
| 5 | Build | (inline) | — |
| 6 | Publish | (inline) | — |
| 7 | Upload build artifact | `actions/upload-artifact@v6` | — |
| 8 | Checkout (test job) | `actions/checkout@v6` | — |
| 9 | Setup .NET | `actions/setup-dotnet@v4` | — |
| 10 | Cache NuGet packages | `actions/cache@v5` | — |
| 11 | Restore dependencies | (inline) | — |
| 12 | Run unit tests | (inline) | — |
| 13 | Upload test results | `actions/upload-artifact@v6` | — |
| 14 | Checkout (deploy job) | `actions/checkout@v6` | — |
| 15 | Download build artifact | `actions/download-artifact@v7` | — |
| 16 | Azure Login | `azure/login@v2` | — |
| 17 | Create deployment package | (inline) | — |
| 18 | Deploy to staging slot | (inline) | — |
| 19 | Wait for staging slot startup | (inline) | **FR-D5** — verify 120s window matches `Deploy-BffApi.ps1` per G-2 (task 075) |
| 20 | Health check — staging `/healthz` | (inline) | **FR-D5** plug-in point |
| 21 | Smoke test — staging `/ping` | (inline) | — |
| 22 | Azure Login (prod swap) | `azure/login@v2` | — |
| 23 | Swap staging → production | (inline) | — |
| 24 | Wait for DNS/routing stabilization | (inline) | — |
| 25 | Health check — production `/healthz` | (inline) | — |
| 26 | Smoke test — production `/ping` | (inline) | — |
| 27 | Azure Login (rollback path) | `azure/login@v2` | — |
| 28 | Swap back — rollback to previous version | (inline) | NFR-06 — rollback path validated by task 009 drill |
| 29 | Verify rollback health | (inline) | — |
| 30 | Write Summary | (inline) | — |

**Phase 6 plug-in points (deploy-bff-api.yml)**:
- Step 19 wait window → **FR-D5 task 075** alignment with `Deploy-BffApi.ps1` 120s tolerance (per FAILURE-MODES.md G-2)

---

## G-3 Action Version Sanity Check

Unique `uses:` lines extracted from both workflows:

| Action | Pinned version | Notes |
|---|---|---|
| `actions/cache` | `@v5` | Valid registry version |
| `actions/checkout` | `@v6` | ⚠️ FLAG — verify against marketplace; v4 was latest as of common knowledge. Operator confirms or downgrades. |
| `actions/download-artifact` | `@v7` | ⚠️ FLAG — verify |
| `actions/github-script` | `@v8` | ⚠️ FLAG — verify |
| `actions/setup-dotnet` | `@v4` | Valid (latest as of common knowledge) |
| `actions/setup-node` | `@v4` | Valid; Dependabot PR #244 pending bump to v6 |
| `actions/upload-artifact` | `@v6` | ⚠️ FLAG — verify; Dependabot PR #203 pending bump to v7 |
| `azure/login` | `@v2` | Valid; Dependabot PR #263 pending bump to v3 |
| `aquasecurity/trivy-action` | `@master` | ⚠️ FLAG — pinning to `@master` is anti-pattern; should pin to specific version per G-3 |
| `github/codeql-action/upload-sarif` | `@v4` | Valid |

**G-3 findings for Phase 6 task 075**:
1. **`aquasecurity/trivy-action@master`** — explicit anti-pattern; replace with a tagged version (e.g., `@v0.x.x`).
2. **Several actions pinned to versions newer than common knowledge cutoff** (checkout@v6, download-artifact@v7, github-script@v8, upload-artifact@v6). Phase 6 task 075 must verify each against current Marketplace at execution time — these may be valid if the org runs aggressive Dependabot bumps.
3. **3 open Dependabot PRs** propose bumps to setup-node, azure/login, upload-artifact, dawidd6/action-download-artifact, actions/setup-node. Phase 6 must coordinate with these PRs.

---

## CI guard implementation roadmap for Phase 6

Bringing this inventory together with `spec.md` Outcome C requirements:

| FR | New CI step (placement) | Target workflow |
|---|---|---|
| FR-C1 (non-Linux RID detection) | After Build step | `sdap-ci.yml` and/or `deploy-bff-api.yml` |
| FR-C2 (`*.js.map` exclusion) | After Build step | `sdap-ci.yml` and/or `deploy-bff-api.yml` |
| FR-C3 (HIGH vuln transitive) | Replace/extend Trivy step | `sdap-ci.yml` |
| FR-C4 (PR-label escape hatches) | Pre-check + skip logic | both workflows |
| FR-C5 (size guard) | After Publish step | `deploy-bff-api.yml` |
| FR-C6 (direct CRUD→AI grep) | After Build step | `sdap-ci.yml` |
| FR-D5 (G-2 health-check window) | Existing step 19/20 update | `deploy-bff-api.yml` |
| FR-D6 (G-3 action version fixes) | Workflow file edits | `deploy-bff-api.yml` (+ `sdap-ci.yml`) |
