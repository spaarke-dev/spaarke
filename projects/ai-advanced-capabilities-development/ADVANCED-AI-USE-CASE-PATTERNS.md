# Advanced AI Use-Case Patterns
## User interaction modes for the Insight Engine, Action Engine, and Lavern-derived capabilities

> **Date**: 2026-05-20
> **Status**: Working design document
> **Owner**: ralph.schroeder@hotmail.com
> **Purpose**: Make concrete how users actually interact with Spaarke's AI capabilities once the Insight Engine, Action Engine, and Lavern-derived patterns (Precedent Board, GateResolver, EvaluatorGate, etc.) are in place. Resists the temptation to over-systematize — gives six vivid interaction modes that each correspond to a real user rhythm.
> **Companion documents**: [`LAVERN-ANALYSIS-AND-PLAN.md`](LAVERN-ANALYSIS-AND-PLAN.md), [`TEST-DATA-REQUIREMENTS.md`](TEST-DATA-REQUIREMENTS.md)

---

## 1. Purpose and audience

This document answers the question: **"What does using Spaarke's AI capabilities actually look like to a person?"**

It is intended for:
- Product designers building UI surfaces for AI-driven workflows
- Solutions engineers scoping customer pilots
- Documentation and demo content authors
- Engineering leads validating that the technical building blocks (in [`LAVERN-ANALYSIS-AND-PLAN.md`](LAVERN-ANALYSIS-AND-PLAN.md)) compose into coherent user value
- Pre-sales and customer-facing roles needing to articulate the product story

It is **not** a UI specification, not a final IxD design doc, and not based on user research. It is a structured hypothesis space derived from the technical building blocks. User research should validate, refine, and probably contradict parts of this document over time.

---

## 2. The design space — why "review/drafting" undersells it

When asked what a legal AI platform does, most people answer "review documents and help draft them." That's the *most visible* use case but only one of at least six distinct interaction patterns. The Spaarke platform's combination of the Insight Engine (durable cross-matter signal), the Action Engine (configurable user-defined workflows), the Precedent Board (firm-level knowledge with lifecycle), and the GateResolver primitive (human-in-the-loop across surfaces) supports a much wider range of rhythms.

These six modes are not separate products — they share underlying infrastructure (JPS playbooks, SSE streaming, Tool Registry, Dataverse, AI Search, Cosmos). Different personas use different modes. The same person uses different modes for different jobs.

---

## 3. Personas

This is a coarse pass — refine via user research. Listed by approximate volume of Spaarke usage (highest first).

| Persona | Primary modes used | Anchoring activity |
|---|---|---|
| **Paralegal / contract manager** | 1 (review), 5 (drafting) | High-volume document work, intake, organization |
| **Junior associate** | 1 (review), 5 (drafting), 3 (chat-driven research) | Document analysis, first drafts, learning institutional patterns |
| **Mid-level attorney / senior counsel** | 1, 3, 5 — and increasingly 4 (curation) | Matter work, negotiation prep, drafting review |
| **Legal operations manager** | 2 (triage), 6 (digests) | Workflow design, queue management, portfolio operations |
| **Risk / compliance officer** | 2 (proactive triage), 6 (scheduled scans) | Continuous monitoring across the portfolio |
| **Partner / general counsel** | 3 (cross-matter inference), 6 (digests) | Strategic judgment calls, occasional deep queries, periodic briefings |
| **Subject-matter expert / KM lead** | 4 (Precedent curation) | Periodic review sessions, institutional knowledge custodianship |
| **Procurement / sales / HR (non-legal)** | 2 (as approval recipient), 5 (limited drafting) | Self-service contract intake; approval-loop participant |

---

## 4. Surface inventory

Where Spaarke renders to users. Each mode below maps to one or more of these surfaces.

