# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-07 (by context-handoff — Teams desktop debugging round)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **RESUME**: Project is COMPLETE + ARCHIVED. Active thread = **post-project Teams DESKTOP debugging** — read "Teams desktop debugging round (2026-08-07)" below FIRST.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Project COMPLETE + ARCHIVED (Issue #724 closed, all 26 ✅). **Active thread: Teams DESKTOP tab renders BLANK post-auth** (web works). |
| **Step** | Desktop sign-in NOW SUCCEEDS (auth error gone) after R2's fix + a fresh-id package. Remaining: desktop tab content area is **blank** on desktop only. |
| **Status** | 🟢 Web Teams: fully working (auth + records). 🟡 Desktop Teams: **authenticates but renders blank** — a post-auth render/data issue, NOT auth. Auth was fixed by R2 (domain-qualified App ID URI + broker pre-auth) + defeating Teams' per-app-id manifest cache with a fresh-id package. |
| **Next Action** | Get the **desktop console** (`Ctrl+Shift+Alt+8` in new Teams, or Settings→About→Developer preview → right-click tab → Inspect): (1) any red **Console** JS errors? (2) is **`GET …/api/v1/external/me`** 200 or failing? → **render error** = trace SPA (WebView2-specific); **`/me` 401/failing** = R2's SSO-token audience path (loop R2 in with exact response). Cheap first: reload tab / restart Teams (tried, still blank). |

---

## Teams desktop debugging round (2026-08-07) — READ FIRST

**Context**: teams-app-r1 shipped/merged/archived. This round is post-project debugging of the Teams **desktop** client (web always worked). R2 (`spaarke-SPA-external-access-platform-r2`) now OWNS the shared Entra app.

**The saga + fixes (in order):**
1. **Shared-BFF clobber**: `spaarke-bff-dev` (shared by 13+ worktrees, manual last-deploy-wins, no CI) was overwritten by another team's branch build → web 401 returned. **Fixed** by redeploying from master (worktree `src/server/**` == master, verified) via `scripts/Deploy-BffApi.ps1`. Web works again. (Durable fix deferred: add a master→dev CI deploy — the only BFF deploy workflow, `deploy-bff-api.yml`, targets PROD/`spaarke-bff-prod` (Stopped) and has failed every run since 2026-06 — effectively dead.)
2. **Desktop "Sign-in failed"** (NAA primary + Teams SSO fallback both fail). Added error-surfacing to the fail-loud screen (`src/client/external-spa/src/main.tsx` → `extractAuthDiagnostics`), deployed SPA (`gh workflow run deploy-external-spa.yml --ref work/teams-app-r1`), which revealed:
   - **NAA**: OneAuth `2002 "Access denied for the resource"` — Windows WAM/OneAuth broker denied silent `access_as_user`.
   - **SSO**: `App resource defined in manifest and iframe origin do not match` — `webApplicationInfo.resource` not domain-qualified.
3. **teams-app-r1 applied** broker pre-auth: added **Microsoft Authentication Broker `29d9ed98-a469-4536-ade2-f981bc1d605e`** to `1e40baad`'s pre-authorized apps on `access_as_user` (scope id `7e9e1e5a-…`), full-api round-trip + verified scopes intact. Backup: `c:/tmp/entra-api.json`.
4. **Coordinated with R2** (they own shared app `1e40baad`). Wrote `notes/r2-shared-entra-app-coordination.md` (full change log + governance ask). **R2 responded** (`notes/teams-sso-fix-and-entra-app-ownership.md`, commit `020c9547e`):
   - **Ratified** the broker pre-auth (Option A — NAA primary).
   - **Applied Option B** (durable SSO fallback): added identifier URI `api://green-dune-0c4f1221e.7.azurestaticapps.net/1e40baad-…` to Entra + set manifest `webApplicationInfo.resource` to it + added **`AzureAd__ValidAudiences__2`** = that URI as a `spaarke-bff-dev` **App Service setting** (deployed + regression-verified — existing-audience token still 200s).
   - **R2 now owns** `1e40baad` config; redeployed the R2 SPA to green-dune.
