# Task 029 — external To Do read + create parity with PATCH

> Rigor FULL · `sonnet` @ `high` (executed on Opus 5) · directional steps · deps 009 ✅
> Started 2026-09-09. Closes the read/write asymmetry task 009 created and flagged.

---

## 0. Live-metadata verification (step 0)

Dataverse MCP was **down** this session (`CONNECTION_CLOSED`), which is the condition the POML's
first escalation trigger names. It did **not** fire: `pac auth` and `az` are both authenticated
against `spaarkedev1`, so live metadata was reachable directly over the Web API with a delegated
token (`az account get-access-token --resource https://spaarkedev1.crm.dynamics.com`). Everything
below is from that live query on **2026-09-09**, not from repo prose.

### 0.1 The three `@odata.bind` navigation properties

`GET /api/data/v9.2/RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata`
`?$select=SchemaName,ReferencingEntity,ReferencingAttribute,ReferencedEntity,ReferencingEntityNavigationPropertyName`
`&$filter=ReferencingEntity eq 'sprk_todo'`

| Lookup attribute | `@odata.bind` navigation property | Target entity |
|---|---|---|
| `sprk_regardingproject` | **`sprk_RegardingProject`** | `sprk_project` |
| `sprk_regardingmatter` | **`sprk_RegardingMatter`** | `sprk_matter` |
| `sprk_regardingworkassignment` | **`sprk_RegardingWorkAssignment`** | `sprk_workassignment` |

The POML warned that "matter / work assignment may not follow the same capitalization". They do —
all three are `sprk_Regarding{PascalEntity}`. **But that was worth checking rather than assuming**,
because the next two findings show the surrounding conventions are *not* uniform.

### 0.2 `sprk_todo` has **14** regarding lookups, not 13 — a number that AGED, not a note that was wrong

⚠️ **I first wrote this up as "the census claim is wrong for the third time in three months." That
was unfair and I checked before letting it stand.** Task 009's count of 13 was **correct on the day
it was measured** (2026-08-24, live). `sprk_regardingagreement` → `sprk_agreement` was live-verified
by **`record-header-and-notepad-r2` (FR-24) on 2026-08-25** — the very next day — and added to that
project's `SUPPORTED_TODO_PARENTS` map. Nobody erred; a measurement decayed in 16 days.

That distinction matters, because the two failures have different fixes. A *wrong* note is fixed by
re-reading the source. An *aged* measurement is only fixed by **re-measuring**, and no amount of care
at authoring time prevents it. This is the same hazard CLAUDE.md §10 documents for the publish-size
baseline — *"re-measuring master is the measurement; the recorded number is only a sanity check"* —
showing up in schema rather than in build output.

Corrected in place in `ExternalDataService.cs`, with the as-of date stated beside the number so the
next reader can see how stale it is instead of trusting it. The full live list:

`agreement · analysis · budget · communication · contact · document · event · invoice · matter ·
organization · project · recordtype* · reportcard · servicerequest · workassignment`

(*`sprk_regardingrecordtype` is the resolver's type lookup to `sprk_recordtype_ref`, not a parent —
so 14 parents + 1 resolver lookup.)

**Not fixed here, but worth someone's attention**: there are now **two 12-entry client maps of the
same set, with different contents**, and neither is complete.

| Map | Count | Missing vs live |
|---|---|---|
| `TODO_REGARDING_CATALOG` (`Spaarke.UI.Components/services/TodoRegardingUpdateBuilder.ts`) | 12 | `agreement`, `servicerequest` — **has `reportcard`** |
| `SUPPORTED_TODO_PARENTS` (`Spaarke.UI.Components/hooks/toolbarLaunchDefaults.ts`) | 12 | `reportcard`, `servicerequest` — **has `agreement`** |
| `entity-schema.md` (`src/solutions/SpaarkeCore/entities/sprk_todo/`) | 11 | oldest of the three |
| **live `sprk_todo`** | **14** | — |

