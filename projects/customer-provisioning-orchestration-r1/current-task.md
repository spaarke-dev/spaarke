# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Older detail: `notes/handoff-history/current-task-archive-2026-11-06.md` and git history of this file.

> **Last Updated**: 2026-11-27 ~15:55Z (Desktop session; dev L2 deployed from master 6f4d7bc7e4; ACME run fe9ec82b Completed with the W1/W2/W4 seeds).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **ACTIVE: bring the ACME demo to the full solution (owner 2026-11-26: "whatever is required so that demo is running the full solution"; "Yes adopt the plan recommendations"; "Proceed with other required updates to bring current"; "approved for the PRs").** DONE today: PR #3217 merged `9d2bf853d7`; dev L2 deployed; ACME run `7a7cbda1` Completed 12:50Z after one owner-approved resume (H13 Cost Management 429 -> ISS-048 #3222); operator client `ec3c52c1-...` now pre-authorized on ACME BFF app b4882848; handoff runs/7a7cbda1-....md + lessons committed `7e32112a0e`; PR #3236 (S29 wording, ebilling-r1 hand-off) merged `18ff1ac4d9`; demo plan decisions 2-5 adopted (recorded in notes/demo-environment-plan.md). STATE: ACME SpaarkeMaster 1.3.3.0 = newest published; BFF 2026.10.16-2 = newest PUBLISHED (latest.json unchanged since 2026-10-16; publish-bff-artifact.yml is manual, publish:true). Gates for a master BFF on ACME: 095a CLEAR (sprk_sourceemaildocument IsSecured=True live), #2724 CLEAR (1.3.3.0), A9 home sign-in OPEN (asked uac-r2 2026-11-26 ~13:05Z, awaiting reply), contact security schema script -Verify not yet run. |
| **Next Action** | DONE 2026-11-27: demo PRs W7 #3243, W1 #3244, W2 #3251, W4 #3252 merged; dev ACT-004 $choices fixed; ISS-049 #3255 filed; dev L2 deployed from master `6f4d7bc7e4` (wtcp2, all checks OK); ACME upgrade run `fe9ec82b` Completed 15:47:57Z after one owner-approved resume (H13 429, ISS-048 again); seeds verified on ACME (21 grid configs, 13 playbooks, 44 bindings, 8 code sets, 161 codes); handoff runs/fe9ec82b-....md. NEXT (each needs the owner's OK): 1) write the 12+12 default-code-set mappings on ACME matter types / practice areas (H12b never writes existing rows); 2) delete + reseed the 2 ACME system layouts (Daily Briefing 4cd0675e-..., Smart To Do List 4dd0675e-...) so they get the fixed ids; 3) W3 documents + W6 personas (owner signs in through the browser with operator client ec3c52c1); 4) W5 eBilling; 5) W9. Open owner decisions: A9 home sign-in, DEF-005 UTBMS standard. Housekeeping: provision-environment SKILL.md ~L906 billing tables line; remove worktrees wtopc, wtdep, wt044 (Remove-WorktreeSafe.ps1). |
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
