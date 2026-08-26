# Design Study DS-3 — API / Worker Split for `Sprk.Provisioning.ControlPlane`

> **Produced**: 2026-08-18 by design-study sub-agent. Research + design only — no source edits, no `.claude/**` writes.
> **Question**: Should the L2 control plane stay ONE process (REST API + session-serialized dispatcher + reconciler + crash recovery + Exchange-sidecar client on one App Service), or split into `.Api` + `.Worker` deployables — and if split, on which compute, and NOW or LATER?
> **Builds on locked Wave A decisions**: DS-1b (Option D hybrid — 12/13 handlers pure .NET, ~200–230 MB pwsh+EXO sidecar for H14a only), DS-2/DS-2b (session-serialized dispatch, `SessionId = CustomerId`, `MaxConcurrentCallsPerSession = 1`), DS-8 (Path X — L2 UAMI as Dataverse App User, no client secret).
> **Owner framing (binding)**: best practice for scale + long-term maintainability + operational integrity, not the easy approach. Online research performed; URLs inline and in Sources.
> **Answer up front**: **Split NOW — Option 2 (two App Services, shared P1v3 plan, one `.Core` class library), with the DS-1b EXO sidecar attached to the Worker app.** The dispatcher (C1.1) does not exist yet; splitting before it is written costs ~2–4 days. The one-process topology has a structural defect — the always-on staging slot is a *shadow worker* running old/new code against production Cosmos + Service Bus — that would otherwise need permanent config-flag mitigations of exactly the silent-drift class this project exists to kill.

---

## 1. Current-state deployment topology

### 1.1 What is in the process today (grep-verified)

`src/server/services/Sprk.Provisioning.ControlPlane/Program.cs` (1,178 lines) composes ONE ASP.NET Core Minimal API host containing four distinct workload classes:

| Workload | Components | Evidence |
|---|---|---|
| **REST API** | `MapRunsEndpoints` (8 endpoints: POST /api/runs, preflight, get, gate-advance, resume, cancel, clear-quarantine) + `MapRunLogsEndpoints` + `MapHealthEndpoints`; JWT bearer (`AuthModule`), `AuditLogMiddleware`, Swagger | `Program.cs:1130–1169` |
| **Background services** | `StateReconcilerService : BackgroundService` (5 s Cosmos poll → DAG advance → enqueue) + `CrashRecoveryStartupService : IHostedService` (one-shot Cosmos scan at boot → re-enqueue orphans) | `ReconcilerModule.cs:82`, `Program.cs:1065` |
| **Handler fleet** | 19 `IProvisioningHandler` implementations (H0–H14) + ~29 collaborator seams (currently shell-outs; post-DS-1b, SDK/REST) | `Program.cs:115–943`, DS-1b §1 |
| **Shared infrastructure** | `CosmosModule` (UAMI CosmosClient), `ServiceBusModule` (`IHandlerEnqueuer`, deterministic MessageId, SessionId=CustomerId), `TelemetryModule` (OTel → Azure Monitor), `CustomerRunGuard`, `RollbackModule` | `Program.cs:83–105, 986–1098` |

**Not yet present**: the Service Bus **dispatcher** itself (the `ServiceBusSessionProcessor` drain loop that receives envelopes and invokes handlers) — DS-2's C1.1 component. Today handlers resolve via DI "for unit tests + the temporary H0-enqueues-H0.5 bridge" (`Program.cs:110–114`). **This is the pivotal timing fact for §7: the highest-coupling component is not yet written.**

Also not yet present: any L2 deploy script (grep `scripts/` for `Sprk.Provisioning.ControlPlane` finds only catalog-generator references) — Wave-D deploy tooling is owed under every option.

### 1.2 What the platform gives it

`infrastructure/bicep/platform-controlplane.bicep` + `modules/controlplane-app-service.bicep`: one Linux App Service, `DOTNETCORE|10.0` (code-based, no custom image), **P1v3 plan** ("Basic/Standard disallowed — alwaysOn semantics + slot support + memory ceiling matter", `platform-controlplane.bicep:92`), `alwaysOn: true`, `healthCheckPath: /healthz`, UAMI-only identity on **both** slots, and a **staging slot with NO slot-sticky settings** — "App settings inherit from prod slot at swap time by default" (`controlplane-app-service.bicep:184–188`).

App Service lifecycle facts that matter here:

