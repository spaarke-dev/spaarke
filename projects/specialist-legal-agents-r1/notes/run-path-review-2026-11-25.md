# Phase 1 run path: playbook orchestration vs the action-based model (2026-11-25)

> **Method.** Read-only, Fable-level review of origin/master at `d148580e15`, requested by the owner after the Phase 0 spec named playbook orchestration as the Phase 1 run path. The owner's challenge was: "we had significant issues with getting our playbook concept to work and reverted to using an actions-based model … confirm at the code level". One general-purpose agent ran it (model fable): 196k tokens, 32 tool calls.
>
> **Spot-checked by the main session on master:**
> - ADR-039 has the MUST NOT on the frozen node engine and the MUST for `coded` composites (`.claude/adr/ADR-039-grounded-execution-closed-catalogs.md` "MUST NOT land new capability on the frozen node-graph engine").
> - ADR-013's 2026-07-05 amendment makes capability invocation the canonical verb.
> - `sprk_playbookrun.sprk_playbookid` is `RequiredLevel=required`.
>
> **Outcome:** the spec's Phase 1 run-path line was reversed the same day (spec FR-13, ADR list, Owner Clarifications). The run-record question is an owner decision (spec Unresolved Questions, D-A18 (7)).

Paths are relative to `src/server/api/Sprk.Bff.Api/` unless they start with `.claude/`, `docs/`, `infra/` or `projects/`.

## Verdict

**Do not use `IPlaybookOrchestrationService` for Phase 1.** Use the action-based model:
- **single functions:** a `prompted` Action via `IActionResolver` + `IActionRunner`, behind a `PublicContracts` facade;
- **multi-step functions:** an `ICodedWorkflow`.

Confidence is high.

The owner's recollection is correct, and it is already binding:
- ADR-039 says "MUST NOT land new capability on the frozen node-graph engine" and "MUST author new composite capabilities as `coded` workflows" (`.claude/adr/ADR-039-grounded-execution-closed-catalogs.md:76`, `:59-60`).
- The spec cited "ADR-013 Invariant 1", but that is not in ADR-013. It is r7's design invariant, "Playbook is the only AI invocation model" (`projects/spaarke-ai-platform-unification-r7/design.md:56`). ADR-013's 2026-07-05 amendment reversed it: capability invocation, `invoke(bindingId, args)`, is the canonical verb, and new consumers must not be wired to the playbook facade (`.claude/adr/ADR-013-ai-architecture.md:4,8,49`).

## History

- **June 2026 (r7):** everything was forced through the node engine; "even a single LLM call is a 3-node playbook" (r7 `design.md:58`). The comment at `Services/Ai/AnalysisOrchestrationService.cs:80-83` is a relic of this.
- **2026-07-05 (code audit, owner-approved):** the audit found the ADRs had "entrenched playbook-centricity" (`projects/spaarke-ai-code-audit-r1/ADR-REVIEW-VS-GREENFIELD.md:19-27`).
  - The resolution (OQ-2) froze the engine: "no new capability lands on it". Only the existing Insights pipelines use it, and it retires by attrition (`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md:2268`, `:2276-2292`).
  - Execution now has two shapes, `prompted` and `coded` (same doc `:2262-2268`).
