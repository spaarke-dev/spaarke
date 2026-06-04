# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Wave E task 042 in progress 2026-06-03; 040 + 041 + 043 ✅; 042 is final Wave E impl.

---

## 🎯 Active task — 042 (E3) — Spaarke Assistant integration (🔄 IN PROGRESS 2026-06-03)

**Rigor**: FULL
**Status**: in-progress
**Wave**: E
**Effort**: 1w (estimated)
**Parallel-safe**: No (depends on 040 + 041)

### Rigor declaration

🔒 RIGOR LEVEL: FULL
📋 REASON: Tags `bff-api`, `integration`, `tool-call-contract`, `spaarke-assistant`, `cross-team`; modifies `.cs` files in `Sprk.Bff.Api` Zones A + B; adds new endpoint + service + facade method + integration tests; cross-team contract.

### Sub-task scoping (per task POML + owner direction)

| Sub-task | Automatable | Approach |
|---|---|---|
| A — Author tool-call contract | ✅ | `projects/.../design-e3-tool-call-contract.md` (canonical) |
| A.5 — Review with Assistant team | ❌ | Deferred — owner-mediated; marked PENDING in design doc |
| B — Implement contract in BFF | ✅ | `Services/Ai/Insights/AssistantToolCallHandler.cs` + `Api/Insights/InsightsAssistantEndpoint.cs` + `IInsightsAi.AssistantQueryAsync` |
| B.5 — Integration tests | ✅ | `tests/.../InsightsAssistantEndpointTests.cs` (12 cases) |
| C — Assistant-side integration | ❌ | Out of r2 scope per POML — author hand-off doc `notes/e3-assistant-team-handoff.md` |
| 6 — Quality gates | ✅ | code-review + adr-check + §3.5 grep + publish-size + format |

### Foundation already in place (040 + 041)

- `IInsightsAi.SearchAsync` + `AnswerQuestionAsync` — both already wired
- `IInsightsIntentClassifier` + `NullInsightsIntentClassifier` (ADR-032 P3) — registered in DI
- `forceMode` wire field plumbed on `/ask` + `/search` (cross-endpoint mismatch returns 400)
- `InsightsPlaybookNameMapOptions` — canonical name → per-env Guid resolution
- `ISubjectParser` (Wave D5) — multi-scheme subject parsing (matter/project/invoice)
- `FeatureDisabledException` + `AsFeatureDisabled503()` — uniform 503 ProblemDetails

### Steps tracker (POML)

| # | POML Step | Status |
|---|---|---|
| A | Author canonical tool-call contract (design-e3-tool-call-contract.md) | 🔄 |
| A.5 | Owner-mediated Assistant team review | ⏭️ deferred |
| B | Implement contract in BFF (handler + endpoint + facade method + DI) | 🔲 |
| B.5 | Integration tests (12 cases) | 🔲 |
| C | Assistant-side integration handoff doc | 🔲 |
| 6 | Quality gates | 🔲 |

### Active key files

- POML: `projects/ai-spaarke-insights-engine-r2/tasks/042-spaarke-assistant-integration.poml`
- Design output: `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` (NEW)
- Handler: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/AssistantToolCallHandler.cs` (NEW)
- Endpoint: `src/server/api/Sprk.Bff.Api/Api/Insights/InsightsAssistantEndpoint.cs` (NEW)
- Facade: `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` (extend with `AssistantQueryAsync`)
- Facade DTOs: `Models/Ai/PublicContracts/AssistantQueryFacadeRequest.cs` + `AssistantQueryFacadeResult.cs` (NEW)
- Wire DTOs: `Models/Insights/InsightsAssistantQueryRequest.cs` + `InsightsAssistantQueryResponse.cs` (NEW)
- DI: `Infrastructure/DI/AnalysisServicesModule.cs` (add `AssistantToolCallHandler` registration)
- Endpoint mapping: `Infrastructure/DI/EndpointMappingExtensions.cs` (add `MapInsightsAssistantEndpoint`)
- Tests: `tests/unit/Sprk.Bff.Api.Tests/Api/Insights/InsightsAssistantEndpointTests.cs` (NEW — 12 cases)
- Handoff: `projects/ai-spaarke-insights-engine-r2/notes/e3-assistant-team-handoff.md` (NEW)

### ADRs in scope

ADR-001, ADR-008, ADR-010, ADR-013-refined, ADR-016, ADR-019, ADR-028, ADR-029, ADR-032 (P3 for handler)

---

## 🎯 Previous task — 043 (E4) — Playbook-vs-RAG decision-tree doc (✅ 2026-06-03)

(History condensed — see TASK-INDEX.md for the full log.)

---

*Active 2026-06-03 — Wave E task 042 in flight.*
