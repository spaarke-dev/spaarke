# 114 — Production add-in: site, identity, and what waits for the directory (2026-10-07)

Context: customer-provisioning-orchestration-r1 is building one shared add-in for every Model 1 customer (owner-approved design, see `notes/113-signin-diagnostics-view.md`). Their third message (2026-10-07) reported the production site live and asked this project to deploy, add redirects to `c1258e2d`, point the manifests at the domain, and fix two guides.

## Owner decisions (2026-10-07)
1. **Production gets its own app registration** (not the dev `c1258e2d`).
2. **Prepare now; first production deploy together with provisioning's directory endpoint (their task 240c).** A production build has no backend to call until the add-in resolves the customer at run time; built today it would point at the dev server.

## Done
| Item | Value |
|---|---|
| Production site (provisioning) | `swa-spaarke-office-addins-prod`, `rg-spaarke-shared-prod`, subscription "Spaarke Shared Production" (`cd95fcec-…`), custom domain **https://addins.spaarke.com** (default host `green-plant-09ecafa1e.1.azurestaticapps.net` — never use it) |
| Production app registration (created by this project, owner decision 1) | **Spaarke Office Add-in (Production)**, client ID **`1958aec2-0218-495e-8e3c-37133e9b8357`**, object `77a7950c-…`, service principal `6fb59227-…`; single tenant; SPA redirects `brk-multihub://addins.spaarke.com`, `https://addins.spaarke.com/auth-callback.html`; Graph `email profile User.Read` admin-consented (AllPrincipals), mirroring the dev app; **no API permission** — customer backends pre-authorize it (provisioning H3). Notes field records its purpose. |
| Guides corrected | `docs/guides/office-addins-admin-guide.md`, `docs/guides/office-addins-deployment-checklist.md` — the never-existing `spe-office-addins-prod` replaced by the real site/domain and the production app. |

## Waiting (with 240c)
- **Runtime customer selection** in the add-in: call the directory with the Spaarke-tenant token, pick an entry (remembered per origin), pass `{authority, bffApiScope, apiBaseUrl}` into `AuthService.initialize`.
- **Production package**: its own manifest app ID and name (dev and production packages both install in Spaarke's tenant — Model 1 users are there — so a shared ID `b3965ea0…` would collide); `ADDIN_BASE_URL=https://addins.spaarke.com` (the build already substitutes it into every manifest URL — no new manifest files); `ADDIN_CLIENT_ID=1958aec2-…`; diagnostics OFF.
- **Production deploy job**: manual (`workflow_dispatch`) job in `deploy-office-addins.yml` with the production deployment token as a new GitHub secret (owner go at that time).
- **Admin center upload** of the production package (owner).
- Dev package rename to "Spaarke (Dev)" at the next dev package bump (with the 404 `code.script` cleanup).
