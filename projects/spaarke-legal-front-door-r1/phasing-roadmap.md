# Legal Front Door — Module Phasing & Roadmap

> **Date**: 2026-07-12
> **Companion**: [`market-survey.md`](./market-survey.md)
> **Status**: Draft initiative plan for review — precedes `design-to-spec` / `project-pipeline`.

---

## Framing: this is a *composition*, not a new module

The single most important planning decision falls out of the [capability mapping](./market-survey.md#4-spaarke-capability-mapping): **9 of 17 category features already ship, 5 are extensions, only 2–3 are net-new.** So the Legal Front Door is **not** a standalone module to be built — it is a **thin new surface + data-model spine composed over existing Spaarke platform primitives**, plus a small number of genuine gaps.

Phasing therefore optimizes for **time-to-demoable-value first** (assemble what exists), then **reach** (get business users in), then **measurement**, then **AI differentiation**, with enterprise/ELM features explicitly deferred. Each phase is independently shippable and demoable.

> **Governance note.** Any phase that adds to `Sprk.Bff.Api` triggers CLAUDE.md §10 (BFF Hygiene — Placement Justification + publish-size check) and the `<hot-path-declaration>` obligation. A new request entity triggers §11 Component Justification. Both are flagged inline below and must be resolved in `design.md` before task creation.

---

## The spine decision (resolve in Phase 0 — blocks everything)

**Is a "legal request" a new first-class entity, or a state of an existing one?**

| Option | For | Against |
|---|---|---|
| **New `sprk_legalrequest` entity** (recommended) | Requests have a distinct pre-matter lifecycle (submitted → triaged → accepted/declined) and many never become matters; clean requester-facing surface; clean analytics | New entity — must clear §11 Component Justification |
| Reuse `sprk_matter` with a status | No new entity | Conflates "ask" with "accepted work"; pollutes matter analytics; awkward for declined/self-served requests |

**Recommendation**: new `sprk_legalrequest` entity with an 11-entity `regarding` relationship (mirrors `sprk_todo`), a request-type option set, a status/stage lifecycle, and a **request → matter promotion** path (accepted request spawns/links `sprk_matter`). This is the one net-new entity; everything else reuses `sprk_matter`, `sprk_todo`, work-assignment, documents.

---

## Phased roadmap

### Phase 0 — Foundation & data model *(spine — blocks all)*
**Goal**: Lock the data model, lifecycle, and taxonomy so every later phase builds on stable ground.
- `sprk_legalrequest` entity + status/stage lifecycle + request-type option set
- Regarding relationships (business unit, requester, matter, documents, work assignment)
- Request → matter promotion contract
- Security roles: requester (business user) vs legal-team vs legal-ops
- **Governance**: §11 Component Justification for the new entity; ADR Tensions review
- **Exit**: schema deployed to dev; ERD documented in `docs/data-model/`

### Phase 1 — Internal front door (legal-team MVP) *(all "Have" capabilities)*
**Goal**: Working intake + triage for the *legal team* on licensed surfaces. Fastest path to a demo.
- **Intake**: guided submission via WizardShell (`CreateLegalRequest` wizard, mirrors existing Create\* wizards)
- **Triage queue**: SmartTodo/Kanban board + DataGrid framework config (`sprk_gridconfiguration`) for the request queue
- **Routing**: Power Automate + Field Mapping / RegardingResolver rules by request type
- **Assignment**: existing **CreateWorkAssignment** wizard for internal-vs-outside-counsel — *the customer's headline ask, working on day one*
- **Matter creation**: accepted request → `sprk_matter`
- **Documents**: attach via SharePoint Embedded + SpeDocumentViewer
- **Optional accelerator**: lightweight JPS classification playbook to pre-tag type/urgency
- **Exit**: legal team can receive → triage → assign → accept a request end-to-end internally

### Phase 2 — Requester experience & visibility *(closes the #1 gap)*
**Goal**: Get *business users* in without Dataverse licenses; give them self-serve visibility.
- **External requester portal** — **Power Pages code site** (Spaarke deploys these today) with SSO: submit + track "my requests"
- **Requester status visibility** — SpaarkeAi workspace widgets / portal views
- **Notifications** — playbook `CreateNotification` / `SendEmail` destinations on status change
- **Collaboration** — surface Dataverse activities/notes as a request thread (comments, @mentions)
- **Governance**: portal auth over BFF → §10 Placement Justification + publish-size check; hot-path declaration
- **Exit**: a business user submits from the portal, gets notified, and tracks status to close

### Phase 3 — SLA & governance *(the measurement layer)*
**Goal**: Set expectations and prove value with data.
- **SLA engine** — response/resolution targets per request type on Dataverse SLA primitives + Power Automate at-risk alerts (net-new gap)
- **Analytics** — deepen DataGrid/SpaarkeAi dashboards: volume, cycle time, bottlenecks by BU/region/type; Power BI for exec reporting
- **Exit**: SLA targets configurable; at-risk alerts fire; leadership dashboard live

### Phase 4 — AI differentiation *(the competitive moat)*
**Goal**: Match/beat Sandstone/Perspective and lean on Spaarke's document-grounded AI.
- **Request-triage playbook** — JPS playbook classifies type/urgency, recommends routing/assignee
- **Conversational intake** — optional AI-interviewer front end (form-free capture)
- **Self-service deflection** — playbook + SPE retrieval answers routine asks (NDAs, templates, policy) before they reach a lawyer
- **Document-grounded assist** — draft/summarize on the customer's actual matter corpus
- **Governance**: §10 for any BFF AI surface; use `Services/Ai/PublicContracts/` facade
- **Exit**: measurable deflection rate; triage suggestions accepted by legal ops

### Phase 5 — Enterprise extensions *(deferred / demand-driven)*
**Goal**: Only if the customer needs ELM-tier depth. Kept out of core to protect the fast-deploy position.
- Teams intake channel (extend multichannel beyond Outlook/email)
- E-billing / legal-spend — **integrate** an ELM (Brightflag / CounselLink), don't rebuild
- Outside-counsel panel analytics
- **Exit**: decided per customer demand, not built speculatively

---

## Sequencing rationale

| Priority driver | How the phasing honors it |
|---|---|
| **Time-to-value** | Phase 1 is nearly all "Have" — a working internal system with minimal build |
| **De-risk the one hard gap early** | Portal (Phase 2) is the biggest lift and comes right after the internal MVP proves the model |
| **Prove value before scaling** | SLA + analytics (Phase 3) generate the ROI story before heavy AI investment |
| **Differentiate last, on a stable base** | AI (Phase 4) lands on a clean data model + real request corpus, where it performs best |
| **Protect positioning** | ELM features (Phase 5) explicitly deferred to keep Spaarke in fast-deploy Archetype A |

**Critical path**: Phase 0 blocks all. Phases 1→2→3 are sequential (each needs the prior surface). Phase 4 can begin partially in parallel with Phase 3 once the Phase-1 data corpus exists. Phase 5 is independent and demand-gated.

---

## Recommended next steps

1. **Confirm the spine decision** (new `sprk_legalrequest` entity vs. reuse) — this gates everything.
2. **Scope Phase 0 + Phase 1 as the first delivery** — the internal MVP is the cheapest credible demo.
3. Run [`/design-to-spec`](../../.claude/skills/design-to-spec/) on this roadmap → [`/project-pipeline`](../../.claude/skills/project-pipeline/) to generate the task WBS.
4. Author `design.md` with the **Placement Justification**, **`<hot-path-declaration>`**, and **ADR Tensions** sections (required — Phases 2/4 touch BFF).
