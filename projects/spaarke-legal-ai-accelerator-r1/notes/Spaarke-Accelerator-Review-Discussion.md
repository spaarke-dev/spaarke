# Spaarke Accelerator Review Discussion

Q&A on deploying the Microsoft AI ecosystem (Copilot for Legal, Cowork, Work IQ, Foundry IQ) for legal teams and the Spaarke Accelerators. Compiled 4 November 2026 from Microsoft documentation and announcements current as of that date. Items marked **Verify in tenant** are not fully documented by Microsoft and should be confirmed by a legal engineer in Spaarke's own tenant before they go into the runbook.

---

## Part 1 — Platform questions (round 1)

### 1. If a customer has M365 Copilot, do they already have the Microsoft legal plugin?

Yes for Word; one user step for Cowork.

- **Word desktop:** the legal plugin is enabled by default for all Microsoft 365 Copilot commercial subscribers. Admins govern it through the policy "Legal Skills in M365 Copilot in Word" in the Microsoft 365 Apps admin center. No admin setup is required. [S1][S2]
- **Cowork:** users must add the "Legal for Cowork" plugin themselves from the plugin list; admins control availability on the Agents > Tools page. Cowork itself still requires Frontier enrollment and usage-based billing (Copilot Credits). [S1][S3]

**Accelerator implication:** assume Word is live on day one; Cowork is a user action unless IT has restricted the catalog.

### 2. Can an individual user install a third-party plugin without IT?

Depends on tenant policy; the default leans "yes".

- Plugins in the Microsoft 365 App Store can be acquired by users themselves; admins can also auto-deploy, restrict to specific users/groups, or block. [S4][S5]
- Partner plugins require the IT admin to make them available in tenants where the store is restricted. [S6]
- Admins control discoverability per publisher category and per plugin (All users / No users / Specific users). [S7]

**Accelerator implication:** ship Plugin 1 (skills-only) also as OneDrive folder-drop skills so L1 never depends on store policy (see Q10/Part 2 Q3).

### 3. Do third-party plugins work in Word?

Not yet as plugins. Microsoft's plugin docs: available now in Copilot Cowork; more surfaces coming; the October 2026 rollout added Copilot Chat (admin-deployed plugins work in both Chat and Cowork). [S8][S9]

Working paths into Word today:
- **Declarative agents** are supported in Word and PowerPoint, can be @mentioned from the regular app chat, read the open document by default, and take the selection in Word. [S10][S11]
- **Office add-in as a skill** (preview) for Word, Excel, PowerPoint. [S12]
- **Custom engine agents are not supported** in Word, Excel, PowerPoint, Outlook. [S13]

### 4. Can we provide multiple playbooks by speciality?

Yes. A playbook is a .docx attached when starting a review; Copilot parses it into topics and rules the user confirms, then reviews topic by topic. One playbook per review run. Review is from playbooks only — not yet from templates or other documents. [S14][S15]

**Accelerator implication:** the content pack is a library of .docx playbooks (NDA, MSA, lease, SPA, …) in the Legal Workspace; skill text tells the user which to attach.

### 5. What is "draft precedent playbook"?

A listed capability in Microsoft's transparency documentation: create a playbook from a set of precedents (alongside drafting from templates/previous contracts and comparing against agreed terms). The support page does not yet document the step-by-step UI. In Word it is invoked by prompt with precedents referenced via Copilot's file picker; in Cowork by pointing at a SharePoint folder. [S16] **Verify in tenant.**

### 6. What can a plugin contain, and does Python run?

- Package: .zip with manifest.json (Unified App Manifest v1.28), icons, `skills/` folders. Each skill: `SKILL.md` + up to 20 companion files (`references/`, `scripts/`); 10 MB per skill, 5 MB per companion file. Connectors: remote HTTPS MCP with OAuth or Dynamic Client Registration; API-key auth not yet available in Cowork. [S8][S17]
- Execution: Copilot in Excel creates a runtime, loads Office.js and executes skill scripts (JavaScript). Whether Cowork executes bundled Python scripts is undocumented. [S18][S19]

**Design rule:** deterministic logic lives in the Spaarke MCP server (BFF); bundled scripts are never load-bearing.

### 7. How is Work IQ loaded, and can we create custom "business skills"?

Work IQ is the built-in grounding layer every Copilot-licensed user gets (toggle in Copilot Chat). "Business skills" are Microsoft's Dynamics 365 / Power Platform skills inside Work IQ. Custom skills use the same SKILL.md mechanism: users create/upload/share skills in Copilot Chat; admins deploy skills and plugins from the admin center; skills work across Chat and Cowork. [S9][S20]

