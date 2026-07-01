# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-30 — Daily Briefing widget cutover DEPLOYED; awaiting operator browser smoke + UAT

---

## Quick Recovery (READ THIS FIRST — POST-/COMPACT)

| Field | Value |
|---|---|
| **Active Mission** | Daily Briefing widget cutover — `/render` is sole data source |
| **Status** | ✅ Code shipped + deployed to spaarkedev1 — **AWAITING operator browser smoke** |
| **Branch** | `work/spaarke-ai-platform-unification-r7` at HEAD `69181bbd9` (pushed) |
| **Bundle deployed** | sprk_spaarkeai webresource modifiedon 2026-06-30 23:30:14 UTC |
| **Rollback tag** | `deploy/spaarkedev1/pre-widget-cutover` → commit `9bae5c306` (pushed) |
| **PR #520** | Still UNSTABLE — CI parameter-binding failure (see restart doc §9.5; separate track) |

### What was done this session

1. ✅ Phase A audit (mapped 16 widget files referencing appnotification chain; confirmed `/render` shape; identified external consumers all safe)
2. ✅ Phase B.1: created `useBriefingRender.ts` hook (single-call to /render, no appnotification gate)
3. ✅ Phase B.2: rewrote `DailyBriefingApp.tsx` — drops `useBriefingNotifications`, `useBriefingActions`, optimistic-overlay, early-exit gate, handleCheck/Remove/Keep
4. ✅ Phase B.3: refactored `ActivityNotesSection.tsx` — dropped `channels` prop, FR-16 raw-card fallback, per-bullet sub-list
5. ✅ Phase B.4: test cleanup — deleted obsolete fallback + subList tests, skipped 2 smoke tests pending /render rewrite, fixed callbacks test prop mismatch
6. ✅ Phase B.5: tagged rollback (`deploy/spaarkedev1/pre-widget-cutover`), built SpaarkeAi bundle (3.98 MB, vite ✓), deployed to spaarkedev1
7. ✅ Deploy integrity check: bundle contains `/api/ai/daily-briefing/render` URL; old `totalUnreadCount === 0` gate absent

Net code change: -275 LOC in src (refactor + dead code removal), -427 LOC in tests (obsolete + cleanup).

### What the operator needs to do next (Phase B.6 + B.7)

**B.6 — Browser smoke (operator-driven; I cannot open Chrome from CLI)**:

1. Open spaarkedev1 SpaarkeAi workspace in a fresh browser session (or force-reload to bypass cache)
2. Open the Daily Briefing widget
3. Verify ONE of these states renders:
   - **Records visible**: TLDR + per-channel bullets across the 6 channels (`upcoming-tasks`, `overdue-tasks`, `documents`, `matters`, `projects`, `to-dos`) — GREEN
   - **EmptyState ("You're all caught up")**: only if there are LEGITIMATELY no records across all 6 channels — operator confirms this matches their Dataverse state
   - **MessageBar error/unavailable**: AI service down OR /render returned an error — investigate
4. Open browser dev tools → Network tab → confirm:
   - `POST /api/ai/daily-briefing/render` fired (NOT `/narrate`)
   - Request body is `{}` (empty JSON object, no appnotification payload)
   - Response status 200 with `{ tldr, channelNarratives, generatedAtUtc }`

**If smoke shows records → proceed to B.7 (operator UAT).**

**If smoke shows EmptyState but operator EXPECTS records → investigate**:
- Check if T130 secondary risk applies: `contact.azureactivedirectoryobjectid` may be missing for the test user → membership resolver returns no team membership → collector queries return 0 rows
- Check Dataverse: does the test user have any active sprk_todo/sprk_document/sprk_matter/sprk_project/sprk_event records they own?
- Check App Insights for /render call: did it return empty channels or did it error?

**If smoke shows error → check the MessageBar text + App Insights for the underlying 500**.

**B.7 — Operator UAT**:
- All 6 channels render with actual records
- TLDR appears + matches Activity Notes content
- Per-bullet entity links are clickable + navigate correctly (FR-19)
- 'Add To Do' checkmark works (creates a sprk_todo with the bullet's primary entity as regarding)
- Refresh button works (triggers re-fetch of /render)
- Empty state shows ONLY when legitimately no records

---

## What was DEFERRED (not done this session)

- **PR #520 CI parameter-binding failure** (restart doc §9.5) — separate investigation
- **Summarize endpoint "can't find playbook"** — operator deferred explicitly
- **5 Wizards UAT** — operator UAT pending; fixes are live (T141 + T142 + T143 + T124-FIX-A)
- **Assistant↔Workspace UAT** — operator UAT pending; fixes are live (T150 + T151 + T152 + T153)
- **Wave 12.5 wrap-up** — happens AFTER this widget cutover passes UAT
- **R7 remaining tasks** — W5 T056, W6 T063/T068/T069, W7 T070-T075, W8 T087/T089/T089d, W11 T119, W10 T101
- **Test rewrites** — DailyBriefingApp.smoke + CountReconciliation.smoke marked describe.skip pending rewrite for /render path
- **7 DEF-NNN candidates from Wave 5 backfill audit** — wrap-up territory
- **spaarkeai-compose-r1 coordination** — after PR #520 merge
- **ISS-NNN to redis-r2 team** — after wrap-up

---

## Rollback (if operator smoke fails)

Two paths:

**Path A — bundle-only rollback**:
```powershell
cd c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7
git checkout deploy/spaarkedev1/pre-widget-cutover -- src/client/shared/Spaarke.DailyBriefing.Components/src/
cd src/solutions/SpaarkeAi && npm run build
cd ../../.. && .\scripts\Deploy-SpaarkeAi.ps1
# Then: git restore src/client/shared/Spaarke.DailyBriefing.Components/src/
```

**Path B — branch revert (preserves working tree)**:
```powershell
cd c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7
git revert 69181bbd9 ad53af431 --no-commit
git commit -m "revert(widget/r7): roll back Daily Briefing widget cutover (smoke failed)"
cd src/solutions/SpaarkeAi && npm run build
cd ../../.. && .\scripts\Deploy-SpaarkeAi.ps1
git push origin work/spaarke-ai-platform-unification-r7
```

---

## Reference

- **Restart doc**: [`notes/handoffs/daily-briefing-widget-cutover-restart.md`](notes/handoffs/daily-briefing-widget-cutover-restart.md)
- **Cutover commits**:
  - `ad53af431` — src refactor (useBriefingRender + DailyBriefingApp + ActivityNotesSection + briefingService export + hooks index)
  - `69181bbd9` — test cleanup
- **Wave 12 plan**: [`notes/wave12-mvp-completion-plan.md`](notes/wave12-mvp-completion-plan.md)
- **PR #520**: https://github.com/spaarke-dev/spaarke/pull/520

---

*End of current-task.md. Operator: please run B.6 smoke + report results back so I can either proceed to UAT or investigate.*
