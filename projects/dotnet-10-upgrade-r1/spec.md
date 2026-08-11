# .NET 8 → .NET 10 Backend Upgrade (r1) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-10
> **Source**: `projects/dotnet-10-upgrade-r1/design.md`
> **Driver**: .NET 8 (LTS) end-of-support **2026-11-10** (~3 months). Skip .NET 9 (EOL same day) → **.NET 10 (LTS, supported to 2028-11-14)**.
> **Execution model note**: this is a brownfield/root-cause-heavy migration → most tasks warrant `<effort>xhigh</effort>`; behavioral tasks (FR-07/FR-08) warrant `<model-tier>opus</model-tier>` per root CLAUDE.md §8.5.

## Executive Summary

Retarget the Spaarke server backend (BFF + 3 shared libraries + ~7 test projects) from `net8.0` to `net10.0` before .NET 8 loses support on 2026-11-10, with **zero product-behavior change** (one documented carve-out: telemetry-pipeline consolidation, FR-06). Research (2026-08-10, 3 sub-agents) confirmed **no hard package blockers**, identified **6 concrete codebase hit-sites**, and verified a **zero-downtime Azure App Service cutover path** (slot swap; the plan is P1v3 → slots available). The `net462` Dataverse plugin is out of scope (sandbox-fixed). This ships and merges to master FIRST; `code-quality-and-assurance-r3` is then re-planned on the net10 baseline (owner decision, design §11).

## Scope

### In Scope
- Retarget to `net10.0`: `Sprk.Bff.Api`, `Spaarke.Core`, `Spaarke.Dataverse`, `Spaarke.Scheduling`, and all `tests/**` projects.
- `global.json` SDK bump 8.0.0 → 10.0.1xx.
- **Required** package moves: align all `Microsoft.Extensions.*` to the 10.0.x wave; `Microsoft.PowerPlatform.Dataverse.Client` 1.1.32 → 1.2.26; MSAL ≥ 4.84.2; `Microsoft.Identity.Web` (+`.MicrosoftGraph`) → 4.14.2.
- Remove superseded CVE-pin `PackageReference`s (NU1510 — inbox supersedes).
- **Routine same-major catch-up bumps** (owner-approved, §6.3 of design): Azure SDKs, OpenTelemetry (+instrumentation), Polly, Cosmos, Caching.StackExchangeRedis, MimeKit, MsgReader, HtmlSanitizer, Handlebars.Net, OpenMcdf.
- **App Insights → OTel consolidation** (owner-approved): remove classic `Microsoft.ApplicationInsights.AspNetCore`; rely solely on the already-wired `Azure.Monitor.OpenTelemetry.AspNetCore` pipeline.
- Remediate the 6 hit-sites (H1–H6) + secondary greps (design §5).
- CI `setup-dotnet` 8.x → 10.x across all workflows; `/bff-deploy` net10 adaptation + slot-swap runbook.
- Publish-size re-baseline on net10 + governance-doc updates (root CLAUDE.md §10, `azure-deployment.md`).
- Non-prod + production cutover via zero-downtime slot swap; rehearsed rollback.

### Out of Scope
- `Spaarke.Dataverse.CustomApiProxy` (net462 — Dataverse sandbox-fixed; **never moves**).
- Client / PCF / Code-Page surfaces (`src/solutions/**`, `src/client/**`) — unaffected; talk HTTP to the BFF.
- The 6 **optional library major-version** modernizations: Graph v6 + Kiota 2.0 (paired), Azure.Search.Documents v12, PowerBI.Api v5, Azure.AI.Projects 2.x GA, Microsoft.Agents.AI GA, `Http.Polly` → `Http.Resilience`. (Each keeps working at current major on net10; deferred to follow-on.)
- `Microsoft.Extensions.AI` 10.3.0 → 10.8.x (would drag `OpenAI` ≥ 2.12 — leave at 10.3.0, works on net10).
- Any functional/product behavior change (except the FR-06 telemetry carve-out).
- `knowledge/**` samples and `projects/**/spike/**` harnesses (not shipped by CI).

