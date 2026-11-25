# Signal read fixes B and D: notes

Ontology task 222 (`spaarke-ontology-platform-r1/tasks/222-worklist-read-latency-single-row-and-concurrent.poml`), built by
mcp-r1 as its own PR to master. Entry point: `src/server/api/Sprk.Bff.Api/Services/Signals/SignalReadService.cs`, `ReadAsync`.

Why: on dev App Insights (2026-11-24/25) about 98% of a worklist read is Dataverse round trips made one after another
(~80-110 ms each, `sprk_signals` FetchXML ~250 ms). One `ReadAsync(Lanes)` is ~17 serial queries, ~1.8-2.0 s; the Console's
`GET /api/v1/signals/worklist` is p50 2.0 s, p95 3.8 s; the MCP plan tool reads the whole lane to pick one row (~2 s per open).

Written before any code change (task 222 step 1).

## 1. Step dependency graph of `ReadAsync`

Each line: step, identity, what it reads from earlier steps. "Independent of" names the edges that can run at the same time.

| Step | Identity | Reads | Can run beside |
|---|---|---|---|
| (1)+(2) caller: `WhoAmI`, then the linked contact | caller (WhoAmI), app/cache (contact) | nothing | (3) |
| (3) configuration row (`sprk_gridconfiguration`) | app | `set` only | (1)+(2) |
| (4) the saved fetches (`FetchAllAsCallerAsync`, one per fetch, each paged serially) | caller | (3); runs after (1)+(2) so an unresolved caller reads no Signal | each other |
| (5) catalog (`sprk_recordtype_ref`) | app | (4) `rows` | (6b) |
| (6) core-record access, per core type per 100 ids | caller | (4) `rows`, (5) `catalog`; per (type, chunk) independent; entity set name first per type | (6b); each other |
| (6b) no-core pair check, per 100 ids | app | (4) `rows` only | (5) and (6) |
| candidates | none (pure) | (6), (6b) | |
| (7) subjects: Do "mine" per rule table, Decide flag per table, per 100 ids | caller | candidates, (5), (1)+(2); per (table, chunk) independent | each other |
| visible | none (pure) | (7) | |
| (8) closed sets only: per-item day | app | visible; a per-request zone cache shared across rows | kept serial (not on the lane path) |
| (9) witness rule: caller witness reads per table per 200 ids, then the app-only family placement | caller, then app | visible's stored evidence; per (table, chunk) independent | (9a), (11) |
| (9a) line invoices (one batched read) | caller | visible's subjects + (5); not changed by (9) | (9), (11) |
| (9b) duplicate rule | caller | (9) output, (9a), (5); stored quotes taken before (9) | (11) |
| (10) closed/decided sets only: Decision Records | caller | visible's record ids | kept serial |
| (11) policy columns (external check, then `sprk_policy` per 100 ids) | app | visible's policy ids, fixed after (7)/(8) for every set but Decided (Decided filters at (10)) | (9), (9a), (9b), (10) |
| (12) invoice review, shaping, extras | none (pure) | all of the above | |

The serial order of steps also fixes which failure wins today: the first step (and, inside a step, the first type/table and
chunk) that faults or returns a failure outcome ends the read; later steps never run.

## 2. Client thread-safety findings

**`IDataverseUserClient` (caller identity; `DataverseUserClient`, Web API over HttpClient, OBO).** Safe for concurrent `GetAsync`:
- every call builds its own `HttpRequestMessage`; `HttpClient.SendAsync` is thread-safe;
- the OBO exchange goes through the singleton `OrderedCredentialClientProvider` and MSAL's confidential client, whose user-token
  cache is thread-safe (the process already exchanges for many requests at once);
- the request context is only read (`HttpContext.User`, the bearer header);
- the one write is `CallerWhoAmIMemo` into `HttpContext.Items`, and only on `WhoAmI()`; this read sends one `WhoAmI` and it runs
  beside the app-only configuration read only, which does not touch `HttpContext`;
- precedent: `CallerMetadataReadMemo` notes the record-create caller checks already ask in parallel (field-mapping task 049).

**`IGenericEntityService` (app identity; `DataverseServiceClientImpl` over one `ServiceClient`).** Safe for concurrent async calls:
- it is a DI singleton, so every BFF request already shares its `ServiceClient` concurrently;
- Microsoft Learn, "Send parallel requests to Dataverse" (checked 2026-11-25): with .NET 6 or higher, use `Parallel.ForEachAsync`
  with one `ServiceClient` and its async methods; `Clone()` is the .NET Framework `CrmServiceClient` pattern. So no `Clone()` is needed;
