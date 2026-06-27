# Wave 8 — DocumentSearch migration to typed handler

**Status**: Handler + seed rows + tests authored. Awaiting main-session SprkChatAgentFactory block removal + Seed-TypedHandlers.ps1 wiring.
**Date**: 2026-06-08
**Wave**: R6 Wave 8 (Q9 chat-tool migration, 1 of 4 parallel agents in this wave)
**Predecessor**: Wave 7c (KnowledgeRetrieval + VerifyCitations) — validated the multi-method dispatch + Wave 7b citation/widget envelope pattern that this migration reuses verbatim.
**Successors**: WebSearchHandler / CodeInterpreterHandler / LegalResearchHandler (parallel agents in Wave 8) + main-session SprkChatAgentFactory block removal (lines 754–785 of pre-Wave-8 file).

---

## What changed

| New file | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DocumentSearchHandler.cs` | Typed handler implementing `IToolHandler`; multi-method dispatch (`SearchDocuments` / `SearchDiscovery`) via `sprk_configuration.method` discriminator. |
| `infra/dataverse/sprk_analysistool-document-search-row.json` | Seed row — `DOCUMENT-SEARCH` / `SYS-Document Search`. |
| `infra/dataverse/sprk_analysistool-document-discovery-row.json` | Seed row — `DOCUMENT-DISCOVERY` / `SYS-Document Discovery`. |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DocumentSearchHandlerTests.cs` | 4-point contract + per-method behavior + tenant isolation + telemetry + adapter wiring tests. |

## The 2 tool codes

| Tool code | Method | Default topK | Scope | Widget |
|---|---|---|---|---|
| `DOCUMENT-SEARCH` | `SearchDocuments` | 5 | Knowledge-source-scoped via `ChatKnowledgeScope.RagKnowledgeSourceIds` (when playbook bound); tenant-wide otherwise | `output_pane` / `SearchResults` (no `isDiscovery` flag) |
| `DOCUMENT-DISCOVERY` | `SearchDiscovery` | 10 | Tenant-wide; constrained to parent-entity boundary when `ChatKnowledgeScope.ParentEntityType/Id` is set | `output_pane` / `SearchResults` with `isDiscovery=true` |

Both methods:
- Use hybrid (semantic + vector + keyword) search via `IRagService.SearchAsync`
- Return citation envelopes via `ToolResult.Metadata[ToolResultMetadataKeys.Citations]`
- Emit `output_pane` `SearchResults` widget via `ToolResult.Metadata[ToolResultMetadataKeys.Widget]` (chat path only)
- Preserve the legacy `DocumentSearchTools` text-formatting verbatim (so chat output is byte-equivalent for end users)

`SearchDiscovery` uses `MinScore = 0.5` (wider net than the default 0.7) and truncates excerpts to 300 chars for preview, matching the pre-R6 behavior.

## Capability gate: NOT set (rationale)

`sprk_requiredcapability` is **null** on both rows. The legacy hardcoded registration was gated only by `if (ragService != null)` — no capability check. Per the project's [no-backcompat-hacks-for-small-counts](https://github.com/anthropic/claude-memory/feedback_no-backcompat-hacks-for-small-counts.md) operating principle and the Wave 7b filter contract ("null/empty `sprk_requiredcapability` = always available"), the data-driven rows correctly mirror that always-available semantics.

This is consistent with the Wave 7c `KNOWLEDGE-SOURCE-GET` + `KNOWLEDGE-BASE-SEARCH` rows (also ungated — KnowledgeRetrieval was always-available pre-Wave-7b). It differs from Wave 7c's `CITATION-VERIFY` (gated to `verify_citations`) and from the still-pending Wave 8 `WEB-SEARCH` / `CODE-INTERPRETER` / `LEGAL-RESEARCH` rows (all of which will be capability-gated by their respective parallel agents).

## ChatKnowledgeScope: dual-axis scoping

`DocumentSearchTools` carried TWO orthogonal scope axes — both must be preserved in the typed handler:

1. **Knowledge-source scoping** (`RagKnowledgeSourceIds`) — used by `SearchDocuments` only. When the chat session is bound to a playbook with RAG knowledge sources, restrict the search to those sources. When null/empty, search is tenant-wide.
2. **Parent-entity scoping** (`ParentEntityType` + `ParentEntityId`) — used by `SearchDiscovery` only. When the chat session has a host-context entity (e.g., matter, project), constrain discovery to that entity's boundary. When null, discovery remains tenant-wide.

The handler reads both axes from `ChatInvocationContext.KnowledgeScope` (already present per Wave 7c — no `ChatInvocationContext` extension needed). This is the second handler (after `KnowledgeRetrievalHandler`) to consume `KnowledgeScope.RagKnowledgeSourceIds`, and the FIRST to consume `KnowledgeScope.ParentEntityType` / `ParentEntityId`.

## Surprises / non-obvious decisions

1. **No `ChatInvocationContext` extension required**. The original ask flagged "tenant isolation via TenantId always forwarded into RAG filter" as a possible stop-and-surface point. Verified: `ChatInvocationContext.TenantId` (inherited from `ToolInvocationContextBase`) is present and required; `ChatKnowledgeScope` already carries `ParentEntityType` / `ParentEntityId`. No interface or context changes needed.
2. **Default method = `SearchDocuments`**, not `SearchDiscovery`. This matches `KnowledgeRetrievalHandler`'s convention of defaulting to the narrower / more-restrictive method when the configuration discriminator is missing. `SearchDocuments` is narrower because it honors playbook knowledge-source scoping.
3. **Query text at Debug level only**. Per ADR-015 + the original ask ("NEVER log query text above Debug"), the handler emits `queryLen` at Information and the actual query text at Debug only (one line per method). Sentinel-string test verifies excerpt content never appears at any level; an additional explicit assertion verifies query text never appears at Information level.
4. **Widget shape preserved verbatim**. The pre-R6 `DocumentSearchTools` widget data shape (with `chunkId` / `documentId` / `documentName` / `knowledgeSourceName` / `score` / `chunkIndex` / `chunkCount` / `excerpt` / `citationMarker` properties, plus `isDiscovery=true` on the discovery row) is preserved byte-equivalent. Frontend `SearchResults` widget continues to render unchanged.
5. **No stop-and-surface triggers hit**. Verified each: `IToolHandler` contract is sufficient (no streaming required); `ChatInvocationContext` carries all needed fields (`KnowledgeScope` already present); no ADR blocks the optimal answer; Wave 7b metadata envelope fits perfectly (citations + widget for both methods).

## ADR compliance

| ADR / NFR | Result |
|---|---|
| ADR-010 (DI minimalism) | PASS — auto-discovered; zero manual DI line. |
| ADR-013 (PublicContracts boundary) | PASS — handler in `Services/Ai/Handlers/`; not exposed via `PublicContracts/`. |
| ADR-014 (per-tenant isolation) | PASS — `TenantId` validated + forwarded into every `IRagService.SearchAsync` call. Test covers both methods. |
| ADR-015 (data governance) | PASS — Information telemetry emits handler name + IDs + method discriminator + result count + duration only. Query text at Debug only; excerpt content never logged at any level. Sentinel-string tests verify. |
| ADR-018 (feature flags) | N/A — no feature flag introduced; the handler always registers when `IRagService` is registered. |
| ADR-029 (publish size) | PASS expected — BCL-only implementation; per-handler delta ≤+0.1 MB target. Main session to verify post-merge `dotnet publish`. |
| NFR-04 (no Agent Framework) | PASS — no `Microsoft.Agents.*` references. |

## Main-session follow-ups

1. **Remove SprkChatAgentFactory block** at lines 754–785 (`// --- DocumentSearchTools ---`). Replace with a comment pointing to the data-driven block + this note (mirroring the pattern used for `KnowledgeRetrievalTools` at lines 796–806).
2. **Add to Seed-TypedHandlers.ps1 `$RowFiles`** — add `sprk_analysistool-document-search-row.json` + `sprk_analysistool-document-discovery-row.json` after the existing Wave 7c entries.
3. **Optional: delete `DocumentSearchTools.cs`** — the class has no non-LLM consumers (verify via `Grep`). Safe to remove after seed rows deploy + chat sessions verify in dev. Leaving it for now keeps the rollback path simple.
