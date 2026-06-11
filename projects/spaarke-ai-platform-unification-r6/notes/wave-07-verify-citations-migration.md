# Wave 7 — VerifyCitationsTool migration — STOP-AND-SURFACE

**Status**: STOPPED before implementation per the "ADRs are defaults — challenge when warranted" binding principle (projects/spaarke-ai-platform-unification-r6/CLAUDE.md §97-138) and the agent prompt's explicit stop-and-report trigger: *"The capability gate (PlaybookCapabilities.VerifyCitations) removal would change the security model."*

**Date**: 2026-06-08
**Agent**: Wave 7 sub-agent (VerifyCitationsTool migration)

## TL;DR

VerifyCitations is the cleanest of the 4 Wave 7 trivial tools — single dependency (`ICitationVerificationService`), single LLM function, no citations accumulator, no SSE, no KnowledgeScope. The migration itself is ~1 hour of code. BUT:

The hardcoded registration is gated behind `capabilities.Contains(PlaybookCapabilities.VerifyCitations)` (option 100000009 in Dataverse), and the **data-driven path at FR-11 has NO per-playbook capability filter** — it exposes every chat-available `sprk_analysistool` row to every playbook. Migrating naively would silently expose `verify_citations` to playbooks that today don't get it. This is a security/governance regression masquerading as a refactor.

The same gap exists for `LegalResearch`, `CodeInterpreter`, `WebSearch`, `WriteBack`, `Reanalyze`, and others currently gated by `PlaybookCapabilities`. Surfacing now — before this and the 3 sibling Wave 7 migrations land — keeps the answer one design, not five rework PRs.

**No code changes made.** All analysis below is READ-ONLY.

---

## The two triggers, verified against current source

### Trigger A — `PlaybookCapabilities.VerifyCitations` is a binding security gate

`src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs` defines `VerifyCitations = "verify_citations"` (Dataverse option set integer 100000009). Per the doc-comment (lines 64-72):

> Gated so only playbooks that deal with legal documents include this tool in the LLM's tool schema. The automatic post-LLM citation check (`CitationSafetyCheck`) runs unconditionally regardless of this capability.

The capability is NOT in `CoreCapabilities` (the standalone-chat allow-list, lines 126-133). Today, in a standalone chat session OR a playbook that omits `verify_citations`, the LLM cannot invoke `verify_citations`. This is the intended security boundary.

The hardcoded registration at `SprkChatAgentFactory.cs` lines 1223-1266 enforces this: `if (capabilities.Contains(PlaybookCapabilities.VerifyCitations))`.

### Trigger B — Data-driven FR-11 path has NO capability filter

`SprkChatAgentFactory.cs` lines 1267-1464 (the FR-11 data-driven block) reads `sprk_analysistool` rows where `AvailableInContexts ∈ {Chat, Both}` and registers them ALL via `ToolHandlerToAIFunctionAdapter`. The filter is purely:

- `isChatAvailable = availability == Chat || availability == Both`
- Plus dedup by name

There is NO check against the current playbook's capability list. A row with `sprk_handlerclass = VerifyCitationsHandler` and `sprk_availableincontexts = 100000002` (Both) would be registered for every chat session, regardless of which playbook is active.

Search confirms zero references from FR-11 path to `PlaybookCapabilities` or to `capabilities` (the local variable). The data-driven path is currently capability-blind.

---

## Why this is a Wave-wide question, not a VerifyCitations-only question

Five of the 10 chat tools targeted by Q9 are currently gated by capability:

| Tool | Capability gate | Today's hardcoded check | Risk if migrated naively |
|---|---|---|---|
| `verify_citations` | `VerifyCitations` | Lines 1235 | Exposed to all playbooks (legal review boundary lost) |
| `legal_research` | `LegalResearch` | (sibling Wave 8) | Bing-grounded research available to non-legal playbooks |
| `code_interpreter` | `CodeInterpreter` | (sibling Wave 8) | Sandbox code execution exposed broadly |
| `web_search` | `WebSearch` | (sibling Wave 8) | External web search bypasses scope-confinement |
| `write_back` / `reanalyze` | `WriteBack` / `Reanalyze` | (sibling Wave 9) | Working-document mutation broadly exposed |

This is NOT a VerifyCitations-only concern. It's the **per-playbook tool-availability filter** that the data-driven path doesn't yet have. Solving it here, before VerifyCitations + LegalResearch + CodeInterpreter + WebSearch all land naively, keeps the answer ONE migration, not five rework PRs.

---

## What spec.md / plan.md actually say about this

