# Developer extension routes into Microsoft 365 Copilot, by surface, and where the platform is heading

Part 3 of 3. Researched 2026-11-27. Ignite 2026 ran 17-20 November, so this includes what Microsoft announced there.

Part 1 owns Copilot's own behaviour inside Word, Excel and PowerPoint. Part 2 (`02-chat-cowork-agents.md`) owns Copilot Chat, Cowork, the skills runtime and the built-in agents. This note covers the routes a developer can use, who can reach them, and Microsoft's direction. Facts from the other parts appear here only where they change an answer.

Builds on the researcher notes of 2026-10-29 to 2026-11-26 and on `../../poc-e2e/results.md`.

**Tags used on every claim**

- **[D]** Documented. A Microsoft page says it. Source id and a short quote follow.
- **[O]** Observed. Seen in our own tenant tests. This is our evidence, not Microsoft's word.
- **[I]** Inferred. Reasoned from documented or observed facts. The basis is stated.
- **[NF]** Not found. I looked and found nothing. Where I looked is stated.
- **[S]** Secondary. A non-Microsoft source, or a mirror or summary of a Microsoft text. Low authority.

Source ids such as (X4) point to the list in section 5. Every source there has a page date. Anything older than about six months carries a "stale" flag.

**Stale local material.** The `knowledge/m365-copilot`, `knowledge/declarative-agents`, `knowledge/mcp-apps` and `knowledge/work-iq` notes date from May 2026. They describe declarative agent manifest 1.6-1.7 and app manifest 1.19-1.25. Current is declarative agent 1.9 and app manifest 1.30. Do not rely on them for this topic.

**One word, two meanings.** "Plugin" now means an installable package that holds skills, connectors and optional agents. [D X2: "A plugin is an installable package that adds new capabilities"]. The older "API plugin" is now only an action inside a declarative agent. [D X18: "It isn't ... a peer capability in the plugin registry model"]

---

## 1. Route x surface matrix

Cells: Yes / No / Preview / Unknown, then the tag and source. "Unknown [NF]" means no Microsoft developer page names that surface for that route.

| Route | Word | Excel | PowerPoint | Outlook | Teams | Copilot Chat | Cowork |
|---|---|---|---|---|---|---|---|
| A. Plugin skill, instructions only (`agentSkills`) | Yes on Windows desktop [O 11-25]. No developer page [NF] | Unknown [NF] | Preview, rolling out [S X34 MC1556104] | Unknown [NF] | Unknown [NF] | Yes, GA, Premium only [D X4] | Yes [D X5] [O] |
| B. Plugin skill that ships scripts (Python, JS, shell) | No [O 11-26] | Only route E | Unknown [NF] | Unknown [NF] | Unknown [NF] | Yes [D X4] | Yes [O] |
| C. Package-level MCP connector (`agentConnectors`) | Yes on Windows desktop [O 11-26]. No developer page [NF] | Unknown [NF] | Preview, rolling out [S X34 MC1556104] | Unknown [NF] | Unknown [NF] | Yes [D X4] | Yes [D X6] [O] |
| D. Federated Copilot connector (MCP; admin-made or from the gallery) | Yes [D X11] | Yes [D X11] | Yes [D X11] | Yes [D X11] | Not listed [D X11, by absence] | Yes [D X11] | Yes [D X11] |
| E. Office.js script inside a skill, run by Copilot | No documented route [NF]. It ran for us [O 11-27] | Preview [D X23] | No documented route [NF] | No [I] | n/a | No [D X23] | No [O 11-27] |
| F. Declarative agent | Yes [D X15, X16] | Yes [D X16]. Reading the workbook is not documented [D X15, by absence] | Yes [D X15] | Yes [D X16] | Yes [D X16] | Yes [D X16] | Preview, private [S X36] |
| G. API plugin or MCP server as an action inside a declarative agent | Yes, with a known defect [D X2] [S X39] | Same as Word [S X39] | Same as Word [S X39] | Yes [I from row F] | Yes [I from row F] | Yes [D X2, X12] | No [D X2] |
| H. Skills attached to a declarative agent (`agent_skills`) | Unknown [NF] | Unknown [NF] | Unknown [NF] | Unknown [NF] | Unknown [NF] | Yes [D X14] | No [D X14: "Reuse across agents: Not supported"] [I] |
| I. MCP Apps widget from a third-party server | Unknown [NF] | Unknown [NF] | Unknown [NF] | Unknown [NF] | Unknown [NF] | Yes, inside a declarative agent [D X12] | Yes [D X32] |
| J. SPFx Copilot UX component | Not listed [D X13, by absence] | Not listed | Not listed | Not listed | Yes, Copilot in Teams [D X13] | Yes [D X13] | No [D X13] |
| K. Custom engine agent (Agents SDK, Teams SDK, Foundry) | Yes [D X16] | Yes [D X16] | Unknown [NF] | Unknown [NF] | Yes [D X16, X17] | Yes [D X17] | Unknown [NF] |
| L. Copilot Studio agent | Unknown [NF] | Unknown [NF] | Unknown [NF] | Unknown [NF] | Yes [D X17] | Yes [D X17] | Unknown [NF] |
| M. Office add-in command as an agent action (`LocalPlugin`) | No. Cancelled June 2026 [D X21] | No. Cancelled [D X21] | No. Cancelled [D X21] | No. Cancelled [D X21] | n/a | n/a | n/a |
| N. A third-party hook into the app's own agentic editing | No documented route [NF] | Preview, through route E [D X23] | No documented route [NF] | n/a | n/a | n/a | n/a |

**Desktop, web and Mac. What is known.**

| Route | Windows desktop | Web | Mac | Mobile |
|---|---|---|---|---|
| A and C in Word | Runs [O 11-25, 11-26] | Lists the plugin, does not run its skills [O 11-01] | Unknown [NF] | Unknown [NF] |
| A and C in PowerPoint | Announced [S X34] | Announced [S X34] | Announced [S X34] | Unknown [NF] |
| D federated connector | No client split is stated anywhere on the page [NF X11] | same | same | same |
| E Excel Office.js skill | Version 2608 or later, Beta and Current Channel (Preview) only [D X23] | Not listed [D X23, by absence] | 16.112.26070718 or later [D X23 tutorial] | No [I] |
| F declarative agent reading the open document | Not split by client on the page [NF X15] | same | same | Unknown |
| I MCP Apps | Yes [I: same host as web] | Yes, tested at m365.cloud.microsoft/chat [D X12] | Unknown [NF] | Unknown [NF] |
| J SPFx UX components | Yes [D X13] | Yes [D X13] | Unknown [NF] | No at GA [D X13] |
| Cowork plugins | n/a | Yes | n/a | Custom skills and plugins not supported (see part 2) |

