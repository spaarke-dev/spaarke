# Email Triage Module — Product Concept & Design Charter

> **Version**: DRAFT r1
> **Date**: July 10, 2026
> **Status**: Concept / pre-design. No implementation authorized from this document.
> **Evidence discipline**: Claims are tagged **[Cited]** (source identified), **[Judgment]** (assessment based on stated reasoning), or **[Open]** (unresolved; requires validation or decision). All references to Spaarke codebase components derive from `EMAIL-TO-DOCUMENT-ARCHITECTURE.md` v1.2 (Jan 17, 2026) and related architecture docs; any component not named in those documents is tagged **[PROPOSED: name]** and carries **[VALIDATION NEEDED]** before an implementation agent may act on it.

---

## 1. Problem Statement

Attorneys and legal operations teams face a compound problem that generic email tooling does not address: **volume × formality**. The volume problem (hundreds of matter-relevant emails per day per attorney) is shared with every knowledge-work profession. The formality problem is not. In legal practice, email is not merely communication — it is:

- **Record.** Correspondence is part of the matter file. An unfiled email is an incomplete matter record, a discovery risk, and a knowledge-management loss.
- **Obligation.** Emails carry deadlines, court dates, client instructions, settlement offers, and service of process. A missed email is potential malpractice, not merely inconvenience.
- **Work product trigger.** Many emails *are* the intake event for downstream legal work — a review request, an invoice, a signed agreement, a regulatory notice.
- **Billable and auditable activity.** Reading, categorizing, and responding to email is legal work that must be attributable to matters, and in firm contexts, to time.

The operational reality this module targets: in defined scenarios (litigation matters, closings, regulatory responses, claims-heavy practices, shared departmental mailboxes), **hundreds of emails must be formally reviewed, categorized, processed, acted on, and saved** — with an auditable record that each one was dispositioned. Today this is done by humans working linearly through inboxes, with filing discipline as the perpetual failure point. Industry commentary consistently identifies email filing as one of the most disliked and skipped tasks in legal practice, and unfiled email in personal inboxes as the primary cause of incomplete matter records. **[Cited: NetDocuments, LexWorkplace, Affinity Consulting market materials, 2025–2026]**

**Thesis.** The market has solved fragments of this problem — predictive *filing* (DMS vendors), inbox *prioritization* (AI email assistants), and structured *intake* (legal front door tools). No one has unified them into a formal, matter-aware **triage operation**: a queue-driven workflow in which every captured email reaches an explicit, audited disposition, with AI classifying and preparing and a human confirming. That unification is precisely the "AI-directed, human-controlled" operations posture Spaarke already occupies, running on capture infrastructure Spaarke has already built. **[Judgment]**

---

## 2. Market Survey — Email Intelligence Solutions

The competitive landscape divides into five categories. Each validates part of the demand and leaves part of the problem unaddressed.

### 2.1 Category A — DMS Predictive Email Filing (legal-specific, AI + rules)

The most mature legal-specific category. These tools answer one question: *which matter does this email belong to?*

| Vendor / Product | Mechanism | Notes |
|---|---|---|
| **NetDocuments ndMail** | ML predictive filing: suggests matters ranked by confidence based on sender, recipients, subject, and body content; rule-based auto-file by client domain; conversation filing; firm-wide duplicate detection | Widely cited as the adoption benchmark for predictive filing. Sits in the Outlook toolbar; one-click filing. **[Cited]** |
| **iManage Smart Filing / Mail Manager** | AI-suggested filing locations from recipient, subject, and content; right-click filing from Outlook; governance-heavy deployment model | Positioned for large firms with complex ethical-wall and governance requirements. **[Cited]** |
| **ZERO** | ML-based automated email filing plus adjacent automation (mobile filing, timekeeping capture from email activity) | Notable for connecting filing to *time capture* — evidence that email activity is treated as billable legal work, not just records management. **[Cited: Legaltech Hub category listing; vendor positioning]** |
| **LexWorkplace, Docsvault, M-Files, Intapp Documents** | Rules-based and assisted filing into matter-centric stores; Outlook add-ins; duplicate detection; full-text search across filed email | The mid-market tier. Docsvault explicitly markets *controlled, transparent* (non-predictive) filing as a feature for compliance-sensitive buyers — evidence of demand for deterministic behavior. **[Cited]** |

