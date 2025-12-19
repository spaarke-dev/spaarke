# Dataverse Patterns Index

> **Domain**: Dataverse Development (Plugins, Web API, Entity Operations)
> **Last Updated**: 2025-12-19

---

## When to Load

Load these patterns when:
- Creating or modifying Dataverse plugins
- Implementing Dataverse Web API calls from BFF
- Working with entity CRUD operations
- Setting up lookup relationships or navigation properties
- Querying entity metadata

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    Dataverse Integration                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────┐      ┌──────────────────────────────────┐ │
│  │   PCF Control   │      │           BFF API                │ │
│  │                 │      │                                  │ │
│  │  WebAPI calls   │      │  DataverseServiceClientImpl      │ │
│  │  (OData)        │      │  DataverseWebApiClient           │ │
│  └────────┬────────┘      └──────────────┬───────────────────┘ │
│           │                              │                      │
│           ▼                              ▼                      │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                    Dataverse                                 ││
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ ││
│  │  │ Entities    │  │ Plugins     │  │ Custom APIs         │ ││
│  │  │ (sprk_*)    │  │ (thin only) │  │ (BFF proxy only)    │ ││
│  │  └─────────────┘  └─────────────┘  └─────────────────────┘ ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Available Patterns

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [plugin-structure.md](plugin-structure.md) | Plugin lifecycle, BaseProxyPlugin | ~130 |
| [web-api-client.md](web-api-client.md) | REST API client patterns | ~120 |
| [entity-operations.md](entity-operations.md) | Late-bound CRUD operations | ~115 |
| [relationship-navigation.md](relationship-navigation.md) | Lookups, @odata.bind, metadata | ~100 |

---

## Key Decisions (ADR-002)

| Constraint | Rule |
|------------|------|
| Plugin size | < 200 lines, < 50ms p95 |
| HTTP calls in plugins | **Never** (except Custom API Proxy → BFF) |
| Plugin types | Validation, projection, audit stamping only |
| Orchestration | BFF/BackgroundService workers only |
| Entity binding | Late-bound (no early-bound code generation) |

---

## Canonical Source Files

### Plugins
| File | Purpose |
|------|---------|
| `src/dataverse/plugins/.../BaseProxyPlugin.cs` | Abstract plugin base |
| `src/dataverse/plugins/.../GetFilePreviewUrlPlugin.cs` | Custom API proxy example |

### Server-Side Data Access
| File | Purpose |
|------|---------|
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs` | Pure REST client |
| `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | ServiceClient wrapper |
| `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` | Service interface |

### Tests
| File | Purpose |
|------|---------|
| `tests/unit/Spaarke.Plugins.Tests/ValidationPluginTests.cs` | Plugin unit tests |
| `tests/unit/Sprk.Bff.Api.Tests/Integration/DataverseWebApiWireMockTests.cs` | Web API mocks |

---

## Quick Reference

### Plugin Decision Matrix

| Plugin Type | Purpose | HTTP Allowed |
|-------------|---------|--------------|
| ValidationPlugin | Field validation | Never |
| ProjectionPlugin | Denormalization | Never |
| AuditPlugin | Timestamp/user stamping | Never |
| Custom API Proxy | BFF delegation | BFF only |

### Web API URL Format

```
{org}.crm.dynamics.com/api/data/v9.2/{entitySetName}({guid})
```

### Lookup Binding Patterns

| Context | Pattern |
|---------|---------|
| Web API (create/update) | `"field@odata.bind": "/entities(guid)"` |
| ServiceClient | `new EntityReference("entity", guid)` |
| OData filter | `_fieldname_value eq {guid}` |

---

**Lines**: ~100
