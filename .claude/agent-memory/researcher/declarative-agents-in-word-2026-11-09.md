---
name: declarative-agents-in-word-2026-11-09
description: Declarative agents inside Word desktop's Copilot pane for legal review (2026-11-09) - DA manifest v1.9 current; DA reads whole open doc as plain text (tables dropped), selection Word-only, can only insert images; no DA text edit/tracked changes; Word chat-vs-agent toggle and inline @DA undocumented; SharePoint knowledge = search, tables not parsed, <=20 files read in full; EmbeddedKnowledge = Agent Builder upload only, not packageable; skills (8, GA Sept 2026) are the way to carry a full review protocol; worker_agents preview + Copilot Chat broker; org prompts still not in Word; MC1536062 retires standalone W/X/P agents in Copilot app
metadata:
  type: reference
---

# Declarative agents in Word's Copilot pane for legal review (2026-11-09)

Tags: **C** = Microsoft primary source (date = ms.date / updated_at, or MC publish date) · **S** = secondary (GitHub issue, blog, mirror) · **I** = inferred · **U** = undocumented · **Obs** = owner's tenant observation 2026-11-09. **NEW** = changed or first documented within 60 days (since 2026-09-10).

Builds on (not repeated): [[word-copilot-pane-plugins-playbooks-addin-2026-10-31]], [[cowork-builtin-skills-and-specialist-agent-patterns-2026-10-31]], [[word-vs-cowork-plugin-kinds-harvey-legora-tuning-2026-11-02]], [[word-plugin-skills-not-executed-2026-11-02]], [[mcp-server-dotnet-bff-copilot-widget-2026-11-05]], [[copilot-as-spaarke-surface-and-frontier-2026-11-05]].

## Headline
1. A specialist DA in Word is a **reader and adviser, not an editor**. Microsoft documents that a DA reads the whole open document as plain text, reads a selection (Word only), and can insert images. It documents no way for a DA to write text, add comments or make tracked changes. The Word Agent can make tracked changes. Whether it will apply a DA's findings in the same chat is undocumented and is the main spike test.
2. **Tables are lost twice.** The open Word document reaches the DA "entirely of plain text … tables … aren't captured". SharePoint knowledge has the same problem: "Copilot is currently unable to parse tables". A 4-column `.docx` playbook used as DA knowledge is therefore at risk. Ship the review protocol as prose or Markdown: put it in the instructions (8,000 characters) or in a **skill** (`SKILL.md` up to 20,000 characters, plus resource files).
3. **One DA with skills is a better front door than many DAs.** A DA can carry 8 skills (GA, MC1476977, Sept 2026). Skills load on demand, one per document type. `worker_agents` is preview, brokered by Copilot Chat, needs Agent 365 registry registration, and every connected agent must be acquired by the user; no source says it works in Word. If separate per-type agents are still wanted, keep the library short and make every agent stand alone.
4. The Word pane's chat-mode versus agent-mode split is **not documented anywhere**. Microsoft only documents the Word Agent as the primary pane experience (MC1240704). Inline `@`-mention of installed DAs is documented only for Word/PowerPoint **web** (MC918574, Oct 2024). Treat the owner's observations as the current truth and re-test after each Word update.

## Answer detail by question

