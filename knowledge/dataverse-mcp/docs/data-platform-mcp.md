---
source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp
fetched: 2026-05-14
note: |
  The directive originally listed `https://learn.microsoft.com/en-us/power-platform/dataverse/mcp-server` —
  that URL returns 404. The canonical landing page for Dataverse MCP in Microsoft Learn is now under
  `power-apps/maker/data-platform/`. All URLs below reflect that confirmed path.
ms_date_on_page: 2026-03-30
gitcommit: https://github.com/MicrosoftDocs/powerapps-docs-pr/blob/121e8d4ef94af1ebfc5e3cf2005af1324b16e723/powerapps-docs/maker/data-platform/data-platform-mcp.md
---

# Connect to Dataverse with model context protocol (MCP) - Power Apps | Microsoft Learn

The Model Context Protocol (MCP) is an open protocol that enables seamless integration between large language model (LLM) applications and external data sources and tools. Microsoft Dataverse can act as an MCP server, providing intelligent access to tables and records to various MCP clients like Copilot Studio agents, Visual Studio (VS) Code GitHub Copilot, GitHub Copilot CLI, Claude desktop, Claude Code, and many others. This integration standardizes and streamlines the interaction between AI models and Dataverse data, making it more efficient and effective for developers to apply Dataverse's rich data capabilities within their AI-driven applications.

To use Dataverse as an MCP server, you need to enable and configure the MCP server and allowed clients for your Power Platform environment. Once configured, you can connect to the Dataverse MCP server using different MCP clients. More information: [Configure the Dataverse MCP server for an environment](data-platform-mcp-disable.md)

### Dataverse MCP server URL

The Dataverse MCP remote server URL follows this format:

`https://{dataverseOrgName}.crm.dynamics.com/api/mcp`

For example, if your Dataverse organization name is `contoso`, the MCP server URL is:

`https://contoso.crm.dynamics.com/api/mcp`

## Connect to a Dataverse MCP server

There are multiple ways to connect to a Dataverse MCP server:

- Microsoft Copilot Studio. To learn how to connect to MCP through Dataverse MCP go to [Connect to Dataverse with model context protocol in Microsoft Copilot Studio](data-platform-mcp-copilot-studio.md).
- Visual Studio GitHub Copilot and Copilot CLI. To learn how to connect to GitHub Copilot in Visual Studio Code or Copilot CLI go to [Connect Dataverse MCP with GitHub Copilot in Visual Studio Code and Copilot CLI](data-platform-mcp-vscode.md).
- Non-Microsoft clients such as Claude desktop and Claude Code. To learn how to connect through Dataverse MCP go to [Connect Dataverse MCP with non-Microsoft clients](data-platform-mcp-other-clients.md).

## List of tools

Once connected to the Dataverse MCP Server, you can choose from various tools in the Power Platform environment.

| Tool | Description |
| --- | --- |
| `create_record` | Inserts a new row into a Dataverse table and returns the GUID. |
| `describe_table` | Retrieves the T-SQL schema of a specified table. |
| `list_tables` | Lists all tables in the Dataverse environment. |
| `read_query` | Executes SELECT queries to fetch data from Dataverse. |
| `update_record` | Updates an existing row in a Dataverse table. |
| `Create Table` | Creates a new table with a specified schema. |
| `Update Table` | Modifies schema or metadata of an existing table. |
| `Delete Table` | Deletes a table from Dataverse. |
| `Delete Record` | Deletes a row from a Dataverse table. |
| `Search` | Searches through keywords over Dataverse for specific record. |
| `Fetch` | Retrieves full content of record in Dataverse using entity name and ID. |

> **Note**
>
> Starting **December 15, 2025**, Dataverse MCP tools are charged when accessed by AI agents created outside of Microsoft Copilot Studio. If you have Dynamics 365 Premium licenses (such as Dynamics 365 Sales Premium, Finance Premium, Supply Chain Premium, and Customer Service Premium) or a Microsoft 365 Copilot User Subscription License (USL), you aren't charged for accessing Dynamics 365 data, even when that data is accessed from outside Microsoft Copilot Studio.
>
> The `Search` tool is billed at the same Copilot Credit rate as Tenant graph grounding, while all other tools follow the **Text and generative AI tools (basic)** per-10-response Copilot credit rate. For information about Copilot billing, go to [Billing rates and management](https://learn.microsoft.com/en-us/microsoft-copilot-studio/requirements-messages-management).

## Related pages (cross-references)

- [Configure the Dataverse MCP server for an environment](data-platform-mcp-disable.md) — admin enable/disable, client allowlisting, **agent instruction authoring guidance**
- [Connect Dataverse MCP with GitHub Copilot in Visual Studio Code and Copilot CLI](data-platform-mcp-vscode.md)
- [Connect to Dataverse with model context protocol in non-Microsoft clients (Claude)](data-platform-mcp-other-clients.md)
- [Enable preview tools and features in Dataverse MCP server](data-platform-mcp-preview-tools.md) — required for Business Skills (Dataverse intelligence)
- [What is Dataverse intelligence?](data-platform-intelligence.md) — Business Skills / Work IQ extension (preview)
