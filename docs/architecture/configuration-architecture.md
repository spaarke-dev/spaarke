# Configuration Architecture

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: Documents the BFF API configuration subsystem — options classes, validators, appsettings hierarchy, and Key Vault integration.

---

## Overview

The Sprk.Bff.Api uses the .NET Options pattern extensively to manage configuration across 20+ options classes covering seven functional domains. Configuration values flow from `appsettings.template.json` through Azure App Service settings and Key Vault references, with startup-time validation ensuring misconfiguration fails fast rather than causing runtime errors.

The design follows ADR-010 (DI Minimalism) by using `ValidateOnStart()` with custom `IValidateOptions<T>` validators for cross-property rules, and `DataAnnotations` for simple range/required constraints. Secrets are never stored in configuration files — they use `@Microsoft.KeyVault(...)` references resolved by Azure App Service at runtime.

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| GraphOptions | `src/server/api/Sprk.Bff.Api/Configuration/GraphOptions.cs` | Microsoft Graph client credentials and managed identity settings |
| GraphOptionsValidator | `src/server/api/Sprk.Bff.Api/Configuration/GraphOptionsValidator.cs` | Conditional validation: requires ClientSecret or ManagedIdentity.ClientId |
| GraphResilienceOptions | `src/server/api/Sprk.Bff.Api/Configuration/GraphResilienceOptions.cs` | Retry, circuit breaker, and timeout settings for Graph API calls |
| DocumentIntelligenceOptions | `src/server/api/Sprk.Bff.Api/Configuration/DocumentIntelligenceOptions.cs` | Azure OpenAI, Document Intelligence, AI Search, file type support, prompt templates |
| DocumentIntelligenceOptionsValidator | `src/server/api/Sprk.Bff.Api/Configuration/DocumentIntelligenceOptionsValidator.cs` | Conditional validation: skips when `Enabled=false` |
| AnalysisOptions | `src/server/api/Sprk.Bff.Api/Configuration/AnalysisOptions.cs` | AI analysis, Prompt Flow, RAG deployment, chat, export, multi-tenant settings |
| DataverseOptions | `src/server/api/Sprk.Bff.Api/Configuration/DataverseOptions.cs` | Dataverse Web API credentials |
| RedisOptions | `src/server/api/Sprk.Bff.Api/Configuration/RedisOptions.cs` | Redis connection and instance naming |
| ServiceBusOptions | `src/server/api/Sprk.Bff.Api/Configuration/ServiceBusOptions.cs` | Azure Service Bus queue names and concurrency |
| AgentTokenOptions | `src/server/api/Sprk.Bff.Api/Configuration/AgentTokenOptions.cs` | M365 Copilot agent OBO token exchange settings |
| AgentTokenOptionsValidator | `src/server/api/Sprk.Bff.Api/Configuration/AgentTokenOptions.cs` | Cross-property validation (trailing slash on Dataverse URL) |
| AiSearchResilienceOptions | `src/server/api/Sprk.Bff.Api/Configuration/AiSearchResilienceOptions.cs` | Azure AI Search retry, circuit breaker, and timeout tuning |
| CommunicationOptions | `src/server/api/Sprk.Bff.Api/Configuration/CommunicationOptions.cs` | Approved senders, webhook URL, archive container |
| EmailProcessingOptions | `src/server/api/Sprk.Bff.Api/Configuration/EmailProcessingOptions.cs` | Email-to-document processing: polling, webhooks, filtering, attachment rules |
| FinanceOptions | `src/server/api/Sprk.Bff.Api/Configuration/FinanceOptions.cs` | Finance intelligence thresholds and AI model deployments |
| OfficeRateLimitOptions | `src/server/api/Sprk.Bff.Api/Configuration/OfficeRateLimitOptions.cs` | Per-endpoint rate limits using sliding window in Redis |
| ToolFrameworkOptions | `src/server/api/Sprk.Bff.Api/Configuration/ToolFrameworkOptions.cs` | AI Tool Framework enable/disable, handler blacklist, parallel execution |
| SpeAdminOptions | `src/server/api/Sprk.Bff.Api/Configuration/SpeAdminOptions.cs` | SPE dashboard sync interval and pagination |
| DemoProvisioningOptions | `src/server/api/Sprk.Bff.Api/Configuration/DemoProvisioningOptions.cs` | Demo environment configs, license SKUs, admin notification |
| ReindexingOptions | `src/server/api/Sprk.Bff.Api/Configuration/ReindexingOptions.cs` | Document re-indexing triggers and tenant routing |
| AiSearchOptions | `src/server/api/Sprk.Bff.Api/Options/AiSearchOptions.cs` | AI Search endpoint, index names, semantic config |
| LlamaParseOptions | `src/server/api/Sprk.Bff.Api/Options/LlamaParseOptions.cs` | LlamaParse document parsing (optional, disabled by default) |

