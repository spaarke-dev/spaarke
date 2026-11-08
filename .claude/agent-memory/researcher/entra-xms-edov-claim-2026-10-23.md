---
name: entra-xms-edov-claim-2026-10-23
description: xms_edov optional claim - token types (JWT = id+access per doc), config JSON, boolean, absent semantics, relation to removeUnverifiedEmailClaim; what is documented vs inferred
metadata:
  type: reference
---

## 2026-10-23: xms_edov claim in v2 access tokens

**Question**: Is xms_edov emitted in v2 access tokens, how configured, when absent, relation to unverified-email change / removeUnverifiedEmailClaim.

**Findings**
- DOCUMENTED (optional-claims-reference, updated 2026-07-22): row in the shared "v1.0 and v2.0 optional claim set"; Token Type = `JWT` (not SAML), User Type blank; "Boolean value indicating whether the user's email domain owner has been verified"; verified = email belongs to the tenant where the account resides AND tenant admin verified the domain; "Also, the email must be from MSA, Google, or OTP flow. Facebook and SAML/WS-Fed do not have verified domains"; "For this claim to be returned in the token, the presence of the `email` claim is required." No additionalProperties documented. No acceptMappedClaims/token-version requirement documented. The "tenant domain" vs "MSA/Google/OTP" sentences are ambiguous (AND vs OR) - needs a live probe for work-account users.
- Config = generic optionalClaims: `"accessToken":[{"name":"xms_edov","essential":false},{"name":"email","essential":false}]` on the RESOURCE (API) app (optional-claims). Doc never says "access token" for xms_edov specifically; "JWT" implies id+access. All community examples (Zammad, SlashID) use ID token only. Access-token emission is INFERRED - verify with a live token.
- Type: doc says Boolean; Zammad doc says value `true`. No doc/live evidence of string form -> parse defensively (JsonValueKind.True or string "true").
- Absent vs false: NOT documented. Community (Zammad admin docs): "Microsoft either reports a verified domain via xms_edov or omits the claim entirely" -> treat absent == unverified (fail closed). No doc says `false` is ever emitted.
- removeUnverifiedEmailClaim (Graph v1.0 authenticationBehaviors, page updated 2026-10-20): MSRC 2023-06 advisory; default = email claims with unverified domain owners are removed EXCEPT single-tenant apps and multitenant apps with prior sign-in activity with unverified emails; set true to force removal. Docs do not link it to xms_edov explicitly; relation is inferred (flag = app-level removal switch; xms_edov = per-token signal).
- MS guidance (all documented): never use email/upn/preferred_username for authorization (optional-claims-reference Warning + email row "not guaranteed correct, mutable"; claims-validation; Graph authenticationBehaviors "apps should never use the email claim for authorization"). Use tid+oid (or sub). Even xms_edov-gated email contradicts the letter of this guidance.
- The Learn page `migrate-off-email-claim-authorization` (en-us and legacy paths) now serves the identity-platform hub page (redirect/removed 2026-10); content only seen via search-summaries (xms_edov = Boolean, tid+oid recommended).

**Sources**
- https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference
- https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims
- https://learn.microsoft.com/en-us/entra/identity-platform/claims-validation
- https://learn.microsoft.com/en-us/graph/applications-authenticationbehaviors
- https://admin-docs.zammad.org/en/latest/settings/security/third-party/microsoft.html (community-grade evidence for absent semantics)
- https://www.slashid.com/blog/sso-safe-email-claim/ (ID token only)

**Open questions**: live probe of an access token for (a) Entra work user from another tenant (home sign-in), (b) B2B guest, (c) MSA - is xms_edov emitted, JSON type, absent vs false. Whether v1 vs v2 access token matters (doc silent).
