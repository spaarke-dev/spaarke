# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. "Multi-customer add-in (Model 1)" and "Guest sign-in, PCFs and telemetry" blocks). History: git log + `notes/handoff-history/` (do not load on recovery; the pre-2026-10-21 state, including the Console run log, is archived in `notes/handoff-history/2026-10.md`).

> **Last Updated**: 2026-10-30 (context-handoff at 85%): dev BFF candidate c91b99e575 is blocked by #2468; UAC's fix is PR #2471 (draft), and our check is in progress.

## 🔥 ACTIVE NOW (2026-10-29)

- **#2412 (ISS-027) and #2416 (ISS-028): CLOSED 2026-10-29.** The owner re-test passed on dev in Outlook web: a typed name produced `2026-10-08_New Email File Name 10-29-2026.eml` filed to PAT-935426, and the subject was kept. Retest comments are on both issues. Known limit: #2421.
- **Outlook DESKTOP sign-in: "Bad Request - Request Too Long" (HTTP 400, request headers too long) on login.microsoftonline.com** in the sign-in popup. Outlook web is fine.
  - NOT the add-in and NOT the Office cache: clearing Wef did not help, and Edge also gets the 400 on admin.microsoft.com and portal.azure.com.
  - Machine: Entra-joined to Helios IP, plus 3 registered work accounts (RightHub Groups, Spaarke Inc., Dewey Cheatham & Howe PC). Likely cause: the per-account Windows SSO cookies Edge and WebView2 send to login.microsoftonline.com are too large in total.
  - Owner steps: an InPrivate test, clear Edge's login.microsoftonline.com site data, then disconnect an unneeded work account (never Helios IP: that is the device join). No code change; nothing to file.
- **Dev BFF candidate c91b99e575 (R3): BLOCKED by us, #2468.** UAC 194b (2cd0f62e2c; not in dev's 136568cf70) makes unfiled Office .eml and attachment documents owned by the ROOT BU team. The communication is now the primary ownership parent, and an unfiled one is root-owned. Basic and Core User read sprk_document at Parent:Child depth (4), so a child-BU saver loses access to their own unfiled save. Verified on dev (read-only).
  - The rest is fine for the add-in: ebilling's /office/save changes are inert while it is off, and #2412/#2416 are intact.
  - R3 told (the owner decides: hold for UAC's fix, which we recommend, or deploy accepting it). UAC told at session spaarke-wt-unified-access-control-r2-db (the old pipe is stale).
  - BFF tests at c91b99e575 (Office, idempotency, association, upload, incoming, EntityAccessFilter): 1203 passed, 7 skipped, 1 failed. The failure is R3's ProvisionProjectIdempotencyTests timeout test, which passes 55/55 alone (timing under load). Reported to R3. C:\wt2464 removed. **UAC confirmed #2468 was unintended** (task 080's rule stands) and is fixing it on fix/uac-r2-2468-office-unfiled-owner, with a real-resolver test and a reconciler test. They are also assessing OFFICE_022 and OFFICE_014. Design A3 (refined by UAC): an UNFILED communication counts only for Secure and the saver's team owns the document; a FILED communication means the document follows its filing team (the family rule, accepted as known behavior); communication ownership is unchanged. Our condition: decide "unfiled" from the filing, not the owner kind, and add a test for a filed communication that is root-team-owned (a root-BU matter). **UAC PR #2471** (fix/uac-r2-2468-office-unfiled-owner @ 810a1bb2a3; draft while 2 UAC security reviews run). I read the resolver diff: filed-ness comes from the communication's own ownership-parent lookups or its thread's filing, never the owner kind; when unfiled, the communication moves to SecureOnlyParents. 14 tests, including our cases (1) and (2) and reconciler parity. CI was pending at the check. UAC's lower items: (a) OFFICE_022 leaving a communication behind gets FILED, not fixed here; (b) the 502 OFFICE_014 on a lookup throw is deliberate (fail closed). **OK sent to UAC** (2026-10-30). The note is resolved: only a MISSING thread refuses (OFFICE_022); a transient fault propagates as the retryable 502 OFFICE_014. UAC is adding a PR line and filing the OFFICE_022 leftover. **EXACT NEXT ACTION:** after #2471 merges, re-check R3's new candidate SHA (it must contain #2471) and send R3 the OK.
