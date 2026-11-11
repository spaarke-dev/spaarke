# Spaarke Solution Accelerators: Vision and Solution Strategy

> **Status:** Draft v0.6. This is the program's **vision document**, for owner review.
> **Date:** 2026-10-31 (v0.4 2026-10-30; v0.1–v0.3 2026-10-29)
> **Author:** Ralph Schroeder + Claude Code
>
> **Companion documents:**
> - [design.md](design.md): R1 build design (T0/T1 foundation and the first six practice accelerators)
> - [references.md](references.md): curated references
> - [notes/poc-2026-10-30-cowork-nda-review.md](notes/poc-2026-10-30-cowork-nda-review.md): the NDA proof of concept
>
> **Inputs:**
> - `projects/spaarke-ontology-platform-r1/notes/spaarke-ontology-strategy-synopsis-v2.md` §2.5b
> - `spaarke-research/2026-10-spaarke-overview/spaarke-business-plan-v4.md` §3, §8, §9
> - `spaarke-research/2026-10-spaarke-method/the-spaarke-method-published.md`
> - researcher notes from 2026-10-29/30/31 in `.claude/agent-memory/researcher/`
>
> **Tags:**
> - `[Cited]`: Microsoft primary sources, checked 2026-10-29/30
> - `[Observed]`: seen in the 2026-10-30 POC
> - `[Judgment]`: our assessment
> - `[Open]`: owner decision
> - `[Verify]`: test in a live tenant before we promise it

**Change log:**
- **v0.6** (aligned with design v0.4, from owner screenshots):
  - Word's Copilot pane has a plugins menu and a model picker.
  - Playbooks are attached per review in Word and can be selected automatically only in Cowork.
  - Add-ins can't drive Copilot.
  - Practice waves decided, adding Legal Intake (§5.4, D2).
  - Specialist reviewers (D15).
- **v0.5** (aligned with design v0.3):
  - New §4.5: the legal plugin in Word and Legal for Cowork share expertise but no object, and there is a "which flow goes where" rule.
  - New §6.1a: why the engines exist, and the one case (single-document review in Word) where we defer to Microsoft.
  - §8: existing Spaarke content is an **untested candidate**, compared rather than copied, and the findings feed Spaarke's backlog.
  - Onboarding is a product feature (principle 10; §5.2).
  - Appendix A: content sources by tier, with licence traps.
  - Corrections:
    - Cowork memory is conversation-scoped;
    - custom instructions are 20 KB and set in Preferences;
    - the Word plugin runs on Windows desktop only;
    - ContractNLI is CC BY 4.0.
- **v0.4: rewritten as the vision document.**
  - New vision, principles and architecture-at-a-glance (§1–§3).
  - Renamed "enablement kits / practice packs" to **Solution Accelerators / practice accelerators**.
  - Added the POC's evidence: a skill ran our Python engines inside Cowork and produced a real redline.
  - Added one content model across Spaarke, Word and Cowork (§7).
  - Added how Spaarke AI and JPS relate (§8).
  - Added forms and interactive UI options (§9).
  - The Spaarke connection is now a BFF-hosted MCP connector (§10).
  - Practice set is now NDA, Employment, CRE, Invoice Review and U.S. Patents.
  - Corrections:
    - installing by copying a folder into OneDrive works;
    - the skill cap is 50;
    - SharePoint site skills don't load in Cowork;
    - Microsoft ships no default playbooks;
    - SharePoint Embedded is unreachable from Cowork.
- **v0.3:** "Copilot for legal" is defined as the umbrella over two plugins. MCP Apps render in Cowork.
- **v0.2:** law firms are an equal audience; Copilot UX components; how Word and Cowork work; package anatomy; Foundry's role.

---

## 1. Vision

> **Every attorney who has Microsoft Copilot can do practice-grade legal work in it, against their own organization's positions, from day one, and each piece of that work is a step toward the Spaarke platform rather than a detour from it.**

Today an attorney who wants AI that reviews an NDA "the way we do it" installs Claude with legal plugins. The Spaarke Solution Accelerators give that attorney the same experience **inside Microsoft**:
- in Word, where their documents are;
- in Copilot Cowork, where multi-step work happens;
- under their firm's or department's identity, data protections and billing.

A practice accelerator installs in hours. A practice team can adopt it in a day or two without IT, and an IT-supported rollout takes about a week.

Three things about the accelerators matter beyond the first sale:
1. **They are aligned with the Spaarke platform from the first file.** The topics, positions and output formats are the same in Cowork, in Word and in Spaarke's own AI.
2. **The customer's own positions live in files the customer owns.** Spaarke adds content, engines and engineering; the customer keeps its knowledge.
3. **Each tier is a rung.** The attorney's first skill, the practice library, the IT-deployed connector and the full platform all build on the same content.

This is component 3 of the business plan, "the customer's own AI", delivered first on Microsoft's runtime, by the same forward-deployed legal engineers who deliver the platform.

---

## 2. Why now

