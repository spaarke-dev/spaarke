# Chat Routing + Stateful Chat Architecture Design

> **Status**: DRAFT v3 — incorporates R6 Pillar 1/4/7/8 status audit + pre-fill consumer audit findings. Major WP6 correction.
> **Date**: 2026-06-19
> **Location**: This file is the canonical design doc at `projects/spaarke-ai-platform-chat-routing-redesign-r1/design.md` in the new worktree (`spaarke-wt-chat-routing-redesign-r1`). The original copy in R6 notes is superseded.
> **Author**: Main session (Claude Opus 4.7) with research synthesis from A1-A4 agents + 2 audit agents + user review v1 + v2
> **Trigger**: User UAT observations exposed multiple intersecting architectural gaps in chat → playbook routing. Surfaced 6 work packages requiring deep design analysis before implementation.
> **Process**: Parallel research on highest-uncertainty WPs (WP2/3/5/6) → main session synthesis → user review v1 → 2 audits (R6 pillar status + pre-fill consumers) → main session correction → user review v2.
> **v2 changes**: Expanded WP5 from "JIT retrieval" to a 6-tier layered memory model per user feedback. Added playbook-embeddings index governance (metadata audit + send-to-index process) to WP1. Incorporated user Q&A resolutions into §5.
> **v3 changes**:
> - MAJOR CORRECTION to WP6 — `summarize-document-for-workspace` is NOT broken; it's the reference impl of Q5 Re-Shaped pattern (one action, two destinations).
> - Added §1.5 (now reframed in v3.1).
> - WP6 reframed as ADDITIVE for playbooks.
> - Added §1.6 "R6 Status (after audit)" — only governance items 089+090 remain; 4 audited pillars complete.
> - Updated §5 with audit-grounded resolutions; flagged Dataverse-audit-required items.
> - Dropped phased-delivery framing for WP5.
> **v3.1 changes (THIS REVISION — user review v2)**:
> - **§1.5 reframed**: "Untouchable" → "Production-Bound — Migration Path Required". Replace via migration, don't delete. Modify in place where consumers can be re-pointed.
> - **NEW §1.7 "Playbook Identification Reform"** — stable `sprk_playbookcode` field; immutable codes; NO `@v1` suffix on new playbooks; `/by-code/{code}` resolution endpoint; migration plan for the 3 current resolution patterns (GUID-hardcoded / name-hardcoded / by-name-lookup).
> - **WP1.5 enhanced**: add `sprk_jpsmatchingmetadata` JPS-formatted field on playbook for structured matching (documentTypes, intents, triggerPhrases, scopeHints) — improves vector + LLM match signal-to-noise.
> - **WP2 multi-file resolution**: 3 options added (A1's parallel-per-file / user's combined-summary / hybrid using precomputed manifests).
> - **R6 review expanded**: Pillars 2/3/5/6/9 also need audit before R6 close (only 1/4/7/8 audited so far).
> - **WP6 naming convention**: drop `@v1` suffix; use kebab-case codes (e.g., `summarize-nda` not `summarize-nda@v1`).
> - **Three new audits dispatched**: complete pre-fill consumer inventory; Pillars 2/3/5/6/9 status; Insights Engine + CosmosDB R6 status cross-check.
> - **WP5 architecture diagram**: deferred pending Insights Engine cross-check (avoid duplication of leverageable code).

---

## §1 Architectural North Star

The R6 code base today has **two parallel playbook-routing mechanisms** (`PlaybookDispatcher` + `CapabilityRouter`), **three parallel playbook-execution code paths** (node-based, legacy-deprecated scopes-array, and a chat-summarize bypass), **inconsistent destination metadata** (mostly hardcoded in streaming code), and a **stateless chat** that prompts users to re-upload documents they already uploaded. The chat-routing path is functional but fragile, and the visible bugs (e.g., `/summarize` produces chat-only output while NL "summarize" opens a Workspace tab) are downstream consequences of the structural duplication, not isolated defects.

The target architecture is:

**1. One playbook-selection layer.** Collapse `PlaybookDispatcher` + `CapabilityRouter` onto a single matcher driven by the existing `playbook-embeddings` AI Search index. The `sprk_aicapability` table — never created, blocking the second mechanism — is **not built**.

**2. File-aware classification.** The matcher embeds `(user message + filename + first ~2000 chars of file)` per file in parallel. For multi-file scenarios (patent + invoice), per-file matching plus a `gpt-4o-mini` decider on disagreement. Industry-converged best practice: filename TF-IDF is ~96% accurate as a fast signal, first ~2000 chars captures the discriminative content of a legal doc, embedding latency is flat with input length up to 8K tokens, multi-file disagreement is rare enough to absorb a 300ms LLM decider call.

**3. Data-driven destinations.** Use the existing `NodeRoutingConfig` schema (already correct per the Q5 re-shape) — destination + widgetType live on the playbook node's `sprk_configjson`. Add the missing `Both` enum value. Wire `NodeRoutingConfig.Destination` into `DispatchResult` and add Workspace/Both/FormPrefill/SideEffect cases to `PlaybookOutputHandler`. The destination is read at dispatch time; the streaming code stops being the implicit decider.

**4. Stateful chat via layered memory + JIT retrieval.** The 2025-2026 industry consensus (Anthropic, Cursor, GitHub Copilot, Harvey, Glean, Hebbia, Linear, Claude Artifacts) is unanimous on JIT retrieval. But "memory" must be modeled as **6 distinct tiers**, not one prompt blob: Working / Session / Matter / User-Org / Retrieval / Audit. Each tier has its own lifecycle, storage, and access pattern. The system prompt carries a **layered context card per file** (not a 1-line summary) — richer structured data the LLM can read directly, with explicit "not authoritative; verify with citations" trust framing for any precomputed summary. The tool surface expands to support each tier explicitly: `recall_session_file` (with purpose + scope + maxTokens + requireCitations), `list_session_files`, `get_file_manifest`, `retrieve_matter_memory`, `write_session_memory`, `promote_to_matter_memory`. Restructured system prompt as static prefix (~6K, cached) + dynamic suffix (~5K).

**5. Composition through JPS `$ref`, not legacy scope arrays.** The R4-era `scopes.{actions|skills|knowledge|tools}` array pattern is deprecated. The supported composition surface is `$ref` inside the action's JPS prompt (resolved by `AiAnalysisNodeExecutor.cs:442-499`). Specialized playbooks like Summarize-NDA author a new domain-specific Action with `$ref` to existing Skills (SKL-003 NDA Review) + Knowledge (KNW-006 NDA Standards). No new scope-composition machinery needed.

**6. No hardcoded GUIDs, slash dicts, or capability-name strings.** `SessionSummarizeOrchestrator.ChatSummarizePlaybookId` GUID, `SoftSlashIntentToCapabilityName` dict, `SoftSlashSummarize*CapabilityName` constants — all removed. Slash commands feed `commandIntent` as a vector-query bias into PlaybookDispatcher; NL flows through the same path. One code path for slash and NL.

**Guiding principles**:
- **Data drives routing.** No code change to onboard a new playbook to a slash.
- **One matcher.** PlaybookDispatcher with vector + LLM is sufficient.
- **Layered memory, not one blob.** Working / Session / Matter / User-Org / Retrieval / Audit tiers stay distinct; each has its own tool surface.
- **JIT over stuffing — with rich context cards.** Identifiers + structured cards in prompt; full content via tools with citation enforcement.
- **Precomputed summaries are not authoritative.** They orient; they don't substitute for citation-bearing recall.
- **Honor the user's mental model.** Chat = discussion; Workspace = work product. Slash and NL converge on the same routing decision.
- **The LLM picks when ambiguous.** Don't hardcode the matching; let the model see the menu.
- **Specialized > generic when the data exists.** NDA, Patent, Invoice playbooks should outrank generic Summarize for their specific document types.
- **Additive over reconciliation.** New specialized playbooks coexist with production-bound playbooks. Do NOT merge, delete, or rename production-bound playbooks (see §1.5).
- **Indexes are governed assets.** Playbook-embeddings entries require complete metadata + tracked publication state. No silent drift between Dataverse rows and search index.

---

## §1.5 Production-Bound Playbooks — Migration-Path Required (BINDING CONSTRAINT)

Per pre-fill consumer audit (2026-06-19), the following 6 playbooks have **hard production consumers** today. The architectural rule is **modify-in-place + migrate consumers; do NOT delete; do NOT rename without migration**. They are NOT redundant with each other — each serves a distinct production surface with a distinct output shape.

**What we CAN do (with migration plan)**:
- Rename via stable code (see §1.7 — consumers move to `/by-code/{code}` lookup; old name/GUID stays valid until cutover)
- Add scopes (JPS `$ref` to Skills + Knowledge + Tools + Persona)
- Modify output schema if widget code is updated in lockstep
- Replace one playbook with a successor IF consumer contracts are preserved (same output shape OR client-side migration handled)

**What we CAN'T do**:
- Delete without migrating consumers
- Change output schema unilaterally (consumer widgets bound to it)
- Break NFR-07 contracts (pre-fill flow signatures + 45s timeout + `useAiPrefill` hook + `$choices` constraint)
- Rename in ways that break `/by-name/` literal-string lookups before stable codes are in place

This constraint binds the successor project. Violation = production breakage.

