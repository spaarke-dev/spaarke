# Phase 3 — Core-ancestor derivation rules (FR-26)

> Produced by task **050** (client write path). Task **052** mirrors these rules in C#.
> Source of truth: `spec.md` FR-26 · `design.md` §4.3 · `notes/investigation/06-adversarial-critique.md` §F1.

---

## 1. Why the stamp exists

The evaluator's child-inheritance term is a set-membership test:

```
child.sprk_regarding{core} ∈ {accessible core ids}
```

That test can only read a lookup the child **row already carries**. It cannot walk a chain — the
`ScopeDimension` shape is a synchronous `Func<CallerPrincipal, IReadOnlySet<Guid>>` with no Dataverse
round-trip by design (`ExternalModuleRegistry.cs:10-17`). So a `todo → communication → matter` chain is
**inexpressible** unless the ultimate core ancestor is denormalized onto the todo at write time. §F1 of the
adversarial critique proved this.

Denormalizing is what makes every chain **one hop**, which is why **ADR-034's 1-hop cap holds unamended** —
no exception was needed or taken.

---

## 2. Taxonomy (pinned literally by test)

| Class | Entities |
|---|---|
| **CORE** | `sprk_project`, `sprk_matter`, `sprk_workassignment`, `sprk_servicerequest` |
| **CHILD** | `sprk_invoice`, `sprk_communication`, `sprk_document`, `sprk_event`, `sprk_todo`, `sprk_analysis` |
| **Unclassified** | `sprk_budget`, `sprk_organization`, `contact`, `sprk_reportcard`, `account` |

**Unclassified is a real third state, not an oversight.** These entities appear in the regarding catalog and
confer access through *other* evaluator terms (org-expansion, explicit grant) — never through core-ancestor
inheritance. Derivation returns `unclassified` and stamps nothing. Folding them into either set would be a
silent model change.

Code: `CORE_RECORD_ENTITIES` / `CHILD_RECORD_ENTITIES` in
`src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`.

---

## 3. Derivation rules table

| Target class | Read performed | Result status | Stamp written |
|---|---|---|---|
| **CORE** | none | `core-target` | The target itself, and **only** the target |
| **CHILD**, ≥1 core-ancestor lookup populated | ONE `$select` of the target's own core-ancestor lookups | `derived` | Each non-null core ancestor |
| **CHILD**, all core-ancestor lookups null | same one read | `no-ancestor` | none |
| **CHILD**, target has no core-ancestor columns at all | metadata only | `no-ancestor` | none |
| Neither | none | `unclassified` | none |
| Read or metadata failure | attempted | `error` | **none — caller MUST abort the write** |

### 3.1 Matter does NOT inherit from Project

Both are CORE. Selecting a Matter stamps `sprk_regardingmatter` and nothing else — its `sprk_project`
association is **not** an access edge. Inverting this hands every Project holder every Matter beneath it.
Pinned by `doesNotStampAMattersOwnProject` and `stampsOnlyTheTargetItselfForACoreTarget`.

### 3.2 Exactly one hop

Derivation reads the target's own `sprk_regarding{core}` columns and **stops**. No recursion, no grandparent
walk. Those columns are themselves FR-26 stamps written by this same function when the *target* was saved —
which is precisely why one read is sufficient. Pinned by
`takesExactlyOneHopAndNeverWalksTheGrandparentChain`.

### 3.3 Fail closed (NFR-01) — and the asymmetry that matters

`error` is returned when the ancestor read throws, returns no row, or the target's metadata cannot be
discovered. The caller must not write.

Note the deliberate asymmetry with the resolver's *other* optional fields
(`sprk_regardingrecordnumber`, `sprk_regardingrecordname`), which degrade gracefully per NFR-06: those are
cosmetic, this one is an access edge. `no-ancestor` and `error` are kept as **distinct** states for the same
reason — "this record inherits nothing" and "we could not find out" must never share a branch.

---

