---
description: Commit changes and push to GitHub following Spaarke git conventions
tags: [git, push, github, pr, commit, operations]
techStack: [git, gh-cli]
appliesTo: ["push to github", "create PR", "commit and push", "ready to merge"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-05-16
---

# Push to GitHub

> **Category**: Operations
> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-B — normalized minimal frontmatter; extracted PR body template to references/)
> **Exemplar rationale**: Each push targets ephemeral branch state — no canonical reference holds.
> **PR body template**: [`references/pr-template.md`](references/pr-template.md)

---

## Purpose

Automate the git workflow from staged changes to pull request creation. Ensures code quality checks run before commits, generates conventional commit messages, and creates well-documented PRs that link to related issues and specs.

---

## Applies When

- User wants to push code to GitHub
- Creating a pull request
- Committing completed work
- **Trigger phrases**: "push to github", "create PR", "commit and push", "ready to merge", "submit changes"

---

## Prerequisites

1. **Git configured**: `git config user.name` and `git.config user.email` set
2. **On a branch**: Should NOT be on `main` or `master` for feature work
3. **GitHub CLI (optional)**: `gh` CLI for automated PR creation

---

## Worktree Support

When working in a **git worktree** (e.g., `spaarke-wt-{project-name}`), additional sync is required:

### Architecture Understanding

```
┌─────────────────────────────────────────────────────────────┐
│  Main Repo (C:/code_files/spaarke)                          │
│  └─ LOCAL master branch ← needs explicit pull after merge   │
├─────────────────────────────────────────────────────────────┤
│  Worktree (C:/code_files/spaarke-wt-{project})              │
│  └─ feature/work branch → pushes to origin/master           │
├─────────────────────────────────────────────────────────────┤
│  GitHub (origin/master) ← "merge to master" updates this    │
└─────────────────────────────────────────────────────────────┘
```

### Worktree Detection

```
DETECT worktree:
  git rev-parse --git-common-dir

  IF output contains ".git/worktrees":
    → Working in a worktree
    → MAIN_REPO_PATH = git rev-parse --git-common-dir (parent of .git/worktrees)
    → After merge to master, MUST sync main repo
```

### Auto-Sync After Merge (MANDATORY for Worktrees)

When merging to master from a worktree, **always** sync the main repo:

```powershell
# After pushing branch:master
cd {MAIN_REPO_PATH}
git fetch origin
git pull origin master
```

This ensures the main repo's local master matches origin/master.

---

## When to Create a PR

PRs should be created **early in the project lifecycle** for visibility:

| Stage | Action | PR State |
|-------|--------|----------|
| After project artifacts created | Create feature branch | No PR yet |
| After first meaningful commit | Create draft PR | **Draft** |
| Implementation complete | Mark PR ready | **Ready for Review** |
| After code review passes | Merge to master | Merged |

### Recommended Workflow

1. **Project start** (after `/design-to-project` Phase 3):
   ```powershell
   git checkout -b feature/{project-name}
   git add projects/{project-name}/
   git commit -m "feat({scope}): initialize {project-name} project"
   git push -u origin feature/{project-name}
   ```

2. **Create draft PR** (for visibility):
   ```powershell
   gh pr create --draft --title "feat({scope}): {project-name}" --body "## Status\n- [ ] Implementation in progress"
   ```

3. **During implementation** (incremental commits):
   ```powershell
   git add .
   git commit -m "{type}({scope}): {description}"
   git push
   ```

4. **When ready for review**:
   ```powershell
   gh pr ready  # Converts draft to ready-for-review
   ```

5. **After approval** (merge to master):
   ```powershell
   gh pr merge --squash  # Or merge via GitHub UI
   ```

---

## Workflow

### Step 1: Pre-flight Checks

Before committing, verify code quality:

