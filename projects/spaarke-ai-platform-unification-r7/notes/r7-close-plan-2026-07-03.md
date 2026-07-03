# R7 Close Plan — Chat Summarize + Playbook-Manifest Composition Model

> **Created**: 2026-07-03 (extension of Wave 12.3 canonical plan after operator design conversation)
> **Supersedes for scope**: [`wave12-3-assistant-summarize-canonical-plan.md`](wave12-3-assistant-summarize-canonical-plan.md) (chat-summarize plan) — extended with Persona / Skills / Knowledge composition
> **Owner**: R7 close — everything remaining before R7 ships
> **Audience**: Coding agents executing the phases below. Read this doc first; then read the referenced files.

---

## 1. What we are building — the composition model in one paragraph

**Playbook is a maker-visible manifest** of one or more **Nodes**. Each Node references one **Action** (prompt + strict output schema), plus zero or more **Skills** (reusable instruction fragments on the Node), one optional **Persona** (voice, on the Action), and zero or more **Knowledge sources** (retrieval context on the Node). **Consumer services** (code) execute Playbooks by ID + Node index — the code has FKs hardcoded; runtime is code, not a graph interpreter. **Level A (composition) is compile-time** (dev writes consumer service to specific FKs). **Level B (content) is runtime** (Dataverse row fetch for prompt text, persona voice, knowledge, etc.). A **CI drift check** validates that consumer service's declared FKs match its Playbook's Node FKs.

---

## 2. Design decisions (LOCKED — 2026-07-03)

Reference conversation ended 2026-07-03. All decisions confirmed by operator:

| # | Decision | Rationale |
|---|---|---|
| D-01 | Playbook name retained | User-facing mental model; graph baggage is our internal problem, not the user's |
| D-02 | Node concept preserved as composition anchor | Already exists as `sprk_playbooknode`; wraps Action + Skills + Knowledge |
| D-03 | Linear consumer = one-node Playbook | Multi-step = multi-node Playbook |
| D-04 | Multi-step orchestration = explicit code sequences in consumer service | Not runtime graph walking. `if` statements, sequential await, output-to-input variable assignment |
| D-05 | **Skill = instruction fragment** (no goal, no output shape). Reusable. On Node (1:N — Node holds Skill references). | Operator explicit: skills are already 1:N to Node |
| D-06 | **Action = complete prompt contract**. Goal + composed Skills + parameters + output schema + Tools + Knowledge references + Persona. What actually runs. | Per operator's diagram |
| D-07 | **Persona** = reusable voice/character. Dataverse entity `sprk_aipersona` (exists). FK on Action | Operator: `sprk_aipersona` exists; Action-level is fine |
| D-08 | **Output schema = ONE source of truth = `sprk_analysisaction.sprk_outputschemajson` field** | Azure OpenAI Structured Outputs binds by schema, not by prompt. JPS output fields duplicate + drift risk (D-1 tech debt evidence). |
| D-09 | Prompt output-fields section rendered from schema at request time (via PromptSchemaRenderer) | Single source of truth; LLM still sees inline guidance |
| D-10 | **Composition binding = build-time (hardcoded FKs in consumer service code)** | Level A. Composition is dev activity per operator |
| D-11 | **Content resolution = runtime (Dataverse row fetch by FK)** | Level B. Content is maker activity — prompt text, persona voice, skill instructions, knowledge |
| D-12 | Playbook Builder UI = deferred | No users on it; will retrofit after everything functional |
| D-13 | Slash commands (`/summarize`) removed entirely | Operator's Wave 12.3 Decision 1 |
| D-14 | Doc Upload's PlaybookId in client contract = retire NOW (not R8) | No external clients; scope creep argument invalid |
| D-15 | Two-step ambiguous-intent flow (LLM picks candidate Actions → user picks → executes) retained as future design (NOT R7) | Real-estate vs NDA example — future consumer set |

---

## 3. Vocabulary — what each term means going forward

