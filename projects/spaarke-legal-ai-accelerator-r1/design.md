# Spaarke Solution Accelerator R1: Practice Accelerators for Copilot for legal and Cowork (T0/T1 foundation)

> **Status:** Draft v0.5 for owner review → `/design-to-spec` after the decisions in §18 are made.
> **Date:** 2026-11-05 (v0.4 2026-10-31; v0.3 earlier that day; v0.2 2026-10-30; v0.1 2026-10-29)
> **Author:** Ralph Schroeder + Claude Code
> **Companion documents:**
> - [solution-strategy.md](solution-strategy.md): the program vision
> - [references.md](references.md): curated reference set
> - [notes/poc-2026-10-30-cowork-nda-review.md](notes/poc-2026-10-30-cowork-nda-review.md): the NDA proof of concept run in Cowork
> - [notes/mvp-nda/results.md](notes/mvp-nda/results.md) and [run-log.md](notes/mvp-nda/run-log.md): the NDA MVP tenant tests (2026-11-01/02). Findings are cited as L1–L27 and S1–S19.
> - [notes/component-protocol.md](notes/component-protocol.md): the component protocol (v0.2), summarized in §5.10
> - [notes/2026-11-05-external-inputs-review.md](notes/2026-11-05-external-inputs-review.md): the expert redliner demo and the 2026-11-04 discussion Q&A, checked against our evidence
>
> **Tags:**
> - `[Cited]`: Microsoft documentation, checked 2026-10-29/30
> - `[Observed]`: seen in the 2026-10-30 POC
> - `[Tested]`: verified in our tenant during the NDA MVP, 2026-11-01/02 (results L#/S#)
> - `[Judgment]`: our assessment
> - `[Open]`: owner decision
> - `[Verify]`: must be proven in the test tenant (§12)
> - `[Proposed]`: a v0.5 change awaiting owner approval (§18)

**Changes in v0.5** (2026-11-05; from the NDA MVP tenant tests, component protocol v0.2, the owner's 2026-11-02 decisions, the expert redliner demo and the 2026-11-04 discussion Q&A):
1. **Tenant evidence folded in.** Spikes S1, S2, S3, S9, S10, S13, S17 and S19 are answered, S5 in part. §12 now has a status column, and stale claims are corrected throughout.
2. **Python scripts in Cowork are verified, not assumed.** Our engines ran on the current service from an uploaded plugin and from a OneDrive skill folder (Python 3.12, lxml, python-docx, LibreOffice). Microsoft documents that scripts are executed but not the runtime, so every engine skill still runs `probe.py` first (§5.2, §5.6).
3. **Word corrected:**
   - Our plugin is *listed* in Word but never *runs* there: Word has no loader for third-party skills (L21, L25).
   - Word's Legal plugin imports our Word playbook into a saved, editable playbook object (L24). That import drops the Topic ID and the fallback position, so the renderer was reshaped.
   - The declarative-agent option is removed from R1 (owner, 2026-11-02).
   - Changed sections: §2.1, §2.4, §5.4, OD-18.
4. **New §5.10: component protocol.** One job per primitive (positions in playbooks, procedures in skills, defaults in the profile); non-overlap rules with Microsoft Legal; governed and personal playbooks. `[Proposed]`: keep one Cowork plugin and defer the Knowledge/Engines split until a Word probe shows it is needed (OD-20).
5. **Playbook locations replace the old lookup order** (§5.3). Governed playbooks sit in a read-only SharePoint library (T-P10 to prove); personal ones in OneDrive. This also fixes L16.
6. **New skill rules from the MVP defects** (§5.7):
   - never reuse plans or outputs from other tasks (L15);
   - read playbooks only from designated locations (L16);
   - keep outputs in the task folder (L10, L17);
   - one-line descriptions (L22);
   - use approved model language verbatim.
7. **Ideas taken from the expert demo** (§5.7, §8.2): a plan-preview card before files are generated, an optional Excel audit pack, and verbatim insertion of approved clause wording.
8. **`[Proposed]` R1 scope narrowed to Core + NDA** (OD-21). Legal Intake and Invoice Review move to R2, and the six-accelerator roadmap stays.
9. **Out of scope, made explicit:** declarative agents, Copilot Tuning, an Office add-in for redlining, App Store / partner listing, and the MCP connector (v2).

**Changes in v0.4** (owner review round 4, 2026-10-31, from the owner's tenant screenshots; two new researcher notes):
1. **Word corrected.**
   - Word's Copilot pane has **Manage plugins & skills**; the Legal plugin shows three skill toggles that share Legal for Cowork's names.
   - It has a **GA model picker**.
   - `/` picks files and `@` calls skills.
   - The "not extensible" claim is replaced by what is documented, what isn't, and what to test (§2.1, §2.4.1–2.4.2).
2. **New §2.4.6:** how a playbook is activated on each surface. Selection can be automated only in Cowork.
3. **New §2.4.7:** ways to get Spaarke into Word: our plugin in Word (untested), a declarative agent (documented, chat only), and why an add-in can't drive Copilot today.
4. **New §2.4.8:** references, scripts and assets serve Cowork; Word gets rendered content.
5. **New §5.2a:** the definitive list of built-in skills.
6. **New §5.8a:** specialist reviewers. A script can't call an agent; a panel inside one skill is used now, and Foundry specialists come through the connector at T2.
7. **Practice set decided:**
   - Wave 1: NDA, **Legal Intake** (an email to a designated mailbox), Invoice Review.
   - Wave 2: CRE, Employment.
   - Wave 3: Patents (§8.3).
8. **New spikes S16–S19; OD-18 and OD-19.**

**Changes in v0.3** (owner review round 3, 2026-10-31; three new researcher notes, 2026-10-31):
1. **New §2.4: the two Microsoft legal plugins.** Sets out the legal plugin in Word and Legal for Cowork side by side: what each takes and produces, how they work separately and together, where they overlap, and which flow belongs where. They share expertise but no object: there is no shared playbook, schema or hand-off. Our content model (§6) is the shared layer.
2. **§5.6 rewritten: why the engines exist.** Covers their origin (the POC), the precedent (Anthropic's document skills, Agent Skills guidance) and the evidence (date and arithmetic benchmarks). An engine-by-engine verdict states the Microsoft built-in alternative for each. `ooxml_redline.py` is **repositioned**: a single-document review in Word goes to Microsoft's plugin. New: `probe.py`, the untracked-edit check, and onboarding tools.
3. **New §8.5: where skill content comes from.** A per-practice source register (licence, what we take, what attorneys must write), from web, GitHub and Hugging Face research.
4. **§7.2 and §8.3 reframed: existing Spaarke content is an *untested candidate*, not the seed.** It is compared against researched content on the same eval corpus, and the findings go to the Spaarke backlog (§7.3).
5. **New §8.1a: onboarding.** The `accelerator-setup` skill and its tools take a new user through:
   1. an environment check;
   2. choosing and installing skills;
   3. importing or building their legal playbook;
   4. personalization;
   5. a trial run;
   6. refinement;
   7. automations.

   It can be re-run at any point.
6. **Corrections:**
   - Cowork memory is conversation-scoped;
   - there is no `copilot-instructions.md` (custom instructions are 20 KB, set in Preferences);
   - Projects are private;
   - the legal plugin in Word is Windows desktop only;
   - Legal for Cowork `edit-contract` produces native tracked changes;
   - Anthropic's document skills are proprietary (patterns only).
7. **New spikes S13–S15**; S3 and S6 extended.

**Changes in v0.2:**
1. **The POC is folded in.** It is the NDA reference implementation (§8.2). Core Python engines run at T0 (§5.6). The Markdown legal playbook format becomes the master format (§5.3).
2. **New §6: one content model across three surfaces.** The Spaarke platform, the legal plugin in Word and Cowork stay aligned through:
   - shared topic IDs;
   - a shared review-result schema;
   - shared version stamps;
   - one master copy per customer.
3. **New §7: what this means for Spaarke AI and JPS.**
4. **New §9: forms and interactive UI in Cowork**, with Legal Front Door intake as the worked case.
5. **New §10: Spaarke connection.** It goes through a BFF-hosted MCP endpoint: no separate API, and no direct Dataverse writes.
6. **Research corrections:**
   - installing by copying a folder into OneDrive is supported;
   - the user skill cap is 50, not 20;
   - SharePoint site skills don't load in Cowork, so they leave the T1 design;
   - `allowed-tools` is a valid Agent Skills key;
   - Microsoft ships no default playbooks.
7. **Spikes rewritten** (§12).

---

## 1. Why this project exists

Microsoft shipped **Copilot for legal** on 2026-10-15. It has two parts:
- a legal plugin in Word;
- a 20-skill **Legal for Cowork** plugin.

Both come with the Copilot seats most law firms and legal departments already own (strategy §3). The skills are generic, though. They carry no practice content and no knowledge of an organization's own positions, and Microsoft ships no default playbooks `[Cited]`. The legal plugin in Word takes a playbook only as a `.docx` attached to each review.

Attorneys who want practice-grade AI today reach for Claude with legal plugins. **This project lets them get that inside Microsoft instead**, under their own tenant's identity and protections, with no IT needed at the entry level. It does this with **practice accelerators**: practice-specific skills, deterministic document engines, Word playbooks and a team library, installed by an attorney (T0) or a practice team (T1).

The 2026-10-30 POC showed the approach works end to end, inside the user's tenant `[Observed]`:
- A Cowork skill reviewed a real NDA.
- Python scripts produced a Word redline with surgical tracked changes, margin comments, a linked summary table, and a clean version.
- The results were emailed.

The 2026-11-01/02 NDA MVP repeated this on the current service with a packaged, tested build: 11 Cowork runs on three models, every output checked by script `[Tested]`.

R1 builds the **foundation** and the **NDA accelerator** `[Proposed]` (OD-21; v0.4 had six accelerators in three waves, which stay as the roadmap in §8.3). It also defines how their content stays aligned with the Spaarke platform's own AI content: JPS Actions, Skills and Knowledge. That way the IT tier (T2) and the platform tier (T3) build on the same content, not a copy of it.

---

## 2. Terminology

### 2.1 Microsoft's products (strategy §3)

| Term | Meaning |
|---|---|
| **Copilot for legal** | Umbrella name for **two** legal plugins. Not a synonym for Word. |
| **The legal plugin in Word** | Admin policy name: "Legal Skills in M365 Copilot in Word". The user turns it on in the Copilot pane in **Word for Windows desktop** (`…` › **Manage plugins & skills** › Legal). It shows three skill toggles with the same names as three Legal for Cowork skills: *Legal reasoning*, *Edit contract*, *Draft new contract*. In practice: "Review contract" against a playbook (native tracked changes) and `@Draft-new-contract` from a precedent (content-control placeholders).<br>**Tested:**<ul><li>It is present in Word desktop and absent in Word for the web (S5).</li><li>It imports our Word playbook into a saved, editable playbook object, with one topic per Name / Rules / Examples (L24).</li></ul>**What we can add to it:** a `.docx` Word playbook. Our own plugin is **listed** in Word's plugin menu but **never runs** (L21, L25; §2.4.7). |
| **Legal for Cowork** | Microsoft's built-in plugin of 20 legal skills, running inside Cowork. The tenant showed `legal-for-cowork` v1.0.10 at the POC `[Observed]`; no ID or version is documented. MC1509799 calls it "Cowork for Legal". Read-only to us. |
| **Copilot Cowork ("Cowork")** | Copilot's general-purpose multi-step agent mode. **Our platform at T0/T1**: it takes our skills and plugins, and runs our scripts. |
| **Agent Skill** | The open skill format Cowork uses ([agentskills.io](https://agentskills.io/home)): a folder holding `SKILL.md` plus optional `references/`, `scripts/` and `assets/`. |

### 2.2 Our terms (proposed; decision OD-1)

| Term | Meaning |
|---|---|
| **Spaarke Solution Accelerator** | The program and product family. |
| **Practice accelerator** | One practice area's package: Agent Skills, engines, legal playbooks, library content and evaluations (e.g., "NDA Accelerator"). Replaces "practice pack" and "enablement kit". |
| **Core** | Skills, shared references and engines used by every practice accelerator. |
| **Legal playbook** | An organization's negotiating positions for one agreement type and side, by topic: preferred, fallback, escalate, model language. Microsoft calls this a "playbook". |
| **Attorney profile** | One attorney's saved defaults: usual side, posture, governing law, output and email preferences. |
| **Baseline positions** | Spaarke's attorney-reviewed default legal playbook, used until a customer supplies its own. |
| **Topic ID** | The stable identifier of one review topic (e.g., `nda.term`). It is the key that joins every surface (§6). |

### 2.3 Two words to qualify in every project document `[Judgment]`

**"Playbook"** has three meanings. Never write it bare:

| Write | Meaning |
|---|---|
| "legal playbook" | Positions, as Microsoft uses the word |
| "Spaarke AI playbook" | A `sprk_analysisplaybook` workflow graph |
| "Word playbook" | The `.docx` rendering of a legal playbook |

**"Skill"** has two meanings:

| Write | Meaning |
|---|---|
| "Agent Skill" | The folder format |
| "Spaarke Skill scope" | `sprk_analysisskill`, a prompt fragment |

Renaming the Spaarke scope would be a terminology change. Those originate only in the ontology component model (CLAUDE.md §1.1), so this design proposes it there rather than making it (§7.2).

### 2.4 The two Microsoft legal plugins: boundaries, overlaps and how they work together

Source: researcher note `word-vs-cowork-legal-boundary-and-onboarding-2026-10-31` (Learn, support and MC pages, checked 2026-10-31) `[Cited]` unless tagged otherwise.

**In one sentence each:**
- **The legal plugin in Word** works *inside one open document*: playbook → topics → rules → Run Review → tracked changes; or precedent → placeholders → Finish Draft.
- **Legal for Cowork** *delegates multi-step work across files, mail, meetings and sources*, and produces new files or edits files in place.

Microsoft's own positioning is a single line for each: Word for "deep document work, with experiences built directly into the document"; Cowork to "delegate broader, multi-step work across agreements, emails, meetings and files".

#### 2.4.1 Side by side

| | Legal plugin in Word | Legal for Cowork |
|---|---|---|
| **Where it runs** | Copilot pane in **Word for Windows desktop** (Mac and web are not listed) | Cowork in the browser, the Copilot desktop app and mobile; Outlook and Teams hosts. Custom skills don't run on mobile. |
| **How it is invoked** | Turned on in `…` › Manage plugins & skills; it then shows legal prompt starters ("Review contract with a playbook", "Draft new contract from a precedent", "Summarize key terms"). It also auto-detects a legal document. Skills are called with `@` (e.g., `@Draft-new-contract`). **`/` in Word picks a file, email or meeting**, not a skill or plugin. | `/skill-name`, the **+** menu, plugin chips (e.g., "Legal (19)") chosen per conversation in Sources & Skills, or a match on intent |
| **Licence and cost** | M365 Copilot licence (USL); no credits for in-app use. Frontier models bill credits. | Licence **plus** an admin spending policy; **every step bills Copilot Credits** |
| **Admin switch** | Cloud Policy "Legal Skills in M365 Copilot in Word" (on by default) | Admin center › Agents › Tools (per plugin, per group) |
| **Model** | **User model picker, GA May 2026** (Auto / Claude / GPT, with reasoning level). Anthropic models are excluded from the EU Data Boundary; EU and UK tenants have them off by default. Whether the legal plugin follows the picker is not documented. | User picks: Claude Opus 5.5 / Sonnet 5.5 / Fable 5.1, GPT 6.1 Sol and others, Grok 4.7. EU Data Boundary tenants get **GPT only**. |
| **What it reads** | The open document plus what the user attaches: a Word playbook, a precedent, agreement context | Everything the user can reach in M365 (files, mail, Teams, meetings), attachments, plugin connectors, **and our skills, references and scripts** |
| **What it produces** | Native tracked changes in the open document, per-topic analysis with clickable citations, content-control drafts | New files in `OneDrive/Cowork/`, in-place edits to Office files (with version history), mail, Teams posts, Adaptive Cards. `edit-contract` produces native tracked changes. |
| **What persists** | **A saved playbook object** `[Tested]` L24. "Save playbook" keeps the imported topics (Name / Rules / Examples) for the user. It is editable by the user, so it can drift from our master. The import is lossy: it dropped the `[nda.*]` ID from the heading and the "Acceptable fallback" line. Whether a saved playbook is offered in a new chat, or can be shared or replaced centrally, is T-P4. Without a saved playbook, nothing carries across chats (S19). | Custom instructions (20 KB), private Projects, skills in OneDrive, Automations. **Memory is conversation-scoped.** |
| **What we can add** | **The Word playbook `.docx`**, rendered in the shape the Legal plugin imports (§5.5).<br>**Tested, doesn't work:** our plugin appears in Word's plugin menu, and its skills can be picked as chips, but Word never loads them (S17, L21, L25). The manifest has no field to limit a plugin to one host.<br>**Out of R1:** a declarative agent (owner, 2026-11-02).<br>Custom skills in Office apps are documented for Excel (preview, Office.js) and PowerPoint, not Word. Word has no custom instructions. | Skills, references, scripts, a plugin, and later a connector |
| **Runs our scripts?** | **No** `[Tested]`: Word Copilot said "Python execution isn't available here" (L21). Excel's preview runs Office.js scripts, not Python. | **Yes** `[Tested]`: Python 3.12.15 with lxml and python-docx, from a plugin and from a OneDrive folder skill (S2, T-02, T-03) |

#### 2.4.2 Same expertise, different objects

- **Probably one skill catalogue, two hosts.** Word's Legal plugin shows three skills (Legal reasoning, Edit contract, Draft new contract) with the same names as three of Legal for Cowork's 20 `[Observed]`. That suggests one Microsoft skill catalogue, filtered by host `[Judgment]`. Microsoft still describes "two legal plugins", with separate admin switches and billing `[Cited]`.

  The same skill name does **not** mean the same capability in each host:
  - In Word, a skill works on the open document, with Word's own editing tools.
  - In Cowork, it works on files in the sandbox and in OneDrive.
- **A "playbook" is a shared concept but not a shared object.**
  - Word parses a `.docx` attached to each review into topics and rules, which the user can edit before running.
  - Legal for Cowork's `review-contract` has no documented input contract. Its `create-playbook` "creates a playbook from a set of precedents", in an undocumented format and location.
  - Microsoft publishes **no playbook schema, sample or template** anywhere.
- **Our Cowork skills and Microsoft's Word plugin draw on the same positions** only because we render both from one source (§6). Microsoft provides no link between them.
- **Partner plugins** (iManage, NetDocuments, Harvey and others) extend Cowork and Copilot Chat, **not** the Word plugin. None ships playbooks.
- **How third parties reach Word.** Harvey appears in Word. Its package is one remote MCP tool with 5 operations, plus 5 one-line skills with no procedure and no instructions `[Tested]` (L26). Its Word presence most likely comes from the MCP tool, not from skill execution. For Spaarke, that points the Word route for *tools* to the v2 connector (§10), not to plugin skills.

#### 2.4.3 Interaction map

| | What happens |
|---|---|
| **Separately** | A Word review touches only the saved document. Cowork work lands in `OneDrive/Cowork/`, and Word's plugin never sees Cowork state. Admin switches and billing are independent. |
| **Together: only through files** | **Cowork → Word:** Cowork edits or creates a `.docx` in OneDrive or SharePoint; the attorney opens it in Word and runs the legal plugin. **Word → Cowork:** a Word-drafted or Word-reviewed contract is attached in Cowork for `summarize-redlines`, `extract-obligations-and-deadlines`, a portfolio comparison, or our skills. **Playbook:** a Word playbook `.docx` in a SharePoint library can be attached to both (Word "Attach from Cloud", Cowork "Attach cloud files"). |
| **No hand-off command** | Neither surface has an "Open in Cowork" or "Continue in Word" command. "Office in Copilot" (Frontier, 2026-10-09) adds a Word canvas beside Cowork, but it isn't GA and doesn't mention tracked changes. |
| **Overlaps** | Playbook review (Word "Review contract" vs Cowork `review-contract`); drafting from a precedent (Word `@Draft-new-contract` vs Cowork `draft-new-contract`); redlining (Word Run Review vs Cowork `edit-contract`). Whether they share prompts or an engine is not documented. |

#### 2.4.4 Which flow goes where (guidance we give attorneys)

There is no in-product router, so every accelerator states this in its install guide and in `accelerator-help`:

| The attorney wants to… | Use | Why |
|---|---|---|
| Review **one** contract against the group's positions and accept or reject redlines while reading | **Word** + our Word playbook | Native, in-document, no credits. Our skills don't compete here. |
| Draft from an approved precedent, filling placeholders | **Word** `@Draft-new-contract` | Content controls and Finish Draft |
| Get our **packaged deliverable**: an internal redline with a page-1 linked summary, an external redline, a clean copy and a cover email | **Our Cowork skill** (e.g., `nda-review`) | Bookmarks, linked summary, plan-bound comments, clean copy and output checks need our engines (§5.6) |
| Triage a batch, compare many agreements, build a lease abstract or invoice exception report in Excel, run an automation | **Our Cowork skill** | Multi-file, formulas, unattended |
| Work outside our practices (MSA, chronology, memo, minutes, obligations tracker) | **Legal for Cowork** built-ins | We don't duplicate them (§5.7-1) |
| Turn precedents into a first-draft legal playbook | **Legal for Cowork** `create-playbook`, then our setup skill converts it to our format (§8.1a) | Re-use Microsoft's skill and keep one master (§6.2) |

#### 2.4.5 Gaps that accelerators fill

1. No playbook object, schema, library or reuse → our legal playbook format, topic IDs and T1 library (§5.3, §6).
2. No positions or house style for the Word plugin → our rendered Word playbooks (§6).
3. No personalization that crosses surfaces → the attorney profile and the custom-instructions block from onboarding (§8.1a).
4. No deterministic, verifiable redline output in Cowork → our engines (§5.6).
5. No onboarding or setup skill from Microsoft for either surface → `accelerator-setup` (§8.1a).
6. No guidance on Word vs Cowork → §2.4.4 in every guide.

#### 2.4.6 How a playbook is activated, surface by surface

"Attached to each review" means, in Word:
1. The attorney asks for a playbook review (a starter or a prompt).
2. Copilot asks for the playbook, and the attorney picks **Upload file** or **Attach from Cloud** (`.docx` only).
3. Copilot parses it into topics and rules, which the attorney can edit.
4. Optionally, **Save playbook**. This keeps the parsed topics as the user's own playbook object `[Tested]` (L24).
5. **Run Review**.

There is no default playbook, no library setting, and no way to automate the choice `[Cited]`. A new chat doesn't remember an *attached* playbook: it re-found ours by search, and by the topic IDs in comments left in the document (S19, L18).

**T-P4 answered (owner, Word desktop, 2026-11-05) `[Tested]`:** **saved** playbooks *are* offered in a new chat. The attorney picks **Review contract** with `/`. Copilot asks "Which playbook do you want to use?" and shows a picker, "Choose a playbook to review this contract". It lists each saved playbook by name with its "Last updated" date, plus **New playbook**, **View all** and **Run review**. **View all** opens "Playbooks: Manage playbooks for contract reviews. Reuse them across future reviews." Our saved `NDA - Mutual (Spaarke baseline)` showed General guidelines plus all of its topics. So, in Word, the playbook is **imported once and then picked from a list**. The list is the user's own: an update means re-importing, and only the name and date reveal a stale copy (L24).

Plain Word Copilot, without the Legal plugin (Word web, T-12), applied our Word playbook and quoted our `nda.*` topic IDs in every comment. Its edits were whole-clause replacements, with no summary and no copies `[Tested]`.

| Surface | How the playbook gets in | Automatic? | What we ship |
|---|---|---|---|
| **Word, legal plugin** | First use: import and Save playbook. After that, `/` **Review contract**, then pick from the saved-playbook list (T-P4) | **No**, but it is one click after the first import | Word playbooks with predictable names in **one** SharePoint `Legal Playbooks` library (T1) or a OneDrive folder (T0), so one search in Attach from Cloud finds them. A one-line "how to attach" card in each guide. |
| **Cowork, our skills** | The skill reads it: our baseline in the skill's `references/`, or the customer's file found by the lookup order in §5.3 | **Yes**: no attach step; the provenance line names what was used | Skills that look the playbook up, and say which one they used |
| **Cowork, Legal for Cowork's `review-contract`** | Attached per conversation, or `/`-referenced in the attorney's custom instructions or a Project | Partly: a `/` reference in Preferences or a Project is included in every task | A ready-made `/`-reference line for Preferences or a practice Project (onboarding step 5) |
So **automation of playbook selection exists only in Cowork**, and it is the main reason our review skills live there. In Word, our job is to make the right Word playbook easy to find and import:
- the one-line onboarding card "type `/` and the playbook name" (L19: "Attach cloud files" did nothing in Word web);
- predictable names;
- a version line, so a stale saved copy can be spotted.

#### 2.4.7 Getting Spaarke into Word: what is possible

| Channel | Status | Can it be tailored by practice or document type? | R1 decision |
|---|---|---|---|
| **Our plugin in Word's "Manage plugins & skills"** | **Tested: listed, never run** (S17, L21, L25). A user-uploaded plugin shows in Word's picker, arriving toggled off in Word web. Its skills can be picked as chips. With the toggle on and three different routes tried, Word ran its own generic suggestions and said it hadn't loaded the skill. Word has no documented loader for third-party skills, and the shared Plugins button (MC1484008) lists plugins in every app without loading them. | — | **Not a channel in R1.** It is a **hazard**: a user who picks our skill in Word gets a generic review that looks like ours. Mitigations: onboarding and the README say "Spaarke reviews run in Cowork"; raise with Microsoft; re-test after admin deploy (T-14) and after Microsoft documents Word skills. |
| **A Spaarke declarative agent in "All agents"** | **Documented, GA.** Agents in the store appear in Word's Copilot pane. | Yes, in principle | **Out of R1** (owner, 2026-11-02). Untested (S18 dropped). It suits Q&A against the playbook, not redlining. Revisit after R1. |
| **A Spaarke MCP connector** | **The route third parties use** (Harvey, L26). Federated connectors are admin-created, with Entra SSO. | Tools, not skills | **v2** (§10, protocol §7) |
| **Prompt starters for the whole organization or by document type** | **Not possible in Word.** Organizational prompts show only in Copilot Chat, Edge and Teams. Word's only document-type sensing is "auto-detects a legal document". | — | Not in R1 |
| **A Spaarke Word add-in button that opens Copilot with plugins and playbooks set** | **Not possible today.** No Office.js API opens the Copilot pane, fills in a prompt, switches a plugin or attaches a file. Microsoft has withdrawn its 2025 "Copilot agents in Office add-ins" pages. | — | Not in R1. A Spaarke ribbon (the existing Word add-in already shows *Save to Spaarke / Quick Save / Open Spaarke*) **could**: <ul><li>open the right Word playbook for a chosen practice;</li><li>copy a ready prompt for the attorney to paste;</li><li>run Spaarke's own review in its task pane (T3).</li></ul>That belongs to the Word add-in project. Re-check when Microsoft documents Word skills. |

#### 2.4.8 Do references, scripts and assets work in Word?

**No** `[Tested]` (L21, L25). The Word legal plugin is Microsoft's own package. Nothing documents Word loading a third-party plugin's `references/` or `assets/`, or running its `scripts/`:
- Excel's preview runs Office.js scripts in a Copilot-managed runtime.
- Declarative-agent skills run in a no-network sandbox.
- Neither is documented for Word.

So in R1:
- **Our references, scripts and assets serve Cowork.**
- **Word receives rendered content only:** the Word playbook.

If Microsoft later documents Word skills and T-P7 shows our instruction-only skills loading there, protocol rule 4.2 still applies. Review in Word = Microsoft Legal + our Word playbook, so we would ship no competing Word review skill.

Live checks that remain: S6 (`create-playbook` output), T-P4 (saved-playbook reuse), T-P6 (protected `.docx` import), T-P7 (Word probe plugin) and T-P10 (read-only library) (§12).

---

## 3. Scope

### 3.1 In scope

1. **Claude Code knowledge and tooling** for building on Cowork, Agent Skills and Copilot for legal (§11). This comes first.
2. **Verification spikes** in a test tenant (§12).
3. **The accelerator foundation:**
   - the canonical content model and alignment rules (§6);
   - the source layout;
   - the build and validation pipeline;
   - Core skills, including the **onboarding** skill and its tools (§8.1a);
   - Core engines (Python), each justified in §5.6;
   - Word playbook rendering;
   - the T1 library template;
   - licence and attribution handling.
4. **The NDA practice accelerator**, to release quality `[Proposed]` (OD-21). The MVP build in `src/accelerators/` is the starting point, and it closes the MVP fix queue (L9, L10/L17, L15, L16) and the attorney content gap (model language for all topics, H2).
   - The roadmap of six accelerators stays as decided on 2026-10-31 (§8.3), but Legal Intake and Invoice Review move to **R2**. Wave 2 (CRE, Employment) and Wave 3 (U.S. Patent Applications) follow.
   - Reason: Core, the build and the gates, onboarding, the playbook model and the Word path are all still being proven on NDA. Spreading across three practices before that settles multiplies rework.
5. **Content sourcing** (§8.5): a source register per practice, recording licence, what we take and what attorneys must write.
6. **The Spaarke content comparison** (§7.3):
   - evaluate the existing Spaarke Actions, Skills and Knowledge for the same practices as an **untested candidate source**, against the researched content and on the same corpus;
   - file the findings to the Spaarke backlog;
   - map our topic IDs and review-result schema onto Spaarke's content;
   - generate JPS Action files from the canonical content as a **validated but not deployed** build output.
7. **Evaluation**, per skill and per accelerator, on one shared corpus (§13).
8. **Delivery collateral for T0/T1:**
   - attorney install guide;
   - practice-team setup guide;
   - IT one-pager;
   - automation recipes;
   - enablement-sprint runbook;
   - the "which flow goes where" guide (§2.4.4).
9. **Optional (OD-11): read-only Spaarke matter context.** A skill that reads matters and documents through the user's own Dataverse access, where the tenant already provides it. No new code, no writes.

### 3.2 Out of scope (later projects)

- **The Spaarke Cowork connector** (§9, §10): a BFF-hosted MCP endpoint with document tools, elicitation forms and MCP Apps. It is the next project, because it adds BFF surface (CLAUDE.md §10).
- **Deploying JPS content to Dataverse**, and any Dataverse schema change (e.g., a legal-playbook or Policy entity).
- **T2 work:**
  - admin tenant package;
  - SPFx UX components;
  - Foundry agents and Routines.
- **T3 work:** Policy versions, Decision Record, Obligations, Finance Intelligence.
- **Any change to the BFF, Dataverse, the Spaarke Console or the Office add-ins.**
- **Partner Center / Microsoft 365 App Store listing, pricing, a download portal.** Distribution in R1 is upload and share (test) plus admin deploy (production, T-14).
- **Declarative agents, Copilot Tuning, and an Office add-in for redlining** (owner, 2026-11-02).
- **Hosting deterministic logic on Spaarke servers for T0/T1.** Engines ship inside the skill package and run in the customer's Cowork sandbox (§5.6). A BFF-hosted engine is the T2 connector path (§10).
- **Jurisdictions other than the United States** (OD-6).

---

## 4. Use cases

| ID | Actor | Journey | Tier |
|---|---|---|---|
| **UC-1** | Attorney (firm or department) | Installs the NDA Accelerator, either by copying its skill folders into OneDrive `Documents/Cowork/Skills/` or by uploading the plugin `.zip`. In a new Cowork session, they ask "review this NDA" and answer one question card. They get back a redline with comments and a linked summary table, a clean version and an emailed cover note. | T0 |
| **UC-2** | Attorney | Runs **onboarding** (`/accelerator-setup`, §8.1a): environment check, skill choice and install, legal playbook (the group's, imported, built from precedents, or baseline), personalization, a trial run and refinement. It writes their attorney profile and a custom-instructions block to paste. Later reviews ask at most one question and state "Standards: <org> NDA – Disclosing v1.2". | T0 |
| **UC-3** | Attorney | Attaches the accelerator's Word playbook (`NDA – Disclosing.docx`) in the legal plugin in Word, runs a playbook review and accepts tracked changes. The Word playbook uses the same topics and positions as the Cowork skill. | T0 |
| **UC-4** | Knowledge lawyer / practice lead | Creates a `Legal Playbooks` SharePoint library from the accelerator's template, read-only for attorneys (§5.3), and maintains the group's legal playbook master there. Shares the accelerator plugin with the group through Cowork, or asks the admin to deploy it to the group. | T1 |
| **UC-5** | Practice-group member | Reviews use the group's legal playbook from the library ahead of any personal copy, so the whole group reviews against the same positions and version. | T1 |
| **UC-6** | Attorney | Sets up an automation from a shipped recipe: "when a file lands in Incoming NDAs, run the NDA review with my defaults and email me the results". It runs unattended, asks nothing and states its defaults on page 1. | T0 |
| **UC-7** | Attorney at a Spaarke customer (optional, OD-11) | Asks "Review the NDA on matter PAT-935426". The skill finds the matter and its documents, with Spaarke's AI summaries (read-only). Until the connector project ships, the file itself is copied to OneDrive. | T0 / T3 bridge |
| **UC-8** | Spaarke legal engineer | Runs the T0/T1 enablement sprint: pre-flight, install, onboarding with the practice lead, evaluation on the customer's own documents, handover. | T0/T1 |

---

## 5. Solution design

### 5.1 Anatomy of a practice accelerator

```
Customer layer   legal playbook(s) · attorney profile                     customer-owned files (§5.3)
Practice layer   Agent Skills · references · assets · Word playbooks ·    Spaarke-authored per practice
                 baseline positions · eval cases
Core             onboarding (setup) · help · shared references · engines  shared by all accelerators
                 (probe, segment, redline, summary table, output checks,
                 playbook import/render)
```

### 5.2 How Cowork runs a skill (what the design relies on)

| Fact | Basis |
|---|---|
| Skills install by putting a folder in `Documents/Cowork/Skills/<name>/` in OneDrive, or by uploading a plugin `.zip`. New or changed skills are picked up by **new** sessions. Both routes ran the same engines identically. Folder skills appeared in `/` within about a minute; plugin upload goes through Plugins › Add › Upload → Publish ("Only you" or specific users). | `[Cited]` `[Tested]` S1, T-01, T-02 |
| A unified (M365) package keeps our name, icons and version. A Claude-format zip also converts on upload, but its display name comes from the `name` slug and its version shows as 1.1.0. | `[Tested]` T-03 |
| Folder-skill quirks: a YAML block scalar in `description` shows literally (`>-`) in the `/` menu; removing the folder does **not** unregister the skill (delete it in Plugins instead). | `[Tested]` L22, L23 |
| At session start Cowork loads every skill's name and description (about 100 tokens each). It loads a skill's body when a request matches, and its `references/` only when the body says to read them. | `[Cited]` |
| A skill can be invoked explicitly as `/skill-name`. | `[Cited]` |
| Limits: 50 custom skills per user; `SKILL.md` ≤ 1 MB; ≤ 20 companion files per skill (5 MB each, 10 MB total); ≤ 20 skills per plugin. No documented limit on plugins. | `[Cited]` |
| **Scripts run.** Microsoft documents `scripts/` files as "Executed, not loaded into context", but not the runtime. On the current service (2026-11-01) we saw:<ul><li>Python 3.12.15, lxml 6.1.3, python-docx 1.2.0;</li><li>LibreOffice (`soffice`) and `pdftoppm`;</li><li>a writable working folder.</li></ul>Plugin skills run from `/mnt/shared/plugins/<id>/<version>/skills/<name>`. That path is an implementation detail, never hard-coded. Node.js and installing packages mid-session remain unobserved. The workspace can reset mid-session. | `[Cited]` `[Tested]` S2, T-03, T-05 |
| A skill can *ask for* Cowork's built-in skills in prose ("present this as an Adaptive Card", "use Communications instead"); the model then loads them. There is no call contract, and routing is the model's choice (§5.2a). | `[Cited]` (handoff wording) / `[Verify]` |
| Outputs persist only when saved to OneDrive or SharePoint. A run's files go to `Documents/Cowork/Tasks/<task>/output/` with no approval needed. The rest of `Documents/Cowork/` is read-only to the agent: a write there (e.g., the profile) needs a Copy/Edit approval card. | `[Tested]` S3, L10 |
| A later session reads the profile and finds a playbook in `Documents/Cowork/Playbooks/<practice>/` without being told. Discovery is a search, though: GPT 5.5 also picked up a playbook from another folder and a previous task's plan (L15, L16), so skills must name exact locations (§5.7). | `[Tested]` T-06, T-07, T-11 |
| Copilot chooses the skill by matching the request against enabled skills' descriptions: "Review this NDA" → `nda-review`; "Review this MSA" → Legal for Cowork's `review-contract`, with our plugin not loaded. | `[Tested]` S9, T-09 |
| Send, post and delete actions need the user's approval unless pre-approved. | `[Observed]` |
| SharePoint Embedded returns 403 to Cowork's Graph access. Dataverse is readable under the user's own security roles where the tenant provides it. | `[Observed]` |
| **Personalization.** Custom instructions are set in Customize › Preferences (20 KB) and are included in every task; no skill can write them. Projects carry their own instructions, take precedence over global ones, and are **private to their creator**. **Memory is conversation-scoped.** There is no `copilot-instructions.md` file; the POC's description of one was Cowork's own claim, not documentation. | `[Cited]` |
| **Skill Management** is a built-in skill. It creates, edits and validates skills in conversation, and saves `SKILL.md` plus a **Skill Report** (0–100, with a conflict scan) to `OneDrive/Documents/Cowork/skills/<name>/`. Authoring and testing a skill bills credits. Whether one skill can drive it to install another is not documented. | `[Cited]` / `[Verify]` S14 |
| Cowork asks structured questions with native **question cards** (choices, Skip, free text). MCP elicitation forms are available only to connectors. | `[Cited]` |

### 5.2a Cowork's built-in skills (definitive list, 2026-10-31)

Source: researcher note `cowork-builtin-skills-and-specialist-agent-patterns-2026-10-31`. The list is Learn's `use-cowork` skills table plus the application card `[Cited]`.

| Built-in | What it does | What our skills use it for |
|---|---|---|
| **Word** | Create and edit Word documents (in place, with version history) | Cover memos. Not used for the redline, which comes from our engines (§5.6) |
| **Excel** | Create and edit spreadsheets | Abstracts and exception reports (formulas written by `calc_amounts`) |
| **PowerPoint** | Create and edit presentations | Rarely |
| **PDF** | Work with PDF documents | Reading PDF invoices and contracts |
| **Email** | Compose, reply, forward and send; drafts and attachments (sending needs approval) | Cover emails; the Legal Intake email |
| **Scheduling** | Schedule meetings | — |
| **Calendar Management** | Events, Teams links, the calendar | Critical-date reminders (optional) |
| **Meetings** | Meeting intelligence | — |
| **Daily Briefing** | A daily briefing | — |
| **Enterprise Search** | Search across the organization (Work IQ) | Finding the legal playbook or a precedent |
| **Deep Research** | Multi-source research | Research steps, with our verification rules (§5.7-9) |
| **Communications** | Stakeholder communications (Microsoft's sample hands off to it as `stakeholder-comms`) | Hand-off target for non-legal communications |
| **Adaptive Cards** | Cards with layouts, buttons and data | Risk dashboards and summaries |
| **App** (Frontier) | Build small interactive apps from a description | Not used in R1 |
| **Skill Management** | Create, edit and validate skills; Skill Report | Onboarding (§8.1a) |
| *(unnamed)* image generation | Images | — |
| *(not a skill)* browser tool, web search, `/cost` | Edge browsing (off by default for the tenant); Bing grounding by policy; credits used by the task | `/cost` to measure accelerator runs |

Constraints:
- Users and admins **cannot switch off individual built-ins**. Controls are coarse: plugin on/off, model family, browsing, web search, Frontier.
- Plugin skills can't override a built-in of the same name, so the lint rejects built-in names (§5.5).
- Copilot in Word has **no** list of built-in skills. Its pane exposes plugins and agents (§2.4).

### 5.3 Customer layer: legal playbooks and attorney profiles

Customer content lives in files that skills read at run time. It is never baked into a skill, so changing a position never needs a reinstall.

**Legal playbook format.** Markdown, one file per agreement type and side, adopted from POC Appendix B:

```markdown
# Legal playbook: NDA – Disclosing Party
Version: 1.2 | Owner: <name> | Effective: 2026-11-01 | Jurisdiction default: Delaware | Accelerator: nda 1.0.0

## topic: nda.definition-unmarked
Preferred: ...
Fallback: ...
Escalate: ...
Model language: "..."
Rationale: ...
Priority: High
```

Each `## topic:` heading carries a **topic ID** from the accelerator's topic catalogue (§6.2).

**Where the customer layer lives: two kinds of playbook, in named places only** (protocol §6; replaces v0.4's lookup order):

| Kind | Location | Editable by the attorney? | How a skill finds it |
|---|---|---|---|
| **Governed** (Spaarke baseline or firm-approved) | SharePoint `Legal Playbooks/<practice>/`, **read-only** for attorneys and editable by the playbook owner | No | The library path recorded in the profile. ❓ T-P10: can Cowork read it, and can Word import from it? |
| **Personal** (the attorney's own) | OneDrive `Documents/Cowork/Playbooks/<practice>/` `[Tested]` T-07 | Yes | That exact folder |
| **Baseline** (fallback) | `references/` inside the skill | No | Used when neither of the above exists, and labelled "Spaarke baseline positions (not tailored)" |

Rules:
- **Named locations only** (L16). A skill reads playbooks only from the locations above or from a file the user attaches in that request, and ignores anything else search returns.
- **Source labels.** `review_basis.source = customer` only for a governed or personal playbook. Spaarke-published playbook files carry a marker that maps to `spaarke-baseline`, and `check_outputs` rejects a mismatch.
- **Ambiguity.** If more than one candidate playbook applies, the skill asks which. If there is none, it says so on page 1.
- **Fallback if T-P10 fails** (Cowork can't read a read-only library): setup copies the governed playbook into OneDrive with its version line, and the skill warns when the copy is older than the library version.

The **attorney profile** is `Documents/Cowork/Profile/legal-profile.md` `[Tested]` S3. Setup writes it after one approval card. A later session reads it unprompted, and with it `nda-review` asked no questions (L11). The custom-instructions block (§8.1a) stays as a complement: it is always loaded, but no skill can write it.

**Provenance rule.** Every substantive output begins with the standards it used:
- With a playbook: *"Standards: Acme Legal NDA – Disclosing v1.2 (SharePoint) · Accelerator nda 1.0.0 · Side: disclosing · Posture: balanced"*.
- Without one: *"Spaarke baseline positions (not tailored)"*.

The skill never invents a position the playbook lacks; it reports that the playbook is silent.

### 5.4 Surfaces and what each receives

| Surface | Receives | Who installs | Limits that bind the design `[Cited]` |
|---|---|---|---|
| **Cowork, personal skills** | Skill folders copied into OneDrive | Attorney | 50 skills; picked up next session; no mobile |
| **Cowork, plugin** | Plugin `.zip` (skills; later the connector) | Attorney (upload, then share with named users); admin for org-wide | ≤ 20 skills; blocked under Information Barriers or when the admin disallows organization-built plugins |
| **Cowork, admin-deployed plugin** | The same `.zip`, through admin center › Agents (production route) | Customer admin, scoped to users or groups | ❓ T-14: does it change where the plugin appears (Cowork, Word, PowerPoint "Organization")? Admin center › Agents › Tools › Skills also has its own **Upload** (L26), to try |
| **Legal plugin in Word** | Word playbook `.docx` | Attorney (imports, optionally saves as a playbook) | `.docx` only; Name / Rules / Examples per topic; **Word for Windows desktop only** `[Tested]` S5; the saved copy is the user's own and can drift (L24) |
| **Our plugin in Word's "Manage plugins & skills"** | — (listed, never executed) | — | `[Tested]` S17, L21: a hazard to manage, not a channel (§2.4.7) |

SharePoint site skills run only in Copilot in SharePoint, not in Cowork `[Cited]`, so they are dropped from R1.

### 5.5 Source format, build and validation

**Source layout** (location is OD-4; proposed `src/accelerators/` in this repo):

```
src/accelerators/
├── README.md
├── core/
│   ├── skills/<name>/                    accelerator-setup, accelerator-help
│   ├── shared-references/                standards-protocol.md · verification.md · disclaimer.md ·
│   │                                     review-result.schema.md · edit-plan.schema.md
│   ├── engines/                          probe.py · segment_clauses.py · ooxml_redline.py ·
│   │   │                                 summary_table.py · check_outputs.py · playbook_parse.py ·
│   │   │                                 playbook_import.py · render_word_playbook.py ·
│   │   │                                 profile_check.py · instructions_block.py (§5.6, §8.1a)
│   │   └── tests/                        golden fixtures
│   └── schemas/                          review-result.schema.json · edit-plan.schema.json ·
│                                         legal-playbook.schema.json
├── practices/<practice>/
│   ├── accelerator.yaml                  id, version, wave, skills, sources, targets
│   ├── topics.yaml                       topic catalogue (§6.2): the join key
│   ├── positions/baseline.md             baseline legal playbook(s), attorney-approved
│   ├── skills/<name>/                    SKILL.md + references/ + assets/ (engines copied in at build)
│   ├── library/                          T1 template content
│   ├── onboarding.md                     practice interview module (copied into accelerator-setup)
│   ├── sources.md                        content source register (§8.5)
│   ├── evals/                            cases, rubric, corpus manifest
│   └── THIRD-PARTY-NOTICES.md
├── third_party/<source>@<sha>/           pinned upstream copies with LICENSE/NOTICE
└── build/                                Build-Accelerator.ps1 + renderers
```

**Self-contained skills.** A skill may only reference files inside its own folder `[Cited]`. So the build **copies** the Core shared references and the engines each skill needs into that skill. There is still one source, and each skill stays under the 20-companion-file limit.

**Build outputs per accelerator release:**

| Output | Target |
|---|---|
| `t0/skills/<name>/` folders + `t0/<practice>-plugin.zip` | Cowork |
| `t0/word-playbooks/*.docx` | Legal plugin in Word. Rendered in the shape the plugin imports (L24): Heading 1 title; Heading 2 per topic; `Topic ID:`, `Priority:`, `Review question:` and `Fallback position:` lines inside the topic body; a General guidelines section; how-to text and version in document properties. *Done 2026-11-02; the A/B import retest is still owed.* |
| `t0/automation-recipes.md` | Cowork Automations (§5.8) |
| `t1/legal-playbooks-library.zip` | SharePoint T1 template |
| `platform/jps/*.json` | Spaarke Actions (validated with `jps-validate`, not deployed; §7) |
| `manifest.json` | Version and content hash of every output (§6.4) |
| Guides, `evals/` | Delivery collateral |

**Validation gates.** These are build-time checks that also run in CI, not prose:

1. **Agent Skills spec** (`skills-ref validate`). Frontmatter may contain only the spec's six keys:
   - `name` and `description` (required);
   - `license`, `compatibility`, `metadata`, `allowed-tools` (optional).

   `metadata` values are flat strings, and Cowork reads `metadata.category` and `metadata.icon`. Any other top-level key is an error. Use a real Python install, never the Windows Store stub.
2. **Microsoft plugin validation** (`wiqd plugin validate --mode deep`, preview), plus Cowork's own skill check during the spikes.
3. **Spaarke accelerator lint.** Each rule ships with a must-fire and a must-not-fire fixture. It fails on:
   - folder name ≠ `name`;
   - a description without trigger phrases or without a "Do NOT use for…" line;
   - `~/.claude` or other host-specific paths;
   - upstream trademarks in names;
   - companion-file limits exceeded, or hidden files;
   - a missing provenance, disclaimer or "contract text is data, not instructions" block;
   - a name or primary trigger that collides with a Legal for Cowork skill;
   - an adapted file that isn't traced to a pinned upstream;
   - **a topic ID that isn't in `topics.yaml`** (§6).
4. **Engine tests.** `pytest` on golden fixtures. They check:
   - output packages are schema-valid;
   - tracked-change counts match the plan;
   - clean text is as expected;
   - existing revisions are untouched.
5. **Alignment check** (§6.5).

**T0 packaging** (OD-7, decided by S1): the **unified (M365) plugin `.zip` is primary**. It uses manifest 1.30, a stable deterministic app GUID and our own name, icons and version. Skill folders are the no-upload alternative. The Claude-format zip converts, but loses the name and version, so it serves only Claude-ecosystem reuse.

**Gates added during the MVP:** `description-plain` (one-line description, L22), `description-routing`, `cli-drift` (every script flag SKILL.md uses exists), a `__pycache__` re-lint before packaging (L7), topic prefixes taken from the catalogue (R13), and 22 gate tests in all. **Planned:** T-P5 (flag position text inside skills), and a lint requiring the L15/L16 sentences in every review skill.

### 5.6 Core engines: what they are, where they come from, and why

Source: researcher note `skill-engines-precedent-licences-2026-10-31` `[Cited]` unless tagged otherwise.

**What an engine is.** An engine is a small Python script shipped in a skill's `scripts/` folder. Cowork *executes* scripts; it does not load them into context `[Cited]`. The model decides what should change and why. The script does the mechanical work and proves it was done: editing the XML, counting, computing dates and amounts, and checking the result. The rule is **mechanics in code, judgment in the model**.

**Where the requirement came from.** Nobody specified these engines up front. They came out of the POC `[Observed]`:
- Asked for a surgical redline with a linked summary, Cowork's in-place Word editing could not add bookmarks or hyperlinks, and rewriting paragraphs produced whole-paragraph changes.
- So Cowork *wrote* Python against the document's OOXML in the session, checked its own output (counts, page renders) and caught a table spill and low-contrast colours.
- The session workspace reset twice, losing that code.

The POC concluded that reusable logic must ship in the skill package, not be re-invented in each session (POC §2.5).

**Precedent: this is how production document skills are built.**
- **Anthropic's own document skills** (docx, xlsx, pptx, pdf; the same family Cowork's technology comes from) ship about 11,400 lines of Python helpers and validators. Examples:
  - a run merger;
  - a comment-parts helper;
  - an **untracked-edit validator**;
  - a LibreOffice accept-all;
  - an Excel recalculation check.
- **Anthropic's authoring guidance** says: "Prefer scripts for deterministic operations"; "Provide utility scripts … more reliable than generated code"; "Solve, don't defer"; and use plan-validate-execute for "batch operations, destructive changes … high-stakes operations".
- **The Agent Skills spec** defines `scripts/` for exactly this purpose. **Microsoft's Cowork plugin docs** show `scripts/extract-clauses.py` as their example.

Two caveats:
- **Licence.** Anthropic's document skills are **proprietary** (source-available; no copying or derivatives). We reuse patterns, not code.
- **Microsoft's samples.** The copilot-camp Cowork labs ship no scripts, but the Power CAT agent-skills gallery does (e.g. `work-order-assist/skills/parts-readiness/scripts/parts_readiness.py`, "deterministic parts_readiness"), and it duplicates shared references in every skill, as we do (NDA MVP finding L5, 2026-10-31).

**Evidence that the model alone is not reliable at these mechanics.** These benchmarks used models one or two generations older than Cowork's. The direction is consistent; the current size of the gap is unknown.

| Mechanic | Finding |
|---|---|
| Date arithmetic | Test of Time (ICLR 2025): 13.5–16% accuracy on duration questions. "The most common error … was a deviation of precisely one day." |
| Multi-step arithmetic | Faith and Fate; GSM-Symbolic: accuracy falls as steps or digits grow, or when only the numbers change |
| Legal content | Large Legal Fictions: 58–88% hallucination on case questions. Lexis+ AI and Westlaw tools: 17–33%. |
| Tracked-change XML | No benchmark. Anthropic's validator exists because untracked edits are "easy to do by accident and invisible in the accepted view". |

**A reason specific to Cowork:** Cowork lets users pick GPT, Claude or Grok, and EU tenants get GPT only. XML that the model writes by hand will vary by model; a script's output won't.

**What Microsoft already does, so we don't build it.** Microsoft already covers these:
- the Word plugin's Run Review and Legal for Cowork's `edit-contract` both produce **native tracked changes**;
- Copilot in Word manages comments.

So we build no engine for:
- a single-document review in Word;
- clause judgment, drafting language or summaries (model work).

**Engine-by-engine verdict:**

| Engine | Does | Microsoft built-in alternative | Precedent | Verdict |
|---|---|---|---|---|
| `probe.py` (new) | Reports Python version, `lxml`/`openpyxl`/`python-docx`, `soffice`/`pdftoppm`; every skill runs it first | None (the sandbox is undocumented) | Anthropic: "Don't assume packages are available" | **Add** |
| `segment_clauses.py` | Section map `{number, heading, para_key, text}`: the anchors for bookmarks and the edit plan | None | Anthropic `merge_runs.py` (make text findable before editing) | **Keep** |
| `ooxml_redline.py` | Edit plan → surgical `w:ins`/`w:del`, plan-bound comments, bookmarks; clean copy | Word Run Review; Legal for Cowork `edit-contract` (native tracked changes) | Anthropic ships helpers and a validator, not a generator; our plan → apply design is the stricter plan-validate-execute pattern | **Keep, repositioned.** Not for a single-document review in Word, which goes to Microsoft's plugin (§2.4.4). Used for the packaged deliverable: Cowork output files, plan-bound comments, bookmarks, the clean copy, batches and automations, and model independence. |
| `summary_table.py` | Page-1 summary with bookmark hyperlinks and contrast-safe colours | None (in-place editing can't make bookmarks or hyperlinks `[Observed]`) | None; Anthropic leaves this to raw XML | **Keep** |
| `check_outputs.py` | Counts (edits = rows = comments), residue, contrast, package sanity; **new: untracked-edit check** (original vs. revisions undone) and render-and-look when a renderer exists | None | Anthropic `validate.py` and `redlining.py`, `recalc.py` | **Keep and extend.** We reimplement the checks rather than copying them. No XSD bundle: the ECMA schemas exceed the 20-file / 10 MB limit. |
| `playbook_parse.py` | Markdown legal playbook → JSON topics; rejects unknown topic IDs | Word parses a `.docx` with the model; `create-playbook` drafts one | Spec: "closed sets", "reject ambiguous input" | **Keep, as a format contract.** Its value is closed topic IDs and versioning, not model reliability. |
| `playbook_import.py`, `render_word_playbook.py` (new, onboarding) | `.docx` legal playbook (or `create-playbook` output) → our Markdown with topic IDs; Markdown → Word playbook `.docx` at run time | None | — | **Add** (§8.1a) |
| `profile_check.py`, `instructions_block.py` (new, onboarding) | Profile has no silent gaps and valid markers; composes the custom-instructions block and enforces ≤ 20 KB | None | claude-for-legal "never write a profile with silent gaps" | **Add** (§8.1a) |
| `calc_dates.py` | Notice windows, critical dates, business-day rules; fails on unknown holiday data and cites the rule | Partner "Calculate Working Day" connector (T2 only) | The behaviour of `lexdays`, not its code | **Keep.** Uses stdlib `datetime` and a JSON holiday asset per jurisdiction, with source and date. |
| `calc_amounts.py` | Rent steps, invoice arithmetic; writes **formulas** (`<f>`) into Excel, never model-computed totals | Not documented for Cowork | Anthropic xlsx: "Use formulas, never hardcoded results" | **Keep.** Uses `decimal`. |
| `ledes_parse.py` (Invoice Review, R2) | LEDES 1998B parse plus UTBMS code validation | None | `ledes-parser` is stale and says "do not rely" | **Build** with stdlib `csv` |
| `claims_check.py` (Wave 3) | Claim numbering, dependency, antecedent basis | None | MIT `claims_analyzer.py` as a reference for which checks to make | **Build** with stdlib `re` |

**Open-source libraries considered and not used:**

| Library | Licence | Why not |
|---|---|---|
| `docx-redline` | MPL-2.0 | Needs Python ≥ 3.12 and `python-docx` ≥ 1.2. Revisit if S2 confirms both. |
| `python-redlines` | MIT | Needs .NET binaries |
| `adeu` | MIT | Heavy dependencies; patterns only |
| `lexdays` | **AGPL-3.0** | Never vendor it |
| `holidays` | MIT | Only if `dateutil` is present |

**Status after the MVP** `[Tested]`:
- `probe`, `segment_clauses`, `ooxml_redline`, `summary_table`, `check_outputs` and `playbook_parse` are built, with 41 engine tests.
- They ran unmodified in Cowork on three models. One adversarial code review found 21 defects, all fixed with regression tests (results R1–R21).
- Word desktop opens every output without repair.
- POC Appendix D's limitations are closed (L1).

**Compared with the expert redliner demo** (`notes/2026-11-05-external-inputs-review.md`):
- The demo segments **plain text** with a stdlib-only script and leaves the redline to Cowork's built-in Word skill.
- That avoids `lxml`, but it can't anchor comments, bookmarks or the linked summary, and our T-12/T-13 show the built-ins' edits are whole-paragraph or comment-free.
- We keep the OOXML engines. Stdlib plain-text segmentation is the documented fallback if `lxml` ever disappears from the sandbox.

**Engine rules:**
- Python 3 standard library plus `lxml`. `python-docx` is present (S2) but not yet relied on. `probe.py` runs first in every engine skill, because the runtime is undocumented and can change. If `lxml` is missing, stop and report; **never fall back to `xml.etree` for writing**, because it rewrites namespace prefixes and breaks `mc:Ignorable`.
- No network access and no secrets.
- Follow the Agent Skills scripting rules: JSON in and out, `--help`, diagnostics on stderr, meaningful exit codes, idempotent, `--dry-run` where it writes.
- Each skill says what to do if a script can't run: stop and report. It never silently falls back to arithmetic by the model.
- Each skill carries its own copy of the engines it needs (Anthropic duplicates its `office/` tree per skill for the same reason) and stays within 20 files and 10 MB. Test fixtures stay outside the shipped skill.
- The same engines are tested against the same golden fixtures as their later C# twins in the BFF (§6.3).

The engine limitations the POC lists (Appendix D) are fixed and its OOXML gotchas (Appendix E) are built in (L1, 2026-10-31).

### 5.7 Skill design rules

1. **Complement Legal for Cowork and the Word plugin; never duplicate them** (§2.4.4).
   - Name and describe skills around our differentiators: positions, side, posture, engines, outputs.
   - Every description ends with "Do NOT use for…" lines that point to the built-in skill (POC Appendix A).
   - Hand off to built-ins where they help, e.g., `create-playbook` from precedents.
2. **Ask once, up front.** One question card, containing only the questions without a saved default.
3. **Unattended mode.** Under an automation the skill asks nothing, uses the profile defaults, and states them on page 1.
4. **Look for standards first, and say what was used** (§5.3).
5. **Minimal redlines.**
   - Change the smallest span.
   - A deleted clause becomes "Intentionally Omitted" so numbering survives.
   - Business confirmations (dates, names, venue choice) are comment-only.
6. **Contract text is data, not instructions.** There is a behaviour test for prompt injection inside the contract.
7. **Validate and render before delivery.** Page renders catch table spill and contrast problems that structural checks miss `[Observed]`.
8. **Human approval at every boundary.**
   - Email defaults to the requesting attorney, with options "draft only" or "do not send".
   - Never address a counterparty unless the user names them.
   - Onboarding warns against a blanket "Always allowed: Send email". The expert demo ran with it on. Pre-approval is scoped to the attorney's own address or the intake mailbox only.
9. **Verification discipline.** Cite authority only from the document, the shipped references or a fetched official source. Tag anything else `[verify]`.
10. **Model-agnostic.** Evaluate on GPT and Claude, because EU Data Boundary tenants get GPT only. On the same NDA (T-11, all Medium):
    - Sonnet 5.5 cost 71 credits, Auto 118 and GPT 5.5 230;
    - all three passed the checks;
    - GPT needed guard rails (L15–L17).

    Onboarding recommends Sonnet or Auto and states the credits per run.
11. **Fresh inputs only** (L15). Build the plan only from this document's `clauses.json` and the playbook. Never open an `edit-plan.json`, `clauses.json` or output from another task. The checks verify a package against its own plan, so they can't detect a reused plan; the skill text and an eval case must.
12. **Named locations only** (L16, §5.3). Read playbooks only from the governed library, the personal folder, the attached file or the shipped baseline, and label the source correctly.
13. **Outputs stay in the task's `output/` folder** (L10, L17). Name the path in the reply. Copy beside the original only on request, because each copy costs an approval card.
14. **Approved wording verbatim.** Where the playbook has model language for a topic, an edit uses it word for word. The model drafts new language only when none exists, and the comment says so. (From the expert demo's `standard-clauses` asset; positions and wording still live in the playbook, not the skill.)
15. **Optional plan preview** `[Proposed]`. Before generating files, show the plan as an Adaptive Card: counts, High items, escalations, and Approve / Change. Skipped in unattended mode. Rendering works (expert demo); whether the buttons send anything back is S7.
16. **One-line descriptions** (L22). The frontmatter `description` is one plain line naming triggers and "Do NOT use for…" neighbours (gated).
17. **Size discipline.** `SKILL.md` stays lean; detail goes in `references/`, one level deep. Each `SKILL.md` follows the POC's sections: when to use, when not to use, workflow, output table, guardrails, common issues.

### 5.8 Automations

Automations belong to the user. A plugin can't ship one, although partners can supply **trigger events** through their connectors `[Cited]`. R1 therefore ships two things:
- **Automation recipes**: prompt text plus a trigger, e.g., "Incoming NDAs folder → NDA review → email me".
- **Unattended mode** in every skill.

Event triggers on Spaarke data (e.g., "new NDA filed on a matter") wait for the connector project.

### 5.8a Specialist reviewers ("a contract attorney looks at this")

The ask is a reviewer with its own specialty (contract attorney, IP specialist, employment counsel), which our skill calls on for one aspect of the document or conversation.

Source: researcher note `cowork-builtin-skills-and-specialist-agent-patterns-2026-10-31` `[Cited]` unless tagged.

**What Cowork does and doesn't allow:**
- **No agent calls.** Nothing in Cowork invokes another agent, whether declarative, Copilot Studio or Foundry. Claude-style `agents/` sub-agents are "not yet supported". Declarative `worker_agents` and Copilot Studio A2A work only in Copilot Chat. Cowork runs skills **sequentially**, in one conversation.
- **No egress from scripts.** A script **cannot call a model or an agent**. The sandbox is designed to block network access: a reported bypass was mitigated on 2026-08-19. There is also no supported way to give a script a credential ("Don't embed secrets in SKILL.md"). So "a `.py` script that calls the contract attorney" is **not viable**, and doing it would move customer documents out of the tenant without the user seeing it.

**Patterns we use:**

| Pattern | How it works | Tier | Verdict |
|---|---|---|---|
| **Specialist panel inside one skill** | Specialists are files: `references/specialists/<role>.md` (mandate, checklist, red flags, house positions, fixed output schema). The skill takes each specialist in turn:<ol><li>read the specialist's file in full;</li><li>review the assigned sections;</li><li>produce anchored findings (finding ID, clause, severity, position, suggested text, confidence).</li></ol>Then it synthesizes the results into one review. `check_outputs.py` validates every specialist's output against the schema. Precedent: Microsoft Power CAT's `persona-reaction-panel` skill, built for Cowork. | T0/T1 | **Default for R1 review accelerators** |
| **Specialist as its own skill** | `/contract-specialist`, `/ip-specialist`: each also usable on its own; the orchestrating skill hands off by name | T0/T1 | **Test-gated** (S16): routing is the model's choice and may skip the hop |
| **Specialist as a server-side agent** | Our Spaarke connector exposes a tool `review_with_specialist(file, specialty, questions)`. The BFF calls a Foundry agent on behalf of the user, and the result returns as text, a card or an MCP App. | T2 (connector project) | The only documented way to get a real second agent, with its own model, knowledge, tools and audit trail. The document leaves M365 for Spaarke's Azure, with the user's approval, so it needs a DPA and pricing. |
| Script calls an LLM | — | — | **Not viable**, and a policy violation |

The specialist roster is versioned content (§6), so the same files can later become the instructions for the T2 Foundry agents. Whichever form a specialist takes, its output uses the review-result schema.

### 5.9 T1 practice library

The practice lead uploads the template folder tree by drag-and-drop. Site owners can't create SharePoint templates without an admin `[Cited]`.

```
Legal Playbooks/
├── <practice>/                 legal playbook .md files (master) + Word playbook .docx (rendered)
├── Precedents/                 customer's approved forms
├── Clause Library/
├── Checklists/
└── Accelerators/               current skill folders and plugin .zip for the group
```

To give the group the skills, the practice lead shares the plugin with named users in Cowork, the admin deploys it to the group (T-14), or members copy the skill folders. The library is where the group's legal playbooks are kept and versioned. Attorneys get **Read** only on `Legal Playbooks/<practice>/` (§5.3).

### 5.10 Component protocol (summary of `notes/component-protocol.md` v0.2)

The protocol answers, for every primitive we use, what it is for, how it is built, who manages it and how it is distributed. Its rules are binding on the spec.

**One job per primitive:**

| Primitive | Holds | Never holds |
|---|---|---|
| Topic catalogue (`topics.yaml`) | Stable topic IDs: the join key across playbooks, skills, comments and evals | — |
| Legal playbook (master Markdown; one per agreement type) | **Positions**: preferred, fallback, prohibited, model language, rationale, priority. Positions shared across types live once in `core/positions/` and are compiled into each playbook. | Procedures |
| Word playbook (`.docx`) | The same positions, rendered for Microsoft's Legal plugin | Anything edited by hand |
| Skill | **Procedures** | Positions (a position in a skill is a defect; gate T-P5) |
| Profile | One attorney's **defaults** | Positions, procedures |
| Plugin | **Distribution** of skills, and later connectors | Content of its own |
| Connector (MCP, v2) | Live tools and data | — |

**Non-overlap rules:**
1. **Microsoft first.** Before building a skill, inventory what Microsoft Legal (Word and Cowork) and installed third parties already do (T-P1). Where Microsoft owns a task, Spaarke supplies content (a playbook), not a competing skill.
2. **In Word, review = Microsoft Legal + the Spaarke Word playbook.** No Spaarke Word review skill. This withdraws the `nda-review-inline` idea of 2026-11-02.
3. **NDAs in Cowork route to Spaarke `nda-review`** because of the packaged, checked output (owner-approved, 2026-11-02). A customer who doesn't want the package uses Microsoft's `review-contract` with our playbook and switches our plugin off.
4. **Every description names its neighbours' owners** ("Do NOT use for MSAs (use review-contract)"). This is gated.
5. **Playbooks complement, never overlap.** One playbook per agreement type; a routing step or the user picks it.

**Orchestration.**
- Copilot, not Spaarke, picks the skill.
- Toggles are per user and per app (`[Tested]`).
- Onboarding gives a recommended toggle set per role, so users don't switch plugins while working.

**Plugin structure** `[Proposed]` (OD-20).
- *Protocol v0.2 proposes two plugins per practice:*
  - **K (Knowledge)**: instruction-only skills, safe to load anywhere including Word, with no review procedure;
  - **E (Engines)**: Cowork-only, holding `nda-review` and its scripts.
- *v0.5 recommends deferring the split, for four reasons:*
  - Word has no third-party skill loader (L25). Harvey reaches Word through an MCP tool (L26).
  - K's candidate skills are thin: setup, playbook routing, and "explain our position".
  - The manifest can't limit E to Cowork, so E still lists in Word and the L21 hazard remains either way.
  - The split adds a second install and an untested cross-plugin hand-off (T-P3).
- *Instead:*
  - ship one Cowork plugin per practice;
  - gate the K rule now: no instruction-only skill may contain a review procedure;
  - run the cheap one-skill Word probe (T-P7);
  - split only if T-P7 shows Word loads instruction-only skills and a Word-side skill would add something Microsoft Legal doesn't.

**Ownership.**

| Role | Owns |
|---|---|
| Spaarke legal engineering | The catalogue, the baseline playbook (with attorney review) and knowledge skills |
| Spaarke engineering | Engines, the build and gates, releases, and the v2 connector |
| Customer knowledge management | Customer playbook masters and the read-only library |
| Customer admin | Deployment, the Legal Cloud Policy and library permissions |
| Attorney | Profile, toggles and their own saved Word playbooks |

---

## 6. One content model, three surfaces

A practice's content appears in three places:
- Cowork skills;
- the legal plugin in Word;
- the Spaarke platform (the JPS Actions, Skills and Knowledge used by Spaarke's own AI).

The formats and storage locations differ. The topics, positions, output meaning and version must not.

### 6.1 The principle

```
                  practices/<p>/  (canonical source, one per practice)
          topics.yaml · baseline legal playbook · references · eval cases · schemas
                                     │  build
        ┌────────────────────────────┼─────────────────────────────┐
        ▼                            ▼                             ▼
  Cowork Agent Skills         Word playbook .docx           Spaarke platform
  SKILL.md + references/      topics → rules → example      JPS Action JSON (instruction,
  + engines + assets          clauses (rendered from the    output schema, $choices on
                              legal playbook)               topic IDs) + Knowledge content
        │                            │                             │
        └──────────── same topic IDs · same review-result schema · same version stamp ──┘
```

### 6.2 Alignment keys

**Topic catalogue** (`topics.yaml`): stable topic IDs (`nda.term`, `nda.data-use-licence-back`, …). Each has a title, description, checklist question and default priority. Topic IDs appear in:
- headings in the legal playbook and the Word playbook;
- the Cowork checklist;
- the JPS output enum (`$choices`);
- eval rubric items.

**Review-result schema** (`review-result.schema.json`): one result shape for every surface.
- `review_basis`: the legal playbook name and version, side, posture and accelerator version.
- `issues[]`: `topic_id`, `section`, `status` (compliant / fallback / deviation / missing / not-applicable / undetermined), `quote`, `basis`, `action`, `priority`, `rationale`.
- `edits[]`: the edit plan.

It is used by the Cowork edit plan and summary, the JPS Action `output` section, the connector's future `log_analysis` tool, and eval scoring.

**Version stamp**: accelerator semver plus a content hash. It appears in:
- `SKILL.md` `metadata.version`;
- the Word playbook's document properties and footer;
- JPS `metadata`;
- the legal playbook header;
- the provenance line in every output.

**Customer master**: at any time, a customer's legal playbook has exactly one master copy. Never two.
- At T0/T1, the master is the playbook owner's Markdown file: the Spaarke repo for the baseline, and the customer's knowledge-management source for customer playbooks. The governed library and the personal folder hold **published outputs** (the Markdown copy Cowork reads and the rendered `.docx`), never a second master (protocol §6). Word saved playbooks are user copies; drift is detected by the `Topic ID:` and version lines, and changes go back to the owner.
- At T3, Spaarke becomes the master (Policy versions, in the ontology project) and **exports** the file for Cowork and Word.

### 6.3 What may differ by surface

| | Cowork | Word | Spaarke platform |
|---|---|---|---|
| Format | Markdown + Python | `.docx` | JPS JSON in `sprk_analysisaction`; Knowledge records |
| Engines | Python (in sandbox) | Microsoft's own | C# executors/tools in the BFF |
| Customer overlay | Files in OneDrive/SharePoint | Attached `.docx` | CUST- scopes (Save As / Extend) |
| Retrieval | `references/` loaded on demand | n/a | Inline Knowledge or RAG index |

The mechanics will exist twice: Python in Cowork and C# in the BFF. Both are tested against **the same golden fixtures and the same review-result schema**. From T2, Cowork can call the BFF's implementation through the connector instead of its own Python (§10).

### 6.4 Release and drift rules

1. Every build writes `manifest.json` (version and hash per output) and stamps every output.
2. Any content change bumps the accelerator version.
3. A topic catalogue change is a **minor** version when it only adds topics, and a **major** version when it renames or removes any.
4. A customer legal playbook declares the accelerator version it was written against. `playbook_parse.py` warns about topics that are unknown or retired.

### 6.5 Alignment check (build gate)

The build fails if any of these hold:
- a topic in the legal playbook, Word playbook or JPS enum is not in `topics.yaml`;
- a topic in `topics.yaml` is missing from the baseline legal playbook without an explicit `n/a`;
- version stamps differ across the outputs of one build.

From Phase 3, a cross-surface evaluation adds a further check. The same corpus document runs through Cowork and through the Spaarke Action (once that Action is deployed, in the T3 project). The two sets of topics and statuses must agree within the pass bar.

---

## 7. What this means for Spaarke AI and JPS

### 7.1 JPS relevance: yes, as the platform twin of an Agent Skill

Cowork does not read JPS, and JPS never goes inside a skill. But a JPS Action and an Agent Skill describe the same thing in two dialects:

| JPS section | Agent Skill equivalent |
|---|---|
| `instruction` | `SKILL.md` body (workflow and evaluation steps) |
| `input` | Intake questions and attorney-profile defaults |
| `output` (structured) | The review-result / edit-plan schema |
| `scopes` (`$ref` Skills, Knowledge) | `references/` files |
| `$choices` | The topic catalogue and status values |
| `examples` | Worked examples, and eval cases |
| `metadata` | Frontmatter `metadata` (version, category) |

R1 generates JPS Action files from the canonical content, validates them with `jps-validate`, and maps them to the existing Spaarke content for the same practices. Deploying them waits for a later project.

### 7.2 Proposed adjustments to Spaarke's AI approach `[Judgment]`

These are for discussion. None of them changes platform code in R1.

1. **Treat Spaarke's existing content as an untested candidate, not as the seed.** The catalogue (`.claude/catalogs/scope-model-index.json`, generated 2026-07-10; refresh it with `jps-scope-refresh` first) holds content for five of the six practices (Legal Intake compares against the Spaarke service-request types instead):
   - **Actions:** NDA Analysis, Employment Agreement Review, Lease Agreement Review, Invoice Processing, Contract Review, Compare/Analyze Clauses, and the IPPAT × Patent Application extract.
   - **Skill scopes:** NDA Review, Employment Contract, Lease Review, Invoice Processing, Clause Comparison.
   - **Knowledge:** NDA Review Checklist, Employment Law Quick Reference, employment-law, Lease Agreement Standards, invoice-validation-rules, Invoice Processing Guide, IP Assignment Clause Library.
   - **Tools:** SYS-Clause Analyzer, SYS-Clause Comparison, SYS-Invoice Extractor.

   None of this has been evaluated against a corpus or reviewed by an attorney. Copying it into Agent Skills would carry its unknown quality onto a second surface. It is therefore compared against the researched sources (§8.5), and **the comparison may change Spaarke as much as it changes the accelerators** (§7.3). The topic catalogue becomes the shared vocabulary, and Spaarke's output schemas converge on the review-result schema.
2. **Make positions first-class.** Spaarke's AI has prompts, fragments, knowledge and tools, but no first-class *customer legal playbook*. CUST- Knowledge can hold one today. The proper home is the ontology project's Policy versions (strategy T3), exporting to the Markdown and `.docx` files the Microsoft surfaces read.
3. **Adopt the review-result schema** as the output contract of Spaarke's legal review Actions. A review done in Cowork, in Word or in Spaarke can then be logged, compared and reported the same way.
4. **Load knowledge on demand.** Spaarke composes skill fragments and inline knowledge into one system prompt, within an 8,000-token budget. Cowork loads references only when needed. For long checklists and position sets, Spaarke could use a "read reference" tool pattern instead of growing the composed prompt.
5. **Mechanics in code.** This matches the Cowork engine rule. Expose Spaarke's clause, invoice and comparison tools through the connector, so that at T2 Cowork uses the same implementation.
6. **Terminology.** Propose to the ontology component model (§3 there) that the `sprk_analysisskill` scope gets a user-facing name that doesn't collide with "Agent Skill", e.g., "Guidance fragment". The engineering identifier stays.
7. **Agent Skill export.** Later, an "export Action as Agent Skill" path would let a Spaarke customer take platform content into their own Cowork. This is a T3 feature.

### 7.3 Comparing Spaarke content with researched content (the feedback loop)

For each practice, before its Agent Skills are written:

1. **Inventory.** Export the practice's Spaarke Action, Skill, Knowledge and Tool content from Dataverse, read-only, to `practices/<p>/spaarke-baseline/` as a dated snapshot.
2. **Map** each Spaarke checklist item, output field and rule to a topic ID in the draft `topics.yaml`, which is built from the researched sources (§8.5). Record three lists:
   - topics Spaarke lacks;
   - Spaarke items with no researched counterpart;
   - conflicting positions.
3. **Evaluate both on the same corpus and rubric (§13).**
   - Run the Spaarke Action through the existing offline harness (`infrastructure/ai-foundry/evaluation/`); this needs no deployment.
   - Run the draft Agent Skill on the same documents.
   - Score both on topic recall, precision, fabricated authority and status agreement.
4. **Decide per topic.** Use whichever content scores better and has attorney approval. An item survives only if an attorney approves it, whatever its source.
5. **Feed back.** Record the findings in `notes/spaarke-content-comparison-<p>.md`. File each Spaarke defect or gap as a GitHub issue (`/project-defer-issue-tracking`); examples are a missing topic, a wrong position, a schema that doesn't fit, or a tool that miscounts. Fixing them belongs to the Spaarke platform backlog or the T3 convergence project, not to R1 (SC-15 still holds).

The result: the accelerators never inherit untested Spaarke content, and Spaarke gets an evaluated, attorney-approved target to converge on.

---

## 8. Practice accelerators

### 8.1 Core (Wave 0)

| Component | Purpose |
|---|---|
| `accelerator-setup` skill | Onboarding: environment check, choosing and installing skills, legal playbook, personalization, trial run, refinement, automations. Re-runnable (§8.1a). |
| `accelerator-help` skill | What is installed, which skills do what, which standards and versions are active, **which flow goes where (§2.4.4)**, how to update, the disclaimer |
| Shared references | `standards-protocol.md`, `verification.md`, `disclaimer.md`, `review-result.schema.md`, `edit-plan.schema.md`, `word-or-cowork.md` |
| Engines | §5.6 |

### 8.1a Onboarding: `accelerator-setup` and its tools

**Why it is needed.** Microsoft ships no setup or onboarding skill for either legal plugin `[Cited]`. Without one, an attorney has to do five things by hand:
- read an install guide;
- copy folders;
- decide which skills they need;
- write a legal playbook in our format;
- work out where defaults live.

That is the point at which T0 adoption fails.

**Prior art:**
- **`anthropics/claude-for-legal` `cold-start-interview`** (Apache-2.0), 13 practice variants:
  - quick (≈ 2 min) and full (≈ 15 min) paths;
  - `[PLACEHOLDER]` / `[DEFAULT]` / `[PENDING]` markers, with pause and resume;
  - positions extracted from seed documents, noting where signed deals departed from stated positions;
  - a prose profile ("Don't write YAML");
  - `--redo`, `--side`, `--check-integrations`.
- **Its `legal-builder-hub`**:
  - `skill-installer` shows the raw `SKILL.md`, runs a trust check and QA, and installs only after an explicit yes;
  - `skills-qa` and `customize` cover checking and changing one section.
- **Anthropic `skill-creator`** (Apache-2.0): the intent interview and description tuning.
- **Cowork's built-in Skill Management**: creates and validates skills and writes a Skill Report.
- **`write-like-me`** (CC BY-SA): a saved writing-voice profile. Its approach can be reused; its text cannot, because of the ShareAlike terms.

We port the *pattern*, re-pointed from Claude's `~/.claude/...` paths to OneDrive files and Cowork custom instructions.

**Flow.** Each step is one native question card (≤ 7 options, always Skip and free text). Steps record markers, so the user can stop at any point and resume later.

| Step | What happens | Tools (scripts) and built-ins | Writes |
|---|---|---|---|
| 0. Welcome and path | Explains what the accelerator does and doesn't do, and the Word-or-Cowork split (§2.4.4). Offers **Quick** (defaults, about 3 minutes) or **Full** (about 15 minutes). | — | — |
| 1. Check the environment | Checks: Python and `lxml` in the sandbox; Legal for Cowork present; skill count against the 50 cap; OneDrive reachable; the T1 library URL, if any; whether the user has Word for Windows. Reports ✓ only for what actually succeeded. | `probe.py`; Cowork's skill list | Results in the profile |
| 2. Role and practice | Firm or department; practice areas; usual side and posture; jurisdiction default | — | Profile: Who / Practice |
| 3. Choose and install skills | Recommends skills from the answers and shows each one's purpose and "do not use for" lines. Installs through **Skill Management** (preferred), or gives copy or upload instructions. Warns at 45 of 50 skills. | Skill Management `[Verify]` S14; `accelerator-help` | Installed skills; install log in the profile |
| 4. Legal playbook | Four routes:<br>(a) **use the group's** from the T1 library;<br>(b) **import** an existing `.docx` or `.md`;<br>(c) **build from precedents**: Legal for Cowork `create-playbook`, or extract positions from 5–10 signed agreements and note where they departed from stated positions;<br>(d) **start from baseline positions**.<br>Every route ends in our Markdown format with topic IDs, and in a rendered Word playbook. | `playbook_import.py`, `playbook_parse.py`, `render_word_playbook.py`; `create-playbook` | Legal playbook `.md` + Word playbook `.docx` |
| 5. Personalize | Defaults (side, posture, governing law, output set, email behaviour, house style). **Optional:** a writing-style sample. | `profile_check.py` (no silent gaps; markers valid); `instructions_block.py` (composes the custom-instructions block and checks it is ≤ 20 KB) | Attorney profile `.md`; a **custom-instructions block for the user to paste** into Customize › Preferences (no skill can write it); optional Project instructions for a practice group |
| 6. Try it | Runs the main skill on the shipped sample document (e.g., the POC test NDA) or one of the user's own, then shows the outputs and the provenance line | The practice skill and engines | Sample outputs in `OneDrive/Cowork/` |
| 7. Refine | Asks "What did I get wrong?" and changes positions or defaults topic by topic, then re-renders the Word playbook | `playbook_parse.py`, `render_word_playbook.py` | Updated playbook and profile, with the version bumped |
| 8. Automations (optional) | Offers the practice's recipes (§5.8) with the trigger and the pre-approval scope written out; the user creates the automation | — | Recipe text |
| 9. Wrap-up | Summary of what was set up, open `[PENDING]` items, and how to change things later | — | Profile summary |

**Re-running.**
- `/accelerator-setup` with no options resumes `[PENDING]` steps.
- `--redo` re-runs everything and shows a diff before overwriting.
- `--section playbook|personalize|skills|automations` re-runs one step.
- `--check` repeats step 1 only.

**Practice modules.** A skill can read only its own folder. So the build copies each installed practice's `onboarding.md` into `accelerator-setup/references/`. Each module holds the practice-specific interview questions, the recommended skills and the sample document. Five practices fit within the 20-file limit.

**Guardrails:**
- Onboarding never sends mail.
- It never installs without an explicit yes after showing what will be installed.
- It never overwrites a playbook or profile without showing a diff.
- It records every default it chose as `[DEFAULT]`.
- It keeps the interview lean, because authoring and evaluating skills bills credits.

**MVP status** `[Tested]` T-05:
- The Quick path ran: two question cards, the probe, a profile written after one approval card, the instructions block, and a trial review.
- S3 is answered: the profile lands where asked and is read back unprompted in a new session.
- Still to build: the Full path, routes 4a–4c, `--redo`, the diff on overwrite, and `playbook_import.py`.

**What remains unverified** (spikes):
- whether a skill can drive Skill Management (S14);
- whether plugin skills count toward the 50 cap (S15).

Each has a fallback:
- installing: copy or upload instructions;
- the profile: the custom-instructions block;
- the cap: fewer, broader skills.

### 8.2 NDA: the reference implementation (from the POC)

The NDA Accelerator's first cut comes from the POC:
- the skill draft (Appendix A);
- the legal playbook format and starter positions (Appendix B);
- the edit-plan schema (Appendix C);
- the engines (Appendices D–E).

Changes on the way in:
- **Topic IDs** aligned to `topics.yaml`.
- **Baseline by side**: split into disclosing, receiving and mutual, and **attorney-reviewed**. The POC's "aggressive, Florida law" positions stay as a sample customer playbook, not the baseline.
- **Two redline outputs**: internal (with the summary page) and external (without). This was a new requirement in POC §4.9 #7.
- **Eval cases**: the POC's 10 behaviour tests.

The POC's test NDA (RPX / Helios IP) may be kept for informational and test use.

**MVP status (2026-11-02)** `[Tested]`:
- **Built:** `src/accelerators/` holds the `nda-review` and `accelerator-setup` skills, the engines, the build and gates, the mutual baseline (24 topics), the Word renderer, and an eval corpus (nda-01 to nda-03, msa-01) with a golden plan.
- **Proven in Cowork** (T-05 to T-11, S9, S10):
  - the internal redline (page-1 linked summary), external redline and clean copy;
  - side and posture overrides;
  - counterparty revisions kept;
  - prompt injection quoted and refused;
  - "don't email" honoured;
  - MSA routed away.
- **Open before release:**
  - **L9:** `unchanged` prints topic IDs and reasons, so page 1 spills to page 2;
  - **L10/L17:** output location;
  - **L15:** a reused plan;
  - **L16:** playbook discovery;
  - **the receiving and disclosing baselines;**
  - **model language for 19 of 24 topics**, plus a **named approving attorney** (H2, OD-5);
  - **R16:** does the clean copy accept the other side's changes? Owner decision pending;
  - **L13:** run-to-run variance. Option: one default action per topic in the playbook, plus a per-topic tolerance in `expected/`.

**Candidate additions from the expert demo** `[Proposed]` (OD-24):
- **Excel audit pack:** Change Log, Risk Summary, Human Review Checklist and Coverage Summary. An engine generates it deterministically from `edit-plan.json`, so it costs no model work and matches the redline exactly.
- **Privilege header** on the internal redline only (attorney decision on wording).
- **Plan-preview card** before files are generated (§5.7-15).

### 8.3 The six accelerators (roadmap; owner decision 2026-10-31; R1/R2 split `[Proposed]` OD-21)

*v0.5:* the set and order stand. R1 delivers **NDA** only. **Legal Intake** and **Invoice Review** (Wave 1 in v0.4) move to R2, followed by Wave 2 and Wave 3. The table below is kept for planning.

| | **NDA** | **Legal Intake** | **Invoice Review** | **Commercial Real Estate** | **Employment** | **U.S. Patent Applications** |
|---|---|---|---|---|---|---|
| **Wave** | 1 (reference) | 1 | 1 | 2 | 2 | 3 |
| **Skills (proposed)** | `nda-review` (redline + summary + clean); `nda-triage` (GREEN/YELLOW/RED for high volume) | `legal-intake` (question cards by request type → structured request → **email to the designated intake mailbox**); `intake-from-email` (turn a forwarded business email into the same request) | `billing-guidelines-to-rules`; `invoice-compliance-review` (LEDES 1998B or PDF → Excel exception report, arithmetic by engine); `reduction-letter-draft` | `lease-review` (tenant or landlord; office/retail/industrial); `lease-abstract` (Excel abstract, critical dates as formulas) | `employment-agreement-review` (either side); `restrictive-covenant-check` (state reference); `separation-agreement-review` (OWBPA timing via `calc_dates`) | `application-qc` (claims antecedent basis, dependency, numbering, spec support, reference numerals, §112(b) watch-terms, abstract); `invention-disclosure-intake`; `office-action-outline` |
| **Word playbooks** | Mutual; disclosing; receiving | n/a (request catalogue) | n/a (rules file) | Office (tenant); office (landlord); retail (tenant) | Executive agreement; offer letter; separation | n/a (QC checklist) |
| **Content sources** | See §8.5 | See §8.5 | See §8.5 | See §8.5 | See §8.5 | See §8.5 |
| **Spaarke candidate (compare, §7.3)** | NDA Analysis + NDA Review Checklist | The Spaarke service-request / Legal Front Door request types and fields | Invoice Processing + invoice-validation-rules + Invoice Extractor (accounts-payable focus) | Lease Agreement Review + Lease Agreement Standards | Employment Agreement Review + Employment Law Quick Reference | IPPAT × Patent Application extract |
| **We must author** | Baseline positions per side; firm and in-house variants | Request-type catalogue; required fields per type; urgency and routing hints; acknowledgement text; mailbox configuration | Legal-billing rules (block billing, vague entries, rates, staffing, UTBMS); check the UTBMS/LEDES licence | Full U.S. checklists (CAM/opex, SNDA, estoppel, co-tenancy, exclusives, TI, holdover); abstract columns | State restrictive-covenant and notice references (dated, cited, refresh calendar) | Most of it; in-house patent expertise is the asset |
| **Engine needs** | Core only | `intake_record.py` (validates the request against the catalogue and renders the email body plus a machine-readable block) | `calc_amounts`, LEDES parser | `calc_dates`, `calc_amounts`, Excel formulas | `calc_dates` | Claim parser (numbering, dependency, antecedents) |
| **Effort** | S (POC exists) | S–M | L | L | M | L |

**Legal Intake output.** The deliverable is **an email to a designated mailbox**, set in the attorney profile or the T1 library (e.g., `legal-intake@<org>`). It has three parts:
- a human-readable summary;
- the attachments the requester supplied;
- a fenced, machine-readable block (request type, fields, requester, urgency, matter or counterparty references).

That keeps T0 free of any Spaarke dependency, and it gives every later tier something to parse:
- at T1, a mailbox rule or Power Automate flow can route the email;
- at T3, the mailbox can be a Spaarke-monitored intake mailbox, where Spaarke's email ingestion creates the service request through the server-side write path (ADR-002).

Sending follows §5.7-8:
- the requester sees the email and approves it;
- automations may be pre-approved for the intake mailbox address only.

**On the set** `[Judgment]`:
- **Six accelerators in three waves:**
  - Wave 1 (NDA, Intake, Invoice Review) is the department-operations core: high volume, measurable, and Invoice Review and Intake aren't covered by Microsoft's legal skills.
  - Wave 2: CRE and Employment.
  - Wave 3: U.S. Patent Applications.
- **Invoice Review** is the most differentiated.
- **Legal Intake** is the front door that later carries customers to the Spaarke connector (§9.2).
- **U.S. Patent Applications** suits engines, because claim-structure checks are mechanical.
- **Alternate:** Privacy/DPA, which has the richest open-source seeds.
- **Rejected:** MSA review and litigation chronology. Both duplicate Legal for Cowork skills.

### 8.4 Content governance

- **Attorney sign-off gate.** No baseline legal playbook, state reference or checklist ships without a recorded approval from a named attorney (OD-5).
- **Dated references.** Every legal reference file carries an as-of date and its sources, and joins a refresh calendar.
- **Licensing:**
  - MIT content keeps "Copyright (c) 2026 Mike" and each skill's author.
  - Apache-2.0 content keeps its licence and NOTICE.
  - Modified files are marked, and each derived skill has a `references/THIRD-PARTY-NOTICES.md`.
  - No upstream trademarks in names.
  - Spaarke-originated content carries `license: Proprietary`.
- **Datasets:** CUAD and ContractNLI are CC BY 4.0 and allowed. (v0.2 wrongly called ContractNLI non-commercial; only one Hugging Face mirror mislabels it.) Non-commercial or ShareAlike datasets are excluded (§8.5).
- **Provenance record.** Adopt OpenAgreements' per-template `metadata.yaml` fields as our record for every adapted file: `source_url`, `license`, `allow_derivatives`, `attribution_text`, `source_sha256`.

### 8.5 Where skill content comes from

Source: researcher note `legal-skill-content-sources-2026-10-31`. It covers GitHub, Hugging Face, standards bodies and government sources, with licences checked at the source on 2026-10-31.

**How content is assembled.** Each `SKILL.md`, reference file and legal playbook is built from five kinds of source, in this order of trust:

| Tier | Kind | Examples | We take |
|---|---|---|---|
| 1 | **Primary law and official guidance** (public domain or freely reusable) | U.S. Code, CFR, EEOC guidance, MPEP, USPTO data, GSA lease forms | Rules, citations, checklists, dated as-of |
| 2 | **Open standard agreements** | Common Paper and Bonterms (CC BY 4.0), Series Seed (CC0) | Baseline clause positions and model language, with attribution |
| 3 | **Open-licensed skill repositories** (Apache-2.0 / MIT) | OpenAgreements, claude-for-legal, mike-workflows, CaseMark, LegalQuants, cre-skills-plugin | Process structure, checklists, playbook schemas, extraction fields. Never positions without review. |
| 4 | **Open datasets** (CC BY 4.0 / public domain) | CUAD, ContractNLI, LegalBench (licensed tasks), ACORD, USPTO research datasets | Eval cases and topic taxonomies, not skill text |
| 5 | **Spaarke's existing content** | §7.2-1 | Compared, not copied (§7.3) |

What remains is **written by our attorneys**: positions, thresholds, escalation rules and state tables. Every item needs attorney approval, whatever its source (§8.4).

**Per-practice register.** This table seeds `practices/<p>/sources.md`. The commit SHAs and versions are those checked on 2026-10-31.

| Practice | Best sources (licence) | Evals | We must author |
|---|---|---|---|
| **NDA** | **OpenAgreements** `skills/agreements/nda` and NDA guides (Apache-2.0, `50ae131`); **Common Paper** Mutual and One-way NDA, **Bonterms** NDA (CC BY 4.0; Bonterms' licence must be checked per document); **LegalQuants** `skills/playbooks/nda*.yaml` playbook schema and 8 seeded positions (Apache-2.0, "not vetted"); mike-workflows `nda-review` (MIT); claude-for-legal NDA process (Apache-2.0); 18 U.S.C. §1833(b) DTSA notice; SEC Rule 21F-17 carve-out. oneNDA as a **benchmark only** (CC BY-ND: no derivatives). | **ContractNLI** (607 NDAs, 17 hypotheses, CC BY 4.0); LegalBench `contract_nli_*`; CUAD; the POC NDA | Positions by side (disclosing / receiving / mutual), residuals policy, choice-of-law table by state, DTSA and 21F-17 language, escalation thresholds |
| **Legal Intake** | **SALI LMSS** taxonomy for matter and request types (MIT); the Spaarke service-request / Legal Front Door request types (candidate, §7.3); public legal-department intake practice (to be researched in Phase 0: CLOC and similar, licence to be checked) | Synthetic requests per type, including incomplete and misrouted ones | The request-type catalogue, required fields, routing hints, acknowledgement text |
| **Employment** | **OpenAgreements** 56-jurisdiction non-compete survey (16 structured columns), state restrictive-covenant and offer-letter templates (CC BY 4.0), hiring and invention-assignment checklists, and a non-compete *currency* eval; **EEOC** OWBPA Q&A (EEOC-NVTA-2009-2: 7 requirements, 45-day disclosure, checklist and sample disclosure table) and 29 CFR 1625.22; **CaseMark** separation (OWBPA 21/45/7-day table), non-compete, contractor and executive drafting maps (Apache-2.0); claude-for-legal `worker-classification` (Apache-2.0); DOL contractor NPRM 2026-02-27 (no final rule yet) | `cedarstoneventures/us-noncompete-enforceability` (CC BY 4.0, 76 holdings, 30 states); CUAD non-compete and no-solicit; EDGAR executive agreements | Covenant positions by state tier, a group-termination disclosure check, a classification decision tree that follows the pending DOL rule, NLRB *McLaren Macomb*-compliant language, state WARN overlays. Firm 50-state surveys are © and for reference only. |
| **Commercial Real Estate** | **GSA** Global Lease L100 (rev. May 2026) and Form 3517B (public domain); **mariourquia/cre-skills-plugin** lease abstract (30+ fields; retail adds co-tenancy, kick-out, radius, percentage rent), estoppel generator and CAM reconciliation (Apache-2.0, `e4597f1`); **CaseMark** office, retail, industrial, NNN and ground lease, SNDA and estoppel maps (Apache-2.0); mike `commercial-lease-review` (MIT; UK law, structure only) | SEC EDGAR Exhibit 10 leases (fine for evals, but filer copyright limits verbatim reuse); LEDGAR via LexGLUE; ACORD | **All substantive positions** (office / retail / industrial × landlord / tenant), the abstract schema (aligned with the Spaarke ontology), estoppel and SNDA checklists, area sanity rules (cite BOMA by name; the standard is paid), escalations and caps. No open U.S. lease checklist of good quality exists. |
| **Invoice Review** | **LEDES 98B** spec and sample (free, © LEDES Oversight Committee; "LEDES®" may not appear in product names); **UTBMS** code sets, including the **IP Patent Prosecution Codes** (Apr 2023, updated Jun 2024); **public outside-counsel guidelines**: FDIC Deskbook (rev. Nov 2025), NJ AG, City of Chicago (2024), Penn State (2025); **Ditzler** outside-counsel billing reviewer schema, metrics and failure modes (Apache-2.0, inside the otherwise NC-ND `lawve-ai` repo); judicialmind `billing-operations` (MIT) | **None public.** Build synthetic LEDES 98B invoices from the LEDES sample and UTBMS sets, with planted violations. | The billing-rule set and severity matrix, appeal and reduction-letter templates, expected UTBMS codes by matter type, the synthetic eval corpus. Check the LEDES and UTBMS reuse terms before shipping code tables (`assets/utbms.json`). |
| **U.S. Patent Applications** | **MPEP** 9th ed. rev. 01.2024 plus the 2025 memos (including *Desjardins* §101); 35 U.S.C. §112; **WIPO Patent Drafting Manual** 2nd ed. (CC BY 4.0); LEDES IPMM invention-disclosure schema (free); claude-for-legal `ip-legal` (Apache-2.0); MIT `claims_analyzer.py` as a reference for checks | **USPTO** Office Action Research Dataset 2008–2017, PatEx, Patent Claims Research Dataset (public domain); BIGPATENT (CC BY 4.0); USPTO published applications | §112(a), (b) and (f) QC checklist, claim-set architecture rules, office-action response strategy per rejection type, disclosure interview questions, §101 flags. Most of it; in-house patent expertise is the asset. The open patent-drafting skills found use KR/JP claim style. |

**Discovery, not sourcing.** `ThomasMoreAI/legal-skills-open` (Apache-2.0) indexes about 18,000 mirrored skills (`us/employment` 81, `us/real-estate` 92, `us/ip` 168). Use it to find skills, then take them from the upstream repo under the upstream's own licence.

**Do not use** (licence traps found):

| Source | Problem |
|---|---|
| `lawve-ai/awesome-legal-skills` | **CC BY-NC-ND** at repo level, which most of its skills inherit, including the 7k-word invoice-review skill. Its NDA reviewer is **AGPL-3.0**. Only the Ditzler billing reviewer is usable (Apache-2.0). |
| `borghei/Claude-Skills` | MIT + **Commons Clause**: not for commercial use |
| `TapnTell-AI/awesome-legal-aiagent-skills`, other unlicensed re-hosts | No licence: no rights |
| oneNDA / oneSaaS / oneDPA; YC SAFE | No derivatives (ND); benchmark only |
| NVCA models | Free to use, not to redistribute |
| HUPD | Licence conflict (SA vs NC-SA); treat as non-commercial |
| LegalBench `learned_hands_*` (NC-SA), `consumer_contracts_qa` (NC), and tasks with no licence line | Excluded |
| Pile of Law and other NC datasets; `ledestool` (GPL-3.0); `lexdays` (AGPL-3.0) | Excluded |
| Firm 50-state surveys; BOMA; AIR CRE; ABA and ACC forms | © or paywalled: reference only, cite by name |

**Process per practice** (Phase 0 for NDA, then each Wave):
1. Pin each source in `third_party/<source>@<sha>/`, with its LICENSE and a provenance record (§8.4).
2. Draft `topics.yaml` from tiers 1–3, then map tier 4's taxonomies onto it (e.g., CUAD's 41 categories, ContractNLI's 17 hypotheses).
3. Run the Spaarke comparison (§7.3).
4. An attorney writes and approves the positions and the "we must author" items.
5. Build eval cases from tier 4 plus public documents (§13), written down before the skill is run.

---

## 9. Forms and interactive UI in Cowork

### 9.1 The four options

| Option | What it is | Who builds and hosts it | Can it do intake? | Tier |
|---|---|---|---|---|
| **Question card** | Cowork asks the user structured questions (choices, text) when a skill says to. The POC's intake was one card with five questions `[Observed]`. | Nothing to host; written in `SKILL.md` | Yes, for fixed options. It can't look up Spaarke data. | T0 |
| **Adaptive Card** | Cowork's built-in render-ui skill draws a card (tables, KPIs, charts), e.g., a risk dashboard. | Nothing to host; the skill asks for it | Display only. Whether buttons send anything back is unverified (S7). The expert demo rendered a donut chart, KPIs and a clause table from a skill before generating files; that is the pattern for the plan preview (§5.7-15). | T0 |
| **MCP elicitation form** | Our MCP tool asks Cowork to collect input against a JSON schema (text, number, true/false, pick-list), and Cowork renders a native form `[Cited]`. | Our MCP server (connector project) | Yes, for simple flat forms whose options can come from Spaarke at call time | T2 |
| **MCP App** | An open extension of MCP. A tool declares an HTML UI resource, and Cowork renders it inline or full screen in a sandboxed frame. The UI talks to the host through a message bridge, can call our tools, and can send results back into the conversation `[Cited]`. | Our MCP server serves the UI (React/Fluent v9; we already have the `widget-design` skill and `knowledge/mcp-apps/`) | Yes: dependent pick-lists, type-ahead look-ups (client, matter, practice area), validation, file attach | T2 |

SPFx Copilot UX components don't render in Cowork. They need an admin, and they render only in Copilot Chat and Teams.

### 9.1a How MCP Apps work in Cowork

Source: the Microsoft Learn page [MCP apps plugin author guide for Cowork](https://learn.microsoft.com/en-us/copilot/cowork/mcp-apps-support) (updated 2026-10-21) `[Cited]`. It implements the open MCP Apps standard SEP-1865 (spec 2026-01-26; SDK `@modelcontextprotocol/ext-apps`). The page carries no GA or preview label.

**How a widget gets on screen:**
1. Our tool declares `_meta.ui.resourceUri` (`ui://spaarke/...`) and returns **data** (text or `structuredContent`), never HTML.
2. Cowork mounts a widget for that call and passes it the result.
3. Cowork fetches the HTML through `resources/read`:
   - MIME type `text/html;profile=mcp-app`, in the `text` field;
   - **self-contained**: scripts and styles inline, assets as `data:` URLs. Cowork hosts nothing.

   For us, that means a single-file build of the Fluent v9 widget.

**What a widget can do:**

| Capability | Detail |
|---|---|
| Call our tools | `tools/call` to our own server. Only tools whose `visibility` includes `"app"` |
| Read our resources | `resources/read` on our own server |
| Post to the conversation | `ui/message` arrives as a user turn |
| Change display | `inline` or `fullscreen` only |
| Open links | `https` only, after the user confirms. Never the only way to finish a flow |
| Read host context | Theme, locale, time zone and style variables |

**What a widget can't do:**
- update the model's context;
- run elicitation;
- receive streaming or server-pushed updates;
- reach other servers.

**Security fits the BFF model:**
- **Isolation.** The widget runs in a sandboxed, cross-origin frame. Widgets from the same server share one origin, so they must not put anything in storage that another of our widgets shouldn't see.
- **Credentials.** Cowork brokers every call over an authenticated session and **attaches the user's auth upstream**. Credentials never reach the widget, so the BFF token stays server-side.

**Network:**
- Any direct network access must be declared in `_meta.ui.csp`:
  - `connectDomains` is required whenever any CSP is declared;
  - also available: `resourceDomains` and `frameDomains`;
  - at most 16 origins per directive.
- For a published package, every declared domain must also appear in the manifest's `validDomains[]`. Undeclared domains are stripped and the user sees a "widget network blocked" card.
- **Preferred:** route everything through `tools/call` and declare no domains.

**Limits:**

| Limit | Value |
|---|---|
| Inline tool result | 64 KiB. Larger results arrive without data; the widget then fetches through `tools/call` |
| Widget calls | 60 per minute per conversation |
| `ui/initialize` | Must finish within 10 s |
| Inline size | 200–720 px wide, up to 640 px tall; use fullscreen for larger |

**Design rules for our widgets:**
- Every tool returns useful data even when no widget renders; display widgets include a text summary.
- App-only tools are marked `visibility: ["app"]`.
- Elicitation handles confirmations, short pick-lists and flat forms. Widgets handle searchable pickers, look-ups, grids, dashboards and previews.
- Files from the Cowork workspace reach our tools as base64 parameters (`contentEncoding: base64`), with no widget needed.

Applied to Legal Front Door (§9.2):
- a fullscreen intake widget;
- look-ups through app-only tools;
- submit through a model-visible tool that calls the BFF write path;
- a compact `structuredContent` confirmation.

### 9.2 Worked case: Legal Front Door / Service Request intake

**T0/T1, no Spaarke (R2 under OD-21, §8.3):** the `legal-intake` skill asks question cards (request type, then that type's fields: urgency, counterparty, deadline, attachments). It then emails the structured request to the **designated intake mailbox**, with the requester's approval. At T3 that mailbox can be a Spaarke-monitored intake mailbox.

**T2, with the connector:**
1. The `spaarke_submit_service_request` tool returns an **MCP App** form, built in Fluent v9 and reusing our existing service-request wizard components where practical.
2. The form's look-ups (client, matter, practice area, requester) call BFF read endpoints through the connector.
3. Submit calls the BFF write path, where validation, routing and assignment happen server-side (ADR-002).
4. The confirmation comes back as a card with a deep link.

For a lighter build, an **elicitation form** covers fixed-option intake with no custom UI.

**Event triggers:** once the connector publishes it, "service request assigned to me" can become a Cowork automation trigger.

Building this is the connector project's scope. R1 records the pattern so that skills are written to hand off to it.

---

## 10. Spaarke connection (next project, recorded here)

- **SharePoint Embedded is unreachable through Cowork's built-in Graph access.** Both container listing and file reads returned 403 `[Observed]`. Spaarke's container type has discoverability off. Turning it on would expose content across Microsoft 365, so that is not the route `[Cited]`.
- **The route is a Spaarke MCP connector hosted by the BFF**, because the BFF is the single backend (CLAUDE.md §10). It is not a separate "Document API" and not a Power Platform custom connector.
  - The plugin's `agentConnectors` entry points at the BFF's MCP endpoint.
  - It signs in through a fixed Entra OAuth configuration (`OAuthPluginVault`), because Entra has no dynamic client registration.
  - An attorney can upload and share it, and each user consents `[Cited]`. Tenants that restrict user consent need an admin.
- **Tools.** These come from POC Appendix H and carry read-only or destructive annotations. Write tools take an explicit target, so automations can be pre-approved narrowly.
  - `spaarke_find_documents`
  - `spaarke_get_document`: returns the file to Cowork as base64 (≤ 8 files and 150 MiB per call `[Cited]`)
  - `spaarke_save_version`
  - `spaarke_create_document`
  - `spaarke_log_analysis`: takes the review-result schema
  - `spaarke_matter_card`: an MCP App
  - the intake tools in §9.2
- **Writes go only through the BFF write path** (ADR-002). The POC proposed logging reviews with the Dataverse connector's "create row" action; that is **rejected**, because it bypasses the server-side owner of the record's rules. Read-only Dataverse access from Cowork is fine.
- **Auth:** on-behalf-of the user, with Spaarke permissions enforced, in line with the Auth v4 direction. User-initiated calls have no superuser path.
- **Before design:** the connector project needs a placement decision, a hot-path declaration and BFF publish-size measurements (CLAUDE.md §10).

---

## 11. Claude Code knowledge and tooling (Workstream K, first)

Cowork, Agent Skills in Microsoft surfaces and Copilot for legal all shipped in 2026, after Claude's training data. Without this workstream, Claude Code tends to:
- confuse Microsoft's Cowork with Anthropic's "Claude Cowork";
- add manifest fields the schema rejects.

Workstream K follows `knowledge/README.md` conventions.

*v0.5 status:* not started. The NDA MVP was built from the researcher notes in `.claude/agent-memory/researcher/` (2026-10-29 → 11-02) instead. K1–K3, K5 and K7 are now the first spec tasks, written from tested facts (results.md), not just documentation. K4 (declarative agents) shrinks to MCP Apps only. K8 pins only the sources the NDA accelerator actually uses.

| # | Deliverable | Content |
|---|---|---|
| K1 | `knowledge/cowork-plugins/` | **Learn pages:** use-cowork, cowork-customize, cowork-plugins, cowork-plugin-development, cowork-manage-plugins, mcp-apps-support, cowork-elicitation-forms, cowork-admin-governance, cowork-projects, cowork-faq, whats-new. **Samples:** `microsoft/copilot-camp` `src/cowork/` and CWRK01. **Tool docs:** `wiqd` plugin docs. |
| K2 | `knowledge/agent-skills-spec/` | **Spec:** [agentskills.io](https://agentskills.io/home) (six frontmatter keys, naming, progressive disclosure, `references/`, `scripts/`, `assets/`). **Guidance:** Anthropic skill-authoring practice. **Pinned:** `skills-ref` @ `69ef37e9`; `cat-agent-skills/_template` @ `3678d23a`. |
| K3 | `knowledge/copilot-for-legal/` | **Microsoft pages:** admin page, support get-started, transparency note, MC1509799. **Our notes:** the 20 Legal for Cowork skills; Word playbook facts; terminology (§2). |
| K4 | Extend `knowledge/mcp-apps/`, `knowledge/declarative-agents/` | MCP Apps and elicitation in Cowork; Agent Builder skill limits |
| K5 | `.claude/skills/cowork-plugin/` | **Packaging:** install rules, limits, manifest templates, connector auth (`OAuthPluginVault`), validation commands. **Runtime facts** from the spikes. **Pitfall list** of stale knowledge. |
| K6 | `.claude/skills/practice-accelerator/` | **Model:** content model and alignment keys (§6); legal playbook format; engine rules; skill section pattern. **Checks:** overlap check against Legal for Cowork; eval and licence checklists. **Lessons:** POC Appendix J and the OOXML gotchas. |
| K7 | Registration | `.claude/skills/INDEX.md`; a CLAUDE.md §17 trigger row ("Before you build a Cowork skill, plugin or practice accelerator → `cowork-plugin`, `practice-accelerator`"); knowledge README; `REFRESH-LOG.md`. Refresh monthly and after Ignite (17–20 Nov 2026). |
| K8 | `src/accelerators/third_party/` | Pinned upstream copies with LICENSE/NOTICE and provenance records: claude-for-legal @ `4a6c651`, mike-workflows @ `20bed20`, Lavern @ `7c2efe6`, OpenAgreements @ `50ae131`, CaseMark @ `51b5e32`, LegalQuants @ `755abc6`, cre-skills-plugin @ `e4597f1` (§8.5) |

The researcher notes from 2026-10-29/30 in `.claude/agent-memory/researcher/` are inputs to K1–K6. K5–K7 write under `.claude/`, so the main session must apply them (CLAUDE.md §3).

---

## 12. Verification spikes (test tenant)

**Prerequisite:** a test M365 tenant (OD-3) with:
- ≥ 2 M365 Copilot licences;
- a Cowork Copilot Credits spending policy;
- Word desktop for Windows;
- a SharePoint site;
- no Information Barriers.

The POC already answered parts of S2 and S9 in a Spaarke tenant. The NDA MVP (2026-11-01/02) answered most of the rest in the Spaarke tenant. Results live in `notes/mvp-nda/results.md` (S#, L#) and `run-log.md`; SC-2 points there.

| # | Question | Status (2026-11-05) | Decides |
|---|---|---|---|
| S1 | Claude-format conversion; folder skills vs plugin skills; non-admin upload when org plugins are disallowed | ✅ **Answered** (T-01 to T-03): upload works; the Claude format converts but loses name and version; folder skills run identically. The disallowed-policy case is still open (customer-like tenant) | OD-7 → unified package primary |
| S2 | Sandbox: Python, packages, renderers, egress, limits, reset | ✅ **Answered** (T-05): Python 3.12.15, lxml, python-docx, soffice, pdftoppm. Egress from scripts not tested (not needed) | §5.6 |
| S3 | Read the profile and playbook in a new session without attaching; where saves land | ✅ **OneDrive answered** (T-05 to T-07). The SharePoint read-only library → T-P10 | §5.3 |
| S4 | Updates on re-share; each recipient's own profile | 🔲 Open (needs a second user) | UC-1, UC-5 |
| S5 | Word playbook structure for the Legal plugin | 🟡 **Partly** (L24): imports 24/24 topics, but drops the Topic ID and fallback. Renderer reshaped; **A/B import retest owed** in Word desktop | §5.5 |
| S6 | `review-contract` with our playbook; `create-playbook` output format and location | 🔲 Open | §2.4.4; onboarding route 4c |
| S7 | Adaptive Card control and button feedback | 🟡 Rendering shown in the expert demo; buttons untested | §5.7-15, §9.1 |
| S8 | Engine-written Excel formulas survive delivery | 🔲 Open (R2 practices; also the audit pack, OD-24) | CRE, Invoice Review |
| S9 | Selection beside Legal for Cowork; "review this MSA" doesn't trigger ours | ✅ **Answered** (T-06, T-07, T-09) | §5.7-1 |
| S10 | Same skill on GPT and Claude | ✅ **Answered** (T-11): all pass; GPT needs guard rails (L15–L17); Sonnet ≈ 60% of Auto's credits | §5.7-10 |
| S11 | Unattended automation end to end | 🔲 Open | §5.8, SC-11 |
| S12 | Read-only Dataverse access in a customer-like tenant | 🔲 Open, gated with OD-11 | OD-11 |
| S13 | `edit-contract` fidelity | ✅ **Answered** (T-13): word-level tracked changes in place, no comments, summary or copies; edits the original | §5.6 positioning |
| S14 | A skill driving Skill Management | 🔲 Open | §8.1a step 3 |
| S15 | Do plugin skills count toward the 50 cap? | 🔲 Open | §8.1a |
| S16 | Specialist hand-off and panel | 🔲 Open (not needed for NDA) | §5.8a |
| S17 | Our plugin in Word | ✅ **Answered** (T-12b, retest): listed, never run (L21, L25). The admin-deploy variant → T-14 / T-P8 | §2.4.7 |
| S18 | Declarative agent in Word | ⛔ **Dropped**: out of R1 (owner, 2026-11-02) | — |
| S19 | Playbook persistence | 🟡 **Partly** (T-15): an attached playbook isn't remembered across chats. Saved-playbook reuse → T-P4 | §2.4.6 |

**Tests added by the component protocol (§8 there):**

| # | Question | Status |
|---|---|---|
| T-P1 | Inventory of what Microsoft Legal (Word, Cowork) and installed third parties do, mapped against our skills | 🔲 |
| T-P2 | Routing with Legal for Cowork **on** and Spaarke on: 5 prompts × 2 configurations | 🔲 (T-14) |
| T-P3 | Cross-plugin hand-off, K → E | Needed only if OD-20 chooses the split |
| T-P4 | Saved Word playbooks: offered in a new chat? Shareable? Centrally replaceable? | 🔲 |
| T-P5 | Build gate that detects positions inside skills | 🔲 |
| T-P6 | Does the Legal plugin import a Restrict-Editing-protected or labelled `.docx`? | 🔲 |
| T-P7 | Does Word load any instruction-only third-party skill? One-skill harmless probe plugin | 🔲 (with T-14) |
| T-P8 | Does admin deployment change where the plugin appears (Cowork, Word, PowerPoint "Organization")? | 🔲 (T-14) |
| T-P9 | Harvey's Word package | ✅ One MCP tool plus thin skills (L26) |
| T-P10 | Word import and Cowork read from a read-only SharePoint library | 🔲 |
| T-P11 | Default plugin-availability policy for partner publishers in a fresh, customer-like tenant (from the 2026-11-04 discussion) | 🔲 (customer-like tenant) |
| T-P12 | Python scripts still run after Ignite (17–20 Nov 2026): re-run `probe.py` | 🔲 |

---

## 13. Evaluation

**Method:**
- **Lavern's pre-registered rubric.** Expected issues are written down by topic ID before the run, then scored on recall, precision and hallucinations.
- **Anthropic's eval-first practice.** At least 3 cases per skill, including at least one negative case, each run with and without the skill.
- **The POC's behaviour cases:** no playbook, receiving side, do-not-send, already redlined, MSA must not trigger, unattended, prompt injection. C4–C7 passed on the first run `[Tested]` (L14).
- **Cases added from the MVP:**
  - a previous task's `edit-plan.json` is present and must be ignored (L15);
  - a Spaarke playbook outside the named folders must not be labelled "customer" (L16);
  - run-to-run variance on the same document, measured, with a per-topic tolerance in `expected/` (L13).
- **Credits per run** are recorded with `/cost` for each model (T-11: Sonnet 5.5 71, Auto 118, GPT 5.5 230).

**Corpus.** Only public documents go in the repo:
- SEC EDGAR exhibits, CUAD, ContractNLI and the CC BY LegalBench tasks for contracts;
- USPTO published applications, office actions and research datasets;
- synthetic LEDES 98B invoices with planted violations (no public corpus exists);
- the POC's test NDA.

Licences per §8.5.

Customer documents stay in the customer's tenant.

**Layers:**
1. Engine unit tests (pytest, golden fixtures).
2. An offline content harness on GPT and Claude, reusing the `infrastructure/ai-foundry/evaluation/` metrics.
3. Recorded Cowork acceptance runs.
4. From Phase 3, a cross-surface check comparing Cowork with the Spaarke Action (§6.5).

**Pass bar per accelerator** (proposed):
- recall ≥ 0.85 on rubric topics;
- zero fabricated authorities;
- results with the skill beat results without it;
- the provenance line in 100% of outputs;
- engine checks green on every output.

**Placement:** evals live in `practices/<p>/evals/` and engine tests in `core/engines/tests/`, not under `tests/**`. See the ADR-038 tension note in §15.

---

## 14. Constraints

- **Platform churn.** Most of this is new or preview, and the docs changed between 28 and 30 October. Pin tool versions per release, and re-verify after Ignite.
- **No admin at T0/T1.** That rules out tenant-wide installs and SPFx. Users can upload connectors, but whether that works depends on the tenant's consent policy.
- **Information Barriers.** Tenants that use them can't use T0/T1; the IT one-pager says so.
- **Customer pays.** Cowork runs on the customer's Copilot Credits.
- **Legal content rules.** Attorney sign-off, dated references, a disclaimer in every output, U.S. only in R1.
- **Data stays in the tenant.** No customer data leaves the customer's tenant in R1, and Spaarke hosts nothing for T0/T1.
- **Temporary sandbox.** All logic ships in the skill package, and outputs are regenerated from the edit plan.

---

## 15. Component justification and architecture tensions

**New surfaces (CLAUDE.md §11):**

| Component | Existing overlap | Why not extend | Cost of doing nothing |
|---|---|---|---|
| `src/accelerators/` | JPS Actions, Skills and Knowledge in Dataverse | Different runtime: Microsoft's sandbox reads files. The overlap is resolved by single-sourcing (§6), not by duplicating. | Three hand-maintained copies of each practice's content drift apart |
| Core Python engines | Compose's docx tooling (TypeScript, browser) and the BFF's C# tools | Cowork's sandbox runs Python, not our TypeScript or C#. From T2, Cowork can call the BFF instead. | Redlines and arithmetic written by the model: exactly the failure the POC's scripts avoided |
| `topics.yaml` + review-result schema | Spaarke's per-Action output schemas | They become the shared contract that Spaarke's schemas converge on (§7.2-3) | Results from the three surfaces can't be compared or logged consistently |
| Knowledge topics K1–K4 | `knowledge/m365-copilot/`, `declarative-agents/`, `mcp-apps/` (extended in K4) | New topics with no home | Claude Code builds against stale knowledge |
| `.claude/skills/cowork-plugin`, `practice-accelerator` | The `declarative-agent`, `widget-design` and `jps-action-create` skills | Different package format, limits and validation | Each authoring task re-derives the rules, and rules that could be linted stay in prose |
| Accelerator lint + alignment gate | `jps-validate` (JPS only) | Different artefacts | Packaging and alignment errors surface in front of customers |

**Architecture tensions:**
- **BFF hygiene (§10) / hot path:** not applicable to R1, which touches neither `Sprk.Bff.Api` nor `src/solutions/SpaarkeAi/**`. The connector project will need both.
- **ADR-002:** no direct Dataverse writes from Cowork (§10). Path C: comply.
- **ADR-038:** content evals and the Python engine and gate tests (41 + 22) live with the accelerator source (`core/engines/tests/`, `build/tests/`). Today they run only through `Build-Accelerator.ps1`: **no CI job runs them** `[Proposed]` (OD-23).
  - Path A (project exception): keep the tests beside the source, and add a GitHub Actions job on `src/accelerators/**` that runs the build with all gates.
  - Decide at design-to-spec. Either way, a CI job is required, because rules that only run locally drift.
- **ADR-039 (agentic placement):** all R1 skills produce drafts for human review, and sends and writes need approval. Compliant.
- **Agent cost:** offline eval runs are metered, so budget them per accelerator.

---

## 16. Phasing (to be refined by `/project-pipeline`)

*v0.5 `[Proposed]`, following OD-21. The NDA MVP spike (2026-10-31 → 11-02) already did much of v0.4's Phase 0 and Phase 1 for NDA.*

| Phase | Content | Exit |
|---|---|---|
| **0. Close the spike** *(before `/design-to-spec`)* | Owner decisions OD-20 to OD-24; fix queue L15, L16, L9, L10/L17; the Word A/B import retest (S5); T-14 admin deploy with T-P2, T-P7, T-P8; project registration (OD-10) and a project `CLAUDE.md` for the standing directives | results.md updated; this design at v0.6 with no stale claims |
| **1. Knowledge and foundation hardening** | Workstream K (K1–K3, K5, K7) from the tested facts; CI job for the engine and gate tests (OD-23); gates T-P5 and the "no review procedure in instruction-only skills" rule; onboarding Full path and routes 4a–4d; `playbook_import.py`; the T1 library template; T-P10 governed library; JPS generator (validated, not deployed) | One command builds every NDA output; every gate passes in CI and fails on its seeded fixture; SC-16 met |
| **2. NDA to release quality** | NDA `sources.md` and pins; the Spaarke NDA content comparison (§7.3); receiving and disclosing baselines; model language for every topic; named attorney sign-off (OD-5); evals on GPT and Claude, including the new cases (§13); guides, the IT one-pager and automation recipes; OD-24 candidates if approved | NDA passes SC-4 to SC-11, SC-13 and SC-16 to SC-19 |
| **3. Pilot readiness** | Sprint runbook; dry-run sprint with a practice lead playing the customer, in a customer-like tenant (OD-3; T-P11) | Dry run completed in ≤ 5 days, with a timed before/after |
| *R2* | Legal Intake, Invoice Review; then CRE, Employment; then U.S. Patent Applications | Same criteria per accelerator |

---

## 17. Success criteria (closed set for spec authoring)

*v0.5: under OD-21, "each accelerator" means the NDA accelerator in R1.*

| ID | Criterion |
|---|---|
| SC-1 | K1–K3 knowledge folders exist with SOURCE.md (pinned SHAs and dates) and NOTES.md, including the tested facts from results.md; the K5 skill exists and is registered (K7); K8 pins carry LICENSE/NOTICE |
| SC-2 | `notes/mvp-nda/results.md` (or its successor) records S1–S17, S19 and T-P1 to T-P12, each with its result, evidence and design consequence, or an explicit "dropped" with the reason |
| SC-3 | One command builds every output for an accelerator. All gates (spec, Microsoft, lint, engine tests, alignment) pass on the release and **fail** on each seeded violation fixture, **in CI** as well as locally (OD-23) |
| SC-4 | For each accelerator, installing by folder copy and by plugin upload both work, the skills run, and a second user receives them by sharing |
| SC-5 | NDA: a review produces an internal redline (summary page, surgical tracked changes, one comment per change, working section links), an external redline and a clean version. Engine checks pass, and page renders show no table spill or contrast failures |
| SC-6 | For each accelerator with Word playbooks, the Word playbook imports into the legal plugin in **Word desktop** with every topic present, each topic's `Topic ID:` and fallback position kept in its Rules, and the version line visible. A review then produces tracked changes on a corpus document, using the same topics as the Cowork skill. |
| SC-7 | Customer layer: changing a position in the library's legal playbook changes the outcome for that topic in Cowork and, after re-rendering, in Word. Every output carries the provenance line. With no playbook, outputs say "Spaarke baseline (not tailored)" |
| SC-8 | Alignment: every topic ID in every output (legal playbook, Word playbook, JPS file, eval rubric) is in `topics.yaml`; version stamps match `manifest.json`; generated JPS files pass `jps-validate` |
| SC-9 | Evaluation: each skill has ≥ 3 cases, including ≥ 1 negative. Each accelerator meets the §13 pass bar on GPT and Claude. Cowork acceptance runs are recorded |
| SC-10 | No skill name or primary trigger collides with a Legal for Cowork skill (lint), and S9 shows correct selection, including "review this MSA" not triggering `nda-review` |
| SC-11 | Unattended mode: an automation recipe runs end to end, asks no questions and states its defaults on page 1 |
| SC-12 | Every adapted file traces to a pinned upstream with its licence and notice; no upstream trademarks in names |
| SC-13 | Every baseline legal playbook and legal reference file has a recorded attorney approval and an as-of date |
| SC-14 | The guides, IT one-pager, automation recipes and sprint runbook exist and were used in the Phase 4 dry run |
| SC-15 | **Negative:** no R1 change touches `src/server/**`, Dataverse schema or data, `src/solutions/SpaarkeAi/**` or the Office add-ins, and no skill writes to Dataverse |
| SC-16 | Onboarding: on a fresh account, `accelerator-setup` completes the Quick path in ≤ 5 minutes and the Full path in ≤ 20. Each run produces:<ul><li>an installed skill set;</li><li>a legal playbook (by each of routes a–d);</li><li>a rendered Word playbook;</li><li>a profile with no unmarked gaps;</li><li>a custom-instructions block ≤ 20 KB;</li><li>a successful trial run.</li></ul>A paused run resumes, and `--redo` shows a diff before overwriting. **Negative:** it never installs or overwrites without an explicit yes, and never sends mail. |
| SC-17 | For each accelerator, `notes/spaarke-content-comparison-<p>.md` records the mapping, the eval scores for the Spaarke Action and the Agent Skill on the same corpus, and the per-topic decision. Every Spaarke gap or defect found has a GitHub issue. **Negative:** no Agent Skill content comes from Spaarke without passing that comparison and attorney approval. |
| SC-18 | Each practice has `sources.md` (§8.5) listing every source used, with licence, version or date, and what was taken. **Negative:** no content from a non-commercial, ShareAlike or AGPL source ships in a skill. |
| SC-19 | Each accelerator's guide and `accelerator-help` state which flows belong in Word and which in Cowork (§2.4.4), including "Spaarke reviews run in Cowork; in Word, pick Microsoft's Legal plugin with the Spaarke Word playbook, not our skill" (L21). Our skills' descriptions route a single-document review in Word to Microsoft's plugin. |
| SC-20 | **Negative (MVP defects):** with a previous task's `edit-plan.json` and a Spaarke playbook outside the named folders both present, a review on each eval model neither reuses the plan (L15) nor labels the stray playbook "customer" (L16), and its outputs stay in the task's `output/` folder (L10, L17) |
| SC-21 | Approved model language: every baseline topic has model language. In an eval run, every edit on a topic that has model language uses it verbatim, and every other edit's comment says the wording was drafted (§5.7-14) |

---

## 18. Owner decisions

| # | Decision | Recommendation |
|---|---|---|
| OD-1 | Naming | "Spaarke Solution Accelerator" (program) and "Practice Accelerator" (per practice) |
| OD-2 | Practice set and waves | **Decided 2026-10-31.** Wave 1: NDA, Legal Intake (email to a designated mailbox), Invoice Review. Wave 2: CRE, Employment. Wave 3: U.S. Patent Applications. Alternate: Privacy/DPA. *(v0.5: the R1/R2 split is OD-21.)* |
| OD-3 | Test tenant and credit budget | A dedicated demo tenant with ≥ 2 Copilot licences and a capped spending policy. The Spaarke tenant served for the POC, but S1 and S12 need a customer-like tenant |
| OD-4 | Source location | `src/accelerators/` in this repo for R1 |
| OD-5 | Legal sign-off | Name the approving attorney(s) for each practice |
| OD-6 | Jurisdiction | United States only in R1 |
| OD-7 | T0 packaging | **Answered by S1.** The unified (M365) plugin `.zip` is primary (it keeps the name, icons, version and a stable GUID); skill folders are the no-upload alternative; the Claude-format zip is for Claude-ecosystem reuse only |
| OD-8 | Getting accelerators to attorneys | Manual delivery in R1 (sprint, email, the T1 library); a portal later |
| OD-9 | Evaluation models | GPT on Azure OpenAI, and Claude |
| OD-10 | Project setup | Register with `/devops-project-start`, give R1 its own worktree, then run `/design-to-spec` |
| OD-11 | Read-only Spaarke matter context in R1 | Include it as an optional skill, gated on S12. It needs no code and makes a strong demo for Spaarke customers |
| OD-12 | Master copy of a customer's legal playbook | Markdown in SharePoint or OneDrive at T0/T1, with the Word `.docx` rendered from it. Spaarke becomes the master at T3 and exports the file |
| OD-13 | How far R1 aligns with Spaarke content | Topic catalogue, review-result schema, and JPS files that are generated and validated but not deployed. Deployment and Spaarke schema convergence go in a later project |
| OD-14 | Connector project | Open "spaarke-cowork-connector" as R1's successor (BFF-hosted MCP, OAuth, document tools, intake forms and MCP Apps), designed in parallel with R1 Phase 2 |
| OD-15 | Role of existing Spaarke content | An untested candidate, compared on the same corpus (§7.3). Findings are filed to the Spaarke backlog and are not fixed in R1 |
| OD-16 | Scope of `ooxml_redline.py` | The packaged Cowork deliverable and batch work only. A single-document review in Word goes to Microsoft's plugin. Revisit after S13 |
| OD-18 | Spaarke's presence in Word | **Decided (owner, 2026-11-02; S17).** R1: the Word playbook only, in the shape the Legal plugin imports, made easy to find (§2.4.6). Our plugin is listed in Word but doesn't run there; we manage that hazard through onboarding. No declarative agent, Copilot Tuning or Office add-in in R1. The Word route for *tools* is the v2 MCP connector |
| OD-19 | Specialist reviewers | A specialist panel inside one skill (§5.8a); standalone `/specialist` skills only if S16 passes; Foundry specialists through the connector at T2. *(Not needed for NDA in R1.)* |
| OD-17 | Where the attorney's defaults live | **Answered by S3:** both. A profile file, read unprompted in new sessions (T-06), and a pasted custom-instructions block |
| **OD-20** | Plugin structure: one plugin, or Knowledge (K) + Engines (E) per practice (protocol §3) | `[Proposed]` **One Cowork plugin per practice for now.** Gate "no review procedure in instruction-only skills" so a split stays mechanical. Run the Word probe (T-P7); split only if Word loads instruction-only skills *and* a Word-side skill would add something Microsoft Legal doesn't (§5.10) |
| **OD-21** | R1 scope | `[Proposed]` **Core + NDA to release quality.** Legal Intake and Invoice Review move to R2, with the rest of the roadmap unchanged (§3.1, §8.3, §16) |
| **OD-22** | Approving attorney for NDA content (OD-5 for NDA; owner item H2) | Name them before Phase 2. Their first job is model language for the 19 topics that lack it, and the receiving and disclosing baselines |
| **OD-23** | Where the Python tests run (ADR-038) | `[Proposed]` Path A: the tests stay beside `src/accelerators/`, and a CI job runs the build and gates on every change there (§15) |
| **OD-24** | Additions from the expert demo | `[Proposed]` Adopt verbatim approved wording (§5.7-14) now. Prototype the plan-preview card (§5.7-15, after S7) and the engine-generated Excel audit pack (§8.2). The privilege header waits for attorney wording |
| **OD-25** | R16: what the clean copy contains | Keep "all changes accepted, disclosed in the reply", or also offer a copy with only our changes accepted. Owner decision pending since 2026-10-31 |

## 19. Next steps

1. Owner review of v0.5: OD-20 to OD-25 and the `[Proposed]` items. Protocol v0.2 is reviewed at the same time, since §5.10 summarizes it.
2. Close the spike (§16 Phase 0):
   - fix L15, L16, L9 and L10/L17;
   - Word A/B import retest (S5);
   - T-14 admin deploy with T-P2, T-P7 and T-P8;
   - record the results in results.md.
3. Register the project (OD-10; the worktree already exists). Add a project `CLAUDE.md` holding the standing directives that now live in `current-task.md`.
4. Revise to v0.6 with the decisions and T-14 results, then `/design-to-spec` → `/project-pipeline`.
5. If OD-14 is accepted, capture the connector project as an Idea issue now, so its BFF placement work can start in parallel.
