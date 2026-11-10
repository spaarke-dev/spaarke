# Current Task State

> **Last Updated**: 2026-11-10 (session `ebilling-r1`, Claude Desktop, authoritative). c7 handed over at `698fd222cc` and is idle; this is the only session writing to the worktree.
>
> Work head is `origin/work/ebilling-r1` (DEF-061 merged; DEF-060 design docs on top). The dev BFF is uac-r2's `6fbb3a7642` (reconcile false). **None of the 130 batch is on work, master or the dev BFF.**
>
> **Agents running (3 of 4):**
> - F3b fix round 1 (Opus, F3b worktree);
> - 116 part 2 (Opus, the 116 worktree);
> - 116 part 3 (Sonnet high, isolated worktree, branch `task/ebilling-116-ui`).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project" and "move forward with the uac-r2 review cycle as expeditiously as possible". At most 4 agents. Merge each finished branch after its reviews, then verify the combined tip: build, full ArchTests, FILTERED unit tests. One adversarial pass per change set; two for partner-readable, auth or tenant-isolation work. A scoped re-check when a fix changes the approach. Every agent launch states a model. Never use `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER. Every brief names the base sha and checks `git merge-base --is-ancestor`. Never cd into an agent worktree; use `git -C` and absolute paths. To remove one: `git worktree unlock`, then `Remove-WorktreeSafe.ps1 -Path <one path>` from the PowerShell tool, one path per call. |
| **Public-repo rule** | Never put access-gap mechanisms, identity names, role memberships or privilege maps in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Live-change rule** | A live Dataverse write or role change needs the owner's OK typed in THIS session, plus uac-r2 notices before and after (and ontology-r1 notices for `sprk_policyversion`). |
| **Peers** | `uac-r2`, `ontology-r1`, `spa-r4` (messages from here are HELD and expire), `provisioning-r1` and `word-add-in-r1` (unreachable 2026-11-10), `field-mapping-r1`. |

## Exact next actions

1. **F3b fix round 1 (running, Opus).**
   - Both Opus passes returned NOT APPROVED. The two F1 findings:
     - (1) the descendant walk is per table, so rows of invoice-linked child tables whose own invoice column is null never follow their parent edges;
     - (2) the notification feed skips the invoice rule for the caller's service requests.
   - The K items: the module parent-column projection, the guard's must-fire control, two exclusion reasons and a stale comment.
   - K-b was ruled NOT a real path by both passes. The client-side `PolymorphicResolverService.ts:637-642` suggestion (refuse instead of warn) is out of F3b's scope: file it via `/project-defer-issue-tracking` without the mechanism detail.
   - **When the round is done:** run a scoped Opus re-check of the fix diff (the approach changed: a per-row walk and the multi-edge REST walk), then send uac-r2 diff 3, covering F3b's ranges `09403449d8..cd4efb3ba6` and `8b122ae8f5..<new head>`.
2. **116 (running).** The head was `15053b7161`. The schema script (`sprk_notconvertedlist`, field-secured, Pester 95/95), the EB-D-211 code, the two routes, the file helpers and the retirements are done. Two agents are running:
   - **part 2** (Opus, `task/ebilling-116`): tests, the 6 failing ArchTests ledgers, the rate-card file step, fail-before, the publish-size delta, code-review and adr-check;
   - **part 3** (Sonnet, `task/ebilling-116-ui` from `15053b7161`): RuleReview, `Deploy-EbillingFormsViews.ps1`, and the read-only `Remove-EbillingSourceDocumentColumns.ps1` with its read-only dev run.
   - **Then:**
     - merge `-ui` into `task/ebilling-116`;
     - verify;
     - run one adversarial pass;
     - uac-r2 reviews the two routes (`POST .../procedures/{p}/versions/{v}/draft`, `PUT .../versions/{v}/file`);
     - clear the `RecordOwnershipResolver.cs:1809` citation entry with uac-r2;
     - close out the notes and TASK-INDEX.
   - **Dev apply of 080's script (memo column):** needs the owner's go here, plus uac-r2 notices.
   - **`sprk_sourcedocument` census (dev, read-only):**
     - `sprk_ruleset`: 0 rows, 0 non-null; `sprk_ratecard`: 0 rows, 0 non-null.
     - The only dependency is each table's main form.
     - Recommendation: delete after the references are cleared and 079 deploys. This needs the owner's confirmation.
3. **DEF-060 (EB-D-212, EB-D-213).**
   - The design `notes/def-060-firm-description-design.md` is ACCEPTED by both peers:
     - uac-r2: C1–C4 (C4 = the name guard at copy time);
     - ontology-r1: O1–O6.
   - EB-D-213 (owner): `sprk_firmdescription` is UNSECURED.
   - **Build after 116 and the 130 batch merge.** It needs the owner's dev go for the column (ontology recipe), notices to both peers, and the scoped `Services/Signals/**` review by ontology-r1 before merge.
