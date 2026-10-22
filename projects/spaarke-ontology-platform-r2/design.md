# Spaarke Ontology Platform R2: Configurable Decision Actions and an AI-Configured Decision Wizard

> **Status**: Draft design, rev 1 (2026-10-22). This is the input for `/design-to-spec`. §9 lists the owner decisions it still needs.
> **Origin**: R1 decision D-183 (`projects/spaarke-ontology-platform-r1/notes/decisions.md`), from owner UAT round 1 of the Console decision wizard.
> **Research**: [`notes/research-configurable-actions-and-ai-authoring.md`](notes/research-configurable-actions-and-ai-authoring.md) covers external patterns, AI-authoring safety and sources.
> **Baseline**: master `f5bf481185`. Code facts below were read from that commit (inventory 2026-10-22).

---

## 1. Problem and goals

During R1 UAT the owner asked whether the decision wizard's action steps are configurable, so that they "can be put together as a configuration". The owner also noted that "we have a standard set of action steps that [are] often reused". Separately, users ask that **an AI agent can, at some point, define or configure a decision wizard**.

Today the wizard is half data-driven:
- A policy version's plan picks **which** actions appear and in what order. That part is data.
- **What** each action is (its fields, the record it writes and its side effects) is a closed C# catalog. Adding or varying one is a code change, a BFF deploy and a new policy version.

**Goals**
1. **G1. Presets as data.** An administrator can define a reusable action, such as "Assign Task", "Create budget" or "Ask the outside firm for an estimate". The definition gives its label, effect text, target record, fixed defaults and the fields to ask. No code change or BFF deploy is needed, as long as the action uses an existing executor.
2. **G2. Plans compose presets.** A rule's decision plan names presets, so a common set ("past due: complete / reschedule / reassign") is defined once and reused.
3. **G3. An AI agent can draft a wizard configuration** (presets and plans) from a plain-language request or from observed decisions. A human reviews, simulates and publishes it. The agent never publishes.
4. **G4. Nothing gets less safe.** Every guarantee R1 has stays:
   - caller-identity writes;
   - preflight before any write;
   - one Decision Record per review;
   - immutable published versions (D-14 / FR-49);
   - fail-closed plan resolution;
   - the gate tiers.

**Non-goals (R2)**
- An arbitrary workflow engine: no branching, loops or scripted steps.
- Letting configuration create new kinds of side effect.
- Letting the AI act on records. The agent configures; people decide.
- Changing the Signal evaluator or rule bodies (R1 territory). The authorable-column allow-list for rule bodies stays in code.

## 2. What exists (R1 baseline)

| Piece | Where | Data or code |
|---|---|---|
| Catalog of 13 action codes, with lane, Next-step flag, class, ResolvesSignal, gate tier, parameters and mutual excludes | `Services/Signals/Actions/DecisionActionCatalog.cs:133-280` | Code (closed) |
| One executor per code (`IDecisionActionExecutor`: Preflight + Execute) | `Actions/DecisionActionExecutors.cs` | Code |
| Generic bases already present: `ChildCreateExecutorBase` (create a row under the core record, with regarding block, privilege and AppendTo checks) and `MessageExecutorBase` (send mail) | `:1015`, `:820` | Code |
| Plan per policy version: `sprk_policyversion.sprk_decisionplan` = `{"actions":[codes],"nextSteps":[codes]}`, resolved fail-closed (`plan_missing`, `unknown_action`, `action_not_allowed`, …) | `Actions/DecisionPlanService.cs:93-162` | Data (codes only) |
| Context-driven plan reductions (`WithoutMarkComplete`), applied in both the plan route and the commit route | `DecisionPlanService.cs:22` | Code |
| Plan route `GET /signals/{id}/decision-plan`, which returns action and parameter DTOs. The client renders them generically, with no code-specific branches | `Api/Signals/DecisionPlanEndpoints.cs` | Code |
| Authoring: the admin RuleWizard ActionsStep picks codes per lane from `GET /ontology/admin/authoring/catalog`. Save and publish require a dry-run proof and run as the caller (Ontology Administrator role) | `OntologyAdminAuthoringService.cs` | Data plus code |
| Commit: the review ledger, preflight of all steps, then ordered writes (actions → Next steps → messages last), one Decision Record with `sprk_steps` and `sprk_followons`, then Signal closure. Retry resumes completed steps by key | `DecisionCommitService.cs` | Code |
| AI involvement | None. Drafts are templated (D-53); the gate tier is a pure function | — |

