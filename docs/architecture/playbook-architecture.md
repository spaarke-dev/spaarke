# Playbook Architecture

> **Last Updated**: 2026-11-25
> **Purpose**: The canonical entry point for what "playbook" means in Spaarke today, how playbook-style capabilities run, and where each concern lives. Read this before concluding that "playbooks are not used".

---

## Overview

**Playbooks are used. Only the node-graph playbook engine is frozen.**

"Playbook" is overloaded. The frozen thing is one execution mechanism (`PlaybookOrchestrationService` and its node executors, frozen by ADR-039). The playbook *concept* and the *composite capabilities* that carry it are the live model: agreement and NDA review, chat Text path, document profiling, invoice AI and email triage run as Actions (invoked through a Binding); Daily Briefing runs as a coded workflow; only Insights (matter health), the analysis workspace and scheduled playbooks still run on the frozen engine.

### The four meanings of "playbook"

| Meaning | What it is | Status |
|---|---|---|
| **Concept** (ontology D-209) | The artifact describing how a legal procedure or protocol is carried out: positions, steps, model wording, fallbacks, escalation. One concept, three formats: JPS playbook (`sprk_analysisplaybook`, JSON, executed by the platform), legal playbook (human-readable Markdown read by Copilot/Cowork skills), contract-review playbook (Microsoft's Topic / Instructions / Example Text / Compromise Positions format). Not a Policy: a policy is a predicate. | Live |
| **Node-graph engine** | `PlaybookOrchestrationService` plus 33 node executors over `sprk_playbooknode` rows | **Frozen** (ADR-039). Kept only for existing Insights pipelines, analysis workspace and scheduled playbooks; retired by attrition |
| **Composite capability** | A system-defined composite Action: a `prompted` Action (`ActionRunner` + `PromptSchemaRenderer`) or a `coded` workflow (`ICodedWorkflow`), invoked through a Binding | Live; the model for all new work |
| **Legacy schema names** | `sprk_analysisplaybook`, `sprk_playbookconsumer` (the Binding, renamed in meaning only), `sprk_playbookrun` (becoming the AI Run) | Live; names kept, meanings changed |

---

## How a playbook runs now

| Concern | Component |
|---|---|
| Content (positions, wording, fallbacks) | Concept D-209 formats; for execution, the JPS prompt and scopes on the Action row (`sprk_analysisaction`) |
| Steps / control flow | Code. `prompted` = one structured-output call (`src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/`). `coded` = `ICodedWorkflow` (`src/server/api/Sprk.Bff.Api/Services/Ai/ICodedWorkflow.cs`), first instance Daily Briefing (`.../Services/Ai/Narrators/DailyBriefingNarrator.cs`) |
| Step prompts | Maker-editable data: child Action rows read by the workflow |
| Invocation and gate | Binding (`sprk_playbookconsumer`): tool description, disposition, `sprk_risk` gate, events, surfaces. The only routing surface |
| Run record | `sprk_playbookrun` (AI Run) with per-step rows in `sprk_playbooknoderun`. **Decided 2026-11-25; schema not yet changed** |

## Data flow

1. A caller (chip, ribbon, event rule, or the single Text-path agent turn) invokes a Binding.
2. The Binding resolves to an Action (`kind` = `prompted` or `coded`) and passes the risk gate.
3. `prompted`: render JPS and make one structured-output call. `coded`: the workflow runs its steps, each reading its prompt from an Action row.
4. The output router and the session ledger (ADR-040) carry the result to the surface.
5. An AI Run row is written on `sprk_playbookrun` with run kind, Action, Binding, caller and parent run. Until the schema extension lands, only engine runs have this shape and they are held in memory.

## Integration Points

- Canonical AI architecture: [`SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) §4.2 (execution shapes), §6 (Action and Binding), §6.6 (AI-run record).
- Document profiling: [`DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`](DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md).
- Insights (still on the frozen engine): [`INSIGHTS-ENGINE-ARCHITECTURE.md`](INSIGHTS-ENGINE-ARCHITECTURE.md).
- Session ledger (erasable, not the durable record): ADR-040.

## Design Decisions

| Decision | Source |
|---|---|
| Freeze the node-graph engine; control flow is code, behaviour is data | ADR-039 and its 2026-11-25 amendment |
| ADR-013 playbook-verb canon replaced by the Action/Binding model | ADR-013, 2026-07-05 amendment |
| Engine-steering default rescinded; section-name streaming contract kept for any composite | ADR-037, 2026-07-05 amendment |
| One execution spine for AI capabilities | ADR-043 |
| Playbook is one concept in three formats and is not a Policy | Ontology D-209, `projects/spaarke-ontology-platform-r1/notes/ontology-component-model.md` |
| `sprk_playbookrun` becomes the one AI-run record for engine, prompted and coded runs; display name "AI Run" | Owner decision 2026-11-25 (precedent: Binding kept its table name, changed meaning; CLAUDE.md §11 prefers extension) |

Why the engine is frozen: document profiling #919 (every Outlook-saved document ended Failed; direct-Action paths were immune), the defect sweep (`projects/spaarke-ontology-platform-r1/notes/playbook-engine-defect-sweep.md`: skip not propagated, `continueonerror`/retry/timeout not enforced, a Designer save can destroy a deployed playbook; about 1 of 27 live playbooks likely to finish usefully), and no model-tier resolver on the node path.

Detailed analyses sit on branch `work/specialist-legal-agents-r1`: `projects/specialist-legal-agents-r1/notes/run-path-review-2026-11-25.md` and `.../playbook-concept-and-run-record-2026-11-25.md`.

## Constraints

- **MUST NOT** build any new capability on the node-graph engine.
- **MUST** build new composites as coded workflows (or a prompted Action when one call suffices), invoked through a Binding.
- **MUST** keep control flow in code and behaviour (prompts, scopes, bindings) in Action/Binding data.
- **MUST** record every AI run (engine, prompted, coded) on `sprk_playbookrun`; **MUST NOT** add a second AI-run store.
- **MUST NOT** read "frozen engine" as "no playbooks".

## Related

### See instead (engine and authoring detail)

| Topic | Doc |
|---|---|
| Frozen engine runtime semantics (dispatch shapes, mode detection, action lookup, pitfalls) | [`ai-architecture-playbook-runtime.md`](ai-architecture-playbook-runtime.md) |
| Config-bag boundary (Action vs playbook header vs node vs N:N scopes) | [`ai-architecture-actions-nodes-scopes.md`](ai-architecture-actions-nodes-scopes.md) |
| Consumer (Binding) dispatch and routing | [`ai-architecture-playbook-consumer-routing.md`](ai-architecture-playbook-consumer-routing.md) |
| Deploy procedure | [`../guides/ai-guide-playbook-deploy-recipe.md`](../guides/ai-guide-playbook-deploy-recipe.md) |
| JPS schema | [`../guides/JPS-AUTHORING-GUIDE.md`](../guides/JPS-AUTHORING-GUIDE.md) |
| Maker recipe | [`../guides/PLAYBOOK-AUTHOR-GUIDE.md`](../guides/PLAYBOOK-AUTHOR-GUIDE.md) |

Pre-2026-06 content of this file is in git history at commit `1e8c95b8e`.
