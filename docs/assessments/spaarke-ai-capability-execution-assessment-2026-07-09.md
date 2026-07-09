# Spaarke AI Capability Execution — Platform Assessment

> **Date**: 2026-07-09
> **Author**: main session (spaarkeai-compose-r2) + 6 read-only investigation sub-agents
> **Scope**: Platform-wide — the AI capability **execution + input-resolution + dispatch** layer of the whole Spaarke legal-AI solution. **Not** a Compose feature doc; Compose is the *symptom that surfaced it*.
> **Status**: ASSESSMENT for owner review. Precedes any ADR or task work. No production code written against these findings yet.
> **Evidence base for**: a proposed ADR ("AI Capability Execution — one contract-driven spine + engine-extension governance") and a re-scoped foundation work item, **owned by platform-core (`spaarke-ai-architecture-redesign-r2`)** with Compose r2 as the forcing consumer.
> **Precedent**: mirrors [`bff-ai-extraction-assessment-2026-05-20.md`](bff-ai-extraction-assessment-2026-05-20.md), which became the evidence base for CLAUDE.md §10 governance. This assessment intends the same arc for the execution layer.

---

## 0. Why this exists

Compose task 034 (undo/replace) tripped over the fact that **compose AI actions cannot dispatch at all** — the shared dispatch engine rejects the `compose` disposition and can't feed a selected clause to the model. Investigating *why* revealed the compose gap is **one instance of a structural pattern**, and that patching it project-locally (widen the seam "just enough for Compose") would (a) leave the pattern intact for the next capability and (b) repeat the exact orphaning that created it. The owner asked for a whole-platform requirement → problem → solution analysis instead. This is that.

**One-sentence thesis**: *Spaarke advertises a single declarative AI-capability contract (Action + Binding + input schema + disposition), but its canonical execution spine realizes only a narrow slice of that contract — input hardcoded to uploaded files, disposition a triplicated hand-coded allow-list, real side-effects living on a second parallel spine — and the governance model owns contract **shapes** but not execution **wiring**, so the gap between "contract designed" and "engine realizes it" is invisible and unowned.*

---

## 1. Requirement — what the legal-AI platform needs from capability execution

Framed from the product, not from Compose. Spaarke is an AI-directed legal operations platform: makers/operators add AI capabilities continuously (explain a clause, compare to a playbook, draft an alternative, summarize Word changes, classify an invoice, draft an email, create a matter, write a governed memory, push a notification…). The execution layer must therefore satisfy:

- **R1 — Contract-driven, not consumer-shaped.** A new capability should be dispatchable by *declaring* its contract (input source, output schema, disposition, action kind), not by hand-editing the engine. Adding the Nth capability must not require editing an allow-list in three files.
- **R2 — Heterogeneous input sources as first-class.** Capabilities operate on: uploaded files, a **selected span** of an open document, **open-document text**, **entity/matter/host context**, **prior-ledger outputs**, **durable memory**, and **structured args**. Input source is part of the contract; the engine must resolve it generically.
- **R3 — Heterogeneous outputs (dispositions) as first-class.** Informational, work-product, **compose edits**, email, record writes, notifications, overlays, and **deterministic operations** (e.g. a retraction) are all legitimate outputs. Disposition capability must be single-sourced so admission and routing cannot drift.
- **R4 — One protocol, three entry paths (ADR-039).** Event / Click / Text must converge on one dispatch protocol with consistent admission and execution semantics — no capability reachable on one path but 422 on another.
- **R5 — Storage precedes rendering (ADR-040).** Every capability output is a ledger `SessionOutput` before render; supersession is the edit/undo model. Deterministic and interactive capabilities must fit this without special-casing.
- **R6 — Cross-cutting changes have an owner.** A change to the shared execution engine (as opposed to a contract shape) must have a clear owner and intake, so it can't fall between platform-core and satellite projects.
- **R7 — "Done" means the vertical slice works.** A distributed capability is "done" only when a real consumer dispatches through the seam to a stored, rendered output — not when each component passes in isolation.

The current platform meets **R4/R5 partially** and **R1/R2/R3/R6/R7 not at all** for anything beyond the first (file→informational) consumer shape. Sections 2 proves this.

---

## 2. Problem — root-cause analysis (technical + organizational)

