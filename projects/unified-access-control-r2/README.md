# Unified Access-Control Cascade — R2

> **Portfolio**: [Project #808](https://github.com/spaarke-dev/spaarke/issues/808) under [Epic #535](https://github.com/spaarke-dev/spaarke/issues/535) `[Epic]: ENTITY FUNCTIONALITY` · [Board #2](https://github.com/users/spaarke-dev/projects/2)
>
> **Status**: INITIALIZED (worktree + investigation doc only) · **Start**: 2026-08-19
> **Worktree**: `c:/code_files/spaarke-wt-unified-access-control-r2` · **Branch**: `work/unified-access-control-r2`
> **Origin**: smart-todo-r5 UAT follow-up — "child access cascade is part of our unified access control system — think it all the way through"

---

## The requirement

> The **members of a parent record** should be able to **access the parent's child records**.

Cross-cutting, not a To Do feature. Applies today to `sprk_todo` (child of Matter / Event / Invoice / +8 more) and will recur for future parent→child pairs. The goal is **one reusable mechanism** any pair opts into, not N bespoke implementations.

## The load-bearing constraint

**Being referenced by a lookup column grants ZERO access in Dataverse.** Access comes only from ownership, security-role privilege, team membership, share (POA), or the user hierarchy. Cascade features *propagate* existing access — they cannot *manufacture* it for a principal a lookup merely points at. This is why the general case needs code, not configuration.

## What already exists (low-invention)

| Building block | Where |
|---|---|
| Membership resolver (ADR-034) — metadata-driven, `user → records` | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipResolverService.cs` |
| POA grant seam — `GrantAccessAsync` | `IDataverseAccessGrantService` / `DataverseWebApiService` |
| Reconciliation job | `MembershipReconciliationJob.cs` |

**Missing piece**: the cascade wiring + trigger (`record → members → grant on children`) and the removal/reconciliation path.

## Documents

| Doc | Purpose |
|---|---|
| [`unified-access-control-cascade.md`](unified-access-control-cascade.md) | Investigation & project proposal (2026-08-18) — current state, config-only verdict, design landscape, §7 open decisions |
| `design.md` | *(next)* Resolves the §7 open decisions into a design |
| `spec.md` | *(after `/design-to-spec`)* |
| `tasks/TASK-INDEX.md` | *(after `/project-pipeline`)* |

## Open decisions to resolve in `design.md`

1. **Policy** — which members confer child access? (all resolved principals / `sprk_assigned*` only / owner+team only)
2. **Mechanism** — owning-team sync vs per-record POA share vs access teams (POA-bloat tradeoff)
3. **Trigger** — Power Automate flow vs Dataverse webhook → BFF
4. **Reconciliation/removal scope** — parent-side edits, member removals (sharing is additive-only)
5. **Do we spend the single allowed parental relationship** on one parent path (owner+team interim coverage)?
6. **BFF placement justification** per [CLAUDE.md §10](../../CLAUDE.md) — new endpoints/services + publish-size verification

## Related prior work

- [`projects/unified-access-control-r1/`](../unified-access-control-r1/) — SPE permission + secure-access-control design docs (not executed)
- [`projects/dataverse-access-unification-r1/`](../dataverse-access-unification-r1/) — **PAUSED 2026-08-19**; its [validation note](../dataverse-access-unification-r1/notes/validation-2026-08-19.md) found five Dataverse access stacks (not two) and flagged fail-OPEN row-level-security risk on a near-zero test baseline. Read before choosing a mechanism here.

## Next step

`/design-to-spec` → `/project-pipeline`
