# SprkChat Context Awareness

> **Project**: ai-sprk-chat-context-awareness-r1
> **Status**: Design
> **Priority**: 1 (Foundation — enables projects #2 and #3)
> **Branch**: work/ai-sprk-chat-context-awareness-r1
> **Last Reviewed**: 2026-03-14 (aligned with production-performance-improvement-r1 worktree)

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

## Non-Negotiable Performance & UX Standards

> These constraints apply to ALL SprkChat work, not just this project. They are documented here because this project introduces new pre-session latency (context-mapping lookup) that could undermine the experience.

### SSE — All Chat Interactions Must Stream

Users must see the first token within 1-2 seconds. Any chat endpoint that returns a complete response body (REST) instead of streaming is a regression.

**Rule**: Every endpoint that generates AI content MUST use SSE. Pre-session setup operations (context-mapping lookup, session creation) are the only REST-appropriate chat endpoints.

| Endpoint | Protocol | Reason |
|----------|----------|--------|
| `GET /api/ai/chat/context-mappings` | REST | Config lookup, not AI generation — must be <100ms |
| `POST /api/ai/chat/sessions` | REST | Session creation, no AI involved |
| `POST /api/ai/chat/sessions/{id}/messages` | SSE ✅ | AI generation — first token must stream immediately |
| `POST /api/ai/chat/sessions/{id}/refine` | SSE ✅ | AI generation — streams incrementally |
| `GET /api/ai/chat/sessions/{id}/history` | REST | Data retrieval, no AI |
| `PATCH /api/ai/chat/sessions/{id}/context` | REST | Context switch metadata update |

**Client-side enforcement**: The `useChatSession` hook must never `await` a full response for message sends. Any refactoring of chat hooks must preserve the SSE `EventSource`/`ReadableStream` consumption pattern. REST fallback for AI generation is not acceptable.

**Audit note**: Known non-streaming endpoints were identified and are being addressed separately. This project must not introduce any new ones and must not regress existing SSE paths when wiring context-mapping data into the session creation flow.

### Redis — Pre-Session Latency Must Be Near Zero

Users benchmark SprkChat against Claude.ai and ChatGPT. The pane-open experience (before the first message) sets the bar — slowness here creates a bad first impression regardless of response quality.

**Latency budget for pane open** (SprkChatPane mounted → ready to receive input):

| Step | Target (p95) | Mechanism |
|------|-------------|-----------|
| Context detection (`detectContext()`) | <10ms | Synchronous Xrm API + URL parse |
| Context-mapping lookup | <50ms | Redis hit; Dataverse only on cold miss |
| Session creation | <100ms | Redis write only (Dataverse write is async/fire-and-forget) |
| Playbook context load | <150ms | Redis-cached playbook + action data |
| **Total pane-open** | **<300ms** | All steps combined |

**Redis caching rules for this project**:

1. **Context-mapping cache**: key `"chat:ctx-mapping:{entityType}:{pageType}"`, TTL **30 minutes** (not 5 — admin config changes rarely; 30 min is safe and cuts Dataverse queries by 99%)
2. **Warm common mappings at startup**: On `ChatContextMappingService` first request per entity/page-type combo, cache the result immediately. Consider a background warm-up task for the top 5 entity/page-type combinations (matter/form, matter/list, project/form, account/form, `*/any`) at API startup.
3. **Client sessionStorage** remains as a second-layer guard: key `"sprkchat-context-{entityType}-{pageType}"`, TTL 5 minutes. This prevents even a Redis round-trip on repeated pane opens within the same browser session.
4. **Cache invalidation**: Admin changes to `sprk_aichatcontextmap` should trigger a targeted Redis key eviction (or use short enough TTL that staleness is acceptable — 30 minutes is the right tradeoff).
5. **Never block pane open on a cache miss going to Dataverse**: If both Redis and sessionStorage miss, show the pane in "global mode" immediately using the `*/any` fallback playbook, then update the selector asynchronously when the Dataverse response arrives.

## What Exists Today (Production State)

> ⚠️ **Updated from worktree audit (production-performance-improvement-r1).** The Chat feature is significantly more mature than the original design assumed. Implementation must extend, not reimagine, these systems.

### Server — BFF API

**`ChatHostContext.cs`** (`Models/Ai/Chat/ChatHostContext.cs`)
```csharp
public sealed record ChatHostContext(
    string EntityType,       // matter | project | invoice | account | contact
    string EntityId,         // GUID of Dataverse record
    string? EntityName = null,    // Display name (for logging and UI)
    string? WorkspaceType = null) // "LegalWorkspace" | "AnalysisWorkspace" | "FinanceWorkspace"
```
- `IsValid()` validates EntityType via `ParentEntityContext.EntityTypes.IsValid()` (see below)
- Valid entity types defined in `ParentEntityContext.EntityTypes`: `matter`, `project`, `invoice`, `account`, `contact`
- **Missing**: `PageType` — this is the primary addition needed in Phase 1

**`ChatEndpoints.cs`** (`Api/Ai/ChatEndpoints.cs`) — 6 active endpoints:
- `POST /api/ai/chat/sessions` — create session (accepts `ChatHostContext?` in request body)
- `POST /api/ai/chat/sessions/{id}/messages` — send message, SSE stream (token, done, error, citations, suggestions)
- `POST /api/ai/chat/sessions/{id}/refine` — refine selected text, SSE stream
- `GET /api/ai/chat/sessions/{id}/history` — message history (Redis hot → Dataverse cold fallback)
- `PATCH /api/ai/chat/sessions/{id}/context` — switch document/playbook/hostContext mid-session
- `DELETE /api/ai/chat/sessions/{id}` — archive session
- `GET /api/ai/chat/playbooks` — discover playbooks (user-owned + public, merged & deduplicated)

**Endpoint patterns** (all new endpoints must follow):
- Group-level `.RequireAuthorization()` already applied to `/api/ai/chat`
- Per-endpoint `.AddAiAuthorizationFilter()` (ADR-008)
- `ExtractTenantId(httpContext)` helper: `tid` JWT claim → `X-Tenant-Id` header fallback (ADR-014)
- `ExtractUserId(httpContext)` helper: `oid` JWT claim
- All errors via `Results.Problem(...)` (ProblemDetails format)
- Streaming endpoints apply `.RequireRateLimiting("ai-stream")`
- Non-streaming endpoints do **not** apply rate limiting

**`ChatSessionManager.cs`** — Dual-storage:
- Hot path: Redis cache, 24-hour sliding TTL, key `"chat:session:{tenantId}:{sessionId}"` (ADR-014 tenant-scoped)
- Cold path: Dataverse `sprk_aichatsummary` entity (audit trail)

**`SprkChatAgent.cs`** — Microsoft.Extensions.AI pattern:
- Uses `IChatClient` (abstraction over Azure OpenAI) — NOT `AzureOpenAIClient` directly
- Three-layer middleware pipeline wrapping agent: `AgentContentSafetyMiddleware` → `AgentCostControlMiddleware` → `AgentTelemetryMiddleware`
- Tools registered via `AIFunctionFactory.Create()` through `SprkChatAgentFactory`

**`PlaybookChatContextProvider.cs`** — Resolves playbook context:
- Loads Action record, partitions knowledge into inline content vs. RAG index sources
- Uses `IScopeResolverService` (which after Task PPI-054 delegates to: `AnalysisActionService`, `AnalysisSkillService`, `AnalysisKnowledgeService`, `AnalysisToolService`)
- Enriches system prompt with entity context, document summary, skill instructions

**`EmbeddingCache.cs`** — Redis embedding cache:
- Key: `sdap:embedding:{base64-sha256}`, TTL: 7 days
- Used by RAG pipeline for cost-efficient repeated searches

**`ChatPlaybookInfo`** (existing DTO used in `GET /api/ai/chat/playbooks`):
```csharp
public record ChatPlaybookInfo(string Id, string Name, string? Description, bool IsPublic);
```
The new context-mapping response must extend or reuse this DTO.

### Client — SprkChatPane

- `contextService.ts` — Detects `entityType` + `entityId` from URL params, `Xrm.Page.data.entity`, or `Xrm.Utility.getPageContext()`
- `contextService.ts` — Hardcoded `DEFAULT_PLAYBOOK_MAP` (to be replaced)
- `contextService.ts` — Context-change polling (2s interval), triggers `ContextSwitchDialog`
- `App.tsx` — Passes `hostContext` (entityType, entityId) to `SprkChat` component
- `ContextSwitchDialog.tsx` — Modal asking user to switch or keep context when navigation detected

### Shared Library (`@spaarke/ui-components`)

- `SprkChat.tsx` — Accepts `hostContext`, `playbooks[]`, `playbookId` props
- `SprkChatContextSelector.tsx` — Document + playbook dropdown (hidden when no options)
- `useChatPlaybooks.ts` — Fetches playbook list from BFF API
- `useChatSession.ts` — `switchContext()` calls PATCH endpoint

---

## Design

### Data Model: Context Mapping Table

New Dataverse entity: `sprk_aichatcontextmap`

| Field | Type | Description |
|-------|------|-------------|
| `sprk_name` | String | Display name (e.g., "Matter Form Assistant") |
| `sprk_entitytype` | String | Dataverse entity logical name (`matter`, `project`, `invoice`, `account`, `contact`, or `*` wildcard). Must align with `ParentEntityContext.EntityTypes` values |
| `sprk_pagetype` | Choice | Form (100000000), List (100000001), Dashboard (100000002), Workspace (100000003), Any (100000004) |
| `sprk_playbookid` | Lookup → sprk_analysisplaybook | The playbook to activate in this context |
| `sprk_sortorder` | Integer | Priority (lower = higher priority, default 100) |
| `sprk_isdefault` | Boolean | Whether this is the auto-selected playbook for the context (vs. available in picker) |
| `sprk_description` | Multi-line Text | Admin-facing description of when this mapping applies |
| `sprk_isactive` | Boolean | Active/inactive toggle (statecode) |

**Resolution logic** (server-side, `ChatContextMappingService`):
```
Query: entityType={detected}, pageType={detected}
  1. Exact match: entityType + pageType
  2. Entity match: entityType + pageType=Any
  3. Wildcard match: entityType=* + pageType={detected}
  4. Global fallback: entityType=* + pageType=Any

  Within each tier: order by sprk_sortorder ASC
  Return: { defaultPlaybook, availablePlaybooks[] }
```

### ChatHostContext Extension (C# — Phase 1)

Add `PageType` to the existing `ChatHostContext` record in `Models/Ai/Chat/ChatHostContext.cs`:

```csharp
public sealed record ChatHostContext(
    string EntityType,
    string EntityId,
    string? EntityName = null,
    string? WorkspaceType = null,
    string? PageType = null)        // NEW: "form" | "list" | "dashboard" | "workspace" | "unknown"
{
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(EntityType) &&
        !string.IsNullOrWhiteSpace(EntityId) &&
        ParentEntityContext.EntityTypes.IsValid(EntityType);
}
```

> **Note**: `EntityName` and `WorkspaceType` already exist in production. Only `PageType` is net-new.

### IHostContext Extension (TypeScript — Phase 1)

Extend the TypeScript `IHostContext` interface in `contextService.ts`:

```typescript
type PageType = "form" | "list" | "dashboard" | "workspace" | "unknown";

interface IHostContext {
  entityType: string;       // already exists
  entityId: string;         // already exists
  entityName?: string;      // already exists
  workspaceType?: string;   // already exists
  pageType?: PageType;      // NEW — populated by detectPageType()
}
```

### BFF API Endpoint

New endpoint in `ChatEndpoints.cs` (or new `ChatContextMappingEndpoints.cs`):

```
GET /api/ai/chat/context-mappings?entityType=sprk_matter&pageType=form
```

**Must follow production patterns**:
- Registered on the existing `/api/ai/chat` group (already has `.RequireAuthorization()`)
- `.AddAiAuthorizationFilter()` (ADR-008)
- No rate limiting (non-streaming)
- `ExtractTenantId()` — required, even though mappings are global config (tenant isolation is mandatory per ADR-014)
- `Results.Problem(...)` for all errors

**Response** — reuse `ChatPlaybookInfo` DTO (already exists, no new DTO needed):

```json
{
  "defaultPlaybook": {
    "id": "5ece14f7-...",
    "name": "Matter Document Assistant",
    "description": "Analyze and search documents for this matter",
    "isPublic": true
  },
  "availablePlaybooks": [
    { "id": "5ece14f7-...", "name": "Matter Document Assistant", "description": "...", "isPublic": true },
    { "id": "a1b2c3d4-...", "name": "Legal Research", "description": "...", "isPublic": true },
    { "id": "e5f6g7h8-...", "name": "Draft Legal Memo", "description": "...", "isPublic": false }
  ]
}
```

**New response record**:
```csharp
public record ChatContextMappingResponse(
    ChatPlaybookInfo? DefaultPlaybook,
    ChatPlaybookInfo[] AvailablePlaybooks);
```

### ChatContextMappingService (New Service — ADR-010 Note)

New singleton service `ChatContextMappingService` registered in DI.

> **ADR-010 Impact**: Production currently has ≤15 non-framework DI registrations. Verify count before adding. If budget is tight, combine with `PlaybookChatContextProvider` or use direct Dataverse query in endpoint.

**Caching** (ADR-009: Redis-first):
- Server-side: Redis cache, key `"chat:ctx-mapping:{entityType}:{pageType}"` (global, not tenant-scoped — mappings are admin configuration shared across tenants)
- TTL: 5 minutes (same as design intent for client sessionStorage)
- Fallback on cache miss: query Dataverse `sprk_aichatcontextmap`
- Client-side: sessionStorage key `"sprkchat-context-{entityType}-{pageType}"`, 5-minute TTL (second defensive layer)

### Page-Type Detection

Extend `contextService.ts`:

```typescript
function detectPageType(): PageType {
  const xrm = findXrm();
  if (!xrm) return "unknown";

  // Primary: Xrm.Utility.getPageContext()
  const pageContext = xrm.Utility?.getPageContext?.();
  if (pageContext?.input?.pageType === "entityrecord") return "form";
  if (pageContext?.input?.pageType === "entitylist") return "list";
  if (pageContext?.input?.pageType === "dashboard") return "dashboard";

  // Fallback: URL pattern matching (iframe cross-origin safe)
  const url = window.top?.location?.href ?? "";
  if (url.includes("/main.aspx?pagetype=entityrecord")) return "form";
  if (url.includes("/main.aspx?pagetype=entitylist")) return "list";
  if (url.includes("/main.aspx?pagetype=dashboard")) return "dashboard";

  // Detect workspace Code Pages (sprk_ web resource embedded)
  if (url.includes("webresourceName=sprk_")) return "workspace";

  return "unknown";
}
```

> **Risk**: SprkChat runs in an iframe — `window.top` access may fail cross-origin. Wrap `detectPageType()` entirely in try/catch and return `"unknown"` on any error (consistent with existing `findXrm()` pattern).

### Client Flow

```
SprkChatPane opens
  ↓
contextService.detectContext() → { entityType, entityId, entityName, workspaceType, pageType }
  ↓
Check sessionStorage: "sprkchat-context-{entityType}-{pageType}" (5-min TTL)
  ↓ (cache miss)
GET /api/ai/chat/context-mappings?entityType={}&pageType={}
  ↓
Store in sessionStorage with timestamp
  ↓
Auto-select defaultPlaybook → POST /api/ai/chat/sessions (with full IHostContext)
  ↓
Populate SprkChatContextSelector with availablePlaybooks[]
  ↓
User can switch playbooks via dropdown (calls PATCH /sessions/{id}/context)
```

### Entity Metadata Enrichment (Phase 3)

When `pageType === "form"`, pass additional entity metadata so `PlaybookChatContextProvider` can enrich the system prompt:

```
You are assisting with Matter "Acme Corp v. Smith" (sprk_matter).
The user is viewing the matter main form.
Available documents: [list from entity's document container]
```

The existing `PlaybookChatContextProvider` already structures system prompt composition — Phase 3 adds a new enrichment block using the `PageType`, `EntityName`, and `WorkspaceType` fields now flowing through `ChatHostContext`.

The `ChatKnowledgeScope` record already carries `ParentEntityType` and `ParentEntityId` — Phase 3 extends usage of these with `EntityName` (display name) for human-readable context injection.

---

## Phases

### Phase 1: Data-Driven Mapping (MVP)
- Add `PageType` to `ChatHostContext` record (C#)
- Add `pageType` to `IHostContext` TypeScript interface and `contextService.ts`
- Create `sprk_aichatcontextmap` Dataverse table and solution
- New `ChatContextMappingService` with Redis caching (ADR-009)
- `GET /api/ai/chat/context-mappings` endpoint (ADR-008 filter, ADR-014 tenant extract)
- Update `SprkChatPane` to query mappings at startup and replace hardcoded `DEFAULT_PLAYBOOK_MAP`
- Seed initial mapping data (matter entity → SprkChat Document Assistant playbook)

### Phase 2: Multi-Playbook UX
- `SprkChatContextSelector` shows available playbooks for current context
- Playbook switching via dropdown (existing `switchContext` / PATCH endpoint)
- sessionStorage caching with TTL on client

### Phase 3: Entity Metadata Enrichment
- `ChatHostContext.EntityName` and `PageType` flow into `PlaybookChatContextProvider` system prompt
- New enrichment block in `PlaybookChatContextProvider`: "Assisting with {EntityType} '{EntityName}' on {PageType} view"
- Entity-scoped document list in system prompt (leverage existing `ChatKnowledgeScope.ParentEntityType/Id`)

### Phase 4: Admin Experience
- Model-driven form for `sprk_aichatcontextmap`
- Bulk seed utility for common entity types
- Validation: warn admin if entity type has no mapping configured

---

## Implementation Constraints

| Constraint | Source | Impact |
|------------|--------|--------|
| Endpoint filters for authorization | ADR-008 | New endpoint needs `.AddAiAuthorizationFilter()` |
| Tenant-scoped operations | ADR-014 | `ExtractTenantId()` mandatory even for global config reads |
| Redis-first caching | ADR-009 | Server-side Redis cache before Dataverse query; no L1 in-memory |
| Redis TTL for context mappings | Performance standard | 30-minute TTL (not 5); Dataverse queries only on cold miss |
| DI minimalism | ADR-010 | `ChatContextMappingService` is 1 new registration; verify ≤15 budget |
| AI Architecture: extend BFF | ADR-013 | No new separate service — extend `ChatEndpoints.cs` |
| Entity type values | `ParentEntityContext.EntityTypes` | Valid: matter, project, invoice, account, contact (+ `*` wildcard) |
| PageType values | `contextService.ts` (new) | Valid: form, list, dashboard, workspace, unknown |
| No L2 HTTP client in plugins | ADR-002 | N/A (no Dataverse plugin changes) |
| SSE for all AI generation | UX standard | `context-mappings` is REST (config lookup); session messages/refine must remain SSE |
| Non-blocking pane open | UX standard | Show global-mode fallback immediately if context-mapping is cold; update async |
| No new REST endpoints for AI content | UX standard | Any AI-generating endpoint added in this project must use SSE |

---

## Success Criteria

1. Admin can configure which playbooks appear for which entity/page context via Dataverse form
2. SprkChat auto-selects the correct playbook when opening on a matter form vs. matter list
3. User can switch between available playbooks for their current context
4. No code deployment required to add a new entity-to-playbook mapping
5. Existing hardcoded behavior works as fallback until mappings are configured
6. New endpoint follows all production patterns: auth filter, tenant extraction, ProblemDetails errors, Redis caching
7. **Pane-open latency ≤300ms p95** (context detection + mapping lookup + session creation combined)
8. **Context-mapping endpoint ≤50ms p95** from Redis cache; Dataverse only on cold miss
9. **No REST regression**: all existing SSE chat paths continue to stream; no new AI-generating endpoints return full response bodies
10. **Non-blocking UX**: pane is interactive in global mode within 300ms even if context-mapping cache is cold; correct playbook selected asynchronously

---

## Dependencies

- `sprk_analysisplaybook` table (playbooks must exist to be mapped)
- `ChatHostContext` record (extend with `PageType` — non-breaking, optional parameter)
- `ChatPlaybookInfo` DTO (reuse for response — already exists in `ChatEndpoints.cs`)
- `ChatEndpoints.cs` route group (add new endpoint to existing `/api/ai/chat` group)
- `IPlaybookService` (validate playbook IDs exist before returning mapping)
- `EmbeddingCache` / Redis `IDistributedCache` (for new mapping response caching)
- Existing `contextService.ts` Xrm frame-walk utilities (extend, not replace)
- SprkChatPane deployed and working ✅ (confirmed in production-performance-improvement-r1)
- BFF API MSAL ssoSilent authentication ✅ (confirmed in production-performance-improvement-r1)

---

## Risks

| Risk | Mitigation |
|------|------------|
| Xrm.Utility.getPageContext() unavailable in some UCI contexts | URL pattern fallback already designed in; return "unknown" on any error |
| SprkChat iframe cross-origin restricts `window.top` access | Entire `detectPageType()` wrapped in try/catch — "unknown" is a valid page type |
| Context-mapping Dataverse cold miss blocks pane open | Show global-mode playbook immediately; update selector asynchronously when Dataverse responds |
| Redis unavailable (transient failure) | Degrade gracefully: skip cache, query Dataverse directly; do not fail pane open |
| 30-minute TTL causes stale mapping after admin change | Acceptable tradeoff; Phase 4 admin form can include "Refresh mappings" button that evicts cache keys |
| ADR-010 DI budget exceeded by new service | Audit DI registrations before implementing; merge into `PlaybookChatContextProvider` if needed |
| `PageType` on `ChatHostContext` is a breaking serialization change | Declared `string? PageType = null` (optional with default null) — backward compatible with sessions in Redis |
| Developer adds REST endpoint for future chat AI feature | SSE rule in this design + code review gate (ADR-check) prevents regression |
