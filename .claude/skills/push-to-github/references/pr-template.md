# Spaarke PR Body Template

> **Parent skill**: [push-to-github](../SKILL.md)
> **Extracted**: 2026-05-16 from SKILL.md by ai-procedure-quality-r1 (Phase 2b Wave 2b-B)

This template is the canonical Spaarke PR body format. Pass it to `gh pr create --body-file` OR copy-paste into the GitHub web UI when creating PRs manually.

## Template content

```markdown
## Summary
{Brief description of changes — 1-2 sentences, focused on the WHY}

## Related
- Closes #{issue number} (if applicable)
- Related to: {link to spec or design doc}
- ADRs touched: {ADR numbers, if any}

## Changes
- {Change 1 — be specific; what files/components}
- {Change 2}
- {Change 3}

## Testing
- [ ] Unit tests pass (`dotnet test`)
- [ ] Manual testing completed (describe what was tested)
- [ ] ADR compliance verified (`/adr-check` clean)
- [ ] UI changes verified in browser (if frontend)

## Checklist
- [ ] Code follows Spaarke conventions (`spaarke-conventions` skill)
- [ ] Documentation updated (if behavior changed)
- [ ] No secrets or sensitive data committed
- [ ] CI green before requesting review
```

## Usage

### Option 1: Via `gh` with body file

```bash
gh pr create --title "{title}" --body-file .claude/skills/push-to-github/references/pr-template.md
```

Then edit the resulting PR description to fill in placeholders (the `{...}` parts).

### Option 2: Via `gh` with heredoc (filling in placeholders inline)

```bash
gh pr create --title "{title}" --body @- << 'EOF'
## Summary
{Brief description}

## Related
- Closes #{N}

## Changes
- {What changed}

## Testing
- [ ] Tests pass
- [ ] Manual verification

## Checklist
- [ ] Conventions followed
- [ ] No secrets committed
EOF
```

### Option 3: Manual (web UI)

Copy the **Template content** block above; paste into the GitHub PR description field; replace placeholders.

## Customization notes

- For PCF/Code Page PRs, add a `## Bundle Size` section with `before` and `after` sizes (per `pcf-deploy` AP-1 lessons).
- For BFF deploys, add a `## Hash-Verify Status` section showing the SHA-256 match counts (per `bff-deploy` Failure Modes & Recovery).
- For Dataverse schema PRs, add a `## Schema Changes` section with affected entity logical names.
- Keep summary under 200 words. Detailed analysis belongs in the linked design doc or ADR, not the PR description.
