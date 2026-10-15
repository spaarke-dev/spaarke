> **SUPERSEDED 2026-10-14. Do not execute.** This June 2026 draft planned the cutover via classic branch protection (`PUT /branches/master/protection`, which returns 404 on this repo) plus a merge queue. The cutover actually used repository ruleset 21824191 (required checks `Router` + `Client lint (type-aware ratchet)`), and the legacy `sdap-ci.yml` was retired in PR #1863. Kept, uncommitted until now, in the stale project worktree; moved here per the move-never-delete rule.

# Task CICD-071 — Cutover Runbook (pre-flight COMPLETE; execution ON HOLD)

**Filed**: 2026-06-29
**Author**: Claude (autonomous prep per user direction)
**State**: Pre-flight COMPLETE. Execution PAUSED awaiting user GO ("finish for the night").

---

## Pre-flight state (verified 2026-06-29 ~01:05 UTC)

| Check | Result |
|---|---|
| PR #482 (087+088+088b ADR fixes) | ✅ MERGED |
| PR #504 (085 ambiguous review: 18 delete / 48 keep) | ✅ MERGED |
| PR #505 (084 contract relocation: 191 methods → tests/integration/contract/) | ✅ MERGED |
| Master HEAD | `d4538dfde` |
| Last completed master CI Router run | ✅ SUCCESS (`64c0fa603`) |
| Master CI on `d4538dfde` | ⏳ pending (running at runbook time; expected SUCCESS based on 87/88/88b+084+085 all individually green) |
| Branch protection snapshot for rollback | ✅ Present (`notes/branch-protection-pre-cutover.json`; pre-state = DISABLED) |
| Current branch protection | DISABLED (matches snapshot — rollback = re-disable) |
| Worktree synced | ✅ at `d4538dfde` |

## What's DEFERRED (do NOT do at cutover)

Per spec MUST NOT rule: **Release matrix restoration is gated by task 075 (≥7 days surviving suite green post-cutover)**. So the original 071 step 2 ("restore Release matrix") is INTENTIONALLY skipped in this cutover. It happens later, in task 075 or after.

Cutover therefore becomes **2 settings flips** (not 3):
1. Branch protection — flip to require only `CI / Router`
2. Merge queue — enable (batch=1, no speculative, 30min timeout)

---

## Cutover commands (READY TO EXECUTE)

### Step 1 — Final pre-flight (do this 5 min before cutover)

```bash
# 1a. Confirm master CI on HEAD is green (NOT cancelled, NOT pending)
gh run list --workflow=CI --branch=master --limit=1 --json conclusion,headSha,createdAt

# 1b. Confirm branch protection is currently disabled (matches snapshot)
gh api repos/spaarke-dev/spaarke/branches/master/protection 2>&1 | grep -E "DISABLED|404"

# 1c. Confirm snapshot file exists
ls -la projects/ci-cd-unit-test-remediation-r1/notes/branch-protection-pre-cutover.json

# 1d. Confirm CI / Router check has appeared on a recent commit (so GitHub recognizes the check name)
gh api repos/spaarke-dev/spaarke/commits/master/check-runs --jq '.check_runs[] | select(.name=="Router") | {name, status, conclusion}'
```

All four must return clean results before proceeding.

### Step 2 — Flip branch protection

```bash
# Apply branch protection: require ONLY `CI / Router` as required status check
gh api -X PUT repos/spaarke-dev/spaarke/branches/master/protection \
  -F required_status_checks[strict]=true \
  -F 'required_status_checks[contexts][]=CI / Router' \
  -F enforce_admins=false \
  -F required_pull_request_reviews=null \
  -F restrictions=null \
  -F allow_force_pushes=false \
  -F allow_deletions=false \
  -F required_conversation_resolution=false \
  -F lock_branch=false \
  -F allow_fork_syncing=true \
  -F block_creations=false
```

### Step 3 — Enable merge queue

The merge queue must be enabled via **GitHub web UI** (no full gh CLI API for the merge_queue setting as of last check):

1. https://github.com/spaarke-dev/spaarke/settings/branches
2. Find the `master` branch rule (the protection from Step 2 will appear)
3. **Edit** that rule
4. Under "Require merge queue":
   - ☑ Require merge queue
   - **Merge method**: Merge commit (matches existing convention)
   - **Build concurrency**: `1` (batch=1 per spec FR-C01; no speculative)
   - **Maximum entries to build**: `5` (reasonable starting point)
   - **Maximum entries to merge**: `5`
   - **Minimum entries to wait for**: `1`
   - **Wait time**: `5` minutes (short window before forced flush)
   - **Status check timeout**: `30` minutes per spec FR-C01

5. Save the rule

### Step 4 — Verify post-cutover state

```bash
# 4a. Branch protection now requires CI / Router
gh api repos/spaarke-dev/spaarke/branches/master/protection \
  --jq '{required: .required_status_checks.contexts, strict: .required_status_checks.strict}'

# Expected output:
# { "required": ["CI / Router"], "strict": true }

# 4b. Merge queue enabled
gh api graphql -f query='{
  repository(owner:"spaarke-dev", name:"spaarke") {
    branchProtectionRules(first:10) {
      nodes { pattern requiresMergeQueue }
    }
  }
}'

# Expected: master rule shows requiresMergeQueue: true
```