- **Always On** keeps the site warm (no idle unload) but does NOT prevent platform-initiated restarts (OS patching, plan rebalancing, auto-heal). Any in-flight 30–60 min handler can be killed at any time; this is why crash recovery (I6) + SB redelivery + L3 dedup exist, and that machinery is required under EVERY topology in this study.
- **Scale-out** clones the whole process: N instances = N reconcilers + N crash-recovery boot scans + N dispatchers + N API servers. There is no per-workload scaling inside one site.
- **Slot swap** exchanges hostname routing between two *live, running* processes. It does not stop either one.
- **Startup ordering inside the host**: `IHostedService.StartAsync` runs before Kestrel accepts traffic — `CrashRecoveryStartupService`'s Cosmos scan sits on the API's cold-start path, and `/healthz` (the swap warm-up gate) waits behind it.

### 1.3 Where the one-process model breaks as Wave-A components land

1. **The always-on staging slot is a shadow worker (the disqualifying defect).** The staging slot inherits ALL app settings — Cosmos endpoint, SB connection, `AZURE_CLIENT_ID` — and has `alwaysOn: true`. The moment bits are deployed to staging (the whole point of the slot), that process boots and: (a) `CrashRecoveryStartupService` scans **production** Cosmos and re-enqueues "stale" runs; (b) `StateReconcilerService` polls production Cosmos every 5 s and enqueues ready handlers; (c) once C1.1 exists, its `ServiceBusSessionProcessor` **acquires session locks and executes real handlers — with the not-yet-swapped build — against production customers**. And *after* a swap, the old-code process (now sitting in the staging slot) keeps draining sessions until someone stops it. Session locks make the two processes not-corrupt each other (broker-side exclusivity, §4), and MessageId dedup absorbs duplicate enqueues — but "the pre-production build silently executes production provisioning work" is exactly the T-trap silent-fail class (§4B) r1 exists to eliminate. For a pure API, slots are precisely right; for a competing consumer, slots are structurally wrong.
2. **Long-running session processor vs deploy/restart cadence.** Every API-only change (a new response field, an auth policy tweak) restarts the process and aborts whatever 30–60 min handler body is mid-flight (H2a Bicep 15–30 min, H6 solution import 30–60 min, H9 15–30 min per DS-2b §1.2). The machinery survives it — SB abandon → redelivery → L3 no-op scan → resume — but each such restart converts one deploy into §4C churn plus a re-run of a long handler. Coupling the API's deploy cadence (high, cheap) to the worker's (low, expensive-to-interrupt) taxes both.
3. **Reconciler + crash recovery multiply with API scale.** Scaling the API to N instances (or just having the staging slot warm) runs N+1 concurrent 5 s cross-partition Cosmos scans and N+1 boot scans — harmless for correctness (reconciler is deliberately Cosmos-write-free, `Program.cs:972–975`; MessageId dedup collapses duplicate enqueues), but pure waste, and each extra scanner widens the surface reasoning "who can enqueue" audits must cover.
4. **Boot coupling.** Crash-recovery's Cosmos scan delays `/healthz`, which is both the slot-swap warm-up gate and the operator's availability probe. A slow or failing boot scan (Cosmos throttling during an incident — precisely when an operator needs the API) takes the *API* down with it.
5. **Sidecar blast radius.** DS-1b's EXO sidecar shares the site's network namespace ("sidecars share the same network namespace and environment as your main app — only run trusted code" — [Configure sidecars](https://learn.microsoft.com/en-us/azure/app-service/configure-sidecar)). In a one-process topology, the container holding Exchange-admin capability rides on the same site as the **internet-facing** API. Splitting moves it to a site with no public traffic contract.
6. **What does NOT break (honesty).** API-vs-worker memory/GC interference is a weak argument here: the API is an internal operator surface at ~zero RPS (see `notes/l2-load-test-2026-08-18.md` scale), envelopes are small, and post-DS-1b the handlers are await-heavy SDK calls, not CPU hogs. P1v3 (2 vCPU / 8 GB) holds both comfortably. The split is justified by **lifecycle, deploy semantics, and security shape — not performance**.

---

## 2. One-process failure modes — summary table

| Failure mode | Severity | Mitigable in-process? |
|---|---|---|
| Staging slot = live old/new-code worker against prod state (pre- AND post-swap) | **High — silent-fail class** | Only via permanent slot-sticky `Dispatcher:Enabled` / `Reconciler:Enabled` / `CrashRecovery:Enabled` flags — three config values whose drift re-opens the hole silently; the flags also make slot-swap trigger a full app restart (sticky settings differ across slots), forfeiting warm-swap. This is the "fix drift at discovery" anti-pattern as a standing design. |
| API deploy aborts in-flight 30–60 min handlers | Medium | No (same process). Absorbed by crash machinery at §4C-churn cost. |
| Worker boot scan delays/blocks API `/healthz` | Medium | Partially (timeout on the scan) — but the coupling itself remains. |
| N-instance scale-out multiplies scanners/dispatchers | Low (correctness holds via session locks + dedup) | N/A — it's waste, not breakage. |
| API p99 vs worker GC | Low at this RPS | N/A. |
| Startup ordering API↔worker | Non-issue by design — the queue + Cosmos decouple them; API can 202-enqueue with zero workers alive; worker never calls the API. | — |

---

## 3. Split-model options

### Option 1 — One process (status quo)

Ship as-is; add C1.1 into the same host. **Requires** the three slot-sticky Enabled flags plus an operator runbook step "verify staging slot is drained/disabled" — permanent operational surface protecting against a topology choice. Cheapest by ~2–4 days. Carries every §2 row forever.

### Option 2 — `.Api` + `.Worker`, both App Service, shared plan (RECOMMENDED)

**Projects** (namespaces unchanged; folder moves only):

| Project | Contains | References |
|---|---|---|
| `Sprk.Provisioning.ControlPlane.Core` (new class lib) | `Handlers/**`, `Reconciler/**` (types, not host registration), `Repositories/`, `Enqueue/`, `Models/`, `Rollback/`, `Concurrency/`, `Registry/`, `Modules/{Cosmos,ServiceBus,Telemetry}` + options types | Azure SDKs (Cosmos, SB, Identity, OTel) |
| `Sprk.Provisioning.ControlPlane.Api` (existing project, thinned) | `Api/`, `Endpoints/`, `Middleware/`, `Modules/{Auth,Swagger}`, Program.cs (~150 lines) | `.Core` |
| `Sprk.Provisioning.ControlPlane.Worker` (new; `WebApplication` minimal host with ONLY `/healthz` + `/ping`, no auth surface) | C1.1 dispatcher hosted service, `StateReconcilerService`, `CrashRecoveryStartupService`, `ExchangePolicySidecarClient` | `.Core` |

The existing csproj has **zero ProjectReferences** (grep-verified) — Core extraction is folder moves + two thin hosts, no dependency untangling.

- **Shared**: CosmosClient/SB client factories, options, DI extension methods — all in `.Core`; each host calls the same `AddCosmosModule`/`AddServiceBusModule`. One `spaarke-provisioning` Cosmos + one SB queue, unchanged.
- **Plan strategy**: both sites on the existing P1v3 plan → **$0 marginal Azure cost** (a plan hosts multiple apps). Separate plans only if/when worker sizing diverges — a one-line Bicep change later, per the Architecture Center's "separate plans so they scale independently" guidance ([Web-Queue-Worker](https://learn.microsoft.com/en-us/azure/architecture/guide/architecture-styles/web-queue-worker)).
- **Slots**: `.Api` KEEPS staging slot + warm swap (it's stateless — slots are exactly right). `.Worker` gets **NO slot**: deploy = stop → zip-deploy → start. Stopping first is the *honest* drain story — no platform gives a 60-min graceful drain; the crash machinery that exists anyway (I6 + SB redelivery + L3 dedup + §4C) is the resume path, now exercised on a *chosen* schedule instead of on every API deploy. Optional refinement: a `POST /admin/drain` on the worker that calls `StopProcessingAsync` and lets the current handler finish before the operator deploys.
- **Auth**: split enables **least-privilege UAMIs** — `.Api` UAMI: Cosmos data contributor + SB **Sender** only. `.Worker` UAMI: the full grant set (ARM, Graph, KV, AI Search, SB Receiver) + the DS-8 Dataverse App User (registry writes are H13/handler/guard territory — all worker-side; no API endpoint touches Dataverse). The internet-facing app structurally *cannot* reach ARM/Graph/KV/Exchange. `Grant-ControlPlaneIdentity.ps1` (DS-8 §4) grows a second principal parameter. (Shipping v1 on the shared UAMI is acceptable interim; the two-UAMI shape is the target and is cheap in Bicep.)
- **Sidecar**: DS-1b's EXO sitecontainer attaches to the **Worker** site (sitecontainers work for code-based Linux apps — the sidecar feature "applies to both single-container and multi-container applications"; localhost networking per [Configure sidecars](https://learn.microsoft.com/en-us/azure/app-service/configure-sidecar); GA Nov 2024, [deep dive](https://azure.github.io/AppService/2025/03/06/Sidecars-Deep-Dive-Part1.html)). This *improves* DS-1b: the EXO-capable container no longer shares a network namespace with the public API.
- **Observability**: same App Insights workspace; distinct `cloud_RoleName` (`controlplane-api` / `controlplane-worker`); W3C trace context flows API → SB message (`Diagnostic-Id` application property, emitted automatically by `Azure.Messaging.ServiceBus` under OTel) → worker span → Cosmos run doc; `RunId` remains the domain correlation anchor in every envelope + log line, so `runs/{id}/logs` (A1) is topology-agnostic.
- **Testing**: 41-file unit test project re-points its ProjectReference to `.Core` — type-level tests (Handlers/, Reconciler/, Rollback/, Concurrency/) unchanged; the single `WebApplicationFactory` consumer (`Api/RunsEndpointsTests.cs`) follows `.Api`. Load/nightly integration tests already treat HTTP + SB + Cosmos as the boundary and don't care how many processes sit behind it.

### Option 3 — `.Api` App Service + `.Worker` Container App (multi-container with EXO sidecar)

Same project split; worker runs as an ACA app (not Job) with the EXO container as a pod sidecar, KEDA `azure-servicebus` scale rule. Genuine advantages: native multi-container pods; revision-based deploys; scale-to-min semantics. Rejected for r1 because: (a) it requires a **custom image for the main worker**, reopening the image-supply-chain/patching loop DS-1b §4 explicitly celebrated deleting ("Main: stock DOTNETCORE|10.0 — no custom image at all"); (b) design.md B2 already rejected Container Apps for this system and DS-1b reaffirmed it for the sidecar ("reopens B2's Container-Apps rejection"); (c) a new ACA managed environment is new ops surface in a fleet that is 100% App Service (BFF parity was an explicit B2 value); (d) KEDA's `azure-servicebus` scaler counts *messages*, not *sessions* — workable but a worse fit than the session-native SDK loop on stable instances (DS-2b §7 cited [target-based scaling on session count](https://learn.microsoft.com/en-us/azure/azure-functions/functions-target-based-scaling) as the Functions-only refinement); (e) ACA's graceful-termination window cannot cover 30–60 min handlers anyway, so its headline deploy advantage over "stop-then-deploy App Service" is small here. ACA is the right *flip target* (§9), not the right first move.

### Option 4 — Others considered

- **`.Worker` as Container Apps Jobs** (event-driven, one execution per message): wrong shape for session-serialized dispatch — [jobs](https://learn.microsoft.com/en-us/azure/container-apps/jobs) run "a single message or a small batch" per execution with `replicaTimeout` bounds; per-customer FIFO across executions would need session-lock gymnastics the long-lived `SessionProcessor` gives for free. Rejected.
- **WebJobs inside the API site**: same process-lifecycle and slot problems as Option 1 with a second-class deployment story on Linux. Rejected.
- **Azure Functions worker**: re-platforms the handler fleet onto a different hosting model mid-project for no capability gain (the 30–60 min handler bodies exceed comfortable Functions execution envelopes outside Durable/dedicated plans). Rejected.

---

## 4. Multi-instance dispatcher story (research)

The locked DS-2 session model is **natively multi-instance**; the split does not perturb it:

- **Broker-side exclusivity**: "When the client accepts and holds a session, it holds an exclusive lock on all messages with that session's session ID … Those receivers can also live on different client machines, since the lock management happens service-side, inside Service Bus." — [Message sessions](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sessions). Two worker instances each running a `ServiceBusSessionProcessor` CANNOT both execute the same customer's handlers; the broker partitions sessions across them with zero coordination code. N workers = horizontal scale-out with per-customer serialization intact.
- **Per-instance ceilings**: [`MaxConcurrentSessions`](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebussessionprocessoroptions.maxconcurrentsessions) (SDK default 8) is per-processor/per-instance; `MaxConcurrentCallsPerSession = 1` stays the correctness invariant (DS-2b R3 forcing-function test). The SDK guidance warns of thread starvation only "with a very high value for MaxConcurrentSessions relative to the number of cores" ([session processor sample/troubleshooting](https://github.com/Azure/azure-sdk-for-net/blob/Azure.Messaging.ServiceBus_7.16.2/sdk/servicebus/Azure.Messaging.ServiceBus/samples/Sample05_SessionProcessor.md), [Functions SB troubleshooting](https://techcommunity.microsoft.com/blog/appsonazureblog/how-to-troubleshoot-azure-functions-service-bus-trigger-issues/4514006)) — irrelevant at 8–16 sessions of await-dominated handlers on 2 vCPU. `PrefetchCount = 0` (already the DS-2 posture) is correct for multi-minute handlers: prefetched messages of a locked session would sit lock-expiring in a client cache.
- **`SessionIdleTimeout` ≈ 30 s** releases a session between handler completions and during gates, so instance slots track *actively executing* customers — the reason 24 h SPE gates hold no capacity (DS-2b §1.4).
- **Scale trigger for a second worker instance**: sustained `MaxConcurrentSessions`-saturation (active-customer count > per-instance ceiling) — a metric alert on SB active-session/queue depth, manual scale-out (or schedule-based); no KEDA needed on App Service at this fleet size.

The split *simplifies* this story: worker instance count becomes a pure throughput dial (sessions × instances, DS-2b §7) that never drags API replicas — and vice versa.

---

## 5. Container Apps vs App Service for the worker (research)

| Dimension | App Service worker (Option 2) | Container App worker (Option 3) |
|---|---|---|
| Artifact | Zip of framework-dependent publish; **no image** | Custom image + ACR + Trivy loop |
| Cold start | None (Always On, code-based) | Image pull on new revision/node; scale-from-min delay |
| Scaling | Manual/autoscale rules on instance count; broker distributes sessions | KEDA on queue depth (message-count proxy for sessions); scale-to-zero possible — but a 5 s-poll reconciler wants ≥1 always-on replica anyway, forfeiting the serverless economics that motivate ACA ([Q&A guidance](https://learn.microsoft.com/en-us/answers/questions/2261273/choosing-between-azure-container-apps-and-app-serv)) |
| Cost | $0 marginal on the existing P1v3 plan | New managed environment + consumption/dedicated compute — small in dollars, non-zero in ops surface |
| Sidecar | Sitecontainers (GA), localhost network, ARM `Microsoft.Web/sites/sitecontainers` ([docs](https://learn.microsoft.com/en-us/azure/app-service/configure-sidecar)) | Native pod sidecars — cleaner, incl. guaranteed sidecar-ready-before-main ordering ([jobs networking note](https://learn.microsoft.com/en-us/azure/container-apps/jobs)) |
| Fleet fit | Parity with BFF + every Spaarke service; existing runbooks/skills | First ACA resource in the estate |

Microsoft's selection guidance ([Choose an Azure container service](https://learn.microsoft.com/en-us/azure/architecture/guide/choose-azure-container-service), [containerisation strategy](https://techcommunity.microsoft.com/blog/appsonazureblog/choosing-the-right-azure-containerisation-strategy-aks-app-service-or-container-/4456645)) steers *bursty, scale-to-zero, event-driven* workers to ACA/KEDA and steady always-on services to App Service. This worker is the latter: an always-on reconciler + a session processor with single-digit concurrent customers, whose burst ceiling is set by third-party throttles (ARM/Dataverse/Graph — DS-2b §7), not compute.

## 6. The Exchange sidecar under each topology

- **Option 2**: DS-1b §3 transfers verbatim — sitecontainer on the **Worker** site, shared network namespace, `POST http://localhost:8091/apply-policy`, per-boot shared-secret header, same UAMI (now the *worker's* UAMI) fetching the EXO cert from KV at call time. Volume mounts are available between sitecontainers but unnecessary (HTTP request/response, no file drop); note the docs' one caveat — the *built-in* code container can't mount custom volumes, which doesn't affect the localhost-HTTP contract. Net security improvement over DS-1b's original placement: no public-ingress process shares the sidecar's network namespace, and the API's UAMI can't read the EXO cert.
- **Option 3**: pod-native sidecar, marginally cleaner ordering guarantees — but it's the tail wagging the dog: adopting a new compute platform to improve the plumbing of one 231-line script's container.
- **Option 1**: sidecar rides the combined public site — the worst of the three shapes.

Best practice for THIS sidecar (single caller, request/response, secret-holding): same-pod/same-site localhost HTTP, non-routable, co-located with the only consumer — i.e., Options 2/3 equally satisfy it; Option 2 does so with zero new platforms.

---

## 7. Decide-now vs decide-later cost analysis

**Split later (after E2E validation on one process):**

- Rework: extract `.Core` + move hosted-service registrations out of a **validated** composition → the E2E acceptance evidence (Phase F harness) describes a topology no longer deployed; re-run it. The interim slot-flag mitigations (§2 row 1) are built, documented in the operator runbook, then thrown away. Wave-D deploy tooling gets written for one topology and rewritten for two. C1.1 gets designed with in-process assumptions (shared lifetime with Kestrel, shared /healthz) that must be unpicked.
- Tests: same csproj re-point either way — later means doing it while the suite is larger.
- **The hidden cost**: during the exact window where E2E evidence is being collected, the staging-slot shadow-worker risk is live. Validating on a topology you intend to discard validates the wrong thing.

**Split now:**

- Delivery cost: **~2–4 days** — `.Core` extraction (folder moves, zero dependency untangling per §3), thin Worker host, one more `controlplane-app-service`-derived Bicep module (minus slot, minus JWT surface), CI publish step ×2, test csproj re-point. No deploy script breaks (none exist yet); no dispatcher rework (not yet written).
- Second-order simplifications unlocked: API keeps warm slot-swap with no Enabled-flag ceremony; worker deploys on its own cadence with an honest stop-drain-deploy story; per-side UAMIs (§3) make least-privilege structural; C1.1 is designed host-native from day one; `/healthz` means one thing per site.

The asymmetry is decisive: the split is at its cheapest **right now** (pre-C1.1, pre-deploy-tooling, pre-E2E) and only gets more expensive every wave it waits.

---

## 8. Best-practice framing

- **Microsoft Architecture Center — Web-Queue-Worker** is this system's literal shape and prescribes the split: "Consider placing the web app and [worker] in separate App Service plans so that they can scale independently… Use deployment slots… swap over" (slots discussed for the *web* tier) — [architecture style](https://learn.microsoft.com/en-us/azure/architecture/guide/architecture-styles/web-queue-worker). Its listed challenge — "the front end and worker can grow into large monolithic components" — is Program.cs at 1,178 lines already.
- **Mature .NET messaging stacks** (NServiceBus, MassTransit — the frameworks DS-2b §8 benchmarked) all ship endpoint-host-per-role as the default production topology; NServiceBus documents web-queue-worker on Azure with dedicated worker endpoints ([Particular docs](https://docs.particular.net/architecture/azure/web-queue-worker)). The .NET platform's own `Worker Service` template + `dotnet-aspire` samples ([example](https://github.com/kimtth/dotnet-aspire-worker-queue-cache)) treat API and queue-worker as separate projects wired to shared infrastructure.
- **2025–2026 shifts found**: (a) App Service **sidecars GA** (Nov 2024) with first-party CI templates through 2025 ([GHA samples](https://azure.github.io/AppService/2025/09/08/GHA-templates-sidecars.html)) — this is what makes Option 2's sidecar story clean without ACA; (b) continued Microsoft push of ACA for *bursty* background work — which this worker is not; (c) nothing that weakens the web/worker separation guidance — the Architecture Center page was refreshed 2026-01 with the recommendation intact.
- In-repo precedent: the BFF already runs its job processor in-process (`ServiceBusJobProcessor`) — and DS-1/gap-analysis flag its execution-environment ceiling as the thing that breaks first at fleet scale (DS-2b §7). L2 is the chance to lay the correct shape down before the load exists, rather than inheriting the BFF's coupling.

---

## 9. Recommendation

**Option 2, NOW.** Split into `Sprk.Provisioning.ControlPlane.Core` (class lib) + `.Api` (existing site, keeps staging slot + swap, UAMI scoped to Cosmos + SB-send) + `.Worker` (new slotless App Service on the SAME P1v3 plan, full-grant UAMI + DS-8 Dataverse App User, DS-1b EXO sitecontainer attached here, hosts C1.1 dispatcher + reconciler + crash recovery). Sequence it as the first task of the C1.1 dispatcher wave, so the dispatcher is born into its correct host and is never written twice. Cost: ~2–4 days, $0 marginal Azure spend.

This is the best-practice answer, not merely the safe one: it is the canonical Web-Queue-Worker shape for a system that is textbook web-queue-worker; it deletes (rather than mitigates) the staging-slot shadow-worker silent-fail class; it makes least-privilege identity structural on the internet-facing surface; it decouples the high-cadence API deploy loop from 60-minute handler bodies; and it moves the Exchange-admin-capable sidecar off the public site. Every alternative either preserves a standing silent-fail risk behind config flags (Option 1) or buys marginal sidecar elegance with a new platform and a resurrected image-supply-chain (Option 3).

**The ONE flip condition** (to Option 3, worker → Container App): if Option-D execution later forces a **custom container image for the main worker anyway** — e.g., the D-2 bounded fallback (running H3/H6/H2a scripts in the sidecar) becomes semi-permanent, or bundling bicep/solution-ZIP payloads outgrows code-deploy — then the "no custom image" advantage that anchors Option 2 evaporates, and the worker (plus its sidecars) should move to a Container App pod, with KEDA on queue depth. The `.Api`/`.Core`/`.Worker` project shape is identical under that flip; only the worker's Bicep changes — which is itself an argument for making the project split now.

**Not** a flip condition: E2E schedule pressure. At 2–4 days against the C1.1 wave, one-process-now would trade a permanent silent-fail mitigation burden for less than a week — the wrong side of the owner's directive.

---

## Sources

- [Web-Queue-Worker architecture style — Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/guide/architecture-styles/web-queue-worker) (separate plans / independent scaling / slots guidance)
- [Message sessions — Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sessions) (service-side session-lock exclusivity across machines)
- [ServiceBusSessionProcessorOptions.MaxConcurrentSessions — .NET API](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebussessionprocessoroptions.maxconcurrentsessions)
- [Sample05 SessionProcessor — Azure SDK for .NET](https://github.com/Azure/azure-sdk-for-net/blob/Azure.Messaging.ServiceBus_7.16.2/sdk/servicebus/Azure.Messaging.ServiceBus/samples/Sample05_SessionProcessor.md)
- [How to troubleshoot Azure Functions Service Bus trigger issues — Microsoft Community Hub](https://techcommunity.microsoft.com/blog/appsonazureblog/how-to-troubleshoot-azure-functions-service-bus-trigger-issues/4514006) (session processor scale-out + concurrency ceilings)
- [Target-based scaling in Azure Functions (session count)](https://learn.microsoft.com/en-us/azure/azure-functions/functions-target-based-scaling)
- [Configure sidecars — Azure App Service](https://learn.microsoft.com/en-us/azure/app-service/configure-sidecar) (shared network namespace, localhost ports, sitecontainers ARM, volume mounts, code-based caveat)
- [Sidecars in Azure App Service: a deep dive](https://azure.github.io/AppService/2025/03/06/Sidecars-Deep-Dive-Part1.html) · [Sidecar extensions](https://azure.github.io/AppService/2025/03/19/Sidecar-extensions.html) · [GHA samples for sidecars incl. code-based apps](https://azure.github.io/AppService/2025/09/08/GHA-templates-sidecars.html)
- [Jobs in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/jobs) (app-vs-job selection table; continuous SB processing = App, not Job; replicaTimeout; sidecar-ready ordering)
- [Choose an Azure container service — Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/guide/choose-azure-container-service)
- [Choosing between Container Apps and App Service for background tasks — Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/2261273/choosing-between-azure-container-apps-and-app-serv)
- [Choosing the right Azure containerisation strategy — Microsoft Community Hub](https://techcommunity.microsoft.com/blog/appsonazureblog/choosing-the-right-azure-containerisation-strategy-aks-app-service-or-container-/4456645)
- [Web-queue-worker on Azure — NServiceBus / Particular docs](https://docs.particular.net/architecture/azure/web-queue-worker)

*Design study only. Grounded in: `Program.cs` (workload census), `controlplane-app-service.bicep` (slot/app-settings inheritance, Always On), `platform-controlplane.bicep` (P1v3 rationale), csproj (zero ProjectReferences), test-project layout (41 files, one WebApplicationFactory consumer), DS-1b/DS-2/DS-2b/DS-8, plus the live fetches cited. No source, config, Azure state, or `.claude/**` files modified.*
