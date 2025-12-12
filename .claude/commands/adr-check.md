# /adr-check

Validate code against Architecture Decision Records (ADRs).

## Usage

```
/adr-check [path]
```

**Examples**:
- `/adr-check` - Check entire codebase
- `/adr-check src/server/api/` - Check API code
- `/adr-check src/client/pcf/` - Check PCF controls

## What This Command Does

This command executes the `adr-check` skill to validate that code complies with the current ADR index in `docs/reference/adr/README-ADRs.md` (currently ADR-001–ADR-020).

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/adr-check/SKILL.md`
2. **Load validation rules**: Read `.claude/skills/adr-check/references/adr-validation-rules.md`
3. **Check all applicable ADRs** for the code path
4. **Output structured findings** with ADR references

## Skill Location

`.claude/skills/adr-check/SKILL.md`

## ADRs Validated

All ADRs listed in `docs/reference/adr/README-ADRs.md`.

## Output Format

```
ADR Compliance Report
=====================

✅ ADR-001: Minimal API - PASS
✅ ADR-006: PCF pattern - PASS
❌ ADR-010: DI minimalism - FAIL
   - Found 18 DI registrations (limit: 15)
   - Location: Program.cs:45-80
⚠️ ADR-007: SpeFileStore - WARNING
   - GraphServiceClient exposed in controller constructor
   - Location: DocumentController.cs:12
```

## Related Skills

- `adr-aware` - Proactively loads ADRs before creating code
- `code-review` - Includes ADR checks as part of review
