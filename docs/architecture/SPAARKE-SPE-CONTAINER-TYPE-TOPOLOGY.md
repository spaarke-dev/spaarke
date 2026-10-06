# Spaarke SPE Container-Type Topology — how to create container types and containers

> **Created**: 2026-08-28 by `sdap-SPE-admin-app-r2` (UAT round 2 follow-up)
> **Updated**: 2026-10-03 by `customer-provisioning-orchestration-r1` task 248 — owning-app credential is MI-FIC
> (no certificate); `Spaarke Model 1` recorded (§3, §3A).
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

> ⚠️ **Inventory correction (2026-10-03).** An earlier revision of this document implied
> `Spaarke PAYGO 1` was effectively the only container type. **It is not.** The SharePoint admin
> center → **SharePoint Embedded → Apps** list shows **four** SPE apps, and because app↔type is 1:1
> (R1) that is **four container types already in use**:
>
> | App | Billing type | ⇒ classification |
> |---|---|---|
> | Spaarke Demo Documents | Owner org | `standard` |
> | Spaarke PAYGO 1 (`8a6ce34c…`) | Owner org | `standard` |
> | Spaarke DMS Dev 1 | **User org** | `directToCustomer` |
> | Spaarke DMS-SPE Trial | **trial** | `trial` |
>
> **Two consequences.** (a) **4 of 25 slots are consumed** and standard types cannot be deleted (R3),
> so the remaining budget is 21, not 24. (b) 🔴 **The one permitted trial slot is ALREADY TAKEN** by
> `Spaarke DMS-SPE Trial` — a new `trial` type cannot be created until that one is deleted, including
> every container of it (trial types *are* deletable; standard ones are not).
>
> **Always read the live Apps list before planning a new type.** This inventory is a snapshot.

| Purpose | Container type | Owning app | Classification | Containers live in | Serves |
|---|---|---|---|---|---|
| Development | `Spaarke PAYGO 1` (existing, `8a6ce34c…`) | `170c98e1…` | as-is | Spaarke | internal |
| Customer trials | `Spaarke Trial 1` | **new** | `standard` | Spaarke | 1 container per prospect |
| Model 1 (customer's Azure subscription inside **Spaarke's** tenant) | `Spaarke Model 1` (**created 2026-10-03**, `fb3817a8…`) | `Spaarke SPE Model 1 Owner` (`bfac7f6e…`) | `standard` | Spaarke | 1 container per customer |
| Model 2 (customer's Azure subscription inside the **customer's own** tenant) | `Spaarke Model 2` | **new** | `directToCustomer` | **Customer tenants** | **ALL** Model 2 customers |

**Budget**: the four types in the inventory above already existed; `Spaarke Model 1` was created
2026-10-03 (**5 of 25**); adding `Spaarke Trial 1` and `Spaarke Model 2` brings it to **7 of 25**.
**Model 2 scales to unlimited customers** through registration (R4), so customer growth does not move
this number.

> 🟡 **Deployment-model note (2026-09-28, owner decision [D-12](../../projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md))**
> — Model 1 is **not** a shared tier. Every customer gets a **dedicated Dataverse environment and a
> dedicated set of Azure resources in their own Azure subscription**, in both models; the models differ in
> **which Azure tenant owns that subscription** (Model 1 = Spaarke's, Model 2 = the customer's). What
> follows in this section — the **single SPE consuming tenant** under Model 1 — is the one genuine
> infrastructural difference between the models beyond H0.5 consent and Azure Lighthouse delegation, and
> it is **unaffected by D-12**. Note the boundary: *one consuming tenant* is not *one container*; each
> Model 1 customer still gets **their own container**.

### Why trials and Model 1 are separate types

Settings are **container-type-scoped** — including `maxStoragePerContainerInBytes`. Sharing one type
would force prospects and paying customers onto the same storage cap and sharing policy, and a
settings change for one would hit the other. Settings changes take **up to 24 hours** to replicate.

### 🔴 The one real Model 1 / Model 2 asymmetry — know this before promising anything

This is an **SPE-tenancy** asymmetry, not an Azure-resource one. Azure resources are dedicated per
customer in both models.

