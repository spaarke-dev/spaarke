# Deferrals & Discovered Issues — email-communication-intelligence-r2

> Source of truth (pairs with GitHub Issues for visibility). Every entry names a concrete behavior/contract
> that fails without the work (§11). File the GitHub Issue via `/defer` (or at push — `push-to-github`
> Step 1.6 blocks push on unfiled entries).

---

## DEFER-030-01 — RAG grounding for non-representable regarding types (service request et al.)

| Field | Value |
|---|---|
| **Filed** | 2026-08-05 (task 030) |
| **GitHub Issue** | {URL} — file via `/defer` |
| **Type** | Deferral (scope boundary) |
| **Concrete failure** | A `sprk_communication` whose **primary** regarding is a **service request** (or work assignment / event / budget / report card / analysis / organization) indexes with `ParentEntity = null`, so a service-request-scoped RAG query returns **zero** of that SR's correspondence — the same class of gap FR-D1 fixes for matters/projects, still open for these types. |
| **Why deferred** | `ParentEntityContext.EntityTypes` (the AI-owned grounding contract) supports only matter / project / invoice / account / contact. Extending it is a change to a shared model consumed by chat / insights / RAG endpoints (`Models/Ai/ParentEntityContext.cs`) and is owned by the AI surface (ADR-013 / §11) — out of scope for a Communication-side task. `RegardingParentEntityMapper` deliberately degrades to null rather than fabricating an unsupported scheme or misfiling to a lower-priority regarding. |
| **Fix shape (future)** | Extend `ParentEntityContext.EntityTypes.All` (+ the index `parentEntityType` filter + `ISearchIndexNameResolver` chain) to cover service request, then add the mapping row in `RegardingParentEntityMapper.RepresentableTypeMap`. Coordinate with the AI-architecture owner (spaarke-ai-architecture-redesign-r2). |
