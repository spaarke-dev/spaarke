---
source: https://devblogs.microsoft.com/microsoft365dev/mcp-apps-now-available-in-copilot-chat/
fetched: 2026-05-14
---

# MCP Apps Now Available in Copilot Chat (Microsoft 365 Dev Blog)

## Overview

Microsoft has announced that MCP Apps and the OpenAI Apps SDK are now available for building interactive applications within Microsoft 365 Copilot chat. This enables agents to deliver rich, app-powered user interfaces directly in Copilot rather than forcing users to switch applications.

## Key Capabilities

Agents can now surface sophisticated UI components including:

- Tables and forms
- Diagrams and dashboards
- Maps and rich media
- Specialized creation surfaces
- All rendered securely in sandboxed iFrames

The platform supports two complementary display modes:

1. **Inline mode** (required) — lightweight widgets appearing before the model's response
2. **Side-by-side mode** (optional) — expanded workspace for complex workflows

## Launch Partners

Pre-built experiences are available from:

- Outlook (compose and scheduling)
- Power Apps (public preview)
- Adobe Express
- Coursera
- Figma
- monday.com

All experiences will be accessible via the Microsoft 365 Agent Store by mid-April.

## Getting Started for Developers

### Development Paths

1. **Microsoft 365 Agents Toolkit** in Visual Studio Code — select "Add an Action," choose "Start with an MCP Server," and provide the server URL.
2. **GitHub Copilot CLI skill** — uses composable skills for scaffolding, MCP development, authentication, and deployment in natural language.

### Authentication Support

- OAuth 2.1
- Microsoft Entra single sign-on (SSO)
- Anonymous authentication

## Sample Resources

Official quick-start samples:

1. **Field Service Dispatch** — assignment intake, map visualization, and dispatch planning.
   - [MCP Apps version](https://github.com/microsoft/mcp-interactiveUI-samples/blob/main/mcp-apps/fieldops/node/src/mcpserver/README.md)
   - [Apps SDK version](https://github.com/microsoft/mcp-interactiveUI-samples/blob/main/oai-apps-sdk/fieldops/node/README.md)
2. **Trey Research — HR Consultant Management** — HR dashboard, consultant profiles, and project details.
   - [MCP Apps version](https://github.com/microsoft/mcp-interactiveUI-samples/blob/main/mcp-apps/trey-research/node/src/mcpserver/README.md)
   - [Apps SDK version](https://github.com/microsoft/mcp-interactiveUI-samples/blob/main/oai-apps-sdk/trey-research/node/src/mcpserver/README.md)
3. **Employee Training** — learning recommendations with embedded video previews.
   - [MCP Apps version](https://github.com/microsoft/mcp-interactiveUI-samples/blob/main/mcp-apps/employee-training/node/README.md)

All samples available in the [mcp-interactiveUI-samples repository](https://github.com/microsoft/mcp-interactiveUI-samples).

## Publishing Options

- **Sideload** within your organization for testing
- **Deploy through Microsoft 365 Admin Center** for organizational distribution
- **Publish to Microsoft 365 Agent Store** for cross-tenant discovery via the Commercial Marketplace

## Developer Support

- [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/tags/466/microsoft-copilot-microsoft-365-copilot-development-routing)
- GitHub repositories: [M365 Agents Toolkit](https://github.com/OfficeDev/microsoft-365-agents-toolkit) and [InteractiveUI samples](https://github.com/microsoft/mcp-interactiveUI-samples)
- Reddit communities: r/copilotstudio and r/microsoft_365_copilot
