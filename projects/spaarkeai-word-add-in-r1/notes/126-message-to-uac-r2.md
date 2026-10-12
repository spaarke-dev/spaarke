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

## Question for you

`WorkforceIdentityOptions` (task 141) says a Model 1 employee "signs in with the CUSTOMER's `tid`", and the member test reads `tid`. That holds only when the employee signs in against their **home** tenant authority, through a multi-tenant app. A guest signing in against Spaarke's tenant carries Spaarke's `tid`.

Which authority does each Type-2 path use? If any path uses Spaarke's authority, the member test refuses every Model 1 employee. A live token probe on the Dewey Cheatham account would settle it.

## Q4 (owner)

Events with event type = Task carry documents and files. Spaarke does not use the out-of-box Dataverse task activity.
