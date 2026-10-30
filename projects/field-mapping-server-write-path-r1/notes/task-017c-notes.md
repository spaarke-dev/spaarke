# Task 017c — chat create / email.draft / budget inquiry re-point (phase 2c)

Branch `task/fm-017c` from `origin/task/fm-017a` (f758480f3c, phase 1). Rigor FULL (task 017). Separate file so it does
not conflict with 017b's notes; the main session folds it in.

## Outcome

**Re-point: STOPPED at the POML escalation triggers, nothing re-pointed.** Two of the three callers create tables that
are not in `RecordTableRules`, and the chat tool's existing refusal codes differ from the shared map's. Details below.
The one change made is the ebilling-r1 review item (server-written invoice tables refused from chat), which does not
depend on the re-point.

## ebilling-r1 review of f758480f3c, 2026-10-30

After phase 1, `sprk_invoicesubmission`, `sprk_invoiceapproval` and `sprk_invoicecorrection` have registry entries, so
the service would accept a chat create of them. ebilling's services read those rows as the record of a decision or a
correction; they are server-written. The chat `dataverse.create_record` handler now refuses them outright with the
`sprk_document` block's mechanism and shape:

- `DataverseCreateRecordHandler.ServerWrittenInvoiceFamilyTables` (the three tables) and
  `ServerWrittenInvoiceFamilyCreateBlockedMessage(table)`;
- checked in `ValidateChat` (pre-suspend: no confirm dialog) and again in `ExecuteChatAsync` (defense in depth), both
  BEFORE any Dataverse call; tool error code `VALIDATION_FAILED`, as for `sprk_document`. No affordance link (there is
  no wizard to point at; the text says to act from the invoice itself).
- `RecordTableRules` untouched. The tool description (byte-mirrored in
  `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json`) is unchanged; the refusal text tells the model
  not to retry.
- Not included: `sprk_externalidentifier` (also given an entry in phase 1). Not named in the review item; ebilling-r1 to
  say whether it is server-written too.

