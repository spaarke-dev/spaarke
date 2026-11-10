# Current Task State

> **Last Updated**: 2026-11-10 (session ebilling-r1-c7, authoritative). Work head is `origin/work/ebilling-r1` at `b178e65d54`. The dev BFF is uac-r2's `6fbb3a7642` (reconcile false). **None of the 130 batch is on work, master or the dev BFF.** The previous version of this file is archived at `notes/handoff-history/current-task-2026-11-10a.md`.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project", and "move forward with the uac-r2 review cycle as expeditiously as possible" (2026-11-09). At most 4 agents. Merge each finished branch after its reviews, then verify the combined tip: build, full ArchTests, FILTERED unit tests. One adversarial pass per change set; two for partner-readable, auth or tenant-isolation work. A scoped Sonnet re-check when a fix changes the approach. Every agent launch states a model. Never use `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER. Every brief names the base sha and checks `git merge-base --is-ancestor`. Never cd into an agent worktree; use `git -C` and absolute paths. To remove one: `git worktree unlock`, then `Remove-WorktreeSafe.ps1 -Path <one path>` from the PowerShell tool, one path per call. |
| **Public-repo rule** | Never put access-gap mechanisms, identity names, role memberships or privilege maps in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Live-change rule** | A live Dataverse write or role change needs the owner's OK typed in THIS session, plus uac-r2 notices before and after. |
| **Peers** | `uac-r2`, `ontology-r1`, `provisioning-r1` (NOT reachable by that name; b8 retired), `word-add-in-r1`, `spa-r4`, `field-mapping-r1`. |

## Exact next action

Wait for the running agents and act on each one:
1. **DEF-061 fix round 1.** The agent is on `task/ebilling-def061`; its last report said it was waiting on ArchTests. Scope:
   - ontology-r1 F1: `OpenedAWindow` = `From && (Active/Superseded || (PublishedOn && !Draft) || Retired && publishedRules)`, as one shared predicate with `ScopeLockedAsync`.
   - `PublishedOn` becomes required.
   - Review F1: the backfill's audit date parse is culture/offset-safe, with Pester tests under en-US and en-GB.
   - `-Verify` and `-Backfill` report counts and fail unless the operator has org-depth Read; they also report the audit-versus-fallback split.
   - The chat-create refusal message for procedure versions.
   - The Repair reason (K1).
   - The K4 test name.

   When it's done: send ontology-r1 the SHA range for its scoped re-check. Once they pass it, run the live steps on dev (the owner's go was given 2026-11-09):
   1. the plain run (creates the column);
   2. `-Backfill -WhatIf`;
   3. `-Backfill`;
   4. `-Verify`.

   Send uac-r2 notices before and after. Then merge into work.
2. **F3b** (Sonnet xhigh, `task/ebilling-130-0-f3b`, base c5c30a9fc4 plus 130-0b `0f6b49b5ab`; it merges the 3g tip `79f5d1a508` before final verification). When it's done: one Opus adversarial pass, then a second (tenant isolation), then uac-r2 as split diff 3.
3. **116** (Opus high, `task/ebilling-116` from `8770b2c659`).
   - When it's done: an adversarial pass; uac-r2 reviews its two new routes.
   - Report to the owner the `sprk_sourcedocument` census and the read-only non-null counts. The owner said: delete only "if not needed; confirm not used or useful anywhere". The delete runs after 079.
4. **The owner's DEF-060 decision** (asked 2026-11-10, framed with uac-r2):
   - **A (recommended):** a per-rule admin description `sprk_policyversion.sprk_firmdescription`, copied onto the Signal at create in `SignalWriter.BuildEntity`. ontology-r1 must accept. Needs a schema change plus a dev go.
   - **B:** a per-rejection reviewer text.

   The owner's earlier words: "option C both; should be done in this project not deferred". Table-only (option 1) does not meet "both", so it is withdrawn. Design: the 130 notes on `task/ebilling-130d`.

   Fixed points from ontology-r1:
   - written only by the ontology writer identity, with no fallback;
   - no impersonation (`createdonbehalfby` empty);
   - never rewrites a Resolved Signal.

   Fixed points from uac-r2:
   - a write guard, a 200-character cap, no placeholders;
   - code-only logs;
   - the writer walks runes with the same Control/Format/Line/Paragraph predicate;
   - stores the reason code with the text, and the projection matches on it;
   - Duplicate never takes an override.
5. **Batch merge** once F3b is approved: 130-0 (fix1 tip `79f5d1a508`) + 130-0b `0f6b49b5ab` + 130a/130b (`2367fbab2c`) + 130c `3a49f67f65` + 130d `69a8b2db72` + F3b, into work.
   - Expect conflicts in the notes and in `WorkforceRoleCeilingGate.cs`.
   - Use `ReadInvoiceSubmissionsWithInvoice` on both GET /invoice-submissions routes, and delete `ReadInvoiceSubmissions` if it is unused.
   - Replace `InvoiceLinkedChildAttributes` (ExternalModuleDataEndpoints ~:502) with `ExternalInvoiceVisibility.IsInvoiceLinkedChildTable`.
   - Fix the K-d comment.
   - Derive `SubmissionCoreColumns` from `InvoiceSubmissionContracts` :69, or add a pointer comment.
   - Trim the 3g notes consumer table's "Before" column (it describes old gap behaviour).
   - Fix `ListingSelect =RowSelect` spacing (`ExternalGrantLifecycle.cs` ~:444).
   - Verify the tip.
   - Mark 130-0, 130-0b, 130a, 130b, 130c and 130d ✅ in TASK-INDEX (add a 130d row).
   - Remove the agent worktrees.
   - Send uac-r2 the batch diff note. Tell spa-r4 that the `ProjectPage.tsx:~465` 404 branch is dead.
   - **Dev gate:** nothing from the batch reaches dev until everything is merged; the deploy goes through uac-r2's session with the owner's go.

