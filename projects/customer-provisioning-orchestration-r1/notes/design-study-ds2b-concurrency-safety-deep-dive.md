# Design Study DS-2b — Concurrency vs Safety Deep-Dive (session-serialized dispatch, adversarially re-examined)

> **Produced by**: design-study agent, 2026-08-18. Research + design only — no source edits, no `.claude/**` writes.
> **Input**: [`design-study-ds2-dispatcher-design.md`](./design-study-ds2-dispatcher-design.md) §2.3/§7.1; [`r1-gap-analysis-2026-08-18.md`](./r1-gap-analysis-2026-08-18.md); spec.md §4.2/§4D/FR-19-family; design.md §4.1 DAG + §4.2 (I5).
> **Owner framing (binding)**: best practice, not the easy approach. This study genuinely evaluates five alternatives to DS-2's session-serialized dispatch, grounds each in grep evidence + external research, and recommends on correctness-for-scale grounds.
> **Web research**: performed per owner addendum 2026-08-18; findings integrated in §2b/§3/§7/§8 with URLs. One finding **changes DS-2's designated fallback** (see §2b + §9).

---

## 0. The question, restated precisely

DS-2 §2.3 recommends `CreateSessionProcessor` with `SessionId = CustomerId`, `MaxConcurrentCallsPerSession = 1`. Effect: within one customer's run, handlers execute strictly one-at-a-time even where design.md §4.1 declares DAG-parallel branches. Cross-customer parallelism is preserved (`MaxConcurrentSessions`). The safety property purchased: the `ProvisioningRun` Cosmos document has **at most one handler-writer at a time**, so the existing 39 ETag-guarded `ReplaceRunAsync` call sites (grep §2.1 below) never race *each other*.

Two facts frame everything that follows:

1. **The write shape.** Every handler does: read run (+ETag) at dispatch start → run body 2–60 min → mutate the in-memory doc (append `CompletedPhases`, set `Status`/`CurrentPhase`/`ErrorDetail`/`GateStates`/`Quarantine`) → ONE `ReplaceRunAsync(run, etag)` at the end (e.g. `H4KvSecretsPopulationHandler.cs:740,780`). On `Conflict` the handler either logs-and-loses (failure path, `:741-747`) or converts its own *successful* execution into a `Resumable` **failure** (success path, `:781-792`: "Resume will re-run H4"). There is no retry-merge machinery anywhere in L2 (grep: zero retry loops around `ReplaceRunAsync`).
2. **The stale-ETag window is the whole handler runtime.** The ETag is captured before a 10–30+ min body. Under concurrent dispatch, a branch-sibling finishing anywhere inside that window makes conflict on the terminal write *near-certain*, not rare — every DAG join point becomes a systematic conflict generator, and the current Conflict posture converts completed work into §4C re-dispatch churn.

---

## 1. Actual wall-clock impact of serialization

### 1.1 The parallel structure being traded away (design.md §4.1, lines 155–170)

| # | Parallel set | Members | Notes |
|---|---|---|---|
| P1 | Post-H2a 3-way fan-out | H2b ∥ H4-chain ∥ H5-chain | The headline branch |
| P2 | Post-H3 2-way | H8 ∥ H9 | Inside the H4-chain |
| P3 | Two long chains | (H4→H3→{H8,H9}) ∥ (H5→H6→H7→H10→H11→…) | The *structural* parallelism — two multi-handler chains side by side |
| P4 | Post-H11 2-way | H12a ∥ H12b | design.md:163, spec FR-16 "DAG-parallel" |
| P5 | H14 sub-steps (a)(b)(c) | in-process within the H14 parent | **Unaffected by dispatch serialization** — H14a/b/c are orchestrated inside one handler invocation (`H14IntegrationWiringHandler.cs:45` "ONE ReplaceRunAsync call"), never enqueued (DS-2 §3.2) |

### 1.2 Per-handler wall-clock estimates (grounded in collaborator types, gap analysis §B.2)

Estimates, not measurements — no live E2E run exists yet (gap analysis §B.4). Basis: what each collaborator shells out to.

