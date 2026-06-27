# Task 070 PART A evidence — Pinned Memory CRUD endpoint pair (Q7 expansion BFF surface)

**Pillar / Spec ref**: R6 Pillar 7 / D-C-24 + D-C-25 (FR-47 Q7 scope expansion) — Pinned Memory CRUD + Visualization UI.
**Wave**: C-G15. PART A (BFF endpoints) only; PART B (frontend Fluent v9 components) deferred to a separate dispatch.
**Date**: 2026-06-18.

## PART A scope (this dispatch)

Task 070 in the POML covers BOTH the BFF endpoint pair AND the four Fluent v9 components (`PinnedMemoryListWidget`, `PinnedMemoryEditDialog`, `PinnedMemoryDeleteConfirmation`, `PinnedMemoryProvenanceBadge`). To avoid stream-idle timeouts the main session split the task into two dispatches:

| PART | Scope | Status |
|------|-------|--------|
| **A (this dispatch)** | BFF endpoint pair (`PinnedMemoryEndpoints.cs`) + repository extension (`UpdateAsync` + `GetByIdAsync`) + endpoint tests | ✅ |
| **B (separate later dispatch)** | Frontend components in `@spaarke/ai-widgets`; Context-pane integration | ⏳ pending |

TASK-INDEX marks 070 as 🟡 (partial); will flip to ✅ only after PART B closes.

## Implementation overview

### Sub-task 1 — `PinnedMemoryEndpoints.cs` (NEW)

NEW endpoint pair at `src/server/api/Sprk.Bff.Api/Api/Memory/PinnedMemoryEndpoints.cs` exposing four endpoints under `/api/memory/pins`:

| Method | Route | Purpose | Auth | Rate limit |
|--------|-------|---------|------|------------|
| `GET` | `/api/memory/pins?matterId={matterId?}` | List the caller's pinned items | RequireAuth + tid+oid claims | `ai-context` 60/min |
| `POST` | `/api/memory/pins` | Create a pin (returns 201) | RequireAuth + tid+oid claims | `ai-context` 60/min |
| `PUT` | `/api/memory/pins/{pinId}` | Update existing pin (200 / 404 / 403) | RequireAuth + tid+oid + ownership | `ai-context` 60/min |
| `DELETE` | `/api/memory/pins/{pinId}` | Delete pin (204 / 404 / 403) | RequireAuth + tid+oid + ownership | `ai-context` 60/min |

**URL convention**: `/api/memory/pins` (NOT `/api/memory/pinned`). Chose `/pins` to align with the `memory.pin_created` / `memory.pin_deleted` / `memory.pin_updated` Counter naming (all singular "pin") that task 069 introduced on the `Sprk.Bff.Api.Memory` Meter. Documented in the file's XML class-doc remarks.

**Tenant + user scope (NFR-16 binding)**: tenant is derived ONLY from the caller's `tid` claim; user is derived ONLY from the caller's `oid` claim. Neither value is accepted from request body or query string — by design, mirroring the `WorkspaceStateEndpoints` precedent. A missing claim returns 401 ProblemDetails. Cross-tenant reads/writes are structurally impossible.

**Q7 ownership invariant (Pillar 7 binding)**: PUT/DELETE callers MUST own the pin. The handler:
1. Loads the pin via `IPinnedContextRepository.GetByIdAsync(tenantId, pinId)` (new repository method — see sub-task 2).
2. Returns 404 if `null`.
3. Compares the pin's `UserId` to the caller's `oid` claim; mismatch returns 403.
4. Only then proceeds with `UpdateAsync` / `DeleteAsync`.

Matter-fact pins use the same UserId-anchored ownership check. The POML noted "matter-scope pins need matter-access check — defer that to `AuthorizationService` if it exists; otherwise filter by userId" — the implementation takes the userId-filter path because the existing `PinnedContextItem` model anchors creator/owner identity on `UserId`. A richer matter-access check is documented as a follow-up but not load-bearing for PART A.

