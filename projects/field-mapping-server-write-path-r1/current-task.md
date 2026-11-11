# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-10 (context-handoff, desktop session `field-mapping-r1`). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030, 034, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a, 070–075, 076c, 060, 045a, 021, 045c, 032b-1, 032b-1b, 045b, 022, 054a piece 2, 054a piece 2b. Latest: #2675 (56d3463c1a, 2026-11-10/11). UAC-r2's 198 is on master too (#2670, c71414c7a0). Nothing deployed.
> **W3 direction:** named exceptions plus guards, no core redesign in R1.

## Quick Recovery

| Field | Value |
|---|---|
| **Session** | Desktop session **`field-mapping-r1`**, worktree `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`. |
| **Work branch** | `work/field-mapping-server-write-path-r1`, pushed at **43f4f12525** (2b + master c71414c7a0). Everything on it is on master via #2675 (merge commit 56d3463c1a). Master's `tier2-master/full-unit-tests` alert #2060: compare against its shard log before a master PR, never re-run unfiltered. |
| **Active task** | **054a.** Piece 2 (the `sprk_directrecipient` column + BFF stamp) is MERGED. The column is live on spaarkedev1 (`scripts/Set-DirectRecipientSchema.ps1`; navigation `sprk_DirectRecipient`, read `_sprk_directrecipient_value`). The stamp is in `Services/Communication/Access/DirectMessageRecipient.cs`. It is written in the update that files a message into a Direct thread (thread lookup + recipient + sender if missing), with its own strict, Read-bit participant read (a deliberate duplicate; it converges after 198, DI-025 #2652). Exactly 2 distinct, enabled, non-application systemusers, one of them the sender; otherwise no stamp (fail closed). Opus adversarial pass done → fix round 1 → C1 DI wiring test (`tests/Spaarke.ArchTests/DirectMessageRecipientDiWiringTests.cs`). UAC-r2 APPROVED. |
| **Piece 2b (MERGED to master)** | **#2675 merged 56d3463c1a** (owner go; CI 41 pass / 0 fail, Codex no findings; UAC-r2 told). Verified at 43f4f12525 (master c71414c7a0 merged): sln 0 warnings, ArchTests 2145/2145, verify-suites 7450 / 0 failed, Communication 2712 / 0 failed, publish +12,870 B (39.39 MB). UAC-r2's **198b** (`task/uac-198b-direct-find-existing`, reviewed + APPROVED by us at 554c1bfb6c; their follow-up commit = Debug log + §7.9 cost note) calls our shape rule from FindExistingAsync; they open its PR now. **Owner chose (a) on 2026-11-10** (`notes/decisions.md`). Merged `--no-ff` as **a215006537** (task branch `task/fm-054a-7`: a5cacab398 feature, 82da70c50d fix round 1, 6ea305809e fix round 2, 0ebd43f6a5 test assertion, 9d2fdfbeab F-2b-1). An outbound Direct send whose To/Cc/Bcc normalize to exactly one internal, enabled, non-application, non-self systemuser goes through `FindOrCreateDirectThreadAsync`. It is joined only if `DirectMessageRecipient.ReadStrictPairShapeAsync` finds a strict pair of exactly {sender, recipient}: Direct, no typed regarding, Open, and ONE share row (a systemuser with Read), else `extra_principal`. The routed join re-decides from its own thread read before any write; a refusal or a read fault before the write falls back to today's resolver path once (5412). Two adversarial passes (Opus + security Opus) plus a Sonnet re-check. UAC-r2 APPROVED a9794dbaad..0ebd43f6a5 with F-2b-1 (fixed in 9d2fdfbeab). Verified at a215006537: sln 0 warnings, ArchTests 2128/2128, verify-suites 7388 passed / 0 failed, Communication filter 2535 / 0 failed, publish delta +9,349 B (39.33 MB, 191 = 191 files) vs c9a62c4a62, no vulnerable packages. Limits L6–L10 in `notes/task-054-notes.md`; L7 + L10 are fixed by UAC-r2's 198b (its FindExistingAsync calls our shape rule). **Deploy check (owner go in THIS session):** one real send on dev confirming a new FindOrCreate thread shows exactly one share row; otherwise routing never fires (fail-safe). Tell UAC-r2 when 2b is on master. |
| **Previous master PRs** | #2659 (534a0e36b4) and #2675 (56d3463c1a), both merge commits with the owner's go. Narrative: their commit messages and PR bodies. |
| **Next action** | 1. (Done: #2659 and #2675 merged; 198b reviewed.) 2. Review 198b's final diff if UAC-r2 sends changes. Clean up agent worktree adb785206289cba75 too. 3. 054b: field security for `sprk_sentby`, `sprk_directrecipient`, `sprk_communicationthread` and the thread flags (live step, owner go), plus guards or tests for the run-as-user writers listed in the 054 notes. 4. 022b. 5. The next PR to master (row below). 6. Clean up agent worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*` from this session (a60710f842ad3a380, a892fa0f61f363b8a, a7bc7a1ae62bf9182, a7ccd21116f208fba, a29908ac16736e9d9, a3b16bcb87c4c3752, afafde0bff35cd782) with `scripts/worktree/Remove-WorktreeSafe.ps1`. |
| **Next PR to master** | None open. The next bundle (054b guards, 022b, …) follows the same steps: `scripts/verify-suites.sh` + Communication filter, `scripts/publish-delta.ps1` vs a fresh master, UAC-r2 review of access-relevant diffs, snapshot branch `work/field-mapping-server-write-path-r1-<tag>`, Auto-fix on, merge only on the owner's go. **Deploy prerequisite (from #2659/#2675):** `scripts/Set-DirectRecipientSchema.ps1 -Apply` in an environment BEFORE a BFF with piece 2 deploys there (only spaarkedev1 has the column). |

## Binding deploy order (agreed with UAC-r2 2026-11-09/10; each live step needs the owner's go in its own session)

1. 054a piece-2 BFF stamp, together with piece 2b (outbound Direct pair routing), ahead of the 198 deploy (UAC-r2, 2026-11-10).
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

- (Decided 2026-11-10: route via the pair thread; piece 2b.)
- sync-mode -Apply on dev; deleting snapshot branches `…-049c` and `…-030b`; the 054b and 054c live runs.

## Coordination

- Peers: `uac-r2` (desktop), `provisioning-r1`, `ontology-r1`, word-add-in, ebilling-r1 (now the Desktop session `ebilling-r1`, since 2026-11-10). Access-defect details go to uac-r2 by message only. UAC-r2 task 198 (`task/uac-198-direct-share-side`) is the share side of 054. It fixes the zero-mask and soft-read defects in `DirectThreadAccessService.cs` (their file; we don't edit it).
- Nightly quality alerts open on master: #1906 (client-tests), #2060 (full-unit-tests). The `Api.Office.` contract classes crash the test host when run together, on base too; exclude them from combined runs.
