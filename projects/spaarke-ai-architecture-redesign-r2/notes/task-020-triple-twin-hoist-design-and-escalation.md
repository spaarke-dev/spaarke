# Task 020 — Triple-Twin Validator Hoist: Discovery, Drift Census, Design + Escalation

> **Task**: AIR2-020 (Phase A-infra, FR-A-01) · **Rigor**: FULL · **Model**: opus @ xhigh · **Step mode**: PRESCRIPTIVE
> **Status**: 🔔 **ESCALATED before implementation** (prescriptive-step surface mis-specification + source-direction ratification). No code written yet — the source-of-truth SHAPE is the load-bearing, hard-to-reverse decision and it must be ratified before migrating ~30 handler/row pairs and wiring two hot-path validators.
> **Author**: task-execute · 2026-07-08

---

## 1. What discovery found (ground truth, cited)

The task frames "three hand-maintained description twins" as **three copies of one guidance string**. Direct code/data inspection shows the reality is more specific — and one of the three named surfaces is **mis-cited**.

### 1a. The LLM-facing tool description comes from Dataverse, not code
- `ToolHandlerToAIFunctionAdapter.cs:361` — `public override string Description => _tool.Description ?? string.Empty;` where `_tool` is the `sprk_analysistool` row. Doc-comment `:359`: *"Sourced from `sprk_analysistool.sprk_description`."*
- The handler's `ToolHandlerMetadata.Description` (code, `IToolHandler.cs:154`) is consumed **only** by the discovery REST endpoint (`Api/Ai/HandlerEndpoints.cs:147,199`) — **never** by the LLM projection or routing. It is registry/documentation text.
- ⇒ The runtime contract the LLM sees is **`sprk_analysistool.sprk_description`** (live Dataverse), seeded from `infra/dataverse/sprk_analysistool-*-row.json` via `scripts/Seed-TypedHandlers.ps1`.

### 1b. The genuine "three mirrors" (confirmed by r1's own UAT note)
`spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round4-findings.md:274-280` documents the manual discipline verbatim:
> **Description guidance — three mirrors updated**: handler `Metadata.Description` + live catalog row `sprk_analysistool` … `sprk_description` (verified by re-read) + seed mirror `infra/dataverse/sprk_analysistool-dataverse-read-query-row.json`.

So the real triple-twin for each handler tool is:
1. **handler `Metadata.Description`** — `Services/Ai/Handlers/*.cs` (code; not LLM-facing)
2. **live `sprk_analysistool.sprk_description`** — Dataverse (LLM-facing)
3. **seed mirror `infra/dataverse/sprk_analysistool-*-row.json`** — the JSON that seeds #2

Since #2 is seeded from #3, the only *hand*-maintained drift is **#1 (code) vs #3 (JSON)**.

### 1c. The task's third surface is mis-cited (prescriptive-step defect)
The POML `<prompt>`, `<goal>`, `<pattern>` (line 57), and `<steps>` (2c/3) name **`infra/dataverse/inputschemas/`** as the third twin to make a GENERATED artifact and enforce parity on. Discovery proves this is a **different entity's input schema**, not a copy of the description prose:
- The 6 `infra/dataverse/inputschemas/*.input.schema.json` files mirror **`sprk_analysisaction.sprk_inputschema`** (JSON Schema: `type`/`properties`/`required` + per-*argument* `description`/`elicitation_prompt`). Wrapper shape `{ actionCode, actionId, environment, inputSchema }` (`create-task-v1.input.schema.json:2-6`).
- They contain **no** tool-selection description prose — no `sprk_description`, no tool-level description.
- They are already single-sourced ("mirror-first") and validated by `CatalogInputSchemaContractTests.cs` + `OpenAiFunctionSchemaValidator.FindFirstError`.
- ⇒ Implementing steps 2c/3 literally would enforce "parity" over the wrong surface (input schemas of a different entity), do nothing for the actual code↔JSON description drift, and risk destabilizing an already-working mirror pipeline.

The **correct** third surface is `infra/dataverse/sprk_analysistool-*-row.json` (per 1b). This is a citation correction of the same class as task 004's `JobStatusService` location fix — but because step mode is **PRESCRIPTIVE**, it must be surfaced, not silently substituted.