```
CHECK current branch:
  IF on main/master AND has changes:
    → WARN: "You're on the main branch. Create a feature branch first?"
    → SUGGEST: git checkout -b feature/{description}

CHECK for uncommitted changes:
  git status --porcelain
  IF no changes:
    → "No changes to commit. Nothing to do."
    → STOP

RUN quality checks (ask user first):
  → "Should I run linting, code review, and ADR check before committing? (recommended)"
  IF yes:
    → Execute linting on changed files:
      • TypeScript/PCF: cd src/client/pcf && npm run lint
      • C#: dotnet build --warnaserror (Roslyn analyzers)
    → Execute /code-review on changed files
    → Execute /adr-check on changed files
    → Report any issues found
    → IF lint errors OR critical issues: STOP and ask user to fix first
```

### Step 1.5: Check for Untracked Source Files (MANDATORY)

**This step prevents accidentally leaving source files uncommitted.**

```
CHECK for untracked source files:
  git status --porcelain | grep "^??" | grep -E "\.(cs|ts|tsx|ps1|js|json|md)$"

  IF untracked source files found:
    → 🚨 WARNING: Untracked source files detected!
    → List all untracked source files with paths
    → ASK: "These files are NOT staged for commit. Actions:"
      1. Add all to this commit (git add {files})
      2. Add to .gitignore (if intentionally excluded)
      3. Review each file individually
      4. Abort and investigate
    → REQUIRE explicit user decision before proceeding
    → IF user chooses to add: git add {files}
    → IF user chooses to ignore: Confirm files are truly not needed
    → DO NOT proceed to Step 2 until resolved

  IF no untracked source files:
    → Continue to Step 1.6

RATIONALE: Untracked source files are a common cause of "missing code after merge"
issues. This check ensures all source files are explicitly handled before push.
```

**Source file patterns to check:**
- `.cs` - C# source files
- `.ts`, `.tsx` - TypeScript/React files
- `.js` - JavaScript files
- `.ps1` - PowerShell scripts
- `.json` - Configuration files (in src/ directories)
- `.md` - Documentation (in docs/ or project directories)

### Step 1.6: Defer / Issue Tracking Audit (MANDATORY when inside a project)

**Pairs with the `/project-defer-issue-tracking` skill. Catches entries that were added to project notes but never filed as GitHub Issues — the whole point of the two-write rule is visibility.**

```
DETECT if we are inside a project:
  project_path = projects/{name} that contains the current working dir
                 OR matches the git branch name (work/{project-name})

IF inside a project:
  notes_path = projects/{name}/notes/defer-issues.md

  IF notes_path exists:
    SCAN for entries with empty or placeholder GitHub Issue URL:
      grep -E "^\| \*\*GitHub Issue\*\* \| (\{URL\}|\s*\|)" $notes_path
      OR look for entries where the GitHub Issue row contains the literal `{URL}` placeholder

    IF unfiled entries found:
      → 🚨 WARNING: N defer/issue entries in `notes/defer-issues.md` have no GitHub Issue URL.
      → List the entries (ID + title)
      → ASK: "These tracking entries are not visible to the team. Actions:"
        1. File them now via /project-defer-issue-tracking (recommended)
        2. Continue push without filing (NOT recommended — they'll be invisible)
        3. Abort and review the entries
      → IF user picks 1: invoke /project-defer-issue-tracking for each unfiled entry
      → IF user picks 2: warn explicitly + log the choice in commit message footer
      → DO NOT silently proceed

    IF all entries have GitHub Issue URLs:
      → Continue to Step 2

  IF notes_path does NOT exist:
    → No tracking file yet — continue to Step 2 (nothing to audit)

IF NOT inside a project (ad-hoc work at repo root):
  → Skip this step (deferral protocol is project-scoped)
```

**Why this step exists**: deferred work hidden in a project's `notes/` folder is invisible to anyone working on other projects. Surfacing entries as GitHub Issues at push time is the latest-possible reliable hook for visibility. See `/project-defer-issue-tracking` skill for the full protocol.

### Step 1.7: Real-Dataverse Smoke Check (Widget/Dataverse Changes)

**Added 2026-08-17 by `smart-todo-r5` task 060 per spec FR-20 / PROC-1. Advisory (ask-user-first), NOT a blocking CI gate — same shape as Steps 1.5/1.6.**

