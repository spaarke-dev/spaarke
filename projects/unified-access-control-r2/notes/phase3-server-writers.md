# Phase 3 — Server-side regarding writers: inventory + convergence status (FR-26)

> Produced by task **052**. Companion to [`phase3-derivation-rules.md`](phase3-derivation-rules.md) (task 050, the
> client side). Derivation rules are identical on both sides and pinned by a **cross-language parity test**.
>
> **2026-09-03 — convergence pass.** The first 052 pass shipped the resolver + inventory and stopped, blocked on
> `Services/Communication/**` ownership. That block cleared; this document is the rewritten result. §1 records
> what the first pass got wrong, because three of its eleven rows were misclassified and its headline finding was
> false.

---

## 0. Status

| Deliverable | Status |
|---|---|
| Grep-complete writer inventory (§2) | ✅ Re-verified independently, 3 corrections |
| C# `CoreAncestorResolver` + tests | ✅ Extended with the stamping surface + DI registration |
| **Convergence of the inventoried writers** | ✅ **Done** — 6 converged, 2 verified-trivial, 3 reclassified |

---

## 1. Corrections to the first pass 🔴

The first pass's inventory was grep-complete but its **classifications were not verified against each writer's
own constraints**. Re-inspection found three rows wrong, and the headline conclusion wrong with them.

### 1a. "The verified-trivial category is EMPTY in practice" — FALSE

The first pass claimed *"every writer above is on a child-class host, so each can receive a child-of-child
target"* and concluded the POML's verified-trivial category was empty. That reasoning conflates the **host** with
the **target**. A writer on a child host is only a risk if it can be handed a *child target*, and two writers
cannot be:

| Writer | Why it is genuinely trivial |
|---|---|
| `Spaarke.Dataverse/DataverseServiceClientImpl.StageAnalysisRegardingFields` | Its lookup map is `RegardingRecordType.GetRegardingLookupFieldByEntity` (`Models.cs:977`), which returns non-null **only** for `sprk_matter` and `sprk_project` — both CORE — and the caller **throws** on anything else (`:453-458`, `:537-540`). A child target cannot reach the write. |
| `Infrastructure/ExternalAccess/ExternalDataService.CreateTodoAsync` | The regarding entity is the **string literal `"sprk_project"`** (`:543-544`), bound alongside a literal `sprk_RegardingProject@odata.bind`. There is no parameter to pass a child through. |

Both write the CORE lookup directly, which **is** the FR-26 stamp. No change needed; changing them would add a
metadata round-trip and a failure mode for zero access-model gain. `ExternalDataService` is additionally inside
the project-wide `parallel-safe:false` zone, so leaving it alone is doubly correct.

### 1b. `IncomingCommunicationProcessor.cs:1260` is a READ, not a write

Classified "Narrow `sprk_regardingmatter` write". It is
`RetrieveAsync("sprk_communication", communicationId, ["sprk_regardingmatter"], ct)` — a projection feeding an
email-received notification. The same file states at `:661-662` that *"Regarding fields … are set in step 4.5 by
IncomingAssociationResolver"*. It is not a writer.

### 1c. `ExplicitReferenceRung` is an emitter, not a writer — and converging it would be the wrong seam

It returns `RungMatch { RegardingFieldName = … }`. It writes nothing. So do the other ~9 rungs. **Every** rung
funnels into one write, `IncomingAssociationResolver.ApplyDecisionAsync` → `UpdateAsync` (`:316`). Converging at
the rung would have to be repeated 10 times and would still miss any rung added later; converging at the single
write covers all of them permanently. That is where the stamp went.

### 1d. `ThreadResolver`'s host is `sprk_communicationthread`, not `sprk_communication`

The row said "`sprk_communication` (thread anchor)". `CreateThreadAsync` and `FindOrCreateDefaultThreadAsync`
both build `new DataverseEntity("sprk_communicationthread")`. That entity is **in neither taxonomy set** — not
CORE, not CHILD. See §3 for why that means "do not stamp", not "stamp anyway".

### 1e. The POML was right about one thing the first pass called wrong

The first pass recommended splitting 052 into 052a/b/c. Not needed: with `Services/Communication/**` released,
the whole convergence is one coherent change, and splitting it would have shipped a resolver used by half its
writers — the worst of both states. `parallel-safe:false` (already corrected in the POML) is the right fix.

