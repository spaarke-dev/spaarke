---
name: word-copilot-pane-plugins-playbooks-addin-2026-10-31
description: Word Copilot pane state 2026-10-31 - Manage plugins & skills documented (Legal = 3 skills sharing Cowork names); org/user plugins in Word undocumented (Office.js skills Excel-only preview); no Office.js API to open/prefill/toggle Copilot (add-in-as-agent docs redirect); model picker GA (RM558440), Anthropic outside EUDB; / = file picker, @name invokes skills; org prompts not in Word pane; DAs/CEAs in All agents but no doc write; playbook still .docx per review, bundle only via skill references/; 11 live tests
metadata:
  type: reference
---

# Word Copilot pane: plugins, playbooks, model picker, starters, add-in launcher, agents (research as of 2026-10-31)

Tags: **C** = Cited (primary Microsoft source) · **S** = Secondary (MVP / press / vendor) · **I** = Inferred from cited facts or the owner's screenshots · **U** = Unknown / not documented. Dates are `ms.date / updated_at` for Learn, publish date for blogs and MC/RM items. Owner tenant screenshots (2026-10-31) are treated as observed facts, tagged **Obs**.

## Summary

1. **The owner's screenshots are consistent with Microsoft's docs; our prior notes under-stated what the Word pane does today.** The support page for Copilot for legal documents the exact path Copilot in Word desktop > `…` > **Manage plugins & skills** > toggle **Legal** (C). The three per-skill toggles seen in Word (Legal reasoning, Edit contract, Draft new contract) carry the same names as three of the 20 Legal for Cowork skills (`legal-reasoning`, `edit-contract`, `draft-new-contract`), so the Word "Legal" plugin is most plausibly the same skill catalogue host-filtered to three skills (I). Microsoft still describes them as "two legal plugins" with separate admin switches (C). Nothing documents that an **organization-built or user-uploaded plugin** appears or runs in Word's pane: the admin doc says plugins are "primarily available to end users in the Copilot channel" (C, 2026-10-14/23); the plugin-skills doc says "host support vary by Microsoft experience … documentation primarily covers adding skills to agents" (C, 2026-09-30/10-27); Office.js-script skills are **Excel-only preview** (build 2608, Beta/Current Channel Preview) (C, 2026-08-13); `office-js-docs-pr` live has **no Word skills page** (C, listing verified 2026-10-31). Word is on the registry's target list "as these updates roll out" (C, 2026-09-30). Net: the Plugins menu has reached Word; org plugins in it are **U** and need a 5-minute live test (T1/T9 below).
2. **Playbooks are unchanged**: Word takes a `.docx` per review via **Upload file** or **Attach from Cloud**; no default, memory, library, or `.md/.pdf` is documented (C). The only "bundle and apply automatically" paths are Cowork/DA skill `references/` (`.docx`, `.pdf`, `.md` are allowed resource types; C) plus a `/`-referenced file in Cowork custom instructions or a Project (C for the mechanism, I for the effect). No automation path exists in Word.
3. **"/" in Word** is documented as a file/email/meeting reference menu, not a skill or plugin picker (C). Skill invocation in Office apps uses `@skill-name` (Excel preview, PowerPoint; Word legal uses `@Draft-new-contract`) (C). Whether choosing a plugin/skill narrows tools for the turn is **U** in Word; in Cowork, plugin on/off is per conversation via **Sources & Skills**, `/name` invokes a skill, and the number on a plugin chip is not documented (C/U).
4. **Model picker in Word is GA** (RM558440 "Choose Anthropic models when editing with Copilot in Word", Launched, GA May 2026, Desktop/Mac/Web; support page: "Model Selector lets you pick a reasoning level, choose Auto, or select a supported model provider such as OpenAI or Claude") (C). Model *names* are not documented by Microsoft for Word; Office-Watch (S, 2026-07-13) listed Auto/Claude Opus 4.8 + Sonnet 5/GPT 5.6 — already stale. Anthropic models are **outside the EU Data Boundary**; EU/EFTA/UK tenants default off, with a separate "Copilot in Microsoft 365 apps with Anthropic models" switch for Word/Excel/PowerPoint (C, 2026-09-03). Whether the legal plugin honours the picker: **U**.
5. **Start experience**: prompt starters change when the Legal plugin is on (Obs → plugins contribute starters, I); Microsoft publishes no per-org or per-document-type starter mechanism for the Word pane. **Organizational prompts** (admin center Copilot > Prompts; 1,000 cap, pin 4) display only in Copilot Chat, Edge and Teams (C, 2026-09-21). Document-type detection is limited to "auto-detects a legal document" (C).
6. **Add-in ↔ Copilot**: there is no Office.js API to open the pane, send a prompt, toggle a plugin, or attach a file (C by absence). The Build-2025 "Copilot agents in Office add-ins / add-in actions" pages now **redirect to the Office Add-ins overview** (verified 2026-10-31); the overview still says "Create custom Copilot agents that read and write to documents open in Office applications (preview)" and links only to the Excel skill sample (C, 2026-09-17). Prompt Gallery "Copy prompt link" exists for Word/PowerPoint but the recipient behaviour is undocumented (U). A Spaarke ribbon can today: open the right playbook, run Spaarke's own Office.js review/redline, and place a prepared prompt on the clipboard — it cannot drive Copilot (I).
7. **"All agents"** in Word lists declarative agents (Agent Builder, Agents Toolkit, Copilot Studio published to the M365 Copilot channel, Microsoft-installed) and, since Oct 2025, custom engine agents (C). A Spaarke DA per practice (instructions + SharePoint playbook library + actions, optionally 8 skills) can be installed and chatted with in Word; **acting on the open document is not a DA capability** and 2025 community reports say "insert and selection are still not working" and SharePoint knowledge worked only in Word Online (S, 2025-08 — possibly stale). DA skills have no documented host matrix (U); ISVs cannot yet ship DAs with skills via Partner Center (C).

