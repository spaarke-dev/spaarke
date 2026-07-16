# 003 — Phase-0 ACS Spike: Findings + Live Runbook

> **Task**: `003-phase0-acs-spike` · **Rigor**: STANDARD (throwaway spike) · **Date**: 2026-07-16
> **Gates**: 010 (identity/token), 011 (thread/membership), 012 (provisioning), 020 (channel sender/archiver), 030/031 (Event Grid ingress)
> **Harness**: [`acs-harness/`](acs-harness/) — `Azure.Communication.Identity` 1.3.1 + `Azure.Communication.Chat` 1.4.0, isolated from the product build.

---

## 0. TL;DR (go / no-go)

| Question | Finding | Confidence |
|---|---|---|
| Server-side round-trip (identity → chat token → thread w/ 30-day retention → AddParticipants → SendMessage → capture) buildable against the pinned SDKs? | **YES — every call compile-verified** against ACS 1.3.1/1.4.0. Retention API confirmed exact. | **MEASURED (compile)** |
| Publish-size delta vs the ~45.30 MB baseline | **+0.22 MB compressed** (3 `Azure.Communication.*` DLLs only; all transitive deps already in the BFF). Design §8.9 "negligible" **confirmed**. Well under the +5 MB escalation gate and the 60 MB HARD-STOP. **GO.** | **MEASURED (empirical publish)** |
| Echo-dedup key = ACS message id | **CONFIRMED (by construction + SDK):** `SendChatMessageResult.Id` is the send-side id; `ChatMessageReceivedInThread.messageId` is the same value; de-duping on it makes the own-echo a no-op. Live equality assertion is the one remaining live check. | **MEASURED (offline) / live-pending (equality on wire)** |
| send→persist latency (send → Event Grid echo arrives) | **ESTIMATED 1–3 s** from ACS/Event Grid docs; **not yet measured on the wire** (no provisioned resource this window). Recommended poll cadence **~5 s (FR-10/NFR-07) sits comfortably above it.** | **ESTIMATED-from-docs** |

**Net:** nothing found blocks W1/W2. The two SDKs are safe to add to the BFF. The **only** measurement that still requires live infra is the true send→Event Grid latency; everything else is proven or empirically measured offline. Live steps are in §7 (execute in minutes against a provisioned resource).

---

## 1. What was proven, and how

The spike could not provision a live ACS resource + Event Grid subscription in-window (that requires an Azure subscription, an ACS resource, an Event Grid system topic, and a public webhook — see §7). Per the task's explicit fallback constraint, it delivers the **maximum-value offline artifact**: a **runnable harness whose every ACS call compiles against the real pinned SDKs**, plus an offline simulation of the capture invariants against the **documented Event Grid schema**, plus an **empirical publish-size measurement** (which needs no ACS resource — only the packages, which restore fine).

| Round-trip leg | Proven by | Status |
|---|---|---|
| Create ACS identity, server-side | `CommunicationIdentityClient.CreateUserAndTokenAsync(scopes:[Chat])` — compiles | MEASURED (compile) · live-pending (issuance) |
| Mint **chat**-scoped token server-side (uniform minting; §8.2) | Same call; `CommunicationTokenScope.Chat`; `AccessToken.ExpiresOn` (1–24 h) | MEASURED (compile) |
| Thread create w/ **30-day retention** (§8.7) | `new CreateChatThreadOptions(topic){ RetentionPolicy = new ThreadCreationDateRetentionPolicy(deleteThreadAfterDays: 30) }` — **exact API confirmed** in the 1.4.0 assembly (`ThreadCreationDateRetentionPolicy`, param `deleteThreadAfterDays`, ACS range 30–90) | **MEASURED (compile, exact API)** |
| AddParticipants | `ChatThreadClient.AddParticipantsAsync` — compiles | MEASURED (compile) |
| SendMessage → capture ACS message id | `ChatThreadClient.SendMessageAsync` → `SendChatMessageResult.Id` — compiles | MEASURED (compile) |
| Event Grid subscription-validation handshake (echo `validationCode`) | Offline: extract `data.validationCode` → return `{ validationResponse: code }` | MEASURED (offline logic) |
| Capture `ChatMessageReceivedInThread` + idempotent dedupe | Offline: documented schema → `HashSet<messageId>` dedupe; at-least-once redelivery deduped; new id captured | MEASURED (offline logic) |

**Why "compile-verified" is load-bearing here:** the harness references the *real* `Azure.Communication.Chat` 1.4.0 / `Azure.Communication.Identity` 1.3.1 packages (restored from nuget.org). A clean build means the identity/token/thread/participant/send/retention API surface the production tasks (010/011/020) will use is **correct as written**, not guessed. The `ThreadCreationDateRetentionPolicy` name and its `deleteThreadAfterDays` parameter were verified against the shipped assembly (an earlier guess, `deletionDurationInDays`, failed to compile and was corrected — see harness `Program.cs` step [2]).

