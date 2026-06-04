# Insights Engine r2 — Lessons Learned (Phase 1.5)

> **Project**: `ai-spaarke-insights-engine-r2`
> **Closed**: 2026-06-04 (task 090)
> **Scope shipped**: Wave B (synthesis unblock) + Wave A (foundations) + Wave C (JPS compliance) + Wave D (2D taxonomy + multi-entity) + Wave E (hybrid consumption + Assistant) + Wave F (contract v1.1: SSE + clickable citations)
> **Total**: 29 in-scope tasks (Waves B/A/C/D/E + wrap-up) + 4 R5-requested Wave F tasks; 5 PRs (#330, #334, #336, #337, #339)
> **Predecessor**: `ai-spaarke-insights-engine-r1` (Phase 1 — plumbing prototype, shipped + deployed)
> **Successor**: `ai-spaarke-insights-engine-r3` (Phase 2 — scope TBD per owner discussion; primary input is [`PHASE-2-OUTLINE.md`](../PHASE-2-OUTLINE.md))

This is an honest retrospective. The intent is to lock proven conventions into r3 and call out specific misses so they don't repeat. No marketing tone.

---

## 1. What worked — preserve as conventions for r3

### 1.1 Wave B sequenced FIRST (synthesis unblock before foundations)

Owner direction WB-1 sequenced the half-day `predict-matter-cost@v1` unblock BEFORE the Wave A design docs. This was the right call. Wave B turned out to be a 1–2 day re-scope (D-01 path-b decision in `decisions/D-01-wave-b-root-cause-corrected.md`) because the original framing ("create 6 rows") missed two real issues: a configjson-wipe pattern in `Deploy-Playbook.ps1 -Force`, and an actionType-vs-actionCode mismatch. Catching this in Wave B — before Waves C/D/E touched the same dispatch surface — avoided a 12-hour debug spiral that would have surfaced mid-refactor with no clear isolation. **Convention for r3**: sequence the smallest end-to-end vertical FIRST when the prior phase shipped plumbing-only.

### 1.2 BFF Placement Justification §10 review held the line

Every wave that touched `Sprk.Bff.Api` passed the `.claude/constraints/bff-extensions.md` §F gate. Publish size moved from ~45.65 MB (Phase 5 baseline) DOWN to 44.13 MB (Wave F close, +0.03 MB delta from Wave E). Zero NuGet additions across all 5 waves. Zero new HIGH-severity CVEs. The §10 binding rule worked exactly as designed: no wave needed extraction to `Spaarke.Core`, every wave's placement decision was documented in either spec.md "BFF Placement Review" or the per-task POML output. **Convention for r3**: keep §10 review as a hard gate; the cost is low and the architectural drift it prevents is real.

### 1.3 F1 spike pattern (Wave F) — binding scope decisions BEFORE parallel execution

Wave F introduced a new dispatch shape that worked exceptionally well for additive contract bumps: **Round 1 = serial spike** (task 050 — `notes/spikes/wave-f-streaming-citation-spike.md`, 6 sections A–F producing binding decisions); **Round 2 = parallel implementation** (051 SSE + 052 citations); **Round 3 = serial docs** (053). The spike resolved the R5-escape-hatch question (plumbing cost = Small → SHIP FULL both observation + document citation href) and locked SSE event semantics BEFORE F2 and F3 dispatched in parallel. Result: 0 mid-stream rework, 0 scope drift, F2/F3 sub-agents shipped clean implementations matching the spike contract. **Convention for r3**: adopt spike + parallel implementation + docs by default for any contract-bump or cross-team additive feature.

### 1.4 Sub-agent parallel dispatch — 0 stuck-agent incidents in Waves E + F

Wave D task 032 hung for ~12 hours on an `mcp__dataverse__read_query` baseline fetch before detection (see §2.1 below). That lesson was memory-encoded as `feedback-detect-stuck-subagents.md` (check output file mtime/size on long-runners). Waves E + F ran 8 sub-agents total across 5 rounds with 0 stuck incidents. The detection heuristic + tighter sub-agent briefs (skip investigation when handoff docs exist; explicit ABORT-after-N-min instructions) held. **Convention for r3**: every parallel-dispatch wave gets an mtime-check protocol embedded in the dispatch plan; sub-agent briefs reference handoff docs instead of re-investigating.

### 1.5 Cross-worktree coordination via shared `notes/` doc

R5 (`spaarke-ai-platform-unification-r5`) and r2 ran concurrently with shared API surface touchpoints (the `/api/insights/assistant/query` contract). Coordination doc `C:/code_files/spaarke-wt-spaarke-ai-platform-unification-r5/projects/spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md` carried a §8 changelog tracking every contract change — Wave E ship (v1.0), Wave F ship (v1.1 with SSE + href). Both worktrees stayed aligned without merge conflicts. R5 raised the Wave F request via `notes/insights-engine-contract-v1.1-request.md`; r2 responded with `notes/wave-f-v1.1-mini-plan.md` integrating R5's responses. **Convention for r3**: when two active projects share an API surface, the producer maintains the canonical contract doc; the consumer maintains a coordination changelog; both reference each other.

### 1.6 `dotnet format whitespace verify` BEFORE push

PRs #336 (Wave D), #337 (Wave E), and #339 (Wave F) all had at least one Code Quality whitespace round-trip cost during the run-up. After PR #336, the lesson was memory-encoded as `reference-spaarke-ci-format-gate.md`. By PR #339, format-verify was run locally before every push. **Convention for r3**: every PR push protocol includes `dotnet format whitespace Spaarke.sln --verify-no-changes` as the LAST step before `git push`.

### 1.7 Owner-mediated escape hatches kept hard cases unblocked

Two examples: (a) R5 v1.1 §3 "plumbing-cost fallback" — if F1 spike showed citation href cost was Large, R5 would have accepted observation-only href; F1 said Small → both shipped. (b) Wave E3 Assistant team handoff — the Assistant team's contract review + implementation was descoped to `e3-assistant-team-handoff.md` rather than blocking Wave E close. The pattern: **when a hard case threatens the wave critical path, owner authorizes a fallback ahead of the spike; the spike resolves the binary; if Small/Resolvable, ship full scope, otherwise apply fallback without re-escalation.** **Convention for r3**: every wave with cross-team or research-dependent scope gets an explicit owner-authorized fallback in the design doc.

### 1.8 Spec.md Terminology section locked the meaning of JPS

Earlier drafts conflated JPS-the-schema with `PlaybookExecutionEngine`-the-code. The spec.md "Terminology" section (load-bearing per Owner Clarification PR-1) wrote out precise definitions for `JPS`, `PlaybookExecutionEngine`, `INodeExecutor`, `sprk_analysisaction`, `sprk_playbook`. Every Wave C/D/E task referenced these terms consistently. No Wave produced a spec drift. **Convention for r3**: when terms are load-bearing (project-specific or cross-cutting), lock them in spec.md upfront. Subsequent waves reference the section, not their own interpretation.

---

## 2. What didn't work — anti-patterns for r3 to avoid

### 2.1 Wave D task 032 — 12-hour sub-agent hang (MCP query timeout)

**What happened**: Task 032 (Wave D3 per-area/doc-type Layer 2 schemas) dispatched a sub-agent that hung on an `mcp__dataverse__read_query` baseline fetch. Output file remained 0 bytes for ~12 hours before owner noticed. The Wave D dispatch plan didn't include an mtime liveness check on parallel agents. **Cost**: half a working day lost; Wave D shipped late. **Mitigation now in place**: `feedback-detect-stuck-subagents.md` memory encoded; re-dispatch protocol skips investigation phases when handoff docs exist; tighter brief includes "ABORT after 2 min if verification hangs". **Held in Waves E + F**: 0 incidents. **r3 carry-forward**: every wave with parallel dispatch embeds the mtime check in its task POML's dispatch-plan section.

### 2.2 Asymmetric registration on `IInsightsAi` — Wave E flagged, Wave F deferred

`adr-check` during Wave E found that `IInsightsAi` is registered unconditionally, but transitively depends on services registered only in the compound-AI=ON branch of `AnalysisServicesModule.cs`. When compound-AI=OFF, callers hit a DI resolution failure (no `NullInsightsAi` facade), violating ADR-032 Null-Object Kill-Switch P3. Wave E inherited the issue from r1; Wave F kept scope tight and deferred. **Why this matters**: the fix is small (mirror `NullRagService` pattern: P3 facade throwing `FeatureDisabledException`), but it's load-bearing architectural debt that should close before r3 adds capability. **r3 carry-forward**: Tier 1 item in `PHASE-2-OUTLINE.md` (§3.1); should be r3 wave 1 task 001 or 002.

### 2.3 Three CI flakes in PRs #337 + #339 — pre-existing test infrastructure latencies

PR #337 (Wave E) and PR #339 (Wave F) each tripped CI flakes on Windows runners:
- **Timing test ±0.5s tolerance**: `EmlGenerationService.GenerateEml_WithSpecialCharactersInSubject` (pre-existing per Wave B's memory note)
- **Post Cache race**: a GitHub Actions cache step race condition
- **FileSystemWatcher dispose NRE on `IntegrationTestFixture`**: commit `c70bbb7a` suppressed this; the underlying race condition lives in test infrastructure, not the Wave F code change

**Cost**: 3 separate fixes, ~30 min CI round-trip cost per fix. **Root cause**: sub-agents repeatedly reported "all tests pass" after running unit tests, but didn't detect integration-test failures in `Spe.Integration.Tests` until CI surfaced them. **r3 carry-forward**: extend sub-agent quality gates to run BOTH unit AND integration tests; review any new `BindConfiguration` for FileSystemWatcher race exposure (Tier 1 item §3.4).

### 2.4 Wave C2 prompt migration — initial baseline-fetch hang prevented parallel start

Initial Wave C2 dispatch hung trying to baseline existing prompts from Dataverse via MCP query. Re-dispatched with explicit "use `c2-prompt-migration-comparison.md` handoff doc as baseline; skip Dataverse baseline fetch" instruction. Same hang pattern as 2.1 above. Confirms the lesson: MCP queries are unreliable enough that sub-agent briefs should default to using pre-captured handoff docs instead of re-querying live Dataverse for baseline reference data.

### 2.5 r1 wrap-up scope ambiguity

Owner direction R1-1 explicitly excluded r1 wrap-up from r2 scope, but the spec.md Out-of-Scope item is the only signal. r1's task 090 still shows 🔲 in the r1 TASK-INDEX (verified absent from this worktree but referenced). For r3, the predecessor's wrap-up state should be a hard prerequisite check (or an explicit "wave-0 sync" task that verifies r1 wrap-up is closed before r3 starts).

---

## 3. What we'd do differently for r3

### 3.1 Adopt the F1 spike + mini-plan pattern by default

For any additive contract bump or cross-team feature, run the spike FIRST (binding scope decisions in 6 sections: signature, dispatch shape, downstream impact, error semantics, kill-switch interaction, test surface). Then parallelize implementation against the spike's contract. Then docs. This pattern produced Wave F's clean ship; should be the default shape for r3 waves 2+.

### 3.2 Sub-agent quality gates: run BOTH unit AND integration tests

Sub-agents reported `dotnet test src/server/api/Sprk.Bff.Api.Tests/` clean and called the task done. Integration tests in `tests/integration/Spe.Integration.Tests/` were only run in CI. For r3: every quality-gate step in `task-execute` runs BOTH `dotnet test` (full suite) AND the integration-test project where it exists. Cost: ~30 sec extra per task. Benefit: catches PR #337 + #339 class of failures before push.

### 3.3 Close asymmetric-registration debt BEFORE adding capability

Wave E's `IInsightsAi` finding (§2.2) is the load-bearing example. r3 wave 1 should mirror `NullRagService` for `NullInsightsAi` and any other unconditionally-mapped service surface. Pattern is well-established in `Configuration/FeatureDisabledException.cs` + `Configuration/FeatureDisabledResults.cs` (added 2026-06-01). Cost: half a day. Benefit: removes a category of latent runtime failures and unblocks ADR-032 compliance.

### 3.4 Embed mtime-check protocol in every parallel-dispatch wave

Wave D's hang showed that the harness's completion notification is reliable on clean exit but cannot detect a hung tool call. Going forward, every wave POML with a parallel dispatch section adds an explicit "if any sub-agent exceeds 2× the median peer runtime, `ls -la` its output file; 0 bytes / stale mtime → TaskStop + tighter re-dispatch". This is owner-side liveness monitoring; the harness will not do it for us.

### 3.5 Cross-worktree coordination contract: producer owns canonical doc, consumer owns changelog

The R5 ↔ r2 coordination pattern (§1.5) worked but was discovered ad-hoc. For r3: any time two active projects share an API surface, codify the producer/consumer split in the design.md up front. Producer publishes canonical contract; consumer maintains coordination changelog in its own notes/. Both reference each other.

### 3.6 Preserve owner-mediated escape hatches in scope docs

Wave F's R5 v1.1 §3 fallback was the model. Bake "if cost > threshold, fall back to X" decisions into the design doc BEFORE the spike runs. This makes the spike a binary decision tool rather than a re-negotiation surface.

---

## 4. Quantitative summary

| Metric | Value | Notes |
|---|---|---|
| Total in-scope tasks | 29 | Plus 4 R5-requested Wave F tasks |
| PRs shipped | 5 | #330 (B), #334 (A+C), #336 (D), #337 (E), #339 (F) |
| Wall-clock duration | ~5 days | Far below the 5–7-week spec estimate; aggressive parallelism + sub-agent dispatch carried the win |
| Sub-agent dispatches | ~25 | Across all waves |
| Stuck-agent incidents | 1 | Wave D task 032; memory-encoded; 0 incidents after |
| CI flake fix round-trips | 3 | Across PRs #337 + #339; root-cause = test infrastructure latencies |
| Publish size delta | −1.52 MB | 45.65 MB (Phase 5 baseline) → 44.13 MB (Wave F close); within 60 MB ceiling |
| New NuGet packages | 0 | All 5 waves |
| New HIGH-severity CVEs | 0 | All 5 waves |
| §3.5 facade-grep gate | clean | All 5 waves |
| New tests added | 25+ | Wave F alone = 25 (8 SSE + 17 href); Waves D + E added ~30 more |
| Architectural debt opened | 1 | Asymmetric `IInsightsAi` registration — deferred to r3 Tier 1 |

---

## 5. Spec.md success criteria — final disposition

| SC | Description | Status | Notes |
|---|---|---|---|
| SC-01 | predict-matter-cost end-to-end returns real Inference or Decline | ✅ | Wave B5; structured Decline confirmed live on Spaarke Dev (Matter `da116923-d65a-f111-a825-3833c5d9bcb1`) |
| SC-02 | Per-practice-area routing for ≥3 areas | ✅ | Wave D2 + D4 (CTRNS, IPPAT, BNKF) via `InsightsActionRouter` |
| SC-03 | Multi-entity subject resolves live facts | ✅ | Wave D5 — Matter + Project + Invoice resolvers in `IDictionary<string, ILiveFactResolver>` registry |
| SC-04 | `/api/insights/search` returns ranked observations + citations | ✅ | Wave E1 task 040 |
| SC-05 | Assistant invokes either path via classifier | ✅ | Wave E2 + E3 tasks 041 + 042 (assistant-side implementation owner-deferred) |
| SC-06 | SME edits prompts in Dataverse without code deploy | ✅ | Wave C2 + A4 design; verified via `sprk_analysisaction.sprk_systemprompt` edit re-invocation pattern |
| SC-07 | `IngestOrchestrator` retired; JPS playbook runs | ✅ | Wave C3 task 022 |
| SC-08 | Zero `.txt` prompt files | ✅ | Wave C2 task 021 + verified by directory grep |
| SC-09 | Every node has `sprk_analysisaction` reference; lint enforces | ✅ | Wave B3 task 003 |
| SC-10 | Per-entity resolvers registered | ✅ | Wave D5 task 034 |
| SC-11 | §3.5 grep gate passes | ✅ | All 5 waves |
| SC-12 | `IInsightGraph` stub remains; Cosmos out of scope | ✅ | r1 stub preserved; Cosmos D-P17 re-deferred to r3 Tier 4 |
| SC-13 | Eval harness across ≥3 practice areas × 2 entity types | ✅ | Wave D7 task 036 — 19 synthetic fixtures |
| SC-14 | Live smoke runbook on Spaarke Dev | ✅ | Wave B5 + E covered |
| SC-15 | SME calibration ≥50 observations with measurable improvement | ⏭️ | **Carried to Phase 2** (r3). Requires production data volume + SME engagement; Phase 1.5 produced the substrate (Wave D6 index + Wave D7 fixtures) but the calibration loop is a Phase 2 deliverable per spec.md NFR-10 design. |

**14 of 15 SCs met. SC-15 explicitly carried to r3.**

---

## 6. Lessons by skill / convention

| Convention | Held | Notes |
|---|---|---|
| `task-execute` for every task | ✅ | All 29 tasks |
| `code-review` + `adr-check` at FULL rigor Step 9.5 | ✅ | Surfaced asymmetric-registration finding in Wave E |
| `repo-cleanup` skill at wave close | partial | Some intermediate notes accumulated; task 090 cleans the rest |
| Proactive `context-handoff` every 3 steps | ✅ | No mid-task compaction losses |
| Rigor-level declaration at task start | ✅ | All tasks; FULL for code, MINIMAL for docs, STANDARD for tests |
| `.claude/constraints/bff-extensions.md` §F load | ✅ | Every BFF-touching wave loaded it; §F.1 anti-pattern check caught the asymmetric-registration issue in Wave E |
| Cross-team coordination contract | ✅ | R5 ↔ r2 worked; pattern documented in §1.5 |

---

*This is the r2 retrospective. It will be referenced by r3's design.md when scoping Phase 2 waves. The primary forward input is [`PHASE-2-OUTLINE.md`](../PHASE-2-OUTLINE.md).*