| # | Playbook | GUID / Name | Hardcoded in | Consumer surfaces | Why untouchable |
|---|---|---|---|---|---|
| **1** | `summarize-document-for-chat@v1` | `44285d15-1360-f111-ab0b-70a8a59455f4` | `SessionSummarizeOrchestrator.cs:78-79` | `POST /api/ai/chat/sessions/{id}/summarize` endpoint + agent-tool dispatch + `R5SummarizeTelemetry` "path" dimension + `StructuredOutputStreamWidget.SUMMARIZE_SCHEMA` + 5+ test suites | Chat /summarize convergence; FR-26 convergence invariant |
| **2** | `summarize-document-for-workspace@v1` | (reuses `SUM-CHAT@v1` action) | Soft-slash routing via `CapabilityRouter.SoftSlashSummarizeCapabilityName = "invoke_playbook_summarize"` | Q5 Re-Shaped reference impl: same action, destination=workspace + widgetType=structured-output-stream + R6 FR-30 CapabilityRouter dedup invariant | **NOT broken — reference impl of "one action, two destinations" pattern.** Pairs with playbook #1 for dedup. |
| **3** | `"Summarize New File(s)"` | `4a72f99c-a119-f111-8343-7ced8d1dc988` | `WorkspaceFileEndpoints.cs:29-32` | `POST /api/workspace/files/summarize` SSE endpoint + Matter ribbon button + SummarizeFilesWizard + DocumentEmailWizard + DocumentRelationshipViewer + Get Started card + `useAiSummary` hook (resolves BY NAME via `/by-name/Summarize%20New%20File(s)`) | DIFFERENT output schema than SUM-CHAT: `{tldr, summary, practice areas, parties, call to action}`. NOT interchangeable. |
| **4** | `"Document Profile"` | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` | `AppOnlyAnalysisService.cs:46` + `data/chat-context-mappings.json` (3 mappings: Matter Form, Matter List, **Global Fallback**) | EVERY uploaded document → `ProfileSummaryWorker` background job → populates `sprk_document.sprk_tldr/summary/keywords/documenttype/entities` + `useAiSummary` hook + `WorkspaceAiService` + DocumentUploadWizard SummaryStep + External SPA reads populated fields | Renaming/deleting silently disables: (a) global chat fallback for unrouted intents, (b) Office document profile pipeline for ALL uploads |
| **5** | `"Create New Matter Pre-Fill"` | `2d660cad-d418-f111-8343-7ced8d1dc988` | `MatterPreFillService.cs:43-44` | `POST /api/workspace/matters/pre-fill` endpoint + CreateMatterWizard via `useAiPrefill` + reconcile-demo-env script + `matter-prefill.schema.json` output contract + 45s CTS | **NFR-07 BINDING** — signatures, 45s timeout, `useAiPrefill` hook contract, `$choices`-constrained output ALL preserved |
| **6** | `"Create New Project Pre-Fill"` | `fc343e9c-3460-f111-ab0b-7c1e521b425f` | `ProjectPreFillService.cs:42-43` | `POST /api/workspace/projects/pre-fill` + CreateProjectWizard via `useAiPrefill` + `project-prefill.schema.json` | **NFR-07 BINDING** + recently HOTFIXED 2026-06-09 (GUID corrected from `3f21cec1-...`) — DO NOT regress |

### Hidden hazards (name-collision risks)

The audit surfaced two **name-collision risks** that could silently shadow production calls:

- **"Document Profile" (#4 above) vs "Document Summary" (PB-012 candidate for deletion)** — `useAiSummary.ts:285` and `ChatContextMappingService.cs` resolve BY LITERAL NAME STRING. Renaming "Document Summary" → "Document Profile" (or accidentally touching the wrong one) silently disables the entire chat default-context path AND breaks the Office upload pipeline (jobs succeed-as-noop per `ProfileSummaryWorker.cs:170-175`).
- **"Summarize New File(s)" (#3 above) vs "Summarize File" (PB-015 candidate for deletion)** — `DocumentEmailWizard.tsx:628`, `DocumentRelationshipViewer/App.tsx:231,432,841`, `useAiSummary.ts:285` resolve BY NAME. Renaming PB-015 → "Summarize New File(s)" (or vice versa) shadows the production wizard playbook.

### Adjacent entity needing decision — `sprk_aichatmessage` (per Insights audit)

`sprk_aichatmessage` Dataverse entity is a **broken placeholder**:
- 5 of `ChatDataverseRepository` methods are `Task.CompletedTask` no-ops
- `GetMessagesAsync` always returns empty array
- Explicit comment admits: "For Phase 1 (AIPL-052), we return an empty list until the query API is extended... All test coverage is against the mock, not this implementation."
- 10K char hard cap on `sprk_content` field
- Listed as "transient chat" in `Migrate-DataverseData.ps1:55` exclude list
- **Real chat history persistence is Cosmos `sessions` container** via `SessionPersistenceService` (write-through, fire-and-forget)

**Decision needed**: fix it as a true write-through audit repository OR formally retire (rename interface to `IChatAuditRepository`, write-only contract, confirm Cosmos `audit` is sole reader for compliance queries). Per user Q11/Q17 — "don't remove; review and remediate to fit new approach." Default recommendation: retire `ChatDataverseRepository` placeholder methods + repurpose `sprk_aichatmessage` as pure audit-write target (no read path). Surfaced in WP5.1d.

### Reconciliation candidates — REQUIRE DATAVERSE AUDIT (not just repo grep)

The following playbooks have ZERO production-code consumers via repo grep, but MUST be verified at the Dataverse level before deprecation:

| Candidate | Repo grep finding | Dataverse verification required |
|---|---|---|
| **PB-009 "Summarize a Non-Disclosure Agreement"** | Only ribbon button + UI strings; no orchestrator binding | Query `sprk_aichatcontextmapping` rows; Power Automate flow audit |
| **PB-012 "Document Summary"** | Repo grep only finds historical project notes; no production runtime consumer. **Confusion risk**: `seed-data/playbooks.json` has `PB-012 = "Email Analysis"` — the user's "Document Summary" may be a different Builder-UI playbook | Verify exact name in Dataverse; check `sprk_aichatcontextmapping` |
| **PB-015 "Summarize File"** | No `PB-015` literal references in code | Verify NOT what `/by-name/Summarize%20File` resolves; check `sprk_aichatcontextmapping` |
| **PB-017 "Matter/Project Activity Summary"** (Scheduled Notification) | No BFF scheduler found | Likely scheduled via Power Automate/Dataverse workflow OUTSIDE the codebase — `sprk_analysisrun` history query needed |

**Decision rule**: do NOT delete any of these without a Dataverse-side verification round confirming zero consumers. The repo audit is necessary but not sufficient.

---

## §1.6 R6 Status (final audit) — All 9 Pillars Materially Shipped

Per R6 Pillar 1/4/7/8 audit + R6 Pillar 2/3/5/6/9 audit (both 2026-06-19), **all 9 R6 pillars are materially complete in code**. R6 is feature-complete; remaining work is governance + UAT + bug triage.

| Pillar | Status | Task range | Evidence highlight |
|---|---|---|---|
| **1 — Persona** | ✅ | 001-005 | Data-driven `sprk_aipersona` wired in `PlaybookChatContextProvider.cs:130-154` |
| **2 — Tool Registry + 8 Typed Handlers** | ✅ | 006-013 + 100-109 (18 tasks) | `IToolHandler` rename + Q9 big-bang migration of 10 chat tools + 8 new typed handlers all in `Services/Ai/Handlers/` |
| **3 — Generic invoke_playbook** | ✅ | 020-023 | `IInvokePlaybookAi` facade + Null peer + symmetric DI + specialized bridges DELETED (verified) |
| **4 — Chat /summarize FK fix** | ✅ | 024-025 | Alternate-key bypass removed (0 references); orchestrator is thin pass-through |
| **5 — Output Schema (Q5 Re-Shaped)** | ✅ | 030-035 + 040-042 + 048-049 (11 tasks) | `NodeRoutingConfig.cs:127` + StructuredOutputStreamWidget schema-aware + CapabilityRouter dedup; Phase B exit signed |
| **6 — Workspace State (6a/6b/6c)** | ✅ | 050-063 (14 tasks) | `WorkspaceStateService.cs:40` Q4 hybrid persistence + 3 chat tools + ExecutionTraceWidget + ADR-015 audit complete; Phase C exit signed 2026-06-18 |
| **7 — Memory + Q7 UI** | ✅ | 064-070 | All 7 memory services + Pinned Memory CRUD UI; **most complete pillar** |
| **8 — Command Router** | ✅ | 080-088 | 6 hard + 4 soft slashes + 3 reference types + composition tests green |
| **9 — Widget Visibility Contract** | ✅ | 071-074 | `getAgentVisibleState` per-widget impls + server-side `TryDeriveVisibleState` privacy defense |

### Honest R6 Closeout Punch List (not just "089+090")

The Pillars 2/3/5/6/9 audit surfaced items beyond pure governance that **must** be triaged before R6 can close cleanly:

1. **Tasks 089 + 090** — governance (Phase D exit gate MINIMAL rigor) + wrap-up (FULL rigor including `/code-review` + `/adr-check` + `/repo-cleanup` + README/plan status flip + lessons-learned + R7 backlog). ~8 hrs total.

2. **3 UAT hotfixes need to land on master** — `be95dfc7d`, `35462f807`, `a74ee9fdb` are on the working branch only. **PR #401 is stale (contains only hotfix #1).** Either update #401 or open a consolidating UAT-hotfix PR. **R6 cannot close cleanly without these on master** because auto-deploy from master would re-introduce the silent `toolCount=0` stall the hotfixes fixed.

3. **~~Open HIGH-severity bug: `/summarize` slash produces chat-only output, no Workspace tab~~** — **v3.3 status (2026-06-21): FIXED in R6 via Hotfix #4**; commit pending PR #401 UAT smoke confirmation. No carry-forward to successor required for the bug itself. The structural concern that this bug exposed — that the slash and NL paths needed to converge on the same dispatch decision — is still architecturally addressed by this successor project via WP2 (file-aware classification with `commandIntent` as vector-query bias, not a separate routing layer) + WP3 (data-driven destination metadata) + WP4 (CapabilityRouter retirement). The fix becomes structural rather than a defensive guard. Original v3.1 framing preserved for historical context: *"Root cause is in CapabilityRouter Layer 0.5 + tool-manifest expansion — the slash path delivers exactly the Pillar 5 dedup contract (one playbook, one render), just to the wrong destination."*

4. **UAT Tiers B/C/D/E/F/G not executed** — only Tier A done. Either complete UAT OR explicitly accept limited UAT coverage with sign-off in task 090 lessons-learned.

5. **Verify Phase D component-task integration** — tasks 081/082/083 marked "ConversationPane.tsx integration deferred to main session" in TASK-INDEX. Hotfix #1 appears to have closed this (Pillar 8 wiring verified in iframe context, 5.88 MB bundle). Confirm during 090.

### Technical debt deferring to successor (documented lift paths)

| Item | Where | Lift target |
|---|---|---|
| Hardcoded `ChatSummarizePlaybookId` GUID | `SessionSummarizeOrchestrator.cs:78-79` | §1.7 stable codes |
| `BuildDefaultSystemPrompt` defense-in-depth fallback | `PlaybookChatContextProvider.cs:146,163` | Delete after stabilization window |
| `SummarizeInvocationPath.AgentTool` discriminator | `SessionSummarizeOrchestrator.cs:211-247` | Fold; only consumer is telemetry |
| `PinnedMemoryProvenanceBadge` stub | shared lib | Wait on `PinDto.source` |
| Persona admin UI | n/a | Build when business case grows |
| Tool-name normalization boundary brittleness | 3 layers (adapter / filter / Layer 3) | Successor cleanup target |
| Pillar 9 server-side closed-union switch | `SprkChatAgentFactory.cs:2178` | R7 backlog (new widget types in R7+ won't appear in prompts until union is updated) |
| Phase B YELLOW tech debt | `phase-b-exit-gate.md:31-36` | R7 candidates |
| Auto-deploy gap (BFF only, not SpaarkeAi frontend) | CI/CD pipeline | Pre-staged for task 090 lessons-learned |

### Hidden findings to acknowledge in task 090

1. **No single end-to-end test exercises all 9 pillars** — task 087 is composed-evidence framing, not a single test run. Be honest in close-out report.
2. **Task 070 marked ✅ but Q7 expansion partially deferred** to R7 (markdown-only eval baseline). Documentation drift; flag.
3. **Cascading hotfix pattern this session** — 3 layers had different "tool name" conceptions; cleanup target for successor.
4. **The `/summarize` bug is a routing failure masquerading as a Pillar 5 issue** because the Pillar 5 contract holds (single-fire correct, wrong destination). Architecturally important framing for successor project spec.

### Bottom line

R6 is shipped. Closure requires: (a) tasks 089+090, (b) hotfix consolidation on master, (c) explicit triage of `/summarize` bug, (d) UAT completion or accepted limited coverage. The successor project absorbs the architectural debt cleanly via §1.7 stable-code reform + WP2-6.

---

## §1.7 Playbook Identification Reform — Stable Codes

**Problem**. The codebase today uses **3 different resolution patterns** for playbook identification, each with environment-portability or naming-fragility issues:

| Pattern | Where | Issue |
|---|---|---|
| **Hardcoded GUID + config override** | `MatterPreFillService.cs:43-44` (`2d660cad-...`), `ProjectPreFillService.cs:42-43` (`fc343e9c-...`), `WorkspaceFileEndpoints.cs:29-32` (`4a72f99c-...`), `WorkspaceAiService.cs:41-44` (`18cf3cc8-...`), `SessionSummarizeOrchestrator.cs:78-79` (`44285d15-...`) | **GUIDs do NOT transfer across environments** — each environment has different GUIDs for the same logical playbook. Config override works but is per-environment, error-prone (`ProjectPreFillService` had a broken GUID until 2026-06-09). |
| **Hardcoded NAME** | `AppOnlyAnalysisService.cs:46` (`"Document Profile"`) | Renaming silently breaks. No structural reference; literal-string lookup. |
| **Resolve-by-NAME at call site** | `useAiSummary.ts:285`, `DocumentEmailWizard.tsx:628`, `ChatContextMappingService.cs`, `/api/ai/playbooks/by-name/{name}` endpoint | Same fragility as hardcoded name; name collisions silently shadow production calls (e.g., "Document Profile" vs "Document Summary", "Summarize File" vs "Summarize New File(s)"). |

The user explicitly flagged: *"the issue with guids is that they don't transfer environments easily BUT there is also the risk of playbook name changes as per the hidden hazards perhaps we need to use this opportunity to strict about playbook codes and make them somehow locked and immutable so transfer environments"*.

This is the right architectural lift.

### §1.7.1 — Recommended schema additions

**New field on `sprk_analysisplaybook`**:

```
sprk_playbookcode   (Text, unique within tenant, immutable once set)
```

**Naming convention**:
- Kebab-case domain code
- **NO `@v1` / `@v2` suffix** — versioning is a separate concern; the code is stable
- Examples:
  - `summarize-document-chat` (replaces `summarize-document-for-chat@v1`)
  - `summarize-document-workspace` (replaces `summarize-document-for-workspace@v1`)
  - `summarize-new-files` (replaces literal name lookup for `"Summarize New File(s)"`)
  - `document-profile` (replaces hardcoded name `"Document Profile"`)
  - `create-matter-prefill` (replaces hardcoded GUID `2d660cad-...`)
  - `create-project-prefill` (replaces hardcoded GUID `fc343e9c-...`)
  - `workspace-ai-summary` (replaces hardcoded GUID `18cf3cc8-...`)
  - `summarize-nda` (new specialized playbook from PB-009 update per Q1)
  - `summarize-patent` (new specialized)
  - `extract-invoice` (new specialized)

### §1.7.2 — Resolution endpoint

**New stable endpoint** `GET /api/ai/playbooks/by-code/{code}`:
- Returns the playbook by `sprk_playbookcode`
- Tenant-scoped resolution
- Cached per ADR-014 pattern (5 min TTL)
- 404 if not found; clean error model

### §1.7.3 — Migration plan (3 patterns, per complete pre-fill audit)

Per complete pre-fill consumer audit (2026-06-19), the 6 user-listed wizards + 3 additional consumers map onto **3 resolution patterns**, not 9 independent migrations:

**Complete consumer inventory** (from audit):

| Consumer | Type | Pattern | Current resolution | Stable code target |
|---|---|---|---|---|
| CreateMatterWizard | Wizard | A | GUID `2d660cad-...` + config `Workspace:PreFillPlaybookId` | `create-matter-prefill` |
| CreateProjectWizard | Wizard | A | GUID `fc343e9c-...` + config `Workspace:ProjectPreFillPlaybookId` (stale comment refs `3f21cec1-...`) | `create-project-prefill` |
| CreateWorkAssignmentWizard | Wizard | A (inherits) | Reuses Matter endpoint (no separate playbook — hidden coupling) | (inherits `create-matter-prefill`) |
| SummarizeFilesWizard | Wizard | A (broken) | GUID `4a72f99c-...` + raw `IConfiguration[Workspace:SummarizePlaybookId]` — **ADR-018 violation; not in WorkspaceOptions** | `summarize-new-files` |
| WorkspaceAiService | Background widget | A | GUID `18cf3cc8-...` + config `Workspace:AiSummaryPlaybookId` | `workspace-ai-summary` |
| DocumentEmailWizard | Wizard | B | Name `"Summarize New File(s)"` literal in `DocumentEmailWizard.tsx:628` → `/by-name/` | `summarize-new-files` (SAME as #4) |
| DocumentUploadWizard | Wizard | B | Name `"Document Profile"` literal in `useAiSummary.ts:285` → `/by-name/` | `document-profile` |
| AppOnlyAnalysisService — Document Profile path | Background | B | Name `"Document Profile"` const at `:46` | `document-profile` |
| AppOnlyAnalysisService — Email Analysis path | Background | B | Name `"Email Analysis"` const at `:1068` | `email-analysis` |
| SessionSummarizeOrchestrator | Chat /summarize | A (no config) | GUID `44285d15-...` **hardcoded with no config override** | `summarize-document-chat` |
| LegalWorkspace dead code | Retired (OC-R4-05) | C | Same as live siblings | Delete (cleanup) |
| PCF `UniversalQuickCreate/useAiSummary.ts` | PCF | C | Duplicate of `useAiSummary` | Migrate or delete (cleanup) |

**Pattern A — Typed-options + stable code** (Matter / Project / WorkspaceAiService / Summarize-Files / SessionSummarizeOrchestrator):

1. Add `sprk_playbookcode` column to `sprk_analysisplaybook` (Dataverse schema change — flag for confirmation trigger)
2. Backfill codes on all production-bound playbooks
3. Stand up `GET /api/ai/playbooks/by-code/{code}` endpoint (cached per ADR-014; tenant-scoped)
4. Update `WorkspaceOptions.cs` — add `Workspace:SummarizePlaybookCode` (fixes the ADR-018 violation on the way); rename existing options to `*PlaybookCode`
5. Update services to resolve by code:
   - `MatterPreFillService.cs:43-44` — `PlaybookCodes.CreateMatterPreFill = "create-matter-prefill"`
   - `ProjectPreFillService.cs:42-43` — same pattern
   - `WorkspaceAiService.cs:41-44` — same pattern
   - `WorkspaceFileEndpoints.cs:29-32` — same pattern + lift to typed options
   - `SessionSummarizeOrchestrator.cs:78-79` — lift to typed options (closes R6-acknowledged debt)
6. CreateWorkAssignmentWizard comes along for free — it shares the Matter endpoint

**Pattern B — Name-resolve → code-resolve** (DocumentEmailWizard / useAiSummary / AppOnlyAnalysisService):

7. Replace literal name strings with codes:
   - `useAiSummary.ts:285` — `"Document Profile"` → `"document-profile"` via `/by-code/`
   - `DocumentEmailWizard.tsx:628` — `"Summarize New File(s)"` → `"summarize-new-files"` via `/by-code/`
   - `AppOnlyAnalysisService.cs:46` — `DefaultPlaybookName` → `DefaultPlaybookCode = "document-profile"`
   - `AppOnlyAnalysisService.cs:1068` — `EmailAnalysisPlaybookName` → `EmailAnalysisPlaybookCode = "email-analysis"`
   - `ChatContextMappingService.cs` — name lookups → code lookups
8. Deprecate `/api/ai/playbooks/by-name/{name}` endpoint — log warning per call
9. After stabilization window with zero warnings, remove `/by-name/{name}` endpoint

**Pattern C — Cleanup** (must happen BEFORE Patterns A + B to clean blast-radius):

10. Delete or migrate `src/solutions/LegalWorkspace/src/components/CreateMatter/CreateRecordStep.tsx` (and Project / WorkAssignment siblings) — dead code per OC-R4-05 retirement
11. Migrate or delete `src/client/pcf/UniversalQuickCreate/control/services/useAiSummary.ts` — duplicate of shared hook
12. Fix stale GUID comments in `WorkspaceOptions.cs:35` and `ProjectPreFillService.cs:40` (still reference rotated `3f21cec1-...`)

**Sequencing**: Pattern C → Pattern A → Pattern B. Cleanup first so the migration's blast radius matches the documented 9-consumer inventory. Pattern A is lowest-risk (sets the resolver infra). Pattern B closes the silent-rename risk last.

### §1.7.3a — Note on `Workspace:SummarizePlaybookId`

The audit surfaced an existing ADR-018 violation: `WorkspaceFileEndpoints.cs:30, :254` reads `Workspace:SummarizePlaybookId` via raw `IConfiguration[...]` indexer instead of `IOptions<WorkspaceOptions>`. This is technical debt the stable-code migration MUST fix in passing — adding the field to `WorkspaceOptions.cs` is part of Pattern A step 4.

### §1.7.4 — Same treatment for actions

The action codes today use `@v1` suffix (`SUM-CHAT@v1`). This violates the same hygiene rule. Recommendation:
- **NEW actions**: kebab-case code without `@v1`: `summarize-document-chat`, `extract-invoice`, etc.
- **Existing actions**: add `sprk_actioncode_clean` column (or rename per migration); keep `@v1`-suffixed codes valid for backward compat until cutover
- This is more scope; flag for separate Sub-WP if user wants it tackled now

### §1.7.5 — Why this dovetails with the rest of the design

- **WP1.5 Send-to-Index**: validation gate enforces `sprk_playbookcode` populated before indexing. Stale-detection via code is more reliable than via name/GUID.
- **WP2 file-aware routing**: PlaybookDispatcher returns the matched playbook's CODE, not GUID. Downstream code paths use codes uniformly.
- **WP4 retire CapabilityRouter**: when the codebase is fully on codes, removing layered routing is cleaner — no GUID/name dual-resolution at multiple layers.
- **WP5 stateful chat**: workspace tabs reference playbooks by code (durable across sessions). Cross-session matter memory can cite "executed playbook = `summarize-nda`" stably.
- **WP6 specialized playbooks**: new authors get clean codes from the start.

### §1.7.6 — Open questions

- Should `sprk_playbookcode` be globally unique or tenant-unique? Recommend **tenant-unique** (allows org-level customization without colliding with SYS- defaults)
- Migration order: which consumers go first? Pre-fill (highest risk) or chat-summarize (lowest risk)? Recommend chat-summarize first (lowest blast radius)
- Action codes: tackle in same WP or defer? Recommend defer to separate sub-WP unless user wants it now

---

## §2 Per-WP Deep Dive

### WP1 — Index the @v1 playbooks (+ new richer playbooks) into `playbook-embeddings`

**Problem**. The `playbook-embeddings` AI Search index is the basis for vector-similarity playbook matching, but it contains only the production PB-* playbooks (PB-009 NDA Summary, PB-012 Document Summary, PB-015 Summarize File, etc.). The R5/R6-seeded `@v1` playbooks (`summarize-document-for-chat@v1`, `summarize-document-for-workspace@v1`) are NOT indexed. PlaybookDispatcher cannot match them; they reach the LLM only via `invoke_playbook`'s dynamic description menu inside chat completion.

**Current state**.
- Indexing is per-playbook via `POST /api/ai/playbooks/{playbookId}/index` ([PlaybookEmbeddingEndpoints.cs:32](src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEmbeddingEndpoints.cs#L32)). Fire-and-forget; background indexing via `PlaybookIndexingBackgroundService`.
- Indexed content per playbook: `playbookName + description + triggerPhrases + tags` ([PlaybookEmbeddingService.cs:28-30](src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingService.cs#L28-L30)).
- Embedding model: `text-embedding-3-large` (3072-dim).
- **Critical**: `description` content directly drives matching. The current @v1 descriptions ("Chat-driven Summarize-for-Chat playbook (R5 D2-02). Single AiAnalysis node executes... per task 006 (D1-06)...") are **developer-prose with project-task references that pollute the embedding**.

**Options considered**.

| Option | Mechanism | Cost | Tradeoff |
|---|---|---|---|
| (a) Trigger index for all existing playbooks as-is | POST to endpoint per playbook | 5 min admin | Bad descriptions pollute vector match; LLM/router will be confused |
| (b) Rewrite descriptions on existing playbooks first, then trigger indexing | Dataverse update per playbook + index trigger | 30-60 min | Cleaner matching; one-time data cleanup |
| (c) (b) + author NEW specialized playbooks (Summarize-NDA, Summarize-Patent, etc.) and index them too | Significant playbook authoring | Days-weeks | Enables file-aware routing (WP2) to differentiate; required for the user's NDA-vs-Patent vision |

**Recommendation**: **(b) + scoped (c) + index governance**. Clean up descriptions on existing playbooks immediately; author Summarize-NDA + Summarize-Patent + Extract-Invoice as the first 3 specialized playbooks in scope alongside WP6. Other specializations follow in later phases. **Plus**: per user feedback, treat the index as a governed asset — audit + extend metadata fields, add a tracked "send to index" process so silent drift between Dataverse rows and search index becomes impossible.

**Description rewrite guidelines** for embedding-friendly text:
- One sentence purpose ("Produces a structured TL;DR + risk summary of an uploaded NDA")
- Trigger words explicitly stated ("Use when the user uploads or references an NDA / non-disclosure agreement / confidentiality agreement")
- Output format mentioned ("Renders to the Workspace pane as a 7-section NDA summary tab")
- Document type hints ("Document types: NDA, non-disclosure agreement, confidentiality agreement")
- No project-task references, no GUIDs, no developer prose

#### WP1.5 — Index Metadata Audit + Send-to-Index Governance (per user feedback)

**Problem**. Current `PlaybookEmbeddingDocument` shape composes embedding from `playbookName + description + triggerPhrases + tags` ([PlaybookEmbeddingService.cs:28-30](src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingService.cs#L28-L30)) but the actual field coverage on existing playbooks is incomplete:
- Most @v1 playbooks have empty `triggerPhrases`
- Tags inconsistent across playbook generations
- No structured "document type" metadata (critical for WP2 file-aware matching)
- No "last indexed at" timestamp on the playbook row → can't tell which playbooks are out-of-sync with their search-index entry
- Indexing is via fire-and-forget POST endpoint with no confirmation feedback

**Required metadata expansion** for routing-quality indexing:

| Field | Purpose | Current state | Recommended |
|---|---|---|---|
| `playbookName` | Display + embedding | ✅ Required | Keep |
| **`sprk_playbookcode`** | Stable identifier across environments | ❌ Doesn't exist | **ADD** per §1.7 — drives `/by-code/{code}` resolution |
| `description` | Primary embedding signal | ⚠️ Inconsistent quality | Audit + rewrite per guidelines above |
| `triggerPhrases` | Embedding boost for common phrasings | ⚠️ Mostly empty | Populate per playbook (5-10 phrases each) |
| `tags` | Categorical bucketing | ⚠️ Inconsistent | Standardize: `["summarize", "nda", "contract", "chat"]` |
| **`documentTypes`** | Routing hint for WP2 file-aware match | ❌ Doesn't exist | **ADD** — `["nda", "non-disclosure agreement", "confidentiality agreement"]` |
| **`destinationHint`** | Surface routing hint visible in search | ❌ Doesn't exist | **ADD** — `"workspace"` / `"chat"` / `"form-prefill"` / `"both"` |
| **`recordType`** | Host context pre-filter | ✅ Exists | Keep |
| **`isEnabled`** | Manual disable | ❌ Doesn't exist on doc | **ADD** — bool, default true; supports staging disabled playbooks |
| **`sprk_jpsmatchingmetadata`** (NEW per user Q) | Structured JPS metadata for routing match quality | ❌ Doesn't exist | **ADD** JSON field with shape below |
| **`indexedAt`** | Sync tracking | ❌ Doesn't exist on Dataverse | **ADD** to `sprk_analysisplaybook` row |
| **`indexHash`** | Drift detection | ❌ Doesn't exist | **ADD** — sha256 of embed-input string; mismatch = stale |

**`sprk_jpsmatchingmetadata` schema** (the user's suggested JPS field — improves vector + LLM match signal-to-noise):

```json
{
  "documentTypes": ["nda", "non-disclosure agreement", "confidentiality agreement"],
  "intents": ["summarize", "review", "extract terms"],
  "triggerPhrases": [
    "summarize this NDA",
    "review the confidentiality clauses",
    "what are the terms"
  ],
  "preferredOver": ["summarize-document-chat"],
  "outputDestination": "workspace",
  "scopeHints": ["legal", "contract"],
  "exclusionHints": ["invoice", "patent"]
}
```

How it improves matching:
1. **Embedding input**: concatenated `documentTypes + intents + triggerPhrases` becomes part of the vector embed — much richer than free-text description alone
2. **Structured filter**: Stage 1 vector search can pre-filter by `documentTypes` matching detected file type (from WP2 Phase A)
3. **Tie-breaker bias**: `preferredOver` lets specialized playbooks (Summarize-NDA) outrank generic ones (Summarize-Document) when both score similarly
4. **LLM hints in Stage 2**: refinement prompt includes structured hints, not just descriptions
5. **Routing semantics in data**: `outputDestination` propagates to `NodeRoutingConfig` consistently

This is one of the highest-leverage additions in the entire design — moves matching from "embedding hope" to "structured signal".

**Tracking fields on `sprk_analysisplaybook` Dataverse row** (additive, no breaking change):
- `sprk_lastindexedat` (DateTime, nullable) — timestamp when index trigger last completed successfully
- `sprk_indexstatus` (Choice: `not-indexed` / `pending` / `indexed` / `stale` / `failed`) — current state
- `sprk_lastindexerror` (Text, nullable) — last failure message (truncated to 500 chars)
- `sprk_indexhash` (Text, nullable) — sha256 of last-indexed embed input; on playbook update, regenerate and compare; mismatch = `stale`

**Send-to-index process**:

1. **Manual trigger UX**: Power Apps form on `sprk_analysisplaybook` carries a "Send to Index" button. Clicking it:
   - Sets `sprk_indexstatus = "pending"`
   - Calls `POST /api/ai/playbooks/{id}/index`
   - Polls or receives webhook on completion → updates `sprk_lastindexedat`, `sprk_indexhash`, sets status to `indexed` or `failed`
2. **Automatic detection of staleness**: nightly background job (or Dataverse plugin on playbook update):
   - For each `sprk_analysisplaybook`, recompute the embed-input hash
   - If hash differs from `sprk_indexhash` → set status to `stale`
   - Optional: auto-trigger reindex for stale playbooks (configurable per environment)
3. **Validation gate**: on send-to-index, reject if any required metadata field is empty (description, documentTypes, destinationHint). Return structured error with which fields are missing.

**Admin view**: a Power Apps view filtered to `sprk_indexstatus IN ('stale', 'failed', 'not-indexed')` makes index-drift discoverable to admins without code investigation.

**Dependencies**: None (additive); enables WP2 (file-aware matching reads `documentTypes` filter); precedes WP6 (new specialized playbooks must follow the metadata standard).

**Success criteria**:
- All currently-active Summary-shaped playbooks have clean, user-facing descriptions
- All carry `documentTypes` + `destinationHint` metadata
- All are indexed in `playbook-embeddings` and queryable
- `sprk_indexstatus` view shows zero `stale` or `failed` entries after migration
- Smoke test: `SearchPlaybooksAsync("summarize this NDA")` returns Summarize-NDA as top-1
- Admin can trigger reindex from Power Apps form; receives confirmation

**Open questions**:
- **PB-009 "Summarize NDA"** authored via Builder UI — RESOLVED per Q1: keep, review, update to current standard
- **`summarize-document-for-workspace@v1`** — RESOLVED per Q2: reconcile (no need for two general-purpose summarize-doc playbooks); BE CAREFUL not to break Document Summary / Profile playbooks used for Create wizards (NFR-07 binding for pre-fill)
- Schema change for the 4 new fields on `sprk_analysisplaybook` — flag for R6 confirmation trigger (schema change to production Dataverse entity)

---

### WP2 — File-aware classification in PlaybookDispatcher (Phase A / B / C)

**Problem**. Today's `PlaybookDispatcher.DispatchAsync(userMessage, hostContext)` ([PlaybookDispatcher.cs:150](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs#L150)) embeds ONLY the user message for vector matching. It cannot differentiate "summarize this NDA" from "summarize this Patent" when the user types the same prompt with different file attachments. Multi-file scenarios (patent + invoice) collapse to a single match.

**Current state**.
- Stage 1: vector similarity against `playbook-embeddings`, 1.5s budget, top-5 candidates ([PlaybookDispatcher.cs:173-211](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs#L173)).
- Stage 2: gpt-4o-mini LLM refinement of top-5, 0.5s budget ([line 228-264](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs#L228)).
- Single-candidate confidence threshold 0.85 skips Stage 2.
- Total budget 2s.

**Research-grounded recommendation** (A1 findings, ~600-1225ms total budget consumption):

**Phase A — Per-file in-memory fingerprint** (parallel, <50ms total)

For each `ChatMessageAttachment`, compute:
```
FileFingerprint {
  filenameTokens: tokenize(filename.toLowerCase()),       // ["nda", "acme", "2025"]
  contentType: contentType,                                // "application/pdf"
  textLength: textContent.length,
  textPrefix: textContent.substring(0, 2000),             // ~500 tokens
  filenameHash: sha256(filename + ":" + textPrefix)       // for cache key
}
```
Pure CPU. No external call. Per-file <5ms.

**Why N = 2000 chars**: Legal docs put discriminative content (title block, parties, contract type, recitals) on the first page; boilerplate (force majeure, severability) is at the end. Per-token embedding latency is flat up to 8K tokens (provider batches), so there's no embedding-side latency penalty to using a larger N. 2000 chars (~500 tokens) captures the discriminator without bloating the embedding. Validate empirically with a 100-doc Spaarke corpus pass at N ∈ {500, 1000, 2000, 4000}.

**Phase B — Per-file vector match** (parallel, ~200ms for 1-3 files)

For each file, compose:
```
queryText = $"{userMessage} | Document: {filename} | Type hint: {contentType} | Content: {textPrefix}"
```
Embed via `text-embedding-3-large`. Search `playbook-embeddings` → top-5 per file.

Parallelism: `Task.WhenAll` across files. Azure OpenAI returns embeddings ~80-150ms per call. Expected: 1 file ≈ 150ms, 3 files ≈ 200ms.

Cost per turn: 5 files × ~540 tokens × $0.13/1M = **$0.00035 per chat turn**. Negligible.

**Critical**: include user message in the per-file query so user intent ("summarize" vs "compare to template") biases the match even for same doc type.

**Phase C — Reconciliation** (≤500ms when needed; 0ms easy case)

```
if (files.Count == 0)
    → existing message-only path

