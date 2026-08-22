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

## What actually argues for moving the Security path

Not exposure — **modeling**.

Secure score and security alerts are **tenant-wide** data. `SecurityEndpoints` resolves them through
`GetClientForConfigAsync(config)`, i.e. as the **owning app of a container-type config**. The config
exists to say *which app owns this container type*; it has no relationship to tenant security posture.

**The tell**: with two customer configs in one environment, which customer's owning app should read the
*tenant's* secure score? The question has no answer. That is a modeling error, and it holds whatever the
credential story is.

`GetSecurityAlertsForConfigAsync` / `GetSecureScoreForConfigAsync` (`SpeAdminGraphService.cs`) are the
two methods; `SecurityEndpoints.cs` is already correct otherwise — its 403 handler was repaired by task
001 and names the missing grant as a *hint*, not a certainty.

### The alternative home

`SDAP-BFF-SPE-API` (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) already holds app-only
`Directory.ReadWrite.All`, `AppRoleAssignment.ReadWrite.All`, `User.ReadWrite.All`,
`Group.ReadWrite.All`, `Files.ReadWrite.All`, `Mail.Read`, `Mail.Send`,
`FileStorageContainer.Selected`, `FileStorageContainerTypeReg.Selected`,
`Files.SelectedOperations.Selected`.

Against that, a **read-only** `SecurityEvents.Read.All` is a marginal addition to an identity that is
already tenant-scoped by design — which is what tenant-wide data wants.

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

**Step 1 complete** (which registration, and confirmation the permission is absent). **Step 2 — the
grant — is NOT done**: where it belongs depends on whether the Security path moves to the BFF, which is
an operator decision. Nothing has been granted, and no broader permission was granted speculatively —
the POML's second escalation trigger is respected.