| Handler | Dominant work | Est. active wall-clock |
|---|---|---|
| H0 | 4× pwsh preflight probes (quota reads) | 2–4 min |
| H1 | ARM reachability probe | 1–2 min |
| H2a | 15-resource Bicep incl. OpenAI + AI Search + App Service (`Provision-Customer.ps1`) | **15–30 min** |
| H2b | 7 index PUTs + REST verify | 3–5 min |
| H4 | az CLI secret writes + T1 `keyVaultReferenceIdentity` PATCH ×2 slots | 3–5 min |
| H5 | `pac admin create` + reachability poll | **10–20 min** |
| H3 | app-reg + 14 grants | 3–5 min → then **manual admin-consent gate** (WaitingOnGate) |
| H8 | container-type create + KV write | ~5 min → then **24 h SPE replication gate** (WaitingOnGate) |
| H9 | dotnet build + publish + deploy + slot swap (`Deploy-BffApi.ps1`) | **15–30 min** (DS-2: "H9 BFF build is the long pole") |
| H6 | 8 managed solutions via Package Deployer, dependency-ordered | **30–60 min** |
| H7 / H10 / H12c | Dataverse Web API writes | 2–3 min each |
| H11 | Graph user creation (+ B2B consent gate for that preset) | 3–10 min |
| H12a / H12b | seed-manifest pwsh runs | ~10 min each |
| H14 | 3 sub-steps in-process-parallel | ~5 min |
| H13 | extended validate + 6 traps + 5 invariants + naming + cost | 10–15 min |

Corroboration: `ServiceBusModuleOptions.DefaultTimeToLive` = 30 min (`ServiceBusModule.cs:189`, "matches spec §4.2 30-min handler tolerance"); DS-2 sizes `MaxHandlerDuration` at 65 min for the H9 pole.

### 1.3 Serialized vs parallel per-customer wall-clock

- **Serialized (sessions)**: active compute ≈ Σ all handlers ≈ **~2.5–3.5 h** (mid ≈ 3 h).
- **Parallel (DAG honored)**: critical path = H0+H1+H2a (~30 min) + Dataverse chain (H5 15 + H6 45 + H7 2 + H10 3 + H11 5 + max(H12a,H12b) 10 + H12c 2 + H14 5) ≈ 87 min + H13 12 ≈ **~2.0–2.2 h**. The KV chain (H4+H3+max(H8,H9) ≈ 35 min) hides entirely inside the Dataverse chain.
- **Delta ≈ 45–70 min per customer** (~40% inflation of *active compute*).

Second-order serialization effect (worse than naive sums suggest): a short ready handler queues behind a long-running session occupant — e.g. H9 (25 min) enqueued while H6 (45 min) holds the session waits the full 45 min. Bounded above by Σ-of-actives; already included in the ~3 h figure.

### 1.4 Real end-to-end wall-clock — the number that matters

E2E to `Ready` includes, in BOTH models:

- **H8's 24 h SPE replication gate** (spec FR-11; H13 verifies T6, so `Ready` cannot precede it),
- **H3's manual admin-consent gate** (human latency: minutes to days),
- H11's B2B consent gate (preset-dependent).

Critically, **gated runs hold no session**: a handler returning `WaitingOnGate` completes its message; `SessionIdleTimeout` (30 s) releases the session. Gates are session-free waits.

| Model | Active compute | E2E to Ready (fresh Model-2 provision) |
|---|---|---|
| Parallel | ~2.1 h | **~26–27 h** (24 h gate + consent latency + compute) |
| Serialized | ~3 h | **~27–28 h** |

**Delta ≈ 1 h on a ~27 h process ≈ 3–4% of E2E.**

### 1.5 When does the delta become operationally material?

Serialization is a **per-customer latency** cost, not a fleet-throughput cost. Throughput = (sessions × instances × 60 min) / ~180 min-active-per-customer, and `MaxConcurrentSessions` scales linearly. There is **no customers-per-hour at which serialization becomes the throughput bottleneck** — it never caps throughput, only stretches each customer's compute window. It becomes material only if **time-to-Ready acquires an SLA under ~3 h**, which is arithmetically impossible while the 24 h SPE gate exists. That is the flip condition (§9).

---

## 2. ETag-retry (DS-2's rejected alternative), costed honestly

### 2.1 Append-site census (grep-verified)

