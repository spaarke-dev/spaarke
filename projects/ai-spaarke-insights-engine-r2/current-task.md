# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Milestone snapshot 2026-06-02 — Wave A complete, all 6 design docs landed.

---

## 🎯 Milestone — 2026-06-02 — Wave A complete (foundations)

**All 6 Wave A foundation design docs landed in a single parallel dispatch (6 sub-agents, ONE message).** Zero `.claude/` write boundary hits; zero retries needed.

### Status

| Dimension | State |
|---|---|
| **Wave B** (Unblock synthesis) | ✅ COMPLETE on master (PR #330, 2026-06-02 16:57 UTC) |
| **Wave A** (Foundations) | ✅ COMPLETE — all 6 tasks closed via parallel dispatch 2026-06-02 |
| **Worktree branch** | ✅ Synced with master; Wave A staged (10 modified, 4 untracked design docs, 1 untracked notes dir) |
| **Wave A sequencing** | Single-shot parallel — 6 sub-agents via Agent tool, ONE message, all completed independently with no inter-agent collisions |
| **Permission boundary** | ✅ Zero `.claude/` writes attempted by sub-agents — design-doc tasks scoped entirely under `projects/ai-spaarke-insights-engine-r2/` + `docs/architecture/` + `docs/guides/` |

### Wave A closeout artifacts (this commit)

| Path | What |
|---|---|
| `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md` | UPDATED (A1/010) — §0a Terminology block + Phase 1.5 wave-status table + practice-area-from-ref-table anchor |
| `docs/guides/INSIGHTS-ENGINE-GUIDE.md` | UPDATED (A2/011) — §§4/5/6/7/7A/7B for practice areas, multi-entity, JPS prompt editing, /api/insights/search, intent classifier, Assistant integration |
| `design-a3-2d-taxonomy.md` | NEW (A3/012) — 2D taxonomy design (practice-area × document-type), N:N intersect entity, NULL `sprk_layer2actioncode` = structured gate-fail, 5 high-value pairs recommended |
| `design-a4-prompt-variants.md` | NEW (A4/013) — Hybrid variant pattern + `@vN` versioning + tenant-scoped variant rows; PR-1 invariant preserved |
| `design-a5-universal-ingest-jps.md` | NEW (A5/014) — 6-node universal-ingest playbook + parameterization schema + 2 PlaybookExecutionEngine gap patches surfaced for Wave C1 |
| `design-a6-multi-entity.md` | NEW (A6/015) — Config-catalog subject parser + `IDictionary<string, ILiveFactResolver>` registry + hybrid scope shape for spaarke-insights-index-v2 (NFR-08 back-compat) |
| `notes/spikes/engine-gap-analysis.md` | NEW (A5 supplement) — Ephemeral Wave C1 backlog with the 2 engine patches |
| `spec.md` | UPDATED (A3) — PA-2 added (CTRNS/IPPAT/BNKF), Q-D2-1 resolved |
| 6× task POMLs | UPDATED — status → completed, `<notes>` blocks added with decisions and downstream impact |
| `tasks/TASK-INDEX.md` | UPDATED — rows 010–015 marked ✅ |

### Open follow-ups (carried into Wave C/D — NOT blockers)

| ID | Item | Owner / Wave |
|---|---|---|
| **PA-2-Q1..Q4** | Owner sign-off on initial 3 practice areas (CTRNS / IPPAT / BNKF) — pending before Wave D2 (031) row creation | Owner / Wave D2 prerequisite |
| **Engine Gap #1** | `EvidenceSufficiencyNode` `predicate: "in"` membership rule (~15–20 LOC patch) | Wave C1 (020) implementor |
| **Engine Gap #2** | `PlaybookOrchestrationService.ExecuteNodeAsync` branch-aware skip (~25–40 LOC patch) | Wave C1 (020) implementor |
| **C2 scope clarification** | Wave C2 must RENAME existing 8 Wave B2 `INS-*` rows to `@v1` suffix AND create new `INS-L1-CLASS@v1` for universal-ingest Layer 1 | Wave C2 (021) implementor |
| **Q-A4-1** | Variant pattern choice (parametric default vs. variant-row escape hatch) per task; A4 design supports either — defer per-Insights-action | Wave C2 (021) per-row decision |

### Project state

| Wave | Status |
|---|---|
| **B** (Unblock synthesis) | ✅ COMPLETE — on master |
| **A** (Foundations) | ✅ COMPLETE — all 6 design docs landed via parallel dispatch 2026-06-02 |
| **C** (JPS compliance) | 🔲 NEXT — 5 tasks (020–024); C-G2 (020 serial, ~2d) is the critical-path entry; depends on A4 (013) + A5 (014) — both ✅ |
| **D** (2D taxonomy + multi-entity) | 🔲 — Depends on C + A3 (012 ✅) + A6 (015 ✅). PA-2 owner sign-off needed before D2 (031) |
| **E** (Hybrid + Assistant) | 🔲 — Depends on D6 (035) |
| Wrap-up | 🔲 — After E |

### How to resume in the next session

1. Read this file for milestone state above.
2. Wave A is done. Wave C is next — start with task 020 (C-G2 critical-path; C1 = author universal-ingest@v1 JPS playbook).
3. **Before Wave C1 starts**: implementor MUST read `design-a5-universal-ingest-jps.md` §3 (6-node coalescence), §6 (parameterization), §7 (engine gaps), and `notes/spikes/engine-gap-analysis.md`.
4. **Before Wave D2 starts**: owner sign-off on PA-2 (CTRNS / IPPAT / BNKF as the initial 3) — see `design-a3-2d-taxonomy.md` §8.

---

## Wave sequencing (per owner direction WB-1)

Wave B FIRST ✅ → A ✅ → C 🔲 → D → E → wrap-up.

| Wave | Tasks | Status |
|---|---|---|
| **B** (Unblock synthesis) | 001–006 | ✅ COMPLETE |
| **A** (Foundations) | 010–015 | ✅ COMPLETE |
| **C** (JPS compliance) | 020–024 | 🔲 NEXT |
| **D** (2D taxonomy + multi-entity) | 030–036 | 🔲 |
| **E** (Hybrid + Assistant) | 040–043 | 🔲 |
| Wrap-up | 090 | 🔲 |

---

*Compacted 2026-06-02 — milestone: Wave A complete via parallel dispatch (6 sub-agents, ONE message, zero retries). Resume with Wave C1 (task 020).*