- **Pillar 2 / FR-11**: "Query `sprk_analysistool` rows for chat-available tools and register via `ToolHandlerToAIFunctionAdapter`." No capability filter mentioned.
- **Pillar 3 / FR-XX (invoke_playbook)**: tool list driven per-playbook, but the *generic invoke_playbook* tool is the routing primitive, not a filter on the underlying tool registry.
- **Pillar 8 (CapabilityRouter)**: the router restricts WHAT the LLM is allowed to do per turn based on capability, NOT what TOOL OBJECTS are in the function-calling schema. The current capability-gate in `SprkChatAgentFactory` is at registration time (tool absent from schema → LLM can't even ATTEMPT the call); CapabilityRouter is per-message.
- **Q11 + plan.md §2**: "Removed: InvokeSummarizePlaybookTool + InvokeInsightsQueryTool C# files; callers route through generic tool." No mention of capability-aware filtering.

The spec **silently assumes** the per-playbook filter exists or will exist. It does not specify where. This is a SPEC GAP, not a violation.

---

## Three viable paths

### Path A — Add per-playbook capability filter to FR-11 path (RECOMMENDED)

**Change set**:
- Extend `sprk_analysistool` row with optional `sprk_requiredcapability` field (text, references a value in `PlaybookCapabilities` constants).
- In FR-11 path: when filtering chat-available rows, also check `string.IsNullOrEmpty(row.RequiredCapability) || capabilities.Contains(row.RequiredCapability)`.
- Seed `verify_citations` row with `sprk_requiredcapability = "verify_citations"`; `legal_research` with `"legal_research"`; etc.
- Standalone chat (no playbook): capabilities falls back to `CoreCapabilities` (already happens) — rows with `RequiredCapability` outside `CoreCapabilities` are skipped, matching today's behavior exactly.

**Pros**:
- Preserves today's security model exactly. Verify, LegalResearch, CodeInterpreter, WebSearch, WriteBack, Reanalyze all stay gated as today.
- Single small change to FR-11 path + one Dataverse column add + per-row population.
- Zero new ADRs. No new feature flags. No new public-contract surface.
- The pattern landed here is the template Wave 8 + Wave 9 reuse for the 4 sibling capability-gated tools.

**Cons**:
- One additional Dataverse column on `sprk_analysistool` (low cost; the entity already has 8+ fields).
- ~30 LOC change to FR-11 path + matching change to `AnalysisTool` DTO + `AnalysisToolService.ListToolsAsync` projection.

**Cost**: ~3 hours including tests. **No new ADR needed.**

**Wave 7/8/9 impact**: Template ready. Each remaining capability-gated tool reuses the field.

### Path B — Surface capability gate via `JsonSchema` / configuration in the row

**Change set**:
- Encode the required capability inside `sprk_configuration` JSON (e.g., `{ "requiredCapability": "verify_citations" }`).
- FR-11 path reads + filters.

**Pros**: No new Dataverse column.
**Cons**: Configuration is opaque to admins browsing rows in the maker UI. Capability gate is a SECURITY concern; hiding it in free-form config is anti-discoverable. Schema-level concern shouldn't live in handler-specific config.

### Path C — Migrate VerifyCitations + siblings WITHOUT capability gate; document as accepted scope expansion

**Change set**: None to the gate; expose verify_citations + the other 4 capability-gated tools to all playbooks.

**Pros**: Smallest diff.
**Cons**: **Silent security model change.** The doc-comment on `PlaybookCapabilities.VerifyCitations` explicitly says "Gated so only playbooks that deal with legal documents include this tool." Removing the gate without sign-off violates the principle the gate enforces.

Per the binding principle (CLAUDE.md §97-138): *"Silently working around a security gate to avoid touching the architecture surface" is the explicit anti-pattern.* Path C IS that anti-pattern.

---

## Recommendation — Path A

Path A is the optimal technical answer. The gap (FR-11 capability filter) is a SPEC GAP, not a violation of any ADR. Adding `sprk_requiredcapability` to `sprk_analysistool` is a small data-model extension within existing entity boundaries — ADR-027 (sprk_ prefix) and ADR-013 (Services/Ai facade boundary) both unaffected. ADR-015 telemetry unaffected (the field is a deterministic identifier, not user content). ADR-029 publish size unaffected.

The pattern landed in Path A solves the same gate question for LegalResearch (Wave 8), CodeInterpreter (Wave 8), WebSearch (Wave 8), WriteBack (Wave 9), Reanalyze (Wave 9). It's NOT a one-off.

---

## What this agent has NOT done

To keep the work reversible and the decision in the user's hands:
- NO code changes to `Sprk.Bff.Api`
- NO `VerifyCitationsHandler.cs` written
- NO row-JSON files created (the seed-row design depends on whether `sprk_requiredcapability` is added)
- NO edits to `Seed-TypedHandlers.ps1`
- NO edits to `SprkChatAgentFactory.cs`
- NO test files written

All findings above are READ-ONLY analysis.

---

## Capability-gate decision required from user

Pick A, B, or C. Path A is the recommended pattern-validator template for the remaining 4 capability-gated Wave 7/8/9 tools.

If Path A is approved:
1. Define the Dataverse column add (`sprk_requiredcapability` text, optional).
2. Decide whether the FR-11 filter change lives in the VerifyCitations PR (this Wave 7 task) or as a separate infrastructure task (analogous to the KnowledgeRetrieval Wave 7 STOP-AND-SURFACE Path A precedent).
3. Coordinate with sibling Wave 7 (KnowledgeRetrieval also halted; AnalysisQuery + TextRefinement are no-capability-gate tools and could proceed independently) and Wave 8 / Wave 9 prompts to apply the same pattern.

If Path B or C is approved: this agent will resume with explicit acknowledgement of the security-model trade-off.

---

## Implementation outline IF Path A is approved (for reference, not executed)

Once Path A is chosen, the VerifyCitations migration is straightforward:

- **Handler**: `Services/Ai/Handlers/VerifyCitationsHandler.cs` — `IToolHandler`, `SupportedInvocationContexts = Both`, resolves `ICitationVerificationService` via DI scope. `ExecuteChatAsync` parses `{ text }` from `ToolArgumentsJson`, calls `_verificationService.VerifyAllAsync(text, ct)`, returns structured `ToolResult` with `data = { citations: [{ raw, type, normalized, isVerified, confidence, sourceUrl, provider }] }`. `ExecuteAsync` reads from `context.Document.ExtractedText`. ADR-015: log counts + duration; NEVER citation text.
- **Row**: `infra/dataverse/sprk_analysistool-citation-verify-row.json` → `sprk_toolcode = "CITATION-VERIFY"`, `sprk_name = "SYS-Citation Verification"`, `sprk_handlerclass = "VerifyCitationsHandler"`, `sprk_availableincontexts = 100000002` (Both), `sprk_requiredcapability = "verify_citations"` (NEW field per Path A).
- **JsonSchema**: single `{ text: string (minLength 1) }` argument.
- **Configuration**: none expected (no knobs).
- **Seed script**: add `"VerifyCitationsHandler" = "$RepoRoot/infra/dataverse/sprk_analysistool-citation-verify-row.json"` to `$RowFiles`.
- **Factory removal**: delete lines 1223-1266 (the `--- VerifyCitationsTool ---` block).
- **Tests**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/VerifyCitationsHandlerTests.cs` covering the 4 contract tests + happy path (mock `ICitationVerificationService` returning verified/unverified/error mix) + empty text rejection + tenantId enforcement + ADR-015 telemetry sentinel scan.
- **NFR-13 compliance**: `CitationSafetyCheck` middleware runs unconditionally post-LLM. This handler is the EXPLICIT-verification path. Middleware unchanged.

Estimated total: ~1 hour of code + ~30 min of tests + ~15 min of seed/deploy/verify, assuming Path A capability filter is already in place.

---

## Files referenced (read-only)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/VerifyCitationsTool.cs` (lines 1-192)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/ICitationVerificationService.cs` (lines 1-44)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/Citation.cs` (lines 1-25)
- `src/server/api/Sprk.Bff.Api/Services/Ai/IToolHandler.cs` (lines 1-225)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/HandlerRegistrationConventions.md` (lines 1-247)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/RiskDetectorHandler.cs` (sibling safety-relevant deterministic-scoring template)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` (lines 1223-1466 — hardcoded reg + FR-11 path)
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs` (lines 1-135)
- `src/server/api/Sprk.Bff.Api/Services/Ai/ChatInvocationContext.cs` (lines 1-93)
- `scripts/Seed-TypedHandlers.ps1` (lines 80-95 — `$RowFiles` map)
- `infra/dataverse/sprk_analysistool-risk-detector-row.json` (sibling row template)
- `projects/spaarke-ai-platform-unification-r6/notes/wave-07-knowledge-retrieval-migration.md` (sibling STOP-AND-SURFACE precedent — Path A pattern)

## Awaiting decision

User to decide Path A / B / C for the per-playbook capability filter on the data-driven FR-11 tool-resolution path.
