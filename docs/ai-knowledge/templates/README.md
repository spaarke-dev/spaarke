# Project Document Templates

> **Last Updated**: December 3, 2025

This folder contains templates and AI agent playbooks for project documentation following the Spaarke development lifecycle.

**Format**: Templates use [POML (Prompt Orchestration Markup Language)](https://microsoft.github.io/poml/stable/) for structured AI agent instructions.

## 🤖 AI Agent Playbook

**Start here for AI-assisted development:**

| Document | Purpose |
|----------|---------|
| [AI-AGENT-PLAYBOOK.md](./AI-AGENT-PLAYBOOK.md) | Process design specs into project artifacts (README, plan, tasks) |
| [task-execution.template.md](./task-execution.template.md) | Execute individual tasks with context management and resource gathering (POML format) |
| [ai-knowledge-article.template.md](./ai-knowledge-article.template.md) | Create AI knowledge base articles (Markdown) |

The playbook guides AI agents through:
1. Ingesting and analyzing a design specification
2. Gathering context (ADRs, existing code, knowledge base)
3. Generating project documents (README, plan, tasks)
4. Validating before implementation
5. **Executing tasks** with context management (reset at >70%)

## Available Templates

| Template | Purpose | When to Use |
|----------|---------|-------------|
| [project-README.template.md](./project-README.template.md) | Project overview, status, graduation criteria | Start of every project |
| [project-plan.template.md](./project-plan.template.md) | Comprehensive project plan | After solution assessment |
| [task-execution.template.md](./task-execution.template.md) | Individual task execution protocol | When executing each task |
| [ai-knowledge-article.template.md](./ai-knowledge-article.template.md) | AI knowledge base articles | When documenting patterns/knowledge for AI consumption |

## Development Lifecycle Mapping

| Lifecycle Phase | Primary Document | Template |
|-----------------|------------------|----------|
| 1. Product Feature Request | Feature request | (Custom) |
| 2. Solution Assessment | Assessment doc | (Custom) |
| 3. Detailed Design Specification | Design spec | (Custom .docx/.md) |
| 4. Project Plan | `plan.md` | `AI-AGENT-PLAYBOOK.md` → `project-plan.template.md` |
| 5. Tasks | `tasks.md` | `AI-AGENT-PLAYBOOK.md` → (generated) |
| 5a. Task Execution | Per-task work | `task-execution.template.md` |
| 6. Product Feature Documentation | User guides | (Coming soon) |
| 7. Complete Project | `README.md` | `project-README.template.md` |

## How to Use

### With AI Agent (Recommended)

1. **Provide your design spec** to the AI agent:
   ```
   "Here is the design spec for {Feature Name}" 
   + attach .docx/.md file or paste content
   ```

2. **AI follows the playbook** - automatically:
   - Extracts key information
   - Finds relevant ADRs and existing code
   - Generates README, plan, and tasks
   - Presents summary for approval

3. **Review and approve** - AI presents summary before coding

### Manual Usage

1. **Copy the template** to your project folder:
   ```bash
   cp docs/templates/project-README.template.md docs/projects/{project-name}/README.md
   cp docs/templates/project-plan.template.md docs/projects/{project-name}/plan.md
   ```

2. **Replace placeholders** (text in `{curly braces}`) with actual content

3. **Remove unused sections** - Not all projects need all sections

4. **Update status** - Keep the status fields current throughout the project

## Template Conventions

- `{placeholder}` - Replace with actual value
- `{YYYY-MM-DD}` - Date format
- `⬜ Not Started` / `🔄 In Progress` / `✅ Complete` - Status indicators
- Sections marked "Optional" can be removed if not applicable

---

*Maintained by: Spaarke Engineering*
