# Current Task State — spaarke-ai-platform-unification-r7

> **Last Updated**: 2026-07-05 (pre-Fable-5-switch handoff)
> **Recovery**: Read "Quick Recovery" section first
> **Session model note**: previous session was Claude Opus 4.7. The next session should be **Claude Fable 5** (`claude-fable-5`) — activated by operator via settings-file or `/model fable`. Fable 5's 1M context + adaptive thinking suits the code-audit + §4-7 design work coming up.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Session** | Canonical Spaarke AI Architecture doc reached v0.2.6 (§0-3 substantive draft including relationship map + orchestration walkthrough + Layer 0 auto-composite + four-layer dispatch + two-catalog Consumers-and-Tools model). **Pausing before Step 1 of the code audit** to switch to Fable 5. |
| **Status** | Doc at **v0.2.6**. Uncommitted edits on disk (this session's v0.2.5 + v0.2.6 changes were not committed). Audit project folder NOT YET CREATED. |
| **Branch** | `work/spaarke-ai-platform-unification-r7` |
| **Latest committed doc version** | v0.2.4 (from 2026-07-04 session) — commit `5f77a1d9c` pushed to origin. **v0.2.5 + v0.2.6 changes are uncommitted on disk.** |
| **Uncommitted** | `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` — v0.2.5 (status banner + revision-log format) + v0.2.6 (three write-shapes hub reframing, on-upload auto-composite, sprk_event fix, Layer 0 dispatch). Consider committing before Fable 5 switch to avoid confusion. |
| **Next Action for Fable 5 session** | **Step 1 of the 3-step code audit.** Todos are laid out in TodoWrite. First todo: create `projects/spaarke-ai-code-audit-r1/` project folder + README.md + spec.md. Then enumerate AI-touching code across ALL worktrees (not just r7). Operator confirmed scope = "All AI-touching code, all worktrees" and output location = "New project folder". |

---

## Fable 5 session — orientation on arrival

**You are the Fable 5 successor to the Opus 4.7 session that produced v0.2.6.** Read this section before touching anything.

### Where we are in the design conversation

The operator and previous session (Opus 4.7) collaboratively built a canonical Spaarke AI architecture doc across 6 minor versions on 2026-07-04–05. Current state is **v0.2.6** at `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`. The doc's Status banner + Last-updated line at the top will confirm the version.

**Highlights of v0.2.6** (read the doc top-to-bottom before proceeding, but this is the shape):
- §0 intro + product context (§1) + competitive landscape (§2) — includes Wordsmith AI + Peppermint + Fable 5 tier context.
- §3 use case catalog — 28 UCs across 8 categories (A-H), each with Typical prior context + Typical next steps declaring session-graph edges.
- §3.9 relationship map + overlap analysis — three write-shapes (edit file / create record / send comm) reframe of universal hubs; 10 overlap points for §5-6 consolidation.
- §3.10 orchestration walkthrough — 14-step NDA scenario as canonical example; 7 mechanisms (M1-M7); 4 locked decisions (D1-D4); testable propositions (P1-P10).
- §3.10.7 dispatch model — Layer 0 (on-upload auto-composite) + Layer 1 chip / Layer 2 Consumer NL match / Layer 3 LLM tool loop over Tool catalog / Layer 4 refusal. Two-catalog model (Consumers + Tools). Locked decisions D5 (grounded execution invariant) + D6 (two catalogs both closed).
- §4-8 explicitly deferred. §9 revision log with per-version summary.

**Design principles locked** (do NOT re-litigate unless operator raises):
- Sequence framing (§3.0): UCs are connected nodes in a session graph, not isolated tools.
- Grounded execution (D5): every output must be Cataloged Consumer output, Tool-composed answer with citations, M4 confirmation, or honest refusal. No free-form ungrounded LLM chat.
- Two catalogs (D6): Consumers (curated) + Tools (LLM composition primitives, incl. Dataverse MCP). Both closed.
- Storage vs rendering separation (D2): `session.outputs` universal + automatic; disposition (informational / work_product / overlay) is a Consumer rendering choice.
- Chip labels Consumer-declared (D4); slot-fill chat by default with modal escape (D3); confidence-threshold confirmation (D1).
- On-upload auto-composite (Layer 0) is the DEFAULT new-session flow when a doc is uploaded.

### The 3-step audit plan the operator directed

1. **Step 1 (this session's first task)** — Inventory ALL existing AI code across all worktrees against the 5 target categories (Session / Consumer / Tool / Dispatcher / Manifest) + functional capabilities. Deliverable: `projects/spaarke-ai-code-audit-r1/SPAARKE-AI-CODE-INVENTORY.md`.
2. **Step 2 (after audit)** — Draft §4-7 of the canonical design doc, informed by what Step 1 revealed. Design against real constraints, not greenfield.
3. **Step 3 (after design)** — For each inventoried component: keep-as-is / refactor-to-target / retire / new-required. Deliverable: `projects/spaarke-ai-code-audit-r1/SPAARKE-AI-MIGRATION-MAP.md`.

### Immediate first moves for you (Fable 5 session)

1. **Read the current doc top-to-bottom** — `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` (v0.2.6, ~2200 lines; comfortably within your 1M context window). This is where the operator and prior session agreed the target model.
2. **Confirm uncommitted state with the operator** — v0.2.5 + v0.2.6 doc edits were not committed. Ask whether to commit before starting the audit (recommended) or defer.
3. **Create the audit project folder**: `projects/spaarke-ai-code-audit-r1/README.md` + `spec.md`. Portfolio issue should be registered via `/devops-project-register --from-folder projects/spaarke-ai-code-audit-r1 --epic 421 --project-type Cleanup` (or Data / Process — operator's call).
4. **Begin Step 1 audit** per the pending TodoWrite list. Enumerate code surfaces across worktrees first (this is a `git worktree list` + Glob per worktree pattern).

### Key context the previous session accumulated

- Spaarke has ~30 accumulated AI-related projects (7 spaarke-ai-platform-unification R1-R7 + ~20+ others: document-intelligence, insight-engine, chat, daily-update, playbook, email-to-document, chat-routing-redesign, etc.). Operator's concern: technical debt from 27+ projects worth of code that may or may not fit the target model.
- Operator explicitly asked: "what happens to all of this code — many required components have been built in one way or another. BUT if not then we need to get rid of all the deadwood."
- Working environment: `spaarkedev1` Dataverse dev env; `spaarke-bff-dev` App Service. Fable 5 session should ONLY do audit-related work; no BFF changes without operator direction.

### Todos to work through (in TodoWrite)

The Fable 5 session should immediately view the TodoWrite list. The first `in_progress` marker was set to "PAUSED: switch to Fable 5" — flip that to completed and start the top pending task: "Create projects/spaarke-ai-code-audit-r1/ folder + README.md + spec.md".

---

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
