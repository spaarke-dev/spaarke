# r3 Pre-Design Context (2026-06-10)

> **Purpose**: Capture all r3-related discussion points + decisions from the 2026-06-10 session for future reference. r3 was paused; this document is the "why + what + when to resume" memo so anyone picking up r3 later — including the original author — has the full thread.
> **Authored**: 2026-06-10
> **Status of r3**: ⏸ **PAUSED** pending R6 ship
> **Companion authoritative inputs**: [`r3/design.md`](../design.md) (skeleton), [`r3/README.md`](../README.md), [`r2/PHASE-2-OUTLINE.md`](../../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md), [`audit/r3-scope-recommendations.md`](../../bff-ai-architecture-audit-r1/notes/r3-scope-recommendations.md)

---

## 1. Why this document exists

The 2026-06-10 session worked through several r3-adjacent decisions: the audit's r3 recommendations (already shipped), R6's overlap with r3 surfaces, the practical risk of r3 + R6 progressing in parallel, and the owner's pivot toward `ai-spaarke-insights-engine-widgets-r1` for the next active project. None of those decisions were captured in r3's own design.md (still a skeleton), nor in the audit r3-scope-recommendations.md (written 2026-06-04, before R6 surfaced as a deep architectural project).

When r3 eventually resumes, the design.md should be authored from this context — with the audit recommendations + this 2026-06-10 thread + R6's final shape (whatever R6 looks like at that point) — as primary inputs.

---

## 2. The PAUSE decision

### 2.1 Decision summary

r3 work is paused as of 2026-06-10 (today). The pause is owner-driven, not blocked by any specific technical dependency. The rationale:

| Reason | Detail |
|---|---|
| **R6 is reshaping consumer patterns r3 would touch** | R6 Pillar 3 introduces `IInvokePlaybookAi` (new PublicContracts facade); Pillar 5 introduces output schema as 6th Scope + schema-aware renderers; Pillars 6 + 9 reshape workspace state model + widget visibility contract. r3 Wave 2 (`InsightsIntentClassifier` ↔ `PlaybookDispatcher` reconciliation) interacts with the same surfaces. |
| **Coordinating "in-flight" R6 + r3 in parallel is impractical** | R6 was authored 2026-06-07 and is still in design — decisions are being made on the fly. r3 Wave 2 implementation against a moving R6 design would mean either (a) r3 takes assumptions that R6 invalidates or (b) r3 retunes constantly. Both cost more than the wait. |
| **Capacity better spent on visible user value** | r3 is platform hardening — important but invisible. Widget delivery (per the new `ai-spaarke-insights-engine-widgets-r1` project) is the first user-visible payoff of the r2 + R5 + audit substrate. Shipping that first validates the platform investment. |
| **r3's HIGHEST-priority item already shipped** | r3 Wave 1.1 (`NullInsightsAi` facade) was SUPERSEDED by audit Migration PR #1 (PR #351). One less open item in r3 scope. |

### 2.2 What is NOT a reason for the pause

- ❌ r3 design has fundamental flaws — no, it's sound; just timing
- ❌ The audit recommendations are wrong — no, they're load-bearing
- ❌ r3 Wave 1 cleanup items have unresolved technical risk — no, they're tractable
- ❌ The InsightsIntentClassifier or PlaybookDispatcher need to change — no, both are KEEP verdicts per audit

The pause is **strategic + coordination**, not technical.

### 2.3 Resumption trigger conditions

r3 resumes when ALL of these are true:

1. **R6 has shipped its surface-defining pillars** — specifically Pillar 3 (`IInvokePlaybookAi`), Pillar 5 (output schema scope), and Pillar 6 (workspace state model). These are the surfaces r3 Wave 2 touches. R6 Pillars 7-9 are less critical for r3 resumption.
2. **Owner direction to resume** — capacity allocation decision
3. **`spaarke-insights-engine-widgets-r1` has reached its first proof-point** (or explicitly hands off insights-engine work)

The "all three" condition prevents premature resumption while one of the three is still moving.

---

## 3. r3 ↔ R6 coordination analysis (from 2026-06-10 discussion)

### 3.1 The shared-surface map

