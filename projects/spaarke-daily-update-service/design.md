# Daily Update Service — Personalized Activity Digest

> **Project**: spaarke-daily-update-service
> **Status**: Design
> **Priority**: Medium
> **Last Updated**: March 30, 2026

---

## Executive Summary

Create a "Daily Update" popup Code Page that presents users with a personalized, at-a-glance summary of what's new and what needs attention across their matters and projects. Users subscribe to channels (email, tasks, documents, events) via preferences, and the digest surfaces the most relevant items in a scannable card-based layout. Opens as a dialog on workspace launch (if enabled) or on-demand via command bar.

---

## Problem Statement

Today, users must navigate to multiple views and entities to understand what happened since their last session:

- **Emails**: Check Outlook or the Communication entity list
- **Tasks due**: Open SmartToDo or the Events grid, filter by due date
- **New documents**: Navigate to each matter/project document library
- **Matter/project activity**: Scroll through the Activity Feed on the workspace

There is no single view that answers: "What do I need to know right now?" The workspace Activity Feed (Latest Updates) is close but shows a flat chronological list without categorization, prioritization, or subscription control.

---

## Goals

1. **Single-glance digest** — one popup shows everything important, categorized by channel
2. **Subscription-based** — users choose which channels to see (preferences stored in Dataverse)
3. **Time-windowed** — shows activity since last login or last 24 hours (configurable)
4. **Actionable** — each item links to the relevant record, document, or form
5. **Non-intrusive** — popup on workspace load (if user enables) or manual trigger via command bar
6. **AI-enhanced** (future) — optional AI summary of key themes across channels

---

## Design

### Launch Behavior

| Trigger | How | User Control |
|---------|-----|--------------|
| **Auto on workspace load** | LegalWorkspace `App.tsx` checks preference → opens dialog | User enables/disables in preferences |
| **Manual** | Command bar button "Daily Update" on workspace | Always available |
| **Deep link** | `Xrm.Navigation.navigateTo({ webresourceName: "sprk_dailyupdate" })` | For M365 Copilot handoff |

Auto-popup fires **once per session** (tracked via `sessionStorage` flag `spaarke-daily-update-shown`). If user dismisses, it doesn't reappear until next session.

### Channels

Each channel is a category of activity the user can subscribe to. Channels are independently queryable and independently toggleable.

| Channel ID | Label | Data Source | Query |
|------------|-------|-------------|-------|
| `tasks-due` | Tasks Due | `sprk_event` | `sprk_todoflag=true AND sprk_duedate <= {today+3days} AND _ownerid_value={userId}` |
| `tasks-overdue` | Overdue Tasks | `sprk_event` | `sprk_todoflag=true AND sprk_duedate < {today} AND sprk_todostatus=Open` |
| `new-documents` | New Documents | `sprk_document` | `createdon >= {since} AND (_sprk_matter_value IN user's matters OR _sprk_project_value IN user's projects)` |
| `new-events` | New Events | `sprk_event` | `createdon >= {since} AND (regarding user's matters/projects) AND sprk_todoflag=false` |
| `new-emails` | New Emails | `sprk_communication` | `createdon >= {since} AND (_sprk_regarding_value IN user's matters)` |
| `matter-updates` | Matter Activity | `sprk_matter` | `modifiedon >= {since} AND _ownerid_value={userId}` |
| `project-updates` | Project Activity | `sprk_project` | `modifiedon >= {since} AND _ownerid_value={userId}` |
| `assignments` | Work Assignments | `sprk_workassignment` | `createdon >= {since} AND _sprk_assignedto_value={userId}` |

**`{since}`** = user's last login timestamp (from `sprk_userpreference`) or fallback to 24 hours ago.

### Channel Subscription Storage

Uses existing `sprk_userpreference` entity:

| Field | Value |
|-------|-------|
| `sprk_preferencetype` | `100000002` (DailyUpdateChannels) |
| `sprk_preferencevalue` | JSON: `{ "channels": ["tasks-due", "tasks-overdue", "new-documents", "new-events"], "autoPopup": true, "timeWindow": "24h" }` |

