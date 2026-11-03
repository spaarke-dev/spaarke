---
name: word-vs-cowork-legal-boundary-and-onboarding-2026-10-31
description: Word legal plugin vs Legal for Cowork side-by-side (surfaces, billing, admin, models, outputs, persistence); no shared playbook object/schema; no Word-Cowork handoff except the OneDrive file; Skill Management + Skill Report; Cowork personalization (20 KB custom instructions, private Projects, conversation-scoped memory, no copilot-instructions.md); claude-for-legal cold-start-interview as onboarding prior art
metadata:
  type: reference
---

# Copilot for Legal: Word plugin vs Legal for Cowork, and Cowork onboarding primitives (research as of 2026-10-31)

Tags: **C** = Cited (primary source says it) · **S** = Secondary (MVP blog, legal press, vendor doc) · **I** = Inferred from cited facts · **U** = Unknown / not documented.
Page dates are `ms.date / updated_at` for Learn, publish date for blogs. Prior researcher notes (2026-10-29/30) are inputs; this note records only what is new or sharpened.

## Summary

- The two plugins are **one product family with two execution models**: Word = in-document, single-file, deterministic flow (playbook → topics/rules → Run Review → native tracked changes; precedent → content-control placeholders → Finish Draft). Cowork = agentic, multi-file, multi-source, produces new files in OneDrive or edits Office files in place. Microsoft's only positioning statement is one sentence each (blog 2026-10-15). **C**
- **Playbook** is the same *concept* but not a shared *object*: Word consumes a `.docx` attached per review and parses it transiently; Cowork `review-contract` takes whatever the user attaches; `create-playbook` "creates a playbook from a set of precedents" with undocumented format/location. There is **no Microsoft playbook schema, sample, or template** anywhere in support, Learn, the transparency note, or the launch blog. **C/U**
- **No documented Word↔Cowork handoff.** The only bridge is the OneDrive/SharePoint file: Cowork edits a Word file in place (version history kept) and tells you to open it in Word; "Office in Copilot" (Frontier, 2026-10-09) adds a Word canvas inside Chat and Cowork. Whether Cowork's `edit-contract` emits native `w:ins/w:del` tracked changes is **not documented**; the Spaarke POC only got tracked changes by writing OOXML from a skill script. **C/U**
- **Partner plugins extend Cowork (and Copilot Chat)**, not the Word legal plugin, today. The plugin registry blog says a Plugins menu reaches "Microsoft 365 apps (Word, Excel, and PowerPoint)" "as these updates roll out". No partner ships playbooks. Thomson Reuters (except HighQ) and LexisNexis are absent. **C**
- **Onboarding primitives that exist in Cowork**: the built-in **Skill Management** skill (create/edit/manage skills by conversation; automatic 0–100 Skill Report with gates and conflict scan; saves `SKILL.md` + report JSON to `OneDrive/Documents/Cowork/skills/<name>/`), **custom instructions** (20 KB, Customize > Preferences), **Projects** (private; instructions override global), native **question cards** (choices + Skip + free text), and **MCP form-mode elicitation** (connectors only). **Memory is conversation-scoped** in Cowork; there is **no `copilot-instructions.md` file** in the Cowork docs. **C**
- Best prior art for the interview itself: `anthropics/claude-for-legal` `<plugin>/skills/cold-start-interview/SKILL.md` (quick 2-min vs full 15-min paths, `[PLACEHOLDER]/[DEFAULT]/[PENDING]` markers, pause/resume, seed-document extraction, prose profile written to a known path, `--redo/--side/--check-integrations`). Its skill-ecosystem hub (`legal-builder-hub`) adds `skill-installer`, `skills-qa`, `customize`. Microsoft ships no onboarding skill. **C**

---

## 1. Side-by-side: legal plugin in Word vs Legal for Cowork

