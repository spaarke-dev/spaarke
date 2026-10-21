# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-10-21 (#2032 merged)
> **Recovery**: read Quick Recovery, then the project [`CLAUDE.md`](CLAUDE.md) **Standing directives & gotchas**.
> Prior state (verbatim): [`notes/handoff-history/current-task-archive-2026-10-07.md`](notes/handoff-history/current-task-archive-2026-10-07.md) — grep only for a specific past detail.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **090 wrap-up**, 🔲 **HELD** until operator UAT passes (no active fix work) |
| **Status** | #2032 **merged** (PR #2205, `9b6963d73`), issue closed with details; R3 and uac-r2 told. Not on dev yet |
| **Tasks** | 27 ✅ · 2 🔄 (**029** = operator UAT render · **050** = platform-blocked) · 1 🔲 (**090**) of 30 |
| **Next Action** | Wait for R3's deploy notice; then verify the live `Sprk.Bff.Api.dll` ProductVersion ⊇ `9b6963d73` via Kudu and healthz. Then operator UAT (item 4, plus create/activate/lock/unlock), then 090 via task-execute |
| **Blocked?** | Deploy: operator approves; R3 deploys from master. 090 held for UAT |

### Uncommitted files
None. Dev BFF live = master `a10e814d02` (R3, 2026-10-21 14:35Z, verified): includes #1910, not #2032.

### Critical context
SPE Admin no longer uses owning-app secrets: container work runs as the BFF managed identity
(`5967251e-…`), grants and container types run delegated. Verified in the deployed DLL (built from master
`2677d48c` ⊇ `a5be02f0`). The god-file refactor is r3's, not r2's.

---

## Open items

| # | Item | Owner | State |
|---|---|---|---|
| 1 | Grant the BFF MI on **Spaarke Model 1** (Consuming Apps tab) | Operator | ✅ done 2026-10-07 (app `full`, delegated none — fine, MI is app-only) |
| 1b | UAT fixes (Permissions tab 403 → delegated read; detail "Billing: Unknown" → no `$select`; config-form labels). PR **#1362 merged** (`e6218e5ba`); BFF **deployed 2026-10-07** from fresh master worktree on top of uac-r2 task 114's `d437b9738` (clobber-checked), live DLL = `e6218e5ba`, hashes + healthz OK. Publish 36.19 vs 36.19 MB | Operator | ⏳ retest Permissions tab + billing badge |
| 1c | **Add Permission 400** ("'userPrincipalName' is required"): container grants must name the user by UPN, not id. Fixed in `73ead280e` (single + bulk grant paths; contract tests). PR **#1910 merged** (`3ee84cf6d`, includes uac-r2 #1909); BFF **deployed 2026-10-15** from fresh master worktree, clobber-checked, live DLL = `3ee84cf6d`, hashes + healthz OK. Publish 37.1 vs 37.1 MB Dropped by an R3 deploy from older master, then **restored 2026-10-15** by R3 (live `3115b90f2` ⊇ `73ead280e`, verified) | Operator | ⏳ retest Add Permission |
| 1d | **503 "scope unverifiable" on container open** — already fixed on master by uac-r2 PR **#1666** (merged 2026-10-12, after the 2026-10-11 503s); live in `3ee84cf6d`. **#1908 closed** by uac-r2; Graph-400 diagnostics follow-up **#1914**. Unrelated: Scheduling tests red on master → **#1912** | uac-r2 | ✅ |
| 1e | **#2032** (filed by uac-r2 2026-10-16; scope widened same day): (a) admin container create (`SpeAdminGraphService.cs:1416`, `Containers.PostAsync`) rides the default Graph retry chain, so a 503 or lost response after Graph commits can re-POST and leave a duplicate or orphaned container. Fix: pass `NoTransientRetryOption` (429-only retry) on that create, once uac-r2 PR **#2024** merges (uac-r2 will notify). (b) create never calls `POST /activate` (fresh container is `inactive`, auto-deleted after 24 h unless activated; implicit activation by the customProperties PATCH unverified); (c) `ActivateContainerAsync` ~1599 and lock/unlock ~1658/~1704 PATCH the read-only `status` instead of the `/activate`, `/lock`, `/unlock` actions. One PR for all, with WireMock contract tests | Claude | ✅ **merged** PR #2205 `9b6963d73`; awaiting dev deploy (R3). Also fixed: create dialog closes on failure, create POST survives caller cancel, 2xx-without-id = unknown outcome, `spe.containers.left_active`. UAT after deploy: create / activate / lock / unlock |
| 2 | `SecurityEvents.Read.All` on `mi-bff-api-dev` | Operator | ✅ **done 2026-10-07 15:06Z** (verified). MI token cache may delay Secure Score up to ~24 h. Alerts will still say "not provisioned" (tenant licensing) |
| 3 | Model 1 config Key Vault field blank | Operator | ✅ already blank (verified) |
| 4 | **UAT** — Model 1 containers + item search on the MI (MI lacks `Files.ReadWrite.All` the owning app had — watch search); Security tab; Add Property (first must survive a second); **Add Permission** (skipped before, not passed); 9 container-type settings save; **029** billing status renders on Container Types | Operator | ⏳ |
| 5 | Task **050** archival | — | 🔄 **platform-blocked**: 403 refusal gone; archive now **503 serviceNotAvailable** (2 runs, teardown verified). Next evidence: a container with content/age, or Microsoft. Findings §9. Re-run: `python projects/sdap-SPE-admin-app-r2/notes/probe050_archival.py` |
| 6 | Task **090** wrap-up | Claude | 🔲 after #4 |

## Branch / PR state
- Branch `work/sdap-SPE-admin-app-r2`; 0 open PRs (#2205 merged 2026-10-21).

## Successor — r3 (decided 2026-10-07, see r3 `design.md` §0)
Phase **1a** first-class SPE container component (product binds directly) → **1b** per-area file split →
**2** full decomposition, **gated** (consumer analysis + uac-r2 coordination, explicit go/no-go).
Next step there: `/design-to-spec projects/sdap-SPE-admin-app-r3`, after coordinating with uac-r2.
