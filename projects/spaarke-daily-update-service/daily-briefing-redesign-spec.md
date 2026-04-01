# Daily Briefing Narrative Redesign - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-04-01
> **Source**: projects/spaarke-daily-update-service/notes/daily-briefing-redesign.md
> **Parent Project**: spaarke-daily-update-service (Phase 8: Redesign)

## Executive Summary

Redesign the Daily Briefing Code Page (`sprk_dailyupdate`) from a notification dashboard with collapsible accordion cards into a **narrative executive memo**. The briefing should read like a daily brief prepared by an executive assistant — a scrollable document with an AI-generated TL;DR summary at top, followed by AI-narrated Activity Notes organized by notification channel with inline actions (add to To Do, dismiss) and record hyperlinks.

## Scope

### In Scope
- Expand AI TL;DR section from 3-4 sentences to 5-7 sentences with top action callout
- Replace collapsible accordion channel cards with flat narrative Activity Notes sections
- AI batch-per-channel narration — one API call per active channel, returns prose bullets
- New BFF endpoint: `POST /api/ai/daily-briefing/narrate` (parallel AI calls internally)
- Inline "Add to To Do" action per bullet — creates `sprk_event` with `todoflag=true` from notification data
- "Dismiss" action per bullet (X icon) — marks notification as read
- Record hyperlinks in narrative bullets — opens entity in dialog via `Xrm.Navigation.navigateTo`
- Move preferences from collapsible bottom panel to gear icon dropdown (Fluent `Popover`) in header toolbar
- Generous whitespace between sections using Fluent v9 spacing tokens
- "You're caught up on: {channels}" footer for zero-item channels
- Toast notification on To Do creation ("Added to To Do")
- Template-based fallback when AI narration is unavailable
- Reuse existing `MicrosoftToDoIcon` (gray → brand blue) for Add to To Do — same pattern as Activity Feed event cards

### Out of Scope
- Changes to notification data fetching (`useNotificationData` hook — unchanged)
- Changes to notification playbooks or scheduler (backend notification generation)
- Changes to user preference Dataverse schema (`sprk_userpreference` — same entity/fields)
- Mobile-responsive layout (desktop dialog only)
- Notification grouping/threading beyond what AI narration provides
- Changes to the auto-popup hook (`useDailyDigestAutoPopup` — unchanged)

### Affected Areas
- `src/solutions/DailyBriefing/src/` — Frontend components, hooks, services (major changes)
- `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` — New `/narrate` endpoint
- `src/solutions/DailyBriefing/src/services/briefingService.ts` — Call new endpoint
- `src/solutions/LegalWorkspace/src/icons/MicrosoftToDoIcon.tsx` — Reuse (copy or extract to shared)

## Requirements

### Functional Requirements

1. **FR-01**: TL;DR Section — AI-generated 5-7 sentence executive summary displayed at top of briefing in a subtle card (`colorNeutralBackground2`). Must include a "top action" sentence identifying the single most important thing to do today. Must display "AI Insight" badge, generation timestamp, and category/item counts. Acceptance: TL;DR renders with 5-7 sentences, top action is identifiable, badge visible.

2. **FR-02**: Activity Notes — Flat, scrollable narrative sections below TL;DR. Each active notification channel renders as a heading (icon + label + item count) followed by AI-generated narrative bullets. No accordion/expand/collapse chrome. Acceptance: All active channels render as headings with narrative bullets, no collapsible UI.

3. **FR-03**: AI Batch-Per-Channel Narration — Each active channel's notification items are sent to the AI as a batch. The AI returns 1-4 narrative bullets per channel that group related items, prioritize by urgency, and reference entity names conversationally. Channels with 1 item still get AI narration for consistency. Acceptance: Each channel has AI-generated prose bullets (not raw notification titles).

4. **FR-04**: Record Hyperlinks — Each narrative bullet includes a clickable link to the primary related record. Link displays entity name, styled with `colorBrandForeground1`. Clicking opens the record in a Dataverse dialog via `Xrm.Navigation.navigateTo` with `target: 2`. Acceptance: Links render, clicking opens correct entity in dialog.

5. **FR-05**: Add to To Do (Inline Quick-Create) — Each bullet has a `MicrosoftToDoIcon` button (gray → brand blue on toggle). Clicking creates a new `sprk_event` record via `Xrm.WebApi.createRecord` with: `sprk_name` = notification title, `sprk_todoflag = true`, `sprk_todosource = 100000001` (User), `sprk_description` = notification body, regarding entity lookup (if available), `sprk_priority` mapped from notification priority. Shows Fluent `Toast` notification ("Added to To Do"). Icon transitions from gray to brand blue (`tokens.colorBrandForeground1/2`) on success. Acceptance: Clicking checkmark creates sprk_event, toast appears, icon turns blue.

