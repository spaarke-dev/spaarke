# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-19 (tasks 022/021/023/028/025 COMPLETE = 12 tasks ✅; CIAM provisioner landed; next 027 xhigh download-authz)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none active — **Phase 0 + 020/024/026 + 022/021/023/028/025 COMPLETE**. 12 tasks ✅. |
| **Step** | 025 done; next 027 (last xhigh) |
| **Status** | 12 ✅ (001, 002, 003, 004, 020, 021, 022, 023, 024, 025, 026, 028) |
| **Next Action** | **027** (external doc download endpoint, **xhigh** — authz-before-stream: enforce `sprk_externalrecordaccess` + document→project scoping BEFORE resolving Graph pointers/streaming; reuse `SpeFileStore.DownloadFileAsync`; endpoint keyed on `documentId`, no driveId/driveItemId exposed; **negative 403/no-bytes test is the single highest-consequence property**; deps 021+023 ✅, on ExternalProjectDataEndpoints). Then **029** (core-user invite trigger, deps 025) → **030** (unit tests for CIAM surface, deps 020/021/022/023/025/026/027) → **031** (deploy BFF). **OPEN ESCALATIONS (notes/defer-issues.md)**: DI-028-01 CIAM SPA+BFF-API app regs (ops — blocks live auth + 031 substitution); DI-028-02 external-spa build (blocks 014); DI-025-01 provisioner partial-failure hardening (post-R1). Runtime prereq for 031: BFF MI needs KV 'Secrets User' on spaarke-spekvcert. |

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
