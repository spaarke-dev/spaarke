# Current Task — spaarke-daily-update-service-r5

> Tracks the ACTIVE task only. History lives in `tasks/TASK-INDEX.md`.

**Status**: none (wave 3 committing; 020 blocked on standalone-build fix — awaiting operator A/B on PR #506)
**Active task**: none
**Next action**: operator picks A (land #506 to master) or B (band-aid branch) → re-run 020

## Completed so far (8/26)
- Validation wave: 001, 030, 035, 040
- Wave 2: 010 (deterministic bullets; −3.25 MB), 033 (resolver-bypass revert + collaborator fix)
- Wave 3: **013** (deterministic TL;DR facts; LLM prose-only, proven by prompt-capture test; −4.76 MB; 51/51 DailyBriefing tests), **011** (removed references[] LLM-citation; deterministic rows; 177 jest pass)
- **020 BLOCKED**: shared lib won't build standalone (peer-only @spaarke/* deps); fix = PR #506 (unmerged 10 days; re-arms the CI gate that was switched off 2026-06-28). Awaiting operator A/B.

## Known follow-ups
- 013 flagged: live BRIEF-NARRATE-TLDR Dataverse row prompt not PATCHed (mirror JSON updated) → bundle with task 016 UAT.

## Next root-ready (post-#506): 012 (retire CHANNEL — narrator.cs, conflicts w/ nothing now 013 done), 034 (collector de-dup), 014 (anchor resolution — deps 013 ✅), 015, 031, then 021 (design, after 020).
Serialization: 012/014 touch narrator.cs; 034/036/037 touch collector.cs → don't run same-file tasks concurrently.

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
