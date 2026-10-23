# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. "Multi-customer add-in (Model 1)" and "Guest sign-in, PCFs and telemetry" blocks). History: git log + `notes/handoff-history/` (do not load on recovery; the pre-2026-10-21 state, including the Console run log, is archived in `notes/handoff-history/2026-10.md`).

> **Last Updated**: 2026-10-21 (by context-handoff): task 130 signed off, auto-merge pending CI; eBilling invoice flow decided.

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **132 ✅ merged 2026-10-23 as `cdb149cc2d`** (#2304; add-in auto-deployed on that push). **132b 🔄 (follow-up, UAC-r2 deploy gate):** UAC HOLDS the dev BFF deploy of cdb149cc2d for F2 (resolve-email-identity answers an unreadable match with 403 ≠ not_saved — an existence signal now covering all captured mail; fix: in `DocumentEmailIdentityFilter`, a 403 from the inner auth/narrowing step → the same 200 not_saved, this route only) and F3 (tests: unreadable match via communication; flagged-external caller via internal-only communication; both answered like no match, no communication read / no probe). Implementer agent (sonnet/high) running in **C:\wt132b** (branch `work/spaarkeai-word-add-in-r1-132b` off cdb149cc2d). R3 told to HOLD cdb149cc2d (0317579272 still OK). K1 → ISS-023 #2306, K3 → ISS-024 #2307 filed; K2 accepted by UAC. Public repo: describe F2 at rule level only. **Next:** review the agent's commit → push → PR (auto-merge) → send UAC the PR for a diff-only re-check → on merge send R3 the master SHA → after deploy live-check the owner's example (communication 4cded5d5 → archive 2e37d3db → card CMRCL-464674; new save stores the real sent date) → remove C:\wt132b (Remove-WorktreeSafe) → report to the owner. Previously: **132 PR #2304 open, auto-merge on** (owner go 2026-10-22: "yes fix the 'already in spaarke' issues" + "yes fix the 'send date' error issue (we do not need to fix the dev test data)"). Branch `work/spaarkeai-word-add-in-r1-132` (C:\wt132; C:\wt132m = master size baseline, remove both with Remove-WorktreeSafe after merge). Review done: one adversarial pass + 2 fix rounds (F1 withheld card on 2 paths, F2 non-caller cancellation, F4 Graph sent date always read). Size +0.002 MB (192/192 files), no CVE. **Next:** on merge → mark POML 132 completed + TASK-INDEX ✅ + drift check; send R3 the master SHA for the dev BFF deploy (coordinate with ontology #2303: second to merge merges master first); after deploy, live-check the owner's email (communication 4cded5d5 → archive 2e37d3db → CMRCL-464674 card). Invoice removal waits on ebilling (owner confirmed). Open: 042 (UAT), 125, 090. |
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
