# Register-EntraAppRegistrations.ps1 -TenantId Fix — Verification Report

> **Task**: `projects/customer-provisioning-orchestration-r1/tasks/066-verify-register-entra-fix-and-pre-commit-scan.poml`
> **Wave**: Wave 4 Batch 4C
> **Date**: 2026-08-17
> **Verifier**: main-session (Sonnet 5 @ high per POML `<model-tier>`)
> **Branch**: `work/customer-provisioning-orchestration-r1` (HEAD `1d28c5f81`)

---

## 1. Verification result — PASS

Commit **`1834b77bc`** ("`docs(customer-provisioning): design.md v3.3 — owner-review round Q1–Q7 + Q5 Graph/SPE spike`") is present in the current branch and includes the code fix removing the hardcoded Spaarke tenant default from `scripts/Register-EntraAppRegistrations.ps1` `-TenantId` parameter, converting it to `[Parameter(Mandatory = $true)]` with no default value. The fix is intact at HEAD `1d28c5f81`.

---

## 2. Evidence

### 2.1 Commit exists in current branch

```
$ git log --oneline | grep 1834b77bc
1834b77bc docs(customer-provisioning): design.md v3.3 — owner-review round Q1–Q7 + Q5 Graph/SPE spike
```

### 2.2 Commit body cites the code fix explicitly

Excerpt from `git show 1834b77bc`:

```
## Code fix

- scripts/Register-EntraAppRegistrations.ps1:63: hardcoded Spaarke tenant
  default REMOVED per §4D I1 tenant-isolation invariant. Was:
  [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
  Now: [Parameter(Mandatory=$true)] [string]$TenantId
  Doc comments + examples updated. Prevents accidental cross-tenant
  provisioning (e.g., running script without -TenantId → customer
  app-reg in Spaarke tenant → Spaarke users have access to customer
  Dataverse).
```

### 2.3 Current-branch line inspection

`scripts/Register-EntraAppRegistrations.ps1` lines 122-129 (the `param(...)` block):

```powershell
param(
    [Parameter(Mandatory = $true)]
    [string]$TenantId,
    [string]$KeyVaultName = "sprk-platform-prod-kv",
    [string]$ProductionApiDomain = "api.spaarke.com",
    [string]$DataverseOrgUrl = "",
    [switch]$DryRun,
    [switch]$SkipBffApi,
```

The `-TenantId` parameter:
- IS `[Parameter(Mandatory = $true)]` — confirmed.
- Has NO default value — confirmed (no `= "..."` after `[string]$TenantId`).
- Is the ONLY tenant parameter in the block.

### 2.4 Documentation aligned with fix

`scripts/Register-EntraAppRegistrations.ps1` doc-comment header (lines 55-63, 106-113) explicitly documents:
- Prerequisites now list `-TenantId REQUIRED (v3.3 tenant-isolation invariant I1)`.
- `.PARAMETER TenantId` describes the parameter as `MANDATORY (v3.3 tenant-isolation invariant I1 — no hardcoded default; requiring the operator to pass -TenantId prevents cross-tenant provisioning accidents like accidentally provisioning the customer's app-reg in Spaarke's tenant)`.
- `.NOTES` block includes: `v3.3 change: -TenantId is MANDATORY (was defaulted to Spaarke tenant) per r1 design.md §4D tenant-isolation invariant I1 (landed 1834b77bc).`
- The two `.EXAMPLE` invocations both pass `-TenantId` explicitly.

---

## 3. STOP-and-escalate condition NOT triggered

POML step 5 escalation clause: "If verification shows the fix is NOT present in the current branch, STOP and escalate per CLAUDE.md §6.5 — do NOT silently re-apply; the fix might have been reverted for a reason."

- Fix IS present in the current branch. No escalation required.
- No re-apply performed. No modifications to `scripts/**` in this task.

---

## 4. Task 065 sibling-script coordination

Task 064's baseline sweep found three sibling scripts with the SAME shape the `1834b77bc` fix eliminated on `Register-EntraAppRegistrations.ps1`. These are task 065's territory — not this task's — and are noted here purely for reader context (task 066's I1 ArchTest correctly reports them because the scanner is generic per §5):

| File:line | Parameter | Owner |
|---|---|---|
| `scripts/Register-BffMiWithContainerType.ps1:25` | `[string]$TenantId = 'a221a95e-…'` | task 065 |
| `scripts/Setup-EntraInfrastructure.ps1:60` | `[string]$TenantId = 'a221a95e-…'` | task 065 |
| `scripts/Test-EntraAppRegistrations.ps1:50` | `[string]$TenantId = 'a221a95e-…'` | task 065 |

These are NOT this task's to fix. This task's mandate is verification of the `1834b77bc` fix on `Register-EntraAppRegistrations.ps1` + hardening the I1 ArchTest (§5). The three sibling offenders remain as I1 test failures until task 065 lands its parallel commit; this is expected per the POML "if 065 hasn't finished yet, your test may still show 3 baseline fails — that's OK" clause.

