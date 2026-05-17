---
source: https://learn.microsoft.com/en-us/agent-framework/agents/
fetched: 2026-05-14
note: The directive URL `/concepts/agents` returned 404; the canonical concept page is `/agents/`.
---

# Microsoft Agent Framework Agent Types | Microsoft Learn

The Microsoft Agent Framework provides support for several types of agents to accommodate different use cases and requirements.

### C#

All agents are derived from a common base class, `AIAgent`, which provides a consistent interface for all agent types. This allows for building common, agent agnostic, higher level functionality such as multi-agent orchestrations.

### Python

All agents are derived from a common base class, `Agent`, which provides a consistent interface for all agent types. This allows for building common, agent agnostic, higher level functionality such as multi-agent orchestrations.

## Default Agent Runtime Execution Model

All agents in the Microsoft Agent Framework execute using a structured runtime model. This model coordinates user interaction, model inference, and tool execution in a deterministic loop.

---

## C# — Simple agents based on inference services

Agent Framework makes it easy to create simple agents based on many different inference services. Any inference service that provides a `Microsoft.Extensions.AI.IChatClient` implementation can be used to build these agents. The `Microsoft.Agents.AI.ChatClientAgent` is the agent class used to provide an agent for any `IChatClient` implementation.

These agents support a wide range of functionality out of the box:

1. Function calling.
2. Multi-turn conversations with local chat history management or service provided chat history management.
3. Custom service provided tools (for example, MCP, Code Execution).
4. Structured outputs.

To create one of these agents, simply construct a `ChatClientAgent` using the `IChatClient` implementation of your choice.

```csharp
using Microsoft.Agents.AI;

var agent = new ChatClientAgent(chatClient, instructions: "You are a helpful assistant");
```

To make creating these agents even easier, Agent Framework provides helpers for many popular services.

| Underlying inference service | Description | Service chat history storage supported | InMemory/Custom chat history storage supported |
| --- | --- | --- | --- |
| Microsoft Foundry Agent | An agent that uses the Foundry Agent Service as its backend. | Yes | No |
| Foundry Models ChatCompletion | An agent that uses any of the models deployed in the Foundry Service as its backend via ChatCompletion. | No | Yes |
| Foundry Models Responses | An agent that uses any of the models deployed in the Foundry Service as its backend via Responses. | Yes | Yes |
| Foundry Anthropic | An agent that uses a Claude model via the Foundry Anthropic Service as its backend. | No | Yes |
| Azure OpenAI ChatCompletion | An agent that uses the Azure OpenAI ChatCompletion service. | No | Yes |
| Azure OpenAI Responses | An agent that uses the Azure OpenAI Responses service. | Yes | Yes |
| Anthropic | An agent that uses a Claude model via the Anthropic Service as its backend. | No | Yes |
| OpenAI ChatCompletion | An agent that uses the OpenAI ChatCompletion service. | No | Yes |
| OpenAI Responses | An agent that uses the OpenAI Responses service. | Yes | Yes |
| Any other `IChatClient` | You can also use any other `Microsoft.Extensions.AI.IChatClient` implementation to create an agent. | Varies | Varies |

## C# — Complex custom agents

It's also possible to create fully custom agents that aren't just wrappers around an `IChatClient`. The agent framework provides the `AIAgent` base type. This base type is the core abstraction for all agents, which, when subclassed, allows for complete control over the agent's behavior and capabilities.

## C# — Proxies for remote agents

Agent Framework provides out of the box `AIAgent` implementations for common service hosted agent protocols, such as A2A. This way you can easily connect to and use remote agents from your application.

| Protocol | Description |
| --- | --- |
| A2A | An agent that serves as a proxy to a remote agent via the A2A protocol. |

## C# — Azure and OpenAI SDK Options Reference

When using Foundry, Azure OpenAI, OpenAI services, or Anthropic services, you have various SDK options to connect to these services.

| AI service | SDK | Nuget | Url |
| --- | --- | --- | --- |
| Foundry Models | Azure OpenAI SDK | Azure.AI.OpenAI | `https://ai-foundry-<resource>.services.ai.azure.com/` |
| Foundry Models | OpenAI SDK | OpenAI | `https://ai-foundry-<resource>.services.ai.azure.com/openai/v1/` |
| Foundry Models | Azure AI Inference SDK | Azure.AI.Inference | `https://ai-foundry-<resource>.services.ai.azure.com/models` |
| Foundry Agents | Azure AI Projects SDK + Microsoft.Agents.AI.Foundry | Azure.AI.Projects / Microsoft.Agents.AI.Foundry | `https://ai-foundry-<resource>.services.ai.azure.com/api/projects/ai-project-<project>` |
| Azure OpenAI | Azure OpenAI SDK | Azure.AI.OpenAI | `https://<resource>.openai.azure.com/` |
| Azure OpenAI | OpenAI SDK | OpenAI | `https://<resource>.openai.azure.com/openai/v1/` |
| OpenAI | OpenAI SDK | OpenAI | No url required |
| Microsoft Foundry Anthropic | Anthropic Foundry SDK | Anthropic.Foundry | Resource name required |
| Anthropic | Anthropic SDK | Anthropic | No url or resource name required |

