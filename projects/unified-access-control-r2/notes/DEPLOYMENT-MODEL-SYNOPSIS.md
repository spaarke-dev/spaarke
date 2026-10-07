# Deployment-model redefinition — problems, solutions, and why they close

> **Written for**: the owner, to confirm alignment before any remediation starts.
> **Date**: 2026-09-28. **Status**: synopsis for sign-off — nothing has been changed yet.
> **Evidence**: three discovery sweeps (docs / code+infra / terminology) plus direct verification
> against live Dataverse, live Azure, and the source tree. Claims below are marked where unverified.

---

## 0. Two corrections to earlier statements in this discussion — mine

Recorded first, because later sections depend on them being right.

**C-1. "Sharing an Azure tenant means customers can't be isolated" — WRONG.**
I claimed the redefinition would not fix the isolation problem because Model 1 customers share Spaarke's
Azure tenant, so `tid` is identical for all of them. That conflates **sharing a tenant** with **sharing
resources**. They are orthogonal. One Azure tenant holds unlimited resource groups; each customer can
have their own AI Search, Cosmos, Storage, SPE container, app registration and Key Vault inside it. The
`tenantId` filter only fails to separate customers **when a resource is genuinely shared**. Make the
resource per-customer and isolation comes from the resource boundary, which is stronger than a query
filter.

**C-2. "`OptionsTenantContainerResolver` returns one container per Azure tenant" — true, but I framed
it as a design property. It is an implementation gap**, and the data model already supports the fix:

| Column | Type | Purpose |
|---|---|---|
| `businessunit.sprk_containerid` | `NVARCHAR(100)` | SPE container, per business unit |
| `businessunit.sprk_searchindexname` | `NVARCHAR(100)` | AI Search index name, per business unit |
| `businessunit.sprk_ai_search_index` | Lookup → `sprk_aisearchindex` | index record, per business unit |

Verified live. Per-customer resource binding is **already modelled**; the BFF runtime resolver ignores it.

---

## 1. The problems, as identified

### P-1 — The deployment models were defined three or four different ways at once

Four mutually incompatible definitions of "Model 1" are live in the documentation **today**:

