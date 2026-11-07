# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Full detail of every open item below as of 2026-11-06 (verbatim): `notes/handoff-history/current-task-archive-2026-11-06.md`.

> **Last Updated**: 2026-11-07 (session b8). Task 266 complete; HEAD pushed.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none in progress. Last completed: **266** — per-stamp ontology writer identity (#2605), ✅ 2026-11-07 |
| **Next Action** | Reply to ontology (#2605): the Bicep lists both UAMIs; S24 adds Firm Reason membership after the 1.3.0.0 upgrade; the ACME writer's aad object id was verified; #2618 open. Then the next PR to master (S23 + 266) — every check terminal and green, `--merge`, no auto-merge. |
| **Task 266 outcome** | Code + tests + guide; no live change. Commits b514a9edca … 9f24305bf2. 3315 control-plane tests, ArchTests 1950/1950. Code review: W1 (justification) fixed in POML; adr-check clean. Adversarial ×2 + scoped re-check done; notes in the POML. |
| **Follow-up** | **#2618** (owner decision): the guide's external-access row asks for a contact role on the customer unit's default team; the ontology writer would inherit it, so S24 refuses (fail-closed). Recommended: identity contacts owned by a dedicated memberless team (UAC/external-access change). Do not add a role to ACME's default team before it is decided. |
| **ACME** | Writer applied by ontology 2026-11-06 under the owner's go; read-only verified 2026-11-07 (run log). Run is `Completed` under pre-266 outputs: a re-run of H4b/H10/H7b refuses "start a new run". Before any `customer.bicep` redeploy: confirm the hand-applied writer UAMI's tags match the template. |

## 🔔 Owner questions open

1. **SpaarkeMaster 1.3.0.0 → ACME: ONTOLOGY IS RUNNING IT** (owner GO given directly in the ontology session, 2026-11-07). Do NOT start it here, even if the owner confirms in this session — tell them ontology is on it (avoids a double run). Ontology's plan: ebilling 095a check → publish workflow dry run → publish=true from master (≥ d53d67f604) → `Import-SpaarkeMasterPackage.ps1 -WhatIf` (StageAndUpgrade 1.2.3.1 → 1.3.0.0) → real → `Test-RolePrivilegeExpectations.ps1` (`spaarkemaster-1.3.0.0.json`) → writer Firm Reason membership → holder sweep → healthz. Read-only facts sent to them: published latest = 1.2.4.0; ACME 1.2.3.1 managed; the four removed components hold 0 rows on ACME. Await their result; record it in the ACME run log.
2. **#2618** — customer default team contact-owner role vs ontology writer least privilege (options in the issue; recommend option 1).
3. Add `isuseraccessauditenabled` to the H6 org-settings contract (ACME off, dev on; recommend yes) + org-audit section in the role check.
4. #2443 read-only check of the production workforce + CIAM clients.
5. Older: L56 AI content for ACME (H12a seeds 4 of 12 sets); demo accounts unclassified; dev BFF secrets printed locally (rotation); ISS-026 publisher verification.

## Branch state

- **On branch, not on master**: H7b S23 (`923990a791`, `ba484977b2`; procedure v6), `49ba9a70d3` (publish rollback pointer on same-commit re-run), task 266 (procedure v7, writer identity). Goes to master with the next PR. ACME gets them only after a control-plane deploy (owner OK).
- Last master merge from here: PR #2438 → `d511a329` (2026-10-29).

## Ordering rules (live)

- No stamp BFF with ebilling 092+ before SpaarkeMaster 1.3.0.0 is installed there AND 091 eBilling storage + `Finance:FileStore` settings are live (#2232).
- No BFF with ebilling 095a before `sprk_invoicesubmission.sprk_sourceemaildocument` is secured (release or `scripts/ebilling/Set-EbillingSourceEmailFieldSecurity.ps1`).
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
