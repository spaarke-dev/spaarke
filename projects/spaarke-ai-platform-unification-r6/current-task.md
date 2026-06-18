# Current Task State — R6 (Wave C-G15 task 070 — PART A done; PART B pending)

> **Last Updated**: 2026-06-18 (task 070 PART A — BFF endpoint pair closed)
> **Mode**: Wave C-G15 (Q7 Pinned Memory CRUD + Visualization UI) — partially complete
> **Branch**: `work/spaarke-ai-platform-unification-r6`

---

## Task 070 PART A summary

Task 070 covers BOTH the BFF endpoint pair AND four Fluent v9 components (per the POML). To keep the work within stream-idle limits the main session split it into two dispatches:

| PART | Scope | Status |
|------|-------|--------|
| **A** | BFF endpoint pair (`PinnedMemoryEndpoints.cs`) + repository extension (`UpdateAsync` + `GetByIdAsync`) + endpoint tests | ✅ this dispatch |
| **B** | Frontend Fluent v9 components in `@spaarke/ai-widgets`; Context-pane integration | ⏳ pending (separate dispatch) |

TASK-INDEX shows 070 as 🟡 (partial); will flip to ✅ only after PART B closes.

| Task | PART | Status | Tests | Evidence note |
|------|------|--------|-------|---------------|
| 070 | A | 🟡 partial | 15 / 0 PinnedMemoryEndpoints + 160 / 0 (1 skip) PinnedContext/Memory regression sweep | [task-070-partA-evidence.md](notes/task-070-partA-evidence.md) |

**Build status**: BFF clean (0 errors, 16 pre-existing warnings; no new warnings).
**Publish-size**: 44.71 MB compressed (-0.01 MB vs task 069 44.72 MB baseline; essentially identical — pure BCL endpoint surface, no NuGet additions).
**CVE**: no new vulnerabilities (no NuGet additions).

---

## What PART A produced

### Sub-task 1 — `PinnedMemoryEndpoints.cs` (NEW)

