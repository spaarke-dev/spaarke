# CLAUDE.md — Spaarke Ontology Platform R1

> **Loads every session for this project.** Root [`CLAUDE.md`](../../CLAUDE.md) still applies in full; this
> file adds project-specific rules and does not override it.
> **Created**: 2026-10-03 by `/project-pipeline`

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks in this project, you MUST invoke the **`task-execute`** skill. Do
NOT read POML files directly and implement manually.

`task-execute` ensures: knowledge files loaded (ADRs, constraints, patterns) · context tracked in
`current-task.md` · checkpointing every 3 steps · quality gates (`code-review` + `adr-check`) at Step 9.5 ·
progress recoverable after compaction.

| User says | Required action |
|---|---|
| "work on task X" | Invoke `task-execute` with that task's POML |
| "continue" / "next task" | Read `tasks/TASK-INDEX.md`, find the first 🔲, invoke `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

Tasks that can run in parallel **still each use `task-execute`** — one message, multiple Skill invocations.

---

## 1. The one thing this project is for

Make **one predicate** computable, recordable and re-tunable without a deployment:

> A communication classified **fee or scope change** in the window **AND** no **budget revision** in that
> window.

If a change does not serve that, or the mechanism that records what was decided about it, it is probably out
of scope. Check [`spec.md`](spec.md) §2.2 before adding.

## 2. Vocabulary — do not improvise

[`notes/ontology-component-model.md`](notes/ontology-component-model.md) **§3 is authoritative**; §3.1 holds
the chain. Only two terms are defined:

- **Signal** (`sprk_signal`) — a condition that held. Engineering term.
- **Work Item** — the actionable unit a Signal produces. User-facing.

A **worklist row** is a **matter grouping Work Items**. "Row" is layout, not vocabulary — it is deliberately
**not** a defined term. **"Flag" is retired** (`sprk_highpriority` / `sprk_monitor` already own that word).
**"Typed outcome" is retired** — the word is **`disposition`** (CM-4, and it is ADR-039's own vocabulary).

The Signal's **subject** (`sprk_event`, `sprk_todo`, `sprk_workassignment`, `sprk_matter`,
`sprk_communication`, `sprk_servicerequest`) is the **target** of a Work Item and **never a Work Item
itself**.

## 3. Project-specific binding rules

### 3.1 §0.3 — a capability must TEST what its message CLAIMS

`sprk_signal.sprk_sentence` and `sprk_policyversion.sprk_messagetemplate` may assert **only what the
predicate actually read**. A template referencing a field the rule body does not read is a **defect**, not a
wording choice.

This is the generalized form of the project's own worst bug: a predicate read **no budget field** while its
message asserted *"unreconciled against its budget"*.

### 3.2 Membership and rank are deterministic — always

The LLM **classifies** (bounded by `sprk_triagecategory` rows) and **writes prose** over facts it did not
compute. It never decides membership, rank, whether a predicate is true, the authorize/deny outcome, or
what goes in the Decision Record.

> **A predicate evaluated by a model makes the Decision Record an anecdote.** This is the single most
> important constraint in the project.

### 3.3 Reuse first on UI — binding, not advisory

[`spec.md`](spec.md) FR-27 names three components that must be **extended, not rebuilt**:

| Need | ✅ Extend | ❌ Not |
|---|---|---|
| Count-filter cards | `WorkspaceShell/MetricCard.tsx:24-222` + `MetricCardRow` (already clickable) | A new card — **nor `StatTiles`**, which has **no `onClick` at all** (`StatTiles.tsx:95-111`) |
| The row ⋮ menu | `DocumentRowMenu.tsx:150-208` | A fourth hand-rolled menu — **three already exist** |
| Post-action outcome | `SprkChat/OutcomeCard.tsx:93-367` | A parallel outcome card |

⚠️ **Always cite the full path for `MetricCard`** — a second, unrelated one exists at
`Spaarke.Visuals/src/components/MetricCard.tsx` serving the `VisualHost` PCF (that ambiguity is item C-3).

**The UI/UX contract is the Console prototype's `HANDOFF.md` @ `ae1cc9f` (v4, findings 1–40)** —
`spaarke-dev/spaarke-prototype`, branch `feature/2026-10-spaarke-console`, path `projects/2026-10-spaarke-console/`;
local copy `c:\code_files\spaarke-prototype-wt-spaarke-console\projects\2026-10-spaarke-console\HANDOFF.md`. Read its
§0 first. **Before any UI task starts, check the task against HANDOFF §1 (binding behaviour), §3 (data needs) and
§4.1 (wizard host). Do not port prototype code.** Where v4 shows something the solution cannot do, it is an owner
decision, not an implementer's: see `notes/v4-prototype-vs-solution.md`.

**One component with data-driven variants, not a family.** A second row component is a design failure, not a
feature. Anything genuinely new lands in `@spaarke/ui-components`, never in `src/solutions/SpaarkeAi/`.

### 3.4 Things that look like extension points and are not

| Looks generic | Reality |
|---|---|
| `ILiveFactResolver` | **Dispatch** is config-driven by `(subject-scheme, predicate)`; **predicates are a closed C# `switch`** (`MatterLiveFactResolver.cs:166-174`). Nothing reads `sprk_communication`. **Neither Path B conjunct is reachable by adding a `case`** |
| `ISignalRule` | A **private nested interface** (`SignalEvaluationService.cs:268`) instantiated in the ctor (`:116-120`). Its XML doc claims a strategy pattern the code does not deliver |
| `GenerateDeterministicId(matterId, signalType)` | `:241-258` — matter+type only. **No room for a polymorphic subject**, so it cannot be reused |
| `notExists` in FetchXML | **No prior art anywhere in the repo.** `EXISTS` has some (`DataversePrecedentBoard.cs:182-190`); every `ConditionOperator.Null` filters the *primary* entity and every `LeftOuter` enriches rather than anti-joins |

**Do not let "already generic" read as "no new code."**

### 3.5 Dataverse schema changes — the recipe and its traps

**Never use MCP `create_table`.** It has no publisher parameter, this environment's default publisher is
**`new`** (CDS default `cr140`), and **logical names are immutable** — a wrong prefix is delete-and-recreate
after code is written against it.

Use Web API `POST /api/data/v9.2/EntityDefinitions` (or `.../Attributes`) with:

- an explicit **PascalCase `SchemaName`** — `sprk_RevisedOn` → logical `sprk_revisedon`, which is **how you
  get no underscore between words**;
- the **`MSCRM.SolutionUniqueName: OntologyPlatformSolution`** header — except on a *solution-create* POST,
  which must **not** carry it (it validates against a solution that does not exist yet → `404`);
- **`DateTimeBehavior` on every datetime.** Omit it → behavior `None` → filtered-view generation breaks →
  **every later relationship on that entity fails**, with an error naming the *datetime column*, not the
  lookup you were creating.

Local choice option values start at **`100000000`**, matching every choice already on these tables.

## 4. Applicable ADRs

| ADR | Rule in one line |
|---|---|
| **ADR-002** | No plugins. Append-only / immutable are **privileges**; one BFF owner per invariant |
| **ADR-004** | Queue work is an `IJobHandler` |
| **ADR-036** | Scheduled work is an `IScheduledJob`. **No new timer `BackgroundService`** |
| **ADR-052** | Placement decided per workload; tie-breaker **fewer moving parts** |
| **ADR-024** | Polymorphic regarding — reuse unchanged |
| **ADR-015** | Privilege is **flagged, never decided**. Copy the flag; never branch on it |
| **ADR-013** | No AI-internal types in the evaluator — `PublicContracts` facade only |
| **ADR-038** | Testing strategy + the two project rules in §5 below |
| **ADR-050** | `SprkModal` + its six presets for every modal |
| **ADR-045** | The tokenizer repair changes the association engine's precision/cost profile — measure it |

### ADR tensions — resolved, do not re-open (D-9)

| ADR | Path | Meaning for implementers |
|---|---|---|
| **ADR-039** | **A — project-scoped exception** | Policy decides whether a claim is **true**; Binding decides what **executes**. Cite this in the PR description |
| **ADR-040** | **B — amendment** | `SessionGate` and the Decision Record are **siblings**, linked by `sprk_decisionrecord.sprk_gatesessionid`. ⚠️ **The amendment must merge before or alongside the evaluator** (task 003) |

## 5. Testing rules this project added (ADR-038)

1. Any test asserting a Dataverse **column list, entity name or option-set value** must be paired with
   something that touches the **real schema**. A shape-only unit test **pinned a non-existent column for
   months** while coverage stayed green over a query that threw on every run.
2. A path that throws **consistently** is a **defect to diagnose, never noise to tolerate**. The
   `sprk_eventdescription` retrieve failed on *every* briefing run for months and nobody read the exception.

## 6. Operational traps — each cost real time

| Trap | What to do |
|---|---|
| **`origin/master` moves very fast** | 66 commits in a day; 70 within hours observed. **Merge master before any deploy** |
| **Hot-path collisions** | 56 of 62 active projects declare one. `/conflict-check` before **every** BFF PR |
| **BFF unit suite takes ~15 min** | Always `run_in_background`; a foreground run blows the 600s timeout |
| **PCF builds use `npm run build:prod`** | **Not** `npm run build` (root CLAUDE.md §12 / FAILURE-MODES AP-1) |
| **Swallow-and-log paths are invisible** | App Insights appId `6a76b012-46d9-412f-b4ab-4905658a9559` — `traces` for `[comms-policy]`/`[comms-ri]`, `exceptions` for swallowed throws. This is how the silent `InvalidCastException` was found |
| **Apostrophes break bash heredocs** | Write the content with the Write tool, or `git commit -F <file>` |
| **A POML quoting a bare element name in prose breaks its own XML parse** | Escape angle brackets in POML text. `scripts/Validate-TaskPoml.ps1` catches it |
| **Delegated wide audits failed five times here** | Run targeted agents **directly** and re-verify load-bearing claims. Doing so corrected a finding in our favour |

## 7. Publish-size verification (CLAUDE.md §10)

Every BFF-touching task measures the delta against a **fresh build of master**, from a **short path**, with
**file counts compared on both sides** and the zip tool named. The recorded baseline is a sanity check, not
the measurement — master grows continuously. Ceiling **≤60 MB**.

## 8. Definition of done for any task here

- Acceptance criteria in the POML all pass (they are a **closed set**, including negative cases)
- `code-review` + `adr-check` clean at Step 9.5 for FULL-rigor tasks
- `TASK-INDEX.md` updated 🔲 → ✅ **and** the POML `<status>` set — both, or
  `scripts/check-task-status-drift.ps1` fails the next push
- Any deviation recorded in `notes/`
- Any deferred work **filed as a GitHub Issue**, never merely listed (§5.0; `push-to-github` Step 1.6
  refuses a push without the URL)
