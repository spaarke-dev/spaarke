# .NET 8 → .NET 10 Backend Upgrade (r1) — Design Document

> **Created**: 2026-08-10
> **Status**: Design — §13-A **RESOLVED** (owner 2026-08-10: separate/sequential, .NET 10 first, then r3 re-planned on net10 baseline); ready for `/design-to-spec`
> **Author**: assessment + parallel research session 2026-08-10 (3 `researcher` sub-agents on Fable — breaking changes, package compat, App Service hosting; each grepped the live codebase)
> **Driver**: **.NET 8 loses support 2026-11-10 (~3 months from creation date).** This is a support-lifecycle upgrade, not a feature upgrade.
> **Scope**: server-side .NET only (BFF + 3 shared libs + test projects). The Dataverse plugin (`net462`) is explicitly **out of scope** — it is fixed by the Dataverse sandbox and never moves.

---

## 0. Facts locked by research (2026-08-10) — binding inputs for the spec

Everything in this section was verified against Microsoft Learn / nuget.org and cross-checked against the live tree. Citations in §12.

- **Lifecycle (the whole reason this project exists):**
  - .NET 8 (LTS) → **end of support 2026-11-10**
  - .NET 9 (STS) → end of support **2026-11-10 (same day)** → **skip 9 entirely**
  - .NET 10 (LTS) → GA 2025-11-11, **supported to 2028-11-14**
- **No hard package blockers.** Every dependency either ships a `net10.0` asset or targets `net8.0`/`netstandard2.0`, which the .NET 10 runtime consumes unchanged. The migration can ship with **only patch/minor bumps** plus one required minor bump (Dataverse.Client, see §6).
- **App Service .NET 10 is GA** on Linux (Ignite 2025). Runtime strings: **`DOTNETCORE|10.0`** for `linuxFxVersion` (pipe), **`DOTNETCORE:10.0`** for `az webapp create --runtime` / `list-runtimes` (colon). `linuxFxVersion` is a **slot-swapped setting** → the zero-downtime path exists (§7).
- **Six concrete codebase hit-sites** were found by the research greps (§5). The single highest-impact one is the **`BackgroundService.ExecuteAsync` threading change** affecting ~10+ workers.
- **The `net462` Dataverse plugin does not move.** It stays on .NET Framework 4.6.2 (sandbox constraint). This is correct, not debt.

---

## 1. Problem statement

The Spaarke server backend targets **.NET 8** across four production projects and ~seven test projects. .NET 8 reaches end of support on **2026-11-10**. After that date:

1. **No runtime security patches.** The BFF `.csproj` is dense with hand-pinned CVE remediations ([`Sprk.Bff.Api.csproj`](../../src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj) lines 139–159; [`Spaarke.Core.csproj`](../../src/server/shared/Spaarke.Core/Spaarke.Core.csproj) lines 16–24). That monthly cadence has no upstream runtime to pull from once .NET 8 is EOL.
2. **Compliance / audit exposure.** An out-of-support runtime on an internet-facing, auth-bearing, document-handling enterprise backend is an audit finding.
3. **Ecosystem drift.** The BFF already pulls `Microsoft.Extensions.AI 10.3.0`, `Microsoft.Extensions.Caching.Abstractions 10.0.3`, and other `10.x` packages *while targeting net8.0* — a split-brain that only widens as libraries move their floor to net10 LTS.

.NET 9 is not an option — it dies the same day as .NET 8. The only supported forward target is **.NET 10 (LTS)**, which is the cleanest possible move (LTS→LTS, skipping the short-lived middle release) and buys ~2 years of runway.

This is a **known, time-boxed migration**, not open-ended R&D. The risk is not "can it be done" (research confirms it can, with no blockers) — the risk is **landing a BFF-wide change amid heavy parallel-worktree traffic without breaking a production path.** The design is built around that risk.

## 2. Goals

