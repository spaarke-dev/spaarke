# /new-project

Interactive wizard to start a new project from scratch.

## Usage

```
/new-project
```

## What This Command Does

This command provides an interactive wizard that:
1. Asks for project details
2. Creates the project folder structure
3. Guides you through creating or placing the spec
4. Runs the appropriate initialization skills

## Wizard Flow

### Step 1: Project Name
```
What is the project name? (kebab-case, e.g., "dark-mode-toggle")
>
```

### Step 2: Check for Spec
```
Do you have a design specification ready?
  [1] Yes - I'll provide the path or paste it
  [2] No - Help me create one
  [3] Skip - Create empty project structure only
>
```

### Step 3A: If spec exists
```
Where is the spec located?
  [1] I'll paste it here
  [2] It's at a file path: ___
  [3] It's already at projects/{name}/spec.md
>
```

### Step 3B: If no spec
```
Let's create a minimal spec. Answer these questions:

1. What problem does this solve?
2. What's the proposed solution (high-level)?
3. What are the key deliverables?
4. Any constraints or dependencies?
```

### Step 4: Initialize
```
Ready to initialize project at: projects/{name}/

This will:
  ✓ Create project folder structure
  ✓ Generate README.md from spec
  ✓ Generate plan.md with WBS
  ✓ Create CLAUDE.md context file
  ✓ Decompose into task files

Proceed? [Y/n]
```

### Step 5: Run Skills

Executes in order:
1. `/project-init projects/{name}`
2. `/task-create projects/{name}`

### Step 6: Summary
```
✅ Project created: projects/{name}/

Files created:
  - spec.md (design specification)
  - README.md (project overview)
  - plan.md (implementation plan)
  - CLAUDE.md (AI context)
  - tasks/TASK-INDEX.md
  - tasks/*.poml (N task files)

Next steps:
  1. Review README.md for accuracy
  2. Check tasks/TASK-INDEX.md for execution order
  3. Start with task 001

To begin implementation: "Start task 001"
```

## When to Use

- Starting a completely new feature
- Don't have a spec yet but want to brainstorm
- Want guided setup instead of manual commands

## Related Commands

- `/project-init` - Direct initialization (requires spec)
- `/design-to-project` - Full pipeline with validation phases
- `/task-create` - Just decompose existing plan
