# Daily Briefing Redesign: Narrative Executive Memo

> **Date**: 2026-04-01
> **Status**: Design Discussion
> **Direction**: Shift from notification dashboard to narrative daily memo

---

## Design Summary

Replace the current card/accordion dashboard with a **scrollable narrative document** — an AI-assisted daily memo that reads like a brief prepared by an executive assistant.

### Current → Proposed

| Element | Current | Proposed |
|---------|---------|----------|
| AI Summary | 3-4 sentence card at top | **TL;DR section** — 5-7 sentences, calls out top action item |
| Channel data | Collapsible accordion cards | **Activity Notes** — flat narrative with channel headings + bullet prose |
| Notification items | Row with title/body/metadata | Narrative bullet with inline record hyperlink (opens dialog) |
| Mark as read | Checkmark icon | **X** icon (dismiss/clear) |
| Add to To Do | N/A | **Checkmark** icon — inline quick-create `sprk_event` with `todoflag=true` |
| Preferences | Collapsible panel at bottom | **Gear icon** in header toolbar → dropdown panel |
| Visual density | Cards with borders, expand/collapse chrome | Clean prose with generous whitespace, Fluent spacing tokens |

---

## Layout (Top to Bottom)

```
┌─────────────────────────────────────────────────────┐
│  🔔 Daily Briefing          [⚙️]  [↻ Refresh]      │  ← Header toolbar
├─────────────────────────────────────────────────────┤
│                                                     │
│  TL;DR                                    AI badge  │  ← AI-generated, 5-7 sentences
│  ─────                                              │
│  You have a busy day ahead. Three overdue tasks     │
│  need immediate attention — the Acme engagement     │
│  letter review is highest priority with a deadline  │
│  tomorrow. Five new documents were uploaded across   │
│  two matters, including the revised Q4 budget that  │
│  Ralph requested. Two new emails arrived on the     │
│  Johnson matter. Your most important action today   │
│  is completing the Acme engagement letter review.   │
│                                                     │
│  Generated just now                                 │
│                                                     │
├ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─┤  ← spacingVerticalXXL divider
│                                                     │
│  Activity Notes                                     │
│                                                     │
│  ■ Overdue Tasks                            3 items │  ← Channel heading
│                                                     │
│  • The Acme engagement letter review is now 2       │  ← AI-generated narrative bullet
│    days overdue — originally due March 30.          │
│    [Acme Engagement Letter ↗]          [✓]  [✕]    │  ← hyperlink + To Do + Dismiss
│                                                     │
│  • Johnson matter compliance filing was due         │
│    yesterday and is flagged as high priority.       │
│    [Johnson Compliance Filing ↗]       [✓]  [✕]    │
│                                                     │
│  • Monthly report summary for Portfolio Health      │
│    is 3 days past due.                              │
│    [Portfolio Health Report ↗]         [✓]  [✕]    │
│                                                     │
│  ■ New Documents                            5 items │  ← Next channel
│                                                     │
│  • Three documents were uploaded to the Acme        │
│    Matter today, including the revised engagement   │
│    letter and two supporting exhibits.              │
│    [Acme Matter ↗]                     [✓]  [✕]    │
│                                                     │
│  • Two new filings were added to the Johnson        │
│    Matter — the amended complaint and motion to     │
│    compel discovery.                                │
│    [Johnson Matter ↗]                  [✓]  [✕]    │
│                                                     │
│  ■ New Emails                               2 items │
│  ...                                                │
│                                                     │
│  ■ Upcoming Events                          1 item  │
│  ...                                                │
│                                                     │
│  ─────────────────────────────────────────────────  │
│  You're caught up on: Matter Activity,              │  ← Channels with 0 items
│  Work Assignments, System                           │
│                                                     │
└─────────────────────────────────────────────────────┘
```

---

## AI Strategy: Batch-Per-Channel

### Cost Analysis

| Approach | API Calls | Est. Cost | Quality |
|----------|-----------|-----------|---------|
| Template-based bullets | 0 | $0.000 | Mechanical, repetitive |
| AI per-item | 10-50 | $0.02-0.10 | Marginal improvement |
| **AI batch-per-channel** | **7-8** | **$0.01-0.02** | **Contextual grouping, natural prose** |

**Recommended: Batch-per-channel.** One API call per active channel. The AI receives all items for a channel and returns narrative bullets that:
- Group related items naturally ("Three documents were uploaded to the Acme Matter...")
- Add contextual detail beyond raw notification data
- Prioritize within the channel (most important first)
- Reference entity names conversationally

### API Changes

**New endpoint** (or extend existing):

```
POST /api/ai/daily-briefing/narrate
```

