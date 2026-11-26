# Task Index — specialist-legal-agents-r1 (Phase 0)

> **Project**: Specialist Legal Agents R1 (umbrella), Phase 0 — evidence and the go/no-go record for D-A7, D-A12, D-A13
> **Created**: 2026-11-26 (task-create, project-pipeline Step 3)
> **Total tasks**: 20 (19 Phase 0 tasks + 1 wrap-up)
> **Status tokens** (task-create Step 5): 🔲 `[open]` · 🔄 `[wip]` · ✅ `[done]` · ⚠️ `[escalated]` · 🟡 `[blocked]` · 🚫 `[cancelled]` · ⏸️ `[deferred]`. The bracketed token is mandatory in every status cell (FAILURE-MODES G-16). After any status change run `pwsh scripts/check-task-status-drift.ps1`.

## Task Registry

| ID | Title | Phase | Status | Dependencies | Parallel | Model / effort | Rigor |
|-----|-------|-------|--------|--------------|----------|----------------|-------|
| 001 | Phase 0 index and artifact inventory | 1 | ✅ [done] | none | Group A | sonnet / medium | MINIMAL |
| 002 | G1-NDA kit: test profiles A and B, prompt script, results sheet | 1 | ✅ [done] | none | Group A | sonnet / high | STANDARD |
| 003 | Synthetic NDA corpus and ratchet probes | 1 | ✅ [done] | none | Group A | sonnet / high | STANDARD |
| 004 | G5 routing kit and G2 packaging questions | 1 | ✅ [done] | none | Group A | sonnet / medium | MINIMAL |
| 005 | G6 coexistence kit (extends T-P2) | 1 | ✅ [done] | none | Group A | sonnet / medium | MINIMAL |
| 006 | Patent test content, drafting skill and patent kits | 1 | ✅ [done] | none | Group A | opus / high | STANDARD |
| 007 | Desk items I-1 to I-4 with file:line evidence | 1 | ✅ [done] | none | Group B | opus / high | STANDARD |
| 010 | Check the agreement-review Action row and its input schema in DEV | 2 | ✅ [done] | none | Group B | sonnet / high | STANDARD |
| 011 | Spike branch: spike_get_profile and spike_review_nda (never merged) | 2 | ✅ [done] | 002, 006, 010 | alone | opus / high | FULL |
| 015 | Spike dev deploy through uac-r2 | 2 | ✅ [done] | 011 | alone (owner go) | sonnet / high | STANDARD |
| 020 | Test agent packages for G1, G5 and G2 | 3 | 🔲 [open] | 002, 004, 015 | alone (owner uploads) | sonnet / high | STANDARD |
| 021 | G3 run: profile resolution, run key and agent identity | 3 | 🔲 [open] | 015, 020 | Group C | sonnet / high | STANDARD |
| 022 | G1-NDA runs: pilots, full runs and verdict | 3 | 🔲 [open] | 003, 020 | Group C | sonnet / high | STANDARD |
| 023 | G5 routing runs and G2 checks | 3 | 🔲 [open] | 020 | Group C | sonnet / high | STANDARD |
| 024 | G6 coexistence runs; results to accelerator-r1 | 3 | 🔲 [open] | 005 | Group C | sonnet / high | STANDARD |
| 025 | Cowork patent-drafting spike | 3 | 🔲 [open] | 006 | Group C | sonnet / high | STANDARD |
| 026 | G1-PAT repeat for variants still in contention | 3 | 🔲 [open] | 022 | alone | sonnet / high | STANDARD |
| 030 | Go/no-go record: phase0/G1-decision.md | 4 | 🔲 [open] | 007, 021, 022, 023, 024, 025, 026 | alone | opus / high | STANDARD |
| 031 | Clean-up: tenant, DEV and inventory | 4 | 🔲 [open] | 030 | alone | sonnet / high | STANDARD |
| 090 | Phase 0 wrap-up | Wrap-up | 🔲 [open] | all | alone | sonnet / high | STANDARD |

Phase 0 merges no product code. Only task 011 writes code, on branch `spike/specialist-agents-phase0`, which is never merged. Every other task writes only under `projects/specialist-legal-agents-r1/`. No task touches `.claude/`.

The lint (`scripts/Validate-TaskPoml.ps1`) warns that 11 tasks mark files `role="new"` without a `<justification>`. Those files are project test kits and notes under `projects/specialist-legal-agents-r1/`, not product surface (root CLAUDE.md §11 lists services, endpoints, DI registrations, packages, columns). Task 011, the only task adding product surface, carries one.

## Gates and external conditions

- **Owner steps** (the task stops and asks; it never performs or simulates them):
  - 010: any DEV write (`agreement-review-spike`);
  - 015: the per-SHA deploy go; the deploy runs through the uac-r2 session;
  - 020: uploads and installs, for the owner and the test user only, each after its R4 row is sent to COPILOT-PLATFORM-COORDINATION (#3006);
  - 021–026: the owner and the test user run the prompts;
  - 024: the owner switches on Legal for Cowork;
  - 031: tenant removals and the DEV row deletion.
- **G8 removals** (owner, by 2026-11-30) come before the first G1-NDA run (022).
- **015 base**: the spike SHA must contain `origin/master` (with mcp-r2's merged work), so the dev deploy rolls nothing back (NFR-06).
- **026** runs only the variants (a)/(b) still in contention after 022. If none survived, 026 records "not run" and completes.
- **090** does not archive the umbrella without the owner's go: Phases 1–4 continue as child projects.

## Critical Path

002 / 006 / 010 → 011 → 015 → 020 → 022 → 026 → 030 → 031 → 090

The owner steps on 015, 020 and 022 set the pace; the kits (Wave 1) are short.

## High-risk items

- **011** (FULL, opus): per-user profile isolation and fail-closed caller resolution. Gets the adversarial review with tenant-isolation seeding proof.
- **022**: the D-A7 verdict rests on it; thresholds are applied exactly as FR-04 states them.
- **020**: tenant uploads; R4 rows and fresh app ids (BR-2) are mandatory.

## Parallel Execution Groups

| Wave | Group | Tasks | Prerequisite | Parallel-safe | Goal-eligible | Goal condition |
|------|-------|-------|--------------|---------------|---------------|----------------|
| 1 | A | 001, 002, 003, 004, 005, 006 | none | yes (separate folders under `phase0/`) | NO | — (document authoring; reviewed by the main session) |
| 2 | B | 007, 010 | none | yes (separate files) | NO | — (010 stops at an owner step) |
| 3 | — | 011 | 002, 006, 010 | alone | NO | — (FULL rigor, review required) |
| 4 | — | 015 | 011 | alone | NO | — (owner go per SHA) |
| 5 | — | 020 | 002, 004, 015 | alone | NO | — (owner uploads) |
| 6 | C | 021, 022, 023, 024, 025 | per row | yes (separate result files) | NO | — (owner and test user run prompts) |
| 7 | — | 026 | 022 | alone | NO | — (owner runs) |
| 8 | — | 030 | 007, 021–026 | alone | NO | — (recommendation for the owner) |
| 9 | — | 031 | 030 | alone | NO | — (owner removals) |
| 10 | — | 090 | all | alone | NO | — (owner archive decision) |

Wave 1 runs six task-execute sub-agents in one message (at most 6 per wave). 024 and 025 depend only on 005 and 006, so they may start before Wave 6 once the owner is ready; they are listed in Group C because they share the owner's test sessions.
