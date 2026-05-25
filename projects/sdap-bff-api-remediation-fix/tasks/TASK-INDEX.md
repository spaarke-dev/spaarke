# Task Index — sdap-bff-api-remediation-fix

> **Project**: BFF API Remediation & Publish Debt
> **Created**: 2026-05-20 by `/project-pipeline`
> **Total tasks**: 63 (Phase 0–6 + wrap-up). Revised 2026-05-24 per senior review: added 009 (rollback drill / G5), 038 (DI baseline / ADR-010), 082 (FR-C6 CI gate / M5 binding); repurposed 002 (operator-only model per UQ-01 RESOLVED + NFR-08 revised); expanded 004 scope (all active BFF projects per M3).
> **Calendar estimate**: 4–6 weeks (bake-window dominated)

## Status Legend

| Symbol | Meaning |
|---|---|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Complete |
| ⛔ | Blocked |
| ⏸ | Deferred (e.g., conditional on UQ resolution) |

---

## Phase 0 — Pre-flight resolution (9 tasks)

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 001 | Owner sign-off on design.md §3 Resolved Decisions | ✅ | No | STANDARD | — |
| 002 | Document operator-only approval model (UQ-01 RESOLVED 2026-05-24) | ✅ | Yes (Group A) | STANDARD | 001 |
| 003 | Determine prod deploy process scope (UQ-02) | ✅ | Yes (Group A) | STANDARD | 001 |
| 004 | Enumerate + coordinate ALL active BFF-touching projects (UQ-03 expanded per M3) | ✅ | Yes (Group A) | STANDARD | 001 |
| 005 | Coordinate baseline window + facade adoption agreement (UQ-04 + G4) | ✅ | Yes (Group A) | STANDARD | 001 |
| 006 | Confirm CI guard size ceiling (UQ-05) | ✅ | Yes (Group A) | STANDARD | 001 |
| 007 | Confirm Outcome E scope + facade granularity + G1 handler reconciliation (UQ-06, UQ-07, G1) | ✅ | Yes (Group A) | STANDARD | 001 |
| 009 | Rollback drill — verify NFR-06 (<10 min) wall-clock (G5) | ✅ | No | STANDARD | 001 |
| 008 | Phase 0 gate review — all checkboxes resolved | ✅ | No | STANDARD | 002–007, 009 |

---

## Phase 1 — Inventory (READ-ONLY, 9 tasks)

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 010 | Direct + transitive package lists | ✅ | Yes (Group B) | STANDARD | 008 |
| 011 | Vulnerable + outdated package scan | ✅ | Yes (Group B) | STANDARD | 008 |
| 012 | Pre-release tracker + inline pinning rationale | ✅ | Yes (Group B) | STANDARD | 008 |
| 013 | Project reference graph | ✅ | Yes (Group B) | STANDARD | 008 |
| 014 | Direct-package static usage map | ✅ | Yes (Group B) | STANDARD | 008 |
| 015 | Reflection-load dynamic probe (pragmatic alternative — deps.json + DI grep) | ✅ | No (build instrumentation) | STANDARD | 008 |
| 016 | Native binary + wwwroot asset inventory + size-by-category | ✅ | Yes (Group C) | STANDARD | 008 |
| 017 | App Service runtime + deploy SHAs + zip metrics | ✅ | Yes (Group C) | STANDARD | 008 |
| 018 | CI workflow inventory + G-3 version sanity + commit INVENTORY.md | ✅ | No | STANDARD | 010–017 |
| 019 | Migrate dev BFF from Windows to Linux App Service (pre-Phase-2 infra; resolves Finding 1) | ✅ | No | STANDARD | 018 |
| 023 | Multi-identity credential refactor — DI-singleton TokenCredential (auth-r2 follow-on bug; Phase 2.5 discovery) | ✅ | No | FULL | 022 |
| 024 | URL source-of-truth refactor — PCFs + Code Pages read sprk_BffApiBaseUrl env var; remove hardcoded fallbacks (post-cutover cleanup; surfaced during Phase 3 baseline 2026-05-25) | 🔄 | No (multi-deploy) | FULL | 019, 023 |
| 025 | Email-send 403 follow-up — Graph "Access is denied" persists after Mail.Send grant + Exchange policy + restart. Diagnostic-first; see POML | ⬜ | No | FULL | 023 |

---