| Dimension | Legal plugin in Word ("Legal Skills in M365 Copilot in Word") | Legal for Cowork (plugin) | Tag / source |
|---|---|---|---|
| **Product framing** | One of the two "legal plugins" under the Copilot for legal umbrella; replaces Legal Agent (Frontier). "The Legal Agent is being retired. Existing Legal Agent users will lose access." | The other plugin. Learn calls it "Legal for Cowork"; MC1509799 calls it "Cowork for Legal". | C: manage-copilot-for-legal (2026-10-13/10-15); MC1509799 (2026-10-15) |
| **Entry point** | Copilot sidebar in Word. Auto-detects a legal document and "will tend to use legal skills for the rest of your chat". Prompt "Review this contract using a playbook"; `@Draft-new-contract …`. User toggle: Copilot `…` > **Manage plugins & skills** > Legal. | Cowork (copilot.cloud.microsoft > Cowork) > **Customize** > Roles & Industry > Legal for Cowork > **Add**. Invoke by `/skill-name`, **+** menu, or implicitly on intent. | C: support get-started-with-copilot-for-legal (undated, © 2026); cowork-available-plugins (2026-10-26/10-29) |
| **Surfaces** | **Word desktop only.** MC1509799 and roadmap RM571884 list Platforms: Desktop; Mac and Web not listed. The April 2026 Word blog said web and Mac "coming soon" for the Word Copilot capabilities; nothing since says shipped. | Browser, Copilot desktop app (Windows, macOS), Copilot mobile (iOS/Android; plugins reachable via + > Skills), plus Outlook and Teams hosts. **Custom skills are not supported on mobile.** | C: RM571884 (upd 2026-10-20); techcommunity 4508974 (2026-04); application card (2026-09-22/10-29); use-cowork (2026-09-14/10-21). U: Mac/web GA date |
| **Licence / billing** | Microsoft 365 Copilot commercial USL. On by default for all USL holders. No per-use credits documented (in-app Copilot is per-user licensed). | USL **plus** Cowork enabled by an admin **spending policy** (usage-based billing, Copilot Credits; policy minimum 2,000 credits/user/month). Model profiles per policy. Every model response, tool/skill call, image, browser task bills. Per-skill cost undocumented. | C: manage-copilot-for-legal; usage-based-billing-manage-copilot-credits (2026-10-28); cowork-faq (2026-09-29/10-29). I: Word "no credits" |
| **Admin control** | Microsoft 365 Apps admin center > Policy Management > Cloud Policy **"Legal Skills in M365 Copilot in Word"** (default on). | Microsoft 365 admin center > **Agents > Tools** (per-plugin Users: all / none / specific groups; Install/Uninstall/Block); Agents > Settings > **Agent and plugin access** (publisher categories Microsoft / your org / certified external). Group scoping for the Word policy: "supplied source does not specify" (MC). | C: manage-copilot-for-legal; agent-365 manage-plugins-skills-mcp-servers (2026-10-14/10-23); MC1509799 |
| **Models** | **Undocumented.** Transparency note: "recent models have led to improved performance"; no model named. | User-selectable: Auto, GPT 6.1 Sol, GPT 6 Astra, GPT 5.6 Terra, GPT 5.5 (Frontier), Claude Opus 5.5, Claude Sonnet 5.5, Claude Fable 5.1 (Frontier, data retention), Grok 4.7. Reasoning effort Light/Medium/High/Extra High/Max. **EU Data Boundary tenants: GPT only.** Admin can switch off the Anthropic family tenant-wide. (Application card still lists Sonnet 4.6/Opus 4.7 — stale.) | C: cowork-models (2026-09-14/10-24); cowork-faq. U: Word model |
| **Data scope** | The open document plus what the user attaches (playbook `.docx` by upload or "Attach from Cloud"; precedent/template; "agreement context" for placeholder fill). Transparency note (umbrella) says Copilot can "reason over context intelligently retrieved using Work IQ … emails, documents, spreadsheets and presentations and web searches". Blog: drafting "can draw on approved precedents in SharePoint". | User's permissions over OneDrive, SharePoint, Teams, mail, calendar, people, meetings; "Add work context"; "Attach cloud files and folders"; reference past chats/tasks (Oct 2026); plugin connectors (iManage, NetDocuments, Harvey, …). No SharePoint Embedded path (prior note). Inherits sensitivity labels; cannot read encrypted files. | C: support page; transparency note; blog 4564246; use-cowork; whats-new (2026-10-28/10-29) |
| **Output type** | **Native tracked changes in the open document** (Accept / Accept All); analysis per topic in the pane with numbered clickable citations; drafting inserts **Word content controls** as placeholders; "Finish Draft" generates the final. Works in documents that already contain tracked changes, keeping prior revisions separate. | **New files** (docx/xlsx/pptx/pdf/md/images) in the OneDrive **Cowork** output folder, or **in-place edits** to existing Word/Excel/PowerPoint files in OneDrive/SharePoint (approval per task; version history preserved). Adaptive Cards in-conversation. Emails, Teams posts, calendar events. **Tracked changes from Cowork edits: not documented.** | C: support page; techcommunity 4516218 (2026-04-30); use-cowork §Edit an existing Office file; whats-new Sept 2026. U: Cowork tracked changes |
| **Comments** | Not mentioned in the support page or transparency note. Legal Agent blog: "View supporting citations for each suggestion". | Cowork docs do not document inserting Word comments. "Office in Copilot" (Frontier) canvas supports commenting/co-authoring. POC produced comments only via skill script writing `comments.xml`. | U (both); C: Office in Copilot blog (2026-10-09, JP repost 2026-10-14); repo POC note |
| **Persistence / state** | Playbook is parsed per review; user can edit topic rules before Run Review; **no save/reuse/share of a playbook or its rules is documented.** Word plugin stays active "for the rest of your chat". | Custom instructions (20 KB), Projects (private, instructions + context, precedence over global), skills in OneDrive, Automations (25 scheduled + event triggers), model/effort selection persists per device/user. **Memory is conversation-scoped**; approvals do not persist across conversations. | C: support page; cowork-customize (2026-09-14/10-21); cowork-projects (2026-10-27/10-28); application card. U: Word rule-edit persistence |
| **Skill names exposed** | "Review contract" skill; `@Draft-new-contract` (with `Continue @Draft-new-contract, display the placeholders…`). No other `@` names documented. | 20 skills: analyze-related-contracts, build-case-chronology, check-cross-references, check-defined-terms, check-execution-readiness, compare-across-jurisdictions, compare-key-terms, create-playbook, draft-new-contract, draft-legal-memo, draft-minutes-and-resolutions, explain-legal-concepts, extract-obligations-and-deadlines, identify-regulatory-gaps, improve-legal-prompt, edit-contract, legal-reasoning, review-contract, summarize-legal-matters, summarize-redlines. Learn lists names only; the transparency note's 25 "legal skills" descriptions map across both surfaces. | C: support page; cowork-available-plugins; transparency note |
| **Languages / jurisdiction** | "Tested primarily for EN-US"; "not jurisdiction-aware by default"; may "miss relevant clauses or misapply playbook guidance" in long documents. | Same transparency note applies; Cowork "optimized for English"; Anthropic models limited to Anthropic-supported regions. | C: transparency note; application card; cowork-faq |
| **Rollout** | GA worldwide from mid-Oct 2026, complete by mid-Dec 2026 (public preview late Apr → mid-Dec overlaps). "New Word experience coming to preview later this month" (Oct 2026): navigate between defined terms and their definitions. | Same MC rollout window. Legal for Cowork listed GA in Learn (2026-10-26). More partner plugins "over time". | C: MC1509799; blog 4564246 |