6. **FR-06**: Dismiss Action — Each bullet has a Dismiss button (X icon, `Dismiss16Regular`). Clicking marks the underlying `appnotification` record as read. Bullet fades out or is removed from view. Acceptance: X click marks notification read, bullet visually dismissed.

7. **FR-07**: Preferences Dropdown — Gear icon (`Settings20Regular`) in header toolbar, right-aligned. Opens Fluent `Popover` (~280px wide) anchored below gear icon. Contains: channel toggles (switches), due window dropdown, recency dropdown, AI threshold dropdown, auto-popup toggle. Changes persist immediately to `sprk_userpreference`. Acceptance: Gear opens popover, changes persist, popover dismisses on outside click.

8. **FR-08**: Caught-Up Footer — Below all Activity Notes sections, display "You're caught up on: {channel1}, {channel2}, {channel3}" listing channels with zero items. Gives explicit "nothing to do" signal. Acceptance: Footer renders listing zero-item channels.

9. **FR-09**: Template Fallback — When AI narration is unavailable (503, circuit breaker, rate limit), fall back to template-based bullets using notification title + body + regarding name. TL;DR falls back to existing `NarrativeSummary` template logic. Layout remains identical — only prose quality degrades. Acceptance: Briefing renders correctly when AI is down, using templates.

### Non-Functional Requirements

- **NFR-01**: AI narration latency < 3 seconds end-to-end (parallel `Task.WhenAll` for all channel calls + TL;DR call). Individual channel call target: < 2 seconds.
- **NFR-02**: Total AI cost per briefing < $0.03 (TL;DR + 7-8 channel narrations at ~300 tokens each).
- **NFR-03**: All styling uses Fluent UI v9 semantic tokens — no hard-coded colors. Dark mode must work automatically via token system.
- **NFR-04**: Add to To Do action must feel instant — optimistic UI update, Dataverse write async. Same debounce pattern as FeedTodoSyncContext (300ms).

## Technical Constraints

### Applicable ADRs
- **ADR-001**: BFF Minimal API — new `/narrate` endpoint must be in Sprk.Bff.Api, not a separate service
- **ADR-006**: Code Page pattern — DailyBriefing is React 18+ Vite SPA, not PCF
- **ADR-010**: DI minimalism — any new services must fit within feature module registration
- **ADR-012**: Shared component library — reuse `@spaarke/ui-components` where applicable
- **ADR-013**: AI extends BFF — all AI calls go through BFF endpoint, not direct from client
- **ADR-021**: Fluent UI v9 exclusively, semantic tokens, dark mode required

### MUST Rules
- MUST use `POST /api/ai/daily-briefing/narrate` as single BFF endpoint for both TL;DR and channel narrations
- MUST fire all AI calls in parallel via `Task.WhenAll` (not sequential)
- MUST return structured response with entity references for hyperlink generation
- MUST use `MicrosoftToDoIcon` with same gray → brand blue pattern as Activity Feed cards
- MUST create `sprk_event` with `sprk_todoflag=true` and `sprk_todosource=100000001` for To Do creation
- MUST use Fluent `Toast` for To Do confirmation feedback
- MUST use Fluent `Popover` for preferences (not Dialog or Panel)
- MUST preserve `/summarize` endpoint during migration (backward compatible)
- MUST fall back to template-based rendering when AI is unavailable
- MUST NOT use hard-coded colors — all via Fluent v9 tokens
- MUST NOT use accordion/collapsible patterns in the new design

### Existing Patterns to Follow
- **Add to To Do**: `src/solutions/LegalWorkspace/src/components/ActivityFeed/FeedItemCard.tsx` (lines 435-729) — icon toggle, optimistic update, debounce, error rollback
- **MicrosoftToDoIcon**: `src/solutions/LegalWorkspace/src/icons/MicrosoftToDoIcon.tsx` — SVG checkmark with active/inactive color states
- **FeedTodoSyncContext**: `src/solutions/LegalWorkspace/src/contexts/FeedTodoSyncContext.tsx` — optimistic toggle, 300ms debounce, rollback on failure
- **DataverseService.toggleTodoFlag()**: `src/solutions/LegalWorkspace/src/services/DataverseService.ts` (lines 497-510)
- **Dialog navigation**: `Xrm.Navigation.navigateTo` with `target: 2` — used throughout WorkspaceGrid.tsx
- **Auth bootstrap**: `src/solutions/DailyBriefing/src/main.tsx` — `resolveRuntimeConfig` + `ensureAuthInitialized`
- **Authenticated BFF calls**: `src/solutions/DailyBriefing/src/services/authInit.ts` — `authenticatedFetch`

## Architecture

### New BFF Endpoint

```
POST /api/ai/daily-briefing/narrate
Authorization: Required
Rate Limiting: ai-batch
```