if (files.Count == 1)
    → top-1 candidate; Stage 2 LLM refine as today

if (files.Count >= 2)
    if all files' top-1 == same playbook:
        → use that playbook; no LLM decider
    elif files agree on top-3 set (intersection non-empty):
        → take intersection's highest aggregate score
    else (true disagreement):
        → invoke gpt-4o-mini decider with structured output:
            {
              "selectedPlaybook": "...",
              "selectionConfidence": 0.0-1.0,
              "reason": "...",
              "isMultiPlaybookScenario": bool
            }
            Input: user message + per-file (filename, contentType, top-3 playbook names with scores)
            NOT file text — only metadata + candidate names.

if isMultiPlaybookScenario AND selectionConfidence < 0.7:
    → ask user: "I see [N] document types in this upload. Run [playbook A] for [file X], or [playbook B] for [file Y], or both?"
else:
    → execute selected playbook(s)
```

**Multi-file resolution — 3 options to evaluate** (per user input v2):

**(A) A1's recommendation: parallel per-file fingerprint + match + reconciliation** (the original Phase A/B/C above)
- Pro: granular per-file matching; can fan out to multiple playbooks if user intent is multi-playbook
- Pro: low latency for single-file case (~655ms total)
- Con: requires per-file Phase A/B/C complexity
- Con: reconciliation logic + decider LLM call needed for disagreement cases

**(B) User's recommendation: leverage existing multi-file summary process to produce matching content**
- Use the existing AI summary process (`SUM-CHAT@v1` action) to summarize multiple uploaded files into ONE combined narrative
- Embed that combined summary + user message → ONE match query against playbook-embeddings
- Pro: leverages existing code; simpler architecture (1 match instead of N)
- Pro: combined summary captures cross-file semantics (might match better when user uploads multiple related docs)
- Con: higher latency (~2-4s for multi-file summarize → match)
- Con: combined summary may dilute doc-type signals when files are heterogeneous (NDA + Invoice → matches neither well)
- Con: doesn't help with "fan out to multiple playbooks" case
- Con: adds an LLM call to the routing path (was just embedding + vector search)

**(C) Hybrid: precomputed file manifests + JPS matching metadata**
- At upload time (WP5.5), compute file manifest including classified `documentType` (cheap LLM call once per file, cached)
- At routing time, use the manifest's `documentType` as a structured pre-filter against playbook-embeddings (using new `sprk_jpsmatchingmetadata.documentTypes` from WP1.5)
- For multi-file: union of detected document types as filter; vector match within filtered set
- Pro: lowest routing latency (manifest precomputed; routing is just filter + vector search)
- Pro: per-file granular signals preserved
- Pro: synergizes with WP1.5 JPS matching metadata
- Pro: ambiguous cases fall back to (A)-style decider LLM call
- Con: depends on WP5.5 upload pipeline being in place
- Con: classification cost paid at upload (one-time per file)

**Decision (default — to discuss)**: **(C) Hybrid** is recommended IF WP5.5 upload pipeline ships in this project (which it does). It avoids per-routing LLM cost while preserving granularity. (B) is a good fallback when manifests are sparse. (A) becomes the disagreement-resolution path within (C). All three approaches are compatible at the architecture level — the choice is about which path is primary.

**Open question**: confirm (C) hybrid as primary OR pick (B) for simpler MVP that ships before WP5.5 lands.

**Slash-bias integration**: `commandIntent` becomes a vector-query bias, not a separate router:
```
if commandIntent == "summarize":
    queryText = $"summarize {userMessage} | Document: ... | Content: ..."