| Source | "Model 1" means |
|---|---|
| `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §3.2 | Shared trial/SMB tier — many customers share ASP + OpenAI + AI Search |
| `INFRASTRUCTURE-PACKAGING-STRATEGY.md` | "Spaarke-Hosted: multi-tenant with **dedicated** resources per customer" |
| `SPAARKE-AI-STRATEGY-AND-ROADMAP.md`, `ADR-013` | All Spaarke-hosted customers, shared AI, `tenantId` filtering |
| `BYOK-CONFIGURATION-GUIDE.md` | "Multi-Customer (Shared)" — plus a **Model 3** no other doc mentions |

Two docs say "three models", one says "two". `AZURE-RESOURCE-NAMING-CONVENTION.md` states the
shared-platform model was already *replaced*, while four other docs still provision it.

### P-2 — `Model2Dedicated` is overloaded 2:1, and it causes a live bug

The old Model 2 covered **both** 2a (dedicated stamp in Spaarke's subscription) and 2b (customer's own
subscription). These have **opposite subscription ownership**, and one handler decides on exactly that:

`H1SubscriptionReadinessHandler.cs:109–118` maps `Model2Dedicated` → `CustomerOwned` → **demands
Lighthouse delegation**. Verified: **H1 reads `Profile` zero times**, so it cannot distinguish 2a from
2b. **Today it demands Lighthouse delegation for subscriptions Spaarke already owns.** Old-2a customers
also never needed H0.5 consent capture, and nothing in the DAG distinguishes them either.

### P-3 — The shared-Dataverse tier was documented, specified, funded and partly built — but never implemented

The control plane's `H5DataverseEnvCreationHandler` creates a Dataverse environment **unconditionally**,
keyed `dvenv-{customerId}`. **There is no code path in which two customers share one Dataverse
environment.** The shared tier exists in docs, Bicep, schema and an Azure subscription — but not in the
engine. Corroborating: the data model has **no tenant/customer discriminator column** on `sprk_project`
or `sprk_matter`, and invariants I1–I5 cover every shared resource *except* Dataverse.

### P-4 — "Tenant" means two different things, and the isolation invariants inherit the confusion

**Tenant** = the Azure/Entra/Dataverse tenant GUID. **Customer** = the business entity Spaarke sells to.
Every runtime `tenantId` in the codebase genuinely holds an Entra tenant GUID — the values are correct.
**The conflation is in the claims made about what those values isolate.** I2/I3/I4 are named for tenant
but their severity rationales promise *customer* isolation ("a legal firm's motion drafts returned to a
different firm"). That promise only holds if one customer = one Azure tenant.

Worst case: **`SpeAdminTenantScope`** contains no tenant id at all — it walks a Dataverse business-unit
hierarchy. Its own comment admits *"the cross-customer boundary lives in this codebase, not in Entra."*
An engineer reading the name could conclude the token already enforces it and weaken a live control.

### P-5 — The secure-record business unit cannot carry two dimensions at once

Under a genuinely shared environment, the business unit would have to carry **both** *customer* and
*security tier*, and those want opposite containment: tenant segregation wants the customer's things
*under* their BU; secure isolation wants secure records *outside* everyone's normal reach. One hierarchy
cannot do both.

---

## 2. The solutions

### S-1 — Redefine the models by **tenant location**, both dedicated

| | Dataverse environment | Azure tenant | Was |
|---|---|---|---|
| **Model 1** | dedicated per customer | **Spaarke's** | old 2a |
| **Model 2** | dedicated per customer | **customer's own** | old 2b |

The shared-Dataverse tier is retired. The 2a/2b split collapses into the model axis itself.

### S-2 — Business-unit hierarchy: customer BU and secure BU as **siblings** under root

```
Root (e.g. "Spaarke")
├── Customer main BU        ← users live here
└── Secure Record BU        ← secure records owned by its default owner team
```

Verified: this is **already the deployed shape** in dev — `Secure Project` and `Spaarke Business Unit 1`
are both children of root `Spaarke`.

### S-3 — Every Azure resource dedicated per customer, except where a fixed floor makes it uneconomic

Per-customer resources inside Spaarke's Azure tenant, bound per customer through provisioning config —
and, where the BFF resolves them at runtime, through the **existing** `businessunit.sprk_containerid` /
`sprk_searchindexname` columns.

### S-4 — Separate the vocabulary; add `customerId` only where a resource is genuinely shared

Do **not** mass-rename `tenantId` — the values are real Entra tenant GUIDs and renaming them would make
the code lie in the opposite direction. Instead:

| Term | Meaning |
|---|---|
| `tenantId` | Entra/Dataverse tenant GUID. Auth, Graph authority, OBO, SPE consuming tenants |
| **`customerId`** | The business entity. Already modelled correctly in the L2 control plane |
| `environmentId` | The stamp a customer is served from |

Rename the four symbols that hold something which is **not a tenant**: `SpeAdminTenantScope`,
`SpeAdminTenantScopeFilter`, `TenantFilterTemplateDocument`, `TenancyModel`.

---

## 3. Why these fully close the problems

| Problem | Closed by | Why it is complete |
|---|---|---|
| **P-1** four definitions | S-1 | One axis — tenant location — with two values and no sub-variants. Nothing left to interpret. The remediation must *first* pick this definition, or it re-encodes the ambiguity. |
| **P-2** overloaded literal | S-1 | The split maps **exactly** onto what H1 already tries to decide: Model 1 → SpaarkeOwned → no Lighthouse; Model 2 → CustomerOwned → Lighthouse. Same for H0.5 consent. This is the first time tenancy label, subscription ownership and consent requirement agree — **the redefinition repairs a live defect rather than only renaming.** |
| **P-3** unimplemented tier | S-1 | Retiring the shared tier aligns docs with an engine that already creates one environment per customer. H5 needs no change; the downstream Dataverse chain (H6/H7/H10/H11/H12a/H12b/H14) has **zero** tenancy branches and is unaffected. |
| **P-4** tenant/customer conflation | S-3 **+** S-4 | Two independent closures, which is why this is robust: **(a)** if every resource is per-customer, isolation comes from the *resource boundary* and no longer depends on a query filter being correct — `tenantId` filters become belt-and-braces rather than load-bearing; **(b)** the four misnamed symbols stop lying regardless. Any resource that stays shared is the *only* place still needing a `customerId` discriminator, and that set is now explicit rather than accidental. |
| **P-5** BU carrying two dimensions | S-1 **+** S-2 | The **environment** carries the customer dimension, which frees the **BU hierarchy** to carry only the security dimension. Sibling placement means Deep depth — which traverses downward — cannot reach the secure BU from a customer BU. Each dimension has exactly one mechanism. |

### The load-bearing point

**S-1 alone does not close P-4.** Redefining the models makes every customer's *Dataverse* separate, but
leaves Azure resources wherever they were. If AI Search stayed shared, two customers in Spaarke's tenant
would still produce an identical `tenantId eq` filter value and see each other's content — **with the
ArchTest passing.**

**S-3 is what closes it.** That is why the shared-vs-dedicated decision for Azure resources is not a cost
footnote — it is the difference between isolation that holds structurally and isolation that depends on
every query being written correctly forever.

---

## 4. Cost: which resources actually have a fixed floor

The owner's read is correct — most of these are consumption-priced and cost little to dedicate. ⚠️ The
*structure* below is stable; the **dollar figures are indicative and must be re-checked against current
Azure pricing** before any decision rests on them.

| Resource | Pricing shape | Real floor to dedicate? |
|---|---|---|
| **App Service Plan** | per-instance | ✅ **Yes** — the largest single floor |
| **Azure AI Search** | per-service tier | ✅ **Yes** — Free tier is too limited to be viable |
| **Redis** | per-instance | ✅ **Yes** |
| **SignalR** | per-unit (Free tier limited) | ✅ **Likely** |
| Cosmos DB | **serverless = consumption**; provisioned has a small floor | ❌ Serverless removes the floor |
| Azure OpenAI | pure token consumption | ❌ No floor — but **TPM quota is per-subscription-per-region**, a capacity constraint, not a cost one |
| SPE containers | storage consumption | ❌ No per-container cost |
| Storage, Key Vault | consumption | ❌ Negligible |

**So the genuine trade-off is narrow**: App Service Plan, AI Search, Redis, SignalR. Everything else can
be dedicated at near-zero marginal cost.

🔴 **One hard limit, not a cost**: SPE **container types** are capped at **25 per tenant** and **cannot
be deleted**. This is only a ceiling if each customer needs their own *type*. One container type serves
many containers, so **per-customer containers scale fine** — per-customer container *types* would not.
**Unverified**: whether a container type named `Spaarke Model 1` was ever actually created. If it was,
it is permanent and counts against the 25.

---

## 5. Scope — corrected

The raw inventory (45 doc files, ~205 passages, ~38 code surfaces) overstates the risk. The actual
**blocking** surface is small and concentrated:

| Category | Real size | Nature |
|---|---|---|
| **Code branch sites** | **8** *(re-verified 2026-09-28; was recorded as 7)* | All compare a string literal in **four** different styles. Only the two H13-probe sites use a named constant, and it is file-private |
| **Silent defaults** | **4** *(re-verified 2026-09-28; was recorded as 3)* | `H2a:284`, `H2b:289` and **`AiSearchTenantFilterInvariantProbe:240`** coerce blank → Model 2; `ArmCostEnvelopeChecker:268` coerces **anything unrecognized, incl. wrong-case** → **retired shared-tier cost envelope**. They **disagree with each other** and all fail *quietly* |
| **Data migration** | **1** | `sprk_tenancymodel`: value `1` holds **both** old-2a and old-2b, which now split across different models. A relabel silently misclassifies every existing dedicated customer |
| **Idempotency keys** | **1** | H12c embeds the tenancy string; renaming invalidates completed phases |
| **Docs — BLOCKING** | **13 files** | The rest of the 45 are wording or passing mentions |
| **Everything else** | ~25 test fixtures, comments | Mechanical |

**Highest-leverage single action, before any renaming**: introduce **one shared `TenancyModel` enum** and
force all 7 branch sites and 3 silent defaults through it. Otherwise a rename lands partially and the
failures — wrong Bicep stack, wrong Lighthouse gate, wrong cost envelope — are silent rather than loud.

**Known-stale, clears itself**: `model1-shared.bicep` has not compiled since 2026-08-17, and
`bicep-e2e-dry-run.ps1` has an assertion that *expects* it to fail. Retiring the tier removes a
permanently-red test. ⚠️ Deleting the file **breaks that assertion** — it expects the file to exist.

---

## 6. What remains open

1. **Which resources stay shared, if any?** S-3 assumes all dedicated. Confirm against the four
   real-floor resources above. Any resource that stays shared needs a `customerId` discriminator in the
   BFF runtime — the concept exists in the L2 control plane and has never reached L1.
2. **Was an SPE container type `Spaarke Model 1` actually created?** The only potentially irreversible
   item in the whole inventory.
3. **Rename the secure BU?** `Secure Project` → `Secure Record` — three entities carry `sprk_issecure`
   today (`sprk_project`, `sprk_matter`, `sprk_workassignment`, derived from live metadata). No customer
   deployments exist, so now is the cheapest moment. The name is a fail-closed lookup key: code and the
   live BU must change together, or provisioning stops.
4. **The live "Spaarke Model 1 Production" subscription** and its resource groups
   (`rg-spaarke-trial01-prod-model1`, `rg-spaarke-shared-prod`) are named for the retired tier.
