# AI Document Intelligence R5: RAG Pipeline & Document Discovery

> **Status**: Planning
> **Created**: January 2026
> **Prerequisite**: R4 Complete (Playbook Scope System)

---

## Overview

R5 implements the automated RAG (Retrieval-Augmented Generation) pipeline, connecting the existing document analysis flow to searchable knowledge indexes. This enables:

1. **Knowledge Base for Analysis**: Curated reference content provides context during AI analysis
2. **Document Discovery**: "Find documents similar to this one" semantic search

## Key Deliverables

| Deliverable | Description |
|-------------|-------------|
| **Chunking Service** | Automatic document splitting for optimal RAG indexing |
| **Indexing Pipeline** | Connect analysis output to RAG indexes |
| **Two-Index Architecture** | Knowledge Base (curated) + Discovery (all documents) |
| **Knowledge Base UI** | Admin management of knowledge sources |
| **Find Similar Feature** | Semantic document search |

## Design Documents

| Document | Purpose |
|----------|---------|
| [RAG-ARCHITECTURE-DESIGN.md](RAG-ARCHITECTURE-DESIGN.md) | Comprehensive architecture and implementation plan |

## Key Findings

### Existing Resources (Leverage)

| Component | Status | Can Leverage |
|-----------|--------|--------------|
| `RagService` | Exists | Search + indexing APIs ready |
| `DocumentIntelligenceService` | Exists | Text extraction + AI analysis |
| `DocumentAnalysisResult` | Exists | Rich metadata (type, keywords, entities) |
| `ExtractedEntities` | Exists | Organizations, People, Dates, DocumentType |
| Background job infrastructure | Exists | Service Bus + handlers |

### Gaps (Must Build)

| Gap | Priority | Impact |
|-----|----------|--------|
| Chunking Service | P0 | No automatic document splitting |
| Indexing Pipeline | P0 | Analysis → RAG connection missing |
| Knowledge Base UI | P1 | No admin management interface |
| Auto-index on Upload | P1 | No discovery index population |
| Find Similar UI | P2 | No semantic search feature |

## Architecture Summary

```
File Upload → SPE Storage → Analysis → DocumentAnalysisResult
                                              ↓
                                    ┌─────────┴─────────┐
                                    ↓                   ↓
                           (if admin-selected)   (if auto-index)
                                    ↓                   ↓
                           Knowledge Base       Discovery Index
                           (curated, 10-100)    (all docs, 1000s)
                                    ↓                   ↓
                           Analysis Context     Find Similar Docs
```

## Success Criteria

- [ ] Documents analyzed with playbooks get RAG context from Knowledge Base
- [ ] New documents automatically indexed to Discovery (configurable)
- [ ] Admins can manage Knowledge Base content via UI
- [ ] Users can "Find Similar Documents" with semantic search
- [ ] Quality metrics tracked and visible

## Related Projects

| Project | Relationship |
|---------|--------------|
| R3 (Phases 1-3) | RAG infrastructure built |
| R4 (Phases 1-6) | Playbook scope system uses RAG knowledge |
| R5 (This) | Automated pipeline + discovery features |

---

*See [RAG-ARCHITECTURE-DESIGN.md](RAG-ARCHITECTURE-DESIGN.md) for full technical details.*
