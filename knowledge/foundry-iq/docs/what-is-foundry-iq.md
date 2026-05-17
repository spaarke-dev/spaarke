---
source: https://learn.microsoft.com/en-us/azure/ai-foundry/agents/concepts/what-is-foundry-iq
canonical_source: https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/what-is-foundry-iq
fetched: 2026-05-14
ms_date: 2026-05-13
ms_git_commit: 93f8a9c3ab6ae448ceac238fd762868a176d0c72
note: |
  Directive URL was https://learn.microsoft.com/en-us/azure/ai-foundry/concepts/knowledge
  which 404s on 2026-05-14. The concept doc was relocated under /agents/concepts/
  during the ai-foundry → foundry URL rebrand.
---

# What is Foundry IQ?

> **Note (Microsoft)**: Some Foundry IQ features are now generally available, while others
> remain in preview. Availability depends on the Search Service REST API version you use.
> The Microsoft Foundry portal and Azure portal continue to provide preview-only access to
> all agentic retrieval features.
>
> For migration guidance, including a breakdown of what's generally available and what
> remains in preview, see "Migrate agentic retrieval code to the latest version".

Agents need context from scattered enterprise content to accurately answer questions. The
Foundry model powering an agent has a knowledge cutoff and can't access your proprietary
data on its own. With Foundry IQ, you can create a configurable, multi-source *knowledge
base* that provides agents with permission-aware responses based on your organization's data.

A knowledge base consists of *knowledge sources* (connections to internal and external
data stores) and parameters that control retrieval behavior. Multiple agents can share the
same knowledge base. When an agent queries the knowledge base, Foundry IQ uses *agentic
retrieval* to process the query, retrieve relevant information, enforce user permissions,
and return grounded answers with citations.

## Capabilities

- Connect one knowledge base to multiple agents. Supported knowledge sources include
  internal data stores (such as Azure Blob Storage, SharePoint, and OneLake) and public
  web data.
- Automate document chunking, vector embedding generation, and metadata extraction for
  indexed knowledge sources. Schedule recurring indexer runs for incremental data refresh.
- Issue keyword, vector, or hybrid queries across indexed and remote knowledge sources.
- Use the agentic retrieval engine with a large language model (LLM) to plan queries,
  select sources, run parallel searches, and aggregate results.
- Return extractive data with citations so agents can reason over raw content and trace
  answers to source documents.
- Synchronize access control lists (ACLs) for supported sources and honor Microsoft Purview
  sensitivity labels. Enforce permissions at query time so agents return only authorized content.
- Run queries under the caller's Microsoft Entra identity for end-to-end permission enforcement.

## Components

A Foundry IQ knowledge base contains knowledge sources and uses agentic retrieval to
process queries. Azure AI Search provides the underlying indexing and retrieval infrastructure.

| Component | Description |
| --- | --- |
| **Knowledge base** | Top-level resource that orchestrates agentic retrieval. Defines which knowledge sources to query and parameters that control retrieval behavior, including the retrieval reasoning effort (minimal, low, or medium) for LLM processing. |
| **Knowledge sources** | Connections to indexed or remote content. A knowledge base references one or more knowledge sources. |
| **Agentic retrieval** | Multi-query pipeline that decomposes complex questions into subqueries, executes them in parallel, semantically reranks results, and returns unified responses. Uses an optional LLM from Azure OpenAI in Foundry Models for query planning. |

You can use Foundry IQ knowledge bases in Foundry Agent Service, Microsoft Agent Framework,
or any custom application by calling the knowledge base APIs from Azure AI Search.

## Workflow

You can set up Foundry IQ through a portal or programmatically. The following steps outline
the typical workflow for both approaches.

### Portal
1. Sign in to Microsoft Foundry. Make sure the **New Foundry** toggle is on.
2. Create a project or select an existing project.
3. From the top menu, select **Build**.
4. On the **Knowledge** tab:
   1. Create or connect to an existing search service that supports agentic retrieval.
   2. Create a knowledge base by adding one knowledge source at a time.
   3. Configure knowledge base properties for retrieval behavior.
5. On the **Agents** tab:
   1. Create or select an existing agent.
   2. Connect to your knowledge base.
   3. Use the playground to send messages and refine your agent.

> The playground provides a simplified workflow for proof-of-concept testing. When you move
> to code, configure managed identities and permissions to meet your organization's security
> requirements.

### Programmatic
1. Create knowledge sources.
2. Create a knowledge base that references your knowledge sources.
3. Connect an agent to your knowledge base.
4. Send messages and refine your agent.

## Relationship to Fabric IQ and Work IQ

Microsoft provides three IQ workloads that give agents access to different aspects of your
organization:

- **Fabric IQ** is a semantic intelligence layer for Microsoft Fabric. It models business
  data (ontologies, semantic models, and graphs) so agents can reason over analytics in
  OneLake and Power BI.
- **Work IQ** is a contextual intelligence layer for Microsoft 365. It captures
  collaboration signals from documents, meetings, chats, and workflows, providing agents
  with insight into how your organization operates.
- **Foundry IQ** is a managed knowledge layer for enterprise data. It connects structured
  and unstructured data across Azure, SharePoint, OneLake, and the web so agents can access
  permission-aware knowledge.

Each IQ workload is standalone, but you can use them together to provide comprehensive
organizational context for agents.

## Get started

- Watch the introduction session (YouTube `slDdNIQCJBQ`) and the deep dive video (YouTube
  `uDVkcZwB0EU`).
- For minimum costs and proof-of-concept testing, start with the Microsoft Foundry portal.
  You can use the free tier for Azure AI Search and a free allocation of tokens for
  agentic retrieval.
- For step-by-step integration guidance, see "Connect a Foundry IQ knowledge base to
  Foundry Agent Service".
- Review application code in the Azure OpenAI demo (`Azure-Samples/azure-search-openai-demo`),
  which uses agentic retrieval.