### Affected Areas
- `src/server/api/Sprk.Bff.Api/**` — TFM, packages, hit-sites, telemetry consolidation, publish re-baseline.
- `src/server/shared/Spaarke.Core/**`, `Spaarke.Dataverse/**`, `Spaarke.Scheduling/**` — TFM, package pins, NU1510 cleanup.
- `tests/**` (unit, integration, arch) — TFM, green on net10.
- `global.json` — SDK pin.
- `.github/workflows/*.yml` — `sdap-ci.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`, `deploy-bff-api.yml`, `deploy-promote.yml`, `nightly-health.yml`, `adr-audit.yml`.
- `.claude/constraints/azure-deployment.md`, root `CLAUDE.md` §10/§12, `.claude/skills/bff-deploy/**` — main-session-only writes (§3 boundary).

## Requirements

### Functional Requirements

1. **FR-01** — Retarget all in-scope projects to `net10.0`. Do `Spaarke.Scheduling` **first** (`TreatWarningsAsErrors=true` → NU1510/SYSLIB = build error there). Acceptance: `dotnet build -c Release` and `dotnet publish -c Release` succeed for BFF + 3 libs; all `tests/**` build; `net462` plugin csproj **unchanged**.
2. **FR-02** — Bump `global.json` to a `10.0.1xx` SDK family; keep `rollForward` semantics. Acceptance: `dotnet --version` in CI resolves a 10.0.x SDK; no `NETSDK1045`.
3. **FR-03** — Land required package moves: every `Microsoft.Extensions.*` at 10.0.x (current 10.0.10); `Dataverse.Client` 1.2.26; MSAL ≥ 4.84.2; `Identity.Web`+`.MicrosoftGraph` 4.14.2. Acceptance: `dotnet list package` shows no `Microsoft.Extensions.*` at 8.0.x; Dataverse seam integration test green; no version-unification (NU1605) warnings.
4. **FR-04** — Remove superseded CVE-pin `PackageReference`s (`System.Text.Json 8.0.5`, `System.Formats.Asn1 8.0.1`, `System.Security.Cryptography.Pkcs 8.0.1`, `System.Text.RegularExpressions 4.3.1`, `System.Security.Cryptography.Xml 8.0.4`) **only after** confirming the net10 inbox version ≥ the pinned CVE-fixed version. Acceptance: no NU1510 warnings (incl. `Spaarke.Scheduling` as error); `dotnet list package --vulnerable --include-transitive` shows no HIGH regression vs the current net8 baseline.
5. **FR-05** — Apply routine same-major catch-up bumps (design §6.3). Acceptance: each bump is same-major (no breaking API surface); build + full test suite green.
6. **FR-06** — **Telemetry consolidation** (owner-approved carve-out to NFR-01): remove `Microsoft.ApplicationInsights.AspNetCore`; confirm all telemetry flows via `Azure.Monitor.OpenTelemetry.AspNetCore` (already wired per redis-remediation R7-S7). Acceptance: request/dependency/exception telemetry + the 12 registered Meters + Redis instrumentation still appear in App Insights after deploy (verified on the non-prod slot, FR-15); no classic-SDK references remain; documented in the PR as an intentional telemetry change.
7. **FR-07** — **`BackgroundService.ExecuteAsync` audit (H1)**: review every `BackgroundService` (ScheduledJobHost, UploadFinalizationWorker, ProfileSummaryWorker, IndexingWorkerHostedService, TodoGenerationService, SpeDashboardSyncService, BulkOperationService, ServiceBusJobProcessor, + any others found) for (a) pre-`await` init assumed complete before serving traffic, (b) startup-ordering / fail-fast assumptions. Move ordering-sensitive/fail-fast code to ctor / `StartAsync` / `IHostedLifecycleService`. Acceptance: closed list of every `BackgroundService` with a per-worker verdict (safe / remediated); `TodoGenerationService` 500.30 startup-guard behavior explicitly verified; adversarially reviewed by a non-author (NFR-07).
8. **FR-08** — **Dev-boot DI validation (H2)**: boot the BFF in the Development environment on net10; fix every `ValidateOnBuild`/`ValidateScopes` failure surfaced. Acceptance: BFF starts clean in Development on net10; closed list of surfaced DI defects + fixes; Production boot unaffected.
9. **FR-09** — **`X509Certificate2` migration (H3)**: replace the obsolete constructor at `CiamGraphClientFactory.cs:167` with `X509CertificateLoader.LoadPkcs12(...)`. Acceptance: no SYSLIB0057 warning; CIAM cert-load path unit/integration verified.
10. **FR-10** — **Secondary hit-site sweep**: grep + remediate/clear H6 (`ConfigurePrimaryHttpMessageHandler` cast; header/query log redaction noted), `System.Linq.Async` refs (CS0121), `\bfield\b` in property accessors (C# 14), `IPNetwork`/`KnownNetworks` → `KnownIPNetworks`, `IExceptionHandler`-suppressed diagnostics, configuration `null`-preservation, `DOTNET_*` App Service settings precedence, `MailAddress` consecutive-dot rejection. Acceptance: each item has a grep result + verdict (n/a or fixed); no behavior regression in affected paths.
11. **FR-11** — All test projects green on net10 (unit + integration + arch). Acceptance: full `dotnet test` green; arch tests (`Spaarke.ArchTests`) pass; no test excluded to force green without a logged rationale.
12. **FR-12** — Publish-size re-baseline + governance updates. Acceptance: measured compressed `dotnet publish -c Release` output reported (absolute + diff vs ~49.63 MB incl-PDB baseline); ≤ **60 MB**; new baseline written to root `CLAUDE.md` §10 + `.claude/constraints/azure-deployment.md`.
13. **FR-13** — CI `setup-dotnet` → `10.x`/`10.0.x` across all listed workflows (use `actions/setup-dotnet@v6`); remove any `dotnet-quality: preview`; keep `global.json` aligned. Acceptance: all CI workflows build/test net10 green; no preview-channel SDK pulled.
14. **FR-14** — `/bff-deploy` skill adapted for the net10 runtime string + a documented slot-swap runbook (staging slot → validate → swap → rollback-by-swap). Acceptance: runbook exists with exact `az` commands (`DOTNETCORE|10.0` pipe for `linux-fx-version`; `DOTNETCORE:10.0` colon for create/list); `/bff-deploy` no longer encodes 8.0.
15. **FR-15** — **Non-prod cutover + validation (go/no-go gate)**: deploy the net10 build to a dev/staging slot on `DOTNETCORE|10.0`; smoke all four auth paths (OBO, MI app-only, named API-key), SSE chat streaming, a real Service Bus job, a real background-worker tick, and FR-06 telemetry. Acceptance: all smoke checks pass on the actual net10 stack; documented evidence; explicit go/no-go recorded before FR-16.
16. **FR-16** — **Production cutover** via P1v3 slot swap (design §7): staging slot on net10, validate, swap (runtime+code atomic), monitor, rollback-ready (swap back). Acceptance: production serving on `DOTNETCORE|10.0` with no dropped requests during swap; rollback rehearsed on the slot before the forward swap.
17. **FR-17** — Wrap-up: `/test-diet`, doc-drift audit, update `projects/INDEX.md` row, and write the **r3 handoff note** (net10 baseline assumptions: don't re-pin superseded CVE packages; H1/H2 already fixed; publish baseline moved). Acceptance: INDEX updated; handoff note exists at `projects/dotnet-10-upgrade-r1/notes/`; deferred optional majors (§6.4) filed as follow-on issues via `/project-defer-issue-tracking`.

### Non-Functional Requirements

- **NFR-01** — **Zero product-behavior change**, with ONE documented carve-out: FR-06 telemetry-pipeline consolidation. Same endpoints, contracts, auth behavior, job behavior. Any other observed behavior delta is a defect.
- **NFR-02** — Publish-size ≤ **60 MB** compressed (CLAUDE.md §10); framework-dependent only (no self-contained — would blow the ceiling).
- **NFR-03** — No new HIGH-severity CVE vs the net8 baseline (`dotnet list package --vulnerable --include-transitive`; net10 restore audits transitively by default).
- **NFR-04** — Runtime and binary must never disagree in production: the App Service runtime string and the deployed TFM change together (slot swap is atomic). Framework mismatch = hard startup 503.
- **NFR-05** — The `net462` Dataverse plugin is untouched and continues to build/deploy unchanged.
- **NFR-06** — Rollback rehearsed (swap-back to 8.0 proven) before the production forward swap.
- **NFR-07** — Behavioral changes (FR-07 workers, FR-08 DI) are **adversarially verified by a non-author** (fan-out → adversarial-verify per the "no issues" mandate).
- **NFR-08** — `/conflict-check` before every BFF PR (13+ active BFF worktrees); no parallel BFF-wide project merges.

## Technical Constraints

### Applicable ADRs
- **ADR-013** — BFF AI facade boundary: unchanged (retarget only; no AI-internal types cross the CRUD boundary).
- **ADR-028** — Auth: OBO + MI paths smoke-tested post-migration; FR-09 touches CIAM cert loading.
- **ADR-032** — Null-object kill-switch DI: conditional registrations are the main surface FR-08 validates.
- **ADR-038** — Testing strategy: test projects move with their code; `TimeProvider` has no net9/10 breaking change (safe); `/test-diet` at wrap-up.

### MUST Rules
- ✅ MUST keep `SelfContained=false`, framework-dependent, `linux-x64`.
- ✅ MUST retarget `Spaarke.Scheduling` first (warnings-as-errors).
- ✅ MUST verify inbox ≥ pinned CVE version before removing any pin (FR-04).
- ✅ MUST re-baseline publish size and update the governance number (FR-12).
- ✅ MUST use the slot-swap path for production cutover (P1v3 supports it).
- ❌ MUST NOT touch the `net462` plugin.
- ❌ MUST NOT pull in the 6 optional library majors (defeats the "no issues" mandate).
- ❌ MUST NOT hardcode `ASPNETCORE_URLS`/`UseUrls()` or pin `RuntimeFrameworkVersion`.
- ❌ MUST NOT use self-contained publish as a region-lag escape hatch.

### Existing Patterns
- Deploy: `.claude/skills/bff-deploy/SKILL.md` (adapt for net10).
- Publish-size rule: `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule (NFR-01)".
- Telemetry wiring precedent: `Sprk.Bff.Api.csproj` lines 119–131 (OTel→Azure Monitor already wired).

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>Y</skill-directives>
  <root-claude-md>Y</root-claude-md>
</hot-path-declaration>
```
**Placement Justification**: This project adds **no** new endpoint/service/DI/package to the BFF — it retargets existing code and **reduces** the package surface (FR-04 pin removals + FR-06 classic-SDK removal). The publish-size ceiling (≤60 MB) applies per task (FR-12). `/conflict-check` before every BFF PR.

### New Components (§11 three-question gate)
**No new components — modify-only.** This project changes target frameworks, package versions, and remediates behavioral hit-sites in existing code. The only new *artifacts* are the slot-swap deploy runbook (documentation) and the r3 handoff note (documentation) — neither is a code component. Net component/package count **decreases**.

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| — (NFR-01 principle) | "Zero behavior change" | FR-06 telemetry consolidation is a deliberate telemetry-pipeline change | Documented exception (owner-approved 2026-08-10) | Removes deprecated double-instrumentation; the OTel→Azure Monitor pipeline is already wired; validated on the non-prod slot (FR-15) before prod. Not an ADR MUST/MUST-NOT — a project-principle carve-out, logged here + in the PR. |

> No ADR MUST/MUST-NOT tensions surfaced at design time. All listed ADRs apply without exception. Re-scrape the "work-in-progress" .NET 10 breaking-changes page (design §12) during execution in case new servicing entries land.

## Success Criteria
1. [ ] All in-scope projects target `net10.0`; net462 plugin unchanged — Verify: `dotnet build -c Release` green + csproj diff review.
2. [ ] No NU1510 (incl. `Spaarke.Scheduling` as error); no HIGH-CVE regression — Verify: build output + `dotnet list package --vulnerable`.
3. [ ] Every `BackgroundService` has a per-worker verdict, adversarially reviewed — Verify: FR-07 closed-list doc + non-author sign-off.
4. [ ] BFF boots clean in Development on net10 (DI validation) — Verify: FR-08 boot log + defect list.
5. [ ] Telemetry intact after consolidation — Verify: App Insights shows requests/deps/exceptions/Meters/Redis on the non-prod slot (FR-15).
6. [ ] Full test suite green on net10 — Verify: `dotnet test` + arch tests.
7. [ ] Publish ≤60 MB; new baseline documented — Verify: FR-12 measurement + governance doc diff.
8. [ ] CI green on net10 across all workflows — Verify: workflow runs.
9. [ ] Non-prod slot smoke passes (all auth paths, SSE, job, worker, telemetry) — Verify: FR-15 evidence + recorded go/no-go.
10. [ ] Production on `DOTNETCORE|10.0` with no dropped requests; rollback rehearsed — Verify: FR-16 swap logs + monitoring.
11. [ ] r3 handoff note written; deferred majors filed — Verify: FR-17 artifacts.

## Dependencies

### Prerequisites
- App Service plan **P1v3** (confirmed) → deployment slots available for the zero-downtime path.
- A dev/staging slot (or ability to create one) on the target App Service.
- Confirm .NET 10 runtime available in the prod region: `az webapp list-runtimes --os-type linux` (paste output at execution time).

### External Dependencies
- Azure App Service .NET 10 stack GA (confirmed available).
- No external approvals beyond standard deploy authorization.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Relationship to r3 | Fold into r3 or separate/sequential? | **Separate, .NET 10 first**, then r3 re-planned on net10 baseline | Standalone project; FR-17 writes the r3 handoff note; no r3 files touched here |
| r3 timing | Can r3 finish before ~2026-10-15 EOL margin? | Yes | Confirms separate-sequential is safe; retarget ships first |
| Package scope | Minimal diff or include routine catch-ups? | **Required + routine same-major catch-ups** | FR-05 in scope (§6.3 bumps) |
| App Insights | Consolidate onto OTel here or defer? | **Consolidate in this project** | FR-06 added; NFR-01 carve-out documented |
| Cutover path | Slot swap / maintenance window / decide later? | **P1v3** (Premium → slots available) | FR-15/FR-16 target the zero-downtime slot-swap path |

## Assumptions
- **M.E.AI**: staying at 10.3.0 (works on net10; bumping would drag OpenAI ≥2.12) — will revisit only if a dependency forces it.
- **Graph 5.x**: ✅ **RESOLVED (research 2026-08-10)** — staying on Graph 5.x is safe. Microsoft services the prior major for **security fixes for 12 months from v6 GA** → 5.x covered to **~2027-05-12**; the one relevant CVE (CVE-2026-44503, GHSA-7j59-v9qr-6fq9, RedirectHandler header leak) is **already patched at Kiota 1.22.0** (our pin — MUST keep). Graph v6 + Kiota 2.0 stays **deferred** to a separate project (hard deadline ~2027-04). Optional cheap hygiene: bump `Microsoft.Graph` 5.101.0 → **5.105.0** (final 5.x; its Graph.Core raises the Kiota floor to the patched 1.22.0 natively) as part of FR-05.
- **Region**: prod region has .NET 10 GA (portal may lag; CLI/ARM works regardless) — verified at execution time.
- **Telemetry**: the OTel→Azure Monitor pipeline (redis-remediation R7-S7) is complete and is the sole telemetry path after FR-06.

## Unresolved Questions
- [x] ~~**Graph 5.x servicing**~~ — ✅ RESOLVED 2026-08-10 (see Assumptions): 5.x security-serviced to ~2027-05-12; CVE already patched at Kiota 1.22.0. Graph v6 stays out of scope; optional 5.105.0 hygiene bump folded into FR-05.
- [ ] **Region runtime evidence** — Paste `az webapp list-runtimes --os-type linux` for the prod subscription/region as FR-15 evidence (execution-time verification).
- [ ] **Slot availability** — Confirm a staging slot exists or can be created on the target App Service without disrupting current config (execution-time verification).

*Both remaining items are execution-time verifications (early tasks), not planning blockers.*

---
*AI-optimized specification. Original design: `design.md`. §13-A resolved (separate/sequential, .NET 10 first). Advance via `/project-pipeline projects/dotnet-10-upgrade-r1` once the Unresolved Questions are directionally settled.*
