# Task Execution Template (POML Format)

> **Last Updated**: December 4, 2025
>
> **Purpose**: POML-formatted template for executing individual tasks with context management, progress review, and resource gathering.
>
> **Format**: Prompt Orchestration Markup Language (POML) - https://microsoft.github.io/poml/stable/
>
> **File Extension**: `.poml` (valid XML document)

---

## Task File Structure

Task files use the `.poml` extension and are valid XML documents. Use Markdown only inside text nodes.

When creating a task file, use this POML structure:

```xml
<task id="{PROJ-NNN}" project="{Project Name}">
  <metadata>
    <title>{Task Title}</title>
    <status>not-started | in-progress | complete | blocked</status>
    <estimated-effort>{X hours/days}</estimated-effort>
    <actual-effort>{X hours/days}</actual-effort>
    <assigned>{AI Agent / Developer Name}</assigned>
    <started>{YYYY-MM-DD}</started>
    <completed>{YYYY-MM-DD}</completed>
  </metadata>

  <prompt>
    {Natural-language description of the task's intent - what needs to be accomplished
    and why it matters to the project.}
  </prompt>

  <role>
    {The persona or agent mode the AI should adopt for this task.}
    
    Examples:
    - "Senior .NET developer familiar with Minimal APIs and SharePoint Embedded"
    - "PCF control developer with expertise in Fluent UI v9 and React"
    - "Full-stack developer with Dataverse plugin experience"
  </role>

  <goal>
    {The final expected outcome - a clear, measurable definition of done.}
    
    Example: "A working API endpoint at /api/documents/{id}/permissions that returns
    document permissions for the authenticated user, with unit tests achieving 80% coverage."
  </goal>

  <inputs>
    <file purpose="design-spec">{path to design specification}</file>
    <file purpose="project-plan">docs/projects/{project-name}/plan.md</file>
    <file purpose="task-list">docs/projects/{project-name}/tasks.md</file>
    <file purpose="module-guidance">{path to relevant CLAUDE.md}</file>
    <!-- Add other required input files -->
  </inputs>

  <constraints>
    <!-- Hard rules restricting the AI - pulled from ADRs and project requirements -->
    <constraint source="ADR-001">No Azure Functions - use Minimal API patterns</constraint>
    <constraint source="ADR-002">Plugin execution must be &lt; 50ms p95</constraint>
    <constraint source="ADR-008">Use endpoint filters for authorization, not middleware</constraint>
    <constraint source="project">Must not break existing tests</constraint>
    <constraint source="project">Must maintain backward compatibility with existing API</constraint>
  </constraints>

  <knowledge>
    <!-- Reference technical approach and best practices -->
    <topic>{topic area - e.g., graph/mail, pcf/fluent-ui, dataverse/plugins}</topic>
    <files>
      <file>docs/adr/{relevant-adr}.md</file>
      <file>docs/KM-{topic}.md</file>
      <file>{path to relevant knowledge article}</file>
    </files>
    <patterns>
      <pattern name="{pattern name}" location="{file path}">
        {Brief description of the pattern and how to apply it}
      </pattern>
    </patterns>
  </knowledge>

  <context>
    <!-- Additional qualitative background -->
    <background>{Why this task exists, business context}</background>
    <dependencies>
      <dependency task="{PROJ-NNN}" status="complete|in-progress">
        {Description of dependency}
      </dependency>
    </dependencies>
    <related-work>
      <item>{Reference to similar past work or related features}</item>
    </related-work>
  </context>

  <steps>
    <!-- Deterministic sequence of actions -->
    <step order="0" name="Context Check">
      Check context usage. If &gt; 70%, create handoff summary and request new chat.
    </step>
    <step order="1" name="Review Progress">
      Read project README and tasks.md. Verify dependencies complete. Check for previous work.
    </step>
    <step order="2" name="Gather Resources">
      Read all files in &lt;inputs&gt; and &lt;knowledge&gt;. Extract applicable constraints.
    </step>
    <step order="3" name="Plan Implementation">
      Break into subtasks. Identify code patterns to follow. List files to create/modify.
    </step>
    <step order="4" name="Implement">
      Execute subtasks in order. Run context check after each. Write tests alongside code.
    </step>
    <step order="5" name="Verify">
      Run tests. Verify build. Check acceptance criteria. Validate ADR compliance.
    </step>
    <step order="6" name="Document">
      Update task status. Add code comments. Create completion report.
    </step>
  </steps>

  <tools>
    <!-- External tools or services the AI can use -->
    <tool name="dotnet">Build and test .NET projects</tool>
    <tool name="npm">Build and test TypeScript/PCF projects</tool>
    <tool name="git">Version control operations</tool>
    <tool name="pac">Power Platform CLI for solution operations</tool>
  </tools>

  <outputs>
    <!-- Artifacts to be produced or modified -->
    <output type="code">{path to files to create/modify}</output>
    <output type="test">{path to test files}</output>
    <output type="docs">docs/projects/{project-name}/tasks.md (status update)</output>
    <output type="report">Task completion report</output>
  </outputs>

  <examples>
    <!-- Reference samples or patterns -->
    <example name="{example name}" location="{file path}">
      {Description of what this example demonstrates and how to apply it}
    </example>
  </examples>

  <acceptance-criteria>
    <criterion testable="true">{Specific, testable criterion 1}</criterion>
    <criterion testable="true">{Specific, testable criterion 2}</criterion>
    <criterion testable="true">{Specific, testable criterion 3}</criterion>
  </acceptance-criteria>
</task>
```

