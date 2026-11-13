# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Full detail of every open item below as of 2026-11-06 (verbatim): `notes/handoff-history/current-task-archive-2026-11-06.md`.

> **Last Updated**: 2026-11-13 (Desktop session `provisioning-r1`; checkpoint at 85% context, mid task 269 Step 9.5 fix round).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **269 IN PROGRESS** (FULL, opus/high) — `tasks/269-owner-teams-exclude-ontology-writer-unit.poml`: `scripts/Set-CustomerOwnerTeams.ps1` never treats "Spaarke Ontology Writer" (task 267) as a customer unit. Owner go 2026-11-13 ("Writer-unit fix"). Master merged first (`04e5cf5997`). Implementation committed `77b6cbe417` (NOT pushed). Last completed: 268 → PR #2766 `4b45bdf3fe`. |
| **Next Action** | Step 9.5 fix round (2 adversarial reviews done: opus logic — no F1/F2; sonnet tests — F3 descendants untested). UNCOMMITTED fix edits: script help text (.DESCRIPTION drift + REFUSED lists), PASS/plan text name the writer unit, K1 refuse `-SecureBusinessUnitName` = writer unit, K3 `$forbiddenOrigin` (ids) replaces label compare; guide ~1968 reworded (named customer subtree only); Pester +4 rows (team below writer unit, team below Secure Record label, K1, holder of writer-child copy) + tree-shape tail assert. Pester 80/81: fix the "no writer unit" row — replace `Should -Not -Match 'ontology writer'` with `Should -Not -Match 'Refused'` (plan text now always names the writer unit). Then: re-run Pester; scoped sonnet re-check of the fix diff; commit `--only`; POML notes (K2 renamed unit invisible, K Verify not run for team configs with writer present, K name-case row absent) + `rework: fix-round-1 task 269` + status completed; TASK-INDEX ✅; drift check; push; open PR (owner approves merge). |
| **Done 2026-11-11/13** | PR #2676 (task 267) → `df93d2029c`. ebilling #2689 → `2fc434c912`. Task 268 → PR #2766 `4b45bdf3fe`; #2725 / ISS-028 filed (19 operator scripts excuse System Administrator by name; ebilling fixing its 4 sites). SpaarkeMaster 1.3.1.0 published 2026-11-13. |
| **Quality alert** | #1821 mutation-nightly, #1906 client-tests, #2060 tier2 full-unit-tests — mentioned to owner; not started. |
| **Working tree** | Uncommitted: scripts/Set-CustomerOwnerTeams.ps1, tests/scripts/Set-CustomerOwnerTeams.Tests.ps1 (CRLF), docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md (LF), this file. |

## 🔔 Owner questions open

1. ACME migration (writer unit + S25 + now S26) through a NEW run, owner OK; first read ACME's DoNotRemoveRolesOnChangeBusinessUnit (task 267 K3).
2. Open from before: `version-compat-matrix.json` stale v1 cell; #2443/#2532 go-live gates (prod workforce client, CIAM BFF app T240d, routing T240c, GitHub env vars, A9 on ACME, DNS); L56 AI content for ACME; demo accounts; dev BFF secrets rotation; ISS-026.

## Follow-ups unblocked by ebilling #2689 (not started)

- (task 269, in progress) Set-CustomerOwnerTeams writer-unit exclusion.
- **H7c (#2429)**: `wip/h7c-customer-owner-teams` (`8ca1c13911`, unreviewed): merge master into it, review, resume (owner go). Covers both owner teams, Billing Rule Reader role, `EnableOwnershipAcrossBusinessUnits`, H12b owner bind + `sprk_aisearchindex` seeding (owner chose B).
- Possible: S26-style check for ebilling 095a (`sprk_invoicesubmission.sprk_sourceemaildocument` lock) — would make that ordering rule mechanical.

## Ordering rules (live)

- No stamp BFF with ebilling 092+ before SpaarkeMaster 1.3.0.0 is installed there AND 091 eBilling storage + `Finance:FileStore` settings are live (#2232).
- No BFF with ebilling 095a on any stamp before `sprk_invoicesubmission.sprk_sourceemaildocument` is field-secured (ships in SpaarkeMaster 1.3.1.0 or via ebilling's script).
- No stamp gets a master BFF containing ebilling 116 / #2724 (on master since 2026-11-12 05:25Z) before SpaarkeMaster 1.3.1.0 is installed there: 1.3.1.0 adds `sprk_procedureversion.sprk_notconvertedlist`, which #2724 reads; without it the rule-set and procedure-version routes fail (ebilling-r1, 2026-11-12).
- SpaarkeMaster 1.3.1.0 (NOT 1.3.0.1; ebilling-r1 2026-11-12): carries 166 (what S26 checks), 095a/095c-1, DEF-061, DEF-059 Decision Record Reader role, uac-r2 193 `sprk_pairedsystemuser`, `sprk_notconvertedlist`. PUBLISHED 2026-11-13 03:22Z (owner go in ebilling-r1's session): run 43358397122 from master `d8b53ec0c3`; `dataverse-solutions-latest.json` = 1.3.1.0 (managed sha256 `4f7cda4c…32e6d4`), versioned manifest `dataverse-solutions-2026.11.13-318-1.json`; expectations `spaarkemaster-1.3.1.0.json`. Also carries field-mapping #2733 (secured message-access columns + BFF profile grants) and the #2725 profile-id fix. The #2724 gate is per stamp: 1.3.1.0 must be INSTALLED there (H6 on a new run), not just published. An environment that already has `sprk_invoicesubmission` rows runs `Deploy-EbillingSubmissionTrackingScopeSchema.ps1` steps 1–8 right after import; a new stamp needs nothing. ebilling will send the version + run when published.
- ACME BFF upgrade gated on A9 home sign-in; any BFF from master needs `Set-ExternalAccessContactSecuritySchema.ps1 -Apply` + `-Verify` first.
- Never remove System Administrator from the BFF identities.

## 📋 Remaining (detail in the archive file)

- Decision Record Reader role (ebilling DEF-059): H10 `AdditionalSecurityRoleNames` + strip + role-expectations; blocked on `task/ebilling-def059` reaching master; needs a task (owner go).
- field-mapping 054 (Direct unit): reserved-list + reader-profile membership when it ships.
- #2232 eBilling storage per stamp; #2584; #2594; PR #2536 review; T186 close-out; ACME `AiSearch__AllowedIndexes__N` live change (owner OK); builds D36, D37, T240c, T240d, A9 H3, 203d, wrap-up 090.
- Task 266 K2 to confirm on next live H7b run.

## 🐞 Open defects

#1543 · #1484 · #1485 · #1487 · #1499 · #1500 · #1523 · #1561–#1564 · #1570 · #1590 · #1803 · #1811 · #1504 · #1432 · #1376 · #1377 · #2584 · #2594.
