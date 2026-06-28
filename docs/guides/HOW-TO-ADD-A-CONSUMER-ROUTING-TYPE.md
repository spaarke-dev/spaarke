# How to Add a Consumer Routing Type

> **Audience**: BFF developers, AI platform engineers, Action Engine authors
> **Pre-req reading**: [`docs/architecture/ai-architecture-playbook-consumer-routing.md`](../architecture/ai-architecture-playbook-consumer-routing.md) (especially §1 Triangle + §2 Why this was created)
> **When to use this guide**: You are adding a NEW consumer surface (BFF service, endpoint, Agent type, widget dispatch path) that needs to invoke a playbook via Dataverse-driven routing
> **When NOT to use this guide**: You are adding a new playbook variant for an EXISTING consumer type — in that case you just add a new `sprk_playbookconsumer` row (or change an existing row's `sprk_playbookid`) and you're done; skip to §5

---

## 1. Decision checklist — should this even use consumer routing?

Before adding a new consumer type, confirm Path A.5 is the right dispatch path. Quick checklist:

- [ ] My consumer is a CRUD-side service / endpoint that needs to invoke a playbook by NAME (not by GUID burned into code)
- [ ] My consumer does NOT need streaming SSE back to a client (otherwise use Path A or Path B)
- [ ] My consumer's playbook target may legitimately change without a BFF deploy (e.g., maker can swap a playbook for an updated version)
- [ ] My consumer fits within ADR-013 — I'm a CRUD-side caller wanting AI capability, and the facade boundary `Services/Ai/PublicContracts/` is the right place to depend on

If all four boxes are checked: proceed. If not: revisit the [Path A / A.5 / B decision matrix](../architecture/ai-architecture-playbook-consumer-routing.md#8-path-a--a5--b-decision-matrix).

---

## 2. The 3-step recipe

### Step 1 — Add a compile-time constant to `ConsumerTypes.cs`

Edit [`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs):

```csharp
public static class ConsumerTypes
{
    // ... existing constants ...

    /// <summary>
    /// <c>MyNewService</c> — short description of what this consumer does and
    /// which playbook family it dispatches.
    /// </summary>
    public const string MyNewConsumer = "my-new-consumer";   // lower-kebab-case

    /// <summary>
    /// Read-only list of all consumer-type constants.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        // ... existing entries ...
        MyNewConsumer,    // <-- ADD HERE
    };
}
```

**Naming rules** (these are not enforced by code but are conventional):

- Lower-kebab-case (`matter-pre-fill`, NOT `MatterPreFill` or `matter_pre_fill`)
- No spaces, no underscores, no leading numbers
- Stable — once shipped to production, the constant value MUST NOT be renamed (it's the contract with the Dataverse row)
- Self-describing — `chat-summarize` is good; `cs1` is not

### Step 2 — Create the `sprk_playbookconsumer` row in Dataverse

Choose ONE of these paths:

#### Option A — Extend the seed script (preferred for standard rows)

Edit [`scripts/dataverse/Seed-PlaybookConsumers.ps1`](../../scripts/dataverse/Seed-PlaybookConsumers.ps1):

```powershell
# Append to the $rows array:
@{
    consumertype = "my-new-consumer"          # MUST match ConsumerTypes constant value
    consumercode = "default"                  # or area-specific code if applicable
    environment  = "*"                        # or "dev" / "test" / "prod" for env-specific
    priority     = 500                        # lowest wins; 100 = override, 500 = normal, 900 = fallback
    enabled      = $true
    playbookKey  = "MY-NEW-PLAYBOOK@v1"       # canonical playbook code (resolved to GUID by script)
}
```

Then run: `pwsh scripts/dataverse/Seed-PlaybookConsumers.ps1 -Environment dev`

#### Option B — Use the one-off script (for environment-specific or experimental rows)

```powershell
pwsh scripts/dataverse/Add-PlaybookConsumer.ps1 `
    -ConsumerType "my-new-consumer" `
    -ConsumerCode "default" `
    -Environment "dev" `
    -PlaybookKey "MY-NEW-PLAYBOOK@v1" `
    -Priority 500
```

#### Option C — Power Apps maker UI (NOT preferred — no audit trail)

Navigate to: Power Apps maker portal → Tables → Playbook Consumer → New row.

Use this for inspection or incident-response disable; avoid for new-feature deploys (no audit, hard to repro across environments).

### Step 3 — Wire the calling surface

Inject `IConsumerRoutingService` and `IInvokePlaybookAi`. Follow the canonical [runtime code skeleton](../architecture/ai-architecture-playbook-consumer-routing.md#5-how-a-typical-resolution-looks-at-runtime) in the architecture doc. Tight version:

```csharp
public sealed class MyNewService
{
    private readonly IConsumerRoutingService _routing;
    private readonly IInvokePlaybookAi _invokePlaybook;

    public MyNewService(
        IConsumerRoutingService routing,
        IInvokePlaybookAi invokePlaybook)
    {
        _routing = routing;
        _invokePlaybook = invokePlaybook;
    }

    public async Task<MyResult> ExecuteAsync(MyRequest request, HttpContext ctx, CancellationToken ct)
    {
        var playbookId = await _routing.ResolveAsync(
            ConsumerTypes.MyNewConsumer,    // <-- compile-time constant
            consumerCode: "default",
            context: null,
            environment: null,
            ct);

        if (playbookId is null)
        {
            return MyResult.ServiceUnavailable("Dispatch unconfigured.");
        }

        var parameters = BuildParameters(request);
        var invocationContext = BuildInvocationContext(ctx);

        var result = await _invokePlaybook.InvokePlaybookAsync(
            playbookId.Value, parameters, invocationContext, ct);

        return result.Success
            ? MyResult.From(result)
            : MyResult.AiUnavailable(result.ErrorMessage);
    }
}
```

DI registration (in your appropriate module — usually existing AI services module is fine):

```csharp
services.AddScoped<MyNewService>();
```

`IConsumerRoutingService` and `IInvokePlaybookAi` are already registered in `AnalysisServicesModule` — no new module registration needed (ADR-010 minimalism).

---

## 3. Worked example — adding `outside-counsel-summarize`

Suppose you want to add a new consumer type for summarizing outside-counsel work product (a hypothetical P3 feature). Walkthrough:

### Step 1 — Constant

```csharp
// In ConsumerTypes.cs
public const string OutsideCounselSummarize = "outside-counsel-summarize";

// Append to All:
public static readonly IReadOnlyList<string> All = new[]
{
    MatterPreFill, ProjectPreFill, AiSummary, SummarizeFile,
    ChatSummarize, EmailAnalysis, DailyBriefingNarrate,
    OutsideCounselSummarize,    // <-- new
};
```

### Step 2 — Dataverse row

```powershell
pwsh scripts/dataverse/Add-PlaybookConsumer.ps1 `
    -ConsumerType "outside-counsel-summarize" `
    -ConsumerCode "default" `
    -Environment "*" `
    -PlaybookKey "OUTSIDE-COUNSEL-WORK-PRODUCT-SUMMARIZE@v1" `
    -Priority 500
```

(Assuming the playbook has already been authored + deployed via `/jps-playbook-design`.)

### Step 3 — Service

```csharp
public sealed class OutsideCounselSummarizeService
{
    private readonly IConsumerRoutingService _routing;
    private readonly IInvokePlaybookAi _invokePlaybook;

    public OutsideCounselSummarizeService(
        IConsumerRoutingService routing,
        IInvokePlaybookAi invokePlaybook)
    {
        _routing = routing;
        _invokePlaybook = invokePlaybook;
    }

    public async Task<OutsideCounselSummaryResult> SummarizeAsync(
        OutsideCounselWorkProduct workProduct,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var playbookId = await _routing.ResolveAsync(
            ConsumerTypes.OutsideCounselSummarize,
            consumerCode: "default",
            context: null,
            environment: null,
            ct);

        if (playbookId is null)
        {
            return OutsideCounselSummaryResult.Unconfigured;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workProduct"] = JsonSerializer.Serialize(workProduct),
            ["matterId"] = workProduct.MatterId.ToString(),
        };

        var result = await _invokePlaybook.InvokePlaybookAsync(
            playbookId.Value,
            parameters,
            BuildInvocationContext(httpContext),
            ct);

        return result.Success
            ? OutsideCounselSummaryResult.From(result)
            : OutsideCounselSummaryResult.AiUnavailable(result.ErrorMessage);
    }
}
```

That's the entire integration. No new endpoint filter. No new orchestrator code. No new feature flag.

---

## 4. Verification checklist (run before declaring done)

- [ ] `dotnet build` passes after `ConsumerTypes.cs` edit
- [ ] `Seed-PlaybookConsumers.ps1` (or `Add-PlaybookConsumer.ps1`) ran without error and the row exists in Dataverse: `dataverse.read_query("sprk_playbookconsumers", filter: "sprk_consumertype eq 'my-new-consumer'")`
- [ ] Wait 5 minutes (cache lag) OR restart the BFF App Service before the first end-to-end test
- [ ] First end-to-end call returns 200 with expected output shape (not 503 "Dispatch unconfigured")
- [ ] Telemetry: `consumerType=my-new-consumer` + `cacheHit=false` (first call), then `cacheHit=true` on subsequent calls within 5 minutes
- [ ] Unit test on the service uses a mock `IConsumerRoutingService` that returns a fixed Guid (don't depend on real Dataverse in unit tests)
- [ ] Integration test exercises the full Path A.5 round-trip with a real (seeded) consumer row in a test environment

---

## 5. Adding a NEW PLAYBOOK to an existing consumer (no code change)

If the consumer type already exists and you just want to redirect it to a different playbook (or add a variant):

### Redirecting an existing consumer to a new playbook

```powershell
# Option 1: Edit the existing row's playbookid
pwsh scripts/dataverse/Add-PlaybookConsumer.ps1 `
    -ConsumerType "chat-summarize" `
    -ConsumerCode "default" `
    -PlaybookKey "SUM-CHAT@v2" `      # <-- new version
    -Upsert
```

### Adding a variant (e.g., area-specific summarize)

```powershell
# Option 2: Add a NEW row with a specific consumerCode + higher priority (lower number)
pwsh scripts/dataverse/Add-PlaybookConsumer.ps1 `
    -ConsumerType "chat-summarize" `
    -ConsumerCode "vendor-contract" `
    -PlaybookKey "SUMMARIZE-VENDOR-CONTRACT@v1" `
    -Priority 100
```

Now `_routing.ResolveAsync(ChatSummarize, code: "vendor-contract")` returns the new playbook; `code: "default"` continues to return the original.

**No BFF code change. No deploy. 5 minutes of cache lag.** This is the design payoff.

---

## 6. Common pitfalls

| Pitfall | Fix |
|---|---|
| Typed a literal `"my-new-consumer"` in service code instead of `ConsumerTypes.MyNewConsumer` | Use the constant. The whole point of `ConsumerTypes.cs` is compile-time safety. |
| Created a Dataverse row with `sprk_consumertype` that DOESN'T match the constant value | The Dataverse-side string is the contract. Misspelling here causes silent fall-through. Use `ConsumerTypes.All` as your spell-check reference. Task 028e (chat-routing-redesign-r1 Phase 1R exit gate) will add a startup health-log to surface this. |
| Forgot to add the new constant to `ConsumerTypes.All` | Self-correcting once the startup health-log lands (task 028e), but until then it's silent. Always update `All` alongside the new constant. |
| Tested immediately after row creation and got 503 | Cache lag — wait 5 minutes or restart the BFF. There is no explicit invalidation hook (out of scope for R4). |
| Used `sprk_priority` for permissioning ("only admin Agents get the high-priority playbook") | Priority is a TIEBREAKER, not an authorization mechanism. Permissioning belongs upstream at the endpoint auth filter or at the Action Engine policy layer. Never at routing. |
| Stuffed executor config into `sprk_matchconditionsjson` | `matchconditionsjson` is for selecting WHICH ROW WINS, not for executor config. Per-node config goes in `sprk_playbooknode.sprk_configjson`. |
| Created a consumer-type-string that contains `/` or other path separators | The string is used in cache keys + telemetry dimensions; keep it URL-safe and lower-kebab-case. |
| Added a new consumer type but the test fixture in `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Mocks/MockConsumerRoutingService.cs` (or similar) doesn't have a case for it | Update the mock to return a deterministic test Guid for the new consumer type, OR ensure the test passes the mock through a `Setup(...)` call for that specific type. |

---

## 7. Related docs

| Doc | Topic |
|---|---|
| [`docs/architecture/ai-architecture-playbook-consumer-routing.md`](../architecture/ai-architecture-playbook-consumer-routing.md) | Architecture, triangle, semantics, Path A.5, Action Engine relationship |
| [`docs/data-model/sprk-playbookconsumer.md`](../data-model/sprk-playbookconsumer.md) | Full schema reference for the routing entity |
| `docs/architecture/ai-architecture-playbook-runtime.md` | How the orchestrator executes the resolved playbook |
| `.claude/skills/jps-playbook-design/SKILL.md` | How to author a new playbook (before routing it from a consumer) |
| [`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs) | The compile-time constants |