---

## 2. Writer inventory (acceptance artifact)

Method: `Grep` for `sprk_regarding*` across all `.cs` under `src/` (51 files, 344 occurrences), then
hand-inspection of each hit to separate **writes** from **reads/projections**, and — new in this pass —
inspection of each writer's **target map** to determine whether a CHILD target can actually reach it.

| # | Writer | Host entity | Can take a CHILD target? | Classification |
|---|---|---|---|---|
| 1 | `Services/Workspace/TodoRegardingBuilder.cs:169` | `sprk_todo` | ✅ 5 of 11 (event, communication, invoice, analysis, document) | ✅ **Converged** |
| 2 | `Services/Communication/CommunicationService.cs:1936` (`MapAssociationFields`, **3 call sites**) | `sprk_communication` | ✅ 4 of 12 (analysis, invoice, event, communication) | ✅ **Converged** |
| 3 | `Services/Communication/ThreadResolver.cs:156,588` | **`sprk_communicationthread`** | n/a — host is outside the taxonomy | ⚪ **Out of scope** (§3) |
| 4 | `Services/Communication/IncomingAssociationResolver.cs:316` | `sprk_communication` | ✅ 4 of 12 via `RegardingFieldMap.All` | ✅ **Converged** — the single seam for all ~10 rungs |
| 5 | `Services/Communication/IncomingCommunicationProcessor.cs:1260` | — | — | ❌ **Not a writer** (§1b) |
| 6 | `Services/Communication/Engine/Rungs/ExplicitReferenceRung.cs:56-106` | — | — | ❌ **Emitter**, covered by #4 (§1c) |
| 7 | `Services/Office/OfficeService.cs:1547` | `sprk_todo` | ✅ Invoice (of Matter/Project/Invoice) | ✅ **Converged** |
| 8 | `Services/Ai/Nodes/ActionCore/TaskActionCore.cs:106` | `sprk_event` | ✅ 4 of 14 (invoice, analysis, event, communication) | ✅ **Converged** |
| 9 | `Services/Ai/Handlers/EmailDraftToolHandler.cs:832` | `sprk_communication` | ✅ 2 of 8 (analysis, invoice) | ✅ **Converged** |
| 10 | `Spaarke.Dataverse/DataverseServiceClientImpl.cs:543` | `sprk_analysis` | ❌ matter/project only, **throws** otherwise | ✅ **Verified-trivial** (§1a) |
| 11 | `Infrastructure/ExternalAccess/ExternalDataService.cs:544` | `sprk_todo` | ❌ literal `"sprk_project"` | ✅ **Verified-trivial** (§1a) |

**Escalation trigger — NOT fired.** No regarding writer exists in `src/dataverse/plugins`. Every one is in the
BFF or `Spaarke.Dataverse`. No deployment-surface escalation needed.

---

## 3. `sprk_communicationthread` — the decision not to stamp

`ThreadResolver` writes typed regarding lookups onto `sprk_communicationthread` (via `RegardingFieldMap`, so it
CAN bind a child target). It was not converged, deliberately:

- `sprk_communicationthread` is in **neither** `CORE_RECORD_ENTITIES` nor `CHILD_RECORD_ENTITIES`. Per
  `phase3-derivation-rules.md` §2, entities in neither set are **`unclassified`** — a real third state, not an
  oversight. They confer access through other evaluator terms, never through core-ancestor inheritance.
- Stamping it would extend child inheritance to a new entity. That is a **model change**, and the place to make
  it is task **056** (child-module registration), where the entity would also get its `ScopeDimension` — a stamp
  with no registered child module does nothing except look like the question was already answered.
- Writing an access-conferring lookup onto an entity the evaluator does not read is the worst of both: no
  benefit today, and a future reader who finds the column populated will reasonably assume inheritance works.

**Filed for task 056**: decide whether threads are child-class. If yes, add `sprk_communicationthread` to the
taxonomy (both sides — the parity test enforces it) and converge `ThreadResolver` in the same change.

---

## 4. What the convergence actually does

### 4.1 The resolver's new surface

`CoreAncestorResolver` gained the stamping half of the job (it previously only derived):