`TODO_REGARDING_CATALOG`'s own comment says it "MUST stay in sync" with a third array
(`TODO_REGARDING_TARGETS` in `AssociateToStep/types.ts`), enforced by a unit test — so the sync
discipline exists between *those two* and does not reach `SUPPORTED_TODO_PARENTS`, which is how they
diverged. That is a CLAUDE.md §11 duplication finding on the client surface, **out of this task's
scope** (this task needs only the three scopeable roots) and recorded so it is not lost.

### 0.3 🔴 The display-name column is DIFFERENT for each of the three roots

This is the finding that would actually have broken the create had it been pattern-matched. Queried
via `EntityDefinitions?$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute`
plus a per-entity attribute listing:

| Root | Entity set | `PrimaryNameAttribute` | **Human display name column used for `sprk_regardingrecordname`** |
|---|---|---|---|
| `sprk_project` | `sprk_projects` | `sprk_projectnumber` | **`sprk_projectname`** |
| `sprk_matter` | `sprk_matters` | `sprk_matternumber` | **`sprk_mattername`** |
| `sprk_workassignment` | `sprk_workassignments` | `sprk_name` | **`sprk_name`** |

Three different column names, and **the primary-name attribute is not the display name** for two of
the three. `sprk_matter` has NO `sprk_name`; `sprk_workassignment` has NO `sprk_workassignmentname`.
Deriving either by convention fails on two of three roots. The existing code already uses
`sprk_projectname` for project (correct), which is why nothing was broken before.

Each column was then verified with **a query that SUCCEEDS**, per this project's standing rule
(never infer presence from a family listing):

```
OK   sprk_projects        [sprk_projectid,sprk_projectname]
OK   sprk_matters         [sprk_matterid,sprk_mattername]
OK   sprk_workassignments [sprk_workassignmentid,sprk_name]
OK   sprk_todos           [sprk_todoid,sprk_name,_sprk_regardingproject_value,
                           _sprk_regardingmatter_value,_sprk_regardingworkassignment_value]
```

---

## 1. 🔴 The POML's load-bearing constraint is STALE — the level asymmetry no longer exists

The POML's longest constraint frames the central decision of this task:

> "the matter and work-assignment accessible sets are bare `IReadOnlySet<Guid>` with NO level
> anywhere in the pipeline, so membership would imply create … Either follow 009's precedent
> (membership implies create) or block create on the levelless roots."

**Both branches are obsolete.** `CallerPrincipal` now carries
`MatterAccess`/`WorkAssignmentAccess` as `IReadOnlyDictionary<Guid, AccessRights>` — full rights per
record for all three roots — with `GetMatterRights` / `GetWorkAssignmentRights` accessors. The id
sets are *derived from* the rights maps, not the other way round.

Tasks **032 + 033** (FR-19 / register A-8) closed this. The `UpdateTodo` handler already says so at
the decision site:

> "✅ THE ASYMMETRY IS GONE … There is no root type left for which membership implies write."

**Consequence for this task**: the create-side level question has a third answer, strictly better
than either the POML offered — **gate all three roots identically on `AccessRights.Create`**, using
the matching accessor. No precedent to follow, no root to block. Implemented and commented at the
decision site per the constraint's requirement that the choice be explicit.

⚠️ This is the **fifth** stale POML premise this project has hit in two sessions (061 ×2, 025, M3,
now 029). The pattern is identical every time: **the code/design moved and the task file did not.**
The POML also cites its comment against `ExternalAccessModule.cs:189-195` describing matter/WA
dimensions as levelless — the module code is fine, only the constraint's *inference* from it was.

---

## 2. Read-half placement decision (step 1) — **sibling routes, not a registered module**

Written before implementing, as the acceptance criteria require.

The POML's own framing: *"A registered module is the reuse answer; bespoke routes are justified only
if the SPA's To Do view genuinely cannot consume the generic plane."* The bespoke answer is correct
here, for three reasons in descending weight:

1. **The generic scoped-fetch plane has no create path at all.** `ExternalModuleDataEndpoints` +
   `Tier2ScopeFilterInjector` serve *reads* (FetchXML with an injected `<filter type='or'>`). A
   registered `todos` module could carry the read half and could never carry the create half. That
   would split the two halves of one parity fix across two planes with two different authorization
   mechanisms — strictly worse coherence than either plane alone, and precisely the kind of surface
   fragmentation CLAUDE.md §11 exists to prevent.
