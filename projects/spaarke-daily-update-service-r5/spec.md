# Spaarke Daily Update Service R5 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-08
> **Source**: `design.md` v0.2 (operator review complete, §6 resolved 2026-07-08)
> **Predecessor**: `spaarke-daily-update-service-r4` (hallucination Round 1) · R7 W12 widget cutover (Round 2a)

## Executive Summary

R5 makes the Daily Briefing **trustworthy** and **appealing**. Two rounds of prompt-steering (R4 temperature-0/grounding, R7 W12 pairing rules) failed to stop cross-item hallucination, so R5 mechanizes: item-level facts render **deterministically** from source records, and the LLM keeps only the abstraction layer (a TL;DR built on deterministically-computed facts). Correctness is by construction — there is no probabilistic groundedness threshold. In parallel, R5 redesigns the briefing's currently-flat UI via the `/prototype` harness workflow (operator-approved before production wiring), and clears the accumulated hardening backlog (Choice-write coercion, collaborator-scope, collector de-dup, client-helper tests, an OData naming convention). The Monitored-For schema and the unused EventDetailSidePane fix are deferred.

## Scope

### In Scope

**Accuracy core (Gate G-R5-A):**
- Deterministic item-level rendering of all briefing channel/section rows (zero LLM in item rows)
- TL;DR remains LLM-authored but built on deterministically-computed facts + binary anchor resolution (no threshold)
- Retire `BRIEF-NARRATE-CHANNEL` Action; keep `BRIEF-NARRATE-TLDR`
- Simplify the `DailyBriefingCompositeService` coded workflow (one TL;DR LLM call)
- Eval family (mixed-item corpus + others) as CI merge gate

**Visual redesign (Gate G-R5-D):**
- `/prototype` harness for `Spaarke.DailyBriefing.Components`, iterate on UX, operator sign-off, port to production shared-lib components under Fluent v9 + ADR-021 tokens (light + dark)

**Hardening sweep (Gate G-R5-C):**
- Choice-field write coercion in `UpdateRecordNodeExecutor` + `jps-validate` authoring check + node fieldMapping sweep
- Collaborator-scope fix (assigned attorneys see their matters) + collector de-duplication
- Jest tests for fragile client helpers; collapse `QueryHighPriority*` helpers; Promise-cache primary-contact lookup; truncation-comment fix
- OData naming convention doc + repo-wide `@odata.bind` grep audit

**Deploy convention (D-6):**
- Master-sync-first binding convention + one-line branch-behind-`origin/master` warning in touched deploy scripts

### Out of Scope