---

## AI Agent Execution Protocol

### 🧠 Step 0: Context Management Check

**CRITICAL: Perform this check before starting AND after each major subtask.**

```xml
<context-check>
  <threshold warning="50" critical="70" emergency="85" />
  <action level="normal">Proceed with task execution</action>
  <action level="warning">Monitor closely, consider completing current subtask</action>
  <action level="critical">STOP - Create handoff summary and request new chat</action>
  <action level="emergency">Immediately create handoff, context may be truncated</action>
</context-check>
```

#### Context Reset Protocol

When context exceeds 70%, create a **handoff summary**:

```xml
<handoff>
  <task-reference>
    <id>{Task ID}</id>
    <title>{Task Title}</title>
    <project>{Project Name}</project>
  </task-reference>
  
  <progress>
    <completed>
      <subtask>{Subtask 1 completed}</subtask>
      <subtask>{Subtask 2 completed}</subtask>
    </completed>
    <remaining>
      <subtask>{Subtask 3 remaining}</subtask>
      <subtask>{Subtask 4 remaining}</subtask>
    </remaining>
    <current-state>{Description of where work stopped}</current-state>
  </progress>
  
  <files-modified>
    <file path="{path}" change="created|modified" status="complete|partial">
      {Brief description of changes}
    </file>
  </files-modified>
  
  <decisions>
    <decision>{Decision 1 and rationale}</decision>
    <decision>{Decision 2 and rationale}</decision>
  </decisions>
  
  <blockers>
    <blocker status="resolved|open">{Issue and resolution/status}</blocker>
  </blockers>
  
  <next-steps order="sequential">
    <step>{Next immediate action}</step>
    <step>{Following action}</step>
  </next-steps>
  
  <resources-needed>
    <adrs>{List of ADRs to re-read}</adrs>
    <files>{List of files to read}</files>
    <knowledge>{List of knowledge articles}</knowledge>
  </resources-needed>
</handoff>
```

**Then instruct user**: "Context is at {X}%. Please start a new chat and provide this handoff summary to continue."

---

### 📋 Step 1: Review Task in Context of Progress

Before writing any code, review using the task's `<context>` section:

```xml
<review-checklist>
  <item name="project-status">
    <action>Read: docs/projects/{project-name}/README.md</action>
    <check>Current project status, blockers, decisions made</check>
  </item>
  
  <item name="dependencies">
    <action>Read: docs/projects/{project-name}/tasks.md</action>
    <check>All dependency tasks are ✅ Complete</check>
    <if-blocked>Report blocker and STOP</if-blocked>
  </item>
  
  <item name="previous-work">
    <action>Search for partial implementations</action>
    <action>Check git status for uncommitted changes</action>
    <action>Review TODO comments related to this task</action>
  </item>
  
  <item name="task-validity">
    <check>Has the design changed since task was created?</check>
    <check>Is the task still aligned with project goals?</check>
    <if-changed>Flag for review before proceeding</if-changed>
  </item>
</review-checklist>
```

