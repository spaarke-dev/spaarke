# PR #331 Merge Follow-up — Checkpoint for Post-Compact Resume

> **Date**: 2026-06-02
> **Purpose**: Hand-off after context compaction so post-R4 merge work can resume cleanly.
> **PR**: https://github.com/spaarke-dev/spaarke/pull/331 (R4 → master)

---

## ⚠️ CRITICAL — READ FIRST

R4 itself is **CLOSED + WRAPPED** (task 090 completed; see `lessons-learned.md`). This file tracks **post-close merge-PR work** — fixing CI gates blocking the R4→master merge.

The operator's standing principle (operator stated 2026-06-02):
> "CI gates are either load-bearing or they're noise. Either fix or remove. Don't bypass."

This is binding for all PR #331 work.

---

## Quick Recovery

| Field | Value |
|---|---|
| **PR** | #331 (R4 → master) — open, awaiting CI green |
| **Branch** | `work/spaarke-ai-platform-unification-r4` (worktree at `c:\code_files\spaarke-wt-spaarke-ai-platform-unification-r4`) |
| **Last commit** | `a78145cc` — Prettier fix (`fix(format): prettier --write across client code`) |
| **Latest CI checks** | See §3 below |
| **Next actions** | Both (a) Client Quality post-ESLint investigation + (b) Trivy `azure-storage-account-key` secret alert — see §4 |

---

## 1. What's done so far

### 1.1 Local merge in main repo (succeeded)
```
cd C:/code_files/spaarke (operator's main repo)
1. git checkout -- src/server/api/Sprk.Bff.Api/Infrastructure/DI/CorsModule.cs   # discarded line-ending swap (per operator)
2. git pull origin master                                                          # absorbed any new master commits
3. git merge --ff-only origin/work/spaarke-ai-platform-unification-r4              # fast-forward succeeded
4. dotnet build src/server/api/Sprk.Bff.Api/                                       # 0 errors
5. git push origin master                                                          # REJECTED — protected branch
```

### 1.2 PR created (PR #331)
- Via `gh pr create` from `C:/code_files/spaarke`
- Comprehensive body with R4 summary + conflict resolution + build verification
- URL: https://github.com/spaarke-dev/spaarke/pull/331

### 1.3 Prettier fix pushed (commit `a78145cc`)
- R4 task 078 ESLint sweep formatted code "to look clean" but never ran the project's actual Prettier.
- CI's "Client Quality" gate caught it: 82 files failed Prettier format check.
- Action: `npx prettier --write "src/client/**/*.{ts,tsx}"` — reformatted 58 files.
- Verified: local prettier --check → "All matched files use Prettier code style!"
- Verified: tsc clean on `@spaarke/ui-components`, ESLint 0 errors / 186 warnings (within CI tolerance)
- Committed + pushed to PR branch.

### 1.4 Operator's main repo `C:/code_files/spaarke` — state preserved
- 26 real new uncommitted doc files (RAG config, JPS conversions, release procedure) — UNTOUCHED per operator decision "we'll handle separately"
- ~4,200 untracked `exports/` files (solution unpacks) — UNTOUCHED
- CorsModule.cs line-ending swap — DISCARDED per operator decision

---

## 2. Current PR #331 check state (as of 2026-06-02 ~19:00 UTC)

| Check | Status | Notes |
|---|---|---|
| ✅ Build & Test (Debug) | **pass** (8m5s) | Builds + tests clean |
| ✅ Build & Test (Release) | **pass** (7m32s) | Builds + tests clean |
| ✅ Security Scan (workflow) | **pass** (50s) | Trivy workflow JOB ran successfully (uploads SARIF) |
| ✅ actionlint | **pass** (17s) | Workflow YAML lint clean |
| ❌ **Client Quality (Prettier + ESLint)** | **fail** (1m36s) | **Prettier ✅ now**, ESLint ✅ (0 errors, 186 warnings within tolerance). **Likely failing on `@spaarke/ai-widgets tsc --noEmit` step (R4 task 061 NFR-05 gate)** — see §4.1 |
| ❌ **Trivy** (GitHub Code Scanning check) | **fail** (4s) | NOT the workflow — the GitHub Code Scanning result check. 20 pre-existing master-state CVEs. **Includes 1 azure-storage-account-key secret alert** — see §4.2 |

The 4-second Trivy "fail" is NOT a workflow crash. It's GitHub's Code Scanning result check based on the SARIF Trivy uploaded — it reports "this branch has open security alerts." The alerts are pre-existing on master, not introduced by R4.

---

## 3. Open issues to address (BOTH per operator 2026-06-02)

### 3.1 (a) Client Quality post-ESLint failure — investigation needed

