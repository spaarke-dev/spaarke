# BFF vs Provisioning Boundary Pattern

> **Last Reviewed**: 2026-08-25
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A07
> **Status**: Content filled (task 203a). Was skeleton from task 202. **Amended 2026-08-24 SESSION 7**: since `code-quality-and-assurance-r3` closed, verified-open BFF-owned Class-B rows are absorbed into the provisioning project (task 204) rather than routed to a non-existent project — see task-202 punch list SESSION 7 amendment.

## When

Load this pattern when:
- A provisioning run surfaces a bug in the BFF (`src/server/api/Sprk.Bff.Api/**` or `src/server/shared/Spaarke.*/**`).
- Reviewing a PR that fixes a BFF issue while in a provisioning branch.
- Deciding whether a lesson-learned entry files as CLASS-A (provisioning-owned), CLASS-B (BFF-owned), or CLASS-C (shared/coordination).
- Coordinating merge sequencing between the provisioning branch and BFF-owning worktrees.

## Read These Files (canonical source)

1. `projects/customer-provisioning-orchestration-r1/tasks/202-pre-live-fire-lessons-audit-and-prereqs-formalization.poml` — the BINDING constraint (owner directive 2026-08-24 SESSION 5) that provisioning-surfaced BFF bugs route to BFF-owning projects.
2. `projects/customer-provisioning-orchestration-r1/notes/task-202-punch-list.md` — the class A/B/C classifier + case-study rulings.
3. `.claude/constraints/bff-extensions.md` § F.1 / F.2 / F.3 — asymmetric-registration, fixture-config-FIRST, empirical-reproduction-FIRST protocols.
4. `.claude/constraints/bff-extensions.md` § F — Test update obligation (BINDING). Every BFF fix routed via this boundary MUST include a test.
5. `projects/customer-provisioning-orchestration-r1/CLAUDE.md` § "Coordination with other worktrees" — target-project routing table (which BFF worktree to file class-B against).
6. `projects/INDEX.md` § BFF hot-path — active BFF-owning worktrees.

## Constraints (BINDING per owner 2026-08-24)

