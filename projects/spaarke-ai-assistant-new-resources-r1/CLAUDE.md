# CLAUDE.md — Spaarke AI Assistant: new AI Search index + SPE container (R1)

> **Project status**: Draft / pending review
> **Started**: 2026-05-28
> **Trigger**: Spaarke AI Assistant integration failure surfaced 2026-05-28 — `400 Bad Request: 'privilege_group_ids' is not a filterable field` on every RAG search. Index schema is immutable; recreating would lose data; new index + new container chosen as the path.

## Quick reference

| Resource | Name | Notes |
|---|---|---|
| New AI Search index | `spaarke-file-index` | 28 fields, 3072-dim vectors only. See `spec.md` §1. |
| New SPE container | `Spaarke Dev Container 2` | Type: `Spaarke PAYGO 1` (id `8a6ce34c-6055-4681-8f87-2f4f9f921c06`). |
| Search service | `spaarke-search-dev` | existing, no change |
| BFF setting to flip | `AiSearch__KnowledgeIndexName=spaarke-file-index` | single switch |
| Legacy index (retained) | `spaarke-knowledge-index-v2` | keeps existing customer docs |
| Legacy container (retained) | (existing) | not migrating per 2026-05-28 decision |

## Active task

See `tasks/TASK-INDEX.md`. None executing yet.

## Project structure

- `design.md` — context, decision, rationale (read first)
- `spec.md` — exact schema, container details, BFF config (the authoritative source)
- `plan.md` — sequenced phases with effort estimates
- `CLAUDE.md` — this file (project context for agents)
- `tasks/` — POML task files (planned: 6 tasks)
- `tasks/TASK-INDEX.md` — task tracker

## Conventions

- Follow the standard Spaarke project conventions (see root `CLAUDE.md`).
- Use the `task-execute` skill for every numbered task.
- Each task targets exactly one phase from `plan.md`.
- After each task: update `current-task.md` (when this project becomes active) and `tasks/TASK-INDEX.md` (🔲 → ✅).

## Related work

- Parent investigation plan: `~/.claude/plans/let-s-do-some-solution-piped-acorn.md` (the AI Assistant investigation that surfaced this work)
- Predecessor index project: `projects/ai-spaarke-platform-enhancments-r3/` (created the `spaarke-rag-references` index — a different concept; this project does not affect it)
- ADR-013: AI Architecture (BFF-centric AI)
- ADR-014: Tenant isolation in caching/storage (`tenantId` field on every chunk)
- ADR-015: AI data governance (privilege trimming via `privilege_group_ids`)
- ADR-028: Spaarke Auth v2 (MI for outbound; KV refs for secrets — this project does not change auth)

## Open questions for review

Listed in `design.md` §"Open questions" and `plan.md` §"Decision log". The big ones:
1. Are the 3 new fields (`containerId`, `lastModified`, `sourceSystem`) the right additions? (Author proposes yes; team review pending.)
2. The default-container wiring in Phase 4 — confirm the correct code path (`BulkRagIndexingJobHandler` vs. `PlaybookChatContextProvider`).

## Out of scope

- Migrating existing SPE files / re-indexing into the new index.
- Retiring the legacy index or container.
- Changes to `spaarke-rag-references`, `discovery-index`, or `playbook-embeddings` indexes.
- Auth model changes (use the model from ADR-028 + the 2026-05-28 KV-API-key exception for OpenAI documented separately).
