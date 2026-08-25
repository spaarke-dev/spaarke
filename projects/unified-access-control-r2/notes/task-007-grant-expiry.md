# Task 007 — grant expiry: making a promise-shaped control real

> **Date**: 2026-08-23 · **Spec**: FR-06 · **Finding**: A-5 (High) · **Register**: D-1 (enforce branch)

---

## 1. What was wrong

`sprk_expiresdate` was **written at grant time and read nowhere.** A repo-wide grep for the column
returned only write-side references (`GrantExternalAccessEndpoint.cs:460`) — no `$filter`, no `$select`,
no sweep job, nothing.

So a grant with an expiry date of last March conferred full access today. Worse than a missing feature:
the Manage Access UI presents expiry as a working control, so an operator who set "access until 30 June"
believed they had bounded the grant. They had not, and nothing anywhere would tell them.

## 2. The closed list of read paths (acceptance criterion 5)

Grep for the entity set across `src/server`, classified:

| # | Path | Conferring? | Predicate | Why |
|---|---|---|---|---|
| 1 | `ExternalParticipationService.QueryGrantSetAsync` | **YES** | ✅ added | The per-contact grant set — the primary enforcement read |
| 2 | `ExternalParticipationService.QueryOrganizationGrantRowsAsync` | **YES** | ✅ added | Org grants union into the same set. Leaving it off would let any contact keep expired access by holding it through their firm — the same finding wearing a different lookup |
| 3 | `ExternalDataService.GetProjectContactIdsAsync` | Display | ✅ added | Feeds `GetContactsAsync`, whose contract says "contacts with **active access**". Not enforcement, but a participant list that disagrees with the enforcement path is its own hazard: it tells an operator someone still has access when they do not, which is how a revocation gets skipped |
| 4 | `ExternalGrantLifecycle.QueryActiveRowsAsync` / `RetrieveRowAsync` | **NO — must not filter** | ❌ deliberately absent | Write-side lifecycle (grant upsert + revoke sweep, task 010). Adding expiry here would make **expired grants unrevokable** — the sweep would skip exactly the rows an operator is trying to clean up |
| 5 | `ProjectClosureEndpoint.BuildActiveProjectGrantsFilter` | **NO — must not filter** | ❌ deliberately absent | Cascade-revocation sweep. Same reasoning as #4; also scoped to task 016 |
| 6 | `AccessibleRecordSetService` | Consumer | inherits | Holds no query of its own — it consumes `ExternalParticipationService`, so it inherits the fix |

Rows 4 and 5 are the interesting half. "Add the predicate everywhere" would have been the obvious
reading of the constraint and would have introduced a new defect: revocation must see expired rows
precisely *because* they are expired.

## 3. The predicate

```
(sprk_expiresdate eq null or sprk_expiresdate ge {today:yyyy-MM-dd})
```

Three decisions inside one line.

### `sprk_expiresdate` is DATE ONLY — verified, not assumed

The task's escalation trigger required checking the live logical name rather than trusting docs
(design-register §G records schema docs in this area as stale). Live metadata:

```
sprk_expiresdate  DATE ONLY
```

Name confirmed → **trigger did not fire**. But the *type* was new information, and it changes the
predicate.

### `ge`, not `gt` — a deliberate deviation from the POML

The POML prescribes `(sprk_expiresdate eq null or sprk_expiresdate gt {utcNow})`, written on the
assumption of a datetime column. Against a **Date Only** column that is wrong in two ways:

- **`{utcNow}` as a timestamp.** A datetime literal against a Date Only column risks a 400 — and a 400
  on this query returns an *empty grant set*, i.e. a total access outage that surfaces as "the user can
  suddenly see nothing" rather than as an error. The comparison must be a bare `yyyy-MM-dd`.
- **`gt` shortens every dated grant by a day.** With no time component, `gt today` kills a grant at
  00:00 on its own expiry date. "Access until 30 June" means 30 June works — that is what the person
  who typed the date meant. `gt` would silently retire every dated grant in the system one day early.

`ge` still satisfies FR-06, whose acceptance is about an expiry **in the past**; the expiry date itself
is not in the past. Pinned by `ExpiryPredicate_OnTheExpiryDateItself_StillConfersAccess`, which fails if
anyone switches the operator back.

Recorded as a deviation from a `prescriptive` step, on new evidence the POML author did not have.

### The null branch is load-bearing

