# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Full detail of every open item below as of 2026-11-06 (verbatim): `notes/handoff-history/current-task-archive-2026-11-06.md`.

> **Last Updated**: 2026-11-12 (Desktop session `provisioning-r1`; task 268 completed).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | None in progress. Last completed: **268** (H7b S26, invoice money + firm column lock, verify only; ProcedureVersion 9) — `1fa31177d0` + fix round `6929b3b10f` + `b921337a9e`, branch pushed. No PR open for the branch. |
| **Next Action** | Owner decides question 1 (merge S26 now vs hold). Then, with owner go: Set-CustomerOwnerTeams writer-unit exclusion, or H7c (#2429). ebilling-r1 was told (2026-11-12) about S26, the SpaarkeMaster > 1.3.0.0 need, our 3 D-130 baseline entries and #2725 — await their published version. |
| **Done 2026-11-11/12** | PR #2676 (task 267) MERGED → `df93d2029c`. ebilling #2689 on master `2fc434c912`. Task 268 done; #2725 / ISS-028 filed (19 operator scripts excuse System Administrator by name). Master has 1 commit since our merge (#2721 uac-r2, no overlap). |
| **Quality alert** | #1821 mutation-nightly, #1906 client-tests, #2060 tier2 full-unit-tests — mentioned to owner; not started. |
| **Working tree** | `H7bSecureRecordSetupHandler.cs` shows `M` with an empty diff (line endings only) — leave it. |

## 🔔 Owner questions open

1. **Task 268 S26**: merge S26 now (blocks every new run at H7b until SpaarkeMaster > 1.3.0.0 with ebilling 166 is published, or the ebilling script is run per stamp) vs hold the merge until that release exists. Recommendation: merge — a run that passes H7b today would deploy a master BFF where users can set invoice totals.
2. ACME migration (writer unit + S25 + now S26) through a NEW run, owner OK; first read ACME's DoNotRemoveRolesOnChangeBusinessUnit (task 267 K3).
3. Open from before: `version-compat-matrix.json` stale v1 cell; #2443/#2532 go-live gates (prod workforce client, CIAM BFF app T240d, routing T240c, GitHub env vars, A9 on ACME, DNS); L56 AI content for ACME; demo accounts; dev BFF secrets rotation; ISS-026.

## Follow-ups unblocked by ebilling #2689 (not started)

- `scripts/Set-CustomerOwnerTeams.ps1`: exclude the "Spaarke Ontology Writer" unit from `$customerTops` by id (`Get-DvOntologyWriterUnitId`), as master excludes the Direct unit. Master already merged into branch (`b94d351d2a`).
- **H7c (#2429)**: `wip/h7c-customer-owner-teams` (`8ca1c13911`, unreviewed): merge master into it, review, resume (owner go). Covers both owner teams, Billing Rule Reader role, `EnableOwnershipAcrossBusinessUnits`, H12b owner bind + `sprk_aisearchindex` seeding (owner chose B).
- Possible: S26-style check for ebilling 095a (`sprk_invoicesubmission.sprk_sourceemaildocument` lock) — would make that ordering rule mechanical.

## Ordering rules (live)

- No stamp BFF with ebilling 092+ before SpaarkeMaster 1.3.0.0 is installed there AND 091 eBilling storage + `Finance:FileStore` settings are live (#2232).
- No BFF with ebilling 095a on any stamp before `sprk_invoicesubmission.sprk_sourceemaildocument` is field-secured (ships in SpaarkeMaster 1.3.0.1 or via ebilling's script).
- ACME BFF upgrade gated on A9 home sign-in; any BFF from master needs `Set-ExternalAccessContactSecuritySchema.ps1 -Apply` + `-Verify` first.
- Never remove System Administrator from the BFF identities.

## 📋 Remaining (detail in the archive file)

- Decision Record Reader role (ebilling DEF-059): H10 `AdditionalSecurityRoleNames` + strip + role-expectations; blocked on `task/ebilling-def059` reaching master; needs a task (owner go).
- field-mapping 054 (Direct unit): reserved-list + reader-profile membership when it ships.
- #2232 eBilling storage per stamp; #2584; #2594; PR #2536 review; T186 close-out; ACME `AiSearch__AllowedIndexes__N` live change (owner OK); builds D36, D37, T240c, T240d, A9 H3, 203d, wrap-up 090.
- Task 266 K2 to confirm on next live H7b run.

## 🐞 Open defects

#1543 · #1484 · #1485 · #1487 · #1499 · #1500 · #1523 · #1561–#1564 · #1570 · #1590 · #1803 · #1811 · #1504 · #1432 · #1376 · #1377 · #2584 · #2594.
