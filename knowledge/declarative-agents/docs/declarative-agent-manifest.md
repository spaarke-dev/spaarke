---
source: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-manifest
fetched: 2026-05-14
---

# Declarative agent schema 1.6 for Microsoft 365 Copilot

This article describes the 1.6 schema used by the declarative agent manifest. The manifest is a machine-readable document that provides a Large Language Model (LLM) with the necessary instructions, knowledge, and actions to specialize in addressing a select set of user problems. Microsoft 365 app manifest references declarative agent manifests inside an [app package](agents-are-apps#app-package).

> **Important**: The latest version of the declarative agent manifest schema is **version 1.7**. Use the latest schema version for new agents.

## Changes from previous version

Schema 1.6 introduces the following changes from version 1.5:

- Added the optional `sensitivity_label` property to specify Purview sensitivity labels for the agent, **only when the agent has embedded files**.
- Added the optional `worker_agents` property to specify other declarative agents that can be used by this agent.
- Added the optional `user_overrides` property to specify configured capabilities that the agent user can modify.
- Added the embedded knowledge capability, allowing agents to use local files as knowledge.
- Added the `items_by_id` property to the meetings object, allowing agent creators to limit the meetings available to the agent.
- Added the `include_related_content` property to the people object.
- Added the `group_mailboxes` property to the email object.

## JSON schema

`https://developer.microsoft.com/json-schemas/copilot/declarative-agent/v1.6/schema.json`

## Conventions

### Relative references in URLs

Unless specified otherwise, all properties that are URLs can be relative references. Relative references are relative to the location of the manifest document.

### String length

Unless specified otherwise, limit all string properties to 4,000 characters.

### Unrecognized properties

Unrecognized or extraneous properties in any JSON object make the entire document invalid.

### String localization

Localizable strings can use a localization key instead of a literal value: `[[key_name]]`, where `key_name` is the key name in the `localizationKeys` property in your localization files.

## Declarative agent manifest object

| Property | Type | Description |
| --- | --- | --- |
| `version` | String | Required. The schema version. Set to `v1.6`. |
| `id` | String | Optional. An identifier for the manifest. |
| `name` | String | Required. Localizable. The name of the declarative agent. Must contain at least one nonwhitespace character and be 100 characters or less. |
| `description` | String | Required. Localizable. Must contain at least one nonwhitespace character and be 1,000 characters or less. |
| `instructions` | String | Required. Detailed instructions or guidelines on how the agent should behave, its functions, and any behaviors to avoid. Must contain at least one nonwhitespace character and be 8,000 characters or less. |
| `capabilities` | Array of Capabilities object | Optional. The array can't contain more than one of each derived type of Capabilities object. |
| `conversation_starters` | Array of Conversation starter object | Optional. Title and Text are localizable. The array can't contain more than 12 objects. |
| `actions` | Array of Action object | Optional. A list of 1-10 objects that identify [plugins](plugin-manifest-2.4) that provide actions accessible to the declarative agent. |
| `behavior_overrides` | Behavior overrides object | Optional. Contains configuration settings that modify the behavior of the agent. |
| `disclaimer` | Disclaimer object | Optional. Disclaimer text displayed to the user at the start of a conversation. |
| `sensitivity_label` | Sensitivity label object | Optional. Specifies a Microsoft Purview sensitivity label for the agent. |
| `worker_agents` | Array of Worker agent object | Optional. Other declarative agents that can be used by this agent. |
| `user_overrides` | Array of User override object | Optional. Capabilities the user can modify via UI toggles. |

### Example: required fields

```json
{
  "version": "v1.6",
  "name": "Repairs agent",
  "description": "This declarative agent is meant to help track any tickets and repairs",
  "instructions": "This declarative agent needs to look at my Service Now and Jira tickets/instances to help me keep track of open items"
}
```

## Capabilities

The capabilities object is the base type for objects in the `capabilities` property. The possible derived types are:

- Web search
- OneDrive and SharePoint
- Copilot connectors (GraphConnectors)
- Graphic art
- Code interpreter
- Dataverse
- Microsoft Teams messages
- Email
- People
- Scenario models
- Meetings
- Embedded knowledge

> **Note**: Users can access declarative agents with any capabilities other than Web search only if their tenants allow metered usage or if they have a Microsoft 365 Copilot license.

### Capabilities example (all types)

```json
{
  "capabilities": [
    {
      "name": "WebSearch",
      "sites": [
        { "url": "https://contoso.com" }
      ]
    },
    {
      "name": "OneDriveAndSharePoint",
      "items_by_sharepoint_ids": [
        {
          "site_id": "bc54a8cc-8c2e-4e62-99cf-660b3594bbfd",
          "web_id": "a5377427-f041-49b5-a2e9-0d58f4343939",
          "list_id": "78A4158C-D2E0-4708-A07D-EE751111E462",
          "unique_id": "304fcfdf-8842-434d-a56f-44a1e54fbed2"
        }
      ],
      "items_by_url": [
        { "url": "https://contoso.sharepoint.com/teams/admins/Documents/Folders1" }
      ]
    },
    {
      "name": "GraphConnectors",
      "connections": [
        { "connection_id": "jiraTickets" }
      ]
    },
    { "name": "GraphicArt" },
    { "name": "CodeInterpreter" },
    {
      "name": "Dataverse",
      "knowledge_sources": [
        {
          "host_name": "organization.crm.dynamics.com",
          "skill": "DVCopilotSkillName",
          "tables": [
            { "table_name": "account" },
            { "table_name": "opportunity" }
          ]
        }
      ]
    },
    {
      "name": "TeamsMessages",
      "urls": [ { "url": "https://teams.microsoft.com/l/channel/..." } ]
    },
    { "name": "People" },
    {
      "name": "ScenarioModels",
      "models": [ { "id": "model_id" } ]
    },
    {
      "name": "Meetings",
      "items_by_id": [
        {
          "id": "010000002300A00045B6...",
          "is_series": true
        }
      ]
    }
  ]
}
```

### WebSearch object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Must be set to `WebSearch`. |
| `sites` | Array of Site object | Optional. If omitted, the agent can search all sites. Max 4 items. |

#### Site object

| Property | Type | Description |
| --- | --- | --- |
| `url` | String | Required. Absolute URL. Can't contain more than two path segments (e.g., `https://contoso.com/projects/mark-8` is valid, `https://contoso.com/projects/mark-8/beta-program` is **not**). Can't contain any query parameters. |

### OneDriveAndSharePoint object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Must be set to `OneDriveAndSharePoint`. |
| `items_by_sharepoint_ids` | Array | Optional. Identifies sources using IDs. |
| `items_by_url` | Array | Optional. Identifies sources by URL. If both `items_by_sharepoint_ids` and `items_by_url` are omitted, the agent can access all OneDrive/SharePoint in the organization. |

#### Items by SharePoint IDs object

| Property | Type | Description |
| --- | --- | --- |
| `site_id` | String | Optional. GUID for site. |
| `web_id` | String | Optional. GUID for web. |
| `list_id` | String | Optional. GUID for document library. |
| `unique_id` | String | Optional. GUID for folder/file within list. |
| `search_associated_sites` | Boolean | Optional. Only applicable when `site_id` references a HubSite. |
| `part_type` | String | Optional. Possible values: `OneNotePart`. |
| `part_id` | String | Optional. GUID for part within a SharePoint item (e.g., a OneNote page). |

#### Items by URL object

| Property | Type | Description |
| --- | --- | --- |
| `url` | String | Optional. Absolute URL to a SharePoint or OneDrive resource. |

### Copilot connectors (GraphConnectors) object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Must be set to `GraphConnectors`. |
| `connections` | Array of Connection object | Optional. If omitted, all org connectors are accessible. |

#### Connection object

| Property | Type | Description |
| --- | --- | --- |
| `connection_id` | String | Required. Unique identifier of the Copilot connector. |
| `additional_search_terms` | String | Optional. KQL query to filter items. |
| `items_by_external_id` | Array | Optional. |
| `items_by_external_url` | Array | Optional. |
| `items_by_path` | Array | Optional. Filters by `itemPath` semantic label. |
| `items_by_container_name` | Array | Optional. Filters by `containerName` semantic label. |
| `items_by_container_url` | Array | Optional. Filters by `containerUrl` semantic label. |

### Graphic art object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Set to `GraphicArt`. |

### Code interpreter object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Set to `CodeInterpreter`. |

### Dataverse object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Set to `Dataverse`. |
| `knowledge_sources` | Array | Optional. Identifiers, skills, and table names for Dataverse instances. |

#### Knowledge sources object

| Property | Type | Description |
| --- | --- | --- |
| `host_name` | String | Required. Unique identifier for the host. |
| `skill` | String | A unique identifier for how the agent interacts with Dataverse knowledge. |
| `tables` | Array | Tables to scope the agent's knowledge. |

To find the `skill` identifier: in Copilot Studio, create a new agent, add a Dataverse knowledge source, publish, download the .zip, and read the `skill` from `declarativeAgent.json`.

```json
{
  "name": "Dataverse",
  "knowledge_sources": [
    {
      "host_name": "org0f612cfc.crm.dynamics.com",
      "skill": "AIBuilderFileAttachedData_e7eTReDbkX_1t4X1oGoCF",
      "tables": [
        { "table_name": "msdyn_aibfileattacheddata" }
      ]
    }
  ]
}
```

### Microsoft Teams messages object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Set to `TeamsMessages`. |
| `urls` | Array | Optional. Max 5 objects. If omitted, agent can search all channels/chats. |

### Email object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Set to `Email`. |
| `shared_mailbox` | String | Optional. SMTP address. |
| `group_mailboxes` | Array of String | Optional. Up to 25 mailboxes. |
| `folders` | Array | Optional. Scope to specific folders. |

### People object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Set to `People`. |
| `include_related_content` | Boolean | Optional. When `true`, includes related documents, emails, and Teams messages between the agent user and referenced people. Default `false`. |

### Scenario models object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Set to `ScenarioModels`. |
| `models` | Array | Required. Task-specific model identifiers. |

### Meetings object

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Set to `Meetings`. |
| `items_by_id` | Array | Optional. Max 5. If omitted, agent can search all meetings. |

### Embedded knowledge object

> **Important**: This feature is not yet available.

Files max 1 MB each, max 10 files; supported formats: .doc, .docx, .ppt, .pptx, .xls, .xlsx, .txt, .pdf.

| Property | Type | Description |
| --- | --- | --- |
| `name` | String | Required. Must be set to `EmbeddedKnowledge`. |
| `files` | Array of File object | Optional. Max 10. |

```json
{
  "name": "EmbeddedKnowledge",
  "files": [
    { "file": "file1.docx" },
    { "file": "file2.csv" }
  ]
}
```

## Conversation starters

Max 6 objects.

| Property | Type | Description |
| --- | --- | --- |
| `text` | String | Required. Localizable. |
| `title` | String | Optional. Localizable. |

## Actions

Min 1, max 10 objects.

| Property | Type | Description |
| --- | --- | --- |
| `id` | String | Required. Unique identifier. Can be a GUID. |
| `file` | String | Required. Path to the API plugin manifest. |

## Behavior overrides object

Optional. Modifies the agent's behavior.

| Property | Type | Description |
| --- | --- | --- |
| `suggestions` | Suggestions object | Optional. |
| `special_instructions` | Special instructions object | Optional. |

### Suggestions object

| Property | Type | Description |
| --- | --- | --- |
| `disabled` | Boolean | Required. Default `false`. |

### Special instructions object

| Property | Type | Description |
| --- | --- | --- |
| `discourage_model_knowledge` | Boolean | Required. If `true`, the agent doesn't use model knowledge when generating responses. Default `false`. |

> This `discourage_model_knowledge: true` toggle is the manifest-level equivalent of the Agent Builder "Only use specified sources" toggle (often called "OnlyAllowedSources" in product UX/docs). It forces grounding-or-fallback rather than letting the model answer from general knowledge.

## Disclaimer object

| Property | Type | Description |
| --- | --- | --- |
| `text` | String | Required. Max 500 chars. Displayed at conversation start. |

## Sensitivity label object

Only applied when the agent has Embedded Files. Not enabled yet.

| Property | Type | Description |
| --- | --- | --- |
| `id` | String | The GUID of the sensitivity label from Microsoft Purview. |

## Worker agent object (preview)

| Property | Type | Description |
| --- | --- | --- |
| `id` | String | Required. Title ID of the application that contains the declarative agent. |

## User override object

Identifies capabilities the user can override via a Microsoft 365 Copilot UI toggle.

| Property | Type | Description |
| --- | --- | --- |
| `path` | String | Required. JSONPath expression identifying the capability. |
| `allowed_actions` | Array of String | Required. Only supported action is `remove`. |

```json
{
  "user_overrides": [
    {
      "path": "$.capabilities[?(@.name == 'WebSearch')]",
      "allowed_actions": ["remove"]
    },
    {
      "path": "$.capabilities[?(@.name == 'TeamsMessages')]",
      "allowed_actions": ["remove"]
    }
  ]
}
```

## Full example

```json
{
  "$schema": "https://developer.microsoft.com/json-schemas/copilot/declarative-agent/v1.6/schema.json",
  "version": "v1.6",
  "name": "Teams Toolkit declarative agent",
  "description": "Declarative agent created with Teams Toolkit",
  "instructions": "You are a repairs expert agent. With the response from the listRepairs function, you **must** create a poem out of the repairs listed and always include their title and the assigned person.",
  "conversation_starters": [
    { "title": "Getting Started", "text": "How can I get started with Teams Toolkit?" }
  ],
  "sensitivity_label": { "id": "00000000-0000-0000-0000-000000000000" },
  "actions": [
    { "id": "repairsPlugin", "file": "repairs-hub-api-plugin.json" }
  ],
  "behavior_overrides": {
    "suggestions": { "disabled": true },
    "special_instructions": { "discourage_model_knowledge": true }
  },
  "disclaimer": { "text": "This declarative agent is a fictional example." },
  "worker_agents": [
    { "id": "P_2c27ae89-1f78-4ef7-824c-7d83f77eda28" }
  ],
  "user_overrides": [
    {
      "path": "$.capabilities[?(@.name == 'OneDriveAndSharePoint')]",
      "allowed_actions": ["remove"]
    }
  ],
  "capabilities": [
    {
      "name": "WebSearch",
      "sites": [ { "url": "https://contoso.com/projects/mark-8" } ]
    },
    {
      "name": "OneDriveAndSharePoint",
      "items_by_url": [ { "url": "https://contoso.sharepoint.com/sites/ProductSupport" } ]
    },
    { "name": "GraphConnectors", "connections": [ { "connection_id": "foodStore" } ] },
    { "name": "GraphicArt" },
    { "name": "CodeInterpreter" },
    {
      "name": "Dataverse",
      "knowledge_sources": [
        {
          "host_name": "organization.crm.dynamics.com",
          "skill": "DVCopilotSkillName",
          "tables": [ { "table_name": "account" }, { "table_name": "opportunity" } ]
        }
      ]
    },
    { "name": "TeamsMessages", "urls": [ { "url": "https://teams.microsoft.com/l/channel/..." } ] },
    { "name": "Email", "shared_mailbox": "sample@service.microsoft.com", "folders": [ { "folder_id": "inbox" } ] },
    { "name": "People" },
    { "name": "ScenarioModels", "models": [ { "id": "model_id" } ] },
    { "name": "Meetings", "items_by_id": [ { "id": "...", "is_series": true } ] },
    {
      "name": "EmbeddedKnowledge",
      "files": [ { "file": "file1.docx" }, { "file": "file2.csv" } ]
    }
  ]
}
```
