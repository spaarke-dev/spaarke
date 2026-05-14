---
source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-intelligence
fetched: 2026-05-14
ms_date_on_page: 2026-03-30
gitcommit: https://github.com/MicrosoftDocs/powerapps-docs-pr/blob/41a6612ba5553aaaa5256927f5cc2932ef2e249d/powerapps-docs/maker/data-platform/data-platform-intelligence.md
note: |
  Reached via redirect from https://aka.ms/DVinWorkIQLearnMore (linked from the dataverse-business-skills
  README). This is the umbrella Learn page for what the dataverse-business-skills repo calls
  "Business Skills" — Microsoft Learn names the broader feature **Dataverse intelligence** and
  positions it as an extension of **Work IQ**. Business Skills are the authored unit of business
  context; this page covers the prerequisites and admin enablement. The authoring format itself
  is documented by example in the microsoft/dataverse-business-skills GitHub repo (see SOURCE.md).
---

# What is Dataverse intelligence? - Power Apps | Microsoft Learn

> [This article is prerelease documentation and is subject to change.]

Microsoft Dataverse intelligence extends **Work IQ** to bring business data understanding to AI agents and Microsoft Copilot. Work IQ helps agents understand work artifacts like files, meetings, and messages. Dataverse intelligence builds on this foundation by enabling agents to understand and act on your business data.

With Dataverse intelligence, you can define reusable business context that agents use to understand what your data means, follow your organization's processes when taking actions, and read and update Dataverse records reliably. **This business context is shared across agents, so you define it once and use it everywhere.**

> **Important**
>
> - This is a **preview** feature.
> - Preview features aren't meant for production use and might have restricted functionality. These features are subject to [supplemental terms of use](https://go.microsoft.com/fwlink/?linkid=2189520), and are available before an official release so that customers can get early access and provide feedback.

## Prerequisites

- Microsoft 365 admin role (AI administrator, Global administrator) to access Microsoft 365 admin center Copilot settings.
- Power Platform administrator role to access Dataverse intelligence environment settings.
- The environment where you use Dataverse intelligence must be a **Managed Environment**.
- The environment must be enabled and configured for **Dataverse MCP server preview**. Business skills are only available for use with the preview version of Dataverse MCP server. More information: [Use preview tools and upcoming features in Dataverse MCP server](data-platform-mcp-preview-tools.md)

## Enable Microsoft 365 admin center Copilot Dataverse settings

1. Go to [Microsoft 365 admin center](https://admin.cloud.microsoft/?#/homepage). Select **Copilot** > **Settings**.
2. Locate and select **Dataverse data available in Microsoft 365 Copilot**.
3. Select **All users**, or
4. Select **Specific groups** and enter the list of Entra security groups.
5. Select **Save** to save the setting changes.

## Enable Dataverse intelligence (preview)

1. Go to [Power Platform admin center](https://admin.powerplatform.microsoft.com/). Select **Manage** > **Environments**.
2. Open the environment where you want to turn on the Dataverse MCP server, and then select **Settings** > **Product** > **Features**.
3. Scroll down to locate **Dataverse intelligence**.
   - Turn on **Allow data availability in M365 copilot**, and/or
   - Turn on **Enable Dataverse intelligence (Work IQ) for agents and AI experiences**.
4. Make sure **Allow MCP clients to interact with Dataverse MCP server (Preview version)** is enabled. If it's not, enable it.
5. Select **Save** to save the setting changes.

---

## GAPs (logged 2026-05-14 by curator)

- **No dedicated Microsoft Learn page yet for the Business Skill authoring format.** The Markdown-with-YAML format used by Business Skill records is documented by example in the `microsoft/dataverse-business-skills` GitHub repository (see `samples/business-skill/SKILL.md`) — not on Learn.
- **No dedicated Microsoft Learn page yet for "App MCP" (model-driven app as MCP server).** Several attempted URLs returned 404 on 2026-05-14:
  - `https://learn.microsoft.com/en-us/power-platform/dataverse/mcp-server` (404)
  - `https://learn.microsoft.com/en-us/power-platform/dataverse/business-skills` (404)
  - `https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/app-mcp` (404)
  - `https://learn.microsoft.com/en-us/power-platform/dataverse/mcp-server-custom-tools` (404)
  - `https://learn.microsoft.com/en-us/power-apps/maker/data-platform/business-skills` (404)
  - `https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-custom-tools` (404)
- **No public Microsoft sample of a custom MCP tool with a widget attached** was located in either `microsoft/PowerApps-Samples` or `microsoft/Dataverse-MCP` as of commit SHAs captured in `SOURCE.md`. Per directive, this is **deferred to the `mcp-apps/` curation**.
- The mention of "custom APIs as MCP tools" in the admin doc ("MCP allow listing applies only to the `/api/mcp` agent entrypoint. MCP‑named custom APIs are regular Dataverse APIs and aren't restricted by this setting.") suggests that the App MCP / custom tool pattern is implemented as **MCP-named custom Dataverse APIs** — but no example was found at curation time.
