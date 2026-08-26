# Task 081.5 — Rollback & Escalation Record

> **Task**: `081_5-refactor-registration-dataverse-service.poml`
> **Author**: Sonnet subagent (task-execute)
> **Date**: 2026-08-18
> **Baseline commit at start**: `78a50edf3` (HEAD; includes 075/076/082-escalation-artifacts/081.5-POML landed by main session in parallel)
> **Outcome**: 🛑 **BLOCKED — code refactor validated (build+test clean) but deploy failed at Step 8. Rolled back per Step 9. Escalating per CLAUDE.md §6 rather than retrying blindly.**

---

## TL;DR

The `RegistrationDataverseService` ctor refactor (remove `[Obsolete]` `DemoProvisioningOptions.Environments`/`DefaultEnvironment` fallback) is **code-complete and fully validated** (build 0/0, 10,484/0 unit tests pass, zero HIGH CVEs, publish size unchanged at 44.96 MB). The pre-deploy Azure config gate (Step 2: set `DATAVERSE_URL` on `spaarke-bff-dev`) was executed successfully and verified safe (old code + `DATAVERSE_URL` present → `/healthz` 200).

**The deploy of the refactored code at Step 7 failed**: the App Service container crashed on startup (`exit code 134` / SIGABRT, ~12-13s after start) via the deploy script's `stop → Kudu zipdeploy → start` auto-recovery path. Per Step 8/9, I executed rollback immediately.

**Critical evidence that this is NOT a defect in the refactor**: after reverting `RegistrationDataverseService.cs` to its original (pre-refactor) content and redeploying via the identical script/path, the **original, unmodified code failed to start the exact same way** (`exit code 134`, same `stop → Kudu zipdeploy → start` path, same ~10-minute timeout). The site only recovered after a plain `az webapp restart` (not a redeploy) — which brought up the already-on-disk original code cleanly. This strongly indicates the failure is in the **deploy script's `stop → Kudu zipdeploy → start` recovery path** (an Azure Linux App Service container-startup flake), not in either version of the source code.

**Current live state**: `spaarke-bff-dev` is healthy (`/healthz` 200) running the **original (pre-refactor, [Obsolete]-fallback) code**. `DATAVERSE_URL` remains set on the App Service (correctly, per rollback plan item (d) — harmless to leave in place). The refactor is reapplied to the local working tree (uncommitted) and build-validated, ready for a retry once the deploy-path issue is understood or a plain (non-recovery-path) deploy succeeds.

---

## Timeline of events

