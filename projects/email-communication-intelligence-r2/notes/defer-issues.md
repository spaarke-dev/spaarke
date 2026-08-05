# Deferrals & Discovered Issues — email-communication-intelligence-r2

> Source of truth (pairs with GitHub Issues for visibility). Every entry names a concrete behavior/contract
> that fails without the work (§11). File the GitHub Issue via `/defer` (or at push — `push-to-github`
> Step 1.6 blocks push on unfiled entries).

---

## DEFER-030-01 — RAG grounding for non-representable regarding types — ✅ RESOLVED (2026-08-05)

| Field | Value |
|---|---|
| **Filed / Resolved** | 2026-08-05 (task 030) → resolved same day (operator direction) |
| **GitHub Issue** | Not filed — resolved before push (no residual product-need work). |
| **Original concern** | A `sprk_communication` whose **primary** regarding is a **service request** (or the non-core types work assignment / event / budget / report card / analysis / organization) indexed with `ParentEntity = null` → a service-request-scoped RAG query returned zero of that SR's correspondence. |
| **Resolution** | Investigated the downstream chain and found the grounding path is a **generic string passthrough**: the RAG query filter is `parentEntityType eq '…'` (`RagService`), `SearchIndexNameResolver` routes the index by the source-entity lookup / tenant default (NOT by `parentEntityType`), and the only type-switch (`AiAnalysisNodeExecutor.MapToRecordEntityType`, a separate records-index feature) already degrades gracefully to null for unknown types. So extending it is safe. **Added `servicerequest`** — the one *core* auto-file type (matter / project / service request) that was missing — to `ParentEntityContext.EntityTypes` + `RegardingParentEntityMapper.RepresentableTypeMap` + a seam test. Build clean; 9 mapper + 141 downstream (normalizer / semantic-search / chat-host) tests green; conflict-check on `ParentEntityContext.cs` clean. |
| **Residual (intentional, NOT deferred)** | The remaining non-core types (work assignment / event / budget / report card / analysis / organization) are **intentionally not RAG-grounding parents** — RAG scoping by those is not a product need. Documented in `RegardingParentEntityMapper`'s XML docs with the one-line recipe to add one later (type const + map row; the filter is generic, no downstream change). No tracking needed. |
| **Cross-worktree note** | Touched the AI-owned `Models/Ai/ParentEntityContext.cs` (spaarke-ai-architecture-redesign-r2 surface). Additive (one const + one `All` entry). **Re-run `/conflict-check` before the PR.** |