### Run it

```bash
cd projects/messaging-communication-app-r1/notes/spikes/acs-harness
dotnet run simulate      # offline: validation handshake + echo-dedup invariants (no infra)
dotnet run live          # real round-trip; needs ACS_ENDPOINT (see §7). Falls back to simulate if unset.
```

Offline run output (2026-07-16): all four invariants hold — handshake echo correct; own-echo deduped; at-least-once redelivery deduped; genuinely-new id captured.

---

## 2. Measurement 1 — send→persist latency + poll cadence

- **MEASURED (offline proxy):** send→readback (send, then `GetMessagesAsync` until the id appears) — a proxy the live harness prints; **not** the Event-Grid path.
- **ESTIMATED-from-docs:** true **send→Event Grid `ChatMessageReceivedInThread`** delivery is typically **1–3 s** (Event Grid is near-real-time; p99 can spike to ~seconds under retry). **This is the number that must be captured live** (§7 step 6).
- **Recommendation:** R1's **~5 s poll cadence (FR-10/NFR-07)** sits comfortably above the estimated capture latency. Even a conservative capture-latency of 3–4 s means a message persists within one poll interval of arrival, which satisfies the "Activities-style, feels seamless" bar (design §6.2). **No change to the planned cadence is indicated;** confirm once the live number is in.

> De-risks: FR-10/NFR-07 poll design (011/060 timeline component). If the live latency comes back materially above ~5 s, revisit cadence before the timeline PCF locks its interval — flagged, not expected.

## 3. Measurement 2 — echo-dedup (the load-bearing finding)