### 1d. A fourth surface the task doesn't name
`sprk_playbookconsumer.sprk_tooldescription` (Binding intent, `BindingCapabilityTool.cs:108`) is the LLM-facing description for **Action/Binding** capability tools (e.g. `CREATE-TASK@v1`) — a *different tool population* from the handler tools in 1a. Whether it is in-scope for single-sourcing is an open scope question (see §4).

---

## 2. Drift census — can the twins be reconciled without behavior change?

Compared COPY 1 (code `Metadata.Description`) vs COPY 2 (`sprk_description` in the paired `sprk_analysistool-*-row.json`, = the live LLM value) for all pairs, matched via the row's `sprk_handlerclass`.

| Classification | Count |
|---|---|
| IDENTICAL (byte-equal) | 2 (financial-calculator, citation-verify) |
| COSMETIC (reword / extra sentences / added examples — **no contradiction**) | 34 |
| **SEMANTIC (contradictory instruction)** | **0** |
| Unpaired handler (no row) | 1 (`GenericAnalysisHandler`) |

**Direction of drift is consistent**: the live JSON (COPY 2) is a **superset** of the code (COPY 1) in essentially every case — same rules plus extra guidance (entity maps, value-type lists, unsupported-dialect additions, scope/purpose enums). Examples: dataverse-create-record JSON appends a "Spaarke entity map" sentence absent from code; dataverse-read-query JSON extends the same unsupported-SQL ban list (`SELECT *`, `GETDATE()`, aggregates) + row-cap note.

### Consequence for the source direction (load-bearing)
- Choosing **COPY 2 (live JSON) as the authored source** = a **no-op for runtime LLM behavior** (live value preserved byte-identical). ✅ satisfies "zero behavioral change."
- Choosing **COPY 1 (code) as the source** would **drop** the live-only guidance = a content regression (still not a contradiction, but a *behavior change* in what the LLM is told). ❌
- ⇒ The zero-behavior-change constraint **forces the authored source to be the JSON seed row** (`sprk_description`), not code. This is also the ADR-039 "risk factors stay catalog-declared DATA" home and the existing live-value source — so Policy v2 (032), create-matter (042), `memory.write` (057), and Compose's 5 rows all author by adding/editing a JSON row, not by editing code.

**The strict escalation trigger in the POML (`<trigger>` line 111: "twins disagree semantically → STOP") does NOT fire.** Reconciliation is provably safe.

---

## 3. Recommended design (for ratification)

**Authored source** = `infra/dataverse/sprk_analysistool-*-row.json` → `sprk_description` (one authored record per handler tool; holds the description prose + the Policy-v2 risk-factor slots task 032 will populate as catalog DATA). Existing rows are **untouched** (live values byte-preserved).

**Three mirrors, generated/validated FROM it:**
1. **live `sprk_analysistool.sprk_description`** — already seeded from the JSON by `Seed-TypedHandlers.ps1` (identity; existing pipeline). **Validated at runtime** by the health check (live == authored).
2. **handler `Metadata.Description` (code)** — brought into byte-parity with the authored JSON, then **validated** by a build-time contract test. Two ways to achieve parity (the fork needing a decision):
   - **Option B (codegen — what step 2 literally asks for):** a generator emits `Handlers/Generated/CatalogToolDescriptions.g.cs` (one `const` per tool, deterministic ordering, no timestamps) from the JSON; each handler replaces its inline `Description:` literal with the const. Code == authored by construction; future edits regenerate. Touches ~30 handlers (mechanical) + adds a checked-in generated file + a regen script + drift test. Highest fidelity to the prescriptive step; largest hot-path blast radius.
   - **Option C (validate-only — minimal, §11-preferred):** no codegen; reconcile the 34 cosmetic code strings to equal their JSON once (additive to code; zero LLM impact), then a contract test asserts `handler.Metadata.Description == row.sprk_description` forever after. Smaller machinery, but the code↔JSON sync stays a hand-edit that the test *guards* rather than *generates*.
3. **The seed JSON IS the authored source** — no separate generation.