| Time (UTC-ish, local session) | Event |
|---|---|
| T+0 | Step 1 baseline: `/healthz` = 200 (unmodified code, `DATAVERSE_URL` unset) |
| T+1 | Step 2: `az webapp config appsettings set DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com` — success. No deployment slots exist (single production slot only). |
| T+2 | Step 2 re-verify: `/healthz` = 200 after restart (proves old-code fallback-preferring-DATAVERSE_URL path still works with both sources present) |
| T+3 | Step 3: refactor applied to `RegistrationDataverseService.cs` (remove `_options` field, `IOptions<DemoProvisioningOptions>` ctor param, fallback branch, `#pragma warning disable CS0618`, stale using directives, stale XML doc comment) |
| T+4 | Step 4: DI verified — `RegistrationModule.cs:32` `AddSingleton<RegistrationDataverseService>()` needs no change (ctor params resolved by type) |
| T+5 | Step 5: grepped `tests/` for `RegistrationDataverseService` ctor tests — none exist. No test updates needed. |
| T+6 | Step 6: `dotnet build` 0 warnings / 0 errors (CS0618 warning eliminated). `dotnet test tests/unit/Sprk.Bff.Api.Tests/`: **10,484 passed, 0 failed, 97 skipped** (skips pre-existing). `dotnet test tests/Spaarke.ArchTests/`: **64/65 pass** — 1 pre-existing failure (`FR-27` on `Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance.TrapVerificationRequest.KeyVaultName`, unrelated to this task — see "Pre-existing ArchTest failure" below). |
| T+7a | Step 7 publish: `deploy/api-publish/` = **44.96 MB** (Δ 0.00 MB vs. baseline — pure shrink refactor, as expected) |
| T+7b | Step 7 deploy via `scripts/Deploy-BffApi.ps1` (canonical script per `bff-deploy` skill): direct `az webapp deploy --type zip` returned exit=1 → script's built-in auto-recovery kicked in (`stop → Kudu zipdeploy → start`). Kudu deployment polling showed `Status: Starting the site...` for ~619s, then **"Deployment failed because the site failed to start within 10 mins"** / **"worker process failed to start within the allotted time."** Hash-verify passed (4/4 critical files matched — the refactored DLLs WERE correctly placed on disk). Health check then failed 24/24 attempts (120s window). |
| T+8 | Step 8: `/healthz` confirmed **503** (not 200). Escalation trigger fires per POML: "If Step 8 /healthz != 200 after refactored deploy, execute Step 9 rollback IMMEDIATELY." |
| T+8.5 | Docker log (`api/logs/docker`) pulled via Kudu VFS: `Container exited with exit code 134 during startup after 12.8s` — repeated `ContainerStartupFailure` state transitions, ending in `Site: spaarke-bff-dev stopped.` No application-level stdout/stderr log was available (only container-orchestration events) — `LogFiles/` had no dedicated app-log stream to inspect for a .NET exception. |
| T+9a | Step 9 rollback (a): `git checkout -- RegistrationDataverseService.cs` — restored the file to its exact pre-refactor content (confirmed via empty `git diff`). |
| T+9b | Step 9 rollback (b): redeploy **original** code via the identical `scripts/Deploy-BffApi.ps1` (same direct-deploy → auto-recovery path). **Same failure pattern recurred**: direct deploy exit=1 → `stop → Kudu zipdeploy → start` → `Status: Starting the site...` for ~626s → `"site failed to start within 10 mins"`. Hash-verify passed (4/4 files matched the ORIGINAL, unmodified build). Health check failed 24/24 attempts. Docker log showed the identical symptom: `Container exited with exit code 134 during startup after 13.5s`. |
| T+9c | `/healthz` confirmed 503; `az webapp show` reported ARM-level `state: Running` / `availabilityState: Normal` despite the container being stopped at the platform layer — a state-reporting lag, not a live discrepancy. |
| T+9d | Ran **`az webapp restart`** (not a redeploy — the original code was already correctly on disk per hash-verify in T+9b). Polled `/healthz` for ~5 minutes (30 consecutive 503s, one transient `000` connection failure, then recovered). |
| T+9e | `/healthz` = **200**. Confirmed `DATAVERSE_URL` still present on the App Service (rollback plan item (d): do not remove it — harmless if unused by the currently-running original code's fallback-preferring-DATAVERSE_URL branch, and needed for the eventual retry). |
| T+9f | Reapplied the (already-validated) refactor to the local working tree (uncommitted). Rebuilt: 0 warnings / 0 errors (after clearing a transient `VBCSCompiler` file-lock artifact from the earlier concurrent build/deploy cycles via `dotnet build-server shutdown`). |

---

## Root-cause assessment

**What is confirmed NOT the cause:**
- The refactor itself — the exact same `exit code 134` / `stop → Kudu zipdeploy → start` timeout occurred when redeploying the **unmodified, pre-refactor code** via the identical script/path.
- Missing `DATAVERSE_URL` — it was set and verified (Step 2) before any deploy was attempted; both deploy attempts had it present.
- Hash/package corruption — hash-verify passed 4/4 on BOTH attempts, meaning the correct bits (refactored, then original) were genuinely written to `wwwroot`.
- The refactor's own runtime behavior — a plain `az webapp restart` against the (still-original-code) disk state succeeded immediately after the second failed deploy, proving the code that was on disk at that moment (original, unmodified) boots fine under a normal restart path.

**Leading hypothesis (not yet confirmed)**: the deploy script's `stop → Kudu zipdeploy → start` **auto-recovery path** — which triggers whenever the primary `az webapp deploy --type zip` call itself returns a non-zero exit — has a startup-timing issue on this Linux App Service distinct from the primary path or a plain `az webapp restart`. This is consistent with (but not proven by) the `bff-deploy` skill's own documented history of deploy-path flakiness (the "silent file-lock failure" G-2 incident, 2026-05-14; the Linux cold-start note recommending up to 120s tolerance — though this incident's failures ran ~620s, well past that tolerance). Both deploy attempts in this session took the SAME unusual path (direct `az webapp deploy` returning exit=1 first, forcing the fallback). Why the direct `az webapp deploy` call itself returned exit=1 on both attempts (rather than the normal ~30-60s success path documented in the skill) was not root-caused in the time available — this is the open question for the retry/investigation.

**Not investigated (time-boxed per "do not retry blindly" instruction)**: application-level stdout/stderr logs (only container-orchestration events were available via `api/logs/docker`; a dedicated app-log stream was not found under `LogFiles/`). If a retry is authorized, capturing `az webapp log tail` DURING a live deploy attempt (rather than after-the-fact Kudu VFS pull) would likely surface the actual exit-134 trigger (e.g., OOM-kill, native fault, App Service platform issue) with much higher fidelity.

---

## Pre-existing ArchTest failure (unrelated, out of scope)

`dotnet test tests/Spaarke.ArchTests/` shows 1 failure unrelated to this task:

```
FR-27: Sprk.Provisioning.ControlPlane types have no string-typed secret-shape properties
Offender: Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance.TrapVerificationRequest.KeyVaultName : string
```

Confirmed pre-existing via `git stash` + retest against baseline commit `78a50edf3` — the failure is identical with or without this task's changes. It originates in task 055's H13 E2E-acceptance placeholder handlers (`Handlers/E2EAcceptance/`), an entirely different file/concern than `RegistrationDataverseService.cs`. Per this task's own constraint ("pure internal ctor refactor... public API stays identical"), fixing an unrelated Cosmos-secret-shape violation in a sibling handler is out of scope and would be scope creep per CLAUDE.md §11. Flagging here for whichever task/owner tracks ArchTest debt (likely task 089 E2E or a Wave 4 wrap-up follow-on, per the Wave 4 Batch 4E task-055 notes: "Trap/invariant verifiers ship as PLACEHOLDERS returning InfraFault — live-probes deferred to task 089").

---

## Current state (as left for main-session/owner review)

- ✅ `spaarke-bff-dev` is healthy: `/healthz` = 200, running the **original (pre-refactor)** code.
- ✅ `DATAVERSE_URL` app-setting remains set (`https://spaarkedev1.crm.dynamics.com`) — correct per rollback plan item (d); required for the eventual retry regardless.
- ✅ No deployment slots exist on `spaarke-bff-dev` (confirmed at Step 2) — nothing to reconcile across slots.
- ✅ Local working tree: the refactor is **reapplied** (uncommitted) to `src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs` — build-validated (0/0), ready to redeploy once the deploy-path issue is understood or a retry is authorized.
- ❌ **No commit was made.** Per prescriptive step ordering, commit was scheduled for Step 12 (after Step 8 deploy verification) — since Step 8 failed, no commit exists to "revert"; the rollback in this session operated directly on the deployed artifact + working tree instead of `git revert HEAD`.
- ❌ TASK-INDEX.md row 081.5 left at 🔲 → being updated to 🔄 (needs-retry) per the parallel-execution failure-isolation convention ("mark failed tasks 🔄, not ❌ — they may succeed on retry").
- ❌ Task 082 remains blocked (`⏸`) — this task did NOT unblock it; the `[Obsolete]` fallback code is still live in production (deployed) even though it's removed from the local working tree.

---

## Recommended next steps (owner/main-session judgment — no path chosen here)

1. **Retry with live log capture**: re-run `scripts/Deploy-BffApi.ps1` (refactored code, already staged in working tree) while tailing `az webapp log tail --resource-group rg-spaarke-dev --name spaarke-bff-dev` in parallel, to catch the actual exit-134 trigger in real time. If it succeeds cleanly this time (no forced fallback path), the earlier failures were likely transient Azure platform flakiness.
2. **Investigate the deploy script's fallback path** (`stop → Kudu zipdeploy → start`) in isolation — is this a known-flaky path independent of which code is being deployed? If task 052 (H9 BFF blue-green slot-swap deploy handler, commit `67e8830ba`) or other recent deploy-path work touched `Deploy-BffApi.ps1`'s primary `az webapp deploy` invocation, that would be a place to look first for why the primary path is now returning exit=1 (forcing the flaky fallback) when it previously worked directly.
3. **If retried and successful**: complete remaining POML steps (10 CVE+publish-size — already done and clean in this session, would just need re-verification post-successful-deploy; 11 TASK-INDEX+current-task.md; 12 commit).
4. **If the deploy mechanism itself needs a fix first**: that is arguably a new, narrowly-scoped task (deploy-script hardening), not a re-scope of 081.5 — the code refactor itself is done and validated.

This escalation follows CLAUDE.md §6 (human escalation trigger: "Breaking changes" / ambiguous operational risk) — retrying the same failing deploy path a third time without new information would be "retry blindly," which the POML explicitly forbids.

---

## POSTMORTEM (added 2026-08-18 by main-session after root-cause fix landed)

> The subagent's root-cause hypothesis above ("deploy script's fallback path is flaky") **was WRONG.** Actual root cause + full recovery documented here so future retries don't chase the wrong lead.

### ACTUAL root cause: 4 missing Tier-1 IOptions on `spaarke-bff-dev` (not a deploy-path bug)

The `exit code 134` was a `.NET` `OptionsValidationException` at boot — thrown by the `Microsoft.Extensions.Options.StartupValidator` because 4 Tier-1 `IOptions` classes required app-settings that had never been set on `spaarke-bff-dev`:

1. **`PublicConfig:BffUrl`** — required by [`PublicConfigOptionsValidator`](../../../src/server/api/Sprk.Bff.Api/Configuration/PublicConfigOptionsValidator.cs) (Wave 4 task 087, commit `adbb11fa2` "/api/config runtime endpoint")
2. **`PublicConfig:MsalClientId`** — required by same validator
3. **`PublicConfig:TenantId`** — required by same validator
4. **`Onboarding:HmacSigningKey`** — required by [`OnboardingModule`](../../../src/server/api/Sprk.Bff.Api/Endpoints/Onboarding/OnboardingModule.cs) `ValidateOnStart()` (Wave 4 task 042 H0.5 consent-callback endpoint) unless `Onboarding:EnableDevBypass=true`

The stack trace was captured in `LogFiles/StartupLogs/{date}_failure.log` (SCM VFS path) — the subagent only pulled `LogFiles/{date}_docker.log` (container-orchestration events, not app stdout), which is why it missed the actual `OptionsValidationException` and defaulted to the deploy-path hypothesis.

### Why this surfaced only on this deploy

Tasks 042 (H0.5 consent-callback) and 087 (`/api/config` runtime endpoint) were coded + tested + merged to the feature branch weeks ago. Their code went in with `ValidateOnStart` guards (per NFR-05 fail-fast pattern from r3 task 061). But the corresponding App Service settings on `spaarke-bff-dev` were never updated — the feature branch had not been deployed to live dev until task 081.5's Step 7. **Task 081.5 was the first Wave 4 BFF deploy to live dev**, and it surfaced ALL the latent config gaps at once.

### Actual recovery (executed by main-session, not subagent)

The subagent's rollback restored `/healthz` = 200 via `az webapp restart` — but only because that restart happened AFTER the container had reached its crash-loop-backoff cooldown window, giving a brief window where the (still-broken) config could run again. In fact the container was cycling exit-134 continuously throughout the subagent's session.

Main-session recovery (2026-08-18, ~15:50-15:54 UTC):
1. Verified `/healthz` = 503 currently (contra subagent's earlier "200 recovered" — the site was actually crash-looping)
2. Pulled `LogFiles/StartupLogs/2026_08_18_ln0sdlwk003J0U_failure.log` via SCM VFS → found the actual `OptionsValidationException` with the 4 missing settings enumerated
3. Reused existing `AzureAd__ClientId` / `AzureAd__TenantId` values (same BFF app-reg per `PublicConfigOptions.cs` class docs — no new secret material) for `PublicConfig__MsalClientId` / `PublicConfig__TenantId`
4. Set `PublicConfig__BffUrl=https://spaarke-bff-dev.azurewebsites.net` (site's own known URL)
5. Set `Onboarding__EnableDevBypass=true` (documented dev-only escape hatch per `OnboardingOptions.cs:64-72`)
6. `az webapp restart` — `/healthz` recovered to 200 in 53s (attempt 3 of poll loop)

### Why the subagent's deploys "succeeded but failed"

`az webapp deploy` returned exit=1 because the App Service health check post-deploy was failing (correctly detecting the boot crash). The subagent's script fell through to the `stop → Kudu zipdeploy → start` recovery path, which ALSO deployed correctly but ALSO couldn't get past the same OptionsValidationException on boot. Hash-verify passed because the bits WERE correctly on disk — the code was fine, the config was the issue. The subagent misattributed this to the deploy path because it never saw the actual exception.

### Systemic gap this exposed

Root CLAUDE.md §10 BINDING pre-check protects KV secret RENAMES (never delete `Dataverse-ClientSecret` etc.) but there's **no equivalent gate for new app-setting REQUIREMENTS** introduced by new Tier-1 `IOptions` classes. When code adds `services.AddOptions<X>().ValidateOnStart()`, no CI check verifies that dev/staging/prod App Services have the corresponding `X__Foo=...` settings. Tasks 042 and 087 both correctly followed the fail-fast pattern; the drift was between "code merges to feature branch" and "config catches up on live App Service."

**Recommended follow-on** (out of scope for 081.5 itself; suggest tracking as a governance follow-up during task 090 wrap-up or in `code-quality-and-assurance-r3` backlog):
- Add a `Tier-1-IOptions deploy checklist` to `.claude/constraints/bff-extensions.md` — whenever a BFF-touching PR introduces a new `AddOptions<X>().ValidateOnStart()`, the PR description MUST enumerate the new App Service settings + confirm dev is updated (or the PR is a code-only-not-yet-deployed change, which is fine).
- Consider a CI script that greps for `.ValidateOnStart()` calls + cross-references against a config-catalog manifest similar to Phase H canonical secret-catalog (task 084 pattern).

### Diagnostic tool for future BFF startup failures

**Always pull `LogFiles/StartupLogs/{date}_failure.log`, not `LogFiles/{date}_docker.log`.** The docker log only shows container-orchestration events (Blocked, Starting, PullingImage, etc.). The StartupLogs directory has the actual `.NET` process stdout with the full stack trace of the boot exception. Fetch via:

```
az rest --method GET --resource https://management.azure.com \
  --uri "https://{app-name}.scm.azurewebsites.net/api/vfs/LogFiles/StartupLogs/{yyyy_mm_dd}_{instance-id}_failure.log" \
  --output-file /tmp/startup-failure.log
```

### Retry outcome — ✅ SUCCESS

After the 4-config fix (see previous section), main-session republished + redeployed the refactored code (2026-08-18 ~16:00-16:04 UTC):

- Publish: 44.96 MB (identical to previous — pure ctor shrink, matches expectation)
- Deploy via direct `az webapp deploy --type zip` (skipping the script's auto-fallback path since it's not needed when the primary path works): **`RuntimeSuccessful`, 1 instance successful, 0 failed** — clean single-attempt deploy
- Post-deploy `/healthz` verify: **200 on first poll** (attempt 1, ~30s after deploy completed)
- Confirms: the refactored `RegistrationDataverseService` ctor (DATAVERSE_URL-only, no fallback) boots cleanly with all Tier-1 IOptions populated. FR-33 `[Obsolete]` retirement contract for this class is now satisfied end-to-end (compile + test + deploy + boot).

**Task 082 is now unblocked** — grep of live production source (`git ls-files | xargs grep '_options.Environments\|_options.DefaultEnvironment' -l` in `src/`) confirms zero remaining consumers of the `[Obsolete]` properties. Safe to proceed with 082's Azure config deletion.

---

