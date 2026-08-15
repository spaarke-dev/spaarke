# Follow-On Quality Backlog (next cycle after r3)

> **Date**: 2026-08-15 · **Source**: post-program full review (4 parallel adversarial audit agents, evidence-based at net10 HEAD).
> The r3 program un-gated the aggregate **F→D** (maintainability mean **C+**); this backlog is the path toward the A+ senior-panel bar.
> Already-tracked deferrals (plugins decommission, Finance web-resource live validation, per-surface TS baseline, NG1, #772, CI-workflow wiring) are NOT repeated here except where the review found them larger than believed.

## Execution status (2026-08-15, branch `work/quality-r3-followups`)

| Item | Status |
|---|---|
| 1 Remove dead allowlist CS0109/CS1998 | ✅ DONE — CS0109 test site fixed + both removed; gate re-armed |
| 2 Ratchet GodClassGuard 4,950→2,700 + waivers | ✅ DONE — ratchet-with-waivers (7 files frozen); ArchTests 38/0 |
| 3 Fix stale allowlist comment | ✅ DONE — counts corrected |
| 4 Fix 16 masked nullable warnings (CS8604/CS8601) | ✅ DONE — all 8 prod + 3 test sites fixed, removed from allowlist; **Fable-verified all SAFE** (none masks a bug); full BFF suite 10,392/0/97 |
| 5 Complete AI-facade migration (IPlaybookLookupService) | ✅ NON-ISSUE — `IPlaybookLookupService` IS a `PublicContracts` type; ADR-013 ArchTest documents these consumers as compliant + a spread-guard already freezes the footprint. No migration needed. |
| 6 Retire CS0618 obsolete debt (14 callers) | ⏭️ SCOPED — needs the real `DemoExpirationService` multi-env refactor; kept as the sole allowlist entry, tracked (mini-project, not a quick fix) |
| 7 Console.WriteLine→ILogger (39 DI sites) | ⏭️ SCOPED — 39 sites, some run before the logger factory (per-site verify); low-med value, deferred to a tracked pass |
| 8 Broaden naming-gate scan scope | ✅ DONE — added bicep + config/environments.json to the default scan; also hardened R1 camelCase + empty-scan-fail-closed earlier |
| 9–12 Structural (SpeAdmin, ChatEndpoints, test-suite, NG1) | 📄 ANALYSIS FILES written → `notes/red-item-analyses/RED-1..4` (project seeds) |

Verified clean (not chased): `Mock<HttpMessageHandler>` = 0, CS1998 = 0.

## Review verdict (what the audit confirmed / corrected)

- **BFF remediations: 4 of 5 VERIFIED against code; build clean 0 errors / 0 warnings.** Finance auth closure, downcast→1 (`DataverseServiceClientExtensions.cs`), dead-code + tarball removal, and the `Endpoints/`→`Api/` namespace migration all hold.
- **1 claim OVERSTATED**: the ADR-013 AI-facade migration is complete for `IActionResolver`/`IActionRunner`, but `IPlaybookLookupService` is still directly injected (outside `PublicContracts`) — see item 5.
- **Forcing-functions: 5 of 7 ENFORCE (non-vacuous, prod-wired); 2 were WEAK** — the naming gate (now hardened, commit `21253e01a`) and the mechanical-baseline allowlist (items 1–4).
- **This session's C# work (061 config validation, 041 nullable fixes) is CLEAN and regression-free** — the `Document!.Save()` calls are dominated by real null-guards; the config migrations are behavior-neutral for absent sections.

## Quick wins (S effort, high value)

| # | Item | Evidence | Value |
|---|---|---|---|
| 1 | **Remove dead allowlist entries** `CS0109` + `CS1998` from `Directory.Build.props:33` | Release rebuild emits **0** of each in BFF — already fixed, still allowlisted. ⚠ verify full-solution build (props is global — other projects may emit them) before removing. | High — re-arms the gate at ~zero risk |
| 2 | **Ratchet `GodClassGuardTests` ceiling 4,950 → ~2,700** with a documented per-file waiver list | Ceiling sits 39 lines above the #1 file (`SpeAdminGraphService.cs` 4,911) so it guards only the worst offender; **8 files hide beneath it** (ChatEndpoints 4,066, ComposeService 3,573, ComposeDocxProjectionBuilder 3,085, ComposeShadowPatchEngine 2,999, CommunicationService 2,676, ComposeEndpoints 2,651, PlaybookOrchestrationService 2,528, SprkChatAgentFactory 2,380). | High — turns a #1-only guard into one that forces the next tier down |
| 3 | **Fix the `WarningsNotAsErrors` comment** in `Directory.Build.props` — it understates masked counts 4–12× (says CS8604 ×1, actual ×12; CS0109/CS1998 ×"1/5", actual 0) | Stale since 2026-06-01; misleads every future reader about the real debt. | Med — honesty of the gate's own doc |

## Medium (real NRE risk / architectural debt)

| # | Item | Evidence | Effort |
|---|---|---|---|
| 4 | **Fix the 16 masked nullable warnings** (CS8604 ×12 + CS8601 ×4) — the allowlist rides them indefinitely | Live sites: `SignalRDeliveryService.cs:214` (×2), `TodoGenerationService.cs:791/823/854`, `CommunicationService.cs:2559`, `AgentEndpoints.cs:287/310`. The props comment itself labels these "real NRE risk; needs per-site review." | M · High |
| 5 | **Complete the AI-facade migration** — move `IPlaybookLookupService` behind `PublicContracts` | Still direct-injected into `MatterPreFillService`, `ProjectPreFillService`, `WorkspaceAiService` (Services/Workspace) + `InvoiceExtractionJobHandler` (Services/Jobs). | M · Med |
| 6 | **Retire CS0618 obsolete debt** (14 callers, not the documented 7) | `DemoProvisioningOptions.Environments/DefaultEnvironment` blocked on the `DemoExpirationService` multi-env refactor; obsolete-marker promises removal. | M · Med |
| 7 | **`Console.WriteLine` → `ILogger`** in DI modules (39 sites, mostly `AnalysisServicesModule.cs`) | r3 task 033 deferred ~43; Services/** is already clean (0). Verify logger availability at each startup site before mechanical replace. | S · Low-Med |
| 8 | **Broaden the naming gate's scan scope** beyond the 3 hardcoded files (bicep, other provisioning scripts) | Env-baked names authored in `infrastructure/bicep/**` or a non-canonical script are never scanned today. (R1 camelCase + empty-scan-fail-closed already fixed in `21253e01a`.) | M · Med |

## Large (structural decomposition)

| # | Item | Evidence | Effort |
|---|---|---|---|
| 9 | **Decompose `SpeAdminGraphService.cs`** (4,911 LOC, 91 async methods, 0 regions) — the #1 god-class + the reason the ceiling can't drop | split into per-concern services (Containers / Permissions / Drives) or partial classes | L · Med |
| 10 | **Split `ChatEndpoints.cs`** (4,066 LOC, 18 routes; grew 3,587→4,066 during r3 — actively worsening) | endpoint files should be thin route groups | M · Med |
| 11 | **Test-suite reduction 10,415 → ADR-038 target ≤3,500** — large mock-scaffolding body remains | 1,922 `Mock<` in `tests/unit` vs 603 in integration; ~70/30 integration-heavy target unmet. Nominally `ci-cd-unit-test-remediation-r1` (CICD-083..085) but **not achieved** (~7,000-test gap). A `/test-diet` sweep of B7/B9/B15 classes is the action. | L · Med |
| 12 | **NG1 scoping note**: the two Dataverse god-services are larger than the NG1 framing implies | `DataverseServiceClientImpl.cs` 2,864 + `DataverseWebApiService.cs` 2,822 — unify as **decomposition**, not lift-and-shift, or it produces one ~5,600-LOC class. | L · Med |

## Trivial cleanup

- Stale comments referencing deleted types (`SafetyPipelineMiddleware` in ~8 handler comments; `OwnershipValidator` at `PlaybookEndpoints.cs:411`). Cosmetic doc-drift; types are gone from code.

## Verified-clean (do NOT spend a task chasing these)

- **`Mock<HttpMessageHandler>` (ADR-038 B1 ban): 0 real usages** — all grep hits are `// no Mock<HttpMessageHandler>` compliance comments. Genuinely absent.
- **CS1998 (async-without-await) in BFF: 0** — already remediated (see item 1).
