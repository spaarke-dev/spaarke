---
name: background-work-hosting-best-practice-2026-09-12
description: MS best practice (Sep 2026) for background/scheduled/queue work beside an App Service .NET API — Functions Flex vs in-process BackgroundService vs WebJobs vs ACA Jobs; timer singleton; Durable Task Scheduler GA; input to uac-r2 background-work ADR rewrite
metadata:
  type: reference
---

# Background / scheduled / queue work hosting — MS best practice (verified 2026-09-12)

**Question**: For uac-r2's ADR rewrite — what does Microsoft currently recommend for background, cron and Service Bus work next to a .NET 10 App Service (Linux) BFF, and is the "no Functions / no Durable" stance still consistent?

**Findings**
- Arch Center *Background jobs* (ms.date 2026-03-30) hosting list = Functions (+Durable), Container Apps (jobs), AKS, Batch, VMs, WebJobs. **In-process IHostedService isn't listed as a hosting option** — it only comes up under "Partitioning" (colocate vs separate: availability, recovery, security, manageability, scalability; separating "adds hosting cost"). Its scaling section says "Scale background tasks independently... Host background tasks in a separate compute service" and "Scale on queue depth". Web-Queue-Worker (2026-01-06) reference = App Service front end + **Functions worker**, in separate plans.
- Scheduled single-execution: the page explicitly warns that scaled schedulers start multiple copies. Recommended = Functions timer (blob-lease distributed lock; UseMonitor default true for intervals of 1 minute or more; IsPastDue; **no retry** on failure; RunOnStartup "rarely if ever" in prod) or ACA Job with concurrency settings. Leader Election (blob lease) is the self-hosted fallback. Flex Consumption has no WEBSITE_TIME_ZONE (UTC only).
- WebJobs: Arch Center says "We don't recommend WebJobs as a general-purpose background job platform for new workloads." Linux WebJobs GA 2025-05-01. Triggered = single instance; continuous = all instances unless singleton. Always On is needed for continuous/cron jobs, and an app without it unloads after 20 minutes idle.
- Durable: Durable Task Scheduler is the "recommended storage provider" (Dedicated SKU GA Nov 2025, Consumption SKU GA Mar 2026). Standalone **Durable Task SDKs run on App Service** (self-hosted model) → a Durable ban can't be justified by "requires Functions".
- Service Bus Functions trigger: auto complete/abandon, lock renewal (maxAutoRenewDuration default 5 min), sessions, target-based scaling, `ServiceBusMessageActions` for manual settlement; poison handling = Service Bus MaxDeliveryCount → DLQ (not configurable in Functions). Duplicate detection: Standard/Premium tiers only, MessageId-based, window 20 s–7 d (default 10 min); idempotent consumers are still required.

**Sources**: learn.microsoft.com/azure/architecture/best-practices/background-jobs; .../architecture-styles/web-queue-worker; .../patterns/leader-election; .../patterns/scheduler-agent-supervisor; azure-functions/functions-bindings-timer (2026-06-21); azure-functions/flex-consumption-plan (2026-09-08); azure-functions/functions-bindings-service-bus-trigger; azure-functions/functions-compare-logic-apps-ms-flow-webjobs (2026-03-23); app-service/webjobs-create; app-service/configure-common; container-apps/jobs (2026-09-04); durable-task/scheduler/durable-task-scheduler; durable-task/common/choose-orchestration-framework (2026-05-04); service-bus-messaging/duplicate-detection; azure-functions/opentelemetry-howto; app-service/app-service-key-vault-references.

**Open questions**: No MS page gives an explicit "in-process BackgroundService on App Service is fine when..." rule; that position is inferred from the Partitioning criteria. I haven't verified whether ACA Jobs' scheduled trigger guarantees no overlap when a run exceeds its interval (the page says "job-level concurrency settings" without detail).

Related: [[functions-flex-consumption-net10-2026-08-13]], [[dotnet10-appservice-linux-2026-08-10]]
