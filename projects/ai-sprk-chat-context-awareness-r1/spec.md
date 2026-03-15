# SprkChat Context Awareness - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-14
> **Source**: design.md (reviewed and updated against production-performance-improvement-r1 worktree)

---

## Executive Summary

SprkChat currently opens with a hardcoded playbook GUID regardless of the Dataverse page the user is on. This project replaces that hardcoded mapping with a data-driven system: a new Dataverse table (`sprk_aichatcontextmap`) lets admins configure which playbooks appear in which context (entity type + page type), with zero code deployment required to change mappings. SprkChat will automatically detect page context, query the mapping, and select the appropriate playbook — while enriching the AI's system prompt with entity-specific context (entity name, page type) so the AI understands what the user is looking at.

All 4 design phases are in scope for this release.

---

## Scope

### In Scope

**Phase 1 — Data-Driven Mapping (MVP)**
- Create `sprk_aichatcontextmap` Dataverse table (net-new entity, core deliverable)
- Add to existing Dataverse solution (same solution that contains `sprk_aiplaybook`)
- `ChatContextMappingService` — new BFF service with Redis caching (30-min TTL)
- `GET /api/ai/chat/context-mappings` — new BFF endpoint
- Add `PageType` to `ChatHostContext` C# record (non-breaking — optional, default null)
- Add `pageType` to TypeScript `IHostContext` interface and `detectPageType()` to `contextService.ts`
- Replace hardcoded `DEFAULT_PLAYBOOK_MAP` in `SprkChatPane` with dynamic lookup
- Seed initial mapping data via PAC CLI import (matter/form → SprkChat Document Assistant)
- Fallback when no mapping exists: hide playbook selector, show generic chat UI

**Phase 2 — Multi-Playbook UX**
- `SprkChatContextSelector` populates `availablePlaybooks[]` from context-mapping response
- Playbook switching via existing PATCH `/sessions/{id}/context` + `switchContext()` hook
- Client sessionStorage caching: key `"sprkchat-context-{entityType}-{pageType}"`, 5-min TTL

**Phase 3 — Entity Metadata Enrichment**
- `ChatHostContext.EntityName` and `PageType` injected into `PlaybookChatContextProvider` system prompt
- New enrichment block appended **after** the playbook's own system prompt (additive, not replacing)
- Guard: only inject if `EntityName` is not null and `PageType` is not null/unknown
- Token-bounded: enrichment block capped at ~100 tokens to respect `AgentCostControlMiddleware` budget

**Phase 4 — Admin Experience**
- Model-driven form for `sprk_aichatcontextmap` (view, create, edit, delete records)
- Bulk seed utility for common entity types
- "Refresh Mappings" button on admin form triggers targeted Redis key eviction (bypasses 30-min TTL)

### Out of Scope

- Slash command menu / quick-action bar (separate project: `ai-sprk-chat-extensibility-r1`)
- Changes to `sprk_aiplaybook` table structure
- Multi-tenant mapping isolation (see Owner Clarifications Q2 — mappings are global config)
- Custom command definitions (Phase 4 of extensibility project)
- Changing the AI agent pipeline, middleware, or tool registration

### Affected Areas

| Area | Files | Change |
|------|-------|--------|
| BFF API — Models | `Models/Ai/Chat/ChatHostContext.cs` | Add `PageType` parameter |
| BFF API — Services | `Services/Ai/Chat/ChatContextMappingService.cs` | New file |
| BFF API — Endpoints | `Api/Ai/ChatEndpoints.cs` | New endpoint + new response record |
| BFF API — Services | `Services/Ai/Chat/PlaybookChatContextProvider.cs` | Phase 3 enrichment block |
| Dataverse Solution | `src/solutions/...` | New `sprk_aichatcontextmap` entity + form |
| Client — SprkChatPane | `src/client/pcf/SprkChatPane/contextService.ts` | `detectPageType()` + `IHostContext` extension |
| Client — SprkChatPane | `src/client/pcf/SprkChatPane/App.tsx` | Wire context-mapping call at startup |
| Client — Shared Library | `src/client/shared/.../SprkChat.tsx` | Pass `availablePlaybooks` into selector |
| Seed Data | `data/chat-context-mappings.json` | PAC CLI import file |

