# Research Findings Summary

> **Status**: Consolidated key findings from 4 research agents + 5 audit agents (2026-06-19 → 2026-06-20)
> **Authority**: This summary captures the substance integrated into `../design.md`. Full agent transcripts are preserved in conversation history at `C:\Users\RalphSchroeder\.claude\projects\c--code-files-spaarke-wt-spaarke-ai-platform-unification-r6\055c4110-978d-4fd6-bf67-53d396e40f4d\tasks\*.output` if deep dive is ever needed.
> **Purpose**: Traceability for design.md claims. Future contributors can verify recommendations against the source research without re-running the agents.

---

## A1 — File-Aware Classification Research (WP2)

**Investigated**: Best practices for fast doc-type classification in legal contexts; multi-file reconciliation patterns; Azure Document Intelligence vs embedding-only vs small-LLM tradeoffs.

### Key findings
- **Filename TF-IDF is shockingly effective** for legal naming conventions: 96.57% accuracy on real datasets, 442× faster than DiT (per Koay et al. 2024, arxiv 2410.01166). NOT used as standalone classifier but as a feature in embedding query.
- **N=2000 chars** is the sweet spot for "first N chars" embedding: legal contracts put discriminative content (title, parties, type, recitals) on the first page; boilerplate is at the end. Embedding latency is FLAT up to 8K tokens (provider batches dominate), so N=2000 has no latency cost.
- **Azure Document Intelligence is the WRONG tool** for Stage 1 routing: prebuilt "contract" model is for EXTRACTION not classification; custom classifier needs binary not text (Spaarke today sends only extracted text); 2-4s latency per page blows the 2s budget.
- **Stay on text-embedding-3-large**: voyage-law-2 beats it 6-10% on legal benchmarks but requires Azure-external API + new ADR. Doc-type bucketing is much easier than case-law retrieval; the margin compresses.
- **Multi-file**: NO competitive product (Cursor, Harvey, Hebbia, Glean) does deterministic per-file fanout — they either treat all files as one RAG context, decompose user-side, or ask.

### Recommendation (per-file Phase A/B/C)
- **Phase A** (in-memory fingerprint): filename tokens + content type + first 2000 chars + sha256 hash. <5ms per file.
- **Phase B** (per-file vector match): `userMessage + filename + first 2000 chars` as composed query → embed → search `playbook-embeddings`. ~150-200ms per file (parallel).
- **Phase C** (reconciliation): single-file path / all-agree path / decider LLM only on real disagreement.
- **Total budget**: 600ms (no files) → 1225ms (5 files disagreeing). Fits in existing 2s `PlaybookDispatcher.TotalTimeout`.

### Integrated into design.md
WP2 (3 multi-file options A/B/C); §1.5 (file classification informs routing); WP5.5 (upload-time classification).

---

## A2 — Destination Metadata Research (WP3)

**Investigated**: Where destination metadata should live (action / node-config / playbook-row / hybrid); how `PlaybookOutputHandler` should consume it.

### Key findings
- **Schema is already correct**: `NodeRoutingConfig` at `Models/Ai/NodeRoutingConfig.cs:30-274` implements the Q5 Re-Shaped design (per-node JSON property in `sprk_configjson`, NOT a column on action).
- **`NodeRoutingConfig.Parse(null)` defaults to `Chat`** → backward compat is structural; existing playbooks with no configJson continue working.
- **`summarize-document-for-workspace@v1` is the REFERENCE IMPL** of "one action, two destinations" (not broken as initially feared).
- **Real gap**: `PlaybookOutputHandler.HandleOutputAsync` has cases for Text/Dialog/Navigation/Download/Insert but **NO Workspace case**. Workspace tab opens today via a DIFFERENT path: `sseToPaneEventBridge` reads `AnalysisChunk` deltas → `workspace.field_delta` events → `StructuredOutputStreamWidget`. Bypasses handler entirely.
- **Missing `Both` enum value**: FR-27 promised 4 destinations (chat/workspace/both/side-effect); code has 3.
- **`sprk_outputformat` on action is taken** by `DeliverOutputNodeExecutor` for text-formatting (max length, include metadata) — NOT destination. Cannot repurpose.

