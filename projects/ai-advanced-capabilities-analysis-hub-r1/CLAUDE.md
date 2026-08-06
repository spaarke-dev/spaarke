# CLAUDE.md — `ai-advanced-capabilities-analysis-hub-r1` (Project AI Context)

> Project-scoped context. Root `CLAUDE.md` rules still bind (§10 BFF Hygiene, §11 Component Justification, §6.5 ADR Conflict Resolution).

## Mission

Generalize the NDA vertical into a first-class **Analysis platform**: durable `sprk_analysis` spine + session↔Analysis persistence (fork-on-analysis) + hub widget + per-type wizard + clean retirement of the `sprk_analysisworkspace` code page. ≈80% reuse; net-new core = data spine + session binding + fork + record integration.

## Hot-path declaration

BFF=**Y** · SpaarkeAi=**Y** · CI=N · Skills=N · root-CLAUDE=N

**Binding coordination**: `Services/Ai/` sole-owned by `spaarke-ai-architecture-redesign-r2` → consume `Services/Ai/PublicContracts/` seams, **NO fork**; `/conflict-check` before every BFF PR. Merge-order `ConversationPane`/widget-registry against `spaarke-ai-architecture-redesign-r1`.

## Applicable ADRs (MUST / MUST NOT)

- **ADR-007** — files via `sprk_documentid` → `sprk_document` SPE hop; MUST NOT duplicate SPE pointers on `sprk_analysis`.
- **ADR-013** — CRUD→AI via `Services/Ai/PublicContracts/`; MUST NOT inject AI-internal types into CRUD or fork `Services/Ai/`.
- **ADR-024** — RegardingResolver field-set; exactly one populated regarding per Analysis.
- **ADR-028** — auth v2 on all new endpoints (`authenticatedFetch`/`DefaultAzureCredential`).
- **ADR-032** — Null-Object kill-switch for any feature-gated service behind an unconditional endpoint.
- **ADR-039** — Grounded Execution & Closed Catalogs; record-driven opens stay in code via `openSpaarkeAi`, NOT `surfaceLaunchRegistry`.
- **ADR-040** — session ledger; **Project Tension → Path A**: Cosmos = transcript store-of-record, Dataverse = anchor + outputs.
- **ADR-029** — BFF publish-size ≤60 MB; per-task report.

## MUST / MUST NOT (project-specific)

- ✅ Bind sessions to `sprk_aichatsummary` + new `→ sprk_analysis` FK (live sessionId-grouped store).
- ❌ NEVER revive/build on `sprk_analysischatmessage` (dead shell — §11 violation).
- ✅ Standardize on `ChatEndpoints` (Redis→Cosmos); ❌ NEVER extend `AnalysisEndpoints` in-memory session model.
- ✅ Files via `sprk_documentid` → `sprk_document`; ❌ NEVER duplicate SPE pointers on `sprk_analysis`.
- ✅ Record-driven modal opens via `openSpaarkeAi`; ❌ NEVER via `surfaceLaunchRegistry`.
- ✅ Exactly one populated regarding field per Analysis.
- ✅ Publish-size + CVE checks on every BFF-touching task.

## Session architecture (verified facts)

- **Session GUIDs are minted server-side** — `ChatSessionManager.cs:108` (`Guid.NewGuid`); client only reads back `data.sessionId`. → fork logic belongs on the **server** (UQ-1 Option B).
- Tiers: Redis 24h (`SessionCacheTtl`) → Cosmos (`ISessionPersistenceService` write-through) → Dataverse `sprk_aichatsummary` (create-time only; reads/archive are stubs — AIPL-054).
- Today's binding seam: `session.HostContext.EntityType="sprk_analysisoutput"` + `EntityId` → `sprk_chathistory` JSON write in `ChatEndpoints.cs:956` (per-turn).
- Legacy path (retire): `AnalysisEndpoints` `/continue`+`/resume` (in-memory, `AnalysisOrchestrationService`), `sprk_chathistory` read.
- ⚠️ `sprk_chathistory` also written by legit non-legacy paths (`ChatEndpoints` new sessions, Insights `ObservationMirrorMapper`) — confirm provenance before any deletion.

## Reuse reference impls

`DataGrid.tsx:725` + `ViewSelector.tsx` · `CreateRecordWizard.tsx` + `AssociateToStep.tsx` + `FieldMappingService.ts:110` · `WorkspaceWidgetRegistry.ts:192` + `PaneEventTypes.ts:111` (`widget_load`) · `ComposeAiToolbar.tsx:490` (`getToolsForSurface`) · `launch-resolver.ts:198,268` (`openSpaarkeAi`/`openSpaarkeAiCompose`) · `DocumentComposeLaunch.ts:91` · `FindingsWidget`/`AnalysisEditorWidget`/`NdaReviewSummaryPanel` (survive). No `fork` exists — extend `composeReviseRouting.ts`/`composeDraftRouting.ts`.

## Retirement (ordered §13.5)

Repoint 4 server C# deep-links → repoint client launch points → retire legacy session path + dead entities → delete web resources + `AnalysisWorkspace/` tree + `Deploy-AnalysisWorkspace.ps1` → reconcile 4-name casing (`sprk_analysisworkspace`, `sprk_AnalysisWorkspace`, `sprk_AnalysisWorkspaceLauncher`, dead `sprk_analysisworkspace_8bc0b`). No solution XML references these — code/scripts only. Clean retirement, no capability migration (§13.6); `SourceViewerPanel` retired not migrated.

## Task execution

Every task via `task-execute`. BFF/three-pane tasks = FULL rigor (code-review + adr-check at Step 9.5). State Placement Justification + publish-size in BFF PRs. `/conflict-check` before every BFF PR.