- **Model 2** customers each have their **own SPE consuming tenant**, so each can hold **setting
  overrides**, and those overrides *survive* our updates.
- **Model 1** customers all sit in **Spaarke's single SPE consuming tenant** — one consuming tenant,
  therefore **one container-type settings baseline with no per-customer divergence.** Container-type
  settings (storage cap, sharing and retention behaviour) are scoped to the *registration*, and under
  Model 1 there is exactly one registration for all of them.

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
  Owning apps are exception **E-1** and may carry secrets (SPE Admin no longer uses any — §6B). Merging drags that
  exception back onto the identity auth-v4 worked to clean. Since task 248 the L2 control plane needs no owning-app
  secret or certificate either — see "Owning-app credential" below.

> ⚠️ **The existing app is the merged shape.** `170c98e1…` is named **`SDAP-PCF-CLIENT`** while being
> the container type's owning app. That is the artifact to unwind, not the pattern to extend — and
> because the binding is permanent, it is also a permanent misnaming.

### BFF instances vs BFF app registrations

They are different things and need not match:

- **BFF instance** = a deployed App Service. **Necessarily per-environment** — that is what a dedicated
  environment means.
- **BFF app registration** = an Entra identity. Several instances *can* share one.

🔴 **Do not share one BFF registration across customers — in EITHER model.** A shared registration means a
shared token audience: a token minted for Customer A's BFF is structurally valid at Customer B's. Under
**Model 1** this matters *more*, not less, because every Model 1 customer presents the **same `tenantId`**
(Spaarke's), so the token's tenant claim cannot tell them apart either.

### The registration set

| # | App registration | Purpose | Cardinality |
|---|---|---|---|
| 1 | `Spaarke SPE Trial 1 Owner` | Owns container type `Spaarke Trial 1` | 1 — fixed by R1 |
| 2 | `Spaarke SPE Model 1 Owner` | Owns `Spaarke Model 1` | 1 — fixed by R1 |
| 3 | `Spaarke SPE Model 2 Owner` | Owns `Spaarke Model 2`. **Multi-tenant** — customers consent to this | 1 — fixed by R1 |
| 4…n | `spaarke-bff-api-{customerId}` | BFF identity **per customer** — created by **H3** during the customer's run; **H8** grants it (and the stamp UAMI) on the container-type registration | **1 per customer** |

> 🟡 **Corrected 2026-10-06 (T227a).** Row 4 `Spaarke BFF — Trial 1` is removed: the shared trial tier is retired
> (D-12) and the script path that created it (`Register-EntraAppRegistrations.ps1 -CreateBffApp`) is deleted. The
> per-customer row now carries the name H3 actually creates (`spaarke-bff-api-{customerId}`, not
> `Spaarke BFF — {Customer}`). Rows 1 and 3 describe container types no stamp uses today (Model 2 is out of scope).

> 🟡 **Corrected 2026-09-28 (D-12).** This table previously carried a singleton row
> `Spaarke BFF — Model 1` — *"BFF identity, shared Model 1 environment"* — and scoped per-customer BFF
> identity to Model 2 only. **There is no shared Model 1 environment.** Each customer has their own BFF
> App Service in their own Azure subscription, so **per-customer BFF identity applies to both models**.
> The container-type *owning* apps (rows 1–3) are unaffected: they are 1:1 with a container type by R1,
> and Model 1's single SPE consuming tenant is unchanged.

Ten customers ⇒ 14 app registrations and still **4 of 25 container types**. That ratio is the
point of the split: **customer growth costs app registrations, not container types.**

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
override surface, which Model 1 (one SPE consuming tenant, one registration) does not get. ⚠️ Note what
this does **and does not** say: Model 1 customers share a *container-type registration*, and therefore a
settings baseline. They do **not** share a container, a BFF, or any Azure resource — those are dedicated
per customer in both models (D-12).

> 🔴 **App-only access is NOT isolated by the platform (owner D28, 2026-10-06).** Each Model 1 stamp's UAMI holds
> application `full` on the shared registration (H8, T227b), and Microsoft documents that an app-only token reaches
> **every container of the type** — there is no per-container app scoping (Learn, *Configure authentication and
> authorization*, 2026-08-24). Delegated (OBO) access is isolated by container membership. The owner chose one container
> type per model and one container per customer (per Dataverse environment), with app-only isolation **enforced in the
> BFF's code and tests (T227d)**: an app-only SPE call may only target the stamp's own container(s).
>
> **How it is enforced (T227d).** Every app-only SPE call in the BFF gets its Graph client from
> `SpeContainerOwnershipGuard`, which refuses (404 `spe_container_not_owned` — the same answer as "does not exist" —
> before Graph) any container the stamp
> does not own. Own = an id in the stamp's container settings (`EmailProcessing__DefaultContainerId`,
> `Communication__ArchiveContainerId`, and — for environments older than the marker — `SharePointEmbedded__OwnedContainerIds`)
> **or** a container whose custom property `spaarkeCustomerId` equals the BFF's `Customer__Id`. The BFF writes that
> marker on every container it creates. SPE Admin on a stamp is confined the same way (owner D29): lists and searches
> show only the stamp's containers. Container-TYPE operations (settings, permissions, create a type) touch no customer's container data and are not filtered: Graph refuses them app-only, because a stamp's identity holds only `FileStorageContainer.Selected` (no `FileStorageContainerType.*`). The marker stops a misrouted or forged container id; it does not stop another
> stamp's code, since every stamp identity can rewrite markers. ArchTest `SpeAppOnlyContainerGuardTests` fails the
> build on an app-only SPE path that bypasses the guard.