### Recommendation (6 additive changes)
1. Add `NodeDestination.Both` enum value + converter
2. Wire `NodeRoutingConfig` into `DispatchResult` (2 new optional params with defaults)
3. Populate `DispatchResult.NodeDestination` in `PlaybookDispatcher` (load primary node, parse configJson)
4. Add Workspace/Both/FormPrefill/SideEffect cases to `PlaybookOutputHandler.HandleOutputAsync`
5. Templated ack for `Both` ("I've added a [playbookName] result to the Workspace.")
6. JSON-schema validation gate in `Deploy-Playbook.ps1`

**No Dataverse schema change. No new ADR.**

### Integrated into design.md
WP3 (entire); §1.5 (chat/workspace siblings are reference impl, not redundant).

---

## A3 — Stateful Chat Research (WP5)

**Investigated**: How 8+ production AI products handle stateful chat memory (Cursor, GitHub Copilot, Claude Artifacts, Linear AI, Glean, Hebbia, Harvey, OpenAI Assistants).

### Key findings
- **Industry has converged on JIT (just-in-time) retrieval**: Anthropic formalized Sept 2025; every product investigated uses identifiers + tools, NOT prompt stuffing.
- **Closest reference for Spaarke is Harvey's drafting agent**: in-memory doc representation with `edit(section, new_text)` + `get_diff` tools. Edits are deterministic; LLM doesn't regenerate full doc each step.
- **Static prefix (cached) + dynamic suffix (per-turn)** = Azure OpenAI prompt cache: 50% discount + 80% faster TTFT.
- **Why NOT stuff file text into every prompt**:
  - Blows 8K NFR-10 budget at >2-3 small docs
  - "Lost in the middle" attention degradation (Stanford 2024 TACL: GPT-4o 128K has ~8K *effective* tokens)
  - Defeats prompt caching (requires stable prefix)
  - Costs 4-10x per turn at scale
- **`recall_session_file` tool design**: 3 modes (`summary`/`section`/`full`); `requireCitations: true` default; backed by `spaarke-session-files` AI Search index for section mode.

### Recommendation
- Inject identifiers + 1-line summaries always; full content via tools on demand
- Per-turn prompt: ~6K static prefix + ~5K dynamic suffix = ~11K total
- Precompute file summaries at upload (one gpt-4o-mini call, ~$0.0001/file)
- "Recently discussed" flag on files (last 3 turns)
- Adopt Harvey/Artifacts targeted-edit pattern for workspace-write tools
- **Net per-turn cost DECREASES** vs current (cache discount + recall only when needed)

### USER FEEDBACK: 6-tier memory model
User pushed back on "1-line summaries every turn is too thin for Spaarke" and "memory must be modeled as 6 tiers, not one blob":
- **T1 Working context** — composed each turn (existing `MemoryCompositionService`)
- **T2 Session memory** — uploaded files, generated summaries, user decisions (existing `ChatSession` + Cosmos `sessions`)
- **T3 Matter memory** — durable matter-level facts (existing `MatterMemoryService`)
- **T4 User/org memory** — preferences, templates, pinned playbooks (existing `sprk_userpreferences` + `PinnedContextItem`)
- **T5 Retrieval memory** — AI Search indexes (multiple existing)
- **T6 Audit memory** — telemetry (existing `AuditLogService` + `context.*` events)

Plus **layered context cards** (not 1-line) + **expanded tool surface** (`recall_session_file`, `list_session_files`, `get_file_manifest`, `retrieve_matter_memory`, `write_session_memory`, `promote_to_matter_memory`, `get_user_preferences`, `get_org_templates`).

### Integrated into design.md
WP5 (entire — 6-tier model, layered cards, expanded tool surface).

---

## A4 — Playbook Composition Research (WP6)

**Investigated**: How playbooks compose Action + Skills + Knowledge + Tools scopes; inventory of existing playbooks; sketch for specialized Summarize-NDA.

