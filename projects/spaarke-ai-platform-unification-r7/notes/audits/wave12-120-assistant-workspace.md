# Wave 12.1 Audit 120 — Assistant ↔ Workspace ↔ Context

> **Status**: COMPLETE — read-only investigation
> **Author**: Wave 12.1 audit task 120 (task-execute STANDARD rigor)
> **Date**: 2026-06-30
> **Scope**: SpaarkeAi assistant chat surface, LegalWorkspace embedding, BFF chat pipeline, context-passing plumbing
> **Output discipline**: file:line citations throughout; no code fixes proposed (those become Wave 12.4 POMLs)
> **Operator report**: "nothing fixed" in UAT

---

## 1. Executive summary

### Top-line finding

The Assistant↔Workspace plumbing is **architecturally complete but operationally broken by a naming-convention split** between the SpaarkeAi client (raw Dataverse logical names: `sprk_matter`) and the BFF chat pipeline (normalized canonical enum: `matter`). The split shows up at FIVE independent surfaces in the BFF and is **not** caught by validation. Because `ChatHostContext` carries the raw value through, three different downstream effects all silently no-op:

1. System-prompt entity enrichment (the LLM never learns "you are assisting with matter X")
2. Matter-memory injection (the LLM never sees prior matter facts/parties/dates)
3. Entity-scoped RAG search (the documents index expects normalized `'matter'`, gets `'sprk_matter'`, returns 0 hits)

These three failures are consistent with operator's UAT verdict "nothing fixed" — every "what matter am I in?", "what documents are in this matter?", and "what's the latest on this case?" question would degrade to generic tenant-wide behaviour or refusal.

### MVP-scope recommendation

**PLUMBING-ONLY** is in scope for MVP. The fix is small (one normalization layer or one consistent decision on raw-vs-normalized), reversible, and unblocks the three operator-visible UAT scenarios. **Partial grounding** (matter-record fields in system prompt) is also in scope because the existing `AnalysisChatContextResolver` + `PlaybookChatContextProvider` already implement it — the data just isn't reaching them. **Out-of-scope** for MVP: retrieval-over-SPE (not the blocker; matter docs already indexed via FileIndexingService + RagEndpoints when documents flow through the Spaarke pipeline) and tool-use / Action Engine R1 (separate project, on hold).

Effort estimate: **2-4 working days** for plumbing + verification + UAT iteration. See §5.

---

## 2. Inventory — shipped surface

### 2.1 Entry points

| Surface | Path | Status |
|---|---|---|
| SpaarkeAi assistant chat code page | `src/solutions/SpaarkeAi/src/main.tsx` | Shipped + deployed (R7 W11 commit `85c762081`) |
| SpaarkeAi three-pane shell | `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx:569-660` | Shipped |
| SpaarkeAi conversation pane | `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | Shipped |
| LegalWorkspace standalone code page | `src/solutions/LegalWorkspace/src/main.tsx` | **Retired per OC-R4-05** (components retained as library; SpaarkeAi is sole host per `docs/architecture/LEGALWORKSPACE-RETIREMENT.md`) |
| LegalWorkspace embedded inside SpaarkeAi | `src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx` (no `hostContext`/SprkChat grep matches; LW doesn't host chat — only sections) | Shipped |
| BFF chat session endpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs:62-285` | Shipped |
| BFF context resolver (playbook-driven chat) | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` | Shipped |
| BFF standalone context provider | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/StandaloneChatContextProvider.cs` | Shipped |
| BFF chat dispatcher | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` | Shipped |
| BFF matter memory enrichment | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs:650-703` (R6 task 068 / D-C-21 / FR-45) | Shipped |
| BFF RAG search filter | `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs:1232-1236` (parent-entity scoping) | Shipped |
| BFF document-search tool | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/DocumentSearchTools.cs:67-90` (uses `_parentEntityType` from scope) | Shipped |
| BFF Dataverse query tool | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/DataverseQueryTools.cs:36-93` (allow-listed `sprk_matter|sprk_project|contact|account|sprk_document`) | Shipped |
| LoadKnowledge node executor | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LoadKnowledgeNodeExecutor.cs:75-362` | Shipped **but**: pass-through placeholder for Daily Briefing (R4); NOT used by chat retrieval. Comment at lines 16-21 confirms R5 will substitute AI Search retrieval but R4/R7 leave it pass-through. |

### 2.2 BFF chat session endpoints (relevant subset)

From `ChatEndpoints.cs:46-285`:

| Endpoint | Purpose | Hostcontext role |
|---|---|---|
| `POST /api/ai/chat/sessions` (`CreateSessionAsync`, line 297) | Create session | Accepts `request.HostContext` (line 321), persists into `session.HostContext` |
| `POST /api/ai/chat/sessions/{id}/messages` (`SendMessageAsync`, line 340) | SSE chat | Reads `session.HostContext` (lines 416, 535, 718, 743) and threads to context provider |
| `PATCH /api/ai/chat/sessions/{id}/context` (`SwitchContextAsync`) | Switch playbook/doc | Optional `HostContext` override (line 3067) — replaces if provided, keeps current otherwise (line 1158) |
| `GET /api/ai/chat/context-mappings` (`GetContextMappingsAsync`) | Resolve playbook for entity | Uses `entityType + pageType` (no `hostContext`; this is pre-session discovery) |

### 2.3 Context-passing mechanism (URL → BFF)

```
1. SpaarkeAi page launched from matter form (Power Apps navigation)
     URL = ...sprk_spaarkeai...?entityType=sprk_matter&entityId=<guid>&matterId=<guid>
2. main.tsx:356-377 parses URL params directly (NO normalization, NO useEntityResolver)
3. main.tsx:399-403 passes raw `entityLogicalName="sprk_matter"` to <App />
4. ThreePaneShell.tsx:580-591 builds EntityContext { entityType: "sprk_matter" as EntityType, ... }
     (line 586-587 comment explicitly notes the cast: "may not narrow to EntityType literal union")
5. ThreePaneShell.tsx:618-620 passes entityContext → AiSessionProvider
6. ConversationPane.tsx:2204-2211 builds hostContext { entityType: entityContext.entityType, ... }
     EntityName: NEVER set
     WorkspaceType: hardcoded "spaarke-ai"
     PageType: NEVER set