Grant the BFF app what it needs on the relevant registration; **do not make it an owner.** The per-app
grant API is v1.0 `PUT /storage/fileStorage/containerTypeRegistrations/{containerTypeId}/applicationPermissionGrants/{appId}`
(`PATCH` to update, `DELETE` to remove), called as the owning app. **H8 owns the per-customer grants (T227b):**
before creating the customer's container it ensures two — the **stamp UAMI** with application `full` (the BFF's
app-only Graph calls run as the UAMI, `Graph__ManagedIdentity__ClientId`) and the **BFF app registration** with
delegated `full` (its OBO calls). A grant naming the app registration does not cover the UAMI, and vice versa.

### Owning-app credential — managed-identity federated credential (task 248, 2026-10-03)

Owner decision **D16** (2026-10-02): the L2 control plane signs in as an owning app through a
**managed-identity federated identity credential (MI-FIC)** — ADR-028 A4's default for confidential
clients — not with a certificate. No ADR amendment was needed. **An owning app set up this way has no
certificate and no client secret**; nothing is stored in any Key Vault.

- The owning app carries one federated identity credential per L2 Worker UAMI that must act as it
  (issuer `https://login.microsoftonline.com/{tenantId}/v2.0`, subject = the UAMI's **principal** id,
  audience `api://AzureADTokenExchange`; at most 20 per app). MI-as-FIC needs a **user-assigned** managed
  identity in the **same tenant** as the app — which is why this works for Model 1's single-tenant owning
  app.
- The Worker UAMI requests a token for `api://AzureADTokenExchange` and presents it as the client assertion
  in a client-credentials request for the owning app (`ClientAssertionCredential`). The Worker's
  configuration names only `ContainerTypeId` + `OwnerAppId` per container type.
- **Evidence (live probe 2026-10-03)**, from a throwaway Azure Container Instance carrying the dev Worker
  UAMI: the exchange returned an owning-app Graph token with **`appidacr` = `2`** (client-assertion class —
  the same class a certificate produces), `idtyp` = `app`, roles `FileStorageContainerTypeReg.Selected` +
  `FileStorageContainer.Selected`. The container-type registration GET and the container list for the type
  both returned 200 as that token.
- The 24 h "certificate replication" wait that an earlier design gated on (H0 `SpeCertBootstrap`) is gone;
  H0 now checks the owner entry, the token and the registration (`SpeOwnerCredential`).

**`Spaarke Model 1` — recorded state (2026-10-03)**