ArchTests: the task-098 source scan (`ExternalInvoiceVisibilityGuardTests`) found the new static set, which names three
invoice-family tables, as a new surface. It reads no row, so it is listed in `NotInvoiceSurfaces` with its reason
(as `EmailDraftToolHandler::.cctor` is) rather than given the (a') predicate.

Tests: `tests/integration/data-mutation/RecordOwnership/ChatServerWrittenTableBlockTests.cs` (7): one refusal per table
through `ExecuteChatAsync` (no app-only create, no POST, no read of the caller's Dataverse), one per table through
`ValidateChat`, and a must-not-fire (`sprk_todo` under an ordinary matter is still created by the app). Must-fire: with
the block disabled, the 6 refusal tests fail and the must-not-fire passes; restored, 7/7 pass.

## Escalation 1 — chat creates tables that are not in RecordTableRules (POML trigger 3)

`OwnedChildWrite.PathFor` sends EVERY `UserOwned`/`TeamOwned` table through `OwnedChildWrite.CreateAsync` except
per-user tables and unfiled communications/threads. The service refuses any table without a registry entry
(`record_creation.table_unsupported`, `RecordCreationService.cs` ~:163). Tables chat creates today on the owned path
that are NOT in the registry:

- `task` (activity) — pinned by `SecureChildOwnershipAiToolTests.cs`
  `CreateRecord_ATableOutsideTheOwnershipSetFiledToAnOrdinaryMatter_IsCreatedByTheApp_OwnedByThatMattersTeam` (must pass
  unchanged) and `..._FiledToASecureMatter_IsRefused` (expects `VALIDATION_FAILED`, would become `table_unsupported`);
- `contact`, `account` — the tool description advertises them ("people = contact, companies = account");
- every other user/team-owned table (activities, `sprk_servicerequest`, …): the tool takes any logical name.

The POML's negative criterion ("a chat create of a table not in RecordTableRules is refused") and the must-pass-unchanged
test above contradict each other. Options for the owner/coordinator:
(a) refuse non-registry tables from chat (criterion as written): the `task` test changes (an assertion is reversed, which
the brief forbids without approval) and chat can no longer create contacts, accounts or activities — a product change;
(b) add the tables chat must create to the registry (012 recipe; 017b owns RecordTableRules) — needs the list decided
(at least `task`, `contact`, `account`);
(c) keep non-registry owned-path tables on `OwnedChildWrite.CreateAsync` for now — a second create core, which FR-13 and
"no fallback" exclude, and it blocks deleting the create half.

## Escalation 2 — budget inquiry creates `sprk_servicerequest`, not in the registry

`BudgetInquiryExecutor.Table = "sprk_servicerequest"` (`BudgetInquiryExecutor.cs:145`). Re-pointed as is, every budget
inquiry would be refused `record_creation.table_unsupported`. Needs a registry entry (017b; child, filed under
`sprk_regardingmatter`; `sprk_regardingorganization` lookup; ADR-024 typed pair) before 017c can re-point it.

## Escalation 3 — chat tool error codes vs the shared map (ADR-019 / NFR-07 trigger)

The brief says the chat handlers take code and text from the shared map, and to stop if an existing refusal's code
changes. They would change. Today's chat codes (owned path) and what the service returns:

| Today (chat tool `ErrorCode`) | Service refusal |
|---|---|
| `AccessDenied` (caller lacks Create / AppendTo / field security), e.g. `SecureChildOwnershipAiToolTests.cs:148,159,198,407`, `RegardingPair.cs:389,490,513,557` | `record_creation.denied` (403) / `record_creation.not_found` (404) |
| `VALIDATION_FAILED` (Invalid, secure filing refused) `:443` | `record_creation.invalid_payload` / `record_creation.blocked_column` / OwnerUnresolved `NoOwnerSource` |
| `INTERNAL_ERROR` (owner resolver faulted) `:469` | `record_creation.owner_unavailable` (503) |
| resolver / plan / invoice-guard codes (e.g. `SecureParentNotIsolated`, `ParentUndetermined`, guard codes) | same codes (carried) |
| WhoAmI / Dataverse client failure: the failure's own code | `PrincipalUnresolved` / `Upstream`, failure carried in `ClientFailure` |

The Office precedent (task 018, `OfficeService.cs` ~:2726-2810) maps refusal KIND to its own existing codes in the adapter
and takes only the text from the service. The same adapter for chat keeps every code above; it needs the coordinator to
confirm that "code from the shared map" means "kind from the service, existing tool code by kind" for chat.

## Owner decision 2c (sprk_matternumber / sprk_projectnumber from chat)

The service already refuses them: step 2, `IsNumberColumn(rule, c)` on the registry's `AutonumberColumn`
(`RecordCreationService.cs` ~:382) → BlockedColumn 403 `record_creation.blocked_column`. **No registry change needed.**
It reaches chat only once chat creates through the service, so the description change ("stop offering the field") is
deferred to land in that same change, per the decision. Today the description says `sprk_matternumber (text)`; the edit is
in `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json` and the mirrored literal at
`DataverseCreateRecordHandler.cs` `Metadata.Description`.

## DI (for when the re-point resumes)

Tool handlers are scoped (`AddToolHandlersFromAssembly`, `IToolHandlerRegistry` scoped); `BudgetInquiryExecutor` is
scoped (`CommunicationModule.cs:134`); `RecordCreationService` is scoped and registered unconditionally in
`OfficeModule.cs:74` (`Program.cs:93`). No captive dependency. A host that composes `AddToolFramework` without the Office
module would not resolve a handler that takes the service (§10 F.1); production composes both.

## email.draft

Re-pointable in principle (`sprk_communication` has an entry; only a FILED draft takes the owned path). Not done alone:
it needs the same chat refusal adapter as escalation 3, and it pre-stamps I-1/I-16 into the payload
(`DeriveForHostAsync`) while the service's step 10 is still post-create only (owner decision 2a not yet implemented in
the service). Keeping its own pre-derivation and passing the item through the service is the behaviour-preserving way.
