# Copilot inside the Office apps: Word, Excel, PowerPoint (Outlook briefly)

Research spike, part 1 of 3. State as of 2026-11-27. Parts 2 (Copilot Chat, Cowork, built-in agents) and 3
(developer extension routes) are written by sibling researchers. This note stays inside the Office apps and
mentions other parts only where they change an answer here.

## How to read the tags

| Tag | Meaning |
|---|---|
| **DOC** | Documented by Microsoft. The source id, its date and a short quote follow. |
| **OBS** | Observed in our own tenant tests (WP-0 to WP-5, T-12b, Nov 2026). Primary evidence, but one tenant, one build. |
| **INF** | Inferred. The note says from what. |
| **NF** | Not found. The note says where I looked. |
| **SEC** | Secondary source (press, vendor blog, forum). Low authority. |
| **STALE?** | Source is older than about six months (before 2026-05-27). It may no longer be true. |

Quotes were pulled through a fetch tool that summarises pages. They are short and I believe them accurate, but
spot-check any quote before using it outside the team. Support pages on support.microsoft.com carry no date; I
give the date I read them (2026-11-27).

Source ids (S1, S2, ...) are listed in full at the end.

---

## 1. Capability matrix

Cells: **Yes / No / Preview / Unknown**, then the tag and source. "Win" means the Windows desktop app.
Excel for Mac and PowerPoint for Mac are covered in the notes column to keep the table at seven columns.

