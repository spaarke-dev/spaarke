# Spike 0 — Validate the session-dispatch path for a Compose Binding

> **Task**: 000 · **Phase**: 0 Spikes · **Date**: 2026-07-08 · **Model**: opus @ high
> **Method**: static code trace (grounded, file:line evidence) + contract reconciliation.
> **Deliverable**: this note. No production code, no throwaway Dataverse rows committed
> (see §7 — why the POML's "author throwaway Binding + wire stub button + run live" steps
> were adapted under directional step-mode).

---

## 1. Decision (the one thing this spike unlocks)

**YES — the shipped ADR-039 session-dispatch seam is confirmed present and coherent end-to-end
for a Compose Binding, by static trace.** Every leg the design §13 chain names exists in this
worktree, connects to the next, and requires **zero new BFF dispatch routes**. Phase 1/3/4 may
build on it.

**One material correction to the design/POML premise** (this is the high-value finding — §4):
the Compose-specific event the POML assumes (`compose_action_request` carrying
`{bindingId, selection, args}`) **does not exist and should not be created.** The R1 Compose
project already shipped the real Compose event contract — the six-flow PaneEventBus choreography
in [`compose-contracts.ts`](../../../../src/client/shared/Spaarke.Compose.Components/src/types/compose-contracts.ts).
A Compose selection/toolbar interaction emits **`conversation.compose_selection_offer`**, and the
actual capability dispatch is a **direct `dispatchConsumer(bindingId, {slots})` call** (the shipped
`useConsumerChips` pattern), not a new bus event. Tasks 016 / 030 / 046 must be authored against
the real contract, not `compose_action_request`.

**Runtime caveat (honest scope):** a *live* observation of SSE frames rendering + a ledger row
being written for a throwaway Compose Binding requires a running BFF + Dataverse + browser session
and a real catalog Binding row (Phase 4, core-A0-gated). That is **not** statically confirmable and
was **not** run here. The static trace confirms the wiring is correct and complete; §6 gives the
exact recipe to close the runtime confirmation on `spaarkedev1` when Phase 1 begins.

---

## 2. The seam, leg by leg (with evidence)

Design §13 chain: PaneEventBus → ConversationPane → `dispatchConsumer(bindingId, args)` →
`POST /api/ai/chat/sessions/{id}/dispatch` → `SessionDispatchOrchestrator` → Binding resolution →
prompted executor → SSE stream + ledger `SessionOutput` write.

| Leg | Status | Evidence (file:line) |
|-----|--------|----------------------|
| Client dispatch helper `dispatchConsumer(bindingId, args)` | ✅ shipped | [`dispatchConsumer.ts:374,377`](../../../../src/client/shared/Spaarke.UI.Components/src/services/dispatchConsumer.ts) — `createConsumerDispatcher(deps)` returns the 2-arg helper |
| Client builds the dispatch URL + POST body | ✅ shipped | `dispatchConsumer.ts:348` `buildDispatchUrl` → `/api/ai/chat/sessions/${sessionId}/dispatch`; `:531` body `{ bindingId, args: args?.slots ?? {} }` |
| Client SSE consumption (canonical, one parser) | ✅ shipped | `dispatchConsumer.ts:528` `readSseStream` + `:538` `parseSseEvent` (the one SSE path, `hooks/useSseStream.ts`) |
| Server route `POST /api/ai/chat/sessions/{sessionId}/dispatch` | ✅ shipped, **unconditional** | [`DispatchSessionEndpoint.cs:90`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/DispatchSessionEndpoint.cs) `MapPost(".../dispatch", DispatchAsync)`; mapped from `EndpointMappingExtensions.cs:180` |
| Server delegates to orchestrator (no routing in endpoint) | ✅ shipped | `DispatchSessionEndpoint.cs:229` `orchestrator.DispatchAsync(request, ct)` |
| Binding resolution lives ONLY in the Binding table (ADR-039) | ✅ confirmed | `DispatchSessionEndpoint.cs:160-171` — `bindingId` must be the `sprk_playbookconsumer` row GUID; non-GUID → 400, unknown/disabled → 404, no fallback |
| ADR-040 ledger write BEFORE render | ✅ confirmed | [`SessionDispatchOrchestrator.cs:388-389`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs) "the universal ledger write BEFORE render… OutputRouter writes the addressable SessionOutput ({bindingId}@t{n})"; terminal chunk "rendered FROM the stored ledger entry" (`:143`) |
| SSE stream out (one wire shape, shared writer) | ✅ shipped | `DispatchSessionEndpoint.cs:319` `ContentType="text/event-stream"`; `:331,:363` `SummarizeSessionEndpoint.WriteSseChunkAsync` (same writer both streams) |
| DI registration (concrete, scoped; Null-Object mirror) | ✅ shipped | `AnalysisServicesModule.cs:699` `AddScoped<SessionDispatchOrchestrator>()`; `:531` `NullSessionDispatchOrchestrator` on compound-OFF branch (ADR-032) |

