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

This command executes the `adr-check` skill to validate that code complies with the current ADR index in `docs/adr/INDEX.md` (currently ADR-001–ADR-021).

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/adr-check/SKILL.md`
2. **Load validation rules**: Read `.claude/skills/adr-check/references/adr-validation-rules.md`
3. **Check all applicable ADRs** for the code path
4. **Output structured findings** with ADR references

## Skill Location

`.claude/skills/adr-check/SKILL.md`

## Key ADRs by Domain

| Domain | ADRs |
|--------|------|
| **UI/UX** | ADR-006, ADR-011, ADR-012, **ADR-021** (Fluent v9) |
| **Backend** | ADR-001, ADR-004, ADR-010, ADR-017, ADR-019 |
| **Security** | ADR-003, ADR-008 |
| **AI** | ADR-013, ADR-014, ADR-015, ADR-016 |
| **Dataverse** | ADR-002 |

## Output Format

```
ADR Compliance Report
=====================

✅ ADR-001: Minimal API - PASS
✅ ADR-006: PCF pattern - PASS
❌ ADR-010: DI minimalism - FAIL
   - Found 18 DI registrations (limit: 15)
   - Location: Program.cs:45-80
❌ ADR-021: Fluent v9 design system - FAIL
   - Found @fluentui/react (v8) import, should use @fluentui/react-components (v9)
   - Location: MyControl.tsx:3
⚠️ ADR-007: SpeFileStore - WARNING
   - GraphServiceClient exposed in controller constructor
   - Location: DocumentController.cs:12
```

## Related Skills

- `adr-aware` - Proactively loads ADRs before creating code
- `code-review` - Includes ADR checks as part of review