---

## Requirements

### Functional Requirements

**FR-01 — Net-New Dataverse Table**
- Create `sprk_aichatcontextmap` with fields: `sprk_name`, `sprk_entitytype`, `sprk_pagetype` (choice), `sprk_playbookid` (lookup → `sprk_aiplaybook`), `sprk_sortorder`, `sprk_isdefault`, `sprk_description`, `sprk_isactive`
- Acceptance: Table exists in solution, records can be created via Dataverse API

**FR-02 — Context-Mapping Resolution Endpoint**
- `GET /api/ai/chat/context-mappings?entityType={}&pageType={}`
- Resolution order: exact → entity+any → wildcard+pageType → `*/any` global fallback
- Within tier: sort by `sprk_sortorder` ASC
- Returns: `{ defaultPlaybook: ChatPlaybookInfo | null, availablePlaybooks: ChatPlaybookInfo[] }`
- Returns empty `availablePlaybooks[]` and `null` `defaultPlaybook` when no mapping configured
- Acceptance: Correct playbook returned for matter/form; empty response for unconfigured entity

**FR-03 — Page-Type Detection**
- `detectPageType()` in `contextService.ts` returns: `"form" | "list" | "dashboard" | "workspace" | "unknown"`
- Primary: `Xrm.Utility.getPageContext()` input.pageType
- Fallback: URL pattern matching for `entityrecord`, `entitylist`, `dashboard`
- Workspace: allowlist of known web resource names (enumerated during implementation: `sprk_corporateworkspace`, `sprk_legalworkspace`, etc.)
- Any error or unrecognized pattern → `"unknown"` (never throws)
- Acceptance: Returns correct type on matter form, matter list, dashboard, workspace; "unknown" on unrecognized

**FR-04 — Dynamic Pane Startup (replaces hardcoded map)**
- On `SprkChatPane` mount: detect context → check sessionStorage → call `GET /context-mappings` if miss
- Auto-select `defaultPlaybook` and populate `SprkChatContextSelector` with `availablePlaybooks[]`
- `DEFAULT_PLAYBOOK_MAP` constant removed from `contextService.ts`
- Acceptance: No hardcoded GUIDs remain in client code; pane selects correct playbook

**FR-05 — No-Mapping Fallback**
- When `context-mappings` returns `null` `defaultPlaybook`: hide playbook selector, show generic chat UI
- Session created with `PlaybookId: null` — `ChatSessionManager.CreateSessionAsync()` signature changed to `Guid? playbookId = null`
- `SprkChatAgent` handles null PlaybookId by running without system prompt or tool registration (raw conversational AI)
- `ChatSession.PlaybookId` type changed from `Guid` → `Guid?` (non-breaking: existing Redis-cached sessions deserialize correctly; Dataverse stores empty/omitted)
- Acceptance: Pane opens and is usable with no `sprk_aichatcontextmap` records; user can type and receive a response; no playbook selector shown

**FR-06 — Phase 3: System Prompt Enrichment**
- When `ChatHostContext.EntityName != null` and `PageType` is not null/unknown:
  - `PlaybookChatContextProvider` appends enrichment block to system prompt
  - Format: `"You are assisting with {EntityType} '{EntityName}'. The user is viewing the {PageType} view."`
  - Block is appended AFTER the playbook's own system prompt — never replaces it
  - Block is truncated if it would push total system prompt beyond the cost-control budget
- Acceptance: AI response on matter form references matter name; generic AI response unchanged on forms with no entity name

**FR-07 — Seed Data**
- Initial mapping delivered via PAC CLI import: `matter` + `form` → SprkChat Document Assistant playbook
- Import file format: `.json` compatible with PAC CLI `pac data import`
- Acceptance: After PAC import, `GET /context-mappings?entityType=matter&pageType=form` returns Document Assistant