```
This eliminates the hardcoded `SoftSlashIntentToCapabilityName` dict (WP4) while preserving the slash-as-strong-signal UX.

**Budget summary**:

| Scenario | Phase A | Phase B | Phase C | Existing Stage 2 | **Total** |
|---|---|---|---|---|---|
| Message only (no files) | 0 | 100ms | 0 | 500ms | **600ms** |
| 1 file | <5ms | 150ms | 0 | 500ms | **~655ms** |
| 2 files agreeing | <10ms | 200ms | 0 | 500ms | **~710ms** |
| 3 files disagreeing | <15ms | 250ms | 400ms | 500ms | **~1165ms** |
| 5 files disagreeing | <25ms | 300ms (tail) | 400ms | 500ms | **~1225ms** |

All within existing 2s `PlaybookDispatcher.TotalTimeout`.

**Dependencies**: WP1 (playbooks must be indexed before matching); WP3 (destination metadata enables PlaybookOutputHandler to act on matches).

**Embedding model decision**: **Stay on `text-embedding-3-large`** for R6. Voyage-law-2 beats it 6-10% on legal benchmarks but requires Azure-external API calls (data residency review) + new ADR. Doc-type bucketing (NDA vs Patent vs Invoice) is much easier than the case-law retrieval voyage-law-2 was tuned for; the 6-10% margin will compress on this task.

**Document Intelligence decision**: **Defer**. Prebuilt "contract" model is for *extraction* not classification. Custom classifier needs binary not text (Spaarke today only sends extracted text). Wrong tool for Stage 1 routing; right tool for a later Phase when a playbook needs structured field extraction.

**Open questions**:
- Empirical validation of N = 2000 chars on Spaarke's legal corpus
- Threshold for `selectionConfidence` triggering user disambiguation (start 0.7, calibrate)
- Telemetry plan: log `(filesCount, filesAllAgree, decidersInvoked, selectedPlaybook, userOverrode)` for accuracy monitoring

**Success criteria**:
- "Summarize this NDA" with NDA upload → routes to Summarize-NDA (not generic Summarize)
- "Summarize this patent" with patent upload → routes to Summarize-Patent
- Patent + invoice + "review these" → either fans out OR asks user
- p95 total dispatch latency < 1.5s for 1-3 file case

---

### WP3 — Destination metadata + `PlaybookOutputHandler` integration

**Problem**. The Q5 re-shaped design says: action carries outputSchema; node config carries destination + widgetType. Today's reality:
- `NodeRoutingConfig` schema is **already correct** ([Models/Ai/NodeRoutingConfig.cs:30-274](src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs#L30-L274))
- `summarize-document-for-workspace@v1` uses it correctly (`destination: "workspace"`, `widgetType: "structured-output-stream"`)
- `PlaybookOutputHandler` has NO `Workspace` case ([PlaybookOutputHandler.cs:108-117](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs#L108-L117)) — only Text/Dialog/Navigation/Download/Insert
- `DispatchResult` doesn't carry `NodeDestination` or `WidgetType` at all
- The Workspace tab opens today via a DIFFERENT path: `sseToPaneEventBridge` ([src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts:174-256](src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts#L174-L256)) reads `AnalysisChunk` deltas → `workspace.field_delta` events → `StructuredOutputStreamWidget`. This bypasses `PlaybookOutputHandler` entirely; the destination is implicit in the streaming code.

**The schema is right. The integration is the gap.**

**Schema-location decision** (closed): per-node JSON property in `sprk_playbooknode.sprk_configjson`. NOT on action row (`sprk_outputformat` is taken by text-formatting). NOT a new column. Backward compat is structural — `NodeRoutingConfig.Parse(null)` defaults to `Chat`, so existing playbooks with no configJson continue to work.

**Missing piece**: `Both` enum value. FR-27 promised `chat / workspace / both / side-effect`; code has Chat/Workspace/FormPrefill/SideEffect ([NodeRoutingConfig.cs:31-64](src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs#L31-L64)). Add `Both`.

**Recommended additive work** (no ADR, no schema change):

1. **Add `NodeDestination.Both` enum value** + converter line ([NodeRoutingConfig.cs:247-272](src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs#L247-L272))

2. **Wire `NodeRoutingConfig` into `DispatchResult`** ([Models/Ai/Chat/DispatchResult.cs:37-46](src/server/api/Sprk.Bff.Api/Models/Ai/Chat/DispatchResult.cs#L37-L46)):
```csharp
public sealed record DispatchResult(
    bool Matched,
    string? PlaybookId,
    string? PlaybookName,
    double Confidence,
    OutputType OutputType,
    bool RequiresConfirmation,
    Dictionary<string, string> ExtractedParameters,
    string? TargetPage,
    NodeDestination NodeDestination = NodeDestination.Chat,  // NEW; default preserves current behavior
    string? WidgetType = null);                              // NEW; null unless workspace
```
Existing call-sites unchanged.

3. **Populate `DispatchResult.NodeDestination` in PlaybookDispatcher** — after matching a playbook, load its primary AI/DeliverOutput node and call `NodeRoutingConfig.Parse(node.ConfigJson)`. Populate the two new fields.

4. **Add Workspace/Both/FormPrefill/SideEffect cases to `PlaybookOutputHandler.HandleOutputAsync`**:
   - **Workspace**: emit `workspace.tab_open` SSE event; delegate streaming to `PlaybookExecutionEngine`; do NOT produce chat tokens
   - **Both**: same as Workspace PLUS templated ack message via existing `EmitTextResponseAsync` ("I've added a [playbookName] result to the Workspace.")
   - **FormPrefill**: no-op in handler (pre-fill flow `MatterPreFillService` / `ProjectPreFillService` is consumer; **NFR-07 forbids modifying it**)
   - **SideEffect**: emit no user-visible chat or workspace content; rely on telemetry/audit

5. **Add JSON-schema validation gate to `Deploy-Playbook.ps1`** — validate `sprk_configjson` against `node-routing-config.schema.json` at deploy time. Catches authoring errors before first invocation.

6. **CapabilityRouter dedup** (already in scope as task 042 / FR-30) — once `DispatchResult.NodeDestination` is read, "one user intent → one playbook → one destination" becomes structural.

**Templated ack format**:
```
"I've added a {playbookName} result to the Workspace."
```
Start hardcoded English. Defer per-playbook customization (e.g., `ackTemplate` JSON property on `NodeRoutingConfig`) until proven necessary.

**Sequence diagram for workspace destination** (post-fix):

```
User: "summarize this NDA"  (with NDA upload)
   ↓
PlaybookDispatcher.DispatchAsync(message, attachments, hostContext)
   ↓
Phase A/B/C (WP2) → matches "Summarize-NDA"
   ↓
Load primary node → NodeRoutingConfig.Parse(node.ConfigJson)
   → { Destination: Workspace, WidgetType: "structured-output-stream" }
   ↓
DispatchResult { NodeDestination=Workspace, WidgetType="structured-output-stream", ... }
   ↓
PlaybookOutputHandler.HandleOutputAsync — case Workspace:
   1. Emit SSE: workspace.tab_open { tabId, widgetType }
   2. Delegate to PlaybookExecutionEngine.ExecuteAsync (streaming path)
   ↓
Engine streams Structured Outputs → AnalysisChunk{type:"delta", delta:FieldDelta}
   ↓ (SSE)
Frontend: sseToPaneEventBridge → workspace.field_delta events
   ↓
PaneEventBus dispatches → StructuredOutputStreamWidget accumulates → schema-aware render
   ↓
Engine emits AnalysisChunk{type:"complete"}
   ↓
Frontend: workspace.streaming_complete → tab marked Complete

(For Both destination, add between steps 4 and 5:)
   PlaybookOutputHandler emits typing_start → token: "I've added a Summarize-NDA result to the Workspace." → typing_end
