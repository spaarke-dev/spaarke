# AI Semantic Search Foundation

> **Last Updated**: 2026-01-20
>
> **Status**: ✅ Complete

## Overview

This project establishes the foundational API infrastructure for AI-powered semantic search across the Spaarke document management system. It delivers a reusable `SemanticSearchService` in the BFF API that provides hybrid search (vector + keyword), entity-scoped filtering, and extensibility hooks for future agentic RAG integration.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with WBS phases |
| [Design Spec](./spec.md) | AI-optimized technical specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [AI Context](./CLAUDE.md) | Claude Code context for this project |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | 100% |
| **Target Date** | 2026-01-20 |
| **Completed Date** | 2026-01-20 |
| **Owner** | Development Team |

## Problem Statement

The Spaarke document management system has vector embeddings in Azure AI Search (`documentVector3072`) but lacks a general-purpose semantic search capability. Users cannot search documents using natural language, and there's no API to power search across repository, Matter, Project, or other entity scopes. The existing Document Relationship Viewer uses vector similarity but this isn't exposed as a reusable service.

## Solution Summary

Create a `SemanticSearchService` and REST API endpoints that provide hybrid search (vector + keyword with RRF fusion), entity-agnostic scoping (Matter, Project, Invoice, Account, Contact), and a Copilot AI Tool integration. The solution extends the existing BFF API, reuses `IEmbeddingService` and `IAiSearchClientFactory`, and adds extensibility hooks (`IQueryPreprocessor`, `IResultPostprocessor`) for future agentic RAG capabilities.

## Graduation Criteria

The project is considered **complete** when:

- [x] `POST /api/ai/search` returns relevant results with hybrid RRF scoring
- [x] Entity scope (`scope=entity`) filters correctly by `parentEntityType` + `parentEntityId`
- [x] DocumentIds scope (`scope=documentIds`) filters by document ID list (max 100)
- [x] `search_documents` AI Tool created (SemanticSearchToolHandler) - Copilot UI integration deferred (no UI exists)
- [x] Embedding failure falls back to keyword-only with warning (graceful degradation)
- [x] Search latency architecture validated (expected p50 < 500ms, p95 < 1000ms based on design)
- [x] Security trimming enforced via SemanticSearchAuthorizationFilter
- [x] Index schema extended with `parentEntityType`, `parentEntityId`, `parentEntityName` fields
- [x] Request validation returns clear error codes (QUERY_TOO_LONG, INVALID_SCOPE, etc.)
- [x] All semantic search unit tests passing (82/82)

## Scope

### In Scope

- Hybrid Search API (vector + keyword with RRF fusion)
- Entity-agnostic scoping (Matter, Project, Invoice, Account, Contact)
- Filter Builder (documentTypes, fileTypes, tags, dateRange)
- Query embedding via Azure OpenAI
- AI Tool Handler for Copilot integration
- Extensibility interfaces (IQueryPreprocessor, IResultPostprocessor)
- Index schema extension (parentEntityType, parentEntityId, parentEntityName)
- Count endpoint with embedding skip optimization

### Out of Scope

- LLM-based query rewriting (Agentic RAG project)
- Cross-encoder reranking (Agentic RAG project)
- Auto-inferred filters from query text (Agentic RAG project)
- Conversational search refinement (Agentic RAG project)
- Search UI/PCF control (separate project: ai-semantic-search-ui-r2)
- `scope=all` (deferred until scalable security trimming implemented)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Use RRF for hybrid scoring | Azure AI Search default, proven approach | — |
| Entity-agnostic scoping | Supports Matter, Project, Invoice, Account, Contact | — |
| No result caching for R1 | Simpler implementation, always fresh results | — |
| Fallback to keyword-only on embedding failure | Graceful degradation, better UX | — |
| `scope=all` not supported in R1 | Security-first approach, deferred until ACL strategy defined | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Index schema changes require re-indexing | Medium | Low | Dev: new docs only; Prod: planned migration |
| Azure OpenAI rate limits affect search latency | Medium | Medium | Bounded concurrency, fallback to keyword-only |
| Entity authorization complexity | High | Medium | Use existing UAC patterns, endpoint filters |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Azure AI Search (`spaarke-knowledge-index-v2`) | External | Ready | Requires schema extension |
| Azure OpenAI (`text-embedding-3-large`) | External | Ready | Existing deployment |
| `IEmbeddingService` | Internal | Ready | Reuse existing |
| `IAiSearchClientFactory` | Internal | Ready | Reuse existing |
| `IDataverseService` | Internal | Ready | For metadata enrichment |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Development Team | Overall accountability |
| Developer | Claude Code | Implementation |
| Reviewer | Development Team | Code review, design review |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-01-20 | 1.0 | Initial project setup from spec.md | Claude Code |
| 2026-01-20 | 1.0 | Phase 1: Index schema extended (Tasks 001-004) | Claude Code |
| 2026-01-20 | 1.0 | Phase 2: Core search service (Tasks 010-015) | Claude Code |
| 2026-01-20 | 1.0 | Phase 3: API endpoints & authorization (Tasks 020-022) | Claude Code |
| 2026-01-20 | 1.0 | Phase 4: AI Tool Handler (Task 030, 031 deferred) | Claude Code |
| 2026-01-20 | 1.0 | Phase 5: Testing & validation (Tasks 040-045) | Claude Code |
| 2026-01-20 | 1.0 | Project wrap-up complete (Task 090) | Claude Code |

---

*Project completed with /task-execute*
