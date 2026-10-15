# Legal Front Door — Solution Overview and Approach

**Spaarke Module Concept · Draft for review**

---

## 1. Current landscape

The legal front door is now an established solution segment: a single point where the business accesses legal services, blending self-service with connection to counsel. Four vendor clusters define the market. Enterprise service-delivery platforms (ServiceNow Legal Service Delivery) apply the ITSM model — service catalog, self-service portal, counsel work queue, SLA discipline, virtual agent. Purpose-built intake platforms (Streamline AI, Checkbox) lead with multi-channel capture (Slack, Teams, email, Salesforce), AI classification, and conversational entry. Matter-management suites (Xakia, LawVu) offer intake as the entry point to a broader toolkit, with unlimited free requester seats as a pricing norm. The fourth option — building on the organization's existing ecosystem — is recognized by advisors as the fastest, lowest-friction path for Microsoft-standard enterprises. Spaarke productizes that fourth option.

The feature set has converged: multi-channel capture, structured intake, classification and routing, self-service deflection, requester-visible status, SLA tracking, a legal-side triage queue, request-to-matter conversion, and demand analytics. Differentiation now lives in *how* intake happens, not *whether* these boxes are checked. Two market weaknesses are exploitable: every vendor still makes the requester carry the structural burden (pick the right form, fill the right fields), and none is natively inside the Microsoft tenant with governed, provenance-carrying AI.

## 2. User expectations

**The business requester** does not want to interact with legal. The request is an interruption to their actual job, often carries mild anxiety, and any friction — choosing among request types they don't understand, fifteen required fields, silence after submission — teaches them to route around the front door and email a lawyer they know. Their expectations, stated plainly: don't make me classify my problem in your vocabulary; don't ask me anything you already know or could read from the document in my hand; take minutes, not a session; tell me who has it, what happens next, and when; and if you need more from me, ask once.

**The law department** needs complete, structured requests routed to the right person at the right priority — without the intake process damaging the department's standing with the business. Its expectations: no request lost or invisible; classification and routing it can trust and audit; workload and demand data that stands up to leadership scrutiny; deflection of the routine without ceding legal judgment to a chatbot; and a clear boundary between business self-service and business self-lawyering.

The apparent tension — ease of submission versus guided completeness; client satisfaction versus necessary information — dissolves once the requester stops being the source of structure. That is the premise of the design.

## 3. A new way to think about the process

**From form to interview.** The reference experience is TurboTax, not a ticketing form. TurboTax operates in a domain users fear and resent, never exposes the underlying taxonomy, asks plain-language intent questions, imports everything derivable, and still produces fully structured, compliant output. The front door does the same: a short guided interview in business language, with the legal taxonomy operating entirely behind the curtain.

**The catalog is infrastructure, not interface.** A closed Request Type Catalog remains the deterministic spine — it drives routing, SLAs, workflow, and reporting. But the requester never picks from it. Analysis of intake schemas shows request types differ enormously in downstream workflow and barely at all in the intake core (who, what activity, when needed, counterparty, documents). One adaptive intake with progressive disclosure replaces the per-type form library; classification is established by the system and confirmed by legal, not selected by the requester.

**Structure moves to the system.** Completeness is a triage-exit criterion, not a submission-entry criterion. Identity, business unit, and region derive from Entra. For contract-type requests — the volume majority — the attached document already contains most fields: counterparty, value, term, governing law. Document-first intake inverts the interaction: drop the file, extraction populates, the requester confirms three things instead of typing fifteen. Whatever remains is gathered in one consolidated follow-up on the request thread, never a drip of clarifying questions. Requester effort goes down as data quality goes up, because extracted values beat hand-typed ones.

**Self-service is not self-lawyering.** The line between them is a policy envelope, encoded deterministically, authored by legal. Green: parameters inside the pre-approved envelope (our-paper NDA, standard terms) auto-resolve with full logging — the judgment was exercised once, when legal defined the envelope. Yellow: near the boundary, the system drafts and an attorney releases with one click — same-day speed, judgment retained. Red: always counsel. This is procurement's guided-buying model applied to legal: nobody calls a catalog purchase "self-procuring." Every auto-resolution carries provenance — which rule, which template version, which corpus answer — converting deflection from a supervision risk into a defensibility asset.

**Deterministic spine, probabilistic assist.** Rules classify first; AI classifies the residue as a suggestion behind a one-click confirmation gate. The front door works completely — and is sellable — with no AI at all. Everything probabilistic layers on top.

## 4. Build phases

**Phase 1 — The pipeline.** Request record and closed catalog in Dataverse; single adaptive intake (web + Teams personal app); deterministic routing engine in the BFF; legal-side triage queue in the three-pane model with confirmation-gated dispositions; "My Requests" status surface with honest state machine and threaded communication; immutable audit log; request-to-matter conversion into the existing wizard. No AI. Proves the pipes; ships a complete front door.

*Gate to Phase 2: routing rule coverage and triage cycle time measured in production; D-01 (requester surface licensing model) resolved before Phase 1 build begins.*

**Phase 2 — Channels and intelligence at the edges.** Document-first intake using the existing extraction pipeline; email and Outlook add-in channels riding the Email Triage classification ladder (dependency: Email Phases A–C; one shared engine, two entry contexts); SLA policies with legal-held vs. business-held time distinction; demand analytics deterministically queried and narrated in the Daily Briefing; AI classification suggestions in the triage queue behind confirmation gates.

**Phase 3 — Guided autonomy.** Conversational intake that converges to the catalog and pre-fills the interview for confirmation; knowledge deflection with citation-bearing answers from the legal-governed corpus; self-service document generation with green/yellow/red envelope routing. Built on the Action Engine, so the front door becomes a demonstration of Spaarke orchestrating itself.

Each phase is independently valuable; no phase depends on a later one to justify itself.

## 5. UI direction

**Two surfaces, two registers, one design system.** The question "website versus internal system" resolves by persona, and the answer should be held firmly on both sides.

**The requester surface leans website — specifically, a quiet service website, not a marketing page and not a ticketing console.** Most employees will touch it a few times a year; it is the only Spaarke surface they will ever see, which makes it a brand surface as much as a workflow surface. Register: generous whitespace, larger type, plain business language, a single clear starting point ("What do you need?"), and consumer-grade status tracking modeled on order tracking — stage indicator, assigned attorney by name, what happens next, with the "waiting on you" state visually loud. What it must *not* have: hero imagery, feature marketing, dashboards, legal vocabulary, or any visible administrative machinery. The fluff to strip is decorative, not spatial — calm and uncluttered is the point; sparse is not the same as dense. Three tasks only: start a request, check my requests, respond to legal.

**The legal surface is unambiguously an internal system** — the existing three-pane model, information-dense, keyboard-fast, zero ceremony. Triage queue and request detail in the Workspace pane as canonical state; the Assistant narrating and brokering ("six overnight, four auto-routed, two need your call") with single-keystroke confirmation-gated dispositions; requester context, prior requests, and related matters in the Context pane. Triage presents as a briefing to act on, not an inbox to empty.

Both surfaces sit on Fluent UI v9 with the same token set and the single accent color; the requester surface simply spends more space and warmth within it. The discipline that holds the whole design together: the requester surface hides all structure, and the legal surface exposes all of it — one data model underneath, two honest presentations of it.

---

*Open decisions carried forward: D-01 requester surface/licensing model (Power Pages vs. BFF-backed Teams/Code Page — blocks Phase 1); D-02 front door / Email Triage engine boundary (codify before Phase 2); D-03 deflection corpus governance (resolved in principle by the envelope model; formalize before Phase 3).*
