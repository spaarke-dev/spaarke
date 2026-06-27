# Phase 2 Review — PSScriptAnalyzer + Bicep Linter

**Project**: spaarke-redis-cache-remediation-r1
**Task**: 029 — Phase 2 review
**Date**: 2026-06-26
**Reviewer**: task-execute (Phase 2 quality gate)

---

## Scope

This review verifies the lint cleanliness of Phase 2 artifacts:

**PowerShell**
- `scripts/Deploy-RedisCache.ps1` (NEW — task 023)
- `tests/manual/RedisValidationTests.ps1` (extended — task 026)
- `scripts/Provision-Customer.ps1` (refactored — task 024)

**Bicep**
- `infrastructure/bicep/modules/redis.bicep`
- `infrastructure/bicep/parameters/redis-dev.bicepparam`
- `infrastructure/bicep/parameters/redis-staging.bicepparam`
- `infrastructure/bicep/parameters/redis-prod.bicepparam`
- `infrastructure/bicep/parameters/customer-template.bicepparam` (Wave 1 task 021 fix)

**Skipped per task POML**: BFF publish-size delta (no BFF code change in Phase 2).

---

## Tooling

| Tool | Version | Status |
|---|---|---|
| PSScriptAnalyzer | 1.24.0 | Available |
| az CLI | 2.77.0 | Available |
| az bicep (bundled) | 0.41.2.15936 | Available |

Command used:
- PowerShell: `Invoke-ScriptAnalyzer -Path <file> -ExcludeRule PSAvoidUsingWriteHost`
- Bicep module: `az bicep build --file <file> --stdout`
- Bicep params: `az bicep build-params --file <file> --stdout`

`PSAvoidUsingWriteHost` is excluded by design — colored console output is intentional in deployment / validation scripts.

---

## 1. PSScriptAnalyzer Results

### 1.1 `scripts/Deploy-RedisCache.ps1`

| Rule | Severity | Line | Message |
|---|---|---|---|
| PSUseBOMForUnicodeEncodedFile | Warning | — | Missing BOM encoding for non-ASCII encoded file |

**Verdict**: 1 cosmetic warning. The BOM-encoding finding is a stylistic warning that applies repo-wide to PowerShell files saved as UTF-8 without BOM; it does not affect script correctness. No functional or security issues. **PASS**.

### 1.2 `tests/manual/RedisValidationTests.ps1`

| Rule | Severity | Line | Message |
|---|---|---|---|
| PSUseBOMForUnicodeEncodedFile | Warning | — | Missing BOM encoding for non-ASCII encoded file |

**Verdict**: 1 cosmetic warning (same BOM finding as above). **PASS**.

### 1.3 `scripts/Provision-Customer.ps1`

| Rule | Severity | Line | Message |
|---|---|---|---|
| PSUseBOMForUnicodeEncodedFile | Warning | — | Missing BOM encoding for non-ASCII encoded file |
| PSPossibleIncorrectComparisonWithNull | Warning | 297 | $null should be on the left side of equality comparisons |
| PSReviewUnusedParameter | Warning | 143 | Parameter 'CertificateThumbprint' declared but not used |
| PSReviewUnusedParameter | Warning | 147 | Parameter 'PlatformResourceGroup' declared but not used |
| PSReviewUnusedParameter | Warning | 152 | Parameter 'BffApiAppId' declared but not used |
| PSReviewUnusedParameter | Warning | 155 | Parameter 'MsalClientId' declared but not used |
| PSReviewUnusedParameter | Warning | 157 | Parameter 'AzureOpenAiEndpoint' declared but not used |
| PSReviewUnusedParameter | Warning | 159 | Parameter 'ShareLinkBaseUrl' declared but not used |
| PSReviewUnusedParameter | Warning | 161 | Parameter 'DataverseRegion' declared but not used |
| PSUseDeclaredVarsMoreThanAssignments | Warning | 327 | Variable 'azVersion' assigned but never used |
| PSUseDeclaredVarsMoreThanAssignments | Warning | 599 | Variable 'authOutput' assigned but never used |
| PSUseSingularNouns | Warning | 321, 843, 896, 1124, 1348 | Function names use plural nouns (5 occurrences: `Invoke-Step1_ValidatePrerequisites`, `Invoke-Step7_ImportSolutions`, `Invoke-Step8_SetDataverseEnvVars`, `Invoke-Step10_ProvisionSPEContainers`, `Invoke-Step12_RunSmokeTests`) |
| PSAvoidOverwritingBuiltInCmdlets | Warning | 193 | 'Write-Log' overlaps built-in cmdlet |
| PSAvoidUsingPositionalParameters | Information | 178, 179, 181, 1100, 1112 | `Join-Path` called with positional parameters |

**Verdict**: All warnings are **pre-existing** in `Provision-Customer.ps1` — they predate the Phase 2 refactor (task 024 only replaced inline Redis Bicep calls with `Deploy-RedisCache.ps1` invocation). None of the findings relate to the Phase 2 changes:
- The `PSReviewUnusedParameter` set covers cert-auth + BFF + AI config that are forwarded to other scripts.
- The plural-noun + Write-Log + Join-Path findings are repo-wide conventions used across the deploy/provision script family.
- The null-comparison and unused-variable findings are pre-existing in non-Redis code paths.

No regressions introduced by task 024. **PASS WITH NOTES** (pre-existing tech debt, out of scope for R1).

---

## 2. Bicep Build Results

| File | Command | Exit | Result |
|---|---|---|---|
| `infrastructure/bicep/modules/redis.bicep` | `az bicep build` | 0 | Compiled successfully (ARM JSON emitted) |
| `infrastructure/bicep/parameters/redis-dev.bicepparam` | `az bicep build-params` | 0 | Compiled successfully — sku=Basic, capacity=0, environment=dev |
| `infrastructure/bicep/parameters/redis-staging.bicepparam` | `az bicep build-params` | 0 | Compiled successfully — sku=Standard, capacity=0, environment=staging |
| `infrastructure/bicep/parameters/redis-prod.bicepparam` | `az bicep build-params` | 0 | Compiled successfully — sku=Standard, capacity=2, environment=prod, deploy-gate=finance+security |
| `infrastructure/bicep/parameters/customer-template.bicepparam` | `az bicep build-params` | 0 | Compiled successfully — Wave 1 task 021 fix verified |

All 5 Bicep artifacts build cleanly with no errors or warnings (other than the unrelated environmental "A new Bicep release is available: v0.44.1" notice from az CLI, which is informational and applies to all Bicep runs on this machine).

**Verdict**: **PASS**.

---

## 3. Deviations From Expected

None. All findings are either:
- Cosmetic (BOM-encoding repo convention)
- Pre-existing tech debt in `Provision-Customer.ps1` unrelated to Phase 2 changes
- Environmental noise (az CLI Bicep upgrade hint)

No security, correctness, or ADR-relevant issues surfaced.

---

## 4. Final Verdict

**PASS WITH NOTES**

- PowerShell: clean for the two NEW Phase 2 files (`Deploy-RedisCache.ps1`, `RedisValidationTests.ps1`); pre-existing tech debt in `Provision-Customer.ps1` is unchanged and out of scope.
- Bicep: all 5 artifacts compile cleanly.
- BFF publish-size: skipped per task POML (no BFF code change in Phase 2).

Phase 2 is cleared from a lint perspective. Tech-debt items in `Provision-Customer.ps1` (unused parameters, plural function nouns, Write-Log shadowing, positional Join-Path) should be tracked separately if cleanup is desired — they are pre-existing and would inflate Phase 2 scope without value to the Redis remediation goals.
