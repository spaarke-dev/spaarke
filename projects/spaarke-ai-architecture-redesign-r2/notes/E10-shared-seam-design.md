# E-10 — Shared input-resolution seam design (anti-recurrence proof)

> Task: AIR2-E10 (Phase E Move 1). ADR-043. Author: task-execute (opus @ xhigh), 2026-07-09.
> Purpose: PROVE the `ContextBinder` + `## Input` producer covers ALL THREE completion
> consumers BEFORE cutting over `ActionRunner` — so the "shared" seam is not fitted to the
> first consumer and reshaped later (the exact disease Phase E cures). Required by the POML
> `design-against-ALL-consumers` constraint.

---

## 1. The three completion consumers (read in code, 2026-07-09)

| # | Consumer | Today's operand mechanism (verified) | Rendered as |
|---|---|---|---|
| 1 | `ActionRunner` (canonical dispatch engine) — **this task's cutover** | `RunAsync(action, DocumentText, ...)` → `PromptSchemaRenderer.Render(documentText: doc.ExtractedText)` | `## Document` (JPS) / flat-text `## Input` header w/ "Document:" (flat). No structured operand at all — the Action's declared `selectionText`/`changesText`/`ledger_resolution` are **accepted and ignored** (`SessionDispatchOrchestrator` reads only `fileIds`). |
| 2 | `AiCompletionNodeExecutor` (playbook node engine) — **E-12 migrates** | `ExtractInputBindingAsJsonElement(configJson)` → `JsonElement` → `PromptSchemaRenderer.Render(runtimeInput: element)` | `## Input` (renderer Layer 2, JPS path) |
| 3 | `DailyBriefingNarrator` (coded workflow) — **E-12 retires replica** | typed `TldrFactsDto` → `JsonSerializer.Serialize(payload, camelCase+WriteIndented)` → `systemPrompt + "\n\n## Input\n\n" + inputJson + "\n"` (HAND-REPLICATES the renderer's `## Input`; its Action is **flat-text**, so it does NOT call the renderer) | `## Input` (hand-rolled) |

**Common denominator (the invariant the seam must fit):** every consumer's operand is (or reduces to)
**a `JsonElement` object rendered as a `## Input` section** — EXCEPT consumer 1's *file-grounding* document,
which is a large text blob rendered as `## Document`. Two channels, both owned by the ONE
`PromptSchemaRenderer`.

Critical facts that shape the seam:
- **`AnalysisAction` carries NO input schema.** The declared operand vocabulary lives on
  `Binding.InputSchemaJson` (mirror of `sprk_analysisaction.sprk_inputschema`), surfaced RAW and
  parsed at point-of-use (`BindingInputSchemaValidator`). Compose actions declare exactly one operand
  field each: `selectionText` (explain-clause, compare-to-playbook), `changesText`
  (summarize-word-changes), `documentText` (defined-terms).
- **Compose actions are FLAT-TEXT** (`compose-explain-clause.action.json` systemPrompt starts `ROLE: …`,
  not `{`/`$schema`). So the `## Input` producer MUST work for flat-text actions, not only JPS — else
  it does not fit consumer 3 (also flat-text) and would force reshaping in E-12. **This is the load-bearing
  anti-recurrence finding.**
- **Shipped SUM-CHAT@v1 is JPS and renders its file text under `## Document`.** Its input schema declares
  `fileIds` + `styleHint` — NEITHER is in the structured-operand vocabulary — so it deterministically
  takes the file/`## Document` branch (non-regression preserved).

---

## 2. The seam (one resolver + one producer + two channels)

```
                         ┌────────────────────────────────────────────┐
   Action's declared     │  ContextBinder.BindAsync(request)           │
   inputs + runtime  ───▶│   ├─ resolve grounding CONTEXT → ContextEnvelope (frozen task-015, unchanged)
   value sources         │   ├─ resolve the OPERAND → ResolvedOperand { Channel, Kind, Document|Input }
                         │   └─ write ContextEnvelope fingerprint (AppendContextFingerprintAsync, NFR-07)
                         └───────────────┬────────────────────────────┘
                                         │ BoundInputs { Context, Operand, Fingerprint }
                                         ▼
                    ┌────────────────────────────────────────────┐
                    │  Completion engine renders the operand via  │
                    │  the SINGLE-SOURCE `## Input` producer      │
                    │  = PromptInputSection.Render(JsonElement?)  │
                    └────────────────────────────────────────────┘
                          ▲              ▲                    ▲
       consumer 1 (E-10)  │  consumer 2  │ (E-12)  consumer 3 │ (E-12)
       ActionRunner       │  node engine │         narrator   │
```

### 2a. `ResolvedOperand` — two channels, one renderer
- **`Document` channel** → `PromptSchemaRenderer.Render(documentText: …)` → `## Document`. Home of the
  large file-grounding blob. **This is the shipped summarize path, byte-for-behavior unchanged.**
- **`Input` channel** → `PromptInputSection.Render(JsonElement)` → `## Input`. Home of the volatile,
  per-action-typed structured operand (compose args, node `inputBinding`, narrator payload, `ledger_resolution`).

The POML/ADR enumerate `documentText` among the `## Input` operands; the operator non-regression constraint
*and* the POML `context-vs-operand` constraint explicitly say **"the file path resolves to a document-operand
(unregressed)"** → a *file-sourced* document renders `## Document`; an *args-sourced* `documentText`
(compose-defined-terms) renders `## Input` as `{ "documentText": … }`. Both are `PromptSchemaRenderer`
sections → the "single-source producer / Layer 2" contract holds. This is a documented reconciliation, not
a silent deviation (see §5).

### 2b. `PromptInputSection.Render` — the FROZEN `## Input` producer
Frozen format: `"## Input\n\n" + JsonSerializer.Serialize(element, WriteIndented) + "\n"` (LF-normalized,
deterministic across platforms). Golden-string test in `tests/integration/seam/**` fails the build on any
drift. `PromptSchemaRenderer.RenderJps` is refactored to call it (byte-identical on CI/Linux; the node
engine keeps working) so there is literally ONE code path producing `## Input`.

### 2c. How EACH consumer maps onto the seam (the proof)
| Consumer | Operand source into `ContextBinder`/`PromptInputSection` | Result | Reshape needed later? |
|---|---|---|---|
| 1 ActionRunner (E-10) — compose args | `request.Args = { selectionText }` + `Binding.InputSchemaJson` → `ResolvedOperand.Input = { selectionText }` | flat-text action → `systemPrompt + PromptInputSection.Render(op)`; JPS action → renderer `runtimeInput` | NO |
| 1 ActionRunner (E-10) — summarize file | `request.FileDocument = DocumentText` → `ResolvedOperand.Document` | renderer `documentText:` → `## Document` (unchanged) | NO |
| 1 ActionRunner (E-10) — ledger_resolution | `request.Args = { ledger_resolution: { key } }` + `request.LedgerOutputs` → resolve prior `SessionOutput.Payload` → `Input` | `## Input` (referenced payload) | NO |
| 2 node engine (E-12) | `request.PreResolvedInput = inputBinding JsonElement` → `Input` (pass-through) | renderer `runtimeInput` → `PromptInputSection.Render` — **byte-identical to today by construction (same helper, same element)** | NO |
| 3 narrator (E-12) | `request.PreResolvedInput = serialize(payload, camelCase) → JsonElement` → `Input` | `PromptInputSection.Render` == today's hand-rolled `## Input` (format frozen + golden-pinned) | NO |

`ContextBindingRequest` carries a `PreResolvedInput` slot **now** (exercised by a ContextBinder unit test
that feeds a node-engine-shaped `inputBinding` object) precisely so E-12 is a call-site swap, not an
abstraction reshape. The seam is fitted to the UNION of all three, not to ActionRunner.

---

## 3. Context vs operand (POML `context-vs-operand` constraint)
- **CONTEXT** (who/what/where — stable prefix) → `ContextEnvelope` slices (User/Workspace/Business/Memory/…),
  the **frozen task-015 contract, redefined by NOTHING here**. `ContextBinder` assembles it via the existing
  `ContextEnvelopeReferenceProducer`. For the dispatch path today the only populated slice is
  `Memory.Conversation` (references to the session's prior `SessionOutput`s — ADR-040 facade, references not
  payloads). Host-record/schema context resolution into the envelope is **E-11+** (dispatch resolves none
  today; populating it would regress the summarize prompt).
- **OPERAND** (the thing acted on — volatile, per-action-typed) → the `## Input`/`## Document` channel, NOT an
  envelope slice. The envelope stays context-only and unchanged (no v1.1).

**Scope boundary (explicit, not a dead-end):** the envelope's *observable end* in E-10 is the **fingerprint**
(`AppendContextFingerprintAsync`, whose read projection is already LIVE → surfaces in the trace `Context`
event). Rendering context slices *into the LLM prompt* is the future "prompt assembler" the envelope's own v1
docs reserve — it is not what compose-B2 needs (compose is grounded by its operand, not by host context), so
nothing is left half-wired. ActionRunner receives the envelope (logs its NFR-07 presence summary — proves the
context flows to the executor) and renders the operand.

---

## 4. Non-regression (summarize file path)
The Document-operand path in `ActionRunner` is the **verbatim current `BuildPrompt`** (JPS → `## Document`;
flat → `## Input` "Document:" append; placeholder substitution). The legacy `RunAsync(action, DocumentText, …)`
overload (8 existing callers: EventRules, RefusalTool, WorkspaceFileEndpoints ×2, AnalysisEndpoints,
Matter/ProjectPreFill) wraps `DocumentText` into a `Document` operand and calls the ONE core path — byte-identical.
Only `SessionDispatchOrchestrator` (the "wired path") moves to `ContextBinder`; for a summarize dispatch it
resolves no structured operand → file branch → `Document` operand → identical prompt → identical SSE output.
The added fingerprint write is additive ledger/trace state, not a change to the SSE output contract.

## 5. ADR reconciliation (surfaced per CLAUDE.md §6.5 — NOT an escalation)
`## Document` for the file-grounding operand vs the ADR's "operand → `## Input`" enumeration is reconciled by
the POML's own `context-vs-operand` constraint ("the file path resolves to a document-operand (unregressed)")
and the hard non-regression constraint. Path A (documented reconciliation): the structured/volatile operand's
home is `## Input`; a large file-grounding document renders `## Document` — both are `PromptSchemaRenderer`
sections (one producer). No format change to `## Input` (escalation trigger 3 not fired); no summarize-path
regression (trigger 2 not fired); the three consumers share one seam (trigger 1 not fired).
