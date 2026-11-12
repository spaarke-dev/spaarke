# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Full detail of every open item below as of 2026-11-06 (verbatim): `notes/handoff-history/current-task-archive-2026-11-06.md`.

> **Last Updated**: 2026-11-10 (Desktop session `provisioning-r1`; took over from VS Code b8 on 2026-11-08).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | None in progress. **267 ✅ done 2026-11-10** (#2618; commits `416e1697ab` … `1c922dff6b`, pushed, NOT on master): writer alone in unit "Spaarke Ontology Writer" (H10 creates it, moves a 266-era writer out of the customer unit, oid checked first, read back); H7b v8 S24 checks that unit by id, S11/S16 skip its default team, new S25 "Spaarke Basic User" on the customer default team; nine operator scripts use `scripts/common/OntologyWriterUnit.ps1`; ArchTest ratchet over scripts/**; uac-r2 census fix (approved). 3402/1 control plane; ArchTests 2116. |
| **Next Action** | **NOW (2026-11-11): PR #2676 Auto-fix ON (owner). CI: only `Tier 1 (Blocking) / Format (changed C# files)` failed (→ Router). Fix: `dotnet format <csproj> --include <changed .cs files>` for the files in `git diff --name-only origin/master...HEAD -- '*.cs'`, check with `git diff --ignore-cr-at-eol`, commit `--only`, push (also pushes local checkpoint 81074ec792).** ebilling's AI-redraft membership request WITHDRAWN (team is memberless by contract). **PR #2676 open** (2026-11-10; branch merged with master `d42eded430`; 3402/1 control plane, ArchTests 2152, BFF build clean; no deploy workflow triggered). Merge only when EVERY check is terminal and green (owner). (a) DONE: dev census incl. the new `businessunits` read PASSED 2026-11-10 (uac-r2 gate); (b) when ebilling-r1's fix (`a945c34a3d` on work/ebilling-r1: all 7 default-team sites, helper copied byte-for-byte) meets our ratchet, DROP their 4 `KnownOffenders` entries in that merge, add none. (c) `scripts/Set-CustomerOwnerTeams.ps1` (ours; ebilling's merge fixes `2011574c2c`/`50c6b9ee76` reviewed OK 2026-11-10): exclude the "Spaarke Ontology Writer" unit from `$customerTops` by id (`Get-DvOntologyWriterUnitId`), as master excludes the Direct unit — today it counts as a customer top (INFO noise; an owner team there counts as allowed; a role copy there reads `othertop`). Do it after ebilling's master merge lands. ACME migration (move writer + S25) needs a NEW run with the owner's OK; first read ACME's DoNotRemoveRolesOnChangeBusinessUnit (K3). **Dev control plane redeployed 2026-11-10** from `7489f5b6b0`. |
| **Done 2026-11-10** | H6 org-settings contract adds `isuseraccessauditenabled=true` (owner: include it) — `7489f5b6b0`, on branch, not on master; Validate-DeployedEnvironment check 6 + guide updated. |
| **Done 2026-11-08/09** | #2605 reply posted; PR #2638 (S23 + 49ba9a70d3 + 266) merged to master `3fa6a061`, #2605 closed. ACME still needs a control-plane deploy (owner OK) to get S23/S24 and the BU id fix. |
| **Quality alert** | #2060 tier2-master/full-unit-tests (nightly) open — mention to owner; not started. |
| **Task 266 outcome** | Code + tests + guide; no live change. Commits b514a9edca … 9f24305bf2. 3315 control-plane tests, ArchTests 1950/1950. Code review: W1 (justification) fixed in POML; adr-check clean. Adversarial ×2 + scoped re-check done; notes in the POML. |
| **Follow-up** | **#2618** fixed in code by task 267 (branch). Do not add a role to ACME's default team or move ACME's writer by hand: ACME gets it through a new run (owner OK). |
| **ACME** | Writer applied by ontology 2026-11-06 under the owner's go; read-only verified 2026-11-07 (run log). Run is `Completed` under pre-266 outputs: a re-run of H4b/H10/H7b refuses "start a new run". Before any `customer.bicep` redeploy: confirm the hand-applied writer UAMI's tags match the template. |

## 🔔 Owner questions open

1. (Done 2026-11-10, owner OK) Registry format is now `SpaarkeMaster {Managed|Unmanaged} {version}` (owner: no "(managed)" after the number) — code, skill Step 6a, docs, matrix v4; ACME row set to `SpaarkeMaster Managed 1.3.0.0` (spaarkedev1, read back). Open: `version-compat-matrix.json` has one stale v1 cell, so any upgrade-mode H0 is RED until cells are added.
2. **#2618 BUILT (task 267, 2026-11-10)**, on branch only. Open: merge to master (owner); ACME migration through a new run (owner OK). ontology-r1 confirmed the writer reads no field-secured column as itself.
3. (Decided 2026-11-10: include `isuseraccessauditenabled` — done in code.) ACME's own setting changes only on its next H6 run or by hand (owner OK).
4. #2443 / #2532 (073 go-live): read-only 2026-11-10 — no production workforce SPA client exists in the Spaarke tenant (only dev `5859e812`, redirects to the dev SWA); external.spaarke.com serves the SWA placeholder (nothing deployed). Open gates: prod workforce client, CIAM BFF app (T240d), routing (T240c, owner), GitHub env vars, A9 on ACME, DNS.
5. Older: L56 AI content for ACME (H12a seeds 4 of 12 sets); demo accounts unclassified; dev BFF secrets printed locally (rotation); ISS-026 publisher verification.

## Branch state

- **On master** since PR #2638 → `3fa6a061` (2026-11-09): H7b S23 (procedure v6), `49ba9a70d3`, task 266 (procedure v7, writer identity). ACME gets them only after a control-plane deploy (owner OK). The merge republished the compiled `customer.bicep` ARM artifact (affects new H2a runs only).
- **On master** since PR #2661 → `d64a45071b` (2026-11-10): business-unit id comparison (H10 + H7b S1). Branch head `89b0ffdd1a` (master merged in); BFF build 0 errors, ArchTests 2108/2108, control-plane 3342 pass / 1 skip.

## Ordering rules (live)

- No stamp BFF with ebilling 092+ before SpaarkeMaster 1.3.0.0 is installed there AND 091 eBilling storage + `Finance:FileStore` settings are live (#2232).
- No BFF with ebilling 095a on any stamp (ACME included) before `sprk_invoicesubmission.sprk_sourceemaildocument` is field-secured there: the lock ships in SpaarkeMaster **1.3.0.1** (not 1.3.0.0; reaches master with ebilling's single master PR, days away) or via ebilling's `-WhatIf`/apply/`-Verify` script as an ordered provisioning prerequisite (ebilling, 2026-11-07).
- ebilling 166: after it reaches master, ebilling sends the master SHA + the exact profile grants on `sprk_totalamount` / `sprk_approvedtotal` (message only) → then the H7b check.
- ACME BFF upgrade gated on A9 home sign-in (project CLAUDE.md §3); any BFF from master needs `Set-ExternalAccessContactSecuritySchema.ps1 -Apply` + `-Verify` first.
- Never remove System Administrator from the BFF identities (relayed standing instruction).
- Next stamp upgrade: run the role check (ontology) and confirm the businessunit→sprk_matter entity map is gone (field-mapping).

## 📋 Remaining (detail in the archive file)

- **H7c (#2429)**: partial work on `wip/h7c-customer-owner-teams` (`8ca1c13911`, unreviewed). Resume after ebilling's 089d option R reaches master (they send the SHA); merge master first. Covers both owner teams, Billing Rule Reader role, `EnableOwnershipAcrossBusinessUnits`, H12b owner bind + `sprk_aisearchindex` seeding (owner chose B).
- **Decision Record Reader role (ebilling DEF-059, uac-r2-approved shape; received 2026-11-09)**: "Spaarke Decision Record Reader" `{a8308e87-b0e0-4cd1-9c66-3672e9bdad16}` (prvReadsprk_DecisionRecord Global only) → BOTH BFF app users (app reg + UAMI), never the writer, people or teams. Ours: (1) H10 list option (e.g. `AdditionalSecurityRoleNames`) associating each role's customer-BU copy with both app users via `DataverseWebApiAppUserCreator`, fail if missing, T2-style verify; (2) strip platform-default privileges (as `Set-CustomerOwnerTeams.ps1` :474/:597; shape: `scripts/ebilling/Set-BffDecisionRecordReaderRole.ps1`); (3) role-expectations file for the next SpaarkeMaster release (present Read Global; absent C/W/D on decisionrecord, C/W on signal). Blocked on ebilling branch `task/ebilling-def059` reaching master (they send the SHA); until then guide §7.7 = run their script after H6. Needs a task (owner go).
- **Business-unit id comparison (uac-r2 finding, owner go 2026-11-09)**: H10 + H7b S1 compare unit IDs, not names — `9332ade5fd` + `07490bde65`, ON MASTER via PR #2661 (`d64a45071b`); uac-r2 tenant-isolation review CLOSED, no F (2026-11-09). Reaches ACME only via a control-plane deploy (owner OK). Adversarial review done (F1 fresh-stamp gap + F2 ambiguous diagnostics fixed). Known limits: intake fold approximates Dataverse CI_AI (ß/æ/ø); runs completed under the old H10 are not re-checked by H10 (H7b S1 catches them). Extend the reserved list with the Direct unit when field-mapping 054 ships.
- **field-mapping 054 heads-up (2026-11-09, `field-mapping-r1`)**: a Direct business unit (direct child of the root, beside Secure Record) with a named memberless non-default Owner team + settings `DirectMessage:BusinessUnitName` / `DirectMessage:OwnerTeamName`; 054b field-security profiles: reader on every BU default team, writer = BFF app users only (a new BU's default team must join the reader profile). Exact setup item comes when 054 ships. The customer-unit id comparison (uac-r2 accent finding) must also cover the Direct unit.
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