1. **Retarget all migratable server projects** (BFF + `Spaarke.Core` + `Spaarke.Dataverse` + `Spaarke.Scheduling` + test projects) from `net8.0` to `net10.0`.
2. **Land the required package moves** in the same change: align every `Microsoft.Extensions.*` to the `10.0.x` wave, bump `Microsoft.PowerPlatform.Dataverse.Client` off its pre-net8 pin, and remove the now-superfluous CVE-pin `PackageReference`s that the net10 inbox supersedes.
3. **Remediate the six concrete hit-sites** (§5) — with the `BackgroundService` change treated as first-class, not incidental.
4. **Update the deployment surface**: `global.json`, all CI `setup-dotnet` versions, the App Service `linuxFxVersion`, and the `/bff-deploy` skill assumptions — coordinated so the code and the runtime never disagree.
5. **Re-baseline the §10 publish-size ceiling** on .NET 10 output and update the number in root `CLAUDE.md` / `azure-deployment.md`.
6. **Prove it end-to-end** in a non-prod environment before production: OBO auth, Graph, Dataverse, Service Bus jobs, SSE chat streaming, background workers.

### Non-goals

- **Not** upgrading the `net462` Dataverse plugin ([`Spaarke.Dataverse.CustomApiProxy.csproj`](../../src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/Spaarke.Dataverse.CustomApiProxy.csproj)). Sandbox-fixed at 4.6.2. Explicitly excluded.
- **Not** the six *optional* major-version library modernizations the research surfaced (Graph v6 + Kiota 2.0, Azure.Search v12, App Insights 3.x / OTel consolidation, PowerBI v5, Azure.AI.Projects 2.x GA, Agents.AI GA). None is required for net10; each is its own decision (§6.4, §8). Pulling them in would turn a mechanical retarget into a multi-front API migration and defeat the "no issues" mandate.
- **Not** a client/PCF/Code-Page change. PCFs and React surfaces talk HTTP to the BFF and are unaffected. `src/solutions/**` is untouched.
- **Not** a functional or behavioral product change. Same endpoints, same contracts, same behavior — on a supported runtime.
- **Not** the deprecated `Microsoft.Extensions.Http.Polly` → `Http.Resilience` migration (separate workstream; the 10.0.x Http.Polly keeps working).

## 3. Current-state inventory (verified)

### 3.1 Projects in scope

| Project | SDK | TFM today | Target | Notes |
|---|---|---|---|---|
| [`Sprk.Bff.Api`](../../src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj) | `Microsoft.NET.Sdk.Web` | net8.0 | **net10.0** | `linux-x64`, `SelfContained=false`, `TreatWarningsAsErrors=false`. 120+ endpoints, ~99 DI regs, 13+ job types. |
| [`Spaarke.Core`](../../src/server/shared/Spaarke.Core/Spaarke.Core.csproj) | `Microsoft.NET.Sdk` | net8.0 | **net10.0** | Pins `System.Text.Json 8.0.5` + 3 other CVE overrides → NU1510 on net10 (§5). |
| [`Spaarke.Dataverse`](../../src/server/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj) | `Microsoft.NET.Sdk` | net8.0 | **net10.0** | `Dataverse.Client 1.1.32` (pre-net8 pin, §6). Same STJ pins. |
| [`Spaarke.Scheduling`](../../src/server/shared/Spaarke.Scheduling/Spaarke.Scheduling.csproj) | `Microsoft.NET.Sdk` | net8.0 | **net10.0** | ⚠️ **`TreatWarningsAsErrors=true`** → any NU1510 / SYSLIB obsolete warning is a **build error** here. Handle first. |
| ~7 test projects (`tests/**`) | `Microsoft.NET.Sdk` | net8.0 | **net10.0** | Unit + integration + arch tests. Move with the code they cover. |

### 3.2 Explicitly out of scope

| Project | TFM | Why frozen |
|---|---|---|
| [`Spaarke.Dataverse.CustomApiProxy`](../../src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/Spaarke.Dataverse.CustomApiProxy.csproj) | net462 | Dataverse plugin sandbox runs .NET Framework 4.6.2. Cannot and must not move. `ManagePackageVersionsCentrally=false`, signed assembly. |
| `knowledge/**` sample `.csproj` (net7.0 / net8.0) | — | Reference samples, not built/shipped by CI. Ignore (or bump opportunistically — no obligation). |
| `projects/**/spike/**`, `projects/**/notes/spikes/**` (net8.0) | — | Throwaway spike harnesses. Not shipped. Ignore. |

### 3.3 Deployment / CI surface