| Item | Value |
|---|---|
| Container type | `Spaarke Model 1`, id `fb3817a8-5a55-42ba-8cc9-12cf055168b8`, `standard`; created by the owner in the SharePoint admin center |
| Owning app | `Spaarke SPE Model 1 Owner`, appId `bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e`, single-tenant, no secret, no certificate; Graph application permissions `FileStorageContainer.Selected` + `FileStorageContainerTypeReg.Selected`, admin-consented via `appRoleAssignedTo` |
| Federated credential | `sprk-controlplane-dev-uami-assertion` → dev Worker UAMI `sprk-controlplane-dev-uami` (principal `38f7693f-e6e2-4a3e-9acf-7f9e29dd4044`) |
| Billing | `Microsoft.Syntex/accounts` `dc4749c2-ca04-4b38-b6c2-e38dc3eec72b` in `rg-spaarke-shared-prod` — the binding is permanent; never delete that resource group or account |
| Registration in Spaarke's tenant | registered at creation (2026-10-03T22:54:58Z); `owningAppId` = the owning app; `billingStatus` `valid` |
| Grants on the registration | the owning app (delegated `full` / application `full`); **known extra grant**: Microsoft Graph Explorer `de8bc8b5-d9f9-48b1-a8ad-b748da725064` (delegated `full` / application `none`) — added during creation, not by L2; owner decision 2026-10-03: **keep** |
| Containers | none yet (list returned empty) |

The admin-center creation flow prompts for a client secret on the owning app; none was added, and none
should be. Setup procedure: [`SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md`](../guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md).

> ⚠️ **Grant the identity the BFF actually authenticates as.** With `Graph:ManagedIdentity:Enabled=true`
> (every Azure environment), the BFF's app-only Graph client is its **user-assigned managed identity**, not
> its app registration — on dev, appId `5967251e…` (`mi-bff-api-dev`), not `1e40baad…`. A grant naming the
> app registration does not give that client access. (Verified 2026-10-04: dev's `Spaarke PAYGO 1`
> registration carries `full` grants for both.) This is also the identity SPE Admin uses — §6B.

---

## 4. How to create a container type

### Prerequisites

- A Microsoft 365 tenant with SharePoint active
- **A NEW Entra app registration** for this container type (R1 — it cannot be shared)
  - `FileStorageContainer.Selected` (application)
  - `FileStorageContainerTypeReg.Selected` (application) — required to register on consuming tenants
  - `signInAudience = AzureADMultipleOrgs` **if it will ever serve a consuming tenant** (Model 2)
  - **No certificate and no client secret.** Its credential is a federated identity credential trusting
    the L2 Worker's user-assigned managed identity (§3A "Owning-app credential"). If the SharePoint admin
    center asks for a client secret during creation, do not add one
- A **non-guest member account in the owning tenant**. No admin role is required — the Graph create
  API is callable by any non-guest owning-tenant user, who is auto-assigned as an owner
- For `standard`: an Azure subscription + resource group, with owner/contributor to attach billing

### ⭐ Preferred path — SharePoint admin center (verified working 2026-10-03)

**SharePoint admin center → SharePoint Embedded → Apps → + Create app.** This is the SPE Apps
experience (GA Jul 2026) and it is the **recommended way to create a container type**: it links the
Entra app, creates the container type, and sets up billing in one flow, with a delegated identity —
so it sidesteps R5 entirely. Used successfully for `Spaarke SPE Model 1 Owner` on 2026-10-03.

The panel's vocabulary differs from Graph's. The mapping:

| Admin-center "Billing type" | Graph `billingClassification` | Who pays |
|---|---|---|
| **Owner org** | `standard` | **Us** — connect an Azure billing subscription |
| **User org** | `directToCustomer` | **The customer** — their admin activates pay-as-you-go |

Panel fields: **Entra app registration** (choose *Use an existing Entra app* and select the owning app
you created) · **Owners** (up to 3) · **Billing type** · **Billing subscription setup** (now / later).

> ⚠️ The panel states *"This setting can't be changed later"* about billing type. That is **R3 +
> §2's no-conversion rule** surfacing in the UI. Get it right the first time.

### Alternative — the Graph call

Use when scripting, or when the admin center is unavailable.

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
   v1.0 `PUT /storage/fileStorage/containerTypeRegistrations/{containerTypeId}`, called as the owning app
   (`FileStorageContainerTypeReg.Selected`; create-or-replace, so a later PUT must keep the owning-app
   grant). Spaarke exposes this as
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

