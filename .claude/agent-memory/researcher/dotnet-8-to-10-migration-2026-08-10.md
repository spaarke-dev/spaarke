---
name: dotnet-8-to-10-migration
description: .NET 8→10 upgrade breaking-change assessment for the BFF (lifecycle dates, cumulative 9+10 breaking changes, C# 14, App Service Linux)
metadata:
  type: project
---

## 2026-08-10: .NET 8 → .NET 10 migration breaking changes (BFF)

**Question**: Authoritative cumulative breaking-change list for upgrading an Azure-hosted ASP.NET Core minimal API from .NET 8 to .NET 10 (skipping 9).

**Findings**: .NET 8 EOS **2026-11-10** (same day as .NET 9 STS EOS) — 3 months away, so the upgrade has a hard deadline. .NET 10 released 2025-11-11, LTS until **2028-11-14**; net10.0 → C# 14 default. Highest-impact for Spaarke BFF: (1) **.NET 10 `BackgroundService.ExecuteAsync` now runs entirely on a background thread** — synchronous pre-first-await code no longer blocks startup ordering; BFF has ~10+ BackgroundService workers, some with deliberate startup-ordering assumptions (TodoGenerationService comments mention 500.30 startup crash guards). (2) **.NET 9 HostBuilder enables ValidateOnBuild/ValidateScopes in Development** — latent DI bugs become dev startup crashes. (3) **NU1510** — `Spaarke.Core`/`Spaarke.Dataverse` pin `System.Text.Json 8.0.5` (CVE fix); on net10.0 these are pruned refs → warning, and `Spaarke.Scheduling` has TreatWarningsAsErrors=true. (4) `new X509Certificate2(pfxBytes,...)` in `CiamGraphClientFactory.cs:167` is SYSLIB0057-obsolete since .NET 9 → migrate to `X509CertificateLoader`. (5) .NET 9 HttpClientFactory primary handler is now `SocketsHttpHandler` (breaks casts to HttpClientHandler) + header/query redaction in logs. (6) .NET 10 config binder preserves JSON `null` (no longer empty-string) and STJ throws early on metadata property-name conflicts ($type etc.). `global.json` pins SDK 8.0.0 → must bump. App Service Linux has .NET 10 GA (Ignite Nov-2025, phased region rollout). No Swashbuckle/WithOpenApi/keyed-services/ConfigurePrimaryHttpMessageHandler usage found in src/server → those deprecations don't bite.

**Sources**:
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core (lifecycle dates)
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0 + /10 (master lists; 10 page marked "work in progress" as of 2026-07-30)
- https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/9/overview + /10/overview
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/configuration-null-values-preserved
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/10/property-name-validation
- https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14
- https://azure.github.io/AppService/ + Ignite 2025 blog (App Service .NET 10 GA)

**Open questions**: whether any BFF worker actually relies on pre-await synchronous startup ordering (needs code audit of each ExecuteAsync); whether System.Linq.Async NuGet is referenced anywhere (would collide with .NET 10's built-in AsyncEnumerable); exact App Service region rollout state for the prod region (westus3).
