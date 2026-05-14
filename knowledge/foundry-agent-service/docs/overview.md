---
source: https://learn.microsoft.com/en-us/azure/ai-foundry/agents/overview
canonical: https://learn.microsoft.com/en-us/azure/foundry/agents/overview
fetched: 2026-05-14
note: Original URL (`ai-foundry/agents/overview`) redirects to canonical (`foundry/agents/overview`). Page rebranded from "Azure AI Foundry Agent Service" to "Microsoft Foundry Agent Service".
---

# What is Microsoft Foundry Agent Service?

Foundry Agent Service is a fully managed platform for building, deploying, and scaling AI agents. Use any framework and [many models](concepts/limits-quotas-regions) from the Foundry model catalog. Create no-code prompt agents in the Foundry portal, or use the available SDKs and REST API to deploy them and code-based **[Hosted agents](concepts/hosted-agents)** built with Agent Framework, LangGraph, or your own code.

Agent Service handles hosting, scaling, identity, observability, and enterprise security so you can focus on your agent logic.

## What is an agent?

An agent is an AI application that uses a model from the Foundry model catalog to reason about user requests and take autonomous actions to fulfill them. Unlike a simple chatbot that only generates text, an agent can call tools, access external data, and make decisions across multiple steps to complete a task. Every agent combines three core components:

- **Model**: A model from the Foundry model catalog that provides reasoning and language capabilities.
- **Instructions**: Define goals, constraints, and behavior. In Foundry, instructions can be prompt-based, workflow definitions, or Hosted agent code.
- **Tools**: Provide access to data or actions, such as search, file operations, or API calls.

## Agent Service at a glance

| Component | What it does |
| --- | --- |
| **Agent Runtime** | Hosts and scales both prompt agents and Hosted agents. Manages conversations, tool calls, and agent lifecycle. |
| **Tools** | Built-in tools including web search, file search, memory, code interpreter, MCP servers, and custom functions. Extend your agent's capabilities without building infrastructure. Tools have managed authentication, including service managed credentials and On-Behalf-Of (OBO) authentication. Some MCP servers, such as Azure DevOps MCP Server (preview), require connecting an organization during setup. Access can be scoped through Foundry tool configuration. |
| **Models** | Works with many models from the Foundry model catalog, such as GPT-4o, Llama, and DeepSeek. Swap models without changing your agent code. |
| **Observability** | End-to-end tracing, metrics, and Application Insights integration. See every decision your agent makes. |
| **Identity & Security** | Microsoft Entra identity, RBAC, content filters, and virtual network isolation. Enterprise-grade trust built in. |
| **Publishing** | Version agents, create stable endpoints, and share through Microsoft Teams, Microsoft 365 Copilot, and the Entra Agent Registry. |

## Get started with agents

- **New to agents?** Start with a prompt agent to create an agent with instructions and tools. Use the Foundry portal to create one with no code required, or use the SDKs or REST API.
- **Want to deploy an agent as a container with a framework of your choice?** Build a Hosted agent with Agent Framework or LangGraph, deploy it to Foundry, and test it end-to-end.
- **Want to orchestrate multiple agents?** Build a workflow to orchestrate agents and business logic in a visual builder.

## Agent types

Agent Service supports three types of agents, each designed for different needs:

- Prompt agents
- Workflow agents (preview)
- Hosted agents (preview)

### Prompt agents

Prompt agents are defined entirely through configuration — instructions, model selection, and tools. Create them in the Foundry portal or through the API or SDKs, and Agent Service handles the orchestration and hosting automatically.

**Best for**: Rapid prototyping, internal tools, and agents that don't need custom orchestration logic. Create a working agent in minutes using the portal.

### Workflow agents (preview)

Workflow agents orchestrate a sequence of actions or coordinate multiple agents using declarative definitions. Build workflows visually in the Foundry portal or define them in YAML through Visual Studio Code. Workflows support branching logic, human-in-the-loop steps, and sequential or group-chat patterns.

**Best for**: Multi-step orchestration, agent-to-agent coordination, approval workflows, and scenarios that need repeatable automation without custom code.