| Term | Dataverse entity | Purpose | Editable by |
|---|---|---|---|
| **Playbook** | `sprk_analysisplaybook` | Composition manifest — documents which Nodes/Actions/Skills/Personas/Knowledge a consumer uses | Dev (composition); Maker (limited via Builder later) |
| **Node** | `sprk_playbooknode` | Composition anchor — one node = one Action invocation with its Skills + Knowledge context | Dev |
| **Action** | `sprk_analysisaction` | Complete prompt contract — SystemPrompt + strict OutputSchema + Temperature + Persona FK + Model deployment | Maker (prompt text, output schema) |
| **Skill** | `sprk_aiskill` (verify exists) | Reusable instruction fragment (e.g., "extract obligations") | Maker (instruction text) |
| **Persona** | `sprk_aipersona` (exists) | Reusable voice/character | Maker (persona text) |
| **Knowledge source** | Existing: RAG index or Dataverse table reference | Retrieval context (e.g., "Golden NDA Index") | Owner (source content) |
| **Consumer service** | Code class in `Services/Ai/LinearConsumers/` (or similar for multi-step) | Executes a Playbook. FKs hardcoded. Level A binding. | Dev only |
| **Consumer type** | Compile-time constant in `ConsumerTypes.*` | Stable string identifier ("review-nda", "chat-summarize") — used for routing + telemetry | Dev only |

---

## 4. Scope for R7 close — phased execution

### Phase 12.3a — Chat Summarize client + PlaybookId retire (FUNCTIONAL FIX)

**Goal**: Assistant pane summarize works end-to-end. Doc Upload client uses consumer-typed endpoint.

Tasks:
1. Remove `/summarize` slash command handler from `SprkChat` (Decision 1)
2. Rewire `SprkChatMessageRenderer` intent detection: explicit summarize intent → POST `/api/ai/chat/sessions/{id}/summarize` (skip `PlaybookCandidateSelector`)
3. Build schema-driven Workspace Summary tab — reads Action's `sprk_outputschemajson` top-level properties → renders skeleton sections → populates from SSE `result` chunk JSON
4. Consume SSE via shared `useLinearRunProgress` hook OR retain existing chat consumer if simpler
5. **Retire Doc Upload's PlaybookId**: client sends consumer-typed URL (`POST /api/ai/documents/{docId}/profile`), server dispatches by URL path (not by `LinearConsumers__PlaybookIds__*` reverse-lookup). Delete `LinearConsumersOptions.PlaybookIds` + last App Settings. Delete `GetConsumerTypeForPlaybookId`.

Done criteria:
- Operator uploads file + types "summarize this" → gets structured summary in Summary tab
- Operator uploads file to Doc Upload wizard → profile fields populate
- Zero `LinearConsumers__*` App Settings on `spaarke-bff-dev`
- Zero `PlaybookId` references in Doc Upload client code

Estimate: **4-5 hrs**

### Phase 12.3b — Output schema single-source-of-truth cleanup

**Goal**: Output schema exists in ONE place (`sprk_outputschemajson`). Prompt renders Expected Output section from it at request time.

Tasks:
1. Audit 6 Linear-target Actions (Doc Profiler `bb356968`, File Summary `ddaa441e`, Chat Summarize `eeb05bfd`, Matter Prefill `89cc641a`, Project Prefill `1e838114`, AiSummary if distinct)
2. For each: strip `output.fields` + `examples[N].output` from `sprk_systemprompt` (JPS). Retain role + task + constraints + example inputs.
3. Extend `PromptSchemaRenderer`: read Action's `sprk_outputschemajson`, render `## Expected Output` section as `property → type → description` from schema, insert at standard position
4. Smoke test each consumer to confirm output quality doesn't regress

Done criteria:
- 6 Actions have JPS free of duplicate output schema info
- PromptSchemaRenderer emits `## Expected Output` section (verify via server log or debug endpoint)
- All 5 Linear consumers produce equivalent output post-cleanup

Estimate: **1-2 hrs**

### Phase 12.4 — Persona introduction

**Goal**: Persona is a first-class composition primitive on Action.