- **Concrete engine failures:**
  - **Document profiling (#919):** every Outlook-saved document ended `Failed`, because the Update Record node re-parsed rendered config. The direct-Action paths were "structurally immune" (`docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md:22-26`). The fix moved app-only profiling onto the Action spine (commit `f5c7687d8`; same doc `:124`).
  - **October defect sweep** (`projects/spaarke-ontology-platform-r1/notes/playbook-engine-defect-sweep.md:20-41`, rows 1.7 and 1.9). No later commit was found that fixes PB-01/02.
    - Skipped nodes do not propagate the skip (PB-02).
    - `continueonerror`, `retrycount` and `timeoutseconds` are not enforced.
    - One Designer save destroys a deployed playbook (PB-08).
    - "Of 27 live non-notification playbooks, only [one] is likely to finish with useful output", and `matter-health-single` "always ends in the fallback decline".
  - **Notification playbooks** were retired (commit `795e2a73d9`, D-100).
  - **ebilling** deleted its extraction playbook path and moved to Actions (commits `3f0c776841`, `43b8cc23f6`).
- **These causes apply to this program.** They sit in templated config rendering between nodes, skip semantics and maker-editable graphs, which are exactly what a multi-node specialist function would rely on.
- **Model tier is a second blocker.** The node path picks models by deployment name, and the tier resolver's only caller is `ActionRunner` (`infra/dataverse/actions/agreement-classify.action.json:3`; `Services/Ai/LinearConsumers/ActionRunner.cs:144`).

## Live paths

| Capability | Path | Evidence |
|---|---|---|
| NDA / agreement review, classify | Action + Binding → `SessionDispatchOrchestrator` → `ActionRunner` ("ACTION-ONLY, no playbook") | `infra/dataverse/actions/agreement-review.action.json:2`; `infra/dataverse/sprk_playbookconsumer-rows.json:694-703`; `Services/Ai/Chat/SessionDispatchOrchestrator.cs:655-657` |
| Chat Text path | `BindingCapabilityTool` → same dispatch | `Services/Ai/Chat/BindingCapabilityTool.cs:242,319` |
| Advisory grounded capabilities (list-tasks) | `AdvisoryCapabilityRunner` (nested bounded turn; output schema not enforced) | `SessionDispatchOrchestrator.cs:637-651`; `Services/Ai/Chat/AdvisoryCapabilityRunner.cs:41-43` |
| Daily Briefing | `coded` composite (`ICodedWorkflow`) | `Api/Ai/DailyBriefingEndpoints.cs:54-57`; `Services/Ai/Narrators/DailyBriefingNarrator.cs:82` |
| Document profile (all entries) | Direct Action | `Api/Ai/AnalysisEndpoints.cs:991`; `Services/Ai/AppOnlyAnalysisService.cs:583` |
| Invoice AI, email triage/propose | Facade → `ActionResolver` + `ActionRunner`, no session | `Services/Ai/PublicContracts/InvoiceAi.cs:312,355` |
| Insights ask / ingest (matter health) | **Node engine (frozen)**, wrapped by the cache | `Services/Ai/Insights/InsightsOrchestrator.cs:452-460,684` |
| Analysis workspace (non-profile playbooks), run routes | Node engine (legacy) | `Api/Ai/AnalysisEndpoints.cs:574`; `Api/Ai/PlaybookRunEndpoints.cs:197` |
| Scheduled playbooks | Node engine, app-only | `Services/Ai/PlaybookSchedulerJob.cs:523` |
| Copilot agent → playbook | **Never wired** (TODO stubs, MCI-023) | `Api/Agent/PlaybookInvocationService.cs:45-51,296,365` |

`PlaybookOrchestrationService` is live only for legacy or frozen consumers. Its runs are held in memory ("Phase 1 - replaced with Dataverse in Phase 2"), with a one-hour eviction (`Services/Ai/PlaybookOrchestrationService.cs:61-62,600-608`).

## Comparison against this program's needs

| Need | Node graph | `IActionRunner` + facade | Session dispatch (Binding) | `ICodedWorkflow` |
|---|---|---|---|---|
| Single function with profile inputs and structured output | Works, but ADR-039 forbids it and it has no tier resolver | **Best fit.** Declared inputs render into `## Input` (`.claude/adr/ADR-043-ai-capability-execution-spine.md:16`); one structured call (`ActionRunner.cs:199`) | Same engine, but needs a chat session | Overkill |
| Under 30 s, or start then check status | In-process stream; status is lost on restart | Synchronous only. Inference: start/check needs an ADR-004 job plus `JobAwareCompletionState` (`Services/Ai/PublicContracts/JobAwareCompletionState.cs:10-17`) | Same | Same |
| Multi-step later (patent sections) | Native but defective | Composed in code | The coded leg exists (`SessionDispatchOrchestrator.cs:385-410`) | **The sanctioned shape** |
| Typed chaining, parent and child runs | Handlebars between nodes. `PriorOutputReference` is "for validation and documentation" only, and nothing consumes it (`Services/Ai/Models/PromptSchema.cs:111-116`) | No built-in chaining; the caller passes typed JSON | `ledger_resolution` operand: a prior `SessionOutput` by key (`Services/Ai/Context/ContextBinder.cs:265-271`) | In code. No parent-run concept anywhere |
| Durable run record | None today | **None.** `LinearRunContext` carries only consumer type, correlation id and tenant (`Services/Ai/LinearConsumers/LinearRunContext.cs`) | `SessionOutput` ledger, Tier 3 and erasable (`.claude/adr/ADR-040-session-ledger.md:45-46`) | None of its own |
| One confirmation gate | Not integrated | Not applied by the facade pattern | Yes, the `sprk_risk` gate (`SessionDispatchOrchestrator.cs:308-341`) | Via dispatch |
| Golden-set eval | One Insights golden (`tests/Insights/golden/predict-matter-cost.json`) | Established: `tests/integration/contract/Eval/AgreementClassifyEvalTests.cs` | Same suite | A briefing eval exists |

**Correction to design §3.15:** `PriorOutputReference` is not a chaining mechanism. It is a node-engine documentation field, and nothing reads it.

## Implications

- **FR-02 (spike):** correct as written. `IActionRunner` over `agreement-review` is the production-shaped path, not a throwaway stand-in.
  - Use the `BoundInputs` overload (`Services/Ai/LinearConsumers/IActionRunner.cs:57-61`).
  - Declare the profile positions in the Action's `sprk_inputschema`; undeclared arguments are dropped (ADR-043 `:16`).
- **FR-13 (Phase 1 run path):** reversed. Phase 1 uses a `PublicContracts` facade (the `InvoiceAi.RunCapabilityAsync` pattern), with `ICodedWorkflow` for multi-step functions. Keeping playbook orchestration would need a §6.5 Path B amendment to ADR-039 and ADR-013, which works against two owner-ratified decisions.
- **D-A18 (7):** `sprk_playbookrun` is the wrong table as it stands. `sprk_playbookid` is required (`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_playbookrun/Entity.xml:566-570`), and an Action run has no playbook row. The action path has no run record, so this is a real gap. The options, for the owner:
  1. a new capability-run table (action code, binding, profile version, parent run, caller, gate decision), justified under §11;
  2. relax `sprk_playbookid` and add an action lookup, which mixes two concepts in one table;
  3. the ledger plus the Decision Record, which is not durable for attribution because the ledger is erasable.

  The reviewer leans to option 1, written at the facade or coded-workflow seam; this is inference.
- **mcp-r2 FR-13:** it persists runs only from `PlaybookOrchestrationService` (mcp-r2 `spec.md:173-174`).
  - That is defensible as status persistence for existing playbooks, but it records no Action-path run, so this program cannot reuse it (mcp-r2 `spec.md:380`).
  - Its `spaarke_run_playbook` tool also gives Copilot a new way into the frozen engine, which is worth an ADR-039 check there.
  - The two projects should agree on one run-record design before either adds columns.

## Open risks and what was not verified

- Whether `agreement-review` on the Reasoning tier finishes within 30 s. There are no timings in the code, so the spike measures it.
- Open issue #1934: the linear facade path ignores the Binding's tier override. Only the session dispatch path composes overrides (`SessionDispatchOrchestrator.cs:630`), so the profile's model tier must be applied explicitly (`action with { ModelTier = … }`).
- ADR-043 is still "Proposed" (`:3`). The `ICodedWorkflow` context is deliberately minimal, with no run id or parent (`Services/Ai/ICodedWorkflow.cs:66-80`).
- The confirmation gate lives on the session dispatch path. A session-less MCP facade must call the same gate explicitly. How `Api/Mcp/**` does this today was not traced.
- Live DEV data was not checked. The defect sweep is dated 2026-10-09, and some engine defects may have been fixed since without a matching commit message.
- mcp-r2's spec was read from its worktree, not from master.
