# 115 — No webApplicationInfo in the unified package (package 1.1.2)

## Finding (provisioning, 2026-10-08)
A customer tenant's admin (test tenant "Dewey Cheatham & Howe PC", `deweycheatham.onmicrosoft.com`, `bc3aa7f4-…`) uploaded package 1.1.1 in Integrated apps. Deployment failed at its consent step: **AADSTS700016 — application `c1258e2d…` was not found in the directory**. The package's `webApplicationInfo.id` named the add-in app, which is single-tenant, so it cannot exist in another tenant. Spaarke's own deployment never showed this because Spaarke's tenant is the app's home.

## Why removing it is right (owner decision 2026-10-08; alternative was making the app multi-tenant)
- The add-in **never uses Office SSO** (`Office.auth.getAccessToken` / `OfficeRuntime.auth`: 0 uses). Sign-in is `@spaarke/auth` NAA.
- In a unified manifest, `webApplicationInfo` exists so the **deployment** collects admin consent in the installing tenant (Microsoft Learn: "Publish an add-in that requires admin consent for Microsoft Graph scopes"). Our tokens are issued by **Spaarke's tenant** (Model 1 users, incl. B2B guests, sign in there), where consent already exists (Graph `email profile User.Read` admin-consented; each backend pre-authorizes the add-in client). The customer tenant needs to consent to nothing.
- Multi-tenant would put Spaarke's app into every customer tenant and widen its sign-in audience for no benefit while sign-in stays in Spaarke's tenant. Revisit only for Model 2 (sign-in in the customer's tenant).
- `MailboxItem.ReadWrite.User` / `Document.ReadWrite.User` are Office add-in permissions (the unified-manifest form of the XML `<Permissions>`), granted with the install — no Entra consent.
- For 240c: no `resource` at all — NAA requests each customer backend's scope at run time.

## Changes
- `outlook/manifest.json`, `word/manifest.json`: `webApplicationInfo` removed; the 404 `CommandRuntime.code.script` entries removed (planned for this bump).
- `packaging/mergeUnifiedManifest.js`: emits no `webApplicationInfo`; **refuses** a source manifest that declares one (so it cannot silently return); the resource-agreement check removed. `clientId` stays only to refuse it as the package id.
- `webpack.config.js`: package **1.1.2** (the admin center rejects a same-version update; the pane footer shows it).
- Test: `mergeUnifiedManifest.test.ts` asserts no `webApplicationInfo`.

## Gates
Packaging + commands 92/92; full add-in jest 89 suites / 1,246; lint 0; typecheck 0 production (test debt 68). Built locally with the CI settings: `dist/spaarke/manifest.json` version 1.1.2, no `webApplicationInfo`, runtimes have `page` only.

## Owner steps after merge
1. Download the package artifact from the `deploy-office-addins` run (`spaarke-addin-1.1.2.zip`).
2. Spaarke tenant: Microsoft 365 admin center → Integrated apps → Spaarke → **Update** with 1.1.2.
3. Test tenant (deweycheatham): update/redeploy with 1.1.2 — the consent step should no longer appear.
4. As the guest (InPrivate): Word/Outlook on the web → Spaarke → ⋮ → Diagnostics → Copy.