2. **The module route would require a shipped-client change; sibling routes do not.** The external
   SPA reads to-dos through `getProjectTodos()` → `GET /api/v1/external/projects/{id}/todos`
   (`web-api-client.ts:473`, consumed by `SmartTodo.tsx`). Moving the read to the generic plane
   changes what a **shipped** client (SPA-r1/r2) must call — the POML's *second* escalation trigger.
   Additive sibling routes leave every shipped route byte-identical.
3. **Sibling routes match the file's live convention exactly.** Every one of the 13 routes in this
   group is root-typed in the path (`/projects/{id}/documents`, `/projects/{id}/events`, …). The new
   routes are the same shape with a different root segment — a third instance of an established
   pattern, which is what the POML asked for.

**What was added** — 4 routes, purely additive, no shipped route touched:

| Route | Handler |
|---|---|
| `GET  /api/v1/external/matters/{id}/todos` | shared `ListTodosForRoot(Matter, …)` |
| `POST /api/v1/external/matters/{id}/todos` | shared `CreateTodoForRoot(Matter, …)` |
| `GET  /api/v1/external/workassignments/{id}/todos` | shared `ListTodosForRoot(WorkAssignment, …)` |
| `POST /api/v1/external/workassignments/{id}/todos` | shared `CreateTodoForRoot(WorkAssignment, …)` |

The existing `/projects/{id}/todos` GET + POST now delegate to the *same* two shared handlers with
`TodoRootKind.Project`, so there is **one** implementation of list-a-to-do and **one** of
create-a-to-do, not three of each. That is the §11 answer for the handler layer.

**Client note for the PR**: the external SPA does not yet call the new routes. This task delivers
server capability and does not change the SPA — the same order in which the project routes shipped.

---

## 3. §11 component justification

| Question | Answer |
|---|---|
| **Existing** | `ExternalDataService.ApplyResolverFieldsAsync` is already entity-generic (takes `regardingEntityName`, resolves `sprk_recordtype_ref` dynamically) — reused **unchanged**. The three-dimension OR'd scope pattern exists in the `documents`/`invoices` module descriptors. `TodoRegardingBuilder.RegardingLookupByEntity` owns entity→lookup-attribute on the **SDK** path. |
| **Extension** | Handlers: the three list routes and the three create routes collapse onto **one** shared implementation each. The root→rights switch that already existed inline in `UpdateTodo` is hoisted to one helper (`RightsForRoot`) and `UpdateTodo` now calls it — removing the drift risk its own comment warned about. |
| **New surface** | Exactly one: `TodoRootBinding` + `TryGetRootBinding` — root kind → (entity, entity set, nav property, lookup attribute, scope-filter attribute, display-name column). |
| **Why not extend `TodoRegardingBuilder`** | It is keyed by entity logical name and carries only the lookup attribute, because the SDK path binds via `EntityReference` and never needs a navigation property or an entity-set name. Adding Web-API-only fields to an SDK-path map couples two planes that have no reason to move together. The Web API path previously had these names as **string literals inline**; this makes them one table instead of scattered literals — a reduction, not a third copy. |
| **Cost of doing nothing** | The plane's write surface stays wider than its read surface: a caller may PATCH a matter- or work-assignment-parented to-do that the same plane refuses to list and cannot create. |

---

## 4. What was built

### 4.1 Data layer — `ExternalDataService`

| Change | Why |
|---|---|
| `TodoRootBinding` + `RootBindings` + `TryGetRootBinding` | One table for the six names per root that cannot be derived (§0.1, §0.3). Adding the fourth root (task 028) is one row. |
| `GetTodosAsync(TodoRootKind, Guid)` — was `(Guid projectId)` | The `$filter` attribute now comes from the binding, so read and write cannot name different columns for the same root. |
| `BuildTodoListUrl` — **new, pure, `internal static`** | Where the widening actually lives, so it can be asserted directly. See §5. |
| `CreateTodoAsync(TodoRootKind, Guid, …)`, now **`virtual`** | Root-parameterized; virtual so a deny test can assert the create never reached Dataverse. |
| `BuildTodoCreatePayload` — **new, pure** (supersedes the private `ApplyResolverFieldsAsync`) | See §4.3 — this is the change that made two of the five perturbations meaningful. |
| `AssertSingleRegardingLookup` — **new, pure** | ADR-024 one-parent enforced on the Web API path; it was only enforced on the SDK path before. |
| `GetRootDisplayNameAsync` — **new, private** | Reads the root's display column, which differs per root. Replaces the project-only `GetProjectByIdAsync` call. |
| Lookup count corrected 13 → **14**, with its as-of date | §0.2. |