---

## Q1–Q7 answer table

| # | Question | Answer | Tag / source (date) |
|---|---|---|---|
| 1a | Is Word's "Legal" plugin the same package as Legal for Cowork? | Microsoft: "Two legal plugins are available: one for Copilot in Word and one for Copilot Cowork." Separate admin switches (Cloud Policy "Legal Skills in M365 Copilot in Word" vs Agents > Tools "Cowork for Legal"/"Legal for Cowork"). Word exposes three skill toggles whose names equal three Cowork skill names → same skill catalogue, host-filtered. Not stated by Microsoft. | C: manage-copilot-for-legal (2026-10-13/15); MC1509799 (2026-10-15); support get-started (undated). Obs + I for "same catalogue" |
| 1b | Does Word now host the plugin registry's Plugins menu? | Yes, the menu exists ("Manage plugins & skills" documented as the Legal toggle path). Registry blog: registry spans "Microsoft 365 apps (Word, Excel, and PowerPoint)"; Plugins menu reaches "supported Microsoft 365 apps" "as these updates roll out". Admin doc: plugins "primarily available to end users in the Copilot channel". | C: support get-started; blog 4559682 (2026-09-30); manage-plugins-skills-mcp-servers (2026-10-14/23) |
| 1c | Can org-built / user-uploaded plugins (SKILL.md + references/scripts/assets + connectors) appear and run in Word's pane? | **U.** No Learn page documents it. Plugin-skills doc: "Skill availability, sharing, automatic use, and host support vary by Microsoft experience. Current documentation primarily covers adding skills to agents." Plugins-overview: "Check support before you build." Only Excel documents custom plugin skills in an Office app (preview). A 2024 forum report of Teams/Outlook plugins not showing in Word is pre-registry (S). Live test T1/T9. | C: plugin-type-skills (2026-09-30/10-27); plugins-overview (2026-09-30/10-12); excel-skills (2026-08-13). S: techcommunity 4272428 (2024-10-16) |
| 1d | Do scripts execute in Word? Same sandbox as Cowork? Do references/assets load? | Word: **U**. Excel preview: "Copilot in Excel automatically creates a runtime, loads Office.js and executes the scripts in your skill"; parameters not supported; scripts live in the package, not hosted; `resources/*.md` are read before running. DA skills: sandbox with **no network**, no package install, files stored in tenant SPE containers. Cowork: scripts run, runtime undocumented (prior note). None of these is documented for Word. | C: excel-skills, excel-copilot-skill (2026-08-13); declarative-agent-skills (2026-10-26/27). U: Word |
| 1e | Billing: does a plugin skill in Word consume Copilot Credits? | **I: no**, under USL. UBB services list (2026-10-30) = Cowork, advanced SharePoint/OneDrive, Work IQ API, Copilot Managed Runtime, Teams Phone Agent, Code. In-app Copilot and model selection are USL ("GPT 5.6 and Sonnet" fair use; "Opus" with limits); "Reach usage limits in the USL (coming soon)" will offer credits. Frontier models (Astra, Fable) are UBB. Per-plugin metering in Word: not documented. | C: usage-based-billing-overview-copilot-credits (2026-10-30); user-subscription-license-usage-based-billing (2026-10-05). I |
| 1f | Admin controls | Word Legal: Cloud Policy (default on). Plugins: Agents > **Tools** > Plugins (Users: all/none/specific; Install/Uninstall/Block/Delete), **Registry** > Upload manifest (scope users), **Requests** (access + MCP approval; AI Admin/Global Admin); Agents > **Settings** > Agent and plugin access (publisher categories Microsoft / your org / certified external). Model provider: Copilot > Settings > **AI providers operating as Microsoft subprocessors** (users/groups); EU/EFTA/UK: "Copilot in Microsoft 365 apps with Anthropic models". Tool-level gateway enforcement for MCP. | C: manage-plugins-skills-mcp-servers (2026-10-14/23); connect-to-ai-subprocessor (2026-09-18); copilot-anthropic-apps (2026-09-03) |
| 2a | How a user supplies a playbook in Word | Prompt "Review this contract using a playbook" → Copilot asks → **Upload file** (local) or **Attach from Cloud** (M365 picker) → `.docx` only → topics/rules (editable) → **Run Review** → tracked changes. "Can currently only perform contract reviews from playbooks." | C: support get-started (undated, © 2026) |
| 2b | Remembered default? Library setting? | **None documented.** No save/reuse/default/library in support, Learn, MC, transparency note. Rules are parsed per review. | C (negative) |
| 2c | Bundle a playbook in a plugin/skill `references/` and apply automatically? | Cowork plugin skills: companions ≤20 files/5 MB/10 MB (prior C). DA skills: resource types include `.docx`, `.doc`, `.pdf`, `.md`, `.txt` (C) → a playbook can ship inside a skill and the SKILL.md can instruct "apply `references/nda-playbook.md`". Whether Cowork's **built-in** `review-contract` will consume a referenced file instead of an attachment: **U**. For the **Word** legal plugin there is no bundle mechanism at all. | C: declarative-agent-skills (2026-10-26); I; U |
| 2d | Custom instructions / Project pointing at a playbook? | Cowork Preferences: "Type `/` to reference a specific skill, file, person, or meeting" (20 KB); Projects carry files + instructions that override global. So a user can `/`-reference the playbook file in Preferences or a Project (C) and Cowork includes it as context (I). Not available in Word. | C: cowork-customize (2026-09-14/10-21); cowork-projects (prior) |
| 2e | Formats: `.docx` only, or `.md/.pdf`? Same files in Cowork? | Word: `.docx` only (C). Cowork accepts `.pdf/.md/.docx` attachments generally (C prior); whether `review-contract` parses `.md/.pdf` as a playbook: **U**; whether it treats the Word `.docx` identically: **U** (plausible, I). `create-playbook` output format/location: **U**. | C/U |
| 2f | "Attached to each review" in practice; automation path | Each Word review session begins with the attach step; rules are transient; nothing persists. Automation: none in Word. Cowork: Automations (scheduled/event) can run a skill whose instructions reference a playbook file (C for Automations; I for playbook). | C: support; use-cowork (prior). I |
| 3a | What does "/" show in Word's pane? | "Typing `/` opens a menu where you can pick a document, email, or meeting for Copilot to use." No skills/plugins. Training module: `/` then paste a OneDrive URL; up to three files (S). | C: welcome-to-copilot-in-word (undated); S: Learn training module, DataCamp |
| 3b | Does choosing a plugin/skill narrow tools for the turn/session in Word? | **U.** Legal plugin "will tend to use legal skills for the rest of your chat" once a legal doc is detected (C). Explicit `@Draft-new-contract` targets one skill (C). | C: support get-started |
| 3c | Cowork chips ("Legal (19)", skill chip) | Plugin on/off per conversation in **Sources & Skills**; "When a plugin is enabled, its skills appear alongside Cowork's built-in skills … as chips in the side panel"; "Use the conversation Sources picker to choose the plugins and skills you need for a request"; `/skill-name` invokes. The count badge is undocumented (likely enabled-skill count, I). Per-skill toggles inside a plugin: **not documented** for Cowork (U) — Word shows them (Obs). | C: cowork-plugins (2026-08-27/10-29); cowork-customize (2026-09-14/10-21) |
| 4a | Model selection in Word documented? | Yes. RM558440 (Launched, GA May CY2026, Desktop/Mac/Web): "select Anthropic models, in addition to OpenAI models, when editing a Word document using Copilot." Support: "Model Selector lets you pick a reasoning level, choose Auto, or select a supported model provider such as OpenAI or Claude." RM545012: Agent Mode model choice (GA Feb 2026). | C: mc.merill.net RM558440 (upd 2026-06-02); welcome-to-copilot-in-word; RM545012 |
| 4b | Which models | Not named by Microsoft for Word. Office-Watch (2026-07-13): Auto (Quick Response/Think Deeper), Claude Opus 4.8 + Sonnet 5, GPT 5.6 (Quick/Think Deeper) — stale vs the owner's menu. USL page names GPT 5.6, Sonnet (fair use), Opus (limits), Astra/Fable (UBB). | S: office-watch; C: USL/UBB page (2026-10-05) |
| 4c | EU Data Boundary | "Anthropic models … are currently excluded from the EU Data Boundary … Customers within the EU Data Boundary and customers in the UK have Anthropic models disabled by default." App-level switch for Word/Excel/PowerPoint on by default for EU/EFTA/UK tenants created after 2026-03-25; IP setting decommissioned 2026-05-01. Fable 5.1 may be subprocessor for eligible orgs; "Anthropic models with Data Retention" (Fable 5.0, Mythos 5) separate opt-in, off everywhere. | C: connect-to-ai-subprocessor (2026-09-18); copilot-anthropic-apps (2026-09-03) |
| 4d | Admin control | Copilot > Settings > View all > AI providers operating as Microsoft subprocessors (Anthropic; users/groups); disable removes Claude option everywhere incl. Word/Excel/PPT Agents. | C: same |
| 4e | Does the Legal plugin honour the picker? | **U.** Transparency note names no model. | U |
| 5a | Starters customizable per plugin? | Obs: starters switch to legal ones when Legal is on → Microsoft plugins contribute starters (I). No doc for org/user plugins. DAs define `conversation_starters` (C); whether they render in Word's pane when the DA is selected: **U** (likely yes, I). | Obs/I; C: declarative-agent manifest (prior) |
| 5b | Per org? | Organizational prompts: admin center Copilot > Prompts; AI Administrator / Search Editor; 1,000 cap; pin 4; "Supported apps" = Copilot web / Copilot work; displayed in **Copilot Chat, Edge, Teams** — not the Word pane. Prompt Gallery save/share applies to Word/PowerPoint; "Copy prompt link"; recipient behaviour undocumented. | C: organizational-prompts (2026-09-21/10-31); share-your-best-prompts (undated) |
| 5c | Per document type (NDA vs patent)? | Only "auto-detects a legal document" (C). No detection config. Cowork routes by skill description match (C prior). | C |
| 6a | Office.js API to open Copilot pane / send prompt / toggle plugin / attach file? | **None.** No Copilot namespace in Office.js; community asks for programmatic pane control go unanswered. | C (absence); S: techcommunity idea 4517001 |
| 6b | "Copilot agents in Office add-ins" (add-in actions, Build 2025) | Docs **pulled**: `/design/agent-and-add-in-overview` and `/develop/agent-and-add-in` (all locales tried) redirect to the Office Add-ins overview. Overview (2026-09-17) keeps "Create custom Copilot agents that read and write to documents open in Office applications (preview)" and links only to "Skill for Copilot in Excel (preview)". Replacement model = plugin package with `agentSkills[]` + Office.js scripts; Excel-only preview; `atk install --scope Personal`; invoke via **+ > All Skills** then `@name`; "does not run in standalone Microsoft 365 Copilot"; can share a package with an add-in but not code. | C: office-add-ins overview (2026-09-17); excel-skills, excel-copilot-skill (2026-08-13); Office-Add-in-samples README (fetched 2026-10-31) |
| 6c | Deep links / URL schemes | `ms-word:ofe|u|<url>` opens files only (prior C). Prompt Gallery link: format and landing surface undocumented (U). No Copilot URL scheme for Word (C absence). | C/U |
| 6d | Declarative agents surfaced in Word | Yes: Agent Store "accessible across Microsoft 365 apps including Teams, Outlook Word, Excel, and PowerPoint"; MC1168289: CEAs join DAs "already available" in Word/Excel/PPT/OneNote; installed agents "appear in the Copilot pane". | C: copilot-agent-store (2026-04-17/10-31); MC1168289 (2025-10-08) |
| 6e | GA vs preview | GA: unified manifest (Jul 2026), NAA, DAs/CEAs in Word, model picker, Legal plugins (rolling to mid-Dec 2026). Preview: Excel Office.js skills; "add-in as Copilot agent" (docs withdrawn). Not shipped: Word Office.js skills, Word OneDrive personal skills (PowerPoint has MC1434580/81/82, Jul–Aug 2026). | C |
| 6f | What a "Spaarke" ribbon with a practice dropdown can do today | Ribbon dropdown → task pane: (1) pick practice → open the governed playbook from SPE/SharePoint; (2) run Spaarke's own deterministic review/redline via Office.js TrackAll (prior C); (3) put a ready prompt (with file name to attach) on the clipboard and tell the user to paste into Copilot; (4) same unified package can carry a Spaarke DA so it appears in All agents. It cannot open the pane, pre-fill, toggle plugins, or attach a playbook for Copilot. | I from C facts |
| 7a | Which agents appear in "All agents" | DAs (Agent Builder, Agents Toolkit, Copilot Studio → "Microsoft Copilot and Microsoft Teams" channel after admin approval), Microsoft-installed agents, partner agents, CEAs. Agent Builder itself runs only in the Copilot app/Teams (not Word). | C: copilot-agent-store; MC1168289; agent-builder (2026-10-26/27) |
| 7b | Can a Spaarke DA per practice (instructions, knowledge = playbook library, actions) be invoked in Word on the open document? | Invoked: yes. "On the open document": DAs have no document read/write capability; 2025 community reports: "insert and selection are still not working within the agent", "interaction with indexed SharePoint only works on Word Online and not Word local" (S, possibly stale). Skills in DAs: 8 per agent, sandbox no network, host matrix undocumented (U); IB tenants: users can't add skills to their own DAs; ISV DAs with skills not yet via Partner Center. | C: declarative-agent-skills; S: Learn Q&A 5490114 (2025-07/08) |

