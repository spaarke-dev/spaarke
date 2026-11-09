# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Full detail of every open item below as of 2026-11-06 (verbatim): `notes/handoff-history/current-task-archive-2026-11-06.md`.

> **Last Updated**: 2026-11-09 (Desktop session `provisioning-r1`; took over from VS Code b8 on 2026-11-08). PR #2638 merged to master.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none in progress. Last completed: **266** — per-stamp ontology writer identity (#2605), ✅ 2026-11-07 |
| **Next Action** | Owner questions below (#2618 first). Done 2026-11-08/09: #2605 reply posted (issuecomment-6726317581); PR #2638 (S23 + 49ba9a70d3 + 266) merged to master `3fa6a061` (35 pass / 9 skipped, `--merge`), #2605 closed. ACME still needs a control-plane deploy (owner OK) to get S23/S24. |
| **Task 266 outcome** | Code + tests + guide; no live change. Commits b514a9edca … 9f24305bf2. 3315 control-plane tests, ArchTests 1950/1950. Code review: W1 (justification) fixed in POML; adr-check clean. Adversarial ×2 + scoped re-check done; notes in the POML. |
| **Follow-up** | **#2618** (owner decision): the guide's external-access row asks for a contact role on the customer unit's default team; the ontology writer would inherit it, so S24 refuses (fail-closed). Recommended: identity contacts owned by a dedicated memberless team (UAC/external-access change). Do not add a role to ACME's default team before it is decided. |
| **ACME** | Writer applied by ontology 2026-11-06 under the owner's go; read-only verified 2026-11-07 (run log). Run is `Completed` under pre-266 outputs: a re-run of H4b/H10/H7b refuses "start a new run". Before any `customer.bicep` redeploy: confirm the hand-applied writer UAMI's tags match the template. |

## 🔔 Owner questions open

1. **ACME registry row**: SpaarkeMaster on ACME is now **1.3.0.0** (ontology ran it 2026-11-07; run log). The registry row `sprk_solutionversion` still holds the run's value — update it? (spaarkeextid write, owner OK).
2. **#2618** — customer default team contact-owner role vs ontology writer least privilege (options in the issue; recommend option 1).
3. Add `isuseraccessauditenabled` to the H6 org-settings contract (ACME off, dev on; recommend yes) + org-audit section in the role check.
4. #2443 read-only check of the production workforce + CIAM clients.
5. Older: L56 AI content for ACME (H12a seeds 4 of 12 sets); demo accounts unclassified; dev BFF secrets printed locally (rotation); ISS-026 publisher verification.

## Branch state

- **On master** since PR #2638 → `3fa6a061` (2026-11-09): H7b S23 (procedure v6), `49ba9a70d3`, task 266 (procedure v7, writer identity). ACME gets them only after a control-plane deploy (owner OK). The merge republished the compiled `customer.bicep` ARM artifact (affects new H2a runs only).
- Branch was updated from master at `3b22175ada` (clean); BFF build 0/0, ArchTests 2100/2100, control-plane 3316 pass / 1 skip.

## Ordering rules (live)

- No stamp BFF with ebilling 092+ before SpaarkeMaster 1.3.0.0 is installed there AND 091 eBilling storage + `Finance:FileStore` settings are live (#2232).
- No BFF with ebilling 095a on any stamp (ACME included) before `sprk_invoicesubmission.sprk_sourceemaildocument` is field-secured there: the lock ships in SpaarkeMaster **1.3.0.1** (not 1.3.0.0; reaches master with ebilling's single master PR, days away) or via ebilling's `-WhatIf`/apply/`-Verify` script as an ordered provisioning prerequisite (ebilling, 2026-11-07).
- ebilling 166: after it reaches master, ebilling sends the master SHA + the exact profile grants on `sprk_totalamount` / `sprk_approvedtotal` (message only) → then the H7b check.
- ACME BFF upgrade gated on A9 home sign-in (project CLAUDE.md §3); any BFF from master needs `Set-ExternalAccessContactSecuritySchema.ps1 -Apply` + `-Verify` first.
- Never remove System Administrator from the BFF identities (relayed standing instruction).
- Next stamp upgrade: run the role check (ontology) and confirm the businessunit→sprk_matter entity map is gone (field-mapping).

## 📋 Remaining (detail in the archive file)

- **H7c (#2429)**: partial work on `wip/h7c-customer-owner-teams` (`8ca1c13911`, unreviewed). Resume after ebilling's 089d option R reaches master (they send the SHA); merge master first. Covers both owner teams, Billing Rule Reader role, `EnableOwnershipAcrossBusinessUnits`, H12b owner bind + `sprk_aisearchindex` seeding (owner chose B).
- **#2232**: dedicated eBilling storage account per stamp + `Finance__FileStore__*` settings + H13 checks; blocked on ebilling's module reaching master. EB-D-164: scanning required on prod stamps.
- **ebilling 166**: when on master, H7b check that `sprk_invoice.sprk_totalamount` / `sprk_approvedtotal` are secured and Writers has Read 4.
- **#2584**: `sprk_workassignmenttype` owner unclaimed. **#2594**: port the Pester 3.4 suites.
- **PR #2536** (skill restructure, tracking #2537): our review + dev dry run + D9–D15 decisions before merge; act on owner direction.
- **T186 close-out**: 6f Copilot package to the owner; carry open lessons into the guide/prereqs (run-log rows not `applied`); L15, L30; remove old broad L2 Graph roles; S18 columns.
- ACME live change still owed (owner OK): `AiSearch__AllowedIndexes__N` (fixed in code `8bef420b86`).
- Builds: D36, D37, T240c, T240d, A9 H3 changes, 203d, wrap-up 090. Repo cleanup of old-demo references.
- Report to owner: S1 plan cost; L49 ComposeIdentityKey DEGRADED on ACME BFF; task 264 vs 047 ordering; registry table in SpaarkeMaster.
- Task 266 known limit to confirm on the next live H7b run: real Dataverse returns the unit's default team in `systemusers(id)/teammembership_association` (K2).

## 🐞 Open defects

#2618 · #1543 · #1484 · #1485 · #1487 · #1499 · #1500 · #1523 · #1561–#1564 · #1570 · #1590 · #1803 · #1811 · #1504 · #1432 · #1376 · #1377 · #2584 · #2594.
