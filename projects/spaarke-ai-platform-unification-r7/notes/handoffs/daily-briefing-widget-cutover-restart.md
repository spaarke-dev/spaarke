# RESTART DOC — Daily Briefing Widget Cutover (post-/compact)

> **Created**: 2026-06-30 (pre-/compact handoff)
> **Mission**: Complete the Daily Briefing widget cutover so `/render` drives EVERYTHING. Remove all `appNotifications` dependencies from the widget. Deploy. Operator UAT.
> **Branch**: `work/spaarke-ai-platform-unification-r7`
> **Worktree**: `c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7/`
> **Working in**: main session (NOT a sub-agent — keep operator in the loop on tricky refactor decisions)
> **Estimated effort**: 4-6 hours wall-clock (straight-shot: audit-then-implement together; no formal POML)

---

## ⚠️ READ THIS FIRST — what NOT to do

1. **DO NOT touch the `summarize` endpoint** — operator deferred to a future session
2. **DO NOT dispatch sub-agents for this work** — main session keeps operator in the loop
3. **DO NOT skip the audit step** — last time we shipped a "POC validated" claim without verifying widget→/render end-to-end; resulting half-cutover is the bug we're now fixing
4. **DO NOT deploy until smoke confirms `/render` data renders in the widget** — the entire failure mode last time was "I assumed it worked"
5. **DO NOT touch other Wave 12 wrap-up tasks** unless they directly block widget cutover

---

## 1. THE PROBLEM (root cause of empty Daily Briefing widget)

### What the architecture doc claimed

`docs` says: widget → `POST /api/ai/daily-briefing/render` → BFF queries Dataverse directly → returns 6 channels → widget renders. This is the POC pivot intent.

### What the widget actually does today

Two pipelines run in parallel:

| Pipeline | Source | Drives | Status |
|---|---|---|---|
| **A — LEGACY** | `useNotificationData` hook → queries `appnotification` table | Channel structure + `totalUnreadCount` + **early-exit "all caught up" gate** | Still active; gates display |
| **B — NEW (W11 T118)** | `fetchBriefingLive()` → POST `/render` | Narrative content (overlaid on Pipeline A's UI) | Active but blocked by Pipeline A's gate |

### Where the early-exit fires

[`DailyBriefingApp.tsx:602-616`](../../../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx#L602-L616)

```typescript
// All caught up — no unread notifications at all
if (totalUnreadCount === 0 && channels.every(ch => ch.status === 'success') && !narrationLoading) {
  return (
    <div className={styles.container}>
      <DigestHeader ... />
      <div className={styles.scrollContent}>
        <EmptyState />
      </div>
      <Toaster toasterId={toasterId} position="bottom-end" />
    </div>
  );
}
```

`channels` comes from `useNotificationData` (legacy). When the operator's appnotification table has 0 unreads (because the scheduler hasn't run, notifications were dismissed, or the legacy path is broken), this early-exit fires and **EmptyState renders regardless of what `/render` returned**.

### Why this slipped past validation in W11 T118

Original POC validation worked because the operator HAD unread appnotifications at that time. Pipeline A was producing channels with items; Pipeline B was overlaying narrative on top. It LOOKED like `/render` was driving everything. The claim of "validated end-to-end" was conflating "POC pipeline exists at BFF layer" with "feature works end-to-end at widget layer". This restart fixes that.

---

## 2. THE TARGET STATE (what "fully working" means)

The widget renders **exclusively** from `/render` response. Specifically:

- Widget calls `POST /api/ai/daily-briefing/render`
- `/render` returns `DailyBriefingNarrateResponse` with 6 channels of real records via DailyBriefingCollector
- Widget displays: TLDR (from response.tldr), 6 channel sections (from response.channelNarratives), per-bullet entity links (from response.channelNarratives[].bullets), 'Add To Do' tool
- Empty state fires ONLY when `/render` returns 0 items across all 6 channels (legitimately no records)
- NO `useNotificationData` hook in the widget data path
- NO `totalUnreadCount` derived from `appnotification`
- NO legacy channel pipeline

The 6 channel codes (from T131 collector + T133 registry):
`upcoming-tasks`, `overdue-tasks`, `documents`, `matters`, `projects`, `to-dos`

---

## 3. APPROACH (straight-shot, main session)

**No formal POML.** Just do the work. Track via TodoWrite. Two phases combined:

### Phase A — Audit (~30-45 min)

Map every appNotification dependency + understand /render response shape. Goal: know EXACTLY what to keep, what to remove, what to refactor BEFORE editing.

### Phase B — Implement (~3-4 hours)

Refactor data layer, outer rendering logic, child components, tests. Build. Deploy. Operator UAT.

---

## 4. AUDIT ITEMS (Phase A checklist)

### 4.1 Map all appnotification dependencies in widget

Files to grep + read:
- `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useNotificationData.ts` — primary loader; what does it return? Does it call Dataverse `appnotification` table directly via `Xrm.WebApi`? Or via service?
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx` — primary consumer; identify EVERY use of channels/totalUnreadCount/narrationLoading/dismiss handlers
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/*` — all 12+ child components; grep for "appnotification", "useNotificationData", "ChannelFetchResult", "totalUnreadCount", "channels.every", "channels.map", "channels.filter"
- `src/client/shared/Spaarke.DailyBriefing.Components/src/services/briefingService.ts` — confirm `USE_LIVE_RENDER = true` still set; `fetchBriefingLive` still works
- `src/client/shared/Spaarke.DailyBriefing.Components/src/types/notifications.ts` — type definitions used across the data path
- `src/client/shared/Spaarke.DailyBriefing.Components/src/types/briefing.ts` (if exists) — render response types

Sweep command (run early in audit):
```bash
cd c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7
grep -rn "appnotification\|useNotificationData\|ChannelFetchResult\|totalUnreadCount\|useBriefing" src/client/shared/Spaarke.DailyBriefing.Components/src/
```

### 4.2 Confirm `/render` response shape

The widget will consume this. What does `/render` actually return?

- Check `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` — find the `HandleRender` method (or similar; check method names)
- Check `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingNarrator.cs` — the response shape produced (`DailyBriefingNarrateResponse`)
- Check `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs` — the 6-channel `BriefingItem[]` projection
- Check shared lib types: what TS interface mirrors this on the client?

Key questions:
- Does `/render` return enough data for the widget to render WITHOUT needing appnotification supplementation? (Items, channel labels, entity links?)
- Are there fields the legacy path produced that `/render` doesn't? (Read/unread state? per-channel order? per-item dismiss state?)
- If yes — those features either go away (acceptable in cutover) OR `/render` response needs extending

### 4.3 Side-effects audit (decisions needed)

Legacy code path has side effects:
- `onChannelDismiss` / `onDismissAll` handlers in DailyBriefingApp.tsx may write `sprk_briefingstate` PATCH back to Dataverse
- Mark-as-read flow
- Refresh button → re-loads from appnotification

Decisions:
- **DROP** dismiss/mark-read entirely (acceptable per architecture intent — /render queries are always fresh; no "state" to persist)
- OR **KEEP** dismiss as a UX nicety that writes to /render-side state (would require new BFF endpoint — out of scope)

Recommend: **DROP dismiss + mark-read for MVP**. Refresh button calls `/render` again. Operator can confirm acceptable.

### 4.4 Other consumers of useNotificationData (outside DailyBriefing.Components)

Grep wider scope:
```bash
grep -rn "useNotificationData\|@spaarke/daily-briefing-components" src/
```

If used elsewhere (e.g., LegalWorkspace embeds the widget), make sure the cutover doesn't break their integration. Most likely the widget is consumed by SpaarkeAi (the workspace).

### 4.5 Components to refactor

These need updating to consume new shape (`DailyBriefingNarrateResponse` instead of `ChannelFetchResult[]`):
- `DailyBriefingApp.tsx` (primary)
- `DigestHeader.tsx`
- `ChannelSection.tsx` (or similar — what renders each channel)
- `ActivityNotesSection.tsx`
- `EmptyState.tsx` (mostly OK — just verify the trigger condition is right)
- `CaughtUpFooter.tsx`
- `NarrativeBullet.tsx` (T134's Add To Do checkmark; already updated; verify still works with new shape)
- `TldrSection.tsx`

Components to potentially remove:
- `useNotificationData.ts` (if no other consumer)
- Any types specific to legacy ChannelFetchResult that aren't needed

---

## 5. IMPLEMENTATION (Phase B steps)

### 5.1 Refactor data layer

Create new hook: `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingRender.ts`

Shape (rough):
```typescript
interface BriefingRenderState {
  status: 'idle' | 'loading' | 'success' | 'error';
  data: DailyBriefingNarrateResponse | null;
  error: string | null;
  refetch: () => Promise<void>;
  lastFetchedAt: Date | null;
}

export function useBriefingRender(): BriefingRenderState {
  // Single call to /render via authenticatedFetch
  // Loading state, error state, refetch capability
}
```

Replaces `useNotificationData` entirely. No appnotification queries.

### 5.2 Refactor DailyBriefingApp.tsx outer logic

Remove:
- `useNotificationData` import + call
- `totalUnreadCount` calculation
- `channels` derivation
- Legacy early-exit at line 602-616
- `narrationLoading` (now just `useBriefingRender.status === 'loading'`)
- Dismiss handlers (per §4.3 decision)
- Mark-read handlers

Add:
- `useBriefingRender` call
- New empty-state condition: `data === null OR data.channelNarratives.every(c => c.bullets.length === 0)`
- New refresh handler: calls `refetch()` from `useBriefingRender`

### 5.3 Update child components

For each component in §4.5, replace `ChannelFetchResult[]` props with the equivalent shape from `DailyBriefingNarrateResponse`. Type-driven refactor.

### 5.4 Update tests

- `DailyBriefingApp.test.tsx` — replace `useNotificationData` mocks with `useBriefingRender` mocks
- Per-component tests — update prop shapes
- Smoke test: empty `/render` response → EmptyState renders; populated `/render` response → 6 channels render with bullets

### 5.5 Build + Deploy

- `cd src/client/shared/Spaarke.DailyBriefing.Components && npm run test`
- `cd src/solutions/SpaarkeAi && npm run build` (the SpaarkeAi widget bundles DailyBriefing.Components)
- Deploy via `scripts/Deploy-SpaarkeAi.ps1` to spaarkedev1

### 5.6 Smoke test BEFORE handing off for operator UAT

This is the step skipped last time. Don't skip it again.

- Open spaarkedev1 SpaarkeAi workspace in browser
- Open Daily Briefing widget
- Verify: does the widget render channels with actual data? OR show EmptyState (legitimate empty)? OR error?
- Check browser dev tools network tab: was `/render` called? What did it return?
- If `/render` returned 6 channels but widget shows empty → bug in widget refactor; investigate
- If `/render` returned empty channels → underlying data issue (T130 / T131 / membership resolver); investigate separately (likely the T130 secondary risk: `contact.azureactivedirectoryobjectid` field missing for some users)
- If `/render` returned 401 → token plumbing issue
- If `/render` returned 500 → BFF exception; check App Insights

### 5.7 Operator UAT

After smoke shows widget rendering with real data, request operator validates:
- All 6 channels render with their actual records
- TLDR appears + matches Activity Notes content
- Per-bullet entity links are clickable + navigate correctly
- 'Add To Do' checkmark works
- Empty state shows ONLY when no records exist
- Refresh button works

---

## 6. CURRENT GIT STATE (as of pre-/compact handoff)

```
Branch:  work/spaarke-ai-platform-unification-r7
HEAD:    cc706614 (Wave 12 Batch 4 deploy handoff + TASK-INDEX updates)
Origin:  in sync (no unpushed local commits)
PR #520: OPEN, MERGEABLE, mergeStateStatus=UNSTABLE (CI in_progress on cc706614)
Tag:     deploy/spaarkedev1/pre-wave12-batch4 (rollback target; commit 4fc73ae4a)
```

Recent commits (top of log):
```
cc7066145  docs(r7): Wave 12 Batch 4 — combined T136+T154 deploy + agent-smoke complete (pending operator UAT)
37ef38c2f  ci(tier1): add Redis__AllowInMemoryFallback for integration tests (second redis-r2 validation)
ef4b0ebcb  feat(bff/r7): T135 Wave 12 — orphan-fallback for per-bullet entity links (6 entity types)
a94cdad1b  docs(r7): Wave 12.3 T145 — wizard UAT signoff doc + audit closure (operator-pending)
20bad1793  fix(dataverse/r7): Wave 12 T124-FIX-A applied — Document Summary node executortype 0→40
... (100 commits total in PR #520)
```

---

## 7. WHAT'S DONE THIS SESSION (context for restart)

Wave 12 work completed across 4 batches + main-session fixes:

### Code changes deployed
- T130 IMembershipResolverService fix (canonical defaults via post-configure)
- T131 DailyBriefingCollector 6-entity extension + resolver consumption
- T132 TLDR↔Notes chaining in narrator
- T133 Widget CHANNEL_REGISTRY 6 entries
- T134 Widget 'Add To Do' checkmark
- T135 Entity-link Tier 2 orphan fallback
- T150 EntityType normalization at ChatHostContext boundary
- T151 Server-side EntityName lazy-fetch in PlaybookChatContextProvider
- T152 Default PageType in PlaybookChatContextProvider
- T153 Gaps D-H disposition (DEF-002 + 4 no-action documented)

### Dataverse fixes applied via MCP
- T141 DELETE Save Profile node `c9334fb7-...` + repair Update Record fieldMappings
- T142 PATCH Project Wizard FK `sprk_actionid` → ACT-024
- T143 DELETE EntityNameValidator node `c3c5226d-...` + strip systemPrompt from AI Analysis `444b06d3-...`
- T124-FIX-A PATCH Document Summary node `e514cfab-...` `sprk_executortype` 0→40

### Other infrastructure
- T140 App Service env var `Workspace__SummarizePlaybookId` set
- BRIEF-NARRATE-CHANNEL Action prompt amended (T132)
- 2 CI workarounds: `APPLICATIONINSIGHTS_CONNECTION_STRING` (`fd657e0b2`) + `Redis__AllowInMemoryFallback` (`37ef38c2f`)
- BFF + SpaarkeAi widget deployed to spaarkedev1 (Wave 12 Batch 4)

### 5 audit docs landed
- `notes/audits/wave12-120-assistant-workspace.md`
- `notes/audits/wave12-121-wizard-file-summary.md`
- `notes/audits/wave12-122-document-create-profile.md`
- `notes/audits/wave12-123-three-prefill-wizards.md`
- `notes/audits/wave12-124-wave5-backfill-health-sweep.md`

### UAT handoff doc
- `notes/handoffs/wave12-3-uat-signoff.md` — wizard UAT checklist
- `notes/handoffs/wave12-batch4-deploy-smoke.md` — Daily Briefing + Assistant↔Workspace UAT checklist

---

## 8. OUT OF SCOPE FOR THIS SESSION (do NOT touch)

- **Summarize endpoint** — operator deferred ("can't find playbook" error; operator wants this in a future session)
- **5 wizards UAT** — operator will test independently; Dataverse fixes are live
- **Assistant↔Workspace UAT** — operator can test independently; BFF fixes are live
- **PR #520 merge** — let CI complete; merge separately when green
- **Wave 12.5 wrap-up** — lessons-learned + README → Complete; happens AFTER widget cutover is done + UAT pass
- **R7 remaining tasks** — W5 T056, W6 T063/T068/T069, W7 T070-T075, W8 T087/T089/T089d, W11 T119, W10 T101 — all parked until Daily Briefing widget is done
- **spaarkeai-compose-r1 coordination** — happens AFTER PR #520 merges
- **DEF-NNN filings** for 7 Wave 5 backfill follow-ups — Wave 12.5 wrap-up territory
- **ISS-NNN for redis-r2** — defer to wrap-up

---

## 9. POST-COMPACT FIRST ACTIONS (in order)

1. Read THIS DOC (`daily-briefing-widget-cutover-restart.md`)
2. Read `current-task.md` (will point back here)
3. Verify worktree state:
   ```bash
   cd c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7
   git status --short        # Should be clean (only .husky untracked)
   git log --oneline -5      # Confirm HEAD is cc706614 or later
   gh pr view 520 --json mergeStateStatus,mergeable | head -5
   ```
4. **(PARALLEL TRACK — operator-raised concern)** Investigate PR #520 CI failure (see §9.5 below)
5. Set up TodoWrite with the 8 implementation steps from §5 of this doc
6. Begin Phase A audit (§4) — start with §4.1 grep sweep
7. Iterate through Phase A items
8. Move to Phase B implementation (§5)
9. Smoke before any deploy (§5.6) — DO NOT SKIP
10. Operator UAT after smoke green (§5.7)

## 9.5 PR #520 CI failure — investigation needed post-/compact

**Status pre-/compact**: latest commit `cc706614` CI failing. NOT the redis-r2 issue anymore (both CI workarounds — APPLICATIONINSIGHTS_CONNECTION_STRING in `fd657e0b2` + Redis__AllowInMemoryFallback in `37ef38c2f` — are working). NEW failure mode surfaced once those passed.

**Error**:
```
System.InvalidOperationException : Failure to infer one or more parameters.
   at System.Reflection.MethodBaseInvoker.InvokeWithOneArg...
```

**Affected**: ALL integration tests in `Spe.Integration.Tests.Api.Ai.*` (ChatEndpointsTests, KnowledgeBaseEndpointsTests, ReAnalysisFlowTests, etc. — same set as before; new failure cause)

**Likely diagnosis**:
- This is ASP.NET Core minimal API parameter binding failing
- An endpoint handler has a parameter that .NET can't bind from request/services
- Most likely cause: a Wave 12 code change added a parameter type that's NOT registered in the test host's DI container (or in main DI but not registered the way test host expects)
- Candidates from Wave 12 work:
  - T130 added `IPostConfigureOptions<MembershipOptions>` — might affect MembershipOptionsDefaults wiring
  - T131 may have added new service injection into endpoint
  - T150 added optional `IGenericEntityService?` ctor param to PlaybookChatContextProvider
  - T151 may have added new helper

**Investigation steps**:
1. Get full failed test output from CI run `28476664311` (latest fail; commit `cc706614`)
2. Identify which test class first fails (likely WebApplicationFactory startup is OK; first endpoint call fails on bind)
3. Read the endpoint handler signatures for failing tests
4. Diff parameters against pre-Wave-12 (master) signatures
5. Identify the unregistered service/parameter
6. Either register it in test fixture OR adjust the parameter binding

**Fix expectation**: 1-3 line change to a test fixture or endpoint signature. Quick.

**Do not block widget cutover on this** — they're independent. Widget cutover work doesn't need PR #520 to be merged. PR #520 merge unblocks the spaarkeai-compose-r1 owner; widget cutover unblocks the operator's Daily Briefing.

---

## 10. REGRESSION RISK + SAFETY

**The thing that went wrong before**: shipped a "POC validated" claim without verifying widget → /render end-to-end. Half-cutover discovered weeks later in UAT.

**To avoid recurrence this time**:
- Phase B step 5.6 (smoke BEFORE handing off for UAT) is non-negotiable
- Verify in browser dev tools: `/render` is called + returns data + widget displays it
- If `/render` returns empty data → investigate underlying T130/T131 issue (don't just deploy and hope)
- Tag rollback target BEFORE deploying (use git tag like `deploy/spaarkedev1/pre-widget-cutover`)
- If widget still shows empty after cutover + `/render` returns data → widget bug; iterate before declaring done

**Other risk**: T130 noted a secondary risk that `contact.azureactivedirectoryobjectid` may be absent in spaarkedev1 → identity normalization may need different cross-ref. If smoke shows /render returns empty for a user who SHOULD have records, this is the suspect.

---

## 11. REFERENCES (KEY DOCS)

| Topic | Path |
|---|---|
| Architecture comparison (POC vs Playbook Engine) | `projects/spaarke-ai-platform-unification-r7/notes/spikes/poc-vs-playbook-engine-architecture.md` |
| Wave 12 plan + scope | `projects/spaarke-ai-platform-unification-r7/notes/wave12-mvp-completion-plan.md` |
| Daily Briefing audit (Wave 12 entry-point) | none yet — start auditing per §4 of this doc |
| Daily Briefing BFF endpoint | `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` |
| Daily Briefing narrator | `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingNarrator.cs` |
| Daily Briefing collector | `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs` |
| Widget code | `src/client/shared/Spaarke.DailyBriefing.Components/src/` |
| Deploy scripts | `scripts/Deploy-BffApi.ps1`, `scripts/Deploy-SpaarkeAi.ps1` |
| App Service | `spaarke-bff-dev` in `rg-spaarke-dev` |
| BFF base URL | `https://spaarke-bff-dev.azurewebsites.net` |
| PR #520 | https://github.com/spaarke-dev/spaarke/pull/520 |

---

## 12. DECISIONS LEDGER (this session)

| Decision | Rationale |
|---|---|
| Straight-shot main session (not sub-agent) | Operator wants to be in loop on tricky decisions; refactor scope warrants close monitoring |
| Skip formal POML | Operator approved; scope small + need to move quickly |
| Audit + implement together (not separate POML tasks) | Faster + reduces handoff overhead |
| Drop dismiss + mark-read for MVP | Acceptable per architecture intent; can re-add as enhancement if operator wants |
| Don't touch summarize endpoint | Operator deferred |
| Don't touch other Wave 12 wrap-up | Focus on Daily Briefing today |

---

## 13. APPENDIX — FOUND ISSUES TO TRACK (file as DEF-NNN later)

These are NOT in scope but should be filed for future tracking:

- T130 secondary risk: `contact.azureactivedirectoryobjectid` absent in spaarkedev1 (identity normalization fallback needed)
- Document Summary `compose-summarize` consumer routing row exists but has no BFF code references (orphan; T124 §11)
- 7 abandoned playbook nodes in §8 of `wave12-124-wave5-backfill-health-sweep.md`
- Summarize-document playbook lookup may have a separate config issue (operator deferred)
- redis-r2 startup validations should be relaxed for Testing env so CI workarounds can be removed (`fd657e0b2` + `37ef38c2f`)

---

*End of restart doc. Created 2026-06-30 pre-/compact. Read this FIRST after /compact.*