## 6A. The Dataverse config record — what is actually read

After creating a container type, register it in Dataverse (`sprk_specontainertypeconfigs`) so the SPE
Admin app can reach it. **`ResolveConfigAsync` selects exactly five columns** — verified in code,
2026-10-03:

```
sprk_specontainertypeconfigid · sprk_containertypeid · sprk_owningappid
sprk_keyvaultsecretname · _sprk_environment_value   (→ sprk_speenvironment.sprk_tenantid)
```

Mapping: **`sprk_owningappid` → `ContainerTypeConfig.ClientId`** (and, identically, `OwningAppId`) and
`sprk_keyvaultsecretname` → `SecretKeyVaultName`. *(Corrected 2026-10-04: an earlier version of this page
said the `OwningApp*` properties were never populated; `ResolveConfigAsync` sets them to the same values.)*

**Since 2026-10-04 no credential is read from this record** (§6B). What each column now does:

| Column | Used for |
|---|---|
| `sprk_containertypeid` | Which container type the config is about |
| `sprk_owningappid` | Default `owningAppId` when **creating** a container type — nothing else |
| `_sprk_environment_value` → `sprk_tenantid` | **Load-bearing.** The tenant guard (§6B) refuses a config whose tenant is blank or is not the BFF's |
| `sprk_keyvaultsecretname` | **Nothing.** Optional on the form and the API; leave it blank |

### 🔴 Everything else on the "Edit Container Type Config" form is inert

| Form section | Read by the BFF? |
|---|---|
| Container Type ID · Owning App Client ID · Environment | ✅ **yes** |
| Key Vault Secret Name | ❌ **no** — since 2026-10-04 (§6B) |
| **Storage & Sharing** (Max Storage Per Container, Sharing Capability, Item Versioning) | ❌ **no** |
| **Permissions** (delegated + application checkboxes) | ❌ **no** |
| **Consuming App Registration** (Client ID, KV Secret Name) | ❌ **no** |

These are collected, stored in Dataverse, and never consulted. An operator setting **Max Storage Per
Container** here would reasonably believe it applied — it does not. The real settings live on the
**Container Type Settings tab**, which writes to Graph.

**"Consuming App Registration" is Phase-3 scaffolding that was never wired.** Conceptually it would
hold a *registered-access* app (§3A: ownership is 1:1, access is N-to-many) — a second app granted
permissions on the container type without owning it. **Today, leave it empty**; populating it changes
nothing. Grant access apps via `applicationPermissionGrants` on the container-type **registration**
instead (§3A).

**Recommended fix**: remove the three inert sections from the config editor
(`src/solutions/SpeAdminApp/src/components/settings/ContainerTypeConfig.tsx` — form state keys
`maxStoragePerBytes`, `sharingCapability`, `isItemVersioningEnabled`, `delegatedPermissions`,
`applicationPermissions`, and the consuming-app pair), or render them read-only labelled *"set on the
container type — shown for reference"*. **Do not wire them** — that would duplicate the Graph settings
tab and create two surfaces that disagree.

---

## 6B. ✅ SPE Admin identity — secret-free (resolved 2026-10-04)

**SPE Admin never authenticates as a container type's owning app.** It uses two identities, neither of
which holds a secret:

| Work | Identity | Mechanism |
|---|---|---|
| **Container work** — containers, items, recycle bin, search, security, dashboard sync, bulk jobs | **The BFF's own app-only identity** — on Azure, its user-assigned managed identity (dev: `mi-bff-api-dev`, appId `5967251e…`) | `IGraphClientFactory.ForApp()` — the same client every other SPE path in the BFF uses |
| **Grants and container types** — Consuming Tenants panel, Register, create/settings/owners | **The signed-in SPE administrator**, delegated | The BFF's existing on-behalf-of exchange |

Access for the BFF's identity comes from an **`applicationPermissionGrant` on the container type's
registration** in its tenant (§3A) — **never from ownership**.

### What it replaced, and why the "obvious" fix was a dead end

It used to authenticate app-only **as each owning app**, with a client secret fetched from Key Vault by
the config's secret name (ADR-028 exception E-1). A config without a usable secret — the Model 1 config
held the literal `null` — failed every container operation.