---

## 2. Interaction map

### Works separately (no shared state)
- **Review in Word** on the open `.docx` with a per-review playbook → tracked changes. Nothing is written anywhere Cowork can see except the saved document itself. **C**
- **Cowork multi-file work** (obligations across a portfolio, key-term comparison, chronologies, memos, minutes) → new files in `OneDrive/Cowork/`. Word's plugin never sees Cowork state. **C**
- **Admin switches are independent**: Cloud Policy for Word vs Agents > Tools for Cowork; turning one off leaves the other on. **C**
- **Billing is independent**: Word use is USL-only; Cowork use burns credits under a spending policy. **C/I**

### Works together (only via files)
- **Cowork → Word**: Cowork edits a Word file in place in OneDrive/SharePoint; the doc tells you to "open the file directly in Word … to review in its existing OneDrive/SharePoint location". A user can then run the Word legal plugin on that file. **C** (use-cowork) / **I** (chaining)
- **Cowork `create-playbook` → Word review**: if the output is a `.docx` in OneDrive (undocumented but the most likely form), Word's "Attach from Cloud" can pick it up. **I**
- **Word draft → Cowork**: a Word-drafted contract saved to OneDrive/SharePoint can be attached in Cowork (`Attach cloud files`, `/` reference) for `summarize-redlines`, `extract-obligations-and-deadlines`, etc. **I**
- **Office in Copilot (Frontier, 2026-10-09)**: Word canvas side-by-side with Chat and Cowork; files in OneDrive; "continue in standalone Office apps". Not yet GA; no tracked-changes mention. **C**
- **No "Open in Cowork" / "Continue in Word" command exists in either direction.** **C (absence across support, Learn, blog)**

### Overlaps
- **Reviewing a contract against a playbook**: Word "Review contract" vs Cowork `review-contract`. Same capability name in the transparency list ("Playbook contract review"). Whether they share prompts/engine is **U**; the Word flow has a documented deterministic pipeline (topics → rules → Run Review), Cowork's does not.
- **Drafting from precedent**: Word `@Draft-new-contract` (content controls, Finish Draft) vs Cowork `draft-new-contract` (new file). **C** names; **U** internals.
- **Redlining**: Word applies native tracked changes; Cowork `edit-contract` edits a file — tracked-change fidelity **U**. The transparency note's "Precise edits: produces tracked-change redlines" is written at umbrella level and most plausibly describes Word. **I**
- **Partner data**: both surfaces are said to "retrieve information from partner third-party information sources" (transparency), but the only documented partner mechanism is Cowork/Chat plugins. **C/I**

### Gaps (where Spaarke accelerators fit)
1. **No playbook object**: no schema, no sample, no library, no reuse in Word, no sharing primitive beyond ordinary SharePoint files. **C**
2. **No house-style / positions / defaults layer for the Word plugin at all** — it has no custom instructions, no skills folder, no memory (Word custom skills exist only for Excel/PowerPoint). **C** (Word custom skills absent; MC1434580/MC1434582 cover PowerPoint; Excel skills GA)
3. **No cross-surface personalization**: Cowork custom instructions/Projects do not reach Word; Copilot Chat memory is not documented as reaching Cowork. **C/U**
4. **No deterministic redline engine exposed in Cowork**: the POC had to ship `scripts/ooxml_redline.py`. **C (repo)**
5. **No onboarding/setup skill from Microsoft** for either surface. **C (absence)**
6. **No Microsoft guidance on Word vs Cowork** beyond: Word = "deep document work, with experiences built directly into the document"; Cowork = "delegate broader, multi-step work across agreements, emails, meetings and files". The Chat-vs-Cowork FAQ table is the closest decision aid. No Microsoft Mechanics video found; the only demo is the 2026-10-22 Discovery Session webinar "Microsoft Copilot in Word: Turn a blank page into a polished draft". **C**

---

## 3. Playbook object: answers

| Question | Answer | Tag / source |
|---|---|---|
| Same object in Word and Cowork? | **No shared object.** Word: a `.docx` the user attaches per review; Copilot "ingests your playbook, breaks it down into a series of rules" → topics with rules + example clauses, user-editable before Run Review. Cowork: `review-contract` has no documented input contract; files are attached per conversation or live in a Project's context. | C: support page; cowork-projects |
| Does Cowork `review-contract` accept the Word `.docx` playbook? | **Undocumented.** Cowork accepts `.docx` attachments (200 MB limit) and will reason over them; whether `review-contract` parses it into rules the same way is **U**. | C: use-cowork file types; U |
| What does `create-playbook` produce? Where? | Only description: "Draft precedent playbook. Create a playbook from a set of precedents." Output format and location **undocumented**. Cowork's general behaviour puts created files in the OneDrive **Cowork** output folder; because Word requires `.docx`, a `.docx` is the likely output. | C: transparency note; use-cowork. U/I |
| Can that output be attached in Word's playbook review? | If it is a `.docx` in OneDrive/SharePoint, yes via **Attach from Cloud**. Untested. | I |
| Documented schema / sample playbook / template from Microsoft? | **None found.** Searched: support get-started page, transparency note, Learn admin page, launch blog 4564246, Legal Agent blog 4516218, MC1509799, RM571884. The transparency note mentions "playbook" only generically. The blog says "Approved templates and reusable playbooks help legal teams work from established standards" with no mechanism. | C (negative) |
| Is the playbook persisted as a "skill" in Word? | The Frontier-era wording ("converts the playbook into a skill") appears only in secondary summaries; the **current** support text says Copilot "ingests your playbook, breaks it down into a series of rules". No save, reuse or library is documented. | C: support page (current); S: search snippets |
| Partner playbooks? | **None found.** LawVu has a "playbooks" feature inside its own product (help.lawvu.com), not a Copilot playbook. No partner plugin description on Learn mentions playbooks. | C (Learn plugin table); S (LawVu) |
| Practical sharing path today | A governed SharePoint library of `.docx` playbooks → Word "Attach from Cloud" / Cowork "Attach cloud files"; or embed positions as `references/` in a Cowork skill (≤20 files, 10 MB) and share the skill with specific users. MVP guidance (2toLead, 2026-10-18) recommends the governed SharePoint site with sensitivity labels. | I; S |

