# Task 003 — rights-level decisions per operation key

> **Date**: 2026-08-21 · **Spec**: FR-03 · **Findings closed**: A-3, A-20 (policy-key half)
> **File changed**: `src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs`
> The Read-ceiling half of A-20 is **task 005**; these endpoints stay functionally 403 for Write+
> callers until 004/005 land. This task guarantees key resolution and least-privilege requirements only.

---

## 1. The four keys

Rights are least-privilege **against the resource the filter actually authorizes** — which is not always
the record being written. That distinction did the real work here.

| Operation | Rights | Authorized resource | Reasoning |
|---|---|---|---|
| `read` | `Read` | the document | Read-only routes: eml-render, `DataverseDocumentsEndpoints.cs:443`, `ChatDocumentEndpoints.cs:915` |
| `finance.read` | `Read` | matter / document / invoice id (`ExtractResourceId`) | Group-level filter on `/api/finance` plus its GET routes |
| `finance.confirm` | `Write` | the document | Both `/invoice-review/confirm` and `/invoice-review/reject` mutate the **document's own** status (→ Confirmed / RejectedNotInvoice). **Deliberately not `Create`**: confirm also creates an `sprk_invoice`, but that is a *different entity* than the one authorized — requiring `Create` on the document would over-restrict |
| `entity.associate_document` | `AppendTo` | the **target entity** (`"{EntityType}:{EntityId}"`, e.g. a matter) | The operation attaches a document **to** the target. In Dataverse that is `AppendTo` ("other records can be attached to this record"). **Not `Write`**: saving an email to a matter does not modify the matter's own fields |

The task POML suggested "Write-bearing" for the last two. `finance.confirm` → `Write` matches that.
`entity.associate_document` → `AppendTo` is a deliberate narrowing to the semantically correct
Dataverse right, and is the one judgment call in this task (see §2).

## 2. ⚠️ Obligation this creates on task 005

`AppendTo` is the **first use of that flag** in `OperationAccessPolicy` — no prior entry uses `Append`
or `AppendTo`. Because `DataverseAccessDataSource.QueryUserPermissionsAsync:305-379` currently returns
at most `AccessRights.Read` (the A-20 Read ceiling), this key is unsatisfiable today. That is expected
and shared with every other Write+ operation.

**The risk is specific**: if task 005 lifts the ceiling by mapping only
`Read`/`Write`/`Delete`/`Create`/`Share` and skips `AppendToAccess`, then `POST /api/office/save`
stays **permanently 403** — and it would look like a fixed endpoint rather than a broken one, because
the operation resolves and the denial reads as a legitimate `insufficient_rights`. That is a silent
failure, which is worse than the loud one it replaced.

**Task 005 MUST map Dataverse `AppendToAccess` → `AccessRights.AppendTo`.** Recorded as an explicit
`<constraint source="task-003">` on task 005's POML, and called out inline in
`OperationAccessPolicy.cs`.

Alternative considered and rejected: assign `Write` instead, which task 005 will certainly map.
Rejected because it authorizes the wrong thing — a caller with Write but no AppendTo on a matter would
be allowed to attach documents to it, and a caller with AppendTo but no Write would be denied. Choosing
the wrong right to dodge a downstream mapping risk trades a correctness bug for a scheduling
convenience. The obligation is the better instrument.

## 3. The completeness test (the forcing function)

`tests/integration/auth/UnifiedAccessControl/OperationAccessPolicyCompletenessTests.cs` — 15 tests.

`OperationAccessRule.EvaluateAsync:35-46` denies any unregistered operation. That is correct
fail-closed design, but it means a new call-site with an unregistered string is a **silent
unconditional 403**: no compile error, no startup failure, no test failure. A-3 and A-20 were found by
hand-enumerating call-sites; this test exists so that is never necessary again — which is why it scans
source rather than snapshotting today's strings.

**Three call-site mechanisms are covered, because the four findings used all three:**

1. literal in `Add*AuthorizationFilter("op")` / `Add*AccessFilter("op")`
2. literal in `Operation = "op"` on an `AuthorizationContext`
3. **const-indirection** — `Operation = SomeConst` with the const declared in the same file. This is
   how `entity.associate_document` reaches the rule; a scan without it would have silently dropped
   that finding.

**Two anti-vacuity guards**, because a source scan that finds nothing passes trivially:

- `MinimumExpectedCallSites` (20) — fails if the scan stops reaching the API tree.
- `SourceScan_DiscoversKnownCallSiteOperation` — asserts the scan actually finds each of the four
  strings, so a regex that quietly stops matching fails loudly instead of reducing coverage.
- `ResolveRepoRoot()` **throws** rather than falling back to `AppContext.BaseDirectory` (the
  `tests/Spaarke.ArchTests` helper falls back; that fallback would make the scan find zero files).

Comment lines are excluded so documentation *examples* are not mistaken for call-sites — specifically
`FinanceAuthorizationFilter.cs:17`, whose `<param>` comment names `"finance.reject"`. **No route uses
that string** (the reject route uses `finance.confirm`), and a test asserts it stays unregistered:
registering unused strings is not the fix.

## 4. Sweep result — A-20's list confirmed, plus one new item

I re-derived the call-site list rather than trusting A-20. **22** `Add*Filter` extensions exist, but
only **7** reach `AuthorizeAsync`, and only those 7 consult `OperationAccessPolicy`:

| Filter | Operations | In policy? |
|---|---|---|
| `DocumentAuthorizationFilter` | `read` (2 sites) | ✅ now |
| `FinanceAuthorizationFilter` | `finance.read`, `finance.confirm` | ✅ now |
| `EntityAccessFilter` | `entity.associate_document` | ✅ now |
| `OfficeDocumentAccessFilter` | parameterized — **zero call-sites** | n/a (see below) |
| `AiAuthorizationFilter` | — | out of scope |
| `AnalysisAuthorizationFilter` | — | out of scope |
| `VisualizationAuthorizationFilter` | — | out of scope |

The three AI filters route through `IAiAuthorizationService`, which checks
`accessSnapshot.AccessRights.HasFlag(AccessRights.Read)` directly (`AiAuthorizationService.cs:176-183`)
and never consults this policy. `DataverseAuthorizationFilter` uses `IDataversePrivilegeChecker`. Both
correctly out of scope. `ResourceAccessHandler`'s `ResourceAccessRequirement` operations were all
already registered.

**A-20's list is confirmed complete for active call-sites.**

### 🆕 New finding — `AddOfficeDocumentAccessFilter` is orphaned

`OfficeDocumentAccessFilter.cs:22` defines the extension, it uses the core `AuthorizationService` with
a caller-supplied operation, and its doc comment offers `"share"` / `"attach"` as examples — **neither
is a policy key**. But grep finds **zero call-sites**: it is attached to no route.

So it is not an active always-deny; it is dead code, the same class as **A-15**
(`AccessibleRecordSetAuthorizationFilter`, also defined and never attached). Severity **Low**.

**Recommendation**: fold into **task 018**, which already owns A-15's deletion. Either delete this
filter too, or attach it with a registered operation — leaving a filter that would 403-unconditionally
if anyone wired it up is a trap for the next author. The completeness test does **not** catch this case
by design: with no call-site there is no operation string to discover. Deleting it removes the trap;
the completeness test then covers it automatically the moment it is ever attached.

Filed as register item **A-23**.