Tasks:
1. Verify `sprk_aipersona` entity exists; if not, create it (`sprk_name`, `sprk_prompttext`)
2. Add `sprk_personaid` LOOKUP column to `sprk_analysisaction` → `sprk_aipersona`
3. Build `IPersonaLibrary` service — `GetAsync(personaId)` → returns persona row + 5-min cache
4. Extend `PromptSchemaRenderer`: if Action has Persona FK, prepend Persona.PromptText to composed prompt (before role/task/constraints from Action's own SystemPrompt)
5. Seed a small library of personas: "Compliance Reviewer", "Matter Analyst", "General Counsel Assistant", "Drafter (Partner-level)"
6. Populate Persona FK on the 6 Linear Actions where appropriate

Done criteria:
- New Persona rows exist + populated
- ActionResolver returns Action with Persona
- Prompt renders with persona voice prepended
- Existing consumer output quality maintained or improved

Estimate: **4-6 hrs**

### Phase 12.5 — Skills formalization on Node

**Goal**: Skills are first-class composition primitives on Node. Consumer services compose Skills into Action prompts at request time.

Tasks:
1. Verify current state: is `sprk_aiskill` an entity? Is the 1:N relationship to `sprk_playbooknode` already schema-defined? Operator said it's already set up — confirm
2. If entity missing: create `sprk_aiskill` (name + instruction fragment text)
3. If relationship missing: add junction or FK
4. Build `ISkillLibrary` service — `GetForNodeAsync(nodeId)` → returns ordered Skills + 5-min cache
5. Extend `PromptSchemaRenderer` (or a new SkillComposer): read Node's Skills, render `## Skills / Capabilities` section into prompt
6. Seed a few reusable skills: "Extract obligation dates", "Flag non-standard clauses", "Assess counterparty risk"

Done criteria:
- Skills entity + relationship exists
- Node → Skills lookup works
- Skills render into prompt in composed order

Estimate: **4-6 hrs**

### Phase 12.6 — Knowledge references on Node

**Goal**: Knowledge sources are first-class references on Node. Consumer service retrieves knowledge context and injects into prompt.

Tasks:
1. Determine Knowledge source primitive shape — likely a Node-level JSON array of Azure Search index names or Dataverse query configs. Or a `sprk_knowledgesource` entity if we want richer maker editing
2. Build `IKnowledgeRetriever` — reads Node's knowledge references, calls `IRagService` (or Dataverse query), returns concatenated retrieval context
3. Extend consumer services + PromptSchemaRenderer to inject `## Reference Context` section from retriever output

Done criteria:
- One test consumer (proposed: NDA Review) uses a Knowledge source successfully
- Existing consumers unaffected

Estimate: **6-8 hrs**

### Phase 12.7 — Retrofit 5 Linear consumers to Playbook-manifest model

**Goal**: All Linear consumers use the same composition pattern. CI drift check catches divergence.

Tasks:
1. For each of 5 Linear consumers (Doc Profile, File Summary, Chat Summarize, Matter Prefill, Project Prefill):
   - Update Playbook row to have exactly ONE Node with FKs correctly populated (Action, Persona if used, Skills if used, Knowledge if used)
   - Update consumer service class to declare compile-time constants matching Playbook Node FKs: `PlaybookId`, `NodeId`, `ActionId`, `PersonaId`, etc.
   - Consumer service reads primitives by FK at runtime — no lookups by consumer type; direct by FK
2. Build CI drift check (`tests/architecture/PlaybookManifestDriftTests.cs`):
   - For each Consumer service class: extract declared FK constants
   - For each Playbook row: extract Node FKs
   - Validate they match; fail build if not
3. Retire `IActionResolver` config-driven lookup (composition is now code)
4. Retire `ResolveActionAsync` on `IConsumerRoutingService` (may keep for future needs but not used by Linear consumers)
5. Retire `sprk_playbookconsumer.sprk_action` column (no longer needed) OR leave populated for maker read-only visibility

Done criteria:
- All 5 consumer services have compile-time FK constants
- CI drift check passes locally + in CI
- No runtime routing table lookups in Linear path
- 5 consumers still produce correct output

Estimate: **8-10 hrs** (largest phase)

### Phase E — Deactivate 6 migrated playbook rows

Only after Phase 12.7 smoke passes.

- Set `statecode = 1` on old playbook rows (18cf3cc8, ddaa441e, 2d660cad, fc343e9c, 44285d15, 47686eb1) if they were duplicates from the pre-manifest era. Keep the manifest playbooks active.
- Deactivate associated `sprk_playbooknode` rows

Estimate: **30 min**

### Phase G — Documentation

Tasks:
1. Rewrite `BUILD-A-NEW-LINEAR-AI-CONSUMER.md` under the manifest model — walkthrough: create Playbook + Nodes + reference primitives + write consumer service + register CI check
2. Add `BUILD-A-MULTI-STEP-AI-CONSUMER.md` — same but for multi-node cases
3. Add `SPAARKE-AI-COMPOSITION-MODEL.md` under `docs/architecture/` — reference for the primitive → composition → execution architecture
4. Update Wave 12 changelog
5. Update ADR-013 (BFF AI Architecture) with the new composition model

Estimate: **6-8 hrs**

---

## 5. What is NOT in R7 close

- Ambiguous-intent flow (LLM picks candidate Actions → user picks) — future consumer set, R8 territory
- Tool definitions on Action — future, when we introduce LLM function-calling
- Playbook Builder UI retrofit for the new model — deferred (Decision D-12)
- Compose R1 rewire — sequential to R7 close, not blocking
- SharePoint Embedded retrieval integration — R5 territory
- Widget / workspace layout changes — separate concern

---

## 6. Total R7 close estimate

| Phase | Estimate |
|---|---|
| 12.3a | 4-5 hrs |
| 12.3b | 1-2 hrs |
| 12.4 | 4-6 hrs |
| 12.5 | 4-6 hrs |
| 12.6 | 6-8 hrs |
| 12.7 | 8-10 hrs |
| Phase E | 0.5 hr |
| Phase G | 6-8 hrs |
| **Total** | **~34-46 hrs** |

Roughly one week of concentrated work.

---

## 7. Coding agent execution rules

1. **Phase order is strict**. Don't start Phase 12.5 without Phase 12.4 complete. Each phase builds on the prior.
2. **Every phase ends with a smoke test**. Operator (or dev) confirms the affected consumers still work before moving on.
3. **Every code change specifies which primitive it touches**. Never edit multiple primitives in one PR (Action + Persona + Skill = 3 PRs, not 1).
4. **FK hardcoding is intentional**. When Phase 12.7 hardcodes FKs into consumer service code, that's the DESIGN — not tech debt.
5. **CI drift check is mandatory for Phase 12.7**. Not optional. Not deferred.
6. **Consult this doc before each phase.** Design decisions D-01 through D-15 are LOCKED unless operator revises in writing.

---

## 8. Reference documents

- **Wave 12.3 canonical plan (chat-summarize specific)**: [`wave12-3-assistant-summarize-canonical-plan.md`](wave12-3-assistant-summarize-canonical-plan.md)
- **Wave 12 tech debt inventory**: [`wave12-linear-migration-tech-debt.md`](wave12-linear-migration-tech-debt.md)
- **R7 UAT + delivery summary**: [`wave12-uat-checklist-2026-07-02.md`](wave12-uat-checklist-2026-07-02.md)
- **Linear architecture (current)**: [`../../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
- **Playbook LLM output pattern (existing PromptSchemaRenderer basis)**: [`../../../docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](../../../docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md)
- **BFF constraints (binding for BFF-touching phases)**: [`../../../.claude/constraints/bff-extensions.md`](../../../.claude/constraints/bff-extensions.md)

---

*End of R7 close plan. Reads bottom-up: primitives → composition → execution. Decisions locked 2026-07-03. Coding agents: start Phase 12.3a with the client audit; every PR references the phase + subtask.*