- **Runtime host**: Azure App Service **Linux**, framework-dependent (`SelfContained=false`, `RuntimeIdentifier=linux-x64`). Runtime supplied by the platform → `linuxFxVersion` must move in lockstep with the binary.
- **`global.json`**: pins `"version": "8.0.0"`, `"rollForward": "latestFeature"` → locks to the 8.0.x SDK. **Must** bump to a `10.0.1xx` family or CI builds net10 with an 8.0 SDK and fails `NETSDK1045`.
- **CI workflows** referencing `dotnet-version: '8.x'` / `8.0.x` (verified): `sdap-ci.yml` (×5), `ci-tier1-blocking.yml` (×4), `ci-tier2-advisory.yml` (×4), `deploy-bff-api.yml` (env `DOTNET_VERSION`), `deploy-promote.yml` (env), `nightly-health.yml` (×5), `adr-audit.yml` (×1). All → `10.x`.
- **No Central Package Management** — versions are inline per-`.csproj`. Each bump touches the file directly (no `Directory.Packages.props`).
- **Deploy skill**: [`/bff-deploy`](../../.claude/skills/bff-deploy/SKILL.md) encodes 8.0 assumptions; must be reviewed with the runtime bump.

## 4. Design principles

1. **Support-lifecycle first, modernization second.** The mandate is "supported runtime, zero behavior change, no issues." Every optional library major-bump is deferred (§8) so the retarget stays mechanical and reviewable.
2. **One coherent change, not a drip.** TFM + required package alignment + hit-site fixes + CI/global.json land together on one branch. A half-migrated state (net10 code, 8.0 SDK, or mixed Extensions versions) is its own failure mode.
3. **Runtime and binary never disagree in production.** Sequencing (§7) is a first-class deliverable, not an afterthought — this is the single most likely way to cause an outage.
4. **Adversarial verification of "done."** Given the "no issues" mandate, each risk area is verified by an independent pass, not asserted by the implementer (§9, and the relationship to `code-quality-and-assurance-r3` in §11).
5. **The net10 upgrade is itself a quality forcing-function.** Dev-environment DI validation, transitive-CVE auditing, and NU1510 pin-cleanup do quality-gate work for free — lean into that rather than suppressing it (§5, §11).
6. **Defer nothing silently.** Every deferred library major (§8) is logged with a concrete reason, not hand-waved.

## 5. Concrete hit-sites (found by research greps — this is the real work)

Ordered by impact. Each becomes one or more spec tasks.

| # | Hit-site | Change (version) | File(s) | Severity | Fix |
|---|---|---|---|---|---|
| **H1** | **`BackgroundService.ExecuteAsync` now runs entirely on a background thread** — synchronous pre-`await` init no longer blocks startup; pre-`await` throws no longer take startup down synchronously. | Behavioral (.NET 10) | ~10+ workers: `ScheduledJobHost`, `UploadFinalizationWorker`, `ProfileSummaryWorker`, `IndexingWorkerHostedService`, `TodoGenerationService`, `SpeDashboardSyncService`, `BulkOperationService`, `ServiceBusJobProcessor`, … | **High** | Audit each for (a) pre-await init assumed complete before serving traffic, (b) startup-ordering/fail-fast assumptions. `TodoGenerationService` has explicit 500.30 startup-crash guard comments → ordering was load-bearing at least once. Move ordering-sensitive code to ctor / `StartAsync` / `IHostedLifecycleService`. |
| **H2** | **Dev-environment `ValidateOnBuild` + `ValidateScopes` enabled by default** — latent DI misconfig (unresolvable ctor deps, captive scoped-in-singleton) becomes a **startup crash in Development** (Production unaffected). | Behavioral (.NET 9) | Whole DI graph (~99 regs, ADR-032 conditional/null-object modules) | **Medium–High** | Boot the BFF in Development on net10 **early**; fix the batch of surfaced DI bugs as its own task. This is a feature — it finds real defects. |
| **H3** | **`X509Certificate2` constructor obsolete — SYSLIB0057** | Source (.NET 9) | [`CiamGraphClientFactory.cs:167`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/CiamGraphClientFactory.cs) — `new X509Certificate2(pfxBytes, (string?)null, EphemeralKeySet)` | Low (warning; BFF has `TreatWarningsAsErrors=false`) | Migrate to `X509CertificateLoader.LoadPkcs12(...)`. |
| **H4** | **NU1510 — direct references pruned; inbox supersedes them** | SDK (.NET 10) | `System.Text.Json 8.0.5`, `System.Formats.Asn1 8.0.1`, `System.Security.Cryptography.Pkcs 8.0.1`, `System.Text.RegularExpressions 4.3.1` in `Spaarke.Core` + `Spaarke.Dataverse`; `System.Text.RegularExpressions` + `System.Security.Cryptography.Xml 8.0.4` in BFF | Low — **but a build error in `Spaarke.Scheduling`** (`TreatWarningsAsErrors=true`) if it inherits any | Remove the superseded CVE-pin `PackageReference`s at retarget (the net10 inbox versions are already patched — CVE rationale evaporates). Keep only pins the framework does *not* supply. |
| **H5** | **`global.json` SDK pin** | SDK | [`global.json`](../../global.json) — `"version": "8.0.0"` | **Blocking if missed** | Bump to `10.0.1xx`. Align CI `setup-dotnet` in the same PR. |
| **H6** | **`HttpClientFactory`: `SocketsHttpHandler` primary + header/query redaction in logs** | Behavioral (.NET 9) | grep found **no** `ConfigurePrimaryHttpMessageHandler` in `src/server` → likely safe; re-verify after any Dataverse-HTTP-unification merge. Header values now `*` in named-client logs. | Low | Verify no primary-handler cast. Note the log redaction for anyone who debugs via those logs. |