```
DETECT Dataverse-querying widget/component changes in this push:
  git diff --name-only origin/{branch}..HEAD (or the staged set)
  FLAG files that are widget/component/service changes which QUERY Dataverse
  entities — e.g. anything touching Xrm.WebApi / IDataverseClient / a
  `sprk_*` (or OOB `contact`/`systemuser`/…) entity name in a
  `retrieveMultipleRecords` / FetchXML / OData `$select` path.

  IF such changes are present:
    → 🚨 WARNING: This push changes code that queries Dataverse entities.
    → List the affected files.
    → ASK: "Before merge, this change MUST have been exercised with ≥1
       create + read against REAL Dataverse (not a prototype / mock harness).
       Confirm:"
      1. Yes — I ran a real create+read against real Dataverse (name the env)
      2. No — exercised only against a mock/prototype harness (NOT sufficient)
      3. N/A — this change does not actually touch a Dataverse query path
    → IF user picks 2: 🚨 WARN explicitly that mock-only verification is the exact
       failure this gate guards against; recommend a real-DV smoke before merge;
       if the user still proceeds, log the choice in the commit/PR footer.
    → REQUIRE an explicit decision — DO NOT silently proceed.

  IF no Dataverse-querying changes:
    → Continue to Step 2.
```

**Why this step exists**: R4 UAT rounds 5–6 burned multiple deploy cycles because the `spaarke-prototype` harness mocked a `sprk_contact` entity that **does not exist in real Dataverse** — the real entity is the OOB `contact`. The mock hid the entity-name bug until it reached a real environment. A mock passing proves nothing about real Dataverse's actual schema; only a real create+read does. R5's own FR-04 (RegardingResolver wiring, smoke-tested by task 014) and FR-06 (Assigned-To typeahead) satisfy this gate by example. This is **reviewer judgment**, not enforcement code — like `/test-diet` and the Step 1.6 defer audit, it surfaces a WARNING and asks; it never runs a `dotnet`/CI script or hard-blocks the merge.

### Step 2: Review Changes

```powershell
# Show what will be committed
git status

# Show diff summary
git diff --stat

# For detailed review
git diff
```

Present summary to user:
```
📋 Changes to commit:
  Modified: {N} files
  Added: {N} files  
  Deleted: {N} files

Files:
  M  src/server/api/SomeFile.cs
  A  src/client/pcf/NewComponent/index.ts
  D  src/old/deprecated.js

Proceed with commit? (y/n)
```

### Step 3: Stage Changes

```powershell
# Stage all changes (default)
git add .

# Or stage specific files if user requests
git add {specific files}
```

### Step 4: Generate Commit Message

Follow **Conventional Commits** format:

```
{type}({scope}): {description}

{body - optional}

{footer - optional}
```

#### Commit Types

| Type | When to Use |
|------|-------------|
| `feat` | New feature |
| `fix` | Bug fix |
| `docs` | Documentation only |
| `style` | Formatting, no code change |
| `refactor` | Code change that neither fixes nor adds |
| `perf` | Performance improvement |
| `test` | Adding or fixing tests |
| `chore` | Build process, dependencies, tooling |

#### Scope (Spaarke-specific)

| Scope | Area |
|-------|------|
| `api` | BFF API changes |
| `pcf` | PCF control changes |
| `plugin` | Dataverse plugin changes |
| `dataverse` | Dataverse configuration/ribbon |
| `infra` | Infrastructure/Bicep |
| `docs` | Documentation |
| `deps` | Dependency updates |

#### Generate Message

```
ANALYZE changed files to determine:
  - Primary type (feat/fix/refactor/etc.)
  - Scope (api/pcf/plugin/etc.)
  - Brief description (imperative mood, <50 chars)

PROPOSE commit message:
  "{type}({scope}): {description}"

ASK user to confirm or modify
```

**Example messages:**
- `feat(pcf): add dark mode theme selector to command bar`
- `fix(api): resolve token caching race condition`
- `refactor(dataverse): update ribbon XML for UCI compatibility`
- `docs(skills): add pr-workflow skill for git automation`

### Step 5: Commit

```powershell
git commit -m "{approved message}"
```

### Step 6: Push to Remote

```powershell
# Push current branch to origin
git push origin HEAD

# If branch doesn't exist on remote yet
git push -u origin HEAD
```

