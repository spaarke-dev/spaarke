# `/beta` vs `/v1.0` — empirical surface verification

> **Task 020** (spec FR-C01) · 2026-08-23 · **🔔 ESCALATION TRIGGER FIRED — task paused for a decision**
> **Method**: live app-only Graph calls against Spaarke Dev (`a221a95e-…`), container type
> `8a6ce34c-…`. Read-only — no container was created, modified, or deleted.
> **No secret value or token appears in this file.**

---

## 🔔 Why this stopped

The POML's escalation trigger:

> *"If a surface believed GA turns out to behave differently on v1.0 than beta (different shape, missing
> field, changed pagination), STOP and escalate rather than adapting the mapping silently — a shape
> change may affect DTOs consumed by the client and is a scope question."*

**It fired on the single most consequential field in the project.**

---

## 1. GA status — measured, not assumed

| Endpoint | v1.0 | beta | GA on v1.0? |
|---|---|---|---|
| `/storage/fileStorage/containers` (filtered) | 200 | 200 | ✅ |
| `/storage/fileStorage/deletedContainers` | 200 | 200 | ✅ |
| `/storage/fileStorage/containerTypeRegistrations` | 200 | 200 | ✅ |
| `/storage/fileStorage/containerTypes` (app-only) | 403 | 403 | delegated-only — confirms task 010 |

## 2. 🔴 But v1.0 `containers` is MISSING fields beta returns

Same tenant, same token, same container type, same moment:

| Property | v1.0 LIST | beta LIST | v1.0 GET | beta GET |
|---|---|---|---|---|
| `id`, `displayName`, `containerTypeId`, `createdDateTime`, `settings` | ✅ | ✅ | ✅ | ✅ |
| **`storageUsedInBytes`** | ❌ **absent** | ✅ **present** | ❌ absent | ❌ absent |
| **`ownershipType`** | ❌ absent | ✅ `tenantOwned` | ❌ absent | ✅ present |

**`storageUsedInBytes` is not in the v1.0 schema at all** — not merely omitted by default:

```
GET /v1.0/storage/fileStorage/containers?$select=id,displayName,storageUsedInBytes
→ 400  "Parsing OData Select and Expand failed:
        Could not find a property named 'storageUsedInBytes'"
```

The same `$select` on beta returns **200** with the value. An absent-by-default property would have
been returned on explicit `$select`; a 400 means the v1.0 schema does not define it.

**It is also LIST-only.** Even on beta, `GET /beta/…/containers/{id}` omits `storageUsedInBytes`. Any
per-container storage figure must be sourced from the LIST projection.

---

## 3. ✅ This answers task 024's spike in advance — and the answer is YES

FR-C06 was written as a two-branch requirement: *implement if Graph exposes consumption, else remove the
tile + column* (removal pre-authorized by owner decision OC-04).

**Graph does expose it.** So the branch resolves to **implement**, and the four hardcoded
`StorageUsedInBytes: null` sites (`:645`, `:976`, `:1060`, `:1110`) are fixable — **but only while
containers are read from `/beta` via LIST.** Task 024 should not re-run the spike; it should start from
this table.

---

## 4. 🔴 Tasks 020 and 024 are in direct conflict

- **020** wants `/beta` eliminated.
- **024** needs `storageUsedInBytes`, which **only exists on `/beta`**.

They cannot both be satisfied for the containers endpoint. This is a scope question, not an
implementation detail — hence the stop.

### The conflict is structural, not per-call

`SpeAdminGraphService.CreateGraphClient` (`:4261`) is reached through `GetClientForConfigAsync`, which
serves **every** `…ForConfigAsync` method — containers, container types, recycle bin, search, security,
audit. **Flipping that one base address flips all of them at once**, so a wholesale migration cannot
spare the containers endpoint. Migration has to be per-operation, or the shared client stays on beta.

---

## 5. Much of task 020's goal is already delivered — by task 011

The POML's stated concern is container **types**. Task 011 moved container-type LIST onto
`IGraphClientFactory.ForUserAsync`, and **that factory path already builds a `v1.0` client**
(`GraphClientFactory.cs:327`). So container-type list is on v1.0 today.

### The four `/beta` sites, re-scoped

| # | Site | Serves | Assessment |
|---|---|---|---|
| 1 | `SpeAdminGraphService.cs:951` | hand-built nextLink for containers LIST | **must match** whatever base the containers client uses — currently beta, correctly |
| 2 | `SpeAdminGraphService.cs:4261` | `CreateGraphClient` — **all** `…ForConfigAsync` | ⚠️ flipping this loses `storageUsedInBytes` + `ownershipType` |
| 3 | `SpeAdminGraphService.cs:4278` | `CreateGraphClientFromBearerToken` — the `SpeAdminTokenProvider` OBO path | **dead code** since path A (tasks 010/011). Delete rather than migrate |
| 4 | `GraphClientFactory.cs:164` | `ForApp()` — BFF-wide app-only | **not named in the POML.** Out of R2 scope: it serves far more than SpeAdmin |

### ⚠️ Also found: task 011 left a version split

Container-type **LIST** runs delegated → **v1.0**; container-type **GET/CREATE** still run through
`…ForConfigAsync` → **beta**. One resource, two API versions, no comment saying so. That is the shape
that makes one screen work and its neighbour fail. Worth closing regardless of the decision below.

---

## 6. The premise behind FR-C01 is inverted for this endpoint

The POML argues: *"beta schema drift is the standing generator of the wrong-property-name defect class
this workstream is fixing."*

For `containers`, the measurement says the opposite: **v1.0 is the version missing properties the
application needs.** The rationale holds for container types (where the correct v1.0 names
`itemMajorVersionLimit` / `maxStoragePerContainerInBytes` are the fix per spec §4.1) but not here.

---

## 7. Options

| | Action | Cost |
|---|---|---|
| **A — targeted (recommended)** | Keep the shared config client on beta. Delete dead site #3. Fix the 011 version split so container types are consistently v1.0. Document beta as deliberate for containers, with the 400 above as the reason. | Task 020's literal acceptance criterion 1 ("no `/beta` except container-type CREATE") is **not** met — a second documented exception is added. Needs sign-off. |
| **B — literal** | Flip #1 and #2 to v1.0. | **Kills task 024** — storage silently unavailable, so FR-C06 collapses to the removal branch. Also loses `ownershipType`. Trades real data for version tidiness. |
| **C — split client** | v1.0 client for GA'd operations, beta client for containers LIST. | Two clients per config; caching and nextLink handling must stay in sync per operation. Highest complexity, and site #1's nextLink must track whichever client made the call. |

**Recommended: A.** The migration's purpose is to stop the app reading wrong/missing properties. On this
endpoint, v1.0 *causes* that rather than fixing it. Option B would satisfy the letter of FR-C01 while
working against the workstream's actual goal.

---

## 8. What was NOT done

No code changed. No migration performed. Stopped at the trigger per POML `mode="directional"` +
CLAUDE.md §6 rather than picking a branch that silently forecloses task 024.
