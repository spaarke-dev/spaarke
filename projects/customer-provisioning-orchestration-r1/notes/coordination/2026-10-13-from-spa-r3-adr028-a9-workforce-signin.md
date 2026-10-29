# From SPA-r3 — ADR-028 Amendment A9: Model 1 workforce contacts sign in at their home directory (owner, 2026-10-13)

Source: message from `spaarke-SPA-external-access-platform-r3`. Their full text:
`projects/spaarke-SPA-external-access-platform-r3/notes/coordination/2026-10-13-model1-workforce-signin-cross-project.md`.
The ADR text is ADR-028 A9 (master once PR #1810 merges). The same decision reached us from UAC-r2 as task 186 ("option A").

**Terms (owner):**
- "Spaarke tenant" means Spaarke's Entra directory. In Model 1 it holds every customer's Dataverse, Azure resources, app
  registrations and SPE.
- "Customer's home directory" means the customer's own Entra directory. Never write a bare "tenant" for the second.

## The decision

- **Licensed core users** stay B2B guests in the Spaarke tenant (A8, unchanged; H11 invites them).
- **Workforce contacts** are licence-free customer staff. They sign in to the browser SPA with their company account, at
  their home directory, through ONE multitenant Spaarke SPA client. They are not guests and are not invited.
- **The stamp BFF accepts their token only when** all of these hold:
  - `iss` is exact;
  - `tid` is in that stamp's `WorkforceIdentity:CustomerTenantIds`;
  - `aud` is that stamp's BFF;
  - it is a user token;
  - `acct = 0`.

  Identity is keyed on `(tid, oid)`.
- **Partners** (external contacts) stay on CIAM (T240d, option 2).

**Ownership:**
- SPA-r3 owns workforce sign-in and registration.
- UAC-r2 owns baseline role access (task 186).
- Provisioning owns the app registrations, consent and the directory.

## What it means for provisioning (checked against our code, 2026-10-13)

| # | A9 item | Our state | Work |
|---|---|---|---|
| 1 | Each stamp's BFF app is multitenant, `requestedAccessTokenVersion = 2`, scope `api://{bffAppId}/user_impersonation`; one shared multitenant workforce SPA client | H3 already creates the app with `signInAudience = AzureADMultipleOrgs` (`EntraAppRegOptions.cs:57`) and the scope. It preserves, but does not set, `requestedAccessTokenVersion` and `knownClientApplications` (`GraphAppRegistrationProvisioner.cs:668-674`) | H3 sets `requestedAccessTokenVersion = 2` and `knownClientApplications = [workforce SPA client]`, and adds the client to `PreAuthorizedClientAppIds` (the Copilot-client pattern, T257). The workforce SPA client is an operator one-time app; its id is a platform parameter |
| 2 | Customer onboarding: an admin-consent link for the customer's IT (one consent covers SPA client + API via `knownClientApplications`); the `CustomerTenantIds` intake (H11) | Intake has no `customerWorkforceTenantIds` yet (UAC task 186 hand-off: `notes/handoffs/INCOMING-141-workforce-tenant-list.md`) | Intake `customerWorkforceTenantIds` (required for Model 1; the CUSTOMER's home directory id, not `-TenantId`). H4b writes `WorkforceIdentity__CustomerTenantIds__N` on both slots. The run hand-off report prints the admin-consent URL. Admin consent is a customer-IT action and a manual gate, never an agent write |
| 3 | Microsoft publisher verification for Spaarke's multitenant apps before the first production customer (an AI Cloud Partner Program account plus a verified publisher domain, not `*.onmicrosoft.com`) | Not started | Owner/business action, tracked as ISS-026 |
| 4 | The T240c directory entry carries the customer's home directory id(s) | T240c is not built | Accept SPA-r3 task 041's field shape when it arrives (alongside `bffAppId`, `ciamBffAppId`) |

**Open technical items, to test before relying on them:**
- does combined consent via `knownClientApplications` work when the SPA requests a dynamic scope rather than `.default`?
- are `api://{guid}` App ID URIs accepted for multitenant APIs? Entra allows `api://{appId}` for multitenant apps, but
  this needs a live check.

