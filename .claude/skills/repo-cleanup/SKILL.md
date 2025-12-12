---
description: Repository hygiene audit - validates structure compliance and removes ephemeral files after project completion
alwaysApply: false
---

# repo-cleanup

> **Category**: Quality  
> **Last Updated**: December 8, 2025

---

## Purpose

Ensures repository cleanliness and compliance with Spaarke conventions after project completion or during periodic maintenance. This skill validates directory structure, identifies orphaned or ephemeral files, and enforces repository standards to prevent technical debt accumulation.

---

## Applies When

- Project marked complete (Task 090 or equivalent wrap-up task)
- User says "clean up repo", "repo hygiene", "validate repository", or "check repo structure"
- Explicitly invoked with `/repo-cleanup` or `/repo-cleanup projects/{project-name}`
- Periodic maintenance (recommend: monthly or after major releases)
- Before merging feature branches to main

**NOT applicable when:**
- Project is actively in development (would delete legitimate working files)
- User only wants code review (use `code-review` skill instead)

---

## Workflow

### Step 1: Determine Scope

```
IF project path provided:
  SCOPE = projects/{project-name}/
  MODE = project-completion
ELSE:
  SCOPE = entire repository
  MODE = full-audit
```

### Step 2: Load Repository Standards

```
READ: docs/reference/architecture/SPAARKE-REPOSITORY-ARCHITECTURE.md
EXTRACT:
  - Valid directory patterns
  - Naming conventions
  - Required files per directory type
```

### Step 3: Audit Directory Structure

#### 3.1 Check Standard Directories Exist

```yaml
required_directories:
  - src/client/pcf/
  - src/server/api/
  - tests/unit/
  - tests/integration/
  - docs/ai-knowledge/
  - docs/reference/
  - infrastructure/bicep/
  - .claude/skills/

expected_files_per_directory:
  src/client/pcf/:
    - CLAUDE.md
    - package.json
    - tsconfig.json
  src/server/api/Sprk.Bff.Api/:
    - CLAUDE.md
    - Program.cs
    - appsettings.json
  .claude/skills/*/:
    - SKILL.md
  projects/*/:
    - spec.md (if initialized)
    - README.md (if initialized)
    - CLAUDE.md (if initialized)
```

#### 3.2 Identify Non-Standard Directories

```
SCAN: All directories not matching known patterns
FLAG: Directories that are:
  - Empty (except for .gitkeep)
  - Not referenced in Spaarke.sln (for .NET projects)
  - Not documented in SPAARKE-REPOSITORY-ARCHITECTURE.md
  - Named inconsistently (e.g., mixed case, spaces)
```

### Step 4: Identify Ephemeral Files to Remove

#### 4.1 Project Notes Cleanup (Project Completion Mode)

```yaml
ephemeral_directories:
  - projects/{project}/notes/debug/
  - projects/{project}/notes/spikes/
  - projects/{project}/notes/drafts/
  - projects/{project}/notes/scratch.md

preserve:
  - projects/{project}/notes/handoffs/ (archive, don't delete)
  - projects/{project}/notes/lessons-learned.md
```

#### 4.2 Build/Generated Artifacts

```yaml
generated_to_ignore_or_clean:
  - **/bin/
  - **/obj/
  - **/node_modules/
  - **/.vs/
  - **/publish/
  - **/*.user
  - **/out/
  - **/*.zip (except intentional archives)
  - **/*.tar.gz (except intentional archives)
  - **/logs.zip
```

#### 4.3 Orphaned Files

```yaml
potential_orphans:
  - .cs files not in any .csproj
  - .ts files not imported anywhere
  - .md files not linked from any document
  - Test files for deleted source files
```

### Step 5: Validate Naming Conventions

```yaml
naming_rules:
  directories:
    src/: kebab-case or PascalCase per type
    docs/: kebab-case
    projects/: kebab-case
    .claude/skills/: kebab-case
    
  files:
    .cs: PascalCase.cs
    .ts/.tsx: PascalCase.ts or kebab-case.ts
    .md: UPPERCASE-KEBAB.md or kebab-case.md
    .json: kebab-case.json or camelCase.json
    .bicep: kebab-case.bicep

violations_to_flag:
  - Spaces in file/directory names
  - Mixed conventions in same directory
  - Abbreviations not in glossary
```

