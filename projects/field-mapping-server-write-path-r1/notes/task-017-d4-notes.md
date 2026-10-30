# Task 017 phase 2d-4: re-point email.draft (STOPPED)

Branch `task/fm-017-d4`, created from `origin/task/fm-017` (e6c20bd51c). Rigor FULL (task 017).

## Outcome

**STOPPED at the brief's escalation trigger. Nothing was re-pointed and no production code changed.** Option (a), with
the handler no longer writing the I-1 stamp into the create item, changes the owner for one data state and an existing
refusal for another. Both are shown below.

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
