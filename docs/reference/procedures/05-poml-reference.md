# POML Reference

> **Audience**: AI Agents, Software Engineers  
> **Part of**: [Spaarke Software Development Procedures](INDEX.md)

---

## Overview

**POML** (Prompt Orchestration Markup Language) provides structured prompt components for AI agent execution.

- **File extension**: `.poml`
- **Format**: Valid XML document
- **Reference**: https://microsoft.github.io/poml/stable/

---

## Document Structure

```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="001" project="{project-name}">
  <metadata>...</metadata>
  <prompt>...</prompt>
  <role>...</role>
  <goal>...</goal>
  <context>...</context>
  <constraints>...</constraints>
  <knowledge>...</knowledge>
  <steps>...</steps>
  <tools>...</tools>
  <outputs>...</outputs>
  <acceptance-criteria>...</acceptance-criteria>
  <notes>...</notes>
</task>
```

---

## Tag Definitions

### `<task>`
Root element containing all task information.

| Attribute | Description |
|-----------|-------------|
| `id` | Task number (e.g., "001") |
| `project` | Project name |

---

### `<metadata>`
Task metadata for tracking and dependencies.

| Child Element | Description |
|---------------|-------------|
| `<title>` | Human-readable task name |
| `<phase>` | Phase number and name |
| `<status>` | `not-started` \| `in-progress` \| `complete` \| `blocked` |
| `<estimated-hours>` | Effort estimate |
| `<dependencies>` | Prerequisite task IDs (or "none") |
| `<blocks>` | Tasks blocked by this one (or "none") |

---

### `<prompt>`
Natural-language description of the task's intent.

**Purpose**: High-level guidance, context, why the task exists.

**AI treatment**: Intent anchor, NOT step-by-step instructions.

---

### `<role>`
Persona and behavior the AI should adopt.

**Examples**:
- Spaarke AI Developer Agent
- Senior .NET developer with Minimal API expertise
- PCF control developer with Fluent UI experience

---

### `<goal>`
Precise, outcome-oriented statement. The success contract.

**Characteristics**:
- Must be measurable
- Cannot be ambiguous
- Describes a final state

---

### `<context>`
Additional background information.

| Child Element | Description |
|---------------|-------------|
| `<background>` | Why this task exists, business context |
| `<relevant-files>` | Files related to this task |
| `<dependencies>` | Task dependencies with status |

---

### `<constraints>`
Hard rules that must be adhered to during execution.

| Attribute | Description |
|-----------|-------------|
| `source` | ADR reference or "project" |

**Example**:
```xml
<constraints>
  <constraint source="ADR-001">Use Minimal API patterns</constraint>
  <constraint source="ADR-010">Use static methods for pure functions</constraint>
  <constraint source="project">Must not break existing tests</constraint>
</constraints>
```

---

### `<knowledge>`
Reference technical approaches and best practices.

| Child Element | Description |
|---------------|-------------|
| `<files>` | Knowledge articles and ADRs to read |
| `<patterns>` | Code patterns to follow with locations |

**Example**:
```xml
<knowledge>
  <files>
    <file>docs/adr/ADR-008-authorization-endpoint-filters.md</file>
  </files>
  <patterns>
    <pattern name="Endpoint Filter">src/api/Filters/AuthorizationFilter.cs</pattern>
  </patterns>
</knowledge>
```

---

### `<steps>`
Deterministic sequence of actions.

| Child Element | Attributes | Description |
|---------------|------------|-------------|
| `<step>` | `order="N"` | Individual action with sequence number |

**Characteristics**:
- Sequential execution
- No ambiguity
- Describes *what* to do, not *how to think*

**Example**:
```xml
<steps>
  <step order="1">Read the existing endpoint pattern in {file}</step>
  <step order="2">Create new endpoint following the pattern</step>
  <step order="3">Add unit tests</step>
  <step order="4">Run tests and verify they pass</step>
</steps>
```

---

### `<tools>`
External tools the AI may use.

**Example**:
```xml
<tools>
  <tool name="dotnet">Build and test .NET projects</tool>
  <tool name="npm">Build TypeScript/PCF projects</tool>
  <tool name="terminal">Run shell commands</tool>
</tools>
```