### Step 7: Create or Update Pull Request

#### First: Check for Existing PR

```powershell
# Check if PR already exists for this branch
gh pr list --head {current-branch} --state open --json number,url,title
```

```
IF PR exists:
  → "✅ PR #{number} already exists: {title}"
  → "   {PR URL}"
  → "   Changes pushed to existing PR."
  → SKIP PR creation
  → DONE

IF no PR exists:
  → "No PR found for branch '{branch}'. Create one? (y/n)"
  → IF user says no:
      → "Pushed to remote. Create PR manually when ready:"
      → "  https://github.com/spaarke-dev/spaarke/compare/{branch}?expand=1"
      → DONE
  → IF user says yes:
      → Continue to PR creation below
```

#### Create New PR: Using GitHub CLI (Preferred)

```powershell
# Check if gh is available
gh --version

# Create PR interactively
gh pr create --title "{commit message}" --body "{PR body}"

# Or with full template (see references/pr-template.md for the canonical body content)
gh pr create --title "{title}" --body-file .claude/skills/push-to-github/references/pr-template.md
```

**PR body template**: See [`references/pr-template.md`](references/pr-template.md) for the full Spaarke PR body convention (Summary, Related, Changes, Testing, Checklist) — plus customizations for PCF, BFF deploy, Dataverse schema PRs.

#### Create New PR: Manual (Browser)

```
PROVIDE GitHub PR URL:
  https://github.com/spaarke-dev/spaarke/compare/{branch}?expand=1

SUGGEST PR template content for user to paste
```

### Step 8: Monitor CI Status

After pushing, CI runs automatically. **Two systems run in parallel today** (updated 2026-08-29):

| Workflow | Role | Blocking? |
|---|---|---|
| **`CI` (Router)** → `ci-tier1-blocking.yml` | Compile, Arch Tests (MUST-NOT subset), auth/tenant/eval/fidelity gates. Target p95 ≤3 min. | **Yes** — this is the verdict that matters |
| **`CI` (Router)** → `ci-tier2-advisory.yml` | Full unit tests, format, lint, ADR NetArchTest, markdown links, plugin size | **No — advisory by design.** A red Tier 2 does **not** block a merge |
| **`SDAP CI`** (legacy `sdap-ci.yml`) | Build & Test, Code Quality, Trivy | Running in parallel pending retirement (task CICD-077) |

Tier 2 being advisory is deliberate, not an oversight — it exists so slow/flaky checks cannot hold up
high-frequency master pushes. Do **not** "fix" a red Tier 2 by making it blocking.

```powershell
gh pr checks {N}
```

```
WAIT until EVERY check reports a TERMINAL state (pass / fail / skipping).

🚨 A zero-failure count while other checks are still `pending` is NOT a green build —
   it is a measurement taken too early. Count pending explicitly:

     gh pr checks {N} | grep -c pending      # must be 0 before you judge the result

   This is the same error class as trusting a too-small test fixture: the observation
   was taken before the thing being observed had happened.

IF a TIER 1 check fails:
  → gh run view {run-id} --log-failed
  → Read the FAILING STEP name before assuming it is your code. An infrastructure step
    (Checkout, setup-dotnet, artifact upload) failing is not a code defect.
  → Fix, commit, push; CI re-runs automatically.

IF only TIER 2 checks fail:
  → Advisory. Report it, judge whether it is real, do NOT treat it as a merge blocker.

IF all terminal and no Tier 1 failures:
  → Ready for review/merge
```

#### 🚨 `--delete-branch` races queued jobs

`gh pr merge --delete-branch` removes the branch immediately. **Any job still queued then fails at
`Checkout`**, unable to fetch a ref that no longer exists — producing a red that looks like a quality
regression and is not.

This happened on **#890**: the legacy `Code Quality` job started 24 s after the merge and failed on
checkout; no quality check ever ran. The tell is `The process '...git.exe' failed with exit code 1`
during **Checkout**, often with a downstream `No files were found with the provided path: ...trx`.

