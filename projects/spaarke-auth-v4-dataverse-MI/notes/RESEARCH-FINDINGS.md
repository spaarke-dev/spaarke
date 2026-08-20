# Research Findings — Zero-Secret BFF (MI-FIC vs certificate vs status quo)

> **Status**: RESEARCH COMPLETE · **Date**: 2026-08-17 · **Method**: three parallel Fable-model investigations
> (Microsoft platform docs · exhaustive codebase audit · internal ADR/docs/cross-project reconciliation),
> plus **live Azure tenant verification** by the main session.
> **This document supersedes [`ASSESSMENT.md`](ASSESSMENT.md)** where they disagree. The seed remains as the origin record.
> **Companion**: [`CREDENTIAL-INVENTORY.md`](CREDENTIAL-INVENTORY.md) — the full `file:line` credential audit.

---

## 0. Bottom line

The seed's central hypothesis — *OBO can be secret-free via Managed Identity as a Federated Identity Credential* —
is **confirmed, and the ground is far more favorable than the seed assumed**:

1. **MI-as-FIC is GA** (announced 2025-05-08), not preview. Our own researcher memory still says "preview" — stale.
2. **Microsoft now publishes an explicit credential ranking**: certificateless (MI-FIC) = *Highest*; certificate =
   fallback; **client secrets = "Development and testing only"**. Entra's app-security best-practice doc says
   plainly: *"Don't use password credentials, also known as secrets."*
3. **Adoption is largely declarative**, not surgical. `Microsoft.Identity.Web` supports
   `ClientCredentials: [{ SourceType: "SignedAssertionFromManagedIdentity" }]` in appsettings, with **ordered
   fallback** entries (MI-FIC → KV cert → dev secret) — which also solves the local-dev problem.
4. **The BFF app registration already has a working federated identity credential** (GitHub Actions OIDC) and the
   App Service already runs **user-assigned MI only** — the two hard platform prerequisites are already satisfied
   *in our tenant, on this exact app registration*. (§3)
5. **No downstream service constrains the credential type.** Dataverse, Graph/SPE, Power BI and Azure OpenAI all
   validate the resulting Entra token; how the client authenticated is invisible to them.

**But the scope is bigger than the seed drew it**: 8 confidential-client sites (not 5), 5 config keys plus a
lowercase KV alias (not 3), a DI-lifetime hazard that must be fixed first, 46 test fixtures to keep green, and a
**cross-tenant tenancy rule** that directly constrains customer provisioning Model 2.

**And one finding inverts the seed's framing of the ADR work**: ADR-028 does **not** document the OBO secret as an
exception at all. Its exception registry contains only E-1 (SpeAdmin) and E-2 (Azure OpenAI), and its own preamble
requires every MI exception to be enumerated there by PR. Strictly read, **the OBO secret is today an undocumented
violation of ADR-028's own MUST — so even "do nothing" requires an ADR edit.** That removes "status quo is free"
from the option set.

---

## 1. Platform research (all verified against live Microsoft docs, 2026-08-17)

### 1.1 MI-as-FIC and OBO

