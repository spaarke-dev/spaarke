# Option A (home-directory sign-in) vs A8 (B2B guest): identity, IT and licensing research (2026-10-13)

Two read-only researcher runs, requested by the owner. Sources are current Microsoft Learn pages and the Power Platform Licensing Guide (October 2026). Status: **V** = verified in current docs, **I** = inferred, **U** = unverified. URL root `L/` = `https://learn.microsoft.com/en-us/`.

Terms (owner, 2026-10-13): **Spaarke tenant** = Spaarke's Entra directory (in Model 1 it holds every customer's Dataverse, Azure, app registrations and SPE). **Customer's home directory** = the customer's own Entra directory.

Owner decisions so far (2026-10-13): workforce contacts sign in with their company account (option A); licensed core users stay B2B guests; a licensed user's home identity is linked to their guest-backed systemuser by a **stored link** (no separate "core user sign-in" button). The licensing approach is **not** decided.

## 1. Identity and IT

| Topic | Finding | Status / source |
|---|---|---|
| Consent | Both the SPA client's and the customer BFF API's service principals must exist in the customer's home directory. `knownClientApplications` on the API gives one combined consent. Admin consent URL: `login.microsoftonline.com/{customer}/v2.0/adminconsent?...` | V: L/entra/identity-platform/howto-convert-app-to-be-multi-tenant, v2-admin-consent |
| Admin consent likely needed | Risk-based step-up consent (on by default) turns user consent into admin consent for **unverified** new multitenant apps asking more than basic sign-in; our API scope counts as more. "Assignment required" also forces admin consent. An admin-consent request workflow exists | V: L/entra/identity/enterprise-apps/configure-risk-based-step-up-consent, configure-user-consent, configure-admin-consent-workflow |
| Publisher verification | Effectively a prerequisite for option A: free; needs a Microsoft AI Cloud Partner Program account, a verified publisher domain (not `*.onmicrosoft.com`) and MFA | V: L/entra/identity-platform/publisher-verification-overview (2026-06-15) |
| Teams/Office NAA, option A | Documented multitenant pattern: multitenant app, `brk-multihub://{origin}` SPA redirect, `createNestablePublicClientApplication`, authority `common` or a tenant. GA in Teams, Outlook and Office (Word/Excel/PowerPoint on web/Windows/Mac/iPad). Teams-mobile specifics not found | V (mobile U): L/microsoftteams/platform/concepts/authentication/nested-authentication; L/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in |
| Teams/Office NAA, A8 | NAA inside the customer's Teams with an authority pinned to the Spaarke tenant (user is a guest there) is **undocumented**; a live-test risk, not a feature | U |
| B2B friction (A8) | First guest sign-in shows "Review permissions"; suppressed only if **both** sides enable automatic redemption. MFA trust is a per-partner inbound setting. The customer's **outbound** B2B settings can block guests (defaults allow) | V: L/entra/external-id/redemption-experience, cross-tenant-access-overview |
| Tenant Restrictions v2 | Does not govern either model's normal path (it controls external accounts used on external apps). Corrects an earlier session statement that TRv2 could block A8 guests: it is the outbound B2B setting | V: L/entra/external-id/tenant-restrictions-v2 (2026-06-11) |
| Conditional Access (option A) | Customer CA targets the **API's** enterprise app (resource), once its service principal exists; "All resources" policies also apply | V: L/entra/identity/conditional-access/concept-conditional-access-cloud-apps |
| "Assignment required" | Gates who can get a token; only Global Administrators bypass it; default off | V: L/entra/identity/enterprise-apps/application-properties |
| Token validation | Multitenant: substitute `tid` into the issuer template and require an exact `iss` match; check `tid` against the allowlist; key data on `tid`+`oid`; never authorize on `email`/`upn`/`preferred_username`. Set `requestedAccessTokenVersion` = 2 | V: L/entra/identity-platform/access-tokens (2026-07-17), claims-validation |
| Lifecycle | A user disabled at home is stopped at the next token refresh (access tokens 60-90 min; SPA refresh tokens 24 h). CAE does not reach third-party APIs and does not support guests. **Option A's off-boarding follows the home directory; A8 leaves stale guests** | V: L/entra/identity/conditional-access/concept-continuous-access-evaluation, L/entra/identity-platform/refresh-tokens |
| Linking a licensed user | A guest object exposes **no** home `tid`/`oid` (only `#EXT#` UPN and `identities` = `ExternalAzureAD`). The stored link must be keyed on home `tid`+`oid`, captured when the person proves both identities, never on email/UPN | V: L/entra/external-id/user-properties, claims-validation |

