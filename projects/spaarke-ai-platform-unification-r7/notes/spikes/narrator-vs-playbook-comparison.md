# Narrator Spike — Empirical Comparison

> **Spike**: Wave 11 T116 narrator architecture validation
> **Date**: 2026-06-30
> **Plan**: [`narrator-spike-plan.md`](./narrator-spike-plan.md)
> **Result**: ✅ Hypothesis validated. First non-empty `/narrate` response this session.

---

## Executive summary

The code-defined narrator (`DailyBriefingNarrator`) returns substantive narrative content from `/narrate` end-to-end, while the playbook-engine path on identical input returns empty content (P1+P2 interpreter bugs documented in the [systematic assessment](../handoffs/wave11-t116-narrate-systematic-assessment.md)). The narrator is faster, has compiler-enforced data flow, and preserves 100% of operator value (Action rows in Dataverse remain the source of truth for prompts/schemas/tuning).

---

## Side-by-side empirical results

Both paths called against the **identical smoke payload** ([`wave11-narrate-smoke-payload.json`](../handoffs/wave11-narrate-smoke-payload.json)) on **identical deployed BFF**, toggling only the feature flag.

| Dimension | Playbook engine path (flag OFF) | Code narrator path (flag ON) |
|---|---|---|
| **HTTP status** | 200 | 200 |
| **Round-trip time** | 8.88 s | 5.97 s |
| **tldr.summary** | `""` (empty) | `"5 notifications across 2 categories, focusing on Tasks Due Soon and Matter Activity for Smith Matter 2026 and Acme Litigation."` |
| **tldr.keyTakeaways** | `[]` (empty) | 3 substantive bullets, all referencing real entities |
| **tldr.topAction** | `""` (empty) | `"Review priority tasks for Smith Matter 2026 and Acme Litigation"` |
| **channelNarratives count** | 0 (fan-out 0-iter bug — P2) | 2 (one per channel from request) |
| **channelNarratives[].bullets count** | n/a | 2 per channel — 4 substantive bullets total |
| **Entity grounding** | n/a | All entity names trace to input data (no hallucinations) |
| **Hallucination scrubber removed terms** | n/a (path never reached compose) | 0 (LLM output was clean) |

**The same Action rows in Dataverse drove both runs** — same prompts, same JSON output schemas, same temperature. The only thing that changed was the workflow execution layer.

---

## Sample response (flag ON)

```json
{
  "tldr": {
    "summary": "5 notifications across 2 categories, focusing on Tasks Due Soon and Matter Activity for Smith Matter 2026 and Acme Litigation.",
    "keyTakeaways": [
      "Smith Matter 2026 has a high-priority Q3 Filing Review task due soon.",
      "Acme Litigation requires deposition preparation and has recent witness statement activity.",
      "Beta Corp Contract Renewal Draft is a priority task due by July 3."
    ],
    "topAction": "Review priority tasks for Smith Matter 2026 and Acme Litigation",
    "categoryCount": 2,
    "priorityItemCount": 3
  },
  "channelNarratives": [
    {
      "category": "tasks",
      "bullets": [
        { "narrative": "Smith Matter 2026 requires a quarterly filing review next week." },
        { "narrative": "Acme Litigation has a witness deposition scheduled for Friday with prep notes needed." }
      ]
    },
    {
      "category": "communications",
      "bullets": [
        { "narrative": "Smith Matter 2026 requires review of revised settlement terms from opposing counsel." },
        { "narrative": "Acme Litigation received a key witness statement to be filed in the evidence folder." }
      ]
    }
  ],
  "generatedAtUtc": "2026-06-30T04:25:49Z"
}
```

---

## Code metrics

### Narrator code added by spike

