---
source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-preview-tools
fetched: 2026-05-14
ms_date_on_page: 2026-01-06
gitcommit: https://github.com/MicrosoftDocs/powerapps-docs-pr/blob/99742472c86e2b46f6c7021db3c42bf3851b1dd6/powerapps-docs/maker/data-platform/data-platform-mcp-preview-tools.md
---

# Enable preview tools and features in Dataverse MCP server - Power Apps | Microsoft Learn

To help customers experiment with new capabilities and provide early feedback, the Microsoft Dataverse model context protocol (MCP) server includes preview tools that enable upcoming features before they're generally available (GA).

This article explains what preview tools are, what to expect when you use them, and how administrators can turn on the preview features for Dataverse MCP server setting from the Power Platform admin center to access the latest Dataverse MCP server enhancements.

## What are preview tools?

Preview tools are early versions of Dataverse MCP server capabilities released for customer evaluation. They allow makers, developers, and system integrators to:

- Try out new API endpoints and behaviors before GA.
- Validate integration scenarios.
- Provide feedback that shapes final product design.
- Prepare internal systems for upcoming releases.

Preview tools might not yet support full functionality, scale, or GA features.

## Prerequisites

The environment must be enabled and configured for Dataverse MCP server. More information: [Configure the Dataverse MCP server for an environment](data-platform-mcp-disable.md)

## Enable preview features for Dataverse MCP server

Administrators can enable preview features using feature settings available in the Power Platform admin center. Once enabled, all users and copilots in the environment gain access to the new tools.

1. Sign in to the [Power Platform admin center](https://admin.powerplatform.microsoft.com/).
2. Select **Manage** > **Environments**.
3. Open the **Environment** where you want to turn on the Dataverse MCP server, and then select **Settings** on the command bar.
4. Expand **Product**, and then select **Features**.
5. Scroll down to locate **Dataverse Model Context Protocol** and enable **Allow MCP clients to interact with Dataverse MCP server (Preview version)**.

The environment might take a few minutes to apply the changes.

## Connect to Dataverse MCP server (preview)

### Connect from Microsoft Copilot Studio

When adding a connector in Copilot Studio, select the connector named **Microsoft Dataverse MCP Server (Preview)**.

This connector is different from the standard **Microsoft Dataverse MCP Server** connector and is required specifically for preview features.

### Connect using Claude, GitHub, or other non-Microsoft MCP clients

Use the preview endpoint `https://<orgUrl>/api/mcp_preview`.

Examples:

- `https://contoso.crm.dynamics.com/api/mcp_preview`
- `https://org123.crm4.dynamics.com/api/mcp_preview`

This endpoint makes available all preview tools and capabilities.

## What happens after enabling preview for Dataverse MCP server

Once preview is enabled:

- MCP Server makes available the corresponding preview tools automatically.
- Agents and Copilots can begin calling these new endpoints immediately.
- You might notice additional system-generated logs or messages marked as **Preview**.
- Behavior might change as Microsoft iterates based on feedback.

If you encounter any issues, you can disable the **Allow MCP clients to interact with Dataverse MCP server (Preview version)** environment setting at any time.

## About preview tools

| Topic | Details |
| --- | --- |
| Support | Preview tools aren't covered by Microsoft support agreements. |
| Breaking changes | Preview APIs might change without notice. |
| Performance | Features might not meet production-grade reliability. |
