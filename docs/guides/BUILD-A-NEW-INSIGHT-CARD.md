# Build a New Insight Card

> **Purpose**: End-to-end recipe for adding a new topic+mode pair to the Spaarke Insights Engine Widgets surface (the `InsightSummaryCard` that mounts on a host record page and surfaces a topic-scoped, AI-grounded narrative + citations).
>
> **r1 baseline**: This guide documents the exact path proven by Insights Engine Widgets r1 (Matter Health, single mode). Re-read the §5 troubleshooting section before deviating — every entry is a real finding from r1 execution.
>
> **Last reviewed**: 2026-06-11 (r1 task 067 / SC-13). The Matter Health single-mode topic is the **first** insight card in production. New topics added after r1 follow the recipe below.
>
> **Audience**: A Spaarke developer (and the SME helping them) who has never authored an insight card. After reading §1 + §2 you should know what artifacts you will produce; §3 + §4 are the file edits and deploy commands; §6 verifies end-to-end; §7 is the gotcha library.
>
> **Required reading before this guide**:
> - [`../../projects/ai-spaarke-insights-engine-widgets-r1/spec.md`](../../projects/ai-spaarke-insights-engine-widgets-r1/CLAUDE.md) — the r1 spec defining FR-01..FR-27 and the `InsightSummaryCard` contract
> - [`../../.claude/patterns/ai/public-contracts-facade.md`](../../.claude/patterns/ai/public-contracts-facade.md) — `IInsightsAi.AnswerQuestionAsync` is the only BFF entry point you call
> - [`../../docs/standards/CODING-STANDARDS.md`](../standards/CODING-STANDARDS.md) — Spaarke naming + structure conventions
> - **Q-U1 owner ban (binding)**: never use identifier-suffix versioning vernacular in any new identifier you author (playbook names, action codes, topic names, mode strings, registry rows, code symbols, FormXml labels, telemetry tag values). Versioning lives in **value-carrying columns** (`sprk_version`, `sprk_versionumber`) or **string envelope fields** (`schemaVersion: "1.0"`), never in identifiers. Pre-existing r2 rows you only *reference* may keep their existing identifier — the ban applies to identifiers **you** author. The single explicit MUST-NOT-USE banner spelling out the forbidden tokens appears in §3.1 below; outside that banner, the forbidden tokens do not appear anywhere in this guide.

---

## 1. What is an insight card?

An **insight card** is a Fluent v9 React widget (`InsightSummaryCard`, lives in `@spaarke/ai-widgets`) that:

1. Mounts on a host record page (in r1: the **Matter** form, `tab_report card_section_3`).
2. Renders a topic-scoped, mode-specific AI narrative grounded in tenant-accessible evidence.
3. Persists its envelope (`{schemaVersion, body, citations, generatedAt, playbookName, tenantId, dimensions}`) into a **single longtext column** on the host record so the narrative survives a reload and can be consumed downstream (reports, emails, notifications) by extracting `.body` from the JSON.
4. Pre-warms on form load (FR-17/18) and renders the stored envelope immediately if fresh (FR-19); fires fire-and-forget refresh otherwise.

The five artifacts you will produce for each new topic+mode pair:

| # | Artifact | Owner | Where it lives |
|---|---|---|---|
| 1 | **Playbook prompt** (system prompt + JPS schema) | You + SME | `sprk_analysisaction.sprk_systemprompt` row in Dataverse |
| 2 | **Playbook orchestration JSON** | You | `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/<name>.playbook.json` + `sprk_analysisplaybook` + 9× `sprk_playbooknode` rows |
| 3 | **Topic registry row** | You (SME-editable) | `sprk_aitopicregistry` row in Dataverse — routes `(topic, mode, hostentity)` → playbook + display config |
| 4 | **BFF config-map entry** | You | `appsettings.template.json` → `Insights:Playbooks:Map:<canonical-name>` |
| 5 | **Host form integration** (per host record type) | You | FormXml patch + 2 form web resources (pre-warm + mount glue) + 1 HTML host (iframe scope) |

The `InsightSummaryCard` React component itself is **already built** (r1, `@spaarke/ai-widgets/src/components/InsightSummaryCard/`). You do not re-author it per topic — you only configure its props via the registry + host glue.

### 1.1 Wire shape (the contract)

The card calls **`POST /api/insights/ask`** with:

```json
{
  "question": "<canonical-playbook-name>",
  "subject":  "<host-entity-prefix>:<record-guid>",
  "parameters": {}
}
```

- `question` accepts EITHER a `sprk_analysisplaybook` Guid OR a canonical playbook name registered in `Insights:Playbooks:Map`. The endpoint resolves via `InsightsPlaybookNameMapOptions.ResolveOrDefault` (`InsightEndpoints.cs:163`).
- `subject` is the **subject scheme** — for r1 Matter Health, it is `matter:<sprk_matterid>`.
- The endpoint returns `200 OK` with the envelope; the persisted form lands in the host record's target longtext field (per registry `sprk_targetfield`).

### 1.2 What r1 ships (your reference implementation)

| Artifact | Identifier | Notes |
|---|---|---|
| Playbook prompt | `sprk_analysisaction` row `5981632e-4165-f111-ab0c-7ced8ddc4cc6` (`matter-health-synthesis`) | Lives in `sprk_systemprompt`. Not in any `.txt` file. See §3.1. |
| Playbook orchestration | `sprk_analysisplaybook` Guid `a0d49d0d-4a65-f111-ab0c-70a8a590c51c` (`matter-health-single`) | 9 nodes, 9 edges. |
| Topic registry row | `sprk_aitopicregistryid` `c46b940e-4b65-f111-ab0c-70a8a590c51c` (`matter-health/single`) | TTL 60 min. |
| BFF config-map entry | `Insights:Playbooks:Map:matter-health-single = a0d49d0d-…` | Dev appsettings (template); App Service uses snake_case (§3.2). |
| Host form integration | Matter form id `4fa382f2-c273-f011-b4cb-6045bdd6a665` | 2 OnLoad libraries + 1 HTML host (iframe-scope mount target). |

---

## 2. Decision tree — is your work actually a new insight card?

