# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-10 (context-handoff, desktop session `field-mapping-r1`). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030, 034, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a, 070–075. Latest: #2629 (57748ebf6d).
> **W3 direction:** named exceptions plus guards, no core redesign in R1.

## Quick Recovery

| Field | Value |
|---|---|
| **Session** | Desktop session **`field-mapping-r1`**, worktree `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`. |
| **Work branch** | `work/field-mapping-server-write-path-r1`. Local HEAD is **4605e234c8**, the merge of `task/fm-054a-5`; **not pushed yet**. A verification run of the merge (BFF and test builds, a filtered BFF run excluding `Api.Office.`, ArchTests DirectMessageRecipientDiWiring + RegardingPair + GenericUpdatePathsPreparePair) was started in the background. Push only after it shows 0 failed. Merged since #2629, not yet on master: 076c, 060, 045a, 021, 045c, 032b-1, 032b-1b (+ C1), 045b, the guard-1 fix, the pair-type bind fix, **022** (10655f4dfe), **054a piece 2** (4605e234c8), docs. |
| **Active task** | **054a.** Piece 2 (the `sprk_directrecipient` column + BFF stamp) is MERGED. The column is live on spaarkedev1 (`scripts/Set-DirectRecipientSchema.ps1`; navigation `sprk_DirectRecipient`, read `_sprk_directrecipient_value`). The stamp is in `Services/Communication/Access/DirectMessageRecipient.cs`. It is written in the update that files a message into a Direct thread (thread lookup + recipient + sender if missing), with its own strict, Read-bit participant read (a deliberate duplicate; it converges after 198, DI-025 #2652). Exactly 2 distinct, enabled, non-application systemusers, one of them the sender; otherwise no stamp (fail closed). Opus adversarial pass done → fix round 1 → C1 DI wiring test (`tests/Spaarke.ArchTests/DirectMessageRecipientDiWiringTests.cs`). UAC-r2 APPROVED. |
| **OPEN OWNER DECISION (blocks piece 3)** | **Resolver-CREATE gap:** in today's shipped flows NO Direct message gets a recipient. A UI send without a `ThreadId` creates a fresh resolver thread, owned by the app user and shared with no one (`ThreadResolver.cs` ~:181-190; `CommunicationService.cs` ~:938; `MessagingChannelSender.cs` ~:72-84). `startDirectThread` has no caller, and inbound never joins a pair thread. Options asked of the owner: (a) **route outbound Direct CREATE through `FindOrCreateDirectThreadAsync(sender, recipient)`**, recipient = the request's single internal addressee resolved on the server (recommended; UAC-r2 reviews; conditions: ids only, exactly one internal, enabled, non-application user, else refuse; inbound stays fail-closed); or (b) defer and stay fail-closed. Piece 2 is inert until then. |
| **Next action** | 1. When the verification run passes: `git push origin work/field-mapping-server-write-path-r1`. 2. Get the owner's answer on the routing decision; if (a), brief an implementer (FULL rigor, adversarial pass, UAC-r2 review). 3. 054b: field security for `sprk_sentby`, `sprk_directrecipient`, `sprk_communicationthread` and the thread flags (live step, owner go), plus guards or tests for the run-as-user writers listed in the 054 notes. 4. 022b. 5. The next PR to master (row below). 6. Clean up agent worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*` from this session (a60710f842ad3a380, a892fa0f61f363b8a, a7bc7a1ae62bf9182, a7ccd21116f208fba, a29908ac16736e9d9, a3b16bcb87c4c3752) with `scripts/worktree/Remove-WorktreeSafe.ps1`. |
| **Next PR to master** | The bundle above. Before it: `scripts/verify-suites.sh`; `scripts/publish-delta.ps1` against a fresh master; send UAC-r2 the combined diff; open the PR from a snapshot branch `work/field-mapping-server-write-path-r1-<tag>` with auto-merge. PR text: ADR-008 path A; 032b-1's push side effect; Office Assigned To targeting; 022's create strip + push gate; the one-line ontology-owned change in `DecisionActionExecutors.cs`; 054a piece 2 (inert until the routing decision). Whoever merges second of 022 and UAC-r2's 198 runs merge-tree on `SecureChildShareSynchronizer.cs` and rebuilds. |

## Binding deploy order (agreed with UAC-r2 2026-11-09/10; each live step needs the owner's go in its own session)

1. 054a piece-2 BFF stamp.
2. 054b field security live.
3. 054c: dry run, owner go, apply (backfill only where the thread has exactly 2 enabled users, one of them `sprk_sentby`; every other row goes to a manual-review list).
4. 198 deploy.

Also: 198 Part A (the Direct role) before 054a piece 3, and piece 3 never in an environment without the 198 job. **Assign invariant:** a message moves to the Direct team only with a valid stamped pair; otherwise its owner is unchanged (piece 3 and the unfile path, with must-fire tests). Details: `notes/task-054-notes.md`.

## Known limits carried

- **022:** K2 (the isolation owner is the single Secure team) and K3 (the Project wizard's follow-on Event/WA creates map in the browser; tasks 023/024). K1 and K4 were resolved in round 6; K4 is filed as DI-024 #2648.
- **054a:** the thread's owner controls which pair the thread yields (L3); 054b's run-as-user writer list (`ChildRecordEndpoints.cs` ~:817, `OwnedChildWrite.cs` ~:1559, `DataverseCreateRecordHandler.cs` ~:489, `EmailDraftToolHandler.cs` ~:478, `InquiryDispositionService.cs` ~:485).

## Other lanes

- **054a piece 1** (the Direct team lookup and settings): FINAL on `task/fm-054a-2` (ac6f1743b3), merged by UAC-r2 into 198, NOT on our work branch. **Piece 3** (the owner move): built against UAC-r2's contract in the 054 notes; blocked on the routing decision.
- **054b / 054c:** live runs need the owner's go.
- **022b:** Restricted mapping source; document access permission set in the create. UAC-r2 reviews.
- **045d:** Q8 (invoice family re-derive); proposal in `notes/task-045c-notes.md`.
- **052b:** scheduled repair job, contact owner rule, chat contact create.
- **032b-2/032b-3:** on the current `work/ebilling-r1` tip; two adversarial passes; the diff goes to ebilling-r1.
- **021:** deploy by UAC-r2 with the owner's go after master. **076:** re-run check (c) after UAC-r2 deploys 076c.
- **Later:** 023–029, 035, 036, 050/051, 053, 061–063, 090.

## Open owner items

- **The routing decision (above).**
- sync-mode -Apply on dev; deleting snapshot branches `…-049c` and `…-030b`; the 054b and 054c live runs.

## Coordination

- Peers: `uac-r2` (desktop), `provisioning-r1`, `ontology-r1`, word-add-in, ebilling-r1. Access-defect details go to uac-r2 by message only. UAC-r2 task 198 (`task/uac-198-direct-share-side`) is the share side of 054. It fixes the zero-mask and soft-read defects in `DirectThreadAccessService.cs` (their file; we don't edit it).
- Nightly quality alerts open on master: #1906 (client-tests), #2060 (full-unit-tests). The `Api.Office.` contract classes crash the test host when run together, on base too; exclude them from combined runs.
