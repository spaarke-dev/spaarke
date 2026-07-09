# Compose-Dispatch Foundation — Validation & Re-Plan

> **Date**: 2026-07-09 · **Author**: task-execute (main session) + 3 read-only validation sub-agents
> **Status**: VALIDATED — awaiting owner approval of the re-plan before editing TASK-INDEX / POMLs. No production code yet.
> **Trigger**: task 034 (FR-17 undo/replace) surfaced that compose actions cannot dispatch through the shared seam. Owner chose "re-plan the chain, but validate the issue + resolution across all scenarios / interactions / dependencies first."

---

## 1. Executive summary

**Compose AI dispatch is fully broken today.** No user-facing path (chip Click, LLM agent-loop Text, or `/dispatch`) can execute a compose Action and write a compose `SessionOutput`. Three independent validations (execution paths, executor input model, task/dep graph) converge on the same conclusion with file:line evidence.

The gap is **three stacked server gaps + two independent blockers**, not one:

| # | Gap | Evidence | Affects |
|---|-----|----------|---------|
| **A** | Dispatch **disposition gate** admits only `Informational`/`WorkProduct` → `Compose` (100000006) rejected 422 *pre-run*, before `OutputRouter` | `SessionDispatchOrchestrator.cs:224` | draft-alternative (042) — the compose-disposition action |
| **B** | Dispatch is **file-oriented**: resolves session files, hard-errors if none, reads only `fileIds`, builds `DocumentText` from file text — **no selection/args text path** | `SessionDispatchOrchestrator.cs:249-297, 336-341, 491-515` | **ALL 5** compose actions (3× `selectionText`, 1× `changesText`, 1× `documentText` — all args, none uploaded files) |
| **C** | **No server producer leg**: `ComposeDraftDisposition.BuildDraftOutput` has **zero production callers** — nothing runs a compose action → shapes the payload → routes it → emits the frame | grep: `ComposeDraftDisposition.cs:66` def only | the compose WRITE half (016 server half never built) |
| +gate | **ActionKind gate** rejects non-`Prompted` actions | `SessionDispatchOrchestrator.cs:209` | a *deterministic* `compose-retract` (undo) would be rejected here |
| +block | **ConsumerTypes not registered**: 5 compose types absent from `ConsumerTypes.cs`. Does NOT block dispatch resolution (`GetBindingByIdAsync` resolves by GUID, reads consumertype as free text), BUT the **boot-reconciliation health check** flips `/healthz` **Unhealthy** when Dataverse rows lack matching constants → **deploy gate** | `ConsumerTypes.cs:40-211`; `RoutingConsumerTypeHealthCheck.cs:253-256, 197-199, 501` | deploy (047) + flagship gate (082) |

**Why my earlier "replace is fully supported" was wrong**: I had checked only `OutputRouter` (the *store* seam — which I promoted 2026-07-09) and not `SessionDispatchOrchestrator` (the *dispatch* seam). The routing promotion made the store case correct but **unreachable** — the dispatch gate rejects Compose before routing.