**Open technical items (test before relying on them):** combined consent via `knownClientApplications` when the SPA asks for a dynamic scope rather than `.default`; whether `api://{guid}` App ID URIs are accepted for multitenant APIs; Teams-mobile NAA; silent sign-in in Teams under option A (live test).

## 2. Licensing (not legal advice; the Product Terms and Spaarke's agreement govern)

| Topic | Finding | Status / source |
|---|---|---|
| Multiplexing | Licensing Guide (Oct 2026) p.24: "Multiplexing does not reduce the number of subscription licenses of any type required... Any user... that inputs data into, queries, views data from or otherwise accesses... directly or indirectly must be properly licensed." No carve-out found for a custom front end + app-only backend. All worked examples use a Power Platform or Dynamics app | V (custom-front-end applicability U): go.microsoft.com/fwlink/?linkid=2085130; microsoft.com/licensing/guidance/Multiplexing |
| Internal vs external | "External Users" = not employees/long-term contractors of **Customer** (the licensee). In Model 1 the licensee is Spaarke, so workforce contacts, partners and likely the licensed B2B guests are External Users for Spaarke's agreement. The test is employment, not Entra `userType` | V (definition) / I (application) |
| Power Pages | Authenticated-user licensing is per **website** (packs of 100 per site per month; PAYG $4 per user per site per month). No text extends it to a custom SPA or API | V / I |
| Pay-as-you-go | Power Apps per-app meter counts users who **open a Power App** ($10 per user per app per month). No Dataverse API or per-user meter exists; the Power Platform requests meter is "not yet available". So SPA/API-only users are neither metered nor thereby compliant | V: L/power-platform/admin/pay-as-you-go-meters (2026-08-14) |
| Backend identity | Application users need no licence and are unlimited, but **all application users in the tenant share a pooled request limit** (25,000 base requests per 24 h with no per-licence accrual; add-on +50,000/24 h). All contact traffic through one backend draws on it: a **capacity risk** if enforced | V: L/power-platform/admin/api-request-limits-allocations (2026-09-08), create-users |
| Licences for B2B guests | Sources conflict on whether a home-directory Power Apps licence covers Dataverse model-driven access ("Guest users should also have a license from the environment's tenant" vs cross-tenant recognition for per-user plans). PAYG environments avoid it: guests need no licence | V (conflict) / U (resolution) |

### Questions for Microsoft or a licensing partner

1. Are custom SPA, Teams and Word add-in users whose only Dataverse access is through our app-only backend "indirect" users needing a Power Platform licence? If yes, which SKU or meter covers a non-Power Apps front end?
2. Is "Customer" in the External Users definition the tenant licensee? May an ISV host one Dataverse environment per customer in its own tenant for that customer's employees, or must each customer license its own users or deploy in its own home directory?
3. Would Power Pages authenticated-user licensing be accepted for workforce and partner contacts who use a separate custom SPA on the same Dataverse contacts, or must the site be Power Pages?
4. Does a home-directory Power Apps Premium licence satisfy model-driven Dataverse access for B2B guests (reconcile `create-users` with the licensing FAQ), or is PAYG ($10 per user per app) the supported path?
5. What request entitlement does a multi-customer SaaS backend get beyond the pooled 25,000 per day, and when are enforcement or a Dataverse requests meter expected?
6. Confirm in writing whether the non-interactive and application-user "without requiring a license" provision covers app-only access made on behalf of human users.

## Implications for the option A decision

- Option A is the documented Microsoft pattern for Teams and Office NAA; A8's cross-tenant guest NAA is undocumented. This supports option A.
- Option A does not remove customer IT: expect one admin consent per customer, and plan **publisher verification** before the first customer.
- Off-boarding is better under option A.
- The licensed-user stored link must capture home `tid`+`oid` at a moment when both identities are proven; the guest object cannot supply the home id.
- Licensing is the same under either sign-in model and is the larger commercial risk, together with the pooled request limit.
