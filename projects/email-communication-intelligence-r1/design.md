# Email Communication Intelligence R1 — Design Charter

> **Status**: DRAFT **rev 3** — 2026-07-28. Rev-3 reconciles the design against the **code-directed Action + Binding** substrate (the node-graph playbook engine is frozen), dissolves the stale `record`-disposition / redesign-r2 blockers, and grounds the G-1 deterministic-association mechanism in live `spaarkedev1` data + operator decisions. New authoritative material is **§0.7–§0.10**; they supersede the mechanism claims in D-2.5 / D-2.7 / T-5 / T-5b. **Read §0 first — it is the authoritative scope.**
> **Prior**: rev 2 (2026-07-28) reconciled the v0.1 charter against what shipped since (r4 engine, r5 client design, notification-spine delivery). Sections §1–§13 are the v0.1 charter, preserved for strategic rationale but **superseded on all as-built and mechanism claims by §0**.
> **Project**: `email-communication-intelligence-r1` (renamed 2026-07-28 from `spaarke-email-intelligence-module` to sort with the `email-communication-*` line). Epic [#431 EMAIL & MESSAGING](https://github.com/spaarke-dev/spaarke/issues/431).
> **Authors**: v0.1 — Claude Opus 4.8 from live-code investigation + operator scope rulings (2026-07-10, verbatim in §1.1). rev 2 — code-grounded reconciliation (9-area as-built audit, 2026-07-28).
> **Predecessor concept**: [`EMAIL-TRIAGE-MODULE-DESIGN.md`](EMAIL-TRIAGE-MODULE-DESIGN.md) (concept/market survey, DRAFT r1). That document is a **general review, not code-grounded** — its component names are *indicative only*.
> **Authoritative as-built context (refreshed rev 2)**:
> - Sibling projects: `email-communication-solution-r4` (matching engine — **shipped, on dev**), `email-communication-solution-r5` (Outlook-style Email Workspace UI — **designed, not built**), `spaarke-notification-spine-r1` (action/notification delivery spine — **shipped, partially inert**), `messaging-communication-app-r1/r2/r3` (Teams-style channel).
> - [`docs/architecture/communication-service-architecture.md`](../../docs/architecture/communication-service-architecture.md) — the canonical email substrate.
> - Key as-built files cited inline in §0.2.

---

## 0. Reconciliation & Narrowed Scope (rev 2 — 2026-07-28) — AUTHORITATIVE

> This section supersedes the v0.1 as-built inventory (§3) and scope (§2, §5) wherever they conflict. It is written to be read standalone: an operator can review §0 alone and understand what r1 is, what it is not, and where its boundaries sit against the sibling projects. The strategic sections (§1.4 thesis, §2.0 pillars, §2.0b IP docketing wedge, §D-5.5 trust model) remain valid and are the "why."

### 0.1 What shipped since v0.1 (the ground moved)

The 2026-07-10 charter assumed the matching engine, the client surface, and the delivery spine were all to-be-built by this module. Three sibling projects have since claimed those layers. r1 must build **on** them, not re-scope them.

| Layer | Owner project | Status (verified 2026-07-28) | What r1 does with it |
|---|---|---|---|
| **Matching / Association Engine** (email → matter/project/invoice/org/contact; deterministic rungs 0–5 + AI rung, provenance, auto-file) | `email-communication-solution-r4` | ✅ Shipped, deployed to dev | **Consume** — r1 adds zero matching logic |
| **Rich AI classification substrate** (`CommunicationClassificationResult`: category, urgency, obligations[], suggestedActions[], privilegeFlagged, rationale) | r4 (task 031, FR-15) | ✅ Built — but **passive**: emitted as `provenance.signals` only; **consumed by nothing** | **Activate** — this is r1's single biggest harvest |
| **Reading / compose / association-review UI** (Outlook-style two-pane, `.eml` render, reply/forward, interactive Connections review) | `email-communication-solution-r5` | 📝 Designed, not built | **Coordinate** — r1's triage/priority/summary is a *mode/overlay* in r5's surface, not a second app |
| **Action + notification delivery spine** (durable outbox, SignalR, `CreateNotificationAsync`, `notification` disposition) | `spaarke-notification-spine-r1` | ✅ Spine shipped; RI path **inert** (see 0.2) | **Feed** — r1 supplies the real intelligence + confidence the spine delivers |
| **Messaging (Teams-style) channel** | `messaging-communication-app-r1/2/3` | Active | **Out of scope** — email-only |

### 0.2 Corrected as-built claims (v0.1 §3 is now wrong in these specifics)

Code-grounded audit, 2026-07-28. These correct both the v0.1 charter and the earlier "r4 wired responsive intelligence" assumption:

1. **The AI classification is rich but dark.** `Models/Ai/Communication/CommunicationClassificationResult.cs` produces category/urgency/obligations/suggestedActions/privilegeFlagged/rationale, but `Engine/Rungs/AiClassificationRung.cs` emits them as **metadata-only provenance signals** (Target=null, fixed 0.60 conf) that never prioritize, summarize-for-a-human, or trigger anything. **The substrate exists; the product on top of it does not.**
2. **Responsive-Intelligence fan-out was RE-HOMED, not built.** r4's W5 tasks 050–054 (FR-18 `record`/`notification` dispositions, FR-19 enrichment→EventRules→Create*, FR-20 "Communication Triage" JPS action) are all **⏭️ RE-HOMED / MOOT** in r4's `TASK-INDEX.md` — `EventRulesService` is SSE/session-shaped and "semantically wrong" for email.
3. **notification-spine landed only a thin slice, currently inert.** `CommunicationEnrichmentService` step 5 emits a `CommunicationAssessedSignal`, but its **`Confidence` is hardcoded to 0** ("the enrichment pipeline does not yet compute an RI-confidence score") → it **denies under any positive threshold**. It creates an **appnotification only** — no Event, no Task — and does not carry category/urgency/obligations.
4. **`record` disposition still throws.** `DispositionRoutability.cs` marks `record` non-routable; `OutputRouter.cs` throws `NotSupportedException`. Job B (record currency) has **no write path from email**.
5. **The "Communication Triage" JPS Action / summary-checklist (FR-20) was never authored.** The only email AI today is `AppOnlyAnalysisService.AnalyzeEmailAsync`, which runs a **document-profile** "Email Analysis" playbook and writes TL;DR/summary/keywords back onto the `.eml` Document — not a triage classification, and it does not fan out.
6. **`UpdateRecordActionCore` / `DataverseUpdateRecordHandler` exist but touch no email flow** — used by daily-briefing/finance/compose only.
7. **No triage queue, no prioritized inbox, no per-email audit entity (`sprk_emailreviewlog`), no Daily Briefing email channel (still 6 channels), no docketing/deadline-cascade code.** All confirmed absent.

### 0.3 What r1 IS — the intelligence layer (narrowed scope)

r1 is the **intelligence and record-currency layer** that sits on the shipped engine (r4), renders in the client (r5), and delivers through the spine (notification-spine). Its four jobs, in priority order:

1. **Activate the classification substrate (Pillar 1 — UNDERSTAND).** Consume the already-produced category/urgency/obligations, compute a **real RI-confidence score** (fixes the hardcoded-0 gap), and surface **prioritization + 2-line summary + extracted obligations** to the user. This is mostly *wiring dark capability to a surface*, not new AI.
2. **Record currency (Pillar 2 — UPDATE / Job B).** Author the email-triage playbook leg that **proposes allow-listed field updates on the matched record** (dates/amounts/parties/status), human-confirmed, cited to source email, audited. Requires completing the **`record` disposition** (coordinate with the `Services/Ai` owner). *This is the differentiator no sibling owns.*
3. **Email-triggered action (Pillar 3 — ACT / Job C).** Feed a real assessment (with confidence + obligations) into the notification-spine so it can create **Event/Task**, not just an appnotification. The flagship vertical is **IP Auto-Docketing** (Office Action → dated deadline cascade), §2.0b — entirely greenfield.
4. **Defensibility & surfacing.** The per-email **review-audit** record (machine + human review), and the **Daily Briefing triage channel** (7th).

### 0.4 Explicit boundary / coordination map (§11 anti-overlap)

| Capability | r1 owns? | Boundary rule |
|---|---|---|
| Matching / association / auto-file | ❌ r4 | Consume `sprk_associationprovenance` + status; add no rung |
| Reading pane / compose / `.eml` render / Connections review UI | ❌ r5 | r1's triage-priority/summary renders **inside r5's surface** as a mode/overlay; if r5 slips, r1 may ship a minimal queue view but must not fork r5's components |
| Notification/action **delivery** (outbox, SignalR, dispositions) | ❌ notification-spine | r1 **feeds** the spine (real confidence + assessment); does not build delivery |
| `record` disposition **implementation** | ⚠️ shared | Owned by `Services/Ai` (redesign-r2 / notification-spine); r1 drives the requirement + the email-side proposal payload — coordinate, do not fork |
| RI-confidence scoring | ✅ r1 | The hardcoded-0 gap is r1's to close |
| Email-triage playbook (category-for-human, summary, obligations→actions, proposed record updates) | ✅ r1 | New JPS Action(s) on the existing AI platform |
| Job B record-currency proposal + confirm flow | ✅ r1 | New |
| IP docketing / deadline cascade | ✅ r1 | Greenfield; the flagship wedge |
| Per-email review-audit entity + Daily Briefing triage channel | ✅ r1 | New |
| Messaging/Teams/SMS | ❌ messaging-app | Email-only |

### 0.5 Coordination hazards (all BFF hot-path)

- **`Services/Ai/` is owned by `spaarke-ai-architecture-redesign-r2`** — consume `Services/Ai/PublicContracts/` seams; the `record`-disposition completion must go through that owner (same gate r4's W5 hit). Run `/conflict-check` before every BFF PR.
- **`Services/Communication/` persist path** is edited by notification-spine (step-5 emit), messaging-app r1/2/3, and r4 — `parallel-safe: false` on that path. r1's RI-confidence work touches exactly this seam → coordinate merge order.
- **`@spaarke/notifications` build break** from notification-spine must be resolved before any SpaarkeAi deploy from tip.

### 0.6 Open questions for operator review (rev 2)

> **Rev-3 resolutions (2026-07-28):** Q1 → **UI gated on r5** (operator); Phase-1 is backend-only but visible via the notification path (§0.10). Q2 → **DISSOLVED** — Job B needs no `record` disposition and redesign-r2 has no live owner (§0.7). Q3 → RI-confidence **derives from the classification rung + deterministic-rung agreement** (§0.10). Q4 → **Phase 1 = intelligence spine** (§0.10). Q5 (IP-docketing competitive validation, D-12) → **still open**, remains a Phase-2 gate.

1. **Sequencing vs r5.** r1's Pillar-1 surface needs r5's client. Do we (a) gate r1's UI on r5, (b) run them together, or (c) let r1 ship a minimal standalone queue if r5 slips? *(Recommend b — shared contract, r1 provides the triage data behind r5's surface.)*
2. **`record` disposition ownership.** Confirm with `spaarke-ai-architecture-redesign-r2` whether r1 or the AI-platform owner completes the `record` leg. Blocks Job B.
3. **RI-confidence definition.** What computes the assessment confidence that notification-spine consumes — a field of the classification result, or a new scorer? *(Recommend: derive from the classification rung + deterministic-class agreement.)*
4. **Scope of Phase 1.** Recommend Phase 1 = Pillar 1 (activate substrate + priority/summary surface) + the RI-confidence fix (unblocks notification-spine's Event/Task path). Defer Job B and IP docketing to Phase 2 — they are higher-value but need the `record` disposition and a docketing data model.
5. **IP docketing as beachhead (D-12/D-13, still open).** Validate the competitive claim before committing the wedge as Phase-2 lead.

---

### 0.7 The intelligence & write substrate is CODE-DIRECTED (Action + Binding) — supersedes D-2.5 / D-2.7 / T-5 / T-5b mechanism

**Investigation 2026-07-28 (four code-tracing passes + canonical docs).** The platform moved from the data-driven **node-graph playbook** engine to the **code-directed Action + Binding** model (ADR-039; `docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`; operator decision 2026-06-30, *"config-table-with-rules IS an interpreter"*). The node-graph engine (`PlaybookOrchestrationService`, `UpdateRecordNodeExecutor`, et al.) is **FROZEN — Insights family only** (matter-health, predict-matter-cost, universal-ingest); ADR-039 MUST NOT land new capability on it. **Every mechanism the v0.1/rev-2 charter names on the node engine or a "JPS playbook graph" is superseded:**

| Charter decision (mechanism) | Superseded by (code-directed) |
|---|---|
| D-2.5 "Email Triage **JPS playbook**" (node graph) | A `prompted` **Action + Binding** (`TRIAGE-EMAIL@v1`) — single structured LLM call via `ActionRunner`; triggered on the **Event path** (a communication event's `sprk_oneventbindings`) or from `CommunicationEnrichmentService`'s AI-analysis step via the `Services/Ai/PublicContracts/` facade (ADR-013 — never `IOpenAiClient` in Communication code) |
| D-2.7 / T-5b Job B via `UpdateRecordNodeExecutor` | `IActionSeam.UpdateRecordAsync` → **`UpdateRecordActionCore`** (the *same shared core* the frozen node executor wraps, minus the template layer) → Dataverse PATCH under user OBO, behind the **existing single confirmation gate** (keyed on `sprk_sideeffectclass`, store-before-render, fail-closed) |
| T-5 "re-point `AppOnlyAnalysisService`" | Triage is a **new Action + Binding on the enrichment/event path**, not a re-point of the document-profile analyzer (which stays as-is) |
| Job C tasks / deadlines | The shipped **create-task pattern** (`CREATE-TASK@v1` → gated `dataverse.create_record` → `sprk_event`), cited to the source communication + analysis |

**The "record disposition" blocker (rev-2 §0.6 Q2 / open D-2.7) is DISSOLVED.** The declarative `record` OutputRouter leg is dead (`DispositionRoutability` marks it non-routable; `OutputRouter` throws) and was explicitly deferred (former "E-21", never scheduled) — but Job B does **not** need it: the write cores (`UpdateRecordActionCore`, `TaskActionCore`, `NotificationActionCore`) are live and reached code-directed via `IActionSeam`. And `spaarke-ai-architecture-redesign-r2` (the claimed "owner" of `Services/Ai/`) is **complete, merged, deployed** — the `projects/INDEX.md:60` "sole owner" row is **stale**; there is no live owner to route through. r1 builds against the published `Services/Ai/PublicContracts/` seams with `/conflict-check`.

**Correction to rev-2 §0.2 item 3:** the RI action chain is more built than rev-2 stated — when *authorized*, `CommunicationRiActionService` creates a **Task + durable outbox row + SignalR ping + appnotification** (not "appnotification only"). It is inert only because `CommunicationAssessedSignal.Confidence` is hardcoded to `0` (denies under the 0.8 default threshold), so today it creates **nothing**. Fixing the score is plumbing, not new infrastructure.

**Coordination hazard (new):** ADR-041 (judgment/confirmation gate), ADR-043 (execution spine), ADR-047 (notification spine) are **Proposed / in-flight** — pin to their current shape and check the r2/successor charters before building against the gate or seams.

### 0.8 G-1 deterministic association — the identifier rung (AUTHORITATIVE mechanism)

Grounded in live `spaarkedev1` data + operator decisions, 2026-07-28.

**Scope: 7 core identifier-bearing records** — matter, project, invoice, work assignment, budget, service request, report card — each with its number field: `sprk_matternumber`, `sprk_projectnumber`, `sprk_invoicenumber`, `sprk_workassignmentnumber`, `sprk_budgetnumber`, `sprk_servicerequestnumber`, `sprk_reportcardnumber`. Today the engine's rung 0 matches **matter only**; the other six are the declared-but-missing G-1 work (this is *extend the existing rung*, not a new engine).

**Catalog-driven, not hardcoded.** The rung reads `sprk_recordtype_ref` (the ADR-024 catalog), which already carries per type: `sprk_recordlogicalname`, `sprk_regardingfield` (where to write the association), `sprk_regardingrecordnumberfield` (the number field to query), and `sprk_recordtypecode` (operator-populated: MTR / PRJT / INV / WRK / BDGT / SVCR / RPTC). The catalog **is** the per-org roster — onboarding a tenant needs no code change.

**Value-based, org-agnostic matching — numbering schemes are deliberately NOT modeled.** Live data proves numbering varies by org *and* by sub-type: matters use practice-area prefixes (`CMRCL-`, `PAT-`, `LITG-`, `EMPL-` — none equals the `MTR` code), projects `PRJT.10001.01`, work assignments `NEW.10001US01`, invoices are inconsistent including bare digits (`123456`). So `sprk_recordtypecode` is a **taxonomy code, not an identifier prefix** and is **not** used as a matcher. The rung instead:
1. extracts identifier-shaped candidate tokens from subject/body (a generic digits-plus-separators shape — never an org-specific format);
2. exact-matches each candidate against the 7 core number fields (**values, not patterns**);
3. a single match writes the typed regarding lookup, then **reinforces with the sender/participant rung via Noisy-OR** (§5.1) before auto-file.

**Guardrails (binding):**
- **Bare-numeric identifiers never auto-file on the identifier alone** — a value like invoice `123456` collides with phone numbers, amounts, ZIPs; it requires sender/participant reinforcement to reach auto-file confidence.
- **Multiple-entity matches → `Ambiguous` → human review** (no write).
- **AI-tier matches never auto-file** (existing invariant §5.3).

**Report-card enablement:** operator has added `sprk_regardingreportcard` on `sprk_communication` and the `sprk_recordtype_ref` row (code RPTC, number field `sprk_reportcardnumber`). r1 adds the matching entry in `Services/Communication/Engine/RegardingFieldMap.cs` so the engine can write it.

**Bonus signal:** the `PAT-` matter prefix deterministically flags **IP matters** — a free routing hint toward the docketing wedge (§2.0b) with no AI call.

**Catalog data hygiene (prerequisite):** `sprk_recordtype_ref` has known typos in some `sprk_regardingfield` values (e.g. `sprk_regarrdingbudget`, `sprk_egardingproject`) and a `contact`-row logical-name anomaly; the rung must read defensively or these are cleaned up first.

### 0.9 Two requirements the charter under-specifies (operator review 2026-07-28)

**(a) "Regarding" vs "related-to" — intent-aware matching (net-new).** An email that quotes a record identifier is not always *about* that record. *"New filing based on PAT-908068"* means a **new** record *related to* PAT-908068, not filing onto it — a naive deterministic match would misfile. Required: (i) context-sensitive phrasing (*"new filing based on / re: / related to / parent:"*) **demotes** the identifier from *regarding* to *related*, suppressing auto-file; (ii) the triage Action classifies intent — `file-to-existing` vs `update-existing` vs **`new-record-related-to`** — and on the last proposes *creating* a record (gated `dataverse.create_record`), human-confirmed, with the referenced record linked as related. The ADR-024 regarding model expresses "regarding" only; representing "related-to" distinctly is new surface.

**(b) Attachment-grounded action extraction (critical for IP).** The action an email implies often lives in the **attachment**, not the body (*"please see attached"* + a PDF Office Action). The Association Engine's `NormalizedMessage` envelope carries attachment **metadata only** (name / type / size), so rung-5 classification is body-only. Required: the triage / action-extraction Action **grounds on extracted attachment text** via the existing text-extraction → SPE-download → child-`sprk_document` path (already RAG-indexed), deterministically gated to likely action-triggers for cost. The IP-PDF deadline/action extraction is the **highest-value, highest-difficulty AI** in the project and carries the heaviest eval-case obligation.

### 0.10 Phase 1 boundary + surface gate (operator decisions 2026-07-28)

- **UI is gated on r5** (`email-communication-solution-r5`, the Outlook-style client — designed, not built). r1 Phase 1 is the **backend intelligence spine**; the triage-inbox rendering and rich review UX are r5's. *Open tension the operator flagged (§0.6 Q1): "make human review very easy" is core value and is r5-owned — revisit r1+r5 co-delivery if r5 slips; the shipped Connections PCF + chat gate are only interim review surfaces.*
- **Phase 1 is visible without r5** via the shipped notification spine: fixing the RI-confidence-0 gap lights up `CommunicationRiActionService` → **Task + appnotification + SignalR ping** for high-signal email. Interim confirm surface for Job B = the existing chat confirmation-gate dialog + the shipped **Connections PCF** (association review on the OOB form).
- **Phase 1 =** RI-confidence scorer (derive from the classification rung + deterministic-rung agreement) + `TRIAGE-EMAIL` Action/Binding + the 7-entity identifier rung (§0.8) + triage fields on `sprk_communication` + `sprk_emailreviewlog` audit entity + the report-card `RegardingFieldMap` entry. **Job B backend** (propose → confirm → `IActionSeam.UpdateRecordAsync`) is in Phase 1 with the interim confirm surface; the rich confirm UX and the regarding-vs-related intent flow (§0.9a) deepen with r5.
- **Phase 2 (deferred, non-blocking now):** IP Auto-Docketing deadline-cascade (greenfield — Phase-1 schema keeps obligations/audit general so a docket entry is *"an obligation type + a dated-cascade rule,"* not a migration); Daily Briefing 7th channel; the r5-gated inbox UX.

---

## 1. Framing

### 1.1 Operator scope rulings (2026-07-10 — binding)

1. **Multi-phase initiative.** This is a program, not a single project. No expectation that the full vision ships in one project; this charter scopes **Phase 1** concretely and sketches the arc.
2. **The existing Graph email system is a building block**, not something to rebuild. Spaarke already sends and receives email through Microsoft Graph today (the Communication Service, §3.1).
3. **No OOB Power Apps Server-Side Sync.** Spaarke deliberately built a **proprietary Graph-based** email capture system for flexibility. We do **not** build toward SSS absent an extraordinary compelling reason. *(This retires the concept charter's "Exchange-layer capture" framing entirely — capture is Graph-subscription-based by design, and that is the intended architecture, not a limitation.)*
4. **Integration with other modules/entities/processes is a first-class requirement**, not an afterthought — matters, projects, invoices (Finance Intelligence), work assignments, documents, Events/To-Dos, and the Daily Briefing.

### 1.2 Phase 1 focus (operator-defined)

- Spaarke as the **incoming/outgoing email service and facility** *(exists — §3.1)*
- Emails **automatically analyzed both deterministically and with AI**
- **Auto-route** emails to related matters, projects, invoices, work assignments, documents, and other records
- **Enhance email-to-document and email-to-Event (task)**
- **Rules to create Event/To-Do tasks** from email
- **Auto-categorize and summarize** emails
- **Prioritize** emails — flag important, deemphasize unimportant
- A **rigorous review audit mechanism** demonstrating both human and machine review
- **Configurable rules** for how emails are handled
- A **purpose-built Spaarke Email Client** (React 19 Code Page) — the OOB Power Apps model-driven form for email is **not sufficient** (no proper HTML body rendering, no threaded conversation view, no clean read/compose/reply/attachment UX). This is the primary human surface for the whole module.

### 1.4 The Spaarke thesis (first-principles distillation — NOT a feature aggregate)

**We are not assembling the best features of the market into one tool.** The market research (§13) surveyed ~25 products across six categories. The discipline: understand *what works and why*, then build a **derivative that is uniquely Spaarke's** — differentiated because we can see the whole board, not different for its own sake.

**The one-sentence value proposition:**

> **Every matter-relevant email should advance the matter on its own — understood, its facts posted to the record, its deadlines docketed, its tasks created, itself filed — with the professional *confirming*, not *doing*.** The inbox stops being a queue of work and becomes a stream the matter consumes. *("The matter reads its own mail.")*

**What the research proved works (and we adopt the principle, not the feature):**
- **Deterministic-first with an AI tail** (every DMS filing tool + the unquantified rules-vs-AI split) → our ladder, but grounded in the matter, not sender-history.
- **AI classifies with rationale; human confirms; everything recorded** (e-discovery / Relativity) → our trust + audit model, applied to *live* mail and docketing.
- **Suggest-with-explanation, one-action accept** (Copilot "prioritize my inbox," ndMail signal strength) → our disposition UX.
- **Email→field-update-suggested-human-confirmed exists and is accepted** (Copilot for Sales) → proves the *mechanism*; we own the *legal instantiation*.

**Why only Spaarke can build this — the seven primitives no competitor combines:**

| # | Spaarke primitive | Who else has it |
|---|---|---|
| 1 | **Native Dataverse matter/IP model** — the grounding context *and* the write target | Legal CRMs have the model but not email-body→field; Copilot has Dataverse but leaves legal "to custom dev" |
| 2 | **Proprietary Graph capture → canonical `sprk_communication`** (no SSS, no add-in dependency) | Filing tools are add-in-dependent; horizontal tools are inbox-local |
| 3 | **JPS playbook engine — deterministic rules AND AI as first-class peers**, tenant-configurable | No competitor exposes a configurable deterministic+AI authoring layer |
| 4 | **RAG over the matter's own correspondence** | Structurally absent from Copilot and every horizontal tool (behavioral only) |
| 5 | **Write + act engine** (`UpdateRecordNodeExecutor`, Event/To-Do creation, Action/playbook engine) | Lindy/Shortwave write generic CRM via MCP; none writes legal-matter semantics |
| 6 | **Evidentiary defensibility** — grounded citation to source email + immutable audit of what changed, by whom, from which message | No one markets this for email-driven updates; it matters far more in legal than sales |
| 7 | **MCP governance posture** — Spaarke owns the write surface (allow-list, human-confirm, audit) even when an external agent proposes | The inversion of Shortwave's blind-MCP-write model |

Microsoft has primitives 1, 2, 4-ish via the platform — and **explicitly leaves the legal-matter layer to custom development**. Spaarke *is* that layer, productized. That is the defensible position; everything else is table stakes we should meet cheaply, not compete on.

**The honesty guardrail (from the legal-CRM research):** do **not** position on "AI updates a record from email" alone — Copilot for Sales already owns that story in sales CRM and it will be the *"isn't this just Copilot?"* objection. We position on the intersection none combine: **email-body input · running/existing matters · legal-matter semantics · grounded-cited-audited defensibility.**

### 1.3 Problem statement

**What the user wants first** (§2.0): (A) *get through email faster and be told what matters*, and (B) *have records stay current from what emails say*. Everything else — filing, audit, the formal "operation" — supports those two jobs; it is not the lead.

In legal practice email is simultaneously a **record** (part of the matter file), an **obligation** (deadlines, service, client instructions), a **work-product trigger** (intake, invoices, executed agreements), and — most under-served today — a **stream of new facts that should update the matter** (dates moved, amounts, parties, status). Spaarke already sends, receives, and files email. The gaps are: captured email lands in a `Pending Review` limbo with **no surface to review it on**, **no summary/priority to speed the human**, and **no path to turn the facts in an email into updates on the associated record** — so someone re-keys them, or they rot. `IncomingAssociationResolver` auto-associates what it can and flags the rest for a "manual review" that has nowhere to happen. Phase 1 builds the client surface, the matter-grounded AI intelligence, the record-update loop, and the audit — as a thin layer over infrastructure (capture, AI platform, write engine) that already works.

---

## 2. Delivered product (user terms — acceptance backbone)

### 2.0 The three pillars — Understand · Update · Act

The module delivers **three jobs**, all on the same substrate (matter-grounded, deterministic-first + AI, human-confirmed, audited). Together they are "the matter reads its own mail" (§1.4). The firm-level operation/audit framing is the *supporting* compliance layer, not the headline.

- **Pillar 1 — UNDERSTAND (Job A: velocity).** "Get me through my email faster; tell me what I need to know." The system reads the inbox *for* you: summarizes, **prioritizes by matter context** (not sender behavior — the whole market's blind spot), deemphasizes noise, extracts asks/dates/amounts, clears items in one action. *(Gates G-2, G-7)*
- **Pillar 2 — UPDATE (Job B: record currency).** "Keep our records current from what emails say." Email is the highest-volume source of new matter facts (dates moved, amounts, parties, status). The system **proposes updates to the associated record's own fields** — human confirms, no re-keying. *(Gates G-1, G-8)*
- **Pillar 3 — ACT (Job C: email-triggered work).** "Turn what the email says into the work it requires." Email content **triggers rules → tasks, to-dos, events, deadlines, docket entries, and downstream playbook outputs.** Deterministic rules where the trigger is exact; AI where the instruction is free-text. *(Gates G-3, G-4, G-9)*

**Pillars 2 and 3 are the differentiators** (the research confirms 1 is table stakes everyone does behaviorally). Filing an email — every DMS does it — is not the same as **making the record reflect the email** (Pillar 2) or **turning the email into correctly-dated obligations** (Pillar 3). Spaarke already owns the engines: `UpdateRecordNodeExecutor` (Update) and the rules + Event/To-Do + Action machinery (Act). This module points them at email.

### 2.0b Flagship use case — IP Auto-Docketing (the wedge)

**Pillar 3's killer application, and the recommended beachhead.** In patent & trademark practice, a high volume of email carries **procedural instructions that trigger official actions with hard, malpractice-grade deadlines** — Office Actions ("response due in 3 months"), Notices of Allowance (issue-fee windows), foreign-associate instructions, annuity/renewal notices, priority deadlines. Calendaring these ("docketing") is mission-critical, high-volume, and — for *free-text email instructions* (as opposed to structured patent-office data feeds) — **still substantially manual**. A missed docket date can forfeit an IP right.

Why this is the perfect Spaarke wedge — it exercises **all three pillars in one workflow**:
1. **Understand** — classify the email as a docketing trigger, extract the action + reference (application/matter no.).
2. **Act** — a **deterministic rule maps the trigger type → a cascade of dated tasks/events** (e.g., Office Action → response deadline + reminder ladder + extension deadlines); AI reads the free-text instruction where the rule can't.
3. **Update** — post the new status/dates onto the matter record; **cite the source email; audit every docket entry** (attorney-confirmed for anything deadline-bearing — the e-discovery "human decides" rule, D-5.5).

Competitive fit: IP docketing systems (Anaqua, Clarivate/CPA Global, Alt Legal, Computer Packages, Dennemeyer) ingest **structured** patent-office data well, but **do not intelligently docket from free-text email instructions**; horizontal email tools have no docketing/deadline-rule concept and no matter model. The intersection — **AI+deterministic docketing from email, matter-grounded, cited, audited** — is open. **P0 must validate this competitive claim directly (D-12).** It is also a beachhead: prove it on IP deadlines (where ROI and pain are highest), then generalize the deadline-cascade engine to litigation dates, closing checklists, and regulatory response windows.

| Gate | The user can now… |
|---|---|
| **G-1 (Auto-route completeness)** | Every inbound email that *can* be deterministically tied to a matter / project / **invoice** / **work assignment** / document / contact **is** — not just matter/org/person (today's ceiling). Unroutable items land in an explicit queue, never silently. |
| **G-2 (Categorize + summarize + prioritize)** | Open an email (or its queue row) and see a **category**, a **2-line AI summary**, extracted **obligations** (dates/amounts/asks), and a **priority/urgency** signal — important items flagged, noise deemphasized. Produced by an **Email Triage playbook on Spaarke's existing AI platform, RAG-grounded in the matter's own correspondence** (D-2.5); deterministic pre-filters resolve the cheap majority first (cost/reliability), the AI does the substantive intelligence. |
| **G-3 (Rules engine)** | An admin configures **rules** — by sender/domain/subject/thread/category — that decide how an email is handled: auto-file, auto-categorize, create an Event/To-Do, route to a queue, or dismiss. Rules are Dataverse-configurable, auditable, and kill-switchable. |
| **G-4 (Email → work)** | Rules and dispositions can **spawn an Event/To-Do** (enhancing today's manual Outlook "Create To Do") and connect an email to downstream work (intake, Finance Intelligence) via existing Action/playbook machinery — not module-private automation. |
| **G-5 (Review audit)** | For any email, produce a **defensible audit record**: who or which rule dispositioned it, when, on what AI suggestion, at what confidence — demonstrating both **machine review** (which rung/model, when) and **human review** (who confirmed/overrode, when). Queryable per matter. |
| **G-6 (Briefing integration)** | The Daily Briefing surfaces a **triage backlog** channel — unrouted/overdue/high-priority counts — consistent with the existing 6-channel briefing design language. |
| **G-7 (Email Client)** | Open a **Spaarke Email Client** that renders captured email properly — HTML body, **threaded conversation view**, attachments, sender/recipient detail, matter/entity association, category + priority + AI summary inline — and lets the user **read, compose, reply, and forward** through the existing send pipeline (archived + tracked). Replaces the inadequate OOB model-driven form. The triage queue (G-1/G-2/G-5) is a **view within this client**, not a separate app. |
| **G-8 (Records current from email — Job B)** | For an email tied to a matter/project/invoice/etc., the system **extracts the facts that should change the record** (e.g., "closing moved to Aug 15," "settlement offer $250k," new opposing-counsel contact) and **proposes those field updates on the associated record** — with the email as cited evidence. The user **confirms in one action** and the record is updated; every proposed + applied change is audited (G-5). No re-keying from email into forms. Powered by the existing `UpdateRecordNodeExecutor` / `DataverseUpdateRecordHandler` (D-2.7), human-confirmed by default. |
| **G-9 (Email-triggered action & docketing — Job C)** | Email content **triggers the work it implies**: a configurable rule (or the AI reading a free-text instruction) turns an email into **tasks, to-dos, events, or a cascade of dated deadlines** on the right matter — the flagship being **IP Auto-Docketing** (Office Action → response-deadline ladder). Deadline-bearing entries are **attorney-confirmed** with the source email cited and every docket entry audited (D-5.5). Deterministic triggers fire without AI; free-text instructions use the Email Triage playbook. |

> Gates are operator-executed browser UAT on the dev environment; curl/tests/logs never satisfy a gate.

---

## 3. As-built foundation (grounded inventory — supersedes concept charter §4)

> **⚠️ SUPERSEDED BY §0.2 (rev 2, 2026-07-28).** This inventory was accurate on 2026-07-10 but predates `email-communication-solution-r4` shipping the Association Engine + classification substrate, `r5` designing the client, and `notification-spine-r1` landing the delivery spine. Read §0.2 for the corrected as-built truth. The text below is retained for historical rationale only.

**Two email stacks exist. This is the central architectural fact.**

### 3.1 Stack A — Communication Service (CANONICAL; the "proprietary Graph system")

`src/server/api/Sprk.Bff.Api/Services/Communication/**`, entity **`sprk_communication`**. Documented in `communication-service-architecture.md` (marked *Current*; explicitly *supersedes* Stack B).

> **Canonical model (operator-confirmed, 2026-07-10).** An email transported through Graph as an `.eml` is **wrapped in a `sprk_communication` record (type = Email)** — that record is Spaarke's **canonical representation of the email**; the `.eml` is its full-fidelity archival payload (in SPE). `sprk_communication` is a *typed* entity (Email is one type), so "Email Intelligence" scopes to Communication-of-type-Email while sharing the substrate with any other communication type. **The Spaarke Email Intelligence App is a client over this canonical record — never a raw Graph mailbox.**

| Capability | Component | Status |
|---|---|---|
| Outbound send (shared-mailbox app-only + user OBO), daily limits, approved-sender validation | `CommunicationService`, `ApprovedSenderValidator`, `CommunicationAccountService` | ✅ Shipped |
| Inbound capture via **Graph subscriptions** (webhook) + 5-min polling backup; 4-layer dedup | `GraphSubscriptionManager`, `InboundPollingBackupService`, `IncomingCommunicationProcessor`, `/api/communications/incoming-webhook` | ✅ Shipped |
| **Auto-association cascade** — thread (`In-Reply-To`) → sender (contact/account-domain) → subject regex (`MAT-\d+`, etc.) | `IncomingAssociationResolver` | ✅ Shipped — **but see §3.3** |
| Association status: **Resolved (100000000) / Pending Review (100000001)** on `sprk_associationstatus` | `IncomingAssociationResolver.ApplyAssociationAsync` | ✅ Shipped — **this is a proto-triage-status** |
| Polymorphic regarding resolver (ADR-024): `sprk_regardingrecordtype/id/name/url` | `PopulateResolverFieldsAsync` | ✅ Shipped |
| `.eml` archival to SPE, attachment processing, mailbox verification | `EmlGenerationService`, `GraphAttachmentAdapter`, `MailboxVerificationService` | ✅ Shipped |
| AI-sent email through the same pipeline | `SendCommunicationToolHandler` (`IAiToolHandler`, ADR-013) | ✅ Shipped |

### 3.2 Stack B — Email-to-Document (LEGACY; email-activity based)

`src/server/api/Sprk.Bff.Api/Services/Email/**` + `Services/Ai/Jobs/EmailAnalysisJobHandler.cs`, keyed on the Dataverse **`email` activity** → `.eml` **`sprk_document`**.

| Capability | Component | Note |
|---|---|---|
| `email` activity → `.eml` → `sprk_document`, confidence-scored association | `EmailToEmlConverter`, `EmailAssociationService`, `EmailAttachmentProcessor` | Parallel to Stack A; assumes `email` activities exist (SSS-era assumption) |
| **App-only AI analysis of email + attachments** (`AnalyzeEmailAsync`), results on the `.eml` Document | `AppOnlyAnalysisService`, `EmailAnalysisJobHandler` (job type `EmailAnalysis`) | ⚠️ **The concept charter claimed this is "not built" — it exists.** But it runs on Stack B's `email`-activity id, not `sprk_communication`. |

### 3.3 What the concept charter got wrong (correct these before building)

1. **Capture is Graph-subscription, not SSS/Exchange-layer.** "Capture independence via Exchange-layer" is not the differentiator; the proprietary Graph capture is (per operator ruling 1.1.3).
2. **`AppOnlyAnalysisService` exists** (§3.2) — but on the wrong stack for triage.
3. **`EmailFilterService` / `sprk_emailprocessingrule` are NOT in `src`.** The "shipped filter rules with Include/Exclude/Route" do not exist. **Configurable rules (G-3) are genuinely net-new.**
4. **Auto-routing to invoice/work-assignment is scaffolded, not implemented.** `IncomingAssociationResolver.RegardingFieldPriority` *declares* matter, project, **invoice, work assignment**, budget, analysis, org, person — but the three resolvers only ever **populate matter, org, and person**. Routing to invoices/work-assignments/projects/documents is a **field that exists with no resolver behind it**. This is extend-not-build, and it is most of G-1.

### 3.4 The reuse precedent — Finance/Invoice Intelligence

`x-financial-intelligence-module-r1` already ships the exact **queue → AI-classify → human-review** pattern (`InvoiceExtractionToolHandler`, `enqueue-classification-from-email-handler`) that Email Intelligence needs. **Phase 1 should clone its shape, retargeted from invoices to correspondence** — not invent a new subsystem (CLAUDE.md §11).

### 3.5 Adjacent surfaces that integrate (not rebuild)

- **Email → To-Do (manual, shipped):** Outlook add-in `CreateTodoView` / `useCreateTodoFromEmail` links a `sprk_todo` to `sprk_communication` via `sprk_regardingcommunication`; `LinkedTodosBanner` shows linkage. G-4 makes this **rule-driven**, not only manual.
- **AI send/draft/disposition:** `SendEmailNodeExecutor` (JPS node), `EmailDraftToolHandler` (`email.draft` → Draft `sprk_communication`), `EmailDispositionSender`/`OutputRouter` (ADR-040 — *note the "disposition" naming collision*, §6 D-4).
- **Daily Briefing:** `DailyBriefingCollector` (6 channels: tasks, overdue, documents, matters, projects, to-dos) — **no email channel yet**; G-6 adds a 7th following the existing collector pattern.
- **Priority scoring:** the Workspace/Portfolio domain already does "priority scoring" — evaluate for reuse before building a new urgency scorer (G-2).

### 3.6 As-built AI platform (the engine for G-2 — reuse, do NOT rebuild)

Every "intelligent" requirement (categorize, summarize, prioritize, extract obligations, suggest routing/action) is work Spaarke's **existing AI platform already does** for other artifacts. Email Intelligence should be a **new consumer of this platform**, authored in its native primitives — not a second AI implementation.

| Existing AI capability | Component / mechanism | How Email Intelligence uses it |
|---|---|---|
| **JPS playbook + Action engine** (the core AI abstraction) | `jps-action-create`, `jps-playbook-design`; `sprk_analysistool` catalog; node executors | Author an **Email Triage playbook** whose Actions produce category, summary, obligations, priority, suggested routing — as **catalog data + prompt schema, not code** |
| **Email/document analysis orchestration** | `AppOnlyAnalysisService.AnalyzeEmailAsync`, `EmailAnalysisJobHandler`, `AnalysisOrchestrationService` | The already-built async analysis entry point — re-point to `sprk_communication` and drive the Email Triage playbook (this is the concept charter's "Phase C Email Analysis playbook", which already has a handler) |
| **RAG over email + documents** | Email content is **already indexed** in `spaarke-knowledge-index-v2` (3072-dim); `RagService`, entity-scoped search | **Ground classification/summary in the matter's own prior correspondence + documents** — "is this the counsel who always emails about MAT-1234?" This is the differentiator the concept charter underplays and Copilot structurally lacks (no Dataverse matter grounding) |
| **Playbook-driven output composition + disposition** | `OutputRouter`, `EmailDispositionSender` (ADR-037/040), narrative-output pattern | Suggested-disposition-with-rationale and File+Act outputs route through existing composition, not module-private automation |
| **Write-to-record engine (the Job B engine)** | `UpdateRecordNodeExecutor`, `DataverseUpdateRecordHandler`, ADR-040 UpdateRecord disposition, `CoerceFieldValue` | **Propose field updates on the associated record from email content** (dates/amounts/parties/status), human-confirmed + audited (D-2.7) — this is how records stay current from email with no re-keying |
| **AI tool handlers / closed catalogs** | `IAiToolHandler` (ADR-013), `SendCommunicationToolHandler`, `EmailDraftToolHandler`, ADR-039 grounded execution | Reply/draft suggestions and "File+Act" spawns ride existing tool handlers; AI-sent replies already go through the same tracked send pipeline |
| **Conversational AI over scoped knowledge** | SprkChat (`ChatSessionManager`, `PlaybookChatContextProvider`, entity-scoped RAG) | P2: let a user **chat with a matter's correspondence or the triage queue** — "summarize everything opposing counsel sent this week," RAG-scoped to that matter |
| **AI cost governance** | ADR-014 (caching/reuse), ADR-016 (per-tenant budgets/backpressure) | Makes AI-at-email-volume affordable; classification results cached/reused — this is *why* deterministic-first ordering exists (cost/latency), not because AI is a last resort |

---

## 4. Core decisions

### D-1 — Consolidate the triage unit onto `sprk_communication` (validate in P0)

**Decision (proposed):** `sprk_communication` is the **Triage Item**. Triage state (status, category, priority, disposition, provenance, audit pointers) hangs off it; we do **not** introduce a parallel `sprk_triageitem` entity unless P0 finds a blocking reason. Stack B's AI-analysis capability (`AppOnlyAnalysisService`) is **re-pointed to run on `sprk_communication` / its archived `.eml`** rather than the `email` activity, and Stack B is put on a deprecation path.

**Why:** Stack A is canonical (docs say so), Graph-native (matches the proprietary-capture ruling), and already carries association + a Resolved/Pending-Review status that is 80% of a triage-status field. Two record systems for "an email" is the scope-creep §11 forbids. **Open risk:** confirm nothing live depends on Stack B's `email`-activity path (P0, D-04 below).

### D-2 — Extend `IncomingAssociationResolver`, don't replace it (the classification ladder is mostly harvest)

The concept charter's "classification ladder rungs 1–4" is **already** `IncomingAssociationResolver`'s cascade. Phase 1:
- **Implement the declared-but-missing resolvers** — invoice, work assignment, project, document (§3.3.4). This *is* G-1.
- Add **deterministic structural detectors** (court notices, e-sign completions, invoices, calendar invites) as cheap pre-filters.
- Hand everything else to the **matter-grounded Email Triage playbook** (D-2.5) for category, summary, priority, obligations, and suggested routing — the substantive intelligence, not an afterthought. Deterministic-first is cost/latency ordering, not AI-minimization.

### D-2.5 — Intelligence is authored on the existing AI platform, not built anew (the AI emphasis)

**Decision:** the categorize / summarize / prioritize / extract-obligations / suggest-routing intelligence (G-2) is delivered as an **Email Triage JPS playbook** — Actions + prompt schemas + the `sprk_analysistool` catalog — executed through the **already-built** `AppOnlyAnalysisService` / `EmailAnalysisJobHandler` (re-pointed to `sprk_communication` per D-1), and **RAG-grounded** in the matter's own correspondence + documents (already indexed in `spaarke-knowledge-index-v2`). No new model-calling code, no second analysis pipeline. Suggested outputs (routing, disposition, replies) ride the existing `OutputRouter` / `IAiToolHandler` machinery.

**Why this is a headline, not a footnote:** the concept charter framed AI as "rung 5 — the last resort." That undersells Spaarke's actual position. Deterministic-first ordering (D-2) is a **cost-and-reliability** sequencing decision (ADR-014/016), *not* a statement that AI is peripheral. The intelligence that makes this a product — matter-grounded classification, summaries, obligation extraction, priority, and eventually conversational review of a matter's mail — is **AI work, and Spaarke already has the platform to do it**. The build is *authoring playbooks and wiring a new consumer*, which is exactly the pattern the AI architecture was designed for (`ai-guide-consumer-wiring.md`, `BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md`). The moat vs. Copilot is precisely this: **RAG grounding in Dataverse matter context**, which no tenant-generic triage can reach.

**What this changes in the design:** the "classification ladder" is not deterministic-rules-with-an-AI-afterthought; it is **deterministic pre-filters that cheaply resolve the easy majority, feeding a matter-grounded AI playbook that does the substantive categorize/summarize/prioritize/extract on everything else** — and *enriches* even deterministically-routed items (summary, obligations) where policy warrants.

### D-2.7 — "File + Update": record currency is a first-class outcome via the existing UpdateRecord engine (Job B)

**Decision:** the Email Triage playbook (D-2.5) does not stop at classify/summarize — for records-relevant emails it **proposes concrete field updates on the associated record** (matter/project/invoice/contact/work-assignment), which the user confirms in the client. This is delivered through the **already-shipped** `UpdateRecordNodeExecutor` / `DataverseUpdateRecordHandler` and ADR-040 output-disposition machinery — **no new write path.** "Update the record from the email" becomes a triage outcome peer to File / Route / Dismiss (an "**Update**" or "**File + Update**" outcome, subject to D-4 naming).

**Guardrails (binding):** proposed updates are **human-confirmed by default** (never silent auto-write in Phase 1); each proposal carries the **source email as cited evidence** and a confidence; each applied change writes to the audit log (G-5, so "machine proposed / human approved" is provable); field-level scope is **allow-listed per entity** (the playbook may only propose updates to configured, safe fields — not arbitrary columns). Choice/Boolean/Number coercion reuses the `CoerceFieldValue` hardening from the Daily Briefing work.

**Why first-class:** this is Job B, the differentiator (§2.0). Filing is table stakes; *making the matter reflect the email* is the value no DMS or inbox assistant delivers — and Spaarke already owns the engine.

### D-5.5 — Trust & defensibility model (adopt the e-discovery blueprint wholesale)

The e-discovery research (§13) hands us a **court-tested, ABA-Rule-1.1-grounded** trust model — *US v. Heppner* (SDNY, Feb 2025) examined the lawyer's reasoning and safeguards, not the software's sophistication. Every AI output in this module (classification, priority, field update, docket entry) **binds to these rules**:

1. **AI flags/proposes; the human decides — never auto-finalize anything deadline-bearing, privilege-adjacent, or record-mutating.** Suggestions land in a queue for one-action confirm/override (this is also the velocity mechanism, Pillar 1).
2. **Cited rationale is the trust primitive.** Every suggestion carries a plain-language reason + **citation to the exact source email text**, and the system **verifies the cited text exists** (Relativity's anti-hallucination guardrail) — no ungrounded assertions.
3. **Confidence tiering routes only the uncertain band to humans** (Everlaw's Yes/Soft-Yes/No/Soft-No). Deterministic-class + high-confidence items can auto-apply *by policy* (P2); the ambiguous tail is where human attention is spent — this is how the queue stays short (R-4 mitigation).
4. **Draft, don't decide** — AI may draft a privilege-log entry, a docket description, a field value; the professional edits and owns it.
5. **Audit is the defensibility receipt** — every AI proposal + human decision (who/what/when/from-which-email/at-what-confidence) is logged immutably (G-5). "Receipts for human judgment."
6. **Privilege is never an autonomous determination** (ADR-015 already says this) — AI may *flag* potential privilege as a handling attribute only.

This is not new scope — it is the acceptance standard every AI feature in Pillars 1-3 must meet, and it is a **marketable differentiator** (no email tool ships this; it matters more in legal than sales CRM).

### D-3 — Rules engine is net-new, and it is the configurability spine (G-3)

There is no shipped rule engine (§3.3.3). Build a Dataverse-configured rule entity (`sprk_emailrule` or similar) evaluated in the inbound pipeline: match on sender/domain/subject/thread/category → action (auto-file / categorize / create Event-To-Do / route-to-queue / dismiss). Per-rule kill switch (ADR-018 posture). This is what makes "rules to create Event tasks" (G-4) and "configurable handling" (G-3) real. **Reuse Finance Intelligence's classification-rule shape where it fits.**

### D-4 — Disambiguate "disposition" before it hardens

ADR-040 already owns "disposition" for **AI output delivery** (`EmailDispositionSender`). Triage needs a word for **review outcome** (File / Route / Hold / Dismiss / File+Act). Pick a distinct term (e.g., *triage outcome* / *review action*) so we don't ship two collwith the same name. **Decide at spec time.**

### D-5 — Audit is a first-class, immutable record (G-5)

A dedicated append-only audit trail (`sprk_emailreviewlog` or similar): item, actor (user **or** rule/model id), action, prior AI suggestion + confidence, timestamp. This is the compliance differentiator no inbox-assistant produces. Machine-review and human-review events are **both** rows — that is what "demonstrate both human and machine reviewed" means concretely.

### D-6 — The Spaarke Email Client is the primary surface; triage is a view within it (G-7)

**Decision:** build **one** React 19 Code Page — the **Spaarke Email Client** (Fluent v9, `@spaarke/ui-components`) — not two overlapping apps. It has two primary modes over the same `sprk_communication` data:

- **Client mode** — a proper email reading/compose experience the OOB MDA form can't give: rendered HTML body (sanitized), **threaded conversation view** (group by `conversationId` / `In-Reply-To` chains already fetched in the pipeline), attachment list (from SPE-archived `.eml`), sender/recipient detail, and **compose / reply / reply-all / forward** that post through `CommunicationService` (`/api/communications/send`) so every send is archived + tracked — no separate send path.
- **Triage mode** — the queue view: category chip, priority, AI summary, obligations, suggested action + rationale, one-action confirm/override, keyboard-first bulk actions. This is the concept charter's "Workbench" — a mode, not a separate deliverable.

**Client is over the canonical `sprk_communication` record — SETTLED (operator, 2026-07-10).** The client renders Spaarke's canonical email model — `sprk_communication` (type Email) wrapping the Graph-transported `.eml` — with its matter association, triage classification, and audit. It is **not** an IMAP/Outlook personal-inbox replacement and does **not** read `me/messages`. Reads come from Dataverse `sprk_communication` (+ SPE `.eml` for full body/attachment fidelity). This is not a fallback or default-pending-validation; it is the canonical architecture: the record is the email, the client is a view over the record. Consequences: fully matter-centric, reuses all capture/association/audit/AI work, no per-user OBO mailbox-sync build.

**Surface placement (open — D-10):** standalone Code Page vs. a SpaarkeAi workspace widget vs. dual-use, per `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` / `BUILD-A-NEW-WORKSPACE-WIDGET.md`. An email client is a "sophisticated single-purpose" surface — likely a standalone Code Page that can *also* be embedded as a widget (dual-use Pattern D). Confirm in P0; drives the Hot-Path SpaarkeAi flag.

**Reuse first (§11):** compose/attachments should reuse the Outlook add-in's shipped save/compose primitives and `@spaarke/ui-components` rather than reinventing them; the Outlook add-in's placeholder Share/Search/Recent tabs remain a later convenience surface, never the capture mechanism.

---

## 5. Program structure — parallel tracks (contract-first)

This is a **program (Epic)**, decomposed into tracks that run **in parallel**, not a serial phase chain. The enabler of parallelism is **Track 0**: a thin, fast, shared **contract** layer. Once the seams are fixed, Tracks 1–4 build against **mocks/fixtures** and never block on each other's internals. (This mirrors the Daily Briefing split the operator named: deterministic collection and AI shaping are separable behind a data contract.)

### The four tracks + the foundation

| Track | Owns | Depends on | Can start | Runs parallel with |
|---|---|---|---|---|
| **T0 — Foundation & Contracts** *(short, shared, front-loaded)* | The **seams**: data model (triage fields on `sprk_communication`, `sprk_emailrule`, `sprk_emailreviewlog`); **BFF API contracts** (queue read, outcome write, propose/confirm record-update, rule CRUD); **Email Triage playbook output schema** (`{category, summary, obligations[], priority, proposedUpdates[]}`); shared TS DTOs. Plus D-1/D-04 consolidation ruling + market-research capability set (§11). | — | Now | (it *is* the unblock) |
| **T1 — UI/UX** | Spaarke Email Client Code Page (client mode + triage mode + record-update confirm UX) and workspace layouts; prototype-first in `spaarke-prototype`. **Job A surface.** | T0 contracts (mockable) | Prototype now; build on T0 contract freeze | T2, T3, T4 |
| **T2 — Communication plumbing / infrastructure** | Extend `IncomingAssociationResolver` (missing invoice/work-assignment/project/document resolvers); Stack-A/B consolidation (D-1); `.eml`→document optimization, SPE creation, AI-search indexing; capture/dedup hardening; outbound. **Produces the real data behind the T0 contract.** | T0 data model | On T0 data-model freeze | T1, T3, T4 |
| **T3 — Intelligence layer** | Email Triage **playbook** (categorize/summarize/prioritize/extract-obligations/**propose-record-updates**), RAG grounding, deterministic structural detectors, rules engine (D-3). **Deterministic collect + AI shape — the Daily Briefing pattern.** Produces classification + proposed updates behind the T0 contract. **Jobs A + B intelligence.** | T0 playbook schema + sample `sprk_communication` fixtures | On T0 schema freeze (fixtures, not live data) | T1, T2, T4 |
| **T4 — Outlook integration** | Extend the existing Outlook add-in: in-context triage/disposition, linked records, record-update confirm from within Outlook. **Convenience surface — consumes the same T0 API as T1.** | T0 API contract | After API proven in T1 (contract stable) | T1, T2, T3 (lower priority) |
| **T5 — Integration & pilot** *(convergence)* | Wire the tracks end-to-end; pilot on a real high-volume scenario (litigation/closing mailbox); measure Job A velocity + Job B update-acceptance. | T1–T3 | On first parallel slices landing | — |

### Why this is parallel, not serial

- **Only T0 is a true predecessor**, and it is deliberately thin (contracts, not implementations). Everything else forks from it.
- T1 mocks the API; T2 fills the API with real data; T3 fills the playbook output — **three teams, one contract, zero mutual blocking.**
- T1 can even start in the **prototype harness before T0 freezes** (UX exploration on mocked data), so design and contract-definition overlap.
- T4 is the only track with a soft ordering (wait for the API to prove out in T1), and even that can overlap once the contract is stable.

### Capability maturity (applies *within* each track — deliver thin first, deepen later)

Each track ships a **P1 slice** first (human-confirmed, Suggest-mode, deterministic-classes), then deepens: **P2** = rule-driven automation for deterministic classes + bulk actions + File+Act + MCP exposure; **P3** = feedback learning from overrides, detector-library growth, team/shared-mailbox queues with SLA. Maturity is a depth axis, not a serial gate — a track can be at P2 while another is still landing P1.

### Coordination (mandatory — all three code tracks touch BFF)

T2 (`Services/Communication/**`), T3 (`Services/Ai/**` + playbook catalog), T1/T4 (Code Page / add-in) are mostly **separable by directory**, but all are BFF hot-path. Run `/conflict-check` before each wave; register all tracks in `projects/INDEX.md` with hot-path declarations; the T0 contract is version-pinned so a mid-flight contract change is a coordinated event, not a silent break.

### Test focus (high-risk seams)

Matter/entity-correlation correctness (T2); **proposed-record-update safety — allow-listed fields, human-confirm, coercion** (T3, Job B); rule-action guardrails (T3); the app-only analysis path, delegated-vs-application permission separation (T2/T3); the T0 contract itself (contract tests shared by all tracks).

---

## 6. Placement Justification (root CLAUDE.md §10 — BINDING)

All server work lands in `Sprk.Bff.Api` **inside the existing Communication + Email/AI domains** — no new top-level service. Rationale per `bff-extensions.md` decision criteria:

- **New endpoints** (triage queue read, disposition write, rule CRUD) are thin minimal-APIs beside `CommunicationEndpoints` / `OfficeEndpoints`; handlers in the existing job-contract worker pattern (ADR-004).
- **AI capability** (category/summary/priority) reached via the `Services/Ai/PublicContracts/` facade — no direct `IOpenAiClient`/`IPlaybookService` injection into triage code (refined ADR-013).
- **Reuse over new:** extend `IncomingAssociationResolver`, `AppOnlyAnalysisService`/`EmailAnalysisJobHandler`, `CommunicationService`, `DailyBriefingCollector`, the **JPS playbook + RAG platform** (D-2.5), and the Outlook add-in compose primitives; clone Finance Intelligence's classifier shape. New surface is limited to: the rule entity, the audit entity, triage fields on `sprk_communication`, the **Email Triage playbook (catalog data, not code)**, and the Email Client Code Page.
- **Publish-size + CVE checks** run on every BFF-touching task (ceiling ≤60 MB compressed; baseline ~49.63 MB incl. PDBs). Category/priority AI adds no new heavy dependency (reuses the AI stack already present).

## 7. Hot-Path Declaration (root CLAUDE.md §10 G — BINDING)

<hot-path-declaration> BFF=Y (Services/Communication/**, Services/Email/**, Services/Ai/Jobs/EmailAnalysisJobHandler, new triage rule/audit/queue endpoints) · SpaarkeAi=N (Triage Workbench is a standalone Code Page, not the SpaarkeAi workspace — confirm in P0) · ci-workflows=N · skill-directives=N · root-CLAUDE.md=N </hot-path-declaration>

## 8. Component Justification (root CLAUDE.md §11 — new surface only)

| New component | Existing overlap | Extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Rule entity (`sprk_emailrule`) | None in `src` (charter's `sprk_emailprocessingrule` doesn't exist) | No existing rule surface to extend | Without it there is no configurable handling and no rule-driven Event/To-Do creation — G-3/G-4 fail |
| Audit entity (`sprk_emailreviewlog`) | `AuditEnrichmentMiddleware` logs requests, not per-email review decisions | No — request logs can't prove "this email was human+machine reviewed" | Without it there is no defensible per-email review record — G-5 fails; this is the compliance differentiator |
| Triage fields on `sprk_communication` | `sprk_associationstatus` (Resolved/Pending) already exists | **Yes — extend `sprk_communication`, not a new `sprk_triageitem`** | N/A (extension) |
| Spaarke Email Client Code Page (incl. triage mode) | OOB MDA email form (insufficient); Outlook add-in compose/save primitives; `@spaarke/ui-components` | Reuses add-in compose/attachment primitives + `CommunicationService` send path + component library; new page because no adequate read/thread/compose surface exists | The OOB form can't render HTML bodies, threads, or a clean read/compose UX, and Pending-Review items have nowhere to be reviewed — G-7/G-1/G-2 have no surface |
| Missing entity resolvers (invoice/work-assignment/project/document) | `IncomingAssociationResolver` (fields declared, resolvers absent) | **Yes — extend the existing resolver** | N/A (extension) — this is the bulk of G-1 |

## 9. ADR Tensions (root CLAUDE.md §6.5)

- **ADR-015 (privilege):** AI may *flag* potential privilege as a handling attribute; it must **never** make a privilege determination. Category-D lesson; binding constraint on the AI rung.
- **ADR-003/008 (authorization):** queue visibility must run through existing authorization seams — triage must not become a side channel around matter-level access control. **Intersects the open matter-level index-security gap** — flag if team/shared queues (P3) are pulled forward.
- **ADR-040 naming (D-4):** reusing "disposition" for triage outcomes collides with the AI-output-disposition vocabulary — resolve at spec time (this is a naming tension, not a rule violation).
- **No email-specific ADR exists** — if Phase 1 sets a durable pattern (rule engine, audit model), consider whether it warrants one.

## 10. Non-goals (Phase 1)

- **No SSS / Exchange transport work** (operator ruling 1.1.3).
- **No rebuild of send/receive/capture** — Stack A is the building block; the Email Client is a new *UI* over it, not a new send path.
- **No raw personal-inbox / Outlook replacement** — the Email Client is a view over the canonical `sprk_communication` (type Email) record, not an IMAP/`me/messages` mailbox client (settled, §3.1 / D-6).
- **No bespoke AI plumbing** — categorize/summarize/prioritize/extract are authored as JPS playbook Actions on the existing AI platform (D-7), not new model-calling code.
- **No autonomous disposition of ambiguous mail** — auto modes are deterministic-classes-only and rule-gated (P2+); Phase 1 is Suggest/confirm.
- **No new email entity** (`sprk_triageitem`) pending P0 (D-1).
- **No Outlook in-context disposition UI** (P2 convenience surface).
- **No two-sided / cross-tenant learning** (P3+, separate governance design).

## 11. Open decisions (for operator + P0)

| ID | Decision | Notes |
|---|---|---|
| D-01 | Consolidate onto `sprk_communication` vs. new `sprk_triageitem` (D-1) | Recommend consolidate; validate nothing depends on Stack B |
| D-02 | Re-point `AppOnlyAnalysisService` from `email` activity to `sprk_communication`/`.eml` — effort + risk | Blocks the AI rung |
| D-03 | Category taxonomy + priority-weight defaults — validate with the operator | Deliberately operational, Dataverse-configurable |
| D-04 | Fate of Stack B (email-to-document) — deprecate, or keep for a specific trigger? | Two stacks is the core risk |
| D-05 | Triage-outcome vocabulary (avoid ADR-040 "disposition" collision) | Naming |
| D-06 | Obligation extraction storage — structured child records vs. JSON on the item | Affects Briefing/reporting |
| D-07 | Shared/departmental-mailbox capture coverage under Graph subscriptions | P0 validation |
| D-08 | Reuse Portfolio "priority scoring" for email urgency vs. build new | §11 reuse |
| ~~D-09~~ | ~~Email Client scope~~ — **RESOLVED (operator, 2026-07-10)**: record-backed over the canonical `sprk_communication` (type Email); NOT a raw-Graph mailbox client | Settled — see §3.1 canonical-model note + D-6 |
| D-10 | Email Client surface placement: standalone Code Page vs. SpaarkeAi workspace widget vs. dual-use (Pattern D) | Drives Hot-Path SpaarkeAi flag |
| D-11 | Which existing AI surfaces to wire in P1 vs. later: Email Analysis **playbook** (categorize/summarize/extract), **RAG grounding** in matter correspondence, **SprkChat** over the queue/matter mail (D-7) | Playbook + RAG in P1; Chat likely P2 |
| D-12 | **IP Auto-Docketing competitive validation** — confirm no IP docketing vendor (Anaqua, Clarivate/CPA Global, Alt Legal, Computer Packages, Dennemeyer) intelligently dockets from **free-text email instructions** (vs. structured office data feeds) | P0 research; gates the flagship-wedge claim (§2.0b) |
| D-13 | Beachhead sequencing — lead with the **IP Auto-Docketing** vertical (highest pain/ROI, rules-heavy) vs. horizontal triage first | Recommend IP wedge as the demonstrable flagship, then generalize the deadline-cascade engine |

---

## 12. Task intake (candidate, pending P0)

| ID | Task | Depends on |
|---|---|---|
| T-1 | P0: validate two-stack consolidation (D-01/D-04), schema deltas, capture coverage (D-07), measure current resolution rate | — |
| T-2 | Extend `IncomingAssociationResolver` — implement invoice/work-assignment/project/document resolvers | T-1 |
| T-3 | Data model: triage fields on `sprk_communication`, `sprk_emailrule`, `sprk_emailreviewlog` | T-1 |
| T-4 | Rules engine + create-Event/To-Do action (clone Finance Intelligence classifier shape) | T-3 |
| T-5 | **Email Triage playbook** (JPS Actions: categorize, summarize, extract-obligations, suggest-priority, suggest-routing) — RAG-grounded in matter correspondence; deterministic detectors as pre-rungs; re-point `AppOnlyAnalysisService`/`EmailAnalysisJobHandler` to drive it on `sprk_communication` | T-1, T-3 |
| T-5b | **File + Update record-currency (Job B)** — extend the Email Triage playbook to propose field updates on the associated record (allow-listed fields, cited evidence, confidence); wire to `UpdateRecordNodeExecutor`/`DataverseUpdateRecordHandler` with human-confirm + audit | T-5 |
| T-6 | **Spaarke Email Client Code Page** — read / threaded conversation / compose / reply / attachments (client mode) + queue / disposition incl. **one-action confirm of proposed record updates** (triage mode) + audit-log wiring; reuse add-in compose primitives + `CommunicationService` send | T-3, T-5, T-5b |
| T-7 | Daily Briefing triage channel (7th channel) | T-3, T-6 |
| T-8 | (P2) SprkChat over the matter's correspondence / triage queue — RAG-scoped conversational review | T-5 |

---

*Draft for operator review. No component names herein are authoritative for implementation until P0 validation — but unlike the concept charter, this draft's as-built references (§3) are drawn from live code, not market materials.*