## Phase 2 — Categorize candidates (3 tasks)

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 020 | Tier SAFE candidates (3 candidates; FR-A3 already no-op) | ✅ | Yes (Group D) | STANDARD | 018 |
| 021 | Tier MEDIUM candidates (1 candidate; IdentityModel bump for SCC.Xml HIGH ×2) | ✅ | Yes (Group D) | STANDARD | 018 |
| 022 | Tier HIGH + REJECT (0 HIGH, 15 REJECT) + commit CANDIDATES.md | ✅ | No | STANDARD | 020, 021 |

---

## Phase 3 — Baseline (9 tasks)

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 030 | Test suite baseline | ✅ (BUILD FAILED documented) | Yes (Group E) | STANDARD | 022 |
| 031 | Build warning count baseline (17 warnings) | ✅ | Yes (Group E) | STANDARD | 022 |
| 032 | Endpoint smoke-test baseline (323 routes; 1 candidate 404) | ✅ | Yes (Group E) | STANDARD | 022 |
| 033 | Synthetic baseline (REDESIGNED 2026-05-25: replaces 48h calendar gate with on-demand `scripts/Capture-BffBaseline.ps1`) | ✅ | Yes (Group E) | STANDARD | 022, 032 |
| 034 | Deployed file SHA-256s via Kudu VFS (10 files) | ✅ | Yes (Group E) | STANDARD | 022 |
| 035 | Current publish + zip metrics (212.5 MB / 287 files / 72.9 MB zip) | ✅ | Yes (Group E) | STANDARD | 022 |
| 036 | Reflection-load probe baseline | ✅ | Yes (Group E) | STANDARD | 022 |
| 038 | DI registration count baseline (265 total) | ✅ | Yes (Group E) | STANDARD | 022 |
| 037 | Archive extraction-assessment + commit BASELINE.md | ✅ (Phase 4 gate — task 040 still blocked by 033 calendar gate + G4 facade agreement) | No | STANDARD | 030–036, 038 |

---

## Phase 4 — Apply changes (15 tasks)

**Outcome A track** (SAFE first, sequential by 24h bake):

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 040 | Publish with `--runtime linux-x64` framework-dependent (FR-A1) | ✅ (bake bypassed per dev-env precedent) | No (bake) | FULL | 037 |
| 041 | Exclude `wwwroot/**/*.js.map` from publish (FR-A2) | ✅ (deployed; bake bypassed per dev-env precedent) | No (bake) | FULL | 040 |
| 042 | Remove duplicate Cosmos `ServiceInterop.dll` if RID trim didn't (FR-A3) | ✅ (no-op verified — FR-A1 RID trim eliminated it as predicted Phase 1) | No (bake) | FULL | 041 |

**Outcome B track** (MEDIUM/HIGH, sequential, one per vuln):

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 043 | Vuln patch: `Microsoft.Kiota.Abstractions` NU1903 HIGH | ⏸ (deferred — REJECT per Phase 0 Decision C.1; Graph SDK 6.x upgrade follow-up project) | No (bake) | FULL | 037 |
| 044 | Vuln patch: `System.Security.Cryptography.Xml` 8.0.1 → 8.0.3 (GHSA-37gx-xxp4-5rgx + GHSA-w3x6-4m5h-cxqf HIGH ×2) | ✅ (csproj transitive override; vuln scan confirms both gone; bake bypassed per dev-env precedent) | No (bake) | FULL | 037 |
| 045 | Vuln patch (count refined post-Phase 1) | ⏸ (no third vuln remains in scope; OpenMcdf + OpenTelemetry.Api are Moderate — deferred per CANDIDATES.md REJECT R-13/R-14) | No (bake) | FULL | 044 |

**Outcome E parallel track** (independent of A; sequential within itself):

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 046 | Create `Services/Ai/PublicContracts/` facade interfaces (FR-E1) | ✅ | No | FULL | 037 |
| 047 | Migrate Finance consumers (Group F) | ✅ | Yes (Group F) | FULL | 046 |
| 048 | Migrate Workspace consumers (Group F) | ✅ | Yes (Group F) | FULL | 046 |
| 049 | Migrate Jobs consumers (Group F) | ✅ | Yes (Group F) | FULL | 046 |
| 050 | Migrate Dataverse + Filters + Endpoints consumers (Group F) | 🔲 | Yes (Group F) | FULL | 046 |
| 051 | Relocate AI-coupled job handlers to `Services/Ai/Jobs/` (FR-E3; post-G1 reality: 4 handlers + EmbeddingMigrationService = 5 files) | ✅ | No | FULL | 047, 048, 049, 050 |

