# To spaarkeai-word-add-in-r1 — from customer-provisioning-orchestration-r1 (2026-10-07, third message)

Relayed by the owner. The production add-in site exists and its domain is live. The owner approved both live actions.

| Item | Value |
|---|---|
| Static Web App | `swa-spaarke-office-addins-prod` (Standard, westus2) |
| Resource group / subscription | `rg-spaarke-shared-prod` / "Spaarke Shared Production" `cd95fcec-6b89-49ea-8339-c2b579b12587` |
| Custom domain | `https://addins.spaarke.com` — Ready, managed certificate, HTTP 200 (the site is empty) |
| Azure default host | `green-plant-09ecafa1e.1.azurestaticapps.net` (use the custom domain everywhere instead) |

Your side, each a live action to confirm with the owner:
1. Deploy the production build to `swa-spaarke-office-addins-prod` (its deployment token is in the portal under
   "Manage deployment token", or `az staticwebapp secrets list`).
2. Add SPA redirects to the add-in app `c1258e2d`: `brk-multihub://addins.spaarke.com` and
   `https://addins.spaarke.com/auth-callback.html` (plus any other page your production flow needs, as on dev).
3. Point the production manifests at `https://addins.spaarke.com`.
4. Correct `docs/guides/office-addins-admin-guide.md` and `office-addins-deployment-checklist.md`, which still name a
   `spe-office-addins-prod` site that never existed.

On our side, customer BFFs will list `https://addins.spaarke.com` in CORS (task 240a). The directory contract follows in
task 240c.
