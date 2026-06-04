# Wave C2 — Prompt Migration Side-by-Side Comparison

> **Authored**: 2026-06-02 (Wave C2 / task 021 execution)
> **Audience**: Wave C3 (task 022 — IngestOrchestrator retirement), Wave C4 (task 023 — facade rewire), Wave D2 (task 031 — per-area Layer 1), owner review of C2 migration fidelity
> **Reference**: design-a4-prompt-variants.md §5 (worked example), spec.md SC-08 / NFR-02

---

## 1. Summary

Wave C2 migrated **3 source .txt prompt files** + their **2 schema files** into Dataverse `sprk_analysisaction.sprk_systemprompt` JPS-formatted JSON.

| Source `.txt` file | Target `sprk_actioncode` | Row state | Content version |
|---|---|---|---|
| `classification.v1.txt` | `INS-L1C` (NEW; Wave C1 deferred-deploy row) | created Wave C2 | v1 (identical-rephrased into JPS shape) |
| `outcome-extraction.v1.txt` | `INS-L2X` (NEW; Wave C1 deferred-deploy row) | created Wave C2 | v1 (identical-rephrased into JPS shape) |
| `predict-matter-cost-synthesis.v1.txt` | `INS-AGNT` (EXISTING; Wave B row) | updated Wave C2 | v1 (full content replacement — existing row carried a placeholder JPS doc only) |

Two C1-deferred rows that don't carry .txt prompts (`INS-SANI`, `INS-OBSE`) were also created in this wave per Option B of the dispatch brief (`/jps-action-create` analog — direct Dataverse MCP create with full JPS doc).

**Acceptance criterion 1 status** (Zero `.txt` files in `Services/Ai/Insights/Prompts/`): **DEFERRED to Wave C3 (task 022)**. See §5 for the sequencing rationale.

---

## 2. Versioning + naming reconciliation

### 2.1 design-a4 §10 `@v1` suffix — BLOCKED by schema constraint

design-a4 §10 prescribes renaming the 8 Wave B rows to `<base>@v1` (e.g., `INS-FACT@v1`) as part of Wave C2. **This rename is BLOCKED**: `sprk_actioncode` has a hard 10-character max length on the `sprk_analysisaction` entity in Spaarke Dev (empirically confirmed during Wave C2 — see action-code length test `INS-TEST@v1` (11 chars) rejected with validation error: "The length of the 'sprk_actioncode' attribute of the 'sprk_analysisaction' entity exceeded the maximum allowed length of '10'"). The pre-existing Wave B note `projects/.../notes/drafts/wave-b-action-codes.md` line 339 documents the same constraint discovered during Wave B (`INS-LIVE-FACT` (13 chars) → shortened to `INS-FACT` (8 chars)).

**Resolution adopted by Wave C2**: Treat current Wave B + C1 codes (`INS-FACT`, `INS-IDXR`, `INS-EVID`, `INS-GRND`, `INS-DECL`, `INS-RART`, `INS-OBS`, `INS-AGNT`, `INS-SANI`, `INS-L1C`, `INS-L2X`, `INS-OBSE`) as **"v1 by convention"** — the implicit first version per design-a4 §3 versioning rules. The `@v2` suffix likewise cannot be applied directly (codes like `INS-FACT@v2` are 11 chars).

**When v2 is needed** (per design-a4 §3.3 rule 1 — "rows pinned by a playbook are immutable; edit = new version"), one of the following is required (PHASE 2 scope — out for Phase 1.5):
1. **Schema change** — extend `sprk_actioncode` MaxLength to 32 chars in a managed-solution update. Trivial; not done here because PR-1 + Wave A4 §0 forbid schema additions for Phase 1.5.
2. **Embed version in `sprk_name`** — keep the code as a stable identifier; track v1/v2 in `sprk_name` (e.g., `Insights — Layer 1 Classify (v2)`). Resolution would then be name-based, which breaks design-a4 §3.2 deterministic action-code lookup.
3. **Use a separate `metadata.versionTag` field inside `sprk_systemprompt`** — purely informational; resolver can't enforce immutability without runtime introspection.