### 2.1 Technical root cause A — TWO disjoint capability spines, and the declarative one is half-built

The platform has **two unrelated mechanisms** by which an AI capability produces an effect:

| Spine | Mechanism | What actually flows through it |
|---|---|---|
| **Declarative disposition spine** (ADR-039 catalog) | Binding → `SessionDispatchOrchestrator` → `ActionRunner` → `OutputRouter` (routes by disposition) | Only **read-only/informational** + two persistence legs (work-product, email) |
| **Imperative tool-handler spine** | LLM agent-loop tools gated by `side_effect_class` (`DataverseCreateRecordHandler`, `EmailDraftToolHandler`, `CreateNotificationNodeExecutor`, `SendWorkspaceArtifactHandler`, `ManagePinnedContextHandler`, …) | Every real **side effect**: record writes, email, notifications, workspace overlays, memory-write |

The ADR-039 promise is "one dispatch protocol, closed catalogs." In reality the declarative spine only ever realized the *informational slice*; **all genuine side-effecting capability lives on the second, parallel spine.** The disposition enum advertises 7 members as one routing surface, but of the 7:

| Disposition | Router leg | Dispatch admits? | Reachable e2e | Note |
|---|---|---|---|---|
| Informational (…000) | real | ✅ | ✅ (all paths) | the one fully-realized case |
| WorkProduct (…001) | real (persister) | ✅ | ✅ | second realized case |
| **Compose (…006)** | **real** (I promoted it 2026-07-09) | **❌** | **partial (Event only)** | **rejected pre-run on Click/Text** — the drift |
| Email (…003) | real (sender) | ❌ | partial | reachable only via coded briefing, bypassing the gate |
| Overlay (…002) | **stub `throw`** | ❌ | ❌ | "later waves" |
| Record (…004) | **stub `throw`** | ❌ | ❌ | real record-write is on the *other* spine |
| Notification (…005) | **stub `throw`** | ❌ | ❌ | real notification is on the *other* spine |

**Disposition capability is triplicated across three hand-maintained lists that drift**: the `OutputRouter` routing switch, the `SessionDispatchOrchestrator` admit-set, and the `ToLedgerValue` enum→wire map. My compose promotion updated two and left the third (the admit gate) at `{Informational, WorkProduct}` — *that live drift is exactly why compose 422s today*. There is no registry, no attribute discovery, no single source of `{disposition → leg, admits?, ledger-string}`.

### 2.2 Technical root cause B — the canonical engine is the input-poorest, and the intended unifier is unbuilt

There are **three** prompt→LLM executors sharing one renderer:

| Input capability | `AiAnalysisNodeExecutor` (playbook, tool) | `AiCompletionNodeExecutor` (playbook, prompt) | **`ActionRunner`** (canonical ADR-039 dispatch) |
|---|---|---|---|
| File/document text | ✅ | ❌ (prohibited) | ✅ (**required**, throws if empty) |
| Structured args (`runtimeInput`) | ❌ | ✅ | **❌** |
| Knowledge/RAG | ✅ | ❌ | **❌** |
| Skills | ✅ | ❌ | **❌** |
| Template params | ✅ | ✅ | **❌** |

`ActionRunner` — the engine ADR-039 designates canonical for **all new capability** — accepts **document text and nothing else**. `PromptSchemaRenderer` is a genuine **superset seam** (it already accepts `runtimeInput`, knowledge, skills, template params, downstream choices) — but `ActionRunner` passes the smallest subset and hardcodes a single `{{document.extractedText}}` placeholder. It **could** pass `runtimeInput` today (the parameter exists); it doesn't.

Crucially, **the platform already designed the general input-resolution abstraction** — and hasn't built it:
- **`ContextEnvelope` v1** (`Services/Ai/PublicContracts/ContextEnvelope.cs`) — the "canonical per-turn context contract," six slices {User, Workspace, Business, Memory, Organizational, Semantic}. Shipped as a *walking-skeleton contract*: budgets are placeholders, two slices interface-only.
- **`ContextBinder`** — the per-turn assembler that would populate that envelope — **does not exist in code** (grep: zero matches). It is redesign-r2 **task AIR2-053, status not-started**, whose job is literally "generalize the six R1 prompt-assembly primitives into one cache-stable Binder."
- **Neither execution engine consumes `ContextEnvelope`.** The canonical dispatch engine takes only `DocumentText` + a `LinearRunContext` (ids only — no context slices).