| Surface | Primary host | Typical use |
|---|---|---|
| **Workspace** | Code page in Power Apps | Central legal workspace — matters, documents, context pane |
| **Context pane** | Component within Workspace | Persistent right-side panel; renders flow UI, signals, Precedent badges |
| **SprkChat** | Inline chat component (workspace, PCF, side panel) | Conversational queries, inferences, Q&A |
| **Office Add-ins** | Word, Outlook task panes | In-document drafting, email-anchored actions |
| **Teams** | Adaptive cards, channel notifications | Approval surfaces, alerts, mobile-friendly summaries |
| **Outlook email** | Plain emails + actionable messages | Digests, briefings, low-friction approvals |
| **Mobile** | Browser-based; future native | Approvals, urgent notifications |
| **Power Pages** | External-facing portal | Self-service intake, external approvals |
| **M365 Copilot** | Callable tool surface | Spaarke Actions exposed as Copilot tools |
| **MCP Server** | Programmatic | External agents (e.g., Claude in customer envs) calling Spaarke primitives |

---

## 5. The six interaction modes

Each mode is described with: **persona**, **surface**, **rhythm**, **flow** (with concrete ASCII visualization), and **technical layers in play** (cross-references to the patterns in `LAVERN-ANALYSIS-AND-PLAN.md`).

### 5.1 Mode 1 — Reactive document review

The obvious case, but it's only one slice.

**Persona**: Paralegal, junior associate, contract manager
**Surface**: Workspace + context pane + chat
**Rhythm**: Synchronous, document-anchored, seconds-to-minutes

**Flow**:

```
[User uploads vendor MSA to matter]
   ↓
Workspace shows the document
   ↓
Context pane lights up with proactive signals:
   "Insight Engine: 14 comparable vendor MSAs in your closure index"
   "Precedent: BigCorp's indemnity is non-standard (Confirmed, 7 matters)"
   "Precedent: This vendor's SLA tier changed in last 18 months (Drift Review)"
   ↓
User clicks "Run Contract Review"
   ↓
PlaybookExecutionFlow streams in context pane:
   [✓] Document parsed (sanitized: 14 zero-width chars stripped)
   [✓] Party Extraction        cited 4 entities
   [✓] Risk Flagging           found 3 HIGH, 2 MEDIUM
       [ ] EvaluatorGate       sonnet checking opus...
       [✗] EvaluatorGate       score 0.6, regenerating...
       [✓] EvaluatorGate       score 0.85 ✓
   [✓] Citation verifier       14 of 14 quotes found in source
   [⚠] decline_to_find         "Cannot rate SLA tier — no comparable matters"
   [✓] Done
   ↓
Output rendered with verified citations, risk flags, and one yellow uncertainty card
```