```

**Dependencies**: None blocking (additive); enables WP2 (file-aware routing only delivers value if destination metadata routes the result).

**Open questions**:
- Audit needed: PB-009/PB-012/PB-015 — are they routed correctly with their current configJson? Or do they rely on streaming-code implicit Workspace routing?
- Should `Both` default to templated ack or be customizable per-playbook?
- Validation timing: deploy-time only, or also write-time (Power Apps plugin)?

**Success criteria**:
- `summarize-document-for-workspace@v1` (or its renamed successor) routes via `DispatchResult.NodeDestination = Workspace`, not via streaming code path
- Workspace destination produces a tab + SHORT chat ack (or no chat output if destination=workspace not both)
- No regression: existing chat-default playbooks (no configJson) continue to render in chat
- Deploy script catches malformed configJson before publishing

---

### WP4 — Remove technical debt

**Problem**. Hardcoded GUIDs / dicts / capability-name strings scattered across the chat-routing layer. The user explicitly flagged this as unacceptable.

**Inventory** (full):

| Item | Location | Replacement |
|---|---|---|
| `SessionSummarizeOrchestrator.ChatSummarizePlaybookId` GUID `44285d15-...` | [SessionSummarizeOrchestrator.cs:78-79](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs#L78-L79) | PlaybookDispatcher picks the right playbook from data via vector match |
| `SoftSlashIntentToCapabilityName` dict (4 entries) | [CapabilityRouter.cs:153](src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs#L153) | `commandIntent` becomes a vector-query bias in WP2's Phase B |
| `SoftSlashSummarize/Draft/ExtractEntities/Analyze` constants | [CapabilityRouter.cs:137-140](src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs#L137-L140) | Constants removed when dict is removed |
| `TryClassifySoftSlash` method | [CapabilityRouter.cs:460](src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs#L460) | Layer 0.5 deleted (functionality moves to bias parameter) |
| `GeneralSupersetFallbackTools` empty array (Hotfix #3) | [CapabilityRouterOptions.cs:58](src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouterOptions.cs#L58) | Entire CapabilityRouter retired (see below) |
| `sprk_aicapability` table | NEVER built | **Do not build.** Functionality absorbed by `playbook-embeddings` vector match |
| `DataverseCapabilityManifestLoader` + supporting infra | `Services/Ai/Capabilities/*` | Retired or refactored to read `playbook-embeddings` |

**Larger architectural question — do we retire CapabilityRouter entirely?**

Today CapabilityRouter does two jobs:
1. Pick a capability by user message (Layer 0/0.5/1/2/3) — duplicates PlaybookDispatcher's Stage 1/2
2. Filter the tool list visible to the LLM ("router-validated subset per FR-11")

Job 1: redundant with PlaybookDispatcher; remove.

Job 2: tool filtering. The LLM today gets a filtered tool set based on the active capability. If we collapse onto PlaybookDispatcher, the tool set should come from the matched playbook's scopes (which tools the playbook declares it needs) — not from a separate "capability" abstraction.

**Recommendation**: retire `CapabilityRouter` entirely. Tool filtering becomes:
- For a matched playbook: expose tools declared by the playbook's Action scope + Tool scope
- For an unmatched conversational turn: expose the always-on conversational tools (`recall_session_file`, `get_workspace_tab_state`, `document_search`, `update_workspace_tab` when tab is agent-editable)

This is cleaner than the current "tool list filtered by capability name."

**Removed code surface**:
- `CapabilityRouter.cs` (entire class)
- `CapabilityRouterOptions.cs`
- `Layer2Options` (LLM classifier — duplicates PlaybookDispatcher Stage 2)
- `DataverseCapabilityManifestLoader.cs`
- `CapabilityManifest*.cs`
- `ICapabilityManifest.cs`, `ICapabilityManifestLoader.cs`
- `ManifestRefreshService.cs`
- `CapabilityValidator.cs`, `CapabilityValidationContext.cs`

That's substantial deletion. The risk is that other code consumes these types — full call-site audit required before removal.

**Removed frontend surface**:
- `SoftSlashRouter.SOFT_SLASH_TO_INTENT` dict ([SoftSlashRouter.ts:139-144](src/solutions/SpaarkeAi/src/components/conversation/SoftSlashRouter.ts#L139-L144))
- `SoftSlashRouter.decorateBody` still EXISTS but now adds `commandIntent` as a vector-bias hint, not a capability-routing signal. The backend treats it differently (incorporate into Phase B query) but the wire format is unchanged.
- `CommandRouter.parse()` unchanged (still parses slash + references + intents)

**Phased removal**:
- Phase 1 (additive): build new PlaybookDispatcher with Phase A/B/C; populate DispatchResult.NodeDestination; PlaybookOutputHandler handles Workspace/Both. Keep CapabilityRouter active in parallel as fallback.
- Phase 2 (cutover): once Phase 1 proves stable in UAT, route all chat turns through new dispatcher exclusively. CapabilityRouter becomes dead code.
- Phase 3 (cleanup): delete CapabilityRouter and supporting infrastructure. Remove SoftSlashIntentToCapabilityName dict.

**Dependencies**: WP2 (need replacement to be functional before removing), WP3 (PlaybookOutputHandler must handle Workspace before removal).

**Open questions**:
- Are there test fixtures asserting CapabilityRouter behavior that would need rewriting?
- Does the existing `CapabilityRouter` provide telemetry signals other code consumes (e.g., `context.decision_made` events)? Need to preserve telemetry through the new dispatcher.
- Frontend `SoftSlashRouter.decorateBody` wire format: keep `commandIntent` field name or rename to `intentHint` to clarify it's no longer a capability lookup?

**Success criteria**:
- No hardcoded GUID for chat-summarize playbook in code
- No hardcoded slash→capability dict
- One playbook-matching code path (PlaybookDispatcher)
- Cleaner DI graph (fewer registrations)
- BFF publish size delta — flag if material

---

### WP5 — Layered Stateful Memory (6-tier architecture)

> **v3.2 reframing per Insights + Cosmos audit (2026-06-19)**: WP5 is **mostly WIRE + REFACTOR, not BUILD**. Most of the memory infrastructure already exists in code: `MatterMemoryService`, `PinnedContextRepository`, `PinnedContextRecallService`, `MemoryCompositionService`, `SummarizationCompressionService`, `PromptBudgetTracker`, all 5 Cosmos containers provisioned via Bicep, Insights Engine's `spaarke-insights-index` ready for T5 retrieval. The actual NEW work: (1) verify `MatterMemoryService` is actually invoked per FR-45 (potentially a one-call wiring fix), (2) layered context cards in prompt assembly, (3) new tool surface (recall/list/manifest/retrieve/write/promote/get_preferences/get_org_templates), (4) upload-time enrichment (classify + summarize + manifest), (5) `sprk_aichatmessage` decision (fix or retire), (6) recently-discussed signal. The 6-tier model stays as the architectural model; the build scope is significantly smaller than v3 suggested.

**Problem**. The agent honestly tells users it doesn't remember their uploaded files turn-to-turn ("I don't currently have the uploaded document's text fully in my context memory"). The user explicitly flagged this as architecturally unacceptable. Pillar 6b workspace-write tools exist but reach the LLM only via Layer 3 fallback (unreliable). Workspace state is in the prompt only when CapabilityRouter routes the right capability — which is broken. Additionally, "memory" today is conflated — uploaded files, prior summaries, matter-level facts, user preferences, and audit trail all collapse into ad-hoc handling. **Per user feedback, "memory" must be modeled as 6 distinct tiers, each with its own lifecycle, storage, tools, and access pattern. Otherwise it becomes an uncontrolled prompt blob.**

**Current state** (verified):
- `ChatSession.UploadedFiles` carries file METADATA (id, name, MIME, size, AI Search doc IDs) — persists in Redis + Cosmos
- `ChatMessageAttachment.TextContent` carries file TEXT but "lives for exactly one user turn" ([ChatEndpoints.cs:2649](src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs#L2649))
- Conversation history has only user typed text + agent prior responses — NOT file content
- Per-turn system prompt has "Session Files: N files: {names}" suffix ([SprkChatAgentFactory.cs:907](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L907)) — knows files exist, not their content
- File chunks searchable via `RagService` against `spaarke-session-files` AI Search index
- Workspace state injection (Pillar 9 `getAgentVisibleState`) implemented per-widget but only delivered when capability routing succeeds
- `MatterMemoryService` exists (R5/R6 Pillar 7) — wires into chat-agent system prompt assembly partially; backed by Cosmos `memory` container
- `sprk_userpreferences` Dataverse entity exists for user/org-level preferences
- ADR-015 binding: all logged trace events tier-1 safe (no user message content)

**Architectural pattern**: layered memory + JIT retrieval. Each tier is explicit, tooled, and has clear promotion/recall semantics.

#### WP5.1 — The 6 Memory Tiers

| Tier | What it holds | Lifetime | Storage | Existing Spaarke infra | Tools |
|---|---|---|---|---|---|
| **1. Working context** | Active turn: persona, tools, instructions, current task, active files cards, active matter card, workspace digest, sliding conversation history, user message | Per-turn (rebuilt each LLM call) | Composed in memory from other tiers | `SprkChatAgentFactory.CreateAgentAsync` system prompt assembly | n/a (it IS the prompt) |
| **2. Session memory** | Uploaded files (metadata + summaries + manifests), generated summaries from playbook executions, user decisions in conversation, pending plans | Session lifetime (24h Redis + Cosmos) | Redis hot tier + Cosmos warm tier | `ChatSession.UploadedFiles`, `ChatSession.Messages`, `PendingPlanManager` | `list_session_files`, `get_file_manifest`, `recall_session_file`, `write_session_memory` |
| **3. Matter memory** | Durable matter-level facts: prior work product, concessions, deadlines, strategy notes, counsel rules for this matter | Matter lifetime (durable until explicit close) | Cosmos `memory` container | `MatterMemoryService` (partial) | `retrieve_matter_memory`, `promote_to_matter_memory` |
| **4. User/org memory** | User preferences (writing style, summary length), org templates (firm's preferred NDA template), user-pinned playbooks, outside counsel rules | User/org lifetime (durable) | Dataverse `sprk_userpreferences` + custom rows | `sprk_userpreferences` (partial) | `get_user_preferences`, `get_org_templates` |
| **5. Retrieval memory** | Searchable indexes — session file chunks, playbook embeddings, document graph, knowledge RAG indexes | Index-managed | Azure AI Search: `spaarke-session-files`, `playbook-embeddings`, `sprk-search-index`, `nda-standards-index`, etc. | Multiple production indexes | `document_search`, `knowledge_retrieval`, `recall_session_file (mode='section')` |
| **6. Audit memory** | What content was used, what was retrieved, what was cited, decisions made, tool calls | Retention-policy-managed (per ADR-015) | App Insights + Cosmos `auditevents` | `context.*` events, `decision_made` telemetry | (read-only; UI surfaces in Context pane execution-trace widget) |

**Critical separation rules**:
- Tier 2 (session) NEVER auto-promotes to Tier 3 (matter) without explicit user action or `promote_to_matter_memory` tool with `approvalRequired: true`
- Tier 4 (user/org) is bound by ADR-013 facade boundary — chat code does NOT directly mutate
- Tier 6 (audit) is append-only; never read by the agent for "memory" purposes (only by UI for transparency)
- Tier 1 composes from tiers 2-5 each turn; nothing else writes to it

#### WP5.1a — Leverage Map (audit-grounded; what exists vs what to build)

> **v3.3 reframing per [`architecture/stateful-chat-architecture.md`](architecture/stateful-chat-architecture.md) §5 (2026-06-21)**: The Insights-Engine "leverage" assertions in the table below + WP5.1c were honest at the time of the 2026-06-19 audit but architecture §5 applied critical scrutiny and downgraded most of them. Binding rules — these OVERRIDE the table below for any conflict:
>
> - **`spaarke-insights-index`** → DO NOT use for chat memory (§5.2.1 — categorical mismatch: Insights holds derived knowledge artifacts; chat memory needs file chunks)
> - **`MultiIndexComposer.Merge`** → DO NOT use for memory composition (§5.2.2 — different semantics: knowledge-tier blending vs time-dependent layered injection)
> - **`InsightsOrchestrator`** → DO NOT use for chat memory orchestration (§5.2.3 — different domain)
> - **`EvidenceSufficiencyNode`** → DO NOT use for memory recall; build clean recall-tool error returns instead (§5.2.4)
> - **`GroundingVerifyNode`** → inspiration only for citation verification; build memory-specific verification (§5.2.5)
> - **`InsightsPlaybookExecutionCache`** → it's a standard Redis-TTL pattern; adopt the pattern, do NOT frame as "Insights borrow" (§5.1.2)
> - **`sprk_matter.sprk_performancesummary`** → DO NOT TOUCH (§5.2.6 — Insights-owned 7-dim diagnostic; race conditions + shape conflict)
> - **Pattern-level reuse only** (§5.3): versioned envelopes with citations, Redis hot-tier with TTL, write-through to compliance store
>
> The table below is preserved for historical traceability; the architecture-doc bindings above override any "reusable?" Y in the table.

Per Insights Engine + R6 CosmosDB + sprk_aichatmessage cross-check (2026-06-19):

| Tier | What exists | Where | Reusable? | Gap / recommendation |
|---|---|---|---|---|
| **T1 Working context** | `MemoryCompositionService` (R6 task 067) composes 4 layers; `PromptBudgetTracker` enforces 8K | In-memory; per-request | **YES — refactor, don't rebuild** | Verify `MatterMemoryService.ToSystemPromptFragmentAsync` actually invoked (FR-45). Unify with `IPlaybookChatContextProvider` (not parallel pipelines). Add layered context cards (replace 1-line file summaries). |
| **T2 Session memory** | `ChatSession.Messages` + Redis (24h sliding) + Cosmos `sessions` (90d TTL); `StoredSession.Summary` + `WidgetStates` + `Tabs` fields; `SessionSummarizationService` triggers at 25 msgs / 8K | Redis hot + Cosmos warm + Dataverse `sprk_aichatmessage` (broken stub) | **YES — extend `StoredSession` in-place** | Pattern precedent: `SaveTabsAsync` (R6 task 051) added new fields. Same pattern for file summaries + manifests. Decide `sprk_aichatmessage` fate. |
| **T3 Matter memory** | `MatterMemoryService` (Cosmos `memory`, doc id `{tenantId}_{matterId}`); ETag concurrency; ToSystemPromptFragmentAsync renders ≤500 tokens; **Insights** `sprk_matter.sprk_performancesummary` (7-dim diagnostic — DO NOT touch) | Cosmos `memory` for facts; Dataverse longtext for diagnostic | **YES — but VERIFY WIRING** | No `sprk_matterfacts` entity exists by design — Cosmos `memory` is the store. Question: is FR-45 wiring complete? |
| **T4 User/org memory** | `PinnedContextItem` (Cosmos `memory`, pin-type: `user-preference` / `system-rule` / `matter-fact`); `sprk_userpreferences` Dataverse entity (consumed by SmartTodo + LegalWorkspace) | Cosmos for pins; Dataverse for preferences | **YES — integrate, do not duplicate** | Clarify boundary: pins = lightweight user-authored anchors; preferences = durable settings. Don't merge. |
| **T5 Retrieval memory** | `spaarke-insights-index` (Observations + Precedents, 3072-dim, `preFilter` mode); `spaarke-files-index`; `spaarke-rag-references`; `spaarke-records-index`; `spaarke-knowledge-index-v2`; `discovery-index`; `spaarke-session-files`; `PinnedContextRecallService` (similarity over pins) | Multiple Azure AI Search indexes | **YES — wrap with retrieval tools** | Don't add new index. Thin `MemoryRetrievalTool` facade over selected indexes. Decision: which indexes are in-scope for chat memory recall vs Insights/RAG. |
| **T6 Audit memory** | `AuditLogService` (Cosmos `audit`, append-only, immutable policy, no TTL); App Insights telemetry; `context.*` PaneEventBus events | Cosmos `audit` (permanent) + App Insights | **YES — read-only display** | Already complete. Memory needs only a READ for execution-trace widget. No new storage. |

#### WP5.1b — Architecture Diagram

```
                ┌─────────────────────────────────────────────────────┐
                │      MemoryCompositionService (T1 — composer)        │
                │      [recent | compressed-mid | retrieved | pinned]  │
                │      (R6 task 067 — exists; verify FR-45 wiring)     │
                └────┬───────────┬──────────────┬───────────┬─────────┘
                     │           │              │           │
              ┌──────▼──────┐ ┌──▼─────────┐ ┌─▼──────────┐ ▼ (always)
              │ Cosmos      │ │ Summari-   │ │ Embedding  │ Pinned (FR-42)
              │ sessions    │ │ zation     │ │ similarity │ never drops
              │ (T2 — warm) │ │ Compression│ │ Recall (T5)│
              │ 90d TTL     │ │ Service    │ │            │
              └──────┬──────┘ │ (R6 task   │ └─┬──────────┘
                     │        │  064)      │   │
        Redis hot ───┘        └────────────┘   │
        24h sliding (T2)                       │
                                               ▼
   ┌───────────────────────────────────────────────────────────────────┐
   │              Cosmos "memory" container (already provisioned)      │
   │              PK: /tenantId  (90d TTL; doc-type discriminator)     │
   │   ┌────────────────┐  ┌──────────────────┐  ┌──────────────────┐ │
   │   │ MatterMemory   │  │ PinnedContext    │  │ WorkspaceTabs    │ │
   │   │ (T3)           │  │ Item (T4)        │  │ (R6 task 051)    │ │
   │   │ id={t}_{m}     │  │ pinned-context_  │  │ workspace-tab_   │ │
   │   │ R6 task 068    │  │ R6 task 065      │  │                  │ │
   │   └────────────────┘  └──────────────────┘  └──────────────────┘ │
   └───────────────────────────────────────────────────────────────────┘
                                       │
              ┌────────────────────────┴──────────────────────────┐
              ▼                                                    ▼
    ┌──────────────────────────────────┐    ┌─────────────────────────────────┐
    │ Azure AI Search (T5 — retrieval) │    │ Dataverse                       │
    │ - spaarke-insights-index         │    │ - sprk_userpreferences (T4)     │
    │ - spaarke-files-index            │    │ - sprk_matter.sprk_performance  │
    │ - spaarke-session-files          │    │     summary (Insights-only —    │
    │ - spaarke-rag-references         │    │     DO NOT TOUCH)               │
    │ - spaarke-records-index          │    │ - sprk_aichatmessage (broken    │
    │ - spaarke-knowledge-index-v2     │    │     stub; fix or retire)        │
    │ - discovery-index                │    └─────────────────────────────────┘
    │                                  │
    │ (Wrap with MemoryRetrievalTool;  │
    │  do NOT add new index)           │
    └──────────────────────────────────┘
                                       │
                                       ▼
                          ┌────────────────────────────┐
                          │ Cosmos "audit" container   │
                          │ (T6 — append-only,         │
                          │ immutable, permanent)      │
                          │ + App Insights telemetry   │
                          │ (Read-only for display)    │
                          └────────────────────────────┘

Tool surface (new — wrappers over existing storage):
  Session  T2:  list_session_files  recall_session_file  get_file_manifest  write_session_memory
  Matter   T3:  retrieve_matter_memory  promote_to_matter_memory
  User/Org T4:  get_user_preferences  get_org_templates  (read-only)
  Retrieval T5: (existing) document_search  knowledge_retrieval  +  recall_session_file(mode='section')
  Audit    T6:  (read-only; UI surfaces via execution-trace widget)
```

**Key insight**: the build is **tool wrappers + prompt-assembly refactor + upload-time enrichment**, NOT new storage. The 5 Cosmos containers + 7+ AI Search indexes + Dataverse entities are all in place.

#### WP5.1c — Existing Components to Leverage

> **v3.3 reframing per `architecture/stateful-chat-architecture.md` §5 (2026-06-21)**: The "From Insights Engine" sub-list below was downgraded by architecture §5 critical scrutiny. Binding rules:
> - `MultiIndexComposer.Merge` → DO NOT use for memory composition (architecture §5.2.2; use `MemoryCompositionService` instead)
> - `Envelope pattern` → PATTERN-LEVEL reuse only (architecture §5.3); do NOT share the Insights `InsightArtifact` type
> - `InsightsPlaybookExecutionCache` → standard Redis-TTL pattern; adopt the pattern, not the Insights borrow framing
> - `spaarke-insights-index` with `preFilter` → DO NOT use for chat memory (architecture §5.2.1)
>
> The "From R6 Pillar 7" + "From R6 R2 carryover" sub-lists below remain valid leverage — those ARE the canonical chat-memory components per architecture §11.1.

From Insights Engine:
- **`MultiIndexComposer.Merge`** — merges multi-tier knowledge blocks under a single output. Already in use by `AiAnalysisNodeExecutor` + `IndexRetrieveNode`. **v3.3: DO NOT use for memory composition** — different semantics (knowledge-tier blending vs time-dependent layered injection). Use `MemoryCompositionService` (architecture §5.2.2).
- **Envelope pattern** `{schemaVersion, body, citations[], generatedAt, playbookName, tenantId, dimensions[]}` — standard provenance shape. **v3.3: PATTERN-LEVEL reuse only** — do not share the Insights `InsightArtifact` type (architecture §5.3 + §5.2 §4.5).
- **InsightsPlaybookExecutionCache** — Redis hot-tier pattern transferable to memory cache. **v3.3: PATTERN-LEVEL only** — it's the standard distributed-cache pattern (Redis with TTL). Adopt the pattern; don't frame as "Insights borrow" (architecture §5.1.2 + §5.3).
- **`spaarke-insights-index`** with `preFilter` over `tenantId + artifactType + predicate + scope` — already supports tenant-scoped vector retrieval. Wrap, don't duplicate. **v3.3: DO NOT use for chat memory** — Insights index holds derived knowledge artifacts (Observations, Precedents), not chat memory. Use `spaarke-session-files`/`spaarke-files-index` for chat T5 (architecture §5.2.1).

From R6 Pillar 7:
- All 7 memory services (`MatterMemoryService`, `PinnedContextRepository`, `PinnedContextRecallService`, `MemoryCompositionService`, `SummarizationCompressionService`, `PromptBudgetTracker`, `ManagePinnedContextHandler`) are built + DI-registered.
- The 4 React components + 4 endpoints for Pinned Memory UI (Q7 expansion) — shipped.

From R6 R2 carryover:
- `SessionPersistenceService` (Cosmos `sessions` warm-tier) — extend with new fields per `SaveTabsAsync` pattern.
- `SessionRestoreService` + `SessionSummarizationService` — already integrated.

#### WP5.1d — Technical Debt Cleanup (per audit)

If WP5 supersedes any existing code:
1. **`ChatDataverseRepository` placeholder methods** — 5 `Task.CompletedTask` no-ops. Decision:
   - (a) Complete them with real FetchXML support per AIPL-054 plan, OR
   - (b) Formally retire them; rename interface to `IChatAuditRepository` (write-only contract); confirm Cosmos `audit` container is sole reader for compliance queries
2. **`sprk_aichatmessage` retention policy** — currently no TTL; listed as "transient chat" in migration exclude. Either add lifecycle management OR document Cosmos+Redis are authoritative and Dataverse is best-effort.
3. **Verify `MatterMemoryService` FR-45 integration** — if spec promise is unmet, it's a wiring fix not a new feature.

#### WP5.1e — Open Questions Surfaced by Audit

1. **FR-45 wiring**: Is `MatterMemoryService.ToSystemPromptFragmentAsync` actually called from `SprkChatAgentFactory`? Or is it dormant code? Inspect `notes/task-068-evidence.md` + `SprkChatAgentFactory` to confirm. **HIGH priority** — could be a 1-line fix.
2. **Turn-level vectorisation (FR-43)**: Where does `ChatHistoryManager.AddMessageAsync` write embeddings? Spec says "vectorize each turn at write time" — not visible in code path read.
3. **`sprk_aichatmessage` future**: Owner decision needed (fix vs retire) — see WP5.1d #1.
4. **`spaarke-insights-index` reuse for memory recall**: Mix-purpose with Insights queries OR dedicated chat-memory index? Recommendation: reuse with new `artifactType="memory"` discriminator if pursued.
5. **Pin vs preference boundary** (T4): Confirm deliberate separation; document in WP5.
6. **Insights `sprk_performancesummary` boundary**: WP5 must NOT extend this field's semantics — confirm with owner.

#### WP5.2 — Layered Context Card (replaces 1-line summary)

Per user feedback, "1-line summaries every turn is too thin for Spaarke." The per-file static-prefix entry becomes a **structured context card**:

```
## File: contract-acme.pdf
- ID: file-a1b2
- Type: PDF, 32 pages
- Uploaded: 4 turns ago by current user
- Classified document type: NDA (confidence 0.94, classified at upload)
- Precomputed summary (NOT AUTHORITATIVE — verify with citations for legal precision):
    Mutual NDA between Acme and Globex dated 2026-04, 2-year term, mutual non-solicit clause.
- Detected sections: Parties; Definition of Confidential Info; Exclusions; Term; Permitted Use; Remedies; Red Flags
- Tables detected: 2 (Definitions; Exhibits)
- Citations referenced in this conversation: §4 ¶2 (turn 3); §6 (turn 5)
- Recently discussed: yes (last 3 turns)
- Recall: recall_session_file({fileId: "file-a1b2", purpose: "...", scope: "relevant_sections", requireCitations: true})
```

Per-card cost ~150-250 tokens × up to 10 files = 1500-2500 tokens. Within static-prefix budget. Files beyond 10 collapse to `list_session_files` summary line.

**Trust framing — load-bearing**: the system prompt explicitly tells the agent:
> "Precomputed summaries in the file cards above are upload-time approximations. For any question requiring legal precision (specific clauses, exact wording, dates, parties, dollar amounts), you MUST call recall_session_file with requireCitations: true and verify against the source. Never quote from the precomputed summary as if it were the source."

This is the user's "do not trust them as authoritative" directive made explicit in the agent's instructions.

#### WP5.3 — Expanded Tool Surface

The tool surface expands to cover each memory tier explicitly:

**Session memory tools (Tier 2)**:

```typescript
list_session_files()
→ Array<{
    fileId: string,
    fileName: string,
    contentType: string,
    sizeBytes: number,
    pageCount?: number,
    uploadedAt: Date,
    uploadedBy: string,
    classifiedDocType: string,
    classifiedConfidence: number,
    precomputedSummary: string,  // marked NOT authoritative
    sections: string[],
    recentlyDiscussed: boolean,
    citationsReferenced: Array<{turn: number, location: string}>
  }>
```

```typescript
get_file_manifest(fileId)
→ {
    fileName: string,
    pageCount: number,
    sections: Array<{name: string, startPage: number, endPage: number}>,
    tables: Array<{name: string, page: number}>,
    citations: number,
    language: string,
    extractedText: { chars: number, tokens: number },
    classifiedDocType: string,
    classifiedConfidence: number,
    sha256: string
  }
```

```typescript
recall_session_file({
  fileId: string,
  purpose: "answer_question" | "quote" | "compare" | "summarize" | "extract_dates" | "verify",
  query: string,
  scope: "summary" | "relevant_sections" | "full_text" | "tables" | "citations",
  maxTokens?: number,
  requireCitations: true  // default true
})
→ {
    content: string,
    citations: Array<{
      page: number,
      paragraph?: number,
      section?: string,
      text: string  // the verbatim excerpt
    }>,
    scope_truncated: boolean,
    truncation_reason?: string
  }
```

The `purpose` enum biases the retrieval strategy:
- `"answer_question"`: balanced section retrieval (top-3 chunks)
- `"quote"`: verbatim section retrieval with strict citation enforcement
- `"compare"`: side-by-side multi-file retrieval (if fileIds array passed)
- `"summarize"`: orientation-first; pulls summary + critical sections
- `"extract_dates"`: deterministic date-pattern matching across sections (handler-backed)
- `"verify"`: targeted retrieval to verify a specific claim; returns "supported" / "contradicted" / "no evidence"

The `requireCitations: true` default is load-bearing — every recall produces verifiable citations. Agent instructions ban quoting recall content without citations.

```typescript
write_session_memory({
  fact: string,           // the fact to remember
  source: string,         // citation or "user-stated"
  confidence: number,     // 0.0-1.0 self-assessed
  category?: "decision" | "preference" | "open-question" | "assumption"
})
→ { factId: string }
```

Session-scoped; cleared at session end. Used when user says "remember that we're treating this as one-way NDA" or agent records "user declined to share counterparty details."

**Matter memory tools (Tier 3)**:

```typescript
retrieve_matter_memory(matterId, query)
→ Array<{
    factId: string,
    fact: string,
    source: string,
    confidence: number,
    category: string,
    createdAt: Date,
    createdBy: string,
    sessionId?: string  // session of origin if promoted from session memory
  }>
```

```typescript
promote_to_matter_memory({
  fact: string,
  source: string,
  approvalRequired: boolean  // if true, queue for user approval before durable write
})
→ {
    factId: string,
    status: "written" | "pending_approval" | "rejected"
  }
```

Approval-gating: `approvalRequired: true` produces a UI prompt the user must accept. Matter memory is durable across sessions; promotion is a deliberate user action, not an agent-side write.

**User/org memory tools (Tier 4)**:

```typescript
get_user_preferences()
→ {
    writingStyle: "concise" | "detailed" | "narrative",
    summaryLength: "short" | "medium" | "long",
    citationStyle: "inline" | "footnote" | "bluebook",
    pinnedPlaybooks: string[]
  }

get_org_templates(category)
→ Array<{ templateId, name, type, content }>
```

Tier 4 tools are READ-ONLY from the chat agent. User/org preferences are mutated through the user-settings UI (out of scope here).

**Retrieval memory tools (Tier 5)** — mostly existing:

- `document_search(query, filters?)` — existing, unchanged
- `knowledge_retrieval(scopeId, query)` — existing, unchanged
- `recall_session_file(mode='section')` overlaps with this tier

**Audit memory** is read-only from the agent's perspective; the Context pane execution-trace widget (Pillar 6c) renders it for users.

#### WP5.4 — Per-Turn Prompt Structure

```
=== STATIC PREFIX (cacheable; ~6K tokens) ===

# Persona (800 tok)
You are Spaarke's legal AI assistant. ...
[CRITICAL trust framing: "Precomputed summaries in file cards below are upload-time
approximations. For legal precision, call recall_session_file with requireCitations: true
and verify against source. Never quote precomputed summaries as authoritative."]

# Available tools (1500 tok — router-filtered + always-on memory tools)
[per-tier tool descriptions; see WP5.3]

# Current workspace (700 tok)
[tab list + per-tab digest; full state via get_workspace_tab_state]

# Active matter context (600 tok)
Matter: Acme v. Globex (matter-id-xyz)
- Type: M&A
- Status: due diligence phase
- Key facts from matter memory: [top-5 most-relevant facts, retrieved via retrieve_matter_memory]
Use retrieve_matter_memory for additional facts.

# Session files (1800 tok max — layered context cards)
[per-file structured context cards; up to 10 files; overflow → list_session_files line]

# User preferences (200 tok)
[writingStyle, summaryLength, citationStyle from get_user_preferences cached snapshot]

# Reserved padding (400 tok)

=== DYNAMIC SUFFIX (not cached; ~5-6K tokens) ===

# Recent conversation (last 5-8 turns; older summarized)

# Tool outputs from this turn (if multi-step)

# Current user message
{user message}
```

Total per-turn target: ~11-12K input tokens. Cache discount applies to the static prefix (~6K) which dominates cost over multi-turn conversations.

#### WP5.5 — Upload-Time Pipeline (richer than v1)

When a user uploads a file:
1. Existing: client-side text extraction (PDF.js / mammoth.js)
2. Existing: chunk + embed + index into `spaarke-session-files`
3. **NEW**: classify document type via the WP2 Phase A/B mechanism (one classification call)
4. **NEW**: precompute 1-paragraph summary via gpt-4o-mini (~$0.0001/file)
5. **NEW**: extract structural manifest (sections, tables, page count, language)
6. **NEW**: persist enriched `ChatSessionFile` with summary + manifest + docType + confidence
7. Existing: persist to Redis + Cosmos

Total added latency: ~2-3s for the classify+summarize+manifest steps; runs in parallel with existing chunking/embedding.

**Important**: the precomputed summary is stored, surfaced in the layered context card, and explicitly framed as not authoritative. Citation-bearing recall is the source of truth.

#### WP5.6 — Workspace-Write Reliability (Pillar 6b)

Adopt **Harvey / Claude-Artifacts targeted-edit pattern** for `update_workspace_tab`, `send_workspace_artifact`, `close_workspace_tab`:

- LLM produces a structured edit op (`{tabId, edit: {old, new}}` or `{tabId, action: "delete-section", target}`)
- Deterministic server applies it via PaneEventBus workspace channel
- LLM verifies by calling `get_workspace_tab_state(tabId)` recall
- Avoids "regenerate the whole thing" tax
- Dovetails with Pillar 9's `getAgentVisibleState()`

These tools become always-on when ANY tab is marked agent-editable. No capability routing required. The "agent-editable" flag lives on the workspace tab metadata (extension to `WorkspaceWidgetRegistry`).

#### WP5.7 — Token Budget Reallocation

Per user Q3 — budget is per-section, not a hard global rule; efficient + cost-conscious but not at expense of accuracy.

| Section | Static prefix tokens | Notes |
|---|---|---|
| Persona + instructions + trust framing | 800 | Identity + tone + non-authoritative warning |
| Tool definitions (filtered + always-on memory tools) | 1500 | Expanded for 6-tier memory tools |
| Workspace digest (tab list + types + 1-line status) | 700 | Pillar 9 lightweight always-on |
| Active matter context | 600 | Top-5 matter-memory facts (relevance-ranked) |
| Session file context cards (up to 10 files × ~200 tok) | 1800 | Layered cards per WP5.2 |
| User preferences snapshot | 200 | Cached from `sprk_userpreferences` |
| Reserved padding | 400 | Buffer for 128-token cache increments |
| **Total static** | **6000** | Cacheable after first turn |

| Section | Dynamic suffix tokens | Notes |
|---|---|---|
| Chat history (last N turns + summarization) | 2500-3500 | Sliding window |
| Tool outputs from this turn | 1500-2500 | Recall results fill here |
| User message | 200-500 | The "now" |
| **Total dynamic** | **~5000** | Per-turn |

Per-turn total: ~11K input. Goes higher when multiple recall tool calls happen (each recall result ~1-2K tokens). All within GPT-4o's 128K window; far under the effective-attention ~8K-12K mid-context.

**Dependencies**: New schema additions on `sprk_analysisplaybook` (from WP1.5) + new entities for matter memory promotions (if matter memory table doesn't exist). Tool surface expansion blocks no other WP. The 6-tier model can land incrementally — Tier 2 (session memory) is the highest-value first delivery; Tiers 3-4 fold in over subsequent phases.

**Open questions**:
- **Tier 3 (matter memory)**: does Spaarke already have a `sprk_matterfacts` or equivalent durable store, or do we add one? The `MatterMemoryService` exists in code but the persistence schema needs confirmation.
- **Promotion approval UX**: where in the UI does the user see/approve a matter-memory promotion request? (Probably a Context pane notification with accept/reject buttons.)
- **User preferences shape**: today `sprk_userpreferences` exists; does it have writingStyle / summaryLength / citationStyle fields, or are those to be added?
- **Per-turn recompose vs cache invalidation**: the layered context cards change between turns (recently-discussed flag flips). How does that interact with prompt caching?
- Q4 deferred (Foundry-pattern long-term memory) → **resolved per user: not in scope**

**Success criteria**:
- "Do you have the document?" → agent answers: "Yes, contract-acme.pdf is in this session. The card above is an approximation — what specific question can I verify?"
- "What was the term of the non-solicit?" → agent calls `recall_session_file({fileId, purpose: 'answer_question', scope: 'relevant_sections', query: 'non-solicit term length', requireCitations: true})` → answers WITH citation
- "Remember that the user prefers concise summaries" → agent calls `write_session_memory` + optionally `promote_to_matter_memory` with approval
- "What did we conclude on indemnification last time?" → agent calls `retrieve_matter_memory(matterId, "indemnification conclusion")`
- "Make the workspace summary shorter" → agent calls `update_workspace_tab` with targeted edit
- Prompt-cache hit rate > 70% on multi-turn conversations (allowing for context card changes)
- Agent NEVER quotes precomputed summary as authoritative — quotes always cite recall output

---

### WP6 — Expand @v1 playbooks + author specialized playbooks

**Problem**. The R5/R6 chat-Summarize playbooks (`summarize-document-for-chat@v1`, `summarize-document-for-workspace@v1`) are **intentionally minimal** — single-node, single-action, no Skills/Knowledge/Tool composition. The user is right that specialized playbooks (Summarize-NDA, Summarize-Patent, Extract-Invoice) need richer composition to differentiate.

**Current state — corrected understanding** (A4 findings):

1. **Three execution paths exist**:
   - **Path 1**: Node-based (canonical for new) — `PlaybookOrchestrationService.ExecuteAsync` walks the node graph, composes Skill + Knowledge via `$ref` resolution in [AiAnalysisNodeExecutor.cs:442-499](src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs#L442-L499)
   - **Path 2**: Legacy `scopes.{actions|skills|knowledge|tools}` arrays — DEPRECATED, only consumed by `AnalysisOrchestrationService.cs:943-1033` with explicit deprecation warning
   - **Path 3**: Chat-Summarize bypass — `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` ([line 185](src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs#L185)) bypasses BOTH; does NOT consult ResolvedScopes; uses streaming Structured Outputs

2. **Legacy `scopes.{...}` arrays are deprecated**. None of the 5 `*.playbook.json` files in the repo use them — all declare `scopes: { actions: [], skills: [], knowledge: [], tools: [] }`. PB-001/PB-002/PB-009 seed catalog (which DOES populate these arrays) goes through deprecated code.

3. **The modern composition surface is JPS `$ref` inside the action's `sprk_systemprompt`**:
```json
"systemPrompt": "{ \"$schema\": \"jps-v1\", \"role\": \"...\", \"scopes\": { \"skills\": [{ \"$ref\": \"skill:NDA Review\" }], \"knowledge\": [{ \"$ref\": \"knowledge:NDA Standards\" }] }, \"instructions\": [...], \"output\": { ... } }"
```
Resolved at runtime by `JpsRefResolver` → `IScopeResolverService.GetSkillByNameAsync` + `GetKnowledgeByNameAsync` → appended into the prompt before LLM call.

4. **The chat-Summarize path (Path 3) does NOT call the JPS `$ref` resolver**. This is the architectural gap — specialized chat playbooks can't compose Skill/Knowledge via the JPS path UNLESS the engine is extended.

**The actual gap for specialized playbooks** is one of:

| Option | Mechanism | Implication |
|---|---|---|
| (a) **Inline composition** — embed all NDA-specific intelligence directly in `SUM-NDA@v1.systemPrompt` text (no $ref) | One action JSON file with all the prompt content | Simplest; loses the "compose existing skills/knowledge" leverage; harder to keep DRY across specializations |
| (b) **Extend Path 3 to resolve JPS `$ref`** — make `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` call `JpsRefResolver` like Path 1 does | Engine extension | Right answer; modest engine work; unlocks Skill/Knowledge composition for all chat-Summarize playbooks |
| (c) **Route chat-Summarize through Path 1** instead of Path 3 | Major refactor; lose per-field SSE streaming via `IncrementalJsonParser` | Wrong tradeoff — streaming UX is core to chat-Summarize |

**Recommendation**: **(b)**. Extend Path 3 to invoke `JpsRefResolver` before the LLM call. Then specialized playbooks (Summarize-NDA, Summarize-Patent, Extract-Invoice) compose existing Skills + Knowledge primitives via `$ref` like Insights playbooks do.

**Streaming preservation requirement (per Q7 resolution)**: This engine extension MUST preserve the existing streaming UX. The `IncrementalJsonParser` + `StreamStructuredCompletionAsync` flow that emits `FieldDelta` SSE events per top-level field stays unchanged. The `$ref` resolution happens BEFORE the LLM call (during prompt assembly) — adds ~100-300ms of Dataverse lookup latency at turn start but does NOT affect token-stream behavior. The implementation pattern:

```csharp
// In PlaybookExecutionEngine.ExecuteChatSummarizeAsync, BEFORE the LLM call:
var actionSystemPrompt = action.SystemPrompt;
if (JpsRefResolver.HasRefs(actionSystemPrompt))
{
    actionSystemPrompt = await _jpsRefResolver.ResolveAsync(
        actionSystemPrompt,
        scopeResolver: _scopeResolverService,
        cancellationToken);
}
// continues unchanged: StreamStructuredCompletionAsync(messages, schema, ...)
```

This keeps Path 3 streaming-first while gaining the composition surface. **Q7 RESOLVED: stays in this project; do not fork to separate spike.**

**Existing primitives available** (from A4 inventory):

**Skills** (from `scripts/seed-data/skills.json`):

| ID | Name | Best for |
|---|---|---|
| SKL-001 | Contract Analysis | General contract review |
| **SKL-003** | **NDA Review** | **Summarize-NDA (8-section pattern)** |
| SKL-004 | Lease Review | Summarize-Lease |
| SKL-005 | Employment Contract | Summarize-Employment |
| SKL-006 | SLA Analysis | Summarize-SLA |
| SKL-007 | Compliance Check | Cross-border overlays |
| SKL-008 | Executive Summary | General high-level summarization |
| SKL-009 | Risk Assessment | Risk flags for one-sided terms |
| SKL-002 | Invoice Processing | Extract-Invoice |

**Knowledge** (from `scripts/seed-data/knowledge.json`):

| ID | Name | Best for |
|---|---|---|
| **KNW-006** | **NDA Standards** (RAG index `nda-standards-index`) | **Summarize-NDA grounding** |
| KNW-005 | Defined Terms (inline) | All contract types |
| KNW-007 | Lease Standards | Summarize-Lease |
| KNW-008 | Employment Standards | Summarize-Employment |
| KNW-009 | SLA Benchmarks | Summarize-SLA |
| KNW-004 | Risk Categories | Risk-aware playbooks |
| KNW-001 | Standard Contract Terms | General contracts |

**Handlers** (for Tool scope; top candidates per A4):

| Handler | What it does | NDA value |
|---|---|---|
| **ClauseAnalyzerHandler** | LLM clause structuring with span validation | ★★★ — break NDA into clause hierarchy |
| **EntityExtractorHandler** | LLM-assisted NER with code-validated normalization | ★★★ — parties, dates, jurisdictions |
| **DateExtractorHandler** | Pure-deterministic date + ISO-8601 normalization | ★★★ — term, survival period, effective date |
| **KnowledgeRetrievalHandler** | RAG retrieval from tenant knowledge index | ★★★ — pull NDA Standards inline |
| **RiskDetectorHandler** | LLM risk identification with deterministic severity scoring | ★★ — flag unusual provisions |
| **VerifyCitationsHandler** | LLM citation verification | ★★ — verify cited statutes |

**Proposed Summarize-NDA playbook sketch** (Option A — single-node, JPS `$ref` composition):

```json
{
  "actions": [
    {
      "actionCode": "SUM-NDA@v1",
      "name": "Summarize NDA",
      "actionTypeName": "Summarize",
      "actionType": 0,
      "systemPrompt": "{ \"$schema\": \"jps-v1\", \"role\": \"NDA summarization specialist\", \"scopes\": { \"skills\": [{ \"$ref\": \"skill:NDA Review\" }], \"knowledge\": [{ \"$ref\": \"knowledge:NDA Standards\" }, { \"$ref\": \"knowledge:Defined Terms\" }] }, \"instructions\": [\"Identify parties + direction (one-way / mutual)\", \"Extract Confidential Information definition\", \"List Exclusions\", \"Identify Term + Survival\", \"Identify Permitted Use\", \"Identify Remedies\", \"Flag unusual provisions (Red Flags)\"], \"output\": { /* 7-section NDA summary schema */ } }",
      "outputSchema": {
        "type": "object",
        "additionalProperties": false,
        "required": ["parties", "definition", "exclusions", "term", "permittedUse", "remedies", "redFlags"],
        "properties": {
          "parties": {
            "type": "object",
            "required": ["disclosingParty", "receivingParty", "direction"],
            "properties": {
              "disclosingParty": { "type": "string" },
              "receivingParty": { "type": "string" },
              "direction": { "enum": ["one-way", "mutual"] }
            }
          },
          "definition": { "type": "string" },
          "exclusions": { "type": "array", "items": { "type": "string" } },
          "term": {
            "type": "object",
            "properties": {
              "durationMonths": { "type": "integer" },
              "survivalMonths": { "type": "integer" }
            }
          },
          "permittedUse": { "type": "string" },
          "remedies": { "type": "array", "items": { "type": "string" } },
          "redFlags": {
            "type": "array",
            "items": {
              "type": "object",
              "required": ["clause", "concern", "severity"],
              "properties": {
                "clause": { "type": "string" },
                "concern": { "type": "string" },
                "severity": { "enum": ["low", "medium", "high"] }
              }
            }
          }
        }
      }
    }
  ],
  "playbook": {
    "name": "summarize-nda-for-chat@v1",
    "description": "Produces a structured 7-section summary of an uploaded NDA: parties + direction, confidentiality definition, exclusions, term + survival, permitted use, remedies, red flags. Renders to the Workspace pane.",
    "isSystemPlaybook": true,
    "sprk_playbooktype": 0,
    "sprk_configjson": { "mode": "chat-summarize", "cacheTtlSeconds": 0 },
    "scopes": { "actions": [], "skills": [], "knowledge": [], "tools": [] }
  },
  "nodes": [
    {
      "name": "summarize",
      "actionCode": "SUM-NDA@v1",
      "actionType": 0,
      "outputVariable": "summary",
      "configJson": {
        "sessionIdFrom": "parameters.sessionId",
        "tenantIdFrom": "parameters.tenantId",
        "fileIdsFrom": "parameters.fileIds",
        "destination": "workspace",
        "widgetType": "structured-output-stream"
      }
    }
  ]
}
```

**Recommended first 3 specialized playbooks** (per Q6 — create them, plus a note explaining seed-data files):

| Playbook code | Action code | Skill `$ref` | Knowledge `$ref` |
|---|---|---|---|
| `summarize-nda` | `summarize-nda` | NDA Review (SKL-003 exists) | NDA Standards (KNW-006 exists), Defined Terms (KNW-005 exists) |
| `summarize-patent` | `summarize-patent` | (new SKL — Patent Review needed) | (new KNW — Patent Standards needed) |
| `extract-invoice` | `extract-invoice` | Invoice Processing (SKL-002 exists) | (none — pure extraction) |

> **Naming convention (v3.1)**: kebab-case stable codes; **NO `@v1` suffix** (per user directive — bad hygiene for product records). Versioning handled separately, not in identifier. See §1.7.

> **What are `scripts/seed-data/skills.json` and `knowledge.json`?** They're JSON catalog files in the repo defining the production `sprk_analysisskill` and `sprk_analysisknowledge` Dataverse rows. The deploy script reads them and upserts rows by code (SKL-003, KNW-006, etc.). When a JPS `$ref` like `"skill:NDA Review"` resolves at runtime, it looks up the corresponding Dataverse row that was seeded from these files. So SKL-003 + KNW-006 are real production data primitives we can reference, not theoretical. New skills/knowledge for Patent etc. would be authored as new entries in these catalog files and deployed.

**Reconciliation guidance (post-audit correction)**: per pre-fill consumer audit (2026-06-19), **WP6 is purely ADDITIVE for playbooks**. My earlier "reconciliation" recommendation was wrong. The audit found:

1. **`summarize-document-for-chat@v1` and `summarize-document-for-workspace@v1` are NOT redundant.** Together they implement the Q5 Re-Shaped "one action, two destinations" reference pattern. Both required for R6 FR-30 CapabilityRouter dedup. **DO NOT merge.** (See §1.5 #1 and #2.)
2. **`"Summarize New File(s)"` (`4a72f99c-...`) is NOT interchangeable with SUM-CHAT.** Different output schema (`{tldr, summary, practice areas, parties, call to action}` vs `{tldr, summary, keywords, entities}`). Different consumers (Matter ribbon + 4 wizards). **DO NOT touch.** (See §1.5 #3.)
3. **`"Document Profile"` (`18cf3cc8-...`) is the GLOBAL CHAT FALLBACK and EVERY uploaded document's profile pipeline.** **DO NOT touch — silent breakage risk.** (See §1.5 #4.)
4. **Pre-fill playbooks (`2d660cad-...` Matter, `fc343e9c-...` Project) are NFR-07 binding.** **DO NOT touch.** (See §1.5 #5-6.)
5. **PB-009/PB-012/PB-015/PB-017** have ZERO repo-grep consumers but require Dataverse-level audit before deletion. Name-collision risk is high. (See §1.5 reconciliation candidates table.)

**Corrected WP6 scope (additive only)**:

| Action | Rationale |
|---|---|
| **Author NEW specialized playbooks** (Summarize-NDA, Summarize-Patent, Extract-Invoice) | Coexist alongside untouchable production playbooks; specialized routing (WP2) prefers them for matching docs |
| **Extend `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` (Path 3) to resolve JPS `$ref`** | Enables Skill + Knowledge composition for chat-Summarize playbooks (per Q7) |
| **Verify PB-009 in Dataverse + clean its description if kept** | Per Q1; do not author SUM-NDA@v1 from scratch — use PB-009 as the canonical NDA playbook after rewrite + index |
| **Run Dataverse audit on PB-009/PB-012/PB-015/PB-017** before any deletion | Required by §1.5 reconciliation rule — repo grep is necessary but not sufficient |
| **DO NOT modify** the 6 production-bound playbooks (§1.5) | Hard constraint |
| **DO NOT modify** the SUM-CHAT@v1 action's output schema | Both chat + workspace siblings depend on it; widgets bound to it |

**Implication for index governance (WP1.5)**: the "Send to Index" UX + tracking fields apply to ALL playbooks in scope (existing + new). Untouchable playbooks still get description cleanup + indexing — that's an additive update, not a semantic change.

**Expanding existing @v1 playbooks** — value-add:
1. **Add evidence-sufficiency precheck**: if RAG returns 0 chunks, return structured Decline rather than calling LLM (mirrors `EvidenceSufficiencyNode` in Insights playbooks). Avoids garbage-summary on empty input.
2. **Bind default Persona via R6 Pillar 1**: `IScopeResolverService.ResolvePersonaForChatAsync` already exists but Path 3 (chat-Summarize) doesn't consult it. Wire it in.

**Dependencies**:
- WP3 (destination metadata must be wired to route Workspace output)
- WP1 (specialized playbooks must be indexed for vector matching)
- Path 3 extension (b) to invoke JPS `$ref` resolver

**Open questions**:
- **PB-009 "Summarize NDA"** in production was authored via Builder UI, not a `.playbook.json`. Need to inspect its actual structure before deciding "use PB-009 vs author new SUM-NDA@v1."
- **Patent Standards / Patent Review** skills + knowledge don't exist in the seed catalog. Need authoring.
- **JPS `$ref` extension to Path 3**: how invasive? Needs design walkthrough with engine maintainers.
- **Token cost of skill+knowledge concatenation** — measure prompt growth per specialization.

**Success criteria**:
- `SUM-NDA@v1` action authored with NDA-specific JPS prompt + `$ref` composition
- Playbook indexed into `playbook-embeddings` with good description
- File-aware router (WP2) matches "summarize this NDA" + NDA upload → Summarize-NDA (not generic Summarize)
- Workspace tab renders 7-section NDA summary
- Path 3 (chat-Summarize engine) resolves `$ref` and includes Skill `PromptFragment` + Knowledge `Content` in the system prompt

---

## §3 Cross-WP Architecture Review

**Dependency graph**:

```
WP1 (index @v1 playbooks)
  ↓
  ├─→ WP2 (file-aware classification) — needs indexed playbooks to match
  │     ↓
  │     └─→ WP3 (destination metadata) — file-aware matching only delivers value if dest routes correctly
  │           ↓
  │           └─→ WP4 (debt removal) — can't remove CapabilityRouter until new path is functional
  ↓