So input resolution is **ad hoc per consumer**, the canonical engine sees the least of it, and the designed unifier (`ContextEnvelope` + `ContextBinder`) is a frozen contract with an unbuilt binder that nothing on the hot path reads.

### 2.3 Why Compose hit it first

Compose is the first capability to need, on the canonical seam, **both** a new disposition (`compose`) **and** a non-file input (selected clause / open-document text). It therefore hit both unrealized halves at once: the disposition admit-gate rejects it, and even past that, the engine can't feed it the selection. The four read-only compose actions (explain/compare/summarize/defined-terms) hit the input half too — they'd get whole-file text or a no-file error instead of the selected clause. **This is not a compose bug; it is the canonical spine meeting the second and third capability shapes it was never generalized for.**

### 2.4 Organizational root cause — the intake owns contracts, not wiring; DoD is per-component; orphaning is systemic

- **Intake gap.** The core/satellite handshake (`SEAM-STATUS.md` + bidirectional handoff docs, charter §3.4 "contract-first, walking-skeleton") governs **contract shapes**. When a needed change is *execution wiring* rather than a *shape*, no task claims it. In the teams' own words, the compose routing promotion *"fell through the cracks — no task owns that promotion"* (both core and satellite confirmed in writing). SEAM-STATUS showed all-green on *contracts* while the *execution path was 422-broken*.
- **No owner for the shared execution engine's wiring.** `SessionDispatchOrchestrator`/`OutputRouter`/`Binding.cs` are core territory ("sole owner of `Services/Ai/` internals"), but "generalize the seam for a new shape" had no owning task — task 010 deferred it "for parallel-agent safety," redesign-r2 never re-parented it, Compose did the router half. **There is a *second* live orphan in the same seam right now**: SEAM-STATUS flags "wiring the engine into the core's own live gate is **UNASSIGNED** in the current WBS."
- **Per-component definition-of-done.** ADR-038's KEEP categories are all per-component (endpoint-contract, domain-logic, data-mutation). The walking-skeleton model ships a *contract test per seam in isolation* but mandates **no vertical-slice test** (real consumer → dispatch → stored ledger entry). Compose 016/042 were "done" per-component (contract green, client half shipped, rows authored) while the integrated path 422'd end-to-end. Spike-0 itself confirmed the seam *by static trace only* and deferred the runtime legs.
- **Systemic, not one-off.** This failure class is already catalogued: `FAILURE-MODES.md` **AP-2** ("two entry points, one contract, asymmetric callers" → orphan RAG chunks) is nearly isomorphic; **AP-4** is another; the bff-extraction assessment documents 20 orphaned CRUD→AI dependencies from the same "features added without holistic ownership" dynamic. `projects/INDEX.md` coordinates *file-collision* risk but not *capability-completeness* risk.

---

## 3. Solution — the correct way (extend existing platform intent, don't invent)

The fix is **not** "unify the two execution engines" — ADR-039 deliberately keeps the frozen node-graph engine + the canonical linear seam, converging only at R8+ (compile-to-code). The fix is to **complete the canonical spine's realization of the two contracts it already claims, single-source their capability, reconcile the two-spine boundary, and govern the seam so it can't re-orphan.**

### 3.1 Technical — three moves, each completing something already designed

**Move 1 — Wire the canonical dispatch engine to the platform's input-resolution model (R1/R2).**
Make `ActionRunner` / `SessionDispatchOrchestrator` resolve input *from the Action's declared input source*, not hardcoded files. The mechanism already exists at two maturity levels:
- *Minimum*: pass dispatch args through to `PromptSchemaRenderer.Render` as `runtimeInput` (the `## Input` section the playbook engine already uses). This alone unblocks all 5 compose actions and any future args-driven capability.
- *Target*: consume an assembled **`ContextEnvelope`** (built by **`ContextBinder`**, redesign-r2 task 053) so files, selection, open-doc, entity context, ledger, memory, and structured args are all resolved uniformly for every disposition. Compose's selection/open-doc input becomes ContextEnvelope slices — **compose is the forcing consumer that finally wires input-resolution to the canonical engine**, rather than a compose-special hack.
- Relax the "no session files → error" hard stop for args/context-driven dispatches.

