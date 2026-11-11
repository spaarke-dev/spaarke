---
name: copilot-legal-taxonomy-and-practice-pack-dev-refs-2026-10-29
description: Official taxonomy (Copilot for legal = umbrella over Word legal plugin + "Legal for Cowork"), Cowork vs Chat vs Word licensing (UBB requires USL), verified dev reference set + sample repo SHAs for Cowork plugins/skills, Agent Builder skills limits, SharePoint site skills, knowledge/ plan for practice packs
metadata:
  type: reference
---

## 2026-10-29: Copilot for legal taxonomy + practice-pack developer reference set

**Question**: Exact naming and definitions for Copilot for legal, Cowork, Chat and Word. Verified dev references and samples for building practice packs (T0 Cowork skills plugin + Word playbooks; T1 SharePoint library, site skills, Agent Builder). Knowledge/skill plan for Claude Code.

**Findings**:
- **Umbrella confirmed.** The Learn admin page "Manage Copilot for legal" (learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-copilot-for-legal, 2026-10-13) says: "Two legal plugins are available: one for Copilot in Word and one for Copilot Cowork". The Cowork plugin's name is **"Legal for Cowork"**; the Word policy is "Legal Skills in M365 Copilot in Word". MC1509799 says "Cowork for Legal" (inconsistent). Blog 4564246 headline: "Introducing Microsoft Copilot for legal".
- **Billing.** The USL/UBB page (2026-10-05) says: "UBB experiences consume Copilot Credits and require a USL for access." Cowork access = in scope of a spending policy that selects Cowork (cowork-access). Copilot Chat (Basic) is "included with eligible Microsoft 365 licenses" (copilot/overview, 2026-09-08).
- **Corrects prior memory**: Cowork DOES render MCP Apps widgets (mcp-apps-support, 2026-09-08). It also supports form-mode MCP elicitation (cowork-elicitation-forms). No `ui/update-model-context`; 64 KiB inline result cap; domains are gated by `validDomains[]`.
- **Limits per surface.** Cowork plugin: 20 skills (ASKILL-M002) and 10 connectors; companions 20 files, 5 MB each, 10 MB total. User custom skills: 50 max; .md up to 1 MB. Agent Builder / declarative-agent skills: 8 per agent; 50 MB zip; 25 MB per file; 350 files; depth 3; instructions <20,000 chars; sandbox with no network. ISVs cannot submit a declarative agent with skills to Partner Center yet. SharePoint site skills (`/Agent Assets/Skills/<name>/SKILL.md`) cannot call external systems or run code.
- **Samples.** microsoft/copilot-camp `src/cowork/` @ b5b1f8a1 (labs CWRK0–04). microsoft/cat-agent-skills @ 3678d23a (Power CAT gallery; vendor-contract-risk-review, redlining-content, cowork-plugin-engineer). microsoft/wiqd @ 8bd50d04 (v0.22.0 preview; eval extension targets declarative agents only). anthropics/claude-for-legal @ 4a6c6518 (Apache-2.0; 12 plugins use agents/, hooks/ and CLAUDE.md, which `atk import openplugin` drops). agentskills/agentskills @ 69ef37e9 (skills-ref validator).

**Sources**: learn copilot/cowork/{cowork-plugin-development (10-02/upd 10-21), mcp-apps-support, cowork-elicitation-forms, cowork-faq, overview, cowork-available-plugins (10-26), cowork-access, cowork-admin-governance, cowork-customize, use-cowork, cowork-manage-plugins}; microsoft-365/copilot/{user-subscription-license-usage-based-billing, usage-based-billing-manage-copilot-credits}; extensibility/{agent-builder, agent-builder-add-skills, declarative-agent-skills} (10-26); sharepoint/copilot-in-sharepoint-skills (09-28); support copilot-for-legal-transparency-documentation; agentskills.io/specification; platform.claude.com agent-skills/best-practices; mc.merill.net MC1509799.

**Open questions**: Do Cowork plugin skills (`agentSkills[]`) run in Copilot Chat, or only in Cowork? Do SharePoint site skills consume UBB ("new agentic experiences in SharePoint")? How does Cowork treat unknown SKILL.md frontmatter keys? The plugin-dev page contradicts itself on whether `mcpToolDescription` is required at v1.28. No official Word playbook schema exists. Related: [[copilot-legal-enablement-kit-deployment-paths-2026-10-29]], [[copilot-ux-components-and-kit-package-anatomy-2026-10-29]].
