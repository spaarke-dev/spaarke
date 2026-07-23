# Task 083 — AnchoredAnnotation Path-A Deviation: Code-Review Sign-Off

> **Reviewed**: 2026-07-10 · Branch `work/spaarkeai-compose-r2` @ post-055.
> **Protocol**: CLAUDE.md §6.5 Path A (project-scoped exception — documented + rationalized + reviewer-approved at the point of decision). Charter §3.4 bans local MemoryItem variants.
> **Subject**: task 060's `AnchoredAnnotation` persistence, in its current post-102 state (task 102 moved the collections onto `StoredSession` + the Cosmos warm tier).

---

## Decision: ✅ ACCEPT — the deviation is justified; `AnchoredAnnotation` is NOT a MemoryItem variant.

The R2 design argues `AnchoredAnnotation` is document-adjacent **positional UI state** (accept/reject/edit marks anchored to editor positions), not a memory record. Each of the five Path-A claims is verified against the code below. Notably, the implementation **self-documents** the boundary.

## Evidence (each claim verified against code)

| # | Path-A claim | Evidence | ✓ |
|---|---|---|---|
| 1 | **Not a MemoryItem type** | `AnchoredAnnotation` (+ `AnchoredAnnotationAnchor`, `AnchoredAnnotationProvenance`) are `sealed record`s in `Models/Ai/Chat/ChatSession.cs:204/239/257` — chat-session payload types. No inheritance from or reference to `MemoryItem`. | ✅ |
| 2 | **No `memory.*` write path** | Grep of `Services/Compose/**` for `memory.write` / `MemoryItem` / `IMemoryWrite` / `memory-items` returns only a **doc-comment** at `IComposeService.cs:221` explicitly stating it does NOT use memory.write. No actual memory-service call path. | ✅ |
| 3 | **Not retrieved by the Context Binder** | Grep of `Services/Ai/Context/**` and `*ContextBinder*.cs` for `AnchoredAnnotation` returns nothing. The Context Binder never reads it. | ✅ |
| 4 | **Not in the memory review/delete view** | It is not a `MemoryItem` and never enters the core memory-items container (core's PR #620 memory-items store is separate). Self-documented at `IComposeService.cs:222-223`: "*the stored collections are never surfaced in the memory review/delete view.*" | ✅ |
| 5 | **Tenant + matter scoped** | The collection lives on `StoredSession.AnchoredAnnotations` (`StoredSession.cs:203`), mapped both directions by `ChatSessionManager` (`:552`, `:623-624`). It rides the Compose **session** envelope, which is keyed by `DocumentId + MatterId` (task 062 cross-version persistence) under the caller's tenant auth boundary — session-scoped state, not a global memory tier. | ✅ |

## Self-documenting implementation (the strongest evidence)

`IComposeService.cs:216-224` carries the Path-A argument inline:

> "Annotations are mutable positional UI state — accept / reject / edit — not an append-only ledger (contrast with `ChatSession.Outputs`/`ToolChains`). **Path-A boundary (charter §3.4)**: this method MUST NOT be confused with `memory.write` — it never touches the Memory Service, never routes through the Context Binder, and the stored collections are never surfaced in the memory review/delete view."

The deviation is not hidden — it is asserted at the API surface and enforced by the ADR-013 NetArchTest facade rule (`ADR013_ComposeFacadeTests`, 2/2 green — task 081), which independently proves `Services/Compose` injects no AI/memory internals.

## Resolution

- **Path A accepted.** No fallback (negotiated MemoryItem sub-type) escalation to core is needed — the argument holds against the implementation.
- The spec.md ADR Tensions Path-A row's "Actions" line (resolve at code-review time) is **satisfied by this sign-off**. Cite this record in the wrap-up PR description.
- No silent local variant ships: `AnchoredAnnotation` is an explicit, documented, facade-guarded session-payload type — the antithesis of the charter §3.4 anti-pattern.

**Reviewer**: automated code-review pass (Claude Opus 4.8), 2026-07-10. Escalate to human architect only if core (redesign-r2) later objects to session-payload annotation state during the master-merge coordination (see `HANDOFF-to-core-shared-surface-heads-up.md` — StoredSession is a flagged shared surface).
