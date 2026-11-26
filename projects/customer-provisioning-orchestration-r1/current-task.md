# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Older detail: `notes/handoff-history/current-task-archive-2026-11-06.md` and git history of this file.

> **Last Updated**: 2026-11-26 ~00:20Z (Desktop session; ACME upgraded to SpaarkeMaster 1.3.3.0; post-run checks done).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | T186-style live run, no POML. **ACME UPGRADE run `85561c4a-0c28-41a8-ad35-66f0aede9ce9` COMPLETED 2026-11-25 23:52:02Z** (SpaarkeMaster managed 1.3.2.1 → **1.3.3.0**, BFF pinned `2026.10.16-2`; 22 handlers, 0 retries, 23 gates Verified; 1 h 20 min). Handoff `runs/85561c4a-0c28-41a8-ad35-66f0aede9ce9.md` (written, NOT yet committed). Run from temp worktree `C:\code_files\wtcp2` at master `af4cfb4527` (this worktree is ~1030 commits behind master; use master's skill/schema). Intake `runs/acme-intake.json` (gitignored, PII) now has `buildId` + `mcpDisconnectPolicy: proceedWithFallback`. |
| **Post-run done** | Run docs committed `8141d07853` (handoff, lessons L01–L04, INDEX, run-log §7). Registry Ready (Web API; MCP disconnected), `sprk_solutionversion` 1.3.3.0, `sprk_bffversion` 2026.10.16-2. `Test-RolePrivilegeExpectations.ps1` 1.3.3.0: all 8 roles OK. H12b seeded 37 reference rows + 87 legal-standard rows + Communication record type. Step 6e read-only items 1, 2, 3, 3b, 4, 5, 5b, 10 PASS; 9 = `SecureBuRoleDepthAssertionTests` in `tests/unit/Sprk.Bff.Api.Tests` exit 0 (111 tests; live case not confirmed un-skipped). Prod BFF `/healthz` 200; **staging slot 503/timeout** while the S1 plan ran 80–99 % CPU after the 23:49 swap (same as U-L08) → evidence for plan sizing (owner item). |
| **Next Action** | 1) Demo dataset LOADED on ACME 2026-11-26 (123 rows, idempotent re-check OK); staging slot healthy again. 2) Merge #3187 when every check is green. 3) Step 6e probes DONE 2026-11-26: 2b (40/40), 6, 7, 8, 13 PASS — secure-record ready on 1.3.3.0. 4) Remove temp worktree `wt041` (merged) with Remove-WorktreeSafe. |
| **PRs** | **#3188 ISS-041 MERGED `c3a42ad85a`** (guard widened; issue #3090 closed). **#3187 ISS-044 open** (owner "Yes merge"): one advisory job `api-office` failed on a Git LFS checkout error (no test ran) — rerun failed jobs once run 45721579035 finishes, merge when EVERY check is green (`--match-head-commit 723c9a6cbf`). |
| **Filed today** | ISS-044 #3183 (PR #3187), ISS-045 #3189 (stamps lack 17 grid configs / 2 layouts / 1 playbook hard-coded by dev id; after #3188). |
| **Peer: ebilling-r1** | Owner approved: `Set-BffDecisionRecordReaderRole.ps1 -Verify` tolerates the 9 S29 platform-added privileges (their branch `e714592cb2`, not on master). When it merges (they will message): reword `SecureRecordSetupProcedure.cs:1541` and `SecureRecordSetupRejectionCodes.cs:314`. Until then never run that script's `-Apply` on ACME. |
| **Temp worktrees** | `C:\code_files\wtcp2` (detached, used for runs), `C:\code_files\wt044` (PR #3187 branch), `C:\code_files\wt041` (merged; remove) — remove only with `scripts/worktree/Remove-WorktreeSafe.ps1` (`-Force` when only `.husky/_` differs). `wtd4c`, `wt3107` locked: owner deletes by hand. |

## 🔔 Owner questions open

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
