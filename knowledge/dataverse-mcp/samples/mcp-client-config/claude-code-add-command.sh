#!/usr/bin/env bash
# Source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-other-clients
# (fetched 2026-05-14) - "Configure the local proxy in Claude Code" section.
#
# Registers the Dataverse MCP server with Claude Code via the @microsoft/dataverse local proxy.
# Replace https://yourorg.crm.dynamics.com with your Dataverse environment URL.
#
# Prerequisites (all documented in docs/data-platform-mcp-other-clients.md):
#   1. Tenant admin has consented to the Dataverse CLI app (client_id 0c412cc3-0dd6-449b-987f-05b053db9457).
#   2. Dataverse CLI client is enabled in Power Platform admin center -> Environment -> Settings ->
#      Product -> Features -> Dataverse Model Context Protocol -> Advanced Settings.
#   3. Node.js >= 18 installed.

claude mcp add dataverse -t stdio -- npx -y @microsoft/dataverse mcp https://yourorg.crm.dynamics.com

# To target the preview endpoint (required for Business Skills / Dataverse intelligence), add --preview:
# claude mcp add dataverse-preview -t stdio -- npx -y @microsoft/dataverse mcp https://yourorg.crm.dynamics.com --preview