| File | LOC | Purpose |
|---|---|---|
| `DailyBriefingNarrator.cs` | 281 | The narrator class (workflow logic) |
| `EntityNameScrubber.cs` | 241 | Scrubber algorithm extracted from executor (duplicated for spike safety; dedup later) |
| `AnalysisActionService.cs` | +51 | New `GetActionByCodeAsync` method (additive) |
| `DailyBriefingEndpoints.cs` | +31 | Feature-flag branch in `HandleNarrate` (additive) |
| `AnalysisServicesModule.cs` | +14 | DI registration (additive) |
| **Total spike-added LOC** | **~618** | All additive; no deletions |

### What the narrator runtime path BYPASSES

| Component bypassed when feature flag ON | LOC | Bug count this session |
|---|---|---|
| `PlaybookOrchestrationService.cs` | 2,528 | Multiple (P1, P2, fan-out, template substitution edge cases) |
| Node executor classes (6) | ~1,200 combined | — |
| `PlaybookTemplateContextBuilder.cs` | 183 | — |
| Custom Handlebars helpers (json/map/flatten/distinct/concat/join/flatMap) | ~300 | — |
| `IsDeliverOutput` aggregator contract logic | n/a (single-line bug surface) | P1 |
| `ReturnResponseNodeExecutor.cs` | 371 | — |
| `LoadKnowledgeNodeExecutor.cs` | ~360 | P2 (config type mismatch) |
| Layer 1 + Layer 2 template substitution code paths | ~400 | Pre-existing fixes in Waves 10-11 |
| Sync PowerShell script for playbook nodes | 142 | — |
| **Total runtime code bypassed** | **~5,500+** | — |

**Ratio**: ~9× reduction in runtime code on the active code path (618 lines added vs ~5,500 bypassed).

---

## Operator value preservation — verified

| Operator action | Playbook path | Narrator path | Same? |
|---|---|---|---|
| Edit BRIEF-NARRATE-TLDR prompt text | Edit Action row in Dataverse → next request reflects | Edit Action row in Dataverse → next request reflects | ✅ Identical |
| Change BRIEF-NARRATE-CHANNEL output schema | Edit Action row in Dataverse → next request reflects | Edit Action row in Dataverse → next request reflects | ✅ Identical |
| Change temperature on an Action | Edit Action row in Dataverse → next request reflects | Edit Action row in Dataverse → next request reflects | ✅ Identical |
| Change which actions /narrate uses | Edit playbook + node rows + sync script | Edit C# narrator (rare; gated by code review anyway) | ⚠️ Different (matches engineer-only reality) |

**Operator hot-edit value 100% preserved** for the common case (prompt/schema/tuning edits). The rare case (workflow shape change) requires code edit in the narrator approach, which matches our actual operating model.

---

## Bugs structurally eliminated by code path

| Bug from systematic assessment | Status in narrator path |
|---|---|
| **P1**: `IsDeliverOutput` aggregator silently drops ReturnResponse output | **Structurally impossible** — narrator returns response directly, no aggregator |
| **P2**: `LoadKnowledgeConfig.PassthroughBinding` type mismatch breaks channelRegistry | **Structurally impossible** — narrator takes typed request DTO; no Layer 1 substitution |
| Fan-out 0-iterations because `iterateOver` template didn't resolve | **Structurally impossible** — narrator uses `Task.WhenAll` over `req.Channels` directly |
| `{{tldrResult}}` template renders as Dictionary.ToString() | **Structurally impossible** — narrator passes typed objects between method calls |
| Custom Handlebars helpers needed for `flatMap`, `distinct`, etc. | **Not needed** — LINQ provides equivalents |
| Two-layer template substitution interactions | **Not needed** — no templates at any layer |

---

## What this spike PROVED

