# SESSION 18 handoff — customer-provisioning-orchestration-r1

> **Date**: 2026-08-27 (SESSION 18)
> **Branch**: `work/customer-provisioning-orchestration-r1`
> **Last commit**: `f5438373a` (Bucket B credential-lifecycle cluster)
> **Purpose**: Fresh-session pickup for the remaining 5 Bucket B HIGH findings before task 186 dispatches.

---

## What landed this session

Three commits, all pushed to `origin/work/customer-provisioning-orchestration-r1`:

| Commit | Scope | Files | LOC |
|---|---|---|---|
| `6baf1fbfd` | ISH-12: intake.schema.json `environment` → `controlPlaneEnv` rename | 5 | +15/-15 |
| `97e18c227` | Bucket A (5 HIGH/MED pre-dispatch blockers) | 4 | +92/-10 |
| `f5438373a` | Bucket B credential-lifecycle cluster (5 HIGH) | 11 | +260/-35 |

**Bucket A** closed the trial1-dispatch blockers:
- HIGH#1: `runs/trial1-intake.json` — added `confirmationAcknowledgment` + `estimatedMonthlyUsd=412` + `costEnvelopePolicy=abortOnOverrun`
- HIGH#2: schema top-level `required[]` — added `confirmationAcknowledgment`
- HIGH#8: SKILL.md batch loader + Step 4.0 nonSecretParameters — wired `estimatedMonthlyUsd` + `costEnvelopePolicy` end-to-end
- HIGH#13: SKILL.md Step 6a — fixed phantom `$constants.spaarke[$env]` shape → real `$constants.name_templates.registryDvUrl.$env`
- MED#12: SKILL.md sample intake block updated
- New forcing function: `ConfirmationAcknowledgment_IsRequired_InIntakeSchema` parity test

**Bucket B credential-lifecycle** closed 5 auth-v4 protection HIGHs:
- HIGH#3: `EntraAppRegRequest.RequireSecretFreeIdentity=true` default; provisioner gates mint + KV write; explicit-true at H3 call site; test locks it
- HIGH#4: `Register-EntraAppRegistrations.ps1` — `-AllowClientSecretMint` opt-in + `-MintReason` required; silent absence forces skip
- HIGH#5 + MED#14: `manifest.yaml` — `app_settings: []` on BFF-API-ClientSecret + Dataverse-ClientSecret; new `Test-E3ClosedNoAppSettingsInvariant` generator guard
- HIGH#11: `Seed-CustomerKeyVault.generated.ps1` — hard-refusal guard for BindingNeverDelete secrets missing from target vault

**Verification** (last known green):
- L2 suite: **1903 passed / 0 failed / 1 skipped** (T5 pre-existing skip)
- Parity tests: 3/3 pass (KnownProfiles, KnownTenancyModels, ConfirmationAcknowledgment_IsRequired)
- Generator: `Invoke-CatalogGenerator.ps1 -Verify` OK (deterministic)
- H3 + A42 subset: 49/49 pass with new assertion locking `RequireSecretFreeIdentity=true`

---

## Adversarial verify workflow context

Everything above closes findings from the SESSION 18 adversarial e2e verify workflow:

- **Workflow ID**: `wepdcb8we`
- **Full output**: `C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-customer-provisioning-orchestration-r1\5aa5d91f-cd2d-4c24-88ae-ae9649a3fe2f\tasks\wepdcb8we.output`
- **Total findings**: 30 (13 HIGH / 13 MEDIUM / 4 LOW) across 5 skeptic lenses
- **Closed**: 10 findings (5 in Bucket A, 5 in credential cluster)
- **Remaining**: 5 HIGH + all MED/LOW (deferred per user triage)

---

## Fresh-session pickup: 5 HIGH remaining

User-approved scope for the fresh session: **fix all 5 remaining HIGHs before task 186 dispatches**. Order below reflects severity × verification cost (lowest-risk first to build momentum).

### 1. HIGH#12 — H0 preflight Model2Dedicated + warnAndProceed enforcement gap