---

### `<outputs>`
Artifacts to be produced or modified.

| Attribute | Values |
|-----------|--------|
| `type` | `code` \| `test` \| `docs` |

**Example**:
```xml
<outputs>
  <output type="code">src/api/Endpoints/DocumentEndpoints.cs</output>
  <output type="test">tests/unit/DocumentEndpointsTests.cs</output>
</outputs>
```

---

### `<acceptance-criteria>`
Testable success criteria.

| Child Element | Attributes | Description |
|---------------|------------|-------------|
| `<criterion>` | `testable="true"` | Individual criterion |

**Format**: Given {precondition}, when {action}, then {expected result}.

**Example**:
```xml
<acceptance-criteria>
  <criterion testable="true">Given a valid document ID, when GET /files/{id}/links is called, then response includes desktopUrl</criterion>
  <criterion testable="true">Given an unauthorized user, when GET /files/{id}/links is called, then response is 401</criterion>
</acceptance-criteria>
```

---

### `<examples>`
Reference samples or patterns to follow.

| Child Element | Attributes | Description |
|---------------|------------|-------------|
| `<example>` | `name`, `location` | Example with description |

---

### `<notes>`
Implementation hints, gotchas, references to spec sections.

---

## Task Numbering Convention

| Phase | Task Numbers | Example |
|-------|--------------|---------|
| Phase 1 | 001, 002, 003... | 001-setup-environment.poml |
| Phase 2 | 010, 011, 012... | 010-create-api-endpoint.poml |
| Phase 3 | 020, 021, 022... | 020-build-ui-component.poml |
| Phase 4 | 030, 031, 032... | 030-integration-tests.poml |

**Gap rationale**: 10-number gaps allow inserting tasks later without renumbering.

---

## Complete Example

```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="010" project="sdap-fileviewer-enhancements-1">
  <metadata>
    <title>Create Desktop Open Links API Endpoint</title>
    <phase>2: API Development</phase>
    <status>not-started</status>
    <estimated-hours>3</estimated-hours>
    <dependencies>001</dependencies>
    <blocks>020</blocks>
  </metadata>

  <prompt>
    Create a new API endpoint that returns desktop application URLs 
    for opening documents in Word, Excel, or PowerPoint.
  </prompt>
  
  <role>Senior .NET developer with Minimal API expertise</role>
  
  <goal>
    GET /api/spe/files/{driveItemId}/open-links endpoint returns 
    desktop URLs for supported Office document types.
  </goal>

  <context>
    <background>Users need to open documents in desktop Office apps</background>
    <relevant-files>
      <file>src/api/Endpoints/SpeEndpoints.cs</file>
    </relevant-files>
  </context>

  <constraints>
    <constraint source="ADR-001">Use Minimal API patterns</constraint>
    <constraint source="ADR-008">Use endpoint filters for auth</constraint>
  </constraints>

  <knowledge>
    <files>
      <file>docs/adr/ADR-008-authorization-endpoint-filters.md</file>
    </files>
    <patterns>
      <pattern name="Minimal API">src/api/Endpoints/SpeEndpoints.cs</pattern>
    </patterns>
  </knowledge>

  <steps>
    <step order="1">Read existing SpeEndpoints.cs pattern</step>
    <step order="2">Create GetOpenLinks endpoint</step>
    <step order="3">Implement MIME type to protocol mapping</step>
    <step order="4">Add authorization filter</step>
    <step order="5">Write unit tests</step>
    <step order="6">Run tests and verify</step>
  </steps>

  <outputs>
    <output type="code">src/api/Endpoints/SpeEndpoints.cs</output>
    <output type="test">tests/unit/SpeEndpointsTests.cs</output>
  </outputs>

  <acceptance-criteria>
    <criterion testable="true">Given a Word doc, when GET /open-links, then returns ms-word: URL</criterion>
    <criterion testable="true">Given a PDF, when GET /open-links, then returns null desktopUrl</criterion>
  </acceptance-criteria>
</task>
```

---

*Part of [Spaarke Software Development Procedures](INDEX.md) | v2.0 | December 2025*
