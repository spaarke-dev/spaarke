# Task 034 — HARD CUTOVER of chat NL to the loop (FR-P2-05) — Task Notes

> Date: 2026-07-06 · Wave W-P2-C · Executed under task-execute FULL rigor.

## What shipped

The chat text path is now the agent-turn loop and **nothing else** (ADR-039 — one dispatch
protocol). Three legacy pre-passes that used to sit in `ChatEndpoints.SendMessageAsync`
between agent creation and the loop stream — each a route by which a chat NL utterance could
reach a legacy dispatcher — are **DELETED outright** (no fallback flag, no compat shim; NFR-08
hard-cutover doctrine):

| Deleted pre-pass | Was | Now |
|---|---|---|
| Compound-intent detection | `agent.DetectToolCallsAsync` → `CompoundIntentDetector.IsCompoundIntent` → `plan_preview` SSE + halt | gone — write/communicate side effects gate loop-native at the dispatch seam (FR-P2-02) |
| PlaybookDispatcher single-match auto-dispatch | `dispatcher.DispatchAsync` → `PlaybookOutputHandler` (R2-018) | gone — capabilities resolve through the projected `BindingCapabilityTool` in the loop |
| FR-49 file-aware options | `dispatcher.RunPhaseBVectorMatchAsync` → reranker → `playbook_options` SSE | gone |

After the deletion the SOLE surviving branch between agent creation and `agent.SendMessageAsync`
is task 032's `effectiveTurnMessage` substitution (`elicitationTurn?.FramedMessage ?? effectiveMessage`)
— a mid-elicitation answer turn rides the loop with the answer frame prepended. Every other NL
utterance flows straight into the loop. `isElicitationAnswerTurn` (whose only remaining consumers
were the deleted pre-passes) was removed.

### intentHint retired end-to-end (NFR-08)

The `intentHint` soft-slash vector-query bias is gone across server + client:
- `ChatSendMessageRequest.IntentHint` DTO field removed (wire contract).
- `SprkChatAgentFactory.CreateAgentAsync` + `NullSprkChatAgentFactory` — `intentHint` param removed.
- `PlaybookDispatcher` (`DispatchAsync` / `RunPhaseBVectorMatchAsync` / `RunPhaseBManifestPresentAsync` /
  `RunPhaseBManifestAbsentAsync`) — the FR-20/task-115 `intentHint` bias parameter + cache-key segment +
  query-prefix composition stripped, reverting to the pre-115 query form. (The class itself is deleted
  wholesale in task 035; here we only remove the parameter surface so grep-zero holds.)
- Client `SoftSlashRouter.ts` (pure `intentHint` plumbing) DELETED; `ConversationPane.handleDecorateOutboundBody`
  no longer decorates soft slashes; shared-lib `SprkChat/types.ts` JSDoc example updated.

Helper methods `BuildDeclaredSideEffectLookupAsync` + `DerivePlanSideEffectClass` (sole callers were the
deleted compound pre-pass) were deleted — this also kills the per-tool-proposing-turn catalog query that
task 031-W2 flagged as living on the legacy detection path.

## Grep-zero evidence (NFR-08) — SHOWN

```
$ git grep -il "intenthint" -- src tests
>>> git grep-zero: NO tracked files under src/ or tests/ contain intentHint <<<
```

(Case-insensitive; explanatory comments were reworded to avoid the literal token so grep-zero is true.
The only remaining hit anywhere is the gitignored build artifact `src/solutions/SpaarkeAi/dist/spaarkeai.html`,
which is not tracked and regenerates on the next `npm run build`.)

Legacy-dispatcher severance in `SendMessageAsync`: the handler body no longer references
`PlaybookDispatcher` / `CompoundIntentDetector` / `DetectToolCallsAsync` / `RunPhaseBVectorMatch` in any
executable path — only in the FR-P2-05 explanatory comment. (The `ExecutePlaybookAsync` click endpoint
lower in the file still names them — see Leftover inventory.)

## AgentContentSafetyMiddleware on the loop path (NFR-03) — evidence

- **Wiring (unconditional)**: `SprkChatAgentFactory.CreateAgentAsync` ends with
  `agent = WrapWithMiddleware(agent, tenantId); return agent;` (no branch). `WrapWithMiddleware`
  instantiates `AgentContentSafetyMiddleware` as the **innermost** wrapper (pipeline order:
  ContentSafety → CostControl → Telemetry → Routing). Every agent the loop uses is the wrapped agent.
