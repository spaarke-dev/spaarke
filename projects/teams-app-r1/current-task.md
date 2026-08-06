# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-05 (by context-handoff — pre-compaction checkpoint)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **RESUME**: read the Quick Recovery table + "Live deploy + integration state" below, then execute `tasks/025-principal-agnostic-collab-endpoints.poml` via task-execute.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | ✅ **025 COMPLETE** (principal-agnostic /external, Option A). Live Teams E2E PASSED (operator-verified 2026-08-06 — "the teams app opens", workspace loads). |
| **Step** | Next work: **080** (full E2E graduation-criteria verification) → **090** (project wrap-up: gates + /test-diet + docs + deferrals). |
| **Status** | 🟢 Shipped + deployed + live-verified: dual-scheme `/api/v1/external` serves CIAM + workforce via `CallerPrincipalResolver`; workforce scoped to accessible-record-set (NFR-08); CIAM byte-for-byte. BFF live on `spaarke-bff-dev`. R2 coordination note delivered. |
| **Next Action** | Run **080** (verify all 7 graduation criteria), then **090** wrap-up. Deferrals for 090: 041 internal-notify Path A/C; pre-existing `System.Security.Cryptography.Xml 8.0.3` HIGH CVE; `Spaarke.Auth` node_modules gap; `AzureAd__ClientSecret` plaintext→KV-ref (ops); §7/§8-D2 cleanup (delete `/collab` transitional group + inert `ExternalCallerAuthorizationFilter`). |

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
