# Microsoft for Legal — Platform Enablement Program: Solution Strategy

> **Status:** Draft v0.1 — strategy, for owner review. Not a design spec; feeds `/design-to-spec` once the decisions in §9 are made.
> **Date:** 2026-10-29
> **Author:** Ralph Schroeder + Claude Code
> **Inputs:** `projects/spaarke-ontology-platform-r1/notes/spaarke-ontology-strategy-synopsis-v2.md` §2.5b (v2.3); `spaarke-research/2026-10-spaarke-overview/spaarke-business-plan-v4.md` §3, §8, §9; `spaarke-research/2026-10-spaarke-method/the-spaarke-method-published.md`; researcher notes dated 2026-10-29 in `.claude/agent-memory/researcher/` (`ms-copilot-for-legal-cowork-registry`, `copilot-legal-enablement-kit-deployment-paths`, `foundry-work-iq-delta`, `dataverse-m365-copilot-platform-delta`).
> **Tags:** `[Cited]` verified against Microsoft primary sources 2026-10-29 · `[Judgment]` · `[Open]` owner decision · `[Verify]` must be tested in a live tenant before we promise it.

---

## 1. Summary

Most legal departments and firms we sell to already pay for Microsoft Copilot. Since 2026-10-15 those seats include **Copilot for Legal**: a Word legal plugin and a Cowork legal plugin with 20+ legal skills `[Cited]`. What Microsoft ships is horizontal. It has no practice content, no knowledge of the customer's own positions, playbooks only as `.docx` uploads, and no notion of a matter. The gap between "we have Copilot" and "Copilot does our M&A diligence the way we do it" is exactly the work Spaarke's forward-deployed legal engineers do.

**The program packages that work as enablement kits:** skills, playbooks, knowledge, tools and connectors, deployed into the customer's own tenant on the Microsoft AI they already license. A kit works with no Spaarke platform at all, and it is built so that every artifact has a defined upgrade into the Spaarke platform.

The design rests on five decisions:

1. **Author once, deploy to many surfaces.** The canonical kit format is the Agent Skills / plugin format (`SKILL.md` + companion files + MCP connectors). Cowork imports it natively, Claude runs it, `atk` packages it for M365 admin deployment, and the same skills compile to Spaarke JPS Actions on the platform. Word gets `.docx` playbooks rendered from the same source.
2. **Value lands in Cowork first, Word second.** Cowork lets an attorney upload and share a plugin with no admin `[Cited]`. The Word legal plugin accepts only an attached `.docx` playbook, with no skills or partner hook documented `[Cited]`.
3. **Four complexity tiers, from "an attorney in an afternoon" to "the Spaarke platform".** Each tier adds one dependency: a Copilot seat, then a SharePoint library, then IT, then the platform.
4. **Calculations are tools, not prompts.** Deadlines, rent schedules, patent terms and invoice arithmetic run as deterministic MCP tools. This applies the Spaarke Method's rule that work which must return the same answer every time uses a calculation. It is also what a prompt pack cannot copy.
5. **Kits are the new first rung of the offer ladder**, below Quick Start. They are component 3 of the business plan ("the customer's own AI") delivered early, standalone, on Microsoft's runtime instead of a Foundry project.

---

## 2. Why now

| Signal | What it means for the program |
|---|---|
| **Copilot for Legal GA 2026-10-15**, included in the M365 Copilot licence; Cowork use metered in Copilot Credits `[Cited]` | Customers have the runtime and have paid for it. Our kit makes it useful on day one. |
| **ILTA 2026: Copilot at 76% of firms, but only 52% in full deployment**; wide piloting, narrow deployment `[Cited, synopsis §2.4]` | The bottleneck is realizing value, not access. That is a services problem with a product shape. |
| **Cowork accepts user-uploaded plugins and converts Claude-format plugins automatically** `[Cited]` | An attorney who would otherwise reach for Claude can get the same skill-driven experience inside Microsoft, with the tenant's identity and data protections. |
| **Plugin registry (2026-09-30)**: one package format across Chat, Cowork and Office; tenant admin upload in minutes, without store validation `[Cited]` | The "days, not weeks" promise is technically achievable. The store listing (4–6 weeks) stays off the critical path. |
| **Microsoft's own legal skills are generic**: playbooks as `.docx` only, no matter object, Information Barriers unsupported for plugin management `[Cited]` | The customer-specific, practice-specific and governance layer is open, and it is where legal engineering adds value. |
| **"Internal build" is the alternative we meet most often** (business plan §7) | Kits are how an internal team builds faster, which turns the alternative into the entry point. |

