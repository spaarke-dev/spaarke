# sdap-ci.yml Repair Evidence — 2026-05-31

> **Task**: 025 (P1.D5 — fix broken sdap-ci.yml workflow)
> **Project**: `sdap-bff.api-test-suite-repair`
> **Repair branch**: `work/sdap-bff.api-test-suite-repair`
> **Verify branch (throwaway)**: `test/sdap-ci-repair-verify-2026-05-31`
> **Date**: 2026-05-31
> **Verdict**: ✅ **WORKFLOW PARSER ERROR FIXED** — root cause was a duplicate YAML mapping key (`if-no-files-found`) introduced by commit `d9018dea`; minimum-viable fix is a one-line deletion.

---

## 1. Root cause (with evidence)

The `Upload ADR test results` step in `code-quality` job had **two `if-no-files-found: warn` lines** inside its `with:` mapping (lines 184 and 186 of pre-fix file). GitHub Actions uses **strict YAML parsing** which rejects mapping documents containing duplicate keys — this is why every run completed in 0 seconds with `conclusion: failure`, no jobs created (`jobs.total_count: 0`), no annotations, and `gh run view` reported "**This run likely failed because of a workflow file issue.**"

### Pre-fix YAML (lines 180–187 of the broken file)

```yaml
- name: Upload ADR test results
  if: always()
  uses: actions/upload-artifact@v6
  with:
    name: adr-test-results
    if-no-files-found: warn          # ← line 184 (first)
    path: ./TestResults/adr-results.trx
    if-no-files-found: warn          # ← line 186 (DUPLICATE — same scope)
```

### Local strict-YAML validation (pre-fix)

```
$ python -c "<strict-duplicate-key loader>" .github/workflows/sdap-ci.yml
yaml.constructor.ConstructorError: duplicate key found: if-no-files-found at line 186
```