**Matrix highlights**

1. Only one route is documented by name for Copilot inside Word, Excel, PowerPoint and Outlook: the federated connector. [D X11: "Copilot in Word, Excel, PowerPoint, and Outlook"]. It carries tools only. It carries no instructions and no UI.
2. Plugin skills and plugin connectors do run in Word desktop for us. No Microsoft developer page says they should. The skills page lists three targets: Copilot Chat, Cowork and declarative agents. It has no row for any Office app. [D X3, by absence] [O]
3. Microsoft warns, on almost every page, against carrying a result from one host to another. [D X5: "Don't assume that passing checks in one product establishes support in another."]
4. No route lets a third party edit the open Word document as tracked changes through a documented contract. The declarative agent can read it as plain text and insert an image. [D X15]. Our working path in Word (skill text that Copilot's own code tool runs) is observed, not documented. [O] [NF]
5. Excel is the only app with a documented way for third-party Office.js to run under Copilot. It is preview and limited to Insider channels. [D X23]
6. The Office add-in route into Copilot was cancelled in June 2026. [D X21]
7. Rich UI from a third party is documented only in Copilot Chat and Cowork (MCP Apps) and in Copilot Chat and Copilot in Teams (SPFx). Nothing is documented for the Office app panes. [D X12, X13] [NF]

---

## 2. Route table

"Edit open doc" means change the document that is open in the app. "Return files" means hand a new file back to the user.

| Route | What it is | Status | Licence | Identity | Edit open doc | Return files | Render UI | Distribution |
|---|---|---|---|---|---|---|---|---|
| A. Plugin skill, instructions only | A folder with `SKILL.md`, listed in `agentSkills` in app manifest 1.28-1.30 | GA in Copilot Chat [D X1, X4]. Cowork GA since June [D X32]. Office apps: no status given [NF] | Chat: Microsoft 365 Copilot (Premium) [D X4]. Cowork: Premium plus a spending policy [D X27] | None of its own. It runs inside the user's Copilot session | Only as far as the host's own tools go. In Word desktop the model followed our skill and edited [O] | Only what the host can make | No | Personal upload, share, org catalog, Marketplace [D X26] |
| B. Plugin skill with scripts | Same, plus scripts the host's sandbox runs | GA in Chat [D X4]. Cowork yes [O]. Word does not load it [O] | As A | None. [D X3: "Don't assume a skill script can access the network, install packages, or call authenticated services."] | No. The sandbox cannot reach the open document [I from no network and no Office.js] | Yes in Chat and Cowork. The sandbox has python-docx, openpyxl, python-pptx and more [D X14] | No | As A |
| C. Package-level MCP connector | `agentConnectors` entry that points at a remote MCP server | Documented for Cowork and for Chat skill plugins [D X6, X4]. Schema GA at 1.29-1.30. Some auth types "require host support" [D X6] | As the host | User signs in through the host: static OAuth, API key, dynamic client registration, Azure Key Vault, or `ConnectionVault` (developer preview) [D X6] | No. It returns data. The host decides what to do with it | No file channel is documented. Text and structured results only [I from X6, X12] | Yes where the host renders MCP Apps (row I) | Inside the plugin package |
| D. Federated Copilot connector | The same MCP server, registered by an admin or picked from the Connectors Gallery | Documented, with host list [D X11]. Admin "Tools" view is "Coming soon" [D X11] | [D X11: "The user has a Microsoft 365 Copilot license."] | [D X11: "the user's identity and permissions"]. No indexing | No | No file channel documented | Not stated [NF] | Admin center, or the gallery. One manifest and pipeline for ISVs [D X11] |
| E. Office.js script in a skill | A `.js` file in the skill that calls Office.js. Copilot creates the runtime and runs it | Preview, Excel only [D X23: "Don't use them in a production Copilot extension."] | Copilot in Excel, Insider channel [D X23] | The open workbook and the signed-in user. No add-in manifest needed [D X23] | Yes, in Excel [D X23]. In Word only by observation [O] | No | No | Custom Excel skills are stored in OneDrive, not in the plugin registry [D X3] |
| F. Declarative agent | Instructions, knowledge, actions and skills on top of Copilot's orchestrator. Manifest 1.9 | GA. 1.9 shipped October 2026 [D X1] | Runs for Copilot Chat users. Some knowledge sources need a licence or usage billing [D X27] | The user's own. Actions use OAuth 2.1 or Entra SSO [D X12] | No. Reads the whole document as plain text. Tables and styles are lost. Selection is Word only. The only write is `Action.InsertImage` [D X15] | Yes in Chat, through code interpreter or its skills (part 2) | Adaptive Cards. MCP Apps widgets [D X12] | Share, org catalog, Marketplace. The best documented store path [D X3, X26] |
| G. API plugin or MCP action in an agent | OpenAPI or `RemoteMCPServer` runtime in plugin manifest 2.4 | GA [D X19] | As F | `None`, `OAuthPluginVault`, `ApiKeyPluginVault` [D X19]. Entra SSO [D X12] | No | No file channel. `uploadFile` and `getFileDownloadUrl` are unsupported [D X12] | Adaptive Cards and MCP Apps [D X12] | Travels with the agent. [D X2: "don't become independently available capabilities of the package"] |
| H. Skills in a declarative agent | Up to 8 skills listed in `agent_skills` | Documented. Tooling flag is off by default in WIQD [D X14] | As F | None for scripts. The agent calls connectors, the script cannot [D X14] | No | Yes [D X14] | No | With the agent |
| I. MCP Apps widget | An HTML widget a tool returns, shown in a sandboxed frame | GA in Chat since March 2026 [D X1]. Cowork since June [D X32] | Included for Copilot Chat users [D X27] | Host attaches the auth. OAuth 2.1 or Entra SSO [D X12] | No | No. File upload and download APIs are unsupported [D X12] | Yes. Full screen is the only display mode the widget can request [D X12] | With the agent or connector |
| J. SPFx Copilot UX component | A SharePoint Framework component rendered by Copilot | GA with SPFx 1.24 on 2026-10-28 [D X13] | Microsoft 365 Copilot add-on licence. No consumption charge [D X13] | SPFx user context [I from the framework] | No | Not documented [NF] | Yes. 10 KB context cap [D X13] | `.sppkg` through the tenant App Catalog. SharePoint Store [D X13] |
| K. Custom engine agent | Your own model and orchestrator behind a Copilot or Teams front end | Listed as a current build option with no preview label [D X17]. Status of each SDK not re-checked this pass | Works for all three licence columns in Microsoft's table [D X27] | Your service handles it. Prompts and replies in Chat and Teams are stored under Microsoft 365 terms [D X17] | Nothing documented [NF X17] | Whatever your service returns in chat | Adaptive Cards [I]. MCP Apps tooling does not target it [D X12: "WIQD doesn't target custom engine agents."] | Org or Marketplace. Store needs Teams SDK, Agents SDK or Foundry [D X17] |
| L. Copilot Studio agent | Low-code agent. Three "harnesses" as of November 2026 | GA product. Harness page dated 11-13 [D X31] | Copilot Credits [D X27, X31] | Maker-configured connections | Not in the open document. The Copilot harness makes and edits files on its own [D X31: "natively creates and edits Word, Excel, PowerPoint, and PDF files"] | Yes, with the Copilot harness [D X31] | Adaptive Cards | Agent Registry, not the plugin registry [D X5]. Store: custom engine agents only [D X30] |
| M. Office add-in as agent action | Add-in function exposed to an agent through `LocalPlugin` | Cancelled 2026-06-17 after a year in preview [D X21] | n/a | n/a | It could. It is gone | n/a | n/a | Store rule now forbids it [D X30] |
| O. Work IQ and Copilot APIs (outside-in) | Call Microsoft's retrieval and chat from your own app over REST, A2A or MCP | Work IQ GA in Foundry and Copilot Studio [S X37]. Copilot APIs at no extra cost with a licence [D X27] | Copilot licence for Copilot APIs. Work IQ API can use Copilot Credits [D X27] | Delegated. New `WorkIQ.Read` and `WorkIQ.Act` permissions [S X37] | No. Office file create and edit is "on the way" [S X37] | Not yet | Your app's UI | Your app |

---

## 3. Findings per question

### Q1. The routes, one by one

**Plugins with `agentSkills`**

- The plugin is now Microsoft's packaging unit. One zip holds skills, connectors and optional agents. [D X2]
- Agents inside a plugin are declarative agents only, for now. [D X2: "today, these are declarative agents"]
- Skills in Copilot Chat reached general availability in November 2026. [D X1: "Skills in Microsoft 365 Copilot Chat are generally available."]
- They are for paying users only. [D X4: "currently available only to users with Microsoft Copilot (Premium)"]. Tenants with Information Barriers are not supported. [D X4]
- The skills page names three targets: Copilot Chat, Cowork, declarative agent. No Office app is named. [D X3]
- Microsoft says a shared file format proves nothing about reach. [D X3: "A shared file format doesn't establish cross-agent reuse or cross-host support."]
- The admin guide does confirm that skills exist in the three Office apps, with different storage. [D X24: "Skills used in Word, Excel, and PowerPoint are stored in OneDrive today."]
- One Plugins button replaces the Agents, Skills and Customize buttons across Chat, Cowork, Code, Autopilot, Word, PowerPoint, Excel and SharePoint. The change runs from late October to late November 2026. Outlook and Teams are not named. [S X34 MC1484008]
- PowerPoint is getting plugins that bring skills and connectors, on Windows, Mac and web. Frontier first in mid November, then general availability. The umbrella message says late November. The per-platform messages were reported as mid November to early December. [S X34 MC1556104, MC1556090, MC1556095]. I record both dates and do not pick one.
- No matching "plugins in Word" or "plugins in Excel" message was found. The only Word item is Microsoft's own Legal plugin. [S X34 MC1509799] [NF: Message Center mirror, what's-new page, Book of News]
- Our tests fill the gap for Word. Word desktop loads an instructions-only plugin skill and calls the plugin's MCP tools. It does not load a skill that ships Python. Word for the web lists the plugin and does not run it. [O 11-01, 11-25, 11-26]
- So for Word: the route works, on one client, with no published contract. [I from the two points above]

