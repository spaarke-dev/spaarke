# Tenancy and the BFF confidential credential — Model 1 and Model 2

> **Status**: NORMATIVE reference (cited by ADR-028 Amendment A4) · **Date**: 2026-08-17 (rewritten 2026-08-18)
> **Purpose**: decide, per deployment model, what credential the BFF uses to authenticate as an OAuth confidential
> client — so that when `customer-provisioning-orchestration-r1` reaches its credential step the answer is already
> written down.
> **Replaces** `MODEL-2-TENANCY.md`, which framed the problem around a generic "topology A/B/C" comparison and a
> 20-FIC-cap ceiling. **Both were dead ends in our architecture** — the cap cannot bind here (§5), and the generic
> topologies didn't map onto how Model 1 is actually composed. This version covers only what applies to us.
> **Counterpart**: provisioning risk **R23** (`design.md:1429`) first flagged MI-FIC for Model 2; this is the answer.

---

## 1. The only platform rule that constrains us

> **The user-assigned managed identity and the app registration must be in the same Entra tenant.**
> — [Entra: configure an app to trust a managed identity](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity), prerequisites

That is the whole decision. Everything in §3 follows from it.

Cross-**tenant resource access** is fine and fully supported: a multitenant app registration in the Spaarke tenant,
consented into a customer tenant, authenticating with an assertion signed by a UAMI **also in the Spaarke tenant**,
calling Dataverse/Graph in the customer's tenant. What is *not* supported is the **app registration and the UAMI
living in different tenants**.

Two secondary constraints, neither of which changes any decision below: **cross-cloud is unsupported**
(commercial ↔ US Gov ↔ China, different exchange audiences), and FIC creation has a **propagation delay**
(`AADSTS70021` for a few minutes — retry logic required).

## 2. How the two models are actually composed

### Model 1 — shared Spaarke environment, 20+ customers

Per [`stacks/model1-shared.bicep`](../../../infrastructure/bicep/stacks/model1-shared.bicep):

- **Shared platform RG** (deployed once, idempotent thereafter): App Service Plan, OpenAI, AI Search, Redis,
  Service Bus, Monitoring, Doc Intelligence, shared KV, and — the important one — **ONE shared multi-tenant BFF
  App Service** (`{sharedBaseName}-api`) with **ONE shared BFF UAMI**, `sprk-{env}-shared-bff-uami`. That UAMI
  exists as the task-029 slot-swap stability fix: one stable identity bound to prod + staging so a swap doesn't
  rotate downstream RBAC, the Dataverse App User, or Graph app-role grants.
- **Per-tenant RG** (one per customer onboarding): a **per-tenant UAMI** `sprk-{env}-{customerId}-uami`, plus
  per-tenant KV, Storage, Cosmos, App Insights.
- **Entra app config (H3) is pass-through** — this stack accepts app identifiers as parameters and echoes them into
  the per-tenant KV; it does not create app registrations.
- Model 1 is the documented **ADR-027 Path A exception** (shared fixed-floor resources make per-customer stamps
  uneconomic at trial/SMB scale). Subscription isolation is replaced by five binding code-level invariants
  **I1–I5** (no default tenant; `tenantId` filter on AI Search; `/tenantId` partition predicate on Cosmos;
  per-tenant SPE container derivation; per-tenant-scoped Graph tokens), enforced by ArchTests.

**The identity that performs OBO in Model 1 is the shared BFF UAMI** — not the per-tenant UAMI, which owns only
per-tenant data resources. This is the fact that decides Model 1's credential story.

### Model 2 — customer-dedicated stamp

Per `customer.bicep` / `stacks/model2-full.bicep`: a full dedicated stamp — its own App Service **and** App Service
Plan, its own UAMI, KV, Cosmos, Storage, AI Search, OpenAI. Strictly ADR-027 compliant (one subscription per
customer). It deploys **either into the Spaarke tenant or into the customer's own tenant** (`design.md:1006`:
"the app registrations live in whichever tenant hosts the deployment").

## 3. The credential decision

