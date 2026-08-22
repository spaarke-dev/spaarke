# Tenant isolation in the SPE Admin app — the gap, and the shape that closes it

> Raised by the operator 2026-08-22 while resolving the task-010 auth decision. Findings are from code
> inspection plus the live Entra/Graph evidence in [`obo-spike-findings.md`](obo-spike-findings.md).
> **This is a design note, not a fix.** Nothing here has been implemented.

---

## The question

Deployment **Model 1** puts multiple customers in one Spaarke environment sharing a **single
`bff.api` app registration**, each customer with their own container type / container ids. **Model 2**
is a dedicated customer (dedicated Spaarke environment or the customer's own tenant), also with a
single `bff.api` registration.

> In Model 1, how is access controlled? Can the container-type / container-id dropdowns be limited by
> business unit, so a user sees only their assigned BU?

**Short answer: yes, business unit is the right mechanism, the column already exists — but the
dropdown is the last place it should be applied, and the enforcement it depends on does not exist yet.**

---

## Where the boundary lives — the actual issue

| Model | Who enforces "customer A cannot see customer B" |
|---|---|
| **Per-customer owning apps** (what `SpeAdminTokenProvider` was built for) | **Entra.** Customer A's app registration is granted access to customer A's container type only. A bug in Spaarke code cannot cross the line — Graph refuses. |
| **One shared BFF app** (what actually exists) | **Spaarke's own code.** The BFF app holds `FileStorageContainer.Selected` + `FileStorageContainerType.Manage.All` and can reach every container type it is registered against. Graph has no concept of which customer a request is "for". |

Using the BFF identity does not *create* a leak. It **moves the boundary out of Entra and into our
code** — where, today, it is missing.

---

## What exists today

| Piece | State |
|---|---|
| `sprk_specontainertypeconfig.sprk_businessunit` | ✅ column exists, populated on create/update |
| `GET /api/spe/config?businessUnitId=…` | ⚠️ **caller-supplied query parameter** (`ConfigEndpoints.cs:106,118-119`). Omit it → every customer's configs. Supply someone else's → their configs. |
| `SpeAdminAuthorizationFilter` | ⚠️ binary `Admin` / `SystemAdmin` role check. No BU dimension. |
| The other **15** endpoint files taking `configId` | ❌ **no ownership check whatsoever** |
| `SpeAdminGraphService.ResolveConfigAsync` | ❌ resolves a config by id with no identity check |
| BFF's Dataverse reads (`DataverseWebApiClient`) | ❌ **app-only credential** — Dataverse's own security trimming never applies |

### The consequence, stated plainly

**`configId` is currently a bearer capability.** Any user who clears the binary Admin check can pass any
customer's `configId` to `/api/spe/containers`, `/files`, `/permissions`, `/audit`, `/search/*` and
receive that customer's data.

- **Model 2** — harmless. One customer; there is no boundary to cross.
- **Model 1** — **cross-customer data exposure**, and the only thing preventing it today is that
  `configId` values are not published.

### Why the Dataverse row matters more than it looks

If the app read Dataverse **as the user** (host-context `Xrm.WebApi`), BU-scoped security roles would
trim `sprk_specontainertypeconfig` automatically — isolation for free, enforced by the platform. The
BFF reads **as an application**, so every row is visible to it and the filtering has to be written by
hand. That is the trade the BFF-centric design already made; this note is the bill for it.

---

## The shape that closes it

**1 — Derive the caller's business unit; never accept it.**
Resolve `systemuser.businessunitid` for the authenticated user server-side. This single change turns
`businessUnitId` from a *filter the client asks for* into a *boundary the server imposes*.

**2 — Enforce once, in an endpoint filter on the `/api/spe` group** (ADR-008 — the pattern is already
in place via `SpeAdminAuthorizationFilter`). Resolve `configId` → config → `sprk_businessunit`, compare
to the caller's BU, reject on mismatch. One place covers all 15 endpoints and cannot be forgotten when
endpoint 16 is added. Enforcing per-endpoint instead guarantees an eventual miss.

**3 — Reject with 404, not 403.** "That config exists but is not yours" confirms another customer
exists. Absence is the safer answer.

**4 — The dropdown then scopes itself,** because the list endpoint only ever returns reachable configs.
No client change needed, and no way to bypass it by calling the API directly.

**5 — Decide the BU hierarchy question.** Dataverse business units are a tree. A Spaarke operator
supporting several customers plausibly needs "parent BU sees descendants"; a customer's own admin
does not. This is a product decision, not a technical one.

**6 — Scope the audit trail the same way.** `sprk_speauditlog` already carries `sprk_businessunit`
(task 005 wired the write path). The read path in `AuditLogEndpoints` must apply the same derived-BU
filter, or the audit screen leaks across customers even when the data screens do not.

---

## Why this is worth doing regardless of the identity decision

Even with per-customer owning apps, Model 1 still needs the dropdown scoped, the audit trail scoped,
and the config list scoped — Entra would prevent the *Graph* call from succeeding, but Spaarke's own
Dataverse config rows are not protected by Entra at all. **The BU enforcement work is required for
Model 1 either way.** Choosing the BFF identity does not add this requirement; it only removes the
Entra safety net that would have caught a mistake in it.

---

## Recommendation

Treat this as a **hard prerequisite for Model 1 go-live**, and as **not required** for Model 2.

Sizing is modest — one BU-resolution helper, one endpoint filter, a change to `ConfigEndpoints` to stop
trusting the query parameter, and the same filter applied to the audit read path. The work is small;
the consequence of skipping it is not.

Suggested home: a new task in Workstream B (it is an authorization concern and shares that
workstream's ADR context), gated behind the identity decision so both land together.
