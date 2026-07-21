# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-20 (context-handoff before /compact — 24/25 tasks done; ALL agent work committed + pushed + MERGED TO MASTER @ a9f6ca180; only owner Power-Pages-site teardown [041] + 090 wrap-up remain. TWO POST-COMPACT FOLLOW-ONS requested — see plan below.)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | External Access Platform R1 — **24 of 25 tasks ✅**. Phase 0/1/2 + 040 parity + 042 docs done; 041 repo-half done. BFF live @ spaarke-bff-dev (CIAM wired), SPA live @ green-dune SWA; owner-verified sign-in + scoping + new-grant propagation. |
| **Step** | All agent work committed + pushed + **merged to origin/master @ a9f6ca180** (branch 0 behind/0 ahead). Working tree clean. |
| **Status** | 24 ✅ (001-004, 010-014, 020-031, 040, 042); 041 🔄 (repo-half done, owner site-teardown pending); 090 ⏳ (blocked on 041). |
| **Next Action** | **Post-compact, the user asked for 3 things (do in order):** **(A) ADMIN-GUIDE NEW-USER-SETUP CLARITY** — make `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md` crystal-clear on how to onboard a new external user, EXPLICITLY stating the current API-only reality (no MDA/ribbon UI yet — DI-029-01). The shipped flow: a core user calls `POST /api/v1/external-access/invite-and-grant` {email, ProjectId, AccessLevel(100000000/1/2), firstName, lastName} → BFF creates/resolves Contact, idempotently provisions a CIAM local account (Graph POST /users in spaarkeextid), binds oid→`Contact.sprk_externalobjectid`, creates `sprk_externalrecordaccess` grant, sends onboarding email → user opens the SWA portal (green-dune-0c4f1221e.7.azurestaticapps.net), sets password via **SSPR Email OTP** ("Forgot password"; isSignUpAllowed=false), signs in, sees granted projects/docs. Add a copy-paste curl/Postman example + the SSPR step. **(B) SCOPE `external-access-platform-r2`** — run `/design-to-spec` → `/project-pipeline`; enhancement backlog in the section below. **(C) FINISH R1**: once OWNER retires the `sprk-external-workspace` Power Pages site (Power Platform admin — agent can't; it's a Power Pages SITE, not a Dataverse web resource — confirmed found:0 web resources), flip 041→✅ and run **090 wrap-up** (`/test-diet` + close-out). |

### Files Modified This Session
- **All committed + pushed + on master** (`a9f6ca180`). Nothing uncommitted. Key commits: 011/012 (BrowserRouter+deep-link), 013 CORS, 020-031 (CIAM auth chain + tests + BFF deploy), CIAM App Service config, deploy-external-spa.yml CIAM env fix, 040/041-repo/042 docs.

### Critical Context (for continuation)
External Secure Project Workspace migrated Power Pages+B2B → **Azure Static Web Apps + Entra External ID (CIAM), broker-only** (ADR-028 Amendment A1). Live + owner-verified. Only the owner's Power-Pages-site teardown + 090 wrap-up remain to close R1.

---

## 🔜 POST-COMPACT CONTINUATION PLAN (user-requested 2026-07-20)

### (A) Admin-guide new-user-setup clarity
Root cause of owner's "I'm not clear on this process": onboarding is **API-only, no UI** (DI-029-01). Rewrite/augment the `EXTERNAL-ACCESS-ADMIN-SETUP.md` "onboard a new user" section to be step-by-step + explicit that a ribbon/MDA button doesn't exist yet. Include: the exact `invite-and-grant` request (curl example), access-level integers, what the BFF does, the user's SSPR password-set step, and where they sign in. Verify against code (`InviteAndGrantExternalUserEndpoint.cs`, `CiamUserProvisioningService.cs`, `ExternalCallerAuthorizationFilter.cs`).

