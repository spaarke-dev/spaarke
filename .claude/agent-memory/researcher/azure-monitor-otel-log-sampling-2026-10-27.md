---
name: azure-monitor-otel-log-sampling-2026-10-27
description: Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0 drops ILogger records of sampled-out traces by default (5 traces/s rate limiter + EnableTraceBasedLogsSampler=true); fix is one option
metadata:
  type: reference
---

**Question**: Can ILogger records (audit EventId 9821/9822) be dropped by sampling before App Insights in the Spaarke BFF?

**Findings (2026-10-27)**:
- BFF: `Program.cs:36` `builder.Services.AddOpenTelemetry().UseAzureMonitor();` (no options lambda); csproj:189 `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 (depends on Exporter 1.8.3). No sampler, SamplingRatio, TracesPerSecond, OTEL_* env, custom log processor anywhere in BFF code/appsettings/infra.
- 1.6.0 source: `AzureMonitorOptions.TracesPerSecond = 5.0` (line 84) -> RateLimitedSampler; `EnableTraceBasedLogsSampler = true` (line 71). Property is spelled `...LogsSampler` in source/Learn/XML doc; AspNetCore CHANGELOG 1.4.0 mis-spells it `...LogSampler`.
- Exporter 1.8.3 `Internals/LogFilteringProcessor.cs`: exports log iff `SpanId == default || TraceFlags == Recorded`. So any log with an Activity that was sampled out (RecordOnly => flags None) is dropped; logs with no Activity always exported.
- RateLimitedSampler: local-parent decision respected; remote parent ignored -> root request span gets sampled at ~5/s budget, others RecordOnly.
- History: log sampler added AspNetCore 1.4.0 (2025-11-14); default sampler flipped 100% -> RateLimited 5/s in AspNetCore 1.5.0 (2026-04-30) (Exporter changelog: 1.6.0-beta.2).
- Fix: `UseAzureMonitor(o => o.EnableTraceBasedLogsSampler = false)` (BFF-wide; keeps trace sampling for cost). Alternatives: SamplingRatio=1 + TracesPerSecond=null (all traces, cost), or emit audit outside Activity (fragile).
- Side finding: audit logger is `ILogger<Program>` (category "Program"); Production template `Default: Warning` would filter LogInformation (category not under Sprk.Bff.Api) independent of sampling, if that template is what ships.

**Sources**:
- C:/wtr3-005/src/server/api/Sprk.Bff.Api/Program.cs:36, Sprk.Bff.Api.csproj:189
- https://raw.githubusercontent.com/Azure/azure-sdk-for-net/Azure.Monitor.OpenTelemetry.AspNetCore_1.6.0/sdk/monitor/Azure.Monitor.OpenTelemetry.AspNetCore/src/AzureMonitorOptions.cs
- https://raw.githubusercontent.com/Azure/azure-sdk-for-net/Azure.Monitor.OpenTelemetry.Exporter_1.8.3/sdk/monitor/Azure.Monitor.OpenTelemetry.Exporter/src/Internals/LogFilteringProcessor.cs
- https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-configuration (Configure trace-based sampling for logs)

**Open questions**: whether the deployed App Service sets OTEL_TRACES_SAMPLER* app settings (not in repo); which appsettings actually ships to dev/prod.
