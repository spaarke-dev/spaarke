# infrastructure/bicep/parameters Drift Audit (FR-10)

> **Task**: 021 - `infrastructure/bicep/parameters` drift audit (FR-10)
> **Date**: 2026-06-25
> **Files audited**: 7 `.bicepparam` files in `infrastructure/bicep/parameters/`
> **Status**: 1 file fixed in place (`customer-template.bicepparam`); 1 follow-up flagged; all files compile.

---

## 1. Files inventoried

| File | Stack reference | Compiles pre-audit | Compiles post-audit |
|---|---|:---:|:---:|
| `customer-template.bicepparam` | `../stacks/customer.bicep` (BROKEN) -> fixed to `../customer.bicep` | NO (BCP091) | YES |
| `demo-customer.bicepparam` | `../customer.bicep` | YES | YES (untouched - NFR-05) |
| `dev.bicepparam` | `../stacks/model1-shared.bicep` | YES | YES (untouched) |
| `model2-customer-template.bicepparam` | `../stacks/model2-full.bicep` | YES | YES (untouched) |
| `platform-prod.bicepparam` | `../platform.bicep` | YES | YES (untouched) |
| `prod.bicepparam` | `../stacks/model1-shared.bicep` | YES | YES (untouched - NFR-05) |
| `staging.bicepparam` | `../stacks/model1-shared.bicep` | YES | YES (untouched) |

`platform.bicep` does NOT deploy Redis (Redis lives in `model1-shared.bicep`); `platform-prod.bicepparam` therefore has no Redis surface and was not relevant to this audit.

---

## 2. Redis-relevant settings tabulated

| File | Redis SKU | Redis capacity | KV-ref pattern | Naming convention used downstream |
|---|---|---|---|---|
| `customer-template.bicepparam` (post-fix) | `Basic` | `0` | n/a (param file does not set KV refs - stack does) | `spaarke-{customerId}-{env}-cache` (per `customer.bicep` L100) |
| `demo-customer.bicepparam` | `Basic` | `0` | n/a | `spaarke-demo-prod-cache` (per `customer.bicep`) |
| `dev.bicepparam` | `Standard` | (stack default 2) | n/a | `sprksharedev-redis` (per `model1-shared.bicep` L97 baseName) |
| `model2-customer-template.bicepparam` | n/a (no Redis param) | n/a | n/a | `model2-full.bicep` controls |
| `platform-prod.bicepparam` | n/a | n/a | n/a | platform has no Redis |
| `prod.bicepparam` | `Premium` | (stack default 1) | n/a | `sprksharedprod-redis` |
| `staging.bicepparam` | `Standard` | (stack default 2) | n/a | `sprksharedstaging-redis` |

`InstanceName: 'spaarke:'` is set in the STACK (`model1-shared.bicep` L184 and `customer.bicep` App Settings), not in any `.bicepparam` file. No `sdap:`, `spe-`, or other deprecated brand references found in any param file. KV-reference syntax `@Microsoft.KeyVault(VaultName=...;SecretName=...)` (semicolon-separated) is consistent across stacks.

---

## 3. Drift items

### D1 - `customer-template.bicepparam` BROKEN `using` reference (FIXED in-place)

- **Severity**: HIGH (file did not compile)
- **Symptom**: `using '../stacks/customer.bicep'` -- file does not exist at that path. `customer.bicep` lives at `infrastructure/bicep/customer.bicep`.
- **Evidence**: `az bicep build-params --file customer-template.bicepparam` returns `Error BCP091: Could not find file 'infrastructure/bicep/stacks/customer.bicep'`.
- **Reference comparator**: `demo-customer.bicepparam` correctly uses `using '../customer.bicep'`.
- **Fix applied**: changed `using '../stacks/customer.bicep'` to `using '../customer.bicep'`.

### D2 - `customer-template.bicepparam` wrong param schema (FIXED in-place)

- **Severity**: HIGH (would not deploy even after D1 fix)
- **Symptom**: Template declared params that don't exist in `customer.bicep` (`customerName`, `sharedKeyVaultName`, `sharedAiSearchEndpoint`, `dataverseUrl`, `speContainerId`) and OMITTED required/optional params that DO exist (`environmentName`, `location`, `platformKeyVaultName`, `storageSku`, `serviceBusSku`, `redisSku`, `redisCapacity`).
- **Reference comparator**: `demo-customer.bicepparam` declares the correct param shape per `customer.bicep` signature (lines 21-67).
- **Fix applied**: rewrote `customer-template.bicepparam` to mirror `demo-customer.bicepparam`'s param shape exactly, with `'replaceme'` placeholder values where customer-specific (vs `'demo'` literals). Per FR-06 "demo and real customers use SAME template (no special-casing)". Preserved the original how-to-use docstring header.

