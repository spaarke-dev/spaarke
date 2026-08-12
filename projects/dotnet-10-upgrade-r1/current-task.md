# Current Task State — dotnet-10-upgrade-r1

> **Last Updated**: 2026-08-12 (by task-execute — 020 H2 COMPLETE + verified; probe promoted to CI guard)
> **Recovery**: Read "Quick Recovery" first. Root CLAUDE.md §4 — execute tasks via `task-execute`, not manually.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project phase** | **P1 IN PROGRESS**. P0 (001–005 ✅) + **H1 DONE + verified (010 ✅, 011 ✅ PASS)** + **H3 (012 ✅)**. BFF build GREEN net10; graph vulnerable-clean. |
| **Active task** | **020 H2 ✅ COMPLETE + verified.** Next: **021** (H2 adversarial verify, non-author). |
| **Status** | between-tasks |
| **Next Action** | Dispatch **021** to a fresh opus subagent (NON-AUTHOR, NFR-07): independently re-run the DI-graph guard, review the 10-root fixes for NFR-01 behavior preservation (esp. stream-scope lifetimes in R1/R9), confirm no captive dep remains + no functional change. CAN overlap **013** (H6 sweep). Remaining after: 013, 014, then P3 030→033→031/032. |
| **Branch** | `work/dotnet-10-upgrade-r1` (worktree; branch already exists on origin) |
| **Git** | Wave 1 committed: `969e15471` (task 010). Wave-1b (011+012) pending commit at this checkpoint. Session P0 commits: 001 `8077e33f5` · 002 `3f6027aa5` · 003 `9b7e9b1ea` · 004 `d39de8665` · 005 `cdb31e0b0`. **NOT merged to master — deferred per owner sequencing.** |

### H1 result (010+011) — VERIFIED, do NOT re-derive
- **28 hosted-service implementers (closed set, grep) → 28 SAFE · 0 REMEDIATE · 0 code changes.** Author doc `notes/h1-backgroundservice-audit.md`; non-author PASS `notes/h1-adversarial-verification.md` (independent grep MATCH=28; all 28 CONFIRMED; 0 refuted/missed).
- Root cause all-SAFE: codebase already follows ADR-001. Genuine fail-fast lives in `StartupValidationService : IHostedService.StartAsync` (net10 changes ONLY `BackgroundService.ExecuteAsync`). Every BG hits first `await` after trivial sync prefix; graceful `return` never pre-await `throw`.
- `TodoGenerationService` 500.30 guard = constructor-avoidance (post-await resolution line 213); net10 doesn't touch ctor semantics.
- Residuals closed empirically: dashboard cache reader `DashboardEndpoints.cs:72-76` returns 204 on cold cache; SB `CreateProcessor` uses `const` queue names (un-throwable). H1 does NOT reopen.

### H3 result (012) — do NOT re-derive
- `CiamGraphClientFactory.cs:167` → `X509CertificateLoader.LoadPkcs12(pfxBytes, (string?)null, EphemeralKeySet)`. Flags/password/cert-source unchanged. SYSLIB0057 GONE (warnings 22→21). Build GREEN. Coverage-gap note `notes/h3-x509-loader.md` (private live-secret path → task 051 smoke).

### net10 retarget state so far (do NOT re-derive)
- **net10.0 now (P0 COMPLETE)**: `Spaarke.Scheduling` (002), `Spaarke.Core` + `Spaarke.Dataverse` (003), `Sprk.Bff.Api` (004), all 8 `tests/**` (005). Whole-solution `dotnet build -c Release Spaarke.sln` GREEN + BFF publish framework-dependent. `net462` plugin never moves (untouched, verified).
- **✅ S.S.C.Xml HIGH CVE fully CLOSED (task 004, owner Option 1)**: the task-003 carry-forward + a live-CVE-mask discovered when deleting NU1903 are both resolved — Core/Dataverse/Scheduling now pin `System.Security.Cryptography.Xml 10.0.11` (+ Pkcs bumped to 10.0.11 to match). `NoWarn=NU1903` DELETED. VERIFIED: zero NU1903 + `dotnet list --vulnerable` = "no vulnerable packages" across BFF+Core+Dataverse+Scheduling. **Task 032 has no S.S.C.Xml regression to chase.**
- **Package versions locked in**: Extensions.* → 10.0.1 (BFF Caching 10.0.3); MSAL → 4.87.0; Dataverse.Client → 1.2.26; Identity.Web(+MicrosoftGraph) → 4.14.2; crypto Pkcs+Xml → 10.0.11 (shared libs). 7 Kiota 1.22.0 pins KEPT; Graph 5.105.0 (Graph6/Kiota2 = task 033).
- **NU1510 pin-removal pattern (proven 003/004)**: framework-superseded pins (Asn1/STJ/RegEx everywhere; S.S.C.Xml on the BFF Web framework) removed; pins the framework does NOT supply (Pkcs, and S.S.C.Xml on non-web libs) kept/bumped to a clean version.
- **Task 005 (P0 EXIT GATE)**: retarget every `tests/**` csproj to net10; achieve clean-solution `dotnet build -c Release` + `dotnet publish`. net462 plugin untouched. This is the gate before any P1 hit-site work (010+).
- **§10 BFF governance**: publish-size re-baseline is task 031; `/conflict-check` before the eventual BFF PR (owner runs at merge).

