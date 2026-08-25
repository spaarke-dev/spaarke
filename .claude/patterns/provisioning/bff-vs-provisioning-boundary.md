# BFF vs Provisioning Boundary Pattern

> **Last Reviewed**: 2026-08-24
> **Reviewed By**: customer-provisioning-orchestration-r1 task 202 (SKELETON — task 203 fills)
> **Status**: Skeleton

## When
A provisioning run surfaces a bug in the BFF (`src/server/api/Sprk.Bff.Api/**` or `src/server/shared/Spaarke.*/**`). Which project owns the fix?

## Read These Files (task 203 fills)
1. `projects/customer-provisioning-orchestration-r1/tasks/202-pre-live-fire-lessons-audit-and-prereqs-formalization.poml` — the BINDING constraint (owner directive 2026-08-24 SESSION 5).
2. `projects/customer-provisioning-orchestration-r1/notes/task-202-punch-list.md` — the class A/B/C classifier.
3. `.claude/constraints/bff-extensions.md` § F.1 / F.2 / F.3 — asymmetric-registration, fixture-config-first, empirical-reproduction-first protocols.
4. `projects/customer-provisioning-orchestration-r1/CLAUDE.md` § "Coordination with other worktrees" — target-project routing table (which BFF worktree to file class-B against).

## Constraints (BINDING per owner 2026-08-24)
- **Provisioning-surfaced BFF bugs MUST route to BFF-owning projects, NOT fixed in a provisioning branch.**
- Even if a BFF fix is urgent for a specific provisioning run: file in BFF worktree + coordinate merge sequencing. Do NOT roll BFF fixes into provisioning branch — that anti-pattern hides real BFF quality debt behind "provisioning worked."
- BFF-owning worktrees: `code-quality-and-assurance-r3` (active decomposition), `spaarke-ai-architecture-redesign-r1/r2` (AI touch), + others in project CLAUDE.md coordination table.
- Every BFF fix routed via this boundary MUST include an accompanying ArchTest that prevents the class-of-bug at build time.

## Key Rules (task 203 fills detail)
1. Classifier: does the fix live in `src/server/api/Sprk.Bff.Api/**` or `src/server/shared/Spaarke.*/**`?
   - YES → CLASS-B (BFF-owned). File in BFF-owning worktree. Include ArchTest.
   - NO but touches `src/server/api/Sprk.Provisioning.ControlPlane.*/**` → CLASS-A (provisioning-owned). Land in this project's branch.
   - Both → CLASS-C (shared/coordination). Document split in punch list; coordinate merge across worktrees.
2. Exception (case-by-case): a BFF fix already committed to a provisioning branch (like SESSION 5 `e3a15db91`) may be KEPT-IN-PLACE with a class-B ArchTest follow-on filed in the correct worktree. Decision documented in punch list § "commit case study".
3. Class-B tasks MUST have accompanying ArchTest — silent violation is forbidden. Fix without prevention = fix that will recur.
4. Merge sequencing: BFF fixes MUST land in BFF branch BEFORE the provisioning branch runs E2E live-fire. Provisioning branch pulls master after BFF PR merges → E2E fires against the fixed state.
