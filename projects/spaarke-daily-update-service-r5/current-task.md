# Current Task — spaarke-daily-update-service-r5

> Tracks the ACTIVE task only. History lives in `tasks/TASK-INDEX.md`.

**Status**: none (wave 2 complete; awaiting operator go-ahead for next wave)
**Active task**: none
**Next action**: operator confirms → dispatch next wave

## Completed so far (6/26)
- Validation wave: 001, 030, 035, 040
- Wave 2: **010** (removed per-channel LLM leg; deterministic `BuildDeterministicBullet`; −3.25 MB; 34/34), **033** (reverted resolver bypass — R7 root-cause fix confirmed present; re-flipped pinned test + collaborator smoke; 12/12)

## Next root-ready: 013 (deps 010 ✅ → deterministic TL;DR scaffolding), 034 (deps 033 ✅ → collector de-dup), 012 (deps 010 ✅ → retire CHANNEL action), 011 (deps 010 ✅ → client deterministic rows), 020 (harness), 015, 031.
Note: 013 and 034 BOTH touch DailyBriefingCollector.cs → must NOT run concurrently.

## Progress (2026-07-08)

- Project initialized: 26 tasks, TASK-INDEX, registered in projects/INDEX.md.
- **Validation wave COMPLETE (4/4 ✅)**: 001 (OData doc), 030 (CoerceFieldValue String→Choice fix), 035 (client jest tests), 040 (deploy convention).
  - Independently verified: BFF build 0 errors; 44/44 affected BFF tests; 36/36 new jest tests (3 failures confirmed pre-existing); publish size −0.17 MB; frozen-engine Path-A honored; DI scope bridge correct.

## Next wave candidates (root-ready, per TASK-INDEX)

- **010** (remove per-channel LLM narrate leg — starts the Phase-A accuracy chain)
- **020** (scaffold /prototype harness — cross-repo; escalates if standalone build fails)
- **033** (collaborator-scope fix — starts the collector chain 033→034→036→037)
- **015** (groundedness guardrail), **031** (jps-validate — main-session)

Run `/conflict-check` before dispatching (r2-core `Services/Ai/` overlap). Collector-chain tasks and 013 must not run concurrently.