### (B) external-access-platform-r2 scoping (enhancement backlog)
Run `/design-to-spec` for a new project `external-access-platform-r2`. Candidate scope (from `notes/defer-issues.md` + gaps found this session):
1. **DI-029-01 — "Invite to Secure Workspace" UI**: MDA/ribbon command button on the Matter/Project form that collects Project + attorney Contact + access level and calls `invite-and-grant` (the missing UX that makes onboarding unclear today). Dark-mode ADR-021.
2. **DI-025-01 — provisioner partial-failure hardening**: self-heal the create-CIAM-ok / persist-oid-fail window (on POST /users 409, look up existing CIAM user by email → recover oid → continue); optional "resend onboarding" on the idempotent path.
3. **DI-030-01 — live-E2E test coverage**: invalid-*issuer* real-JWT rejection + real oid-resolution logic (bound-oid not hijacked by mismatched email) — needs a CIAM test user.
4. **Self-service sign-up / Legal Front Door**: currently `isSignUpAllowed=false` (admin-initiated only). R2 could add a gated self-service path.
5. **SSPR verification**: confirm Email OTP is enabled in the CIAM tenant (Entra > Protection > Authentication methods) — pending item in config/environments.json dev.ciam.
6. **Cleanup**: remove the dead Power Pages dev-proxy block in `src/client/external-spa/vite.config.ts` (`/_api`,`/_layout`,`/_services` → `sprk-external-workspace.powerappsportals.com` — vestigial post-SWA); optionally enable push-to-master auto-deploy on `deploy-external-spa.yml` (currently workflow_dispatch only).
7. Consider: per-document deep-link, richer participation UI, revoke/close-project flows exercised E2E.

### (C) Close R1
Owner retires Power Pages site `sprk-external-workspace` → 041 ✅ → run 090 wrap-up (`/test-diet` reconciles the 030 tests vs ADR-038; close-out PR). Then merge-to-master (already current).

### Completed this session (2026-07-19)
- **004** — `contact.sprk_externalobjectid` (String/100) created live on `spaarkedev1`, in SpaarkeCore + SpaarkeMaster, published, queryable. Doc: `notes/data-model-sprk_externalobjectid.md`. MetadataId `b28603f2-bd83-f111-8076-7ced8ddc4cc6`.
- **024** — `SendCiamOnboardingEmailAsync` + `CiamOnboardingTemplate.html` (auto-embedded via existing `*.html` wildcard; no .csproj change). Reuses shared-mailbox app-only pipeline.
- **026** — removed synthetic `contact_{guid}` SPE grant + dead helpers/DTO/usings/`IGraphClientFactory` param from `GrantExternalAccessEndpoint.cs`; preserved `sprk_externalrecordaccess` create + ADR-009 cache invalidation.
- **Gates:** build 0 errors; 133 tests pass; publish **47.03 MB** compressed (−2.60 MB vs 49.63 baseline, ≤60 ✓); code-review + adr-check both clean (0 critical/0 warnings).
- **003** — SWA `swa-spaarke-external-spa-dev` (rg-spaarke-dev, westus2, Free) provisioned, host `green-dune-0c4f1221e.7.azurestaticapps.net` (HTTP 200). Deploy token → GitHub secret `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV`. Scaffold workflow `.github/workflows/deploy-external-spa.yml` (workflow_dispatch only). Hostname in config `dev.externalSpa`.
- **001 (PARTIAL)** — CIAM tenant `spaarkeextid.onmicrosoft.com` / tenantId `7052feba-bfc4-43e0-b09e-65014b429131` created (MAU billing); RP `Microsoft.AzureActiveDirectory` registered; authority in config `dev.ciam`. SSPR + `isSignUpAllowed=false` flow ESCALATED (403 under headless CLI token — needs admin consent). No user flow exists yet ⇒ sign-up ABSENT (safe interim).

### ✅ RESOLVED — CIAM tenant admin bootstrap (was 001 tail + 002)
Owner completed the interactive portal steps 2026-07-19: app reg + `User.ReadWrite.All` admin consent, SSPR Email OTP, and (I set via Graph) `isSignUpAllowed=false`. Cert created in KV + public uploaded to app. Phase 0 fully done.

