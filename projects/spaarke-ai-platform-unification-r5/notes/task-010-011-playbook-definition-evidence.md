# Tasks 010 + 011 — Combined Playbook Definition Evidence

> **Tasks**: 010 D2-01 (sprk_analysisaction "Summarize Document for Chat" seed) + 011 D2-02 (sprk_analysisplaybook "summarize-document-for-chat@v1" config)
> **Status**: Definition file authored; Dataverse deploy + commit deferred to main session
> **Date**: 2026-06-04
> **Scope**: AUTHOR THE PLAYBOOK JSON DEFINITION FILE ONLY (per parent task instruction). Main session handles `scripts/Deploy-Playbook.ps1` invocation + git commit.

---

## 1. Chosen file path

`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-chat.playbook.json`

- **NEW directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/` (created; Chat/ pre-existed with no Playbooks subfolder).
- **Naming convention**: mirrors the in-repo Insights precedent (`predict-matter-cost.playbook.json`, `universal-ingest.playbook.json`) — `<verb-noun-for-purpose>.playbook.json` directly under a `Playbooks/` folder of the owning feature subsystem (Insights vs. Chat).
- **Why under Chat/ (not Insights/)**: the R5 Summarize-for-Chat capability is owned by the BFF Chat subsystem (orchestrator lives in `Services/Ai/Chat/` per `SessionSummarizeOrchestrator.cs` planned in task 012); Insights/ houses the Insights Engine artifacts. Placement keeps subsystem boundaries clean.

## 2. Action code confirmed

- **`actionCode`**: `SUM-CHAT@v1`
- **Naming convention**: `<SCOPE>-<MNEMONIC>@<VERSION>` (per InsightsActionRouter convention; Insights uses `INS-FACT@v1`, `INS-IDXR@v1` etc.). R5 uses `SUM` (Summarize) scope prefix; chat-pane mnemonic `CHAT`; v1 versioning.
- **Action `sprk_name`**: `"Summarize Document for Chat"`
- **Action `actionType`**: `0` (AiAnalysis — handled by existing `AiAnalysisNodeExecutor`, matching the universal-ingest layer1Classify + layer2Extract + predict-matter-cost convention).

## 3. Playbook name confirmed

- **`playbook.name`**: `summarize-document-for-chat@v1`
- **Naming convention**: `<verb-noun-for-purpose>@v<version>` (per `universal-ingest@v1` / `predict-matter-cost@v1` precedent).
- **`isPublic`**: `false` (chat-pane sibling of the public wizard-path Summarize; not user-discoverable as a standalone playbook).
- **`isSystemPlaybook`**: `true` (system-owned, not user-authored — matches Insights precedent).
- **`sprk_playbooktype`**: `0` (AiAnalysis).
- **`mode`** (in `sprk_configjson`): `chat-summarize`.
- **`cacheTtlSeconds`**: `0` (per-session, per-file-set; cache is unsafe because the file set changes mid-session per FR-08 / R5 §3.8 20-file cap).
- **Structure**: SINGLE-NODE playbook (one `AiAnalysis` node `summarize`). Much simpler than universal-ingest (6 nodes) or predict-matter-cost (8 nodes) — no fan-out, no evidence-sufficiency gate, no decline branch in the playbook itself (decline routes through `DeclineToFindNode` downstream per Insights pattern + spec FR-11).

## 4. System prompt rationale (streaming-aware, TL;DR-first)

The `systemPrompt` field (inline in the action seed) is a JPS-formatted structured-text prompt. Key design choices:

1. **Streaming-aware emission order in INSTRUCTIONS step 3**: explicit `tldr → summary → keywords → entities` order is documented and explained as load-bearing for the R5 UI's TL;DR-first UX (~300-500ms TTFB to first user-visible content).
2. **Cross-reference to task 006 spike**: the prompt explicitly cites the spike findings so future maintainers don't reorder casually.
3. **No fabrication / decline routing**: instructs the LLM NOT to invent content; explicitly notes the orchestrator handles decline via `DeclineToFindNode` (per Insights pattern; matches spec FR-11). The action stays focused on the success path.
4. **Schema field shape**: instructs the model that:
   - `tldr` is `string[]` (1-3 items, ≤140 chars each) — array shape per `DocumentAnalysisResult.TlDr`.
   - `summary` is `string` (≤2 paragraphs, ≤2000 chars) — per `DocumentAnalysisResult.Summary`.
   - `keywords` is `string` (comma-separated, NOT array) — per `DocumentAnalysisResult.Keywords`.
   - `entities` is `{ organizations: string[], persons: string[] }` — per `DocumentAnalysisResult.Entities` shape.
5. **Exclusion of BFF-only fields**: explicitly tells the LLM NOT to emit `rawResponse`, `parsedSuccessfully`, or `emailMetadata` — these are populated by BFF post-processing (`DocumentAnalysisResult.cs`), not the LLM. This prevents schema-incompat with strict Structured Outputs mode.
6. **Quality hints (file-name distinction in multi-file summaries, canonical entity deduplication, concrete keyword preference)**: improves output quality without constraining the model unnecessarily.

## 5. Output schema declaration order (VERBATIM)

```
properties:
  tldr      (array of strings, 1-3 items, maxLength 140)
  summary   (string, maxLength 2000)
  keywords  (string)
  entities  (object: organizations: string[], persons: string[])