### Q1 Pane modes
- MC1240704 (pub 2026-02-27, upd 2026-03-27): single corner entry point; "Word Agent in the chat pane as the primary Copilot interface"; Win/Mac/Web; WW Apr-Jun 2026, GCC/DoD Oct 2026. Says nothing about modes, `@`, custom instructions or models. **C**. The support page "Copilot Dynamic Action Button" says the same. **C**.
- Chat/agent toggle; chat mode cannot `@` and ignores custom instructions; agent mode accepts `@agent` and reads them: **Obs only, U in docs**. Third-party blogs say Agent Mode became the default on 2026-04-22 and was relabelled "Edit with Copilot". **S, inconsistent.**
- Inline `@DA`: MC918574 (DAs GA in Word/PPT **web**, late Oct-Nov 2024): type "@" to list installed agents. **C (2024, stale)**. For desktop, MC1168289 (2025-10-08) says DAs are "already available" in Word/Excel/PPT/OneNote. **C**. The support page "Customize with agents in Word and PowerPoint" says pane menu > Get Agents (undated). The "Switch copilots" menu is in the support page "Get started with agents" (undated). **C**.
- July 2026 "What's new": users can `@`-mention the **Word/Excel/PowerPoint agents** inside a Copilot Chat prompt. That refers to the Copilot app agents, not DAs in Word. **C**.
- **NEW: MC1536062 (2026-11-03)** retires the standalone Word/Excel/PowerPoint agents in the Copilot app and Copilot Chat. Rollout is mid-Nov to late Dec 2026, for USL users. Users can no longer "find, pin, @mention, or invoke them". It does not mention Word's in-app pane or custom DAs. **C**. That Word's own Word Agent is unaffected is **I**.
- Custom instructions: the support page "Customize how M365 Copilot responds" (undated) covers Copilot Chat only and says nothing about Word or about agents. **C (absence)**. Whether DAs honour personal custom instructions is **U**. Owner saw agent mode use them (**Obs**).
- Legal plugin skills: Word desktop only, needs an M365 Copilot licence, "will tend to use legal skills for the rest of your chat". The support page does not say which mode the plugin runs in. **C** (support page, undated).
- Model picker: Word picker GA (RM558440, prior note). DAs get Microsoft-chosen model upgrades (MC1251203: GPT-5.2). DA manifest v1.7+ `behavior_overrides.default_response_mode` = Auto / Think deeper / Quick. Whether the Word picker applies to a DA turn is **U**.
- Platforms: DA document selection is Word-only. Whole-document access and image insert work in Word and PowerPoint. Teams sideload is unsupported on macOS. Legal plugin = Word desktop. **C**.

### Q2 Document context (Learn `declarative-agent-document-interaction`, ms.date 2026-03-06; older than 2026-08, still the current page)
- "By default, agents can read the entire open document." When the prompt references the open document ("Summarize this document"), its contents are added automatically. Selection reaches the agent through the **Add selection** button. Neither needs manifest changes. The caller's phrase "document selection by default" understates this: the whole document is the default. **C**.
- Word format: "entirely of plain text … headings, body paragraphs, and list elements … sequential chunking". "Tables, charts, images, page formatting, and styling aren't captured." Mentions of visual objects come through as metadata only. **C**. Tracked changes and comments in the open document are not mentioned (**U**; likely not included, **I**).
- No size limit is documented for the open document (**U**). Attached files and `/` references: the `/` file picker is documented for the Word pane in general, not for DAs (prior note) (**U** for DA turns).
- API plugins: the agent decides how much of the document to pass; steer it with plugin 2.4 `reasoning` state instructions. **C**.

