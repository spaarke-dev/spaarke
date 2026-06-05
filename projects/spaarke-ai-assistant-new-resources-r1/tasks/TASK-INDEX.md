# Task Index — Spaarke AI Assistant: new AI Search index + SPE container

> Status legend: 🔲 pending · 🔄 in progress · ✅ complete · ⏭️ skipped · ❌ blocked

| # | Task | Status | Rigor | Phase | Est. effort |
|---|---|---|---|---|---|
| 001 | Create `spaarke-file-index` in `spaarke-search-dev` | 🔲 | STANDARD | 1 | 1.5h |
| 002 | Create `Spaarke Dev Container 2` in `Spaarke PAYGO 1` container type | 🔲 | STANDARD | 2 | 0.5h |
| 003 | Update BFF App Service settings (`AiSearch__KnowledgeIndexName`, `SharePointEmbedded__DefaultContainerId`) | 🔲 | MINIMAL | 3 | 0.5h |
| 004 | BFF code changes: add `DefaultContainerId` to options + wire indexing pipeline | 🔲 | FULL | 4 | 1.5h |
| 005 | Deploy + smoke test (Phase 5 of plan.md) | 🔲 | STANDARD | 5 | 1h |
| 006 | Documentation: provisioning guide + FAILURE-MODES entry | 🔲 | MINIMAL | 6 | 1h |

**Total estimated effort**: ~6 hours sequential, or ~4 hours with Phases 1 + 2 + 6 parallelized.

## Notes

- Tasks are not yet authored as POML files. To begin execution, use the `task-create` skill to decompose `plan.md` into POML tasks under this folder.
- Task 001 has highest leverage — get the schema exactly right (see `spec.md` §1) since the index is immutable. Spec.md was authored from the deployed `spaarke-knowledge-index-v2` schema dump + code field-usage audit, but a second reviewer should validate before deploy.
- Tasks 002 and 001 can run in parallel.
- Task 004 (code changes) needs Tasks 001 and 002 complete first (so it has a real container id and index to point at).
