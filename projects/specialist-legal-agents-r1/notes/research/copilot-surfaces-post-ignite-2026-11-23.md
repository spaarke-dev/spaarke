# Microsoft Copilot surfaces after Ignite 2026: capability boundaries (research as of 2026-11-23)

Purpose: facts for designing Spaarke's attorney-facing legal agents. No recommendations.

Tags: **C** = Cited (Microsoft primary source; URL and page date in the source list). **O** = Observed (our tenant; file named). **I** = Inferred from cited facts. **U** = Unknown or undocumented. **S** = Secondary (non-Microsoft, mirror or search snippet). Learn pages fetched on 2026-11-23 carry no ms.date in the fetch, so they are cited as "Learn, fetched 2026-11-23".

Builds on, and does not repeat: the accelerator-r1 researcher notes `word-vs-cowork-legal-boundary-and-onboarding-2026-10-31`, `declarative-agents-in-word-2026-11-09`, `word-plugin-skills-not-executed-2026-11-02`, `word-vs-cowork-plugin-kinds-harvey-legora-tuning-2026-11-02`, `copilot-as-spaarke-surface-and-frontier-2026-11-05`; accelerator design.md section 2.4; mcp-bff-api-r1 `phase0-results.md`; this project's `design-synopsis-2026-11-22.md`.

Reading coverage: the Ignite Book of News was read in full (both halves; the synopsis read only the first 100,000 of about 162,000 characters). The Cowork-at-Ignite and Copilot-at-Ignite posts were read. The "Office at Ignite 2026" and "Home: your starting place" community posts could not be fetched (403); the first was read via a Japanese Microsoft mirror (translated), the second only as a search snippet (S).

---

## 1. Copilot Home

**What it is.** A tab inside the Copilot app that is "your new starting point in Copilot, where Chat and Cowork come together in one place" (C, Microsoft blog 2026-09-25). Learn: "The Home tab provides a unified starting point for Chat and Cowork in the Copilot app. Users can switch between experiences from either the left navigation or the prompt box" (C, copilot-overview). Support page: "From Home, you type in the message box and choose Chat or Cowork" and Home is "still rolling out" (C, support, undated, (c) 2026). The Book of News says Home, "announced in September and now rolling out worldwide, is the new starting point in Copilot" (C, 2026-11-17).

**Status: GA per Microsoft's Ignite blog, rollout still in progress, docs lag.**
- C: Copilot blog 2026-11-17: "Home, where Chat and Cowork come together, and Office in Copilot are now generally available."
- C: Book of News 2026-11-17: "now rolling out worldwide"; Home carries no GA/preview label in the text read.
- C: the 2026-09-25 blog said "Home and Code will start rolling out in our Frontier program in the coming weeks" (Chat and Cowork themselves were GA). So Home moved Frontier (Oct) to GA (Nov 17). One search summary claimed the Sept post already said GA; the post text does not.
- C (stale): Learn Cowork "What's new", October 2026 row still reads "Open Cowork in the Home tab (Frontier)". The November section has no Home row. The support page still says "still rolling out".
- U: whether our dev tenant has Home today (our 2026-11-12/13/23 phase0 runs describe Chat and Cowork as separate surfaces; no Home observation).

**What it unifies.**
- C: Chat and Cowork behind one composer and one shared history; Auto "may recommend moving from Chat to Cowork" and, if the user agrees, Copilot transfers the task (blog 2026-11-17); Book of News: context moves with the task, Auto recommendation is GA.
- C: Office in Copilot (Word, Excel, PowerPoint canvas beside the conversation, entered from Chat or Cowork; techcommunity 4561230, 2026-10-09; GA per Ignite blog).
- C: a Library of recent Word/Excel/PowerPoint files, pages and images (4561230). Plugins and "Agents & Skills" appear in the same app (Learn Cowork plugins: "Select Home > Plugins").
- C (Frontier/preview only): Workspaces (evolution of Copilot Notebooks), Today (private preview), Autopilot, Code (GA "in the coming weeks"; December).
- S (search snippet of the community post "Home: your starting place for work in the Microsoft Copilot app"): a shared place for Chat and Cowork history, a Today view, and Projects/Workspaces holding context.
- C: Pages and Notebooks appear only as navigation sections on the support page; Search is listed as an experience (support page). Whether Home "unifies" search, Pages or Notebooks is U beyond that.
- C negative: the 2026-09-25 blog says nothing about memory or Projects; the 2026-11-17 blog says nothing about memory, Projects or mobile for Home.

