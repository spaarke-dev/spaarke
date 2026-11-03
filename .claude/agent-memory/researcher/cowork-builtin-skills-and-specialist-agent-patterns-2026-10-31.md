---
name: cowork-builtin-skills-and-specialist-agent-patterns-2026-10-31
description: Definitive Cowork built-in skill list (14 in use-cowork + Skill Management + image-gen + browser tool); Built-in list read-only, no per-skill off switch; skill-to-skill only by prose handoff; nothing in Cowork calls agents (worker_agents = Copilot Chat only); script egress blocked by design (PromptArmor bypass mitigated 2026-08-19); specialist pattern R1 = persona panel in one skill, T2 = BFF MCP connector to Foundry agent under OBO
metadata:
  type: reference
---

# Cowork built-in skills (Q9) and the "specialist agent" pattern (Q10) — research as of 2026-10-31

Tags: **Cited** = a primary Microsoft source says it · **Secondary** = credible non-Microsoft or Microsoft-community source · **Inferred** = follows from cited facts, not stated · **Unknown** = no source found. Page dates are `ms.date / updated_at` for Learn, publish date otherwise.

Prior notes read first and not re-derived: `cowork-skill-runtime-and-mvp-claims-2026-10-30.md`, `skill-engines-precedent-licences-2026-10-31.md`, `word-vs-cowork-legal-boundary-and-onboarding-2026-10-31.md` (all under `.claude/agent-memory/researcher/`). This note adds only what is new or sharpened.

---

## Summary

1. **Q9 — the definitive built-in list is 15 named skills plus two unnamed capabilities.** Microsoft's `use-cowork` table (2026-09-14 / 2026-10-21) names 14: Word, Excel, PowerPoint, PDF, Email, Scheduling, Calendar Management, Meetings, Daily Briefing, Enterprise Search, Deep Research, Communications, Adaptive Cards, App (Frontier). The Application card (2026-09-22 / 2026-10-29) adds **Skill Management**. `use-cowork` and the FAQ also describe a "built-in image-generation skill" (ChatGPT Images 2.0) that is not in the table. Local browser use is documented as a "browser tool" (not a skill) with its own admin switch. `/cost` is documented only in a Tech Community release log (prior note). Teams posting, file management, web search and Automations are capabilities with no skill name of their own. **Cited.**
2. **Built-ins cannot be switched off individually.** The Customize > Skills tab has a read-only **Built-in** list; edit/delete/share apply only to "skills you created". The documented off-switches are coarse: Anthropic model family (tenant), **Cowork Browsing** (tenant, off by default; user can decline first-run consent), "Allow web search in Copilot" policy, Frontier membership for App, plugin block/user scoping, and per-conversation plugin toggles in **Sources & Skills**. **Cited (absence + controls).**
3. **A custom skill can only *ask* for a built-in by name; there is no call contract.** Microsoft documents that plugin skills "coordinate with other active skills" and that the conflict scan is resolved by "adding an explicit handoff between them"; Microsoft's own sample `weekly-status-mail` says "use **stakeholder-comms** instead" (an internal id, not the display name "Communications"). Routing is still the model's decision. **Cited / Inferred.**
4. **Q10a — nothing in Cowork calls another agent.** The Cowork TOC has no agents page; plugins are skills + connectors only; Claude-plugin `agents/` is "Not yet supported". Agent-to-agent exists elsewhere: declarative-agent `worker_agents` is brokered by **Copilot Chat** (not Cowork); Copilot Studio connects child/Copilot Studio/A2A/Foundry/Fabric/Agents-SDK agents (Foundry/Fabric/SDK in preview); Foundry Agent Service exposes **incoming A2A v1.0 (GA)** with Entra-only auth (OBO or service identity). **Cited.**
5. **Q10b — treat the Cowork script sandbox as having no egress and no credentials.** Learn is silent for Cowork, but the declarative-agent skills sandbox (the only documented one) says "no network access at runtime", "Authenticated network calls from scripts: Not supported", and "Scripts can't call [connectors/MCP] through the sandbox". PromptArmor's disclosed sandbox bypass (disclosed 2026-06-24, mitigated by Microsoft 2026-08-19) confirms the sandbox "is intended to block network access"; any egress that works today is a bug that will be closed. Microsoft's plugin guidance: "Don't embed secrets in SKILL.md files." **Cited (declarative) / Secondary (Cowork).**
6. **Q10c — recommendation.** R1 (T0/T1, no Spaarke hosting): a **specialist panel inside one skill** (persona files in `references/`, sequential per-persona pass, synthesis, verdict), with optional `/skill-name` handoff to sibling skills as a second, test-gated variant. T2: a **BFF-hosted MCP connector** whose tool calls a Foundry agent server-side (OBO or MI), returned as text or an MCP App; this is the only documented route from Cowork to a real second agent. Copilot Studio/A2A patterns do not reach Cowork today; script→LLM is not viable. **Inferred from cited facts.**
7. **Q10e — "Auto" is model selection, not delegation.** "Cowork picks the model based on the models enabled by your organization." The Application card describes planning as decomposition into steps "executed sequentially". No document describes sub-agents or per-step model routing. **Cited (absence).**