### Step 6: Check for Required CLAUDE.md Files

```
FOR each code directory with significant content:
  IF no CLAUDE.md exists:
    FLAG: "Missing CLAUDE.md in {directory}"
    SUGGEST: Create from template

required_claude_locations:
  - / (root)
  - src/client/pcf/
  - src/server/api/Sprk.Bff.Api/
  - tests/
  - docs/
  - Each project folder with active work
```

### Step 7: Validate Git Hygiene

```yaml
git_checks:
  - Large files (> 10MB) not in .gitignore
  - Binary files that should be ignored
  - Sensitive patterns (secrets, keys, passwords)
  - Merge conflict markers left in files
```

### Step 8: Generate Report

```markdown
## Repository Cleanup Report

**Scope**: {Full Repository | projects/{project-name}}
**Date**: {timestamp}
**Mode**: {Project Completion | Full Audit | Pre-Merge}

### Summary
| Category | Issues Found | Auto-Fixable |
|----------|--------------|--------------|
| Structure | {count} | {count} |
| Ephemeral Files | {count} | {count} |
| Naming | {count} | {count} |
| Missing CLAUDE.md | {count} | {count} |
| Git Hygiene | {count} | {count} |

### Issues Found

#### 🗂️ Structure Issues
| Issue | Location | Severity | Action |
|-------|----------|----------|--------|
| {description} | {path} | {High/Medium/Low} | {recommended action} |

#### 🗑️ Ephemeral Files (Safe to Remove)
| File/Directory | Reason | Size |
|----------------|--------|------|
| {path} | {why ephemeral} | {size} |

#### 📛 Naming Violations
| Item | Current | Expected | Severity |
|------|---------|----------|----------|
| {path} | {current name} | {expected pattern} | {severity} |

#### 📋 Missing CLAUDE.md
| Directory | Content Type | Priority |
|-----------|--------------|----------|
| {path} | {code type} | {High/Medium/Low} |

#### ⚠️ Git Hygiene Issues
| Issue | Location | Action |
|-------|----------|--------|
| {description} | {path} | {recommended action} |

### Recommended Actions

#### Immediate (Auto-Fixable)
1. {action that can be automated}
2. {action that can be automated}

#### Manual Review Required
1. {action requiring human decision}
2. {action requiring human decision}

### Cleanup Commands (If Approved)

```powershell
# Remove ephemeral directories
Remove-Item -Recurse -Force "projects/{project}/notes/debug/"
Remove-Item -Recurse -Force "projects/{project}/notes/spikes/"
Remove-Item -Recurse -Force "projects/{project}/notes/drafts/"

# Archive handoffs (move to .archive/)
Move-Item "projects/{project}/notes/handoffs/" "projects/{project}/.archive/handoffs/"
```
```

### Step 9: Execute Cleanup (With Confirmation)

```
IF user approves cleanup:
  EXECUTE: Approved removal commands
  LOG: What was removed
  VERIFY: No unintended deletions

IF user requests dry-run only:
  OUTPUT: Report only, no modifications
```

---

## Conventions

### What Gets Removed (Project Completion)

| Location | Removed? | Notes |
|----------|----------|-------|
| `notes/debug/` | ✅ Yes | Debugging artifacts |
| `notes/spikes/` | ✅ Yes | Exploratory code |
| `notes/drafts/` | ✅ Yes | WIP content |
| `notes/scratch.md` | ✅ Yes | Brainstorming |
| `notes/handoffs/` | 📦 Archive | Move to `.archive/` |
| `notes/lessons-learned.md` | ❌ Keep | Valuable retrospective |
| `notes/meetings/` | ❌ Keep | Historical record |

### What Gets Preserved

| Location | Reason |
|----------|--------|
| `spec.md` | Original design intent |
| `README.md` | Project documentation |
| `plan.md` | Implementation record |
| `CLAUDE.md` | AI context |
| `tasks/*.poml` | Task history |
| `notes/lessons-learned.md` | Knowledge capture |

### Directory Archival Policy

When a project is complete:
1. `notes/handoffs/` → Move to `projects/{project}/.archive/handoffs/`
2. Keep main project files for reference
3. Consider moving entire project to `projects/.archive/` after 6 months

