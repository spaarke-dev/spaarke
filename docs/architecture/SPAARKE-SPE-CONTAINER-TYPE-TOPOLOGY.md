# Spaarke SPE Container-Type Topology — how to create container types and containers

> **Created**: 2026-08-28 by `sdap-SPE-admin-app-r2` (UAT round 2 follow-up)
> **Status**: Authoritative for container-type topology decisions.
> **Audience**: anyone standing up a Spaarke environment, or adding an SPE surface.
>
> **Read this BEFORE creating a container type.** Standard container types **cannot be deleted**, and
> the owning app and billing model are **permanent**. There is no undo, and you get 25 per tenant.

---

## 0. TL;DR

| Question | Answer |
|---|---|
| Where does a container type live? | **Always in the owning (Spaarke) tenant.** Customers never create one |
| Can one container type serve many customers? | **Yes** — in other tenants via *registration*. This is the scaling mechanism |
| Do Model 2 customers consume our 25? | **No.** One container type serves all of them |
| Can one app own several container types? | **No — 1:1, permanent.** Each new type needs its own app registration |
| Which billing classification? | Containers in **our** tenant → `standard`. Containers in the **customer's** tenant → `directToCustomer` |
| Can we delete a mistake? | **Only trial types.** Standard types are permanent |

---

## 1. The five rules that constrain everything

Each rule is sourced. "VERIFIED" means this repo tested it against a live tenant.

