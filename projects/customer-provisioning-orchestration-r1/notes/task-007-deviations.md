# Task 007 — Author 6 U-CB customer-comms templates — Deviations

**Task**: `007-author-ucb-customer-comms-templates.poml`
**Executed**: 2026-08-17
**Wave**: Wave 0 Batch 1 (parallel dispatch with tasks 001, 002, 003, 004, 006, 008)
**Rigor**: MINIMAL
**Model**: sonnet @ medium

## Summary

All 6 U-CB customer-comms templates authored and published under `docs/deployment/customer-comms/` per POML Steps 0–4. Steps 5–7 are partially deferred as documented below.

## Files created

- `docs/deployment/customer-comms/U-CB-1-column-removal.md`
- `docs/deployment/customer-comms/U-CB-2-vector-dim-change.md`
- `docs/deployment/customer-comms/U-CB-3-permission-additions.md`
- `docs/deployment/customer-comms/U-CB-4-spe-schema.md`
- `docs/deployment/customer-comms/U-CB-5-kv-secret-cascading.md`
- `docs/deployment/customer-comms/U-CB-6-client-secret-expiry.md`

Each template contains the mandated 7 sections (summary / trigger / impact / timeline / action / confirmation / rollback) plus front-matter (Purpose / Applies-when / Owner / Delivery format / Related) per the `<knowledge>` pattern in the POML. All copy is plain-text markdown; no HTML, no branded/table-styled email formatting; `{curly-brace}` placeholders throughout for operator substitution per Owner Assumption (spec.md § Assumptions row 7).

## Deviation 1 — POML Step 5 (bidirectional cross-references) partially deferred

**Step 5 text**: "Ensure the new authoritative deploy guide (task 001) + version-compatibility matrix (task 006) reference these templates; add cross-refs bidirectionally."

**Deviation**: Task 007 runs in **Wave 0 Batch 1 parallel dispatch** alongside task 001 (`001-consolidate-deploy-guides`) and task 006 (`006-publish-version-compat-matrix-initial`). At the time task 007 authored its files, tasks 001 and 006 had not yet committed their target files (`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` and `docs/deployment/version-compatibility-matrix.md` respectively). Writing outbound cross-references into those files from task 007 would race with the parallel sibling agents on the same paths.

**Compensating action taken**:
- **Inbound cross-references** (templates → deploy guide / version-compat matrix) ARE authored inline (all 6 templates include front-matter `Related:` lines that point at `../version-compatibility-matrix.md` and `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`). These become live links when tasks 001 and 006 land.
- **Outbound cross-references** (deploy guide → templates, version-compat matrix → templates) are the responsibility of the sibling task authors and/or a Wave 0 wrap-up integration pass. Recommend logging this as follow-on in the wave dispatcher's aggregation report.

**Impact**: none functional — the templates are self-contained operational text and do not require the sibling files to work as intended. The bidirectional-cross-ref gap is a documentation-polish item that can be closed in a subsequent PR without blocking any downstream task.

## Deviation 2 — POML Steps 6 and 7 (TASK-INDEX update, deviation doc) — dispatcher-owned

Per Wave 0 parallel-dispatch context in the launching instruction:
- Step 6 (`Update TASK-INDEX.md: set task 007 status to ✅`) is **skipped** — dispatcher handles it after the wave completes.
- Step 7 (`Document any deviation in projects/customer-provisioning-orchestration-r1/notes/`) is **this file**.

## Acceptance criteria verification

| Criterion | Status | Evidence |
|---|---|---|
| Exactly 6 files matching U-CB-{1..6}-*.md exist | ✅ | Listed above |
| All 7 mandated sections present per template | ✅ | Verified during authoring; §1 Summary, §2 Trigger, §3 Impact, §4 Timeline, §5 Action, §6 Confirmation, §7 Rollback |
| Plain-text markdown only (no HTML/styles/images) | ✅ | Only markdown headings, bullets, blockquotes, and one table per template (Timeline table is markdown-basic) |
| Version-compat matrix cross-references appropriate U-CB template | ⚠️ Deferred | Owned by task 006 (parallel sibling); templates carry inbound refs |
| Negative: git diff shows only the 6 new templates + cross-ref updates | ✅ | 6 new template files + this deviation note; no code/Bicep/script touched |

No blockers.