### 8. What is the Work IQ API/MCP and how would Spaarke use it?

Developer entry to the same grounding: redesigned remote MCP server, REST API and A2A, consumption-based and independent of Copilot licensing. The retrieve tool supports SharePoint Embedded content; **Dataverse is not a supported retrieval source and not a near-term priority.** [S21][S22]

**Spaarke use:** inside the BFF to pull related mail, chats, meetings and SPE documents into intake/triage/matter context with user permissions enforced. Not for records — Spaarke MCP is the path for Dataverse.

### 9. What is "SharePoint Embedded working storage"?

Microsoft provisions an SPE container as scratch space for agents — intermediate data and outputs supporting task progression, reuse and handoff, inside the tenant boundary. [S22] Validates Spaarke's SPE architecture.

### 10. Foundry IQ vs plugins vs playbooks

| Thing | Job | Consumed by |
|---|---|---|
| Playbook (.docx) | Instruction: what good looks like | Microsoft legal plugin (Word/Cowork) |
| Plugin (skills + MCP) | Behavior + actions | Copilot Chat, Cowork |
| Foundry IQ knowledge base | Retrieval: multi-source, permission-aware, cited | Agents (Copilot Studio, Foundry, Agent Framework, any MCP host) |

Use Foundry IQ for cross-corpus retrieval at department scale (precedents, data rooms, lease portfolios, regulatory). [S23]

### 11. Programmatic delivery of knowledge packs to Foundry IQ

Yes. Knowledge sources and knowledge bases are Azure AI Search objects created via REST/SDK; ship ARM/Bicep + a creation script + content to Blob/SharePoint. Alternative: host in Spaarke's own Foundry IQ and expose through the Foundry IQ MCP server (any MCP host) or register as a generic MCP knowledge source (preview) in the customer's base. [S23][S24][S25]

### 12. L3 knowledge base setup; can OneDrive substitute?

Portal or code — same objects; the Accelerator ships code. An individual cannot replicate Foundry IQ from OneDrive, but Work IQ already grounds Copilot on OneDrive/SharePoint, and a declarative agent with attached files is a lightweight per-user "knowledge base". Foundry IQ is for department-scale retrieval. [S21][S23]

### 13. Where the Spaarke declarative agent fits

Primary vehicle is the plugin (skills auto-invoke in Copilot Chat and Cowork; MCP connector supplies the record). Declarative agent covers Word (no third-party plugins yet) and Copilot Chat for licensed-but-not-Cowork users; @mentioned from the regular chat. Package all surfaces in one M365 app. [S9][S10][S11]

---

## Part 2 — Follow-up questions (round 2)

### 1. App Store plugins vs "partner plugins": what is the difference and which is Spaarke?

Same artifact, two policy states. Partner plugins are distributed through the App Store but require the IT admin to make them available in tenants where the admin has restricted the store [S6]; in open tenants users acquire App Store plugins themselves [S4]. Admins set discoverability per publisher category (Microsoft, partner, …) and per plugin (All / None / Specific users). [S7] Microsoft's role plugins are first-party; Spaarke will be a **partner plugin**.

**Plan:** readiness scan checks the plugin-availability policy; in open tenants the user adds Spaarke in a click, in locked tenants it is a one-line admin approval.

### 2. Is Copilot in Word "Copilot Chat"?

No. Copilot Chat is the standalone cross-app experience (m365.cloud.microsoft, the Copilot app, Teams/Outlook sidebars). In-app Copilot is the pane inside Word/Excel/Outlook/Teams/PowerPoint with access to the open document. [S26][S27] Skills and plugins reached Copilot Chat and Cowork in October 2026 [S9]; Word has no documented SKILL.md skills picker today, unlike PowerPoint and Excel. [S28]

### 3. Skills-only plugin plus OneDrive folder-drop — and where playbooks live

Two deliverables from one source tree:
- **Packaged plugin** (.zip with manifest + `skills/`) → App Store or admin upload.
- **Folder-drop skills** (same `skills/<name>/SKILL.md` folders, no manifest) → user drops into OneDrive at `/Documents/Cowork/skills/<name>/`; Cowork discovers them at the next conversation; or upload a .md/.zip/.skill from the Skills tab. Limits: 50 custom skills per user, 20 companion files, 10 MB per skill. [S29][S30]