**Where it runs.** C: the Copilot app on Windows and Mac desktop, web (copilot.cloud.microsoft), iOS and Android: "Wherever you use Copilot on your desktop, web, or mobile, it's the same assistant" (support). The blogs do not state platforms for Home itself. Home on mobile: U. Home inside Teams or Outlook: U (Cowork itself works "in Outlook and Teams", Learn get-started; Outlook "Today" is a Frontier private preview).

**Is it the renamed Microsoft 365 Copilot app?** Yes, C: "The Microsoft 365 Copilot app is now called Microsoft Copilot. The primary URL ... is changing to copilot.cloud.microsoft" and "The Microsoft 365 Copilot Chat app is now called Microsoft Copilot Chat" (Learn copilot-overview). Home is a tab within that renamed app, not a separate product (I). Our phase0 notes use m365.cloud.microsoft/chat and `copilot.cloud.microsoft` interchangeably (O).

**Billing shown on Home.** C (2026-09-25 blog): Chat under the user subscription licence; Cowork, Code and Autopilot on usage-based billing. Learn copilot-overview: "If a usage-based experience requires a spending policy, the user is prompted to ... Request access."

---

## 2. Chat vs Cowork: partially collapsing

**Verdict: partially collapsing.** The shell, the extension model, the plugin catalog and governance and the Office-file layer are converging. The runtime, billing, memory and personalization, and declarative-agent hosting remain separate. Evidence per dimension:

| Dimension | Converging or separate | Evidence |
|---|---|---|
| Entry point | Converged | Home with Chat/Cowork toggle, GA 2026-11-17 (C, S2) |
| Chat to Cowork hand-off with context | Converged (GA) | Auto recommends the move; user approves; context transfers (C, Book of News; blog 2026-11-17). Also manual: in Cowork type `/` then the "Copilot" tab to reference earlier Chat conversations and Cowork tasks (C, Learn use-cowork) |
| SKILL.md skills | Converged | "Skills in Copilot Chat are generally available"; users select a skill explicitly or let Copilot auto-select; skills "created in Copilot Chat can be used in Cowork"; Cowork-created skills appear in Chat "only if they are shared" (C, MC1493223 2026-10-09; Learn build-copilot-chat-skills). Chat skills need Microsoft Copilot (Premium); not Chat (Basic) (C) |
| Skill scripts | Same sandbox documented for Chat and DA skills; Cowork has its own | Chat and DA skills: py/js/ts/sh, no network, no package install, preinstalled list (python-docx, openpyxl, pypdf, pandas, LibreOffice, Pandoc ...; `lxml` is not in the listed inventory) (C, Learn declarative-agent-skills). Cowork: Python 3.12.15 with lxml and python-docx (O, design 2.4.1) |
| Plugin catalog and governance | Converged | One plugin registry; "Plugins that admins deploy from the Microsoft 365 admin center are available in both Copilot Chat and Cowork" (C, MC1493223); "Plugins and skills governance is generally available" (C, Book of News); plugin catalog at aka.ms/plugins (C, Ignite blog). Caveat from Learn: "A shared package doesn't mean an identical experience everywhere" |
| Connectors (MCP) | Partly converged | Federated connectors are documented for Chat, Agents, Cowork, Word/Excel/PowerPoint/Outlook, Researcher (C, Learn federated-connectors-overview). But "connector-only plugins aren't in the Store yet and remain under the Data Sources menu in Chat" (C, MC1493223). Our package-delivered connector ran in Cowork but failed before reaching the BFF in Chat on 2026-11-23, while the Dev Portal federated connector worked in both (O, phase0 T2/T13/T14) |
| MCP Apps widgets | Both render, with different capability sets | Chat (M365 Copilot): no `frameDomains`, no `destructiveHint`, fullscreen only; Cowork: inline 200-720 px, 64 KiB inline result, `ui/update-model-context` unsupported (C, Learn plugin-mcp-apps and prior note). Both rendered our spike widget (O, phase0 T1 2026-11-12/13) |
| Approval cards | Same shape, different layout | Chat: per-call card with raw parameters, Allow once / Always allow; no card for widget-initiated writes. Cowork: same, with relabelled fields ("title" shown as "Subject"); unannotated tools prompt in both (O, phase0 T3/T4/T12). Cowork documents its own approval model (C, Learn use-cowork) |
| Declarative agents | **Separate** | Chat hosts them (GA). Cowork "can invoke the declarative agents a company has already built"; "available in private preview through the Frontier program" (C, Book of News 2026-11-17). No GA date. Learn plugin pages describe agents only as plugin components "today, these are declarative agents" |
| Billing | **Separate** | Chat: user subscription licence; Cowork: usage-based credits via spending policy that selects Cowork (C, blog 2026-09-25; Learn cowork-admin-governance). Skills in Chat: licence implied, billing not stated (C, MC1493223 silent) |
| Model picker | Similar UI, different lists | Chat: Auto, Quick response, Think deeper plus manual OpenAI or Anthropic selection under org policy (C, Learn copilot-overview; blog 2026-11-17). Cowork: Auto plus named models (Claude Fable 5.2, Opus 5.5 Fast, GPT 6.1 Sol; GPT 5.5 retired) (C, Learn Cowork what's new Nov 2026). Auto in Cowork with subagents and Microsoft-Decision-1.1 is Frontier (C, Cowork Ignite post, Book of News) |
| Custom instructions | **Separate** | Cowork: personal instructions, 20 KB, edited on the Plugins page (C, Learn cowork-customize). Chat: Settings > Personalization (C, support). Project instructions in Cowork "take precedence over global Cowork or chat instructions" (C, Learn cowork-projects), which implies two separate stores (I) |
| Memory | **Separate** | Cowork: "conversation-scoped ... Memory does not persist across conversations" (C, application card). Chat: Copilot memory, saved memories and chat-history details, stored in the user's mailbox, "in preview" per Learn (C, Learn copilot-personalization-memory, fetched 2026-11-23) |
| Projects | Cowork only (documented) | "Projects are private to the individual creator" (C, Learn cowork-projects). Chat Projects: U. Workspaces (Frontier) described as holding shared context (C, Ignite blog; S mirror) |
| Mobile | Cowork documented; Chat via same app | Cowork on iOS/Android since May 2026; custom skills not supported on mobile (C, use-cowork), yet "plugins are discoverable and configurable ... + > Skills" (C, cowork-faq): internal tension, U which wins |

Other facts:
- C: Cowork FAQ positions them as different products: Chat "Always-on AI for drafting, summarizing, answering questions ... Seconds to minutes"; Cowork "Agentic AI ... Minutes to hours".
- C (Sept 25 blog): automatic mode routing "Chat, Cowork or Code" is "Coming soon". At Ignite, only the Chat-to-Cowork recommendation is GA (Book of News).
- I: our synopsis decision rule (agent in Chat, skill in Cowork) still fits the documented split, but "the same skill folder runs in both" is now documented, not just conventional.

---

## 3. Word Copilot and other Office hosts after Ignite

**Word Agent / edit.** C: Word edits with Track Changes and manages comments (prior notes, Word blog 2026-04-08); web: "Copilot can edit your document by default ... All changes ... trackable and reversible" (Learn release notes 2026-06-02, [Web]). No Ignite change found for the Word in-app pane. The "Office at Ignite 2026" post (read via the Japanese mirror, original 2026-11-17) does not mention a Word agent (S/C).

**Legal plugin in Word.**
- C: enabled by default in Word desktop for M365 Copilot commercial subscribers; policy "Legal Skills in M365 Copilot in Word"; `...` > Manage plugins & skills > Legal (Learn manage-copilot-for-legal; support, undated).
- O: present in Word desktop, absent in Word for the web (design.md 2.4 "S5").
- C unchanged: playbook must be `.docx`; "can currently only perform contract reviews from playbooks"; proposed edits as tracked changes (support).
- C: Ignite blog lists "legal (20+)" skills "in Word and Cowork"; no new Word legal capability found. U: whether Mac/web ever get it (RM571884 listed Desktop).

**Third-party plugin skills in Word.** Unchanged: no documented loader (C absence: Learn plugin-type-skills says "host support varies"; Learn declarative-agent-skills and plugin pages list Chat, DAs and Cowork only; the Word-only exceptions are Excel (preview, Office.js) and PowerPoint). O: our skills listed, never run, Python not available (design.md 2.4.1/2.4.7, L21/L25). The Ignite "Office" post says "Skills and connectors bring specialized help into Office, such as ... legal workflows in Word" (C, 4561230 2026-10-09), which describes Microsoft's own plugins, not a third-party loader (I).

**Declarative agents in Word.** Unchanged since the 2026-11-09 note (C): whole open document as plain text (tables dropped), selection Word-only, image insert only, no documented DA text edit or tracked changes. DA manifest still v1.9; `agent_skills` at most 8; `instructions` at most 8,000 characters; `actions` 1-10; `conversation_starters` at most 12 (C, Learn declarative-agent-manifest-1.9, fetched 2026-11-23; Learn extensibility "What's new" newest section is October 2026, so no post-Ignite schema change). Skill reuse across agents: "Not supported" (C, Learn declarative-agent-skills).

**"Office in Copilot" (Word canvas in Copilot).**
- C: GA at Ignite (blog 2026-11-17; Book of News). MC1530611 (pub 2026-10-29, updated 2026-11-10): rollout "expected to finish by mid-November 2026", deferred release "early December 2026"; "expands Copilot's ability to read and edit document content"; no admin action required.
- C: a prompt generates a Word document that opens beside the conversation; follow-up prompts revise it; direct editing; sharing, comments, coauthoring, presence; files in OneDrive with labels and version history (4561230; Book of News). **Tracked changes are not mentioned** (C negative, all three sources).
- C: entry from Chat or Cowork (4561230). SharePoint (non-OneDrive) storage: commenters asked; not answered (S).
- U: whether the Legal plugin, our plugin skills, DAs or connectors work inside the Office-in-Copilot Word canvas.

**MCP connectors in Word.**
- C: Learn federated-connectors-overview lists "Copilot in Word, Excel, PowerPoint, and Outlook". Conflict: Learn submit-federated-connector says connectors surface in Chat, Excel, Cowork and Researcher "with Word and PowerPoint coming soon".
- O: Word web: Spaarke MCP selectable via + > Choose plugins; Word shows its own approval card even for a read-only tool; tool ran; no widget rendered (phase0 T1 Word, 2026-11-14). Word desktop: U.

**Outlook.**
- O: Copilot in Outlook (web) did not offer the Spaarke connector; "+ > Change data sources" listed only Microsoft 365 data and Fabric (phase0 T1 Outlook, 2026-11-14). Docs list Outlook as a supported host (C): documented and observed behaviour conflict. No post-Ignite change found.
- C new: "Access custom engine agents directly in Outlook" [Web] (Learn release notes 2026-11-17). C: declarative agents with integrated extensibility in classic Outlook for Windows (release notes 2026-09-23). C: Copilot Chat in Outlook reasons over inbox, calendar, meetings and enterprise data (2026-07-01).
- C (Frontier, private preview): "Today" in Outlook and the Copilot app; Autopilot can be @mentioned in Outlook.

**Teams.** C: Cowork works in Teams (Learn get-started); event-driven Cowork tasks fire on Teams messages (Learn use-cowork); Autopilot (Frontier) can be @mentioned in Teams channels. Copilot Chat in Teams uses open content (Learn copilot-overview). MCP Apps in Teams: U (only SPFx UX components documented, prior note).

---

## 4. Microsoft's legal offering after Ignite

- C: **No change to the skill list.** Legal for Cowork still lists exactly 20 skills: analyze-related-contracts, build-case-chronology, check-cross-references, check-defined-terms, check-execution-readiness, compare-across-jurisdictions, compare-key-terms, create-playbook, draft-new-contract, draft-legal-memo, draft-minutes-and-resolutions, explain-legal-concepts, extract-obligations-and-deadlines, identify-regulatory-gaps, improve-legal-prompt, edit-contract, legal-reasoning, review-contract, summarize-legal-matters, summarize-redlines (Learn cowork-available-plugins, fetched 2026-11-23).
- C: Ignite blog 2026-11-17 repeats "more than 20 legal skills ... in Word and Cowork, plus partner-built plugins". The legal launch itself was 2026-10-15 (MC1509799); the Legal Agent is retired.
- C: two plugins, two admin switches, unchanged (Learn manage-copilot-for-legal).
- C: skills in Chat are now documented, and "Plugins that admins deploy ... are available in both Copilot Chat and Cowork" (MC1493223). So Legal for Cowork could be deployed so it is visible in Chat (I). Whether its skills are usable in Chat: U (no Microsoft statement).
- C: no new playbook schema, no org-wide playbook library found. U: nothing found in Ignite material about matters, playbook sharing or partner legal plugins beyond the Oct 15 set (Harvey, Legora, Bloomberg Law and others).
- O: Harvey and Legora appear in our Chat `@`/Agents & Skills picker and in Word's plugin list (phase0 E1/E5, T1 Word).

---

## 5. Declarative agents

- C: still the documented way to give users "a dedicated conversational experience" in Chat; Learn now frames a DA as "one component of a plugin" with skills and tools (Learn plugins overview, overview-declarative-agent). "Declarative agent" vs "custom engine agent" choice guidance unchanged.
- C unchanged limits: 8 skills per agent (Agent Builder and Agents Toolkit), 350 files across skills, directory depth 3, SKILL.md under 20,000 characters, instructions at most 8,000 characters, actions 1-10, starters at most 12, skills "Reuse across agents: Not supported" (Learn, fetched 2026-11-23). Instructions are "Required" in the v1.9 table although the October changelog says optional (Learn inconsistency, carried from prior note).
- C: `worker_agents` is unchanged in Learn (preview; Chat as broker). Not re-verified for Ignite changes (U).
- C: availability: Chat GA; Word/Excel/PowerPoint per prior note; classic Outlook (2026-09-23); **Cowork: private preview via Frontier** (Book of News 2026-11-17).
- C: "agents in the new app": the Copilot app has an "Agents & Skills" area in Chat (Context IQ picker) and an "Agent Store" where "skills, agents, and plugins that contain skills" are discoverable (MC1493223). The synopsis's unexplained Book of News phrase "declarative agents in the new app" resolves to the Cowork private-preview sentence (C, read in full).
- C: a Chat user without the add-on licence gets DAs grounded only in instructions and public websites; custom or work-data agents are pay-as-you-go (Learn copilot-overview).

---

## 6. Capability matrix

Columns: **WD** = Word Copilot desktop; **WW** = Word Copilot web; **Chat** = Copilot app Chat (web/desktop); **Cowork**; **Home** = distinct only where noted (otherwise equals Chat or Cowork by mode); **Outlook**; **Teams**. Tags in brackets.

| Capability | WD | WW | Chat | Cowork | Home | Outlook | Teams |
|---|---|---|---|---|---|---|---|
| Invocation: `@` | `@skill` for legal skills (e.g. `@Draft-new-contract`) [C]; `@agent` in agent mode [O 11-09] | Inline `@DA` documented for web [C, stale 2024]; connector via + > Choose plugins [O] | `@` picker: Agents & Skills, People, Files, Meetings, Emails...; connector not @-able [O E5] | Not documented as `@`-based; people/files via `/` [C] | Chat/Cowork toggle in prompt box [C] | U | `@agent`, `@Autopilot` (Frontier) [C] |
| Invocation: `/` | File/email/meeting picker, not skills [C] | Same [I] | `/` opens Agents & Skills picker [O E1] | `/skill-name`, `/` also refs files, people, past chats and tasks [C; O E2] | as mode | U | U |
| Invocation: auto | Legal plugin auto-detects a legal document [C] | Plain Word Copilot applied our playbook without plugin [O design T-12] | Skills auto-selected unless user turns off per skill [C MC1493223]; tool use 13/15 prompts without naming Spaarke [O T6] | Skill chosen from intent; plugin chip on/off per conversation [C] | Auto recommends Chat to Cowork [C GA] | U | Cowork event-driven tasks on Teams/mail events [C] |
| Runs our SKILL.md (plugin skill) | No: listed, never loads [O] | No: listed, toggled off, never loads [O] | Documented GA, Premium licence [C]; not yet tested with our plugin [U] | Yes [O] | by mode | U | U |
| Runs skill scripts (Python) | No: "Python execution isn't available here" [O L21] | No [I] | Documented sandbox: no network, no pip, fixed package list, `lxml` not listed [C]; our scripts untested [U] | Yes: Python 3.12.15, lxml, python-docx [O] | by mode | U | U |
| Hosts declarative agents | Yes [C]: whole doc as plain text, tables dropped, no documented text edit [C 11-09] | Yes [C 2024, stale] | Yes, GA [C]; our DAs listed [O synopsis] | Private preview, Frontier only [C Book of News] | Chat mode yes; Cowork mode Frontier [C] | Classic Outlook for Windows DAs [C 09-23]; custom engine agents on web [C 11-17] | Yes [C general] |
| Spaarke MCP connector tools | U (docs list Word [C]; submit page says "coming soon" [C]) | Yes via + > Choose plugins, approval card even for reads [O 11-14] | Yes via Dev Portal federated connector [O T0/T5]; package-delivered connector failed before reaching BFF 11-23 [O T2/T13/T14] | Yes, both registrations (must switch plugin on in Customize) [O]; register once or tools run twice [O T2] | by mode | Not offered in test [O 11-14]; docs list Outlook [C] | U |
| MCP Apps widgets | U | Tool ran, widget not rendered [O] | Renders; theme, 1/3/6 MB init OK [O] | Renders; 6 MB OK; inline limits [O, C] | by mode; Home-specific rendering U | n/a (no connector) | U |
| Reads files, mail, meetings | Open doc + attachments; `/` picks file/email/meeting [C] | Same [I] | Work IQ button, on by default, toggle [C RN 08-25]; Basic licence: upload only [C] | Files, mail, Teams, calendar, meetings, OneNote, with approval for sensitive actions [C] | by mode | Chat in Outlook reasons over inbox, calendar, meetings [C 07-01] | Open content; Cowork reads Teams chats/channels [C] |
| Edits Office docs with tracked changes | Legal plugin "Review contract" yields tracked changes [C]; Word Agent edits with Track Changes [C prior]; DAs: no documented edit [C] | Copilot edits by default, "trackable and reversible" [C 06-02]; no legal plugin [O] | Office in Copilot canvas beside chat; tracked changes not mentioned [C] | Edits files in OneDrive/SharePoint in place, version history kept [C]; tracked changes not documented [C negative]; our skill wrote tracked changes via its own OOXML script [O] | Office in Copilot GA: edit by prompt or directly, coauthor; tracked changes not mentioned [C] | n/a | n/a |
| Microsoft Legal skills | 3 skills + Review contract flow, on by default [C, O] | Absent [O] | Legal for Cowork plugin deployable to both [C MC1493223]; skills usable in Chat [U] | 20 skills [C, list unchanged] | via Cowork | Cowork hosts in Outlook [C]; legal skills there [U] | same [U] |
| Personalization (instructions, memory, projects) | No custom instructions documented for Word [C absence]; agent mode honoured them [O 11-09] | Same [U] | Custom instructions + memory (preview) in Settings > Personalization [C]; Projects [U] | 20 KB instructions, private Projects, conversation-scoped memory [C] | Projects/Workspaces held in Home [S]; Workspaces Frontier [C] | Copilot personalization applies [U] | U |
| Approval for writes | Card for tool use [O]; tracked changes are reviewable [C] | Card even for read-only tool; Approve all in chat [O] | Card per model-initiated write with raw params; none for widget writes; unannotated tools prompt; reads with `readOnlyHint` do not [O T3/T4/T12] | Per-action approval; Approve once / Always allow per conversation; same MCP behaviour [C; O] | by mode | U | Cowork approvals reach mobile [C] |
| Billing | M365 Copilot (Premium) licence; legal plugin needs it; no credits documented [C] | same [I] | Licence (USL); skills need Premium; Basic gets web-only DAs [C] | Licence plus usage-based credits via spending policy; per-step cost unpublished [C] | Chat on USL; Cowork/Code/Autopilot on credits [C] | Chat in Outlook on USL [C] | same [C] |
| Admin controls | M365 Apps admin center Cloud Policy "Legal Skills in M365 Copilot in Word" [C]; plugins via Agents > Tools [C] | same [I] | Agents > Tools, RBAC by user/group, can review user-created skills [C MC1493223]; plugin and skill governance GA [C] | Spending policy is the access control; Agents > Tools per plugin; Anthropic family switch; Frontier gating for DAs, Home features [C] | Home/Frontier switch in admin center Copilot > Settings [C prior] | Copilot > Connectors for federated connectors [C]; agents via Agents settings [C] | same [C] |
| Mobile | Word mobile Copilot [U] | n/a | Copilot app on iOS/Android [C]; widgets, skills on mobile [U] | iOS/Android GA; custom skills not on mobile; approvals on phone [C] | Home on mobile [U] | Outlook mobile [U] | Teams mobile DAs as message extensions [C 2025] |
| Model picker | GA in Word, Auto/Claude/GPT [C prior]; whether legal plugin or a DA follows it [U] | same [C prior] | Auto, Quick, Think deeper, manual OpenAI/Anthropic [C] | Auto plus named models; EU Data Boundary GPT-only [C prior] | per mode | U | U |

---

## 7. What Ignite changed (relative to the 2026-10-29 to 11-09 notes and the 2026-11-22 synopsis)

1. **Home is GA** (blog 2026-11-17), after Frontier-only on 2026-09-25 and 2026-10 Learn. Roll-out continues; Learn Cowork "What's new" and the support page still say Frontier/"still rolling out" (C).
2. **Auto recommends Chat to Cowork, with context carried over, GA** (Book of News; blog). Automatic mode routing across Chat/Cowork/Code is still "coming soon" (Sept 25 blog).
3. **Office in Copilot is GA** (was Frontier on 2026-10-09). Rollout expected to complete mid-November, deferred release early December (MC1530611 v4, 2026-11-10). Tracked changes unmentioned.
4. **Skills in Copilot Chat are GA** and shared with Cowork (MC1493223, 2026-10-09; rollout complete mid-November). This supersedes the earlier "standalone plugin skills in Chat: U" (2026-11-02 note). It predates Ignite but became real at the end of the rollout.
5. **Cowork calling declarative agents: still private preview (Frontier)**, now stated in the Book of News (synopsis already had this).
6. **Plugin catalog (aka.ms/plugins), unified registry, and plugin and skill governance GA**, with MCP-dependency visibility (Ignite blog; Book of News; MC1484008 UI rollout to late November).
7. **Custom engine agents in Outlook** (release notes 2026-11-17, web). New for Outlook.
8. **Legal: no change to the 20-skill Legal for Cowork list or the three Word skills.** Ignite repeated "20+ legal skills in Word and Cowork" (blog).
9. **Cowork model changes**: Fable 5.2, Opus 5.5 Fast, GPT 5.5 retired, GPT 6.1 Sol; Planner plugin; OneNote read/edit; Auto with subagents and proactive recommendations are Frontier (Learn Cowork what's new Nov 2026; Cowork Ignite post).
10. **Not changed** (checked 2026-11-23): DA manifest v1.9 and its limits; skills reuse across agents unsupported; no Word third-party skill loader; Cowork Learn pages list no MCP-Apps-in-Home statement.
11. Frontier-only, not to design on: Copilot Workspaces, Today, Autopilot, Business plugins in Microsoft IQ, Copilot Code (GA planned December).

---

## 8. Still unknown / needs tenant test

1. Is Home live in our dev tenant, and does it change where plugins, skills and connectors appear (Plugins button replaces Agents / Customize, UI rollout to late November per MC1484008)?
2. Does our `agentSkills` plugin run in Chat (instruction-only skill, then a skill with a Python script)? Does the documented Chat sandbox lack `lxml`, which our OOXML scripts use in Cowork?
3. Package-delivered connector in Chat: re-test after rollout completes (failed 2026-11-23 before reaching the BFF; Dev Portal connector worked).
4. Does Chat render a widget returned by a connector delivered in a plugin package? Does Home Chat mode differ from Chat?
5. Do admin-deployed Legal-for-Cowork skills appear and run in Chat? Do they run inside the Office-in-Copilot Word canvas?
6. Tracked changes from Cowork `edit-contract` and from the Office-in-Copilot canvas.
7. Word desktop: federated connector tool visibility and widget behaviour (only Word web tested); whether the Word Agent applies a DA's findings as tracked changes in one chat (open from the 2026-11-09 note).
8. Outlook: re-test the connector after Ignite (docs say supported; tenant said no).
9. Cowork invoking a declarative agent: needs a Frontier-enrolled test tenant; unknown invocation syntax, auth and billing.
10. Billing of Chat skills and Chat script runs (MC silent); per-step Cowork credit cost for a legal task (still unpublished).
11. Whether custom instructions and Copilot memory are applied in Chat when an agent or skill is active (a search snippet said memory and custom instructions are "not currently available for Agents", S, unverified).
12. Home and Cowork on mobile with plugins (Learn says custom skills unsupported yet plugins configurable on mobile).
13. Whether Chat shares Cowork's Projects, and what the shared history and Library mean for privilege and retention (Purview does not apply retention to Copilot memory, C Learn copilot-personalization-memory).

---

## Sources

C, Microsoft primary:
- Microsoft blog 2026-09-25, "Introducing the new Copilot with Home, Code and Autopilot": https://blogs.microsoft.com/blog/2026/09/25/introducing-the-new-copilot-with-home-code-and-autopilot/
- Copilot blog 2026-11-17, "Copilot at Microsoft Ignite": https://microsoft.com/en-us/copilot/blog/2026/11/17/copilot-at-microsoft-ignite-expanding-whats-possible-with-intelligence-built-for-work
- Ignite 2026 Book of News (read in full): https://news.microsoft.com/events/ignite/2026/book-of-news/
- Cowork at Ignite 2026, 2026-11-17: https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/whats-new-in-copilot-cowork-at-ignite-2026/4572242
- Introducing Office in the new Copilot app, 2026-10-09: https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/introducing-office-in-the-new-microsoft-copilot-app/4561230
- MC1493223 Skills in Copilot Chat (2026-10-09): https://mc.merill.net/message/MC1493223
- MC1530611 Office in Copilot (pub 2026-10-29, upd 2026-11-10): https://mc.merill.net/message/MC1530611
- MC1484008 plugin registry (2026-09-30): https://mc.merill.net/message/MC1484008
- Support, Get started with the Microsoft Copilot app (undated): https://support.microsoft.com/en-us/microsoft-365-copilot/what-is-microsoft-copilot-app
- Support, Create and edit skills in Copilot (undated): https://support.microsoft.com/en-us/microsoft-365-copilot/create-edit-skills-copilot
- Support, Get started with Copilot for legal (undated): https://support.microsoft.com/en-us/word/get-started-with-copilot-for-legal
- Learn (fetched 2026-11-23): https://learn.microsoft.com/en-us/copilot/plan/copilot-overview ; https://learn.microsoft.com/en-us/copilot/cowork/whats-new ; .../cowork/use-cowork ; .../cowork/cowork-plugins ; .../cowork/cowork-customize ; .../cowork/cowork-projects ; .../cowork/cowork-mobile ; .../cowork/cowork-faq ; .../cowork/cowork-available-plugins ; .../cowork/responsible-ai/copilot-cowork-application-card ; https://learn.microsoft.com/en-us/plugins/build-copilot-chat-skills ; .../plugins/declarative-agent-skills ; .../plugins/plugin-type-skills ; .../plugins/plugin-mcp-apps ; .../plugins/choose-plugin-development-tools ; .../plugins/overview-declarative-agent ; https://learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-copilot-for-legal ; https://learn.microsoft.com/microsoft-365/copilot/connectors/federated-connectors-overview ; .../connectors/submit-federated-connector ; https://learn.microsoft.com/en-us/copilot/microsoft-365/copilot-personalization-memory ; https://learn.microsoft.com/microsoft-365/copilot/extensibility/declarative-agent-manifest-1.9 ; .../extensibility/whats-new
- Learn release notes (sections 2026-11-17, 2026-09-23, 2026-07-01, 2026-06-02, 2026-08-25): https://learn.microsoft.com/en-us/copilot/microsoft-365/release-notes

S, secondary:
- Japanese Microsoft Windows blog translation of "Office at Ignite 2026" (orig. 2026-11-17): https://blogs.windows.com/japan/?p=47815
- TechRadar 2026-10-12 (Office in Copilot, Frontier then): https://techradar.com/pro/the-new-microsoft-copilot-app-is-getting-the-full-office-experience-heres-what-you-can-look-forward-to-from-word-excel-and-more
- Office-Watch Ignite 2026 (GA/Frontier tally): https://office-watch.com/?p=78227
- Community-post mirror via search snippet: https://www.elevenforum.com/t/home-your-starting-place-for-work-in-the-microsoft-copilot-app.50954/ (403 on fetch)

O, observed: `C:\code_files\spaarke-wt-spaarkeai-mcp-bff-api-r1\projects\spaarkeai-mcp-bff-api-r1\notes\phase0-results.md`; `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1\projects\spaarke-legal-ai-accelerator-r1\design.md` (section 2.4).

Prior researcher notes: `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1\.claude\agent-memory\researcher\` (files named in the header).

Repo `knowledge/m365-copilot`, `knowledge/declarative-agents`, `knowledge/mcp-apps`: stale (last refreshed 2026-05; sample manifests DA v1.2-v1.6). Not used for facts here.
