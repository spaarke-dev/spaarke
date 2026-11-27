# Copilot Chat, Cowork and the built-in agents as places our AI capabilities run

Part 2 of 3. Researched 2026-11-27. Ignite 2026 ran 17-20 November, so this includes what Microsoft announced there.

Builds on, and does not repeat, `../cowork-access-to-spe-files-2026-11.md` (2026-11-26) and the researcher notes of 2026-10-29 to 2026-11-05.

**Tags used on every claim**

- **[D]** Documented. A Microsoft page says it. Source id and a short quote follow.
- **[O]** Observed. Seen in our own tenant tests. Treated as primary evidence.
- **[I]** Inferred. Reasoned from documented or observed facts. The basis is stated.
- **[NF]** Not found. I looked and found nothing. Where I looked is stated.
- **[S]** Secondary. A non-Microsoft source, or a mirror of a Microsoft message. Low authority.

Source ids such as (S3) point to the list at the end. Every source there has a page date. Nothing older than six months is relied on without a "stale" flag.

**Names changed in November 2026.** Microsoft now writes "Microsoft Copilot" for the app, "Copilot Chat (Basic)" for the free tier, "Microsoft 365 Copilot (Basic)" for unlicensed users who still get in-app Copilot, and "Microsoft 365 Copilot (Premium)" for the paid add-on. Cowork, Copilot Code and Autopilot are "usage-based billing capabilities" on top of Premium. [D] (S20: "you must have a Microsoft 365 Copilot (Premium) license"). In this note "Chat licensed" means Premium and "Chat free" means Copilot Chat (Basic).

---

## 1. Capability matrix

Cells: Yes / No / Preview / Unknown, then the tag and source.

| Capability | Cowork | Copilot Chat licensed (Premium) | Copilot Chat free (Basic) | Researcher | Analyst |
|---|---|---|---|---|---|
| Loads a personal SKILL.md skill | Yes [D S1] [O] | Yes, GA [D S7] | No [D S7] | Unknown [NF] | Unknown [NF] |
| Loads plugin `agentSkills` | Yes [D S2] [O] | Yes [D S7], not yet tested by us | No [D S7] | Unknown [NF] | Unknown [NF] |
| Sees skills kept in OneDrive `Documents/Cowork/skills/` | Yes [D S1] [O] | Only if shared [S S24] | No [I] | No [I] | No [I] |
| Runs scripts shipped in a skill | Yes [D S2] [O] | Yes [D S7, S8] | No [D S7] | Unknown [NF] | Unknown [NF] |
| Script languages | Python [O]. Others Unknown [NF] | py, js, mjs, cjs, ts, mts, sh, bash [D S7] | n/a | n/a | n/a |
| Preinstalled packages documented | No [NF]. Ours: Python 3.12, python-docx, lxml [O] | Yes, a published list [D S8] | n/a | n/a | n/a |
| `pip install` at run time | Unknown [NF] | No [D S7] | n/a | n/a | n/a |
| Network egress from a script | Unknown [NF]. Likely blocked [S] | No [D S7] | n/a | n/a | n/a |
| Model writes and runs its own Python on user files | Yes [O]. Wording not in docs [NF] | Yes, code interpreter [D S15] | Yes, with fair-use limits [D S15, S14] | Unknown [NF] | Yes [D S23, stale 2025] |
| Package-level MCP connector (`agentConnectors`) | Yes [D S2] [O] | Yes by implication [D S2, S7], not tested by us | Unknown [NF] | No [I] | No [I] |
| Gallery or admin "federated" MCP connector | Yes [D S12] | Yes [D S12] | No, licence needed [D S12] | Yes, read only [D S12] | Not listed [D S12, by absence] |
| Renders MCP Apps widgets | Yes [D S16] | Yes, inside a declarative agent [D S17] | Yes, inside a declarative agent [D S14] | No [NF] | No [NF] |
| Approval card before a write | Yes [D S1] [O] | Yes for connector write tools [D S12] | n/a | Read only [D S12] | Unknown [NF] |
| Scheduled and event-driven runs | Yes, 25 schedules [D S1] | Not checked this pass | Not checked | No [NF] | No [NF] |
| Output files land in | OneDrive `Cowork` folder [D S1] | Download link, session only, for code interpreter [D S15]. Skill outputs Unknown | Same as licensed for code interpreter | Report in chat | Report in chat |
| Edits an existing OneDrive or SharePoint Office file in place | Yes [D S1] | Out of my part (see part 1) | No [I] | No [I] | No [I] |
| Built-in Word edit makes tracked changes or comments | Unknown [NF]. Our Python engine does it in a new file [O] | Out of my part (see part 1) | No [I] | No [I] | No [I] |
| Runs Office.js supplied by a skill | No [O, 2026-11-27] | Unknown [NF]; see part 1 | No [I] | No [I] | No [I] |
| Reads SharePoint Embedded file bytes | No [O] (403) | Unknown [NF] | No [I] | Unknown [NF] | Unknown [NF] |
| Third parties can extend it | Yes: skills, plugins, connectors [D S2] | Yes: skills, plugins, agents, connectors [D S6, S7] | Agents only [D S14] | Connectors only [D S12, S22] | Not found [NF] |
| Licence | Premium plus a spending policy; Copilot Credits [D S4, S5, S20] | Premium [D S20] | Included with Microsoft 365 [D S20] | Premium; 25 queries a month [D S22, stale] | Premium [S] |
| Mobile | Yes. Custom skills and custom plugins not supported [D S1, S2] | Yes [D S20]. Skills on mobile Unknown | Yes | Yes [D S22] | Unknown [NF] |

**Matrix highlights**

1. Skills now run in two hosts, not one. Copilot Chat got skills at general availability in October-November 2026, for Premium users only. [D S7]
2. The two hosts have different script runtimes. Chat's is documented and closed (no network, no pip, a fixed package list). Cowork's is still undocumented. [D S6, S7] [NF]
3. Researcher and Analyst cannot be extended with skills or plugins. Researcher reads federated MCP connectors, read only. [D S12, S22]
4. The free tier gets none of the skills machinery. It gets declarative agents, code interpreter and MCP Apps widgets inside agents. [D S7, S14]

---

## 2. Findings per question

### Q1. The skills spec

**What Microsoft documents**

- A skill is a folder with a required `SKILL.md`. It can hold resource files and scripts. [D S6: "can include supporting resources or scripts"]
- There are three separate skill routes. They share a file format and nothing else. [D S6: "packaging requirements and limits aren't interchangeable"]
  1. A skill used directly in Copilot Chat, alone or inside a plugin (S7).
  2. A skill in a Cowork plugin, or in the user's OneDrive (S1, S2).
  3. A skill attached to a declarative agent through `agent_skills` (S8).
