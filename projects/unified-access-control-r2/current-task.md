# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-06 ~02:25Z (#2577 merged f34993dc78; ebilling 164a is create-only; the next deploy waits for 049).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; peers take the owner's go for their own live writes in their sessions. |
| **Active work** | **#2560** (a mirrored share on a hard-deny row carries Read only): worktree C:/wt2560, branch `task/uac-2560-hard-deny-mirror-read` from f34993dc78, with an Opus implementer running. Then: adversarial review twice (security), publish-size delta, PR. Earlier: **PR #2577 MERGED** as `f34993dc78` (POST /api/v1/documents: the caller needs Create on sprk_document, in the caller's BU container); worktree C:/wtdc removed; fm was told and adds the "Summarize Files (unfiled document)" matrix row in 046b. No uncommitted files. |
| **Dev state** | BFF = **0f53e053bf** since 2026-11-05T21:39:06Z (gate PASS; record `notes/deploy-gate-2026-11-05-0f53e053bf.md`; gate script `$TEMP/uacmerge/uacg/gate0f5.sh`). Rollback snapshot `deploy/rollback/wwwroot-20261105T213757Z` = 909754869f. Dev SPA = b083d62183. 046b role additions are live (below). The root default team "Spaarke" holds Console User + Basic User only. |
| **Next action** | 1. (Done: #2577 merged.) 2. (Done: nightly on 0f53e053bf clean; sent to ontology 02:5xZ; census now 5, after the root-team change.) 3. (Done 02:35Z: 046b R1 removed on dev from `e6c0c94fa3`; -Remove -Verify and -Verify PASS.) Review the 046b PR when fm sends it; G11/G12: owner ruled in fm's session "not granted (Core User required)"; 12 entries stay in the ratchet; fm's final round checks that send and re-file answer a not-held probe with 403, not 500. 4. Review fm 049b when the head SHA arrives (~3–4 h from 01:00Z). 5. Then ONE combined deploy (owner's go naming the SHA; every peer re-OKs the combined SHA, ontology included; avoid restarts 02:20–03:00Z). |

## The next dev deploy (owner: wait for 049)
- Contents: master `0f6618094b` (#2573 fm 043) + #2577 (`f34993dc78`) + 049. Code pages: LegalWorkspace, SpaarkeAi, CreateProjectWizard, AI.Widgets.
- Gate: gate0f5.sh set, plus:
  - fm 043: analysis create 0 5xx; push 0 5xx with the 409 rate unchanged on non-analysis targets; child-records create 0 5xx;
  - ontology: table_unsupported = baseline 0; writer codes 0; SignalWriter/DecisionRecordWriter refusals 0; Undetermined 0; census unchanged (5 since 2026-11-06 00:30Z).
- ebilling gates: `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` (params in project CLAUDE.md §5 / gate notes) exits 0; `Ebilling__StoredFileReconcileEnabled` stays false.

## Reviews in flight
- **field-mapping** (`-69`):
  - **046b:** the additions G1, G2, G4, G6, G7, G8 were APPLIED on dev 2026-11-05 from `98813dcaa2` (-Verify PASS). Removals: R1 (Ontology Admin loses Create on sprk_budgetrevision) APPLIED on dev 2026-11-06 02:35Z from `e6c0c94fa3` (-Remove -Verify + -Verify PASS). R2 (Basic User AppendTo reportcard) is DROPPED: its condition failed because the communication send/re-file paths need it. Run `-Remove` dry run → apply → verify for R1, under the owner's existing go.
  - **049b** (fix round). Decisions sent:
    - maker-as-assignee stays inline, test pinning both sides;
    - only the removal paths take the lease (#2574 filed for revoke-on-change);
    - scan caps fixed in 049, all three scans (Assigned, Restricted, Ledger);
    - record-busy stays a 500 ProblemDetails;
    - **secure from creation** (PRIVATE wording): the wizard's create declares secure intent (`secure=true`, only sprk_project/sprk_workassignment), runs the isolated-create path, and the Assigned-To pass runs after isolation. D1: ask the SAME rights as DelegationRuleFilter (Write + Share), as the caller, reusing its predicate, same 403. D2: the wizard treats `already_provisioned` as success after an intent create; the server answers it ONLY when the share and the container both exist (test). A3 unchanged.
    - When reviewing: check D1's predicate extraction carefully (the likeliest F).
  - **046c (after 046b merges): Basic User read-only on sprk_* (owner ruling in fm's session, fm decisions `d24514a6a9`).** fm sends the inventory and the dry run privately. My conditions:
    - non-human principals (app users, service identities) that get writes through the default team: a stop;
    - human Basic holders without Core, by count and kind;
    - their 14-day writes;
    - ownership stays valid on Read;
    - the rollback list generated before the apply;
    - its own window, apart from the deploy, with a ≥2 h denied-privilege watch;
    - a guard with controls.
  - 044 field-security profile still owed (owner go here).
- **ebilling** (`-c7`): master PR coming (082c-3, 114, 095d-0, 095d-2/2a, EB-D-188 Add line). My checklist:
  - the pre-P2 table; the `sprk_trackingscope` re-grep; the runbook lines; prevseq MappingProtectedColumns; C22; the full integration suites;
  - 095d-2: partners stay withheld while the re-run is pending; a failed job keeps the hold; placement in `Services/Ai/Jobs` if AI-coupled;
  - 095d-2a option A conditions: names/quotes only in field-secured `sprk_findingsjson`; never on external routes; not indexed or logged; the push gap on that column closed;
  - EB-D-188: ledger entry; G5 on the invoice; In Review + firm only; no external reach; the DR names the caller.
  - **164a (sprk_billingevent): generic CREATE refused only** (the full writer-only rule would break D-96's detach/move/re-attach, which uses generic update). Conditions sent: a separate create-only flag (NOT `CreatedOnlyByItsWriter`, which also drives 048's update/delete refusals) plus one derived property read by every create site; the PR lists every reader of the flag as create or update/delete; tests: create refusals write nothing; must-not-fire on D-96 re-attach and the typed writers; signal/DR/budgetrevision refusals unchanged; delete unchanged and stated; guard text at AccessTableReaderScanTests:73 updated. Gate effect: ontology's `table_unsupported` baseline count must be filtered by table after 164a deploys (tell ontology at that deploy).
  - After the PR: P2 migration (their session), then my flip of `Ebilling__StoredFileReconcileEnabled` (owner go HERE).
- **ontology** (`-38`): Worklist 200 from organic traffic DONE (01:31:37Z). Nightly counts sent (clean). 174 (commit route refuses billing Signals, 409 `decision.decided_in_invoice_review`) approved in placement; binding condition: the PR lists every Signal-decide entry point (refuse + test, or a reason with file:line). Send-SHA pending. Audit pass at 03:00Z: 89 tables / 569 columns incl. the 59 field-secured (owner go in ontology's session).

## Owner items outstanding
- **#2574:** turn on `JobRevokeOnChangeEnabled` (G-8) on dev. App-setting change: owner go here.
- **Column auditing** on the writer-only tables' lookups (ontology recommendation): Signal (sprk_matter, sprk_decisionrecord, sprk_policy, sprk_policyversion); DR (sprk_matter, sprk_project, sprk_policyversion); BudgetRevision (sprk_budget, sprk_matter). A solution schema change: owner go + notice to customer-provisioning. Not asked yet.
- ebilling 095d-2a: A (store) vs C (re-read): the owner's choice in ebilling's session; my conditions apply to A.
- The person-path refusal uniformity question (private, K).
- Email to AP diagnostics (batch 14680609-b198-48e2-8391-3ada84fda689).
- VS Code agent-dir cleanup OK (`C:/code_files/spaarke/.claude/worktrees`, 123 dirs; Remove-WorktreeSafe; skip anything used in the last 24h and the dirty ones).
- word-add-in: the owner's Office saves and Submit Invoice (read-back on the live build).
- 095a lookup-name question; click-test 195; Q1/Q4 private items.
- Older: #2343, #2245, #2250, #2260, #2270, #2262, #2396, 191 F1/S4, 190 Apply, #2382, #2505; tasks 140/165/153.

## Decided 2026-11-05/06 (in notes/decisions.md)
- Flagged licensed users read DR and Signals per their roles.
- 046 additions; Office To Do / Do lane Core-only; Ontology Admin = cleanup role.
- Office, AI Analysis and Console User all need Core User (no standalone grants).
- R1 removal approved; R2 dropped (its condition failed).
- Office Add In User: licensed core users only, assigned directly, never via a team; the 4 service/demo accounts don't get it back.
- The wizard's secure project is secure from creation (049); A3 unchanged.
- The next deploy waits for 049.

## Private items (never in issues/PRs/commits)
- Core pair, lineage and update paths: merged in #2568 and deployed.
- The push runs as the caller (#2253 K2); its lack of external narrowing and the 404 timing are round-77 class Ks.
- 049 secure-from-creation reasoning (assigned contacts granted before securing) stays in peer channels only.

## My own backlog
- #2576 (probe fault shown as denial; ~26 callers), #2560, #2524, gate null-column hardening, SharePoint census, #2543, #2292, K-P1 for task 191; parked: 193 (C:/wt193), 197, #2427, 196a/196b; TASK-INDEX reconcile at 090.

## Peers
uac-r2 `-20` (this session) · ebilling `-c7` · field-mapping `-69` · ontology `-38` · provisioning `-b8` · word-add-in `-7b` · website `spaarke-website-cb` · SPA R4 `spaarke-wt-spa-external-access-platform-ad` (copy it on SPA invoice or record-modal UI changes).