**Default subscription** (new users): All channels enabled, auto-popup enabled, 24h window.

### UI Layout

```
┌─────────────────────────────────────────────────────────────┐
│ Daily Update                           March 30, 2026  [✕]  │
│ Since your last visit (8 hours ago)     [⚙️ Preferences]    │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│ ⚠️ Overdue Tasks (2)                                        │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ 🔴 Review NDA — Acme Corp          Due: Mar 28 (2d ago)│ │
│ │ 🔴 File Response Brief — Smith IP  Due: Mar 29 (1d ago)│ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│ 📋 Tasks Due Soon (3)                                       │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ 🟡 Complete Compliance Checklist    Due: Mar 31 (tmrw) │ │
│ │ 🟡 Send Engagement Letter          Due: Apr 1 (2 days)│ │
│ │ 🟡 Review Invoice #4521            Due: Apr 2 (3 days)│ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│ 📄 New Documents (4)                                        │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ 📎 Smith_Lease_Amendment.pdf        Acme Corp Matter   │ │
│ │ 📎 Q1_Compliance_Report.xlsx        Q1 Audit Project   │ │
│ │ 📎 NDA_Redline_v3.docx             Smith IP Matter     │ │
│ │ 📎 Invoice_4521.pdf                Acme Corp Matter    │ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│ 📧 New Emails (2)                                           │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ ✉️ RE: Settlement Proposal          From: J. Smith      │ │
│ │ ✉️ Engagement Letter Draft          From: K. Williams   │ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│ 📊 Matter & Project Activity (3)                            │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ 🔄 Acme Corp Matter — status changed to "Active"       │ │
│ │ 🔄 Q1 Audit Project — new work assignment added        │ │
│ │ 🔄 Smith IP Matter — outside counsel assigned          │ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│                    [Open Workspace]  [Dismiss]              │
└─────────────────────────────────────────────────────────────┘
```

### Component Architecture

```
DailyUpdate Code Page (sprk_dailyupdate)
├── React 19 + Vite single-file build (ADR-026)
├── FluentProvider (theme from shared themeStorage)
│
├── DailyUpdateApp.tsx
│   ├── useDailyUpdateData(webApi, userId, preferences)
│   │   ├── Fetches all subscribed channels in parallel
│   │   ├── Returns { channels: ChannelResult[], since: Date, isLoading }
│   │   └── Updates lastLoginTimestamp after successful fetch
│   │
│   ├── DailyUpdateHeader
│   │   ├── Title + date
│   │   ├── "Since your last visit" subtitle with relative time
│   │   └── Preferences gear button → opens inline settings
│   │
│   ├── ChannelSection × N (one per subscribed channel)
│   │   ├── Channel icon + title + badge count
│   │   ├── Collapsible (default expanded if items > 0, collapsed if empty)
│   │   └── ItemCard × N
│   │       ├── Icon (based on item type)
│   │       ├── Title (record name)
│   │       ├── Subtitle (matter/project name, date, sender)
│   │       └── onClick → Xrm.Navigation.openForm or navigateTo
│   │
│   ├── EmptyState (when all channels return 0 items)
│   │   └── "You're all caught up! No new activity since {time}."
│   │
│   └── Footer
│       ├── [Open Workspace] → navigates to corporateworkspace
│       └── [Dismiss] → closes dialog
│
└── PreferencesPanel (inline, slides in from right)
    ├── Channel toggles (Switch per channel)
    ├── Auto-popup toggle
    ├── Time window selector (12h / 24h / 48h / 7d)
    └── [Save] → persists to sprk_userpreference
```

### Data Flow

