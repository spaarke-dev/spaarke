# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-03
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | ▶ **NEXT: execute task 025** (principal-agnostic collab endpoints — Option A). 22/25 core done; both deploys live; live Teams E2E blocked on 025. |
| **Step** | Live Teams integration reached auth-complete but 401s on data. R2 decided Option A (dual-scheme /external). Task 025 spec written. Phase 0 verified (no audience blocker). |
| **Status** | 🟢 Auth chain PROVEN live in Teams (NAA workforce token acquired). ❌ Data 401: SPA calls CIAM-only /api/v1/external/*; workforce plane (/collab) has only /me+download. Fix = task 025. |
| **Next Action (post-compact)** | **Execute `tasks/025-principal-agnostic-collab-endpoints.poml`** via task-execute (opus@xhigh, main-session/opus-subagent). It has the full binding R2 guardrails. Then rebuild+redeploy BFF (065 mechanism), operator re-runs live Teams E2E (080), then 090 wrap-up. |

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
