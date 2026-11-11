# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-11 (checkpoint, desktop session `field-mapping-r1`). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030, 034, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a, 070–075, 076c, 060, 045a, 021, 045c, 032b-1, 032b-1b, 045b, 022, 054a piece 2, 054a piece 2b. Latest: #2675 (56d3463c1a). UAC-r2's 198 is on master (#2670). Nothing deployed.
> **Done on the work branch, not yet on master:** 054b, 054c (pushed at 61782874e7).
> **W3 direction:** named exceptions plus guards, no core redesign in R1.

## Quick Recovery

| Field | Value |
|---|---|
| **Session** | Desktop session **`field-mapping-r1`**, worktree `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`. |
| **Work branch** | `work/field-mapping-server-write-path-r1`, pushed at **61782874e7**. Ahead of master by 054b + 054c + checkpoint docs. Master is ~986 commits ahead of the branch: merge master before the next master PR. Master alert #2060 (`tier2-master/full-unit-tests`): compare against its shard log, never re-run unfiltered. |
| **054b (merged to work branch 4342167caf, UAC-r2 APPROVED)** | Five BFF-owned columns (thread `sprk_threadtype`/`sprk_isdefaultthread`; message `sprk_sentby`/`sprk_directrecipient`/`sprk_communicationthread`): `InheritedAccessPermission.BffOwnedCommunicationColumns`, refused on every as-the-user / generic path (reason `sdap.access.communication_bff_column_server_only`; chat create AND chat update refuse before the confirm dialog; `RecordCreationService` system path too). Generic update can no longer move a message's thread (the #2110/#2143 tests flipped; UAC-r2 confirmed none drives a path a person uses). ArchTest `ThreadFlagUpdateWriterGuardTests`: the only update writer of the two thread flags is `ThreadResolver.AnchorThreadForFilingAsync`. **A1 (owner decision 2026-11-11, strict pair):** rule 2 never anchors a Direct thread that is a strict pair or holds a message with `sprk_directrecipient`; read fault = no anchor; a missing `sprk_directrecipient` column = "no message stamped" (EventId 5413). Solution source: `IsSecured=1` for four columns + their rows in `Other/FieldSecurityProfiles.xml` (Readers 4/0/0, Writers 4/4/4); `sprk_directrecipient` is live-only. Script `scripts/Set-ThreadFlagFieldSecurity.ps1` (dry run / `-Apply` / `-Verify`; p0 refuses unless all five columns exist). Reviews: Opus pass 1, Opus pass 2 (F1 profile rows fixed), Sonnet re-check. Verified at merge: sln 0 warnings, ArchTests 2149/2149, verify-suites 7630 / 0 failed, publish +2,738 B (39.4 MB, 191 = 191 files, vs pre-merge tip 245d2e459f), no vulnerable packages. Known limits K1–K5 in the notes; K3 filed as DI-026 #2701. |
| **054c (merged to work branch 61782874e7, UAC-r2 APPROVED)** | `scripts/Migrate-DirectMessagesToOwnerTeam.ps1` + Pester (113 tests; CI job `owner-teams-script-tests` runs 3 files, 154 pass on Pester 5.7.1 — 6.x fails at load). Stamp = `ReadParticipantsAsync` rule; Move = valid stamped pair only, participants keep Read first (owner granted + read back; never a new grant to a non-owner), `-DirectShareJobLive` required for Move `-Apply`, `-GateRecordWriterApplicationIds` (ontology writer's identity), Assign-cascade refusal (`-AcceptedAssignCascade`), per-reason review counts. Precondition: 054b's field security verified, in every phase and mode. **L16 rule:** the first Move `-Apply` on an environment is scoped with `-NamePrefix` to one `FMSWP-` message (the `GrantAccess` body shape is unverified live). |
| **Next action** | 1. **Piece 3** (owner move in the BFF, after 054b): build against UAC-r2's contract in `notes/task-054-notes.md` via task-execute (FULL, Opus); re-check overlap with ontology PR #2674 on `CommunicationService.cs`. 2. 022b, 045d, 052b. 3. The next PR to master (row below). 4. Remove the merged agent worktrees (`agent-a7bb5f277747a0758` = 054b, `agent-ad75f716d72643979` = 054c) with `scripts/worktree/Remove-WorktreeSafe.ps1`. |
| **Next PR to master** | None open. Bundle 054b + 054c (+ piece 3 if ready): merge master first, `scripts/verify-suites.sh` + Communication filter, `scripts/publish-delta.ps1` vs a fresh master, snapshot branch `work/field-mapping-server-write-path-r1-<tag>`, Auto-fix on, merge only on the owner's go. Deploy prerequisite: `scripts/Set-DirectRecipientSchema.ps1 -Apply` on an environment BEFORE a BFF with piece 2 deploys there. |

## Binding deploy order (UAC-r2 D-2 revised, owner "Accept all" 2026-11-11; each live step needs the owner's go)

1. Dev BFF from master (after ebilling's batch; coordinate with mcp-r1).
2. 054b: `Set-ThreadFlagFieldSecurity.ps1` dry run → `-Apply` → `-Verify` (owner go in THIS session).
3. Direct setup + the two `DirectMessage:*` settings (owner go in uac-r2's session), only after 054b `-Verify` exits 0 there. **Binding:** the settings stay unset on an environment until then. DI-026 (#2701) check before the settings go live.
4. 054c Stamp (dry run → owner go → `-Apply` → `-Verify`).
5. Piece 3.
6. 054c Move (`-DirectShareJobLive`; first run scoped to one `FMSWP-` message, L16).

After every SpaarkeMaster import on an environment where 054b is applied: `Set-ThreadFlagFieldSecurity.ps1 -Verify`, and `-Apply` if it fails. **Assign invariant:** a message moves to the Direct team only with a valid stamped pair. Details: `notes/task-054-notes.md`.

## Known limits carried

- **022:** K2 (the isolation owner is the single Secure team) and K3 (the Project wizard's follow-on Event/WA creates map in the browser; tasks 023/024). K4 is DI-024 #2648.
- **054a:** L3 (the thread's owner controls which pair the thread yields); L6–L10 in the notes (L7/L10 fixed by 198b).
- **054b:** K1 (a user-owned email thread with one other user's Read share is not anchored), K2 (check-then-write race), K3 (DI-026), K4 (system-principal server-set columns), K5 (ArchTest blind spots).
- **054c:** L11–L16 in the notes.

## Other lanes

- **Piece 3** (owner move): next, see Next action.
- **022b:** Restricted mapping source; document access permission set in the create. UAC-r2 reviews.
- **045d:** Q8 (invoice family re-derive); proposal in `notes/task-045c-notes.md`.
- **052b:** scheduled repair job, contact owner rule, chat contact create.
- **032b-2/032b-3:** on the current `work/ebilling-r1` tip; two adversarial passes; the diff goes to ebilling-r1.
- **021:** deploy by UAC-r2 with the owner's go after master. **076:** re-run check (c) after UAC-r2 deploys 076c.
- **Later:** 023–029, 035, 036, 050/051, 053, 061–063, 090.

## Open owner items

- The live steps in the deploy order above (054b, 054c Stamp/Move on dev).
- sync-mode -Apply on dev; deleting snapshot branches `…-049c` and `…-030b`.

## Coordination

- Peers: `uac-r2` (desktop, `local_54acf7b0-9732-48a1-aa4c-2d840ded57e5`), `provisioning-r1`, `ontology-r1`, word-add-in, ebilling-r1. Access-defect details go to uac-r2 by message only. UAC-r2 owns `DirectThreadAccessService.cs`; we don't edit it.
- Nightly quality alerts open on master: #1906 (client-tests), #2060 (full-unit-tests). The `Api.Office.` contract classes crash the test host when run together; exclude them from combined runs.
