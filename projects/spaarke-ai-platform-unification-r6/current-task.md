# Current Task State — R6 (Wave C-G6 closeout — task 069 complete)

> **Last Updated**: 2026-06-18 (Wave C-G6 closeout)
> **Mode**: Wave C-G6 (Pillar 7 voice command memory primitives) — closed
> **Branch**: `work/spaarke-ai-platform-unification-r6`

---

## Wave C-G6 closeout summary

| Task | Status | Title | Tests | Evidence note |
|------|--------|-------|-------|---------------|
| 069 | ✅ | "Remember / forget / always" recognition + ManagePinnedContextHandler chat tool (D-C-23 / FR-47) | 16 / 0 ManagePinnedContext + 28 / 0 (1 skip) CapabilityRouterVoiceMemory + 1090 / 0 (4 skipped) broader CapabilityRouter / Handler / Memory regression sweep | [task-069-evidence.md](notes/task-069-evidence.md) |

**Build status**: BFF clean (0 errors, 16 pre-existing warnings).
**Publish-size**: 44.72 MB compressed (+0.01 MB vs task-068 44.71 MB baseline; BCL-only handler + regex pre-pass; no NuGet deps added).
**CVE**: no new vulnerabilities; pre-existing Kiota Abstractions 1.21.2 HIGH unchanged.

**Wave C-G6 status**: 1 of 1 tasks closed. The voice memory primitives ("remember X" / "forget X" / "always X") are now live: the CapabilityRouter Layer 0 pre-pass recognises the three patterns and biases the LLM toward `manage_pinned_context`; the handler creates / deletes `PinnedContextItem` rows via the task-065 repository; the task-067 hierarchical memory composition automatically picks up the new pins on the next chat turn.

---

## What Wave C-G6 produced

### Sub-task 1 — `ManagePinnedContextHandler` (Pillar 7 / FR-47)

NEW chat-only `IToolHandler` exposing a single LLM-facing function:
`manage_pinned_context(action: "create" | "delete", pinType: "user-preference" | "system-rule" | "matter-fact", title, content?)`.

- `action=create` + `pinType=user-preference` ← "remember X" voice mapping
- `action=create` + `pinType=system-rule` ← "always X" voice mapping
- `action=delete` ← "forget X" voice mapping (match by case-insensitive title against the user's pins of the same pinType; refused_not_found returns a structured response — not an error — when no match is found)
- `pinType=matter-fact` exists for completeness but is NOT a voice mapping; task 070's Pinned Memory UI is the canonical matter-fact write surface.

Injects `IPinnedContextRepository` (task 065) directly per ADR-013's 2026-05-20 refined AI-internal collaborator boundary. Auto-discovered via `ToolFrameworkExtensions.AddToolHandlersFromAssembly` per ADR-010 (ZERO new `Program.cs` lines). Seed row: `infra/dataverse/sprk_analysistool-manage-pinned-context-row.json`; registered in `scripts/Seed-TypedHandlers.ps1` (idempotent UPSERT).

### Sub-task 2 — `CapabilityRouter` Layer 0 voice command pre-pass

NEW pre-pass in `CapabilityRouter.RouteSync` that matches `^(remember|forget|always)\b` (case-insensitive, compiled regex) BEFORE Layer 1 keyword scoring. On match, returns a Confident result selecting the synthetic `manage_pinned_context` capability + emits a `context.decision_made` event (layer=`layer0`, decision=`voice_memory`). Non-matching messages fall through to Layer 1 unchanged. Word-boundary anchoring prevents `remembered` / `forgetfulness` from false-firing.

### Sub-task 3 — `ChatInvocationContext.UserId` additive field

NEW optional `string? UserId` field on `ChatInvocationContext` carrying the chat session's principal `oid` claim (Azure AD GUID rendered as string). Required by `ManagePinnedContextHandler` because `PinnedContextItem` is user-scoped (the `IPinnedContextRepository` contract requires non-empty `UserId` on `CreateAsync`). Wired at the existing `SprkChatAgentFactory.ResolveTools` `contextFactory` closure — extracted from `httpContext.User.FindFirst("oid")`. Nullable so back-compat is preserved (existing chat tools that don't read `UserId` are unaffected).

### Sub-task 4 — Task 068 follow-up housekeeping