`ReplaceRunAsync` call sites: **39 across 18 handler files** (H0 2, H1 2, H2a 2, H2b 2, H3 3, H4 2, H5 2, H6 2, H7 2, H8 2, H9 2, H10 2, H11 3, H12a 2, H12b 2, H12c 2, H13 2, H14 3) + `QuarantineClearService.cs:112` + `StateReconcilerService.cs:430` = **41 total ETag-guarded write sites**. `CompletedPhases.Add` appears in 18 handlers (H14 3× for sub-phases); every one is paired with the read-mutate-replace shape of §0.

### 2.2 What the retrofit actually is

Not "wrap 39 sites in a retry loop" — retrying a `Replace` with the *same stale doc* re-loses every concurrent write. The correct retrofit is a single **read-merge-write mutator**:

```csharp
Task<MutateResult> MutateRunAsync(string customerId, string runId,
    Action<ProvisioningRun> mutation, int maxAttempts = 5, CancellationToken ct);
// re-read fresh doc+ETag → apply mutation closure → Replace(ifMatch) → on Conflict, loop.
```

Each handler's terminal-write block (success + failure paths) becomes a closure over a *fresh* doc rather than the doc it has held for 30 minutes. **Effort**: mutator ~100 LOC + restructure ~20–40 LOC × 18 handlers + endpoint/QuarantineClearService alignment ≈ **500–800 LOC net**, touching every handler's most safety-critical block.

### 2.3 Idempotency + semantic wrinkles

1. **Closure-level dedup re-check**: the L3 `CompletedPhases.Any(...)` scan currently runs at handler entry; under concurrency it must re-run *inside the closure* against the fresh doc, or duplicate delivery + retry double-appends.
2. **Per-field merge semantics**: `Status`, `CurrentPhase`, `ErrorDetail` are scalars (`ProvisioningRun.cs:78,87,152`) — two branch handlers finishing near-simultaneously produce last-write-wins on all three. Mostly benign (observational), but every closure must guard terminal states (never resurrect `Cancelled`/`Quarantined` — today that check exists only at dispatch step 4, once, at entry). `GateStates` dictionary writes (e.g. `H8SpeContainerTypeHandler.cs:495,538`) merge safely only if keys never collide across handlers — true today, unenforced.
3. **Conflict-loop poison**: bounded retries, then what? Abandon → SB redelivery → the *entire 30-min handler* re-runs (L3 makes it a no-op, but the churn is real). A hot loop against a pathologically busy doc is a new liveness failure mode that doesn't exist under sessions.
4. **Test surface**: concurrent-append fuzz per join pair (P1/P2/P4), mutator property tests, per-closure merge tests ≈ **15–25 new tests** plus review of 18 restructured terminal blocks.

### 2.4 Is the pattern established in Spaarke?

No. Grep of the BFF finds ETag machinery (`EtagPreconditionFailedException`, `WorkspaceLayoutEndpoints.cs:402-414`, `MemoryItemStore.cs:127`) whose uniform posture is **surface 412/409 to the caller and let the client retry** — the same posture L2's `ReplaceRunResult.Conflict` was designed for (`IProvisioningRunRepository.cs:19-23`: "so the endpoint layer can return HTTP 409"). **Zero internal retry-merge loops exist anywhere in the codebase.** Adopting one for L2 introduces a novel pattern in the highest-blast-radius write path.

### 2b. Web-research correction — conditional Patch beats ETag-retry (changes DS-2's fallback)

Microsoft's [partial document update (Patch API)](https://learn.microsoft.com/en-us/azure/cosmos-db/partial-document-update) supports **array append** (`add` with path `/completedPhases/-`), up to 10 atomic ops per patch, and **conditional update** ("a SQL-like filter predicate … such that the operation fails if the precondition isn't satisfied"). That composes into a server-side **atomic check-and-append**:

```
Patch(runId, pk: customerId,
  ops: [ add /completedPhases/-  {phase},  set /currentPhase "H4",  set /status "Running" ],
  condition: "FROM c WHERE NOT ARRAY_CONTAINS(c.completedPhases, {'phase':'H4'}, true)")
```