### 4.2 Endpoint layer — `ExternalProjectDataEndpoints`

Four additive routes; **no shipped route's shape changed**. Six route handlers, but only **one**
implementation of list and **one** of create (`ListTodosForRoot` / `CreateTodoForRoot`) — three
copies of an authorization sequence is how the read and write planes drifted apart to begin with.

`RightsForRoot` was **hoisted out of `UpdateTodo`** and is now shared by all three verbs. Its own
comment had warned that "a future root type cannot be added to the scope branch while being
forgotten in the rights branch"; three copies of that switch, one per verb, would have reintroduced
exactly that risk one level up.

### 4.3 🔴 A structural change made specifically so two perturbations could bite

`ApplyResolverFieldsAsync` was **not wrong** — it was already entity-generic, exactly as the POML
said. The problem was **where it ran**: inside `CreateTodoAsync`, which every endpoint test
substitutes. So the two questions "which entity do the resolver fields name?" and "did more than one
parent lookup get set?" were unanswerable by any test, and perturbations (d) and (e) would each have
failed **ZERO** tests.

That is the exact failure the POML's review constraint names — *mocking at a seam proves the CALLER,
never the CALLEE* — and the constraint's instruction is to **fix the test level, never drop the
perturbation**. Here the fix had to be structural, because no test at any level could see inside a
substituted method:

- the three synchronous resolver fields moved into the pure `BuildTodoCreatePayload`, driven by
  `binding.EntityLogicalName`;
- the one async step (`ResolveRecordTypeRefAsync`) is now called by `CreateTodoAsync` and its result
  passed **in** as a value;
- `AssertSingleRegardingLookup` runs at the end of the pure builder.

Same payload, same ADR-024 atomicity, same non-fatal behaviour when `sprk_recordtype_ref` is
missing — but every decision is now on a path a test can call. Perturbations (d) and (e) then failed
2 and 6 tests respectively. **This is the generalisable lesson: when a perturbation cannot fail,
the answer is sometimes to move the decision, not to add a test.**

---

## 5. The create-side level decision (POML step 4)

**Decision: all three roots are gated identically on `AccessRights.Create`.**
Commented at the decision site in `CreateTodoForRoot`, as the constraint requires — not only here.

The POML offered two branches, both resting on the premise that matter/WA access carries no level.
§1 shows that premise died with tasks 032+033. The implementable answer is now the one the POML did
not list, and it is strictly better than either: a ViewOnly matter participant cannot create, which
is the same answer a ViewOnly project participant has always received. **No root type is left for
which membership implies anything.**

The list gate is `AccessRights.Read`. For a project that is *exactly* the `HasProjectAccess`
membership test it replaces — every participation carries a level and the lowest (ViewOnly) already
maps to `Read` — so no caller who could list project to-dos before can be denied now. Behaviour
preserved, uniformity gained.

---

## 6. Perturbation results (POML step 6 — mandatory)

Each perturbation applied alone, built, `ExternalTodoScopeTests` run, then reverted. **58 tests
green before and after.**

| # | Perturbation | Tests failed |
|---|---|---|
| a | `GetTodosAsync`'s filter reverted to `_sprk_regardingproject_value` for every root | **3** |
| b′ | Create-side gate bypassed | **5** |
| c | Matter dimension cross-wired to the work-assignment set | **5** |
| d | `sprk_project` stamped into every create's resolver fields | **2** |
| e | A second parent lookup allowed into the create payload | **6** |

**None was zero** — but two of them only because of the §4.3 restructure, and one had to be reshaped
to compile:

⚠️ **(b) did not compile as first written.** Replacing the guard with `if (false)` produced
unreachable code, which is an error under `--warnaserror`. Reshaped to
`!rights.HasFlag(AccessRights.None)` — always true, so the gate never denies, and `rights` stays
used. (Fittingly, `HasFlag(None)` is the exact fail-open this project caught in task 033.) Task 009
hit the same compile-vs-perturb problem in this same file pair; **the compiler enforcing part of a
contract is worth knowing, but it is not test coverage**, so the perturbation is reshaped to compile
and then counted, never counted as "the compiler caught it".

**Every membership check has a negative test**, per task 009's hardest-won lesson (its work-assignment
check had a positive test only, so deleting it was free). Here each of the three roots has a positive
and a negative on **both** list and create, plus a cross-root test proving that holding a GUID as a
matter does not admit it as a work assignment.

---

## 7. What of the owner's intent remains UNIMPLEMENTED

The owner's stated intent is that a To Do be associable to **any** regarding entity, with the parent
flowing from the creation context. This task delivers that for **three of fourteen** parents. Stated
plainly, per the POML's third escalation trigger — these were **not** invented here:

| Parent | Status | What it needs |
|---|---|---|
| project, matter, work assignment | ✅ **Delivered** — list, create, update | — |
| **service request** | ❌ Not delivered | **Task 028** — `CallerPrincipal` has no `ServiceRequestAccess` set. The model names four core types; the principal carries three. Once 028 lands, this task's work is one `TodoRootBinding` row + one `RightsForRoot` arm + two routes. |
| the other **ten** (agreement, analysis, budget, communication, contact, document, event, invoice, organization, reportcard) | ❌ Not delivered, and **not a code change** | No accessible set exists for any of them, and inventing one is an authorization-model decision, not an implementation detail. Note several are *children*, not roots — a to-do parented to a document should almost certainly inherit that document's own root rather than acquire an accessible set of its own. **That is a design question for the child-inheritance work (054/055/056), not a gap to be closed by adding routes.** |

