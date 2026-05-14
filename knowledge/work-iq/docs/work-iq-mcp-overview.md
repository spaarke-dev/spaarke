---
source: https://learn.microsoft.com/en-us/microsoft-agent-365/tooling-servers-overview
fetched: 2026-05-14
ms_date: 2026-04-29
ms_updated_at: 2026-05-02
---

# Work IQ MCP overview (preview)

> **Preview**: Preview features aren't meant for production use and might have restricted functionality. Subject to supplemental terms of use.

Add Work IQ MCP to your agents to enhance what they can understand and do. Work IQ is the intelligence layer that grounds Microsoft 365 Copilot and your agents in real-time, shared context across your organization. It enables personalized search, advanced reasoning, and deeper semantic understanding by connecting signals across the Microsoft 365 ecosystem and your business systems.

Work IQ is built on three tightly integrated layers: Data, Memory, and Inference.

- **Data** unifies signals from files, emails, meetings, chats, and business systems across Microsoft 365 to capture how work happens across the organization.
- **Context** builds persistent understanding of how people and teams work, enabling Agent 365–managed agents to stay aligned to priorities and remain consistent across tasks, apps, and sessions.
- **Skills and Tools** brings together models, skills, and tools so agents can reason and take action by using Work IQ MCP tools, while the Agent 365 control plane ensures those actions remain observable, governed, and compliant.

You can get started enhancing your agents with Work IQ MCP tools by using any one of these supported clients:

- Microsoft 365 admin center
- Microsoft Copilot Studio
- Microsoft Foundry

> **Important**: You must have a Microsoft 365 Copilot license to use Work IQ MCP servers.

## Work IQ MCP key features

- **Centralized governance**: Admins manage MCP servers in the Microsoft 365 admin center.
- **Enterprise-grade security**: Scoped permissions, policy enforcement, and runtime observability.
- **Continuous evaluation**: All Work IQ MCP servers undergo rigorous testing for Accuracy, Latency, Reliability.
- **Integrated developer experience**: Tooling infrastructure is built into Microsoft Agent 365 SDK and CLI, Microsoft Foundry, and Copilot Studio.

## Agent 365 tools catalog

> **Note**: Existing connections using previous versions of Microsoft MCP servers (e.g., Microsoft Teams MCP server) remain supported. For all new connections, use the latest Work IQ MCP servers, such as Work IQ Teams.

Highlights include:

- **Work IQ Copilot** (`mcp-server-reference/searchtools`): Chat with Microsoft 365 Copilot, start a conversation, continue multi-turn threads, and ground responses with files.
- **Work IQ Calendar** (`mcp-server-reference/calendar`): Create, list, update, and delete events; accept and decline; resolve conflicts.
- **Work IQ Mail** (`mcp-server-reference/mail`): Create, update, and delete messages; reply and reply all; semantic search.
- **Work IQ SharePoint** (`mcp-server-reference/sharepoint`): Upload files; get metadata; search; manage lists.
- **Work IQ OneDrive** (`mcp-server-reference/onedrive`): Manage files and folders in user's personal OneDrive.
- **Work IQ Teams** (`mcp-server-reference/teams`): Create, update, and delete chat; add members; post messages; channel operations.
- **Work IQ User** (`mcp-server-reference/me`): Get manager, direct reports, and profile info; search users.
- **Work IQ Word** (`mcp-server-reference/word`): Create and read documents; add comments; reply to comments.
- **Dataverse and Dynamics 365** (`mcp-server-reference/dataverse`): CRUD operations and domain-specific actions.

## Security and compliance

- **Admin control**: Activate or block servers in Microsoft 365 admin center.
- **Scoped access**: Grant only the permissions agents need.
- **Observability**: Full tracing of tool calls for audits and troubleshooting (Microsoft Defender Advanced Hunting).
- **Policy enforcement**: Rate limits, payload checks, security scans at runtime.

Each MCP server corresponds to a permission on the Agent 365 application. When an agent is onboarded, the admin grants the required permissions. The agent gains access to the MCP server only after this consent.

## Developer experience

- Discover tools through the Agent 365 SDK or Copilot Studio.
- Integrate MCP servers into agent workflows via declarative manifests or SDK calls.
- Authenticate via agentic user identity or On-Behalf-Of (OBO) delegated user permissions.
- Test and validate with tracing enabled.

## Get started in Copilot Studio (Work IQ Mail example)

1. Sign in to Copilot Studio, select/create your agent.
2. Select **Tools** > **Add Tool**.
3. On *Add tool*, select **Model Context Protocol**.
4. Search for **mail**.
5. Select **Work IQ Mail**, expand connection dropdown, **Create New Connection**.
6. **Create**, provide credentials, sign in.
7. Test with: *"Send an email to [test address] and ask how the hands-on lab is going."*

