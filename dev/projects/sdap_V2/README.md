# Claude Code Context Files

This directory contains architecture decision records (ADRs), refactoring instructions, and guidelines for the SDAP BFF API refactoring project.

## Quick Start for Claude Code

### Phase Execution Order
1. **Phase 1**: Configuration & Critical Fixes → `.claude/phase-1-instructions.md`
2. **Phase 2**: Service Layer Simplification → `.claude/phase-2-instructions.md`
3. **Phase 3**: Feature Module Pattern → `.claude/phase-3-instructions.md`
4. **Phase 4**: Graph Token Caching → `.claude/phase-4-instructions.md`

### Before Starting Any Phase

Read these context files first:
- `project-context.md` - Project constraints and technology stack
- `architectural-decisions.md` - ADR enforcement rules
- `code-patterns.md` - Correct patterns to follow
- `anti-patterns.md` - What NOT to do

### During Each Phase

Refer to:
- `codebase-map.md` - Where to find files
- `refactoring-checklist.md` - Validation steps
- `testing-strategy.md` - How to validate changes

### After All Phases

Check:
- `success-metrics.md` - Did we achieve the goals?

## File Index

| File | Purpose | When to Read |
|------|---------|--------------|
| `project-context.md` | Constraints, tech stack, repo structure | Before Phase 1 |
| `architectural-decisions.md` | ADR-007, ADR-009, ADR-010 rules | Before each phase |
| `code-patterns.md` | Examples of correct code | During implementation |
| `anti-patterns.md` | Common mistakes to avoid | During implementation |
| `codebase-map.md` | File locations and structure | When modifying code |
| `refactoring-checklist.md` | Validation steps per phase | After each task |
| `testing-strategy.md` | How to test changes | After each phase |
| `success-metrics.md` | Definition of done | Final validation |
| `phase-1-instructions.md` | Configuration fixes | Phase 1 execution |
| `phase-2-instructions.md` | Storage simplification | Phase 2 execution |
| `phase-3-instructions.md` | Feature modules | Phase 3 execution |
| `phase-4-instructions.md` | Token caching | Phase 4 execution |

## Git Workflow
```bash
# Create refactoring branch
git checkout -b refactor/adr-compliance

# After each phase validation
git add .
git commit -m "refactor(phase-N): description"

# After all phases complete
git push origin refactor/adr-compliance
# Create pull request