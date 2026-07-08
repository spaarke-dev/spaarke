# ADR Review vs the Greenfield Approach

> **Status**: v1.1 — **APPROVED by operator 2026-07-05** (recommendations
> A-1..A-5 accepted). A-2 (ADR-037 rescind) APPLIED same day — concise + full
> ADR amended, INDEX updated. A-1 (ADR-013 amendment) and A-4/A-5 (new
> ADR-039/040) queued alongside canonical doc v0.4. A-3 refreshes batched to
> Step 3 doc tasks.
> **Method**: every AI-relevant ADR read against GREENFIELD-CONCEPTUAL-DESIGN.md
> v0.2 + the resolved decisions (OQ-2/OQ-4, Action+Binding, D7-D12 as amended).
> Conducted under CLAUDE.md §6.5 (ADR Conflict Resolution Protocol) — every
> "amend" verdict below is a formal path-B surfacing for operator decision.
> **Verdicts**: ✅ aligned (keep) · 🔧 amend (path B) · 🚫 rescind rule(s) ·
> ⬜ orthogonal.

---

## 0. The meta-finding (read this first)

The operator's suspicion is **half right, and the other half is more important**:

1. **Where ADRs misdirected, it was by codifying MECHANISMS, not principles.**
   Two ADRs actively steer new work toward the architecture being retired
   (§2 below) — and both were written/amended *recently* (ADR-037 June 2026;
   ADR-013's playbook-facade widening 2026-07-01, four days before the pivot).
   Each individual amendment was locally rational; collectively they entrenched
   playbook-centricity because the ADR tracked the current mechanism's shape
   rather than the invariant behind it.

2. **But the larger cause of drift was ADR ABSENCE.** The domains where the
   audit found the worst accumulation had NO ADR at all: intent/dispatch (ten
   mechanisms, zero ADRs), session-state semantics (no addressable outputs
   contract), and the capability model (Action/playbook/consumer smear).
   Where strong ADRs existed — auth (028), caching (009/014), data governance
   (015), eventing (030), kill switches (018/032) — the architecture stayed
   coherent and the greenfield keeps those layers nearly verbatim.

**Recommendation**: amend the two misdirectors (§2), refresh three stale ones
(§3), and — the highest-value action — **author 2 new principle-level ADRs**
(§4) so the greenfield's load-bearing invariants become binding constraints the
next 27 projects cannot drift past.

---

## 1. ✅ Aligned — keep as-is (the greenfield builds on these)

| ADR | Verdict notes |
|---|---|
| ADR-001 Minimal API + BackgroundService | Aligned. Loop self-hosted in BFF per revised D10. |
| ADR-004 Job contract | Aligned — jobs become Event-path capability invokers; no doc bytes in payloads unchanged. |
| ADR-008 Endpoint filters | Aligned. |
| ADR-009 / ADR-014 Redis-first + AI caching | Aligned — ledger honors versioned tenant-scoped keys; "never cache streaming tokens; cache final outcome" is exactly the ledger's write-on-complete rule. |
| ADR-015 AI data governance (3-tier) | Aligned — the session ledger maps cleanly: ledger = Tier 3 (`sessions`, user-owned, GDPR-erasable); L3 tool-chain audit entries carry identifiers/filters/counts only → Tier 2-compatible. One small addition wanted: a ledger-entry-class → tier mapping table (fold into the §4 new ADR, not an ADR-015 change). |
| ADR-016 AI cost/rate limits | Aligned — and the greenfield *extends* it in spirit: per-turn tool budget + per-user daily Event-path budget are ADR-016-shaped controls. Add both to its budget guidelines when convenient. |
| ADR-018 Feature flags — capability-boundary discipline | Aligned and genuinely helpful: "flags at product-capability boundaries" maps 1:1 onto catalog capabilities. Small future note: Binding rows' `enabled`/`environment` give *finer* per-capability disablement as DATA; appsettings flags remain the coarse kill switches. Non-urgent clarifying sentence. |
| ADR-028 Auth v2 | Aligned — BFF OBO is load-bearing for revised D10 (user-context parity argument). |
| ADR-029 Publish hygiene | Aligned — net-negative code from the migration helps the ratchet. |
| ADR-030 PaneEventBus (4 channels) | Aligned — kept verbatim (overlay C2). Greenfield's multi-surface answer IS this ADR. |
| ADR-031 Stage lifecycle | Aligned — kept (overlay C5). |
| ADR-032 Null-Object kill-switch | Aligned — the overlay's placement fixes (FinanceModule strays, ungated LinearConsumers) are ADR-032 *enforcement*, not change. |
| ADR-036 Spaarke.Scheduling | Aligned — `IScheduledJob` invokers fit "jobs invoke capabilities". |
| ADR-038 Testing strategy | Aligned — one addition wanted: classify the **golden-utterance eval suite** (overlay S4) within its KEEP categories (it is `tests/integration/contract/**`-shaped: catalog-change regression gate). One-line amendment when the suite lands. |
| ADR-002/006/007/012/019/020/021/022/026/027 | ⬜ Orthogonal or trivially aligned (UI standards, SPE, errors, versioning, solutions). |

---

## 2. 🔧 The two misdirectors — amendment required before Step 3 work starts

### 2.1 ADR-013 (AI Architecture) — amend: replace the playbook-shaped canon with capability invocation

**What stays (the valuable core)**: BFF-hosted-by-default with the four
extraction criteria; the `PublicContracts` facade boundary; no direct CRUD→AI
injection; the decision table for service-boundary questions. These rules are
correct and the greenfield obeys all of them.

**What misdirected**:
- The ADR canonizes **`IInvokePlaybookAi`** as THE invocation surface — making
  "invoke a playbook" the blessed verb every consumer wired through. Each new
  consumer that complied (Finance, Workspace, Compose…) deepened
  playbook-centricity; the **2026-07-01 amendment** widened the playbook facade
  further (document-context params) *four days before* the strategic pivot
  declared playbook-centricity the problem. The ADR was ratcheting the wrong
  invariant: it protected the boundary (right) by hardcoding the verb (wrong).
- Its embedded architecture map is stale (lists dead `Chat/Tools/*` classes,
  the retiring `AnalysisOrchestrationService`) — an agent loading this ADR today
  is pointed at the wrong code.

**Proposed amendment (path B)**:
1. The canonical facade verb becomes **capability invocation**
   (`invoke(bindingId, args)` per the Action+Binding model); `IInvokePlaybookAi`
   is grandfathered as a legacy shim over it, retired with its callers
   (overlay S3).
2. The reflection-guard test follows the new facade surface.
3. Replace the stale architecture appendix with a pointer to the canonical doc
   (per doc-discipline: ADRs carry constraints, not architecture maps that rot).
4. All boundary rules, extraction criteria, and CRUD→AI prohibitions carry over
   verbatim.

### 2.2 ADR-037 (Multi-node output composition) — amend: rescind the engine-steering rule; keep the streaming contract

**What misdirected**: ADR-037 (June 2026) invested in NEW engine machinery
(`NodeType.DeliverComposite`, `ActionType 42`) and — critically — contains a
forward-steering rule: *"any future workspace playbook authored after Phase 5R
uses `DeliverComposite` by default."* Under the OQ-2 resolution (engine frozen;
no new capability lands on it) this rule **actively directs new work onto the
frozen representation** and 🚫 must be rescinded. Note also its flagship
migration (118R) is still blocked-undeployed on the `sprk_nodetype` option-set
gap — the mandate never actually shipped a second consumer.

