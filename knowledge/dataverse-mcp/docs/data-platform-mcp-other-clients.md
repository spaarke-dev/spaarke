---
source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-other-clients
fetched: 2026-05-14
ms_date_on_page: 2026-03-07
gitcommit: https://github.com/MicrosoftDocs/powerapps-docs-pr/blob/2b877061d14758aa314973907229f27ba8b61425/powerapps-docs/maker/data-platform/data-platform-mcp-other-clients.md
---

# Connect to Dataverse with model context protocol in non-Microsoft clients - Power Apps | Microsoft Learn

You can connect to Microsoft Dataverse using a non-Microsoft model context protocol (MCP) client, such as Claude desktop or Claude Code. There are two approaches for connecting non-Microsoft clients to a Dataverse MCP server:

- **Local proxy**: Use the `@microsoft/dataverse` npm package to run a local proxy that connects to the Dataverse MCP server on your behalf.
- **Remote endpoint**: Connect directly to the Dataverse MCP server remote endpoint (`/api/mcp`) by registering a custom Microsoft Entra app.

## Prerequisites

- The Dataverse MCP server must be enabled for the environment. More information: [Configure and manage the Dataverse MCP server for an environment](data-platform-mcp-disable.md)
- For the local proxy approach: [Node.js](https://nodejs.org/) (version 18 or later) installed on your machine.
- For the remote endpoint approach: Access to register an application in [Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app).

## Connect using the local proxy

The `@microsoft/dataverse` npm package provides a local proxy that handles authentication and communication with the Dataverse MCP server. This approach is recommended for most non-Microsoft MCP clients that can run local MCP servers.

### Grant tenant admin consent

A tenant administrator must grant admin consent for the Dataverse CLI app before users can authenticate. Navigate to the following URL in a browser, replacing `{your-tenant-id}` with your Microsoft Entra tenant ID:

`https://login.microsoftonline.com/{your-tenant-id}/adminconsent?client_id=0c412cc3-0dd6-449b-987f-05b053db9457`

Sign in with a tenant administrator account and accept the permissions prompt. This step only needs to be completed once per tenant.

### Enable the Dataverse CLI client in the Power Platform admin center

Before you can connect using the local proxy, the **Dataverse CLI** client must be enabled as an allowed MCP client in your environment.

1. Go to [Power Platform admin center](https://admin.powerplatform.microsoft.com/). Select **Manage** > **Environments**.
2. Select the environment where you want to enable the client, and then select **Settings**.
3. Under **Settings**, select **Product** > **Features**. Scroll down to locate **Dataverse Model Context Protocol** and select **Advanced Settings**.
4. Locate the **Dataverse CLI** client (app ID `0c412cc3-0dd6-449b-987f-05b053db9457`) and set **Is Enabled** to **Yes**.
5. Select **Save & Close**.

> **Note**
>
> If the **Dataverse CLI** entry doesn't appear in the list of available clients, you can add it manually. Create a new client entry with any name and specify the app ID `0c412cc3-0dd6-449b-987f-05b053db9457`, and then enable it.

### Install the local proxy

You can install the `@microsoft/dataverse` package globally or run it directly with `npx`.

To install globally, run the following command in a terminal:

```bash
npm install -g @microsoft/dataverse
```

Alternatively, you can use `npx` to run the proxy without installing it globally:

```bash
npx @microsoft/dataverse mcp https://yourorg.crm.dynamics.com
```

> **Tip**
>
> To connect to the preview endpoint (`/api/mcp_preview`) instead of the generally available endpoint (`/api/mcp`), add the `--preview` parameter to the command. For example: `npx @microsoft/dataverse mcp https://yourorg.crm.dynamics.com --preview`. The preview endpoint must be enabled in your environment. More information: [Use preview tools and upcoming features in Dataverse MCP server](data-platform-mcp-preview-tools.md)

### Configure the local proxy in Claude desktop

This section describes how to configure the Dataverse MCP server local proxy in Claude desktop.

1. Open Claude desktop and go to **File** > **Settings** > **Developer**.
2. Select **Edit Config** to open the `claude_desktop_config.json` file.
3. Add the following JSON snippet to the file. Replace `<friendly name>` with a name you can easily remember (for example, *MyDataverseMCPServer*) and replace `<your org URL>` with your Dataverse environment URL (for example, `https://contoso.crm.dynamics.com`).

   ```json
   {
     "mcpServers": {
       "<friendly name>": {
         "command": "npx",
         "args": [
           "-y",
           "@microsoft/dataverse",
           "mcp",
           "<your org URL>"
         ]
       }
     }
   }
   ```

4. Save the file.

#### Verify the connection in Claude desktop

1. Exit Claude desktop by selecting **File** > **Exit**, and then reopen it to apply the changes.
2. Sign in with your credentials when prompted to authenticate to your Dataverse environment.
3. Select **Search and tools** to verify that the Dataverse MCP server and its tools are available.

### Configure the local proxy in Claude Code

This section describes how to configure the Dataverse MCP server local proxy in Claude Code.

Run the following command to add the Dataverse MCP server. Replace `https://yourorg.crm.dynamics.com` with your Dataverse environment URL.

```bash
claude mcp add dataverse -t stdio -- npx -y @microsoft/dataverse mcp https://yourorg.crm.dynamics.com
```

### Verify and interact with the connection in Claude Code

1. Restart Claude Code to apply the changes.
2. Sign in with your credentials when prompted to authenticate to your Dataverse environment.
3. Verify that the Dataverse MCP server and its tools are available.

If you have data in the Dataverse environment, you can test the setup by asking *list tables in Dataverse*, *describe table account*, or *how many accounts do I have*.

> **Tip**
>
> If you have other MCP servers registered with Claude Code, include *Dataverse* in your prompt to specify which MCP server to use.

## Connect using the remote endpoint

You can connect non-Microsoft MCP clients directly to the Dataverse MCP server remote endpoint without using a local proxy. This approach requires you to register a custom application in Microsoft Entra ID and add its client ID to the allowed clients list in the Power Platform admin center.

### Register a custom Microsoft Entra app

Register an application in Microsoft Entra ID to use for authentication when connecting to the Dataverse MCP server.

1. Sign in to the [Microsoft Entra admin center](https://entra.microsoft.com/).
2. Go to **Identity** > **Applications** > **App registrations**, and then select **New registration**.
3. Enter a name for your application (for example, *Dataverse MCP Client*), configure the supported account types for your scenario, and then select **Register**.
4. On the **Overview** page, note the **Application (client) ID**.

### Configure API permissions for the Dataverse MCP server

After you register the app, you must grant it permissions to access the Dataverse MCP server.

1. In the app registration, select **API permissions** from the left navigation pane.
2. Select **Add a permission**.
3. Select **Microsoft APIs**, and then select **Dynamics CRM**.
4. Select the **mcp.tools** permission, and then select **Add permissions**.

> **Note**
>
> The authentication flow used by the Entra app depends on the MCP client you're using. Refer to your MCP client's documentation for the supported authentication methods.

### Add the custom app to the allowed clients list

After you register the Entra app, add its client ID to the list of allowed MCP clients for your environment (same path as the Dataverse CLI step above).

### Connect to the remote endpoint

Configure your MCP client to connect to the Dataverse MCP server at the following URL:

`https://<your org URL>/api/mcp`

Use the **Application (client) ID** from your Entra app registration for authentication.