**Move 2 — Single-source disposition capability (R3).**
Replace the three drift-prone lists with one registry — `{disposition → routing leg, admits-on-dispatch?, ledger-string, action-kind constraints}` — so admission is driven by "the router can route it," and adding/finishing a disposition is one edit, not three. Deliberately decide each stubbed leg (overlay/record/notification): finish it, or explicitly defer it **with a named owner** (see §3.2).

**Move 3 — Reconcile the two capability spines (R3/R4) — the biggest architectural decision.**
Decide, as a platform ruling, which capabilities are **disposition-routed** vs **loop-tool-handlers**, and *why*, and where **interactive/deterministic** capabilities (compose edit, compose retract, and future document mutations) belong. Options to weigh in the ADR:
- (a) Bring side-effecting capabilities onto the declarative disposition spine (finish overlay/record/notification; model compose-edit + retract as dispositions incl. a deterministic action-kind) — one spine, ADR-039 fully realized.
- (b) Formalize the two-spine model as intentional (declarative = read/inform/persist; tool-handler = side effects) and document the boundary + where compose edits sit.
- (c) A hybrid: a distinct **document-mutation capability class** for interactive edits (propose→pending→accept/reject→supersede) that doesn't force stateful editing through stateless request/response.
This is the "fundamental" call the owner asked about; the assessment recommends it be an explicit ADR decision, not a default.

### 3.2 Organizational — extend the proven §10/§11 governance to the execution engine (R6/R7)

The repo already has the ideal template (§10 BFF Hygiene, §11 Component Justification): *binding CLAUDE.md rule → `.claude/constraints/*` checklist → mandatory `design.md` section → PR declaration → code-review Step → cited evidence-base assessment; no CI script; forcing functions in the skills.* Apply it to the AI execution engine:

1. **Named owner for the shared AI execution engine.** Platform-core (`spaarke-ai-architecture-redesign-r2`) owns `SessionDispatchOrchestrator` / `OutputRouter` / `ActionRunner` / `Binding` disposition wiring. A new binding CLAUDE.md section ("AI Execution Engine — Binding Governance") + `.claude/constraints/ai-execution-extensions.md` requires: any task adding a **disposition, input source, action kind, or entry-path** states the placement decision, updates the single-source registry, and cannot leave the admit-gate/router/map divergent.
2. **Vertical-slice definition-of-done (new ADR-038 KEEP category).** A required `tests/integration/seam/**` (or "consumer-dispatch") test that exercises **real consumer → dispatch → stored `SessionOutput` → render** for each new capability, so per-component green can't mask a 422 integrated path. This directly closes the gap that hid AP-2, AP-4, and compose.
3. **Deferral re-parenting forcing function.** When a task POML defers a cross-cutting slice "for parallel-agent safety," the deferral MUST be filed against an *owning task* (extend the two-write `defer-issues.md` rule to require an assignee). "No task owns it" then cannot recur. (Would have caught the compose routing promotion **and** the still-live UNASSIGNED gate-wiring orphan.)
4. **Capability-completeness in `INDEX.md`.** Extend the hot-path registry to track capability-completeness (a capability with a published contract but no vertical-slice test is flagged), not just file-collision risk.

### 3.3 How Compose fits (consumer, not owner)

