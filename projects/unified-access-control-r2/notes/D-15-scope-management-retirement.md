# D-15 — `ScopeManagementService` is retired, and `sprk_navitem` is the favorites store

> **Status**: ✅ **DECIDED 2026-09-29** (owner, session 26). Two decisions recorded together because both
> came out of the `#229` re-cut.
> **Implemented by**: task 129 (deletion) · consumed by the future `#229` task C (favorites).

---

## 1. Why this note exists

Both decisions are the kind that get re-litigated because the *absence* of something is invisible. A year
from now someone will find a `#229` marker mentioning `sprk_userfavorite`, or wonder why there is a
`ScopeResolverService` but no `ScopeManagementService`, and the honest answer will not be in the code —
because the answer is "we deliberately removed it". This is that record.

---

## 2. Decision A — `ScopeManagementService` is DELETED, not implemented

### The decision

> **Owner, 2026-09-29:** *"ok delete; i don't know what this does so no plans to use/consume it; make sure
> that does not impact ScopeResolverService → AnalysisActionService, and that the removal is documented in
> case comes up again."*

### What it was

A service with **seven `#229` stub markers** — 35% of that issue's remaining work — that returned
in-memory placeholder data instead of querying Dataverse. It was on the list to be *implemented*.

### Why deleting was correct rather than lazy

**It had zero production consumers.** Verified 2026-09-29 by direct search, not inference: `ScopeManagementService`
and `IScopeManagementService` appeared in exactly **three** files under `src/` — the class, the interface,
and one DI registration line (`AnalysisServicesModule.cs:808`). Nothing injected it.

**It had been superseded.** `ScopeResolverService` does the same job against real Dataverse, and it does
not go through this service at all — it injects `AnalysisActionService` and `AnalysisSkillService`
**directly** (`ScopeResolverService.cs:19-20, 32-33`). Confirmed at the owner's explicit request:
`ScopeResolverService.cs` contains **no reference to ScopeManagement of any kind**, so the deletion
cannot affect that path.

Implementing it would have meant writing seven real Dataverse queries for code nothing calls.

### 🔴 The trap, and why "delete the class and its interface" was too coarse

The first attempt deleted `IScopeManagementService.cs` wholesale and **broke five live services**.

That file declared the dead interface *and* **13 types that are very much alive**:

| Kept | Consumed by |
|---|---|
| `CreateActionRequest` / `UpdateActionRequest` | `AnalysisActionService` |
| `CreateSkillRequest` / `UpdateSkillRequest` | `AnalysisSkillService` |
| `CreateKnowledgeRequest` / `UpdateKnowledgeRequest` | `AnalysisKnowledgeService` |
| `CreateToolRequest` / `UpdateToolRequest` · `CreateOutputRequest` / `UpdateOutputRequest` | the analysis scope surface |
| `AnalysisOutput` · `OutputFieldType` · `ScopeType` · `ScopeOwnershipException` | same |

They lived in that file only because the interface happened to share it. **A file name is not a
statement about what a file contains**, and "delete the interface" quietly meant "delete ten request
contracts" — caught by the compiler, but only because the compiler was asked before the commit.

### What was actually done

- **Deleted**: `Services/Ai/ScopeManagementService.cs` (the dead class — verified to contain no other
  public type), the `IScopeManagementService` interface declaration, the DI line, and
  `ScopeManagementServiceTests.cs` (**23 tests** pinning in-memory behaviour of a service with no callers).
- **Removed**: five defensive `Mock<IScopeManagementService>` registrations in `Spe.Integration.Tests`
  fixtures. These were not consumers — they registered a loose mock so the host could boot.
- **Kept and renamed**: the surviving contracts now live in `Services/Ai/AnalysisScopeContracts.cs`, whose
  name says what it holds. A header comment records the retirement and points here.

### If this comes up again

`#229` will still show seven markers in any stale copy of its body. They are gone, not pending. Do not
re-implement `ScopeManagementService`; if a genuine need for scope *management* appears, extend
`ScopeResolverService` / `AnalysisActionService`, which are the live, Dataverse-backed path.

---

## 3. Decision B — favorites are stored on `sprk_navitem`, not a new table

### The decision

> **Owner, 2026-09-29:** *"ok sprk_navitem"*

### The question

`OfficeService`'s `#229` stub for `GET /api/office/recent` names a **`sprk_userfavorite`** table. That
table **does not exist** — zero references repo-wide, absent from `docs/data-model/`. So the choice was
create it, or reuse something.

### Why `sprk_navitem` wins on design, not just on cost

`sprk_navitem` (`src/solutions/SpaarkeCore/entities/sprk_navitem/entity-schema.md`) already models both
halves of what that route needs:

| Requirement | `sprk_navitem` |
|---|---|
| Per-user isolation | **UserOwned** — and the schema states per-user isolation is a hard requirement (NFR-03), with Team ownership *deliberately* excluded |
| Favorites | `sprk_type` is a choice of **`history` / `pin`** — a pin *is* a favorite |
| Recents | the **same** table — `history` rows ordered by `sprk_lastvisited` |
| Dedupe / ranking | `sprk_visitcount` |
| Target addressing | `sprk_targetlogicalname` + `sprk_targetid` (+ `sprk_url` for raw links) |

The `/recent` route needs recents **and** favorites. A new favorites table would split one concept across
two tables while leaving recents needing separate storage anyway. CLAUDE.md §11 defaults to reuse; here
reuse is also simply the better model.

### ⚠️ One thing the implementer must check (not a blocker)

The schema documents a **30-day prune-on-write** retention (FR-06 / task 031). That is described for
*history* rows. **If the prune also removes `pin` rows, favorites would silently expire after a month** —
which would look like a sync bug, not a retention policy. Confirm the prune is scoped to
`sprk_type = history` before shipping favorites.

---

## 4. Consequence for `#229`

With Decision A, `#229` loses 35% of its remaining surface. The recommended re-cut stands:

| Task | Scope |
|---|---|
| ~~A~~ | ✅ **done** — delete `ScopeManagementService` (this note, task 129) |
| B | invoice handlers ×2 — real `IGenericEntityService` reads; no security coupling |
| C | `/search/documents` + `/recent` + the `#1023` gate — **uses `sprk_navitem` per Decision B** |
| D | `/share/links` + `/share/attach` + the `#1024` gate — the heaviest |
| E | `KnowledgeDeploymentService` persistence |

🔴 **The gate must land in the same change that wires real data.** `SearchEntitiesAsync` is the cautionary
example: task 026 implemented its query without security trimming, and that is precisely why `#1021`
existed. The arch guard states the rule at `RouteAuthorizationGuardTests.cs`.
