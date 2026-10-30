# Task 017 phase 2d-4: re-point email.draft

Branch `task/fm-017-d4`, created from `origin/task/fm-017` (e6c20bd51c). Rigor FULL (task 017).

## Outcome

1. **First pass: stopped at the brief's escalation trigger.** Option (a) would stop the handler writing the I-1 stamp into
   the create item. That changes the owner in one data state and an existing refusal in two others. The evidence is
   under "Why" below.
2. **Coordinator decision, 2026-10-30: option (a′).** The re-point is done: see "The re-point (option a′)" next.

## The re-point (option a′)

### What changed in `EmailDraftToolHandler`

- **A filed draft is created through the service.**
  - It calls the scoped `RecordCreationService.CreateAsync` with `RecordCreationPrincipal.ForCaller(_dataverse, oid)`.
  - The payload is `RecordCreationPayload.Mapped(mapped.Item)`.
  - The request sets `ServerSetLookupColumns = {sprk_sentby}` and `CorrelationId = DecisionId`.
- **The stamp stays in the item.** The handler keeps writing its `DeriveForHostAsync` stamp into the create item, as
  today. Consequences:
  - Step 5 asks AppendTo on the stamped root.
  - Step 7's owner question includes the stamped root.
  - Step 10 plans nothing missing.
- **What does not change:**
  - the owned or run-as-user choice: `IsFiledUnderAnOwnershipParent` and `OwnedChildWrite.AppliesTo`;
  - the sender: `sprk_sentby` = the caller, sent as a server-set lookup;
  - the run-as-user POST of an unfiled draft.
- **Fail closed:**
  - A refusal returns the tool error and creates nothing.
  - A fault in the service propagates to the handler's catch, which returns `INTERNAL_ERROR`.
  - A result with neither an id nor a refusal is `INTERNAL_ERROR`.
  - There is no fallback create.
- **Warnings.** The service's warnings are added to the tool result's warnings. They are new: the owned create returned
  none.
- **Constructor.** The `IRecordOwnershipResolver` and `IFieldMappingDataverseService` parameters are replaced by
  `RecordCreationService`.
  - The class is now `internal sealed`. A public class cannot have a public constructor that takes the internal service
    (CS0051).
  - Handlers are found by `assembly.GetTypes()`, which includes internal types, so DI still resolves it, as it resolves
    `OfficeService`, which is also internal.
  - 2d-3's `DataverseCreateRecordHandler` will hit the same constraint.

### Deviation: the handler still calls WhoAmI itself

This is a deviation from the acceptance criterion that callers "no longer resolve WhoAmI themselves".

- **Kept: the first WhoAmI (~:366).** It identifies the caller for the AppendTo check on the regarding record. That check
  must run before the app-only `DeriveForHostAsync`, or the derivation's answers would reveal whether the record exists
  (verifier round on #1612, F3). The same id is the sender: the service never sets `sprk_sentby`.
- **Deleted: the second WhoAmI (~:418-427).** It could not run, because "filed" implies a regarding, so the first WhoAmI
  always ran first. In its place a null caller throws `InvalidOperationException`. The catch turns that into
  `INTERNAL_ERROR`, and nothing is created.

### Error codes

Each refusal KIND keeps the tool code the owned create gave for it. The text is always the shared map's
(`RecordCreationProblemMap.ToProblemDetails(...).Detail`), so upstream text is never shown and a not-found or denied
record gets the one uniform body. This follows the Office precedent. The mapping is `EmailDraftToolHandler.RefusalCodeAndText`.

| Kind (service codes) | Tool `ErrorCode` | Today, through `OwnedChildWrite.CreateAsync` |
|---|---|---|
| `PrincipalUnresolved`, `Upstream` | the failure's own `ErrorCode` | `ClientFailure` → `MapClientError` (same code; the text was Dataverse's) |
| `NotFoundOrDenied`, `Forbidden` + `record_creation.denied`, `BlockedColumn` | `AccessDenied` | `Denied` → `AccessDenied` |
| `Invalid` (including `stamp_underivable`) | `VALIDATION_FAILED` | `Invalid` → `VALIDATION_FAILED` |
| `OwnerUnresolved` (resolver codes), other `Forbidden` codes | the code, carried | `OwnerRefusal.RefusalCode`, carried |
| `Unavailable` (including `stamp_unavailable`, `owner_unavailable`, `check_failed`), `Undecided`, `Conflict` | `INTERNAL_ERROR` | a fault → the catch → `INTERNAL_ERROR` |

- **Correction to the first-pass plan below.** That plan mapped `OwnerUnresolved` to `VALIDATION_FAILED`. The owned
  create CARRIED the resolver's code, so the code is carried, and the not-isolated test pins it.
- **Unreachable rows.** Two of these cannot occur for a communication, which is a reparentable child with no plan and no
  pre-write rule:
  - `OwnerUnresolved` from a secure-filing refusal: today `INTERNAL_ERROR`, now `NoOwnerSource` carried;
  - `Undecided`.

### Tests (KEEP paths)