### Sub-task 2 — `IPinnedContextRepository` extension

Two new methods added to `IPinnedContextRepository` (and matching implementations on `PinnedContextRepository`):

- `Task<PinnedContextItem?> GetByIdAsync(string tenantId, string pinId, CancellationToken)` — point-read by deterministic id; returns `null` on 404 (idempotent). Used by PUT/DELETE for ownership validation BEFORE the mutation.
- `Task UpdateAsync(PinnedContextItem pin, CancellationToken)` — `ReplaceItemAsync` against the partition; caller is responsible for ownership validation upstream (the endpoint reads via `GetByIdAsync` first).

**Why extend the interface rather than add a service layer?** The 2026-05-20 refined ADR-013 boundary rule says AI-internal collaborators consume the pinned-context repository DIRECTLY (no PublicContracts facade). Task 069's `ManagePinnedContextHandler` already does this. Adding endpoint-side `GetByIdAsync` + `UpdateAsync` to the same repository contract keeps a single ownership surface and avoids inventing a new service layer that would itself be AI-internal plumbing. The repository's contract stability note (model XML doc) is still honored — added methods, no breaking changes to the four existing methods.

**Length caps inherited**: `UpdateAsync` mirrors `CreateAsync`'s length-cap validation (`MaxTitleLength = 200`, `MaxContentLength = 1000`) via the same internal constants on `PinnedContextRepository`. Endpoint validation does the same caps client-side so the 400 surface fires before the repo call.

### Sub-task 3 — Endpoint wiring (`EndpointMappingExtensions.cs`)

Single line added inside `MapDomainEndpoints` immediately after `MapWorkspaceStateEndpoints()`:

```csharp
Sprk.Bff.Api.Api.Memory.PinnedMemoryEndpoints.MapPinnedMemoryEndpoints(app);
```

ZERO new top-level `Program.cs` lines. ZERO new DI registrations (`IPinnedContextRepository` is already registered in `AnalysisServicesModule` at task 065).

### Sub-task 4 — DTOs (in `PinnedMemoryEndpoints.cs`)

Four DTOs live next to the endpoint surface (single-file contract):
- `CreatePinRequest` — `{ title, content, pinType, matterId? }`
- `UpdatePinRequest` — same shape as Create (full replacement model; PATCH semantics deferred)
- `PinResponse` — `{ item: PinDto }` (POST 201 + PUT 200)
- `PinListResponse` — `{ items: PinDto[], count }`
- `PinDto` — `{ pinId, pinType, title, content, matterId?, createdAt, updatedAt, createdBy }`

**PinDto.pinId is the wire id** (the `{pinId}` portion of the Cosmos doc id, NOT the full `pinned-context_{tenantId}_{pinId}` doc id). Recovered via prefix stripping in `ToDto()`; mirrors the same pattern in `ManagePinnedContextHandler.ExtractPinIdFromDocumentId`.

### Sub-task 5 — Endpoint tests

NEW test file at `tests/unit/Sprk.Bff.Api.Tests/Api/Memory/PinnedMemoryEndpointsTests.cs`. Pattern mirrors `WorkspaceStateEndpointsTests`: in-process `WebApplicationFactory<Program>`, fake auth handler emitting `oid` + optional `tid`, mocked `IPinnedContextRepository`.

15 tests covering POML's required scope:

| Test | Scenario | Status |
|------|----------|--------|
| `ListPins_Unauthenticated_Returns401` | No Authorization header | ✅ |
| `ListPins_MissingTidClaim_Returns401` | Authenticated without tid claim | ✅ |
| `ListPins_Authenticated_Returns200WithItems` | Happy path | ✅ |
| `ListPins_MatterIdFilter_NarrowsMatterFactPinsOnly` | matterId narrows matter-fact pins; other pinTypes unchanged | ✅ |
| `CreatePin_Authenticated_Returns201AndEmitsCounter` | Happy path + ADR-015 counter verification | ✅ |
| `CreatePin_MissingTitle_Returns400` | Validation | ✅ |
| `CreatePin_InvalidPinType_Returns400` | Validation | ✅ |
| `CreatePin_MatterFactWithoutMatterId_Returns400` | matter-fact requires matterId | ✅ |
| `UpdatePin_Authenticated_Returns200` | Happy path | ✅ |
| `UpdatePin_NotFound_Returns404` | Pin missing | ✅ |
| `UpdatePin_NotOwned_Returns403` | Caller mismatches owner UserId | ✅ |
| `DeletePin_Authenticated_Returns204AndEmitsCounter` | Happy path + counter | ✅ |
| `DeletePin_NotFound_Returns404` | Pin missing | ✅ |
| `DeletePin_NotOwned_Returns403` | Caller mismatches owner | ✅ |
| `ListPins_ScopesByCallerTidClaim_NeverAcceptsTenantQuery` | NFR-16 isolation — tenantId query param is ignored | ✅ |

**ADR-015 counter audit**: two dedicated MeterListener-based tests (Create + Delete) verify that the emitted `memory.pin_created` / `memory.pin_deleted` Counter dimensions contain `tenantId`, `userId`, `pinId`, `pinType`, `decision` — and explicitly NEVER contain `title` or `content` keys. The pattern mirrors task 069's `TypedToolHandlerTestFixture.AssertTelemetryRespectsAdr015` approach.

## ADR governance summary

- **ADR-008**: every endpoint inherits `RequireAuthorization()` from the group + per-handler tid/oid claim extraction. Mirrors `WorkspaceStateEndpoints`.
- **ADR-010**: ZERO new top-level DI registrations. `IPinnedContextRepository` already registered (task 065). Single `MapPinnedMemoryEndpoints()` call inside `EndpointMappingExtensions.MapDomainEndpoints` (endpoint mapping, NOT DI).
- **ADR-013**: endpoint consumes `IPinnedContextRepository` directly per the 2026-05-20 refined boundary rule for AI-internal collaborators. NO `IOpenAiClient` / `IPlaybookService` / other AI-internal types injected.
- **ADR-015 BINDING**: telemetry emits handler tag (`[PINNED-MEMORY]`) + decision discriminator + deterministic identifiers (tenantId, userId, pinId, pinType) + length-class flags (titleLen, contentLen) ONLY. NEVER the title body. NEVER the content body. NEVER request body raw text. Exception paths log `errorType = ex.GetType().Name` only. Verified by 2 dedicated MeterListener tests.
- **ADR-016**: `ai-context` rate limit (60/min sliding window) applied at the group level.
- **ADR-029**: BCL-only implementation; published-size delta = -0.01 MB vs task 069 baseline (44.71 MB compressed vs 44.72 MB baseline; essentially identical, well within tolerance).
- **NFR-03 (no new ADRs)**: honored. Pattern (4 CRUD endpoints + claim-derived tenant scope + ownership invariant + ai-context rate limit + ADR-015 counter dimensions) is the established `WorkspaceStateEndpoints` + task 069 pattern.
- **NFR-16 (per-tenant isolation)**: tenant is derived ONLY from the caller's `tid` claim. The repository scopes by Cosmos partition key `/tenantId`. Cross-tenant reads/writes are structurally impossible. Verified by `ListPins_ScopesByCallerTidClaim_NeverAcceptsTenantQuery`.

## Build + publish-size verification

```
Build: dotnet build src/server/api/Sprk.Bff.Api/ -nologo -v q
  → 0 errors, 16 warnings (matches baseline; no new warnings).

Test (PinnedMemoryEndpoints scope):
  → 15 passed / 0 failed.

Test (PinnedContext|Memory regression sweep):
  → 160 passed / 0 failed / 1 pre-existing skip.

Publish-size:
  → 44.71 MB compressed (vs 44.72 MB task-069 baseline; -0.01 MB delta).
```