## 4. Ordering — the reason there is one exported builder

`buildRegardingSelectionPayload()` assembles the whole payload. Each step's position is load-bearing:

1. **Derive first.** A failed derivation returns before any payload object exists, so it cannot become a
   partially-built write.
2. **Pre-clear** every other regarding lookup present on the host (FR-13 mutual exclusivity).
3. **`applyResolverFields`** sets the chosen lookup + the 5 resolver fields (ADR-024 — never reimplemented).
4. **Apply ancestor stamps LAST**, so a stamp this payload is setting can never be nulled by step 2.

Consumers get the combined payload rather than assembling it, so per-consumer ordering cannot be wrong. This
is the POML constraint *"the pre-clear step MUST be computed AFTER derivation"* implemented as
"derivation first, stamp application last" — which satisfies both readings and is robust to a consumer
reordering its own calls.

---

## 5. Schema findings (these are the interesting part)

### F-050-1 — Not every child carries all four core-ancestor lookups

`sprk_todo` has **11** regarding lookups and **no `sprk_regardingservicerequest`**
(06-adversarial-critique §F1, citing `docs/architecture/spaarke-todo-architecture.md:34`).
`sprk_communication` **does** have one (`Services/Communication/Engine/RegardingFieldMap.cs:18`).

Consequence: a `$select` of a non-existent lookup returns **HTTP 400** and would turn a schema gap into a
blocked save. Derivation therefore resolves column presence against the target's **discovered nav-props**
every time, never against an assumed list. Same on the write side.

**Live-metadata caveat (honest):** this session had no live Dataverse connection, so the per-entity column
sets above are from repo evidence (§F1 + `RegardingFieldMap.cs` + `CommunicationService.cs:1914`), not a
metadata query. The implementation does not depend on the list being right — presence is resolved at runtime
— but a live confirmation is still worth doing during Phase 3 UAT.

### F-050-2 — A derived ancestor the host cannot store is a real inheritance hole 🔴

If a To Do regards a Communication whose ancestor is a **Service Request**, the ancestor is derived but
cannot be stamped: `sprk_todo` has no column for it. That To Do will **not** be visible to principals whose
access comes from the Service Request.

This is **surfaced, not swallowed** — `buildRegardingSelectionPayload` returns
`unstampable: ['sprk_regardingservicerequest']` and logs a warn. It is a *schema* gap; closing it means
adding `sprk_todo.sprk_regardingservicerequest` (a Dataverse schema change, out of task 050's file set).

**Recommendation for the owner:** add the column, or accept and document that To Dos do not inherit
Service Request access. Task **056** (child-module registration) and **028** (service-request accessible set)
are the natural place to decide. Filed here so it is not lost.

### F-050-3 — `TODO_REGARDING_CATALOG` has no Service Request entry, which broke reparent-clear

The catalog drives the pre-clear loop. Because it omits `sprk_servicerequest`, a stale
`sprk_regardingservicerequest` stamp would have **survived a reparent** on any host that *does* carry the
column (e.g. `sprk_communication`) — leaving the child visible to the old ancestor's principals after the
user believes they detached it.

Fixed by pre-clearing the **union** of (host catalog) ∪ (`CORE_ANCESTOR_LOOKUPS`), intersected with what the
host actually has. `buildTodoRegardingClear` got the same treatment. The catalog itself was **not** extended —
it drives the PCF picker UI and changing it is a UX change owned by task 051.

---

## 6. What task 050 added vs reused

| | Decision |
|---|---|
| **Reused** | `applyResolverFields` (byte-unchanged), `discoverNavProps`, `cleanGuid`, `findNavProp`, the `IPolymorphicWebApi` shim, `TODO_REGARDING_CATALOG` |
| **Added** | `CORE_RECORD_ENTITIES` / `CHILD_RECORD_ENTITIES` / `CORE_ANCESTOR_LOOKUPS`, `deriveCoreAncestorStamps()`, `buildRegardingSelectionPayload()`, `findHostNavPropForLookup()` |
| **New files** | Tests only (`PolymorphicResolverService.coreAncestor.test.ts`) |

Placed **inside `PolymorphicResolverService.ts`** rather than a new module: ADR-024 makes this file the single
canonical home for resolver field-write logic, and derivation *is* write logic. A separate
`CoreAncestorService.ts` would have created a second place a consumer could import write logic from — the
exact fork ADR-024 and CLAUDE.md §11 exist to prevent. Exported via the module path (`dist/services/...`),
no new barrel entry, per ADR-012's PCF-webpack constraint.

---

## 7. ADR compliance (Step 9.5 gate)

| ADR | Result |
|---|---|
| **ADR-024** Polymorphic resolver | ✅ **Strengthened.** Derivation lives in the shared service; `buildTodoRegardingUpdate` now delegates its *entire* payload assembly instead of hand-rolling the pre-clear. One fewer place to reimplement write logic. |
| **ADR-034** 1-hop cap | ✅ **Structurally enforced**, not merely intended: one read, no recursion, pinned by `takesExactlyOneHopAndNeverWalksTheGrandparentChain`. **No amendment needed** — this task is what makes the cap hold. |
| ADR-010 / 021 / 028 / 001 / 007 / 008 / 009 | ✅ N/A — no DI, no UI, no auth, no BFF surface touched. |

### ⚠️ ADR-012 tension 1 — hard-coded entity names (CLAUDE.md §6.5 **Path A**, project-scoped exception)

ADR-012 says *"MUST NOT hard-code Dataverse entity names or schemas as string literals (use configurable
entity maps)."* `CORE_RECORD_ENTITIES` / `CHILD_RECORD_ENTITIES` are literal.

