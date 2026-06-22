# Wave 2-C scope assessment — Phase 2 endpoint-blocker cascade

> **Date**: 2026-06-22
> **Author**: Main session post-Wave-2-B closeout
> **Status**: ⚠️ **PARTIALLY RETRACTED 2026-06-22 by owner challenge** — endpoint EXISTS at `POST /api/ai/playbooks/{id}/index` (different URL than I grepped for). See "Retraction" section below. Original analysis remains for Power Apps infra blocker (still valid for tasks 033/035).

---

## Retraction (owner-flagged 2026-06-22)

**My original analysis was wrong on the endpoint claim.** The user pointed out that the `playbook-embeddings` Azure AI Search index has playbook entries — which is impossible if no indexing endpoint exists. Re-grepping with the correct noun:

- `PlaybookEmbeddingEndpoints.cs:32` exposes `POST /api/ai/playbooks/{playbookId:guid}/index`
- Endpoint returns 202 Accepted, enqueues via `PlaybookIndexingBackgroundService.Instance.EnqueueIndexing(playbookId)`
- Called by Dataverse plugin on `sprk_analysisplaybook` create/update + admin tooling
- Plus `scripts/Index-ExistingPlaybooks.ps1` for bulk seeding (bypasses BFF; calls Azure OpenAI + AI Search directly)

My grep for `send-to-index|SendToIndex|sendToIndex` missed the actual endpoint URL pattern `/index`.

### Corrected task statuses

| Task | Original (wrong) status | Corrected status | Reason |
|---|---|---|---|
| 033 | 🚫 BLOCKED (no endpoint) | 🟡 PARTIAL — Power Apps infra still missing | Endpoint EXISTS; ribbon-button JS just needs to POST to `/api/ai/playbooks/{id}/index`. But `src/dataverse/web-resources/` and `src/dataverse/solutions/` directories don't exist — Power Apps layout unknown |
| 035 | 🟡 PARTIAL (ribbon blocked) | 🟡 PARTIAL — Power Apps infra still missing | Same as 033 — view itself shippable + ribbon can call endpoint; just need Power Apps source-layout direction |
| 036 | 🚫 BLOCKED (no endpoint) | 🟢 **UNBLOCKED — DO-ABLE** | Validation gate goes in the existing endpoint handler at `PlaybookEmbeddingEndpoints.cs:53-78` |
| 034 | 🟢 DO-ABLE | 🟢 DO-ABLE | Unchanged — drift job is independent |

### Remaining real blocker (Power Apps infra, NOT endpoint)

The genuine block on tasks 033 + 035 is: **Where do Spaarke's Power Apps web-resources + solutions live in the repo?** The POMLs reference `src/dataverse/web-resources/` and `src/dataverse/solutions/` but those dirs don't exist. Either:

- Power Apps source lives elsewhere (e.g., `src/solutions/SpaarkeAi/` or `src/solutions/SpaarkeCommon/` or maker-portal-only with no repo source)
- Solution layout was reorganized after the POMLs were authored
- Power Apps work is done out-of-band via maker portal (no repo source)

This blocker is real and warrants owner direction (or a brief discovery sub-task to find the correct paths). For autonomous mode, 033 and 035 remain partial-blocked; tasks 036 and 034 can proceed.

---

## Original Wave 2-C analysis (pre-retraction)

This section preserved as audit trail; the endpoint-blocker claim was wrong.

---

## What's in Wave 2-C

| ID | Title | Rigor | Status | Why |
|---|---|---|---|---|
| 034 | Nightly drift-detection background job | FULL | 🟢 DO-ABLE | Self-contained BFF code (~4h scope); no endpoint dependency |
| 035 | Power Apps "Playbook Index Drift" view | MINIMAL | 🟡 PARTIALLY BLOCKED | View shippable; ribbon inherits from blocked task 033; `src/dataverse/solutions/` missing |
| 036 | Validation gate on send-to-index endpoint | STANDARD | 🚫 BLOCKED | The `POST /api/ai/playbooks/{id}/send-to-index` endpoint does not exist — validation gate has no entry point to gate |

