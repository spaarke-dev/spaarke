# AI Guide — Wiring a New Capability (Action + Binding)

> **Audience**: Makers + AI engineers shipping a new AI capability onto the Spaarke platform.
> **Author**: spaarke-ai-architecture-redesign-r1 task 052 (FR-P4-03)
> **Last Updated**: 2026-07-07
> **Status**: Current — the canonical capability-wiring tutorial for the Action + Binding catalog model (ADR-039).
>
> ⚠️ **Supersedes the R7 "Wiring a New Consumer" content** that previously lived at this path. That guide taught the `ConsumerTypes` + `ResolveAsync` + `IInvokePlaybookAi` triangle for wiring a code surface to a playbook. Under the redesigned architecture, most capabilities ship as **catalog data with ZERO new code**: an Action row + a Binding row. Even the R7 worked example (chat-summarize) is now just another catalog capability — Action `SUM-CHAT@v1` behind the `chat-summarize` Binding, dispatched like everything else. The `ConsumerTypes` constants + `IConsumerRoutingService.ResolveAsync` still exist for code-side consumers (Matter pre-fill, Document Profile, Insights) and for the FR-P0-04 constants↔rows boot parity check, but they are no longer how you add a chat/loop capability.

---

## What this guide covers

You want the Spaarke assistant (or a chip, ribbon, wizard, or platform event) to be able to do a new thing — "extract key dates", "draft a status memo", "create a follow-up task". This guide walks you through shipping that as **catalog data**:

1. Author the **Action** row (`sprk_analysisaction`) — the execution unit: WHAT runs.
2. Author the **Binding** row (`sprk_playbookconsumer`) — the invocation unit: WHEN/HOW it is offered and what happens to its output.
3. Understand how the **three entry paths** (Event, Click, Text) reach your Binding with no per-capability code.
4. Handle **side effects** through the ONE confirmation gate.
5. Add a **golden-utterance eval case** — the merge gate.

What this guide does **not** cover:
- The loop/gate/ledger runtime internals → [`docs/architecture/chat-architecture.md`](../architecture/chat-architecture.md)
- Authoring multi-node playbooks (`sprk_analysisplaybook`) → [`PLAYBOOK-AUTHOR-GUIDE.md`](PLAYBOOK-AUTHOR-GUIDE.md)
- Coded composite workflows (`sprk_kind = coded`, `ICodedWorkflow` — e.g. Daily Briefing) — those DO require code; this guide notes where they diverge.

---

## §1. The model: Action + Binding

| Unit | Table | Owns | Analogy |
|---|---|---|---|
| **Action** (execution unit) | `sprk_analysisaction` | `sprk_kind` (`prompted` \| `coded`), the system prompt / JPS, the output schema, `sprk_inputschema` (typed argument contract), default model tier | "the function body" |
| **Binding** (invocation unit) | `sprk_playbookconsumer` | consumer type, `sprk_action` lookup → the Action, tool description, disposition, chips, risk, capture mode, event memberships, surfaces | "where the function is callable from, and what happens to its return value" |