**Request:**
```json
{
  "tldr": {
    "categories": [...],         // same as current summarize
    "priorityItems": [...],
    "totalNotificationCount": 15
  },
  "channels": [
    {
      "category": "tasks-overdue",
      "label": "Overdue Tasks",
      "items": [
        {
          "id": "guid",
          "title": "Acme engagement letter review",
          "body": "Task is 2 days overdue",
          "priority": "high",
          "regardingName": "Acme Matter",
          "regardingEntityType": "sprk_matter",
          "regardingId": "guid",
          "createdOn": "2026-03-30T..."
        }
      ]
    }
  ]
}
```

**Response:**
```json
{
  "tldr": {
    "briefing": "5-7 sentence executive summary...",
    "topAction": "Complete the Acme engagement letter review",
    "generatedAtUtc": "..."
  },
  "channelNarratives": [
    {
      "category": "tasks-overdue",
      "bullets": [
        {
          "narrative": "The Acme engagement letter review is now 2 days overdue — originally due March 30.",
          "itemIds": ["guid1"],
          "primaryEntityType": "sprk_matter",
          "primaryEntityId": "guid",
          "primaryEntityName": "Acme Engagement Letter"
        },
        {
          "narrative": "Johnson matter compliance filing was due yesterday and is flagged as high priority.",
          "itemIds": ["guid2"],
          "primaryEntityType": "sprk_matter",
          "primaryEntityId": "guid",
          "primaryEntityName": "Johnson Compliance Filing"
        }
      ]
    }
  ]
}
```

**Implementation strategy**: Single BFF endpoint that makes parallel OpenAI calls internally:
- 1 call for TL;DR (expanded prompt, 5-7 sentences + top action)
- 1 call per active channel (batch all items, return narrative bullets)
- All calls fire in parallel via `Task.WhenAll`
- Total latency ≈ slowest single call (~1-2s), not sum

### Prompt Design (Channel Narration)

```
System: You are a concise legal operations assistant writing a daily briefing.

Task: Convert these notification items into 1-4 narrative bullet points.
- Group related items (e.g., multiple docs on same matter → single bullet)
- Write in natural prose, not templates
- Include entity names and relevant dates
- Lead with the most important/urgent item
- Be specific about counts when grouping
- Keep each bullet to 1-2 sentences max

Channel: {channelLabel}
Items: {JSON array of items}

Return JSON array of bullets, each with:
- narrative: string (the prose bullet)
- itemIds: string[] (which notification IDs this bullet covers)
- primaryEntityType: string (for hyperlink)
- primaryEntityId: string (for hyperlink)
- primaryEntityName: string (link display text)
```

---

## Component Changes

### Remove
- `ChannelCard.tsx` — replaced by flat narrative sections
- `NarrativeSummary.tsx` — merged into TL;DR section
- `NotificationItem.tsx` — replaced by `NarrativeBullet.tsx`
- Accordion import/usage

### Modify
- `App.tsx` — new layout: Header → TL;DR → Activity Notes → Caught-up footer
- `AiBriefing.tsx` → rename to `TldrSection.tsx` — expanded prompt, remove card chrome
- `DigestHeader.tsx` — add gear icon (right side), remove "Mark All Read" (moved per-item)
- `PreferencesPanel.tsx` → `PreferencesDropdown.tsx` — Fluent Popover anchored to gear icon
- `briefingService.ts` — call new `/narrate` endpoint instead of `/summarize`
- `useAiBriefing.ts` → `useBriefingNarration.ts` — handles both TL;DR and channel narratives
- `DailyBriefingEndpoints.cs` — new `/narrate` endpoint with parallel AI calls

### Create
- `ActivityNotesSection.tsx` — renders all channel headings + bullets
- `ChannelHeading.tsx` — channel icon + label + item count (simple heading, no accordion)
- `NarrativeBullet.tsx` — prose text + hyperlink + [✓ To Do] + [✕ Dismiss] actions
- `CaughtUpFooter.tsx` — "You're caught up on: {channels}" for zero-item channels

### Action Buttons per Bullet

| Button | Icon | Action | Visual |
|--------|------|--------|--------|
| Record link | `Open16Regular` | `Xrm.Navigation.navigateTo` target:2 dialog | Inline text link on entity name |
| Add to To Do | `Checkmark16Regular` | Quick-create `sprk_event` with `todoflag=true`, prefill from notification | Icon button, right side |
| Dismiss | `Dismiss16Regular` | Mark notification as read, fade out bullet | Icon button, right side |

### Quick-Create To Do (Inline)