---

## Q9. Cowork built-in skills — definitive table

Display names are exactly as Microsoft prints them. "Documented where" lists the first authoritative source; "Can a skill call it" means: can a `SKILL.md` instruct Cowork to use it.

| # | Skill (exact display name) | What it does (Microsoft's words) | Documented where | Can a custom skill call it? |
|---|---|---|---|---|
| 1 | **Word** | "Create and edit Word documents." Edit-in-place for OneDrive/SharePoint files, version history preserved. | use-cowork §Cowork skills (2026-09-14 / 10-21); FAQ; overview; Application card | By prose only ("create a Word document…"); the model loads it. No API. **Inferred** |
| 2 | **Excel** | "Create and edit Excel spreadsheets." | same | same |
| 3 | **PowerPoint** | "Create and edit PowerPoint presentations." Brand templates applied (June 2026). | same; cowork-brand-templates | same |
| 4 | **PDF** | "Work with PDF documents." | same | same |
| 5 | **Email** | "Compose, reply, forward, and send emails. Save drafts and manage attachments." | same | same; send requires approval |
| 6 | **Scheduling** | "Schedule meetings." | same | same |
| 7 | **Calendar Management** | "Create events using natural language, add Teams meeting links, and manage your calendar." | same | same |
| 8 | **Meetings** | "Prepare meeting intelligence." | same | same |
| 9 | **Daily Briefing** | "Prepare your daily briefing." | same | same |
| 10 | **Enterprise Search** | "Search across your organization." ("Browse your entire Work IQ" — overview) | same | same |
| 11 | **Deep Research** | "Conducts in-depth research across multiple sources to compile comprehensive answers and analysis on complex topics." | same | same |
| 12 | **Communications** | "Draft stakeholder communications." Microsoft's sample refers to it as `stakeholder-comms` in a handoff line. | same; copilot-camp `src/cowork/weekly-status-mail/SKILL.md` (b5b1f8a, 2026-10-28) | **Yes by name** in Microsoft's own sample ("use stakeholder-comms instead"). **Cited (sample) / Inferred (that it routes)** |
| 13 | **Adaptive Cards** | "Generates interactive card-based responses with structured layouts, buttons, and data displays in the conversation." | same; Application card key term "Adaptive card" | By prose ("present as an Adaptive Card"). No card-JSON contract (prior note). |
| 14 | **App (Frontier)** | "Build lightweight, interactive apps from a description, then refine, open, publish, and share them. No coding required." Preview; Frontier program required; Copilot Managed Runtime. | use-cowork §Build apps; whats-new Sept 2026 | By prose; Frontier only |
| 15 | **Skill Management** | "Users can create, edit, and manage custom skills directly through conversation using the Skill Management skill, without manually editing files in OneDrive." Produces the Skill Report. | Application card (2026-09-22 / 10-29); copilot-camp lab CWRK01 shows "Skill management" chip | Probably by prose ("use Skill Management to create skill X") — **Unknown**, untested (prior note §4.1) |
| 16 | *image generation* (unnamed: "the built-in image-generation skill") | "Automatically uses the ChatGPT Images 2.0 model"; saves to session + OneDrive output folder. | use-cowork §Generate images; FAQ; whats-new June 2026; M365 release notes 2026-08-25 | By prose. Not in the skills table; no admin switch documented. **Cited / Unknown (switch)** |
| 17 | *browser use* (not a skill: "browser tool") | Hidden Edge tab on the user's device, user's SSO/cookies; web only, Edge ≥152.0.4191.53; hands back for CAPTCHA/credentials/MFA. | cowork-local-browser (2026-10-22 / 10-24); admin-governance | By prose ("complete this in the browser"); unavailable in desktop app/mobile/non-Edge. |
| 18 | `/cost` | Shows Copilot Credits used by the current task. | Tech Community "Cowork in progress" (prior note S14, Cited there); futurework.blog 2026-06-18 (Secondary) | User-invoked slash; not documented on Learn. **Secondary** |
| — | *Web search* | Bing grounding governed by "Allow web search in Copilot" policy; no Cowork user toggle. | manage-public-web-access (prior S17); overview §Data protection | Not a skill; the agent does it. |
| — | *Teams posting, file/folder management, inbox rules, HTML newsletters* | Listed as capabilities in overview §What can Cowork do; no skill name. | overview (2026-09-29 / 10-29) | Which skill owns them is **Unknown**. |
| — | *Automations* (scheduled prompts ≤25, event-driven tasks) | A page/feature, not a skill; triggers can come from plugins/connectors (Oct 2026). | use-cowork; whats-new Oct 2026 | A skill can only propose one (prior note). |

