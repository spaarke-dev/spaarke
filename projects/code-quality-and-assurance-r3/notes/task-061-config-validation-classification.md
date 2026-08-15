# Task 061 — BFF Configuration Validation Classification & Exemption List

> **Task**: 061 — uniform fail-fast configuration validation (refactor ask #2)
> **Date**: 2026-08-14
> **Grounded against**: net10 HEAD (post-532-commit master merge). The POML line numbers predate the
> net10 merge; this census re-grounded at head. **Material finding**: the net10 merge already closed
> most of the POML's stated gap — CommunicationModule's ~12 `services.Configure<>` sites were already
> migrated to the validated `AddOptions().ValidateDataAnnotations().ValidateOnStart()` chain upstream.
> **Consumed by**: task 040 ArchTest rule (a) — the Tier-2 exemption list below IS the allowlist.

## Classification model (per POML constraint)

Every options registration is classified into exactly one tier:

- **Tier 1 — customer-critical** → `[Required]` on critical props (or cross-property `IValidateOptions`)
  + `ValidateDataAnnotations()` + `ValidateOnStart()`. A missing key fails **at startup** naming the key.
- **Tier 2 — kill-switch-gated** → validation **deferred** (no `ValidateOnStart`), because the app MUST
  boot with the feature off and its config section absent. Required-when-enabled semantics live in an
  `IValidateOptions<T>` (short-circuits to Success when disabled) or at the use-site. **Bare `[Required]`
  is forbidden on these classes** (2026-06-09 BingGrounding eager-`.Value` startup-crash class).
- **Tier 3 — optional-with-safe-defaults** → binds valid defaults when the section is absent; `[Required]`
  is NOT used. `ValidateOnStart` is behavior-neutral where present but **not mandated** (a misconfig here
  is non-customer-critical and self-heals via defaults).

## Tier 1 — customer-critical (validate on start) — 24 registrations

| Option | Module | Notes |
|---|---|---|
| GraphOptions | ConfigurationModule | + `GraphOptionsValidator` (IValidateOptions) |
| DataverseOptions | ConfigurationModule | ClientSecret `[Required]` — stays until #3b MI migration (task 011) |
| ServiceBusOptions | ConfigurationModule | |
| RedisOptions | ConfigurationModule | |
| AnalysisOptions | ConfigurationModule | |
| ModelSelectorOptions | ConfigurationModule | |
| SharePointEmbeddedOptions | ConfigurationModule | |
| EmailProcessingOptions | EmailServicesModule | webhook signing key; required-when-webhook at use-site (fail-closed 401) |
| FinanceOptions | FinanceModule | migrated task 061 (behavior-neutral; no `[Required]`) |
| SpeAdminOptions | SpeAdminModule | migrated task 061 ([Range]-only) |
| TenantEnvironmentRoutingOptions | AuthorizationModule | migrated task 061 (deny-by-design; empty list denies all) |
| GraphResilienceOptions | GraphModule | |
| OfficeRateLimitOptions | OfficeModule | |
| AiSearchResilienceOptions | TelemetryModule | |
| AutoFile, CategoryRouting, TrackingFooter, SemanticMatch, AiClassification, RecordNameMatch, ContactNameMatch, Affinity, AttachmentMatch, CommsPolicy, AcsEventGridIngress, MembershipReconcile | CommunicationModule | 12 — validated-on-start (net10 merge + task 061 finish); AcsEventGridIngress carries the fail-closed topic allow-list |

**Cross-property invariant added (task 061, POML criterion 5)**: `AgentServiceOptionsValidator`
(`IValidateOptions<AgentServiceOptions>`) enforces the **Enabled→Endpoint/AgentId** invariant. Registered
in ConfigurationModule with `ValidateOnStart` — a misconfigured `AgentService:Enabled=true` (missing
Endpoint/AgentId) now fails startup naming the key(s) and aggregating both; `Enabled=false` boots cleanly.
Covered by `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/AgentServiceOptionsValidationTests.cs` (4
tests, both directions + fail-fast + gated boot path).

## Tier 2 — kill-switch-gated (DEFERRED — EXEMPTION LIST / task-040 allowlist)

These MUST NOT be forced to `ValidateOnStart` with bare `[Required]`. This is the authoritative allowlist
task 040's ArchTest rule (a) consumes.

| Option | Module | Gating flag / reason |
|---|---|---|
| CommunicationOptions | CommunicationModule | 4 `[Required]` webhook members (WebhookNotificationUrl, WebhookClientState, WebhookSigningKey, ApprovedSenders) present only when Graph-webhook comms provisioned; no full-boot fixture seeds all 4. Required-when-provisioned at use-site. |
| PowerBiOptions | ConfigurationModule | `sprk_ReportingModuleEnabled` kill switch |
| AgentServiceOptions | ConfigurationModule | `AgentService:Enabled` (ADR-018) — **now** Enabled→Endpoint via IValidateOptions (safe: short-circuits when disabled) |
| CodeInterpreterOptions | ConfigurationModule | `CodeInterpreter:Enabled` (ADR-018) |
| BingGroundingOptions | ConfigurationModule | `BingGrounding:Enabled` (ADR-018) — the 2026-06-09 incident origin; `[Required]` removed from class by design |
| DemoProvisioningOptions | RegistrationModule | demo provisioning; demo env decommissioned (dev-only reality) |
| AcsProvisioningOptions | RegistrationModule | ACS provisioning gated |
| AgentTokenOptions, AgentConfigurationOptions | AgentModule | config may be absent until agent feature configured (in-code comment) |
| SubjectSchemeCatalogOptions, InsightsMirrorOptions | InsightsModule | optional insights config |
| ConfidenceThresholdOptions | InsightsExtractionModule | deferred (in-code comment); DataAnnotations only |
| DeepLinkBuilderOptions | TodoSyncModule | cross-subtree bind; optional |
| TodoGenerationOptions | WorkspaceModule | optional todo-generation tuning |
| SummarizationCompressionOptions, PinnedContextRecallOptions, MemoryCompositionOptions | AnalysisServicesModule | R6 Pillar-7 memory features; carry `[Required]` but gated/optional-scoped; `BindConfiguration` deferred |
| AssistantCitationHrefOptions | AnalysisServicesModule | bound only inside the Analysis-enabled conditional branch |
| InsightsIntentClassifierOptions | AnalysisServicesModule | classifier opt-out; registration choice made at startup |

## Tier 3 — optional-with-safe-defaults (`services.Configure<>`, VoS not mandated)

Left as `services.Configure<>` by design — safe defaults, non-customer-critical, misconfig self-heals.
Classified for census completeness (POML criterion 4); not migrated (behavior-neutral, low value, avoids
churn/breakage across hot-path DI files).

| Option | Module:line |
|---|---|
| ServiceBusOptions (named override) | OfficeWorkersModule:81 |
| AcsOptions | AcsServiceCollectionExtensions:30 |
| TodoGenerationOptions | WorkspaceModule:115 |
| SignalRDeliveryOptions | NotificationsModule:24 |
| PostUploadIndexingOptions | AnalysisServicesModule:105 |
| EventRulesOptions | AnalysisServicesModule:120 |
| AnalysisOptions (override of validated base) | AnalysisServicesModule:732 |
| AgentTurnOptions | AnalysisServicesModule:738 |
| SuggestionGateOptions | AnalysisServicesModule:837 |
| SessionFilesCleanupOptions | AnalysisServicesModule:1292 |
| ToolFrameworkOptions | AnalysisServicesModule:1659 / ToolFrameworkExtensions:26 |
| MembershipOptions, MembershipCacheInvalidatorOptions, MembershipJunctionUpdaterOptions, MembershipReconciliationOptions | MembershipModule:79/183/225/276 |
| EmbeddingMigrationOptions, ScheduledRagIndexingOptions, RecordSyncOptions, ReindexingOptions, LlamaParseOptions, AiSearchOptions | JobProcessingModule:68/72/77/81/85/86 |

## What task 061 changed (this session)

1. **DI migrations (agent, verified + integrated)**: 5 DI modules — CommunicationModule (12 sites →
   validated chain; CommunicationOptions kept deferred as Tier-2 exempt), EmailServicesModule
   (EmailProcessingOptions → VoS), FinanceModule, SpeAdminModule, AuthorizationModule
   (TenantEnvironmentRoutingOptions). All behavior-neutral (no `[Required]` added to any class → no
   Enabled=false boot regression).
2. **Cross-property validator (main session)**: `AgentServiceOptionsValidator` + `ValidateOnStart` on the
   AgentService registration + registration in ConfigurationModule.
3. **Tests**: `AgentServiceOptionsValidationTests` (4) — startup fail-fast naming key, multi-key
   aggregation, fully-configured boot, gated (disabled) boot. No fixture changes required (CustomWebAppFactory
   already seeds `AgentService:Enabled=false` + Endpoint/AgentId; all contract fixtures set Enabled=false).

## Verification

- `dotnet build -c Release`: 0 errors (24 pre-existing warnings).
- `dotnet test` (full BFF suite): **10,392 passed / 0 failed / 97 skipped**.
- Publish size: see task notes / PR (measured vs 44.96 MB incl-PDBs net10 baseline; behavior-neutral,
  no new NuGet package).
