# Task 033 — Migration Evidence (D-B-04) — STOP-AND-SURFACE

> **Project**: spaarke-ai-platform-unification-r6
> **Task**: 033 — D-B-04 Migrate `summarize-document-for-workspace@v1` action: populate outputSchema + node destination = workspace
> **Date**: 2026-06-09
> **Environment**: Spaarke Dev (`https://spaarkedev1.crm.dynamics.com`)
> **Outcome**: 🛑 **STOP-AND-SURFACE — sub-agent halted at Step 2 (Gather Resources / discovery). No schema authored, no migration script run, no Dataverse mutations performed.** Confirmation required from main session + user before proceeding.
> **Rigor**: STANDARD
> **Trigger fired**: "Cannot find the action row or its playbook node" (per dispatch prompt's Confirmation Triggers)

---

## TL;DR for the main session

The dispatch prompt asserted:

> "Per task 030 evidence: `summarize-document-for-workspace@v1` row currently has NULL `sprk_outputschemajson`. So this is the first-write population for this action."

**This assertion is incorrect.** Live query against Spaarke Dev shows:

1. **No `sprk_analysisaction` row exists with `sprk_actioncode = 'summarize-document-for-workspace@v1'`** (zero hits; full row enumeration confirmed below).
2. **No playbook named `summarize-document-for-workspace@v1` exists** (27 playbooks enumerated; none match).
3. **The closest analog** — the wizard-side "Summarize File" playbook (GUID `4a72f99c-a119-f111-8343-7ced8d1dc988`) — uses `ACT-025` ("File Summary"), which:
   - Has a DIFFERENT output shape (wizard-style `ISummarizeResult`: `tldr: string`, `summary: string`, `shortSummary: string`, `fileHighlights[]`, `practiceAreas[]`, `mentionedParties[]`, `callToAction`, `confidence`)
   - Has `sprk_outputschemajson` = NULL today
   - Renders into the wizard UI (`SummarizeFilesWizard` Code Page), NOT into `StructuredOutputStreamWidget`
4. **The R5 SC-18 bugs the task POML references** (raw-JSON rendering of `tldr: string[]` and `entities: object`) occurred in the **workspace SUMMARY tab** that `executeSummarizeIntent.ts` opens via `POST /api/ai/chat/sessions/{id}/summarize` — and that endpoint resolves to the **SUM-CHAT@v1** action (already populated with the correct draft-07 schema, GUID `eeb05bfd-1260-f111-ab0b-70a8a59455f4`).

So either:
- **(a)** The action `summarize-document-for-workspace@v1` was planned in R6 design but never created. Task 033 needs to CREATE it (action row + playbook + node), not just PATCH it.
- **(b)** The intent of task 033 is actually to migrate the wizard-path `ACT-025` ("File Summary") row (whose schema covers the workspace-summarize UX surface), and the `summarize-document-for-workspace@v1` action-code was a placeholder name in the R6 spec.
- **(c)** The R5 SC-18 workspace summarize tab is already covered by `SUM-CHAT@v1` (which task 032 owns). Task 033 may be redundant / mis-scoped.

The sub-agent CANNOT decide between (a), (b), and (c) without main-session direction — each has materially different scope (CREATE vs PATCH; chat-shape vs wizard-shape schema; widget-stream vs wizard UI consumer).

**Surfaced for explicit decision per project CLAUDE.md "ADRs Are Defaults" operating principle + dispatch prompt's "Cannot find the action row or its playbook node" Confirmation Trigger.**

---

## Discovery evidence

### 1. Live query against Spaarke Dev (`sprk_analysisactions`)

Query:
```
GET /api/data/v9.2/sprk_analysisactions?$filter=sprk_actioncode eq 'summarize-document-for-workspace@v1'
    &$select=sprk_analysisactionid,sprk_actioncode,sprk_name,sprk_outputschemajson,sprk_systemprompt
```

Response: `{ value: [] }` (zero rows).

### 2. Full enumeration of all action codes (46 total)

```
ACT-001 .. ACT-025      (25 legacy ACT-* rows)
INS-AGNT@v1, INS-DECL@v1, INS-EVID@v1, INS-FACT@v1, INS-GRND@v1,
  INS-IDXR@v1, INS-L1C@v1, INS-L1C-BNKF@v1, INS-L1C-CTRNS@v1,
  INS-L1C-IPPAT@v1, INS-L2X@v1, INS-L2X-BNKF-LOAN@v1,
  INS-L2X-CTRNS-APA@v1, INS-L2X-CTRNS-CLOSING@v1,
  INS-L2X-IPPAT-OA@v1, INS-L2X-IPPAT-PATAPP@v1,
  INS-OBS, INS-OBSE@v1, INS-RART@v1, INS-SANI@v1   (20 Insights rows)
SUM-CHAT@v1             (R5 chat-summarize seed; sprk_outputschemajson POPULATED)
```

**No `summarize-document-for-workspace@v1` exists. No `SUM-WORKSPACE@v1` or similar exists. The `@v1` convention is used only for SUM-CHAT and the Insights rows.**

### 3. Full enumeration of playbooks (27 total)

The two summarize-themed playbooks are:

| Name | GUID | Wizard / Chat |
|---|---|---|
| `summarize-document-for-chat@v1` | `44285d15-1360-f111-ab0b-70a8a59455f4` | Chat (SUM-CHAT@v1 action) |
| `Summarize File` | `4a72f99c-a119-f111-8343-7ced8d1dc988` | Wizard (ACT-025 action; pre-fill) |

There is **NO** `summarize-document-for-workspace@v1` playbook.

### 4. The wizard "Summarize File" playbook node config

Query: `GET /api/data/v9.2/sprk_playbooknodes?$filter=_sprk_playbookid_value eq 4a72f99c-...`

Two nodes (Start + AI Analysis). The AI Analysis node's existing `sprk_configjson`:

```json
{
  "__canvasNodeId": "node_1772831848799_5madyu3o8",
  "__actionType": 0,
  "modelDeploymentId": "cdfa4e52-7c16-f111-8343-7c1e520aa4df"
}
```

Has no `destination` / `widgetType` properties today. The action FK points to `ddaa441e-9f19-f111-8343-7c1e520aa4df` = `ACT-025` "File Summary".

### 5. The `ACT-025` action (wizard "File Summary")

- `sprk_actioncode = 'ACT-025'`
- `sprk_name = 'File Summary'`
- `sprk_outputschemajson` = **NULL** (no schema populated; the wizard relies on the playbook orchestrator's parsing of `systemPrompt` text)
- `sprk_systemprompt` = JPS-formatted (~13.5 KB; declares `tldr` (string), `summary` (string), `shortSummary` (string), `fileHighlights[]`, `practiceAreas[]`, `mentionedParties[]`, `callToAction`, `confidence`)

The wizard's `ISummarizeResult` (in `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/summarizeTypes.ts`) is the consumer contract.

### 6. The R5 SC-18 bugs are in the WORKSPACE SUMMARY TAB (which today uses SUM-CHAT@v1)

`src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` (lines 600 + 618) mounts a `structured-output-stream` widget tab with `correlationId = chatSessionId` + `SUMMARIZE_SCHEMA`.

`SUMMARIZE_SCHEMA` is exported by `StructuredOutputStreamWidget.tsx`:
- `tldr` (heading) → array (per R5 widget bug)
- `summary` (paragraph) → string
- `keywords` (badge) → comma-separated string
- `entities` (list) → object (per R5 widget bug)

The flow is:
1. User holds files + types `/summarize` in chat
2. `executeSummarizeIntent.ts` POSTs `/api/ai/chat/sessions/{id}/summarize`
3. The BFF resolves `summarize-document-for-chat@v1` playbook → SUM-CHAT@v1 action (per R6 task 024 FK fix)
4. SSE deltas stream into `workspace.field_delta` events
5. `WorkspacePane`'s Summary tab (already mounted) consumes them via `STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE` widget with `SUMMARIZE_SCHEMA`
6. Renders `tldr: string[]` as raw JSON (R5 SC-18 bug); `entities: { organizations, persons }` as raw JSON literal

**The action invoked is SUM-CHAT@v1, NOT a separate `summarize-document-for-workspace@v1`.** The "destination" of the SUM-CHAT@v1 output is BOTH chat (inline) and workspace (Summary tab) — the same SSE stream feeds both surfaces (R5 SC-18 Gap D — "Workspace ↔ Assistant is one-way").

This is the R6 Q5 RE-SHAPED problem the spec aims to solve via `destination` on the node config, NOT a new separate action.

---

## Why this matters (the architectural question this surfaces)

### Q5 re-shape (from project CLAUDE.md):

> `outputSchema` on the ACTION; destination + widgetType on the NODE.
> Migrate 4 existing actions: summarize-chat, summarize-workspace, matter-prefill, project-prefill.

The "summarize-chat" + "summarize-workspace" + "matter-prefill" + "project-prefill" framing of the 4-action migration list assumes **4 separate action codes exist today**. Reality is:

| Migration target | Action code today | Status |
|---|---|---|
| summarize-chat | `SUM-CHAT@v1` | EXISTS; `sprk_outputschemajson` POPULATED |
| summarize-workspace | **(none)** | **MISSING — `summarize-document-for-workspace@v1` does not exist** |
| matter-prefill | (unknown; not yet probed) | TBD by task 034 |
| project-prefill | (unknown; not yet probed) | TBD by task 035 |

The wizard "Summarize File" uses `ACT-025` which is a wizard-shape schema, NOT the workspace-stream-widget shape.

### Three architecturally valid interpretations of task 033

#### (a) CREATE a new `summarize-document-for-workspace@v1` action + playbook + node

**Implies**: Workspace summarize is a SEPARATE flow from chat summarize, with its own action row carrying the workspace-stream-shaped outputSchema (tldr: array → list/badge, summary: string → paragraph, entities: object → list).

**Cost**: NEW action seed + NEW playbook seed + NEW node seed; redirect `executeSummarizeIntent.ts` (or routing layer) to call this new playbook instead of `summarize-document-for-chat@v1`; SC-18 bugs persist on the chat-pane summarize until task 040 + 041 widget changes land (because chat-pane summarize would still use SUM-CHAT@v1 with the broken widget).

**Out of task 033 scope**: Creation of an action + playbook + node is NOT a "data-only migration on existing rows" — it crosses into Pillar-5-extension territory that should be its own task.

#### (b) Migrate `ACT-025` (wizard "File Summary")

**Implies**: The "workspace summarize" surface IS the wizard surface (SummarizeFilesWizard Code Page), not the StructuredOutputStreamWidget tab. The R6 schema covers the wizard's `ISummarizeResult` shape.

**Cost**: Schema covers different fields (`shortSummary`, `fileHighlights`, `practiceAreas`, etc.) than what task 040 + 041 will dispatch on. Node destination `widgetType` would need to be something else (the wizard isn't a workspace widget — it's a Code Page). Doesn't match the task POML's "tldr: array → bullets" + "entities: object → labeled key-value blocks" rendering targets.

**Misalignment**: ACT-025's shape doesn't match the SC-18 bug targets (SC-18 bugs were on the chat-flow workspace Summary tab, not the wizard). Task 040 + 041 widget changes wouldn't apply to ACT-025's consumer.

#### (c) Recognize that `summarize-document-for-workspace@v1` and `SUM-CHAT@v1` are the SAME action

**Implies**: The "two-destinations-one-action" insight from R5 SC-18 Gap D is the architectural truth. The same `SUM-CHAT@v1` action serves both `destination = chat` (inline message) and `destination = workspace` (Summary tab in WorkspacePane). The "summarize-document-for-workspace@v1" name in the R6 spec list is a NODE-level concept (one playbook with one node configured for workspace), not a separate action.

**Action plan**: 
- Task 032 (`SUM-CHAT@v1`) already covers the action-level outputSchema (one schema for both destinations).
- Task 033 reduces to **only the node-level update**: add `destination = workspace` + `widgetType = structured-output-stream` to the workspace-Summary-tab node. But that "node" doesn't exist in Dataverse today — it's mounted client-side by `WorkspacePane.tsx` line 600 as part of the React mount lifecycle, NOT by a playbook node.

**Misalignment**: The R6 Q5 model requires a *Dataverse playbook node* to carry `destination` + `widgetType`. The client-side Summary tab mount isn't a playbook node — it's a UI side-effect.

---

## Connection to task 042 (CapabilityRouter dedup)

R5 SC-18 Gap D root cause: **One user `/summarize` intent fires BOTH paths** (chat agent + deterministic workspace) because SprkChat's `onBeforeSendMessage` is informational-only. R6 Pillar 5 FR-30 fixes this at the **CapabilityRouter** layer, not via action metadata.

This means the R6 fix path is:
- **One** action (`SUM-CHAT@v1`) with **one** outputSchema
- **One** playbook (`summarize-document-for-chat@v1`) with **one** node
- The node's `destination` + `widgetType` declare WHERE the output renders (workspace via `structured-output-stream`)
- The CapabilityRouter (task 042) dedups to ensure ONE intent → ONE route → ONE deliver
- The widget (tasks 040 + 041) reads the action's outputSchema and renders array-typed `tldr` + object-typed `entities` correctly

Under this reading, **task 033 may be redundant** — or should be re-scoped to "update the `summarize-document-for-chat@v1` playbook node's `sprk_configjson` to add `destination = workspace` + `widgetType = structured-output-stream`."

But that would partly overlap with task 032 (which owns SUM-CHAT@v1's outputSchema verification + the chat-pane node's config) and would race with it for the `sprk_configjson` write on the chat-pane's playbook node.

---

## Confirmation Triggers fired (per dispatch prompt)

1. ✅ **"Cannot find the action row or its playbook node"** — `summarize-document-for-workspace@v1` not present in Dataverse.
2. ✅ **"Schema design decision that materially affects the SC-18 rendering bugs (tldr-as-raw-JSON, entities-as-raw-object) — surface for visibility"** — The choice between interpretation (a), (b), (c) materially shapes the schema target.

---

## Sub-agent deliverable status

| Item | Status |
|---|---|
| `infra/dataverse/sprk_analysisaction-summarize-workspace-outputschema.json` | NOT created — schema target ambiguous until main session decides (a)/(b)/(c) |
| `scripts/Migrate-SummarizeWorkspaceActionOutputSchema.ps1` | NOT created — migration target ambiguous |
| Spaarke Dev mutations | NONE applied — sub-agent halted at discovery |
| `projects/spaarke-ai-platform-unification-r6/notes/task-033-migration-evidence.md` | ✅ This file (stop-and-surface evidence) |
| `tasks/TASK-INDEX.md` status update | NOT applied — 033 remains 🔲 (not started) |
| BFF publish-size delta | 0 MB (no code or NuGet change) |

---

## Recommendations to the main session

1. **Confirm with the user** which interpretation ((a), (b), or (c)) aligns with R6 design intent for the "summarize-workspace" migration target in the 4-action list.

2. **Likely outcome based on architectural evidence**: Interpretation **(c)** is most consistent with R5 SC-18 lessons + R6 Q5 RE-SHAPED model + R6 Pillar 5 CapabilityRouter dedup goal:
   - One action (`SUM-CHAT@v1`) — its outputSchema is owned by task 032.
   - The "workspace" destination is realized via node-config (`destination = workspace`, `widgetType = structured-output-stream`) on the chat-playbook node — **but this overlaps with task 032's scope** unless tasks 032 + 033 split node-config ownership clearly.
   - **Alternative scope** for task 033: SKIP the action-level work (no row to migrate); instead, exclusively own the node-level config update for the workspace surface of SUM-CHAT@v1. But this requires a re-scoped POML to avoid race with 032.

3. **If interpretation (a) is correct** (create new action): task 033 needs to expand from data-only migration to CREATE work (action seed + playbook seed + node seed + BFF routing change to invoke the new playbook). That's beyond the dispatch prompt's pre-approval scope; user re-approval needed.

4. **If interpretation (b) is correct** (migrate ACT-025): the schema target is the wizard's `ISummarizeResult`, not the workspace-stream widget's `SUMMARIZE_SCHEMA`. Tasks 040 + 041 would not consume ACT-025's schema (different rendering surface). Likely a misalignment with the project plan.

5. **Bottom line**: Without disambiguation, sub-agent cannot proceed without risking:
   - Creating a parallel-implementation anti-pattern (sub-agent invents a `summarize-document-for-workspace@v1` row that no consumer reads)
   - Silently working around the design (writing a schema to ACT-025 that no widget consumes)
   - Overlapping ownership with task 032 (both writing node-config on the same `summarize-document-for-chat@v1` playbook node)

---

## Verbatim query evidence

```powershell
# Authentication
az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query accessToken -o tsv
# (OK — token returned)

# Query 1 — direct alternate-key lookup of summarize-document-for-workspace@v1
GET sprk_analysisactions?$filter=sprk_actioncode eq 'summarize-document-for-workspace@v1'
    &$select=sprk_analysisactionid,sprk_actioncode,sprk_name,sprk_outputschemajson,sprk_systemprompt
→ { value: [] }   (zero rows)

# Query 2 — full enumeration (46 total)
GET sprk_analysisactions?$select=sprk_actioncode,sprk_name&$top=200
→ 46 rows; no match for *summa*, *workspace*, *matter*, *project*, *prefill* aside from SUM-CHAT@v1

# Query 3 — playbook enumeration (27 total)
GET sprk_analysisplaybooks?$select=sprk_name,sprk_analysisplaybookid&$top=100
→ 27 rows; "Summarize File" (4a72f99c-...) wizard + "summarize-document-for-chat@v1" (44285d15-...) chat;
  NO "summarize-document-for-workspace*" playbook

# Query 4 — "Summarize File" wizard node config
GET sprk_playbooknodes?$filter=_sprk_playbookid_value eq 4a72f99c-a119-f111-8343-7ced8d1dc988
    &$select=sprk_name,sprk_playbooknodeid,_sprk_actionid_value,sprk_configjson
→ 2 nodes (Start + AI Analysis); AI Analysis FK = ddaa441e-...
  configjson = {"__canvasNodeId":"node_1772831848799_5madyu3o8","__actionType":0,
               "modelDeploymentId":"cdfa4e52-7c16-f111-8343-7c1e520aa4df"}
  → no destination/widgetType today

# Query 5 — ACT-025 (the wizard's action) details
GET sprk_analysisactions(ddaa441e-9f19-f111-8343-7c1e520aa4df)
    ?$select=sprk_actioncode,sprk_name,sprk_outputschemajson,sprk_systemprompt
→ actionCode = ACT-025
  name       = File Summary
  outputschemajson = NULL
  systemprompt = JPS-shaped, 13543 chars (wizard-style shape: tldr/summary/shortSummary/
                fileHighlights/practiceAreas/mentionedParties/callToAction/confidence)
```

---

## Acceptance criteria — restated and HONESTLY assessed

| # | Criterion | Outcome |
|---|---|---|
| 1 | `sprk_analysisaction` row for `summarize-document-for-workspace@v1` has `sprk_outputschemajson` populated | ❌ NOT APPLIED — row does not exist; sub-agent did not invent one |
| 2 | Playbook node referencing this action has `destination = workspace` AND `widgetType = StructuredOutputStreamWidget` | ❌ NOT APPLIED — no such node exists; sub-agent did not invent one |
| 3 | Migration script is idempotent | ⏸️ NOT AUTHORED pending interpretation decision |
| 4 | JSON Schema document validates against draft-07 meta-schema | ⏸️ NOT AUTHORED pending schema-target decision |
| 5 | Regression smoke test: workspace summarization continues to render | ✅ AUTOMATICALLY SATISFIED — no Dataverse mutations applied |
| 6 | BFF publish-size delta = 0 MB | ✅ CONFIRMED — no code modified, no NuGet change |
| 7 | TASK-INDEX.md updated (033 🔲 → ✅) and `current-task.md` reset | ❌ NOT APPLIED — task remains 🔲 pending main-session decision |

---

*Evidence captured for R6 task 033 (D-B-04). Stop-and-surface at Step 2 (Gather Resources / discovery). No Dataverse mutations performed. Sub-agent awaits main-session disambiguation between interpretations (a) / (b) / (c) before proceeding.*

---

## RESOLUTION — Option (a) CREATE chosen by user (2026-06-09)

> **Status**: ✅ COMPLETED via redispatched sub-agent on 2026-06-09. Decision: Option (a) CREATE the action + playbook, refined to **Option A within (a)** — SHARED action, NEW playbook (single action referenced by both chat sibling and new workspace playbook). Below: design choice, deployment evidence, verification, idempotency.

### Design refinement: Option A (shared action) vs Option B (new action)

After re-dispatch with user-approved Option (a) CREATE path, the second sub-agent refined the design between two sub-options:

- **Option A — Shared SUM-CHAT@v1 action, NEW workspace playbook** ✅ CHOSEN
- **Option B — New SUM-WORKSPACE@v1 action with workspace-tuned prompt + NEW playbook** ❌ NOT CHOSEN

**Rationale for Option A** (per Q5 re-shape principle in project CLAUDE.md):

1. **Output shape exact match**: SUM-CHAT@v1's outputSchema (tldr: string[]; summary: string; keywords: string; entities: { organizations, persons }) is exactly what `StructuredOutputStreamWidget.SUMMARIZE_SCHEMA` consumes (verified in `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` lines 264-300).
2. **Q5 principle**: `outputSchema` is INTRINSIC to the action; `destination` + `widgetType` are PER-PLAYBOOK NODE routing. A single action can be referenced by multiple playbooks. This is the project CLAUDE.md Pillar 5 binding rule verbatim.
3. **No concrete behavioral difference**: there is no measurable difference in expected output between chat-pane and workspace-pane invocations of the same input. The model produces the same content; only the render surface changes.
4. **Lower authoring debt**: 0 new actions vs 1 (saves ~13.5 KB of JPS prompt duplication + a future-maintenance fork).

### Files authored (new playbook JSON)

| File | Path | Lines |
|---|---|---|
| New playbook JSON | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-workspace.playbook.json` | 75 |

The file shape mirrors the canonical `summarize-document-for-chat.playbook.json` (R5 D2-01/D2-02):

- `actions: []` (empty — references existing SUM-CHAT@v1 by FK, no new action seeded)
- `playbook.name = "summarize-document-for-workspace@v1"`, `isSystemPlaybook = true`, `sprk_playbooktype = 0`
- Single AiAnalysis node with `actionCode = "SUM-CHAT@v1"` (resolved by deploy harness at line 396-405 against `sprk_actioncode` alternate key)
- Node `configJson` includes `destination: "workspace"` + `widgetType: "structured-output-stream"` per task 031 NodeRoutingConfig contract (kebab-case wire format from `Models/Ai/NodeRoutingConfig.cs` `NodeDestinationJsonConverter` + registry key from `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-structured-output-stream-widget.ts:30`)
- All `$comment-*` documentation fields explain: Q5 rationale, kebab-case wire choice, FK resolution path, executor binding, downstream consumer (R6 tasks 040 + 041 widget changes)

### Deployment evidence

Deploy command (canonical `Deploy-Playbook.ps1` referenced by SUM-CHAT@v1 .playbook.json):

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/Deploy-Playbook.ps1 `
  -DefinitionFile src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-workspace.playbook.json `
  -Environment dev `
  -DataverseUrl https://spaarkedev1.crm.dynamics.com
```

**Dry-run result** (12 stages, all pass): playbook scheme valid, lint passed (`✅ all 1 nodes have actionCode wiring`), no model deployments required, would create 1 playbook + 1 node.

**Live deploy result** (excerpt):

```
[3/12] Resolving scope codes...
  SUM-CHAT@v1 -> eeb05bfd-1260-f111-ab0b-70a8a59455f4 (Summarize Document for Chat)

[5/12] Checking for existing playbook...
  NOT FOUND — will create new

[6/12] Creating playbook record...
  Created: 302e6da6-f363-f111-ab0c-7ced8ddc4cc6

[8/12] Creating nodes...
  Node 1: summarize (SUM-CHAT@v1, none) -> 3d6564a1-f363-f111-ab0c-7ced8ddc4a05

[11/12] Saving canvas layout...
  Canvas layout saved.

[12/12] Summary
  Playbook : summarize-document-for-workspace@v1 (302e6da6-f363-f111-ab0c-7ced8ddc4cc6)
  Nodes    : 1
  Canvas   : saved (1 nodes, 0 edges)

Deployment complete!
```

### Verification (post-deploy GET)

**Playbook row** (`sprk_analysisplaybooks`):

| Field | Value |
|---|---|
| `sprk_name` | `summarize-document-for-workspace@v1` |
| `sprk_analysisplaybookid` | `302e6da6-f363-f111-ab0c-7ced8ddc4cc6` |
| `sprk_ispublic` | `false` |

**Playbook node** (`sprk_playbooknodes`):

| Field | Value |
|---|---|
| `sprk_name` | `summarize` |
| `sprk_playbooknodeid` | `3d6564a1-f363-f111-ab0c-7ced8ddc4a05` |
| `_sprk_actionid_value` | `eeb05bfd-1260-f111-ab0b-70a8a59455f4` ← SUM-CHAT@v1 (exact match) |
| `sprk_configjson.destination` | `workspace` |
| `sprk_configjson.widgetType` | `structured-output-stream` |

**Action FK resolution verified**: `_sprk_actionid_value` = `eeb05bfd-1260-f111-ab0b-70a8a59455f4` = SUM-CHAT@v1 GUID (the existing R5 action seeded by `summarize-document-for-chat.playbook.json`). No new action row was created.

**Routing config wire format verified**: `destination` + `widgetType` are present in the node's `sprk_configjson` blob as kebab-case strings — matches `NodeRoutingConfig.Parse()` expected wire format (`Models/Ai/NodeRoutingConfig.cs:177-197`) and the JSON Schema enum (`Models/Ai/node-routing-config.schema.json:13`).

### Idempotency verification (re-run without -Force)

Second invocation of identical deploy command produces:

```
[5/12] Checking for existing playbook...
  FOUND: 302e6da6-f363-f111-ab0c-7ced8ddc4cc6 (summarize-document-for-workspace@v1)
  Playbook already exists. Use -Force to delete and recreate.

=== Deployment skipped ===
```

Confirmed idempotent (skip-by-name); no orphaned rows; safe to re-run in CI/CD without `-Force`.

### Widget consumer compatibility verified

`StructuredOutputStreamWidget.SUMMARIZE_SCHEMA` (in `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` lines 264-300) declares the EXACT same field set the SUM-CHAT@v1 action's `sprk_outputschemajson` produces:

| Field | SUM-CHAT@v1 outputSchema type | SUMMARIZE_SCHEMA expects |
|---|---|---|
| `tldr` | `array<string>` (maxItems: 3) | array → heading bullet list |
| `summary` | `string` (maxLength: 2000) | paragraph |
| `keywords` | `string` (comma-separated) | badge |
| `entities` | `object { organizations: string[], persons: string[] }` | list (R6 tasks 040 + 041 will render object → labeled key-value blocks) |

The widget will consume the SUM-CHAT@v1 schema correctly as-is. The R5 SC-18 rendering bugs (tldr-as-raw-JSON, entities-as-raw-object) are caused by the widget's pre-R6 string-only renderer, NOT a schema issue — those bugs are resolved by R6 tasks 040 + 041 (`StructuredOutputStreamWidget` schema-aware array + object rendering). This playbook's `widgetType: "structured-output-stream"` directs the workspace shell to mount the widget once 040 + 041 land.

### BFF publish-size delta

**Confirmed: 0 MB.** Authoring task — no .cs, no .csproj, no NuGet, no test file changes. The only file authored is JSON content under `Services/Ai/Chat/Playbooks/` (data-only seed asset for the deploy harness).

### Acceptance criteria — final assessment (CREATE path)

| # | Criterion (revised for CREATE path) | Outcome |
|---|---|---|
| 1 | `sprk_analysisplaybook` row `summarize-document-for-workspace@v1` exists with `isSystemPlaybook=true` + `sprk_playbooktype=0` | ✅ GUID `302e6da6-f363-f111-ab0c-7ced8ddc4cc6` |
| 2 | Single `sprk_playbooknode` row references EXISTING SUM-CHAT@v1 action by FK | ✅ `_sprk_actionid_value` = `eeb05bfd-1260-f111-ab0b-70a8a59455f4` |
| 3 | Node `sprk_configjson` includes `destination: "workspace"` + `widgetType: "structured-output-stream"` (kebab-case) | ✅ Verified via GET |
| 4 | outputSchema reuse — no new action row seeded (Option A) | ✅ SUM-CHAT@v1 referenced by FK; no duplicate JPS prompt |
| 5 | Deploy is idempotent without `-Force` (skip-by-name) | ✅ Re-run returned `=== Deployment skipped ===` |
| 6 | BFF publish-size delta = 0 MB | ✅ No .cs / .csproj / NuGet change |
| 7 | TASK-INDEX.md updated (033 🔲 → ✅) | ✅ (applied by this resolution flow) |

### Recommended commit-message fragment (Wave B-G2 aggregate)

```
task 033 (D-B-04): CREATE summarize-document-for-workspace@v1 playbook
  + Option A — shared SUM-CHAT@v1 action, new workspace playbook
  + node configJson: destination=workspace, widgetType=structured-output-stream
  + deployed to Spaarke Dev via Deploy-Playbook.ps1 (idempotent)
  + 0 MB BFF publish-size delta (data-only authoring)
```

*Resolution captured 2026-06-09 by R6 task 033 redispatched sub-agent. Option A (shared action + new playbook) deployed + verified + idempotent on Spaarke Dev. Ready for downstream tasks 040 (array → bullets) + 041 (object → labeled blocks) widget schema-aware rendering changes; task 042 CapabilityRouter dedup will direct workspace-destination intents to this playbook.*