**Deliberate, and the rule's intent inverts here.** ADR-012's rule exists so *UI components* stay reusable
across schemas. This constant is not presentation — it is the **access model**. spec.md FR-26 and the task's
acceptance criteria require the sets to be pinned literally so that *"changing either set fails a test."*
Making them configurable would let a config edit silently change who can see what, with no test failing —
the precise failure mode the pinning exists to prevent.

**Scope of the exception:** the two taxonomy constants and `CORE_ANCESTOR_LOOKUPS` in
`PolymorphicResolverService.ts`. Nothing else. Recorded here per §6.5; cite in the PR.

### ⚠️ ADR-012 tension 2 — metadata read via raw `fetch`

`deriveCoreAncestorStamps` resolves column presence through the **existing shared** `discoverNavProps`,
which reaches `/api/data/v9.0/EntityDefinitions(...)` with a host-relative `fetch` (there is no shim for the
metadata endpoint; the data plane still goes through `IPolymorphicWebApi`). This is the pattern already
established in this file and consumed by every wizard service. **No new I/O surface was added** — a new
caller of an existing one, with the same `fetchImpl` test seam. Reported for completeness; recommend no
change.

### ⚠️ ADR-038 — the one `fetch` stub in the tests

Tests stub `fetch` for the **metadata** endpoint only, for the reason above. All data reads go through the
injected `IPolymorphicWebApi`. Noted in the test file header so a future reader does not mistake it for
data-plane mocking.

---

## 8. POML inaccuracies found (recorded per project convention)

| POML said | Reality |
|---|---|
| Step 3: derive "via the nav-prop table already passed in" | The passed-in table is the **HOST's** nav-props. Presence must be checked on the **TARGET**, which needs its own `discoverNavProps` call. Implemented that way. |
| Step 3 lists the four core lookups as if universal | `sprk_todo` has only three of them (no service request) — see F-050-1. |
| Step 5 test case "todo→communication-with-nothing yields explicit no-ancestor result" | Kept, and made a genuinely distinct status rather than an empty array, because an empty array is indistinguishable from `error`. |
| `<relevant-files>` names a `__tests__` path for "resolver service tests" | Correct path exists; tests added there. |
