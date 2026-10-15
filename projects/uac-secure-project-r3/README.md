# uac-secure-project-r3: Secure Family

> **Status**: PRE-DESIGN. The folder was created 2026-10-14 on the owner's instruction. There is no spec, no tasks and no worktree yet.
> **Sequencing**: starts **after `unified-access-control-r2` is complete and stable** (owner, 2026-10-14).
> **Predecessors**: `unified-access-control-r2` (the access model this replaces part of); `spaarke-secure-project-r1` (an August 2026 draft about secure document storage; most of it was delivered by uac-r2 tasks 076, 083, 085 and 175, and task 178 carries the rest).

## What this project is

Secure records (projects, matters and work assignments marked secure, and everything filed under them) form **families**. Today the platform never stores which family a row belongs to. It recomputes it, every time, by walking the row's links to its parents. This project makes the family a stored fact:
- a family record;
- one family lookup on every family table;
- one owner team per family.

Every access, storage and external check then reads the family in one hop. Making a record secure, or not secure, moves the whole family at once.

## Start here

1. [design-brief.md](design-brief.md): the problem, what is decided, the open choices, the constraints and the scope. This is the input to `/design-to-spec`.
2. [notes/secure-family-design-research-2026-10-12.md](notes/secure-family-design-research-2026-10-12.md): the options research (BU, access team or owner team per family) and the seven platform checks.
3. [notes/researcher-dataverse-secure-family-access-teams-2026-10-12.md](notes/researcher-dataverse-secure-family-access-teams-2026-10-12.md): the researcher's raw findings and sources (Microsoft Learn and community).
4. [notes/owner-decisions-from-uac-r2.md](notes/owner-decisions-from-uac-r2.md): verbatim owner rounds 87, 88, 94, 95, 99, 100 and 100b.
5. [notes/context-from-uac-r2.md](notes/context-from-uac-r2.md): the code map, related tasks and issues, the invariants that must survive, and the lessons learned.

## How to start (when uac-r2 is done)

1. Create the worktree and branch (`spaarke-wt-uac-secure-project-r3`, `work/uac-secure-project-r3`).
2. The owner settles the open choices in `design-brief.md` §5, at least D1–D4.
3. Run `/design-to-spec projects/uac-secure-project-r3/design-brief.md`, then `/project-pipeline`.
4. The first tasks are the platform checks (spikes 2–7), with go/no-go criteria, before any schema change.
