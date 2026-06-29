# AI Guide — Wiring a New Consumer

> **Audience**: Makers + AI engineers wiring a new surface (chat window, widget, code page, ad-hoc launcher) to invoke playbooks.
> **Author**: spaarke-ai-platform-unification-r7 Wave 6 (FR-31)
> **Status**: Maker-facing tutorial. For runtime mechanics (cache TTL, internal classes, match-conditions JSON predicates), see the canonical reference: [`docs/architecture/ai-architecture-playbook-consumer-routing.md`](../architecture/ai-architecture-playbook-consumer-routing.md) (READ-ONLY — do not modify).
> **Last Updated**: 2026-06-28

---

## What this guide covers

You have a new place in the product — a chat window, a widget tile, a code page button, a background job — and you want it to call a playbook. This guide walks you through the **3-step wiring pattern** every consumer follows in Spaarke, with a worked example from the chat-summarize migration we shipped in R7.

What this guide does **not** cover:
- Authoring the playbook itself → [`PLAYBOOK-AUTHOR-GUIDE.md`](PLAYBOOK-AUTHOR-GUIDE.md)
- Runtime resolution algorithm, cache semantics, `sprk_matchconditionsjson` JSON predicates → [`docs/architecture/ai-architecture-playbook-consumer-routing.md`](../architecture/ai-architecture-playbook-consumer-routing.md)
- BFF placement / publish-size / CVE governance for new endpoints → [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) + root CLAUDE.md §10

---

## §1. What is a consumer?

A **consumer** is any surface that invokes a playbook through Spaarke's unified dispatch model. Examples shipped today:

- A workspace tile that summarizes a document (consumer surface = `WorkspaceAiService`)
- The chat `/summarize` slash-command (consumer surface = `SessionSummarizeOrchestrator`)
- The Matter form pre-fill flow (consumer surface = `MatterPreFillService`)
- The daily-briefing narration endpoint (consumer surface = `DailyBriefingEndpoints.HandleNarrate`)

What unifies them: each surface asks **two questions** at runtime:

1. *Which playbook should I run for my current context?* → `IConsumerRoutingService.ResolveAsync(consumerType, …)`
2. *Run it and give me the result.* → `IInvokePlaybookAi.InvokePlaybookAsync(playbookId, parameters, context, …)`

The surface itself never hardcodes a playbook GUID. The mapping from "I am the chat-summarize surface" to "the playbook is `summarize-document-for-chat@v1`" lives in a Dataverse row (`sprk_playbookconsumer`) that an admin can change without touching code.

**One-line definition**: a consumer is a `(ConsumerTypes.X identifier)` + `(sprk_playbookconsumer row)` + `(call site that resolves and invokes)`.

---

## §2. When to add a new consumer

Decision tree:

```
Are you invoking a playbook from a NEW surface (not already in the table in §5)?
├── NO → Don't add a consumer. Extend the existing one (e.g., add a parameter, change the routing row).
└── YES → Continue.
    ├── Do you have a playbook GUID hardcoded somewhere?
    │   ├── YES → Replace with consumer routing. The whole point is admin-changeable
    │   │         mapping without redeploying code. See §3.
    │   └── NO → Continue.
    ├── Do you need streaming SSE (per-token UX, progressive rendering)?
    │   ├── YES → Special case. See §6 "Streaming consumers (Path B variant)".
    │   │         You still register a consumer; the call site is slightly different.
    │   └── NO → Standard Path A.5. Follow §3 verbatim.
    └── Do you have a document context + need legacy doc-bound semantics?
        ├── YES → You may not need this guide. See ai-architecture-playbook-consumer-routing.md §4
        │         "Path A / A.5 / B decision matrix" for which path to use.
        └── NO → Standard Path A.5. Follow §3 verbatim.
```

---

## §3. The 3-step wiring pattern

### Step 1 — Add your consumer-type identifier to `ConsumerTypes.cs`

