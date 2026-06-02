# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Milestone snapshot 2026-06-02 — Wave B closed + landed on master.

---

## 🎯 Milestone — 2026-06-02 — Wave B complete, on master

**The dispatch-architecture work that this entire project was scoped to deliver is COMPLETE and LIVE.**

### Status

| Dimension | State |
|---|---|
| **Wave B objective** | ✅ predict-matter-cost@v1 produces real `DeclineResponse` (reason=insufficient-evidence, confidenceInDecline=0.95, structured suggestedActions) — NOT scaffold |
| **Spaarke Dev BFF** | ✅ Serving Wave B code (deployed manually via `Deploy-BffApi.ps1` earlier in session); verified HTTP 200 + correct response shape |
| **Master** | ✅ Wave B merged via PR #330 (2026-06-02 16:57 UTC). 14 commits + merge. Branch protection passed all 8 status checks. |
| **Worktree branch** | ✅ Synced with master (fast-forward to `1bdc0a33`). 0 commits ahead/behind. |
| **Production auto-deploy** | ❌ Failed on pre-existing flaky tests (`EmlGenerationService`, `RegisterContainerTypeTests`) — UNRELATED to Wave B. Same pattern as previous 2 deploys (#310, #318). |
| **Dataverse state** | ✅ Spaarke Dev has: `sprk_executoractiontype` field on `sprk_analysisactiontype`; 17 backfilled rows (11 = 0, 6 = 70/80/90/100/110/120 + 60 AgentService); 7 INS-* action rows with JPS prompts + lookup FKs; 8 `predict-matter-cost@v1` playbook nodes correctly wired (Guid `fd584739-965e-f111-ab0c-7c1e521b425f`) |

### Owner clarifications captured in master

1. **Unmanaged-everywhere** is current Spaarke practice — ADR-027 amended 2026-06-02 (commit `d41daf93`) with "Read this BEFORE the body" block. Managed-solution mandates suspended; preserved as future direction.
2. **No managed solutions yet** — memory saved at `~/.claude/projects/.../memory/spaarke-unmanaged-solutions.md` for future sessions.
3. **JPS authoring goes through `/jps-action-create` skill** — owner direction 2026-06-02; captured in Wave B re-scope.
4. **Wave B sequenced FIRST** (per WB-1 owner clarification 2026-05-30) — done.

### Wave B closeout artifacts (all on master)

| Path | What |
|---|---|
| `decisions/D-01-wave-b-root-cause-corrected.md` | Full empirical investigation + closure summary + 3 fixes |
| `notes/handoffs/wave-b1-investigation-notes.md` | D-01 Q1/Q2/Q3 resolution |
| `notes/handoffs/wave-b5-smoke-results.md` | Live smoke iteration table + SC-01 met |
| `notes/drafts/wave-b-action-codes.md` | 7 INS-* JPS prompt drafts + Guid map + lookup-row Guids |
| `scripts/Setup-InsightsEngineSchema.ps1` | NEW — idempotent setup for other devs |
| `scripts/Deploy-Playbook.ps1` | UPDATED — action-code wiring lint |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs` | UPDATED — reads ActionType from lookup target |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` | UPDATED — template substitution + non-AI-node action-FK dispatch |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/InsightsOrchestrator.cs` | UPDATED — derive matterId/projectId/invoiceId from Subject |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json` | UPDATED — actionCode per node |
| `.claude/adr/ADR-027-...md` + `docs/adr/ADR-027-...md` | AMENDED — unmanaged-everywhere |
| `.claude/constraints/bff-extensions.md` | UPDATED — §F.4 Deploy Coordination Across Parallel Projects |

### Project state

| Wave | Status |
|---|---|
| **B** (Unblock synthesis) | ✅ COMPLETE — all 6 tasks closed |
| **A** (Foundations) | 🔲 NEXT — 6 parallel-safe design-doc tasks (010-015) |
| **C** (JPS compliance) | 🔲 Depends on A4, A5 |
| **D** (2D taxonomy + multi-entity) | 🔲 Depends on C + A3, A6 |
| **E** (Hybrid + Assistant) | 🔲 Depends on D6 |
| Wrap-up | 🔲 After E |

### Open follow-ups (cosmetic — do NOT block Wave A-E)

1. **`{have}` / `{need}` token substitution in DeclineToFindNode explanation** — DeclineToFindNode has its own template engine (not the orchestrator's `{{var}}` substitution). When checkSufficiency's gap analysis flows through, the explanation tokens should resolve but they don't. Field-mapping issue — minor.
2. **`minimumEvidenceNeeded` dict empty** — `EvidenceSufficiencyNode.ExtractCount` doesn't find `totalCount` field on `IndexRetrieveNode`'s structured output. Field-name mismatch between rule's `countFrom` and the executor's emitted shape. Minor.
3. **CI deploy failures (#310, #318, #26835063719)** — pre-existing pattern unrelated to Wave B. Likely flaky tests (EmlGenerationService special-char test; RegisterContainerType URL validation test). Worth flagging to whoever owns those modules.

### How to resume in the next session

1. Read this file (`projects/ai-spaarke-insights-engine-r2/current-task.md`) for the milestone state above.
2. Wave B is done. Wave A is next — start any of 010-015 (all parallel-safe; A3 task 012 has the most downstream dependencies and is the recommended starting point).
3. The branch is on master + worktree synced. Just say "start Wave A" or "work on task 012" to continue.
4. If a new dev joins and needs to bring their env up to baseline: `pwsh ./scripts/Setup-InsightsEngineSchema.ps1 -DataverseUrl https://<env>.crm.dynamics.com -DryRun` first, then apply.

---

## Wave sequencing (per owner direction WB-1)

Wave B FIRST ✅ → A → C → D → E → wrap-up.

| Wave | Tasks | Status |
|---|---|---|
| **B** (Unblock synthesis) | 001–006 | ✅ COMPLETE — all 6 tasks closed, D-01 closed, SC-01 met live |
| **A** (Foundations) | 010–015 | 🔲 NEXT |
| **C** (JPS compliance) | 020–024 | 🔲 |
| **D** (2D taxonomy + multi-entity) | 030–036 | 🔲 |
| **E** (Hybrid + Assistant) | 040–043 | 🔲 |
| Wrap-up | 090 | 🔲 |

---

*Compacted 2026-06-02 — milestone is "Wave B end-to-end COMPLETE + on master". Resume with Wave A.*
