# HANDOFF → core (redesign-r2): Compose Phase 9b UAT fixes MERGED + DEPLOYED — re-merge before your next deploy

> From compose-r2, 2026-07-10 (later same day). Follows `HANDOFF-to-core-deploy-done-flip-rows-2026-07-10.md`.

## What happened
Owner smoke test found 7 real Compose defects (the AI surface was unit-green / user-broken). We remediated them in **Phase 9b** (tasks 110–114) and **merged to master (PR #627, master `69255dd4f`) + redeployed BFF + SpaarkeAi to spaarkedev1**. `/healthz` Healthy; new `/api/compose/active-document` route live.

## What touches YOUR co-owned surface (additive — verify on your next master re-merge)
Task 113 added a **session-scoped active-document identity** to fix the chat↔Compose bridge:
- **`Models/Ai/Chat/ChatSession.cs`** — NEW nullable `ActiveDocument` (`init` prop) + `ActiveDocumentIdentity` record. **Additive**: positional ctor + all existing members unchanged; SSE frame byte-identical for existing inputs. If you reference ChatSession, this is a pure add.
- **`Services/Ai/Handlers/SendWorkspaceArtifactHandler.cs`** — injects `ChatSessionManager`; when the LLM sends no explicit doc pointer it resolves the Compose mount from `session.ActiveDocument`, and defaults `layoutName="Compose"` when none supplied. Explicit-pointer path unchanged. `ValidateChat` relaxed (layout no longer required for Compose intent).
- **`Services/Ai/Sessions/StoredSession.cs` + `Services/Ai/Chat/ChatSessionManager.cs`** — warm-tier Cosmos round-trip of `ActiveDocument` (so it survives restore; ADR-040).
- **`Api/ComposeEndpoints.cs`** — new `POST /api/compose/active-document` register endpoint (reuses the existing chat-documents upload path for bytes; no dup).

**Frozen files respected**: `OutputRouter.cs` and `Binding.cs` were NOT touched (E-20). No new AI dispatch endpoint (ADR-039). No new NuGet package. ADR-013: the register endpoint injects `ChatSessionManager` (session-state, not an AI-capability type) — Compose facade NetArch test green; accepted **Path A** per CLAUDE.md §6.5.

## Action for you
- On your next master re-merge you'll pick up the additive `ActiveDocument` + handler changes. We grep-verified no breaking change to the co-owned contract, but confirm on your side.
- If you deploy BFF from master, you now carry these fixes (that's the point — prevents the earlier branch-only clobber). No rows-off dependency here; the earlier 3-retired-rows request still stands separately.

— compose-r2, 2026-07-10
