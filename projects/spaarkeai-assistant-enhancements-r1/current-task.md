# Current Task State — Assistant Enhancements R1 ("Follow-Through")

| Field | Value |
|---|---|
| **Active task** | **Owner-requested enhancements DONE (2026-07-16): D-032-01 BU/team ✅ + D-042-01 user-memory seed ✅ (combined build + 100 affected tests green). D-042-02 resolved. Back to the maker-portal wait.** |
| **Status** | Done: 001 ✅, 030 ✅, 040 ✅, 031 ✅, 032 ✅, 020 ✅, 043 ✅, 052 ✅, **042 ✅ (keyed upsert + N:N + F5 erasure; alt-key Active F-2✅, role global F-1✅; MemoryItem seed → D-042-01; write-authZ config → D-042-02)**. **10/25 effective.** **Held**: 002 (needs `SurfaceLaunch` option-set add, maker portal), 003 ⏸️. |
| **Next action** | **Blocked pending maker-portal action** — the `SurfaceLaunch` `sprk_disposition` value add (on `sprk` publisher) unblocks the create-flow chain (010 → 011/012/013/014 → 021 → 022 → 050 → 051/053 → 054 → 090). Same for the `sprk_grounding` predicate column (003 → 044). Nothing else runnable autonomously. **Commits unpushed** (031/032/design/052/020/043/F2/042). |
| **052 sign-off (2026-07-16)** | F1 app-only read RATIFIED · F2 free-text delimiting APPLIED (StatedProfileRenderer «...» + guard line; 032 byte-golden re-baselined) · F5 GDPR-erasure ACCEPTED into 042. |
| **002/012 DESIGN (resolved 2026-07-16)** | Surface-launch = **Assistant→surface hand-off**, 3 classes: **1 dispatch** (server fn, shipped) · **2a in-app** (tab/widget/layout/Compose — in-app event bus, config object, NO id) · **2b launched Code Page** (wizard/OOB — caller-minted **handoff id** injected in launch URL + payload in **sessionStorage**, outcome-back keyed by id → feeds P5/020) · **3 external** (out of scope). Routing = new **`SurfaceLaunch` `sprk_disposition` value** (the `Compose` precedent), discriminated by `target.kind`. Full contract: `notes/012-assistant-surface-handoff-design.md`. **NL→filter builder = closed dimension set {dateRange,dateField,status,assignee} → widget config, NEVER FetchXML/LLM-authored** (ADR-039/P1). |
| **R1 scope addition (owner-approved 2026-07-16)** | **"show my open tasks"** (2a instance) → **filtered Task grid** (DataGrid `configId` + status/owner filter; "my" via `IMembershipResolverService` — redeems the FR-E5 membership work descoped from 032). Needs a new small task (NL→dimension builder + Task-grid target); create at execution time. Verify DataGrid accepts a runtime status/owner filter override. |
| **Parallel Execution** | Wave 4: 031 (sonnet subagent, test) ‖ 032 (opus main-session, hot-path) ran concurrently, no file overlap. 031 committed; 032 held at gate. Earlier wave: 030 (BFF) + 040 (client). |
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
