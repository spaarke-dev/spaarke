# Which Azure resources can be shared, which cannot — a precise analysis

> **Written 2026-09-28** at the owner's direction: *"I'm afraid we are not being precise in our analysis of
> the resources and what can/should be shared and which could not / should not be shared."*
> That is correct. D-12 §3's *"every Azure resource is dedicated per customer"* plus three exceptions was a
> **posture, not an analysis** — it asserted a default and then carved out whatever contradicted it.
> **This note replaces that reasoning.** ⚠️ It does not change any decision by itself; it is the basis for
> re-deciding §3 row by row.

---

## 1. Why the previous framing was imprecise

Three specific defects, all mine:

1. **One axis for many different questions.** "Shared vs dedicated" conflated *where data rests*, *how
   access control is scoped*, *what a bug can reach*, and *what it costs*. Those give different answers per
   resource, so a single verdict column hid the reasoning.
2. **A default was doing the work of an argument.** "Everything dedicated, except…" means any resource
   nobody thought about inherits the expensive answer without justification.
3. **A cost claim was inherited, not verified.** The Redis P1 figure came from a prior note; §4 below shows
   the premise behind it does not survive checking.

---

## 2. The criteria that actually decide it

A resource is **unsafe to share** if **any** of C1–C3 holds. C4–C6 then decide whether dedication is
*worth* it where sharing is safe.

| # | Criterion | Why it is decisive |
|---|---|---|
| **C1** | **Does it hold customer data at rest?** | The privilege boundary. Legal work product in a shared store is the case that cannot be argued away. |
| **C2** | **Is its access control scoped per-instance rather than per-object?** | If a credential grants the whole instance, every consumer can reach every customer's data and only *our code* prevents it. Redis is the clearest case: auth is per-instance, not per-keyspace. |
| **C3** | **Does a per-customer discriminator exist AND is it actually distinct?** | 🔴 The trap D-12 was built to catch: `tenantId` is a discriminator that is **identical for every Model 1 customer**. A control keyed on it passes its tests and separates nothing. |
| **C4** | **Fixed cost floor per instance** | Consumption-priced ⇒ dedication is ~free ⇒ dedicate by default. A real monthly floor ⇒ justify it. |
| **C5** | **Hard platform cap** | Some resources have caps that make *either* choice fail at scale — FIC = 20/app registration, SPE container types = 25/tenant. These must be counted, not assumed. |
| **C6** | **Blast radius of a defect or misconfiguration** | Not "will it leak", but "if it does, how many customers". |

**Sharing is safe only when C1 = no, C2 = no, and C3 = a genuinely distinct discriminator exists.**

---

## 3. The analysis

✅ = safe/yes · ❌ = no · ⚠️ = qualified. **Verdict** is *this note's recommendation*, not a decision.

| Resource | C1 data at rest | C2 instance-scoped auth | C3 real discriminator | C4 floor | Verdict |
|---|---|---|---|---|---|
| **Dataverse environment** | ✅ **yes** — the record store | ✅ | ❌ no column exists | licence | 🔴 **DEDICATED** — settled, D-12 §1 |
| **SPE container** | ✅ **yes** — document bytes | per-container | ✅ container id | none | 🔴 **DEDICATED** — already is |
| **AI Search** | ✅ **yes** — document text + embeddings | ✅ service-scoped keys | ⚠️ only `tenantId` today → **fails C3** | real (S1+) | 🔴 **DEDICATED** — the strongest case after Dataverse |
| **Cosmos** | ✅ **yes** — audit, sessions, memory | ✅ account keys | ⚠️ `/tenantId` → **fails C3** | serverless ⇒ none | 🔴 **DEDICATED** — free to do; already is |
| **Storage** | ✅ **yes** — document/temp bytes | ✅ account-scoped | container path | negligible | 🔴 **DEDICATED** — already is |
| **Key Vault** | ✅ **yes** — secrets | ✅ vault-scoped RBAC | vault name | ~free | 🔴 **DEDICATED** — already is |
| **Redis** | ⚠️ **cache of** customer data + **OBO tokens** | 🔴 **yes — per-instance, not per-keyspace** | ⚠️ 3 sites fail C3 (§4) | ⚠️ **see §4 — premise overturned** | ⚠️ **fails C2 ⇒ lean DEDICATED, but at Standard not Premium** |
| **Service Bus** | ⚠️ message bodies transiently | namespace-scoped | queue/topic | ~free | 🔴 **DEDICATED** — already is; ~free |
| **App Service + Plan** | ❌ compute only | n/a | app identity | 🔴 **real, largest** | 🔴 **DEDICATED — forced**, not chosen: a plan cannot span subscriptions (ADR-027 amendment) |
| **Azure OpenAI** | ❌ **not persisted by default** | ✅ resource keys | n/a | consumption ⇒ none | 🟡 **DEDICATED for quota, not data** — free, so no reason not to |
| **Document Intelligence** | ❌ stateless per call | ✅ | n/a | consumption | 🟢 **SHAREABLE** — dedicated anyway because it is free to be |
| **Content Safety** | ❌ stateless | ✅ | n/a | free tier | 🟢 **SHAREABLE** ✔ named exception |
| **App Insights / Log Analytics** | ⚠️ **telemetry, which can carry identifiers** | workspace-scoped | ⚠️ needs `customerId` | per-GB | 🟢 **SHAREABLE with `customerId`** ✔ named exception — ⚠️ see §6 |
| **Static Web Apps** | ❌ static bundles | n/a | n/a | ~free | 🟢 **SHAREABLE** ✔ named exception |
| **SignalR** | ❌ transient | ✅ | connection | per-unit | 🟢 shareable when enabled; dedicate if it ever carries content |
| **BFF app registration** | ❌ | n/a | ✅ **FIC per customer** | none | ⚠️ **SHAREABLE up to 20 customers** — §5 |