**Request DTO:**
```csharp
public record DailyBriefingNarrateRequest
{
    // TL;DR input (same shape as existing summarize)
    public NotificationCategoryDto[] Categories { get; init; }
    public PriorityItemDto[] PriorityItems { get; init; }
    public int TotalNotificationCount { get; init; }

    // Channel narration input (new)
    public ChannelNarrationInput[] Channels { get; init; }
}

public record ChannelNarrationInput
{
    public string Category { get; init; }      // e.g., "tasks-overdue"
    public string Label { get; init; }          // e.g., "Overdue Tasks"
    public ChannelItemDto[] Items { get; init; }
}

public record ChannelItemDto
{
    public string Id { get; init; }
    public string Title { get; init; }
    public string Body { get; init; }
    public string Priority { get; init; }
    public string RegardingName { get; init; }
    public string RegardingEntityType { get; init; }
    public string RegardingId { get; init; }
    public string CreatedOn { get; init; }
}
```

**Response DTO:**
```csharp
public record DailyBriefingNarrateResponse
{
    public TldrResult Tldr { get; init; }
    public ChannelNarrationResult[] ChannelNarratives { get; init; }
    public DateTimeOffset GeneratedAtUtc { get; init; }
}

public record TldrResult
{
    public string Briefing { get; init; }       // 5-7 sentence summary
    public string TopAction { get; init; }      // Single most important action
    public int CategoryCount { get; init; }
    public int PriorityItemCount { get; init; }
}

public record ChannelNarrationResult
{
    public string Category { get; init; }
    public NarrativeBullet[] Bullets { get; init; }
}

public record NarrativeBullet
{
    public string Narrative { get; init; }              // Prose text
    public string[] ItemIds { get; init; }              // Which notification IDs this covers
    public string PrimaryEntityType { get; init; }      // For hyperlink
    public string PrimaryEntityId { get; init; }        // For hyperlink
    public string PrimaryEntityName { get; init; }      // Link display text
}
```

**Internal implementation:**
- Parse request
- Fire TL;DR prompt + N channel prompts via `Task.WhenAll` (all parallel)
- TL;DR prompt: expanded to request 5-7 sentences + top action identification
- Channel prompt: "Convert these items into 1-4 narrative bullets, group related items, return structured JSON"
- Catch `OpenAiCircuitBrokenException` per-call — return template fallback for failed channels
- Return combined response

### Frontend Component Architecture

**Remove:**
- `ChannelCard.tsx` — replaced by flat narrative sections
- `NarrativeSummary.tsx` — merged into TL;DR section
- `NotificationItem.tsx` — replaced by NarrativeBullet component

**Modify:**
- `App.tsx` — new layout: Header → TL;DR → Activity Notes → Caught-Up Footer
- `AiBriefing.tsx` → rename to `TldrSection.tsx` — 5-7 sentences, top action, remove card chrome, keep AI badge
- `DigestHeader.tsx` — remove "Mark All Read" button, add gear icon (right side)
- `PreferencesPanel.tsx` → `PreferencesDropdown.tsx` — Fluent `Popover`, ~280px, anchored to gear icon
- `briefingService.ts` — call `/narrate` instead of `/summarize`, handle new response shape
- `useAiBriefing.ts` → `useBriefingNarration.ts` — returns TL;DR + channel narratives

**Create:**
- `ActivityNotesSection.tsx` — renders all channel headings + bullets
- `ChannelHeading.tsx` — channel icon + label + item count (simple heading, not accordion)
- `NarrativeBullet.tsx` — prose text + record hyperlink + [To Do] + [Dismiss] action buttons
- `CaughtUpFooter.tsx` — "You're caught up on: {channels}" for zero-item channels
- `useInlineTodoCreate.ts` — hook for creating sprk_event from notification data (quick-create pattern)
- `MicrosoftToDoIcon.tsx` — copy from LegalWorkspace (or extract to shared library)

### Spacing & Typography

| Between | Fluent Token | Value |
|---------|-------------|-------|
| Header ↔ TL;DR | `spacingVerticalXXL` | 24px |
| TL;DR ↔ Activity Notes | `spacingVerticalXXL` | 24px |
| Channel heading ↔ first bullet | `spacingVerticalM` | 12px |
| Bullet ↔ bullet | `spacingVerticalL` | 16px |
| Channel section ↔ channel section | `spacingVerticalXXL` | 24px |
| Activity Notes ↔ Caught-up footer | `spacingVerticalXXL` | 24px |

| Element | Font Token | Weight Token |
|---------|-----------|-------------|
| "TL;DR" heading | `fontSizeBase500` (20px) | `fontWeightSemibold` |
| "Activity Notes" heading | `fontSizeBase500` (20px) | `fontWeightSemibold` |
| Channel heading | `fontSizeBase400` (16px) | `fontWeightSemibold` |
| Bullet narrative | `fontSizeBase300` (14px) | `fontWeightRegular` |
| Record link | `fontSizeBase300` (14px) | `colorBrandForeground1` |
| Item count | `fontSizeBase200` (12px) | `colorNeutralForeground3` |

