# HANDOFF from core (redesign-r2) — post-audit remediation wave, 2026-07-10 (night)

> From the `spaarke-ai-architecture-redesign-r2` session. The operator ordered a full end-to-end
> completion audit of core (triggered by what you found in compose-r2 — tasks marked ✅ that were
> shims/unwired), then mandated fixing every finding in-project. This note covers what changed on
> core's branch that affects you. Report: `projects/spaarke-ai-architecture-redesign-r2/notes/e2e-completion-audit-2026-07-10.md`.

## 1. #621 is CLOSED — stop adjudicating the Changed-Surface smoke
The session-cleanup GET-after-DELETE 500 was **root-caused as a test-fixture artifact, not an
environment defect**: the mock repository returned the session even after archive, slipping past the
production 404 guards into a real SPE call that throws in the test host. Production mappings were
correct all along. Fixed in the fixture; the smoke should be green on this test from core's next merge.
The per-PR "#621 adjudication" both projects have been doing is over.

## 2. F-3 residual — a job-aware OutcomeCard seam is live and waiting for YOUR producer
Core wired `OutputRouter`/`CompletionEngine` so any routed stored payload embedding a
`JobAwareCompletionState` v1 under the reserved field `completionState` gets its OutcomeCard status
**derived from the job aggregate** (incomplete → `Partial`, never hardcoded `Succeeded`; NFR-12 live;
seam tests `tests/integration/seam/Ai/JobAwareCompletionRouterSeamTests.cs`).

Finding from the audit: **the only real producer of `JobAwareCompletionState` today is Compose**
(create-on-save + push-save project it via `JobAwareCompletionStateProjector`), but Compose returns it
on its own REST response (`SaveComposeDocumentResult.CompletionState`) and never routes through
`OutputRouter`. If/when Compose (or any doc-create capability) migrates its save outcome onto the
router/OutcomeCard path, embed the state under `CompletionEngine.JobAwareCompletionStateField`
("completionState") and the honest job-aware card is automatic. No obligation now — just know the seam
is live and tested, and core considers Compose the natural first consumer.

## 3. Dispatch prompts now carry envelope grounding (PE-D8(b) delivered)
`ActionRunner` prepends host-identity + user-memory + record-memory fragments above the operand
`## Input` section (consuming the existing dispatch `BoundInputs`; no second bind). Dispatches with no
host/memory context are **byte-identical** (seam-pinned), and no eval outcome changed — but if compose
has prompt-sensitive dispatch behavior, be aware dispatched capabilities on a host record now see that
record's memory + the caller's user memory.

## 4. Interactive convergence (F-1/F-2) — merge-relevant surface changes
- `SprkChatAgentFactory.CreateAgentAsync` gained an optional `ledgerOutputs` param;
  `IChatContextProvider.GetContextAsync` gained `sessionId` + `ledgerOutputs` params;
  `ChatContext` gained `BoundEnvelope`. If compose implements/mocks these seams, next master merge
  needs signature updates (defaults keep it source-compatible at most call sites).
- The per-turn bind moved from `ChatEndpoints` into `PlaybookChatContextProvider` (one bind per turn).
- User-scope memory now RECALLS: new `MemoryItemStore.ToUserPromptFragmentAsync` +
  `CallerSystemUserResolver` (oid→systemuserid); `memory.write scope=user` facts appear in later
  sessions' prompts ("About You" fragment, 250-token cap, mirror-guarded).

## 5. CI is now honest — expect real red where green was fake (FYI, repo-wide)
The Build & Test classifier only ever read ONE overwritten `pass1.trx` (the last test project), so
failures in `Sprk.Bff.Api.IntegrationTests` / `Spe.Integration.Tests` / `Spaarke.Scheduling.Tests`
never failed CI. Fixed (`LogFilePrefix` per-project TRX + multi-TRX verdict, commit `fe1d1cfab`).
Core also fixed every deterministic failure that had accumulated behind it — the full solution is
0-failures as of core commit `abf4471b1`. If your branch's CI goes red on integration projects after
re-merging master, that's the gate working for the first time, not a new core regression.
Also: 3 of the 5 advisory ADR ArchTest failures are fixed; ADR-007 Graph isolation + ADR-010
interface ceiling have paste-ready handoff charters
(`projects/spaarke-ai-architecture-redesign-r2/notes/adr-archtest-handoff-charters.md`).

## 6. Security heads-up (operator decision pending on core)
The audit found `SafetyPipelineMiddleware` (PromptShield scanning) is **not wired into the live chat
pipeline** — dropped by r1's dispatcher deletion (`26fde1f68`). The gate now honors a
`SafetyPerimeterDegraded` probe (core F-8), but the producer is dark until the middleware is
re-activated (operator deciding: in-wave vs security project). No action for compose; awareness only.

— core session (redesign-r2), 2026-07-10 (night)
