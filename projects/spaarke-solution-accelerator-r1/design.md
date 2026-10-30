# Spaarke Solution Accelerator R1: Practice Accelerators for Copilot for legal and Cowork (T0/T1 foundation)

> **Status:** Draft v0.1 for owner review → `/design-to-spec` after the decisions in §14 are made.
> **Date:** 2026-10-29
> **Author:** Ralph Schroeder + Claude Code
> **Companion documents:** [solution-strategy.md](solution-strategy.md) (program strategy, v0.3) · [references.md](references.md) (curated reference set)
> **Tags:** `[Cited]` verified against primary sources 2026-10-29 · `[Judgment]` · `[Open]` owner decision · `[Verify]` must be proven in the test tenant (§9)

---

## 1. Why this project exists

Microsoft shipped **Copilot for legal** on 2026-10-15: a legal plugin in Word and a 20-skill **Legal for Cowork** plugin, both included in the Copilot seats most law firms and legal departments already own (strategy §3). The skills are generic. They carry no practice content and no knowledge of an organization's own positions. Word takes a playbook only as a `.docx` attached to each review, and nothing is saved between reviews.

Attorneys who want practice-grade AI today reach for Claude with legal plugins. **This project lets them get that inside Microsoft instead**, under their own tenant's identity and protections, with no IT involvement at the entry level. It does so with **practice accelerators**: practice-specific skills, Word playbooks and a team library, deployed by an attorney (T0) or a practice team (T1).

R1 builds the **foundation** and the **first five accelerators**. Every artifact is designed to carry forward unchanged into the IT-deployed tier (T2) and the Spaarke platform (T3), which are later projects (strategy §4.3).

---

## 2. Terminology

**Microsoft's products** (strategy §3.0; quoted from the Microsoft admin page):

| Term | Meaning |
|---|---|
| **Copilot for legal** | Umbrella name for **two** legal plugins. Not a synonym for Word. |
| **The legal plugin in Word** | Legal skills in the Copilot pane in Word desktop: playbook review → tracked changes; drafting from precedent. **Not extensible**: its only input from us is a `.docx` playbook. |
| **Legal for Cowork** | Microsoft's built-in plugin of 20 legal skills, running inside Cowork. |
| **Copilot Cowork ("Cowork")** | Copilot's general-purpose multi-step agent mode. **Our platform at T0/T1**: it accepts our skills and plugins. |

**Our terms** (proposed; decision OD-1):