**What Category A proves.** Matter-centric email filing is a paid, universal need; predictive suggestion measurably drives adoption; duplicate detection across a team is table stakes; capture of *outbound* email matters as much as inbound.

**What Category A misses.** Filing is the *end* of these tools' ambition. They do not review, prioritize, extract obligations, route work, or produce a disposition record. The email is saved; nothing is *done*. They are also (with the exception of client-side auto-file rules) dependent on per-user Outlook add-in behavior — filing discipline remains a human habit problem. **[Judgment]**

### 2.2 Category B — General-Purpose AI Inbox Triage (horizontal, AI-first)

| Vendor / Product | Mechanism | Notes |
|---|---|---|
| **Microsoft 365 Copilot in Outlook** | Native triage engine: priority inbox, thread summarization, drafting, and (per Microsoft's April 2026 announcements) agentic capabilities — triaging email, rescheduling conflicts, surfacing what matters — grounded in Microsoft Graph within tenant permission boundaries | The most strategically significant entrant for Spaarke: it lives in the same tenant, on the same Graph, as Spaarke deployments. **[Cited: Microsoft Outlook Blog via press coverage, April 2026]** |
| **Superhuman** (Grammarly) | Split-inbox triage, AI drafting, follow-up reminders; premium per-seat pricing (~$30–40/user/mo) | Validates willingness to pay for triage speed at the individual level. **[Cited]** |
| **SaneBox, Shortwave, alfred_, Lindy, Fyxer, Spark** | Background priority scoring, thread bundling, task extraction, daily briefs, workflow triggers | Feature vocabulary worth noting: "morning brief," "task extraction," "priority scoring that learns your criteria." **[Cited]** |

**What Category B proves.** AI triage of high-volume inboxes is now a mainstream expectation; summarize-then-prioritize-then-draft is the accepted interaction pattern; buyers explicitly fear autonomous archiving/routing that buries important mail — reviewed commentary repeatedly warns against agentic triage burying a legal notice or deadline. **[Cited: multiple 2026 buyer's guides]**

**What Category B misses.** No matter context, no legal taxonomy, no disposition record, no privilege awareness, no connection between an email and the work it obligates. Priority is inferred from behavioral signals, not from the fact that *this email is opposing counsel on an active litigation matter with a response deadline*. For legal buyers, third-party tools also introduce an unacceptable data path: inbox contents leaving the tenant. **[Judgment]**

### 2.3 Category C — Legal Front Door / Intake & Triage (in-house legal, workflow-first)

| Vendor / Product | Mechanism | Notes |
|---|---|---|
| **Streamline AI** | Centralized intake from email/Slack/Salesforce; AI categorization and routing; SLA tracking; approval workflows | Markets itself explicitly on the statistic that the vast majority of legal requests arrive by email. **[Cited]** |
| **Checkbox** | "AI Legal Front Door" — understands, categorizes, and routes requests from Teams/Slack; self-service resolution for routine requests; explicit human-in-the-loop positioning for high-risk matters | Vendor claims 30–40% of requests resolvable via self-service in typical deployments. **[Cited: vendor and analyst commentary]** |
| **Xakia** | Structured intake portal + triage inside matter management; FAQ interception of routine questions | Reports large cycle-time reductions post-intake-implementation (vendor-claimed 73% reduction in days-to-completion). **[Cited: vendor claim]** |
| **Wordsmith** | "Legal front door that does the work" — captures, triages, resolves, and records every request; agentic resolution posture | The most aggressive automation posture in the category. **[Cited]** |

**What Category C proves.** *Triage as a formal workflow* — capture → categorize → route → track → record — is an established product category with proven ROI framing. The disposition/audit concept Spaarke needs is native here. The "emergency room" intake/triage metaphor is standard buyer language. **[Cited]**

**What Category C misses.** These tools triage *requests*, not *correspondence*. They work best when they can force traffic through a portal or form; email is treated as an unruly channel to be redirected, not as the primary artifact to be processed in place. None of them files to a matter-centric document record, none serves outside counsel, and none sees both sides of the firm/client relationship. **[Judgment]**

### 2.4 Category D — E-Discovery Classification (adjacent proof point)

**Relativity aiR** and peers demonstrate, at litigation scale, that LLM-based classification of email corpora for relevance, privilege, and issue coding is defensible, auditable, and commercially accepted — with transparent per-document rationale as the trust mechanism, and human review of AI-prioritized queues as the operating model (reported 30–60% review-time reductions). **[Cited: Relativity materials and third-party reviews, 2026]**

**Relevance to this module.** E-discovery proves the *pattern* Spaarke should adopt for live triage: AI classifies with stated rationale; humans confirm; every decision is recorded. It also proves that privilege detection by AI is treated as a prioritization aid, never an autonomous decision — a boundary this module must respect. **[Judgment]**

### 2.5 Category E — Non-AI / Deterministic Mechanisms (the installed base)

The unglamorous incumbents matter because they define the reliability bar and the buyer's mental model:

- **Exchange transport rules and Outlook rules** — sender/domain/subject routing; zero-cost, fully deterministic, universally deployed.
- **Shared mailboxes and folder taxonomies** — the de facto "triage queue" in most departments today.
- **DMS folder-mapping auto-file** (e.g., ndMail rules, Docsvault) — "any email from client domain X files to matter Y" without ML.
- **Journaling/archiving platforms** (Mimecast, Proofpoint, M365 retention) — compliance capture without operational value.

**What Category E proves.** A large share of triage decisions are *exactly determinable* — sender domain, thread continuity, matter correspondence lists resolve most classification without any model. Buyers trust deterministic behavior and explicitly select for it in compliance-sensitive contexts. **[Cited: Docsvault positioning; Judgment on share]** This aligns directly with the standing Spaarke principle that deterministic and probabilistic tools are first-class peers.

### 2.6 Market Synthesis — The Unclaimed Position

| Capability | Cat A (DMS filing) | Cat B (AI triage) | Cat C (Front door) | Cat D (eDiscovery) | **Spaarke Email Triage** |
|---|---|---|---|---|---|
| Matter-aware classification | ✅ | ❌ | Partial | ✅ (post hoc) | ✅ |
| Priority / urgency intelligence | ❌ | ✅ | Partial | ❌ | ✅ |
| Formal disposition & audit trail | ❌ | ❌ | ✅ (requests only) | ✅ (review only) | ✅ |
| Obligation/deadline extraction | ❌ | Partial | ❌ | ❌ | ✅ |
| Filing to matter record (SPE/DMS) | ✅ | ❌ | ❌ | ❌ | ✅ (existing pipeline) |
| Downstream action orchestration | ❌ | Partial | ✅ | ❌ | ✅ (Action Engine / playbooks) |
| Outbound email capture | ✅ | ❌ | ❌ | ✅ | ✅ (existing pipeline) |
| Tenant-resident, client-independent capture | ❌ (add-in dependent) | Native (Copilot only) | ❌ | ❌ | ✅ (Exchange-layer capture) |
| Two-sided (firm + client) intelligence | ❌ | ❌ | ❌ | ❌ | ✅ (unique) |

The unclaimed position: **email triage as a formal legal operation** — every matter-relevant email captured at the Exchange layer, classified deterministically first and probabilistically second, queued for review with AI-prepared context, dispositioned by a human (or by policy for defined low-risk classes), filed to the matter record, and connected to the work it creates — with a complete audit trail. **[Judgment]**

**The Copilot question.** Microsoft's agentic Copilot in Outlook is the only entrant with comparable tenant posture, and it will keep absorbing generic triage (summarize, prioritize, draft). Spaarke should not compete on generic triage. The defensible layer is what Copilot structurally lacks: legal taxonomy, matter correlation against Dataverse, disposition workflow, OCG/deadline awareness, filing to the matter record, and playbook-driven downstream action. Consistent with the established MCP posture, the module should be exposed so Copilot *consumes* Spaarke triage intelligence rather than Spaarke wrapping Copilot. **[Judgment]**

---
## 3. Product Concept

### 3.1 Working Name

**[PROPOSED: Spaarke Email Triage]** as the module name; the review surface is **[PROPOSED: Triage Workbench]**. Naming should follow the Action Engine naming resolution (user-facing vocabulary decisions are open there; this module should not introduce a third vocabulary). **[Open — see D-05]**

### 3.2 Positioning Statement

> Every email that touches a matter becomes a reviewed, categorized, filed, and actioned record — automatically prepared by AI, explicitly dispositioned under human control, and fully auditable.

This is not a filing plugin (Category A), not an inbox assistant (Category B), and not a request portal (Category C). It is the **operations layer between the mailbox and the matter**: the email equivalent of invoice review — a formal pipeline with intake, classification, review, disposition, and audit. It extends the "Legal Operations Intelligence" category thesis to the highest-volume artifact in legal work. **[Judgment]**

### 3.3 Core Concepts

| Concept | Definition |
|---|---|
| **Triage Item** | The unit of work: one captured email (inbound or outbound) awaiting or holding a disposition. Wraps the existing email activity + `sprk_document` pair; does not replace them. |
| **Classification** | The machine-assigned attributes of a Triage Item: matter correlation, category (taxonomy below), urgency, extracted obligations, suggested disposition. Produced by the classification ladder (§5.3). Always carries provenance (which rung produced it) and confidence. |
| **Disposition** | The explicit, recorded outcome of review. Closed set: **File** (to matter/project), **File + Act** (spawn task/playbook/Action), **Route** (reassign to person/queue), **Hold** (needs information), **Dismiss** (not matter-relevant; reason coded). Every Triage Item terminates in exactly one disposition. |
| **Triage Queue** | A scoped, ordered worklist of Triage Items (per user, per matter, per shared mailbox, per team). Ordering is urgency-first with deterministic tie-breaks. |
| **Triage Policy** | Configuration governing automation level per classification class: *Suggest* (default — human confirms everything), *Auto-file* (deterministic classes only, e.g., exact thread continuity on an already-filed thread), *Auto-dismiss* (e.g., newsletters, per seeded exclusion rules). Policies are tenant-configurable and auditable. |

### 3.4 Legal Email Taxonomy (Phase 1 candidate)

A fixed, configurable starting taxonomy — deliberately operational rather than doctrinal:

`Correspondence — Counsel` · `Correspondence — Client` · `Court / Tribunal / Filing Notice` · `Service of Process` · `Deadline / Calendar Trigger` · `Document Delivery (executed / draft / production)` · `Invoice / Billing` · `Request for Legal Work (intake)` · `Regulatory / Government Notice` · `Internal / Administrative` · `Marketing / Noise`

Each class carries default urgency weighting and default Triage Policy. Taxonomy is Dataverse-configurable per tenant. **[Open — taxonomy validation with design partners; D-02]**

### 3.5 What the User Experiences

1. **Nothing changes in Outlook.** Capture is at the Exchange layer via the existing pipeline — client-independent, no add-in dependency for capture (an add-in remains an optional convenience surface).
2. **The Triage Workbench** presents the queue: each item shows sender/matter correlation, category chip, urgency, a two-line AI summary, extracted obligations (dates, amounts, requested actions), and a *suggested disposition with stated rationale* (the Relativity-proven trust pattern).
3. **Disposition is one action.** Accept the suggestion (single keystroke/click), or override — override reasons feed back as signals. Bulk disposition for homogeneous items (e.g., 40 emails on one closing thread) is first-class.
4. **File + Act** connects triage to the platform: dispositioning a "Request for Legal Work" item can instantiate a matter-intake record; a "Deadline" item creates the calendar/task entry; an "Invoice" item routes into Finance Intelligence — via existing playbook and Action Engine machinery rather than module-private automation.
5. **The Daily Briefing integrates**, showing triage backlog, overdue high-urgency items, and auto-dispositioned counts — consistent with the established Briefing design language (metric strip; Critical Today treatment reserved for genuinely critical items).
6. **Everything is audited.** Who (or which policy) dispositioned what, when, on what AI suggestion, with what confidence — queryable per matter for defensibility.

---

## 4. Existing Foundation (What Is Already Built)

The module is an extension of shipped infrastructure, not a greenfield system. Per `EMAIL-TO-DOCUMENT-ARCHITECTURE.md` v1.2: **[Cited]**

| Capability | Component | Status |
|---|---|---|
| Exchange-layer capture (inbound + outbound) via Server-Side Sync → Dataverse email activity | Dataverse SSS + webhook on `email.Create` | ✅ Shipped |
| Reliable async processing (webhook primary, 5-min polling backup, Redis idempotency) | `EmailToDocumentJobHandler`, `EmailPollingBackupService`, `JobSubmissionService` | ✅ Shipped |
| Deterministic filter rules with **Include / Exclude / Route** actions over subject, from, to, cc, body, attachment name | `EmailFilterService`, `sprk_emailprocessingrule`, `EmailRuleSeedService` | ✅ Shipped — note the **Route (flag for manual review)** action already anticipates a triage queue |
| .eml archival to SPE + `sprk_document` record with email metadata fields (`sprk_emaildirection` covers sent/received) | `EmailToEmlConverter`, `SpeFileStore` | ✅ Shipped |
| Automatic RAG indexing of email content | `RagIndexingJobHandler` → `spaarke-knowledge-index-v2` | ✅ Shipped |
| Telemetry | `EmailTelemetry` (OpenTelemetry counters/histograms) | ✅ Shipped |
| Attachment processing as child Documents | Planned Phase A | ⬜ Not built |
| App-only AI analysis of email (no user context) | Planned `AppOnlyAnalysisService`, Phase B | ⬜ Not built |
| Email Analysis playbook (metadata + attachments combined) | Planned Phase C | ⬜ Not built |

**Implication.** The email architecture's own Phases A–C are prerequisites of, and largely subsumed by, this module. The Triage module gives those planned phases a product destination rather than existing as pipeline improvements without a surface. **[Judgment]**

---

## 5. Proposed Architecture

### 5.1 Design Principles Applied

- **Build the pipes, not the water.** Phase 1 ships the queue, disposition model, and deterministic ladder; classification intelligence deepens over time from disposition feedback.
- **Deterministic and probabilistic peers.** LLM classification is the *last* rung, not the first (§5.3).
- **AI-directed, human-controlled.** Default policy is Suggest; automation is opt-in, per class, deterministic-classes-first.
- **Job contract pattern (ADR-004).** All pipeline stages are job-contract handlers; the Workbench is a thin surface over Dataverse state.
- **MCP exposure posture.** Triage state and operations exposed via MCP so Copilot/Teams surfaces consume the module.

### 5.2 Pipeline

```
Exchange (SSS) ──▶ email activity ──▶ webhook/polling (existing)
                                         │
                                         ▼
                            EmailFilterService (existing)
                            Include / Exclude / Route
                                         │
                     ┌───────────────────┴───────────────────┐
                     ▼                                       ▼
        EmailToDocumentJobHandler (existing)     [PROPOSED: TriageClassificationJobHandler]
        .eml → SPE → sprk_document → RAG          runs classification ladder (§5.3)
                     │                                       │
                     └───────────────┬───────────────────────┘
                                     ▼
                    [PROPOSED: sprk_triageitem] (Dataverse)
                    classification + suggestion + provenance
                                     │
                    ┌────────────────┼────────────────────┐
                    ▼                ▼                    ▼
            Triage Policy      Triage Workbench      Daily Briefing /
            auto-disposition   (React 19 Code Page)  MCP consumers
            (deterministic     human disposition
             classes only)          │
                    └────────┬──────┘
                             ▼
              [PROPOSED: TriageDispositionJobHandler]
              File / File+Act / Route / Hold / Dismiss
              → matter linkage, task/playbook/Action spawn,
                audit record, feedback signal
```

All new handlers follow the existing idempotency pattern (Redis keys, e.g., `Email:{emailId}:Triage`). **[VALIDATION NEEDED: confirm no naming collision with existing idempotency key conventions]**

### 5.3 Classification Ladder (deterministic-first)

Each rung either resolves an attribute with full confidence or defers downward. Provenance is recorded per attribute.

| Rung | Mechanism | Resolves | Cost |
|---|---|---|---|
| 1 | **Filter rules** (existing `EmailFilterService`) | Exclude/noise classes; forced-include | ~0 |
| 2 | **Thread continuity** — Message-ID / In-Reply-To / References against already-filed emails (`sprk_emailmessageid` exists on `sprk_document`) | Matter correlation for replies on filed threads — the single highest-volume case | ~0 |
| 3 | **Participant correlation** — sender/recipients against matter contacts, counsel records, client domains in Dataverse | Matter correlation; Counsel vs Client category | Low (cached per ADR-009) |
| 4 | **Structural detectors** — deterministic parsers for known formats (court e-filing notices, e-signature completions, invoice attachments, calendar invites) | Category + obligations for machine-generated mail | Low |
| 5 | **LLM classification** (planned `AppOnlyAnalysisService` + Email Analysis playbook, per existing Phases B/C) — email + attachment content, RAG-grounded in matter context | Ambiguous matter correlation; category; urgency; summary; obligation extraction; suggested disposition with rationale | Model cost — governed by ADR-016 budgets |

Rungs 1–4 are expected to fully resolve a majority of volume; rung 5 handles the remainder and *enriches* (summary, obligations) even deterministically-classified items where policy warrants. **[Judgment — share to be measured; instrument from day one]**

### 5.4 Data Model (Dataverse) — [PROPOSED, VALIDATION NEEDED]

| Entity | Purpose | Key fields (indicative) |
|---|---|---|
| `sprk_triageitem` | The triage unit | email lookup, document lookup, matter lookup (suggested + confirmed), category, urgency, status (Pending/InReview/Dispositioned), disposition, disposition source (Human/Policy), rationale text, classification provenance JSON, confidence, assigned queue/user, SLA timestamps |
| `sprk_triagepolicy` | Per-class automation config | class, mode (Suggest/AutoFile/AutoDismiss), scope (tenant/team/mailbox), enabled flag (kill-switchable per ADR-018) |
| `sprk_triagedispositionlog` | Immutable audit trail | item, actor, action, prior suggestion, override reason, timestamp |
| (existing) `sprk_emailprocessingrule` | Rung-1 rules | unchanged; Route action feeds the queue |

Taxonomy and urgency weights as configuration rows, not code. Obligation extractions (dates/amounts/asks) stored as structured child records or JSON — decision deferred. **[Open — D-03]**

### 5.5 Surfaces

- **Triage Workbench** — React 19 Code Page, Fluent UI v9, `@spaarke/ui-components`. Queue list with compact divider rows (Briefing design language), right-aligned relative-time chips, per-row disposition actions; full-card treatment reserved for critical items. Keyboard-first bulk disposition.
- **Daily Briefing tile** — backlog metrics + Critical Today integration.
- **Outlook add-in (Phase 2+)** — optional in-context disposition; convenience surface only, never the capture mechanism.
- **MCP server exposure (Phase 2)** — `list_triage_queue`, `get_triage_item`, `disposition_item` tools so Copilot/Teams and internal agents consume the module. Consistent with the Insights Engine MCP posture.

### 5.6 Outbound Email

The existing pipeline already captures sent mail (`sprk_emaildirection`). Triage treatment differs: outbound items default to **auto-file** on thread continuity (rung 2) with no review burden, surfacing only when matter correlation fails. Outbound triage is a filing-completeness feature, not a review workflow. **[Judgment]**

### 5.7 ADR Compliance Mapping

| ADR | Application |
|---|---|
| ADR-001 | New endpoints as minimal APIs in `Sprk.Bff.Api`; handlers in workers |
| ADR-004 | Classification and disposition as deterministic job contracts |
| ADR-009 | Matter-contact correlation snapshots cached Redis-first |
| ADR-013–016 | LLM rung governed by AI architecture, caching/reuse, data governance, and cost/backpressure ADRs; classification results cached and reused per ADR-014 |
| ADR-015 | Privilege posture: AI may *flag* potential privilege as a handling attribute; it never makes privilege determinations (Category D lesson) |
| ADR-018 | Triage Policies and the LLM rung individually kill-switchable |
| ADR-003/008 | Queue visibility enforced through existing authorization seams — triage must not become a side channel around matter-level access control. Intersects the open matter-level index security gap in the AI architecture. **[Open — D-06]** |

---

## 6. Differentiation Summary

1. **Capture independence.** Exchange-layer capture means completeness by architecture, not user discipline — the failure mode every Category A tool inherits from add-in dependence.
2. **Two-sided intelligence.** With both firm-side and client-side deployments, triage classification can eventually learn from cross-boundary patterns (e.g., counsel correspondence norms, invoice cadences) no single-sided vendor can observe. Phase 2+ opportunity; requires the established cross-tenant governance posture. **[Open — D-07]**
3. **Triage terminates in work, not storage.** File + Act connects email to matter intake, tasks, Finance Intelligence, and playbooks — the Action Engine is the downstream, so the module ships thin.
4. **Defensible record.** Disposition audit per email is a compliance artifact no inbox assistant produces and no DMS filing tool attempts.
5. **Copilot-complementary.** Via MCP, Spaarke supplies the legal-operations context Copilot's generic triage lacks, rather than competing with Microsoft on summarization.

---

## 7. Phasing

| Phase | Scope | Exit criterion |
|---|---|---|
| **P0 — Discovery** | Codebase inventory (mandatory Phase 0): validate all [PROPOSED]/[VALIDATION NEEDED] items; measure current email pipeline volume and rung-1/2/3 resolvability on real data; confirm attachment Phase A status | Validated component map; measured deterministic-resolution rate |
| **P1 — Triage core** | `sprk_triageitem` + queue + disposition model + rungs 1–3 + Workbench (Suggest mode only) + audit log + Briefing tile | 100% of Route-flagged and unresolved emails reach explicit disposition; full audit trail |
| **P1.5 — Enrichment** | Attachment processing (existing Phase A), `AppOnlyAnalysisService` (Phase B), Email Analysis playbook (Phase C) as rung 5; summaries, obligations, suggested dispositions with rationale | Suggestion acceptance rate measurable; per-item AI cost within ADR-016 budget |
| **P2 — Automation + reach** | Triage Policies (auto-file/auto-dismiss for deterministic classes), bulk disposition, MCP exposure, Outlook add-in surface, File+Act playbook/Action integration | Auto-disposition share with zero misfiled-matter incidents in pilot; Action spawn round-trip working |
| **P3 — Intelligence** | Feedback learning from overrides; structural detector library growth; shared-mailbox/team queues with SLA; two-sided pattern exploration | Deterministic + learned resolution share rising release-over-release |

Tests concentrate on high-risk seams: matter-correlation correctness, privilege-flag handling, auto-disposition guardrails, delegated vs. application permission separation in the app-only path.

---

## 8. Risks

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| R-1 | **Misclassification with automation on** — auto-filing to the wrong matter contaminates the record; worse than not filing | High | Suggest-by-default; auto modes restricted to deterministic rungs; per-class kill switches; misfile is a re-file operation with audit, never a delete |
| R-2 | **Privilege mishandling** — AI summary/routing exposes privileged content to wrong queue members | High | Authorization seams enforced at queue level (ADR-003/008); privilege flag restricts summary visibility; ties to matter-level index security gap **[Open]** |
| R-3 | **Copilot absorption** — Microsoft ships legal-adjacent triage in Outlook | Medium | Compete on matter context, disposition workflow, and audit — not on summarize/draft; MCP-complement posture; monitor Build announcements |
| R-4 | **Queue becomes a second inbox** — if disposition is slower than reading email, adoption dies | High | Keyboard-first bulk actions; rung 1–4 auto-resolution keeps the human queue short; SLA metrics visible; pilot with a real high-volume scenario before broad release |
| R-5 | **LLM cost at email volume** | Medium | Deterministic-first ladder; ADR-014 caching; enrichment gated by policy and class; per-tenant budget enforcement (ADR-016) |
| R-6 | **App-only AI path is net-new** — `AppOnlyAnalysisService` remains unbuilt; OBO assumptions elsewhere don't transfer | Medium | Sequenced as P1.5 dependency with its own design review; reuse shared components per existing plan |

---

## 9. Open Decisions

| ID | Decision | Options / Notes |
|---|---|---|
| D-01 | Relationship between Triage Item and the Category C "matter intake" concept — is a `Request for Legal Work` disposition the front door, or does Spaarke's existing matter-intake feature own that? | Avoid two intake systems; likely File+Act spawns the existing intake record |
| D-02 | Phase 1 taxonomy — validate class list and urgency defaults with design partners | §3.4 candidate list |
| D-03 | Obligation extraction storage — structured child entities vs. JSON on the item | Affects reporting and Briefing integration |
| D-04 | Shared mailbox capture path — SSS coverage for shared/departmental mailboxes vs. Graph subscription | **[VALIDATION NEEDED — P0]** |
| D-05 | Naming — module and surface names pending Action Engine vocabulary resolution | §3.1 |
| D-06 | Queue authorization model — per-matter visibility inside a mixed queue; intersects matter-level index security gap | Blocks team queues (P3) if unresolved |
| D-07 | Two-sided learning governance — what cross-boundary signals are permissible, anonymization posture | P2+ only; explicit governance design required |
| D-08 | Suggestion acceptance target — what acceptance/override rate gates enabling auto modes | Propose: measured in P1.5 pilot before P2 policy work |

---

## 10. Task Intake (candidate, pending P0)

| ID | Task | Depends on |
|---|---|---|
| T-1 | P0 codebase inventory: validate [PROPOSED] components, idempotency conventions, shared-mailbox capture (D-04) | — |
| T-2 | Volume + resolvability measurement: instrument rungs 1–3 against production-representative email corpus | T-1 |
| T-3 | Data model design: `sprk_triageitem`, `sprk_triagepolicy`, `sprk_triagedispositionlog` | T-1 |
| T-4 | `TriageClassificationJobHandler` + rungs 2–3 (thread continuity, participant correlation) | T-3 |
| T-5 | Triage Workbench Code Page (Suggest mode) | T-3 |
| T-6 | Disposition handler + audit log + Briefing tile | T-4, T-5 |
| T-7 | Attachment processing (existing Phase A) — prerequisite for rung 5 | T-1 |
| T-8 | `AppOnlyAnalysisService` + Email Analysis playbook as rung 5 (existing Phases B/C) | T-7 |

---

## 11. Evidence Register (market survey sources)

Vendor and analyst materials reviewed July 2026: NetDocuments (ndMail product and email-management guide), iManage (Smart Filing / Work), ZERO (Legaltech Hub category listing), LexWorkplace, Docsvault comparative material, Affinity Consulting DMS guidance, Microsoft Outlook Copilot coverage (agentic triage, April 2026), Superhuman/Grammarly, SaneBox, Shortwave, alfred_, Lindy and 2026 buyer's-guide roundups, Streamline AI, Checkbox, Xakia, Wordsmith, Relativity (aiR product materials and third-party reviews), DLA Piper AI-intake analysis (Perspective AI). Vendor-claimed metrics (e.g., Xakia 73% cycle-time reduction, Checkbox 30–40% self-service resolution, aiR 30–60% review-time reduction) are reported as claims, not validated benchmarks.

---

*Prepared for review. No component names herein may be cited authoritatively by implementation agents prior to P0 validation.*
