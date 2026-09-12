# Workload placement policy — BFF vs Azure Functions vs Container Apps Jobs (evaluation + owner decisions)

> **Date**: 2026-09-12 · **Status**: owner-approved direction; ADR drafting is task **102**
> **Trigger**: task 100 review found `.claude/constraints/bff-extensions.md` §D ("`IJobHandler<T>`, not a free-form
> `IHostedService`") contradicting ADR-036 (`IScheduledJob`). Tracing it exposed a four-era disagreement about
> Azure Functions and a real duplicate-execution defect in the in-process scheduler.
> **Decided here**: the policy direction. **Not decided here**: ADR wording (task 102, Fable-reviewed), the
> scheduler lease design (task 103).

---

## 1. Owner decisions (2026-09-12, verbatim intent)

| # | Question | Decision |
|---|---|---|
| D1 | Should Functions be restricted? | **No prohibition.** *"We do not want to introduce unnecessary complexity; if Azure Functions help streamline and/or improve performance/flexibility then a function can be the right solution."* The original caution dates from project start and was meant to avoid architectural complexity — it must not forbid a best-practice fit. |
| D2 | Function identity | **Reuse the stamp's BFF identity by default**, consistent with Model 1 / Model 2. |
| D3 | Multi-tenant Functions | **Follow Model 1 / Model 2**: a shared (multi-tenant) Function is acceptable for **Model 1**; Model 2 is per customer. |
| D4 | Tie-breaker | **Fewer moving parts** when neither side is clearly stronger. |
| D5 | Where the policy lives | **A new ADR** — and **all documentation aligned so there is no drift**. |
| D6 | Scope | Update **all** documentation; **new task** for the governance work; **registration helper** (aligned with the Functions decision); ArchTest: fix **both** the message and the scan defect. |
| D7 | Task 100 escalation (2026-09-11) | Recipient chain granter → record owner → record creator (enabled, non-application user), else unroutable; channel = MDA bell (`NotificationService`). |

---

## 2. The policy (direction the ADR must encode)

**Principle.** No default host and no ban. Each workload is placed where it fits best, judged against Microsoft's
criteria and Spaarke's own costs, and the choice is recorded in the Placement Justification.

**Two separate questions — never merged** (the historical error was answering the host question from the trigger):

1. **Where does it run?** — host (the new ADR).
2. **How does it run inside the BFF?** — mechanism by trigger: queue/event → Service Bus + `IJobHandler` (ADR-004);
   schedule → `IScheduledJob` (ADR-036); one-shot startup / long-lived listener → plain `IHostedService`. No new
   hand-rolled timer `BackgroundService`.

**Fit signals for Functions** (Microsoft Architecture Center "Background jobs", 2026-03-30; "Web-Queue-Worker",
2026-01-06; "Choose a compute service", 2026-02-18):

| # | Signal |
|---|---|
| F1 | Event intake that should not depend on the BFF's availability/deploys (webhooks, Event Grid, blob, change feed) |
| F2 | Independent scaling — bursty/heavy work that should scale on queue depth, not API load |
| F3 | Must run **exactly once per schedule** across a scaled-out app (timer trigger = singleton via storage lease) |
| F4 | Isolation — failure, security boundary, or release cadence |
| F5 | Multi-step durable orchestration (Durable Task on Durable Task Scheduler) |

**Stay-in-BFF signals:** B1 needs the user's identity (OBO / user-written SPE files — `bff-extensions.md` Pattern 4);
B2 uses a large share of BFF domain code (a Function cannot reference the web assembly — extraction to shared
libraries first); B3 low volume, same scale signal / identity / release cadence; B4 tied to a live request
(streaming, chat, sub-500 ms).

**Spaarke-specific costs of a Function** (why placement is a decision, not a default):
1. **Identity per customer** — Dataverse application user per environment; SPE grant on each customer's
   container-type **registration** (grants keyed by appId, per consuming tenant —
   `docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md` §3A); Key Vault + AI Search RBAC. **Avoided** by D2:
   reuse the stamp's BFF app identity (same UAMI + federated credential → same appId → existing grants).
2. **Code sharing** — BFF-coupled work needs domain logic extracted to shared libraries; independent work does not.
3. **A deployable per stamp** — provisioning handlers H2a (Bicep) / H7 (app settings) / H13 (acceptance), CI/CD,
   deploy procedure, test harness. One-time when the first Function ships.
4. **Money/limits** — Flex Consumption ≈ $15–25/mo per stamp with 1 always-ready instance (knowledge/azure-functions-isv);
   Model 2 floor ≤ $400/mo; Flex: Linux only, no deployment slots, UTC-only timers, regional availability.

**Rules:** never user-facing BFF API endpoints or user-token work in Functions · Functions = .NET isolated on Flex
Consumption, Bicep per stamp following Model 1 (shared OK) / Model 2 (per customer), managed identity only (reuse
the stamp's BFF identity by default; dedicated only when isolation is the reason), Key Vault references,
OpenTelemetry/W3C into the shared App Insights, Service Bus as the event buffer, idempotent handlers + DLQ +
alerts, shared code from shared libraries (never copied) · Durable Task allowed on Durable Task Scheduler for
multi-step orchestration; hand-rolled Service Bus + state machine needs a written reason · no new WebJobs ·
Container Apps Jobs for heavy/long custom-runtime work · tie-breaker = fewer moving parts.

---

## 3. Applied to today's workloads

| Workload | Signals | Verdict |
|---|---|---|
| Grant-expiry reminders (task 100), playbook scheduler, membership reconciliation | B2, B3; once-per-schedule | **BFF + scheduler lease (task 103)** |
| Dataverse → AI Search / Insights sync (Function shell already in `infra/insights/modules/function-app.bicep`) | F1, F2 | **Functions** |
| Document processing / indexing / embedding backfills | F2, F4 | **Future candidates** (with the job-contract cleanup) |
| Incoming webhooks (Graph mail, ACS Event Grid, SPE doc-changed) | F1 | **Candidates** — move when BFF deploys cause missed events |
| Polling / subscription-renewal timers | once-per-schedule, B2 | **BFF + lease**, migrate over time |
| Provisioning control plane L2 (multi-step, manual gates) | F5 | **Durable Task candidate** at next rework |
| Chat, streaming, AI synthesis | B1, B4 | **BFF** |

---

## 4. Evidence (condensed from three read-only reports, 2026-09-12)

### 4.1 The directives disagree — four eras
1. **≤ 2026-04-05 "no Functions"** — still stated by: ArchTest message (`tests/Spaarke.ArchTests/ADR001_MinimalApiTests.cs:38`),
   `docs/adr/README-ADRs.md:15,85`, `ADR-VALIDATION-PROCESS.md:19`, `.claude/adr/INDEX.md:25`,
   `docs/architecture/{background-workers-architecture,jobs-architecture,sdap-bff-api-patterns,sdap-overview,resilience-architecture}.md`,
   patterns `api/background-workers.md:18`, `api/endpoint-definition.md:17`, `auth/graph-webhooks.md:14`,
   `testing/integration-tests.md:17`, skills `mcp-tool-handler`, `project-setup/references/claudemd-template.md:148`
   (seeds every new project), `docs/procedures/testing-and-code-quality.md:366,1293`, ~12 project CLAUDE.md files.
2. **2026-05-19 ADR-001 narrowed** (`84cec9f92`) — Functions permitted for out-of-band integration incl. timer
   indexers; "default to BackgroundService when close". Reached api/ai/jobs constraints, standards, adr-check /
   code-review / adr-aware, PR template. Never reached the ones in (1).
3. **2026-05-20 `bff-extensions.md` + ADR-013** (`ddaaa6744`) — overstated to "event-driven (timer, queue, webhook)
   → Functions" (§E L76, table L342; ADR-013 concise L89) and §D "`IJobHandler<T>`" — **a type that does not exist**
   (real: non-generic `Services/Jobs/IJobHandler.cs:7`; also cited by `code-review/SKILL.md:684`,
   `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md:261`).
4. **2026-06-21/23 ADR-036** (`1e8c95b8e`, PR #415) — scheduled = in-process `IScheduledJob`, restating ADR-001 as
   "no Azure Functions" (concise L135; `scheduled-jobs.md:24`; `spaarke-scheduling-architecture.md:245,273`;
   `sprk_backgroundjob*.md`). Never reconciled with §D/§E; code-review / design-to-spec / task-create still check
   only ADR-004.

**Code reality**: zero Azure Functions (one unused Flex shell for Insights; the ingest consumer shipped as
`InsightsIngestJobHandler`); **14 hand-rolled timer `BackgroundService`s, 5 added after ADR-036**; 3 `IScheduledJob`s.
The ArchTest checks only the BFF assembly (correct scope, stale message) and scans **class-level** attributes
only — Function attributes sit on methods, so its second test can never fire.

### 4.2 Microsoft best practice (researcher; Learn / Architecture Center, Mar–Sep 2026)
- Per-workload partitioning (independent scaling, security boundary, release cadence, failure isolation vs cost);
  Web-Queue-Worker reference = App Service + queue + **Functions worker**.
- Scheduled work on a scaled-out app: in-process timers on each instance "start multiple copies"; recommended =
  Functions timer (singleton via blob lease; `UseMonitor`, `IsPastDue`; no retry on failure) or Container Apps
  scheduled job, or self-hosted Leader Election / distributed lock + persisted history for missed-run alerts.
- Queue consumers: in-process `ServiceBusProcessor` acceptable at modest, coupled volume; Service Bus trigger for
  independent scaling. All consumers idempotent; duplicate detection = Standard/Premium, MessageId only.
- Durable Task Scheduler GA (Dedicated Nov 2025, Consumption Mar 2026); standalone Durable Task SDK runs on App
  Service; hand-rolled Scheduler Agent Supervisor "can be difficult to implement" → a blanket Durable ban is not
  best practice.
- WebJobs: not recommended for new workloads.
- Full report + URLs: `.claude/agent-memory/researcher/background-work-hosting-best-practice-2026-09-12.md`.

### 4.3 Runtime + Service Bus facts
- `ScheduledJobHost`: no lease/leader election; `HasRunForScheduledTimeAsync` is process-local
  (`InMemoryBackgroundJobStore.cs:104-127`); no `DataverseBackgroundJobStore` exists although the Dataverse tables are
  deployed; admin disable flips one instance's dictionary and is re-seeded `Enabled:true` on restart. Every instance
  **and the staging slot** runs it. `docs/guides/BACKGROUND-JOBS-ADMIN-GUIDE.md` claims Dataverse-backed behaviour
  the code does not have.
- Instances: `model2-full` autoscales **min 2** (`stacks/model2-full.bicep:300-322`); `model1-shared` and
  `customer.bicep` default to 1 (+ a staging slot); dev BFF is out-of-band (unknown).
- Service Bus: `ServiceBusJobProcessor` conforms to ADR-004; `CommunicationJobProcessor` mostly; the three Office
  workers + `MembershipJunctionUpdaterHost` use bespoke shapes; `IndexingWorkerHostedService` **completes messages
  on failure**; `office-profile` has no sender; BFF queues have **duplicate detection off** (`service-bus.bicep:39`)
  while a guide claims it prevents races; `MembershipEventPublisher` sets no MessageId; BFF vs L2 provisioning
  MessageId hashes have drifted.

---

## 4.4 Fable-tier review of the ADR-052 drafts (2026-09-12) — corrections to this note

The review confirmed D1–D7 and found the v1 drafts not ready; v2 drafts in `notes/drafts/` fix every finding.
Two corrections apply to THIS note:

- **Identity reuse (D2) — mechanism, and what it does not inherit.** §2 above said reuse gives "same appId →
  existing grants". That conflated two identities. The stamp's **managed identity** holds the Dataverse application
  user and the Key Vault / AI Search / Service Bus assignments; the BFF **app registration** — which the managed
  identity can act as through its federated credential (ADR-028 A4) — holds the SharePoint Embedded
  container-type registration grants and can perform OBO. ADR-052 v2 therefore says: a Function reuses the
  **managed identity, app-only**, and MUST NOT act as the BFF app registration (no confidential client, no OBO, no
  user tokens, no calls to BFF endpoints). SPE access for a Function is an explicit extra grant. This narrows D2
  for safety without changing its intent — **flagged to the owner**.
- **Timer triggers can retry.** §4.2's "no retry on failure" is imprecise: `[FixedDelayRetry]` /
  `[ExponentialBackoffRetry]` are supported on timer triggers.

## 5. Now vs over time

| Now (tasks 102 / 103 / 100) | Over time (tracked GitHub issues, filed by task 102) |
|---|---|
| New placement ADR + ADR-001/004/013/036 amendments + all docs aligned + drift guard + ArchTest message & scan fix (102) | Migrate the 14 hand-rolled timers |
| Scheduler lease, staging-slot guard, `AddScheduledJob<TJob>` helper (103) | Office workers + membership topic → job contract |
| Task 100 on the helper + review fixes (100) | `IndexingWorkerHostedService` completes on failure (data loss) |
| | `office-profile` queue has no sender |
| | Enable duplicate detection on BFF queues (Bicep) |
| | `MembershipEventPublisher` sets no MessageId |
| | BFF ↔ L2 provisioning MessageId hash drift |
| | `DataverseBackgroundJobStore` (durable history + disable) |