### Add to To Do: Quick-Create Pattern

**Difference from Activity Feed pattern**: Activity Feed toggles `sprk_todoflag` on an existing `sprk_event`. Daily Briefing notifications are `appnotification` records — must **create** a new `sprk_event` from notification data.

**Quick-create flow:**
1. User clicks `MicrosoftToDoIcon` on a bullet
2. Icon immediately transitions gray → brand blue (optimistic)
3. `Xrm.WebApi.createRecord('sprk_event', { ... })` fires async:
   - `sprk_name`: notification title
   - `sprk_todoflag`: `true`
   - `sprk_todosource`: `100000001` (User)
   - `sprk_description`: notification body
   - `sprk_priority`: mapped from notification priority (high → 100000001, urgent → 100000000)
   - `sprk_Regarding@odata.bind`: `/{entitySetName}({regardingId})` (if available)
4. On success: show Fluent `Toast` ("Added to To Do"), mark notification as read
5. On failure: rollback icon to gray, show error toast

**Reuse**: `MicrosoftToDoIcon` component (same SVG, same color tokens). New `useInlineTodoCreate` hook for the create-from-notification pattern (distinct from `FeedTodoSyncContext` which toggles existing events).

## Success Criteria

1. [ ] TL;DR renders 5-7 sentence AI summary with top action callout — Verify: visual inspection, count sentences
2. [ ] Activity Notes render flat narrative bullets per channel (no accordion) — Verify: visual inspection, no expand/collapse UI
3. [ ] Record hyperlinks open correct entity in dialog — Verify: click link, entity form opens
4. [ ] Add to To Do creates sprk_event with correct fields, icon turns blue, toast appears — Verify: click checkmark, check Dataverse, verify toast
5. [ ] Dismiss (X) marks notification read and removes bullet — Verify: click X, check appnotification.isRead
6. [ ] Preferences gear icon opens popover dropdown, changes persist — Verify: click gear, toggle setting, refresh, setting retained
7. [ ] Fallback renders template bullets when AI unavailable — Verify: simulate 503, verify briefing still renders
8. [ ] Dark mode renders correctly — Verify: toggle dark mode, all elements use semantic tokens
9. [ ] End-to-end latency < 3 seconds for full narration — Verify: measure from page load to all bullets rendered

## Dependencies

### Prerequisites
- Existing `sprk_dailyupdate` web resource deployed (done ✅)
- Existing `/api/ai/daily-briefing/summarize` endpoint working (done ✅)
- Existing notification data hooks (`useNotificationData`) working (done ✅)
- `MicrosoftToDoIcon` component available (exists in LegalWorkspace ✅)

### External Dependencies
- Azure OpenAI service available for narration calls
- `appnotification` entity populated by notification playbooks

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| To Do feedback | Toast + icon state, or icon only? | Both: toast notification + icon gray→blue | Need Fluent Toast provider in component tree |
| To Do icon | Which icon/pattern? | Same `MicrosoftToDoIcon` as Activity Feed event cards (gray→brand blue) | Reuse existing SVG component, same color tokens |
| Single-item channels | AI or template for 1-item channels? | AI for all — consistency over marginal savings | All channels go through AI batch endpoint |
| Empty channels | Show or omit? | Show "You're caught up on: {channels}" footer | Implement CaughtUpFooter component |

## Assumptions

- **Entity set name resolution**: Assuming `regardingEntityType` from notification data maps directly to Dataverse entity set names (e.g., `sprk_matter` → `sprk_matters`). Will need a small lookup map if not.
- **Toast provider**: Assuming Fluent `Toaster` / `useToastController` can be added to the DailyBriefing component tree without conflicts.
- **Notification mark-as-read**: Assuming `appnotification` can be updated via `Xrm.WebApi.updateRecord` to mark as read (standard Dataverse entity).

## Unresolved Questions

- None — all blocking questions resolved in design discussion.

## Migration Path

This can be implemented incrementally while keeping the existing briefing functional:

1. **Backend first**: Add `/narrate` endpoint (keeps `/summarize` working)
2. **New components**: Build TldrSection, ActivityNotesSection, NarrativeBullet, PreferencesDropdown, CaughtUpFooter, useInlineTodoCreate
3. **Swap in App.tsx**: Replace old components with new ones
4. **Remove old**: Delete ChannelCard, NarrativeSummary, old NotificationItem
5. **Build + Deploy**: Vite build → deploy `sprk_dailyupdate`

Estimated: 8-10 tasks.

---

*AI-optimized specification. Original design: projects/spaarke-daily-update-service/notes/daily-briefing-redesign.md*
