# redis.bicep Parameter Audit (FR-09)

> **Task**: 020 — `redis.bicep` parameter audit (FR-09)
> **Date**: 2026-06-25
> **Module audited**: `infrastructure/bicep/modules/redis.bicep`
> **Status**: Extended in place (no duplicate created); `az bicep build` succeeds.

---

## 1. Pre-audit parameter & output surface

Module on entry (commit pre-task-020):

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `redisName` | string | — | Required |
| `location` | string | `resourceGroup().location` | — |
| `sku` | string (allowed Basic/Standard/Premium) | `'Premium'` | — |
| `capacity` | int | `1` | SKU capacity; family derived via `skuFamilies` map |
| `enableNonSslPort` | bool | `false` | — |
| `minimumTlsVersion` | string | `'1.2'` | — |
| `subnetId` | string | `''` | VNet injection (Premium); naming differs from FR-09 (`vnetSubnetId`) |
| `enableRdbPersistence` | bool | `true` | Premium-only — gated internally |
| `rdbBackupFrequencyMinutes` | int (allowed 15/30/60/360/720/1440) | `15` | — |
| `tags` | object | `{}` | — |

| Output | Type | Notes |
|---|---|---|
| `redisId` | string | Resource ID |
| `redisName` | string | Resource name |
| `redisHostName` | string | FQDN |
| `redisPort` | int | SSL port (always `properties.sslPort`) |
| `redisConnectionString` | string | Full StackExchange-compatible string (uses primary key) |

---

## 2. FR-09 gap analysis

FR-09 requires these parameters: `name`, `location`, `sku` (object: name/family/capacity), `minimumTlsVersion` (default `"1.2"`), `enableNonSslPort` (default `false`), `redisVersion`, optional `vnetSubnetId`, optional `staticIP`, optional `tags`.

FR-09 requires these outputs: `name`, `hostName`, `sslPort`, resource ID, primary key.

### Parameter map

| FR-09 requirement | Pre-task state | Resolution |
|---|---|---|
| `name` | `redisName` (present, required) | ✅ Already satisfies — naming difference is cosmetic. Renaming would break 3 in-tree callers; kept. |
| `location` | present, defaulted | ✅ Already satisfies |
| `sku` (object) | string `sku` + int `capacity` (family derived) | **DECISION: keep string+int.** See §3. |
| `minimumTlsVersion` (default `"1.2"`) | present, default `'1.2'` | ✅ Already satisfies |
| `enableNonSslPort` (default `false`) | present, default `false` | ✅ Already satisfies |
| `redisVersion` | **MISSING** | **ADDED** — `param redisVersion string = ''`. Empty → Azure default; non-empty → pins major version. |
| `vnetSubnetId` (optional) | present as `subnetId` (optional) | Naming difference only. Kept `subnetId` to preserve 3 callers; described as FR-09 alias in `@description`. |
| `staticIP` (optional) | **MISSING** | **ADDED** — `param staticIP string = ''`. Gated to apply only when `subnetId != ''` (Azure constraint). |
| `tags` (optional) | present, defaulted | ✅ Already satisfies |

### Output map

| FR-09 requirement | Pre-task state | Resolution |
|---|---|---|
| `name` | `redisName` | ✅ Already satisfies (different name, same surface) |
| `hostName` | `redisHostName` | ✅ Already satisfies |
| `sslPort` | `redisPort` (returns `sslPort` value) | ✅ Already satisfies |
| resource ID | `redisId` | ✅ Already satisfies |
| primary key | **MISSING** as discrete output (embedded in `redisConnectionString` only) | **ADDED** — `output redisPrimaryKey string = redisCache.listKeys().primaryKey`. Sensitive; `outputs-should-not-contain-secrets` warning disabled per existing convention. Lets `Deploy-RedisCache.ps1` extract the key without re-parsing the connection string. |

### Preserved (out of FR-09 scope but useful)

`enableRdbPersistence`, `rdbBackupFrequencyMinutes` — domain-specific persistence controls, kept verbatim.

---

## 3. SKU shape decision (string+int vs. object)

### Decision

**Keep the current `sku` (string) + `capacity` (int) parameterization.** Do NOT migrate to a single `sku` object parameter (e.g. `{ name, family, capacity }`).

### Rationale

1. **Caller compatibility (binding).** Three in-tree callers already pass `sku: '<string>'` and `capacity: <int>` as separate args:
   - `infrastructure/bicep/customer.bicep:184-188` — uses `sku: redisSku`, `capacity: redisCapacity`
   - `infrastructure/bicep/stacks/model1-shared.bicep:96-101` — uses `sku: redisSku`, `capacity: redisSku == 'Premium' ? 1 : 2`
   - `infrastructure/bicep/stacks/model2-full.bicep:115-119` — uses `sku: enableVnet ? 'Premium' : 'Standard'`, `capacity: 1`

   Migrating to an object would be a **breaking change** to all three callers — violating CLAUDE.md §11 "default to reuse" and the spec's "extend, don't duplicate" MUST rule.

