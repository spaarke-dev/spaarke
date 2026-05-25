# Spaarke AI Platform Unification R4

> **Status**: **Planning — scoping decisions finalized 2026-05-25.** `plan.md` is the authoritative project plan. Ready for `spec.md` formalization and `/project-pipeline` task generation.
> **Last Updated**: 2026-05-25
> **Predecessor**: spaarke-ai-platform-unification-r3 (shipped at master `3813af32`)

---

## Project documents

| Document | Purpose | Status |
|---|---|---|
| **[`plan.md`](plan.md)** | **Authoritative project plan.** WBS across 8 phases, dependencies, acceptance criteria, risk register. | ✅ Created 2026-05-25 |
| [`backlog.md`](backlog.md) | Full per-item analysis with sources, technical detail, rationale, effort estimates. Annotated with scoping decisions (IN / DEFER / NEW) at the top. | ✅ Updated 2026-05-25 |
| `spec.md` | Formal FRs/NFRs derived from IN items | ⏳ TBD |
| `CLAUDE.md` | Project-scoped AI context | ⏳ Created by `/project-pipeline` |
| `current-task.md` | Active task state tracker | ⏳ Created by `/project-pipeline` |
| `tasks/` | POML task files | ⏳ Created by `/project-pipeline` / `task-create` |

## What R4 covers

**Scope (decided 2026-05-25)**:
- **34 work items** across **8 phases**
- **~116 hours** estimated effort (~14-15 working days)
- Mix of: R3 wrap-up + documentation establishing the new dashboard+widget architecture + BFF governance + UQ-03 verification + substantive code refactors + build hygiene

**Key new architectural work** (Group W):
- Write `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` — authoritative architecture doc capturing the two-wrapper model (Dashboard wrapper via `LegalWorkspaceApp` + Direct widget wrapper via `WorkspaceWidgetRegistry`) and the LegalWorkspace-as-dashboard-engine framing
- Rewrite `BUILD-A-NEW-WORKSPACE-WIDGET.md` with two-wrapper decision tree
- Fix `WorkspaceLayoutWizard` catalog drift bug (Calendar/Daily Briefing missing from builder picker)
- Wire Assistant → Workspace mount source (first end-to-end demo)
- Wire Context → Workspace mount source for one wizard
- Document LegalWorkspace code page retirement

**Out of scope (deferred)**:
- Stages 2-4 chrome (Moment 2+ scope)
- AI-vs-User visual + AIReasoningSurface conventions
- Hoist remaining 5 LW-internal section factories (Calendar precedent gives forward path; no forcing function)
- `runtimeConfig` hoist to `@spaarke/auth`
- Section registry plug-in style
- Bundle-size Option 2 separate-web-resources IMPLEMENTATION (only the ADR amendment is in scope)

## Phases at a glance

| # | Phase | Effort | Theme |
|---|---|---|---|
| 0 | R3 wrap-up + retroactive memo | ~4h | Close R3; F-1 light memo |
| 1 | Documentation round | ~21h | W-1, W-2, ADRs (A-2, D-2), decision criteria (C-1, C-2), F-3 |
| 2 | BFF governance audit | ~2h | F-2 facade audit |
| 3 | UQ-03 verification + fix | ~10h | A-5 verify-then-fix tab persistence |
| 4 | Workspace builder + mount sources | ~19h | W-3 wizard fix, W-4 + W-5 mount sources, W-6 LW retirement |
| 5 | Substantive code changes | ~31h | A-4 attachment 25MB, C-3 hooks, C-4 renderer interface, B-4/B-5/B-6 |
| 6 | Build hygiene cluster | ~21h | B-1, B-2, B-3, B-7, B-8, B-9, B-10, B-11 |
| 7 | R4 wrap-up | ~2h | Close project |

## Next steps

1. **Review `plan.md`** — operator confirms phases + acceptance criteria
2. **Create `spec.md`** — translate IN items to FRs/NFRs
3. **Run `/project-pipeline projects/spaarke-ai-platform-unification-r4`** — generates CLAUDE.md, current-task.md, tasks/ POML files
4. **Create worktree** for R4 development (after Phase 0 R3 wrap-up lands and master is updated)
5. **Phase 0 first** — E-1 + F-1 are independent and don't block
6. **Subsequent phases** — execute per WBS in `plan.md`

---

*See `plan.md` for the authoritative project plan; `backlog.md` for the per-item analysis.*
