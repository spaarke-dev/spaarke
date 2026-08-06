# Multitenant Workforce Entra App + Admin-Consent Onboarding (Task 061)

> **Date**: 2026-08-03
> **Task**: `061-multitenant-entra-consent.poml` · **Rigor**: STANDARD · **Tier**: sonnet @ high
> **Status**: 🟢 **LIVE CONFIG APPLIED** (Azure auth was available this session) — see §1.
> **App**: `SDAP-BFF-SPE-API`, client ID `1e40baad-e065-4aea-a8d4-4b7ab273458c` (**REUSED**, not re-registered).

---

## 0. TL;DR

| Item | Value |
|---|---|
| Azure auth available this session | ✅ Yes (`az account show` succeeded — signed in as `ralph.schroeder@spaarke.com`, tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2` / spaarke.com) |
| Multitenant setting | ✅ **Applied live**: `signInAudience` `AzureADMyOrg` → **`AzureADMultipleOrgs`** ("Accounts in any organizational directory") |
| CIAM enabled on this app | ❌ Not touched, not enabled (out of scope — CIAM is the External SPA's separate posture per Amendment A1) |
| Least-privilege delegated scope for Teams SSO/NAA | **`access_as_user`** (existing scope, id `7e9e1e5a-3b0b-4153-9753-85b41d48c6fe`) — reused, no new scope created |
| Scopes removed | None (removal would break other consumers of this shared app — see §2.3) |
| `webApplicationInfo` for task 070 | `id = 1e40baad-e065-4aea-a8d4-4b7ab273458c`, `resource = api://1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| Admin-consent onboarding flow | Documented in §3 — **scoped** v2 admin-consent URL (not the blanket `/adminconsent` endpoint) |

---

## 1. Live-config change applied (§ Step 1 of the task)

Azure CLI auth was live and working this session (verified via `az account show`), so per the task's live-config boundary instructions this section was **applied live**, not merely authored for an operator.

### 1.1 Before

```json
// az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c
{
  "appId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "signInAudience": "AzureADMyOrg",   // single-tenant (Spaarke's own tenant only)
  ...
}
```

The app already carried the Teams-SSO prerequisites from the Copilot project (D7) and task 001's foundation spike:
- `web.redirectUris` already includes `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` and `https://teams.microsoft.com/api/platform/v1.0/oAuthConsentRedirect`.
- `api.oauth2PermissionScopes` already exposes `access_as_user` (delegated, type `User`).
- `identifierUris` already includes the clean `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` App ID URI (a second, auto-generated `api://auth-3e04ab58-.../1e40baad-...` URI also exists — **not** used for `webApplicationInfo`, see §4).

Only `signInAudience` needed to change to realize the multitenant workforce posture (D6 / Amendment A2).

### 1.2 Command applied

```bash
az ad app update --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --sign-in-audience AzureADMultipleOrgs
```

Exit code `0`, no errors.

### 1.3 After (verified)

```json
// az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "{appId,signInAudience,identifierUris,webRedirects:web.redirectUris,spaRedirects:spa.redirectUris}"
{
  "appId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "signInAudience": "AzureADMultipleOrgs",
  "identifierUris": [
    "api://auth-3e04ab58-8450-44d6-b95b-daca16b6cbdb/1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
  ],
  "webRedirects": [
    "https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect",
    "https://teams.microsoft.com/api/platform/v1.0/oAuthConsentRedirect",
    "https://oauth.pstmn.io/v1/browser-callback",
    "https://oauth.pstmn.io/v1/callback"
  ],
  "spaRedirects": [
    "https://spaarkedev1.crm.dynamics.com",
    "https://spaarkedev1.crm.dynamics.com/webresources/sprk_spaarkeai"
  ]
}
```

`signInAudience` is now **`AzureADMultipleOrgs`** = "Accounts in any organizational directory (Any Microsoft Entra ID tenant — Multitenant)" — exactly the D6 / ADR-028 Amendment A2 posture. This is a **non-breaking, additive** change for existing consumers: Spaarke's own home-tenant users/apps (PCF, Code Pages, Copilot, GitHub Actions OIDC) continue to authenticate exactly as before; the change only *permits* users from other (customer) tenants to also authenticate, contingent on their tenant admin consenting (§3).

**CIAM was NOT touched.** `signInAudience` values that enable CIAM (`AzureADandPersonalMicrosoftAccount` + External ID configuration) were never selected; this app remains a pure workforce-Entra registration. The external portal's separate CIAM tenant/app (Amendment A1) is untouched by this task.

---

## 2. Least-privilege delegated scope decision (§ Step 2 of the task)

### 2.1 Scope chosen: `access_as_user`

The Teams SSO/NAA flow for the collaboration tab MUST request **only** the app's own resource scope:

```
api://1e40baad-e065-4aea-a8d4-4b7ab273458c/access_as_user
```

This is an **existing** exposed scope on the app (added during the Copilot project per D7 — `adminConsentDescription: "Allow M365 Copilot to access Spaarke BFF API on behalf of the user"`) — reused, not newly created. Project-level docs consistently point at this exact scope for the Teams SSO path: design.md D7/§8, spec.md §9/§ "Reuse", task 001's foundation-spike `<step>` text, and the spike's own `config.sample.js` (`bffScope: "api://XXXX/access_as_user"`).

**Format-compliance note**: `.claude/constraints/auth.md` names `api://{APP_ID}/user_impersonation` as the illustrative BFF-API OBO scope format. The binding rule underneath that example (`MUST NOT use friendly scope names — use api://{GUID}/scope format`) is what actually carries force, and `api://1e40baad-.../access_as_user` complies with it (full `api://{guid}/{scope}` shape, not a bare friendly name). The literal scope *name* `user_impersonation` is the **existing OBO-exchange scope** used by PCF/Code-Page/Dataverse-OBO consumers of this same app for a different, unrelated purpose — it is not reused here because it belongs to a flow that performs server-side OBO, and the Teams collaboration path is explicitly **broker-only / no-OBO** (NFR-02). Using the distinct `access_as_user` scope keeps the two flows' consent/audit trails separable even though they share one app registration.

### 2.2 What was confirmed absent (no scope broadening needed)

- **No Teams messaging/bot scopes exist anywhere on this app.** `appRoles` contains exactly one entry (`Admin` — application-type, used for BFF admin/RAG endpoints, unrelated to Teams). There is no `ChannelMessage.Send`, no Bot Framework registration, no `TeamsAppInstallation.*` permission. Confirms the NFR-02 / ADR-029 "no bot" boundary holds by construction — nothing needed to be removed because nothing out-of-scope was ever added.
- **No new Graph delegated scope was requested or added** for the collaboration path. `getAuthToken()` (Teams SSO) mints a token whose audience (`aud`) is the app's own resource (`api://1e40baad-...`) — it never targets `graph.microsoft.com`. The collaboration path stays broker-only: the acquired token authenticates to the BFF only and is never exchanged downstream (verified by the foundation spike, §1.4 of `foundation-spike-findings.md`).

### 2.3 Existing broader permissions — flagged, deliberately NOT removed

The app's `requiredResourceAccess` also carries (pre-existing, unrelated to this task):
- ~20 Microsoft Graph delegated scopes + application roles (resource `00000003-0000-0000-c000-000000000000`) — used by PCF/Code-Page OBO flows, RAG indexing, and Copilot.
- 1 SharePoint Online delegated scope + 1 application role (resource `00000003-0000-0ff1-ce00-000000000000`).
- 1 Dataverse/Dynamics CRM delegated scope (`user_impersonation`, resource `00000007-0000-0000-c000-000000000000`) — used by PCF's OBO-to-Dataverse flow.

**Decision: flag, do not remove.** Removing any of these would break unrelated, already-shipped consumers (PCF controls, Code Pages, the Copilot agent) that depend on the same shared app registration — out of this task's scope per the REUSE constraint and root CLAUDE.md §11 (a removal here has no cost-of-doing-nothing justification for *this* task; its cost-of-doing-something is breaking other surfaces). Instead, the least-privilege goal is achieved at the **consent-request layer** (§3.2): the per-customer admin-consent URL is scoped to request *only* `access_as_user`, so a customer tenant admin approving the Teams app never sees or grants the other ~20+ permissions that exist on the app for Spaarke-internal consumers. This is the practical, safe way to get least-privilege behavior for the new customer-facing surface without touching a shared registration's blast radius for existing consumers.

### 2.4 Non-blocking follow-up (not applied — flagged for operator judgment)

The `access_as_user` scope's admin/user-consent display text (`adminConsentDisplayName: "Access Spaarke BFF API as user"`, `adminConsentDescription: "Allow M365 Copilot to access Spaarke BFF API on behalf of the user"`) still mentions "M365 Copilot" specifically, even though this task now reuses the same scope for the Teams tab. This text is shown verbatim on the scoped admin-consent screen (§3.2) a customer tenant admin sees. It is **functionally harmless** (OAuth scopes are opaque strings; the description is cosmetic) but could read as confusing/inaccurate to a customer security reviewer. Not changed in this task (outside the prescribed step list; editing consent-screen text is a judgment call, not a scope-set decision). If desired, an operator can update it with:

```bash
# Illustrative only — NOT executed by this task
az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/c2aab303-50f8-4279-9934-503ab3a4b357" \
  --headers "Content-Type=application/json" \
  --body '{"api":{"oauth2PermissionScopes":[{"id":"7e9e1e5a-3b0b-4153-9753-85b41d48c6fe","adminConsentDisplayName":"Access Spaarke BFF API","adminConsentDescription":"Allows the Spaarke collaboration app (Teams tab or M365 Copilot) to access the Spaarke BFF API on behalf of the signed-in user.","isEnabled":true,"type":"User","userConsentDisplayName":"Access Spaarke BFF API","userConsentDescription":"Allow this app to access your Spaarke data","value":"access_as_user"}]}}'
```
> Note: Graph's `PATCH` on `api.oauth2PermissionScopes` requires the **full existing array** (partial updates are rejected) — the app's Object ID is `c2aab303-50f8-4279-9934-503ab3a4b357`. OPERATOR-GATED.

---

## 3. Per-customer admin-consent onboarding procedure (§ Step 3 of the task)

### 3.1 Why admin consent is a hard gate (not advisory)

A brand-new customer tenant's users **cannot** silently self-consent their way into the collaboration surface, for two independent reasons:

1. **Publisher-verification baseline.** The app has not yet completed Publisher Attestation (design §8 — Publisher Attestation is the R1-minimum trust bar; M365 Certified is a parallel future workstream). Microsoft Entra's baseline/recommended tenant consent policies block user self-consent for apps from unverified publishers requesting anything beyond a small "low impact" permission set — `access_as_user` (an org-data-scoped delegated permission) does not qualify as low-impact. Most customer tenants (especially security-conscious legal customers) run this baseline or something stricter (many disable user consent entirely).
2. **Even where a tenant permits some user self-consent**, Azure AD's admin-consent requirement is evaluated per registered app, and this app registration also carries application-type Graph permissions (`Role` entries in `requiredResourceAccess`, §2.3) — those are **always** admin-consent-only regardless of tenant policy. The *first* interaction with this app id in a new tenant reliably surfaces an admin-consent-required prompt.

Net effect: until a customer tenant admin explicitly runs the consent flow below, `authentication.getAuthToken()` in the Teams tab fails for every user in that tenant (Teams surfaces this as a "needs admin approval" error), and the BFF never issues a session — satisfying the "hard gate, not advisory" acceptance criterion by construction, not by an extra check the BFF has to implement.

### 3.2 The onboarding procedure (customer tenant admin)

**Recommended path — scoped admin consent (does NOT expose Spaarke-internal permissions, §2.3):**

1. The customer tenant's **Global Administrator** or **Privileged Role Administrator** (any role that can grant admin consent) navigates to:

   ```
   https://login.microsoftonline.com/{customer-tenant-id-or-domain}/v2.0/adminconsent
     ?client_id=1e40baad-e065-4aea-a8d4-4b7ab273458c
     &scope=api://1e40baad-e065-4aea-a8d4-4b7ab273458c/access_as_user
     &redirect_uri=https://teams.microsoft.com/api/platform/v1.0/oAuthConsentRedirect
   ```

   (`{customer-tenant-id-or-domain}` = the customer's own tenant ID or verified domain, e.g. `contoso.onmicrosoft.com`, or `organizations` to let the admin pick their tenant at sign-in.) This is the standard Microsoft-documented pattern for Teams tab SSO admin consent — the `oAuthConsentRedirect` URI is Teams' own consent-completion endpoint and is **already registered** on the app (§1.1), so no redirect-URI change was needed.

2. The admin signs in with their tenant-admin credentials. Azure AD presents a consent screen scoped to **only** the `access_as_user` permission (`"Access Spaarke BFF API"`) — none of the ~20+ Graph/SharePoint/Dataverse permissions used by Spaarke-internal consumers are shown, because the request used the scoped `scope=` parameter rather than the blanket `/adminconsent` endpoint (no `scope` param), which would instead prompt for the app's **entire** `requiredResourceAccess` list.
3. The admin clicks **Accept**. Azure AD provisions a service principal for `1e40baad-e065-4aea-a8d4-4b7ab273458c` in the customer tenant (if one doesn't already exist) and records a tenant-wide admin consent grant for `access_as_user`.
4. **Install the Teams app.** The admin (or a delegated Teams app-catalog manager) uploads the packaged Teams app (task 070's output) to the org's app catalog under **App Centric Management**, per design §8. This can be done before or after step 1–3, but the tab will not complete SSO until admin consent (steps 1–3) has happened at least once for the tenant.
5. **Verify.** Any user in the customer tenant opens the Spaarke tab in Teams (desktop or web); `authentication.getAuthToken()` succeeds with **no second login prompt** (SSO), and the BFF's `tid`→environment routing (task 060, separate scope) resolves their tenant to the correct backing environment.

**MUST NOT — do not use the blanket admin-consent endpoint for customer onboarding**: `https://login.microsoftonline.com/{tenant}/adminconsent?client_id=1e40baad-...` (no `scope` parameter) grants the app's *entire* `requiredResourceAccess` list, including the ~20+ Spaarke-internal Graph/SharePoint/Dataverse permissions that have nothing to do with the Teams collaboration surface. A security-reviewing customer admin seeing that full list at onboarding time is exactly the outcome §2.3's flag-not-remove decision is trying to avoid; always use the scoped `scope=access_as_user` URL above.

### 3.3 Revocation / offboarding

A customer tenant admin can revoke consent at any time via **Entra admin center → Enterprise applications → SDAP-BFF-SPE-API → Permissions → Revoke**, or by removing the app's service principal from their tenant. This immediately blocks all further token issuance for that tenant (existing tokens remain valid only until their normal ~1-hour expiry).

---

## 4. `webApplicationInfo` values captured for task 070 (§ Step 4 of the task)

Byte-exact values task 070's Teams manifest MUST use:

```json
"webApplicationInfo": {
  "id": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "resource": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
}
```

- `id` = the app's `appId` (client ID) — confirmed via `az ad app show` (§1.3).
- `resource` = the **canonical** App ID URI. The app has two `identifierUris`:
  - `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` ← **use this one** (matches every reference across `docs/architecture/auth-azure-resources.md`, `config/spaarke-resources.yaml`, and the BFF's own `Audience` appsetting).
  - `api://auth-3e04ab58-8450-44d6-b95b-daca16b6cbdb/1e40baad-e065-4aea-a8d4-4b7ab273458c` ← an auto-generated secondary URI (Entra adds one of these automatically for apps enrolled in certain first-party integration flows, e.g. Copilot plugin registration) — **do not use this one**; it is not the audience the BFF's `JwtBearerOptions.Audience` validates against.

Task 070's escalation trigger ("if the values do not match... STOP and escalate") does not fire — both values are confirmed live against the actual app registration, not inferred from docs.

---

## 5. Acceptance criteria

| # | Criterion | Status |
|---|---|---|
| 1 | Entra app `1e40baad-…` confirmed configured as multitenant (accounts in any org directory), delegated scopes limited to least-privilege for Teams SSO/NAA | ✅ **Met** — `signInAudience` applied live (§1); `access_as_user` confirmed as the sole scope the Teams flow requests (§2); no broader scope added |
| 2 | A second (customer) tenant admin, following the documented onboarding procedure, can consent and install the app | 🟡 **Met (procedure documented + technically sound); live two-tenant install test is OPERATOR-GATED** — no second (customer) Entra tenant is available to this session to run the actual consent flow end-to-end. §3 procedure uses only already-registered redirect URIs and an already-exposed scope, so no unverified step remains, but the live click-through itself needs a real second tenant + admin, mirroring task 001's operator-gated live-Teams-client validation. |
| 3 | Exact `webApplicationInfo { id, resource }` values captured in this file for task 070 | ✅ **Met** — §4 |
| 4 | A tenant that has NOT admin-consented — that tenant's users CANNOT access the collaboration surface (hard gate) | ✅ **Met by construction** — reasoned in §3.1 from the app's actual registered permission shape (unverified publisher + application-role permissions present ⇒ Azure AD refuses token issuance without a prior admin-consent grant, independent of any BFF-side check) |

---

## 6. Scope boundary confirmation

- This task did **not** touch BFF `tid`→environment routing (task 060's scope) — no BFF code, config, or routing table was modified.
- This task did **not** register a new Entra app — `1e40baad-e065-4aea-a8d4-4b7ab273458c` was reused throughout.
- This task did **not** enable CIAM/External ID on this app registration.
- This task did **not** modify `src/**` — the only live change was the `signInAudience` property on the existing Entra app registration (via `az ad app update`), and the only file written is this notes file.
