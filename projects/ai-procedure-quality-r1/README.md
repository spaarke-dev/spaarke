# AI Procedure Quality R1

> **Status**: Setup Complete — Ready for Phase 0 Execution
> **Started**: 2026-05-14
> **Branch**: `work/ai-procedure-quality-r1`
> **Effort estimate**: ~1 working week of agent execution + ~3 hours of human review
> **Cadence after launch**: monthly proactive audit; per-commit validation; quarterly deep audit

## Purpose

Harden the Claude Code AI procedure surface (`.claude/`, `CLAUDE.md`, `.github/workflows/`) against latent drift, schema malformation, and procedure dilution. Introduce the `researcher` subagent for accumulating external-platform knowledge. Establish a recurring monthly audit so the work stays valuable rather than decaying.

Triggered by four reactive bug-fixes in the 2026-05-14 working session that exposed the cost of having no proactive infrastructure: a skill that recommended the wrong build command (10× bundle regression), a 2-month-old settings.json schema malformation (permission rules silently disabled), a deploy script with the wrong health-check window for current Azure behavior, and 3 GitHub Actions workflows that have been failing in 0 seconds on every push.

## Scope

**In**: `.claude/skills/`, `.claude/patterns/`, `.claude/constraints/`, `.claude/adr/`, `.claude/settings.json`, `.claude/agents/` (new), `.claude/CHANGELOG.md` (new), `.claude/FAILURE-MODES.md` (new), `.claude/archive/` (new), `CLAUDE.md`, `.github/workflows/`, `scripts/quality/`, parts of `docs/procedures/` (for notification routing).

**Out**: `spaarke/knowledge/` content (parallel `coding-knowledge-base-setup-r1` project); MCP server cleanup; plugin marketplace; application code in `src/`.

## Graduation Criteria

This project is **done** when all of the following are true (mirrors spec.md "Success Criteria"):

1. Skills + CLAUDE.md audits complete; approved refinements applied; archived not deleted
2. `CLAUDE.md` is under 200 lines (ideally under 150) and routes rather than instructs
3. Every skill has a precise trigger description, a body under 200 lines, a Gotchas section, an opt-in/opt-out `exemplar:` field, and no contradictions with other skills
4. `researcher` subagent exists, has been invoked successfully, reads `spaarke/knowledge/` before searching externally
5. CI validates `.claude/settings.json` + `.mcp.json` schemas on every commit (<30s)
6. Pre-commit + GHA actionlint validation operational (pre-commit <3s budget)
7. Drift detector, reference-exemplar tester, bundle-size guard operational
8. 3 currently-failing GHA workflows are fixed (or explicitly disabled with rationale)
9. GitHub Actions pinned to commit SHAs; Dependabot enrolled for actions
10. `procedure-quality-audit` skill exists; monthly schedule running; first report produced
11. `.claude/CHANGELOG.md` (forward-only) + `.claude/FAILURE-MODES.md` exist and are referenced from CLAUDE.md
12. `docs/procedures/github-notification-routing.md` exists; team `dev@spaarke.com` routing confirmed by at least one user

## Quick Links

- [Specification (spec.md)](spec.md) — 20 functional requirements across 7 phases
- [Implementation Plan (plan.md)](plan.md) — phase-by-phase WBS
- [Task Index (tasks/TASK-INDEX.md)](tasks/TASK-INDEX.md) — task list with parallel groups
- [Project CLAUDE.md](CLAUDE.md) — agent context for this project
- [Design input (design.md)](design.md) — claude.ai consultation directives (preserved verbatim)
- [Additional best practices notes](notes/additional-best-practices.md) — deferred items + reasoning record

## Failure-mode evidence (the four 2026-05-14 issues)

| Issue | Commit that fixed it | Root cause |
|---|---|---|
| 1. `/pcf-deploy` skill said "NEVER use `build:prod`" | `c132773c` | Skill instruction was inverted; default `build` runs dev mode |
| 2. `.claude/settings.json` hooks malformed since 2026-03-14 | `8ca796ab` | Schema mismatch; flat `command` vs nested `hooks: [{type, command}]` |
| 3. `/bff-deploy` 60s health-check window was too short for Linux | `6d7bcf45` | Default sized for old Windows deploy path |
| 4. 3 GHA workflows fail in 0s on every push | (in progress in this project) | Likely action-version pins beyond what exists |

All four are now load-bearing evidence in spec.md for why this project is worth doing.

## What gets created and what gets changed

**New files** (Phase 1):
- `.claude/agents/researcher.md`
- `.claude/skills/_template/SKILL.md`
- `.claude/CHANGELOG.md`
- `.claude/FAILURE-MODES.md`
- `.claude/archive/.gitkeep`

**New skills** (Phase 5):
- `.claude/skills/procedure-quality-audit/SKILL.md`

**New validators / CI** (Phase 4):
- `scripts/quality/Validate-ClaudeSettings.ps1`
- `scripts/quality/Find-PatternDrift.ps1`
- `scripts/quality/Test-ReferenceExemplars.ps1`
- `scripts/quality/Check-BundleSizeDrift.ps1`
- `scripts/quality/Run-ProcedureQualityAudit.ps1`
- `scripts/quality/Schedule-MonthlyQualityAudit.ps1`
- `.github/workflows/procedure-quality.yml`
- `.github/workflows/actionlint.yml`

**Refactors** (Phases 2 + 3):
- Each of 49 skills under `.claude/skills/` audited; refinements applied per per-skill recommendation
- `CLAUDE.md` rewritten from 1190 lines to <200; extracted content moved to skills/docs/settings
- 3 GHA workflows fixed; others audited
- All workflow action references pinned to commit SHAs

**Archived** (`.claude/archive/2026-05-14/`):
- Original CLAUDE.md
- Any skills removed
- Old workflow YAML if replaced

## How to keep this from decaying back

The `procedure-quality-audit` skill (Phase 5) runs monthly. It:
1. Re-runs all 4 validators
2. Re-runs the reference-exemplar test harness against opted-in skills
3. Spot-checks 3 random opted-out skills manually
4. Cross-checks `CLAUDE.md` line count against the 200-line target
5. Reports diff vs prior month's report
6. Produces `.claude/QUALITY-AUDIT-<date>.md`

Human action expected within 1 week of each monthly run.

---

*This is round 1. R2 may follow once standing infrastructure surfaces patterns that need codifying.*