---

## 4. Onboarding and personalization primitives in Cowork, with prior art

### 4.1 Skill Management (built-in)
- **What it is.** Application card: built-in skills include **Skill Management**; "Users can create, edit, and manage custom skills directly through conversation using the Skill Management skill, without manually editing files in OneDrive." **C** (2026-09-22/10-29)
- **How a user invokes it.** Customize > Skills > **Add > Create new** (guided session), or simply ask in chat ("help me create a skill"). The copilot-camp lab shows the side panel loading "Skill management" and a "Skill purpose" card (draft document / summarise content / format data / workflow automation). **C** (use-cowork; copilot-camp `docs/pages/copilot-cowork/01-cowork-skills.md`)
- **What it does.** Collects name, description, category, instructions; checks the name and your capacity (50 skills); generates `SKILL.md`; stores it at `OneDrive/Documents/Cowork/skills/<name>/`; validates; produces a **quality report** (a JSON file saved beside `SKILL.md` per the lab screenshot). You can test it in the same session and iterate; new/edited skills load in the **next** session. **C**
- **Evaluation ("Skill Report").** Runs automatically on create/update/validate. Depth scales with reach (personal/team/org/store) and risk: Minimal (structure + score) → Standard (+behavioral test) → Full (+asset validation, trust & safety set, **conflict scan**) → Maximal (+human review, regression, adversarial). Score 0–100 = Trigger clarity + Instruction specificity + Scope boundaries + Robustness (25 each); bands Excellent ≥85 / Good 70–84 / Needs work 50–69 / Poor <50. Gates: launch floor, Safety/RAI scan (destructive ops, sending without confirmation, etc. fail without guardrails), trust-and-safety gate (injection/jailbreak/sensitive-data/unconfirmed-action cases), conflict scan ("adding an explicit handoff between them resolves the conflict"), regression check. **C** (use-cowork §How Cowork evaluates skills)
- **Edit/delete/share.** Skill detail page: edit instructions, download, open in OneDrive, share (Only you / Specific users; Re-share pushes updates), delete. **C** (cowork-customize)
- **Cost.** "Authoring and testing a custom skill in Copilot Cowork will consume Copilot Credits." **C** (copilot-camp lab)
- **Can one skill instruct Cowork to create/install/modify other skills?** **Not documented (U).** Inference: Skill Management is an ordinary built-in skill "acquired dynamically as needed during a conversation", so a `SKILL.md` that says "use Skill Management to create a skill named X with these instructions" should trigger it — the same way the POC's onboarding design (Appendix I) assumed. Constraints that are documented: plugin skills "can't override built-in skills of the same name"; the Safety/RAI and Scope-boundaries checks may penalise a skill whose instructions write other skills (**U**). Direct file writes into `Documents/Cowork/skills/` from a skill (bypassing Skill Management) are **undocumented**; Cowork's documented write targets are the Cowork output folder and in-place Office files. Treat Skill Management as the only supported "install" path from inside a conversation; the OneDrive folder copy (or Customize > Upload skill `.md/.zip/.skill`) is the supported path from outside. **C/I/U**
- **Copying skills from a package.** A skill can carry other skills as companion files (`references/`, `assets/`; ≤20 files, 5 MB each, 10 MB total) and instruct Cowork to hand their content to Skill Management. Whether Skill Management accepts a prewritten full `SKILL.md` verbatim vs re-drafting it is **U**.

### 4.2 Personalization primitives

| Primitive | Where | Limits | Persists across sessions? | Can a skill read it? | Can a skill write it? | Tag / source |
|---|---|---|---|---|---|---|
| **Custom instructions** | Cowork > Customize > **Preferences** tab (rich text; `/` to reference a skill, file, person or meeting) | 20 KB ≈ 3,000 English words; live counter; "lengthy or conflicting instructions … affect response quality" | Yes; personal to the user | Yes — included "as context for every task" (so any skill sees them implicitly) | **No documented API.** A skill can only draft text and tell the user to paste it into Preferences | C: cowork-customize (2026-09-14/10-21); cowork-faq |
| **`copilot-instructions.md`** | **Not a Cowork artefact.** Not present in any Cowork Learn page, FAQ, application card or TOC. (The POC note's primitives table lists it; that was Cowork's self-description in session, not documentation.) | — | — | — | — | C (negative, cowork TOC 26 pages) |
| **Projects** | Left nav > Projects; **Edit instructions**; Project context (reference items, uploaded files, items from chats) | Instruction limit shown in editor, number undocumented | Yes; **private to the creator**; project instructions "take precedence over global Cowork or chat instructions" | Yes, for tasks started in the project | No API | C: cowork-projects (2026-10-27/10-28) |
| **Memory** | — | — | **No.** "Memory (conversation-scoped) … Memory does not persist across conversations." Copilot Chat has a separate preview **Copilot memory** (saved memories + inferred + custom instructions, stored in a hidden Exchange mailbox folder, Settings > Personalization); **no document says Cowork reads it.** | n/a | n/a | C: application card; copilot-personalization-memory (2026-09-02/10-31). U: Cowork↔Chat memory link |
| **Approval preferences** ("don't ask again") | Side panel > Permissions | — | **No** — per conversation only | — | — | C: application card; use-cowork |
| **Model / reasoning effort** | Compose box pickers | — | Model: per device until changed; effort: "persists across future Cowork tasks until you change it" | — | — | C: cowork-models |
| **Plugin on/off per conversation** | Sources & Skills | — | Saved per device | — | — | C: cowork-plugins (prior) |
| **Skills folder** | `OneDrive/Documents/Cowork/skills/<name>/SKILL.md` (+ companions) | 50 skills; 1 MB SKILL.md; 20 companions/5 MB/10 MB | Yes; discovered at session start; not on mobile | Yes (loaded by description match or `/name`) | Via Skill Management (documented) or OneDrive sync/upload (documented); direct write from a running skill **U** | C: use-cowork; application card |
| **Output folder** | `OneDrive/Cowork/` | — | Yes (files) | Yes if attached/referenced | Yes — Cowork creates files here, including `.md` | C: use-cowork; cowork-faq |
| **Profile file pattern** (e.g. POC's `Documents/Cowork/Profile/legal-profile.md`) | Any OneDrive file the skill tells Cowork to read | — | Yes (it is a file) | Yes, if the skill instructs "read `<path>`" and the user has shared/attached it — whether a skill can read an arbitrary OneDrive path *without* attachment is **U** (Cowork's documented access is via picker, `/` reference, or Enterprise Search) | Cowork can create `.md` files; controlling the exact folder is **U** (outputs land in `OneDrive/Cowork/`) | I/U |

