# Work IQ MCP Tool Catalog (Synthesized)

> **Provenance**: Compiled from Microsoft Learn `mcp-server-reference/*` pages on 2026-05-14. See `SOURCE.md` for per-server URLs. Snapshots of full reference pages live in `docs/`.
>
> **Preview notice**: All Work IQ MCP servers below are in **public preview** as of 2026-05-14. Subject to supplemental terms of use.
>
> **Licensing**: Every Work IQ MCP server requires a Microsoft 365 Copilot license per consuming user.

## Remote server URL pattern

Work IQ MCP servers are hosted by Microsoft as remote streamable HTTP MCP servers (not stdio). Endpoint shape:

```
https://agent365.svc.cloud.microsoft/agents/tenants/{tenantId}/servers/{serverId}
```

Where `{serverId}` is the catalog server ID (column 1 in the table below).

For local AI assistants (Claude Code, GitHub Copilot CLI, VS Code), connect via `.mcp.json` with `"type": "http"` and OAuth. See `docs/work-iq-mcp-overview.md` for full configuration examples.

## Catalog

| Server ID | Display name | Scope | What it does |
|---|---|---|---|
| `mcp_M365Copilot` | Work IQ Copilot | (omnibus) | Delegates to M365 Copilot for retrieval, search, and chat synthesis across the entire tenant — semantic index + grounding |
| `mcp_MailTools` | Work IQ Mail | mail | Microsoft Graph mail tools: create/update/delete messages, send/reply/replyAll, sendDraft, searchMessages, listSent |
| `mcp_CalendarTools` | Work IQ Calendar | calendar | Graph calendar tools: createEvent, updateEvent, deleteEvent, getEvent, listEvents, listCalendarView, acceptEvent, declineEvent, cancelEvent, findMeetingTimes, getSchedule (free/busy) |
| `mcp_TeamsServer` | Work IQ Teams | `McpServers.Teams.All` | Graph Teams tools: chat + channel + message CRUD; member management; chat lifecycle |
| `mcp_SharePoint` (probable; verify in catalog) | Work IQ SharePoint | sharepoint | Upload files, get metadata, search, manage lists *(server ID unverified — reference page not snapshotted)* |
| `mcp_OneDrive` (probable; verify in catalog) | Work IQ OneDrive | onedrive | Manage files/folders in user's personal OneDrive *(server ID unverified — reference page not snapshotted)* |
| `mcp_User` (probable; verify in catalog) | Work IQ User | me | Get manager, direct reports, profile info; search users *(server ID unverified — reference page not snapshotted)* |
| `mcp_Word` (probable; verify in catalog) | Work IQ Word | word | Create and read documents; add/reply comments *(server ID unverified — reference page not snapshotted)* |
| `mcp_Dataverse` (probable; verify in catalog) | Dataverse and Dynamics 365 | dataverse | CRUD + domain-specific actions on Dataverse / D365 entities *(server ID unverified — reference page not snapshotted)* |

> **GAP**: Server IDs marked *(probable; verify in catalog)* are inferred from the Agent 365 tools catalog naming convention (`mcp_*`). The reference pages at `mcp-server-reference/{sharepoint,onedrive,me,word,dataverse}` were not fetched in this batch. Confirm exact server IDs from those pages on next refresh.

## Detail: Work IQ Copilot (`mcp_M365Copilot`)

The omnibus tool. The catalog directive: *"If the user's request could plausibly be answered using organizational content, you **must** invoke this tool unless a workload-specific tool exists for that scenario."*

Has exactly **one tool**: **Copilot Chat** (a.k.a. `copilot_chat`).

| Parameter | Required? | Description |
|---|---|---|
| `message` | yes | The chat message text from the user to send to Copilot |
| `conversationId` | no | ID of the existing M365 Copilot conversation (autocreated if missing) |
| `agentId` | no | Agent ID for the M365 Copilot conversation (only sent when provided) |
| `fileUris` | no | Array of file references with URI property to ground the response (SharePoint, OneDrive, etc.) |

**REST API equivalent**: The Microsoft 365 Copilot Chat API (preview) at `graph.microsoft.com/v1.0/copilot` exposes the same capability without going through MCP. See `docs/copilot-chat-api-overview.md`.

## Detail: Work IQ Mail (`mcp_MailTools`)

Full Graph mail surface as MCP tools. All tool names are `mcp_MailTools_graph_mail_<verb>`:

| Tool | Required params |
|---|---|
| `createMessage` | subject, toRecipients, body |
| `deleteMessage` | id |
| `getMessage` | id |
| `listSent` | (none — optional filter/search/orderby/top/select) |
| `reply` | id |
| `replyAll` | id |
| `searchMessages` | requests (array of search requests w/ entityTypes, query, from, size) |
| `sendDraft` | id |
| `sendMail` | message, subject, toRecipients, body |
| `updateMessage` | id |

Search uses Microsoft Graph Search API with KQL-style queries. HTML support via `preferHtml` or `body.body.contentType = "HTML"`.

## Detail: Work IQ Calendar (`mcp_CalendarTools`)

