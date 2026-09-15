# Task 030 — ADR-003 Amendment A1: decisions, deviations, and what was verified

> **Completed** 2026-09-04. Path **B** amendment per root CLAUDE.md §6.5.
> Outputs: `.claude/adr/ADR-003-authorization-seams.md` (rewritten), `docs/adr/ADR-003-lean-authorization-seams.md` (Amendment A1 added), `.claude/CHANGELOG.md` (entry).

---

## 1. The escalation trigger was evaluated and did NOT fire

The POML carries this trigger:

> *"If, while drafting, a still-live consumer is found that genuinely depends on a retired rule (e.g.
> code registered as `IAuthorizationRule` that the amendment would orphan), STOP and escalate."*

**A live consumer exists.** `OperationAccessRule`
(`src/server/shared/Spaarke.Core/Auth/Rules/OperationAccessRule.cs:20`) is the **only**
`IAuthorizationRule` implementation in the repository, and it **is** registered —
`src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs:96`:

```csharp
services.AddScoped<IAuthorizationRule, OperationAccessRule>();
```

**But the amendment does not orphan it**, so the trigger's condition is not met. The retired rule is
*"new auth logic **MUST** be an `IAuthorizationRule`"* — a mandate on the **shape of future work**.
Retiring a mandate is not the same as deprecating the mechanism: the rule chain, the interface, and
`OperationAccessRule` itself all remain sanctioned and registered. Nothing that exists stops working,
and nothing that exists is left without an ADR that permits it.

This distinction is written into **both** ADR versions ("What A1 does NOT do" / "`OperationAccessRule`
is NOT orphaned") specifically so a future reader cannot infer deprecation from the retirement.

**Why this was checked rather than assumed**: retiring a MUST that a live consumer depends on would be
an amendment that breaks running code — which is the one outcome path B exists to prevent.

## 2. Both drift claims were verified in source, not taken from the docs

The spec's ADR Tensions row asserted two divergences. This project has been wrong eleven times by
trusting its own prose (see `current-task.md`), so both were re-derived from code:

| Claim | Verification | Result |
|---|---|---|
| `CachedAccessDataSource` violates "per-request only" | `Infrastructure/Caching/CachedAccessDataSource.cs` — depends on **`IDistributedCache`**; `RolesTtl` / `TeamsTtl` = 2 min, `ResourceAccessTtl` = 60 s, applied via `AbsoluteExpirationRelativeToNow` | ✅ **Confirmed, and stronger than claimed** — `IDistributedCache` is cross-request *and* cross-instance, not merely cross-request |
| The external stack is a service layer, not a rule | `CallerPrincipalResolver` + `AccessibleRecordSetService` under `Infrastructure/ExternalAccess/**` | ✅ Confirmed |

## 3. Deviation — a MUST NOT I added, then removed (scope discipline)

While drafting the concise ADR I added:

> ~~**MUST NOT** let a client name the container/drive its bytes land in — the server resolves it from
> a record it has already authorized the caller against~~

**Removed before completion.** It is true, and it is an authorization rule, but it does not belong
here:

1. **Out of scope.** The POML constrains this task to retire *exactly four* named rules and add the
   evaluator contract. The container rule is in neither set and is not in the spec's ADR Tensions table.
2. **It already has a home** — ADR-049 plus the `SpeWriteSinkContainerProvenanceGuardTests` allow-list,
   which is *mechanically enforced*. Restating it in ADR-003 creates a second home for one rule
   (root CLAUDE.md §11: prefer one component that works well over two that overlap), and the two can
   then drift.
3. **It would have manufactured an ADR-003 violation owned by another task.** Two `ClientSupplied`
   sinks remain live in `Api/DocumentsEndpoints.cs` (task 083 rows 4 + 5). Adding the rule here would
   make `adr-check` flag ADR-003 against code whose remediation is tracked, owned, and sequenced
   elsewhere — noise that displaces signal.

Recorded rather than silently reverted, because "I widened the scope and then narrowed it back" is
exactly the kind of decision that is invisible in a diff.

## 4. Deviation — files touched outside acceptance-criterion 4's list

Criterion 4 says no file outside `.claude/adr/**`, `docs/adr/**`, `.claude/CHANGELOG.md` and this
project's `notes/` may be modified. Task bookkeeping requires two more:
`tasks/030-*.poml` (status → completed) and `tasks/TASK-INDEX.md` (🔲 → ✅), mandated by the
`task-execute` protocol Step 10 and root CLAUDE.md §7.

This is a **tension inside the task definition**, not a scope breach: criterion 4 is aimed at source
code, and the two protocols cannot both be satisfied literally. Bookkeeping updates were made; **no
source file was touched** — `git status` at completion showed exactly the three content files plus
bookkeeping.

## 5. Also fixed while in the file

Register **§G row 2** listed a dead link in the concise ADR:
`../patterns/auth/authorization-service.md` — the file does not exist. Repointed to
`../patterns/auth/uac-access-control.md`, which does. The other five outbound links were checked and
all resolve.

## 6. What this unblocks

Task **032** (the evaluator: `(recordId → rights)` with additive terms, highest-wins, then ordered
vetoes) can now land **under** an ADR that sanctions its shape rather than in violation of one. That
sequencing — amendment before dependent code — is the path-B requirement, and it is the reason this
task existed at all.
