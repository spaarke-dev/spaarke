# Spaarke Daily Update Service R5 — Implementation Plan

> **Status**: ✅ Complete (2026-07-10) — all phases (0/A/B/D/E) + wrap-up shipped to master (PR #611); operator-UAT confirmed. Deferred items filed as issues #650/#651/#652.
> **Created**: 2026-07-08
> **Source**: `spec.md` (21 FRs) ← `design.md` v0.2
> **Baseline**: synced to `origin/master` 2026-07-08 (incl. r2-core merges #580/#582); BFF build green (0 errors)

## Executive Summary

Two headlines — make the Daily Briefing **accurate by construction** (deterministic item rows + deterministic-fact TL;DR) and **appealing** (visual redesign via the `/prototype` harness) — plus a bounded hardening sweep and a deploy convention. Monitored-For schema and the unused EventDetailSidePane fix are deferred.

## Architecture Context

### Discovered Resources

**BFF (server) surfaces** — `src/server/api/Sprk.Bff.Api/Services/Ai/`
- `Narrators/DailyBriefingCollector.cs` — deterministic live-query collector; builds `items[]` / view model in `BuildNarrateRequest` (~1216); 7 `QueryHighPriority*Async` wrappers (344–427) → shared `QueryHighPriorityGenericAsync` (450); resolver-bypass at 216–248; `ClassifyAction` (574). **No LLM.**
- `Narrators/DailyBriefingNarrator.cs` — LLM layer. `BRIEF-NARRATE-TLDR` (61), `BRIEF-NARRATE-CHANNEL` (62). TL;DR single call 166–194; **per-channel LLM leg 197–231** (D-1 removes this).
- `Narrators/DailyBriefingCompositeService.cs` — ADR-039/040 dispatch boundary; `CollectAsync` 319–332.
- `Nodes/UpdateRecordNodeExecutor.cs` — `CoerceFieldValue` (417); String-typed→Choice-column passes verbatim (the FR-C1 defect).
- `Safety/GroundednessCheckService.cs` — wired ONLY into chat `SafetyPipelineMiddleware`, **not** the briefing path (FR-A6 = keep it that way).

**Client surfaces** — `src/client/shared/Spaarke.DailyBriefing.Components/src/`
- Item-level renderers: `NarrativeBullet.tsx`, `NarrativeCitedText.tsx` (`buildSegments` @107), `HighPrioritySection.tsx` (`actionToBadge`/`reasonToLabel`), `SubRow*.tsx`, `ActivityNotesSection.tsx`
- TL;DR renderer: `TldrSection.tsx`
- Hooks: `useInlineTodoCreate.ts`, `useBriefingRender.ts` (`isEmptyResponse` @60)
- Top composer: `DailyBriefingApp.tsx`

**Tests / eval**
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingCollectorTests.cs` — `..._ResolverBypassed` @238 (FR-C4 re-flips this)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/UpdateRecordNodeExecutorTests.cs`
- `tests/integration/contract/Eval/golden-utterances.json` + `GoldenUtteranceEvalSuiteTests.cs` (dispatch-oriented schema; mixed-item corpus likely needs its own fixture)

**Skill / catalog / cross-repo**
- `.claude/skills/jps-validate/SKILL.md` Step 7.7 @179 (source-schema check; FR-C2 extends — main-session edit)
- Catalog: `BRIEF-NARRATE-CHANNEL` / `BRIEF-NARRATE-TLDR` rows (BA editor/MCP)
- `spaarke-prototype` repo — D-8 harness (cross-repo)
- Unmerged dependency: `fix/daily-briefing-components-standalone-build` (shared-lib standalone build; needed for D-8 harness)

### Applicable ADRs

ADR-039/040 (coded-composite + catalog-Action), ADR-013/037 (AI boundaries, PublicContracts facade), ADR-015/016 (briefing tiers/budgets), ADR-021 (Fluent tokens, dark mode), ADR-022 (PCF platform libs), ADR-024 (todo regarding), ADR-029 (publish size ≤60 MB; baseline ~49.63 MB incl PDBs), ADR-032 (Null-Object kill-switch), ADR-038 (test shapes; TEST-MODIFYING rigor).

### ADR Tensions

1 tension — FR-C1 modifies the frozen `UpdateRecordNodeExecutor` → **Path A (project-scoped exception)**: defect-hardening (Choice writes 500), not new capability; documented in PR per §10 frozen-engine rule.

## Phase Breakdown

### Phase 0 — Quick fixes (no dependency; parallel-safe)

- **0.1** OData naming convention doc in `docs/standards/` ("binding a lookup → PascalCase SchemaName; filtering/selecting → lowercase LogicalName") — FR-C9
- **0.2** Repo-wide grep audit for PascalCase `@odata.bind` pattern; fix in-use occurrences; report in `notes/`; EventDetailSidePane instance explicitly excluded (deferred) — FR-C9

### Phase A — Accuracy core (the headline; FULL rigor)

- **A.1** Remove per-channel LLM narrate leg from `DailyBriefingNarrator` (197–231); simplify composite to collect → view model → ONE TL;DR call → render — FR-A3
- **A.2** Deterministic item-row rendering: client renders channel/section rows from `items[]` source fields only; retire the narrative-bullet dependency on CHANNEL output — FR-A1
- **A.3** Retire `BRIEF-NARRATE-CHANNEL` Action (catalog data, BA editor/MCP); grep-zero code consumers (remove the constant + its call path) — FR-A2 / NFR-05
- **A.4** Deterministic factual scaffolding for TL;DR: compute counts/dates/names/"you have N" deterministically and pass to LLM as ground truth — FR-A4
- **A.5** Binary anchor resolution: TL;DR/takeaways schema with `itemRefs[]`; widget drops non-resolving anchors (`buildSegments`/`TldrSection`) — FR-A5
- **A.6** Groundedness guardrail: confirm no briefing code path gates user content on a groundedness score; document eval/telemetry-only usage — FR-A6
- **A.7** Eval family: mixed-item corpus (zero cross-pairing) + aggregation-preference + grounding round-trip + TL;DR-abstraction cases; wire as merge gate; unit tests for the deterministic renderer — FR-A7 / NFR-02 (TEST-MODIFYING)
- **A.deploy** BFF + code-page deploy to spaarkedev1 for G-R5-A browser UAT; capture before/after token metering (054) — NFR-06

### Phase D — Visual redesign (co-headline; parallel with A; cross-repo)

- **D.1** Scaffold `/prototype` harness in `spaarke-prototype` for `Spaarke.DailyBriefing.Components` (`prototype-harness-setup`) + mock data (`prototype-harness-extend`), reusing A.7 mixed-item corpus as fixtures; depends on shared-lib standalone build (coordinate `fix/daily-briefing-components-standalone-build`) — FR-D1
- **D.2** Design iterations honoring UX principles (Fluent v9 + MDA alignment; glanceable "what matters"; not a report/table; visuals where they help; easy nav-to-detail; TL;DR + "Critical Today" surfaced; concise narration); 2–3 variants — FR-D2
- **D.3** Operator harness sign-off; screenshots archived in `notes/` — FR-D3 (GATE G-R5-D)
- **D.4** Production port into existing shared-lib components under Fluent v9 + ADR-021 tokens (light + dark); preserve D-1/D-2 fact contract; lands AFTER A.2 view-model stable — FR-D4 / NFR-04
- **D.deploy** code-page deploy for G-R5-D browser UAT (light + dark)

### Phase B — Coercion + hardening sweep (FULL / TEST-MODIFYING rigor)

- **B.1** `CoerceFieldValue`: metadata-driven coercion when mapping Type=String but target column is Choice/Boolean/Number (cached column metadata, label→option value, case-insensitive); unmatchable fails loud with label + valid options — FR-C1 (frozen-engine Path A exception)
- **B.2** `jps-validate` Step 7.7 extension: flag `type:"string"` mappings targeting Choice columns (main-session edit) — FR-C2
- **B.3** fieldMapping sweep for the string→Choice pattern; restore `sprk_documenttype` to Profile Document node once coercion ships — FR-C3
- **B.4** Collaborator-scope fix: revert collector membership-resolver bypass; re-flip `..._ResolverBypassed` @238 to assert resolver routing; add collaborator smoke test (`sprk_assignedattorney1` sees assigned non-owned matter) — FR-C4 (TEST-MODIFYING)
- **B.5** Collector de-duplication — FR-C5
- **B.6** Jest tests for `buildSegments`, `isEmptyResponse`, `useInlineTodoCreate`, `actionToBadge`/`reasonToLabel` (corrected from `classifyDueDate`) — FR-C6 (TEST-MODIFYING)
- **B.7** Collapse 7 `QueryHighPriority*Async` wrappers into one spec-driven method (delegating to existing `QueryHighPriorityGenericAsync`) — FR-C7
- **B.8** Promise-cache primary-contact lookup; fix truncation comment — FR-C8
- **B.deploy** BFF deploy for G-R5-C browser UAT (collaborator-scope smoke)

### Phase E — Deploy convention (small; docs + script)

- **E.1** Adopt master-sync-first convention (project binding); add one-line branch-behind-`origin/master` warning to any touched deploy script — FR-E1

### Wrap-up

- **090** `/test-diet` reconciliation + `/defer` (capture deferred Monitored-For + EventDetailSidePane as tracked issues) + lessons-learned + README status → Complete

## Parallelization

- Phase 0 fully parallel (2 tasks, no deps)
- Phase A internal: A.1→A.2→A.3 sequential (same files); A.4/A.5 after A.1; A.7 after A.2/A.4/A.5
- Phase D runs parallel to A (harness = mocked data); D.4 (port) gated on A.2
- Phase B mostly parallel internally (distinct files) EXCEPT B.2 (`.claude/` — main-session sequential)
- Phase A and Phase B touch `DailyBriefingCollector.cs` (A: view model; B.4/B.5/B.7) — sequence to avoid same-file collisions

## Risks

- **Cross-repo D-8**: harness in `spaarke-prototype`; standalone-build fix unmerged — coordinate/land first
- **r2-core coordination**: `Services/Ai/` shared; run `/conflict-check` before each wave; register in `projects/INDEX.md`
- **Eval fixture format**: golden-utterances schema is dispatch-oriented; mixed-item accuracy corpus may need a new fixture + suite — size A.7 accordingly
- **Catalog retirement**: `BRIEF-NARRATE-CHANNEL` retire must be grep-zero and eval-case updated before merge

## References

- `spec.md`, `design.md` v0.2, `notes/inbound-from-r7/`
- `docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`, `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`
- `.claude/constraints/bff-extensions.md`, `.claude/adr/` (039/040/013/021/029/032/038)
