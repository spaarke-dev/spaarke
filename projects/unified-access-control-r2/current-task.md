# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md`, now at **round 122 (2026-10-22)**.
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§12 = ebilling D-146, §13 = slice D Restrict, §14 = ebilling EB-D-148 Procedures).
> - **Task 191 design + T191a results:** `notes/2179-flagged-external-dataverse-design.md` (last sections: carried-in answer, spend decision, T191a read-only and live spike F1–F5).
> - **Task 190 / round 122:** `notes/task-190-legacy-backfill.md`, `notes/task-190-round122-linked-eml-access.md`.
> - **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp59.md`.

> **Last Updated**: 2026-10-22 (checkpoint #60).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 wrap-up rules. Merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green. One background merge job at a time. Merge with `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. |
| **Agents** | Opus for executors and verifiers, with every model stated. Helper scripts get a unique prefix in a private folder. Cap 4 concurrent. |
| **Dev live** | BFF `1.0.0+a7b956fb55` since **02:18:52Z 2026-10-22** (R3, owner go): #2221 (task 190) + #2223 + fm-016. Rollback snapshot: R3 `deploy/rollback/wwwroot-20261022T021635Z`. Master is now **230302c307** (#2234, round 122); the only src/ change since live is #2234. |
| **Peers** | R3 `spaarke-wt-spa-external-access-platform-05`, ebilling `spaarke-wt-ebilling-r1-5b`, word-add-in **`spaarke-wt-spaarkeai-word-add-in-r1-c4`** (the -a1 name is gone), field-mapping `spaarke-wt-field-mapping-server-write-pa-be`, provisioning `spaarke-wt-customer-provisioning-orchest-ff`, ontology `spaarke-wt-spaarke-ontology-platform-r1-79`. |

## Next actions (mine)
1. **Round 122 (#2234, merged 230302c307): needs deploy + gate.** Batch it with the #2237 fix into one R3 deploy (owner go in R3's session; ancestry check against a7b956fb55). Dev already has the precondition (`Set-DocumentNarrowingFieldSecurity.ps1`). Gate: the `secure-child-reconciliation` report shows `releaseRefusedLinkColumnsNotSecured = 0`, with no new exception class. Then record it in note §5 and remove C:/wt122 (check junctions first).
2. **#2237 (DocumentProfileOutputMapper writes AI-output keys app-only, bypassing RefuseAppOnlyWrite and the link refusals).**
   - An implementer agent is running in worktree C:/wt2237, branch `fix/uac-2237-profile-mapper-refusal`.
   - Then: one adversarial pass (two if it counts as security), merge, and add it to the round 122 deploy.
3. **Task 191: blocked on two owner decisions, asked in text (the owner asked for explanations, and those were given).**
   - **F1:** a flagged user can lower `sprk_accesspermission` on a row shared with them for Write. Recommended: field security on that column, with writers = the 2 BFF app users plus a profile on the internal business units' default teams. The alternative is read-only mirror shares.
   - **S4:** recommended: view/download only for flagged users. The other options are a BFF SSH-console invite test, or defer.
   - **Other T191a findings:** F2 (RevokeAccess leaves principal-object-access rows at mask 0; recheck the count), F3 (search latency for team shares; recheck after 24 h), F4 (a business-unit move does not move owned rows), F5 (WhoAmI ignores impersonation).
   - **Spike scripts:** in temp `uac191a/` and `uac191b/`. Undo has been run.
4. **Task 190 owner items:**
   - **Apply** (`EmailArchiveLinkSweep:Apply`; restarts the BFF). Before it, run the migration report-only to `passComplete`. After it, run the migration again and save the ResultJson.
   - **The owner's Restricted call** on the 91 unmatched archives plus their 20 children.
5. **CI:** the Tier 1 Arch Tests job has a 5-minute timeout that intermittently cancels it (evidence on #2224). Re-run the job after the run completes.

## Peer reviews owed (UAC is the gate)
- **field-mapping 018a:** a condition-(b) adversarial reviewer is running. Then reply ACCEPT, or send F items, and concur on the maker's-contact default with two conditions: the adapter surfaces `default_value_dropped`, and it never writes Assigned To app-only after the drop. Also owed: 017 and 018b.
- **ebilling:**
  - 088 is fully accepted (pre-flight 83aec94fc0 ACCEPT; profile reuse conditions met).
  - 119 was accepted at 8986bc5292.
  - 095: the submission Restricted is non-provisional (test a4695317e4). #2236 was closed as a duplicate of #2237.
  - Gates owed: 081, 086 (2 passes), 089, 090, 094, 095, 098 (2 passes), 117.
  - 091 still owes the what-if, `Test-EbillingFileStoreRole.ps1` run live, and the tag-write proof output.
  - Standing rule: no role privilege on the Procedure tables before 191 without UAC review.
- **word-add-in task 130:** two security passes when the PR arrives. They were told about the #2234 merge.

## Owner items outstanding
1. F1 and S4 (above) block T191b onward.
2. Task 190 Apply, and the Restricted call on the unmatched archives.
3. **Optional live checks:** partner document read (D7 matters `b68299c6…` → 0 documents, `86335ce3…` → 3); #2177 Outlook (2 .eml saves); #2161 re-file; T191 MDA/Copilot/grid checks.
4. **Carried:**
   - #2163 environment-change go.
   - 186: Tracy intake checks 3/5b/6 and the Workforce role export.
   - 036 gates (ii)–(vi).
   - #2153 sign-off (#2152, C:/wt2152, draft).

## Done 2026-10-22 (since checkpoint #59)
- **Task 190:** closed. #2221 was deployed in a7b956fb55; server gate PASS (the sweep dry run equals the census); R3's 30-minute reading was clean.
- **#2230** merged (fixes #2229, SPE suite fixture drift).
- **Round 122:** designed, reviewed (word-add-in plus 2 adversarial passes, 2 fix rounds) and merged as **230302c307** (#2234).
- **Task 191:** the carried-in question is answered (no new gate). The owner kept spend visible to flagged users. T191a S1–S3 ran live and were undone; S4 is blocked on a credential.
- **Peers:**
  - ebilling 119 and 088 ACCEPTED.
  - field-mapping 018 questions answered: (1)(a) refuse with a field-naming code; (2) accept; (3) OK with conditions.
- **Filed:** #2237. **Housekeeping:** merged worktrees removed, including wt2103 and wt036 (their junctions were unlinked first).