---

## 3. The offering

### 3.1 What a kit is

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

### 3.2 Complexity tiers

| Tier | Who installs | Time | Depends on | What the customer gets |
|---|---|---|---|---|
| **T0: Attorney self-serve** | The attorney | Hours | M365 Copilot seat; Cowork enabled (a Copilot Credits spending policy exists); no Information Barriers on the user | Practice-pack **skills plugin** uploaded in Cowork › Customize and shared with named colleagues; `.docx` **playbook templates** for Word's "Attach from Cloud"; a **prompt-link pack**; checklists as skill companion files (≤ 10 MB per skill) |
| **T1: Practice team** | Practice lead or knowledge lawyer, no IT | 1–2 days | T0 + a SharePoint site they own | T0, plus a provisioned **knowledge library** (precedents, clause library, checklists, the customer overlay) and an **Agent Builder agent** per practice grounded on it (≤ 100 SharePoint files), shared with the team |
| **T2: Department, IT-deployed** | Customer IT + Spaarke legal engineer | 3–5 days | Copilot admin + AI Admin/Global Admin for MCP consent | T1, plus a **tenant-specific package** uploaded by admin (Agents › Tools › Registry), assigned to a legal security group; **Spaarke Kit Tools** MCP connector (deterministic calculations); partner connectors the customer already licenses (iManage, NetDocuments, etc. via their own plugins); optional **Foundry add-on** for batch work (data-room or invoice-batch review) in the customer's Azure |
| **T3: Spaarke platform** | Spaarke engagement (Quick Start onward) | Per business plan | Spaarke platform in the customer's tenant | The same skills, now bound to the ontology through the **Spaarke MCP server**: matter context, Policy versions, Obligations and Invoices as governed objects, and the Decision Record |

`[Judgment]` T0 and T1 are the "use Microsoft instead of Claude" tiers and the marketing engine. T2 is the paid enablement product. T3 is the platform.

**Caveats that shape what we promise** `[Cited]` `[Verify]`:
- Admins **cannot turn off** user plugin sharing in Cowork, but conservative firms may block or audit uploads once they notice. T0 must come with an "IT one-pager" so it reads as governed, not as shadow AI.
- **Information Barriers block plugin and skill upload entirely.** Many law firms run IB, so for them T0/T1 is not available and T2 is the entry point.
- Cowork **Projects are private to their creator**. Team context therefore lives in shared skills and the SharePoint library, never in Projects.
- Whether a user-uploaded plugin's MCP connector works without admin consent is **untested**. Until verified, tools are a T2 feature.
- Whether registry plugins load **inside Word's legal experience** is undocumented. Re-check after Ignite (17–20 Nov 2026).

### 3.3 Practice packs (initial four)

