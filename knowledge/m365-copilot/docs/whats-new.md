---
source: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/whats-new
fetched: 2026-05-14
---

# What's New for Microsoft 365 Copilot Developers

As a developer, you can extend, enrich, and customize Microsoft 365 Copilot for the unique way your customers work. This article provides the latest information about what's new in Microsoft 365 Copilot extensibility.

For the latest information, announcements, and news about preview and generally available (GA) features, follow the Microsoft 365 Copilot developer blog (`https://devblogs.microsoft.com/microsoft365dev/category/microsoft-365-copilot/`).

## May 2026

### Declarative agent manifest version 1.7

A new version of the declarative agent manifest schema is available. Declarative agent manifest schema version 1.7 adds the following features:

- Added the optional `editorial_answers` property so agents can match semantically similar user queries to predefined question and answer pairs.
- Added the optional `default_response_mode` property to the Behavior overrides object so you can set the agent's default mode to `Auto`, `Reasoning`, or `Quick response`.
- Added the optional `depends_on` property to the Conversation starters object to specify capability dependencies for conversation starters.

### New agent templates added to Agent Builder

Seven new agent templates are now available in Agent Builder to help you quickly build declarative agents for common workplace scenarios:

- Executive Briefing Agent
- My Company Policy
- Personal News Digest
- Plan My Day
- Project Delta Digest
- SME Finder
- Status Update Agent

### Evaluate agents

Evaluate agents by using a comprehensive evaluation framework and tooling to refine agent performance. The Agent Evaluations CLI tool enables developers to create, run, and analyze tests for their agents.

### Package Management API updates (preview)

The Package Management API has new capabilities for IT administrators to manage apps and agents in their Microsoft 365 organization. Administrators can now block and unblock packages to control their availability, update package metadata, and reassign package ownership.

## April 2026

### Agent Registration API (preview)

The Agent Registration API enables developers and administrators to programmatically register and manage agents within their Microsoft 365 environment. The API supports creating, retrieving, updating, and deleting agent registrations with associated metadata and agent cards.

### Copilot policy settings API (preview)

The Copilot policy settings API is now available in preview. This API provides a unified endpoint to read and update Copilot settings across multiple policy services, including Cloud Policy Service (CPS) and Microsoft Intune.

## March 2026

### Share agents to teams in Microsoft Teams

You can now share agents built with Agent Builder in Microsoft 365 Copilot to teams as well as users and groups.

### Use natural language to create an agent in Microsoft 365 Copilot

You can now create agents more quickly with Agent Builder in Microsoft 365 Copilot by using natural language. The agent is automatically configured for you.

### Interactive UI widgets for declarative agents

You can now add interactive UI widgets to your declarative agents by extending MCP server-based actions using the OpenAI Apps SDK. Widgets can render inline or in full-screen mode within Microsoft 365 Copilot.

## February 2026

### Agent Builder availability in GCCH

Agent Builder is now available in Government Community Cloud High (GCCH) environments.

## January 2026

### Retrieval API pay-as-you-go consumption (preview)

The Microsoft 365 Copilot Retrieval API is now available to users without a Microsoft 365 Copilot add-on license via pay-as-you-go consumption (preview). This model provides access to the Retrieval API for tenant-level data sources such as SharePoint and Microsoft 365 Copilot connectors.

## December 2025

### Teams meeting AI insights APIs now generally available

The Teams meeting AI insights APIs are now generally available in Microsoft Graph v1.0. These APIs enable you to retrieve AI-generated insights from Teams meetings, including action items, meeting notes, and mentions.

## November 2025

### People knowledge source in Copilot Studio lite experience

The People knowledge source is now available in the Copilot Studio lite experience, allowing agents to answer questions about individuals in your organization.

### Agent Builder in Microsoft 365 Copilot is available in GCCM

The Agent Builder feature in Microsoft 365 Copilot is now available in Microsoft 365 Government Community Cloud Moderate (GCCM) environments.

### Embedded file content file size limit increase

You can now upload files up to 512 MB in size when you embed file content as knowledge in Agent Builder in Microsoft 365 Copilot.

## October 2025

### New admin controls for agent sharing

Tenant administrators can now govern who is allowed to share agents created in Microsoft 365 Copilot.

### Copy an agent to Copilot Studio

You can copy your declarative agent from Microsoft 365 Copilot to Copilot Studio by using the **Copy to full experience** feature. This unlocks advanced lifecycle management, analytics, governance controls, and deeper enterprise integration options.

### Use the Search API (preview) to perform semantic search

The Microsoft 365 Copilot Search API (preview) enables developers to perform semantic search across OneDrive content using natural language queries with contextual understanding and intelligent results.

### Users with usage billing have access to additional knowledge sources

Users who are configured with usage billing in the Microsoft 365 admin center now have access to embedded file content, SharePoint data, and Microsoft 365 Copilot connectors custom knowledge sources.

### Microsoft 365 Copilot Chat API (preview)

The Microsoft 365 Copilot Chat API (preview) enables you to programmatically engage in multi-turn conversations with Microsoft 365 Copilot, grounded in enterprise search and web search.

## August 2025