---

## Corrections to prior notes

| Prior claim (note, date) | Status | Correction / sharpening |
|---|---|---|
| "The Word legal plugin is NOT extensible" (2026-10-31) | **Partly stale** | Still no documented way to add org content/skills to Word's pane, but the pane now has the registry **Plugins menu** ("Manage plugins & skills") with per-skill toggles; the Legal plugin is a 3-skill bundle. Org/user plugins in Word: **untested**, not "impossible". |
| "takes only a .docx playbook attached per review" | **Confirmed** | Unchanged (support page). |
| "undocumented model" (Word) | **Superseded** | Word has a GA user **model picker** (RM558440, May 2026; support page). The legal plugin's model routing is still undocumented. |
| "partner plugins reach only Cowork/Chat" (2026-10-29/31) | **Softened** | Plugins menu has reached Word desktop. Whether any partner/org plugin is listed there is **U**; admin doc still says "primarily … the Copilot channel". |
| "Word custom skills exist only for Excel/PowerPoint" (2026-10-31) | **Confirmed** | `office-js-docs-pr` live `docs/word/` has no skills page (verified 2026-10-31). |
| "Build-2025 add-in actions docs pulled" (2026-09-01, from git) | **Confirmed on live Learn** | Both pages redirect to the Office Add-ins overview in every locale tried. |
| "Declarative agents DO surface inside Word" (2026-09-01) | **Confirmed + extended** | CEAs too (MC1168289, Oct 2025). Agent Store reachable from Word/Excel/PPT/Outlook/Teams. |
| "Cowork model list …; EU Data Boundary tenants: GPT only" (2026-10-31) | **Consistent** | Same rule applies to Word/Excel/PPT via the AI-providers setting; EU app-level switch exists for new tenants. |
| Owner's screenshot items not previously recorded | **New** | Word `…` menu: Manage plugins & skills / All agents / Send feedback; legal starters; per-skill toggles; model submenus; Cowork "Added (3)" incl. org plugin "Patent" with notification dot; "Legal (19)" chip. |

