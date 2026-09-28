# D-12 — Deployment-model redefinition (Model 1 / Model 2)

> **Decided**: 2026-09-28 by the owner, in discussion, across several turns.
> **Supersedes**: D3 v3 (`customer-provisioning-orchestration-r1/design.md`, 2026-08-12), which created
> the two-tier shared/dedicated split.
> **Status**: **FULLY DECIDED** (incl. §3a subscription structure and §4 App Service Plan, both settled
> 2026-09-28). Remediation NOT started. ⚠️ Requires an **ADR-027 amendment** — see §3a.
> **Evidence**: `notes/DEPLOYMENT-MODEL-SYNOPSIS.md` (problems → solutions → why they close), plus three
> discovery sweeps and direct verification against live Dataverse, live Azure and the source tree.

---

## 1. The decision

**Every customer gets their own Dataverse environment. The models differ by AZURE TENANT LOCATION.**

| | Dataverse environment | Azure tenant | Replaces |
|---|---|---|---|
| **Model 1** | dedicated per customer | **Spaarke's** | old "2a" (`spaarke-hosted-model2`) |
| **Model 2** | dedicated per customer | **the customer's own** | old "2b" (`customer-owned-model2`) |

**RETIRED**: the old "Model 1 = shared trial/SMB tier, many customers in one Dataverse environment."
**RETIRED**: the 2a / 2b sub-variant split — it collapses into the model axis itself.
**RETIRED**: any "Model 3", and every "three deployment models" framing.

### Terminology, binding

| Term | Meaning |
|---|---|
| **tenant** / `tenantId` | The Entra / Azure / Dataverse **tenant GUID**. Model 1 ⇒ always Spaarke's. Model 2 ⇒ the customer's. |
| **customer** | The business entity Spaarke sells to. **Many customers can share one Azure tenant** (that is Model 1). |
| **environment** | The Dataverse environment / stamp serving one customer. |

🔴 **Never use "tenant" to mean "customer."** They are different under Model 1 and the difference is
load-bearing for isolation.

---

## 2. Business-unit hierarchy

```
Root business unit  (e.g. "Spaarke")
├── Customer main BU        ← users live here
└── Secure Record BU        ← SIBLING, not a child
```

**Sibling placement is load-bearing.** Dataverse `Deep` depth traverses **downward** only, so a user in
the customer BU cannot reach a sibling secure BU. Making the secure BU a *child* of the customer BU
would let Deep depth read straight into it and destroy the isolation.

✅ Verified already deployed in dev: `Secure Project` and `Spaarke Business Unit 1` are both children of
root `Spaarke`.

**Renaming** (owner-approved): `Secure Project` → **`Secure Record`**. Three entities carry
`sprk_issecure` today — `sprk_project`, `sprk_matter`, `sprk_workassignment` (derived from live
metadata, not a written list). ⚠️ The name is a **fail-closed lookup key**: the live BU, the code
default (`DefaultSecureBusinessUnitName`), the config key (`SecureProject:BusinessUnitName`), the
pinning test and the docs must change **together**, or provisioning stops.

---

## 3. Azure resource disposition — DEDICATED per customer

**Every Azure resource is dedicated per customer, in both models.** Under Model 1 these live inside
Spaarke's Azure tenant as separate resources; sharing a *tenant* does not mean sharing *resources*.

Already per-customer before this decision, and unchanged: Cosmos (serverless — no fixed floor), Key
Vault, Storage, Service Bus, Document Intelligence, UAMI, SPE container, Dataverse environment.

### Why dedication is the correct default, not merely the safe one