| # | Rule | Source |
|---|---|---|
| **R1** | **One owning app ↔ one container type, permanently.** *"SharePoint Embedded requires a one-to-one relationship between one owning application and one container type."* The container type ID and owning application ID **can't be updated later** | [Learn: Create and configure a container type](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/create-container-type) |
| **R2** | **25 container types per tenant.** One may be trial; the rest are standard | Learn, same page |
| **R3** | **Standard container types cannot be deleted.** Only trial types can. Every standard type you create permanently consumes one of the 25 | Learn, same page |
| **R4** | **One container type registers into many consuming tenants.** This is how a multitenant ISV scales — not by duplicating types | Learn, "Link to multitenant onboarding" |
| **R5** | **Container-type CREATE is delegated-only.** Application permission is **"Not supported"**. An app-only token receives `403 accessDenied` — **VERIFIED 2026-08-21** (task 010) and again **2026-08-28** | [Learn: List containerTypes](https://learn.microsoft.com/en-us/graph/api/filestorage-list-containertypes) + `notes/probe_containertype_create.py` |

### Consequences people get wrong

- **R1 + R2** ⇒ 25 container types means **25 app registrations**. Plan registrations alongside types.
- **R3** ⇒ never create a container type "to see if it works". It is permanent.
- **R4** ⇒ do **not** create one container type per customer. That caps you at 25 customers for no benefit.
- **R5** ⇒ any automation that creates a container type with `client_credentials` is broken by construction. See §7.

---

## 2. Choose the billing classification — this is permanent

Graph's enum is `standard · trial · directToCustomer · unknownFutureValue`
(beta CSDL, `fileStorageContainerBillingClassification`). **"premium" does not exist**, despite having
appeared in this repo's own validation until 2026-08-28.

| Classification | Metering | Who pays | Use when |
|---|---|---|---|
| `standard` | Pay-as-you-go via an Azure billing profile on **our** subscription | **Spaarke** | Containers live in **our** tenant |
| `directToCustomer` (pass-through) | Pay-as-you-go activated by the customer in **their** M365 admin center | **The customer** | Containers live in the **customer's** tenant |
| `trial` | None — free | Nobody | Local proof-of-concept only. See the trap below |

> 📛 **Historical note.** `standard` used to be called **PAYGO** by Microsoft, which is why existing
> Spaarke container types are named `… PAYGO`. Both `standard` and `directToCustomer` are
> pay-as-you-go; **PAYGO does not identify which one**. Prefer names that state the *payer*.

### 🔴 The `trial` trap

`trial` is a developer sandbox, **not** a mechanism for customer trials:

- **5 containers maximum**, including the recycle bin
- **1 GB** per container
- **Expires after 30 days**, and access to existing containers is then removed
- **Cannot be registered in any other consuming tenant**

A "customer trial" environment whose containers live in the Spaarke tenant is `standard` — we are
paying for it either way. Naming an environment "trial" is fine; setting the *classification* to
`trial` is what breaks.

**You cannot convert.** Trial → standard is impossible; standard → pass-through is impossible. A wrong
choice means creating a replacement, and (R3) the mistake stays on the books forever.

---

## 3. Spaarke's topology

| Purpose | Container type | Owning app | Classification | Containers live in | Serves |
|---|---|---|---|---|---|
| Development | `Spaarke PAYGO 1` (existing, `8a6ce34c…`) | `170c98e1…` | as-is | Spaarke | internal |
| Customer trials | `Spaarke Trial 1` | **new** | `standard` | Spaarke | 1 container per prospect |
| Model 1 (shared, hosted by us) | `Spaarke Model 1` | **new** | `standard` | Spaarke | 1 container per customer |
| Model 2 (dedicated / customer-tenant) | `Spaarke Model 2` | **new** | `directToCustomer` | **Customer tenants** | **ALL** Model 2 customers |

Four of twenty-five. **Model 2 scales to unlimited customers** through registration (R4).

### Why trials and Model 1 are separate types

Settings are **container-type-scoped** — including `maxStoragePerContainerInBytes`. Sharing one type
would force prospects and paying customers onto the same storage cap and sharing policy, and a
settings change for one would hit the other. Settings changes take **up to 24 hours** to replicate.

### The Model 1 / Model 2 asymmetry — know this before promising anything

- **Model 2** customers each have their **own consuming tenant**, so each can hold **setting
  overrides**, and those overrides *survive* our updates.
- **Model 1** customers all sit in **our single tenant** — one consuming tenant, therefore **one
  settings baseline with no per-customer divergence.**

If a Model 1 customer needs different sharing or retention behaviour, the answer is *"move to Model
2"*, not *"apply an override"*. There is nowhere to put the override.

---

## 3A. App registration topology

### The two roles — conflating them is the trap

| Role | Cardinality | Mutable? | Purpose |
|---|---|---|---|
| **Owning app** | **1:1 with the container type** | ❌ **Permanent** | Satisfies R1. Holds full access by default. For Model 2, this is the identity consuming tenants grant admin consent to |
| **Registered app** | **N per registration** | ✅ Grant / revoke | Actually does the work — creates containers, reads and writes files. Needs no ownership |

`scripts/Create-NewContainerType.ps1` only ever registers the owning app, which makes these look like
one thing. They are not. Ownership is immutable and capped; **access is an ordinary, revocable grant**.

### 🔴 The BFF app registration MUST be separate from the owning app

This is the single most consequential decision on this page.

**If the BFF app registration is also the owning app, container-type cardinality infects the BFF —
and runs backwards into the 25-cap.** Every Model 2 customer needing its own BFF identity would then
need **its own container type**, hitting the 25-customer wall permanently, with no way to reclaim a
slot (R3).

Separating them is what makes Model 2 scale: **customer growth costs app registrations — free and
unlimited — instead of container types, which are capped and undeletable.**

Three further reasons, all pointing the same way:

- **Immutability.** The owning-app binding can never be changed (R1). Merged, the BFF app registration
  could never be rotated or retired — it would be welded to a container type that also cannot be
  deleted. Compromise, tenant migration, or a rebrand all become unsolvable.
- **Consent surface.** Model 2 customers consent to the *owning* app. Merged, they are consenting to
  something that also carries the BFF's API scopes, redirect URIs, and Dataverse/Mail permissions.
- **Auth v4.** The BFF identity is deliberately secret-free ([ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) A4).
  Owning apps are exception **E-1** and may carry secrets. Merging drags that exception back onto the
  identity auth-v4 worked to clean.

> ⚠️ **The existing app is the merged shape.** `170c98e1…` is named **`SDAP-PCF-CLIENT`** while being
> the container type's owning app. That is the artifact to unwind, not the pattern to extend — and
> because the binding is permanent, it is also a permanent misnaming.

### BFF instances vs BFF app registrations

They are different things and need not match:

- **BFF instance** = a deployed App Service. **Necessarily per-environment** — that is what a dedicated
  environment means.
- **BFF app registration** = an Entra identity. Several instances *can* share one.

For **Model 2, do not share one registration across customers.** Isolation is Model 2's premise, and a
shared registration means a shared token audience — a token minted for Customer A's BFF is
structurally valid at Customer B's.

### The registration set

| # | App registration | Purpose | Cardinality |
|---|---|---|---|
| 1 | `Spaarke SPE Trial 1 Owner` | Owns container type `Spaarke Trial 1` | 1 — fixed by R1 |
| 2 | `Spaarke SPE Model 1 Owner` | Owns `Spaarke Model 1` | 1 — fixed by R1 |
| 3 | `Spaarke SPE Model 2 Owner` | Owns `Spaarke Model 2`. **Multi-tenant** — customers consent to this | 1 — fixed by R1 |
| 4 | `Spaarke BFF — Trial 1` | BFF identity, trial environment | 1 |
| 5 | `Spaarke BFF — Model 1` | BFF identity, shared Model 1 environment | 1 |
| 6…n | `Spaarke BFF — {Customer}` | BFF identity per Model 2 customer | **1 per customer** |

Ten Model 2 customers ⇒ 15 app registrations and still **4 of 25 container types**. That ratio is the
point of the split.

### How a BFF gets container access without owning anything — VERIFIED

**Permission grants are per consuming tenant, not global to the container type.** Confirmed against the
Graph beta CSDL 2026-08-30:

```
fileStorageContainerTypeRegistration
  ├─ owningAppId, billingClassification, registeredDateTime
  ├─ settings                       ← the consuming-tenant OVERRIDE surface
  └─ applicationPermissionGrants    → Collection(fileStorageContainerTypeAppPermissionGrant)
                                        └─ keyed by appId
                                           ├─ applicationPermissions   (app-only)
                                           └─ delegatedPermissions
```

The grants hang off the **registration** — the container type *as registered in one tenant* — and are
keyed by `appId`. So each Model 2 customer's registration carries **its own** grants, listing only that
customer's BFF app. One customer's grant list is invisible to and independent of another's.

This also confirms the Model 1 / Model 2 asymmetry in §3: `fileStorageContainerTypeRegistration.settings`
is a distinct property from `fileStorageContainerType.settings` — that is the per-consuming-tenant
override surface, which Model 1 (single tenant, single registration) does not get.

Grant the BFF app what it needs on the relevant registration; **do not make it an owner.**

---

## 4. How to create a container type

### Prerequisites

- A Microsoft 365 tenant with SharePoint active
- **A NEW Entra app registration** for this container type (R1 — it cannot be shared)
  - `FileStorageContainer.Selected` (application)
  - `FileStorageContainerTypeReg.Selected` (application) — required to register on consuming tenants
  - `signInAudience = AzureADMultipleOrgs` **if it will ever serve a consuming tenant** (Model 2)
- A **non-guest member account in the owning tenant**. No admin role is required — the Graph create
  API is callable by any non-guest owning-tenant user, who is auto-assigned as an owner
- For `standard`: an Azure subscription + resource group, with owner/contributor to attach billing

### The call

```http
POST https://graph.microsoft.com/beta/storage/fileStorage/containerTypes
Content-Type: application/json

{
  "name": "Spaarke Model 1",
  "owningAppId": "{app registration client id}",
  "billingClassification": "standard"
}
```

**All three fields are required.** `owningAppId` is `Nullable="false"` in the CSDL; omitting it yields
`400 invalidRequest: One of the provided arguments is not acceptable` — which does **not** name the
missing field. That was a live defect in this repo until 2026-08-28.

> 🔴 **The token MUST be delegated (R5).** A `client_credentials` (app-only) token returns
> `403 accessDenied` no matter what roles it holds — Application permission is "Not supported" on
> this endpoint. Use the SPE Admin app (which performs the delegated exchange), the SharePoint
> Embedded VS Code extension, or the SharePoint admin center.

### After creation

1. **Record the container type ID** — it is immutable and needed everywhere.
2. **`standard` only** — attach the Azure billing profile. If billing setup fails with
   `SubscriptionNotRegistered`, wait a few minutes and retry; the `Microsoft.Syntex` resource
   provider registration is slow.
3. **`directToCustomer` only** — the consuming tenant admin must activate pay-as-you-go in the M365
   admin center (**Setup → Billing and licenses → Activate pay-as-you-go services**, then
   **Apps → SharePoint Embedded**) **before** the app can be used.

---

## 5. How to onboard a consuming tenant (Model 2)

Per customer, once:

1. **Create the container type in the owning tenant** — already done; the *same* type serves every
   Model 2 customer (R4). Do not create a new one.
2. **Customer admin grants admin consent** to the owning app (this is why it must be multi-tenant).
3. **Register container-type application permissions** in the consuming tenant —
   `POST /storage/fileStorage/containerTypeRegistrations`. Spaarke exposes this as
   `POST /api/spe/containertypes/{typeId}/register` and the `/consumers` routes.
4. **Configure pass-through billing** — the customer activates pay-as-you-go (§4).
5. **Validate** container creation and access.

---

## 6. How to create containers

Containers are cheap, deletable, and **not** the scarce resource — create them freely.

```http
POST https://graph.microsoft.com/beta/storage/fileStorage/containers
{ "displayName": "Acme Corp", "description": "...", "containerTypeId": "{id}" }

POST /beta/storage/fileStorage/containers/{id}/activate
```

A container is **not usable until activated**. One container per customer (trials, Model 1) or per
customer tenant (Model 2).

**Deleting** is two steps — soft-delete then purge from the deleted collection:

```http
DELETE /beta/storage/fileStorage/containers/{id}
DELETE /beta/storage/fileStorage/deletedContainers/{id}
```

Both are required. A container left in the deleted collection still counts against trial caps, and
a container type cannot be deleted while any container of it exists anywhere.

### Known limits

| Limit | Value |
|---|---|
| Containers per **trial** container type | **5**, including the recycle bin |
| Storage per container (trial) | 1 GB |
| Storage per container (standard) | `maxStoragePerContainerInBytes`, set **on the container type** — type-wide, not per container |
| Containers per **standard** container type | ⚠️ **UNDOCUMENTED** — see §8 |

---

## 7. 🔴 Known defect — `scripts/Create-NewContainerType.ps1` cannot work

**Found 2026-08-28.** The script acquires an **app-only** token
(`grant_type = client_credentials`, `scope = https://graph.microsoft.com/.default`, line 46) and calls
**`POST https://graph.microsoft.com/beta/storage/fileStorage/containerTypes`** (line 76).

That combination is refused by design (R5). Probed twice on 2026-08-28: `403 accessDenied — Caller
does not have required permissions for this API`, with the permission granted and admin-consented.

The likely origin is [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md)
§13.3, which prescribes a *"confidential-client (app-only) token"* as the fix for a
`403 public client not allowed`. **"Confidential client" and "app-only" are not the same thing** — the
correct fix for a public-client rejection is a *confidential client performing a **delegated**
exchange* (auth-code or OBO), not `client_credentials`.

The script's **registration** step (line 141, `_api/v2.1/storageContainerTypes/.../applicationPermissions`
against the SharePoint domain) is a different API and is **not** implicated.