- Microsoft warns against assuming reuse. [D S6: "shared file format doesn't establish cross-agent reuse or cross-host support"]
- `agentSkills` (app manifest) and `agent_skills` (declarative agent manifest) are different properties. Declaring one does not register the other. [D S7: "Don't interchange these properties"]

**Format**

- Frontmatter needs `name` and `description`. `name` is 1-64 characters, lowercase letters, digits and single hyphens, and must equal the folder name. `description` is 1-1,024 characters. [D S2, S7]
- Optional `license` and `metadata` appear in Microsoft's example. [D S2] No host field exists. [D, from the manifest schema, checked 2026-11-02]
- Microsoft's OneDrive example uses `name: Weekly Report`, which breaks the kebab-case rule that plugin validation enforces. The two routes validate differently. [D S1, S2] Use kebab-case everywhere.

**Limits**

| Limit | Cowork | Copilot Chat | Declarative agent |
|---|---|---|---|
| Skills per user | 50 custom [D S1] | Not stated [NF] | 8 per agent [D S8] |
| Skills per plugin package | 20 [D S2, ASKILL-M002] | Not stated; plugin validation is shared [I] | n/a |
| Connectors per package | 10 [D S2] | Not stated | n/a |
| SKILL.md size | 1 MB [D S1]; target under 5,000 tokens [D S2] | 20,000 characters "as an authoring recommendation" [D S7] | Under 20,000 characters, required [D S8] |
| Companion files | 20 files, 5 MB each, 10 MB total, 15-second download [D S2] | "Confirm the file count ... for each target host" [D S7] | 350 files across skills, depth 3 [D S8] |
| Upload archive | 10 MB zipped, 50 MB unzipped, 100 files [D S3] | Not stated | 50 MB zip in Agent Builder [D S8] |

The Cowork archive limit (100 files) and the companion rule (20 files) still conflict. Design to 20. [D S2, S3]

**Where skills load**

- Cowork: built-in, custom (OneDrive), uploaded, shared, plugin and admin-deployed skills. [D S1, S2]
- Copilot Chat (Premium): personal skills and plugin skills. Upload path is **Agents > More agents > Add > Upload skill**. [D S7]
- Declarative agents: skills attached to that agent only. Reuse across agents is "Not supported". [D S6]
- Office apps: out of my part. One cross-part fact matters here: Excel custom skills "are stored in OneDrive, not in the plugin registry". [D S6] So a plugin is not a universal carrier.
- Autopilot (Frontier preview) also takes skills and plugins. [D S19: "Create a skill in a conversation or upload one"]
- Researcher and Analyst: nothing found. [NF: S22, S12, S23, Ignite posts S25, S26]

**Script support and runtime, per host**

| Runtime fact | Cowork | Copilot Chat and declarative agents |
|---|---|---|
| Scripts run | Yes [D S2: "Executed, not loaded into context"] [O] | Yes [D S7] |
| Languages | Only `.py` is shown [D S2]. We ran Python 3.12 on Linux [O] | `.py .js .mjs .cjs .ts .mts .sh .bash` [D S7] |
| Packages | Not documented [NF]. Ours: python-docx 1.2.0, lxml 6.1.3, LibreOffice, pdftoppm [O] | Published list: python-docx, python-pptx, openpyxl, pypdf, pymupdf, pandas, matplotlib, LibreOffice, Pandoc, Poppler, Playwright and more [D S8] |
| pip at run time | Not documented [NF] | "A skill can't install packages at runtime" [D S8] |
| Network | Not documented [NF] | "no network access at runtime, except during browser use" [D S8] |
| Authenticated calls from a script | No documented channel [NF]. "Don't embed secrets in SKILL.md files" [D S2] | "Not supported" [D S8] |
| Calling connectors from a script | Not documented [NF] | "Scripts can't call them through the sandbox" [D S8] |
| File system | Temporary, isolated, removed at task end [D S4] | Sandbox; labels honoured [D S8] |
| Time limit | Not documented [NF]. MCP tool calls should answer in under 30 seconds [D S2] | Not documented [NF] |
| Output | Files appear in the Output folder and OneDrive `Cowork` [D S1] | Not documented for skills [NF]. Code interpreter files are "available for download during the active session only" [D S15] |

Key points:

- Microsoft says the Chat and declarative-agent sandboxes are the same. [D S8: "same sandbox and package inventory apply"] It does **not** say Cowork shares that sandbox. It says the opposite: use the Cowork reference for Cowork. [D S6] The Cowork reference is silent on the runtime. [NF S2, S1, S4]
- Microsoft's general warning now covers every host. [D S6: "Don't assume a skill script can access the network"]
- `lxml` is not in the published Chat package list. python-docx depends on it, so it is almost certainly present. [I] Our redline scripts must not assume it until CC-2 confirms.
- The user article for Chat skills mentions "current preview limits" for scripts, network and package installation when skills are used with Advanced Reasoning. It gives no numbers and no link. [D S9] So some script behaviour in Chat is still described as preview even though skills are GA.

**How a skill is chosen**

- Cowork: description matching at session start, then progressive loading. [D S2: "Always - at startup"] Users can also call `/skill-name`. [D, earlier note S13 2026-10-21] A plugin can be switched on or off per conversation in **Sources & Skills**. [D S10]
- Copilot Chat: automatic or explicit. Each skill has its own automatic-use switch. [D S7: "Users control automatic invocation for each skill"]
- Cowork runs an automatic evaluation and returns a Skill Report. A conflict scan flags skills that compete. [D S1: "Adding an explicit handoff between them resolves the conflict"]
- Name clash with a built-in: the docs still conflict. One page says the agent decides (S10). Another says plugin skills cannot override (S2). Do not reuse built-in names.

**Versioning and updates**

- Keep the manifest `id` stable across versions. [D S2: "Be sure to keep it stable across versions"]
- A shared skill or plugin does not update itself. The owner opens it and selects **Share** again. [D S3: "send the latest version to everyone"]
- A removed or revoked plugin drops out on the next sync. Running conversations continue. [D S2: "On the next sync cycle"]
- An uploaded skill with the same name is not replaced. [D S3: "keeps both and adds a number"]
- OneDrive-folder skills are read at session start, so a file edit takes effect in the next session. [D S1]
- Cowork compares a changed skill against its baseline. [D S1: "Any drop against the baseline blocks the change"] Whether this gate applies to a plugin a user uploaded "Only you" is not stated. [NF]
- Manifest version: the Cowork page's examples use 1.28, the CLI pins 1.29, another page shows 1.30. [D S2, S13] Our 1.30 packages load. [O]
- Admin-deployed update behaviour (replace or duplicate) is not documented. [NF] See CC-10.

### Q2. Cowork

**Name, status, licensing, cost**

