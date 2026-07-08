# Task 030 — Agent-Turn Loop Contract (FR-P2-01) — Task Notes

> Date: 2026-07-06 · Wave W-P2-A · Executed under task-execute FULL rigor.

## What shipped

The loop contract on the existing SprkChatAgent stack (ADR-039 loop-as-dispatcher; no parallel loop built):

| Contract clause | Implementation | Proof |
|---|---|---|
| Per-turn tool budget (default 8, tunable `Ai:AgentTurn:ToolCallBudget`, NFR-09/ADR-016) | `AgentTurnContract` + `BudgetedAIFunction` wrapper; `[ADR-016][agent-turn.*]` telemetry; over-budget calls return the grounded budget-exhausted message (inner tool never executes) | `AgentTurnLoopContractTests` budget group |
| Capability-tools projection from the closed catalog | Binding rows: `IConsumerRoutingService.ListTextProjectableBindingsAsync` (opt-in = non-empty `sprk_tooldescription`) → `BindingCapabilityTool` → executes by Binding id through `SessionDispatchOrchestrator` (same stack as Click path; ledger-write-before-render inside). `sprk_analysistool` rows: existing FR-11 data-driven path, extracted verbatim to `AgentToolCatalogProjector` | projection tests + factory wiring |
| Deterministic session-context pre-filter | `AgentToolProjection.PreFilter` — pure predicate over catalog columns (`sprk_surfaces` vs the assistant surface) + structural session facts (`AgentToolFilterContext`). No scoring, no classification, no tool-name lists | pre-filter tests |
| Prompt-cache-stable projection (NFR-04) | Survivors sorted `StringComparer.Ordinal` by function name; `ComputeProjectionFingerprint` (SHA-256 over ordered name/description/schema) logged per agent creation as `[FR-P2-01][NFR-04] ... fingerprint=` | fingerprint stability tests (same tools any input order ⇒ same fingerprint; description edit ⇒ new fingerprint) |
| Citation enforcement on reads | `AgentTurnCitationEnforcer.Evaluate` at stream end in `SprkChatAgent.SendMessageAsync`; violating turn (read results consumed, zero `[N]` markers) is telemetered `[agent-turn.citation_violation]` and repaired with the deterministic `Sources:` block | enforcer tests + agent stream test |
| ToolChain ledger persistence BEFORE render (ADR-040, NFR-07) | Calls recorded on the turn contract (identifiers/filters/counts only — `SummarizeArguments` redacts free text to `<redacted:len>`); `ChatEndpoints.SendMessageAsync` flushes unpersisted segments via `ChatSessionManager.AppendToolChainAsync` BEFORE each rendered text segment + trailing flush | NFR-07 recording tests, drain-semantics tests, AppendToolChain tests |

## Factory shrink (acceptance)

- `SprkChatAgentFactory.cs`: **2714 → 1942 lines (−772, −28%)** — `ResolveTools` + FR-11 data-driven block + 3 static helpers moved verbatim to `AgentToolCatalogProjector` (889 lines, new home of the catalog projection).

## Publish size (ADR-029 / NFR-01)

- `dotnet publish -c Release` → `deploy/api-publish/`: **45.59 MB compressed** (141.84 MB uncompressed).
- Baseline ~45.65 MB (post-Phase 5 Outcome A) → **delta −0.06 MB (neutral-to-reduction, as expected)**. Ceiling 60 MB: far clear.

## Prompt-cache verification evidence (NFR-04)

- Deterministic serialization proven by test: two projections over the same tool set in different input orders produce identical SHA-256 fingerprints; the fingerprint is emitted per agent creation (`[FR-P2-01][NFR-04]` log) so cache stability across turns is observable in App Insights (same session state ⇒ same fingerprint ⇒ byte-identical tool block ⇒ Azure OpenAI prefix-cache hits).
- The `BudgetedAIFunction` wrapper preserves inner name/description/schema verbatim, so wrapping does not perturb the projected block.
- Live cache-hit-rate measurement against Azure OpenAI is a deploy-time observation — belongs to the W-P2 gate UAT (fingerprint log makes it a 1-query check).

## Orphan-handler health-check escalation (gate-014 deferral, extra acceptance criterion)

- `RoutingConsumerTypeHealthCheck`: orphan-handler dimension **escalated Degraded → Unhealthy** — `HandlersWithoutToolRows` folded into `HasDrift`; `BuildOrphanDescription` deleted; drift text names the remediation (seed row via `scripts/Seed-TypedHandlers.ps1` or delete the handler).
- Reconciliation census (2026-07-06, spaarkedev1 via Dataverse MCP): 35 concrete registered handler classes vs active `sprk_analysistool` rows — **every registered handler has a row except `TemplateHandler`** (self-documented "NOT a production handler... never invoked"; zero test references). Resolution: **deleted** (`git rm`), grep-zero shown (only residues were one adapter error-string + one .md pointer, both updated). The stale "14 orphans" figure predated the gate-014/-027 row seeding.
- `/healthz/catalog` on spaarkedev1 currently returns `Degraded` from the DEPLOYED (pre-task-030) build — its registered set still contains `TemplateHandler`. **Healthy flips at the next BFF deploy of this branch** (W-P2 gate): the deleted handler leaves the registry and the bijection is complete. Post-deploy verification step for the gate: `curl https://spaarke-bff-dev.azurewebsites.net/healthz/catalog` → `Healthy`.