| Deployment | App registration lives in | UAMI performing OBO | Same tenant? | **Credential** |
|---|---|---|---|---|
| **Model 1** (shared env, 20+ customers) | Spaarke tenant | `sprk-{env}-shared-bff-uami` — Spaarke tenant | ✅ | ✅ **MI-FIC** |
| **Model 2 — Spaarke tenant** | Spaarke tenant | stamp UAMI — Spaarke tenant | ✅ | ✅ **MI-FIC** |
| **Model 2 — customer tenant** (Azure + Dataverse + SPE + app registration all customer-side) | Customer tenant | stamp UAMI — customer tenant | ✅ | ✅ **MI-FIC** |

> ### Every Spaarke deployment shape is intra-tenant. **MI-FIC covers all of them.**
> No certificate path is required for any shape we deploy. The certificate remains the ADR-028 A4 sanctioned
> alternative as a matter of policy (and is already in production use by `CiamGraphClientFactory` for the CIAM
> provisioner), but **no Spaarke deployment model needs it**, and no certificate provisioning automation needs
> to be built.

**Normative rule (ADR-028 A4)** still applies as a guard: if a future shape ever cannot satisfy the same-tenant
rule, fall back to a **Key Vault certificate — never to a client secret** (§6).

### 3.1 Explicitly ruled out — Spaarke-owned app registration + customer-tenant compute

A fourth shape is *technically* constructible: compute (App Service + UAMI) in the customer's Azure while the app
registration remains Spaarke's multitenant app, its service principal provisioned into the customer tenant by
admin consent. This is the **only** shape that breaks the same-tenant rule — credentials attach to the
*application object* (which would stay in the Spaarke tenant), not to the service principal — so MI-FIC would be
structurally impossible and a certificate would be mandatory.

**It is ruled out. It was never part of the approach** (owner decision, 2026-08-18). Recorded here so it is not
re-derived from the platform rule by a future reader.

**Why it is the wrong design**, if it ever resurfaces:

- **Nothing requires it.** In a full customer-tenant install, the users, Dataverse, SPE and Graph resources are all
  customer-side, so every job the app registration does — validating inbound tokens, holding the permission grants,
  performing the OBO exchange — is customer-tenant scoped. There is no cross-tenant hop for a Spaarke-side app
  object to serve.
- **It is the worst of both worlds.** It is the single shape that breaks MI-FIC and forces a certificate; it places
  a foreign identity holding ~14 Graph permissions inside the customer's tenant; and it leaves Spaarke permanently
  holding a credential for a system running in someone else's environment. That is a liability, not a benefit.
- **The arguments for it don't survive.** *Trial→production continuity* (keep one app registration across a Model 1
  → Model 2 migration to avoid re-consent) is a one-time migration cost — create the app in their tenant and
  consent once. *Central permission curation* (update grants once rather than in N tenants) is real but modest, and
  `Register-EntraAppRegistrations.ps1` is already idempotent and tenant-aware, so it can push grant updates into
  customer tenants.

**Where the shape came from**: the multitenant app + consent-capture mechanism is genuinely **correct for Model 1**
(Spaarke-hosted BFF; customers' users and M365 in their own tenants, so the app must be foreign and consented). It
appears to have been generalized to Model 2 without re-examination.

**Doc fix owed by provisioning** — `design.md:1006` (§9.1) states both readings in consecutive sentences:

> *"…for Model 2 customer-owned tenants, **register the same multitenant BFF app in the customer tenant** (per D18
> consent-capture)."* — implies one Spaarke-owned app object, SP provisioned by consent (the ruled-out shape)
>
> *"The app registrations below **live in whichever tenant hosts the deployment**…"* — implies a distinct app object
> created in the customer's tenant (correct)

The second sentence is the intended rule. The first should be corrected so it does not read as licensing the
ruled-out shape. Note the surrounding config (`AzureADMultipleOrgs`, the D18 `consent-callback` endpoint, `U-CB-3`
re-consent) is **correct and necessary for Model 1** — it should not be removed, only scoped to Model 1.

## 4. Open question — which app registration does the shared Model 1 BFF act as?

This is **not** a scaling or cap question; every reading works. It is an identity-design question that provisioning
must answer, because it changes onboarding.

When the shared Model 1 BFF performs an OBO exchange for customer #17, which app registration is it authenticating
as?

