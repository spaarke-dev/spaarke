---
name: b2b-guest-home-tenant-classification-2026-10-12
description: Model 1 - how to tell a B2B guest's HOME tenant (token tid/iss/idp/acct, Graph identities, signIn beta homeTenantId, systemuser) - no documented home-tid claim or user property; recommend Spaarke-owned binding at invite time
metadata:
  type: reference
---

## 2026-10-12: Classify B2B guest as customer workforce (home tenant in configured list)

**Question**: In Model 1 (Dataverse + BFF app reg in Spaarke tenant, customer staff = B2B guests), what is the reliable rule for "this user's home tenant is one of the customer's tenant ids" at sign-in (token) and in a systemuser data script?

**Findings**
- Resource-tenant sign-in: `tid` = the tenant being signed in to (= Spaarke tenant), `iss` = Spaarke tenant STS (`/v2.0` for v2). `idp` = authenticating IdP, present for guests in v1 and v2 (MS Q&A 2022), absent for members. Entra-account guest: STS URI that embeds the home tenant GUID (doc says "usually an STS URI"; GUID form is observed practice, NOT a documented contract). MSA guest: `live.com` or STS URI with MSA tenant `9188040d-6c67-4c5b-b112-36a304b66dad`. Email-OTP/Google/SAML-WS-Fed: undocumented (idp value not stated; do not rely).
- `acct` (optional claim): 0 member / 1 guest **in the resource tenant** only. No documented `home_tid`/`home_oid`/`xms_*` home-tenant claim (access-token-claims-reference 2023-05 page, updated 2026-07; optional-claims-reference 2026-07-22). `xms_pl` is "sourced from home tenant" but is a language. `xms_edov` = email domain verified (MSA/Google/OTP only).
- Graph user: `identities` for Entra guest = issuer `ExternalAzureAD` (no tenant id; issuerAssignedId semantics undocumented). MSA = "Microsoft account"/live.com, Google `google.com`, OTP `mail`, SAML = issuer URI. `externalUserState` = invite status only. No v1.0 or beta user property for home tenant. Beta `externalUserConvertedOn` irrelevant.
- Only documented home-tenant-id source: **beta** `signIn.homeTenantId` (+`resourceTenantId`, `crossTenantAccessType`=b2bCollaboration, `userType`). Needs Entra P1/P2, AuditLog.Read.All, ~30-day retention, beta, absent for users who never signed in. v1.0 signIn has none.
- Domain->tenant: `GET /tenantRelationships/findTenantInformationByDomainName(domainName=..)` / `...ByTenantId(tenantId=..)` (v1.0, app perm CrossTenantInformation.ReadBasic.All, 2024-04). Maps a VERIFIED domain to a tenant id. Usable at invite time on the invitee email domain; weak after (email/UPN mutable, aliases, multi-domain tenants, MTO).
- Dataverse `systemuser`: no tenant/home-tenant/idp column. Relevant: `domainname` (UPN incl. `#EXT#`, mutable), `azureactivedirectoryobjectid` (= guest's oid IN Spaarke tenant, not home), `internalemailaddress`, `identityid`, `userpuid` (opaque). Nothing else.
- Multi-tenant app + home-authority sign-in: token `tid` = HOME tenant, `oid` = home-tenant oid (different from the guest object's oid in the resource tenant). Docs (redemption-experience 2026-04-21) say common/organizations URL routes a guest to home tenant; tenant-specific endpoint required to get resource-tenant token. Dataverse OAuth doc samples use AzureAdMultipleOrgs but I found NO doc stating Dataverse accepts a home-tenant token for an env in another tenant; the env's identities are the resource tenant's directory, so treat as NOT accepted (inference, MED confidence). Claims-validation doc: "tid must match tenant where data is stored".
- Repo fact: BFF requests optional claims email/upn/acct (GraphAppRegistrationProvisioner `AcctClaimName`); CallerIdentity reads `tid`.

**Recommendation**: do not derive "customer workforce" from token/Graph at runtime. Spaarke OWNS the binding: at invite/provision time resolve invitee home tenant (findTenantInformationByDomainName on verified domain, plus require `ExternalAzureAD`), then record guest oid -> customer in Spaarke's store (Entra security group per customer, or Dataverse team/field). Runtime check = oid (and tid==Spaarke tenant) membership. Use `idp` GUID and beta signIn.homeTenantId only as advisory cross-checks / audit script.

**Sources**
- https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference
- https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference
- https://learn.microsoft.com/en-us/entra/identity-platform/claims-validation
- https://learn.microsoft.com/en-us/answers/a/837836 (idp always present for guests; MSA example)
- https://learn.microsoft.com/en-us/entra/external-id/user-properties
- https://learn.microsoft.com/en-us/entra/external-id/redemption-experience
- https://learn.microsoft.com/en-us/graph/api/resources/objectidentity?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/graph/api/tenantrelationship-findtenantinformationbytenantid?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/graph/api/resources/signin?view=graph-rest-beta (homeTenantId; GitHub microsoft-graph-docs-contrib beta signin.md 2025-11-27)
- systemuser table ref (GitHub MicrosoftDocs/powerapps-docs raw) - no tenant column

**Open questions**: live probe on Dewey Cheatham: decode access token (idp exact form for Entra guest; for OTP guest), read `identities` issuerAssignedId, beta signIn homeTenantId; confirm Dataverse rejects home-tenant token; whether restrictGuestUserAccess applies to userType=Member guests.
