# Current Task State

> **Last Updated:** 2026-11-20 (130g complete). Session `ebilling-r1` (Claude Desktop) is the only writer to this worktree.
> Older detail: `notes/handoff-history/current-task-2026-11-20a.md` (the previous state, verbatim), plus `-2026-11-16a`, `-15a` and `-13a`.

## Owner mode

- "Continue with open work items that are available." Keep `notes/owner-testing-checklist.md` current.
- The owner works over Remote Control. The closing message says what was done and what failed, plus the decisions needed, as questions with reply choices.

## Quick recovery

- **Active task:** none. 130g finished 2026-11-20 (merged into work 6b84fb1db1).
- **Next candidate:** 134 (SPA invoice detail and on-screen report). Its POML now carries 130g's rule: render `noticeProse` as plain text only. It also has the 130f K6 constraint. It touches `src/client/external-spa`, which is shared with spa-r4, so run /conflict-check and send spa-r4 a copy.
- **Waiting on the owner:** the OK to redeploy the Invoice Review page to dev, which makes checklist M2 testable.
- **Uncommitted files:** none, after the 130g bookkeeping commit.

## Recently done (detail in the notes and git)

- **130g ✅** merged into work 6b84fb1db1.
  - `FirmOutcome.NoticeProse` holds the stored prose from the invoice's own submission. It is set for Rejected and ReturnedToFirm only, when the kind and version match, and is re-screened on every read. A read fault omits only the field. Both outcome routes carry it.
  - Reviews: one Opus pass (two F2s and one K1, all fixed) and uac-r2 PASS (N1 and N2 fixed).
  - Notes: `notes/130g-firm-view-notice-prose.md`. Checklist section O applies after 134.
- **132ui ✅** merged into work 04718b3224. ISS-041 #2930 is filed. Checklist M2 waits for the page deploy.
- **131 dev seed ✅** 2026-11-19 (EB-D-256). `/healthz/catalog` stays 503 because of pre-existing drift (#1987).

## Open work

- **Build:** 134, 123, 124, 050b, 096, 117, 121 (121 waits for uac-r2 191).
- **Partly done:**
  - 086.
  - 164: the live `-Apply` needs the owner's go.
  - 089 and 050a: owner questions are in their notes.
- **Deploy close-outs:** 053, 079, 112, 136, 153. Then 160, 161/162 (live runs need the owner's OK), 163 UAT, 189, 190.
- **Index tidy-up:** the drift check lists 028, 040, 050a, 084d, 093, 113 and 161. These are pre-existing; resolve each from evidence.

## Standing reminders

- **Live Dataverse writes or role changes:**
  - Need the owner's OK, typed in THIS session.
  - uac-r2 gets a notice before and after.
  - Private access findings go to uac-r2 by message only, never into the public repo.
- **Commands:**
  - Never run `python`, `python3` or bare `python`.
  - No `git stash`, no `--no-verify`.
  - Merge master PRs as merge commits.
  - Never run the whole `Sprk.Bff.Api.Tests` or the whole ArchTests; filter them.
- **Agents:**
  - At most 4, each with a named model.
  - A worktree agent starts on master, so its brief names the base sha.
  - Remove agent worktrees with `scripts/worktree/Remove-WorktreeSafe.ps1`. Waiting for removal, in the main checkout `.claude/worktrees/`: `agent-a15c660293effd9cb` (132ui, locked at the last check), plus the two 130g agent worktrees.
- **Clean-up:** a human should delete C:\wt131a and C:\wt131b.
