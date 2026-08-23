# App-registration topology — what `SDAP-PCF-CLIENT` actually is

> **Found during task 013** (spec FR-B04) · 2026-08-22 · verified live against the Spaarke Dev tenant
> **No secret value, token, or assertion appears in this file.** App ids, tenant ids, role ids and
> credential *key ids* are public identifiers, not credentials.

---

## Why this exists

Task 013 says "grant `SecurityEvents.Read.All` to the app registration". Tracing which registration the
Security screen actually authenticates as turned up something worth writing down: **`SDAP-PCF-CLIENT`
is four things at once**, and one of them is a Microsoft sample app.

---

## The registration

`SDAP-PCF-CLIENT` — `170c98e1-d486-4355-bcbe-170454e0207c` · `signInAudience: AzureADMultipleOrgs` ·
`identifierUris: []` · **exposes no scopes**

| Hat | Evidence |
|---|---|
| **Shared browser client for every Spaarke surface** | Dataverse env var `sprk_MsalClientId` → `@spaarke/auth` `resolveRuntimeConfig` → SpeAdminApp, AllDocuments, WorkspaceLayoutWizard, `useWizardPageBootstrap`, `sprk_DocumentOperations.js`, `sprk_emailactions.js`. `resolveRuntimeConfig.ts:276` documents `msalClientId → "170c98e1-…"` |
| **SPE owning app** | `sprk_specontainertypeconfig.sprk_owningappid` = this app (task 010 ledger). Holds 5 app-only Graph roles |
| **Confidential client** | secret + certificate; the **BFF reads a secret from it** — `SPE.BFF.API-SECRETS-SETUP.md:55`, `Graph:ClientSecret` |
| **Microsoft sample app (origin)** | credential display names `SharePointEmbeddedVSCode` and `CN=SharePoint Embedded VS Code Ext` — created by the SharePoint Embedded VS Code extension, then adopted |

Platform config: `spa.redirectUris` → the Dataverse orgs + localhost; `web.redirectUris` → Postman
callbacks with **implicit grant enabled** (`enableAccessTokenIssuance` + `enableIdTokenIssuance`).

### App-only Graph roles held

`FileStorageContainer.Selected` · `FileStorageContainerTypeReg.Selected` · `Files.ReadWrite.All` ·
`Files.SelectedOperations.Selected` · `Files.ReadWrite.AppFolder` — exactly matching the roles task 010
independently observed in an app-only token. Two sources, same answer.

**`SecurityEvents.Read.All` (`bf394140-e372-4bf9-a898-299cfc7564e5`) is NOT granted**, on this
registration or on the BFF. Task 013's premise is confirmed.

---

## ⚠️ A correction, recorded so it is not repeated

An earlier draft of this analysis argued that granting an app-only role here would "put tenant-wide
security-read on the identity every browser signs into." **That is wrong, and the operator caught it.**

**Application permissions are unreachable from a browser.** App-only roles are obtainable only through
`client_credentials`, which requires the secret or certificate. A browser signing in as `170c98e1`
receives a *delegated* token bounded by the signed-in user's own rights; it cannot mint an app-only
token regardless of which app roles the registration carries. **The exposure boundary is the credential,
not the registration.**

Related framing also worth stating precisely: "public client" in OAuth means *cannot keep a secret*, not
*reachable from the internet*. SPE Admin runs only inside the Dataverse harness (MDA custom page / XRM
chrome wrapper), which constrains **who can load the page** — but the bundle still executes in the
user's browser, so it still cannot hold a secret. Both things are true, and neither implies the other.

---

## ❌ RETRACTED — the "modeling error" argument was wrong

An earlier version of this note argued that routing tenant-wide security data through a container-type
config's owning app was a modeling error, and that `SecurityEvents.Read.All` belonged on the BFF. **That
is wrong. Retained here, struck, because the reasoning was persuasive and should not be re-invented.**

The argument was: *secure score is tenant-wide, so with two customer configs, which one's owning app
should read "the" tenant's score? No answer ⇒ modeling error.*

**The hidden premise was one tenant per Spaarke environment. It is false.** A Spaarke environment can
manage container types living in **customers' own Entra tenants** (operator-confirmed 2026-08-23) —
which is exactly why `sprk_speenvironment` carries `sprk_tenantid` and why `GetClientForConfigAsync`
threads it through. So the config selection **does** determine the answer: it selects *whose tenant* is
being read. The wiring is correct.

