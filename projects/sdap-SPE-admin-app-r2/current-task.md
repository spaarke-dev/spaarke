# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-10-07 (by context-handoff, before /compact)
> **Recovery**: read Quick Recovery, then the project [`CLAUDE.md`](CLAUDE.md) **Standing directives & gotchas**.
> Prior state (verbatim): [`notes/handoff-history/current-task-archive-2026-10-07.md`](notes/handoff-history/current-task-archive-2026-10-07.md) — grep only for a specific past detail.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **090 — wrap-up**, 🔲 **HELD** until operator UAT passes |
| **Status** | All code merged and **live in dev** (BFF + SPE Admin page). Waiting on operator actions + UAT |
| **Tasks** | 27 ✅ · 2 🔄 (**029** = operator UAT render · **050** = platform-blocked) · 1 🔲 (**090**) of 30 |
| **Next Action** | Wait for the operator's UAT result. Then run task **090** via `task-execute` (cite `notes/test-diet-report.md` — the `/test-diet` gate is already satisfied) |
| **Blocked?** | Not code-blocked. Operator owns: Model 1 grant + UAT |

### Files modified this session (all committed)
- `src/server/api/Sprk.Bff.Api/**` SpeAdmin — secret-free managed-identity change (PR #1291, merged + deployed)
- `projects/sdap-SPE-admin-app-r2/notes/probe050_archival.py` — archival probe, re-created + committed
- `projects/sdap-SPE-admin-app-r2/notes/task-050-findings.md` §9 — 2026-10-07 re-probe
- `projects/sdap-SPE-admin-app-r3/{design.md,README.md}` — **re-scoped** (§0)
- `docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md` §3A/§6A/§6B — identity model

### Critical context
SPE Admin no longer uses owning-app secrets: container work runs as the BFF managed identity
(`5967251e-…`), grants and container types run delegated. Verified in the deployed DLL (built from master
`2677d48c` ⊇ `a5be02f0`). The god-file refactor is r3's, not r2's.

---

## Open items

| # | Item | Owner | State |
|---|---|---|---|
| 1 | Grant the BFF MI on **Spaarke Model 1**: SPE Admin → Container Types → Model 1 → **Consuming Tenants → Add**, appId `5967251e-171c-46fe-a6c2-ef843c90309d`, app `full`, delegated `full` (≤1 h to propagate) | Operator | ⏳ not confirmed |
| 2 | `SecurityEvents.Read.All` on `mi-bff-api-dev` | Operator | ✅ **done 2026-10-07 15:06Z** (verified). MI token cache may delay Secure Score up to ~24 h. Alerts will still say "not provisioned" (tenant licensing) |
| 3 | Model 1 config Key Vault field blank | Operator | ✅ already blank (verified) |
| 4 | **UAT** — Model 1 containers + item search on the MI (MI lacks `Files.ReadWrite.All` the owning app had — watch search); Security tab; Add Property (first must survive a second); **Add Permission** (skipped before, not passed); 9 container-type settings save; **029** billing status renders on Container Types | Operator | ⏳ |
| 5 | Task **050** archival | — | 🔄 **platform-blocked**: 403 refusal gone; archive now **503 serviceNotAvailable** (2 runs, teardown verified). Next evidence: a container with content/age, or Microsoft. Findings §9. Re-run: `python projects/sdap-SPE-admin-app-r2/notes/probe050_archival.py` |
| 6 | Task **090** wrap-up | Claude | 🔲 after #4 |

## Branch / PR state
- Branch `work/sdap-SPE-admin-app-r2`; PRs #1291 and #1356 **merged**; 0 open PRs.
- This checkpoint commit (r3 re-scope, project CLAUDE.md directives, this file) is **pushed to the branch,
  not yet on master** — include it in the next PR (or 090's).

## Successor — r3 (decided 2026-10-07, see r3 `design.md` §0)
Phase **1a** first-class SPE container component (product binds directly) → **1b** per-area file split →
**2** full decomposition, **gated** (consumer analysis + uac-r2 coordination, explicit go/no-go).
Next step there: `/design-to-spec projects/sdap-SPE-admin-app-r3`, after coordinating with uac-r2.
