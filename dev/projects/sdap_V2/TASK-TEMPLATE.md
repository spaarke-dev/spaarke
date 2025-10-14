# Task Template

## Task Header
TASK ID: [Phase]-[TaskNumber]
PHASE: [Phase Name]
TASK: [Task Name]
DURATION: [Estimated time]
RISK: [Low/Medium/High]
DEPENDENCIES: [Previous task IDs]

## Pre-Flight Checklist

Before starting this task:
- [ ] Previous task validated and committed
- [ ] All tests passing
- [ ] Application running without errors
- [ ] Relevant context files read:
  - [ ] `.claude/project-context.md` (if first task)
  - [ ] `.claude/architectural-decisions.md` (ADR sections relevant to this task)
  - [ ] `.claude/anti-patterns.md` (sections relevant to this task)
  - [ ] `.claude/code-patterns.md` (patterns needed for this task)

## Task Objective

**What:** [One sentence - what needs to be done]

**Why:** [One sentence - why this is necessary]

**Success Criteria:** [Specific, measurable outcomes]

## Files to Modify

List exact files with line numbers (if known):
- `path/to/file.cs` (lines 45-60)
- `path/to/config.json` (section: AzureAd)

## Files to Create (if any)

- `path/to/new/file.cs`

## Files to Delete (if any)

- `path/to/obsolete/file.cs` (after validation only)

## Step-by-Step Instructions

### Step 1: [Action Verb + Specific Change]
```language
[Code example or configuration change]
Validation:

 [How to verify this step worked]

Step 2: [Action Verb + Specific Change]
language[Code example or configuration change]
Validation:

 [How to verify this step worked]

Step N: [Action Verb + Specific Change]
language[Code example or configuration change]