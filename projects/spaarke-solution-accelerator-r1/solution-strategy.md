# Microsoft for Legal — Platform Enablement Program: Solution Strategy

> **Status:** Draft v0.3 — strategy, for owner review. The build design for T0/T1 is `design.md` in this folder; references are in `references.md`.
> **Date:** 2026-10-29 (v0.1–v0.3)
> **Author:** Ralph Schroeder + Claude Code
> **Inputs:** `projects/spaarke-ontology-platform-r1/notes/spaarke-ontology-strategy-synopsis-v2.md` §2.5b (v2.3); `spaarke-research/2026-10-spaarke-overview/spaarke-business-plan-v4.md` §3, §8, §9; `spaarke-research/2026-10-spaarke-method/the-spaarke-method-published.md`; researcher notes dated 2026-10-29 in `.claude/agent-memory/researcher/` (`ms-copilot-for-legal-cowork-registry`, `copilot-for-legal-mechanics-walkthrough`, `copilot-legal-enablement-kit-deployment-paths`, `copilot-ux-components-and-kit-package-anatomy`, `foundry-work-iq-delta`, `dataverse-m365-copilot-platform-delta`).
> **Tags:** `[Cited]` verified against Microsoft primary sources 2026-10-29 · `[Judgment]` · `[Open]` owner decision · `[Verify]` must be tested in a live tenant before we promise it.

> **v0.2 change note.** From the owner review of v0.1:
> 1. **Law firms are an equal audience.** The kits serve *attorney work*, in a firm or a department alike (§4.1).
> 2. **Copilot UX components added** (GA 2026-10-28) as the interactive layer of the admin tier (§6.3).
> 3. **New §3**: how Copilot for Legal works in Word and in Cowork, and where each piece lives in M365.
> 4. **New §6.5**: exactly what ships — the files in a kit release, per tier.
> 5. **`mike-workflows` explained** (Appendix A).
> 6. **Foundry made the third runtime**, with a defined role (§6.1), instead of an optional add-on.

> **v0.3 change note.** (1) New §3.0 defines Microsoft's products precisely: "Copilot for legal" is the umbrella over the legal plugin in Word and Legal for Cowork; Cowork is a separate agent product. (2) §6.3 corrected: MCP Apps UI **does** render in Cowork (GA 2026-09-08); only the SPFx UX components don't. D9 now recommends MCP Apps. (3) Moved into project `spaarke-solution-accelerator-r1`.

---

## 1. Summary

Most legal departments and law firms already pay for Microsoft Copilot. Since 2026-10-15 those seats include **Copilot for Legal**: a legal skill set in Word and a 20-skill Legal plugin for Cowork `[Cited]`. What Microsoft ships is horizontal. It has no practice content, no knowledge of the customer's own positions, playbooks only as `.docx` attachments supplied per review, and no notion of a matter. The gap between "we have Copilot" and "Copilot does our diligence or lease review the way we do it" is the work Spaarke's forward-deployed legal engineers do.

**The program packages that work as enablement kits** for the *attorney work* side of legal operations intelligence: practice skills, playbooks, knowledge, tools, interactive components and connectors, deployed into the customer's own tenant on the Microsoft AI it already licenses. A kit works with no Spaarke platform at all, and every artifact has a defined upgrade into the platform.

The design rests on six decisions:

1. **Attorneys in firms and departments alike.** Contract review, diligence, abstraction and drafting are the same task in both; only the buyer and the entry path differ.
2. **Author once, deploy to many surfaces.** The canonical kit format is the Agent Skills format (`SKILL.md` + companion files + MCP connectors). Cowork imports it natively, Claude runs it, `atk` packages it for admin deployment, and the same source renders Word playbooks and, on the platform, JPS Actions.
3. **Three Microsoft runtimes, each with a job.** Word for document-level review and redlines. Cowork (and Copilot Chat) for multi-step, multi-document work. Foundry for unattended, scheduled and high-volume work.
4. **Four complexity tiers**, from "an attorney in an afternoon" to "the Spaarke platform". Each tier adds one dependency: a Copilot seat, then a shared SharePoint library, then IT, then the platform.
5. **Calculations are tools, not prompts.** Deadlines, rent schedules, patent terms and invoice arithmetic run as deterministic MCP tools: the Spaarke Method's rule that work which must return the same answer every time uses a calculation.
6. **Kits are the new first rung of the offer ladder**, below Quick Start: business-plan component 3 ("the customer's own AI") delivered early, on Microsoft's runtime.

---

## 2. Why now

| Signal | What it means for the program |
|---|---|
| **Copilot for Legal GA 2026-10-15**, included in the M365 Copilot licence; Cowork metered in Copilot Credits `[Cited]` | Customers have the runtime and have paid for it. Our kit makes it useful on day one. |
| **ILTA 2026: Copilot at 76% of firms, 88% of 700+ lawyer firms; only 52% in full deployment** `[Cited, synopsis §2.4]` | Firms are the most Copilot-saturated legal buyers, and the bottleneck is realizing value, not access. |
| **Microsoft's 25+ launch partners are largely firm vendors** (iManage, NetDocuments, HighQ, Litera, Harvey, Legora…) `[Cited]` | The firm ecosystem is converging on Cowork as its agent surface. |
| **Cowork accepts user-uploaded plugins and converts Claude-format plugins automatically** `[Cited]` | An attorney who would otherwise reach for Claude can get the same skill-driven experience inside Microsoft, under the firm's or department's identity and data protections. |
| **Plugin registry (2026-09-30); Copilot UX components GA (2026-10-28)** `[Cited]` | Tenant admin upload takes minutes, without store validation, and the admin tier can now carry real interactive UI. "Days, not weeks" is technically achievable. |
| **Microsoft's legal skills are generic**: playbooks as `.docx`, no saved playbook library in Word, no matter object, Information Barriers unsupported for plugin management `[Cited]` | The practice-specific, customer-specific and governance layer is open. That is where legal engineering adds value. |

---

## 3. How Copilot for legal works