### Step 5 — Test merge

```bash
# Create a trivial test PR (use this worktree)
git checkout work/ci-cd-unit-test-remediation-r1
git fetch origin master
git merge origin/master --no-edit  # if not already up to date

# Make a trivial change (e.g., append timestamp to a notes file)
echo "Cutover test merge at $(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  >> projects/ci-cd-unit-test-remediation-r1/notes/cutover-test-merges.md
git add projects/ci-cd-unit-test-remediation-r1/notes/cutover-test-merges.md
git commit -m "test(cutover-071): test merge through merge queue"
git push origin work/ci-cd-unit-test-remediation-r1

# Open PR
gh pr create --base master --title "test(cutover-071): merge queue verification" \
  --body "Verifies CI / Router gate + merge queue per task 071 acceptance criterion."

# Watch the PR's checks: should see CI / Router run, then merge queue activate
gh pr checks --watch

# After CI / Router passes, the PR should auto-enter merge queue
# Then merge_group event fires CI / Router again on the queued ref
# Then merges

# When merged, verify master CI / Router on the merge commit
gh run list --workflow=CI --branch=master --limit=1
```

### Step 6 — Document success

If everything green:

```bash
cat > projects/ci-cd-unit-test-remediation-r1/notes/cutover-success.md <<EOF
# Task CICD-071 — Cutover SUCCESS

**Cutover timestamp**: $(date -u +%Y-%m-%dT%H:%M:%SZ)
**Master HEAD at cutover**: $(git rev-parse origin/master)
**Test merge PR**: #<NUM>
**Test merge SHA**: $(git rev-parse origin/master)

## Final state
- Branch protection on master: requires \`CI / Router\` only
- Merge queue: enabled (batch=1, timeout 30min)
- sdap-ci.yml: still running in parallel (non-required; retires in task 077 at cutover+14d)
- Release matrix: NOT YET restored (gated by task 075 7-day soak per spec MUST NOT)

## Next phases
- T+7d: task 075 — verify 7-day surviving suite green → restore Release matrix
- T+14d: task 077 — retire sdap-ci.yml
- T+30d: task 076 — SC-01..SC-10 measurements window closes
- T+30d+: task 090 — wrap-up with /test-diet
EOF
git add projects/ci-cd-unit-test-remediation-r1/notes/cutover-success.md
git commit -m "test(cutover-071): success — branch protection + merge queue enabled"
git push origin work/ci-cd-unit-test-remediation-r1
```

---

## ROLLBACK procedure (if needed)

Trigger conditions per spec §152:
- Tier 1 flake rate > 2% sustained over 24h
- Master green rate < 90% over 24h
- > 3 false-positive blocks within 24h
- Merge queue stalls > 2h with > 4 PRs

### Rollback commands

```bash
# 1. Disable branch protection (restore to pre-cutover DISABLED state per snapshot)
gh api -X DELETE repos/spaarke-dev/spaarke/branches/master/protection

# 2. Disable merge queue via GitHub UI:
#    Settings → Branches → master → uncheck "Require merge queue" → Save

# 3. Verify rollback
gh api repos/spaarke-dev/spaarke/branches/master/protection 2>&1 | grep -E "DISABLED|404"
# Expected: 404 (matches pre-cutover snapshot)

# 4. Document
cat > projects/ci-cd-unit-test-remediation-r1/notes/cutover-rollback.md <<EOF
# Task CICD-071 — Rollback EXECUTED

**Rollback timestamp**: $(date -u +%Y-%m-%dT%H:%M:%SZ)
**Trigger**: <which of the 4 trigger conditions fired>
**Evidence**: <link to CI runs / specific failures>
**Recovery actions**: re-disabled branch protection, disabled merge queue
**Next step**: investigate root cause; plan re-cutover attempt
EOF
git add projects/ci-cd-unit-test-remediation-r1/notes/cutover-rollback.md
git commit -m "test(cutover-071): rollback executed — <trigger>"
git push
```

---

## Estimated active operator time

| Step | Time |
|---|---|
| Pre-flight check (Step 1) | 5 min |
| Branch protection flip (Step 2) | 2 min |
| Merge queue enable via UI (Step 3) | 5 min |
| Verify state (Step 4) | 3 min |
| Test merge (Step 5) | 10-15 min (includes CI run wait) |
| Document success (Step 6) | 3 min |
| **Total** | **~30 min active** |

Plus passive monitoring overnight = no operator action needed unless rollback triggers.

---

## After cutover succeeds

When you wake up:
1. Glance at `gh run list --workflow=CI --branch=master --limit=5` — should be all green
2. Glance at `gh api repos/.../branches/master/protection` — confirms CI / Router is the sole required check
3. Resume normal velocity (with new tier semantics)

Phase 3 calendar-gated tasks fire automatically per schedule:
- 075 (7-day soak gate)
- 077 (sdap-ci retirement at cutover+14d)
- 076 (30-day SC measurement window)
- 090 (wrap-up after SC measurements complete)
