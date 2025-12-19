# AIP-002: POML Format

> **Status**: Active  
> **Created**: December 4, 2025  
> **Applies To**: AI agents creating or executing task files

---

## Summary

POML (Prompt Orchestration Markup Language) is the standard format for task files in Spaarke projects. This protocol defines the structure, required elements, and conventions for `.poml` files.

---

## Format Rules

### Rule 1: File Format
- **Extension**: `.poml`
- **Format**: Valid XML document
- **Encoding**: UTF-8
- **Reference**: https://microsoft.github.io/poml/stable/

### Rule 2: Required Elements
Every task file MUST include:
- `<task>` root with `id` and `project` attributes
- `<metadata>` with title, phase, status
- `<prompt>` with task description
- `<goal>` with measurable outcome
- `<steps>` with ordered actions
- `<acceptance-criteria>` with testable criteria

### Rule 3: Numbering Convention
| Phase | Task Numbers | Example |
|-------|--------------|---------|
| Phase 1 | 001, 002, 003... | `001-setup.poml` |
| Phase 2 | 010, 011, 012... | `010-api-endpoint.poml` |
| Phase 3 | 020, 021, 022... | `020-ui-component.poml` |
| Phase 4 | 030, 031, 032... | `030-integration.poml` |

Gap of 10 allows inserting tasks without renumbering.

---

## Document Structure

```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="{NNN}" project="{project-name}">
  <metadata>
    <title>{Task Title}</title>
    <phase>{N}: {Phase Name}</phase>
    <status>not-started</status>
    <estimated-hours>{2-4}</estimated-hours>
    <dependencies>{task IDs or "none"}</dependencies>
    <blocks>{task IDs or "none"}</blocks>
  </metadata>

  <prompt>{Natural language task description}</prompt>
  <role>{Persona AI should adopt}</role>
  <goal>{Measurable outcome}</goal>
  
  <context>
    <background>{Why this task exists}</background>
    <relevant-files>
      <file>{path/to/file}</file>
    </relevant-files>
  </context>

  <constraints>
    <constraint source="{ADR-NNN}">{Rule}</constraint>
  </constraints>

  <knowledge>
    <files>
      <file>{docs/path/to/knowledge.md}</file>
    </files>
    <patterns>
      <pattern name="{Pattern Name}">{src/path/to/example.cs}</pattern>
    </patterns>
  </knowledge>

  <steps>
    <step order="1">{First action}</step>
    <step order="2">{Second action}</step>
  </steps>

  <tools>
    <tool name="{tool}">{Description}</tool>
  </tools>

  <outputs>
    <output type="code">{path/to/new/file.cs}</output>
    <output type="test">{path/to/test/file.cs}</output>
  </outputs>

  <acceptance-criteria>
    <criterion testable="true">{Given X, when Y, then Z}</criterion>
  </acceptance-criteria>

  <notes>{Implementation hints}</notes>
</task>
```

---

## Tag Reference

### `<task>`
Root element.

| Attribute | Required | Description |
|-----------|----------|-------------|
| `id` | Yes | Task number (e.g., "001") |
| `project` | Yes | Project name |

### `<metadata>`
Task tracking information.

| Child | Required | Values |
|-------|----------|--------|
| `<title>` | Yes | Human-readable name |
| `<phase>` | Yes | "{N}: {Name}" |
| `<status>` | Yes | `not-started` \| `in-progress` \| `complete` \| `blocked` |
| `<estimated-hours>` | Yes | Number (typically 2-4) |
| `<dependencies>` | Yes | Task IDs or "none" |
| `<blocks>` | Yes | Task IDs or "none" |

### `<prompt>`
High-level task description. AI uses this as intent anchor, not step-by-step instructions.

### `<role>`
Persona AI should adopt. Examples:
- "Senior .NET developer with Minimal API expertise"
- "PCF control developer with Fluent UI experience"
- "Spaarke AI Developer Agent"

### `<goal>`
Measurable outcome statement. Must be:
- Specific and unambiguous
- Testable/verifiable
- Describes end state

### `<context>`
Background information.

| Child | Purpose |
|-------|---------|
| `<background>` | Business context, why task exists |
| `<relevant-files>` | Related files to reference |
| `<dependencies>` | Task dependency status |

### `<constraints>`
Hard rules. Each `<constraint>` has `source` attribute (ADR reference or "project").

### `<knowledge>`
Resources to load before implementation.

| Child | Purpose |
|-------|---------|
| `<files>` | Knowledge articles, ADRs |
| `<patterns>` | Code patterns with locations |

### `<steps>`
Ordered actions. Each `<step>` has `order` attribute for sequence.

### `<tools>`
External tools AI may use (dotnet, npm, terminal, etc.).

### `<outputs>`
Artifacts to produce. `type` attribute: `code` \| `test` \| `docs`

### `<acceptance-criteria>`
Testable success criteria. Format: "Given X, when Y, then Z"

