# Current Task State

> **Last Updated**: 2026-06-20 (R2.2 hotfix starting)
> **Recovery**: Read "Quick Recovery" section first
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | R2.2 hotfix — Daily Briefing UX improvements bundle (3 items) |
| **Step** | Starting (no commits yet on R2.2 branch) |
| **Status** | not-started |
| **Next Action** | Item 1: TL;DR prompt change in [`DailyBriefingEndpoints.cs`](../../src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs#L398-L460) — convert 5-7 sentences format to 2-3 sentences + key takeaways bullets |
| **Branch** | `work/spaarke-daily-update-service-r2.2` (forked from `origin/master` @ `3d5c58e4b`) |
| **PR** | Not yet created — draft PR after first commit (per push-to-github convention) |
| **Predecessors** | R2 (PR #397, merged) + R2.1 hotfix (PR #400, merged) — both shipped + redeployed to spaarkedev1 |
| **Successor** | R3 `spaarke-platform-foundations-r3` (PR #404 design.md merged; tasks generation in progress in separate worktree) |

### Scope (3 items)

| # | Item | Effort | Files |
|---|---|---|---|
| 1 | TL;DR as 2-3 sentences + key takeaways bullets (was 5-7 sentences) | ~1h | `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` (prompt change L398-460) |
| 2 | Due date rendered per item in `NarrativeBullet` | ~1h | `src/client/shared/Spaarke.DailyBriefing.Components/src/components/NarrativeBullet.tsx` |
| 3 | `AddToTodo` sets sensible default due date + shows confirmation toast | ~2h | `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts` + UI toast wiring |

### Out of scope for R2.2 (deferred to R3)

- ❌ FetchXML OR interim unblock for `notification-new-documents` / `notification-new-emails` playbooks — **decided 2026-06-20 to wait for R3's `MembershipResolverService`** rather than do throwaway work
- ❌ `??` Handlebars helper fix (R3 Part 3 H1 — `Spaarke.Scheduling` + platform-level)
- ❌ Unrendered `{{` runtime warning (R3 Part 3 H1)

### Files Modified This Session

(none yet — starting fresh on R2.2)

### Critical Context

R2.2 is a small UX-only hotfix bundle for the Daily Briefing widget + standalone code page. It ships independently of R3 (the large platform-foundations project) and supersedes nothing — the 3 items are real UX gaps surfaced during R2 round-2 UAT (2026-06-19) that don't require platform-level work to fix.

All 3 items affect both the SpaarkeAi-embedded widget AND the standalone Daily Briefing code page (`sprk_dailyupdate`) since R2 + R2.1 unified them via Pattern D (`@spaarke/daily-briefing-components`). A single set of changes deploys to both surfaces.

### Deployment notes

R2.2 changes affect:
- **BFF** (item 1): redeploy via `bff-deploy` skill or auto-promotion
- **Both code pages** (items 2 + 3): rebuild + redeploy `sprk_dailyupdate` (Daily Briefing) + `sprk_spaarkeai` (SpaarkeAi workspace) since both consume `@spaarke/daily-briefing-components`

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | R2.2 hotfix bundle (no POML — emerged from R2 round-2 UAT findings 2026-06-19) |
| **Title** | Daily Briefing UX bundle: TL;DR bullet format + per-item due dates + AddToTodo improvements |
| **Phase** | Post-R2.1 (PR #400 already merged) |
| **Status** | not-started |
| **Started** | 2026-06-20 |
| **Branch** | `work/spaarke-daily-update-service-r2.2` (forked from `origin/master` @ `3d5c58e4b`) |
| **Rigor Level** | STANDARD — small UX changes, no architectural impact; quality gates skipped per CLAUDE.md §8 |

### Steps

**Item 1 — TL;DR format**
1. Read current prompt at `DailyBriefingEndpoints.cs:398-460`
2. Modify prompt to ask for: 2-3 sentence executive summary + key takeaways bullets
3. Update response parsing (if needed)
4. Add/update unit test in `Sprk.Bff.Api.Tests`
5. Commit

**Item 2 — Due dates in NarrativeBullet**
1. Read `NarrativeBullet.tsx`
2. Add due-date rendering (data is already in `appnotification.data` payload — `dueDate` field per item)
3. Style consistent with Fluent v9 + existing card design
4. Add snapshot test
5. Commit

**Item 3 — AddToTodo improvements**
1. Read `useInlineTodoCreate.ts:159-279`
2. Add default due-date setting (proposed: 3 days from now, or use the bullet's own dueDate if available)
3. Add Fluent v9 toast on success (via `useToastController`)
4. Add toast on failure (already has error tooltip, but toast is more discoverable)
5. Update existing test in `ActivityNotesSection.subList.test.tsx` or add new
6. Commit

### Step Progress
- ⏳ Item 1 — not started
- ⏳ Item 2 — not started
- ⏳ Item 3 — not started

### Decisions

- **2026-06-20**: Skip FetchXML interim unblock (item 4 from original scope). Reason: R3's `MembershipResolverService` supersedes it; ~3-4h of throwaway work for ≤3 weeks of broken "My Documents" channel doesn't pencil out.
- **2026-06-20**: Branch named `work/spaarke-daily-update-service-r2.2` (versioned, distinguishes from merged R2.1 hotfix branch).

### Handoff Notes

If session ends mid-flight, the next session can resume by:
1. Reading this current-task.md Quick Recovery section
2. Running `git status` to see what's modified
3. Checking the most recent commit message to know which Item was last touched
4. Resuming the next un-checked step in the Steps section above

---

*Updated 2026-06-20 — R2.2 starting.*