4. **Batch merge** once F3b is approved: 130-0 (`79f5d1a508`) + 130-0b `0f6b49b5ab` + 130a/130b `2367fbab2c` + 130c `3a49f67f65` + 130d `69a8b2db72` + F3b (it already contains 130-0, 3g and 130-0b), into work.
   - Expect conflicts in the notes and in `WorkforceRoleCeilingGate.cs`.
   - **Housekeeping:**
     - use `ReadInvoiceSubmissionsWithInvoice` on both GET /invoice-submissions routes, and delete `ReadInvoiceSubmissions` if it is unused;
     - replace `InvoiceLinkedChildAttributes` (ExternalModuleDataEndpoints ~:502) with `ExternalInvoiceVisibility.IsInvoiceLinkedChildTable`;
     - check the K-d comment;
     - derive `SubmissionCoreColumns` from `InvoiceSubmissionContracts` :69, or add a pointer comment;
     - trim the 3g notes consumer table's "Before" column;
     - fix the `ListingSelect =RowSelect` spacing (`ExternalGrantLifecycle.cs` ~:444).
   - **Then:**
     - verify the tip;
     - mark 130-0, 130-0b, 130a–d ✅ in TASK-INDEX (add a 130d row);
     - remove the agent worktrees;
     - send uac-r2 the batch diff note, and tell spa-r4 the `ProjectPage.tsx:~465` 404 branch is dead.
   - **Dev gate:** nothing from the batch reaches dev until all of it is merged; the deploy goes through uac-r2's session with the owner's go.

## Branch heads

| Branch | Head | State |
|---|---|---|
| `task/ebilling-130-0-fix1` | `79f5d1a508` | uac-r2 APPROVED diffs 1 and 2 |
| `task/ebilling-130-0b` | `0f6b49b5ab` | FINAL, approved |
| `task/ebilling-130b-fix1` | `2367fbab2c` | FINAL, approved |
| `task/ebilling-130c` | `3a49f67f65` | FINAL, approved |
| `task/ebilling-130d` | `69a8b2db72` | FINAL, approved |
| `task/ebilling-130-0-f3b` | `b12d14d868`+ | fix round 1 running (two Opus passes: NOT APPROVED, findings F1 ×2) |
| `task/ebilling-116` | `15053b7161`+ | part 2 running |
| `task/ebilling-116-ui` | from `15053b7161` | part 3 running |

**Agent worktrees to remove at the batch:**
- earlier rounds: aff95c719a54a6e5b, acfabc1702a61b70f, a0af46b7a8db4e045, af7d21452796b74a4, a56d6943daafbbf5e, ad9cdf00a103ae635, ae2d0b82254167eb0, a7b2594412602599c, aa937999fa605a887, a28d8883986230c2f, abc82a6e0f76f217d, a45f20db996c18d3a;
- DEF-061 `ae6a8b9f011df7b9c`;
- F3b `ac0f083be9ad9dfe6`;
- 116 `a34109880857ed7fd` and the part-3 worktree, only after 116 is finished.

## Rulings in force

- **D-70 / owner 2026-11-08: firm-match.** No firm means no invoices; a failed firm read hides everything.
- **REST.**
  - On a list, a firm-read failure returns 200, hides the rows and sets `truncated`.
  - A single faulted record returns 503 `InvoiceUnreadableReasonCode`.
  - A decided-hidden record gets the uniform 403.
  - read_query returns a tool error.
- **130b firm-outcome.** Unknown firms: 503. No firm: 403.
- **Absent-column class.** An absent column is unreadable or undecidable; an explicit null means not filed. `ChildAccessPermissionReconciler` belongs to uac-r2's task 198.
- **EB-D-209 to EB-D-213:** see the project CLAUDE.md §3 and `notes/decisions-log.md`. EB-D-213 is recorded only in the decisions log so far; add it to the project CLAUDE.md §3 at the next CLAUDE.md edit.

## Open owner questions / pending goes

- The dev go for 080's script (the `sprk_notconvertedlist` memo column), plus uac-r2 notices.
- Confirmation of the `sprk_sourcedocument` delete (both tables are empty; the delete runs after 079).
- Later: the dev go for the DEF-060 column and the smoke Signal.

## Owed to peers

- **provisioning-r1** (unreachable):
  - the 166 grants plus `-Verify` (seven columns; Writers read/create/update on the stamp's BFF app users; Readers read on every business unit's default team; no other profile; smoke checks);
  - the DEF-059 and 166 master SHA plus the SpaarkeMaster version;
  - the 089d SHA;
  - the session-move notice;
  - the 116 memo column grant line.
- **word-add-in-r1** (unreachable): the 095c-2..4 dev deploy master SHA; the 095a/095b/151e PR plus SHA; the session-move notice.
- **spa-r4:** the session-move notice (expired undelivered); the `ProjectPage.tsx` note at the batch.
- **uac-r2:** F3b diff 3; the 116 routes; the 116 memo column notices; the batch diff note; the 165e/165f batch SHA; the DEF-060 build diff.
- **ontology-r1:** the RowReads rerun after 095c-1's dev apply (PowerShell, with `SIGNALS_LIVE_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`, `SIGNALS_LIVE_CALLER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc` and `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, then `dotnet test --no-build`); the DEF-059 outcome; the DEF-060 Signals diff.

## Standing gates

- Creator-person `-Verify` exits 0 before any BFF deploy.
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0, nor with 095a before 1.3.0.1.
- One master PR, reviewed by ontology-r1, uac-r2 and R4.