### D3 - `customer-template.bicepparam` placeholder length collision (FIXED in-place)

- **Severity**: MED (would not compile after D2 mass rewrite)
- **Symptom**: Initial rewrite used `'REPLACE_CUSTOMER_ID'` (19 chars), exceeding the `@maxLength(10)` constraint on `customerId` in `customer.bicep`.
- **Fix applied**: changed placeholder to `'replaceme'` (9 chars). Verified via `az bicep build-params`.

### D4 - Naming inconsistency between shared and per-customer Redis resources (FLAGGED for follow-up - NOT in scope)

- **Severity**: LOW (cosmetic; not a deploy failure)
- **Observation**: Shared (model1) Redis is named `sprksharedev-redis` (camelCase concat); per-customer Redis is named `spaarke-{customerId}-{env}-cache` (hyphenated, `-cache` suffix); planned per-env Redis (tasks 022-024) will be `spaarke-bff-redis-{env}` (per FR-10 spec). THREE different conventions across stack contexts.
- **Why flag, don't fix**: The shared `model1-shared.bicep` baseName `sprkshared{env}` is established convention and changing it would require touching the stack file, KV secret names, and any downstream references -- out of scope for this task. The customer.bicep `spaarke-{customerId}-{env}-cache` convention is also well-established. FR-10 introduces the THIRD convention (`spaarke-bff-redis-{env}`) for the dedicated BFF Redis cutover, but that's a new tier, not a rename.
- **Follow-up**: After Phase 3 cutover lands, an OPTIONAL future cleanup project could harmonize the three. Not required by this project's NFR-03 (which only mandates `spaarke-bff-redis-{env}` for the new dedicated Redis).

### D5 - `customer.bicep` and `serviceBus-queues` array contains `sdap-jobs`, `sdap-communication` (FLAGGED for follow-up - NOT this task's scope)

- **Severity**: LOW (cosmetic; FR-07 `sdap`->`spaarke` rebrand pertains to InstanceName key prefix, not Service Bus queue names)
- **Observation**: `customer.bicep` L55 and `model1-shared.bicep` L117-122 declare queues named `sdap-jobs`, `sdap-communication`. Spec FR-07 (drop `sdap` brand) was specifically scoped to the cache `InstanceName`. Renaming Service Bus queues would be a breaking change with downstream consumers.
- **Why flag, don't fix**: out of scope for FR-10 (Redis param drift audit). Mentioned here for awareness.

### D6 - All other files

No drift found. SKU per env follows a sensible ladder (dev=Standard, staging=Standard, prod=Premium) for the shared model1 Redis. `demo-customer` and `customer-template` (post-fix) both use Basic C0 as the cost-optimized default. `model2-customer-template.bicepparam` does not surface Redis params at all (model2-full.bicep controls them) -- this is by design, no drift.

---

## 4. Fixes applied summary

| File | Change | Compiles? |
|---|---|:---:|
| `customer-template.bicepparam` | Replaced entirely with corrected schema (D1+D2+D3 combined): fixed `using` path, replaced wrong params with the working shape from `demo-customer.bicepparam`, used `'replaceme'` placeholders within length constraints | YES (`az bicep build-params --file ...` clean) |

No other files modified. Prod (`prod.bicepparam`) and demo (`demo-customer.bicepparam`) untouched per NFR-05.

---

## 5. Build verification

```
az bicep build-params --file infrastructure/bicep/parameters/customer-template.bicepparam
# returns: only version-upgrade warning ("v0.44.1 available"); no errors
# exit code 0
```

All other 6 files were verified pre-task to compile (no changes made, no re-verification needed).

---

## 6. Acceptance criteria status

- [x] Audit notes file exists with table of drift items + fixes.
- [x] Any fixed `.bicepparam` files still compile (`az bicep build-params`).

---

## 7. Out-of-scope follow-ups (not blockers)

1. Optional harmonization of the three Redis naming conventions (shared / per-customer / dedicated-BFF) into one rule. Would require a separate small project; NOT required by this project's spec.
2. Optional `sdap-*` Service Bus queue rename (FR-07 rebrand expansion). Would be a breaking change with downstream consumers.

Both items are documented here for future-project visibility; neither blocks Phase 2 tasks 022-024.
