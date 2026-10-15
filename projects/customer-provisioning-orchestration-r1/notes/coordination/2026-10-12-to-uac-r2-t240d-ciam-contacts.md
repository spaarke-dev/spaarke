# To unified-access-control-r2 — T240d CIAM external contacts: three items in your code (2026-10-12)

From `customer-provisioning-orchestration-r1`. Design: `notes/t240d-ciam-external-contacts-design.md` (§4 refusal,
§5 keyless provisioner, §8 spike S1 results, §9 decision 3). Spike S1 ran in dev on 2026-10-10 and 2026-10-12.

## Context

External contacts (CIAM local accounts in `spaarkeextid`, no Entra guest access) are served per customer:
- **Audience.** Each customer gets its own CIAM API app in `spaarkeextid` (S1: a contact cannot get a token for a
  Spaarke-tenant app, AADSTS500207). A token for customer X is refused 401 at customer Y (S1 (iv) PASS).
- **Provisioner.** Each stamp's own BFF app, via MI-FIC with no certificate, holds only Graph `User.Create` in
  `spaarkeextid`.

## What we need from UAC-r2 (§9 decision 3 — yours, then the owner)

1. **Marker-gated E11 (tenant isolation).**
   - **The gap.** Any CIAM user can get a token for Y's audience: the SPA client is pre-authorized for every customer.
     Y then resolves the `oid` in Y's Dataverse. Today E11 email-binds an unbound Y contact with a matching email even
     when Y never invited that person (`ContactBindingDecision.cs:659`). That grants access at Y that Y never gave.
   - **Proposed fix.** The invite writes a pending-invite marker (plane External, no oid) *before* it creates the CIAM
     account, and E11 binds only a contact that carries the marker.
   - This changes the 141 binding model's contract and schema, so it is your call.
2. **The invite's "already has an account" branch.**
   - **S1 (ii) measured the error.** A second `POST /users` with the same email returns **400 `Request_BadRequest` /
     `ObjectConflict` on `proxyAddresses`** (because the request sets `mail`).
   - **Proposed behaviour.** On that error the invite keeps the contact pending (the marker in item 1) and sends a "sign
     in with your existing Spaarke account" email. The first sign-in binds through the marker-gated E11.
   - No `User.Read.All` is needed, so no stamp can list the directory.
   - Today the second customer's invite simply fails (`InviteExternalUserEndpoint.cs:115, 228-248`;
     `CiamUserProvisioningService.cs:57-69`).
3. **Invite provisioner facts from S1 (i)**, for whoever changes `CiamUserProvisioningService`.
   - **Use the `login.microsoftonline.com` authority.** App-only tokens for `spaarkeextid` must come from
     `https://login.microsoftonline.com/{ciamTenantId}`. The `*.ciamlogin.com` authority refuses app-only
     (AADSTS500206).
   - **`User.Create` cannot read `/organization`.** The local-account issuer domain therefore comes from configuration
     (`Ciam:Domain` = `spaarkeextid.onmicrosoft.com`), never from a Graph read.
   - **`POST /users` with an `emailAddress` identity** (issuer = that domain) returns 201 under `User.Create`.

## What provisioning does (no action for you)

Provisioning (H3c / H4b) sets per stamp:
- `Ciam:Audience` = the customer's CIAM API app id;
- `Ciam:GraphProvisioner:ClientId` = the BFF app id;
- `Ciam:TenantId`, `Ciam:Instance`, `Ciam:Domain`;
- `ExternalAccess:PortalUrl`.

The credential order on stamps is `ManagedIdentityFederated` only.

## Please reply

Do you accept items 1 and 2 as UAC-r2 work, and do you own item 3 (`CiamUserProvisioningService`)? If not, tell us who
does.
