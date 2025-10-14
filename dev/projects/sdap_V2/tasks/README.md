# Task Guide Directory

**Purpose**: Discrete, focused implementation guides for each refactoring task
**Format**: One file per task, copy-paste ready code, clear validation steps

---

## File Naming Convention

```
phase-{phase#}-task-{task#}-{short-description}.md

Examples:
- phase-1-task-1-app-config.md
- phase-2-task-1-create-spefilestore.md
- phase-4-task-2-integrate-cache.md
```

---

## Task File Structure

Each task file follows this template:

```markdown
# Phase {X} - Task {Y}: {Title}

**Phase**: {Phase number and name}
**Duration**: {Estimated time}
**Risk**: {Low/Medium/High}
**Pattern**: [Link to pattern file]

---

## Goal

{Clear problem statement and desired outcome}

---

## Files to Edit

```bash
- [ ] {File path 1}
- [ ] {File path 2}
```

---

## Implementation

### Step 1: {Action}
{Detailed instructions with code examples}

### Step 2: {Action}
{More instructions}

---

## Validation

{Specific validation steps with expected results}

---

## Checklist

- [ ] {Action item 1}
- [ ] {Action item 2}
- [ ] Build succeeds
- [ ] Tests pass
- [ ] {Functional validation}

---

## Expected Results

**Before**:
- ❌ {Current problem}

**After**:
- ✅ {Desired state}

---

## Troubleshooting

### Issue: {Common problem}
**Cause**: {Why it happens}
**Fix**: {How to resolve}

---

## Commit Message

```bash
{Conventional commit format with ADR references}
```

---

## Next Task

➡️ [Next task link]

---

## Related Resources

- **Pattern**: [Link]
- **Anti-Pattern**: [Link if applicable]
- **Architecture**: [Link]
```

---

## Completed Task Files

### Phase 1: Configuration & Critical Fixes (3/3)
- ✅ [phase-1-task-1-app-config.md](phase-1-task-1-app-config.md) - Fix app registration configuration
- ✅ [phase-1-task-2-remove-uami.md](phase-1-task-2-remove-uami.md) - Remove UAMI logic
- ✅ [phase-1-task-3-serviceclient-lifetime.md](phase-1-task-3-serviceclient-lifetime.md) - Fix ServiceClient lifetime

### Phase 2: Simplify Service Layer (0/6)
- [ ] phase-2-task-1-create-spefilestore.md - Create SpeFileStore concrete class
- [ ] phase-2-task-2-update-endpoints.md - Update endpoints to use SpeFileStore
- [ ] phase-2-task-3-update-di.md - Update DI registrations
- [ ] phase-2-task-4-update-tests.md - Update test mocking strategy
- [ ] phase-2-task-5-simplify-authz.md - Simplify authorization layer
- [ ] phase-2-task-6-cleanup.md - Delete obsolete files

### Phase 3: Feature Module Pattern (0/2)
- [ ] phase-3-task-1-feature-modules.md - Create feature module extensions
- [ ] phase-3-task-2-refactor-program.md - Refactor Program.cs to use modules

### Phase 4: Token Caching (0/4)
- [ ] phase-4-task-1-create-cache.md - Create GraphTokenCache service
- [ ] phase-4-task-2-integrate-cache.md - Integrate cache with GraphClientFactory
- [ ] phase-4-task-3-register-cache.md - Register cache in DI
- [ ] phase-4-task-4-cache-metrics.md - Add cache metrics (optional)

**Total**: 15 tasks (3 completed, 12 remaining)

---

## Creating New Task Files

To create a new task file, follow this process:

### 1. Extract from REFACTORING-CHECKLIST.md

- Find the task in REFACTORING-CHECKLIST.md
- Extract code examples
- Extract validation steps
- Extract checklists

### 2. Structure the Task File

- Use the template above
- Include before/after code examples
- Add specific file paths
- Include validation commands
- Add troubleshooting section

### 3. Link Pattern Files

- Reference relevant pattern files for implementation
- Reference anti-pattern files for what to avoid
- Link to architecture documents for context

### 4. Add Navigation

- Link to next task at bottom
- Link to previous task (optional)
- Update this README with task status

### 5. Key Principles

**Keep it focused**:
- One task = one file
- ~200-400 lines per file
- Actionable steps only

**Make it copy-paste ready**:
- Complete code examples
- Exact commands to run
- Clear expected outputs

**Include validation**:
- Build/test commands
- Manual testing steps
- Performance checks
- Expected before/after results

**Provide troubleshooting**:
- Common issues
- Clear causes
- Specific fixes

---

## Task File Template

Copy this template to create new task files:

```markdown
# Phase {X} - Task {Y}: {Title}

**Phase**: {X} ({Phase Name})
**Duration**: {time estimate}
**Risk**: {Low/Medium/High}
**Pattern**: [pattern-name.md](../patterns/pattern-name.md)

---

## Goal

{Problem statement and desired outcome}

---

## Files to Edit

```bash
- [ ] path/to/file1.cs
- [ ] path/to/file2.cs
```

---

## Implementation

### Step 1: {Action}

{Instructions with code}

```csharp
// ❌ OLD (WRONG)
// ...

// ✅ NEW (CORRECT)
// ...
```

---

## Validation

```bash
# Build check
dotnet build

# Test check
dotnet test

# Manual test
{specific test commands}
```

---

## Checklist

- [ ] {Action 1}
- [ ] {Action 2}
- [ ] Build succeeds
- [ ] Tests pass

---

## Expected Results

**Before**:
- ❌ {Problem}

**After**:
- ✅ {Solution}

---

## Commit Message

```bash
git commit -m "{type}({scope}): {description}

{body}

{footer}"
```

---

## Next Task

➡️ [phase-{x}-task-{y+1}-{name}.md](phase-{x}-task-{y+1}-{name}.md)

---

## Related Resources

- **Pattern**: [pattern.md](../patterns/pattern.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md)
```

---

## AI Assistant Instructions

When creating task files, AI assistants should:

1. **Extract** relevant content from REFACTORING-CHECKLIST.md
2. **Simplify** to focus on one discrete task
3. **Add** copy-paste ready code examples
4. **Include** validation steps with expected outputs
5. **Provide** troubleshooting for common issues
6. **Link** to pattern files and anti-patterns
7. **Format** with clear headers and sections
8. **Test** that file paths and commands are correct

**Example prompt for AI**:
> "Create phase-2-task-1-create-spefilestore.md following the template in tasks/README.md. Extract content from REFACTORING-CHECKLIST.md Phase 2, Task 2.1. Include complete code examples for SpeFileStore.cs and both DTO files. Add validation steps and troubleshooting."

---

## Related Documentation

- **Master Checklist**: [../IMPLEMENTATION-CHECKLIST.md](../IMPLEMENTATION-CHECKLIST.md)
- **Detailed Reference**: [../REFACTORING-CHECKLIST.md](../REFACTORING-CHECKLIST.md)
- **Pattern Library**: [../patterns/README.md](../patterns/README.md)
- **Architecture**: [../TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md)

---

**Last Updated**: 2025-10-13
**Status**: Phase 1 complete (3/3), 12 tasks remaining
**Next**: Create Phase 2 task files