**Declarative agents**

- Manifest 1.9 shipped in October 2026. It added agent skills and a Fabric knowledge source. [D X1]
- They run in Copilot and in the apps. [D X16: "Run in Microsoft 365 Copilot and Microsoft 365 apps like Teams, Word, Excel, and Outlook."]
- In Word and PowerPoint they read the open document. [D X15: "By default, agents can read the entire open document."]
- Word gives plain text only. [D X15: "tables, charts, images, page formatting, and styling aren't captured"]
- Selection is Word only. [D X15: "Accessing the document selection is only supported in Word."]
- The only write is an image insert from an Adaptive Card. [D X15]. This matches our test: the agent reviewed, it could not edit. [O]
- That page now carries a new warning. Document access is [D X15] "not support for the full plugin package in Word or PowerPoint".
- Limits: 8 skills per agent, no reuse of a skill across agents, 10 MB package through Agents Toolkit. [D X14]
- They cannot start a conversation themselves. [D X16: proactive interactions "Not supported"]
- A declarative agent with an MCP action answers twice in Word, Excel and PowerPoint. The issue was open on 2026-10-22. Not re-checked. [S X39]
- Cowork calling a declarative agent is private preview under Frontier. [S X36, summary of the Book of News]
- The standalone Word, Excel and PowerPoint agents in the Copilot app retire between mid November and late December 2026. [S X34 MC1536062]

**API plugins and remote MCP servers as actions**