**Output**:
```markdown
## Pre-Execution Review

**Project Status**: {On Track / At Risk / Blocked}
**Dependencies Met**: {Yes / No - list missing}
**Previous Work Found**: {None / Partial - describe}
**Task Still Valid**: {Yes / No - explain}

**Proceed?**: {Yes / No - explain if No}
```

---

### 📚 Step 2: Gather Required Resources

Read all items specified in the task's `<inputs>`, `<knowledge>`, and `<context>` sections.

```xml
<resource-gathering>
  <from-task-definition>
    <read ref="inputs/*">All files listed in inputs section</read>
    <read ref="knowledge/files/*">All knowledge articles referenced</read>
    <read ref="knowledge/patterns/*">All patterns to follow</read>
    <read ref="context/dependencies">Dependency task outputs</read>
  </from-task-definition>
  
  <standard-resources>
    <resource type="adr-index">docs/adr/README-ADRs.md</resource>
    <resource type="root-claude">CLAUDE.md</resource>
    <resource type="module-claude" conditional="api-work">src/server/api/Spe.Bff.Api/CLAUDE.md</resource>
    <resource type="module-claude" conditional="pcf-work">src/client/pcf/CLAUDE.md</resource>
    <resource type="module-claude" conditional="shared-dotnet">src/server/shared/CLAUDE.md</resource>
    <resource type="module-claude" conditional="shared-ui">src/client/shared/CLAUDE.md</resource>
    <resource type="module-claude" conditional="test-work">tests/CLAUDE.md</resource>
  </standard-resources>
  
  <pattern-search>
    <search-for>Similar implementations in codebase</search-for>
    <search-for>Shared utilities to reuse</search-for>
    <search-for>Service patterns to follow</search-for>
  </pattern-search>
</resource-gathering>
```

**Output**:
```markdown
## Resources Gathered

### From Task Definition
| Source | File | Key Information |
|--------|------|-----------------|
| inputs | {path} | {what was extracted} |
| knowledge | {path} | {what was extracted} |

### ADRs Reviewed
| ADR | Key Constraint for This Task |
|-----|------------------------------|
| {ADR-NNN} | {constraint} |

### Knowledge Articles
| Article | Relevant Section |
|---------|------------------|
| {KM-XXX} | {section} |

### Code Patterns Found
| Pattern | Location | How to Apply |
|---------|----------|--------------|
| {pattern} | {file path} | {usage} |

### Reusable Components
| Component | Location | Purpose |
|-----------|----------|---------|
| {name} | {path} | {how it helps} |
```

---

### 🔨 Step 3: Execute Task

Follow the `<steps>` defined in the task, breaking each into subtasks:

```xml
<subtasks>
  <subtask id="1" name="{First subtask}" status="not-started">
    {Smallest unit of work}
  </subtask>
  <subtask id="2" name="{Second subtask}" status="not-started">
    {Description}
  </subtask>
  <subtask id="3" name="Write/update tests" status="not-started">
    Unit tests for new functionality
  </subtask>
  <subtask id="4" name="Verify tests pass" status="not-started">
    Run test suite, fix failures
  </subtask>
  <subtask id="5" name="Update task status" status="not-started">
    Mark complete in tasks.md
  </subtask>
</subtasks>
```

#### For Each Subtask

```xml
<subtask-execution>
  <step>CONTEXT CHECK - Is context still &lt; 70%?</step>
  <step>Read relevant code sections</step>
  <step>Plan the change (think before coding)</step>
  <step>Implement the change</step>
  <step>Verify change (run tests if applicable)</step>
  <step>Mark subtask complete</step>
  <step>CONTEXT CHECK again</step>
</subtask-execution>
```

#### Coding Standards Checklist

Validate against task's `<constraints>`:

