---
source: https://learn.microsoft.com/en-us/microsoft-agent-365/mcp-server-reference/searchtools
fetched: 2026-05-14
ms_date: 2026-04-28
ms_updated_at: 2026-05-01
---

# Work IQ Copilot reference (preview)

> **Preview**: Preview features aren't meant for production use and might have restricted functionality.

This is the omnibus Work IQ MCP server — the one that delegates to M365 Copilot itself (semantic index + retrieval pipeline + chat synthesis) rather than calling a workload Graph API directly.

## Overview

| Server ID | Display name | Description |
| --- | --- | --- |
| `mcp_M365Copilot` | Work IQ Copilot | Use this tool for any user request that might require finding, searching, discovering, or locating information contained within Microsoft 365 content—including documents, PDFs, spreadsheets, emails, sites, reports, or files—regardless of whether the question appears general or domain-specific. If the user's request could plausibly be answered using organizational content, you **must** invoke this tool unless a workload-specific tool exists for that scenario. When no dedicated tool (mail, calendar, Teams, OneDrive, SharePoint, etc.) is available, this tool becomes the primary mechanism for retrieval. If the request mentions a specific workload and its tool is unavailable, you might use this search tool as a fallback. If the search tool can't retrieve the information, clearly state that the information isn't accessible from the available tools instead of answering from general knowledge. |

> **Note**: Existing connections that use previous versions of Microsoft MCP servers, such as Microsoft Teams MCP server, remain supported. For all new connections, use the latest Work IQ MCP servers, such as Work IQ Teams.

## Available tools

### Copilot Chat

> This is the **`copilot_chat`** tool — the omnibus tool that delegates to M365 Copilot. The "Required/Optional parameters" below define its full input contract.

**Description**: Search across your Microsoft 365 ecosystem—including SharePoint sites, OneDrive files, email messages, Teams chats, and other connected Microsoft 365 content—to locate information stored within your organization.

This tool enables discovery of documents, PDFs, spreadsheets, presentations, emails, sites, and other enterprise content by querying your Microsoft 365 data sources. Use this tool whenever the user's request might involve information that could exist inside Microsoft 365, or when more organizational context might be helpful beyond general knowledge.

If a workload-specific tool (mail, calendar, Teams, etc.) isn't available for a request that references that workload, this search tool might be used as a fallback to attempt retrieval. If a `conversationId` is already provided, the tool continues the same thread; otherwise, a new conversation is created automatically.

**Required parameters**:

- **`message`** — The chat message text from the user to send to Copilot

**Optional parameters**:

- **`conversationId`** — ID of the existing Microsoft 365 Copilot conversation (autocreated if missing)
- **`agentId`** — Agent ID for the Microsoft 365 Copilot conversation (only sent when provided)
- **`fileUris`** — Array of file references with URI property to ground the response (SharePoint, OneDrive, etc.)

## Key features

### Microsoft 365 Copilot integration

- Direct chat integration with Microsoft 365 Copilot
- Multi-turn conversation support
- Persistent conversation IDs for context retention

### Contextual chat

- Location and time zone context support
- File grounding for contextual responses
- Attach files as contextual resources

### Conversation management

- Start new conversations
- Continue existing conversations with `conversationId`
- Agent-specific conversations

## Use cases

- Searching for documents, PDFs, spreadsheets, and presentations across Microsoft 365
- Finding information in emails, Teams chats, and SharePoint sites
- Discovering organizational content when the question might be domain-specific
- Fallback search when workload-specific tools are unavailable
- Grounding responses with specific file URIs from SharePoint or OneDrive
- Maintaining conversation context with `conversationId` for multi-turn interactions
