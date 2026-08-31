# Live verification against Spaarke Dev — 2026-08-24

> Delegated token via device-code as `ralph.schroeder@spaarke.com` through
> `SPAARKE-SPE-Admin-CLI` (`68cf5a14-1efb-4254-80bf-2761ffc89373`), scopes
> `FileStorageContainerType.Manage.All` + `FileStorageContainer.Selected`.
> App-only token as the owning app `170c98e1`. **No secret or token value appears in this file.**

---

## 1. ✅ Task 030 — every field the fix added is really returned

```
GET /beta/storage/fileStorage/containerTypes   →  200, 4 container types
```

| name | owningAppId | billing | expirationDateTime |
|---|---|---|---|
| Spaarke DMS-SPE Trial | `2c708318-…` | **trial** | 🔴 **2025-10-10** |
| Spaarke DMS Dev 1 | `fd1325aa-…` | directToCustomer | absent |
| Spaarke PAYGO 1 | `170c98e1-…` | standard | absent |
| Spaarke Demo Documents | `da03fe1a-…` | standard | absent |

**`owningAppId` is returned for all four**, and all four differ — so the grid's blank "Owning App"
column was hiding the single field that distinguishes one container type from another. Task 030's fix
is confirmed necessary, not speculative.

### 🔴 The trial expired eleven months ago

`Spaarke DMS-SPE Trial` expired **2025-10-10**. `expirationDateTime` was being returned by Graph the
whole time — the BFF simply never mapped it, so the UI could not have said so. This is exactly the
30-day trap task 030's creation-flow warning was written for, sitting in the live tenant as a worked
example.

### Quota reasoning holds

1 trial + 3 non-trial. `assessTrialQuota` sees a visible trial and blocks a second — correct, and the
blocking direction is provable from the data, which is the asymmetry option A relied on.

---

## 2. ✅ Task 023 — the shape fix is confirmed correct

Live settings on `Spaarke PAYGO 1`:

```json
"settings": {
  "urlTemplate": "https://localhost", "isDiscoverabilityEnabled": false,
  "isSearchEnabled": true, "isItemVersioningEnabled": true,
  "itemMajorVersionLimit": 500, "maxStoragePerContainerInBytes": 27487790694400,
  "isSharingRestricted": false, "isOfficeRestricted": false,
  "consumingTenantOverridables": "sharingCapability,itemMajorVersionLimit,isOfficeRestricted"
}
```

Every conclusion task 023 reached from SDK reflection is confirmed by the live resource:

- settings **are a nested object**, not top-level members ✅
- the names are `itemMajorVersionLimit` and `maxStoragePerContainerInBytes` ✅
- versioning is `isItemVersioningEnabled`, not `isVersioningEnabled` ✅

So the pre-fix code could not have written anything, for exactly the reason recorded.

### 🔔 But AC-2 (write → read back) CANNOT be met — escalation

**Every PATCH is rejected**, with `400 invalidRequest / badArgument`:

| Attempt | Result |
|---|---|
| nested `{"settings":{"itemMajorVersionLimit":499}}` | 400 |
| nested with the **current** value (500) — a no-op | 400 |
| nested boolean (`isSearchEnabled`), nested string (`urlTemplate`) | 400 |
| full settings blob, unchanged | 400 |
| bare `{"name":"Spaarke PAYGO 1"}` | 400 |
| v1.0 instead of beta · `PUT` instead of `PATCH` · `@odata.type` · `If-Match: etag` | 400 |
| **app-only as the owning app** `170c98e1` | **403** accessDenied (GET *and* PATCH) |

The body is not the problem — a bare `name` PATCH fails identically.

**Most likely cause, NOT yet proven**: only the **owning application** may modify its container type.
Microsoft's own wording is that a container type is "strongly coupled with one SharePoint Embedded
application", and the docs describe updates as performed by the owning app. Our delegated token belongs
to `68cf5a14`, which owns nothing; the type is owned by `170c98e1`.

If that is the rule, the settings write needs **delegated-as-the-owning-app** — precisely the exchange
task 010 proved unworkable (`AADSTS500011`, and OBO requires the assertion's audience to be the
exchanging client). That would mean **task 023's write path is unreachable under the current app
topology**, and the ADR-028 §6.5 gate needs re-running rather than assumed closed.

**Note the POML's escalation trigger did not literally fire** — it names "PATCH returns 200 but the
value did not persist". This is a 400. The situation is escalation-worthy for a different reason: not
replication lag versus rejection, but *no write path at all*.

### The decisive next test, and its cost

Create a **trial** container type owned by `68cf5a14` and PATCH that. If it succeeds, ownership is the
rule and the finding is confirmed. Costs: the tenant already holds one trial (expired), and the limit
is **one at a time** — so this likely requires deleting `Spaarke DMS-SPE Trial` first, which is
irreversible and needs its containers permanently deleted. **Operator decision required.**

A cheaper alternative: enable public-client flows on `170c98e1` long enough to take one delegated token
as the owning app. That modifies the production SPA registration, so it also wants sign-off.

---

## 3. Findings for downstream tasks

| For | Finding |
|---|---|
| **025** | Graph returns **`isOfficeRestricted`**, which is **not typed** on the SDK's `FileStorageContainerTypeSettings` — so the "nine properties" is really nine typed + one untyped. Conversely **`sharingCapability` is NOT returned** in the settings payload at all, though it is settable. Both facts change FR-C07's surface. |
| **026** | `consumingTenantOverridables` is a **comma-delimited string** (`"sharingCapability,itemMajorVersionLimit,isOfficeRestricted"`), not an array or enum flags. |
| **011 / 023** | Container types are delegated-only for **write as well as read** — app-only is 403 on GET *and* PATCH. |
| **051** | `maxStoragePerContainerInBytes` is **27,487,790,694,400** (25 TiB) on the two standard types and **209,715,200** (200 MiB) on the trial. Real ceilings exist to surface. |

---

## 4. What is now verified vs still open

| Task | Status |
|---|---|
| **022** | ✅ `deletedContainers` → 200, no OData error. Timestamp mapping still WireMock-only (recycle bin is empty). |
| **024** | ✅ 5/5 containers report storage, 861 MB total, vs `0 B` displayed. GET confirmed to omit it. |
| **030** | ✅ `owningAppId` + `expirationDateTime` confirmed returned; an expired trial exists in the tenant. |
| **023** | ⚠️ Read/shape confirmed. **Write blocked — escalation open.** |
| **UI** | ❌ Still unverified anywhere. Needs the local harness or a deployment. |
