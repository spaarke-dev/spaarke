# Repo-Level Failure Modes Catalog

> **Purpose**: Cross-cutting failure patterns that don't belong inside any single skill's Gotchas section. The agent should mentally cross-reference this catalog before executing a skill; sessions that hit a NEW failure type should append an entry here.

> **Last Updated**: 2026-05-14 (inaugural entries)

---

## Classification

| Class | Meaning |
|---|---|
| **Anti-pattern** | Something that LOOKS RIGHT but isn't. A skill or doc may even prescribe it — but it's wrong. Discovery requires empirical pushback. |
| **Gotcha** | Something that HAPPENS UNEXPECTEDLY. The doc/skill is fine; the runtime/platform/environment has surprising behavior. |

The distinction matters because the fix is different. Anti-patterns require *unlearning* (update the offending skill or doc, and capture the wrong-belief here so it doesn't return). Gotchas require *defensive code* and clearer warnings.

---

## Table of Contents

### Anti-patterns
- [AP-1: Skill prescribes X but X is wrong (`/pcf-deploy` "NEVER use `build:prod`")](#ap-1-skill-prescribes-x-but-x-is-wrong)

### Gotchas
- [G-1: Settings-file schema malformation silently disables permission rules + hooks](#g-1-settings-file-schema-malformation-silently-disables-permission-rules--hooks)
- [G-2: Default health-check window sized for old behavior (Linux cold start)](#g-2-default-health-check-window-sized-for-old-behavior)
- [G-3: Zero-second GitHub Actions workflow failures are startup failures, not test failures](#g-3-zero-second-github-actions-workflow-failures-are-startup-failures-not-test-failures)

---

## Anti-patterns

### AP-1: Skill prescribes X but X is wrong

**Title**: `/pcf-deploy` skill said "NEVER use `npm run build:prod`" — actually `build:prod` IS the correct invocation.

**Date**: 2026-05-14 (caught after user pushback)

**Classification**: Anti-pattern (skill prescribed wrong behavior with confident "NEVER" framing)

**What happened**: While deploying SpeDocumentViewer PCF, the bundle size jumped from 440 KB to 6.7 MB. Initially deferred as "needs investigation." User pushed back: "did you use the skill `/pcf-deploy` to check the build process?" — investigation revealed the skill explicitly said "NEVER use `npm run build:prod` — pcf-scripts does not have a separate production build script; use `npm run build`." This was wrong on both counts: (1) `pcf-scripts build --buildMode production` IS a separate production mode, and (2) `npm run build` defaults to dev mode (no tree-shaking) producing 5-10× larger bundles.

**Root cause**: A doc/skill confidently asserted a "NEVER" rule. Wrong-belief was reinforced because the rule was framed as authoritative. The check that would have caught it (an empirical build-mode comparison) never ran because the skill already "had the answer."

**Fix**:
- Removed wrong "NEVER" instruction from `.claude/skills/pcf-deploy/SKILL.md`
- Added "Bundle Size & Production Mode" section mandating `build:prod`
- Fixed 3 PCFs whose `package.json` `build:prod` scripts had wrong flags (`-- --mode production` and `--production` are silently ignored by `pcf-scripts`; correct form is `pcf-scripts build --buildMode production`)
- Commit: `c132773c`

**Prevention**: When a skill says "NEVER" or "ALWAYS," that's the cue to verify empirically before trusting. Stronger claims in docs warrant stronger evidence — and visible evidence (e.g., a comparison run, a link to the upstream CLI docs) should accompany absolute rules. Phase 2a skill audit `needs-substantive-rewrite` recommendation exists specifically for this class of issue.

**Evidence**: commit `c132773c` (skill fix + 3 package.json fixes)

---

## Gotchas

### G-1: Settings-file schema malformation silently disables permission rules + hooks

**Title**: `.claude/settings.json` had a flat-format `hooks` block (using `{matcher, command}`) for ~2 months — it silently failed to register, so the format-on-edit and quality-gate hooks never ran.

**Date**: 2026-03-14 introduced. 2026-05-14 caught (when a user screenshot showed "Settings file failed to parse: Expected array, but received undefined").

**Classification**: Gotcha (the runtime tolerated invalid shape silently)

**What happened**: The settings.json `hooks` block was written in a flat shape — `{matcher: "Edit", command: "..."}` — at the same time `TaskCompleted` was added as a hook event. Claude Code's runtime parser silently rejected the malformed shape AND `TaskCompleted` (which is not a real event). The settings parsed as JSON (no syntax errors) but the hooks never fired. We went 2 months thinking format-on-edit was running when it wasn't.

**Root cause**: (1) Settings schema does not have a hard reject on shape mismatch — invalid sub-blocks just silently no-op. (2) The agent had no validation step against the published schema during edits. (3) The "tested by use" feedback loop (hooks visibly firing) is too quiet — if the hook does nothing or does only background work, you don't notice it's not running.

**Fix**: Reshaped to the correct nested form:
```json
"hooks": {
  "PostToolUse": [
    {
      "matcher": "Edit",
      "hooks": [{ "type": "command", "command": "bash scripts/quality/post-edit-lint.sh" }]
    }
  ],
  "Stop": [
    { "hooks": [{ "type": "command", "command": "bash scripts/quality/task-quality-gate.sh" }] }
  ]
}
```
Changed `TaskCompleted` (not a real event) to `Stop`. Commit: `8ca796ab`.

**Prevention**: Phase 4a task 060 introduces a JSON-schema validator for `.claude/settings.json` that runs in pre-commit. Note from Phase 0 inventory: the published schema's `permissionRule` regex is stricter than Claude Code's runtime parser, so the validator must focus structural validation on the `hooks` block (where the actual bug lived) and not enforce the strict regex on `permissions.allow`.

**Evidence**: commit `8ca796ab` (settings.json fix); Phase 0 task 004 inventory at `projects/ai-procedure-quality-r1/notes/inventory/settings.md` confirms current state is nested-correct.

---

### G-2: Default health-check window sized for old behavior

**Title**: `Deploy-BffApi.ps1` 60-second health-check window false-failed Linux App Service deploys to the demo environment.

**Date**: 2026-05-14

**Classification**: Gotcha (default tuned for Windows historical behavior; Linux platform has different cold-start)

**What happened**: Demo BFF deploy reported failure at the health-check step. The actual deployment had succeeded (SHA-256 hash-verify of 6 critical files all matched) but the `/healthz` endpoint hadn't responded within 60 seconds. Linux App Service cold start is 90-120 seconds.

**Root cause**: The deploy script's `$MaxHealthCheckRetries = 12` (with 5-second waits = 60s window) was tuned to Windows App Service warm-restart behavior. When demo was created on Linux, nobody re-tuned the window.

**Fix**: Bumped `$MaxHealthCheckRetries = 24` (= 120s window). Also clarified in `bff-deploy` skill that hash-verify success + healthz timeout means the deploy IS correct, just still booting (two-layer safety net). Commit: `6d7bcf45`.

**Prevention**: When tuning defaults (timeouts, retry counts, batch sizes), verify against CURRENT behavior, not historical assumptions. If a default is environment-dependent (Linux vs Windows, dev vs prod), make it explicit in the script comments. Phase 4a task 067 will add `Check-DeployScriptDrift.ps1` that compares deploy-script defaults against observed runtimes.

**Evidence**: commit `6d7bcf45` (script tuning + skill update)

---

### G-3: Zero-second GitHub Actions workflow failures are startup failures, not test failures

**Title**: 5 of 13 workflows fail 100% of recent runs in 0 seconds. The failures look like "tests failing" but they're actually action-resolution failures at workflow startup.

**Date**: First observed 2026-05-14 during Phase 0 inventory (task 003).

**Classification**: Gotcha (failure presentation is misleading — `gh run view` shows "failed" without distinguishing startup-failure vs test-failure)

**What happened**: Phase 0 workflow inventory found 5 workflows failing 100% of recent runs (sdap-ci, deploy-infrastructure, deploy-promote, Deploy BFF API, Nightly Quality) — every run terminates in 0-2 seconds. Hypothesis: action references like `actions/checkout@v6`, `actions/upload-artifact@v6`, `actions/download-artifact@v7`, `actions/cache@v5` reference major versions that do not exist in the GitHub Actions registry (current published majors are v4/v5/v3). GitHub fails the run instantly without proceeding to any job step.

**Root cause**: Action references can be wrong without any local validation. The wrong version gets through PR review because `gh run view` shows "failed" — a reviewer assumes "the tests broke," not "the workflow couldn't even start." Diagnosis requires drilling into the run logs or reading the workflow file carefully.

**Fix**: Phase 4b task 070 will diagnose and fix these specific workflows. Phase 4b task 071 adds `actionlint` to a `procedure-quality` workflow that runs on every PR touching `.github/workflows/*.yml` — `actionlint` catches non-existent action versions BEFORE merge. Phase 4b task 074 introduces `dependabot.yml` to keep action versions in sync going forward.

**Prevention**:
- Use exact SHA pins or trusted-action tags only (F-20 target — currently 0 of 115 actions are SHA-pinned per Phase 0 inventory)
- Lint workflow YAML with `actionlint` in CI
- When a workflow shows 0-second failure, look at action version mismatches FIRST, test logic last

**Evidence**: Phase 0 task 003 inventory at `projects/ai-procedure-quality-r1/notes/inventory/workflows.md` enumerates the 5 affected workflows and the suspect actions.

---

## How to use this catalog

1. **Before executing a skill**, the agent should mentally cross-reference: does this skill touch anything in the catalog?
2. **When a skill says "NEVER" or "ALWAYS"** with confidence, but the agent has no recent empirical verification, the agent should add a brief "verify" step (per AP-1's prevention).
3. **When a session surfaces a NEW cross-cutting failure pattern** — something that affects more than one skill, or recurs across different sessions — append an entry here. Use the same shape: title, date, class, what-happened, root-cause, fix, prevention, evidence.
4. **Bidirectional links**: each affected skill should have a `See FAILURE-MODES.md#<anchor>` pointer in its Gotchas section. (Phase 2b refinements will add these.)

---

*Established 2026-05-14 by project `ai-procedure-quality-r1` (task 013). Cross-reference: [.claude/CHANGELOG.md](CHANGELOG.md) for the entry stream.*
