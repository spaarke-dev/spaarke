# R3 Smoke Test Playbook (Operator-Driven)

> **Date**: 2026-05-20
> **Deploy under test**: commit `2e9c4105` (BFF) + `2e9c4105` (SpaarkeAi) — see [`notes/deploys/2026-05-20-deploy.md`](deploys/2026-05-20-deploy.md)
> **Target environment**: DEV — `https://spaarkedev1.crm.dynamics.com`
> **BFF base URL**: `https://spe-api-dev-67e2xz.azurewebsites.net`

This playbook is YOUR (operator) smoke checklist for Wave 7 tasks 071-074. For each task, follow the checks in order. Record PASS / FAIL / NOTES per item in the linked memo. When all items in a task pass (or you've accepted any failures), tell me and I'll mark the task ✅ in TASK-INDEX.

---

## Access SpaarkeAi

**Primary path** (Power Apps model-driven app):
1. Navigate to `https://spaarkedev1.crm.dynamics.com`
2. Open the Spaarke model-driven app that hosts SpaarkeAi as a custom page
3. Click the SpaarkeAi sitemap entry (or whatever opens the welcome state)

**Direct path** (if you know the app ID):
```
https://spaarkedev1.crm.dynamics.com/main.aspx?pagetype=custom&name=sprk_spaarkeai&appid={your-app-id}
```

**Hard refresh after first load** to ensure the deployed bundle replaces any cached version: Ctrl+Shift+R (Win) / Cmd+Shift+R (Mac).

---

## Task 071 — Assistant pane smoke (FR-02..FR-09)

**Record results in**: `projects/spaarke-ai-platform-unification-r3/notes/smoke-assistant.md`

### Checks

| # | Check | FR | Expected |
|---|---|---|---|
| 1 | Pane header visible | FR-02 | `<PaneHeader>` with brand-color `ChatRegular` icon + title "Assistant"; NO `Chat | History` tab buttons remain |
| 2 | History button in right-slot | FR-03 | `HistoryRegular` icon button visible in PaneHeader right side |
| 3 | History overlay opens on click | FR-03 | Click history button → right-side overlay slides in <200ms; shows up to 50 recent sessions |
| 4 | Session selection swaps conversation | FR-03 | Click a session in overlay → conversation pane updates to that session; overlay closes |
| 5 | Welcome chrome trimmed | FR-04 | NO sparkle icon, NO "Welcome to Spaarke AI" heading, NO 4 hardcoded prompt cards. ONLY "How can I help you today?" remains |
| 6 | "How can I help you today?" sized correctly | FR-04 | Size 400; no leading icon |
| 7 | "Recent Conversations" present | FR-05 | Section still renders below the prompt with last 5 sessions |
| 8 | Input editable on cold load | FR-06 | Refresh page → input is clickable + typeable BEFORE any session exists |
| 9 | First send creates session | FR-06 | Type "Hello" + send → BFF creates new session; reply streams normally |
| 10 | `+` (attach) button visible | FR-09 | `AttachRegular` icon button in horizontal strip ABOVE input, alongside prompt menu |
| 11 | "Open in Word" absent | FR-08 | NO `SprkChatExportWord` button anywhere in toolbar |
| 12 | Single attachment | FR-07 | Click `+` → file picker opens → select 1 small `.txt` file → chip appears with filename + status visual + `×` button |
| 13 | Multi-file attach (≤5) | FR-07 | Add 3 more `.txt` files → 4 chips total, all in order |
| 14 | 6th file rejected | NFR-04 | Try to add a 6th file → UI rejects with message; only 5 chips remain |
| 15 | Oversized file rejected | NFR-04 | Try a >10 MB file → rejected with size message |
| 16 | Disallowed MIME rejected | NFR-04 | Try a `.png` or `.zip` → rejected with MIME message |
| 17 | × removes chip | FR-07 | Click `×` on a chip → removes that file from staging |
| 18 | End-to-end AI sees attachments | FR-07 | Attach 2 short `.txt` files with distinct content (e.g., "apple" and "banana"); type "summarize the attached files" → reply references BOTH "apple" and "banana" |
| 19 | Chips clear on success | FR-07 | After successful send, attachment chips disappear |
| 20 | Chips persist on error | FR-07 | Simulate error (e.g., disconnect network mid-send) → chips remain so user can retry |
| 21 | Dark mode parity | NFR-06 / ADR-021 | Toggle dark mode in browser/Power Apps → all colors adapt; no hardcoded white/black |

### Pass criteria
All 21 checks PASS or accepted-with-note. FR-07 end-to-end (#18) is the headline acceptance — if it fails, do NOT mark 071 ✅.

---

## Task 072 — Workspace pane smoke (FR-10..FR-16, NFR-09)

**Record results in**: `projects/spaarke-ai-platform-unification-r3/notes/smoke-workspace.md`

### Checks

| # | Check | FR/NFR | Expected |
|---|---|---|---|
| 1 | Pane header visible | FR-10 | `<PaneHeader>` with brand-color `AppsListRegular` icon + title "Workspace"; `WorkspacePaneMenu` button in right-slot |
| 2 | Home tab embeds LegalWorkspace | FR-11 | Cold load → Home tab content shows LegalWorkspace sections from your default layout (`GET /api/workspace/layouts/default`) |
| 3 | WorkspaceLandingWidget gone | FR-11 | No "old" landing widget visible; Home is the only initial tab |
| 4 | WorkspacePaneMenu sections | FR-12 | Click menu → 4 sections with dividers: **Open** / **Home** / **Switch Workspace** / **Edit current workspace** |
| 5 | × close on non-Home only | FR-12, FR-13 | `×` close button visible on every non-Home item; NO × on Home |
| 6 | + New Workspace launches wizard | FR-12, FR-14 | Click "+ New Workspace" → WorkspaceLayoutWizard opens; **only 6 templates shown** (2-col-equal, 3-row-mixed, hero-2x2, sidebar-main, single-column, single-column-5) |
| 7 | MAX_TABS = 8 + FIFO | FR-13 | From Context pane, open 9 different wizards in succession → 9th eviction removes oldest non-Home; Home preserved; total = 9 (1 Home + 8 non-Home) |
| 8 | Daily Briefing section renders | FR-15 | Use WorkspaceLayoutWizard to create a layout including "Daily Briefing" section → switch to it → bullets render from `POST /api/ai/daily-briefing/narrate` |
| 9 | Daily Briefing 429 graceful | NFR-11, FR-24 | Force a 429 (e.g., rapid tab switching) → degraded card with retry CTA; section stays visible; App Insights `spaarke-ai-error.daily-briefing.rate-limited` event fires |
| 10 | Daily Briefing empty state | FR-16, OC-08 | When endpoint returns empty → "Nothing to see right now — enjoy your day" + sparkle icon; widget stays visible |
| 11 | **NFR-09 tab persistence** | NFR-09 | Open 3 non-Home tabs (Create Matter, Find Similar, Send Email). Refresh page (Ctrl+Shift+R). All 3 tabs restored, same order, active selection preserved. **THIS IS THE TASK 065 ACCEPTANCE TEST.** |
| 12 | Dark mode parity | NFR-06 / ADR-021 | Toggle dark mode → all colors adapt |
| 13 | Standalone LegalWorkspace untouched | FR-25, NFR-10 | Open standalone LegalWorkspace in separate tab → still has 9 templates in its wizard; existing layouts unchanged |

### Pass criteria
All 13 checks PASS. Check #11 (NFR-09 tab persistence) is the headline acceptance — if it fails, do NOT mark 072 ✅. Re-verify task 065 implementation if needed.

---

## Task 073 — Context pane smoke (FR-17..FR-22)

**Record results in**: `projects/spaarke-ai-platform-unification-r3/notes/smoke-context.md`

### Checks

| # | Check | FR | Expected |
|---|---|---|---|
| 1 | Pane header visible | FR-17 | `<PaneHeader>` with brand-color `DocumentRegular` icon + title "Context"; stage label visible in right-slot |
| 2 | Stage label "Get Started" on welcome | FR-22 | Right-side stage label reads "Get Started" (NOT "Gallery") |
| 3 | 7 GetStarted cards visible | FR-18 | 2-column scrollable grid of 7 cards: Create Matter / Create Project / Assign Work / Summarize Files / Find Similar / Send Email / Schedule Meeting |
| 4 | Keyboard navigation | NFR-05 | Tab to first card → arrow keys move focus → Enter activates |
| 5 | **Create Matter** card | FR-19 | Click → `CreateMatterWizardWidget` opens as new top tab in Workspace pane |
| 6 | **Create Project** card | FR-19 | Click → existing `sprk_createprojectwizard` Code Page opens via Xrm.Navigation |
| 7 | **Find Similar** card | FR-19 | Click → existing `sprk_findsimilar` Code Page opens via Xrm.Navigation |
| 8 | **Summarize Files** card | FR-19 | Click → `DocumentUploadWizardWidget` opens as new top tab |
| 9 | **Send Email** card | FR-19 | Click → Analysis Builder opens pre-configured with `email-compose` intent |
| 10 | **Schedule Meeting** card | FR-19 | Click → Analysis Builder opens pre-configured with `meeting-schedule` intent |
| 11 | **Assign Work** card | FR-20 | Click → `sprk_createworkassignmentwizard` opens via `Xrm.Navigation.navigateTo` |
| 12 | PlaybookGalleryWidget retained for non-welcome | FR-21 | Send a message that transitions to non-welcome stage → Context pane swaps to PlaybookGalleryWidget (not the 7-card grid) |
| 13 | Dark mode parity | NFR-06 / ADR-021 | Toggle dark mode → all 7 cards adapt |

### Pass criteria
All 13 checks PASS or accepted-with-note. Checks #5-#11 (7 cards) are the headline acceptance — every card must route to its correct destination.

---

## Task 074 — NFR verification (Lighthouse + timings)

**Record results in**: `projects/spaarke-ai-platform-unification-r3/notes/nfr-verification.md`

### Checks

| # | Check | NFR | Target | How to measure |
|---|---|---|---|---|
| 1 | Cold-load pane render | NFR-01 | <500 ms per pane | Lighthouse Performance audit (Mobile / Desktop) on SpaarkeAi cold load. Or Chrome DevTools Performance tab: load page, measure time-to-interactive for each pane |
| 2 | Shell-stage transition | NFR-01 | <100 ms | DevTools: send a message → measure time from `tab_count_change` event dispatch to UI update |
| 3 | History overlay open | NFR-03 | <200 ms | DevTools: click History button → measure click-to-overlay-visible |
| 4 | History list populate | NFR-03 | <300 ms p95 (50 items) | Open overlay 10× → measure `GET /api/ai/chat/sessions?limit=50` round-trip + render. Take p95. Use the `console.debug` timing emitted by `HistoryOverlay.tsx` (look for `performance.mark("HistoryOverlay.populated")`) |
| 5 | Session restore | NFR-02 | p95 <500 ms (R2 D-08) | Open a session → close → reopen via History overlay 10× → measure p95 |
| 6 | Bundle size (production) | NFR-12 | Reference | Already measured at 777.6 KB gzip; record + reference [`bundle-size-investigation.md`](perf/bundle-size-investigation.md) for Option 1 deviation context |
| 7 | Tab persistence latency | NFR-09 | <200 ms debounce + <500 ms restore | After tab change, observe in DevTools Network: PATCH `/tabs` fires 200ms after last action; on refresh, GET `/tabs` returns + restores within 500ms |

### Pass criteria
NFR-01, NFR-02, NFR-03 within targets. NFR-12 documented (Option 1 already accepted). NFR-09 timings reasonable.

---

## When you're done

For each task, when all checks pass (or you accept the failures with notes):

> **Tell me**: "task 071 done, all pass" or "task 072 done, item #11 failed" etc.

I'll:
1. Update TASK-INDEX row → ✅
2. Update task POML status → completed
3. Open any fix tasks for failures (if needed)
4. Move to next task or Wave 8 (wrap-up) when all 4 smoke tasks done

---

## If something fails

Common smoke-test failure modes + first-diagnosis steps:

| Symptom | Likely cause | Quick check |
|---|---|---|
| 401 on every BFF call | Auth bootstrap failed | DevTools Network: confirm `Authorization: Bearer ...` header is on every BFF call; if missing, see [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md) |
| 404 on `/api/ai/chat/sessions/.../tabs` | BFF deploy didn't include task 065 changes | Re-run `Deploy-BffApi.ps1`; verify `git log --oneline -1 src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/StoredSession.cs` shows commit `aa65a874` or later |
| Daily Briefing 500 errors | Endpoint connection failure or rate-limit cache misconfigured | Test `/api/ai/daily-briefing/narrate` directly via curl with valid token |
| Tabs don't persist on refresh | Frontend not calling PATCH /tabs OR BFF endpoint returning 404/500 | DevTools Network: watch for PATCH on tab change; check response code |
| Hex/rgba literal visible in dark mode | Slipped past task 062 audit | Find offending element via DevTools color picker; open fix task |
| Bundle size dramatically different from 777 KB | Build cache issue or unexpected commit | `cd src/solutions/SpaarkeAi && rm -rf dist && npm run build`; re-measure |

---

*This playbook is the canonical smoke checklist for R3 Phase G operator-driven smoke. Reference from per-task memo files (smoke-assistant.md, smoke-workspace.md, smoke-context.md, nfr-verification.md).*
