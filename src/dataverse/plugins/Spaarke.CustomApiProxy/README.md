# Spaarke.CustomApiProxy

**Thin Custom API proxy plugins for SDAP**

Dataverse plugin solution providing server-side proxy for SharePoint Embedded operations. Eliminates client-side MSAL.js complexity by proxying through SDAP BFF API.

## Purpose

Provides thin Custom API plugins that:
- ✅ Validate inputs and generate correlation IDs
- ✅ Call SDAP BFF API with app-only authentication
- ✅ Return ephemeral URLs to client
- ✅ Comply with ADR-002 (no heavy orchestration in plugins)

## Documentation

📚 **[Technical Overview](docs/TECHNICAL-OVERVIEW.md)** - Complete plugin architecture and implementation details

## Plugins

| Custom API | Purpose | Status |
|------------|---------|--------|
| `sprk_GetFilePreviewUrl` | Get ephemeral SharePoint Embedded preview URL | ✅ Production |

## Architecture

```
Power Apps Client
      ↓ Calls Custom API
Dataverse Plugin (Thin Proxy)
      ↓ HTTP to BFF API
SDAP BFF API
      ↓ Validates UAC + Calls Graph
SharePoint Embedded
      ↓ Returns ephemeral URL
Client (displays preview)
```

**Key Principle**: Plugin is thin - only validates, calls BFF, returns result. All orchestration in BFF API.

## Status

✅ Production-Ready | ADR-002 Compliant | Last Updated: 2025-12-03
