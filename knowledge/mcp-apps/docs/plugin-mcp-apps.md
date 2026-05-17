---
source: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-mcp-apps
fetched: 2026-05-14
---

# MCP apps in Microsoft 365 Copilot - Build interactive UI widgets

> Microsoft Learn page metadata: `ms.date: 2026-04-24`, `updated_at: 2026-05-12`, `git_commit_id: 11498941adeba0ecbe195450195efcd1320560d7`.

MCP apps are interactive UI widgets that run inside Microsoft 365 Copilot, powered by Model Context Protocol (MCP) servers. They allow declarative agents to go beyond text responses and deliver rich, actionable experiences directly in the Copilot chat. You can add MCP apps to your declarative agents by adding an [MCP server-based action](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/build-mcp-plugins) to your agent and extending the MCP tools used by the agent to include UI. Microsoft 365 Copilot supports UI widgets created using the following methods.

- [MCP Apps](https://modelcontextprotocol.github.io/ext-apps/api/documents/Overview.html) - an extension to MCP that enables MCP servers to deliver interactive user interfaces to hosts.
- [OpenAI Apps SDK](https://developers.openai.com/apps-sdk) - tools to build ChatGPT apps based on the MCP Apps standard with extra ChatGPT functionality.

For example MCP server plugins, see [MCP based interactive UI samples for Microsoft 365 Copilot](https://github.com/microsoft/mcp-interactiveUI-samples) on GitHub.

For details on which MCP Apps or OpenAI Apps SDK capabilities are supported, see Supported MCP Apps capabilities in Copilot.

## Prerequisites for MCP apps

- Requirements specified in [Requirements for Copilot extensibility options](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/prerequisites#requirements-for-copilot-extensibility-options)
- A remote MCP server that provides UI widgets or that you can modify to implement UI widgets
- A tool to view MCP server responses, such as [MCP Inspector](https://www.npmjs.com/package/@modelcontextprotocol/inspector)
- [Visual Studio Code](https://code.visualstudio.com/)
- [Microsoft 365 Agents Toolkit](https://marketplace.visualstudio.com/items?itemName=TeamsDevApp.ms-teams-vscode-extension) (version 6.6.1 or later)

## MCP server requirements for MCP apps

- **Authentication** - OAuth 2.1 and Microsoft Entra single sign-on (SSO) are supported. Anonymous authentication is supported for development purposes.
- **Allowed URLs** - the following URLs should be allowed by both your MCP server and your identity provider.
    - **Widget host URL for CORS** - Copilot renders widget UI under an MCP server-specific host with the following URL: `{hashed-mcp-domain}.widget-renderer.usercontent.microsoft.com`, where `{hashed-mcp-domain}` is the SHA-256 hash of your MCP server's domain. You can use the [Widget Host URL Generator](https://aka.ms/mcpwidgeturlgenerator) to generate the host URL based on your MCP server URL.
    - **OAuth 2.1 redirect URIs**:
        - `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` for Copilot
        - `https://vscode.dev/redirect` for Visual Studio Code to fetch tools using the Agents Toolkit
    - **Microsoft Entra SSO redirect URIs**:
        - `https://teams.microsoft.com/api/platform/v1.0/oAuthConsentRedirect` for Copilot
        - Visual Studio Code doesn't currently support SSO for fetching tools
- **UI widgets** - UI widgets must be implemented according to the MCP Apps or OpenAI Apps SDK requirements.

## Best practices for MCP apps in Copilot

### User experience design

See [User experience guidelines for MCP apps in declarative agents for Microsoft 365 Copilot](plugin-mcp-apps-ui-guidelines.md).

### Verify API availability

Not all `window.openai.*` APIs are available on every platform or host. APIs that are unsupported are `undefined`. Always check API availability and provide a fallback if the API is unavailable.

```typescript
if (window.openai.callTool) {
  const result = await window.openai.callTool({ name: 'myTool', params: {} });
} else {
  // Handle unsupported case — show fallback UI, skip the feature, etc.
}
```

```typescript
function FullScreenButton() {
  // Don't render the button if the host doesn't support it
  if (!window.openai.requestDisplayMode) {
    return null;
  }

  return (
    <button onClick={() => window.openai.requestDisplayMode({ mode: 'fullscreen' })}>
      Enter Fullscreen
    </button>
  );
}
```

```typescript
interface PlatformCapabilities {
  canCallTools: boolean;
  canChangeDisplayMode: boolean;
  canSendMessages: boolean;
}

function detectCapabilities(): PlatformCapabilities {
  return {
    canCallTools: !!window.openai.callTool,
    canChangeDisplayMode: !!window.openai.requestDisplayMode,
    canSendMessages: !!window.openai.sendMessage,
  };
}

// Use at widget startup
const capabilities = detectCapabilities();

if (!capabilities.canCallTools) {
  // Show a reduced-functionality experience
}
```

## Create a declarative agent

1. Open Visual Studio Code and select the **Microsoft 365 Agents Toolkit** icon in the left-hand Activity Bar.
2. Select **Create a New Agent/App** in the Agents Toolkit task pane.
3. Select **Declarative Agent**.
4. Select **Add an Action**, then select **Start with an MCP Server**. If prompted, choose **Remote MCP server**.
5. Enter URL to your MCP server.
6. Choose a location for the agent project.
7. Enter a name for the agent.

### Update and sideload the agent

1. Open the `.vscode/mcp.json` file. Select the **Start** button in the file editor.
2. Select the **ATK: Fetch action from MCP** button in the file editor, then select **ai-plugin.json**.
3. Select the tools for the agent to use and select **OK**. Be sure to select at least one tool that has a UI widget.
4. Select the applicable authentication type.
5. Select the **Microsoft 365 Agents Toolkit** icon in the left-hand Activity Bar.
6. In the **Accounts** pane, select **Sign in to Microsoft 365**.
7. Confirm that both **Custom App Upload Enabled** and **Copilot Access Enabled** display under your Microsoft 365 account.
8. In the **Lifecycle** pane, select **Provision**.
9. If prompted, add your authentication details.
10. Wait for the toolkit to report that it finishes provisioning.

### Test the agent

1. Open your browser and go to https://m365.cloud.microsoft/chat.
2. Select your agent in the left-hand sidebar.
3. Ask the agent to do something that invokes your MCP server.
4. Allow the agent to connect to the MCP server when prompted.
5. The agent renders the UI widget.

## Supported MCP Apps capabilities in Copilot

### Component bridge

| OpenAI Apps SDK | MCP Apps equivalent | Supported? |
| --- | --- | --- |
| `window.openai.toolInput` | `app.ontoolinput` | ✅ |
| `window.openai.toolOutput` | `app.ontoolresult` | ✅ |
| `window.openai.toolResponseMetadata` | `app.ontoolresult` → `params._meta` | ✅ |
| `window.openai.widgetState` | — | ✅ |
| `window.openai.setWidgetState(state)` | Not directly available. Use alternative mechanisms including `app.updateModelContext()` | ✅ |
| `window.openai.callTool(name, args)` | `app.callServerTool({ name, arguments })` | ✅ |
| `window.openai.sendFollowUpMessage({ prompt })` | `app.sendMessage({ ... })` | ✅ |
| `window.openai.uploadFile(file)` | — | ❌ |
| `window.openai.getFileDownloadUrl({ fileId })` | — | ❌ |
| `window.openai.requestDisplayMode(...)` | `app.requestDisplayMode({ mode })` | ✅ (full screen only) |
| `window.openai.requestModal(...)` | — | ❌ |
| `window.openai.notifyIntrinsicHeight(...)` | `app.sendSizeChanged({ width, height })` | ✅ |
| `window.openai.openExternal({ href })` | `app.openLink({ url })` | ✅ |
| `window.openai.setOpenInAppUrl({ href })` | — | ✅ |
| `window.openai.theme` | `app.getHostContext()?.theme` | ✅ |
| `window.openai.displayMode` | `app.getHostContext()?.displayMode` | ✅ |
| `window.openai.maxHeight` | `app.getHostContext()?.viewport?.maxHeight` | ✅ |
| `window.openai.safeArea` | `app.getHostContext()?.safeAreaInsets` | ✅ |
| `window.openai.view` | — | ✅ |
| `window.openai.userAgent` | `app.getHostContext()?.userAgent` | ✅ |
| `window.openai.locale` | `app.getHostContext()?.locale` | ✅ |
| — | `app.ontoolinputpartial` | ❌ |
| — | `app.ontoolcancelled` | ❌ |
| — | `app.getHostContext()?.availableDisplayModes` | ❌ |
| — | `app.getHostContext()?.toolInfo` | ❌ |
| — | `app.onhostcontextchanged` | ❌ |
| — | `app.onteardown` | ❌ |
| — | `app.sendLog({ level, data })` | ❌ |
| — | `app.getHostVersion()` | ❌ |
| — | `app.getHostCapabilities()` | ✅ |

### Tool descriptor _meta fields

| OpenAI Apps SDK | MCP Apps equivalent | Supported? |
| --- | --- | --- |
| `_meta["openai/outputTemplate"]` | `_meta.ui.resourceUri` | ✅ |
| `_meta["openai/widgetAccessible"]` | `_meta.ui.visibility` (string[]) | ❌ |
| `_meta["openai/visibility"]` | `_meta.ui.visibility` (string[]) | ✅ |
| `_meta["openai/toolInvocation/invoking"]` | — | ❌ |
| `_meta["openai/toolInvocation/invoked"]` | — | ❌ |
| `_meta["openai/fileParams"]` | — | ❌ |
| `_meta["securitySchemes"]` | — | ❌ |

### Tool descriptor annotations

| OpenAI Apps SDK | MCP Apps equivalent | Supported? |
| --- | --- | --- |
| `readOnlyHint` | `readOnlyHint` | ✅ |
| `destructiveHint` | `destructiveHint` | ❌ |
| `openWorldHint` | `openWorldHint` | ❌ |
| `idempotentHint` | `idempotentHint` | ❌ |

### Component resource _meta fields

| OpenAI Apps SDK | MCP Apps equivalent | Supported? |
| --- | --- | --- |
| `_meta["openai/widgetDescription"]` | — | ❌ |
| `_meta["openai/widgetPrefersBorder"]` | `_meta.ui.prefersBorder` | ❌ |
| `_meta["openai/widgetCSP"]` | `_meta.ui.csp` | ✅ |
| `_meta["openai/widgetDomain"]` | `_meta.ui.domain` | ❌ |
| — | `_meta.ui.permissions` | ❌ |

### Properties in CSP object

| OpenAI Apps SDK | MCP Apps equivalent | Supported? |
| --- | --- | --- |
| `connect_domains` | `connectDomains` | ✅ |
| `resource_domains` | `resourceDomains` | ✅ |
| `frame_domains` | `frameDomains` | ❌ |
| `redirect_domains` | — | ❌ |
| — | `baseUriDomains` | ❌ |

### Host-provided tool result _meta fields

| OpenAI Apps SDK | MCP Apps equivalent | Supported? |
| --- | --- | --- |
| `_meta["openai/widgetSessionId"]` | — | ❌ |

### Client-provided _meta fields

| OpenAI Apps SDK | MCP Apps equivalent | Supported? |
| --- | --- | --- |
| `_meta["openai/locale"]` | `_meta["openai/locale"]` | ✅ |
| `_meta["openai/userAgent"]` | `_meta["openai/userAgent"]` | ✅ |
| `_meta["openai/userLocation"]` | `_meta["openai/userLocation"]` | ✅ |
| `_meta["openai/subject"]` | — | ❌ |

## FAQ

### What are MCP apps?

MCP apps are interactive UI widgets delivered by MCP servers that render directly inside Microsoft 365 Copilot. They extend declarative agents beyond text-only responses, enabling rich experiences like data visualizations, forms, and task management interfaces.

### What is the difference between MCP Apps and OpenAI Apps SDK?

[MCP Apps](https://modelcontextprotocol.github.io/ext-apps/api/documents/Overview.html) is an open extension to the MCP standard that enables MCP servers to deliver interactive UIs to any compatible host. The [OpenAI Apps SDK](https://developers.openai.com/apps-sdk) builds on the MCP Apps standard and adds extra functionality specific to ChatGPT. Microsoft 365 Copilot supports both, though not all capabilities are available.

### Can I use MCP apps without authentication during development?

Yes. Anonymous authentication is supported for development purposes. However, you need to add authentication before deploying to production. OAuth 2.1 and Microsoft Entra single sign-on (SSO) are the supported authentication methods.
