# Phase 3 — Server-side regarding writers: inventory + convergence status (FR-26)

> Produced by task **052**. Companion to [`phase3-derivation-rules.md`](phase3-derivation-rules.md) (task 050, the
> client side). Derivation rules are identical on both sides and pinned by a **cross-language parity test**.

---

## 0. Status of this task — read this first 🔴

| Deliverable | Status |
|---|---|
| Grep-complete writer inventory (§2) | ✅ Done — this document |
| C# `CoreAncestorResolver` + tests | ✅ Done — 24/24, incl. TS↔C# taxonomy parity |
| **Convergence of the inventoried writers** | ⛔ **NOT DONE — blocked, see §1** |

Task 052 is therefore **partially complete**. It is recorded as `blocked`, not `completed`. The reason is a
real coordination constraint, not an implementation difficulty.

---

## 1. Why convergence is blocked ⛔

**5 of the 9 server writers live under `src/server/api/Sprk.Bff.Api/Services/Communication/**`, which another
agent owns concurrently in this worktree.** The project's parallel-safety rule
(`projects/unified-access-control-r2/CLAUDE.md` → *Parallel-safety rules*) is explicit: two agents editing an
authorization path concurrently produce a silent merge mess. Editing them anyway is the failure mode the rule
exists to prevent, so this task stopped instead.

**⚠️ The POML is wrong about this.** `052-server-writer-ancestor-stamping.poml` declares:

```xml
<parallel-safe>true</parallel-safe>
<parallel-reason>Touches Services/Communication/** + a new helper — disjoint from task 050's
                 TS shared-lib files; …</parallel-reason>
<file role="modify">src/server/api/Sprk.Bff.Api/Services/Communication/ThreadResolver.cs</file>
```

It reasons about disjointness **only against task 050** and concludes `parallel-safe: true`. But
`Services/Communication/**` is a heavily-shared surface across this project's own waves and across the
concurrently-active email/communication projects. The POML's `<parallel-reason>` establishes disjointness with
one sibling task and then generalizes it to "safe" — which is not the same claim.

**Recommended fix to the POML/plan:** mark 052 `parallel-safe: false`, or split it:

| Split | Scope | Parallel-safe |
|---|---|---|
| **052a** | Audit + `CoreAncestorResolver` + tests | ✅ true — **this is what landed** |
| **052b** | Converge the `Services/Communication/**` writers | ❌ false — serialize with the communication owner |
| **052c** | Converge the non-communication writers (§2 rows 1, 6–8) | ✅ true |

---

## 2. Writer inventory (acceptance artifact)

Method: `grep` over `src/server` for `sprk_regarding{matter,project,workassignment,servicerequest,
communication,event,invoice,document,analysis,todo}` plus the four denormalized resolver fields, then
hand-inspection of each hit to separate **writes** from **reads/projections**.

| # | Writer | Host (child) entity | Evidence | Classification |
|---|---|---|---|---|
| 1 | `Services/Workspace/TodoRegardingBuilder.cs:169` | `sprk_todo` | Writes the typed lookup + 4 resolver fields. 11-entity map at `:54-68`. | 🔲 **Converge** — in-lane, deferred with 052c |
| 2 | `Services/Communication/CommunicationService.cs:1903,1936-1954` | `sprk_communication` | `RegardingLookupMap` (12 entities incl. service request at `:1914`) → typed lookup + resolver fields | ⛔ **Blocked** (owned) |
| 3 | `Services/Communication/ThreadResolver.cs:153-160,257` | `sprk_communication` (thread anchor) | Writes anchor resolver fields; reads `RegardingFieldMap.AllRegardingFields` | ⛔ **Blocked** (owned) |
| 4 | `Services/Communication/IncomingAssociationResolver.cs:381-420` | `sprk_communication` | Fallback regarding-field writer over `RegardingFieldMap.All` | ⛔ **Blocked** (owned) |
| 5 | `Services/Communication/IncomingCommunicationProcessor.cs:1260` | `sprk_communication` | Narrow `sprk_regardingmatter` write | ⛔ **Blocked** (owned) |
| 6 | `Services/Communication/Engine/Rungs/ExplicitReferenceRung.cs:56-106` | `sprk_communication` | Emits `RegardingFieldName` for the engine to write | ⛔ **Blocked** (owned) |
| 7 | `Services/Office/OfficeService.cs:1487-1560` | `sprk_todo` | Add-in "Related to" picker → typed lookup + resolver fields. **Map covers only Matter/Project/Invoice** | 🔲 **Converge** (052c) |
| 8 | `Services/Ai/Nodes/ActionCore/TaskActionCore.cs:57-73` | `sprk_event` | Creates a Task-typed `sprk_event` with a typed regarding lookup | 🔲 **Converge** (052c) |
| 9 | `Services/Ai/Handlers/EmailDraftToolHandler.cs:127-134,837` | `sprk_communication` | Draft-email create with regarding map + `sprk_regardingrecordid` | 🔲 **Converge** (052c) |
| 10 | `Spaarke.Dataverse/DataverseServiceClientImpl.cs:546-560` | `sprk_analysis` | Analysis resolver-field write (no `…recordurl` — not a deployed attribute there) | 🔲 **Converge** (052c) |
| 11 | `Infrastructure/ExternalAccess/ExternalDataService.cs:678-703` | `sprk_todo` (external) | Web-API mirror of `TodoRegardingBuilder` | ⛔ **Blocked** — `Infrastructure/ExternalAccess/**` is `parallel-safe:false` per project CLAUDE.md |

### Verified-trivial (no change needed)