- **Reading 1 — one shared multitenant app registration.** The live BFF app registration `SDAP-BFF-SPE-API`
  (`1e40baad-…`) is already `AzureADMultipleOrgs`, which is the standard shape for one app serving many tenants.
  Consequence: **one FIC, created once.** Customer onboarding creates no federated credential at all.
- **Reading 2 — one app registration per customer.** Each customer's app registration carries its own FIC pointing
  at the *same* shared BFF UAMI. Consequence: **onboarding gains a per-customer FIC step**, and the BFF must select
  the correct app registration per request.

**Why the docs are ambiguous**: `design.md:57` (D2) states "per-customer app registrations in both models" and
`spec.md:236` makes it a binding MUST — but Model 1 deploys a *single shared* BFF App Service, and one app
validating 20+ audiences is an unusual design. The live app registration being multitenant points at Reading 1.
Nothing in the provisioning docs reconciles these.

**What it changes**: onboarding steps (a per-customer FIC, or none); the security boundary (one shared OAuth client
identity vs one per customer); H10's Dataverse App User registrations and T3's 14-role Graph parity checks, which
are currently per-app-registration; and which object each customer admin consents to.

**What it does *not* change**: whether MI-FIC works (it does, either way), and whether it scales (it does — §5).

**Working assumption pending provisioning's answer**: Reading 1 — `spec.md:236`'s MUST reads as written for Model 2
and generalized to "both models" without re-testing it against the shared-BFF composition. **This is inference, not
evidence**, and it is provisioning's call.

### 4.1 Proposed invariant I6 (Model 1 only)

Under MI-FIC, the shared BFF UAMI can mint an assertion for **any** app registration that trusts it. Today part of
the isolation boundary is resource-level — the BFF must read customer X's secret from customer X's Key Vault. That
boundary becomes purely **code-level**: nothing but correct tenant routing stops the process from authenticating as
the wrong customer.

Model 1 has already accepted logical-over-physical isolation (the ADR-027 Path A exception, invariants I1–I5), so
this is consistent with the tier's posture rather than a new compromise — and the practical delta is small, since
the shared BFF already needs read access to every per-tenant Key Vault. But it deserves naming:

> **I6 (proposed)** — the app registration used for an OBO exchange MUST be derived from per-tenant request context;
> no default or fallback app registration. ArchTest-enforced, same pattern as I1–I5.

Relevant only under Reading 2 in its strong form, but worth stating either way. **To be raised with provisioning**,
not adopted unilaterally here.

## 5. The 20-FIC cap does not bind — closing the question

> *"A maximum of 20 federated identity credentials can be added to an application or user-assigned managed
> identity."* — [Entra: workload identity federation considerations](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-considerations) (verified 2026-08-17)

The cap counts credentials **held by** an object. In MI-FIC the FIC object lives on the **app registration**, and
the UAMI is only the issuer — it holds nothing. So the cap asks: *how many UAMIs must one app registration trust?*

In every shape we deploy, the answer is **one**:

- **Model 1** — many app registrations (or one, per §4), all trusting **the single shared BFF UAMI**. Each app
  registration holds **1 of 20**, regardless of customer count.
- **Model 2** — each stamp has its own app registration *and* its own UAMI. **1 of 20.**

The cap would only bind in the inverse shape — **one** app registration trusting **many** UAMIs — which requires
per-customer compute behind a single shared OAuth identity. Neither model does that. Note that if we ever did, it
could not be engineered around: flexible FICs (which lift the cap via claim-matching) are preview **and explicitly
do not support managed identities**.

**Conclusion: treat the cap as a non-factor.** Current dev headroom for reference: the BFF app registration holds
**1 of 20** — the GitHub Actions deploy OIDC credential. Adding the MI-FIC makes it 2.

## 6. Why a client secret is the worst option for customer-tenant deployments

Entra **app management policies** are GA and let a tenant block `passwordAddition` outright or cap secret
`maxLifetime` on service principals. A customer with a hardened identity posture can therefore **refuse to let
Spaarke's service principal hold a client secret at all** — turning a hygiene preference into a deployment blocker,
precisely in the Model 2 customer-tenant case. Certificates and federated credentials are unaffected.

