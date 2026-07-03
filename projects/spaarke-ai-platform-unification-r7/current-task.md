# Current Task State — spaarke-ai-platform-unification-r7

> **Last Updated**: 2026-07-03 (post-conversation on composition model; ready to start Phase 12.3a client work)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Session** | R7 close — Wave 12.3+ chat-summarize + Playbook-manifest composition model |
| **Status** | **Server side of Wave 12.3 complete + merged to master** (PR #546, commit `5f8543457`). **Client migration NOT started.** **Manifest overhaul phases 12.4–12.7 NOT started.** |
| **Branch** | `work/spaarke-ai-platform-unification-r7` — synced with master (commit `12c62b711` merges origin/master into work branch) |
| **Worktree** | `c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7/` |
| **BFF live at commit `a34631f03`** on `spaarke-bff-dev` (deployed 2026-07-03 pre-merge; content-identical to `5f8543457`) — hash-verify ✓, `/healthz` 200, `/summarize` 401 |
| **Master local repo** | `c:/code_files/spaarke` synced to `5f8543457` |
| **Next Action** | Start Phase 12.3a client audit — see [`notes/r7-close-plan-2026-07-03.md`](notes/r7-close-plan-2026-07-03.md) §4 Phase 12.3a for tasks. First subtask: locate `SprkChatMessageRenderer` slash-command handling + `PlaybookCandidateSelector` invocation. |

---

## Companion docs (READ IN THIS ORDER)

1. **Canonical R7 close plan** — [`notes/r7-close-plan-2026-07-03.md`](notes/r7-close-plan-2026-07-03.md) — **NEW — READ THIS FIRST**. Design decisions locked; phased execution scope; vocabulary.
2. Wave 12.3 canonical plan (chat-summarize) — [`notes/wave12-3-assistant-summarize-canonical-plan.md`](notes/wave12-3-assistant-summarize-canonical-plan.md)
3. Wave 12 tech debt inventory — [`notes/wave12-linear-migration-tech-debt.md`](notes/wave12-linear-migration-tech-debt.md)
4. R7 delivery + UAT — [`notes/wave12-uat-checklist-2026-07-02.md`](notes/wave12-uat-checklist-2026-07-02.md)

---

## What is DONE (Wave 12.3 server side)

Committed on `a34631f03`, merged to master via PR #546:

- **Chat Summarize Linear dispatch**: `SessionSummarizeOrchestrator` checks `IConsumerRoutingService.ResolveActionAsync("chat-summarize")` first → if populated (it is — routing row `sprk_action = eeb05bfd-1260-f111-ab0b-70a8a59455f4`), dispatches to `FileSummarizeService.ExecuteAsync(text, filename, ConsumerTypes.ChatSummarize, ctx, ct)`. Fall-through preserves engine path.
- **`FileSummarizeService.ExecuteAsync` parameterized** — accepts `consumerType` arg (was hardcoded). Same class serves both Workspace File Summary AND Chat Summarize.
- **`ISessionFileTextSource` + `SessionFileTextSource`** — session-scoped RAG (`SessionId`, keyword-only, no vector/semantic ranking) concatenates all chunks for target files with per-file headers for multi-file input.
- **Option 3 routing table refactor**:
  - Added `sprk_action` LOOKUP column to `sprk_playbookconsumer` (→ `sprk_analysisaction`)
  - Extended `IConsumerRoutingService` with `ResolveActionAsync(consumerType)` — same query + selection algorithm as `ResolveAsync`; returns `sprk_action` GUID
  - `ActionResolver` refactored to use `ResolveActionAsync` (retired `LinearConsumersOptions.ActionIds` map)
  - Populated `sprk_action` on 4 existing rows (chat-summarize, summarize-file, matter-pre-fill, project-pre-fill)
  - Created new routing row for `document-profile` (Doc Upload wizard's IActionResolver lookup)
  - Retired `LinearConsumersOptions.ActionIds` + `TryGetActionId`
  - Deleted 4 `LinearConsumers__ActionIds__*` App Settings on `spaarke-bff-dev`
- **Deploy verified**: 46.84 MB package, hash-verify 4/4 ✓, `/healthz` 200, `POST /api/ai/chat/sessions/.../summarize` returns 401 (route registered, needs auth)

---

## What is NOT DONE — the R7 close path

Follow [`notes/r7-close-plan-2026-07-03.md`](notes/r7-close-plan-2026-07-03.md) §4 Phase order. Summary:

| Phase | Purpose | Estimate |
|---|---|---|
| **12.3a** (NEXT) | Chat client rewire + Doc Upload PlaybookId retire | 4-5 hrs |
| 12.3b | Output schema single-source cleanup (strip from JPS, render from schema) | 1-2 hrs |
| 12.4 | Persona (`sprk_aipersona` FK on Action) | 4-6 hrs |
| 12.5 | Skills formalized on Node (already 1:N per operator) | 4-6 hrs |
| 12.6 | Knowledge references on Node | 6-8 hrs |
| 12.7 | Retrofit 5 Linear consumers to Playbook-manifest model + CI drift check | 8-10 hrs |
| Phase E | Deactivate 6 migrated playbook rows | 0.5 hr |
| Phase G | Docs (BUILD-A-NEW-LINEAR-AI-CONSUMER + BUILD-A-MULTI-STEP + SPAARKE-AI-COMPOSITION-MODEL) | 6-8 hrs |

**Total remaining: ~34-46 hrs.**

---

## Design decisions locked (2026-07-03)

Full list in `r7-close-plan-2026-07-03.md` §2. Key ones the coding agent MUST honor:

- Playbook is a MANIFEST (documents composition), not a runtime graph interpreter
- Composition binding = **compile-time FKs in consumer service code**
- Content resolution = **runtime Dataverse row fetch** by FK
- Skills on Node (1:N). Persona on Action. Output schema ONLY in `sprk_outputschemajson` (never in JPS output.fields).
- Multi-step = **explicit code sequence in consumer service** (`if` statements, sequential awaits) — NOT runtime graph walking
- Slash commands (`/summarize`) removed entirely
- Doc Upload's PlaybookId in client contract = **retire in Phase 12.3a** (not R8)
- CI drift check in Phase 12.7 is **mandatory**

---

## Key files for Phase 12.3a (the next work)

Client (rewire target):
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatMessageRenderer.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useActionHandlers.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useDynamicSlashCommands.ts` — REMOVE if not needed elsewhere
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatInput.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/SlashCommandMenu/` — REMOVE entire directory if `/summarize` was its only use
- Existing shared hook: `src/client/shared/Spaarke.UI.Components/src/hooks/useLinearRunProgress.ts`

Doc Upload retire target (client):
- `src/solutions/LegalWorkspace/**` — find Doc Upload wizard client code that posts to `/api/ai/documentintelligence/analyze` with `PlaybookId = 18cf3cc8-…`
- Change to `POST /api/ai/documents/{docId}/profile` (or similar consumer-typed URL)

Server (needs a new endpoint for Doc Upload consumer-typed URL):
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — add new consumer-typed endpoint route; retire `PlaybookIds` reverse-lookup dispatch code
- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/LinearConsumersOptions.cs` — delete `PlaybookIds` map + `GetConsumerTypeForPlaybookId` method
- `spaarke-bff-dev` App Settings — delete remaining `LinearConsumers__PlaybookIds__*` and `LinearConsumers__MaxOutputTokens__*` keys (verify none used elsewhere first)

Test rendering:
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/__tests__/SprkChatMessageRenderer.playbook-options.test.tsx` — likely needs rewrite or delete

---

## Rollback for Wave 12.3 server-side (if needed)

Revert PR #546 (single merge commit). Restores App Settings needed for old code:
- Would need to also restore 4 `LinearConsumers__ActionIds__*` on `spaarke-bff-dev`
- Doc Upload / File Summary / Prefills would go back to config-map lookup

Not planned. Server side deployed clean.

---

*End of current-task.md. Recovery point: server merged to master; Wave 12.3 client work is the next material task per [`r7-close-plan-2026-07-03.md`](notes/r7-close-plan-2026-07-03.md) Phase 12.3a.*