### 4.3 Asking questions: elicitation and question cards
- **Native question cards (skills).** Cowork "presents a set of choices for you to pick from"; arrow keys + Space, **Submit**, or **Skip**; the user can also type their own answer; task status shows **Needs user input** (also surfaced in the Tasks filter "Needs your input" and in-app notifications). A `SKILL.md` can script a sequence of these ("Ask once, up front" — the POC got five questions on one card). No documented card-JSON contract for skills. **C** (use-cowork §Answer questions from Cowork; cowork-faq; whats-new Apr 2026) / **I** (multi-step)
- **MCP form-mode elicitation (connectors only).** During `tools/call` the server sends `elicitation/create` with a flat JSON Schema (string with email/uri/date/date-time formats, number/integer, boolean, enum → dropdown); `title`/`description` become labels; response `accept`/`decline`/`cancel`; auto-cancel if the user sends a new message or the connection drops. "Keep forms short … 5–7 fields"; **one pending elicitation per session; sequential OK, parallel not**; URL mode not supported; no styling. Cowork advertises the capability automatically. → Only available if Spaarke ships an MCP connector (the BFF route). **C** (cowork-elicitation-forms, 2026-05-27/10-21)
- **Adaptive Cards (built-in skill).** Renders cards "with structured layouts, buttons, and data displays". Whether button actions round-trip into the conversation is **U** (prior note).

### 4.4 Prior art for onboarding interviews (concrete paths)

**anthropics/claude-for-legal** (Apache-2.0; clone in scratchpad `src-claude-for-legal/`; HEAD 4a6c6518 per prior note)
- 13 copies of `<plugin>/skills/cold-start-interview/SKILL.md` (ai-governance-legal, commercial-legal, corporate-legal, employment-legal, ip-legal, law-student, legal-builder-hub, legal-clinic, litigation-legal, privacy-legal, product-legal, regulatory-legal; `commercial-legal` = 643 lines).
- **Trigger**: "on first use of the plugin, when `~/.claude/plugins/config/claude-for-legal/<plugin>/CLAUDE.md` is missing or still contains template placeholders, or when the user says 'set up the plugin' … 'onboard me'". "This is the only skill that should run on a fresh install."
- **Writes**: `~/.claude/plugins/config/claude-for-legal/<plugin>/CLAUDE.md` (practice profile) using the scaffold at `<plugin>/CLAUDE.md`; shared `~/.claude/plugins/config/claude-for-legal/company-profile.md` from `references/company-profile-template.md` ("The first plugin you set up writes this; the rest read it"). Migration from the old cache path `~/.claude/plugins/cache/claude-for-legal/<plugin>/*/CLAUDE.md`.
- **Flags**: `--redo` (re-interview, show diff before overwrite), `--check-integrations` (probe MCPs; "only report ✓ if an MCP tool call actually succeeded"), `--side sales|purchasing` (re-run only the playbook section).
- **Pacing**: fork **quick (2 min)** = Part 0 + playbook side, write `[DEFAULT]` markers; **full (15 min)** = Parts 0–4. Marker vocabulary: `[PLACEHOLDER]` (never filled), `[DEFAULT]` (deliberately defaulted), `[PENDING]` (paused), `<!-- SETUP PAUSED AT: [section] -->` top comment for resume. "Never write a practice profile with silent gaps."
- **Interview parts**: 0 who's using this / what's connected / practice setting; 1 the team; 2 the playbook (LoL, indemnity, data protection, term, governing law, "the one thing", AI/ML training rights 7-point sub-checklist); 3 escalation matrix; 4 seed documents (5–10 signed agreements; "extract actual playbook positions. Note deltas between stated positions and what was signed").
- **Profile format**: prose with tables, "not a config file"; sections `## Who we are`, `## Who's using this`, `## Available integrations`, `## Playbook` (`**Active side:**`, `### Sales-side playbook`, `### Purchasing-side playbook`), `## Escalation`, `## House style`, `## Outputs`, `## Seed documents reviewed`, `## Review preferences`, `## NDA triage preferences`, `## Playbook monitor settings`. Rule: "Don't write YAML."
- **Post-write**: show a summary ("what did I get wrong?"), prompt to connect a research tool, propose starter skills based on "what hurts", offer a test run, close with how to change things; "Your practice profile learns" (deviation monitor proposes playbook updates).
- **Hub plugin** `legal-builder-hub/skills/`: `cold-start-interview` (recommends + installs a starter pack from a registry), `skill-installer` (allowlist → fetch → show RAW SKILL.md → structural trust check → `skills-qa` → explicit `yes` → copy + update `install-log.yaml`), `skill-manager` (uninstall/disable from the log), `skills-qa` (13 design parameters incl. trust surface, freshness, schema, conflicts; 3-band verdict Ready / Some Concern / Material Concerns), `customize` (change one section without re-running), `registry-browser`, `auto-updater`, `related-skills-surfacer`, `disable`, `uninstall`.
- **Porting caveat** (prior note): `atk import openplugin` keeps skills but drops `agents/`, `hooks/`, commands and the plugin `CLAUDE.md`, so the `~/.claude/...` profile path breaks in Microsoft Cowork; the pattern must be re-pointed at an OneDrive file or at custom instructions.