| # | Capability | Word Win | Word Mac | Word web | Excel Win | Excel web | PowerPoint Win | PowerPoint web | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Copilot edits the open file in place ("Edit with Copilot" / Edit mode; formerly Agent Mode) | Yes, GA. DOC S1, S2 | Yes, GA. DOC S2 | Yes, GA. DOC S2 | Yes, GA. DOC S3 | Yes, GA. DOC S3 | Yes, GA. DOC S10 | Yes, GA. DOC S10 | Excel Mac and PowerPoint Mac: Yes. DOC S3, S10 |
| 2 | "Chat only" mode (no edits) | Yes. DOC S2 | Yes. INF S2 | Yes. INF S2 | Yes. DOC S3, S24 | Yes. DOC S3 | Unknown. NF | Unknown. NF | PowerPoint pages do not describe a mode switch |
| 3 | "Plan" mode (plan first, then edit) | Unknown. NF | Unknown. NF | Unknown. NF | Yes, GA. DOC S3, S22 | Yes, GA. DOC S3, S22 | Unknown. NF | Unknown. NF | Word shows "a preview of suggested changes in chat first" (DOC S2), which is not called Plan |
| 4 | User model picker (OpenAI or Anthropic) | Yes. DOC S2 | Yes. DOC S2 + prior note (RM558440) | Yes. DOC S2 + prior note | Yes. DOC S3 | Yes. DOC S3 | Yes. DOC S10 | Yes. DOC S10 | Anthropic is outside the EU Data Boundary; admin can switch it off |
| 5 | Copilot writes tracked changes and comments | Yes. DOC S25 + OBS (WP-5, W4) | Yes. DOC S25 | Unknown. NF (S25 names only Windows and Mac) | n/a (Show Changes attribution: Yes. DOC S22) | n/a (same) | n/a | n/a | Word only |
| 6 | Copilot generates and runs JavaScript (Office.js) against the open file | Yes. OBS (WP-5). Not documented. | Unknown | Unknown | Yes. INF from S3, S4, S5 | Yes. DOC S4 (STALE?) | Unknown. NF | Unknown. NF | Only Excel has any Microsoft statement |
| 7 | Copilot runs Python against the open file | No. OBS (WP-1) | Unknown (likely No. INF) | Unknown (likely No. INF) | Yes. DOC S24 (STALE?) | Yes. DOC S24 (STALE?) | Unknown. NF | Unknown. NF | Excel Mac: Yes. DOC S24 |
| 8 | Microsoft built-in skills or plugins in the pane | Yes (Legal plugin). DOC S27 | Unknown ("Word desktop" is not defined). NF | No. INF from S27 | Yes. DOC S7, S22 | Yes. DOC S7, S22 | Yes. DOC S10, S40 | Yes. INF S10 | |
| 9 | Personal custom skills (SKILL.md in a OneDrive folder or upload) | No documented route. NF | No documented route. NF | No documented route. NF | Yes, GA. DOC S7, S22 | Yes, GA. DOC S7, S22 | Yes, GA. DOC S8, S31 | Yes. DOC prior note (MC1434580) | Excel and PowerPoint Mac: Yes |
| 10 | Organization skills deployed by an admin | No documented route. NF | No documented route. NF | No documented route. NF | Unknown. Thin: DOC S7, S22 | Unknown. Thin: DOC S7, S22 | Yes, GA. DOC S9, S40 | Yes. DOC S9 + prior note (MC1478961) | Excel: "your organization ... can provide plugins" but no deploy page |
| 11 | Third-party plugin `agentSkills`, instructions only | Yes. OBS (WP-2). Not documented. | Unknown | No. OBS (WP-2b, T-12b; run was confounded) | Preview (Insider builds). DOC S5, S6 | Unknown. NF | Yes, by admin deployment. DOC S9 | Yes. INF S9 | Excel Mac: Preview. DOC S6 |
| 12 | Plugin skill scripts are executed | Python: No. OBS (WP-1). Office.js carried in the skill text: Yes. OBS (WP-5), not documented | Unknown | No. OBS | Preview, Office.js `.js` only. DOC S5 | Unknown. NF | Unknown. Zip accepts `py` and `js` (DOC S8); execution not stated | Unknown | See section 3 |
| 13 | MCP connector tools called from the pane | Yes. DOC S13 + OBS (`spaarke-decisions`) | Yes. DOC S13 (client not named) | Yes. DOC S13 + OBS (WP-2b: Word asked for connector access) | Yes. DOC S13, S22 | Yes. DOC S13, S22 | Yes. DOC S13, S14 | Yes. DOC S13, S14 | Outlook: Yes. DOC S13 |
| 14 | Declarative agents selectable in the pane | Yes. DOC prior note (MC1168289) | Yes. INF | Yes. DOC prior note (MC918574, STALE?) | Yes. DOC prior note | Yes. INF | Yes. DOC prior note | Yes. DOC prior note (STALE?) | A DA can read the open document (Word, PowerPoint) but cannot edit it. DOC S35 (STALE?) + OBS |
| 15 | API plugins | Only as actions of a declarative agent. INF + SEC (GitHub issue #16869) | same | same | same | same | same | same | No standalone API-plugin loader in the apps. NF |
| 16 | MCP Apps / UX components rendered in the app's Copilot pane | Unknown. NF S30 | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown | Docs test only in Copilot Chat on the web |
| 17 | Admin switch to turn off edit mode or code execution for one app | No. NF | No. NF | No. NF | No. DOC S3 | No. DOC S3 | No. NF | No. NF | Controls are the licence, the Anthropic switch, and plugin/connector settings |
| 18 | Copilot interactions written to the Purview audit log | Yes. DOC S19 | Yes. INF | Yes. INF | Yes. DOC S19 | Yes. INF | Yes. DOC S19 | Yes. INF | AppHost = Word / Excel / PowerPoint |

**Licence line that applies to the whole table.** In-app Copilot with editing needs a paid Microsoft 365 Copilot
licence ("M365 Copilot (Premium)") or a consumer plan with AI credits. Since 2026-04-15, users on free Copilot
Chat ("Basic") have lost or had restricted in-app Copilot in Word, Excel, PowerPoint and OneNote (SEC S33;
Microsoft's own Message Center items MC1253858 / MC1253863 have expired and I could not read them). Plugins,
skills and connectors all state a paid licence (DOC S12, S13, S27).

---

## 2. Findings by question

### Q1. Copilot modes in each app, names, status, licence

**The names have changed three times. Use the current ones.**

| Name you may see | What it is today | Status |
|---|---|---|
| **Agent Mode** (Sept 2025) | Old name for Copilot editing the open file in multiple steps. | Renamed. The support page says it was "previously called Agent Mode" and is now the normal Copilot experience (DOC S32, paraphrase). |
| **Edit with Copilot** / **Edit mode** / **Allow editing** | The current name. It is the default when the pane opens. | **GA** in Word, Excel, PowerPoint. DOC S1 (2026-04-22, STALE?): "take multi-step, app-native actions directly in your documents". Word: "Copilot opens in the chat pane with Edit mode turned on." (DOC S2). Excel: "By default, 'Allow editing' is enabled for many users" (DOC S24). |
| **Chat only** | Copilot answers in the pane and does not change the file. | GA. Word DOC S2. Excel DOC S3, S24. |
| **Plan** (Excel) | Copilot lists the ranges, sheets and formulas it will change, then edits. | GA in Excel on web, Windows, Mac. DOC S3: "Edit, chat, and plan modes are available on Windows, Mac, and the web." Not found for Word or PowerPoint. |
| **Word Agent** | Microsoft's name for the primary pane experience in Word (MC1240704). It is the same thing as Edit mode. | GA. DOC prior note (2026-02-27, STALE?). |
| **Word / Excel / PowerPoint Agents** (in the Copilot app) | Agents in the Copilot app that create new files from chat. They use Anthropic models only. | **Being retired** mid-Nov to late Dec 2026 (MC1536062, prior note). Replaced by **Office in Copilot**, GA 2026-11-17 (DOC S21: "Office in the Copilot app is generally available"). Part 2 covers this. |
| **Office Agent** | The Sept 2025 Frontier agent in Copilot chat. | Folded into the above. The admin page still mentions it (DOC S20). |
| **Skills** / **Plugins** | Reusable instruction packs. Menu names differ: Word "Manage plugins & skills", Excel "Manage plugins", PowerPoint "Manage skills" and "Choose skills". | The single **Plugins** button rollout was due to complete late Nov 2026 (DOC S18). |

**Per app.**

- **Word.** Edit mode and Chat only. Model Selector. Windows, Mac, web and iPad are listed (DOC S2). Tracked
  changes and comments are GA on Windows and Mac since June to July 2026 (DOC S25: "Copilot enables Track Changes
  with word-level precision"; "creating, reading, replying to, and managing comments"). The web is not named in
  that notice (NF). The Legal plugin is "available for Copilot in Word desktop and Cowork" (DOC S27).
- **Excel.** Edit, Chat and Plan on Windows, Mac and web (DOC S3). iPad: chat and plan, edit rolling out. Python
  inside Edit mode (DOC S24, STALE?). Built-in skills (`@clean-data`, `@chart-design`, `@skill-builder`, finance
  skills) (DOC S7, S22). Federated data connectors are GA in Excel (DOC S22).
- **PowerPoint.** "Copilot, including Brand Kit, is available in PowerPoint for Windows, Mac, and the web"
  (DOC S10). Model choice. Built-in skills: Review this presentation, Visualize this slide, Prepare for Questions
  (DOC S10, S40). Personal and organization skills are GA (DOC S8, S9, S40).
- **Outlook (brief).** No edit-mode equivalent. Federated connectors are supported in "Copilot in Word, Excel,
  PowerPoint, and Outlook" (DOC S13). Custom engine agents reached Outlook on the web on 2026-11-17 (DOC S31).
  Outlook is not in the Plugins-button notice (DOC S18). Skills in Outlook: NF.

**Licence.** Word: "an eligible Microsoft 365 subscription and/or Microsoft Copilot license is required"
(DOC S2). Excel lists Personal/Family with AI credits, Microsoft 365 Premium, a commercial Copilot subscription,
or a Copilot Chat-eligible business subscription, and says availability varies by licence and tenant (DOC S3).
In practice, for a business tenant: the full edit experience, model choice, skills, plugins and connectors all
need the paid Microsoft 365 Copilot licence. Free Copilot Chat users were removed from the Office apps in tenants
over 2,000 seats and restricted in smaller ones from 2026-04-15 (SEC S33).

### Q2. Code execution by Copilot in the app

**Short answer.** Excel: yes, JavaScript and Python, and Microsoft says so, thinly. Word: yes for JavaScript, but
only our own tests show it; Microsoft has published nothing. PowerPoint: nothing found either way.

**Excel.**

- The Excel team wrote that Copilot "interacts with the workbook by writing and executing code" and that it
  "makes direct workbook modifications via Excel APIs in a JavaScript runtime" (DOC S4, 2025-09-29, STALE?). That
  post describes a plan, execute, reflect loop, so results do reach the model in Excel (DOC S4, STALE?).
- Python: "You can now use Python directly in Edit with Copilot in Excel." Copilot may also choose Python itself.
  Listed for Windows, Mac and web (DOC S24, 2026-04-28, STALE?). The older "Copilot in Excel with Python" support
  article is retired (OBS: the page now says "This article has been retired").
- The FAQ gives the only general statement: "Code execution occurs through existing Copilot in Excel tools,
  operating within approved environments." (DOC S3). It does not say what the environments or limits are.
- For developer skills (preview): "Copilot in Excel automatically creates a runtime, loads Office.js and executes
  the scripts" (DOC S5, 2026-08-13). Scripts must "run to completion without further interaction". "Any Office.js
  APIs can be called". There is no way to pin a requirement set; a call to an unsupported API has an "undefined"
  effect (DOC S5). Script parameters are not supported (prior note, same page family).
- Sandbox details, validators, denylists, size or time limits: **NF** in the FAQ, the Learn pages, the Excel blog
  posts and the engineering post.

**Word.**

- Microsoft documents what Copilot can do (tracked changes, comments, headers, table of contents: DOC S25), not
  how. I searched the Word support pages, the Word FAQ, Learn, Tech Community and the Office add-ins docs
  repository for "JavaScript", "Office.js", "code" and "sandbox" in the context of Copilot in Word. **NF.**
- The Word Legal Agent post describes a "deterministic resolution layer over the edits" and a "purpose-built
  insertion algorithm" (DOC S26, 2026-04-30, STALE?). That is Microsoft's own legal redline engine. It is not a
  description of the general code tool.
- Our tests are the evidence (OBS, Word for Windows, 2026-11-26 and 11-27):
  - Copilot said it can run "JavaScript that edits the open document, plus a few built-in document tools". It
    cannot run Python.
  - The runner checks code before running it. It refused `toString`. It refused a top-level `await`.
  - It ran code shaped as `async function main(context) { ... }`. The runner supplies `context`. No `Word.run`.
  - The return value did not reach Copilot. Copilot had to read the document afterwards.
  - A tracked replacement and a comment were made. A run-time hash proved our code ran as written.
- So in Word: sandbox, denylist, limits, API sets and the result path are **undocumented**. We know two denylist
  entries and one calling convention, from one build.

**PowerPoint.**

- "It understands document context, can perform multi-step tasks, and generates or edits content directly"
  (DOC S10). "Large or complex presentations may exceed system limits." (DOC S10). No statement about code. **NF**
  in the support pages, the FAQ, the Brand Kit and Skills post, and the skills pages.
- INF: PowerPoint most likely uses the same pattern as Excel and Word (model-written code against the app's
  JavaScript API), because all three shipped as one feature (S1). This is a guess until tested.

**Is it documented, or only described in blogs?** Excel: one engineering blog (stale), one FAQ sentence, and the
preview developer pages. Word and PowerPoint: capability notices and blogs only. No reference documentation of the
runner exists for any app.

### Q3. Third-party extensibility inside the app

**The one Microsoft statement about skills that supply code.** It is in the Excel FAQ (DOC S3):

- "Skills do not introduce a new code-execution mechanism to Excel."
- "Scripts contained in skills are resources available to Copilot in Excel"
- they "do not provide capabilities beyond what Copilot in Excel already has"

This matches what we saw in Word exactly. A skill does not get its own runtime. Copilot reads the skill's code as
a resource and runs it through its own tool, under that tool's filters. So skill-supplied code is **not
prohibited**. In Excel it is a documented preview. In Word it is undocumented behaviour. Nowhere is it a contract.

Microsoft also warns, in the developer docs, against assuming anything carries across hosts:

- "A shared file format doesn't establish cross-agent reuse or cross-host support." (DOC S11, 2026-11-11)
- "Each Microsoft experience activates only the capabilities it supports" (DOC S17, 2026-11-16)
- "Don't assume a skill script can access the network" (DOC S11)
- "Don't infer availability or editing support in one product from a successful test in the other." (DOC S12,
  2026-11-19, about Chat and Cowork)

**By component.**

| Component | Word | Excel | PowerPoint |
|---|---|---|---|
| Plugin `agentSkills`, instructions only | Windows: works (OBS). Web: listed, not run (OBS). No Microsoft page describes third-party skills in Word (NF: support pages, Learn, `office-js-docs-pr`, release notes). | **Preview.** Needs Beta or Current Channel (Preview): Windows 2608+, Mac 16.112+. "Don't use them in a production Copilot extension." (DOC S5, S6). Web not listed. | **GA through admin deployment.** Same zip: manifest 1.30 with `agentSkills` and a `SKILL.md` folder (DOC S9). |
| Plugin skill with scripts | Python not loaded (OBS). Office.js inside the skill text runs (OBS). | Office.js `.js` files only, in the `scripts` folder (DOC S5). Python scripts: NF. | Zip may contain `py` and `js` (DOC S8). Whether PowerPoint runs them is not stated (NF). |
| Personal skills | No route documented (NF). | OneDrive skills folder; `scripts`, `references`, `assets` folders allowed (DOC S7). "Excel custom skills are stored in OneDrive, not in the plugin registry." (DOC S11). | Upload `.md` or `.zip`, or the OneDrive folder. "This skill is only for your personal use." (DOC S8). |
| MCP connector tools | **Documented and observed.** Federated connectors work in "Copilot in Word, Excel, PowerPoint, and Outlook" (DOC S13, 2026-11-16). "All Connectors work in Microsoft Copilot Chat, Word, Excel, PowerPoint and Researcher." (DOC S14, 2026-11-11). We saw Word desktop call our MCP tool and Word web ask for connector access (OBS). | Same (DOC S13). Finance data connectors are GA in Excel (DOC S22). | Same (DOC S13, S14). |
| Declarative agents | Selectable in the pane. Reads the whole open document as plain text; tables are lost. Cannot write text, comments or tracked changes (DOC S35, STALE?; OBS). | Selectable (prior note). Document access is documented for Word and PowerPoint only. | Selectable. Whole-document read and image insert (DOC S35, STALE?). |
| API plugins | Only as actions inside a declarative agent (INF; SEC: issue #16869 shows a DA with an MCP plugin answering in Word, Excel and PowerPoint, twice). | same | same |
| MCP Apps / UX components | **Unknown.** The page says widgets "run inside Microsoft 365 Copilot" and its test steps use only `m365.cloud.microsoft/chat` (DOC S30, 2026-11-09). No Office app is named. It also warns that not every API exists "on every platform or host". | Unknown. Excel has its own "interactive canvas" (Insiders, DOC S23), which is a Microsoft feature, not an extension point. | Unknown. |
| Office add-in called by Copilot ("add-in actions") | **Docs withdrawn.** See below. | Replaced by Excel skills (preview). | Docs withdrawn. |

**Three things to note.**

1. **MCP is the documented third-party route in all three apps.** Federated connectors "use Model Context Protocol
   (MCP) to fetch data in real time", can "create, update, or delete data with the user's approval", need a
   Microsoft 365 Copilot licence, and are governed in the admin center (DOC S13). One caution: Microsoft says
   "Using MCP doesn't make a federated connector interchangeable with a direct MCP server integration" (DOC
   S13 family, connectors-as-plugin page, 2026-11-16). The host list above is written for federated connectors.
   Our MCP server reaches Word as a plugin connector (`agentConnectors`), and that works on Windows (OBS), but no
   page states the Office-app host list for that form. Part 3 should settle which form we ship.
2. **The Word support page is out of date on this point.** It says Copilot "does not connect to external tools"
   (DOC S2, undated). The Learn page (2026-11-16) and our own test say otherwise. Trust Learn and the test.
3. **"An Office add-in that Copilot calls" is no longer a documented route.** Our 2026-11-27 project note lists it
   as Microsoft's documented way to run our own Office.js. The overview still carries one line, "Create custom
   Copilot agents that read and write to documents open in Office applications (preview)" (DOC S28, 2026-09-17).
   But the two how-to pages redirect to that overview, and the "local Office API plugin" page now redirects to
   the Excel skills page (OBS, 2026-11-27). The only sample linked is the Excel skill. So today the only
   documented way for Copilot to run a developer's Office.js is an **Excel skill, in preview**. For Word there is
   none. This is a part 3 topic, noted here because it removes the fallback our note assumed.

### Q4. Admin and governance

**Turning Copilot editing or code execution on or off per app.**

- There is no per-app switch. Excel: "Copilot in Excel is a Microsoft Copilot Chat tool and can't be turned off
  independently." "All admin settings for controlling access to Microsoft Copilot Chat apply." (DOC S3). I found
  no Cloud Policy or admin center setting named Agent Mode, Edit with Copilot, or code execution for Word or
  PowerPoint (NF: admin docs, Message Center mirrors, forum threads).
- The levers that exist: (a) the licence; (b) the Anthropic sub-processor switch, plus the separate "Copilot in
  Microsoft 365 apps with Anthropic models" switch for EU, EFTA and UK tenants (prior note); (c) the Cloud Policy
  "Legal Skills in M365 Copilot in Word" for Microsoft's legal plugin (prior note); (d) users can untick Enable
  Copilot per app and device (SEC).

**Plugins, skills and MCP servers.** These are managed in the Microsoft 365 admin center under **Agents** (DOC
S15, 2026-11-24).

| Control | Where | Effect |
|---|---|---|
| Publisher categories | Agents > Settings > Agent and plugin access | Allow Microsoft, your organization, certified external publishers. |
| Per-plugin audience | Agents > Tools > Plugins > Users | All users, No users, or Specific users and groups. Applies to the package, not to each skill. |
| Upload a package | Agents > Tools > Registry > Upload | Admin uploads the manifest, reviews components, scopes users, installs. |
| Requests | Agents > Tools > Requests | Approve user requests and MCP server registrations (AI Administrator or Global Administrator). |
| Block | Agents > Tools | Block a plugin, or block an MCP server (all its tools). Tool-level on/off is rolling out for some servers. |
| Federated connectors | Copilot connectors > Your connections | Enable or disable, staged rollout to users or groups (DOC S13). |
| Tenant ceiling | Agents > Settings > User access | Maximum audience for any agent, plugin or connector (DOC S13). |

Two cautions from the same page. "Plugins are primarily available to end users in the Copilot channel" (DOC S15),
so the admin doc does not promise the Office apps. And "Plugin governance and MCP governance are separate": blocking
a plugin does not block its MCP server (DOC S15).

**Deployment routes.**

- **Only you (sideload).** Works for Cowork and, as we saw, reaches Word desktop (OBS). Excel preview uses
  `atk install --scope Personal` (prior note).
- **Shared.** `wiqd plugin share` to people, groups or the tenant; org-wide Cowork sharing needs admin approval
  (DOC S16).
- **Admin-deployed.** Agents > Tools > Registry > Upload (DOC S15, S16). PowerPoint organization skills use
  Agents > All agents > Add agent with the same zip (DOC S9). Our `spaarke-decisions` plugin was admin-deployed
  and worked in Word desktop (OBS).
- **Marketplace.** Partner Center, "Microsoft 365 and Copilot" program. "A capability that you can package isn't
  automatically eligible for public distribution." (DOC S17). Excel partner skills were promised for Q3 2026 via
  Marketplace and the admin center (DOC S22); I found no confirmation that this shipped (NF).
- "Published doesn't mean installed." Approval, installation, user availability, consent and connections are
  separate steps (DOC S16).

**Audit.**

- Copilot interactions are logged under Audit (Standard) with no setup. The `AppHost` value is `Word`, `Excel` or
  `PowerPoint` for in-app use (DOC S19, 2026-08-26).
- Each record lists `AccessedResources` with an `Action` of read, create or modify, and `AISystemPlugin` with the
  name, id and version of plugins used. The model provider is recorded. Prompt and response text is not in the
  audit record; only message ids are (DOC S19).
- Admin actions such as `CreatePlugin` and `DeletePlugin` are logged (DOC S19).
- For connector write actions: "Admins can audit activity in Microsoft Purview." (DOC S13).
- **Not documented:** whether a skill is named in the record, whether an edit made by skill-supplied code shows
  as a "modify" on the open document, and whether each MCP tool call is listed (NF in S19). Test AD-2 settles it.
- Inside the file, Excel attributes changes to Copilot in the Show Changes pane (DOC S22). Word edits made with
  Track Changes on are attributed like any tracked change (DOC S25, OBS).

### Q5. Direction

- **Ignite 2026 (17 to 20 Nov).** The Office post is about Office moving into the Copilot app, not about making
  in-app editing extensible. "Skills, plugins and connectors bring specialized expertise into Copilot and the
  Office apps." "The sales skills for PowerPoint and Copilot Cowork will be available together in a single
  plugin." (DOC S21). That last sentence is the clearest signal of intent: **one plugin, skills that light up in
  both an Office app and Cowork.** Microsoft is doing it first for its own sales skills in PowerPoint.
- **No Word announcement.** The Ignite post names no Word or Excel skills. The release notes to 2026-11-18 show
  PowerPoint custom skills (Windows, 6 Oct) and nothing for Word skills (DOC S31). The Office add-ins docs
  repository has skills pages for Excel only, the newest dated 2026-11-19 (OBS S29). I found no Ignite 2026 post
  for Office add-in developers (NF: devblogs Office Add-ins category). I found no roadmap item for third-party
  or organization skills in Word (NF: roadmap search, Message Center mirrors).
- **Excel is the lead app.** Custom skills, a skill builder (GA rollout Oct 2026), a skill recorder (Insiders),
  partner skills, federated data connectors, Plan mode, attribution in Show Changes (DOC S22, S23). The
  developer route (plugin package with Office.js scripts) is still preview after three months, and a second
  tutorial was added on 2026-11-19 (DOC S6).
- **Microsoft's stated next priorities for in-app editing** (April 2026): deeper editing for "finance
  spreadsheets and legal documents", and more control, including previewing changes (DOC S1, STALE?). Legal is
  named. Microsoft then shipped its own Legal plugin for Word in October (prior note).
- **The add-in route is fading.** The Build 2025 "add-in actions" pages are gone and one now points at Excel
  skills (OBS). The direction is: skills plus Copilot's own runtime, and MCP for anything server-side.
- **Standalone agents are being retired** in favour of Office in Copilot (prior note, MC1536062). Part 2.

**What this means for us (my reading, INF).**

1. Server-side playbooks over MCP are on documented ground in Word, Excel, PowerPoint and Outlook. Build on that.
2. Edits in the open Word document can be made two ways today: by Copilot's own editing, steered by an
   instructions-only skill and MCP results; or by Office.js that the skill carries (WP-5). The second is real and
   precise but rests on undocumented behaviour of one client. Treat it as an accelerator, not a foundation, and
   keep a fallback that works without it.
3. Do not plan one package with one behaviour. Each app loads skills differently, and Microsoft says so in writing.
4. Expect Word to follow Excel and PowerPoint. When a Word skills page appears in `office-js-docs-pr`, the WP-5
   pattern probably becomes a documented preview. Watch that repository.

---

## 3. Tests to run

Each test names what to upload, the app and client, the exact prompt, and the pass or fail signal. Keep the
Spaarke MCP connector **off** unless the test is about it, and keep "Spaarke" out of prompts and skill names for
the Word web runs (the WP-2b confound).

**Word**

- **WX-1. Word for Mac: does it load an instructions-only plugin skill?** Upload the existing Word probe (Only
  you). Open any document in Word for Mac. Prompt: `run the word probe`. Pass: the reply contains
  `WPI-1112-A`. Fail: no code, or "skill isn't available".
- **WX-2. Word for Mac: does it run Office.js that a skill supplies?** Same plugin, JS probe v0.4. Prompt as in
  WP-5. Pass: last paragraph ends with the run-time hash `d81beac6`, a tracked change and a comment exist. Fail:
  `code not run`. Record any wrapper change Copilot reports.
- **WX-3. Word for the web, de-confounded.** Rename the probe plugin and skills so no name contains "Spaarke".
  Remove the Spaarke connector for the test user. Open the document in **edit** mode, not view mode. Run WX-1 and
  WX-2 prompts. Pass and fail as above. This tells us whether Word web is a real "No" or a routing accident.
- **WX-4. Word desktop: how much code will the runner take?** Three probe variants whose fixed code is padded
  with real statements to about 5 KB, 20 KB and 60 KB; each still computes the hash at the end. Pass per size:
  hash present. Record the first size that fails and the error text.
- **WX-5. Word desktop: can the model fill a data block while the code stays fixed?** Skill carries fixed code
  plus `const EDITS = [/* Copilot fills this */];` and tells Copilot to put three find/replace pairs from the
  prompt into `EDITS` and change nothing else. The code hashes its own body minus the data block and writes that
  hash to the document. Pass: three tracked changes and the expected hash. Fail: code refused or hash differs
  (Copilot rewrote the code).
- **WX-6. Word desktop: can any result get back to the model?** Probe code throws
  `new Error("WPJ-ERR-" + hash)` after writing nothing. Prompt: `run the probe and tell me the exact error`.
  Pass: Copilot quotes the hash (errors reach the model, so a status channel exists). Fail: generic failure
  text. Second variant: `console.log` the hash.
- **WX-7. Word desktop: map the pre-run filter.** One skill per identifier, each a three-line body that uses
  exactly one of: `fetch`, `XMLHttpRequest`, `eval`, `Function`, `setTimeout`, `constructor`, `import(`,
  `globalThis`, `Office.context`, `getOoxml`, `insertOoxml`, `JSON.stringify`, `String(`, `padStart`. Pass or
  fail per identifier: ran or refused, with the refusal text. This becomes `JS_FORBIDDEN` v2.
- **WX-8. Word desktop: which Word API sets work?** Probe calls, in separate runs: `body.getOoxml()`,
  `insertOoxml`, `document.getTrackedChanges()` then `accept()` on one, `comments.add` with a reply,
  `contentControls` insert, `footnotes` insert. Pass per call: the document shows the effect. This tells us
  whether an edit list can cover real redline work.
- **WX-9. Word desktop: does the model choice matter?** Repeat WP-5 with the Model Selector set to an OpenAI
  model, then an Anthropic model, then Auto. Pass: hash present in all three. A fail on one provider means the
  runner or its willingness depends on the model.
- **WX-10. Stability watch.** Re-run WP-5 after every Word build update and at least monthly. Log the Word
  build number, the model selected and the result. Any change in the wrapper form or the refused identifiers is a
  finding.

**Excel**

- **XP-1. Excel desktop (Current Channel, not Insider): does a third-party plugin skill load at all?** Build an
  Excel edition of the instructions probe (`metadata.tags: excel`). Upload Only you. Prompt: `run the excel
  probe`. Pass: the code string appears. Fail: skill not listed or not used. Repeat on a Beta Channel machine.
- **XP-2. Excel desktop: does Copilot run Excel JS a skill supplies?** Two variants. (a) Documented form: a
  `scripts/probe.js` file with `await Excel.run(async (context) => { ... })` that writes a run-time hash to cell
  A1 of a new sheet. (b) WP-5 form: the same code inline in `SKILL.md`. Prompt: `run the excel js probe`. Pass:
  the hash is in the cell. Record which form works, on which channel, and whether the wrapper is changed.
- **XP-3. Excel for the web:** repeat XP-1 and XP-2. The developer page lists only Windows and Mac builds, so
  this is an open cell.
- **XP-4. Excel: does a skill's Python run?** Skill carries `scripts/probe.py` that computes the hash and
  prints it. Prompt: `run the excel python probe`. Pass: the hash appears in chat or in a cell, with no
  JavaScript rewrite. Fail: "can't run Python scripts" or Copilot re-implements it in JS or as formulas.
- **XP-5. Excel: does the result reach the model?** JS probe returns the hash and writes nothing. Prompt:
  `run the probe and tell me what it returned`. Pass: Copilot quotes the hash. (Excel's loop is documented as
  reflective, so this may differ from Word.)

**PowerPoint**

- **PP-1. PowerPoint desktop: do skill scripts run?** Admin-deploy (Agents > All agents > Add agent) a zip with
  one skill that carries `scripts/probe.py` and `scripts/probe.js`; each writes a run-time hash into the notes
  of slide 1. Prompt: `@<skill-name> run the probe`. Pass: a hash in the notes; record which language. Fail:
  Copilot only follows the text.
- **PP-2. PowerPoint desktop: inline PowerPoint JS, WP-5 style.** Fixed code in `SKILL.md` using
  `PowerPoint.run` (and the `main(context)` form if refused). Pass: hash on the slide.
- **PP-3. PowerPoint for the web:** repeat PP-1 and PP-2.
- **PP-4. Does a sideloaded (Only you) plugin reach PowerPoint, or only an admin-deployed one?** Upload the
  instructions probe Only you. Check + > Choose skills. Pass: listed and runs.

**MCP, UX components, admin, audit**

- **MC-1. MCP tool call from Excel and PowerPoint.** With the admin-deployed `spaarke-decisions` plugin on, in
  Excel desktop and PowerPoint desktop, prompt: `use the decisions tool to list the open decisions`. Pass: a
  consent or connect prompt, then tool output. Repeat on the web clients.
- **MC-2. MCP write approval in Word.** Call a write tool from Word desktop. Pass: an approval card with Allow
  once / Allow for conversation / Always allow. Record whether the card appears for a plugin connector as it is
  documented for federated connectors.
- **UX-1. Does an MCP App widget render in the Word, Excel or PowerPoint pane?** Use a declarative agent whose
  MCP tool returns a UI widget (Microsoft's sample is fine). Select the agent in the Word pane. Prompt: the
  sample's trigger. Pass: the widget renders in the pane. Partial: a card or link only. Fail: plain text.
  Repeat in Excel and PowerPoint, desktop and web.
- **AD-1. How fast does an admin "No users" take effect in Word?** Set the probe plugin's Users to No users.
  Poll Word desktop every 10 minutes with `run the word probe`. Record the time to stop. Then block the MCP
  server only and confirm the skill still loads but the tool fails.
- **AD-2. What does the audit log say after a skill run?** Run WP-5 and one MCP tool call in Word. Next day,
  search Purview Audit for `CopilotInteraction` for that user. Record: `AppHost`; whether `AISystemPlugin` names
  our plugin or skill; whether `AccessedResources` shows the open document with `Action: modify`; whether the
  MCP tool call appears. Pass for governance purposes: our plugin is named and the modify is recorded.
- **AD-3. Anthropic off.** In a test tenant or for a test group, disable the Anthropic sub-processor. Repeat
  WP-2 and WP-5. Pass: both still work on OpenAI models. Fail: skills or the code tool disappear.
- **LIC-1. Copilot Chat (Basic) user.** Sign in to Word desktop as a user without the paid licence. Check
  whether the pane, the plugins menu, our skill and our connector appear. Expected: none. Record what is shown.

---

## 4. Caveats

- Quotes came through a summarising fetch tool. Spot-check before quoting Microsoft externally.
- Support pages are undated and lag the product. Two contradictions found: the Word page's "does not connect to
  external tools" (contradicted by Learn and by our tests), and Excel custom skills described as GA on a support
  page while the Learn developer page still says Insider preview. These describe different routes (OneDrive
  personal skills are GA; plugin-packaged skills with Office.js are preview).
- Sources older than six months are marked STALE?. The most important stale ones are the Excel engineering post
  (Sept 2025) and the April 2026 GA announcement.
- The 2026-04-15 licensing change rests on press and university notices. The Message Center items have expired.
- "Word desktop" in Microsoft's legal plugin pages is not defined as Windows only. Treat Mac as unknown.
- The release notes page is very long. I read the entries from 2026-09-23 to 2026-11-17 only.
- Items tagged "prior note" were verified in our researcher notes of 2026-10-31, 2026-11-02 and 2026-11-09 and
  were not fetched again today.

---

## 5. Sources (all read 2026-11-27 unless marked "prior note")

| Id | Source | Date | URL |
|---|---|---|---|
| S1 | Microsoft 365 blog: Copilot's agentic capabilities in Word, Excel and PowerPoint are generally available | 2026-04-22 (STALE?) | https://www.microsoft.com/en-us/microsoft-365/blog/2026/04/22/copilots-agentic-capabilities-in-word-excel-and-powerpoint-are-generally-available/ |
| S2 | Support: Welcome to Copilot in Word | undated | https://support.microsoft.com/en-us/word/welcome-to-copilot-in-word |
| S3 | Support: Frequently asked questions about editing with Copilot in Excel | undated | https://support.microsoft.com/en-gb/office/frequently-asked-questions-about-editing-with-copilot-in-excel-1cfd906d-40b4-46be-8e2d-65b893e28a02 |
| S4 | Excel blog: Building Agent Mode in Excel | 2025-09-29, updated 2025-10-23 (STALE?) | https://techcommunity.microsoft.com/blog/excelblog/building-agent-mode-in-excel/4457320 |
| S5 | Learn: Overview of Copilot skills for Excel (preview) | 2026-08-13 | https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-skills |
| S6 | Learn: Create a Copilot skill for Excel that uses custom functions (preview) | 2026-11-19 | https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-copilot-custom-functions-skill |
| S7 | Support: Use skills with Copilot in Excel | undated | https://support.microsoft.com/en-us/excel/copilot/copilot-in-excel-skills |
| S8 | Support: Use custom skills with Copilot in PowerPoint | undated | https://support.microsoft.com/en-US/PowerPoint/copilot/copilot-in-powerpoint-skills |
| S9 | Support: Deploy organization skills for Copilot in PowerPoint | undated | https://support.microsoft.com/en-us/powerpoint/copilot/deploy-organization-skills-for-copilot-in-powerpoint |
| S10 | Support: Edit with Copilot in PowerPoint | undated | https://support.microsoft.com/en-us/powerpoint/edit-with-copilot-in-powerpoint |
| S11 | Learn: Skills as plugin capabilities | 2026-11-11, updated 11-16 | https://learn.microsoft.com/en-us/plugins/plugin-type-skills |
| S12 | Learn: Build skills for Microsoft 365 Copilot Chat | 2026-11-19 | https://learn.microsoft.com/en-us/plugins/build-copilot-chat-skills |
| S13 | Learn: Federated connectors overview; and Copilot connectors as plugin capabilities | 2026-11-16 | https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/federated-connectors-overview ; https://learn.microsoft.com/en-us/plugins/plugin-type-connectors |
| S14 | Tech Community: Microsoft Copilot connectors update, October 2026 | 2026-11-11 | https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/microsoft-copilot-connectors-update-october-2026/4572240 |
| S15 | Learn: Manage plugins, skills, and MCP servers in Microsoft 365 admin center | 2026-11-24, updated 11-25 | https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers |
| S16 | Learn: Make plugins and agents available | 2026-11-16 | https://learn.microsoft.com/en-us/plugins/deploy-plugin |
| S17 | Learn: Build and publish Microsoft 365 Copilot plugins for customers (ISV guide) | 2026-11-16 | https://learn.microsoft.com/en-us/plugins/isv-publisher-guide |
| S18 | Message Center MC1484008: Microsoft plugin registry and Plugins button (mirror) | 2026-09-30 | https://mc.merill.net/message/MC1484008 |
| S19 | Learn: Audit logs for Copilot and AI applications | 2026-08-26 | https://learn.microsoft.com/en-us/purview/audit-copilot |
| S20 | Learn: Create files with Word, Excel, and PowerPoint Agents in Microsoft Copilot | 2026-03-26, updated 2026-11-04 | https://learn.microsoft.com/en-us/copilot/manage/wordexcelppt-agents |
| S21 | Tech Community: Office at Ignite 2026: from apps to agentic system of work | 2026-11-17, updated 11-18 | https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/office-at-ignite-2026-from-apps-to-agentic-system-of-work/4572226 |
| S22 | Microsoft 365 blog: Copilot in Excel, built for the era of frontier finance | 2026-06-25 | https://www.microsoft.com/en-us/microsoft-365/blog/2026/06/25/copilot-in-excel-built-for-the-era-of-frontier-finance |
| S23 | Excel blog: Beyond analysis: Copilot gets to work in Excel | 2026-10-12, updated 10-16 | https://techcommunity.microsoft.com/blog/ExcelBlog/beyond-analysis-copilot-gets-to-work-in-excel/4517581 |
| S24 | Excel blog: What's new in Excel, April 2026 | 2026-04-28, updated 05-21 (STALE?) | https://techcommunity.microsoft.com/blog/excelblog/whats-new-in-excel-april-2026/4502696 |
| S25 | Message Center MC1384426: Copilot in Word, improved support for complex edits (mirror) | 2026-06-08 | https://mc.merill.net/message/MC1384426 |
| S26 | Tech Community: Word Legal Agent in Frontier | 2026-04-30, updated 06-12 (STALE?) | https://techcommunity.microsoft.com/blog/microsoft365copilotblog/word-legal-agent-in-frontier/4516218 |
| S27 | Support: Get started with Copilot for legal | undated | https://support.microsoft.com/en-us/word/get-started-with-copilot-for-legal |
| S28 | Learn: Office Add-ins platform overview. Also the redirects observed from `/design/agent-and-add-in-overview` (to this page) and `/microsoft-365/copilot/extensibility/build-api-plugins-local-office-api` (to S5) | 2026-09-17 | https://learn.microsoft.com/en-us/office/dev/add-ins/overview/office-add-ins |
| S29 | GitHub: OfficeDev/office-js-docs-pr, code search for skills pages (Excel only: `excel-skills.md`, `excel-copilot-skill.md`, `excel-copilot-custom-functions-skill.md`) | repo state 2026-11-25 | https://github.com/OfficeDev/office-js-docs-pr |
| S30 | Learn: MCP apps in Microsoft 365 Copilot | 2026-11-09, updated 11-16 | https://learn.microsoft.com/en-us/plugins/plugin-mcp-apps |
| S31 | Learn: Microsoft 365 Copilot release notes (entries 2026-09-23 to 2026-11-17 read) | updated 2026-11-18 | https://learn.microsoft.com/en-us/copilot/microsoft-365/release-notes |
| S32 | Support: Get started editing with Copilot in Office | undated | https://support.microsoft.com/en-us/topic/6d043333-6eeb-49d8-a80c-681d29ab7c04 |
| S33 | SEC: press and university notices on the 2026-04-15 removal of in-app Copilot for unlicensed users (cite MC1253858, MC1253863) | 2026-03 to 2026-04 (STALE?) | https://www.ghacks.net/2026/03/18/microsoft-removes-copilot-chat-from-office-apps-for-unlicensed-users-on-april-15/ ; https://it.purdue.edu/newsroom/articles/260403-microsoft-removes-in-app-copilot-chat-features-for-non-licensesd-users.php |
| S35 | Learn: declarative agent document interaction (prior note, not re-read) | 2026-03-06 (STALE?) | https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-document-interaction |
| S40 | Tech Community: Brand Kit and Skills in Copilot in PowerPoint (read via the Japanese Windows Blog translation) | 2026-09-14 | https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/brand-kit-and-skills-in-copilot-in-powerpoint/4554932 |
| — | Learn: Build plugins for Copilot Cowork (checked: no statement about Office app hosts) | 2026-11-16 | https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development |
| — | Work IQ Dev Tools plugin authoring reference (checked: no host matrix; covers Cowork and declarative agents only) | read 2026-11-27 | https://microsoft.github.io/wiqd/getting-started/plugin-reference/ |
| — | Support: Frequently asked questions about Copilot in Word; about Copilot in PowerPoint (checked: nothing on skills, code or admin control) | undated | https://support.microsoft.com/en-us/word/frequently-asked-questions-about-copilot-in-word |

**Prior researcher notes re-used** (in `.claude/agent-memory/researcher/`):
`word-copilot-pane-plugins-playbooks-addin-2026-10-31.md`, `word-plugin-skills-not-executed-2026-11-02.md`,
`declarative-agents-in-word-2026-11-09.md`. They hold MC1240704, MC1536062, MC1168289, MC918574, MC1434580,
MC1478961 and RM558440.

**Our own test record:** `projects/spaarke-legal-ai-accelerator-r1/notes/poc-e2e/results.md` (WP-0 to WP-5).

**Local knowledge base checked first:** `knowledge/m365-copilot`, `knowledge/declarative-agents`,
`knowledge/mcp-apps`, `knowledge/work-iq`. All four `NOTES.md` files are stubs curated 2026-05-14. They hold
nothing on Copilot inside the Office apps.