- There are now three ways to register the same MCP server. They are not the same thing. [D X9]
  1. An action inside a declarative agent (plugin manifest 2.4, runtime `RemoteMCPServer`). [D X19]
  2. A package-level connector (`agentConnectors`, app manifest 1.29-1.30). [D X6]
  3. A federated Copilot connector, made by an admin or published to the gallery. [D X11]
- An action inside an agent stays inside that agent. [D X2: "don't become independently available capabilities of the package"]
- Supporting MCP is not the same as supporting a registration model. [D X6: "Support for MCP alone doesn't establish support for this registration model."]
- Schema values are not promises. [D X6: "A schema value doesn't guarantee support in the target experience."]
- Server requirements for the package-level route: HTTPS with TLS 1.2 or later, Streamable HTTP, `tools/list` and `tools/call`, and fast tools. [D X7: "Tool calls that complete in less than 30 seconds"]
- Cowork still needs a tool description file even though manifest 1.29 made it optional. [D X6: "Copilot Cowork still requires a tool description file"]
- API-key auth is not yet available in Cowork. [D X8]
- A fourth path exists for other clients. The Agent 365 CLI "bring your own MCP" route is preview. It does not serve declarative agents. [D X7: "Microsoft 365 declarative agents aren't supported by this route."]
- The federated connector is the widest route. It reaches Chat, agents, Cowork, the four Office apps and Researcher (read only). [D X11]
- Write tools ask the user first. Read tools never ask. [D X11]
- Admin deployment settings for agents do not apply to it. [D X11: "Installed for and agent sharing settings don't apply to federated connectors."]
- The gallery's legal category already lists Harvey, Legora, iManage Work, NetDocuments, Litera, Relativity, Everlaw, DeepJudge, Juro, Thomson Reuters HighQ, Bloomberg Law and others. [D X11]. Legal vendors have chosen this route.

**Office add-in actions and agents (facts only)**

