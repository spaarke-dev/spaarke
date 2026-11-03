---
name: ms-copilot-for-legal-cowork-registry-2026-10-29
description: Microsoft Copilot for Legal (GA 2026-10-15) replaces Legal Agent for Word; Cowork state; plugin registry (ISV path via MCP plugin); Agent 365/E7 unchanged; Business-apps plugin can WRITE Dataverse
metadata:
  type: reference
---

## 2026-10-29: Copilot for Legal / Legal Agent / Cowork / Agent 365 refresh (delta vs synopsis v2 §2.5, 2026-09-21)

**Question**: What did Microsoft ship as "Copilot for Legal", what happened to Legal Agent for Word, Cowork state, and Agent 365/E7 changes affecting an ISV legal app?

**Findings**:
- **Copilot for Legal = legal plugins, GA 2026-10-15** (techcommunity post; MC1509799, roadmap 571884; GA rollout mid-Oct→mid-Dec 2026). Two plugins: Word desktop (on by default; admin policy "Legal Skills in M365 Copilot in Word" in M365 Apps admin center) and "Legal for Cowork" (user adds it; admin via Agents > Tools). 20 Cowork skills incl. `review-contract`, `create-playbook`, `extract-obligations-and-deadlines`, `analyze-related-contracts`, `build-case-chronology`. Requires M365 Copilot commercial USL; Cowork usage is usage-based billing (Copilot Credits, $0.01 PayGo).
- **Legal Agent for Word never reached standalone GA**: it was folded into the legal plugin; MC1509799 says "The Legal Agent is being retired" and existing users "will lose access" (no date).
- **Playbook ingestion is still .docx only** (upload or "Attach from Cloud" from M365). No DMS, no MCP for playbooks in docs. 25+ partner plugins (iManage, NetDocuments MCP, HighQ, Harvey, Legora, DeepJudge, Relativity, Everlaw, Docusign, Bloomberg Law…) arrive via the plugin registry; several are not yet in the Cowork catalog. "Matters" appear only in marketing (Work IQ) and in file-based skills. There is no matter object.
- **Plugin registry (2026-09-30)**: plugins = skills (Agent Skills SKILL.md) + connectors (remote MCP) + agents (sub-agents not supported yet). ISVs publish to the M365 App Store via Partner Center. Admin publisher categories: Microsoft / your org / certified external publishers. MCP registration needs AI Admin or Global Admin consent. Per-tool enablement is enforced by the Agent 365 Tooling Gateway. Claude plugins convert with `atk import openplugin`. **Purview Information Barriers are not supported for plugin/skill management.** Microsoft does not persist ISV content returned via MCP.
- **Hazard**: the "Business apps (preview)" Cowork plugin reads and WRITES custom Dataverse tables under the user's security roles. That could bypass the BFF write path (ADR-002) if users hold direct Write on sprk_ tables.
- **Cowork** GA 2026-06-16; runs Anthropic, OpenAI and xAI models; UBB; Projects + non-Microsoft event triggers (Oct 2026). **Agent 365/E7 prices unchanged** ($15 / $99 / E7 No-Teams $90.45). "Microsoft Copilot" relaunch 2026-09-25 (Home/Code/Autopilot; USL + UBB pricing model).

**Sources**: techcommunity 4564246 (2026-10-15), 4559682 (2026-09-30), 4559416 (2026-09-25), 4516218 (2026-04-30); support.microsoft.com/en-us/word/get-started-with-copilot-for-legal; learn manage-copilot-for-legal (2026-10-13); agent-365 manage-plugins-skills-mcp-servers (2026-10-14); cowork whats-new (2026-10-28), cowork-available-plugins (2026-10-26), cowork-plugin-development (2026-10-02), cowork-work-iq (2026-10-13, preview); mc.merill.net MC1509799; blogs.microsoft.com 2026-09-25; microsoft.com Cowork GA 2026-06-16; microsoft.com/microsoft-agent-365. Techcommunity pages need raw-HTML extraction (WebFetch returns title only).

**Open questions**: Do partner plugins (iManage etc.) work inside Word Copilot, or only Chat/Cowork? Which model powers the Word legal plugin? Does the legal plugin itself work in Information Barriers tenants? Do Spaarke's security roles grant direct Write that the Business-apps plugin could use? EU Data Boundary status for Anthropic-backed Cowork. Related: [[legal-word-ai-mcp-landscape-2026-09-01]], [[m365-copilot-agent-guest-crosstenant-2026-10-08]], [[foundry-iq-work-iq-state-2026-09-21]].