| Pack | Skills (illustrative) | Knowledge / templates | Deterministic tools (T2+) | Lands best in | Spaarke upgrade (T3) |
|---|---|---|---|---|---|
| **M&A** | Diligence review with a tabular extraction set (change of control, assignment, exclusivity, MFN, termination, IP ownership); SPA/APA review against playbook; disclosure-schedule drafting; issues list; closing checklist and signing/closing tracker | Buyer-side and seller-side playbooks; diligence request list; closing checklist; precedent library index | Materiality and threshold calculators; closing-date and long-stop calculators | Cowork (batch file review), Word (SPA redline) | Deal as a Matter; post-closing obligations as Obligation objects; decisions on issues recorded |
| **Commercial real estate** | Lease review (tenant- or landlord-side); lease abstraction (dates, rent, options, CAM, assignment); estoppel/SNDA review; amendment comparison | Lease playbooks; abstract template; estoppel checklist | **Rent-schedule and escalation calculator**; **critical-date calculator** (notice windows, options, expirations) | Cowork (abstraction), Word (lease redline) | Critical dates and options as Obligations with owners; portfolio view |
| **Patents** | Invention-disclosure triage; claim-chart drafting; office-action analysis and response outline; FTO checklist; portfolio summary | Disclosure form; response templates; jurisdiction notes | **Patent term and annuity-deadline calculators** (jurisdiction rules) | Cowork | Deadlines and annuity decisions as Obligations plus Decision Record; docket bridge |
| **Invoice review** | Outside-counsel-guideline compliance review (block billing, vague entries, staffing, rate and expense rules); budget-vs-actual narrative; appeal/reduction letter drafting | Billing-guideline playbook built from the customer's OCGs; reduction-letter templates | **LEDES parser and line-item arithmetic**; rate-card and cap checks; budget variance | Cowork (T0 uses uploaded LEDES/PDF; arithmetic is reliable only from T2) | Spaarke Finance Intelligence (built): invoices and spend signals as objects; approve/reduce decisions recorded |

`[Judgment]` Two packs prove the two ends of the model. **CRE lease review proves T0.** It is strong on skills alone, and a seed exists (MIT-licensed `mike-workflows` commercial-lease review and lease extraction columns). **Invoice review proves T2 and T3.** It needs deterministic tools, matches the business plan's spend lead offer, and has a built platform module behind it. M&A and Patents follow.

### 3.4 Core components (shared by every pack)

