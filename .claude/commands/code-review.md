# /code-review

Review code changes for quality, security, and standards compliance.

## Usage

```
/code-review [files-or-path]
```

**Examples**:
- `/code-review` - Review recent changes
- `/code-review src/server/api/` - Review specific directory
- `/code-review src/client/pcf/SpeFileViewer/` - Review PCF control

## What This Command Does

This command executes the `code-review` skill to perform comprehensive code review covering:
- Security vulnerabilities
- Performance issues
- Code style and conventions
- ADR compliance
- Test coverage

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/code-review/SKILL.md`
2. **Load the checklist**: Read `.claude/skills/code-review/references/review-checklist.md`
3. **Follow the review procedure** systematically
4. **Output structured findings** with severity levels

## Skill Location

`.claude/skills/code-review/SKILL.md`

## Review Categories

| Category | Focus Areas |
|----------|-------------|
| **Security** | Auth, input validation, secrets, injection |
| **Performance** | N+1 queries, caching, async patterns |
| **Standards** | ADR compliance, naming, patterns |
| **Testing** | Coverage, edge cases, mocks |
| **Documentation** | Comments, API docs, README |

## Output Format

Findings are categorized by severity:
- 🔴 **Critical** - Must fix before merge
- 🟠 **Warning** - Should fix, creates tech debt
- 🟡 **Suggestion** - Nice to have improvement
- 🟢 **Good** - Positive pattern observed

## Related Skills

- `adr-check` - For ADR-specific validation
- `spaarke-conventions` - For naming/pattern standards