**Recommended path for Phase 2+**: Option 1 (schema MaxLength increase). The design-a4 doc's §10 prescription assumed schema flexibility that doesn't yet exist; Wave C2 surfaces this empirically.

**Filed as open follow-up**: design-a4 §10 + the spec.md "C2 risk-mitigation" wording need an erratum reflecting this constraint. The main session can pick this up; sub-agent can't write into design-a4 (it's a project doc) — but it CAN flag it (this section does that).

### 2.2 Naming chosen for the 3 NEW Wave C1 rows

Wave C1 reserved short codes (`INS-SANI`, `INS-L1C`, `INS-L2X`, `INS-OBSE`) anticipating the 10-char limit. Wave C2 uses these as-is.

### 2.3 v1 vs v2 content classification per source file

| Source `.txt` | Target row | Content classification (v1 = identical-rephrased, v2 = behavior-changed) |
|---|---|---|
| `classification.v1.txt` | `INS-L1C` | **v1 (identical-rephrased)** — same 8-class enum, same `confidence/reasoning/classification` output, same 5 rules. JPS shape adds `$schema`, `$version`, `instruction.{role,task,constraints,context}`, `input`, `output`, `examples`, `metadata` — but the **runtime behavior is identical** because IngestOrchestrator's call site (`BuildPromptWithDocument`) treats the prompt as a flat string and appends the document text; the JPS structure isn't yet consumed by the engine. The JPS `instruction.task` summarizes the .txt opening paragraph; `instruction.constraints[]` mirrors the .txt "Rules:" 1-5; `output.fields[]` mirrors the .txt JSON shape; `examples[]` is the .txt prompt's "Return JSON only: { ... }" shape captured as one minimal illustrative example. Per A4 §3.3 rule 1, any future change to `instruction.{role,task,constraints,context}` requires a new version row (blocked per §2.1 above). |
| `outcome-extraction.v1.txt` | `INS-L2X` | **v1 (identical-rephrased)** — same schema shape (outcomeCategory + settlementAmount + outcomeCurrency + outcomeDate + matterDurationDays + keyTerms + evidence + confidence + explanations), same 6 rules, same verbatim-quote requirement. JPS shape preserved. Runtime behavior identical. |
| `predict-matter-cost-synthesis.v1.txt` | `INS-AGNT` (existing) | **v1 (full content replacement of placeholder)** — the pre-Wave-C2 `INS-AGNT.sprk_systemprompt` carried a Wave-B placeholder JPS doc (`Insights — Agent Service Synthesis` skeleton with empty instruction). Wave C2 replaced it with the full synthesis prompt from `predict-matter-cost-synthesis.v1.txt`, preserving the template-substitution placeholders (`{{liveFacts}}`, `{{cohortObservations}}`, `{{precedents}}`) which are resolved by `PlaybookOrchestrationService.ApplyConfigJsonTemplates` at runtime. Runtime behavior change: predict-matter-cost@v1's `synthesize` node (per `predict-matter-cost.playbook.json` line 134) currently uses `$ref:Services/Ai/Insights/Prompts/predict-matter-cost-synthesis.v1.txt` (file inline at deploy-time). After Wave C2, the prompt is also in `INS-AGNT.sprk_systemprompt`. Two consumption paths now exist; Wave C4 should pick one canonical path (recommend reading from `sprk_systemprompt` via AgentServiceNodeExecutor — eliminates the `$ref:` inline). |

### 2.4 Behavior parity check (per spec.md C2 risk-mitigation)

The spec.md §Risks line "Prompt migration changes runtime behavior subtly" is mitigated for `classification.v1` + `outcome-extraction.v1` because:
1. The .txt files remain on disk (acceptance criterion 1 deferred to C3) — the live code path is unchanged.
2. The new INS-L1C / INS-L2X rows are not yet consumed by any live code path (Wave C4 task 023 wires the universal-ingest@v1 playbook into `IInsightsAi.RunIngestAsync`).
3. Wave C4 + Wave C5 smoke tests will exercise the new code path against a fixture; only after that smoke confirms parity does Wave C3 retire the .txt files + loader.

