# Design — Spaarke AI Assistant: new AI Search index + SPE container

> **Status**: Draft — pending team review
> **Date**: 2026-05-28
> **Origin**: Spaarke AI Assistant integration failure surfaced 2026-05-28 (see plan `let-s-do-some-solution-piped-acorn.md`). After resolving the auth chain (CORS + 3 RBAC gaps + `keyVaultReferenceIdentity` for UAMI + KV-backed API key for AzureOpenAI), the first real document-search query returned `400 Bad Request: 'privilege_group_ids' is not a filterable field`. The index schema is immutable after creation; recreating the existing `spaarke-knowledge-index-v2` would lose existing customer data. Hence the strategy: create new resources, route the Assistant + all new functionality to them, leave legacy resources untouched.

## Context

The Spaarke AI Assistant (`sprk_spaarkeai` Code Page and the ribbon-launched SprkChat side pane) shares a single backend chat engine (`SprkChatAgent` in `Sprk.Bff.Api`). When the agent invokes `DocumentSearchTools` or `RagService` to retrieve grounding documents, it issues an OData filter expression against `spaarke-knowledge-index-v2` that includes `privilege_group_ids/any(...)`. Azure AI Search returns `400 Bad Request` because that field is `retrievable: true` but `filterable: false`. The filter is required by the privilege-trimming security model added in AIPU2-027 (entity-scoped retrieval isolation).

Azure AI Search index schemas are **immutable** once created. The only ways to fix the field's flags are:
1. Recreate the index in place — would drop all existing indexed customer documents (data loss).
2. Create a new index alongside the old one — keeps legacy data accessible to legacy paths, points new functionality at the new index.

Option 2 is the decision. While we're creating a new index, we also create a new SharePoint Embedded container so that the Assistant's documents are isolated from legacy SPE content and the indexing pipeline has a clear scope boundary. Legacy records and their SPE container remain accessible to legacy paths (Insights Engine, existing analyses, prior chats) but are out of scope for new AI Assistant functionality.

This work is the unblocker for **Phase B and beyond** of the AI Assistant project (`let-s-do-some-solution-piped-acorn.md`). Without these new resources the Assistant cannot retrieve documents, run any RAG-backed playbook, or fulfill its core "find similar documents / find records" use cases.

## Decision

Create three new resources and route the Spaarke AI Assistant + all new AI functionality to them. Leave the legacy resources in place for backward compatibility.

| # | Resource | Name | Notes |
|---|---|---|---|
| 1 | Azure AI Search index | `spaarke-file-index` | 3072-dim vectors only (drop legacy 1536). All 25 fields from existing schema retained + `privilege_group_ids` corrected to `filterable: true` + 3 additions for future-proofing. See `spec.md` for full schema. |
| 2 | SharePoint Embedded container | `Spaarke Dev Container 2` | Under existing container type `Spaarke PAYGO 1` (ContainerTypeId `8a6ce34c-6055-4681-8f87-2f4f9f921c06`). Becomes the default container for AI Assistant document operations. |
| 3 | BFF App Service config | `AiSearch__KnowledgeIndexName=spaarke-file-index` + new `SharePointEmbedded__DefaultContainerId=<new-container-id>` | Single config switch flips the runtime to the new resources. Old config preserved as overrides for specific legacy paths if needed. |

### What stays the same

- `spaarke-knowledge-index-v2` continues to exist; legacy documents remain searchable via direct OData queries that don't use `privilege_group_ids` filter. Code paths that explicitly use this index (admin/migration tools, Insights Engine prior data) keep working.
- The existing SPE container continues to exist; legacy documents stored there remain accessible to legacy paths. No migration is planned.
- `spaarke-rag-references` index (golden reference knowledge from `ai-spaarke-platform-enhancments-r3`) is **unaffected** — different purpose, different content, separate concern.
- The OBO + MI + KV + named-API-key auth model per ADR-028 is unchanged.

### What's out of scope

- Migrating existing documents from the legacy container/index to the new ones. Decided 2026-05-28: leave SPE documents as-is.
- Retiring or deleting the legacy container/index. They stay for the foreseeable future.
- Schema changes to the `spaarke-rag-references` or `playbook-embeddings` or `discovery-index` indexes.
- Container access policies / per-customer admin handoff (the new container uses the same PAYGO container type already in use).

## Why now

- **Unblocks the Assistant**: Phase B of the AI Assistant project is gated on document retrieval working. As of 2026-05-28 the auth chain is fully fixed but RAG returns 400 on every filtered query.
- **One-time fix**: Immutability of AI Search indexes means we get one chance to do this right; doing it now while the failure context is fresh avoids missing fields later.
- **Cleaner separation**: New schema, new container, new defaults — gives the team a clean baseline for AI Assistant features and a known-good reference for future env provisioning.

## Rationale for not just fixing the existing index

We considered (and rejected) two alternatives:

| Alternative | Rejected because |
|---|---|
| Recreate `spaarke-knowledge-index-v2` in place with corrected schema | Loses all existing indexed documents. Re-indexing would take hours and risks data inconsistency during the cutover. |
| Patch the BFF code to not filter on `privilege_group_ids` | Breaks the ADR-015 / AIPU2-027 entity-scoped retrieval isolation requirement. Cross-matter document leakage is a real security risk. Non-starter. |

Creating a new index + new container costs ~1 day of work but gives us a clean baseline and zero impact on existing data.

## Decision drivers (priority order)

1. **Security** — `privilege_group_ids` must be filterable so cross-matter retrieval isolation works (ADR-015, AIPU2-027).
2. **Data safety** — no destruction of existing indexed documents or SPE files.
3. **Schema completeness** — capture every field any current OR planned AI consumer needs, since the index is immutable.
4. **Operational simplicity** — single config switch to point the BFF at the new resources; clear back-out path (revert the config).
5. **ADR alignment** — preserve ADR-028 auth model (MI for outbound, KV for secrets, etc.); preserve ADR-013 BFF-centric AI architecture; preserve ADR-014 tenant isolation; preserve ADR-015 data governance.

## Open questions (resolve in spec.md)

1. **Backward-compat field `documentVector3072`** — code in `RagService.cs` reads it for document-similarity visualization. Keep? Yes (no harm; small storage cost).
2. **Embedding dimensions** — confirm 3072 (text-embedding-3-large) is still the strategic choice. Yes — `ai-spaarke-platform-enhancments-r3` standardized on this and `spaarke-rag-references` uses the same.
3. **New fields to add for future-proofing** — `containerId`, `lastModified`, `sourceSystem` (proposed). See spec.md for justification.
4. **Container type vs. new container type** — using existing `Spaarke PAYGO 1` container type (decided 2026-05-28). No new container type needed.