### Using the OpenAI SDK

If a custom URL is required, you can set it via the `OpenAIClientOptions`.

```csharp
var clientOptions = new OpenAIClientOptions() { Endpoint = new Uri(serviceUrl) };
```

It's possible to use an API key when creating the client.

```csharp
OpenAIClient client = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
```

When using an Azure Service, it's also possible to use Azure credentials instead of an API key.

```csharp
OpenAIClient client = new OpenAIClient(new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"), clientOptions)
```

> **Warning**: `DefaultAzureCredential` is convenient for development but requires careful consideration in production. In production, consider using a specific credential (e.g., `ManagedIdentityCredential`) to avoid latency issues, unintended credential probing, and potential security risks from fallback mechanisms.

Once you have created the OpenAIClient, you can get a sub client for the specific service you want to use and then create an `AIAgent` from that.

```csharp
AIAgent agent = client
    .AsAIAgent(model: model, instructions: "You are good at telling jokes.", name: "Joker");
```

### Using the Azure AI Projects SDK

```csharp
AIAgent agent = new AIProjectClient(
    new Uri(serviceUrl),
    new DefaultAzureCredential())
     .AsAIAgent(
         model: deploymentName,
         instructions: "You are good at telling jokes.",
         name: "Joker");
```

### Using the Azure AI Projects SDK with Foundry Agents

```csharp
var aiProjectClient = new AIProjectClient(new Uri(serviceUrl), new DefaultAzureCredential());
AIAgent agent = aiProjectClient.AsAIAgent(
    model: deploymentName,
    instructions: "You are good at telling jokes.",
    name: "Joker");
```

### Using the Foundry Anthropic SDK

```csharp
var client = new AnthropicFoundryClient(new AnthropicFoundryApiKeyCredentials(apiKey, resource));
AIAgent agent = client.AsAIAgent(
    model: deploymentName,
    instructions: "Joker",
    name: "You are good at telling jokes.");
```

### Using the Anthropic SDK

```csharp
var client = new AnthropicClient() { ApiKey = apiKey };
AIAgent agent = client.AsAIAgent(
    model: deploymentName,
    instructions: "Joker",
    name: "You are good at telling jokes.");
```

---

## Python — Simple agents based on inference services

Agent Framework makes it easy to create simple agents based on many different inference services. Any inference service that provides a chat client implementation can be used to build these agents. This can be done using the `SupportsChatGetResponse` protocol, which defines a standard for the methods that a client needs to support to be used with the standard `Agent` class.

These agents support a wide range of functionality out of the box:

1. Function calling
2. Multi-turn conversations with local chat history management or service provided chat history management
3. Custom service provided tools (for example, MCP, Code Execution)
4. Structured outputs
5. Streaming responses

```python
import os
from agent_framework import Agent
from agent_framework.foundry import FoundryChatClient
from azure.identity.aio import DefaultAzureCredential

agent = Agent(
    client=FoundryChatClient(
        credential=DefaultAzureCredential(),
        project_endpoint=os.getenv("FOUNDRY_PROJECT_ENDPOINT"),
        model=os.getenv("FOUNDRY_MODEL"),
    ),
    instructions="You are a helpful assistant",
)
response = await agent.run("Hello!")
```

Alternatively, use the convenience method on the chat client:

```python
from agent_framework.foundry import FoundryChatClient
from azure.identity.aio import DefaultAzureCredential

agent = FoundryChatClient(
    credential=DefaultAzureCredential(),
    project_endpoint=os.getenv("FOUNDRY_PROJECT_ENDPOINT"),
    model=os.getenv("FOUNDRY_MODEL"),
).as_agent(
    instructions="You are a helpful assistant"
)
```

### Streaming Responses

```python
# Regular response (wait for complete result)
response = await agent.run("What's the weather like in Seattle?")
print(response.text)

# Streaming response (get results as they are generated)
async for chunk in agent.run("What's the weather like in Portland?", stream=True):
    if chunk.text:
        print(chunk.text, end="", flush=True)
```

### Function Tools

```python
import os
from typing import Annotated
from azure.identity.aio import DefaultAzureCredential
from agent_framework.foundry import FoundryChatClient

def get_weather(location: Annotated[str, "The location to get the weather for."]) -> str:
    """Get the weather for a given location."""
    return f"The weather in {location} is sunny with a high of 25°C."

async with DefaultAzureCredential() as credential:
    agent = FoundryChatClient(
        credential=credential,
        project_endpoint=os.getenv("FOUNDRY_PROJECT_ENDPOINT"),
        model=os.getenv("FOUNDRY_MODEL"),
    ).as_agent(
        instructions="You are a helpful weather assistant.",
        tools=get_weather,
    )
    response = await agent.run("What's the weather in Seattle?")
```

### Custom agents

For fully custom implementations (for example deterministic agents or API-backed agents), see Custom Agents. That page covers implementing `SupportsAgentRun` or extending `BaseAgent`, including streaming updates with `AgentResponseUpdate`.

### Other agent types

| Agent Type | Description |
| --- | --- |
| A2A | A proxy agent that connects to and invokes remote A2A-compliant agents. |
