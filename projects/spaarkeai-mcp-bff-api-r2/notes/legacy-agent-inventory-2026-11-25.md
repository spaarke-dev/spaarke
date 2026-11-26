# Legacy "Spaarke AI" agent: function inventory vs MCP (2026-11-25)

> Read-only code inventory of master at `2fc3542b28` (code-mapper agent, Sonnet). Facts with file:line; dispositions are in `design.md` §3. Paths are relative to `src/server/api/Sprk.Bff.Api/Api/` unless they start with `src/solutions`.

## Sources

- `src/solutions/CopilotAgent/spaarke-api-plugin.json`: 28 functions in `run_for_functions`.
- `spaarke-bff-openapi.yaml`: maps each function to a method and path.
- `declarativeAgent.json`: instructions only.

## Adaptive cards

- No function is bound to a card in the plugin or the spec.
- Cards are built server-side in `Agent/AdaptiveCardFormatterService.cs:34-297`, and only on `/api/agent/*`.
- The formatter calls in `Agent/AgentEndpoints.cs:276` and `:540` are commented out as TODO.
- The live card producer is `Agent/PlaybookInvocationService.cs`, at `:179`, `:257`, `:334` and `:391`.

## Functions

| function | method+path | purpose | R/W | BFF endpoint | service | MCP? | blockers |
|---|---|---|---|---|---|---|---|
| semanticSearch | POST /api/ai/search | hybrid doc search | R | Ai/SemanticSearchEndpoints.cs:80 | ISemanticSearchService `:177` | no | — |
| searchRecords | POST /api/ai/search/records | find matters/projects/events/docs by name | R | Ai/RecordSearchEndpoints.cs:40 | IRecordSearchService `:164` | no | free-text query; query construction not checked |
| listDocuments | GET /api/v1/documents | list docs | R | DataverseDocumentsEndpoints.cs:622 | IDocumentDataverseService | no | — |
| getDocument | GET /api/v1/documents/{id} | doc metadata | R | DataverseDocumentsEndpoints.cs:38 | IDocumentDataverseService `:40` | no | — |
| getDocumentPreviewUrl | GET /api/documents/{documentId}/preview-url | preview URL | R | FileAccessEndpoints.cs:65 | not traced | no | — |
| summarizeFile | POST /api/workspace/files/summarize | summarise an uploaded file | R | Workspace/WorkspaceFileEndpoints.cs:81 | IFileSummarizeAi `:158`, ITextExtractor `:157` | no | multipart upload `:156`; SSE |
| listChatPlaybooks | GET /api/ai/chat/playbooks | chat playbooks | R | Ai/ChatEndpoints.cs:323 | IPlaybookService `:1832` | no | — |
| listPublicPlaybooks | GET /api/ai/playbooks/public | public playbooks | R | Ai/PlaybookEndpoints.cs:98 | IPlaybookLookupService `:354` | no | — |
| executePlaybook | POST /api/ai/playbooks/{id}/execute | run playbook on a document | W | Ai/PlaybookRunEndpoints.cs:54 | IPlaybookOrchestrationService.ExecuteAsync `:197` | no | streams; may exceed 30 s |
| getPlaybookRunStatus | GET /api/ai/playbooks/runs/{runId} | run progress | R | Ai/PlaybookRunEndpoints.cs:69 | GetRunStatusAsync `:245` | no | polling |
| getPlaybookRunDetail | GET /api/ai/playbooks/runs/{runId}/detail | run results | R | Ai/PlaybookRunEndpoints.cs:108 | GetRunDetailAsync `:465` | no | — |
| listEvents | GET /api/v1/events | list tasks/events | R | Events/EventEndpoints.cs:105 | IEventDataverseService.QueryEventsAsCallerAsync `:607` | no | BFF identity + BFF-applied caller filter (`:29`) |
| getEvent | GET /api/v1/events/{id} | event detail | R | Events/EventEndpoints.cs:117 | IEventDataverseService | no | as above |
| createEvent | POST /api/v1/events | create task/event | W | Events/EventEndpoints.cs:129 | recordCreation.CreateAsync `:747` | no | as above |
| completeEvent | POST /api/v1/events/{id}/complete | complete event | W | Events/EventEndpoints.cs:170 | IEventDataverseService | no | as above |
| getPortfolio | GET /api/workspace/portfolio | portfolio overview | R | Workspace/WorkspaceEndpoints.cs:32 | PortfolioService `:140` | no | — |
| getBriefing | GET /api/workspace/briefing | daily briefing | R | Workspace/WorkspaceEndpoints.cs:58 | BriefingService `:261` | no | — |
| getMatterAiSummary | POST /api/workspace/matters/ai-summary | AI matter summary | R | Workspace/WorkspaceMatterEndpoints.cs:65 | briefingAi.GenerateNarrativeAsync `:235` | no | SSE `:177-276` |
| getMatterFinanceSummary | GET /api/finance/matters/{matterId}/summary | budget and spend | R | Finance/FinanceEndpoints.cs:96 | IFinanceSummaryService `:609` | no | finance filter-held auth |
| sendCommunication | POST /api/communications/send | send email | W | CommunicationEndpoints.cs:111 | CommunicationService.SendAsync `:552` | no | irreversible |
| handleAgentMessage | POST /api/agent/message | conversational gateway | W | Agent/AgentEndpoints.cs:46 | session manager + agent `:262` | no | streams; session state |
| listAgentPlaybooks | GET /api/agent/playbooks | playbooks for the agent | R | Agent/AgentEndpoints.cs:62 | IPlaybookService `:334`, `:357` | no | duplicate |
| runAgentPlaybook | POST /api/agent/run-playbook | run playbook | W | Agent/AgentEndpoints.cs:80 | IPlaybookOrchestrationService `:445` | no | streams |
| getAgentPlaybookStatus | GET /api/agent/playbooks/status/{jobId} | job status | R | Agent/AgentEndpoints.cs:97 | GetRunStatusAsync `:495` | no | cache-backed job tracker |
| createAnalysis | POST /api/ai/analysis/create | start analysis | W | Ai/AnalysisEndpoints.cs:70 | CreateAnalysisAsync `:1335` | no | UI hand-off |
| getAnalysis | GET /api/ai/analysis/{analysisId} | analysis status/results | R | Ai/AnalysisEndpoints.cs:137 | GetAnalysisAsync `:718` | no | — |
| getCurrentUser | GET /api/me | profile | R | UserEndpoints.cs:20 | handler `:73` | no | — |
| getUserCapabilities | GET /api/me/capabilities | capabilities | R | UserEndpoints.cs:25 | SpeFileStore.GetUserCapabilitiesAsync | no | needs `containerId` |

Dead functions: none.

## MCP tools today

All five are on `/mcp/legal-operations` (`Mcp/McpEndpoints.cs:58`) and call `IDecisionCapability`. None overlaps an agent function.

- `spaarke_show_decisions` (R)
- `spaarke_list_decision_page` (R)
- `spaarke_get_decision_plan` (R)
- `spaarke_search_decision_lookup` (R)
- `spaarke_complete_decision` (W)

## Agent gateway

- **Wiring:** `MapAgentEndpoints()` is called at `Infrastructure/DI/EndpointMappingExtensions.cs:480`, and `AddAgentModule` at `Program.cs:232`.
- **Live routes:** the four routes at `Agent/AgentEndpoints.cs:46`, `:62`, `:80` and `:97`.
- **Not checked:**
  - `SpaarkeAgentHandler.cs:130,202`: whether the Bot activity handlers are registered;
  - whether `EmailDraftService` has a caller.

## Not covered by this inventory

- The service implementations behind each handler.
- `appPackage/manifest.json` wiring.
- The contents of `cards/*.json`.
- The `/api/me` handler body.
