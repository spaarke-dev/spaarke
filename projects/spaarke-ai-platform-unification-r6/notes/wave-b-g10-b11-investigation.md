# Wave B-G10 — B11 Production Bug Investigation

> **Date**: 2026-06-10
> **Symptom**: Production walkthrough on Spaarke Dev (post-deploy of Wave B-G9c2): still only ONE Summary tab; content of file B replaces file A. Reproduces in both regular Chrome (after Clear site data) and InPrivate — NOT browser cache.
> **Investigation budget**: 90 minutes
> **Status**: **ROOT-CAUSE-IDENTIFIED — Diagnosis (A)**
> **Stop-and-surface triggered**: YES at Step 1.

---

## Conclusion (TL;DR)

**Diagnosis (A): The Wave B-G9c2 source code was committed but NEVER bundled or redeployed.** The artifact at `src/solutions/SpaarkeAi/dist/spaarkeai.html` predates the B-G9c2 commit by ~25 hours, and `Deploy-SpaarkeAi.ps1` only uploads the existing artifact — it does not invoke `npm run build`. The walkthrough is exercising the pre-B-G9c2 code.

**Fix path**: Run `npm run build` in `src/solutions/SpaarkeAi`, then re-run `Deploy-SpaarkeAi.ps1`. No code changes needed.

---

## Evidence

### 1. Bundle timestamp predates the commit

```
src/solutions/SpaarkeAi/dist/spaarkeai.html   Jun 9 11:47    (3,582,855 bytes)
commit dd8466734 (B-G9c2)                     Jun 10 12:43:54 -0400
```

The deployed bundle is ~25 hours OLDER than the B-G9c2 commit. The build was run on Jun 9 (likely for the prior wave, B-G9 / B-G9b) and never re-run after B-G9c2 source changes.

### 2. Source file modification times confirm post-commit edits

```
ConversationPane.tsx          Jun 10 11:08
executeSummarizeIntent.ts     Jun 10 11:24
WorkspacePane.tsx             Jun 10 11:09
```

All three B-G9c2-modified files were last touched Jun 10, AFTER the Jun 9 build.

### 3. Bundle marker scan — B-G9c2 markers ABSENT

Searched `dist/spaarkeai.html` for distinctive B-G9c2 strings:

| Marker | In source? | In bundle? |
|---|---|---|
| `B-G9c2` | YES (5 occurrences in `executeSummarizeIntent.ts`) | **NO (0)** |
| `B7 (defer install)` | YES (in source comments) | **NO (0)** |
| `formatTitleSuffix` (new helper, NOT a comment — minifier-resistant function name) | YES (line 192 of `executeSummarizeIntent.ts`) | **NO (0)** |
| `streamId: undefined` (load-bearing literal in `ConversationPane.tsx:1169`) | YES | **NO (0)** |
| `widget_load` | YES | YES (2 occurrences — pre-existing) |

The `formatTitleSuffix` function name should survive Vite production minification (it's a top-level named function, not a comment). Its absence is conclusive — the source was never bundled.

### 4. Deploy script does NOT build

`scripts/Deploy-SpaarkeAi.ps1` line 44–48:

```powershell
$filePath = Join-Path $repoRoot 'src\solutions\SpaarkeAi\dist\spaarkeai.html'
Write-Host '[1/4] Verifying build artifact...'
if (-not (Test-Path $filePath)) {
    Write-Error "Build artifact not found: $filePath -- run 'npm run build' in src/solutions/SpaarkeAi first"
}
```

The script verifies the artifact exists, then uploads it. It does NOT run `npm run build`. The build is a manual prerequisite that was missed for the B-G9c2 deploy.

### 5. Git state confirms no uncommitted SpaarkeAi changes

`git status --short src/solutions/SpaarkeAi/` → empty output. HEAD is at dd8466734 (B-G9c2). Working tree matches the commit — so the source-vs-bundle mismatch is purely a stale-build issue, not local uncommitted edits.

---

## Why this happened

The repo has **no post-commit/post-merge hook** that rebuilds `dist/` for code-page solutions. Each deploy requires an explicit `npm run build` step. The Jun 9 build was likely run for an earlier wave (B-G9 / B-G9b), and when B-G9c2 was committed Jun 10, the deploy script was re-run without first rebuilding. The script silently uploaded the stale Jun 9 artifact.

This is a recurring failure mode for Code Page deployments — listed in `.claude/FAILURE-MODES.md` patterns about deploy scripts that don't invoke build steps.

---

## Fix proposal

### Immediate fix (single user action)

From `src/solutions/SpaarkeAi/`:
```
npm run build
```
Then from repo root:
```
.\scripts\Deploy-SpaarkeAi.ps1
```

The user should then re-test the walkthrough. The expected behavior post-deploy: summarizing File A → new "Summary: A.pdf" tab; summarizing File B → SECOND tab "Summary: B.pdf" (both retained, neither replaced).

### Verification of fix (before re-walkthrough)

Quick post-build sanity scan on the new bundle:

```
grep -c "formatTitleSuffix" src/solutions/SpaarkeAi/dist/spaarkeai.html
```

Must return ≥1. If 0, the build silently failed (check vite output).

### Long-term fix (deferred to a separate task — not required for B11)

Modify `Deploy-SpaarkeAi.ps1` to either (a) invoke `npm run build` automatically, or (b) compare bundle mtime against most-recent source mtime under `src/` and refuse to deploy a stale bundle. This is a repeating failure mode — same pattern likely affects other Code Page deploys (`Deploy-CorporateWorkspace.ps1`, `Deploy-SystemWorkspaceLayouts.ps1`).

---

## Investigation steps NOT executed (and why)

Per task instructions: "If at any point you find the deployed bundle doesn't contain B-G9c2 source markers, STOP IMMEDIATELY and report — the rest of the investigation is moot."

Steps 2–5 (execution-path tracing, per-stream behavior analysis, tab-dedup investigation) were SKIPPED because the source code IS the B-G9c2 fix as designed, but production is not running it. Any code-level debugging on the assumption that B-G9c2 is live would be misdirected effort.

If, after rebuild+redeploy+re-walkthrough, the bug PERSISTS — then re-open this investigation at Step 2.

---

## Files referenced

- `C:\code_files\spaarke-wt-spaarke-ai-platform-unification-r6\src\solutions\SpaarkeAi\dist\spaarkeai.html` — stale bundle (Jun 9 11:47)
- `C:\code_files\spaarke-wt-spaarke-ai-platform-unification-r6\src\solutions\SpaarkeAi\src\components\conversation\executeSummarizeIntent.ts` — B-G9c2 source (Jun 10 11:24)
- `C:\code_files\spaarke-wt-spaarke-ai-platform-unification-r6\src\solutions\SpaarkeAi\src\components\conversation\ConversationPane.tsx` — `streamId: undefined` change (line 1169)
- `C:\code_files\spaarke-wt-spaarke-ai-platform-unification-r6\src\solutions\SpaarkeAi\src\components\workspace\WorkspacePane.tsx` — auto-install removal
- `C:\code_files\spaarke-wt-spaarke-ai-platform-unification-r6\scripts\Deploy-SpaarkeAi.ps1` — verify-only deploy script (does not build)
- `C:\code_files\spaarke-wt-spaarke-ai-platform-unification-r6\src\solutions\SpaarkeAi\package.json` — single `build` script (no `build:prod` variant)
- Commit `dd8466734` — B-G9c2 source changes (Jun 10 12:43)
