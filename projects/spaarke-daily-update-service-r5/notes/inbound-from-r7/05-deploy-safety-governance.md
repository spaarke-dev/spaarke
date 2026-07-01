# 05 — Deploy-safety governance

> **Priority**: PROCESS — not code; feeds into R5 development workflow
>
> **Source**: R7 W12 UAT incident, 2026-06-30 → 2026-07-01
>
> **Scope**: How to coordinate concurrent BFF + widget deploys across active worktrees

## Verbatim operator concern

> "do you merge or rebase master before doing a local deploy? because you could be overwriting others"

Asked after R7 discovered that a concurrent `spaarkeai-compose-r1` deploy briefly overwrote R7's Daily Briefing widget bundle in spaarkedev1.

## What happened during R7 W12

Timeline (UTC, 2026-06-30 to 2026-07-01):

- **04:07** — `spaarkeai-compose-r1` GitHub Actions auto-deployed BFF API to spaarkedev1 (their normal CI/CD)
- **~15:xx** — R7 deployed BFF API to spaarkedev1 (Daily Briefing collector fixes)
- **16:14** — `spaarkeai-compose-r1` GitHub Actions auto-deployed the SpaarkeAi widget bundle (their bundle contained no R7 changes because they'd branched before R7's widget code shipped)
- **~16:15+** — R7's newly-deployed widget features (HighPriority section, inline references, navigateTo fix) disappeared in spaarkedev1 — overwritten by compose-r1's older widget bundle
- **~17:xx** — R7 rebuilt widget from R7 worktree + redeployed manually; features restored

Root cause: two teams landing changes to the same shared BFF + widget in the same environment without cross-team awareness of who last deployed. The compose-r1 team wasn't ignoring R7 work — their CI happened to auto-deploy while R7 changes were in flight.

## The governance rule (adopted 2026-07-01)

**ALWAYS sync master into the worktree BEFORE building + deploying locally, so we never overwrite in-flight master changes from other teams.**

Standard sequence for any local deploy from a worktree:

```powershell
# 1. Fetch latest from origin
git fetch origin

# 2. Fast-forward if possible (or rebase if divergence); NEVER force
git merge --ff-only origin/master
# OR if diverged:
git rebase origin/master

# 3. Sanity check what you're actually about to deploy
git log origin/master..HEAD --oneline

# 4. Build with master's changes incorporated
dotnet build src/server/api/Sprk.Bff.Api/
cd src/solutions/SpaarkeAi && npm run build

# 5. Deploy
../../.. && ./scripts/Deploy-BffApi.ps1
./scripts/Deploy-SpaarkeAi.ps1
```

Why this is enough:

- If someone else's PR already merged to master, step 2 pulls their changes into the worktree — R5's deploy bundle then contains their code + R5's code
- If step 2 shows divergence (rebase needed), it's a signal to check whether R5's changes conflict with the incoming code before proceeding
- Step 3 makes intent visible ("here's what my deploy adds on top of master")
- Steps 4-5 deploy a bundle that reflects the union of master + local changes, never a bundle that's missing someone else's merged work

## Additional guardrails to consider for R5 development

### 1. Watchlist coordination

`projects/INDEX.md` (added by `ci-cd-unit-test-remediation-r1`) enumerates every active worktree and declares hot-path touch (BFF Y/N, SpaarkeAi Y/N, etc.). Before deploying:

```
grep -E "BFF.*Y" projects/INDEX.md
```

Any row with `BFF=Y` is a worktree that might deploy BFF concurrently. Awareness of the current active set helps time R5 deploys around known auto-deploy cadences (compose-r1 auto-deploys on merge; that's predictable).

### 2. Sole-deploy convention for shared environments

Consider adopting a "reserved deploy window" convention: a Slack ping or a short-lived flag file (`deploy/.reserved-{env}-{project}`) that signals "R5 is actively deploying, please pause auto-deploys for 15 min." Lightweight, honest about the collaboration.

R5 design can decide whether this is warranted or if the sync-master-first rule alone is sufficient.

### 3. CI runs on the deploying branch, not on master

Currently `spaarkeai-compose-r1` runs GitHub Actions on their branch and auto-deploys. R7 W12 rebuilt + redeployed manually. Both are correct in isolation, but they don't compose. Options:

- **Every deploy runs from master** (post-merge only). Downside: slower feedback loop for feature branches.
- **Deploy from feature branches allowed, but coordination doc requires master-sync-first**. Current implicit convention; R5 formalizes.
- **Deploy from feature branches allowed with per-project deploy-namespace** (e.g., R5 deploys to `spaarkedev1-r5` and R7 to `spaarkedev1-r7`). Not feasible for Daily Briefing since it's a shared BFF + widget in one Dataverse environment.

Recommend R5 formalize the second option (feature-branch deploys OK + master-sync-first mandatory) as the operating convention.

## Impact on R5 design.md

- Add a "Deploy Governance" section referencing this document
- If R5 introduces any auto-deploy trigger (e.g., new GitHub Actions workflow), it MUST include a master-sync step before build
- Any R5 script under `scripts/Deploy-*.ps1` that R5 adds SHOULD emit a warning if the local branch is behind `origin/master`

## References

- R7 checkpoint documenting the incident: `projects/spaarke-ai-platform-unification-r7/current-task.md` § "Deploy safety governance (added 2026-07-01 per operator concern)"
- Compose-r1 project (concurrent BFF worktree during R7 W12): `projects/spaarkeai-compose-r1/`
- Active-project registry: [`projects/INDEX.md`](../../../../projects/INDEX.md)
- `merge-to-master` skill (Path A auto-merge PR is the correct pattern for protected master): `.claude/skills/merge-to-master/SKILL.md`
- Deploy scripts: `scripts/Deploy-BffApi.ps1`, `scripts/Deploy-SpaarkeAi.ps1`