```
Dialog opens
  │
  ├── Read preferences from sprk_userpreference (type 100000002)
  │   └── If none exists → use defaults (all channels, 24h)
  │
  ├── Determine "since" timestamp
  │   ├── Read lastLoginTimestamp from preferences
  │   └── Fallback: now - timeWindow (24h default)
  │
  ├── Fetch subscribed channels IN PARALLEL
  │   ├── Channel A: GET sprk_events?$filter=...  → IEvent[]
  │   ├── Channel B: GET sprk_documents?$filter=... → IDocument[]
  │   ├── Channel C: GET sprk_communications?$filter=... → ICommunication[]
  │   └── ...
  │
  ├── Sort each channel's items by relevance
  │   ├── Overdue tasks: by days overdue (most overdue first)
  │   ├── Due soon: by due date ascending
  │   ├── Documents/events: by createdon descending (newest first)
  │   └── Emails: by createdon descending
  │
  ├── Render ChannelSection per subscribed channel
  │   └── Skip channels with 0 items (or show collapsed with "No new items")
  │
  └── After render: update lastLoginTimestamp in preferences
```

### Preferences Panel

```
┌─────────────────────────────────────────┐
│ Daily Update Preferences          [✕]   │
│                                         │
│ Show on workspace launch                │
│ [=====●] On                             │
│                                         │
│ Time window                             │
│ [  24 hours  ▾]                         │
│                                         │
│ Channels                                │
│ ──────────                              │
│ [=====●] Overdue Tasks                  │
│ [=====●] Tasks Due Soon                 │
│ [=====●] New Documents                  │
│ [=====●] New Events                     │
│ [=====●] New Emails                     │
│ [=====●] Matter Activity                │
│ [=====●] Project Activity               │
│ [=====●] Work Assignments               │
│                                         │
│               [Cancel]  [Save]          │
└─────────────────────────────────────────┘
```

---

## BFF API Endpoints (Optional — for complex queries)

For simple queries, the Code Page can use `Xrm.WebApi.retrieveMultipleRecords()` directly (no BFF needed). However, some channels may benefit from a BFF endpoint for:
- Aggregating across multiple entities in a single call
- Resolving "user's matters/projects" authorization scope
- Computing relative priority/urgency scores

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/daily-update/digest?since={iso}&channels=tasks-due,new-documents` | Returns aggregated digest across channels |
| `GET` | `/api/daily-update/preferences` | Returns user's channel subscriptions |
| `PUT` | `/api/daily-update/preferences` | Updates user's channel subscriptions |

**R1 approach**: Use `Xrm.WebApi` directly for all queries (simpler, no BFF changes). Evaluate BFF aggregation endpoint for R2 if performance is a concern (multiple parallel OData queries may be faster than one BFF round-trip for small datasets).

---

## Integration Points

### Workspace Launch Hook

In `LegalWorkspace/src/App.tsx` (or `main.tsx`):

```typescript
useEffect(() => {
  const shown = sessionStorage.getItem("spaarke-daily-update-shown");
  if (shown) return;

  // Check user preference for auto-popup
  const prefs = await getUserPreference(webApi, userId, DAILY_UPDATE_PREF_TYPE);
  if (prefs?.autoPopup !== false) {
    Xrm.Navigation.navigateTo(
      { pageType: "webresource", webresourceName: "sprk_dailyupdate" },
      { target: 2, width: { value: 60, unit: "%" }, height: { value: 80, unit: "%" } }
    );
    sessionStorage.setItem("spaarke-daily-update-shown", "true");
  }
}, []);
```

### Command Bar Button

Add "Daily Update" button to workspace command bar (alongside theme menu):

```xml
<Button Id="sprk.DailyUpdate.Button"
        Command="sprk.DailyUpdate.Open"
        LabelText="Daily Update"
        Image16by16="$webresource:sprk_DailyUpdate16.svg" />
```

### M365 Copilot Handoff

M365 Copilot agent can suggest opening the Daily Update:

```
User: "What's new today?"
Agent: Here's a quick summary of your activity.
       [📊 Open Daily Update]  ← deep-link to sprk_dailyupdate
