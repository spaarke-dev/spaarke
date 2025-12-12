# AI Foundry Infrastructure

This directory contains the Azure AI Foundry configuration for Spaarke Document Intelligence.

## Directory Structure

```
ai-foundry/
├── connections/                 # Connection YAML definitions
│   ├── azure-openai-connection.yaml
│   └── ai-search-connection.yaml
├── prompt-flows/               # Prompt Flow definitions
│   ├── analysis-execute/       # Main analysis execution flow
│   │   ├── flow.dag.yaml
│   │   ├── build_system_prompt.py
│   │   ├── build_user_prompt.py
│   │   ├── generate_analysis.jinja2
│   │   └── requirements.txt
│   └── analysis-continue/      # Conversational continuation flow
│       ├── flow.dag.yaml
│       ├── build_continuation_prompt.py
│       ├── generate_continuation.jinja2
│       └── requirements.txt
└── evaluation/                 # Evaluation configuration
    ├── eval-config.yaml
    ├── metrics/
    │   ├── format_compliance.py
    │   └── completeness.py
    └── test-data/
        └── sample-evaluations.jsonl
```

## Deployed Resources

| Resource | Name | Location | Purpose |
|----------|------|----------|---------|
| AI Foundry Hub | sprkspaarkedev-aif-hub | West US 2 | Central management, shared connections |
| AI Foundry Project | sprkspaarkedev-aif-proj | West US 2 | Prompt flows, experiments, deployments |
| Storage Account | sprkspaarkedevaifsa | West US 2 | Flow artifacts, model files |
| Key Vault | sprkspaarkedev-aif-kv | West US 2 | Connection secrets |
| Application Insights | sprkspaarkedev-aif-insights | West US 2 | Runtime monitoring, metrics |
| Log Analytics | sprkspaarkedev-aif-logs | West US 2 | Diagnostic logs |

## Component Relationships

```
                    ┌─────────────────────────────────────────────┐
                    │           Azure Subscription                 │
                    │       Resource Group: spe-infrastructure-    │
                    │                westus2                       │
                    └─────────────────────────────────────────────┘
                                         │
        ┌────────────────────────────────┼────────────────────────────────┐
        │                                │                                │
        ▼                                ▼                                ▼
┌──────────────────┐        ┌──────────────────┐        ┌──────────────────┐
│  Azure OpenAI    │        │   AI Foundry     │        │  Azure AI Search │
│ spaarke-openai-  │◄───────│      Hub         │───────►│ spaarke-search-  │
│      dev         │        │ sprkspaarkedev-  │        │      dev         │
│                  │        │   aif-hub        │        │                  │
│ • gpt-4o-mini    │        │                  │        │ • spaarke-       │
│   deployment     │        │ Managed Identity │        │   records-index  │
└──────────────────┘        │ for connections  │        └──────────────────┘
                            └────────┬─────────┘
                                     │
                    ┌────────────────┴────────────────┐
                    │                                 │
                    ▼                                 ▼
        ┌──────────────────┐              ┌──────────────────┐
        │    AI Foundry    │              │   Supporting     │
        │     Project      │              │   Resources      │
        │ sprkspaarkedev-  │              │                  │
        │   aif-proj       │              │ • Storage        │
        │                  │              │ • Key Vault      │
        │ • Prompt Flows   │              │ • App Insights   │
        │ • Evaluations    │              │ • Log Analytics  │
        │ • Experiments    │              │                  │
        └──────────────────┘              └──────────────────┘
                            │
                            │ Future: BFF API integration
                            ▼
        ┌──────────────────────────────────────────────────────────┐
        │                     BFF API                              │
        │              spe-api-dev-67e2xz                          │
        │                                                          │
        │  Current: Direct Azure OpenAI calls                      │
        │  Future: AI Foundry endpoint for prompt flow execution   │
        └──────────────────────────────────────────────────────────┘
```

### Integration Points

