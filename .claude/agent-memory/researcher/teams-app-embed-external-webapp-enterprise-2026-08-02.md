---
name: teams-app-embed-external-webapp-enterprise-2026-08-02
description: Enterprise-grade custom Teams app embedding an external web app as a tab/personal app with Entra SSO — distribution/governance, SSO app-reg + consent, manifest schema, security/compliance expectations, and an enterprise-readiness checklist (as of Aug 2026)
metadata:
  type: reference
---

# Shipping an enterprise-grade Teams tab that embeds an external web app (Entra SSO), Aug 2026

Context: selling to regulated (legal) enterprise IT orgs with strict governance. What MUST be true for their IT to approve/deploy.

## 1. Distribution / governance (Teams admin center)
- Three paths: (a) **custom app upload / sideload** (dev/test, per-user), (b) **org app catalog** ("Built for your org" store section — admin uploads via Manage apps, OR user submits via Teams App Submission Graph API → admin approves), (c) **Teams Store / AppSource** (public marketplace).
- **App Centric Management (ACM)**: since **April 2025** tenants auto-migrated to ACM. Legacy **App permission policies are DEPRECATED**; user/group-level access is now ACM assignment (security/M365/dynamic/nested groups, distribution lists; guests can't access even if assigned). App **setup policies** still exist (pin/preinstall + the "Upload custom apps" toggle).
- Custom-app upload enablement = 3 interacting toggles: **Org-wide app settings → Custom apps → "Let users interact with custom apps in preview"** (org master switch, still labeled "in preview"), **app setup policy "Upload custom apps"** (per-user), **team-level "Allow members to upload custom apps"**. All-off ⇒ only submit-for-admin-approval path works. GCC/GCCH/DoD: third-party apps blocked by default. 21Vianet/air-gapped: custom upload supported via Manage apps.
- Admin approval workflow: submit (Toolkit `Teams: Publish to Organization` / Graph submission API) → **Pending approval** widget in Manage apps → admin flips status Submitted→Publish (auto-Allowed). Admin upload needs no approval.
- Manage apps page now surfaces **"Apps to consider allowing"** tile (M365-certified + publisher-attested counts), a per-app **Security and Compliance tab**, CSV catalog export. Sources dated 2026-04-14 / 2026-07-05.

## 2. Entra app registration + tab SSO
- Register app; **single-tenant** = LOB for one org; **multitenant** = ISV selling to many enterprises (each customer tenant needs admin consent). For selling to other enterprises → multitenant.
- **Expose an API**: App ID URI must be `api://<fully-qualified-domain>/<AppID>` (domain must match app host; lowercase; multi-domain not supported). Add scope **access_as_user** (recommend **Admins only** consent). Set `requestedAccessTokenVersion = 2`.
- **Pre-authorize** Teams client IDs so users skip consent: Teams web `5e3ce6c0-2b1f-4285-8d4b-75ee78787346`, Teams desktop/mobile `1fec8e78-bce4-4aaf-ab1b-5451cc387264`; also M365 web `4765445b-32c6-49b0-83e6-1d93765276ca`, M365 desktop `0ec893e0-5785-4de6-99da-4ed124e5296c`, M365 mobile / Outlook desktop `d3590ed6-52b3-4102-aeff-aad2292ab01c`, Outlook web `bc59ab01-8403-45c6-8796-ac3ef710b3e3`, Outlook mobile `27922004-5251-4030-b22d-91ecd9a37ea4`.
- Flow: client calls Teams JS **`getAuthToken()`** → returns Entra access token for the app (id/email/profile/offline_access user-level only). For real Graph scopes (User.Read, Mail.Read, etc.) do **server-side OBO exchange on the v2 endpoint**. Admin consent in the *installing* tenant means no user consent dialogs.
- CRITICAL: manifest `webApplicationInfo.id` = App (client) ID, `webApplicationInfo.resource` = App ID URI — must match Entra EXACTLY (subdomain/case/`api://` vs `https://`).

## 3. Manifest requirements
- Latest schema **v1.29** (June 2026): `https://developer.microsoft.com/json-schemas/teams/v1.29/MicrosoftTeams.schema.json`. Called the **Microsoft 365 app manifest** (was "Teams app manifest").
- Embedding a tab needs: `staticTabs`/config tab with `contentUrl` (+ entityId, name, scopes personal/team), **`validDomains`** listing EVERY runtime hostname (redirects, auth pages, CDNs, subdomains; exclude localhost), and `webApplicationInfo{id,resource}` for SSO.
- Forward-path tooling = **Microsoft 365 Agents Toolkit** (renamed from **Teams Toolkit**), VS Code extension + CLI; `m365agents.yml` lifecycle; `Zip Teams App Package`; `Publish to Organization`; CI/CD supported. Store validation at dev.teams.microsoft.com/tools/store-validation.

## 4. Security / compliance expectations
- **Iframe/CSP** (tab-requirements page): tab content loads in an iframe. Set `Content-Security-Policy: frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft` (Microsoft moved to the unified **`*.cloud.microsoft`** domain). Do NOT send `X-Frame-Options: DENY|SAMEORIGIN`. Desktop client enforces stricter framing than web. **Sign-in redirect pages won't render in the iframe** (clickjacking guard) → use getAuthToken/OBO or popup auth, not top-level redirect.
- **Conditional Access / device compliance**: enterprise will scope CA to the app's Entra app; supported via standard Entra CA (require compliant device / MFA / managed). App must tolerate CA challenges (NAA/popup, not silent-only).
- **M365 App Compliance Program** (3 tiers, page dated 2025-10-07): **Publisher Verification** (MPN identity → verified badge on consent prompt), **Publisher Attestation** (self-assessment vs 80+ MDA risk factors — security/data-handling/compliance), **Microsoft 365 Certified** (audited against SOC 2 / ISO 27001 / HIPAA / GDPR-style controls; can attach downloadable evidence in commercial tenants). TAC now shows SOC2/ISO27001/HIPAA/GDPR/CCPA/FedRAMP/CSA-STAR/pen-test/SSO attributes + MDA permission-risk ratings; trust-based filters. **Enterprise legal IT will expect at minimum Publisher Attestation, ideally M365 Certified.**
- **Data residency / tenant isolation**: external web app must document where data lives, keep per-tenant isolation, use least-privilege delegated scopes (avoid broad app-only Graph). Multitenant apps must validate the incoming token `tid`/issuer and isolate by tenant.

## 5. Enterprise-readiness checklist (short)
Multitenant Entra reg w/ verified publisher · access_as_user (admin-consent) + preauthorized Teams client IDs + token v2 · server-side OBO for Graph, least-privilege delegated scopes · manifest v1.29, complete validDomains, exact webApplicationInfo · CSP frame-ancestors incl `*.cloud.microsoft`, no X-Frame-Options DENY, non-redirect auth · HTTPS everywhere · Publisher Attestation (min) / M365 Certified (ideal) · privacy policy + ToU URLs · CA/device-compliance tolerant · data residency + tenant isolation documented · ship via org app catalog (admin upload or submission-API + approval), pin via setup policy, scope via ACM · built/published with Agents Toolkit + CI/CD.

## Sources (all learn.microsoft.com)
- /microsoftteams/teams-custom-app-policies-and-settings (2026-04-14)
- /microsoftteams/manage-apps (2026-07-05; ACM April 2025, deprecation of app permission policies)
- /microsoftteams/platform/resources/schema/manifest-schema (v1.29, updated 2026-06-30)
- /microsoftteams/platform/tabs/how-to/authentication/tab-sso-register-aad (updated 2026-04-01; client IDs, App ID URI, token v2)
- /microsoftteams/platform/tabs/how-to/authentication/tab-sso-overview, /tab-sso-code, /tab-sso-graph-api
- /microsoftteams/overview-of-app-certification (2025-10-07; 3-tier program + TAC security/compliance surfacing)
- /microsoftteams/platform/toolkit/publish (updated 2025-09-15; Agents Toolkit rename, publish-to-org flow)
- /microsoftteams/platform/tabs/how-to/tab-requirements (CSP frame-ancestors, *.cloud.microsoft)

## Open questions
- Exact current default state of "Let users interact with custom apps in preview" per license/tenant age (still labeled preview in 2026-04 docs).
- Whether Spaarke's external SPA (external-access-platform) would go multitenant vs a per-customer single-tenant deployment — affects consent + isolation model.
- FedRAMP/GCC-High story if any legal customer is government-adjacent (third-party apps blocked by default there).
