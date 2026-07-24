# Legal Front Door — Market Survey & Spaarke Capability Mapping

> **Prepared for**: Spaarke product & a corporate-legal prospect
> **Date**: 2026-07-12
> **Scope**: 16 vendors across 5 archetypes, 17 feature areas
> **Companion artifact**: rendered version published to claude.ai (see project notes)
> **Status**: Product-strategy input, not a procurement recommendation. Vendor ratings are directional, synthesized from public positioning.

---

## 0. Executive summary

"Legal front door" — also sold as **legal intake & triage** — is now a distinct software category, not a feature. It solves one recurring pain: business users fire legal requests into email, Slack, and hallway conversations, and the legal team has no structured queue, no visibility, and no data on turnaround. The reference vendor, [Streamline AI](https://www.streamline.ai/product/intake), was purpose-built for this problem; a dozen credible competitors now surround it, ranging from intake-only specialists to full enterprise legal management (ELM) suites.

**The strategic read for Spaarke**: the category's feature set maps unusually well onto capabilities Spaarke already ships — Dataverse first-class entities, the wizard/intake framework, the SmartTodo/Kanban work surface, the DataGrid framework for queues, the SpaarkeAi widget dashboards for visibility, and the JPS + Azure OpenAI layer for AI triage and narrative status. The **CreateWorkAssignment wizard already models internal-vs-external counsel assignment** — the exact resourcing capability this customer asked for.

The genuine build gaps are narrow and known: an **external requester-facing portal** (business users aren't Dataverse-licensed), an **SLA engine**, and optionally **conversational AI intake** and **e-billing**. Everything else is assembly, not invention.

---

## 1. The landscape — five vendor archetypes

The market splits by how much of the legal lifecycle a vendor tries to own past intake.

| Archetype | Description | Deploy time | Vendors |
|---|---|---|---|
| **A — Intake-first specialists** | Purpose-built front door: multichannel capture, dynamic forms, triage, routing, request analytics | 2–4 weeks | Streamline AI · Checkbox · Sandstone · Perspective AI · mot-r |
| **B — All-in-one in-house suites** | Intake is the entry to matter management, CLM, and spend in one workspace | 3–6 months | LawVu · Xakia · Dazychain · Lawcadia |
| **C — Enterprise Legal Management** | Heavyweight ELM: 100s of workflows, e-billing, outside-counsel & matter mgmt at scale | 6+ months | Onit · Mitratech (Acuity ELM) · LexisNexis CounselLink · Brightflag · Litify |
| **D — Horizontal service delivery** | General enterprise workflow/ITSM configured for legal; strong routing/SLA, weak legal depth | Varies | ServiceNow Legal Service Delivery · Jira Service Mgmt · Tonkean · monday.com |
| **E — CLM-anchored** | Contract lifecycle platforms with intake bolted on the front | Varies | ContractPodAi · Ironclad · SpotDraft |

**Spaarke's natural position**: Archetype A executed on an owned Dataverse platform — the fast-deploy front door — while reaching into B/C capabilities (matter management, resourcing, documents) that Spaarke already owns natively, without the customer buying a second suite.

---

## 2. Vendor survey

Front-door fit = suitability specifically as a business-user intake/triage front door for a corporate legal department (not overall product strength). Ratings directional.

| Vendor | Archetype | What it is | Differentiator | Fit |
|---|---|---|---|---|
| **Streamline AI** (reference) | A | Purpose-built legal intake, triage & workflow automation for in-house teams | Prebuilt legal workflows live in weeks; AI email intake; knowledge bot | ★★★★★ |
| **Checkbox** | A | No-code intake & triage; general automation engine adapted to legal | Deep no-code flexibility; shared with procurement/HR/IT | ★★★★☆ |
| **Sandstone** | A | AI-native "operating system": intake → execution | Conversational (form-free) intake; business-context layer; AI playbooks | ★★★★☆ |
| **Perspective AI** | A | Conversational AI interviewer replacing static forms | AI asks follow-ups, qualifies matter, captures "why now" | ★★★★☆ |
| **mot-r** | A | Intake front-end (mot-r Q) wired to a matter/ops back-end | Clean intake-to-ops handoff in one product family | ★★★★☆ |
| **LawVu** | B | In-house workspace: matter mgmt + intake + CLM + spend | One consolidated interface across the lifecycle | ★★★★☆ |
| **Xakia** | B | Matter management where an intake request *is* the matter | Tight intake-to-matter identity; strong dashboards | ★★★★☆ |
| **Dazychain** | B | End-to-end in-house matter mgmt incl. external panel mgmt | Panel/outside-counsel management built in | ★★★☆☆ |
| **Lawcadia** | B | Intelligent matter intake & triage with automation logic engine | Rules-driven triage; strong outside-counsel workflow | ★★★☆☆ |
| **Onit** | C | ELM: 200+ workflow apps incl. legal service requests | Breadth & scale; e-billing; enterprise integrations | ★★★☆☆ |
| **Mitratech / Acuity ELM** | C | Full ELM: service requests, matters, e-billing, doc collab | Mature enterprise footprint; spend depth | ★★★☆☆ |
| **LexisNexis CounselLink** | C | Corporate matter management + outside-counsel spend | Outside-counsel analytics; deep e-billing | ★★☆☆☆ |
| **Brightflag** | C | AI-powered spend & matter management for in-house | Invoice AI review; spend visibility | ★★☆☆☆ |
| **ServiceNow LSD** | D | Legal Service Delivery on the ServiceNow platform | Enterprise portal, routing, SLA; cross-dept | ★★★☆☆ |
| **Tonkean** | D | Process-orchestration layer for cross-functional legal | Flexible no-code orchestration; meets requesters in their tools | ★★★☆☆ |
| **ContractPodAi** | E | CLM platform with intake layer for contract-heavy teams | Intake tied directly to contract execution | ★★☆☆☆ |

**Pricing signals** (only two surfaced publicly): Streamline AI ~$22.9K (Pro) / ~$26.9K (Enterprise) per year; Onit ~$40–50/user/mo, enterprise from ~$50K/yr.

---

## 3. Feature taxonomy — the 17 capabilities that define the category

### Layer 1 — Capture & qualify
1. **Multichannel intake** — web portal, email, Teams/Slack, Salesforce, Outlook
2. **Guided dynamic forms** — conditional, no-code, request-type-specific
3. **AI / conversational triage** — classify type, urgency, route; some replace forms with an AI interviewer
4. **Self-service & playbooks** — deflect routine asks (NDAs, templates, policy lookups)

### Layer 2 — Route & run
5. **Routing & assignment rules** — by type, expertise, workload
6. **Approval workflows** — multi-step, conditional sign-off
7. **Matter creation / management** — accepted request → tracked matter
8. **Resource assignment — internal & outside counsel** — assign in-house or external; panel management
9. **Task & work management** — Kanban / queue views; prioritization
10. **Document management & generation** — attach, store, auto-generate; DMS/CLM handoff

### Layer 3 — See & communicate
11. **Requester status visibility** — business users track own requests
12. **Notifications** — automated status updates
13. **SLA configuration & tracking** — response/resolution targets, at-risk alerts
14. **Collaboration** — threaded comments, @mentions, embedded conversation

### Layer 4 — Measure & govern
15. **Analytics & dashboards** — volume, cycle time, bottlenecks by BU/region/type
16. **Legal spend / e-billing** — outside-counsel invoices, spend visibility (ELM-tier)
17. **Security, permissions & audit** — confidentiality, RBAC, audit trail

---

## 4. Spaarke capability mapping

Legend: **Have** = ships today · **Partial** = extend existing · **Gap** = net-new build.

| Feature area | Spaarke capability | Notes | Status |
|---|---|---|---|
| Multichannel intake | Office Add-ins (Outlook/Word), ribbons, BFF endpoints, Power Pages code site | Outlook add-in + email intake exist; Teams channel is the main add | **Partial** |
| Guided dynamic forms | WizardShell + Create* wizards | CreateMatter/Project/Event/Todo/WorkAssignment already model multi-step guided capture | **Have** |
| AI / conversational triage | JPS playbooks + Azure OpenAI + AnalysisOrchestrationService | Classification/summarization pipeline exists; request-triage playbook + optional chat intake is new | **Partial** |
| Self-service & playbooks | JPS playbook system, narrative-output consumers, SPE retrieval | Playbook + retrieval already answers/deflects; front-door self-serve UX to be wired | **Partial** |
| Routing & assignment rules | Dataverse + Power Automate, Field Mapping Framework, RegardingResolver | Rule-based routing; field mapping auto-populates/inherits | **Partial** |
| Approval workflows | Power Automate / Dataverse BPF | Native platform capability | **Have** |
| Matter creation / management | `sprk_matter` first-class entity + 11-entity regarding model | Matters/projects/events/todos already first-class | **Have** |
| Resource assignment (internal & outside counsel) | CreateWorkAssignment wizard | Purpose-built to assign internal staff or external counsel — exact customer request | **Have** |
| Task & work management | SmartTodo Code Page + Kanban, `sprk_todo` | Queue/Kanban triage board built; subgrids, Outlook todo ribbon | **Have** |
| Document management & generation | SharePoint Embedded + SpeDocumentViewer | Container storage, viewer, upload wizards, AI grounding | **Have** |
| Requester status visibility | SpaarkeAi workspace + widgets, DataGrid framework | Dashboard renders "my requests"; external-portal surface is the gap | **Partial** |
| Notifications | Playbook CreateNotification / SendEmail destinations, Daily Briefing widget | Notification + email destinations exist; narrative digests proven | **Have** |
| SLA configuration & tracking | Dataverse SLA / custom entity + Power Automate | No dedicated SLA engine today; buildable on Dataverse SLA primitives + alerts | **Gap** |
| Collaboration | Dataverse activities / notes, workspace panes | Comments/notes/activity feed native; thread UI to surface | **Partial** |
| Analytics & dashboards | DataGrid framework + SpaarkeAi widgets, Power BI | Grid configs + widgets for volume/cycle-time | **Have** |
| Legal spend / e-billing | — (integrate ELM or defer) | Out of core scope; integrate Brightflag/CounselLink or phase later | **Gap** |
| Security, permissions & audit | Spaarke Auth v2 (SSO) + Dataverse security model | RBAC, audit, confidentiality native; SSO across surfaces | **Have** |

**Tally**: 9 Have · 5 Partial · 2–3 Gap.

---

## 5. Build gaps — what Spaarke would actually need to build

**Priority build list**
- **External requester portal** — business users typically aren't Dataverse-licensed. A Power Pages code site (Spaarke already deploys these) gives them a branded submit-and-track surface with SSO. *Highest-value gap.*
- **SLA engine** — response/resolution targets, at-risk alerts, per-request-type turnaround expectations on Dataverse SLA primitives + Power Automate.
- **Request-triage playbook** — a JPS playbook that classifies type/urgency and recommends routing; optionally a conversational intake front end.
- **Teams intake channel** — extend multichannel capture beyond the existing Outlook/email path.

**Deliberately out of scope for a first release**: e-billing / legal spend (integrate an ELM if needed) and deep outside-counsel panel analytics. These are ELM-tier features that pull Spaarke away from the fast-deploy Archetype-A position where it's strongest.

---

## 6. Positioning

Spaarke isn't buying into a crowded intake market — it's exposing a front door on a platform the customer would already run for matters, documents, resourcing, and AI. Three advantages the point-solutions can't match:

1. **One platform, not a second silo.** Intake specialists hand off to *someone else's* matter management. In Spaarke the request, matter, documents, assigned counsel, and AI all live on the same Dataverse spine — no integration tax.
2. **Resourcing is already solved.** The customer explicitly asked for internal-vs-outside-counsel assignment. CreateWorkAssignment does this today; most Archetype-A vendors don't, and Archetype-C vendors that do require a heavyweight ELM purchase.
3. **AI that acts on your own documents.** JPS playbooks + SPE retrieval mean triage, drafting, and status narratives are grounded in the customer's actual matter corpus — deeper than a bolt-on knowledge bot.

**Recommended framing**: *"the legal front door as a native surface of your legal-operations platform"* — the intake experience of Streamline AI, backed by matter management, resourcing, and document AI Spaarke already owns, with only a requester portal and SLA layer to build.

---

## Sources

[Streamline AI](https://www.streamline.ai/product/intake) · [Sandstone](https://sandstone.com/blog/legal-intake-software) · [Checkbox](https://www.checkbox.ai/platform/legal-intake-and-triage) · [LawVu](https://lawvu.com/workspace/intake/) · [Xakia](https://www.xakiatech.com/blog/best-in-house-legal-software) · [Dazychain](https://www.dazychain.com/) · [Onit](https://www.onit.com/blog/six-features-for-legal-intake-software/) · [ServiceNow](https://www.servicenow.com/products/legal-service-delivery.html) · [Perspective AI](https://getperspective.ai/blog/legal-intake-software-2026-platforms-for-law-firms) · [G2 — Streamline alternatives](https://www.g2.com/products/streamline-ai/competitors/alternatives)