| Tool | Purpose |
|---|---|
| `mcp_CalendarTools_graph_acceptEvent` | Accept event invitation |
| `mcp_CalendarTools_graph_cancelEvent` | Cancel an event + notify attendees |
| `mcp_CalendarTools_graph_createEvent` | Create event (supports recurrence + online meetings) |
| `mcp_CalendarTools_graph_declineEvent` | Decline event invitation |
| `mcp_CalendarTools_graph_deleteEvent` | Delete an event |
| `mcp_CalendarTools_graph_findMeetingTimes` | Suggest meeting times based on attendee availability |
| `mcp_CalendarTools_graph_getEvent` | Get single event |
| `mcp_CalendarTools_graph_getSchedule` | Free/busy for users, DLs, resources |
| `mcp_CalendarTools_graph_listCalendarView` | Occurrences within a time range |
| `mcp_CalendarTools_graph_listEvents` | List events from user's calendar |
| `mcp_CalendarTools_graph_updateEvent` | Update existing event |

Notes:
- Calendar MCP relies on `UserprofileMCP` (Work IQ User) to resolve users in the org.
- All timestamps UTC/ISO 8601 with timezone.
- `onlineMeetingProvider`: `teamsForBusiness`, `skypeForBusiness`, `skypeForConsumer`.

## Detail: Work IQ Teams (`mcp_TeamsServer`)

Two surfaces: **chat tools** (DM/group chat) + **channel/team tools** (collaboration spaces).

Scope: `McpServers.Teams.All`.

### Chat tools (`mcp_graph_chat_*`)

| Tool | Endpoint |
|---|---|
| `addChatMember` | POST /v1.0/chats/{chat-id}/members |
| `createChat` | POST /v1.0/chats |
| `deleteChat` | DELETE /v1.0/chats/{chat-id} |
| `deleteChatMessage` | POST /v1.0/users/{user-id}/chats/{chat-id}/messages/{chatMessage-id}/softDelete |
| `getChat` | GET /v1.0/chats/{chat-id} |
| `getChatMessage` | GET /v1.0/chats/{chat-id}/messages/{message-id} |
| `listChatMembers` | GET /v1.0/chats/{chat-id}/members |
| `listChatMessages` | GET /v1.0/chats/{chat-id}/messages |
| `listChats` | GET /v1.0/chats |
| `postMessage` | POST /v1.0/chats/{chat-id}/messages |
| `updateChat` | PATCH /v1.0/chats/{chat-id} |
| `updateChatMessage` | PATCH /v1.0/chats/{chat-id}/messages/{message-id} |

### Channel/Team tools (`mcp_graph_teams_*`)

| Tool | Endpoint |
|---|---|
| `addChannelMember` | POST /v1.0/teams/{team-id}/channels/{channel-id}/members |
| `createChannel` | POST /v1.0/teams/{team-id}/channels |
| `createPrivateChannel` | POST /v1.0/teams/{team-id}/channels (with membershipType=private) |
| `getChannel` | GET /v1.0/teams/{team-id}/channels/{channel-id} |
| `getTeam` | GET /v1.0/teams/{team-id} |
| `listChannelMembers` | GET /v1.0/teams/{team-id}/channels/{channel-id}/members |
| `listChannelMessages` | GET /v1.0/teams/{team-id}/channels/{channel-id}/messages |
| `listChannels` | GET /v1.0/teams/{team-id}/allChannels |
| `listTeams` | GET /v1.0/users/{user-id}/joinedTeams |
| `postChannelMessage` | POST /v1.0/teams/{team-id}/channels/{channel-id}/messages |
| `replyToChannelMessage` | POST /v1.0/teams/{team-id}/channels/{channel-id}/messages/{message-id}/replies |
| `updateChannel` | PATCH /teams/{team-id}/channels/{channel-id} |
| `updateChannelMember` | PATCH /teams/{team-id}/channels/{channel-id}/members/{membership-id} |

Channel messages: plain text only via these tools. Compliance and tenant retention policies still apply.

## MCP Management Server (custom servers)

Server URL pattern:
```
https://agent365.svc.cloud.microsoft/mcp/environments/{environment ID}/servers/MCPManagement
```

Tools exposed (for building your own Work IQ MCP servers):

| Tool | Purpose |
|---|---|
| `CreateMCPServer` | Create a new MCP server instance |
| `CreateToolWithConnector` | Add a tool to your custom server backed by a connector, Graph API, REST endpoint, or Dataverse custom API |
| `UpdateTool` | Modify an existing tool |
| `DeleteMCPServer` | Remove an MCP server |
| `PublishMCPServer` | Publish a custom MCP server |

Integration sources for `CreateToolWithConnector`:
- 1,500+ connectors (ServiceNow, JIRA, etc.)
- Microsoft Graph APIs (Mail, Calendar, Teams)
- Dataverse custom APIs
- Arbitrary REST endpoints

> Currently only tenant admins can publish custom MCP servers within a tenant.

## Per-server permission scopes (Entra App Registration)

When registering an enterprise app to broker access to Work IQ MCP servers, add the per-server permission. Confirmed scope from docs:

| Server | Permission scope |
|---|---|
| Work IQ Mail | `WorkIQ-MailServer` |
| Work IQ Teams | `McpServers.Teams.All` |
| Work IQ Copilot | (scope name not snapshotted in this batch) |
| Other servers | (scope names not snapshotted in this batch — pattern is `WorkIQ-<Server>` or `McpServers.<Workload>.All`) |

> **GAP**: Full per-server permission scope list not captured. Refresh next cycle by fetching each `mcp-server-reference/*` page.