```
START
  │
  │  Q1. Does an existing topic registry row already cover your host entity
  │      and topic at a different mode (e.g. matter-health/single exists,
  │      you want matter-health/historical)?
  │
  ├── YES ──▶ §2.1 — Add ONE new registry row + ONE new playbook + (optional)
  │            ONE new prompt. Reuse the host form integration unchanged.
  │
  └── NO ──▶ Q2. Is your card going on a host entity that has never carried
  │              an insight card before (e.g. sprk_contract, not sprk_matter)?
  │
              ├── YES ──▶ §2.2 — Full path: registry + playbook + prompt +
              │            target-field schema patch (if a longtext doesn't yet
              │            exist on the host) + per-host form integration.
              │            This is the largest scope; budget ~3 weeks.
              │
              └── NO ──▶ §2.3 — You probably want a NEW topic on the same host
                         (e.g. matter-budget on sprk_matter). Same as §2.2
                         minus the host form integration (reuse `tab_report
                         card_section_3` if available, OR add a new section
                         via a FormXml patch).
```

The Calendar of artifacts above is the same in all three paths — what changes is **how many** of them you author. r1 was a §2.2 path (first card on Matter).

---

## 3. Section 1 — Author the playbook

> **Phase 2 recap** (r1 Tasks 020 + 021 + 022 + 023).

### 3.1 Author the prompt — `sprk_analysisaction` row

**CRITICAL — canonical surface**: The system prompt is stored in `sprk_analysisaction.sprk_systemprompt` (a longtext Dataverse column). It is **NOT** in a `/Prompts/` directory and **NOT** in a `.txt` file. This is the **audit-codified pattern** per Audit DR-007 / `canonical-architecture-decisions.md` §2.7 — citation corrected 2026-06-10 (the pattern is NOT governed by ADR-014; ADR-014 covers AI caching/reuse). All r1 playbook prompts live in Dataverse rows; local files in `notes/handoffs/` are versioned design records, not the canonical source.

#### MUST-NOT-USE banner

> 🚫 **Q-U1 owner ban**: Do NOT use `@v1`, `@v2`, `@vN`, or any identifier-suffix versioning vernacular in:
> - `sprk_analysisaction.sprk_actioncode`
> - `sprk_analysisplaybook.sprk_name`
> - `sprk_aitopicregistry.sprk_topicname`, `sprk_mode`, or `sprk_playbookname`
> - any code symbol, FormXml label, or telemetry tag value you author
>
> Use **value-carrying columns** (`sprk_version`, `sprk_versionumber`) or **bare semver strings** in envelope fields (`"schemaVersion": "1.0"`) for versioning. Pre-existing r2 rows you only **reference** (e.g. `INS-FACT`-suffixed rows seeded by r2) may keep their existing identifier — the ban applies to identifiers **you** author in r1+.

#### What goes in `sprk_systemprompt`

A JPS (JSON Prompt Schema) document with these required elements (r1 Matter Health uses 14.7 KB of JSON; budget similar for a new topic):

```json
{
  "instruction": {
    "role": "Spaarke Insights Engine synthesizer for <topic>-<mode> playbook; audience = <SME audience>",
    "task": "Compose {{templateParam1}}, {{templateParam2}}, ... into a <N>-dimension diagnostic narrative + envelope",
    "constraints": [
      "DIMENSION 1: <explicit, per-dimension rule>",
      "...",
      "EVIDENCE GROUNDING: every claim must cite a `citations[]` entry with verbatim excerpt (minLength 12)",
      "ENVELOPE FIELDS: emit exactly the fields listed in output.fields; do not add or omit",
      "JSON-ONLY OUTPUT: no markdown wrapper, no commentary outside the JSON",
      "NARRATIVE STYLE: <SME tone guidance>"
    ],
    "context": "Playbook orchestration semantics — upstream <GateNodeName> + downstream <GroundingNodeName> + persistence target `<sprk_entity>.<sprk_targetfield>`"
  },
  "output": {
    "structuredOutput": true,
    "fields": {
      "schemaVersion": { "type": "string", "enum": ["1.0"] },
      "body":          { "type": "string", "minLength": 100 },
      "citations":     { "type": "array",  "items": { "type": "object", "properties": { "type": { "enum": ["assessment","document"] }, "excerpt": { "minLength": 12 } } } },
      "generatedAt":   { "type": "string", "format": "date-time" },
      "playbookName":  { "type": "string", "enum": ["<canonical-playbook-name>"] },
      "tenantId":      { "type": "string", "format": "uuid" },
      "dimensions":    { "type": "array", "items": { "enum": ["<dim1>", "<dim2>", "..."] } }
    }
  },
  "examples": [
    { "input": { /* ... */ }, "output": { /* full happy-path */ } },
    { "input": { /* ... */ }, "output": { /* Decline path — "Insufficient evidence to diagnose" */ } }
  ],
  "scopes": {}
}
```

**Why per-dimension constraint pinning** (r1 D3 design decision): single-constraint approaches drift; per-dimension pinning + a dedicated KPI / domain-vocabulary constraint + a grounding constraint is more reliable than rolling everything into one mega-constraint. r1 uses 12 constraints (7 dimension + 5 cross-cutting). The LLM treats each as a separate requirement.

**Why two examples**: few-shot for both the happy path AND the Decline path improves Decline-path reliability (LLMs over-narrate when given only happy-path examples).

#### Deploy the prompt row

Create the row via `mcp__dataverse__create_record` or any seed script. Required columns:

| Column | r1 example value | Notes |
|---|---|---|
| `sprk_name` | `Matter Health Synthesis (Single Mode)` | Human-readable label |
| `sprk_actioncode` | `matter-health-synthesis` | **Bare** — no `@v` suffix |
| `sprk_actiontypeid` | `60 - Agent Service` lookup | Match the executor type for synthesis nodes |
| `sprk_executoractiontype` | `60` | Integer mirror of FK (per r1 D8: r1 sets this explicitly, r2 left it null) |
| `sprk_outputformat` | `0` (JSON) | Structured output |
| `sprk_temperature` | `0.2` | Synthesis benefits from low temperature |
| `sprk_allowsknowledge` | `true` | If the playbook uses knowledge scopes (r1 leaves empty for matter-health) |
| `sprk_systemprompt` | the full JPS JSON above | Canonical surface |

Verify: round-trip read confirms `sprk_systemprompt` parses as valid JSON; grep the body for `@v` (must be 0).

### 3.2 Author the playbook orchestration

The playbook JSON lives in `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/<canonical-name>.playbook.json` and gets deployed to Dataverse as 1× `sprk_analysisplaybook` row + N× `sprk_playbooknode` rows by `scripts/Deploy-Playbook.ps1`.

#### Anatomy of `<name>.playbook.json`