```xml
<coding-checklist>
  <item>Following patterns from &lt;knowledge&gt; section</item>
  <item>Complying with all &lt;constraints&gt;</item>
  <item>Reusing existing utilities where possible</item>
  <item>Adding appropriate error handling</item>
  <item>Including necessary logging</item>
  <item>Writing tests for new functionality</item>
</coding-checklist>
```

---

### ✅ Step 4: Verify Completion

Validate against task's `<acceptance-criteria>` and `<outputs>`:

```xml
<verification>
  <acceptance-criteria-check>
    <!-- Verify each criterion from task definition -->
    <criterion ref="1" status="pass|fail" evidence="{how verified}" />
    <criterion ref="2" status="pass|fail" evidence="{how verified}" />
  </acceptance-criteria-check>
  
  <outputs-check>
    <!-- Verify each output from task definition was produced -->
    <output ref="code" status="created|modified" path="{actual path}" />
    <output ref="test" status="created|modified" path="{actual path}" />
  </outputs-check>
  
  <test-verification>
    <command type="dotnet">dotnet test {test-project-path}</command>
    <command type="npm">npm test</command>
  </test-verification>
  
  <build-verification>
    <command type="dotnet">dotnet build</command>
    <command type="npm">npm run build</command>
  </build-verification>
  
  <quality-check>
    <item>No new compiler warnings</item>
    <item>No hardcoded secrets or URLs</item>
    <item>Error handling in place</item>
    <item>Logging appropriate (not excessive)</item>
  </quality-check>
</verification>
```

---

### 📝 Step 5: Update Documentation

```xml
<documentation-updates>
  <update target="docs/projects/{project-name}/tasks.md">
    Change task status from 🔄 to ✅
    Add completion date and any notes
  </update>
  
  <update target="docs/projects/{project-name}/README.md" conditional="if-needed">
    Update progress, any new risks/decisions
  </update>
  
  <code-documentation>
    <item>New public methods have XML doc comments (C#)</item>
    <item>New functions have JSDoc comments (TypeScript)</item>
    <item>Complex logic has inline comments explaining "why"</item>
  </code-documentation>
</documentation-updates>
```

---

### 📊 Step 6: Task Completion Report

```xml
<completion-report>
  <summary>
    <task-id>{Task ID}</task-id>
    <task-title>{Task Title}</task-title>
    <status>complete</status>
    <actual-effort>{X hours/days}</actual-effort>
  </summary>
  
  <work-summary>{1-2 sentence summary of what was accomplished}</work-summary>
  
  <files-changed>
    <file path="{path}" change="created|modified|deleted">
      {Brief description}
    </file>
  </files-changed>
  
  <tests>
    <added>{count}</added>
    <modified>{count}</modified>
    <all-passing>true|false</all-passing>
  </tests>
  
  <constraints-verified>
    <constraint source="{ADR-NNN}">Verified: {how}</constraint>
  </constraints-verified>
  
  <notes-for-future>
    <item type="tech-debt">{Any technical debt introduced}</item>
    <item type="improvement">{Suggestions for improvement}</item>
    <item type="related">{Related tasks to consider}</item>
  </notes-for-future>
  
  <context-status>
    <usage>{X}%</usage>
    <recommendation>continue|reset</recommendation>
  </context-status>
</completion-report>
```

---

## Quick Reference: Context Thresholds

| Context % | Level | Action |
|-----------|-------|--------|
| < 50% | Normal | ✅ Proceed normally |
| 50-70% | Warning | ⚠️ Monitor, consider wrapping up current subtask |
| > 70% | Critical | 🛑 STOP - Create handoff summary and reset |
| > 85% | Emergency | 🚨 CRITICAL - Immediately create handoff, may lose context |

## Quick Reference: Ephemeral Files (Notes Directory)

**Location**: `docs/projects/{project-name}/notes/`

Use this directory for **temporary working files** that support task execution but are NOT permanent project artifacts:

