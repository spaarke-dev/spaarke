# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> **Standing rules:** project `CLAUDE.md`. **History:** git log of this file (the last working state is in the
> commit before task 090's wrap-up PR).

> **Last Updated**: 2026-11-11 (task 090 wrap-up). Private details stay in the private handoffs outside the repo; never copy them into repo text.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | none — **project complete** (task 090 wrap-up PR, branch `task/uac-090-wrapup`) |
| **Status** | Complete. No task is in progress in this project |
| **Next action** | Owner items only (below). Then the full code review the owner asked for |

## Open items after close

1. **Owner UAT table** — U-1..U-11 in `notes/wrap-up-uat-and-decisions-2026-11-11.md`. A failed test becomes a defect that is fixed or filed (round 90); it does not re-open its task.
2. **D-2 deploy steps** (each needs the previous one and the owner's go): (1) dev BFF from master, after the ebilling batch and mcp-r1 coordination; (2) fm 054b -DryRun / -Apply / -Verify; (3) Direct setup plus `DirectMessage:BusinessUnitName` / `OwnerTeamName` — **binding: these settings stay unset on an environment until 054b verifies there** (198 is inert while unset); (4) 054c Stamp; (5) piece 3; (6) 054c Move. D-10 (142's revoke-on-change setting) after U-3 passes.
3. **Small fixes decided now:** D-3 (#2033, refuse the send for a user disabled in Dataverse) and D-5 (#2250, refuse `sprk_assigned*` on the app-only playbook writers), as separate implementer runs.
4. **Hand-offs** — the owner confirms the list in `notes/defer-disposition-2026-11-11.md` §2 in one batch, and the two close candidates in §3.
5. **Tasks outside D-1** — 186, 191, 193 and 194 are still open (in-progress / pending); the owner decides: close under round 90 or carry. 178 moved to `uac-secure-project-r3`.
6. **Follow-ons** — D-4 (#2245), D-6 (#2270/#2278, secure-family project), D-7 (#2203, roadmap), FR-32 replay (#1303).
