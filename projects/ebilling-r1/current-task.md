# Current Task State

> **Last Updated**: 2026-11-10 (context-handoff at the session move, by ebilling-r1-c7). **Session move:** this project moves from the VS Code session `spaarke-wt-ebilling-r1-c7` to the Claude Desktop session **`ebilling-r1`**. c7 launches nothing new. `ebilling-r1` takes over once c7 sends "HANDOFF COMPLETE". Only one session writes to this worktree at a time.
>
> Work head is `origin/work/ebilling-r1` (DEF-061 merged at `0775d2a443`; the tip was verified with build 0 errors, ArchTests 2200/0 and filtered unit tests 7747/0). The dev BFF is uac-r2's `6fbb3a7642` (reconcile false). **None of the 130 batch is on work, master or the dev BFF.** No agents are running.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project" and "move forward with the uac-r2 review cycle as expeditiously as possible". At most 4 agents. Merge each finished branch after its reviews, then verify the combined tip: build, full ArchTests, FILTERED unit tests. One adversarial pass per change set; two for partner-readable, auth or tenant-isolation work. A scoped Sonnet re-check when a fix changes the approach. Every agent launch states a model. Never use `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER. Every brief names the base sha and checks `git merge-base --is-ancestor`. Never cd into an agent worktree; use `git -C` and absolute paths. To remove one: `git worktree unlock`, then `Remove-WorktreeSafe.ps1 -Path <one path>` from the PowerShell tool, one path per call. A branch checked out in an agent worktree can't be checked out elsewhere until that worktree is removed. |
| **Public-repo rule** | Never put access-gap mechanisms, identity names, role memberships or privilege maps in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Live-change rule** | A live Dataverse write or role change needs the owner's OK typed in the CURRENT session, plus uac-r2 notices before and after. |
| **Peers** | `uac-r2`, `ontology-r1`, `provisioning-r1` (reachable again as of 2026-11-10), `word-add-in-r1`, `spa-r4`, `field-mapping-r1`. Cross-session messages can be HELD when permission modes differ; a held message is not delivered. |
| **Slow shell** | git and grep on this worktree often exceed 120 s and move to the background; wait for the notification. |

## Exact next actions (in order)

1. **F3b reviews.** F3b is done and pushed: `task/ebilling-130-0-f3b` at `b12d14d868`. It already contains 3g `79f5d1a508` and 130-0b.
   - **F3b's own change set:**
     - `09403449d8..cd4efb3ba6` (the pre-merge commits `c5553b1ec8` and `cd4efb3ba6`);
     - `8b122ae8f5..b12d14d868` (post-merge commits: K-a guard, lookup entries, the upload route fix `b849e8de9f`, REST tests, K-d comments, message routes, notes).
     - `8b122ae8f5` itself is the 3g merge. Its only conflict was the notes file, and both sections were kept.
   - **Run two Opus adversarial passes (tenant isolation), in parallel, each with a different focus:**
     - (a) the parent-chain walk, hop bound and fail-closed paths, and the firm rulings;
     - (b) K-a coverage of the 22 lookups, and the REST and assistant parity.
   - **Ask both passes to judge these open limits as real paths or not:**
     - **K-b (not implemented).** A child linked to an invoice only through the polymorphic `sprk_regardingrecordtype`/`sprk_regardingrecordid` pair, not a typed `sprk_regardinginvoice`. Does any writer produce that? If yes, it is a tenant-isolation defect: fix it in scope or send it to uac-r2 privately, never in an issue.
     - the notification feed lists the caller's own service requests without the invoice rule;
     - the event self-chain is not followed;
     - the REST ancestor walk covers single-edge tables only.
   - Then send uac-r2 split diff 3 (the same ranges).
   - **Fix-round baseline:** filtered tests show 18 timeout-only failures in untouched classes; their 16 classes pass 297/297 on a rerun. ArchTests: 2228/0.
