# Phase 0 — live tenant verification and prerequisite resolution

> **Status**: PREREQUISITES RESOLVED · **Date**: 2026-08-19 · **Method**: live `az` against the Spaarke dev tenant
> **Purpose**: remove Phase 0 from the critical path. The design (§6) listed Phase 0 as a gate that blocked
> everything behind "who provisions the FIC / when is an AAD admin available". That question is now closed.
> **Cited by**: [`PROVISIONING-CHANGE-REQUEST.md`](PROVISIONING-CHANGE-REQUEST.md) ·
> [`COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md`](COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md)

---

## 1. What changed

**The MI-as-FIC federated identity credential now exists on the BFF app registration.** It was created in this
session by the owner acting as Azure AD admin. It is additive and inert: no code in the repo mints a client
assertion today, so the running BFF is unaffected. Reversal is one command (§6).

This permanently removes the single hardest scheduling dependency in the project — the design assumed the FIC
would need an admin ceremony scheduled against someone else's availability, per environment, before the spike
could even begin.

## 2. Live state, verified 2026-08-19

| Item | Value | Note |
|---|---|---|
| Tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | |
| Subscription | `Spaarke Devlopment Environment` — `484bc857-3802-427f-9ea5-ca47b43db0f0` | |
| BFF app registration | `SDAP-BFF-SPE-API` · appId `1e40baad-e065-4aea-a8d4-4b7ab273458c` · objectId `c2aab303-50f8-4279-9934-503ab3a4b357` | |
| **`signInAudience`** | **`AzureADMultipleOrgs`** | ⚠ **Resolves the §3 conflict** — see below |
| Password credentials | **1** — `Dataverse-Checkout-20251218`, expires **2027-12-19T03:56:35Z** | The secret to eliminate. Note the *name* implies a Dataverse-checkout origin, not "BFF API" — relevant to the KV-name/app-reg mapping contradiction in `auth-azure-resources.md:705-708` |
| Key credentials | **0** | No certificate on this app registration |
| Federated credentials **before** | 1 — `github-actions-deploy-staging` | issuer `token.actions.githubusercontent.com`, subject `repo:spaarke-dev/spaarke:environment:staging`, audience `api://AzureADTokenExchange` |
| Federated credentials **after** | **2** — added `mi-bff-api-dev-assertion` | id `66bac39a-71f4-40d2-b3fe-abd7f6b0f7d1` (§4) |
| FIC headroom | 2 of 20 | Cap remains a non-factor |
| App Service | **`spaarke-bff-dev`** in RG **`rg-spaarke-dev`** | ⚠ **Doc drift** — see below |
| App Service identity | **`UserAssigned` only** — `mi-bff-api-dev` · clientId `5967251e-171c-46fe-a6c2-ef843c90309d` · principalId `9fd47efb-7962-492b-ac44-e5ccd0268ebb` | MI-FIC's hard prerequisite. ✅ satisfied. UAMI lives in RG `spe-infrastructure-westus2` — **cross-resource-group from the app, same tenant**, which is fine: the platform rule is same-*tenant*, not same-RG |
| App Service Plan | `spaarke-dev-plan` — **P1v3 / PremiumV3** | ✅ Confirms E9. Slots supported |
| Deployment slots | **0 exist** | Must be created before a slot-based rollout |
| Other web apps | `spaarke-provisioning-controlplane-dev` (Running, `rg-spaarke-platform-dev`) · `spaarke-bff-prod` (**Stopped**, `rg-spaarke-platform-prod`) | Prod is stopped; dev is the only live target, matching the r3 handoff |

### 2.1 Conflict resolved — `signInAudience`

[`config/spaarke-resources.yaml:105`](../../../config/spaarke-resources.yaml) records
`sign_in_audience: AzureADMyOrg`. **Live is `AzureADMultipleOrgs`.** The YAML is stale.

This matters beyond hygiene: [`TENANCY-AND-CREDENTIALS.md`](TENANCY-AND-CREDENTIALS.md) §4's working assumption
(**Reading 1** — one shared multitenant app registration for Model 1) leans on the app registration being
multitenant. That inference now rests on verified live state rather than on a stale inventory file. It is still
an inference — provisioning owns the decision — but the premise is sound.