**The shape that emerges is simpler than "everything dedicated":** the resources that must be dedicated are
**the ones that hold data at rest** (Dataverse, SPE, AI Search, Cosmos, Storage, Key Vault), plus **App
Service Plan** — which is dedicated for a *structural* reason, not a data one. Everything else is either
free to dedicate (so the question does not matter) or genuinely shareable.

---

## 4. 🔴 Redis — the P1 premise does not survive checking

**Owner's account**: *"we had to go with the P1 because of some feature not available in the Basic C0 tier;
which is also why lean shared."*

The reasoning is sound, but the repo says the jump from Basic did not need to reach Premium.

**(a) What the module claims.** `infrastructure/bicep/modules/redis.bicep:2,19,41` — *"hardened with VNet
injection, RDB persistence…"*, `param sku string = 'Premium'`, *"(Premium required for VNet injection +
persistence)"*. So the **module default** is Premium, justified by **VNet injection**.

**(b) VNet injection is not actually configured anywhere.** `redis-dev.bicepparam` and
`redis-staging.bicepparam` both record *"Module defaults: … subnetId='' (public)"*, and `customer.bicep:423`
says *"No `subnetId`/`staticIP` override"*. The Premium-only feature the default is justified by is **unused**.

**(c) Microsoft now recommends against it.** [Private Link is supported on **all** tiers — Basic, Standard,
Premium, Enterprise](https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/cache-private-link);
[VNet injection is **Premium-only** and *"not recommended"*](https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/cache-network-isolation),
with private endpoint preferred as *"easy to set up or remove, supported on all tiers"*. **Network
isolation therefore does not require Premium.**

**(d) 🔴 The repo's own prod parameter file already says Standard.**
`infrastructure/bicep/parameters/redis-prod.bicepparam:14-16` — *"**Standard C2 is the starting
recommendation for prod**; Premium tier (VNet injection, RDB persistence, geo-replication) **deferred to
S1** once finance + security sign-off lands"*, and `:43` — *"Standard C2 (2.5 GB cache, replicated, ~99.9%
SLA) is the MVP prod tier."*

**(e) Live state**: the only deployed instance is `spaarke-bff-redis-dev` = **Basic C0**, in
`spe-infrastructure-westus2`. Nothing is on Premium today.

### What this means

**The gap you were closing from Basic C0 is closed by *Standard*, not Premium.** Basic lacks **SLA and
replication** — both present in Standard. The genuinely Premium-only features are **VNet injection**
(unused, deprecated), **RDB persistence**, **geo-replication** and **clustering**. For a cache governed by
ADR-009 — short TTLs, *"never cache authorization decisions"* — **persistence is not obviously required**;
losing the cache costs a cold start, not data.

⚠️ **The one thing to confirm with you**: if the driving feature was **RDB persistence** or **clustering**
rather than VNet injection, Premium stands and this section is wrong. The module comment names VNet
injection first, and that one is checkable and not in use — but your recollection is the authority on intent.

### Revised recommendation