| From | To | Method | Status |
|------|-----|--------|--------|
| AI Foundry Hub | Azure OpenAI | Workspace Connection (Managed ID) | Deployed |
| AI Foundry Hub | Azure AI Search | Workspace Connection (Managed ID) | Deployed |
| AI Foundry Project | Hub | Inherited connections | Deployed |
| BFF API | AI Foundry | Prompt Flow Endpoint | Future |
| BFF API | Azure OpenAI | Direct API calls | Current |

### Authentication Flow

1. **Hub Level**: Uses System-Assigned Managed Identity
2. **Connections**: Authenticate via Managed Identity to connected services
3. **Future BFF Integration**: BFF will call AI Foundry endpoints with its own managed identity

## Connections

| Connection | Type | Target |
|------------|------|--------|
| azure-openai-connection | Azure OpenAI | spaarke-openai-dev |
| ai-search-connection | Azure AI Search | spaarke-search-dev |

## Prompt Flows

### analysis-execute

Main analysis flow that processes documents with configurable actions and scopes.

**Inputs:**
- `document_text`: Extracted text from the document
- `action_system_prompt`: System prompt from selected action
- `skills_instructions`: Combined skill instructions
- `knowledge_context`: Reference materials
- `output_format`: "markdown" or "structured_json"
- `max_tokens`: Maximum response tokens (default: 4096)

**Outputs:**
- `analysis_result`: The generated analysis
- `token_usage`: Token usage statistics

### analysis-continue

Conversational continuation flow for refining analysis results.

**Inputs:**
- `working_document`: Current analysis result
- `chat_history`: Previous conversation messages
- `user_message`: New refinement request
- `max_history_messages`: History limit (default: 10)
- `max_tokens`: Maximum response tokens (default: 4096)

**Outputs:**
- `refined_analysis`: Updated analysis
- `token_usage`: Token usage statistics

## Evaluation

The evaluation pipeline uses both GPT-based and custom metrics:

### GPT-Based Metrics
- **Groundedness** (threshold: 3.5/5): How well grounded in source document
- **Relevance** (threshold: 3.5/5): Relevance to document content
- **Coherence** (threshold: 4.0/5): Logical flow and consistency
- **Fluency** (threshold: 4.0/5): Language quality and readability

### Custom Metrics
- **Format Compliance** (threshold: 90%): Follows requested format
- **Completeness** (threshold: 85%): Contains required sections

## Deployment Commands

### Deploy AI Foundry Hub
```bash
az deployment group create \
  --resource-group spe-infrastructure-westus2 \
  --template-file ../bicep/stacks/ai-foundry-stack.bicep \
  --parameters customerId=spaarke environment=dev location=westus2
```

### Create Connections
```bash
az ml connection create \
  --file connections/azure-openai-connection.yaml \
  --workspace-name sprkspaarkedev-aif-hub \
  --resource-group spe-infrastructure-westus2

az ml connection create \
  --file connections/ai-search-connection.yaml \
  --workspace-name sprkspaarkedev-aif-hub \
  --resource-group spe-infrastructure-westus2
```

### Deploy Prompt Flows
```bash
# From AI Foundry Studio (https://ai.azure.com):
# 1. Navigate to the project
# 2. Import the prompt flow directories
# 3. Create deployments for each flow

# Or via CLI (requires promptflow SDK):
pf flow create --file prompt-flows/analysis-execute/flow.dag.yaml \
  --workspace sprkspaarkedev-aif-proj \
  --resource-group spe-infrastructure-westus2

pf flow create --file prompt-flows/analysis-continue/flow.dag.yaml \
  --workspace sprkspaarkedev-aif-proj \
  --resource-group spe-infrastructure-westus2
```

### Run Evaluation
```bash
pf run create --flow prompt-flows/analysis-execute \
  --data evaluation/test-data/sample-evaluations.jsonl \
  --workspace sprkspaarkedev-aif-proj \
  --resource-group spe-infrastructure-westus2

# Run evaluation on results
pf evaluation run --config evaluation/eval-config.yaml \
  --run <run-name> \
  --workspace sprkspaarkedev-aif-proj
```

## Portal Access

- **AI Foundry Studio**: https://ai.azure.com/build/overview?wsid=/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.MachineLearningServices/workspaces/sprkspaarkedev-aif-proj
