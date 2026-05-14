---
source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-disable
fetched: 2026-05-14
ms_date_on_page: 2026-05-04
gitcommit: https://github.com/MicrosoftDocs/powerapps-docs-pr/blob/33b2bf3032ad500029a3a448d909c613e7d3515a/powerapps-docs/maker/data-platform/data-platform-mcp-disable.md
---

# Configure the Dataverse model context protocol (MCP) server - Power Apps | Microsoft Learn

This article provides detailed instructions about how to enable, manage, configure, and disable the Dataverse Model Context Protocol (MCP) server for environments within the Power Platform admin center. It's intended for Power Platform administrators handling managed environments and also covers prerequisites for enabling the server.

## Prerequisites

- Power Platform administrator role in order to access Dataverse MCP server environment settings, enable allowed MCP clients, create or edit an environment group, and change connector policies.
- The steps described in this article require that the environment is a **Managed Environment**.
- By default, the Microsoft Copilot Studio client for Dataverse MCP is enabled for all environments. You must enable the additional clients in the Power Platform admin center before you can connect to the client.

## Configure and manage the Dataverse MCP server

By default, Dataverse MCP server is enabled for Copilot Studio. To enable non-Microsoft MCP clients, such as Visual Studio GitHub Copilot and Claude, follow these steps:

> **Note**
>
> MCP allow listing applies only to the `/api/mcp` agent entrypoint. MCP‑named custom APIs are regular Dataverse APIs and aren't restricted by this setting.

1. Go to [Power Platform admin center](https://admin.powerplatform.microsoft.com/). Select **Manage** > **Environments**.
2. Select the **Environment Name** where you want to turn on the Dataverse MCP server, and then select **Settings**. Under **Settings**, select **Product** > **Features**. Scroll down to locate **Dataverse Model Context Protocol** and make sure **Allow MCP clients to interact with Dataverse MCP server** is turned on.
3. Select **Advanced Settings**.
4. The list of available clients is shown. Open the client record you want. In this example, the **Microsoft GitHub Copilot** client is enabled.
5. On the MCP client record, set **Is Enabled** to **Yes**.
6. Select **Save & Close**.
7. Repeat steps 4-7 to enable other clients as needed.

## Disable the Dataverse MCP server for an environment

By default the **Allow MCP clients to interact with Dataverse MCP server** is turned on for Copilot Studio. Admins can disable MCP for Dataverse by clearing the setting.

> **Warning**
>
> Disabling the Dataverse MCP Server stops all tools and agents that rely on it. Any ongoing development or AI integration testing using MCP is also interrupted.

## Write effective instructions for a Dataverse MCP server agent

When you configure your agent in Copilot Studio or Visual Studio Code to use a Dataverse MCP server, clear and well-structured instructions are key to guiding how the agent operates. These instructions help the agent understand its role, what capabilities it has via the MCP server tools, and how to carry out workflows reliably and consistently.

Agent instructions are natural-language directives that tell your agent what it should do, how it should behave, and how to use the MCP tools available to it. They give important context so the agent can:

- Select and call the right MCP tools.
- Fill in tool inputs correctly.
- Decide when to use tools versus generating answers directly.
- Follow the desired tone or behavior patterns during conversations.

### Principles for effective instructions

When writing instructions for a Dataverse MCP server agent, consider the following guidelines:

1. **Define the agent's purpose clearly.**

   State the agent's role in simple terms. For example: *"Assist with customer data queries, updates, and record management using Dataverse MCP tools."*

   Include tone or restrictions if applicable such as, *"always respond in a professional tone"* or *"do not expose sensitive fields in responses."*

2. **Outline skills and tool actions.**

   Tell the agent which high-level tasks it can perform with the MCP tools. For example:

   - Retrieve or filter records using the `read_query` tool.
   - Create or update records with `create_record` and `update_record`.
   - Explore table schema with `describe_table`.

   Phrase these as actionable instructions, such as *"Use the read_query tool to fetch records when the user asks for data retrieval."*

3. **Add workflow guidance.**

   For common processes (like looking up a record or updating a field), add step-by-step guidance. For example: *"When updating a record, first identify it using a read_query call, then use update_record with the appropriate field values."*

   Providing this context helps the agent orchestrate the right sequence of MCP tool calls.

### Add instructions in Visual Studio Code or Copilot Studio

You can use a markdown structured block of instructions as a starting point for your own agent's instructions.

#### Add instructions in Visual Studio Code

1. Open Visual Studio Code.
2. Open the **Chat** pane.
3. Select **Settings** > **Chat Instructions**.
4. Select **New instruction file**.
5. Copy and paste the instruction content into the new file, such as the sample instructions.

#### Add instructions in Copilot Studio

1. Open Copilot Studio.
2. Select your agent.
3. Go to **System instructions** and paste your agent instructions, such as the sample instructions.

#### Sample agent instructions

```md
## Role
Act as an autonomous agent responsible for interacting with the Microsoft Dataverse app.

## Objective
Your objective is to respond to tasks provided by the user. First execute each step of the provided task workflow using your MCP tools. Check if you have achieved your objective after each tool call. If you have not achieved your objective then continue to execute the next step in the task workflow. Do not ask for user intervention, if you have to run an operation externally in case dataverse doesnt support that operation. you are allowed to run it without user confirmation. Also do not ask confirmation for delete table or delete record operation, you can delete.

## MCP tool selection instructions
 - Whenever you have to use logical table name, call the list_tables tools to get that logical table name.
 - Whenever you have to use column/attribute name, call the describe_table tool to get the column/attribute name.

## MCP tool usage instructions

- Before executing an MCP tool, always review the tool description and restrictions.
- Always strictly follow the description of each MCP tool and perform actions without any deviation from the tool description.
- Provide higher precedence to tool description over general knowledge.
- Always review the tool documentation and restrictions before running any query or operation. Strictly validate each planned action against the tool's rules and supported features before execution.
- For read_query tool, there are restrictions on SQL conditions. Always refer to the tool description for supported and unsupported sql keywords before generating the sql query and ensure only supported conditions/keywords are used.

## Reasoning instructions

- Think out loud and reason step by step.
- Before each tool call, plan and verify the action conforms to the tool description.
- After each tool call, reflect on the result and determine the next step.
- If an exception, error, or warning is observed, communicate it clearly to the user and retry based on the error message.
- When answering questions about data, DO NOT rely on general knowledge - always use tools to retrieve accurate, current data.
- DO NOT stop reasoning until all tasks are complete or an unrecoverable error occurs.
- Only ask clarifying questions if the task requirements are ambiguous.
```
