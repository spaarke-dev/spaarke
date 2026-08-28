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

---

## 9. Sources

- [Create and configure a container type — Microsoft Learn](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/create-container-type) (fetched 2026-08-28)
- [`knowledge/sharepoint-embedded/docs/learn-containertypes.md`](../../knowledge/sharepoint-embedded/docs/learn-containertypes.md) — curated snapshot + project findings
- Graph beta CSDL — `fileStorageContainerType`, `fileStorageContainerBillingClassification`
- `projects/sdap-SPE-admin-app-r2/notes/probe_containertype_create.py` — the app-only 403 evidence
- `projects/sdap-SPE-admin-app-r2/notes/obo-spike-findings.md` — task 010, the original delegated-only finding
