# R3 Lessons Learned — Daily Briefing Read-State Decoupling + TTL Hardening

> **Project**: spaarke-daily-update-service-r3
> **Created**: 2026-06-25
> **PR**: [#451](https://github.com/spaarke-dev/2/pull/451)
> **Outcome**: All 7 ACs delivered; UAT confirmed AC-3a (the headline bug fix); peripheral issues surfaced that motivated R4

---

## TL;DR

R3 was scoped narrowly as a defect-fix project (5 tasks, ~6h estimated, 1 day execution): decouple the Daily Briefing widget's read-state from `appnotification.toasttype` (which was being misinterpreted) and fix a parallel `ttlindays` → `ttlinseconds` field-name bug in `NotificationService.cs`. The execution succeeded. **UAT, however, revealed that the widget had deeper structural defects R2 had inherited but R3 explicitly out-of-scoped** — `/narrate` hallucination, dead preferences, missing JPS Action deployment, stub playbooks. R3 ships its narrow fixes and motivates R4 to address the broader gap.

---

## What went well

### 1. Narrow scope held
R3's spec explicitly out-of-scoped weekend-aware TTL math, widget-side membership filtering, narrative quality, and producer playbook fixes. We resisted scope creep during execution. Result: a small, reviewable PR (~1100 LOC of code change across 9 files + 24 new jest tests + 1 BFF test) with zero ADR violations and zero §10/§11 issues at the final code-review gate.

### 2. Wave 1 parallel execution worked
Tasks 001 (Dataverse schema), 010 (BFF fix), 020 (widget service refactor) were parallel-safe — they touched completely independent surfaces. Dispatched as 3 concurrent `general-purpose` agents. Wall-clock wins:
- Sequential estimate: 7.5h
- With Wave 1 parallel: ~6.5h actual
- ~1h saved (~13% reduction)

The constraint was task 020 (widget service) being the long pole. Tasks 001 and 010 each completed in ~5 min and ~7 min respectively; task 020 took ~11 min. Real parallelism payoff for the dispatch overhead.

### 3. JSX-agnostic hook design
Task 030 (agent's call) kept `useBriefingActions` **without** importing `@fluentui/react-components`. Instead it returns a `BriefingActionOptions<T>` callback bag (`onOptimistic` / `onSuccess` / `onRevert` / `onError`). The UI layer (task 031) constructs the actual `<Toast>` JSX. This:
- Made the hook unit-testable in isolation (10 new tests, no DOM required)
- Mirrored the existing `handleAddToTodo` canonical pattern (ADR-024 sibling)
- Preserved layer boundaries cleanly

The code-review at branch-level called this out as a "What's Good" item. It's worth keeping the pattern.

### 4. Failure-mode rigor on FR-3 AC-3c
Task 020's null-coalesce read derivation (`((entity['sprk_briefingstate'] as number) ?? 0) === BRIEFING_STATE_CHECKED`) eliminated the need for a backfill of pre-rollout existing rows. The 24 new jest tests explicitly cover:
- `sprk_briefingstate = 0` → Unread
- `sprk_briefingstate = 1` → Checked  
- `sprk_briefingstate = 2` (filtered out by EXCLUDE_REMOVED_FILTER)
- `sprk_briefingstate = undefined` → Unread (FR-3 AC-3c)
- `sprk_briefingstate = null` → Unread (FR-3 AC-3c)
- FR-7 invariant: `toasttype = 200000000` alone is Unread

This level of explicit coverage on the edge cases is what made the agents' inline code-review come back clean.

### 5. Default propagation from Dataverse Choice column worked
Spec Risk R5 hedged on "Dataverse may not honor Choice column default on a Microsoft-owned table." Task 001's agent verified empirically by creating + deleting a test `appnotification` row and confirming the new row surfaced `sprk_briefingstate = 0` automatically. **Risk R5 unfounded — Dataverse handled it correctly.** Removes one defensive code path we'd planned.

---

## What was hard / what we learned

### 1. Two semantic-mismatch bugs in two projects — pattern, not coincidence

R2 Phase B fixed `markAllNotificationsRead` writing to `toasttype` (display behavior) as if it were a read marker. R3 fixes the same class of bug on the **read** side (widget reading `toasttype === 200000000` as "Dismissed") AND on the **producer** side (`NotificationService` writing `ttlindays` — a field that doesn't exist — when the canonical column is `ttlinseconds`).

**The pattern**: Microsoft OOB option-set values and field names have canonical meanings documented on Microsoft Learn. Spaarke code in 2 separate places treated them as semantic-neutral integers/strings.

**Lesson for future Microsoft-platform work**: When writing or reading any OOB Dataverse field, **verify against Microsoft Learn** before assuming semantic meaning. Especially for option-set values where the integer code carries non-obvious display-behavior meaning (`toasttype = 200000000` is "Timed", not "Dismissed").

**Mitigation going forward**: R4 will add JPS-validated entity-name allow-list scrubbers and `/narrate` prompt grounding. But the systemic risk persists — any future producer/consumer code that reads/writes OOB fields could hit this class of bug.

### 2. UAT revealed scope tension — narrow R3 vs broader user-expected fix

The user expected Daily Briefing to "work end-to-end" after R3 deploy. R3 fixed the headline bug (empty state) — but UAT immediately surfaced:
- TL;DR hallucinations (Johnson & Lee LLP not in user data)
- Recency preference doesn't filter
- Activity Notes disappears after clicks (`/narrate` cache + render-on-empty)
- Inline button UX collision (R3 introduced 3 new ✓ icons next to existing ✓ Add-to-To-Do)

**Lesson**: When a project scopes narrowly (defect fix only), the UAT experience may still feel "broken" because the user evaluates the surface as a whole. Two ways to handle this:
- (a) **Explicitly time-box UAT** to the in-scope ACs only — pre-commit to "verify FRs, don't evaluate the surface holistically"
- (b) **Bundle adjacent fixes** if the project owner suspects the surface has broader issues

For R3 we chose (a) implicitly — and the broader UAT findings became R4's scope. This is fine architecturally, but worth setting that expectation explicitly in pre-UAT briefings going forward.

### 3. The icon collision — UX debt I introduced

Task 031 added 3 new R3 action icons (Check ✓, Remove ✗, Keep +7d 📅) **inline** next to the existing Add-to-To-Do (also ✓) and Dismiss (✗) actions. Two checkmarks side-by-side, two X icons side-by-side. UAT user immediately clicked the wrong button.

**Lesson**: When adding new actions to an existing UI with established action icons, **audit visual collisions first**. The R3 icons were owner-specified per spec — but the placement (inline, all visible) was my design choice. Better choice would have been overflow-menu pattern from the start (which is now R4 FR-18).

**For future similar UI work**: When 3+ actions exist in a single surface, default to overflow menu unless there's a strong reason to expose all inline.

### 4. Husky bootstrap in fresh worktrees

Each new worktree fails to commit/push until `.husky/_/h` and `.husky/_/husky.sh` are copied from the main repo (they're npm-prepare-generated and gitignored). Hit this twice in R3 and again in R4 worktree setup.

**Lesson**: The husky helper files should be re-bootstrapped automatically by `npm install` in the worktree, OR `/worktree-setup` should include the copy step. Filing as backlog: improve worktree-setup skill to handle this automatically.

### 5. CI Prettier auto-format added merge commits to my push flow

Several of my pushes during R3 came back with "rejected — fetch first" because the CI auto-format had pushed a commit on top of mine while I was working. Each time I `git pull --rebase`, the Prettier-format commit was preserved, then pushed cleanly.

**Lesson**: This is fine workflow but adds noise to the commit history (`d67c89e42 style: auto-format Prettier (CI)` × 3 commits in R3). Worth running `npm run format` locally before push to short-circuit the round-trip.

### 6. JPS deployment gap — code shipped ≠ data deployed

While investigating R4 architecture, I queried Dataverse for `sprk_analysisaction` rows with `sprk_executoractiontype = 52` (LookupUserMembership) — found **0 rows in spaarkedev1** despite the C# `LookupUserMembershipNodeExecutor` having shipped from platform-foundations-R3.

**This is a generalizable lesson** — and the exact same failure mode that Insights Engine Phase 1 hit ("Phase 1 deployment didn't wire `sprk_analysisaction` rows for the new ActionTypes — a JPS-convention gap, not an engine bug"). Spaarke's AI architecture has a critical principle: **JPS is data, not code** (per `INSIGHTS-ENGINE-ARCHITECTURE.md`). Source-code changes to NodeExecutor classes are necessary but not sufficient — corresponding `sprk_analysisaction` rows must be deployed to each environment.

**Lesson for future R-projects that add new ActionTypes**:
- Add a checklist item to the project's plan: "Deploy `sprk_analysisaction` row for new ActionType to {env}."
- Include `jps-validate` and `jps-action-create` in task knowledge files.
- Add a Dataverse query as a deployment-verification step.

R4 will close this specific gap (Workstream 0) and lift it to be a first-class spec concern.

---

## Cross-project observations

### R2 → R3 inheritance pattern

R2 set up the Pattern D widget structure (the consumer layer R3 inherited). R3 fixed defects that R2 hadn't surfaced. R4 will fix defects that **R2 introduced architecturally** (preferences hooks wired to nothing; `/narrate` as hardcoded BFF endpoint not JPS playbook; `ActivityNotesSection` hides when narratives are empty).

**Lesson**: When a project rebuilds a surface (R2 = Pattern D migration), the *structural* work can be correct while the *data plumbing* through the structure remains incomplete. Future structural-refactor projects should include an end-to-end "every setting changes some behavior" smoke test as a graduation criterion — this would have caught the R2-inherited dead preferences before they reached production.

### Project-pipeline accuracy

The `/project-pipeline` skill generated R3's 7-task POML decomposition that mapped cleanly to FRs 1–7. All 7 tasks completed in ~6.5h actual vs 7.5h estimated. The agents' inline code-review + adr-check at task-execute Step 9.5 caught issues during execution (not at PR review). This worked well — the final pre-merge code-review at branch level surfaced only architectural follow-ups, not blocking issues.

### Multi-task parallel agent pattern

3 parallel agents in Wave 1 (tasks 001, 010, 020) returned in 5–11 min each, all clean. The dispatch coordination overhead was minimal because the tasks touched completely independent surfaces. **Validation: parallel-safety analysis at task-create time pays off.** When tasks are truly parallel-safe (no shared files, no shared state, no cross-cutting dependencies), the parallel speedup is real.

---

## Specific recommendations for future R-projects on Daily Briefing surface

1. **Include JPS deployment checklist** in any project that adds Spaarke AI primitives. Query the target env's `sprk_analysisaction` rows as a deployment verification step.

2. **Use overflow menu pattern** for any UI that has 3+ per-item actions (semantic-search PCF is the reference). Inline-only is acceptable up to 2 actions per item.

3. **For Microsoft OOB Dataverse fields**, cite Microsoft Learn URL in code comments + spec when writing or reading the field. Both `toasttype` (option-set) and `ttlinseconds` (integer) caused production bugs that a Microsoft-Learn cross-reference would have prevented.

4. **Bundle R2-inherited bugs**: If R2 shipped a structural refactor (Pattern D), the next project on the same surface should include a "verify everything from R2 still works end-to-end" smoke test as graduation criterion. R3 didn't, and we paid for it in UAT.

5. **Treat the playbook engine and BFF AI as parallel surfaces**: the `/narrate` hardcoded BFF endpoint was architecturally dissonant with the rest of Spaarke's playbook-driven AI. Future AI work should default to JPS playbook + Action rows, even when the use case "could just be a direct LLM call."

---

## What R4 inherits from R3

R4 should be aware of:
- **R3 widget service refactor lives in `notificationService.ts`** — R4's W2.4 (wire preferences) will need to integrate with the existing `EXCLUDE_REMOVED_FILTER` and the canonical name conventions (`markBriefingChecked`, not `markNotificationRead` — the transitional alias was removed in R3 task 030).
- **R3 hook `useBriefingActions` has a callback-bag pattern** (`BriefingActionOptions<T>`) — R4 should preserve this when adding more actions or wiring narration refresh.
- **R3 UI in `NarrativeBullet.tsx` has 3 inline action buttons** — R4 W2.5 will move these into an overflow menu. The button order from spec was Check → Remove → Keep → existing Add-to-To-Do → existing Dismiss. R4 needs to preserve that semantic order in the overflow menu.
- **R3 added `ttlinseconds` propagation** (FR-6 follow-up) — `NotificationItem.ttlinseconds` is now plumbed through types → service `NOTIFICATION_SELECT` + `toNotificationItem` → component prop. R4 can rely on this when wiring `dueWithinDays` preferences.
- **The pre-rollout TTL edge case** documented in `notificationService.ts:165-169` and `NarrativeBullet.tsx:286-295` — when a row has no stored TTL and the user clicks Keep, R3 writes a flat 604800 (7d), which may **shorten** TTL for rows on tenant-default 14d. R4 should consider: do we keep this behavior, or branch on null `ttlinseconds` to use a different policy (e.g., write `2 weeks + 1 week`)?

---

## Process improvements going forward

1. **Wave 1 parallel-agent dispatch pattern** validated. Use it on R4 (which has 5+ workstream items per phase that are mostly independent).

2. **JPS deployment verification** — add to task POML knowledge files when introducing new ActionTypes. Don't conflate "C# code shipped" with "deployed JPS data."

3. **Pre-UAT scope alignment** — when a project is narrow-scoped, explicitly tell the UAT user: "Verify only AC-N; don't evaluate the surface holistically." Avoids the UAT-scope-creep that happened with R3 → R4.

4. **Icon collision audits** — when adding UI actions, screenshot the existing surface and the new surface side-by-side before merging. R3 task 031 didn't, and it was an avoidable miss.

---

*Lessons captured 2026-06-25 from R3 execution + UAT + R4 design session.*
