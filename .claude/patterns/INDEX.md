# Code Patterns (AI Context)

> **Purpose**: Reusable code examples organized by domain for efficient AI reference
> **Target**: 100-200 lines per pattern file
> **Source**: Extracted from guides and existing code
> **Last Updated**: 2025-12-25

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
├── INDEX.md              # This file
├── api/                  # BFF API patterns
│   ├── INDEX.md
│   ├── background-workers.md
│   ├── endpoint-definition.md
│   ├── endpoint-filters.md
│   ├── error-handling.md
│   ├── resilience.md
│   └── service-registration.md
├── auth/                 # Authentication patterns
│   ├── INDEX.md
│   ├── msal-client.md
│   ├── oauth-scopes.md
│   ├── obo-flow.md
│   ├── service-principal.md
│   └── token-caching.md
├── caching/              # Caching patterns
│   ├── INDEX.md
│   ├── distributed-cache.md
│   ├── request-cache.md
│   └── token-cache.md
├── dataverse/            # Dataverse patterns
│   ├── INDEX.md
│   ├── entity-operations.md
│   ├── plugin-structure.md
│   ├── relationship-navigation.md
│   └── web-api-client.md
├── pcf/                  # PCF control patterns
│   ├── INDEX.md
│   ├── control-initialization.md
│   ├── dataverse-queries.md
│   ├── dialog-patterns.md
│   ├── error-handling.md
│   └── theme-management.md
├── ai/                   # AI patterns
│   ├── INDEX.md
│   ├── analysis-scopes.md
│   ├── streaming-endpoints.md
│   └── text-extraction.md
├── testing/              # Testing patterns
│   ├── INDEX.md
│   ├── integration-tests.md
│   ├── mocking-patterns.md
│   └── unit-test-structure.md
└── webresource/          # JavaScript web resource patterns
    └── custom-dialogs-in-dataverse.md
```

---

## Available Pattern Files

### API Patterns (`api/`)

| Pattern | File | Source ADRs |
|---------|------|-------------|
| Background Workers | [background-workers.md](api/background-workers.md) | ADR-001 |
| Endpoint Definition | [endpoint-definition.md](api/endpoint-definition.md) | ADR-001 |
| Endpoint Filters | [endpoint-filters.md](api/endpoint-filters.md) | ADR-008 |
| Error Handling | [error-handling.md](api/error-handling.md) | ADR-019 |
| Resilience | [resilience.md](api/resilience.md) | ADR-017 |
| Service Registration | [service-registration.md](api/service-registration.md) | ADR-010 |

### Auth Patterns (`auth/`)

| Pattern | File | Source ADRs |
|---------|------|-------------|
| MSAL Client | [msal-client.md](auth/msal-client.md) | ADR-004 |
| OAuth Scopes | [oauth-scopes.md](auth/oauth-scopes.md) | ADR-004, ADR-008 |
| OBO Flow | [obo-flow.md](auth/obo-flow.md) | ADR-004, ADR-009 |
| Service Principal | [service-principal.md](auth/service-principal.md) | ADR-004, ADR-016 |
| Token Caching | [token-caching.md](auth/token-caching.md) | ADR-009 |

### Caching Patterns (`caching/`)

| Pattern | File | Source ADRs |
|---------|------|-------------|
| Distributed Cache | [distributed-cache.md](caching/distributed-cache.md) | ADR-009 |
| Request Cache | [request-cache.md](caching/request-cache.md) | ADR-009 |
| Token Cache | [token-cache.md](caching/token-cache.md) | ADR-009 |

### Dataverse Patterns (`dataverse/`)

| Pattern | File | Source ADRs |
|---------|------|-------------|
| Entity Operations | [entity-operations.md](dataverse/entity-operations.md) | ADR-002, ADR-007 |
| Plugin Structure | [plugin-structure.md](dataverse/plugin-structure.md) | ADR-002 |
| Relationship Navigation | [relationship-navigation.md](dataverse/relationship-navigation.md) | ADR-007 |
| Web API Client | [web-api-client.md](dataverse/web-api-client.md) | ADR-007, ADR-010 |

### PCF Patterns (`pcf/`)

| Pattern | File | Source ADRs |
|---------|------|-------------|
| Control Initialization | [control-initialization.md](pcf/control-initialization.md) | ADR-006, ADR-012 |
| Dataverse Queries | [dataverse-queries.md](pcf/dataverse-queries.md) | ADR-006, ADR-009 |
| Dialog Patterns | [dialog-patterns.md](pcf/dialog-patterns.md) | ADR-006 |
| Error Handling | [error-handling.md](pcf/error-handling.md) | ADR-006, ADR-012 |
| Theme Management | [theme-management.md](pcf/theme-management.md) | ADR-012 |

### AI Patterns (`ai/`)

| Pattern | File | Source ADRs |
|---------|------|-------------|
| Analysis Scopes | [analysis-scopes.md](ai/analysis-scopes.md) | ADR-013 |
| Streaming Endpoints | [streaming-endpoints.md](ai/streaming-endpoints.md) | ADR-013 |
| Text Extraction | [text-extraction.md](ai/text-extraction.md) | ADR-013 |

### Testing Patterns (`testing/`)

| Pattern | File | Source ADRs |
|---------|------|-------------|
| Integration Tests | [integration-tests.md](testing/integration-tests.md) | ADR-022 |
| Mocking Patterns | [mocking-patterns.md](testing/mocking-patterns.md) | ADR-022 |
| Unit Test Structure | [unit-test-structure.md](testing/unit-test-structure.md) | ADR-022 |

### Web Resource Patterns (`webresource/`)

| Pattern | File | Source ADRs |
|---------|------|-------------|
| Custom Dialogs in Dataverse | [custom-dialogs-in-dataverse.md](webresource/custom-dialogs-in-dataverse.md) | ADR-006, ADR-023 |

---

## Usage by AI Agents

**Loading strategy**: Load specific pattern files when implementing related features.

| Task Type | Load These Patterns |
|-----------|---------------------|
| Creating BFF endpoint | `api/endpoint-definition.md` + `api/endpoint-filters.md` |
| Implementing auth | `auth/oauth-scopes.md` + `auth/obo-flow.md` |
| Creating PCF control | `pcf/control-initialization.md` + `pcf/theme-management.md` |
| Writing plugin | `dataverse/plugin-structure.md` |
| Adding caching | `caching/distributed-cache.md` |
| Writing tests | `testing/unit-test-structure.md` + `testing/mocking-patterns.md` |
| Background jobs | `api/background-workers.md` + `auth/service-principal.md` |
| Custom dialogs in web resources | `webresource/custom-dialogs-in-dataverse.md` |

---

## Related Constraint Files

Patterns show **how** - constraints define **what**:

| Pattern Domain | Constraint File |
|----------------|-----------------|
| api/ | `.claude/constraints/api.md` |
| auth/ | `.claude/constraints/auth.md` |
| pcf/ | `.claude/constraints/pcf.md` |
| dataverse/ | `.claude/constraints/plugins.md` |
| caching/ | `.claude/constraints/data.md` |
| ai/ | `.claude/constraints/ai.md` |
| testing/ | `.claude/constraints/testing.md` |

---

**Lines**: ~160
