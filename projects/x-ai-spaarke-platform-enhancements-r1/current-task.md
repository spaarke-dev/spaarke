# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-02-22 00:00
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | AIPL-055 — C6: Build SprkChat React Shared Component |
| **Step** | Not started |
| **Status** | not-started |
| **Next Action** | Begin AIPL-055 or check TASK-INDEX.md for next pending task |

### Files Modified This Session (AIPL-054)
- `src/server/api/Sprk.Bff.Api/Api/Filters/AiAuthorizationFilter.cs` - Fixed: changed empty-documentIds case from returning 400 to pass-through (next()) for session-scoped endpoints
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentContentSafetyMiddleware.cs` - Fixed pre-existing build error: ChatResponseUpdate.Text (read-only) → Contents.Add(new TextContent())
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs` - Fixed pre-existing build error: ChatResponseUpdate.Text (read-only) → Contents.Add(new TextContent())
- `tests/integration/Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs` - Fixed: added comprehensive service stubs (11 tests all pass)
- `tests/integration/Spe.Integration.Tests/Api/Ai/KnowledgeBaseEndpointsTests.cs` - Fixed: added same stubs + SearchModelFactory response + ServiceBusClient mock (13 tests all pass)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/054-build-chat-endpoints.poml` - Status → completed
- `projects/ai-spaarke-platform-enhancements-r1/tasks/TASK-INDEX.md` - AIPL-054 🚧 → ✅

### Files Modified This Session (AIPL-052)
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatMessage.cs` - Created: record with ChatMessageRole enum (User=726490000/Assistant=726490001/System=726490002) (AIPL-052)
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs` - Created: record with SessionId, TenantId, DocumentId, PlaybookId, CreatedAt, LastActivity, IReadOnlyList<ChatMessage> Messages (AIPL-052)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/IChatDataverseRepository.cs` - Created: testability seam for Dataverse persistence (sprk_aichatsummary + sprk_aichatmessage) (AIPL-052)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatDataverseRepository.cs` - Created: concrete implementation using IDataverseService (AIPL-052)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs` - Created: Redis hot path (24h sliding TTL) + Dataverse cold path; cache key "chat:session:{tenantId}:{sessionId}" (AIPL-052)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatHistoryManager.cs` - Created: AddMessageAsync, GetHistoryAsync, TriggerSummarisationAsync (threshold=15), ArchiveHistoryAsync (threshold=50) (AIPL-052)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs` - Added IChatDataverseRepository (scoped), ChatSessionManager (scoped), ChatHistoryManager (scoped); DI count 97→99 (AIPL-052)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ChatSessionManagerTests.cs` - Created: 13 tests (Redis hit/miss/TTL, cache key, Dataverse persist, delete) (AIPL-052)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ChatHistoryManagerTests.cs` - Created: 12 tests (message persist, summarisation at 15, archive at 50, GetHistoryAsync) (AIPL-052)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/052-implement-chat-session-manager.poml` - Status → completed (AIPL-052)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/TASK-INDEX.md` - AIPL-052 🔲 → ✅; Phase 4 count 3/10 → 4/10 (AIPL-052)
- `projects/ai-spaarke-platform-enhancements-r1/CLAUDE.md` - DI count updated to 99 (AIPL-052)