WP6 (specialized playbooks) — new playbooks need indexing (WP1) + Path 3 extension
  ↓
  └─→ (depends on WP3 for destination routing)

WP5 (stateful chat) — independent of the routing layer; can land in parallel
```

**Critical path**: WP1 → WP3 (schema) → WP2 (file-aware match using indexed playbooks routed via destination metadata) → WP4 (remove debt now that path is proven). WP6 + WP5 can land in parallel.

**Quick wins** (each ~1-2 days):
- **WP1 description rewrite** of existing @v1 + production playbooks
- **WP3.1** add `NodeDestination.Both` enum value + converter
- **WP3.2** wire `NodeRoutingConfig` into `DispatchResult`
- **WP3.3** add Workspace/Both/FormPrefill/SideEffect cases to `PlaybookOutputHandler`
- **WP5.1** add `recall_session_file` tool (without changing prompt structure)
- **WP5.2** precompute file summaries at upload + persist on `ChatSession.UploadedFiles`

**Structural changes** (each multi-day):
- **WP2** file-aware classification (Phase A/B/C in PlaybookDispatcher)
- **WP4** retire CapabilityRouter
- **WP5.3** restructure system prompt as static + dynamic with workspace digest + session file digest
- **WP6** author specialized playbooks + extend Path 3 to resolve JPS `$ref`

**Preserves (do not disturb)**:
- 11 production node executors (NFR-08)
- Pre-fill flow (NFR-07) — `MatterPreFillService`, `ProjectPreFillService`, `useAiPrefill`, 45s timeout, `$choices`-constrained output
- 4-channel PaneEventBus (NFR-05)
- 4-stage shell lifecycle (NFR-06)
- AI Public Contracts facades (ADR-013)
- Safety pipeline (NFR-13)

**Replaces**:
- `sprk_aicapability` table (never built — design abandoned)
- `CapabilityRouter` entire class (retired)
- `SoftSlashIntentToCapabilityName` dict
- Hardcoded `SessionSummarizeOrchestrator.ChatSummarizePlaybookId` GUID
- Streaming-code-implicit Workspace destination (becomes data-driven)
- Per-message file content lifetime (becomes per-session via summary + recall tool)

**Risks**:
- **WP4 dead-code audit**: CapabilityRouter is consumed by many call sites; removal requires comprehensive replacement and regression suite
- **WP5 token budget growth** from 8K → 13K per turn; cost increase if prompt cache hit rate is low
- **WP6 Path 3 extension** to invoke `$ref` resolver — engine change, requires careful review
- **WP3 backward compat** for the 7+ existing summary playbooks — audit before changing destination routing
- **WP2 telemetry needed** to validate file-aware matching accuracy before retiring CapabilityRouter fallback

---

## §4 Scope Allocation Analysis

**Three options**:

### (i) Extend R6 with a "Phase E" wave

- **Pro**: Project context is hot; team already in R6 mindset; CI/CD pipeline + PR pattern in place
- **Pro**: Quick wins (WP1 description rewrite, WP3 enum addition, WP5 recall tool) land fast
- **Con**: R6 spec doesn't mention these WPs; scope creep against NFR-03 "no new ADRs"
- **Con**: PR #401 (current hotfix PR) is open; adding 6 WPs makes the PR enormous
- **Con**: R6's nominal "convergence + finishing what was started" framing strains under "and also redesign the routing layer"

### (ii) New successor project R7 / `spaarke-chat-routing-redesign-r1`

- **Pro**: Clean separation; proper design-to-spec cycle; can introduce new ADRs cleanly
- **Pro**: R6 closes with documented known issues; R7 owns the architectural rework
- **Pro**: Easier to time-box and resource
- **Con**: Some quick wins (WP1, WP3.1, WP5.1) stall waiting for project initialization
- **Con**: User experience continues to be degraded until R7 ships

### (iii) Split — quick wins in R6 closeout, structural work in successor

- **Pro**: Best of both: ship the obvious fixes now, do the architectural rework right
- **Pro**: R6's UAT pain (broken `/summarize`, missing destination routing) gets resolved fast
- **Pro**: Successor project has cleaner scope (file-aware classification + stateful chat + CapabilityRouter retirement)
- **Con**: Two-PR coordination
- **Con**: Need clear boundaries — which WPs are "quick" vs "structural"

**Recommendation**: **(iii) Split**, with explicit guard against R6 scope creep.

> **CRITICAL USER DIRECTIVE (Q9)**: "r6 was expected to be completed and it is now being extended in this new project. THIS IS SUPER CRITICAL." R6's discipline is convergence + finishing what was started. We MUST NOT use this design as license to extend R6 indefinitely. The new architectural work goes to the successor project. R6 absorbs only what aligns with its existing convergence framing.
>
> Additionally per Q9: R6 work is broader than chat/playbook routing — Pillars 1 (persona), 4 (FK fix), 7 (memory partial), 8 (command router) are all R6 scope. The split below preserves R6's broader closeout while routing the architectural rework to the new project.

**Successor project name** (per user): `spaarke-ai-platform-chat-routing-redesign-r1`

**R6 closeout scope** (Phase E micro-wave — ONLY items that align with R6's convergence framing):
- **WP1 description rewrite** for `@v1` playbooks + the 5+ production summary playbooks (clean text; not infrastructure)
- **WP1.5 metadata audit ONLY** (catalog what's missing — defer schema additions to successor project)
- **WP3.1** add `NodeDestination.Both` enum value + converter (minor; closes FR-27 spec promise)
- **WP3.2** wire `NodeRoutingConfig` into `DispatchResult` (small DTO addition with default values; structural backward compat)
- **WP3.3** add Workspace/Both/FormPrefill/SideEffect cases to `PlaybookOutputHandler` (handler completion — aligns with Pillar 5 "Q5 re-shaped" design)
- All within current R6 NFR budget; no schema changes to production Dataverse entities; small additive code changes

**OUT of R6 closeout** (anything user-facing-architectural; goes to successor):
- WP1.5 schema additions (`sprk_lastindexedat` etc.) — schema change requires R6 confirmation trigger + cleanly belongs to successor
- WP2 file-aware classification — new functionality, not convergence
- WP4 CapabilityRouter retirement — too large + risky for R6 closeout
- WP5 (entire memory rework) — substantial new architecture; cannot be jammed into closeout
- WP6 Path 3 engine extension + specialized playbooks — new playbooks + engine change
- "Send to Index" governance UX — new Power Apps UX work

**Successor project scope** (`spaarke-ai-platform-chat-routing-redesign-r1`):
- WP1.5 full schema additions + Send-to-Index governance + tracking fields
- WP2 file-aware classification (Phase A/B/C)
- WP4 retire CapabilityRouter (full audit + single-phase cutover per Q8 — no backward compat needed)
- WP5 full 6-tier memory architecture:
  - WP5.1 Tier 1-6 separation + storage wiring
  - WP5.2 layered context cards (richer than 1-line summaries) + trust framing
  - WP5.3 expanded tool surface (recall_session_file + list_session_files + get_file_manifest + retrieve_matter_memory + write_session_memory + promote_to_matter_memory + get_user_preferences + get_org_templates)
  - WP5.4 per-turn prompt structure
  - WP5.5 upload-time pipeline (classify + summarize + manifest)
  - WP5.6 workspace-write reliability (Harvey/Artifacts targeted-edit pattern)
  - WP5.7 token budget reallocation (per-section, not hard global per Q3)
- WP6 Path 3 engine extension to resolve JPS `$ref` (streaming preserved per Q7) + author Summarize-NDA + Summarize-Patent + Extract-Invoice (per Q6); reconcile with existing summary playbooks per Q2 (do NOT break pre-fill/wizard consumers)
- Successor project carries its own design-to-spec cycle, new ADRs where warranted, dedicated PR flow

**R6 closeout includes Pillars beyond chat/playbook (per Q9 reminder)**:
- Pillar 1 (persona) status — needs verification against current code
- Pillar 4 (FK fix) status — verify the alternate-key bypass is fully removed
- Pillar 7 (memory) — partial; the deeper memory work moves to successor but Pillar 7 R6 scope should be closed
- Pillar 8 (command router) — partial; the routing-layer simplification moves to successor but Pillar 8 R6 scope should be closed
- General R6 wrap-up tasks 087-090 (vertical-slice, eval baseline, exit gate, lessons-learned)

**Timeline rough sizing** (honest):
- R6 closeout: 1-2 weeks (each WP3/WP5 sliver is 1-2 days; plus broader Pillar review)
- Successor project: 6-10 weeks (memory rework expanded significantly with 6 tiers + 8 new/expanded tools; Path 3 engine extension; file-aware routing; specialized playbook authoring; CapabilityRouter retirement) + 2-3 weeks stabilization

**PR strategy (per Q10)**:
- PR #401 (currently open) → push to merge; R6 closeout micro-wave goes in a new PR
- Successor project gets its own design-to-spec → spec → tasks → PR flow

---

## §5 Open Questions — RESOLVED (user review v1, 2026-06-19)

All 10 questions resolved. New open items surfaced during synthesis are listed below the resolutions.

| # | Question | **Resolution** |
|---|---|---|
| Q1 | PB-009 vs new SUM-NDA@v1 | **Keep PB-009, review, update to current standard.** Becomes canonical specialized NDA playbook after WP6 enrichment. **v3 refinement**: Dataverse audit needed before action — repo grep found zero production consumers but Power Automate / chat-context-mapping verification required. |
| Q2 | `summarize-document-for-workspace@v1` — keep / rewrite / delete | **v3 CORRECTION**: KEEP. The pre-fill consumer audit (2026-06-19) showed `summarize-document-for-workspace@v1` is NOT broken/redundant — it's the reference impl of the Q5 Re-Shaped "one action, two destinations" pattern, paired with the chat sibling for R6 FR-30 dedup invariant. My v2 "reconcile" recommendation was wrong. WP6 is now purely ADDITIVE for playbooks. See §1.5. |
| Q3 | Token budget restatement | **Per section, NOT a hard rule.** Be efficient + cost-conscious but not at the expense of accuracy/performance. |
| Q4 | Foundry-pattern long-term memory | **Not in scope.** |
| Q5 | `commandIntent` wire-format rename | **No backward compat needed. Rename so it aligns with purpose.** |
| Q6 | Specialized playbooks for WP6 | **Create them.** (Plus user asked what `seed-data/skills.json` + `knowledge.json` are — answered in WP6 doc; they're catalog files seeding production `sprk_analysisskill` + `sprk_analysisknowledge` Dataverse rows.) |
| Q7 | Path 3 JPS `$ref` extension | **Streaming MUST be preserved. Handle here (no separate spike, no fork to other project).** See WP6 streaming-preservation implementation pattern. |
| Q8 | WP4 cutover strategy | **Single phase. No backward compat needed.** |
| Q9 | R6 vs successor allocation | **Include in R6 what is logical and aligned (NOT the architectural rework). New design goes to NEW project, not R6.** ALSO: continue reviewing broader R6 work — it's more than chat/playbook. R6 was expected to be complete; we cannot extend it indefinitely. **SUPER CRITICAL.** |
| Q10 | PR #401 (open) — push/merge or expand | **Push/merge PR #401.** New work goes to new PR in the successor project. |

### Additional decisions captured from user feedback v1

- **Successor project name**: `spaarke-ai-platform-chat-routing-redesign-r1` (per user)
- **Playbook embeddings index governance**: full metadata audit + send-to-index process + tracking fields (added to WP1.5)
- **Memory architecture**: 6 tiers (Working / Session / Matter / User-Org / Retrieval / Audit), NOT a single "memory" blob (WP5 major rewrite)
- **Context cards**: layered structured cards per file, NOT 1-line summaries (WP5.2)
- **Trust model**: precomputed summaries are NOT authoritative; agent MUST call recall with `requireCitations: true` for legal precision (WP5.2 trust framing)
- **Expanded tool surface**: 6 memory tools instead of 1 — `recall_session_file` (expanded args) + `list_session_files` + `get_file_manifest` + `retrieve_matter_memory` + `write_session_memory` + `promote_to_matter_memory` + (read-only) `get_user_preferences` + `get_org_templates` (WP5.3)
- **Reconciliation requires audit**: before deprecating ANY existing summary playbook, audit pre-fill / wizard / workflow consumers per NFR-07 (WP6)

### Decisions captured from v2 feedback + audits

- **WP5 phasing dropped**: single project ships when ready; 6-tier model stays as architectural model but no artificial UX-value-early phasing
- **6 Untouchable Production Bindings**: §1.5 added as binding constraint (`summarize-document-for-chat@v1`, `summarize-document-for-workspace@v1`, `"Summarize New File(s)"`, `"Document Profile"`, Matter Pre-Fill, Project Pre-Fill)
- **WP6 corrected from "reconcile" to "additive"**: do not merge chat/workspace siblings; do not touch production-bound playbooks; only author NEW specialized playbooks
- **R6 closeout confirmed narrow**: Pillars 1/4/7/8 materially complete (per status audit); only tasks 089 + 090 governance remain
- **Technical debt lift paths documented**: ChatSummarizePlaybookId GUID, BuildDefaultSystemPrompt fallback, SummarizeInvocationPath, PinnedMemoryProvenanceBadge stub — all defer to successor project as natural cleanup during architectural rework
- **Dataverse audit required for PB-009/PB-012/PB-015/PB-017** before any deletion — name-collision risks documented
- **Design doc moved to new worktree** (`spaarke-wt-chat-routing-redesign-r1`); R6 notes copy superseded

### Open items surfaced during synthesis

11. **Matter memory persistence schema** — `MatterMemoryService` exists in code but persistence schema for promoted facts needs verification. Is there a `sprk_matterfacts` entity already, or do we add one? **(Successor project decision)**

12. **Promotion approval UX** — where in the UI does the user see/approve a `promote_to_matter_memory` request? Probably a Context pane notification with accept/reject buttons. **(Successor project UX)**

13. **`sprk_userpreferences` field coverage** — does it carry `writingStyle` / `summaryLength` / `citationStyle` today? Need data inspection. **(Successor project)**

14. **Per-turn recompose vs cache invalidation** — layered context cards change between turns (recently-discussed flag flips, citation counters update). How does that interact with Azure OpenAI prompt caching's stable-prefix requirement? **(Successor project — needs benchmark)**

15. ~~**Pre-fill consumer audit**~~ — **COMPLETED 2026-06-19.** See §1.5 "6 Untouchable Production Bindings". WP6 corrected to additive-only.

16. ~~**Pillar 1 / 4 / 7 / 8 R6 closeout status**~~ — **COMPLETED 2026-06-19.** See §1.6 "R6 Status". All 4 pillars materially complete; only tasks 089 + 090 governance remain.

### Newly opened items from audits

17. **Dataverse audit for PB-009/PB-012/PB-015/PB-017** — repo grep found zero production consumers but each needs Dataverse-level verification: query `sprk_aichatcontextmapping` rows; Power Automate flow audit; `sprk_analysisrun` history query. **(Required before any deletion; in successor project)** — **v3.1: per user — DON'T delete; review and remediate to fit new approach.**

18. **Name-collision audit** — document Profile/Summary and Summarize-File-vs-Summarize-New-Files. Add lint rule or naming convention to prevent silent shadowing via `useAiSummary` / `ChatContextMappingService` literal-name lookup. **(Successor project)** — **v3.1: per user — resolve via stable playbook codes (§1.7) in lieu of name/GUID lookup. Solves the collision problem structurally.**

19. **PB-009 inspect-and-update workflow** — per Q1 resolution, keep + review + update. Need to (a) inspect the actual node graph + scopes via Builder UI / Dataverse query, (b) rewrite description for embedding-friendly text, (c) extend it via JPS `$ref` to use SKL-003 + KNW-006 Knowledge sources, (d) populate `sprk_jpsmatchingmetadata` + `sprk_playbookcode = "summarize-nda"`. **(Successor project task — early)** — **v3.1: confirmed yes; playbooks need to use full scopes where helpful.**

20. **CapabilityRouter dedup tests (R6 task 042 / FR-30)** — the chat + workspace siblings have a binding co-dependency for the dedup invariant. WP4 (retire CapabilityRouter) must preserve this dedup semantics through the new PlaybookDispatcher path. Verify the dedup test suite stays green after retirement. **(Successor project — binding test)** — **v3.1: confirmed.**

### v3.1 newly opened items

21. **Complete pre-fill consumer inventory** — original audit covered 4 of the 6 user-listed wizards. Need explicit audit of: **Create Matter / Create Project / Assign Work / Summarize Files / Send Email / Document Upload Page** — confirm AI-prefill path for each, GUID/code/name hardcoding, output-schema consumer. (Dispatched 2026-06-19.)

22. **Pillars 2/3/5/6/9 R6 status** — only 1/4/7/8 audited. R6 cannot close before all 9 pillars reviewed per user reminder. (Dispatched 2026-06-19.)

23. **Insights Engine + R6 CosmosDB cross-check** — Q11 raised: `sprk_matterfacts` does not exist; `sprk_aichatmessage` exists (flexibility/performance unclear); what happened to CosmosDB aspects of R6? Need cross-reference with Insights Engine project to identify leverageable components for WP5 memory tiers. (Dispatched 2026-06-19.)

24. **WP5 architecture diagram** — deferred pending #23 findings; design after Insights Engine cross-check to avoid duplicating leverageable code. (Per user — "ensure we aren't either duplicating or if we are replacing, then remove technical debt".)

25. **Action code reform** — `@v1` suffix on actions (`SUM-CHAT@v1`) violates the same hygiene as on playbooks. Decision: tackle action-code reform in same WP as playbook-code reform OR defer to separate sub-WP. (See §1.7.4.)

26. **Send-by-Email pre-fill investigation** — not surfaced in v2 audit; user listed as a pre-fill wizard. Confirm whether this is `DocumentEmailWizard.tsx` (which uses `/by-name/Summarize New File(s)`) or a separate Email composer flow. (Part of #21.)

27. **Confirm Q9 reaffirmation**: "r6 needs full testing before close out" per user v2. The 4-pillar code audit is NOT testing — testing is separate. R6 closeout punch list expands to include: tasks 089 + 090 + **full UAT regression of all 9 pillars**. Sequencing implication: R6 testing can happen in parallel with successor project init, but R6 close-out signing happens AFTER testing pass.

### User-confirmed decisions (v3.1)

- **Playbook codes (§1.7)**: build the stable-code system; immutable; no `@v1`; resolve via `/by-code/{code}`
- **WP6 naming**: drop `@v1` from new playbooks/actions
- **§1.5 reframing**: production-bound = migrate, not untouchable
- **PB-009 etc.**: review and remediate to fit new approach; don't delete unless truly duplicative
- **R6**: do not extend scope BUT do review all deliverables (testing pass required before close)
- **Indexing**: follow recommendation (Send-to-Index + tracking fields)
- **Multi-file matching**: 3 options surfaced; user to pick primary
- **WP5 architecture diagram**: needed; defer until Insights Engine cross-check finishes
- **JPS matching metadata field**: yes, add `sprk_jpsmatchingmetadata` for improved routing quality

### User Q-resolutions (v3.1 set)

| Q | Resolution |
|---|---|
| Q11 | `sprk_matterfacts` doesn't exist. Cross-check Insights Engine. `sprk_aichatmessage` exists — flexibility/performance unclear. R6 CosmosDB status unclear. **All in flight via Audit 3.** |
| Q12 | Yes — Context pane for approval UX. **Defer detailed design.** |
| Q13 | Defer. |
| Q14 | Defer. |
| Q15 | Pre-fill consumer audit — confirm via new dispatch (Audit 1) covering all 6 wizards. |
| Q16 | R6 needs full testing before close out — adds testing pass to closeout punch list. |
| Q17 | Don't remove PB-009/PB-012/PB-015/PB-017 — review and remediate to fit new approach. |
| Q18 | Yes resolve name-collision risk — via stable playbook codes (§1.7), not just lint. |
| Q19 | Yes — playbooks need to use full scopes where helpful (JPS `$ref` composition). |
| Q20 | Yes — preserve CapabilityRouter dedup semantics in new PlaybookDispatcher path. |

---

## §6 Implementation Sequencing (for the successor project, if §4(iii) approved)

If R6 closeout absorbs the quick wins, the successor project's task graph:

**Phase 1 — Foundation (parallel)**:
- 1a. WP6 Path 3 extension: invoke `JpsRefResolver` in `PlaybookExecutionEngine.ExecuteChatSummarizeAsync`
- 1b. WP6 author Summarize-NDA action + playbook (uses existing SKL-003 + KNW-006)
- 1c. WP2 PlaybookDispatcher Phase A (per-file fingerprint)

**Phase 2 — Routing (depends on Phase 1)**:
- 2a. WP2 PlaybookDispatcher Phase B (per-file vector query with composed embedding)
- 2b. WP2 PlaybookDispatcher Phase C (reconciliation + decider)
- 2c. WP6 author second + third specialized playbook (Summarize-Patent or Summarize-Lease + Extract-Invoice)

**Phase 3 — Stateful chat (parallel with Phase 2)**:
- 3a. WP5 prompt restructure (static prefix + dynamic suffix)
- 3b. WP5 workspace digest in static prefix
- 3c. WP5 recently-discussed flag
- 3d. WP5 Pillar 6b reliability fix (workspace-write tools always available when tab is agent-editable)

**Phase 4 — Cleanup (after Phase 2 stable)**:
- 4a. WP4 Phase 1 (parallel run new dispatcher + CapabilityRouter)
- 4b. WP4 Phase 2 (cutover — all chat turns through new dispatcher)
- 4c. WP4 Phase 3 (delete CapabilityRouter + supporting infrastructure)

**Phase 5 — Exit (after Phase 4)**:
- 5a. Vertical-slice validation
- 5b. Lessons-learned
- 5c. Documentation refresh (architecture docs, ADRs if any)

---

## Appendices

### A. Code/Data Reference Index

**Routing**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` — current two-stage matcher
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingService.cs` — vector search against playbook-embeddings
- `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs` — to-be-retired layered router
- `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/DataverseCapabilityManifestLoader.cs` — to-be-retired manifest loader

**Frontend routing**
- `src/solutions/SpaarkeAi/src/components/conversation/CommandRouter.ts` — slash parser
- `src/solutions/SpaarkeAi/src/components/conversation/SoftSlashRouter.ts` — to-be-simplified decorator
- `src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts` — SSE → PaneEventBus

**Destination**
- `src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs:30-274` — destination metadata schema (correct as-is, needs `Both` enum value)
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/DispatchResult.cs:37-46` — DTO needing NodeDestination + WidgetType additions
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs:108-117` — case switch needing Workspace/Both/FormPrefill/SideEffect cases

**Execution**
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs:185` — Path 3 chat-Summarize bypass (needs JPS `$ref` extension)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs:442-499` — Path 1 JPS `$ref` resolver (canonical composition)
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs:943-1033` — deprecated legacy scopes-array path
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs:78-79` — hardcoded GUID to remove

**Chat state**
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs:72-91` — `UploadedFiles` shape
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs:134-140` — `ChatSessionFile` shape
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs:2649` — "lives for exactly one user turn" attachment lifecycle
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs:907` — current "Session Files: N files: {names}" prompt suffix

**Playbook composition**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/matter-health-single.playbook.json` — best Insights-mode exemplar (9 nodes)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-chat.playbook.json` — minimal chat-Summarize
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-workspace.playbook.json` — Workspace-destination variant
- `scripts/seed-data/skills.json` — 10 production skills incl. SKL-003 NDA Review
- `scripts/seed-data/knowledge.json` — 10 knowledge sources incl. KNW-006 NDA Standards
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/` — 25 handler classes

**Workspace state**
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` — schema-aware stream renderer
- `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` — Pillar 9 `getVisibleState` registry