File: [`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs)

Add a `public const string` with the stable lower-kebab-case identifier:

```csharp
public static class ConsumerTypes
{
    // ... existing constants ...

    /// <summary>
    /// <c>MyNewConsumerService</c> — pre-fills the Foo widget from selected files.
    /// </summary>
    public const string MyNewConsumer = "my-new-consumer";
}
```

Then append it to the `All` list at the bottom (the startup health-check diffs `All` against the live Dataverse table):

```csharp
public static readonly IReadOnlyList<string> All = new[]
{
    // ... existing entries ...
    MyNewConsumer,
};
```

**Why a constant and not the literal string?** A 2026-06-24 UAT incident shipped `matter-pre-fil` (missing the final `l`) to a Power Apps form. The compiler can't catch a typo in a literal; it can catch a typo in a constant name. The `ConsumerTypes` class is the BFF-side defense.

### Step 2 — Create a `sprk_playbookconsumer` row in Dataverse

The row tells the routing service which playbook to dispatch when your consumer asks.

| Column | Value for our example |
|---|---|
| `sprk_consumertype` | `my-new-consumer` (must match the constant value EXACTLY) |
| `sprk_code` | `default` (use `default` unless you have per-instance discrimination) |
| `sprk_enabled` | `true` |
| `sprk_playbookid` | Lookup → the `sprk_analysisplaybook` row you want to invoke |
| `sprk_environment` | `dev`, `test`, `prod`, or leave null for wildcard |
| `sprk_priority` | `100` (lower wins when multiple rows match) |
| `sprk_matchconditionsjson` | leave null (advanced — see canonical reference) |

You can create the row two ways:

**a) Maker-friendly (recommended for first wiring):** open the `sprk_playbookconsumer` table in Power Apps Maker portal → New Row → fill the columns → Save.

**b) Scripted (for repeatable seed):** extend [`scripts/dataverse/Seed-PlaybookConsumers.ps1`](../../scripts/dataverse/Seed-PlaybookConsumers.ps1) with a new entry. The script is idempotent — re-running it does not duplicate rows.

After saving, the next call to `ResolveAsync("my-new-consumer")` from any tenant + environment that matches will return your playbook ID.

> **Cache note**: the routing service caches resolutions for 5 minutes. If you change a row and want to test immediately, restart the BFF App Service (or wait 5 minutes).

### Step 3 — At the consumer surface, resolve and invoke

This is the call-site pattern. Inject `IConsumerRoutingService` + `IInvokePlaybookAi` into your service, then:

```csharp
public sealed class MyNewConsumerService
{
    private readonly IConsumerRoutingService _consumerRouting;
    private readonly IInvokePlaybookAi _invokePlaybookAi;
    private readonly ILogger<MyNewConsumerService> _logger;

    public MyNewConsumerService(
        IConsumerRoutingService consumerRouting,
        IInvokePlaybookAi invokePlaybookAi,
        ILogger<MyNewConsumerService> logger)
    {
        _consumerRouting = consumerRouting;
        _invokePlaybookAi = invokePlaybookAi;
        _logger = logger;
    }

    public async Task<MyDomainResult> RunAsync(
        MyRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Step 3a — resolve the playbook ID from Dataverse routing
        var playbookId = await _consumerRouting.ResolveAsync(
            ConsumerTypes.MyNewConsumer,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!playbookId.HasValue || playbookId.Value == Guid.Empty)
        {
            // Pick your fallback: feature-disabled response, typed-options
            // backup ID, or fail-fast. The routing service is silent on what
            // "no match" means semantically — you decide.
            throw new InvalidOperationException(
                "MyNewConsumer playbook not configured. Seed a " +
                "sprk_playbookconsumer row for consumertype='my-new-consumer'.");
        }

        // Step 3b — build the parameter dictionary the playbook expects.
        // ADR-015 binding: deterministic IDs and display values only — never
        // raw user message content / file body / secrets.
        var parameters = new Dictionary<string, string>
        {
            ["entityId"] = request.EntityId.ToString(),
            ["entityName"] = request.EntityName,
        };

        // Step 3c — invoke the facade
        var invocationContext = new PlaybookInvocationContext
        {
            TenantId = request.TenantId,
            HttpContext = httpContext,            // ASP.NET primitive — OK on facade
            CorrelationId = Activity.Current?.TraceId.ToString()
        };

        var result = await _invokePlaybookAi.InvokePlaybookAsync(
            playbookId.Value,
            parameters,
            invocationContext,
            cancellationToken).ConfigureAwait(false);

        // Step 3d — translate the facade result to your domain shape
        if (!result.Success)
        {
            _logger.LogWarning(
                "MyNewConsumer playbook failed. RunId={RunId}, ErrorCode={ErrorCode}",
                result.RunId, result.ErrorCode);
            return MyDomainResult.Failure(result.ErrorMessage ?? "Playbook failed.");
        }

        return MyDomainResult.From(result.TextContent, result.StructuredData);
    }
}
```

That is the entire wiring. Three steps: constant, Dataverse row, call site. No new endpoint registration, no new DI module beyond your service registration, no new orchestrator code.

---

## §4. Worked example — chat-summarize migration (R7 Wave 9 task 091)

**The case**: chat's `/summarize` slash command originally called `IPlaybookExecutionEngine.ExecuteChatSummarizeAsync(playbookId, …)` — a chat-streaming-specific method buried inside the AI internals. The playbook ID itself came from `WorkspaceOptions.ChatSummarizePlaybookId` (a config-file setting). Every other consumer in the codebase was on the canonical triangle; chat-summarize was the only outlier.

R7 task 091 fixed it.

### BEFORE (chat-streaming-specific, non-canonical)

```csharp
// SessionSummarizeOrchestrator.cs (pre-R7)
public async IAsyncEnumerable<AnalysisChunk> SummarizeSessionFilesAsync(
    ChatSummarizeRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    var configuredId = _workspaceOptions.Value.ChatSummarizePlaybookId;
    var playbook = await _playbookLookup.GetByIdAsync(configuredId, cancellationToken);

    await foreach (var chunk in _executionEngine
        .ExecuteChatSummarizeAsync(playbook.Id, engineRequest, cancellationToken)
        .ConfigureAwait(false))
    {
        yield return chunk;
    }
}
```

Problems:
1. Playbook ID hardcoded in config (one per environment) — admins can't redirect without a redeploy.
2. `ExecuteChatSummarizeAsync` is AI-internal — CRUD-side orchestrator reaches across the ADR-013 facade boundary.
3. The dispatch path doesn't exist for any other consumer — every other surface uses `IConsumerRoutingService` + the canonical triangle.

### AFTER (consumer-routed, canonical triangle)

```csharp
// SessionSummarizeOrchestrator.cs (post-R7 task 091)
public async IAsyncEnumerable<AnalysisChunk> SummarizeSessionFilesAsync(
    ChatSummarizeRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    // Step 3a — resolve via the routing table (compile-time-safe identifier)
    Guid resolvedPlaybookId;
    var routedPlaybookId = await _consumerRouting
        .ResolveAsync(ConsumerTypes.ChatSummarize, cancellationToken: cancellationToken)
        .ConfigureAwait(false);

    if (routedPlaybookId.HasValue && routedPlaybookId.Value != Guid.Empty)
    {
        resolvedPlaybookId = routedPlaybookId.Value;
    }
    else
    {
        // Graceful-degrade fallback (FR-1R-06 deprecation window only)
        var configuredId = _workspaceOptions.Value.ChatSummarizePlaybookId;
        var playbook = await _playbookLookup.GetByIdAsync(configuredId, cancellationToken);
        resolvedPlaybookId = playbook.Id;
    }

    // Step 3c — invoke (this consumer uses IPlaybookOrchestrationService directly,
    // NOT IInvokePlaybookAi, because it needs per-token SSE streaming — see §6)
    var playbookRequest = new PlaybookRunRequest
    {
        PlaybookId = resolvedPlaybookId,
        DocumentIds = Array.Empty<Guid>(),
        Parameters = BuildParameters(request, session, uploadedFiles, resolvedFileIds)
    };

    await foreach (var ev in _orchestrationService
        .ExecuteAsync(playbookRequest, httpContext, cancellationToken)
        .ConfigureAwait(false))
    {
        var chunk = TranslateEventToChunk(ev);
        if (chunk is not null) yield return chunk;
    }
}
```

What changed:
- ✅ Identifier is `ConsumerTypes.ChatSummarize` (compile-time-safe), not a literal string.
- ✅ Playbook ID comes from the routing table — admin redirect = Dataverse row update, no redeploy.
- ✅ Dispatch is through a canonical Spaarke contract.
- ✅ The previously hardcoded `WorkspaceOptions.ChatSummarizePlaybookId` is a graceful-degrade fallback for the deprecation window, not the primary path.

**Why this consumer uses `IPlaybookOrchestrationService.ExecuteAsync` instead of `IInvokePlaybookAi.InvokePlaybookAsync`**: chat-summarize emits per-token `FieldDelta` chunks to give the user a progressive-rendering UX. `IInvokePlaybookAi` aggregates the SSE stream into a single result — that would break the load-bearing per-token rendering. The task-090 design picked direct orchestration injection with an inline SSE adapter (`TranslateEventToChunk`) instead. See §6 for when to do this.

**Closing the loop**: task 092 created the corresponding `sprk_playbookconsumer` row (`sprk_consumertype = 'chat-summarize'`, pointing at `summarize-document-for-chat@v1`) in the spaarkedev1 environment.

---

## §5. Existing consumers reference table

These are the 7 consumer-type identifiers shipped in [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs) as of R7:

| Constant | Identifier | Surface (concrete consumer class) | What it does |
|---|---|---|---|
| `MatterPreFill` | `matter-pre-fill` | `MatterPreFillService` | Pre-fills a new Matter form from uploaded documents |
| `ProjectPreFill` | `project-pre-fill` | `ProjectPreFillService` | Pre-fills a new Project form from uploaded documents |
| `AiSummary` | `ai-summary` | `WorkspaceAiService` | Generates the workspace tile AI summary (Document Profile playbook) |
| `SummarizeFile` | `summarize-file` | `WorkspaceFileEndpoints` | File summarization behind the Workspace summarize button |
| `ChatSummarize` | `chat-summarize` | `SessionSummarizeOrchestrator` | Chat-side `/summarize` slash command (R7 case study, §4) |
| `EmailAnalysis` | `email-analysis` | `AppOnlyAnalysisService` | Email analysis pipeline (app-only execution context) |
| `DailyBriefingNarrate` | `daily-briefing-narrate` | `DailyBriefingEndpoints.HandleNarrate` | Daily briefing narration dispatch — the canonical Path A.5 reference |

To see the live mapping in your environment, query the `sprk_playbookconsumer` table in Power Apps Maker portal or via `mcp__dataverse__read_query`:

```sql
SELECT sprk_consumertype, sprk_code, sprk_environment, sprk_playbookid_value, sprk_enabled, sprk_priority
FROM sprk_playbookconsumer
WHERE sprk_enabled = true
ORDER BY sprk_consumertype, sprk_priority
```

---

## §6. Special case — streaming consumers (Path B variant)

If your consumer needs per-token SSE streaming (e.g., chat surfaces with progressive rendering), `IInvokePlaybookAi.InvokePlaybookAsync` is the wrong tool — it aggregates the stream into a single result before returning. The chat-summarize migration (§4) demonstrates the workaround:

1. Still register your consumer in `ConsumerTypes.cs` (Step 1 from §3).
2. Still create a `sprk_playbookconsumer` row in Dataverse (Step 2 from §3).
3. At the call site, inject `IConsumerRoutingService` AND `IPlaybookOrchestrationService` directly (instead of `IInvokePlaybookAi`).
4. Resolve via `ResolveAsync(ConsumerTypes.YourConsumer, …)` as normal.
5. Call `_orchestrationService.ExecuteAsync(request, httpContext, ct)` — the SSE event stream is `IAsyncEnumerable<PlaybookStreamEvent>`.
6. Translate each `PlaybookStreamEvent` into your surface's SSE chunk shape (chat uses `AnalysisChunk`; your surface may use something else).

This is the **only** valid reason to bypass `IInvokePlaybookAi`. Per ADR-013, direct injection of `IPlaybookOrchestrationService` from CRUD code is normally a hygiene violation; the SSE streaming requirement is the documented exception. Cite the exception in your XML doc comment when you do this — task 091's `SessionSummarizeOrchestrator` class comment is the reference.

For the runtime detail on what `PlaybookStreamEvent` carries, the SSE adapter pattern, and the Path A / A.5 / B decision matrix: [`docs/architecture/ai-architecture-playbook-consumer-routing.md`](../architecture/ai-architecture-playbook-consumer-routing.md) §3 and §4 (READ-ONLY — do not modify).

---

## §7. Troubleshooting

### "`ResolveAsync` returned null"

Most common cause. Check, in order:

1. **Typo in the consumer-type string in Dataverse**. The row's `sprk_consumertype` must match `ConsumerTypes.YourConsumer` value EXACTLY (case-sensitive, no leading/trailing whitespace). Open the row in Maker portal and compare character-by-character.
2. **`sprk_enabled = false`** on the row. Resolution ignores disabled rows.
3. **Wrong environment**. If your row has `sprk_environment = "prod"` and you're running in `dev`, no match. Either set `sprk_environment` to wildcard (null/empty) or add a `dev` row.
4. **Cache staleness**. The routing service caches for 5 minutes. If you just changed the row, restart the BFF or wait.
5. **You're not registered in `ConsumerTypes.All`**. The startup health check ([`RoutingConsumerTypeHealthCheck.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/RoutingConsumerTypeHealthCheck.cs)) diffs `ConsumerTypes.All` against the live Dataverse table on boot. Missing entries log a warning.