Note: `yaml.safe_load()` succeeded silently (Python's default loader is lenient and silently picks "last value wins"). GitHub Actions' parser is strict, matching the strict-loader behavior. This is the reason a casual `python -c "import yaml; yaml.safe_load(...)"` check did not surface the issue until the strict-mode loader was used.

### Git blame for the introduction

```
git log --all --oneline -p -S "if-no-files-found: warn" -- .github/workflows/sdap-ci.yml
d9018dea chore(ci): clean up CI workflows, fix ADR tests, update notification emails
# diff shows the duplicate `if-no-files-found: warn` line ADDED above the existing one
```

The "cleanup" commit accidentally introduced the second copy of the key above the original copy without removing the original.

---

## 2. Runs inspected (Step 3 of the POML procedure)

```
gh run list --workflow=sdap-ci.yml --limit 10 --json status,conclusion,startedAt,headBranch,event,databaseId
```

| Run ID | Branch | Status | Conclusion | Duration |
|---|---|---|---|---|
| 26723216636 | work/sdap-bff.api-test-suite-repair | completed | failure | 0s |
| 26723095636 | work/matter-ui-r1-v1.1.72-vh-polish | completed | failure | 0s |
| 26722852442 | work/sdap-bff.api-test-suite-repair | completed | failure | 0s |
| 26722820392 | work/sdap-bff.api-test-suite-repair | completed | failure | 0s |
| 26722646509 | test/ci-gate-negative-path-verification | completed | failure | 0s |

For each run inspected: `gh run view {id}` reported "This run likely failed because of a workflow file issue." and `gh run view {id} --log` returned "failed to get run log: log not found" (confirming **zero steps ran**). API queries confirmed `total_count: 0` jobs and `billable: {}` — the workflow was rejected during parsing before any compute was provisioned.

---

## 3. Minimum-viable fix applied (NFR-09 compliant)

Single deletion of the duplicate line at the original line 184. Net diff: **-1 line**.

### Post-fix YAML

```yaml
- name: Upload ADR test results
  if: always()
  uses: actions/upload-artifact@v6
  with:
    name: adr-test-results
    path: ./TestResults/adr-results.trx
    if-no-files-found: warn
```

### Size estimate

- Lines changed: **1 deletion**
- % of file rewritten: **0.26%** (1 of 387 lines)
- §4.8 escalation required: **NO** (rewrite threshold = 50%)
- NFR-09 `repair-not-rewrite: true`: **PRESERVED**

---

## 4. Local validation results (acceptance criterion 1)

| Check | Result |
|---|---|
| `python -c "import yaml; yaml.safe_load(...)"` | ✅ PASS |
| Strict-loader duplicate-key detection | ✅ PASS — no duplicates |

---

## 5. D-02 binding preserved (acceptance criterion 7)

- `enforce_admins: true` on master branch protection: **UNCHANGED** (not touched)
- 3 required status checks (`Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`): **UNCHANGED** (not touched)
- The fix repairs the workflow that POSTS these checks — branch protection itself is untouched.

---

## 6. NFR-01 file scope compliance

```
git status   # → only `.github/workflows/sdap-ci.yml` modified
```

Modified paths: `.github/workflows/sdap-ci.yml` only (plus the persistent artifact this document, in `projects/sdap-bff.api-test-suite-repair/baseline/`).

NO modifications to `src/`, `power-platform/`, `infra/`, `scripts/` — compliant with NFR-01.

---

## 7. Verify-PR outcome

| Field | Value |
|---|---|
| **PR URL** | https://github.com/spaarke-dev/spaarke/pull/313 |
| **PR number** | 313 |
| **Title** | `[CI repair verify] do not merge — T-025 sdap-ci.yml fix` |
| **Head branch** | `test/sdap-ci-repair-verify-2026-05-31` |
| **Base branch** | `master` |
| **Verify run ID** | 26723333123 |
| **Run start** | 2026-05-31 20:16:14 UTC |
| **Run state at signal-verification time** | `in_progress` (workflow LOADED + DISPATCHED jobs — opposite of the pre-fix 0s-failure pattern) |
| **Final state** | Cancelled (`gh run cancel 26723333123`) per orchestrator brief: "Cancel after confirming it's actually running >0s and posting a signal — don't wait for full test completion." |
| **PR state at cleanup** | CLOSED 2026-05-31 20:17:02 UTC (no merge); remote branch deleted; local branch deleted |

### Critical evidence: Status checks ARE NOW POSTED

```
$ gh pr checks 313
Build & Test (Debug)                 pending  0  https://github.com/spaarke-dev/spaarke/actions/runs/26723333123/job/78754226609
Build & Test (Release)               pending  0  https://github.com/spaarke-dev/spaarke/actions/runs/26723333123/job/78754226615
Client Quality (Prettier + ESLint)   pending  0  https://github.com/spaarke-dev/spaarke/actions/runs/26723333123/job/78754226604
Security Scan                        pending  0  https://github.com/spaarke-dev/spaarke/actions/runs/26723333123/job/78754226601
```

**All 4 named jobs were posted to the PR** — including `Build & Test (Debug)`, `Build & Test (Release)`, and `Client Quality (Prettier + ESLint)`. `Code Quality` (which `needs: build-test`) would post after `build-test` completed. The fix is verified: the workflow LOADS, the jobs RUN, and the named status checks REACH branch-protection's required-check list.

Before the fix (Run ID 26723216636, same branch class): `jobs.total_count: 0`, `billable: {}`, run finished in 0s. After the fix (Run ID 26723333123): jobs created, all 4 enter `pending`, workflow actively executing — this is conclusive verification.

### Cleanup actions performed

```bash
gh run cancel 26723333123      # cancel verify run after signal confirmed
gh pr close 313                 # PR closed (branch deletion via PR command blocked by worktree state)
git push origin --delete test/sdap-ci-repair-verify-2026-05-31  # remote branch deleted manually
git branch -D test/sdap-ci-repair-verify-2026-05-31              # local branch deleted manually
```

Verify branch is GONE from both local and remote.

---

## 8. Why this caused the exact symptom observed

GitHub Actions workflow loading happens in two phases:

1. **Parse** — strict YAML 1.2; rejects duplicate mapping keys with no error annotation visible to users
2. **Validate + dispatch** — only runs after parse succeeds

When parse fails:
- The run record is created (so it appears in `gh run list`)
- It transitions immediately to `conclusion: failure` with `run_started_at == updated_at` (0s duration)
- `jobs/` collection is empty (`total_count: 0`)
- No annotations or logs are produced
- The `gh run view` UI hint reads "This run likely failed because of a workflow file issue"

This matches the observed symptom 1:1 across every run on every branch since commit `d9018dea`.

---

## 9. Phase 1 P1.D Track FINAL exit

✅ The full P1.D Track is now operationally complete:
- Task 020 (FR-09 enforce_admins flip): done
- Task 021 (FR-10 skip-tests removed from deploy-bff-api.yml): done
- Task 022 (FR-11 emergency procedure documented): done
- Task 023 (FR-12 CI gate negative-path verified): done — gate operational
- **Task 025 (sdap-ci.yml workflow repair): done — gate's underlying signal restored**

The CI gate now both BLOCKS unauthorized merges (task 023 verification) AND will receive the real pass/fail signal it requires from `sdap-ci.yml` (this task).