**FR-08 — Phase 4: Admin Form + Cache Refresh**
- Model-driven app form for `sprk_aichatcontextmap`: view, create, edit, deactivate records
- "Refresh Mappings" action on form: calls a BFF endpoint (or Dataverse plugin) that evicts `"chat:ctx-mapping:*"` Redis keys
- Acceptance: Admin can change a mapping and force it live without waiting 30 minutes

### Non-Functional Requirements

**NFR-01 — Context-Mapping Endpoint Latency**
- `GET /api/ai/chat/context-mappings` ≤50ms p95 (Redis hit)
- Dataverse query only on cold miss; must not block client

**NFR-02 — Total Pane-Open Latency**
- SprkChatPane mounted → ready for input: ≤300ms p95
- Breakdown: context detection <10ms, mapping lookup <50ms, session create <100ms, playbook context load <150ms

**NFR-03 — Non-Blocking Pane Open**
- If both sessionStorage and Redis miss: show generic chat UI immediately (FR-05 fallback)
- Correct playbook selector updated asynchronously once Dataverse responds
- Never: spinner blocking full pane on context-mapping lookup

**NFR-04 — SSE Integrity**
- No new REST-based AI generation endpoints introduced
- Existing `/messages` and `/refine` SSE paths not regressed
- `useChatSession` hook preserves `ReadableStream` consumption pattern

**NFR-05 — Backward Compatibility**
- `ChatHostContext` with `PageType = null` must deserialize correctly from existing Redis-cached sessions
- No migration of existing sessions required

**NFR-06 — System Prompt Token Impact (Phase 3)**
- Entity enrichment block: ≤100 tokens
- `PlaybookChatContextProvider` must check remaining token budget before appending
- If appending would exceed budget: skip enrichment, log warning (do not fail)

**NFR-07 — No PII in Telemetry (Phase 3)**
- `EntityName` must NOT appear in structured log properties (only in unstructured debug-level messages if at all)
- `AgentTelemetryMiddleware` must not record entity name in telemetry metrics

---

## Technical Constraints

### Applicable ADRs

| ADR | Constraint |
|-----|-----------|
| **ADR-001** | Minimal API pattern — new endpoint registered via `MapGet`, not controller |
| **ADR-008** | Endpoint filters for authorization — `.AddAiAuthorizationFilter()` required |
| **ADR-009** | Redis-first caching — no L1 in-memory cache; Redis before Dataverse |
| **ADR-010** | DI minimalism — `ChatContextMappingService` is 1 new registration (confirmed: not a concern) |
| **ADR-013** | AI Architecture — extend BFF API, not a new service |
| **ADR-014** | Tenant-scoped operations — `ExtractTenantId()` mandatory on all endpoints |

### MUST Rules

- ✅ MUST call `.AddAiAuthorizationFilter()` on the new context-mappings endpoint
- ✅ MUST call `ExtractTenantId()` and return 400 if null (even though mapping data is global)
- ✅ MUST check Redis before querying Dataverse for context-mapping lookups
- ✅ MUST use `string? PageType = null` default on `ChatHostContext` (backward-compatible)
- ✅ MUST append Phase 3 enrichment AFTER (not replacing) the playbook system prompt
- ✅ MUST guard Phase 3 enrichment: skip if `EntityName` is null or `PageType` is null/unknown
- ✅ MUST cap Phase 3 enrichment block at ≤100 tokens
- ❌ MUST NOT log `EntityName` in structured telemetry properties
- ❌ MUST NOT block pane open waiting for a cold Dataverse query
- ❌ MUST NOT introduce any REST endpoint that returns AI-generated content
- ❌ MUST NOT remove hardcoded GUIDs without replacing with the FR-05 no-mapping fallback
- ❌ MUST NOT use `sprk_` prefix wildcard for workspace detection — use explicit allowlist

### Existing Patterns to Follow