**Action**: correct `config/spaarke-resources.yaml` (design §10 follow-up list).

### 2.2 One stale App Service reference — narrower than first assessed

The dev App Service is `spaarke-bff-dev` in `rg-spaarke-dev`. An earlier read of this session flagged
`config/spaarke-resources.yaml` as pointing at a phantom resource; on closer inspection **the inventory file is
substantially correct** — it records the 2026-05-24 Windows→Linux migration at `:62` and marks the old
`spe-api-dev-67e2xz` entry as `status: legacy` with a retirement date. That entry is deliberate history, not drift.

The genuine residue is one line: **`STAGING_APP_NAME: spe-api-dev-67e2xz`** (`:558`, annotated "Legacy; revisit
post-Linux-migration") is a **live GitHub Actions repo-secret mapping** pointing at a resource that no longer
exists. Worth filing, but it belongs to the CI estate, not to auth-v4.

The broader caution still stands and is already design §10 item 5: `docs/architecture/auth-azure-resources.md` is
materially stale — it claims system-assigned MI (live is user-assigned) and contradicts itself on which app
registration owns `BFF-API-ClientSecret`. **CLI-confirm every identifier before automating anything.**

One contradiction *does* resolve here: the live password credential is named `Dataverse-Checkout-20251218`, which
looks nothing like "BFF API secret" — but `config/spaarke-resources.yaml:298-300` states that the **Dataverse
application user shares the BFF API app registration and reuses the same secret**. The name is an artefact of
which consumer provisioned it. Same object, same secret, five config keys plus the lowercase KV alias.

### 2.3 Five UAMIs exist — pick deliberately

| UAMI | clientId | principalId | RG |
|---|---|---|---|
| **`mi-bff-api-dev`** ← the BFF's | `5967251e-…` | **`9fd47efb-…`** | `spe-infrastructure-westus2` |
| `spaarke-bff-identity` | `17a74f26-…` | `c8cdf6fc-…` | `SharePointEmbedded` |
| `insights-spaarkedev-uami` | `583ca2d8-…` | `9e4261c7-…` | `spe-infrastructure-westus2` |
| `insights-search-deploy-uami` | `4ceca18e-…` | `42ffe13e-…` | `spe-infrastructure-westus2` |
| `sprk-controlplane-dev-uami` | `965a4a01-…` | `38f7693f-…` | `rg-spaarke-platform-dev` |

`spaarke-bff-identity` is a **decoy** — the name reads as the BFF's identity but it is not attached to
`spaarke-bff-dev`. This is the "multi-identity ambiguity" the `ManagedIdentityCredentialFactory` comments refer
to. The FIC subject must be `9fd47efb-…` and nothing else; a wrong subject **creates successfully and fails only
at token exchange**.

## 3. The credential that was created

```
name:      mi-bff-api-dev-assertion
id:        66bac39a-71f4-40d2-b3fe-abd7f6b0f7d1
issuer:    https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0
subject:   9fd47efb-7962-492b-ac44-e5ccd0268ebb        # principalId (object ID) of mi-bff-api-dev
audiences: [ api://AzureADTokenExchange ]
```

**The shape to reuse per environment** — issuer is the *hosting tenant's* v2.0 OIDC endpoint, subject is the
UAMI's **principalId** (not its clientId, a common and silent error), audience is exactly
`api://AzureADTokenExchange`.

## 4. What Phase 0 still has to prove — and why it no longer blocks

Creating the FIC settles *provisioning feasibility*. It does not settle the one thing documentation cannot: that
**our** OBO chain works under a FIC-authenticated client. That still needs code running on compute that carries
the UAMI, because a UAMI assertion can only be minted from inside Azure — a developer workstation cannot produce
one.

| Must-prove | Status | Blocking? |
|---|---|---|
| FIC exists / admin available | ✅ **done** | No longer |
| App reg + UAMI same tenant | ✅ verified | No |
| UAMI-only identity on the App Service | ✅ verified | No |
| Slots available for staged rollout | ✅ plan supports; **0 created** | No — creating one is routine |
| **OBO → Graph/SPE under MI-FIC** | ⬜ | **The remaining spike** |
| **OBO → Dataverse `user_impersonation` under MI-FIC** | ⬜ | **The remaining spike** |
| Long-running OBO under MI-FIC | ⬜ | Spike |
| Ordered local-dev fallback degrades correctly | ⬜ | Spike — see the correction below |
| Power BI under MI-FIC | ⬜ | Spike (or adopt MI-as-principal) |
| Model 2 cross-tenant *resource* access | ⬜ | Spike; distinct from credential tenancy |

### 4.1 Correction carried into the spike design

The design's evidence **E4** — that adoption is "largely declarative" via `Microsoft.Identity.Web`'s ordered
`ClientCredentials` list — **does not hold for this codebase**. Verified by grep: there are **zero** occurrences
of `EnableTokenAcquisition`, `ITokenAcquisition`, `IDownstreamApi` or `ClientCredentials` in any `.cs` file. All
eight confidential clients hand-roll `ConfidentialClientApplicationBuilder`; `AddMicrosoftIdentityWebApi`
([`AuthorizationModule.cs:36`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs))
is **inbound token validation only**. And [`Spaarke.Dataverse.csproj:17`](../../../src/server/shared/Spaarke.Dataverse/Spaarke.Dataverse.csproj)
references only `Microsoft.Identity.Client` — no `Microsoft.Identity.Web` at all, so two of the sites cannot use
a config-bound mechanism even in principle.

**The spike must therefore prototype `.WithClientAssertion(Func<AssertionRequestOptions, Task<string>>)`** backed
by `ManagedIdentityClientAssertion` (`Microsoft.Identity.Web.Certificateless`, **not currently referenced** — a
small publish-size add), not an appsettings edit. The ordered-fallback behaviour that the rollback plan depends
on must be **built** into the provider seam and tested, not inherited from the framework.

## 5. Recommended way to run the remaining spike

Two options; both keep the running dev BFF untouched.

| | **A. Dev deployment slot** | **B. Throwaway App Service** |
|---|---|---|
| Fidelity | High — same plan, same config surface, same UAMI | Medium — must replicate config |
| Risk to `spaarke-bff-dev` | Low; a slot is isolated until swapped, and we will not swap during the spike | None |
| Cost | None (same P1v3 plan) | Small, and must be torn down |
| Doubles as | Phase 4's rollout mechanism — the slot is needed anyway | Nothing |
| Setup | Create slot, assign `mi-bff-api-dev`, deploy spike branch | Create app + plan + identity + config |

**Recommendation: A.** The slot is required for Phase 4 regardless (§6), dev is P1v3, and zero slots exist today —
so creating it is work that has to happen anyway and is better done now, when nothing depends on it.

**Constraint that still stands**: no in-session flips. `#3b` attempt 1 took dev down (SIGABRT from an eager
connect under `ValidateOnBuild`). The slot gets deployed, exercised, and read — it does not get swapped as part
of the spike.

## 6. Reversal

```bash
az ad app federated-credential delete \
  --id 1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --federated-credential-id 66bac39a-71f4-40d2-b3fe-abd7f6b0f7d1
```

Deleting it restores the exact prior state. Nothing consumes the credential until the provider seam ships, so
there is no window in which removal breaks a running path.

## 7. Known behaviour to expect

> ⚠️ **CORRECTED 2026-08-21 (task 030, measured).** The bullet below names `AADSTS70021` as the
> propagation code. Against this tenant it is **`AADSTS70025`**, and `70021` was never observed.
> Propagation also **flaps** — 8 failures interleaved with successes over ~130 s — rather than failing
> cleanly then succeeding. Separately, a **wrong subject** returns **`AADSTS700213`**, not `70021`, so
> the two cases are distinguishable at the exchange (an assumption to the contrary was built into task
> 030's first design and had to be corrected). Evidence:
> [`decisions/030-fic-automation.md`](decisions/030-fic-automation.md) §11.

- **`AADSTS70021` for the first few minutes** after FIC creation is normal propagation delay, **not** evidence
  that MI-FIC and OBO are incompatible. Retry before concluding anything. This credential was created
  2026-08-19; by the time the spike runs, propagation is long settled.
- **Misconfiguration is silent.** A wrong issuer, subject or audience creates successfully and fails only at
  exchange, with a generic error. §2.3 is the trap.