Also recorded, not fixed (outside this task's surface):

- `TODO_REGARDING_CATALOG` (client, `Spaarke.UI.Components`) lists **12** targets and
  `src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md` lists **11**. Live is **14**. Both
  are short, and neither includes `sprk_regardingagreement`.
- The external SPA does not call the four new routes. Server capability shipped first, as it did for
  the project routes.

---

## 8. Code review (Step 9.5) — two findings in my own code, both fixed/filed

### 8.1 🔴 FIXED — the ADR-024 one-parent guard was case-SENSITIVELY evadable

`AssertSingleRegardingLookup` matched `sprk_Regarding*@odata.bind` with `StringComparison.Ordinal`.
Dataverse requires the PascalCase navigation property, so a case-sensitive match *looks* right — but
the guard's whole job is to catch a **wrong** second bind, and a wrongly-cased one is exactly the
wrong bind most likely to appear. It would have been waved straight through.

Not hypothetical: `spaarke-daily-update-service-r5`'s bind audit found a lowercase
`sprk_regardingproject@odata.bind` **in this very file** (`:270`, since fixed), and lowercase
`sprk_regarding*@odata.bind` keys still appear in client code today
(`TodoRegardingUpdateBuilder.test.ts`, `useSprkMemoRepository.test.ts`).

Fixed to `OrdinalIgnoreCase`, with the reasoning at the code and a dedicated test
(`AssertSingleRegardingLookup_WhenTheSecondParentIsWronglyCased_StillThrows`). **Detect broadly; let
Dataverse reject the casing.** A validator that is stricter about form than the bug it hunts will
miss the bug.

### 8.2 📋 FILED, not fixed — `$top=200` with no `@odata.nextLink` (**ISS-002 / #963**)

`GetCollectionAsync` returns `result?.Value` and never reads `@odata.nextLink`; four call sites pin
`$top=200`. A matter with 250 to-dos renders 200 with no truncation signal. Pre-existing — but task
029 widened one of those four call sites from one root to three, so the blast radius grew, and
widening it silently would have been the wrong call.

Same **honesty** class as task 024 (an incomplete result presented as complete), different plane —
024 is SPE, this is Dataverse. Out of scope here; filed with entry points and the warning that the
in-repo paging precedent carries its own bug (#962).

### 8.3 Reviewed and deliberately accepted

| Observation | Disposition |
|---|---|
| `ExternalDataService.cs` grew 983 → 1,206 lines, complexity est. 108 → 131 | **Accepted.** Per §11.5 / `COMPONENT-COMPLEXITY.md` the LOC ratchet was retired 2026-08-20; the test is cohesion, not size. This file has one reason to change (the external plane's Dataverse queries). Most of the growth is XML doc, and it now holds *fewer* scattered string literals than before. |
| The project create's deny message changed from *"…on this project"* to *"…on this record"*, and out-of-scope now shares it with insufficient-rights | **Accepted, deliberate.** Status code unchanged (403); no test or client branches on the text. Restoring a separate scope check purely to preserve wording would rebuild the redundant-guard shape task 009 removed — two guards denying the same case, where deleting either is invisible. `UpdateTodo` already collapses them for exactly this reason, and perturbation (b) confirms the single check is covered (5 tests). |
| `UpdateTodo` now passes `rootId ?? Guid.Empty` where it previously used `rootId!.Value` | **Accepted, an improvement.** `GetTodoRootAsync` guarantees non-null for the three real kinds. If that contract were ever broken, the old code threw (500); the new code denies (403). Fail closed beats fail loud on an authorization path. |
| Two `is null` checks added | **Not AI smells.** Both are on genuinely nullable types (`(Guid,string)?` and `TRow?`). |
| No new interface, DI registration, or package | Confirmed by diff — nothing to justify under ADR-010. |
| Comment density is high | **Matches the file.** This surface's house style is heavy explanatory comment, and the project's own recurring failure is *stale prose*, not absent prose — so every load-bearing comment added here carries its as-of date or its evidence. |

### 8.4 ⚠️ A FOURTH publish-size hazard, found the hard way

CLAUDE.md §10 names two hazards (ageing baseline, zip tool); session 4 added a third (build
environment). This is a **fourth, specific to the procedure §10 itself prescribes**:

**Building the fresh master worktree under a deep path fails silently-ish on Windows.** The first
attempt put the worktrees in the session scratchpad, making the real path
`C:\Users\RalphSchroeder\AppData\Local\Temp\claude\…\scratchpad\wt-master\src\server\api\Sprk.Bff.Api\Services\Ai\Chat\Playbooks\summarize-document-for-workspace.playbook.json`
— **262 characters, over `MAX_PATH`**. MSBuild reports it as:

```
error MSB3030: Could not copy the file "…summarize-document-for-workspace.playbook.json"
because it was not found.
```

**The file exists.** It is in master, and it was on disk in the worktree — the message says "not
found" for a file that is merely unreachable. The danger is the failure *mode*: a publish that dies
this way leaves no output, and a publish that dropped only *some* files would zip **smaller** and
read as a size WIN. Re-run at `C:\wt029m` / `C:\wt029b` and it publishes 214 files cleanly.

**Recommended addition to §10's procedure: use a SHORT worktree path (`C:\wt-<tag>`), and assert the
published file COUNT, not just the zip size.** Not applied to root `CLAUDE.md` — same standing as the
third hazard, which is still awaiting owner sign-off (hot-path file + `.claude/CHANGELOG.md` entry).

*(Second-order note: `C:\` itself is not writable for the zip — write the archive elsewhere.)*

---

## 9. §10 BFF placement justification (for the PR)

Per [`.claude/constraints/bff-extensions.md`](../../../.claude/constraints/bff-extensions.md), stated
explicitly even though the answer is "in the BFF":

**In the BFF.** These four routes are the external-access data plane — the same route group, the same
`CallerPrincipalAuthorizationFilter`, the same app-only broker reads (NFR-02: no OBO, no Graph SDK,
no AI-internal types). The BFF filter **is** the entire security boundary for this surface, since
these reads are app-only and Dataverse row-level security is inert on them. Placing to-do scoping
anywhere else would put an authorization decision outside the only place that can make it. No new
package, no new DI registration, no new background work — one new `internal` record and its table,
inside an existing service.