**The Binding table is THE only routing surface on the platform** (ADR-039). Every entry path resolves through it; there is no second routing contract, no tool-name allow-lists, no config-file playbook maps. The full typed contract is [`Services/Ai/PublicContracts/Binding.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/Binding.cs) — read it once; every column you author below maps to a property there.

One Action can be targeted by several Bindings (e.g. an "informational render" Binding and an "email disposition" Binding for the same Action — Daily Briefing does exactly this).

---

## §2. Step 1 — Author the Action row (`sprk_analysisaction`)

| Column | What to put there |
|---|---|
| `sprk_name` / action code | Versioned code, e.g. `EXTRACT-DATES@v1` (existing examples: `SUM-CHAT@v1`, `CREATE-TASK@v1`, `DRAFT-CORR@v1`, `REF-CHAT@v1`) |
| `sprk_kind` | `prompted` (JPS prompt run by ActionRunner + PromptSchemaRenderer — the default, and what this guide assumes) or `coded` (a registered `ICodedWorkflow` C# class named in `sprk_workflowclass` — requires code; see Daily Briefing, task 043) |
| System prompt / JPS | The prompt content executed by the prompted executor. See [`JPS-AUTHORING-GUIDE.md`](JPS-AUTHORING-GUIDE.md) |
| Output schema | The structured-completion schema the executor enforces (mirrors live in `infra/dataverse/outputschemas/`) |
| `sprk_inputschema` | The typed argument contract — read the rules below **before** authoring |

### 2.1 `sprk_inputschema` rules (G-P3 UAT hard lesson — read this)

The input schema is a JSON-Schema object that becomes the projected tool's `function.parameters` when the agent loop offers your capability. Azure OpenAI validates **every** known JSON-Schema keyword in **every** projected tool schema and rejects the **entire request** (`invalid_function_parameters`) if any one is malformed.

**The binding rule**: declare required-ness ONLY via the **object-level `required` array**. NEVER put `"required": true` inside a property definition. The G-P3 UAT round-1 incident (2026-07-07): one `CREATE-TASK@v1` row with property-level `"required": true` 400-failed **every text-path turn on the tenant** — every capability, not just create-task ([`notes/g-p3-uat-round1-findings.md`](../../projects/spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round1-findings.md) finding 1).

```jsonc
// ✅ CORRECT — object-level required array only
{
  "type": "object",
  "properties": {
    "due_date": {
      "type": "string",
      "description": "The task's due date as the user stated it (e.g. 7/9/2026).",
      "elicitation_prompt": "What's the due date for this task?"
    },
    "assign_to": {
      "type": "string",
      "description": "Who the task is assigned to — 'me' or a person's name.",
      "elicitation_prompt": "Should I assign it to you or someone else?"
    }
  },
  "required": ["due_date", "assign_to"]
}

// ❌ WRONG — poisons EVERY loop turn, not just this tool
// "due_date": { "type": "string", "required": true }
```

Custom keywords like `elicitation_prompt` are tolerated (OpenAI ignores unknown keywords; the platform's elicitation reads them). Since the G-P3 fix, [`OpenAiFunctionSchemaValidator`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/OpenAiFunctionSchemaValidator.cs) excludes an invalid schema's tool at projection time (so one bad row can no longer take down the loop), flags the row via health check (Degraded), and emits `ai.tool.schema_invalid` telemetry — but the excluded tool is still **dead** until you fix the row. Author it correctly.

### 2.2 Author the schema mirror FIRST (CI-validated)

Author your input schema in [`infra/dataverse/inputschemas/`](../../infra/dataverse/inputschemas/) as `{action-code}.input.schema.json` **before** writing it to Dataverse. The mirrors are CI-validated by `tests/integration/contract/Catalog/CatalogInputSchemaContractTests.cs` against the OpenAI function-parameters subset (property-level boolean `required` is explicitly banned). Workflow: author mirror → CI green → copy into the Dataverse row. See [`create-task-v1.input.schema.json`](../../infra/dataverse/inputschemas/create-task-v1.input.schema.json) for the annotated reference shape.

---

## §3. Step 2 — Author the Binding row (`sprk_playbookconsumer`)

| Column | Meaning | Authoring guidance |
|---|---|---|
| `sprk_consumertype` | Stable lower-kebab-case capability key (e.g. `create-task`) | Also add it to `ConsumerTypes.All` in [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs) — the FR-P0-04 boot reconciliation diffs constants against live rows |
| `sprk_action` | Lookup → your Action row | Required for dispatch-path execution; a Binding with no Action target is rejected `dispatch.action-kind-unsupported` |
| `sprk_tooldescription` | **The intent surface the agent loop sees.** This text becomes the projected tool's `description` — it is what the model reads when deciding whether your capability matches the user's utterance. | **Treat it as prompt engineering.** Say what the capability does, when to use it, and what it does NOT do. A vague description = mis-dispatch; an empty one = the Binding is NOT text-projectable at all (non-empty `sprk_tooldescription` is the maker's explicit opt-in to the Text path, ADR-039) |
| `sprk_disposition` | What happens to the output: `informational` \| `work_product` \| `overlay` \| `email` \| `record` \| `notification` | The dispatch path currently executes `informational` and `work_product`; other legs reject pre-run with stable errors until their OutputRouter legs land |
| `sprk_chiptransitions` | Curated next-step chips: `[{"target_binding_id": "<binding row GUID>", "chip_label": "Summarize again"}]` (optional: `bulk_chip_label`, `requires_attachments`, `prefill_slots`) | Emitted after every successful dispatch of this Binding so the chip strip always shows current next steps |
| `sprk_risk` | `none` \| `confirm-when-uncertain` \| `always-confirm` | Binding-level confirmation posture; tool-level `sprk_sideeffectclass` gating (§5) applies independently |
| `sprk_capturemode` | `loop-elicitation` (default) or `modal` | How missing required args are collected (§6) |
| `sprk_oneventbindings` | Event-path memberships: `[{"event": "document_uploaded", "order": 2}]` | Membership in a platform event's ordered composite (§4.1) |
| `sprk_surfaces` | Comma-separated surface tokens (`assistant`, `record-form`, `wizard`, `office`, …) | Empty = offered on ALL surfaces; tool projection filters on the session's surface |
| `sprk_modeltieroverride` | Optional per-Binding model tier (`fast` \| `standard` \| `reasoning`) | Overrides the Action's default tier |

Create the row in the Power Apps Maker portal, via `mcp__dataverse__create_record`, or extend `scripts/dataverse/Seed-PlaybookConsumers.ps1` (idempotent seed).

---

## §4. Step 3 — How the three entry paths reach your Binding

You do **not** wire the entry paths — they already exist and read the catalog. Authoring the columns above IS the wiring.

### 4.1 Event path — `sprk_oneventbindings`

When a platform event fires (e.g. `document_uploaded` on a chat-session file upload), the Event Rules service (`Services/Ai/EventRules/EventRulesService.cs`) calls `IConsumerRoutingService.ResolveEventBindingsAsync(event)` and executes the member Bindings in `order`. Shipped example: `document_uploaded` runs `chat-classify` (order 1, `CLS-CHAT@v1` Layer-0 classification) then `chat-summarize` (order 2, `SUM-CHAT@v1`). To join an event composite, add a membership entry to your Binding's `sprk_oneventbindings` — nothing else.

### 4.2 Click path — dispatch by Binding id (zero LLM)

Chips, ribbon buttons, and wizard actions carry your **Binding row GUID** and POST it to:

```
POST /api/ai/chat/sessions/{sessionId}/dispatch
{ "bindingId": "<sprk_playbookconsumer row GUID>", "args": { "fileIds": ["..."] } }
```

([`Api/Ai/DispatchSessionEndpoint.cs`](../../src/server/api/Sprk.Bff.Api/Api/Ai/DispatchSessionEndpoint.cs); client helper: `@spaarke/ui-components` `dispatchConsumer(bindingId, args)`.) The id IS the routing decision — no intent detection, no LLM call. [`SessionDispatchOrchestrator`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs) resolves the Binding by id (`GetBindingByIdAsync`), loads the Action, executes it via the prompted executor (ActionRunner + PromptSchemaRenderer), ledger-writes the output (`{bindingId}@t{n}`) BEFORE the terminal render chunk (ADR-040), then emits your `sprk_chiptransitions` as next-step chips. Unknown/disabled ids get clean stable errors (`dispatch.binding-not-found` 404, `dispatch.action-kind-unsupported` / `dispatch.disposition-not-supported` 422) — no fallback.

### 4.3 Text path — the bounded agent loop

Every NL utterance enters the agent-turn loop, which projects each text-projectable Binding (non-empty `sprk_tooldescription` + surface match) as a tool named `capability_{consumertype}` ([`BindingCapabilityTool`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/BindingCapabilityTool.cs)). The model choosing your tool IS the dispatch decision; invocation delegates to the SAME `SessionDispatchOrchestrator.DispatchAsync` seam as the Click path, so ledger, disposition routing, and chips behave identically. The catalog is closed — the model can only pick projected tools; off-catalog requests route to the `no_match_handler` refusal Binding (`REF-CHAT@v1`).

**Important honesty contract**: a `capability_*` tool call only GENERATES content (a draft stored to the session ledger). It does not create/send/save anything — writes go through typed tools under the confirmation gate (§5). The loop's system prompt pins this (`SideEffectHonestyDirective`, G-P3).

---

## §5. Side effects — the ONE confirmation gate

If your capability's flow ends in a real side effect (create a record, send/draft a communication), that side effect executes through a **typed tool** (`sprk_analysistool` row, e.g. `dataverse.create_record`, `email.draft`) whose row DECLARES `sprk_sideeffectclass` = `write` or `communicate`. Declared side-effecting tools never execute directly from the loop:

1. [`SideEffectGateAIFunction`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SideEffectGateAIFunction.cs) wraps the tool at projection time (keyed EXCLUSIVELY on the declared class — never tool-name lists, ADR-039) and, on invocation, SUSPENDS it into the unified pending store ([`PendingPlanManager`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PendingPlanManager.cs)) — ledger marker first (ADR-040), then an `action_confirmation` SSE event renders the confirmation dialog. Fail-closed: if the store is unavailable, the tool is refused, never executed.
2. The user confirms or rejects via:
   ```
   POST /api/ai/chat/sessions/{sessionId}/gates/{gateId}/resolve
   { "approved": true | false }
   ```
   On confirm, [`TypedHandlerResumeExecutor`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/TypedHandlerResumeExecutor.cs) executes the suspended handler under the **confirming user's OBO scope**, ledger-writing `loop@t{n}` SessionOutput + ToolChain before rendering. Handler failures return **422** with stable errorCode `gate.dispatch-failed` (plus a `dispatch-failed` gate marker and an honest ❌ transcript message); success persists a ✅ transcript message — both so the next turn's model knows the real outcome.

Authoring impact for you: declare the correct `sprk_sideeffectclass` on any new `sprk_analysistool` row, and write your Binding's `sprk_tooldescription` to instruct the draft→confirm→write flow (see the create-task tool description for the pattern). You never build a confirmation UI.

---

## §6. Elicitation — missing required args

If the model invokes your capability without the `required` args your Action's input schema declares, `BindingInputSchemaValidator` catches it BEFORE dispatch and suspends into an elicitation gate instead of executing with guessed values (FR-P2-03). What happens next depends on the Binding's `sprk_capturemode`:

- **`loop-elicitation`** (default) — the model asks a clarifying question in chat, using each property's `elicitation_prompt`; the re-invocation with completed args resumes through the same gate.
- **`modal`** — an `elicitation_modal` SSE event routes the user to a wizard/form surface; the completed args come back through the dispatch endpoint, which resolves the pending gate.

Never mark system-supplied properties as required (the DAILY-BRIEFING lesson: a required `briefingPayload` would make the loop ask the USER for an internal payload).

---

## §7. Step 4 — Add a golden-utterance eval case (merge gate)

Every catalog change adds-or-updates eval cases (NFR-06). Edit the fixture
[`tests/integration/contract/Eval/golden-utterances.json`](../../tests/integration/contract/Eval/golden-utterances.json) — JSON only, no code:

```jsonc
{
  "caseId": "GU-0XX",
  "family": "your-capability",
  "ucId": "UC-…",                     // §3 trigger in the canonical design doc
  "channel": "text",                   // text | click | event
  "utterance": "extract the key dates from this contract",
  "context": { "surface": "assistant", "sessionHasDocument": true },
  "expected": { "outcomeClass": "dispatch", "consumerType": "your-capability" }
}
```

Your `consumerType` must appear in `ConsumerTypes.All` (or be marked `catalogStatus: "planned"` citing the introducing FR). The suite runs as the **blocking `eval-gate` CI job** (`Category=GoldenUtteranceEval`, no `continue-on-error`) — eval green is a merge gate per spec NFR-02. Full case schema + phase activation rules: [`tests/integration/contract/Eval/README.md`](../../tests/integration/contract/Eval/README.md).

---

## §8. Worked example — the shipped create-task capability (FR-P3-03)

The reference implementation of everything above, shipped by task 042 and hardened through three G-P3 UAT fix waves:

| Piece | Value |
|---|---|
| Action | `CREATE-TASK@v1` (`sprk_kind = prompted`) — drafts a well-formed follow-up task proposal grounded in session documents + ledger outputs |
| Input schema | `due_date` + `assign_to` in the object-level `required` array, each with an `elicitation_prompt` — mirror at [`infra/dataverse/inputschemas/create-task-v1.input.schema.json`](../../infra/dataverse/inputschemas/create-task-v1.input.schema.json) |
| Binding | `sprk_consumertype = create-task`; tool description instructs draft→confirm→write; projected as `capability_create-task` |
| Elicitation | "create a follow-up task" with no due date → loop asks "What's the due date for this task?" (loop-elicitation capture mode) |
| Write leg | The EXISTING `dataverse.create_record` typed tool (declared `sprk_sideeffectclass = write`) creates `sprk_event` with `sprk_eventtype_ref = Task`, carrying provenance refs (source document + source analysis `{bindingId}@t{n}`) — suspended by the gate, executed on confirm by `TypedHandlerResumeExecutor` under the confirming user's OBO |
| Eval | `capability_create-task` projection, elicitation contract, and the suspend → confirm → real-handler-execution walk are pinned in the eval suite (GU-051/052 + `CreateTask_ConfirmedWriteInvocation_*`) |

End-to-end user experience: "create a follow-up task from this letter" → capability drafts a proposal (ledger-stored) → model asks for due date/assignee if missing (at most once) → user confirms → the write tool suspends into the confirmation dialog → user clicks Confirm → record created under their identity → ✅ outcome message in the transcript.

---

## §9. Troubleshooting

| Symptom | Likely cause |
|---|---|
| Capability never offered on the Text path | `sprk_tooldescription` empty (not text-projectable), or `sprk_surfaces` excludes the session's surface, or the input schema is invalid (check health check Degraded status + `[invalid-tool-schema]` error logs + `ai.tool.schema_invalid` telemetry) |
| Model picks the wrong capability | Tool descriptions overlap/vague — sharpen the intent surface text; it is prompt engineering |
| Chip click → 404 `dispatch.binding-not-found` | Chip carries a wrong/disabled Binding GUID; `sprk_chiptransitions.target_binding_id` must be the row GUID |
| 422 `dispatch.action-kind-unsupported` | Binding has no `sprk_action` lookup, or targets a `coded` Action on the prompted-only dispatch envelope |
| Every loop turn fails 400 `invalid_function_parameters` | Should no longer happen (projection-time validation) — but on an old build, a malformed input schema anywhere in the catalog. Check for property-level `"required": true` |
| Confirmed write "vanishes" | Check the 422 `gate.dispatch-failed` ProblemDetails detail + the ❌ transcript message — handler validation / Dataverse rejection, correctable payload problem |
| Routing changes don't take effect | Binding resolution is cached (~5 min); restart the BFF or wait |

---

## §10. See also

| Document | Why |
|---|---|
| [`docs/architecture/chat-architecture.md`](../architecture/chat-architecture.md) | The agent-turn loop, gate, ledger, and SSE runtime this guide's capabilities execute in |
| [`Services/Ai/PublicContracts/Binding.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/Binding.cs) | The typed Binding contract — authoritative column semantics + safe defaults |
| [`tests/integration/contract/Eval/README.md`](../../tests/integration/contract/Eval/README.md) | Eval case schema, BA workflow, merge-gate wiring |
| [`JPS-AUTHORING-GUIDE.md`](JPS-AUTHORING-GUIDE.md) | Authoring the prompted Action's JPS content |
| [`PLAYBOOK-AUTHOR-GUIDE.md`](PLAYBOOK-AUTHOR-GUIDE.md) | Multi-node playbooks (engine-target Bindings) |
| Root [`CLAUDE.md`](../../CLAUDE.md) §10 + §11 | BFF Hygiene + Component Justification — apply if your capability genuinely needs new code (typed handler, coded workflow) |
| [`projects/spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round1-findings.md`](../../projects/spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round1-findings.md) (+ round2/round3) | The UAT evidence behind the schema rules, honesty directives, and gate-outcome contracts cited here |

---

*Rewritten 2026-07-07 by spaarke-ai-architecture-redesign-r1 task 052 (FR-P4-03). The R7 consumer-wiring tutorial this replaces described the pre-redesign `ConsumerTypes`/`ResolveAsync`/`IInvokePlaybookAi` pattern; code-side consumers still using that triangle are documented inline in `ConsumerTypes.cs`.*
