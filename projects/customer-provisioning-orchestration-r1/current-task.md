# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Older detail: `notes/handoff-history/current-task-archive-2026-11-06.md` and git history of this file.

> **Last Updated**: 2026-11-26 ~02:00Z (Desktop session; demo plan decision 1 approved; operator-tools client work IN PROGRESS).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **ACTIVE: demo plan decision 1 (owner "yes register operator sign-in client on demo", 2026-11-26).** Branch `work/demo-operator-tools-client` in worktree `C:\code_files\wtopc` (from master `5577cace71`), NOTHING COMMITTED yet. Done in the worktree: (a) `scripts/provisioning/Register-OperatorToolsClient.ps1` (public client "Spaarke Operator Tools (Demo)", single tenant, device code + http://localhost, no secret/cert, SP appRoleAssignmentRequired, assigns operator; `-WhatIf` ran clean: app does not exist yet); (b) registry snapshot gains `EnvironmentType`/`IsActive` (`sprk_environmenttype`, `sprk_isactive`; `IsActiveDemo` = type 1 AND active) in `Registry/DataverseEnvironmentRegistryClient.cs` + `IDataverseEnvironmentRegistryClient.cs`; (c) `EntraAppRegOptions.DemoPreAuthorizedClientAppIds`, rejection codes `appreg-demo-preauthorized-client-invalid` + `appreg-registry-environment-type-unavailable`, H3 step (4d) unions the Demo clients only when the run's registry row (by `run.EnvironmentId`, same customerId) is an active Demo; read failure → Resumable refuse; no Demo ids → no registry read; (d) H3 tests Demo_1..Demo_6 added + A42 test ctor updated (NOT yet built/run). |
| **Next Action** | 1) `dotnet test` the ControlPlane tests (H3*, A42*, Registry*); add ParseSnapshot tests for the two columns. 2) Worker Bicep: param `demoPreAuthorizedClientAppIds` in `infrastructure/bicep/modules/controlplane-worker-app-service.bicep` + `platform-controlplane.bicep` (copy the `copilotAgentClientAppId` pattern), settings `EntraAppRegOptions__DemoPreAuthorizedClientAppIds__N`; rebuild `platform-controlplane.json`. 3) Docs: deployment guide H3 row, PROVISIONING-PREREQUISITES, `.claude/constraints/provisioning.md` (main session). 4) Auth change: TWO adversarial reviews (Fable), then PR. 5) Run the script live (owner approved creating the client); put the app id in `parameters/platform-controlplane-dev.bicepparam`. 6) Owner OKs still needed: dev L2 Worker deploy, then an ACME upgrade run so H3 pre-authorizes it. Earlier today: demo dataset loaded on ACME (123 rows); Step 6e probes all PASS; demo plan `notes/demo-environment-plan.md`. |
| **PRs** | **#3188 ISS-041 MERGED `c3a42ad85a`** (guard widened; issue #3090 closed). **#3187 ISS-044 MERGED `178fa6d08d`** (2026-11-26, all 36 checks green after an LFS-flake rerun; #3183 closed). Temp worktree `wt044` can be removed. |
| **Intake engine** | ISS-046 #3210 accepted from intake-pipeline-r1 (2026-11-26): H2a/H4b/H1/H13 wiring for the opt-in Bicep intake engine. Starts after their Phase 0/1 PRs merge. Owner item: a constrained L2 grant on the platform ACR (AcrPull assignments). Our open points were sent to them. |
| **Filed today** | ISS-044 #3183 (PR #3187), ISS-045 #3189 (stamps lack 17 grid configs / 2 layouts / 1 playbook hard-coded by dev id; after #3188). |
| **Peer: ebilling-r1** | Owner approved: `Set-BffDecisionRecordReaderRole.ps1 -Verify` tolerates the 9 S29 platform-added privileges (their branch `e714592cb2`, not on master). When it merges (they will message): reword `SecureRecordSetupProcedure.cs:1541` and `SecureRecordSetupRejectionCodes.cs:314`. Until then never run that script's `-Apply` on ACME. |
| **Temp worktrees** | `C:\code_files\wtcp2` (detached, used for runs), `C:\code_files\wt044` (PR #3187 branch), `C:\code_files\wt041` (merged; remove) — remove only with `scripts/worktree/Remove-WorktreeSafe.ps1` (`-Force` when only `.husky/_` differs). `wtd4c`, `wt3107` locked: owner deletes by hand. |

## 🔔 Owner questions open

0. Full demo environment plan `notes/demo-environment-plan.md` (2026-11-26, W1–W9, ~12–17 d): six decisions in its §5. Decision 1 (user token for the loader) is security-sensitive.
1. ISS-040 #3089: retire the dead "Communication" event-type entry or add the row on dev.
2. #3173: remove the retired Copilot agent's Bicep param + registry column, or keep dormant.
3. ISS-038 #3018 decisions (`notes/iss038-upgrade-mode-design.md`: approve, Cosmos failover, CI hot path).
4. ACME BFF upgrade blocked on A9 home sign-in; guest Console sign-in; stamp plan sizing (S1 saturates after every swap).
5. From before: `version-compat-matrix.json` stale v1 cell; #2443/#2532 go-live gates; L56 AI content for ACME; demo accounts; dev BFF secrets rotation; ISS-026; D3, D5, D8.

## Ordering rules (live)

- No stamp BFF with ebilling 092+ before SpaarkeMaster 1.3.0.0 is installed there AND 091 eBilling storage + `Finance:FileStore` settings are live (#2232).
- No BFF with ebilling 095a on any stamp before `sprk_invoicesubmission.sprk_sourceemaildocument` is field-secured.
- No stamp gets a master BFF containing ebilling 116 / #2724 before SpaarkeMaster 1.3.1.0 is INSTALLED there (ACME now has 1.3.3.0).
- ACME BFF upgrade gated on A9 home sign-in; any BFF from master needs `Set-ExternalAccessContactSecuritySchema.ps1 -Apply` + `-Verify` first.
- Never remove System Administrator from the BFF identities.
- Before ANY stamp's next SpaarkeMaster MANAGED UPGRADE: run `scripts/ebilling/Remove-EbillingRateCardCoreLookups.ps1` in report mode (done for ACME before 1.3.3.0: READY).
- Upgrade runs send `upgradeOfProvisionedOn` = the row's current `sprk_provisionedon` (now 2026-11-25T23:51:44Z for ACME) and pin `buildId` when the BFF must not change.

## 📋 Remaining (detail in the archive file)

- H7c (#2429) `wip/h7c-customer-owner-teams` (`8ca1c13911`, unreviewed): merge master, review, resume (owner go).
- field-mapping 054 (Direct unit): reserved-list + reader-profile membership when it ships.
- #2232 eBilling storage per stamp; #2584; #2594; PR #2536 review (skill restructure); T186 close-out; ACME `AiSearch__AllowedIndexes__N` (owner OK); builds D36, D37, T240c, T240d, A9 H3, 203d, wrap-up 090.
- spaarkeai-mcp-bff-api-r1 stamp work when Copilot goes to customers: H3 connector registration + pre-auth, `Mcp:Enabled` per stamp.

## 🐞 Open defects

Checked 2026-11-24, plus today: #1543 · #1484 · #1485 · #1487 · #1499 · #1500 · #1523 · #1561 · #1562 · #1564 · #1570 · #1590 · #1803 · #1811 · #1504 · #1432 · #1376 · #1377 · #2584 · #2594 · #2725 · #3018 (ISS-038) · #3089 (ISS-040) · #3105 (ISS-042) · #3183 (ISS-044) · #3189 (ISS-045) · #3173. ISS-032 #2942, ISS-041 #3090, ISS-043 #3109 closed.
