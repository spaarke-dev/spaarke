# Wave 11 Design — Playbook Orchestrator Runtime Variable Resolution

> **Task**: 110 (Wave 11 — audit + design)
> **Authored**: 2026-06-29
> **Rigor**: STANDARD (audit + planning doc, no source modification)
> **Status**: ✅ Design complete; T111-T114 execute against it

---

## Why this design exists

Wave 10 task 100 marked 15 success criteria GREEN at the verification-report level. Wave 10 task 101 (UAT) discovered `/narrate` returns HTTP 200 with empty `summary` / `keyTakeaways[]` / `channelNarratives[]`. Root cause: the orchestrator's `ApplyConfigJsonTemplates` only does literal `{{paramName}}` string replacement against `runContext.Parameters` — it does NOT resolve the richer expressions (`{{json start}}`, `{{tldrResult.summary}}`, `{{map}}`, `{{flatten}}`, `{{distinct}}`, `{{concat}}`, `{{join}}`, fan-out iteration, inline lambda) the deployed `DAILY-BRIEFING-NARRATE` playbook uses. Wave 11 closes that gap. T110 is the audit + design; T111-T114 implement against it.

---

## Audit findings (Step 2-5 of T110)

### Finding 1: NodeOutputs surface already exists — narrower work than first thought

