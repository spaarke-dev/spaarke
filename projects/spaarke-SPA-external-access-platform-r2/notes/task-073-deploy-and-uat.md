# Task 073 — Deploy + both-plane UAT — deployment record & owner UAT checklist

> 2026-08-11 · STANDARD rigor · prescriptive deploy. Deploys the P2b polymorphic access-write wave
> (070 grant-WRITE + 071 grant UI/PCF + 072 Tier-1 entitlement) and hands off the interactive both-plane
> UAT. Branch `work/spaarke-SPA-external-access-platform-r2` (synced with master: 0 behind at deploy time).

## Split: machine-deployed (done) vs owner-driven (pending)

Task 073 is fundamentally a deploy + **interactive** both-plane UAT. The deploy + machine-verifiable parts
are done; the interactive UAT (real workforce-SSO + CIAM logins + a browser) and the SPA workflow-deploy
are owner-driven — see the escalation + checklist below.

## Deployment record (done)

| Artifact | Mechanism | Result |
|---|---|---|
| **BFF (070 grant-write + 072 entitlement + master)** | `scripts/Deploy-BffApi.ps1` → spaarke-bff-dev (from worktree, NOT CI) | ✅ Deployed HEAD. Package **48.48 MB** (≤60; +0.02 vs 072's 48.46, from the master merge). `/healthz` passed; 4 critical files SHA-256 verified. |
| **TrackingFieldTrio PCF (071)** | `pac solution import` v1.0.12 (done during 071) | ✅ Live on SPAARKE DEV 1 (v1.0.12). No 071/072 change to the PCF → no re-import. Footer must read v1.0.12. |
| **external-spa (072 tab sets + 073 me-client flip)** | `deploy-external-spa.yml` (SWA) | ⏸ **OWNER-TRIGGERED** — production build verified locally (compiles + bundles, 368 KB gzip); SPA not yet uploaded (see escalation). |

### me-client mock→real flip (Deviation D-1 from 072 — DONE)
`fetchMeEntitlements` now calls `bffApiCall<MeEntitlementsResponse>('/api/v1/external/me/entitlements')`
in production, with a **graceful fallback**: on any failure it degrades to plane-derived `MOCK_BY_PLANE`
defaults (console.warn emitted) so a transient live-auth hiccup never leaves the workspace tab-less
(NFR-06 — the module-data endpoints enforce Tier-1/Tier-2 server-side regardless). `VITE_DEV_MOCK=true`
still short-circuits to the persona mock for local dev. **UAT tell:** the mock `displayName` ('Sam Rivera'
workforce / 'Dana Okafor' ciam) means the fallback fired; the real signed-in user's name means the live
path worked.

### Machine verification (done)
- `GET /api/v1/external/me/entitlements` → **401** unauthenticated post-redeploy (registered + auth-gated);
  sibling nonexistent route → 404 (proves real registration).
- Live grant rows (MCP), all on the correct typed root lookup (070 polymorphic write): **1 matter**
  (REAL-2026-123456.02), **4 project** (PRJT.10001.01 ×3, PRJT.10007.02 ×1), 0 standalone WA currently.
- BFF builds clean after the 19-commit master merge; 239 external-access unit tests green (072).

## 🔔 Escalation — external-spa SPA deploy path (owner decision)

The external-spa deploys **only** via the `deploy-external-spa.yml` GitHub workflow (`workflow_dispatch`,
using the `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` secret). There is **no worktree/CLI deploy path** for the SWA
(the SWA deployment token is not held locally), unlike the BFF (`Deploy-BffApi.ps1`). This collides with
the durable "deploy-from-worktree, never `gh workflow run deploy-*.yml`" rule — which was a BFF-specific
learning. Per CLAUDE.md §6 (outward-facing + auth-sensitive), the agent did NOT unilaterally trigger it.

**Owner action:** trigger the SPA deploy (Actions → "Deploy External SPA (Static Web App)" → Run workflow
on this branch), or run:
```
gh workflow run deploy-external-spa.yml --ref work/spaarke-SPA-external-access-platform-r2
```
The workflow builds with `VITE_DEV_MOCK` unset (real entitlement calls) + the dev CIAM/Teams identifiers
already baked in. Target SWA: `swa-spaarke-external-spa-dev`.

## Owner UAT checklist (both planes — interactive)

Prereq: SPA deployed (above); hard-refresh (Ctrl+Shift+R). Capture App Insights `[EXT-ENTITLE]` +
`[EXT-MODULE]` + grant-write traces during the run.

### A. Workforce (internal) admin — grant authoring (071 UI + 070 write)
1. Open a **Matter** record (e.g. REAL-2026-123456.02) → Manage Access (person icon). Confirm candidates +
   current grants load (**no "Failed to load access data"**) — the 071 polymorphic-read fix.
2. **Select contact** via the side-pane Advanced Lookup → grant. Optionally pick an **Organization**.
   Repeat on a **standalone Work Assignment** and a **Project**.
3. Verify each wrote a grant on the correct typed lookup (MCP: `SELECT sprk_project, sprk_matter,
   sprk_workassignment, sprk_organization FROM sprk_externalrecordaccess WHERE statecode=0`).
4. Confirm `sprk_grantedby` is populated under the real workforce SSO token (070 follow-up — was omitted
   under the CLI smoke token; should resolve to the caller's systemuser now).

### B. Workforce (internal) — entitlement + tab set (072)
5. Load the SPA as a workforce caller **with the FrontDoorUser App-Role**. Confirm `/me/entitlements`
   returns `['legal-front-door','policy-library']` (Network tab or the rendered tabs), and the display name
   is the real user (NOT 'Sam Rivera' — that would mean the fallback fired).
6. Tab set = **Quick Start (pinned) + Service Requests + Policy Library**. My Requests / Inventions /
   Messages are NOT default tabs (still openable from the library).
7. **FR-08 live:** add a new active `sprk_approlemodulemap` row (e.g. `FrontDoorUser → admin`), wait ~60s
   (cache TTL) or reload, and confirm the new module appears in `/me/entitlements` — **no deploy**.

### C. External (CIAM) partner — read + tab set (028 read + 072)
8. Log in as an external partner. Confirm `/me/entitlements` = `['assigned-work']`, tab set = **Work
   Assignments / Projects / Matters / Invoices / Documents** (no Service Requests / Policy Library).
9. The **granted** Matter/WA/Project from step 2 appear, with their **documents/invoices rolling up**
   (028 read). 
10. **NEGATIVES (NFR-08):** a matter/WA the partner was NOT granted (and its children) never appears;
    revoke a grant → it disappears; **close-project** cascade-revoke removes that project's grants (070's
    `_sprk_project_value` fix — verify the grants deactivate).

### Sign-off
If A–C all green → the P2b polymorphic access-write wave is verified end-to-end on both planes and the
branch is ready to merge to master. Record results here + flip TASK-INDEX 073 → ✅.