Sources: Microsoft admin page "Manage Copilot for legal" (Learn, 2026-10-13), support "Get started with Copilot for legal" and the Copilot for Legal transparency note, Learn Cowork pages (updated 2026-10-21 to 10-29), MC1509799. Detail and citations: `copilot-for-legal-mechanics-walkthrough-2026-10-29.md` and `copilot-legal-taxonomy-and-practice-pack-dev-refs-2026-10-29.md`. GA rolls out until mid-December 2026, so tenants differ.

### 3.0 What is what

**"Copilot for legal" is not Word.** It is Microsoft's umbrella name for **two legal plugins**: *"Two legal plugins are available: one for Copilot in Word and one for Copilot Cowork"* (admin page); *"a set of legal capabilities for Copilot in Word desktop and Cowork"* (transparency note). Cowork itself is a separate, general-purpose Copilot product that the legal plugin runs inside.

| Term (Microsoft's) | What it is | Runs in | Paid by | Can we extend it? |
|---|---|---|---|---|
| **Microsoft Copilot for legal** | Umbrella over the two legal plugins below | Word desktop + Cowork | M365 Copilot licence | — |
| **Legal plugin in Word** (policy: "Legal Skills in M365 Copilot in Word") | Legal skills built into the Copilot pane in Word: playbook review, redlines, drafting from precedent | **Word desktop only** | M365 Copilot licence; on by default | **No.** Only input is the `.docx` playbook the user attaches |
| **Legal for Cowork** (MC1509799 says "Cowork for Legal"; Learn says "Legal for Cowork") | A built-in plugin of 20 legal skills that a user adds inside Cowork | Wherever Cowork runs | Copilot Credits | **Alongside it**: our plugins sit next to it |
| **Copilot Cowork** | *"Agentic AI that completes multistep work across Microsoft 365"*: a general agent mode, not legal-specific | Web (copilot.cloud.microsoft), Windows/macOS app, mobile | Copilot Credits (usage-based billing) **and** an M365 Copilot licence; access granted by a spending policy | **Yes.** User skills, user-uploaded plugins (skills + MCP connectors), MCP Apps UI |
| **Copilot Chat** | Copilot's conversational experience | Web/app, Teams, Outlook | Basic is included; full grounding needs the licence | Through agents (Agent Builder, declarative agents) |
| **Copilot in Word** | Copilot inside Word | Word | M365 Copilot licence | No custom skills documented for Word |

**Terms we use in our documents:** "Copilot for legal" for the umbrella; "the legal plugin in Word" for the Word part; "Legal for Cowork" for the Cowork part; "Cowork" for the agent product. Never "Claude Cowork" (Anthropic's product) or "Cowork for Legal". **Plugin** = an installable package of skills and connectors. **Skill** = a folder containing a `SKILL.md`. **Connector** = a remote MCP server inside a plugin (not a Copilot/Graph connector). **Practice pack** = our term (§4.2).

**So, for a kit:** the legal plugin in Word is a *consumer* of our content: we hand it playbooks. Cowork is our *platform*: we install skills, connectors and UI into it.

### 3.1 The legal plugin in Word

It is **built into the Copilot pane in Word desktop**, not an add-in. There is nothing to install, it is on by default, and it is governed by a Cloud Policy ("Legal Skills in M365 Copilot in Word") in the M365 Apps admin center. Users can turn it off under ⋯ › Manage plugins & skills. Licence: M365 Copilot.

**The playbook review, step by step:**

1. The attorney opens the contract in Word desktop, opens Copilot and asks for a playbook review. There is no separate "Legal" button; Copilot detects a legal document and uses its legal skills.
2. Copilot asks for the playbook. It **must be a `.docx`**, uploaded or attached from OneDrive or SharePoint.
3. Copilot parses the playbook into **topics, each with rules and example clauses**. The attorney checks and edits them, then clicks **Run Review**.
4. Results stream in topic by topic, each showing the rules it applied. Non-compliant clauses get proposed edits; **Accept** or **Accept All** inserts them as **native tracked changes**.

Other legal skills in Word: Q&A and summaries with clickable citations to the source text; single-issue redlines as tracked changes; drafting from a precedent (`@Draft-new-contract`), which turns the precedent into a placeholder template and fills it.

**What Word does *not* do (documented):** review against templates or other documents (playbooks only); custom or partner skills in Word (not documented); comments from the legal review (not documented); Mac or web (only "Word desktop" is documented). **Not documented:** where parsed rules or the analysis are kept, or whether playbook edits persist between reviews. **Consequence for kits:** the playbook *library* — current, versioned, practice-specific `.docx` playbooks the attorney attaches each time — is ours to provide.

### 3.2 Legal for Cowork, inside Cowork

**Cowork is Copilot's multi-step agent mode.** It opens from Microsoft Copilot › Cowork, in the browser (copilot.cloud.microsoft), the Copilot desktop app (Windows, macOS) and mobile.

**Getting it:** an admin grants access by creating a **Copilot Credits spending policy** that includes Cowork (the policy *is* the access control; minimum 2,000 credits per user per month). The attorney then adds **Legal for Cowork** from Customize › Roles & Industry › Legal.

**The 20 legal skills** `[Cited, names from Learn]`, grouped:

| Group | Skills |
|---|---|
| Contracts | review-contract, edit-contract, create-playbook, draft-new-contract, summarize-redlines, compare-key-terms, analyze-related-contracts, extract-obligations-and-deadlines |
| Drafting quality | check-cross-references, check-defined-terms, check-execution-readiness |
| Advisory and research | draft-legal-memo, explain-legal-concepts, legal-reasoning, compare-across-jurisdictions, identify-regulatory-gaps |
| Matters and corporate | build-case-chronology, summarize-legal-matters, draft-minutes-and-resolutions |
| Meta | improve-legal-prompt |

**How a task runs:** the attorney invokes a skill with `/`, from the `+` menu, or just by asking. Cowork runs it as a multi-step agent with a progress panel, Input and Output folders, and an isolated workspace inside the M365 service boundary. It can pull in work context — files, people, email, Teams chats and meetings — through Work IQ, always under the user's own permissions. Sends, posts and edits to shared files need the user's approval.

**Outputs:** new Word, Excel, PowerPoint or PDF files go to the user's **OneDrive Cowork folder**; existing files in OneDrive or SharePoint are edited in place with version history.

**Also:** Projects (instructions + context + outputs; **private to the creator**); up to 25 scheduled prompts and event triggers (email, Teams, non-Microsoft connectors), defaulting to draft-and-approve; a model picker (GPT, Claude, Grok — **EU Data Boundary tenants get GPT only**); partner plugins (iManage, NetDocuments, Harvey…) that sit alongside the legal skills, each needing a one-time per-user connector sign-in. How a legal skill combines with partner data is left to the agent at runtime; no documented recipe exists.

### 3.3 Component map

```
                           ┌──────────────────── Customer Microsoft 365 tenant ─────────────────────┐
 Attorney ── Word desktop ─┤ Copilot pane ─► legal skills (built in) ─► Copilot service             │
                           │      └─ output: tracked changes in the document, analysis in the pane    │
                           │      governed by: Cloud Policy, M365 Apps admin center                  │
                           │                                                                         │
 Attorney ── Copilot app ──┤ Cowork (agent mode, isolated workspace)                                 │
   (web/desktop/mobile)    │   ├─ plugins = skills (SKILL.md) + connectors (remote MCP)               │
                           │   │    ├─ Legal for Cowork (Microsoft, 20 skills)                        │
                           │   │    ├─ partner plugins ──MCP──► iManage / NetDocuments / …            │
                           │   │    └─ Spaarke kit plugin ──MCP──► Spaarke Kit Tools (§6)             │
                           │   ├─ Work IQ / Microsoft Graph ─► OneDrive · SharePoint · Outlook · Teams │
                           │   └─ output: OneDrive "Cowork" folder, or files edited in place          │
                           │                                                                         │
                           │ Governance: M365 admin center › Agents › Tools (plugin registry, MCP    │
                           │ consent, per-tool toggles) · Copilot Credits spending policy (gates     │
                           │ Cowork) · Purview (audit, DSPM for AI, labels, eDiscovery)              │
                           └─────────────────────────────────────────────────────────────────────────┘
```

### 3.4 Marketing vs documentation

Microsoft's launch post mentions drawing on approved precedents in SharePoint, reusable playbooks, "matters", and partner plugins inside Word. **None of these has a documented step today.** The documented flows have the user attach files, playbooks are not saved, there is no matter object, and partner plugins are documented for Cowork only. These are exactly the gaps the kits and, at T3, the Spaarke platform fill — and they are worth stating plainly in sales, because customers will hit them.

---

## 4. The offering

### 4.1 Who it is for: attorneys in firms and departments

The kits serve the **attorney work** side of legal operations intelligence: review, drafting, diligence, abstraction, chronology, invoice review. That work is the same in a firm and in a department, so T0–T2 are designed for both. What differs is who buys, where the kit enters, and what the T3 upgrade means.

| | Law firm | Corporate legal department |
|---|---|---|
| **Typical buyer** | Innovation / knowledge-management lead; practice-group leader | Legal operations director; deputy GC |
| **IT gate** | Firm CIO / CISO; often stricter, often Information Barriers | CIO / legal technology lead |
| **Natural entry** | T0/T1 for practice groups *without* Information Barriers; **T2 for walled firms** | T0/T1 for individual teams, T2 department-wide |
| **Existing assets the kit draws on** | Knowledge lawyers' precedents and playbooks; DMS (iManage, NetDocuments) via partner plugins | Policies, contract standards, outside-counsel guidelines, e-billing data |
| **Best-fit packs** | M&A, CRE, Patents (prosecution) | Invoice Review, CRE (portfolio), Patents (portfolio), M&A (in-house deal team) |
| **T3 upgrade means** | The firm's own platform, and the **engagement edge**: the client's matter record shared with the firm (business plan §6) | The full Spaarke platform: matters, policies, obligations, decision record |

`[Judgment]` Firms may be the faster early market for T0–T2: they are more Copilot-saturated, they already staff knowledge lawyers who maintain exactly what a kit packages, and their launch-partner DMS vendors are already in Cowork. Departments remain the stronger T3 market.

### 4.2 What a kit is

A kit is a versioned package with three layers, merged at install time:

```
┌────────────────────────────────────────────────────────────┐
│  Customer overlay  — the customer's positions, house style,│  produced in the
│  fallbacks, escalation contacts, approved precedents       │  enablement sprint
├────────────────────────────────────────────────────────────┤
│  Practice pack     — M&A · Commercial Real Estate ·        │  Spaarke-authored,
│  Patents · Invoice Review · (later: NDA/commercial,        │  maintained
│  employment, privacy, litigation…)                         │
├────────────────────────────────────────────────────────────┤
│  Core              — legal foundation skills, governance,  │  Spaarke-authored,
│  setup interview, eval harness, admin + adoption guides    │  shared by every pack
└────────────────────────────────────────────────────────────┘
```

Updates to Core and to practice packs never overwrite the customer overlay. That separation is what makes the subscription maintainable.

### 4.3 Complexity tiers

| Tier | Who installs | Time | Depends on | What the customer gets | Surfaces |
|---|---|---|---|---|---|
| **T0: Attorney self-serve** | The attorney | Hours | M365 Copilot seat; Cowork enabled (a spending policy exists); no Information Barriers on the user | Practice-pack **skills plugin** uploaded in Cowork › Customize and shared with named colleagues; a **playbook library** (`.docx`) in the attorney's OneDrive for Word's "Attach from Cloud"; a **prompt pack**; checklists as skill companion files | Cowork, Word |
| **T1: Practice team** | Practice lead or knowledge lawyer, no IT | 1–2 days | T0 + a SharePoint site the lead owns | T0, plus a shared **practice library** (playbooks, precedents, clause library, checklists, customer overlay) uploaded as a ready folder tree; **SharePoint site skills** in the library (any Edit user can add them, View users run them) `[Cited]`; an **Agent Builder agent** per practice grounded on the library (≤ 100 SharePoint files), shared with the group | Cowork, Word, Copilot Chat, SharePoint |
| **T2: Firm- or department-wide, IT-deployed** | Customer IT + Spaarke legal engineer | 3–5 days | Copilot admin; AI Admin/Global Admin for MCP consent | T1, plus: a **tenant package** uploaded by admin and assigned to a security group; **Spaarke Kit Tools** MCP connector (calculations) carrying **MCP Apps UI** for interactive review (§6.3); partner connectors the customer already licenses; optional **Foundry** agents for scheduled and batch work (§6.1) | + interactive UI in Cowork and Chat, Foundry |
| **T3: Spaarke platform** | Spaarke engagement (Quick Start onward) | Per business plan | Spaarke platform in the customer's tenant | The same skills, now bound to the ontology through the **Spaarke MCP server**: matter context, Policy versions, Obligations and Invoices as governed objects, the Decision Record; UX components write decisions back | + Spaarke Console, Word/Outlook add-ins |

`[Judgment]` T0 and T1 are the "use Microsoft instead of Claude" tiers and the marketing engine. T2 is the paid enablement product. T3 is the platform.

**Caveats that shape what we promise** `[Cited]` `[Verify]`:
- Admins **cannot turn off** user plugin sharing in Cowork, but conservative firms may block or audit uploads once they notice. T0 ships with an IT one-pager so it reads as governed, not as shadow AI.
- **Information Barriers block plugin and skill upload entirely.** Walled firms enter at T2.
- Cowork **Projects are private to their creator**, so team context lives in shared skills and the SharePoint library, never in Projects.
- **SharePoint `.agent` files are being retired** (no new ones from mid-November 2026; existing ones stop after March 2027). The kit uses Agent Builder agents and site skills instead.
- A site owner **cannot create** SharePoint site templates without an admin, so T1 ships its library as a folder tree the lead uploads, not as a provisioning template.
- Whether a user-uploaded plugin's MCP connector works without admin consent is **untested**. Until verified, tools are a T2 feature.
- **EU Data Boundary tenants get GPT models only** in Cowork. Skills must not depend on a specific model.

### 4.4 Practice packs (initial four)

| Pack | Skills (illustrative) | Knowledge / templates | Deterministic tools (T2+) | Firm / department use | Spaarke upgrade (T3) |
|---|---|---|---|---|---|
| **M&A** | Diligence review with a tabular extraction set (change of control, assignment, exclusivity, MFN, termination, IP ownership); SPA/APA review against playbook; disclosure-schedule drafting; issues list; closing checklist and signing/closing tracker | Buyer- and seller-side playbooks; diligence request list; closing checklist; precedent index | Materiality and threshold calculators; closing-date and long-stop calculators | **Firm:** deal teams, diligence at scale. **Dept:** in-house deal team, integration obligations | Deal as a Matter; post-closing obligations as Obligation objects; issue decisions recorded |
| **Commercial real estate** | Lease review (tenant- or landlord-side); lease abstraction (dates, rent, options, CAM, assignment); estoppel/SNDA review; amendment comparison | Lease playbooks; abstract template; estoppel checklist | **Rent-schedule and escalation calculator**; **critical-date calculator** (notice windows, options, expirations) | **Firm:** real estate practice, portfolio transactions. **Dept:** lease portfolio management | Critical dates and options as Obligations with owners; portfolio view |
| **Patents** | Invention-disclosure triage; claim-chart drafting; office-action analysis and response outline; FTO checklist; portfolio summary | Disclosure form; response templates; jurisdiction notes | **Patent term and annuity-deadline calculators** (jurisdiction rules) | **Firm:** prosecution practice. **Dept:** IP counsel, portfolio and annuity decisions | Deadlines and annuity decisions as Obligations + Decision Record; docket bridge |
| **Invoice review** | Billing-guideline compliance review (block billing, vague entries, staffing, rate and expense rules); budget-vs-actual narrative; reduction-letter drafting | Billing-guideline playbook built from the guidelines; reduction-letter templates | **LEDES parser and line-item arithmetic**; rate-card and cap checks; budget variance | **Dept:** outside-counsel spend review. **Firm:** pre-bill review against each client's guidelines | Spaarke Finance Intelligence (built): invoices and spend signals as objects; approve/reduce decisions recorded |

`[Judgment]` Two pilot packs prove the two ends of the model. **CRE lease review proves T0/T1.** It is strong on skills alone, it serves firms and departments equally, and a seed exists (Appendix A). **Invoice review proves T2/T3.** It needs deterministic tools and a UX component, it matches the business plan's spend lead offer, and a built platform module sits behind it. Invoice review also has a firm angle (pre-bill review against client guidelines). M&A and Patents follow.

### 4.5 Core components (shared by every pack)

- **Legal foundation skills:** review-against-playbook, build-a-playbook (positions → `.docx`), issues list, key-terms extraction to a table, document comparison, chronology, matter brief, and a citation and verification discipline any pack skill can reference. These *extend* Microsoft's 20 legal skills with the customer's positions; they don't duplicate them.
- **Setup interview skill.** It walks the practice lead through positions, fallbacks, escalation and house style, and writes the **customer overlay** as files the customer owns.
- **Governance set:** human-review and confidentiality guidance in every skill; an IT one-pager (what's installed, data flows, how to remove it); Purview-auditable install steps.
- **Evaluation harness.** Each pack ships a question and document set; the sprint adds the customer's own, following the Spaarke Method's build-phase test. It reuses `infrastructure/ai-foundry/evaluation/` and runs in Foundry (§6.1).
- **Admin and adoption kit:** install guides per tier, pre-flight checklist, training prompts, and a usage baseline so we can show value.

### 4.6 The service wrapper: the enablement sprint

Forward-deployed delivery compressed to one week: the Spaarke Method run once, for one practice.

| Day | Method phase | Output |
|---|---|---|
| **0 (remote)** | Pre-flight | Licence and policy check: Copilot seats, Cowork spending policy, Word legal skills on, Information Barriers, MCP consent owner, EU Data Boundary. Tier confirmed. |
| **1** | Envision | Install Core and the practice pack; choose the first two or three use cases; baseline (time and quality today) |
| **2–3** | Design + build | Setup interview → customer overlay; playbooks rendered; library curated; tools and UX components connected (T2) |
| **4** | Build test | Evaluation on the customer's own documents and questions, timed against the baseline; fixes |
| **5** | Deploy | Team rollout, training, handover; usage measurement starts |

After the sprint comes the **kit subscription**: pack updates, Microsoft change tracking (the surfaces change monthly), new skills, and quarterly re-evaluation. It can be part of Spaarke managed services or sold alone.

### 4.7 Commercial shape `[Open]`

This follows the business plan's principles: AI runs at cost on the customer's own agreement (Copilot seats and Cowork credits are theirs), and business users are never charged per seat. Options to test with design partners:

- **A.** T0 Core free (lead generation); practice packs and T1/T2 paid.
- **B.** Fixed-fee enablement sprint per practice, plus an annual kit subscription per practice pack, with the sprint fee credited toward a platform Quick Start.
- **C.** Kits only inside managed services.

Recommendation: **B**, with a free T0 sample skill per pack as marketing. For firms, price per practice group, since that is how firms budget for knowledge work.

---

## 5. Where the pieces run, at a glance

| Kit piece | T0 | T1 | T2 | T3 |
|---|---|---|---|---|
| Skills (SKILL.md) | Cowork (user upload) | + SharePoint site skills | + admin-deployed tenant package | + bound to Spaarke MCP |
| Playbooks (`.docx`) | OneDrive | SharePoint practice library | same | Rendered from Policy versions |
| Knowledge | Skill companion files | SharePoint practice library + Agent Builder agent | + partner DMS connectors | + Spaarke documents and matters |
| Tools (calculations) | — | — | Spaarke Kit Tools (MCP) | Spaarke MCP server |
| Interactive UI | Markdown tables in chat | same | MCP Apps via our connector (Cowork, Chat); SPFx components optional (Chat, Teams) | + decisions written back |
| Scheduled / batch work | — | — | Foundry agents (customer Azure) | + Spaarke background jobs |

---

## 6. Component architecture

### 6.1 Three runtimes, each with a job

| Runtime | Job in the kit | Why this runtime |
|---|---|---|
| **Word (Copilot for Legal)** | Document-level review and redline against a playbook; drafting from a precedent | It is where the document is and where tracked changes belong. Not extensible beyond the attached `.docx` playbook, so the kit supplies the playbook library. |
| **Cowork / Copilot Chat** | Multi-step, multi-document work: diligence across a data room, lease abstraction, chronologies, invoice review, issues lists | Accepts skills and MCP connectors (Cowork from user upload; Chat from admin deployment); reaches mail, files and meetings through Work IQ |
| **Microsoft Foundry** (customer's Azure) | **Unattended work**: scheduled sweeps (deadline monitoring, monthly invoice batches), **high-volume extraction** (hundreds of leases or contracts), **evaluation** of packs against the customer's documents; the long-run home of the customer's own agents (business plan component 3) | Copilot and Cowork run only when a user starts them and meter per user. Foundry Routines (GA 2026-09-24) run on a schedule under the agent's own identity, with model choice for cost (GPT-6 Luna at $0.10/$0.50 per 1M tokens) and parallel runs `[Cited]` |

Foundry constraints `[Cited]`: it needs the customer's Azure subscription and IT, so it starts at T2. A Foundry agent can be published into Copilot only within the tenant that hosts it, so it is deployed into the customer's own Azure, never Spaarke's. Foundry IQ (agentic retrieval) remains extractive-only at GA and is not needed for kits; T0–T2 retrieval is Microsoft's own (Work IQ, SharePoint grounding).

### 6.2 Single source, many targets

```
                      ┌─────────────────────────────────────┐
                      │   KIT SOURCE (Spaarke-owned repo)    │
                      │   skills/*/SKILL.md + companions     │
                      │   playbooks/*.yaml (positions)       │
                      │   library/ (folder tree manifest)    │
                      │   tools/ (MCP tool contracts)        │
                      │   ux/ (UX component sources)         │
                      │   foundry/ (agent + routine defs)    │
                      │   evals/ · overlay schema · metadata │
                      └──────────────┬──────────────────────┘
                                     │  kit build pipeline
                                     │  validate → render → evaluate → sign
  ┌───────────┬───────────┬──────────┼──────────┬────────────┬──────────────┐
  ▼           ▼           ▼          ▼          ▼            ▼              ▼
 Plugin     Word        Library    Tenant     UX comp.     Foundry        Spaarke JPS
 .zip       playbooks   folder     package    .sppkg       deployment     Actions +
 (skills)   (.docx)     tree +     (.zip,     (SPFx)       (Bicep/azd)    Policy seeds
                        site       connectors)
                        skills
 T0         T0          T1         T2         T2           T2             T3
```

`[Judgment]` One source stops the skills, the Word playbooks and the platform Actions drifting into three copies. It is the kit-level version of the existing rule "compiled once for everyone, never per customer" (synopsis §9.4).

### 6.3 Interactive UI in Copilot: MCP Apps and Copilot UX components

Microsoft has two ways to put real web UI (tables, forms, grids, buttons) inside Copilot. Both use the **MCP Apps** model: an iframe in the Copilot canvas, driven by tools Copilot calls. They differ in who hosts them and where they render. `[Corrected v0.3 — v0.2 said interactive UI could not appear in Cowork; that is true only of the SPFx variant.]`

| | **MCP Apps** (UI served by our MCP server) | **Copilot UX components** (SPFx 1.24) |
|---|---|---|
| Status | GA in Cowork (Learn `mcp-apps-support`, 2026-09-08) and in declarative agents (2026-09-30) | GA 2026-10-28 |
| Renders in | **Cowork**, Copilot Chat (via a declarative agent) | Copilot Chat and Teams (browser, desktop) only |
| Not in | Word; mobile not documented | **Cowork**, Word, mobile ("planned", no dates) |
| Hosted by | Spaarke, alongside the MCP connector | The customer's SharePoint tenant app catalog |
| Delivered in | The plugin's MCP connector, so it travels with a Cowork plugin | A separate `.sppkg`, installed by an admin |
| Licensing | Cowork: Copilot Credits; Chat: M365 Copilot licence | M365 Copilot licence for every user |
| Notable limits | Cowork: 64 KiB inline result, no model-context updates, domains listed in `validDomains[]`; no file upload or modals | 10 KB context; no documented direct tool call from the UI; tenant-wide SharePoint API grant |

**Recommendation (D9): MCP Apps.** Cowork is the kit's primary surface, and only MCP Apps render there. It also keeps the UI in the same connector as Kit Tools. SPFx components are worth adding only if a customer standardizes on Copilot Chat or Teams. Because the UI comes from our MCP server, it needs the connector, so it is T2 by default. It moves into T0 only if a user-uploaded plugin's connector works without admin consent `[Verify]`.

**Kit components** (T2; write-back to Spaarke only at T3):

| Component | Mode | What it does |
|---|---|---|
| **Contract-review issues table** | Full screen | Each issue with the playbook rule, risk and proposed edit; accept, reject or comment; export the decisions to Word as redline instructions |
| **Lease / contract abstract card** | Inline | Key terms and dates at a glance; "open in Word" / "open in Spaarke" |
| **Invoice exception grid** | Full screen | Line items that breach guidelines, with reason codes; approve, adjust or reject; totals computed by Kit Tools |
| **Obligations and deadlines tracker** | Full screen | Filter, assign an owner, add to calendar |
| **Decision / approval card** (T3) | Inline | Approve or decline with rationale; writes the Decision Record |

**Design rule** `[Judgment]`: every kit skill must work without interactive UI (markdown tables, files in OneDrive), because Word can't render it, T0/T1 have no connector, and mobile is undocumented. UI is an upgrade, not a dependency.

### 6.4 Runtime view (T2, in the customer's tenant)

```
 Customer M365 tenant                                          Spaarke
 ──────────────────────────────────────────────────           ───────────────────────────
 Attorney ── Word (Copilot for Legal) ◄── .docx playbooks (SharePoint practice library)
        ├─── Cowork
        │      ├── Legal for Cowork (Microsoft)
        │      ├── Spaarke practice plugin (skills) ──MCP──► Spaarke Kit Tools
        │      │        └── MCP Apps UI (issues table, grids)   (stateless calculations
        │      ├── partner plugins (iManage, …)                + UI; no customer data
        │      └── Work IQ (mail/files/meetings)                stored)
        └─── Copilot Chat / Teams
               └── "Spaarke Legal" agent (+ optional SPFx UX components) ──► Kit Tools
 Customer Azure: Foundry agents + Routines (batch, schedules, evals) ──► Kit Tools
 Governance: Agent 365 registry · Copilot Credits policy · Purview audit · per-tool toggles
```

At T3 the plugin's connector points at the **Spaarke MCP server** in the customer's Spaarke stamp. Kit Tools is a subset of it, so the plugin doesn't change, only its connector binding.

### 6.5 What ships: the files in a kit release

A kit release is **a set of files, one per target, plus guides.** There is no single installer, because each tier is installed by a different person through a different Microsoft screen. Nothing in it is a "Claude instruction" in the sense of something only Claude runs: a `SKILL.md` is plain Markdown instructions that any agent supporting the Agent Skills format can run, and Cowork is one of them.

```
spaarke-kit-cre-v1.2.0/
├── README.pdf                         what's inside, which tier needs what, how to remove it
├── t0-attorney/
│   ├── spaarke-cre-skills.zip         Cowork plugin: skills/<name>/SKILL.md + companions
│   │                                  (≤ 20 skills; ≤ 10 MB per skill); attorney uploads it
│   │                                  in Cowork › Customize and shares it with colleagues
│   ├── playbooks/*.docx               Word playbooks (tenant- and landlord-side lease review);
│   │                                  saved to OneDrive, attached via "Attach from Cloud"
│   └── prompt-pack.docx               starter prompts per task
├── t1-team/
│   ├── practice-library.zip           folder tree for a SharePoint library: playbooks,
│   │                                  precedents, clause library, checklists, overlay
│   ├── site-skills/*/SKILL.md         copied into the library's /Agent Assets/Skills/
│   └── agent-builder-setup.md         instructions and knowledge list for the practice agent
├── t2-it/
│   ├── spaarke-cre-tenant.zip         M365 app package: manifest.json (v1.28/1.29),
│   │                                  agentSkills[], agentConnectors[] (Kit Tools MCP), icons;
│   │                                  built with atk; uploaded in Agents › Tools › Registry
│   ├── spaarke-legal-ux.sppkg         OPTIONAL SPFx package for Chat/Teams-standardized
│   │                                  customers; MCP Apps UI needs no file (served by Kit Tools)
│   ├── foundry/                       Bicep/azd template: Foundry project, agents, Routines
│   ├── Install-SpaarkeKit.ps1         stamps tenant values (app IDs, Kit Tools URL), checks
│   │                                  prerequisites, runs the uploads an admin approves
│   └── it-one-pager.pdf               data flows, permissions, audit, removal
└── evals/                             question and document set for the evaluation run
```

`[Verify]` The plugin `.zip` format: a Claude-format plugin (`.claude-plugin/plugin.json`, `skills/`, `.mcp.json`) imports into Cowork and converts automatically; commands, sub-agents and hooks are dropped. Whether T0 ships the Claude format (also runs in Claude) or the M365 format (built with `atk`) is decision D3.

### 6.6 Engineering components to build

| Component | What it is | Reuse first (CLAUDE.md §11) |
|---|---|---|
| **Kit source format + schema** | Folder convention, metadata YAML, overlay schema, versioning | Agent Skills spec (what Cowork imports); `mike-workflows` layout (Appendix A) |
| **Kit build pipeline** | Validate, render the targets in §6.5, run evals, produce a signed release | `atk` for M365 packages; the repo's existing docx rendering (Compose / OpenXML) for playbooks; `.github/workflows/publish-copilot-agent-template.yml` pattern |
| **Spaarke Kit Tools (MCP)** | Deterministic calculation tools (dates, rent, patent term, LEDES, rate caps) | **Subset of the planned Spaarke MCP server**; existing deterministic calculation services. Not a second server. |
| **Interactive UI** | MCP Apps served by Kit Tools (D9); optional SPFx 1.24 Copilot components | Fluent UI v9 and Spaarke shared UI patterns; the existing Spaarke Copilot agent (`src/solutions/CopilotAgent`) |
| **Tenant installer** | Prerequisite checker; tenant-value stamping; guided admin uploads | T257 per-customer Copilot agent template and `scripts/Deploy-CopilotAgent.ps1`; `/provision-environment` patterns |
| **Foundry templates** | Agents, Routines, evaluation runs for the customer's Azure | `infrastructure/ai-foundry/` (agent YAML, evaluation config, legal-eval metrics); `foundry-agent` skill |
| **Content library** | Core skills plus four practice packs, with evals | `mike-workflows` (Appendix A); existing JPS Actions and playbooks |
| **Platform bridge (T3)** | Policy version → `.docx` playbook; `get_matter_context` / `get_policy_version` / `record_decision` tools; Cowork outputs mapped to Obligations and Invoices | Policy object and Decision Record (ontology r1/r2); Finance Intelligence |

`[Open]` **Hosting Kit Tools when the customer has no Spaarke stamp:** (a) a shared, stateless, multitenant Spaarke endpoint — fastest, matches "days"; or (b) deployed into the customer's Azure — sovereignty-pure, slower. Either is a new hosting unit and needs an ADR-052 placement decision plus the §11 justification. Recommendation: (a) by default, (b) on request, and (b) automatically when the customer already runs Foundry for the kit.

### 6.7 Data and trust boundaries

- At T0–T2, **Spaarke stores no customer content.** Skills run in Microsoft's runtime, knowledge stays in the customer's SharePoint, Kit Tools are stateless, Foundry runs in the customer's Azure. Microsoft contractually does not persist ISV content returned via MCP `[Cited]`.
- Connector auth is `OAuthPluginVault` with a multitenant app registration ("Any Microsoft 365 Organization"); each user signs in to the connector once `[Cited]`. T257's Copilot-agent design already covers this shape.
- UX components get API access through a tenant-wide SharePoint grant; scope it to Kit Tools only.
- **ADR-002 watch item (T3):** Cowork's "Business apps" plugin can write custom Dataverse tables under the user's own roles `[Cited]`. Before a T3 customer turns it on, Spaarke's role design must ensure no invariant can be bypassed that way.

---

## 7. How it fits the Spaarke platform

### 7.1 Against the business plan's five components

| Business plan component | Role of the program |
|---|---|
| 1. The platform | T3. Kits are built to bind to it without rework. |
| 2. Forward-deployed legal engineers | They deliver the enablement sprint; each engagement adds to the kit library (business plan §8). |
| **3. The customer's own AI** | **This is component 3, productized**, delivered first on Copilot and Cowork, then on Foundry. |
| 4. Managed services | The kit subscription is the first managed-service line, before any platform. |
| 5. Connections and ecosystem | Partner plugins, Work IQ and the plugin registry are the ecosystem in practice. |

### 7.2 The offer ladder, extended

```
 NEW ► Enablement kit (T0–T2) ─► Quick start ─► Ontology build ─► First use cases ─► Scale & manage
       days; Copilot only        (existing ladder, business plan §9)
```

For firms, the ladder has a second branch: a firm's kit work on a client's matters becomes the **engagement edge** when that client runs Spaarke (business plan §6). Firms join through shared engagements, and a firm already running our kits joins faster.

### 7.3 The upgrade path: same skill, different binding

| Kit artifact (T0–T2) | Becomes on the platform (T3) |
|---|---|
| `.docx` playbook in SharePoint | A **Policy version**: evaluated and cited on a decision, with the `.docx` rendered from it for Word |
| Obligations/critical-dates spreadsheet from Cowork | **Obligation** objects with owner, authority and lifecycle |
| Issues list / reduction recommendation | **Decision Record** entries (`record_decision`), with the approver |
| Invoice reviewed in Cowork | Invoice in **Finance Intelligence**, with spend signals |
| "Which matter is this?" answered by the attorney | `get_matter_context` from the ontology |
| UX component accept/reject | Writes a governed action through a confirmation gate |
| Usage baseline | The decision baseline the Spaarke Method's manage phase measures against |

### 7.4 Positioning

- **With Microsoft, not against it.** We make Copilot for Legal work for a practice and for this customer, which also positions Spaarke for Microsoft partner co-sell.
- **Neutral by format.** The canonical kit is Agent Skills format, so the same pack runs in Claude. Lead with Microsoft; keep Claude as a fallback.
- **The moat is not the prompts.** Skills are copyable, and Microsoft already ships 20 legal skills. What lasts is the customer overlay produced by legal engineers, the deterministic tools, the evaluations on the customer's own work, the UX components, maintenance against a monthly-changing Microsoft surface, and the T3 bridge.

---

## 8. Risks

| Risk | Mitigation |
|---|---|
| **Commoditization:** Microsoft or partners ship practice skills | Differentiate on the overlay, tools, evals, UX and the T3 bridge; skills are the delivery vehicle, not the product |
| **IT pushback on self-serve uploads**, especially in firms | IT one-pager; Purview-auditable install; T2 as the "governed" path |
| **Information Barriers** in firms block T0/T1 | Walled firms enter at T2 |
| **Interactive UI coverage is partial**: MCP Apps don't render in Word; SPFx components only in Chat and Teams | Every skill works without UI (§6.3 design rule) |
| **Cost surprises:** Cowork credits are the customer's (minimum 2,000 per user per month on the policy) | Pre-flight sets expectations; the usage baseline shows cost per task |
| **Platform churn:** Cowork, registry, Word and UX components all change monthly; `.agent` files retiring; much is preview | Subscription maintenance; a pinned compatibility matrix per kit release; re-verify after Ignite (17–20 Nov 2026) |
| **EU Data Boundary**: GPT models only | Skills are model-agnostic; evaluated on GPT |
| **Content quality and liability** | Evaluations per pack; human-review language in every skill; overlay holds the customer's own positions; terms of use are not legal advice |
| **Scope drift into a second platform** | Kit Tools is a subset of the Spaarke MCP server; no kit-only data store; anything stateful is T3 |
| **Name.** "Microsoft for Legal" uses Microsoft's mark | A Spaarke-owned name, e.g. "Spaarke Enablement Kits for Microsoft Copilot"; check Microsoft partner branding rules `[Open]` |

---

## 9. Roadmap (proposed)

| Phase | Window | Scope | Exit evidence |
|---|---|---|---|
| **A. Prove the mechanics** | Nov – mid-Dec 2026 | Kit format and build pipeline (plugin `.zip`, Word `.docx`, library tree targets); Core; **CRE pack at T0/T1**; live-tenant verification of the `[Verify]` items; re-check after Ignite | An attorney in a test tenant installs and uses the CRE pack in under 2 hours; evaluation run |
| **B. The paid tier** | Q1 2027 | **Invoice Review pack**; Kit Tools MCP; tenant package; first UX component (invoice exception grid); first design-partner sprint — one firm, one department | A sprint finished in 5 days with a timed before/after on the partner's own work |
| **C. Breadth** | Q1–Q2 2027 | M&A and Patents packs; Foundry templates (Routines, batch extraction, evals); remaining UX components | Four packs released; second and third customers |
| **D. Platform bridge** | With ontology r2 deliveries | Policy → `.docx`; `record_decision`; Obligation and Invoice mapping; store listings (4–6 weeks) | A T2 customer converts to a Quick Start with kit artifacts carried over |

---

## 10. Relationship to existing projects

- **spaarke-ontology-platform-r1/r2:** owns Policy, Obligation and the Decision Record. The kit bridge consumes them and does not redefine them. Terminology comes from `ontology-component-model.md` §3.
- **Spaarke MCP server** (planned): Kit Tools is its first tool subset. Sequencing the MCP server earlier helps both.
- **customer-provisioning-orchestration-r1 (T257):** the per-customer Copilot agent template and tenant-value stamping are the base for the T2 tenant package and installer.
- **Agreement Analysis repositioning** (business plan §5): kits are where Spaarke's contract-review content goes, inside Copilot for Legal instead of competing with it.

---

## 11. Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| D1 | Program name | A Spaarke-owned name, not "Microsoft for Legal" |
| D2 | Pilot packs | CRE (proves T0/T1) and Invoice Review (proves T2/T3) |
| D3 | Canonical kit format; T0 plugin format | Agent Skills format as source; T0 ships the Claude-format plugin (runs in Cowork and Claude), T2 the `atk`-built M365 package |
| D4 | Kit Tools hosting when there is no stamp | Shared stateless multitenant endpoint by default (needs an ADR-052 placement decision) |
| D5 | Where the kit source lives | Its own repo (`spaarke-enablement-kits`), on a content cadence separate from the platform |
| D6 | Commercial model | Option B: sprint plus subscription, credited toward Quick Start; free T0 sample per pack; per practice group for firms |
| D7 | Claude as a target | Supported by format; not marketed initially |
| D8 | Project setup | Register as a project (`/devops-project-start`), then `/design-to-spec` on Phase A |
| D9 | Interactive UI technology | MCP Apps served by our MCP connector (render in Cowork and Chat); SPFx UX components only for Chat/Teams-standardized customers (§6.3) |
| D10 | First design partners | One firm and one department, so both entry paths are proven in Phase B |

## 12. To verify before external claims

1. A user-uploaded Cowork plugin's MCP connector works without admin consent under default Entra settings.
2. Whether "Allow agents and plugins built by your organization = off" also blocks user-uploaded Cowork plugins.
3. Whether registry or partner plugins appear in Word's "Manage plugins & skills"; whether parsed playbook rules persist; Word on Mac (check after Ignite).
4. MCP Apps rendering in Cowork with our connector in practice (documented GA, 2026-09-08); SPFx UX components in Cowork (planned, no date).
5. Which Claude-format plugin features survive Cowork conversion in practice.
6. The `mike-workflows` licence and attribution terms for redistribution inside a commercial kit; the licence of any other third-party legal plugin content before reuse.

---

## Appendix A. What `mike-workflows` is

**Mike OSS** (`github.com/Open-Legal-Products/mike`, mikeoss.com) is an open-source legal AI application. We assessed it on 2026-07-21. The application itself is **not reusable**: it is AGPL-3.0 and built on a different stack (TypeScript, Express, Supabase).

Its **workflow content is published separately** in the `mike-workflows` repository under the **MIT licence**, which allows commercial reuse with attribution. It contains:

- **About 14 assistant workflows**: NDA review, commercial lease review, employment, credit, shareholder and guarantee agreement review, draft-from-template, key-terms extraction, document comparison, proofreading.
- **About 12 tabular-review column sets**: extraction schemas for change of control, e-discovery, NDA, lease, SPA and others.

The workflows are written as **`SKILL.md` files in the Agent Skills format** — the same format Cowork imports — with column sets in `table-columns.yaml`. So they are a legally reusable starting point for the CRE pack (commercial lease review, lease columns) and the M&A pack (SPA and change-of-control columns), and benchmark content for our evaluations. We would adapt, extend and attribute them, not ship them as-is. Licence and attribution terms are re-checked before the first commercial release (§12 item 6).