```json
{
  "name": "matter-health-single",
  "displayName": "Matter Health Insight (Single Mode)",
  "playbookType": 0,
  "cacheTtlSeconds": 3600,
  "nodes": [
    {
      "name": "queryMatterContext",
      "nodeType": "Action",
      "actionCode": "INS-FACT",
      "actionType": 51,
      "configJson": { /* ... */ },
      "outputVariable": "matterContext"
    },
    {
      "name": "queryKpiAssessments",
      "nodeType": "Action",
      "actionCode": "INS-FETCH-KPI",
      "actionType": 51,
      "configJson": { /* sprk_kpiassessment query */ },
      "outputVariable": "assessments"
    },
    {
      "name": "retrieveObservations",
      "nodeType": "Action",
      "actionCode": "INS-IDXR",
      "actionType": 70,
      "outputVariable": "observations"
    },
    {
      "name": "checkSufficiency",
      "nodeType": "Action",
      "actionCode": "INS-EVID",
      "actionType": 70,
      "outputVariable": "sufficiencyDecision",
      "dependsOn": ["queryKpiAssessments", "retrieveObservations"]
    },
    {
      "name": "synthesize",
      "nodeType": "AIAnalysis",
      "actionCode": "matter-health-synthesis",
      "actionType": 60,
      "outputVariable": "insightArtifact",
      "configJson": {
        "modelDeployment": "gpt-4o",
        "templateParameters": {
          "matterId":      "{{Subject}}",
          "currentGrade":  "{{Parameters.currentGrade}}",
          "matterContext": "{{matterContext}}",
          "assessments":   "{{assessments}}",
          "observations":  "{{observations}}"
        }
      },
      "dependsOn": ["checkSufficiency"]
    },
    {
      "name": "groundCitations",
      "nodeType": "Action",
      "actionCode": "INS-GRND",
      "actionType": 70,
      "outputVariable": "groundedArtifact",
      "dependsOn": ["synthesize"]
    },
    {
      "name": "ReturnInsightArtifactNode",
      "nodeType": "Action",
      "actionCode": "INS-RART",
      "actionType": 70,
      "configJson": {
        "valueFrom": "body",
        "evidenceFrom": "citations",
        "producedByVersion": "1.0"
      },
      "dependsOn": ["groundCitations"]
    },
    {
      "name": "declineInsufficient",
      "nodeType": "Action",
      "actionCode": "INS-DECL",
      "actionType": 70,
      "dependsOn": ["checkSufficiency"]
    },
    {
      "name": "persistEnvelope",
      "nodeType": "Output",
      "actionCode": "INS-UPDR",
      "actionType": 22,
      "configJson": {
        "entityLogicalName": "sprk_matter",
        "recordId": "{{matterId}}",
        "fieldMappings": [
          {
            "field": "sprk_performancesummary",
            "type": "string",
            "value": "{\"schemaVersion\":\"1.0\",\"body\":{{{groundCitations.output.body}}},\"citations\":{{{groundCitations.output.citations}}},\"generatedAt\":\"{{run.startedAtIso}}\",\"playbookName\":\"matter-health-single\",\"tenantId\":\"{{tenantId}}\",\"dimensions\":{{{groundCitations.output.dimensions}}}}"
          }
        ]
      },
      "dependsOn": ["ReturnInsightArtifactNode"]
    }
  ]
}
```

#### Executor types you will reference

| Executor type | What it does | r1 actionCode examples |
|---|---|---|
| `22` — UpdateRecord | Writes a value to a Dataverse column | `INS-UPDR` (r1 net-new) |
| `51` — QueryDataverse | OData query against a Dataverse entity | `INS-FACT`, `INS-FETCH-KPI` (r1 net-new) |
| `60` — Agent Service | Pure-prompt LLM synthesis (no document binding) | `matter-health-synthesis` (r1 net-new) |
| `70` — generic action | r2 ingest / evidence / grounding / decline / return nodes | `INS-IDXR`, `INS-EVID`, `INS-GRND`, `INS-DECL`, `INS-RART` |