### Use Teams meetings as a knowledge source

Teams meetings are now available as a knowledge source when you use Microsoft 365 Copilot to create agents.

## July 2025

### Scope Copilot connector data sources

You can now scope Copilot connectors to specific data attributes when you use Microsoft 365 Copilot to create your agent.

### Declarative agent manifest version 1.5

A new version of the declarative agent manifest schema is available. Declarative agent manifest schema version 1.5 adds the following:

- Added the `meetings` capability to the list of `capabilities`, which allows agents to search meetings in the organization.

### Disclaimers in declarative agents

Added the `disclaimers` property to the Declarative agent manifest object in schema version 1.4.

### Embedded file content file size limit increase

You can now upload files up to 100 MBs in size when you embed file content as knowledge in Microsoft 365 Copilot.

### Increased SharePoint file limit for agents

You can now specify up to 100 SharePoint files as knowledge when you use Microsoft 365 Copilot, up from a limit of 20 files.

### Build Microsoft 365 Copilot connectors for people data (preview)

Build connectors to ingest people data from your source systems into Microsoft Graph for Microsoft 365 Copilot.

### Agents supported in Microsoft 365 Government clouds

Limited support for declarative agents is available for Microsoft 365 Government tenants. Support is now available for Government Community Cloud (GCC) tenants.

### Asynchronous and proactive messages in custom engine agents

You can implement asynchronous and proactive message flows in your custom engine agents.

### Convert declarative agents to custom engine agents

You can convert your declarative agent to a custom engine agent to take advantage of advanced functionality and workflows.

### Prioritize declarative agent knowledge sources

You can configure your agent to prioritize the knowledge sources that you provide rather than general knowledge in its responses.

### Custom engine agents generally available

Custom engine agents for Microsoft 365 Copilot are now generally available (GA).

## June 2025

### Maximum number of conversation starters for declarative agents

You can now add up to 12 conversation starters to your declarative agent when you use the Microsoft 365 Agents Toolkit to create your agent.

### Embedded file content as knowledge

Use the file upload feature in Microsoft 365 Copilot to upload files from your device or the cloud to use as knowledge for your agent.

### Use the Retrieval API (preview) to retrieve data

The Microsoft 365 Copilot Retrieval API (preview) allows you to retrieve relevant content from SharePoint and Copilot connectors.

### Microsoft 365 Copilot API client libraries

Use the Copilot API libraries to work with Microsoft 365 Copilot APIs.

### Outlook email and Teams chats knowledge in Microsoft 365 Copilot

Add Outlook email and Teams group, channel, and meeting chats as knowledge when you use Microsoft 365 Copilot to build your agent.

## May 2025

### Microsoft 365 Agents Toolkit

Use Microsoft 365 Agents Toolkit to Build declarative agents and Build Copilot connectors.

### Microsoft 365 Copilot APIs

The Microsoft 365 Copilot APIs provide a comprehensive set of capabilities that enable you to build AI-powered applications grounded in enterprise data.

### API plugin manifest version 2.3

A new version of the API plugin manifest schema is available. Plugin manifest schema 2.4 for Microsoft 365 Copilot adds support for Model Context Protocol (MCP) servers, enhanced response semantics with file references, and improved confirmation handling.

### Declarative agent manifest version 1.4

A new version of the declarative agent manifest schema is available. Declarative agent manifest schema version 1.4 adds the following.

- Added the `behavior_overrides` property to the Declarative agent manifest object.
- Added the `part_type` and `part_id` properties to the Items by SharePoint IDs object.
- Added the Scenario models capability.

## April 2025

### Email as knowledge

Email is now available as a knowledge source for agents built with Microsoft 365 Agents Toolkit.

### Copilot Studio agent templates

Use templates in Copilot Studio to streamline your agent development process.

### Document interaction for declarative agents in Word

Declarative agents in the Copilot experience in Word can interact with the open document. Users can provide the current selection to the agent and can insert images provided by the agent into the document.

## March 2025

### Declarative agent manifest version 1.3

A new version of the declarative agent manifest schema is available. Declarative agent manifest schema version 1.3 adds support for the following capabilities:

- Dataverse knowledge
- Teams messages as knowledge
- People knowledge

## February 2025

### Copilot Studio available in Copilot Chat

Microsoft 365 Copilot Chat users can now access Copilot Studio to build agents in the Microsoft 365 Copilot app and the Copilot app in Teams.

### Add websites as knowledge in Copilot Studio

You can add specific public websites as agent knowledge sources to make your agent context-aware.

### Custom engine agents available in Copilot app (preview)

Custom engine agents are now available to users who have Microsoft 365 Copilot licenses or users in tenants with metering enabled in the Microsoft 365 Copilot app (for preview), in addition to Teams.

## January 2025

### Links are no longer redacted in Copilot responses

Links to organizational and web resources are no longer redacted from Copilot responses. Links that don't explicitly match grounding data or resources defined in the agent manifest continue to be redacted.

### Build agents for Microsoft 365 Copilot Chat

You can now build agents for Microsoft 365 users who don't have a Microsoft 365 Copilot license, grounded on the web and with limited capabilities.