### `<notes>`
Implementation hints, gotchas, spec references.

---

## Examples

### Minimal Task
```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="001" project="my-project">
  <metadata>
    <title>Set Up Project Structure</title>
    <phase>1: Setup</phase>
    <status>not-started</status>
    <estimated-hours>2</estimated-hours>
    <dependencies>none</dependencies>
    <blocks>002, 003</blocks>
  </metadata>

  <prompt>Create the initial folder structure and configuration files.</prompt>
  <role>Spaarke AI Developer Agent</role>
  <goal>Project folder structure matches spec.md requirements.</goal>

  <steps>
    <step order="1">Read spec.md for required structure</step>
    <step order="2">Create folders and files</step>
    <step order="3">Verify structure is correct</step>
  </steps>

  <acceptance-criteria>
    <criterion testable="true">All folders from spec.md exist</criterion>
    <criterion testable="true">Configuration files are valid</criterion>
  </acceptance-criteria>
</task>
```

### Full Task with Constraints
```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="010" project="sdap-fileviewer">
  <metadata>
    <title>Create Desktop Open Links Endpoint</title>
    <phase>2: API Development</phase>
    <status>not-started</status>
    <estimated-hours>3</estimated-hours>
    <dependencies>001</dependencies>
    <blocks>020</blocks>
  </metadata>

  <prompt>
    Create an API endpoint that returns desktop application URLs 
    for opening Office documents in Word, Excel, or PowerPoint.
  </prompt>
  
  <role>Senior .NET developer with Minimal API expertise</role>
  
  <goal>
    GET /api/spe/files/{driveItemId}/open-links returns desktop URLs 
    for supported Office document types with proper authorization.
  </goal>

  <context>
    <background>
      Users need to edit documents in desktop Office apps with full 
      functionality like track changes.
    </background>
    <relevant-files>
      <file>src/api/Endpoints/SpeEndpoints.cs</file>
      <file>src/api/Services/SpeFileStore.cs</file>
    </relevant-files>
  </context>

  <constraints>
    <constraint source="ADR-001">Use Minimal API patterns</constraint>
    <constraint source="ADR-007">Use SpeFileStore facade</constraint>
    <constraint source="ADR-008">Use endpoint filters for auth</constraint>
  </constraints>

  <knowledge>
    <files>
      <file>docs/adr/ADR-008-authorization-endpoint-filters.md</file>
      <file>docs/ai-knowledge/architecture/sdap-bff-api-patterns.md</file>
    </files>
    <patterns>
      <pattern name="Minimal API Endpoint">src/api/Endpoints/SpeEndpoints.cs</pattern>
      <pattern name="Authorization Filter">src/api/Filters/DocumentAuthFilter.cs</pattern>
    </patterns>
  </knowledge>

  <steps>
    <step order="1">Read existing SpeEndpoints.cs pattern</step>
    <step order="2">Create GetOpenLinks method with route</step>
    <step order="3">Implement MIME type to protocol mapping</step>
    <step order="4">Add DocumentAuthFilter to endpoint</step>
    <step order="5">Write unit tests for all scenarios</step>
    <step order="6">Run tests and verify they pass</step>
  </steps>

  <tools>
    <tool name="dotnet">Build and test</tool>
    <tool name="terminal">Run commands</tool>
  </tools>

  <outputs>
    <output type="code">src/api/Endpoints/SpeEndpoints.cs</output>
    <output type="test">tests/unit/SpeEndpointsTests.cs</output>
  </outputs>

  <acceptance-criteria>
    <criterion testable="true">Given Word doc, when GET /open-links, then returns ms-word: URL</criterion>
    <criterion testable="true">Given Excel doc, when GET /open-links, then returns ms-excel: URL</criterion>
    <criterion testable="true">Given PDF, when GET /open-links, then returns null desktopUrl</criterion>
    <criterion testable="true">Given unauthorized user, when GET /open-links, then returns 401</criterion>
  </acceptance-criteria>

  <notes>
    Protocol URL format: ms-word:ofe|u|{encoded-sharepoint-url}
    See spec.md section 3.2 for MIME type mapping table.
  </notes>
</task>
```

---

## Rationale

### Why XML format?
- Structured and validatable
- Clear separation of concerns
- Easy for AI to parse and generate
- Compatible with existing tooling

### Why numbered tasks with gaps?
- Sequential execution order
- Easy insertion of new tasks
- Clear phase organization

### Why acceptance criteria format?
- Maps directly to tests
- Unambiguous pass/fail
- AI can verify completion

---

## Related Protocols

- [AIP-001: Task Execution](AIP-001-task-execution.md) - How to execute tasks
- [AIP-003: Human Escalation](AIP-003-human-escalation.md) - When to ask for help

---

*Part of [AI Protocols](INDEX.md) | Spaarke AI Knowledge Base*