**Our ADR-028 check:** no conflict with in-flight provisioning work.
- T240d (option 2) concerns the CIAM partner plane only.
- H11's B2B invites stay for licensed users.
- The `requestedAccessTokenVersion = 2` change alters the workforce token format for existing callers. ~~Identity.Web
  accepts both formats~~ **Corrected 2026-10-19 (SPA-r3 task 077):** the BFF's default scheme accepted only
  `aud = api://{appId}` (plus the Copilot audience), so a v2 token (`aud` = the bare app id) got 401 on every workforce
  surface. See item 8 for the precondition this puts on H3.

## Update 2026-10-13 (SPA-r3, owner-approved; R3 commit `63b7c575b`): Teams for workforce contacts is in scope

The Teams host signs everyone in through the shared multitenant SPA client, with NAA and the Teams context `tid`
(R3 `notes/a9-bff-design.md` §2, §4, §8, §12; R3 does dev in its task 059).

| # | Item | Provisioning work |
|---|---|---|
| 5 | The shared multitenant SPA client needs three SPA-platform redirects for the production origin: `https://external.spaarke.com` (browser sign-in), `https://external.spaarke.com/auth-callback.html` (Teams popup fallback, R3 task 075, and browser silent-link retry, task 058; msal-browser v5 redirect bridge, `POPUP_REDIRECT_PATH` in `src/client/external-spa/src/auth/msal-config.ts`), and `brk-multihub://external.spaarke.com` (Teams NAA broker). The A8 single-tenant client keeps its redirects (licensed-user guest-token link only) | An operator one-time step on the platform app (not per stamp): add all three. Dev is R3 task 059. Confirmed by SPA-r3 task 076 on 2026-10-19, which added `/auth-callback.html` |
| 6 | `authenticationBehaviors.removeUnverifiedEmailClaim = true` on the multitenant SPA client and on every stamp BFF app. R3's registration binds an existing contact by the token `email` | H3 sets it on the BFF app (get-before-set, read back), and the operator one-time step sets it on the SPA client. `authenticationBehaviors` is, as far as we know, a Graph **beta** property: confirm on Learn when H3 is built, and pin a test on the payload. H13 should verify it on the stamp's BFF app |
| 7 | Each customer's Teams admin approves the Spaarke Teams app once | A customer onboarding step and manual gate, never an agent write. The run hand-off report lists it next to the admin-consent URL. Confirmed by SPA-r3 task 076 (2026-10-19): the package is the org-catalog zip from `deploy-teams-app.yml` (build only; publishing is an owner or customer-admin action). It carries no per-customer value: no `webApplicationInfo`, no client id (the multitenant id is baked into the SPA bundle from `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`, which the owner sets on `external-spa-prod`) |
| 8 | **Precondition (SPA-r3 task 077, 2026-10-19):** the BFF default scheme now accepts both audience forms (`api://{appId}` and the bare app id, derived from a GUID `AzureAd:ClientId`) and refuses ID tokens for the app (no `azpacr`/`appidacr`). R3 branch commits `f68d0f312` + `77d74f305`; ships with the final R3 PR, not on master yet | Before H3 (`GraphAppRegistrationProvisioner`) **sets or keeps** `requestedAccessTokenVersion = 2` on a stamp's BFF app, the stamp must run a BFF build containing 077. Today H3 only preserves an existing value (`GraphAppRegistrationProvisioner.cs:680-682`), so nothing breaks yet. When the A9 H3 change is built: gate it on the stamp's BFF build containing 077. Also: `AzureAd__ValidAudiences__*` app settings are read by no BFF code; stamp settings must not rely on them (provisioning writes none, checked 2026-10-19) |
| 9 | **Dev reference configuration (SPA-r3 059 stage B, 2026-10-19 21:13Z, owner-approved):** dev BFF app `1e40baad` now has `requestedAccessTokenVersion = 2`, `knownClientApplications` += `5859e812` (the multitenant Spaarke Workforce SPA), access-token optional claims `given_name` + `family_name`, and `removeUnverifiedEmailClaim = true`; the dev BFF runs `283466d40`+ (contains 077). Every dev BFF build must now contain 077 | When the A9 H3 change is built, H3 sets the same four properties on each stamp BFF app (get-before-set, read back), and H13 verifies them. The `given_name`/`family_name` access-token claims are new to this mapping. Order: the stamp BFF build containing 077 first (item 8), then the token version |

