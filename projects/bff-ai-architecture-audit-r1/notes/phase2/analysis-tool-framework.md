# Phase 2.5 Mini-Audit — Category 10: Tool Framework Duality (TRIALITY)

> **Authored by**: Phase 2.5 Sub-Agent L
> **Pinned to**: commit `357e6936`; ZERO drift at HEAD
> **Authorization**: Path B mini-audit per owner direction 2026-06-04
> **Scope boundary**: tool framework duality only; out-of-scope = tool content/signatures, consumer migrations
> **Headline**: Inventory's "2 parallel surfaces" framing is WRONG. Empirical = **3 coexisting surfaces**. NEW LATENT BUG candidate discovered.

---

## §1 Phase 1 baseline (verbatim from inventory §2.10 + §7.7)

Inventory §2.10 stated: "Multiple parallel 'tool' surfaces: `Chat/Tools/` 10 AIFunction tools + `Tools/` 5 IAiToolHandler impls... They overlap semantically." Inventory §7.7 asked: "Are they intended to coexist or is one slated for retirement?"

**Empirical reproduction overturns the framing.**

---

## §2 Empirical reproduction at HEAD

### §2.1 — 3 surfaces, not 2

| # | Surface | Base abstraction | Files | Context | DI strategy |
|---|---|---|---|---|---|
| **1** | `Services/Ai/Chat/Tools/` | `AIFunction` via `AIFunctionFactory.Create` | **12** (inventory said 10) | Chat agent function-calling loop (`IChatClient.UseFunctionInvocation()`) | NOT DI-registered — factory-instantiated per session by `SprkChatAgentFactory` |
| **2** | `IAiToolHandler` (`ToolName` + `ExecuteAsync(ToolParameters) → PlaybookToolResult`) | Sprk custom | **4** (inventory said 5; correction in §2.4) | Playbook workflow handler — concrete-type injection in jobs + metadata enumeration | DI-registered manually per module (FinanceModule + CommunicationModule, **inconsistent**) |
| **3** | `IAnalysisToolHandler` (`HandlerId` + `Metadata` + `Validate` + `ExecuteAsync(ToolExecutionContext, AnalysisTool) → ToolResult`) | Sprk custom | **4** (`SummaryHandler`, `SemanticSearchToolHandler`, `DocumentClassifierHandler`, `GenericAnalysisHandler`) | Analysis pipeline / playbook nodes via `IToolHandlerRegistry.GetHandler(handlerId)` | Assembly-scan auto-discovery via `AddToolHandlersFromAssembly` |
| (3b) | `IStreamingAnalysisToolHandler : IAnalysisToolHandler` | Opt-in streaming sub-interface | **1** (`GenericAnalysisHandler`) | Same as #3, per-token SSE | Same as #3 |

### §2.2 — `Chat/Tools/` count is 12, not 10

Empirical glob: `AnalysisExecutionTools`, `AnalysisQueryTools`, `CodeInterpreterTools`, `CompareDocumentsTool`, `DataverseQueryTools`, `DocumentSearchTools`, `KnowledgeRetrievalTools`, `LegalResearchTools`, `TextRefinementTools`, `VerifyCitationsTool`, `WebSearchTools`, `WorkingDocumentTools`. All confirmed `AIFunction`-pattern; none DI-registered.

### §2.3 — `Services/Ai/Tools/` is MIXED, not homogeneous

| File | Interface | Surface |
|---|---|---|
| `DataverseUpdateToolHandler.cs` | `IAiToolHandler` | Surface 2 |
| `SendCommunicationToolHandler.cs` | `IAiToolHandler` | Surface 2 |
| `SummaryHandler.cs` | `IAnalysisToolHandler` | **Surface 3** |
| `DocumentClassifierHandler.cs` | `IAnalysisToolHandler` | **Surface 3** |
| `SemanticSearchToolHandler.cs` | `IAnalysisToolHandler` | **Surface 3** |
| `PromptContextHelper.cs` | (helper class) | NOT a handler |

Inventory §2.10's "5 IAiToolHandler impls" framing is wrong on TWO counts: actual `IAiToolHandler` count is **4** (not 5), and `Tools/` is **mixed** (2 + 3 + 1 helper).

### §2.4 — Full `IAiToolHandler` impl roster (4 total)