### Hosted agents (preview)

Hosted agents are code-based agents built with a framework of your choice and deployed as containers on Agent Service. You write the orchestration logic — tool calls, multi-step reasoning, agent-to-agent coordination — and Foundry manages the runtime, scaling, and infrastructure.

> Hosted agents are currently in public preview.

**Best for**: Complex workflows, custom tool integrations, multi-agent systems, and scenarios where you need full control over agent behavior.

### Compare agent types

| - | Prompt agents | Workflow agents | Hosted agents (preview) |
| --- | --- | --- | --- |
| **Code required** | No | No (YAML optional) | Yes |
| **Hosting** | Fully managed | Fully managed | Container-based, managed |
| **Orchestration** | Single agent | Multi-agent, branching | Custom logic |
| **Best for** | Prototyping, simple tasks | Multi-step automation | Full control, custom frameworks |

## Tools

Agent Service provides built-in tools and supports custom tools so your agents can take actions and access data.

Foundry supports remote MCP servers that you can add from the **Add Tools** catalog in the Foundry portal. You can also connect custom MCP servers hosted on Azure Functions using the Functions MCP webhook endpoint (`/runtime/webhooks/mcp`) to expose custom tools to your agents.

Supported authentication options for connecting MCP servers include:

- Key-based access
- Microsoft Entra (using the agent's managed identity or the project's managed identity)
- OAuth identity passthrough (On-Behalf-Of)
- Unauthenticated access, where appropriate

### Toolbox (preview)

Toolbox lets you define a curated set of tools once, manage them centrally in Foundry, and expose them through a single MCP-compatible endpoint. Any MCP-compatible agent runtime or client can consume a toolbox, regardless of the framework you use. Toolbox versioning gives you explicit control over when changes take effect.

## Development lifecycle

Agent Service supports the full build-test-deploy-monitor workflow:

1. **Create** — Define a prompt agent in the portal or build a Hosted agent in code.
2. **Test** — Chat with your agent in the agents playground or run locally. MCP server integrations can be exercised directly in the playground via chat prompts.
3. **Trace** — Inspect every model call, tool invocation, and decision with agent tracing.
4. **Evaluate** — Run evaluations to measure quality and catch regressions.
5. **Publish** — Promote your agent to a managed resource with a stable endpoint.
6. **Monitor** — Track performance and reliability with service metrics and dashboards.

## Enterprise capabilities

- **Agent identity** — Each agent can have a dedicated Microsoft Entra identity, enabling secure, scoped access to resources and APIs without sharing credentials. Agent identities can authenticate to external MCP servers, and OAuth On-Behalf-Of (OBO) passthrough is supported when configured.
- **Private networking** — Run agents within your Azure virtual network for full network isolation. Hosted agents support bring-your-own Azure Virtual Network (BYO VNet).
- **Role-based access control** — Fine-grained permissions through Microsoft Entra and Azure RBAC.
- **Content safety** — Integrated content filters help mitigate prompt injection risks (including cross-prompt injection) and prevent unsafe outputs.

## Publishing and sharing

- **Versioning** — As you iterate on your agent, versions are automatically snapshotted. Roll back to any previous version or compare changes between versions.
- **Publishing** — Promote an agent to a managed resource with a stable endpoint. Published agents inherit the enterprise identity and access controls configured for your project.
- **Distribution** — Share published agents through Microsoft 365 Copilot and Teams and the Entra Agent Registry. Foundry Agent Service supports the OpenResponses and Activity Protocols for Microsoft 365 publishing, an Invocations protocol for flexible endpoint integration, and the **A2A protocol (preview)** for agent-to-agent communication.

## Security, privacy, and compliance

- **Safety controls**: Integrated guardrails to help reduce unsafe outputs and mitigate prompt injection risks (XPIA).
- **Network isolation and data residency controls**: Use virtual networks and bring-your-own resources.
- **Bring your own resources**: Use your own Azure resources (for example, storage, Azure AI Search, and Azure Cosmos DB for conversation state).