Under Model 1 **every customer presents the same `tenantId`** (Spaarke's). Every isolation control that
keys on `tenantId` therefore cannot separate customers, **and reports success while failing**:

| Control | Key | Under Model 1 |
|---|---|---|
| AI Search `tenantId eq` filter (I2) | Azure tenant GUID | identical for all customers ⇒ **no separation**, ArchTest passes |
| Cosmos `/tenantId` partition (I3) | Azure tenant GUID | same partition ⇒ **no separation** |
| SPE container resolver (I4) | Azure tenant GUID | same container ⇒ **no separation** |
| **Redis cache key** `tenant:{tenantId}:{resource}:{id}:v{version}` | Azure tenant GUID | 🔴 **KEY COLLISION** — Customer A's cached value served to Customer B |

**Dedicating the resource moves isolation from a query filter to a resource boundary.** A filter must be
written correctly in every query, forever, by everyone. A boundary cannot be forgotten. For a legal
product holding privileged material, that difference is the whole argument.

Any resource that stays shared needs a **`customerId`** discriminator added to the BFF runtime — the
concept already exists and is modelled correctly in the L2 control plane (`CustomerId` alongside
`TenantId`), and has never reached L1.

### Per-resource rationale

| Resource | Dedicated? | Why |
|---|---|---|
| **AI Search** | ✅ **Yes** | Holds indexed document text and embeddings — the highest-value segregation case. A shared service means one filter defect exposes another firm's material. Real monthly floor; pay it. |
| **Redis** | ✅ **Yes** | 🔴 Not merely cost: the cache key is tenant-keyed, so a shared instance **collides** across Model 1 customers. Dedicating is the fix; the alternative is adding `customerId` to every cache key. Has a genuine **per-instance cost** — see §4. |
| **Azure OpenAI** | ✅ Yes | ⚠️ Note the reasoning differs: Azure OpenAI does not persist prompts or completions by default, so the *data*-segregation argument is weaker than for AI Search. The reason to dedicate is **noisy-neighbour / quota isolation**. Consumption-priced, so dedication costs nothing extra. ⚠️ **TPM quota is per-subscription-per-region** — separate OpenAI *resources* in one subscription still share the quota pool. Quota isolation needs subscription separation, not just resource separation. |
| **App Service Plan** | 🔔 **See §4** | The one genuine trade-off. |
| SignalR | optional | Feature-gated (`Notifications:SignalRSpine:Enabled`), Null-Object when off. Dedicate when enabled. |

---

## 3a. Azure subscription structure — ONE SUBSCRIPTION PER CUSTOMER (decided 2026-09-28)

**Each customer gets their own Azure subscription AND resource group**, so that usage can be segregated
and billed per customer. Applies to both models; under Model 1 the subscription lives in Spaarke's
Azure tenant, under Model 2 in the customer's.

**Two consequences, one good and one expensive:**

✅ **It resolves the OpenAI TPM-quota caveat in §3.** Quota is per-subscription-per-region, so
per-customer subscriptions give genuine quota isolation. Separate OpenAI *resources* inside one
subscription would not have.

🔴 **It forces App Service Plan option A — see §4.** An App Service app must be in the **same
subscription** as its App Service Plan, so a plan cannot be shared across customers that live in
different subscriptions.

### ⚠️ This requires an ADR-027 amendment (CLAUDE.md §6.5 path B)

`docs/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md` **Decision 1** currently
specifies environment-separated subscriptions — *"**Production subscription**: All production shared and
customer resources"* — i.e. one production subscription holding every customer's resources. Per-customer
subscriptions contradict that as written.

Note the ADR **raises this exact question and never answers it**: its §Context lists
*"4. **Customer isolation**: Whether customers need their own subscriptions"*, but Decision 4 turned out
to be about Dataverse CI/CD. **This decision answers ADR-027's own open question**, which makes it an
amendment rather than a violation — but the amendment must be written, not assumed.

---

## 4. App Service Plan — option A, FORCED by §3a

**DECIDED: option A — a dedicated App Service Plan per customer, in both models.** Not by preference —
§3a's per-customer subscription forces it.

Three shapes exist in principle:

| Option | Isolation | Viable here? |
|---|---|---|
| **A. Dedicated plan per customer** | Full — compute, process, config, identity, blast radius | ✅ **CHOSEN** |
| **B. Shared plan, dedicated BFF app per customer** | Process/config/identity per-customer; CPU + memory shared | 🔴 **IMPOSSIBLE under §3a** |
| **C. One shared BFF app for many customers** | — | ❌ rejected outright |

🔴 **Why B is impossible, not merely rejected**: an App Service **app must be in the same Azure
subscription as its App Service Plan.** With one subscription per customer, a shared plan would sit in
one subscription while the apps sat in others. Azure does not support that.
⚠️ **Verify this constraint against current Azure documentation before it carries budget** — it is now
load-bearing for a per-customer cost floor. The reasoning is sound but it should not stay unverified.

**C was rejected independently**: each BFF is bound to one Dataverse environment through per-deployment
config (`AzureAd:TenantId`, Dataverse URL), so a per-customer app is required regardless — and a shared
app would re-introduce every "`tenantId` cannot separate customers" problem in §3.

**Consequence**: a genuine App Service Plan floor per customer, the largest single per-customer fixed
cost in the stack. It was previously recorded here as an open trade-off (B for Model 1, A for Model 2);
that option **closed** when the per-customer subscription was decided.

> **Side effect worth noting**: with per-customer subscriptions and dedicated plans, Model 1 and Model 2
> become **nearly identical infrastructurally** — own subscription, own everything — differing only in
> **which Azure tenant owns the subscription**. That is consistent with this decision's framing, and it
> means the Model 1 / Model 2 distinction is genuinely thin. Worth remembering when writing the docs:
> resist re-inventing differences that no longer exist.

---

## 5. What this decision repairs

**P-2 — a live defect, not just naming.** `Model2Dedicated` was overloaded 2:1, covering old-2a and
old-2b, which have **opposite subscription ownership**.
`H1SubscriptionReadinessHandler.cs:109-118` maps it → `CustomerOwned` → **demands Azure Lighthouse
delegation**. Verified: **H1 reads `Profile` zero times**, so it cannot distinguish them. Today it
demands Lighthouse delegation for subscriptions **Spaarke already owns**.

The new split maps exactly onto what H1 already decides:

| Model | Ownership | Lighthouse | H0.5 consent |
|---|---|---|---|
| **1** (Spaarke tenant) | SpaarkeOwned | not needed | not needed |
| **2** (customer tenant) | CustomerOwned | required | required |

First time the tenancy label, subscription ownership and consent requirement agree.

**P-3 — the shared tier was never implemented.** `H5DataverseEnvCreationHandler` creates a Dataverse
environment **unconditionally**, keyed `dvenv-{customerId}`. There is no code path in which two
customers share one environment. Corroborating: **no tenant/customer discriminator column** exists on
`sprk_project` or `sprk_matter`, and invariants I1–I5 cover every shared resource **except** Dataverse.

**P-5 — the business unit stops carrying two dimensions.** The *environment* now carries the customer
dimension, freeing the *BU hierarchy* to carry only the security dimension. Customer segregation wants
containment; secure isolation wants non-containment. One hierarchy cannot do both — and now it does not
have to.

### The crux, stated precisely so it is not re-litigated

The framing *"there is no way to differentiate multiple customers in one Dataverse environment"* is
**slightly too strong** — per-customer app registrations, added as per-customer app users in
per-customer BUs, *could* differentiate ownership. **The decision holds for three stronger reasons:**

1. 🔴 **One shared `Secure Record` BU would hold every customer's secure records.** A single
   misconfiguration — a human added to the owner team, one role with Local depth — exposes **all
   customers at once** rather than one. Per-customer secure BUs break resolve-by-name and multiply BUs.
2. **There is no data-level discriminator as a backstop.** BU depth would be the only mechanism, with
   nothing to catch an error.
3. **The BU hierarchy cannot express both dimensions** with the containment each requires.

---

## 6. Consequences to execute (not done)

Ordered. Earlier items gate later ones.

1. **Resolve the four conflicting "Model 1" definitions in the docs** — four are live simultaneously;
   two docs say "three models", one says "two". Pick this decision, or remediation re-encodes ambiguity.
2. 🔴 **Introduce ONE shared `TenancyModel` enum** and force all **7 branch sites** and **3 silent
   defaults** through it — **before any renaming.** The literal is duplicated independently in ~8 places
   with three comparison styles. A partial rename fails **silently**: wrong Bicep stack, wrong
   Lighthouse gate, wrong cost envelope.
3. 🔴 **Migrate `sprk_tenancymodel` — do not relabel it.** Value `1` (`Model2Dedicated`) holds **both**
   old-2a and old-2b, which now split across *different* models. Re-pointing labels silently
   misclassifies every existing dedicated customer. Value `0` has no successor.
4. **H12c idempotency keys** embed the tenancy string (`h12c-{customerId}-{tenancyModel}-{hash}`) —
   renaming invalidates completed phases.
5. **13 BLOCKING doc files** (of 45 with hits, ~205 passages). ⚠️ `COMPONENT-INVENTORY.md` §7 is the
   **authoritative** shared-vs-dedicated BOM — *"when the two disagree, INVENTORY wins."*
6. **`Secure Project` → `Secure Record`** rename, as one coordinated change (§2).
7. **Azure cleanup** — the `Spaarke Model 1 Production` subscription and `rg-spaarke-trial01-prod-model1`
   / `rg-spaarke-shared-prod` are named for the retired tier. Owner is handling separately.

**Retired artifacts** (dead once the tier goes): `model1-shared.bicep` (+ `.json`), `model1-customer.bicep`,
`model1-shared-l2-rbac.bicep`, `model1-prod.bicepparam`, the `spaarke-hosted-model1-trial` profile,
H4-shared and its collaborators, the H2b/H12c Model 1 branches, `SharedPlatformOpenAiEndpoint`,
`SharedBffAppRegistrationId`. ⚠️ `model1-shared.bicep` **has not compiled since 2026-08-17** and
`bicep-e2e-dry-run.ps1` has an assertion that *expects* it to fail — **deleting the file breaks that
assertion**.

**Invariants needing re-statement**: I2 / I3 / I4 have accurate *mechanisms* but promise *customer*
isolation their tenant-keyed values cannot deliver. Under §3 (all resources dedicated) they become
belt-and-braces rather than load-bearing — but their rationales must say what they actually enforce.
**I6** is scoped "Model 1 only" and derives its severity from the shared app-reg; it needs re-scoping or
it becomes vacuous. **I1 and I5 are clean** and need no change.

---

## 7. Not part of this decision

- The four misnamed symbols (`SpeAdminTenantScope`, `SpeAdminTenantScopeFilter`,
  `TenantFilterTemplateDocument`, `TenancyModel`) should be renamed regardless — they hold something
  that is **not a tenant**. ~40 call sites, no behaviour change.
- **Do NOT mass-rename `tenantId`.** The values genuinely are Entra tenant GUIDs. The defect is a
  **missing concept** (`customerId`), not a misnamed one.