**Verification**:

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 052 | Outcome E test verification (FR-E4) | 🔲 | No | FULL | 051 |
| 053 | Outcome E grep acceptance (FR-E2) | 🔲 | No | STANDARD | 052 |
| 054 | Phase 4 EXECUTION-LOG.md + gate review | 🔲 | No | STANDARD | 042, 045, 053 |

---

## Phase 5 — Promote demo + prod (4 tasks; 062/063 conditional on UQ-02)

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 060 | Deploy cumulative changeset to `spaarke-demo` | 🔲 | No | FULL | 054 |
| 061 | Demo smoke test + 48h bake | 🔲 | No (48h bake) | FULL | 060 |
| 062 | Production deploy (UQ-02 RESOLVED YES 2026-05-24 — uses Deploy-Release.ps1) | 🔲 | No | FULL | 061 |
| 063 | 7-day prod observation + LESSONS-LEARNED entries | 🔲 | No (7d bake) | FULL | 062 |

---

## Phase 6 — Prevention / codification (13 tasks; all sequential; mix of `.claude/` + `scripts/` + `.github/`; 082 = FR-C6 binding per senior-review M5)

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 070 | Deploy-BffApi.ps1 hard-fail size guard + `-AllowOversize` (FR-C5) | 🔲 | No | STANDARD | 054 |
| 071 | CI: fail on non-Linux RIDs (FR-C1) | 🔲 | No | STANDARD | 054 |
| 072 | CI: fail on `*.js.map` in publish (FR-C2) | 🔲 | No | STANDARD | 054 |
| 073 | CI: fail on HIGH-severity vuln transitive (FR-C3) | 🔲 | No | STANDARD | 054 |
| 074 | CI: PR-label escape hatches + PR template (FR-C4) | 🔲 | No | STANDARD | 071, 072, 073 |
| 075 | Workflow alignment: G-2 + G-3 (FR-D5, FR-D6) | 🔲 | No | STANDARD | 054 |
| 076 | ADR-029 (concise) at `.claude/adr/` + INDEX (FR-D1 part 1) | 🔲 | No (.claude/) | STANDARD | 054 |
| 077 | ADR-029 (full) at `docs/adr/` + INDEX (FR-D1 part 2) | 🔲 | No | STANDARD | 076 |
| 078 | `.claude/constraints/azure-deployment.md` Publish Hygiene (FR-D2) | 🔲 | No (.claude/) | STANDARD | 076 |
| 079 | `.claude/skills/bff-deploy/SKILL.md` update (FR-D3) | 🔲 | No (.claude/) | STANDARD | 076 |
| 080 | `.claude/FAILURE-MODES.md` new entry (FR-D4) | 🔲 | No (.claude/) | STANDARD | 054 |
| 081 | `src/server/api/Sprk.Bff.Api/CLAUDE.md` Publish Hygiene + AI Boundary (FR-D7) | 🔲 | No | STANDARD | 076, 079 |
| **082** | **CI: fail direct CRUD→AI dependency injection (FR-C6, M5 binding)** | 🔲 | No | STANDARD | 054, 046, 074 |

---

## Wrap-up (1 task)

| # | Task | Status | Parallel-safe | Rigor | Dependencies |
|---|---|---|---|---|---|
| 090 | Project wrap-up — README→Complete, plan→✅, LESSONS-LEARNED.md, code-review, adr-check, repo-cleanup | 🔲 | No | STANDARD | 081, 082 |

---

## Parallel Execution Groups

| Group | Phase | Tasks | Prerequisite | Constraint |
|---|---|---|---|---|
| **A** | 0 | 002, 003, 004, 005, 006, 007 | 001 complete | Independent owner asks; parallel-safe |
| **B** | 1 | 010, 011, 012, 013, 014 | 008 complete | Read-only `dotnet list` + grep commands |
| **C** | 1 | 016, 017 | 008 complete | Read-only inventory commands (different domains: binaries vs Azure) |
| **D** | 2 | 020, 021 | 018 complete | Independent tier sorting |
| **E** | 3 | 030, 031, 032, 034, 035, 036, 038 | 022 complete | Read-only baselines; **033 sequential (48h calendar gate)** |
| **F** | 4 | 047, 048, 049, 050 | 046 complete | Facade migration in separate consumer modules; no shared files |