---

## Implications for the practice-accelerator design

1. **Do not design around a Word plugin.** No Microsoft doc supports org plugins or SKILL.md skills in Word's pane; Excel is the only Office app with a (preview) path. Keep T0 = Cowork plugin (skills + connector). Run T1/T9 once; if an org plugin shows up and triggers in Word, that is a bonus channel, not a dependency.
2. **Ship a declarative agent in the same unified package** ("Spaarke Legal" or one per practice) with knowledge = governed SharePoint playbook library and optional actions to the BFF, so a Spaarke entry appears in Word's **All agents** today. Expect chat-only behaviour in Word (no document write); verify SharePoint-knowledge grounding in Word desktop (T7).
3. **Playbook activation**: (a) Word — one governed SharePoint library with discoverable names so **Attach from Cloud** finds them in one search; a one-line "how to attach" card in the kit; (b) Cowork — bundle `.docx` + `.md` playbooks in skill `references/` and have the SKILL.md apply them; also give users a ready `/`-reference snippet for Preferences or a practice **Project**; (c) do not promise a default/remembered playbook anywhere.
4. **Add-in launcher** is viable only as Spaarke-native: ribbon → practice dropdown → task pane that opens the playbook, runs Spaarke's own Office.js redline, and copies a prepared Copilot prompt. Track `office-js-docs-pr` `docs/word/` for a Word skills page; when Office.js skills reach Word, the task-pane logic can be re-packaged as `agentSkills[]`.
5. **Model/EU guidance** in the kit: Word picker exists; EU/UK tenants need the AI-providers setting; frontier models bill credits; the legal plugin's model is unknown — advise testing a review on Auto vs Claude vs GPT.
6. **Starters**: no org starters in Word's pane; use organizational prompts for Copilot Chat/Teams and DA `conversation_starters` for the Spaarke agent.

