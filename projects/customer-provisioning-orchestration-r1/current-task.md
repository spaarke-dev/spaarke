# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2/§3/§6. Decisions → `notes/model1-dedicated-remediation-plan.md`. Narrative → commit messages. Full detail of every open item below as of 2026-11-06 (verbatim): `notes/handoff-history/current-task-archive-2026-11-06.md`.

> **Last Updated**: 2026-11-11 (Desktop session `provisioning-r1`; checkpoint at 85% context, mid task 268 Step 9.5 fix round).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **268 IN PROGRESS** (FULL, opus/high) — `tasks/268-h7b-invoice-money-column-lock.poml`: H7b S26 verifies the seven EB-D-200 `sprk_invoice` columns' lock (secured, BFF-Managed Readers R4, Writers 4/4/4, no other writer); ProcedureVersion 9. Owner go 2026-11-11 ("Do the H7b check first"). Master merged first (`b94d351d2a`). Implementation committed `1fa31177d0` (NOT pushed). Step 8.0: no delegation. |
| **Step** | Step 9.5 fix round (after adversarial ×2: opus security + sonnet tests). |
| **Uncommitted (fix round, in progress)** | `tests/Spaarke.ArchTests/FinanceBaseValuesSingleDefinitionGuardTests.cs` (ebilling D-130 `AmountColumnBaseline` += 3 entries for our S26 file + 2 H7b test files, with reasons — ArchTests 2491/2491 after it); `SecureRecordSetupProcedure.cs` (F1: new `SystemAdministratorFieldSecurityProfileId` = `572329c1-a042-4e22-be47-367c6374ea45`, replaces the by-NAME `FindFieldSecurityProfilesAsync("System Administrator")` lookup for S14/S18/S19/S22/S26; F2: S26 other-writer message lists rights and names Readers only when present; three other refusals (S19 standing grant, S22 marker, S22 removed-on) reworded "(the Readers profile is not exempt — …)"); `FakeSecureRecordSetupDataverse.cs` (`AddProfile(name, fixedId?)`; SA profile seeded with the fixed id). |
| **Next Action** | Finish the fix round: (1) theory rows: a look-alike "System Administrator"-named profile with another id writing `sprk_totalamount` → `InvoiceMoneyLockOtherWriter`, and writing `sprk_issecure` (FieldPermissions on sprk_matter) → `FieldLockOtherWriter`; (2) F2 remedy text — `InvoiceMoneyLockRemedy` + guide S26 bullet (~line 2058) + RejectionCodes doc: the release must be a SpaarkeMaster version ABOVE 1.3.0.0 (the published 1.3.0.0 predates 166; H6 skips an equal version); until it is published run the ebilling script on ANY stamp (fresh or existing) with owner OK; (3) guide H7b handler row (~line 602): procedure 9, S26 clause, `invoice_money_lock_other_writer` in the quarantine list; guide change-log row for task 268 (after the 2026-11-10 task 267 row ~line 3082); guide S26 bullet "the Readers profile included" → "not exempt"; (4) packaged test: pin script `$Table` = `InvoiceMoneyTable` (K3) and `Level()` → -1 when the element is missing (K4); (5) `provisioning.md:250` stale `secure-setup-procedure=4` → "(since procedure 4)"; (6) run control-plane tests + ArchTests; scoped re-check of the fix diff (sonnet); commit `--only`; POML notes (known limits K1 order of refusals, K2 regex `)`, K1-sec field shares/POA, K2-sec read-only extra grant not refused, K3-sec mapping rules) + rework line `rework: fix-round-1 task 268`; TASK-INDEX ✅; drift check; push. (7) REPORT TO OWNER (important): from procedure 9 EVERY new provisioning run stops at H7b S26 until a SpaarkeMaster > 1.3.0.0 carrying ebilling 166 is published (or the script is run per stamp with owner OK); tell ebilling-r1 (they owe the SpaarkeMaster version; 1.3.0.1 already planned for 095a) + ask owner whether to merge S26 before that release. Also message ebilling about the 3 D-130 baseline entries we added to their guard. |
| **Done 2026-11-11** | PR #2676 (task 267 + H6 audit + registry format) MERGED → master `df93d2029c`. ebilling PR #2689 on master `2fc434c912` (166, 089d option R, KnownOffenders emptied — verified). |
| **Quality alert** | #1821 mutation-nightly, #1906 client-tests, #2060 tier2 full-unit-tests — mention to owner; not started. |

## 🔔 Owner questions open

1. **NEW (task 268)**: merge S26 now (blocks every new run at H7b until SpaarkeMaster > 1.3.0.0 with ebilling 166 is published, or the ebilling script is run per stamp) vs hold the merge until that release exists. Recommendation: merge — a run that passes H7b today would deploy a master BFF where users can set invoice totals.
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