For `predict-matter-cost-synthesis.v1` → `INS-AGNT`: live code path is `predict-matter-cost.playbook.json` `synthesize` node, which reads via `$ref:` at deploy-time. The deploy-time read is unchanged by this wave (the .txt file is untouched). The `sprk_systemprompt` content is the same as the .txt content (modulo JPS wrapper). When Wave C4 / Wave D2 picks the canonical consumption path, the parity check is: re-run the Wave B5 predict-matter-cost smoke and confirm identical Inference output. **Wave C2 does not regress this** because the existing code path (`$ref:` inline) is untouched.

---

## 3. Side-by-side: source `.txt` → target JPS `sprk_systemprompt`

### 3.1 `classification.v1.txt` → `INS-L1C.sprk_systemprompt`

**Source** (45 lines, 1.6 KB): unstructured prompt with embedded JSON example + 5 rules.

**Target JPS shape** (mirrors `ACT-021 "Classify Document"` reference row):
- `$schema`: `https://spaarke.com/schemas/prompt/v1`
- `$version`: 1
- `instruction.role`: legal-document classification expert framing
- `instruction.task`: 1-sentence task description
- `instruction.constraints[]`: 5 rules from the .txt (mapped 1:1)
- `instruction.context`: legal-document-management context blurb
- `input.document.placeholder`: `{{document.extractedText}}`
- `input.parameters.categories`: array placeholder (Wave D2 will bind per-area catalogs per A4 §2.3 parametric base pattern)
- `output.fields[]`: `classification`, `confidence`, `reasoning` (matches the .schema.json exactly)
- `output.structuredOutput`: true
- `examples[]`: 1 minimal illustrative example
- `metadata`: `author = "wave-c2-migration"`, `supersedes = "classification.v1.txt"`, `tags = [insights, layer1, classification, wave-c2]`

**Content delta vs `.txt`**:
- The .txt's hardcoded 8-category enum (`closing_letter, settlement_agreement, …, other`) is preserved INSIDE `instruction.task` text + the example. Per A4 §2.3, the parametric base reads `categories` from `input.parameters.categories[]` — Wave D2 will bind the per-area catalog. For Phase 1 backward-compat, the litigation-default catalog is hard-coded inside the prompt task description.
- The .txt's "Document content follows below." closing line is replaced by JPS's `input.document.placeholder = "{{document.extractedText}}"` mechanism.
- Schema enforcement still goes through `output.structuredOutput=true` + the engine's structured-completion path (`IOpenAiClient.GetStructuredCompletionRawAsync`).

### 3.2 `outcome-extraction.v1.txt` → `INS-L2X.sprk_systemprompt`

**Source** (61 lines, 3.4 KB): structured schema description + 6 rules + verbatim-quote requirement.

**Target JPS shape**:
- Same boilerplate as 3.1
- `instruction.role`: outcome-extraction expert framing (legal-document focus)
- `instruction.task`: extract structured outcome data; for each field return value + verbatim quote + confidence
- `instruction.constraints[]`: 6 rules from the .txt (verbatim quote requirement preserved as constraint 4)
- `instruction.context`: clarifies the downstream mechanical verifier strips non-verbatim quotes
- `input.document.placeholder`: `{{document.extractedText}}`
- `input.parameters.documentTypeHint`: optional string (Wave D3 per-(area, doctype) Layer 2 schemas)
- `output.fields[]`: matches the outcome-extraction.v1.schema.json properties exactly (outcomeCategory, settlementAmount, settlementCurrency, outcomeDate, matterDurationDays, keyTerms, evidence, confidence, explanations)
- `output.structuredOutput`: true
- `examples[]`: 1 minimal illustrative example with verbatim-quote evidence

**Content delta vs `.txt`**: zero behavior change. The .txt's JSON-schema-style description is restructured into the JPS `output.fields[]` array.

