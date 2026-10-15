# T126 — message to unified-access-control-r2 (2026-10-12)

> From spaarkeai-word-add-in-r1. Owner answers recorded 2026-10-11: Q1 yes; Q2 follow the recommendation; Q3 clarified below; Q4 ok.

## Do not run `Set-ExternalFlagForB2BGuests.ps1 -Apply` in a Model 1 environment

The script treats every `#EXT#` systemuser as external. In Model 1 that is wrong:

- **Customer staff are guests too.** The customer's own employees (e.g. Dewey Cheatham) sign in as Entra B2B guests in Spaarke's tenant and get a Dataverse systemuser, so every one of them has `#EXT#`. They are the customer's internal users.
- **External partners are a different path.** Outside parties use CIAM (Entra External ID) and are contacts, not systemusers.
- **Evidence in dev:** Ralph Dewey (`ralph_deweycheatham.onmicrosoft.com#EXT#…`) is correctly `sprk_isexternal = No`. `-Apply` would set it to Yes, removing his Restricted-record shares and internal-only messages.

**Correct rule:** a guest systemuser is external only when it is **not** one of this customer's workforce guests.

## How to tell a customer-workforce guest from any other guest

Researcher finding, 2026-10-12; memory `.claude/agent-memory/researcher/b2b-guest-home-tenant-classification-2026-10-12.md`.

**Neither the token nor Dataverse gives a guest's home tenant:**
- A guest signing in against Spaarke's tenant gets `tid` = Spaarke's tenant, never the home tenant.
- `idp` often holds the home STS URI for Entra-account guests, but that form is not documented.
- No home-tenant claim is documented.
- Graph v1.0 `user.identities` shows `ExternalAzureAD` with no tenant id.
- Only the beta `signIn.homeTenantId` carries the home tenant, and it is audit-grade: beta, P1/P2, about 30-day retention.
- `systemuser` has no tenant column.

**Recommended rule: bind at provisioning time and record it.**
1. **Canonical customer identity (owner Q2).** The provisioning control plane's customer record is the primary source: the customer's Dataverse environment plus `customerWorkforceTenantIds`. Everything else is derived from it, including `WorkforceIdentity:CustomerTenantIds` and the legacy `sprk_CustomerTenantId` env var, which should be retired or filled from it.
2. **At invite/provision time** (`DataverseWebApiGuestUserWriter` path):
   - Resolve the invitee's email domain with `findTenantInformationByDomainName`.
   - Require the result to be in the customer's workforce tenant list.
   - Record the guest's `oid` as customer workforce, in a per-customer Entra group or a Dataverse team/field.
   - Set `sprk_isexternal = No` on that systemuser explicitly.
3. **Data step** (replacing the `#EXT#` rule): a guest systemuser is external unless its `azureactivedirectoryobjectid` is in that recorded workforce set.
   - Graph `identities` excludes MSA, OTP and Google guests (never customer workforce).
   - Beta `signIn.homeTenantId` may be used only as a cross-check.
4. **At sign-in:** customer workforce = `tid` is Spaarke's tenant **and** `oid` is in the recorded set. `idp` and `acct` are sanity checks only. Never use `email`, `upn` or `preferred_username`.

## The member test and sign-in authority (revised 2026-10-12 after a code check)

`WorkforceIdentityOptions` (task 141) assumes a Model 1 employee "signs in with the CUSTOMER's `tid`". The code bears that out for every path that runs the member test:

| Sign-in path | Authority | `tid` for a customer employee | Runs the member test |
|---|---|---|---|
| Dataverse-hosted (PCFs, code pages, Console — `@spaarke/auth`) | Spaarke's tenant (forced since #1453) | Spaarke's, `acct = 1` | No — licensed systemusers |
| External SPA, workforce realm (Type-2) | `/organizations` → the user's home tenant | the customer's, `acct = 0` | Yes (`ContactIdentityBinder`) |
| External SPA in Teams | Teams SSO, home tenant | the customer's | Yes |

So the member test itself is consistent. What remains open:

1. **Consent in the customer's tenant.** The workforce plane's app (`1e40baad…`, SDAP-BFF-SPE-API) is `AzureADMultipleOrgs`, but a home-tenant sign-in fails with AADSTS700016 until the customer's tenant has a service principal for it (admin consent). Is that a Model 1 provisioning step today?
2. **The customer tenant list.** `WorkforceIdentity__CustomerTenantIds` must hold the customer's home tenant id (provisioning's H4b writes it from `customerWorkforceTenantIds`). Empty = everyone refused.
3. **The external-flag script** works on systemusers, not tokens, so the member test does not protect against it — the rule above still applies.

A live sign-in as the Dewey Cheatham account on the external SPA's workforce realm will confirm 1 and 2: a consent / AADSTS700016 error points to 1, a `workforce_tenant_not_customer` deny to 2, `workforce_guest` to a Spaarke-authority sign-in.

## Update 2026-10-13: the home-tenant scheme ("option 1") is dropped — comply with ADR-028 A8

- **Live probe (2026-10-12).** The Dewey Cheatham admin signed in to the external SPA's workforce realm at the home tenant (`/organizations`) and consented. The BFF answered 401 IDX40001, because the default scheme is single-tenant.
- **Option 1.** On 2026-10-12 we built a JwtBearer scheme for `/api/v1/external`, keyed to an allow-list of customer tenants. It was never pushed or deployed and is parked on the local branch `wip/t126-workforce-customer`.
- **Why it was dropped.** ADR-028 Amendment A8 reached master with external-access-r3's #1795. A8 forbids a multitenant or `organizations` authority for Model 1. Both reviews of option 1 found fix-now gaps: no scope-or-roles check, no signing-key issuer validation, and customer-tenant email binding. Every one of them only existed because of the scheme.
- **The A8 path is already on master.**
  - R3 task 040: the external SPA signs workforce users in through the single-tenant "Spaarke Workspace" client (`9f1da340…`), pinned to Spaarke's tenant.
  - R3 task 012: the member test accepts a Spaarke-tenant guest token (`acct = 1`, `tid` = stamp) whose `idp` home tenant is in `WorkforceIdentity:CustomerTenantIds`. Keep the dev setting `WorkforceIdentity__CustomerTenantIds__1 = bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b`.
- **What the retest needs.**
  1. Deploy R3's task 040 SPA build to dev. This is owner-gated; it also needs `user_impersonation` on the dev CIAM API app (R3 task 041).
  2. Owner retests the Dewey Cheatham sign-in.
- **Review spin-off.** External-access-r3's `WorkforceCallerClaims.TokenEmail` did not read the mapped `upn` claim. Fixed in #1799 (closes #1798).
- **Still open for 126:** the external-flag script rule above, and UAC-r2's answer about it.

## Update 2026-10-13 (later): the owner chose option A for workforce contacts, recorded as ADR-028 A9

The owner re-examined the guest-versus-company-account question. The research is in `.claude/agent-memory/researcher/b2b-guest-vs-multitenant-home-authority-2026-10-13.md` and SPA-r3's `notes/2026-10-13-option-a-identity-and-licensing-research.md`. The owner then approved **ADR-028 A9** (PR #1810).

**Who signs in how:**
- **Workforce contacts** sign in to the browser SPA only, with their company account through a multitenant Spaarke SPA client. They are not guests. The BFF accepts their tokens only when the `tid` is on the list for that customer's backend and `acct` = 0, and keys them on (`tid`, `oid`).
- **Licensed core users** stay on A8's B2B guests. A stored home-to-guest link resolves them in the SPA.
- **Partners** stay on CIAM.

**Ownership:** SPA-r3 owns this end to end. UAC-r2 owns the baseline role, and provisioning owns app registrations and consent.

**For this project:** no code change. The add-in serves licensed core users only, on A8's sign-in. The shelved scheme stays shelved; SPA-r3 owns the BFF side. Its review findings above went to UAC and SPA-r3 as input.

**Live-test item for us:** a licensed guest signs in to the add-in from their own home Office, desktop and Word. Outlook on the web was verified on 2026-10-08.

## Q4 (owner)

Events with event type = Task carry documents and files. Spaarke does not use the out-of-box Dataverse task activity.