- Name: "Microsoft Copilot Cowork", shortened to Cowork. GA since June 2026. [D S11]
- Microsoft now calls it an "agentic system", not an agent. [D S4: "different from an *agent*"]
- Access needs two things. First, the Premium licence. [D S20] [D S2: "Microsoft 365 Copilot license: Required for Cowork users"] Second, a spending policy that selects Cowork. [D S5: "A spending policy is an access control"] This closes the open question in our 2026-11-05 note about a per-user licence.
- A low limit does not block access. [D S5: "A policy with a limit of one credit still grants access"]
- Billing is by use, in Copilot Credits. [D S4: "model responses, tool and skill calls, image generation, and browser tasks"] So every skill run and every call to our MCP connector costs the customer credits.
- Cowork, Copilot Studio agents and the Work IQ API can draw on one shared tenant pool. [D S18: "consume Copilot credits from a shared tenant pool"] A customer's Cowork budget can be eaten by other workloads.
- Higher reasoning effort costs more. [D S21: "slower and uses more credits"] Levels are Light, Medium (default), High, Extra High and Max.
- Enforcement is not instant. A user may start tasks after hitting the limit. [D S5]

**What it reads and writes**

- Reads files, mail and Teams content the user can already reach. [D S11: "inherits your permissions"]
- Attach from device (under 200 MB), or pick from OneDrive, SharePoint or Teams. [D S1, S11]
- Edits existing Word, Excel and PowerPoint files in OneDrive or SharePoint in place, with version history kept. [D S1]
- OneNote: read, and edit pages with approval (new in November 2026). [D S1]
- Sends mail, posts in Teams, schedules meetings, creates mail rules, all behind approval. [D S1]
- Cannot delete files or folders. Cannot read local files. Cannot read encrypted files without EXTRACT and VIEW rights. [D S11]
- SharePoint Embedded is still not named anywhere. [NF] Our 403 stands. [O]
- Previous Copilot Chat conversations can be attached to a Cowork task. [D S1] This is a real Chat-to-Cowork handoff path for users.

