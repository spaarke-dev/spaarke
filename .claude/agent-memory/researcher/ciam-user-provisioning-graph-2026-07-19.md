---
name: ciam-user-provisioning-graph-2026-07-19
description: Implementation-grade — how to provision + link external users in an Entra External ID (CIAM) external tenant via Graph for a workforce-tenant .NET 8 BFF (broker pattern, migrating off B2B guest). Create-user shape, credential delivery, token claims/stable key, sign-up gating, cross-tenant app model.
metadata:
  type: project
---

# CIAM (Entra External ID) user provisioning via Graph — 2026-07-19

Follow-up to [[spe-ciam-crosstenant-apponly-brokering-2026-07-18]] and [[power-pages-vs-entra-external-id-portal-2026-07-17]]. Context: BFF in workforce tenant; external users live in a SEPARATE Entra External ID (external-configuration) tenant; users never get workforce/Dataverse/PowerApps identity; broker pattern; migrating OFF B2B guest invitations.

## 1. Back-end create of a CIAM local account (Graph `POST /users`, Example 3)
Exact shape for an email local account in an external tenant:
```json
POST https://graph.microsoft.com/v1.0/users
{
  "displayName": "Test User",
  "identities": [
    { "signInType": "emailAddress", "issuer": "contoso.onmicrosoft.com", "issuerAssignedId": "adelev@adatum.com" }
  ],
  "mail": "adelev@adatum.com",
  "passwordProfile": { "password": "passwordValue", "forceChangePasswordNextSignIn": true },
  "passwordPolicies": "DisablePasswordExpiration"
}
```
- `issuer` = the external tenant's initial domain (`{tenant}.onmicrosoft.com`), NOT the social IdP. `issuerAssignedId` = the email.
- Local-account identities: password expiration MUST be disabled (`passwordPolicies: "DisablePasswordExpiration"`).
- Response `userPrincipalName` becomes `{guid}@{tenant}.onmicrosoft.com` (synthetic); `id` (oid) is the real key.
- Permission: **`User.ReadWrite.All`** (application) is the practical one; least-privileged is `User.Create`; `Directory.ReadWrite.All` is higher-privileged superset. Admin consent required.
- **Password-less (email-OTP-only) via Graph is NOT cleanly supported.** A Graph-created account is a password account. Email OTP as primary sign-in is tied to the OTP user-flow sign-up path; native-auth OTP sign-in "will not work with a user created via portal or Graph API" (MS Q&A). Flag as ambiguous — if OTP-only is a hard requirement, spike it; default to password + forceChange.

## 2. Credential / first sign-in delivery
- **No invitation/redemption email exists for pure CIAM LOCAL accounts** (that flow is B2B-guest-only). Two supported models: (a) admin sets temp password + `forceChangePasswordNextSignIn=true`, shares out-of-band; (b) user self-service sign-up.
- **There is NO Graph feature to auto-send a "set your password" email on create** (confirmed MS Q&A). Microsoft's recommended "admin creates, user sets own credential" pattern: enable **SSPR** (External ID SSPR supports Email OTP + SMS), then send your own onboarding email instructing the user to click **"Forgot password"** on the sign-in page to set their initial password. The BFF owns that onboarding email.

## 3. Token claims + stable linking key
- External-ID access/ID tokens carry: `aud, iss, oid, sub, preferred_username, tid, ver`, and `email` (email may need to be added as a claim in the user flow / claims mapping — several reports of `email` missing from ID token until configured).
- **STABLE link key = `oid`** (+ `tid` for routing). `oid` is the same across apps for a user and is the immutable directory object ID. Persist `oid` as the FK to the Dataverse Contact. Do NOT use `email` (mutable, differs across social IdPs) or `sub` (`sub` is pairwise — unique per app+tenant+user, so different apps get different `sub` for the same user; fine as a per-app key but NOT as a cross-system identity).
- Issuer format: `https://{subdomain}.ciamlogin.com/{tenant-id}/v2.0/`. OIDC metadata: `https://{subdomain}.ciamlogin.com/{tenant-id}/v2.0/.well-known/openid-configuration`.
- Validation differs from workforce v2.0 only in authority host (`ciamlogin.com` vs `login.microsoftonline.com`); still validate issuer, audience, lifetime, signing key. BFF needs a SEPARATE JwtBearer scheme/authority pointed at the CIAM tenant to validate external-user tokens (distinct from its workforce-token validation).

## 4. Self-service sign-up gating
- Sign-up CAN be disabled so ONLY pre-created accounts sign in: `PATCH /beta/identity/authenticationEventsFlows/{user-flow-id}` with `externalUsersSelfServiceSignUpEventsFlow.onInteractiveAuthFlowStart.isSignUpAllowed = false`. Get the user-flow-id via `identityContainer list authenticationEventsFlows` (filter by app id — not visible in portal).
- **This is exactly the "admin pre-creates, user just signs in" model** — avoids open self-service entirely. Caveat: with `isSignUpAllowed=false`, auto-JIT creation of users from a federated IdP is ALSO blocked → every external user must be Graph-provisioned first (matches the broker/invitation-link design).
- There is no built-in "allow-list of pre-authorized emails within open sign-up" — gating is binary (sign-up on/off); pre-authorization = pre-create + disable sign-up.

## 5. Cross-tenant app model
- Graph app permissions are granted to a service principal IN the resource (CIAM) tenant. A single-tenant workforce app has no SP there → **you need an app identity in the CIAM tenant**: either a dedicated app registration in the CIAM tenant, or a multitenant app whose SP is provisioned + admin-consented (Graph app roles) in the CIAM tenant.
- **A workforce managed identity cannot be granted Graph app perms directly on the separate CIAM tenant.** Classic path: CIAM-tenant app registration + client secret/cert (client-credentials).
- **Preview escape hatch:** *Managed Identities as Federated Identity Credentials* lets the workforce MI federate as the CIAM-tenant (multitenant) app with NO secret/cert. Still requires the app registration in the CIAM tenant; removes secret lifecycle. Recommend this if it's GA/acceptable-preview at build time; else cert in Key Vault.

## Sources (most authoritative first)
- https://learn.microsoft.com/en-us/graph/api/user-post-users — Example 3 create customer account in external tenant; permissions table. Updated 2026-07-04.
- https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-disable-sign-up-user-flow — isSignUpAllowed=false. Updated 2026-06-15.
- https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-enable-password-reset-customers — SSPR (Email OTP + SMS) for external tenants.
- https://learn.microsoft.com/en-us/entra/external-id/one-time-passcode — email OTP passwordless.
- https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference — oid/sub/tid semantics (sub is pairwise).
- https://devblogs.microsoft.com/identity/access-cloud-resources-across-tenants-without-secrets/ — MI-as-FIC cross-tenant (preview).
- MS Q&A: no auto set-password email on Graph create; native-auth OTP won't work with portal/Graph-created user.

## Open questions
- Can a Graph-provisioned account use hosted-user-flow Email OTP sign-in (not native auth)? Native-auth path explicitly can't; hosted flow unconfirmed — 30-min spike.
- Is MI-as-FIC GA yet (was preview as of early 2026)? Confirm at build time.
- Whether `email` claim needs explicit user-flow claim mapping for the BFF (several reports it's absent by default) — verify against the actual token.
