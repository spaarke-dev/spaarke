# Build a New Narrative-Output Consumer

> **Audience**: Playbook + Action JPS authors building new "LLM produces structured output from playbook runtime data" features for the Workspace UX, Insights Engine, or other Spaarke surfaces.
> **Architecture reference**: [`docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](../architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md)
> **Last Updated**: 2026-06-29 (R7 Wave 11 task 111a)

---

## 1. Decision tree — does this pattern apply?

```
Is the output: structured (typed fields, schema-constrained) from an LLM?
  └─ NO → not this pattern.
       │     - Plain text from LLM? Consider direct IOpenAiClient.GetCompletionAsync.
       │     - Computation only (no LLM)? Write straight C#.
       │     - Chat session with multi-turn + RAG? Use SprkChat.
       └─ YES → next question

Is the input: derived at runtime from a playbook graph (Start → Query → AI → Destination)?
  └─ NO → not this pattern.
       │     - Single-document analysis? AiAnalysis (with RAG / tool handlers).
       │     - Direct request → LLM → response (no intermediate nodes)? Consider direct IOpenAiClient.
       └─ YES → next question

Does the output need to land somewhere structured (Dataverse field, widget, email)?
  └─ YES → ✅ Use this pattern.
       │     - Widget (HTTP response): ReturnResponse terminal node.
       │     - Dataverse field write: UpdateRecord downstream node.
       │     - Email: SendEmail downstream node.
       │     - Notification: CreateNotification downstream node.
       │     - Multiple destinations: compose downstream nodes in playbook.
       └─ NO → reconsider the requirement; structured output without a destination is rare.
```

If you reached "✅ Use this pattern" — continue to §2.

---

## 2. Pre-flight checklist

Before authoring:

- [ ] Dataverse access to your environment (e.g., spaarkedev1) — you'll be deploying Action + Playbook rows
- [ ] Understanding of the [data flow](../architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md#5-runtime-data-flow-6-step-trace) (read it once before authoring)
- [ ] Decide the input shape: what runtime data does the LLM need? Categories? A single matter's metrics? An array of items?
- [ ] Decide the output shape: what fields does the LLM produce? Define them as a JSON Schema (you'll write this into the Action's `sprk_outputschemajson` column)
- [ ] Decide the destination: a Dataverse field write? A widget HTTP response? A notification?
- [ ] Decide what triggers the playbook: HTTP endpoint? Scheduler? Form save?

---

## 3. Step-by-step tutorial

### Step 1 — Decide the trigger

The playbook needs a caller. Three common shapes in Spaarke:

| Trigger | Wiring | Example |
|---|---|---|
| BFF HTTP endpoint | New endpoint in `src/server/api/Sprk.Bff.Api/Api/Ai/` calling `IConsumerRoutingService` → `IInvokePlaybookAi` | `/api/ai/daily-briefing/narrate` |
| Background scheduler | Job handler in `Services/Jobs/` | (deferred for R7) |
| Form / save event | Power Apps form save → Dataverse webhook → BFF endpoint | (not yet built) |

For new endpoints, see [`docs/architecture/ai-architecture-consumer-routing.md`](../architecture/ai-architecture-consumer-routing.md) and the existing `/narrate` endpoint at [`src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs`](../../src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs).

### Step 2 — Author the Action JPS

The Action defines the LLM prompt + output schema. Create one JSON file per Action under `projects/{your-project}/notes/playbooks/actions/`. **The body becomes `sprk_systemprompt` on the deployed `sprk_analysisaction` row.**

Template ([JPS-AUTHORING-GUIDE](JPS-AUTHORING-GUIDE.md) for the canonical spec):

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "_dataverseRow": {
    "_comment": "Stripped before deploy; populates Dataverse columns.",
    "sprk_name": "Matter Performance Summarize",
    "sprk_actioncode": "MATTER-METRICS-SUMMARIZE",
    "sprk_actionid": "MATTER-METRICS-SUMMARIZE",
    "sprk_executoractiontype": 1,
    "sprk_temperature": 0,
    "sprk_outputformat": 0,
    "sprk_description": "Summarize a matter's performance metrics into a 3-5 sentence status paragraph.",
    "sprk_tags": "matter, performance, summarize, insight-engine",
    "statecode": 0,
    "statuscode": 1
  },
  "instruction": {
    "role": "legal-matter performance analyst",
    "task": "Read the matter's performance metrics in the structured input below. Produce a one-paragraph status summary suitable for display on the matter overview record. Reference the matter by name and the as-of date. Flag any metric outside its expected range.",
    "constraints": [
      "Use only data present in the input.",
      "Keep summary to 3-5 sentences.",
      "Use plain professional English (no markdown, no bullet lists)."
    ],
    "context": "This summary renders on the matter overview form as a read-only Performance Summary field."
  },
  "input": {
    "parameters": {
      "description": "matterName (string); metrics[] (array of { name, value, threshold, unit }); asOfDate (ISO 8601)."
    }
  },
  "output": {
    "fields": [
      { "name": "summaryText", "type": "string", "description": "The 3-5 sentence summary paragraph.", "maxLength": 1500 }
    ],
    "structuredOutput": true
  },
  "examples": [
    {
      "input": "{ \"matterName\": \"<matter-name-1>\", \"metrics\": [...], \"asOfDate\": \"...\" }",
      "output": { "summaryText": "<matter-name-1> (as of ...): ... All metrics within expected ranges." }
    }
  ],
  "metadata": {
    "author": "your-name",
    "createdAt": "YYYY-MM-DDTHH:MM:SSZ",
    "description": "...",
    "tags": ["matter", "performance", "summarize"]
  }
}
```

**Critical authoring rules**:

- ✅ `instruction.task` describes WHAT to do. It does NOT reference the input data with `{{X}}` placeholders. The data arrives via the `## Input` section (rendered automatically by PromptSchemaRenderer when the AI executor passes `runtimeInput`).
- ✅ `input.parameters.description` documents the expected input shape — this is documentation for AI maintainers + future authors, NOT a runtime injection point. The actual input value flows in via the playbook's `inputBinding` (Step 4).
- ✅ `output.fields[]` defines the LLM's structured output. **This becomes `sprk_outputschemajson` on the Dataverse row** — used by `IOpenAiClient.GetStructuredCompletionRawAsync` to constrain the LLM via `response_format: json_schema`.
- ✅ `examples[]` use placeholder names (`<matter-name-1>`, etc.) — never real firm/case names (per FR-13 anti-hallucination discipline).

### Step 3 — Compose the playbook

The playbook defines the node graph. Create one JSON file at `projects/{your-project}/notes/playbooks/{playbook-code}.json`. Structure:

```json
{
  "_dataverseRow": {
    "sprk_name": "Matter Performance Summarize",
    "sprk_playbookcode": "MATTER-PERF",
    "sprk_playbooktype": 0,
    "statecode": 0,
    "statuscode": 1
  },
  "nodes": [
    {
      "name": "Start",
      "nodeType": "Control",
      "actionType": null,
      "canvasType": "start",
      "outputVariable": "start",
      "positionX": 100, "positionY": 200,
      "dependsOn": [],
      "configJson": {
        "scope": "matter-input",
        "description": "Receives { matterId, matterName } from the trigger.",
        "inputContract": {
          "matterId": "Guid",
          "matterName": "string"
        }
      }
    },
    {
      "name": "QueryMetrics",
      "nodeType": "Workflow",
      "actionType": 51,
      "canvasType": "queryDataverse",
      "outputVariable": "metricsQueryResult",
      "positionX": 300, "positionY": 200,
      "dependsOn": ["Start"],
      "configJson": {
        "__actionType": 51,
        "actionCode": "SYS-QUERY-DV",
        "fetchXml": "<fetch top='50'><entity name='sprk_performancemetric'><attribute name='sprk_name' /><attribute name='sprk_value' /><attribute name='sprk_threshold' /><attribute name='sprk_unit' /><filter><condition attribute='sprk_matterid' operator='eq' value='{{start.matterId}}' /></filter></entity></fetch>"
      }
    },
    {
      "name": "SummarizeMetrics",
      "nodeType": "AiAnalysis",
      "actionType": 1,
      "canvasType": "skillNode",
      "outputVariable": "metricsSummary",
      "positionX": 500, "positionY": 200,
      "dependsOn": ["QueryMetrics"],
      "configJson": {
        "__actionType": 1,
        "actionCode": "MATTER-METRICS-SUMMARIZE",
        "actionId": "MATTER-METRICS-SUMMARIZE",
        "modelSelection": { "useActionDefaults": true },
        "inputBinding": {
          "matterName": "{{start.matterName}}",
          "metrics": "{{json metricsQueryResult.records}}",
          "asOfDate": "{{run.completedAtUtc}}"
        }
      }
    },
    {
      "name": "UpdateMatter",
      "nodeType": "Workflow",
      "actionType": 22,
      "canvasType": "updateRecord",
      "outputVariable": "matterUpdate",
      "positionX": 700, "positionY": 200,
      "dependsOn": ["SummarizeMetrics"],
      "configJson": {
        "__actionType": 22,
        "entityName": "sprk_matter",
        "recordId": "{{start.matterId}}",
        "fieldMappings": {
          "sprk_performancesummary": "{{metricsSummary.summaryText}}"
        }
      }
    },
    {
      "name": "ReturnResponse",
      "nodeType": "Output",
      "actionType": 143,
      "canvasType": "returnResponse",
      "outputVariable": "response",
      "positionX": 900, "positionY": 200,
      "dependsOn": ["UpdateMatter"],
      "configJson": {
        "responseBinding": {
          "status": "ok",
          "matterId": "{{start.matterId}}",
          "summaryText": "{{metricsSummary.summaryText}}",
          "generatedAtUtc": "{{run.completedAtUtc}}"
        }
      }
    }
  ]
}
```

**Critical authoring rules**:

- ✅ `inputBinding.<key>` values are Handlebars templates. They get resolved against `runContext.NodeOutputs + Parameters + run` by Layer 1 (the orchestrator). Reference upstream nodes by their `outputVariable` (e.g., `{{start.matterName}}`, `{{metricsQueryResult.records}}`).
- ✅ The key inside `inputBinding` becomes the JSON key the LLM sees in the `## Input` section (e.g., `inputBinding.matterName` → `"## Input\n{ \"matterName\": \"Acme 2026\", ... }"`).
- ✅ Use `{{json X}}` when you want the value serialized as JSON inline (instead of `.ToString()`). Required for arrays/objects you want the LLM to traverse.
- ✅ Set node `dependsOn` array so the orchestrator knows execution order.
- ✅ `outputVariable` on each node names the slot in `NodeOutputs` that downstream `{{<varname>.field}}` references read from.

### Step 4 — Wire the destination

Three common destinations:

**A. Write to a Dataverse field (UpdateRecord node)**
```json
{
  "name": "UpdateMatter",
  "actionType": 22,
  "configJson": {
    "entityName": "sprk_matter",
    "recordId": "{{start.matterId}}",
    "fieldMappings": {
      "sprk_performancesummary": "{{metricsSummary.summaryText}}"
    }
  }
}
```

**B. Return as HTTP response (ReturnResponse terminal node)**
```json
{
  "name": "ReturnResponse",
  "actionType": 143,
  "configJson": {
    "responseBinding": {
      "tldr": "{{tldrResult}}",
      "channelNarratives": "{{channelNarrationResults}}",
      "generatedAtUtc": "{{run.completedAtUtc}}"
    }
  }
}
```

**C. Create a notification (CreateNotification node)**
```json
{
  "name": "CreateNotif",
  "actionType": 50,
  "configJson": {
    "title": "Performance summary updated",
    "body": "{{metricsSummary.summaryText}}",
    "regardingEntityType": "sprk_matter",
    "regardingId": "{{start.matterId}}"
  }
}
```

Compose multiple destination nodes if you want side effects (e.g., UpdateMatter AND CreateNotif AND ReturnResponse).

### Step 5 — Deploy

**A. Action JPS → `sprk_analysisaction` row**

Most actions deploy via [`scripts/Deploy-AnalysisAction.ps1`](../../scripts/Deploy-AnalysisAction.ps1) (script reads manifest, PATCHes the row). For one-off authoring, MCP `mcp__dataverse__create_record` works.

After deploy, populate `sprk_outputschemajson` (the JSON Schema derived from `output.fields[]`). For BRIEF-NARRATE-style actions, model your sync script on [`scripts/dataverse/Sync-BriefNarrateOutputSchemas.ps1`](../../scripts/dataverse/Sync-BriefNarrateOutputSchemas.ps1).

**B. Playbook + nodes → `sprk_analysisplaybook` + `sprk_playbooknode` rows**

Use [`scripts/Deploy-Playbook.ps1`](../../scripts/Deploy-Playbook.ps1) (canonical post-Wave-5 path; writes `sprk_executortype` explicitly per FR-20). For one-off, MCP works.

After deploy, MCP `mcp__dataverse__read_query` to verify all nodes have non-null `sprk_executortype` + correct `sprk_configjson`. See [`docs/guides/ai-guide-playbook-deploy-recipe.md`](ai-guide-playbook-deploy-recipe.md) for the full recipe.

**C. Register the consumer (if HTTP endpoint trigger)**

Add a `sprk_playbookconsumer` row pointing your consumer code to the deployed playbook GUID. See [`docs/guides/ai-guide-consumer-wiring.md`](ai-guide-consumer-wiring.md) for the canonical wiring procedure.

### Step 6 — Test

**A. Curl smoke (against your deployed endpoint)**
```bash
TOKEN=$(az account get-access-token --resource <bff-client-id> --query accessToken -o tsv)
curl -X POST https://<spaarkedev1-bff>/api/your/endpoint \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"matterId":"...","matterName":"Test Matter"}'
```

Expected: HTTP 200 with non-empty structured response matching your Action's output schema.

**B. Inspect Dataverse**

If your destination is `UpdateRecord`, query the target row to confirm the field was populated:
```
MCP: SELECT sprk_performancesummary FROM sprk_matter WHERE sprk_matterid = '...'
```

**C. Inspect the App Service logs**

Look for `AiCompletion node {NodeId}` log lines. The runtime-resolved configJson (after Layer 1) appears in debug logs. If the LLM received empty input, check whether `ExtractInputBindingAsJsonElement` logged a warning (malformed configJson) — that's the most common author error.

---

## 4. Worked example 1 — Daily Briefing TL;DR (shipped reference)

The canonical reference. Live in production as of R7 Wave 11.

**Files**:
- Action JPS: [`projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-tldr.action.json`](../../projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-tldr.action.json)
- Action JPS: [`brief-narrate-channel.action.json`](../../projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-channel.action.json)
- Playbook: [`daily-briefing-narrate.json`](../../projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json)
- Endpoint: [`DailyBriefingEndpoints.cs:202`](../../src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs#L202)

**Structure**: Start → LoadKnowledge → (GenerateTldr ‖ GenerateChannelNarratives parallel) → ValidateEntityNames (post-LLM scrubber) → ReturnResponse.

**Output destination**: HTTP response → `useBriefingNarration.ts` widget hook → Daily Briefing widget UI.

**What to study**:
- Note `instruction.task` in both BRIEF-NARRATE actions — pure instructions, no `{{X}}` for data.
- Note `inputBinding.briefing = "{{json start}}"` in GenerateTldr — the whole Start payload becomes the LLM's `## Input`.
- Note `responseBinding` in ReturnResponse — the final widget response is composed from multiple node outputs.

---

## 5. Worked example 2 — Insight Engine Matter Performance Summary (design / not yet shipped)

Future Workspace UX feature: matter overview form shows a Performance Summary read-only field, auto-updated when metrics change.

**Files** (when this ships):
- Action JPS: `projects/spaarke-insight-engine-rN/notes/playbooks/actions/matter-metrics-summarize.action.json`
- Playbook: `projects/spaarke-insight-engine-rN/notes/playbooks/matter-performance-summarize.json`
- Destination: existing `sprk_matter.sprk_performancesummary` field (no new endpoint needed)
- Trigger: form save webhook OR scheduled scan (TBD per Insight Engine team)

**Structure**: Start (matterId, matterName) → QueryMetrics (Dataverse query for sprk_performancemetric) → SummarizeMetrics (AiCompletion with action MATTER-METRICS-SUMMARIZE) → UpdateMatter (writes summaryText to sprk_matter.sprk_performancesummary) → ReturnResponse (optional).

**Sample inputs / outputs**:

Input payload to SummarizeMetrics (assembled by Layer 1 from inputBinding):
```json
{
  "matterName": "Acme Litigation 2026",
  "metrics": [
    {"name": "Hours billed", "value": 145, "threshold": 200, "unit": "h"},
    {"name": "Days since last activity", "value": 3, "threshold": 7, "unit": "days"},
    {"name": "Open tasks", "value": 4, "threshold": 10, "unit": "tasks"}
  ],
  "asOfDate": "2026-06-29T17:36:15Z"
}
```

What the LLM sees in its prompt (assembled by PromptSchemaRenderer):
```
legal-matter performance analyst

Read the matter's performance metrics in the structured input below. Produce a one-paragraph status summary suitable for display on the matter overview record. ...

## Constraints
1. Use only data present in the input.
2. Keep summary to 3-5 sentences.
3. Use plain professional English (no markdown, no bullet lists).

This summary renders on the matter overview form as a read-only Performance Summary field.

## Input
{
  "matterName": "Acme Litigation 2026",
  "metrics": [
    {"name":"Hours billed","value":145,"threshold":200,"unit":"h"},
    ...
  ],
  "asOfDate": "2026-06-29T17:36:15Z"
}
```

LLM response (constrained by output schema):
```json
{
  "summaryText": "Acme Litigation 2026 (as of 2026-06-29): 145 hours billed against a 200-hour threshold (72% utilization). Last activity 3 days ago, well within the 7-day stale threshold. 4 open tasks against a 10-task ceiling. All performance metrics are currently within expected ranges; no items flagged."
}
```

Then UpdateMatter writes `metricsSummary.summaryText` to `sprk_matter.sprk_performancesummary`. The form's read-only Performance Summary field renders that text on next load.

**What this demonstrates**: same architectural primitives as Daily Briefing. Different playbook composition. Different destination. **Zero new C#.**

---

## 6. Common gotchas

| Symptom | Fix |
|---|---|
| `{{X}}` appears literally in the LLM prompt | The orchestrator's Layer 1 didn't resolve it. Verify the upstream node ran successfully and its `outputVariable` matches your `{{...}}` reference. Check App Service logs for "Failed to render template" warnings. |
| Action JPS has `{{briefingPayload}}` in `instruction.task` and the LLM gets the literal string | Don't put data refs in the prompt body — use `## Input` (which renders automatically when you set `inputBinding` in the playbook node). |
| Custom helper like `{{flatMap COLL 'nested.path'}}` doesn't render | Wave 11 T112+T113 register the standard helpers (json, map, flatten, distinct, concat, join, flatMap). If you're using an unregistered helper, register it in [`TemplateEngine.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs) following the existing pattern. |
| Tried inline lambda or pipe shorthand (`(lambda c …)`, `(… \| flatten)`) | These are NOT valid Handlebars. Rewrite using nested subexpressions: `{{flatMap A 'b.c'}}` for the lambda case, `(join '\n' (flatten (map X 'narrative')))` for the pipe case. See T113 in Wave 11. |
| LLM returns plain text instead of structured JSON | Verify `sprk_outputschemajson` is populated on your Action row. Without it, the LLM has nothing to constrain output. Run your sync script (model on `Sync-BriefNarrateOutputSchemas.ps1`). |
| UpdateRecord didn't write anything | Verify `{{nodeName.field}}` reference matches the upstream node's actual output shape. Use MCP `read_query` to inspect a successful run's intermediate state. |
| Widget shows stale data | Cache hit. Force-refresh the widget; clear browser cache; verify the App Service deploy commit SHA. |

---

## 7. Cheat sheet

| Authoring artifact | What goes where |
|---|---|
| LLM instructions (role, task, constraints, context) | Action JPS `instruction.*` |
| Input shape documentation | Action JPS `input.parameters.description` (for humans + maintainers) |
| Output schema | Action JPS `output.fields[]` + deployed to `sprk_outputschemajson` column |
| Runtime data flowing into the LLM | Playbook node `inputBinding.<key>` (Handlebars templates against upstream node outputs) |
| Order of execution | Playbook node `dependsOn` array |
| Output variable name for downstream `{{X}}` references | Playbook node `outputVariable` |
| Where the output lands | Downstream node (UpdateRecord / ReturnResponse / SendEmail / CreateNotification) |

---

## 8. Reference

- **[`SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](../architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md)** — full architecture reference
- **[`JPS-AUTHORING-GUIDE.md`](JPS-AUTHORING-GUIDE.md)** — Action JPS authoring guide
- **[`PLAYBOOK-AUTHOR-GUIDE.md`](PLAYBOOK-AUTHOR-GUIDE.md)** — playbook authoring guide
- **[`ai-guide-playbook-deploy-recipe.md`](ai-guide-playbook-deploy-recipe.md)** — Deploy-Playbook.ps1 contract
- **[`ai-guide-consumer-wiring.md`](ai-guide-consumer-wiring.md)** — sprk_playbookconsumer wiring
- **[`AI-ARCHITECTURE.md`](../architecture/AI-ARCHITECTURE.md)** — BFF AI subsystem overview
- **Skill**: [`.claude/skills/jps-action-create/SKILL.md`](../../.claude/skills/jps-action-create/SKILL.md) — Claude Code skill for new Action authoring
- **Skill**: [`.claude/skills/jps-playbook-design/SKILL.md`](../../.claude/skills/jps-playbook-design/SKILL.md) — Claude Code skill for new playbook design
