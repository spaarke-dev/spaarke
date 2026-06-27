# Spaarke AI Platform Unification R5

> **Status**: ✅ **CLOSED with known limitations (2026-06-06)** — wire-layer foundation shipped; renderer + duplicate-fire defects deferred to R6 architecture phase. See [`notes/lessons-learned.md`](notes/lessons-learned.md).
> **Created**: 2026-06-03
> **Closed**: 2026-06-06
> **Predecessor**: [spaarke-ai-platform-unification-r4](../spaarke-ai-platform-unification-r4/) — shipped to master at `18b9323f` on 2026-06-03
> **Successor**: [spaarke-ai-platform-unification-r6](../spaarke-ai-platform-unification-r6/) — architecture phase, in design as of close
> **Repo state at R5 kickoff**: master after PR #331 (R4) + PR #332 (Trivy cleanup) + PR #333 (skill update)

---

## Purpose

R5 began as a **user testing + feedback capture** round for SpaarkeAi functional requirements; the kickoff session pivoted to a full implementation project after design.md scoped a Summarize-document vertical slice + Insights tool integration. R5 closes after shipping that vertical slice end-to-end at the wire layer, plus the Phase 2 closeout work surfaced during the SC-18 SME walkthrough.

## Outcome

R5 shipped (master via PRs #345 / #354 / #359 / #361 / #362 / #364):
- Phase 1 platform extensions (session-files Azure Search index, RagIndexingPipeline session writes, ChatSession.UploadedFiles, FieldDelta SSE variant, Structured Outputs wiring, cleanup IHostedService, telemetry)
- Phase 2 vertical slice (SessionSummarizeOrchestrator, /summarize endpoint, InvokeSummarizePlaybookTool, StructuredOutputStreamWidget, PaneEventBus events, intent matcher, executeSummarizeIntent, sseToPaneEventBridge, FR-07 chat attachments with pdfjs v4 + FileList snapshot + cross-package File forwarding)
- Phase 2 closeout (WorkspaceTabManager.prependTab + Summary tab installer + auto-focus + 7 tests; JSONPath strip for widget schema compatibility)

R5 deferred to R6 (architecture phase):
- Schema-aware renderer for array/object fields (TL;DR + Entities currently render raw JSON fragments) → R6 Pillar 5
- Duplicate-fire (chat agent + workspace path both invoke summarize for `/summarize`) → R6 Pillars 5 + 8
- Insights renderer + clickable citations + confidence floor (tasks 026/027/028)
- DocumentViewerWidget upgrade (task 022); Context-pane execution-trace widget (task 037); Phase 3 polish (040–044)

See [`notes/lessons-learned.md`](notes/lessons-learned.md) for the full per-task → R6-pillar mapping (§6), the four architectural gap families surfaced (§2), and the patterns to carry forward (§5).

## Quick Links — Read These at Kickoff

The kickoff session should load these as primary context:

### R5-specific (this folder)
- [`notes/ground-truth-spaarkeai-state.md`](notes/ground-truth-spaarkeai-state.md) — **READ FIRST**: actual shipped behavior of `sprk_spaarkeai` as of R4 merge. Code-grounded survey (not architecture aspirations). Entry points, widget catalog, mount sources, FR-to-surface map, suggested initial test focus.
- [`notes/user-testing-kickoff.md`](notes/user-testing-kickoff.md) — scoping questions + recommended kickoff prompt for the new session

### Canonical architecture (master)
- [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — two-wrapper architecture (Dashboard wrapper vs Direct widget wrapper); four mount sources; required reading
- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — end-to-end pipeline (cold-load → render → persistence)
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — `@spaarke/*` library inventory + PaneEventBus contract
- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — 5 archetypes with decision tree (R4 rewrite)

### Requirements baseline
- [`projects/spaarke-ai-platform-unification-r4/spec.md`](../spaarke-ai-platform-unification-r4/spec.md) — R4 FRs/NFRs/DRs/PRs (14/9/7/2)
- [`projects/spaarke-ai-platform-unification-r4/notes/lessons-learned.md`](../spaarke-ai-platform-unification-r4/notes/lessons-learned.md) — what shipped, what surprised, what was deferred (R5 backlog items live here)
- [`projects/spaarke-ai-platform-unification-r3/spec.md`](../spaarke-ai-platform-unification-r3/spec.md) — predecessor FRs still in force
- [`projects/spaarke-ai-platform-unification-r3/notes/lessons-learned.md`](../spaarke-ai-platform-unification-r3/notes/lessons-learned.md) — R3 lessons recommended

## Sibling Projects (cross-references)

| Project | Status | Relationship to R5 |
|---|---|---|
| [`projects/spaarke-iframe-wizard-pattern-enhancement/`](../spaarke-iframe-wizard-pattern-enhancement/) | Design (pre-planning) | Captures cross-surface "mount workspace tab" pattern; W-5 implementation gap surfaced in R5 testing may feed this project's prioritization |
| `projects/spaarke-graph-sdk-kiota-upgrade-r1/` (placeholder) | Not started | Deferred Kiota CVE-2026-44503 lives here; no R5 dependency unless backend FRs test against Graph SDK behavior |
| [`projects/trivy-cve-backlog-cleanup-r1/`](../trivy-cve-backlog-cleanup-r1/) | ✅ Shipped 2026-06-03 | Trivy backlog cleared during R4 close-out; security gate is back ON; no R5 dependency |

## Graduation Criteria (final state at close)

- [x] Scope agreed — Summarize-document vertical slice + Insights tool integration (decided at kickoff via design.md)
- [x] Phase 1 platform extensions shipped — session-files index, ChatSession.UploadedFiles, FieldDelta SSE variant, Structured Outputs, cleanup job, telemetry
- [x] Phase 2 vertical slice shipped at wire layer — endpoint, orchestrator, tool, widget, events, intent matcher, executor, bridge, FR-07 attachments
- [x] Phase 2 closeout shipped — WorkspaceTabManager.prependTab + Summary tab installer
- [⚠️] SC-18 SME walkthrough completed — 9 defect cycles surfaced; architectural gaps drove pivot to R6 rather than continued patching
- [x] R6 successor project scoped — see [`../spaarke-ai-platform-unification-r6/design.md`](../spaarke-ai-platform-unification-r6/design.md) (9 pillars, ~6 weeks, in design)
- [x] Lessons-learned captured — see [`notes/lessons-learned.md`](notes/lessons-learned.md)

## Changelog

| Date | Entry |
|---|---|
| 2026-06-03 | Project folder created (operator). README + ground-truth survey + kickoff prompt drafted post-R4 close. Kickoff to happen in a new session with the documents in `notes/` loaded as primary context. |
| 2026-06-03 (late) | Kickoff session. design.md scoped Summarize-document vertical slice + Insights tool integration (3 phases, ~36–44 tasks). spec.md + plan.md + CLAUDE.md authored; 37 task POMLs generated 2026-06-04. |
| 2026-06-04 | Phase 1 shipped (PR #345 tasks 001–009). Phase 2 partial. |
| 2026-06-04 → 2026-06-05 | SC-18 SME walkthrough cycles 1–4: tid claim shape, upload-not-indexed, Tags=null, 7 customer-corpus-only field rejections. Fixed via PRs #354 / #359 / #361. |
| 2026-06-05 | SC-18 cycle 5: two-upload-paths divergence. Task 036 (inline holding + extensible intent matcher + deterministic Summarize promotion) shipped via PR #362. |
| 2026-06-05 → 2026-06-06 | SC-18 cycles 6–9: FileList silent mutation, pdfjs v4 worker missing, misleading preview + missing Workspace Summary tab, JSONPath-vs-schema-key mismatch. Fixed + task 038 shipped via PR #364. |
| 2026-06-06 | Architecture chat session surfaced systemic gaps (persona hardcoded, tool registry ignored, playbook FK bypassed, schema-aware rendering implicit, workspace/assistant one-way). R6 design.md authored (9 pillars, ~6 weeks). R5 closed with known limitations; remaining work deferred to R6. |
