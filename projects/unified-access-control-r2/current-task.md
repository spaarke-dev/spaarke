# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> **Standing rules:** project `CLAUDE.md`. **History:** git log of this file (the last working state is in the
> commit before task 090's wrap-up PR).

> **Last Updated**: 2026-11-11 (task 090 wrap-up). Private details stay in the private handoffs outside the repo; never copy them into repo text.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 090 is `[wip]`: housekeeping merged via #2685 (branch `task/uac-090-wrapup`); 090 closes after 191, 193 and 194, with a final test-diet over their tests |
| **Status** | In progress: wrap-up housekeeping done (090 PR); remaining 191 (D-13), 193 (D-14), 194's report-only gate |
| **Next action** | Build 191 (D-13) and 193 (D-14) in uac-r2 (owner, 2026-11-11: "Build in uac-r2 now"); run 194's report-only gate after the D-2 deploy; then close 090 (final test-diet, lessons re-run). Owner items below. Then the full code review the owner asked for |

## Open items

1. **Owner UAT table** — U-1..U-12 in `notes/wrap-up-uat-and-decisions-2026-11-11.md`. A failed test becomes a defect that is fixed or filed (round 90); it does not re-open its task.
2. **D-2 deploy steps** (each needs the previous one and the owner's go): (1) dev BFF from master, after the ebilling batch and mcp-r1 coordination; (2) fm 054b -DryRun / -Apply / -Verify; (3) Direct setup plus `DirectMessage:BusinessUnitName` / `OwnerTeamName` — **binding: these settings stay unset on an environment until 054b verifies there** (198 is inert while unset); (4) 054c Stamp; (5) piece 3; (6) 054c Move. D-10 (142's revoke-on-change setting) after U-3 passes.
3. **Small fixes decided now:** D-3 (#2033, refuse the send for a user disabled in Dataverse) and D-5 (#2250, refuse `sprk_assigned*` on the app-only playbook writers), as separate implementer runs.
4. **Hand-offs** — the owner confirms the list in `notes/defer-disposition-2026-11-11.md` §2 in one batch. The two close candidates in §3 (#1830, #1014) were closed on 2026-11-11.
5. **Tasks outside D-1** — 186 closed under round 90 (main-session decision; checks 3 and 6 are U-12). 178 is `[deferred]` (moved to `uac-secure-project-r3`). 191 (`[open]`) and 193 (`[wip]`) are built in uac-r2: owner D-13/D-14 2026-11-11, "Build in uac-r2 now". 194: open for its post-194b report-only gate (uac-r2 session, after the D-2 deploy); `OrdinaryMovesWritesEnabled` stays off until an owner go.
6. **Follow-ons** — D-4 (#2245), D-6 (#2270/#2278, secure-family project), D-7 (#2203, roadmap), FR-32 replay (#1303).
7. **Workforce role export** — `docs/data-model/package-scope.json` still excludes "Spaarke Internal Contact User (Workforce)". Including it needs the role exported into SpaarkeMaster (no role file exists in the repo), so it is a follow-up, not a JSON edit.
