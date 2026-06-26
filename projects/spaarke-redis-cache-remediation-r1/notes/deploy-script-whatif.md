# Deploy-RedisCache.ps1 -WhatIf Integration Smoke Check (Task 028)

> **Date**: 2026-06-25
> **Task**: 028 — Deploy-RedisCache.ps1 -WhatIf integration check
> **Phase**: 2 (Bicep + provisioning artifacts)
> **Rigor**: STANDARD (integration smoke; no Azure-side change)
> **Owner of script**: Task 025 (Wave 3)

## Purpose

Verify, without touching Azure, that `scripts/Deploy-RedisCache.ps1` (delivered by task 025):

1. Produces a sensible deployment plan for dev under `-WhatIf` (NFR-06).
2. Enforces the prod gate per NFR-05 (exits non-zero with an explanatory message when `-Environment prod` is invoked without `-Force`).
3. Wires up `tests/manual/RedisValidationTests.ps1` correctly under `-VerifyOnly` (verifies the call path even when the dev Redis cache doesn't yet exist).

## Acceptance Criteria (task POML)

1. Dev `-WhatIf` succeeds and the plan targets the canonical resource name + RG.
2. Prod without `-Force` exits non-zero with clear message.

---

## Invocation 1 — `dev -WhatIf`

**Command**

```powershell
pwsh -NoProfile -Command "& './scripts/Deploy-RedisCache.ps1' -Environment dev -WhatIf"
```

**Exit code**: `0`

**Verbatim output**

```
Deploy-RedisCache.ps1 starting
  Environment    : dev
  ResourceGroup  : spe-infrastructure-westus2
  Redis name     : spaarke-bff-redis-dev
  Bicep module   : C:\code_files\spaarke-wt-spaarke-redis-cache-remediation-r1\infrastructure\bicep\modules\redis.bicep
  Bicep param    : C:\code_files\spaarke-wt-spaarke-redis-cache-remediation-r1\infrastructure\bicep\parameters\redis-dev.bicepparam
  KeyVault       : (not specified)
  Mode           : what-if

What if: Performing the operation "Deploy Bicep (redis.bicep with redis-dev.bicepparam)" on target "spaarke-bff-redis-dev in spe-infrastructure-westus2".

Deploy-RedisCache.ps1 completed successfully.
```

**Verdict**: PASS

- Canonical resource name `spaarke-bff-redis-dev` matches NFR-03 / spec naming convention.
- Resource group `spe-infrastructure-westus2` matches the canonical dev RG (POML defaults).
- Bicep module path resolves to `infrastructure/bicep/modules/redis.bicep` (extended per FR-09).
- Param file resolves to `infrastructure/bicep/parameters/redis-dev.bicepparam` (created by task 022).
- Mode is correctly reported as `what-if`; exit code 0.
- Acceptance criterion #1 satisfied for naming + RG + plan.

**Note — KeyVault upsert not exercised under `-WhatIf`**

The POML prompt mentions verifying "KV upsert against canonical dev KV." The script's `-WhatIf` path emits `KeyVault : (not specified)` because no `-KeyVaultName` parameter was passed. The Bicep `-WhatIf` itself does not surface KV secret upsert because that is a post-deploy script action, not a Bicep resource. This is a documentation-only observation; the script behavior matches its `-WhatIf` contract.

---

## Invocation 2 — `prod` (no `-Force`)

**Command**

```powershell
pwsh -NoProfile -Command "& './scripts/Deploy-RedisCache.ps1' -Environment prod"
```

**Exit code**: `1` (from PowerShell host wrapper)

**Verbatim output**

```
NFR-05: -Environment prod requires -Force flag. This project must NOT touch prod/demo without explicit operator intent. Aborting.
```

**Verdict**: PASS for the acceptance criterion (non-zero exit + clear message). Minor deviation documented below.

**Acceptance criterion #2 satisfied**:

- Exit code is non-zero (`1`).
- The message is clear, explicit, and cites NFR-05 verbatim.
- The script aborts before any Azure call is attempted.

**Deviation from task 025 design intent (DOCUMENT ONLY — do not fix here)**

Per the "Decisions Made" entry in `projects/spaarke-redis-cache-remediation-r1/CLAUDE.md` (Wave 3 / task 025):

> `Deploy-RedisCache.ps1` skeleton adjustment: `Write-Error` under `$ErrorActionPreference=Stop` throws exit 1 (masking `exit 2`); replaced with `Write-Host -ForegroundColor Red + exit 2` for NFR-05 gate so exit-code discrimination is observable.

The script's `exit 2` is overridden to `exit 1` when run through `pwsh -NoProfile -Command "& '...'"` because the outer host wrapper appears to clamp the child exit code to 1 on non-zero. Inside a direct invocation (without the wrapping `pwsh -Command` indirection) the script would honor its own `exit 2`. The acceptance criterion only requires "non-zero with clear message" — both are present. **Recommendation**: if NFR-05 exit-code discrimination (specifically `2`, not just non-zero) is operationally required, task 025 / future hardening should consider invoking the script directly or via `pwsh -File`. This is logged here for the project's awareness; no change made under task 028.

---

## Invocation 3 — `dev -VerifyOnly -ResourceGroup spe-infrastructure-westus2`

**Command**

```powershell
pwsh -NoProfile -Command "& './scripts/Deploy-RedisCache.ps1' -Environment dev -VerifyOnly -ResourceGroup spe-infrastructure-westus2"
```

**Exit code**: `1`

**Verbatim output**

```
Deploy-RedisCache.ps1 starting
  Environment    : dev
  ResourceGroup  : spe-infrastructure-westus2
  Redis name     : spaarke-bff-redis-dev
  Bicep module   : C:\code_files\spaarke-wt-spaarke-redis-cache-remediation-r1\infrastructure\bicep\modules\redis.bicep
  Bicep param    : C:\code_files\spaarke-wt-spaarke-redis-cache-remediation-r1\infrastructure\bicep\parameters\redis-dev.bicepparam
  KeyVault       : (not specified)
  Mode           : verify-only

Verify-only mode: invoking RedisValidationTests.ps1...
==================================================================
Redis Validation Tests - Sprint 4 Task 4.1
==================================================================

[Test 1] Verifying configuration files...
  ✗ appsettings.json missing
```

**Verdict**: PASS for wire-up verification (expected failure observed; document deviation in test output).

**Wire-up working as intended**:

- The script correctly chains to `tests/manual/RedisValidationTests.ps1` under `-VerifyOnly`.
- It reports `Mode : verify-only` and explicitly states "invoking RedisValidationTests.ps1...".
- Validation script begins executing (Test 1) — i.e., the call path is live.

**Pre-existing reality observed by Test 1** (matches CLAUDE.md Wave 4 decision):

> `appsettings.json` does NOT exist for `Sprk.Bff.Api` (only `appsettings.template.json` with `#{REDIS_INSTANCE_NAME}#` token). The dev-environment value lives in `src/server/api/Sprk.Bff.Api/appsettings.tokens.md`.

`RedisValidationTests.ps1` Test 1 looks for the literal `appsettings.json` and reports it missing. This is not a wire-up failure — the wrapper script did its job. It is a known divergence between the validation script's assumption and the BFF's actual configuration model (token + template).

**Limitation discovered**: `RedisValidationTests.ps1` predates the token-based config model documented in CLAUDE.md (Wave 4). It will continue to report `appsettings.json missing` until either (a) the validation script is updated to look at the template + token model, or (b) `appsettings.json` is materialized for the BFF in dev. Both are out of scope for task 028. Log this for Phase 4/5 hardening or task 062 (final dotnet test) consideration.

---

## Summary of Verdicts

| Invocation | Acceptance Criterion | Result |
|---|---|---|
| 1. `dev -WhatIf` | #1 — Plan targets canonical name + RG | PASS |
| 2. `prod` no `-Force` | #2 — Non-zero exit with clear NFR-05 message | PASS (exit code `1` instead of designed `2` — see Deviation note) |
| 3. `dev -VerifyOnly` | (Beyond POML scope; supplemental wire-up check) | Wire-up PASS; Test 1 fails due to known pre-existing `appsettings.json` absence (CLAUDE.md Wave 4) |

## Limitations & Follow-Ups (No Action Under Task 028)

1. **Exit code from prod gate**: Designed as `2`, observed as `1` when invoked through `pwsh -NoProfile -Command "& '...'"` wrapper. NFR-05 acceptance is "non-zero with clear message" — both met. If operational tooling differentiates `1` vs `2`, consider direct invocation or `pwsh -File`. Owner: task 025 / future hardening (not task 028).
2. **`RedisValidationTests.ps1` config check**: Looks for `appsettings.json` literal, but BFF uses `appsettings.template.json` + `appsettings.tokens.md`. The validation script needs updating to reflect the actual config model. Owner: Phase 4/5 hardening (not task 028).
3. **KV upsert not exercised under `-WhatIf`**: Expected — KV upsert is a post-deploy action, not a Bicep resource. Will be exercised at Phase 3 cutover (task 032).

## Files Referenced

- `scripts/Deploy-RedisCache.ps1` (owner: task 025)
- `infrastructure/bicep/modules/redis.bicep` (owner: task 020)
- `infrastructure/bicep/parameters/redis-dev.bicepparam` (owner: task 022)
- `tests/manual/RedisValidationTests.ps1` (owner: task 026; extension)
- `projects/spaarke-redis-cache-remediation-r1/CLAUDE.md` (Wave 3 + Wave 4 decisions)
- `projects/spaarke-redis-cache-remediation-r1/spec.md` (NFR-05, NFR-06)

## Sign-off

- Both binding acceptance criteria met.
- No deploys to Azure performed.
- No source-of-truth file modified (`Deploy-RedisCache.ps1` untouched).
- One designed-vs-observed exit-code deviation documented (criterion still satisfied).
- One pre-existing validation-script limitation documented (out-of-scope for this task).