---

## Live-tenant tests the owner can run (≈5 minutes each)

| # | Test | Record |
|---|---|---|
| T1 | Word > Copilot `…` > Manage plugins & skills. | Every plugin listed; is the org "Patent" plugin (seen in Cowork) or any uploaded Cowork plugin present? Toggle states; per-skill toggles for non-Microsoft plugins? |
| T2 | In the Word pane type `/`, then `@`, then `@legal` and `@edit`. | What each menu lists (files / agents / skills); whether `@edit-contract` resolves. |
| T3 | Legal on, pick Claude; "Review this contract using a playbook"; repeat with GPT. | Does the picker persist through the review flow; any "credits"/frontier badge; output differences. |
| T4 | Attach a playbook via Attach from Cloud (SharePoint library); finish; new chat: "Review using the same playbook as before." | Does Copilot remember or re-prompt? |
| T5 | Cowork > Customize > Plugins > Legal for Cowork detail page. | Can individual skills be toggled? Does 19 vs 20 reflect a disabled skill? What does the "Legal (19)" chip do when clicked? |
| T6 | Cowork `/review-contract` with the same playbook as `.docx`, then as `.md`. | Does it extract rules from `.md`; output differences; where files land. |
| T7 | Word > All agents: open a quick Agent Builder DA with SharePoint knowledge (a playbook folder). Ask: "Which clauses in this document conflict with the playbook?" Then "Insert a summary." | Does it see the open document; does insert work; SharePoint grounding on desktop. |
| T8 | Word pane: hover a response → Save prompt / Copy prompt link; open the link on another device. | Where the link lands (Copilot app vs Word) and whether the prompt pre-fills. |
| T9 | Admin: Agents > Tools > Registry > Upload the POC plugin manifest; scope to self. Wait 30 min and 24 h. | Appears in Cowork? Appears in Word's Manage plugins & skills? Triggers on a matching prompt in Word? |
| T10 | Legal plugin: turn off "Edit contract", ask for redlines; turn off "Legal reasoning", ask to summarize key terms. | Behavioural effect of per-skill toggles (maps toggles to transparency-note capabilities). |
| T11 | Word > `…` > model menu: screenshot full submenus; check for a "data retention"/Fable marker. | Current model names for the kit's guidance; EU/UK differences if a second tenant exists. |