---

## 5. I1 ArchTest — already generic; regression seed added

`tests/Spaarke.ArchTests/TenantIsolation/I1_NoHardcodedTenantTests.cs` (landed by task 064 as commit `40b09f837`) already scans every `.ps1` under `scripts/**` recursively via `Directory.EnumerateFiles(scriptsDir, "*.ps1", SearchOption.AllDirectories)`. **No generalization was required.** Empirical proof: the test caught the three sibling offenders listed in §4, on scripts other than the one the `1834b77bc` fix targeted.

Task 066's hardening contribution (POML step 6): a **regression-seed / end-to-end negative control** test — `ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine` — that closes the "does the scanner actually catch a real file, or only an in-memory regex?" gap task 064 left open. The test:

1. Creates a temp `SeedRegression.ps1` under `Path.GetTempPath()` (NOT under `scripts/**` so it cannot pollute the real repo tree) with a tenant-shaped default on `[string]$TenantId`.
2. Invokes the extracted `ScanForI1Offenders(scanRoot, excludes)` helper against just the temp dir.
3. Asserts the offender is reported with correct file name, line number (line 3), GUID, parameter name, and remediation guidance including the commit-SHA reference.
4. Deletes the temp file in `finally`.

Small refactor accompanying the seed test: the inline scan in `ProvisioningScripts_HaveNoHardcodedTenantDefault` was extracted into the private helper `ScanForI1Offenders(scanRoot, excludedRelPrefixes)`; `EnumerateProvisioningScripts` gained an `excludedRelPrefixes` parameter (was reading the static `ExcludedRelDirs` field). Behavior of the main test is unchanged — it now calls `ScanForI1Offenders(scriptsDir, ExcludedRelDirs)`.

---

## 6. Test results

`dotnet test tests/Spaarke.ArchTests/ --filter "FullyQualifiedName~I1_NoHardcoded"`:

| Test | Result (task 066 code + task 065 WIP on disk) | Notes |
|---|---|---|
| `ProvisioningScripts_HaveNoHardcodedTenantDefault` | **PASS** | Task 065 has applied fixes to `Register-BffMiWithContainerType.ps1`, `Setup-EntraInfrastructure.ps1`, `Test-EntraAppRegistrations.ps1` on disk (uncommitted at time of this verification). ArchTest reads files from disk → passes. |
| `TenantIdDefaultRegex_FlagsHardcodedGuidDefault` | PASS | Regex negative-control (task 064) |
| `TenantIdDefaultRegex_PermitsCompliantShapes` | PASS | Regex negative-control (task 064) |
| `ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine` | **PASS** | End-to-end seed (task 066 — new); temp-dir isolation means result independent of task 065 state |

Build: `dotnet build tests/Spaarke.ArchTests/` — 0 warnings / 0 errors.

**Coordination-timing note**: task 066's commit uses `git commit --only` on task 066 files ONLY (tests + notes + TASK-INDEX). Task 065's WIP on the three sibling scripts + `GraphClientFactory.cs` is NOT included in this commit — task 065 owns that commit. If task 066's commit lands on any branch that does NOT yet have task 065's fixes, the main I1 test will FAIL on that branch with the three baseline offenders — this is the expected, correct behavior (that's the whole point of the ArchTest). When task 065 lands, all four tests pass together across the whole branch history.

---

## 7. Acceptance-criteria coverage

| POML acceptance criterion | Result |
|---|---|
| Verification report cites commit SHA 1834b77bc and confirms Register-EntraAppRegistrations.ps1:63 has -TenantId as Mandatory with no default | ✅ §1-§2 |
| I1_NoHardcodedTenantTests scans EVERY .ps1 under scripts/ (verified by test coverage against a temp .ps1 with a tenant-shaped default → test fails; remove temp file) | ✅ §5 + `ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine` |
| All 5 ArchTests (I1-I5) PASS against current codebase; dotnet test tests/Spaarke.ArchTests/ exits 0 | ⚠ I1 (this task's scope) — **all 4 I1 tests PASS on disk with task 065's WIP applied** (task 065 uncommitted at time of verification; when both commits land the aggregate branch state is green). I2/I3/I5 baseline fixes are task 065 territory; I4 has always passed. |
| Negative: if the fix is NOT present in the current branch, task STOPS and escalates per CLAUDE.md §6.5 rather than silently re-applying | ✅ Fix IS present; no escalation triggered; no re-apply performed |
| dotnet build exits 0; dotnet test exits 0; zero analyzer warnings | ⚠ dotnet build passes with 0 warnings; dotnet test I1 exit non-zero until task 065 lands (see above) |

---

*Report generated per POML step 3. Deviations note at `notes/task-066-deviations.md`.*