**Technical layers in play**: ACT-001 Contract Review playbook · EvaluatorGate primitive (Pattern #2) · GroundingVerifier post-step (Pattern #3) · Sanitizer (Pattern #10) · DeclineToFind (Pattern #7) · Flow UI (Pattern #4) · Precedent Board lookups (Pattern #1)

**What makes this mode succeed**:
- Latency under ~30 seconds for the full review
- Citations clearly verified and clickable to source spans
- Risk flags ranked, not just listed
- Precedent context provided proactively before user asks
- Uncertainty (yellow cards) is honest and useful, not embarrassing or hidden

### 5.2 Mode 2 — Proactive monitoring + triage

The Action Engine sweet spot. Completely different rhythm from Mode 1: setup once, run forever.

**Persona**: Legal operations manager, risk officer
**Surface**: Configured in workspace; runs in background; surfaces in Teams/email/queue
**Rhythm**: Event-driven, fire-and-forget, async (minutes to hours from trigger to approval)

**Flow**:

```
SETUP (one time):
Legal ops opens Action Engine, picks template:
   "Vendor Contract Triage" → fill in: inbox X, thresholds, approver group
   Save as Action with default humanGate.required=true

ONGOING (invisible until something happens):
New vendor PDF arrives in inbox X
   → Webhook to BFF
   → Action runs playbook in background
   → Sanitizer + Parser + RedFlagDetector + EvaluatorGate
   → 1 HIGH risk: uncapped indemnity in §8.3
   ↓
GateResolver routes approval to Teams (legal-ops channel)
Card shows:
   "🟡 Vendor: Acme Corp · MSA dated 2026-05-19
    AI flagged: uncapped indemnity (§8.3)
    Evidence: '...indemnify and hold harmless without limitation...'
    Similar pattern in 4 prior matters — all escalated
    [Approve escalation]  [Reject]  [View full analysis]"
   ↓
Approver clicks Approve in Teams
   → New matter created, attorney auto-assigned
   → Original PDF + AI analysis attached
   → Audit trail: signal → analysis → finding → approval → action
```

**Technical layers in play**: Action Engine Action manifest · Webhook ingest · Sanitizer (Pattern #10) · JPS playbook execution · EvaluatorGate (Pattern #2) · GateResolver with Teams surface (Pattern #5) · Phase deny-tools enforcing safe execution (Pattern #8) · Precedent Board for "similar pattern in 4 prior matters"

**What makes this mode succeed**:
- Approver gets enough context in the card to decide without leaving Teams
- Card includes evidence (quoted text) and precedent context, not just AI summary
- Latency from trigger to approval card under ~5 minutes
- Default-approve-required policy is sensible; opt-out for low-risk classes is auditable
- Full audit trail behind the approval for compliance review later

### 5.3 Mode 3 — Conversational cross-matter inference

The most strategically valuable mode but the lowest-volume. Partners don't query daily — they query before key decisions.

**Persona**: Partner, general counsel, senior counsel making judgment calls
**Surface**: SprkChat in workspace; potentially M365 Copilot
**Rhythm**: Ad-hoc, exploratory, seconds-to-minutes

**Flow**:

```
Partner: "What's our typical outcome on M&A deals where the target
          missed earnouts?"

SprkChat (Insights Agent under the hood):
  [thinking — tool calls visible to user as they happen]
  → IFindComparableMatters: scoping query
  → IGetMatterFacts on 14 results
  → IAssessEvidenceSufficiency: ≥12 threshold met for general M&A
  → Checking Precedent Board for confirmed patterns
  → Citation verifier: 8/8 quotes verified

Response:
  Based on 14 comparable matters (2023–2026), average settlement
  at 62% of disputed earnout amount (range 40–85%). [confidence: MEDIUM]

  📌 Two Confirmed Precedents apply here:
    • PR-007: "Earnout disputes with software targets typically
      settle within 90 days when revenue thresholds missed by <20%"
      (12 supporting matters · Last reviewed by S. Lee, 2026-04-12)
    • PR-014: "Material adverse change defenses rarely succeed
      post-2023 in tech M&A" (8 supporting matters)

  🟡 I declined to infer earnout patterns specific to your fintech
  segment — only 4 matters in that sub-category, below the
  threshold of 12. Want me to expand to fintech-adjacent?
```

**Technical layers in play**: Insights Agent · IFindComparableMatters / IGetMatterFacts / IAssessEvidenceSufficiency (existing Insight Engine tools) · Precedent Board cross-references (Pattern #1) · DeclineToFind (Pattern #7) · GroundingVerifier post-step (Pattern #3) · Tool-call visibility in chat for transparency

**What makes this mode succeed**:
- Tool calls visible in chat for trust ("I see what it's doing")
- Confidence level explicit and earned through evidence count
- Precedent citations include their *own* confidence (last reviewed date, supporting matter count)
- DeclineToFind is graceful — offers a follow-up direction rather than just refusing
- Partners can drill into any cited matter or Precedent from the chat response

### 5.4 Mode 4 — Precedent curation

The SME workflow that keeps the system honest. Without this mode, AI patterns are unverified hypotheses; with it, they become institutional knowledge.

**Persona**: Subject-matter experts, knowledge management leads, senior partners with curation responsibilities
**Surface**: Dedicated Precedent Management code page; potentially also workspace context pane for ad-hoc review
**Rhythm**: Periodic (weekly/monthly), short focused sessions of 15–60 minutes

**Flow**:

```
SME opens Precedent Management page
   ↓
3 queues visible:
   [READY FOR CONFIRMATION (4)] — Tentative precedents at threshold
   [DRIFT REVIEW (1)]            — Confirmed precedents with negative outcomes
   [STALE WARNING (12)]          — Confirmed precedents decaying

Clicks first item in READY:
   "Tentative Precedent #847:
    Counterparty 'BigCorp' typically negotiates 30-day cure
    periods up from 15 in IP indemnity clauses"

    Evidence trail (5 matters):
    [M-2024-091] Cure period went 15→30 · settled
    [M-2024-203] Cure period went 15→30 · settled
    [M-2025-014] Cure period went 15→45 · litigated, lost
    [M-2025-188] Cure period went 15→30 · settled
    [M-2026-022] Cure period went 15→30 · settled

    Score: 0.78  ·  4 of 5 positive outcomes  ·  All evidence verified

    [Confirm]  [Reject]  [Modify]  [More evidence needed]
```

**Technical layers in play**: Precedent Board state machine (Pattern #1) · `sprk_precedent` Dataverse entity · Supporting Observation linkage · Drift detection logic · GateResolver as the confirmation/rejection surface (Pattern #5)

**What makes this mode succeed**:
- Evidence trail clear and complete — SME can verify each supporting matter without leaving the page
- The "Modify" option lets the SME refine the pattern text without rejecting
- Drift review has the same information density as confirmation review
- Confirmation is fast (sub-minute per Precedent) so a 30-minute session can clear a queue
- Confirmation is auditable — name, date, optional reasoning preserved

**Why this matters**: This is where Spaarke beats a black-box AI. SMEs *own* institutional knowledge. AI surfaces it; humans ratify it. The Precedent Board becomes the firm's living institutional memory — defensible, citable, governed.

### 5.5 Mode 5 — Drafting with grounded suggestions

In-flow, real-time. Different from Mode 1 in that the user is actively *creating* the document, not reviewing one that exists.

**Persona**: Attorneys, paralegals doing first-pass drafting
**Surface**: Word add-in, code page editor, chat
**Rhythm**: In-flow, real-time, seconds

**Flow**:

```
Attorney in Word, typing a SaaS agreement
Selects empty section → "Insert clause: indemnity"
   ↓
Action Engine triggers drafting workflow
   → Pulls from Confirmed Precedents for our positions
   → Pulls from CUAD reference index for industry-standard variants
   → Generates 3 options with provenance:

   Option 1: Our standard cap of $1M (8 matters precedent: PR-022)
   Option 2: $5M cap matching prior BigCorp deal (1 matter: M-2025-188)
   Option 3: CUAD-style mutual indemnity (industry baseline, low risk)

   Each option has citations + risk flags inline.

Attorney picks Option 2
   ↓
GateResolver intercepts: "This deviates from our standard cap.
                          You'll need partner approval before signing."
   ↓
Approval card appears in partner's queue.
```

**Technical layers in play**: Action Engine drafting workflow · Precedent Board retrieval (Pattern #1) · CUAD reference clause index (Pattern #9) · GroundingVerifier (Pattern #3) · GateResolver (Pattern #5) · Office Add-in surface

**What makes this mode succeed**:
- Provenance visible before the attorney picks an option (precedent count, supporting matter)
- Risk flags inline (e.g., "deviates from standard cap") not buried in a separate panel
- Gate intercepts unusual choices automatically — attorney doesn't need to remember to escalate
- Insertion preserves citations as Word comments or footnotes for later audit
- Falls back gracefully when no precedent exists ("Industry baseline option only — your firm has not handled this matter type")

### 5.6 Mode 6 — Long-running background insights

The digest/heartbeat mode. The user does *nothing* — Spaarke does the watching, distillation, and curation.

**Persona**: GCs, partners, ops leads who don't want to ask — they want to be told
**Surface**: Email, Outlook, Teams cards, mobile notifications
**Rhythm**: Scheduled (daily / weekly / monthly)

**Flow**:

```
Every Monday morning — GC's Outlook receives:

  "Spaarke Weekly Briefing — Week of 2026-05-20

   🆕 Newly Confirmed Precedents (3):
      • PR-849 IP indemnity carve-outs for SaaS w/ ML training data
      • PR-850 Late-stage MSA termination cure extensions
      • PR-851 Privacy clauses post-GDPR enforcement (EU matters)

   ⚠️  Drift detection (1):
      • PR-007 (Earnout settlement timing) has 2 contradicting outcomes
        Suggest review — link to evidence

   📊 Portfolio metrics:
      • 17 matters opened
      • 12 contracts reviewed by AI (avg confidence 0.84)
      • 4 approvals routed via GateResolver (avg time 2.3 hrs)
      • 23 documents sanitized at ingest

   📌 Pending your review:
      • 2 high-risk Action Engine signals awaiting approval [open]
      • 1 Precedent in Drift Review needs SME confirmation [open]"
```

**Technical layers in play**: Scheduled Action Engine job · Precedent Board lifecycle events (promotion, drift) · Portfolio analytics queries · GateResolver pending queue summarization · Outlook actionable message rendering · Telemetry roll-up

**What makes this mode succeed**:
- Digest is short enough to read on mobile while walking to a meeting
- Each item links to the full detail view for follow-up
- Pending items don't get stale — the digest is the call to action
- Customization per recipient: GC sees portfolio metrics, partner sees their matter portfolio only, ops lead sees queue health
- Skippable: recipient can disable any section without losing the others

---

## 6. The unifying pattern across all six modes

Every mode has the same building blocks under the hood; different modes compose them differently. This is the architectural payoff: invest once in primitives, reuse across rhythms.

| Layer | Mode 1 | Mode 2 | Mode 3 | Mode 4 | Mode 5 | Mode 6 |
|---|---|---|---|---|---|---|
| Trigger | User upload | Webhook signal | User chat | Scheduled / queue | Word selection | Daily cron |
| Engine | Insight + JPS | Action + JPS | Insight | Insight | Action + Insight | Insight + Action |
| Latency | Seconds-minutes | Async minutes-hours | Seconds | Per-item sub-minute | Real-time | Scheduled |
| Sanitize (Pattern #10) | ✓ | ✓ | (if doc input) | — | (if generating) | — |
| EvaluatorGate (Pattern #2) | ✓ | ✓ | (optional) | — | (optional) | — |
| Citation verifier (Pattern #3) | ✓ | ✓ | ✓ | — | ✓ | — |
| Flow UI (Pattern #4) | ✓ | (in detail view) | (tool calls) | — | (compact) | — |
| GateResolver (Pattern #5) | (rare) | ✓ | (rare) | ✓ (confirm) | ✓ | (digest summary) |
| Evidence-required (Pattern #6) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| decline_to_find (Pattern #7) | ✓ | (graceful skip) | ✓ | — | ✓ | — |
| Phase deny-tools (Pattern #8) | ✓ | ✓ | — | — | ✓ | — |
| Seed data (Pattern #9) | (background) | (background) | (background) | (background) | ✓ direct | — |
| Provider tiers (Pattern #11) | ✓ | ✓ | ✓ | — | ✓ | — |
| Precedent Board (Pattern #1) | ✓ read | ✓ read | ✓ read | ✓ write/curate | ✓ read | ✓ summary |

**Read this matrix as a feature roadmap diagnostic**: a pattern missing from too many modes is under-leveraged; a mode missing too many patterns is under-realized. The matrix should fill in over time as workstreams ship.

---

## 7. What is NOT a primary user surface

Worth saying out loud so we don't over-build:

- **A standalone "AI playground"** — that's a developer surface, not user-facing. Engineers should have one. End users should not.
- **An admin dashboard for the agent system** — SMEs touch the Precedent Board; system admins shouldn't need to monitor agents directly. If agent monitoring becomes a recurring need, that's a signal of a different problem (unreliable agents, undertested workflows).
- **A "build your own agent" UI for end users** — the Action Engine *templates* are the entry point; freestyle composition is for power users at best. Most users should never see a node-graph editor.
- **An "AI history" page** — every AI interaction is anchored to a matter, a document, a chat session, an approval, or a digest. There's no value to a chronological list of "AI events" outside those contexts.
- **An "explain why AI did this" reasoning trace UI exposed to end users** — citations + evidence + Precedent links provide enough explanation for most users. Detailed reasoning traces are a debugging surface for engineers and a compliance artifact for auditors, not a daily UX.

---

## 8. Cross-mode considerations

### What composes well

- **Mode 2 → Mode 1**: A triaged signal often surfaces as an approval card that, when approved, lands in someone's workspace for full Mode 1 review.
- **Mode 4 → Mode 3**: A newly confirmed Precedent is immediately citable in Mode 3 conversational queries.
- **Mode 1 → Mode 4**: Findings from Mode 1 reviews accumulate as Observations that feed Tentative Precedents → eventually surface in Mode 4 curation queue.
- **Mode 5 → Mode 6**: Drafted documents become Observations as they close out, feeding Precedent reinforcement and ultimately the weekly digest.

### What doesn't compose well (anti-patterns)

- **Mode 3 results auto-firing Action Engine workflows**: conversational outputs are exploratory, not operational. Don't wire chat answers to triggers.
- **Mode 4 confirmations bypassing GateResolver**: even SME confirmations should go through the gate primitive for audit consistency.
- **Mode 6 digests becoming dashboards**: digests are scheduled distillations, not real-time monitors. Resist scope creep.

---

## 9. Open design questions

1. **How does a user discover modes they're not already using?** First-run experience, in-app suggestions, role-based defaults — TBD.
2. **What's the upper bound on Mode 2 frequency?** If a customer wants Spaarke watching dozens of inboxes with hundreds of daily signals, is that an Action Engine throughput question, or a user experience question (too many approval cards = approval fatigue)?
3. **Does Mode 3 need conversation memory across sessions?** Or is every chat session independent? Affects how Precedent citations and prior queries surface.
4. **How does Mode 4 work for matters under attorney-client privilege**? Can Precedents derive from privileged matters at all, and if so under what governance?
5. **Does Mode 5 require pre-approval of the *workflow*, not just the *output*?** A drafting workflow that touches sensitive clause libraries may need a meta-approval.
6. **What's the failure mode when Mode 6 has nothing to report?** Silent skip vs "all quiet" digest vs aggregated monthly when slow? Affects perceived value of the digest.

---

## 10. Validation through user research

This document is a hypothesis. Validation requires:

- **Shadow studies** of 3–5 customer roles using a beta deployment for ≥4 weeks
- **Mode adoption tracking** — which surfaces and patterns each role actually uses vs. ignores
- **Friction logging** — where users abandon flows mid-stream
- **Counterfactual interviews** — for any mode a user doesn't use, why not? Wrong feature, wrong surface, wrong rhythm, wrong trigger, or genuinely unneeded?

Modes that show low adoption should be deprecated, simplified, or repositioned. Modes that show heavy customization should be promoted to first-class with template variants.

---

## 11. Cross-references

- [`LAVERN-ANALYSIS-AND-PLAN.md`](LAVERN-ANALYSIS-AND-PLAN.md) — patterns, schemas, ADRs, sequencing
- [`TEST-DATA-REQUIREMENTS.md`](TEST-DATA-REQUIREMENTS.md) — how to seed and validate these modes with realistic data
- [`projects/ai-spaarke-insights-engine-r1/`](../ai-spaarke-insights-engine-r1/) — Insight Engine project (Modes 1, 3, 4, 6 origin)
- [`projects/ai-spaarke-action-engine-r1/`](../ai-spaarke-action-engine-r1/) — Action Engine project (Modes 2, 5, 6 origin)