`PlaybookRunContext` ([src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookRunContext.cs:30](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookRunContext.cs#L30)) ALREADY has a `ConcurrentDictionary<string, NodeOutput> _nodeOutputs`, exposes it as `IReadOnlyDictionary<string, NodeOutput> NodeOutputs` (line 167), and the orchestrator already calls `runContext.StoreNodeOutput(output)` after each node executes. **`CreateNodeContext` already passes them to executors via `NodeExecutionContext.PreviousOutputs = NodeOutputs`** (line 315).

**Implication for T111**: outputs ARE collected + passed to executors. The only gap is that `ApplyConfigJsonTemplates` doesn't expose them as template substitution context. **No `RunContext` schema change needed.** T111 reduces to: change the static `ApplyConfigJsonTemplates` signature to take `runContext` (not just `parameters`), and call `_templateEngine.Render` with a merged context dictionary.

### Finding 2: ReturnResponseNodeExecutor already proves the pattern

[src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ReturnResponseNodeExecutor.cs:312-339](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ReturnResponseNodeExecutor.cs#L312) has `BuildTemplateContext` that:

```csharp
foreach (var (varName, output) in context.PreviousOutputs) {
    if (output.StructuredData.HasValue) {
        templateContext[varName] = TemplateEngine.ConvertJsonElement(output.StructuredData.Value);
    } else {
        templateContext[varName] = null;
    }
}
// The "run" metadata bag
templateContext["run"] = new {
    id = context.RunId.ToString(),
    playbookId = context.PlaybookId.ToString(),
    tenantId = context.TenantId,
    completedAtUtc = DateTimeOffset.UtcNow.ToString("o")
};
```

This works (it's how `{{validationResult.scrubbedText}}` resolves on ReturnResponse today). **The exact same code needs to run at every executor's configJson resolution point — not just ReturnResponse.** T111 extracts this into a shared helper.

### Finding 3: Comprehensive expression inventory in DAILY-BRIEFING-NARRATE

```
{{briefing.channel}}             ← Action JPS internal (not orchestrator-resolved)
{{briefing.payload}}             ← Action JPS internal (not orchestrator-resolved)
{{channelNarrationResults}}      ← Variable ref (no helper)
{{channelRegistry.channels}}     ← Variable ref with dotted path
{{distinct (concat (map …) (flatten (map … (lambda c …)))…)}}  ← Complex expression (T112+T113)
{{join '\\n\\n' tldrResult.summary tldrResult.keyTakeaways tldrResult.topAction (map channelNarrationResults 'narrative' | flatten | join '\\n')}}  ← Pipe shorthand (T113)
{{json channel}}                 ← json helper (T112)
{{json start}}                   ← json helper (T112)
{{output}}                       ← Special: current-node output (different lifecycle)
{{run.completedAtUtc}}           ← System variable (already handled by Finding 2 pattern)
{{start.channels}}               ← Variable ref with dotted path
{{start.priorityItems}}          ← Variable ref with dotted path
{{tldrResult}}                   ← Variable ref
{{validationResult.removedTerms}} ← Variable ref with dotted path
{{validationResult.scrubbedText}} ← Variable ref with dotted path
```

**T113 scope expanded beyond just lambda**: the expression `(map channelNarrationResults 'narrative' | flatten | join '\\n')` uses **pipe-then-helper shorthand** that is NOT valid Handlebars syntax. T113 must rewrite both lambda AND pipe shorthand. The rewrite uses the `flatMap` helper for lambda AND nested subexpression syntax for pipe (e.g., `(join '\\n' (flatten (map X 'narrative')))`).

### Finding 4: Two layers of templating, NOT confused

- **Orchestrator layer** (ApplyConfigJsonTemplates) resolves `{{nodeOutput.field}}` etc. BEFORE handing configJson to the executor. T111 fixes this layer.
- **Action JPS layer** (LLM prompt content) uses placeholders like `{{briefing.payload}}` INSIDE the Action's stored `sprk_systemprompt`. These are NOT resolved by the orchestrator — they're internal to the prompt template that the LLM sees. The AiCompletion executor reads `inputBinding.payload` from configJson (after T111 resolution), and the executor itself injects that value into the LLM prompt at the `{{briefing.payload}}` placeholder. Two different template layers, two different resolution points — Wave 11 only touches the orchestrator layer.

### Finding 5: `{{output}}` is a current-node lifecycle reference

Used in `outputBinding.tldrResult = "{{output}}"` etc. — meaning "this node's own output becomes `tldrResult` in NodeOutputs". This is RESOLVED AFTER the executor returns (in the orchestrator's StoreNodeOutput flow + the executor's output-binding handler), NOT before. Different lifecycle from `{{nodeName.field}}` (read of prior node's output). T111 does NOT need to handle `{{output}}` — that's a separate (already-working) code path inside AiAnalysis / AiCompletion executors' output-binding handlers.

---

## Answers to the 7 goal questions

### Q1: What does ApplyConfigJsonTemplates do today, and what does it NOT do?

**Today** ([line 1921](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs#L1921)):

```csharp
private static PlaybookNodeDto ApplyConfigJsonTemplates(
    PlaybookNodeDto node,
    IReadOnlyDictionary<string, string>? parameters)
{
    var rendered = node.ConfigJson;
    foreach (var kvp in parameters) {
        var placeholder = "{{" + kvp.Key + "}}";
        rendered = rendered.Replace(placeholder, kvp.Value ?? string.Empty, StringComparison.Ordinal);
    }
    return node with { ConfigJson = rendered };
}
```

Handles only literal `{{paramName}}` substitution from `runContext.Parameters` (an `IReadOnlyDictionary<string, string>`). Does NOT:
- Use Handlebars (`{{X.Y.Z}}` dotted paths fail)
- Resolve node outputs (`{{tldrResult.summary}}` stays literal)
- Resolve helpers (`{{json X}}` stays literal)
- Resolve the `run` system variable

### Q2: What does ITemplateEngine accept, and what helpers are already registered?

`ITemplateEngine.Render(string template, IDictionary<string, object?> context)` ([interface](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/ITemplateEngine.cs)). The Handlebars-backed `TemplateEngine.cs` already supports:
- Nested object access `{{X.Y.Z}}` (Handlebars-native)
- Graceful missing (`UndefinedBindingResult` → empty string; configured via `ThrowOnUnresolvedBindingExpression = false`)
- Existing custom helpers: `default`, `safe`, `joinIds`

Already DI-registered as Singleton at [AnalysisServicesModule.cs:860](../../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs#L860). The static helper `TemplateEngine.ConvertJsonElement(JsonElement) → object?` already exists for JsonElement → traversable Dictionary/List conversion.

### Q3: Proposed RunContext.NodeOutputs shape

**No new property needed.** `PlaybookRunContext.NodeOutputs` already exists as `IReadOnlyDictionary<string, NodeOutput>` (line 167). The shape is unchanged. The template context dictionary built FROM NodeOutputs is `Dictionary<string, object?>` where each value is either:
- `TemplateEngine.ConvertJsonElement(output.StructuredData)` when StructuredData is non-null (the common LLM-output case)
- `output.TextContent` when TextContent is non-null and StructuredData is null
- `null` otherwise (gracefully handled by Handlebars)

### Q4: Where do node outputs get registered, and what does the orchestrator pass to subsequent template renders?

**Registration** (already done): `runContext.StoreNodeOutput(output)` called after each `executor.ExecuteAsync()` returns. Keys by `OutputVariable` name.

**New context-passing**: extract `BuildTemplateContext(runContext)` static helper (mirroring ReturnResponseNodeExecutor's pattern from lines 312-339). Call it from a NEW non-static `ApplyConfigJsonTemplates` method that accepts `runContext` (not just parameters). Pass the merged context (Parameters + NodeOutputs + `run` bag) to `_templateEngine.Render`.

### Q5: Custom helpers needed (per expression inventory)

**T112 (standard 6)**:
- `{{json X}}` — JsonSerializer.Serialize with camelCase
- `{{map COLL 'field'}}` — pluck `field` (dotted path) from each item; null-tolerant
- `{{flatten X}}` — flatten array of arrays
- `{{distinct X}}` — case-insensitive distinct, preserve first-occurrence order
- `{{concat A B …}}` — concatenate enumerables (skip null args)
- `{{join SEP A B …}}` — first arg is separator, remainder are values to join

**T113 (7th + source rewrite)**:
- `{{flatMap COLL 'nested.path'}}` — replaces inline `lambda` AND pipe shorthand simultaneously
- Source playbook `daily-briefing-narrate.json` `allowList` expression rewritten to use `flatMap`
- Source playbook `candidateText` expression rewritten to convert pipe → nested subexpression (e.g., `(join '\n' (flatten (map X 'narrative')))`)

### Q6: Iteration semantic placement — orchestrator, NOT TemplateEngine

**Decision: orchestrator.** Rationale:
- Handlebars `#each` block helpers iterate INSIDE a string template — they can't multi-call an executor with per-iteration context
- Fan-out iteration semantic per ADR-037 needs: (a) resolve iterateOver → array, (b) for each item, render configJson with `[itemAlias]: item` overlay, (c) call executor, (d) collect N outputs into an array
- That control flow is orchestration, not templating. It belongs in PlaybookOrchestrationService.

T114 detects `iteration.iterateOver` + `iteration.itemAlias` from RAW configJson (parsed as JsonDocument before template render), then branches to fan-out execution. Backward-compatible: nodes without the iteration block fall through to existing single-call path.

### Q7: Minimum-viable test plan per task

**T111 (wire template engine + NodeOutputs)** — 5 tests:
- `{{paramName}}` regression (literal substitution still works)
- `{{nodeA.field}}` resolves to nodeA's prior output's StructuredData.field
- `{{nodeA.nested.deep}}` resolves through dotted path
- `{{nonexistent}}` renders as empty string (graceful, no throw)
- Parameters wins on key collision when both Parameters and NodeOutputs contain the same key

**T112 (6 helpers)** — ~30 tests (6 helpers × 5 cases): per helper, happy path + null + empty + string-as-scalar + JsonElement input. Plus 1 subexpression-composition integration test: `{{distinct (concat (map A 'x') (map B 'y'))}}` end-to-end.

**T113 (flatMap + source rewrite)** — 6 tests: array-of-objects-with-array-subfield happy path, empty outer, missing nested path → skip, scalar intermediate → empty, JsonElement input, single-level path. Plus: grep for `lambda` AND for ` | ` pipe in source playbook returns zero matches after rewrite.

**T114 (fan-out iteration)** — 6 tests: N=3 iteration with mock executor records per-iteration itemAlias, output ordered by input order, empty iterateOver → empty array (no exec calls), backward-compat (no iteration block → single call), iterateOver renders to JsonElement array, iterateOver renders to null → empty array.

---

## Proposed shared helper signature (for T111)

To avoid duplicating BuildTemplateContext between ReturnResponseNodeExecutor + ApplyConfigJsonTemplates, extract it. Two options:

**Option A** (recommended): static method on `PlaybookRunContext` or its own static helper class:

```csharp
public static class PlaybookTemplateContextBuilder
{
    public static Dictionary<string, object?> Build(
        IReadOnlyDictionary<string, NodeOutput> nodeOutputs,
        IReadOnlyDictionary<string, string>? parameters,
        Guid runId,
        Guid playbookId,
        string tenantId,
        DateTimeOffset startedAt)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal);
        // 1. NodeOutputs → traversable
        foreach (var (varName, output) in nodeOutputs)
        {
            ctx[varName] = output.StructuredData.HasValue
                ? TemplateEngine.ConvertJsonElement(output.StructuredData.Value)
                : (object?)output.TextContent;
        }
        // 2. Parameters (string-typed; win on collision per existing semantics)
        if (parameters is not null)
            foreach (var kvp in parameters)
                ctx[kvp.Key] = kvp.Value;
        // 3. `run` metadata bag
        ctx["run"] = new {
            id = runId.ToString(),
            playbookId = playbookId.ToString(),
            tenantId,
            completedAtUtc = DateTimeOffset.UtcNow.ToString("o")
        };
        return ctx;
    }
}
```

**Option B**: keep BuildTemplateContext in ReturnResponseNodeExecutor; orchestrator copies the pattern. Discouraged — drift risk.

T111 implements Option A. ReturnResponseNodeExecutor refactored to call the shared helper.

---

## Proposed ApplyConfigJsonTemplates rewrite (for T111)

```csharp
private PlaybookNodeDto ApplyConfigJsonTemplates(
    PlaybookNodeDto node,
    PlaybookRunContext runContext)
{
    if (string.IsNullOrEmpty(node.ConfigJson) ||
        !node.ConfigJson.Contains("{{", StringComparison.Ordinal))
    {
        return node;
    }

    var context = PlaybookTemplateContextBuilder.Build(
        runContext.NodeOutputs,
        runContext.Parameters,
        runContext.RunId,
        runContext.PlaybookId,
        runContext.TenantId,
        runContext.StartedAt);

    var rendered = _templateEngine.Render(node.ConfigJson, context);
    return node with { ConfigJson = rendered };
}
```

(`_templateEngine` is constructor-injected.) The call site at line 1198 becomes `var substitutedNode = ApplyConfigJsonTemplates(node, runContext);` — one parameter, not two.

---

## Open questions flagged for downstream tasks

- **T112 / T113 (Handlebars subexpression depth)**: Handlebars supports nested subexpressions, but VERY deep nesting may hit parser limits. The deployed `allowList` expression has ~9-level nesting. T113 must verify the rewritten expression parses cleanly (smoke at T112 + T113 task boundaries via TemplateEngineTests).
- **T114 (iteration concurrency)**: V1 is sequential per the POML. Parallelizing per-iteration LLM calls is a deferred optimization. Track as a future DEF if perf matters in production.
- **T115 (deployed-data PATCH ordering)**: ValidateEntityNames node sprk_configjson PATCH must happen AFTER T113's source rewrite is in the repo (else the deployed value would still reference lambda/pipe). T115 dependency `112, 113, 114` enforces this.
- **T116 smoke surfaces the `{{output}}` lifecycle**: if the GenerateTldr output (`tldrResult`) doesn't appear in NodeOutputs after the executor returns, the issue is the AiCompletion executor's outputBinding handler — NOT the orchestrator template engine. T116 smoke catches this; if found, file as DEF or spawn investigation task.

---

## Conclusion (initial)

Work scope confirmed narrower than initial estimate. T111 reduces from "build NodeOutputs infrastructure" to "wire ITemplateEngine + extract BuildTemplateContext helper". T113 scope expands slightly to cover pipe-shorthand elimination in addition to lambda elimination. Other tasks unchanged.

**T111 starts with**: extract `PlaybookTemplateContextBuilder` as new static helper (alongside `TemplateEngine.cs`), refactor `ReturnResponseNodeExecutor.BuildTemplateContext` to call it, inject `ITemplateEngine` into `PlaybookOrchestrationService` constructor, rewrite `ApplyConfigJsonTemplates` to use it, add 5 unit tests.

---

## REVISION 2026-06-29 — Two-Layer Architecture (operator-approved)

> **Triggered by operator question 2026-06-29**: "in previous Daily Briefing projects the TL:DR and Activities were being shown including in narrative form. If there was not previously a template resolution how were those being constructed? my concern is that the wiring is already available and somehow we're missing it"

### Finding 6 — historical answer: R1/R2/R3 bypassed the playbook engine

Git history confirms `HandleNarrate` was originally pure-C# composition (commit `836da2394`):

```csharp
private static async Task<TldrResult> GetTldrAsync(...) {
    var prompt = BuildNarrateTldrPrompt(request);              // C# composes prompt
    var briefingText = await openAiClient.GetCompletionAsync(prompt, ...);   // direct LLM call
}
```

Commit `88dd66a1c` (R4 task 031) migrated HandleNarrate to playbook dispatch via `IConsumerRoutingService` → `IInvokePlaybookAi`. The migration was **never tested end-to-end**. R7 inherits the incomplete migration. **The "missing wiring" the operator suspected is real — but not because something was bypassed; because the R4 migration was incomplete.**

### Finding 7 — partial wiring + naming mismatch in AI executors

| Executor | Reads | Uses `_templateEngine.Render`? |
|---|---|---|
| `LoadKnowledgeNodeExecutor` | `configJson.passthroughBinding` | ✅ YES (line 230) |
| `ReturnResponseNodeExecutor` | `configJson.responseBinding` | ✅ YES (line 263) |
| `AiCompletionNodeExecutor` | `configJson.templateParameters` (flat dict) | ❌ NO — only string substitution into JPS prompt body via PromptSchemaRenderer |
| `AiAnalysisNodeExecutor` | `configJson.templateParameters` (per code-comment mirror) | ❌ NO |

The deployed playbook author wrote `inputBinding.payload = "{{json start}}"`. `AiCompletionNodeExecutor.ExtractTemplateParameters` (line 543) ONLY reads `configJson.templateParameters` — the `inputBinding` key is silently ignored. Even with orchestrator-level template resolution, the AI executor still wouldn't connect `inputBinding.payload` to the Action prompt's `{{briefing.payload}}` placeholder.

### Revised T111 design — TWO LAYERS, both centralized

```
Layer 1: ORCHESTRATOR-LEVEL TEMPLATE RESOLUTION (universal)
  ─────────────────────────────────────────────────────────
  Every executor (current + future) benefits without per-executor template code.
  - Extract `PlaybookTemplateContextBuilder.Build(runContext)` — one source of truth
  - Orchestrator's `ApplyConfigJsonTemplates` calls `ITemplateEngine.Render` with this context
  - configJson handed to executor has all `{{X}}` resolved at string level
  - Existing `LoadKnowledgeNodeExecutor` + `ReturnResponseNodeExecutor` per-executor resolvers
    become no-ops (Render of resolved string is identity); they keep their "build output from
    resolved values" logic but call the shared `PlaybookTemplateContextBuilder.Build()`
    eliminating today's BuildTemplateContext duplication

Layer 2: AI-SHARED PROMPT INPUT BINDING (AI executors only)
  ─────────────────────────────────────────────────────────
  Centralized to AI executors — AiCompletion + AiAnalysis + any future AI executor.
  - New helper `AiPromptInputBindingResolver` (free function or static class)
  - Reads `configJson.inputBinding.<key>` (RESOLVED values from Layer 1)
  - Looks up `action.input[<key>].placeholder` to map to LLM-prompt-placeholder name
  - Populates the `templateParameters` dict that `PromptSchemaRenderer` consumes
  - AI executors invoke this helper once before PromptSchemaRenderer
  - Future AI executors get this for free — one line addition, NOT per-executor template code
```

**Why this addresses operator concern**: future "narrative output" executors throughout the Workspace UX (R8, R9, …) do NOT need per-executor template-resolution code. Non-AI executors get Layer 1 for free. New AI executors get Layer 1 + Layer 2 by invoking the shared resolver. No duplication.

### Architecture diagram

```
┌─────────────────────────────────────────────────────────────┐
│ Orchestrator                                                 │
│  └─ ApplyConfigJsonTemplates(node, runContext)               │
│        ├─ uses PlaybookTemplateContextBuilder.Build()        │ ← Layer 1 (universal)
│        └─ uses ITemplateEngine.Render(configJson, context)   │
│  After: configJson has all {{X}} resolved at string level    │
└──────────────────────────┬───────────────────────────────────┘
                           ▼
              ┌───────────────────────────────┐
              │ Executor receives RESOLVED    │
              │ configJson (no templates left)│
              └────────────┬──────────────────┘
       ┌───────────────────┼───────────────────────┐
       ▼                   ▼                       ▼
  AI executors        Load/Return            Other executors
  invoke              (read resolved         (read resolved
  AiPromptInput-      bindings, build        fields, no
  BindingResolver     output / response)     template code)
  (Layer 2 shared)         │                       │
       │                   │                       │
       ▼                   ▼                       ▼
  inputBinding.X     existing logic         existing logic
  → action.input
    [X].placeholder
  → templateParameters
  → PromptSchemaRenderer
```

### Revised T111 sub-task list

| # | Sub-task | File |
|---|---|---|
| 1 | Extract `PlaybookTemplateContextBuilder.Build(runContext)` static helper | NEW: `Services/Ai/PlaybookTemplateContextBuilder.cs` |
| 2 | Wire orchestrator to use ITemplateEngine + helper (Layer 1) | `Services/Ai/PlaybookOrchestrationService.cs` |
| 3 | Refactor `LoadKnowledgeNodeExecutor.BuildTemplateContext` to call shared helper | `Services/Ai/Nodes/LoadKnowledgeNodeExecutor.cs` |
| 4 | Refactor `ReturnResponseNodeExecutor.BuildTemplateContext` to call shared helper | `Services/Ai/Nodes/ReturnResponseNodeExecutor.cs` |
| 5 | Add `AiPromptInputBindingResolver` shared helper (Layer 2) | NEW: `Services/Ai/Nodes/AiPromptInputBindingResolver.cs` |
| 6 | Wire `AiCompletionNodeExecutor` to invoke the AI helper | `Services/Ai/Nodes/AiCompletionNodeExecutor.cs` |
| 7 | Wire `AiAnalysisNodeExecutor` to invoke the AI helper | `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` |
| 8 | Unit tests covering both layers | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/` (new test files) |

T111 size: medium (~1 day). Single PR / commit series.

### Confirmation

Operator approved "two layer" 2026-06-29. T111 executes against this revised design. T112-T119 unchanged.