| Signal | What it means |
|---|---|
| **Copilot for legal GA 2026-10-15**, included in the M365 Copilot licence; Cowork metered in Copilot Credits `[Cited]` | Customers have the runtime and have paid for it. Accelerators make it useful on day one. |
| **Microsoft ships no default legal playbooks and no playbook library.** Word takes a `.docx` per review; Legal for Cowork's skills are generic `[Cited]` | The practice-specific and customer-specific layer is open. It is exactly what legal engineers produce. |
| **Cowork runs open-standard Agent Skills, including scripts** ([agentskills.io](https://agentskills.io/home)). Attorneys install them by copying a folder into OneDrive or uploading a plugin, with no admin `[Cited]` `[Observed]` | Content we author once runs in Cowork and in Claude. An attorney can adopt it alone. |
| **The 2026-10-30 POC:** a Cowork skill reviewed a real NDA and ran our Python against the Word file. Output: surgical tracked changes, margin comments, a linked summary table, a clean version, an email `[Observed]` | "Mechanics in code" works at the attorney tier, not only behind IT. The core idea is proven. |
| **ILTA 2026:** Copilot at 76% of firms, 88% of 700+ lawyer firms; only 52% in full deployment `[Cited, synopsis §2.4]` | The bottleneck is realizing value, not access. |
| **25+ launch partners are largely firm vendors** (iManage, NetDocuments, HighQ, Harvey, Legora…) `[Cited]` | The firm ecosystem is converging on Cowork as its agent surface. |
| **MCP Apps and elicitation forms render in Cowork; Copilot UX components GA 2026-10-28** `[Cited]` | A connector can bring real Spaarke UI, such as intake forms, matter cards and review grids, into Copilot. |

---

## 3. The solution at a glance

```
                           Customer's Microsoft 365 tenant
 ┌─────────────────────────────────────────────────────────────────────────────────────────┐
 │  Word desktop ── legal plugin in Word ◄── Word playbooks (.docx, rendered by us)         │
 │                                                                                          │
 │  Copilot Cowork ── Legal for Cowork (Microsoft, 20 skills)                               │
 │        ├── Practice accelerators (our Agent Skills: SKILL.md + references + engines)     │
 │        │       ├─ read the customer's legal playbooks and attorney profile (OneDrive/SP)  │
 │        │       ├─ run deterministic engines (redline, summary, dates, amounts) in sandbox │
 │        │       └─ use built-ins: Word · Excel · mail · Adaptive Cards · automations      │
 │        └── Spaarke connector (T2+) ──MCP──► Spaarke BFF ──► SPE documents, matters,       │
 │                 elicitation forms, MCP Apps UI                 write path, AI tools      │
 │                                                                                          │
 │  SharePoint "Legal Playbooks" library: legal playbooks (master) · precedents ·           │
 │  clause library · accelerator packages                                                   │
 │                                                                                          │
 │  Customer Azure (T2+): Foundry agents and Routines for unattended and batch work         │
 └─────────────────────────────────────────────────────────────────────────────────────────┘
            ▲                                                         ▲
            │  one content model: topic IDs · review-result schema · version stamps
            │                                                         │
   Spaarke accelerator source (practices/<p>/) ── build ──► Cowork skills · Word playbooks ·
                                                            Spaarke JPS Actions + Knowledge
```

**Ten principles:**
1. **Attorneys in firms and departments alike.** Review, diligence, abstraction and drafting are the same work in both. Only the buyer and the entry path differ (§5.1).
2. **Microsoft-first, standard format.** Accelerators are Agent Skills, so Cowork runs them natively and Claude runs them too. We lead with Microsoft.
3. **Complement Microsoft, never duplicate it.** We add positions, practice depth, engines and outputs on top of Legal for Cowork and the legal plugin in Word, and hand off to their skills where they help. A single-document review in Word belongs to Microsoft's plugin, fed by our Word playbook. Our Cowork skills handle packaged deliverables, batches, spreadsheets and automations (§4.5).
4. **The customer's positions are the customer's files.** Legal playbooks and attorney profiles live in their OneDrive or SharePoint, read at run time. Our updates never overwrite them.
5. **Mechanics in code, judgment in the model.** Redlines, tables, dates and arithmetic come from deterministic engines:
   - in the Cowork sandbox at T0/T1;
   - through the Spaarke connector at T2+.

   This is the Spaarke Method's rule that work which must give the same answer every time uses a calculation.
6. **One content model across every surface.** Spaarke, Word and Cowork share topic IDs, a review-result schema and version stamps. Formats differ, but meaning does not drift (§7).
7. **Every output names its standards.** Each output says which legal playbook and version it used, or says that no tailored playbook was found.
8. **Human approval at every boundary.** Outputs are drafts for attorney review. Sending and writing need approval, and writes to Spaarke go only through the BFF write path.
9. **Each tier is a rung.** Each tier adds one dependency (§5.3) and carries the previous tier's content forward unchanged.
10. **The first run is guided.** Microsoft ships no onboarding for either legal plugin. Every accelerator starts with a re-runnable setup skill that checks the environment, installs the right skills, builds or imports the legal playbook, personalizes defaults, and runs a first review with the attorney (§5.2).

---

## 4. How Copilot for legal works

Sources: the Microsoft admin page "Manage Copilot for legal" (2026-10-13); the support pages and transparency note; the Learn Cowork pages (updated 2026-10-21 to 10-30); MC1509799. Detail is in the researcher notes and [references.md](references.md). GA rolls out until mid-December 2026, so tenants differ.

### 4.1 What is what

**"Copilot for legal" is not Word.** It is Microsoft's umbrella name for **two legal plugins**:
- *"Two legal plugins are available: one for Copilot in Word and one for Copilot Cowork"* (admin page).
- Cowork itself is a separate, general-purpose Copilot product that the legal plugin runs inside.

| Term (Microsoft's) | What it is | Runs in | Paid by | Can we extend it? |
|---|---|---|---|---|
| **Microsoft Copilot for legal** | Umbrella over the two plugins below | Word desktop + Cowork | M365 Copilot licence | n/a |
| **Legal plugin in Word** (policy "Legal Skills in M365 Copilot in Word") | Legal skills in the Copilot pane in Word, turned on under `…` › Manage plugins & skills: playbook review, redlines, drafting from precedent. Its three skills share names with Legal for Cowork's. | **Word for Windows desktop** | M365 Copilot licence; on by default; no credits | **Only with a `.docx` playbook**, attached per review. Our own plugin in Word's plugin menu is undocumented (to test). A Spaarke agent can sit in Word's "All agents", chat only |
| **Legal for Cowork** (MC1509799 says "Cowork for Legal") | A built-in plugin of 20 legal skills a user adds in Cowork | Wherever Cowork runs | Copilot Credits | **Alongside it.** Our skills sit next to it and hand off to it |
| **Copilot Cowork** | *"Agentic AI that completes multistep work across Microsoft 365"* | Web, Windows/macOS app, mobile | Copilot Credits **and** an M365 Copilot licence; access via a spending policy | **Yes.** Agent Skills (with scripts), plugins, MCP connectors, elicitation forms, MCP Apps |
| **Copilot Chat** | Copilot's conversational experience | Web/app, Teams, Outlook | Included (basic) / licence | Through agents (Agent Builder, declarative agents) |

**Terms in our documents:**
- **Microsoft's products:** "Copilot for legal", "the legal plugin in Word", "Legal for Cowork", "Cowork". Never "Claude Cowork" or "Cowork for Legal".
- **Qualify "playbook":** "legal playbook" means positions; "Word playbook" means its `.docx`; "Spaarke AI playbook" means a platform workflow.
- **Qualify "skill":** "Agent Skill" means the folder format; "Spaarke Skill scope" means the platform's prompt fragment.

**So:**
- The legal plugin in Word *consumes* our content: we hand it Word playbooks.
- Cowork is our *platform*: we install skills, engines and connectors into it.
- How they differ, and how they work together, is in §4.5.

### 4.2 The legal plugin in Word

The legal plugin is built into the Copilot pane in Word desktop. It is not an add-in, there is nothing to install, it is on by default, and it is governed by a Cloud Policy.

**Playbook review:**
1. The attorney asks Copilot for a playbook review.
2. They attach a `.docx` playbook from OneDrive or SharePoint.
3. Copilot parses it into **topics, each with rules and example clauses**. The attorney edits these, then clicks **Run Review**.
4. Non-compliant clauses get proposed edits, inserted as **native tracked changes** on Accept.

Other skills: Q&A with citations, single-issue redlines, and drafting from a precedent.

**Limits** `[Cited]`:
- It reviews against playbooks only, not templates.
- It has no documented custom skills, no custom instructions and no memory.
- It runs in **Word for Windows desktop** only; Mac and web are not listed.
- **Playbooks are attached per review.** Each review asks for the `.docx` again. There is no default, no library setting and no automation.
- **Model.** Word has a user model picker (Auto / Claude / GPT, GA May 2026). Whether the legal plugin follows it is undocumented.
- **No credits.** In-app use is covered by the licence.
- **Add-ins can't drive it.** An Office add-in cannot open the Copilot pane, fill in a prompt, or switch plugins or playbooks.

**Consequence:** a current, versioned, practice-specific **library of Word playbooks**, consistent with the Cowork skills, is ours to provide.

### 4.3 Cowork and Legal for Cowork

**Access:** an admin grants Cowork through a **Copilot Credits spending policy** (minimum 2,000 credits per user per month). The attorney adds Legal for Cowork from Customize › Roles & Industry › Legal.

**The 20 legal skills** `[Cited]`:

| Group | Skills |
|---|---|
| Contracts | review-contract, edit-contract, create-playbook, draft-new-contract, summarize-redlines, compare-key-terms, analyze-related-contracts, extract-obligations-and-deadlines |
| Drafting quality | check-cross-references, check-defined-terms, check-execution-readiness |
| Advisory and research | draft-legal-memo, explain-legal-concepts, legal-reasoning, compare-across-jurisdictions, identify-regulatory-gaps |
| Matters and corporate | build-case-chronology, summarize-legal-matters, draft-minutes-and-resolutions |
| Meta | improve-legal-prompt |

**How our skills run** `[Cited]` `[Observed]`:

- **Format.** A skill is an Agent Skills folder: `SKILL.md` plus `references/` (loaded on demand), `scripts/` (executed, not read into context) and `assets/` (templates).
- **Installing.**
  - Attorneys install skills by copying folders into OneDrive `Documents/Cowork/Skills/`, or by uploading a plugin and sharing it with colleagues.
  - Each user can have up to 50 custom skills.
  - New sessions pick up new skills.
- **Choosing a skill.**
  - Cowork reads every skill's name and description at session start and matches requests against them.
  - Users can also call a skill as `/skill-name`.
  - Overlap with Microsoft's skills is managed through precise descriptions and "Do NOT use for…" lines.
- **Scripts.**
  - Scripts run in a temporary Linux workspace inside the Microsoft 365 boundary. The POC ran Python with `lxml` and rendered pages to images.
  - The workspace can reset.
  - Outputs persist only when saved to OneDrive or SharePoint.
- **Built-in skills.** Skills can direct Cowork's built-ins: Word, Excel, PowerPoint, PDF, mail, Adaptive Cards and automations.
- **Approval.** Sending, posting and deleting need the user's approval.
- **Automations.** These are scheduled prompts and event triggers. They belong to the user: we ship recipes and an unattended mode, and partners can supply trigger events through connectors.
- **Personalization.**
  - Custom instructions (20 KB, set in Customize › Preferences) reach every task, but no skill can write them.
  - **Memory is conversation-scoped.**
  - **Projects** are private to their creator.
  - Team context therefore lives in shared skills and the SharePoint library, and personal defaults live in a profile file plus a pasted instructions block.
- **Skill Management** is a built-in skill that creates, validates and scores skills (a 0–100 Skill Report with a conflict scan). Our onboarding uses it.
- **Models.** Users pick GPT, Claude or Grok. **EU Data Boundary tenants get GPT only.**
- **Data reach.** Cowork can't reach **SharePoint Embedded** content: the POC got 403 on both list and read. It **can** read Dataverse under the user's own roles where the tenant provides that.

### 4.4 Marketing vs documentation

Microsoft's launch post mentions drawing on approved precedents in SharePoint, reusable playbooks, "matters", and partner plugins inside Word. None of these has a documented step today. These are exactly the gaps that accelerators and, at T3, the Spaarke platform fill. Say so plainly in sales, because customers will hit them.

### 4.5 Word and Cowork: same expertise, different products

The two legal plugins share a vocabulary (playbook review, drafting from precedent, redlines) but **nothing else**. Each has:
- its own runtime;
- its own billing (Word: licence; Cowork: credits);
- its own admin switch;
- its own model;
- its own inputs.

Microsoft documents no shared playbook object, no playbook schema or sample, and no hand-off between them. The only bridge is the file in OneDrive or SharePoint `[Cited]`.

| | Legal plugin in Word | Legal for Cowork (and our Cowork skills) |
|---|---|---|
| Works on | One open document | Many files, mail, meetings and sources |
| Takes from us | A Word playbook `.docx` per review. A Spaarke agent in "All agents" (chat only) is possible. Our plugin in Word is to be tested. | Agent Skills, references, engines, a connector |
| Playbook selection | Manual, every review | Automatic: our skills look it up and name it |
| Produces | Tracked changes and drafts in that document | New files, in-place edits, mail, cards, automations |
| Persists | Nothing documented | Skills, custom instructions, private Projects, automations |

**What this means for the accelerators** `[Judgment]`:
1. **One content source feeds both.** The Word playbook and the Cowork skills are rendered from the same topics and positions (§7). That gives attorneys the same answer in Word and in Cowork, which Microsoft does not provide.
2. **We route, because Microsoft doesn't.** Every accelerator tells the attorney which flow goes where:

   | Flow | Goes to |
   |---|---|
   | Review one contract while reading it | Word, with our Word playbook |
   | Draft from a precedent | Word |
   | Packaged deliverable (internal and external redlines with a linked summary, clean copy, cover email) | Our Cowork skill |
   | Batches, abstracts and exception reports in Excel | Our Cowork skill |
   | Automations | Our Cowork skill |
   | Work outside our practices | Legal for Cowork |

   The full table is in design §2.4.4.
3. **Our value is the layer neither plugin has:** positions, house style, onboarding, verifiable outputs and alignment with the Spaarke platform.

---

## 5. The offering

### 5.1 Who it is for

The accelerators serve the **attorney work** side of legal operations intelligence: review, drafting, diligence, abstraction, invoice review.

| | Law firm | Corporate legal department |
|---|---|---|
| **Typical buyer** | Innovation / KM lead; practice-group leader | Legal operations director; deputy GC |
| **IT gate** | Firm CIO/CISO; often Information Barriers | CIO / legal technology lead |
| **Natural entry** | T0/T1 for groups without Information Barriers; **T2 for walled firms** | T0/T1 per team; T2 department-wide |
| **Assets the accelerator draws on** | Knowledge lawyers' precedents and playbooks; DMS via partner plugins | Policies, contract standards, outside-counsel guidelines, e-billing data |
| **Best-fit accelerators** | NDA, CRE, Employment, U.S. Patents (prosecution) | NDA, Invoice Review, CRE (portfolio), Employment, Patents (portfolio) |
| **T3 upgrade means** | The firm's own platform, and the **engagement edge** (business plan §6) | The full Spaarke platform: matters, policies, obligations, decision record |

`[Judgment]` Firms may be the faster early market for T0–T2. Departments remain the stronger T3 market.

### 5.2 What a practice accelerator is

```
┌───────────────────────────────────────────────────────────────┐
│ Customer layer   legal playbooks · attorney profiles           │  customer-owned files,
│                  (positions, defaults, house style)            │  built during onboarding
├───────────────────────────────────────────────────────────────┤
│ Practice layer   Agent Skills · references · assets ·          │  Spaarke-authored from
│                  Word playbooks · baseline positions · evals   │  researched sources,
│                                                                │  attorney-approved
├───────────────────────────────────────────────────────────────┤
│ Core             onboarding · help · verification and          │  shared by every accelerator
│                  disclosure rules · deterministic engines       │
└───────────────────────────────────────────────────────────────┘
```

Updates to Core and to the practice layer never overwrite the customer layer. That separation is what makes the subscription maintainable.

**Onboarding is part of the product.** The setup skill (`/accelerator-setup`) walks a new attorney through the steps below as a series of Cowork question cards, with a quick path (about 3 minutes) and a full one (about 15). It can be paused, resumed and re-run for one section.

| Step | What it covers |
|---|---|
| 1. Check | The environment (sandbox, installed skills, skill cap, library) |
| 2. Role | Practice and usual side |
| 3. Skills | Choose and install them, through Cowork's Skill Management |
| 4. Legal playbook | Use the group's, import one, build from precedents, or start from the baseline |
| 5. Personalize | A profile file plus a custom-instructions block to paste |
| 6. Try | A first review |
| 7. Refine | "What did I get wrong?" |
| 8. Automations | Optional recipes |

The pattern follows Anthropic's open-source `claude-for-legal` cold-start interview, re-pointed at Microsoft's primitives. Design §8.1a has the full specification.

### 5.3 Tiers

| Tier | Who installs | Time | Adds | What the customer gets | Surfaces |
|---|---|---|---|---|---|
| **T0: Attorney** | The attorney | Hours | An M365 Copilot seat with Cowork, and no Information Barriers | Accelerator skills (folder copy or plugin upload); deterministic engines; Word playbooks; onboarding → their legal playbook and profile; automation recipes | Cowork, Word |
| **T1: Practice team** | Practice lead, no IT | 1–2 days | A SharePoint site the lead owns | T0, plus a **Legal Playbooks** library (the group's master legal playbooks, precedents, clause library, accelerator packages); plugin shared with the group; optional Agent Builder agent for Copilot Chat users | + Copilot Chat |
| **T2: Firm or department, IT-supported** | Customer IT + Spaarke legal engineer | 3–5 days | Admin deployment; connector consent | T1, plus an org-wide package for a security group; the **Spaarke connector** where the customer uses Spaarke, with elicitation forms and MCP Apps; optional **Foundry** for unattended and batch work | + interactive UI, Foundry |
| **T3: Spaarke platform** | Spaarke engagement (Quick Start onward) | Per business plan | The Spaarke platform in the customer's tenant | Same content, now bound to the ontology: legal playbooks as Policy versions; matters, obligations and invoices as governed objects; the Decision Record; Spaarke AI running the same topics | + Spaarke Console, Word/Outlook add-ins |

`[Judgment]` T0 and T1 are the "use Microsoft instead of Claude" tiers and the marketing engine. T2 is the paid enablement product. T3 is the platform.

**Caveats that shape what we promise:**
- **Information Barriers block plugin and skill upload.** Walled firms enter at T2.
- **Admins can block organization-built plugins.** T0 ships with an IT one-pager so it reads as governed, not as shadow AI.
- **A user can upload a connector, but each user consents.** Tenants that restrict user consent need an admin.
- **SharePoint site skills don't load in Cowork,** and `.agent` files are retiring. T1 distribution is the library plus Cowork sharing.
- **EU Data Boundary tenants get GPT only.** Skills must not depend on one model.

### 5.4 The first practice accelerators

Waves (decided 2026-10-31):
- **Wave 1:** NDA, Legal Intake, Invoice Review.
- **Wave 2:** Commercial Real Estate, Employment.
- **Wave 3:** U.S. Patent Applications.

| Accelerator | Core job | Why it is in the first set |
|---|---|---|
| **Legal Intake** (Wave 1) | Question cards by request type → a structured request **emailed to a designated intake mailbox** (summary, attachments, machine-readable block) | The legal front door every department needs, with no Spaarke dependency at T0. At T3 the mailbox feeds Spaarke's intake through the server write path |
| **NDA** (reference) | Review against the legal playbook, then deliver an internal redline (summary page, comments), an external redline and a clean version; high-volume triage | Proven in the POC. High volume for firms and departments. Our value over the built-in `review-contract` is positions, side, posture and engine-quality redlines |
| **Employment** | Agreement, restrictive-covenant and separation review (state references; OWBPA timing by engine) | The richest open sources (a 56-jurisdiction non-compete survey; EEOC OWBPA guidance); every organization has the work |
| **Commercial Real Estate** | Lease review by side and type; lease abstract in Excel with critical dates as formulas | Strong for firm practices and department portfolios; engines do the dates |
| **Invoice Review** | Outside-counsel guideline compliance (LEDES or PDF) with an Excel exception report and reduction letter | The most differentiated: Microsoft's legal skills don't cover it, and Spaarke Finance Intelligence sits behind it at T3 |
| **U.S. Patent Applications** | Application QC (claims, antecedent basis, spec support, numerals), disclosure intake, office-action outline | Mechanical checks suit engines; it plays to in-house patent expertise |

**Later:** M&A (diligence tables, closing checklists), Privacy/DPA (richest open-source seeds), litigation support (where it adds to Legal for Cowork rather than duplicating it). Full specs are in [design.md §8](design.md).

### 5.5 The service: the enablement sprint

This is forward-deployed delivery compressed to about a week: the Spaarke Method run once, for one practice.

| Day | Method phase | Output |
|---|---|---|
| 0 (remote) | Pre-flight | Seats, Cowork spending policy, Word legal skills, Information Barriers, consent owner, EU Data Boundary. Tier confirmed. |
| 1 | Envision | Install Core and the accelerator; choose two or three use cases; baseline time and quality |
| 2–3 | Design + build | Onboarding with the practice lead → customer legal playbooks; library curated; automation recipes; connector and forms (T2) |
| 4 | Build test | Evaluation on the customer's own documents, timed against the baseline |
| 5 | Deploy | Rollout, training, handover; usage measurement starts |

The **accelerator subscription** follows the sprint. It covers content updates, Microsoft change tracking (the surfaces change monthly), new skills and quarterly re-evaluation.

### 5.6 Commercial shape `[Open]`

AI runs at cost on the customer's own agreement: Copilot seats and Cowork credits are theirs. Business users are never charged per seat.

**Recommendation:** a fixed-fee sprint per practice plus an annual subscription per accelerator, with the sprint fee credited toward a platform Quick Start. Offer a free T0 sample skill per accelerator as marketing. Firms are priced per practice group.

---

## 6. Runtimes and where the pieces run

### 6.1 Three Microsoft runtimes, each with a job

| Runtime | Job | Why |
|---|---|---|
| **Word (legal plugin)** | Document-level playbook review and redline | It is where the document is. We supply the Word playbooks, and our Cowork skills produce the same topics |
| **Cowork (and Copilot Chat)** | Multi-step, multi-document work, run by our skills and engines: review packages, abstracts, exception reports, intake | Runs Agent Skills with scripts; reaches mail, files and meetings; renders forms and MCP Apps |
| **Microsoft Foundry** (customer's Azure, T2+) | **Unattended work**: scheduled sweeps, high-volume extraction, evaluation runs | Cowork runs when a user starts it and meters per user. Foundry Routines (GA 2026-09-24) run on schedules under an agent identity `[Cited]` |

### 6.1a Why the engines exist

The engines are small Python scripts that ship inside a skill. They do the mechanical work:
- surgical tracked changes, comments and bookmarks;
- a linked summary page and a clean copy;
- dates, amounts and Excel formulas;
- checks that the output matches the plan.

The model does the judgment.

**Where they came from.** The POC. Cowork's own in-place editing couldn't make bookmarks or surgical redlines, so Cowork wrote this code in the session, and the code was lost when the workspace reset `[Observed]`.

**Why ship them:**
- **Precedent.** Anthropic's production document skills ship about 11,000 lines of such helpers and validators. Anthropic's guidance is to "prefer scripts for deterministic operations" and "solve, don't defer" `[Cited]`.
- **Evidence.** Benchmarks show models miss date arithmetic (13–16% accuracy on durations, typically one day off) and multi-step arithmetic `[Cited]`.
- **Model independence.** Cowork lets users choose GPT, Claude or Grok, so only code gives the same output on every model.

**Where we defer.** Microsoft's Word plugin and Legal for Cowork's `edit-contract` already produce native tracked changes. A single-document review in Word therefore uses Microsoft's plugin with our Word playbook. Our redline engine is for the packaged deliverable, batches and automations. Design §5.6 gives the engine-by-engine verdict.

### 6.2 Where each piece runs, by tier

| Piece | T0 | T1 | T2 | T3 |
|---|---|---|---|---|
| Skills | Cowork (folder or plugin) | Shared plugin; optional Agent Builder agent | Admin-deployed package | + bound to Spaarke through the connector |
| Legal playbooks | OneDrive (Markdown master + Word `.docx`) | SharePoint library | same | Policy versions in Spaarke (master), exported |
| Engines | Python in the Cowork sandbox | same | + BFF tools through the connector | BFF tools |
| Spaarke data | Optional read-only matter context | same | Connector: SPE documents, matters, write path | Full platform |
| Forms / UI | Question cards, Adaptive Cards | same | Elicitation forms, MCP Apps; SPFx optional (Chat/Teams) | + decisions written back |
| Unattended work | Automation recipes | same | + Foundry | + Spaarke background jobs |

---

## 7. One content model across Spaarke, Word and Cowork

A practice's content appears on three surfaces, in three formats:
- **Cowork:** Agent Skills, Markdown and Python.
- **Word:** a `.docx` Word playbook.
- **The Spaarke platform:** JPS Actions and Knowledge in Dataverse.

Storage and format may differ. **Meaning may not.**

**One source per practice** (`practices/<p>/` in the accelerator source) is built into all three. The build also checks four alignment keys:

| Key | What it guarantees |
|---|---|
| **Topic catalogue** (stable topic IDs, e.g., `nda.term`) | The same review topics appear in the legal playbook, the Word playbook, the Cowork checklist, the JPS output enum and the evaluation rubric |
| **Review-result schema** | A review means the same thing wherever it ran: basis, issues by topic and status, priorities, edits. It can be logged, compared and reported the same way |
| **Version stamp** | Every output names the accelerator version and legal playbook version that produced it |
| **One customer master** | A customer's legal playbook has exactly one master copy. At T0/T1 that is the Markdown file in SharePoint or OneDrive, and the Word `.docx` is rendered from it. At T3 it is Spaarke's Policy version, exported for Cowork and Word. Never two masters. |

**Engines exist twice.** Deterministic mechanics run as Python in the Cowork sandbox and as C# in the BFF. Both are tested against the same golden fixtures and the same schema. From T2, Cowork can call the BFF's implementation through the connector.

The detailed rules, including release, drift and the build gate, are in [design.md §6](design.md).

---

## 8. Spaarke AI and JPS

**JPS matters, as the platform twin of an Agent Skill.** Cowork doesn't read JPS, and JPS never goes inside a skill. But both describe the same thing:

| JPS | Agent Skill |
|---|---|
| `instruction` | The `SKILL.md` body |
| `input` | Intake questions and profile |
| `output` | The review-result schema |
| `scopes` | `references/` |
| `$choices` | Topic IDs |
| `examples` | Eval cases |

The accelerator build therefore also generates JPS Actions. They are validated in R1 and deployed in a later project.

**Spaarke already has content for five of the six first practices** (Legal Intake maps to Spaarke's service-request types):

| Kind | Content |
|---|---|
| Actions | NDA Analysis; Employment Agreement Review; Lease Agreement Review; Invoice Processing; the patent-application extract |
| Skill scopes | NDA Review; Employment Contract; Lease Review; Invoice Processing |
| Knowledge | NDA Review Checklist; employment law references; Lease Agreement Standards; invoice validation rules |
| Tools | Clause analysis and comparison; invoice extraction |

**That content is an untested candidate, not the seed.** None of it has been evaluated against a corpus or reviewed by an attorney. So for each practice:
1. It is mapped onto the topic catalogue built from researched sources (Appendix A).
2. It is run on the same eval corpus as the draft Agent Skill.
3. It is kept topic by topic only where it scores better and an attorney approves it.

Every Spaarke gap or defect the comparison finds is filed to the Spaarke backlog (design §7.3). The accelerators therefore also serve as an **evaluation programme for Spaarke's own legal AI content**: the platform gets an evaluated, attorney-approved target to converge on. The topic catalogue becomes the shared vocabulary between them.

**What Cowork's design suggests for Spaarke AI** `[Judgment]`. These are for discussion and are detailed in design §7.2.

1. **Customer legal playbooks should be first-class.** CUST- Knowledge works today. The ontology's Policy versions are the proper home.
2. **Adopt the review-result schema** as the output contract of Spaarke's legal review Actions.
3. **Load reference material on demand** rather than growing one composed prompt.
4. **Expose Spaarke's deterministic tools through the connector**, so there is one implementation from T2.
5. **Resolve the "skill" naming collision** through the ontology terminology process.
6. **Later:** export a Spaarke Action as an Agent Skill, so platform customers can take content into their own Cowork.

---

## 9. Forms and interactive UI

Copilot offers five ways to show or collect structured information:

| Option | What it is | Hosted by | Tier | Good for |
|---|---|---|---|---|
| **Question card** | Cowork asks structured questions when a skill says to (the POC's intake) | Nothing | T0 | Fixed-option intake, review settings |
| **Adaptive Card** | Cowork's built-in card renderer, driven by a skill | Nothing | T0 | Dashboards and summaries (e.g., a risk summary) |
| **MCP elicitation form** | Our connector asks Cowork to collect input against a schema; Cowork renders a native form | Spaarke connector | T2 | Simple forms with options looked up from Spaarke |
| **MCP App** | Our connector serves a web UI that Cowork shows inline or full screen. It can call our tools and return results to the conversation | Spaarke connector | T2 | Rich forms and grids: Legal Front Door / service-request intake with look-ups, matter cards, review and invoice exception grids |
| **Copilot UX components (SPFx)** | SharePoint Framework UI that renders in Copilot Chat and Teams, **not in Cowork** | Customer's app catalog (admin) | T2 option | Chat- or Teams-standardized customers |

**Design rule:** every skill works without interactive UI. Word can't render it, T0/T1 have no connector, and mobile is undocumented. UI is an upgrade, never a dependency.

**Legal Front Door is the flagship T2 case.** An MCP App intake form in Cowork:
1. looks up client, matter and practice area through the connector;
2. submits through the BFF write path, where validation, routing and assignment run server-side;
3. returns a confirmation card with a deep link.

"Service request assigned to me" can then trigger a Cowork automation.

---

## 10. The Spaarke connection

- **Cowork can't reach SharePoint Embedded** through its built-in Graph access `[Observed]`. Opening Spaarke's container type to Copilot discovery would expose content across Microsoft 365, so that is not the route.
- **The route is a Spaarke connector: an MCP endpoint hosted by the Spaarke BFF**, which is Spaarke's single backend. It is not a separate API.
  - **Sign-in:** the user's Entra identity, on their behalf, with Spaarke permissions enforced.
  - **Installation:** it ships in the accelerator plugin. An attorney can upload it, and each user consents.
- **What it provides:**
  - document search, open and save-as-new-version on SPE;
  - matter context;
  - review logging, which takes the review-result schema;
  - elicitation forms and MCP Apps;
  - Spaarke's deterministic tools;
  - trigger events for automations.
- **Writes go only through the BFF write path** (ADR-002). Direct Dataverse writes from Cowork, for example through a Dataverse connector's "create row", are not allowed. Read-only Dataverse access is fine.
- **Data boundary.** At T0/T1, Spaarke hosts nothing and stores no customer content. Customers who don't use the Spaarke platform don't need a Spaarke-hosted service at all, because the engines run in Microsoft's sandbox.

---

## 11. How it fits the Spaarke platform

### 11.1 Against the business plan's five components

| Component | Role of the program |
|---|---|
| 1. The platform | T3. Accelerators bind to it without rework (§7, §10). |
| 2. Forward-deployed legal engineers | They deliver the sprint. Each engagement adds to the accelerator library. |
| **3. The customer's own AI** | **This program is component 3, productized.** It runs first on Copilot and Cowork, then on Foundry. |
| 4. Managed services | The accelerator subscription is the first managed-service line, ahead of any platform work. |
| 5. Connections and ecosystem | The Spaarke connector, partner plugins and the plugin registry. |

### 11.2 The offer ladder, extended

```
 NEW ► Practice accelerators (T0–T2) ─► Quick start ─► Ontology build ─► First use cases ─► Scale & manage
       days; Copilot only              (existing ladder, business plan §9)
```

Firms also have a second branch. When a firm's client runs Spaarke, the firm's accelerator work on that client's matters becomes the **engagement edge** (business plan §6).

### 11.3 The upgrade path: same content, richer binding

| T0–T2 artifact | On the platform (T3) |
|---|---|
| Legal playbook (Markdown master + Word `.docx`) | A **Policy version**, cited on decisions; the files are exported from it |
| Review result from Cowork or Word | An Analysis logged on the matter; issue decisions in the **Decision Record** |
| Critical-dates and obligations workbook | **Obligation** objects with owner and lifecycle |
| Invoice exception report | Invoice in **Finance Intelligence**, with spend signals |
| "Which matter is this?" answered by the attorney | Matter context from the connector |
| Usage baseline | The decision baseline that the Spaarke Method's manage phase measures |

### 11.4 Positioning

- **With Microsoft, not against it.** We make Copilot for legal work for a practice and for this customer. That also positions Spaarke for Microsoft partner co-sell.
- **Neutral by format.** Agent Skills run in Claude too. We lead with Microsoft and keep Claude as a fallback.
- **The moat is not the prompts.** Skills are copyable, and Microsoft ships 20 legal skills. What lasts is:
  - the customer layer built by legal engineers;
  - the engines;
  - evaluations on the customer's own work;
  - alignment with the platform;
  - maintenance against a Microsoft surface that changes every month;
  - the T3 bridge.

---

## 12. Risks

| Risk | Mitigation |
|---|---|
| **Commoditization:** Microsoft or partners ship practice skills | Differentiate on the customer layer, engines, evals, alignment and the T3 bridge |
| **Overlap with Legal for Cowork confuses skill selection** | Precise descriptions, "Do NOT use for…" lines, explicit hand-offs, selection tests |
| **IT pushback on self-serve uploads** (especially firms) | IT one-pager; T2 as the governed path |
| **Information Barriers** block T0/T1 | Walled firms enter at T2 |
| **Sandbox behaviour is undocumented** (libraries, network, resets) | Dependencies kept to Python standard library + `lxml`; a probe spike; skills stop and report rather than fall back to model arithmetic |
| **Content drift between Cowork, Word and Spaarke** | One source per practice, alignment keys, build gate (§7) |
| **Cost surprises:** Cowork credits are the customer's | Pre-flight sets expectations; the usage baseline shows cost per task |
| **Platform churn:** much is new or preview; docs changed during our research | Subscription maintenance; pinned compatibility per release; re-verify after Ignite (17–20 Nov 2026) |
| **EU Data Boundary:** GPT only | Model-agnostic skills, evaluated on GPT |
| **Content quality and liability** | Attorney sign-off on baseline content; dated references; evaluations; disclaimers; human review in every output |
| **Scope drift into a second platform** | No accelerator-only data store; Spaarke integration only through the BFF connector; anything stateful is T3 |
| **Name:** "Microsoft for Legal" uses Microsoft's mark | "Spaarke Solution Accelerators"; check Microsoft partner branding rules `[Open]` |

---

## 13. Roadmap

| Phase | Window | Scope | Exit evidence |
|---|---|---|---|
| **A. Foundation + first accelerators** (R1, [design.md](design.md)) | Nov 2026 – Q1 2027 | Claude Code knowledge; tenant spikes; content model; engines from the POC; build pipeline; NDA, Employment, CRE, then Invoice Review and U.S. Patents at T0/T1 | An attorney in a test tenant installs and uses an accelerator in under 2 hours. Dry-run sprint in ≤ 5 days |
| **B. Spaarke connector + T2** | Q1 2027 (designed in parallel with A) | BFF-hosted MCP connector; documents on SPE; review logging; elicitation forms and the first MCP App (Legal Front Door intake); admin package; first design-partner sprints (one firm, one department) | A sprint finished in 5 days with a timed before/after on the partner's own work |
| **C. Breadth + Foundry** | Q1–Q2 2027 | M&A, Privacy/DPA; Foundry templates (Routines, batch, evals); more MCP Apps (review grid, invoice exceptions) | Seven accelerators; second and third customers |
| **D. Platform bridge** | With ontology r2 | Legal playbooks as Policy versions; JPS deployment from the accelerator source; Decision Record, Obligations, Finance Intelligence mapping; store listing | A T2 customer converts to a Quick Start with accelerator content carried over |

---

## 14. Relationship to existing projects

- **spaarke-ontology-platform-r1/r2.** Owns Policy, Obligation and the Decision Record, and all terminology changes (`ontology-component-model.md` §3). The accelerators consume these and don't redefine them.
- **AI platform (JPS, scopes).** The accelerator source generates JPS Actions for the platform. It compares against the existing SYS- content rather than copying it, and files what it finds to the platform backlog (§8).
- **Auth v4 / BFF.** The connector is new BFF surface, so it needs a placement decision, a hot-path declaration and a publish-size check. It also follows the on-behalf-of direction.
- **customer-provisioning-orchestration-r1.** The per-customer Copilot agent template and tenant-value stamping are the base for the T2 admin package.
- **Compose, Word add-in.** We reuse their docx expertise. The Cowork engines are Python because the sandbox runs Python, and they share golden fixtures with any C# equivalent.
- **Agreement Analysis repositioning** (business plan §5). Spaarke's contract-review content reaches attorneys inside Copilot for legal, rather than competing with it.

---

## 15. Program decisions

| # | Decision | Recommendation |
|---|---|---|
| D1 | Name | "Spaarke Solution Accelerators"; per practice, "practice accelerator" (design OD-1) |
| D2 | First accelerators | **Decided 2026-10-31:** NDA, Legal Intake (email to a designated mailbox), Invoice Review (Wave 1); CRE, Employment (Wave 2); U.S. Patents (Wave 3) (design OD-2) |
| D3 | Format and packaging | Agent Skills as the source format; T0 ships folders and a plugin (Claude format if it converts cleanly); T2 an admin package |
| D4 | Spaarke-hosted services | None for non-Spaarke customers (engines run in the sandbox). The Spaarke connector is hosted by the BFF for platform customers |
| D5 | Where the source lives | In this repo (`src/accelerators/`) for R1; revisit a separate content repo at first commercial release |
| D6 | Commercial model | Sprint plus subscription, credited toward Quick Start; free T0 sample; per practice group for firms |
| D7 | Claude as a target | Supported by format; not marketed initially |
| D8 | Interactive UI | MCP Apps and elicitation forms through the connector; SPFx components only for Chat/Teams-standardized customers |
| D9 | Content alignment | One source per practice; topic IDs, review-result schema, version stamps, one customer master (§7) |
| D10 | Next project | Spaarke Cowork connector (Phase B), designed in parallel with R1 |
| D11 | First design partners | One firm and one department |
| D12 | Existing Spaarke AI content | An untested candidate: compared on the same corpus, never copied unreviewed; findings go to the platform backlog (design OD-15) |
| D13 | Word vs Cowork | A single-document review goes to Microsoft's Word plugin with our Word playbook; packaged deliverables, batches and automations go to our Cowork skills (design OD-16) |
| D15 | Specialist reviewers | A panel of specialists inside one skill now; Foundry specialist agents through the Spaarke connector at T2. Scripts never call models (design OD-19) |
| D16 | Spaarke in Word | Word playbooks now; test our plugin and a declarative agent in Word; an add-in launcher waits for a Microsoft API (design OD-18) |
| D14 | Onboarding | A re-runnable setup skill in every accelerator's Core; the profile is a file plus a pasted custom-instructions block (design OD-17) |

## 16. To verify before external claims

1. Behaviour of the script sandbox in a clean tenant: libraries, network, run time and resets (design spike S2).
2. Run-time reads of legal playbooks from OneDrive and SharePoint (S3).
3. A non-admin connector upload under default and restrictive consent settings.
4. Whether "Allow agents and plugins built by your organization = off" blocks user uploads (S1).
5. How the legal plugin in Word parses Word playbook structure, and whether edits persist (S5).
6. Whether Legal for Cowork's `review-contract` uses a legal playbook we supply (S6).
7. Adaptive Card interactivity, and MCP Apps rendering with our connector in practice.
8. Licence and attribution terms for every third-party content source before the first commercial release.
9. What Legal for Cowork's `create-playbook` produces and where, and whether Word can use it (S6).
10. Whether `edit-contract` makes word-level tracked changes (S13).
11. Whether a skill can drive Skill Management to install other skills (S14), and whether plugin skills count toward the 50-skill cap (S15).

---

## Appendix A. Content sources

Skill content is assembled from five tiers, in this order of trust:
1. primary law and official guidance;
2. open standard agreements;
3. open-licensed skill repositories;
4. open datasets (evals only);
5. Spaarke's existing content (compared, not copied).

Our attorneys write the positions, thresholds and state tables, and approve everything. The per-practice register is in design §8.5.

| Source | Licence | What it gives us |
|---|---|---|
| **The 2026-10-30 POC** | Ours | NDA skill draft, legal playbook format, edit-plan schema, redline/summary engines, behaviour tests, lessons learned |
| **Spaarke platform content** | Ours, **untested** | Existing Actions, Skill scopes, Knowledge and Tools for NDA, employment, lease, invoice and patent work. A candidate to compare (§8) |
| **OpenAgreements** | Apache-2.0; templates CC BY 4.0 / CC0 | The best NDA and employment scaffold: cited U.S. practice guides, a 56-jurisdiction non-compete survey, state templates, checklists, and a provenance model we adopt |
| **Common Paper, Bonterms** | CC BY 4.0 | Standard NDA and commercial terms: baseline positions and model language |
| **CaseMark skills, LegalQuants, cre-skills-plugin** | Apache-2.0 | Lease, SNDA, estoppel, separation and non-compete maps; an NDA playbook schema; lease-abstract fields and CAM logic |
| **Government and standards sources** | Public domain / free | EEOC OWBPA guidance, MPEP and USPTO datasets, GSA lease forms, LEDES 98B and UTBMS codes, public outside-counsel guidelines; the WIPO Patent Drafting Manual (CC BY 4.0) |
| **Datasets** | CC BY 4.0 / public domain | CUAD, ContractNLI, licensed LegalBench tasks, USPTO research data: for evals |
| **`mike-workflows`** (Mike OSS) | MIT | About 14 Agent Skills workflows (NDA, commercial lease, employment review…) and about 12 tabular column sets. The Mike application itself is AGPL and not reusable; its workflow content is separate and MIT-licensed, so it can be adapted with attribution. Its lease content is UK law. |
| **`claude-for-legal`** (Anthropic) | Apache-2.0 | Method and guardrails, plus practice skills. It reads settings from a Claude-only path, so skills must be adapted to read our legal playbook files instead |
| **Lavern reference vault** | Apache-2.0 | Pre-registered evaluation rubric method; NDA templates; U.S. lease checklist lines |

**Not usable:**
- `lawve-ai/awesome-legal-skills` (CC BY-NC-ND);
- `borghei/Claude-Skills` (Commons Clause);
- oneNDA and YC (no derivatives);
- NVCA (no redistribution);
- `lexdays` (AGPL);
- HUPD and the non-commercial LegalBench tasks.

Pinned versions and licence handling are in [references.md](references.md) and [design.md §8.4–§8.5](design.md).
