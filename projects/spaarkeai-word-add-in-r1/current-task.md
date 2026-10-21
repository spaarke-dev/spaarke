# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. "Multi-customer add-in (Model 1)" and "Guest sign-in, PCFs and telemetry" blocks). History: git log + `notes/handoff-history/` (do not load on recovery; the pre-2026-10-21 state, including the Console run log, is archived in `notes/handoff-history/2026-10.md`).

> **Last Updated**: 2026-10-21 (by context-handoff): task 130 signed off, auto-merge pending CI; eBilling invoice flow decided.

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **130 ✅** merged 2026-10-21 16:11Z (#2177, `4e36c1929d`). **131 🔄** (FULL, sonnet/high): #2182, now based on master. |
| **Status** | #2182 retargeted to master. Master merged into `work/spaarkeai-word-add-in-r1-131` (C:\wt131, `0c6f90d97a`, clean; not yet pushed while the guard and email-filing tests run). |
| **Next Action** | 1. When the C:\wt131 tests pass: push the 131 branch, `gh pr ready 2182`, `gh pr merge 2182 --auto --merge` (131's gates are done; it merges under the owner's 2026-10-21 sign-off).<br>2. After #2182 merges: Step 10 for 131 (POML, TASK-INDEX ✅, drift check), merge master into the work branch, then remove C:\wt131 with `pwsh -NoProfile -Command "& ./scripts/worktree/Remove-WorktreeSafe.ps1 -Path 'C:\wt131'"`.<br>3. Dev deploy of 130+131: only on the owner's go, coordinated with R3; the build must contain 077. |

## Task 130 (#2126 / R3 ISS-015 / UAC D7 T8): what is in #2177
- Every Office email-derived `sprk_document` (the .eml, each attachment child, a single attachment, the worker fallback) is created linked to its `sprk_communication`, or Restricted. Restricted carries `sprk_accessinheritance` = `AccessInheritance.OwnNarrowing(Restricted)` in the same create (UAC 189); the stager refuses a value without its record.
- Guards: `DocumentCreateStagesCommunicationFieldsGuardTests` (stager call; outcome at all 3 sites; only `ApplyEmailCommunicationOutcome` sets the record, compound assignment included); 189's `DocumentAccessPermissionWriterGuardTests` (the stager listed; UNSEEN-alias check; Key widened). Write-path registry I-19.
- Gates: full Office/ChildAccessPermission filter 749/0/7; ArchTests 1225/1225 (`65b4f5f468`); the pass 1/2 fixes re-run on their scope.
- **Deploy dependency:** a BFF with T8 REQUIRES `sprk_document.sprk_accessinheritance` (`Set-DocumentAccessPermissionSchema.ps1`, guide §7.4). It is applied in dev; prod needs it before any BFF with 189 or T8. A deploy needs the owner's go, coordinated with R3, and the build must contain 077.

## Live in dev
- **BFF `spaarke-bff-dev`: master `1.0.0+a10e814d02`** since 2026-10-21 14:35:33Z (deployed by R3; contains UAC #2169/189, #2150, #2184, and 077; healthz 200). Our task 130 is NOT in it.
- **Console `sprk_spaarkeai`:** `deploy-spaarke-ai.yml` auto-publishes on every master push touching SpaarkeAi or the shared UI packages; read `modifiedon` to know which build is live. Default landing tab is Worklist (ontology data change 2026-10-17; Daily Briefing is in the Workspaces dropdown).
- The add-ins are current (`05356cbf7`).

## eBilling (ebilling-r1, owner-requested review): decisions recorded in `notes/ebilling-r1-review-2026-10-21.md`
- Decided and sent: K5 race fixed by A + B; internal-only, Restricted and Private-thread sources allowed with isolation carried. The Outlook invoice flow:
  - an explicit Invoice mark;
  - corrections are resubmissions, refused after Approved/Sent to AP/Paid;
  - a separate Invoice task-pane view with a "Submit Invoice" ribbon button;
  - Invoice removed from Save and quick-create;
  - Matter/Project/Work Assignment (Agreement in ebilling R2);
  - the .eml always saved to the core record;
  - no LEDES.
- ebilling's `POST /api/finance/invoice-submissions/from-email` contract includes our four changes (their model v5.5 §9.1, `8c65b3bd89`):
  - step 0 refuses with 422 when a marked file isn't in the request;
  - the .eml id comes from the job's artifact, and filing step A runs through the refile path;
  - the known limits are recorded;
  - our key and removal order are adopted.
  Not built; it waits on the owner finalizing ebilling's model.
- **Future work for this project (no task yet):** the Invoice view and ribbon button, removing Invoice from Save and quick-create (the add-in side goes first, or in the same release as ebilling's server-side removal), the email-only .eml save. Create a task when ebilling's contract is final.

## Filed 2026-10-21
- **ISS-022 / #2209:** the `/office/save` re-save gap (the email save key excludes the attachment selection, so re-saving to add attachments is answered "already saved"). Filed on the owner's go; next-round, no task yet.
- **ebilling v5.7 (`ef9b1a9e9b`)** took our fix for finding the .eml, and the owner chose F1-a(i) and F1-c. Both are recorded in `notes/ebilling-r1-review-2026-10-21.md`: they shape the future Invoice view (show a retry; suggest internal upload). No reply owed.

## Waiting on the owner (live checks)
- UAT: 042 (full); 080 (Test User 1 opens a matter-filed document they saved, then Index and document To Do); 121 (email save: Message-ID, link to an existing communication, an unfiled communication gets filed, "Related to" cards); 127 (grids: Matter Documents footer `v1.1.85`, no `[AppInsightsService]` warning, VisualHost, App Insights `customEvents`, Daily Briefing); 078 (one Word-on-the-web run plus the desktop Word build number). Fix what they report.
- PCF re-sync: MatterHeader 1.0.22; RecordHeader 1.1.12; Communication form timeline and actions; the document relationship viewer opens.
- A9: a licensed B2B guest signs in from their own company's desktop Office and Word.
- SPA-r3 waits on the owner's Ralph Dewey sign-in retest; pass the result on.
- Then 090 wrap-up (`/test-diet`), after UAT.

## Blocked / waiting on others
- SPA-r3 (A9): workforce-contact sign-in tasks not built.
- customer-provisioning-orchestration-r1: T186 awaits the owner's OK.

## Open owner items / later
- Main checkout `C:\code_files\spaarke` refused a fast-forward earlier (untracked `tests/Spaarke.ArchTests/WebResourceMirrorParity.baseline.txt`, 4 modified tracked files, possibly another session's work). Left untouched for the owner.
- Optional: reassign the 3 pre-task-146 app-owned dev events (#1034 comment). A Dataverse write; needs the owner's go.
- Filed follow-ups (other owners): #1464, #1468–#1471, #1476, #1477, #1505, #1540, #1551, #1591, #1832, #2172.
- C:\wtqa1906 is left for the owner.