| Term | Meaning |
|---|---|
| **Spaarke Solution Accelerator** | The program and product family. |
| **Practice accelerator** | One practice area's package: skills + Word playbooks + library content + evaluations (e.g., "NDA Accelerator"). Replaces "practice pack" and "enablement kit". |
| **Core** | Skills and shared references used by every practice accelerator. |
| **Overlay** | The customer's own positions, house style and escalation rules, kept as files the customer owns, read by the skills at run time (§5.2). |
| **Baseline positions** | Spaarke's attorney-reviewed default positions, used until a customer creates an overlay. |
| **Skill / plugin / connector** | A folder with `SKILL.md` / an installable package of skills and connectors / a remote MCP server inside a plugin (Microsoft's usage). |

---

## 3. Scope

### 3.1 In scope

1. **Claude Code knowledge and tooling** for building on Cowork, Copilot for legal and SharePoint skills (§7). This comes first, because these tools post-date Claude's training data.
2. **Verification spikes** in a test tenant (§9).
3. **The accelerator foundation:**
   - source format
   - overlay model
   - build and validation pipeline
   - Core skills
   - Word playbook rendering
   - T1 library template
   - licence and attribution handling
4. **Five practice accelerators** in two waves (§6):
   - **Wave 1:** NDA, Employment, Commercial Real Estate
   - **Wave 2:** Invoice Review, U.S. Patent Applications
5. **Evaluation**: per skill and per accelerator (§8).
6. **Delivery collateral for T0/T1:**
   - attorney install guide
   - practice-team setup guide
   - IT one-pager
   - enablement-sprint runbook (T0/T1 version)

### 3.2 Out of scope (later projects)

- **T2:**
  - Spaarke Kit Tools MCP server
  - MCP Apps interactive UI
  - SPFx UX components
  - admin tenant package
  - Foundry agents and Routines
- **T3:** the Spaarke platform bridge (Policy versions, Decision Record, Obligations, Finance Intelligence).
- **Any change to the BFF (`Sprk.Bff.Api`), Dataverse, the Spaarke Console (`src/solutions/SpaarkeAi/**`) or the Word/Outlook add-ins.** R1 is content, tooling and packaging only.
- Partner Center / store listing; commercial pricing; a download portal.
- Jurisdictions other than the United States (decision OD-6).

---

## 4. Use cases

| ID | Actor | Journey | Tier |
|---|---|---|---|
| **UC-1** | Attorney (firm or department) | Downloads the NDA Accelerator; uploads the plugin `.zip` in Cowork › Customize. Asks Cowork to "triage this NDA against our positions" and gets a GREEN/YELLOW/RED triage and issues table. Shares the plugin with two colleagues. | T0 |
| **UC-2** | Attorney | Runs the **setup interview** for the NDA Accelerator, which writes their positions, house style and escalation rules to their overlay folder. Later reviews show "Positions: <org> v1, 2026-11-03". | T0 |
| **UC-3** | Attorney | Saves the accelerator's Word playbook (`NDA – Recipient.docx`) to OneDrive. In Word, asks Copilot for a playbook review, attaches it, edits a rule, runs the review and accepts tracked changes. | T0 |
| **UC-4** | Knowledge lawyer / practice lead | Creates a SharePoint library for the practice; uploads the accelerator's library folder tree (playbooks, precedents, clause library, checklists, overlay); copies the site skills into `/Agent Assets/Skills/`. Creates an Agent Builder agent grounded on the library and shares it with the practice group. | T1 |
| **UC-5** | Practice-group member | Uses the shared library's overlay automatically: the T1 overlay overrides personal T0 overlays, so the whole group reviews against the same positions. | T1 |
| **UC-6** | Attorney | Runs a multi-document task (e.g., abstract 12 leases into an Excel abstract with critical dates flagged for verification). The output lands in the OneDrive Cowork folder. | T0/T1 |
| **UC-7** | Spaarke legal engineer | Runs the T0/T1 enablement sprint: pre-flight, install, setup interview with the practice lead, evaluation on the customer's own documents, handover. | T0/T1 |

---

## 5. Solution design

### 5.1 Anatomy of a practice accelerator

```
Customer overlay   positions · house style · escalation       (customer-owned files; §5.2)
Practice layer     skills · Word playbooks · library content   (Spaarke-authored per practice)
Core               setup interview · overlay protocol ·        (shared by all accelerators)
                   verification discipline · disclaimer · help
```

### 5.2 The overlay model

Customer positions are **files read at run time**, not text baked into the skill. Changing positions therefore never requires re-uploading a plugin. This is the Cowork-compatible replacement for claude-for-legal's profile file, which lives in a Claude-only config path that doesn't exist in Cowork `[Cited]`.

**Where the overlay lives:**

| Level | Location | Who maintains it |
|---|---|---|
| T1 practice overlay | `<Practice Library>/_Overlay/<practice>/` in SharePoint | Practice lead |
| T0 personal overlay | A fixed OneDrive path, e.g. `Documents/Spaarke/<practice>/` (exact path set by spike S2) | The attorney |
| Baseline positions | `references/baseline-positions.md` inside each skill | Spaarke |

**Overlay files:**
- `positions.md`: per issue, the standard position, acceptable fallbacks and walk-away point, with a side flag (e.g., discloser, recipient).
- `house-style.md`
- `escalation.md`: who to escalate to and when.
- `overlay.yaml`: version, owner, date, scope.

**Resolution order:** T1 overlay → T0 overlay → baseline positions. A pointer file `Documents/Spaarke/config.md` tells a Cowork plugin skill where the T1 library is.

**Provenance rule:** every substantive output begins with one line naming the positions used, for example *"Positions: Acme Legal v3 (2026-11-03, T1 library)"* or *"Positions: Spaarke baseline (not tailored)"*.

**Fallback if run-time reads prove unreliable** (S2 fails): the setup interview produces an updated skill `.zip` with the overlay embedded, and the attorney re-uploads it. This is clumsier, but it works with documented features only.

### 5.3 Surfaces and what each one receives

| Surface | Receives | Who installs | Limits that bind the design `[Cited]` |
|---|---|---|---|
| **Cowork, user plugin** | The practice plugin `.zip` (skills + companions) | Attorney | ≤ 20 skills per package; ≤ 20 companion files, 5 MB each, 10 MB per skill; SKILL.md ≤ 1 MB; ≤ 50 user skills in total; no mobile; blocked under Information Barriers |
| **Legal plugin in Word** | `.docx` playbooks | Attorney (attaches per review) | `.docx` only; topics → rules → example clauses; Word desktop only; no persistence documented |
| **SharePoint site skills** | Copies of selected skills in `/Agent Assets/Skills/` | Practice lead (Edit permission) | No external systems or custom code |
| **Agent Builder agent** | Instructions + ≤ 8 skills + library knowledge | Practice lead | Instructions < 20,000 characters; ≤ 100 SharePoint files; sharing to users, groups or link |

### 5.4 Source format, build and validation

**Source layout** (location: decision OD-4; proposed `src/accelerators/` in this repo for R1):

```
src/accelerators/
├── README.md
├── core/
│   ├── skills/<name>/SKILL.md + references/
│   └── shared-references/          overlay-protocol.md · verification.md · disclaimer.md
│                                   (copied into every skill at build time; skills may
│                                   reference only files inside their own folder)
├── practices/<practice>/
│   ├── accelerator.yaml            id, version, wave, skills, playbooks, library, sources
│   ├── skills/<name>/SKILL.md + references/
│   ├── positions/baseline.yaml     Spaarke baseline positions (attorney-reviewed)
│   ├── playbooks/*.yaml            rendered to .docx
│   ├── library/                    T1 folder-tree content
│   ├── evals/                      cases, rubric, corpus manifest
│   └── THIRD-PARTY-NOTICES.md
├── third_party/<source>@<sha>/     pinned copies of upstream files we adapt, with LICENSE/NOTICE
└── build/                          Build-Accelerator.ps1 and helpers
```

**Build outputs per accelerator release** (the T0/T1 part of strategy §6.5):
- `t0/<practice>-skills.zip`: the Cowork plugin.
- `t0/playbooks/*.docx`
- `t0/prompt-pack.docx`
- `t1/practice-library.zip`
- `t1/site-skills/`
- `t1/agent-builder-setup.md`
- guides
- `evals/`

**Validation gates** (enforcement ladder: these are build-time checks that also run in CI, not prose):
1. `wiqd plugin validate --mode deep` (Microsoft).
2. `skills-ref validate` (Agent Skills spec). It is Python-based: use a real Python install, never the Windows Store stub.
3. **Spaarke accelerator lint.** Fails on any of:
   - folder name ≠ skill `name`
   - description > 1,024 characters or missing trigger phrases
   - Claude-only frontmatter (`user-invocable`, `argument-hint`, `model`, `allowed-tools`)
   - `~/.claude` paths
   - "claude", "anthropic", "mike" or "lavern" in skill or package names
   - companion-file limits exceeded, or hidden files present
   - provenance or disclaimer block missing
   - a skill name or primary trigger that collides with one of the 20 Legal for Cowork skills
   - an adapted file with no attribution to its pinned upstream

   Each rule ships with a must-fire and a must-not-fire fixture.

**T0 plugin format** (decision OD-7, after spike S1): ship the **Claude-format plugin** if Cowork converts it cleanly, so the same file also runs in Claude. Otherwise ship the M365 format built with `atk`.

### 5.5 Skill design rules

1. **Complement Legal for Cowork, never duplicate it.** Microsoft's own guidance says "Don't duplicate built-in skills". Our skills are named and described around *our* differentiators: positions, side-of-deal logic, practice checklists and triage. For example, "Triage an NDA against the organization's positions", not "Review a contract".
2. **Trigger-specific descriptions:** third person, naming the document type, the side and the output. Spike S9 tests that Cowork picks our skill over the built-in one.
3. **Model-agnostic.** EU Data Boundary tenants get GPT models only, and Cowork's default model is "Auto". Evaluate on GPT and Claude.
4. **No authoritative arithmetic or date math by the model.** Dates and amounts are extracted as stated in the document. Computed values (deadlines, totals, notice windows) either go into **Excel formulas** in an output workbook (spike S8) or are marked `[compute — verify]`. Deterministic tools are T2.
5. **Verification discipline** (adapted from claude-for-legal): legal authority is cited only from the document or the shipped reference files; anything else is tagged `[verify]`. No case-law citations from model memory.
6. **Human review stated in every output**, with a disclaimer. Outputs are drafts for attorney review, not legal advice.
7. **Size discipline:** SKILL.md under 500 lines; detail goes in `references/`, one level deep.
8. **No reliance on things Cowork drops or lacks:** `commands/`, `agents/`, `hooks/`, plugin-level `CLAUDE.md`, local or encrypted files, deletes, mobile.

### 5.6 Word playbooks

Microsoft publishes no playbook schema. The only documented rules are "`.docx`", "topics" and "rules and example clauses" `[Cited]`. So we don't invent a format. Playbooks are authored as YAML (topic → rules → example clauses → fallbacks), rendered to a plain, consistently styled `.docx`, and tuned empirically in spike S4 to see which structure the legal plugin in Word parses best.

Rendering reuses the repo's existing OpenXML tooling where practical. The setup interview can also produce a customized playbook `.docx` directly from Cowork (spike S4b).

### 5.7 T1 practice library

The template folder tree is uploaded by drag-and-drop. Site owners can't create SharePoint templates without an admin `[Cited]`.

```
<Practice> Library/
├── Playbooks/            the Word .docx playbooks (attach from here in Word)
├── Precedents/           customer's approved forms (customer adds)
├── Clause Library/
├── Checklists/
├── _Overlay/<practice>/  positions.md · house-style.md · escalation.md · overlay.yaml
└── Agent Assets/Skills/  site skills (copied from t1/site-skills/)
```

The Agent Builder agent is grounded on this library (≤ 100 files). The setup guide says which folders to index.

---

## 6. Practice accelerators

### 6.1 Core (Wave 0)

| Skill | Purpose |
|---|---|
| `accelerator-setup` | The setup interview, per practice: positions by issue and side, fallbacks, escalation, house style. Writes the overlay files and a customized Word playbook. |
| `accelerator-help` | What is installed, which skills do what, which positions are active, how to update, the disclaimer |
| Shared references | `overlay-protocol.md` (find and read the overlay, resolution order, provenance line), `verification.md`, `disclaimer.md` |

### 6.2 The five practice accelerators

| | **NDA** | **Employment** | **Commercial Real Estate** | **Invoice Review** | **U.S. Patent Applications** |
|---|---|---|---|---|---|
| **Wave** | 1 (reference implementation) | 1 | 1 | 2 | 2 |
| **Skills (proposed)** | `nda-triage` (GREEN/YELLOW/RED by positions and side); `nda-review-against-positions` (issues table + redline instructions); `nda-scope-traps` (hidden non-solicit or non-compete, residuals, DTSA whistleblower notice) | `employment-agreement-review` (employer or employee side); `restrictive-covenant-check` (against a state reference file); `separation-agreement-review` (incl. OWBPA timing for 40+ releases); `worker-classification-screen` | `lease-review` (tenant or landlord side; office/retail/industrial variants); `lease-abstract` (Excel abstract, critical dates flagged); `estoppel-snda-review`; `lease-amendment-compare` | `billing-guidelines-to-rules` (outside-counsel guidelines → rules file); `invoice-compliance-review` (LEDES 1998B or PDF: block billing, vague entries, rates, staffing, expenses, UTBMS codes) → Excel exception report; `reduction-letter-draft` | `application-qc` (claims antecedent basis, dependency and numbering, spec support for claim terms, reference numerals, §112(b) watch-terms, abstract); `invention-disclosure-intake` (§101/102/103 screens, bar-date flags); `office-action-outline` (rejections → response outline); `ids-ads-checklist` |
| **Word playbooks** | Mutual; one-way (discloser); one-way (recipient) | Executive agreement; offer letter; separation agreement | Office lease (tenant); office lease (landlord); retail lease (tenant) | — (guideline rules file instead) | — (QC checklist instead) |
| **Seed content** | mike `nda-review` + `nda-tabular-review` (MIT); claude-for-legal `commercial-legal/skills/nda-review` (Apache, method); Lavern NDA templates (Apache) | mike `employment-agreement-review` + tabular (MIT); claude-for-legal `employment-legal` hiring/termination/worker-classification (Apache) | mike `commercial-lease-review` + tabular (MIT; **UK law, rewrite for U.S.**); Lavern `real-estate-counsel.ts` checklist lines (Apache) | **None**: author from scratch | claude-for-legal `ip-legal/invention-intake` (Apache, inspiration only); otherwise author |
| **We must author** | Baseline positions for each side; firm and in-house variants | State restrictive-covenant and notice reference files, dated and cited, with a refresh process | Full U.S. checklists (CAM/opex and gross-up, SNDA, estoppel, percentage rent, co-tenancy, exclusives, TI, holdover); abstract columns | Everything; check the UTBMS/LEDES code-set licence first | Nearly everything; the in-house patent expertise is the asset |
| **Calculation risk** | Low | OWBPA periods → `[compute — verify]` | Critical dates, rent steps → Excel formulas (S8) | Totals, rate × hours → Excel formulas (S8), else T2 | Bar dates, deadlines → `[compute — verify]` |
| **Firm / department** | Both: high volume, ideal demo | Both | Firm real-estate practice; department lease portfolio | Department spend review; firm pre-bill review | Firm prosecution practice; department IP counsel |
| **Effort** | S | M | L | L | L |

`[Judgment]` on the set:
- **Keep all five**, in waves.
- **NDA** goes first as the reference implementation. It proves the overlay model and the triage pattern cheaply, even though Microsoft's built-in `review-contract` covers generic NDA review; our value there is positions and triage.
- **Invoice Review** is the most differentiated: no open-source seed, and not covered by Microsoft's legal skills.
- **U.S. Patent Applications** has no seed and a specialist audience. It plays to Spaarke's own IP expertise, and its QC checks are well suited to skills.
- **Alternate if one drops:** Privacy/DPA, which has the richest reusable seeds (claude-for-legal `privacy-legal/dpa-review`; mike `dpa-review`, `dpa-draft`, `eu-gdpr-pack`).
- **Rejected:** MSA review and litigation chronology. Both duplicate Legal for Cowork skills.

### 6.3 Content governance

- **Attorney sign-off gate:** no baseline positions, state reference file or checklist ships until a named attorney approves it and the approval is recorded (decision OD-5).
- **Dated reference files:** every legal reference file (e.g., state non-compete rules) carries an as-of date and sources, and joins a refresh calendar.
- **Licensing:**
  - **MIT:** keep "Copyright (c) 2026 Mike" plus each skill's `author`.
  - **Apache-2.0:** include the licence and carry NOTICE text forward.
  - **Mark modified files**, and put a `references/THIRD-PARTY-NOTICES.md` in each derived skill.
  - **No upstream trademarks in names.**
  - **Datasets:** CUAD is CC BY 4.0 with attribution; exclude ContractNLI (non-commercial); avoid CC BY-SA unless we accept ShareAlike.

---

## 7. Claude Code knowledge and tooling (Workstream K, first)

Cowork plugins, Agent Skills in Microsoft surfaces, SharePoint site skills and Copilot for legal all shipped in 2026. Claude Code will otherwise build from stale or confused knowledge. For example, it may treat Microsoft's Cowork as Anthropic's "Claude Cowork", or add Teams manifest fields that the v1.28 schema rejects. Workstream K follows `knowledge/README.md` conventions (SOURCE.md with pinned SHAs and dates, NOTES.md, curated samples, `docs/` snapshots). It runs **before** any accelerator authoring.

| # | Deliverable | Content |
|---|---|---|
| K1 | `knowledge/cowork-plugins/` | docs: cowork-plugin-development, cowork-customize, cowork-plugins, cowork-manage-plugins, mcp-apps-support, cowork-elicitation-forms, cowork-faq. Samples: `microsoft/copilot-camp` @ `b5b1f8a1` `src/cowork/` (CopilotDevCamp-for-cowork, zava-claims-sso, weekly-status-mail). wiqd `build-a-plugin` and `plugin-reference` @ `8bd50d04` |
| K2 | `knowledge/agent-skills-spec/` | agentskills.io specification; Anthropic skill-authoring best practices; `skills-ref` README @ `69ef37e9`; `cat-agent-skills/_template` @ `3678d23a` |
| K3 | `knowledge/copilot-for-legal/` | docs: admin page, support get-started, transparency note, blog and MC1509799 extracts. NOTES: the 20 Legal for Cowork skills, the terminology (§2), the Word playbook facts. Samples: `cat-agent-skills` `vendor-contract-risk-review`, `redlining-content`; claude-for-legal `commercial-legal/skills/nda-review` @ `4a6c651` (as a conversion case) |
| K4 | `knowledge/sharepoint-skills/` + extend `knowledge/declarative-agents/` and `knowledge/mcp-apps/` | Site skills; `.agent` retirement; Agent Builder skills and knowledge limits; MCP Apps in Cowork |
| K5 | `.claude/skills/cowork-plugin/` | Authoring and packaging rules table, limits, manifest templates (v1.28 strict / v1.29), connector rules, the validation commands, the stale-knowledge **pitfall list** (references.md §2 and the 2026-10-29 researcher notes) |
| K6 | `.claude/skills/practice-accelerator/` | Composition per tier; overlay protocol; overlap check against the 20 Legal for Cowork skills; Word playbook outline (no invented schema); evaluation requirements; licence and attribution checklist; tier gates (Information Barriers, mobile, EU models) |
| K7 | Registration | `.claude/skills/INDEX.md`; a CLAUDE.md §17 trigger row ("Before you build a Cowork plugin or practice accelerator → `cowork-plugin`, `practice-accelerator`"); knowledge README topics table; `REFRESH-LOG.md` entry; refresh monthly and after Ignite (17–20 Nov 2026) |
| K8 | `src/accelerators/third_party/` | Pinned upstream copies of the files we adapt (claude-for-legal @ `4a6c651`, mike-workflows @ `20bed20`, Lavern @ `7c2efe6`) with LICENSE and NOTICE |

K5–K7 write under `.claude/`, which sub-agents can't do (CLAUDE.md §3). The main session applies those edits.

---

## 8. Evaluation

- **Method:**
  - **Lavern's pre-registered rubric** (`evals/jv/RUBRIC.md`): the expected issues are written down *before* the run, then scored for recall, precision and hallucination count.
  - **Anthropic's eval-first practice:** ≥ 3 cases per skill, including at least one negative case (a clean document, or the wrong document type), each run **with and without** the skill.
- **Corpus:**
  - Public documents only in R1: SEC EDGAR exhibits (leases, employment agreements, NDAs) and CUAD (CC BY 4.0) for contracts.
  - USPTO published applications and office actions (public domain) for patents.
  - Synthetic LEDES 1998B invoices with planted violations for Invoice Review.
  - Customer documents enter only during an enablement sprint, inside the customer's tenant.
- **Two layers:**
  1. **Offline harness.** Runs each SKILL.md against the corpus on GPT (Azure OpenAI) and Claude, scored automatically, reusing `infrastructure/ai-foundry/evaluation/` (citation-accuracy and completeness metrics). Cowork has no evaluation API, so this is our measurement of content quality.
  2. **Cowork acceptance runs.** Scripted runs in the test tenant, recorded, which prove the skill behaves the same on the real surface.
- **Pass bar** (per accelerator; proposed):
  - recall ≥ 0.85 on rubric issues
  - zero fabricated legal authorities
  - with-skill recall above without-skill, with no increase in hallucinations
  - provenance line present in 100% of outputs
- **Placement:** evals live with the accelerator (`practices/<p>/evals/`), not under `tests/**`. They measure content, not code (ADR-038 tension note, §11).

---

## 9. Verification spikes (test tenant)

**Prerequisite:** a test M365 tenant with ≥ 2 M365 Copilot licences, a Cowork Copilot Credits spending policy, Word desktop for Windows, a SharePoint site, and no Information Barriers (decision OD-3).

| # | Question | Decides |
|---|---|---|
| S1 | Does a Claude-format plugin (mike `nda-review`; claude-for-legal `nda-review`) upload and convert cleanly? What happens to frontmatter, companions and descriptions? | OD-7 format; lint rules |
| S2 | Can a skill reliably read overlay files from a fixed OneDrive path and from a SharePoint library at run time? Exact path of the OneDrive Cowork folder? | §5.2 overlay model or its fallback |
| S3 | Sharing with "specific users": do recipients get updates on re-share? Does a recipient's own overlay apply? | UC-1, UC-5 |
| S4 | Which playbook `.docx` structure does the legal plugin in Word parse best (headings, tables, numbered rules)? Are edits or rules kept between reviews? (S4b) Can Cowork generate a usable playbook `.docx`? | §5.6 |
| S5 | SharePoint site skills: placement, invocation, reading library files; do they consume Copilot Credits? | UC-4 |
| S6 | Agent Builder agent with ≤ 8 uploaded skills and library knowledge: does it run our skills as authored? | UC-4 |
| S7 | Effect of "Allow agents and plugins built by your organization = off" on user uploads; behaviour under Information Barriers | T0 claims; IT one-pager |
| S8 | Can a skill make Cowork produce an Excel workbook whose totals and dates are **formulas**, not model-computed values? | Calculation rule §5.5-4 for CRE and Invoice Review |
| S9 | With Legal for Cowork installed, does Cowork select our skill for its trigger phrases? | §5.5-1/2 |
| S10 | Same skill on GPT-only (EU Data Boundary) vs Auto: quality parity? | §5.5-3 |
| S11 | Do Cowork plugin skills also appear in Copilot Chat? | Surface claims |

Results go in `notes/spikes.md`, each with its design consequence. A failed spike changes the design before authoring scales, which is why the spikes sit in Phase 0.

---

## 10. Constraints

- **Platform churn:** everything in scope changed in the last six months and keeps changing monthly; much of the tooling is preview (`wiqd` plugin commands are alpha). Pin tool versions per release; re-verify after Ignite.
- **No admin at T0/T1** means no connectors, no tools, no interactive UI, no tenant-wide install. Anything that needs those is T2 by definition.
- **Information Barriers** tenants can't use T0/T1 at all; the IT one-pager says so.
- **Cost belongs to the customer:** Cowork runs on their Copilot Credits (minimum policy 2,000 credits per user per month).
- **Content is legal content:** attorney sign-off, dated references, disclaimers, U.S. only in R1.
- **No customer data leaves the customer's tenant in R1.** Spaarke hosts nothing for T0/T1.

---

## 11. Component justification and architecture tensions

**New surfaces (CLAUDE.md §11):**

| Component | Existing overlap | Why not extend | Cost of doing nothing |
|---|---|---|---|
| `src/accelerators/` | JPS Actions / playbooks in Dataverse (platform runtime; different format and runtime) | Accelerators run in Microsoft's runtime from files; JPS needs the BFF. The T3 bridge maps one to the other later | No source of truth for skills, playbooks and library content; three hand-maintained copies drift |
| `knowledge/` topics K1–K4 | `knowledge/m365-copilot/`, `declarative-agents/`, `mcp-apps/` (extended where they fit, K4) | Cowork plugins, Agent Skills and Copilot for legal are new topics with no home | Claude Code builds against stale knowledge; the pitfall list shows the concrete failures |
| `.claude/skills/cowork-plugin`, `practice-accelerator` | `declarative-agent` skill (declarative agent manifests, not Cowork plugins or skills) | Different package format, limits and validation | Every authoring task re-derives the rules; lint-able rules stay prose |
| Accelerator lint (build + CI) | None | — | Packaging rules violated silently; Cowork rejects or mis-handles uploads in front of customers |

**Architecture tensions:**
- **BFF hygiene (§10) and hot-path declaration:** not applicable. R1 touches neither `Sprk.Bff.Api` nor `src/solutions/SpaarkeAi/**`.
- **ADR-038 (testing):** the evaluations are content evaluations, not code tests. Proposed placement outside `tests/**` (Path C, comply: no conflict if ADR-038 governs code tests only). Confirm at design-to-spec.
- **ADR-039 (agentic placement):** all R1 skills produce human-reviewed drafts (review, triage, abstraction). Nothing writes data or decides silently, so this is compliant.
- **Agent-cost constraint:** offline evaluation runs are metered model calls. Budget them per accelerator and record the spend.

---

## 12. Phasing (to be refined by `/project-pipeline`)

| Phase | Content | Exit |
|---|---|---|
| **0. Knowledge and spikes** | Workstream K (K1–K8); test tenant; spikes S1–S11 | Knowledge and skills registered; `notes/spikes.md` complete; design updated with spike consequences |
| **1. Foundation** | Source layout; build pipeline + validation gates (with must-fire fixtures); Core skills; overlay protocol; Word playbook renderer; T1 library template; evaluation harness | One-command build produces a valid empty-practice release |
| **2. Wave 1** | NDA (reference implementation) → Employment → CRE; attorney sign-off; evaluations; T0/T1 guides | Each passes §13 criteria SC-4 to SC-8 |
| **3. Wave 2** | Invoice Review; U.S. Patent Applications | Same criteria |
| **4. Pilot readiness** | IT one-pager; sprint runbook; dry-run sprint in the test tenant with a practice lead acting as the customer | Dry-run sprint completed in ≤ 5 days with a timed before/after |

---

## 13. Success criteria (closed set for spec authoring)

| ID | Criterion |
|---|---|
| SC-1 | Knowledge folders K1–K4 exist with SOURCE.md (pinned SHAs and dates) and NOTES.md; skills K5–K6 exist and are registered (K7); third-party pins K8 carry their LICENSE/NOTICE |
| SC-2 | `notes/spikes.md` records S1–S11 with result, evidence and design consequence |
| SC-3 | One command builds every T0/T1 artifact for an accelerator from source. All three validation gates pass on the release and **fail** on each seeded violation fixture |
| SC-4 | For each accelerator: the plugin `.zip` uploads to Cowork; its skills run; a second user receives them by sharing |
| SC-5 | For each accelerator with playbooks: a playbook review in the legal plugin in Word produces tracked changes on a corpus document |
| SC-6 | T1: library template, site skills and Agent Builder agent work in the test tenant; the T1 overlay overrides a T0 overlay |
| SC-7 | Overlay: changing a position changes the review outcome for that issue; every output carries the provenance line; with no overlay, outputs say "Spaarke baseline (not tailored)" |
| SC-8 | Evaluation: each skill has ≥ 3 cases including ≥ 1 negative; each accelerator meets the §8 pass bar on GPT and Claude; Cowork acceptance runs recorded |
| SC-9 | No skill name or primary trigger collides with a Legal for Cowork skill (lint), and S9 shows our skills are selected for their triggers |
| SC-10 | Every adapted file traces to a pinned upstream with licence and notice; each derived skill has THIRD-PARTY-NOTICES; no upstream trademarks in names |
| SC-11 | Every baseline position set and legal reference file has a recorded attorney approval and an as-of date |
| SC-12 | Attorney install guide, practice-team setup guide, IT one-pager and T0/T1 sprint runbook exist and were used in the Phase 4 dry run |
| SC-13 | **Negative:** no R1 change touches `src/server/**`, Dataverse schema, `src/solutions/SpaarkeAi/**` or the Office add-ins |

---

## 14. Owner decisions

| # | Decision | Recommendation |
|---|---|---|
| OD-1 | Naming | "Spaarke Solution Accelerator" (program) and "Practice Accelerator" (per practice), replacing "practice pack" and "enablement kit" |
| OD-2 | Practice set and waves | Wave 1: NDA, Employment, CRE. Wave 2: Invoice Review, U.S. Patent Applications. Privacy/DPA as the alternate |
| OD-3 | Test tenant and credit budget | A dedicated demo tenant with ≥ 2 Copilot licences and a capped Cowork spending policy, not the Spaarke dev tenant |
| OD-4 | Source location | `src/accelerators/` in this repo for R1 (knowledge, skills and CI apply); revisit a separate repo at first commercial release |
| OD-5 | Legal sign-off | Name the approving attorney(s) per practice; patents in-house |
| OD-6 | Jurisdiction | United States only in R1; state-specific references for Employment |
| OD-7 | T0 plugin format | Claude-format plugin if S1 passes (runs in Cowork and Claude); otherwise the M365 package |
| OD-8 | Getting the T0 `.zip` to attorneys | Manual delivery in R1 (sprint or email); a download portal is a later project |
| OD-9 | Evaluation models | GPT on Azure OpenAI (matches EU-boundary tenants) and Claude |
| OD-10 | Project setup | Register with `/devops-project-start` and give R1 its own worktree (`spaarke-wt-spaarke-solution-accelerator-r1`), then `/design-to-spec` |

## 15. Next steps

1. Owner review of this design and the decisions in §14.
2. Register the project and create its worktree (OD-10).
3. `/design-to-spec` → `/project-pipeline`. Phase 0 tasks (Workstream K and the spikes) go first and can begin as soon as the tenant is available.
