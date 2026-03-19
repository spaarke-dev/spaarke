# SprkChat Platform Enhancement R2

> **Last Updated**: 2026-03-17
>
> **Status**: Complete

## Overview

SprkChat R2 elevates the AI workspace companion to copilot-quality by adding markdown rendering, SSE streaming, playbook dispatch with semantic matching, source document context injection, dynamic slash commands, web search synthesis, multi-document analysis, document upload, and Open in Word. The centerpiece is the **Playbook Dispatcher + UI Handoff Pattern** — intent recognition → playbook execution → pre-populated dialog handoff.

## Quick Links

| Document | Description |
|----------|-------------|
| [Spec](./spec.md) | AI implementation specification |
| [Design](./design.md) | Original design document |
| [Plan](./plan.md) | Implementation plan with parallel execution groups |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [Project Context](./CLAUDE.md) | AI context for Claude Code |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | 100% |
| **Target Date** | — |
| **Completed Date** | 2026-03-17 |
| **Owner** | Ralph Schroeder |

## Problem Statement

SprkChat R1 delivered the foundational AI workspace companion (contextual launch, inline toolbar, plan preview, write-back, slash commands, quick action chips). User testing revealed gaps: raw markdown display, missing source document context, static commands, no streaming, no playbook dispatch, and no web search synthesis. These gaps prevent SprkChat from reaching copilot-quality UX.

## Solution Summary

R2 addresses all identified gaps through 13 in-scope work items organized into 6 parallel-optimized phases. The implementation extends the existing BFF API (per ADR-013), enhances the SprkChat shared component library, and adds new Code Page dialogs. The Playbook Dispatcher uses two-stage semantic matching (AI Search vector similarity → LLM refinement) with typed outputs and HITL/autonomous execution modes.

## Graduation Criteria

The project is considered **complete** when:

- [x] Chat messages render formatted markdown (no raw symbols visible)
- [x] SprkChat references and reasons about source document content
- [x] Conversation-aware chunking re-selects relevant chunks per turn
- [x] Slash commands change dynamically based on analysis context
- [x] PlaybookDispatcher matches natural language to playbooks with parameter extraction
- [x] Typed playbook outputs (dialog, navigation, download, insert) work end-to-end
- [x] HITL vs autonomous execution modes function correctly
- [x] Chat responses stream token-by-token via SSE
- [x] Web search synthesizes results with scope-guided citations
- [x] Scope capabilities contribute commands independent of active playbook
- [x] Multi-document analysis works with 5+ documents
- [x] Document upload processes and injects into context within 15 seconds
- [x] Uploaded documents can optionally persist to SPE
- [x] "Open in Word" generates .docx and opens in Word Online
- [x] AnalysisChatContextResolver returns real capabilities (not stubbed)
- [x] Write-back streams via SSE and updates Lexical editor via BroadcastChannel

## Scope

### In Scope
1. Markdown rendering standardization (single pipeline, all surfaces)
2. Source document context injection (conversation-aware semantic chunking, 30K budget)
3. Write-back via SSE + BroadcastChannel (streaming delivery, real-time editor update)
4. Dynamic slash commands (metadata-driven, no relationship table)
5. Playbook Dispatcher (semantic matching, typed outputs, HITL vs autonomous)
6. SSE streaming (token-by-token chat responses)
7. Web search synthesis (AI-composed responses with citations, scope-guided)
8. Scope capabilities independent of playbooks
9. Multi-document context + document upload (drag-and-drop, optional SPE persist)
10. Open in Word (.docx generation, SPE upload, Word Online)
11. AnalysisChatContextResolver real implementation
12. JPS architecture extensions (output node, autonomous flag, trigger metadata)
13. Playbook embedding index (dedicated AI Search index)

### Out of Scope
- Voice input/output
- Mobile-responsive SprkChat layout
- Playbook builder UI (visual editor)
- Multi-user collaborative chat sessions
- Offline/disconnected mode
- Custom AI model fine-tuning per tenant
- Static playbook relationship tables

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Extend BFF, no separate AI service | Minimize infrastructure, shared auth | ADR-013 |
| Redis-first caching for context resolver | Cross-request state, session scoping | ADR-009 |
| Dedicated playbook-embeddings AI Search index | Clean separation, independent scaling | — |
| Dataverse fields for matching + JPS JSON for execution | Queryable discovery + runtime behavior | — |
| Conversation-aware chunking (not static) | Accuracy over latency for long documents | — |
| Optional SPE persist for uploads | User control over document lifecycle | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| DI registration ceiling (ADR-010 ≤15) | Med | High | Instantiate tools directly in factory, not DI |
| 128K token budget exceeded with multi-doc | High | Med | Strict budget partitioning, user notification on truncation |
| Bing API not provisioned (web search) | Med | Med | Implement with mock first, wire real API when ready |
| SSE reconnection on navigation | Med | Med | Session persistence in Redis, resumable streams |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| R1 complete (29 tasks) | Internal | Ready | SprkChat foundation in place |
| Azure OpenAI text-embedding-3-large | External | Ready | Deployed for playbook embeddings |
| Azure AI Search | External | Ready | For dedicated playbook-embeddings index |
| Document Intelligence | External | Ready | For uploaded document processing |
| Redis | External | Ready | ADR-009 |
| Bing Web Search API | External | Pending | GitHub #232 — mock until provisioned |
| Open XML SDK | External | Ready | Already in use by DocxExportService |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-03-17 | 1.0 | Project initialized via /project-pipeline | Claude Code |