**What is genuinely good and survives**: the **section-name-keyed streaming
contract** (`section_started/section_data/section_completed` keyed by NAME, not
schema position) solved a real fragility (5 coordination points → 2) and is
**transport, not engine** — a `coded` composite workflow emits the same events.
The widget-side dual renderer already handles both event families.

**Proposed amendment (path B)**:
1. Re-scope the ADR to the **SSE section-streaming contract + widget contract**,
   binding for ANY composite executor (coded workflow or frozen engine node).
2. Rescind the "DeliverComposite by default for future workspace playbooks"
   rule; the node type is frozen-with-engine (overlay S3/S7 TL bucket).
3. Backward-compat invariants (FieldDelta preservation, append-only ordinals)
   carry over unchanged — they protect the frozen representation correctly.

---

## 3. 🔧 Minor refreshes (non-blocking; batch into Step 3 doc tasks)

| ADR | Refresh |
|---|---|
| ADR-033 Streaming side channel | Pattern is sound and survives with the tool framework (overlay S5). Refresh: exemplar references the legacy `WorkingDocumentTools`; the two-channel table stays binding. |
| ADR-034 Membership resolution | `MembershipResolverService` + junction table + Service Bus topic survive as services/tools. The `LookupUserMembership` **node (ActionType 52)** binding freezes with the engine; the service remains invocable from coded workflows/tools. One-sentence note. |
| ADR-010 DI minimalism | Principle aligned; reality is 265 registrations with the AI subtree having ignored "no interfaces without seams" wholesale. The greenfield consolidation is the first real *tailwind* toward this ADR (net-negative registrations: engine wrapper, dispatcher stack, dead clusters). Add the migration as the named "separate architectural follow-up" the Phase-5 baseline note promised. Target components default to concretes + ADR-032 unsealed-virtual pattern (which preserves ADR-010 — no interface needed for Null-Objects). |