---

## Resources

| Resource | Purpose |
|----------|---------|
| `docs/reference/architecture/SPAARKE-REPOSITORY-ARCHITECTURE.md` | Structure standards |
| `docs/reference/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` | Naming standards |
| `.gitignore` | Files that should be ignored |

---

## Integration Points

### In Project Lifecycle

This skill runs as part of **Task 090 (Project Wrap-up)**:

```xml
<!-- Add to 090-project-wrap-up.poml -->
<step order="12">Run /repo-cleanup projects/{project-name} to audit and clean</step>
<step order="13">Review cleanup report and approve removals</step>
```

### In CI/CD

Can be invoked as a PR check:
```yaml
# .github/workflows/repo-audit.yml
- name: Repository Structure Audit
  run: |
    # Check for structure violations
    # Fail PR if critical issues found
```

### Periodic Maintenance

Recommend running full audit:
- Monthly
- Before major releases
- After large feature merges

---

## Examples

### Example 1: Project Completion Cleanup

**Trigger**: `/repo-cleanup projects/sdap-fileviewer-enhancements-1`

**Output**:
```markdown
## Repository Cleanup Report

**Scope**: projects/sdap-fileviewer-enhancements-1
**Mode**: Project Completion

### Summary
| Category | Issues Found | Auto-Fixable |
|----------|--------------|--------------|
| Ephemeral Files | 12 | 12 |
| Structure | 0 | 0 |

### Ephemeral Files (Safe to Remove)
| File/Directory | Reason | Size |
|----------------|--------|------|
| notes/debug/032-browser-compatibility.md | Debug session | 4KB |
| notes/spikes/preview-url-spike.ts | Exploratory code | 2KB |
| notes/drafts/api-design-v1.md | Superseded draft | 8KB |

### Recommended Actions
1. Remove 12 ephemeral files (45KB total)
2. Archive notes/handoffs/ to .archive/

Proceed with cleanup? (y/n)
```

### Example 2: Full Repository Audit

**Trigger**: `/repo-cleanup`

**Output**:
```markdown
## Repository Cleanup Report

**Scope**: Full Repository
**Mode**: Full Audit

### Summary
| Category | Issues Found | Auto-Fixable |
|----------|--------------|--------------|
| Structure | 2 | 0 |
| Naming | 5 | 0 |
| Missing CLAUDE.md | 3 | 3 |
| Git Hygiene | 1 | 1 |

### Issues Found

#### Structure Issues
| Issue | Location | Severity |
|-------|----------|----------|
| Orphaned directory | src/server/api/Sprk.Bff.Api/ | Medium |
| Empty directory | src/client/office-addins/ | Low |

#### Missing CLAUDE.md
| Directory | Priority |
|-----------|----------|
| infrastructure/bicep/ | Medium |
| tests/integration/ | Low |
```

### Example 3: Pre-Merge Check

**Trigger**: `/repo-cleanup --mode=pre-merge`

**Output**:
```markdown
## Pre-Merge Repository Check

**Branch**: feature/ai-document-summary → main

### Merge Readiness
✅ No structure violations
✅ No ephemeral files in committed changes
⚠️ 1 naming inconsistency (non-blocking)
✅ All CLAUDE.md files present

**Recommendation**: Safe to merge
```

---

## Error Handling

| Situation | Response |
|-----------|----------|
| Project not found | "Project path not found. Available projects: {list}" |
| Project still active | "Project has incomplete tasks. Run cleanup after wrap-up." |
| Removal permission denied | "Cannot remove {path}. Check file permissions." |
| Protected file flagged | "File {path} is in protected list. Skipping." |

---

## Related Skills

- `project-init` - Creates initial project structure (this skill validates it)
- `design-to-project` - Full project lifecycle (includes cleanup at end)
- `code-review` - Code quality (this skill focuses on structure/hygiene)
- `adr-check` - Architecture compliance (complementary)

---

## Tips for AI

- Always run in dry-run mode first and show report before making changes
- Never delete files in `src/`, `docs/ai-knowledge/`, or `docs/reference/` without explicit approval
- When in doubt about whether to delete, flag for manual review
- After cleanup, verify build still passes: `dotnet build`
- Update TASK-INDEX.md if cleaning up a completed project