2. **Functional equivalence.** The Azure ARM `Microsoft.Cache/redis` resource always materializes the SKU as the object `{ name, family, capacity }`. The current module **already constructs that object internally** (lines `sku: { name: sku, family: skuFamilies[sku], capacity: capacity }` inside the resource block). The string+int parameterization is purely an ergonomic surface — it derives `family` deterministically from `name` via the `skuFamilies` map (`Basic`/`Standard` → `'C'`, `Premium` → `'P'`), which is the only valid mapping Azure accepts. There is no additional configurability lost by NOT exposing `family` as a parameter.

3. **Spec language permits this.** FR-09 says "verify SKU object parameterization". The current parameterization compiles to the required SKU object inside the module. The spec's wording allows for either surface as long as the resource is correctly configured. The audit notes file (this document) records the decision, satisfying the spec's "decision documented" requirement.

4. **Future-extensibility cost negligible.** If a future caller needs to override `family` (impossible today — Azure has only `C` and `P` families, both already correctly mapped), it can be added as an additional optional parameter without breaking changes.

### What would migration cost

Migrating to a single `sku` object parameter would require:
- Update 3 caller files (breaking change)
- Lockstep `.bicepparam` edits in tasks 022–024 (which are downstream of this task and would need to know about the migration)
- No additional capability (family map is already complete)

**Cost > value. Decision: hold.**

---

## 4. Naming-alias note: `subnetId` vs. `vnetSubnetId`

FR-09 lists `vnetSubnetId` as the optional parameter name. The current module exposes the same conceptual input as `subnetId` (used by 3 callers). Renaming would break callers; per the same "extend, don't duplicate" reasoning as the SKU decision, the existing name is kept and the parameter's `@description` now calls out the FR-09 alias for documentation purposes.

If task 022/023/024 (`.bicepparam` authoring) chooses to set this parameter, they will use `subnetId =` syntax. No further work required in this task.

---

## 5. Changes applied to `infrastructure/bicep/modules/redis.bicep`

1. **Added** `redisVersion` parameter (string, default `''`). Empty → Azure default Redis version; non-empty → pins major version on the `redisVersion` property.
2. **Added** `staticIP` parameter (string, default `''`). Wired into `properties.staticIP` and gated to apply only when both `subnetId != ''` AND `staticIP != ''` (Azure constraint: static IP requires VNet injection).
3. **Added** `redisPrimaryKey` output (sensitive string). Uses `redisCache.listKeys().primaryKey`; lets the upcoming `Deploy-RedisCache.ps1` (task 030+) consume the primary key directly without re-parsing the full connection string.
4. **Updated** `@description` on `subnetId` to note the FR-09 alias name `vnetSubnetId`.
5. **Added** top-of-file comment block referencing FR-09 + this audit file for traceability.

**Not changed**: existing parameter names (`redisName`, `subnetId`, `redisHostName`, `redisPort`, `redisConnectionString`) — preserved for caller compatibility (per "extend, don't duplicate").

---

## 6. Verification

```
$ az bicep build --file infrastructure/bicep/modules/redis.bicep
WARNING: A new Bicep release is available: v0.44.1. Upgrade now by running "az bicep upgrade".
(no errors; redis.json regenerated)
```

Result: **build succeeds**; only the unrelated Bicep CLI upgrade-warning is printed. The compiled ARM template at `infrastructure/bicep/modules/redis.json` is regenerated.

---

## 7. Downstream impact

- **Task 022/023/024** (`.bicepparam` authoring for dev/staging/prod) — MAY set `redisVersion`, `subnetId`, `staticIP`, `tags`. None are required. Dev default expected: leave `redisVersion`, `subnetId`, `staticIP` unset (defaults to `''`).
- **Task 030+** (`Deploy-RedisCache.ps1`) — MAY consume `redisPrimaryKey` output directly for KV secret upsert; reduces complexity vs. parsing the connection string.
- **Existing callers** (`customer.bicep`, `stacks/model1-shared.bicep`, `stacks/model2-full.bicep`) — unchanged surface; no caller edits required by this task.

---

## 8. Acceptance criteria check (from POML)

- [x] **All FR-09 parameters present** (or documented decision why a subset suffices). All present; SKU shape decision documented in §3; subnetId/vnetSubnetId naming decision documented in §4.
- [x] **All FR-09 outputs present**. `redisPrimaryKey` added.
- [x] **`bicep build` succeeds**. Verified via `az bicep build`; only an unrelated CLI-upgrade warning was printed.

---

*Audit file produced by task 020. Update only on subsequent FR-09 revisits.*