**Playbooks are not skills.** The legal plugin does not convert a playbook into a skill; the playbook is a .docx attached to a review and parsed into topics/rules for that run. [S14] The .docx lives wherever stored — the Accelerator places them in the Legal Workspace "Playbooks" library in SharePoint. Saved parsed playbooks (Q5) are held by Copilot for the user, not as a managed file.

### 4. What is a declarative agent; does it overcome the Word limit; can it touch the document?

- **Composition:** a JSON manifest — name, instructions, knowledge (SharePoint sites/files, Graph connectors), capabilities, optional actions (API plugins or Office add-in functions) — executed by Copilot's own orchestrator. No code, no hosting. Built with Agent Builder, Copilot Studio or the Agents Toolkit. [S31][S32]
- **Document interaction:** in Word and PowerPoint a declarative agent can read the entire open document by default; in Word it can also take the user's selection; it can insert images; API plugin actions can receive the document or selection. [S11]
- **Invocation:** @mention from the regular Word Copilot chat or select from the agents menu — explicit, but not a separate app. [S10]
- **Plugin vs agent:** not the same thing. A plugin = skills (SKILL.md) + MCP connectors that auto-invoke. A declarative agent = instructions + knowledge + API actions that run when addressed.
- **Word gap:** yes, the declarative agent is the only third-party path in Word today (custom engine agents unsupported). [S13]
- **Caution:** a community report found some agent-document interactions working in Word Online but not desktop. [S33] **Verify in tenant.**

### 5. Do users attach the playbook every session? What does "save" do?

Microsoft's support page documents attach → parse → confirm → review [S14], and the announcement refers to "reusable playbooks" [S34], but the save UI is not documented. Working interpretation: saving keeps the parsed topic/rule set so later reviews select it rather than re-upload and re-parse; the .docx remains where it was; saved playbooks are per user. **Verify in tenant** — including whether a saved playbook can be shared.

**Runbook step:** each attorney attaches and saves the pack's playbooks on day one.

### 6. "Business skills" are not in Cowork

Correct. "Business skills" is Microsoft's label for the Dynamics 365 / Power Platform capabilities inside Work IQ that surface in Copilot Chat and in Dynamics/Power Apps Copilots — not a Cowork feature. [S20] The Spaarke mechanism is plain skills in the plugin calling the Spaarke MCP connector.

### 7. How do Copilot / Cowork get benefit from Foundry IQ?

Indirectly, through an agent. There is no Foundry IQ toggle in Copilot Chat or Cowork.
- Copilot Studio agents connect to a Foundry IQ knowledge base (GA). [S35][S36]
- Foundry agents publish into Microsoft 365 Copilot and Teams. [S37]
- That agent then appears in Copilot Chat and Cowork and is @mentioned or called as a step.
- Copilot knowledge sources and Foundry IQ knowledge sources are not interchangeable. [S25]

**Spaarke pattern:** Foundry IQ for L3 cross-corpus agents surfaced via a published agent; everyday Copilot/Cowork grounding via SharePoint/SPE under Work IQ plus the Spaarke MCP connector.

---

## Items to verify in Spaarke's tenant

1. "Draft precedent playbook" invocation and inputs in Word and Cowork.
2. Playbook save: persistence location, per-user vs shared, re-use flow.
3. Declarative agent document interaction on Word desktop vs Word Online.
4. Whether Cowork executes bundled scripts (Python) in plugin skills.
5. Default plugin-availability policy for partner publishers in a fresh tenant.

---

## Sources

