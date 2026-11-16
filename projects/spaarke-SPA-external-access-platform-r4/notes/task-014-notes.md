# Task 014 — BFF Event forms (FR-35, FR-36, FR-37)

## Rigor
FULL, opus @ high, directional. Two adversarial passes (external write route).

## Gates
1. PR #2847 (#2845) merged: **done**, master `ce6c241964`, merged into the branch at `5eb257a40d` (2026-11-16).
2. uac-r2 UAC review of the proposal: **sent 2026-11-16** (proposal A–F, questions Q1–Q6, below). No BFF code before the answer.

## Conflict check (2026-11-16)
39 open PRs checked; none touches `Api/ExternalAccess/`, `Infrastructure/ExternalAccess/`, `ExternalAccessModule.cs` or `external-spa/src/`.

## Proposal sent to uac-r2 (summary)
- **A.** `creatableEventTypes`: every active type row.
  - DataverseConfirmsWrites callers are filtered by a batched AppendTo probe, memoized under `event-type-appendto:{principal}:{sortedTypeIds}`.
  - Contacts get every active row.
  - Config `ExternalAccess:EventTypes:Allowed` can narrow the set.
  - 400 `sdap.event.type_not_allowed`. `"task"` is kept for one release.
- **B.** Create adds `eventTypeId` (required), `sprk_priority`, `sprk_description` and `assignedToContactId` (400 `sdap.event.assignee_not_allowed`).
- **C.** New readable columns: type id and name, description, priority, assignee name, creator name. Plus `canEdit`, `canChangeStatus` and `allowedStatuses` per row.
- **D.** `GET /{root}/{id}/assignable-people`: the only route that returns contact ids. The gate is the create or edit right.
- **E.** `GET /events/{id}`: requires read on every parent; otherwise the uniform 404.
- **F.** `PATCH /events/{id}`:
  - partial fields;
  - any status, with `statecode` paired;
  - the type cannot change (400 `sdap.event.type_immutable`);
  - the right is Collaborate or Full on every parent (403 `sdap.access.deny.event_update`).

Questions:
- **Q1:** which access resolution decides the edit right, plus a Dataverse Write probe for systemusers?
- **Q2:** does the assignable set include organisation grants? Are deny-list subjects removed? Which reader?
- **Q3:** is "all active types" acceptable for CIAM contacts?
- **Q4:** must PATCH clear the memo after an assignee write?
- **Q5:** may the creator name be exposed?
- **Q6:** any route-ledger pins?

## uac-r2 review (2026-11-16, verbatim)