**anthropics/skills — `skills/skill-creator/`** (fetched 2026-10-31; SKILL.md 33 KB)
- Description: "Create new skills, modify and improve existing skills, and measure skill performance … run evals … benchmark … optimize a skill's description for better triggering accuracy."
- Loop: capture intent (4 questions: what / when to trigger / output format / test cases?) → interview & research → write SKILL.md (anatomy `SKILL.md` + `scripts/` + `references/` + `assets/`; progressive disclosure; <500 lines; "pushy" descriptions) → 2–3 realistic test prompts in `evals/evals.json` → spawn with-skill and baseline runs → draft assertions → grade (`agents/grader.md`) → `scripts/aggregate_benchmark.py` → viewer `eval-viewer/generate_review.py` (has a `--static` mode "for Cowork / headless environments") → read `feedback.json` → improve → iterate.
- Files: `scripts/{quick_validate,run_eval,run_loop,aggregate_benchmark,generate_report,improve_description,package_skill,utils}.py`; `agents/{analyzer,comparator,grader}.md`; `references/schemas.md`; `eval-viewer/`. Note: relies on sub-agents and Python tooling that Microsoft Cowork does not document; the *interview* and *description-tuning* parts port, the eval harness does not. **C/I**

**anthropics/knowledge-work-plugins — `legal/`** (skills: brief, compliance-check, legal-response, legal-risk-assessment, meeting-briefing, review-contract, signature-request, triage-nda, vendor-check). Personalization pattern: a **`legal.local.md`** playbook file "saved in any folder you've shared with Cowork … the plugin finds it automatically" (Claude Cowork). Default positions are US (DE/NY/CA) and must be customised. The simplest "profile file" precedent. **C** (README)

**Microsoft samples**
- `microsoft/copilot-camp` `src/cowork/weekly-status-mail/SKILL.md`: shows the `cowork:` frontmatter extension (`category: communication`, `icon: Mail`) and explicit negative routing ("Do NOT use for … — use stakeholder-comms instead"). `src/cowork/CopilotDevCamp-for-cowork/manifest.json`: plugin with `agentSkills[]` (3 folders) + `agentConnectors[]` (Microsoft Learn MCP, auth None). `src/cowork/zava-claims-sso/`: SSO connector sample. Lab `docs/pages/copilot-cowork/01-cowork-skills.md`: the Skill Management walkthrough quoted above. **C**
- `microsoft/cat-agent-skills` (`submissions/…`, Power CAT gallery): no onboarding/setup skill in the listing; `cowork-plugin-engineer`, `vendor-contract-risk-review`, `redlining-content` per prior note. **C**
- Microsoft Learning "AI Flight Academy" has Cowork scenario bricks (`/build/cowork-scenario-1..3`); the `cowork-install-skill` brick URL from search returns 404. **U**
- **No Microsoft-authored onboarding/setup skill exists.** **C (negative)**

**Community**
- `4444j99/a-i--skills` `setup-cowork` (skills.sh, first seen 2026-05-09): five steps (role → plugins → connectors → try a skill → wrap); writes nothing; targets Claude Cowork. **S**
- `kesslernity/awesome-copilot-cowork-skills` (CC BY-SA 4.0): `skills/writing-communication/write-like-me` builds a **saved writing-voice profile** then drafts in it (closest Microsoft-Cowork precedent for a persisted profile); `skills/meta-skills/skills-backup-keeper`; `skills/meta-skills/no-delete-guardrail`. **S**
- No MVP blog showing a "setup" skill for Microsoft Cowork was found; CIAOPS (2026-04-23), The Windows Club, aguidetocloud describe the guided builder only. **S/U**