- [S1] Manage Copilot for legal — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-copilot-for-legal?view=o365-worldwide
- [S2] MC1509799 — Legal plugins in Word and Cowork: https://mc.merill.net/message/MC1509799
- [S3] Cowork role-based plugins admin setup guide (WindowsForum): https://windowsforum.com/news/microsoft-copilot-cowork-adds-sales-service-education-and-legal-plugins-admin-setup-guide.449455/
- [S4] Manage plugins for Copilot Cowork — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/cowork-manage-plugins
- [S5] Copilot Cowork common questions — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/cowork-faq
- [S6] Available plugins for Copilot Cowork — Microsoft Learn: https://learn.microsoft.com/en-us/copilot/cowork/cowork-available-plugins
- [S7] Manage plugins, skills, and MCP servers in Microsoft 365 admin center — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-plugins-skills-mcp-servers?view=o365-worldwide
- [S8] Build plugins for Copilot Cowork — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/cowork-plugin-development
- [S9] MC1493223 — Skills in Copilot Chat: https://mc.merill.net/message/MC1493223
- [S10] General Availability of Declarative Agents in Word and PowerPoint (Web): https://m365admin.handsontek.net/general-availability-of-declarative-agents-in-microsoft-word-and-microsoft-powerpoint-web/
- [S11] Declarative agent document interaction — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/declarative-agent-document-interaction
- [S12] Combine Copilot Agents with Office Add-ins (preview) — Microsoft Learn: https://learn.microsoft.com/office/dev/add-ins/design/agent-and-add-in-overview
- [S13] Known issues in Microsoft 365 Copilot extensibility — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/known-issues
- [S14] Get started with Copilot for legal — Microsoft Support: https://support.microsoft.com/en-us/word/get-started-with-copilot-for-legal
- [S15] Copilot's legal features on by default in Word (AI Switch): https://aiswitch.co.kr/docs/en/resources/insights/microsoft-copilot-legal-plugin-word-default-on-202610/
- [S16] Copilot for Legal: Transparency Documentation — Microsoft Support: https://support.microsoft.com/en-us/word/copilot-for-legal-transparency-documentation
- [S17] wiqd plugin authoring reference — Microsoft: https://microsoft.github.io/wiqd/getting-started/plugin-reference/
- [S18] Overview of Copilot skills for Excel (preview) — Microsoft Learn: https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-skills
- [S19] awesome-copilot-cowork-skills (community; script execution undocumented): https://github.com/kesslernity/awesome-copilot-cowork-skills
- [S20] Work IQ: Business and workplace intelligence — Microsoft Dynamics 365 Blog: https://www.microsoft.com/en-us/dynamics-365/blog/it-professional/2026/09/25/work-iq-business-and-workplace-intelligence-in-the-flow-of-work/
- [S21] Use the retrieve tool in Work IQ MCP — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/mcp/retrieve
- [S22] Work IQ: Production-ready intelligence for every agent — Microsoft 365 Dev Blog: https://devblogs.microsoft.com/microsoft365dev/work-iq-production-ready-intelligence-for-every-agent/
- [S23] What is Foundry IQ? — Microsoft Learn: https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/what-is-foundry-iq
- [S24] Foundry IQ: unified knowledge and serverless retrieval — Foundry Dev Blog: https://devblogs.microsoft.com/foundry/build-smarter-agents-faster-with-foundry-iq/
- [S25] Foundry IQ FAQ — Microsoft Learn: https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/foundry-iq-faq
- [S26] Copilot Chat vs Microsoft 365 Copilot — Tech Community discussion: https://techcommunity.microsoft.com/discussions/microsoft365copilot/copilot-chat-vsus-microsoft-365-copilot-whats-the-difference/4506965
- [S27] Copilot Chat in Word, Excel & PowerPoint — Office Watch: https://office-watch.com/?p=73378
- [S28] Copilot Skills in Word: what exists today — A Guide to Cloud: https://www.aguidetocloud.com/blog/microsoft-365-copilot-skills-word/
- [S29] Use Copilot Cowork (skills) — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/use-cowork
- [S30] Copilot Cowork skills not working (limits) — Microsoft Q&A: https://learn.microsoft.com/en-my/answers/questions/5977097/copilot-cowork-skills-not-working
- [S31] Declarative agents FAQ — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/transparency-faq-declarative-agent
- [S32] Create declarative agents with Agents Toolkit — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/build-declarative-agents
- [S33] "The agent isn't available in Word" — Microsoft Q&A: https://learn.microsoft.com/en-us/answers/questions/5490114/the-agent-isnt-available-in-word-but-you-can-acces
- [S34] Introducing Microsoft Copilot for legal — Tech Community: https://techcommunity.microsoft.com/blog/microsoft-copilot-blog/introducing-microsoft-copilot-for-legal/4564246
- [S35] Foundry IQ in Copilot Studio GA — Azure AI Foundry Blog: https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/foundry-iq-in-microsoft-copilot-studio-is-now-generally-available/4557687
- [S36] What's new in Copilot Studio — Microsoft Learn: https://learn.microsoft.com/en-us/microsoft-copilot-studio/whats-new
- [S37] Publish agents to Microsoft Copilot and Teams — Microsoft Foundry: https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/publish-copilot
- [S38] Foundry IQ deep dive (Jannik Reinhard): https://jannikreinhard.com/foundry-iq-knowledge-bases/