Compose's foundation work (formerly the ad-hoc "task 018") is redefined as **the first consumer of Move 1** — it is *not* compose's job to own the engine change. Concretely: core lands the input-resolution wiring + disposition single-sourcing (Moves 1–2) and the vertical-slice DoD; Compose consumes it (draft-alternative dispatches; the 4 read-only actions get selection text; undo/replace + retract land per the owner's Path B once Move 3 decides how a deterministic retraction is modeled). This is the anti-orphaning outcome: **a platform decision owned at platform level, with compose as the forcing consumer and co-author.**

---

## 4. Recommended decisions (for owner)

1. **Adopt this as the evidence base for a platform ADR** — "AI Capability Execution: contract-driven input + single-sourced dispositions + engine-extension governance" — **owned by redesign-r2 (core)**, co-authored with Compose r2.
2. **Move 1 minimum (runtimeInput wiring) is approved as the compose unblock** *inside that ADR's frame* — i.e., compose rides the general mechanism, not a compose-special branch. Target (ContextEnvelope/ContextBinder, task 053) sequenced by core.
3. **Move 3 (two-spine reconciliation + where interactive edits live) is an explicit ADR decision**, not defaulted. Recommend option (c) — a document-mutation capability class — but this is the owner's call.
4. **Governance §3.2 items 1–3 are adopted** (engine owner + vertical-slice KEEP category + deferral re-parenting). These are cheap, proven-shape, and prevent recurrence.
5. **Re-plan the Compose chain** against the above (016/042/046/047/034 re-scoped as consumers of the platform foundation), per [`compose-dispatch-foundation-validation.md`](../../projects/spaarkeai-compose-r2/notes/compose-dispatch-foundation-validation.md) — but with the foundation task **owned by core**, not compose.

## 5. Open questions

- **Q1 (Move 3)**: one spine (finish dispositions) vs. formalized two spines vs. a distinct document-mutation class? Determines where compose-edit + retract live and how the ActionKind gate is handled.
- **Q2 (input target)**: gate compose behind the full `ContextBinder` (task 053) or ship `runtimeInput`-minimum now and migrate to the envelope later? Trade-off: coupling compose to an unbuilt binder vs. a second migration.
- **Q3 (ownership mechanics)**: does core take the foundation as a new redesign-r2 task now, or is a joint mini-project stood up? Either way it must have a *named owner*, per the whole point of this assessment.
- **Q4 (scope of finishing dispositions)**: finish overlay/record/notification legs as part of this, or explicitly defer-with-owner? (Record/email/notification already have working tool-handler implementations — reconciliation, not green-field.)

---

## 6. Evidence index

**Two spines / disposition realization** — `Services/Ai/OutputRouter.cs:224-282` (switch + stubs), `:383-394` (ToLedgerValue), `:22-24,57-58` (roadmap comments); `Services/Ai/Chat/SessionDispatchOrchestrator.cs:224` (admit gate), `:52-54,218-223` (P1/P2/P3 envelope), `:209` (ActionKind gate), `:248-297` (file requirement); `Services/Ai/PublicContracts/Binding.cs:117-145` (7-member enum); side-effect tool-handlers `Services/Ai/Handlers/{DataverseCreateRecord,EmailDraftTool,ManagePinnedContext,SendWorkspaceArtifact}Handler.cs`.
**Three executors / input asymmetry** — `Services/Ai/LinearConsumers/ActionRunner.cs:27,44,75,90-101,120-145`; `Services/Ai/Nodes/AiCompletionNodeExecutor.cs:187,195,313,319`; `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs:414,417,430`; `Services/Ai/PromptSchemaRenderer.cs:83-93,224-239,242-248`.
**Intended-but-unbuilt input model** — `Services/Ai/PublicContracts/ContextEnvelope.cs` (v1 walking skeleton); `ContextBinder` absent (grep zero); `projects/spaarke-ai-architecture-redesign-r2/tasks/053-context-binder-contextenvelope-assembly.poml` (not-started); `Services/Ai/PublicContracts/MemoryItem.cs`.
**Direction of travel (two engines by design)** — `.claude/adr/ADR-039-grounded-execution-closed-catalogs.md:70` (MUST-NOT new capability on frozen node engine); `docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md:12,16-20,222-224` (two-path split, interpreter tax, compile-to-code R8+); `Services/Ai/LinearConsumers/LinearRunContext.cs:9-13`.
**Governance precedent + intake/DoD gap** — `CLAUDE.md` §10/§11; `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`; `docs/adr/ADR-038-testing-strategy.md` §2 (KEEP categories); `projects/spaarke-ai-architecture-redesign-r2/design.md` §3.2-3.4, §0.2 R-2; `projects/spaarke-ai-architecture-redesign-r2/notes/SEAM-STATUS.md` (contracts-green-while-broken + UNASSIGNED gate-wiring orphan); `.claude/FAILURE-MODES.md` AP-2/AP-4; `projects/spaarkeai-compose-r2/notes/HANDOFF-to-redesign-r2-compose-routing-promotion.md` (+ response — "fell through the cracks"); `projects/spaarkeai-compose-r2/notes/compose-dispatch-foundation-validation.md`.

---

*Prepared as a decision-of-record candidate. The next step is owner review of §4/§5, then a core-owned ADR + re-plan — deliberately, at platform level, so this class of debt is owned rather than orphaned.*
