# Lessons Learned — SprkChat Context Awareness R1

> **Project**: ai-sprk-chat-context-awareness-r1
> **Completed**: 2026-03-15

---

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Use Dataverse native pageType values (`entityrecord`, `entitylist`, etc.) | Aligns with `Xrm.Utility.getPageContext().input.pageType` — no translation needed | Clean detection code, no mapping layer |
| 4-tier resolution (exact → entity+any → wildcard+pageType → global fallback) | Balances specificity with admin flexibility | Covers all scenarios without excessive config |
| Nullable `Guid?` PlaybookId | Enables generic chat mode without playbook | Backward-compatible; no breaking changes |
| Redis 30-min cache + manual eviction endpoint | Avoids polling; admin controls freshness | Fast reads, admin-driven updates |
| Entity name in system prompt (not structured logs) | Privacy: enrichment is useful but PII-adjacent | AI context without telemetry exposure |
| Token budget guard (100 tokens enrichment, 8000 total) | Prevents enrichment from bloating prompts | Safe append-only, never disrupts playbook prompt |
| SessionStorage 5-min TTL on client | Reduces API calls within same tab session | Fast pane re-opens without stale data risk |

## Architecture Insights

1. **System prompt enrichment must be append-only** — inserting mid-prompt breaks playbook instructions. Always append after all other sections.

2. **PageType detection requires fallback chain** — `Xrm.Utility.getPageContext()` is not always available (depends on embedding context). URL pattern matching provides reliable fallback.

3. **Workspace detection needs an explicit allowlist** — broad `sprk_` prefix matching would false-positive on non-workspace web resources. Named allowlist (`sprk_corporateworkspace`, etc.) is safer.

4. **`sprk_analysisplaybook`** is the correct entity logical name (not `sprk_aiplaybook`). Important to verify entity names against actual Dataverse schema.

## Parallel Execution Results

- 22 tasks organized into 10 parallel execution groups (A-J)
- Up to 4 concurrent agents in Phase 1, 5 concurrent across Phases 2-4
- Total: 72 new tests (22 mapping service + 15 enrichment unit + 9 enrichment integration + 26 client)
- Build: 4,222 tests passing, 0 failures, 0 regressions

## Files Created/Modified

### New Files (18)
- `Services/Ai/Chat/ChatContextMappingService.cs` — 4-tier resolution + Redis caching
- `Models/Ai/Chat/ChatContextMapping.cs` — response DTOs
- `Tests/.../ChatContextMappingServiceTests.cs` — 22 unit tests
- `Tests/.../PlaybookChatContextProviderEnrichmentTests.cs` — 15 unit tests
- `Tests/.../PlaybookChatContextProviderEnrichmentIntegrationTests.cs` — 9 integration tests
- `SprkChatPane/__tests__/services/contextService.test.ts` — 26 client tests
- `data/chat-context-mappings.json` — seed data
- `scripts/Create-AiChatContextMapEntity.ps1` — entity creation
- `scripts/Create-AiChatContextMapForm.ps1` — admin form creation
- `scripts/Deploy-ChatContextMappings.ps1` — seed data deployment
- `scripts/Deploy-RefreshMappingsButton.ps1` — ribbon button deployment
- `scripts/Test-AdminWorkflow.ps1` — admin workflow verification
- `webresources/js/sprk_aichatcontextmap_ribbon.js` — refresh button JS
- `webresources/ribbon/sprk_aichatcontextmap_ribbon.xml` — ribbon XML

### Modified Files (10)
- `ChatHostContext.cs` — added PageType parameter
- `ChatSession.cs` / `ChatContext.cs` — nullable PlaybookId
- `PlaybookChatContextProvider.cs` — entity enrichment + PageTypeLabels
- `IChatContextProvider.cs` — nullable playbookId signature
- `SprkChatAgentFactory.cs` — null playbook handling
- `ChatEndpoints.cs` — context-mappings + cache eviction routes
- `AiModule.cs` — DI registration
- `contextService.ts` — API-driven mapping + native pageType detection
- `App.tsx` — async playbook resolution + availablePlaybooks state
- `SprkChat.tsx` / `types.ts` — onPlaybookChange callback

## Deployment Checklist

1. `.\scripts\Deploy-BffApi.ps1` — BFF API to Azure
2. `.\scripts\Create-AiChatContextMapForm.ps1` — admin form in Dataverse
3. `.\scripts\Deploy-ChatContextMappings.ps1` — seed data
4. `.\scripts\Deploy-RefreshMappingsButton.ps1` — ribbon button
5. Build + upload SprkChatPane code page to Dataverse
6. `.\scripts\Test-AdminWorkflow.ps1` — verify end-to-end