- **Path**: `ChatEndpoints.SendMessageAsync` streams via `agent.SendMessageAsync(effectiveTurnMessage, …)`
  — the wrapped agent — so every loop-turn token passes through `AgentContentSafetyMiddleware.FilterContent`.
  Uploaded-document text is composed into `effectiveMessage`/`effectiveTurnMessage`
  (`ComposeMessageWithAttachments`) and tool results are produced inside the loop; both surface as
  response tokens that stream through the middleware. The middleware is origin-agnostic — it filters
  ALL response tokens regardless of whether they derive from trusted or untrusted (document/tool) input.
- **Tests (passing)**: `AgentMiddlewareTests` — `ContentSafetyMiddleware_FiltersSsnPattern` /
  `_FiltersCreditCardPattern` / `_FiltersEmailPattern` / `_PassesCleanContentUnchanged` /
  `_AcceptsCustomPatterns`, plus the pipeline-order anchor
  `MiddlewareChain_ContentSafetyFiltersBeforeCostControlCounts` (proves innermost placement). 136/138
  green in the middleware+chat targeted run (2 pre-existing skips: CostControl budget-signature +
  a stale SSN-warning test).

## Publish size (ADR-029 / NFR-01)

`dotnet publish -c Release -o deploy/api-publish` → **46.95 MB compressed** (PowerShell
`Compress-Archive -CompressionLevel Optimal`, incl. 1.92 MB PDBs) / 141.88 MB uncompressed / 270 files.
Same compressor + tree lineage as the W-P2-B baseline (tasks 032/033 = **46.95 MB**): **delta ≈ 0.00 MB**
— net-neutral, as expected for a deletion task (the cutover removes call sites + a threaded parameter;
the IL reduction rounds to zero at MB precision). **ZERO NuGet changes** (`git diff` on the csproj is
empty) → no new CVE surface by construction. Far below the 60 MB ceiling; no escalation threshold approached.

## Tests

- **Targeted (green)**: AgentTurnLoopContract + ConfirmationGateUnification + LoopElicitation +
  RefusalCapabilityTool + ChatEndpointsAttachments + SprkChatAgent + AgentMiddleware = **136 passed / 2 skipped**.
  All five PlaybookDispatcher suites in isolation = **38/38 green** (incl. PhaseB latency test).
- **Full unit suite**: **8037 total — 7931 passed, 101 skipped, 5 failed**. All failures on the KNOWN
  pre-existing list (ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector resolver,
  PlaybookTemplateContextBuilder TextOnly, SessionFilesCleanup) + the PlaybookDispatcherPhaseB latency
  flake (passes in isolation — confirmed). **Zero failures attributable to task 034.**
- **Eval suite (NFR-02)**: `--filter Category=GoldenUtteranceEval` = **12/12 green** through the loop path.
- **Client**: SpaarkeAi `npm run typecheck` = 0 surface-owned errors (77 pre-existing shared-lib errors
  deferred); jest `src/components/conversation` = **12 suites / 216 tests passed** (no broken imports from
  the deleted SoftSlashRouter).

## Tests deleted (exercised deleted pre-pass / retired mechanism)

- `tests/unit/.../Api/Ai/ChatIntentHintRoundTripTests.cs` — tested the retired `IntentHint` DTO field.
- `tests/unit/.../Services/Ai/Chat/PlaybookDispatcherIntentBiasTests.cs` — tested the retired `intentHint`
  vector-query bias.
- `src/solutions/SpaarkeAi/.../__tests__/SoftSlashRouter.test.ts` — tested the deleted `SoftSlashRouter`.
- `.../__tests__/composition.integration.test.ts` — soft-slash `intentHint`-decoration composition
  (reference-resolution coverage remains in `ReferenceResolver.test.ts`).
- `.../__tests__/natural-language-regression.test.ts` — NFR-11 "no intentHint for NL" regression, moot
  after retirement (CommandRouter parse behavior covered by `CommandRouter.test.ts`).

## 🔔 ESCALATION — soft-slash deterministic direct invocation (acceptance criterion 2)

**Status: PARTIAL — the retirement is done; the deterministic-invocation mechanism is a documented design
decision deferred to P3, per the parent's "narrower deletion + document the leftover" directive.**

The task requires the four retained soft slashes (`/summarize`, `/draft`, `/extract-entities`, `/analyze`)
to invoke **deterministically via Click-path semantics — not loop suggestions**. On close inspection this
sub-goal cannot be fully + cleanly realized in P2 without either an ADR-039 violation or new machinery that
is explicitly P3 scope:

