# Task Index — Spaarke External Access Platform R3

> **Status**: Initialized 2026-10-10 by `/project-pipeline`; execution not started.
> **Total**: 52 tasks (P0: 3 · P1: 3 · P2: 1 · P3: 10 · P4: 4 · P4b A9: 9 · P5: 8 · P5b owner-check fixes: 7 · P6: 1 cancelled · P7: 5 · wrap-up: 1).
> **2026-10-13:** ADR-028 A9 tasks added (044–048, 058, 059, 074–076) from `notes/a9-bff-design.md` §10 (owner: "yes go on design note", "yes include teams"); 060 cancelled (superseded by A9); 012/056 superseded in part (notes in their POMLs); 071 widened to A9 Teams. Owner check-2 fixes added (061–063).
> **Source**: `spec.md` (FR-01–FR-26). Plan: `plan.md`. Operating manual: `CLAUDE.md`.

Legend: Tier S = sonnet, O = opus · Effort h = high · status tokens `[open]` `[wip]` `[done]` `[escalated]` `[blocked]` (the ASCII token is mandatory; FAILURE-MODES G-16).

| ID | Title | Phase | Status | Deps | Tier/Eff | Rigor | Parallel |
|----|-------|-------|--------|------|----------|-------|----------|
| 001 | ADR-028 amendment for R3 (A2, A3 :124, A1 :55) | P0 | ✅ [done] | none | O/h | FULL | — (.claude/, main session; owner approval) |
| 002 | Grid columns per owner list (FR-09) | P0 | 🔄 [wip] step 1 live on dev 2026-10-29 (allowed columns in all 6 grids); step 2 BFF allow-list in PR #2450 (UAC approved); dev deploy held by UAC 194b hold, then the 9 grid columns | none (owner list) | S/h | STANDARD | Wave 1 when the list arrives |
| 003 | Spike: Entra B2B self-service sign-up vs custom join (FR-12) | P0 | ✅ [done] | none | O/h | STANDARD | Wave 1 |
| 010 | Ciam audience forms + default-scheme CIAM guard (FR-22, FR-23) | P1 | ✅ [done] | none | O/h | FULL | Wave 1 |
| 011 | Modules by user type + partner service-request switch (FR-19, FR-06) | P1 | ✅ [done] | 001 | O/h | FULL | Wave 2 |
| 012 | Member-test branch for registered guests (FR-15) | P1 | ✅ [done] | 001 | O/h | FULL | Wave 2 |
| 020 | R3 schema: sprk_createdbycontact + intake columns (FR-25, FR-05) | P2 | ✅ [done] dev import 2026-10-11, verify pass (Merge cascade = Cascade, platform-forced) | none | S/h | FULL | Wave 1 (owner-gated import) |
| 030 | Creator stamping on every external create (FR-25) | P3 | ✅ [done] | 020 | O/h | FULL | Wave 2 |
| 031 | Document routes for Matter/WA/Invoice/Service Request (FR-24) | P3 | ✅ [done] | 030 | O/h | FULL | Wave 3 |
| 032 | DELETE own documents/to-dos/events/service requests (FR-26) | P3 | ✅ [done] | 031 | O/h | FULL | Wave 4 |
| 033 | External message read endpoint (FR-04) | P3 | ✅ [done] | 011 | O/h | FULL | Wave 3 |
| 034 | External message send endpoint (FR-11) | P3 | ✅ [done] | 030, 033 | O/h | FULL | Wave 4 |
| 035 | Intake submit endpoint (FR-05, FR-06) | P3 | ✅ [done] | 011, 020, 033 | O/h | FULL | Wave 5 |
| 036 | Email on subsequent grant (FR-07); no message email (spec C6) | P3 | ✅ [done] | 034 (UAC-r2 PR #1583 merged 2026-10-10, in branch) | S/h | FULL | Wave 5 |
| 037 | Notification feed + last-seen (FR-08) | P3 | ✅ [done] | 034, 035 | O/h | FULL | Wave 6 |
| 038 | Deploy R3 BFF to dev + live probes | P3 | ✅ [done] Phases A+B 2026-10-13 (R3 live on dev); Phase C probes folded into 074/072; dev BFF since redeployed (now c3ec5d6e5) | 010, 011, 012, 032, 035, 036, 037, 039 + 020 columns live in dev first — **satisfied 2026-10-11** (task 030 stamps sprk_createdbycontact; without it a contact create returns 400, after the upload bytes are already in SPE; tasks 033/034: the external message read selects _sprk_createdbycontact_value, so GET messages returns 500 and a contact POST fails until the column exists) | S/h | STANDARD | — (live deploy, owner-gated) |
| 039 | Event reads for Matter and Work Assignment (FR-03 server) | P3 | ✅ [done] | 032, 037 | S/h | FULL | Wave 7 |
| 040 | Workforce client switch (FR-16) | P4 | ✅ [done] code (A8 client switch) 2026-10-10; Teams live verification moved to 071 | 001 (+ owner client registration) | O/h | FULL | Wave 2 |
| 041 | Run-time backend selection (FR-17) | P4 | ✅ [done] | 040 | O/h | FULL | Wave 3 |
| 042 | Out-of-plane pages + no production mock (FR-18) | P4 | ✅ [done] | 041 | O/h | FULL | Wave 4 |
| 044 | A9 schema: contact sprk_externaltenantid + plane WorkforceHome; systemuser home-id link + key | P4b | ✅ [done] | none (owner-gated dev import) | S/h | STANDARD | A9 Wave 1 |
| 045 | BFF WorkforceHome scheme, ExternalCollaboration only, census ArchTest, must-reject tests | P4b | ✅ [done] merged e6f6c6cd7; ships only with 046 (resolver denies WorkforceHome until then) | none | O/h | FULL | A9 Wave 1 |
| 046 | BFF home principal resolution (link, contact by tid+oid, registration_required, guards d/e) | P4b | ✅ [done] merged dfee0a438 (2 Opus passes, fix round 1 bc0113e9d); on dev | 044, 045 | O/h | FULL | A9 Wave 2 |
| 047 | BFF registration endpoint POST /api/v1/external/registration | P4b | ✅ [done] merged 176857143 (2 Opus passes, fix rounds 1-3; schema label fix 69eed60c7); on dev 2026-10-16 (2c2fed38d); option 100000012 applied; UAC review #2005 clean | 046 | O/h | FULL | A9 Wave 3 |
| 048 | BFF link endpoint POST /api/v1/external/workforce/link (dual-token proof) | P4b | ✅ [done] on branch (a9c5faf1d, 6e6fa656b, 543afaefa + code-review fixes; 2 Opus passes; adr-check 0 violations; +0.06 MB); on dev 2026-10-17 (d5401cf2d) | 046 (after 047: shared files) | O/h | FULL | A9 Wave 4 |
| 043 | Production build + Teams package in CI (FR-20) | P4 | ✅ [done] 2026-10-29: workflows and dev path live (run 39489551799); remainder handed to provisioning — #2443 (external-spa-prod values, TEAMS_APP_ID, prod redirects/DNS/CORS, prod Teams dry run) | 040 | S/h | STANDARD | Wave 3 |
| 050 | Record detail view: fields, docs/invoices, events/tasks (FR-01–03) | P5 | ✅ [done] | none | S/h | FULL | Wave 1 |
| 051 | Detail messages section: read + send (FR-04/FR-11 client) | P5 | ✅ [done] | 050, 034 | S/h | FULL | Wave 5 |
| 052 | Intake wizards: wiring + NDA + Policy Question (FR-05) | P5 | ✅ [done] | 035 | S/h | FULL | Wave 6 |
| 053 | Intake wizards: Invention + Trademark (FR-05) | P5 | ✅ [done] | 052 | S/h | FULL | Wave 7 |
| 054 | Detail documents + delete-own actions (FR-24/FR-26 client) | P5 | ✅ [done] | 051, 032 | S/h | FULL | Wave 6 |
| 055 | Notification bell + deep-link record open (FR-08/FR-07 client) | P5 | ✅ [done] | 054, 037, 041 | S/h | FULL | Wave 7 |
| 056 | Join page (FR-13), flagged until 060 | P5 | ✅ [done] | 003, 041 | O/h | FULL | Wave 8 |
| 057 | Detail events + to-dos for Matter and Work Assignment (FR-03 client) | P5 | ✅ [done] | 039, 055 | S/h | FULL | Wave 8 |
| 058 | SPA multitenant sign-in, tid → backend, silent/interactive link, profile form, landing wording | P4b | ✅ [done] 2026-10-19 | 047, 048 | S/xh | FULL | A9 Wave 5 |
| 059 | Dev Entra + settings setup for A9 (live, owner-gated) | P4b | ✅ [done] 2026-10-29: stages R, A–F done 2026-10-19/20; C-1 revoked in Dewey Cheatham 2026-10-29 (dev BFF grants 0; workforce SPA grant kept) | 044, 077 (stage B) | S/h | STANDARD | A9 Wave 2 (live, alongside 046) |
| 060 | Registration service: owner host decision, then build or R4 hand-off (FR-14) | P6 | 🚫 [cancelled] superseded by ADR-028 A9 (047 + 058) | 003 + owner decision | O/h | FULL | — |
| 061 | Shared DataGrid shows option-set labels from @formattedValues (check-2 #1) | P5b | ✅ [done] reaches dev with the next SPA deploy | none | S/h | FULL | CHK2 |
| 062 | Message sender name falls back to createdby (check-2 #4) | P5b | ✅ [done] merged a379d71fa; live check of the createdby expand after the BFF deploy | none | S/h | FULL | CHK2 |
| 063 | External upload queued for indexing + profile (check-2 #5) | P5b | ✅ [done] merged 70f7649db; reaches dev with the next BFF deploy | none (owner: index + profile, 2026-10-13) | O/h | FULL | — (serial: ExternalProjectDataEndpoints.cs) |
| 064 | External document lists fail when a document has a type (sprk_documenttype option set) (check-3 #1) | P5b | ✅ [done] merged 15f2dc686; reaches dev with the next BFF deploy | none | S/h | FULL | CHK3 |
| 065 | MDA message send carries "Internal only" (default No) + sender (check-3 #5) | P5b | ✅ [done] merged bbcb80659 (D1-D9; ISS-009 done, ISS-010 #1864 filed); reaches dev with the next BFF deploy | none | O/h | FULL | CHK3 |
| 066 | Documents grid / document detail: download or open the file (check-3 #2-3) | P5b | ✅ [done] reaches dev with the next SPA deploy | none | S/h | FULL | CHK3 |
| 067 | Service Request automatic numbers RQST-{SEQNUM:4} (check-2 #3) | P5b | ✅ [done] dev applied 2026-10-13 (next value 3); owner create-test pending | none (owner yes 2026-10-13; dev apply gated) | S/h | STANDARD | CHK3 |
| 068 | "Internal Only" switch on message and email compose surfaces; column and form label "Internal Only" | P5b | ✅ [done] on dev and owner-checked 2026-10-15; "External / Internal Only" tags in the Messages modal and New Email (7419e8637, 247ce6a6e) | 065 | S/h | FULL | CHK3 |
| 070 | Deploy SPA + dev Teams package to dev | P7 | 🔄 [wip] SPA live from master 28274e04e; dev Teams package built (run 39489551799); owner uploads it + smoke (notes/task-070-deploy-record.md) | 038, 042, 043, 053, 055, 056, 057 | S/h | STANDARD | — (live, owner-gated) |
| 071 | Teams live check: contact, licensed user, unlisted directory × desktop/web/mobile (FR-21, A9) | P7 | 🔲 [open] after the owner installs the 070 package | 070, 075, 076 | S/h | STANDARD | — (owner-driven) |
| 072 | Both-plane E2E on dev, success criteria 1–12 + FR-10 | P7 | 🔲 [open] needs 002 + 071 | 002, 070, 071 | S/h | STANDARD | — (live) |
| 073 | Production deploy to external.spaarke.com (gated) | P7 | 🔲 [open] BLOCKED — ACME gate items missing (notes/task-073-gate-acme.md): stamp v2/pre-auth/BU setting/removeUnverifiedEmailClaim, T240c/T240d, consent design, DNS, prod environment | 072 + T240c, T240d, DNS, owner go | S/h | STANDARD | — (blocked until gates) |
| 074 | A9 live probes on dev | P7 | 🔄 [wip] probes 1-5, 7, 8 ✅ (2026-10-29: licensed-user link, resubmit, existing-record bind, intake; notes/task-074-a9-probes.md); probe 6 unlisted directory pending (needs an other-directory account) | 058, 059, 075 | S/h | STANDARD | — (live, owner-gated) |
| 075 | SPA Teams strategy → multitenant NAA (Teams context tid), registration + link in Teams | P4b | ✅ [done] 2026-10-19 | 058 | S/h | FULL | A9 Wave 6 |
| 076 | Teams package + registration for A9 (manifest, brk-multihub, 043 pipeline) | P4b | ✅ [done] 2026-10-19 | 075 | S/h | FULL | A9 Wave 7 |
| 077 | BFF default scheme accepts the bare app-id audience (v2 tokens) before the 059 v2 switch | P4b | ✅ [done] 2026-10-19 | 045 | O/h | FULL | A9 (from 059 stop S-1; owner 2026-10-19) |
| 078 | ISS-010 quick fix: read_query / search_data hide internal-only communications from licensed users flagged external | R3 hardening | ✅ | 065 | O/h | FULL | ISS-010 #1864 (owner 2026-10-20) |
| 090 | Project wrap-up | Wrap-up | 🔲 [open] | all | S/h | FULL | — (serial) |

## Parallel Execution Plan

At most 4 agents per wave (`CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS=4`, `.claude/constraints/agent-cost.md` rule 5); every wave below fits. Tasks that touch `.claude/` (001, and 090 if it promotes lessons) run in the main session only.

| Wave | Tasks | Prerequisite | Files touched (no overlap inside a wave) | goal-eligible |
|---|---|---|---|---|
| 1 | 003, 010, 020, 050 (+ 002 once the owner list arrives); 001 in the main session alongside | none | notes/spikes · AuthorizationModule.cs · Dataverse solution · external-spa detail view · grid config | NO (auth, schema, owner gates) |
| 2 | 011, 012, 030, 040 | 001 (011, 012, 040); 020 (030) | ModuleEntitlementResolver/ExternalAccessModule/options · WorkforceIdentityOptions/binder/resolver · ExternalDataService + ExternalProjectDataEndpoints · external-spa auth | NO (auth) |
| 3 | 031, 033, 041, 043 | 030; 011; 040; 040 | ExternalProjectDataEndpoints + ExternalDataService · new ExternalMessageEndpoints + ExternalAccessEndpoints · SPA auth/backend-selection · workflows + prod manifest | NO (auth) |
| 4 | 032, 034, 042 | 031; 030 + 033; 041 | ExternalProjectDataEndpoints + ExternalDataService · ExternalMessageEndpoints + CommunicationService · SPA pages | NO (auth) |
| 5 | 035, 036, 051 | 011 + 020 + 033; 034 + #1583; 050 + 034 | new ExternalIntakeEndpoints + ExternalAccessEndpoints + ExternalDataService · GrantAccessNotifier + ExternalMessageEndpoints · SPA RecordMessages | NO (auth) |
| 6 | 037, 052, 054 | 034 + 035; 035; 051 + 032 | new ExternalNotificationFeedEndpoints + ExternalAccessEndpoints + ExternalDataService · SPA intake + QuickStartPane · SPA detail + DocumentLibrary | NO (auth) |
| 7 | 039, 053, 055 | 032 + 037; 052; 054 + 037 + 041 | ExternalProjectDataEndpoints + ExternalDataService · SPA intake · SPA shell + msal-auth + App.tsx | NO (auth) |
| 8 | 038, 056 | Wave 7 + 010/012/036; 003 + 041 | live BFF deploy · SPA join page + App.tsx | NO (deploy, auth) |
| 9 | 070 → 071 → 072 → 073 → 090 | sequential | live deploys and verification | NO (deploy, live) |
| — | 060 | cancelled 2026-10-13 | superseded by ADR-028 A9 | — |
| CHK3 | 064, 065, 066 | none | ExternalDataService.cs · CommunicationService.cs + sprk_communication_send · external-spa detail + DocumentLibrary (no overlap; 064 vs 063: 063 is ExternalProjectDataEndpoints.cs) | NO (security-relevant 065) |
| CHK2 | 061, 062 | none | shared DataGrid.tsx · CommunicationThreadReadService.cs (no overlap) | NO (2 tasks) |
| A9-1 | 044, 045 | none | Dataverse solution + schema script · AuthorizationModule + ArchTests | NO (auth, schema) |
| A9-2 | 046; 059 (live, alongside) | 044 + 045; 044 | ExternalAccess resolver/decision/store · Entra + app settings | NO (auth, live) |
| A9-3..7 | 047 → 048 → 058 → 075 → 076, then 074 | sequential (shared files) | ExternalAccessEndpoints + binder/store · SPA auth/* · appPackage + workflow | NO (auth, live) |

**No wave is `/goal`-eligible.** Every wave includes auth or security-sensitive work, a live deploy, or an owner gate (task-create Step 3.85).

## Critical Path

```
001 → 011 → 033 → 034 → 035 → 037 → 039 → 038 → 070 → 071 → 072 → 073 → 090
001 → 040 → 041 → 042 ──────────────────────────────┘
050 → 051 → 054 → 055 ──────────────────────────────┘
020 → 030 → 031 → 032 ┘
```

## Shared-file chains (why some tasks are sequential)

| File | Order |
|---|---|
| `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` | 030 → 031 → 032 → 039 |
| `Infrastructure/ExternalAccess/ExternalDataService.cs` | 030 → 031 → 032 → 035 → 037 → 039 |
| `Api/ExternalAccess/ExternalAccessEndpoints.cs` (route registration) | 033 → 035 → 037 |
| `Api/ExternalAccess/ExternalMessageEndpoints.cs` | 033 → 034 → 036 |
| `external-spa/src/components/detail/RecordDetailModal.tsx` | 050 → 051 → 054 → 055 |
| `external-spa/src/components/shell/QuickStartPane.tsx` | 052 → 053 |
| `external-spa/src/auth/*`, `App.tsx` | 040 → 041 → 042 → 055 → 056 |

## High-risk and coordination

- **A9 (044–048, 058, 059, 074–076):** BFF tasks 045–048 run on opus with **two adversarial passes** each (auth). 046/047 build on UAC #1801 (on master) and **rebase onto UAC task 185 if it merges first** (shared: binder, decision, store, `CallerPrincipalResolver`). Send PRs touching `WorkforceIdentityOptions.cs`, `WorkforcePrincipalResolver.cs` or `ContactIdentityBinder.cs` to UAC for review. 059 and 074 are live and owner-gated (commands + rollback first).

- **010, 012, 032, 033, 034, 037:** security guards. Each needs a seeding proof and two adversarial passes for auth/tenant isolation (root CLAUDE.md §8.5).
- **036:** UAC-r2 PR #1583 (`GrantAccessNotifier`) merged 2026-10-10 and is merged into this branch; 036 now waits only on 034.
- **Shared surfaces:**
  - `/conflict-check` before every BFF PR (UAC-r2 PRs #1583 and #1586 touch the same files);
  - PR #1494 touches `Services/Communication`;
  - dependabot PR #909 touches both SPA workflows.
- **Owner gates:** 001 (ADR edit), 002 (column list), 020 (schema import), 040 (client registration), 060 (host decision), and every deploy (038, 070, 073).
- **060:** if the owner does not decide in time, FR-14 moves to R4 with a written path and 056 stays flagged off.

## How to execute

1. Run `dotnet build src/server/api/Sprk.Bff.Api/` before any BFF wave; build checks between waves per `project-pipeline` Step 5.
2. Run `task-execute` per task. For a parallel wave, send one message with up to 4 agents.
3. 001 is main-session-only.
4. Run at most one other heavy project on this machine at the same time (agent-cost rule 5). The auth project's remediation phase counts.

## Model and effort (`.claude/constraints/agent-cost.md`)

- **Task execution.** Launch the `implementer` agent definition with `model` = the POML's `<model-tier>`. Its frontmatter sets `effort: high`, and every POML here says `high`, so the declared effort is the applied effort. (The Agent tool takes `model` only; a different POML effort would be silently ignored.)
- **Split.** 17 opus tasks (auth, security and tenant-isolation code, ADR, spike, registration service) and 17 sonnet tasks, per task-create Step 3.5.5b.
- **Review.**
  - One `adversarial-reviewer` pass (fable/high) per change set.
  - Two passes for the auth/tenant-isolation tasks: 010, 011, 012, 030, 032, 033, 034, 037, 040, 041, 056, 060.
  - Fix rounds and re-checks of a fix diff run on sonnet/high.
- **Mapping and search** use `code-mapper` (sonnet/low). **Platform research** (003, 060) uses `researcher` (opus/high).
- **Escalate on evidence.** If a sonnet task's result is wrong despite a clear brief, re-run it one tier up and record why in the task notes.
