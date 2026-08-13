# Current Task State — dotnet-10-upgrade-r1

> **Last Updated**: 2026-08-12 (by task-execute — 030 IN PROGRESS: net10-caused test failures FIXED (522→0 on BFF); 2 PRE-EXISTING clusters awaiting owner decision)
> **Recovery**: Read "Quick Recovery" first. Root CLAUDE.md §4 — execute tasks via `task-execute`, not manually.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project phase** | **P0 ✅ · P1 ✅ (010–014 all ✅) · P2 ✅ (020/021)**. → **P3 open** (030→033→031/032). BFF build GREEN net10 (0 err, 21 warn = post-012 baseline); graph vulnerable-clean; DI graph adversarially verified; classic App Insights SDK removed (OTel sole path). |
| **Active task** | **030 IN PROGRESS** — net10-caused test failures fixed + verified; **BLOCKED on owner decision** for 2 pre-existing (non-net10) clusters. |
| **Status** | in-progress (fixes committed; awaiting owner call on ArchTests-4 + Plugins-orphan before marking 030 ✅) |
| **Next Action** | **AWAIT owner decision** (see `notes/test-green.md` §Escalation): (1) ArchTests ADR-007/010 ×4 pre-existing arch drift → recommend **document + defer to master-sync** (branch is 44 behind master); (2) `Spaarke.Plugins.Tests` dead orphan (references deleted project) → recommend **flag for /test-diet deletion at task 090**. Also remaining: run the FR-03 **Dataverse seam** integration test (`tests/integration/seam/**`) + confirm ADR-002 arch tests pass (they do). On decision → finalize 030, then **033** (Graph6/Kiota2) → **031/032**. |
| **Branch** | `work/dotnet-10-upgrade-r1` (worktree; exists on origin) |
| **Git** | ✅ **CLEAN — tip `d9eb30305`, 0 unpushed, 44 behind origin/master.** Session commits: P0 001 `8077e33f5`·002 `3f6027aa5`·003 `9b7e9b1ea`·004 `d39de8665`·005 `cdb31e0b0` · 010 `969e15471` · 011+012 `5191efb6a` · **020 `d9eb30305`**. **NOT merged to master — deferred per owner sequencing.** Safe to `/compact`. |

### H2 result (020) — VERIFIED, do NOT re-derive
- **45 captive-dependency (singleton→scoped) errors → 10 root singletons → ALL FIXED → probe CLEAN.** Method: in-process probe reused `CustomWebAppFactory` neutralization (mocked IDataverseService + fake IGraphClientFactory + removed hosted services) with ValidateOnBuild+ValidateScopes RE-ENABLED; iterated 45→19→2→0.
- Fixes (behavior-preserving, NFR-01): **Family A scope-per-unit-of-work** (IServiceScopeFactory, scope spans stream consumption) for R1/R2/R3/R4/R6/R7/R8/R9/R10; **Family B demote singleton→scoped** for R5 ActionResolver (+ NullActionResolver peer, ADR-032 symmetry) — safe because the CLEAN probe proves no singleton consumer. 9 BFF source + 24 test files + 2 scope-factory stubs.
- **FR-08 honored** (validations NOT disabled; no IsDevelopment branch; Production path unaffected). Probe **promoted** to permanent CI guard `tests/unit/Sprk.Bff.Api.Tests/DiGraphValidationTests.cs` (asserting, network-free) — flagged for 021/030 shape review.
- Main-session independent verification: probe CLEAN + BFF Release build 0 errors + targeted Nodes/Communication tests 44 pass/0 fail + full 9-file diff review (stream scopes span consumption). Step 9.5 code-review + adr-check PASS. Full detail: `notes/h2-di-validation.md`.

### H2 ADVERSARIAL VERIFY (021) — PASS, do NOT re-derive
- **Non-author opus subagent (isolated worktree) → PASS.** Independently re-ran `dotnet test ... --filter DiGraphValidationTests` on net10 = `Passed: 1, Failed: 0` (clean boot confirmed without trusting author). **All 10 roots R1–R10 CONFIRMED behavior-preserving; 0 REFUTED → task 020 stands.**
- Stream-scope lifetimes (R1/R9) CONFIRMED: every SPE stream materialized to memory before scope disposal (no use-after-dispose); rests on `SpeFileStore` stateless ADR-007 facade. R5 demote + NullActionResolver symmetry CONFIRMED (stateless resolver, no singleton consumer, guard-pass dispositive, null-object throw unaffected by lifetime). ValidateOnBuild/ValidateScopes NOT disabled in production. Guard test truly asserts (KEEP). No §6.5. Full report: `notes/h2-verification.md`.