**Secondary audits** (grep + verdict during spec authoring, low expected impact):
- `System.Linq.Async` package refs (would cause CS0121 ambiguity on net10 — `AsyncEnumerable` moved inbox). Solution-wide grep.
- `\bfield\b` identifiers inside property accessors (C# 14 `field` contextual keyword). Grep.
- `IPNetwork` / `ForwardedHeadersOptions.KnownNetworks` obsolete → `KnownIPNetworks` (relevant behind App Service front-end).
- `IExceptionHandler.TryHandleAsync` returning `true` now suppresses error-log + diagnostics metrics (.NET 10) — if dashboards/alerts key on those, they go quiet. `ExceptionHandlerOptions.SuppressDiagnosticsCallback` to opt out.
- Configuration `null` now preserved (not coerced to `""`); empty arrays bind empty not null (.NET 10) — audit `appsettings*.json` for literal nulls + code relying on "null keeps ctor default."
- `DOTNET_*` App Service app settings now override runtimeconfig (.NET 9 precedence change) — audit App Service settings.
- `MailAddress` now rejects consecutive dots (.NET 10) — relevant to email-intelligence address parsing.

## 6. Package strategy

### 6.1 Required moves (land WITH the TFM change)

| Package | From → To | Why required |
|---|---|---|
| **All `Microsoft.Extensions.*`** (Hosting.Abstractions, Logging.Abstractions, Options, DI.Abstractions, Configuration.Abstractions — currently 8.0.x) | → **10.0.x wave** (current 10.0.10) | Ends the 8.0/10.0 split-brain (M.E.AI 10.x + Caching 10.0.x already pull 10.x abstractions transitively). Manual edit; within-wave = servicing train, no API break. |
| **Microsoft.PowerPlatform.Dataverse.Client** | 1.1.32 → **1.2.26** | **1.1.x predates the `net8.0` target** (added 1.2.1/1.2.2). On net10 the 1.1.32 asset resolves an out-of-support-runtime asset. 1.1→1.2 is same `ServiceClient` API surface (low code impact). Drags **MSAL ≥ 4.84.2**. |
| **Microsoft.Identity.Client (MSAL)** | 4.79.2 → **≥ 4.84.2** (target 4.87.0) | Forced by Dataverse.Client 1.2.26 + Identity.Web bump. |
| **Microsoft.Identity.Web** (+ `.MicrosoftGraph`) | 4.3.0 → **4.14.2** | Same major; gets the net10-targeted asset. Routine. |

### 6.2 Pin removals (H4)

Remove the CVE-override `PackageReference`s the net10 inbox supersedes (`System.Text.Json 8.0.5`, `System.Formats.Asn1 8.0.1`, `System.Security.Cryptography.Pkcs 8.0.1`, `System.Text.RegularExpressions 4.3.1`, `System.Security.Cryptography.Xml 8.0.4`) — **after** confirming the inbox version is ≥ the pinned CVE-fixed version. This is a net *reduction* in the dependency surface.

### 6.3 Routine catch-ups (recommended, same PR, low risk — all same-major)

Azure SDKs (`Azure.Identity` 1.17.1→1.21.0, `Azure.Core` →1.61.0, `Azure.Storage.Blobs`, `Azure.Messaging.ServiceBus`, `Azure.Security.KeyVault.Secrets`, `Azure.Monitor.OpenTelemetry.AspNetCore` →1.6.0), `OpenTelemetry` 1.15→1.17 (+ instrumentation), `Polly` 8.6.5→8.7.0, `Microsoft.Azure.Cosmos` →3.62.1, `Caching.StackExchangeRedis` →10.0.10, `MimeKit` →4.17.0, `MsgReader` →6.1.0, `HtmlSanitizer` →9.2.995, `Handlebars.Net` →2.4.3, `OpenMcdf` →3.2.0. (Note: `Microsoft.Extensions.AI` bump to 10.8.x would drag `OpenAI` ≥ 2.12 — only do it if you also move OpenAI in lockstep; otherwise leave M.E.AI at 10.3.0.)

### 6.4 Deferred majors (explicitly OUT — §8)

Graph v6 + Kiota 2.0 (paired), Azure.Search.Documents v12, ApplicationInsights.AspNetCore 3.x (or drop for OTel — you double-instrument today), PowerBI.Api v5, Azure.AI.Projects 2.x GA, Microsoft.Agents.AI GA (rc1→1.17 churn), Http.Polly → Http.Resilience. **None blocks net10.** Each keeps working at its current major on net10.

> **One open watch-item (§10):** confirm Graph 5.x still receives security patches post-v6. If it does not, the Graph v6 + Kiota 2.0 pair graduates from "deferred" to "in scope" — because an unpatched Graph SDK on a supported runtime reintroduces exactly the CVE exposure this project exists to close.

> **⚠️ AMENDMENT (owner decision 2026-08-11) — Graph v6 + Kiota 2.0 moved IN-SCOPE (task 033).** The deferral above (and the spec §Assumptions "stays deferred" resolution) is **superseded**. Rationale is *not* the servicing watch-item (5.x is still serviced to ~2027-05-12) — it is efficiency + core-integration ownership: Graph is a core integration and we author code directly against Kiota, so a second BFF-wide regression pass later is costly. A read-only break-assessment (`notes/graph6-kiota2-break-assessment.md`) sized Graph 5→6 / Kiota 1→2 as **MECHANICAL, not deep** (the hard v4→v5 Kiota rewrite is already absorbed; direct-Kiota usage is 100% on the stable side of the 2.0 break — 0 hits on the 5 broken APIs; no batch usage; churn ~1–2 files forced, 0 deep). Given that, the batching efficiency wins while risk isolation is preserved by sequencing it as **task 033 AFTER the net10 build is green** (031/032 then measure the post-033 graph). Escalation valve: a non-mechanical call site STOPs 033 and defer-back-out is a valid outcome. The **5 remaining** §6.4 majors stay deferred.

## 7. Deployment sequencing (a first-class deliverable)

`linuxFxVersion` is on the App Service **slot-swapped settings** list — a slot swap moves the runtime string *and* the code atomically. That gives a genuine zero-downtime path (Standard tier or above):

1. Create/refresh a **staging slot** from production config.
2. Set the runtime **on the slot only**: `az webapp config set ... --slot staging --linux-fx-version "DOTNETCORE|10.0"`. Production stays on `DOTNETCORE|8.0`.
3. Deploy the `net10.0` framework-dependent build to the slot (via `/bff-deploy` adapted for net10).
4. Validate on the slot hostname — full smoke of OBO/Graph/Dataverse/Service Bus/SSE/workers. Optionally canary with `az webapp traffic-routing set`.
5. **Swap** staging→production (or `--action preview` two-phase). Warm-up pings before routing switches → no dropped requests.
6. **Rollback = swap again** (staging still holds the 8.0 app + 8.0 runtime).

Hard facts baked into the spec:
- Runtime strings: **`DOTNETCORE|10.0`** (pipe) for `linuxFxVersion` / `az webapp config set`; **`DOTNETCORE:10.0`** (colon) for `az webapp create --runtime` / `list-runtimes`. Both load-bearing.
- **Framework-version mismatch = hard startup failure** ("framework 'Microsoft.AspNetCore.App' version '10.0.x' was not found" → container killed → HTTP 503, can look like a 230s timeout). No cross-major roll-forward for FDD.
- **Do not hardcode `UseUrls()` / `ASPNETCORE_URLS`** — platform sets the port (container listens on **8080** for .NET 8+). Don't pin `RuntimeFrameworkVersion` (let the platform patch within the major).
- **Auto-swap is not supported on Linux** — swap is manual/CLI/pipeline-driven.
- CI: **`actions/setup-dotnet@v6`**, `dotnet-version: 10.0.x`, **remove any `dotnet-quality: preview`** (was preview-cycle only; post-GA it pulls pre-release patches). `global.json` overrides the input → keep them aligned.
- **Verify the prod region** at spec time: `az webapp list-runtimes --os-type linux` (paste output as evidence). CLI/ARM works even if a straggler region still shows "(Preview)" in the portal picker.

## 8. Phasing

Each phase independently reviewable; ordered so the cheap/blocking config moves and the DI/worker audits (the real risk) come before the production cutover.

| Phase | Scope | Risk |
|---|---|---|
| **P0 — Retarget + build-green** | Bump all in-scope `.csproj` to `net10.0`; bump `global.json`; align `Microsoft.Extensions.*` to 10.0.x; bump Dataverse.Client + MSAL + Identity.Web (§6.1); remove superseded CVE pins (§6.2). Get a clean `dotnet build -c Release` + `dotnet publish`. **`Spaarke.Scheduling` first** (warnings-as-errors). | Low–Med (mechanical, but NU1510/SYSLIB surface here) |
| **P1 — Hit-site remediation** | H1 (BackgroundService audit — **the big one**), H3 (X509CertificateLoader), H6 verify, + the §5 secondary greps (`System.Linq.Async`, `field`, `IPNetwork`, `IExceptionHandler`, config-null, `DOTNET_*`, `MailAddress`). | **Med–High** (H1 is behavioral + broad) |
| **P2 — Dev-boot DI validation** | Run BFF in Development on net10; fix the batch of `ValidateOnBuild`/`ValidateScopes` failures (H2). | Med |
| **P3 — Test suite green + re-baseline** | All test projects on net10 green (unit + integration + arch). Re-measure publish size; update §10 baseline in root `CLAUDE.md` + `azure-deployment.md`. Transitive-CVE audit pass (`dotnet list package --vulnerable`) now that net10 restore audits by default. | Med |
| **P4 — CI/CD + deploy plumbing** | Bump `setup-dotnet` across all workflows; adapt `/bff-deploy`; wire the slot-swap runbook. | Med (operational) |
| **P5 — Non-prod cutover + validation** | Slot-deploy to dev/staging on `DOTNETCORE|10.0`; full smoke. **This is the go/no-go gate for production.** | Med |
| **P6 — Production cutover** | Slot swap in production per §7; monitor; rollback-ready. | Med (mitigated by slot swap) |
| **P7 — Wrap-up** | `/test-diet`, doc-drift, update `projects/INDEX.md`, close. Optionally file the deferred majors (§6.4) as follow-on issues. | Low |

## 9. Verification strategy ("no issues" mandate)

The owner's explicit ask is *"thorough and complete analysis so that there are no issues."* Assertion by the implementer is not sufficient for a production runtime bump. Each risk area gets an **independent** check:

- **Build/restore**: clean `-c Release` on all projects incl. warnings-as-errors `Spaarke.Scheduling`; `dotnet list package --vulnerable --include-transitive` reviewed.
- **Behavioral (H1/H2)**: adversarial review of every `BackgroundService` and the DI graph by a reviewer who did not write the fix — the fan-out → adversarial-verify (Fable) method already chosen for `code-quality-and-assurance-r3` (§11) fits this exactly.
- **Runtime**: non-prod slot smoke of all four auth paths + SSE + a real Service Bus job + a real background-worker tick, on the actual `DOTNETCORE|10.0` stack (P5), before any production swap.
- **Rollback rehearsed**: prove the swap-back returns to 8.0 before the forward swap in production.

## 10. Constraints, ADRs, governance

- **CLAUDE.md §10 (BFF Hygiene) — TRIGGERED.** This is a BFF-touching (in fact BFF-wide) change. Obligations:
  - **Placement Justification**: no new endpoint/service/DI/package is *added* by this project — it retargets existing code and *reduces* the pin surface (§6.2). The only new "component" is the deploy runbook. State this explicitly in the spec.
  - **Publish-size re-baseline**: measure net10 `dotnet publish -c Release` compressed output; report absolute + diff vs the ~49.63 MB (incl. PDBs) baseline; keep ≤ **60 MB** ceiling. A framework-version bump *will* shift the baseline — update the number in `CLAUDE.md` §10 + [`azure-deployment.md`](../../.claude/constraints/azure-deployment.md). (Self-contained publish would blow the ceiling — do **not** use it as a region-lag escape hatch.)
  - **No new HIGH CVE**: net10 restore audits transitively by default — run and review.
  - **`/conflict-check` before every BFF PR** — 13+ active worktrees touch the BFF (§11).
- **CLAUDE.md §11 (Component Justification)**: net-negative on component count (removes pins; adds no services). Poster case for the rule.
- **ADR-013 (BFF AI facade)**: unchanged — no AI-internal types cross the CRUD boundary; this is a retarget.
- **ADR-028 (Auth)**: OBO + MI paths must be smoke-tested post-migration (H3 touches CIAM cert loading).
- **ADR-038 (Testing)**: test projects move with their code; `TimeProvider` has no net9/10 breaking change (safe). `/test-diet` at wrap-up (P7).
- **§6.5 ADR Conflict Protocol**: none anticipated (see §13).

### Hot-Path Declaration (CLAUDE.md §10 §G)

| Surface | Touch | Detail |
|---|---|---|
| **BFF** (`src/server/api/Sprk.Bff.Api/**`) | **Y** | The entire point — TFM + packages + hit-sites + publish re-baseline. |
| **SpaarkeAi** (`src/solutions/SpaarkeAi/**`) | **N** | Server-only. No React/Code-Page change. |
| **CI Workflows** (`.github/workflows/**`) | **Y** | `setup-dotnet` bumps in 7 workflow files + `deploy-bff-api` / `deploy-promote` env. |
| **Skill Directives** (`.claude/**`) | **Y** | Update `azure-deployment.md` publish-size baseline + `/bff-deploy` skill for net10 runtime string. **Main-session-only writes** (§3 boundary). |
| **Root `CLAUDE.md`** | **Y** | Update §10 publish-size baseline number + §12 build-command notes. |

## 11. Relationship to `code-quality-and-assurance-r3` — DECIDED (owner, 2026-08-10)

`code-quality-and-assurance-r3` is a **single-worktree quality program** (north star: A+ senior-panel grade; BFF = workstream #1; method = **fan-out → adversarial verify (Fable, mandatory) → remediation**). Both projects are BFF-wide, so uncoordinated parallel execution would contend on the same files.

**Decision (owner, 2026-08-10): run them as TWO SEPARATE, SEQUENTIAL projects — `dotnet-10-upgrade-r1` FIRST, merged to master; then `code-quality-and-assurance-r3` is reviewed/updated and re-planned on the net10 baseline.** The owner confirmed r3 can complete before the ~2026-10-15 margin, which removes the only reason folding-in was considered.

**Why sequential-separate wins (not folded-in):**
- **Contention is a *parallel* problem, not a *sequential* one.** Because .NET 10 merges to master *before* r3 begins BFF work, r3 branches from a net10 master → **zero contention**, exactly as if folded in — without coupling the schedules.
- **Deadline isolation.** The retarget has a hard external date (2026-11-10). Kept as a small, single-purpose project it cannot be delayed by r3 scope/gates. A tightly-scoped retarget moves faster than a workstream embedded in a multi-front program — and "faster" is what the EOL clock wants.
- **r3 refactors once, on the supported runtime.** Retarget first → every A+ refactor happens on net10. Refactoring on net8 then retargeting would re-validate/invalidate work.
- **Clean, greppable history.** One PR "upgrade to .NET 10, zero behavior change"; separate PRs "quality remediation." Worth a lot during a future bisect.
- **(C) Parallel/independent is rejected** — direct TFM + `Services/**` contention; violates the coordination intent of `projects/INDEX.md`.

**Boundary between the two projects:**
- **`dotnet-10-upgrade-r1`** fixes *what the migration requires to run correctly* on net10 (H1–H6, required package moves §6.1–6.2, deploy cutover §7). **Zero behavior change.** Ships fast → merges to master.
- **`code-quality-and-assurance-r3`** branches from net10-master and elevates quality to A+ via its fan-out/adversarial-verify method — on a supported runtime.

**Handoff obligation to r3 (executed AFTER this project merges, per owner):** r3's design/plan will be **reviewed and updated on the net10 baseline** before r3 starts BFF work. r3 must assume:
- Do **not** re-pin the CVE packages net10's inbox supersedes (the H4 / NU1510 cleanup this project performs) — else r3 fights the retarget.
- The `BackgroundService` (H1) and dev-boot DI-validation (H2) changes already surfaced + fixed a defect batch — build on them, don't re-litigate.
- The §10 publish-size baseline moved (this project re-baselines it).

This is a **post-merge handoff, not a concurrent injection** — r3 is re-planned after `dotnet-10-upgrade-r1` lands, so no r3 files are touched by this project.

## 12. Sources (research, 2026-08-10)

- Lifecycle: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- Breaking changes: [.NET 9](https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0) · [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10) (marked "work in progress" — re-scrape at spec time) · [ASP.NET Core 9](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/9/overview) · [ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview)
- Detail pages: [BackgroundService ExecuteAsync (H1)](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task) · [Configuration null preservation](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/configuration-null-values-preserved) · [STJ property-name validation](https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/10/property-name-validation)
- C#: [What's new in C# 14](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14)
- Packages: nuget.org (verified 2026-08-10) — [Dataverse.Client](https://www.nuget.org/packages/Microsoft.PowerPlatform.Dataverse.Client) · [Graph SDK v6 notes](https://github.com/microsoftgraph/msgraph-sdk-dotnet/releases) · [Http.Polly deprecation](https://www.nuget.org/packages/Microsoft.Extensions.Http.Polly)
- App Service: [Configure ASP.NET Core (Linux)](https://learn.microsoft.com/en-us/azure/app-service/configure-language-dotnetcore?pivots=platform-linux) · [Staging slots (swapped settings)](https://learn.microsoft.com/en-us/azure/app-service/deploy-staging-slots) · [Ignite 2025 GA](https://techcommunity.microsoft.com/blog/appsonazureblog/whats-new-in-azure-app-service-at-msignite-2025/4468207) · [.NET version selection / roll-forward](https://learn.microsoft.com/en-us/dotnet/core/versions/selection) · [container port 80→8080](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/8.0/aspnet-port) · [actions/setup-dotnet](https://github.com/actions/setup-dotnet)

Full research transcripts (with per-item verdicts + codebase grep evidence) are the three sub-agent reports from the 2026-08-10 assessment session; salient findings are inlined above.

## 13. Open questions (for owner / `/design-to-spec`)

- **A. Relationship to `code-quality-and-assurance-r3` — ✅ RESOLVED (owner, 2026-08-10):** two **separate, sequential** projects — `dotnet-10-upgrade-r1` ships + merges FIRST; r3 is then reviewed/updated + re-planned on the net10 baseline before it starts BFF work (§11). Not folded in; not parallel.
- **B. Graph 5.x servicing watch (§6.4).** Confirm Graph 5.101.0 still gets security patches post-v6. If not, Graph v6 + Kiota 2.0 move from deferred → in-scope (unpatched SDK reintroduces CVE exposure).
- **C. Routine catch-ups scope (§6.3).** Take all the same-major Azure/OTel/utility bumps in the retarget PR (recommended — one review, current baselines), or minimize to only the *required* moves (§6.1) for the smallest possible diff?
- **D. `Microsoft.Extensions.AI` 10.3.0 → 10.8.x?** Bumping drags `OpenAI` ≥ 2.12 in lockstep. Leave at 10.3.0 (works on net10) unless there's a reason to move — recommend leave.
- **E. App Insights (§6.4).** The net10 forcing-function is a good moment to drop classic `ApplicationInsights.AspNetCore` (2.x deprecating) and consolidate on the already-present `Azure.Monitor.OpenTelemetry.AspNetCore` — or keep both for now? Recommend: separate workstream, not this project.
- **F. Region availability evidence.** Paste `az webapp list-runtimes --os-type linux` for the prod region/subscription at spec time (§7).

---

*End of design document. §13-A resolved (separate/sequential, .NET 10 first). Advancing via `/design-to-spec`. Remaining open questions §13-B..F are scoping refinements resolvable during spec authoring.*