**Observed-only names (not documented):** internal folder ids `docx`, `xlsx`, `pptx`, `pdf`, `render-ui`, `calendar-management`, `daily-briefing`, `meeting-intel`, `stakeholder-comms` and paths `/opt/workspace-config/.claude/skills/` (MVP/POC agent self-report; prior note). The appearance of `stakeholder-comms` in Microsoft's sample is the only corroboration. **Secondary.**

### Can a custom skill explicitly invoke a built-in by name?
- **Cited:** "Conversation scope — Plugin skills … can read files you attached, reference earlier messages, and coordinate with other active skills" (cowork-plugins, 2026-08-27 / 10-29). "Two skills compete for the same requests → add an explicit handoff between them" (use-cowork). "Don't duplicate built-in skills. Check the built-in skills list before building" (cowork-plugin-development, 2026-10-02 / 10-21). Microsoft's sample: "Do NOT use for leadership or executive updates … use stakeholder-comms instead."
- **Cited (conflict):** cowork-plugins: "If a plugin skill has the same name as a built-in skill, the agent determines which skill to use." vs plugin-development FAQ: plugin skills "can't override built-in skills of the same name."
- **Inferred:** a `SKILL.md` line such as "Produce the deliverable with the Word skill; present the summary with the Adaptive Cards skill" is a routing hint the model usually follows. There is no tool-call API, no return-value contract, and no guarantee. Users (not skills) have `/skill-name`.
- **Unknown:** whether a custom skill can trigger a *plugin* skill (e.g., Legal for Cowork `legal-reasoning`) by name.

### Can built-ins be turned off?
| Scope | Control | Source | Tag |
|---|---|---|---|
| Per built-in skill | **None documented.** Customize > Skills shows "**Built-in** for the skills that ship with Cowork"; editing/deleting/sharing is "For skills you created". | cowork-customize (2026-09-14 / 10-21) | Cited absence |
| Per plugin (and its skills) | User toggle in **Sources & Skills** (saved per device); admin Block / user scoping in M365 admin center > Agents > Tools; admin-deployed plugins can still be toggled off by the user per session | cowork-plugins; Agent 365 manage-plugins-skills-mcp-servers (2026-10-14 / 10-23) | Cited |
| Browser | Tenant **Cowork Browsing** ("Allow browser access"), off by default; Edge policy `CopilotCoworkToolActionsEnabled`; user may decline the first-run "I understand" | cowork-local-browser; cowork-admin-governance (2026-09-14 / 10-30) | Cited |
| Web search | "Allow web search in Copilot" tenant policy; no Cowork user toggle | prior note S17 | Cited |
| Models | Anthropic family off tenant-wide (Auto then chooses from the rest); Fable requires data-retention opt-in | cowork-models (2026-09-14 / 10-24) | Cited |
| App skill | Frontier program membership | use-cowork | Cited |
| Image generation | No Cowork-specific switch documented | — | Unknown |

### Is there a corresponding list for Copilot in Word's pane?
- **No Microsoft list of Word "skills" exists.** Word has no documented custom-skills folder; PowerPoint does (M365 Copilot release notes 2026-09-23 web, 2026-10-06 Windows) and Excel does (excel-skills preview, prior S16). **Cited absence.**
- What the Word pane exposes: the hamburger menu ("Agents and chat history") → **Get Agents** (support "Customize with agents in Word and PowerPoint", undated); the "Switch copilots" menu to "view installed agents" (support "Get started with agents"); third-party reports of a menu with "New chat / your agents / **All agents** / Create agent" (Secondary). The Learn page `wordexcelppt-agents` (2026-03-26 / 10-31) places Word/Excel/PowerPoint Agents "in the **Tools** menu and under **All Agents** in the left navigation pane" of the **Copilot app**, Anthropic-dependent, paid licence only. **Cited.**
- **Manage plugins & skills**: documented only as the path to the Legal plugin toggle in Word (support get-started-with-copilot-for-legal, prior note) and observed in PowerPoint (Office-Watch, Sept 2026, Secondary). What "skills" that menu lists in Word (if anything beyond plugins) is **Unknown**; it is the same menu that hosts PowerPoint's custom skills on PowerPoint.
- Conclusion: in Word, "agents" = declarative agents installed from the store or admin-deployed plus the Word Agent (MC1240704, rollout Apr–Jun 2026); "plugins & skills" = Legal plugin (+ partner plugins "as these updates roll out", prior note). Cowork custom skills are not documented as visible in Word.

---

## Q10. Specialist-agent pattern

### Q10a. Can a Cowork skill invoke another agent?