### ⚠️ CLI note for next session
The Azure CLI token cache is polluted for `az role assignment` / Graph-resolve ops (side effect of `az account get-access-token --tenant <ciam>` calls) — those return `MissingSubscription`. ARM resource ops + KV data-plane work fine. If a future step needs role-assignment/`az ad` ops, run a fresh `az login` first. Does NOT affect the code tasks (020/022/etc.).

### ⚠️ Carry-forward for TASK 025 (provisioner, which calls 024's method)
- Pass a **config-derived `portalUrl`** to `SendCiamOnboardingEmailAsync` — it is inserted into HTML **un-encoded** (like existing `{{AccessUrl}}`); MUST be trusted server config, never user input.
- `SendCiamOnboardingEmailAsync(recipientEmail, firstName, portalUrl, ct)` is minimal — 025 may need an extra field (org / display-name); extend the signature then, not speculatively.
- **026 follow-up:** `GrantAccessResponse.SpeContainerMembershipGranted` is now always `false` (vestigial). Deferred dropping it from the public DTO (touches contract + 2 DTO tests + external-spa consumer) — track if the field is confirmed unused.

### Where things stand (fresh-session summary)
- **Design → Spec → Pipeline all DONE and committed + pushed.** No PR opened yet (branch is planning-only; open one when implementation code lands, or a draft for visibility).
- **ADR-028 Amendment A1 APPLIED** to `.claude/adr/ADR-028-spaarke-auth-architecture.md` (CIAM sanctioned for external surface, broker-only invariant, E-3 boundary).
- **BFF audit done** (3-track) — reuse map is baked into `spec.md`/`plan.md`/`CLAUDE.md`. Key reuse: `SpeFileStore.DownloadFileAsync` (no new download method), `SpeAdminTokenProvider` (cross-tenant client template), `GraphUserService`/`PasswordGenerator`, `RegistrationEmailService`, extend `ExternalCallerAuthorizationFilter` (don't fork).
- **Registered** in `projects/INDEX.md` (BFF=Y narrow, CI=Y).
- **25 POMLs validated** (Validate-TaskPoml.ps1: 0 errors/0 warnings); TASK-INDEX has the DAG + 16 waves; **no `/goal`-eligible waves** (auth/deploy/irreversible).

### Critical Context
Hosting + identity migration (Power Pages + B2B → Azure SWA + Entra External ID/CIAM), broker-only. Type-2 (CIAM/MAU) only; Type-1 demo-registration out of scope. Two `xhigh` correctness-critical tasks: **025** (provisioner) + **027** (download authz-before-stream, negative test is the key property). Phase 0 = live Azure/CIAM ops provisioning (why execution paused). See `CLAUDE.md` for binding project rules + decisions.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | — |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps
*No steps completed yet*

### Files Modified (All Task)
*No task files modified yet*

### Decisions Made
*See [`CLAUDE.md`](CLAUDE.md) "Decisions Made" for project-level decisions*

---

## Next Action

**Next Step**: Run `/task-create` to generate POML task files from `plan.md`, then execute Phase 0.

**Pre-conditions**: spec.md + plan.md finalized (done); ADR-028 Amendment A1 applied (done); baseline builds (verified).

**Key Context**: Phase 0 (foundations: CIAM tenant/app + SWA resource + `sprk_externalobjectid`) gates Phases 1–2 and depends on live Azure/CIAM provisioning.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-07-19
- Focus: Project initialization (design → spec → BFF audit → artifacts). Pipeline paused before task execution per owner request.

### Key Learnings
- BFF audit found significant reuse (download, provisioning, email, auth) — scope is smaller than the raw spec implied.

---

## Quick Reference

### Project Context
- **Project**: spaarke-SPA-external-access-platform-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) (pending task-create)

### Applicable ADRs
- ADR-028 (+Amendment A1): CIAM external identity/auth
- ADR-008: endpoint authorization filters
- ADR-009: Redis-first caching
- ADR-007: SpeFileStore facade

---

*This file is the primary source of truth for active work state. Keep it updated.*