If your playbook uses a new executor type, you must also seed a `sprk_analysisactiontype` row (r1 seeded types 22 and 51 because r2 hadn't). The Web API navigation property for `sprk_ActionTypeId@odata.bind` is **Pascal-case** (the binding endpoint requires the nav name, not the snake-case lookup attribute name).

#### Envelope shape (FR-14) — the 7 fields actually emitted

r1's `persistEnvelope` template emits **7 fields**: `schemaVersion`, `body`, `citations`, `generatedAt`, `playbookName`, `tenantId`, `dimensions`.

The spec FR-14 originally listed 8 (including `playbookVersion`). r1's Task 022 omitted `playbookVersion` and Task 025 reconciled the spec to **intentionally omit** it from the in-envelope payload — the authoritative version source is `sprk_analysisplaybook.sprk_version` (Dataverse-side), resolvable via `playbookName`. Including `playbookVersion` in-envelope would create a double source of truth. See `projects/ai-spaarke-insights-engine-widgets-r1/notes/insight-envelope-schema.md` §6 for full rationale.

When you author a new playbook, use the same 7-field shape unless you have a documented spec exception.

#### Deploy the playbook — `scripts/Deploy-Playbook.ps1`

```pwsh
# Dry run — verifies action-code resolution + node DAG + alt-key check
.\scripts\Deploy-Playbook.ps1 -PlaybookJsonPath src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/<name>.playbook.json -DryRun

# Real deploy
.\scripts\Deploy-Playbook.ps1 -PlaybookJsonPath src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/<name>.playbook.json
```

The script:
1. Authenticates to dev Dataverse (or your target env).
2. Pre-flight resolves every `actionCode` against `sprk_analysisaction.sprk_actioncode` (exact-match lookup).
3. Lints the node DAG (cycles, missing dependsOn, unbound `outputVariable`).
4. Creates the `sprk_analysisplaybook` row (or PATCHes if name exists).
5. Creates N× `sprk_playbooknode` rows with `sprk_isactive=true`, FK-bound to actions.
6. Saves the canvas layout for the Designer UI.

Verify: `sprk_analysisplaybook` queryable by name; `_sprk_actionid_value ne null` returns the full node count.

#### BFF config-map entry (`appsettings.template.json`)

Add the playbook name → Guid mapping. r1 added:

```json
"Insights": {
  "Playbooks": {
    "Map": {
      "matter-health-single": "a0d49d0d-4a65-f111-ab0c-70a8a590c51c"
    }
  }
}
```

**App Service caveat** (Linux POSIX env-var rules): when you set this via Azure App Service Application Settings, the key MUST be snake_case (no `-`, no `@`). Use `Insights:Playbooks:Map:matter_health_single = <guid>`. The dev environment file binding tolerates hyphens (used here); the App Service path does not.

#### Concurrency dedup — already done for you (FR-22)

r1 Task 053 added a per-key `SemaphoreSlim` registry inside `InsightsPlaybookExecutionCache` so that two simultaneous POSTs for the same `(playbookId, subject, parameters, accessibleScopeHash)` produce **exactly one** engine invocation; the second observer reads the cached artifact after the first writes through. Fast-path (cache HIT) is unchanged — no throughput hit. Single-instance scope; if telemetry shows duplicate engine invocations across BFF instances for the same key, layer a Redis `SETNX` lock — but do not re-author the interface.

---

## 4. Section 2 — Register the topic

> **Phase 1 recap** (r1 Tasks 010 + 011 + 012 + 013 + 014).

The `sprk_aitopicregistry` entity is the **routing table** that maps `(topic, mode, hostentity)` → playbook + display configuration. It is the only place where SMEs (without dev tooling) can:
- Add a new topic+mode pair to a host
- Toggle a card on/off in production (`sprk_enabled`)
- Tune the per-topic cache TTL (`sprk_cachettlminutes`)
- Change the card header / icon
- Re-point a topic at a different playbook

### 4.1 The 9 business fields

| # | Logical name | Type | Notes |
|---|---|---|---|
| 1 | `sprk_topicname` | NVARCHAR(100) NOT NULL | Stable topic id, kebab-case (`matter-health`) |
| 2 | `sprk_mode` | NVARCHAR(50) NOT NULL | r1: `single` only |
| 3 | `sprk_playbookname` | NVARCHAR(200) NOT NULL | FK by name to `sprk_analysisplaybook.sprk_name`; Q-U1 ban enforced |
| 4 | `sprk_displayname` | NVARCHAR(200) NOT NULL | Card header text |
| 5 | `sprk_icon` | NVARCHAR(100) | **Fluent v9 component name string** (e.g. `Sparkle24Filled`) — bare component name, not a path, not a URL |
| 6 | `sprk_hostentity` | NVARCHAR(100) NOT NULL | Host record type (`sprk_matter`) |
| 7 | `sprk_targetfield` | NVARCHAR(100) NOT NULL | Longtext column on `sprk_hostentity` that stores the envelope JSON |
| 8 | `sprk_cachettlminutes` | INT NOT NULL | Range 1..1440; r1 matter-health default 60 |
| 9 | `sprk_enabled` | BIT | Default true; SME on/off toggle |

Alternate key: `(sprk_topicname, sprk_mode)` enforced via platform-native composite index (no plugin, no JS). Duplicate insert returns **HTTP 412 Precondition Failed** (not 409, not 400).

### 4.2 Use the Power Apps forms (no JS required)

r1 Task 012 deployed two systemforms on `sprk_aitopicregistry`:

| Form | type | Use for |
|---|---|---|
| **AI Topic Registration** (Main, `1823746f-…`) | 2 | Full edit; 3 sections — Identity / Routing / Runtime — wired to all 9 business fields + `sprk_name` |
| **Add Topic** (Quick Create, `5523746f-…`) | 7 | One-column SME-friendly add; 7 fields (omits `sprk_cachettlminutes` and `sprk_icon`) |

**SME workflow**:

1. Open the Spaarke model-driven app → AI Topic Registry table.
2. Click **+ New** to open the Quick Create form.
3. Fill in:
   - `sprk_name` = `{topicname}/{mode}` convention (e.g. `matter-health/single`) — type this directly per design §5.4 (r1 deliberately ships zero OnSave JS; the convention is SME-typed).
   - `sprk_topicname` = bare kebab-case (e.g. `matter-health`)
   - `sprk_mode` = `single`
   - `sprk_playbookname` = bare playbook name matching `sprk_analysisplaybook.sprk_name` (e.g. `matter-health-single`)
   - `sprk_hostentity` = host record type logical name (e.g. `sprk_matter`)
   - `sprk_targetfield` = the longtext column that will store the envelope (e.g. `sprk_performancesummary`)
   - `sprk_displayname` = card header text (e.g. `Matter Health Insight`)
4. **Save**. Then open the new row on the Main form to set:
   - `sprk_cachettlminutes` (Quick Create omits this; Dataverse `ApplicationRequired` is form-level only — an inserted row without this field is `null` and the next save on the Main form will require a value).
   - `sprk_icon` (optional) — Fluent v9 component name like `Sparkle24Filled`.
   - `sprk_enabled` defaults to `true`; toggle off to hide the card without deleting the row.

### 4.3 Field meanings — what each one actually does at runtime

| Field | Read by | Effect |
|---|---|---|
| `sprk_topicname` + `sprk_mode` | Card mount glue (FR-05) | Resolves `(topic, mode)` → playbook name; queried at mount time via OData `$filter=sprk_hostentity eq '...' and sprk_topicname eq '...' and sprk_mode eq '...' and sprk_enabled eq true` |
| `sprk_playbookname` | Mount glue + BFF | Mount glue passes this verbatim as the `question` field on the `/api/insights/ask` POST; BFF resolves via `InsightsPlaybookNameMapOptions.ResolveOrDefault` |
| `sprk_hostentity` + `sprk_targetfield` | OnLoad pre-warm (FR-17) | Read the envelope from `{sprk_hostentity}.{sprk_targetfield}` via `Xrm.WebApi`; check `.generatedAt` for staleness |
| `sprk_displayname` | Card header (FR-06) | Rendered as the card title |
| `sprk_icon` | Card chrome | Fluent v9 component name string; the card resolves the component dynamically via `ConfigurationService.ts` pattern. Q-U2 evidence: see `src/client/shared/Spaarke.UI.Components/src/services/ConfigurationService.ts`. |
| `sprk_cachettlminutes` | BFF cache wrapper (FR-21) | r1 Task 052 plumbs this into `TopicRegistryTtlLookup`, which overrides the playbook's default `cacheTtlSeconds`. Default 60 min for matter-health (r2 default for `IInsightsPlaybookExecutionCache` is 15 min). |
| `sprk_enabled` | Card mount glue + SME kill | If `false`, the mount glue's OData filter excludes the row → card is not mounted. Lets SMEs disable a card without code or deploy. |

### 4.4 Deploy the registry row

Use the scripts that r1 proved:

- **Schema deploy** (one-time per environment): `scripts/temp/Deploy-AiTopicRegistryEntity.ps1` — creates the entity + 9 attributes + alt key + publish. The **2-run idempotent pattern** is normal: first run creates the entity, the immediate attribute-add may fail with `0x80060888 "An unexpected error occurred"` (metadata-propagation race). Re-run the script; the second run sees the entity present and adds the 9 attributes successfully. See §7 Troubleshooting.
- **Forms deploy** (one-time per environment): `scripts/temp/Deploy-AiTopicRegistryForms.ps1` — Main + Quick Create form upserts.
- **Seed row**: the SME workflow above OR a POST via `mcp__dataverse__create_record` (`tables/sprk_aitopicregistries`) with the 9 + 1 fields.

Verify: MCP describe shows all 10 sprk_ columns; OData filter on `(topicname, mode, enabled=true)` returns exactly 1 row.

---

## 5. Section 3 — Wire to host form

> **Phase 4 recap** (r1 Tasks 040 + 041 + 042 + 043).

This section is the largest scope per host entity. r1 wired the Matter form (`sprk_matter`); a new host entity (e.g. `sprk_contract`) repeats the same five steps with its own form id.

### 5.1 The five host-side artifacts

| # | Artifact | r1 example | Purpose |
|---|---|---|---|
| 1 | OnLoad pre-warm JS web resource | `sprk_matter_insight_onload.js` | FR-17 read envelope + FR-18 fire-and-forget POST if stale/absent |
| 2 | OnLoad mount glue JS web resource | `sprk_matter_insight_card_mount.js` | FR-19 host the React card; resolve `(topic, mode, subject)` → card props |
| 3 | HTML host web resource (iframe scope) | `sprk_matter_insight_card_host.html` | The DOM mount target (id `spaarke-matter-insight-card-host`) the React card binds to |
| 4 | FormXml patch — two new libraries + two new OnLoad handlers | Matter form `4fa382f2-…` | Wires #1 and #2 onto the form |
| 5 | FormXml patch — WebResource control in target section | Matter `tab_report card_section_3` | Mounts #3 onto the card section so the React card has somewhere to render |

### 5.2 Pre-warm OnLoad — wire shape (FR-17 / FR-18)

```js
// File: src/dataverse/forms/<sprk_host>/<host>InsightWidgetOnLoad.js
var Spaarke = Spaarke || {};
Spaarke.MatterInsight = Spaarke.MatterInsight || (function () {
  var ns = {};
  ns._version = "0.2.0";
  ns._prewarmEndpoint = "/api/insights/ask";
  ns._playbookName = "matter-health-single";   // BARE — no `@v` suffix
  ns._targetField  = "sprk_performancesummary"; // from registry sprk_targetfield
  ns._staleThresholdMinutes = 60;

  ns.onLoad = function (executionContext) {
    try {
      var formContext = executionContext.getFormContext();
      var matterId = formContext.data.entity.getId().replace(/[{}]/g, "");
      // FR-17: read envelope; tolerate non-JSON via try/catch
      Xrm.WebApi.retrieveRecord("sprk_matter", matterId, "?$select=" + ns._targetField)
        .then(function (record) {
          var decision = ns._evaluateStaleness(record[ns._targetField]);
          if (decision.status === "fresh") {
            console.log("[Matter Insight] v" + ns._version + " envelope read: fresh (ageMinutes=" + decision.ageMinutes + ")");
            return; // skip prewarm
          }
          ns._firePrewarm(matterId, decision);
        })
        .catch(function (e) { console.warn("[Matter Insight] read failed", e); });
    } catch (e) { console.warn("[Matter Insight] onLoad caught", e); }
  };

  ns._firePrewarm = function (matterId, decision) {
    var subject_1 = "matter:" + matterId;
    // FR-18: fire-and-forget; DO NOT await; .then/.catch logs only
    fetch(ns._prewarmEndpoint, {
      method: "POST",
      credentials: "include",
      keepalive: true,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        question: ns._playbookName,
        subject: subject_1,
        parameters: {}
      })
    })
      .then(function (r) { console.log("[Matter Insight] prewarm dispatched. status=" + decision.status + ", httpStatus=" + r.status); })
      .catch(function (e) { console.warn("[Matter Insight] prewarm catch", e); });
  };

  ns._evaluateStaleness = function (rawText) {
    if (!rawText) { return { status: "absent" }; }
    try {
      var env = JSON.parse(rawText);
      if (!env.generatedAt) { return { status: "absent" }; }
      var ageMin = (Date.now() - new Date(env.generatedAt).getTime()) / 60000;
      return ageMin > ns._staleThresholdMinutes
        ? { status: "stale", ageMinutes: ageMin }
        : { status: "fresh", ageMinutes: ageMin };
    } catch (e) {
      return { status: "absent" }; // FR-17 graceful: non-JSON legacy R5 text
    }
  };

  return ns;
})();
```

**Wire-shape decision (r1 Task 042 Option b — binding)**: the `question` field is the **canonical playbook name** (bare, no `@v`). The BFF resolves via the name-map. Do **not** send `{topic, mode, subject, parameters}` — that shape returns 400 (r1 Task 041 P1 finding; resolution documented inline in `notes/handoffs/phase-4-e2e-test.md` §C). The decision rationale:
1. No BFF change needed; the endpoint already accepts canonical names via `InsightsPlaybookNameMapOptions.ResolveOrDefault`.
2. The registry already stores the playbook name; the mount glue derives `(topic, mode)` → `playbookName` from the registry contract.
3. Forward-compatible: r2+ adds new topics by adding registry rows + name-map entries; mount glue + BFF endpoint are stable.

**NFR-03 (Form TTI)**: the handler MUST return synchronously. `Xrm.WebApi.retrieveRecord` returns a Promise; you handle the result in `.then`; you do NOT `await` and you do NOT block. The fire-and-forget POST has `keepalive: true` so it survives form navigation.

### 5.3 Mount-glue OnLoad — wire shape (FR-19)

The mount glue runs as a second OnLoad handler. It:
1. Resolves the host DOM element by id (`spaarke-matter-insight-card-host`).
2. Defers via a single `requestAnimationFrame` so the React mount is detached from the synchronous OnLoad return path (NFR-03).
3. Reads the stored envelope from the host record; if present, hands it to the React card as `initialEnvelope` so the card renders **immediately** (FR-19) without spinner.
4. Initialises the card with props: `{subject: "matter:<guid>", topic, mode, theme, playbookName, onFetchInsight, onCitationClick}`.

The full mount-glue source is `src/dataverse/forms/sprk_matter/insightCardMount.js`. The card's `state.ts` exposes the `INIT_FROM_STORED_ENVELOPE` action which the mount glue dispatches with the parsed envelope.

**Q-U3 — no feedback UI** in the card props. The card contract intentionally **does not** carry any feedback affordance (no feedback callback prop, no thumbs-up/down button component) in r1. Feedback (thumbs up/down) is **deferred to r2+** pending the Cosmos feedback container (parallel project AIPU2 / ADR-015). Do not add a feedback callback prop, do not import the thumbs-up/down button component from the shared library. (r1 Sandbox states and the audited a11y states are all feedback-free.)

### 5.4 The `@spaarke/ai-widgets` IIFE bundle — currently a P1 gap

**Read this before you wire any host form.**

r1 Task 043 closed Phase 4 with a **documented P1 gap**: there is **no IIFE bundle** of `@spaarke/ai-widgets` for MDA-form WebResource consumption. The package builds with plain `tsc` (CommonJS ESM modules) only. The Dataverse Form WebResource iframe scope does NOT have host-provided React; the bundle must self-contain React 19 + ReactDOM + the card + its transitive deps (`@spaarke/ui-components`, `@spaarke/ai-outputs`).

**Until the IIFE bundle ships**, the card is **NOT visibly rendered** on the host form:
- The pre-warm OnLoad (#1) fires; the POST happens; the envelope persists.
- The mount glue OnLoad (#2) loads; `_resolveHost()` emits the warning `[Matter Insight Card] host element 'spaarke-matter-insight-card-host' not found on form. FormXml patch may not be deployed (Task 043).` This is the **expected Phase 4 staging signal** — it proves the script loaded and resolved the subject.
- The HTML host (#3) is deployed but the FormXml WebResource control (#5) is **not yet** wired to `tab_report card_section_3` because the Web API `PATCH systemforms` rejects `$webresource:` deps with `PrimaryNameLookup` failure even after `PublishAllXml + 10s sleep`. The canonical resolution is `pac solution import` of a packed ZIP (per `dataverse-deploy` skill Scenario 1d).

**Recovery path** (when funded — r2 or P1 retrofit):

1. Add `vite` (or `esbuild`) to `Spaarke.AI.Widgets/package.json` devDeps.
2. Add `build:bundle` script producing `dist/spaarke-ai-widgets.iife.js` (~1-2 MB gzipped).
3. Deploy the bundle as a flat-named web resource (e.g. `sprk_spaarke_ai_widgets_bundle.js`).
4. Update the HTML host web resource to add `<script src="sprk_spaarke_ai_widgets_bundle.js">`.
5. Use the `pac solution export → unpack → edit FormXml → repack → pac solution import` route to add the WebResource control to the target section.

**Until step 5 lands**, demos on the host form rely on the **console-observable signals + Network-tab POST + Dataverse field write + telemetry events** described in §6 and `notes/handoffs/phase-4-e2e-test.md`.

### 5.5 Deploy the host-form integration

Use `scripts/temp/Deploy-MatterInsightCard.ps1` (or fork it for your new host). The script is idempotent:
- Web resources: PATCH if `name` matches; CREATE otherwise.
- Solution component add tolerates "already exists".
- FormXml `<formLibraries>` and `<event name="onload">` are patched only if entries are missing.
- `PublishAllXml` runs before form patch; `PublishXml(entity=<sprk_host>)` runs after.

**Web resource naming gotchas** (from r1 Task 043 — burned in):
- Web resource names CANNOT contain forward slashes. Old r1 attempts used `sprk_/scripts/matter_insight_onload.js` (mirroring `sprk_/scripts/` convention from old design docs) and `PrimaryNameLookup` failed every time. Use flat names: `sprk_matter_insight_onload.js`.
- FormXml `cell` attributes are case-sensitive **lowercase**: `showlabel`, `rowspan`, `colspan`. Power Apps Maker exports may use camelCase; Dataverse Web API `PATCH systemforms` rejects camelCase.
- `tab_matter_health` / `section_matter_health_card` do NOT exist on the live r1 Matter form. The canonical surface is `tab_report card` and `tab_report card_section_3` — **with a literal space** in the tab name. Author assumptions must be verified against the live form via Web API `GET systemforms(<formId>)?$select=formxml` before any FormXml patch.

---

## 6. Section 4 — Verify end-to-end

> **Phase 6 recap** (r1 Tasks 060 + 061 + 062 + 063 + 064 + 065 + 066).

After deploy, run three UAT scenarios + a KQL telemetry sweep. Each scenario is **self-contained** so they may be run in parallel by different operators.

### 6.1 Scenario A — Real host record (SC-05 + SC-06)

**Persona**: SME or analyst with read access to the host entity; comfortable with DevTools.

**Prereqs**: dev BFF healthy (`/healthz` → 200), kill-switches OFF (`DocumentIntelligence:Enabled=true` AND `Insights:Enabled=true`), a host record with the data the playbook expects (r1 matter-health: ≥3 KPI assessments per Performance Area; for new topics, the playbook's `checkSufficiency` node defines the threshold).

**Steps**:

1. Navigate to the host record form in the Spaarke MDA. DevTools → Console + Network tabs open.
2. Observe console signals (verifies FR-17 + FR-18 wiring):
   - `[Matter Insight] v<ver> onLoad start (matter id=...)`
   - `[Matter Insight] v<ver> envelope read: <absent|stale|fresh>`
   - `[Matter Insight] v<ver> prewarm dispatched. status=<absent|stale>, httpStatus=<200|202>, subject=matter:<guid>` — **only** if status is absent/stale; fresh skips the POST per FR-18.
   - `[Matter Insight Card] v<ver> onLoad start` + `host element 'spaarke-matter-insight-card-host' not found on form.` — until the IIFE bundle ships (§5.4), this is the expected staging signal.
3. Network tab → filter `/api/insights/ask`. Verify request body matches §1.1 wire shape: `{question: "<canonical-playbook-name>", subject: "<host>:<guid>", parameters: {}}`. Response status MUST be 200 or 202, NOT 400.
4. Wait for the playbook to drain (3-8s for cold call). Query the host record's target field via Web API: `GET /api/data/v9.2/sprk_matters(<guid>)?$select=sprk_performancesummary` — verify the value is a JSON envelope with 7 fields (§3.2). R5-era placeholder text is overwritten.
5. **Second click within TTL window** (SC-06): re-click or re-load. Network tab shows the second POST returns within **<100ms** with `X-Insights-Cache: true` header. If timing exceeds 100ms, file P0 — cache wiring regressed.

### 6.2 Scenario B — Low-data record (SC-07 / FR-24)

**Persona**: SME opening a brand-new host record before data has landed.

**Steps**:

1. Open a low-data host record (r1 matter-health: <2 KPI assessments OR all Notes empty).
2. Network tab POST returns **200 OK** with envelope where `dimensions: ["evidence-gap"]`, `body` begins with "Insufficient evidence to diagnose" (or your playbook's Decline language), `citations: []`.
3. No 4xx, no 5xx. No exception. No `sprk_performancesummary` update on Decline (verify by re-querying the field after the call and confirming it is unchanged from the prior value).

### 6.3 Scenario C — Kill-switch ON (SC-08 / FR-25)

**Persona**: Platform Operations Engineer with Azure App Service Contributor RBAC.

**Steps**:

1. Azure Portal → BFF App Service → Configuration → Application settings → set `DocumentIntelligence__Enabled=false` (and/or `Insights__Enabled=false`). App Service restarts (~30s).
2. `GET /healthz` returns 200 (kill-switch does NOT take down the health endpoint).
3. Open any host record; pre-warm POST returns **HTTP 503** with `Content-Type: application/problem+json`. Body matches ADR-019 ProblemDetails shape: `{"type": "https://spaarke.dev/errors/feature-disabled", "title": "Feature disabled", "status": 503, "detail": "...FeatureDisabledException...", "instance": "/api/insights/ask"}`. NOT 500 (the canonical r1 audit DR-003 / PR #351 LATENT BUG fix).
4. **Cleanup**: restore the kill-switch values; await restart; re-verify Scenario A Step 1.

### 6.4 KQL telemetry sweep (SC-11)

After the three scenarios above, the BFF should have emitted `widget.insightcard.invoked` events on meter `Sprk.Bff.Api.InsightWidgets`. Run these in App Insights:

```kql
// 1. Invocation volume by topic
customMetrics
| where name == "widget.insightcard.invoked"
| where timestamp >= ago(2h)
| summarize count() by tostring(customDimensions["topic"])
```

```kql
// 2. Cache-hit rate (corroborates SC-06)
customMetrics
| where name == "widget.insightcard.invoked"
| where timestamp >= ago(2h)
| summarize sum(value) by tostring(customDimensions["cacheHit"])
```

```kql
// 3. p95 latency by topic + mode
customMetrics
| where name == "widget.insightcard.duration"
| where timestamp >= ago(2h)
| summarize percentile(value, 95) by tostring(customDimensions["topic"]), tostring(customDimensions["mode"])
```

```kql
// 4. Kill-switch frequency (corroborates SC-08)
customMetrics
| where name == "widget.insightcard.invoked"
| where timestamp >= ago(2h)
| where customDimensions["outcome"] == "kill_switched"
| summarize count() by bin(timestamp, 1h)
```

```kql
// 5. Failure rate (corroborates NFR-05)
customMetrics
| where name == "widget.insightcard.invoked"
| where timestamp >= ago(2h)
| summarize
    total = sum(value),
    failures = sumif(value, customDimensions["outcome"] == "failed"),
    killSwitched = sumif(value, customDimensions["outcome"] == "kill_switched")
    by bin(timestamp, 1h)
| extend failureRate = (failures + killSwitched) * 1.0 / total
```

Expected: no nulls in `topic`, `mode`, `outcome`, `cacheHit` dims; one row each of `cacheHit=true` and `cacheHit=false` after Scenario A; one row of `outcome=kill_switched` after Scenario C; `failureRate=0` during well-formed UAT runs (kill-switch is expected behaviour, not a failure).

The four outcome paths the BFF emits (`InsightEndpoints.cs` lines 304/332/362):

| Exit path | `outcome` | `cacheHit` |
|---|---|---|
| `FeatureDisabledException` (ADR-018 kill-switch) | `kill_switched` | `false` |
| Generic `Exception` | `failed` | `false` |
| Cache hit (post-call) | `cache_hit` | `true` |
| Success first-call | `success` | `false` |

`subject` lives on the **Activity span tag only** (high-cardinality discipline per ADR-014/015) — NOT on the metric counter. Do not query `subject` from `customMetrics`; query it from `dependencies` or `customEvents` (the Activity span name is `InsightSummaryCard.Invoke`).

---

## 7. Section 5 — Troubleshooting + FAQ

The entries below are real r1 findings. Each one cost a deploy iteration or a debugging session; do not re-discover them.

### 7.1 Q: "Why is the card not visibly rendering on the form, even though my pre-warm POST returns 200?"

**A**: You are observing the r1 Phase 4 P1 gap — there is no `@spaarke/ai-widgets` IIFE bundle and the WebResource control on the target section is not yet wired. The mount glue script loads (you'll see `[Matter Insight Card] onLoad start` in console) and the pre-warm fires correctly, but the React card has no host DOM element to mount onto. Recovery path: §5.4 above.

### 7.2 Q: "My POST returns 400 'question must be either a valid playbook Guid id OR a canonical name registered in Insights:Playbooks:Map'."

**A**: Two common causes:
1. **Wrong wire shape** — you are sending `{topic, mode, subject, parameters}`. Switch to `{question: "<canonical-playbook-name>", subject, parameters}` per §1.1. r1 Task 041 P1 finding; resolved before Task 043 deploy.
2. **Branch not deployed** — the appsettings map entry exists in your branch but the BFF hasn't been redeployed. Two paths: (a) POST the **raw Guid** as `question` to bypass the name map (works against the currently-deployed BFF without a deploy); (b) deploy the BFF first via `bff-deploy` skill, then use the canonical name.

### 7.3 Q: "My playbook deploy fails with 'actionCode <X> does not resolve'."

**A**: `Deploy-Playbook.ps1` does an **exact-match lookup** on `sprk_analysisaction.sprk_actioncode`. The r1 Wave 1B path found that the r2 `INS-*` rows in dev exist **only with the version-suffix vernacular form** as their `sprk_actioncode` — there are no bare-code variants. Two options:

- **Option (a) — Create bare-code alias rows** for every `INS-*` you reference. Costs 6 redundant `sprk_analysisaction` rows for the r1 use case.
- **Option (b, chosen for r1)** — Reference the existing rows by their actual identifier (the suffixed form r2 deployed) in the playbook JSON. The Q-U1 ban applies to **new** identifiers **you** author; pre-existing r2 rows that you only reference may keep their existing identifier. The playbook's `sprk_name` (e.g. `matter-health-single`) remains bare — that is what Q-U1 most cares about. Document the decision in your handoff so future audits can trace the reference.

The new identifiers r1 net-authored (`matter-health-synthesis`, `INS-FETCH-KPI`, `INS-UPDR`) are all bare — Q-U1 compliant.

### 7.4 Q: "The first attribute-add on a brand-new entity returns `0x80060888 'An unexpected error occurred'`."

**A**: Metadata-propagation race — the entity was created milliseconds before the attribute-add and Dataverse hasn't fully propagated it yet. Re-run the idempotent deploy script; the second run sees the entity present and adds all attributes successfully. This is **not** a script bug; the 2-run pattern is the expected workflow (r1 Task 011 finding). The same applies to alt-key creation immediately after entity creation: `EntityKeyIndexStatus: Pending` flips to `Active` in ~10s; don't test duplicate-detection in the same request thread as alt-key creation.

### 7.5 Q: "FormXml `PATCH systemforms` rejects my WebResource control with `PrimaryNameLookup` failure."

**A**: The Web API form-patch path does strict immediate `PrimaryNameLookup` on `$webresource:` deps and rejects unresolvable deps even when the web resource exists, is published, and `PublishAllXml` was run with a 10s sleep. The canonical resolution is `pac solution import` of a packed ZIP (per `dataverse-deploy` skill Scenario 1d). The solution-ZIP path resolves `$webresource:` deps during import.

### 7.6 Q: "Why does my web resource fail to attach to the form even though it deployed successfully?"

**A**: Web resource names CANNOT contain forward slashes. r1 initially deployed `sprk_/scripts/matter_insight_onload.js` (mirroring `sprk_/scripts/` from old design docs) and `PrimaryNameLookup` failed every time. Use **flat** names: `sprk_matter_insight_onload.js`. Old slash-named resources should be deleted to avoid orphans.

### 7.7 Q: "Where does feedback (thumbs up/down) plug in?"

**A**: **r1 deliberately ships no feedback affordance** (Q-U3). Feedback is deferred to r2+ pending the Cosmos feedback container (parallel project AIPU2 / ADR-015). The card's props contract does **not** declare a feedback callback; the thumbs-up/down button component is not imported in any r1 component. When AIPU2 lands on master, r2 will re-introduce the feedback affordance per the Cosmos contract. The SC-12 success criterion was renumbered-but-preserved (deferred) for this reason.

### 7.8 Q: "My envelope is missing `playbookVersion` — is that a spec violation?"

**A**: No. r1 Task 025 reconciled spec FR-14 to **intentionally omit** `playbookVersion` from the in-envelope payload. The authoritative version source is `sprk_analysisplaybook.sprk_version` (Dataverse-side), resolvable via `playbookName`. Including it in-envelope would create a double source of truth. Your new playbook's `persistEnvelope` template should emit 7 fields per §3.2.

### 7.9 Q: "Why does the prompt live in Dataverse instead of a `/Prompts/<name>.txt` file?"

**A**: Per Audit DR-007 / `canonical-architecture-decisions.md` §2.7, the **canonical surface** for playbook prompts is `sprk_analysisaction.sprk_systemprompt`. This was previously misattributed to ADR-014 in some design docs; the citation was corrected 2026-06-10. Rationale: prompts are deployable artifacts that an SME may need to tune without a code deploy; storing them in Dataverse rows lets them update via solution import. Local `.json` files in `notes/handoffs/` are versioned design records, not the canonical source.

### 7.10 Q: "Two parallel POSTs for the same `(subject, topic, mode)` produce two playbook runs — is that a bug?"

**A**: No longer. r1 Task 053 closed this gap by adding a per-key `SemaphoreSlim` registry inside `InsightsPlaybookExecutionCache` (single-instance scope). After Task 053, two concurrent POSTs for the same cache key produce **exactly one** engine invocation; the second observer reads the cached artifact after the first writes through. Single-instance scope: if telemetry shows duplicate engine invocations across BFF instances for the same key, layer a Redis `SETNX` lock; do not re-author the interface (audit DR-002).

### 7.11 Q: "Pre-existing OnLoad handlers on the host form — will my deploy clobber them?"

**A**: Not if you use the r1 deploy pattern. The idempotent FormXml deploy script in `scripts/temp/Deploy-MatterInsightCard.ps1` patches `<formLibraries>` and `<event name="onload">` ONLY by adding missing entries (matched by `name` / `functionName`). Pre-existing handlers (e.g. `Spaarke.MatterKpi.onLoad`, `Spaarke.SubgridRollup.onLoad`) are preserved. Handlers fire in registration order — your pre-warm should be registered before the mount glue so the OnLoad sequence is "read envelope → POST (fire-and-forget) → mount glue resolves host → render".

### 7.12 Q: "How do I verify FR-22 dedup without writing a Parallel.For unit test?"

**A**: r1 Task 053 verified via **static-trace**: `ConcurrentDictionary.GetOrAdd` is well-documented as concurrent-safe (both threads get the same `SemaphoreSlim`), `SemaphoreSlim(1,1)` is the canonical per-key serializer, and the double-check `GetAsync` after lock acquisition is the textbook SingleFlight/LazyInit pattern. A defense-in-depth unit test (`Parallel.For` + Moq `Verify(Times.Once)`) is recommended as a regression guard but was not required to ship FR-22.

### 7.13 Q: "Dark mode breaks my card / colors don't adapt."

**A**: All InsightSummaryCard styles use Fluent v9 **semantic tokens** (`tokens.*` from `useInsightSummaryCardStyles.ts`) — no hex literals, no rgba literals, no CSS custom property color values. r1 Task 037 verified: 0 hex matches, 0 rgba matches, 0 raw color literals across the 7 card files. The Popover + Dialog wrappers use **Option A explicit FluentProvider re-wrap** per the portal-gotcha pattern (`fluent-v9-portal-gotcha.md`). If your card lights up wrong in dark mode, you almost certainly broke one of those two rules.

### 7.14 Q: "The card mount glue logs 'host element not found' — is that an error?"

**A**: It's an **expected staging signal** while the IIFE bundle gap (§5.4) is open. It proves the mount glue script LOADED, the namespace bound, `_resolveHost()` executed, and the subject contract-resolved. The host DOM element (`spaarke-matter-insight-card-host`) becomes present only after the FormXml WebResource control is wired to the target section via the solution-ZIP path. **Treat it as informational** until §5.4 closes.

---

## 8. Where to go next

- **r1 source code**: `src/client/shared/Spaarke.AI.Widgets/src/components/InsightSummaryCard/` — the React card you'll mount.
- **r1 mount glue reference**: `src/dataverse/forms/sprk_matter/insightCardMount.js` + `insightWidgetOnLoad.js` — fork these for new hosts.
- **r1 playbook reference**: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/matter-health-single.playbook.json` — copy as the starting shape for a new topic.
- **Topic registry SME guide**: the Power Apps Main form ("AI Topic Registration") — the SME-facing surface; no docs needed beyond §4.2.
- **Add the canonical UAT scenarios for your new topic** to `projects/<your-project>/notes/uat-scenarios.md` using r1's three-scenario template (real data / low data / kill-switch) as the shape.

---

*Authored 2026-06-11 by Insights Engine Widgets r1 task 067 / SC-13. Reviewed by the same engineer who authored the InsightSummaryCard component (Q-U7).*