| Target | From Cowork? | What exists elsewhere | Status | Source |
|---|---|---|---|---|
| Another **Cowork skill** (custom or plugin) | Only by prose handoff; model decides; same conversation; sequential | "coordinate with other active skills"; conflict-scan "explicit handoff" | GA (skills) | cowork-plugins; use-cowork |
| **Declarative agent** (Agent Builder / Agents Toolkit) | **No** — no Cowork doc mentions agents as callable; Cowork TOC has no agents page (26 pages) | `worker_agents` (manifest ≥1.6): "Copilot Chat acts as the broker"; target must be a declarative agent or an Agent 365-registered A2A card; text in/text out; connected agent's Adaptive Cards are consumed by the active agent, not shown | GA (declarative) / preview (A2A registration `/beta`, 2026-04-30) | declarative-agent-connected-agent (2026-06-18 / 10-22); agent-registration overview |
| **Copilot Studio agent** | **No** documented path. Publishing to the "Microsoft 365 Copilot" channel is GA, but no source says such agents appear in Cowork | Copilot Studio as *orchestrator*: child agents; connected Copilot Studio agents; **A2A** (standard harness; auth None/API key/OAuth 2.0; endpoint URL + `.well-known/agent.json`); Foundry agents (preview); Fabric Data agents (preview; "don't currently function when your main agent is deployed to Microsoft Copilot"); Agents SDK agents (preview). Cannot be *exposed* as an MCP server (no doc found). | A2A GA-ish (standard harness page 2026-08-26 / 10-15 has no preview banner); others preview | authoring-add-other-agents (2026-05-15 / 10-15); add-agent-agent-to-agent |
| **Foundry agent** | **No** direct path; reachable only behind an MCP connector that Spaarke hosts | Foundry Agent Service **incoming A2A v1.0 GA** (v0.3 preview), Entra ID only ("Key-based authentication and unauthenticated access aren't supported"), **OBO or service identity**, Foundry Agent Consumer role, text-only, no streaming, JSON-RPC; Foundry agents call each other via `A2ATool`. Foundry agent as MCP server: not documented (community wraps with Azure Functions `mcpToolTrigger`, Secondary) | GA (A2A 1.0) | enable-agent-to-agent-endpoint (2026-09-11 / 10-21) |
| Claude-plugin **`agents/` sub-agents** | "Not yet supported" (also `hooks/`, `commands/`; `bin/` "Not applicable") | — | — | cowork-plugin-development (2026-10-02 / 10-21) |
| **Agent 365 / Entra Agent ID** A2A | Not surfaced in Cowork | Entra Agent ID "supports … OAuth 2.0, MCP, and A2A for authentication and agent-to-agent communication"; agent registry is the directory `worker_agents` consumes; Agent 365 Tooling Gateway enforces per-tool policy for MCP servers | Preview | entra agent-id overview; manage-plugins-skills-mcp-servers |