**Conclusion:** the server surface is "already answered" as the POML states; the client helper and
the ledger/SSE legs are equally present. No gap in the transport seam.

---

## 3. Compose-specific contracts a Compose interaction must satisfy (acceptance criterion #2)

### 3a. The dispatch call shape (the actual "how to invoke")

The canonical, shipped pattern to copy is **`useConsumerChips.tsx`** (chip click → dispatch):

```ts
// createConsumerDispatcher is bound ONCE per host surface (useMemo):
const dispatchConsumer = createConsumerDispatcher({
  bffBaseUrl,
  getSessionId,          // re-read per dispatch
  getAccessToken,        // Auth v2 / ADR-028 — never snapshotted
  publishPaneEvent,      // workspace-channel PaneEventBus publisher
});

// A Compose action invocation (once the user picks Explain / Draft / etc.):
await dispatchConsumer(bindingId, {
  slots: <selection payload>,   // forwarded VERBATIM as `args` to the server
  requiresAttachments,          // optional Click precondition
  attachmentCount,              // optional
});
```
Evidence: [`useConsumerChips.tsx:84-133`](../../../../src/solutions/SpaarkeAi/src/components/conversation/useConsumerChips.tsx).

- `bindingId` **MUST** be the `sprk_playbookconsumer` row GUID of the chosen Compose action's
  Binding (Phase 4 catalog rows — **core-A0-gated**; string-key resolution outside the Binding
  table is banned, ADR-039).