**File**: `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/Preflight/H0PreflightHandler.cs` around line 489
**Class**: H0 cost-envelope (COMP-10 hardening)
**Failure scenario**: Direct-API caller POSTs `tenancyModel=Model2Dedicated`, `controlPlaneEnv=prod`, `costEnvelopePolicy=warnAndProceed`, over-budget estimate. SKILL Step 1.0 line 533-535 enforces the ban for skill-dispatched batch runs — but the L2 side does NOT, so a rogue caller bypasses.
**Fix plan**: In `CheckCostEnvelopeAsync`, before honoring warnAndProceed branch (line 489), read `run.TenancyModel` (or `run.Parameters.NonSecret['tenancyModel']`) — if `Model2Dedicated` OR controlPlaneEnv is `prod`, treat warnAndProceed as if absent (fall through to abortOnOverrun). Alternatively enforce at `RunsEndpoints.PostRuns` (reject 400).
**Test to add**: `H0PreflightHandlerTests.PostRuns_Model2Dedicated_WarnAndProceed_IsRejected_ForcesAbortOnOverrun`
**Estimated effort**: 30-45 min (single file, single guard, one new test)

### 2. HIGH#9 — prereqs.yaml recipes exit-0 on empty stdout (12 recipes)

**File**: `scripts/provisioning-prereqs/prereqs.yaml`
**Class**: Silent-fail traps
**Affected recipes** (per skeptic): PRQ-T-03/T-04/T-05/T-06/T-07, PRQ-S-04/S-05, PRQ-E-03/E-04/E-05/E-07/E-09/E-10 — all use `az … --query "[0].appId" -o tsv` or `az role assignment list … -o tsv` which return empty stdout with exit 0 when target does not exist.
**Failure scenario**: PRQ-T-03 checks for `Spaarke Outlook Add-in dev` app-reg via `az ad app list --filter "displayName eq '...'" --query "[0].appId"`. Missing → empty stdout → exit 0 → skill classifies as PASS. Same class for `az role assignment list` recipes → missing UAMI role assignments silent-PASS at prereqs then blow up mid-DAG at H2a/H9/H10/H12 with opaque 403.
**Fix plan**: Wrap each affected recipe body in `result=$(az … 2>/dev/null); [ -z "$result" ] && { echo "NOT_FOUND"; exit 1; }; echo "OK: $result"`. Also add `scripts/validate-prereqs-authoring.ps1` that greps every recipe for `-o tsv` OR `--query` and fails CI if the body lacks a `[ -z ` or `exit 1` line — forcing function so Wave-3 contract erosion doesn't recur.
**Test to add**: The validate-prereqs-authoring.ps1 script itself serves as the forcing function. No unit test needed.
**Estimated effort**: 1.5-2h (12 recipe edits + validate script)

### 3. HIGH#10 — Step 6a handoff-report resilience beyond HIGH#13 null-guards

**File**: `.claude/skills/provision-environment/SKILL.md` around line 1427-1440 (Step 6a fallback catch clause)
**Class**: Silent-fail trap
**Failure scenario**: HIGH#13 fix added null-guards for `$dvUrl` + `$dvToken` — but the `Invoke-RestMethod PATCH` on line 1432-1434 STILL propagates uncaught on 403 / network / expired-token failures. Steps 6b (handoff report) + 6c (final summary) never run → operator loses mandatory `runs/{runId}.md` artifact per SKILL.md line 60 MUST.
**Fix plan**: Restructure Step 6 as outer try/finally: write skeleton `runs/{runId}.md` UNCONDITIONALLY in the finally block (with "registry-stale" flag if Step 6a failed). Move the try/catch inside so registry-update failures write a `runs/{runId}-registry-stale.md` diagnostic + operator instructions BEFORE re-throwing / exiting non-zero.
**Test to add**: N/A (SKILL.md is prose + PS; no unit-test coverage today for skill flow).
**Estimated effort**: 45-60 min (structural edit of ~50 lines of SKILL.md + careful comment update)

### 4. HIGH#6 — HandlerOutcomeApplier Success branch missing explicit Release