No new CVE introduced (no NuGet additions; pure BCL endpoint surface).

## Files created

- `src/server/api/Sprk.Bff.Api/Api/Memory/PinnedMemoryEndpoints.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Api/Memory/PinnedMemoryEndpointsTests.cs`
- `projects/spaarke-ai-platform-unification-r6/notes/task-070-partA-evidence.md` (this file)

## Files modified

- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IPinnedContextRepository.cs` — added `GetByIdAsync` + `UpdateAsync`.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PinnedContextRepository.cs` — implementations of the two new methods (ReplaceItemAsync + ReadItemAsync; idempotent on 404 for GetByIdAsync).
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` — single line to wire `MapPinnedMemoryEndpoints(app)` inside `MapDomainEndpoints` after `MapWorkspaceStateEndpoints()`.
- `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` — 070 🔲 → 🟡 (PART A done; PART B frontend pending).

## PART B handoff notes

For the next agent dispatching PART B (frontend components):

- **BFF base URL**: `/api/memory/pins`
- **Auth**: `useAuth()` + `authenticatedFetch` per ADR-028 (the standard pattern). The BFF derives tenant + user from the JWT `tid` + `oid` claims; the client does NOT send them.
- **Request shape (POST/PUT)**:
  ```json
  {
    "title": "string ≤200 chars (required)",
    "content": "string ≤1000 chars (required)",
    "pinType": "user-preference | system-rule | matter-fact (required)",
    "matterId": "string (required only when pinType = matter-fact)"
  }
  ```
- **Response shape (GET list)**:
  ```json
  {
    "items": [PinDto],
    "count": number
  }
  ```
- **Response shape (POST 201 / PUT 200)**:
  ```json
  { "item": PinDto }
  ```
- **PinDto** (the canonical UI display contract):
  ```json
  {
    "pinId": "stable id (GUID without dashes)",
    "pinType": "user-preference | system-rule | matter-fact",
    "title": "string",
    "content": "string",
    "matterId": "string | null",
    "createdAt": "ISO-8601 UTC",
    "updatedAt": "ISO-8601 UTC",
    "createdBy": "user oid (string)"
  }
  ```
- **Optional query**: `GET /api/memory/pins?matterId={matterId}` narrows matter-fact pins to the supplied matter; other pinTypes are unaffected.
- **DELETE**: returns 204 No Content (no response body).
- **Error shapes** (ProblemDetails RFC 7807 — same as workspace state):
  - 400 — validation (missing fields, bad pinType, length caps, matter-fact without matterId)
  - 401 — unauthenticated or missing tid/oid claim
  - 403 — caller does not own the pin
  - 404 — pin not found
  - 429 — rate limit exceeded
  - 500 — internal error

- **Provenance**: the BFF does NOT currently surface a `source` field distinguishing "created via chat" vs "created via UI". The chat-side `ManagePinnedContextHandler` (task 069) and the UI endpoint both populate the same `PinnedContextItem` model — there is no discriminator at the data layer. PART B's `PinnedMemoryProvenanceBadge` component will need a follow-up data-layer extension (add `source` field to `PinnedContextItem` + a column in the JSON-property contract; task 069 evidence note documented this as an open follow-up). For now PART B can default to "Created via UI" and treat provenance as a stub until the data field lands.

- **PART B should NOT modify the BFF surface unless the provenance follow-up is taken**. If provenance is taken, add `[JsonPropertyName("source")] public string? Source { get; init; }` to `PinnedContextItem` + `PinDto`, populate it (`"chat"` in `ManagePinnedContextHandler.CreateAsync`, `"ui"` in `PinnedMemoryEndpoints.CreatePinAsync`), and surface it in the DTO via the `ToDto()` mapping.