This inverts the intuition that customer-tenant deployments are a reason to defer going secret-free. They are the
strongest reason to do it.

## 7. Provisioning impact

`customer-provisioning-orchestration-r1` has already **shipped** the relevant handlers on PR #779 — H3 (app
registration + ~14 Graph/Dynamics grants + a 24-month client secret), H4 (Key Vault secret population + a separate
rotation handler), H9 (blue-green slot deploy), H10 (Dataverse application user). This lands as a **change request
against working code**, not as input to a design.

Since every shape is MI-FIC (§3), there is exactly one target state to build toward:

| | **MI-FIC** (all three deployment shapes) | **Secret** (today) |
|---|---|---|
| H3 | **Add**: create the FIC (issuer = the UAMI's tenant OIDC endpoint, subject = the UAMI principal, audience `api://AzureADTokenExchange`). Once per app registration — §4 decides how many that is | unchanged |
| H4 | **Delete**: no client secret to create or store | creates `BFF-API-ClientSecret` |
| Rotation | **Removed entirely** — the 24-month ceremony, `H4-rotate`, and expiry alarms all retire (U-CB-5 / U-CB-6) | permanent per-customer cost — and it compounds at 20+ customers |
| Never-remove MUST | `spec.md:242` / `design.md:783` rewritten | in force |
| Automation to build | **All of it** — zero FIC automation exists in the repo (verified exhaustively, incl. untracked paths); even the existing GitHub OIDC FIC was hand-run, requiring Application Administrator | exists |
| Failure mode | Silent misconfig (wrong issuer/subject/audience creates fine, fails only at exchange); `AADSTS70021` for minutes after creation | Expiry, **or the customer tenant blocking secrets outright** (§6) |

**The one new cost line is FIC provisioning automation**, which replaces secret creation *and* the rotation
ceremony. Net, provisioning gets simpler. **No certificate provisioning is needed** (§3.1) — that item is dropped,
not deferred.

## 8. What must be settled

1. **§4 — which app registration the shared Model 1 BFF acts as.** Provisioning's call. Decides whether onboarding
   creates a FIC per customer or none. **The one open item that changes work.**
2. **§3.1 doc fix** — provisioning corrects `design.md:1006` so the "register the same multitenant BFF app in the
   customer tenant" sentence no longer reads as licensing the ruled-out shape. One sentence; scope the multitenant
   + consent-capture mechanism explicitly to **Model 1**.
3. **Prototype cross-tenant *resource* access.** Distinct from credential tenancy (which is now always
   intra-tenant): a Model 2 customer-tenant stamp reaches Dataverse, Graph and SPE **in that customer's tenant**.
   That OBO path must be smoke-tested end-to-end before anything is removed.
4. **Keep the credential step pluggable.** The single ask of provisioning today: H3/H4's "configure BFF confidential
   credential" must be swappable between secret and FIC without restructuring the handler.
5. **I6** (§4.1) — raise with provisioning as a candidate sixth Model 1 isolation invariant.
6. **Sovereign cloud** — not in the current pipeline; recorded so it isn't discovered late (cross-cloud unsupported,
   different exchange audience).

**Dropped, not deferred**: certificate provisioning automation. No Spaarke deployment shape requires it (§3, §3.1).

## 9. Summary

- **Every Spaarke deployment shape is intra-tenant, so MI-FIC covers all of them** — Model 1, Model 2 in the
  Spaarke tenant, and Model 2 in the customer's tenant. One credential mechanism, no special cases.
- **No certificate path is needed.** The only shape that would have required one — Spaarke-owned app registration
  with customer-tenant compute — is **explicitly ruled out** (§3.1); it was never part of the approach. Certificate
  remains ADR-028 A4's sanctioned alternative as policy, but nothing needs to be built for it.
- **Never fall back to a secret** — it is the one credential a hardened customer tenant can refuse outright (§6).
- **The 20-FIC cap is a non-factor** in our architecture (§5). It should not appear in future scoping discussions.
- The genuine open item is **§4** — an onboarding-shape question (one FIC or N), not a feasibility, scaling, or
  tenancy one.