- Endpoint registration: see `ChatEndpoints.MapChatEndpoints()` — add new route to same group
- Redis caching pattern: see `ChatSessionManager` — `IDistributedCache`, sliding TTL, JSON serialization
- Tenant extraction: see `ChatEndpoints.ExtractTenantId()` — copy exact helper
- Error responses: see `ChatEndpoints` — `Results.Problem(statusCode, title, detail)` format
- System prompt enrichment: see `PlaybookChatContextProvider` — append to existing builder pattern

---

## Phase 3 Risk Assessment

> Phase 3 (entity metadata enrichment in system prompt) modifies a production AI service. These risks must be planned against during implementation — they are not blockers to starting but must have mitigations in place before Phase 3 tasks begin.

| Risk | Severity | Mitigation |
|------|----------|-----------|
| Enrichment block disrupts carefully-crafted playbook system prompts | High | Block is strictly appended last; never inserted mid-prompt; playbook prompt is immutable |
| Entity name pushes system prompt past `AgentCostControlMiddleware` token budget | Medium | Check budget before appending; skip enrichment and log warning if insufficient headroom |
| `EntityName` contains PII (client/matter names) that appears in logs | High | Never log `EntityName` in structured properties; only allowed in Debug-level unstructured message (opt-in per environment) |
| Phase 3 modifies `PlaybookChatContextProvider` which has no unit tests | Medium | Write tests for enrichment logic in isolation before modifying the provider; test: null EntityName (no enrichment), valid EntityName (enrichment appended), budget exceeded (skipped) |
| Wrong entity name displayed (stale from Xrm.Page.data.entity) | Low | `EntityName` is read at pane-open time from `Xrm.Page.data.entity.getPrimaryAttributeValue()` or URL param; document the read-once behavior explicitly |
| "form" vs "workspace" pageType in enrichment prompt is confusing to AI | Low | Use human-readable strings in the injected block: "main form view", "list view", "dashboard view", "workspace view" — not the raw `pageType` token |

---

## Success Criteria

1. [ ] Admin creates a `sprk_aichatcontextmap` record via Dataverse UI → no code deployment needed — **Verify**: create a record in admin form, wait ≤30 min, confirm SprkChat picks up the new playbook
2. [ ] SprkChat auto-selects Document Assistant on matter form, shows different playbook (or generic) on a contact form — **Verify**: open SprkChat from matter record vs. contact record
3. [ ] `GET /api/ai/chat/context-mappings` returns in ≤50ms p95 on Redis hit — **Verify**: load test with k6, 50 concurrent requests, Redis pre-warmed
4. [ ] SprkChat pane opens and accepts input within 300ms p95 — **Verify**: Performance tab measurement from pane trigger to input enabled
5. [ ] Pane opens in generic chat mode when no `sprk_aichatcontextmap` records exist — **Verify**: fresh environment, no seed data, confirm pane usable
6. [ ] Seed data import via PAC CLI populates matter/form → Document Assistant mapping — **Verify**: `pac data import` → confirm record via Dataverse API
7. [ ] Phase 3: AI response on matter form references matter name in context — **Verify**: open matter "Acme Corp v. Smith", ask AI "what am I looking at?", confirm response mentions the matter
8. [ ] Phase 3: Entity name does NOT appear in Application Insights structured logs — **Verify**: run chat session on a named matter, query App Insights for structured fields containing the entity name
9. [ ] No REST regression: existing `/messages` and `/refine` responses stream tokens — **Verify**: network tab confirms `text/event-stream` content type, tokens appear progressively
10. [ ] `ChatHostContext` with `PageType = null` deserializes correctly from Redis — **Verify**: create session without pageType, read back from cache, confirm no deserialization error

---

## Dependencies

### Prerequisites (Must Exist Before Implementation)
- `sprk_aiplaybook` table with at least one active playbook (seed playbook must exist for import)
- `sprk_aichatsummary` Dataverse table ✅ (created 2026-03-14)
- `sprk_aichatmessage` Dataverse table ✅ (created 2026-03-14)
- SprkChatPane deployed and functional ✅
- BFF API MSAL ssoSilent authentication ✅
- Redis `IDistributedCache` registered in DI ✅
- `ChatEndpoints.cs` route group `/api/ai/chat` with auth ✅
- `PlaybookChatContextProvider.cs` with system prompt builder ✅

