---
source: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/agent-builder-add-knowledge
fetched: 2026-05-14
---

# Add knowledge sources to your declarative agent in Microsoft 365 Copilot

The Agent Builder feature in Microsoft 365 Copilot provides a simple interface for you to integrate knowledge sources to make your declarative agent more intelligent and context-aware. These knowledge sources ground your agent in enterprise data, public content, and user-specific information to enable them to deliver more accurate, relevant, and personalized responses.

You can add:

- Up to four public website URLs.
- SharePoint files, folders, or sites.
- Up to five Teams chat URLs.
- Embedded files uploaded from your device (on the **Configure** tab).
- Microsoft 365 Copilot connectors (if enabled by your organization's administrator).

## Add knowledge sources

If you use natural language to create your agent, Agent Builder adds knowledge sources for you based on your description. You can also add knowledge sources from the chat box:

1. In Microsoft 365 Copilot, choose **New agent** on the left pane, and provide a natural language description for your agent.
2. Choose the plus icon (**+**) in the chat box. Methods:
    - **Add work content** - Select, search for, or upload files.
    - **Upload images and files** - Upload directly from your device.
    - **Attach cloud files** - Attach cloud files that you have access to.

If you're configuring your agent manually:

1. In Microsoft 365 Copilot, choose **New agent** from the left pane, and choose **Skip to configure**.
2. In the **Knowledge** section, use one of the following methods:
    - **Search bar** - Type keywords (use for email and Teams messages).
    - **Enter URL** - Public website or SharePoint link (two levels deep, no query parameters).
    - **Picker** - SharePoint file picker UI.
    - **Upload** - Upload files directly from your device.

## Public websites

- Public website URLs must only be two levels (`https://example.org/a/b/c` is invalid).
- URLs can't contain query parameters.
- Up to four URLs.

To configure your agent to use any web data as knowledge:

- Use natural language to ask Agent Builder to prioritize your knowledge sources.
- On the **Configure** tab, under **Knowledge**, choose the toggle next to **Search all websites**.

## SharePoint content

- Select up to 100 SharePoint files for each agent.
- The agent respects existing permissions and [sensitivity labels](https://learn.microsoft.com/purview/sensitivity-labels) for files already uploaded to SharePoint.
- Although there isn't a direct file size limit on the knowledge files you select, the agent can only reason over specific file types.

> If [Restricted SharePoint Search](https://learn.microsoft.com/sharepoint/restricted-sharepoint-search) is enabled, you can't use SharePoint as a knowledge source.

### Entering a URL for a SharePoint site, folder, or file

You can enter a URL for a SharePoint site, folder, or file, such as `contoso.sharepoint.com/sites/policies`. The agent searches the URL and subpaths. For example, a URL such as `contoso.sharepoint.com/sites` also includes subpaths like `contoso.sharepoint.com/sites/policies`.

### SharePoint file picker

You can also select files or folders from the SharePoint file picker by choosing the cloud icon in the **Knowledge** section.

### File readiness

When new files are uploaded to SharePoint, they can take up to several minutes to be ready for the agent to include in its response.

## Microsoft Teams data

You can ground your agent in Microsoft Teams data, including Teams chat messages and meeting information. To use all chat messages, meeting transcripts, and calendars that you have access to as knowledge, select **My Teams chats and meetings**.

You can also scope your agents to specific chats (up to 5).

> Teams knowledge is only available to users with a Microsoft 365 Copilot add-on license.
> You can't scope to individual meetings.

## Outlook emails

You can ground your agent in Outlook email. On the **Configure** tab, in the **Knowledge** section, select the search bar, and choose **My emails**.

> You can't scope email knowledge. When you add email, the agent uses all email in your mailbox as knowledge. Users that you share the agent with don't have access to your email as knowledge. This capability is only available to users with a Microsoft 365 Copilot add-on license.

## Embedded file content

You can upload files directly from your device for your agent to use as knowledge. Files become embedded content in the agent. Up to 20 files.

> [Microsoft Purview Information Barriers (IB)](https://learn.microsoft.com/purview/information-barriers) **isn't supported** on embedded files. Any user who can access the agent can see responses grounded in the embedded file content.

Files with any of the following characteristics aren't supported:

- Double key encryption.
- Sensitivity labels that have user-defined permissions (agent creation fails).
- Sensitivity labels that have extract rights permission disabled.
- Files from another tenant that have encryption enabled.
- Password protection.

### Sensitivity labels for agent embedded content

The sensitivity label applied to the embedded content is the higher priority of:

- The highest priority sensitivity label applied to any embedded file.
- The default sensitivity label policy applied by the organization.

The sensitivity label applies only to the embedded content; it doesn't apply to other knowledge sources that the agent references.

#### Unsupported sensitivity label scenarios

| Scenario | Behavior | Action |
| --- | --- | --- |
| DKE-labeled file | Embedded but not used as knowledge. No error shown. | Avoid uploading. |
| Label with user-defined permissions | Uploaded but agent creation fails (no error). | Remove the file. |
| Label with extract rights disabled | Uploaded but agent creation fails (no error). | Remove the file. |
| File with cross-tenant label and encryption | Embedded but not used as knowledge. | Avoid uploading. |
| Password-protected file | Uploaded; visible error shown. | Remove the file. |

### File types and size limits

| File type | Embedded file limit |
| --- | --- |
| .doc / .docx | 512 MB |
| .pdf | 512 MB |
| .ppt / .pptx | 512 MB |
| .txt | 512 MB |
| .xls / .xlsx | 30 MB |
| .html\* | NA |

\* Only supported for SharePoint in Microsoft 365.

## People data

If you ground your agent in People data, it can deliver more personalized and context-aware responses. People data provides public information about individuals, such as name, position, skills, and organizational relationships.

People data is enabled by default for agents created by users who have a Microsoft 365 Copilot license. Disable/re-enable via the **Reference people in organization** toggle on the **Configure** tab.

## Copilot connectors

Copilot connectors allow agents to access and apply knowledge from external systems such as customer accounts, incident tickets, code repositories, and knowledge articles.

> Admins must enable and configure Copilot connectors in the [Microsoft 365 admin center](https://learn.microsoft.com/microsoftsearch/configure-connector).

### Scope Copilot connector data sources

| Connector | Scoping attribute |
| --- | --- |
| Azure DevOps Work Items | Area path |
| Azure DevOps Wiki | Project |
| Confluence | Space |
| Google Drive | Folder |
| GitHub Cloud Pull Requests | Repository |
| GitHub Cloud Issues | Repository |
| GitHub Cloud Knowledge | Repository |
| Jira | Project |
| ServiceNow Knowledge | Knowledge base |
| ServiceNow Catalog | Catalog |
| ServiceNow Tickets | Entity type (Sys_class_name / Category / Subcategory) |

## Prioritize your knowledge sources over general knowledge

You can configure your agent to prioritize the knowledge sources you provide—such as SharePoint content or embedded files—when it responds to queries that require knowledge-based searches.

When you enable this feature, the agent answers simple questions that don't require searching by using its general knowledge. It uses your knowledge sources only to answer search-based questions. If the agent can't find relevant information in the knowledge sources you provide, it responds with a fallback message that states it can't find the information.

**To configure your agent to prioritize your knowledge sources, on the Configure tab, select the toggle next to "Only use specified sources".**

> This toggle is the UX equivalent of `behavior_overrides.special_instructions.discourage_model_knowledge: true` in the JSON manifest — frequently called "OnlyAllowedSources" behavior. See [`declarative-agent-manifest.md`](./declarative-agent-manifest.md#special-instructions-object).

> Agent Builder in Microsoft 365 Copilot doesn't support **blocking** general AI knowledge from your agent's responses. For stricter control, use Copilot Studio. See [Orchestrate agent behavior with generative AI](https://learn.microsoft.com/microsoft-copilot-studio/advanced-generative-actions).