---

## Reference links (with dates)

**Microsoft primary**
- Get started with Copilot for legal (Word toggle path, Upload file / Attach from Cloud, `.docx`, Run Review) — https://support.microsoft.com/en-us/word/get-started-with-copilot-for-legal (undated, © 2026)
- Copilot for Legal: Transparency Documentation (25 capabilities) — https://support.microsoft.com/en-us/word/copilot-for-legal-transparency-documentation (undated)
- Manage Copilot for legal — https://learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-copilot-for-legal (2026-10-13/15)
- MC1509799 Legal plugins in Word and Cowork — https://mc.merill.net/message/MC1509799 (2026-10-15); RM571884 (upd 2026-10-20)
- Introducing the Microsoft plugin registry — https://techcommunity.microsoft.com/blog/microsoft365copilotblog/introducing-the-microsoft-plugin-registry/4559682 (2026-09-30)
- Introducing Microsoft Copilot for legal — https://techcommunity.microsoft.com/blog/microsoft365copilotblog/introducing-microsoft-copilot-for-legal/4564246 (2026-10-15)
- What's new in Microsoft Copilot, September 2026 (PowerPoint skills; Word citations) — https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/what%E2%80%99s-new-in-microsoft-copilot--september-2026/4559107
- Manage plugins, skills, and MCP servers (admin) — https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers (2026-10-14/23)
- What is a Microsoft 365 Copilot plugin — https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugins-overview (2026-09-30/10-12)
- Skills as plugin capabilities — https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-type-skills (2026-09-30/10-27)
- Plan your plugin — https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/planning-guide (2026-09-30/10-12)
- Skills in declarative agents (sandbox, file types, limits) — https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-skills (2026-10-26/27)
- Declarative agents overview — https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview-declarative-agent (2026-09-30/10-27)
- Agent Builder overview — https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/agent-builder (2026-10-26/27)
- Agent Store in Microsoft Copilot — https://learn.microsoft.com/en-us/microsoft-365/copilot/copilot-agent-store (2026-04-17/10-31)
- MC1168289 CEAs in Word/Excel/PowerPoint/OneNote — https://mc.merill.net/message/MC1168289 (2025-10-08)
- Office Add-ins platform overview (Copilot agents preview line; Excel skill sample) — https://learn.microsoft.com/en-us/office/dev/add-ins/overview/office-add-ins (2026-09-17); `/design/agent-and-add-in-overview` and `/develop/agent-and-add-in` redirect here (verified 2026-10-31)
- Overview of Copilot skills for Excel (preview) — https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-skills (2026-08-13)
- Create a Copilot skill for Excel that uses Office.js (preview) — https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-copilot-skill (2026-08-13)
- Sample excel-copilot-skill — https://github.com/OfficeDev/Office-Add-in-samples/tree/main/Samples/excel-copilot-skill (fetched 2026-10-31)
- office-js-docs-pr live `docs/word/` (no skills page) — https://github.com/OfficeDev/office-js-docs-pr/tree/live/docs/word (listed 2026-10-31)
- MC1434580 / MC1434581 PowerPoint OneDrive custom skills (web/Mac) — https://mc.merill.net/message/MC1434580 , https://mc.merill.net/message/MC1434581 (upd 2026-08-03)
- RM558440 Choose Anthropic models when editing with Copilot in Word — https://mc.merill.net/message/RM558440 (upd 2026-06-02; Launched; Desktop/Mac/Web)
- RM545012 Agent Mode in Word model choice — https://mc.merill.net/message/RM545012
- Welcome to Copilot in Word (`/` menu; Model Selector) — https://support.microsoft.com/en-us/word/welcome-to-copilot-in-word (undated)
- Copilot in Microsoft 365 apps with Anthropic models (EU/EFTA/UK switch) — https://learn.microsoft.com/en-us/microsoft-365/copilot/copilot-anthropic-apps (2026-09-03/10-31)
- Anthropic models in Microsoft Online Services — https://learn.microsoft.com/en-us/microsoft-365/copilot/connect-to-ai-subprocessor (2026-09-18/10-31)
- Word, Excel, PowerPoint Agents (admin; Anthropic-only) — https://learn.microsoft.com/en-us/microsoft-365/copilot/wordexcelppt-agents (2026-03-26/10-31)
- USL and UBB — https://learn.microsoft.com/en-us/microsoft-365/copilot/user-subscription-license-usage-based-billing (2026-10-05/10-31)
- Usage-based billing overview (metered services list) — https://learn.microsoft.com/en-us/microsoft-365/copilot/usage-based-billing-overview-copilot-credits (2026-10-30/31)
- Organizational prompts — https://learn.microsoft.com/en-us/microsoft-365/copilot/organizational-prompts (2026-09-21/10-31); MC1396361; RM569425 (delegated publishing, GA Sept CY2026)
- Share your best prompts (Prompt Gallery; Word/PowerPoint) — https://support.microsoft.com/en-us/microsoft-365-copilot/share-your-best-prompts-with-others (undated)
- Export Prompt Gallery prompts (admin) — https://learn.microsoft.com/en-us/microsoft-365/copilot/copilot-prompt-gallery-export-prompts (2025-10-31)
- Cowork: use plugins (Sources & Skills, chips) — https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugins (2026-08-27/10-29)
- Cowork: customize (Preferences `/` reference; Plugins/Skills tabs; upload) — https://learn.microsoft.com/en-us/copilot/cowork/cowork-customize (2026-09-14/10-21)
- Learn Q&A "The agent isn't available in Word…" — https://learn.microsoft.com/en-us/answers/questions/5490114/ (2025-07-23 → 2025-08-08)

