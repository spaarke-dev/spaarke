# Task 051 — per-container quota ceiling: the escalation, and what is actually available

> **2026-08-27** · Spec FR-E02 · Measured against Graph CSDL (both versions, no token) and live against
> Spaarke Dev on a **throwaway container** (created → activated → probed → torn down 204/204, NFR-07).
> **Status: 🔔 ESCALATED — the POML's `<escalation><trigger>` fired. No production code written.**

---

## 1. The trigger fired: the ceiling is container-TYPE scope only

The POML's trigger:

> *"If `maxStoragePerContainerInBytes` proves settable only at container-TYPE scope rather than per
> container, STOP and escalate — spec FR-E02 promises a per-container ceiling, and delivering a
> type-wide setting instead is a different capability with different customer semantics."*

**It is type-scope only.** Confirmed two independent ways.

### CSDL — the property is not on the container at all

| Complex type | Properties |
|---|---|
| **`fileStorageContainerSettings`** (the CONTAINER's `settings`) | `isItemVersioningEnabled`, `isOcrEnabled`, `itemMajorVersionLimit` (+ `itemDefaultSensitivityLabelId` on beta) — **no storage property** |
| **`fileStorageContainerTypeSettings`** (the TYPE's `settings`) | …, **`maxStoragePerContainerInBytes`**, … |

Identical on v1.0 and beta. There is also **no bound action** on `fileStorageContainer` that sets a
quota — the full action list is `restore`, `activate`, `permanentDelete`, `archive`, `unarchive`,
`lock`, `unlock`, `provisionMigrationContainers`, `transferPrincipalOwnership`, `getByUser`.

### 🔴 Live — and Graph answers 200 while discarding the write

```
PATCH /beta/storage/fileStorage/containers/{id}
      {"settings":{"maxStoragePerContainerInBytes": 10737418240}}   → 200 OK
PATCH /beta/storage/fileStorage/containers/{id}
      {"maxStoragePerContainerInBytes": 10737418240}                → 200 OK

GET   /beta/storage/fileStorage/containers/{id}
      settings = {"isOcrEnabled":false,"itemMajorVersionLimit":500,
                  "isItemVersioningEnabled":true,"itemDefaultSensitivityLabelId":""}
```

**Both writes accepted. Neither persisted. No error, no warning, no echo.**

This is the project's signature defect arriving from the platform — the same shape as task 028's
`$expand=drive` (accepted, 200, silently dropped from every row) and task 050's `$select=status`
(accepted, 200, omitted). A naive FR-E02 implementation would PATCH, receive 200, and report
*"Storage limit set to 10 GB."* Nothing would have happened.

**The POML's constraint is what caught this**, and it deserves to be called out:

> *"Scope: verification. Every ceiling write MUST be confirmed by read-back, not by a 200 response."*

Had that constraint not been written, the obvious implementation would have shipped and looked correct.

---

## 2. What IS available per container — and it is genuinely useful

`GET /beta/storage/fileStorage/containers/{id}/drive` returns a populated `quota` facet:

```json
"quota": { "total": 27487790694400, "used": 40488,
           "remaining": 27487790653912, "deleted": 0, "state": "normal" }
```

Two things follow.

### 2a. The ceiling IS enforced per container — it is just set once, on the type

`total` = **27,487,790,694,400** bytes = 25 TiB = **exactly** the container type's
`maxStoragePerContainerInBytes` as measured live by task 023 on `Spaarke PAYGO 1`.

So the semantics are: *"maximum storage **per container**"* — a per-container ceiling whose **value is
authored once at the type level and applies uniformly to every container of that type**. It is a real
per-container limit; what does not exist is a *per-container-differentiated* limit.

That distinction is the whole of FR-E02's customer promise. The spec's phrasing — *"gives legal
customers per-matter storage caps"* — implies matter A can have 10 GB while matter B has 100 GB.
**Graph cannot express that.** One value covers every container of the type.

### 2b. 🔑 `drive.quota.used` closes a gap tasks 020/024 left open

Tasks 020 and 024 established that `storageUsedInBytes` is **beta-only AND LIST-only** — absent from
`GET /containers/{id}` even with an explicit `$select`. That is why `ContainerDto.StorageUsedInBytes`
is documented as "populated on list, null on detail".

`drive.quota.used` is **available on the per-container GET** and reported the correct figure (40,488
bytes on a brand-new container with an empty document library). It is a second, independent source of
consumption for the single-container view, on a surface Graph actually serves.

This was not known when tasks 020/024 ran. It is a real improvement available regardless of how the
FR-E02 question is resolved.

---

## 3. The decision — options, with what each actually delivers

| | What ships | Honest label | FR-E02 as written? |
|---|---|---|---|
| **A. Type-scope ceiling + per-container quota reporting** | Read/write `maxStoragePerContainerInBytes` on the container TYPE (one knob, all containers), **plus** per-container `total`/`used`/`remaining` from `drive.quota` | *"Storage limit per container (applies to all containers of this type)"* | ❌ Not per-matter differentiated |
| **B. Read-only quota surface only** | Per-container `total`/`used`/`remaining`/`state` from `drive.quota`. No write. | *"Storage limit"* + *"Used"* + *"Remaining"* | ❌ No control at all |
| **C. Drop FR-E02** | Nothing | — | Withdrawn |

**Recommendation: A.**

- It delivers the only storage *control* Graph offers, in the only place Graph accepts it.
- It delivers the per-container *reporting* the spec's UI acceptance criteria describe
  (*"presented distinctly from any usage display"* — `total` vs `used` is exactly that distinction,
  and it comes from one call).
- It picks up §2b for free, fixing the detail-view storage gap.
- The one thing it cannot do — different caps for different matters — is a **platform limit**, and the
  honest move is to label it plainly rather than build a per-container field that silently writes
  nowhere.

⚠️ **Whatever is chosen, the write path must read back.** A 200 from Graph on a container-scope quota
PATCH is meaningless, as measured above. If option A is taken, the type-scope PATCH must be verified by
read-back too — task 023 already established that the container-TYPE settings PATCH does persist, so
that path is sound, but the verification belongs in the code regardless.

⚠️ **Do not "fix" this by adding a per-container ceiling field.** It will accept input, PATCH
successfully, return 200, and change nothing. Verified 2026-08-27.

---

## 4. What was built (option A, chosen by the operator 2026-08-27)

**Scope was smaller than the POML assumed.** Tasks 023/025 had already built the type-scope ceiling
read + write + validation end-to-end — `UpdateContainerTypeSettingsRequest.MaxStoragePerContainerInBytes`,
`ContainerTypeDtos`, the PATCH, a `> 0` validation, and a client field already labelled *"Storage ceiling
per container"*. So FR-E02's write half existed; what was missing was the verification, the
per-container read surface, and honest labelling.

| Layer | Change |
|---|---|
| `SpeAdminGraphService` | `SpeContainerQuota` record + `ReadContainerQuota(Drive?)`; `GET` expand widened to `drive($select=webUrl,quota)`; **write verification** comparing requested vs reported settings, raising `SettingsNotPersistedException` |
| `ContainerTypeSettingsEndpoints` | `SettingsNotPersistedException` → **502 Bad Gateway** with `unwrittenFields`. Not 500: the request was valid and authorized; the UPSTREAM service acknowledged and did not act |
| `ContainerEndpoints` | `ContainerQuotaDto` on the detail response, omitted when null |
| `ContainerDetail.tsx` | Storage Used (from `quota.used`), Storage Limit + *"Set on the container type — applies to every container of this type"*, Remaining, and "Held by deleted items" |
| `ContainerTypeSettingsForm.tsx` | A `warning` MessageBar on the ceiling field: *"This limit applies to every container of this type."* |
| Tests | `SpeAdminContainerQuotaContractTests` — 8 tests, incl. the silent-drop regression guard and two negative controls |

### Two design points worth keeping

**`remaining` is Graph's figure, never `total − used`.** The live facet also carries `deleted` (bytes
held by deleted items that still count), so a local subtraction disagrees with Graph whenever a recycle
bin is non-empty — and would look authoritative doing it. A contract test asserts the two differ.

**The verification warns rather than throws when the response carries no settings at all.** Throwing
there would assert a failure not established — the same species of dishonesty as reporting an
unverified success, pointing the other way. It throws on the shape actually measured: settings present,
requested field absent or different.

---

## 5. Cost of the probe

One throwaway container (`ZZ-Task051-QuotaProbe-…`), created and fully torn down (soft-delete 204 +
permanent-delete 204). No pre-existing container was touched. No production code was written pending
this decision.
