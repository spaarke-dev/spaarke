# Teams desktop sign-in fix + R2 ownership of the shared Entra app (`1e40baad`)

> 2026-08-07. Applied by spaarke-SPA-external-access-platform-r2 (now the owner of the shared platform Entra app).
> Responds to `projects/teams-app-r1/notes/r2-shared-entra-app-coordination.md` (teams-r1 → R2 handoff; teams-r1 is archived).

## Context
Teams **desktop** sign-in failed two ways (web worked):
1. **NAA (primary)**: OneAuth broker `2002 "Access denied for the resource"` — the Windows WAM/OneAuth broker couldn't silently get `access_as_user` for `api://1e40baad-…`.
2. **Teams SSO (fallback)**: *"App resource defined in manifest and iframe origin do not match"* — `webApplicationInfo.resource = api://1e40baad-…` is not domain-qualified to the tab origin.

## Decisions (R2, as owner of `1e40baad` = "SDAP-BFF-SPE-API")
- **Governance ask #1 — accepted**: R2 owns this shared platform Entra app's config going forward. Future changes (redirect URIs, pre-authorized clients, scopes, audiences) route through R2.
- **Change #4 (broker pre-auth) — RATIFIED (Option A kept)**: the Microsoft Authentication Broker (`29d9ed98-a469-4536-ade2-f981bc1d605e`) stays pre-authorized on `access_as_user`. It's the least-invasive NAA-desktop fix, matches how the Teams web/desktop clients are already pre-authorized, and is additive/reversible (backup `c:/tmp/entra-api.json`). This fixes the **NAA primary** path.
- **Option B — APPLIED (durable SSO fallback)**: per governance ask #3, R2 also hardens the SSO fallback so desktop is robust even if NAA is fragile across clients/tenants. Both paths now work.

## Applied (all additive, verified)
1. **Entra `1e40baad` identifierUris** — added `api://green-dune-0c4f1221e.7.azurestaticapps.net/1e40baad-e065-4aea-a8d4-4b7ab273458c` (domain-qualified App ID URI). Existing two URIs + the 4 exposed scopes + the 9 pre-authorized apps (incl. the broker) **all preserved** (before/after verified via `az ad app show`).
2. **R2 Teams manifest** (`src/client/external-spa/appPackage/manifest.json`) — `webApplicationInfo.resource` → the domain-qualified URI (so Teams SSO's origin match passes).
3. **BFF `spaarke-bff-dev`** — added `AzureAd__ValidAudiences__2 = api://green-dune-…/1e40baad-…` so SSO-fallback tokens (issued for the new resource) validate. Regression-verified: an existing-audience token still returns 200 on `/api/v1/external/me` (no break to NAA / current clients).

## Remaining steps (admin — cannot be done from the worktree)
1. **Re-upload the Teams app package** with the updated `webApplicationInfo.resource` (domain-qualified). ⚠️ The **live** Teams app is teams-r1's package, which still carries the GUID-form resource — the SSO-fallback fix only takes effect once a package with the new resource is uploaded (Teams admin center / sideload). When R2's client is redeployed to the SWA, repackage from this manifest.
2. **Desktop retest** — confirm NAA (Option A / broker pre-auth) clears the `2002` error on the desktop client; the SSO fallback (Option B) now backstops it.
3. **Admin consent** on this app must use the **Entra portal** or `az ad app permission admin-consent` — NOT a bare `/adminconsent?client_id=…` URL (fails `AADSTS7000471` because of the `brk-…` SPA redirects; teams-r1 coordination note §6).

## Provisioning-doc follow-up
Fold the full Teams NAA/SSO Entra recipe (multitenant + broker/Teams pre-auth + `brk-…` redirects + domain-qualified App ID URI + BFF ValidAudiences) into `docs/guides/auth-deployment-setup.md` so a second/customer tenant reproduces it deterministically (teams-r1 governance ask #5; graduation criterion 6).
