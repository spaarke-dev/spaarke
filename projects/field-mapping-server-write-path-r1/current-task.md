# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-09 (by context-handoff — session moving from the VS Code window to the Claude Code desktop session `field-mapping-r1`). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030, 034, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a, 070–075. Latest: #2629 (57748ebf6d).
> **W3 direction:** named exceptions plus guards, no core redesign in R1.

## Quick Recovery

| Field | Value |
|---|---|
| **Session** | This project now runs in the desktop session **`field-mapping-r1`** (same worktree `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`). Peers were told the new name. |
| **Work branch** | `work/field-mapping-server-write-path-r1`, pushed. Merged since #2629, not yet on master: 076c, 060, 045a, 021, 045c, 032b-1, 032b-1b (+ C1), 045b, guard-1 fix (8988c2499e), pair-type bind spelling fix + guard (8e0b48eadb), docs, POMLs 054 (with the 2026-11-09 option A and thread-lookup additions) and 022b. Builds 0 warnings. |
| **Active task** | **022 round 2**: fix round 5 (from the second adversarial pass) is DONE on `task/fm-022-4`, head **85c70c4431** (4efea58bea pair columns + wording, 82531dc983 create Unknown → 503, a9ec720749 wizard Secure reset in `CreateProjectWizard.tsx`, 85c70c4431 notes). Client 93/93; BFF filtered 4391 pass + 1 unrelated timing test (passes alone 18/18); ArchTests 6/6; 0 warnings. |
| **Next action** | 1. A scoped Sonnet `adversarial-reviewer` re-check of the fix diff only: `git diff 5cd3479650 85c70c4431` plus direct callers and callees. 2. If no F: send UAC-r2 the full 022 diff (`git diff origin/work/field-mapping-server-write-path-r1...origin/task/fm-022-4`) and wait for OK. 3. Merge `--no-ff` `origin/task/fm-022-4` into the work branch, build, push. 4. Prepare the next PR to master (row below). 5. Start 022b. |
| **Next PR to master** | The bundle above + 022. Before it: `scripts/verify-suites.sh`; `scripts/publish-delta.ps1` against a fresh master; send UAC-r2 the combined diff; open the PR from a snapshot branch `work/field-mapping-server-write-path-r1-<tag>` with auto-merge. PR text: ADR-008 path A; 032b-1's push side effect (invoice display copies never pushed); Office Assigned To targeting; 022's create strip + push gate; the one-line ontology-owned change in `DecisionActionExecutors.cs` (ontology reviewed it: OK). |

## 022 fix round 5 (DONE at 85c70c4431 — what the re-check verifies)

1. **F1:** a secure-source push can still change a child's regarding pair through the pair planner, which runs after the gate (`FieldMappingEndpoints.cs` ~:1483, `OwnedChildWrite.cs` ~:717-741 rule 4, party lookups in `CoreAncestorResolver.PartyRegardingColumns`). Fix: treat `OwnedChildWrite.PairAgreementColumns(target)` keys as filing writes in `FieldMappingPushSourceGate.FilingWrites`, compared with `ChangesFiling`. A change skips the child whole (`field_mapping.push.secure_source_refile_not_allowed`) in preview AND apply; an equal value proceeds. Must-fire: a secure matter profile copying the client into `sprk_event.sprk_regardingaccount`, with the pair naming that account.
2. **F1:** client wording in `recordFamilyPushService.ts`: "Some fields…" → "No fields were copied to this record because the source record is secure." / "N records were not updated because the source record is secure." Check the refile and undetermined texts too. jest.
3. **F1:** create, `RecordCreationService.Mapping.cs`: an `Unknown` verdict must give `MappingSourceCheckFailed` (503, retry wording) and a "could not be checked" warning. Only `Secure` gives `record_creation.mapping_source_secure_filing` (400) and the secure wording. Applies in `EnforceMappingSourceOwnerAsync` and at step 3 (~:554).
4. **Wizard:** `CreateProjectStep.tsx`: when a parent change releases the Secure lock, restore Secure to its pre-lock value (off if never set). Keep a user's explicit choice made while unlocked. jest.
5. **Notes only** (known limits, rules-only wording): Secure Record team missing or ambiguous means team-owned sources map or push nothing (check the wording, don't suggest a retry); the reconciliation window for a non-root source; the time-of-check owner read.

## Other lanes

- **054:**
  - **054a piece 1** (lookup + settings) is FINAL on `task/fm-054a-2` (code ac6f1743b3); UAC-r2 merged it into 198. The consumer rule (`IsIdentified` first) is owned by 198.
  - **Rest of 054a** waits for UAC-r2's 198 Part C. It now includes `sprk_directrecipient` (owner option A, 2026-11-09): stamped at create on Direct messages; inbound that can't be mapped → no recipient + a stable Error logged with the message id (198 treats "no recipient" as terminal; a later stamp must bump `modifiedon`).
  - **054b** secures the thread flags, `sprk_sentby`, `sprk_directrecipient` and `sprk_communicationthread`. Its dry-run evidence covers every thread-move path, including ThreadResolver (app-only). The fallback (re-grant only when the thread is unchanged since filing) touches UAC-r2's `AfterRefileAsync`: agree it with UAC-r2 first.
  - **054c** moves rows and backfills the recipient (exactly 2 distinct enabled users, one = `sprk_sentby`, else list and skip).
  - 054b and 054c live runs need the owner's go.
  - Send UAC-r2 the confirmed Web API navigation property name of `sprk_directrecipient` once it exists.
- **022b:** starts after 022 merges (Restricted mapping source; document access permission set in the create). UAC-r2 reviews.
- **045d:** Q8 (invoice family re-derive); proposal in `notes/task-045c-notes.md`.
- **052b:** scheduled repair job, contact owner rule, chat contact create.
- **032b-2/032b-3:** on the current `work/ebilling-r1` tip; 032b-2 gets two adversarial passes; the diff goes to ebilling-r1.
- **021:** deploy by UAC-r2 with the owner's go after master. **076:** re-run check (c) after UAC-r2 deploys 076c.
- **Later:** 023–029, 035, 036, 050/051, 053, 061–063, 090.

## Done this session (not project work)

- `projects/messages-enhancements-r1/` (requirements capture) merged to master via #2646; Idea #2645 on Project #2. Its branch's worktree is at `C:\wtmsg` (sparse). Remove it only with `scripts/worktree/Remove-WorktreeSafe.ps1`. The main checkout `C:\code_files\spaarke` was NOT fast-forwarded, because it has another session's local changes.

## Open owner items

- sync-mode -Apply on dev; deleting snapshot branches `…-049c` and `…-030b`; 054b and 054c live runs.

## Coordination

- Peers: `uac-r2` (desktop), `provisioning-r1`, `ontology-r1` (desktop, takes over from `spaarke-wt-spaarke-ontology-platform-r1-38`), word-add-in, ebilling-r1. Access-defect details go to uac-r2 by message only.
- UAC-r2 task 198 is the share side of 054 and uses the audience pair {`sprk_sentby`, `sprk_directrecipient`}.
