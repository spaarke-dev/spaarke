# Current Task State — Assistant Enhancements R1 ("Follow-Through")

| Field | Value |
|---|---|
| **Active task** | **002 — ⛔ BLOCKED** (escalated to owner; see [BLOCKED.md](BLOCKED.md)) |
| **Status** | 001 ✅. 002 hit a material as-built conflict (shipped `create-task`→`sprk_event`(Task), not `sprk_todo`; shipped creates use direct `dataverse.create_record`, NOT a pre-seeded wizard). Awaiting owner decision on options A/B/C/D. |
| **Next action** | Owner picks the create-flow resolution (BLOCKED.md). Unblocked alternatives to keep momentum: **030** (User Model producer) or **020** (action truthfulness) — both independent of the conflict. |
| **Completed** | **001** ✅ — `notes/userprofile-schema-contract.md`. 2 owner-actionable findings: **F-1** `sprk_primaryrole` may be local (owner wanted global set); **F-2** alt-key name unreadable via MCP (confirm at task 042). |
| **Branch** | `work/spaarkeai-assistant-enhancements-r1` (synced with origin/master 2026-07-15; clean; seams intact) |
| **Scope** | R1 only. **R1.5 (proactive push / Azure SignalR) designed, NOT decomposed** — filed as a follow-on spec-pass at wrap-up (task 090). |

## Task breakdown (25 tasks)

| Phase | Tasks | Theme |
|---|---|---|
| 1 — Catalog & Schema | 001, 002, 003 | userprofile contract · create-todo/-event capabilities · grounding-predicate column |
| 2 — Structured Creation | 010–014 | resolver · arg-fill exclusion · wizard envelope · smart pre-seed · assign/associate |
| 3 — Truthfulness & Risk | 020, 021, 022 | ack-contract truthfulness · `sprk_risk` gate · `dispatchUncertain` |
| 4 — User Model | 030, 031, 032 | profile producer · preference≠permission test · budget amend + byte-stable |
| 5 — Assistant Surface | 040–044 | drop-down · Quick Start · My Assistant · SNS cards · PreFilter wiring |
| 6 — Authoring/Eval/Hardening | 050–054 | catalog authoring · eval gate · security · size/CVE · deploy |
| 7 — Wrap-up | 090 | gates · test-diet · deferrals · README→Complete · sync |

## Critical path & risk

- Chains: `002 → 010 → 013` and `001 → 030 → 032`; every project closes `053 → 054 → 090`.
- Highest-risk: 010 (resolver), 021 (risk gate), 030/032 (hot chat-path + token budget), 044 (PreFilter), 042 (write path).
- Dispatch-spine tasks (010, 021, 022, 030, 044) are **sequential / main-session** (seam-test DoD); never parallelized with each other.

## Prerequisites status

- ✅ `sprk_userprofile` schema created in spaarkedev1 (task 001 verifies + records the contract).
- ✅ Registered in `projects/INDEX.md` + portfolio Project #649 (Epic #421).
- ◻️ (execution-time) confirm exact `sprk_matter` practice-area / matter-type field shapes for the resolver (FR-B1, task 010).
- ◻️ (execution-time) amended `EnvelopeBudget.User` value sized from the rendered fragment (NFR-01, task 032).

## Open decisions carried into execution (non-blocking)

- Constrained-field resolver placement — `Services/Ai/PublicContracts/` vs new component (Placement Justification at task 010).
- Finalize `sprk_primaryrole` global-set binding + practice-area taxonomy at column-verification (task 001).
- `EnvelopeBudget.User` amendment is an ADR-tension Path-B change — needs code-review sign-off (task 032).

## Decisions log (design→plan→tasks)

All owner decisions + the four design-time open questions are resolved and recorded in [`spec.md`](spec.md) Owner Clarifications + [`design.md`](design.md) revision log. redesign-r2 verified complete/merged/archived → **R1 is self-contained, no cross-project coordination.** goal-eligibility = NO for all waves (dispatch-spine + existential + deploy dominance; owner review-first stance).
