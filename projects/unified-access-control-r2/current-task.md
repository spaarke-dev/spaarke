# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-05 ~13:15Z (C19 cleared pending one answer; dev BFF = 909754869f, gate PASS; reviews and owed items below).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green: `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys and settings changes need the owner's go IN THIS SESSION. Peers take the owner's go for THEIR live writes in THEIR sessions. |
| **Active work** | None in flight. No uncommitted files, no running agents. Waiting on peers (reviews) and the owner (items below). |
| **Dev state** | BFF = **909754869f** (master with #2568) since 2026-11-05T04:16:30Z. Gate PASS, ontology PASS. Record: `notes/deploy-gate-2026-11-05-909754869f.md`. Rollback: `deploy/rollback/wwwroot-20261105T024806Z` in THIS worktree (d763dc120e) or redeploy d763dc120e. Dev SPA = b083d62183. |
| **Next action** | 1. **Owed to ontology:** /api/v1/signals 200 from organic or UAT traffic (no self-token call unless the owner has seen the command), plus the suppression and `SuppressionPartitionUndeterminedException` counts after the first nightly `SignalReevaluationJob` on 909754869f. Query App Insights `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`), role `spaarke-bff-dev`. A non-zero Undetermined count or a Signals 5xx reopens the gate. 2. Re-check the review diffs as they arrive (below). 3. Own backlog. |

## Private items (tracked privately only, owner choice; never in issues/PRs/commits)
- Core pair, lineage and update-path work: MERGED in #2568, deployed, read-back unchanged (7 project-core Signals, 4 SR-core DRs: owner/BU/POA unchanged; 0 SR-core Signals).
- **Owner question (K, not a regression):** when a person runs a playbook update (output mapping), the refusal for an unknown id differs from the refusal for a row they can't write. Making the two uniform would change #2257's stable code. Owner decides.
- The field-mapping push runs AS its caller, so for a person's write the boundary is their own Dataverse privileges plus field security (#2253 fix round 1, K2; by design). Confirmed again 2026-11-05.

## Reviews in flight
- **ebilling** (`spaarke-wt-ebilling-r1-c7`):
  - **095d-1 CLEARED** at b1930c9e21 (1a, 1b at d71b2f20f0, 1c with F4-1: `sprk_findingsjson` in the submission AccessColumns and MappingProtectedColumns, plus a field-security agreement test; F3-1; K2). 1c merges into work after ontology K-3. Wording nit sent (no re-check).
  - **082c-2 (coming):** `sprk_reviewdetail` approved with conditions:
    - a local closed choice;
    - cleared on every exit from In Review, with a test;
    - R4 renders it only In Review;
    - `MappingProtectedColumns`, not ParentLineage AccessColumns;
    - a known limit that every partner reader of the row sees it.
  - **082c-2 (coming):** `ForReaders()` BRR code uses option (a):
    - base catalog code plus an `IsRecordRule` flag, never an id;
    - fail closed to blank;
    - one place;
    - external surfaces drop the flag;
    - sentinel scan with must-fire.
  - **082c C19 CLEARED at c704a9f2dc** (2026-11-05):
    - the re-check confirms close only, the effective-date scope with mixed-date tests, the children-side failure dropping only the children, and the notes;
    - K5 (c) option (iii) is a K: the close stays system-attributed, logs carry the assignment id, and option (i) is filed for 095d-2;
    - (d) aggregate counts in the logs are OK;
    - adjustment lines never reach the evaluator (verified); the close log now carries the assignment id, the version id and the closed Signal ids;
    - waits only on ontology's ruling about the superseding version on the Signal row (provenance; no uac objection to deferring it to 095d-2).
  - Earlier conditions in force: 082c §3(c) pre-check conditions; 082d snapshot design; C22 (task 114 before 082c reaches master).
  - Still owed: 089b/089c, 095a, 095c; the full master PR; the NFR-05 census after deploy.