| Impl | Location | DI | Production consumer |
|---|---|---|---|
| `FinancialCalculationToolHandler` | `Services/Finance/Tools/` | **Dual** concrete + interface @ `FinanceModule.cs:170-171` | `InvoiceExtractionJobHandler.cs:248` — **concrete-type** injection (bypasses interface) |
| `InvoiceExtractionToolHandler` | `Services/Finance/Tools/` | Interface only @ `FinanceModule.cs:181` | **NONE** — `ExecuteAsync` never called |
| `DataverseUpdateToolHandler` | `Services/Ai/Tools/` | Interface only @ `FinanceModule.cs:176` | **NONE** — `ExecuteAsync` never called |
| `SendCommunicationToolHandler` | `Services/Ai/Tools/` | **Concrete singleton ONLY** @ `CommunicationModule.cs:30` (**NO interface forwarding** — won't appear in `IEnumerable<IAiToolHandler>`) | **NONE** — `ExecuteAsync` never called |

**Three latent issues**:
1. `FinancialCalculationToolHandler` consumer bypasses `IAiToolHandler` abstraction (concrete-type injection)
2. `SendCommunicationToolHandler` registration is INCONSISTENT (concrete-only, no forwarding — won't even appear in the metadata enumeration the dual-registration pattern was designed for)
3. **3 of 4 `IAiToolHandler` impls have ZERO behavioral consumers** at HEAD — reachable ONLY via `/api/ai/tools/handlers` metadata listing

### §2.5 — `IAnalysisToolHandler` impls (4 total, all active)

`GenericAnalysisHandler` (assembly-scan), `SummaryHandler`, `DocumentClassifierHandler`, `SemanticSearchToolHandler` — all consumed via `IToolHandlerRegistry` by `AnalysisOrchestrationService`, `AppOnlyAnalysisService`, `AiAnalysisNodeExecutor`, `HandlerEndpoints`. **Surface 3 is materially active and central.**

### §2.6 — `AIFunction` Chat/Tools/ wiring

All 12 factory-instantiated per session by `SprkChatAgentFactory.ResolveTools()` (lines 604-1030). Per AIPL-053 design comment lines 553-555: "Tool classes are instantiated directly (not resolved from DI)... avoids registering them in the DI container (ADR-010)". Per-turn capability-gated injection (AIPU2-061) + per-tool error isolation (AIPU2-063).

### §2.7 — `AddToolFramework` DI audit

`ToolFrameworkExtensions.AddToolFramework`:
- Uses older `services.Configure<T>()` pattern (W4 §2.4 reinforcement)
- Assembly-scans ONLY for `IAnalysisToolHandler` (Surface 3)
- Does NOT auto-discover `IAiToolHandler` (Surface 2)
- Registers `IToolHandlerRegistry` as Scoped

Gate at `AnalysisServicesModule.cs:575-590`:
- `Enabled=false` branch registers `IToolHandlerRegistry` BUT no handlers → registry returns null for every lookup. **Semantic gap.**
- Gate is INSIDE the compound-AI-ON branch — see §4.6.

### §2.8 — Semantic-overlap audit: `AnalysisQueryTools` (S1) vs `SummaryHandler` (S3)

| Axis | `AnalysisQueryTools` | `SummaryHandler` |
|---|---|---|
| Operation | READ existing persisted analyses | WRITE production of new analysis |
| Caller | Chat LLM function-calling loop | Analysis pipeline (deterministic playbook step) |
| Input | `string documentId` | `ToolExecutionContext` + `AnalysisTool` |
| Output | `string` (text for LLM) | `ToolResult` (persisted to Dataverse) |

**Verdict**: NO semantic overlap. Shared "analysis" substring refers to different things (existing record vs in-flight production). Inventory's framing is wrong. REJECT consolidation.

### §2.9 — `DataverseQueryTools` (S1) vs `DataverseUpdateToolHandler` (S2)

READ vs WRITE — domain boundary. REJECT consolidation. (Note: `DataverseUpdateToolHandler` is orphan — DELETE candidate.)

### §2.10 — `DocumentSearchTools` (S1) vs `SemanticSearchToolHandler` (S3)

The truest "semantic overlap" pair. But same logic as W2 Cat 3 search-substrate REJECT: different consumers (chat vs analysis), different security posture, different output shape (citations + SSE vs structured ToolResult). REJECT consolidation.

### §2.11 — No 4th surface missed

Greps for `IPlaybookTool`, `IAgentTool`, `IChatTool` etc. return nothing. **3 surfaces is complete.**

---

## §3 Per-surface decision table

| # | Surface | Verdict | Reason |
|---|---|---|---|
| **1** | `Chat/Tools/` (12 × `AIFunction`) | **KEEP** | Distinct execution context. Per-session captured state DI can't supply. Microsoft Agent Framework future-roadmap canonical layer. |
| **2** | `IAiToolHandler` (4 impls; 3 orphan) | **DOWNSIZE + RECONSIDER** | Surface justified, but 3 of 4 impls have ZERO behavioral consumers. ~285 LOC DELETE candidate pending team confirmation. Align registration patterns (SendCommunicationToolHandler inconsistency). |
| **3** | `IAnalysisToolHandler` (+ streaming) | **KEEP — canonical** | Heavy active consumption. Configuration-driven HandlerClass field is load-bearing. Reference impl: `GenericAnalysisHandler`. |

**No CONSOLIDATE, no RETIRE-ONE verdicts.** Three surfaces serve genuinely different execution contexts.

---

## §4 Cross-cutting findings

### §4.1 — TRIALITY, not duality

The inventory's "2 parallel surfaces" framing collapses three distinct contexts. Applies the W2+W3+W4 framing: shapes/contexts/contracts genuinely differ. REJECT-CONSOLIDATION consistent with universal Phase 2 verdict.

### §4.2 — LATENT issue: `Services/Ai/Tools/` directory misclassified

Directory name does not indicate surface. Future audits will repeat inventory's error. RECOMMEND (LOW priority): split into per-surface directories OR add README. ADR-bullet only.

### §4.3 — LATENT issue: 3 of 4 `IAiToolHandler` impls orphan

`InvoiceExtractionToolHandler`, `DataverseUpdateToolHandler`, `SendCommunicationToolHandler` have NO production `ExecuteAsync` callers. Reachable only via `/api/ai/tools/handlers` metadata for PCF dropdown (AIPL-036). Cross-reference: `IOutputOrchestratorService` may have superseded `DataverseUpdateToolHandler`; `CommunicationService` direct usage may have superseded `SendCommunicationToolHandler`. Cross-team confirmation needed (Finance + Communication, per Q-003). **~285 LOC DELETE candidate**.

### §4.4 — LATENT issue: `FinancialCalculationToolHandler` bypasses abstraction

`InvoiceExtractionJobHandler.cs:32` injects concrete type, not `IAiToolHandler`. The interface forwarding at `FinanceModule.cs:171` exists ONLY for metadata enumeration. The interface adds NO ABSTRACTION VALUE here. KEEP for metadata-enumeration backward-compat, but document.

### §4.5 — Endpoint↔DI Symmetry Rule applied retroactively

- **Surface 1**: symmetric ✓ — chat agent gated unconditionally, no latent bug
- **Surface 2**: semi-asymmetric, **LOW risk** — registered unconditionally, consumers unconditional, no AI ctor deps
- **Surface 3**: symmetric BUT subtle gap — see §4.6

### §4.6 — 🚨 POTENTIAL SECOND LATENT BUG (HIGH priority): `IToolHandlerRegistry` under compound-AI-OFF

**Hypothesis**:
- `IToolHandlerRegistry` registered ONLY in `AddToolFramework` (line 575-590)
- `AddToolFramework` called ONLY in compound-AI-ON branch (line 53)
- `AddNullObjectsForCompoundOff` does NOT register `IToolHandlerRegistry`
- `HandlerEndpoints` map UNCONDITIONALLY (`MapHandlerEndpoints` at line 17) and inject `IToolHandlerRegistry` (lines 128 + 182)

**If confirmed**: under compound-AI-OFF, GET `/api/ai/handlers` → `InvalidOperationException("Unable to resolve service for type IToolHandlerRegistry")` → 500 instead of 503. **EXACTLY the W4 §4.5 LATENT BUG pattern.**

**Recommended remediation** (Phase 4):
- Option A: move `HandlerEndpoints` mapping behind compound-AI gate (symmetric)
- Option B: register `NullToolHandlerRegistry` peer in `AddNullObjectsForCompoundOff` (ADR-032 P3 Fail-Fast, analogous to `NullRagService`)

**This is the SECOND §F.1 LATENT BUG candidate surfaced by the audit.** Flagged for owner review at Phase 4 — verification needed before locking.

### §4.7 — `ToolFrameworkOptions` pattern alignment

Uses older `services.Configure<T>()`, no DataAnnotations, no `ValidateOnStart()`. Align with W4 Layer 8 canonical pattern. LOW priority cosmetic.

---

## §5 Canonical naming candidates (Q-004 surfaced not locked)

| Surface | Recommended canonical name |
|---|---|
| 1 | **Spaarke Canonical Chat Agent Tool Pattern** (factory-instantiated + per-session-state-captured + capability-gated) |
| 2 | **Spaarke Playbook Tool Handler Pattern** (degraded to pattern pending DOWNSIZE; analogous to Lookup pattern §2.2) |
| 3 | **Spaarke Canonical Analysis Tool Handler Registry** (assembly-scan + HandlerId-keyed lookup + optional streaming sub-interface) |

Two new §2 layers to add: **Layer 9 — Chat Agent Tool Pattern** + **Layer 10 — Analysis Tool Handler Registry**. Surface 2 sits as pattern-level (no canonical layer).

---

## §6 Drift report

**ZERO file drift** on load-bearing files vs snapshot `357e6936`. Pinned snapshot still valid for Cat 10. No new tool impls discovered at HEAD.

---

## §7 Open questions for owner review (Q-002)

1. **§4.6 LATENT BUG verification (HIGH)**: dispatch verification sub-agent or empirically test compound-AI-OFF + probe `/api/ai/handlers`. If confirmed, joins W4 §4.5 remediation pack.
2. **Surface 2 DOWNSIZE confirmation (cross-team, per Q-003)**: confirm orphan status of 3 impls with Finance + Communication. ~285 LOC DELETE adds to bundled DELETE PR (revised total ~2285 LOC).
3. **`SendCommunicationToolHandler` registration inconsistency**: (a) add interface forwarding for consistency, OR (b) DELETE if confirmed orphan.
4. **`Services/Ai/Tools/` directory misclassification**: low priority; ADR-bullet only.
5. **`ToolFrameworkOptions` pattern alignment**: cosmetic; defer to code-quality batch.
6. **Canonical naming locks** (§5): owner-lock Layer 9 + Layer 10 + Playbook-Tool-Handler-Pattern names.
7. **Inventory correction PR — 4 items**:
   - `Chat/Tools/` count 10 → **12**
   - `Tools/` is **MIXED** (2 IAiToolHandler + 3 IAnalysisToolHandler + 1 helper), NOT "5 IAiToolHandler"
   - `IAiToolHandler` total impls: **4** (not 5)
   - "Two parallel surfaces" → **"Three coexisting surfaces"** (4 with streaming sub-interface)

---

## §8 ADR candidates (Q-005 bullets only)

| # | Candidate | Priority |
|---|---|---|
| L-1 | **Three-Surface Tool Framework Pattern** — codifies three distinct surfaces + explicit REJECT-CONSOLIDATION clause | **HIGH** (prevents re-litigation) |
| L-2 | **Chat-Tool Factory-Instantiation Pattern** — codifies AIPL-053 / AIPU2-061 / AIPU2-063 lineage | MEDIUM |
| L-3 | **Analysis Tool Handler Registry Pattern** — codifies assembly-scan + HandlerId-keyed lookup + streaming sub-interface | MEDIUM |
| L-4 | **`IAiToolHandler` Orphan-Impl Mitigation Convention** — DELETE-or-explicit-attribute rule | MEDIUM |
| L-5 | **`IToolHandlerRegistry` Symmetry Rule application (LATENT BUG candidate)** — pending §4.6 verification; Option A/B framework | **HIGH** (pending §7-1) |
| L-6 | **Tool-Surface Directory Naming Rule** — no mixed-surface directories | LOW |

**Total: 6 ADR candidates** (audit total shifts 34 → 40).

---

# Sub-Agent L Final Status Report

1. **Status**: COMPLETED
2. **Output file path**: `projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-tool-framework.md`
3. **Surfaces analyzed**: 3 distinct (4 with streaming sub-interface). Inventory §2.10 "2 parallel surfaces" framing empirically WRONG.
4. **Decision distribution**: KEEP × 2 / DOWNSIZE × 1 / CONSOLIDATE × 0 / RETIRE-ONE × 0
5. **Drift findings**: ZERO code drift; 4 inventory enumeration corrections.
6. **Headline verdict**: **COEXIST (intentional)** — three distinct execution contexts, three distinct contracts, three distinct DI strategies. REJECT consolidation. **HOWEVER 3 latent issues surfaced including a SECOND HIGH-priority LATENT BUG candidate.**
7. **Open questions**: 7 items in §7
8. **Recommendations for canonical-architecture-decisions.md**: 2 new §2 layers (9 + 10) + 1 §3 row + 6 ADR candidates (totals shift 34 → 40); §4 add LATENT BUG #2 candidate; bundled DELETE PR revised ~2000 → ~2285 LOC pending team confirmation