- **Monitored-For schema** (`sprk_monitorreason` / `sprk_monitornotes`) — deferred to a later round (prerequisites + other considerations); no `sprk_monitor*` schema deployed in R5
- **Groundedness threshold / warn-withhold band** — rejected; correctness is by construction, not by score
- **EventDetailSidePane fix** — side-pane not currently in use; its `@odata.bind` one-liner deferred (the repo-wide grep audit still fixes any *in-use* occurrences)
- **New widget framework / layout engine** — D-8 extends existing components only
- **LLM channel-narration rescue** (Round 3 of prompt engineering) — not a track
- **New briefing entry paths, Compose/assistant scope, multi-language choice-label localization**
- **`sprk_monitor` Boolean deletion** — belongs to the deferred Monitored-For round

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefing*` — coded composite, view-model assembly, TL;DR call (BFF hot path)
- `src/server/api/Sprk.Bff.Api/Services/Ai/.../UpdateRecordNodeExecutor.cs` — Choice-coercion (frozen-engine defect-hardening)
- `src/client/shared/Spaarke.DailyBriefing.Components/**` — deterministic renderer, TL;DR block, "Critical Today" section, redesign target (SpaarkeAi hot path)
- `tests/unit/Sprk.Bff.Api.Tests/**` — collector tests (incl. the pinned resolver-bypass test), coercion tests, eval cases
- `spaarke-prototype` repo — D-8 harness (cross-repo, tracked as R5 tasks per owner clarification)
- `docs/standards/` — OData naming convention doc
- Catalog data (Dataverse) — retire `BRIEF-NARRATE-CHANNEL`, keep `BRIEF-NARRATE-TLDR`
- `.claude/skills/jps-validate/` — Step 7.7 authoring-check extension (main-session-only edit; skill-directive hot path)

## Requirements

### Functional Requirements — Accuracy (Gate G-R5-A)

1. **FR-A1 (Deterministic item rows)**: Every channel/section row is composed **only** from known-safe source-record fields (title/subject, party/sender, date, flags, regarding record name, server-composed link). Zero LLM involvement in item-level rendering. — **Acceptance**: on the browser UAT briefing, click any item link → the target record matches the row text exactly; a mixed-item corpus scenario produces **zero** cross-item pairing (title/regarding/citation never sourced from different items).

2. **FR-A2 (Retire channel narration)**: `BRIEF-NARRATE-CHANNEL` Action row is retired (catalog-data change via BA editor/MCP); all code consumers removed. — **Acceptance**: repo-wide grep for `BRIEF-NARRATE-CHANNEL` returns zero code references; eval cases referencing it updated per NFR-05.

3. **FR-A3 (Composite simplification)**: `DailyBriefingCompositeService` reduces to **collect → assemble deterministic view model → ONE TL;DR LLM call → render**. Per-channel LLM narrate calls removed. — **Acceptance**: exactly one LLM call per briefing run on the coded entry path; task-054 metering shows a measurable per-run token reduction (before/after captured in gate evidence).

4. **FR-A4 (Deterministic factual scaffolding for TL;DR)**: Every fact the TL;DR rests on — counts, dates, record names, "you have N …" — is computed deterministically from source records and passed to the LLM as ground truth. The LLM composes concise prose and prioritizes; it never introduces a fact of its own. — **Acceptance**: TL;DR counts/dates match the deterministic view model exactly across the eval corpus.

5. **FR-A5 (Binary anchor resolution)**: TL;DR + takeaways emit against a schema (counts/themes/action slots + `itemRefs[]`). Any named anchor must carry an `itemId` that resolves against `items[]`; the widget **drops** non-resolving anchors. No warn/withhold threshold band exists. — **Acceptance**: an eval case injecting a non-resolving anchor renders with that anchor removed, not warned.

6. **FR-A6 (Groundedness demotion)**: `GroundednessCheckService` is **not** a user-facing gate. It is retained (if at all) only as an eval/telemetry signal and never decides what the user sees. — **Acceptance**: no code path withholds or warns user-facing briefing content based on a groundedness score.

7. **FR-A7 (Eval family)**: A mixed-item corpus, aggregation-preference cases, grounding round-trip cases, and TL;DR-abstraction cases join the golden-utterance suite as CI merge gates. The deterministic channel renderer gets **unit** tests. — **Acceptance**: the eval suite is green as a required merge check; the mixed-item corpus asserts zero cross-pairing.

### Functional Requirements — Visual Redesign (Gate G-R5-D, D-8)

8. **FR-D1 (Prototype harness)**: Scaffold a `/prototype` harness in the `spaarke-prototype` repo for `Spaarke.DailyBriefing.Components` (`prototype-harness-setup` for a production component) with mocked briefing data (`prototype-harness-extend` for factories/presets), reusing the FR-A7 mixed-item corpus as fixtures. — **Acceptance**: harness runs locally with HMR and renders the briefing against realistic mixed-item data in light **and** dark.

9. **FR-D2 (UX principles honored)**: Design iterations satisfy the operator's UX parameters:
   - Fluent v9 design system + components; visually aligned with Power Apps model-driven apps
   - Purpose = a **glanceable answer to "what are the most important things I need to be aware of"** across the system
   - **Not** a report/dashboard — no columns/rows table paradigm; visuals are permitted where they convey information better
   - Every briefing point is **easily navigable to its detail**
   - **TL;DR** and **"Critical Today"** are prominently surfaced/highlighted (the High-Priority section is presented under the user-facing "Critical Today" framing)
   - Narration is **concise, succinct, value-adding** — makes the point, not verbose
   — **Acceptance**: operator judges the harness design against these parameters at the G-R5-D sign-off.

10. **FR-D3 (Operator sign-off)**: The redesign is operator-approved **in the harness** before any production change. — **Acceptance**: sign-off recorded; screenshots archived in `projects/spaarke-daily-update-service-r5/notes/`.

11. **FR-D4 (Production port)**: The approved design lands by **extending the existing** `Spaarke.DailyBriefing.Components` (no parallel component tree) under Fluent v9 + ADR-021 design tokens, dark-mode verified. The D-1/D-2 fact contract is preserved — redesign changes presentation, never the data contract. Port lands **after** the FR-A1 deterministic view-model is stable. — **Acceptance**: browser UAT on spaarkedev1 shows the redesigned briefing; ADR-021 dark-mode check passes; no net-new component without a `BUILD-A-NEW-WORKSPACE-WIDGET.md` archetype justification.

### Functional Requirements — Hardening (Gate G-R5-C)

12. **FR-C1 (Choice-coercion runtime fix)**: `UpdateRecordNodeExecutor.CoerceFieldValue` gains metadata-driven coercion — when the mapping type is `string` but the target column is Choice/Boolean/Number, resolve via cached column metadata (label→option value, case-insensitive) instead of 500ing; unmatchable labels **fail loud** with the label + valid options in the error. — **Acceptance**: an Update Record write of a Choice label string succeeds; an invalid label returns a descriptive error listing valid options (not a 500).

13. **FR-C2 (`jps-validate` authoring check)**: Extend `jps-validate` Step 7.7 to flag `type:"string"` fieldMappings whose target column is Choice. Main-session edit (skill-directive hot path). — **Acceptance**: running `jps-validate` on a node with a string→Choice mapping emits a warning.

14. **FR-C3 (fieldMapping sweep)**: Audit existing playbook nodes' fieldMappings for the string→Choice pattern (note 06 cases 3/4); restore `sprk_documenttype` to the Profile Document node once coercion ships. — **Acceptance**: sweep documented; `sprk_documenttype` mapping restored and writing successfully.

15. **FR-C4 (Collaborator-scope fix)**: Revert the collector's membership-resolver bypass; **re-flip** `DailyBriefingCollectorTests.CollectAsync_OwnershipGate_UsesOwnerScopedQueryExpressions_ResolverBypassed` (rewritten in PR #558 to deliberately assert the bypass) to re-assert resolver routing; add a collaborator smoke test (a `sprk_assignedattorney1` user sees an assigned, non-owned matter). — **Acceptance**: assigned attorney sees their non-owned matter in the briefing (browser UAT); the re-flipped test + collaborator smoke test pass.

16. **FR-C5 (Collector de-duplication)**: The collector no longer emits duplicate items. — **Acceptance**: a scenario with an item reachable via two collection paths appears once.

17. **FR-C6 (Client-helper tests)**: Jest tests for `NarrativeCitedText.buildSegments`, `classifyDueDate`, `isEmptyResponse`, `useInlineTodoCreate`. — **Acceptance**: tests land at KEEP paths and pass (TEST-MODIFYING rigor).

18. **FR-C7 (Query helper collapse)**: Collapse the 7 `QueryHighPriority*` helpers into one spec-driven method. — **Acceptance**: single method covers all prior cases; existing behavior preserved under test.

19. **FR-C8 (Perf + comment fixes)**: Promise-cache the primary-contact lookup; fix the truncation comment. — **Acceptance**: primary-contact lookup resolves once per run; comment accurate.

20. **FR-C9 (OData convention + audit)**: Document the OData naming convention in `docs/standards/` ("binding a lookup → PascalCase SchemaName; filtering/selecting → lowercase LogicalName") and run a repo-wide grep audit for the PascalCase `@odata.bind` pattern, fixing any **in-use** occurrences (EventDetailSidePane instance excluded — deferred). — **Acceptance**: convention doc exists; grep audit report in `notes/`; in-use violations fixed.

### Functional Requirements — Deploy (D-6)

21. **FR-E1 (Deploy convention)**: Adopt **master-sync-first** as a binding project convention for manual worktree deploys; any new/touched deploy script emits a one-line warning when the branch is behind `origin/master`. No new deploy mechanism (reserved-window flag-file rejected). — **Acceptance**: touched deploy scripts warn on branch-behind; convention stated in PR descriptions of deploy-touching work.

### Non-Functional Requirements

- **NFR-01 (Publish size)**: Verify BFF publish size on every BFF-touching task. Ceiling ≤60 MB compressed; baseline ~49.63 MB incl. PDBs (state PDB convention when reporting). D-1 should *reduce* per-run tokens (measurable win, not a size regression).
- **NFR-02 (Eval merge gate)**: The eval suite (golden-utterance + new families) is green as a required merge check.
- **NFR-03 (Telemetry)**: NFR-07 identifiers-only holds; no PII in briefing telemetry.
- **NFR-04 (Dark mode)**: ADR-021 dark-mode verification on every widget change (no hard-coded colors).
- **NFR-05 (Retirement verification)**: grep-zero for `BRIEF-NARRATE-CHANNEL` code consumers before merge.
- **NFR-06 (Evidence)**: Capture before/after per-run token counts (054 metering), zero cross-pairing on the mixed-item corpus, and the collaborator-scope smoke as gate evidence.
- **NFR-07 (Gate medium)**: Gates G-R5-A/C are operator-executed **browser UAT on spaarkedev1**; G-R5-D is **operator harness sign-off** (screenshots archived). curl/tests/logs never satisfy a gate.
- **NFR-08 (Test rigor)**: Any task touching `tests/**` runs at TEST-MODIFYING rigor (code-review + adr-check unconditionally).

## Technical Constraints

### Applicable ADRs

- **ADR-039 / ADR-040** (Accepted) — coded-composite + catalog-Action architecture; no new playbooks/dispatch/manifest tables
- **ADR-013 / ADR-037** (amended) — AI service boundaries; PublicContracts facade for CRUD↔AI
- **ADR-015 / ADR-016** — briefing data tiers + budgets
- **ADR-021** — Fluent design tokens (reason/priority affordance colors; dark mode)
- **ADR-022** — PCF platform libraries
- **ADR-024** — todo regarding model
- **ADR-029** — publish per-task size verification (baseline 49.63 MB incl. PDBs)
- **ADR-032** — Null-Object kill-switch (any feature-gated service)
- **ADR-038** — test shapes; new tests land at KEEP paths; TEST-MODIFYING rigor

### MUST Rules

- ✅ MUST render item-level facts deterministically; MUST NOT invoke an LLM for item rows
- ✅ MUST compute all TL;DR facts deterministically and pass them as ground truth
- ✅ MUST drop non-resolving TL;DR anchors; MUST NOT gate user-facing content on a groundedness score
- ✅ MUST extend existing `Spaarke.DailyBriefing.Components`; MUST NOT introduce a parallel component tree or new widget framework
- ✅ MUST use Fluent v9 + ADR-021 tokens; MUST verify dark mode
- ✅ MUST fail loud (not 500) on unmatchable Choice labels in `UpdateRecordNodeExecutor`
- ✅ MUST re-flip the PR #558 resolver-bypass test when reverting the bypass
- ✅ MUST verify publish size and no new HIGH-severity CVE on BFF-touching tasks
- ✅ MUST state BFF placement decision (bff-extensions.md) in PR/design for any BFF addition

### Existing Patterns to Follow

- `HighPrioritySection` mini-report cards (R7 W12) — the deterministic-card precedent D-1 follows
- `docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md` — two-layer narrative-output pattern
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — widget archetypes (for any net-new component justification)
- `prototype-harness-setup` / `prototype-harness-extend` skills — D-8 harness scaffolding
- `dataverse-create-schema` — (only if the deferred Monitored-For round is ever activated; N/A in R5)

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-039 / ADR-013 | "No new capability on the frozen node-execution engine" | FR-C1 modifies `UpdateRecordNodeExecutor.CoerceFieldValue` | **A (project-scoped exception)** | The change is **defect-hardening** (Choice writes currently 500), not new capability. Documented in the PR description per §10 frozen-engine rule; code-review approves explicitly. |

> Aside from the frozen-engine defect exception above, no ADR tensions are anticipated. D-8 stays within existing component surfaces (ADR-021/022 apply without exception); the accuracy work strengthens ADR-039's coded-composite posture rather than challenging it.

## Success Criteria

1. [ ] **G-R5-A** — briefing item rows are 100% fact-derived; zero cross-pairing on the mixed-item corpus; TL;DR asserts only deterministic facts and drops non-resolving anchors — *Verify: browser UAT on spaarkedev1 + eval suite green*
2. [ ] **G-R5-D** — the redesigned briefing is glanceable, Fluent-v9/MDA-aligned, not report-like, with TL;DR + "Critical Today" surfaced and easy navigation to detail — *Verify: operator harness sign-off, then browser UAT of the ported design (light + dark)*
3. [ ] **G-R5-C** — assigned attorneys see their matters; Choice writes stop 500ing; no duplicate items; client-helper tests + OData audit landed — *Verify: browser UAT + passing test suite*
4. [ ] `BRIEF-NARRATE-CHANNEL` retired with grep-zero consumers — *Verify: grep audit*
5. [ ] Per-run token reduction demonstrated — *Verify: task-054 metering before/after*
6. [ ] Publish size within ceiling — *Verify: `dotnet publish` measurement per BFF task*

## Dependencies

### Prerequisites

- `GroundednessCheckService` auth fix (PR #567, 2026-07-08) — landed; enables its use as an eval/telemetry signal
- Deploy pipelines shipping master-only (2026-07-08) — landed; underpins D-6 disposition
- `spaarke-prototype` repo available for the D-8 harness (cross-repo work per owner clarification)

### External / Coordination

- **r2-core coordination** (`spaarke-ai-architecture-redesign-r2` active worktree): briefing work was removed from r2 by operator ruling; r5 owns `Services/Ai/Narrators/DailyBriefing*` + the frozen-engine executor fix, r2 owns shared internals (gate engine, Binder, Memory, Completion). Register both in `projects/INDEX.md`; run `/conflict-check` before each wave. If r2's Completion/OutcomeCard contracts land first, briefing action buttons may adopt them opportunistically — **not a dependency**.
- Catalog edits (`BRIEF-NARRATE-CHANNEL` retire, `BRIEF-NARRATE-TLDR` keep) via BA editor/MCP — mirror-first authoring + eval-case obligation + `OpenAiFunctionSchemaValidator`.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Groundedness threshold (D-2) | Warn-X/withhold-Y or measure-first? | **No threshold.** Existence is not probabilistic; LLM surfaces important already-true items + narrative only | D-2 rewritten: deterministic factual scaffolding + binary anchor resolution; groundedness demoted to eval/telemetry (FR-A4/A5/A6) |
| Monitored-For (D-3) | Value list + change-tracking? | **Deferred** entirely — prerequisites + not the material issue | D-3 out of scope; no schema deployed |
| EventDetailSidePane (D-5) | Ship standalone or in sweep? | **Deferred** — side-pane not currently in use | Excluded from FR-C9; grep audit still fixes in-use occurrences |
| Sweep scope | Keep the tech-debt sweep in R5? | **Keep all of it** — don't lose track of the items | Full D-4/D-5 in scope (FR-C1–C9, FR-E1) |
| Prototype tracking (D-8) | Cross-repo tasks or operator precursor? | **(a)** R5 owns cross-repo tasks | Phase D tasks scaffold/iterate the harness in `spaarke-prototype`, then port back |
| Visual north-star (D-8) | Reference product? | None; use stated UX parameters (Fluent v9 + MDA, glanceable "what matters", not a report, easy nav-to-detail, TL;DR + "Critical Today" surfaced, concise narration) | FR-D2 encodes these as design acceptance parameters |

## Assumptions

*Proceeding with these unless the operator redirects:*

- **Mixed-item corpus**: Assuming R5 **builds it fresh** in Phase A and reuses it as the D-8 harness fixtures; will confirm existing eval assets during pipeline resource discovery — affects FR-A7 / FR-D1 sizing
- **G-R5-D sign-off medium**: Assuming screenshots archived in `notes/` + optional live harness review; operator approval is the gate — affects FR-D3
- **Phase shape**: Phase 0 (OData doc + grep audit) → Phase A (accuracy core) → Phase D (harness redesign, parallel with A; port after A's view-model stabilizes) → Phase B (coercion + hardening sweep) → wrap-up (`/test-diet` + `/defer`)
- **"Critical Today"** is the user-facing label for the existing High-Priority section (presentation/naming, not a new data surface)

## Unresolved Questions

*None blocking. To confirm during pipeline resource discovery (not before):*

- [ ] Whether a reusable mixed-item eval corpus already exists in the golden-utterance suite (assumption: build fresh) — Blocks nothing; refines Phase A task count
- [ ] Exact current channel/section inventory in `Spaarke.DailyBriefing.Components` (deterministic renderer surface) — Blocks nothing; pipeline discovery enumerates it

---
*AI-optimized specification. Original design: `design.md` v0.2.*
