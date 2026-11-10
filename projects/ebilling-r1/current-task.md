# Current Task State

> **Last Updated**: 2026-11-10 (session `ebilling-r1`, Claude Desktop, authoritative). c7 handed over at `698fd222cc` and is idle; this is the only session writing to the worktree.
>
> Work head is `origin/work/ebilling-r1` (DEF-061 merged; DEF-060 design docs on top). The dev BFF is uac-r2's `6fbb3a7642` (reconcile false). **None of the 130 batch is on work, master or the dev BFF.**
>
> **Agents running (3 of 4):**
> - F3b fix round 1, step 2 (Opus, F3b worktree, from `d3d997511d`): F2 feed tests, the service-request parity test, the F1 route/handler tests on both planes, K1 with narrowing off, a C2 pin, fail-before, full ArchTests. Step 1 is DONE: ExternalAccess filter 5682/5682; 142 new pins; a real defect fixed in `55ae7e6cec` (the message ancestor map was keyed by reference, so every message read as a failed chain). Still to do after step 2: C1 logging and the C3 dev query (if the owner accepts the conditions), code-review, adr-check, notes, then the scoped Opus re-check, then diff 3;
> - 116 verify + code-review + adr-check (Sonnet high, report only);
> - 116 adversarial pass (Opus, read-only).
>
> **RULED (EB-D-215): the walk's bound is 5 hops, with uac-r2's conditions C1, C4, the batched read per hop and the page read count. That work goes into the F3b step after step 2.** History: F3b's 3-hop walk now hides any record whose parent chain is longer than 3 links, even with no invoice in it (fail closed). The owner said "Ask uac-r2 first" (2026-11-10). uac-r2 replied: accept failing closed at 3 hops, on four conditions. The decision is still the owner's.
> - **C1:** a walk that hits the bound logs a warning (table, row id, hop count, plane), and the metric counts it if there is one. On a list, `truncated` stays set. A single record gets the same answer as a denied id.
> - **C2:** only child-to-child hops count. Reaching a core record means "no invoice". By my reading of `InvoiceLinks.cs:199-229`, the code already does this: parent edges are added only to a decided child or a descendant, so a core record is never walked. Confirm this in the diff 3 notes. Answer for uac-r2: a service request follows the child rule (`Supplemental`, `:84-90`, their earlier ruling).
> - **C3:** a read-only dev query finds the deepest child-to-child chain across the 16 tables, and diff 3 notes the count of rows past 3 hops. If any row a firm can see goes past 3, raise the bound to that depth plus 1.
> - **C4:** record it as a known limit, including the over-hiding on multi-edge tables. File an issue with the C3 number, worded neutrally: "rows whose child chain exceeds N hops are withheld from external callers". No examples.

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
2. **116 (in review).** Head `68aea7af88` on `task/ebilling-116`, pushed: part 2 (`936699a4b0`) plus part 3 merged in (only the notes conflicted).
   - Part 2 results: ArchTests 2200/2200; filtered unit tests 7914 passed, 0 failed; publish size +0.31 MB. It found and fixed a defect: the `/file` route stored the client's claimed Content-Type, and now derives it from the file name. The rate-card workbook now goes through `ReplaceForCallerAsync`, and a refused write deletes the card's rows.
   - Part 3 results: RuleReview Jest 60 passed; Pester 25/25 and 5/5.
   - **Verify plus code-review and adr-check (Sonnet): all green at `68aea7af88`.**
     - BFF build 0/0; ArchTests 2200/2200; filtered unit tests 7914/0; RuleReview Jest 60/60 and its build; Pester 5/5, 25/25 and 95/95 (the first two need Pester 5.7.1; the schema file needs Pester 3.4 under pwsh).
     - No ADR violations.
     - **Findings beyond the adversarial ones:**
       - F4: `PutVersionFile` has no `RequestSizeLimit` (the `InvoiceSubmission*` routes have one), so a configured cap above ~28 MB fails at Kestrel's limit;
       - K4: a drafted version with no single Servable file answers 422 before 409;
       - K4: there is no file-extension allowlist on the PUT (an unsupported file fails only in the job);
       - §11: "which Servable source files does this version have" is implemented three times (`StoredFileService.ReadRowsForProcedureVersionAsync`, `ProcedureService.CountServableSourceFilesAsync` ~:1151, `RuleSetService.LoadServableSourceFilesAsync` :643). Consolidate them, or add the §11 remark.
     - Access observations went to uac-r2 privately, with the route review.
   - **Adversarial pass (Opus): NOT APPROVED, 6 F-class findings.**
     - **A1 (F1):** a PUT can replace the file on a version published mid-upload (the Draft check sits at `ProcedureEndpoints.cs:331`; `ReplaceForCallerAsync` never re-checks). The rate-card import has the same gap (`:193` vs `:338`). Fix: after the new row is Servable and before the withdraw, re-read the status; if it is no longer Draft, withdraw the new row and return 409 `procedure_version.not_draft`.
     - **A2 (F1, OWNER DECISION):** a replace after drafting leaves the proposals and the list stale, and the draft route then returns 409 `ruleset.already_drafted` forever. A PUT during a running job causes the same. Option (a): the PUT refuses a drafted version (recommended). Option (b): the replace clears the marker and the list.
     - **A3 (F2):** a cancelled request skips the rollback (`StoredFileService.cs:943-945` passes the request token, and the catch excludes OCE), which leaves two Servable files. Fix: use `CancellationToken.None` once the new file is Servable, in the rate-card import too.
     - **A4 (F1):** the rule-set read shows `sprk_notconvertedlist` without checking the marker (`RuleSetService.cs:496-508`; contract `ruleSetApi.ts:80`). Fix: return null unless `sprk_notconverted` is Yes.
     - **A5 (F1):** the replace's prior-row read is capped at 100 with no status filter (`StoredFileService.cs:900`). Fix: filter on Servable, or refuse when the read could be incomplete.
     - **A6 (F1):** `Deploy-EbillingFormsViews.ps1:122` still binds the stale `sprk_ruleset.sprk_notconverted` section. Fix: drop it from the spec.
     - **Known limits:** K1, a chunked upload over 30 MB gets 400 instead of 413 (cheap to map, so fix it); K2, a publish during the job's write phase (not traced); K3, a second rate-card import removes the first card's workbook (record in the notes).
     - **Clean:** authorization order and the 404s on both routes, job keys, the clean-up scopes, the ledgers, the removal script, the absence of retired-route callers, and DI.
     - **Fix round 1 RUNNING** (Opus, from `68aea7af88`) covers:
       - A1–A6, F4/K1, the 422-vs-409 order (K4) and §11;
       - A2 per EB-D-214: clear the drafted state first, then stage the file; the job re-checks its source; no duplicate drafts on a redraft;
       - uac-r2's NFR-01 route order on BOTH routes: 404 for missing, foreign or unreadable, then 403 without Write on the version, then 409, then 413. The draft route adds the caller's Write on the version, and the POML acceptance criteria are refined to match.
     - **Then:** a scoped Opus re-check (the auth order changed), then uac-r2 reviews the routes plus the fix diff together.
   - **Then:**
     - one fix agent for any F-class findings from both;
     - uac-r2 reviews the two routes (`POST .../procedures/{p}/versions/{v}/draft`, `PUT .../versions/{v}/file`);
     - clear the `RecordOwnershipResolver.cs:1809` citation entry with uac-r2;
     - close out the notes and TASK-INDEX.
   - **Dev apply of 080's script: DONE 2026-11-10.**
     - The owner's go was typed in this session; notices went to uac-r2 and ontology-r1 before and after.
     - The run was `861008c591`, exit 0. -WhatIf and the real run matched: 3 created (`sprk_notconvertedlist` plus 2 field permissions), 2 changed (the `sprk_notconverted` Description; securing the new column), 86 skipped.
     - Verify: the column is secured, reader read-only, writer read/create/update, and no other writer profile.
     - Masked window 28.3 s; there was one 429 retry (a concurrent publish).
     - **Still to do:** record the run in the 116 notes at close-out.
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
| `task/ebilling-116` | `68aea7af88` | parts 2 and 3 done and merged; verify, code-review, adr-check and the adversarial pass running |
| `task/ebilling-116-ui` | `0c30c25f71` | merged into 116 |

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