- **Provisioning-surfaced BFF bugs default-route to BFF-owning projects, NOT fixed silently in a provisioning branch.** Even if a BFF fix is urgent for a specific provisioning run, file it in the correct BFF worktree + coordinate merge sequencing.
- **Do NOT roll BFF fixes into provisioning branch silently** — that anti-pattern hides real BFF quality debt behind "provisioning worked." Every BFF fix that lands in a provisioning branch must be documented + a same-class prevention (ArchTest / test) filed against the BFF-owning worktree.
- **SESSION 7 amendment** (2026-08-24): if the target BFF-owning project has CLOSED (like `code-quality-and-assurance-r3` did) and the bug is E2E-blocking, absorb into the current provisioning project as a class-B follow-on (task 204a in this project). Route rationale documented in the punch list re-scope section.
- Every BFF fix routed via this boundary MUST include an accompanying ArchTest (or equivalent forcing function) that prevents the class-of-bug at build time. Fix without prevention = fix that will recur.
- **Test update obligation** (bff-extensions.md § F): PRs modifying `src/server/api/Sprk.Bff.Api/Services/` MUST add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`.

## Key Rules (walk this for every fix-in-provisioning decision)

1. **Classifier — where does the fix code live?**
   - Lives in `src/server/services/Sprk.Provisioning.ControlPlane.*/**` → **CLASS-A** (provisioning-owned). Land in this project's branch.
   - Lives in `src/server/api/Sprk.Bff.Api/**` or `src/server/shared/Spaarke.*/**` → **CLASS-B** (BFF-owned). Default: file in BFF-owning worktree per coordination table. Exception (post-SESSION-7): if target BFF worktree is closed AND row is E2E-blocking, absorb into this project as a task-204 sub-phase.
   - Touches both → **CLASS-C** (shared/coordination). Document split in punch list; coordinate merge across worktrees.
2. **Exception (case-by-case): fix already committed in a provisioning branch** (like SESSION 5 `e3a15db91` — IActionSeam hoist). Decision: KEEP-IN-PLACE (avoid rework/regression) + file a class-B ArchTest follow-on in the correct worktree. Decision documented in punch list § "commit case study".
3. **Class-B tasks MUST have accompanying ArchTest** — silent violation forbidden. Fix without prevention = fix that will recur. This is the reason `code-quality-and-assurance-r3` maintained the ArchTest suite; when it closed, the discipline moved to task 204e in this project.
4. **Merge sequencing** — BFF fixes MUST land in BFF branch BEFORE the provisioning branch runs E2E live-fire. Provisioning branch pulls master after BFF PR merges → E2E fires against the fixed state.
5. **Fixture-config FIRST + empirical-reproduction FIRST** (per § F.2 / F.3): when a fix requires code, hand-trace + reproduce first. Skipping these protocols leads to fixing symptoms while root causes persist.

## Anti-patterns this catches

- ❌ Fixing a BFF bug inside a provisioning branch without filing the ArchTest/prevention against the BFF worktree → next month the bug recurs and no one knows about the earlier fix.
- ❌ "It's just one line, I'll fix it here" → the ONE line hides an asymmetric registration or config drift that will re-manifest as another consumer trips on the same shape.
- ❌ Merging the provisioning branch to master with a bundled BFF fix that never got reviewed by the BFF worktree's owner → CODEOWNERS bypass + BFF quality debt.
- ❌ Filing a class-B row against a project that no longer exists (like `code-quality-and-assurance-r3` after 2026-08-16) without updating the routing → row disappears from all boards; nothing gets done.
- ❌ Skipping the F.2 fixture-config check → rewriting DI code that was actually correct all along.

## Recovery recipes

- **Discovered a BFF bug during a provisioning run**: (a) classify per rule 1; (b) if CLASS-B, check the coordination table for the target worktree; (c) if target is CLOSED, apply SESSION 7 amendment (absorb into task 204); (d) file the row in the punch list with `class`, `landing-spot`, `blocks-e2e` fields.
- **Class-B fix already landed in provisioning branch**: apply rule 2 exception. Keep in place; document in "commit case study" section of punch list; file ArchTest follow-on against BFF worktree (or task 204 if absorbed).
- **Merge conflict at BFF PR time**: BFF PR should land FIRST per rule 4. If provisioning branch already merged with a bundled BFF change, resolve by pulling BFF PR's version and re-applying provisioning-specific delta.

## Worked example — three real routing decisions from this project

### Decision 1: SESSION 5 IActionSeam hoist (`e3a15db91`) — Class-B, keep-in-place exception

- **Where the fix lives**: `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` → CLASS-B (BFF-owned).
- **Standard rule** would route to BFF-owning worktree.
- **Exception applied** (rule 2): fix already committed + deployed to Model 1 Prod BFF; reverting risks BFF SIGABRT regression. KEEP-IN-PLACE.
- **Prevention follow-on**: filed Class-B row B01 (nightly ArchTest for asymmetric-registration Tier 1.5). Filed against `code-quality-and-assurance-r3` originally; SESSION 7 amendment absorbed into task 204e when r3 closed.
- **Documented in**: punch list § "IActionSeam case study" section.

### Decision 2: H4b bulk-appsettings handler (task 201) — Class-A

- **Where the fix lives**: `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/H4bBulkAppSettingsHandler.cs` → CLASS-A (provisioning-owned).
- **Route**: land in this project. No coordination needed.
- **Rationale**: the handler itself is a provisioning concern (deploy-time app-settings orchestration). The IOptions modules it seeds are BFF concerns, but those aren't being modified — H4b just seeds their settings.

### Decision 3: Multi-tenant DV routing gap (B04) — Class-B, ADR-tension escalation required

- **Where the fix lives**: `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:39` → CLASS-B (BFF-side shared).
- **Standard rule** would route to BFF-owning worktree.
- **Complication**: fix is architectural (Model 1 shared BFF ↔ per-tenant Dataverse routing decision) — not a one-liner. Needs ADR conflict resolution per CLAUDE.md §6.5.
- **Route applied**: task 204b (ADR tension) in THIS project (SESSION 7 absorption, since r3 closed). Task PAUSES at Step 2 for owner Path A/B/C decision per §6.5 protocol.
- **Documented in**: punch list SESSION 7 amendment + task 204b escalation trigger.

### Anti-pattern decision (what NOT to do)

- ❌ "Just fix DataverseServiceClientImpl inline while we're here" — that's Decision 3 without escalation. Silent violation of §6.5, and hides an architectural boundary decision behind a code diff.
- ❌ "Skip filing B01 ArchTest because r3 is closed" — SESSION 7 amendment explicitly absorbs into 204e; the discipline transfers, it doesn't disappear.
- ❌ "Route H4b handler to BFF worktree because it touches BFF app-settings" — misreads the classifier. H4b's CODE lives in L2 control-plane; what it MODIFIES (App Service app-settings) is runtime state, not BFF source.

## Cross-refs

- Related constraint: `.claude/constraints/bff-extensions.md` § F (all sub-sections)
- Related constraint: `.claude/constraints/provisioning.md` § Class-A/B/C routing (task 203a authors)
- Related pattern: [null-object-kill-switch-anti-pattern.md](null-object-kill-switch-anti-pattern.md) (F.1 detection — often surfaces as class-B)
- Related project doc: `projects/customer-provisioning-orchestration-r1/CLAUDE.md` § Coordination with other worktrees
- Related project doc: `projects/customer-provisioning-orchestration-r1/notes/task-202-punch-list.md` § SESSION 7 verification matrix

## Classifier flowchart (quick reference)

```
Bug surfaced during provisioning run
        │
        ▼
Where does the FIX code live?
        │
        ├── src/server/services/Sprk.Provisioning.ControlPlane.*/**
        │       │
        │       └── CLASS-A → land in this project
        │
        ├── src/server/api/Sprk.Bff.Api/** or src/server/shared/Spaarke.*/**
        │       │
        │       └── CLASS-B → is target BFF worktree active?
        │               │
        │               ├── YES → file in target worktree; coordinate merge sequencing
        │               │
        │               └── NO (target closed) → SESSION 7 amendment: absorb into task 204 sub-phase
        │
        └── Both provisioning + BFF surfaces
                │
                └── CLASS-C → document split in punch list; coordinate across worktrees
```

Every route MUST include a prevention (ArchTest / test / lint rule) — a fix without prevention is a fix that will recur.