- Offer to correct the owner's 10-28 test record (comm 12f1f3f8…, doc 536204ff…) only if they ask.
- **ebilling 094 client: #2448 MERGED** at 5119519679 (2026-10-29 14:48Z; inert, `?action=submitInvoice` only). C:\wt133d removed.
  - Every 094 row in §6 and §9 has a sentence. `filingKept`: the top-level field first, then the pinned sentence. Code = `errorCode ?? reasonCode`, plus `extensions.code` on a 403 only.
  - ebilling's contract fix is on task/ebill-095-errorcode @ d7218072fb: step A's 403 gets a top-level `errorCode`; `filingKept: true` goes on every refusal after step A filed (including reasonCode-only answers and Office pass-throughs); sweep list received. I checked their contract diff: it matches #2448. ebilling 095 is ON MASTER: #1911 merged at eaa0d44f7f (verified; the contract is unchanged since 842110e6ab, the version we OK'd). Not live on dev yet. It needs an SPA-r3 dev BFF deploy from this master, then `Ebilling:Enabled=true` on dev. ebilling reports the owner OK'd this in their session (EB-D-173, 2026-10-29; reconcile job stays off). They set it after the SPA-r3 deploy passes uac's 30-minute check. ebilling owns that setting, not us. Then ebilling sends the dev notice, and slice C follows with the owner's go.
- Cleanup later (Remove-WorktreeSafe): C:\wt2412, C:\wt2412m, C:\wt2416, C:\wt2416c.

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **133 🔄 Outlook Submit Invoice view** (FULL, opus/high). Slices A+B MERGED: server #2357 (68048b83d5, Work Assignment in office entity search) and client #2359 (4ab7c1a9c1, inert: `?action=submitInvoice` only; the ribbon button appears only on an owner manifest redeploy). All 133 worktrees removed. Dev BFF = e56a3a51d4 (R3, live 2026-10-28 14:11:58Z; contact schema -Verify passed first; rollback de38296499): #2357 is LIVE, so C0 is met (Work Assignment in office entity search on dev). Slice C's manifest redeploy now waits only on from-email working on dev + owner go. ebilling 2026-10-27: 095 built (task/ebill-095-from-email 96681b8825, not merged; submit path 503 until 094); new Failed code `ebilling.submission.file_scan_timeout` (092) OK'd by us; its client message merged in #2361 (90f3cab765; contract f1ae28290e); the no_message_id 400 is a code-less ValidationProblem, which our generic 400 sentence covers; OP-5 decided (EB-D-165, 2026-10-28): another user's Failed attempt allows a new attempt; otherwise 409 document_already_submitted with no reason field (by design). Our neutral 409 sentence merged in #2365 (083b7c9727). 095 review round 2026-10-28 (contract at cc7bb5eeee, task/ebill-095-from-email: K v2 without firm fields; 422 attachment_not_preserved; 500 OFFICE_AUTH_ERROR; firms 404; poNumber ≤100) agreed; client follow-up merged #2384 (b099bc4e8f). ebilling's next step: 094 seam (route stops answering 503). WAITING: supporting files (after ebilling 123 is built), Correction (after ebilling 097), slice C (from-email works on dev ~11-20 + owner go; order: BFF with #2357 live -> owner manifest redeploy -> remove Invoice from Save/+New in both hosts, tell the owner Word loses Invoice Save -> tell ebilling for 096). Owner Outlook checks PASSED 2026-10-28 (already-in-Spaarke card on the Form D email; new save stores sprk_emaildate 2026-10-16T02:52:53Z = Outlook's 10/15 10:52 PM EDT; To Do, Find, Send also OK). Found in passing: ISS-027 / #2412 (save-pane matter lost to a body tracking token → communication Ambiguous, no regarding); owner to decide whether to fix now (email-intelligence engine). Office dev-cert files exist in %USERPROFILE%\.office-addin-dev-certs from a review build (CA NOT installed). |
| **Status** | 130 and 131 are both live in dev. BFF `583f90c1d9` (R3, 18:04:37Z) contains 733c65063c, 4e36c1929d and 077. The add-in auto-deployed on the #2182 merge push (`deploy-office-addins.yml`, 16:50:34Z, success). The notice text is in the hosted `150.bundle.js`, which both the Outlook taskpane and commands (Quick Save) load. No manual add-in deploy is needed. |
| **Next Action** | 0. **Dev BFF is now `1fa0cf5cb8`** (2026-10-23 00:00:36Z, R3, on our OK; our healthz check: 200). It is `f5bf481185` plus field-mapping #2282.
   - The save path is untouched. The Office change is field-mapping 018b/018c: the Matter/Project quick-create now runs on RecordCreationService.
   - ISS-001 only widens the Copilot audience; the add-in's workforce audiences are unchanged.
   - Quick-create problems found in UAT go to field-mapping first.<br>1. When R3 sends the times or subjects of the owner's two .eml saves, read the sprk_document rows back and report them to UAC and R3.<br>2. Owner live checks (below), then 090. |