**Secondary**
- Office-Watch, "Which Copilot Model Should You Use in Word?" — https://office-watch.com/2026/choose-copilot-model-word/ (2026-07-13)
- Office-Watch, "Microsoft Copilot September 2026 updates" (menu image incl. "Manage plugins & skills") — https://office-watch.com/?p=77878 (2026-10-06)
- aguidetocloud, "Copilot Skills in Word: What Exists Today" — https://www.aguidetocloud.com/blog/microsoft-365-copilot-skills-word/ (2026-07-31)
- techcommunity discussion "Copilot 365 plugins for Word / PowerPoint" — https://techcommunity.microsoft.com/discussions/microsoft365copilot/copilot-365-plugins-for-word--powerpoint/4272428 (2024-10-16; reply 2025-07-03)
- techcommunity idea "No programmatic way to pin the task pane" — https://techcommunity.microsoft.com/idea/microsoft365developerplatform/no-programmatic-way-to-pin-the-task-pane-for-users/4517001

**Repo inputs checked**
- `.claude/agent-memory/researcher/{word-vs-cowork-legal-boundary-and-onboarding-2026-10-31, copilot-for-legal-mechanics-walkthrough-2026-10-29, word-extensibility-platform-2026-09-01, ms-copilot-for-legal-cowork-registry-2026-10-29, copilot-legal-taxonomy-and-practice-pack-dev-refs-2026-10-29}.md`
- `knowledge/declarative-agents/NOTES.md`, `knowledge/m365-copilot/NOTES.md` (both ⚠️ STUB; curated 2026-05-14; no Office add-in / Copilot-in-Word material; DA schema notes stop at v1.7 and predate skills-in-DAs, plugin registry, Agent 365 Tools admin, and CEAs in Office apps → stale for this project). No `knowledge/office-add-ins/` topic exists.

