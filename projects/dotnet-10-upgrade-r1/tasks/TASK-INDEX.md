# TASK-INDEX — .NET 8 → .NET 10 Backend Upgrade (r1)

> **Generated**: 2026-08-11 by `/project-pipeline`
> **Branch**: `work/dotnet-10-upgrade-r1` · **Spec**: [`../spec.md`](../spec.md) · **Plan**: [`../plan.md`](../plan.md)
> **Status legend**: 🔲 not-started · 🔄 in-progress/needs-retry · ✅ complete · ⏸ deferred/blocked
> **Execution**: per-task via `task-execute` (root CLAUDE.md §4). Deploy tasks (050/051) are **OPERATOR-DRIVEN** (Azure + go/no-go).
> **⏸ Environment reality (owner 2026-08-11)**: only `spaarke-dev` is live; demo/prod decommissioned for budget (to be re-provisioned on net10 later). **051 (dev deploy) is the completion gate; 060/061 (production cutover) are DEFERRED.** See project memory `active-environments`.

---

## Task registry

| # | Task | Phase | FR | MT / E | Deps | Parallel-safe | Status |
|---|------|-------|----|--------|------|---------------|--------|
| 001 | Bump global.json → 10.0.1xx + re-scrape breaking changes (H5) | P0 | FR-02 | sonnet/high | — | false | ✅ |
| 002 | Retarget **Spaarke.Scheduling FIRST** (warnings-as-errors) + NU1510/SYSLIB | P0 | FR-01,04 | sonnet/xhigh | 001 | false | ✅ |
| 003 | Retarget Core + Dataverse; required package moves + pin removals | P0 | FR-01,03,04 | sonnet/xhigh | 002 | false | ✅ |
| 004 | Retarget Sprk.Bff.Api; package alignment + §6.3 catch-ups | P0 | FR-01,03,05 | sonnet/xhigh | 003 | false | ✅ |
| 005 | Retarget tests/**; clean solution build + publish (**P0 exit gate**) | P0 | FR-01 | sonnet/high | 004 | false | ✅ |
| 010 | **H1** BackgroundService.ExecuteAsync audit (closed per-worker list) | P1 | FR-07 | **opus**/xhigh | 005 | false | ✅ |
| 011 | **H1 adversarial verification** (non-author) | P1 | NFR-07 | opus/xhigh | 010 | false (V1) | ✅ |
| 012 | **H3** X509Certificate2 → X509CertificateLoader.LoadPkcs12 | P1 | FR-09 | sonnet/high | 005 | false | ✅ |
| 013 | **H6 + secondary sweep** (grep + per-item verdict) | P1 | FR-10 | sonnet/xhigh | 005 | false | ✅ |
| 014 | **FR-06** telemetry consolidation (drop classic App Insights SDK) | P1 | FR-06 | sonnet/high | 004 | false | ✅ |
| 020 | **H2** dev-boot DI validation (fix ValidateOnBuild/ValidateScopes) | P2 | FR-08 | **opus**/xhigh | 010 | false | ✅ |
| 021 | **H2 adversarial verification** (non-author) | P2 | NFR-07 | opus/xhigh | 020 | false (V2) | ✅ |
| 030 | Full test suite green on net10 (unit + integration + arch) | P3 | FR-11 | sonnet/xhigh | 021 | false | ✅ |
| 033 | **Graph 5.101→6.5 + Kiota 1→2** (transitive); retire 7 Kiota pins + NoWarn (owner fold-in 2026-08-11) | P3 | FR-03,NFR-03 | sonnet/xhigh | 030 | false | 🔲 |
| 031 | Publish-size re-baseline + governance updates 🔒 | P3 | FR-12 | sonnet/high | 033 | false | 🔲 |
| 032 | Transitive CVE audit (no HIGH regression) | P3 | NFR-03 | sonnet/high | 033 | false | 🔲 |
| 040 | CI setup-dotnet → 10.x / @v6 across 7 workflows | P4 | FR-13 | sonnet/xhigh | 032 | false | 🔲 |
| 041 | App Service Bicep DOTNETCORE\|10.0 (+ platform.json) + Functions | P4 | FR-14 | sonnet/xhigh | 032 | false | 🔲 |
| 042 | Adapt /bff-deploy + slot-swap runbook 🔒 | P4 | FR-14 | sonnet/high | 041 | false | 🔲 |
| 050 | Confirm `spaarke-dev` runs net10 (runtime + slot evidence) 🛠️ | P5 | FR-15 | sonnet/high | 042 | false | 🔲 |
| 051 | **Deploy net10 to `spaarke-bff-dev`** + full smoke + **go/no-go** (completion gate) 🛠️ | P5 | FR-15 | sonnet/high | 050 | false | 🔲 |
| 060 | ⏸ *(deferred)* Rehearse rollback (swap-back) 🛠️ | P6 | NFR-06 | sonnet/high | future demo/prod | false | ⏸ |
| 061 | ⏸ *(deferred)* Production slot swap to net10 🛠️ | P6 | FR-16 | sonnet/high | 060 | false | ⏸ |
| 090 | Wrap-up: test-diet, doc-drift, INDEX, r3 handoff, defer majors 🔒 | P7 | FR-17 | sonnet/high | 051 | false | 🔲 |

