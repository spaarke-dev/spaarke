---
name: naa-preauthorized-teams-clients-2026-10-29
description: Does an API need Teams desktop/web (1fec8e78, 5e3ce6c0) or MS Authentication Broker (29d9ed98) in preAuthorizedApplications for Teams NAA? Docs say NOT needed (that's Teams SSO only); broker pre-auth undocumented; consent to OUR client is what matters
metadata:
  type: reference
---

## 2026-10-29: NAA and preAuthorizedApplications for Teams first-party clients (r3 task 070/071)

**Question**: For a Teams personal tab using msal-browser NAA (no webApplicationInfo, no getAuthToken) to get `api://<BFF>/user_impersonation`, must the API pre-authorize Teams desktop/mobile 1fec8e78, Teams web 5e3ce6c0, or MS Authentication Broker 29d9ed98?

**Findings**
- Teams NAA doc (updated 2026-09-30) says explicitly: "You don't need to preauthorize your hosts, such as Teams or Microsoft 365, to call your endpoints" and "Unlike the OBO flow, you don't need to preauthorize your hosts". Its comparison table marks "Authorize Teams client apps" Required for Teams Entra SSO, blank for NAA. Devblog 2024-09-04 says the same.
- The 1fec8e78/5e3ce6c0 IDs are documented only in tab-sso-register-aad (Teams SSO / getAuthToken, "allows Teams Client to obtain an access token on behalf of your application"). 29d9ed98 (broker) appears in NO Learn page or official sample found - undocumented for any NAA/Teams purpose.
- NAA tokens "are issued for your Microsoft Entra app registration ID" -> the consent check is for OUR client app, not the host. preAuthorizedApplications semantics (Graph apiApplication): preauthorized clients skip consent for listed scopes. So the only pre-auth that can matter for NAA is the API pre-authorizing OUR client (Office sample Outlook-Add-in-SSO-NAA-Identity pre-authorizes its own client id when client==API), or an admin-consent grant in the customer tenant.
- Missing consent: docs say MSAL "tries to get an access token silently. If that's unsuccessful, it prompts the user for consent" (via acquireTokenPopup); IT admin "might block the app or consent to only certain permissions". Admin-only scope / user consent disabled -> standard Entra admin-approval behaviour (not NAA-specific in docs).
- Local evidence: teams-app-r1 added broker pre-auth 2026-08-07 for OneAuth 2002 "Access denied for the resource" on desktop NAA; its own note (desktop-blank-tab-rootcause-and-fix.md) says NAA still failed "even after broker pre-auth" -> broker pre-auth was NOT shown to fix anything. Q&A 5852192 (Outlook desktop, same error) points to Conditional Access (deprecated approved-client-app grant) and self-pre-auth, not broker.
- Verdict: "not needed" for the Teams client IDs (documented); broker = undocumented, no evidence it helps, do not add.

**Sources**
- https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/authentication/nested-authentication
- https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/how-to/authentication/tab-sso-register-aad
- https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in
- https://learn.microsoft.com/en-us/office/dev/add-ins/outlook/faq-nested-app-auth-outlook-legacy-tokens
- https://devblogs.microsoft.com/microsoft365dev/introducing-nested-app-authentication-an-improved-authentication-protocol-for-your-teams-app/
- https://learn.microsoft.com/en-us/graph/api/resources/apiapplication
- https://github.com/OfficeDev/Office-Add-in-samples/tree/main/Samples/auth/Outlook-Add-in-SSO-NAA-Identity
- https://learn.microsoft.com/en-us/answers/questions/5852192 ; https://learn.microsoft.com/en-us/answers/questions/5608319 (getAuthToken, not NAA)
- local: projects/teams-app-r1/notes/r2-shared-entra-app-coordination.md, desktop-blank-tab-rootcause-and-fix.md

**Open questions**: no doc on what the host broker does when OUR client lacks consent and user consent is disabled (expected AADSTS65001/admin-approval surfaced through popup); no live stamp test without Teams/broker pre-auth yet. Related: [[naa-teams-tenant-authority-guest-2026-10-19]].