---

## 4. 🆕 The missing ADRs (the real fix — principles, not mechanisms)

The drift census is unambiguous: **no ADR ever governed dispatch, session
semantics, or the capability model** — precisely where ten mechanisms, four
routing surfaces, and the Action/playbook/consumer smear accumulated. Two new
principle-level ADRs, authored from the converged design after the overlay
review, close the gap:

**ADR-039 (proposed): Grounded Execution & Closed Catalogs**
- Every platform output is one of: cataloged-capability output / tool-composed
  answer with citations / confirmation prompt / honest refusal (D5).
- Two closed catalogs (Actions+Bindings; Tools) — the LLM never invokes an
  unlisted tool; nothing dispatches to an uncataloged capability (D6).
- ONE dispatch protocol (Event / Click / Text paths); adding a second
  intent-detection mechanism anywhere is a violation, full stop. (This single
  MUST NOT would have prevented mechanisms #2 through #10.)
- Control flow is code; behavior is data (the OQ-2 principle — maker surface is
  prompt-based scopes + binding metadata, never graphs).
- Side effects gate through the one Confirmation Gate, driven by
  `side_effect_class` — hardcoded tool-name lists are forbidden (the
  CompoundIntentDetector lesson).

**ADR-040 (proposed): Session Ledger**
- Append-only, addressable, typed session ledger; ALL capability outputs write
  to it before rendering (storage/rendering separation, D2/D8).
- Ledger-entry classes mapped to ADR-015 tiers; tool chains replayable.
- No capability reads its input from a surface; all cross-capability context
  flows by ledger reference (P10).
- Disposition vocabulary (informational / work_product / overlay / email /
  record / notification) is the ONLY rendering contract.

These are deliberately mechanism-light: they constrain *what must always be
true*, not which class implements it — the property that made ADR-028/030/015
durable while ADR-013/037 rotted.

---

## 5. Decision summary for the operator

| # | Decision | Recommendation |
|---|---|---|
| A-1 | ADR-013 amendment (capability-invocation canon; boundary rules unchanged) | Approve; author alongside canonical doc v0.4 |
| A-2 | ADR-037 amendment (keep section-streaming contract; rescind engine-steering default) | Approve; **the rescind is urgent** — it currently directs any new workspace playbook onto the frozen engine |
| A-3 | Minor refreshes (033/034/010/016/018/038) | Batch as Step 3 doc tasks |
| A-4 | New ADR-039 Grounded Execution & Closed Catalogs | Approve authoring after overlay review (encodes D5/D6/D7/OQ-2) |
| A-5 | New ADR-040 Session Ledger | Approve authoring after overlay review (encodes D2/D8) |

Nothing in the aligned set (§1) blocks or bends the greenfield — the auth,
caching, governance, eventing, kill-switch, and testing ADRs are assets the
greenfield inherits whole. The constraint problem was two mechanism-shaped
rules and three governance vacuums.