**Built-in Office file editing (added after the owner's Office.js test of 2026-11-27)**

What we saw. Asked to run a plugin skill's Office.js, Cowork declined. It said it "can only make changes to a Word document through fixed editing commands, and none of them runs supplied code". It also reported that no document was open. [O]

What Microsoft documents:

- Cowork has built-in **Word**, **Excel**, **PowerPoint** and **PDF** skills. The whole description of the Word skill is one line. [D S1: "Create and edit Word documents."]
- Editing an existing file in place arrived in September 2026. [D S11: "preserving the shared file and its existing version history"]
- The file must be in OneDrive or SharePoint, and the user must attach it or reference it with `/`. [D S1: "use `/` in the compose box to reference an existing stored file"] This matches the "no document was open" message. Cowork has no idea of an open document. It works on a file the user points to. [I]
- Cowork asks for edit access first. The choices are **Approve once** and **Always allow** (current conversation only). [D S1]
- The change is saved straight into the shared file. [D S1: "Cowork saves the changes to the same shared file."] There is no draft copy. Version history is the only way back. [D S1]
- The user can then open the file inside Cowork. [D S1: "view edits side-by-side and continue editing"]
- Nothing describes the editing commands themselves. No page lists them, and no page says a skill or plugin can call them or add to them. [NF S1, S2, S11]

Tracked changes and comments:

- No Cowork page mentions tracked changes, redlines or document comments. I checked the use guide, the FAQ and the change log for those words. [NF S1, S11] The only "comments" on those pages are feedback comments on chat messages.
- Microsoft's legal launch post ties tracked changes to Word, not Cowork. [D S37: "Word applies the edit as a native tracked change"] The same post describes Cowork as the place for work "across multiple files and steps", with examples such as tracking obligations and building chronologies. It does not say Cowork redlines. [D S37]
- For Copilot inside Word, Microsoft says edits are tracked only if Track Changes is already on, and Copilot cannot switch it. [D S38, search summary only; part 1 owns this] Whether Cowork's in-place editor behaves the same way is not stated. [NF]

What this means for us:

- Cowork has two separate ways to change a .docx. One is the built-in editor, which works in place and whose tracked-change behaviour is unknown. The other is a skill script (our Python engine), which writes a new file with tracked changes and comments. [O] They are not connected.
- Office.js is not a route in Cowork. [O] It needs an Office host with a document open. Cowork has neither. [I]
- There is a real risk in the fallback. If a redline skill fails, the model may reach for the built-in editor and change the customer's source document in place, possibly with no revision marks. [I, from "saves the changes to the same shared file"] The edit-access approval card is the only stop. Our skills should say plainly: never edit the source file in place; always write a new file; if the script cannot run, stop and say so.
- CC-14 settles whether the built-in editor can produce tracked changes and comments.

**Connectors and MCP inside Cowork**

- A plugin connector is a remote MCP server over streamable HTTP. [D S2]
- Auth: `None`, `OAuthPluginVault`, or dynamic client registration by omitting the auth block. API key is "not available in Cowork yet". [D S2]
- The DCR shortcut is Cowork only. [D S2: "It isn't supported in Copilot Chat"] A plugin meant for both hosts must use `OAuthPluginVault`. This matters for us because Entra has no DCR endpoint anyway. [D S13]
- New rule on annotations. [D S2: "Tools without annotations are treated as destructive and require confirmation"] But for non-Microsoft servers the annotation-driven prompt "is being rolled out progressively". [D S2] Set `readOnlyHint` and `destructiveHint` on every tool now.
- Files go from the workspace to our server through a `contentEncoding: base64` parameter. 8 files and 150 MiB per call. [D S2] The reverse direction (a tool result becoming a workspace file) is still not documented. [NF] Microsoft notes its mechanism predates the MCP file-uploads work and it "might add support for the standardized form". [D S2]
- Tool description file: still a doc conflict. The Cowork page says Cowork "doesn't use this file". The plugin page says "Copilot Cowork still requires a tool description file". [D S2, S13] Keep shipping it.
- Cowork also takes Power Platform connectors, Copilot connectors and federated connectors. [D S11, S12]
- Microsoft's statement on ISV data is new and useful for our customers' security reviews. [D S2: "doesn't retain ISV Content beyond the duration of that session"]

**Approval cards**

- Options are: the action button, **More options** (skip similar prompts for this conversation), **Approve All**, and **Cancel**. [D S1]
- Editing an existing Office file has its own card: **Approve once** or **Always allow**. [D S1]
- "Don't ask again" lasts for the current conversation only. [D S1]
- Medium and high risk approvals show a risk indicator. [D S11]
- A connector tool that takes a file always asks first. [D S10: "asks for your approval before the tool runs"]
- "No email" is not an option name in any Microsoft page. [NF S1, S11] The card we answer with "No email" is therefore most likely a question card ("Answer questions from Cowork"), not an approval card. [I] That distinction matters: a question card can be skipped by the model's own judgement, an approval card cannot.
- GCC is stricter. Every action needs approval, including file creation. [D S4]

**Scheduled and long-running work**

- Scheduled prompts: up to 25 per user. [D S1]
- Event-driven tasks: on a matching email or Teams message, and since October on events from non-Microsoft connectors and plugins. [D S11, S1]
- They run as the user, with rate limits and loop protection. Default is draft-and-approve. Users can pre-authorise actions when creating the task. [D S4]
- Cowork is built for long tasks. [D S11: "Minutes to hours (autonomous execution)"] No maximum task duration is documented. [NF]
- For scheduled tasks, Auto uses the default model, not a per-request choice. [D S21]
- Whether a scheduled run can run skill scripts unattended and still reach an approval-gated connector is not stated. [NF] See CC-9.

**Outputs**

- Created files go to the user's OneDrive **Cowork** folder and show in the side panel Output folder. [D S1]
- **Download All** gives a zip of up to 50 files and 500 MB. [D S1]
- Previews: PDF, Office, CSV, Markdown, code, images, HTML, email. [D S1]
- New Office and PDF files inherit the sensitivity label of their source. Not for files over 50 MB, and not for user-defined permissions. [D S27]

**Platforms**

- Web at copilot.cloud.microsoft, the Copilot desktop app for Windows and macOS, the mobile app for iOS and Android. [D S11] Also in Outlook and Teams. [D S28]
- Cowork in Edge for Business (side pane and new tab page) is GA since Ignite. [D S25]
- Mobile gaps: local browser is not available. [D S29] "Custom skills aren't supported in Cowork on mobile". [D S1] "Custom plugins aren't supported in Cowork on mobile". [D S2] So our accelerator, as placed today, does not work on a phone.
- GCC rollout began in November 2026, GPT models only, no Purview support yet. [D S4, S25]

**Doc conflicts worth knowing**

- The application card still says steps run "sequentially" and that memory "does not persist across conversations". [D S28, page date 2026-11-04] Ignite announced Auto with subagents that run side by side (Frontier), and Projects and Cowork memory exist. [D S25, S11] The card is behind the product.
- The application card also says Cowork is not intended for uses needing guaranteed accuracy without review, and names "legal filings". [D S28] Our user guide should carry the same human-review line.

### Q3. Copilot Chat

**Skills and scripts**

- Skills in Copilot Chat are GA. [D S7: "Skills in Microsoft 365 Copilot Chat are generally available"]
- Premium only. [D S7: "aren't available to Copilot Chat (Basic) users"]
- Not available in Information Barriers tenants. [D S7] The Message Center post said IB support was due in November. [S S24]
- A skill can carry scripts, and they run in the documented sandbox (Q1). [D S7]
- A plugin with `agentSkills` is a documented way to deliver a Chat skill. [D S7: "A skill distributed as part of a plugin"]
- Microsoft tells authors to test both hosts separately. [D S7: "Don't infer availability or editing support in one product"]
- Chat-created skills also work in Cowork. Cowork-created skills show in Chat "only if they are shared". Admin-deployed plugins go to both. [S S24, a mirror of MC1493223; the Learn pages do not restate it]
- Consequence for us: a skill placed as an OneDrive folder, or a plugin uploaded "Only you" in Cowork, may not show in Chat. Untested. See CC-1 and CC-5.

**Code execution without a skill**

- Code interpreter writes and runs Python in a sandbox. It takes Word, Excel, PowerPoint, PDF, CSV and text files and returns downloadable files. [D S15]
- It is on by default for in-context agents. [D S15: "enabled by default for in-context agents"]
- Available to licensed users and to unlicensed Chat users. [D S15: "Copilot Chat users without metered usage enabled"] Fair-use limits apply on the free tier and Microsoft does not publish numbers. [D S20, support page]
- Files are temporary. [D S15: "Files are not persisted after the session ends"]

**MCP connectors**

- Three registration models exist. Do not mix them up (see part 3 for detail).
  1. An MCP action inside a declarative agent. Works in Chat for all tiers, inside that agent. [D S14]
  2. A package-level `agentConnectors` entry. Documented for "supported Microsoft experiences" without a host list. [D S13: "Support for MCP alone doesn't establish support for this registration model"] Chat support is implied by the DCR note and by the Chat skills page. [D S2, S7] Not tested by us.
  3. A federated connector from the Gallery or created by an admin. Works in Chat, Agents, Cowork, the Office apps and Researcher. Needs the Premium licence. [D S12]
- Federated write tools ask the user: Allow once, Allow for conversation, Always allow, Cancel. Read tools never ask. [D S12]
- "Connector-only plugins remain under the Data Sources menu for now". [S S24] In Chat, users connect sources under **Settings > Sources**. [D S12]

**UX components and MCP Apps**

- MCP Apps widgets render in Copilot Chat inside a declarative agent. [D S17: "allow declarative agents to go beyond text responses"]
- The capability table lists MCP Apps as available without a licence and without usage billing. [D S14]
- Whether a widget renders from a package-level connector with no agent is not stated. [NF S17, S13]
- Chat and Cowork support different widget features (Chat: fullscreen only, no `frameDomains`; Cowork: no `update-model-context`). [D S17, S16] One server must tolerate both. Detail is in part 3.

**Licensed versus free**

| | Chat free (Basic) | Chat licensed (Premium) |
|---|---|---|
| Grounding | Web. Work content only if uploaded or open [D S20] | Web plus work data through Graph and Work IQ [D S20] |
| Skills | No [D S7] | Yes [D S7] |
| Declarative agents with instructions and web | Yes, free [D S14] | Yes |
| Agents grounded in tenant data | Pay as you go [D S14] | Included [D S14] |
| Code interpreter | Yes, fair-use limits [D S15] | Yes |
| MCP Apps in agents | Yes [D S14] | Yes |
| Federated connectors | No [D S12: "has a Microsoft 365 Copilot license"] | Yes |
| Researcher, Analyst | No [D S22: "Microsoft Copilot licensed users"] | Yes |
| Cowork, Code, Autopilot | No | Only with a spending policy [D S20] |

### Q4. Built-in agents

**Researcher**

- Cannot be customised. [D S22: "you can't modify Researcher's predefined logic or content"] That page is dated 2025-10-31, refreshed 2026-11-04. Treat the wording as possibly stale; nothing newer contradicts it.
- It does read connectors. Federated MCP connectors are supported in Researcher, read only. [D S12: "Federated Copilot connectors remain read-only in this experience"]
- Users find the source in Researcher's **Sources** menu and sign in with their own account. [D S12]
- New connectors are being added one by one (HubSpot, Canva in October-November 2026). [D S30]
- It can be added to a normal chat with `@Researcher`. [D S22]
- Agent-to-agent is "To be released soon". [D S22, stale]
- Usage is capped. [D S22: "maximum of 25 queries per user per month", stale]
- Compliance gap. Session content is not reviewable by default. [D S22: "isn't directly accessible to admins or compliance tools", stale] A law firm should check this before allowing matter research there.
- Skills in Researcher: nothing found. [NF S22, S7, S9, S24, S25, S26]

**Analyst**

- Runs Python on user data. [D S23, a Microsoft blog from March 2025, stale] The current support page describes statistics, charts and tables from uploaded or OneDrive files but does not use the word Python. [D S31]
- No page says Analyst takes connectors, plugins or skills. It is not in the federated connector host list. [D S12 by absence] [NF S31, S23]
- Whether Analyst opens a .docx and runs python-docx on it is not documented. [NF] See CC-8.

**Others**

- Autopilot (Frontier, preview): a persistent cloud agent with its own Entra identity, mailbox, memory and routines. It takes skills and plugins and is billed by use. [D S19, S20] One per person. Not production-ready.
- Copilot Code (Frontier): GA "in the coming weeks". [D S26] Out of scope here except as a direction signal.
- Word, Excel and PowerPoint agents: part 1.

**Can our playbooks reach them?**

- Researcher: only as read tools on a federated connector. That means either Microsoft approves us into the Connectors Gallery, or each customer admin creates a custom federated connector to our MCP server. [D S12] No skill instructions travel with it. [I]
- Analyst: no documented route. [NF]
- Neither agent can be invoked by a Cowork skill. Cowork has no agent-calling contract. [D, earlier note 2026-10-31; nothing changed in S2: "`agents/` (sub-agents) | Not yet supported"]

### Q5. Distribution and governance

**Who can get our package**

| Route | How | Lands in | Admin needed |
|---|---|---|---|
| Only you | Cowork **Plugins > Add > Upload**, Share "Only you" [D S3] | The uploader's Cowork. Chat: Unknown | No |
| Specific people | Same, "Specific users in your organization" [D S3] | **Shared with me** [D S2] | No [D S2: "doesn't require admin approval"] |
| Whole tenant, shared | `wiqd plugin share --scope tenant` [D S2] | Shared with me | Yes [D S2: "Organization-wide sharing requires admin approval"] |
| Whole tenant, listed | Admin uploads at **Agents > Tools > Registry > Upload**, then scopes users or groups [D S32] | **Discover** in Cowork [D S2]; Chat **Agents & Skills** [D S9] | Yes |
| Public | Partner Center, "Microsoft 365 and Copilot" program [D S33] | Microsoft Marketplace and the Copilot Plugin Catalog | Each customer admin still installs and allows it [D S34] |
| Personal Chat skill | Chat **Agents > More agents > Add > Upload skill** [D S7] | That user's Chat | No |
| OneDrive folder | Copy to `Documents/Cowork/skills/<name>/` [D S1] | That user's Cowork | No |

- There is one admin registry for plugins, skills and MCP servers. [D S32] Microsoft announced a "plugin catalog" and a "unified plugin registry" at Ignite. [D S25, S26]
- Admin install, uninstall, block and availability are per package, not per skill. [D S32: "aren't separate controls for each included component"]
- Publisher categories gate everything: Microsoft, your organization, certified external publishers. [D S32] If "your organization" is cleared, user-uploaded plugins show as blocked. [I]
- Plugin governance and MCP governance are separate. [D S32: "Blocking a plugin ... MCP servers and connectors remain available"]
- Users can request access to a blocked plugin; admins approve under **Agents > Tools > Requests**. [D S32]
- Doc conflict: the Cowork plugin page lists "Admin consent required for MCP registration and server-linked plugins". [D S2] The same page says sharing with specific people needs no admin. Our own "Only you" uploads with a connector work. [O] The consent line most likely refers to the Agent 365 MCP registry path. [I]
- Public listing is not automatic for skills. [D S6: "doesn't establish eligibility for every standalone skill or composed plugin package"]
- Existing assets may need rework for the registry. [D S11: "some owners need to package, validate, publish, or republish"] Microsoft has not yet published migration guidance.
- Information Barriers tenants: plugin and skill upload and sharing are blocked in Cowork; skills are unsupported in Chat. [D S2, S7] Some law firms run IB for ethical walls. This is a hard stop for them today.

**Tenant controls for Cowork**

- Access: spending policy scope. [D S5]
- Discovery: a separate visibility setting. [D S5]
- Models: tenant switches per provider; Anthropic can be scoped to users or groups. [D S4, S35]
- Browser use: **Cowork Browsing** tenant setting. [D S4]
- Cost: per-user and per-group limits; Agent 365 Cost Management. [D S4, S25]

**Purview**

- Supported for Cowork: DSPM, audit, sensitivity labels, DLP, Insider Risk, Communication Compliance, eDiscovery, retention. Not supported: data classification, Compliance Manager. [D S27]
- Audited events Microsoft lists. [D S27: "A user adds, removes, or shares a skill or plugin"] Also: starts a conversation, runs a scheduled prompt, starts a browser task, uploads a file, and Cowork creates a file in OneDrive.
- **Script runs and individual connector tool calls are not in that list.** [NF S27] Federated connector activity is auditable. [D S12: "Admins can audit activity in Microsoft Purview"] Whether a plugin connector call is audited the same way is not stated. See CC-11.
- eDiscovery covers transcripts, files Cowork created, scheduled prompts and uploaded files. [D S27]
- Cowork activity does not appear in the DSPM "Apps and agents" dashboard or "AI observability" page. [D S27]
- GCC: Purview features are "not yet supported". [D S4]

**Data residency and compliance statements**

- Cowork "follows the same data residency model as Copilot". [D S4]
- Task processing happens in a temporary environment "inside the *Microsoft 365 service boundary*". [D S4]
- Claude models are excluded from the EU Data Boundary and from in-country processing commitments. [D S35: "currently excluded from the EU Data Boundary"]
- In the EU, EFTA and UK, Claude is off by default. [D S35] In EU Data Boundary tenants only GPT models are supported in Cowork. [D S21]
- A setting change for EU and UK tenants takes effect on 2026-12-09. [D S35] I did not read the linked page.
- Fable-class models may require data retention by Anthropic, outside the Microsoft DPA. [D S35: "stores most inputs and outputs for up to 30 days"] For some organisations Fable 5.1 runs under the Microsoft DPA with no retention. [D S35] Cowork's page says Fable 5.2 "requires data retention". [D S11 whats-new] A legal customer should keep Fable off unless its terms are checked.
- Grok is provided by xAI as an independent processor, not a subprocessor. [D S21]
- GPT 6.2 Sol is hosted by Microsoft and still works if OpenAI is turned off as a subprocessor. [D S21] This is the safest model line to recommend to a cautious legal tenant.
- Our skills produce different results on different models. [I, from our own model comparisons] An EU customer running GPT only is a distinct test target.
- Frontier features carry no ISO 27001 statement and no SLA. [D, earlier note 2026-11-05] Subagents, Autopilot and Workspaces are all Frontier.

### Q6. Direction

Announced at Ignite, 17 November 2026 (S25, S26):

| Item | Status | Why it matters to us |
|---|---|---|
| Home tab joining Chat and Cowork | GA | One entry point; users move between the two hosts our skills target |
| Office in Copilot | GA | Part 1 |
| App building in Cowork and Copilot Managed Runtime | GA | A Microsoft-hosted way to ship dashboards; competes with our HTML outputs |
| Cowork in Edge for Business | GA | A possible route to our web app through the user's own session |
| Plugin catalog and unified plugin registry | Announced, no status | One publish, several hosts. Confirms plugins as the carrier |
| Auto with effort levels and parallel subagents | Frontier | Ends the "sequential only" rule, but only in preview. Skills still cannot define subagents |
| Autopilot | Frontier, rolling out | Persistent agent that takes skills and plugins. A future host |
| Copilot Code | GA "in the coming weeks" | Direction only |
| Legal skills | "More than 20", no new status | Microsoft keeps investing in first-party legal content |
| Entra Agent ID controls for MCP servers, plugins and skills | Announced, no status | More admin gates in front of our connector |
| Cowork in GCC | Rolling out | Government legal departments |
| Agent 365 Cost Management and Business Analytics | Announced; Analytics in Frontier | Customers will see what our skills cost them |

Other direction signals:

- Tooling. Microsoft now steers developers to Work IQ Dev Tools (`wiqd`) for plugins. One page calls it "generally available" (S14). Others still say "preview" and "not production-ready" (S2, S12). Treat as preview.
- Plugin Builder is the new name for Agent Builder in one place. [D S2: "Plugin Builder (formerly Agent Builder)"]
- File inputs. Cowork may adopt the standard MCP file-upload form later. [D S2] No date.
- Annotation-driven confirmations for third-party MCP servers are still rolling out. [D S2]
- No roadmap or Message Center item was found for: Cowork script runtime documentation, network egress policy, tool results as files, skills in Researcher or Analyst, or SharePoint Embedded in Cowork. [NF: searched the Microsoft 365 roadmap, the release notes S30 (first 100,000 characters only), and the Ignite posts]
- Build 2026 (May): Work IQ GA and federated connector GA came from that period. [D, earlier notes] I did not re-read Build material this pass.

### How this reconciles with our own evidence

| Our finding | Docs say | Verdict |
|---|---|---|
| Cowork runs plugin-shipped Python 3.12 on Linux | Scripts are "Executed". Runtime not described | Consistent. Our tenant result is still the only source for versions |
| Cowork runs OneDrive skill folders | Documented path, lowercase `skills` | Consistent |
| Approval cards; we choose "No email" | No such option is documented | Probably a question card, not an approval card. Check |
| Cowork cannot read SPE bytes | SPE not named anywhere | Consistent |
| Cowork will not run a skill's Office.js; it has "fixed editing commands"; "no document was open" | Built-in Word skill edits OneDrive or SharePoint files in place. No code-running route is described. Files are attached, not open | Consistent. Tracked changes and comments through the built-in editor are not documented either way (CC-14) |
| Script egress undocumented | Still undocumented for Cowork; "no network" for Chat | Test it (CC-3). Expect blocked |
| MCP tool result to sandbox file untested | Still undocumented | Test it (CC-4) |
| Manifest 1.30 with `agentSkills` uploads | Examples say 1.28 or 1.29; another page shows 1.30 | Fine |

---

## 3. Owner-run tests

Each test settles one unknown. Run them in the DEV tenant with a Premium-licensed, Cowork-enabled test user. Turn our MCP connector off unless the test needs it.

**CC-1. Does our "Only you" plugin load and run in Copilot Chat?**
- Setup: the current skills-only plugin zip, already uploaded "Only you" in Cowork.
- Where: copilot.cloud.microsoft, **Chat** (not Cowork). Open **Agents > More agents**, then **Agents & Skills**.
- Step 1: look for the plugin or its skills in the list.
- Step 2: select the NDA review skill explicitly. Attach a small NDA .docx. Prompt: "Run the NDA review on the attached file and give me the redlined document."
- Pass: the skill is listed, the script runs, and a .docx with tracked changes comes back.
- Partial: listed but the model answers in prose with no script run. Record the exact message.
- Fail: not listed. Then run CC-5 and CC-10 to find which placement makes it appear.

**CC-2. What is the Chat script sandbox, really?**
- Setup: a probe skill zip with `SKILL.md` at the root and `scripts/probe.py`. The script prints `sys.version`, `platform.platform()`, the result of importing `docx`, `lxml`, `openpyxl`, `fitz`, `pandas`, whether `soffice` and `pandoc` are on PATH, the working directory, and whether it can write `out/probe.txt`.
- Where: Chat, **Agents > More agents > Add > Upload skill**.
- Prompt: "Use the probe skill. Run scripts/probe.py and show me its full output without changes."
- Pass: output shows Python and the packages our scripts need (`docx`, `lxml`).
- Fail: `lxml` or `docx` missing, or the script does not run. Then our redline engine cannot run in Chat as built.
- Also record: whether the user sees the run, and whether a written file is offered for download.

**CC-3. Cowork script egress, pip and time limit (the test already planned).**
- Setup: probe skill in `Documents/Cowork/skills/probe/` with three scripts.
  - `net.py`: DNS lookup and HTTPS GET to a neutral public host, then to our BFF `/healthz`. Print the exception class and any response headers.
  - `pip.py`: run `python -m pip download six -d /tmp/x` and print the result.
  - `sleep.py`: print a timestamp every 30 seconds for 20 minutes.
- Where: Cowork, new conversation. Prompt per script: "Run scripts/<name> from the probe skill and show the raw output."
- Pass for egress: status 200 from either host. Then egress exists today, and a security review is still needed before any use.
- Fail for egress: an exception or a proxy block header. Record it. This is the expected result and confirms connectors are the only route.
- Pip pass or fail: package downloaded or not.
- Time limit: note the last timestamp printed. That is the practical script ceiling.

**CC-4. Can an MCP tool result become a file in the Cowork sandbox?**
- This is Test 2 in `../cowork-access-to-spe-files-2026-11.md`. Run it as written (three result shapes: base64 text, embedded `resource` blob, `resource_link`).
- Pass: Python opens the returned file from disk.
- Fail: bytes only appear as text in the conversation. Then keep device upload or OneDrive staging.

**CC-5. Do OneDrive-folder skills and Cowork-shared skills show in Chat?**
- Step 1: with a skill present only in `Documents/Cowork/skills/`, open Chat **Agents & Skills**. Listed or not.
- Step 2: in Cowork **Plugins**, open that skill, **Share > Specific users**, add a second test user, **Publish**. Check Chat for both users.
- Pass: listed after step 1 (best case) or after step 2.
- Fail: never listed. Then Chat needs its own upload or an admin deployment.

**CC-6. Where do Chat skill outputs go, and do they last?**
- After CC-1 passes, close the chat, reopen it an hour later, and try the download link. Check the user's OneDrive for the file.
- Pass: the file is in OneDrive or the link still works.
- Fail: link dead and no OneDrive copy. Then Chat is a preview surface, not a system of record, and the skill must tell the user to save the file.

**CC-7. Does our MCP connector work in Chat from the same plugin?**
- Setup: the plugin variant with `agentConnectors` and `OAuthPluginVault`.
- Where: Chat. Check **Settings > Sources** and the plugin's detail page for the connector. Prompt: "Using the Spaarke connector, list my matters." Then a write tool.
- Pass: sign-in prompt appears once, the read tool returns data without a prompt, the write tool shows a confirmation card.
- Also check: does a tool with a `ui://` resource render a widget in Chat when no declarative agent is involved?
- Fail: the connector is absent in Chat. Then Chat needs a declarative agent or a federated connector (part 3).

**CC-8. Can Analyst process a .docx with Python?**
- Where: Chat, select **Analyst**. Upload a small NDA .docx.
- Prompt: "Use Python with python-docx to list every tracked change in this file. Show the code you ran."
- Pass: code is shown and the list is correct. Then Analyst is a usable no-install fallback for document inspection.
- Fail: it refuses, or summarises without running code.
- While there: note whether the **Agents & Skills** picker or a **Sources** menu is offered inside Analyst and inside Researcher.

**CC-9. Does a scheduled Cowork run execute a skill and its script unattended?**
- Prompt: "Every day at 08:00, run the NDA review skill on any new .docx in my OneDrive folder NDA-inbox and save the redline to NDA-out. Do not send anything."
- Choose **Activate and run now** once, then let one scheduled run happen with the laptop closed.
- Pass: the redline appears in OneDrive with no approval needed.
- Partial: the run stops on an approval card. Record which action asked.
- Record the model badge used and the credits from `/cost`.

**CC-10. Admin deployment: where does one upload land, and does an update replace?**
- Step 1: as admin, **Agents > Tools > Registry > Upload** the plugin zip. Scope to one test group. Install.
- Step 2: as a group member, check Cowork **Plugins** (Added and Discover), Chat **Agents & Skills**, and the Word Copilot pane.
- Step 3: raise `version` in the manifest, keep the same `id`, upload again.
- Pass: one entry in each host, showing the new version after the next sync. Note the time it takes.
- Fail: a duplicate entry, or the old version persists after 24 hours.

**CC-11. What does Purview record for a skill run?**
- After a Cowork run that used a plugin skill, a script and one connector tool call, wait an hour.
- In the Purview portal, search **Audit** for the test user and Copilot activities. Also open DSPM for AI **Activity explorer > AI activities**.
- Pass: records name the conversation and the file created. Record whether the skill name, the script run and the connector tool call appear.
- This tells us what a law firm's compliance team will and will not be able to show.

**CC-12. Is the "No email" card an approval card or a question card?**
- Re-run the flow that shows it. Take a screenshot. Check the side panel **Permissions** section afterwards.
- Approval card: it has an action button such as **Send**, **Show parameters**, and it leaves an entry in Permissions when "don't ask again" is chosen.
- Question card: it has choices and **Submit** or **Skip**.
- If it is a question card, the skill text, not the platform, is what stops the email. Then tighten the skill wording and never rely on the card.

**CC-13. Mobile.**
- On the Copilot mobile app, open Cowork, **+ > Skills**. Check whether our uploaded plugin and our OneDrive skill are listed, and try the NDA review on a OneDrive file.
- Expected: not available. Confirm, so the user guide can say so.

**CC-14. Can Cowork's built-in Word editor make tracked changes and comments?**
- Setup: two copies of a small NDA .docx in OneDrive. Copy A has Track Changes off. Copy B has Track Changes on (Review > Track Changes > For Everyone), saved and closed.
- Where: Cowork, new conversation, our plugin and skills switched off in **Sources & Skills** so only the built-in Word skill can act.
- Run 1: attach copy A with **Attach cloud files and folders**. Prompt: "Edit this file in place. In the term clause change 'two (2) years' to 'three (3) years' as a tracked change. Add a comment on the governing law clause that says 'Check with client'. Use your own Word editing tools. Do not run Python or any script."
- Run 2: attach copy B. Prompt: "Edit this file in place. Change 'two (2) years' to 'three (3) years'." Say nothing about tracking.
- Check each file in Word desktop, Review tab, **All Markup**.
- Pass: the change shows as an insertion and a deletion with an author name, and the comment exists. Record the author name shown (the user or Copilot).
- Partial: run 2 is tracked and run 1 is not. Then tracking follows the document's own setting, as in Word.
- Fail: both edits are clean with no revision marks, or Cowork says it cannot. Then tracked redlines in Cowork can only come from our Python engine, and every skill must forbid in-place edits of the source.
- Also record: the wording of the edit-access card, whether Cowork quietly used a script anyway (look for a new file in the Output folder instead of a change to the source), and whether version history shows a new version.

---

## 4. Biggest unknowns

1. The Cowork script runtime contract: languages, packages, egress, time limit. Still undocumented. (CC-3)
2. Whether one plugin package really runs in both Chat and Cowork, and under which placement. Documented in principle, untested by us. (CC-1, CC-5, CC-10)
3. Whether a tool result can become a sandbox file. (CC-4)
4. Whether a package-level connector and its widgets work in Chat without a declarative agent. (CC-7)
5. What Purview records for script runs and plugin connector calls. (CC-11)
6. Whether Researcher or Analyst will ever take skills. Nothing announced.
7. Whether Cowork's built-in Word editor can make tracked changes and comments, and whether a skill can steer it. Not documented. (CC-14)

---

## 5. Sources

All checked 2026-11-27. Dates are the page's `ms.date` / last update unless noted.

| Id | Source | Date | Note |
|---|---|---|---|
| S1 | https://learn.microsoft.com/en-us/copilot/cowork/use-cowork | 2026-11-25 / 11-26 | Most authoritative for Cowork behaviour |
| S2 | https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development | 2026-11-16 / 11-17 | Most authoritative for packaging, connectors, file input |
| S3 | https://learn.microsoft.com/en-us/copilot/cowork/cowork-customize | 2026-11-25 / 11-26 | Upload, share, re-share |
| S4 | https://learn.microsoft.com/en-us/copilot/cowork/cowork-admin-governance | 2026-11-17 / 11-18 | Access, billing, automation safeguards, residency |
| S5 | https://learn.microsoft.com/en-us/copilot/cowork/cowork-access | 2026-11-04 / 11-18 | Spending policy as access control |
| S6 | https://learn.microsoft.com/en-us/plugins/plugin-type-skills | 2026-11-11 / 11-16 | The three skill routes |
| S7 | https://learn.microsoft.com/en-us/plugins/build-copilot-chat-skills | 2026-11-19 | Most authoritative for skills in Chat |
| S8 | https://learn.microsoft.com/en-us/plugins/declarative-agent-skills | 2026-11-18 / 11-19 | Sandbox table and package list |
| S9 | https://support.microsoft.com/en-us/microsoft-365-copilot/create-edit-skills-copilot | undated | User article; mentions preview limits with Advanced Reasoning |
| S10 | https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugins | 2026-11-25 / 11-26 | Plugin use, sign-in, name clash |
| S11 | https://learn.microsoft.com/en-us/copilot/cowork/cowork-faq and https://learn.microsoft.com/en-us/copilot/cowork/whats-new | 2026-11-16 and 2026-11-25 / 11-26 | Limits, platforms, change log |
| S12 | https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/federated-connectors-overview | 2026-11-16 | Host list for federated connectors |
| S13 | https://learn.microsoft.com/en-us/plugins/configure-plugin-mcp-servers | 2026-10-28 / 11-16 | `agentConnectors`, auth types, tool description file |
| S14 | https://learn.microsoft.com/en-us/plugins/prerequisites | 2026-11-16 | Capability-by-licence table |
| S15 | https://learn.microsoft.com/en-us/plugins/code-interpreter | 2026-11-09 / 11-16 | Code interpreter |
| S16 | https://learn.microsoft.com/en-us/copilot/cowork/mcp-apps-support | 2026-09-08 / 11-03 (from our 11-05 note; not re-read) | MCP Apps in Cowork |
| S17 | https://learn.microsoft.com/en-us/plugins/plugin-mcp-apps | 2026-11-09 / 11-16 | MCP Apps in Chat |
| S18 | https://learn.microsoft.com/en-us/plugins/cost-considerations | 2026-11-16 | Shared credit pool |
| S19 | https://learn.microsoft.com/en-us/copilot/autopilot/overview | 2026-11-10 / 11-24 | Prerelease |
| S20 | https://learn.microsoft.com/en-us/copilot/plan/copilot-overview | 2026-11-17 | Licence tiers and names |
| S21 | https://learn.microsoft.com/en-us/copilot/cowork/cowork-models | 2026-11-06 / 11-26 | Models, EU note, effort levels |
| S22 | https://learn.microsoft.com/en-us/copilot/manage/faq-researcher | 2025-10-31 / 2026-11-04 | **Stale ms.date**; only Researcher FAQ found |
| S23 | https://techcommunity.microsoft.com/blog/Microsoft365CopilotBlog/analyst-agent-in-microsoft-365-copilot/4397191 | March 2025 | **Stale**; seen through search summary only |
| S24 | https://mc.merill.net/message/MC1493223 | published 2026-10-09 | Mirror of a Message Center post; secondary |
| S25 | https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/whats-new-in-copilot-cowork-at-ignite-2026/4572242 | 2026-11-17 | Microsoft blog |
| S26 | https://www.microsoft.com/en-us/copilot/blog/2026/11/17/copilot-at-microsoft-ignite-expanding-whats-possible-with-intelligence-built-for-work | 2026-11-17 | Microsoft blog |
| S27 | https://learn.microsoft.com/en-us/purview/ai-copilot-cowork | 2026-11-09 / 11-10 | Most authoritative for audit and eDiscovery |
| S28 | https://learn.microsoft.com/en-us/copilot/cowork/responsible-ai/copilot-cowork-application-card | 2026-11-04 / 11-18 | Behind the product in places |
| S29 | https://learn.microsoft.com/en-us/copilot/cowork/cowork-mobile | 2026-09-11 / 11-18 | Mobile |
| S30 | https://learn.microsoft.com/en-us/copilot/microsoft-365/release-notes | 2026-09-03 / 11-18 | Only the first tenth of the page was read |
| S31 | https://support.microsoft.com/en-US/Microsoft-365-Copilot/use-analyst-for-deep-data-analysis | undated | Analyst user article |
| S32 | https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers | 2026-11-24 / 11-25 | Most authoritative for admin controls |
| S33 | https://learn.microsoft.com/en-us/plugins/isv-publisher-guide | 2026-11-16 | ISV route |
| S34 | https://learn.microsoft.com/en-us/plugins/publish and https://learn.microsoft.com/en-us/plugins/overview | 2026-11-16 | Distribution routes, registry terms |
| S35 | https://learn.microsoft.com/en-us/copilot/implement/connect-to-ai-subprocessor | 2026-11-20 | Anthropic terms, EU and UK defaults |
| S36 | https://microsoft.github.io/wiqd/getting-started/plugin-reference/ | wiqd v1.1.0; limits verified 2026-10-14 | Limits cross-check |

| S37 | https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/introducing-microsoft-copilot-for-legal/4564246 | 2026-10-15 | Microsoft blog; tracked changes described for Word, not Cowork |
| S38 | https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/copilot-in-word-new-capabilities-for-document-workflows/4508974 | not read; search summary only | Copilot in Word and Track Changes; part 1 owns this |

Low-authority items used only as pointers: search-result summaries of third-party blogs (windowsforum, datastudios, Directions on Microsoft, a university FAQ on Analyst). None carries a claim on its own.

Repo and earlier notes used:

- `projects/spaarke-legal-ai-accelerator-r1/notes/research/cowork-access-to-spe-files-2026-11.md`
- `.claude/agent-memory/researcher/cowork-skill-runtime-and-mvp-claims-2026-10-30.md`
- `.claude/agent-memory/researcher/cowork-spe-playbooks-automation-2026-10-30.md`
- `.claude/agent-memory/researcher/cowork-builtin-skills-and-specialist-agent-patterns-2026-10-31.md`
- `.claude/agent-memory/researcher/word-plugin-skills-not-executed-2026-11-02.md`
- `.claude/agent-memory/researcher/copilot-as-spaarke-surface-and-frontier-2026-11-05.md`
- `knowledge/m365-copilot`, `knowledge/work-iq`, `knowledge/declarative-agents`, `knowledge/mcp-apps` (NOTES.md files are stubs; nothing there on skills, Cowork or the built-in agents)

## 6. Caveats

- Cowork and plugin pages changed on 2026-11-16, 11-19, 11-24, 11-25 and 11-26. Re-check before a decision.
- The statement that Cowork skills show in Chat "only if shared" comes from a mirror of a Message Center post, not from Learn.
- The Researcher FAQ and the Analyst blog are old. Their facts may have moved.
- I could not get the full text of the Chat skills user article (S9); the tool returned a paraphrase. The "preview limits" sentence should be read at source.
- Frontier items (subagents, Autopilot, Workspaces) may change or never ship.
- I did not test anything in a tenant. Every "not tested by us" cell needs its CC test before it is treated as true.