### 4.5 Design implications for a Spaarke onboarding skill (inferred)
1. Run the interview as a sequence of **native question cards** (≤5–7 options each; always allow Skip/free text); offer the claude-for-legal **quick vs full** fork and `[DEFAULT]`/`[PENDING]` markers.
2. Persist the profile as a **Markdown file Cowork creates** (it lands in `OneDrive/Cowork/`; tell the user to keep it or move it) **and** emit a ≤20 KB **custom-instructions block** the user pastes into Customize > Preferences — the only cross-task context that needs no attachment. For a practice group, also write **Project instructions** (private per user, so each attorney does it once).
3. Create/personalize practice skills **through Skill Management**, not by writing into the skills folder; expect a Skill Report per skill; pre-empt the **conflict scan** by giving each skill explicit "Do NOT use for … use /other" lines (Microsoft's own sample does this) and never reuse built-in names.
4. Budget **credits**: authoring + evaluation bills; keep the interview lean.
5. Playbooks: produce a `.docx` per practice area in a SharePoint library so the same artefact feeds Word ("Attach from Cloud") and Cowork ("Attach cloud files"); optionally mirror positions as `references/` inside the skill.
6. Hand-offs: tell the attorney explicitly which flows belong in Word (single-document review/redline) and which in Cowork (multi-file, outputs, automations); there is no in-product router.

---

## 5. Corrections to the context given

| Context claim | Status | Correction |
|---|---|---|
| "Legal for Cowork (plugin `legal-for-cowork`, ~v1.0.10 …)" | **U** | No version or internal ID appears in Learn, the MC, or the transparency note. Treat `legal-for-cowork`/`1.0.10` as observed-in-tenant, not documented. |
| "Word desktop" | **C, sharpened** | Windows desktop only is what shipped; Mac/web were "coming soon" in April 2026 and remain unlisted on RM571884/MC1509799 (Platforms: Desktop). |
| "only input = a .docx playbook attached per review" | **C for review**; drafting also takes an attached precedent/template plus optional "agreement context" for placeholder auto-fill. |
| "Cowork has a built-in skill-management capability" | **C, named** | The built-in skill is literally called **Skill Management**; it creates, edits, validates (Skill Report) and saves; copilot-camp shows it generating the report JSON beside `SKILL.md`. |
| POC note: `copilot-instructions.md` in the Cowork folder | **Not documented** | Cowork custom instructions live in Customize > Preferences (20 KB). No file-based instructions are documented. |
| POC note: "Memory — durable facts about the user, Cowork memory service" | **Contradicted** | Application card: Cowork memory is conversation-scoped and does not persist across conversations. Copilot Chat's memory (preview) is a separate feature with no documented Cowork link. |
| POC note: skills can run "parallel sub-agents" | **Contradicted** (prior) | Sequential execution; Claude `agents/` not supported. |
| "20 skills" list | **C** | Matches Learn exactly (2026-10-26/10-29). |
| Plugin name | **Inconsistent in Microsoft sources** | Learn/admin page: "Legal for Cowork"; MC1509799 and some Microsoft Korea posts: "Cowork for Legal". Use "Legal for Cowork" (Learn). |
| Partner plugins "extend" the Word legal plugin | **Not evidenced** | Partner legal plugins are MCP-connector plugins in the Cowork/Chat catalog. The registry blog promises a Plugins menu in Word/Excel/PowerPoint "as these updates roll out"; iManage's own doc claims "Cowork is also available within Microsoft Word" (vendor claim, unverified). |
| Cowork model list in prior notes (Sonnet 4.6/Opus 4.7, Sonnet 5/Opus 4.8) | **Superseded** | cowork-models (upd 2026-10-24) lists Claude Opus 5.5, Claude Sonnet 5.5, Claude Fable 5.1 (Frontier), GPT 6.1 Sol, GPT 6 Astra, GPT 5.6 Terra, GPT 5.5 (Frontier), Grok 4.7; effort levels Light→Max. |
| Ignite 2026 (17–20 Nov) legal/Cowork sessions | **U** | No catalog entries surfaced via search; the official catalog must be checked directly. The only dated Microsoft legal event found is the 2026-10-22 Word webinar. |

---

## 6. Reference links (with dates)

**Microsoft primary**
- Manage Copilot for legal — https://learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-copilot-for-legal (2026-10-13 / 2026-10-15)
- Get started with Copilot for Legal (Word + Cowork flows) — https://support.microsoft.com/en-us/word/get-started-with-copilot-for-legal (undated; © 2026). Legacy URL still serving the same text: https://support.microsoft.com/en-us/word/get-started-with-the-legal-agent-frontier
- Copilot for Legal: Transparency Documentation — https://support.microsoft.com/en-us/word/copilot-for-legal-transparency-documentation (undated)
- Introducing Microsoft Copilot for legal (blog) — https://techcommunity.microsoft.com/blog/microsoft365copilotblog/introducing-microsoft-copilot-for-legal/4564246 (2026-10-15)
- Word Legal Agent in Frontier (blog) — https://techcommunity.microsoft.com/blog/microsoft365copilotblog/word-legal-agent-in-frontier/4516218 (2026-04-30)
- Copilot in Word: new capabilities (Mac/web "coming soon") — https://techcommunity.microsoft.com/blog/microsoft365copilotblog/copilot-in-word-new-capabilities-for-document-workflows/4508974 (2026-04-08)
- Introducing the Microsoft plugin registry — https://techcommunity.microsoft.com/blog/microsoft365copilotblog/introducing-the-microsoft-plugin-registry/4559682 (2026-09-30)
- Introducing Office in the new Microsoft Copilot app — https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/introducing-office-in-the-new-microsoft-copilot-app/4561230 (2026-10-09; JP repost https://blogs.windows.com/japan/2026/10/14/introducing-office-in-the-new-microsoft-copilot-app-4561230/ 2026-10-14)
- MC1509799 (via mc.merill.net) — https://mc.merill.net/message/MC1509799 (published 2026-10-15); RM571884 — https://mc.merill.net/message/RM571884 (upd 2026-10-20)
- Cowork: available plugins — https://learn.microsoft.com/en-us/copilot/cowork/cowork-available-plugins (2026-10-26 / 2026-10-29)
- Cowork: use (skills, Skill Report, questions, edit-in-place) — https://learn.microsoft.com/en-us/copilot/cowork/use-cowork (2026-09-14 / 2026-10-21)
- Cowork: customize (custom instructions 20 KB, upload/share) — https://learn.microsoft.com/en-us/copilot/cowork/cowork-customize (2026-09-14 / 2026-10-21)
- Cowork: projects — https://learn.microsoft.com/en-us/copilot/cowork/cowork-projects (2026-10-27 / 2026-10-28)
- Cowork: elicitation forms — https://learn.microsoft.com/en-us/copilot/cowork/cowork-elicitation-forms (2026-05-27 / 2026-10-21)
- Cowork: application card (Skill Management; memory conversation-scoped) — https://learn.microsoft.com/en-us/copilot/cowork/responsible-ai/copilot-cowork-application-card (2026-09-22 / 2026-10-29)
- Cowork: models — https://learn.microsoft.com/en-us/copilot/cowork/cowork-models (2026-09-14 / 2026-10-24)
- Cowork: FAQ — https://learn.microsoft.com/en-us/copilot/cowork/cowork-faq (2026-09-29 / 2026-10-29)
- Cowork: what's new — https://learn.microsoft.com/en-us/copilot/cowork/whats-new (2026-10-28 / 2026-10-29)
- Cowork: mobile — https://learn.microsoft.com/en-us/copilot/cowork/cowork-mobile (2026-09-11 / 2026-10-21)
- Cowork: plugin development — https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development (2026-10-02 / 2026-10-21)
- Manage plugins, skills, MCP servers (admin) — https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers (2026-10-14 / 2026-10-23)
- Copilot personalization and memory (Chat) — https://learn.microsoft.com/en-us/microsoft-365/copilot/copilot-personalization-memory (2026-09-02 / 2026-10-31)
- Usage-based billing / spending policies — https://learn.microsoft.com/en-us/microsoft-365/copilot/usage-based-billing-manage-copilot-credits (2026-10-28)
- copilot-camp lab CWRK01 — https://microsoft.github.io/copilot-camp/pages/copilot-cowork/01-cowork-skills/ ; samples https://github.com/microsoft/copilot-camp/tree/main/src/cowork
- Microsoft Korea (ModernWork) posts — https://microsoft.github.io/mwkorea/copilot/CopilotLegalPlugins/ (upd 2026-10-16); https://microsoft.github.io/mwkorea/copilot/WordCopilotLegalPlugins/ (checked 2026-09-29)

**Secondary**
- Artificial Lawyer, "Microsoft Launches Copilot for Legal – Brace For Impact" — https://artificiallawyer.com/2026/10/15/microsoft-launches-copilot-for-legal-brace-for-impact (2026-10-15)
- LawNext (Ambrogi), "Microsoft Launches Copilot for Legal…" — https://lawnext.com/2026/10/microsoft-launches-copilot-for-legal-becoming-the-fourth-ai-giant-in-five-months-to-build-out-a-legal-vertical.html (2026-10-15)
- Legaltech Hub — https://www.legaltechnologyhub.com/contents/microsoft-launches-copilot-for-legal-with-legal-specific-skills-and-partner-built-plugins/ (2026-10-15, paywalled)
- 2toLead (Khipple, MVP), "Copilot for Legal 90-day metrics" — https://www.2tolead.com/insights/microsoft-copilot-for-legal-90-day-metrics (2026-10-18)
- iManage, "Adding the iManage plugin for Copilot Cowork" — https://docs.imanage.com/ai/mcpserver-user-help/en-US/Adding_the_iManage_plugin_for_Copilot_Cowork.html (undated); iManage blog https://imanage.com/resources/resource-center/blog/knowledge-work-microsoft-copilot-cowork/
- Harvey blog (Copilot Chat agent, Q2 2026) — https://www.harvey.ai/blog/harvey-accelerates-enterprise-ai-with-agentpowered-platform-and-microsoft-365-copilot (2026-03-04); release notes behind login (help.harvey.ai)
- LawVu playbooks (vendor feature) — https://help.lawvu.com/en/articles/13904561-manage-playbooks

**Open source**
- anthropics/claude-for-legal — https://github.com/anthropics/claude-for-legal (Apache-2.0; scratchpad clone `src-claude-for-legal/`)
- anthropics/skills `skills/skill-creator` — https://github.com/anthropics/skills/tree/main/skills/skill-creator (fetched 2026-10-31)
- anthropics/knowledge-work-plugins `legal/` — https://github.com/anthropics/knowledge-work-plugins/tree/main/legal
- kesslernity/awesome-copilot-cowork-skills — https://github.com/kesslernity/awesome-copilot-cowork-skills (CC BY-SA 4.0)
- 4444j99/a-i--skills `setup-cowork` — https://www.skills.sh/4444j99/a-i--skills/setup-cowork

**Repo inputs**
- `projects/spaarke-legal-ai-accelerator-r1/notes/poc-2026-10-30-cowork-nda-review.md` (§2.5, §3.1–3.4, Appendix I)
- `.claude/agent-memory/researcher/{copilot-for-legal-mechanics-walkthrough-2026-10-29, cowork-skill-runtime-and-mvp-claims-2026-10-30, cowork-spe-playbooks-automation-2026-10-30, copilot-legal-taxonomy-and-practice-pack-dev-refs-2026-10-29, practice-pack-content-sources-2026-10-29}.md`

## Recommended follow-ups (live-tenant)
1. Run `/create-playbook` with 3 precedents; record output type, name, folder; then attach it in Word "Attach from Cloud" and run Review.
2. Run `/edit-contract` on a OneDrive `.docx`; open in Word and check whether changes are `w:ins/w:del` tracked changes or plain edits.
3. Write a test skill whose body says "use Skill Management to create skill `probe-child` with the following SKILL.md …"; record whether a child skill appears in `Documents/Cowork/skills/` and what its Skill Report says.
4. Ask Cowork to "save this as `Documents/Cowork/Profile/legal-profile.md`" and check where the file actually lands; then in a new session ask a skill to read it without attaching.
5. Check whether Legal for Cowork's 20 skills count toward the 50-skill cap.
6. Check the Ignite 2026 session catalog (ignite.microsoft.com) for Cowork / legal sessions once published.