No ETag, no read-merge-write loop, no lost-update class for appends; concurrent patches to distinct paths do not clobber each other (the doc's path-level conflict-resolution section demonstrates the semantics; single-region single-master patches are applied serially server-side — [Microsoft Q&A confirms](https://learn.microsoft.com/en-us/answers/questions/1608281/how-does-azure-cosmos-db-handle-concurrency-when-p) the ETag/412 machinery applies only when `IfMatchEtag` is supplied). Costs: rewrites the repository contract (today Replace-only, `CosmosProvisioningRunRepository.cs:149`), abandons the typed full-doc write model, per-field patch semantics must be reasoned exactly as §2.3.2, and scalar fields remain LWW.

**Consequence**: if L2 ever goes concurrent, the right mechanism is **conditional-patch append, not ETag-retry loops**. DS-2 §7.1 framed the alternative as "ETag-retry in every handler" — that was the *weaker* alternative. This does not change the §9 recommendation (the wall-clock math and the shared-scalar problem stand), but it changes what the flip path should be, and DS-2's sign-off item 1 should be amended accordingly.

---

## 3. Cosmos `TransactionalBatch`

`TransactionalBatch` is **atomic multi-operation from a single caller** within one partition ([database transactions doc](https://learn.microsoft.com/en-us/azure/cosmos-db/database-transactions-optimistic-concurrency)). The contention here is multiple independent *processes* writing one document at *different times* — there is no caller who naturally holds several handlers' appends to batch, because "handlers finish at roughly the same time" is an accident, not a rendezvous.

- **Coordination overhead**: someone must buffer appends and flush — an accumulator service. That accumulator *is* a serializer (it reinvents the session), and unless it is itself durable, a crash between handler-complete and flush **loses completed phases** — strictly worse than any alternative.
- **Where batch legitimately fits**: as a building block for §5 — atomically writing a per-phase document + patching a slim run doc in one batch (the docs explicitly support multi-document patch inside a batch).

**Verdict**: not a standalone option for this write shape; noted as §5 machinery only.

---

## 4. Redis per-customer distributed lock (concurrent dispatch + lock at outcome-apply)

### 4.1 Wire semantics — coherent, not contradictory

This model uses a plain `ServiceBusProcessor` (no sessions); the enqueuer's `SessionId` stamp becomes inert metadata on a non-session queue. (A session processor with `MaxConcurrentCallsPerSession > 1` technically also delivers same-session messages concurrently, but then sessions buy nothing — the honest form of this option is sessionless.) Per-customer FIFO (I5's transport half) is **lost**; ordering falls entirely to the reconciler's ready-set computation.

### 4.2 Where it actually differs from session-serialization

**Lock scope.** Sessions serialize the whole 10–30 min handler body; a lock around the terminal write serializes only a seconds-long window — so branch parallelism survives. That is the option's one real advantage, and it's genuine.

### 4.3 Why it collapses under inspection

1. **The lock doesn't fix the stale read.** The handler's doc+ETag are 30 minutes old at write time. Locking the write of a stale doc still loses the sibling's append. Correctness requires **re-read + merge under the lock** — i.e., the *entire §2 restructure* — with the lock added on top as a retry-reducer.
2. **The lock cannot be the correctness mechanism.** The existing pattern to mirror (`IdempotencyService.cs`) is get-then-set (non-atomic, DS-2 §1.3) and **fail-open on Redis outage** (`:43,96`). A safety lock must be fail-closed atomic `SET NX PX` with **fencing tokens** — and the only fencing token available is… the Cosmos ETag/conditional predicate, meaning the datastore-level check is still mandatory and the lock is purely an optimization. This is the canonical distributed-locking result (Kleppmann's Redlock critique): a lease-based lock without fencing cannot guarantee mutual exclusion across pauses/expiry.
3. **Failure modes bought**: lease expiry mid-write (az-CLI hang, GC pause, App Service swap), Redis outage (fail-open = silent loss of the "guarantee"; fail-closed = provisioning hard-depends on cache availability, contradicting the existing posture), lock-TTL-vs-handler-duration coupling.

**Verdict**: strictly dominated. It requires everything option 2/2b requires *plus* a liveness sidecar that can never be load-bearing. Rejected on best-practice grounds, not effort grounds.

---

## 5. Per-phase append-only documents (event-sourced)

### 5.1 Does the r1 state model already commit to this shape? No.

Grep-verified: the shipped model is a **single mutable document** — `ProvisioningRun` embeds `List<CompletedPhase>` (`ProvisioningRun.cs`), the repository is read/create/replace-only, `DagAdvancer.cs:174` computes ready-sets from the embedded list, `CrashRecoveryStartupService.cs:352-366` takes MAX over it, H12c/H14 gating reads join-check it, and ~524 tests exercise that shape. Spec FR-27/FR-30 ("ProvisioningRun documents; ETag concurrency") codify it. This option is a **state-model rewrite**, not a refinement.

### 5.2 The design if built

Per-phase docs `{pk: customerId, id: "phase-{runId}-{phase}"}` + a slim run doc (Status, gates, quarantine, TTL). Handler completion = **create-if-not-exists** of its phase doc — 409 = duplicate, which is *stronger* L3 idempotency than the array scan, with zero contention (each handler owns its own document; the [Azure-Samples event-sourcing pattern](https://github.com/Azure-Samples/cosmos-db-design-patterns/blob/main/event-sourcing/README.md) and [change-feed design patterns](https://learn.microsoft.com/en-us/azure/cosmos-db/change-feed-design-patterns) are exactly this shape, partitioned by aggregate id). Optionally a `TransactionalBatch` pairs the phase-doc create with a slim-doc patch.

### 5.3 Honest accounting

- **The scalar problem survives**: `Status`/`Quarantine`/gates remain mutable shared state on the slim doc — event sourcing relocates the contention, it does not eliminate it; the slim doc still needs conditional writes or a single writer.
- **Reconciler read complexity**: point-read + one single-partition query per run per 5 s tick (vs one point-read). RU cost up modestly; `CosmosActiveRunScanner` and the C4.5 serialization fix all re-land on a new shape.
- **Storage**: negligible (~19 × ~1 KB per run).
- **Blast radius**: repository + reconciler + crash-recovery + 19 handler write paths + endpoints' status projection + test migration across ~524 tests.

**Verdict**: the architecturally purest concurrent-write answer and the correct *greenfield* shape for a high-write-rate ledger — but this workload writes **~19 events per ~27 hours per customer**. Adopting event sourcing here is machinery without a workload, against a shipped, spec-committed state model. Right pattern, wrong write rate, wrong moment. Revisit if r2's fleet-management app wants per-phase history/telemetry streams (change-feed projections) — that's the workload that would justify it.

---

## 6. Concurrent dispatch with strong handler invariants ("just fix the handlers")

This is option 2/2b as a *governance regime*: every handler MUST be idempotent AND concurrency-safe by contract.

- **Mechanically enforceable half (ArchTest)**: forbid direct `IProvisioningRunRepository.ReplaceRunAsync` references from the `Handlers` namespace; allow only the shared mutator (or patch gateway), reconciler, and QuarantineClearService. Same forcing-function family as the r3 I1–I5 ArchTests and the interface-shape enforcement already in `IProvisioningRunRepository.cs:7-15` — cheap, reliable, ~1 test.
- **Unenforceable half (review burden)**: whether each closure's per-field merge is *semantically* right (terminal-state guards, gate-key non-collision, dedup-in-closure) is invisible to ArchTests. That is a human review obligation on 18 handlers **and every future handler, forever** — precisely the "subtle: works but why" tax §8 scores against. The mature frameworks that run this model (NServiceBus, MassTransit — §8) pair it with recoverability retry policies because they *accept* conflict-churn as a steady-state cost.

**Verdict**: viable, industry-standard, and the correct *destination if the flip condition fires* — but as the day-one choice it spends a permanent review tax to buy ~1 h on a ~27 h process.

---

## 7. Future-scale analysis

| Load point | Session-serialized | Concurrent + invariants | What actually breaks first |
|---|---|---|---|
| **10 customers concurrently mid-provision** | Default `MaxConcurrentCustomers=4` queues 6; bump to 8–16 — one config value. Note: **gated runs (24 h SPE, consent) hold no session slot**, so "mid-provision" ≫ "concurrently executing" | Fine | Nothing — both models coast |
| **100 customers/day (~4/hr, ~13 avg mid-flight)** | Sessions ~16 on 1–2 instances; throughput scales linearly | Fine | **The execution environment (DS-1)**: pwsh/az/pac shell-out processes, App Service memory/process ceilings — plus external throttles (ARM deployment rate, Dataverse env-creation rate limits H0 probes exist for, Package Deployer) |
| **1000 customers/day (~42/hr, ~130 mid-flight)** | Session model itself still fine — SB sessions scale to unbounded session counts; the SDK holds only `MaxConcurrentSessions` AMQP links; `PrefetchCount=0` already correct for long handlers | Same external limits | Dispatcher host must become a container/worker fleet (DS-1 decision) **regardless of dispatch model**; ARM/Dataverse/Graph throttles dominate long before Cosmos or SB |
| **Multi-instance dispatcher fleet** | **Native.** N instances each run a session processor; the broker grants each *session lock* to exactly one instance; work distributes with zero coordination code. Azure Functions even [target-scales on session count](https://learn.microsoft.com/en-us/azure/azure-functions/functions-target-based-scaling); [`MaxConcurrentSessions`](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebussessionprocessoroptions.maxconcurrentsessions) is per-instance (SDK default 8) | Also native (competing consumers) but adds a shared Redis (option 4) or per-write conditional-patch RU load (option 2b) in the hot path | — |

**SessionProcessor's own ceilings**: per-instance session cache = `MaxConcurrentSessions` held locks + renewal churn (trivial at these counts); `SessionIdleTimeout=30 s` releases sessions between handlers so slots track *actively executing* customers only. No ceiling in this design's operating range is attributable to sessions.

**Key scale conclusion**: the serialization delta (~1 h/customer) is **invariant with fleet size** — it never compounds into a throughput problem. Every approach hits the same real walls first: DS-1's execution environment and third-party rate limits.

---

## 8. Best-practice comparison (senior distributed-systems framing)

| Option | Industry pattern | Ownership / consistency | Self-documenting? |
|---|---|---|---|
| **Session-serialized** | **Single-writer-per-aggregate / actor model** — Orleans grain-per-key, Kafka partition-per-key, SB sessions (this is the scenario [message sessions](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sessions) exist for); [Wolverine markets exactly this as "ordered messaging without the locks"](https://jasperfx.net/news/ordered-messaging-without-the-locks-wolverine-global-partitioning-and-re-sequencer) | Transport owns mutual exclusion; strong per-customer consistency; correctness is a *structural property* | **Yes** — `MaxConcurrentCallsPerSession = 1  // §4D I5: single-writer per customer` is the whole story |
| ETag-retry (2) | Optimistic concurrency control — the [NServiceBus](https://docs.particular.net/nservicebus/sagas/concurrency)/[MassTransit](https://masstransit-project.com/usage/sagas/guidance) saga-persistence default (one winner; losers roll back into recoverability retry) | Datastore owns detection; every call site owns resolution | No — 18 closures each carrying merge reasoning |
| Conditional patch (2b) | Server-side atomic ops / commutative-append (CRDT-adjacent) | Datastore owns append atomicity; scalars remain LWW | Partly — append is elegant; scalar semantics stay subtle |
| Transactional batch (3) | Atomic multi-doc commit | Wrong shape (single-caller) | n/a |
| Redis lock (4) | Distributed pessimistic lock | **Nobody fully owns it** — without fencing it degenerates to OCC anyway; Redis fail posture decides safety | **Worst** — the classic "works but why" trap |
| Event-sourced (5) | Event sourcing / CQRS ([Azure-Samples pattern](https://github.com/Azure-Samples/cosmos-db-design-patterns/blob/main/event-sourcing/README.md)) | Append-only store owns history; projections own reads | Yes at pattern level; heavy at codebase level |

The senior-reviewer principle in play: **the cheapest concurrency to maintain is the concurrency you eliminated structurally**. Mature systems converge on partition-per-key ordering when write rates are low and ordering has value; they reach for OCC-everywhere when per-key parallelism is a *requirement*, and they accept the recoverability churn as its price. Here per-key parallelism buys 4% of E2E.

**Did web research change the from-scratch analysis?** It (a) *upgraded the designated fallback* from ETag-retry to conditional-patch append (§2b — Cosmos Patch API's conditional predicate is a strictly stronger primitive than DS-2's rejected alternative assumed); (b) *confirmed* the session model's multi-instance scaling story is first-class (target-based scaling on sessions); (c) *confirmed* both the serialized and OCC models are mainstream in .NET messaging frameworks — neither is exotic. Net: recommendation unchanged, fallback changed.

---

## 9. Recommendation

**Adopt DS-2's session-serialized dispatch (`MaxConcurrentCallsPerSession = 1`, `SessionId = CustomerId`). This is the best-practice answer for this system, not merely the easy one.** The grounds, in order of weight:

1. **The parallelism being traded is worth ~45–70 min of a ~27 h E2E (~4%)**, because E2E is dominated by the 24 h SPE replication gate and human consent gates that no dispatch model touches — and gated runs hold no session, so serialization doesn't even extend the gates.
2. **Serialization is a latency policy with zero throughput cost at any scale** (§7): throughput scales with sessions × instances; the delta never compounds. The correct-for-scale walls are DS-1's execution environment and third-party throttles, identically in every option.
3. **Every alternative costs 500–800 LOC of restructure across all 18 handlers' most safety-critical blocks plus a permanent per-handler semantic-review tax** (§2/§4/§6), to make systematic branch-join conflicts *survivable* — while sessions make them *impossible*. Where the correct choice did cost more engineering (rejecting the Redis lock despite it preserving parallelism, §4; rejecting event sourcing despite architectural purity, §5), this study rejects on correctness/fit grounds, not effort.
4. **It is the self-documenting industry pattern for this exact shape** (single-writer-per-aggregate, §8).

**Three riders (amendments to DS-2, not reversals):**

- **R1 — Keep every handler's `Conflict` arm; do not treat sessions as total single-writer.** The run doc retains concurrent *non-handler* writers: cancel / gate-advance endpoints, `QuarantineClearService`, and the reconciler's outcome applier. Sessions eliminate the handler∥handler class; the handler∥operator class remains, and the existing log-or-`Resumable` posture is the right, now-rare backstop. This is the trade-off reviewers most commonly miss in "session = single writer" designs.
- **R2 — Amend DS-2 sign-off item 1's alternative**: the documented flip path is **concurrent dispatch + conditional-patch append (§2b)** — not ETag-retry loops. Record it in design.md §4.2; do not pre-build any abstraction for it (root CLAUDE.md §11 — no concrete failing behavior today).
- **R3 — One-line forcing function**: a unit test asserting the dispatcher's constructed `ServiceBusSessionProcessorOptions.MaxConcurrentCallsPerSession == 1` (it is a correctness invariant, not tuning — protect it from config drift), consistent with the repo's ArchTest culture.

**The one flip condition**: *time-to-Ready becomes a contractual SLA below ~3 hours* — which first requires the 24 h SPE replication gate to disappear (Microsoft-side change) and consent gates to be pre-satisfied. If that fires, move to concurrent dispatch with conditional-patch appends per §2b, keeping enqueuer, reconciler, DAG, and §4C untouched.

---

## Sources

- [Partial Document Update — Azure Cosmos DB](https://learn.microsoft.com/en-us/azure/cosmos-db/partial-document-update) (array append `/-`, 10-op atomic patch, conditional predicate, path-level conflict resolution)
- [Concurrency of simultaneous patch operations — Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/1608281/how-does-azure-cosmos-db-handle-concurrency-when-p)
- [Database transactions & optimistic concurrency control — Azure Cosmos DB](https://learn.microsoft.com/en-us/azure/cosmos-db/database-transactions-optimistic-concurrency)
- [ServiceBusSessionProcessorOptions.MaxConcurrentSessions](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebussessionprocessoroptions.maxconcurrentsessions) · [MaxConcurrentCallsPerSession](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebussessionprocessoroptions.maxconcurrentcallspersession)
- [Message sessions — Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sessions)
- [Target-based scaling in Azure Functions (session-count scaling)](https://learn.microsoft.com/en-us/azure/azure-functions/functions-target-based-scaling)
- [Saga concurrency — NServiceBus](https://docs.particular.net/nservicebus/sagas/concurrency) · [Saga guidance — MassTransit](https://masstransit-project.com/usage/sagas/guidance)
- [Ordered messaging without the locks — Wolverine global partitioning](https://jasperfx.net/news/ordered-messaging-without-the-locks-wolverine-global-partitioning-and-re-sequencer)
- [Event-sourcing pattern — Azure-Samples cosmos-db-design-patterns](https://github.com/Azure-Samples/cosmos-db-design-patterns/blob/main/event-sourcing/README.md) · [Change-feed design patterns — Azure Cosmos DB](https://learn.microsoft.com/en-us/azure/cosmos-db/change-feed-design-patterns)

*Design study only. No source, config, Azure state, or `.claude/**` files modified.*