## Get started in Microsoft Foundry

1. Create new agent at https://ai.azure.com — name e.g. *A365*, **Create**.
2. Configure: select model (e.g. GPT-4o), add instructions, select **Add** under Tools.
3. Filter tool catalog by **Provider > Microsoft**. Find **Microsoft 365 Frontier tools (User Profile, Outlook Calendar, Copilot Search)**.
4. Connect (e.g. Microsoft Outlook Calendar) — accept default MCP server endpoint + auth settings.
5. Test in Chat Playground.

## Custom MCP servers via MCP Management Server

The MCP Management server is itself an MCP server that exposes tools for creating, updating, and deleting MCP servers and tools:

- **CreateMCPServer**: Create a new MCP server instance.
- **CreateToolWithConnector**: Add connectors, Graph APIs, REST endpoints, or Dataverse custom APIs as tools.
- **UpdateTool**: Modify existing tools.
- **DeleteMCPServer**: Remove an MCP server.
- **PublishMCPServer**: Publish an MCP server.

Integration sources:

- 1,500+ connectors (ServiceNow, JIRA, etc.)
- Microsoft Graph APIs (Mail, Calendar, Teams)
- Dataverse custom APIs
- REST APIs for any HTTP endpoint

### Connect to MCP Management Server in VS Code

1. **Ctrl+Shift+P** (or **Cmd+Shift+P** on macOS) > **MCP: Add Server**.
2. Select **http** as server type.
3. Server URL — replace `{environment ID}` with your Power Platform environment ID:
   ```
   https://agent365.svc.cloud.microsoft/mcp/environments/{environment ID}/servers/MCPManagement
   ```
4. Name: `MCPManagement`. Scope: **Global**.
5. Sign in.

> **Note**: Currently, only tenant administrators can publish custom MCP servers within a tenant.

## Set up Work IQ MCP servers for coding agents

Connect to Work IQ MCP servers from GitHub Copilot CLI, Claude Code, and VS Code to access M365 work context while coding.

You must register an enterprise application as a client with proper permissions to access Work IQ MCP servers. Register via PowerShell script or directly in the Microsoft Entra admin center.

### Prerequisites

- Microsoft Entra user account with app registration permissions (admin consent might be required).
- Claude Code subscription + extension (for Claude Code usage).
- GitHub Copilot plan + CLI installed (for GitHub Copilot CLI usage).
- Microsoft 365 Copilot license.

### Register enterprise app in Microsoft Entra admin center

1. Sign in to https://entra.microsoft.com or via the Azure portal.
2. Select tenant. Select **App registrations**.
3. **New registration** if needed. Note **Application (client) ID** and **Directory (tenant) ID**.
4. **API permissions**: Add the Work IQ server permissions you want (e.g. `WorkIQ-MailServer` to enable Work IQ Mail). Consent.
5. **Authentication**: Add redirect URI under **Mobile and Desktop applications**: `http://localhost:8080/callback`.

Other valid redirect URIs:
- `ms-appx-web://Microsoft.AAD.BrokerPlugin/0f03d86d-f81f-4eda-8330-e852c8154ff3`
- `http://127.0.0.1`
- `http://vscode.dev/redirect`
- `https://localhost`

### Claude Code: .mcp.json example

```json
{
  "mcpServers": {
    "WorkIQ-MailServer": {
      "type": "http",
      "url": "https://agent365.svc.cloud.microsoft/agents/tenants/{tenantId}/servers/mcp_MailTools",
      "oauth": {
        "clientId": "{clientId}",
        "callbackPort": 8080
      }
    }
  }
}
```

Place in working directory. Run `/mcp` in Claude Code, authenticate, then prompt: *"What are the tools in WorkIQ-MailServer?"*

### GitHub Copilot CLI: .mcp.json example

Same `.mcp.json` shape as Claude Code. Requires GitHub Copilot CLI version 1.0.40 or later. CLI auto-detects `.mcp.json` and prompts for auth.

### VS Code: mcp.json example

```json
{
  "mcpservername": {
    "url": "https://agent365.svc.cloud.microsoft/agents/servers/<serverName>/tenants/<tenantId>",
    "type": "http",
    "oauth": {
      "clientId": "108fa535-fee5-4f75-9296-25231310626e"
    }
  }
}
```

Requires VS Code 1.118+ for MCP integration.

## Remote MCP server URL pattern

Work IQ MCP servers are hosted by Microsoft as remote streamable HTTP MCP servers. Endpoint shape:

```
https://agent365.svc.cloud.microsoft/agents/tenants/{tenantId}/servers/{serverId}
```

Where `{serverId}` is the catalog server ID (e.g. `mcp_MailTools`, `mcp_M365Copilot`, `mcp_CalendarTools`, `mcp_TeamsServer`).