### Files Modified This Session (AIPL-053)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/DocumentSearchTools.cs` - Created: SearchDocumentsAsync + SearchDiscoveryAsync with [Description] attributes, tenantId isolation, MinScore 0.7f/0.5f
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/AnalysisQueryTools.cs` - Created: GetAnalysisResultAsync + GetAnalysisSummaryAsync calling IAnalysisOrchestrationService
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/KnowledgeRetrievalTools.cs` - Created: GetKnowledgeSourceAsync (redirect) + SearchKnowledgeBaseAsync with semantic+vector+keyword search
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/TextRefinementTools.cs` - Created: RefineTextAsync + ExtractKeyPointsAsync + GenerateSummaryAsync using IChatClient
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` - Updated ResolveTools(): instantiates all 4 tool classes directly (not DI), registers 9 AIFunction instances
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs` - Added AiChatMessage alias to resolve ChatMessage type ambiguity (Models.Ai.Chat vs Microsoft.Extensions.AI)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/Tools/DocumentSearchToolsTests.cs` - Created: 14 unit tests (Description attrs, tenantId isolation, topK, empty results, formatting, validation)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentTests.cs` - Fixed ChatMessage ambiguity (added AiChatMessage alias), updated all test setup/callback/capture types
- `projects/ai-spaarke-platform-enhancements-r1/tasks/053-implement-chat-tools.poml` - Status → completed
- `projects/ai-spaarke-platform-enhancements-r1/tasks/TASK-INDEX.md` - AIPL-053 🔲 → ✅; Phase 4 count updated to 3/10

### Files Modified This Session (AIPL-051)
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatContext.cs` - Created record: SystemPrompt, DocumentSummary, AnalysisMetadata, PlaybookId (AIPL-051)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/IChatContextProvider.cs` - Created interface: GetContextAsync(documentId, tenantId, playbookId, ct) → ChatContext (AIPL-051)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` - Created: loads system prompt from playbook Action record via ScopeResolverService (AIPL-051)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs` - Created: IChatClient wrapper with system prompt injection, tool support, streaming via GetStreamingResponseAsync (AIPL-051)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` - Created: singleton factory, resolves IChatContextProvider from async scope per call (AIPL-051)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs` - Added SprkChatAgentFactory (singleton) + IChatContextProvider/PlaybookChatContextProvider (scoped) (AIPL-051)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentTests.cs` - Created: 10 unit tests for SprkChatAgent (AIPL-051)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryTests.cs` - Created: 4 unit tests for SprkChatAgentFactory (AIPL-051)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/051-implement-sprk-chat-agent.poml` - Status → completed (AIPL-051)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/TASK-INDEX.md` - AIPL-051 🔲 → ✅; Phase 4 count updated (AIPL-051)
- `projects/ai-spaarke-platform-enhancements-r1/CLAUDE.md` - DI count updated to 97 (AIPL-051)

### Files Modified This Session (AIPL-036)
- `src/server/api/Sprk.Bff.Api/Api/Ai/HandlerEndpoints.cs` - Added GET /api/ai/tools/handlers endpoint returning IEnumerable<IAiToolHandler> class names (AIPL-036)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/036-verify-handler-discovery-api.poml` - Status → completed (AIPL-036)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/TASK-INDEX.md` - AIPL-036 🔲 → ✅ (AIPL-036)

### Files Modified This Session (AIPL-015)
- `src/server/api/Sprk.Bff.Api/Api/Ai/KnowledgeBaseEndpoints.cs` - Created (AIPL-015): 5 endpoints under /api/ai/knowledge
- `src/server/api/Sprk.Bff.Api/Program.cs` - Added app.MapKnowledgeBaseEndpoints() (AIPL-015)
- `tests/integration/Spe.Integration.Tests/Api/Ai/KnowledgeBaseEndpointsTests.cs` - Created (AIPL-015): 11 integration tests
- `projects/ai-spaarke-platform-enhancements-r1/tasks/015-build-knowledge-base-endpoints.poml` - Status → completed (AIPL-015)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/TASK-INDEX.md` - AIPL-015 🔲 → ✅ (AIPL-015)