**What we know**:
- Job: `Client Quality (Prettier + ESLint)` in workflow `sdap-ci.yml` lines ~70-90
- Steps in order:
  1. Configure git line endings ✓
  2. Checkout ✓
  3. Setup Node.js ✓
  4. Install root deps (Prettier) ✓
  5. Prettier format check ✓ (after our fix)
  6. Install PCF deps (ESLint) ✓
  7. ESLint check ✓ (0 errors, 186 warnings within tolerance)
  8. Install Spaarke.AI.Outputs deps — **unknown**
  9. Build Spaarke.AI.Outputs (declarations) — **unknown**
  10. Install Spaarke.AI.Widgets deps — **unknown**
  11. **`@spaarke/ai-widgets tsc --noEmit`** — **PROBABLE FAILURE POINT** per R4 task 061 NFR-05 (added this gate)
  12. (post-steps) — cache, cleanup ✓

**Why suspected**: R4 task 067 made tsc changes. After merging master's 222 commits, there may be new tsc errors from interactions between R4's type tightening and master's new code (Insights Engine R2, test-suite-repair, etc.).

**Verify locally to reproduce**:
```bash
cd /c/code_files/spaarke-wt-spaarke-ai-platform-unification-r4

# 1. Build Spaarke.AI.Outputs declarations first
cd src/client/shared/Spaarke.AI.Outputs
npm install --legacy-peer-deps --no-audit --no-fund
npm run build  # produces dist/

# 2. Then tsc --noEmit on AI.Widgets
cd ../../shared/Spaarke.AI.Widgets
npm install --legacy-peer-deps --no-audit --no-fund
npx tsc --noEmit
```

If it produces errors → that's the failure. Fix the tsc errors (likely type drift in Widgets after merge), commit, push.
If it's clean → look elsewhere in the workflow. Get the actual log:
```bash
cd /c/code_files/spaarke && gh run view --log-failed --job=<latest-job-id-for-client-quality>
```

### 3.2 (b) Trivy `azure-storage-account-key` secret alert — URGENT investigation

**What we know**:
- Alert ID: `azure-storage-account-key`
- Severity: `error`
- Origin: pre-existing on master (likely accumulated from various projects)
- One of 20 pre-existing alerts shown via Code Scanning

**This is potentially a REAL credential leak**, not a CVE. If a Storage Account Key is in source control, it must be:
1. **Rotated** in Azure (key cycling)
2. **Removed** from git history (BFG repo-cleaner or git filter-repo)
3. **Replaced** with managed identity / Key Vault reference

**Find the source location**:
```bash
cd /c/code_files/spaarke && gh api "/repos/spaarke-dev/spaarke/code-scanning/alerts?state=open&per_page=100" \
  --jq '.[] | select(.rule.id == "azure-storage-account-key") | {location: .most_recent_instance.location.path, line: .most_recent_instance.location.start_line, snippet: .most_recent_instance.location.region}'
```

This will tell us:
- File path where the key was committed
- Line number
- Surrounding context

Then check if the key is still valid (try connecting to that storage account) or if it's already been rotated. If still valid → IMMEDIATE ROTATION required, then clean from history.

### 3.3 Full Trivy CVE catalog (for reference)

20 open Code Scanning alerts (deduplicated). All present on master before PR #331:

| Severity | ID | Disposition |
|---|---|---|
| error | CVE-2024-43485 | Unknown — investigate |
| error | CVE-2026-26171 | Unknown — investigate |
| error | CVE-2026-26996 | Unknown — investigate |
| error | CVE-2026-27903 | Unknown — investigate |
| error | CVE-2026-27904 | Unknown — investigate |
| error | CVE-2026-33116 | Unknown — investigate |
| error | **CVE-2026-44503** | **Microsoft.Kiota.Abstractions HIGH** — DEFERRED per R4 task 080 to `spaarke-graph-sdk-kiota-upgrade-r1` future project. Should be acceptable residual via `.trivyignore` with documented rationale. |
| error | CVE-2026-46681 | @nevware21/ts-utils Prototype Pollution — investigate |
| error | **azure-storage-account-key** | **Secret leak — URGENT** |
| warning | CVE-2026-24001 | Note severity |
| warning | CVE-2026-30227 | MimeKit — R4 task 080 patched to 4.15.1; scanner may be seeing stale (verify) |
| warning | CVE-2026-33750 | Unknown |
| warning | CVE-2026-40894 | OpenTelemetry.Api — R4 task 080 patched (1.15.3); scanner may be stale (verify) |
| warning | CVE-2026-41238 | DOMPurify XSS |
| warning | CVE-2026-41239 | DOMPurify XSS |
| warning | CVE-2026-41240 | DOMPurify XSS |
| warning | CVE-2026-41511 | OpenMcdf — R4 task 080 patched (3.1.4); scanner may be stale (verify) |
| warning | CVE-2026-41907 | Unknown |
| warning | CVE-2026-45785 | OpenMcdf — R4 task 080 patched (3.1.4); scanner may be stale (verify) |
| warning | GHSA-39q2-94rc-95cp | DOMPurify ADD_TAGS bypass |

---

## 4. Step-by-step playbook for post-compact resume

