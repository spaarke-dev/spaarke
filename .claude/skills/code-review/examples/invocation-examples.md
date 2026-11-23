# Invocation examples — scoping a review from the request

Read by `code-review` (Inputs) when a request names a depth or a narrow focus. Whatever the depth or focus, a finding you make is reported (Step 0); depth and focus decide where you look.

## Example 1: Review Git Changes
**Trigger**: "Review my changes before I commit"

**Process**:
1. Run `git diff --name-only` to get changed files (BASE = HEAD)
2. Read each file, apply every Workflow step
3. Output categorized findings

## Example 2: Focused Security Review
**Trigger**: "Do a security review of the auth endpoints"

**Process**:
1. Find files related to authentication
2. Focus on the Step 3 security checks (and ADR-008 / ADR-028 in Step 6)
3. Report security-specific findings; anything else you notice on the way still goes in the report, under its own category

## Example 3: Thorough Pre-PR Review
**Trigger**: "Thorough code review for PR"

**Process**:
1. Get all changed files vs master (`git diff $(git merge-base HEAD master) --name-only`; BASE = the merge base)
2. Run every Workflow step at thorough depth
3. Include suggestions for improvement
4. Run full adr-check

## Example 4: Quick Hotfix Review
**Trigger**: "/code-review quick src/api/fix.cs"

**Process**:
1. Review the single file
2. Focus on critical issues
3. Style suggestions are out of scope at quick depth
