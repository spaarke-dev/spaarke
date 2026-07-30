# 022 — `TRIAGE-EMAIL` Action Authoring (`triage-email`)

> **Task**: 022 (P2, catalog authoring — STANDARD rigor) · **Date**: 2026-07-29 · **Method**: `jps-action-create` pattern (manual authoring against the skill's Step 1-4 workflow — no live skill-invocation tool available in this run), checked against the `jps-validate` skill's Step 2-7 checklist (manual check-by-check, same reason). **No BFF `.cs` touched. No Dataverse write performed** (this task authors the mirror file only; deploying the row to `spaarkedev1` is a later step, likely folded into 023 or 060).

---

## 1. What was authored

**File**: [`infra/dataverse/actions/triage-email.action.json`](../../../infra/dataverse/actions/triage-email.action.json)

- **`actionCode`**: `triage-email` (no version suffix — matches current owner-hygiene convention: `nda-review`, `compose-*`, `list-tasks`).
- **`name`**: "Triage Email"
- **Action type**: `prompted` (ADR-039) — `actionType: 0` = `ExecutorType.AiAnalysis`, the existing prompt-driven executor (`ActionRunner` + `PromptSchemaRenderer`). **No new executor.** Lands on the closed catalog (`sprk_analysisaction` row), never the frozen node-graph engine / `PlaybookOrchestrationService` / `IInvokePlaybookAi`.
- **`modelTier`**: `Fast` (`AiModelTier.Fast = 100000000` per `Services/Ai/PublicContracts/Binding.cs`) — deliberately the cheap/fast tier, since this Action performs a light structuring/mapping pass over an **already-produced** classification signal, not fresh deep reasoning. Reinforces the "no second full LLM pass" cost intent even though a (cheap) call still occurs.
- **`temperature`**: `0.2` (faithful mapping/summarizing, not creative — matches the extraction/analysis convention, e.g. `compose-explain-clause`).

### Shape choice (load-bearing — read before extending)

Unlike the sibling files in `infra/dataverse/actions/` (`nda-review.action.json`, `compose-*.action.json`), which store a **pre-rendered flat `systemPrompt` string + a raw `outputSchema`**, this file's root **IS the classic JPS document** (`$schema` / `instruction` / `input` / `output` / `examples` / `metadata`), wrapped with the same deploy-row scalars (`actionCode`/`name`/`description`/`actionType`/`modelTier`/`temperature`) the sibling files also carry.

**Why**: the `category` output field needs the JPS **`$choices` dynamic-lookup mechanism** (`lookup:sprk_triagecategory.sprk_name`) to stay Dataverse-tunable per **FR-16** ("an admin can add/reweight categories without code"). A pre-rendered flat prompt + a static `outputSchema` enum would hardcode the 7 seeded taxonomy names into the Action and require a redeploy every time an admin adds a category — breaking FR-16's own acceptance criterion. `LookupChoicesResolver.cs` confirms `lookup:`/`optionset:`/`multiselect:`/`boolean:` are all real, implemented `$choices` prefixes (not just documented aspiration), so this is a grounded design choice, not skill-literalism.

At deploy time, the classic-JPS content (everything except the deploy-row scalars) is the literal value for `sprk_analysisaction.sprk_systemprompt` — `PromptSchemaRenderer.IsJpsFormat()` detects the leading `{` + `"$schema"` key and renders it as structured JPS, resolving `$choices` via `LookupChoicesResolver` **at render time** (not a second classification pass).

---

## 2. The five output fields (FR-05 contract)

| Field | Type | Resolution | Persists to (per 001/011 note — task 025's job) |
|---|---|---|---|
| `category` | string | `$choices: lookup:sprk_triagecategory.sprk_name` (013 taxonomy: Court/Filing, Client instruction, Opposing counsel, Invoice/Billing, Scheduling, Administrative, Marketing/Noise) | `sprk_communication.sprk_triagecategory` (lookup) |
| `summary` | string, maxLength 320 | Free text, constrained to "exactly 2 lines" in the prompt | `sprk_communication.sprk_triagesummary` |
| `obligations` | array of string (maxItems 8) | Free text list | `sprk_communication.sprk_triageobligation` (**singular** — task 025 serializes the array to lean JSON per D-06) |
| `priority` | string | `$choices: optionset:sprk_communication.sprk_triagepriority` (Urgent/High/Medium/Low) | `sprk_communication.sprk_triagepriority` |
| `reviewOutcome` | string | `$choices: optionset:sprk_communication.sprk_reviewoutcome` (D-05 closed set: File/Update/Route/Dismiss/Pending) | `sprk_communication.sprk_reviewoutcome` |

All three `$choices` fields deliberately carry **no static `enum`** alongside `$choices` (jps-validate CHECK 17 — choices override enum; a static enum would drift from the live Dataverse taxonomy/option-set labels).

---

## 3. The "no second full LLM pass" contract — how it's enforced

Not just prompt wording — structural:
- `input.classification` (required) takes the **already-produced** `CommunicationClassificationResult` (candidateRecordTypes[], category, urgency, obligations[], suggestedActions[], privilegeFlagged, rationale) from `AiClassificationRung` as the Action's primary input.
- `input.message` (subject + bodyText, bounded 20000 chars) is supplied **only** as supporting grounding for the 2-line summary and finer-grained obligation/date detail — the instruction explicitly forbids treating it as a directive to re-classify from scratch, and only permits falling back to it when the classification signal is sparse/missing.
- `instruction.role`/`task`/`constraints` all state the reuse-not-rederive contract explicitly (see the authored file for exact wording).

**Task 023's job**: wire the actual runtime binding — feeding `AiClassificationRung`'s produced `CommunicationClassificationResult` + the normalized message into this Action's `input.classification`/`input.message` via the `Services/Ai/PublicContracts/` facade (never `IOpenAiClient`/`IPlaybookService` directly, per ADR-013/NFR-03).

---

## 4. §11 Component Justification (restated from the POML + the action file's `$comment-justification`)

1. **Existing** — No triage capability exists. `AiClassificationRung` produces a classification *signal* (category/urgency/obligations/suggestedActions/rationale, metadata-only) but never the `{category, summary, obligations[], priority, reviewOutcome}` triage output a reviewer or the RI-confidence scorer (024) can consume. Grep-confirmed no existing Action produces this shape.
2. **Extension** — This Action reuses the classification signal (no re-classification) and adds only a structuring/mapping/summarizing layer: new catalog data (ADR-039), not a new engine, not a second LLM pass.
3. **Cost-of-doing-nothing** — Without it, `sprk_communication` has no category/summary/obligations/priority/reviewOutcome. Pillar 1 (triage) fails and success-criterion 4 (opening an email shows category/2-line summary/obligations/priority) cannot pass. Concrete contract failure.

---

## 5. What is explicitly DEFERRED to task 023 (do not re-author here)

- The `sprk_playbookconsumer` **Binding** row (`infra/dataverse/sprk_playbookconsumer-rows.json` — reference pattern for a Binding row: `consumerType`, `actionCode: "triage-email"`, `disposition`, `risk`, `captureMode`, `onEventBindings` for the enrichment-path trigger).
- The **OpenAI function-parameters-subset input schema mirror** under `infra/dataverse/inputschemas/` (the loop-projectable tool-calling contract, e.g. `create-task-v1.input.schema.json`'s shape) — this is DIFFERENT from the JPS `input{}` section already authored above (that's the Action's own prompt-intrinsic input, Home A); the `inputschemas/` mirror is the Binding-level tool-calling contract, if this capability is ever exposed as an agent-callable tool (likely NOT needed here since this Action triggers automatically off the enrichment path, not a chat/loop tool call — but 023 should confirm and either add it or explicitly note "not tool-projectable").
- **RAG grounding** — `scopes.$knowledge` over the matter's own prior correspondence (FR-06). Deliberately **omitted** from the authored Action (see `$comment-scopes-omitted` in the file). 023 adds this scope.
- **The golden-utterance eval case** (NFR-07 — blocking merge gate). Discharged at the Binding merge gate (023), not here.
- **The enrichment/event trigger wiring** through `Services/Ai/PublicContracts/` — the actual `.cs` call site that invokes this Action on the enrichment path, immediately after `AiClassificationRung` runs.
- **Deploying the row** to `spaarkedev1` via Dataverse MCP `create_record` + post-deploy verification (`jps-action-create` Step 5.5) — not performed in this task; whichever of 023/060 owns Dataverse writes should do this and verify `sprk_actioncode = 'triage-email'` resolves to exactly 1 row with non-empty `sprk_systemprompt` and `sprk_outputschemajson`.

---

## 6. jps-validate checklist — manual pass record

No live `Skill` invocation tool was available in this execution context (sub-agent tool set: Read/Write/Edit/Bash/Grep/Glob/Agent/Artifact/PowerShell/ToolSearch — no `Skill` tool). The `jps-validate` Step 2-7 checklist was applied **manually** (parsed the authored JSON in Python and asserted each check programmatically). **Result: ALL CHECKS PASS** — valid JSON; `$schema` present and correct; `instruction.role`/`task` non-empty; 5 output fields, each with `name`/valid `type`/`description`/`maxLength` (string fields); the 3 `$choices` fields use supported prefixes (`lookup:`, `optionset:` ×2) each with exactly one `.` separator and no static `enum` alongside; `instruction.constraints` is an array; `metadata.description` present; 1 example provided; format-detection (`IsJpsFormat()`) would succeed (content starts with `{` and contains `"$schema"`).

**Main session / next task should re-run the actual `jps-validate` skill** (or the render test against `PromptSchemaRenderer`) once the `Skill` tool is available, as a belt-and-suspenders check on top of this manual pass.

---

## 7. Downstream unblock status

| Task | Depends on | Status |
|---|---|---|
| 023 (Binding + input/output schema + eval + RAG + trigger) | This Action's `actionCode` (`triage-email`) + the 5-field output contract | ✅ **UNBLOCKED** — bind against `actionCode: "triage-email"`; add RAG `scopes.$knowledge`; add the eval case; wire the enrichment trigger through `Services/Ai/PublicContracts/` |
| 024 (RI-confidence scorer) | Reads the `priority`/urgency output this Action produces (via 023's persisted fields) | ✅ **UNBLOCKED** (transitively, once 023 lands) |
| 025 (persist triage output) | The 5-field shape + as-built column mapping (table above) | ✅ **UNBLOCKED** — field-name mapping table in §2 above is copy-ready |

**No Binding, eval case, RAG, or trigger authored in this task** — scope held strictly to the Action definition per the POML's negative acceptance criteria.