**Prevention**: confirm `grep -c pending` is `0` *before* merging with `--delete-branch`. If you hit it
anyway, verify the merge commit itself is green (`gh api "repos/{owner}/{repo}/actions/runs?head_sha={merge_sha}"`)
— that, not the branch-side run, is what the shadow window reads.

### Step 9: Summary

```
✅ PR Workflow Complete

Branch: {branch-name}
Commit: {short-sha} - {commit message}
PR: {PR URL or "Create manually at {URL}"}
CI Status: gh pr checks (run to verify)

Next steps:
1. Monitor CI: gh pr checks --watch
2. Fix any CI failures
3. Request reviewers (when CI green)
4. Merge when approved and CI passes
```

### Step 10: Merge to Master (When Ready)

> **⚠️ Corrected 2026-08-29.** This step previously read *"Push branch to master:
> `git push origin {branch}:master`"*. That is a **direct push that bypasses the PR**, and it is not how
> work lands on this repo. Every PR merged in the CI-remediation project used `gh pr merge --squash`.
>
> A direct push also **starves the CI cutover measurement**: `scripts/ci/shadow-window-status.ps1`
> enumerates merged **PRs** and compares the legacy vs. new workflow verdict on each merge commit.
> Work pushed straight to master creates no PR, contributes no comparison, and therefore *delays*
> retiring `sdap-ci.yml`.
>
> **Master currently has no branch protection** (verified 2026-08-29; the intentional pre-cutover
> state). That is not permission to push directly — see `merge-to-master` Step 3. Task CICD-071
> enables `CI / Router` as a required check, at which point a direct push is refused outright.
>
> **Prefer `/merge-to-master`**, which runs the pre-merge branch update + conflict resolution (its
> Step 2.5) that this skill does not.

When user requests "merge to master" or "merge and sync":

```
1. Verify CI reached a TERMINAL state (not merely "no failures yet") — see Step 8:
   gh pr checks {N}

2. Merge via the PR (squash is this repo's convention):
   gh pr merge {N} --squash --delete-branch

3. IF in worktree (MANDATORY):
   MAIN_REPO=$(git rev-parse --git-common-dir | sed 's|/.git/worktrees.*||')
   cd "$MAIN_REPO"
   git fetch origin
   git pull origin master
   → Report: "✅ Main repo synced to {commit-sha}"

4. Summary:
   ✅ Merged to master
   ✅ Remote origin/master updated
   ✅ Main repo local master synced (if worktree)
```

**Important**: "Merge to master" updates origin/master but does NOT automatically update the main repo's local master when working in a worktree. Step 3 ensures full sync.

---

## Conventions

### Branch Naming

| Type | Pattern | Example |
|------|---------|---------|
| Feature | `feature/{description}` | `feature/dark-mode-theme` |
| Bug fix | `fix/{description}` | `fix/token-cache-race` |
| Hotfix | `hotfix/{description}` | `hotfix/prod-auth-failure` |
| Project | `project/{project-name}` | `project/mda-darkmode-theme` |

### Commit Message Rules

- **Imperative mood**: "add feature" not "added feature"
- **No period** at end of subject line
- **Subject ≤ 50 chars**, body ≤ 72 chars per line
- **Reference issues** in footer: `Closes #123` or `Refs #456`

---

## Error Handling

| Situation | Response |
|-----------|----------|
| On main/master branch | Warn user, suggest creating feature branch |
| No changes to commit | Inform user, stop workflow |
| Code review finds critical issues | Report issues, ask user to fix before continuing |
| Push rejected (behind remote) | Suggest `git pull --rebase origin {branch}` |
| Push rejected (no upstream) | Use `git push -u origin HEAD` |
| `gh` CLI not installed | Fall back to manual PR creation with URL |
| Merge conflicts | Stop and guide user through resolution |

---

## Related Skills

- `code-review` - Run before committing to catch issues
- `adr-check` - Validate ADR compliance before committing
- `spaarke-conventions` - Naming and coding standards
- `ci-cd` - Monitor CI pipeline status and troubleshoot failures
- `merge-to-master` - Merge branch into master after push (pushing ≠ merging)
- `worktree-sync` - Full bidirectional worktree sync (commit → push → merge → update from master)

---

## Quick Reference