5. **Package re-upload** (R2's remaining step): built the package from R2's manifest. The **same-id (`23610794`) package was cache-stale** on desktop (SSO "mismatch" error persisted = Teams served the cached OLD manifest for that id). Built a **fresh-id package to defeat the cache**: `src/client/external-spa/appPackage/build/SpaarkeTeamsApp-freshid.zip` (id `965ff172-995e-4fbe-aa29-e4e0d247efde`, R2's domain-qualified resource inside).
6. **Result (2026-08-07 ~13:37)**: with the fresh-id package + cache clear + remove-old-app, **desktop sign-in SUCCEEDS** (auth error gone). **BUT the desktop tab content area is BLANK** (web renders fine). ← **current blocker**.

**Current blocker**: Desktop Teams authenticates but renders a blank content area (web works). Post-auth render or data-fetch issue, desktop/WebView2-specific. Reload + Teams restart tried — still blank.

**Key values/artifacts:**
- Shared Entra app: `1e40baad` = **SDAP-BFF-SPE-API**, obj id `c2aab303-50f8-4279-9934-503ab3a4b357`, SP `d93c832e-…`, tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2`. **R2-OWNED** now. Backup `c:/tmp/entra-api.json`.
- `access_as_user` scope id `7e9e1e5a-3b0b-4153-9753-85b41d48c6fe`. Pre-authorized: Teams web `5e3ce6c0`, desktop `1fec8e78`, **broker `29d9ed98`** (+ others). Identifier URIs incl. domain-qualified `api://green-dune-…/1e40baad-…`.
- SWA: `green-dune-0c4f1221e.7.azurestaticapps.net` (R2's redeployed SPA). BFF: `spaarke-bff-dev` (task 025 build == master; R2 added ValidAudiences setting).
- Packages (build artifacts, uncommitted): `SpaarkeTeamsApp-freshid.zip` (**use this** — id 965ff172), `SpaarkeTeamsApp.zip` (canonical id 23610794 — cache-stale on desktop).
- Teams manifest cache: keyed by **app id** → re-uploading the same id serves the cached manifest; a **fresh id forces the new manifest**.
- Coordination docs: `notes/r2-shared-entra-app-coordination.md` (teams-r1→R2), `notes/teams-sso-fix-and-entra-app-ownership.md` (R2's response).
- `AADSTS7000471` on bare `/adminconsent?client_id=…` = the `brk-…` SPA redirects; grant consent via **portal** or `az ad app permission admin-consent`, not a bare URL.

**Next diagnostic steps (for whoever resumes):**
1. Desktop console (`Ctrl+Shift+Alt+8`): Console red errors + `/api/v1/external/me` network status.
2. Render error → trace the SPA under WebView2 (R2 owns the SPA + just redeployed; may be their build).
3. `/me` failing → the desktop **SSO-fallback token** (audience `api://green-dune…/1e40baad…`) vs the web **NAA token** (`api://1e40baad…`) — token-shape/claims nuance in the BFF `CallerPrincipalResolver` or ValidAudiences; R2 owns this.
4. NAA still fails on desktop (broker `access denied`) even post-pre-auth — desktop relies on R2's SSO fallback; NAA-desktop root cause unresolved (deferred; SSO backstops it).

**Git**: worktree synced to master (merged 61 commits post-R2, 0 conflicts, BFF build 0 errors); branch pushed; PRs #746 (diagnostic + coord docs) merged. Build-artifact zips uncommitted in `appPackage/build/`.

### Task 025 BFF deploy (2026-08-06)
- **Deployed** via `scripts/Deploy-BffApi.ps1` → `spaarke-bff-dev` / `rg-spaarke-dev`. Package 48.27 MB; 4 critical files SHA-256 verified; `/healthz` 200.
- **Smoke**: `GET /api/v1/external/me` (unauth) → **401** = route registered under the dual-scheme `ExternalCollaboration` policy (expected). A workforce token now authenticates there (was CIAM-only → 401 before).
- **Live E2E (080) is the last gate**: workforce user in Teams → `/api/v1/external/me` + `/projects` should now return data (scoped to the accessible-record-set), not 401.

### Task 025 design decisions (locked 2026-08-05)
- **Plane selection**: CIAM iff token `iss` contains `ciamlogin.com` OR `tid`==Ciam:TenantId; else workforce. Deterministic, config-light.
- **Unified type** `CallerPrincipal` (Plane, ContactId, SystemUserId?, Email, Oid, ProjectAccess[]) replaces per-plane context in the /external handlers. CIAM strategy reuses ExternalParticipationService (Resolve+GetParticipations) → byte-for-byte /me + list output. Workforce strategy wraps IWorkforcePrincipalResolver + IAccessibleRecordSetService(sprk_project) → Tier-2 record scope; NOT all-projects.
- **Workforce rights** = ExternalAccessLevel.Collaborate (Read|Create|Write, no Delete) for records in the accessible set — DOCUMENTED DECISION (coordination note); R2 F3/F5 may refine per-project levels.
- **/collab** kept but marked TRANSITIONAL (removal note) — guardrail #5. SPA already calls /external/*.
- **No AzureAd audience change** (Phase-0 GREEN): workforce scheme accepts api://1e40baad v1 token.

### Live deploy + integration state (2026-08-05)
- **BFF (065) ✅ deployed** → `spaarke-bff-dev` (hash-verified, health-passed). Workforce scheme = the `1e40baad` app; `AzureAd__Audience = api://1e40baad-…` **accepts the Teams token — no config change (Phase 0 GREEN)**.
- **PCF v1.0.11 (045) ✅ deployed** → `spaarkedev1` (imported+published clean).
- **external-spa SWA ✅ deployed** → `green-dune-0c4f1221e.7.azurestaticapps.net` (Teams framing header live). `deploy-external-spa.yml` now injects `VITE_TEAMS_MSAL_CLIENT_ID/SCOPE`.
- **Teams app package** → `src/client/external-spa/appPackage/build/SpaarkeTeamsApp.zip` (app id `23610794-…`; sideloaded OK).
- **Entra app `1e40baad-…` config APPLIED** (object id `c2aab303-…`): multitenant; Teams clients `1fec8e78`+`5e3ce6c0` pre-authorized on `access_as_user`; SPA redirects include `https://green-dune-…` + **`brk-multihub://green-dune-…`** (the NAA `AADSTS700046` fix) + per-broker brk- URIs.
- **R2 decision doc**: `notes/teams-app-r1-coordination.md` (Option A + 8 guardrails — BINDING). Handoff that prompted it: `notes/spa-v2-handoff-workforce-endpoint-gap.md`.
- **Deferred at 090**: 041 internal-notify Path A/C; pre-existing `System.Security.Cryptography.Xml 8.0.3` HIGH CVE; `Spaarke.Auth` node_modules gap (flag to team); `AzureAd__ClientSecret` plaintext in App Service settings (should be KV ref — flag to ops).
- **git**: branch pushed (`5555108ab`+ deploy/workflow commits); backups `backup-teams-{wave4,premaster,postmaster,wave5,presync2}`.

### Files Modified This Session
- `notes/spikes/foundation-spike-findings.md` — code-verified go/no-go per path + operator runbook (NEW)
- `notes/spikes/teams-tab-spike/{README.md,manifest.json,index.html,teams-sso.js,config.sample.js}` — throwaway runnable spike (NEW)
- `.claude/adr/ADR-028-spaarke-auth-architecture.md` — **task 002**: applied Amendment A2 (workforce collaboration host)
- `projects/teams-app-r1/adr-028-amendment-draft.md` — DRAFT → APPLIED
- `.claude/CHANGELOG.md` — A2 amendment entry
- `tasks/002-adr-028-a2-amendment.poml` — status → completed
- `tasks/TASK-INDEX.md` — 001 → 🔄, 002 → ✅ + Wave 0 status note

### Critical Context
Code inspection found **NO architectural NO-GO**. Systemuser membership plane is wired end-to-end today (code-GO). Contact-only plane: `BuildFetchXml` `Contact` branch already binds `ContactId` (ADR-034 Path-C reuse VERIFIED), but the entry/normalization layers are systemuser-keyed and the endpoint 401s a no-systemuser caller (`MembershipEndpoints.cs:215-231`) — that gap **is** tasks 020/021, not a redesign. SPA/CIAM path is independent (no regression). The project's one true unknown = whether Teams SSO/NAA delivers a **BFF-valid workforce token in the desktop client** — inherently operator-run (a coding agent cannot sign into real Teams clients). No `src/` changes; spike is throwaway.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 001 (+ 002 available in Wave 0) |
| **Task File** | `tasks/001-foundation-spike.poml` |
| **Title** | Foundation spike — Teams-tab workforce SSO → membership (both planes) + SPA unchanged |
| **Phase** | 0 Foundation Spike & ADR |
| **Status** | 🔄 in-progress (autonomous prep complete; live validation operator-gated) |
| **Started** | 2026-08-03 |

---

## Progress

### Completed Steps (autonomous)
- [x] Step 1 (adapted): verified the multitenant Entra app + membership endpoint contract by code inspection (no live manifest registration — operator step).
- [x] Step 3: systemuser plane resolution verified end-to-end (`ResolveSystemUserIdAsync` → `IdentityNormalizationService` → `MembershipResolverService.BuildFetchXml`).
- [x] Step 4: contact plane analyzed — `BuildFetchXml` `Contact` branch confirmed (ADR-034 Path C); entry-layer gap identified = tasks 020/021.
- [x] Step 5 (code): SPA/CIAM path confirmed independent (no regression surface).
- [x] Step 7: findings + explicit go/no-go per path recorded in `notes/spikes/foundation-spike-findings.md`.
- [x] Built throwaway operator-runnable scaffold `notes/spikes/teams-tab-spike/`.

### Pending (operator-gated)
- [ ] Step 2/6: live workforce-SSO token acquisition in Teams **desktop + web** (BFF-valid token).
- [ ] Step 4 (live): contact-only plane behavior in a real client (expected 401 today).
- [ ] Step 6: desktop-vs-web Conditional-Access differences captured.
- [ ] Overall GO recorded in findings §5 → only then set 001 ✅ and start Wave 1.

### Decisions This Task
- 2026-08-03: Did NOT mark 001 ✅ — the go/no-go is a live, human-operated validation (task `<escalation>` trigger + root CLAUDE.md §6). Autonomous work delivered code-verification + operator runbook + scaffold instead of a fabricated pass.

### Blockers
- Live Teams-client sign-in cannot be performed by an autonomous agent → operator must run the spike to close the gate.