- **field-mapping** (`-69`):
  - #2569 (042c, auto-merge) joins the next deploy batch.
  - **048** extends D-204 to updates: chat update, the playbook step, the output mapping and the push; one shared list from `RecordTableRules`. Review it with ontology.
  - Also owed: 044 field-security profile and 046 roles matrix (owner go HERE).
  - 048 review: no F; two K fixes coming (the endpoint and push check the refusal themselves; a plain refusal message). The chat delete tool doesn't check the three D-204 tables. For uac it's a K, because it runs as the caller (same as the MDA). My recommendation: settle Delete on those tables in the 046 roles matrix; add the chat guard in 048 only if the owner extends D-204 to deletes (fm is asking the owner).
  - ebilling: `sprk_reviewdetail` column live write on dev (run by ontology, owner go in their sessions); after-notice and -Verify owed. `StoredFileReconcileEnabled` is held for DEF-044 (#2296); a before-notice is owed before any change (the gate expects false).
- **ontology** (`-38`): the 2 owed items above.

## Dev BFF deploy procedure
- `docs/procedures/dev-bff-deploy-coordination.md` on master. Owner go per SHA HERE; peer OKs (ontology, word-add-in, field-mapping, ebilling; provisioning standing).
- Gates:
  - CI on the commit;
  - #2450 ancestor and no `sprk_invoicestatus`;
  - `Set-ChildRecordCreatorPersonSchema.ps1 -Verify`;
  - 225 settings, with `SecureRoot__Inheritance__RootOwnerWritesEnabled` and `SecureChild__Reconciliation__OrdinaryMovesWritesEnabled` true, `Ebilling__Enabled` true, `Ebilling__StoredFileReconcileEnabled` false, and the 3 `Finance__FileStore__*` names.
- Steps:
  1. Detached worktree at the SHA.
  2. Snapshot with `Save-BffDevRollbackSnapshot.ps1 -RepoRoot <this worktree>`.
  3. `Deploy-BffApi.ps1 -Environment dev`.
  4. `Test-BffDevDeployReadback.ps1`.
  5. Before/after notices to the peers plus R4.
  6. Gate script `$TEMP/uacmerge/uacg/gate909.sh <start> <end>`.
  7. Record in `notes/deploy-gate-<date>-<sha>.md`.
  8. Remove-WorktreeSafe.
- **Next batch:** #2569 (fm 042c) when merged, then ebilling's master PR (pre-checks: sprk_firmhold on dev; the Stored Files K4 backfill; C22; `Deploy-EbillingFormsViews.ps1` after the gate, owner go in ebilling's session).
- **Peer live checks pending (their sessions, owner's go):**
  - word-add-in: Office save smoke (one filed and one unfiled save), plus a Submit Invoice from an email;
  - field-mapping: creates for 020/031/042b.

## Email-to-AP (owner)
- Completed demo batch **14680609-b198-48e2-8391-3ada84fda689**; the server side was verified OK. The owner still sees no buttons. Owed by the owner: the URL (org/app), an InPrivate retry, and the `&ribbondebug=true` Command checker on Email to AP.

## Owner's VS Code issue (C:/code_files/spaarke window hangs)
- **Done:**
  - archived 3 transcripts (`~/.claude/projects/c--code-files-spaarke/_archive-2026-11-04/`);
  - VS Code excludes `**/.claude/worktrees` (backup `%APPDATA%/Code/User/settings.backup-2026-11-04.json`);
  - killed 2 rg scans by PID.
- **Open:** may I clean up the old agent dirs in `C:/code_files/spaarke/.claude/worktrees` (123 dirs)? Use Remove-WorktreeSafe; skip anything used in the last 24h and the 3 dirty ones; list them first.

## My own backlog
- #2560 (share-mirror levels on RestrictedContentTables)
- #2524
- gate null-column hardening
- SharePoint privileges census item
- #2543, #2292
- K-P1 for task 191
- parked: 193 (C:/wt193), 197, #2427, 196a/196b
- TASK-INDEX reconcile at wrap-up 090
- known limits: lineage `sprk_corerecordid` walk unindexed; #2090 (optimistic mode)

## Owner items outstanding
- The person-path refusal question (above).
- Email to AP diagnostics.
- The VS Code cleanup OK.
- The Office saves and Submit Invoice for word-add-in.
- fm's test creates (in fm's session).
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
- SPA: R3 closed; R4 = `spaarke-wt-spa-external-access-platform-ad` (copy it on SPA invoice or record-modal UI changes); `-8c` holds messages.