Side note (Cited): the Agent 365 admin page says "Blocking a plugin package blocks its included agents", and `wiqd validate` checks "the declarative-agent surface" — so a plugin package *can* carry a declarative agent alongside skills and connectors. Cowork consumes only the skills and connectors (Inferred; test #7).

### Q10b. Scripts in the Cowork sandbox: egress, credentials, LLM calls

| Question | Answer | Tag | Source |
|---|---|---|---|
| Outbound HTTPS from a skill script | Cowork Learn: silent. The only documented skill sandbox (declarative agents) says: "Internet or network access from scripts — Not supported. The sandbox has no network access at runtime." PromptArmor: Cowork's sandbox "is intended to block network access and prevent the agent from running code that reaches any untrusted services"; their bypass used a file-sync helper outside the sandbox that accepted URLs; disclosed 2026-06-24, "Microsoft confirmed the issue had been mitigated" 2026-08-19; background processes "continue to execute even when an agent's turn ends". | Cited (declarative) / Secondary (Cowork) | declarative-agent-skills (2026-10-26 / 10-27); promptarmor sandbox-bypass |
| Hold credentials | No documented way to inject a secret into a script. Microsoft: "Don't embed secrets in SKILL.md files. Use agentConnectors with authentication for API credentials"; connector secrets live in the Enterprise Token Store via `referenceId` and "never appear in the manifest or skill files". Static evaluation scans bundled code "for security issues". | Cited | cowork-plugin-development; use-cowork §How Cowork evaluates skills |
| Call an LLM endpoint from a script | Not viable: no egress by design, no credential channel, and declarative-agent rule "Authenticated network calls from scripts: Not supported." The only model available is the one the user picked in the model picker. | Inferred | as above |
| Call a connector/MCP tool from a script | Declarative sandbox: "The agent can use enabled capabilities through the orchestrator. Scripts can't call them through the sandbox." Cowork: undocumented; assume the same. | Cited (declarative) / Inferred (Cowork) | declarative-agent-skills |
| Security/ethics implications | (a) Any working egress from a script is a vulnerability class Microsoft has already fixed once; a skill that depends on it will break and may be flagged by the Trust & Safety gate ("sensitive-data" cases). (b) Customer documents leave the M365 service boundary only via *connector* tool calls, which Cowork gates with an approval card ("Cowork asks for your approval before the tool runs") and sends as `contentEncoding: base64`; Purview DLP governs Cowork actions but not what a third-party server does with the payload — Spaarke needs its own DPA/processing terms for T2. (c) Secrets in a skill are visible to every user who can download it. (d) PromptArmor's second finding: Cowork can message *the active user* in Teams/email without approval, which an injected skill can abuse for exfiltration — keep specialist outputs in-conversation or in OneDrive, never auto-send. | Cited / Secondary / Inferred | use-cowork §Approve actions; cowork-plugins §Connect; promptarmor exfiltrates-files; promptarmor practitioner guide (2026-05-13) |

### Q10c. Viable patterns, ranked

| # | Pattern | Availability | Auth | Billing | Data boundary | Latency | What the user sees | Verdict |
|---|---|---|---|---|---|---|---|---|
| **1a** | **Specialist panel inside one skill**: `references/specialists/<role>.md` (persona, mandate, checklist, red flags, house positions); SKILL.md loops: for each specialist → read file in full → produce an anchored finding set → synthesis → verdict (SHIP / REVISE / HOLD style). Precedent: cat-agent-skills `persona-reaction-panel` (platforms Cowork + Copilot Studio; "anti-drift rule: every paragraph must contain a quoted phrase … from the personas file"). | **GA now** (skills, 50 cap, 20 companions / 10 MB) | User's own; nothing new | Copilot Credits for the one conversation (model responses + tool calls); `/cost` to measure | Inside M365 service boundary + chosen model subprocessor (Anthropic/OpenAI/xAI per picker; EU tenants GPT-only) | One conversation; linear in #specialists; reasoning-effort picker matters | One skill chip; optional Adaptive Card per specialist; question cards for scoping | **Recommended for R1.** Deterministic structure, no cross-skill routing risk, survives any model. |
| **1b** | **Skill-to-skill handoff**: orchestrating skill says "invoke `/contract-specialist` on sections 3–7 and wait for its findings"; each specialist is its own skill (own description, own Skill Report, own `references/`). | GA mechanics, but **routing is undocumented** ("coordinate with other active skills"; Microsoft's sample hands off by name) | same | same; more skill loads = more credits | same | Sequential ("executes them sequentially"); each load costs a turn | Multiple chips ("Preparing to …"); user can also call `/specialist` directly | **R1 variant, test-gated** (tests #1–#3). Good for reuse (`/ip-specialist` usable alone) and for the conflict scan ("explicit handoff"). Risk: the model skips the hop. Never reuse built-in names; the 20-skills-per-package cap bounds a plugin's panel. |
| **1c** | **Project instructions as persialist persona** (Cowork Projects: private, instructions override global). | GA (Oct 2026) | — | — | — | — | — | Not callable from a skill; per-user setup. Useful for a "practice-group project", not as a specialist. |
| **2** | **BFF-hosted MCP connector → Foundry agent (server-side)**: plugin = our skills + `agentConnectors[]` remote MCP server on the BFF; tool `review_with_specialist(file, specialty, questions)` → BFF calls Foundry agent (Agent Framework handoff/magentic if multi-specialist) → returns text/JSON; optional **MCP App** widget or **elicitation form** for scoping. | **GA** (Cowork plugins, MCP connectors, file input Aug 2026, elicitation 2026-05, MCP Apps June 2026); upload + share needs no admin; Foundry A2A/Responses GA | User completes OAuth once (`OAuthPluginVault`) or **DCR** ("Omitting the authentication configuration is supported only in Cowork"); BFF gets a user token → **OBO** to Foundry (Foundry supports OBO) or MI-as-FIC (Spaarke auth v4) | Cowork credits for the tool call **plus** Azure Foundry/AOAI tokens on Spaarke's bill → needs a pricing/pass-through decision (customer-stamp pricing note) | Document bytes leave M365 to Spaarke Azure (approval card, `Show parameters`); DLP/Purview do not see inside; Spaarke DPA required; EU-boundary tenants need region pinning | Tool-call round-trip; MCP Apps `ui/initialize` ≤10 s; long reviews → return a job id + poll or write result to OneDrive via Cowork | One-time "Connect" sign-in; approval card per tool run; result text or widget; client identity header `copilot-cowork/1.0` | **Recommended for T2.** The only documented way to get a *real* second model/agent with its own instructions, knowledge, tools and audit trail. |
| **3** | **Copilot Studio / Foundry via A2A or connected agents** | Copilot Studio A2A GA; Foundry/Fabric/SDK connections preview; declarative `worker_agents` GA but **Copilot Chat only** | OAuth/API key/None (Copilot Studio); Entra OBO/service (Foundry) | Copilot Studio usage-based credits + Foundry tokens | Depends on host | Extra orchestration hop ("Increase latency … the connected agent then runs using its own orchestration layer") | Only in Copilot Chat/Teams; **not in Cowork** | **Not for Cowork now.** Relevant only if Spaarke also ships a Copilot Chat declarative agent (T2/T3); then `worker_agents` → Agent 365-registered A2A card pointing at the Foundry agent. Re-check if Cowork gains an agents surface. |
| **4** | **Script calls an LLM** (Python `urllib` → AOAI/BFF) | — | No credential channel | — | Would exfiltrate | — | Nothing (background) | **Not viable** and a policy violation ("Don't embed secrets"; sandbox designed to block egress; bypass mitigated 2026-08-19). |

**Recommendation for R1 (T0/T1):** build 1a as the default shape of every "review" accelerator (the specialist roster is a `references/` asset, so the same roster can be versioned, shared and later moved server-side), and run tests #1–#3 before deciding whether 1b (standalone `/specialist` skills) is reliable enough to ship as well. Give each specialist file a fixed output schema (finding id, clause anchor, severity, position, suggested text, confidence) so the synthesis step and any script (`check_outputs.py`) can validate it. Keep every specialist output in the conversation or OneDrive; never auto-send.

**Recommendation for T2:** implement pattern 2 as *the* connector: one MCP tool per specialist capability, OBO through the BFF to a Foundry agent per specialty (Agent Framework handoff/magentic for multi-specialist), text results plus an MCP App for the findings grid. Keep pattern 1a as the fallback path when the connector is not installed (skills must degrade gracefully).

### Q10d. Microsoft and community samples

| Source | What it shows | Relevance | Tag |
|---|---|---|---|
| `microsoft/cat-agent-skills` @ `3678d23` (2026-10-28), 133 submissions; platforms tagged Cowork 79 / Copilot Studio 80 / Scout 44 | `persona-reaction-panel` (N personas from one `references/` file, per-persona six-question pass, synthesis, SHIP/REVISE/HOLD; Cowork + Copilot Studio) — closest precedent for 1a. `enterprise-agent-design-authority` (Discover→…→Challenge→Decide lifecycle). `agent-red-team` (adversarial reviewer with canary script; Copilot Studio). `redlining-content` (lxml + pdfplumber "present in the Copilot Studio sandbox — no pip install"). `agent-harness-explorer` (runtime probing; its only network probe is "one HTTPS request to pypi.org" as an *active-safe* step, with results recorded as unknown/not-visible when it fails). **No submission delegates to a named sub-agent in Cowork.** | Pattern 1a precedent; sandbox probing precedent | Cited |
| `microsoft/copilot-camp` @ `b5b1f8a` (2026-10-28) `src/cowork/` | `weekly-status-mail` hands off by name ("use stakeholder-comms instead"); `CopilotDevCamp-for-cowork` = 3 skills + Microsoft Learn MCP connector (`authorization.type: None`) — the skill `foundry-research` tells the model to call the connector's search tools. No scripts, no multi-agent. Lab index: "Agents — Declarative agents that combine instructions, tools and orchestration for multi-step work." | Pattern 2 shape (skill + connector); handoff-by-name precedent | Cited |
| Microsoft Agent Framework orchestrations 1.0 (devblogs 2026-07-08): sequential, concurrent, group chat, **handoff** ("routed specialist teams"), **magentic** (manager plans, assigns specialists, replans) in Python + .NET | Server-side multi-specialist engine for the Foundry side of pattern 2 | Cited |
| `microsoft/CopilotStudioSamples` `extensibility/a2a/Simple-A2A-Sample` (.NET 10, AOAI) | Minimal A2A server consumed by Copilot Studio | Pattern 3 | Cited (linked from Learn) |
| Copilot Studio guidance "Multi-agent orchestration patterns and best practices" (`/guidance/multi-agent-patterns`) and rule of thumb "more than 30-40 choices of action" → split | Design heuristics for when to split specialists | Cited |
| Anthropic `skills/skill-creator` uses `agents/{analyzer,comparator,grader}.md`; `claude-for-legal` plugins carry `agents/` | Dropped by `atk import openplugin` / "Not yet supported" in Cowork | Prior notes | Cited |

### Q10e. Does "Auto" or the planner delegate internally?
- **Cited:** "When you select Auto, Cowork picks the model based on the models enabled by your organization." "Cowork shows a model badge in the conversation so you can see which model produced a response." "Usage is based on factors such as model choice, context volume, **orchestration**, and tools used" (cowork-models). Application card: "Planning: Cowork decomposes complex natural language instructions into a sequence of individual steps and executes them across multiple Microsoft 365 services"; "executes them sequentially".
- **Unknown:** whether the planner ever runs sub-agents or mixes models within one task. The word "orchestration" appears only as a billing factor. Design for a single sequential agent.

---

## Corrections and sharpenings to prior notes

| Prior statement | Status | Update |
|---|---|---|
| 2026-10-30: built-ins = S1 table + Skill Management + `/cost` + image generation | **Confirmed, extended** | Add: the Customize > Skills tab now documents a read-only **Built-in** list; local browser is a "browser tool" with tenant switch **Cowork Browsing** (off by default); image generation has no documented switch. |
| 2026-10-30: "A skill can web-fetch public sites — probably true for the agent; **Unknown for scripts**" | **Sharpened** | Script egress is *intended to be blocked* (PromptArmor, with Microsoft mitigation 2026-08-19) and is explicitly unsupported in the declarative-agent sandbox. Treat as "no egress"; a skill must not depend on it. |
| 2026-10-30: MVP's internal names (`stakeholder-comms`, …) "can't be verified" | **Partly corroborated** | Microsoft's own `weekly-status-mail` sample hands off to `stakeholder-comms`. Still Secondary; do not hard-code. |
| 2026-10-30: "A skill can call built-in skills (C/I)" | **Downgraded to Inferred** | Only "coordinate with other active skills" and "explicit handoff" are cited; there is no call contract. |
| 2026-10-31 word-vs-cowork: "Word custom skills exist only for Excel/PowerPoint" | **Confirmed** | PowerPoint custom skills GA per M365 release notes 2026-09-23 (web) / 2026-10-06 (Windows). Word: still none. |
| 2026-10-31 word-vs-cowork: "Manage plugins & skills" = Legal toggle path | **Confirmed; extended** | The Copilot app's "All Agents" nav and Word's pane hamburger ("Agents and chat history" → Get Agents / Switch copilots) are the agent surfaces; the Word/Excel/PowerPoint Agents are Anthropic-dependent, paid-licence, Copilot-app agents (Learn 2026-03-26 / 10-31). |
| 2026-10-30: "Don't rely on parallel sub-agents … `agents/` not supported" | **Confirmed** | Still "Not yet supported" at 2026-10-21. No agents page in the Cowork TOC. |
| General assumption that a Copilot Studio or declarative agent could be "called from Cowork" | **Contradicted (absence)** | No Cowork document mentions agents as callable; `worker_agents` is Copilot Chat-brokered; Fabric Data agents "don't currently function when your main agent is deployed to Microsoft Copilot". |

---

## Live-tenant tests (ordered by value to R1)

1. **Skill→skill handoff.** Skill A (`panel-orchestrator`) says: "Invoke `/contract-specialist` on sections 3–7, wait for its findings table, then continue." Record: does the B chip load; is B's output available to A; order; credits via `/cost`. Repeat with "use the contract-specialist skill" (no slash).
2. **Skill→plugin skill.** Skill A says "use Legal for Cowork's `legal-reasoning` skill to evaluate …". Does the plugin chip load? (Legal for Cowork installed.)
3. **Skill→built-in by name.** "Render the summary with the Adaptive Cards skill; save the memo with the Word skill." Confirm chips, and that referring to `stakeholder-comms` vs "Communications" behaves the same.
4. **Egress probe (benign, Spaarke-controlled host).** `scripts/probe.py` attempts one HTTPS GET to a Spaarke endpoint and one DNS lookup; print exception class; also spawn a 60 s background sleep and check whether it outlives the turn. Expect failure; document the exact error for the skill's fallback text.
5. **Built-in inventory.** Customize > Skills > Built-in: exact names and count; any toggles; whether Skill Management, image generation, browser, `/cost` appear. Compare with the Q9 table.
6. **Word pane.** Copilot pane hamburger: list menu items (All agents / Get agents / Manage plugins & skills); check whether any Cowork or PowerPoint custom skill is visible in Word.
7. **Plugin with a declarative agent.** Upload a package containing a declarative agent + 2 skills + 1 connector to Cowork; record what installs and whether the agent is reachable.
8. **Cost of a panel.** Run 1a with 3 specialists vs a single-pass review on the same NDA; compare `/cost`, wall-clock, and finding counts under Auto, GPT 6.1 Sol and Claude Opus 5.5.
9. **Connector OBO spike (T2 pre-work).** Minimal MCP server on the BFF with DCR (no `authorization` object), one tool echoing the caller's `oid`/`tid`; confirm Cowork's OAuth flow, the `copilot-cowork/1.0` User-Agent, and base64 file input size behaviour.

---

## Reference links (dates)

**Cowork (Learn, all `learn.microsoft.com/en-us/copilot/cowork/…`)**
- `use-cowork` — skills table, image generation, evaluation gates (2026-09-14 / 2026-10-21)
- `responsible-ai/copilot-cowork-application-card` — Skill Management, sequential execution, conversation-scoped memory (2026-09-22 / 2026-10-29)
- `cowork-faq` — skills list, browser, Auto (2026-09-29 / 2026-10-29)
- `` (overview) — capabilities by category (2026-09-29 / 2026-10-29)
- `whats-new` (2026-10-28 / 2026-10-29)
- `cowork-customize` — Built-in list, Preferences 20 KB (2026-09-14 / 2026-10-21)
- `cowork-plugins` — "coordinate with other active skills", name-clash rule (2026-08-27 / 2026-10-29)
- `cowork-plugin-development` — `agents/` not yet supported, secrets rule, auth types, DCR Cowork-only, client identity (2026-10-02 / 2026-10-21)
- `cowork-models` — Auto, effort, "orchestration" billing factor (2026-09-14 / 2026-10-24)
- `cowork-local-browser` — browser tool, Cowork Browsing, Edge 152 (2026-10-22 / 2026-10-24)
- `cowork-admin-governance` — isolated environment, model toggles, browser (2026-09-14 / 2026-10-30)
- `toc.json` — 26 pages, none about agents (fetched 2026-10-31)

**Agent-to-agent**
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-connected-agent (2026-06-18 / 2026-10-22)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/agent-registration/overview (preview, 2026-04-30)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-skills — sandbox table (2026-10-26 / 2026-10-27)
- https://learn.microsoft.com/en-us/microsoft-copilot-studio/authoring-add-other-agents (2026-05-15 / 2026-10-15)
- https://learn.microsoft.com/en-us/microsoft-copilot-studio/add-agent-agent-to-agent (2026-08-26 / 2026-10-15)
- https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/enable-agent-to-agent-endpoint (2026-09-11 / 2026-10-21)
- https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers (2026-10-14 / 2026-10-23)
- https://learn.microsoft.com/en-us/entra/agent-id/what-is-microsoft-entra-agent-id (preview; via search)
- https://devblogs.microsoft.com/agent-framework/agent-frameworks-orchestration-patterns-reach-1-0 (2026-07-08)

**Word / Office surface**
- https://learn.microsoft.com/en-us/microsoft-365/copilot/wordexcelppt-agents (2026-03-26 / 2026-10-31)
- https://support.microsoft.com/en-au/topic/customize-with-agents-in-word-and-powerpoint-2419337e-8043-4f18-8bab-1ad1344d5cde (undated)
- https://mc.merill.net/message/MC1240704 (2026-02-27, upd 2026-03-27) · https://mc.merill.net/message/MC1187799 (2025-11-18, upd 2026-02-12)
- https://learn.microsoft.com/en-us/copilot/microsoft-365/release-notes — PowerPoint custom skills 2026-09-23 / 2026-10-06; Cowork 2026-09-23, 2026-08-25 (page 2026-09-03 / 2026-10-31)

**Samples**
- https://github.com/microsoft/cat-agent-skills (HEAD 3678d23ad8fb8d098c428302a98044ac9514d5a1, 2026-10-28) — `submissions/persona-reaction-panel`, `enterprise-agent-design-authority`, `agent-red-team`, `redlining-content`, `agent-harness-explorer`
- https://github.com/microsoft/copilot-camp/tree/main/src/cowork (HEAD b5b1f8a1e488713063b46883d5f56d3442607155, 2026-10-28)
- https://github.com/microsoft/CopilotStudioSamples (extensibility/a2a/Simple-A2A-Sample)

**Secondary**
- PromptArmor, "Microsoft Copilot Cowork sandbox bypass" — https://www.promptarmor.com/resources/microsoft-copilot-cowork-sandbox-bypass (disclosure 2026-06-24, mitigated 2026-08-19)
- PromptArmor, "Copilot Cowork exfiltrates files" — https://promptarmor.com/resources/microsoft-copilot-cowork-exfiltrates-files (undated, 2026)
- PromptArmor, "Securing Microsoft Copilot Cowork: a security practitioner's guide" — https://promptarmor.com/resources/securing-microsoft-copilot-cowork-a-security-practitioners-guide (2026-05-13)
- futurework.blog, `/cost` skill (2026-06-18) · Office-Watch, "Manage plugins & skills" in PowerPoint (Sept 2026)

Local clones used: `C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke\ee376ef3-44a0-44eb-9490-149c2fde42f6\scratchpad\gh\{copilot-camp,cat-agent-skills}`.

---

## Proposed researcher-memory entry (main session to apply under `.claude/agent-memory/researcher/`)

File: `cowork-builtin-skills-and-specialist-agent-patterns-2026-10-31.md`

```markdown
---
name: cowork-builtin-skills-and-specialist-agent-patterns-2026-10-31
description: Definitive Cowork built-in list (14 in use-cowork table + Skill Management + unnamed image-gen skill + browser "tool" + /cost); Customize > Skills "Built-in" list is read-only, no per-skill off switch; skill-to-skill only by prose handoff (MS sample uses `stakeholder-comms`); nothing in Cowork calls agents (worker_agents = Copilot Chat only; Copilot Studio A2A/Foundry/Fabric connections; Foundry incoming A2A v1.0 GA Entra-only); script egress designed-blocked (PromptArmor bypass mitigated 2026-08-19); ranked specialist patterns: panel-in-one-skill (R1) → BFF MCP connector→Foundry OBO (T2); Copilot Studio/A2A not reachable from Cowork; script→LLM not viable
metadata:
  type: reference
---
(body = this file's Summary, Q9 table, Q10 options table, Corrections, Live-tenant tests, Reference links)
```

Index line for `MEMORY.md` (AI platform section):

`- [Cowork built-ins + specialist agents (2026-10-31)](cowork-builtin-skills-and-specialist-agent-patterns-2026-10-31.md) — 15 named built-ins + image-gen/browser/`/cost`; Built-in tab read-only; no agent calls from Cowork (worker_agents = Chat only); script egress blocked by design; R1 = persona panel in one skill, T2 = BFF MCP→Foundry OBO`
