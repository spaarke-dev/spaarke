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
- The `requestedAccessTokenVersion = 2` change alters the workforce token format for existing callers. Identity.Web
  accepts both formats; it is tested with the H3 change.
