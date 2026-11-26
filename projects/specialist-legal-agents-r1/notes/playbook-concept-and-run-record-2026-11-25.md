# "Playbook" today: what is frozen, what is live, and what this program needs (2026-11-25)

> **Why:** the owner asked, before choosing where action runs are recorded: "while the node-graph engine is frozen, does this mean we are not using any concept of playbook? … is there another structure of playbook that we are building?"
> **Evidence (origin/master, read 2026-11-25):**
> - `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` §4.2 and §6 (the canonical AI architecture);
> - `projects/spaarke-ontology-platform-r1/notes/ontology-component-model.md` line 130 (D-209) and §4.11;
> - the `sprk_playbookrun` / `sprk_playbooknoderun` `Entity.xml`;
> - `notes/run-path-review-2026-11-25.md` (Fable review).

## The short answer

**Only the execution engine is frozen. The playbook concept is alive and central to this program.**

The canonical AI architecture says so directly (§4.2): "'playbook' survives as product language for a system-defined composite Action". The rule that replaced the engine is "**control flow is system-owned; behavior (prompts/scopes) is maker-editable data**" (§4.2.1). The engine failed because it declared control flow in JSON (ontology §4.11), not because the idea of a playbook was wrong.

## Four meanings of "playbook" on master

| # | Meaning | Status | Where |
|---|---|---|---|
| 1 | **The concept** (ontology D-209, owner 2026-11-06): "the artifact that describes how a legal procedure or protocol is carried out": positions, steps, model wording, fallbacks, escalation. It comes in three formats: | **Live, adopted.** This is what an attorney's method *is* | Ontology component model line 130 |
| | - the **JPS playbook** (`sprk_analysisplaybook`); | | |
| | - the **legal playbook** (Markdown, read by Copilot/Cowork skills); | | |
| | - the **contract-review playbook** (Microsoft's four-column format for Word and Legal for Cowork). | | |
| 2 | **The node-graph engine**: `PlaybookOrchestrationService` plus 33 node executors, running `sprk_analysisplaybook` + `sprk_playbooknode` graphs | **Frozen** (ADR-039). Kept only for Insights, the analysis workspace and scheduled playbooks, and retired by attrition | Canonical doc §4.2 |
| 3 | **"Playbook" as a composite capability**, rebuilt: a **`coded` workflow** (`ICodedWorkflow`, a registered C# class) whose steps read their prompts from child **Action** rows. Single-step work is a **`prompted` Action** | **Live, and the sanctioned shape for new composites** | Canonical doc §4.2; Daily Briefing is the first instance |
| 4 | **Legacy schema names** that now mean something else: | Names kept, meaning changed | Canonical doc §6.2 |
| | - `sprk_playbookconsumer` is the **Binding**, the live invocation unit: tool description, risk and confirmation gate, disposition, model override, events; | | |
| | - `sprk_playbookrun` / `-noderun` are the engine's run tables (0 rows; the engine keeps runs in memory). | | |

So there *is* a new playbook structure. It is split by concern instead of living in one JSON graph:

| Concern | Old (one JSON graph) | New |
|---|---|---|
| What the method says (positions, standards, fallbacks, escalation) | Node prompts | **Playbook content.** The firm master is an Internal Standard Procedure with Procedure Versions (D-175), exported to the legal and contract-review formats. The attorney's layers are the profile (§3.14) |
| How it runs (steps, order, branching) | `sprk_playbooknode` graph | **Code**: a prompted Action for one step, a coded workflow for several |
| What each step asks the model | Node config | **Action rows** (`sprk_analysisaction`: JPS prompt, output schema, input schema, model tier). Maker-editable |
| How it is invoked and gated | Playbook consumer rows | **Binding rows** (`sprk_playbookconsumer`): tool description, `sprk_risk` gate, disposition |
| What ran | `sprk_playbookrun` (never written) | **Nothing durable today.** That is the gap |

## What this program needs, mapped

| Need | Built from |
|---|---|
| A specialist's **method** (patent drafting standards, review checklist, positions) | Playbook content (meaning 1): the firm master as a Procedure; the archetype/firm/attorney/matter layers in the agent profile. Profile positions are keyed by the playbook's topic ids |
| A **function** (review an NDA; draft claims) | A prompted Action, invoked through a Binding |
| A **multi-step function** (patent application: outline → claims → description → consistency check) | A coded workflow, with each step's prompt in an Action row. This is the "playbook" of product language |
| **One gate and closed catalogue** | The Binding (`sprk_risk`) and ADR-039 catalogues. A session-less MCP facade calls the same gate explicitly |
| **Chaining** (associate draft → partner review) | Typed outputs passed as declared Action inputs, plus a **parent-run link on the run record**. `PriorOutputReference` is documentation only (see the run-path review) |
| **Durable attribution** (which profile version ran, who, which gate decision, parent run) | **The missing piece**, below |

## The run record, with this framing

The run record is the one place where the new structure has nothing. The existing tables:
- `sprk_playbookrun` already holds a run's status, start and end times, input and output JSON, error and `sprk_triggeredby` (the user). Only `sprk_playbookid` stops an Action or coded-workflow run from using it, because that column is required.
- `sprk_playbooknoderun` holds per-step status, duration, tokens in/out and validation warnings. It requires `sprk_playbooknodeid` and `sprk_playbookrunid`.

| Option | For | Against |
|---|---|---|
| **1. New capability-run table** | Clean name; no engine baggage | It overlaps a table that already has the right columns. §11 asks "can an existing component be extended?", and here it can. It also means two run tables if mcp-r2 still persists engine runs |
| **2. Extend `sprk_playbookrun` as the one AI-run record** | Follows the Binding precedent (`sprk_playbookconsumer` kept its name and changed meaning). One table answers "what AI ran" for engine, Action and coded runs. It is already in both apps' navigation. mcp-r2's task 005 columns land in the same design | The `sprk_playbookid` name reads oddly on an Action run; relabel the display name ("AI Run") and document it |
| **3. Session ledger + Decision Record** | No schema change | The ledger is GDPR-erasable (ADR-040), so attribution is not durable. The Decision Record holds decisions, not runs |

**Recommendation: option 2.**

Change `sprk_playbookrun` as follows:
- **Relax:** make `sprk_playbookid` optional. Relaxing a required level is non-breaking, and there are 0 rows.
- **Add these lookups:**
  - Action (`sprk_analysisaction`);
  - Binding (`sprk_playbookconsumer`);
  - profile version (`sprk_agentprofileversion`, Phase 1);
  - parent run (self).
- **Add these columns:**
  - a run kind (`engine` / `prompted` / `coded`);
  - a gate-decision reference;
  - mcp-r2 task 005's correlation/idempotency id, error code, regarding and summary.

Use `sprk_playbooknoderun` for coded-workflow steps: make `sprk_playbooknodeid` optional and add an Action lookup per step.

Write runs at one seam, the `PublicContracts` facade and coded-workflow runner, so every Action-path run is recorded. mcp-r2 FR-13 writes the same table for engine runs.

This revises D-A18 (7). It is still "lookups on `sprk_playbookrun`", but the table becomes the general AI-run record, not the engine's.

## Design corrections this implies (design.md, owner's document)

- **§3.14 "Other existing tables"** says that `sprk_analysisplaybook` / `sprk_playbooknode` are "the functions a profile points at, by code". That is wrong for new work: functions are **Actions and Bindings**, by `sprk_actioncode` and binding code. `sprk_analysisplaybook` stays only as the JPS format of meaning 1.
- **§3.15 "Typed output"** cites `PromptSchema.PriorOutputReference`. Chaining is done with declared Action inputs plus the parent-run link.
- **§3.14 / §12, D-A18 (7):** revised as above, once the owner picks.