And the proposed fix was not merely worse — it was **unworkable**. `IGraphClientFactory.ForApp()`
authenticates in the BFF's own home tenant, so it could never read a customer tenant's secure score.

**The POML's literal instruction — grant to the owning app — was right.** Executed and verified; see
[`security-grant-record.md`](security-grant-record.md).

### What the multi-tenant model means instead

The consequence is not a code change but an **onboarding obligation**: every customer tenant needs
`SecurityEvents.Read.All` granted and admin-consented on **that customer's** owning app. Recorded in
[`docs/guides/auth-deployment-setup.md`](../../../docs/guides/auth-deployment-setup.md) **§5e**.

### It also partly rehabilitates ADR-028 E-1

Task 010 concluded *"there is no per-customer owning app in this environment"* — true of **Spaarke Dev**,
which is Spaarke's *own* tenant, so the owning app and the shared browser client collapse onto one
registration (`170c98e1`). In a real customer tenant they are distinct, so **E-1's concept is real**; the
Spaarke Dev collapse is an artifact of dogfooding, not evidence the model doesn't exist.

**Task 010's OBO verdict is untouched.** That finding rests on assertion audience: the code page signs in
against the BFF, so the assertion the BFF receives always carries `aud = 1e40baad` (the BFF). MSAL OBO
requires the exchanging client to be that audience, so `Create(OwningAppId)` fails **even with a
genuinely separate per-customer owning app**. Path A (BFF-identity OBO) remains correct.

---

## ✅ Done — expired credential cleanup (operator-authorized 2026-08-22)

Both removed credentials were **already expired**, so neither could authenticate anything; removal
cannot break working functionality.

| Removed | Type | Expired | keyId |
|---|---|---|---|
| `SharePointEmbeddedVSCode` | secret | 2025-11-22 (9 months) | `3ae36063-a8a4-4153-a201-548c6d9f2275` |
| `CN=SharePoint Embedded VS Code Ext` | certificate | 2026-03-14 (5 months) | `cde620c7-9d67-4a31-9fd9-ca7db06f442d` |

**Retained and verified after cleanup:**

| Retained | Type | Valid to |
|---|---|---|
| `SPE Dev 2 Functions Secret` | secret | 2027-09-22 |
| `CN=SDAP-SPE-Owner-Renewed-2026` | certificate | 2027-07-19 |

---

## 🔔 Open — belongs to auth-v4, not to R2

**One registration is simultaneously a shared browser client and a confidential app-only identity.**
The narrower, defensible concern is **blast radius of the single live secret**: every app role on
`170c98e1` is reachable by whoever holds it, so each role added widens what that one secret can do. The
BFF holding a secret belonging to a *different* registration is its own oddity, and sits close to
**ADR-028 A4** (BFF-identity confidential clients should use MI-FIC or a Key Vault certificate).

A per-app "SPE Admin registration" would **not** fix this — it adds another registration without
separating the two roles. The split that matters is by **credential type**:

| Identity | Should hold |
|---|---|
| Shared browser client | delegated only — no secret, no certificate, no app-only roles |
| SPE owning app (per customer) | app-only SPE container roles + its confidential credential |
| BFF | OBO + backend app-only, on MI-FIC / KV certificate per ADR-028 A4 |

Also outstanding on `170c98e1`: **implicit grant is enabled** (`enableAccessTokenIssuance` +
`enableIdTokenIssuance`) alongside Postman redirect URIs — deprecated, and unnecessary now that MSAL
uses auth-code + PKCE.

→ Filed for **`spaarke-auth-v4-dataverse-MI`**, which already owns credential migration. **Not R2 scope**
— recorded here so the next project does not re-derive it.

Note this also revises the premise auth-v4 recorded at its `design.md:149` — that
`SpeAdminTokenProvider` / `SpeAdminGraphService` are out of scope because they "authenticate
per-customer *owning applications*". The owning app is `SDAP-PCF-CLIENT`, the shared browser client.
Same false premise task 010 found under ADR-028 **E-1**; it appears in two places now.

---

## Status of task 013

**Done 2026-08-23.** `SecurityEvents.Read.All` granted + admin-consented on `170c98e1` in the Spaarke
tenant; exactly one permission added, verified by before/after diff. Secure Score returns **200** live.
`alerts_v2` still 403s — but with a **different, non-permission cause** (Defender not provisioned),
escalated rather than papered over with a broader grant. Full record:
[`security-grant-record.md`](security-grant-record.md).