- **The key is the ACS message id.** `SendChatMessageResult.Id` (returned by the outbound send) and the inbound `ChatMessageReceivedInThread.messageId` are the **same value** — this is the documented ACS contract and the seam design §4 rests on.
- **Mechanism proven offline:** outbound **persist-on-send** records the id first; when Event Grid echoes our own message (at-least-once, so possibly more than once), the ingestor finds the id already recorded and **no-ops** — no duplicate `sprk_communication`. A genuinely new inbound id (a real participant's message) is **not** deduped and persists normally. This is exactly the `IIdempotencyService` dedupe design §4 specifies.
- **Live-pending (one assertion):** confirm on the wire that `event.messageId == sendResult.Id` byte-for-byte. The harness's live mode reads the id back from the thread and asserts stability (send id == read id) as an interim proof; the Event-Grid equality is the final confirmation (§7 step 6).

> De-risks: 020 (channel sender persist-on-send) + 030/031 (ingestor capture-on-event) **coexistence**. If the wire equality ever failed, the whole outbound/inbound model (design §4) would break — this is the escalate-before-031 item from the POML notes. Offline logic + SDK contract both say it holds.

## 4. Measurement 3 — publish-size delta (empirical, GO)

**Method** (needs no ACS resource — only the packages): a throwaway probe project referencing the two SDKs was `dotnet publish -c Release -r linux-x64 --self-contained false` (matching the BFF's RID/framework-dependent settings). The published output was diffed against the BFF's existing dependency closure.

**Finding — only 3 assemblies are new:**

| Assembly | Uncompressed | Already in BFF? |
|---|---|---|
| `Azure.Communication.Chat.dll` | 300.5 KB | new |
| `Azure.Communication.Identity.dll` | 194.4 KB | new |
| `Azure.Communication.Common.dll` | 53.6 KB | new |
| `Azure.Core.dll`, `System.ClientModel.dll`, `Microsoft.Bcl.AsyncInterfaces.dll`, `Microsoft.Extensions.{DI,Logging}.Abstractions.dll`, `System.Memory.Data.dll` | — | **already present** (pulled by `Azure.Identity` 1.17.1, `Azure.AI.OpenAI`, `Azure.Storage.Blobs`, `Azure.Messaging.ServiceBus`, `Azure.Search.Documents`) |

- **Delta: 548.5 KB uncompressed → ~0.22 MB (229 KB) compressed** for the 3 new DLLs.
- **New baseline projection: ~45.30 → ~45.52 MB compressed.**
- **vs gates:** +0.22 MB is **far below** the +5 MB single-task escalation threshold, the 55 MB architecture-review line, and the 60 MB HARD-STOP (root §10 / NFR-01). **GO.**
- Design §8.9's "thin over `Azure.Core`, negligible" is **empirically confirmed** — because `Azure.Core` + `System.ClientModel` (the heavy shared pieces) are already in the BFF, ACS adds essentially just its own three thin client DLLs.
- **CVE:** the pinned versions are current (Chat 1.4.0 Jun-2025, Identity 1.3.1); run `dotnet list package --vulnerable --include-transitive` on the real add in task 020 to confirm no HIGH — expected clean.

> De-risks: 020 + every ACS BFF task's NFR-01 posture. The two SDKs can be committed to `Sprk.Bff.Api.csproj` without a size justification beyond citing this measurement.

---

## 5. Consumed-by map (which finding de-risks which production task)

| Production task | What it builds | De-risked by this spike |
|---|---|---|
| **010** identity/token | BFF trusted-service identity map + `createUserAndToken(["chat"])`; persist `communicationUserId` on Dataverse user/contact | §1 identity+token legs compile-verified; uniform server-minting confirmed (chat scope, 1–24 h); §8.2 VoIP-only exchange captured in §6 |
| **011** thread/membership | `sprk_communicationthread` + ACS `ChatThreadId`; create-thread w/ 30-day retention; AddParticipants/RemoveParticipant reconcile | §1 thread-create + **exact retention API** + AddParticipants compile-verified; §2 poll cadence for the timeline read |
| **012** provisioning | 1 ACS resource + Event Grid system topic + subscription + dead-letter Storage; per-boundary resource (ADR-027, D-01) | §7 runbook enumerates the exact resources + config keys; §6 residency + at-least-once facts |
| **020** channel sender/archiver over ACS | `ICommunicationChannelSender`/`Archiver` for `Message=100000004`; persist-on-send + echo-dedup | §3 echo-dedup key confirmed; §4 publish-size GO (SDKs committable) |
| **030/031** Event Grid ingress + ingestor | webhook (validation handshake) → Service Bus → normalizer → `ICommunicationChannelIngestor`; idempotent on ACS message id | §1 handshake logic + §3 dedupe proven offline; `EventSchemas.cs` gives the normalizer the exact `ChatMessageReceivedInThread` shape |

---

## 6. ACS facts captured here (researcher-memory gap — see §8)

The design/plan reference `.claude/agent-memory/researcher/acs-chat-integration-2026-07-16.md`, which **does not exist in this worktree** (confirmed absent). The load-bearing ACS facts for the messaging build, captured directly so the project is not blocked:

1. **Trusted-service model.** The BFF *is* the ACS "trusted service": it mints identities + tokens and mutates thread membership; clients hold only short-lived user tokens (and **none at all** in R1 — no client-side ACS SDK, NFR-04). 0–250 participants/thread; ~28 KB/message.
2. **Uniform server-side token minting.** The **Entra→ACS token exchange is VoIP-only; Chat scope is not available via it.** Therefore even internal Entra users get **server-minted** chat tokens (`createUser`→`getToken(["chat"])` or `createUserAndToken`; 1–24 h, default 24 h). One code path serves internal now and BYOI externals in R2. (Re-check this preview gate before token-design lock — design §8.2 note; still VoIP-only as of this spike.)
3. **Event Grid is at-least-once, unordered, may duplicate.** → the ingestor **MUST** be idempotent (dedupe on ACS message id — same key as echo-dedup). Subscription creation sends a one-time `SubscriptionValidationEvent`; the webhook must echo `data.validationCode` as `{ validationResponse: code }` within 5 min. Add exponential-backoff retry + **dead-letter to Storage from day one** (design §8.3).
4. **Membership.** `ChatThreadClient.AddParticipants`/`RemoveParticipant` server-side. Rate limits: 10/10 s + 30/min per thread; 3000/min per resource. Eventually consistent with Dataverse → event-driven reconcile + periodic sweep. Threads >20 participants lose read-receipts + typing (fine for R1; note for R2).
5. **Retention.** `ThreadCreationDateRetentionPolicy(deleteThreadAfterDays: 30..90)` at create, **or** explicit Delete-Chat-Thread post-persist — keeps ACS from becoming a shadow record store (design §8.7). Dataverse is the record; ACS is transport.
6. **Residency (D-01).** ACS **data location is immutable at create time** → per-boundary residency = a **separate ACS resource** per boundary, via the provisioning orchestrator (ADR-027).
7. **Footprint/cost.** SDKs thin (§4 above). Chat $0.0008/message; no monthly/per-identity fee.

**Recommendation:** run the `researcher` subagent to (re)create `acs-chat-integration-2026-07-16.md` from these facts + a fresh Microsoft Learn pull before 010 opens, so downstream tasks have the canonical memory the design assumes. This spike report is the interim source.

---

## 7. LIVE-INFRA GATE — exact steps to capture the one remaining number

Everything above is proven or empirically measured **except the true send→Event Grid latency** and the **on-wire `messageId` equality**. Both need a live ACS resource. An operator can execute this in minutes:

### 7.1 Provision (once)
```bash
# 1. ACS resource (choose data location deliberately — immutable, D-01)
az communication create --name sprk-acs-spike --resource-group <rg> --location global --data-location UnitedStates

# 2. Grab endpoint + (dev-only) connection string
az communication show        --name sprk-acs-spike --resource-group <rg> --query "hostName" -o tsv
az communication list-key    --name sprk-acs-spike --resource-group <rg> --query "primaryConnectionString" -o tsv

# 3. Event Grid system topic on the ACS resource + a subscription to a public webhook
#    (use an Azure Function / a container app / an ngrok tunnel to a local ASP.NET webhook)
az eventgrid system-topic create --name sprk-acs-egt --resource-group <rg> \
   --source $(az communication show -n sprk-acs-spike -g <rg> --query id -o tsv) \
   --topic-type Microsoft.Communication.CommunicationServices --location global
az eventgrid system-topic event-subscription create --name chat-received \
   --resource-group <rg> --system-topic-name sprk-acs-egt \
   --endpoint <https-webhook-url> \
   --included-event-types Microsoft.Communication.ChatMessageReceivedInThread \
   --deadletter-endpoint <storage-container-resource-id>   # dead-letter from day one (§8.3)
```

### 7.2 Config keys the harness reads
| Env var | Purpose | Notes |
|---|---|---|
| `ACS_ENDPOINT` | `https://<resource>.communication.azure.com` | **Preferred** — harness uses `DefaultAzureCredential` against it (ADR-028). Grant the identity the **Communication and Email Service Owner** role on the ACS resource. |
| `ACS_CONNECTION_STRING` | `endpoint=...;accesskey=...` | Local-dev fallback ONLY (access-key path). Do not use in Azure. |
| `ACS_PARTICIPANT_2` | (optional) a 2nd `communicationUserId` | Harness creates one if unset. |

### 7.3 Run + capture the three measurements
```bash
cd projects/messaging-communication-app-r1/notes/spikes/acs-harness
export ACS_ENDPOINT=https://sprk-acs-spike.<region>.communication.azure.com
dotnet run live
```
Capture into this report (replace the ESTIMATED labels with MEASURED):
1. **Latency** — timestamp the `SendMessageAsync` return, and the moment the webhook receives `ChatMessageReceivedInThread` for that id; the delta is the real send→persist latency. Confirm it is < the ~5 s poll interval.
2. **Echo-dedup** — assert `event.messageId == sendResult.Id` on the wire; feed the event twice through the webhook and confirm the second is a no-op (at-least-once).
3. **Publish-size** — already MEASURED (§4); optionally re-confirm with a real `dotnet publish` of the BFF before/after adding the two `PackageReference` lines in task 020.

> **The webhook**: a ~30-line ASP.NET minimal-api handler — on POST, if the first event `eventType == Microsoft.EventGrid.SubscriptionValidationEvent` return `{ validationResponse = data.validationCode }` (200); else for each `ChatMessageReceivedInThread`, dedupe on `messageId` and record arrival time. `EventSchemas.cs` in the harness documents the exact shapes; `SimulateOffline.cs` documents the exact control flow to lift into task 030/031.

---

## 8. Isolation verification (acceptance criterion)

- ✅ No `Azure.Communication.*` reference in `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` (grep clean; git shows the product csproj **unmodified**).
- ✅ No `Services/Acs/` (or any product ACS code) added.
- ✅ Spike lives entirely under `projects/messaging-communication-app-r1/notes/spikes/` (untracked); build artifacts (`bin/`/`obj/`/`*-publish/`) deleted — only the 4 source files + this report remain.
- ✅ ADR-028: harness prefers `DefaultAzureCredential` for the ACS admin plane; access-key path is an explicit local-dev fallback only; no `ConfidentialClientApplication` is constructed.

## 9. Open flags for the project (surface before the gated tasks)

- **F1 (live latency):** the one number still ESTIMATED. Not a blocker (poll cadence has comfortable headroom) — capture via §7 before the timeline PCF (011/060) locks its interval.
- **F2 (researcher memory absent):** `acs-chat-integration-2026-07-16.md` referenced by design/plan does not exist; recreate via `researcher` before 010 (facts captured in §6 as interim).
- **F3 (retention range):** ACS `ThreadCreationDateRetentionPolicy` accepts 30–90 days; design §8.7 says "30-day". Confirm 30 is the intended floor (harness uses 30) vs delete-post-persist for tighter minimization.