2. **116, schema step first (EB-D-211).**
   - **Extend 080's `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1`** (P3):
     - add a memo column on `sprk_procedureversion` for the not-converted JSON list (choose the name, e.g. `sprk_notconvertedlist`; cap 100000 like `sprk_ruleset.sprk_notconverted`);
     - secure it, with the same grants as `sprk_notconverted`: Writers read/create/update, Readers read;
     - update the `sprk_notconverted` description to "drafted" (Yes written last);
     - add Pester tests and update `-Verify`.
   - **No SpaarkeMaster edits:** deploy task 079 syncs the source.
   - **Dev apply needs the owner's go in the current session.** Then send uac-r2 notices before and after, and run `-Verify`.
3. **116, the code.** The WIP is at `1571ff8551` on `task/ebilling-116` (pushed as WIP, unbuilt). It is checked out in agent worktree `C:\code_files\spaarke\.claude\worktrees\agent-a34109880857ed7fd`.
   - **Start a FRESH Opus agent:** either have it work in that worktree, or remove the worktree first so a new isolated agent can check out the branch.
   - **Rework the marker:** write the JSON list to the new memo, then write `sprk_notconverted` = Yes last. AlreadyDrafted reads the Yes/No. `RuleSetDetail.NotConverted` reads the memo of the highest-numbered version.
   - **Remaining work** (see the agent's report in the handoff history):
     - the two new ProcedureEndpoints routes, with authorization;
     - the StoredFileService replace helper and the per-version listing;
     - retiring the two RuleSetEndpoints routes, the ArchTests route ledger, the rate-card import file step and RuleReview;
     - `Deploy-EbillingFormsViews.ps1` and the new `Remove-EbillingSourceDocumentColumns.ps1`, read-only here;
     - the tests, fail-before runs, publish-size delta, code-review, adr-check, the notes and TASK-INDEX.
   - **Then** one adversarial pass, and uac-r2 reviews the two new routes.
   - **Report to the owner** the `sprk_sourcedocument` census and the read-only non-null counts. The owner said: delete only "if not needed; confirm not used or useful anywhere". The delete runs after 079.
4. **DEF-060, option A (EB-D-212).**
   - Write the design: `sprk_policyversion.sprk_firmdescription` and the reason-code companion on the Signal; copy at create in `SignalWriter.BuildEntity`; the fixed points listed in EB-D-212. The base design is the 130 notes on `task/ebilling-130d`.
   - Send it to uac-r2 and ontology-r1 and get both acceptances before building.
   - The schema change needs the owner's dev go.
5. **Batch merge** once F3b is approved: 130-0 (fix1 tip `79f5d1a508`) + 130-0b `0f6b49b5ab` + 130a/130b `2367fbab2c` + 130c `3a49f67f65` + 130d `69a8b2db72` + F3b `b12d14d868` (F3b already contains 130-0, 3g and 130-0b), into work.
   - Expect conflicts in the notes and in `WorkforceRoleCeilingGate.cs`.
   - **Housekeeping:**
     - use `ReadInvoiceSubmissionsWithInvoice` on both GET /invoice-submissions routes, and delete `ReadInvoiceSubmissions` if it is unused;
     - replace `InvoiceLinkedChildAttributes` (ExternalModuleDataEndpoints ~:502) with `ExternalInvoiceVisibility.IsInvoiceLinkedChildTable`;
     - check the K-d comment (F3b rewrote the stale firm comments; confirm it covers the locator);
     - derive `SubmissionCoreColumns` from `InvoiceSubmissionContracts` :69, or add a pointer comment;
     - trim the 3g notes consumer table's "Before" column;
     - fix the `ListingSelect =RowSelect` spacing (`ExternalGrantLifecycle.cs` ~:444).
   - **Then:**
     - verify the tip;
     - mark 130-0, 130-0b, 130a, 130b, 130c and 130d ✅ in TASK-INDEX (add a 130d row);
     - remove the agent worktrees;
     - send uac-r2 the batch diff note, and tell spa-r4 the `ProjectPage.tsx:~465` 404 branch is dead.
   - **Dev gate:** nothing from the batch reaches dev until all of it is merged; the deploy goes through uac-r2's session with the owner's go.

## Branch heads

| Branch | Head | State |
|---|---|---|
| `task/ebilling-130-0-fix1` | `79f5d1a508` | Rounds 3–3g; uac-r2 APPROVED diffs 1 and 2 |
| `task/ebilling-130-0b` | `0f6b49b5ab` | FINAL, approved |
| `task/ebilling-130b-fix1` | `2367fbab2c` | FINAL, approved |
| `task/ebilling-130c` | `3a49f67f65` | FINAL, approved |
| `task/ebilling-130d` | `69a8b2db72` | FINAL; uac-r2 approved; two Opus passes |
| `task/ebilling-130-0-f3b` | `b12d14d868` | DONE; awaiting two Opus passes, then uac-r2 diff 3 |
| `task/ebilling-116` | `1571ff8551` | WIP, paused at the EB-D-211 schema step |

**Agent worktrees to remove at the batch:**
- earlier rounds: aff95c719a54a6e5b, acfabc1702a61b70f, a0af46b7a8db4e045, af7d21452796b74a4, a56d6943daafbbf5e, ad9cdf00a103ae635, ae2d0b82254167eb0, a7b2594412602599c, aa937999fa605a887, a28d8883986230c2f, abc82a6e0f76f217d, a45f20db996c18d3a;
- F3b `ac0f083be9ad9dfe6`;
- DEF-061's worktree;
- 116 `a34109880857ed7fd`, only after 116 is finished.

## Rulings in force

- **D-70 / owner 2026-11-08: firm-match.** No firm means no invoices; a failed firm read hides everything.
- **REST.**
  - On a list, a firm-read failure returns 200, hides the invoice-linked rows and sets `truncated`.
  - A single invoice-linked record whose decision faulted returns 503 `InvoiceUnreadableReasonCode`.
  - A decided-hidden record gets the uniform 403.
  - read_query returns a tool error.
- **130b firm-outcome.** Unknown firms: 503. No firm: 403.
- **Absent-column class.**
  - An absent column is unreadable or undecidable; an explicit null means not filed.
  - An absent `sprk_accesspermission` maps to `Unreadable`.
  - `ChildAccessPermissionReconciler` belongs to uac-r2's task 198; do not add it to the guard.
- **EB-D-209, EB-D-210, EB-D-211, EB-D-212:** see the project CLAUDE.md §3 and `notes/decisions-log.md`.

## Open owner questions

- None open. The owner's go is still needed, in the current session, for:
  - the EB-D-211 memo column on dev;
  - the EB-D-212 schema on dev;
  - the `sprk_sourcedocument` delete, after 116 reports.

## Owed to peers

- **provisioning-r1:**
  - the 166 grants plus `-Verify` (seven columns; Writers read/create/update on the stamp's BFF app users; Readers read on every business unit's default team; no other profile; smoke checks);
  - the DEF-059 and 166 master SHA plus the SpaarkeMaster version;
  - the 089d SHA.
- **word-add-in-r1:** the 095c-2..4 dev deploy master SHA; the 095a/095b/151e PR plus SHA.
- **uac-r2:** F3b (diff 3); the 116 routes; the 116 memo column notices; the batch diff note; the 165e/165f batch SHA.
- **ontology-r1:** the RowReads rerun; the DEF-060 design; the DEF-059 outcome (never delivered).
  - The RowReads rerun runs after 095c-1's dev apply, from PowerShell with `SIGNALS_LIVE_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`, `SIGNALS_LIVE_CALLER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc` and `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, then `dotnet test --no-build`.

## Standing gates

- Creator-person `-Verify` exits 0 before any BFF deploy.
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0, nor with 095a before 1.3.0.1.
- One master PR, reviewed by ontology-r1, uac-r2 and R4.
