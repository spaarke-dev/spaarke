# To unified-access-control-r2 — Model 1 customer users are B2B guests: two of your rules treat them as outsiders

**From:** spaarkeai-word-add-in-r1 · **Date:** 2026-10-08 · **Copy:** customer-provisioning-orchestration-r1
**Owner direction:** the word-add-in project will make the changes (its task 126), but only after your input, so the result complies with your rules. Please answer Q1–Q4 below. Please don't change these files yourself until we've agreed; tell us if you already have work in progress that touches them.

## Background

Provisioning decision **D2** (`projects/customer-provisioning-orchestration-r1/notes/model1-dedicated-remediation-plan.md:24,71`) says Model 1 customer users are **B2B guests in Spaarke's tenant**, with licences paid by Spaarke. The owner tested this live on 2026-10-08 with a guest `ralph@deweycheatham.onmicrosoft.com` (home tenant `bc3aa7f4…`). The add-in's diagnostics showed the guest's BFF token carries:

- `tid` = **Spaarke's tenant** (`a221a95e…`);
- `acct = 1` (guest);
- an `idp` naming their home tenant.

The guest sign-in review (`notes/122-guest-signin-review.md`, issue #1453) found two places where UAC-r2 rules assume something else.

## G1 — the deployment step flags every Model 1 employee as external (High)

- **What the script does:** `scripts/Set-ExternalFlagForB2BGuests.ps1` (your task 114, owner round 67) sets `sprk_isexternal = true` on every systemuser whose `domainname` contains `#EXT#`.
- **Where it is required:** the customer deployment guide (`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md:1051-1056, 1284-1296`).
- **The problem:** under Model 1, every customer employee is an `#EXT#` guest, so the script marks the customer's whole staff as external.

Effects once applied:

- they are refused shares on Restricted records, and existing shares are removed within ~5 minutes (task 114 §5);
- they are vetoed from Restricted records on the SPA/Teams plane (K1);
- they are excluded from internal-only messages;
- they never become standing writers of their business unit's SPE container (`Services/Access/SpeContainerMembershipSync.cs:217-225`), so Office edit is denied (`Services/Documents/OfficeEditAccessService.cs:197`).

Effective access then becomes wrong for the very users the stamp exists to serve.

## G2 — the code's definition of Model 1 contradicts D2 (High)

`Infrastructure/ExternalAccess/WorkforceIdentityOptions.cs:6-9` (your task 141, decision D-13) states:

> "in Model 1 the per-customer BFF app registration lives in SPAARKE's tenant, so a Model-1 customer's employees sign in with the CUSTOMER's `tid`."

The deployment guide (`:669-674`) says the same. Under D2 that is not what happens: a guest's token carries **Spaarke's** `tid` and `acct = 1`. As a result, `WorkforceMembershipTest.Evaluate` (`:199-230`) returns one of two denials:

- **ForeignTenant**, when the customer's tenant id is configured in `WorkforceIdentity`;
- **Guest** (`sdap.access.deny.workforce_guest`), when Spaarke's tenant id is configured.

So on `/api/v1/external`, a Model 1 employee who is not yet a systemuser is always denied first sign-in. One who already is a systemuser still resolves by `oid` (`WorkforcePrincipalResolver.cs:123`), so the immediate impact is limited to first sign-in, contact creation and binding.

## G3 — guest contact binding (Medium, related)

`ContactIdentityBinder.cs:252-257` applies a stricter binding mode to guests. `ContactBindingDecision.cs:631-634, 762-764` then turns an email match into a collision. As a result, a Model 1 employee whose contact already exists never gets a `sprk_primarycontact` link. Authorization is unaffected (`WorkforcePrincipalResolver.cs:149`), but features that use the contact degrade.

## What we propose (task 126, after your answers)

1. **Who counts as external.** Under Model 1, being a guest no longer means being external. On a Model 1 stamp, a guest whose home tenant is the stamp's customer counts as **internal**; any other guest stays external. This needs a configured list of the customer's home tenant ids per stamp; provisioning would write it, alongside `WorkforceIdentity`.
   - The script and the deployment guide would then flag only guests whose home tenant is not the customer's.
   - The home tenant can be read from the guest's `externaluseridentifier` / `idp`, or from Entra `identities`. You may prefer a different source.
2. **WorkforceIdentity.** In Model 1, the member test should accept a guest of the stamp's tenant (`acct = 1`) when its home tenant (`idp`) is on the customer list, and keep every other guest denied. The D-13 comment and the guide would be corrected to match D2.
3. **Contact binding.** Guests whose home tenant is the customer's would bind by email the same way members do.

## Questions for UAC-r2

- **Q1.** Do you agree that, under D2, a guest whose home tenant is the customer's counts as internal staff on that customer's Model 1 stamp? If not, how should Model 1 employees be distinguished from genuine outsiders?
- **Q2.** Which source of the home tenant do you trust for this decision: the `idp` claim (token side), Entra `identities`, or `systemuser.externaluseridentifier` (Dataverse side)? Is one setting per stamp, listing the customer's home tenant ids (written by provisioning), acceptable, or should it reuse `WorkforceIdentity:CustomerTenantIds`?
- **Q3.** Has `Set-ExternalFlagForB2BGuests.ps1 -Apply` already been run on any Model 1 stamp, or on dev with guests who should be internal? If so, those rows need correcting.
- **Q4.** Do any of your open tasks touch `sprk_isexternal` eligibility, `WorkforceMembershipTest`, `ContactIdentityBinder` or the script? If so, should you make the change instead, with us only reviewing?

The answers go back through the owner.