### 030 test-suite state (IN PROGRESS) — do NOT re-derive
- **net10 retarget introduced exactly 2 test-infra regressions, BOTH FIXED** (cleared 521+): (1) `Microsoft.AspNetCore.Mvc.Testing` 8.0.23→**10.0.1** in all 3 test projects — net10 STJ needs `PipeWriter.UnflushedBytes`, old TestHost lacks it → 1928 exceptions → Sprk.Bff.Api.Tests **522 fail → 0**; (2) ArchTests `System.Net.Http` NU1510-as-error pin removed (task-005 miss). Both = task-005 gaps (ArchTests + Plugins.Tests are NOT in Spaarke.sln, so 005's solution build missed them).
- **Also fixed (retarget-caused)**: `AttachmentActionEvalTests` source-string `_createTaskAi`→`createTaskAi` (H2 R3 field→scoped-local rename; behavior-preserving; master still has field form). Verified 7/7.
- **PRE-EXISTING (not net10) — FIXED**: `DesktopUrlBuilderTests` ×10 — stale test (production intentionally abbreviated `ms-{app}:{url}` per FileViewer bb63d9818; test lagged; proven red on master). Verified Core 45/45.
- **PRE-EXISTING (not net10) — AWAITING OWNER DECISION** (`notes/test-green.md` §Escalation): ArchTests ADR-007/010 ×4 (whole-codebase arch drift, incl. 76→153 interface ceiling; ADR-002 tests PASS) + `Spaarke.Plugins.Tests` dead orphan (references deleted `src/server/plugins/Spaarke.Plugins.csproj`; NU1015; not in sln). Branch is **44 behind master** → some may resolve on master-sync.
- **Green now**: Core 45/45 · Scheduling 47/57(10skip) · RecordSyncJob 12/12 · **Sprk.Bff.Api.Tests 10348 pass/0 fail/101 skip**. Full triage: `notes/test-green.md`.

### FR-06 result (014) — do NOT re-derive
- **Classic App Insights SDK removed; OTel→Azure Monitor is sole telemetry path.** 2 files: BFF csproj (dropped `Microsoft.ApplicationInsights.AspNetCore 2.23.0`) + `Api/Agent/AgentTelemetry.cs` (dropped classic usings + `TelemetryClient?` field/ctor-param + all dead `_telemetryClient?.` calls; kept all `_logger.Log*` + all public method signatures). BFF net10 GREEN (0 err, 21 warn = unchanged).
- **Gap-free proof (why escalation did NOT fire)**: `AddApplicationInsightsTelemetry()` never called (only `UseAzureMonitor`, since R7-S7) + `TelemetryClient` registered NOWHERE → optional ctor param bound `null` (why task-020 DI guard passed clean) → classic emissions were ALREADY inert no-ops. Removing them drops NO live signal.
- OTel pipeline `TelemetryModule.cs` UNTOUCHED: 10 custom `Sprk.Bff.Api.*` AddMeter + AddRedisInstrumentation + 3 AddSource intact ("12 Meters" in comment/task = historical nominal; code has 10 custom — material point holds). Package removal = strict publish/CVE-surface reduction (formal re-baseline = task 031). NFR-01 carve-out (the ONLY one). Step 9.5 PASS. Carve-out doc (cite in PR ADR Tensions): `notes/fr06-telemetry-carveout.md`.

### H6 result (013) — do NOT re-derive
- **Closed 10-item secondary sweep → ALL n/a → ZERO code changes.** (8 design-§5/FR-10 items + 2 re-scrape follow-up items.) BFF net10 Release build GREEN (0 err, 21 warn = unchanged post-012 baseline; **0× CS9258/CS9259** = definitive `field`-keyword clearance).
- n/a verdicts: H6 handler-cast (0 matches) · System.Linq.Async (0 csproj) · `field` keyword (comments/locals only, no accessor identifier) · IPNetwork/KnownNetworks (0) · IExceptionHandler (lambda handler w/ explicit LogError, no interface impl) · config-null (2 nulls in non-runtime-loaded `appsettings.template.json`; all consumers `IsNullOrWhiteSpace`/`??`) · DOTNET_* (0 App Service settings in infra) · MailAddress (2 outbound validators only, no inbound parse — escalation did NOT fire) · C#14 overload (build clean) · XmlSerializer (0 matches; re-scrape ComposeService note stale).
- Downstream (NOT 013 fixes): task 050 live-env `DOTNET_*` confirm · task 040 CI `DOTNET_VERSION:'8.x'` bump · task 031/032 2× residual NU1510 on BFF (H4/P0 hygiene) · task 051 ActivitySource/W3C propagator smoke. Full checklist: `notes/secondary-sweep.md`.

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
