---
source: https://learn.microsoft.com/en-us/azure/ai-foundry/agents/durable-orchestration
canonical: https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/hosted-agents
fetched: 2026-05-14
note: Original URL (`/agents/durable-orchestration`) returns 404. The "Durable Agent Orchestration" page no longer exists as a standalone concept — durable orchestration is now part of the Hosted Agents preview surface (session resume, $HOME persistence, idle-timeout deprovisioning). This snapshot captures the current canonical "Hosted agents" page which describes the durable execution model.
---

# Hosted agents in Foundry Agent Service (preview)

Hosted agents call models from the Foundry model catalog to perform reasoning while your custom code handles orchestration. You package your agent as a container image, push it to Azure Container Registry, and Agent Service handles deployment, scaling, identity, and observability.

## When to use hosted agents

Choose Hosted agents over prompt-based agents when you need to:

- **Bring your own code** — use any framework (Agent Framework, LangGraph, Semantic Kernel, or custom code) rather than prompt-only definitions.
- **Use custom protocols** — accept webhooks or non-OpenAI payloads via the Invocations protocol.
- **Control compute resources** — specify CPU and memory for your agent's sandbox.
- **Run stateful workloads** — persist files and state across turns via `$HOME` and the `/files` endpoint.

## How it works

You package your agent as a container image and push it to Azure Container Registry. When you deploy, Agent Service pulls the image, provisions compute, assigns a dedicated Microsoft Entra ID (agent identity), and exposes a dedicated endpoint. At runtime, your agent code handles requests from clients and can call Foundry models, Toolbox tools, and downstream Azure services using its agent identity. The platform handles scaling, session state persistence, observability, and lifecycle management.

## Key concepts

### Isolation model

Hosted agents run in **per-session VM-isolated sandboxes**. Each session gets a dedicated sandbox with a persistent filesystem (`$HOME` and `/files`), enabling scale-to-zero with stateful resume and predictable cold starts.

### Protocols: Responses and Invocations

Hosted agent containers expose one or both of two protocols.

| Scenario | Protocol | Why |
| --- | --- | --- |
| Conversational chatbot or assistant | **Responses** | Platform manages conversation history, streaming events, session lifecycle. |
| Multi-turn Q&A with RAG or tools | **Responses** | Built-in conversation ID threading. |
| Background / async processing | **Responses** | `background: true` with platform-managed polling and cancellation. |
| Agent published to Teams or M365 | **Responses** + **Activity** | Platform automatically bridges Responses to Activity for channel delivery. |
| Webhook receiver (GitHub, Stripe, Jira) | **Invocations** | External system sends its own payload format. |
| Non-conversational processing (classification, extraction, batch) | **Invocations** | Input is structured data, not a chat message. |
| Custom streaming protocol (AG-UI, etc.) | **Invocations** | Non-OpenAI-compatible — needs raw SSE control. |
| Protocol bridge (GitHub Copilot, proprietary) | **Invocations** | Caller has its own protocol that doesn't map to `/responses`. |

#### Protocol comparison

| - | **Responses** | **Invocations** |
| --- | --- | --- |
| **Best for** | Most agents — platform manages history, streaming, background execution | Agents that need full HTTP control, custom payloads, or long-running async workflows |
| **Payload** | OpenAI-compatible `/responses` contract | Arbitrary JSON via `/invocations` — you define the schema |
| **Client SDK** | Any OpenAI-compatible SDK | Custom client — you define the contract |
| **Session history** | Platform-managed via conversation ID | You manage sessions (in-memory, Cosmos DB, etc.) |
| **Streaming** | Platform-managed `ResponseEventStream` | Raw SSE — you format and write events |
| **Background / long-running** | Built-in (`background: true`) | Manual task tracking and custom polling endpoints |

#### Additional protocols

Hosted agents also support the **Activity** protocol for Teams and M365, and the **A2A** protocol for agent-to-agent delegation. All four protocols (Responses, Invocations, Activity, A2A) can be combined in a single agent.

### Agent identity and endpoint

Every Hosted agent gets a dedicated Microsoft Entra ID and a dedicated endpoint, both created automatically at deploy time:

- **Responses**: `{project_endpoint}/agents/{name}/endpoint/protocols/openai/v1/responses`
- **Invocations**: `{project_endpoint}/agents/{name}/endpoint/protocols/invocations`
- **A2A (preview)**: `{project_endpoint}/agents/{name}/endpoint/protocols/a2a`

Two identities are involved:

| Identity | Scope | Purpose |
| --- | --- | --- |
| **Microsoft Entra ID** (agent identity, per-agent) | Created automatically at deploy time | Identity the agent container authenticates with at runtime. Used for model invocation, tool access, downstream Azure services. |
| **Project managed identity** (project-wide) | System-assigned on the Foundry project | Used by the platform for infrastructure operations (e.g., Container Registry Repository Reader). Not the agent's runtime identity. |

When integrated via M365 channels, Hosted agents can also operate with on-behalf-of (OBO) user identity.

### Sessions and conversations

#### Sessions

- **State persistence**: `$HOME` and `/files` content persist across turns and across idle periods. When compute goes idle and is brought back, the session's state is automatically restored.
- **Isolation**: Each session is isolated from other sessions.
- **Session lifetime**: Up to **30 days**. Idle timeout is **15 minutes** — if no request arrives, platform deprovisions compute and persists session state.

#### Conversations

- **Persistence**: Conversation history stored in Foundry, independent of compute state.
- **Cross-channel access**: Users access the same conversation from playground, API, Teams, or other published channels.

#### How sessions/conversations work per protocol

- **Responses protocol**: conversation ID is primary. Platform manages conversation history; session ID associated automatically.
- **Invocations protocol**: session ID is primary. Client manages session ID directly; no platform-managed conversation history — you manage state in your own code.

#### Session compute lifecycle

| State | What happens |
| --- | --- |
| **Active** | Compute running. Requests routed. `$HOME` and `/files` available. |
| **Idle** | No requests for 15 minutes. Compute deprovisioned. State persisted. |
| **Resumed** | Same session ID referenced again. Platform provisions new compute and restores persisted state. |

## Security and data handling

Treat a Hosted agent like production application code.

- **Don't put secrets in container images or environment variables**. Use managed identities and connections, and store secrets in a managed secret store.
- **Be careful with non-Microsoft tools and servers**. If your agent calls tools backed by non-Microsoft services, data might flow to those services. Review data sharing, retention, and location policies.

## Platform details

### Versioning

Each call to create a version produces an **immutable agent version** — a snapshot of the container image, resource allocation, environment variables, and protocol configuration. You can split traffic between versions with weighted rollouts for canary and blue-green deployments.

### Observability

The platform automatically injects an Application Insights connection string into your agent container. Agents that use the protocol libraries emit OpenTelemetry traces by default.

### Toolbox in Foundry

Hosted agents access Foundry-managed tools (Code Interpreter, Web Search, Azure AI Search, OpenAPI, custom MCP connections, A2A) through a **Toolbox MCP endpoint** provisioned in your Foundry project. Your agent code connects to this endpoint using standard MCP client libraries.

### Language support

Hosted agents support **Python** and **C#**. Any agent framework can be used — protocol libraries are framework-agnostic. Samples: https://github.com/microsoft-foundry/foundry-samples/tree/main/samples/python/hosted-agents

### Sandbox sizes

CPU and memory allocations range from **0.25 vCPU / 0.5 GiB** to **2 vCPU / 4 GiB**.

### Private networking

Hosted agents support deployment within network-isolated Foundry resources and can use a customer-provided Azure VNet for outbound traffic. Note: The ACR holding your agent image must currently remain reachable over its public endpoint.

## Limits, pricing, availability (preview)

| Limit | Scope | Default | Adjustable |
| --- | --- | --- | --- |
| Maximum active concurrent sessions | per subscription per region | 50 | Yes, with quota requests to Microsoft Support |

### Region availability (preview, expanding)

East US 2, North Central US, Sweden Central, Canada Central, Southeast Asia, Poland Central, South Africa North, Korea Central, South India, Brazil South, West US, West US 3, Norway East, Japan East, France Central, Switzerland North, Spain Central, Australia East.