| Member | For |
|---|---|
| `StampAsync(Entity child, targetEntity, targetId, ct)` | Writers building an `Entity` (#1, #2, #7, #8) |
| `StampAsync(IDictionary<string,object> fields, hostEntity, …)` | Writers building an update payload |
| `DeriveForHostAsync(hostEntity, targetEntity, targetId, ct)` | Writers whose payload is neither (#4's multi-target loop, #9's hand-written JSON) |
| `ApplyStamps(IDictionary<string,object>, …)` | Dictionary sibling of the existing `Entity` overload |
| `CoreAncestorStampOutcome` | The result a writer must inspect: `Succeeded`, `Stamps`, `Unstampable`, `Error` |

Derivation, host-column probe, and application are **one call** on purpose. Split across three, every writer
re-implements the same three failure branches, and one of them forgetting the `Succeeded` check is a silent
under-grant that no test would catch. One call, one outcome object, one thing to get wrong.

The **host** column set is resolved from live metadata, exactly like the target's — not from each writer's own
regarding map. Two writers (#1 and #7) share the `sprk_todo` host, so a static per-writer list would have
duplicated that knowledge in two modules and drifted. It also matches the client's discover-never-assume rule
(F-050-1: a `$select` of a non-existent column is an HTTP 400).

### 4.2 Ordering — the same as the client's

Every converged writer stamps **after** it binds its own typed lookup. Two reasons, both load-bearing:

1. A stamp applied last cannot be nulled by a pre-clear or overwritten by the direct bind.
2. `TodoRegardingBuilder`'s ADR-024 mutual-exclusion guard fires on an **already-set** lookup. Stamping first
   would make an ancestor stamp look like a competing user choice and throw. The client's
   `buildRegardingSelectionPayload` orders it the same way, for the same reason
   (`phase3-derivation-rules.md` §4).

The directly-bound target is always skipped (`skipEntityType`) — the writer already wrote it.

### 4.3 Fail-closed, in each writer's own error contract (NFR-01)

The POML says *"fail the operation (or queue retry per the writer's existing error contract) — never create the
child silently unstamped."* Each writer's existing shape was used rather than a new one:

| Writer | Existing failure shape | Fail-closed behaviour |
|---|---|---|
| #1 `TodoRegardingBuilder` | throws (`ArgumentException` / `InvalidOperationException`) | throws; callers create the to-do only after it returns |
| #2 `CommunicationService` | `SdapProblemException` (as `ValidateRequest`) | `SdapProblemException` 502 `CORE_ANCESTOR_DERIVATION_FAILED` |
| #4 `IncomingAssociationResolver` | defensive; association failure is non-fatal (NFR-06) | throws → association fails, communication survives unassociated; **no partial write** |
| #7 `OfficeService.CreateTodoAsync` | `null` return | `null` |
| #8 `TaskActionCore` | "degraded success" `Guid.Empty` | `Guid.Empty`, **and no `CreateAsync` call** |
| #9 `EmailDraftToolHandler` | `ToolResult` error | `ToolErrorCodes.InternalError`, before the payload is built |

`Unstampable` is **never** a failure. A derived ancestor the host has no column for is a schema gap (F-050-2),
surfaced as a warning and returned on the outcome — aborting the write there would turn a known schema hole into
an outage.

### 4.4 One deliberate asymmetry: #4 does not clear stale stamps

`phase3-derivation-rules.md` F-050-3 requires reparent paths to **null** the core-ancestor lookups the new
target does not supply. `IncomingAssociationResolver` does **not** do this, on purpose:

The engine is additive by contract — *"a sibling regarding field is never cleared"* (task-042 semantics), and it
runs on inbound mail **suggestion**, not on a user's deliberate reparent. Making it clear would let an inbound
heuristic silently unfile a human's manual filing. Clearing on reparent belongs to the deliberate reparent paths
(tasks 050/051), where a person chose the new target. Recorded here so the asymmetry is a decision, not an
omission.

Related: a rung that wrote a CORE lookup **explicitly** is never overwritten by a derived stamp. The rung
observed evidence about *this message*; the stamp is only an inherited pointer.

---

## 5. DI placement (CLAUDE.md §10)

| Question | Answer |
|---|---|
| **Existing?** | No C# equivalent existed before task 052. Nearest neighbours are per-module field maps (`RegardingFieldMap`, `TodoRegardingBuilder.RegardingLookupByEntity`, `TaskActionCore.RegardingFieldByEntity`, `RegardingRecordType`) — maps, not derivations. |
| **Extension?** | No. `ThreadResolver` is communication-specific; the derivation serves todo, event, communication and (trivially) analysis writers across four modules. |
| **Cost of doing nothing** | FR-27 acceptance failure: *"a contact with Project access sees its communications"* returns 0 rows for every server-created child of a child. Task 053's backfill would also permanently miss every future one. |
| **Placed in** | `Services/Dataverse/` — a cross-cutting Dataverse helper, deliberately not `Services/Communication/`. |
| **Lifetime** | **Singleton** with a scope-bridged metadata probe. Consumers span every lifetime (`CommunicationService` + `IncomingAssociationResolver` are singletons; `OfficeService` and the tool handlers are scoped; `TaskActionCore` / `TodoRegardingBuilder` are constructed inline) — only a singleton serves all. `MetadataService` is **scoped**, so capturing one would be a captive dependency; the `EntityColumnProbe` opens a scope per call, the same bridge `UpdateRecordActionCore.cs:222` already uses. Metadata is Redis-cached 6h, so steady state is a cache read. |
| **Registered** | `AddCoreAncestorResolver()` (idempotent `TryAddSingleton`), called from `AddDataverseMetadataServices()` **and** from both `AddToolFramework` overloads. The second call is not redundant: `EmailDraftToolHandler` is registered by the tool-framework assembly scan, so registering only in the metadata module would make it unresolvable wherever the tool framework is composed without that module — the §10 F.1 asymmetric-registration anti-pattern. Same posture as `TimeProvider` three lines above. |
| **New interface** | None (ADR-010). The only seam is the `EntityColumnProbe` delegate. |
| **New packages / endpoints** | None. |
| **Publish size** | Unaffected: no package reference added, no new project reference. Baseline ~44.96 MB incl. PDBs; ceiling 60 MB. |

---

## 6. Tests

| File | Covers |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/CoreAncestorResolverTests.cs` | Taxonomy pinned literally + **TS↔C# parity by reading the TypeScript file**, derivation rules, one-hop cap, Matter≠Project-child, fail-closed states |
| `tests/integration/data-mutation/CoreAncestorStamping/ServerWriterAncestorStampingTests.cs` | **New.** That the WRITERS call it: stamp-on-create for a child-of-child on `sprk_todo` / `sprk_event` / `sprk_communication`, core-target-stamps-only-itself, explicit-rung-write-not-overwritten, and a fail-closed negative per writer |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/CoreAncestorResolverFixtures.cs` | **New.** `Inert()` / `WithAncestors(…)` / `Failing()` — the ~42 existing writer tests take `Inert()`, which exercises the real derivation path but derives nothing, so their assertions are unchanged |

KEEP path: the new writer tests are at `tests/integration/data-mutation/**` per `tests/CLAUDE.md`
("every new write path → ≥1 integration test"). The negative cases are the point — an unstamped row is
indistinguishable from a correct one until records go missing from a client's view, so a happy-path-only suite
would pass against a writer that swallowed derivation errors.

---

## 7. Open findings

### F-050-2 (unchanged, confirmed server-side) 🔴

`sprk_todo` has no `sprk_regardingservicerequest` column; `sprk_communication` does
(`RegardingFieldMap.cs:18`). A To Do regarding a Communication whose ancestor is a Service Request **cannot be
stamped**, and will not be visible to principals whose access comes from that Service Request.

Server behaviour now matches the client exactly: the ancestor is derived, `ApplyStamps` reports it in
`Unstampable`, a warning names the missing column and the affected record, and **the write proceeds**. Closing
it requires a Dataverse schema change. Owner decision — tasks **028** and **056**.

### F-052-1 — `sprk_communicationthread` taxonomy question (new)

See §3. Needs a decision at task 056.

### F-052-2 — `EmailDraftToolHandler` derives app-only under a user-context write (new, low)

That handler writes through `IDataverseUserClient` (user OBO), but the ancestor derivation goes through the
app-only `IGenericEntityService` like every other writer. The user can already bind the target as regarding, so
they can see it; the ancestor id is a denormalization onto a row they are creating, not a new disclosure. Noted
rather than fixed — a user-context derivation path would be a second mechanism for one caller, and the stamp
would be *missing* (breaking FR-26) whenever the user cannot read the ancestor. Revisit if the evaluator ever
treats the stamp as caller-asserted rather than system-derived.