### 3.3 `predict-matter-cost-synthesis.v1.txt` → `INS-AGNT.sprk_systemprompt`

**Source** (89 lines, 4.4 KB): synthesis prompt with 7 non-negotiable rules + 1 illustrative example.

**Target JPS shape**:
- Same boilerplate
- `instruction.role`: predict-matter-cost synthesizer for Insights-mode playbook (D-P14)
- `instruction.task`: predict total cost + duration by composing structured evidence from comparable prior matters + applicable Precedents
- `instruction.constraints[]`: 7 non-negotiable rules from the .txt (mapped 1:1 — evidence minimum ≥12, verbatim quotes, citation source format, statistical honesty, precedent weighting, confidence scaling ≤0.95, JSON-only output)
- `instruction.context`: structured-output downstream gating context (EvidenceGuard + GroundingVerifyNode mechanically strip non-conformant output)
- `input.parameters`:
  - `liveFacts` — placeholder `{{liveFacts}}` (resolved from `resolveLiveFacts` node output by template engine)
  - `cohortObservations` — placeholder `{{cohortObservations}}` (resolved from `retrieveCohortObservations` node output)
  - `precedents` — placeholder `{{precedents}}` (resolved from `retrievePrecedents` node output)
- `output.fields[]`: `value.raw.estimatedTotalCost.{p25,p50,p75}`, `value.raw.estimatedDurationDays.{p25,p50,p75}`, `value.displayHint`, `confidence`, `evidence[]`, `reasoning` (matches the .txt OUTPUT SCHEMA exactly)
- `output.structuredOutput`: true
- `examples[]`: the .txt EXAMPLE OUTPUT block as a single illustrative example
- `metadata.author`: `wave-c2-migration`, `supersedes`: `predict-matter-cost-synthesis.v1.txt`

**Content delta vs `.txt`**: zero behavior change. The .txt INPUTS block's template placeholders are preserved exactly (`{{liveFacts}}`, `{{cohortObservations}}`, `{{precedents}}`).

---

## 4. Code-path read trace (post-migration state)