## Branch heads

| Branch | Head | State |
|---|---|---|
| `task/ebilling-130-0-fix1` | `79f5d1a508` | Rounds 3–3g. uac-r2 APPROVED diff 1 (`ed11b30f95..c5c30a9fc4`) and diff 2 (3g, `c5c30a9fc4..79f5d1a508`). Guard 54 groups / 121 sites, `KnownOpenSites` 0. ArchTests 2221/0. |
| `task/ebilling-130-0b` | `0f6b49b5ab` | FINAL, approved |
| `task/ebilling-130b-fix1` | `2367fbab2c` | FINAL, approved |
| `task/ebilling-130c` | `3a49f67f65` | FINAL, approved |
| `task/ebilling-130d` | `69a8b2db72` | FINAL. uac-r2 APPROVED all rounds; two Opus passes. Firm-safe reason table (10 codes); Duplicate is uniform and never overridden; read-side rune filter. |
| `task/ebilling-130-0-f3b` | running | diff 3 |
| `task/ebilling-def061` | `fd8a259460` + fix round 1 running | ontology-r1 re-check after the fix |
| `task/ebilling-116` | running | — |

Agent worktrees to remove at the batch: aff95c719a54a6e5b, acfabc1702a61b70f, a0af46b7a8db4e045, af7d21452796b74a4, a56d6943daafbbf5e, ad9cdf00a103ae635, ae2d0b82254167eb0, a7b2594412602599c, aa937999fa605a887, a28d8883986230c2f, abc82a6e0f76f217d, a45f20db996c18d3a, and later the F3b, DEF-061 and 116 worktrees.

## Rulings in force

- **D-70 / owner 2026-11-08: firm-match.** A contact sees only its own firms' invoices on records it can access. No firm means no invoices. A failed firm read hides everything.
- **REST.**
  - On a list, a firm-read failure returns 200, hides the invoice-linked rows and sets `truncated`.
  - A single invoice-linked record whose decision faulted returns 503 `InvoiceUnreadableReasonCode`.
  - A decided-hidden record gets the uniform 403.
  - read_query returns a tool error.
- **130b firm-outcome.** Unknown firms: 503. No firm: 403. The core union is kept, and the secure-roots rule applies to both the cores and the submission.
- **Absent-column class (rounds 3–3g).**
  - On a Web API row, an absent column is unreadable or undecidable; an explicit null means not filed.
  - Typed rows track presence, and selects are pinned.
  - An absent `sprk_accesspermission` maps to `Unreadable`.
  - Choice columns are outside the guard and covered by pins.
  - `ChildAccessPermissionReconciler` belongs to uac-r2's task 198; do not add it to the guard.
- **EB-D-209:** with the flag off, the firm sees the reason only.
- **EB-D-210:** reason code plus firm-safe text, whatever the flag (130d).

## Done this session (2026-11-09/10)

- **166 applied on dev.** `-Verify` passed, and the read check passed for the BFF, writer and plain-user identities. Notes 166 §7.
  - The forms redeploy also put two work-only forms on dev (submission `13a10bcbfc`, billing export `61ac61d5bc`); they stay unmerged on master until our master PR.
  - F2 (`_base`) is a non-issue under EB-D-200's read model, subject to two preconditions; precondition 2 is DEF-066 #2651.
- **Live guards.** The AuthorableColumns guard passes: 17 derived columns checked, 2 secured, both excepted (ontology-r1 recorded it; the 165b-4 gate is met on their side).
  - `FinanceBaseValuesSeamTests`: SpendQueries pass. RowReads is blocked by the missing `sprk_trackingscope` (095c-1 schema not on dev) and left no rows.
  - **Rerun RowReads after 095c-1's dev apply and send the result to ontology-r1.** The command needs `SIGNALS_LIVE_DATAVERSE_URL` and `SIGNALS_LIVE_CALLER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc`, run from PowerShell with `--no-build` after a build.
- **Decisions and spec.** EB-D-209 and EB-D-210 recorded in the decisions log, the project CLAUDE.md and the FR-28 spec text.
- **Filed.** DEF-066 #2651 (Bug, on the board).
- **H10 go** sent to provisioning. provisioning-r1 is unreachable by name, so the 166 grants message is still owed.

## Open owner questions

1. DEF-060: A or B (see above).
2. The `sprk_sourcedocument` delete confirmation, after 116 reports.

## Owed to peers

- **provisioning-r1** (unreachable by name; check ListAgents):
  - the 166 grants plus `-Verify` (text drafted in this session: seven columns; Writers read/create/update on the stamp's BFF app users; Readers read on every business unit's default team; no other profile; smoke checks);
  - the DEF-059 and 166 master SHA plus SpaarkeMaster version;
  - the 089d SHA.
- **word-add-in-r1:** the 095c-2..4 dev deploy master SHA; the 095a/095b/151e PR plus SHA.
- **uac-r2:** F3b (diff 3); the 116 routes; the batch diff note; the 165e/165f batch sha.
- **ontology-r1:** the DEF-061 fix SHA; the RowReads rerun; the DEF-060 design once the owner decides; the DEF-059 outcome (never delivered).

## Standing gates

- Creator-person `-Verify` exits 0 before any BFF deploy.
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0, nor with 095a before 1.3.0.1.
- One master PR, reviewed by ontology-r1, uac-r2 and R4.