| Surface | r3's interest | R6's interest | Conflict risk |
|---|---|---|---|
| **`InsightsIntentClassifier`** | Wave 2 reconciles with `PlaybookDispatcher` per 7-element canonical pattern; Insights becomes thin wrapper, "no playbook match → RAG fallback" branch | Pillar 3 reuses it as-is for chat-agent playbook-vs-RAG routing (R6 spec.md line 701) | **HIGH** — R6 expects classifier public surface to remain stable; r3 must not change `IInsightsIntentClassifier`'s method signatures or return shape in incompatible ways |
| **`PlaybookDispatcher`** (2-stage matching) | Wave 2 leverages as primary intent-routing mechanism | Pillar 3 wraps `IPlaybookOrchestrationService` in `IInvokePlaybookAi` facade for generic invocation | LOW — different layers; both inside Zone A per ADR-013 |
| **PublicContracts facade pattern** | Wave 1.1 NullInsightsAi (SUPERSEDED by audit PR #351) | Pillar 3 adds new `IInvokePlaybookAi` facade per audit DR-003 + DR-008 | NONE — both follow same audited pattern |
| **Cache patterns** | Wave 2 may add classifier cache | Pillar 7 uses existing `IEmbeddingCache` for memory recall | LOW — both bound by audit DR-002 canonical cache stack |
| **`forceMode` / `playbookHint`** | r3 Tier 2.3 `playbookHint` is forward-looking | R6 chat agent passes `forceMode` to assistant query | LOW — additive contract evolution |
| **Workspace widget contract** | r3 has no workspace scope | Pillar 6 + 9 define workspace widget visibility + tri-directional state | NONE — no r3 overlap |

### 3.2 The 3 specific coordination points that need explicit alignment

The audit anticipated structural conflict (REJECT generic IIntentClassifier<TResult>, KEEP InsightsIntentClassifier as canonical, conform to 7-element pattern). But the audit predated R6, so 3 coordination points specifically NEED owner-driven alignment when r3 resumes:

#### Point 1 — `InsightsIntentClassifier` public surface stability through r3 Wave 2

R6 design line 701: *"R6 reuses `InsightsIntentClassifier` for the chat-agent's playbook-vs-RAG routing decision."*

R6 ASSUMES the classifier's current API is stable. If r3 Wave 2's "no-playbook-match → RAG fallback" branch changes:
- Method signature
- Return shape (e.g., adds enum value `FallbackRoute`)
- Result type

→ R6's chat-agent consumer breaks.

**Resolution required**: r3 Wave 2 MUST preserve the existing `IInsightsIntentClassifier` public API. New behavior (RAG fallback) expressed via existing return shape, or additive enum/property only. Breaking changes require explicit coordination with R6.

#### Point 2 — `IInvokePlaybookAi` (R6 Pillar 3) vs Insights's `IPlaybookOrchestrationService` use

r3 Wave 2 leverages `PlaybookDispatcher` directly. R6 Pillar 3 wraps `IPlaybookOrchestrationService` behind a new PublicContracts facade.

If r3 Wave 2 lands first and bypasses `IInvokePlaybookAi` (because it doesn't exist yet), the Insights invocation path doesn't flow through R6's facade. This is FINE for Insights (Insights IS Zone A per ADR-013) but means R6's facade isn't the single chokepoint they wanted for cross-context invocation.

**Resolution required**: Confirm explicitly with R6 owner that Insights remaining inside Zone A and using `IPlaybookOrchestrationService` directly is acceptable. R6 spec.md already preserves this boundary, but explicit re-confirmation at resumption time avoids drift.

#### Point 3 — Index naming `playbook-embeddings` → `spaarke-playbook-index`

r3 Wave 1.5 renames the `playbook-embeddings` AI Search index to `spaarke-playbook-index` (Spaarke naming convention). R6 doesn't reference this index by name.

Risk: anyone reading R6 design and grep'ing for `playbook-embeddings` finds nothing and gets confused.

**Resolution required**: r3 Wave 1.5 lands FIRST (it's tiny — config + Bicep + reindex). After that, all references use the new name. R6 docs that mention the index by name update at next R6 working session.

---

## 4. What r3 inherited from the audit (status check)

The audit's `r3-scope-recommendations.md` (2026-06-04) made specific calls on every r3 candidate item. As of 2026-06-10, here's the inheritance status:

### 4.1 r3 Wave 1 — Architectural cleanup

| Item | Audit verdict | Status |
|---|---|---|
| **1.1 NullInsightsAi facade** | SUPERSEDED by audit Migration PR #1 (the audit shipped this work alongside 4 other Null peers + structural relocation) | ✅ DONE (PR #351 merged 2026-06-04) |
| **1.2 v1.2 contract — `spe://drive/X/item/Y` evidence-ref href** | Not in audit scope (r3-specific); ship as r3 owns | ⏸ PENDING (paused with r3) |
| **1.3 Test-fixture hygiene cleanup** | Independent; coordinate with audit Migration PR #8 author | ⏸ PENDING (paused with r3); could ship independently of R6 |
| **1.4 `InsightsActionLookupFailed` telemetry + dashboards** | Independent | ⏸ PENDING (paused with r3); could ship independently of R6 |
| **1.5 Index rename `playbook-embeddings` → `spaarke-playbook-index`** | Independent; UNBLOCKS Wave 2 | ⏸ PENDING (paused with r3); MUST land before Wave 2 |

**Net**: r3 Wave 1 originally estimated ~3.5d total; one item (1.1) shipped early via the audit. Remaining ~3d of Wave 1 cleanup is paused with r3, but technically nothing prevents Wave 1 items 1.2/1.3/1.4/1.5 from shipping independently of R6. They're paused with r3 because **owner is allocating capacity to widgets-r1, not because Wave 1 is blocked**.

### 4.2 r3 Wave 2 — `InsightsIntentClassifier` ↔ `PlaybookDispatcher` reconciliation

**Audit recommendation** (locked):

| Direction | Detail |
|---|---|
| **DO** leverage existing `PlaybookDispatcher` for playbook selection | W2 Cat 1 KEEP verdict |
| **DO** leverage existing `playbook-embeddings` / `spaarke-playbook-index` | W2 Cat 3 KEEP verdict |
| **DO** leverage `CapabilityRouter` for capability-level routing if needed | W2 Cat 1 KEEP verdict (peer pattern) |
| **DO** conform new code to **Spaarke Canonical Intent Classifier Pattern** (7 elements per [canonical-architecture-decisions.md §2.5](../../bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md)): Singleton + IOptions + ADR-032 §F.1 dual registration + JSON-schema-constrained LLM decoding + SHA-256 cache key + OTEL-instrumented latency + domain-specific result type | Binding pattern |
| **REJECT** generic `IIntentClassifier<TResult>` consolidation | Locked |
| **REJECT** new search substrate | Locked |
| **REJECT** new generic classifier service | Locked |
| **SEQUENCE** Wave 2 AFTER audit Migration PR #1 (LATENT BUG remediation) lands | Now DONE (PR #351 merged) |
| **REDUCED estimate** from original ~2-3 weeks to ~1 week (5 days) per audit-verified existence of `PlaybookDispatcher` + `spaarke-playbook-index` infrastructure | Locked |

**Net**: r3 Wave 2 is well-scoped and ready to execute. Paused per coordination decision above.

### 4.3 r3 Tier 2+ candidate items (NOT yet locked)

22 candidate items in 4 tiers per [`r2/PHASE-2-OUTLINE.md`](../../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md). When r3 resumes, owner picks which Tier 2 items become r3's later waves.

Of these, the following are interesting **with respect to widgets-r1**:

| r3 Tier 2 item | Widgets-r1 benefit | Priority assessment |
|---|---|---|
| **2.4 Actionable citations** (`citations[].action: { type, payload }`) | Workspace narrative citations could carry click-actions (open document, filter, create task). Without this, citations are display-only `href`. **HIGH leverage for widgets-r1 Phase 2 workspace narrative widget.** | Worth elevating if widgets-r1 r2 workspace narrative becomes priority |
| **2.2 Full SSE on playbook path** | Widget gets token-level streaming for playbook narratives (today: only RAG path streams). UX "live" feel | Nice-to-have; not blocking widgets-r1 |
| **2.3 `playbookHint`** | Workspace widget soft-bias to portfolio playbooks; cleaner than `forceMode=playbook` | Optional polish |
| **2.1 Bidirectional clarification** | Workspace narrative asks follow-ups: "see 5 matters with budget escalation — drill into CTRNS only?" | Future enhancement; not for v1 |

When r3 resumes, **Tier 2.4 should be elevated** if widgets-r1 has by then progressed to Phase 2 workspace narrative work.

---

## 5. What changed between r3's original scope and the resumption posture

### 5.1 SHRINKS

| Item | Why |
|---|---|
| **Wave 1.1 NullInsightsAi** | DONE (audit PR #351) |
| **Wave 1.4 telemetry** if widgets-r1 ships its own telemetry meter first | r3 Wave 1.4 can either coordinate or replace its own telemetry plans |

### 5.2 GROWS / GAINS NEW DEPENDENCIES

| Item | Why |
|---|---|
| **Wave 2 design** | Now must respect R6's `InsightsIntentClassifier` reuse pattern; this is binding |
| **Tier 2.4 actionable citations** | If widgets-r1 reaches workspace narrative phase, Tier 2.4 jumps in priority |
| **Coordination protocols** | New documentation requirement: when r3 resumes, design.md must include "Coordination with R6" section (per audit guardrail recommendation) and reciprocal in R6 |

### 5.3 STAYS THE SAME

- All Tier 1 items 1.2, 1.3, 1.4, 1.5 stay valid as scoped
- All Wave 2 audit recommendations stay binding
- All Tier 2/3/4 candidates stay candidates

---

## 6. The widgets-r1 pivot decision

### 6.1 What is widgets-r1?

`ai-spaarke-insights-engine-widgets-r1` (NEW project initiated 2026-06-10) — see [`projects/ai-spaarke-insights-engine-widgets-r1/`](../../ai-spaarke-insights-engine-widgets-r1/).

**Purpose**: surface the r2 Insights Engine substrate to end users via reusable topic/subject-scoped Insight Summary widgets. First topic Matter Health (single-mode); first record type Matter.

### 6.2 Why widgets-r1 instead of r3 right now

| Dimension | r3 cleanup | widgets-r1 |
|---|---|---|
| User-visible value | Low (platform hardening) | HIGH (sparkle icon → AI narrative on Matter record) |
| R6 dependency | HIGH (classifier surface, Pillar 3, Pillar 6) | LOW (uses existing IInsightsAi directly; designed to ship independently) |
| Validates audit + r2 investment | Indirectly | Directly (proves the whole pipeline ingest → Observations → playbook → narrative end-to-end UX) |
| Time to first user payoff | ~3-4 weeks if Wave 1 capacity available | ~4-5 weeks |
| Coordination complexity | High (3+ touchpoints with R6) | Low (1 soft dependency on R5 reuse investigation) |

### 6.3 What widgets-r1 borrows from r3 (already-shipped or shipped-via-audit)

| Borrowed from | Item |
|---|---|
| Audit Migration PR #351 | `NullInsightsAi` (r3 Wave 1.1 SUPERSEDED) |
| Audit `.claude/patterns/ai/public-contracts-facade.md` | Facade interaction pattern |
| Audit `.claude/patterns/ai/endpoint-di-symmetry.md` | DI symmetry rule |
| r2 Wave E | `IInsightsAi.AnswerQuestionAsync` + `SearchAsync` |
| r2 Wave D | `matter:GUID` multi-entity subject |
| r2 Wave F | SSE + citations[].href |

widgets-r1 is built on a stable foundation that r3 helped establish (via the audit it triggered). When r3 resumes, widgets-r1 will be a CONSUMER of any r3 Wave 1.5 index rename (cosmetic) and r3 Tier 2.4 actionable citations (functional upgrade).

---

## 7. Resumption plan (when conditions are met)

### 7.1 Step-by-step resumption sequence

When the 3 trigger conditions (§2.3) are met:

1. **Re-read this document** + audit `r3-scope-recommendations.md` + R6's final spec.md + widgets-r1 status doc
2. **Create dedicated worktree**: `spaarke-wt-ai-spaarke-insights-engine-r3` on branch `work/ai-spaarke-insights-engine-r3` (per `worktree-setup` skill convention)
3. **Author r3's design.md** (currently a skeleton) using:
   - This document as primary input
   - Audit `r3-scope-recommendations.md` as binding constraints
   - R6's final shape as coordination input
   - Owner-selected Tier 2 items from the 22-item Phase 2 outline
4. **Add explicit "Coordination with R6" section to r3 design.md** per audit guardrail recommendation:
   ```
   R6 (`projects/spaarke-ai-platform-unification-r6/`) reuses
   `InsightsIntentClassifier` for chat-agent routing per R6 design line 701.
   r3 Wave 2 reconciliation MUST preserve the public `IInsightsIntentClassifier`
   API; new behavior (RAG fallback) expressed via existing return shape or
   additive extension only. Breaking changes require explicit R6 design update.
   ```
5. **Add reciprocal section to R6 design.md / spec.md** confirming r3's reconciliation scope is internal-only
6. **Derive spec.md** via `/design-to-spec`
7. **Standard pipeline**: `/project-pipeline` → tasks → implementation

### 7.2 Sequencing within r3

Per audit's r3-scope-recommendations.md:

```
Wave 1 (cleanup, ~3d) ─→ Wave 2 (classifier reconciliation, ~5d) ─→ Tier 2 selections
       │                            │
       │                            └─ Wave 2 requires Wave 1.5 (index rename) FIRST
       └─ Wave 1.1 (NullInsightsAi) is SUPERSEDED — skip
```

### 7.3 Cross-team coordination cycles at resumption

| Team | r3 touchpoint | When to engage |
|---|---|---|
| **Insights team** | Owns InsightsIntentClassifier + PlaybookDispatcher | At resumption + before Wave 2 PR |
| **R6 team** | Reuses InsightsIntentClassifier for chat agent routing | At resumption (confirm Point 1 + Point 2 above) |
| **AIPL team** | Owns Rag-adjacent search substrate | If r3 Tier 2 items touch search |
| **SprkChat team** | Owns PlaybookDispatcher + factory-instantiation pattern | Confirm Wave 2 doesn't affect SprkChat consumption |

---

## 8. Open questions parked for resumption

The following questions arose during the 2026-06-10 discussion but did NOT need answers to lock the pause decision. They become live questions at r3 resumption:

- [ ] **Q-1**: Does R6 Pillar 6 (workspace state model) supersede r3 Wave 2 in any way? r3 Wave 2 is BFF-internal; R6 Pillar 6 is workspace-side. Likely independent but confirm at resumption.
- [ ] **Q-2**: Should r3 Tier 2.4 (actionable citations) be **promoted to Wave 2** if widgets-r1 has reached workspace narrative phase by then? Depends on widgets-r1 progress + owner priority.
- [ ] **Q-3**: Should r3 Wave 1.5 (index rename) ship as a one-off PR ASAP — independent of the full r3 pause — to remove cosmetic naming drift? It's tiny and useful to anyone reading the audit / R6 / widgets-r1 docs. **Tentatively: not now (capacity)**; revisit if owner wants the cosmetic cleanup before r3 fully resumes.
- [ ] **Q-4**: Should r3 own the "Spaarke Canonical Intent Classifier Pattern" pattern doc (1 of the 8 canonical patterns the audit deferred)? Could ship as part of Wave 2 or be authored by audit follow-on phase.
- [ ] **Q-5**: When R6's `IInvokePlaybookAi` facade ships, does anything in r3's Wave 1 cleanup or Wave 2 reconciliation become trivially simpler? Re-evaluate r3 scope at resumption.

---

## 9. Cross-references

| Topic | Document |
|---|---|
| Audit recommendations for r3 (binding) | [`bff-ai-architecture-audit-r1/notes/r3-scope-recommendations.md`](../../bff-ai-architecture-audit-r1/notes/r3-scope-recommendations.md) |
| Audit canonical-architecture decisions | [`bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md`](../../bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md) |
| Phase 2 candidate items (22 items, 4 tiers) | [`ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md`](../../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md) |
| r2 lessons learned | [`ai-spaarke-insights-engine-r2/notes/lessons-learned.md`](../../ai-spaarke-insights-engine-r2/notes/lessons-learned.md) |
| r3 design skeleton (to be authored at resumption) | [`r3/design.md`](../design.md) |
| r3 README | [`r3/README.md`](../README.md) |
| R6 project | [`spaarke-ai-platform-unification-r6/`](../../spaarke-ai-platform-unification-r6/) |
| widgets-r1 project (pivoted to) | [`ai-spaarke-insights-engine-widgets-r1/`](../../ai-spaarke-insights-engine-widgets-r1/) |
| Spaarke Public-Contracts Facade pattern | [`.claude/patterns/ai/public-contracts-facade.md`](../../../.claude/patterns/ai/public-contracts-facade.md) |
| Endpoint↔DI Symmetry Rule | [`.claude/patterns/ai/endpoint-di-symmetry.md`](../../../.claude/patterns/ai/endpoint-di-symmetry.md) |

---

## 10. Document maintenance

When r3 resumes:

1. **Update r3 design.md** using this document as primary input
2. **Move this document** to `notes/` as historical context (don't delete — preserve the pause rationale for future audit/review)
3. **Add a closing addendum** to this document noting "resumed YYYY-MM-DD, see design.md for forward direction"

---

*Authored 2026-06-10 in the `spaarke-wt-ai-spaarke-insights-engine-r2` worktree (which retains broader cross-project context for r3 + audit + widgets reasoning). r3 dedicated worktree to be created at resumption.*
