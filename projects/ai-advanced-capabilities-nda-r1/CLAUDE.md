# CLAUDE.md — `ai-advanced-capabilities-nda-r1` (project context)

> Loads with every task in this project. Root `CLAUDE.md` rules still apply. Code wins over docs.

## 🚨 Task execution protocol (MANDATORY)
Execute every task via the **`task-execute`** skill (root CLAUDE.md §4). Do NOT read POML files and implement manually. Declare rigor level at task start.

## What this project is
First **analysis/advisory** AI vertical: NDA review inside Compose. **North star**: Claude/ChatGPT-level advisory output, deliberately relaxing deterministic guardrails (ADR-039 amendment) while staying accurate, cited, human-verified, "not legal advice." Compose is the single surface. See `spec.md`.

## Binding gates for this project
- **ADR-039 amendment = task 001, a MERGE GATE** — no advisory-tier task merges before it (FR-00). Deterministic (fact) vs advisory (probabilistic) grounding tiers.
- **§10 BFF Hygiene** — tasks touching `Sprk.Bff.Api` (model-tier, orchestration, Summary-Page writer, comment materialization): state Placement Justification (cite `.claude/constraints/bff-extensions.md`), verify publish ≤60 MB, no new HIGH CVE. Hot-path: BFF=Y, SpaarkeAi=Y.
- **§11 Reuse-first** — reuse the canonical seeds (PLAN.md §2); `DocxAnnotationWriter.cs` is RETIRED → use `ComposeShadowPatchEngine`/`ComposeDocumentRenderer`.
- **NFR-06 grounding tenant pin** — reference docs seeded `tenantId="system"`; verify NDA-REVIEW's execution tenant matches or grounding returns zero.

## Applicable ADRs (load per task by tag)
**AI/dispatch**: ADR-039 (amended here), ADR-040 (ledger), ADR-043 (execution spine), ADR-041 (confirmation/OutcomeCard), ADR-013 (facade), ADR-016 (model tier — Path C), ADR-015/014 (governance/caching), ADR-032 (kill-switch) · **Compose**: ADR-049 (shadow document — central), ADR-037 (composite streaming), ADR-033 (doc-stream SSE), ADR-030 (PaneEventBus) · **Storage/auth**: ADR-007 (SpeFileStore), ADR-028 (auth) · **UI**: ADR-006 (Code Pages), ADR-021 (Fluent v9), ADR-012 (shared lib), ADR-031 (stage lifecycle) · **Testing**: ADR-038.

## Key constraints / patterns
`.claude/constraints/`: bff-extensions (binding), ai, api, auth, azure-deployment, testing · `.claude/patterns/ai/`: public-contracts-facade, endpoint-di-symmetry, node-executor-authoring, streaming-endpoints, indexing-pipeline, text-extraction · `.claude/patterns/ui/`: fluent-v9-component-authoring, embedded-widget-sizing.

## Entry points
- AI dispatch: `Services/Ai/Chat/SessionDispatchOrchestrator.cs`, `LinearConsumers/ActionRunner.cs`
- Model tier: `Services/Ai/PublicContracts/Binding.cs` (`AiModelTier`), `Configuration/DocumentIntelligenceOptions.cs`
- Compose server: `Services/Compose/ComposeShadowPatchEngine.cs` (`ApplyComment`), `ComposeDocumentRenderer.cs`, `ComposeService.cs`
- Compose client: `Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx`, `ComposeWorkspace.tsx`, `useComposeWorkspaceReceivers.ts`, `hooks/useComposeCommentThreads.ts`
- RAG: `Services/Ai/ReferenceRetrievalService.cs`, `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs`
- Action/binding seeds: `infra/dataverse/actions/compose-*.action.json`, `infra/dataverse/sprk_playbookconsumer-rows.json`

## Current state
See `current-task.md`. Task index: `tasks/TASK-INDEX.md` (pending `/task-create`).
