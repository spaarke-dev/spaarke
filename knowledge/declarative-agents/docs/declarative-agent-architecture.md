---
source: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-architecture
fetched: 2026-05-14
---

# Declarative agent architecture

Declarative agents are conversational AI experiences that result from declared configurations loaded into Microsoft 365 Copilot. You can use declarative agents to create sophisticated AI-powered solutions through configuration rather than custom code, using the full Microsoft 365 ecosystem infrastructure and capabilities.

Declarative agents can include instructions, Microsoft knowledge sources and capabilities such as SharePoint, code interpreter, Microsoft 365 Copilot connectors, and API plugins that connect to external APIs. This configuration-based approach allows organizations to rapidly deploy AI agents while inheriting Microsoft 365 security, compliance, and governance frameworks.

## Architecture components

Declarative agents inherit the Microsoft 365 infrastructure and provide specific capabilities and constraints across different architectural components.

| Component | Considerations | Optimized for | Poor fit for |
| --- | --- | --- | --- |
| Client | Inherits Microsoft 365 client support; minimal developer effort required. Constrained Microsoft support and delivery timelines. | Microsoft 365 ecosystem | Web chat and external clients. |
| Infrastructure | Consider whether the Microsoft 365 infrastructure meets your current needs. | Using existing Microsoft 365 app ecosystem features: policies, governance, catalog, publication, audit, retention, DLP. | Privatized network/storage needs. |
| Catalog | Inherits the Microsoft 365 app catalog and publication process. Less effort for developers but no developer control. | Microsoft 365 app ecosystem catalog | Blended catalog of Microsoft 365 and non-Microsoft 365 agents and clients. |
| Security and compliance | Inherits Microsoft 365 controls and commitments. Reduced developer effort but no developer control. | Microsoft 365 ecosystem organizations that want lower SecOps overhead | Organizations that require different security and compliance postures. |
| Orchestrator and language model | Not within developer's control. | Microsoft 365 Copilot licensed users, Copilot Tuning organizations, and Microsoft 365 Copilot Chat | Complex intent or dictionary use cases. |
| Tool calling | Provides best of both worlds for calling Microsoft owned capabilities and external tools. | Microsoft 365 workloads | On-premises APIs and data sources. Non OpenAPI-based specs. Streaming API workloads. |

> **Note**: Cross prompt injection attacks (XPIA) are a type of security vulnerability in conversational AI systems. Malicious input in one prompt or conversation context manipulates or alters the behavior of the AI in subsequent prompts or sessions. Content filtering and inline disengagement are common mitigations for XPIA.

## Declarative agent data flow

Declarative agents follow a specific data flow pattern where Microsoft manages the orchestration and processing pipeline, while developers control limited configurations.

Developers control components like instructions, knowledge and data sources, and API plugins. Because Microsoft grounding and calls to external tools happen **sequentially**, you can't use chained operations with grounding data or looped operation plans. **This architecture isn't suitable for complex multistep operations.**

The sequential processing model ensures consistent performance and security but limits the types of workflows that you can implement. Design solutions that work within this constraint and don't require iterative reasoning loops.

This model works well for scenarios where the agent can provide complete responses based on a single grounding operation and external tool call. However, it struggles with workflows that require multiple interdependent operations.

### Technical limits

| Limit type | Value | Considerations |
| --- | --- | --- |
| Grounding record limit | 50 items | Affects the amount of contextual data available |
| Plugin response limit | 25 items | Constrains external API response sizes |
| Token limit | 4,096\* | Includes all context and response data |
| Timeout limit | 45 seconds\* | Includes network latency and processing time |

\* Optimize for about 66% of the technical limit.

Given these limitations, declarative agents aren't a good fit for:

- Scenarios that require full document or large data contexts
- Handling a large number of records or paginated results
- Long-running processes that exceed timeout limits

## Advantages and limitations

### Key advantages

- **Infrastructure inheritance**: Agents automatically inherit existing Microsoft deployment, client, governance, and authentication structures.
- **Access to Microsoft capabilities**: Direct access to Microsoft capabilities, including advanced AI features and productivity integrations.
- **Reduced complexity**: Configuration-based approach eliminates the need for custom infrastructure development and maintenance.

### Primary limitations

- **Limited orchestration control**: No iterative reasoning loops or complex workflow orchestration.
- **Restricted customization**: Custom corpus development isn't possible except in limited scenarios such as Copilot Tuning.
- **Sequential processing**: The sequential nature of grounding and tool calling prevents complex multistep operations.

## Use case alignment

### Optimal scenarios

- **Information retrieval**: Search and summarize information from Microsoft 365 or external sources.
- **Simple workflows**: Straightforward processes that single-step operations complete.
- **Productivity enhancement**: Tasks that enhance existing Microsoft 365 workflows without requiring complex orchestration.

### Unsuitable scenarios

- **Complex decision trees**: Workflows that require multiple conditional branches and iterative processing.
- **Large data processing**: Operations that require analysis of extensive datasets or full document contexts.
- **Custom AI models**: Scenarios that require specialized language models or custom training data.