7. ConversationPane.tsx:2435 passes hostContext to SprkChat
8. SprkChat sends to BFF: POST /api/ai/chat/sessions with { HostContext: { EntityType: "sprk_matter", EntityId, EntityName: null, WorkspaceType: "spaarke-ai", PageType: null } }
```

**Comparison**: `useEntityResolver` (`src/client/shared/Spaarke.AI.Context/src/providers/useEntityResolver.ts:97-103`) DOES normalize via `TYPENAME_TO_ENTITY_TYPE`: `sprk_matter → 'matter'`, `sprk_project → 'project'`. **SpaarkeAi doesn't use this hook.** The hook is the standard normalization layer the rest of the codebase implicitly assumes.

---

## 3. Gap list

### Gap A — Naming-convention split: SpaarkeAi sends `sprk_matter`, BFF expects `matter` for enrichment + matter-memory + RAG-scope; expects `sprk_matter` for matter-id extraction + StandaloneChatContextProvider; **the system is fragmented**

**Category**: **PLUMBING** (the most-impactful, multi-headed root cause)

**Evidence — convention-A "normalized" surfaces (expect `matter`/`project`/`invoice`/`account`/`contact`)**:
- `src/server/api/Sprk.Bff.Api/Models/Ai/ParentEntityContext.cs:35-64` — `EntityTypes.All = [matter, project, invoice, account, contact]`; `IsValid` lowercases and matches against this set
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatHostContext.cs:44-48` — `IsValid()` delegates to above (never called anywhere — `grep "IsValid"` shows zero call sites for `ChatHostContext.IsValid`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs:666` — `AppendMatterMemoryAsync` requires `EntityType == "matter"`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs:1133-1144` — `MapEntityTypeToRecordType` maps `matter → sprk_matter`, etc. (i.e., INPUT is normalized)
- `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs:668-689` — when indexing documents, sets `ParentEntityContext.EntityType = "matter"` / `"project"` / `"invoice"` (so AI Search index has `parentEntityType = "matter"`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/BulkRagIndexingJobHandler.cs:278` — same pattern as above

**Evidence — convention-B "raw" surfaces (expect `sprk_matter`/`sprk_project`)**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs:1542` — `TryParseMatterId` checks `ParentEntityType == "sprk_matter"` (matter-id telemetry only)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/StandaloneChatContextProvider.cs:63-85` — `SupportedEntityTypes` + `EntityDisplayNames` keys are `sprk_matter`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AnalysisChatContextResolver.cs:550-559` — `GetEntityContextColumns` switches on `sprk_matter|sprk_project|sprk_invoice`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/DataverseQueryTools.cs:45-106` — `AllowedEntityTypes` + all dictionaries keyed on `sprk_matter|sprk_project|contact|account|sprk_document`

**Operational consequence when SpaarkeAi sends `EntityType="sprk_matter"`**:

| Component | Result | Operator impact |
|---|---|---|
| `AppendEntityEnrichment` (line 543) | Skipped (EntityName null at line 550; PageType null at line 554) | LLM has no "you are assisting with matter X" context |
| `AppendMatterMemoryAsync` (line 650) | Skipped (line 666 expects "matter" not "sprk_matter") | LLM has no prior matter facts/parties/dates |
| `RagService` parent-entity filter (line 1234) | Filter is `parentEntityType eq 'sprk_matter'` but index has `'matter'` — **0 hits** | Document search returns "no relevant documents" for every matter-scoped query |
| `DocumentSearchTools.SearchDocumentsAsync` (line 67) | Inherits the 0-hit filter via `_parentEntityType` (line 53) | Same as above — assistant cannot find matter docs |
| `DocumentSearchTools.SearchDiscoveryAsync` | Same issue | Same |
| `TryParseMatterId` (line 1542) | **Works** — matter-id captured for telemetry only | None directly user-visible (telemetry only) |
| `StandaloneChatContextProvider` (sibling pre-session endpoint) | Works (expects raw form) | Pre-session entity probe is fine |
| `AnalysisChatContextResolver` (sibling) | Works (expects raw form) | Independent of chat-session flow |

**Note**: `ChatHostContext.IsValid()` exists (line 44) but is never invoked. There's no validation gate that catches the mismatched naming at the BFF boundary.

### Gap B — `EntityName` never populated by SpaarkeAi → entity enrichment unconditionally skipped even if naming convention is normalized

**Category**: **PLUMBING**

**Evidence**:
- `ConversationPane.tsx:2204-2211` — `hostContext` build omits `EntityName`
- `PlaybookChatContextProvider.cs:550-551` — `AppendEntityEnrichment` early-exits on null/whitespace EntityName

**Operational consequence**: Even after Gap A is fixed (naming normalized to "matter"), the system prompt enrichment ("Context: You are assisting with matter record '<name>'. The user is viewing the <page-type>.") still won't fire because EntityName is null.

**Fix surface**: SpaarkeAi must populate EntityName. Options:
- Read from Xrm `formContext.data.entity.getPrimaryAttributeValue()` (PCF-frame-walk; useEntityResolver-style)
- Lazy-fetch from Dataverse via Xrm.WebApi inside `ThreePaneShell` (per CLAUDE.md DATA-ACCESS-DECISION-CRITERIA: this is fine — single record, read-only, no auth crossing BFF)
- Or: have the BFF lazy-fetch on session-create if EntityName missing but EntityId present (simpler at-the-boundary; matches the convention in StandaloneChatContextProvider which does its own lookup)

### Gap C — `PageType` never populated → enrichment doubly blocked + telemetry impoverished

**Category**: **PLUMBING** (small)

**Evidence**:
- `ConversationPane.tsx:2204-2211` — no `pageType` in hostContext build
- `PlaybookChatContextProvider.cs:554-556` — `AppendEntityEnrichment` early-exits when PageType is null or "unknown"
- `PlaybookChatContextProvider.cs:559-565` — even with a value, must match `PageTypeLabels` dictionary (need to grep that dictionary; assume e.g. "MainForm", "AnalysisView", "DocumentPanel")

**Operational consequence**: Independent third guard rail on enrichment. Even with Gaps A + B fixed, enrichment still skipped without a valid PageType. From SpaarkeAi, the natural value is "MainForm" or "Dashboard" or "AssistantPane" — needs operator + LLM-utility decision on which contributes most signal.

### Gap D — Chat `hostContext` is fixed at mount (URL params); switching workspace tabs does NOT refresh chat context

**Category**: **MISSING IMPLEMENTATION** (potentially **OUT OF MVP** depending on UX expectation)

**Evidence**:
- `ConversationPane.tsx:853-871` — `usePaneEvent("workspace", ...)` handles `tab_change` (line 854) for tab-id tracking only; `selection_changed` (line 859) for selection chip; **no `active_widget_changed` subscription that refreshes `hostContext`**
- `ThreePaneShell.tsx:580-591` — `entityContext` is `React.useMemo` on `[entityLogicalName, entityId, matterId]` — these come from URL props which don't change post-mount
- `AiSessionProvider.tsx:147,199,289-297` — accepts `entityContext` prop; the only way to change it is to remount the provider tree

**Operational consequence**: If the operator opens SpaarkeAi from matter X, then switches to a "Documents" workspace tab showing project Y's documents (or to "Daily Briefing" with no entity context), the chat still thinks it's talking about matter X. Inverse: opening SpaarkeAi without an entity URL (e.g., direct dashboard link) means there's no way to "promote" the active workspace's entity into the chat context.

**MVP scope decision**:
- If operator's UAT expectation is "chat follows the URL/launch context" (most common pattern), this gap is **NOT a blocker** — out of MVP.
- If operator's UAT expectation is "chat follows the active workspace tab", this gap is **plumbing-class** (subscribe to `active_widget_changed`, update entityContext state, re-create session if needed) but not trivially small — likely 1-2 days.

**Recommendation**: Defer to operator decision. If unspecified, defer this gap to a follow-up project — Gap A + B + C fixes already restore the URL-launched matter-context scenario, which is the documented Wave 12 success criterion (AC13/AC14).

### Gap E — No "current entity context" tool surfaced to the LLM; assistant cannot self-answer "what matter am I in?" deterministically

**Category**: **MISSING IMPLEMENTATION** (small, in-MVP if Gap A blocks enrichment)

**Evidence**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/` — no tool named `GetCurrentMatter`, `GetMatterContext`, `WhereAmI`, etc. (verified by grep `GetCurrentContext|GetMatterDetails|currentMatter|whereAmI` returning 0 files)
- `DataverseQueryTools` exists but the LLM has no signal of "which entity ID is the current one" beyond what's in the system prompt — and Gap A blocks the system prompt

**Operational consequence**: When operator asks "what matter am I in?", the LLM must either (a) read it from the system prompt enrichment (broken by Gaps A+B+C) or (b) be told via tool. Today neither path works.

**Resolution path**: If Gaps A+B+C are fixed, the system-prompt enrichment block ("Context: You are assisting with matter record 'Smith v. Jones'. The user is viewing the MainForm.") naturally answers Scenario A below. No new tool needed.

**MVP scope**: subsumed by Gaps A+B+C fix; no separate work.

### Gap F — RAG index population for matter documents — separate concern; assumed populated in spaarkedev1

**Category**: **CONFIGURATION / DATA** (out of audit scope to verify here; flag for UAT)

**Evidence**:
- `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs:668-672` shows the indexing path with `EntityType: "matter"` (normalized) at the index-write side
- `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs:315-331` writes `ParentEntityType = parentEntity?.EntityType` directly (passes through whatever caller provides)
- **Whether matter documents have actually been indexed in spaarkedev1** is a separate UAT verification. The audit confirms the WRITE path is normalized; if operator-observed UAT failure says "no documents found" even after Gap A fix, then this is the next-most-likely root cause.

**MVP scope**: not a Wave 12.4 task per se — it's a UAT verification step. Wave 12.4 fix for Gap A unblocks the search; if results are still empty, a separate ad-hoc "verify documents are indexed in spaarkedev1" task or operator-run check is needed.

### Gap G — LoadKnowledgeNodeExecutor is NOT the chat retrieval path; the operator's "retrieval over SPE deferred" concern is about a different surface

**Category**: **OUT OF SCOPE** (not blocking; operator clarification)

**Evidence**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LoadKnowledgeNodeExecutor.cs:16-50` — explicitly a Daily Briefing playbook control node, R4 pass-through placeholder. Comment at line 46-50: "R5 will substitute the bind with AI Search retrieval". R7 left this pass-through.
- Chat-time retrieval (the operator-relevant path) flows through `DocumentSearchTools` + `KnowledgeRetrievalTools` → `RagService`, **NOT** through `LoadKnowledgeNodeExecutor`.

**Operational consequence**: The "R5 deferred work" placeholder mentioned in task POML inputs is a RED HERRING for Assistant↔Workspace. RAG search for chat is shipped + functional; it's just not getting matter-scoped because of Gap A.

**MVP scope**: NO ACTION. Document the misdirection so future audits don't conflate the two retrieval paths.

### Gap H — Wave 12 plan §10 Q3 deployment-coordination concern affects UAT environment, not the code

**Category**: **CONFIGURATION**

**Evidence**: `notes/wave12-mvp-completion-plan.md:178-197` documents that other-project deployment risk has been flagged but unresolved. Until the BFF + SpaarkeAi code-page deployed to spaarkedev1 match this branch's state, UAT scenarios below can't be reliably reproduced.

**MVP scope**: Coordination question for operator. Audit doesn't block on it.

---

## 4. UAT scenarios — defined + traced (deployed-env reproduction blocked per Gap H)

> **Reproduction status**: code paths traced exhaustively; live spaarkedev1 reproduction NOT attempted within audit scope (would require operator session + network access).

### Scenario A: User in matter X workspace asks "what matter am I in?"

**Expected**: Assistant responds "You are working with matter 'Smith v. Jones' (or matter name from Dataverse). You're viewing the [page label]."

**Actual** (traced through code):
1. URL: `...?entityType=sprk_matter&entityId=<guid>&matterId=<guid>`
2. SpaarkeAi parses URL (main.tsx:356-377) → entityLogicalName="sprk_matter" (raw)
3. ThreePaneShell builds entityContext { entityType: "sprk_matter" } (line 580-591)
4. ConversationPane builds hostContext { entityType: "sprk_matter", entityId, entityName: undefined, workspaceType: "spaarke-ai", pageType: undefined } (lines 2204-2211)
5. SprkChat POSTs to BFF: `POST /api/ai/chat/sessions` with body `{ HostContext: { EntityType: "sprk_matter", EntityId: "<guid>" } }`
6. BFF persists into session (ChatEndpoints.cs:317-322)
7. On first message: `PlaybookChatContextProvider.AppendEntityEnrichment` (line 543) called
   - Guard at line 546: `hostContext is null` → false (have one)
   - Guard at line 550: `EntityName` null → **return early without enrichment**
8. Even if EntityName fix applied (Gap B):
   - Guard at line 554: `PageType` null → **return early without enrichment**
9. Even if PageType fix applied (Gap C):
   - Guard at line 559: PageType must be in `PageTypeLabels` dictionary
10. `AppendMatterMemoryAsync` (line 650):
    - Guard at line 666: `EntityType == "matter"` (lowercased) — `"sprk_matter" != "matter"` → **return early without matter memory**
11. LLM receives bare system prompt + user question
12. **LLM response**: generic refusal or "I don't have information about your current matter" — matches operator's "nothing fixed" verdict

**Gap categorization**: Gaps A + B + C (PLUMBING)

### Scenario B: User in matter X asks "what documents are in this matter?"

**Expected**: Assistant calls SearchDocuments tool, returns list of matter X's documents.

**Actual** (traced):
1. Steps 1-6 same as Scenario A
2. `ResolveKnowledgeScopeAsync` (PlaybookChatContextProvider.cs:387) builds `ChatKnowledgeScope` with `ParentEntityType: hostContext?.EntityType = "sprk_matter"` (line 452)
3. `SprkChatAgentFactory.CreateAgentAsync` builds `DocumentSearchTools(_parentEntityType="sprk_matter", _parentEntityId=<guid>, ...)` (DocumentSearchTools.cs:53)
4. LLM calls `SearchDocumentsAsync(query="documents in this matter")`
5. Tool builds `RagSearchOptions { ... }` (line 74-82) — but **does NOT pass `ParentEntityType`/`ParentEntityId`** on this overload!
   - Need to verify: does `SearchDocumentsAsync` (line 67) pass parent-entity filter? Tool source at line 172 shows `ParentEntityType = _parentEntityType` only on `SearchDiscoveryAsync`. `SearchDocumentsAsync` body (lines 67-90) **does not pass `_parentEntityType` to RagSearchOptions**.
   - **Implication**: `SearchDocumentsAsync` is tenant-wide; `SearchDiscoveryAsync` is entity-scoped (when scope is valid).
6. RagService.cs:1232-1236 — applies `parentEntityType eq 'sprk_matter'` filter when SearchDiscoveryAsync is invoked
7. Index has `parentEntityType = 'matter'` (per RagEndpoints.cs:669, FileIndexingService population)
8. Filter `'sprk_matter' eq parentEntityType` matches zero documents
9. **LLM response**: "No relevant documents found" — matches "nothing fixed"

**Gap categorization**: Gap A (PLUMBING — naming convention); secondary: `SearchDocumentsAsync` vs `SearchDiscoveryAsync` tool selection by LLM is fragile (tool-design issue, not audit-blocking)

### Scenario C: User opens SpaarkeAi from outside a matter context (e.g., direct dashboard link)

**Expected**: Assistant degrades gracefully — "I don't have a matter context. What can I help you with?"

**Actual** (traced):
1. URL: no entityType/entityId params
2. SpaarkeAi main.tsx:364-372 sets `entityLogicalName=undefined, entityId=undefined`
3. ThreePaneShell.tsx:581 — `if (!entityLogicalName || !entityId) return null;` → entityContext = null
4. ConversationPane.tsx:2205 — `hostContext = entityContext ? {...} : undefined`
5. BFF receives session-create with `HostContext: null` (or absent)
6. All three enrichment paths in PlaybookChatContextProvider correctly no-op on null hostContext (guards at line 546, 663)
7. **Assistant works generically** — this scenario is ACTUALLY fine. Tenant-wide search still works.

**Gap categorization**: NONE — this scenario degrades gracefully; not a UAT failure.

### Scenario D: Multi-turn conversation — does context persist across messages?

**Expected**: Asking "what about that matter again?" in turn 5 should reference the same matter context.

**Actual** (traced):
1. Session persisted in Redis (per ChatEndpoints.cs comments about "Redis hot cache")
2. `session.HostContext` persisted at session-create (line 321), preserved across messages
3. Every message-send (SendMessageAsync) re-reads `session.HostContext` (line 535)
4. So HostContext IS persisted. The problem is the same as Scenario A — the persisted HostContext is unusable due to Gaps A+B+C.

**Gap categorization**: Subsumed by Gap A — no new gap surfaces in multi-turn.

### Scenario E: User on matter X workspace, switches to "Daily Briefing" workspace tab, asks chat "what's in my daily briefing?"

**Expected (if "chat follows tab" UX)**: Assistant knows the active workspace is Daily Briefing.
**Expected (if "chat follows launch context" UX)**: Assistant still talks about matter X.

**Actual** (traced):
- `ConversationPane` does NOT subscribe to `active_widget_changed` events from the pane bus (only `tab_change` for telemetry + `selection_changed` for chip)
- `hostContext` is fixed at mount per URL — switching tabs has no effect
- LLM continues thinking about matter X (assuming Gaps A+B+C fixed)

**Gap categorization**: Gap D (MISSING IMPLEMENTATION, possibly out-of-MVP per operator UX decision)

---

## 5. Disposition recommendation — Wave 12.4 task scope

### Per-gap disposition

| Gap | Disposition | Recommended Wave 12.4 task | Effort |
|---|---|---|---|
| **A** Naming convention split | **FIX IN MVP** — most-impactful single root cause | Add normalization layer at BFF boundary: in `ChatEndpoints.CreateSessionAsync` (or in `ChatHostContext` constructor / a validator filter), normalize `sprk_matter|sprk_project|sprk_invoice → matter|project|invoice`. Decision required: do we ALSO update `StandaloneChatContextProvider` / `DataverseQueryTools` / `AnalysisChatContextResolver` to use normalized form (consistency wins) — or accept the existing split and ONLY normalize at the chat-session boundary (minimum-touch fix)? Recommend MINIMUM-TOUCH (normalize at boundary only); the other surfaces aren't operator-blocking today. | 0.5 day (normalize at boundary) — 1 day (add an `EntityTypeNormalizer` helper + tests + flip both boundaries) |
| **B** EntityName never populated | **FIX IN MVP** | Add server-side lazy-fetch of EntityName when HostContext arrives with EntityId but no EntityName: in `PlaybookChatContextProvider` add a small Dataverse name-lookup before `AppendEntityEnrichment`. Single read, cached for session lifetime. (Decision rationale: minimum-touch — SpaarkeAi-side fix would require Xrm form access which adds frame-walk fragility; server-side fix is centralized.) | 0.5 day |
| **C** PageType never populated | **FIX IN MVP** | Either: (a) SpaarkeAi sends a default `pageType: "AssistantPane"` (1-line change in ConversationPane.tsx:2208), and BFF `PageTypeLabels` dictionary gets a corresponding entry; OR (b) loosen the `AppendEntityEnrichment` guard at line 554 to use a default label when PageType is null. Recommend (a) for explicit telemetry value. | 0.25 day |
| **D** Tab-change doesn't refresh hostContext | **DEFER post-MVP** | Subscribe ConversationPane to `active_widget_changed`, plumb a setter into AiSessionProvider for entityContext, decide on session-recreate vs in-place update semantics. Non-trivial; operator hasn't surfaced this specific UX expectation in Wave 12 AC13/AC14 wording. | 1-2 days; defer to a follow-up "assistant-workspace-context-sync" project IF operator wants this UX |
| **E** No "current matter" tool | **NO ACTION** (subsumed by A+B+C) | — | — |
| **F** Verify matter documents indexed in spaarkedev1 | **UAT VERIFICATION STEP** (not a code task) | Wave 12.4 includes "after deploying Gap A fix, smoke `POST /sessions/{id}/messages` with 'what documents are in this matter?' and confirm RAG returns >0 hits. If 0 hits, file separate task for indexing verification". | 0.25 day of UAT time |
| **G** LoadKnowledgeNodeExecutor is not the chat retrieval path | **NO ACTION** (documentation only) | Note in Wave 12.4 design doc that the R5 "deferred retrieval over SPE" concern is about Daily Briefing's LoadKnowledge node — NOT chat retrieval. Chat retrieval works via `DocumentSearchTools` + `RagService` and is functional once Gap A is fixed. | — |
| **H** Deployment coordination | **OPERATOR DECISION** | Track in wave12 plan §10 Q3; not a Wave 12.4 task | — |

### Total MVP effort estimate

**1.5 working days code** (Gaps A + B + C) + **0.25 day UAT verification** (Gap F) + **0.25 day for code-review/adr-check at FULL rigor on the Gap A change** = **2 days minimum, 4 days with iteration buffer**.

This fits comfortably in the wave12-mvp-completion-plan W12.4 envelope of "1-4 weeks (audit-dependent)" — actually well below the high end.

### MVP-scope recommendation

**PLUMBING-ONLY** is in MVP scope (Gaps A, B, C). The plumbing already exists; it just doesn't reach end-to-end because of naming inconsistency + two missing client-side fields (or one server-side lazy fetch).

**Partial-grounding** (matter-record fields injected into system prompt) is ALSO in MVP scope by side effect — `AppendEntityEnrichment` + `AppendMatterMemoryAsync` are already implemented and ship enrichment when their guards pass. Fixing Gap A unlocks both.

**Out-of-scope-retrieval-needed** scenario is NOT triggered: chat-time RAG retrieval over already-indexed matter documents IS implemented (`DocumentSearchTools` + `RagService`) and works once naming convention is normalized. The "retrieval-over-SPE-deferred" concern in the task POML inputs refers to the Daily Briefing `LoadKnowledgeNodeExecutor` placeholder (Gap G) — a different code path.

**Out-of-scope-tool-use-needed** scenario is also NOT triggered: the assistant doesn't need new tools for Scenarios A-D. System prompt enrichment + existing search tools cover the MVP behaviour. Action Engine / tool-use (e.g., "create a task in this matter") remains on hold per separate project status.

---

## 6. Blockers + assumptions

1. **Deployed-env reproduction not attempted** — audit is code-trace only. Operator UAT against actual spaarkedev1 deployment may surface additional gaps (e.g., the documents aren't indexed at all — Gap F).
2. **`useEntityResolver`-style normalization is the canonical client-side pattern** but SpaarkeAi doesn't use it. Decision needed: bring SpaarkeAi onto `useEntityResolver` (consistency win, larger diff) OR add a minimum-touch normalization line in main.tsx OR do the normalization at the BFF boundary. Recommend BFF-side per Gap A disposition above (smallest blast radius).
3. **Documentation drift**: SPAARKEAI-WORKSPACE-ARCHITECTURE.md §1 (line 192-208) documents the URL-param entity context flow but does NOT call out the convention split or the missing EntityName/PageType. Wave 12.4 should update this doc as part of the fix.
4. **`ChatHostContext.IsValid()` is dead code** (zero call sites). Fixing the convention split is a good moment to either delete IsValid OR wire it up + fail-fast on invalid inputs. Recommend wiring it up — gives the boundary observable invariant.

---

## 7. References

| File | Lines | What it shows |
|---|---|---|
| `src/solutions/SpaarkeAi/src/main.tsx` | 356-377, 388-406 | URL param parsing + raw entityType pass-through |
| `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` | 580-591 | EntityContext build with `as EntityType` cast |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | 2204-2211, 2435, 853-871 | hostContext build (no EntityName/PageType) + workspace pane subscriptions |
| `src/client/shared/Spaarke.AI.Context/src/providers/useEntityResolver.ts` | 97-103, 128-177 | The canonical normalization (UNUSED by SpaarkeAi) |
| `src/client/shared/Spaarke.AI.Context/src/types/entity-context.ts` | 19, 36-58 | EntityType union (normalized) + EntityContext shape |
| `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatHostContext.cs` | 34-48 | DTO contract; IsValid never called |
| `src/server/api/Sprk.Bff.Api/Models/Ai/ParentEntityContext.cs` | 35-64 | Normalized allow-list (matter/project/invoice/account/contact) |
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | 297-329, 416-417, 535, 1158 | HostContext receive + persistence + reads |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` | 387-464, 543-615, 650-703 | Knowledge scope build + entity enrichment + matter memory (3 guards each) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` | 1133-1144 | INVERSE map matter→sprk_matter (assumes normalized input) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | 1539-1545 | Convention-B "sprk_matter" usage |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/StandaloneChatContextProvider.cs` | 63-85 | Convention-B "sprk_matter" usage (standalone endpoint) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AnalysisChatContextResolver.cs` | 550-559 | Convention-B "sprk_matter" usage (resolver) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/DataverseQueryTools.cs` | 36-106 | Convention-B "sprk_matter" allow-list |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/DocumentSearchTools.cs` | 41-90, 167-180 | parent-entity-scoped vs unscoped tool methods |
| `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` | 1232-1236 | RAG OData filter for parentEntityType |
| `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs` | 668-689 | Convention-A "matter" usage at index-write side |
| `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` | 315-331 | Pass-through of caller-provided ParentEntityType to index |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LoadKnowledgeNodeExecutor.cs` | 16-50, 75-362 | Daily Briefing placeholder; NOT the chat retrieval path |
| `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` | 192-208 | Context flow documentation (now known to be incomplete re: convention) |
| `projects/spaarke-ai-platform-unification-r7/notes/wave12-mvp-completion-plan.md` | 66-73, 95-103, 113 | Wave 12 plan: Assistant↔Workspace is MVP-critical; AC13-AC15 |

---

## 8. Resolution — Wave 12 task 130 (added 2026-06-30)

> **Author**: Wave 12.2 task 130 (task-execute FULL rigor)
> **Date**: 2026-06-30
> **Scope of this Resolution subsection**: covers ONLY the `IMembershipResolverService` 0-results-for-all-users bug. The Gap A/B/C plumbing fixes for Assistant↔Workspace remain owned by Wave 12.4 tasks T150/T151/T152/T153 — those are independent of this membership-resolver fix.
> **Why this Resolution lives in audit 120**: audit 120 §2.1 (line 6 of original audit) flagged `IMembershipResolverService` as broken context and routed it to Wave 12.2 T130; this is the resolution record for that referral.

### Root cause (file:line evidence)

**`src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipOptions.cs:30` (pre-fix)** — `IncludedIdentityTables` defaulted to an empty `List<IdentityTableConfig>`.

**`MembershipFieldDiscoveryService.cs:268-299`** — `BuildDiscoveryResult` classifies every membership-bearing lookup as `IgnoredField` with reason `target-table-not-in-identity-list` because no target matches an identity table (the gate at line 280 `identityTypeByTable.TryGetValue(...)` returns false for every entry).

**`MembershipResolverService.cs:220-244`** — `ResolveAsync` short-circuits when `descriptors.Count == 0` and returns an empty `MembershipResponse` via `BuildEmptyResponse`. No FetchXml query is ever built.

### Why this happened (configuration omission)

The `"Membership"` appsettings section is present in **only one** file: `src/server/api/Sprk.Bff.Api/appsettings.Development.json.template` — a TEMPLATE for the **gitignored** `appsettings.Development.json` that exists only on local developer machines. The deployed-environment configs (`appsettings.template.json`, `appsettings.Production.json.template`, and the Bicep `appSettings` block in `infrastructure/bicep/stacks/model2-full.bicep:187-225`) all OMIT it. Result: every deployed BFF instance ran with empty `IncludedIdentityTables` + empty `GlobalFieldExclusions`.

Verified in spaarkedev1 via `mcp__dataverse`:
- `sprk_matter` schema has all the expected membership-bearing lookups: `ownerid` (systemuser), `owningteam` (team), `owningbusinessunit` (businessunit), `sprk_assignedattorney1/2` + `sprk_assignedparalegal1/2` + `sprk_assignedtoexternal` + `sprk_assignedtointernal` (all contact), `sprk_assignedlawfirm1/2` (sprk_organization).
- Real matter rows exist with `sprk_assignedattorney1` populated (e.g., matter `491b1efe-e562-f111-ab0c-000d3a4d8152` "Test New Matter via Workspace", attorney = `8e9918a9-9021-f111-88b5-7c1e520aa4df`).
- The data is fine; the discovery layer was filtering everything out.

### Fix (smallest change targeting root cause)

Two-file edit, NO interface change, NO new abstraction:

1. **`MembershipOptions.cs`** — added a new class `MembershipOptionsDefaults : IPostConfigureOptions<MembershipOptions>` that seeds the 6 canonical Spaarke identity tables + 4 standard audit-field exclusions ONLY when the bound list is empty. Constants `CanonicalIdentityTables` + `CanonicalAuditFieldExclusions` are public static for test reuse + future operator inspection.
2. **`MembershipModule.cs`** — registered the post-configure via `services.AddSingleton<IPostConfigureOptions<MembershipOptions>, MembershipOptionsDefaults>()` immediately after the existing `Configure<MembershipOptions>` call.

**Why post-configure (not property-initializer defaults)**: `IConfiguration.Bind` APPENDS to `List<T>` properties — property-initializer defaults would double up entries when operators bind the same names. The post-configure pattern runs AFTER binding and only seeds when the bound list is empty, so operator config replaces cleanly. This invariant is pinned by a dedicated test (`AddMembership_WithOperatorBoundIdentityTables_DoesNotSeedDefaults`).

### Tests added (per ADR-038 KEEP-path conventions)

1. **`DiscoverAsync_WithPostConfiguredDefaultMembershipOptions_DiscoversMatterAssignmentFields`** (in `MembershipFieldDiscoveryServiceTests.cs`) — the regression-protection test. Constructs default `MembershipOptions`, applies `MembershipOptionsDefaults.PostConfigure`, runs discovery against canonical `sprk_matter` lookup roster, asserts all 6 membership-bearing lookups land in `DiscoveredFields`. Pre-fix, all 6 would land in `IgnoredFields` with reason `target-table-not-in-identity-list`.
2. **`AddMembership_WithEmptyConfig_SeedsCanonicalDefaultsViaPostConfigure`** (in `MembershipOptionsTests.cs`) — DI-level verification: empty `IConfiguration` → 6 identity tables + 4 exclusions present via the full DI pipeline.
3. **`AddMembership_WithOperatorBoundIdentityTables_DoesNotSeedDefaults`** (in `MembershipOptionsTests.cs`) — pins the gating contract: operator-bound list of 1 stays at 1 (no append). Protects against future regression to property-initializer defaults.
4. **Updated**: `DefaultValues_RawConstruction_AreEmptyAndScalarsSetToCanonical` (renamed from `DefaultValues_AreConservativeEmptyDefaults`) — documents the property-level-empty + post-configure-seeds split.

Tests follow ADR-038: integration-shape where DI wiring matters; pure unit-test for the `PostConfigure(options)` invariant. NO `Mock<HttpMessageHandler>`, NO DI-registration tests of the form `Assert.NotNull(services.GetRequiredService<X>())`, NO ctor null-check tests.

### Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors (19 pre-existing warnings unrelated to T130).
- `dotnet test --filter ~Membership`: 195/195 pass.
- Full BFF unit suite: 7,570 pass / 6 pre-existing baseline failures unrelated to T130 (confirmed by stashing the T130 change and re-running — failures reproduce). The 6 baseline failures are in `AuditLogServiceTests`, `ExecutorConfigSchemasEndpointTests`, `SummarizeSessionEndpointContractTests`, `KnowledgeDeploymentConfigTests`, `PlaybookDispatcherPhaseBTests`, `SessionFilesCleanupJobTests` — none touch `Membership/**`.
- Publish-size impact: negligible (one new class + property setter changes; no new packages).
- No new DI registrations beyond `IPostConfigureOptions<MembershipOptions>` (zero transient/scoped registrations added).
- `IMembershipResolverService` interface signature unchanged (binding contract per POML constraint).

### Post-deploy validation (reserved for T136 wave-end gate)

After deploying the fix to spaarkedev1:

1. **Smoke against `GET /api/users/me/memberships/sprk_matter`** for 3 test systemuserids:
   - **Operator (Ralph Schroeder spaarke.com)** systemuserid `1d02f31c-1872-f011-b4cb-7c1e52671ad0` — expect non-zero matter list (he owns multiple matters per the spaarkedev1 query).
   - 2 additional test users selected by operator.
2. **Expected response shape**: `Count > 0`, `ByRole` contains at least one of `owner`, `assignedAttorney`, `assignedParalegal`, `assignedLawFirm`, `owningTeam`, `owningBusinessUnit` per the canonical strategy.
3. **NEGATIVE test**: query a user known to have no matter assignments → expect `Count: 0` + empty `Ids` (not an error).
4. **Cross-check by oracle**: the systemuserid's matters reported by the resolver MUST match a hand-rolled FetchXml that ORs `ownerid eq <userId>` with `sprk_assignedattorney1 eq <contactId-for-user>` (where contactId comes from `IIdentityNormalizationService` cross-ref — note that for spaarkedev1 the contact cross-ref may need verification because the audit found only 1 contact has `sprk_systemuser` populated and it points to a different systemuserid than the AAD oid mapping would suggest; if matter `ByRole.assignedAttorney` returns empty when it should not, this points to a secondary cache-refresh / identity-normalization issue surfaced after this fix unblocks the discovery layer).

### Out of scope for T130 (handed off elsewhere)

- **Identity normalization issue** (contact cross-ref via `azureactivedirectoryobjectid` — the audit notes that `contact.azureactivedirectoryobjectid` field appears not to be present in the spaarkedev1 schema per `mcp__dataverse__describe tables/contact`; the canonical path may need to be `contact.sprk_systemuser` for this environment). This is a candidate follow-up if T136 post-deploy validation surfaces it as a remaining blocker.
- **Gap A/B/C plumbing fixes for Assistant↔Workspace** (separate POMLs T150/T151/T152/T153).
- **Operator deployment of `Membership__*` App Service settings** — NOT required after this fix because defaults are seeded in code. Operator can still set them to override; the existing R3 runbook (`projects/spaarke-platform-foundations-r3/notes/bff-deploy-runbook.md` lines 109-118) remains valid for the EventPublisher / JunctionUpdater / CacheInvalidator flags (those are independent kill-switches and remain unaffected by this fix).

### Commit

- `451603bac` — `fix(bff/r7): seed canonical MembershipOptions defaults via post-configure (T130 root-cause fix)`

---

## 9. Resolution — Wave 12 task 150 / Gap A (added 2026-06-30)

> **Author**: Wave 12.4 task 150 (task-execute FULL rigor)
> **Date**: 2026-06-30
> **Scope of this Resolution subsection**: covers ONLY Gap A — boundary normalization at ChatHostContext. Gaps B (EntityName lazy-fetch), C (default PageType), D-H remain owned by parallel/follow-up POMLs T151/T152/T153.

### Approach

Single normalization point at the BFF chat-session boundary (the smallest blast radius per §5 disposition A recommendation). The fix is implemented in `ChatHostContext` via an init-accessor that invokes a new static helper `EntityTypeNormalizer.Normalize()`. The accessor pattern is load-bearing because it covers ALL three construction paths — primary constructor, `with` expression (used by `SwitchContextAsync`), and System.Text.Json deserialization (the Redis hot-tier round-trip used on every chat message).

### Actual surfaces touched (vs §5 disposition prediction)

The audit's §3 evidence list called out 5 inbound consumers of the convention. The fix landed on a slightly different distribution because the boundary normalization resolves 3 consumers by side effect (no code change needed) BUT requires 2 OTHER read-site updates that the audit didn't predict — they only become broken once normalization is in place:

| # | Surface | File:line | Required change |
|---|---|---|---|
| 1 | **Boundary normalization point** | `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatHostContext.cs:34-86` | NEW init-accessor pattern + new helper at `Models/Ai/Chat/EntityTypeNormalizer.cs` |
| 2 | System prompt enrichment | `Services/Ai/Chat/PlaybookChatContextProvider.cs:543` (`AppendEntityEnrichment`) | None — guard now passes because HostContext.EntityType is canonical |
| 3 | Matter memory injection | `Services/Ai/Chat/PlaybookChatContextProvider.cs:650-666` (`AppendMatterMemoryAsync`) | None — guard now passes (EntityType == "matter") |
| 4 | Knowledge-scope build → RAG filter + DocumentSearchTools | `Services/Ai/Chat/PlaybookChatContextProvider.cs:452` (`ResolveKnowledgeScopeAsync`) → flows to `RagService.cs:1232-1236` + `Tools/DocumentSearchTools.cs:53` | None — scope's `ParentEntityType` is now canonical, matching the index's `parentEntityType = 'matter'` |
| 5 | Matter-id telemetry extraction | `Services/Ai/Chat/SprkChatAgentFactory.cs:1539-1551` (`TryParseMatterId`) | UPDATED — now accepts both "matter" (post-fix) AND "sprk_matter" (defensive forward-compat). Without this, matter-id telemetry would have BROKEN post-fix because the check was raw-only. |
| 6 | Analysis chat entity-context columns + Dataverse retrieve | `Services/Ai/Chat/AnalysisChatContextResolver.cs:362-374, 550-580` (`GetEntityContextColumns` + new `ToDataverseLogicalName`) | UPDATED — switch arms accept both forms; explicit inverse-map (canonical → raw) for `IDataverseEntityService.RetrieveAsync`. Without this, analysis chat entity-record retrieval would have BROKEN post-fix. |

### Surfaces NOT touched (audit §3 mentioned them but no change needed)

- `StandaloneChatContextProvider.cs:63-85` — pre-session probe receives `entityType` directly from URL parameter, NOT via HostContext. Different bounded context.
- `DataverseQueryTools.cs:45-106` — tool allow-list operates on raw Dataverse names; LLM passes entity type as a tool argument, not via HostContext.
- `RagEndpoints.cs:668-689` + `BulkRagIndexingJobHandler.cs:278` — INDEX-WRITE side already operates on canonical form (per audit §3 evidence list "convention-A").
- `PlaybookDispatcher.MapEntityTypeToRecordType` (line 1133-1144) — expects canonical input (per audit §3); now correctly receives canonical.

### Tests added (per ADR-038 KEEP-path conventions)

- New: `tests/unit/Sprk.Bff.Api.Tests/Models/Ai/Chat/EntityTypeNormalizerTests.cs` — 30 tests (16 [Fact]/[Theory] methods, 30 expanded cases) covering:
  - Helper raw → canonical mapping for all 3 Spaarke logical names
  - Helper idempotence on already-canonical inputs (matter/project/invoice/account/contact)
  - Case-insensitivity + whitespace trimming
  - Pass-through for unknown / non-parent-business types (e.g. `sprk_analysisoutput` — critical regression-protection for the analysis-session HostContext slot)
  - Null / empty / whitespace input handling
  - `ChatHostContext` primary constructor normalization
  - `ChatHostContext` `with` expression normalization (load-bearing for `SwitchContext`)
  - `ChatHostContext` System.Text.Json round-trip (load-bearing for Redis hot tier)
  - `ChatHostContext.IsValid()` behaviour pre/post normalization (the dead-code validator now meaningfully passes for raw inputs)

No `Mock<HttpMessageHandler>`, no DI-registration tests, no ctor null-check tests.

### Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: **0 errors**, 19 pre-existing warnings (none from T150 surface area)
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`: **0 new failures** added. Before-T150 baseline on master after T130 landed: 13 failures / 7563 passed. After T150: **6 failures / 7570 passed** — T150 indirectly resolved 7 pre-existing failures (likely tests previously depending on broken canonical-form path). The 6 remaining failures are all unrelated to chat HostContext (KnowledgeDeploymentConfig defaults, SessionFilesCleanupJob, AuditLogService, SummarizeSessionEndpoint, ExecutorConfigSchemas) — confirmed pre-existing by stash + reproduction.
- **Compressed publish size**: 47.6 MB (well within 60 MB ceiling per `.claude/constraints/azure-deployment.md` NFR-01)
- **Code review + ADR check**: 0 violations, 0 warnings (Step 9.5 Quality Gates per task-execute protocol)

### Backward / forward compatibility

- **Backward**: Clients may send either `sprk_matter` or `matter`; both normalize. Legacy raw-form data in Redis caches normalizes on JSON deserialize (covered by `ChatHostContext_JsonRoundTrip_NormalizesRawInputFromDeserialization` test).
- **Forward**: `SprkChatAgentFactory.TryParseMatterId` accepts BOTH forms defensively (canonical post-fix + raw for any code path that bypasses `ChatHostContext` construction). No callers currently do this; the dual-form acceptance is a forward-compat insurance.

### Out of scope for T150 (deliberately preserved for follow-up tasks)

- **Gap B** (EntityName lazy-fetch in `PlaybookChatContextProvider`) — T151
- **Gap C** (default PageType handling) — T152
- **Gaps D-H** (chat hostContext refresh on tab change, current-matter tool, etc.) — T153
- **`PlaybookChatContextProvider.cs` itself** — deliberately NOT modified by T150 so T151/T152/T153 can layer cleanly without merge conflict.

### Commit

- `287e7b0a9` — `fix(bff/r7): T150 Wave 12 — normalize EntityType at ChatHostContext boundary (audit 120 Gap A)` (pushed to `work/spaarke-ai-platform-unification-r7`)

---

## 10. Resolution — Wave 12 task 151 / Gap B (added 2026-06-30)

> **Author**: Wave 12.4 task 151 (task-execute FULL rigor)
> **Date**: 2026-06-30
> **Scope**: ONLY Gap B — server-side EntityName lazy-fetch in `PlaybookChatContextProvider`.

### Approach

Per §5 disposition B recommendation: server-side lazy-fetch (minimum-touch, centralised). When `ChatHostContext.EntityName` is null/whitespace but `EntityType` + `EntityId` are present, the provider retrieves the entity's primary-name attribute (`sprk_name`) from Dataverse via `IGenericEntityService.RetrieveAsync` and uses it for system-prompt enrichment. Result memoised per-request to avoid duplicate round-trips when the prompt is composed twice in the same request lifecycle.

### Actual surfaces touched

- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` — added:
  - `using Microsoft.Xrm.Sdk;` import for `Entity` return type
  - `IGenericEntityService? _entityService` field (optional ctor param — nullable for backward-compat with existing test fixtures that pre-date T151; production DI satisfies via existing `GraphModule.AddSingleton<IGenericEntityService>(...)` forward from `IDataverseService` composite)
  - `Dictionary<string, string?> _entityNameCache` instance field (Scoped lifetime → naturally per-request; null cached value = "previous fetch failed; do not retry")
  - `TryResolveEntityNameAsync` private async helper (single Dataverse retrieve + cache; soft-fails on any exception path)
  - `ToDataverseLogicalName` private static helper (maps "matter"/"project"/"invoice" → "sprk_*"; pass-through for unknown types)
  - `AppendEntityEnrichmentAsync` (renamed from sync `AppendEntityEnrichment`): calls `TryResolveEntityNameAsync` BEFORE the EntityName guard when EntityName is missing but EntityType+EntityId present
- Both call sites updated to `await`: line 219 (generic mode) and line 329 (playbook mode)

### Tests added

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookChatContextProviderEntityNameLazyFetchTests.cs` — 6 new tests (later 7 after T152's end-to-end addition):

1. `GetContextAsync_MissingEntityName_LazyFetchesNameFromDataverseAndEnriches` — happy path
2. `GetContextAsync_MissingEntityName_LazyFetchFails_SkipsEnrichmentAndContinues` — soft-fail invariant
3. `GetContextAsync_TwoCallsSameInstance_LazyFetchCachedAcrossCalls` — per-request cache invariant (asserts `RetrieveAsync` called `Times.Once` despite 2 GetContextAsync calls)
4. `GetContextAsync_EntityNameAlreadyPresent_DoesNotInvokeLazyFetch` — `Times.Never` when EntityName supplied
5. `GetContextAsync_MissingEntityName_InvalidGuidId_SkipsLazyFetchAndEnrichment` — non-GUID boundary
6. `GetContextAsync_MissingEntityName_RawLogicalNameType_LazyFetchUsesSameLogicalName` — `sprk_matter` form pass-through

Test fixture pattern: `IDataverseService` mock satisfies `IGenericEntityService` injection because `IDataverseService` composite interface inherits `IGenericEntityService` (per `Spaarke.Dataverse.IDataverseService.cs:9-19`). Same composite mock already used by sibling tests (`PlaybookChatContextProviderEnrichmentTests`).

NO `Mock<HttpMessageHandler>`, NO DI-registration tests, NO ctor null-check tests per ADR-038.

### Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: **0 errors**, 19 pre-existing warnings (unchanged from T150 baseline)
- `dotnet test --filter "FullyQualifiedName~PlaybookChatContextProvider"`: **46/46 pass** (6 new T151 tests + 40 existing)
- Full chat suite (`--filter "FullyQualifiedName~Chat"`): **1253/1253 pass**
- Full BFF unit suite: same 6 pre-existing baseline failures from audit Resolution §9 (`AuditLogServiceTests`, `ExecutorConfigSchemasEndpointTests`, `SummarizeSessionEndpointContractTests`, `KnowledgeDeploymentConfigTests`, `PlaybookDispatcherPhaseBTests`, `SessionFilesCleanupJobTests`) — none touch chat hostContext / entity-name surfaces; T151 introduces **0 new failures**.
- **Compressed publish size**: 46 MB (well within 60 MB NFR-01 ceiling; -1.6 MB vs T150 baseline 47.6 MB because no new packages, only an additional optional ctor param + helper methods)
- No new DI registrations (existing `IGenericEntityService` singleton already wired in `GraphModule.cs:70`)

### Out of scope for T151

- Gap C (default PageType) — T152
- Gaps D/E/F/G/H — T153
- `IGenericEntityService` becoming a REQUIRED (non-nullable) ctor param — deferred to keep existing test fixtures green; the nullable+default-null pattern matches `IPromptBudgetTracker`'s existing convention

### Commit

- `e2b4abdad` — `feat(bff/r7): T151 Wave 12 — server-side EntityName lazy-fetch in PlaybookChatContextProvider (audit 120 Gap B)` (pushed to `work/spaarke-ai-platform-unification-r7`)

---

## 11. Resolution — Wave 12 task 152 / Gap C (added 2026-06-30)

> **Author**: Wave 12.4 task 152 (task-execute FULL rigor)
> **Date**: 2026-06-30
> **Scope**: ONLY Gap C — default `ChatHostContext.PageType` substitution at the enrichment guard.

### Approach

Per §5 disposition C option (b) (loosen the guard with a default label) blended with option (a) (explicit default value): introduced an `internal const string DefaultPageType = "entityrecord"` on `PlaybookChatContextProvider` and a one-line null-coalesce in `AppendEntityEnrichmentAsync` so the missing-PageType case maps to "main form view" via the existing `PageTypeLabels` dictionary. Choice rationale: chat requests carrying EntityType+EntityId are by definition on an entity-form surface, and "entityrecord" is the canonical Dynamics page type for that.

### Surfaces touched

- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` — added:
  - `internal const string DefaultPageType = "entityrecord"` with full rationale XML doc
  - One null-coalesce in `AppendEntityEnrichmentAsync`: `var pageType = string.IsNullOrWhiteSpace(hostContext.PageType) ? DefaultPageType : hostContext.PageType;`
  - Guards downstream (line 612 +) now operate on the local `pageType` variable rather than `hostContext.PageType` directly

### Contract change — existing test rewired

`PlaybookChatContextProviderEnrichmentTests.GetContextAsync_NullPageType_NoEnrichmentBlockAppended` was the source of the bug: it asserted that a null PageType produced no enrichment. After T152 the contract is that the default substitution happens and enrichment DOES fire. Test renamed to `GetContextAsync_NullPageType_EnrichmentAppendedWithDefaultPageType` and its assertions inverted to verify the new contract.

The "unknown" PageType test (`GetContextAsync_UnknownPageType_NoEnrichmentBlockAppended`) is **unchanged** — "unknown" is the client's explicit not-known signal and is preserved as a deliberate no-enrichment trigger so upstream misconfiguration stays visible.

### Tests added

- `PlaybookChatContextProviderEntityNameLazyFetchTests.GetContextAsync_AuditScenarioA_ClientSendsOnlyEntityTypeAndId_EnrichmentFires` — end-to-end protection covering T150 + T151 + T152 combined. Client sends ONLY EntityType + EntityId (the SpaarkeAi shipped behaviour pre-fixes) → assistant chat receives full matter-aware enrichment.

### Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: **0 errors**
- `dotnet test --filter "FullyQualifiedName~PlaybookChatContextProvider"`: **47/47 pass** (was 46 before T152; +1 Scenario A end-to-end test)
- Publish size: negligible impact (one const + one ternary)

### Commit

- `800f23a0a` — `feat(bff/r7): T152 Wave 12 — default PageType in PlaybookChatContextProvider (audit 120 Gap C)` (pushed to `work/spaarke-ai-platform-unification-r7`)

---

## 12. Resolution — Wave 12 task 153 / Gaps D-H (added 2026-06-30)

> **Author**: Wave 12.4 task 153 (task-execute FULL rigor)
> **Date**: 2026-06-30
> **Scope**: Process the remaining 5 gaps (D, E, F, G, H) per the audit's §5 disposition table. None of the 5 are in-scope code fixes per the audit's own dispositions — T153 records dispositional closure + files deferred-work tracker for the one item the audit explicitly marked DEFER (Gap D).

### Per-gap closure

| Gap | Audit §5 disposition | T153 closure action | Status |
|---|---|---|---|
| **D** Tab-change doesn't refresh hostContext | DEFER post-MVP | Filed as `DEF-002` in `notes/defer-issues.md` with concrete-behaviour rationale + scope-of-work + when-to-address. To be filed as GitHub Issue on next `push-to-github` invocation per project DEF convention. | DEFERRED |
| **E** No "current matter" tool | NO ACTION (subsumed by A+B+C) | Recorded in `notes/defer-issues.md` § R7-NOACTION-001. After T150+T151+T152, system-prompt enrichment answers "what matter am I in?" deterministically — no separate tool needed. | NO-ACTION (closed) |
| **F** Verify matter documents indexed in spaarkedev1 | UAT VERIFICATION (not a code task) | Owned by T136+T154 UAT smoke. If post-deploy smoke shows 0 hits on `"what documents are in this matter?"`, file ISS-NNN at that point. Recorded in § R7-NOACTION-001. | NO-ACTION (UAT-owned) |
| **G** LoadKnowledgeNodeExecutor is NOT the chat retrieval path | NO ACTION (documentation only) | Already documented in audit 120 §3 Gap G + Wave 12 plan §2.3 + W11 architecture doc §5. No additional documentation edits needed. | NO-ACTION (documented) |
| **H** Deployment coordination across spaarkedev1 | OPERATOR DECISION | Tracked in `notes/wave12-mvp-completion-plan.md` §10 Q3. Not a code task; deployment scripting (T136+T154) + operator notification own. | NO-ACTION (operator-owned) |

### Why no `PlaybookChatContextProvider.cs` edits in T153

T153 inspected the audit §5 disposition table and confirmed every remaining gap (D-H) is either:
1. Explicitly NOT a code task (E, F, G, H per audit), OR
2. Explicitly DEFERRED post-MVP per the audit's own recommendation (D)

The audit itself anticipated this outcome — Wave 12 task 153's input POML §inputs explicitly notes "Some gaps may turn out to be out-of-MVP-scope on closer read (e.g., retrieval-grounding-needed); flag those and defer". T153 honoured that anticipation rather than fabricating fixes not called for by the source audit.

### Tests added by T153

None — T153 adds no executable code paths. Audit closure is documentation work.

### Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: **0 errors** (no code change in T153)
- `dotnet test --filter "FullyQualifiedName~PlaybookChatContextProvider"`: **47/47 pass** (unchanged from T152 baseline)
- Publish size: 46 MB compressed (unchanged from T152)

### Commit (pending — this file change is staged as part of T153)

To be commit `<sha>` — `docs(bff/r7): T153 Wave 12 — audit 120 Gaps D-H disposition (DEF-002 + 4 no-action closures)` (will be pushed after this resolution section is committed)

---

*End audit 120 — Resolution §8 records T130 closure; Resolution §9 records T150 / Gap A; §10 records T151 / Gap B; §11 records T152 / Gap C; §12 records T153 / Gaps D-H dispositional closure. Audit 120 is now CLOSED for R7 W12.4 — Gap D follow-up is tracked as DEF-002.*
