# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-04 ~17:00 local (checkpoint #92: core-pair branch has the lineage fix + fix round; 2nd Opus pass running; fm 042c reworked under D-204; ebilling 089e approved + dev roles applied).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green: `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys and settings changes need the owner's go IN THIS SESSION. Peers take the owner's go for THEIR live writes in THEIR sessions. |
| **Active work** | Core-pair branch `fix/uac-core-pair-update`, worktree **C:/wtcore**, NOT pushed. Commits on 0d4aa87ee0: f90529a016, 2b8667a691, ce2bed5f70, 2fd3c52e40, 3509be3712 (pair fixed after create on every BFF update path; 2 Opus passes + Sonnet re-check, no F), **9aa627d3d1** (secure-child lineage reads the Signal/DR core pair), **e4d7492040** (its fix round: D/B/P/N id forms, ordinary-move service-request match, held unknown/blank type + Warning, tests). 2nd Opus pass on 3509be3712..e4d7492040: **no F**; K1 (Decision Record witnesses read without the pair) being fixed; K2 (rename the changed ontology test) asked of ontology; K3 accepted (fail-safe). **23b1e5fb00** (K1 + ontology's D36 test split) and **5df0a80e1d** (output mapping, Update Record node/ActionSeam and FM push use `UpdateExistingRecordFieldsAsync`; ArchTest `UpdateRecordFieldsCallerGuardTests`; the chat tool already sent If-Match). **Opus adversarial pass on e4d7492040..5df0a80e1d RUNNING.** Ontology and FM notified. Ontology reviewed both lineage commits: PASS. |
| **Dev state** | BFF = **d763dc120e** since 2026-11-03 22:26:31Z (my gate PASSED: `$TEMP/uacmerge/uacg/gated763_30m.txt`, script `gated763.sh`). Rollback: `deploy/rollback/wwwroot-20261103T220358Z` or redeploy 5796774091. Dev SPA = b083d62183. |
| **Next action** | 1. When the 2nd pass reports: fix any F (Opus implementer in C:/wtcore), scoped re-check. 2. Run the FULL integration suites (`tests/integration/Sprk.Bff.Api.IntegrationTests` + `Spe.Integration.Tests`) once machine load allows (CPU was 90%+). 3. Push, PR with NEUTRAL wording ("core record pair fixed after create on BFF update paths; secure-child lineage reads the core record pair"); nothing about scenarios. Merge per Mode. 4. Next dev deploy (owner go here) with the read-back below. |

## Private items (tracked privately only, owner choice; never in issues/PRs/commits)
- Core pair fixed after create, plus the lineage follow-up: both on the branch above.
- fm 042c: my approval was WITHDRAWN (F), and the item is in rework. The owner chose **option 1 (ontology D-204)** in fm's session: the generic create refuses sprk_signal / sprk_decisionrecord / sprk_budgetrevision as whole tables, for the caller and the system principal alike. fm fix round 4 is running; ontology and I get the diff before any merge.
- **Update-path follow-up (mine, F, found by fm 2026-11-04; details by message only):** goes on the core-pair branch after the 2nd pass. Generic update paths (UpdateRecordActionCore, DataverseUpdateHandler, chat update) use the existing-only update (`If-Match: *`) and refuse an unknown id. Audit every `UpdateRecordFieldsAsync` caller for upsert reliance; tests on every path. fm 042c r4 (4b45ba3cc5) APPROVED by me; ontology's review pending.
- Withdrawn or closed today: the finance-filter "veto" item (matches the record routes, N2 / round 77), evaluator cost (already uniform; 403 0x80048306 = unknown), #2563 (closed).

## Dev BFF deploy (owner confirmed 2026-11-03; R3 rollout record §17, PR #2554)
- **Each deploy:**
  - the owner's go per build in THIS session;
  - pre-checks: `Set-ChildRecordCreatorPersonSchema.ps1 -Verify`, the 225-name settings check, ebilling's 6 keys, both write switches true;
  - `scripts/Save-BffDevRollbackSnapshot.ps1`, then the deploy, then `scripts/Test-BffDevDeployReadback.ps1`;
  - my 30-minute gate (copy `gated763.sh`);
  - record it in `docs/procedures/dev-bff-deploy-coordination.md`;
  - notices before and after to ontology, word-add-in and R4 (`spaarke-wt-spa-external-access-platform-ad`).
- **Queued:** #2550 (R3 docs/tests), #2551 (fm 020), #2562 (fm 042b), #2564 (fm 031, auto-merge), the core-pair branch (once its PR merges). Later: fm 042c (D-204 rework), ebilling's master PR.
- **Core-pair read-back (ontology's request):** snapshot BEFORE and AFTER the deploy plus one reconcile.
  - (a) The 7 project-core Signals: principalobjectaccess. Expected: no change; they carry matching stamps.
  - (b) The 4 service-request-core Decision Records: ownerid, owningbusinessunit, principalobjectaccess. Name any owner change.
  - (c) Re-query for service-request-core Signals (none found so far).
  - (d) Any pair row held after the deploy is a catalog data error: report it to ontology.
  - Ids: `sprk_signal` joined to `sprk_recordtype_ref` on `sprk_corerecordtype`, where `sprk_recordlogicalname` = 'sprk_project'.
- **ebilling 089e deploy order (K3):** the Billing Rule Reader extension must be applied before or with the BFF carrying 089e. **Met on dev** (my read-back: Reader and Billing Admin hold Policy/ruleset/procedure Read Deep; Reader has 0 holders).
- **ebilling master PR pre-checks:** sprk_invoice.sprk_firmhold present on dev (verified 2026-11-04); the Stored Files count on invoices of Secure records (dev: 1) with a K4 backfill after the gate; `StoredFileReconcileEnabled` stays false until the NFR-05 census; C22: 082c needs task 114 to lift the `DecidingIndexes.Count == 0` refusal before master. After the BFF gate: `Deploy-EbillingFormsViews.ps1` (owner go in ebilling's session).
- **Watches:** fm FR-40 live proof; word-add-in Office smoke; SPA registration message 85fbba3f64 waits for the next SPA deploy.

## Reviews owed / in flight
- **ebilling** (`spaarke-wt-ebilling-r1-c7`):
  - 089e APPROVED (2b6b027a63; assertion list option (a)); merged into their work at 6bb7d928a9; dev roles applied with my read-back.
  - 082b approved, merged into work 7061b5b7ec. K1: Level+Distance known limit; condition that Procedure names stay catalog-level.
  - 082c §3(c) shape approved with 3 conditions (the pre-check runs only after submission auth, using the authorized record and firm; one fixed sentence; a C16 source scan with must-fire); it goes to R4 `-ad`.
  - 082d snapshot design is binding: store ids in an app-only or field-secured part; findings carry only ForReaders(); names resolved at read behind the record check.
  - 095d-1: 1a merged (allocator columns sprk_submissionsequence and sprk_previoussequence listed); 1b adds sprk_firminvoicenumber plus a test that the undo still clears it. The FINAL REVIEW of all of 095d-1 is mine (plus ontology K-3).
  - Still owed: 089b/089c, 095a, 095c; the full master PR; the NFR-05 census after deploy.
- **field-mapping** (`spaarke-wt-field-mapping-server-write-pa-69`):
  - 042c rework diff (D-204).
  - 044 field-security profile (dev apply needs owner go HERE).
  - 046 roles matrix (owner go HERE per change set).
- **ontology** (`-38`): told about the typed `sprk_matter` readers (functional) and the `sprk_steps` integrity question; it's covered by D-204.

## Email-to-AP (owner)
- AP address ap@finance.spaarke.com (never receive-enabled).
- Completed demo batch **14680609-b198-48e2-8391-3ada84fda689**.
- Server side verified OK: ribbon, rules, script, enable rule.
- The owner still sees no buttons. Diagnostics owed by the owner: URL (org/app), an InPrivate retry, and the `&ribbondebug=true` Command checker on Email to AP.

## Owner's VS Code issue (C:/code_files/spaarke window hangs)
- **Done:**
  - archived 3 large transcripts to `~/.claude/projects/c--code-files-spaarke/_archive-2026-11-04/`;
  - VS Code user settings exclude `**/.claude/worktrees` (files, search, watcher) plus node_modules from the watcher; backup at `%APPDATA%/Code/User/settings.backup-2026-11-04.json`;
  - stopped 2 runaway rg scans by PID;
  - advised disabling Teams Toolkit / Postman per workspace.
- **Cause:** `C:/code_files/spaarke/.claude/worktrees` holds 123 dirs: 66 clean worktrees, 3 dirty, 54 leftover non-worktree dirs. The extension host stays unresponsive (Teams Toolkit topped the profiles).
- **Open:** may I clean up the old agent dirs? (Remove-WorktreeSafe; skip anything used in the last 24h and the 3 dirty ones; list them first.) Workaround: run `claude` in a terminal. A `spaarke-c4` session was busy, so it may be working now.

## My own backlog
- #2560 (share-mirror levels on RestrictedContentTables)
- #2524 (195 re-file link)
- gate null-column hardening
- SharePoint privileges census item
- #2543, #2292
- K-P1 for task 191
- parked: 193 (C:/wt193), 197, #2427, 196a/196b
- TASK-INDEX reconcile at wrap-up 090
- known limit: the lineage `sprk_corerecordid In (...)` walk is unindexed (prod hardening)

## Owner items outstanding
- Email to AP diagnostics.
- The VS Code cleanup OK.
- 095a lookup-name question.
- Click-test 195; Q1/Q4 private items.
- Older: #2343, #2245, #2250, #2260, #2270, #2262, #2396, 191 F1/S4, 190 Apply, #2382, #2505; tasks 140/165/153.

## Peers
- This session: `spaarke-wt-unified-access-control-r2-20`
- ebilling: `-c7`
- field-mapping: `-69`
- ontology: `-38`
- provisioning: `-b8`
- word-add-in: `-7b`
- website: `spaarke-website-cb`
- SPA: R3 closed (`-1f`/f2); R4 = `spaarke-wt-spa-external-access-platform-ad` (copy it on SPA invoice or record-modal UI changes); `-8c` holds messages (different permission mode).