| | Cost | SLA / replication | Network isolation | Verdict |
|---|---|---|---|---|
| Basic C0 (live today) | ~$16 | ❌ **none** | private endpoint ✅ | ❌ fails ADR-009 fail-closed posture in prod |
| **Standard C2** | *(verify — materially below P1)* | ✅ 99.9% + replica | private endpoint ✅ | ✅ **recommended** |
| Premium P1 | ~$405 | ✅ | VNet injection (deprecated) | ❌ not justified by evidence found |

**So the shared-vs-dedicated question changes shape**: at Standard, dedicating is far cheaper than the P1
figure implied, and Redis **fails C2** (per-instance auth, holding OBO tokens and the `uac-access`
authorization cache). ⚠️ **Verify current Standard C2 pricing before this carries budget** — the $405 P1
figure came from a 2026-08 note and I am not re-quoting a number I have not checked.

**Independent of the SKU decision**: the three C3-failing key sites (`agent-thread`, `agent-config`,
`approle-module-map`) must be fixed either way. Dedication does **not** fix `agent-thread`, which collides
across *users* inside one customer.

---

## 5. App registrations — my claim was an over-assertion

**What I wrote** (ADR-028 amendment): *"Every customer gets its own BFF App Service, its own UAMI **and its
own app registration**."* The App Service and UAMI parts follow from D-12. **The app-registration part does
not** — I asserted it.

**App registration sharing is a separate axis from compute sharing.** One app registration can serve many
per-customer App Services: each customer's UAMI gets its **own federated identity credential** on the
**same** app registration, and MI-FIC still works — the credential is bound to the managed identity, not to
the app object.

🔴 **But there is a hard cap, and it is low.**
[A maximum of **20 federated identity credentials** per application or user-assigned managed identity](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-considerations)
— *"There isn't currently a way to increase this quota, even through a support request… you can create
another application registration to work around the limit."* ADR-028 already records the dev app
registration at *"1 of 20 used"*, so the limit is real and already tracked.

**So: one shared BFF app registration serves at most 20 Model 1 customers**, then needs a second. That is a
**sharding** problem, not a blocker — but it must be *designed*, not discovered at customer 21.

⚠️ [**Flexible federated identity credentials**](https://learn.microsoft.com/en-us/entra/workload-id/workload-identities-flexible-federated-identity-credentials)
(**preview**) allow expression matching on the subject claim specifically to escape this cap. Preview status
means it should not be depended on yet, but it is the likely long-term answer.

**Corrected position**: shared-vs-per-customer app registration is **open**, not decided. It affects
onboarding (N FICs vs 1 app reg per customer), the 20-cap sharding design, and consent. ADR-028's original
open question was better-founded than my closure of it — **I am reopening it.**

---

## 6. Two things this analysis exposes that were not on anyone's list

1. ⚠️ **App Insights is the weakest of the three named exceptions.** It is the only one where C1 is *"⚠️
   partly"* — telemetry legitimately carries user and record identifiers, and Spaarke logs record ids by
   design (ADR-015 permits identifiers, forbids content). A shared workspace therefore holds *metadata about
   every customer's matters*. The `customerId` requirement is what makes it acceptable; **without it this
   should be dedicated.** It should not sit in the same "obviously fine" bucket as Content Safety.
2. ⚠️ **"Already per-customer today" was being used as evidence that a resource *must* be dedicated.** For
   Cosmos, Storage, Service Bus and Key Vault that is fine — they are ~free to dedicate. But it is not an
   argument, and D-12 §3 presented it as one. The argument is C1: they hold data at rest.

---

## 7. What is now open for the owner

| # | Question | Blocking |
|---|---|---|
| Q1 | **Redis SKU** — was the Premium driver VNet injection (checkable, unused, deprecated) or RDB persistence / clustering? If the former, **Standard C2 + private endpoint** replaces P1. | the Redis shared/dedicated call |
| Q2 | **Redis shared vs dedicated at Standard pricing** — it fails C2 (per-instance auth over OBO tokens), which argues dedicate; the cost is much lower than P1 implied. | D-12 §3 Redis row |
| Q3 | **BFF app registration: shared (≤20 customers/app reg, sharded) or one per customer?** | ADR-028; onboarding design |
| Q4 | Confirm **App Insights** stays a named exception given §6.1, or moves to dedicated. | D-12 §3 exceptions |

**Nothing in §3's table is changed by this note on its own.** It is the reasoning; the decisions are the
owner's.