- The feature that let a declarative agent call an Office add-in function was cancelled. [D X21: "So, this feature is being cancelled."]. The docs pull request merged on 2026-06-17.
- Microsoft's stated reason: [D X21] "better ways for external developers to integrate the Office JavaScript Library APIs into custom agents".
- The three articles now redirect to the add-ins overview. [D X21, redirection file]
- The add-ins overview still has two preview lines about Copilot agents and natural-language instructions. Its only linked sample is the Excel skill. The page is dated 2026-09-17. [D X20]. Treat those two lines as leftover text. [I from X21 and the missing articles]
- Plugin manifest 2.4 still defines the `LocalPlugin` runtime. [D X19]. The schema lags the cancellation. [I]
- The store rules forbid it. [D X30: "WXP add-ins should not be used as actions in the agent manifest."]
- Add-ins themselves are unchanged and still supported. Nothing found says otherwise. [NF: add-ins overview, what's-new, Book of News]

**The replacement: Office.js inside a skill**

- One week after the cancellation Microsoft added Excel Copilot skills. [D X22: "This is a new way to use Office.js: in a custom Copilot skill, not an add-in."]
- Copilot does the hosting. [D X23: "Copilot in Excel automatically creates a runtime, loads Office.js and executes the scripts in your skill"]
- It is preview, needs version 2608 or later, and only Beta and Current Channel (Preview). [D X23]
- It does not run in Copilot Chat. [D X23 sample: "The skill does *not* run in standalone Microsoft 365 Copilot"]
- These skills live in OneDrive, not in the plugin registry. [D X3: "Excel custom skills are stored in OneDrive, not in the plugin registry."]
- No Word or PowerPoint page of this kind exists. [NF: the `docs/word`, `docs/powerpoint`, `docs/outlook` and `docs/design` folders of the Office docs repository, Learn search, Message Center, Book of News]
- Our Word result fits this direction. Word ran Office.js that a skill supplied, through its built-in code tool. [O 11-27]. Cowork did not. [O 11-27]. This is where Microsoft is pointing for Excel. For Word it is undocumented and can change without notice. [I]

**Copilot UX components and MCP Apps**

- MCP Apps widgets work in declarative agents since March 2026 and in Cowork since June 2026. [D X1, X32]
- The page says they [D X12] "run inside Microsoft 365 Copilot". It tests in Copilot Chat on the web. It says nothing about the Word, Excel, PowerPoint, Outlook or Teams panes. [NF X12]
- Host support is partial. [D X12: "Not all window.openai.* APIs are available on every platform or host."]
- Unsupported: file upload, file download URL, modal, frame domains, the destructive hint, and partial tool input. Full screen is the only display mode a widget can ask for. [D X12]
- SPFx Copilot UX components went GA on 2026-10-28. They cover Copilot and Copilot in Teams, browser and desktop. [D X13]
- Not covered at GA: mobile, Cowork and standard plugin packaging. [D X13: "Cowork, and other additional surfaces aren't GA capabilities"]
- They need the paid Copilot licence and deploy as a SharePoint package. [D X13]
- An Adaptive Card can open a dialog that hosts a web page (`Action.OpenUrlDialog`). That page was dated 2026-03-06 when last read. **Stale. Not re-checked.** [D, from our 2026-11-05 notes]

**Agents SDK, custom engine agents, Copilot Studio**

- Custom engine agents are built with [D X17] "Copilot Studio, the Microsoft 365 Agents SDK, Teams SDK or Microsoft Foundry".
- They can run in [D X16] "Microsoft 365 Copilot and Microsoft 365 apps like Teams, Word, Excel, and Edge".
- Nothing says they can read or edit the open document. [NF X16, X17]
- Microsoft treats them as a separate path: build Copilot into your own app. [D X18]
- Microsoft's own example for the Foundry option is legal case analysis. [D X17]
- Copilot Studio now has three harnesses. The Microsoft Copilot harness has skills, memory and a sandbox, and bills in Copilot Credits. [D X31]
- Agent Builder cannot add skills. [D X5: "Agent Builder in Copilot Studio doesn't currently support adding skills."]
- Copilot Studio agents sit in a different registry. [D X5: "Standalone Copilot Studio agents appear in the Agent Registry, not under plugins."]
- Whether a Copilot Studio agent appears in the Office app panes was not found. [NF X5, X17, X31]

**A third-party route into Agent Mode**

- None is documented for Word or PowerPoint. [NF: everything listed above, plus the Ignite Book of News and the Ignite plugins blog]
- Excel has one, in preview: route E. [D X23]
- What works in Word today is indirect. A skill tells Copilot what to do. Copilot's own tools do the editing. [O]. Part 1 owns the detail.

### Q2. Identity and data

**Identity**

- Skills have no identity of their own. Scripts cannot call signed-in services. [D X3, X4, X14]. In a declarative agent the orchestrator calls the connector, not the script. [D X14: "The agent can use enabled capabilities through the orchestrator"]
- Package-level MCP connectors take one of six auth types: `None`, `OAuthPluginVault`, `ApiKeyPluginVault`, `DynamicClientRegistration`, `AzureKeyVault`, `ConnectionVault` (developer preview). [D X6]
- Entra cannot do dynamic client registration. [D X6: "Microsoft Entra ID doesn't provide an RFC 7591 registration endpoint; use static OAuth registration"]. The redirect address is `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`. [D X6]
- Actions inside a declarative agent support OAuth 2.1 and Entra single sign-on. [D X12]
- Federated connectors act as the user. [D X11: "the user's identity and permissions"]. MCP traffic comes from static Microsoft egress addresses. [D X11]
- Custom engine agents handle identity in your own service. [I from X17, X18]
- A plugin never widens access. [D X28: "A plugin doesn't grant a user access to data that the user ... isn't otherwise authorized to access."]. The service behind it must enforce its own permissions. [D X28]

**File context. What each route sees.**

| Route | What it gets about the user's file |
|---|---|
| Skill in Word (A) | Whatever Word's Copilot has loaded. In our tests, the open document [O] |
| Skill in Chat or Cowork (A, B) | Files the user attaches or points to. Scripts read them in the sandbox (part 2) |
| MCP connector (C, D, G) | Only what the model puts in the tool arguments. No documented channel hands the server the file or its identity [NF X6, X11, X12] |
| Declarative agent in Word or PowerPoint (F) | The whole document as plain text. Selection in Word [D X15] |
| Excel Office.js skill (E) | The live workbook through Office.js [D X23] |
| MCP App widget (I) | Tool results up to the host's limits. No file upload or download API [D X12] |
| Custom engine agent (K) | Not documented [NF] |

- Cross-part fact that changes this answer: our files live in SharePoint Embedded. Cowork cannot read them natively (note of 2026-11-26). So every route that needs the file body must either get it from the open document or have our server fetch it by an id the model passes. [I from the table above and that note]

**What each route can return**

- Text: all routes.
- Adaptive Cards: API plugin actions and custom engine agents. Confirmation cards are `None` or `AdaptiveCard`. [D X19]
- Widgets: MCP Apps in Chat and Cowork, SPFx in Chat and Teams. [D X12, X13]
- New files: skills with scripts in Chat and Cowork, declarative agent skills, the Copilot Studio Copilot harness. [D X14, X31]
- Edits to the open document: Excel Office.js skills [D X23]; image insert from a declarative agent [D X15]; the host's own agent acting on a skill's instructions in Word [O].
- No MCP route returns a file to the user through a documented contract. [NF X6, X11, X12]

### Q3. Distribution, governance and commercial terms

**Distribution**

- Three routes: share for testing, publish to the organization, publish publicly. [D X26]
- Public means Partner Center, the "Microsoft 365 and Copilot" program and a verified publisher. [D X26]
- Microsoft keeps the last word. [D X26: "Microsoft controls certification and live publication."]
- Publication is not installation. [D X26: "Marketplace publication doesn't make the plugin available in every customer organization."]. The customer's admin must allow the publisher category and choose the users. [D X25, X26]
- Not everything you can package can be published. [D X26: "A capability that you can package isn't automatically eligible for public distribution."]
- The documented store path for skills is a declarative agent that contains them. That [D X3] "doesn't establish eligibility for every standalone skill or composed plugin package".
- A public Copilot Plugin Catalog launched on 17 November. [S X35]. The page returned only its title to my fetch. Per-plugin host support could not be read. [NF X38]
- Five names, five different things: Copilot Plugin Catalog, plugin registry, Tools Registry, Agent Registry, Microsoft Marketplace. [D X2: "aren't interchangeable names for one catalog"]
- Review time is not stated on the November pages. [NF X26]. Our October note recorded four to six weeks from a Partner Center page. Not re-checked.
- Transactable offers or paid listings for plugins: not found. [NF X26, X27]

**Store rules that matter to us** (page dated 2026-08-13, updated 2026-10-23) [D X30]

- The rules cover ISVs that publish agents and Cowork plugins.
- The package must add value beyond what Copilot already does.
- Must-fix clients include [D X30] "Microsoft 365 Copilot in Word", Teams desktop and web, and Copilot on the web. An ISV agent is tested in Word.
- Response time targets: 9 seconds at the 99th percentile, 5 at the 75th, 2 at the median. Availability 99.9 percent.
- Some agent capabilities are not allowed in store agents. [D X30: "restricted to be used in the LOB scenario only"]. The list is Dataverse, file embedding, sensitivity label and scenario models. This bars the Dataverse knowledge source in a store agent. It does not bar a Dataverse-backed service behind an MCP server. [I]
- From Copilot Studio only custom engine agents are eligible. [D X30: "Declarative Agents aren't supported"]
- Server calls must use HTTPS and stay on the publisher's verified domain. [D X30, summarised]

**MCP server certification**

- It is preview and capacity-limited. [D X10: "only a limited number of submissions can be processed"]
- It needs a verified publisher in the Microsoft 365 and Copilot program, and a package with manifest 1.30, a tool file and an intro file. [D X10]
- Authentication runs through the publisher's Azure Key Vault. The vault holds `ClientId`, `ClientSecret` and `TokenUrl`. [D X10]
- [D X10: "PKCE isn't supported today"]
- **Flag, not a decision:** this needs a client secret. Our auth direction is to remove client secrets. The owner must weigh this before any certification work.
- Certified servers are expected to show up in Copilot Studio and Foundry. [D X10]
- An existing store agent with an MCP server need not be resubmitted. [D X10]
- Documented conflict: one page says the legacy process ends on 18 November 2026 [D X9], another says the end of 2026 [D X10]. Not resolved.
- Certification is not reach. [D X9: "Publishing a plugin or certifying its MCP server doesn't establish support in every host."]

**Admin governance**

- Controls sit in the Microsoft 365 admin center under Agents > Tools > Registry. [D X25]
- Plugins and MCP servers are governed apart. [D X25: "Plugin governance and MCP governance are separate."]
- Admins choose which publisher categories users may install: Microsoft, the organization, certified external publishers. [D X25]
- A single skill cannot be blocked. [D X24: "You can't block an individual skill inside a package today."]
- Approving an MCP request needs an AI Administrator or Global Administrator and tenant-wide consent. [D X25]
- Per-tool control is rolling out and works only through the Agent 365 Tooling Gateway. [D X25]
- Plugins are aimed at Copilot itself. [D X25: "Plugins are primarily available to end users in the Copilot channel"]
- Ignite stated that plugin and skill governance is generally available. [S X36: "Plugins and skills governance is generally available."]

**Metering and billing**

- Cowork is usage-billed. [D X27: "uses usage-based billing. Access comes only from a spending policy"]
- Cowork, Copilot Studio agents and the Work IQ API draw on one tenant credit pool. [D X27]
- Copilot APIs cost nothing extra for licensed users. [D X27]
- Skills in Chat and federated connectors need the paid licence. [D X4, X11]
- Declarative agents with custom actions, code interpreter and MCP Apps work for unlicensed Copilot Chat users. Connectors, SharePoint, embedded files and Dataverse knowledge need a licence or usage billing. [D X27]
- Usage billing is not available in GCC and GCC High. [D X27]
- SPFx components carry no consumption charge. [D X13]
- No route meters the ISV. The customer pays Microsoft. How the ISV charges is its own matter. [I from X26, X27 and the missing monetisation text]

**Purview and audit**

- One audit record type covers Copilot and Cowork: `CopilotInteraction`. [D X29, page dated 2026-08-26]
- It records the host app (`AppHost`: Word, Excel, PowerPoint, Outlook, Teams, BizChat), the agent id and name, the plugins used (`AISystemPlugin`) and the resources accessed. [D X29]
- Custom engine agent prompts in Chat and Teams are stored and searchable in Purview. [D X17]
- Federated connector activity is auditable. [D X11: "Admins can audit activity in Microsoft Purview."]
- Whether the record holds MCP tool arguments or skill script runs: not found. [NF X29]
- No per-route audit matrix exists. [NF X28]
- The service behind a connector keeps its own compliance duty. [D X28]

### Q4. Direction

**What Microsoft said and shipped, May to November 2026**

- May: declarative agent 1.7. July: 1.8. October: 1.9, plus a package API filter for plugins, MCP servers and skills, and a cost API. November: skills in Chat GA. [D X1]
- No Build 2026 entry names a new Office-app extension route. [NF X1]. Build-specific posts were not searched beyond this page.
- June: Office add-in agent actions cancelled. Excel Office.js skills started. [D X21, X22]
- June: Cowork GA with MCP Apps and zip upload that converts Claude packages. [D X32]
- October: Copilot for Legal GA, as a Word desktop plugin plus a Cowork plugin. [S X34 MC1509799]
- Ignite, November: plugins are the packaging unit; a public Plugin Catalog; role plugins for sales, service, finance and legal. [S X35]
- Ignite: Office in Copilot GA; Copilot Code and Autopilot in Frontier; MCP governance widening; an Entra MCP server; an MCP firewall in preview. [S X36]
- Ignite: Work IQ GA in Foundry and Copilot Studio over MCP, REST and A2A. Office file create and edit through Work IQ is "on the way". [S X37]
- Nothing at Ignite opened Agent Mode, Edit with Copilot, the Office panes' UI, or Office add-ins to third parties. [NF X35, X36]. I read summaries of both texts, not every word.
- A third-party count says 76 of 215 Ignite features are available today. [S X40, low authority]

**Deprecations and consolidation**

| What | State | Source |
|---|---|---|
| Office add-in functions as agent actions | Cancelled 2026-06-17 | [D X21] |
| Standalone Word, Excel, PowerPoint agents in the Copilot app | Retire mid November to late December 2026 | [S X34 MC1536062] |
| Agents, Skills and Customize buttons | Replaced by one Plugins button | [S X34 MC1484008] |
| Agents Toolkit as the default tool | Kept for existing projects. Work IQ Dev Tools is the default | [D X5] |
| Legacy MCP certification | Ends 18 November 2026 or end of 2026 (two pages disagree) | [D X9, X10] |
| SharePoint `.agent` files | No new ones from mid November 2026. Stop working after March 2027 | our October notes, not re-checked |
| Copilot Tuning | Paused August 2026, moving to Copilot Studio | our October notes, not re-checked |
| Microsoft's Legal Agent | Retired in favour of Copilot for Legal | [S X34 MC1509799] |

**Microsoft's stated ISV path**

1. Plan by host. Decide which experiences you target and test each one. [D X5]
2. Build a plugin package: skills, plus connectors to your MCP server, plus an optional declarative agent. [D X2, X26]
3. Use Work IQ Dev Tools. [D X5: "Work IQ Dev Tools is the default professional-developer path for supported plugin scenarios."]
4. Validate the exact zip you will submit. [D X26]
5. Publish through Partner Center to Microsoft Marketplace. [D X26]
6. The customer's admin allows, installs and scopes it. [D X26: "Published doesn't mean installed."]
7. For connectors alone: one manifest and one pipeline to the Connectors Gallery. [D X11: "single connector manifest and single publishing pipeline for all connector types"]
8. If you run your own model and orchestrator, that is the separate custom engine agent path. [D X18]

- The caveat Microsoft attaches: [D X26] "Each Microsoft experience activates only the capabilities it supports".
- The tooling imports Claude and Cursor plugin sources. Conversion is not support. [D X5: "Format conversion doesn't establish equivalent runtime behavior or host support"]
- Documented conflict on tool maturity. One page calls Work IQ Dev Tools generally available. [D X27]. The federated connector page carries a banner calling it experimental and not production-ready. [D X11]. Not resolved.

**What this adds up to** [I, from the facts above]

- Microsoft is moving third-party logic out of add-in code and into two shapes: instructions (skills) and remote tools (MCP). Copilot's own agent does the document editing.
- The open-document editing surface stays Microsoft's. Third parties steer it. They do not plug into it.
- Reach is converging on the plugin, but it is arriving app by app. Chat and Cowork are documented. PowerPoint is rolling out. Word works without a contract. Excel has its own preview route. Outlook and Teams are silent.
- The federated connector is the one piece with a documented, wide host list today.

### How this reconciles with our own evidence

| Our finding | Microsoft's documents | Reading |
|---|---|---|
| Word desktop loads instructions-only plugin skills | No developer page. Admin guide confirms skills exist in Word [D X24] | Works, no contract |
| Word desktop calls MCP connector tools | Federated connectors are documented for Word [D X11]. Package-level connectors are not | The documented form of what we do is the federated connector |
| Word does not load skills with Python | Scripts are documented for Chat, Cowork and agents only [D X3, X4] | Consistent |
| Word ran skill-supplied Office.js | Documented for Excel preview only [D X23] | Matches direction, undocumented host |
| Word declarative agent reviews but cannot edit | Exactly as documented [D X15] | Consistent |
| Word for the web lists but does not run | No client split documented | Unknown. Test again after the Plugins button rollout ends |

---

## 4. Tests

Each test names what to do, what to record and what the result decides. Run with one licensed user unless stated.

**XR-1. Same plugin, every Office host and client.**
Do: install one plugin with one instructions-only skill and one `agentConnectors` server. Open Word, Excel and PowerPoint on Windows, Mac and web.
Record, per cell: plugin listed; skill offered; skill followed; MCP tool called.
Decides: whether "one package, all Office apps" is real, and on which clients. Re-run Word web, which failed on 2026-11-01.

**XR-2. Federated connector against package connector.**
Do: register the same MCP server twice. Once as an admin-made federated connector. Once inside a plugin. Test each alone in Word and Outlook. Repeat with a user who has no Copilot licence.
Record: tools visible; tool called; approval prompt on a write tool; behaviour without a licence.
Decides: which registration to lead with for the Office apps and Outlook.

**XR-3. Widgets outside Chat.**
Do: call a tool that returns an MCP Apps widget from the Word pane, the Outlook pane, Copilot in Teams, Copilot Chat and Cowork.
Record: widget rendered, text fallback, or error. Note the display size.
Decides: whether any UI can be planned for the Office panes.

**XR-4. Office.js skill, with a control.**
Do: on an Insider build, install Microsoft's Excel skill sample and confirm it runs. Then make the same package shape with a Word script and install it the same way.
Record: whether Word's loader accepts a skill with `scripts/*.js`; whether the script runs as written.
Decides: whether Word has the Excel route switched on quietly, or only the looser path we saw in WP-5.

**XR-5. Declarative agent with an MCP action in Word.**
Do: open a document, pick the agent, ask for a review that calls the tool. Then ask Word's own agent to apply the findings as tracked changes.
Record: single or double answer (the open defect); whether the hand-off to Word's agent works in one conversation.
Decides: whether a declarative agent has any role in Word next to the plugin.

**XR-6. Store preflight.**
Do: run the Partner Center preflight in Work IQ Dev Tools on our skills-plus-connector package. Do not submit.
Record: every eligibility error and warning.
Decides: whether a plugin without a declarative agent can go to the store today, or whether an agent wrapper is required.

**XR-7. Audit trail.**
Do: in Word, run one skill that calls one MCP tool. Wait a day. Export the `CopilotInteraction` records from Purview.
Record: `AppHost`, `AISystemPlugin`, `AccessedResources`; whether tool names or arguments appear.
Decides: what we can tell a law firm's compliance team about traceability, and what our own server must log.

**XR-8. Certification fit.**
Do: read the intake form and map our server's Entra sign-in to the required Key Vault secrets. Do not create a secret.
Record: whether a certificate or federated credential is accepted in place of `ClientSecret`; whether the lack of PKCE blocks our flow.
Decides: whether certification is compatible with our no-secret auth direction. Needs an owner decision either way.

**XR-9. Agents in the Office panes.**
Do: publish one minimal custom engine agent and one Copilot Studio agent to the tenant. Look for each in the Word, Excel, PowerPoint and Outlook panes.
Record: listed or not; whether it receives any document context.
Decides: whether rows K and L of the matrix are "Yes" in practice. Low priority unless we consider our own orchestrator in the pane.

**XR-10. Plugin Catalog reading.**
Do: open the public Copilot Plugin Catalog in a browser. Open two legal vendors' entries.
Record: what each entry declares as supported experiences, and whether it is a plugin, a connector or an agent.
Decides: how peers package, and whether the catalog states host support.

---

## 5. Sources

All checked 2026-11-27. Dates are the page's `ms.date` and, where shown, its last update.

| Id | URL | Page date | Note |
|---|---|---|---|
| X1 | https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/whats-new | 2026-11-13 / 11-18 | Change log. Most authoritative for dates |
| X2 | https://learn.microsoft.com/en-us/plugins/overview | 2026-11-16 | Plugin model and the five catalog terms |
| X3 | https://learn.microsoft.com/en-us/plugins/plugin-type-skills | 2026-11-11 / 11-16 | Skill targets. No Office app row |
| X4 | https://learn.microsoft.com/en-us/plugins/build-copilot-chat-skills | 2026-11-19 | Skills in Chat |
| X5 | https://learn.microsoft.com/en-us/plugins/planning-guide , /choose-plugin-components , /choose-plugin-development-tools | 2026-11-16 | Host-by-host warnings; default tool |
| X6 | https://learn.microsoft.com/en-us/plugins/configure-plugin-mcp-servers | 2026-10-28 / 11-16 | `agentConnectors` and auth types |
| X7 | https://learn.microsoft.com/en-us/plugins/build-reuse-mcp-servers | 2026-11-16 | Server requirements; Agent 365 CLI route |
| X8 | https://microsoft.github.io/wiqd/getting-started/plugin-reference/ | undated; limits verified 2026-10-14 | Tool reference. No host matrix |
| X9 | https://learn.microsoft.com/en-us/plugins/plugin-type-mcp-servers | 2026-11-16 | Three integration paths |
| X10 | https://learn.microsoft.com/en-us/microsoft-copilot-studio/mcp-certification | 2026-11-23 | Certification (preview) |
| X11 | https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/federated-connectors-overview | 2026-11-16 | **The only page that names Word, Excel, PowerPoint and Outlook** |
| X12 | https://learn.microsoft.com/en-us/plugins/plugin-mcp-apps | 2026-11-09 / 11-16 | MCP Apps support table |
| X13 | https://learn.microsoft.com/en-us/sharepoint/dev/spfx/copilot/overview-copilot-apps | 2026-10-28 | SPFx Copilot UX components |
| X14 | https://learn.microsoft.com/en-us/plugins/declarative-agent-skills | 2026-11-18 / 11-19 | Agent skills, sandbox, package list |
| X15 | https://learn.microsoft.com/en-us/plugins/declarative-agent-document-interaction | 2026-11-16 | What an agent can do with the open document |
| X16 | https://learn.microsoft.com/en-us/plugins/agents-overview | 2026-11-16 | Where each agent type runs |
| X17 | https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview-custom-engine-agent | 2026-11-09 / 11-16 | Custom engine agents |
| X18 | https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/apps-agents-overview | 2026-11-16 | Two paths: extend Copilot, or build it into your app |
| X19 | https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-manifest-2.4 | 2026-11-16 | Runtime and auth types; `LocalPlugin` still present |
| X20 | https://learn.microsoft.com/en-us/office/dev/add-ins/overview/office-add-ins | 2026-09-17 | Add-ins overview; leftover preview lines |
| X21 | https://github.com/OfficeDev/office-js-docs-pr/pull/5799 and the repository's `.openpublishing.redirection.json` | merged 2026-06-17 | **Primary evidence of the cancellation** |
| X22 | https://github.com/OfficeDev/office-js-docs-pr/pull/5804 | merged 2026-06-24 | Excel Copilot skills added |
| X23 | https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-skills ; tutorial `docs/excel/excel-copilot-custom-functions-skill.md` in the same repository; sample https://github.com/OfficeDev/Office-Add-in-samples/tree/main/Samples/excel-copilot-skill | 2026-08-13; tutorial 2026-11-19 | Office.js in a skill, Excel preview |
| X24 | https://learn.microsoft.com/en-us/plugins/administrator-guide | 2026-11-16 | Skill storage; no per-skill block |
| X25 | https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers | 2026-11-24 / 11-25 | Most authoritative for admin controls |
| X26 | https://learn.microsoft.com/en-us/plugins/publish , /publish-plugin-publicly , /deploy-plugin , /govern-plugins , /isv-publisher-guide | 2026-11-16 | **Microsoft's ISV path** |
| X27 | https://learn.microsoft.com/en-us/plugins/cost-considerations and /prerequisites | 2026-11-16 | Billing and the licence table |
| X28 | https://learn.microsoft.com/en-us/plugins/data-privacy-security | 2026-11-16 | General. No per-route matrix |
| X29 | https://learn.microsoft.com/en-us/purview/audit-copilot | 2026-08-26 | Audit record fields. Three months old |
| X30 | https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/deploy-and-publish/appsource/prepare/review-copilot-validation-guidelines | 2026-08-13 / 10-23 | Store validation rules |
| X31 | https://learn.microsoft.com/en-us/microsoft-copilot-studio/harnesses-overview | 2026-11-13 / 11-17 | Copilot Studio harnesses |
| X32 | https://learn.microsoft.com/en-us/copilot/cowork/whats-new | 2026-11-25 | Cowork change log |
| X33 | https://learn.microsoft.com/en-us/copilot/autopilot/plugins | 2026-11-10 / 11-20 | Autopilot uses the same plugins. Frontier |
| X34 | https://mc.merill.net/message/ followed by MC1484008 (2026-09-30), MC1556104 (2026-11-16), MC1556090, MC1556095, MC1558023, MC1530611, MC1536062 (2026-11-03), MC1509799, MC1240704, MC1478961 | as shown | Mirror of Message Center posts. Secondary. Some read through search summaries only |
| X35 | https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/plugins-at-microsoft-ignite-2026/4569951 | 2026-11-12 / 11-16 | Microsoft blog. Read as a summary, not verbatim |
| X36 | https://news.microsoft.com/events/ignite/2026/book-of-news/ | 2026-11-17 | Microsoft. Read as a summary, not verbatim |
| X37 | https://techcommunity.microsoft.com/blog/microsoft_365blog/whats-new-in-work-iq-at-ignite-2026-agents-that-get-work-done/4572219 | 2026-11-17 | Microsoft blog. Summary |
| X38 | https://www.microsoft.com/en-us/copilot/features/plugins/explore (target of aka.ms/plugins) | undated | Only the title was readable |
| X39 | https://github.com/OfficeDev/microsoft-365-agents-toolkit/issues/16869 | opened 2026-10-22 | From our 2026-11-09 note. Not re-checked |
| X40 | next.ink Ignite roundup (2026-11-18); Office-Watch feature count | November 2026 | **Third party. Low authority.** Seen through search results |
| — | `Action.OpenUrlDialog` page on Learn | 2026-03-06 | **Stale.** From our 2026-11-05 notes. Not re-read |
| — | `../../poc-e2e/results.md`, `../../copilot-app-components.md` | 2026-11-01 to 11-27 | Our own tests. Source of every [O] |

---

## 6. Caveats

- Three Microsoft texts were read as summaries: the Book of News, the Ignite plugins blog and the Work IQ blog. Quotes from them are short and may be paraphrase. "Not found" for those texts means not found in the summary.
- Message Center items come from a public mirror. Dates there can trail the real post. Two PowerPoint dates disagree.
- Not researched this pass: the Agents SDK status page; `/plugins/manage` and `/plugins/monitor-plugin`; Marketplace review time; paid listings; Build 2026 posts beyond the change log; whether Office in Copilot can be extended.
- "Unknown [NF]" cells are gaps in Microsoft's pages, not proof of absence. Our Word result shows a host can support a route before any page says so. The reverse is also possible.
- The whole area changed in the last six weeks. Most pages cited here are dated 16 November or later. Expect them to change again when the Plugins button rollout and the PowerPoint rollout finish.