## Data Flow

Configuration resolution follows this sequence at startup and runtime:

1. **Base configuration** loads from `appsettings.template.json` (committed, contains `#{PLACEHOLDER}#` tokens and `@Microsoft.KeyVault(...)` references)
2. **Environment overrides** are applied by Azure App Service application settings, which replace `#{PLACEHOLDER}#` tokens during deployment
3. **Key Vault references** (`@Microsoft.KeyVault(SecretUri=...)`) are resolved by the App Service Key Vault reference feature at runtime — secrets never appear in configuration files
4. **Options binding** maps JSON sections to strongly-typed classes via `SectionName` constants (e.g., `"DocumentIntelligence"` maps to `DocumentIntelligenceOptions`)
5. **Startup validation** runs `DataAnnotations` attributes and custom `IValidateOptions<T>` validators — misconfiguration causes startup failure with actionable error messages
6. **Runtime access** is via `IOptions<T>`, `IOptionsSnapshot<T>`, or `IOptionsMonitor<T>` depending on whether hot-reload is needed

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Consumed by | Resilience layer | `IOptions<GraphResilienceOptions>`, `IOptions<AiSearchResilienceOptions>` | Configures retry/circuit breaker behavior |
| Consumed by | SpeFileStore | `IOptions<GraphOptions>` | Graph client credentials |
| Consumed by | AI pipeline | `IOptions<DocumentIntelligenceOptions>`, `IOptions<AnalysisOptions>` | Model names, endpoints, feature flags |
| Consumed by | Background jobs | `IOptions<ServiceBusOptions>` | Queue names, concurrency limits |
| Consumed by | Rate limiting | `IOptions<OfficeRateLimitOptions>` | Per-endpoint limits |
| Depends on | Azure Key Vault | `@Microsoft.KeyVault(SecretUri=...)` | All secrets resolved at App Service level |
| Depends on | Azure App Service | Application settings | Token replacement for `#{PLACEHOLDER}#` values |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Validation pattern | `IValidateOptions<T>` + `ValidateOnStart()` | Cross-property rules (e.g., conditional required fields) cannot be expressed with `DataAnnotations` alone | ADR-010 |
| Conditional validation | Skip validation when feature disabled | Allows startup without Azure OpenAI config when `Enabled=false` | — |
| Secret management | `@Microsoft.KeyVault(...)` references | Secrets never in config files; resolved by App Service runtime | — |
| Section naming | `SectionName` constant on each options class | Single source of truth for binding path | ADR-010 |
| Dual Options directories | `Configuration/` (primary) + `Options/` (secondary) | `Options/` contains newer additions that reference Key Vault secret names rather than direct values | — |

## Constraints

- **MUST**: Use `SectionName` constant for appsettings section binding — never hard-code strings at registration site
- **MUST**: Use `@Microsoft.KeyVault(...)` for all secrets in `appsettings.template.json` — never commit actual secret values
- **MUST**: Implement `IValidateOptions<T>` for cross-property validation rules (e.g., "required when `Enabled=true`")
- **MUST**: Use `[Range]`, `[Required]`, `[Url]` attributes for simple single-property constraints
- **MUST NOT**: Store secrets in `appsettings.template.json` — use `#{PLACEHOLDER}#` tokens or Key Vault references
- **MUST NOT**: Access `IConfiguration` directly in services — always use strongly-typed options

## Known Pitfalls

- **AgentToken trailing slash**: `DataverseEnvironmentUrl` must not end with `/` — the validator catches this but it causes silent scope construction failures if missed
- **DocumentIntelligence conditional validation**: When `Enabled=false`, OpenAI endpoint/key validation is skipped. Toggling `Enabled` to `true` at runtime without setting credentials will fail
- **Email attachment patterns**: `SignatureImagePatterns` and `TrackingPixelPatterns` use regex — invalid patterns cause runtime exceptions during email processing, not at startup
- **Redis fallback**: When `Redis:Enabled=false`, the system uses in-memory cache. This is fine for dev but breaks distributed scenarios in production with multiple App Service instances
- **Key Vault reference format**: Two formats are used — `@Microsoft.KeyVault(SecretUri=...)` and `@Microsoft.KeyVault(VaultName=...;SecretName=...)`. Both are valid but must match Azure documentation

## Related

- [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) — DI minimalism and options pattern constraints
- [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) — Redis-first caching (RedisOptions)
- [Resilience Architecture](resilience-architecture.md) — Consumes resilience options classes
