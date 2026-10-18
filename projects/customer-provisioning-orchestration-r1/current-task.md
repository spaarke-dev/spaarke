# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md` D-table. Session narrative → commit messages.

> **Last Updated**: 2026-10-17 SESSION 49. T186 ACME run `b6941254-ac1b-42a6-8b12-dec7587090fc` is at H14 (H14m), blocked on an owner decision (L62). Branch merged with master `bed14dd67`+ (UAC #2025) at `4b2d0efc9`; HEAD `74aa190fd`, pushed.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | T186: first live E2E via `/provision-environment acme` ("ACME Corporation", DEMO sub `2ff9ee48-6f1d-4664-865c-f11868dd1b50`, dev L2 control plane, managed SpaarkeMaster 1.2.3.1). Run log: `notes/t186-acme-run-log.md` (lessons L1–L62 + timeline), kept current AS THINGS HAPPEN. |
| **Status** | Run **Failed @ H14** (Resumable, `h14m-mailbox-ensure-failed`). Done: H0 H1 H2a H2b H3 H4 H4b H5 H6 H7 H7b H8 H9 H10 H11 H12a H12b H12c H14a. Remaining: H14 (H14m), then H13. Nothing created in Exchange. No agents or background tasks running. |
| **Blocker (L62)** | Exchange now refuses `New-Mailbox -Shared` and `Add-DistributionGroupMember` for `Spaarke Exchange Admin`: 403 `X-ExceptionType: CmdletAccessDeniedException` (raw adminapi replays as the app, `-WhatIf`, 2026-10-16 22:18–22:28 UTC); `New-ManagementScope` and all reads still allowed; all 8 assignments enabled. Both writes passed M4 on 2026-10-13. The D32 roles are assigned directly with `-App`, which Microsoft documents for neither admin role. |
| **Next Action** | Owner decides (asked 2026-10-16): **recommended option 1** — role group `Spaarke Mailbox Provisioning` holding `Spaarke Shared Mailbox Create` + `Spaarke Scope Group Membership` (`-CustomRecipientWriteScope 'Spaarke Customer Scope Groups'`), member = SP `b5d396bb-d8a7-4017-875c-d999a4692163` (owner runs; no Entra role). Then: rerun `scratchpad/t186-h14m-whatif.ps1`-style `-WhatIf` probes as the app (30 min–2 h propagation) → `POST /resume` → poll to H14 ✓ and H13 → update guide §4.2.1 step 5 / PRQ-E-16 → remove the two old `-App` assignments. Option 2 (fallback): `Set-ServicePrincipal -OverrideEnforceExoAppRbacPermissions $true` (undocumented). |

## Live state

- Worker sidecar `exchange-policy-sidecar` = `provisioning-sidecar:2026.10.16-38` (L61 diagnostic, master `292c33f46`); dev bicepparam pin matches (guarded by `SidecarImagePinTests`). Roll forward: ARM GET/PUT `.../sites/spaarke-provisioning-controlplane-worker-dev/sitecontainers/exchange-policy-sidecar?api-version=2024-04-01` (`az webapp sitecontainers update` crashes), then `Verify-Sidecar-Live.ps1 -Environment dev`.
- Control plane API/Worker code: last deployed before L54/L55/L57 were proven; the #2034 fix (246f6f166) is on the branch only — redeploy the control plane before the next customer run.
- In-tenant diagnostics as the app: scratchpad scripts `t186-h14m-*.ps1` create/delete ACI `sprk-sidecar-*-dev` in `rg-spaarke-platform-dev` (Worker UAMI → MI-FIC → `Spaarke Exchange Admin`). EXO 3.5.1 reads 403 bodies as zero bytes in both 10-13 and 10-16 images — use the `X-ExceptionType` header.
- Local Pester: on this Windows machine Pester 6.2.0 fails the sidecar suite at import (HEAD too); run under the saved Pester 5.7.1 (`scratchpad/psmods`). CI (Linux, Pester 6.2.0) passes.

## 🔔 Waiting on the owner

1. **L62 Exchange role grant** (above) — blocks T186.
2. **L56 AI content for ACME**: ACME has 0 playbooks / nodes / actions / tools / consumers (dev: 34 / 93 / 80 / 51 / 40); H12a seeded 4 of 12 sets and the repo lacks dev's content. Recommended: export what the 40 consumer rows reach from dev with ids preserved, port a PK-preserving writer into H12a (tools → actions → playbooks → N:N → nodes after H12c → node N:N → canvas → consumers), re-seed ACME. Does not block the run.
3. **#2027 org auditing** (code done `c28d9dc48`: H6 contract `isauditenabled=true`, `auditretentionperiodv2=-1`, read-back): OK needed for two live writes — spaarkedev1 retention null→-1 (uac-r2 waits to verify read-only), and ACME (H6 already ran under the old contract). Tell uac-r2 when dev is set.
4. Demo accounts not classified: `jake.schroeder`, `rachel.schroeder`, `sarah.johnson`, `jane.smith`, `alex.demo` @demo.spaarke.com.
5. Dev BFF secrets an agent printed locally (webhook secret, signing key, SignalR key): rotation is the owner's call.
6. ISS-026 publisher verification (Partner ID 7011236) — waits on Partner Center facts.

## 📋 Remaining

- **When this branch merges to master**: PR body must say `Closes #2034`, `Closes #2040` and `Closes #2027` (UAC offered a review of the isdisabled check — send them the PR number).
- **After T186 completes**: Step 6a registry reconcile; handoff `runs/{runId}.md`; 6e/6f; Step 7; carry lessons L1–L62 into the guide + prereqs; fix L15 and L30; remove the old broad L2 Graph roles; track the S18 columns.
- **Report to the owner**: S1 plan cost; L49 ComposeIdentityKey DEGRADED on the ACME BFF; task 264 ordering vs task 047; registry table shipping in SpaarkeMaster (does the BFF need it per environment?).
- **T186 live proofs still open**: ISS-008 (second POST refused); H14m route-level M4; H13 census (T260); PRQ-C-14.
- **Repo cleanup of old-demo references**: `config/environments.json:77-115`, `config/spaarke-resources.yaml:601-630`, `spaarke-constants.yaml:79`, `scripts/Reconcile-DemoEnvironment.ps1`, `redis-demo.bicepparam`, `DocumentOperations.js:164`, docs.
- **Builds**: D36 (H4b keyless SignalR + `Rag__ApiKey` note); D37 (`appRoleAssignmentRequired` after A9); T240c (D38); T240d; A9 H3 changes (wait on SPA-r3 task 059); 203d; wrap-up 090.
- Leftovers for the user to delete: `C:\code_files\spaarke-wt-spaarkemaster-1231`, `C:\code_files\spaarke\.claude\worktrees\agent-aaff1644fa899f03b`; worktree `C:\code_files\spaarke-wt-sidecar-l59` (branches merged) — remove only with `scripts/worktree/Remove-WorktreeSafe.ps1`.

## 🐞 Open defects

#1543 ISS-018 · #1484 ISS-008 · #1485 ISS-009 · #1487 ISS-011 · #1499 ISS-012 · #1500 ISS-013 · #1523 ISS-017 · #1561/#1562/#1563/#1564/#1570 · #1590 ISS-023 · #1803 ISS-025 · #1811 ISS-026 · #1504 · #1432 · #1376 · #1377 · #2034 / #2040 (fixed on branch; close on merge).