| Finding | Evidence | Confidence |
|---|---|---|
| MI-as-FIC is **GA since 2025-05-08**; the how-to carries no preview banner | [GA blog](https://devblogs.microsoft.com/identity/access-cloud-resources-across-tenants-without-secrets-ga/) · [how-to](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity) (ms.date 2025-06-06, site 2026-06-15) | High |
| **OBO works with a FIC-authenticated client.** The assertion is a standard `client_assertion` — identical parameter to the certificate case; the token service does not distinguish signing origin | [Agent ID OBO doc](https://learn.microsoft.com/en-us/entra/agent-id/agent-on-behalf-of-oauth-flow) (updated 2026-08-10) shows the wire protocol: `client_assertion={T1 from MI}` + `requested_token_use=on_behalf_of`, and states *"The client credential can be a client secret, a client certificate, or a Federated Identity Credential (FIC). When possible, use a managed identity to obtain the FIC."* | High |
| The canonical OBO protocol doc still documents only secret + certificate — **it lags**; no doc anywhere excludes FIC clients from OBO. OBO's documented client limitations concern app-only assertions, custom signing keys, and `/common` — not credential type | [v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow) | High (absence of restriction), Medium (no first-party OBO+FIC sample) → **smoke test required** |
| Nothing newer supersedes MI-FIC. Flexible FICs are preview and **explicitly not available for managed identities**; Entra Agent ID is preview and itself prefers MI-as-FIC | [flexible FIC](https://learn.microsoft.com/en-us/entra/workload-id/workload-identities-flexible-federated-identity-credentials) · [Agent ID](https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id) | Med-High |

**Hard constraints to design around** (from the Entra how-to + considerations docs):

- **User-assigned MI only.** (Identity.Web docs show a system-assigned variant — treat as doc inconsistency; plan UAMI.)
- **The UAMI and the app registration must be in the same tenant.** Cross-tenant *resource* access is supported via
  a multitenant app provisioned into the other tenant. **Cross-cloud is not supported.**
- **Audience is exactly `api://AzureADTokenExchange`** (sovereign clouds differ).
- **Max 20 FICs** per app registration and per UAMI.
- **Propagation delay**: new FICs can return `AADSTS70021` for minutes — retry logic required.
- **Misconfiguration is silent**: a wrong issuer/subject/audience creates successfully and fails only at exchange.

### 1.2 The API surface

Recommended, config-driven (`Microsoft.Identity.Web`, ms.date 2026-04-19):

```json
"AzureAd": {
  "ClientCredentials": [
    { "SourceType": "SignedAssertionFromManagedIdentity",
      "ManagedIdentityClientId": "<UAMI client id>",
      "TokenExchangeUrl": "api://AzureADTokenExchange/.default" }
  ]
}
```

Multiple entries give **ordered fallback** — the documented pattern for environment portability, and our answer to
local dev. Direct MSAL: `WithClientAssertion(Func<AssertionRequestOptions, Task<string>>)`, or the first-class
`ManagedIdentityClientAssertion` helper in **`Microsoft.Identity.Web.Certificateless`** (caches the signed
assertion until expiry — reuse the instance). Azure.Identity equivalent: `ClientAssertionCredential`.

### 1.3 Microsoft's current credential ranking (this is now shipped guidance, not blog posture)

| Credential | Microsoft's stated position |
|---|---|
| **Certificateless (MI-FIC)** | *"Highest"* security · *"No rotation — Azure manages lifecycle"* · *"Meets zero-trust requirements"* · decision tree: "Running on Azure + can use MI → certificateless (recommended)" |
| **Certificate** | The fallback *when MI isn't available* |
| **Client secret** | *"Low… Development and testing only"*; Entra best practices: *"Don't use password credentials, also known as secrets."* |

Sources: [Identity.Web credentials overview](https://learn.microsoft.com/en-us/entra/msidweb/authentication/credentials-overview) · [Entra app security best practices](https://learn.microsoft.com/en-us/entra/identity-platform/security-best-practices-for-app-registration).

**Policy pressure**: no announced global retirement of secrets, but **app management policies are GA** and let any
tenant block secret creation (`passwordAddition`) or cap `maxLifetime`. For a multi-tenant ISV this is a
**portability risk, not just hygiene** — a customer tenant can refuse secrets on your service principal.
*Not verified*: any Microsoft-mandated enforcement date (third-party blogs claiming one describe example configs).

### 1.4 Downstream services — none constrain the credential

- **Dataverse**: no native "managed identity application user" for *inbound* access; the supported zero-secret
  pattern is exactly app-reg + FIC, with `ServiceClient`'s `tokenProviderFunction`. Dataverse only sees a bearer
  token. (Note: "Power Platform managed identity" is the *outbound* plug-in direction — do not confuse them.)
  The OAuth doc now carries the SFI-era line: *"only use this flow when other more secure flows, such as managed
  identities, aren't viable."* Its "Connect as an app" section still lists only secret/certificate — **docs lag the
  Entra-side capability**. Confidence: high on mechanism, medium that no first-party page blesses the full chain.
- **SharePoint Embedded**: no credential-type constraint — *"SharePoint Embedded supports all Microsoft Entra
  service principal types."* Delegated (OBO) remains Microsoft's stated preference. **The historical cert-only
  pocket is gone**: container-type registration moved to Graph v1.0 (`FileStorageContainerTypeReg.Selected`).
- **Power BI**: certificate auth is documented, **and a UAMI can replace the service principal outright** —
  *"we recommend that you use a user-assigned managed identity"*. MI-FIC on an existing PBI app-reg is undocumented
  (expected to work; prototype or switch to the MI-as-principal model).
- **Azure OpenAI**: Entra/MI auth is **fully supported for inference** (doc refreshed 2026-08-04). The classic
  "MI returns 401" root cause is documented as a **missing custom subdomain** (regional endpoints reject Entra
  tokens), plus `Cognitive Services OpenAI User` per account and ≤5 min role propagation. No data-plane operation
  is documented as still requiring an API key.

---

## 2. Live tenant verification (this session, `az` read-only)

This is the decisive de-risking evidence — the platform prerequisites are already met **on the exact app
registration and App Service in question**.

| Check | Result | Why it matters |
|---|---|---|
| BFF app registration | `SDAP-BFF-SPE-API` = `1e40baad-e065-4aea-a8d4-4b7ab273458c`, tenant `a221a95e-…` | The OBO confidential client |
| **Existing federated credentials** | ✅ **One already exists** — `github-actions-deploy-staging`, issuer `token.actions.githubusercontent.com`, audience **`api://AzureADTokenExchange`** | **Workload identity federation is already live and working on this app registration.** Adding an MI-issuer FIC is the same object type, different issuer — not new infrastructure |
| FIC headroom | 1 of 20 used → **19 free** | The 20-FIC cap is not binding for dev/demo/prod |
| Password credentials | **1**, expiring **2027-12-19** | No expiry emergency; this is the secret to eliminate |
| Key credentials | **0** | No certificate on the BFF app reg today (the CIAM cert is a different app) |
| `signInAudience` | **`AzureADMultipleOrgs`** (multitenant) | Exactly the supported MI-FIC cross-tenant shape, and matches provisioning's shipped design |
| App Service identity | **`UserAssigned` only** — `mi-bff-api-dev`, clientId `5967251e-…`, principalId `9fd47efb-…` | MI-FIC requires UAMI. ✅ Already satisfied; no system-assigned identity to trip over |
| `Graph__ManagedIdentity__Enabled` | `true` on dev | #3b is live |
| **Dev App Service Plan** | **`spaarke-dev-plan` = P1v3 (PremiumV3)** | ⚠ **Corrects the IaC**: `stacks/dev.bicepparam:12` says `B1` (Basic, *no slots*). Live is Premium → **up to 20 slots available**. Slot-based rollout IS possible on dev; no plan upgrade needed. No slots exist yet — they must be created |

*(`spaarke-bff-prod-plan` is B1/Basic, but prod/demo are decommissioned per the r3 handoff — dev is the only live target.)*

---

## 3. Corrections to the seed assessment

**Wrong or outdated:**

1. **"ADR-028 §24 documents OBO-secret retention"** — no numbered §24 exists (the convention refers to *line 24*),
   and more importantly **ADR-028 does not document the OBO secret at all**. Its exception registry is E-1 (SpeAdmin)
   + E-2 (Azure OpenAI) only, and its preamble states adding an exception *"requires a PR that updates this list."*
   The OBO retention lives in `.claude/constraints/auth.md:108`, `Sprk.Bff.Api/CLAUDE.md:110,221`, and the guides.
   **Consequence: the status-quo option is not free — it requires adding exception E-3.**
2. **The load-bearing wrong sentence** is `.claude/constraints/auth.md:108`: *"OBO flow (OAuth spec requires
   confidential client + secret)."* OAuth requires a confidential **credential**, not a secret. This one clause is
   what foreclosed the question in every prior audit.
3. **`customer-provisioning-orchestration-r1` is NOT design-phase** — PR **#779**, ~68% executed. H3 (per-customer
   app registration), H4 (KV secret population + rotation handler), H9 (blue-green slot deploy), H10 (Dataverse app
   user) and the UAMI Bicep are **already implemented on that branch**. Auth-v4's outcome lands as a **change
   request against shipped handlers**, so coordination is more urgent than the seed implies.
4. **The seed's Model-2 framing is inaccurate.** Provisioning's shipped design is a **multitenant app
   (`AzureADMultipleOrgs`) consented into the customer tenant** — the app object and its credentials stay
   Spaarke-side. The genuinely hard case is narrower: a *customer-owned Azure subscription* stamp whose UAMI issues
   from the customer's tenant. Provisioning already logged this as risk **R23** (design.md:1429) five days before
   the seed, including the 20-FIC cap — so "MI-FIC was never on the table" is not quite true repo-wide.
5. **`AgentToken:ClientSecret` is not a separate secret** in template-conformant environments — it is a fifth config
   key onto the same `BFF-API-ClientSecret` (`CONFIGURATION-MATRIX.md:319`, `Reconcile-DemoEnvironment.ps1:76`).
   `PowerBi:ClientSecret` *is* genuinely separate.

**Understated or missing:**

6. **8 confidential-client sites, not 5** — `SpeAdminTokenProvider` (OBO, per-owning-app KV secrets) and
   `SpeAdminGraphService` (app-only, per-BU secrets) were absent from the seed.
7. **A sixth secret path**: duplicate lowercase KV alias **`bff-api-client-secret`** used by the Office add-in
   deploy. Any removal that ignores it breaks the add-in.
8. **A live gating defect**: `DataverseAccessDataSource` and `DataverseWebApiClient` **never read
   `Graph:ManagedIdentity:Enabled`** — secret *presence* alone selects the secret path. On dev, where
   `API_CLIENT_SECRET` is set, they run on the secret today despite MI being enabled. This is a pre-existing bug
   worth fixing regardless of which option wins.
9. **DI lifetime hazard**: `DataverseAccessDataSource` is transient and `AgentTokenService` scoped → a fresh MSAL
   client per request. Client assertions need shared/cached clients. `DataverseUserClient.cs:55-56,91` already has
   the static-cache pattern to copy. **This must be fixed before or with the credential swap.**
10. **Slot rollout prerequisite** — the seed proposed slot-based rollout without checking plan tier. IaC says B1
    (no slots); **live dev is P1v3, so slots are available**. Resolved in our favor, but the IaC drift itself
    should be filed.
11. **No FIC automation exists anywhere** — exhaustively verified (unrestricted filesystem grep including
    gitignored/untracked paths): **zero** repo automation creates a federated identity credential — neither an
    app-registration FIC nor a `Microsoft.ManagedIdentity/.../federatedIdentityCredentials` Bicep resource — for
    the BFF runtime identity or anything beyond GitHub Actions deploy OIDC, **and even that one was hand-run**
    (`.github/D-11:61`, requires Application Administrator; setup documented at
    `docs/guides/redis-cache-azure-setup.md:306` and `.github/ENVIRONMENTS.md:53-69`). The only other trace is a
    pre-authorized permission allowlist in `.claude/settings.local.json:205,207`
    (`az ad app federated-credential list/create`) that **nothing invokes**. CIAM *certificate provisioning* also
    appears manual. **Both secret-free options carry a to-be-built automation cost the seed doesn't price in** —
    and note the existing GitHub OIDC FIC is proof the *operation* works in this tenant, not that it is automated.
12. **Plugins store client secrets in plaintext Dataverse columns** (`sprk_externalserviceconfig`) — outside Key
    Vault entirely, and outside the seed's inventory.
13. **Doc hazard**: `docs/architecture/auth-azure-resources.md` is materially stale (says system-assigned MI;
    contradicts itself on which app-reg owns `BFF-API-ClientSecret`), and three identities are routinely confused
    (`1e40baad` app-reg / `9fd47efb` UAMI / retired `56ae2188`). Portal-confirm before automating any removal.

**Confirmed accurate**: the shared-secret finding; the `DataverseOptions.ClientSecret [Required]` + ValidateOnStart
startup-crash consequence; #3b's scope, live status and retained fallback; that `// No OBO support with managed
identity` describes wiring, not a platform limit; the CIAM certificate precedent; the OBO fail-closed blast radius;
the research-first, no-in-session-flip posture (validated by #3b attempt 1's dev outage); and the fair judgment that
prior audits deliberately retained the secret rather than missing it.

---

## 4. Option analysis

| | **A. MI-FIC** | **B. Certificate** | **C. Status quo (secret)** |
|---|---|---|---|
| Microsoft's ranking | **Highest** (recommended when on Azure) | Fallback when MI unavailable | *"Development and testing only"* |
| Rotation burden | **None** — Azure-managed | Rotate before expiry (KV can automate) | Rotate + redeploy, per customer, forever |
| In-repo precedent | FIC object already live on this app reg (GitHub OIDC); UAMI already the only identity | ✅ `CiamGraphClientFactory` cert path is proven production code | ✅ current state |
| Code change | Config-first (`ClientCredentials` ordered list) + provider seam + lifetime fixes | Same code shape, different `SourceType`/`WithCertificate` | None |
| Local dev | Needs the ordered-fallback entry (MI-FIC → cert → user-secret) | Same | Works today |
| Provisioning impact | H4 drops secret creation + the 24-month rotation ceremony entirely; H3 gains a per-customer FIC step (**no automation exists yet**) | H3/H4 provision + rotate a per-customer cert (CIAM is the model); rotation lifecycle persists | No change; per-customer rotation is a permanent operating cost |
| Customer-tenant policy risk | Immune (no secret to block) | Low | **Exposed** — tenants can block/limit secrets on the SP |
| Hard constraint | UAMI + app reg must share a tenant; cross-cloud unsupported; 20-FIC cap | None material | — |
| ADR work required | Amend line 24 + add A4 | Amend line 24 (non-secret credential) | **Still required** — add exception E-3 |

**Preliminary reading (to be ratified by the spike, not pre-decided):** Option A is favored on every documented
axis, and the two prerequisites that would normally make it risky — UAMI-only identity and a working FIC on the app
registration — are **already true in our tenant**. Option B is the credible fallback with in-repo proof. Option C is
no longer a null-cost choice.

The decision should still gate on the §5 spike, because the one thing documentation cannot settle is whether
**our** OBO chain works end-to-end under a FIC-authenticated client.

---

## 5. Must-prototype (what docs cannot settle)

1. **End-to-end OBO smoke test under MI-FIC** — SPA token → BFF `AcquireTokenOnBehalfOf` → (a) Graph/SPE and
   (b) Dataverse `user_impersonation`. The canonical OBO doc has no FIC example; only the Agent ID docs show it.
   Include long-running OBO (`InitiateLongRunningProcessInWebApi` + distributed cache).
2. **Dataverse inbound MI-FIC** — no first-party page documents UAMI→FIC→app-reg→Dataverse app user end-to-end
   (community-proven only). Test both `ServiceClient` `tokenProviderFunction` and raw `HttpClient` under refresh.
3. **Local-dev credential story** — verify the ordered `ClientCredentials` fallback actually degrades to a dev
   credential without an MI present, including for OBO.
4. **Power BI** — MI-FIC on the existing SP is undocumented; prototype, or adopt the documented MI-as-principal model.
5. **Azure OpenAI E-2 re-test** — check whether `spaarke-openai-dev` has a **custom subdomain**; if not, that is the
   documented root cause and the fix is independent of everything else here.
6. **Identity topology for provisioning** — per-customer app reg (current H3 design) vs shared; the 20-FIC cap and
   the same-tenant rule bind differently. Must be settled *with* provisioning, against Model 1 and Model 2.
7. **Package versions** — pin `Microsoft.Identity.Web` / `.Certificateless` minimums at implementation time; docs
   state no minimums. Note open PR **#293** bumps `Azure.Identity` 1.17.1→1.21.0 — relevant and probably wanted.

---

## 6. Sequencing and coordination

1. **Now (zero conflict)**: research spike + ADR-028 amendment decision — read-only.
2. **Prefer letting `dataverse-access-unification-r1` land first — but it is NOT a dependency**
   (corrected 2026-08-19). It deletes `DataverseWebApiService` + `DataverseWebApiClient`, both of which read the
   secret, but **both already degrade to MI when it is absent** (`DataverseWebApiService` is flag-gated and never
   reads it with MI on; `DataverseWebApiClient` falls through to `DefaultAzureCredential` at `:50-52`).
   **Neither blocks removal of `BFF-API-ClientSecret`** — the real blockers are the no-fallback paths listed in
   §5/§0 (`DataverseOptions` `[Required]` startup crash, `GraphClientFactory`, `DataverseAccessDataSource`,
   `DataverseUserClient`, `AgentTokenService`). The genuine sequencing reasons are narrower: contention on
   `Spaarke.Dataverse`, avoiding churn on classes about to be deleted, and halving the T3 gating-fix scope.
   It does not touch `GraphClientFactory` (it does repoint `GraphModule.cs`). If it stalls, auth-v4 proceeds —
   serialize PRs and run `/conflict-check` on each.
3. **Coordinate with `customer-provisioning-orchestration-r1` immediately, not later** — H3/H4 are shipped on
   PR #779. Minimum ask: keep the "configure BFF confidential credential" step **pluggable**, and pull provisioning's
   Model-1/Model-2 constraints and risk R23 into the spike as first-class input.
4. **Fix the DI lifetimes and the MI-flag gating defect first** — they are prerequisites, independently correct, and
   safely separable from the credential swap.
5. **Then** the staged credential migration per environment, slot-based (dev P1v3 supports it), with explicit
   rollback. No in-session flips — #3b attempt 1 took dev down.

## 7. Follow-ups to file regardless of the option chosen

- Correct `.claude/constraints/auth.md:108` (the "OAuth spec requires a secret" over-claim).
- Fix the MI-flag gating defect in `DataverseAccessDataSource` + `DataverseWebApiClient`.
- Update the stale researcher memory that records MI-as-FIC as preview.
- Reconcile `stacks/dev.bicepparam` (B1) with live (P1v3) — IaC drift.
- Refresh `docs/architecture/auth-azure-resources.md` (system-assigned MI claim; app-reg↔secret contradiction).
- Rotate the live Service Bus SAS key present in a local `appsettings.Development.json`.
- Re-test ADR-028 **E-2** (Azure OpenAI): check for a custom subdomain first.
- File the plaintext-secrets-in-Dataverse-columns issue for `BaseProxyPlugin`.