### "Playbook not found" / `ErrorCode = PLAYBOOK_INVOCATION_FAILED`

The `sprk_playbookconsumer` row points at a playbook ID that doesn't exist or isn't deployed. Verify:
1. The lookup column `sprk_playbookid` resolves to a real `sprk_analysisplaybook` row.
2. That playbook has `sprk_playbooknode` rows (i.e., it's actually a deployed playbook, not an empty shell).
3. The playbook deployment is current per [`PLAYBOOK-AUTHOR-GUIDE.md`](PLAYBOOK-AUTHOR-GUIDE.md) Step 7.

### "Parameter mismatch" / playbook errors on a `{{templateVariable}}` not resolving

The playbook's nodes reference template variables (e.g., `{{entityId}}`) that you didn't pass in the `parameters` dictionary. Either:
1. Add the missing keys to your `parameters` dictionary at the call site.
2. Update the playbook to use defaults (`{{default entityId 'unknown'}}` — see [`PLAYBOOK-AUTHOR-GUIDE.md`](PLAYBOOK-AUTHOR-GUIDE.md) Handlebars Template Helpers).

### "I changed the routing row but nothing happened"

5-minute cache. Restart the BFF App Service or wait. The routing service does not subscribe to Dataverse change events — by design (the cost-of-doing-nothing for fresh-by-the-second routing isn't worth the complexity).

### "Compile error: `IInvokePlaybookAi` not in scope"

Add `using Sprk.Bff.Api.Services.Ai.PublicContracts;` at the top of your file. The facade lives in the `PublicContracts/` folder — the only types from `Services/Ai/` that CRUD-side code is permitted to inject per ADR-013.

### Compile-time defense against future typos

When you add a new consumer, also add it to `ConsumerTypes.All` (Step 1 in §3). The startup health check then guarantees that any production-environment row with a `sprk_consumertype` value NOT in `ConsumerTypes.All` will surface a warning at app boot. This is the line of defense against admins typing the consumer code freehand in Power Apps.

---

## §8. See also

| Document | Why you'd read it |
|---|---|
| [`docs/architecture/ai-architecture-playbook-consumer-routing.md`](../architecture/ai-architecture-playbook-consumer-routing.md) (READ-ONLY) | Runtime mechanics — cache TTL, `SelectBestMatch` algorithm, match-conditions JSON predicates, Path A/A.5/B decision matrix, R4 `/narrate` case study |
| [`docs/guides/PLAYBOOK-AUTHOR-GUIDE.md`](PLAYBOOK-AUTHOR-GUIDE.md) | Authoring the playbook itself (nodes, edges, Handlebars helpers, deploy script) |
| [`docs/guides/JPS-AUTHORING-GUIDE.md`](JPS-AUTHORING-GUIDE.md) | Authoring the JPS schema referenced by AI-driven nodes inside the playbook |
| [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) | BFF Hygiene — required pre-merge checklist when your new consumer adds endpoints or services to the BFF |
| Root [`CLAUDE.md`](../../CLAUDE.md) §10 (BFF Hygiene) | Binding governance — Placement Justification, publish-size verification, CVE scan |
| Root [`CLAUDE.md`](../../CLAUDE.md) §11 (Component Justification) | Three-question template: Existing / Extension / Cost-of-doing-nothing — apply before adding a new consumer surface |
| Spec FR-17 + FR-18 + FR-31 | The R7 functional requirements that drove the consumer-routing model and this guide |
| [`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/) | The actual interfaces — `ConsumerTypes.cs`, `IConsumerRoutingService.cs`, `IInvokePlaybookAi.cs` |

---

*Created in R7 Wave 6 task 067 (FR-31). The companion canonical architecture doc is owned by `spaarke-ai-platform-chat-routing-redesign-r1` and is READ-ONLY in this project. Future updates to routing mechanics belong there; future updates to maker tutorials belong here.*