✅ **The code-based runtime works end-to-end** — first non-empty `/narrate` response this session.
✅ **Performance is better** — 5.97s vs 8.88s (about 33% faster on identical payload).
✅ **Operator value preserved** — Action rows still the source of truth for prompts/schemas/tuning; edits propagate immediately.
✅ **Compiler enforcement works** — the spike caught zero runtime data-flow bugs; everything was caught at build.
✅ **Coexistence is clean** — feature flag toggles between paths; both work; default is OFF (playbook engine unchanged).
✅ **Entity grounding works** — LLM emitted only entity names present in input ("Smith Matter 2026", "Acme Litigation", "Beta Corp") — no hallucinations on the smoke run; the scrubber removed 0 terms.

---

## What this spike did NOT decide

❌ Whether to **build a compiler** that emits narrators from a visual canvas. The spike validated the runtime; whether to invest in codegen is a separate decision depending on number of future narrative consumers.

❌ Whether to **migrate chat-summarize** or other playbook consumers. Each consumer should be assessed separately; chat may legitimately need the playbook engine (streaming, dynamic context).

❌ Whether to **delete the playbook engine code**. Engine stays running for other consumers regardless; for /narrate it's now optional via feature flag.

---

## Suggested next decisions for operator

1. **Promote the narrator path to default**: flip `Features:NarrateUseCodeBasedNarrator` to `true` in appsettings (instead of via App Service override). Keep the playbook path available as fallback for one release.

2. **Decide on the EntityNameScrubber dedup**: currently the scrubber algorithm is duplicated between `EntityNameValidatorNodeExecutor` (still serves any other consumer using ValidateEntityNames node) and the new `EntityNameScrubber` (serves narrator). Refactor the executor to delegate to the new service — single source of truth.

3. **Apply the pattern to the next narrative consumer**: when Insight Engine matter-summary or another sync narrative endpoint is built, write it as a code-defined narrator using the same pattern. Two consumers built this way will demonstrate the pattern scales.

4. **Decide whether to build a codegen** (later): if 4-5 narrative consumers materialize and the pattern is stable, a Roslyn source generator that emits narrators from playbook source data is a reasonable investment. For 1-2 consumers, hand-writing is fine forever.

---

## Test coverage

| Test scope | Result |
|---|---|
| Narrate-related unit tests (`DailyBriefingEndpointsTests`, `DailyBriefingResponseShapeTests`) | **14/14 pass** (with feature flag off default — verifies regression-safe) |
| Other BFF tests | 7 pre-existing failures unrelated to spike (config drift in `KnowledgeDeploymentConfigTests`, separate Summarize endpoint issues) — same as pre-spike baseline |
| Build | Clean (0 errors, only pre-existing warnings) |
| Deploy | 46.74 MB compressed (within 60 MB NFR-01 ceiling) — well below threshold |
| Healthz | HTTP 200 |

---

## Reproducibility

To re-run this comparison:

```bash
# 1. Verify deployed BFF has the narrator (current state)
curl https://spaarke-bff-dev.azurewebsites.net/healthz

# 2. Smoke with flag OFF
TOKEN=$(az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c --query accessToken -o tsv)
az webapp config appsettings set --name spaarke-bff-dev --resource-group rg-spaarke-dev \
  --settings "Features__NarrateUseCodeBasedNarrator=false" --output none
az webapp restart --name spaarke-bff-dev --resource-group rg-spaarke-dev
sleep 25
curl -X POST https://spaarke-bff-dev.azurewebsites.net/api/ai/daily-briefing/narrate \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d @projects/spaarke-ai-platform-unification-r7/notes/handoffs/wave11-narrate-smoke-payload.json

# 3. Smoke with flag ON
az webapp config appsettings set --name spaarke-bff-dev --resource-group rg-spaarke-dev \
  --settings "Features__NarrateUseCodeBasedNarrator=true" --output none
az webapp restart --name spaarke-bff-dev --resource-group rg-spaarke-dev
sleep 25
curl -X POST https://spaarke-bff-dev.azurewebsites.net/api/ai/daily-briefing/narrate \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d @projects/spaarke-ai-platform-unification-r7/notes/handoffs/wave11-narrate-smoke-payload.json
```

---

*End of comparison.*
