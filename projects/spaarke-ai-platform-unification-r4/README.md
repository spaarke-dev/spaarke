# Spaarke AI Platform Unification R4

> **Status**: Pre-spec scoping. Not yet a full project.
> **Last Updated**: 2026-05-24

---

## What this folder is

A staging area to consolidate the follow-up items that surfaced during R3 (tasks 001-140, including 13 rounds of operator polish) so they can be reviewed in one place and decided on **IN / DEFER / OUT-OF-SCOPE** for R4.

R3 closed at master commit `3813af32` (cherry-pick of R3 `59c1ac3f` — task 140 Fluent Dropdown min-width fix). Throughout R3, ~30 follow-up items were intentionally flagged-and-deferred across multiple documents (design.md, notes/, deploy memos, the componentization audit, CLAUDE.md §10). This folder pulls them all into one self-contained backlog with full detail inlined.

## Documents in this folder

- **[`backlog.md`](backlog.md)** — the consolidated backlog. Six categories (A-F), 30 items, each with source quote, technical detail, why-it-matters, recommended remediation, and effort estimate. **Self-contained** — no cross-references required for first-pass review.

## How to review

1. Read `backlog.md` top-to-bottom (or jump to the **Decision Matrix** section near the end).
2. For each item, mark **INCLUDE** / **DEFER** / **OUT-OF-SCOPE** with notes.
3. Once decided, the INCLUDE items become the basis for `spec.md`.

## Recommended minimum R4 scope (per the backlog's Tier 1)

| Item | Effort | Why |
|---|---|---|
| Task 090 — R3 wrap-up (lessons-learned, README → Complete, repo cleanup) | ~2h | Closes R3 cleanly |
| Document Xrm.WebApi vs BFF decision criteria | ~2h | Every audit + deploy memo since R8 has flagged this |
| Document embedded-mode contract formally | ~3h | Informal today; new hosts would have to reverse-engineer |
| ADR-026 amendment (singlefile + heavy library handling) | ~4h | Future Code Pages will hit the same surprise |
| Task 065 — extend SessionPersistence for tab-state ⚠️ | ~8h | **Operator-visible gap**: tabs are NOT persisted across refresh |
| BFF governance follow-through (Placement Justification rule + publish-size check) | ~2h | CLAUDE.md §10 binding |
| **Tier 1 total** | **~21h (~3 days)** | |

If R4 has more appetite, Tier 2 (build hygiene) adds ~14.5h and Tier 3 (code quality / dev-ex) adds ~18h. See `backlog.md` for details.

## After scoping decisions

Once review is complete, the standard pipeline applies:

1. Write `spec.md` from the INCLUDE items.
2. Optionally write `design.md` for cross-cutting design decisions.
3. Run `/project-pipeline projects/spaarke-ai-platform-unification-r4` — generates README/plan/CLAUDE.md/current-task.md/tasks/, identifies dependencies, creates POML task files.

---

*This is a scoping artifact, not an executing project. It exists to make the review pass efficient.*