### Critical Context (do NOT re-derive)
- Target is **.NET 10 (LTS), NOT .NET 11** (STS/not-GA) — LTS-hopping; see memory `dotnet10-not-11`.
- **Only `spaarke-dev` is live**; demo/prod decommissioned for budget (re-provision on net10 later) — memory `active-environments`.
- Retarget is a **serial atomic chain** — no P0 parallel groups. H1(010)/H2(020) are opus/xhigh with non-author adversarial verify (011/021).
- Deploy tasks are **operator-driven**: **051 (deploy net10 to `spaarke-bff-dev`) is the completion gate**; **060/061 (production cutover) are DEFERRED**.
- **CI-forced deploys DISABLED**: `deploy-bff-api.yml` (push:master) + `deploy-promote.yml` (workflow_run) → `workflow_dispatch` only, so the eventual merge won't auto-deploy. `deploy-infrastructure.yml` push:master is validate-only (kept).
- **Kiota CVE + Graph v6 fold-in (owner 2026-08-11)**: GHSA-7j59-v9qr-6fq9 is already fixed by the `Kiota 1.22.0` pins; `NoWarn=NU1903` is stale (task 004 deletes it). The "requires .NET 10" premise does NOT hold (all fix paths support net8). A break-assessment sized Graph 5→6 / Kiota 1→2 as **mechanical** → owner chose **Option B: fold Graph 6.5 + Kiota 2.0 in as NEW task 033** (P3, after 030-green; deletes the 7 direct pins; 031/032 gate on 033). Graph v6 comes OFF the deferred list (now 5 majors). Escalation valve in 033 if a call site is non-mechanical. Memos: `notes/kiota-cve-finding.md` + `notes/graph6-kiota2-break-assessment.md`.

### Sequencing (agreed with owner this session)
1. Build **P0–P4** concurrently with the 4–5 truly-active worktrees.
2. **P5** off-hours, exclusive BFF-deploy window: deploy net10 to `spaarke-bff-dev` + smoke + go/no-go (task 051 = completion gate).
3. **Merge to master** near the deploy; broadcast to the 4–5 worktrees to rebase + retarget onto net10.
4. Fleet tail: other BFF worktrees rebase onto net10 master.
5. **P6 (prod cutover) deferred** until demo/prod are re-provisioned on net10.

---

## Full State (Detailed)

### What exists (all committed + pushed)
- `plan.md` — P0–P7 WBS + discovered resources.
- `tasks/` — 24 POMLs (22 active + 060/061 deferred); `TASK-INDEX.md`. (033 = Graph 6/Kiota 2, added 2026-08-11.)
- `spec.md` / `plan.md` / `README.md` / `CLAUDE.md` — refreshed; FR-16/NFR-04/NFR-06 annotated DEFERRED.
- Lint: `scripts/Validate-TaskPoml.ps1` → 24 POMLs, **0 errors** (16 benign role="new"-on-notes warnings).

### This session's work (planning + reframe, NO src/tests code touched)
- Generated the full plan + 23 task POMLs + TASK-INDEX + current-task; refreshed stale README/CLAUDE; appended `projects/INDEX.md` row.
- Removed CI-forced BFF deploy triggers (`deploy-bff-api.yml`, `deploy-promote.yml`).
- Reframed deploy for dev-only: 050/051 → `spaarke-dev`; 060/061 → deferred; 042 runbook split (§A dev direct-deploy · §B future prod slot-swap); 090 gates on 051.
- Saved project memory: `active-environments`, `dotnet10-not-11`.

### Commits this session (on `work/dotnet-10-upgrade-r1`)
- `84a646789` — generate plan + 23 task POMLs (pipeline init-only)
- `57cca469f` — remove push:master auto-deploy from deploy-bff-api
- `758cb415b` — reframe deploy for dev-only environment reality
- `6b1926823` — remove workflow_run auto-promote from deploy-promote

### Open follow-ups (not blockers)
- No draft PR opened (init-only). Offer one when execution starts.
- Project not registered on the DevOps portfolio (no `> **Portfolio**:` pointer in README) → `/devops-project-sync` is a no-op this session.

### Next action (explicit)
Run `task-execute` against `projects/dotnet-10-upgrade-r1/tasks/001-bump-globaljson-sdk.poml`. Task 001 bumps `global.json` to a 10.0.1xx SDK and re-scrapes the .NET 10 breaking-changes page (H5) — the hard prerequisite for the whole retarget chain.