### Recommended Structure
```
projects/{project-name}/notes/
├── scratch.md              # General brainstorming, quick notes
├── debug/                  # Debugging session artifacts
│   ├── 2025-12-04-auth-issue.md
│   └── api-responses.json
├── spikes/                 # Exploratory code/research
│   ├── redis-spike.cs
│   └── spike-results.md
├── drafts/                 # Work-in-progress before finalization
│   └── draft-api-design.md
└── handoffs/               # Context handoff summaries
    └── handoff-001.md
```

### What Goes in Notes (Ephemeral)

| Artifact Type | Location | Example |
|---------------|----------|---------|
| Brainstorming | `notes/scratch.md` | Ideas, rough outlines |
| Debug sessions | `notes/debug/` | Troubleshooting logs, captured responses |
| Spike/POC code | `notes/spikes/` | Throwaway exploration code |
| Draft content | `notes/drafts/` | WIP before moving to final location |
| Handoff summaries | `notes/handoffs/` | Context reset handoffs |
| Test data | `notes/test-data/` | Sample payloads, mock data |
| Meeting notes | `notes/meetings/` | Discussion records |

### What Does NOT Go in Notes

| Artifact Type | Correct Location |
|---------------|------------------|
| Final code | `src/` (appropriate module) |
| Tests | `tests/` |
| Design spec | `docs/projects/{project-name}/spec.md` |
| Project docs | `docs/projects/{project-name}/` (root) |
| Permanent diagrams | `docs/projects/{project-name}/` or committed to repo |

### Rules
- ✅ Create freely for working notes, debugging, exploration
- ✅ Reference in handoff summaries for continuity
- ✅ Use subdirectories to organize by purpose
- ❌ Do NOT store final artifacts here (move to proper location when done)
- ❌ Do NOT reference in permanent documentation
- 🗑️ Contents may be deleted when project completes

### Example Usage in Task Outputs
```xml
<outputs>
  <!-- Ephemeral working files -->
  <output type=\"notes\" ephemeral=\"true\">projects/{project-name}/notes/debug/auth-trace.md</output>
  <output type=\"notes\" ephemeral=\"true\">projects/{project-name}/notes/spikes/caching-poc.cs</output>
  
  <!-- Permanent artifacts (NOT in notes/) -->
  <output type=\"code\">src/server/api/Services/CacheService.cs</output>
  <output type=\"test\">tests/unit/CacheService.Tests.cs</output>
  <output type=\"docs\">projects/{project-name}/tasks/001-setup.md</output>
</outputs>
```

### Handoff Summary Location
When context reaches 70%, save handoff to `notes/handoffs/`:
```
notes/handoffs/handoff-{NNN}-{date}.md
```
This allows the next session to find and continue from the handoff.

## Quick Reference: Resource Locations

| Resource | Location |
|----------|----------|
| **Project files** | `projects/{project-name}/` |
| Project spec | `projects/{project-name}/spec.md` |
| Project tasks | `projects/{project-name}/tasks/` |
| **Ephemeral notes** | `projects/{project-name}/notes/` |
| ADRs | `docs/reference/adr/` |
| Knowledge base | `docs/ai-knowledge/` |
| Templates | `docs/ai-knowledge/templates/` |
| API CLAUDE.md | `src/server/api/Spe.Bff.Api/CLAUDE.md` |
| PCF CLAUDE.md | `src/client/pcf/CLAUDE.md` |
| Root CLAUDE.md | `CLAUDE.md` |

## Quick Reference: POML Tags

| Tag | Purpose |
|-----|---------|
| `<prompt>` | Natural-language description of task intent |
| `<role>` | Persona or agent mode to adopt |
| `<goal>` | Final expected outcome |
| `<inputs>` | Required files and artifacts |
| `<constraints>` | Hard rules restricting the AI |
| `<steps>` | Deterministic sequence of actions |
| `<tools>` | External tools or services available |
| `<outputs>` | Artifacts to be produced or modified |
| `<context>` | Additional qualitative background |
| `<examples>` | Reference samples or patterns |
| `<knowledge>` | Technical approach and best practices references |
| `<acceptance-criteria>` | Testable success criteria |

---

*Template version: 2.0 | POML Format | For use with AI Agent Playbook*