## Integration notes for tasks 032 / 034 (and 031)

- **032 (elicitation)**: clarifying-turn logic intentionally OUT of this task. Seams provided: `Binding.CaptureMode` is carried on every projected `BindingCapabilityTool.Binding`; `AgentTurnContract` is reachable via `ISprkChatAgent.TurnContract` through the whole middleware pipeline; ledger `Gate` markers write through the same `ChatSessionManager` extension pattern as `AppendToolChainAsync`.
- **031 (confirmation gate)**: not touched (hard boundary). `BindingCapabilityTool` codes against the `SessionDispatchOrchestrator` seam — the generalized pending store slots in behind dispatch without changing the projection. `SessionDispatchOrchestrator`'s P1 envelope already rejects non-informational dispositions pre-run, so no ungated side effect is reachable through the loop today.
- **034 (hard cutover)**: the loop tool list is now budget-wrapped, pre-filtered, deterministic, and ledger-audited — cutover deletes `PlaybookDispatcher`/reranker/candidate-selector call sites in `ChatEndpoints.SendMessageAsync` (lines around the R2-018/FR-49 blocks) and the `DetectToolCallsAsync` compound pre-pass; `AgentToolCatalogProjector` + `AgentToolProjection.Finalize` is the single projection path to keep.
- **Turn numbering**: ToolChain `Turn` uses `max(existing ToolChains[].Turn)+1` per user turn (mirrors OutputRouter's Output ordinal decision, task 021). Interleaved tool/text phases within one turn append multiple chain segments under the same turn ordinal (append-only).

## Step 9.5 quality gates (2026-07-06)

- **code-review: PASS** — 0 Critical, 5 Warnings, 3 suggestions. Actions taken in-task:
  - **W1** (citation-delta correct only under sequential tool invocation): documented as an explicit prerequisite comment in `BudgetedAIFunction` (FunctionInvokingChatClient default `AllowConcurrentInvocation=false`); watermark refactor prescribed if concurrency is ever enabled.
  - **W2** (single-token free text under content-bearing arg names passed the identifier heuristic): FIXED — `AgentTurnContract.AlwaysRedactedArgNames` (`query/text/body/message/content/prompt/instruction/question`) now redacts by NAME regardless of shape; regression test added.
  - **W3** (turn-ordinal race + non-atomic ledger append under concurrent SendMessage on one session): accepted for this task (sequential invocation + re-fetch mitigate; same last-writer pattern as the pre-existing OutputRouter/session pipeline). Follow-up candidate: optimistic concurrency token on the session blob — flagged for the project backlog.
  - **W4** (tool side-channel SSE frames precede their covering chain segment under a strict per-event ADR-040 reading): interpretation documented — the PRIMARY render (assistant text, citations footer, done) is always preceded by the ledger flush; side-channel widget/citation frames are emitted by the pre-existing adapter during tool execution and are not the turn's output.
  - **W5** (test placement outside the 6 KEEP paths): consistent with the entire existing Chat unit suite pre-reorganization; tests are behavior anchors — defend at /test-diet.
- **adr-check: PASS** — 0 violations across ADR-039/040/010/013/014/015/016/019 + NFR-04/07/09. No §6.5 resolution path needed. Extraction fidelity of `AgentToolCatalogProjector` mechanically verified against `git show HEAD` (4 declared deltas, no semantic drift).

## Test results (2026-07-06)

- Targeted: `AgentTurnLoopContractTests` (28) + `RoutingConsumerTypeHealthCheckTests` (14) + factory tool-resolution + SprkChatAgent + OutputRouter + ledger round-trip — all green.
- Full unit suite: **8017 total — 7911 passed, 101 skipped, 5 failed**; all 5 failures are the KNOWN pre-existing list (DailyBriefingCollector resolver, TemplateContextBuilder TextOnly, SessionFilesCleanup, KnowledgeDeploymentConfig, ExecutorConfigSchemas; the PlaybookDispatcherPhaseB latency flake passed on the final run). Zero failures attributable to task 030.
- One intentional test UPDATE: `CheckHealthAsync_HandlerWithoutToolRow_ReturnsDegradedNamingOrphanHandler` → `...ReturnsUnhealthyNamingOrphanHandler` — this test anchored the gate-014 deferral semantics that this task's acceptance criterion explicitly ends.
- Publish size measured after the full implementation; subsequent post-review edits were comments + one redaction set + test code (no packages) — size impact nil.

## Known pre-existing failures observed (not chased, per task brief)

ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector resolver, TemplateContextBuilder TextOnly, SessionFilesCleanup, AuditLogService + PlaybookDispatcherPhaseB latency flakes, NetArchTest ADR-010 ceiling.
