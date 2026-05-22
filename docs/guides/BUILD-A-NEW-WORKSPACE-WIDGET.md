# Build a New Workspace Widget

> **Purpose**: Step-by-step tutorial for adding a new system workspace (or extending the existing pipeline). Walks through the file edits, the Dataverse seed update, the BFF impact analysis, and the deploy sequence. Includes a worked example: the **Calendar widget** the operator has requested for a future round.
>
> **Last reviewed**: 2026-05-22 (Task 113). Periodic review required.

> **Required reading before starting**:
> - [`../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — pipeline reference
> - [`../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — known coupling that affects new-widget design choices
> - [`CLAUDE.md` §10 — BFF Hygiene](../../CLAUDE.md) — binding rules if you add ANY BFF code
> - [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — required pre-merge checklist for BFF additions

---

## 1. Decision tree: which pattern fits your new widget?

Before writing code, classify the new feature:

```
Is the new widget a "workspace" — i.e. one or more sections inside the workspace pipeline?
├── YES → Pattern A: Section factory + Dataverse layout (RECOMMENDED for most cases)
│         Examples: Daily Briefing, My Work, Documents, Smart To Do List, Calendar
│
└── NO  → Is it a one-off Code Page dispatcher (open an existing wizard) or AI output?
          ├── Code Page dispatcher → Pattern B: register a new workspace widget type with a
          │   thin wrapper that calls Xrm.Navigation.navigateTo
          │   Examples: create-project-wizard, find-similar-wizard
          │
          └── AI output (chat-driven) → Pattern C: register a new workspace widget type with
              a React component that renders the AI tool's output
              Examples: RedlineViewer, AnalysisEditor
```

**This guide focuses on Pattern A** — the operator's "Calendar" request is the worked example. Patterns B and C follow simpler paths in `register-workspace-widgets.ts` and are not unique to this guide.

---

## 2. Pattern A: Adding a new system workspace section

### Steps overview

1. Decide whether the section needs new components, or whether an existing section's content suffices.
2. Implement the section component(s) inside LegalWorkspace.
3. Create the `<sectionName>.registration.ts` file in `src/solutions/LegalWorkspace/src/sections/`.
4. Add the registration to `src/solutions/LegalWorkspace/src/sectionRegistry.ts`.
5. Add the layout to `scripts/system-layouts.json` (or design a multi-section layout).
6. Run `scripts/Deploy-SystemWorkspaceLayouts.ps1` to seed the layout in Dataverse.
7. Verify cold load in dev (operator smoke).
8. Deploy `LegalWorkspace` web resource via `code-page-deploy` skill.

NO BFF code changes are required IF:
- The section reads data only via `Xrm.WebApi` (Dataverse-side).
- The section does not need new BFF endpoints for cross-tenant data, AI grounding, or SharePoint Embedded operations.

If the section needs new BFF endpoints, you MUST follow CLAUDE.md §10 (BFF Hygiene) and load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before designing the addition.

### Step 1 — Decide section scope

Examples of section scope:
- **Single-data-source section** (e.g. todo): one Dataverse entity, one query pattern. Maps cleanly to one `ContentSectionConfig` with one query hook.
- **Multi-card aggregation section** (e.g. quick-summary): N independent counts, presented as a card grid. Maps to a `MetricCardSectionConfig` (or a `ContentSectionConfig` if cards are interactive).
- **AI-curated section** (e.g. daily-briefing): server-side analysis returned by a BFF endpoint. Maps to `ContentSectionConfig` with a TTL-cached fetch hook.

### Step 2 — Implement section components

Place under `src/solutions/LegalWorkspace/src/components/<SectionName>/`. Required for the widget to be data-correct in BOTH embedded (SpaarkeAi tab) and standalone (LegalWorkspace) hosts:

- Use the `webApi` + `userId` props passed via `SectionFactoryContext` — NEVER reach for the Xrm global directly.
- Use the `scope` + `businessUnitId` context if the section needs "my records" vs "all in my BU" filtering.
- Honor `context.onNavigate`, `context.onOpenWizard`, `context.onBadgeCountChange`, `context.onRefetchReady` callbacks for cross-section UX.

Reference the existing `quickSummary.registration.ts` (single content section with custom toolbar + 6-card grid) or `documents.registration.ts` (single content section with a per-card grid + view picker).

### Step 3 — Create the registration file

File: `src/solutions/LegalWorkspace/src/sections/<sectionName>.registration.ts`.

```ts
import * as React from "react";
import { CalendarRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { CalendarSection } from "../components/Calendar/CalendarSection";

export const calendarRegistration: SectionRegistration = {
  id: "calendar",                          // UNIQUE — must not clash with existing IDs
  label: "Calendar",                       // shown in wizard step 2
  description: "Upcoming events and deadlines",
  icon: CalendarRegular,
  category: "productivity",                // overview | data | ai | productivity
  defaultHeight: "440px",

  factory(context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "calendar",
      type: "content",
      title: "Calendar",
      style: {},
      renderContent: () =>
        React.createElement(CalendarSection, {
          webApi: context.webApi as any,
          userId: context.userId,
          scope: context.scope,
          businessUnitId: context.businessUnitId,
        }),
    };
  },
};

export default calendarRegistration;
```

### Step 4 — Register in `sectionRegistry.ts`

```ts
import { calendarRegistration } from "./sections/calendar.registration";

export const SECTION_REGISTRY: readonly SectionRegistration[] = [
  getStartedRegistration,
  quickSummaryRegistration,
  latestUpdatesRegistration,
  todoRegistration,
  documentsRegistration,
  dailyBriefingRegistration,
  calendarRegistration,                  // NEW
] as const;
```

The dev-mode guard at the bottom of `sectionRegistry.ts` will alert in console if you accidentally duplicate an ID.

### Step 5 — Add the system layout

Open `scripts/system-layouts.json` and add a new entry:

```json
{
  "name": "Calendar",
  "sectionId": "calendar",
  "layoutTemplateId": "single-column",
  "sortOrder": 4,
  "isDefault": false,
  "description": "Upcoming events and deadlines. Operator request 2026-MM-DD."
}
```

- `name`: appears in the Workspaces dropdown.
- `sectionId`: must match the registration `id` above.
- `layoutTemplateId`: typically `single-column` for a single-section workspace. Use `3-row-mixed` for the Corporate Workspace pattern.
- `sortOrder`: position among the Dataverse-system layouts (Daily Briefing=0, todo=1, quick-summary=2, documents=3, calendar=4).
- `isDefault`: set TRUE for ONLY ONE layout. Setting two will pick the lowest-sortOrder one (the BFF's `GetDefaultLayoutAsync` Step 2 orders by sortOrder ASC).

### Step 6 — Seed the Dataverse record

Run the seed script:

```pwsh
pwsh scripts/Deploy-SystemWorkspaceLayouts.ps1 -EnvironmentUrl <DataverseUrl>
```

The script reads `system-layouts.json` and upserts each layout via the `sprk_workspacelayout` Web API with `sprk_issystem=true`. The user running the script becomes the `ownerid`, but this does NOT gate visibility — system records are visible to all authenticated users.

Verify via:

```pwsh
pwsh scripts/Verify-SystemWorkspaceLayouts.ps1 -EnvironmentUrl <DataverseUrl>
```

(If a verify script doesn't exist for your environment, query Dataverse directly: `GET /api/data/v9.2/sprk_workspacelayouts?$filter=sprk_issystem eq true&$select=sprk_name,sprk_sortorder,sprk_isdefault`.)

### Step 7 — Verify cold load in dev

After seeding, restart SpaarkeAi (or hit `localStorage.removeItem('lw-layout-cache')` + refresh):

1. The Workspaces dropdown should show **Calendar** in the system section (between Documents and any user layouts).
2. Click **Calendar** → a new tab opens with title "Calendar".
3. The tab renders the embedded LegalWorkspaceApp with `initialWorkspaceId` set to the new layout's GUID.
4. `WorkspaceGrid` calls LegalWorkspace's `useWorkspaceLayouts`, parses the sectionsJson, and resolves `calendar` from `SECTION_REGISTRY`.
5. The `CalendarSection` component renders.

Standalone test (in a SEPARATE browser tab):
1. Open the LegalWorkspace web resource directly.
2. Switch to **Calendar** via LegalWorkspace's internal workspace dropdown.
3. Verify byte-identical rendering with embedded mode (FR-25 / NFR-10 invariant — see audit §5).

### Step 8 — Deploy

Use the appropriate deploy skill:

- **LegalWorkspace web resource**: `code-page-deploy` skill (or `Deploy-LegalWorkspace.ps1`)
- **SpaarkeAi web resource**: `code-page-deploy` skill (or `Deploy-SpaarkeAi.ps1`) — only needed if SpaarkeAi code changed, which Pattern A does NOT require
- **System layouts seed**: `scripts/Deploy-SystemWorkspaceLayouts.ps1` per environment

---

## 3. Pattern B — New BFF endpoint (if your section needs server-side data)

**STOP and read CLAUDE.md §10 first.** Then load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md). Then:

1. Justify placement in your design doc's **Placement Justification** section (mandatory).
2. Verify publish-size impact stays under the baseline 60 MB compressed.
3. Verify no new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive`.
4. Endpoint goes in an existing endpoint group if topical (`WorkspaceFileEndpoints`, `WorkspaceAiEndpoints`, ...) or a NEW endpoint group registered in `Program.cs`.
5. Use `RequireAuthorization()` (the `/api/workspace` group already does this).
6. Service goes in `Services/<Area>/` as a concrete type registered Scoped (ADR-010 — no interface unless multiple impls).

The decision criteria from CLAUDE.md §10 boil down to: does this CRUD code need to inject AI types directly? If yes, route through the `Services/Ai/PublicContracts/` facade. Do NOT inject `IOpenAiClient`, `IPlaybookService`, or other AI-internal types.

---

## 4. Pattern B + C — New widget type (Code Page dispatcher OR AI output)

For non-workspace widgets, edit `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts`:

```ts
registerWorkspaceWidget(
  'my-new-widget',                        // type string — must match server / dispatcher
  {
    displayName: 'My New Widget',
    category: 'wizard',                   // or 'ai' / 'document' / etc.
    icon: 'CalendarRegular',
    allowMultiple: true,
    defaultOrder: 150,                    // append after existing entries
  },
  () => import('./MyNewWidget').then((m) => ({ default: m.MyNewWidget as WorkspaceWidgetComponent })),
);
```

Then implement `MyNewWidget.tsx` as a `WorkspaceWidgetComponent`:

```tsx
export const MyNewWidget: WorkspaceWidgetComponent<MyData> = ({ data }) => {
  // ... render ...
};
MyNewWidget.displayName = 'MyNewWidget';
```

Pattern B (Code Page dispatcher) — body of MyNewWidget calls `Xrm.Navigation.navigateTo({ pageType: 'webresource', webresourceName: 'sprk_mywizard', ... })` and renders a launch/relaunch button.

Pattern C (AI output) — body of MyNewWidget renders the AI tool's output payload (passed via `data`).

---

## 5. Worked example: Calendar widget

**Operator request (anticipated future round)**: "Add a Calendar system workspace that shows upcoming events and deadlines, with a per-card open affordance and a 'view all' link to the full calendar."

### 5.1 Scope decision (Step 1)

Single-section workspace, single Dataverse data source (`sprk_workitem` or similar event entity). Maps to Pattern A.

### 5.2 Component implementation (Step 2)

New files:
- `src/solutions/LegalWorkspace/src/components/Calendar/CalendarSection.tsx`
- `src/solutions/LegalWorkspace/src/components/Calendar/CalendarItemCard.tsx`
- `src/solutions/LegalWorkspace/src/hooks/useCalendarEvents.ts` (Xrm.WebApi fetch hook with scope-aware filter)

The `CalendarSection` accepts `webApi`, `userId`, `scope`, `businessUnitId` props and renders a chronological list of upcoming events. Each `CalendarItemCard` shows date/time, title, related entity, and an Open button.

### 5.3 Registration (Steps 3 + 4)

Create `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` per the template in §2.

Add to `sectionRegistry.ts`:

```ts
import { calendarRegistration } from "./sections/calendar.registration";

export const SECTION_REGISTRY: readonly SectionRegistration[] = [
  getStartedRegistration,
  quickSummaryRegistration,
  latestUpdatesRegistration,
  todoRegistration,
  documentsRegistration,
  dailyBriefingRegistration,
  calendarRegistration,                  // NEW
] as const;
```

### 5.4 Layout seed (Steps 5 + 6)

Add to `scripts/system-layouts.json`:

```json
{
  "name": "Calendar",
  "sectionId": "calendar",
  "layoutTemplateId": "single-column",
  "sortOrder": 4,
  "isDefault": false,
  "description": "Upcoming events and deadlines."
}
```

Run `pwsh scripts/Deploy-SystemWorkspaceLayouts.ps1 -EnvironmentUrl <url>`.

### 5.5 Verification (Step 7)

The Workspaces dropdown should now show `Daily Briefing | Smart To Do List | My Work | Documents | Calendar` in the system section, plus any user layouts.

Click **Calendar** → tab opens with title "Calendar" → `LegalWorkspaceApp(embedded, initialWorkspaceId=<calendarLayoutId>)` mounts → `WorkspaceGrid` resolves sectionsJson `["calendar"]` → factory returns `ContentSectionConfig` for "Calendar" → `WorkspaceShell` renders the section → `CalendarSection` component fetches events via `useCalendarEvents(webApi, userId, scope, businessUnitId)` → cards render.

### 5.6 What changes are NOT required

- **NO** changes to SpaarkeAi source code — the existing `workspace` widget type covers every new layout.
- **NO** changes to BFF — the existing `WorkspaceLayoutService.GetLayoutsAsync` already includes ALL `sprk_issystem=true` records via `QueryDataverseSystemLayoutsAsync`.
- **NO** changes to `@spaarke/ai-widgets` — `WorkspaceLayoutWidget` is unchanged.
- **NO** new auth wiring — `useAuth` + `authenticatedFetch` + `Xrm.WebApi` already in place.
- **NO** new ADRs — Pattern A is the established pattern.

### 5.7 What IS NOT clean about this (operator's audit question, honest answer)

- **The section factory lives inside LegalWorkspace.** If you wanted to build a Calendar widget WITHOUT touching LegalWorkspace (e.g. embed it in a different host like Outlook), today's only path is to embed `LegalWorkspaceApp` again or duplicate the section factory. See [`SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) §2.
- **Xrm.WebApi vs BFF**: the new `useCalendarEvents` hook uses Xrm.WebApi (consistent with QuickSummary). If the section ever needs aggregation across tenants or AI grounding, it would shift to a new BFF endpoint — but the decision criteria for "when to add a BFF call vs use Xrm.WebApi" is NOT documented today. See audit §4.

---

## 6. Cheat sheet — file edit list for the Calendar example

| File | Action | Notes |
|---|---|---|
| `src/solutions/LegalWorkspace/src/components/Calendar/CalendarSection.tsx` | CREATE | Section component |
| `src/solutions/LegalWorkspace/src/components/Calendar/CalendarItemCard.tsx` | CREATE | Card component |
| `src/solutions/LegalWorkspace/src/hooks/useCalendarEvents.ts` | CREATE | Xrm.WebApi fetch hook |
| `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` | CREATE | `SectionRegistration` factory |
| `src/solutions/LegalWorkspace/src/sectionRegistry.ts` | EDIT | Add `calendarRegistration` import + entry |
| `scripts/system-layouts.json` | EDIT | Add Calendar layout entry |
| (run) `scripts/Deploy-SystemWorkspaceLayouts.ps1` | RUN | Seed Dataverse |
| (deploy) `code-page-deploy` for LegalWorkspace | RUN | Push LegalWorkspace web resource |

Total: 5 new files + 2 edits + 2 commands. NO SpaarkeAi changes. NO BFF changes. NO new packages.

---

## 7. Common pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Forgot to add to `SECTION_REGISTRY` | Tab opens but section doesn't render | Dev-mode console warns about unknown section ID — check `sectionRegistry.ts` |
| Duplicate section ID | Dev-mode `console.error` at load | Pick a unique ID |
| `sortOrder` collision with existing system layouts | Layout appears in unexpected dropdown position | Verify all 4 existing `sortOrder` values (0..3) and pick the next free integer |
| Tried to set TWO layouts `isDefault: true` | Only one appears as default; other behaves as non-default | Only ONE Dataverse-system layout can be the global default |
| Section needs auth-bearing fetch from BFF | Uses `webApi` instead — works in MDA, but fails when embedded in a future non-Xrm host | Use `authenticatedFetch` from a new hook; pass `bffBaseUrl` from `SectionFactoryContext` |
| Forgot to seed the layout | Workspaces dropdown doesn't show the new entry | Run `Deploy-SystemWorkspaceLayouts.ps1`; verify with the API query in §2.6 |
| Section behaves differently in embedded vs standalone | Section uses LegalWorkspace-internal context (e.g. FeedTodoSyncContext) that doesn't exist if its provider isn't mounted | Only depend on `SectionFactoryContext`; if you need extra context, hoist its provider into `WorkspaceShell` |

---

## 8. Related docs

- [`../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — pipeline reference
- [`../architecture/SPAARKEAI-COMPONENT-MODEL.md`](../architecture/SPAARKEAI-COMPONENT-MODEL.md) — component inventory
- [`../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — coupling + reuse gaps
- [`SHARED-UI-COMPONENTS-GUIDE.md`](./SHARED-UI-COMPONENTS-GUIDE.md) — `@spaarke/ui-components` consumption
- [`PCF-DEPLOYMENT-GUIDE.md`](./PCF-DEPLOYMENT-GUIDE.md) — Code Page deploy reference (the section about `Deploy-SpaarkeAi.ps1` + `Deploy-LegalWorkspace.ps1`)