> uac-r2 → spa-r4: UAC review of R4 task 014. The shape A–F is approved, with the conditions below. Record them verbatim.
>
> **Q1 (F, edit right).** Re-check on every PATCH; never trust the list's canEdit.
> - Contact callers: RightsForRootWriteAsync on EVERY parent, Collaborate or higher (as create does). The vetoes come with it (Restricted → None for contacts; the deny list).
> - Systemuser (DataverseConfirmsWrites) callers: BOTH the parent rule above AND Dataverse's own Write on the event ROW, asked as the user: CallerRecordAccessProbe, the app-only impersonated variant (GetPrincipalRightsAppOnlyOrUnknownAsync with the binder's systemuserid), since OBO is barred on /api/v1/external.
>   - D1/C9: a systemuser's access is Dataverse's answer, and parent Write does not imply Write on the event row (ownership, BU).
> - G5: check as the user, write as the app.
> - Outcomes:
>   - unknown → 503;
>   - can't READ the event (any parent unreadable, or row Read None) → uniform 404 sdap.access.deny.record_unavailable;
>   - can read but not edit → 403 sdap.access.deny.event_update.
> - Status changes use the same right.
>
> **Q2 (D, assignable-people)** = contacts with EFFECTIVE external access to that record now, not raw grant rows:
> 1. Start from the root's ACTIVE, unexpired contact grants. Use the grant core's row read (QueryActiveRowsForRootAsync) with ConfersAccessOn's UTC-today rule. ReadPairingsOnRecordAsync is the wrong reader: it reads pairings.
> 2. Add contacts reached through an org (account) grant (the evaluator's org-expansion term). EXCEPT on a Secure record: for contacts only named direct grants count.
> 3. Restricted record (sprk_accesspermission 100000002) → empty list.
> 4. Remove deny-list subjects: the contact itself, or its org, on this record or its filing ancestors. Reuse the evaluator's deny path (ResolveDenyVetoAsync over NoAccessListReader); don't write a new sprk_noaccessentries query.
> 5. Remove contacts with a removed marker, and inactive contacts.
> 6. Fail closed: any unknown read → typed 503. Never a partial or empty 200 (the RecordGrantsEndpoint precedent).
> 7. Return name + contactId + isMe only, never email.
> 8. One owner: put the "contacts with access to root R" computation in Infrastructure/ExternalAccess (next to AccessibleRecordSetService), not route-local. That area is parallel-safe:false, so tell me before you start, and I review that PR as a gate.
> 9. Note for your owner: a contact caller will see the names of OTHER organisations' contacts who are on the record. That follows from their rule; say it to them once.
>
> **Q3 (A).** "Every active type" for contacts is acceptable: an event type confers no access, and the record gate is unchanged. Validate eventTypeId against the ACTIVE set at create time (400 type_not_allowed). Existing events whose type was later deactivated stay readable and editable (type is immutable, so it's moot). Systemuser AppendTo on type rows: fine as one batched probe. The memo key must include the principal KIND and id plus the sorted type ids. Remember only complete answers (RequestAccessMemo's remember predicate).
>
> **Q4 (memo).** No external-access rule derives from sprk_event.sprk_assignedto. AssignedAccessMaterializer's registry is the three roots only (TryParseRootLogicalName), so an assignee write needs NO RequestAccessMemo clear. A clear is needed only for grant, removed-marker or contact-state writes. BUT: MembershipOptions (AI membership) lists sprk_event.sprk_assignedto as access-conferring for Contact. So write the assignee through the SAME path as EventEndpoints' existing assignee write (EventEndpoints.cs:~193), not a hand-rolled PATCH, so whatever membership upkeep follows a write still runs. Verify that path and cite it in your notes.
>
> **Q5 (creator name).** Names were approved per column (2026-11-08), so the creator column needs your owner's explicit OK as its own column. I don't object: FormattedValue name only, never the systemuser id or email. A contact creator's name is fine.
>
> **Q6 (ledger and pins).** New routes in existing files don't change ExpectedEndpointFileCount. Each new route must still carry a credited authorization form or RouteAuthorizationGuardTests fails. Pin these with tests:
> - unknown vs denied event id → identical 404 (NFR-01);
> - read-but-not-edit → 403 event_update;
> - systemuser with parent Write but row Write None → 403;
> - assignable-people:
>   - fault → 503, not []
>   - Restricted → []
>   - Secure → direct grants only
>   - a deny-listed contact is absent
>   - a removed or inactive contact is absent
> - PATCH with an assignee outside D's set → 400 assignee_not_allowed;
> - ExternalPlaneNoOboGuardTests stays green.

## Consequences for the plan
- The task splits into two PRs:
  - **014a**: the "contacts with access to root R" computation in `Infrastructure/ExternalAccess`. uac-r2 reviews that PR as a gate, and must be told before work starts.
  - **014b**: everything else.
- The assignee write must reuse the service path behind `EventEndpoints.cs:~193` (verify it and cite it here). The internal route runs OBO, which is barred on the external plane, so only the shared service write is reused, not the endpoint.
- Owner input:
  - **Q5 — approved (owner, 2026-11-16: "Yes, show it").** The creator's name shows on the event (FormattedValue name only, never an id or email).
  - **Q2.9 — told to the owner 2026-11-16:** contact callers will see the names of other organisations' contacts who are on the record.
