# Current Task State — email-communication-intelligence-r1

> **Last Updated**: 2026-07-29 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | Project setup COMPLETE → **execution starting** |
| **Task** | **001** — Verify operator schema inputs (first task, read-only) |
| **Status** | ready-to-start (not yet begun) |
| **Next Action** | Invoke `task-execute` on `tasks/001-verify-operator-schema-inputs.poml`; then continue through the waves per `tasks/TASK-INDEX.md`. |
| **Execution mode** | **AUTONOMOUS + PARALLEL where possible** (operator's explicit instruction). Wave-by-wave via TASK-INDEX parallel groups; `dotnet build` between waves; `/conflict-check` before each BFF PR. |
| **Branch** | `work/email-communication-intelligence-r1` (all setup committed + pushed, latest `5acd5c00c`) |

### Schema readiness — operator COMPLETED all objects in `spaarkedev1` (2026-07-29)
Task 001 **verifies** these (does NOT create); schema tasks **011/012/013 are now verify-only** (no create/collision):
- `sprk_emailupdatefield` (Job B allow-list) ✅
- `sprk_communication` triage fields: `sprk_triagecategory` (lookup), `sprk_triagepriority`, `sprk_triagesummary`, `sprk_triageobligations` (JSON), `sprk_riconfidence`, `sprk_reviewoutcome` ✅
- `sprk_emailreviewlog` (audit) ✅
- `sprk_triagecategory` (taxonomy config) ✅
- **AS-BUILT option-set values → `notes/schema-to-create.md` § "AS-BUILT option-set values" — implementation MUST use those integer values.** Deltas: `sprk_fieldtype` = Text/Lookup/OptionSet/Number/DateTime/Boolean/Memo/Currency (single **Number** → resolve whole-vs-decimal from field metadata); `sprk_triagepriority` = Urgent/High/Medium/Low; `sprk_action` label "Overriden" (one `d` — code keys on int).

### Critical Context
22 task POMLs validated (PASS). Design §0 authoritative. **Binding locks** (full list in CLAUDE.md): code-directed Action+Binding only (node engine FROZEN); auto-file **C-1 = rung 0 + rung 1 only** (2/3 → `Suggested`); **Job B FULL** (propose→confirm→apply via `IActionSeam.UpdateRecordAsync` under OBO → `sprk_emailreviewlog` audit; allow-listed fields only); IP docketing OUT; **surfaces owned by completed r5** (r1 builds NO UI — feed + apply endpoints only, C-3; contract in `notes/email-intelligence-r1-coordination.md`).

---

## Full State (Detailed)

### Done
- design.md rev-3 (§0.11 closes all §11 decisions; C-1 locked; Job B FULL; IP docketing removed) · spec.md (17 FRs/8 NFRs; C-3/C-4/FR-15 resolved) · plan.md · CLAUDE.md · 22 POMLs + TASK-INDEX (validator PASS) · notes (schema-to-create, coordination, ux-research). All committed + pushed.

### Execution plan (from TASK-INDEX)
- **Critical path**: `001 → 020 → 030 → 031 → 060 → 061 → 090` (Job B deepest track).
- **Parallel groups**: P0(001) → P1(010,011,012,013) → {P2-assoc: 020→021 ‖ P2-triage: 022→023→{024,025}} → P3(030→{031,032}) → P4(040→041; 042); **P5(050→051) fully parallel to P1–P4**; P6(060→061→090).
- **Model tiers**: default sonnet/high; **opus** on 020,030,031,041,042,050; **xhigh** on 020,042; 001/061/090 low.
- **Max concurrency 6/wave**. BFF writers to shared `Services/Communication/` are `parallel-safe: false` (never concurrent). `.claude/` tasks main-session-only.
- **Between waves**: `dotnet build src/server/api/Sprk.Bff.Api/` if any `.cs` changed → STOP on failure.

### Watch items
- 020 highest blast-radius (association correctness); bare-numeric never auto-files alone; multi-entity → `Ambiguous`; read `sprk_recordtype_ref` defensively (typos).
- 030/031 record-mutating — human-confirm + cite + audit + allow-list + OBO; verify cited text exists.
- 050 spike has escalation trigger; 051 gated on its finding.
- Every BFF task: publish-size (≤60 MB; baseline ~49.63 MB) + CVE + `/conflict-check` + tests + golden-utterance eval (new Actions/Bindings).

### To resume (post-compact)
Say "continue" or "work on task 001" → invoke `task-execute` on task 001, then proceed autonomously through the waves.

## Decisions Made
*(appended by task-execute during execution)*

## Implementation Notes
*(none yet — execution not started)*