- `slots` (the selection args) is forwarded verbatim; **the server owns the typed parse** against
  the Action's `sprk_inputschema`. So the *exact slot keys* (e.g. `{ target_text, from, to,
  document_ref }`) are defined by each catalog Action's mirror-first input schema, **not** invented
  on the client. This is why the precise args shape is core-A0-gated — the transport is fixed, the
  payload vocabulary is per-Action.

### 3b. The selection-offer event (the real "compose toolbar click" surface)

The POML's `compose_action_request` is superseded by the R1-shipped **Flow 2** contract
([`compose-contracts.ts:235-266`](../../../../src/client/shared/Spaarke.Compose.Components/src/types/compose-contracts.ts)):

```ts
dispatch('conversation', {
  type: 'compose_selection_offer',   // additive discriminant on the `conversation` channel
  documentRef,                       // { speDriveItemId, sprkDocumentId?, fileName?, containerId? }
  selection,                         // { from, to, selectionText (≤2000, Tier-3), contextLabel? }
  jpsScope: 'compose-selection',     // R1 value; R2 may add scopes
  sessionId,
  timestamp,                         // ISO-8601
});
```
The editor emits this when a selection settles (debounced). The Assistant pane is the primary
subscriber; it renders the action menu (Explain / Compare / Draft alternative / …). Picking an
action is what triggers the **§3a dispatch call**. Insertion of an Assistant draft back into the
editor is **Flow 5** `workspace.compose_assistant_insert` (`compose-contracts.ts:429-494`,
R1-wired behind a user-confirm gate).

### 3c. Scope-payload assembly

There is **no separate "scope payload assembly" step** on the transport. The server assembles
execution context from `sessionId` (route) + `bindingId` (Binding row → Action + scope) + `args`
(verbatim slots). The client's only assembly duty is building the `selection`/`slots` object from
the ProseMirror selection (`from`/`to`/`selectionText`, capped ≤2000 chars, Tier-3 privacy — strip
from telemetry).

---

## 4. Assumption corrections that change Phase 1/3/4 (read before authoring those tasks)

1. **`compose_action_request` does not exist and must not be created.** Author against the six-flow
   contract in `compose-contracts.ts`. Impacted tasks: **030** (inline AI toolbar — emits Flow 2,
   does not invent a new event), **016** (draft-into-editor — consumes/produces Flow 5), **046**
   (dispatch wiring — the PaneEventBus → `dispatchConsumer` bridge; wire the *offer→dispatch* path,
   not an `compose_action_request` handler).
2. **The parallel Compose AI-dispatch endpoint is confirmed deleted.** `ComposeEndpoints.cs` maps
   only DOCX-lifecycle routes (upload/load/save/promote/checkout/checkin/heartbeat); there is **no**
   `/api/compose/action/{consumerType}`. The lone surviving reference is a **stale doc-comment** at
   [`IComposeService.cs:35`](../../../../src/server/api/Sprk.Bff.Api/Services/Compose/IComposeService.cs)
   — worth a one-line cleanup in a Phase-4/5 task, not load-bearing. Design §2.1/§7.2 premise holds.
3. **`compose-contracts.ts` carries stale R1 dispatch references** (`POST /api/compose/action/
   {consumerType}`, `IConsumerRoutingService.ResolveAsync(consumerType, jpsScope)` in its JSDoc,
   lines 45-48, 218-219, 253-255). The *type shapes* are correct and shipped; the *dispatch-path
   prose* predates the ADR-039 cutover. When Phase 3/4 wires these flows, dispatch via
   `dispatchConsumer(bindingId, …)`, and treat that JSDoc as historical. (Do not edit the contract
   file in a spike; flag for the task that touches it.)
4. **R7 LinearConsumers and the session-dispatch seam are the same routing surface, not rivals.**
   `ComposeEndpoints.cs:12` ("through the Assistant pane via R7 LinearConsumers") + the appsettings
   note ("ALL consumer-to-playbook/action routing is declared in the `sprk_playbookconsumer` table;
   the LinearConsumers config block was DELETED under the ADR-039 single-routing-surface rule") show
   both terminate at Binding resolution. There is one routing surface.

---

## 5. Acceptance criteria — disposition

| # | Criterion | Result |
|---|-----------|--------|
| 1 | End-to-end seam confirmed (yes/no + evidence) for a Compose Binding | ✅ **YES**, by static trace (§2). Live runtime confirmation deferred (§1 caveat, §6 recipe). |
| 2 | Exact PaneEventBus event shape a Compose interaction emits documented | ✅ Documented (§3b) — **corrected**: `conversation.compose_selection_offer` (Flow 2), NOT `compose_action_request`. Dispatch call shape in §3a. |
| 3 | SSE streaming into the chat surface observed + recorded | ⚠️ **Statically confirmed** (§2 rows: `text/event-stream`, shared `WriteSseChunkAsync`, client `readSseStream` bridge to `workspace.section_*`). Live frame observation deferred (§6). |
| 4 | Ledger `SessionOutput` write observed + recorded (ADR-040) | ⚠️ **Statically confirmed** (`SessionDispatchOrchestrator.cs:388-389`, render-from-stored `:143`). Live row observation deferred (§6). |
| 5 | Zero new BFF dispatch routes (ADR-039) — stated explicitly | ✅ **Zero.** Compose reuses the shipped unconditional `POST /api/ai/chat/sessions/{id}/dispatch`. No route added; the parallel Compose action endpoint is confirmed deleted (§4.2). |

Two criteria (3, 4) are marked ⚠️ because their *wiring* is confirmed but a spike in a headless
code session cannot *observe live frames/rows*. This is disclosed rather than overclaimed.

## 6. Recipe to close the runtime confirmation (run on `spaarkedev1` at Phase 1 start)

Requires a deployed BFF + one **throwaway** Compose Binding row (do NOT deploy to a shared catalog):
1. Author one mirror-first `sprk_analysisaction` (`compose-explain-clause`, minimal SystemPrompt +
   OutputSchemaJson) + one `sprk_playbookconsumer` Binding targeting it under
   `infra/dataverse/inputschemas/` (Phase 4 authoring — **core-A0-gated**; do not guess the
   triple-twin hoist shape until core A0 publishes).
2. Create a chat session; capture its `sessionId`.
3. `POST /api/ai/chat/sessions/{sessionId}/dispatch` with `{ bindingId: <throwaway GUID>,
   args: { <minimal slots> } }` (curl or the browser via a temporary `dispatchConsumer` call).
4. Confirm: (a) `text/event-stream` response with a terminal `complete` AnalysisChunk;
   (b) a `SessionOutput` addressed `{bindingId}@t{n}` in the session ledger BEFORE the terminal
   frame; (c) for a Tier-0/1 action the gate does not force a confirmation pause.
5. Delete the throwaway rows.

## 7. Why the POML steps were adapted (directional step-mode note)

POML steps 1-4 read "author a throwaway Binding, wire a stub toolbar button, run it, observe SSE +
ledger live." Under `<steps mode="directional">` the binding contract is the goal + acceptance
criteria, and the sequence is adaptable to the real codebase state. Two facts made literal
execution the wrong move:
- **Cannot run headlessly.** No live BFF/Dataverse/browser in this session; a "live observation"
  would be fabricated, not evidence. The honest confirmable artifact is the static trace + the
  runnable recipe (§6).
- **"Throwaway, no production code committed" constraint.** Committing throwaway Action/Binding
  rows + stub UI that can't be executed to completion would be repo noise that contradicts the
  spike's own constraint. The catalog authoring is also core-A0-gated (Phase 4).

The spike's **goal** — "is the seam confirmed end-to-end for a Compose Binding, and what are the
Compose-specific contracts?" — is fully met by the trace + the contract reconciliation, which also
surfaced a design-invalidating correction (§4) that literal button-wiring would have missed.

## 8. Gate (criterion 5 tail) — note

The confirmation gate (`PendingPlanManager.RequiresConfirmation`, referenced from the dispatch seam
in `ChatEndpoints.cs:1726` and `SessionDispatchOrchestrator`) lives *inside* the orchestrator, so it
applies uniformly to the dispatch path. Tier-0/1 non-interference is a **runtime** property (which
tier a given Binding maps to, and whether it trips confirmation) — confirm it with the §6 recipe;
it is not statically assertable from the transport wiring alone. Recorded here rather than claimed.