## Task 130 (#2126 / R3 ISS-015 / UAC D7 T8): what is in #2177
- Every Office email-derived `sprk_document` (the .eml, each attachment child, a single attachment, the worker fallback) is created linked to its `sprk_communication`, or Restricted. Restricted carries `sprk_accessinheritance` = `AccessInheritance.OwnNarrowing(Restricted)` in the same create (UAC 189); the stager refuses a value without its record.
- Guards: `DocumentCreateStagesCommunicationFieldsGuardTests` (stager call; outcome at all 3 sites; only `ApplyEmailCommunicationOutcome` sets the record, compound assignment included); 189's `DocumentAccessPermissionWriterGuardTests` (the stager listed; UNSEEN-alias check; Key widened). Write-path registry I-19.
- Gates: full Office/ChildAccessPermission filter 749/0/7; ArchTests 1225/1225 (`65b4f5f468`); the pass 1/2 fixes re-run on their scope.
- **Deploy dependency:** a BFF with T8 REQUIRES `sprk_document.sprk_accessinheritance` (`Set-DocumentAccessPermissionSchema.ps1`, guide §7.4). It is applied in dev; prod needs it before any BFF with 189 or T8. A deploy needs the owner's go, coordinated with R3, and the build must contain 077.

## Live in dev
- **BFF `spaarke-bff-dev`: master `1.0.0+1283c41817`** since 2026-10-22 12:50:18Z (deployed by R3 on our OK; healthz 200). It adds to `a7b956fb55`:
  - UAC #2234 (round 122, the provisional own-Restricted release; our save line);
  - #2239 / #2237 (the AI profile writer is limited to the ACT-011 columns);
  - #2244 (the generic writers refuse the BFF-owned document columns);
  - #2249 (and the owner and creator columns);
  - #2248 (the on-demand archive verifies each attachment's Graph item, #2246);
  - field-mapping 018a (dormant).

  None of these changes Office save files.