**Cost today of adding one action**: a catalog entry, an executor class, a DI registration, tests and census rows, a BFF deploy, then a new policy version that names the code. No client work is needed unless there is a new parameter kind.

## 3. Evidence from dev (2026-10-22)

There are six plans on dev, using 10 of the 13 codes:
- `mark-complete` and `reschedule` each appear in 3 plans; `reassign` in 2.
- The triple `mark-complete, reschedule, reassign` appears verbatim in two policies (open task past due; To Do past due).
- No plan uses a Next step yet.

R1 UAT round 1 then produced four action changes in one day (tasks 159–161):
- Assign Task;
- Create budget;
- a composer-based inquiry;
- link-by-recipient.

Each is a code change, but each is a **variation of an existing executor**: create a row of type X with fields Y, or send a message with link Z. That is the case for presets.

## 4. Proposed model: three layers

```
 Executors (code, closed, few)      Presets (data, admin- or AI-authored, versioned)      Plans (data, per policy version)
 ───────────────────────────        ────────────────────────────────────────────────      ───────────────────────────────
 create-record                ◄──── "Assign Task": create-record on sprk_event,            POL-MATTER-NOBUDGET v3:
 update-subject                      eventtype=Task (fixed), ask assignee*, description*,    actions:   [assign-task@2]
 send-message                        dueDate; effect text; gate Tier3; resolves             nextSteps: [add-todo@1, assign-work@1]
 revise-budget (domain)       ◄──── "Create budget": create-record on sprk_budget …
 budget-inquiry (domain)
```

### 4.1 Executors stay code: a small closed set

There are two kinds.
- **Generic executors** are a closed set: `create-record`, `update-subject` and `send-message`. Each one declares in code:
  - its parameter schema;
  - its side-effect class (§7);
  - its required privilege;
  - an allow-list of `(table → writable columns)` it may touch.
- **Domain executors** stay where a generic shape cannot express the invariant. Examples: `revise-budget` (writer split, `sprk_totalbudget`, revision row), the budget inquiry (service request plus reply correlation), and the Do-lane status writers.

`ChildCreateExecutorBase` and `MessageExecutorBase` already exist. R2 generalises them so that column names come from the preset, validated against the allow-list, instead of from C#.

### 4.2 Presets are data