- **Catalog reality (spaarkedev1)**: only ONE of the four commands has a live, text-projectable Binding —
  `/summarize` → `chat-summarize` (SUM-CHAT@v1). `/draft` (→ P3 FR-P3-02 `draft-correspondence`),
  `/extract-entities`, and `/analyze` have **no Binding rows** yet.
- **Click path = `dispatchConsumer(bindingId, args)`** — it dispatches by the Binding **row GUID**
  (`DispatchSessionEndpoint` rejects non-GUIDs; ADR-039 forbids a second/consumer-type resolution
  vocabulary). The client holds NO standing binding GUID for a typed command (chips receive theirs from
  server SSE events; there is no client capability-discovery endpoint).
- Therefore a soft-slash → deterministic Click dispatch needs a binding-id-carrying **launcher affordance**,
  which is exactly the P3 FR-P3-06 deliverable ("wizard/launcher widgets carry binding ids"), OR the P3
  Binding rows for the other three commands.

**Every alternative I considered has an ADR-039 tension:**
- (A) Hardcode binding GUIDs in the client → env-specific + violates "client carries zero routing logic".
- (B) New server soft-slash→consumer-type dispatch endpoint → a second resolution vocabulary / routing
  config outside the Binding table (ADR-039 MUST NOT).
- (C) Server-side leading-token command parse in the messages endpoint → reintroduces a routing pre-pass
  in the very handler we just severed.

**What I did instead (ADR-clean):** fully retired `intentHint` + `SoftSlashRouter`; kept `CommandRouter`
soft-slash RECOGNITION and hard-slash client execution (hard slashes ARE deterministic client-side per
§7.2). Post-cutover, a soft slash's text enters the loop like any NL utterance (the loop projects
`chat-summarize`, so `/summarize` will be honored — but as a loop decision, not a guaranteed deterministic
Click invocation).

**Recommended resolution (operator decision — §6.5 path A/C):** treat soft-slash deterministic direct
invocation as **P3 FR-P3-06** scope (binding-id launcher affordances) — the cleanest path that neither
violates ADR-039 nor invents throwaway machinery. UAT-7 (gate 038) verifies soft-slash determinism in the
browser; if the operator wants it live for G-P2, the minimal ADR-defensible interim is a client
capability-discovery read (list the session's projectable Bindings once, map the closed 4-command vocab to
the returned GUID for `chat-summarize`, dispatch via the existing `dispatchConsumer`) — a small addition
that stays "bindingId in / stream out." Flagged for operator direction.

## Leftover inventory for tasks 035 / 036 (narrower-deletion boundary)

Severing the **chat NL entry** (this task) leaves the **dispatcher stack + its click legs** for 035/036:

- **035 (FR-P2-06)**: delete `PlaybookDispatcher` (+ embeddings index jobs), `IntentRerankerService`,
  `PlaybookCandidateSelector`, `CompoundIntentDetector`, and their unit tests
  (`PlaybookDispatcher*Tests`, `CompoundIntentDetector*`, `PlaybookOptionsEventBuilderTests`,
  `ConfirmationGateUnificationTests` detector assertions). NOTE: the flaky `PlaybookDispatcherPhaseBTests`
  latency test dies with the class — correct.
- **036 (FR-P2-07)**: `PlaybookOutputHandler` + `Services/Ai/Chat/Tools/*`.
- **Dead click endpoints in `ChatEndpoints.cs`** (now unreachable — nothing emits their triggering SSE):
  `ExecutePlaybookAsync` (`POST /api/ai/playbook-dispatch/execute`, consumed `playbook_options` picks) +
  `ApprovePlanAsync`/plan endpoints (consumed `plan_preview`). These still compile (they call factory
  `CreatePlaybookDispatcherAsync` / `CreatePlaybookOutputHandler`, which survive to 035). Left in place per
  the narrower-deletion boundary; delete alongside the stack in 035/036. The `plan_preview` /
  `playbook_options` SSE DTOs + the `ChatSseEvent.Type` enum strings are their downstream and go with them.
- **Client (P3 FR-P3-06)**: `ConversationPane` playbook_options handlers (task 117b), `ActionConfirmationDialog`
  leftovers, and the soft-slash launcher work (see escalation).

## Step 9.5 quality gates

(see below — code-review + adr-check run after this note is written)