Four endpoints under `/api/memory/pins`:

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/memory/pins?matterId={matterId?}` | List caller's pinned items (matterId optional filter for matter-fact pins) |
| POST | `/api/memory/pins` | Create pin (201) |
| PUT | `/api/memory/pins/{pinId}` | Update pin (200 / 404 / 403 not-owned) |
| DELETE | `/api/memory/pins/{pinId}` | Delete pin (204 / 404 / 403 not-owned) |

**URL convention**: `/api/memory/pins` (singular "pin" — aligns with task 069's `memory.pin_created` / `memory.pin_deleted` / new `memory.pin_updated` Counter naming on the shared `Sprk.Bff.Api.Memory` Meter).

**Tenant + user scope (NFR-16 BINDING)**: tenant from caller's `tid` claim ONLY; user from caller's `oid` claim ONLY. Neither value is accepted from request body or query string. Cross-tenant reads/writes structurally impossible.

**Q7 ownership invariant (Pillar 7 BINDING)**: PUT/DELETE load pin via `GetByIdAsync`, then compare `pin.UserId` to caller's `oid` — mismatch returns 403. Matter-fact pins share the same UserId-anchored ownership check (richer `AuthorizationService` matter-access check documented as a follow-up).

**ADR-015 telemetry**: three Counters on the `Sprk.Bff.Api.Memory` Meter (`memory.pin_created`, `memory.pin_updated` [NEW], `memory.pin_deleted`). Dimension set: `tenantId`, `userId`, `pinId`, `pinType`, `decision`. NEVER title body. NEVER content body. NEVER request text. Verified by 2 dedicated MeterListener tests (Create + Delete).

### Sub-task 2 — `IPinnedContextRepository` extension

Two new methods added to interface + implementation:
- `GetByIdAsync(tenantId, pinId)` — point-read; returns `null` on 404 (idempotent on stale-handle race)
- `UpdateAsync(pin)` — `ReplaceItemAsync`; caller is responsible for ownership validation upstream

ADR-013 binding preserved: endpoint consumes repository DIRECTLY (no PublicContracts facade) per the 2026-05-20 refined boundary rule for AI-internal collaborators. Mirrors task 069's direct-injection pattern.

### Sub-task 3 — Endpoint wiring

Single line added to `EndpointMappingExtensions.MapDomainEndpoints` after `MapWorkspaceStateEndpoints()`:

```csharp
Sprk.Bff.Api.Api.Memory.PinnedMemoryEndpoints.MapPinnedMemoryEndpoints(app);
```

ZERO new top-level `Program.cs` lines. ZERO new DI registrations.

### Sub-task 4 — DTOs

Five DTOs in `PinnedMemoryEndpoints.cs`:
- `CreatePinRequest` — `{ title, content, pinType, matterId? }`
- `UpdatePinRequest` — same shape as Create
- `PinResponse` — `{ item: PinDto }` (POST 201 + PUT 200)
- `PinListResponse` — `{ items: PinDto[], count }`
- `PinDto` — `{ pinId, pinType, title, content, matterId?, createdAt, updatedAt, createdBy }`

`PinDto.pinId` is the wire id (the `{pinId}` portion of the Cosmos doc id), NOT the full document id. Mirrors task 069's `ManagePinnedContextHandler.ExtractPinIdFromDocumentId` pattern.

### Sub-task 5 — Endpoint tests (15 tests)

NEW `tests/unit/Sprk.Bff.Api.Tests/Api/Memory/PinnedMemoryEndpointsTests.cs` — mirrors `WorkspaceStateEndpointsTests` pattern: in-process `WebApplicationFactory<Program>`, fake auth handler emitting `oid` + optional `tid`, mocked `IPinnedContextRepository`. ADR-015 counter verification via MeterListener.

---

## ADR governance summary

- **ADR-008**: every endpoint inherits group-level `RequireAuthorization()` + per-handler tid/oid claim extraction.
- **ADR-010**: ZERO new top-level DI registrations. Single `MapPinnedMemoryEndpoints()` call inside `EndpointMappingExtensions.MapDomainEndpoints`.
- **ADR-013**: direct `IPinnedContextRepository` injection per the 2026-05-20 refined AI-internal collaborator boundary.
- **ADR-014**: every repository call forwards `tenantId`; cross-tenant impossible by partition key.
- **ADR-015 (BINDING)**: telemetry emits `[PINNED-MEMORY]` tag + decision discriminator + deterministic IDs + length-class flags ONLY. NEVER title body. NEVER content body. NEVER request raw text. Verified by 2 dedicated MeterListener tests.
- **ADR-016**: `ai-context` rate limit (60/min sliding window) at group level.
- **ADR-029**: BCL-only implementation; +0.00 MB (44.71 vs 44.72 baseline).
- **NFR-03 (no new ADRs)**: honored — established `WorkspaceStateEndpoints` + task 069 pattern.
- **NFR-16 (per-tenant isolation)**: tenant from `tid` claim ONLY; verified by `ListPins_ScopesByCallerTidClaim_NeverAcceptsTenantQuery`.

---

## Files touched

### Created
- `src/server/api/Sprk.Bff.Api/Api/Memory/PinnedMemoryEndpoints.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Api/Memory/PinnedMemoryEndpointsTests.cs`
- `projects/spaarke-ai-platform-unification-r6/notes/task-070-partA-evidence.md`

### Modified
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IPinnedContextRepository.cs` — added `GetByIdAsync` + `UpdateAsync` to interface.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PinnedContextRepository.cs` — added two method implementations (ReadItemAsync 404-idempotent + ReplaceItemAsync with length-cap validation).
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` — single line to wire `MapPinnedMemoryEndpoints(app)`.
- `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` — 070 🔲 → 🟡 (PART A done; PART B frontend pending).
- `projects/spaarke-ai-platform-unification-r6/current-task.md` — task 070 PART A entry (this file).

---

## Outstanding

- **PART B (frontend components)** — separate dispatch. The four Fluent v9 components per POML:
  - `PinnedMemoryListWidget` (Context pane widget; groups by pinType; filter + search)
  - `PinnedMemoryEditDialog` (create / edit form)
  - `PinnedMemoryDeleteConfirmation` (cross-session impact warning)
  - `PinnedMemoryProvenanceBadge` (chat vs UI source attribution — depends on follow-up data-layer extension; see PART A evidence note for the `source` field design)
- **Provenance source-field follow-up** — `PinnedContextItem` currently has no `source` discriminator. PART B can either stub the badge ("Created via UI" default) OR a follow-up task adds `source` to the data model + endpoints + chat handler. Documented in PART A evidence note.

---

## Wave C-G15 PART A → PART B transition

PART B (frontend Fluent v9 components) is the canonical next dispatch. BFF contract is fully testable + documented for PART B consumption per the handoff notes in `notes/task-070-partA-evidence.md`.
