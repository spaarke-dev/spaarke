# Azure Functions for ISV / multi-tenant scenarios (2026-05)

> **Status**: Researched 2026-05-19 for `projects/ai-spaarke-insights-engine-r1/`.
> **Curator**: researcher subagent (auto-research, not yet senior-engineer-reviewed).
> **Refresh cadence**: monthly per [`knowledge/REFRESH-PROCEDURE.md`](../REFRESH-PROCEDURE.md).

The Insights Engine uses Azure Functions for the Dataverse-to-AI-Search sync pipeline (out-of-band, event-driven). ADR-001 was updated to permit Functions for this kind of out-of-band integration work. This document captures current best practices for Functions in an ISV multi-tenant context, with focus on the Spaarke deployment model: one set of resources per tenant, packaged via Bicep.

---

## Status as of 2026-05

Azure Functions has three current hosting plans: Consumption (the original, still GA), Premium (warm-pool model with always-ready and pre-warmed instances), and **Flex Consumption** (2025-GA; Microsoft's current recommendation for new serverless workloads — consumption-style billing with always-ready instances, VNet integration, and per-function scaling groups). The 2026 sweet spot for the Insights Engine is Flex Consumption with 1–2 always-ready instances per tenant — eliminates cold start on the sync pipeline without the always-on cost of Premium. Identity isolation per tenant via per-Function-App user-assigned managed identities is the standard pattern; Application Insights correlation across BFF App Service + Functions works via shared workspace-based AI resource and the same `APPLICATIONINSIGHTS_CONNECTION_STRING`.

## Key URLs consulted (2026-05-19)

- [Azure Functions hosting plans overview](https://learn.microsoft.com/en-us/azure/azure-functions/functions-scale)
- [Azure Functions Premium plan](https://learn.microsoft.com/en-us/azure/azure-functions/functions-premium-plan)
- [Azure Functions Flex Consumption plan hosting](https://learn.microsoft.com/en-us/azure/azure-functions/flex-consumption-plan)
- [Event-driven scaling in Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/event-driven-scaling)
- [Architectural Approaches for the Deployment and Configuration of Multitenant Solutions](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/approaches/deployment-configuration) — last updated 2026-04-30
- [Use Bicep to deploy resources to tenant](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/deploy-to-tenant)
- [Managed identities for App Service and Azure Functions](https://learn.microsoft.com/en-us/azure/app-service/overview-managed-identity)
- [Use Webhooks to Create External Handlers for Server Events](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-webhooks) — Dataverse-side of the sync trigger
- [Azure Service Bus Integration for Dataverse](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/azure-integration)

## Findings

### 1. Hosting plan choice (Flex Consumption recommended)

For 2026 ISV workloads, the recommendation order is:

1. **Flex Consumption** — consumption billing model; supports always-ready instances (eliminates cold start for the configured instance count), VNet integration (which Premium pioneered), per-function scaling groups (HTTP, Blob, Durable, others scale independently). Microsoft's recommended new default.
2. **Premium** — appropriate when you need >20 always-ready instances per app, longer execution timeouts (60 min vs Flex's 60 min — now comparable), or specific networking features Flex doesn't yet have. Maximum 20 always-ready instances per app.
3. **Consumption** — pure pay-per-execution. Cold starts can be hundreds of ms to several seconds on .NET. Use only for genuinely low-frequency or non-latency-sensitive jobs.
4. **Dedicated (App Service Plan)** — appropriate only if you're already operating a Plan for other reasons; not the right primary choice.

**Spaarke Insights Engine recommendation**: Flex Consumption with 1 always-ready instance per tenant. Eliminates cold start for the webhook-triggered sync, costs ~$15–$25/mo per tenant for always-ready capacity, scales naturally on burst.

### 2. Bicep per-tenant deployment pattern

Two patterns map to the Spaarke tenant model:

- **Tenant list as configuration** (parameters file in CI/CD) — works up to ~10 tenants, becomes painful above. Bicep deploys all tenant resources from a single template using a `for tenant in tenantList` loop.
- **Tenant list as data** (control plane + tenant catalog) — recommended once we expect to onboard >10 tenants. A control-plane API ingests a new-tenant request, invokes Bicep deployment of just that tenant's resources, then writes the new tenant's resource IDs to a central catalog (Cosmos or Azure SQL).

For r1 of the Insights Engine, **tenant-list-as-configuration is fine** — Spaarke isn't yet onboarding tenants by self-service. Plan for the data model upgrade by Phase 2.

Canonical Bicep skeleton for per-tenant resources (Function App + dependencies):

```bicep
// per-tenant.bicep
@description('Tenant identifier (lowercase, no dashes)')
param tenantId string

@description('Azure region')
param location string = resourceGroup().location

@description('Existing shared Application Insights workspace ID')
param appInsightsConnectionString string

var prefix = 'spaarke-${tenantId}'

// User-assigned managed identity per tenant
resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-uami'
  location: location
}

// Storage account for Functions (FlexConsumption uses Blob)
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = { /* ... */ }

// FlexConsumption hosting plan
resource hostingPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${prefix}-plan'
  location: location
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  kind: 'functionapp,linux'
  properties: { reserved: true }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: '${prefix}-func'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${uami.id}': {} }
  }
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: hostingPlan.id
    functionAppConfig: {
      deployment: { /* deployment storage config */ }
      scaleAndConcurrency: {
        alwaysReady: [
          { name: 'http', instanceCount: 1 }
          { name: 'sync', instanceCount: 1 }   // custom scaling group
        ]
        instanceMemoryMB: 2048
        maximumInstanceCount: 100
      }
      runtime: { name: 'dotnet-isolated', version: '8.0' }
    }
    siteConfig: {
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'AZURE_CLIENT_ID', value: uami.properties.clientId }  // for DefaultAzureCredential to prefer UAMI
        // ... other tenant-specific config
      ]
    }
  }
}
```

### 3. Identity isolation per tenant

**Principle**: each tenant's Function App gets its own user-assigned managed identity (UAMI). That UAMI is granted access to *only that tenant's* resources (Dataverse environment, AI Search index, Cosmos Gremlin account, etc.). A leaked credential or compromised Function in one tenant cannot reach another tenant's data.

Implementation notes:

- Use **user-assigned** identities, not system-assigned. UAMI survives the Function App being deleted and recreated (useful during disaster recovery and for redeployments). System-assigned dies with the resource.
- Configure `AZURE_CLIENT_ID` app setting so `DefaultAzureCredential` picks the UAMI when multiple identities are bound to the Function.
- RBAC grants: the UAMI needs `Search Index Data Contributor` on the tenant's AI Search service, `DocumentDB Account Contributor` (or fine-grained data-plane role) on the Cosmos account, and Dataverse user mapping (Application user with appropriate security role).
- For Dataverse, the standard pattern is to register the UAMI's *associated Entra application* (Microsoft.ManagedIdentity creates a backing app registration automatically) as an Application User in the Dataverse environment, with a security role scoped to the tables the sync needs to read.

### 4. Sharing Application Insights correlation between BFF App Service and Functions

The pattern that gives you end-to-end distributed traces across BFF → Function → AI Search:

1. **Single shared Application Insights resource** per tenant (or per environment if you want a single observability lens across tenants — Spaarke's tenancy model favors per-tenant for blast radius).
2. **Both BFF and Function set `APPLICATIONINSIGHTS_CONNECTION_STRING`** to the same value.
3. **Use the auto-instrumentation in `Microsoft.ApplicationInsights.AspNetCore` (BFF)** and **`Microsoft.Azure.Functions.Worker.ApplicationInsights` (Function, isolated worker)**. Both emit W3C trace-context headers (`traceparent`).
4. **When the Function calls AI Search or Cosmos**, ensure the outbound HTTP client uses `HttpClientFactory` so AI's auto-correlation injects `traceparent` automatically.
5. If your sync pipeline uses Service Bus as the trigger, Service Bus messages carry `Diagnostic-Id` / `traceparent` properties — the auto-instrumentation handles this in both directions.

End-to-end trace example flow:
```
BFF (HTTP) → BFF emits OBO call to Dataverse → Dataverse webhook → Service Bus message
  → Function trigger → Function calls AI Search Index API → all four in a single end-to-end trace
```

### 5. Dataverse webhook integration patterns

Two options for the Dataverse → Function sync trigger:

| Trigger type | Pros | Cons | When to use |
|---|---|---|---|
| **Webhook → Function HTTP trigger** | Simplest, lowest latency, sync + async supported, 60s timeout per webhook call | Single subscriber (one webhook → one endpoint). 256 KB payload limit (some context properties dropped above). Synchronous webhook blocks Dataverse on the response. | Single Function App per tenant. Acceptable for the Insights Engine sync. |
| **Service Bus topic → Function Service Bus trigger** | Multi-subscriber (fan-out), persistent queueing, retry, dead-letter. 192 KB payload limit (similar to webhook but with `MessageMaxSizeExceeded` flag retained). | More moving parts (Service Bus instance + topic + subscriptions). Asynchronous only. | When multiple downstream consumers need the same event, or when the sync must survive Function outages. |

**Spaarke Insights Engine recommendation**: Use **Service Bus topic per tenant** as the buffer between Dataverse and the Function. Reasoning:

1. Survives Function App restarts and deployments — events queue up rather than getting lost.
2. Single Dataverse-side configuration (one service endpoint) regardless of how many downstream consumers we add later (AI Search sync, graph builder, analytics).
3. Dead-letter queue gives a recovery path for poison messages.
4. The 192 KB threshold drop behavior is acceptable for the Insights Engine because we re-fetch the full record from Dataverse anyway (the webhook payload only carries the operation + entity reference, not the full state we want to index).

Use a single Service Bus topic with one subscription per consumer Function. Use `Auth=Managed Identity` from Dataverse to Service Bus (Microsoft Entra-based, no SAS to rotate).

### 6. Cold start mitigation

For the Insights Engine sync pipeline:

- **Always-ready instances on Flex Consumption.** Per tenant, configure 1 always-ready instance per scaling group. Eliminates the 1–3s cold start that would otherwise appear on the first webhook of a quiet day.
- **Prefer the .NET isolated worker model** (`dotnet-isolated`). The in-process model is being deprecated; isolated has slightly better cold start once .NET 8+ AOT runs are tuned, and is the only supported model for new Function Apps.
- **Use `HttpClientFactory`** (single shared instance) — naive `new HttpClient()` per invocation adds startup cost.
- **Reuse SDK clients** across invocations via DI (`SearchClient`, `CosmosClient`). They handle connection pooling internally.
- **Watch out for the "First Function Cold Start" issue**: after a deployment, the *first* invocation per scaling group can be cold even with always-ready, because always-ready instances pull the new code on first activation. Plan deployments for low-traffic windows or use deployment slots.

## Implications for Spaarke Insights Engine

1. **Per-tenant Function App + per-tenant UAMI + per-tenant Service Bus topic** is the correct isolation pattern. Bicep template should provision all three as a single tenant module.
2. **Flex Consumption with 1 always-ready instance per scaling group.** Don't default to Premium unless we hit a specific Flex limitation.
3. **Service Bus topic as the buffer between Dataverse webhook and Function.** Even though only one subscriber exists today, the topic model gives us room to grow without re-registering Dataverse webhooks.
4. **Single tenant-scoped Application Insights** correlated across BFF + Function. End-to-end traces are a primary observability requirement for Insights Engine debugging.
5. **Bicep tenant-list-as-configuration is OK for r1; design for tenant-list-as-data by Phase 2** — pick a control-plane pattern (workflow trigger + tenant catalog) before tenant count hits ~10.
6. **The Function's signature is "small, single-purpose, idempotent"**. It receives a Dataverse change event, re-reads the current Dataverse record, projects to AI Search + Cosmos Gremlin, returns. Don't accumulate state in the Function; let Service Bus handle ordering and retry.

## Open questions

- **Maximum Service Bus topic count per Service Bus namespace** — at scale (thousands of tenants), do we share namespaces? Probably yes (Standard tier supports 1000 topics per namespace). Worth planning.
- **What's the cost ceiling for "always-ready 1 instance" per tenant at 100+ tenants?** Need to model. At ~$20/mo/tenant just for the warm instance, 100 tenants = $2k/mo of idle capacity. Consider sharing Function App across tenants with tenant-context discriminator if the cost gets material — but that compromises isolation. Trade-off to decide explicitly.
- **Flex Consumption regional availability** is narrower than Consumption — confirm Spaarke's target regions all support FC1 SKU before committing.
- **Dataverse + Service Bus with managed identity** is supported but newer; the older SAS pattern is more battle-tested. Worth a short spike to confirm the MI path works for our tenancy model before committing the design.
