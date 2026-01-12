# AI Search & Visualization Module

> **Last Updated**: 2026-01-12
>
> **Status**: ✅ Complete

## Overview

This project delivers an AI-powered document relationship visualization module for the Spaarke Power Apps model-driven application. Users can discover semantically similar documents and explore relationships through an interactive graph interface, answering key questions like "What documents are related to this one?" and "What other Matters/Projects are similar based on their documents?"

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with WBS and phases |
| [Design Spec](./spec.md) | Original design specification |
| [Tasks](./tasks/TASK-INDEX.md) | Task breakdown and status tracking |
| [AI Context](./CLAUDE.md) | Project-specific AI context file |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | 100% |
| **Target Date** | 2026-01-12 |
| **Completed Date** | 2026-01-12 |
| **Owner** | Spaarke AI Team |

## Problem Statement

Users working with documents in Spaarke need to discover relationships between documents that would otherwise remain hidden in traditional folder-based or search-based document management. Currently, finding similar documents requires manual searching and browsing, which is time-consuming and often misses connections based on content similarity.

## Solution Summary

The AI Search & Visualization Module leverages the existing Spaarke R3 RAG architecture (Azure AI Search, Azure OpenAI embeddings, Redis caching) to provide:
1. A BFF API endpoint for finding semantically related documents
2. A DocumentRelationshipViewer PCF control with React Flow canvas and d3-force layout
3. A "Find Related" ribbon button on the `sprk_document` form that opens an interactive full-screen modal

## Graduation Criteria

The project is considered **complete** when:

- [x] `GET /api/ai/visualization/related/{documentId}` returns valid graph response with < 500ms P95 latency
- [x] DocumentRelationshipViewer PCF control renders interactive graph with d3-force layout
- [x] ~~Full-screen modal opens from "Find Related" ribbon button on sprk_document form~~ (Changed to inline section-based visualization)
- [x] Control panel filters (similarity threshold, depth, node limit) update graph in real-time
- [x] Node actions navigate correctly to Dataverse record and SPE file
- [x] Dark mode fully supported via Fluent UI v9 tokens
- [x] Integration tests pass against Azure AI Search dev environment
- [x] All existing indexed documents have `documentVector` after backfill migration
- [x] Code review and ADR-check quality gates pass

## Scope

### In Scope

- Backend API endpoint for related documents (`GET /api/ai/visualization/related/{documentId}`)
- Document-level embedding generation and storage (`documentVector` field)
- Backfill migration for existing indexed documents
- DocumentRelationshipViewer PCF control with React Flow + d3-force
- Full-screen modal with control panel and node action bar
- "Find Related" ribbon button on `sprk_document` entity
- Unit tests, component tests, and integration tests

### Out of Scope

- Mobile/tablet responsive design (model-driven apps are desktop-focused)
- Telemetry/analytics (deferred to future phase)
- Offline/disconnected support
- Microsoft Foundry IQ integration (continuing with R3 architecture)
- Cluster visualization endpoint (`/cluster/{tenantId}`)
- Multi-seed exploration endpoint (`/explore`)
- Rebuilding existing SPE file viewer, auth flows, or navigation

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Continue with R3 architecture | SharePoint Embedded not supported by Foundry IQ; R3 is battle-tested | -- |
| Full-screen modal | Maximum canvas area for complex graphs with 25+ nodes | -- |
| d3-force layout | Natural clustering, similarity-based edge length; interactive exploration | -- |
| Dedicated documentVector field | Optimal performance; aggregation fallback for existing data | -- |
| PCF over webresource | Better testability, lifecycle management, modern patterns | [ADR-006](.claude/adr/ADR-006-pcf-over-webresources.md) |
| Endpoint filters for auth | Resource-based authorization with full routing context | [ADR-008](.claude/adr/ADR-008-endpoint-filters.md) |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Performance with large graphs | High | Medium | Implement depth limiting, lazy loading, hard cap at 100 nodes |
| React Flow bundle size | Medium | Low | Tree-shaking, dynamic imports |
| Embedding drift | Medium | Low | Re-index on model version changes |
| User confusion with visualization | Medium | Medium | Clear tooltips, documentation, onboarding |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Azure AI Search index | External | Ready | Existing `spaarke-knowledge-index` |
| Azure OpenAI embeddings | External | Ready | text-embedding-3-small deployed |
| R3 RAG architecture | Internal | Production | IRagService, IEmbeddingCache |
| `sprk_document` entity | Internal | Production | Existing entity with extract fields |
| @xyflow/react npm package | External | Available | React Flow for graph visualization |
| d3-force npm package | External | Available | Force-directed layout |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Spaarke AI Team | Overall accountability |
| Developer | Claude Code | Implementation |
| Reviewer | Human Review | Code review, design review |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-01-08 | 1.0 | Initial project setup | Claude Code |
| 2026-01-12 | 2.0 | Project complete - All phases delivered | Claude Code |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