---

## Root cause: missing send-to-index endpoint

Task 033 was BLOCKED earlier today because `POST /api/ai/playbooks/{id}/send-to-index` does not exist in the BFF codebase (see `033-block-send-to-index-button.md`). That same missing endpoint is the gating dependency for:

- Task 033 (UI ribbon button that calls the endpoint)
- Task 035 (ribbon inheritance — partial block; view itself ships)
- Task 036 (validation gate on the endpoint — full block)
- Task 037 (backfill triggers send-to-index per playbook — partial; can backfill without UI trigger by direct Dataverse update)
- Task 038 (benchmark relies on indexed playbooks — partial; can index via direct script)
- Task 039 (deploy includes the endpoint — partial)

5 tasks downstream all touch the same missing surface.

---

## Owner decision menu (Phase 2 endpoint blocker)

| Option | Description | Unblocks | Approx effort |
|---|---|---|---|
| A | Stand up `POST /api/ai/playbooks/{id}/send-to-index` BFF endpoint as a SEPARATE preliminary task (insert 028.5 or 033a) | 033, 035 (ribbon part), 036, 037, 038, 039 | 4–6h (per `bff-extensions.md` placement decision + endpoint + tests) |
| B | Replace UX flow with **server-side auto-index on playbook publish** trigger (Dataverse plugin or PostUploadIndexingEnqueuer extension); no manual button | All same downstream tasks, redefined | 6–8h |
| C | Skip send-to-index UX for MVP; rely on task 034 nightly drift detection + manual `pac` CLI for one-offs. Mark 033, 035, 036 ⏭️ DEFERRED with task 037/038/039 redefined to use direct Dataverse update | All downstream redefined | 0 (decisions only) |
| D | Skip all index governance UX for MVP; cut Wave 2-C entirely and proceed to Phase 3 | Phase 3 unblocked immediately | 0 |

**Recommended**: **C or D**. Option C preserves the drift-detection observability story; option D maximally accelerates Phase 3.

The full WP1.5 index governance was authored before the send-to-index endpoint absence was discovered. The MVP-cut philosophy from Q5b ("extend existing over new; one excellent over five partial") suggests trimming this work too.

---

## What I'm doing autonomously without owner input

Per the autonomous-mode rules of engagement + recent stop-condition pattern (S6 escalations on infrastructure gaps), the prudent action is:

- ✅ Wave 2-A complete (030, 031)
- ✅ Wave 2-B partial (032 complete; 033 blocked-and-escalated)
- 🛑 **Wave 2-C STOPPED** pending owner decision — too many cascading blockers to proceed autonomously
- ⏭️ Phase 3 ready to start (Wave 3-A tasks 045, 046, 047 depend only on task 027 ✅ — fully unblocked)

If the user signals "continue with what's safe", I'll:
1. Block 036 + partial-block 035 in TASK-INDEX (similar to 033)
2. Pivot to Phase 3 Wave 3-A (3 parallel-safe BFF model tasks)
3. Optionally dispatch task 034 (drift job) as a long-running side-quest

If the user signals "continue with task 034 only", I'll dispatch 034 (FULL rigor BFF work; significant scope; risk of sub-agent stall like task 032).

If the user signals "fix the endpoint first", that's owner decision A; I'd need a new task POML.

---

## Branch state

- Last commit: `1347b8c92` (task 032 PlaybookEmbeddingService extension)
- Commits ahead of origin/master: 27
- BFF build: 0 errors, 16 pre-existing warnings
- BFF publish: ~46.08 MB (under 60 MB ceiling; +1.33 MB from task 032)
- Phase 1 regression suite: 10/10 pass
- Task 032 unit tests: 4/4 pass in 5 ms
- PR #409: still DRAFT

---

## Related artifacts

- `notes/handoffs/032-bff-publish-delta.md` — task 032 evidence + sub-agent dispatch notes
- `notes/handoffs/033-block-send-to-index-button.md` — original 033 block doc with owner decision menu
- `tasks/TASK-INDEX.md` — Wave 2-C task status