### Step 1: Verify branch state
```bash
cd /c/code_files/spaarke-wt-spaarke-ai-platform-unification-r4
git branch --show-current   # expect: work/spaarke-ai-platform-unification-r4
git log --oneline -3         # latest should be a78145cc Prettier fix
git status --short           # expect: clean
```

### Step 2: Check current PR state
```bash
cd /c/code_files/spaarke && gh pr checks 331
```

If Client Quality + Trivy still failing → proceed. If anything else changed → re-read §2.

### Step 3: Issue (a) — Client Quality investigation
```bash
cd /c/code_files/spaarke-wt-spaarke-ai-platform-unification-r4

# Build AI.Outputs first (declarations required by Widgets)
cd src/client/shared/Spaarke.AI.Outputs && \
  npm install --legacy-peer-deps --no-audit --no-fund && \
  npm run build

# Then tsc on AI.Widgets
cd ../../shared/Spaarke.AI.Widgets && \
  npm install --legacy-peer-deps --no-audit --no-fund && \
  npx tsc --noEmit
```

- If errors → fix them, commit, push to PR branch.
- If clean → fetch the actual CI log:
  ```bash
  cd /c/code_files/spaarke && \
    gh run view --log-failed --job=<latest-job-id-from-gh-pr-checks-331>
  ```

### Step 4: Issue (b) — Trivy azure-storage-account-key
```bash
cd /c/code_files/spaarke && gh api "/repos/spaarke-dev/spaarke/code-scanning/alerts?state=open&per_page=100" \
  --jq '.[] | select(.rule.id == "azure-storage-account-key")' | head -30
```

- Find the file/line.
- Read the snippet to confirm if it's a real key or test fixture / placeholder.
- If real → present to operator IMMEDIATELY (urgent decision needed: rotate + clean history).
- If test fixture / placeholder / docs example → add to Trivy ignore with rationale.

### Step 5: After both green
Operator merges PR #331 via GitHub UI (recommend: "Create a merge commit" or "Rebase and merge"; NOT squash — preserves R4 commit history).

### Step 6: Post-merge cleanup
```bash
cd /c/code_files/spaarke
git fetch origin
git reset --hard origin/master   # aligns local master with origin (now contains R4 + all post-fix commits)
```

This resolves the "local master 21 ahead of origin/master" state from Step 1 of the merge workflow.

---

## 5. Operator decisions captured (chronological)

| Date | Decision |
|---|---|
| 2026-06-02 | Hold R4 merge until operator's main-repo "Assistant fix" work was done |
| 2026-06-02 | Pull master into R4 worktree first to surface + resolve conflicts safely |
| 2026-06-02 | 42 conflicts resolved in 3 phases — main session (not sub-agents) |
| 2026-06-02 | Resolved phase 1 (docs + ADR-030/032 renumber) + phase 2 (22 shared lib code: R4 cleanup wins on type/lint hygiene) + phase 3 (17 BFF tests: master's test-suite-repair wins comprehensive mock isolation) |
| 2026-06-02 | Discard CorsModule.cs line-ending swap — accept as benign noise |
| 2026-06-02 | Skip the 26 doc files + 4,200 exports — operator will handle separately |
| 2026-06-02 | Path A: merge in main repo + push (rejected protected branch) → fallback to PR |
| 2026-06-02 | PR #331 created with comprehensive body |
| 2026-06-02 | "CI gates are load-bearing or noise — fix or remove. Don't bypass." (binding principle) |
| 2026-06-02 | Fix Prettier (done: `a78145cc`). Investigate ESLint after Prettier passes (turned out passes within 186-warning tolerance). |
| 2026-06-02 | Investigate both (a) Client Quality post-ESLint + (b) Trivy azure-storage-account-key |
| 2026-06-02 | Operator requested compact + checkpoint creation (this file) |

---

## 6. Recovery commands for next session

```bash
# Confirm worktree state
cd /c/code_files/spaarke-wt-spaarke-ai-platform-unification-r4
git branch --show-current && git log --oneline -3 && git status --short

# Confirm PR state
cd /c/code_files/spaarke && gh pr checks 331

# Read this file
cat projects/spaarke-ai-platform-unification-r4/notes/pr-331-merge-followup.md

# Then execute Step 3 (Client Quality) + Step 4 (Trivy secret) per §4 above
```

---

## 7. What to AVOID after compact

- **Do NOT** push code that bypasses any CI gate (per operator binding principle 2026-06-02)
- **Do NOT** add CVE entries to `.trivyignore` without documented rationale + operator approval
- **Do NOT** touch operator's main-repo dirty state (26 docs + 4,200 exports) — operator said "handle separately"
- **Do NOT** force-merge PR #331 via "admin override" — wait for legitimately green CI
- **Do NOT** rewrite the R4 wrap-up artifacts (lessons-learned, README, plan) — R4 itself is closed

---

*End of checkpoint. Safe to compact after this file is committed + pushed.*
