# Agent findings — peripheral AI surfaces (auditor 7/7, 2026-07-05)

Scope: jobs, plugins, PCF, Office add-ins, external SPA, server shared libs, MCP, Insights remnants.
Audited against MASTER.

## 1. Background jobs (`Services/Jobs/**`)
| Component | Category | Status |
|---|---|---|
| `ScheduledRagIndexingService` | Infra | Partial/opt-in — registered hosted service; `Enabled=false` default; requires TenantId or no-op. |
| `DocumentVectorBackfillService` | Infra (embeddings) | Partial/scaffolding — one-time migration, `Enabled=false`; `GetAllTenantIdsAsync` is an empty-list stub (lines 411-417). Likely no longer needed. |
| `RagIndexingJobHandler` | Infra | Working, gated `DocumentIntelligence:Enabled` — background RAG indexing (email-to-doc FR-10, bulk). |
| `InvoiceExtractionJobHandler` | **Consumer** | Working, unconditional (FinanceModule:170) — AI invoice extraction (Playbook B) → OutputOrchestrator. |
| `AttachmentClassificationJobHandler` | **Consumer** | Partial — gated `DocumentIntelligence:RecordMatchingEnabled` — AI invoice-candidate classification (Playbook A). |
| `InvoiceIndexingJobHandler` | Infra (embeddings) | Gated — embeds invoices (text-embedding-3-large, 3072-dim) → `spaarke-invoices-index`. |
| `SpendSnapshotGenerationJobHandler` | Infra | Working, **NO AI** (deterministic aggregation). |
| `IncomingCommunicationJobHandler` | Infra (delivery) | Working — email ingest on-ramp that enqueues downstream AI. |
| `DocumentProcessingJobHandler` | Infra | Working — generic doc-processing dispatch. |
| `InsightsIngestJobHandler` (+payload) | **Dispatcher** | Partial/scaffolding — Zone-B boundary routing SPE-upload jobs → `IInsightsAi.RunIngestAsync`; opt-in `AiProcessingOptions.InsightsIngest` default FALSE. Namespace firewall (imports only IInsightsAi). |
| `RecordSyncJob` | Infra | Working, gated — Dataverse records → `spaarke-records-index` (feeds record matching); no-ops without endpoint config. |
| `PlaybookSchedulerJob` (`Services/Ai/PlaybookSchedulerJob.cs`) | Dispatcher/Infra | Working — IScheduledJob; replaced DELETED `PlaybookSchedulerService` (AnalysisServicesModule:776-780 warns against re-adding). |

## 2. PCF controls
| Control | Category | Status |
|---|---|---|
| `SemanticSearchControl` | **Consumer** | Working, mature (v1.1.51) — `POST /api/ai/search`; per-scope index routing; "All Documents" union merge; tests. Most substantial peripheral AI control. |
| `ScopeConfigEditor` | **Manifest** | Working — maker editor per entity: Action/Skill/KnowledgeSource/Tool editors over the scope tables. The maker surface behind Tool/Action catalogs. |
| `EmailProcessingMonitor` | Widget/Infra | Working, deployed — email→doc AI pipeline dashboard (`/api/admin/email-processing/stats`). |
| `AIMetadataExtractor` | — | **DEAD** — `.gitkeep` only, never built. |
| `AnalysisWorkspace`, `AnalysisBuilder`, `PlaybookBuilderHost`, `DrillThroughWorkspace` (PCF) | — | **DEAD artifacts** — no source (no index.ts / ControlManifest.Input.xml); only generated ManifestTypes.d.ts + obj/out build output. Source deleted, orphaned build output committed. |

## 3. Office add-ins — NO client-side AI
- Save Flow (`useSaveFlow.ts`, `SaveFlow.tsx`, `OfficeApiClient.ts`) — **Consumer (trigger only)**: `profileSummary` (default TRUE) / `deepAnalysis` (default FALSE) / `ragIndex` flags in `POST /office/save` → server stages (ProfileSummary → Indexed → DeepAnalysis) via UploadFinalizationWorker/ProfileSummaryWorker → AppOnlyDocumentAnalysisJobHandler + RAG + optional InsightsIngest.
- LinkedTodos — confirmed **non-AI** (plain todo CRUD).

## 4. External SPA (Power Pages)
- `AiToolbar.tsx` — Consumer — 3 hardcoded playbook buttons (summarize-document / summarize-project / run-analysis); ADR-013 IP protection (no prompts client-side).
- `usePlaybookExecution.ts` — Consumer/Infra — `POST /api/v1/external/ai/playbook` → structured sections.
- `PlaybookLibraryPage.tsx` — Consumer/Dispatcher — renders shared `PlaybookLibraryShell` (browse + intent mode).
- `SemanticSearch.tsx` — Consumer — SPA variant of semantic search.
- `SmartTodo.tsx` — non-AI (display only).

## 5. Shared client libs (outside SpaarkeAi page)
- `Spaarke.DailyBriefing.Components/services/briefingService.ts` — Consumer — `POST /api/ai/daily-briefing/summarize`, 503/circuit-breaker fallback, AI-content labeling. Live (standalone page + SpaarkeAi embed).
- `Spaarke.UI.Components/.../PlaybookLibraryShell/{PlaybookLibraryShell,IntentWizardFlow,DocumentSelector}.tsx` — Consumer/Dispatcher — reusable playbook browser + intent wizard.

## 6. Dataverse plugins — CONFIRMED NO AI
Only `Spaarke.CustomApiProxy` (file-preview URL proxy). Grep for AI terms: zero matches.

## 7. Server shared libs
`Spaarke.Dataverse/IAnalysisDataverseService` (+2 impls) — Infra — analysis-record CRUD + scope N:N associate.
Rest of Spaarke.Core/Scheduling hits are generic plumbing.

## 8. COVERAGE-GAP FLAG: `Services/Workspace/**` (AI but outside Services/Ai)
All working Consumers routing through `IWorkspacePrefillAi` / PublicContracts facade (ADR-013 refined, task 046):
`WorkspaceAiService.cs` (feed/todo AI summaries), `TodoGenerationService.cs`, `BriefingService.cs`,
`MatterPreFillService.cs`, `ProjectPreFillService.cs`.

## Duplicates / overlaps
1. Semantic-search consumer ×2 (PCF control vs external-SPA component).
2. Playbook-execution client ×3 forms (external-spa hook w/ 3 hardcoded IDs vs PlaybookLibraryShell generic catalog vs SpaarkeAi page).
3. Three AI-Search index-writers with similar embedding/Search plumbing (RAG docs, invoices, records).
4. Summary generation triggered from 4 unrelated request contracts (Office save flags, AiToolbar playbooks, WorkspaceAiService, DailyBriefing).

## Confirmed-absent areas
- **MCP server: ABSENT in repo** — no src/mcp/, no ModelContextProtocol impl; only a comment in `IGroundingVerifier.cs:13` referencing an external file. (The `mcp-tool-handler` skill exists but no in-repo server.)
- Plugins: no AI. Office add-ins: no client-side AI.
- Insight Engine: Zone-B = `InsightsIngestJobHandler` (default off); Zone-A engine under `Services/Ai/Insights/**` (covered by auditor 3).

## Dead-code suspects
Empty `AIMetadataExtractor` PCF; 4 source-less PCF host dirs; `DocumentVectorBackfillService` (one-time
migration, stub method); decommissioned `PlaybookSchedulerService` pattern (already deleted, documented).