### Key findings
- **Legacy `scopes.{actions|skills|knowledge|tools}` arrays are DEPRECATED**: consumed only by `AnalysisOrchestrationService.cs:943-1033` with explicit deprecation warning.
- **Modern composition is JPS `$ref` inside the action's prompt**: `"scopes": { "skills": [{ "$ref": "skill:NDA Review" }], "knowledge": [{ "$ref": "knowledge:NDA Standards" }] }`. Resolved by `JpsRefResolver` (`AiAnalysisNodeExecutor.cs:442-499`) → `IScopeResolverService.GetSkillByNameAsync` + `GetKnowledgeByNameAsync` → appended to system prompt.
- **3 execution paths exist** (potentially confusing):
  - **Path 1 — Node-based**: canonical for new playbooks (`PlaybookOrchestrationService` → `AiAnalysisNodeExecutor`); supports JPS `$ref`
  - **Path 2 — Legacy scope arrays**: DEPRECATED; only `AnalysisOrchestrationService`
  - **Path 3 — Chat-Summarize bypass**: `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` does NOT call `JpsRefResolver` today; chat-summarize playbooks can't use `$ref` composition
- **Insights playbooks are best exemplar**: `matter-health-single.playbook.json` is 9-node DAG with evidence-sufficiency gate, citation grounding, persistence
- **Existing primitives ready for specialized playbooks**:
  - SKL-003 "NDA Review" + KNW-006 "NDA Standards" (`nda-standards-index`) + KNW-005 "Defined Terms" → all in seed-data
  - 25 handler classes in `Services/Ai/Handlers/` available as Tool scope candidates

### Recommendation
- **Don't add `scopes.{...}` arrays** to chat-Summarize playbooks (silently no-op)
- **Author NEW specialized actions** with JPS `$ref` to existing Skills + Knowledge
- **Extend Path 3** to invoke `JpsRefResolver` before LLM call (preserves streaming UX)
- **Specialized playbooks to author**: summarize-nda (uses SKL-003 + KNW-006), summarize-patent, extract-invoice

### Integrated into design.md
WP6 (entire); §1.7 (stable codes for new playbooks).

---

## Audit 1 — R6 Pillar 1/4/7/8 Status

**Investigated**: Implementation status of Pillars 1 (Persona), 4 (Chat /summarize FK fix), 7 (Memory + Q7 UI), 8 (Command Router).

### Key findings
- **All 4 pillars materially complete in code**
- Pillar 1: data-driven `sprk_aipersona` resolver wired in `PlaybookChatContextProvider.cs:130-154`; default SYS- seeded (GUID `4fe49430-...`)
- Pillar 4: alternate-key bypass removed (0 references); orchestrator is thin pass-through
- Pillar 7: ALL 7 memory services + Pinned Memory CRUD UI shipped (most complete pillar)
- Pillar 8: 6 hard + 4 soft slashes + 3 reference types + composition tests green

### Technical debt identified (lift paths documented)
- `SessionSummarizeOrchestrator.ChatSummarizePlaybookId` GUID `44285d15-...` (line 78-79) → lift to `AnalysisOptions.ChatSummarizePlaybookId` via `IOptions<T>`
- `PlaybookChatContextProvider.BuildDefaultSystemPrompt` defense-in-depth fallback → delete after stabilization window
- `SummarizeInvocationPath.AgentTool` discriminator → fold (only consumer is telemetry)
- `PinnedMemoryProvenanceBadge` stub provenance → wait on `PinDto.source`

### Bottom line
R6 closeout for these 4 pillars = governance only (tasks 089 + 090).

---

## Audit 2 + 3 — Pre-Fill Consumer Audit (Initial + Complete)

**Investigated**: Which production code paths depend on existing summary-shaped playbooks; what's safe to deprecate / merge / modify.

