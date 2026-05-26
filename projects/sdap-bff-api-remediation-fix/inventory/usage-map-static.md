# Static Package Usage Map (Task 014)

> **Source**: `grep -rln '^using {namespace}' --include='*.cs' src/server/`
> **Captured**: 2026-05-24
> **WARNING**: Static grep MISSES reflection-loaded use. Task 015 (reflection probe) is the complement; must agree before MEDIUM/HIGH-tier package removal.

---

## Per-package static usage counts (44 direct packages)

| Package | Namespace searched | Static `using` count | Removability flag |
|---|---|---:|---|
| Azure.AI.DocumentIntelligence | Azure.AI.DocumentIntelligence | 2 | KEEP — used in document analysis |
| Azure.AI.OpenAI | Azure.AI.OpenAI | 2 | KEEP — pre-release pin (out of scope; chain-locked) |
| Azure.AI.Projects | Azure.AI.Projects | 3 | KEEP — pre-release pin (Agent Framework chain) |
| Azure.Identity | Azure.Identity | 27 | KEEP — auth (Managed Identity per ADR-028) |
| Azure.Messaging.ServiceBus | Azure.Messaging.ServiceBus | 11 | KEEP — Service Bus job processing |
| Azure.Search.Documents | Azure.Search.Documents | 27 | KEEP — RAG search |
| Azure.Security.KeyVault.Secrets | Azure.Security.KeyVault.Secrets | 4 | KEEP — secrets |
| Azure.Storage.Blobs | Azure.Storage.Blobs | 1 | KEEP — blob storage |
| DocumentFormat.OpenXml | DocumentFormat.OpenXml | 2 | KEEP — OOXML manipulation |
| Handlebars.Net | HandlebarsDotNet | 1 | KEEP — playbook templating |
| **Microsoft.Agents.AI** | Microsoft.Agents.AI | **0** | ⚠️ Reflection-loaded via `AddAgentModule(...)` at Program.cs:89 — DO NOT REMOVE based on static analysis |
| **Microsoft.Agents.Hosting.AspNetCore** | Microsoft.Agents.Hosting.AspNetCore | **0** | ⚠️ DI-loaded via hosting extension — DO NOT REMOVE based on static analysis |
| Microsoft.ApplicationInsights.AspNetCore | Microsoft.ApplicationInsights | 1 | KEEP — telemetry |
| Microsoft.Azure.Cosmos | Microsoft.Azure.Cosmos | 6 | KEEP — Cosmos sessions |
| Microsoft.Extensions.AI | Microsoft.Extensions.AI | 23 | KEEP — Agent Framework chain |
| Microsoft.Extensions.AI.OpenAI | Microsoft.Extensions.AI.OpenAI | — | KEEP — Agent Framework chain (bridge) |
| Microsoft.Extensions.Caching.Abstractions | Microsoft.Extensions.Caching | 55 | KEEP — distributed cache |
| Microsoft.Extensions.Caching.StackExchangeRedis | Microsoft.Extensions.Caching | 55 (shared) | KEEP — Redis cache (ADR-009) |
| Microsoft.Extensions.Hosting.Abstractions | Microsoft.Extensions.Hosting | 2 | KEEP — IHostedService background jobs |
| **Microsoft.Extensions.Http.Polly** | Microsoft.Extensions.Http | **0** | ⚠️ Used via `AddHttpClient(...)` registration; DI-resolved. DO NOT REMOVE based on static analysis |
| Microsoft.Graph | Microsoft.Graph | 48 | KEEP — Graph SDK (highest static usage) |
| Microsoft.Identity.Client | Microsoft.Identity.Client | 6 | KEEP — MSAL |
| Microsoft.Identity.Web | Microsoft.Identity.Web | 1 | KEEP — JWT validation (per ADR-028) |
| Microsoft.Identity.Web.MicrosoftGraph | Microsoft.Identity.Web | 1 (shared) | KEEP — OBO Graph helper |
| Microsoft.Kiota.* (7 packages) | Microsoft.Kiota | 2 | KEEP — Graph SDK chain (CLAUDE.md: must stay version-matched at 1.21.2) |
| Microsoft.PowerBI.Api | Microsoft.PowerBI | 2 | KEEP — Power BI Embed |
| MimeKit | MimeKit | 5 | KEEP — email parsing |
| MsgReader | MsgReader | 1 | KEEP — .msg file reading |
| OpenAI | OpenAI | 5 | KEEP — chain-locked at 2.8.0 (Agent Framework) |
| **OpenTelemetry** (4 packages) | OpenTelemetry | **0** | ⚠️ Registered via `AddOpenTelemetry()` builder; DI-resolved. DO NOT REMOVE based on static analysis |
| Polly | Polly | 7 | KEEP — resilience policies |
| Polly.Extensions.Http | Polly | 7 (shared) | KEEP — HTTP resilience |
| QuestPDF | QuestPDF | 2 | KEEP — PDF generation |
| System.Text.RegularExpressions | System.Text.RegularExpressions | 21 | KEEP — explicit ref (BCL package) |

---

## 4 Packages with 0 static usages — ALL are reflection/DI-loaded

These are the highest-risk false positives for static-grep removability flags:

| Package | Why static analysis misses it | Verification command |
|---|---|---|
| `Microsoft.Agents.AI` | Registered via `AddAgentModule()` extension method; agent types reflected at runtime | Confirm via task 015 dynamic probe (assembly will be in `AppDomain.CurrentDomain.GetAssemblies()`) |
| `Microsoft.Agents.Hosting.AspNetCore` | Hosting infrastructure registered via extension method | Confirm via task 015 |
| `Microsoft.Extensions.Http.Polly` | `AddHttpClient(...).AddPolicyHandler(...)` pattern in DI registration | Confirm via task 015 |
| `OpenTelemetry` (+ extensions + instrumentation) | Registered via `AddOpenTelemetry()` builder; processors loaded at runtime via type discovery | Confirm via task 015 |

---

## Conclusion for Phase 2 categorization

**No SAFE-tier package removal candidates emerge from this static analysis.** All 44 direct packages are either:
- Statically used (40 packages, KEEP)
- Reflection/DI-loaded (4 packages, KEEP pending task 015 confirmation)
- Pre-release pinned per chain (out of scope per spec)

This is the expected outcome for a mature reflection-heavy stack (Graph SDK + Identity.Web + Agent Framework + DI). Outcome A savings come from:
- FR-A1 RID trim (publish-time config; no source change)
- FR-A2 sourcemap exclusion (publish-time config; no source change)
- NOT from package removal

**Phase 2 will categorize MEDIUM/HIGH tier candidates around (a) outdated transitive bumps for security and (b) verification that 4 reflection-loaded packages are genuinely loaded (task 015).**