**Until this is fixed, create container types through the SPE Admin app, the VS Code extension, or the
SharePoint admin center** — all of which use a delegated identity. Handler **H8** of the provisioning
flow inherits this defect and should be treated as unproven.

---

## 8. Open questions

| # | Question | Why it matters | Status |
|---|---|---|---|
| 1 | **How many containers can one *standard* container type hold?** | If Model 1 holds one container per customer, this is the ceiling on Model 1 customers. Only the trial cap of 5 is published | ⚠️ **UNDOCUMENTED** — confirm with Microsoft before it becomes load-bearing |
| 2 | Does the create-role documentation conflict still stand? | Learn's Graph reference and its conceptual doc disagree on whether an admin role is needed to create | Open — see [`knowledge/sharepoint-embedded/docs/learn-containertypes.md`](../../knowledge/sharepoint-embedded/docs/learn-containertypes.md) |
| 3 | Is `scripts/Create-NewContainerType.ps1` used anywhere that currently succeeds? | If H8 has ever worked, our understanding of R5 is incomplete | Open — §7 |
| 4 | ~~Are `applicationPermissions` scoped per consuming tenant, or global to the container type?~~ | Decides whether Model 2 customers' BFF apps are isolated from each other | ✅ **RESOLVED 2026-08-30** — per consuming tenant. Grants hang off `fileStorageContainerTypeRegistration`, not the container type (§3A) |

### For `customer-provisioning-orchestration-r1`

Three items in this document bear directly on that project and should be reconciled before its
provisioning flow is treated as correct:

1. **Handler H8 is unproven** — it inherits the `Create-NewContainerType.ps1` defect (§7). Container-type
   creation cannot be automated with an app-only token.
2. **The app registration set (§3A) is a provisioning input**, not an afterthought. Each Model 2 customer
   needs its own BFF app registration and a grant on that customer's container-type registration —
   **not** a new container type.
3. **`sprk_containertypeid` on the environment registry** now has a defined meaning per model: shared
   across all customers of a model, not allocated per customer.

---

## 9. Sources

- [Create and configure a container type — Microsoft Learn](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/create-container-type) (fetched 2026-08-28)
- [`knowledge/sharepoint-embedded/docs/learn-containertypes.md`](../../knowledge/sharepoint-embedded/docs/learn-containertypes.md) — curated snapshot + project findings
- Graph beta CSDL — `fileStorageContainerType`, `fileStorageContainerBillingClassification`
- `projects/sdap-SPE-admin-app-r2/notes/probe_containertype_create.py` — the app-only 403 evidence
- `projects/sdap-SPE-admin-app-r2/notes/obo-spike-findings.md` — task 010, the original delegated-only finding
