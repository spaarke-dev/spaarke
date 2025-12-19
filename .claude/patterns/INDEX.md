# Code Patterns (AI Context)

> **Purpose**: Reusable code examples organized by domain for efficient AI reference
> **Target**: 100-200 lines per pattern file
> **Source**: Extracted from guides and existing code

## About This Directory

This directory contains focused, copy-paste-ready code patterns organized by technical domain. Each file shows common implementation patterns with minimal explanation.

**Format**:
```markdown
## Pattern Name

**When to use**: [1 sentence]

**Example**:
```typescript
// Concise, complete, copy-paste ready code
```

**Anti-pattern**:
```typescript
// What NOT to do
```
```

## Directory Structure

```
patterns/
├── api/          # BFF API patterns (endpoints, filters, services)
├── pcf/          # PCF control patterns (React, hooks, Dataverse)
├── auth/         # Authentication patterns (OAuth, OBO, tokens)
├── dataverse/    # Dataverse patterns (plugins, Web API, metadata)
└── testing/      # Testing patterns (unit, integration, mocks)
```

## Planned Pattern Files

<!-- TODO: Phase 3 - Create these files -->

### api/
- [ ] **endpoint-definition.md** - Minimal API endpoint patterns
- [ ] **endpoint-filters.md** - Authorization filter patterns
- [ ] **service-registration.md** - DI registration patterns
- [ ] **error-handling.md** - ProblemDetails patterns
- [ ] **resilience.md** - Polly retry patterns

### pcf/
- [ ] **control-initialization.md** - PCF init and updateView patterns
- [ ] **react-hooks.md** - React hook patterns for PCF
- [ ] **dataverse-queries.md** - WebAPI query patterns
- [ ] **error-display.md** - User-friendly error UI patterns
- [ ] **metadata-caching.md** - Metadata retrieval and caching

### auth/
- [ ] **oauth-scopes.md** - Correct OAuth scope patterns
- [ ] **token-acquisition.md** - MSAL token patterns
- [ ] **obo-flow.md** - On-Behalf-Of implementation
- [ ] **service-principal.md** - Service-to-service auth

### dataverse/
- [ ] **plugin-structure.md** - Thin plugin patterns
- [ ] **early-binding.md** - Strongly-typed entity patterns
- [ ] **web-api-calls.md** - Dataverse Web API patterns
- [ ] **relationship-navigation.md** - N:1, 1:N navigation

### testing/
- [ ] **unit-test-structure.md** - xUnit test patterns
- [ ] **mocking.md** - NSubstitute mock patterns
- [ ] **integration-tests.md** - WebApplicationFactory patterns
- [ ] **test-data-builders.md** - Test data creation patterns

## Phase 3 TODO

- [ ] Extract patterns from existing guides
- [ ] Create focused pattern files (100-200 lines)
- [ ] Add correct and incorrect examples
- [ ] Cross-reference related constraints and ADRs
- [ ] Validate patterns against current codebase

## Usage by AI Agents

**Loading strategy**: Load specific pattern files when implementing related features.

**Examples**:
- Creating BFF endpoint → Load `patterns/api/endpoint-definition.md` + `patterns/api/endpoint-filters.md`
- Creating PCF control → Load `patterns/pcf/control-initialization.md` + `patterns/pcf/react-hooks.md`
- Implementing auth → Load `patterns/auth/oauth-scopes.md` + `patterns/auth/token-acquisition.md`
- Writing plugin → Load `patterns/dataverse/plugin-structure.md`

Patterns provide immediate, actionable code examples without verbose explanation.
