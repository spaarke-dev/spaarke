# Storage consumption — spike result and branch

> **Task 024** (spec FR-C06, owner decision OC-04) · 2026-08-24
> **Result: consumption IS available. Branch taken: IMPLEMENT.**
> Escalation trigger evaluated and did **NOT** fire — no separate reporting API or additional
> permission scope is involved.

---

## 1. The spike was already answered, live, by task 020

Acceptance criterion 1 asks for the empirical finding **with raw evidence, recorded before the branch
was chosen**. It was — on 2026-08-23, against Spaarke Dev, with a real token, comparing v1.0 and beta
on the same tenant at the same moment. Full record:
[`beta-vs-v1-surface-verification.md`](beta-vs-v1-surface-verification.md) §2.

```
GET /v1.0/storage/fileStorage/containers?$select=id,displayName,storageUsedInBytes
→ 400  "Parsing OData Select and Expand failed:
        Could not find a property named 'storageUsedInBytes'"

GET /beta/storage/fileStorage/containers?$select=id,displayName,storageUsedInBytes
→ 200  { "storageUsedInBytes": … }
```

**Re-running it was not required and would have been wasteful** — the measurement is recent, direct,
and unambiguous. This session additionally has no interactive Azure login, so a re-run was not
available in any case.

### The finding is more specific than "always / sometimes / never"

| Surface | Reports consumption? |
|---|---|
| `GET /beta/…/containers` (**LIST**) | ✅ **yes** |
| `GET /beta/…/containers/{id}` (**GET**) | ❌ **no** — omitted even on beta |
| `GET /v1.0/…/containers` (either) | ❌ **not in the schema at all** (400 on `$select`) |

So availability is partitioned **by operation**, not by container. That is still the POML's
partial-availability case — its constraint applies — but the partition matters for the UI: the same
container legitimately shows a figure in the grid and none in a detail fetch.

This also explains task 020's decision to keep containers on `/beta`: migrating to v1.0 would have
**deleted this feature**, which is why the two tasks conflicted.

---

## 2. What the code was doing

`StorageUsedInBytes: null` was hardcoded at **four** sites, with the comment *"Not always returned by
Graph"* — while the `$select` at each read site **asked Graph for the field**. The code requested the
value and threw it away.

| Site | Method | Now |
|---|---|---|
| `:694` | `ListContainersAsync` | ✅ real value |
| `:1032` | `ListContainersPageAsync` | ✅ real value |
| `:1116` | `CreateContainerAsync` | read, not assumed (see below) |
| `:1166` | `GetContainerAsync` | read; normally null because GET omits it |

Consequences: every Containers row rendered **"—"**, and the Dashboard summed nothing into a confident
**"0 B"** — across a tenant holding signed NDAs, Compose drafts, and matter files. The spec calls this
the purest instance of the §2.4 systemic defect, and that is fair.

**The CREATE site is worth noting.** Its comment reasoned *"New containers always start empty; Graph
returns null here"* and hardcoded null on that basis. The inference is probably true — but an inference
is not a measurement, and if Graph ever did report a figure there, the code would have discarded it
indefinitely. It now reads what arrived.

---

## 3. Implementation notes

### `storageUsedInBytes` is untyped, so its runtime type is a hazard

The SDK models the **v1.0** schema, and this property is **beta-only** — so it is absent from
`FileStorageContainer`'s typed surface and arrives through `AdditionalData`. Exactly the shape that
cost task 022 the deleted-container timestamp: that code tested `is string`, Kiota had stored a
`DateTime`, and the value was silently dropped for the life of the product.

`ReadStorageUsedInBytes` therefore accepts **every numeric shape Kiota can produce** — `long`, `int`,
`double`, `decimal`, `string`, `JsonElement` — instead of guessing one. A byte count straddles the
int/long boundary, which is precisely where a narrow match would break: 5 GB does not fit in an `int`.
Pinned by a theory over 42, 5 GB, and 2^53−1.

### Null means NOT REPORTED. Zero means zero.

Both directions are guarded by tests. Collapsing them is what let the Dashboard show "0 B", and the
converse trap — flattening a genuine 0 into "not reported" — would be equally wrong for an empty
container.

### The Dashboard total now states its own coverage

A partial sum presented as a total is the same defect in miniature. `DashboardMetrics` gained
`StorageReportingContainerCount`, and the tile reads:

| Situation | Renders |
|---|---|
| all containers reported | `Across all N container(s)` |
| some reported | **`At least — only M of N container(s) reported`** |
| none reported | value **`Not reported`**, subtext names the count |
| no containers | `No containers` |

The field is optional client-side so an older cached metrics payload still deserializes.

### Ceiling vs consumption stayed separate (AC-6)

Nothing here touches `maxStoragePerContainerInBytes`, which task 023 established as the per-container
quota **ceiling** on the container **type**. Different concept, different resource, no shared field,
DTO property, parameter, or control.

### Also fixed here — the last 4 timestamp fabrications (handed over by task 023)

`CreatedDateTime ?? DateTimeOffset.UtcNow` at the same four sites rendered a container of unknown age
as **created today**. `SpeContainerSummary.CreatedDateTime` and `ContainerDto.CreatedDateTime` are now
nullable. **`SpeAdminGraphService.cs` now has zero `?? DateTimeOffset.UtcNow` fabrications on any
read path.**

---

## 4. Tests

Task 040's characterization test `ListContainers_AlwaysReportsStorageUsedAsNull_PinningTheKnownDefect`
said *"WHEN THAT LANDS, THIS TEST MUST FAIL AND BE UPDATED"*. It did — inverted to
`ListContainers_ReportsTheStorageGraphActuallySent`, plus:

- `…WhenGraphOmitsStorage_ReportsNull_NotZero`
- `…ReadsZeroAsZero_NotAsAbsent`
- `…ReadsStorage_WhateverNumericShapeKiotaProduces` (theory, 3 cases)

One incidental correction: that test's comment claimed `storageUsedInBytes` was "a quota CEILING…not
consumption". Task 023 settled that it genuinely **is** consumption on a container; the ceiling is a
different property on a different resource.

---

## 5. Gates

- **BFF build ✅** — 0 errors, 7 pre-existing warnings.
- **Tests ✅** — **10,670 passing** (+5), 0 failed, 97 skipped. **ArchTests 36/36.**
- **Publish ✅** — **43.67 MB compressed incl. PDBs, 0 MB delta**. Ceiling 60 MB. No package change.
- **Code page ✅** — `vite build`, 17.28 s. 0 type errors in the touched files.

**Placement justification (root CLAUDE.md §10):** no new endpoint, service, DI registration, or
package. Four call-site corrections, one private reader, one added metrics field. The `<justification>`
block's extension test is satisfied — this extends the existing container read path rather than adding
a service.

⚠️ **Step 6 not live-verified.** "No screen shows a silent 0 B or em-dash" requires a deployed app and
an interactive Azure login this session cannot perform. The mapping is pinned by WireMock against the
real Kiota deserializer — where the defect lived — and the UI states are deterministic on the null/0
distinction, but a live pass remains outstanding (001 / 003 / 012 / 021 / 022 / 023 / 030).