```powershell
# Full workflow in commands
git status                              # Review changes
git add .                               # Stage all
git commit -m "type(scope): message"    # Commit
git push origin HEAD                    # Push
gh pr create                            # Create PR (if gh installed)
```

---

## Tips for AI

- **CRITICAL: Always run Step 1.5 (untracked source file check) before ANY commit/push**
- Untracked files have caused code loss - treat this check as mandatory, not optional
- **Step 1.7 (real-Dataverse smoke): for any push that changes Dataverse-querying widget/component/service code, ASK whether a real create+read against real Dataverse was exercised — a mock/prototype harness passing is NOT sufficient (R4 `sprk_contact`-vs-OOB-`contact` regression). Advisory (ask-user-first), not a blocking CI gate.**
- Always show `git status` before committing so user sees what's included
- Propose a commit message based on changed files - don't just ask user to write one
- If user is on main/master, strongly recommend creating a feature branch first
- Run `/code-review` and `/adr-check` by default unless user declines
- For large changesets, suggest breaking into multiple commits
- Always provide the GitHub compare URL even if `gh` CLI creates the PR
- Include project/issue references in PR body when context is available
- After push, **always run `gh pr checks`** to show CI status
- If CI fails, use `gh run view {id} --log-failed` and **read the failing STEP name first** — an
  infrastructure step (Checkout, setup-dotnet) failing is not a code defect
- **Never judge a result while checks are `pending`.** "0 failures so far" is not "passing";
  confirm `gh pr checks {N} | grep -c pending` is `0` first
- Merge on a green **Tier 1**. **Tier 2 is advisory and does not block** — report a red Tier 2, judge
  whether it is real, but do not treat it as a merge blocker (that is the design, per north star
  "CI must not hold up high-frequency pushes")
- **Do not edit `ci-router.yml` / `ci-tier1-blocking.yml` / `ci-tier2-advisory.yml` while the shadow
  window is open** — changing the configuration invalidates what was observed and restarts the
  cutover clock. Check with `pwsh scripts/ci/shadow-window-status.ps1`. Narrow exception, already
  set by PRs #865/#890: adding a guard whose live count is **zero** is verdict-neutral, since no
  existing PR's result can change.
- Reference `ci-cd` skill for detailed troubleshooting guidance
- **After successful push, always remind**: "Branch pushed to origin. When ready to merge this work into master, run `/merge-to-master`." Pushing to origin does NOT merge to master — these are separate operations.

### Worktree-Specific Tips

- **ALWAYS** detect if working in a worktree before merge operations
- After merging to master, **ALWAYS** sync the main repo's local master
- Use `git rev-parse --git-common-dir` to find the main repo path
- Report both remote AND local sync status to user
- If user says "merge to master", this means: push to origin/master AND sync main repo

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Untracked files (secrets, build artifacts) committed accidentally | Used `git add .` or `git add -A` blindly | NEVER use `-A` or `.` without first running `git status` and reviewing each untracked file. Step 1.5 of the workflow is the explicit untracked-file check. |
| Pushed broken code that fails CI | Tests or build not run locally before push | MANDATORY: `dotnet build src/server/api/Sprk.Bff.Api/` AND `dotnet test` BEFORE `git push`. The skill enforces this at Step 6. |
| PR opened against the wrong base branch | `gh pr create` defaulted to a non-master base | Always pass `--base master` explicitly when creating PRs. Verify before clicking "Create PR" in the web UI. |
| Force-pushed to a shared branch and overwrote teammate's work | Used `--force` instead of `--force-with-lease` | NEVER `--force` on shared branches. `--force-with-lease` rejects the push if the remote moved since your last fetch. |
| Worktree merge-to-master succeeded but main repo's local master is stale | Forgot Step 10.3 (sync main repo) | Always run `git rev-parse --git-common-dir` to find main repo path; `cd $MAIN_REPO && git pull origin master`. Skill enforces this when in worktree. |
| PR description copy-pasted from another PR with wrong issue link | Reused template without updating placeholders | Use `references/pr-template.md` with `--body-file` then EDIT the PR description on GitHub — don't leave placeholder text. |
