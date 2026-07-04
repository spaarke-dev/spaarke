# Current Task State — spaarke-ai-platform-unification-r7

> **Last Updated**: 2026-07-04 (context-handoff, pre-compact)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Session** | R7 Wave 12.3 Phase 12.3a closed + strategic pivot to canonical Spaarke AI architecture design |
| **Status** | **Wave 12.3 summarize flow WORKING end-to-end** (curl + browser UAT). **Now paused for strategic architecture design.** Operator to review §3 of new canonical doc before we draft §4-8. |
| **Branch** | `work/spaarke-ai-platform-unification-r7` |
| **Latest commit** | `5f77a1d9c docs(architecture): canonical Spaarke AI Architecture and Component Design v0.1` (pushed to origin 2026-07-04) |
| **Uncommitted** | none — clean tree |
| **Next Action** | **Wait for operator review of §3 (use cases) in [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md).** After approval → draft §4 architecture overview, §5 component model, §6 capability manifest, §7 intent+dispatch (resolves the 4-mechanism drift), §8 roadmap. |

### Critical context (30-second read)

Today's work had two phases. **Phase 1 (morning-afternoon)**: chased and closed the Wave 12.3 Phase 12.3a summarize failures — shipped 7 commits (session-id fix, ExtractedText persistence, auto-promote, field_delta synthesis, retry-with-backoff diagnostics, plus a broadened summarize regex to handle typos). Verified end-to-end via `curl` on session `9d466fd406b54e5d8777642849cd90f3` AND browser UAT. Summary tab renders TL;DR, Summary, Keywords, Entities correctly.

**Phase 2 (evening) — strategic pivot triggered by operator**: the operator recognized we've been shipping one-off tactical patches without a coherent target architecture for the general N-capability case. Spaarke AI is a **portfolio of AI capabilities for legal operations**, not a "summarize a document" tool. Certain Wave 12.3 artifacts (server-side regex in `TryDetectExplicitConsumerType`, `linear_dispatch` SSE event, `executeLinearDispatch.ts` client helper) are architecturally out of place — they create a fourth intent-detection mechanism in a system that already had three (CapabilityRouter, LLM agent tool loop, SoftSlashRouter). Operator directed a strategic pause to define the canonical architecture.

**Deliverable this session**: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) v0.1 — canonical Spaarke AI architecture doc with §0-3 drafted (intro, product context, competitive landscape, 28 use cases in 7 categories with stable UC-* IDs). §4-8 explicitly deferred to next iteration pending operator review.

---

## Files modified this session

### Committed today (all pushed to origin)

**Server (BFF) — Wave 12.3 UAT fixes**:
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — server keyword-match `linear_dispatch` bypass; `TryDetectExplicitConsumerType` with regex; diagnostic log (`Wave 12.3 keyword-check`); broadened regex for typos (`sumarize`, `summerize` etc.). **Architecturally at risk in §7 dispatch redesign.**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/LinearDispatchSseEvent.cs` — new SSE event type. **Architecturally at risk.**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/ChatSseEventFactory.cs` — factory method for the event.
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs` — added `ChatSessionFile.ExtractedText` nullable init-only property.
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs` — persist `ExtractedText` at upload time.
- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/SessionFileTextSource.cs` — read inline `ExtractedText` first; RAG fallback.

**Client (shared libs + SpaarkeAi) — Wave 12.3 UAT fixes**:
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts` — `ILinearDispatchPayload` interface, `linear_dispatch` variant on `ChatSseEventType`, `onLinearDispatch` prop, `onSessionStale` prop, `resumeSession` API.
- `src/client/shared/Spaarke.UI.Components/src/hooks/useSseStream.ts` — parser branch for `linear_dispatch`, `setOnLinearDispatch` callback ref.
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` — `initialSessionId` now honored via `resumeSession()`; `onSessionStale` wiring; prop destructure for `onLinearDispatch`.
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatSession.ts` — new `resumeSession(id)`; `loadHistory()` returns `{ok, staleSession?}`.
- `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` — new `clearChatSession()`; `removeSession()` helper.
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — auto-promote `useEffect` (POSTs ready chips to `/documents`); `handleLinearDispatch`; `handleSessionStale`; retired NL `executeSummarizeIntent` branch. Latest edit adds explicit diagnostics + retry-with-backoff to auto-promote.
- `src/solutions/SpaarkeAi/src/components/conversation/executeLinearDispatch.ts` — new companion helper (widget_load + POST /summarize + SSE bridge). **Architecturally at risk in §7 dispatch redesign.**
- `src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts` — synthesize `field_delta` events from `complete` chunk's `result` payload per top-level property.

**Docs**:
- `projects/spaarke-ai-platform-unification-r7/notes/current-architecture-map-2026-07-03.md` — architecture map + KQL queries + regression analysis (diagnostic artifact, not authoritative).
- `projects/spaarke-ai-platform-unification-r7/notes/summarize-flow-2026-07-03.md` — end-to-end trace of the successful summarize flow (preserves the working-state knowledge).
- `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` — **CANONICAL v0.1** — Spaarke AI architecture doc. §0-3 drafted (intro, product context, competitive landscape, 28 use cases with UC-* IDs). §4-8 deferred pending operator review of §3.

### Commits this session (chronological)

