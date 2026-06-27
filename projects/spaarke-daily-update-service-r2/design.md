# Daily Briefing — SpaarkeAi Widget Framework Migration (R2)

> **Project**: spaarke-daily-update-service-r2
> **Status**: Design
> **Predecessor**: [`spaarke-daily-update-service`](../spaarke-daily-update-service/) (R1 — shipped producer layer + standalone code page; some Phase 7 items remain)
> **Created**: 2026-06-18
> **Author**: Architecture review session, 2026-06-18

---

## Executive Summary

The Daily Briefing widget renders empty in the SpaarkeAi workspace pane even when `appnotification` records exist for the signed-in user. The standalone Daily Briefing code page (`sprk_dailyupdate`) — same feature, different surface — works correctly and has a richer feature set (TL;DR with top action, per-bullet hyperlinks, Add-to-To-Do, Dismiss, Preferences dropdown, Caught-up footer). The SpaarkeAi widget, in contrast, renders only a stripped-down bullet list and currently shows nothing at all because of a wiring orphan.

This project migrates Daily Briefing to the canonical **SpaarkeAi widget framework Pattern D dual-use model** (Calendar precedent, per [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` §4](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md)): one shared widget component in `@spaarke/ui-components`, reused by BOTH the standalone code page and the SpaarkeAi workspace. The standalone code page is retained for users who access Daily Briefing as a standalone surface; it becomes a thin host shell over the shared component.

Along with the migration we fix two real bugs discovered during the architecture review (an orphan loader and a BFF `/narrate` prompt defect that produces dead hyperlinks), a UX defect (AI-aggregated "Multiple X" bullets that hide the actual items), and two side cleanups (native MDA bell deep-links + `sprk_playbooktype` OptionSet cleanup).

---

## Problem Statement

### What users see today

1. **SpaarkeAi workspace pane → Daily Briefing widget** renders the empty-state UI ("Nothing to see right now — enjoy your day") regardless of how many notifications the user has. Verified for a user with 40 unread `appnotification` records in spaarkedev1.
2. The standalone Daily Briefing code page (`sprk_dailyupdate`) renders correctly for the same user — same data, same BFF endpoint, completely different visual output.
3. AI-generated "Multiple X" bullets (e.g., *"Multiple urgent tasks and events related to the engagement of legal services by ACME Corporation are overdue since June 7-8, 2026"*) give no way to see what those underlying tasks are, no per-item action, and the matter hyperlink fails to open the matter record when clicked.

### What's broken (root causes, verified)

| # | Defect | Where | Verified by |
|---|---|---|---|
| **D1** | SpaarkeAi-local `loadSpaarkeAiNotificationContext` (332 lines) is an orphaned function — no callers in `src/solutions/SpaarkeAi/src/**`. The LegalWorkspace shim that the SpaarkeAi widget renders through provides no injection seam for this loader. Result: BFF `/narrate` receives empty payload → BFF short-circuits to empty bullets → empty-state UI. | [`src/solutions/SpaarkeAi/src/services/notificationContextLoader.ts:332`](../../src/solutions/SpaarkeAi/src/services/notificationContextLoader.ts); [`src/solutions/LegalWorkspace/src/sections/dailyBriefing/DailyBriefingSection.tsx:54-99`](../../src/solutions/LegalWorkspace/src/sections/dailyBriefing/DailyBriefingSection.tsx) | `grep` of SpaarkeAi src returns only the definition, no callers. Confirmed in git history: `WorkspaceHomeTab.tsx` (deleted in commit `3fe6d3b26` / PR #375) was the only consumer. |
| **D2** | SpaarkeAi widget uses the `@spaarke/ui-components`-hoisted `DailyBriefingSection`, which renders a flat bullet list — no TL;DR section, no channel headings, no per-bullet hyperlinks, no Add-to-To-Do, no Dismiss, no Preferences dropdown, no Caught-up footer. The full UX lives only in the standalone code page (`src/solutions/DailyBriefing/`). Pattern D violated: two divergent component trees instead of one shared component reused twice. | [`src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/sections/dailyBriefing/`](../../src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/sections/dailyBriefing/) vs [`src/solutions/DailyBriefing/src/components/`](../../src/solutions/DailyBriefing/src/components/) | Visual diff: standalone code page has 9 component files (TldrSection, ActivityNotesSection, NarrativeBullet, ChannelHeading, PreferencesDropdown, CaughtUpFooter, DigestHeader, EmptyState, MicrosoftToDoIcon); shared lib has 3 (registration, hook, section). |
| **D3** | BFF `/narrate` channel prompt does not include each item's `RegardingId`, so the LLM cannot return a real `primaryEntityId`. It hallucinates or uses the `appnotification` ID, which when passed to `Xrm.Navigation.navigateTo({entityName: "sprk_matter", entityId: "<hallucinated>"})` silently fails (no matching row). The frontend's `.catch(() => {})` swallows the rejection — no console error, just a dead link. | [`src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs:474-487`](../../src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs) (prompt builder); [`src/solutions/DailyBriefing/src/components/NarrativeBullet.tsx:119-134`](../../src/solutions/DailyBriefing/src/components/NarrativeBullet.tsx) (silent failure mode) | Code inspection. `ChannelItemDto` carries `RegardingId`; prompt builder only emits `Title|Body|RegardingName|RegardingEntityType|CreatedOn|Priority`. |
| **D4** | "Multiple X" aggregated bullets give the user no way to see or act on the underlying items. The matter-level hyperlink hides which 6 tasks are overdue. The aggregated "Add to To Do" creates a single vague `sprk_todo` named after the narrative summary, not after a real task. | Same UI files | UX inspection of live spaarkedev1 briefing screenshot, 2026-06-18. |

### What was already verified working (out of scope)

| ✓ | Component | Verified |
|---|---|---|
| ✓ | 7 notification playbooks deployed and active | Dataverse query 2026-06-18: 7 rows in `sprk_analysisplaybook` with `sprk_playbooktype=2`, all `statecode=Active`, all `triggertype=Scheduled`, recent `sprk_lastrundate` timestamps (most within last 24h) |
| ✓ | `PlaybookSchedulerService` registered as `HostedService` and running | [`Infrastructure/DI/AnalysisServicesModule.cs:531`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs) — no feature flag |
| ✓ | `CreateNotificationNodeExecutor` (ActionType 50) registered | [`Infrastructure/DI/AnalysisServicesModule.cs:670`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs) |
| ✓ | 4 inline event-driven hooks creating `appnotification` records | UploadEndpoints, AnalysisEndpoints, IncomingCommunicationProcessor, WorkAssignmentEndpoints (all wired) |
| ✓ | All 7 channel categories materializing in live data | Dataverse `appnotification` query 2026-06-18 returned rows for `new-documents`, `tasks-overdue`, `tasks-due-soon`, `new-emails`, `new-events`, `matter-activity`, `work-assignments` |

The producer layer is **healthy**. R2 is consumer-layer + BFF-prompt work only.

---

## Architecture Context

### Two-layer Daily Briefing architecture (verified)

```
┌─ PRODUCER LAYER (writes appnotification) — HEALTHY, OUT OF SCOPE ─────┐
│                                                                       │
│  Path A — Scheduled deterministic playbooks                           │
│    PlaybookSchedulerService (BackgroundService, registered)           │
│      ↓ ticks                                                          │
│    7 playbooks where sprk_playbooktype=2 × each active user           │
│      ↓ PlaybookOrchestrationService.ExecuteAppOnlyAsync               │
│    Query → Condition → CreateNotification (ActionType 50)             │
│      → POST appnotification (data.customData.category=...)            │
│                                                                       │
│  Path B — Inline event-driven hooks (4 BFF endpoints)                 │
│    Upload / AnalysisComplete / EmailReceived / WorkAssignmentCreated  │
│      → NotificationService.CreateNotificationAsync(...)               │
│                                                                       │
│  Path C — Native MDA / other Dataverse subsystems                     │
└────────────────────────────────────────────────────────────────────────┘
                                ↓
                  appnotification rows in Dataverse
                                ↓
┌─ CONSUMER LAYER (reads + narrates) — THIS PROJECT ────────────────────┐
│                                                                       │
│  Client (Xrm.WebApi) queries appnotification → groups by category    │
│      ↓                                                                │
│  Client POSTs grouped channels to BFF /api/ai/daily-briefing/narrate │
│      ↓                                                                │
│  BFF fires TL;DR + per-channel narration prompts in parallel via LLM │
│      ↓                                                                │
│  Returns { tldr, channelNarratives[] } with primaryEntity refs       │
│      ↓                                                                │
│  Client renders TL;DR + channel sections + bullets with action UX    │
│                                                                       │
│  Three surfaces consume this layer:                                  │
│    1. MDA native bell icon (built-in; reads data.actions[] — TODAY   │
│       this is empty → no native bell deep-links)                     │
│    2. Standalone Daily Briefing code page (sprk_dailyupdate)         │
│    3. SpaarkeAi workspace widget (BROKEN: orphan loader; stripped UX)│
└────────────────────────────────────────────────────────────────────────┘
```

### Pattern D dual-use (the target shape)

Per [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` §4](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md), the canonical "shared-lib widget + thin host shell" pattern is proven by Calendar:

| | Today | After R2 |
|---|---|---|
| Daily Briefing component tree | Two divergent trees (standalone code page = full UX; shared lib = stripped bullet list) | **One** tree in `@spaarke/ui-components`, mounted by both hosts |
| Standalone code page | ~9 component files + 3 hook files + 2 service files | Thin host shell (~50-100 LOC `main.tsx`/`App.tsx`) that initializes auth + Xrm + theme and mounts the shared `<DailyBriefingApp />` |
| SpaarkeAi widget | LegalWorkspace shim with no injection seam for `loadNotificationContext` | LegalWorkspace shim updated to forward `loadNotificationContext`; SpaarkeAi `main.tsx` injects `loadSpaarkeAiNotificationContext`. Same `<DailyBriefingApp />` shared component renders. |
| Future enhancements | Land in one tree, miss the other | Land in shared lib; both surfaces inherit automatically |

The standalone code page is **retained** (operator decision, 2026-06-18) — users may access Daily Briefing as a standalone surface; we don't retire it.

---

## Proposed Solution

### Scope summary

| # | Workstream | Files | Effort estimate |
|---|---|---|---|
| **P1** | Fix the SpaarkeAi orphan loader (wiring seam) | LegalWorkspace shim (2 files) + SpaarkeAi `main.tsx` | S |
| **P2** | Pattern D hoist — move full Daily Briefing UX into `@spaarke/ui-components`; standalone code page becomes thin host shell | ~9 component files + 3 hooks moved into shared lib; standalone `App.tsx` shrunk; SpaarkeAi mounts shared component | M-L |
| **P2a** | "Multiple X" hybrid aggregation UX — keep narrative bullet + always-visible per-item sub-list with own per-item entity links + own per-item To Do buttons | `NarrativeBullet.tsx` extended; uses supplied `regardingId` directly (no AI involvement) | S-M |
| **P2b** | BFF `/narrate` fix — include `RegardingId` in channel prompt + server-side validation of returned `primaryEntityId` against supplied IDs; null out invalid responses so frontend omits broken links | `DailyBriefingEndpoints.cs` (prompt builder + post-parse validation) | S |
| **P3** | Populate `data.actions[]` in `CreateNotificationNodeExecutor` so the MDA native bell icon gets clickable "Open" buttons | `CreateNotificationNodeExecutor.cs` (~5-10 LOC) | XS |

### P1 — SpaarkeAi orphan loader fix

**Goal:** Establish a single injection seam so SpaarkeAi can plumb `loadSpaarkeAiNotificationContext` into the rendered Daily Briefing section without resurrecting the deleted `WorkspaceHomeTab.tsx`.

**Approach:**
1. Extend `LegalWorkspace/src/sections/dailyBriefing/DailyBriefingSection.tsx` shim with optional prop `loadNotificationContext?: () => Promise<NarrateRequest | null>`. Forward to `DailyBriefingSectionShared`. Standalone callers continue to omit it (FR-25 preserved).
2. Replace the static `dailyBriefingRegistration` constant in `LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts` with a thin shim that reads from a module-level setter `setDailyBriefingNotificationLoader(fn)` (or via React context). Standalone behavior is byte-stable when no loader is set.
3. In `SpaarkeAi/src/main.tsx`, alongside the existing `setDefaultWorkspaceRenderer(LegalWorkspaceRenderer)` call (line ~216), add `setDailyBriefingNotificationLoader(loadSpaarkeAiNotificationContext)` (import from `./services/notificationContextLoader`).

**Note:** After P2 hoist completes, this seam moves into the shared lib's `createDailyBriefingRegistration` factory (the loader becomes a factory option). P1 may be subsumed by P2; whether P1 ships independently or merges with P2 is a sequencing question, not a scope question.

### P2 — Pattern D hoist

**Goal:** Move the standalone code page's full UX into `@spaarke/ui-components`. Standalone code page becomes a thin host shell. SpaarkeAi mounts the same shared component.

**Hoisted into `@spaarke/ui-components/src/components/WorkspaceShell/sections/dailyBriefing/`** (or a new top-level surface — folder layout TBD during implementation):

- Components: `DailyBriefingApp`, `TldrSection`, `ActivityNotesSection`, `ChannelHeading`, `NarrativeBullet` (extended for P2a), `PreferencesDropdown`, `CaughtUpFooter`, `DigestHeader`, `EmptyState`, `MicrosoftToDoIcon`
- Hooks: `useBriefingNarration`, `useInlineTodoCreate`, `useNotificationData` (the data path — currently `src/solutions/DailyBriefing/src/hooks/useNotificationData.ts`)
- Services: `notificationService` (Xrm `appnotification` query + group + mark-read), `preferencesService`, `briefingService` (BFF `/narrate` client)
- Types: `notifications.ts`

**Standalone code page** (`src/solutions/DailyBriefing/`) becomes ~3 files:
- `main.tsx` — runtime config + auth init + theme bootstrap + Xrm resolution + mount `<DailyBriefingApp {...hostProps} />`
- `App.tsx` (or inline in main.tsx) — Fluent provider + the shared component
- Vite config / index.html — unchanged

**SpaarkeAi widget** mounts the same shared component via the `createDailyBriefingRegistration` factory (now consuming the full UX), with `loadNotificationContext: loadSpaarkeAiNotificationContext` injected.

**FR-25 / NFR-10 preservation:** the standalone code page's rendered output must remain byte-stable (or as close as practical) after the hoist. The standalone host shell provides the SAME host bindings (`authenticatedFetch`, `tenantId`, `webApi`, `userId`) that the inline component currently resolves.

### P2a — Hybrid aggregation UX (Option A)

**Goal:** Surface actionable items even when the AI groups multiple notifications into a single "Multiple X" narrative bullet.

**Shape:** For any `NarrativeBullet` where `itemIds.length > 1`, render in addition to the narrative line + matter link:

```
• Multiple urgent tasks and events related to the engagement of legal services
  by ACME Corporation are overdue since June 7-8.
  Engagement of legal services by ACME Corporation ↗     [✓] [✗]
     · Overdue: Event "Discovery review"  Engagement... ↗   [✓] [✗]
     · Overdue: Task "Draft response brief"  Engagement... ↗ [✓] [✗]
     · Overdue: Task "File motion"  Engagement... ↗          [✓] [✗]
     · Overdue: Event "Status conference"  Engagement... ↗   [✓] [✗]
     · Overdue: Event "Settlement meeting"  Engagement... ↗  [✓] [✗]
     · Overdue: Task "Review depositions"  Engagement... ↗   [✓] [✗]
```

Each sub-row:
- Renders the underlying `NotificationItem` (resolved via `itemIds[]` cross-referenced against the `channels[].group.items[]` from `useNotificationData`).
- Sub-row entity link uses the supplied `regardingEntityType` + `regardingId` directly — NO AI involvement → no broken links.
- Sub-row To Do button calls `useInlineTodoCreate(item)` with the SPECIFIC underlying `NotificationItem`, producing a concrete `sprk_todo` (real title, real body, real regarding lookup).
- Sub-row Dismiss button marks that specific `appnotification` as read.

Single-item bullets (`itemIds.length === 1`) render unchanged.

**Visual treatment:** sub-rows indented with a left rule, smaller font (`fontSizeBase200`), reduced spacing (`spacingVerticalS`). Whole component still passes Fluent v9 semantic tokens (ADR-021).

### P2b — BFF `/narrate` fix

**Goal:** Eliminate the dead-hyperlink failure mode at the source (in addition to defense-in-depth at the frontend via P2a's per-item sub-rows).

**Prompt builder change** ([`DailyBriefingEndpoints.cs:474-487`](../../src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs)) — include `regardingId` per item:

```csharp
// In BuildChannelNarrationPrompt, the per-item line becomes:
sb.AppendLine($"- [id={item.Id} regardingId={item.RegardingId}] {string.Join(" | ", parts)}");
```

**Prompt instructions** — add an explicit rule:

```
Rules:
- ...
- Set primaryEntityType to the regardingEntityType of the most relevant
  supplied item.
- Set primaryEntityId to the matching regardingId of the most relevant
  supplied item. Do NOT use the [id=...] notification ID. Do NOT invent IDs.
```

**Server-side validation** — after `ParseChannelBullets`, validate each bullet's `primaryEntityId` against the union of supplied `item.RegardingId` values for that channel. If no match, null out `primaryEntityType` + `primaryEntityId` + `primaryEntityName` so the frontend renders no link rather than a broken link. Log a warning so we can monitor model drift.

### P3 — Native MDA bell deep-links

**Goal:** Every `appnotification` produced by the executor gets a clickable "Open" button in the MDA native bell icon, with no Daily Briefing UI involvement.

**Change** in `CreateNotificationNodeExecutor.cs` — populate `data.actions[]` (Dataverse native action array, up to 2 entries) when `actionUrl` is present:

```csharp
data = JsonSerializer.Serialize(new {
    actions = !string.IsNullOrEmpty(actionUrl) ? new[] {
        new { title = "Open", data = new { url = actionUrl } }
    } : null,
    customData = new { /* existing fields */ }
})
```

The Daily Briefing UI continues to read `customData.actionUrl` as today (no change). The MDA bell icon picks up `actions[]` automatically.

> **Note:** The `sprk_playbooktype` OptionSet cleanup discussed during scoping (label typo at value 2, duplicate at value 3, retire/repurpose value 4) was applied directly by the operator on 2026-06-18 and is **out of scope** for R2. Final post-fix state: `AiAnalysis(0) / Workflow(1) / Notification(2) / Hybrid(3)`.

---

## In Scope

- P1 — Wiring seam for `loadNotificationContext` injection from SpaarkeAi
- P2 — Pattern D hoist: full Daily Briefing UX into `@spaarke/ui-components`; standalone code page becomes thin host shell; SpaarkeAi mounts shared component
- P2a — "Multiple X" hybrid aggregation UX (narrative bullet + always-visible per-item sub-list with own entity links + own To Do buttons)
- P2b — BFF `/narrate` prompt fix: include `regardingId` + server-side `primaryEntityId` validation
- P3 — Populate `data.actions[]` in `CreateNotificationNodeExecutor` for native MDA bell deep-links
- Test coverage for the hoisted shared lib (unit + integration where practical)
- Updated architecture docs: `SPAARKEAI-WORKSPACE-ARCHITECTURE.md` Daily Briefing section + `SPAARKEAI-COMPONENT-MODEL.md` for the new shared lib location

## Out of Scope

- Producer-layer changes (the 7 notification playbooks, the scheduler, the executor) beyond P3 — they are verified healthy
- New notification playbook categories (similar-documents, similar-matters, budget alerts, etc.) — these were R1 R2-future scope; remain future
- Retiring the standalone Daily Briefing code page — explicitly retained (user decision, 2026-06-18)
- MDA bell icon UX customization beyond `data.actions[]` population
- Real-time SignalR push for instant notification delivery — future
- Mobile / responsive layout adjustments — desktop dialog only
- Changes to `sprk_userpreference` schema or preference-fetch logic
- Auto-popup behavior changes (`useDailyDigestAutoPopup` unchanged)

---

## Technical Constraints

### Applicable ADRs

- **ADR-001**: BFF Minimal API — all BFF changes (P2b, P3) stay inside `Sprk.Bff.Api`; no Azure Functions, no separate services
- **ADR-006**: Code Page pattern — standalone Daily Briefing remains a Vite + React 19 + Fluent v9 Code Page; not converted to PCF
- **ADR-010**: DI minimalism — no new DI registrations needed for this project
- **ADR-012**: Shared component library — Pattern D hoist into `@spaarke/ui-components`; both standalone code page AND SpaarkeAi widget consume the same shared component
- **ADR-013**: AI features extend BFF — `/narrate` endpoint changes stay in BFF
- **ADR-021**: Fluent UI v9 exclusively, semantic tokens, dark mode required throughout
- **ADR-024**: Multi-entity regarding resolution — `useInlineTodoCreate` already implements this via `TODO_REGARDING_CATALOG`; preserve in the hoist
- **ADR-028**: Auth contract — `@spaarke/auth` provides true SSO; `authenticatedFetch` flows through `DailyBriefingApp` as a prop

### MUST rules

- MUST preserve standalone Daily Briefing code page as a working surface (operator decision 2026-06-18)
- MUST use Pattern D dual-use (one shared component, two host shells) — no divergent component trees
- MUST validate AI-returned `primaryEntityId` against supplied `regardingId` set server-side; null out invalid responses rather than rendering broken links
- MUST use the supplied `regardingId` (not AI output) for per-item sub-row hyperlinks in the hybrid aggregation UX
- MUST preserve FR-25 / NFR-10 byte-stability for the standalone code page render (or document any intentional deviation)
- MUST NOT resurrect `WorkspaceHomeTab.tsx` (the post-task-109 unified pipeline is the canonical SpaarkeAi shape)
- MUST NOT introduce new BFF endpoints (use existing `/narrate`)
- MUST NOT retire the standalone Daily Briefing code page in this round

---

## Success Criteria

1. **SpaarkeAi widget renders Daily Briefing with real notifications** — for a user with N>0 unread `appnotification` records, the workspace pane's "Daily Briefing" tab shows the same TL;DR + channel sections + bullets the standalone code page shows for the same user, with the same items.
2. **Same component, two hosts** — `@spaarke/ui-components` contains the canonical `DailyBriefingApp` component; the standalone code page `App.tsx` is ≤100 LOC of host-binding plumbing; SpaarkeAi mounts the same component via the section factory.
3. **No dead hyperlinks** — for every rendered bullet, clicking the entity link opens a modal dialog with the correct record. Verified for at least: aggregated matter-level bullets, single-item sub-row links, and single-item top-level bullets. Per-item sub-rows always resolve (no AI involvement).
4. **Aggregated bullets surface action items** — every "Multiple X" bullet has a visible per-item sub-list below it. Each sub-row has its own entity link + own To Do button + own Dismiss button.
5. **Add-to-To-Do creates concrete `sprk_todo` records** — per-item To Do creation produces a `sprk_todo` with the specific item's title/body/regarding, not a vague aggregated summary.
6. **BFF `/narrate` prompt includes `regardingId`** for every item; server validates `primaryEntityId` against supplied IDs; null-out on mismatch verified via unit test.
7. **MDA native bell icon shows clickable "Open" buttons** for every notification produced by the executor (verified by opening the bell after a scheduler tick fires).
8. **Standalone Daily Briefing code page behavior unchanged for end users** — same TL;DR, same bullets, same actions, same preferences UX (byte-stable or documented deviation).
9. **Dark mode unaffected** — all hoisted components use Fluent v9 semantic tokens.

---

## Open Questions / Risks

| # | Topic | Question | Risk if wrong |
|---|---|---|---|
| **Q1** | Hoist folder layout in shared lib | Place under `WorkspaceShell/sections/dailyBriefing/` (extending the existing shim location) or a new top-level surface like `DailyBriefing/`? Calendar precedent uses `@spaarke/events-components` — a separate package. | Bad layout creates churn for future widget hoists; can be refactored later but ideally pick the right shape now. |
| **Q2** | `useNotificationData` couples preferences + mark-as-read into the data fetch | Should the hoisted hook keep that coupling (current shape) or be split (data fetch vs preferences vs actions)? | Coupled shape was the original "Option C copy" decision; splitting is cleaner but invasive. Defer or address inside P2. |
| **Q3** | `MicrosoftToDoIcon` already lives in `LegalWorkspace/src/icons/` — needs to move where? | Move into `@spaarke/ui-components/icons/` and have LegalWorkspace consume from there, OR keep in LegalWorkspace and have shared lib import (creates cycle), OR duplicate. | Wrong choice creates a cycle or duplication. |
| **Q4** | Calendar precedent uses a separate package `@spaarke/events-components` — should Daily Briefing get `@spaarke/briefing-components` similarly? | Open. | Affects package boundary; reversible but costly. |
| **R1** | Standalone code page byte-stability under hoist | The hoist may shift styles/spacing subtly; FR-25 says byte-stable. | If deviation surfaces, document it and have operator sign off. |
| **R2** | BFF `/narrate` prompt change may shift the LLM's output distribution beyond just `primaryEntityId` | LLM may grow more conservative on aggregation, fewer bullets per channel, etc. | Mitigate with golden-prompt regression tests + monitoring. |
| **R3** | `data.actions[]` population — does the MDA bell render reliably across all toasttype values used by the executor? | Need to verify with one playbook category + one inline category. | If not reliable, P3 reduces to a no-op or needs an alternate shape. |

---

## Dependencies

### Prerequisites

- Producer layer operational (verified)
- BFF `/narrate` endpoint operational (verified)
- `@spaarke/ui-components` shared lib build pipeline operational
- `@spaarke/auth` providing `authenticatedFetch` (in use)
- spaarkedev1 access for E2E verification

### Related projects

- [`spaarke-daily-update-service`](../spaarke-daily-update-service/) (R1) — predecessor; producer layer + standalone code page + Phase 8 narrative redesign
- `spaarke-ai-workspace-UI-r1` followup-backlog — Item 5 (Summarize Files Wizard Email 400 + Project create missing fields) is unrelated but proximate; do not merge
- `spaarke-ui-functional-cleanup-r1` — Item 1 (Modal-preview record-open standard for ALL dataset grids) overlaps with the modal-dialog UX expectations; coordinate
- Calendar widget (`@spaarke/events-components`) — Pattern D precedent; review during P2

---

## Migration / Sequencing Notes

The P-numbered items above are workstreams, not phases. A likely phasing:

- **Phase 1** — P2b (BFF prompt fix) ships first as a self-contained backend change; benefits both the standalone code page and the eventual SpaarkeAi widget. Low risk.
- **Phase 2** — P1 (wiring seam) ships independently as a tactical fix for the SpaarkeAi empty-state regression. Benefits users immediately even with the stripped-down UX. Cheap.
- **Phase 3** — P2 + P2a (Pattern D hoist + hybrid aggregation UX) ships together — they touch the same files. Higher risk; needs the standalone byte-stability check.
- **Phase 4** — P3 (data.actions[]) ships as a parallel housekeeping commit.

This sequencing keeps each phase's blast radius small and gives the operator wins at each step rather than a single big-bang at the end. Final phasing decided during `/project-pipeline`.

---

## References

- Assessment: [`C:\tmp\daily-briefing-spaarkeai-wiring-assessment.md`](C:\tmp\daily-briefing-spaarkeai-wiring-assessment.md) (the user-supplied bug report that initiated this review)
- Predecessor project: [`projects/spaarke-daily-update-service/`](../spaarke-daily-update-service/)
- Architecture: [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md), [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md), [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md)
- Pattern D precedent: Calendar widget — `@spaarke/events-components` + `src/solutions/Calendar/` (standalone) + SpaarkeAi workspace section
- Live verification (2026-06-18): 7 deployed `sprk_analysisplaybook` rows with `sprk_playbooktype=2` in spaarkedev1; 20+ recent `appnotification` rows covering all 7 designed channel categories

---

*Generated from architecture-review session, 2026-06-18*