### Q3 Editing
- The DA's only documented write is `Action.InsertImage` in an API-plugin Adaptive Card (Selection/Top/Bottom; bmp/gif/jpeg/png). Text insert, replace, comments and tracked changes are not documented for DAs. **C (absence)**.
- The Word Agent can edit "with Track Changes on" and can add, read, reply to and manage comments (What's new July 2026; Word blog 2026-04-08). **C**.
- Whether the Word Agent can apply a DA's recommendations in the same chat: **U**. This is the spike's key test. A 2025 Q&A report says "insert and selection are still not working within the agent" (S, stale).

### Q4 Knowledge
- `OneDriveAndSharePoint` (v1.9): `items_by_url[{url}]` (absolute URL of a site, folder or file) or `items_by_sharepoint_ids[{site_id, web_id, list_id, unique_id (folder or file), search_associated_sites, part_type/part_id}]`. Omitting both gives access to the whole tenant. The manifest sets no count limit. **C** (2026-09-30/10-22). Agent Builder allows 100 SharePoint files, 1 list, 50 OneDrive files, 4 URLs and 20 uploaded files. **C** (agent-builder-add-knowledge 2026-09-30).
- Retrieval works by search over the semantic index. With **≤20 specified files, "Copilot searches the full contents of all files"**; with more, it searches the full contents of the top 20. Best practice is ≤300 pages in total. For site or folder scope, keep each file ≤36,000 characters (about 15-20 pages). **"Copilot is currently unable to parse tables and other special formatting in SharePoint content."** **C** (optimize-content-retrieval, ms.date 2025-08-07, upd 2026-07-29). New uploads take minutes to appear ("Preparing"). **C**.
- Making a DA read the whole playbook: point to the specific file (not the folder) and keep it short. Better, put the protocol in instructions or in a skill. Skills load "instructions and contents into context only when the task requires them"; `.md`/`.docx`/`.pdf` resources are allowed. **C/I**.
- `EmbeddedKnowledge`: the manifest object exists (≤10 files, ≤1 MB each, doc/docx/ppt/pptx/xls/xlsx/txt/pdf). The v1.9 page still says "Embedded Files are not enabled yet". knowledge-sources marks embedded files **Agent Builder ✅ / Agents Toolkit ❌**. In Agent Builder you upload up to 20 files (512 MB docx/pdf, 30 MB xlsx). They are stored in SharePoint Embedded in the tenant's default geography, not supported in GCC, and IB does not apply. Agent Builder's "Download .zip" **cannot include embedded files**. So a vendor package cannot ship embedded knowledge. **C**. v1.8 added `embedded_resource_snapshot_id` (purpose undocumented).
- Knowledge in Word: no Microsoft page restricts DA knowledge by host (I: works). A 2025 report said SharePoint knowledge worked only in Word Online (S, stale). Re-test.

### Q5 Instructions and other fields (v1.9, ms.date 2026-09-30, upd 2026-10-22)
- `instructions` ≤8,000 characters. The changelog says it became optional in v1.9; the field table still says "Required" (doc inconsistency). `name` ≤100; `description` ≤1,000; `conversation_starters` ≤**12** (`title`, `text`, `depends_on`); `disclaimer.text` ≤500, shown at conversation start; `actions` 1-10; `agent_skills` ≤8; `behavior_overrides.special_instructions.discourage_model_knowledge`; `default_response_mode`; `editorial_answers`; `user_overrides` (`remove` only); `sensitivity_label` (not enabled). **C**.
- Starters render in Word: a GitHub issue (#16869, 2026-10-22) shows a DA starter used in Word. **S**. Disclaimer in Word: **U**.
- `worker_agents`: **preview**; `id` or `file` (v1.8+). Copilot Chat acts as broker; text in/out; the target must be a DA or an A2A card registered in Agent 365; users must acquire each connected agent; Adaptive Cards from the connected agent are not shown. **C** (connected-agent 2026-06-18/10-22). In Word: **U**. "MC1478975" was not found under that number (**U**).

### Q6 Schema
- Current: **DA v1.9** (Oct 2026, **NEW**: `agent_skills`, `Fabric`, optional instructions). v1.8 (July 2026): `EmailActions`, `MeetingActions`, worker `file`, `embedded_resource_snapshot_id`. v1.7 (May 2026): `editorial_answers`, `default_response_mode`, starter `depends_on`. Plugin v2.4; app manifest v1.30. **C** (whats-new 2026-11-02/11-05).
- Capabilities in v1.9: WebSearch (≤4 sites), OneDriveAndSharePoint, GraphConnectors (Copilot connectors), GraphicArt, CodeInterpreter, Dataverse, TeamsMessages (≤5), Email (≤25 group mailboxes), EmailActions, People, ScenarioModels, Meetings (≤5), MeetingActions, Fabric, EmbeddedKnowledge. "Any capabilities other than Web search" need metered usage or an M365 Copilot licence. **C**.
- `knowledge/` samples are stale: DA v1.2-v1.6, app manifest 1.19-1.25, plugin 2.1-2.4. `knowledge/declarative-agents/NOTES.md` is a stub from 2026-05 that says EmbeddedKnowledge is "not yet available" (still true for packages).

### Q7 Build and deploy
- **Agent Builder** (in the Copilot app): share with users, groups or teams as Can chat / Can edit, or turn on org-wide sharing (then the agent shows in the Agent Store). "Submit to org catalog" goes to admin review and then **Built by your org**. "Download .zip" sideloads into Teams, without embedded files. An admin policy can restrict sharing. Recipients need the licence that matches the agent's capabilities. Page dated 2026-11-06 (**NEW**). Agent Builder agents live in one tenant (I).
- **Agents Toolkit / WIQD** (pro-code package): sideload (Upload custom apps), `atk`/`wiqd plugin publish` to the org catalog, or hand the zip to an admin (M365 admin center Agents > Tools > Registry/Upload). **NEW** (publish pages 2026-11-03/11-06): docs are reframed around "plugin packages". The **"Advanced package uploads"** tenant setting (DA with actions or MCP) defaults to All users from 2026-10-26 (S: ATK issue #16867; blog citing MC1472007, unverified).
- **Copilot Studio**: publish to the M365 Copilot / Teams channel, then admin approval. Agent Builder sharing "doesn't allow … integration with other channels"; multi-channel needs Copilot Studio. **C**.
- **Marketplace (Partner Center, M365 and Copilot program)**: the only multi-tenant route. "Not every plugin package is eligible"; confirm the offer type for your capabilities. **C** (2026-09-30/10-12). A packaged DA with tenant-specific SharePoint URLs or IDs cannot be generic (I). For repeatable ISV delivery: a per-customer package from a template (URLs as env variables), deployed by the customer admin. **I**.
- Approval: org catalog submissions and admin-uploaded packages go through admin center Agents > Requests (prior note). **C**.

### Q8 Licensing
- prerequisites (2026-09-30): DAs in Copilot Chat without a licence get instructions, custom actions, code interpreter, image generator, MCP Apps and web search. **SharePoint, Copilot connectors, embedded files and Dataverse** need pay-as-you-go (Copilot Credits) or an M365 Copilot licence. Email, People, Teams messages and meetings need the licence. **C**.
- Conflict: knowledge-sources (2026-09-30/10-22) says "SharePoint and OneDrive knowledge sources require the signed-in user to have an active Microsoft 365 Copilot license … fail with 'Sorry, I wasn't able to respond'". prerequisites says PAYG works if the billing policy is connected to the SharePoint agents service. **C (conflict)**. Moot for this product: the Legal plugin itself needs an M365 Copilot commercial licence. Agents for Copilot Chat users in Word: limited "depending on tenant configuration and rollout status" (C).

### Q9 Coexistence with the Legal plugin
- No Microsoft document describes routing between plugins or skills and an active DA, in Word or anywhere else. **U**. Legal routing: auto-detect legal document → "tend to use legal skills for the rest of your chat"; explicit `@Draft-new-contract`. **C**. Owner: `@agent` adds a DA inline to the same chat (Obs). Whether "Review contract" (playbook card → Run Review → tracked changes) still fires after a DA has joined, or inside a DA-scoped chat, is **U**. Test it.

### Q10 Org prompts and starters
- Organizational prompts (ms.date 2026-09-21, **upd 2026-11-04**): shown in Copilot Chat, Edge and Teams. "Supported apps" = Copilot web / Copilot work. Prompt text ≤8,000, 1,000 per tenant, pin 4. **Still not in the Word pane.** **C**. The DA's `conversation_starters` is the only per-agent starter mechanism; Word renders them (S). There is no "on open" hook.

### Q11 Portability
- **The agent itself**: a DA runs only on M365 Copilot hosts. Nothing documents importing a DA into Claude, or a Claude project into a DA. **C (absence)**.
- **Components**: (a) instruction text, as plain prose (I). (b) `agent_skills` "follow the Agent Skills standard" (v1.9 C) → the same `SKILL.md` folders run in Claude, Cowork and Copilot Studio (Copilot Studio skills: S). The sandbox has no network and no package install (C). (c) An MCP server is reusable: DA `RemoteMCPServer` action, Cowork `agentConnectors`, federated connector, and Claude MCP connectors. MCP Apps per SEP-1865 work in M365 Copilot and Claude (prior note). (d) SharePoint knowledge is M365-only; Claude would need its own M365 connector (I). (e) A2A: `worker_agents` can call an A2A card registered in Agent 365 (preview); Foundry incoming A2A v1.0 is GA (prior note).
- Skills in DAs: **GA**, MC1476977 (2026-09-22; WW mid-to-end Sept 2026), "web and desktop experiences supporting Declarative Agents" (→ Word, I). IB tenants are blocked. MC says one skill can be reused across agents; Learn (2026-11-04/06) says "Reuse across agents: Not supported". Limits: 8 per agent, 350 files, depth 3; Agent Builder takes a ≤50 MB zip with ≤25 MB per file; ATK takes a directory within the 10 MB package limit. **C, NEW**.
- MCP and MCP Apps in DAs inside Word: actions run in Word (S: #16869 is an MCP-plugin DA answering in Word). Widget rendering in Word is **U**.

### Q12 Known issues, last 60 days
- microsoft-365-agents-toolkit #16869 (open, 2026-10-22): a DA with an MCP plugin shows **every answer twice in Word, Excel and PowerPoint** (and over its A2A endpoint and in evals); a single answer in Chat web. **S**.
- #16857 / #16858 (closed, 2026-10-20): ATK 6.18 / CLI 1.1.18 regressions; skill folder missing from the package and "Add Skill" missing. **S**. #16896 (open, 2026-10-29): Entra SSO registration 403 ConfigCreateError. **S**.
- copilot-camp #1053/#1052 (open, 2026-09-29): Entra SSO lab needs "Allow all tenants"; Cowork SSO lab ordering. **S**.
- No Tech Community or Q&A thread from the last 60 days found specifically on DAs in Word desktop (search limited).

## Implications for the specialist-agent proposal
1. Prefer **one "Spaarke Legal Review" DA with one skill per document type**: NDA, legal hold, then patent, lease and pleading, up to 8. Each skill holds the review protocol as Markdown plus the playbook as `.md` (not a table-only `.docx`). Keep instructions short: identify the document type, then load the matching skill. This gives the "what type of document?" front door without `worker_agents`. If the owner wants separate `@NDA Review` style agents for discoverability, build them from the same skill folders (copy, because reuse across agents is unsupported).
2. Knowledge = the document type's SharePoint **files** (≤20, by URL or ID), not folders, for full-content reads; precedents in prose. Do not rely on knowledge for the rules themselves.
3. Expect findings in chat. For tracked changes, use the Word Agent, or Microsoft's Review contract for contracts. Test whether the Word Agent applies the DA's findings in the same chat.
4. Distribution: Agent Builder for the owner's tenant pilot. For customers, an ATK/WIQD package per customer tenant (SharePoint URLs parameterised), uploaded by the customer admin. Marketplace later, if eligible.

## Spike tests (docs silent)
S1 Agent mode: `@` a Builder DA after Legal auto-detect; then ask "Review contract with playbook". Does the Legal flow still run? Who answers?
S2 Ask the DA for redlines; then ask the Word Agent (same chat) "apply those as tracked changes with comments". Is it applied, and does it keep anchors?
S3 Open a contract with a clause table: ask the DA to quote a cell. Confirms the plain-text and table loss.
S4 Playbook as a 4-column `.docx` in SharePoint knowledge vs the same rules as `.md`: compare rule recall.
S5 Same DA in chat mode: can it be picked from the pane menu (Get Agents / Switch copilots) even if `@` is blocked? Do custom instructions apply?
S6 Single DA with 2 skills (NDA, legal hold): does it pick the right skill from the document alone? Does Word show a skill chip?
S7 Starters, disclaimer and `default_response_mode` rendering in the Word desktop pane; duplicate-answer bug (#16869) with and without an MCP action.
S8 Model picker change while a DA is active: does the DA turn change model (badge)?
S9 A user with PAYG only (no licence) in Word, against SharePoint knowledge: works or "Sorry, I wasn't able to respond"?

## Sources
Microsoft Learn (m365 copilot extensibility): declarative-agent-document-interaction (2026-03-06); declarative-agent-manifest-1.9 (2026-09-30/10-22); whats-new (2026-11-02/11-05); knowledge-sources (2026-09-30/10-22); agent-builder-add-knowledge (2026-09-30); optimize-content-retrieval (2025-08-07/2026-07-29); declarative-agent-skills (2026-11-04/11-06); declarative-agent-connected-agent (2026-06-18/10-22); prerequisites (2026-09-30); publish (2026-11-06); publish-plugin-organization (2026-11-03/11-05); publish-plugin-publicly (2026-09-30/10-12); agent-builder-share-manage-agents (2026-11-06). learn.microsoft.com/copilot/manage/organizational-prompts (2026-09-21/11-04).
Message Center / roadmap (mc.merill.net): MC1240704 (2026-02-27/03-27); MC1536062 (2026-11-03); MC1476977 (2026-09-22); MC918574 (2024, via cloudscout mirror); MC1168289 (2025-10-08); RM570435 (2026-09-01; Copilot Studio agent UX in Chat, web).
Support (undated): get-started-with-copilot-for-legal; customize-with-agents-in-word-and-powerpoint; customize-how-microsoft-365-copilot-responds-to-you; agent-mode-in-word (rewrite only).
Tech Community: What's new in M365 Copilot July 2026 (4538332); Copilot in Word document workflows (4508974, 2026-04-08).
GitHub: OfficeDev/microsoft-365-agents-toolkit #16869, #16867, #16857, #16858, #16896; microsoft/copilot-camp #1052, #1053.
Repo: `knowledge/declarative-agents/NOTES.md` (stub), `knowledge/{declarative-agents,m365-copilot,mcp-apps}/**/appPackage/*.json` (versions above).

## Open questions
Is there an MC item for the Word pane chat/agent toggle or for `@`-agents in Word desktop? Does `embedded_resource_snapshot_id` let a package reference Builder-uploaded files? Does Learn's "reuse across agents: not supported" for skills override MC1476977? Is MC1478975 a real number (not found)?
