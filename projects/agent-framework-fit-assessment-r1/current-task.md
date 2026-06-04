# Current Task — Agent Framework Fit Assessment R1

> Tracks ACTIVE task only. History lives in `TASK-INDEX.md` and per-task `.poml` files.

---

**Active task**: none — Phase 4 complete (tasks 000, 001, 002, 003, 004, 005 all ✅). Ready for Phase 5 (synthesis).
**Next task**: task 006 — **synthesis = the canonical assessment document**. This is the project's primary deliverable.

**How to start**: from a fresh session, type `work on task 006` and the harness will invoke `task-execute` with the POML.

---

## Last completed task

### Task 005 — Deployment + migration analysis
- **Output**: `projects/agent-framework-fit-assessment-r1/notes/05-deployment-and-migration.md` (476 lines)
- **Commit**: `45b93668`

---

## Key findings for task 006 synthesis (carry forward — these drive §6, §7, §8 of the doc)

### Deployment-model recommendations (drives synthesis §6)

| Surface | Verdict (task 004) | Deployment model (task 005) | Confidence |
|---|---|---|---|
| **S1 SprkChat** | PARTIAL (gated on #6268) | **In-process BFF** — ADR-013 criteria (1)+(2) fail decisively (latency + transactional coupling) | HIGH |
| **S3 Builder** | PARTIAL | **In-process BFF** — criterion (4) fails (duplication cost > value for small surface) | HIGH |
| **S5A Foundry wrapper (shipped)** | PARTIAL | **In-process BFF unchanged** — wrapper-only code simplification | HIGH |
| **S5B canonical durable HITL** | **ADOPT** ⭐ | **Mixed — prototyping required before commitment** | **LOW** (F12 gap) |
| **S7 Insights MCP** | PARTIAL | Deferred to D-A20 contract; preliminary read: ADR-013 criteria likely pass | MED |
| **S8a SessionSummarizationService** | PARTIAL | Fold into S1 perimeter (in-process BFF) | HIGH |
| **S8b CapabilityRouter** | PARTIAL | Fold into S1 perimeter (in-process BFF) | HIGH |
| **S2 JPS** | DON'T ADOPT | no migration; no deployment change | — |
| **S4 Background jobs** | DON'T ADOPT | no migration; no deployment change | — |
| **S6 M365 Copilot** | DON'T ADOPT | no migration; no deployment change | — |

### S5B is the central undecided question

All four ADR-013 §"Exceptions" criteria PASS for S5B — non-BFF deployable is legitimately permitted. But three candidates (**Workflows-in-BFF · Workflows-in-Function · Foundry-hosted**) cannot be ranked with current sources. Drivers of uncertainty:
- **F12 evidence gap** (task 003): no `/hosting/` Learn page; GitHub Issue #6308 in active triage
- **Foundry SKU costs** UNKNOWN
- **VM-isolation requirement** UNKNOWN (this is also task-004 open question #1)

**Recommendation for synthesis §6**: present S5B's deployment model as "ADR-013 criteria PASS — but choose between three models requires prototyping; do not pre-commit." Frame as honest evidence-thin conclusion, not adversarial fence-sitting.

### Publish-size impact (drives synthesis §7)

- **Baseline**: 45.65 MB (post-Outcome-A from `bff-extensions.md`/BFF extraction assessment baseline)
- **`Microsoft.Agents.AI 1.0.0-rc1` is already referenced** in `Sprk.Bff.Api.csproj:29-33` (zero source usage per task 001) — so S1/S3/S5A/S8a/S8b lifts to actual use have **net-zero publish-size impact**
- **S5B only**: adds `Microsoft.Agents.AI.Workflows` + possibly `Hosting.A2A.AspNetCore` + `Foundry` glue (+1.5-6 MB cumulative, UNCERTAIN)
- **Worst case projection**: ~47-54 MB (well under 80 MB tolerance from `.claude/constraints/bff-extensions.md`)

### Shared infrastructure changes (one cost amortized across surfaces)

S1 + S3 + S8a + S8b all benefit from **one shared change**: lift middleware from per-instance decoration of `ISprkChatAgent` to canonical `chatClient.AsBuilder().Use*().Build()` composition. Task 001 flagged this as "the biggest single migration vector for S1." Frame this as **one cross-cutting change** in the migration cost summary, not four independent migrations.

### Risk register (10 risks; drives synthesis §7) — top 3 HIGH-severity

1. **R1 (HIGH)** — Issue #6268 unresolved; affects S1's canonical multi-tool streaming workload
2. **R2 (HIGH)** — F12 durable-hosting evidence is thin; pre-commitment on S5B's hosting model is design-by-assumption
3. **R9 (HIGH)** — S5B mis-scoping risk: framing as "small framework adoption" instead of "build multi-day legal workflows from scratch"

### Migration cost (drives synthesis §7)

**8-17 person-weeks for Phase 1+2+3** (Builder pre-work + shared infra middleware lift + per-surface S1-family lifts).
- **Excludes S5B** — greenfield, person-quarters not person-weeks
- **Excludes S7** — deferred to D-A20 contract
- **Confidence: LOW-MED**

### Open questions for synthesis §8 (carry from tasks 004 + 005)

1. **S5B VM-isolation requirement** (task 004) — determines Foundry-hosted vs Workflows-in-BFF/Function for the sole ADOPT verdict
2. **S5B prototyping scope** (task 005) — what does a 1-2 week S5B prototype need to validate before SPEC commitment? F12 hosting + Foundry SKU costs + workflow checkpointing under Spaarke load
3. **S1 wait-or-pilot timing for Issue #6268** (task 004) — wait for shipped 1.x fix, or pilot with feature flag + fallback now?
4. **S7 D-A20 contract authoring** (task 004) — must address host library + deployment model + BFF seam (three UNKNOWNs)

---

## Prior phase findings (preserved for synthesis grounding)

- **Task 000** baseline: SHA `afa7834e` (2026-06-03 "1.9 release"); 34 primary sources, 100% within recency floor; AF 1.0 GA April 2026; #6268 RED FLAG for S1; Tool Approval is framework feature; Workflow HITL is framework-internal not Foundry-exclusive
- **Task 001** inventory: `Microsoft.Agents.AI` package referenced with ZERO source usage (the "half-adopted" framing has a literal evidence base); S1 only Extensions.AI user; middleware wraps `ISprkChatAgent` not `IChatClient`; 2 S8 surfaces discovered
- **Task 002** inventory: S5 BIMODAL — SPEC was wrong, corrected to A/B split; S6 uses M365 Agents SDK (distinct from Agent Framework); S7 Phase-2-deferred
- **Task 003** feature map: 12 features F1-F12; 19 distinct primary-source citations; 94.7% recency; F3 + F12 evidence-thin (F12 came due in task 005, forced LOW confidence on S5B hosting)
- **Task 004** decision matrix: 10 surfaces; 1 ADOPT / 5 PARTIAL / 4 DON'T ADOPT distribution passes anti-bias check decisively

---

## Citation discipline for task 006

- §10 Sources appendix is **mandatory** — table every primary URL + fetched date + referencing section
- ≥80% of primary-source citations dated 2026-04-01 onwards
- Every claim cites: notes/00 (live URLs) for feature facts; notes/01-05 (project-local) for analysis; ADRs/constraints for binding rules
- No claims citing ONLY the curated `knowledge/agent-framework/` snapshot — orientation only

---

## Phase status

- Phase 0 ✅ (task 000 — primary-source baseline)
- Phase 1 ✅ (tasks 001, 002 — inventory)
- Phase 2 ✅ (task 003 — feature map)
- Phase 3 ✅ (task 004 — decision matrix)
- Phase 4 ✅ (task 005 — deployment + migration)
- Phase 5 🔲 (task 006 — **synthesis = canonical assessment document**)
- Phase 6 🔲 (tasks 007, 008 — adversarial review + sign-off)
