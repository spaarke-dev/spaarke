---
name: office-apps-copilot-edit-code-skills-2026-11-27
description: Copilot inside Word/Excel/PowerPoint as of 2026-11-27 - Edit with Copilot GA Win/Mac/web (ex Agent Mode); code execution documented only for Excel (JS runtime + Python); Excel FAQ "skills do not introduce a new code-execution mechanism"; MCP federated connectors documented for Word/Excel/PowerPoint/Outlook; Word third-party skills still undocumented; add-in-action docs now redirect to Excel skills; no per-app admin switch; Purview AppHost audit
metadata:
  type: reference
---

# Copilot inside the Office apps (2026-11-27)

**Question**: For Word, Excel, PowerPoint (Win/Mac/web): Copilot modes, code execution, third-party extensibility, admin/governance, direction. Part 1 of a 3-part surface-strategy spike.

**Findings**
- **Modes**: "Agent Mode" is now "Edit with Copilot" / Edit mode, GA in all three apps on Windows, Mac, web (M365 blog 2026-04-22; support pages). Word: Edit + Chat only. Excel: Edit, Chat, Plan. Model picker in all three. Paid M365 Copilot licence in practice; unlicensed Copilot Chat users lost in-app Copilot 2026-04-15 (press only; MC1253858/63 expired). Word tracked changes + comments GA Win/Mac (MC1384426, 2026-06-08); web not named.
- **Code execution**: only Excel has Microsoft statements: "Excel APIs in a JavaScript runtime" (Excel blog 2025-09, stale), Python in Edit with Copilot (Apr 2026), FAQ "Code execution occurs through existing Copilot in Excel tools, operating within approved environments". Word and PowerPoint: nothing published on the runner; our WP-5 test is the only Word evidence. No sandbox/denylist/limit docs for any app.
- **Skills supplying code**: Excel FAQ says "Skills do not introduce a new code-execution mechanism"; scripts are "resources". Matches WP-5 in Word. Not prohibited, not a contract. Learn (2026-11-11): "A shared file format doesn't establish cross-agent reuse or cross-host support."
- **Extensibility per app**: Excel = OneDrive personal skills GA; plugin `agentSkills` with Office.js `.js` scripts still Insider preview (Win 2608+, Mac 16.112+; second tutorial added 2026-11-19). PowerPoint = personal + admin-deployed org skills GA (same manifest 1.30 `agentSkills` zip); script execution not stated. Word = only Microsoft's Legal plugin documented ("Word desktop"). **MCP federated connectors are documented for "Copilot in Word, Excel, PowerPoint, and Outlook"** with write approval, licence, admin controls, Purview audit (Learn 2026-11-16). MCP Apps in Office panes: undocumented.
- **Docs withdrawn**: `/design/agent-and-add-in-overview` redirects to the add-ins overview; `build-api-plugins-local-office-api` redirects to the Excel skills page. Excel skills (preview) is the only documented way for Copilot to run developer Office.js.
- **Admin**: no per-app switch for edit mode or code ("can't be turned off independently", Excel FAQ). Plugins via Agents > Tools / Registry / Settings; connectors via Copilot connectors > Your connections. Audit: CopilotInteraction, AppHost Word/Excel/PowerPoint, AccessedResources Action, AISystemPlugin; no prompt text.
- **Direction**: Ignite 2026 Office post is about Office in Copilot (GA 2026-11-17); one signal for extensibility: sales skills for PowerPoint and Cowork "together in a single plugin". No Word skills roadmap item found. Word support page still says Copilot "does not connect to external tools" (stale; contradicted by Learn and our tests).

**Sources**
- https://support.microsoft.com/en-gb/office/frequently-asked-questions-about-editing-with-copilot-in-excel-1cfd906d-40b4-46be-8e2d-65b893e28a02 (undated)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/federated-connectors-overview (2026-11-16)
- https://learn.microsoft.com/en-us/plugins/plugin-type-skills (2026-11-11); /plugins/build-copilot-chat-skills (2026-11-19); /plugins/deploy-plugin; /plugins/isv-publisher-guide; /plugins/plugin-mcp-apps
- https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-skills (2026-08-13); excel-copilot-custom-functions-skill (2026-11-19)
- https://learn.microsoft.com/en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers (2026-11-24)
- https://learn.microsoft.com/en-us/purview/audit-copilot (2026-08-26)
- https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/office-at-ignite-2026-from-apps-to-agentic-system-of-work/4572226 (2026-11-17)
- https://techcommunity.microsoft.com/blog/excelblog/building-agent-mode-in-excel/4457320 (2025-09-29)
- https://www.microsoft.com/en-us/microsoft-365/blog/2026/06/25/copilot-in-excel-built-for-the-era-of-frontier-finance
- https://mc.merill.net/message/MC1384426 ; https://mc.merill.net/message/MC1484008
- Full write-up with matrix and 26 owner tests: `projects/spaarke-legal-ai-accelerator-r1/notes/research/office-surfaces-2026-11/01-office-apps.md`

**Open questions**: Word Mac/web skill loading and JS runner; runner size limit, denylist, API sets, result path; PowerPoint script execution; MCP Apps rendering in Office panes; whether Purview names the skill and logs skill-driven edits; host list for plugin `agentConnectors` (vs federated connectors) in Office apps. Related: [[word-plugin-skills-not-executed-2026-11-02]], [[word-copilot-pane-plugins-playbooks-addin-2026-10-31]], [[declarative-agents-in-word-2026-11-09]].
