---
source: https://learn.microsoft.com/en-us/azure/ai-foundry/agents/workflows
canonical: https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/workflow
fetched: 2026-05-14
note: Original URL (`/agents/workflows`) returns 404. Canonical doc lives at `/agents/concepts/workflow`. The workflow surface is UI-first (Foundry portal visual builder) with optional YAML export edited in VS Code via the `vs-code-agents-workflow-low-code` / `vs-code-agents-workflow-pro-code` extensions. No directly-readable Python "workflow DSL" exists yet in public samples.
---

# Build a workflow in Microsoft Foundry

Workflows are UI-based tools in Microsoft Foundry. Use them to create declarative, predefined sequences of actions that orchestrate agents and business logic in a visual builder.

## Decide when to use workflows

Workflows are ideal for scenarios where you need to:

- Orchestrate multiple agents in a repeatable process.
- Add branching logic (for example, if/else) and variable handling without writing code.
- Create human-in-the-loop steps (for example, approvals or clarifying questions).

If you want to edit workflow YAML in Visual Studio Code or run workflows in a local playground, see:

- Work with Declarative (Low-code) Agent workflows in Visual Studio Code
- Work with Hosted (Pro-code) Agent workflows in Visual Studio Code

## Understand workflow patterns

Foundry provides templates for common orchestration patterns. Start with a blank workflow or select a template:

| Pattern | Description | Typical use case |
| --- | --- | --- |
| Human in the loop | Asks the user a question and awaits user input to proceed | Creating approval requests during workflow execution and waiting for human approval, or obtaining information from the user |
| Sequential | Passes the result from one agent to the next in a defined order | Step-by-step workflows, pipelines, or multiple-stage processing |
| Group chat | Dynamically passes control between agents based on context or rules | Dynamic workflows, escalation, fallback, or expert handoff scenarios |

For more information, see Microsoft Agent Framework workflow orchestrations.

## Create a workflow

1. Sign in to Microsoft Foundry. Make sure the **New Foundry** toggle is on.
2. On the upper-right menu, select **Build**.
3. Select **Create new workflow** > **Sequential** (or another template).
4. Assign an agent to each agent node — pick existing or create new.
5. Select **Save** in the visualizer to save the changes. Foundry **does not save workflows automatically**.
6. Select **Run Workflow**.
7. Interact with the workflow in the chat window.

## Add nodes

Nodes are the building blocks of your workflow. Common node types:

- **Agent**: Invoke an agent.
- **Logic**: Use *if/else*, *go to*, or *for each*.
- **Data transformation**: Set a variable or parse a value.
- **Basic chat**: Send a message or ask a question to an agent.

To add nodes, select the plus (**+**) icon in the workspace.

## Add agents

> Hosted agents aren't supported in the workflow designer. To coordinate tasks, call other agents, or orchestrate workflows within a Hosted agent, use Microsoft Agent Framework workflows or another agent framework that supports workflow capabilities from your Hosted agent code.

## Configure an output response format

Agents can return structured JSON output. Example schema:

```json
{
  "name": "math_response",
  "schema": {
    "type": "object",
    "properties": {
      "steps": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "explanation": {"type": "string"},
            "output": {"type": "string"}
          },
          "required": ["explanation", "output"],
          "additionalProperties": false
        }
      },
      "final_answer": {"type": "string"}
    },
    "additionalProperties": false,
    "required": ["steps", "final_answer"]
  },
  "strict": true
}
```

> Don't include secrets (passwords, keys, tokens) in JSON schemas, prompts, or saved workflow variables.

## Configure additional features

- **YAML visualizer view**: Set the **YAML Visualizer View** toggle to **On** to store the workflow as a YAML file. Edit in either the visualizer or the YAML view. Both stay in sync.
- **Versioning**: Each save creates a new, unchangeable version. View version history or delete older versions via the **Version** dropdown.
- **Notes**: Add notes to the workflow visualizer for extra context.

## Create expressions with Power Fx

Power Fx is a low-code language that uses Excel-like formulas. Use Power Fx to create complex logic that lets your agents manipulate data — set a variable value, parse a string, or evaluate a condition.

To use a variable in a Power Fx formula, add a prefix to its name to indicate scope:

- System variables: `System.`
- Local variables: `Local.`

### Example: capitalize a user response

1. Create a workflow and add an **Ask a question** node.
2. In the **Ask a question** box, enter "What is your name?". In **Save user response as**, enter `Var01`.
3. Add a **Send message** action. In **Message to send**, enter `{Upper(Local.Var01)}`. Select **Done**.

## Troubleshooting

| Issue | Solution |
| --- | --- |
| **Workflows** option not visible | Confirm you have the **Contributor** role or higher on your project. |
| Changes don't appear after editing | Select **Save** in the visualizer. Foundry doesn't save changes automatically. |
| Power Fx formula error: "Name isn't valid" | Add the correct scope prefix (`System.` or `Local.`). |
| Power Fx formula error: "Type mismatch" | Use conversion functions like `Text()` or `Value()`. |
| Workflow times out | Break complex workflows into smaller segments. |
