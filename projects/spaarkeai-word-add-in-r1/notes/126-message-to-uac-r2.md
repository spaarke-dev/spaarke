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

## Q4 (owner)

Events with event type = Task carry documents and files. Spaarke does not use the out-of-box Dataverse task activity.