| Commit | Purpose |
|---|---|
| `139014adc` | server-side `linear_dispatch` SSE event + keyword-match bypass |
| `7f0e42b30` | client SSE wiring + first `executeLinearDispatch.ts` |
| `a9bdd2f88` | retire client NL `executeSummarizeIntent` branch (avoid double dispatch) |
| `5ab21578b` | persist `ExtractedText` on `ChatSessionFile`; SessionFileTextSource inline-first |
| `ab8ab68a8` | resume, don't recreate, persisted chat sessions (single session id) |
| `68e8b96f1` | auto-promote ready chips to `/documents` (regression from executeSummarizeIntent retire) |
| `2d4e0c8d8` | bridge synthesizes `field_delta` from `complete` chunk result payload |
| `1e366dc5b` | docs: successful summarize flow synopsis + component model |
| `5f77a1d9c` | **docs: canonical Spaarke AI Architecture and Component Design v0.1** ← current tip |

### App Insights connection (for next session's KQL queries)

- App ID: `6a76b012-46d9-412f-b4ab-4905658a9559`
- Endpoint: `westus2-2.in.applicationinsights.azure.com`
- Successful curl reproduction session id: `9d466fd406b54e5d8777642849cd90f3` (~22:31 UTC 2026-07-03)

### Curl-driven repro (bypasses browser cache)

See §6 of `notes/summarize-flow-2026-07-03.md`. Full working bash script:

```bash
TOKEN=$(az account get-access-token --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c" --query "accessToken" -o tsv)
TID=a221a95e-6abc-4434-aecc-e48338a1b2f2
# ... create session, upload doc, POST /messages, POST /summarize
```

Test file at `c:/tmp/testdoc.txt`.

---

## Decisions locked this session

- **Wave 12.3 tactical closure**: 7 commits ship a working summarize flow. Fixes to session-id, ExtractedText persistence, auto-promote, and field_delta synthesis are architecturally sound and stay regardless of §7 redesign.
- **Strategic pivot**: Wave 12.3's `linear_dispatch` SSE event + server-side regex + `executeLinearDispatch.ts` client helper are architecturally out of place — they create a fourth intent-detection mechanism. Retirement path to be defined in §7.
- **Canonical use-case catalog established**: 28 use cases (UC-A-1 through UC-G-2) across 7 categories. Stable IDs. All future capability work references these.
- **Broadened regex ships as tactical band-aid**: to unblock further testing while §7 redesign is in progress. Explicitly flagged for retirement.
- **Compound-intent plan_preview UX** (`SYS-Recall_Session_File` triple-fire for read-only recall tools) is a pre-R7 feature not caused by Wave 12.3. Refinement (whitelist read-only tools) noted as follow-up, not urgent.

---

## What "next session" should do first

1. **Read** `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` §0-3 in full.
2. **Ask the operator** for review notes on §3 (use case catalog): category taxonomy, use case completeness, status honesty, boundary with SprkChatAgent tool loop, ordering.
3. **After approval**, draft §4-8 in the canonical doc. Suggested sequence:
   - §4 architecture overview (5 layers: intent, capability manifest, input resolution, execution engine, output routing)
   - §5 component model (Dataverse tables, BFF services, shared client libs, widgets — with contracts)
   - §6 capability manifest schema (Actions + Consumers + Personas + Skills + Triggers + Output bindings — the maker-configurable model)
   - §7 intent + dispatch (single mechanism to replace the current four: regex + CapabilityRouter + agent tool loop + SoftSlashRouter; propose LLM classification OR embedding similarity against trigger phrases)
   - §8 roadmap (phased migration from today's state → target; identify what retires, what generalizes, what's genuinely new)
4. **Do not** ship more tactical patches without a §7 decision — every new capability we add today deepens the architectural drift.

---

## Environment state at handoff

- **BFF deploy**: `spaarke-bff-dev` — up, healthy, running commit that includes diagnostic log + broadened regex.
- **Client deploy**: `sprk_spaarkeai` code page (resource `5206a442-3451-f111-bec7-7ced8d1dc988`) — running the bundle from commit `2d4e0c8d8` (bridge fix). The `5f77a1d9c` regex + retry improvements are on disk locally but NOT yet deployed (docs-only commit). If testing continues before §4-8 land, rebuild + deploy.
- **Redis + AI Search**: healthy, no known issues.
- **App Insights**: capturing traces. `Wave 12.3 keyword-check` diagnostic log fires when regex is evaluated — useful for validating dispatch decisions.

---

## Not-yet-addressed items surfaced today

- Widget rendering for `entities` field currently receives a JSON string (via `JSON.stringify` in the bridge). Widget parses fine for organizations; nested arrays like `persons` render empty (not yet verified whether widget schema supports the nested object shape). Follow-up.
- `sumarize` typo case now handled by broadened regex, but the typo issue is a symptom of the wrong dispatch pattern. §7 should address the general issue.
- Schedule 13A.pdf silent-upload-failure from the last UAT: user cited "Failed to fetch" banner. My auto-promote effect now has explicit diagnostics + retry-with-backoff — if the failure recurs, the console log will explain why. Not yet reproduced via curl.
- Compound-intent `plan_preview` UX for read-only recall tools is arguably overkill. Whitelist `SYS-Recall_*` in `CompoundIntentDetector.IsCompoundIntent`. Follow-up, not urgent.

---

*End of current-task.md. Recovery point: strategic architecture doc drafted; operator review pending; §4-8 to be drafted after approval.*