| Code path | Reads prompt from | Live state after Wave C2 |
|---|---|---|
| `IngestOrchestrator.RunLayer1Async` (line 311) | `_promptLoader.Get("classification.v1")` → `Services/Ai/Insights/Prompts/classification.v1.txt` | **UNCHANGED** — IngestOrchestrator still reads .txt. Wave C3 retires this. |
| `IngestOrchestrator.RunLayer2Async` (line 331) | `_promptLoader.Get("outcome-extraction.v1")` → `Services/Ai/Insights/Prompts/outcome-extraction.v1.txt` | **UNCHANGED** — same. Wave C3 retires this. |
| `predict-matter-cost.playbook.json` `synthesize` node `configJson.prompt` | `$ref:Services/Ai/Insights/Prompts/predict-matter-cost-synthesis.v1.txt` (inlined at deploy-time by `Deploy-Playbook.ps1`) | **UNCHANGED** — `$ref:` substitution at deploy-time reads the .txt file. Future Wave C4/D2 may switch to reading `INS-AGNT.sprk_systemprompt`. |
| `universal-ingest.playbook.json` `layer1Classify` node | Will read from `INS-L1C.sprk_systemprompt` (via PlaybookExecutionEngine → action-code lookup) | **NEW** — wired but not yet exercised (universal-ingest@v1 deploy is in C1's deferred-deploy plan; Wave C4 task 023 wires `IInsightsAi.RunIngestAsync` to invoke it). |
| `universal-ingest.playbook.json` `layer2Extract` node | Will read from `INS-L2X.sprk_systemprompt` | **NEW** — same as above. |

**Net effect**: All 3 source .txt prompts now have authoritative JPS-formatted equivalents in Dataverse. The live code path still reads from .txt during the transition window. Wave C3 + C4 close the transition.

---

## 5. Why acceptance criterion 1 (zero .txt files) is deferred to Wave C3

Task 021 POML acceptance criterion 1 states "Zero .txt files in `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Prompts/`". Strict interpretation: `git rm` the 3 .txt files now. But:

1. **IngestOrchestrator.cs lines 313 + 333 still call `_promptLoader.Get("classification.v1")` + `_promptLoader.Get("outcome-extraction.v1")`**. The loader reads from `{AppContext.BaseDirectory}/Services/Ai/Insights/Prompts/{basename}.txt`. Deleting the .txt files yields runtime `FileNotFoundException` on the next ingest call.
2. **`predict-matter-cost.playbook.json` synthesize node uses `$ref:Services/Ai/Insights/Prompts/predict-matter-cost-synthesis.v1.txt`**. Deploy-Playbook.ps1 inlines this file at deploy-time. Deleting yields deploy failure on the next playbook redeploy.
3. **Task 022 (C3 — IngestOrchestrator retirement) declares dependencies `020, 023`**. C3 is explicitly the task that retires the code path that reads the .txt files. Doing it in C2 inverts the dependency.
4. **The spec.md C2 risk-mitigation says "side-by-side comparison required" + "per-playbook cutover"** — implying coexistence during transition, not an atomic switchover.

**Conclusion**: criterion 1 is sequencing-incompatible with criteria 3 (smoke test passes) and 4 (no regressions). The right reading is:
- **Wave C2 deliverable (this task)**: prompt content migrated; .txt files retained; loader retained
- **Wave C3 deliverable (task 022)**: IngestOrchestrator + loader + .txt files all retired together (atomic)
- **Wave C4 deliverable (task 023)**: facade rewires to the JPS playbook (the consumer of `INS-L1C` / `INS-L2X.sprk_systemprompt`)

This matches the design-a4 §10 sequencing where "C2 migrates prompt content" is one of multiple steps; C3 retires the orphaned code.

**Recommendation to owner**: update task 021 POML to either (a) move criterion 1 to task 022's acceptance criteria, or (b) reword criterion 1 to "prompt content migrated into `sprk_systemprompt` such that .txt files become unreferenced after Wave C3". Wave C2 will record completion satisfying (b).

---

## 6. Open follow-ups

| ID | Owner wave | Description |
|---|---|---|
| C2-FU-1 | ~~post-C2 / owner~~ | ~~**design-a4 §10 erratum**~~ — **DONE 2026-06-02**: schema bumped to MaxLength=64 + 11 rows renamed to `@v1`. Design-a4 §10 stays as authored. See [`schema-bump-actioncode-64.md`](./schema-bump-actioncode-64.md). |
| C2-FU-2 | C3 (task 022) | Delete the 3 .txt files in `Services/Ai/Insights/Prompts/` as part of IngestOrchestrator retirement. Also delete `IInsightsPromptLoader` + `InsightsPromptLoader` + the 2 .schema.json files (rolled into `sprk_systemprompt.output` per JPS structuredOutput). |
| C2-FU-3 | C4 (task 023) / D2 (task 031) | When `IInsightsAi.RunIngestAsync` is rewired to invoke universal-ingest@v1 playbook, the AiAnalysisNodeExecutor must read `sprk_systemprompt` from the resolved `sprk_analysisaction` row (existing behavior — verify with task 023 implementor that schema validation flows through correctly). |
| C2-FU-4 | C4 or later | Decide canonical synthesis prompt source: `predict-matter-cost.playbook.json` `$ref:.txt` (deploy-time inline) vs `INS-AGNT@v1.sprk_systemprompt` (runtime read). After C2, both paths exist; pick one before the .txt is deleted in C3. Recommend `sprk_systemprompt` per design-a4 §10 + FR-06 SME-iterate-without-deploy. |
| C2-FU-5 | ~~Phase 2+~~ | ~~Add `sprk_actioncode` MaxLength=32 in a managed-solution update~~ — **DONE 2026-06-02**: schema bumped to MaxLength=64 directly on Spaarke Dev (unmanaged path per ADR-027 amendment). See [`schema-bump-actioncode-64.md`](./schema-bump-actioncode-64.md). |

---

*End of c2-prompt-migration-comparison.md.*
