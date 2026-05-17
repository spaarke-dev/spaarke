---
source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-vscode
fetched: 2026-05-14
ms_date_on_page: 2026-03-09
gitcommit: https://github.com/MicrosoftDocs/powerapps-docs-pr/blob/e49fd06a45649d02dd8af850bcdc72dd2b5e3e0a/powerapps-docs/maker/data-platform/data-platform-mcp-vscode.md
---

# Connect Dataverse MCP with GitHub Copilot in Visual Studio Code and Copilot CLI - Power Apps | Microsoft Learn

This article explains how to set up and use the Microsoft Dataverse model context protocol (MCP) server with GitHub Copilot in Visual Studio Code and GitHub Copilot CLI.

## GitHub Copilot in Visual Studio Code

### Prerequisites

- The **Microsoft GitHub Copilot** MCP client must be allowed in the environment. More information: [Configure and manage the Dataverse MCP server for an environment](data-platform-mcp-disable.md)
- Visual Studio Code installed with GitHub Copilot extension. More information: [GitHub Copilot extension for Visual Studio Code](https://marketplace.visualstudio.com/items?itemName=GitHub.copilot)

### Steps to connect to Dataverse MCP server in Visual Studio Code

1. Open Visual Studio Code. Select **View** > **Command Palette** (Ctrl+Shift+P), type **MCP: Add Server**, and then select **HTTP or Server Sent Events**.
2. Paste your instance URL, such as `https://contoso.crm.dynamics.com/`, append `/api/mcp` to it, and press Enter. You can get the instance URL at make.powerapps.com > **Settings** (gear icon) > **Session details** > **Instance url**.

   This step generates the MCP server configuration in Visual Studio Code.
3. Type a MCP server name or press Enter to accept the default name.
4. Choose **Global** or **workspace**.
5. Press **Ctrl+Alt+I** and ensure that agent mode is selected.

## GitHub Copilot CLI

### Prerequisites

- GitHub Copilot CLI installed. More information: [GitHub Copilot CLI](https://docs.github.com/en/copilot/github-copilot-in-the-cli)
- The **Microsoft GitHub Copilot** MCP client must be allowed in the environment.

### Option 1: Manually add the MCP server

You can configure the Dataverse MCP server in GitHub Copilot CLI by editing the MCP configuration file directly.

1. Open your MCP configuration file. For global configuration, edit `~/.copilot/mcp-config.json`. For project-scoped configuration, edit `.mcp/copilot/mcp.json` in your project directory.
2. Add the following JSON snippet. Replace `<your org URL>` with your Dataverse environment URL (for example, `https://contoso.crm.dynamics.com`).

   ```json
   {
     "mcpServers": {
       "DataverseMcp": {
         "type": "http",
         "url": "<your org URL>/api/mcp"
       }
     }
   }
   ```

3. Save the file and restart GitHub Copilot CLI for the changes to take effect.

### Option 2: Use the Dataverse plugin from the Awesome Copilot marketplace

The [Awesome Copilot](https://github.com/github/awesome-copilot) marketplace provides a Dataverse plugin that includes an `mcp-configure` skill. This skill guides you through configuring the Dataverse MCP server interactively, including environment discovery and endpoint selection.

1. Add the Awesome Copilot marketplace to your Copilot CLI:

   ```bash
   copilot plugin marketplace add github/awesome-copilot
   ```

2. Install the Dataverse plugin:

   ```bash
   copilot plugin install dataverse@awesome-copilot
   ```

3. In a Copilot chat session, use the `/dataverse:mcp-configure` skill to configure the Dataverse MCP server. The skill walks you through selecting your environment and choosing between the generally available (`/api/mcp`) and preview (`/api/mcp_preview`) endpoints.
