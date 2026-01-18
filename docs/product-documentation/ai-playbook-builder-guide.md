# AI Playbook Builder - User Guide

> **Version**: 1.0.0
>
> **Last Updated**: January 2026

## Overview

The AI Playbook Builder is an intelligent assistant that helps you create and modify analysis playbooks using natural language. Instead of manually configuring nodes and connections, you can describe what you want to build, and the AI will create it for you.

---

## Getting Started

### Opening the AI Assistant

1. Navigate to a playbook record in Dataverse
2. The playbook canvas will display
3. Click the **AI Assistant** button in the toolbar, or press **Cmd/Ctrl+K**
4. The AI chat panel will open on the side

### Your First Playbook

Try saying:

> "Create a lease analysis playbook that extracts key dates, financial terms, and identifies risks"

The AI will:
1. Understand your intent
2. Create appropriate analysis nodes
3. Connect them in a logical flow
4. Add relevant scopes (skills, knowledge, tools)

---

## Available Commands

### Creating Playbooks

| What to Say | What Happens |
|-------------|--------------|
| "Create a contract review playbook" | Creates a new playbook with contract analysis nodes |
| "Build a document summarization flow" | Creates nodes for document summarization |
| "Make a compliance checker" | Creates nodes for compliance verification |

### Adding Nodes

| What to Say | What Happens |
|-------------|--------------|
| "Add a clause extraction step" | Adds a clause extraction node |
| "Add risk detection" | Adds a risk analysis node |
| "Include a summary node" | Adds a summarization node |

### Connecting Nodes

| What to Say | What Happens |
|-------------|--------------|
| "Connect extraction to analysis" | Creates an edge between nodes |
| "Link the summary to the output" | Creates edge to output node |

### Removing Elements

| What to Say | What Happens |
|-------------|--------------|
| "Remove the risk node" | Deletes the specified node |
| "Delete the connection between A and B" | Removes the edge |

### Configuring Nodes

| What to Say | What Happens |
|-------------|--------------|
| "Make the summary shorter" | Adjusts summary configuration |
| "Add legal analysis skills" | Adds skill scopes to node |

---

## Test Execution

### Test Modes

The AI Playbook Builder supports three test modes:

#### 1. Mock Test
- **What it is**: Simulated execution with sample data
- **Best for**: Quick validation of playbook structure
- **No documents needed**: Uses generated sample data
- **How to use**: Say "Run a mock test" or click Test > Mock

#### 2. Quick Test
- **What it is**: Real AI processing with uploaded document
- **Best for**: Testing analysis quality
- **Temporary storage**: Document stored for 24 hours only
- **How to use**: Say "Quick test with a document" or click Test > Quick

#### 3. Production Test
- **What it is**: Full production workflow
- **Best for**: Final validation before deployment
- **Uses real storage**: Document saved to production storage
- **How to use**: Say "Run production test" or click Test > Production

### Running a Test

1. Click the **Test** button or say "Test this playbook"
2. Select the test mode
3. For Quick/Production: Upload a document
4. Watch the progress indicator
5. Review the results

---

## Scope Management

### What are Scopes?

Scopes define what capabilities your playbook has:

- **Actions**: What the playbook does (summarize, extract, analyze)
- **Skills**: Specialized analysis capabilities (legal, financial, risk)
- **Tools**: Processing utilities (date extractor, risk detector)
- **Knowledge**: Reference information (company policies, standards)

### System vs Custom Scopes

| Type | Prefix | Can Edit | Can Delete |
|------|--------|----------|------------|
| System | SYS- | No | No |
| Custom | CUST- | Yes | Yes |

### Creating Custom Scopes

1. Open the Scope Browser (View > Scopes)
2. Click **Create New**
3. Choose the scope type
4. Fill in the details
5. Save

### Save As (Copying Scopes)

To create a custom version of a system scope:

1. Select the system scope
2. Click **Save As**
3. Enter a new name
4. The copy will have the CUST- prefix

### Extending Scopes (Inheritance)

To create a scope that inherits from another:

1. Select the parent scope
2. Click **Extend**
3. Enter a name for the child scope
4. Override specific fields as needed
5. Non-overridden fields inherit from parent

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **Cmd/Ctrl+K** | Toggle AI Assistant |
| **Escape** | Close modal/dialog |
| **Enter** | Send message |
| **Shift+Enter** | New line in message |

---

## Troubleshooting

### AI Assistant Not Responding

1. Check your internet connection
2. Wait a few moments and try again
3. If rate limited, wait 30 seconds before retrying
4. Refresh the page if issues persist

### Canvas Not Updating

1. The AI may still be processing - watch the loading indicator
2. Try a more specific command
3. Check for error messages in the chat

### Test Failures

| Error | Solution |
|-------|----------|
| "Document too large" | Use a smaller document (<50MB) |
| "Unsupported format" | Use PDF, DOCX, or TXT files |
| "Rate limit exceeded" | Wait 30 seconds and try again |
| "Analysis failed" | Check document is readable text |

### Scope Errors

| Error | Solution |
|-------|----------|
| "Cannot modify system scope" | Use Save As to create a custom copy |
| "Name already exists" | Choose a different name |
| "Cannot delete: has children" | Delete child scopes first |

---

## Best Practices

### Writing Good Prompts

**Good:**
> "Create a lease analysis playbook that extracts rent amounts, identifies escalation clauses, and flags renewal deadlines"

**Not as good:**
> "Make a playbook"

### Organizing Playbooks

- Use descriptive names
- Group related analysis in single playbooks
- Use node labels to identify purpose
- Test with representative documents

### Managing Scopes

- Create custom scopes for organization-specific analysis
- Use inheritance to share common configurations
- Review scope suggestions from the AI

---

## Getting Help

If you need additional assistance:

1. Ask the AI: "Help me with..." or "How do I..."
2. Check the error message for specific guidance
3. Contact support with your correlation ID (shown in error messages)

---

*AI Playbook Builder is powered by Azure OpenAI and integrates with Dataverse for playbook management.*