- Before it: `a7b956fb55` from 02:18:52Z, which added to `f1b3dadcf5`:
  - field-mapping #2223 and #2228;
  - UAC #2221 / task 190, the .eml-to-communication link sweep, registered disabled with Apply off.

  **Owner (UAC round 122): "the access should match the 'parent'".** A task-130 own-Restricted .eml that is later linked to exactly one communication (by the 190 sweep or CrossPathLink) follows that communication. A Restricted set by hand still stays (round 87). UAC builds it as a task-190 follow-up and sends us the design before touching CrossPathLink.

  **UAC design** (`projects/unified-access-control-r2/notes/task-190-round122-linked-eml-access.md`, `6c980f0da3`): an `OwnProvisional` marker in the AccessInheritance JSON, set only through `AccessInheritance.OwnNarrowingUntilEmailIdentified` in `ApplyEmailCommunicationOutcome`, and released in `OwnPermissionOf` by the D7 walk.
  - **We reviewed it 2026-10-22: no objection.** UAC builds all of it in one PR (including our one line); we review before merge. **UAC accepted all five points** (no record version bump: an older BFF ignores the field and keeps own = Restricted). **Draft PR #2234** (head `d5448eddd4`) reviewed 2026-10-22. Our scope is approved: the factory swap at `OfficeDocumentPersistence.cs:144`, our test helpers asserting the marker, and I-19.
  - **F1 sent:** a released attachment child can end up wider than its person-Restricted .eml, because the release falls back to the TOPS floor (`ParentLineage.cs:525-538`), which ignores an intermediate document's own narrowing. Suggested fix: no release while any document on the walk carries a non-provisional own narrowing, plus a must-not-fire test.
  - **K sent:** confirming the same value can't make a provisional Restricted the person's; state it in I-19.
  - **Fix round 1 (`96ab2be917`) re-checked 2026-10-22: F1 closed; we approved from our side.** The release now walks every node to the link and refuses on any person-set narrowing. It also has an Undecidable state that is carried to the next run, a field-security gate, and a copy constructor that drops the marker. Noted, no change needed: a chain-refused child isn't carried; it stays Restricted until its row changes or a sweep pass reaches it. **Merged to master as `230302c307`** (both UAC passes ACCEPT; fix round 2: "link columns not secured" is a refusal that isn't carried, and the marker is kept only while the own value is the same). The dev deploy is batched with UAC #2237 through R3. Master is merged into our work branch.
  - Our five points:
    1. The release needs the link field security (`Set-DocumentNarrowingFieldSecurity.ps1`) applied first, or a stated window.
    2. Single-attachment saves are never released: a known limit.
    3. Our test helpers must assert `OwnProvisional`.
    4. A guard against forging the marker, and the I-19 row updated.
    5. Parse must fail closed on an unknown higher record version.
  - FYI: 190's server gate passed. The sweep dry run found 29 of 120 rows would link (0 written); Apply is an owner item.

  `f1b3dadcf5` (21:45:37Z) added UAC 188 / D7 T7, field-mapping #2214 and UAC #2186 to `583f90c1d9` (tasks 130 and 131, and 077). UAC is applying, live, the document-link Delete = Restrict (`Set-DocumentLinkDeleteRestrict.ps1`), field security and web resources. After that, deleting a saved .eml that has attachment children is refused (409) until the children are deleted. No Office save path deletes. R3's 30-minute App Insights reading (16:41:30Z–17:12:38Z): **0** stager refusals and no new exception class; the only group is the known Graph "Invalid hostname" noise. UAC's gate (one identified and one unidentifiable .eml save) waits on the owner; when R3 sends times or subjects, read the sprk_document rows back (relatedcommunication, accesspermission, accessinheritance).
- **Console `sprk_spaarkeai`:** `deploy-spaarke-ai.yml` auto-publishes on every master push touching SpaarkeAi or the shared UI packages; read `modifiedon` to know which build is live. Default landing tab is Worklist (ontology data change 2026-10-17; Daily Briefing is in the Workspaces dropdown).
- The add-ins are current (`733c65063c`; `deploy-office-addins.yml` runs on every master push).

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
- **Future work for this project (no task yet):** the Invoice view and ribbon button, removing Invoice from Save and quick-create, the email-only .eml save. All ship in ONE add-in release; ebilling 096 removes the server legs one release later.
  - **Owner decision 2026-10-21: create the task only once ebilling 095 (from-email) is built.** Send ebilling the task number then.
  - **ebilling timeline (2026-10-27, owner asked them to send it):** 095 not started; chain 089 (~4d) → 086 (~1d) → 094 (~3-4d) → 095 (~3-4d), with 092/098 alongside 089 (089, 092, 098 started 10-27). 095 code-complete ~11-13 at the earliest, ~11-20 realistic; on dev after #1911 merges and an R3 deploy. They will update at 094's merge or a slip of more than a few days. We asked them to pin in 095's POML: request DTO names, idempotency-key transport, response (status, ids, how to follow progress), a reasonCode per refusal, duplicate replay. Question to the owner: start our task now against §9.1 with a mock (release still waits on a working dev endpoint)?
  - Gates: 118's shared `InvoiceAttachmentMarking` merged; 095 deployed. The 095 shapes are frozen at v5.9 FINAL.
  - Non-invoice attachments default to Supporting (owner decision 2026-10-21).
  - Production needs a manifest version bump and an M365 Admin Center upload (new ribbon button).

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
