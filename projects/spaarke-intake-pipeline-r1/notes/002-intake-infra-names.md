# Task 002 — intake engine infrastructure: names, outputs, deviations

For tasks 001 (engine entrypoint), 008 (dev spike / deployment) and 014 (completion handler). Authored and compiled only; nothing deployed.

## Files
- `infrastructure/bicep/modules/intake-staging-storage.bicep` — private container, 7-day lifecycle rule, container-scoped Blob Data Contributor.
- `infrastructure/bicep/modules/intake-engine-job.bicep` — Container Apps managed environment, event-triggered Job, engine request queue, Service Bus and ACR RBAC.
- `infrastructure/bicep/customer.bicep` / `customer.json` — params `deployIntakeEngine` (default **false**), `intakeEngineImage`, `intakeEngineRegistryName`, `intakeEngineGrantAcrPull`; two conditional module calls; five outputs (empty when off). With the flag off the compiled template differs from before only by the param, the conditional modules, the outputs and `templateHash`.

## Names (contract for task 001 and 014)
| Thing | Name |
|---|---|
| Engine request queue | `intake-engine-requests` (created by the Job module; `maxDeliveryCount` 5, lock 5 min, DLQ on expiry, duplicate detection off) |
| Completion queue | `intake-engine-completions` (maxDeliveryCount 5, duplicate detection off), consumed by the BFF completion processor (task 014). Rework 2026-11-25; was `sdap-jobs` |
| Staging container | `intake-staging` (private; lifecycle deletes blobs under `intake-staging/` 7 days after creation) |
| Environment / Job | `sprk-{customerId}-{env}-cae` / `sprk-{customerId}-{env}-intake-job` (Job name is 32 chars at the longest customerId and env) |
| Container name | `intake-engine` |

Environment variables set on the container (read these in the entrypoint):
`AZURE_CLIENT_ID` (the job's own identity), `INTAKE_SERVICEBUS_FQDN`, `INTAKE_REQUEST_QUEUE`, `INTAKE_COMPLETION_QUEUE` (= `intake-engine-completions`), `INTAKE_STAGING_ACCOUNT_URL`, `INTAKE_STAGING_CONTAINER`, `INTAKE_MAX_DELIVERY_COUNT` (5), `INTAKE_JOB_TIMEOUT_SECONDS` (600), `APPLICATIONINSIGHTS_CONNECTION_STRING`.

## Identity and RBAC
The Job runs as the existing stamp `uami` module (no new identity). Grants made by this task: Data Receiver and Data Sender on the request queue (Sender is for the scheduled retry copy carrying `PriorDeliveryCount`, as `ServiceBusJobProcessor` does), Data Sender on `sdap-jobs`, Blob Data Contributor on `intake-staging` (container scope; BFF and job are the same UAMI, so one assignment), AcrPull on the registry when `intakeEngineGrantAcrPull` is true. Nothing namespace- or account-wide is added.
**Finding:** the stamp UAMI already holds namespace-wide Service Bus Sender + Receiver (`bff-runtime-rbac.bicep`), so on a stamp the queue-scoped grants are a documented contract, not a narrowing of access. A dedicated job identity would give true least privilege; the module takes the identity as parameters, so that is a one-line change in `customer.bicep` if the owner wants it.

## Telemetry
`APPLICATIONINSIGHTS_CONNECTION_STRING` = `monitoring.outputs.connectionString`, the same value the BFF gets as an app setting in `customer.bicep` (the BFF does not read it from Key Vault). It is stored as a Job secret and referenced by `secretRef`.

## Deviations and decisions
1. **API version.** The Job uses `Microsoft.App/jobs@2025-01-01`; `@2024-03-01` has no `identity` on a scale rule (BCP037), which the identity-authenticated KEDA scaler needs.
2. **Logs without a key.** The environment uses `appLogsConfiguration.destination: 'azure-monitor'` plus a diagnostic setting to the stamp Log Analytics workspace. The `logAnalyticsConfiguration` form needs the workspace shared key (`listKeys()`), which `CustomerStampKeylessTemplateTests` forbids.
3. **Registry (escalation trigger 2).** No registry is created. The module takes the name of an EXISTING registry in the stamp resource group and grants AcrPull on it; for a registry elsewhere (the platform ACR from `controlplane-acr.bicep`) pass `intakeEngineGrantAcrPull=false` and grant the identity there. **The owner still has to choose platform ACR versus a per-stamp registry before task 008 deploys**, and a Model 2 stamp (customer tenant) cannot be granted a role on a Spaarke-tenant registry.
4. **Queue placement.** The request queue is a dedicated resource in the Job module, not in `serviceBusQueues`, because `service-bus.bicep` fixes `maxDeliveryCount` at 10 for every queue.
5. **One lifecycle policy per account.** `managementPolicies/default` is a singleton; this module owns the stamp account's only one (`customer.bicep` sets `enableTestDocumentLifecycle: false`). Any later lifecycle rule must be merged into `intake-staging-storage.bicep` or it replaces the 7-day rule.
6. **Timeout vs lock.** Service Bus caps lock duration at 5 minutes and a run may take 10, so the entrypoint must renew the lock (ADR-004 A2).