### External Dependencies
- PAC CLI available in deployment pipeline for seed data import
- Dataverse solution package authoring for `sprk_aichatcontextmap` (same solution as `sprk_aiplaybook`, `sprk_aichatsummary`, `sprk_aichatmessage`)

---

## Owner Clarifications

| Topic | Question | Answer | Implementation Decision |
|-------|----------|--------|------------------------|
| Phase scope | Which phases are in scope for R1? | All 4 phases | All phases implemented in this project; Phase 3 risks assessed and mitigated |
| New table | Is `sprk_aichatcontextmap` an existing table? | No — it does not exist yet | Core deliverable: net-new Dataverse entity to be created in this project |
| No-mapping fallback | What shows when no `*/any` mapping exists? | Option A: hide selector, show generic chat | Pane opens with no playbook selector when `defaultPlaybook` is null |
| DI budget | Is ADR-010 a constraint? | Not an issue | Proceed with standalone `ChatContextMappingService`; no merge needed |
| Seed data delivery | How to deliver initial mapping records? | PAC CLI data import | `.json` import file checked into repo; deployed via `pac data import` |
| Cache TTL | Is 30 minutes acceptable for mapping TTL? | Yes | 30-min Redis TTL; Phase 4 admin form adds manual refresh |
| Workspace detection | Use broad `sprk_` match or allowlist? | Allowlist | Enumerate known workspace web resource names at implementation time |
| Default indicator | `isDefault` field on `ChatPlaybookInfo`? | Array position sufficient | `defaultPlaybook` at top level of response is unambiguous; no field needed |

---

## Assumptions

- **Mapping scope**: `sprk_aichatcontextmap` is global admin configuration shared across all users (not per-tenant, not per-user). Redis cache key is non-tenant-scoped: `"chat:ctx-mapping:{entityType}:{pageType}"`.
- **Phase 3 enrichment format**: Injected as: `"Context: You are assisting with {entityType} record '{entityName}'. The user is viewing the {humanReadablePageType} view."` (human-readable page type, not raw token).
- **Phase 3 token budget**: 100-token guard is a conservative cap; actual enrichment block will be ~30-50 tokens for typical entity names.
- **Workspace allowlist seed**: At implementation time, enumerate: `sprk_corporateworkspace`, `sprk_legalworkspace`, `sprk_analysisworkspace` (developer verifies full list from deployed web resources).
- **Generic chat session**: When no playbook mapping exists, `POST /api/ai/chat/sessions` is called with `PlaybookId: null`. `ChatSessionManager.CreateSessionAsync()` accepts `Guid? playbookId = null`. `SprkChatAgent` runs without a system prompt or tool registration when PlaybookId is null — raw conversational AI only.
- **Phase 4 cache refresh**: "Refresh Mappings" button calls a new `DELETE /api/ai/chat/context-mappings/cache` endpoint that evicts all `chat:ctx-mapping:*` keys from Redis. This is a Phase 4 task; Phase 1-3 rely on TTL expiry.

---

## Unresolved Questions

*None — all questions resolved.*

---

## Resolved Decisions

| Decision | Resolution | Implementation Impact |
|----------|-----------|----------------------|
| Generic session without PlaybookId | **Option B**: Make `PlaybookId` nullable (`Guid?`) on `ChatSession` record and `CreateSessionAsync` signature | `ChatSession.PlaybookId` → `Guid?`; `ChatSessionManager.CreateSessionAsync()` → `Guid? playbookId = null`; `SprkChatAgent` must handle null PlaybookId by running without a playbook system prompt (raw IChatClient, no tool registration); `ChatDataverseRepository` writes empty string or omits field when null |

---

*AI-optimized specification. Original design: design.md*
