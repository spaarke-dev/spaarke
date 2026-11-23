# project-pipeline — sample step reports

Sample milestone reports. Each `SKILL.md` step lists what its report must contain; these show one way to format it. In autonomous mode, drop the bracketed prompt lines (interactive gates: `references/interactive-mode.md`).

## Contents

- Step 0.3: Pre-flight
- Step 1: SPEC.md validated
- Step 1.5: Overlap detected
- Step 2: Resources and artifacts
- Step 3: Task files
- Step 4: Branch
- Scale reference

## Step 0.3: Pre-flight

```
✅ Pre-flight checks passed:
   - On branch: {branch}
   - Working tree: clean
   - Master: current (0 commits behind)
   - Build: passing (0 errors, {N} warnings)
   - Main repo sync: {status}

📋 Recent lessons-learned: {path} (please review if not already)
```

## Step 1: SPEC.md validated

```
✅ SPEC.md validated:
   - 2,306 words
   - All required sections present (incl. ADR Tensions)
   - Ready for planning

📋 Next step: resource discovery and artifact generation
[Y to proceed / refine to make changes / stop to exit]
```

## Step 1.5: Overlap detected

```
⚠️ Potential Overlap Detected

Your project (from spec.md) appears to touch:
  - src/client/pcf/ (new PCF control)
  - .claude/skills/ (skill updates)

Active PRs with overlapping files:
──────────────────────────────────
PR #98: chore: project planning updates
  Branch: work/project-planning-and-documentation
  Overlapping: .claude/skills/

Recommendations:
1. If PR #98 is close to merge → wait for it, then start
2. If both sessions are yours → coordinate file ownership (Session A owns file X, Session B owns file Y)
3. If proceeding → plan to rebase after PR #98 merges

[Y to proceed with awareness / stop to wait]
```

## Step 2: Resources and artifacts

```
✅ Resources discovered:
   - 4 ADRs identified (ADR-001, ADR-007, ADR-008, ADR-010)
   - 2 skills applicable (dataverse-deploy, adr-aware)
   - 3 knowledge docs found (SPAARKE-ARCHITECTURE.md, ...)
   - 2 scripts available (Invoke-PcfBuildProd.ps1, Test-SdapBffApi.ps1)
   - ✅ Hot-path surfaces unique among active worktrees

✅ Artifacts generated:
   - README.md (project overview, graduation criteria)
   - plan.md (5 phases, WBS, Discovered Resources)
   - CLAUDE.md (project operating manual)
   - current-task.md (context recovery)
   - tasks/ (empty, ready for task files), notes/ (with subdirectories)

📋 Next step: decompose plan.md into task files
[Y to proceed / review to view artifacts / refine {file} to edit / stop to exit]
```

## Step 3: Task files

```
✅ Task files generated:
   - 178 tasks created in tasks/ (POML/XML)
   - TASK-INDEX.md created (6 parallel groups)
   - Tag-to-knowledge mapping applied
   - 4 deployment tasks added
   - Wrap-up task added
   - Completeness Lint: passed

📁 Files created:
   - projects/ai-document-intelligence-r1/tasks/TASK-INDEX.md
   - projects/ai-document-intelligence-r1/tasks/001-create-environment-variables.poml
   - ... (176 more tasks)
   - projects/ai-document-intelligence-r1/tasks/090-project-wrap-up.poml
```

## Step 4: Branch

```
✅ Project branch ready:
   - Branch: work/{project-name}
   - Initial commit: "feat({scope}): initialize {project-name} project"
   - Pushed: origin/work/{project-name}
   - Draft PR: #{PR-number} (optional)

🔗 https://github.com/{org}/{repo}/pull/{PR-number}
```

## Scale reference

AI Document Intelligence R1, for a sense of scale: spec.md of 2,306 words; 4 ADRs loaded (ADR-013 to ADR-016); 8 knowledge docs discovered; 178 tasks generated with full context. Runs this large are why the output-length setting in `SKILL.md` exists.