A preset row is proposed as a new table, `sprk_decisionactionpreset`, with an immutable versions child or a version column (to decide in spec). Its fields:
- code, label, effect text, lane, Next-step flag, record class;
- executor code;
- target table (from the executor's allow-list);
- **fixed values**, e.g. `sprk_eventtype_ref = Task`, `statuscode = Open`;
- **fields to ask**: column, label, kind, required, hint and lookup filter, each within the executor's schema;
- excludes;
- gate tier (≥ the executor's floor);
- publish state, version, and author type (human or agent).

**Data may only narrow code**:
- a preset cannot name a column, table, side-effect class or privilege that its executor does not already allow;
- it cannot lower the gate tier below the executor's floor.

The 13 R1 codes become **built-in presets**, shipped as rows with a `managed` flag, so existing plans keep resolving (§8).

### 4.3 Plans name preset versions

The plan JSON gains a pinned reference: `{"actions":["assign-task@2"], "nextSteps":["add-todo@1"]}`. A bare code means "the version that was current when this policy version was published", and is resolved and pinned **at publish**.
- A published plan never changes meaning when a preset is edited later (D-14 carried over).
- Editing a preset creates a new preset version.
- Policies pick it up only through a new policy version, or through an explicit "adopt latest presets" publish that shows a diff.

### 4.4 One validator, three call sites

The same code validates at three points:
- **(a) Authoring time.** It also generates the AI agent's structured-output schema, with allow-listed tables, columns and codes as `enum`s.
- **(b) Publish.**
- **(c) Commit.** It re-checks the pinned preset version against the current executor allow-list.

Anything missing, inactive or invalid fails closed: the step is not offered, as with R1's `unknown_action`. Checks that strict JSON schema cannot express (patterns, ranges, lengths) live in the validator.

## 5. The AI configuration agent

### 5.1 What it does

- **"Draft me a wizard for …"**: the user describes a rule's decision in plain language, and the agent proposes a plan from existing presets, plus new preset drafts if needed.
- **"Tidy the catalog"**: the agent reads plan usage and Decision Record statistics (counts, skip rates, dismissal reasons), never raw record text, and proposes merges, defaults or a recommended-first order.
- **"Explain this wizard"**: a read-only, plain-language description of a plan, for reviewers.

### 5.2 How it is bounded

The design is **propose → validate → simulate → human publish.**

**Tools.** The agent has four tools:
- `list_executors`;
- `list_presets`;
- `propose_preset` / `propose_plan` (each writes a **Draft** only);
- `validate` and `simulate`.

There is **no publish tool**. Publishing is a Dataverse privilege that the agent's identity does not hold, so the boundary is enforced by the platform, not by the prompt.

**Structured output.** The proposal schema is generated per request from the allow-list, with codes, tables and columns as `enum`s. Strict mode guarantees only the shape; the validator decides validity.

**Plan-then-execute.** The proposal's structure (which executors, which tables) is fixed before any record data is read. Usage statistics reach the agent as numbers and enums, never as free text from records. Record text is treated as hostile.

**No external-communication tools in the agent.** Presets that send messages can be drafted, but they need an elevated publisher (§7) and keep the human gate at Record decision.

**Simulate before publish.** The draft is replayed against N historical Signals: the rendered effect text and email, the resolved target rows and the field writes are shown. Nothing is written; the existing executors' preflight runs. This matches the R1 rule "wizards preview only" and the Conditional Access report-only precedent.

**Provenance.** Each preset or plan version stores:
- author type (agent or human) and the reviewer who published it;
- a hash of the agent's inputs;
- the validator version.

Each Decision Record already stores the policy version. R2 adds the preset versions used.

**Diff review.** Publish shows the draft against the current published version. Free-text fields (effect text, email body template) are shown prominently, because draft config is a persistence vector.

### 5.3 Where it runs

The agent is a BFF-hosted call to Azure OpenAI with strict `json_schema`, through the AI `PublicContracts` facade. It does **not** use Foundry Agent Service, which does not support structured outputs. The UI is a panel in the admin RuleWizard; the Console Assistant comes later.

The agent's own principal (Entra Agent ID or Dataverse agent users) is **preview** as of 2026-09. Until it is GA, drafts are written as the requesting admin with `authorType = agent`, and publish still needs a separate privilege. This is §9 Q4.

## 6. Admin experience (sketch)

The RuleWizard gets two new parts:
- **Actions library**: presets grouped by lane, each showing its executor, target and the fields it asks. Built-in presets are read-only and can be copied.
- **Preset editor**: target table and fixed values, chosen from allow-listed pickers; fields to ask; effect text; and a live preview of the wizard step rendered with the R1 `ParameterFields`.

The ActionsStep of a policy then picks presets instead of codes, with the same role select (Not used / Action / Next step), excludes and ordering.

The AI panel offers "Draft from description". It shows the proposal as a diff with a **Simulate** button, then Save draft. Publish follows the existing dry-run and publish flow.

## 7. Guardrails

| Risk | Guardrail |
|---|---|
| Config widens capability | Executors are closed, and presets only narrow them. The allow-list is `(executor, table) → columns` in code. Owner, status and security columns are excluded unless the executor itself owns them |
| Writes to the wrong table or columns | Validated at author time (schema enums), at publish, and at commit. Writes run as the deciding user, so Dataverse privileges are a second wall |
| Template or parameter injection in email | Inert `{field}` substitution from a closed variable set, with no expressions and no re-expansion. Plain text, or HTML-encoded. Links are built only from configured base URLs plus a type token and a GUID (R1 task 160). Record text never becomes a recipient, table or field name |
| Side-effect escalation | The side-effect class belongs to the executor. In ascending order: none; internal reversible write; internal notify or irreversible; external communication. Publishing a preset above "internal reversible" needs an elevated publisher privilege, and external communication keeps the human gate at Record decision |
| Prompt injection via record data | The agent never sees raw record free text; it gets stats and enums only (plan-then-execute). Prompt Shields, where enabled, is a detector, not the boundary |
| Poisoned drafts | Drafts are inert until published. The diff highlights free text, and provenance is stamped |
| Runaway agent | Caps on proposals per session, tokens and tool calls; rate limits |
| Stale plans after preset edits | Version pinning (§4.3); adopting a new preset version requires a new policy version |

## 8. Migration and compatibility

- The 13 R1 codes ship as built-in preset version 1, with identical parameters, effects, excludes and gate tiers.
- Existing plans that hold bare codes resolve to those built-ins, so no data migration of `sprk_decisionplan` is needed.
- The R1 executors map to built-ins one-to-one. Any that fit a generic executor (`add-todo`, `create-event`, `assign-work`, R1 task 159's `assign-task`, task 161's `create-budget`) are re-expressed as generic presets only once the generic executor passes the same tests. Domain executors stay.
- Decision Records keep `sprk_actioncode` and add a preset-version reference. Old records stay readable.
- The R1 plan reductions (`WithoutMarkComplete`, task 161's create-budget substitution) become declared preset properties ("offered when …") only if a small closed set of conditions covers them. Otherwise they stay in code.

## 9. Owner decisions needed (for `/design-to-spec`)

1. **Q1. Who may author and publish presets?** Proposal: Ontology Administrator authors; a new "Decision Action Publisher" privilege publishes presets with a side-effect class above "internal reversible".
2. **Q2. First generic executors.** Proposal: `create-record` (event, To Do, work assignment, budget) and `send-message`. `update-subject` comes later.
3. **Q3. AI scope for R2.** Proposal: R2a builds presets with no AI; R2b adds the agent for "draft a plan from description" and "explain"; "tidy the catalog" comes later.
4. **Q4. Agent identity.** Run as the admin with `authorType = agent` until Entra Agent ID or Dataverse agent users are GA, or wait for GA?
5. **Q5. Where the AI panel lives.** The admin RuleWizard only, or also the Console Assistant?
6. **Q6. Version adoption.** When a preset changes, should the publish screen offer "adopt in all policies using it" (one new policy version each, listed), or leave adoption manual?

## 10. Placement and constraints (to be completed in spec)

- **BFF** (`Sprk.Bff.Api`):
  - preset store and validator under `Services/Signals/Actions/`;
  - authoring routes under the existing `/api/v1/ontology/admin` group;
  - the AI proposal service behind the AI `PublicContracts` facade (ADR-013).

  This is a hot path, so a `<hot-path-declaration>` and Placement Justification are needed (CLAUDE.md §10), and the publish-size delta is measured per task.
- **Dataverse**: the new preset table needs owner approval for schema (R1 standing rule). No plugins (ADR-002); all validation is server-side in the BFF.
- **Client**: RuleWizard (admin) and DecisionWizard (Console) in `Spaarke.UI.Components`, which has no Xrm (ADR-012).
- **R1 alignment**: R1 tasks 158–161 build on existing executors so they port to presets cleanly. R2 must not start editing R1-owned files while R1 lanes are open; it coordinates through the R1 session.

## 11. Suggested phasing

| Phase | Delivers |
|---|---|
| R2a: Presets | Preset table and versions; built-in rows for the 13 codes; generic `create-record`; validator; preset-aware plan resolution, commit and Decision Record; RuleWizard library and editor |
| R2b: AI drafting | Proposal service (strict schema from the allow-list); simulate; draft, diff and publish UI; provenance |
| R2c: Breadth | `send-message` as a generic preset (templates, link-by-recipient); `update-subject`; "tidy the catalog" suggestions; adoption flow (Q6) |
