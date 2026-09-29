# Branch protection ENABLED on master — 2026-08-29

> Owner-directed, this session. Supersedes the "protection is DISABLED" state recorded in
> `branch-protection-current.json` (2026-06-26) and `branch-protection-pre-cutover.json`.
>
> **This changes task CICD-071's cutover procedure. Read §3 before running it.**

---

## 1. What is now enforced

Repository **ruleset** `21824191` — *"master: require CI Router"* — `enforcement: active`, targeting
`~DEFAULT_BRANCH`:

| Rule | Setting |
|---|---|
| `required_status_checks` | **`Router`**, `strict_required_status_checks_policy: false` |
| `pull_request` | required, **0 approving reviews** (single-maintainer repo) |
| `non_fast_forward` | force-push blocked |
| `deletion` | branch deletion blocked |

Verify what actually applies at any time:

```bash
gh api repos/spaarke-dev/spaarke/rules/branches/master     # effective rules on the ref
gh api repos/spaarke-dev/spaarke/rulesets/21824191         # the ruleset object
```

**Rollback** (restores the pre-2026-08-29 state exactly):

```bash
gh api -X DELETE repos/spaarke-dev/spaarke/rulesets/21824191
```

---

## 2. Why now, rather than at cutover

The shadow window gates **deleting `sdap-ci.yml`** (task 077). It does **not** gate *having a gate*.
Those were conflated. Until today master had **no protection of any kind** — any push, from anyone,
with no CI. That was the single largest exposure in the CI/CD setup, and it was independent of
whether the new tier had finished proving itself.

Evidence supporting `Router` as the sole required check at this point: **18 of 20 comparable PRs
agreeing, 0 false greens**, Tier 1 p95 within its ~3-minute budget. The old CI keeps running in
parallel and keeps feeding the window, so **enabling protection did not disturb the measurement**.

`strict: false` is deliberate — see §4.

---

## 3. 🚨 Task CICD-071's cutover command will NOT work as written

Task 071 step 4 is:

```bash
gh api -X PUT repos/spaarke-dev/spaarke/branches/master/protection \
  -F required_status_checks[contexts][]='CI / Router'
```

**Three independent defects:**

1. **The classic endpoint is unavailable on this repo.** Both `GET` and `PUT` on
   `/branches/master/protection` return `404 — "Branch protection has been disabled on this
   repository"`, despite the authenticated user holding `admin: true` and a `repo`-scoped token, and
   despite the repo being **public** (so no private-plan limitation applies). Rulesets are the working
   mechanism. **Corollary: the 404 recorded in the 2026-06-26 snapshots is NOT proof that protection
   was deliberately switched off** — the same 404 is returned when the endpoint simply does not serve
   this repo. That inference should not be carried forward.
2. **The context name is wrong.** The actual check-run name is **`Router`**, not `CI / Router`. A
   required context that never reports blocks every PR permanently — the worst possible failure mode
   for a 4-hour cutover window.
3. **The payload is incomplete.** `PUT` on the classic endpoint requires a full object —
   `required_status_checks` needs `strict` alongside `contexts`, plus top-level `enforce_admins`,
   `required_pull_request_reviews`, and `restrictions`. A single `-F` field would 422 even if the
   endpoint were available.

**Action for 071**: replace step 4 with *"verify ruleset 21824191 is active and requires `Router`"* —
the protection half of the cutover is already done. 071's remaining substance is the **merge queue**
and the `Release` matrix restore.

---

## 4. `strict: false` — deliberate, and it contradicts the old plan

`branch-protection-pre-cutover.json` names `strict: true` as the forward target. **That is wrong for
this repo** and was not adopted.

`strict: true` means a PR branch must contain the newest master commit before it can merge. With
several PRs open, the first merge invalidates all the others: each author must update and wait for a
full CI cycle. That serializes merging and is precisely **north star #2** ("CI must not hold up
high-frequency master builds and pushes"). It also matches the owner's earlier 2026-06-05 decision to
run `strict=false` under the "PR + advisory CI" model.

The correct instrument for the risk `strict` addresses (semantic conflicts — two PRs that pass alone
and break together) is the **merge queue**, which tests the combined result without forcing every
author to manually rebase and re-wait. Merge queue remains in scope for task 071.

Partial mitigation already in place: `/merge-to-master` **Step 2.5** (added 2026-08-29) updates the
branch from master and resolves conflicts *before* merging. That is voluntarily doing what `strict`
would compel — but it is a **convention, not enforcement**: it only applies when the skill is used,
not when someone merges from the GitHub UI. Merge queue is the enforcement answer.

---

## 5. Blast radius

8 PRs from other active projects were open when this landed (#887, #859, #806, #636, #526, #507,
#453, #452). They now require `Router` green to merge. Spot-checked at enable time: **#887 reports
`mergeable=MERGEABLE`, `state=CLEAN`, `Router: SUCCESS`** — so existing green PRs were not disrupted.

Docs-only PRs are safe: Tier 1 **skips** on a docs-only diff but `Router` still reports **success**
(`if: always()` + tier2/tier1 excluded-or-skipped handling). Verified against #891 and #892, both
docs-only, both `Router: success`.
