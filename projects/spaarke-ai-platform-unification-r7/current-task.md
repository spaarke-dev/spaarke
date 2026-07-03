# Current Task State — spaarke-ai-platform-unification-r7

> **Last Updated**: 2026-07-03 (Phase 12.3a client-side wiring for `linear_dispatch` complete; ready to build + deploy + UAT)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Session** | R7 close — Wave 12.3+ chat-summarize + Playbook-manifest composition model |
| **Status** | **Phase 12.3a client-side wiring COMPLETE (uncommitted).** All four subtasks done + tsc-surface-gate confirms 0 surface-owned TS errors. Next: build + deploy client + UAT smoke Assistant pane summarize. |
| **Branch** | `work/spaarke-ai-platform-unification-r7` |
| **Latest commits** | Server side of Wave 12.3 merged via PR #546 (`5f8543457`); D-13 revision pushed as `b1e4a4b11`; server keyword-match bypass = `139014adc` |
| **Uncommitted (server, from prior turn)** | `LinearDispatchSseEvent.cs`, `ChatSseEventFactory.cs`, `ChatEndpoints.cs` (keyword bypass + `TryDetectExplicitConsumerType`). |
| **Uncommitted (client, this turn)** | `types.ts` (new `ILinearDispatchPayload` + `IChatSseEventData` fields + `ChatSseEventType` extension + prop + hook API), `useSseStream.ts` (parse `linear_dispatch` case + ref + setter), `SprkChat.tsx` (prop + useEffect wiring), `ConversationPane.tsx` (`handleLinearDispatch` + prop wire), `executeLinearDispatch.ts` (NEW companion helper for the pre-promoted case). |
| **Next Action** | **Build + deploy SpaarkeAi code page + UAT smoke.** Note: local Vite build blocked on missing `@spaarke/legal-workspace` workspace dep in node_modules (unrelated to this task); `tsc --noEmit` on shared lib passes 0 errors. Recommend `npm install` at solutions root before deploying, or use CI. |

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

## Phase 12.3a Progress (in-flight)

**Operator direction (2026-07-03)**: for NL messages the intent-match confidence must be
100% (deterministic keyword match) to auto-dispatch; anything less → show top-3 candidates
+ library option. Slash commands = direct route (auto-dispatch).

### Design pattern implemented (server side)

- NEW SSE event type `linear_dispatch` (distinct from `playbook_options` per FR-48 invariant).
- Server detects explicit summarize keywords ("summarize", "summary", "summarise") whole-word case-insensitive.
- On match + attachments present → emit `linear_dispatch` event carrying the target endpoint URL, consumer type, request body, session file IDs; skip PhaseB entirely.
- On no keyword match → existing PhaseB flow (playbook_options with candidates).

### Server changes (uncommitted, build clean)

1. `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/LinearDispatchSseEvent.cs` — NEW file
2. `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/ChatSseEventFactory.cs` — added `CreateLinearDispatchEvent` factory method
3. `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — insert keyword-match bypass before `RunPhaseBVectorMatchAsync` (line ~633); new helper method `TryDetectExplicitConsumerType(message)` + compiled `SummarizeKeywordRegex`

### Client changes (COMPLETE — uncommitted)

**All four subtasks landed 2026-07-03. tsc --noEmit on shared lib = 0 errors.
tsc-surface-gate on SpaarkeAi = 0 surface-owned errors.**

1. **`src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts`** ✅
   - Added `ILinearDispatchPayload` interface (mirrors BFF `LinearDispatchSseEventData`)
   - Added `linear_dispatch` variant to `ChatSseEventType` union
   - Added `consumerType`, `dispatchUrl`, `requestBody`, `reason` fields to `IChatSseEventData` (sessionAttachmentIds reused from playbook_options)
   - Added `onLinearDispatch?` prop to `ISprkChatProps`
   - Added `setOnLinearDispatch` to `IUseSseStreamResult`

2. **`src/client/shared/Spaarke.UI.Components/src/hooks/useSseStream.ts`** ✅
   - Import `ILinearDispatchPayload`
   - Added `onLinearDispatch` to `SseEventHandlers` interface
   - Added `else if (event.type === 'linear_dispatch')` branch in `processEvent`
   - Added `onLinearDispatchRef` + `setOnLinearDispatch` (synchronous callback-ref pattern, mirrors `setOnPlaybookOptions`)
   - Exposed `setOnLinearDispatch` from the returned hook API

3. **`src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx`** ✅
   - Destructured `setOnLinearDispatch` from `sseStream`
   - Added `onLinearDispatch: onLinearDispatchProp` to destructured props
   - Added `useEffect` that wires prop → setter (mirrors playbook_options `useEffect`)

4. **`src/solutions/SpaarkeAi/src/components/conversation/executeLinearDispatch.ts`** ✅ (NEW file)
   - Companion helper to `executeSummarizeIntent.ts` for the pre-promoted case
   - Steps: (1) emit `workspace.widget_load` for Summary tab; (2) POST to `dispatchUrl` with `requestBody`; (3) consume SSE stream, bridge via `createSseToPaneEventBridge` to `workspace.streaming_*` events
   - `resolveWidgetConfig(consumerType)` currently handles `chat-summarize`; extend as future consumers migrate

5. **`src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`** ✅
   - Imported `executeLinearDispatch`
   - Added `handleLinearDispatch` React.useCallback with structural-only logging + interstitial chat message + fire-and-forget helper invocation + ADR-019 error surfacing
   - Wired `onLinearDispatch={handleLinearDispatch}` prop on `<SprkChat>`

### Next steps

- **Commit + push client changes** (batch with any additional Phase 12.3a work below)
- **Deploy client** (SpaarkeAi is a code page; use appropriate deploy script — note local Vite build blocked on missing `@spaarke/legal-workspace` node_module; may need `npm install` at solutions root first)
- **UAT smoke** — user uploads file, types "summarize this document" (NL, no slash) → workspace Summary tab installs + structured JSON renders (validates server keyword match + client `linear_dispatch` handling end-to-end)

### Deferred to future 12.3a sub-work (after keyword auto-dispatch smoke passes)

- **Server**: retire `PlaybookCandidateSelector` confidence threshold (0.85 → 0.0) so candidates always return top 3 even at low confidence
- **Server**: new endpoint `GET /api/ai/playbooks/library?type=linear` returning enabled Linear playbooks with slashkey/displayname/description (needs Phase 12.4 schema — chicken-and-egg; either defer this to Phase 12.4 or use a hardcoded list temporarily)
- **Server**: implement `/api/ai/playbook-dispatch/execute` endpoint (currently 404 per UAT feedback — this is why "library modal items aren't selectable")
- **Client**: fix `handleSelectPlaybook` in ConversationPane to route through the new dispatch endpoint (or bypass into a Linear direct dispatch)
- **Server**: new consumer-typed endpoint `POST /api/ai/documents/{docId}/profile` + retire `LinearConsumersOptions.PlaybookIds` + last App Settings
- **Client**: `useAiSummary.ts` change POST target to new consumer-typed endpoint

Phase 12.3a estimate revision: **7-11 hrs total** (was 4-5). ~2 hrs already done (audit + server keyword bypass).

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
