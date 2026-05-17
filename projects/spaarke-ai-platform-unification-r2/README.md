# Spaarke AI Platform Unification R2

> **Status**: In Progress
> **Branch**: `work/spaarke-ai-platform-unification-r2`
> **Created**: 2026-05-17
> **Type**: Platform Rebuild (frontend) + Extension (backend)

## Overview

Rebuild the Spaarke AI frontend and extend the BFF backend to deliver an AI-directed three-pane experience (Conversation, Workspace, Context) with dynamic capability orchestration, AI safety perimeter, work history persistence, and interactive widget framework.

## Key Deliverables

### Frontend (Rebuild)
- Three-pane coordinated shell with unified event bus
- `@spaarke/ai-widgets` shared library with interactive widget contract
- Workspace pane with tab management (max 3)
- Context pane with adaptive stage-based rendering
- 13 R1 widgets migrated + 5 new widgets
- Safety annotation UI + feedback collection

### Backend (Extension)
- Dynamic capability orchestration (CapabilityManifest, 3-layer router)
- AI safety perimeter (Prompt Shields, Groundedness, Citation Verification, Privilege Filter)
- Work history (Redis + Cosmos DB write-through)
- ISprkAgent abstraction (R3-ready)
- Hybrid search (OData + AI Search)
- Audit trail + matter memory + prompt library

### Infrastructure
- Cosmos DB serverless provisioning
- GPT-4o-mini deployment
- AI Search index updates
- Azure AI Content Safety resource

## Graduation Criteria

1. Three-pane shell renders with coordinated event flow
2. Dynamic routing handles >60% turns via Layer 1 (no LLM)
3. Single LLM call per turn always
4. Safety perimeter operational (injection blocked, groundedness annotated, citations verified)
5. Session restore in <500ms
6. All R1 SprkChat functionality preserved
7. Widget serialize/restore works for all migrated widgets
8. Prompt token budget stays within ~9000 tokens

## Quick Links

- [Implementation Plan](plan.md)
- [Specification](spec.md)
- [Design Document](design.md)
- [Architecture](architecture.md)
- [Task Index](tasks/TASK-INDEX.md)
- [AI Context](CLAUDE.md)
