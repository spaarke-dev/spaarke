---
name: b2b-guest-vs-multitenant-home-authority-2026-10-13
description: Model 1 - A8 (B2B guest in Spaarke tenant, tenant-pinned MSAL/NAA) vs Option A (/organizations multi-tenant app, tid allow-list) - verified Entra facts on consent screen, auto-redemption, MFA trust, user consent, NAA/Teams, token validation, guest lifecycle
metadata:
  type: reference
---

## 2026-10-13: B2B guest (A8) vs multi-tenant home-authority (Option A) for licence-free workforce contacts

**Question**: Confirm/correct 8 claim groups comparing A8 (guest in Spaarke tenant) with Option A (customer tenant issues token for Spaarke multi-tenant BFF app, tid allow-list).

**Findings (all MS Learn, pages dated 2026)**
- Review-permissions consent appears at first sign-in for any redemption path (email link, direct tenant-specific link); PendingAcceptance until accepted. Auto-redemption suppresses it ONLY if BOTH tenants tick it (customer outbound org-setting for Spaarke tenant AND Spaarke inbound org-setting for customer tenant); org-specific only (not default); a notification email still goes to the user; does NOT affect app consent. Trust settings need Entra P1 on the configuring tenant (auto-redeem is under Trust settings; P1 for it specifically is inference).
- Cross-tenant defaults: B2B collab inbound+outbound ALLOW all; MFA/compliant/hybrid claims NOT trusted by default. With CA requiring MFA and no trust, guest must satisfy/register MFA in RESOURCE tenant (needs My Sign-ins app allowed if inbound app allow-list). Compliant/hybrid device CA effectively blocks guests unless device trust enabled (devices are managed only in home tenant; MS says do not require managed devices for externals unless trusting).
- Customer outbound CAN block (Block access for users/apps); default allow. Both outbound(home)+inbound(resource) evaluated at sign-in; invite-time check is resource side.
- Option A: multi-tenant reg needs signInAudience AzureADMultipleOrgs and globally-unique App ID URI; SP created in customer tenant on first consent; delegated perms w/o admin-consent flag are user-consentable; Microsoft-managed default policy ("Let Microsoft manage", default for new tenants) = users may consent to any user-consentable delegated perm EXCEPT a named Graph/EXO list (no publisher-verification requirement there); older "verified publishers, low-impact only" is a separate option admins can pick. If app requires user assignment -> admin consent mandatory. Tenants may disable user consent -> admin consent (AADSTS90094 AdminConsentRequired / AADSTS65001 DelegationDoesNotExist = no consent; AADSTS700016 = app not found in tenant, i.e. not installed/consented). CA: evaluated in the tenant that issues the token = customer tenant for Option A (customer's own CA+MFA); for A8 it is Spaarke tenant CA (+ trust settings decide whether home MFA counts).
- NAA/Teams: NAA (MSAL createNestablePublicClientApplication) needs SPA redirect brk-multihub://<origin>; Office docs use authority `common` with multitenant reg and say single-tenant is configurable; Teams doc sample uses `/{tenant_id}`. NO doc found that explicitly guarantees silent NAA for multi-tenant app in a customer tenant, nor for a guest account to Spaarke's tenant from home-tenant Teams. Teams legacy SSO doc: consent needed only for multitenant (app in different tenant); GitHub OfficeDev/microsoft-teams-library-js#1452 (2022): getAuthToken for GUEST accounts in other tenant returns consent-required until interactive (silent=false). Azure Architecture Center: cross-tenant switch generally needs new token, may be interactive.
- Token validation (access-tokens 2026-07-17, claims-validation 2026-06-15): multi-tenant API must validate iss==`https://login.microsoftonline.com/{tid}/v2.0` with tid a GUID, validate signing-key issuer (M.I.Web `EnableAadSigningKeyIssuerValidation`), tid is part of data key; `sub` is only unique within issuer; NEVER authorize on email/preferred_username/unique_name/upn; require/ check `scp` for delegated (absent = app-only/ID token); check idtyp for app tokens; tid check alone would also admit service principals -> use group/role/explicit allow-list. xms_edov = domain-verified email only for MSA/Google/OTP-style, requires email claim; Entra work accounts N/A. (tid, oid) is the documented combined key.
- Guest lifecycle: sign-in authenticates at the home IdP, so disabled/deleted home account cannot obtain new tokens (flow documented; no single explicit sentence found). Existing refresh token for guests cannot be invalidated per Q&A - disable blocks new tokens. MS guidance: inactive-guest report (90d), Access Reviews (P2/Entra ID Governance licence), auto block 30d then delete. AADSTS50020 if guest not invited to resource tenant (so registration service must pre-create guest before first token).
- No MS doc found declaring Option A unsupported/discouraged. Azure Architecture Center multitenant identity guidance (2026-07) says multi-tenant app in your own tenant is the standard SaaS pattern for customers on Entra; and for "invite your own staff" use guest access. Both are sanctioned.

**Sources**
- https://learn.microsoft.com/en-us/entra/external-id/redemption-experience (ms.date 2026-04-21)
- https://learn.microsoft.com/en-us/entra/external-id/cross-tenant-access-overview (updated 2026-03-27)
- https://learn.microsoft.com/en-us/entra/external-id/cross-tenant-access-settings-b2b-collaboration (2026-04-24)
- https://learn.microsoft.com/en-us/entra/external-id/authentication-conditional-access (2026-03-27)
- https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/manage-app-consent-policies (2026-08-28), configure-user-consent (2026-08-04), user-admin-consent-overview
- https://learn.microsoft.com/en-us/entra/identity-platform/howto-convert-app-to-be-multi-tenant (2026-06-15)
- https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens, claims-validation, optional-claims-reference, reference-error-codes
- https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/authentication/nested-authentication (2026-09-30); https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in (2026-01-23); tab-sso-overview (2026-07-22)
- https://learn.microsoft.com/en-us/entra/identity/users/clean-up-stale-guest-accounts (2026-03-25)
- https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/approaches/identity (2026-07-02)
- https://learn.microsoft.com/en-us/troubleshoot/entra/entra-id/app-integration/error-code-aadsts50020-user-account-identity-provider-does-not-exist

**Open questions**: live test of silent NAA (a) multi-tenant app in customer tenant, (b) guest-of-Spaarke with pinned authority, from customer-tenant Teams; whether auto-redemption needs P1 on customer; whether M.I.Web 3.x AddMicrosoftIdentityWebApi enables signing-key issuer validation by default (GitHub issue #2323 suggests not automatic - verify in repo).