### Files Modified This Session (AIPL-050)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs` - Added IChatClient registration via AddChatClient + AsIChatClient() (AIPL-050)
- `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` - Added Microsoft.Extensions.AI.OpenAI 10.3.0 and OpenAI 2.8.0 packages (AIPL-050)
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` - Added AzureOpenAI:Endpoint and AzureOpenAI:ChatModelName config keys (AIPL-050)
- `projects/ai-spaarke-platform-enhancements-r1/CLAUDE.md` - Updated DI count to 95, added new package versions (AIPL-050)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/050-integrate-agent-framework.poml` - Status → completed (AIPL-050)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/TASK-INDEX.md` - AIPL-050 🔲 → ✅ (AIPL-050)

### Files Modified This Session (Prior tasks)
- `projects/ai-spaarke-platform-enhancements-r1/notes/design/dataverse-chat-schema.md` - Created (AIPL-001)
- `projects/ai-spaarke-platform-enhancements-r1/scripts/Deploy-ChatEvaluationSchema.ps1` - Created (AIPL-001)
- `projects/ai-spaarke-platform-enhancements-r1/spec.md` - Updated unresolved questions resolved (AIPL-001)
- `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` - Added Agent Framework packages (AIPL-002)
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` - Added AiSearch/LlamaParse/AzureOpenAI sections (AIPL-002/003)
- `src/server/api/Sprk.Bff.Api/Options/LlamaParseOptions.cs` - Created (AIPL-004)
- `src/server/api/Sprk.Bff.Api/Options/AiSearchOptions.cs` - Created (AIPL-004)
- `src/server/api/Sprk.Bff.Api/Program.cs` - Added Configure<LlamaParseOptions> + Configure<AiSearchOptions> (AIPL-004)
- `projects/ai-spaarke-platform-enhancements-r1/CLAUDE.md` - DI baseline documented + package versions pinned (AIPL-002/004)
- `projects/ai-spaarke-platform-enhancements-r1/tasks/TASK-INDEX.md` - Phase 1 marked ✅; AIPL-016 marked ✅
- `infrastructure/ai-search/knowledge-index.json` - Created (AIPL-016)
- `infrastructure/ai-search/discovery-index.json` - Created (AIPL-016)
- `projects/ai-spaarke-platform-enhancements-r1/scripts/Provision-AiSearchIndexes.ps1` - Created (AIPL-016)
- `projects/ai-spaarke-platform-enhancements-r1/notes/design/ai-search-schema.md` - Created (AIPL-016)

### Critical Context
Phase 1 complete. Key findings: (1) DI baseline is already 89 (not 0) — new AI services MUST use feature module extension methods to comply with ADR-010. (2) Dataverse entities designed but NOT yet created — run Deploy-ChatEvaluationSchema.ps1 before AIPL-051. (3) Microsoft.Agents.AI 1.0.0-rc1 + Microsoft.Extensions.AI 10.3.0 pinned. (4) appsettings.template.json (not appsettings.json) is the config file.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | AIPL-016 |
| **Task File** | tasks/016-configure-two-index-ai-search-schema.poml |
| **Title** | Provision Two-Index Azure AI Search Schema and Semantic Config |
| **Phase** | 2 — Workstream A: Retrieval Foundation |
| **Status** | completed |
| **Started** | 2026-02-23 |
| **Completed** | 2026-02-23 |
| **Rigor Level** | FULL — infrastructure + bff-api tags, creates Azure resources, 6 steps |

---

## Progress

### Completed Steps

- [x] Phase 1, AIPL-001: Define Dataverse Chat + Evaluation Entities (2026-02-23) — schema in notes/design/dataverse-chat-schema.md
- [x] Phase 1, AIPL-002: Add Agent Framework NuGet Packages (2026-02-23) — 0 errors build
- [x] Phase 1, AIPL-003: Configure Two-Index AI Search Architecture (2026-02-23) — appsettings.template.json updated
- [x] Phase 1, AIPL-004: Wire Foundational DI Registrations (2026-02-23) — LlamaParseOptions/AiSearchOptions created
- [x] Phase 2, AIPL-016: Provision Two-Index AI Search Schema (2026-02-23) — JSON schemas + PowerShell script + documentation created

### Current Step

Phase 1 complete. Ready to begin Phase 2 (Workstream A) + Phase 3 (Workstream B) in parallel.

### Files Modified (All Task)

See "Files Modified This Session" in Quick Recovery section above.

### Decisions Made

