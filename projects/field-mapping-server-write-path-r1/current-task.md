# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-08 (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030 (a #2610, b #2629), 034 (#2629), 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a (#2629), 070–075. Latest: #2629 (57748ebf6d, 2026-11-08 04:43Z).
> **W3 direction:** named exceptions plus guards, no core redesign in R1 (decisions.md 2026-11-06).

## Quick Recovery

| Field | Value |
|---|---|
| **Work branch** | `work/field-mapping-server-write-path-r1`, pushed, ~30 commits ahead of master. Merged since #2629 and not yet on master: 076c, 060, 045a (d6318dc1f8), 021 (deafbd328c), docs. PRs go from a snapshot branch `work/field-mapping-server-write-path-r1-<tag>`. |
| **Next PR** | 076c + 060 + 045a + 021 (+ 045c if its fix round lands first). Steps: `scripts/verify-suites.sh` → `scripts/publish-delta.ps1` (fresh master) → diff to UAC-r2 → snapshot branch → PR, auto-merge. Cite ADR-008 path A. Closes #2124 if not already closed by #2629. |
| **Running agents** | 045c (`task/fm-045c`): fix round 3, two live client defects (stale `sprk_regardingrecordnumber` on re-regard in `ResolverWriteHandler.ts` and `ConnectionsWriteHandler.ts`; create-mode staged number). 045b (`task/fm-045b`): update planner, guard 2, Q8 update side. 032b-1 (`task/fm-032b-1`, 95c594829f): Opus adversarial review running. 022 (`task/fm-022`): Project wizard, code part. |
| **After each** | 045c → scoped re-check → merge. 045b → Opus adversarial review → merge. 032b-1 → fix any F → UAC-r2 review → publish delta → its own PR to master; then 032b-2 (two adversarial passes) and 032b-3 on the current `work/ebilling-r1` tip, diff to ebilling-r1. 022 → review → merge; then 023–029 in order. |
| **Owner decisions 2026-11-08** | 032 option B with ebilling-r1; O1 inline; U7 accepted (Daily Briefing stops selecting invoices through the copied assignee columns); O2 gate move approved (other creators listed in the 032b-1 inventory). Repair pass: contacts only get an owner rule, via `ExternalAccess:CustomerBusinessUnitId` → that BU's default Owner team, fail closed; same for the chat contact create; task and account excluded. Inbound mapping: gap (DI-022 #2633). NFR-02 verdict moves to UAT. |
| **Open with the owner** | One private item, handled with UAC-r2 by message (not recorded in the repo, by rule). Sync-mode `-Apply` on dev (owner review). Deleting snapshot branches `…-049c`, `…-030b` (user OK). |
| **Gotchas this session** | Shell calls time out under agent load: use Grep/Read, timeouts ≥300 s, background runs. `timeout 400 git commit`. `gh --jq`, not `jq`. Live helpers in the scratchpad `live/`; never print a token. App Insights drops whole requests (#2158). |

## Task state (detail in TASK-INDEX.md)

- **021:** code merged. Deploy order (UAC-r2, owner go): BFF with the `sprk_matter` route, then `sprk_creatematterwizard` via code-page-deploy, then the live checks in `notes/task-021-notes.md`. The Console's in-app wizard moves with a SpaarkeAi rebuild.
- **045:** 045a merged; 045b running; 045c fix round 3; 045d (Q8 create half) after 045b if the proposal in `notes/task-045c-notes.md` holds.
- **032b-1 open questions for 032b-2:** Repair would fill empty invoice display copies once #1911 lands (decide in 032b-2); invoice `sprk_assignedto1/2` are plain data now; Compose sync endpoints not traced in the inventory.
- **052:** 052b (scheduled job, disabled by default, GoLiveUtc, principal-set selection, watermark/claim, per-pass metadata cache, contact owner rule, chat contact create on the same mechanism) waits on 032b-1's shared gates; UAC-r2 reviews. Then 052c, 052d, 052e.
- **076:** re-run check (c) after UAC-r2 deploys 076c (BFF + RecordFamily code page).
- **Later:** 035, 036, 050/051, 053, 061–063, 090.

## Standing rules (also in the project CLAUDE.md and memory)

- The repo is PUBLIC: access defects go to UAC-r2 by message only.
- Live Dataverse writes need the owner's go in THIS session.
- No deploys from here.
- Never run Sprk.Bff.Api.Tests unfiltered: use `scripts/verify-suites.sh`.
- Every Agent call names a model; at most 4 agents at once.
- Every defect is fixed in scope, or filed and reported.
