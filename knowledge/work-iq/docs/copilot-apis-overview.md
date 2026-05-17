---
source: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/copilot-apis-overview
fetched: 2026-05-14
ms_date: 2025-10-24
ms_updated_at: 2026-04-02
note: This is the canonical Microsoft Learn doc for the "Work IQ API" surface — referenced in copilot-camp lab BAF6.
---

# Microsoft 365 Copilot APIs Overview

> **Terminology**: copilot-camp Lab BAF6 calls this the "Work IQ API". On Microsoft Learn it's documented as the "Microsoft 365 Copilot APIs" under the `graph.microsoft.com/v1.0/copilot` namespace.

The Microsoft 365 Copilot APIs enable you to securely access Microsoft 365 Copilot capabilities in your own applications and custom engine agents, while aligning with compliance standards of Microsoft 365. Whether you're building a custom engine agent for Microsoft 365 Copilot with your own models or orchestrator or a conversational experience in your own applications, the Copilot APIs provide access to components that power Microsoft 365 Copilot experiences.

## Copilot API capabilities

| API | Use it to | Example scenario |
| --- | --- | --- |
| **Retrieval API** | Retrieve relevant information from Microsoft 365 content in a secure and compliant way. | Connect your own AI models to M365 content without data extraction. Build specialized assistants grounded in your org's docs, policies, KBs while respecting access controls and sensitivity labels. |
| **Search API (preview)** | Hybrid search (semantic + lexical) across OneDrive content with natural language queries. | AI-powered search apps for users to discover relevant documents using natural language vs exact keywords. |
| **Interaction Export API** | Compliance solutions to capture and archive user-Copilot interactions across M365. | Maintain comprehensive AI interaction records, enable monitoring, ensure compliance with org policies and regulatory requirements. |
| **AI Interactions Change Notifications API (preview)** | Subscribe to change notifications for Copilot interactions across M365. | Real-time monitoring/logging of AI interactions for proactive compliance checks, anomaly detection, auditing. |
| **Meeting Insights API** | Extract AI-generated meeting notes, action items, discussion topics for Teams meetings. | Auto-extract action items, decisions, summaries; link to PM tools, CRM, custom workflows. |
| **AI Insights Change Notifications API (preview)** | Subscribe to change notifications for Copilot AI Insights of Teams meetings. | Apps that consume Teams meeting AI Insights for consistent capture of key details. |
| **Chat API (preview)** | Conversational experiences powered by M365 Copilot in custom apps. | Integrate M365 Copilot into enterprise apps that can answer questions, perform tasks, provide guidance based on M365 data + user context. |
| **Copilot usage reports API** | Query user counts and usage data for M365 Copilot in your organization. | Build adoption reports. |
| **Package management API** | View and manage apps and agents across M365. | Inventory all agents and apps within your org. |

## Key benefits

- **Secure grounding, governance and compliance** — Access Microsoft 365's knowledge index directly. All existing permissions, sensitivity labels, compliance controls, audit, logging, monitoring, and policy enforcement are automatically respected.
- **Production-ready AI** — Same production-grade AI capabilities that power Microsoft 365 Copilot. Integrated into Azure AI Foundry, Microsoft Copilot Studio, Microsoft Agents SDK.
- **Responsible AI** — RAI validation checks protect against harmful content.

## Requirements

- **Microsoft 365 Copilot license** required per user accessing M365 Copilot functionality via these APIs.
- **Microsoft 365 subscription**: E3 or E5 (or equivalent) foundation for M365 Copilot.

## REST API integration

Available as standard REST APIs under the Microsoft Graph namespace:

- `graph.microsoft.com/v1.0/copilot`
- `graph.microsoft.com/beta/copilot`

Uses the same authentication/authorization process as other Microsoft Graph APIs. All Copilot APIs respect existing org policies (identity access, conditional access, sensitivity labels, permission trimming) by default.

## Copilot APIs vs. Microsoft Graph APIs

Microsoft Graph APIs generally provide CRUD operations on Microsoft 365 data. The Copilot APIs deliver AI-powered capabilities built on Microsoft 365 data.

- **Microsoft Graph APIs** — manipulate/access data, available under standard M365 license terms
- **Copilot APIs** — AI reasoning over data, require a Microsoft 365 Copilot license

This licensing model delivers higher-value AI capabilities beyond standard data access.