```

---

## Item Actions

Each item in the digest is clickable with context-appropriate actions:

| Item Type | Click Action | Secondary Actions |
|-----------|-------------|-------------------|
| Task (overdue/due) | Open event form | Mark complete, Open SmartToDo |
| Document | Open document preview (Analysis Workspace) | Download, Open in matter |
| Email | Open communication record | Reply (future) |
| Matter update | Open matter form | Open workspace filtered to matter |
| Project update | Open project form | Open workspace filtered to project |
| Work assignment | Open assignment form | Accept/decline (future) |

---

## Channel Registry (Code-Side)

Similar to the workspace Section Registry pattern, channels are registered in code:

```typescript
export interface ChannelRegistration {
  id: string;
  label: string;
  icon: FluentIcon;
  category: "urgent" | "new" | "activity";
  sortOrder: number;
  fetchItems: (webApi: IWebApi, userId: string, since: Date) => Promise<DigestItem[]>;
  renderItem: (item: DigestItem) => React.ReactNode;
}
```

This allows new channels to be added by registering them — no changes to the digest layout component.

---

## Scope

### In Scope (R1)
- `sprk_dailyupdate` Code Page (React 19, Vite single-file)
- 8 channels: tasks-due, tasks-overdue, new-documents, new-events, new-emails, matter-updates, project-updates, assignments
- Channel subscription preferences via `sprk_userpreference` (type 100000002)
- Auto-popup on workspace launch (configurable)
- Manual trigger via command bar button
- Inline preferences panel (channel toggles, time window, auto-popup)
- Time window configuration (12h / 24h / 48h / 7d)
- Click-through to source records
- Empty state when no new activity
- Dark mode support (unified theme from R2 theme project)
- Channel registry pattern for extensibility

### Out of Scope (R2+)
- AI-generated summary across channels ("Today's key themes: 3 contracts pending review, 2 overdue compliance items")
- BFF aggregation endpoint (R1 uses Xrm.WebApi directly)
- Push notifications (email digest, Teams notification)
- Scheduled background job to compute daily update
- Custom channels (user-defined queries)
- Shared/team digests
- Historical digest archive ("What happened last week")
- Badge count on workspace navigation showing unread digest items

---

## Technical Constraints

### Applicable ADRs
- **ADR-006**: Code Page for standalone dialog (not PCF)
- **ADR-012**: Shared components from `@spaarke/ui-components`
- **ADR-021**: Fluent UI v9 exclusively; semantic tokens; dark mode support
- **ADR-026**: Vite + vite-plugin-singlefile build standard

### MUST Rules
- MUST use `Xrm.Navigation.navigateTo` to open as dialog (not `window.open`)
- MUST use `Xrm.WebApi.retrieveMultipleRecords` for data queries (R1 — no BFF)
- MUST fetch channels in parallel (`Promise.allSettled`) — one slow channel must not block others
- MUST use `Promise.allSettled` (not `Promise.all`) — individual channel failures show inline error, not dialog crash
- MUST track auto-popup per session via `sessionStorage` (not localStorage) — one popup per browser session
- MUST support dark mode via unified theme utility (themeStorage.ts)
- MUST use Fluent v9 components (Card, Badge, Switch, Dropdown)

---

## Success Criteria

1. [ ] Daily Update dialog opens on workspace launch (if user has auto-popup enabled)
2. [ ] Dialog shows categorized activity summary across subscribed channels
3. [ ] Each item is clickable and navigates to the correct record
4. [ ] Preferences panel allows toggling channels and time window
5. [ ] Preferences persist to Dataverse (cross-device)
6. [ ] Auto-popup fires only once per session
7. [ ] Empty state displays when no new activity
8. [ ] Dark mode renders correctly
9. [ ] New channels can be added via registry without layout changes

---

## Dependencies

### Prerequisites
- `sprk_userpreference` entity (exists)
- Workspace command bar (exists — add button)
- Unified theme utility from dark mode R2 project (recommended but not blocking)

### Related Projects
- `spaarke-workspace-user-configuration-r1` — workspace header could include Daily Update button
- `spaarke-mda-darkmode-theme-r2` — unified theme for dark mode support
- `ai-m365-copilot-integration-r1` — Copilot can suggest "Open Daily Update"

---

*Last updated: March 30, 2026*
