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

This command executes the `adr-check` skill to validate that code complies with all 12 Spaarke ADRs.

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/adr-check/SKILL.md`
2. **Load validation rules**: Read `.claude/skills/adr-check/references/adr-validation-rules.md`
3. **Check all applicable ADRs** for the code path
4. **Output structured findings** with ADR references

## Skill Location

`.claude/skills/adr-check/SKILL.md`

## ADRs Validated

| ADR | Title | Key Check |
|-----|-------|-----------|
| ADR-001 | Minimal API + BackgroundService | No Azure Functions |
| ADR-002 | Thin Dataverse plugins | No HTTP calls in plugins, <50ms |
| ADR-003 | Lean authorization | UAC + file storage seams |
| ADR-004 | Async job contract | Standard JobContract format |
| ADR-005 | Flat SPE storage | No folder hierarchies |
| ADR-006 | PCF over webresources | No new legacy JS |
| ADR-007 | SpeFileStore facade | No Graph SDK leakage |
| ADR-008 | Endpoint filters | No global auth middleware |
| ADR-009 | Redis-first caching | No hybrid L1 without proof |
| ADR-010 | DI minimalism | ≤15 registrations |
| ADR-011 | Convention types | Fluent UI v9, Tailwind banned |
| ADR-012 | Shared component library | Use @spaarke/ui-components |

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