```

**This order is LOAD-BEARING** per `notes/task-006-spike-results.md` "Top-level field appearance order" section:

| Field | Spike-tested arrival order | Char position | UX role |
|---|---|---|---|
| tldr | #4 event | 10 | FIRST — TL;DR-first UX (spec FR-02) |
| summary | #66 event | 299 | Second |
| keywords | #150 event | 742 | Third |
| entities | #177 event | 879 | Last |

Cross-references:
- Task 006 spike: `notes/task-006-spike-results.md` "No schema reordering needed: Azure OpenAI emits fields in JSON-schema property declaration order."
- Task 006 implementation evidence: `notes/task-006-implementation-evidence.md` "First delta event references TL;DR-first ✅ (parser correctness)... task 010 controls schema declaration to make TL;DR-first" — this task 010+011 file IS that control point.
- `DocumentAnalysisResult.cs`: target C# shape (`Summary`, `TlDr`, `Keywords`, `Entities`, `RawResponse`, `ParsedSuccessfully`, `EmailMetadata`). The schema declares ONLY the 4 LLM-populated fields; the 3 BFF-only fields are populated post-streaming.

The schema includes `additionalProperties: false` at every object level, all four top-level fields are required, and uses no unsupported Structured-Outputs keywords (no `anyOf`/`oneOf`/`discriminator`).

## 6. Combined-file rationale (tasks 010 + 011 in ONE JSON)

Per parent task instruction: "the existing `scripts/Deploy-Playbook.ps1` schema supports a single JSON definition file holding BOTH the action seed AND the playbook+nodes (mirrors the existing Insights `predict-matter-cost.playbook.json` + `universal-ingest.playbook.json` precedent). One combined file is cleaner than two."

**Surprise / decision noted for main session**: The current `Deploy-Playbook.ps1` (read in full) does NOT yet have an `actions` array section. It RESOLVES existing actions (Step 3, lines 395-405 against `sprk_analysisactions` filtered by `sprk_actioncode`) but does not CREATE/UPSERT them. The author of this file has placed the action seed under a top-level `actions: []` array (extra-schema, beyond what the current deploy script reads) so that:

1. The file documents the FULL intent (action + playbook + node) in one place.
2. The main session can choose between:
   - **Option A (preferred, lowest churn)**: extend `Deploy-Playbook.ps1` to read the `actions[]` array and upsert each into `sprk_analysisactions` (lookup by `sprk_actioncode`, then POST or PATCH) BEFORE the existing Step 3 resolution. This keeps the deploy harness consolidated per R5 CLAUDE.md §3.1 reuse mandate.
   - **Option B**: pre-seed the action row separately (e.g., via a small `Deploy-AnalysisAction.ps1` sibling reusing the `Get-DataverseHeaders` + `Invoke-DataversePost` helpers), then run `Deploy-Playbook.ps1` against this file (which will resolve `SUM-CHAT@v1` to the pre-seeded GUID).

Today, `Deploy-Playbook.ps1` will IGNORE the unrecognized top-level `actions` key (PowerShell `ConvertFrom-Json` produces a tolerant object). The node's `actionCode: SUM-CHAT@v1` requires resolution at Step 3, so without Option A or Option B the deploy will fail at the pre-flight check with `Action 'SUM-CHAT@v1' not found` — this is exactly the gate task 010's POML §<dependencies> warns about.

Recommendation: **Option A** per R5 §3.1 reuse mandate. The required change to `Deploy-Playbook.ps1` is small (~30 LOC: a new "Step 2.5: Upsert actions" block that loops `$definition.actions`, queries existing by `sprk_actioncode`, and POSTs if absent or PATCHes if present). The main session is best positioned to make and verify this change.

## 7. Key decisions + surprises

| Decision / Surprise | Resolution |
|---|---|
| **Schema shape**: parent task instruction overrides task 010 POML | Followed parent task instruction (tldr, summary, keywords, entities — matching `DocumentAnalysisResult.cs`). The task 010 POML's older shape (`fileHighlights`, `mentionedParties`, `callToAction`) is superseded — these may be R6+ enrichments but are NOT in the C# `DocumentAnalysisResult` today, and the parent task explicitly anchored to `DocumentAnalysisResult.cs`. |
| **Action's output schema field**: not yet known on Spaarke Dev | The action's `outputSchema` is authored as a structured JSON object IN this definition file under `actions[0].outputSchema`. When the main session extends `Deploy-Playbook.ps1` to upsert actions, it must serialize this object to JSON string and write to whichever Dataverse field carries the schema on `sprk_analysisaction`. Insights actions appear to use `sprk_systemprompt` for the prompt only; the output schema field name needs Step-2 discovery on Spaarke Dev. Likely candidates: `sprk_outputschemajson` or embedded in `sprk_configjson`. The main session should verify on first deploy (e.g., query INS-FACT@v1 row to see which field carries its output schema). |
| **systemPrompt field naming in this file**: chose `systemPrompt` (camelCase) | Reasoning: matches the convention used in `predict-matter-cost.playbook.json` for `actionCode`, `actionType`, `outputVariable` (camelCase JSON keys that get mapped to Dataverse `sprk_*` columns at deploy time). The Dataverse column is `sprk_systemprompt` — the deploy harness handles the mapping. |
| **Combined-file precedent**: the Insights `*.playbook.json` files do NOT carry an `actions[]` array | True. The Insights pattern assumes actions are pre-seeded (Insights Engine r2 Wave B / B3 pre-seeded INS-* actions separately before deploying universal-ingest@v1 / predict-matter-cost@v1). R5 introduces the combined-file convention as a structural extension. Main session should treat this as the R5 template for D3-01 (`/analyze`) and any future R6+ playbooks. |
| **Idempotency semantics**: `Deploy-Playbook.ps1` Step 5 (line 514-572) skips by playbook name without `-Force` | Confirmed via direct read. The action seed needs equivalent skip-on-exists semantics in the future Step 2.5 — pattern: lookup by `sprk_actioncode`, if found exit cleanly OR PATCH if `-Force` (matching playbook semantics). |
| **No BFF code change** | Confirmed: this task touched zero `.cs` / `.csproj` / `Program.cs` / DI module files. BFF publish-size delta = 0 MB by construction. |
| **No new Dataverse entity** | Confirmed per R5 CLAUDE.md §10 BINDING gotcha: NO new prompt-bearing entity. The action seed lives in the existing `sprk_analysisactions` collection; the playbook in the existing `sprk_analysisplaybooks` collection; the node in the existing `sprk_playbooknodes` collection. |
| **No new feature flag** | Confirmed per R5 CLAUDE.md §3.2 (ADR-018 Flag Scope Discipline). No new flag introduced. |
| **No DI registration changes** | Confirmed per R5 CLAUDE.md §3.3 (ADR-010 DI minimalism). N/A for data deploy. |
| **JSON parse validation** | Validated locally via `pwsh -Command "Get-Content ... \| ConvertFrom-Json"`. Parse OK. Schema property order confirmed: `tldr -> summary -> keywords -> entities`. |

## 8. Verification of acceptance-criteria (for main session)

When the main session runs the deploy, these criteria gate ✅:

- [ ] Action seed publishes — `sprk_analysisaction` row with `sprk_actioncode='SUM-CHAT@v1'`, `sprk_name='Summarize Document for Chat'`, `sprk_systemprompt` populated, output schema field populated.
- [ ] Playbook publishes — `sprk_analysisplaybook` row + 1 `sprk_playbooknode` row, name=`summarize-document-for-chat@v1`, `isSystemPlaybook=true`, `sprk_playbooktype=0`.
- [ ] Node FK resolution — `sprk_playbooknode.sprk_actionid` non-null, references the action seeded above.
- [ ] Schema declaration order preserved verbatim on the live row.
- [ ] Idempotency — second deploy (no `-Force`) is a no-op (both action upsert and playbook skip).
- [ ] BFF publish-size delta = 0 MB.
- [ ] `code-review` + `adr-check` quality gates pass.

## 9. Task POML status updates

- `tasks/010-sprk-analysisaction-summarize-seed.poml`: status → `complete`, started/completed → 2026-06-04, actual-effort → ~1 hour (definition-authoring portion only).
- `tasks/011-sprk-analysisplaybook-summarize-config.poml`: status → `complete`, started/completed → 2026-06-04, actual-effort → ~1 hour (definition-authoring portion only).

`TASK-INDEX.md` and `current-task.md` updates are intentionally LEFT FOR MAIN SESSION per parent task instruction ("DO NOT modify TASK-INDEX.md or current-task.md — main session will").