### Key findings
- **`summarize-document-for-workspace@v1` is NOT broken** — it's the reference impl of Q5 Re-Shaped pattern. Pairs with chat sibling for R6 FR-30 dedup invariant.
- **6 production-bound playbooks** with hard consumers (modify-with-migration, don't delete):
  1. `summarize-document-for-chat@v1` (`44285d15-...`) — chat /summarize endpoint + agent-tool dispatch + StructuredOutputStreamWidget
  2. `summarize-document-for-workspace@v1` — soft-slash routing via CapabilityRouter
  3. `"Summarize New File(s)"` (`4a72f99c-...`) — `WorkspaceFileEndpoints` + Matter ribbon + 4 wizards (SummarizeFilesWizard, DocumentEmailWizard, DocumentRelationshipViewer, GetStarted card)
  4. `"Document Profile"` (`18cf3cc8-...`) — `AppOnlyAnalysisService` + global chat fallback in `chat-context-mappings.json` + EVERY uploaded document's profile pipeline
  5. `"Create New Matter Pre-Fill"` (`2d660cad-...`) — NFR-07 binding
  6. `"Create New Project Pre-Fill"` (`fc343e9c-...`) — NFR-07 binding (recently HOTFIXED 2026-06-09)
- **3 resolution patterns** in production:
  - **Pattern A** — Hardcoded GUID + config override (5 services)
  - **Pattern B** — Resolve-by-NAME at call site (`useAiSummary`, `DocumentEmailWizard`, `AppOnlyAnalysisService`)
  - **Pattern C** — Hardcoded NAME (`AppOnlyAnalysisService.DefaultPlaybookName`)
- **Hidden hazards**:
  - Name collision: "Document Profile" vs "Document Summary" (PB-012) — literal-string lookup silently shadows
  - Name collision: "Summarize File" (PB-015) vs "Summarize New File(s)" (production)
  - **CreateWorkAssignmentWizard silently shares Matter pre-fill endpoint** — no separate playbook, hidden coupling
  - **DocumentEmailWizard resolves same playbook as SummarizeFiles via name (not GUID)** — dual-identification risk
  - **`Workspace:SummarizePlaybookId` is NOT in WorkspaceOptions** — ADR-018 violation; raw `IConfiguration` indexer
  - **LegalWorkspace dead-code wizards** still reference live pre-fill endpoints post-OC-R4-05 retirement

### Recommendation (3-pattern migration plan)
- **Pattern A**: typed-options + stable code (~4-file C# change)
- **Pattern B**: replace literal name strings with codes via `/by-code/{code}` endpoint
- **Pattern C cleanup**: delete LegalWorkspace dead code + PCF copy + fix stale GUID comments

### Integrated into design.md
§1.5 (production-bound playbooks); §1.7 (stable codes + 3-pattern migration plan).

---

## Audit 4 — R6 Pillars 2/3/5/6/9 Status

**Investigated**: Implementation status of remaining R6 pillars.

### Key findings
- **All 5 pillars materially complete in code**
- Pillar 2: 18 tasks done; tool registry + Q9 big-bang migration + 8 typed handlers all shipped
- Pillar 3: `IInvokePlaybookAi` facade + Null peer + symmetric DI; specialized bridges DELETED (verified)
- Pillar 5: 11 tasks done; Phase B exit signed; `NodeRoutingConfig` correct
- Pillar 6: 14 tasks done; Phase C exit signed 2026-06-18; ExecutionTraceWidget shipped
- Pillar 9: 4 tasks done; server-side `TryDeriveVisibleState` privacy defense
- **Combined with Audit 1: all 9 R6 pillars materially shipped**

### Expanded R6 closeout punch list (not just 089+090)
1. Tasks 089 + 090 (governance, ~8 hrs)
2. **3 UAT hotfixes need to land on master** (PR #401 stale, contains only #1)
3. **HIGH-severity bug: `/summarize` slash produces chat-only output** — root cause in CapabilityRouter Layer 0.5; either fix or explicitly defer to successor
4. **UAT Tiers B/C/D/E/F/G not executed** — only Tier A done
5. Verify Phase D component-task integration completed via hotfix #1

### Hidden risks
1. **Pillar 5 dedup is what makes the bug invisible to single-fire tests** — FR-30 contract holds, just to wrong destination. Routing-layer problem, not Pillar 5 defect.
2. **Tool-name normalization boundary brittleness** — 3 layers had different "tool name" conceptions (sanitized adapter / raw filter / Layer 3 capability-names). Cascading hotfix proves it.
3. **Pillar 9 server-side closed-union switch** — new widget types in R7+ won't appear in agent prompts until C# union is updated. R7 backlog.
4. **No single end-to-end test exercises all 9 pillars** — task 087 is composed-evidence framing.

### Integrated into design.md
§1.6 (R6 status — all 9 pillars + honest closeout punch list).

---

## Audit 5 — Insights Engine + R6 CosmosDB + sprk_aichatmessage Cross-Check

**Investigated**: What existing infrastructure leverageable for WP5 memory architecture; what's the state of CosmosDB usage; what's `sprk_aichatmessage` actually doing.

### Key findings — most WP5 infrastructure already exists
- **5 Cosmos containers provisioned via Bicep**: `sessions`, `prompts`, `audit`, `memory`, `feedback` — all serverless, `/tenantId` PK, managed identity via `DefaultAzureCredential`
- **R6 reused `memory` container** with doc-type discriminators — no new containers added
- **Pillar 7 services all built + DI-registered**: `MatterMemoryService`, `PinnedContextRepository`, `PinnedContextRecallService`, `MemoryCompositionService`, `SummarizationCompressionService`, `PromptBudgetTracker`
- **Insights Engine ready for T5 retrieval**: `spaarke-insights-index` with vector retrieval, tenant + artifactType + scope filtering; `MultiIndexComposer` already merges multi-tier knowledge blocks; envelope pattern reusable
- **`sprk_aichatmessage` is a BROKEN PLACEHOLDER**:
  - 5 `ChatDataverseRepository` methods are `Task.CompletedTask` no-ops
  - `GetMessagesAsync` always returns empty array
  - Explicit comment: "test coverage is against the mock, not this implementation"
  - 10K char hard cap on content
  - Listed as "transient chat" in migration exclude
  - **Cosmos `sessions` is authoritative for chat history**, not Dataverse
- **No `sprk_matterfacts` entity** — `MatterMemoryService` covers matter facts via Cosmos `memory` (doc id `{tenantId}_{matterId}`)
- **`sprk_matter.sprk_performancesummary`** is Insights-owned (holds 7-dim health diagnostic envelope) — DO NOT repurpose

### USER DECISION: retire `sprk_aichatmessage` placeholder
- Rename `IChatDataverseRepository` to `IChatAuditRepository` (write-only contract)
- Confirm Cosmos `audit` container is sole reader for compliance queries
- `sprk_aichatmessage` becomes pure audit-write target

### USER DECISION: MatterMemoryService FR-45 wiring VERIFIED 2026-06-20
- Called at `PlaybookChatContextProvider.cs:627` inside `AppendMatterMemoryAsync`
- Invoked from generic-chat path (line 173) AND playbook-bound path (line 282)
- Triggers when `hostContext.EntityType=="matter"` + valid `EntityId` + `tenantId`
- **Wiring is in place**; Insights audit's UNCLEAR flag was over-cautious

### WP5 reframe
- WP5 is **mostly WIRE + REFACTOR, not BUILD**
- Build scope: layered context cards (T1 refactor) + tool surface expansion (new tools wrapping existing storage) + upload-time enrichment (classify + summarize + manifest) + `sprk_aichatmessage` retirement + recently-discussed signal
- 6-tier model stays as architecture; the build is wrapper tools over existing infrastructure

### Integrated into design.md
WP5 (entire reframe); WP5.1a (leverage map); WP5.1b (architecture diagram); §1.5 (sprk_aichatmessage decision).

---

## Decisions captured during research

| Decision | Source | Outcome |
|---|---|---|
| 6-tier memory model | A3 user feedback | WP5 redesigned around tiers |
| Layered context cards (not 1-line summaries) | A3 user feedback | WP5.2 |
| Stable playbook codes (no @v1) | Audit 2/3 user feedback | §1.7 |
| JPS matching metadata field | A3 + A4 user feedback | WP1.5 |
| 3 multi-file matching options (A/B/C) | A1 + user multi-file question | WP2; Option C recommended primary |
| Modify-not-delete production playbooks | Audit 2/3 user feedback | §1.5 reframed |
| Retire `sprk_aichatmessage` | Audit 5 user decision | WP5.1d + §1.5 |
| Verify FR-45 wiring | Audit 5 user decision | VERIFIED ✅ wired |
| Defer `/summarize` bug to successor | Audit 4 + user decision | First issue in successor project |
| UAT R6 before close | Audit 4 + user decision | Tiers B-G execution required |
| New project name `spaarke-ai-platform-chat-routing-redesign-r1` | User v2 feedback | Worktree + folder created per skill convention |