When user clicks checkmark on a bullet:
1. Create `sprk_event` via `Xrm.WebApi.createRecord`:
   - `sprk_name` = notification title
   - `sprk_todoflag` = `true`
   - `sprk_description` = notification body
   - `sprk_regarding` = regarding entity lookup (if available)
   - `sprk_duedate` = tomorrow (default)
   - `sprk_priority` = map from notification priority
2. Show brief toast: "Added to To Do" (Fluent `Toast`)
3. Swap checkmark to filled checkmark (visual confirmation)
4. Mark original notification as read

No dialog — instant inline creation.

---

## Spacing & Visual Design (Fluent v9)

### Whitespace Guidelines

| Between | Token | Value |
|---------|-------|-------|
| Header ↔ TL;DR | `spacingVerticalXXL` | 24px |
| TL;DR ↔ Activity Notes | `spacingVerticalXXL` | 24px |
| Channel heading ↔ first bullet | `spacingVerticalM` | 12px |
| Bullet ↔ bullet | `spacingVerticalL` | 16px |
| Channel section ↔ channel section | `spacingVerticalXXL` | 24px |
| Activity Notes ↔ Caught-up footer | `spacingVerticalXXL` | 24px |

### Typography

| Element | Token | Style |
|---------|-------|-------|
| "TL;DR" heading | `fontSizeBase500` + `fontWeightSemibold` | 20px semibold |
| "Activity Notes" heading | `fontSizeBase500` + `fontWeightSemibold` | 20px semibold |
| Channel heading | `fontSizeBase400` + `fontWeightSemibold` | 16px semibold |
| Bullet narrative | `fontSizeBase300` + `fontWeightRegular` | 14px regular |
| Record link | `fontSizeBase300` + `colorBrandForeground1` | 14px brand blue |
| Item count badge | `fontSizeBase200` + `colorNeutralForeground3` | 12px subtle |

### Container

| Property | Token | Notes |
|----------|-------|-------|
| Page background | `colorNeutralBackground1` | Adapts to dark mode |
| TL;DR background | `colorNeutralBackground2` | Subtle elevation |
| TL;DR border radius | `borderRadiusLarge` | 8px |
| TL;DR padding | `spacingHorizontalXL` + `spacingVerticalL` | 20px H, 16px V |
| Channel heading bottom border | `colorNeutralStroke2` | Subtle separator |

---

## Preferences Dropdown

Gear icon in header toolbar → Fluent `Popover` with `PopoverSurface`:

```
┌──────────────────────────────────┐
│  Preferences                     │
├──────────────────────────────────┤
│                                  │
│  Channels                        │
│  ☑ Overdue Tasks                 │
│  ☑ Tasks Due Soon                │
│  ☑ New Documents                 │
│  ☑ New Emails                    │
│  ☑ Upcoming Events               │
│  ☑ Matter Activity               │
│  ☑ Work Assignments              │
│                                  │
│  Due window     [3 days ▾]       │
│  Recency        [24 hours ▾]     │
│  AI threshold   [75% ▾]         │
│                                  │
│  ☑ Auto-open on workspace launch │
│                                  │
└──────────────────────────────────┘
```

Width: ~280px. Positioned below gear icon, right-aligned. Changes persist immediately (same as current).

---

## Loading States

### Initial Load (before AI completes)

```
┌─────────────────────────────────────────────────────┐
│  🔔 Daily Briefing                    [⚙️]  [↻]    │
├─────────────────────────────────────────────────────┤
│                                                     │
│  TL;DR                                              │
│  ─────                                              │
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░            │  ← Skeleton shimmer
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░                     │
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░               │
│                                                     │
│  Activity Notes                                     │
│                                                     │
│  ░░░░░░░░░░░░░                                      │  ← Channel heading skeleton
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░            │
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░                     │
│                                                     │
│  ░░░░░░░░░░░░░                                      │
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░                │
│                                                     │
└─────────────────────────────────────────────────────┘
```

### Fallback (AI unavailable)

If AI narration fails, fall back to **template-based bullets** using the current `NarrativeSummary` logic for TL;DR and simple title/body for bullets. The layout stays the same — only the prose quality degrades.

---

## Migration Path

This can be done incrementally:

1. **Backend first**: Add `/narrate` endpoint (keeps `/summarize` working)
2. **New components**: Build `TldrSection`, `ActivityNotesSection`, `NarrativeBullet`, `PreferencesDropdown`
3. **Swap in App.tsx**: Replace old components with new ones
4. **Remove old**: Delete `ChannelCard`, `NarrativeSummary`, old `NotificationItem`
5. **Deploy**: Build + deploy `sprk_dailyupdate`

Estimated: 6-8 tasks, similar to the original Phase 4 build.