**New file:** `tests/integration/data-mutation/RecordOwnership/EmailDraftRecordCreationTests.cs`. It is a partial of
`SecureChildOwnershipAiToolTests`, over that suite's real resolver world and app-only seam, and has 20 cases. The handler
and the service each read through `StampWorld`.

| Case | Owner | Sender | Code |
|---|---|---|---|
| A draft under an ordinary matter | child BU team | caller | created |
| A draft under an invoice under an ordinary matter (filed through its stamp) | child BU team | caller | created; the `sprk_RegardingMatter` stamp bind appears once in the create payload |
| A draft under an invoice (team-owned, ordinary BU) under a secure matter | `SecureNamedTeam` | caller | created |
| A draft under an invoice under a matter flagged secure but not isolated | none | none | `record_owner_secure_parent_not_isolated`; no app create, no user POST |
| Caller may AppendTo the invoice but not its matter | none | none | `AccessDenied`; nothing created |
| Unfiled draft | none (run-as-user, its creator) | none (no `sprk_SentBy` in the user's POST) | created as the user; no app create |
| The app-only create faults | none | none | `INTERNAL_ERROR`; no fault text; nothing created; no user POST |

It also has refusal-map theories:
- 12 kind/code rows: the tool code, and the text equal to the shared map's;
- 2 rows for `PrincipalUnresolved` and `Upstream`: the failure's own code, without its upstream text.

**Changes to existing files: construction sites only.**
- `SecureChildOwnershipAiToolTests.cs` (2) and `.RegardingPair.cs` (2) now pass `DraftCreation()` in place of
  `_world.Resolver(), _appOnly.Object`.
- `EmailDraftToolHandlerTests.cs` and `EmailDraftToolHandlerPerItemToolsTests.cs` pass `RouteRecordCreation.Over(...)`
  over the same two doubles. They existed already, so this is no new test under the unit path.
- `RegardingPair`'s control test (`EmailDraft_RegardingAnAppendableInvoice_DerivesItsAncestry_AndIsCreated`) gives the
  service's step 10 a `StampWorld` holding the same invoice the handler reads. In production one Dataverse answers both.
  The empty default world answered "not found", which is `stamp_underivable`.

No assertion changed.

**Must-fire evidence:**
- **Option (a)**, stamps left out of the item (`ancestorStamps.Take(0)`): the secure-matter, not-isolated and
  root-AppendTo tests fail (3 of 77). The ordinary-invoice payload test still passes, because the service's step 10 then
  binds the stamp itself.
- **`OwnerUnresolved` mapped to `VALIDATION_FAILED`, and no sender:** 7 fail.
  - The two `OwnerUnresolved` theory rows fail.
  - The not-isolated test fails.
  - The four sender tests fail; one of them is the existing `EmailDraft_RegardingASecureMatter_…`.
- With the code restored, all 77 `EmailDraft*` tests pass.

### ArchTests: owner census

`RecordOwnerAssignmentCensusTests.EveryUnscannedWriterRoutesThroughTheResolver` failed. Its `ResolverCall` pattern knew
only `OwnedChildWrite.CreateAsync`, so it reported "EmailDraftToolHandler.cs: no IRecordOwnershipResolver call". The
route passed only because it still calls `OwnedChildWrite.RefileAsync`.

- **Fix:** `ResolverCall` also accepts `_?recordCreation.CreateAsync(`, the scoped service's create. That service is
  itself an unscanned writer whose step 7 calls the resolver.
- **Controls:** a new fact, `TheUnifiedCreateCountsAsAResolverCall`.
  - must fire: `_recordCreation.CreateAsync(...)` and `recordCreation.CreateAsync(...)`;
  - must not fire: `_entities.CreateAsync(...)` and `myrecordCreation.CreateAsync(...)`.
- The email.draft census entry's description now names the service.
- **2d-3 and 2d-5** get this for free when their handler holds the service as `_recordCreation`. 2d-5 deletes
  `OwnedChildWrite.CreateAsync`, so it can then drop that alternative from the pattern.

### Verification

- Targeted run (`EmailDraft*`, `SecureChildOwnershipAiTool*`, `Communication*`): 2706 passed, 0 failed, 2 skipped.
- `verify-suites.sh` (log directory: this worktree's `.verify-logs`, not committed):
  - solution build: 0 warnings, 0 errors;
  - suites: 6239 passed, 0 failed, 7 skipped;
  - ArchTests: 1 failure, the census failure above.
- After the census fix: ArchTests 1859 passed, 0 failed.

## Why: the service decides the owner and the AppendTo checks over the item, not over step 10's stamp

- Step 10 (`RecordCreationService.StampBeforeCreateAsync`, 2d-1) binds the derived stamp into the create body (`fields`)
  only. By design, the item that step 5 (the caller checks) and step 7 (the owner) read stays unchanged.
- Today, `EmailDraftToolHandler.BuildCommunicationItem` writes the `DeriveForHostAsync` stamps into the item. As a result,
  `OwnedChildWrite.CreateAsync`:
  - asks the caller's AppendTo on the stamped root (every lookup in the item except `sprk_sentby`);
  - includes the root in the resolver's question.
- If the stamp leaves the item, the owner is decided over the regarding record alone. The resolver does not follow a
  TEAM-OWNED child parent ("its owner already IS the resolver's answer", `RecordOwnershipResolver.cs` ~:3803). So the
  root then takes no part in secure-if-any.

The stamp exists only when the regarding is an intermediate: `sprk_analysis`, `sprk_invoice` or `sprk_budget`. These
are the only email.draft regarding tables in `CoreAncestorResolver.IntermediateRootColumns["sprk_communication"]`. A root
regarding is bound directly, so no stamp is added.

A scratch probe was run and then deleted; it is not committed. It used the real resolver over `OwnershipDirectory`, asking
`RecordCreationService.OwnerQuestionOf("sprk_communication", …)` for a draft regarding an invoice, team-owned by the
child BU team, under the matter:

| World | Today (stamp in the item) | Option (a) (stamp in the body only) |
|---|---|---|
| Ordinary matter | child BU team | child BU team (same) |
| Secure and isolated matter; the invoice is still team-owned in the ordinary BU (reconcile window) | `SecureNamedTeam` | child BU team: **the owner changes, and isolation is weaker** |
| Matter flagged secure but not isolated (interrupted provisioning, C11) | refused `record_owner_secure_parent_not_isolated` | created, child BU team: **an existing refusal changes** |

Found by reading the code, not probed: a caller with AppendTo on the invoice but not on its matter is refused
`AccessDenied` today. Under option (a) they are not refused, because the matter is no longer a record the item names.
**An existing refusal changes.**

If the invoice is not team-owned, the resolver looks through to its filing, so the root is included and the owner is
the same.

## Other findings (for whoever resumes)

- **The service does not set the sender.** `RecordCreationService` never writes `sprk_sentby`. Step 9 says
  "'for' person and table defaults stay with the adapters". `RecordTableRules`' `ChatCallerIsTheForPerson` is a
  description, not a step. A `PostMappingDefaults` entry for it is an argument error, because the "for" column is
  `ProtectedFromMapping`. So on the owned path the handler must keep sending `sprk_sentby` = the caller's WhoAmI id, with
  `ServerSetLookupColumns = {sprk_sentby}`, exactly as today. That WhoAmI is the one the handler already makes for the
  pre-derivation AppendTo check on the regarding record (`EmailDraftToolHandler.cs` ~:366). That check stays: without it,
  the app-only derivation would leak whether the record exists. The second WhoAmI (~:418-427) never runs, because
  "filed" implies a regarding.
- **"A draft under an event or communication" cannot happen for email.draft.** Its regarding vocabulary
  (`RegardingLookupColumns`) has no `sprk_event` or `sprk_communication`. The stamp-only cases are the analysis, invoice
  and budget regardings. All three are ownership parents themselves (`OwnershipParentEntities`). So
  `IsFiledUnderAnOwnershipParent` is already true from the regarding alone, and its stamp branch decides nothing today.
- **Error codes planned for the re-point (not applied).** These follow the Office precedent: the existing tool code by
  kind, and the text from the shared map.

  | Kind | Code |
  |---|---|
  | `Forbidden` / `NotFound` / `BlockedColumn` | `AccessDenied` |
  | `Invalid` (including `stamp_underivable`) / `OwnerUnresolved` | `VALIDATION_FAILED` |
  | `Unavailable` (including `stamp_unavailable`, `owner_unavailable`) | `INTERNAL_ERROR` |
  | `PrincipalUnresolved` / `Upstream` | `MapClientError(ClientFailure)` |
  | resolver codes | carried |

- **Constructor.** The re-point replaces the `IRecordOwnershipResolver` and `IFieldMappingDataverseService` parameters
  with `RecordCreationService`. Every existing `new EmailDraftToolHandler(…)` must change:
  - `SecureChildOwnershipAiToolTests.cs` :481, :512;
  - `.RegardingPair.cs`;
  - `EmailDraftToolHandlerTests.cs`;
  - `EmailDraftToolHandlerPerItemToolsTests.cs`;
  - `CommunicationVisibilityFixtures.cs`;
  - `P2LoopInjectionEvalSuiteTests.cs`.

## Options for the coordinator / owner

1. **(a′) Keep the derived stamp in the create item, as today; recommended.**
   - Step 10 then finds no stamp missing and adds only what is missing (fill-only).
   - The owner question and the AppendTo check are the same as today.
   - They are also the same as the route, whose browser payloads carry the client-derived stamp.
   - No service change. It departs from decision (a) only in "stops writing the stamp".
2. **(b) Service change.** Steps 5 and 7 would read step 10's derived stamp: AppendTo on it and the owner question over
   it. This changes the service contract for every caller, the route included, and needs owner and UAC-r2 review.
3. **(c) Accept the change.** This needs owner sign-off, because of the weaker isolation in the reconcile window and the
   changed refusals. Not recommended.