---

## Proposed researcher-memory entry (for the main session to write under `.claude/agent-memory/researcher/`)

```markdown
---
name: word-copilot-pane-plugins-playbooks-addin-2026-10-31
description: Word Copilot pane state (2026-10-31) — "Manage plugins & skills" menu documented (Legal toggle path), Legal = 3-skill bundle sharing Cowork skill names; no doc for org/user plugins or SKILL.md skills in Word (Excel-only preview; office-js-docs-pr has no word-skills page); add-in-as-Copilot-agent docs redirect to overview; model picker GA (RM558440 May 2026, Anthropic outside EUDB, EU/UK default off); "/" = file/email/meeting picker; org prompts not in Word pane; DAs/CEAs appear in All agents but no document write; playbook still .docx attached per review, bundle only via skill references/ (.docx/.md/.pdf allowed) or Cowork `/`-referenced files; 11 live-tenant tests
metadata:
  type: reference
---
```

Index line for `MEMORY.md` (Word add-in / Office section):

`- [Word Copilot pane plugins/playbooks/add-in (2026-10-31)](word-copilot-pane-plugins-playbooks-addin-2026-10-31.md) — Plugins menu in Word documented; Legal = 3 Cowork-named skills; org plugins in Word U; Office.js skills Excel-only; model picker GA, EUDB excluded; no Copilot API for add-ins`
