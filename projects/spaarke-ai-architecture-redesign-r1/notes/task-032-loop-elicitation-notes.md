# Task 032 — Loop-Native Elicitation (FR-P2-03 / OQ-3) — Execution Notes

> Date: 2026-07-06 · Wave W-P2-B · Branch: `work/spaarke-ai-architecture-redesign-r1` · Rigor: FULL
> Status: code complete; commit/status updates owned by the main session per wave protocol.

## What shipped

**Elicitation is a state of the loop, not a new dispatcher** (ADR-039; canonical walkthrough §3.10 steps 10-12):

| Contract clause | Implementation |
|---|---|
| Missing required args → clarifying turn (never execute with absent/guessed values) | `BindingCapabilityTool.InvokeCoreAsync` validates arguments against the Binding's DECLARED `sprk_inputschema` via new pure `BindingInputSchemaValidator` (`required` array + per-property `required:true`; missing = absent / null / whitespace; malformed maker JSON degrades to nothing-required). On missing: suspend + return `ElicitationTurnRouter.BuildClarifyInstruction` (declared field names + maker `elicitation_prompt` ?? `description` ONLY — grounded outputs). |
| Gate ledger markers track in-flight invocations (ADR-040 write-before-render) | Suspension goes through THE unified store: `PendingPlanManager.SuspendInvocationAsync` now kind-aware (`PendingInvocation.Kind` = `elicitation`, new `MissingFields` identifiers on both `PendingInvocation` and `SessionGate` + `StoredGate` Cosmos mapping). Marker lands BEFORE the clarifying/modal turn renders — test-proven by reading the ledger from inside the SSE writer callback. |
| `capture_mode: modal` → wizard surface | New `elicitation_modal` SSE event (`ChatSseElicitationModalData`: gateId, bindingId, consumerType, title, missingFields[{name,prompt,type}], providedArgs) emitted by the tool via the factory-forwarded `sseWriter`; model gets `BuildModalNotice` (don't Q&A in chat). No SSE surface → degrades to loop elicitation with loud log. |
| Mid-elicitation utterances parse as ANSWERS unless hard-slash / explicit restart | `ChatEndpoints.SendMessageAsync` → `ResolveElicitationTurnAsync`: ledger-state check (`PendingPlanManager.FindPendingGate(kind=elicitation)`) + deterministic escapes (`ElicitationTurnRouter.IsHardSlash` prefix check; `IsExplicitRestart` closed exact-match set: restart / start over / cancel / never mind / nevermind / forget it / stop). Answer turns get `BuildAnswerFrame` prepended to the effective message (same composed-message pattern as FR-07 attachments; persisted history keeps the raw user text) and BYPASS the legacy pre-passes (compound detection + FR-49 options + PlaybookDispatcher — the turn continues ONE pending invocation). Escapes close the gate `superseded`; TTL-lapsed payloads close `expired`. NO intent classification anywhere. |
| Resolution at the ONE dispatch seam | `SessionDispatchOrchestrator.DispatchAsync` (now injecting `PendingPlanManager`) calls new `ResolveElicitationOnDispatchAsync(tenant, session, bindingId)` after the ledger write, before the terminal chunk — a successful dispatch of the awaited Binding IS the answer, whether it arrived via loop re-invocation, wizard `dispatchConsumer`, or a chip click. Partial answers re-suspend under the SAME gate id (fresh pending marker with remaining fields; payload overwrite). |
| Budget accounting (NFR-09) | Elicitation-triggering invocations are BudgetedAIFunction-wrapped like every projected tool — they consume a budget unit and are recorded on the ToolChain. Telemetry: `[agent-turn.elicitation] suspended/modal`, `gate_suspended` (kind-tagged), `elicitation_resolved`, `gate_closed` — identifiers + counts only (NFR-07). |

## 031 W1 tightening (assigned)

`PendingPlanManager.SuspendInvocationAsync` now **throws** `InvalidOperationException` when the session no longer exists (previously: warning log + stored an unmarked payload). ADR-040: a suspension whose pending marker cannot land is aborted, not silently unmarked. Test-proven.

## Client leg

- `useSseStream.ts`: `elicitation_modal` branch (tolerant parse — drops events missing gateId/bindingId) + `setOnElicitationModal` callback-ref (mirrors `setOnPlaybookOptions`); `'elicitation_modal'` added to `ChatSseEventType`.
- `types.ts`: `IElicitationModalPayload` / `IElicitationModalField` + `onElicitationModal` SprkChat prop (host contract documented: open wizard → complete via `dispatchConsumer(bindingId, {slots})` — P3 matter pre-fill wizard builds on this).
- `SprkChat.tsx`: forwards `elicitation_modal` to the host prop (no host handler → console.warn + drop; the assistant's chat notice already told the user).
- **Confirmation-dialog rewire (031 handoff item)**: `dispatchConfirmedAction` now POSTs to the NEW unified-gate route `POST /api/ai/chat/sessions/{sessionId}/gates/{gateId}/resolve` (`{approved:true}`; `pendingAction.actionId` carries the gate id); new exported `rejectPendingAction` posts `{approved:false}`; `SprkChat.handleActionCancel` now rejects server-side (ledger `rejected` marker) instead of dropping client-side. 409 → "already resolved/expired" UX.

## New endpoint (Component Justification §11)

`POST /sessions/{sessionId}/gates/{gateId}/resolve` (`ResolveGateAsync`, JSON — not SSE):
1. **Existing**: `/plan/approve` resolves the PLAN-shaped session-singleton entry only; nothing resolved generalized `PendingInvocation`s (031 deliberately deferred: "032 chooses in-turn resume or adds the endpoint").
2. **Extension**: overloading `/plan/approve` would fork its planId contract; elicitation uses in-turn resume (no endpoint), but the confirmation DIALOG needs an HTTP resolution surface.
3. **Cost-of-doing-nothing**: `ActionConfirmationDialog` Confirm had NO server path (031's loud dead leg), and task 034's loop-boundary confirmation suspensions would have no resume surface.
   Behavior: reject → `RejectInvocationAsync`; confirm → `ResumeInvocationAsync` (409 on miss) → Binding-backed invocations execute via `SessionDispatchOrchestrator.DispatchAsync` (drained server-side; terminal summary returned as JSON; output already ledger-written by the orchestrator); non-Binding invocations → 422 `gate.no-binding-target` (loop-resume is 034's seam).

## Files

**New**: `Services/Ai/Chat/BindingInputSchemaValidator.cs` · `Services/Ai/Chat/ElicitationTurnRouter.cs` · `tests/unit/.../Chat/LoopElicitationTests.cs`
**Modified (server)**: `Models/Ai/Chat/SessionLedgerEntries.cs` (+`SessionGate.MissingFields`) · `Models/Ai/Chat/PendingInvocation.cs` (+`Kind`, `MissingFields`) · `Services/Ai/Sessions/StoredLedgerEntries.cs` + `SessionPersistenceService.cs` (mapping) · `Services/Ai/Chat/PendingPlanManager.cs` (kind-aware suspend/resolution, W1 throw, `GateKindElicitation`/`GateStatusExpired`/`GateStatusSuperseded`, `FindPendingGate`, `CloseInvocationAsync`, `ResolveElicitationOnDispatchAsync`, `WriteGateMarkerAsync(+missingFields)`) · `NullPendingPlanManager.cs` (peer overrides) · `BindingCapabilityTool.cs` (validation + suspend + modal; optional `sseWriter` ctor param) · `SprkChatAgentFactory.cs` (passes sseWriter) · `SessionDispatchOrchestrator.cs` (+`PendingPlanManager` dep + resolve-on-dispatch) · `Api/Ai/ChatEndpoints.cs` (turn routing + pre-pass bypass + records + gate-resolve endpoint)
**Modified (client)**: `hooks/useSseStream.ts` · `components/SprkChat/types.ts` · `SprkChat.tsx` · `hooks/useActionHandlers.ts` · `hooks/index.ts`
**Modified (tests)**: `tests/integration/contract/Api/Ai/DispatchSessionEndpointContractTests.cs` (fixture registers real PendingPlanManager over InMemoryTenantCache) · `tests/integration/contract/Eval/golden-utterances.json` (GU-043..046, family `elicitation`)

## Eval cases (NFR-02)

GU-043 missing-args clarify (P2) · GU-044 mid-elicitation answer "7/9/2026 and yes me" → dispatch (P2) · GU-045 hard-slash escape mid-elicitation (P2) · GU-046 capture_mode modal escape → matter-pre-fill (P3). All pending-inventory cases (live NL-loop assertions activate at tasks 034/037 per the suite's activation ledger); eval gate green (12/12) with inventory-integrity checks passing.

## Tests

- New `LoopElicitationTests` — **28/28 green**: declared-schema validation (required array / per-property / malformed-degrade / missing-value semantics), walkthrough 10-12 answer-vs-escape matrix (incl. "cancel the meeting on friday" = answer, exact "cancel" = escape), clarify instruction grounded in declared fields only, answer frame carries pending state + user message, tool suspension (marker + payload + no execution), modal event AFTER marker (ledger read from inside the SSE writer), modal-without-SSE degrade, `FindPendingGate` semantics, resolve-on-dispatch (confirmed marker + payload cleanup + turn correlation), close-superseded idempotency, W1 throw, `MissingFields` Cosmos-mapping round-trip.
- Adjacent targeted: ConfirmationGateUnification + AgentTurnLoopContract + ChatSessionLedgerRoundTrip + all Dispatch* suites — **159/159 green** (DispatchSessionEndpoint fixture updated for the orchestrator's new dependency).
- Client: `tsc` clean; jest over hooks + dispatchConsumer: 209 passed, 2 failed — both in `toolbarLaunchDefaults.test.ts` (Notepad sizing/webresource-name expectations, record-header-and-notepad workstream; files untouched by this task — pre-existing).

## Integration notes for task 034 (hard cutover)

1. **Pre-pass bypass flag**: `isElicitationAnswerTurn` gates `DetectToolCallsAsync` (empty tool-call list), the FR-49 options block, and the `PlaybookDispatcher` leg (`dispatcher`/`dispatchResult` are null on answer turns). When 034 deletes those blocks, the flag's only remaining consumer is the answer-frame substitution (`effectiveTurnMessage`) — keep that.
2. **Confirmation resume surface ready**: emit the loop-boundary `action_confirmation` SSE with `actionId = GateId` when suspending via `SuspendInvocationAsync` (kind `confirmation`); the client dialog + `POST /gates/{gateId}/resolve` are already wired end-to-end. Streamed (SSE) resume presentation is a 034 upgrade option — the endpoint currently drains and returns JSON.
3. **One resolution point**: elicitation gates resolve inside `SessionDispatchOrchestrator.DispatchAsync`. If 034 adds non-Binding execution paths for resumed invocations, route them through the same seam or call `ResolveElicitationOnDispatchAsync`/`AppendResolution` equivalents explicitly.
4. **Refusal seam (task 033, parallel)**: no shared code paths — `RefusalCapabilityTool` projects for the `no_match_handler` row and never elicits (its schema is file-less); `BindingCapabilityTool` owns elicitation for every other projected Binding. Factory loop discriminates by catalog data (consumer type), not names.
5. **Catalog authoring note**: elicitation only fires for Bindings whose ACTION declares required fields in `sprk_inputschema`. No spaarkedev1 row currently declares any (`fileIds`/`styleHint` optional) — the walkthrough `create-task` capability lands at P3 (FR-P3-03) and gets it for free; `matter-pre-fill` should declare `sprk_capturemode=modal` (GU-046).

## NFR-08 (hard cutover) evidence

No legacy clarification/slot-fill component existed to delete — OQ-3's ratified point is that NO SlotFillEngine is built; the ledger Gate markers replace the removed `in_progress_dispatch` machinery (already gone). Grep-zero SHOWN (Grep tool, 2026-07-06):

```
pattern: in_progress_dispatch|SlotFillEngine  (case-insensitive)
src/   → No files found
tests/ → No files found
```

## Publish size (ADR-029 / NFR-01)

`dotnet publish -c Release` → `deploy/api-publish/`: **141.89 MB uncompressed / 270 files**; compressed **46.95 MB** (PowerShell `Compress-Archive -CompressionLevel Optimal`).

- vs task 030's measurement (141.84 MB uncompressed / 45.59 MB compressed): **uncompressed delta +0.05 MB** — pure code (this working tree also carries task 033's refusal changes; ZERO new NuGet packages on either task — `git diff` on `Sprk.Bff.Api.csproj` is empty, so no new CVE surface by construction).
- The compressed figure differs from prior notes primarily by compressor (+1.36 MB vs 030's tooling); the uncompressed comparator is the honest one at +0.05 MB. Ceiling 60 MB: far clear; no escalation threshold approached.
- Measured before a final 4-line 404-mapping fix in `ResolveGateAsync` (code-only; size impact nil).

## Step 9.5 quality gates (2026-07-06)

- **code-review: PASS** — 0 Critical. Findings + dispositions:
  - **W1 (FIXED in-task)**: `ResolveGateAsync` let a vanished session surface as an unmapped 500; now maps `InvalidOperationException("… not found …")` → 404 ProblemDetails `gate.session-not-found` (mirrors DispatchSessionEndpoint's contract).
  - **W2 (accepted, documented)**: the mid-elicitation answer frame feeds the pending invocation's `ArgsJson` back into the LLM prompt. Those values were model-supplied last turn (already in-context then) and may derive from untrusted document text (NFR-03) — posture unchanged: the frame is context, not authority; side effects remain behind the declared-class confirmation gate (the last line, per project rule 7). No content reaches ledger/logs.
  - **W3 (accepted, by design)**: the explicit-restart escape set is a CLOSED exact-match vocabulary (7 phrases). A user typing "let's abandon this" mid-elicitation parses as an answer — deliberate: growing the set toward fuzzy matching would recreate an intent classifier (ADR-039 MUST NOT). The model's re-invocation instruction tolerates non-answer replies ("ask only for what is still missing"), and hard-slash/restart remain the deterministic exits. Documented in `ElicitationTurnRouter` header.
  - **W4 (accepted)**: `SuspendInvocationAsync` now throws on missing session (031-W1 tightening as directed); a mid-turn session eviction breaks the turn loudly instead of storing unmarked state — intended ADR-040 behavior.
  - Suggestion (deferred to 034): gate-resolve responses are JSON (drained server-side); streamed resume presentation is a 034 upgrade when the loop-boundary confirmation emitter lands.
- **adr-check: PASS** — 0 violations; no §6.5 path needed.
  - ADR-039: elicitation is loop state; answer-vs-escape is deterministic string checks in ONE auditable home (`ElicitationTurnRouter`); no tool-name lists (validation keys on catalog columns `sprk_inputschema`/`sprk_capturemode`); clarifying turns grounded in declared fields only; gate-resolve executes by Binding id through the ONE dispatch seam.
  - ADR-040: pending markers written BEFORE clarify/modal render (test-proven from inside the SSE writer); resolutions are append-only new entries correlated by gate id; markers carry identifiers only (NFR-07 — `MissingFields` = schema field names; `ArgsJson` Tier-3 store only, never logged).
  - ADR-010/032: no new interfaces; Null peers updated (`NullPendingPlanManager` overrides all new virtuals; Null orchestrator unaffected via protected ctor). ADR-014: tenant-scoped keys unchanged. ADR-019: stable errorCodes (`gate.not-pending`, `gate.no-binding-target`, `gate.dispatch-failed`, `gate.session-not-found`). ADR-016/NFR-09: elicitation-triggering calls consume budget + record on the ToolChain.
- Lint: `dotnet build` clean (0 errors; warnings pre-existing); `tsc` clean.

## Full-suite triage (2026-07-06)

**8059 total — 7953 passed, 101 skipped, 5 failed** (second run; first run had +2 AuditLogService/PlaybookDispatcherPhaseB flakes that passed on re-run). All 5 are the KNOWN pre-existing list: DailyBriefingCollector resolver, SessionFilesCleanup, TemplateContextBuilder TextOnly, KnowledgeDeploymentConfig, ExecutorConfigSchemas. **Zero failures attributable to task 032.** Client jest: 209 passed, 2 failed — both `toolbarLaunchDefaults.test.ts` (Notepad workstream, files untouched; pre-existing).

