# Task 020 — customer.bicep Redis Removal Verification

**Date**: 2026-06-26
**Task**: 020-customer-bicep-redis-removal
**FR**: FR-12 (closes IaC gap left by R1)
**Outcome**: PASSED — Redis removed; no live customer affected.

---

## 1. Pre-delete safety evidence (R1 already removed Redis from runtime path)

`scripts/Provision-Customer.ps1` grep for `[Rr]edis` (relevant lines):

- Lines 421–444: explicit deprecation header — "Per Q-E Architecture 1, per-customer Redis is DEPRECATED. Redis is now provisioned per-environment via `scripts/Deploy-RedisCache.ps1`..."
- Lines 481–483: `# Note: redis* outputs from customer.bicep are intentionally NOT consumed per Q-E Architecture 1 (per-customer Redis deprecated).`
- Lines 514–517: `# Note: 'Redis-ConnectionString' is intentionally NOT set per customer (Q-E Architecture 1, FR-12).`
- Lines 1470–1471: Status summary line confirms per-customer Redis DEPRECATED.

Conclusion: R1 already neutered the runtime path. Bicep template's Redis module call was dead code.

---

## 2. Live-customer escalation gate (cleared)

`az group list --query "[?starts_with(name, 'rg-spaarke-')]"`:

| RG name | Notes |
|---|---|
| `rg-spaarke-website` (eastus2) | Website infra — not `customer.bicep` |
| `rg-spaarke-platform-prod` (westus2) | Shared platform — uses `platform.bicep` not `customer.bicep` |
| `rg-spaarke-dev` (westus2) | Dev env — not customer-pattern (`rg-spaarke-{customerId}-{env}`) |

NO resource groups match the `rg-spaarke-{customerId}-{env}` customer-deployment pattern produced by `customer.bicep` line 85 (`var resourceGroupName = 'rg-spaarke-${customerId}-${environmentName}'`).

**Escalation gate: CLEARED.** No live customer deployment depends on this template's Redis module.

---

## 3. BEFORE-delete what-if (baseline)

Command:
```
az deployment sub what-if --location westus2 \
  --template-file infrastructure/bicep/customer.bicep \
  --parameters infrastructure/bicep/parameters/customer-template.bicepparam \
  --parameters customerId=whatif tags='{...}'
```

Against fresh empty RG `rg-spaarke-whatif-prod`.

Result:
- **Resource changes: 15 to create, 1 to modify.**
- Redis resource confirmed in plan:
  - `+ Microsoft.Cache/redis/spaarke-whatif-prod-cache [2023-08-01]`
  - Action: `+ create` (NOT `delete` against live; NOT `modify` against live)
- Escalation gate cleared (no `delete`/`modify` for a live Redis).

---

## 4. Deletes applied to `infrastructure/bicep/customer.bicep`

| Element | Original lines | Action |
|---|---|---|
| Header comment "Redis Cache" bullet (line 10) | line 10 | Replaced with deprecation footnote (lines 10-15) |
| `redisSku` + `redisCapacity` params + `// --- Redis options ---` block | lines 60-67 | DELETED |
| `redisName` variable + comment | lines 99-100 | DELETED |
| `// REDIS CACHE` section + `module redis 'modules/redis.bicep'` call | lines 177-191 | DELETED |
| `// --- Redis Cache ---` outputs (`redisHostName`, `redisPort`, `redisConnectionString`) | lines 223-227 | DELETED |

Net effect: 5 distinct edits, all Redis material removed. Only `redis` substring remaining is in the deprecation comment block (lines 11-15), which is documentary not functional.

---

## 5. POST-delete verification

### 5a. `az bicep build` — PASS
```
az bicep build --file infrastructure/bicep/customer.bicep
```
Exit 0; no errors. Template parses cleanly.

### 5b. `grep -in 'redis' infrastructure/bicep/customer.bicep` — DOCUMENTARY ONLY

Matches only on deprecation comment lines 11-15 (intentional human-readable history pointer). Zero functional references.

### 5c. POST-delete what-if — PASS (no Redis in plan)

Command:
```
az deployment sub what-if --location westus2 \
  --template-file infrastructure/bicep/customer.bicep \
  --parameters customerId=whatif environmentName=prod location=westus2 \
                platformKeyVaultName=sprk-platform-prod-kv \
                storageSku=Standard_LRS serviceBusSku=Standard \
                tags='{...}'
```

(Note: inline params used — `customer-template.bicepparam` still references `redisSku`/`redisCapacity` and is cleaned in task 021.)

Result:
- **Resource changes: 14 to create, 1 to modify** (was 15 baseline; -1 = Redis dropped).
- Grep `(redis|cache)` against full what-if output: **zero matches**.

---

## 6. Acceptance criteria status

| Criterion | Status |
|---|---|
| customer.bicep contains zero functional references to Redis | PASS (only documentary deprecation comment remains) |
| `az bicep build customer.bicep` succeeds | PASS |
| `az deployment group what-if` (used `sub what-if` per template targetScope) for fresh customer shows NO Redis resource | PASS (14 vs 15 resources; zero `redis`/`cache` matches in plan) |
| No live customer requires deleted Redis module path | PASS (escalation gate cleared — no `rg-spaarke-{customerId}-{env}` RGs exist in subscription) |

## 7. Test-RG cleanup

`rg-spaarke-whatif-prod` deleted via `az group delete --no-wait` (was empty — what-if only).
