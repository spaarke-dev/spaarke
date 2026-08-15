# Plugin Structure Pattern

> **Last Reviewed**: 2026-08-14
> **Reviewed By**: code-quality-and-assurance-r3 task 034 (doc-drift)
> **Status**: Verified
>
> **⚠️ 2026-08-14 (task 015 assessment): the `BaseProxyPlugin` / `Spaarke.CustomApiProxy` "Custom API proxy → BFF" pattern is RETIRED.** It is `[Obsolete]`-marked and is an ADR-002 violation (HTTP + AAD token acquisition inside the plugin pipeline; a plain-text OAuth secret at rest on a Dataverse column). r3 task 015's recommendation is **decommission** — do NOT extend `BaseProxyPlugin` or create new Custom-API-proxy plugins. New plugins are **thin validation / projection / audit only** (no HTTP/Graph). Client→BFF calls belong in a Code Page / PCF / web resource via `@spaarke/auth` (ADR-028), not a plugin.

## When
Creating or modifying Dataverse plugins (validation, projection, or audit stamping — NOT Custom API proxy; see the retirement note above).

## Read These Files
1. `tests/unit/Spaarke.Plugins.Tests/ValidationPluginTests.cs` — test patterns for thin plugins
2. *(retired — reference only)* `src/dataverse/plugins/Spaarke.CustomApiProxy/**` — the `[Obsolete]` proxy plugin; read only to understand what NOT to build (r3 task 015 recommends decommission).

## Constraints
- **ADR-002**: Plugins must be thin — <200 LoC, <50ms p95
- MUST NOT make HTTP/Graph calls from standard plugins (only Custom API Proxy → BFF)
- Plugin types: validation, projection, audit stamping ONLY — no orchestration

## Key Rules
- Late-bound entities only (no early-bound code generation)
- Always wrap in try/catch → `InvalidPluginExecutionException`
- Do NOT make HTTP/Graph/AAD calls from a plugin, and do NOT extend `BaseProxyPlugin` (retired — see the note above). A plugin needing to reach the BFF is a design smell → move the trigger to a Code Page / PCF / web resource using `@spaarke/auth`.
- Redact sensitive data before logging request/response payloads; never persist a secret on a Dataverse column