**The resolution is smaller than feared** because it **reuses a shipped mechanism**: `PromptSchemaRenderer.Render` already accepts a `runtimeInput` that renders a `## Input` section, and the **playbook path already uses it** (`AiCompletionNodeExecutor` passes a node's `inputBinding` as `runtimeInput`). The dispatch→`ActionRunner` path simply never wired it. So the foundation forwards dispatch args → `ActionRunner` → `runtimeInput`, admits the Compose disposition, and shapes output via the existing `BuildDraftOutput` — not a from-scratch build.

---

## 2. All-scenarios coverage (does the resolution cover everything?)

| # | Scenario | Gaps it hits | Covered by | Notes |
|---|----------|--------------|------------|-------|
| 1 | **Draft-alternative** (compose disposition, edit-producing) | A + B + C | Foundation (018) | The flagship edit flow |
| 2 | **4 read-only compose actions** (explain/compare/summarize-word-changes/defined-terms — Informational) | **B** (they pass A) | Foundation (018 selection/args input) | ⚠️ These were assumed dispatchable; they are NOT — they'd get whole-file text or a no-file error instead of the selected clause |
| 3 | **Undo** (`compose-retract`, deterministic) | A + **ActionKind gate (209)** | 018 + a retract-write decision | OPEN: how a retraction writes (see §5) |
| 4 | **Replace** ("try another approach" = re-dispatch draft-alternative) | A + B + C | 018 | Works once 018 lands |
| 5 | **Entry paths**: Click / Text-loop | both hit the SAME orchestrator/gates | 018 | Agent loop `BindingCapabilityTool` → `DispatchAsync` (same seam). Event path is not a compose entry (compose is user-selection-triggered) |
| 6 | **Gate/confirm** (GateDecision, 055 push/save) | none from compose dispatch | unaffected | draft-alternative is risk=None → no gate fires on dispatch; push/save DOCX pipeline is separate |
| 7 | **Serial queue** (032) | ordered ledger writes unprovable until writes exist | buildable now; e2e proof after 018 | Serialization logic sound |
| 8 | **Deploy** (047) | dispatch smoke-test 422s + health gate | 018 + ConsumerTypes reg (019) | AC-4 must be re-scoped/gated |
| 9 | **ConsumerTypes health** | boot reconciliation Unhealthy | 019 (register 5 constants) | Second independent blocker |
| 10 | **Input-schema validation** | executor ignores args beyond fileIds today | 018 (args→runtimeInput) | Text path already projects the schema as an LLM tool + elicitation presence-check; executor consumption is the gap |

**Conclusion: the resolution (foundation 018 + registration 019 + re-scopes) covers every compose scenario.** The only unresolved sub-decision is the retract-write mechanism (§5), which is a design choice inside 018/034, not a coverage gap.

---

## 3. Corrected task / dependency graph

### New / reopened tasks

- **NEW 018 — "Compose dispatch execution leg (server producer)"** [FULL · opus · bff-api]. Make `SessionDispatchOrchestrator` execute compose bindings:
  1. Admit `BindingDisposition.Compose` in the disposition guard (`:224`) → route output through the existing `OutputRouter` compose case (`OutputRouter.cs:242`).
  2. Add a `selection`/`selectionText` (+ optional `documentText`/`changesText`) arg to `SessionDispatchRequest` + `DispatchSessionRequest`, parsed alongside `fileIds`; forward it to `ActionRunner` → `PromptSchemaRenderer.Render` as `runtimeInput` (the shipped `## Input` mechanism); relax the empty-`ExtractedText`/no-file hard-stop for selection-scoped dispatches.
  3. Wire `ComposeDraftDisposition.BuildDraftOutput` as the compose payload shaper (or confirm the Action's structured output matches `ComposeDraftPayload` and passes straight through `OutputRouter`); emit `ComposeDispositionFrame` on the SSE side channel after the ledger write.
  4. **This is the real body of FR-04 (016)'s server half** — reopen 016's BFF half or fold it here.
  - *Shared-seam caution*: `SessionDispatchOrchestrator` serves every consumer (Click, agent loop, gate-resolve, `/summarize`). Changes must be additive (compose-scoped branch), regression-tested against the informational/work-product paths. **Possible redesign-r2 (core) coordination** — see §6.
- **NEW 019 — "Register 5 compose ConsumerTypes constants"** [STANDARD · bff-api] (or fold into 047). Add the 5 constants + `ConsumerTypes.All` entries so boot reconciliation stays Healthy after 047 deploys the rows. Must merge **before/with** 047. ~10 lines but touches `.cs` (needs build).

### Re-scoped / reopened

- **046** dispatch-wiring — **mis-scoped**. Drop the "REUSE `SessionDispatchOrchestrator` unchanged" premise; keep the "zero new routes" guard; **add dependency on 018**. It remains client choreography (PaneEventBus discriminants + ConversationPane subscription), now sitting on a working seam.
- **016** draft-into-editor — **server half never built**. Reopen (or mark subsumed by 018). Its `blocks: 033,042,046` were treated as satisfied when only the client half + types shipped.

### Corrected edges

```
000 → 018 ─┬→ 016(server half closed / subsumed) ─┬→ 046 → 082
           ├→ 034 (undo/replace: needs compose WRITE + supersession)
           ├→ 042 runtime-verification (row authored; runtime proof needs 018)
           └→ 047 dispatch smoke-test (AC-4)
042(row authored) ──┘
030 ────────────────────────────────────────────→ 046
040,041,043,044 → 045 → (019 + 047) → 082
019 (ConsumerTypes) → 047 (health gate) → 082
055 → 082   (independent; own core-A0 GateDecision gate — unaffected)
032 → buildable now; end-to-end ledger-ordering proof waits on 018
```

### Genuinely done vs. blocked-on-foundation (status correction)

| Genuinely done (as authored / client artifact) | Blocked on foundation (018) / registration (019) |
|---|---|
| **033** client redline materialization (renders IF a ledger entry exists) | **016 server half** — the producer leg, never built |
| **042** Action+Binding *rows* (mirror-first, schema-valid) | **042 runtime** — undispatchable (422) until 018 |
| **045** eval cases (authoring artifact) | **034** — needs compose WRITE + supersession (018) |
| **061** action-history *query* (read-only) | **046** — mis-scoped; needs 018 + re-scope |
| **032** serial-queue logic (buildable) | **047** — smoke dispatch 422s + health gate (018 + 019) |

⚠️ **Honesty correction**: 016 and 042 were reported "done" but only their client/config halves exist; the runtime dispatch acceptance criteria were never exercised. This re-plan makes that explicit.

---

## 4. Evidence index (file:line)

- Disposition gate: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs:224`
- ActionKind gate: `…/SessionDispatchOrchestrator.cs:209`
- File requirement / no-file abort: `…/SessionDispatchOrchestrator.cs:249-297`
- File-only text build; args=fileIds only: `…/SessionDispatchOrchestrator.cs:336-341, 491-515`
- `RunAsync` call site: `…/SessionDispatchOrchestrator.cs:353-355`
- Executor accepts only `DocumentText`: `Services/Ai/LinearConsumers/IActionRunner.cs:32-36`; empty-text throw `ActionRunner.cs:75-79`; passes only file text `ActionRunner.cs:120-145`
- Shipped `runtimeInput`/`## Input` mechanism: `Services/Ai/PromptSchemaRenderer.cs:93, 224-239` (Document section `:242-248`)
- Proven consumer (playbook path): `Services/Ai/Nodes/AiCompletionNodeExecutor.cs:304-329, 616-644`
- OutputRouter compose case (store half, correct but unreachable): `Services/Ai/OutputRouter.cs:242-243, 391`
- Producer helper with zero callers: `Services/Compose/ComposeDraftDisposition.cs:66`
- All 5 input schemas (args, not files): `infra/dataverse/inputschemas/compose-*.input.schema.json`
- ConsumerTypes missing 5 constants: `…/ConsumerTypes.cs:40-211`
- Health-gate reconciliation: `…/RoutingConsumerTypeHealthCheck.cs:253-256, 197-199, 501, 36-41`
- Resolution by GUID (registration not needed for dispatch): `…/ConsumerRoutingService.cs:380, 851`
- Agent-loop uses same seam: `Services/Ai/Chat/BindingCapabilityTool.cs:210` (schema projection `:109,122`; args build `:197-204`)
- Retired parallel compose endpoint: `Api/ComposeEndpoints.cs:10-12`

---

## 5. Open design questions (to resolve within the re-plan, not blockers to it)

1. **Retract-write mechanism (undo, FR-17 Path B).** A deterministic `compose-retract` hits the ActionKind gate (`:209`). Options: (a) widen the ActionKind gate to admit a deterministic compose-supersede action dispatched through the seam; (b) model retract as a minimal prompted no-op (wasteful/odd catalog citizen); (c) a compose-scoped supersession write that rides 018's producer leg without being a full "action." Recommend deciding this **inside 018/034 design**, after the foundation shape is fixed. Owner already chose Path B (durable undo) — this is the *how*.
2. **Args→input mapping convention.** Treat a designated arg field as the `## Input` `runtimeInput` (cleanest, mirrors playbook path) vs. synthesize `DocumentText.ExtractedText` from a `documentText` arg. Recommend the `runtimeInput` convention for all 5.
3. **016 disposition — reopen vs. subsume.** Cleaner to fold 016's server half into 018 and mark 016 "superseded-by-018" than to reopen 016 piecemeal.

---

## 6. Ownership / coordination note

`SessionDispatchOrchestrator` is the **shared** dispatch seam (redesign-r1/r2 core territory), consumed by every capability. Compose-r2 already took on the `OutputRouter` compose promotion (with redesign-r2's documented approval). Task 018 modifies the same shared seam. Two options:
- **(a) Compose-r2 owns 018** (tightly coupled to compose's selection-input + payload needs; additive compose-scoped branch; regression-tested) — faster, keeps the chain in one worktree.
- **(b) Coordinate with redesign-r2 (core)** since the seam is shared and the change is arguably the unbuilt remainder of core task 010.
- **Recommendation**: Compose-r2 authors 018 as an additive compose-scoped branch with full regression coverage of the informational/work-product paths, and files a coordination note to redesign-r2 (as was done for the OutputRouter promotion). Escalate to core only if the change can't be kept additive.

---

## 7. Recommended next step

Owner approves this re-plan → main session updates `TASK-INDEX.md` + `plan.md` + the affected POMLs (add 018/019, re-scope 046, reopen/subsume 016, correct 034/042/047 deps + status) → THEN execute 018 (foundation) under normal task-execute FULL rigor with its own gates. **No production code until the re-plan edits are approved.**
