# SOURCE — azure-functions-isv

> Provenance for Azure Functions multi-tenant ISV curation.

**Curated**: 2026-05-19
**Curator**: researcher subagent (Insights Engine pre-design research)
**Refresh cadence**: monthly (see `knowledge/REFRESH-LOG.md`)

---

## Source documents

This topic is a **reference-only** topic — no curated samples (yet). The README.md is the substantive content; this SOURCE.md records the documentation pages consulted.

| Page | URL | Fetched / accessed | Relevance |
|---|---|---|---|
| Architectural Approaches for the Deployment and Configuration of Multitenant Solutions | https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/approaches/deployment-configuration | 2026-05-19 | Tenant-list-as-config vs tenant-list-as-data; deployment stamps pattern; Bicep approach |
| Azure Functions Flex Consumption plan hosting | https://learn.microsoft.com/en-us/azure/azure-functions/flex-consumption-plan | 2026-05-19 | Current recommended hosting plan, always-ready instances, per-function scaling groups |
| Azure Functions Premium plan | https://learn.microsoft.com/en-us/azure/azure-functions/functions-premium-plan | 2026-05-19 | Always-ready + pre-warmed pattern; comparison with Flex |
| Event-driven scaling in Azure Functions | https://learn.microsoft.com/en-us/azure/azure-functions/event-driven-scaling | 2026-05-19 | Trigger semantics, scaling behavior |
| Managed identities for App Service and Azure Functions | https://learn.microsoft.com/en-us/azure/app-service/overview-managed-identity | 2026-05-19 | UAMI vs system-assigned, multiple identity binding |
| Use Bicep to deploy resources to tenant | https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/deploy-to-tenant | 2026-05-19 | Tenant-scope deployments |
| Use Webhooks to Create External Handlers for Server Events (Dataverse) | https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-webhooks | 2026-05-19 | Dataverse webhook trigger semantics (256 KB limit, retry, auth) |
| Azure Service Bus Integration for Dataverse | https://learn.microsoft.com/en-us/power-apps/developer/data-platform/azure-integration | 2026-05-19 | Service Bus integration via plug-in registration, contract types, async semantics |

## Gaps / open

- **No first-party "multi-tenant ISV with per-tenant Function App + per-tenant Service Bus + Bicep" reference architecture** consolidated in one place. The pieces exist; the integrated reference is left to ISVs to assemble.
- **Flex Consumption regional availability** is narrower than Consumption and not currently called out in a single place. Confirm per-region availability before Bicep-templating.
- **Bicep modules for Flex Consumption** are still evolving — the property shape (`functionAppConfig` with `scaleAndConcurrency.alwaysReady[]`) is current as of the doc date but worth checking the latest `Microsoft.Web/sites@2024-04-01` API version reference.

## Samples not curated (deliberate)

No sample code copied; the README contains a canonical Bicep skeleton with all the load-bearing properties named. For runnable end-to-end samples, `Azure-Samples/functions-flex-consumption-samples` is the upstream reference (consider curating selectively in a future refresh if Spaarke needs deeper detail).