### B. Research-Agent Findings

The full findings documents from agents A1-A4 are referenced in line within §2 WP deep dives. Source documents kept in agent output for traceability.

- **A1** — WP2 file classification research (researcher agent, ~3000 words, primary sources cited)
- **A2** — WP3 destination metadata research (researcher agent, ~3000 words, code-grounded with file:line)
- **A3** — WP5 stateful chat research (researcher agent, ~4000 words, competitive comparison with 8 products)
- **A4** — WP6 playbook composition research (general-purpose agent, ~4000 words, full inventory)

### C. Glossary

- **Path 1** — Node-based execution via `PlaybookOrchestrationService` + `AiAnalysisNodeExecutor` (canonical; supports JPS `$ref` resolution)
- **Path 2** — Legacy scope-array execution via `AnalysisOrchestrationService` (deprecated)
- **Path 3** — Chat-Summarize streaming bypass via `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` (current; does NOT support JPS `$ref`)
- **JPS** — JSON Prompt Schema (Spaarke's prompt format with `$ref` composition)
- **JIT retrieval** — Just-in-time content retrieval (identifiers in prompt, content via tools)
- **PlaybookDispatcher** — Current Stage 1 (vector) + Stage 2 (LLM) playbook matcher in chat endpoint
- **CapabilityRouter** — Layered router (0/0.5/1/2/3) inside chat completion that filters the tool list (to be retired per WP4)
- **NodeRoutingConfig** — JSON shape on `sprk_playbooknode.sprk_configjson` carrying `destination` + `widgetType`
