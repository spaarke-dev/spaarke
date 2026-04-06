# Plugin Structure Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Creating or modifying Dataverse plugins (validation, projection, audit, or Custom API proxy).

## Read These Files
1. `src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/BaseProxyPlugin.cs` — Abstract base with correlation ID, audit logging, retry
2. `src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/GetFilePreviewUrlPlugin.cs` — Custom API proxy exemplar
3. `tests/unit/Spaarke.Plugins.Tests/ValidationPluginTests.cs` — Test patterns for plugins

## Constraints
- **ADR-002**: Plugins must be thin — <200 LoC, <50ms p95
- MUST NOT make HTTP/Graph calls from standard plugins (only Custom API Proxy → BFF)
- Plugin types: validation, projection, audit stamping ONLY — no orchestration

## Key Rules
- Late-bound entities only (no early-bound code generation)
- Always wrap in try/catch → `InvalidPluginExecutionException`
- Custom API Proxy: extend `BaseProxyPlugin`, implement `ExecuteProxy()` — handles correlation, audit, retry
- Redact sensitive data before logging request/response payloads
