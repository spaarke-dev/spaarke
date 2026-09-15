# Task 026 — Related-record card: slot-scope decision

> FR-09. `RelatedRecordCard.tsx` + `useRelatedRecord.ts`, extending `POST /api/documents/resolve-identity`
> (task 012, extended by task 021's precedent of "extend, don't add a route" — see §5). Written 2026-09-14.

## 1. The sixteen candidate slots, and the four this card reads

`sprk_document` carries sixteen association-shaped lookups: FOUR direct slots and TWELVE `sprk_related*` slots.

| Family | Slots | Read by this card? |
|---|---|---|
| **Direct** (4) | `sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment` | **YES** — all four, in this precedence order |
| **Related** (12) | `sprk_relatedmatter`, `sprk_relatedproject`, `sprk_relatedagreement`, `sprk_relatedcommunication`, `sprk_relatedcontact`, `sprk_relatedevent`, `sprk_relatedinvoice`, `sprk_relatedorganization`, `sprk_relatedservicerequest`, `sprk_relatedtodo`, `sprk_relatedvendororg`, `sprk_relatedworkassignment` | **NO** — none |

There is **no `sprk_event`** direct slot — a live query throws `'sprk_Document' entity doesn't contain attribute
with Name = 'sprk_event'` (verified 2026-09-04, corrected in this task's brief). The Event column is
`sprk_relatedevent` only, and it is excluded along with the other eleven `sprk_related*` slots — see §2.

## 2. Why the direct four and nothing else

**The Office save path writes ONLY the direct family.** `DocumentAssociationMap.TryApply`
(`src/server/shared/Spaarke.Dataverse/Models.cs`) maps matter/project/invoice/workassignment onto
`MatterLookup`/`ProjectLookup`/`InvoiceLookup`/`WorkAssignmentLookup`, and `DataverseServiceClientImpl.cs`
writes those onto `sprk_matter`/`sprk_project`/`sprk_invoice`/`sprk_workassignment`. Nothing in the Office save
path writes any `sprk_related*` column. A card that read `sprk_related*` instead would be BLANK on every
document the add-in itself filed — the load-bearing fact this decision protects (task 012's own resolver
already reached the same conclusion for the SAME reason; see `notes/012-identity-resolver-decisions.md` and
`DocumentUrlIdentityResolution.RelatedRecordAttributes`).

This task does not invent a new scoping decision — it **reuses task 012's already-shipped one**. Reading the
`sprk_related*` family would also duplicate `unified-access-control-r2` task 095's in-flight two-slot model
(direct-write / related-read) rather than consuming it, which constraint 2 of this task's POML forbids.

## 3. "Name" vs "number" — the trap, and how it is closed

Verified live 2026-09-14 against Dataverse metadata (`EntityDefinitions(LogicalName='…')?$select=PrimaryNameAttribute`):

| Entity | `PrimaryNameAttribute` | Separate descriptive-name column | Separate number column |
|---|---|---|---|
| `sprk_matter` | **`sprk_matternumber`** | `sprk_mattername` | — (the primary name IS the number) |
| `sprk_project` | **`sprk_projectnumber`** | `sprk_projectname` | — (the primary name IS the number) |
| `sprk_invoice` | `sprk_name` | — (the primary name IS the descriptive name) | `sprk_invoicenumber` |
| `sprk_workassignment` | `sprk_name` | — (the primary name IS the descriptive name) | `sprk_workassignmentnumber` |

Dataverse auto-populates `EntityReference.Name` from the referenced record's PRIMARY NAME attribute on every
lookup retrieve. That means task 012's resolver, which already returns `relatedRecord.name` from
`EntityReference.Name`, returns **a NUMBER for Matter/Project and a DESCRIPTIVE NAME for Invoice/
WorkAssignment** — under the SAME field name. A card that read only `name` would show a number labeled as a
name for half the family, and a name with no number for the other half.

**Fix, applied at the SAME route** (`POST /api/documents/resolve-identity`, `RelatedRecordIdentity`):

- `name` — kept, UNCHANGED, for backward compatibility. Existing consumers (`documentIdentityService.ts`'s
  `applyDocumentIdentityOutcome`, which seeds the Create-To-Do "regarding" fields) keep reading exactly what
  they read before this task. **Known adjacent issue, out of this task's scope**: `regardingName` therefore
  still shows a NUMBER for a Matter/Project-regarding Create-To-Do, which is the same trap one level removed.
  Not fixed here — changing `name`'s semantics would be a behavior change for a feature this task does not
  own or test. Flagged for the owner; a GitHub Issue was not filed because this agent may not open one (see
  the escalation/boundary notes on this task).
- `displayName` (NEW) — always the descriptive name, regardless of entity type. `sprk_mattername` /
  `sprk_projectname` (fetched by a targeted extra retrieve) for Matter/Project; `EntityReference.Name` (the
  primary name) for Invoice/WorkAssignment.
- `number` (NEW) — always the number, regardless of entity type. `EntityReference.Name` for Matter/Project;
  `sprk_invoicenumber` / `sprk_workassignmentnumber` (fetched by a targeted extra retrieve) for Invoice/
  WorkAssignment.

The card reads `displayName` and `number` exclusively — never the ambiguous `name`.

**Blank numbers render gracefully.** `030-numbering-handoff.md` §3 confirms a pane-created Matter has NO
number until the separate numbering project ships (numbering was explicitly removed from task 030). When
`sprk_matternumber` is empty, `EntityReference.Name` is null/empty, so `number` is `null` — the card shows
"No number yet", never an error, never a blank card region (contract test
`ResolveIdentity_ForAPaneCreatedMatterWithNoNumberYet_ReturnsANullNumber_NotAnError`; client test
`renders a blank number as "No number yet", not an error`).

## 4. Precedence when more than one direct slot is populated

**Reused, not reinvented.** Task 012's resolver already applies a deterministic precedence — matter > project >
invoice > work assignment (`DocumentUrlIdentityResolution.RelatedRecordAttributes`, consumed by
`FirstRelatedRecord`). The client never sees more than one candidate: the server has already picked the
highest-priority populated slot before the response leaves the BFF, so `useRelatedRecord` and
`RelatedRecordCard` are inherently deterministic for the same input — there is no second precedence decision
to make client-side. The escalation trigger in this task's POML ("no precedence rule is derivable") does not
fire: the rule already exists, is already shipped, and needed no changes.

## 5. Read-surface decision: extend, don't add a route (CLAUDE.md §11)

**Existing** — `POST /api/documents/resolve-identity` (task 012) already returns `relatedRecord
{entityType, id, name}` for the SAME four direct slots this card needs, and that response ALREADY flows into
`App.savedContext` → `SaveFlow`'s `documentIdentity` prop (task 013). Grep evidence:
`grep -rn "RelatedRecordIdentity" src/server/api/Sprk.Bff.Api` → exactly two files
(`Models/FileOperationModels.cs`, `Api/FileAccessEndpoints.cs`) before this task; `grep -rn "relatedRecord"
src/client/office-addins/shared` → `documentIdentityService.ts` already threads it end-to-end.

**Extension** — YES. The only gap is the two new display fields (§3). Extended `RelatedRecordIdentity` with
`DisplayName`/`Number` (additive — `Name` untouched) and extended `DocumentUrlIdentityResolution` with
`ResolveRelatedRecordDisplayAsync`, called from the `resolve-identity` HANDLER (not from `ResolveAsync` —
see §6) only when a related record is present. No new endpoint, no new client network call: `useRelatedRecord`
is a **pure derivation** over `documentIdentity`, which task 013 already resolved once per document open. This
is also why the card can never show a different answer than the rest of the pane (SaveModeSection, Create
To Do regarding) — they all read the same single resolution.

**Cost of doing nothing** — a user with an identified Spaarke document would see no indication of which
matter/project/invoice/work-assignment it belongs to, could not confirm correct filing before re-saving, and
task 027 (FR-10, "open the related record") would have nothing to anchor a click on.

No new route was written. The escalation trigger for "a NEW BFF route is required" did not fire.

## 6. Access model: why no second authorization check on the related record

Per this task's brief and `current-task.md` Critical Context item 2: **record access is enforced as
equivalent to document access.** `AuthorizationService` fails closed and queries Dataverse AS THE USER; no
code in this codebase shares document access without also implying the equivalent record access. Given that,
gating the related record's `displayName`/`number` on the SAME `DocumentAuthorizationFilter("read")` check
that already gates `documentName`/`fileName` is not a shortcut — it is the correct application of the
existing model, not a second, redundant check invented for this task.

**Enforced structurally, not just by convention**: `ResolveRelatedRecordDisplayAsync` is called from the
`resolve-identity` endpoint HANDLER, which ASP.NET Core only reaches after `DocumentAuthorizationFilter`
allows the request (ADR-008 — ordered endpoint filters). It is deliberately NOT called from
`DocumentUrlIdentityResolution.ResolveAsync`, which runs BEFORE that filter. Contract test
`ResolveIdentity_WhenCallerLacksReadOnADocumentFiledToAMatter_Returns403_LeaksNoNameOrNumber` proves this with
a STRICT `IGenericEntityService` mock carrying NO setup for the complementary-field fetch: if the handler ever
attempted it before authorization denied, the strict mock would throw and fail the test. It does not.

## 7. Excluded-slot test

The POML's ui-test "Excluded slots do not render" (seed a document whose only populated association is an
excluded `sprk_related*` slot) cannot be exercised without a live Dataverse write, which this task may not do
(read-only GETs only). It is proven by construction instead: `DocumentUrlIdentityResolution.LookupColumns`
never requests any `sprk_related*` attribute (`RelatedRecordAttributes` is the closed four-item array in §1),
so the resolver CANNOT return one even if it were populated — there is no code path by which an excluded slot
could reach the card. Listed as UNVERIFIED live (see the task's final report), same as the other Office-host
UI tests.

## 8. RelatedToPicker stays untouched

`grep -n "matterTypeId\|MatterType" src/client/office-addins/shared/taskpane/components/RelatedToPicker.tsx`
confirms task 038's required-Matter-Type field is present and unmodified by this task. `RelatedRecordCard.tsx`
is a NEW, separate component (`shared/taskpane/components/RelatedRecordCard.tsx`) — no edits to
`RelatedToPicker.tsx` were made. It is wired into `SaveFlow.tsx` ABOVE the `!isVersionMode` block that gates
`RelatedToPicker` (see the code comment at the wiring site) — deliberately, because a resolved identity
DEFAULTS to a version save (task 024), which is exactly when `RelatedToPicker` is hidden; the filed-to card is
the pane's only indication of the record in that common case, and this placement is what makes FR-09's
"when the open document is identified and associated... show a card" true unconditionally rather than only in
the "new document" override.