None. **Every writer above is on a child-class host**, so each can receive a child-of-child target and each
needs the stamp. The POML anticipated a "writers that only ever set a CORE target lookup directly" category —
it is **empty in practice**, because all these maps accept child targets (communication, event, invoice,
analysis, document) as regarding parents.

### Escalation trigger — NOT fired ✅

The POML's `<escalation>` fires if a writer is found inside a Dataverse **plugin** (`src/dataverse/plugins`).
Grep found none: every regarding writer is in the BFF or `Spaarke.Dataverse`. No scope escalation needed on
that axis. (The block in §1 is a different, unanticipated axis.)

---

## 3. What landed: `CoreAncestorResolver`

`src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs`
Tests: `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/CoreAncestorResolverTests.cs` — 24 passing.

### Placement justification (CLAUDE.md §10)

| Question | Answer |
|---|---|
| **Existing?** | No C# equivalent. `grep CoreAncestor src/` returned zero before this task. Closest neighbours are the per-module regarding maps (`RegardingFieldMap.cs`, `TodoRegardingBuilder.RegardingLookupByEntity`, `TaskActionCore.RegardingFieldByEntity`) — all *field maps*, none derive an ancestor. |
| **Extension?** | No. `ThreadResolver` is communication-specific; derivation must serve todo, event, analysis, document and invoice writers too. Extending it would couple every child writer to the communication module — the opposite of what §10 asks for. |
| **Cost of doing nothing** | Concrete FR-27 acceptance failure: *"a contact with Project access sees its communications"* returns **0 rows** for server-filed emails, because the row carries no ancestor stamp. The one-time backfill (053) would also permanently miss every future server-created row. |
| **Placed in** | `Services/Dataverse/` — a cross-cutting Dataverse read helper, cohesive with `RecordService`/`MetadataService`, and deliberately NOT in `Services/Communication/`. |
| **New packages** | None. |
| **New endpoints / DI** | **None yet** — deliberately unregistered until a converged writer consumes it (CLAUDE.md §11: an unconsumed DI registration is surface with no cost-of-doing-nothing). 052c registers it alongside its first consumer. |
| **New interface** | None (ADR-010). Reads go through the already-registered `IGenericEntityService`; the column-presence test seam is a **delegate** (`EntityColumnProbe`), not a fresh abstraction. Production binds it to the 6h-cached `MetadataService` via `CoreAncestorResolver.FromMetadata`. |
| **Publish size** | Unchanged — one source file, zero package references. Not re-measured; the ≤60 MB ceiling is unaffected by a leaf class with no new dependency. Re-measure at 052c when DI + consumers land. |
| **CVE** | No package change → no new surface. |

### Design parity with the client (task 050)

Identical status model, identical rules:

| | Client (TS) | Server (C#) |
|---|---|---|
| Core target | stamps itself, **no read** | same |
| Child target | **one** read of its own core-ancestor lookups | same |
| Matter → Project | never stamped | same |
| Column presence | discovered nav-props | live metadata (`MetadataService`) |
| Statuses | `core-target` / `derived` / `no-ancestor` / `unclassified` / `error` | `CoreTarget` / `Derived` / `NoAncestor` / `Unclassified` / `Error` |
| Fail-closed | error result, caller aborts | `Error` status, caller aborts (**does not throw** — so a broad `catch` cannot swallow it) |
| Unstampable ancestor | returned + warned | returned + warned |

`CoreAncestorResolverTests.Taxonomy_MatchesTheTypeScriptSide` **reads the TypeScript file and compares the
literal arrays**, so the two implementations cannot drift silently — a one-sided edit fails the C# build.
That is stronger than the POML's "pinned by test on each side" (which only catches an edit on the side you
happen to run).

---

## 4. Convergence recipe for 052b / 052c

For each writer, at the point it binds a regarding target:

```csharp
var ancestor = await _coreAncestors.ResolveStampsAsync(targetEntity, targetId, ct);
if (!ancestor.Succeeded)
{
    // NFR-01: fail the operation or queue a retry per THIS writer's existing error contract.
    // Never create the child unstamped.
    return /* the writer's own failure shape */;
}
var hostColumns = await columnProbe(hostEntity, ct);
var unstampable = _coreAncestors.ApplyStamps(child, ancestor, hostColumns, skipEntityType: targetEntity);
```

Reparent paths must additionally **null** the core-ancestor lookups the new target does not supply — the same
stale-stamp hazard task 050 found on the client (`phase3-derivation-rules.md` F-050-3). A stale stamp keeps the
child visible to the OLD ancestor's principals after the user believes they detached it.

Per-writer notes:

- **#7 `OfficeService`** — its map covers only Matter/Project/Invoice. Invoice is child-class, so the add-in can
  already produce a child-of-child To Do today. Highest-value convergence outside the blocked set.
- **#8 `TaskActionCore`** — the host is `sprk_event`; keep the schema's own `sprk_regardingorganziation`
  misspelling (it is real, per the file's own warning).
- **#10 `DataverseServiceClientImpl`** — shared client; `sprk_analysis` has a reduced resolver field set (no
  `…recordurl`). Verify the four core-ancestor lookups actually exist on `sprk_analysis` before converging —
  the probe will report it, but the write path must not assume.

---

## 5. Open finding carried from task 050

**F-050-2 (unchanged, now confirmed server-side):** `sprk_todo` has no `sprk_regardingservicerequest` column
while `sprk_communication` does (`RegardingFieldMap.cs:18`). A To Do regarding a Communication whose ancestor is
a Service Request cannot be stamped, and will not be visible to principals whose access comes from that Service
Request. `ApplyStamps` reports it as `unstampable`; closing it requires a Dataverse schema change. Owner
decision needed — see tasks 028 and 056.