4 pre-existing test files (`PlaybookChatContextProviderTests`, `PlaybookChatContextProviderEnrichmentTests`, `PlaybookChatContextProviderEnrichmentIntegrationTests`, `SprkChatAgentFactoryPersonaTests`) were broken at task-068 closeout because that task made `IMatterMemoryService` a required ctor param on `PlaybookChatContextProvider`. The task-068 evidence note claimed the ctor accepted the new dep as nullable for back-compat, but the actual production ctor sig is non-nullable. Each broken test received a minimal 1-line fix (pass `new Mock<IMatterMemoryService>().Object`) so the test project compiles and the new task-069 tests can run. This is the SAME spirit as the "no backward-compat hacks for small counts" memory — 4 broken tests is well within the migrate-them-now range.

---

## ADR governance summary

- **ADR-010**: auto-discovery via assembly scan; ZERO new `Program.cs` lines. `IPinnedContextRepository` already registered in `AiPersistenceModule` (task 065).
- **ADR-013**: handler injects `IPinnedContextRepository` DIRECTLY (no PublicContracts facade) per the 2026-05-20 refined AI-internal collaborator boundary. Mirrors the same direct-injection rationale that landed in tasks 067 (hierarchical composition) and 068 (matter-memory activation).
- **ADR-014**: every repository call forwards `context.TenantId`; cross-tenant reads/writes are structurally impossible. Counter dimensions include `tenantId` as deterministic identifier only.
- **ADR-015 (BINDING)**: telemetry dimensions = handler name + decision + action + pinType + title LENGTH + content PRESENCE + deterministic IDs (tenantId, userId, sessionId, pinId) + duration ONLY. The user-authored title body and content body NEVER touch the telemetry sink. Verified by 2 dedicated tests (`ExecuteChatAsync_Telemetry_Adheres_ToAdr015_OnCreate` + `..._OnDelete`) using the `TypedToolHandlerTestFixture.AssertTelemetryRespectsAdr015` scanner.
- **ADR-029**: BCL-only implementation; +0.01 MB compressed delta vs task-068 baseline.
- **NFR-03 (no new ADRs)**: honored — no new ADR introduced. The pre-pass uses an existing pattern (regex + IContextEventEmitter); the field addition uses an existing pattern (additive nullable property mirroring `MatterId` / `AnalysisId`).
- **NFR-10 (8K budget)**: no impact — the handler does not consume system-prompt budget; pinned items injected by task-067 composition continue to use the existing `IPromptBudgetTracker` `memory-composition` layer tag.

---

## Files touched

### Created
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/ManagePinnedContextHandler.cs`
- `infra/dataverse/sprk_analysistool-manage-pinned-context-row.json`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/ManagePinnedContextHandlerTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterVoiceMemoryTests.cs`
- `projects/spaarke-ai-platform-unification-r6/notes/task-069-evidence.md`

### Modified
- `src/server/api/Sprk.Bff.Api/Services/Ai/ChatInvocationContext.cs` — added `string? UserId` field.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — populate `UserId` at the contextFactory closure.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs` — Layer 0 voice command pre-pass.
- `scripts/Seed-TypedHandlers.ps1` — added `MANAGE-PINNED-CONTEXT` row entry.
- `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` — 069 🔲 → ✅.
- `projects/spaarke-ai-platform-unification-r6/current-task.md` — Wave C-G6 closeout entry (this file).
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookChatContextProviderTests.cs` — task 068 follow-up (Mock<IMatterMemoryService>).
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookChatContextProviderEnrichmentTests.cs` — task 068 follow-up.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookChatContextProviderEnrichmentIntegrationTests.cs` — task 068 follow-up.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryPersonaTests.cs` — task 068 follow-up.

---

## Outstanding

- **Dataverse deploy is USER ACTION**: the seed script (`scripts/Seed-TypedHandlers.ps1`) is idempotent and now includes the new row. Per project sequencing decision the user deploys separately — main session does NOT run `pac` / `mcp__dataverse__*`.
- **Task 070** (C-G15 — Q7 expansion: Pinned Memory CRUD + visualization UI) is now unblocked. Task 069's `manage_pinned_context` tool + the task 065 repository surface together cover the chat-side write paths; task 070 owns the user-direct UI write surface + visualization.
- The four task-068 follow-up test file fixes are minimal (1 line each) and intentionally do not migrate the test fixtures to exercise matter-memory — those tests cover the non-matter-memory enrichment paths and the matter-memory call is a no-op for them (no host context with `EntityType=="matter"` is set up). A more thorough migration could exercise matter-memory directly, but that's a follow-up housekeeping pass and not load-bearing for task 069.

---

## Wave C-G6 → C-G7 transition

Task 070 (Wave C-G7, FULL rigor, parallel-safe = false) is the canonical next task. Its dependencies (065, 069) are satisfied.