**Parity enforcement (single reused rule — §11):**
- Extend `OpenAiFunctionSchemaValidator` (`:70`) with a pure `FindDescriptionParityError(authored, mirror)` helper (deterministic, NFR-07-safe — emits identifiers/positions only, never content).
- **Contract test (KEEP-path, ADR-038):** for every handler-paired row assert code == authored JSON; **NEGATIVE**: corrupt one mirror out-of-band → parity error asserted; author a representative `memory.*` row *through the source* and assert three-surface propagation + green.
- **Health-check (`RoutingConsumerTypeHealthCheck` `:66`):** add a description-parity dimension — for each active tool row with a registered handler, live `sprk_description` vs handler Metadata (= authored) → **Unhealthy** on drift (hard fail, per task).

**Deferred correctly:** fully seeding a live `memory.write` row is **premature at 020** — the handler is built at task 057, and seeding a row-without-handler now would trip the *existing* bijection health check (`HandlersWithoutToolRows`/`ToolRowsWithoutHandlers`). The `memory.*` proof at 020 is therefore at the **contract-test level** (author through the source, assert generation/validation), with live seeding landing in 057. Flagged so 057 authors *through* this source.

**Mechanical caveats for whoever automates:** 4 handlers embed `//` comments *inside* the concatenated Description literal (`DataverseCreateRecordHandler`, `DataverseReadQueryHandler`, `EmailDraftToolHandler`, `SendWorkspaceArtifactHandler`) — a naive extractor truncates them. `GenericAnalysisHandler` has no row — decide whether the generator/validator should require one (recommend: exempt handler-only tools with no `sprk_analysistool` row).

---

## 4. Decisions requested (🔔 per CLAUDE.md §6.5)

- **ADR/step in question**: FR-A-01 / POML steps 2c + 3 (PRESCRIPTIVE mode).
- **Specific defect**: the prescribed third surface `infra/dataverse/inputschemas/` is not a description twin — it is `sprk_analysisaction.sprk_inputschema` (input schema, different entity, already single-sourced). The genuine third twin is `infra/dataverse/sprk_analysistool-*-row.json` (r1 UAT note g-p3-uat-round4-findings.md:274).
- **Proposed path**: **A (project-scoped correction)** — retarget the third surface to `sprk_analysistool-*-row.json`; adopt seed-JSON as the authored source (evidence-forced by zero-behavior-change); leave the input-schema mirror pipeline as its own already-working single-source.
- **Rationale**: census proves 0 semantic drift and JSON-as-source is a runtime no-op; ADR-039 keeps catalog text as DATA; matches the existing seed pipeline and how 032/042/057/Compose will author.
- **Open questions for the operator**:
  1. **Source direction** — ratify **seed-JSON as authored source** (recommended) vs code-as-source (rejected: drops live guidance).
  2. **Code-parity mechanism** — **Option B (codegen, literal to step 2)** vs **Option C (validate-only, minimal/§11)**. Recommend **C** unless you want the generator artifact for downstream authoring ergonomics.
  3. **inputschemas scope** — confirm it is **OUT** of this description hoist (recommended), OR expand FR-A-01 to also single-source the Action input-schema mirror (a separate, larger hoist over `sprk_analysisaction`).
  4. **Fourth surface** — confirm `sprk_playbookconsumer.sprk_tooldescription` (Binding intent for Action capabilities) is **OUT** for 020, OR in-scope (adds a fourth generator/validator target).
- **Alternative considered (rejected)**: implement steps 2c/3 literally over `inputschemas/` — rejected: enforces parity on the wrong surface, leaves the real code↔JSON drift unaddressed, and risks the working input-schema pipeline.

---

## 5. Evidence index
- Projection source: `ToolHandlerToAIFunctionAdapter.cs:359-361`; discovery-only Metadata: `Api/Ai/HandlerEndpoints.cs:147,199`; `IToolHandler.cs:154`.
- Genuine triple-twin: `.../ai-architecture-redesign-r1/notes/g-p3-uat-round4-findings.md:274-280`.
- inputschemas = different entity: `infra/dataverse/inputschemas/create-task-v1.input.schema.json:2-6`; `tests/integration/contract/Catalog/CatalogInputSchemaContractTests.cs`.
- Seed pipeline: `scripts/Seed-TypedHandlers.ps1`; row files `infra/dataverse/sprk_analysistool-*-row.json` (36).
- Validator seam confirmed: `notes/discovery-obligations.md §2` (task 004); anchors `OpenAiFunctionSchemaValidator.cs:70`, `RoutingConsumerTypeHealthCheck.cs:66`.
- Census: 36 pairs, 0 semantic (full table in the task-execution transcript).