**File**: `src/server/services/Sprk.Provisioning.ControlPlane.Core/Reconciler/HandlerOutcomeApplier.cs` around line 88-94
**Class**: I5 concurrency + rollback
**Failure scenario**: H13.MarkCompleteAsync writes RunStatus.Completed directly to Cosmos + returns HandlerResult.Success. Dispatcher invokes HandlerOutcomeApplier.ApplyHandlerOutcomeAsync which at lines 88-94 early-returns on `outcome is HandlerResult.Success` WITHOUT invoking `_runGuard.ReleaseAsync`. The only reason sprk_currentrunid is ever cleared on success is that H13 (line 602) separately calls `IRegistrySetupStatusUpdater.TransitionToReadyAsync` (delegates to `DataverseEnvironmentRegistryClient.UpdateSetupStatusAsync` with `ClearCurrentRunId=true`). Fragile coupling — any future refactor that moves the Ready-writer breaks release.
**Fix plan**: Add explicit `_runGuard.ReleaseAsync(run.CustomerId, run.RunId, cancellationToken)` best-effort call in Success branch (before return at line 90-94), gated on `run.Status is RunStatus.Completed` to avoid firing on mid-DAG per-handler successes. Stale-value-safe (Mismatched = no-op).
**Test to add**: `HandlerOutcomeApplierTests.ApplyHandlerOutcome_Success_WithCompletedStatus_InvokesReleaseExactlyOnce`
**Estimated effort**: 45 min

### 5. HIGH#7 — DataverseEnvironmentRegistryClient H13 unconditional sprk_currentrunid clear

**File**: `src/server/services/Sprk.Provisioning.ControlPlane.Core/Registry/DataverseEnvironmentRegistryClient.cs` around line 341 (`UpdateSetupStatusAsync` / `BuildPatchBody`)
**Class**: I5 concurrency + rollback
**Failure scenario**: H13's Ready-writer PATCH sends `{ "sprk_setupstatus": 2, "sprk_currentrunid": null }` with only `Prefer: return=minimal` — no `If-Match` header, no equality check. Contrasts sharply with `CustomerRunGuard.ReleaseAsync` (Concurrency/CustomerRunGuard.cs:278-321) which does LookupAsync → runId-equality check → TryClearAsync with If-Match ETag. Two write paths clearing the same column with different safety modes = subtle concurrency skew.
**Fix plan**: Route H13's release through `ICustomerRunGuard.ReleaseAsync` instead of piggy-backing on Ready PATCH. Change `UpdateSetupStatusAsync`/`BuildPatchBody` to write ONLY `sprk_setupstatus` (drop the `ClearCurrentRunId` param), add explicit `_runGuard.ReleaseAsync(...)` call in `H13.MarkCompleteAsync` AFTER Ready PATCH succeeds. Mismatched result treated as benign no-op (parity with QuarantineClearService.ClearAsync's release handling).
**Test to add**: `DataverseEnvironmentRegistryClientTests.UpdateSetupStatus_DoesNotClearCurrentRunId_AfterUnification` + `H13.MarkComplete_CallsRunGuardRelease_AfterRegistryPatch`
**Estimated effort**: 1-1.5h (coordination between two files + tests for both surfaces)

---

## Total remaining effort estimate

~4.5-6h for all 5. If context is tight, prioritize in the ORDER above (HIGH#12 fastest, HIGH#7 largest).

---

## Post-HIGHs sequence

After all 5 HIGHs land:
1. Run full L2 test suite + verify all 1903+ tests pass (any new tests added push the count higher).
2. Regenerate catalog artifacts one final time + verify determinism.
3. `git status --porcelain` should be clean.
4. Push to origin.
5. **Task 186 dispatch**: `/provision-environment trial1 --batch runs/trial1-intake.json` via the L3 skill (NEVER bypass — root CLAUDE.md §4 mandatory task-execute protocol).

---

## Deferred (Bucket B non-HIGH — post-dispatch cleanup)

- 13 MEDIUM findings across all 5 lenses (schema drift, generated-script secondary refs, I5 conflict handling, H0 hardening, skill flow gaps)
- 4 LOW findings (docstring drift, ordering optimization, sample doc drift)

Deferred to a follow-on wave with proper task decomposition. Do NOT attempt in the same fresh session as the 5 HIGHs — context budget will exhaust.

---

## Standing binding rules (already known, restated for clarity)

- Never CREATE / seed / restore `BFF-API-ClientSecret` (either casing) — auth-v4 task 033 DELETED it 2026-08-24; CredentialGuardTests fails build on new `.WithClientSecret` sites
- Never DELETE `Dataverse-ClientSecret` before 2026-11-23 (auth-v4 owns retirement)
- Never touch claude.ai Gmail/Calendar/Drive MCPs (Spaarke doesn't use them)
- Sub-Agent Write Boundary: `.claude/**` = main-session only; sub-agents can READ but not WRITE
- Do NOT run task 186 without invoking `/provision-environment` skill
- Operator uses OWN AAD identity (NEVER service principal) per NFR-11
- Canonical Spaarke regional strategy: westus2 platform + westus3 OpenAI