- 2026-02-23: DI baseline is 89 (pre-existing). New AI services MUST use feature module extension methods (not inline Program.cs registrations) — ADR-010 compliance
- 2026-02-23: Dataverse entities need manual creation via Deploy-ChatEvaluationSchema.ps1 (pac CLI lacks table create commands)
- 2026-02-23: Config file is appsettings.template.json (not appsettings.json) — values injected at runtime via Azure App Settings
- 2026-02-23: Microsoft.Agents.AI 1.0.0-rc1 requires Microsoft.Extensions.AI >= 10.3.0 (bumped from 10.0.x)

---

## Next Action

**Next Step**: Phase 2 (Workstream A) + Phase 3 (Workstream B) in parallel

**⚠️ PREREQUISITE ACTION REQUIRED**: Before running AIPL-051 (ChatSessionManager), the Dataverse entities must exist. Run:
```powershell
pwsh "projects/ai-spaarke-platform-enhancements-r1/scripts/Deploy-ChatEvaluationSchema.ps1"
```

**Parallel Execution Groups (start simultaneously)**:
- **Agent A group**: AIPL-010 (RagQueryBuilder), AIPL-011 (SemanticDocumentChunker), AIPL-012 (DocumentParserRouter) — independent, different files
- **Agent B group**: AIPL-030 (Actions), AIPL-031 (Skills), AIPL-032 (Knowledge Sources), AIPL-033 (Tools) — independent Dataverse seed data
- Phase 2 and Phase 3 can run fully in parallel (different files/owners)

**Sequential after parallel groups**:
- AIPL-013 after 011+012; AIPL-016 after 003; AIPL-014 after 013; AIPL-015 after 010+014
- AIPL-034 after 030+031+032+033; AIPL-035 after all seed data; AIPL-036+037 last

**Key Context for next tasks**:
- New services must use feature module extension methods (DI baseline already at 89)
- Use `IOptions<AiSearchOptions>` and `IOptions<LlamaParseOptions>` — already registered
- Config file: `appsettings.template.json` (not appsettings.json)
- Agent Framework: Microsoft.Agents.AI 1.0.0-rc1, Microsoft.Extensions.AI 10.3.0 (pinned)

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-02-22
- Focus: Project initialization via /project-pipeline

### Key Learnings
- Workstream A and B can run in parallel (different files/ownership)
- Workstream C C1/C2 can start overlapping with Phase 3 (B)
- Phase D requires ALL of A+B+C complete
- Agent Framework RC (Feb 19, 2026) is available and API-stable

### Handoff Notes
*No handoff notes yet*

---

## Quick Reference

### Project Context
- **Project**: ai-spaarke-platform-enhancements-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-001: Minimal API + BackgroundService — no Azure Functions
- ADR-002: Thin plugins — no AI processing; seed data is records only
- ADR-004: Job contract — idempotent handlers, deterministic key
- ADR-008: Endpoint filters — authorization on all new endpoints
- ADR-009: Redis-first caching — tenant-scoped keys
- ADR-010: DI minimalism — <= 15 non-framework registrations
- ADR-013: AI in BFF — extend BFF, not separate service
- ADR-021: Fluent v9 — dark mode, WCAG 2.1 AA
- ADR-022: PCF platform libraries — React 16, unmanaged solutions

### Knowledge Files
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` — AI architecture reference
- `docs/guides/RAG-ARCHITECTURE.md` — RAG patterns
- `docs/guides/PCF-DEPLOYMENT-GUIDE.md` — PCF deployment procedures
- `docs/guides/HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md` — Scope creation guide

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load task file**: `tasks/{task-id}-*.poml`
4. **Load knowledge files**: From task's `<knowledge>` section
5. **Resume**: From the "Next Action" section

**Commands**:
- `/project-continue` - Full project context reload + master sync
- `/context-handoff` - Save current state before compaction
- "where was I?" - Quick context recovery

---

*This file is the primary source of truth for active work state. Keep it updated.*