In OData, `field ge X` **excludes nulls**. Most grants have no expiry. Without `eq null` this predicate
would revoke every open-ended grant in the system — an outage, not an expiry bug, and one that would
look nothing like the change that caused it. Pinned by
`ExpiryPredicate_TreatsAGrantWithNoExpiryAsNeverExpiring`.

### Server-side, deliberately

Filtering after materialization would mean expired rows crossed the wire and any later path that forgot
to re-filter would see them. The predicate belongs where the set is defined.

## 4. Extraction before fix (task-001 obligation)

Task 001 could not pin A-5 because the queries were inline interpolations immediately before
`_httpClient.SendAsync` — observing the emitted `$filter` required intercepting the transport, and
`Mock<HttpMessageHandler>` is banned (ADR-038 §7 ban B1).

`BuildContactGrantFilter`, `BuildOrganizationGrantFilter`, `ExpiryPredicate` and `GrantRowSelect` are now
`internal static` pure members reachable via `InternalsVisibleTo("Sprk.Bff.Api.Tests")` — the convention
already used across this assembly. No reflection (ban B8), no transport mock (ban B1).

## 5. Escalation triggers — both evaluated

| Trigger | Fired? | Why |
|---|---|---|
| Live logical name differs from `sprk_expiresdate` | **No** | Verified against live Dataverse metadata; the name matches. The **type** (Date Only) differed from the POML's assumption and is handled in §3 |
| A read path cannot take a server-side predicate | **No** | All three conferring/display paths build OData `$filter` strings directly. No FetchXML composed elsewhere; nothing was filtered client-side |

## 6. Test coverage and its honest limit

`tests/integration/auth/UnifiedAccessControl/GrantExpiryCharacterizationTests.cs` — 11 tests.
(The POML named `tests/unit/Sprk.Bff.Api.Tests/AccessControl/…`, which is not a KEEP path and does not
exist — the fifth POML in this project with a wrong test path.)

**Verified by perturbation:**

| Perturbation | Result |
|---|---|
| Drop the predicate from the contact filter | **2 of 11 fail** |
| Drop the `eq null` branch | **1 of 11 fails** |
| `ge` → `gt` | **1 of 11 fails** — the boundary-day test |
| Ungroup the org disjunction (`(a or b)` → `a or b`) | **1 of 11 fails** |
| All restored | 11/11 |

That last one is worth keeping: without the brackets, the `and` terms bind only to the **last** org, and
every other organization's grants leak through unfiltered. Adding a term to a filter is exactly when
that breaks.

**The limit, stated plainly.** These assert the *query*, not Dataverse's evaluation of it. End-to-end
"an expired grant is absent from the caller's grant set" requires Dataverse to honour the filter, and
`ExternalParticipationService` talks to it through a raw `HttpClient` — so proving it offline would need
a transport mock (ban B1). Query-level assertion is the honest maximum here, and it is strong: it would
catch every realistic regression in this code. Live confirmation is filed on **task 034**, which has the
tenant.

## 7. Placement Justification (CLAUDE.md §10) + §11

No new components, no new services, no new packages, no background work. Four `internal static` members
extracted from inline code in a file that already existed, plus one shared call from `ExternalDataService`
so the display path cannot drift from the enforcement path.

**§11**: not applicable — this task adds no new surface. It modifies existing read paths.

**Publish**: 43.69 MB compressed incl. PDBs (unchanged; baseline 44.96, ceiling 60). No vulnerable
packages. Full suite **10,726 passed / 0 failed** · ArchTests 36/36 · Core 45/45.

## 8. Deliberately NOT done

- **No sweep/deactivation job** — register D-1's minimum is read-side enforcement; expired rows remain
  in the table as inactive-by-read. Phase 5 attestation will see them.
- **The UI expiry input stays** — FR-06's "if deferred, remove the input" branch does not apply, because
  this task took the enforce branch. The control now does what it says.

## 9. Follow-on obligations

| # | Obligation | Owner |
|---|---|---|
| 1 | Confirm against a live tenant that the Date Only comparison behaves as expected (an expired grant disappears from `/api/v1/external/me`, a grant expiring today still works, an open-ended grant is unaffected). The offline tests assert the query, not Dataverse's evaluation of it | **task 034** |
| 2 | Expired rows accumulate. If a proactive sweep is ever wanted it is separate authorized work — and it MUST read the grant table WITHOUT the expiry predicate, or it will skip exactly the rows it exists to clean up (see §2 rows 4–5) | owner decision |
| 3 | `sprk_granteddate` is also Date Only and also read nowhere. Not a security issue (it confers nothing), but the same write-only pattern — worth a glance during attestation | Phase 5 |
