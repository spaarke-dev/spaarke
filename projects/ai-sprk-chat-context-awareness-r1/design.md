# SprkChat Context Awareness

> **Project**: ai-sprk-chat-context-awareness-r1
> **Status**: Design
> **Priority**: 1 (Foundation — enables projects #2 and #3)
> **Branch**: work/ai-sprk-chat-context-awareness-r1

---

## Problem Statement

SprkChat currently opens with a hardcoded playbook GUID regardless of what Dataverse page the user is on. The AI assistant has no awareness of whether the user is viewing a matter list, editing an invoice form, or working in a dashboard. This means every conversation starts from the same generic starting point, requiring the user to manually explain their context.

The contextService infrastructure exists (Xrm frame-walk, entity detection, context-change polling, session persistence) but the playbook mapping is hardcoded in TypeScript — requiring a code deployment to change which AI capabilities appear in which context.

## Goals

1. **Data-driven playbook mapping** — Admins configure which playbooks appear in which Dataverse context through a form, not code
2. **Page-type awareness** — SprkChat distinguishes between entity list views, entity main forms, dashboards, and workspaces
3. **Multi-playbook contexts** — When multiple playbooks match a context, the user can choose between them
4. **Entity metadata enrichment** — The AI receives structured context about the current record (entity name, key fields) automatically
5. **Global mode** — When no entity context is detected, SprkChat works as a general-purpose assistant with full document search

## What Exists Today

### Client (SprkChatPane)
- `contextService.ts` — Detects entityType + entityId from URL params, Xrm.Page.data.entity, or Xrm.Utility.getPageContext()
- `contextService.ts` — Hardcoded `DEFAULT_PLAYBOOK_MAP` maps entity types to playbook GUIDs (all currently point to same GUID)
- `contextService.ts` — Context-change polling (2s interval) detects form navigation, triggers ContextSwitchDialog
- `App.tsx` — Passes `hostContext` (entityType, entityId) to SprkChat component
- `ContextSwitchDialog.tsx` — Modal asking user to switch or keep context when navigation detected

### Server (BFF API)
- `ChatEndpoints.cs` — `POST /api/ai/chat/sessions` accepts `HostContext` (entityType, entityId)
- `ChatEndpoints.cs` — `PATCH /api/ai/chat/sessions/{id}/context` switches document/playbook/hostContext mid-session
- `ChatEndpoints.cs` — `GET /api/ai/chat/playbooks` lists available playbooks (used by SprkChatContextSelector)
- `PlaybookChatContextProvider.cs` — Uses HostContext for entity-scoped RAG search
- `ChatSessionManager.cs` — HostContext flows through to Redis cache and Dataverse persistence

### Shared Library
- `SprkChat.tsx` — Accepts `hostContext`, `playbooks[]`, `playbookId` props
- `SprkChatContextSelector.tsx` — Dropdown for switching documents and playbooks (hidden when no options)
- `useChatPlaybooks.ts` — Fetches playbook list from BFF API
- `useChatSession.ts` — `switchContext()` calls PATCH endpoint

## Design

### Data Model: Context Mapping Table

New Dataverse entity: `sprk_aichatcontextmap`

| Field | Type | Description |
|-------|------|-------------|
| `sprk_name` | String | Display name (e.g., "Matter Form Assistant") |
| `sprk_entitytype` | String | Dataverse entity logical name (e.g., "sprk_matter"). Wildcard `*` = any entity |
| `sprk_pagetype` | Choice | Form (100000000), List (100000001), Dashboard (100000002), Workspace (100000003), Any (100000004) |
| `sprk_playbookid` | Lookup → sprk_aiplaybook | The playbook to activate in this context |
| `sprk_sortorder` | Integer | Priority (lower = higher priority, default 100) |
| `sprk_isdefault` | Boolean | Whether this is the auto-selected playbook for the context (vs. available in picker) |
| `sprk_description` | Multi-line Text | Admin-facing description of when this mapping applies |
| `sprk_isactive` | Boolean | Active/inactive toggle (statecode) |

**Resolution logic** (server-side):
```
Query: entityType={detected}, pageType={detected}
  1. Exact match: entityType + pageType
  2. Entity match: entityType + pageType=Any
  3. Wildcard match: entityType=* + pageType={detected}
  4. Global fallback: entityType=* + pageType=Any

  Within each tier: order by sprk_sortorder ASC
  Return: { defaultPlaybook, availablePlaybooks[] }
```

### BFF API Endpoint

```
GET /api/ai/chat/context-mappings?entityType=sprk_matter&pageType=form

Response:
{
  "defaultPlaybook": {
    "id": "5ece14f7-...",
    "name": "Matter Document Assistant",
    "description": "Analyze and search documents for this matter"
  },
  "availablePlaybooks": [
    { "id": "5ece14f7-...", "name": "Matter Document Assistant", "description": "...", "isDefault": true },
    { "id": "a1b2c3d4-...", "name": "Legal Research", "description": "...", "isDefault": false },
    { "id": "e5f6g7h8-...", "name": "Draft Legal Memo", "description": "...", "isDefault": false }
  ]
}
```

### Client Flow

```
SprkChatPane opens
  ↓
contextService.detectContext() → { entityType, entityId, pageType }
  ↓
GET /api/ai/chat/context-mappings?entityType={}&pageType={}
  ↓
Cache response in sessionStorage (key: "sprkchat-context-{entityType}-{pageType}")
  ↓
Auto-select defaultPlaybook → create session
  ↓
Populate SprkChatContextSelector with availablePlaybooks[]
  ↓
User can switch playbooks via dropdown (calls switchContext)
```

### Page-Type Detection

Extend `contextService.ts` to detect page type:

```typescript
type PageType = "form" | "list" | "dashboard" | "workspace" | "unknown";

function detectPageType(): PageType {
  const xrm = findXrm();
  if (!xrm) return "unknown";

  // Check Xrm.Utility.getPageContext() for page type
  const pageContext = xrm.Utility?.getPageContext?.();
  if (pageContext?.input?.pageType === "entityrecord") return "form";
  if (pageContext?.input?.pageType === "entitylist") return "list";
  if (pageContext?.input?.pageType === "dashboard") return "dashboard";

  // Fallback: check URL patterns
  const url = window.top?.location?.href ?? "";
  if (url.includes("/main.aspx?pagetype=entityrecord")) return "form";
  if (url.includes("/main.aspx?pagetype=entitylist")) return "list";
  if (url.includes("/main.aspx?pagetype=dashboard")) return "dashboard";

  // Check for workspace Code Page
  if (url.includes("webresourceName=sprk_")) return "workspace";

  return "unknown";
}
```

### Entity Metadata Enrichment

When on a form, pass additional entity metadata to the AI:

```typescript
// Extended HostContext
interface IHostContext {
  entityType: string;
  entityId: string;
  entityName?: string;        // Display name of the record (e.g., "Acme Corp v. Smith")
  workspaceType?: string;     // "analysis" | "corporate" | "legal"
  pageType?: string;          // "form" | "list" | "dashboard" | "workspace"
}
```

The BFF API's `PlaybookChatContextProvider` uses this to enrich the system prompt:
```
You are assisting with Matter "Acme Corp v. Smith" (sprk_matter).
The user is viewing the matter main form.
Available documents: [list from entity's document container]
```

## Phases

### Phase 1: Data-Driven Mapping (MVP)
- Create `sprk_aichatcontextmap` entity in Dataverse
- BFF API endpoint for context mapping resolution
- Update SprkChatPane to query mappings at startup (replace hardcoded map)
- Seed initial mapping data (sprk_matter → SprkChat Document Assistant)
- Page-type detection in contextService

### Phase 2: Multi-Playbook UX
- SprkChatContextSelector shows available playbooks for current context
- Playbook switching via dropdown (uses existing switchContext)
- Cache mappings in sessionStorage with TTL

### Phase 3: Entity Metadata Enrichment
- Extended HostContext with entityName, pageType
- BFF API system prompt enrichment from HostContext
- Entity-scoped document list in system prompt

### Phase 4: Admin Experience
- Model-driven form for `sprk_aichatcontextmap`
- Bulk seed utility for common entity types
- Validation: warn if entity type has no mapping

## Success Criteria

1. Admin can configure which playbooks appear for which entity/page context via Dataverse form
2. SprkChat auto-selects the correct playbook when opening on a matter form vs. matter list
3. User can switch between available playbooks for their current context
4. No code deployment required to add a new entity-to-playbook mapping
5. Existing hardcoded behavior works as fallback until mappings are configured

## Dependencies

- Existing `sprk_aiplaybook` table (playbooks must exist to be mapped)
- SprkChatPane deployed and working (completed in prior sprint)
- BFF API authentication working (completed — MSAL ssoSilent)

## Risks

- Xrm.Utility.getPageContext() may not be available in all UCI contexts (mitigation: URL pattern fallback)
- Side pane runs in an iframe — cross-origin restrictions may limit page-type detection (mitigation: frame-walk with try/catch)
- Too many Dataverse queries on pane open (mitigation: sessionStorage caching with 5-minute TTL)