- **Legal foundation skills:** review-against-playbook, build-a-playbook (positions → `.docx`), issues list, key-terms extraction to a table, document comparison, chronology, matter brief, and a citation and verification discipline that any pack skill can reference.
- **Setup interview skill.** It walks the practice lead through positions, fallbacks, escalation and house style, and writes the **customer overlay**. Claude for Legal opens the same way; ours writes the overlay to files the customer owns.
- **Governance set:** human-review and confidentiality guidance embedded in every skill; an IT one-pager (what's installed, data flows, how to remove it); Purview-auditable install steps.
- **Evaluation harness.** Each pack ships a question and document set. The sprint adds the customer's own questions, following the Spaarke Method's build-phase test. It reuses `infrastructure/ai-foundry/evaluation/` (legal-eval config, citation-accuracy metric).
- **Admin and adoption kit:** install guides per tier, pre-flight checklist, training prompts, and a usage baseline so we can show value.

### 3.5 The service wrapper: the enablement sprint

Forward-deployed delivery, compressed to one week. It is the Spaarke Method run once, for one practice.

| Day | Method phase | Output |
|---|---|---|
| **0 (remote)** | Pre-flight | Licence and policy check: Copilot seats, Cowork spending policy, Word legal plugin on, IB status, MCP consent owner. Tier confirmed. |
| **1** | Envision | Install Core and the practice pack; choose the first two or three use cases; baseline (time and quality today) |
| **2–3** | Design + build | Setup interview → customer overlay; playbooks rendered; knowledge library curated; tools connected (T2) |
| **4** | Build test | Evaluation on the customer's own documents and questions, timed against the baseline; fixes |
| **5** | Deploy | Team rollout, training, handover; usage measurement starts |

After the sprint comes the **kit subscription**: practice-pack updates, Microsoft change tracking (the surface changes monthly), new skills, and quarterly re-evaluation. It can be part of Spaarke managed services or sold alone.

### 3.6 Commercial shape `[Open]`

This follows the business plan's principles: AI runs at cost on the customer's own agreement (Copilot seats and Cowork credits are theirs), and business users are never charged per seat. Options to test with design partners:

- **A.** T0 Core free (lead generation); practice packs and T1/T2 paid.
- **B.** Fixed-fee enablement sprint per practice, plus an annual kit subscription per practice pack, with the sprint fee credited toward a platform Quick Start.
- **C.** Kits only inside managed services.

Recommendation: **B**, with a free T0 sample skill per pack as marketing.

---

## 4. Component architecture

### 4.1 Single source, many targets

```
                      ┌─────────────────────────────────────┐
                      │   KIT SOURCE (Spaarke-owned repo)    │
                      │   skills/*/SKILL.md + companions     │
                      │   playbooks/*.yaml (positions)       │
                      │   knowledge/ manifest                │
                      │   tools/ (MCP tool contracts)        │
                      │   evals/ · overlay schema · metadata │
                      └──────────────┬──────────────────────┘
                                     │  kit build pipeline
                                     │  validate → render → evaluate → sign
     ┌──────────────┬────────────────┼────────────────┬───────────────────┐
     ▼              ▼                ▼                ▼                   ▼
 Plugin .zip   M365 app package   Word playbooks   SharePoint site     Spaarke JPS
 (Claude/      (atk; agentSkills  (.docx rendered  template +          Actions + Policy
 Agent Skills) + agentConnectors) from positions)  Agent Builder spec  seeds (T3)
 T0: Cowork    T2: admin upload   T0+: Word legal  T1+: knowledge      platform
 upload/share; (tenant-specific)  plugin "Attach   library             binding
 also Claude                      from Cloud"
```

`[Judgment]` One source for content stops the skills, the Word playbooks and the platform Actions from drifting into three copies. It is the kit-level version of the existing rule "compiled once for everyone, never per customer" (synopsis §9.4).

### 4.2 Runtime view (T2, in the customer's tenant)

```
 Customer M365 tenant                                         Spaarke
 ─────────────────────────────────────────────────           ─────────────────────────
 Attorney ── Word (Copilot for Legal) ◄── .docx playbooks
        └─── Cowork / Copilot Chat
               ├── Copilot for Legal plugin (Microsoft)
               ├── Spaarke practice plugin (skills)  ─────► Spaarke Kit Tools MCP
               │        └── reads SharePoint knowledge library  (stateless calculations;
               ├── partner plugins (iManage, etc.)              no customer data stored)
               └── Work IQ (mail/files/meetings, delegated)
 Optional: customer Azure ── Foundry agent (batch review) ──► same Kit Tools
 Governance: Agent 365 registry · Purview audit · admin per-tool toggles
```

At T3 the plugin's connector points at the **Spaarke MCP server** in the customer's Spaarke stamp. The Kit Tools catalog is a subset of it, so the plugin itself does not change, only its connector binding.

### 4.3 Engineering components to build

| Component | What it is | Reuse first (CLAUDE.md §11) |
|---|---|---|
| **Kit source format + schema** | Folder convention, metadata YAML, overlay schema, versioning | Agent Skills spec (what Cowork imports); `mike-workflows` layout (SKILL.md + `table-columns.yaml`) |
| **Kit build pipeline** | Validate, render the five targets, run evals, produce a signed release | `atk` for M365 packages; the docx rendering already in the repo (Compose / OpenXML) for playbooks; `.github/workflows/publish-copilot-agent-template.yml` pattern |
| **Spaarke Kit Tools (MCP)** | Deterministic calculation tools (dates, rent, patent term, LEDES, rate caps) | **Subset of the planned Spaarke MCP server** (business plan §5, "in development"); existing deterministic calculation services. Not a second server. |
| **Tenant installer** | Per-tier scripts: SharePoint site template, admin package with tenant values, pre-flight checker | T257 per-customer Copilot agent template and `scripts/Deploy-CopilotAgent.ps1`; `/provision-environment` patterns |
| **Content library** | Core skills plus four practice packs, with evals | `mike-workflows` (MIT, with attribution); existing JPS Actions/playbooks; `infrastructure/ai-foundry/evaluation/` |
| **Platform bridge (T3)** | Policy version → `.docx` playbook; `get_matter_context` / `get_policy_version` / `record_decision` tools; Cowork outputs mapped to Obligations and Invoices | Policy object and Decision Record (ontology r1/r2); Finance Intelligence |

`[Open]` **Hosting for Kit Tools when the customer has no Spaarke stamp.** Two candidates:
- **(a)** A shared, stateless, multitenant Spaarke endpoint. Fastest, and matches "days".
- **(b)** Deployed into the customer's Azure. Sovereignty-pure, slower.

Either is a new hosting unit and needs an ADR-052 placement decision, plus the §11 justification at design time. Recommendation: (a) by default, (b) on request.

### 4.4 Data and trust boundaries

- At T0–T2, **Spaarke stores no customer content.** Skills run in Microsoft's runtime, knowledge stays in the customer's SharePoint, and Kit Tools are stateless. Microsoft contractually does not persist ISV content returned via MCP `[Cited]`.
- Connector auth is `OAuthPluginVault` with a multitenant app registration ("Any Microsoft 365 Organization"); each user signs in to the connector `[Cited]`. A Copilot-agent design already exists in T257 for this shape.
- **ADR-002 watch item (T3):** Cowork's "Business apps" plugin can write custom Dataverse tables under the user's own roles `[Cited]`. Before a T3 customer turns it on, Spaarke's role design must make sure no invariant can be bypassed that way.

---

## 5. How it fits the Spaarke platform

### 5.1 Against the business plan's five components

| Business plan component | Role of the program |
|---|---|
| 1. The platform | T3. Kits are built to bind to it without rework. |
| 2. Forward-deployed legal engineers | They deliver the enablement sprint; each engagement adds to the kit library (business plan §8: "each engagement adds at least one pack"). |
| **3. The customer's own AI** | **This is component 3, productized**, delivered first on Copilot and Cowork; Foundry delivery becomes the T2 add-on. |
| 4. Managed services | The kit subscription is the first managed-service line, before any platform. |
| 5. Connections and ecosystem | Partner plugins, Work IQ and the plugin registry are the ecosystem in practice. |

### 5.2 The offer ladder, extended

```
 NEW ► Enablement kit (T0–T2) ─► Quick start ─► Ontology build ─► First use cases ─► Scale & manage
       days; Copilot only        (existing ladder, business plan §9)
```

`[Judgment]` The kit is the cheapest credible proof of the legal engineers' value. It ends with evidence the customer can inspect (the timed evaluation), as every rung of the ladder must.

### 5.3 The upgrade path: same skill, different binding

| Kit artifact (T0–T2) | Becomes on the platform (T3) |
|---|---|
| `.docx` playbook in SharePoint | A **Policy version**: evaluated and cited on a decision, with the `.docx` rendered from it for Word |
| Obligations/critical-dates spreadsheet produced in Cowork | **Obligation** objects with owner, authority and lifecycle |
| Issues list / reduction recommendation | **Decision Record** entries (`record_decision`), with the approver |
| Uploaded invoice reviewed in Cowork | Invoice in **Finance Intelligence**, with spend signals |
| "Which matter is this?" answered by the attorney | `get_matter_context` from the ontology |
| Usage baseline | The decision baseline the Spaarke Method's manage phase measures against |

This table is the sales story for T3. Everything the customer learned to use stays; the platform adds the matter, the policy, the authority and the record that Copilot for Legal lacks (synopsis §2.5b).

### 5.4 Positioning

- **With Microsoft, not against it.** We make Copilot for Legal work for a practice and for this customer, which also positions Spaarke for Microsoft partner co-sell.
- **Neutral by format.** The canonical kit is Agent-Skills format, so the same pack runs in Claude. That is the neutrality claim in the business plan §7, at no extra cost. Lead with Microsoft; keep Claude as a fallback.
- **The moat is not the prompts.** Skills are copyable, and Microsoft will add practice skills of its own. What lasts is the customer overlay produced by legal engineers, the deterministic tools, the evaluations on the customer's own work, ongoing maintenance against a monthly-changing Microsoft surface, and the T3 bridge.

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| **Commoditization:** Microsoft or partners ship practice skills | Differentiate on the overlay, the tools, the evals and the T3 bridge; treat skills as the delivery vehicle, not the product |
| **IT pushback on self-serve uploads** | The IT one-pager; Purview-auditable install; offer T2 as the "governed" path |
| **Information Barriers in firms** block T0/T1 | Firms with IB enter at T2 |
| **Cost surprises:** Cowork credits are the customer's | The pre-flight sets expectations; the usage baseline shows cost per task |
| **Platform churn** (Cowork, registry, Word all changing monthly; preview tooling) | Subscription maintenance; a pinned compatibility matrix per kit release; re-verify after Ignite |
| **Content quality and liability** | Evaluations per pack; human-review language in every skill; the customer overlay is the customer's own positions; terms of use are not legal advice |
| **Scope drift into a second platform** | Kit Tools is a subset of the Spaarke MCP server; no kit-only data store; anything stateful is T3 |
| **Name.** "Microsoft for Legal" uses Microsoft's mark | Choose a Spaarke-owned name, e.g. "Spaarke Enablement Kits for Microsoft Copilot"; check Microsoft partner branding rules `[Open]` |

---

## 7. Roadmap (proposed)

| Phase | Window | Scope | Exit evidence |
|---|---|---|---|
| **A. Prove the mechanics** | Nov – mid-Dec 2026 | Kit format and build pipeline (plugin .zip + Word `.docx` targets); Core; **CRE pack at T0/T1**; live-tenant verification of the `[Verify]` items; re-check after Ignite | An attorney in a test tenant installs and uses the CRE pack in under 2 hours; evaluation run |
| **B. The paid tier** | Q1 2027 | **Invoice Review pack**; Kit Tools MCP (calculations); admin package target; first design-partner enablement sprint | A sprint finished in 5 days with a timed before/after on the partner's own matters |
| **C. Breadth** | Q1–Q2 2027 | M&A and Patents packs; Foundry batch add-on; SharePoint/Agent Builder target hardened | Four packs released; second and third customers |
| **D. Platform bridge** | With ontology r2 deliveries | Policy → `.docx`; `record_decision`; Obligation and Invoice mapping; Partner Center listing (4–6 weeks) | A T2 customer converts to a Quick Start with kit artifacts carried over |

---

## 8. Relationship to existing projects

- **spaarke-ontology-platform-r1/r2:** owns Policy, Obligation and the Decision Record. The kit bridge consumes them and does not redefine them. Terminology comes from `ontology-component-model.md` §3.
- **Spaarke MCP server** (planned): Kit Tools is its first tool subset. Sequencing the MCP server earlier helps both efforts.
- **customer-provisioning-orchestration-r1 (T257):** the per-customer Copilot agent template and tenant-value rendering are the base for the T2 admin package.
- **Agreement Analysis repositioning** (business plan §5): kits are where Spaarke's contract-review content goes, inside Copilot for Legal instead of competing with it.

---

## 9. Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| D1 | Program name | A Spaarke-owned name, not "Microsoft for Legal" |
| D2 | Pilot packs | CRE (proves T0) and Invoice Review (proves T2/T3) |
| D3 | Canonical kit format | Agent Skills / plugin format, with M365 and Word as rendered targets |
| D4 | Kit Tools hosting when there is no stamp | Shared stateless multitenant endpoint by default (needs an ADR-052 placement decision) |
| D5 | Where the kit source lives | Its own repo (`spaarke-enablement-kits`), on a content cadence separate from the platform; the build pipeline can live there too |
| D6 | Commercial model | Option B: sprint plus subscription, credited toward Quick Start; free T0 sample per pack |
| D7 | Claude as a target | Supported by format; not marketed initially |
| D8 | Project setup | Register as a project (`/devops-project-start`), then run `/design-to-spec` on Phase A |

## 10. To verify before external claims

1. A user-uploaded Cowork plugin's MCP connector works without admin consent under default Entra settings.
2. Whether the admin setting "Allow agents and plugins built by your organization = off" also blocks user-uploaded Cowork plugins.
3. Whether registry plugins appear inside Word's Copilot for Legal experience (check after Ignite).
4. The EU Data Boundary position for Cowork when Anthropic models are selected.
5. The `mike-workflows` licence and attribution terms for redistribution inside a commercial kit; the licence of any third-party legal plugin content before reuse.