**Phase 6 has NO parallel group** — all `.claude/` paths are main-session-only per sub-agent write boundary (root CLAUDE.md §3). Task 082 (FR-C6 CI gate) is added at end of Phase 6.

**Phase 4 outside Group F has NO parallel execution** — bake windows force sequential execution (one change per deploy). Outcome E tasks 046–051 are committed individually but bundled into a **single atomic squash-merge PR** per plan.md PR-2 (G9 owner binding).

**Task 009 (rollback drill)** is sequential within Phase 0 (cannot run with other tasks because it deploys + reverts on dev environment); it's a dependency of Phase 0 gate 008.

---

## Critical Path

```
001 → [009 + Group A: 002, 003, 004, 005, 006, 007 in parallel] → 008 (Phase 0 gate)
  → 015 + 018 (Phase 1 INVENTORY.md gate, after reflection probe + group B/C complete)
    → 020/021 → 022 (Phase 2 CANDIDATES.md)
      → 030–037+038 (Phase 3 BASELINE.md, 48h calendar gate on 033, DI baseline 038)
        → 040 → 041 → 042 (Outcome A SAFE, 24h bake each)
        ║   ║
        ║   └─ 043 → 044 → 045 (Outcome B vuln patches, sequential)
        ║
        └─ 046 → [047/048/049/050 parallel] → 051 (Outcome E — single squash-merge PR)
              → 052 → 053 → 054 (verification + Phase 4 gate)
                → 060 → 061 (Phase 5 demo + 48h bake)
                  → 062 → 063 (Phase 5 prod + 7d, conditional on UQ-02)
                    → 070–082 (Phase 6 codification, sequential; 082 = FR-C6 gate)
                      → 090 (Wrap-up)
```

**Longest dependency chain**: 001 → 009 → 008 → 015 → 018 → 022 → 033 (48h calendar) → 037 (depends on 038) → 040 → 041 → 042 (each 24h bake) → 054 → 060 → 061 (48h bake) → 062 → 063 (7d bake) → 070 → 082 → 090

**Calendar duration estimate**: 4–6 weeks dominated by 48h dev / 48h demo / 7d prod bake windows.

---

## High-Risk Items

| # | Risk | Mitigation |
|---|---|---|
| 015 | Reflection-load probe needs build instrumentation (`Program.cs` localhost-only diagnostic flag) | Single-purpose commit; revert after baseline captured |
| 033 | 48h App Insights baseline window blocks Phase 4 start | Coordinate with sibling-project owners; capture outside integration window |
| 040, 041, 042 | Each Outcome A SAFE candidate has 24h dev bake | Sequential execution + App Insights monitoring per per-candidate procedure |
| 043 | NU1903 HIGH Kiota Abstractions patch may force version chain bump (Graph SDK chain-lock) | Treat as MEDIUM-tier with owner sign-off + AI verification per NFR-08 revised; verify Kiota chain alignment before commit |
| 047–050 | Group F parallel migration touches `Sprk.Bff.Api/Services/` — risk of merge conflicts | Distinct consumer subfolders (Finance/Workspace/Jobs/Dataverse) — verify no shared file edits across agents |
| 051 | Handler relocation distinct from existing `Services/Ai/Handlers/` | Task documentation explicitly states the distinction; commit message includes clarification |
| 062 | Production deploy — high blast radius | Owner sign-off + AI verification (NFR-08 revised) + ops team authorization required; cumulative pre-tested changeset; 10-min rollback rehearsed (task 009) |

---

## Coordination Items (informational)

15+ open Dependabot PRs touch `Sprk.Bff.Api/`. Phase 1 task 011 (vulnerable + outdated scan) must reconcile against these to avoid wasted patch work. Notable overlaps:
- PR #289 (Microsoft.Agents.AI, multi-package)
- PR #266 (DocumentFormat.OpenXml)
- PR #248 (Azure.Security.KeyVault.Secrets)
- PR #244/203/202/264/263 (GitHub Actions versions — coordinate with Phase 6 task 075)
- PR #265 (coverlet.collector for Sprk.Bff.Api.Tests)

---

*Generated by `/project-pipeline` 2026-05-20. Status updates happen automatically via `task-execute` skill — do not edit manually unless rebalancing groups.*