The intuitive secret-free fix was to federate each **owning app** to the BFF's managed identity. Microsoft's
rules make that unworkable ([Configure an application to trust a managed identity](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity)):

- **The managed identity and the app registration must be in the same tenant**, and
- **an app registration holds at most 20 federated credentials.**

Under D-12 every customer has its own BFF and managed identity, so: **Model 1** → one federated credential
per customer on the Model 1 owner ⇒ a hard **20-customer cap**; **Model 2** → each customer's identity lives
in the *customer's* tenant while the owner lives in Spaarke's ⇒ **not supported at all**. Grants have
neither limit: each customer's BFF identity is granted on the registration in its own tenant.

### 🔴 Tenant guard — fail closed

The BFF's identity can only act in its own tenant. App-only work is **refused** for a config whose
environment tenant is blank, or is not the BFF's (`TENANT_ID`) — otherwise the client would list the *BFF's*
tenant's containers under another tenant's config. Manage a container type from the BFF deployed in its tenant.

### Onboarding a container type for SPE Admin (operator steps)

1. **Grant the BFF's identity on the registration.** SPE Admin → Container Types → the type →
   **Consuming Tenants → Add**: appId = **the BFF managed identity's appId** (dev `5967251e-171c-46fe-a6c2-ef843c90309d`),
   application permissions `full`, delegated permissions `full` *(mirrors the dev type's existing grant)*.
   Requires the **SharePoint Embedded Administrator** (or Global Administrator) Entra role. New grants can
   take **up to an hour** to propagate.
2. **App roles on the BFF identity** (one-time per BFF, Entra admin):
   `FileStorageContainer.Selected` ✅ (dev has it) · `SecurityEvents.Read.All` — **needed by the Security
   tab**; dev's managed identity does **not** have it yet (the old owning app did).
3. **Config record**: leave *Key Vault Secret Name* blank; make sure the linked environment's tenant id is set.

Verified state (2026-10-04, read-only probe): on dev type `Spaarke PAYGO 1` (`8a6ce34c…`) the BFF managed
identity already holds `full` app-only and delegated — broader than the owning app's own grant — so dev
loses nothing. The Model 1 registration needs step 1.

ADR-028 E-1 no longer covers any SPE Admin path: the census and allowlist rows for `SpeAdminGraphService`
and the (removed) `SpeAdminTokenProvider` were deleted from `CredentialCensusTests` / `CredentialGuardTests`,
so a secret-bearing credential reappearing there fails the build.

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
   *Update 2026-10-03 (tasks 213.2 / 248):* H8 no longer creates container types — the operator creates the
   type once (runbook). H8 creates, activates and verifies the customer's **container**, app-only as the
   type's owning app through the Worker UAMI's federated credential (§3A). Owning-app sign-in and the
   registration were proven live 2026-10-03; H8's container create has not yet run against `Spaarke Model 1`.
2. **The app registration set (§3A) is a provisioning input**, not an afterthought. **Every** customer —
   Model 1 and Model 2 alike — needs **its own BFF app registration** and a grant on the relevant
   container-type registration (its own under Model 2; Spaarke's single Model 1 registration under Model 1)
   — **not** a new container type.
3. **`sprk_containertypeid` on the environment registry** has a defined meaning per model: the **container
   type** is shared across all customers of a model, not allocated per customer. ⚠️ That is a statement
   about the container *type* only — each customer still gets their own **container**, and every Azure
   resource is dedicated per customer in both models (D-12).

---

## 9. Sources

- [Create and configure a container type — Microsoft Learn](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/create-container-type) (fetched 2026-08-28)
- [`knowledge/sharepoint-embedded/docs/learn-containertypes.md`](../../knowledge/sharepoint-embedded/docs/learn-containertypes.md) — curated snapshot + project findings
- Graph beta CSDL — `fileStorageContainerType`, `fileStorageContainerBillingClassification`
- `projects/sdap-SPE-admin-app-r2/notes/probe_containertype_create.py` — the app-only 403 evidence
- `projects/sdap-SPE-admin-app-r2/notes/obo-spike-findings.md` — task 010, the original delegated-only finding