- no code sets `CallerId` on the shared client: the one impersonation (`UserPrivilegeChecker`) clones first so the singleton is never mutated;
- `GetEntitySetNameAsync` caches in a `ConcurrentDictionary` (ADR-009 metadata L1); `RetrieveEntitySetNameAsync` runs the sync
  `Execute` on the thread pool, which is the same client and also already concurrent process-wide.

**Other state the read touches.**
- `IIdentityNormalizationService` (Redis `ITenantCache`), `ICallerSystemUserResolver`, `ISystemUserIdentityResolver` (distributed
  cache): no shared mutable in-memory state; each is called from one branch at a time.
- `SignalSuppressionService` (singleton) and `EarlierInvoiceReadability` (stateless): each is called from one branch.
- `SignalReadService` itself keeps no per-request mutable fields; the per-read state (`rows`, `catalog`, dictionaries) is merged
  by the awaiting code, in the serial order, never written from two tasks.

Verdict: both clients are safe for concurrent reads. No identity changes: every read keeps the identity it has today.

## 3. What runs concurrently (fix D)

One per-read `DataverseConcurrencyGate` (the existing #2845 type, a new instance per read) with **4 permits**: a permit is held
for one Dataverse call, or for one short serial sequence that takes no other permit, so it cannot deadlock. At most 4 of this
read's Dataverse requests are in flight at once, well under the 52-per-user service-protection limit, leaving room for the
caller's other requests.

Known limit (K1, ontology review 2026-11-25): the bound is per read, not per identity. On the caller side the Console's ~3
concurrent set reads give about 12 in flight per user, still under 52. On the app side the config/WhoAmI, catalog/pair-check and
policy/family-placement reads now overlap, so one read makes ~2–3 concurrent app-identity calls where it made 1, and every user
shares the BFF identity; `ServiceClient` retries a 429, so heavy load is slower, not a failure.

- (3) beside (1)+(2).
- The saved fetches beside each other (each still pages serially).
- (6b) beside (5) then (6).
- Inside (6) and (7): every (type or table, chunk) read; each type's entity set name is read once, before its chunks.
- Inside (9): every (witness table, chunk) read.
- (9a) and (11) beside (9); (11) also beside (9b) and (10). For the Decided set (11) stays at its own position.

Kept serial: the paging inside one fetch, (8), (10) and (9b) (the policy unit may overlap them).

**Equivalence rules.**
- Results are merged in the serial order (type, then chunk; rule table order; Decide tables by first appearance), so
  dictionaries, ordering and "last write" are as today.
- After every branch has finished, the branches are awaited again in serial order: the first one that faulted (rethrown as it
  was) or returned a failure outcome is the read's answer, the one the serial loop would have met first.
- Response parsing and its logging (`ReadAccessRows`, the witness read's fault log) run at that in-order inspection, so the
  same log lines are written.
- No partial result and no task left running: every started task is awaited before `ReadAsync` returns, including when an
  earlier step fails.
- The cancellation token flows to every branch, and to the gate's wait.

Known log-only differences: when an earlier branch fails, a later concurrent branch may still have run and logged (for example
the second saved fetch's own warning, or the (6b) "left out" warnings). The response is unchanged.

## 4. Single-row read (fix B)

`ReadAsync(set, user, ct, extras, onlySignalId)`. When `onlySignalId` is set, each saved fetch's root `<entity>` gets its
top-level filters and `sprk_signalid eq <id>` wrapped in one `<filter type="and">`, so the id narrows the configuration's own
predicate and can never widen it. The read still runs as the caller through `FetchAllAsCallerAsync`; every later step is the
same code. A `WorklistItemRow` is built from its own row only (`ToItem`), and the rules that drop a row are per row, so a
visible Signal comes back identical. A Signal any rule hides comes back as an empty response, the same as an unknown id.

Differences that are faults or volume limits, not visibility rules:
- the lane refuses (503) past `MaxPages` pages; a single-row read never pages;
- a witness-table read fault withholds every witness detail on the lane; a single-row read is withheld only on its own fault;
- (6b): a no-core Do row the app-only read does not return is dropped from a lane where other rows of its chunk came back; alone in
  its chunk it is the 503 the lane also gives when every row of a chunk is missing (a delete race, transient);
- (K2, ontology review 2026-11-25) a duplicate-rule or family-placement fault withholds detail on every lane row, but only on its
  own row in a single-row read; a line-invoice read fault drops `invoiceReview` lane-wide, but only on its own row in a
  single-row read.

All of these fail toward showing only what the caller is already entitled to. None is an existence oracle: the 503 needs the
caller's own Dataverse read to have returned the Signal, and the plan tool authorises the Signal before it reads the row.

The id is never logged.