🔒 = writes root `CLAUDE.md` / `.claude/` / `projects/INDEX.md` → **main-session-only** (root §3).
🛠️ = **OPERATOR-DRIVEN** — needs Azure credentials + a recorded human go/no-go; not run autonomously.
⏸ = **DEFERRED** — fires only when demo/prod are re-provisioned on net10 (no production environment today).

**Count**: 24 tasks across P0–P7 (P0=5, P1=5, P2=2, P3=4, P4=3, P5=2, P6=2, P7=1) — **22 active + 2 deferred** (060/061).

> **Note on 033 ordering**: task 033 (Graph 6/Kiota 2) is numbered after 032 but **runs before** 031/032 in execution order — it gates on 030 (net10 green) and 031/032 measure the post-033 package graph. Deps columns + the critical path above are authoritative for sequencing, not the numeric label.

---

## Critical path

```
001 → 002 → 003 → 004 → 005 → 010 → 012 → 013 → 014 → 020 → 030 → 033 → 031 → 032
    → 040 → 041 → 042 → 050 → 051 → 090          (060/061 DEFERRED — off the active path)
```

The adversarial-verify tasks (011 after 010; 021 after 020) hang off the path but are not on the longest chain — each can overlap the next author task. **060/061 (production cutover) are deferred** — the active path ends at 051 (dev deploy) → 090 (wrap-up).

## Parallel execution groups

**By design, this project has NO P0 parallel groups.** Per design §4 principle 2 ("one coherent change, not a drip"), the retarget is intentionally an atomic serial chain — a half-migrated state (net10 code + 8.0 SDK, or mixed Extensions versions) is its own failure mode. The whole in-scope tree lands together on one branch.

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| V1 | 011 | 010 complete | Adversarial verify of H1 — **different agent than 010's author** (NFR-07); read-only, may overlap 012/013 |
| V2 | 021 | 020 complete | Adversarial verify of H2 — **different agent than 020's author** (NFR-07); read-only |

There are no concurrent code-writing waves — every code task is `parallel-safe: false`.

## Dependency notes / gates

- **001 is the hard prerequisite** (H5): until global.json moves to 10.0.1xx nothing builds net10 (NETSDK1045).
- **002 before 003/004**: `Spaarke.Scheduling` has `TreatWarningsAsErrors=true` — do it first so NU1510/SYSLIB surface early on the smallest project.
- **005 is the P0 exit gate**: whole-solution build + publish green before any P1 hit-site work.
- **021 gates P3**: H2 fixes must be adversarially verified before the test suite is declared green.
- **051 "go" is the project completion gate**: on a recorded dev go/no-go "go", the active work is done and 090 (wrap-up) proceeds. There is no production cutover in scope today.
- **060/061 DEFERRED**: production cutover (rollback rehearsal + slot swap) fires only when demo/prod are re-provisioned on net10. Procedure preserved in the task-042 runbook §B; 090 files it as a tracked follow-on.

## Governance reminders (per task)

- `/conflict-check` **before EVERY BFF PR** — 13+ active BFF worktrees (NFR-08); no parallel BFF-wide project merges.
- Publish-size ≤60 MB, framework-dependent only (re-baselined in 031).
- `net462` plugin **untouched** (NFR-05); do NOT pull the **5 remaining** deferred optional majors (Graph v6/Kiota 2.0 is now IN scope — task 033).
- MUST NOT weaken `TreatWarningsAsErrors` / disable DI validation / exclude tests to force green.

## Success-criteria → task map

| SC | Criterion | Task(s) |
|----|-----------|---------|
| 1 | in-scope net10; plugin unchanged | 001–005 |
| 2 | no NU1510; no HIGH-CVE regression (Kiota CVE closed) | 002, 033, 032 |
| 3 | per-worker verdict, adversarially reviewed | 010, 011 |
| 4 | Dev DI validation clean | 020, 021 |
| 5 | telemetry intact | 014, 051 |
| 6 | full test suite green | 030 |
| 7 | publish ≤60 MB; baseline documented | 031 |
| 8 | CI green on net10 | 040 |
| 9 | dev smoke on `spaarke-bff-dev` + go/no-go (completion gate) | 050, 051 |
| 10 | ⏸ prod on DOTNETCORE\|10.0; rollback rehearsed — **DEFERRED** (no prod env) | 060, 061 (deferred) |
| 11 | r3 handoff note; deferred majors filed | 090 |
