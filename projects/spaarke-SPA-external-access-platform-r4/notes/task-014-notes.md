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

## 014a result (2026-11-16)
- **PR #2852** (head `work/r4-014a-record-contact-access` @ `09ce996d15`, base master). Sent to uac-r2 for its gate review the same day.
- **Built:**
  - `Infrastructure/ExternalAccess/RecordContactAccessService.cs`: `GetContactsWithAccessAsync` → `Known` / `Unknown`;
  - `ExternalGrantLifecycle.GrantFirmIsActiveAsync` extracted (ISS-026), and `ReadContactHeldGrantsAsync` uses it;
  - `RequestAccessMemo.ReadRemovedMarkerAsync` / `ReadOrganizationMembershipsAsync` (moved out of the evaluator);
  - DI `AddScoped<RecordContactAccessService>()`.
- **Verification:**
  - service tests 32/32;
  - ExternalAccess/AccessControl 11,464;
  - ArchTests 2,635;
  - no vulnerable packages;
  - publish size +9,584 B (master `ce6c241964` 42,336,904 B; 191 files each).
- **Review:**
  - Opus pass 1 found F1 (inactive firm), F2 (memo path untested) and F2 (duplicated readers). All were fixed in `09ce996d15`.
  - The Sonnet scoped re-check of `ac28d45699..09ce996d15` was clean (185 related tests).
  - Pass 2 (the auth task's second pass) runs on the full 014 diff after 014b.
- **Known limits:**
  - K2: the ancestry is re-read per candidate (bounded by `MaxContacts`);
  - firm state is read per contact without a memo;
  - the race half of the memo test is probabilistic;
  - over 200 contacts or 1,000 rows → `Unknown`.
- **Defect found:** D-18 #2851, the `RequestAccessMemo.For` race. uac-r2 owns it. 014a pre-creates the memo before its fan-out.
- **Open for uac-r2:**
  - K1: conferring-membership vs. workforce standing-grant expansion;
  - the merge order against `perf/2845-round2` (both touch `RequestAccessMemo.cs` / `AccessibleRecordSetService`).
- **uac-r2 interim reply (2026-11-16):**
  - The gate review has started (an Opus pass plus uac-r2's own read). **No pushes to `work/r4-014a-record-contact-access` until the verdict.**
  - Merge order: **014a first.** uac-r2 rebases round 2 onto it.
  - Keep the memo pre-create even after #2851 lands.
  - K1: the preliminary answer is yes (conferring-membership org grants only; the workforce standing-grant expansion is a separate term, and leaving it out under-lists, which fails closed). Final with the verdict. If confirmed: record it as a known limit in the PR, and file an issue for listing standing-access workforce contacts in a later picker iteration.
- **uac-r2 GATE VERDICT (2026-11-16) on `09ce996d15`: PASS-WITH-FIXES.**
  - No over-listing and no fail-open on the contract paths.
  - Behaviour parity confirmed: the memo readers keep the same keys, remember rules, fault mapping, log text and levels, and Clear path; `GrantFirmIsActiveAsync` keeps the same outcomes.
  - **Required fixes** (push once, send the SHA; uac-r2 re-checks only the fix diff):
    - **F2:** `QueryActiveRowsForRootAsync` (lenient `QueryAsync`) turns a 200 `{}` / `{"value":null}` into `Known([])`. Make the shared reader throw when `value` is missing or null, as #2847 did for `ReadIdColumnAsync`. Confirm that the other callers fail closed on a throw (`ExternalGrantRevocationService:879`, `RecordGrantsEndpoint:105`, `SetRecordShareExpiryEndpoint:155`). Add service tests for both bodies → Unknown(GrantsUnreadable), plus a reader unit test.
    - **F4:** a cancel in the flags read is swallowed as FlagsUnreadable. Add `ThrowIfCancellationRequested` and a test.
    - **F4 perf:** cache the firm state per call, keyed by firm id.
    - **Doc:** the class remarks state the standing-grant / derived-member exclusion and its reason, and that `isMe` misses such a caller.
  - **K1 confirmed, final:** the org term matches the evaluator's org-grant inheritance (`QueryOrganizationGrantRowsAsync`, `ExternalParticipationService.cs:1742`). Filed as **D-19 #2854**.
  - **Known limits accepted (for the PR body):**
    - K2: an out-of-set `sprk_accesslevel` wins the evaluator's Max() dedupe and maps to None. uac-r2 files this one.
    - K3: up to 60 s of over-listing after a maker-authored membership junction (the evaluator's grant-set cache isn't invalidated).
    - K4: about 3 repeated calls per candidate in `CheckGranteeNoAccessAsync`, memoized by #2845 round 2.
    - K5: one faulted candidate doesn't cancel the others.
  - Optional: make the #2851 regression test deterministic.
  - **On a clean fix diff, uac-r2's gate go for 014b.**
  - **Fix 1's premise doesn't hold.** `QueryActiveRowsForRootAsync` returns `DataverseWebApiClient.QueryAsync<T>`, which maps a missing or null `value` to an empty list (`DataverseWebApiClient.cs:381-382`). #2847's `ReadIdColumnAsync` parsed raw JSON instead. Caller audit (all fail closed on a throw): `ExternalGrantRevocationService:879` → null; `RecordGrantsEndpoint:105` → 503; `SetRecordShareExpiryEndpoint:155` → 500 with no change; the service → Unknown; `GrantRowWireTests.cs:161` is test-only.
    - **uac-r2 recommends (A)**, on these terms:
      - add `DataverseWebApiClient.QueryStrictAsync<T>` (virtual, same signature) sharing one core with a `strict` flag; a missing or null `value` → `InvalidOperationException` with a fixed message;
      - use it only in `QueryActiveRowsForRootAsync`, and move the ~15 mocks;
      - answer §11 in the PR;
      - tests at client and service level, with `{"value":[]}` as the control;
      - file a systemic issue owned by uac-r2.
    - It rejects (B) for now: 87 callers, some without a catch, would turn into 500s. It rejects (C) because "empty" is a believable lie in a picker, but would accept (C) if the owner chooses it.
    - **Owner chose (A) on 2026-11-16** ("Strict method, grant reader"). Sent to the implementer on uac-r2's terms. Systemic issue filed: **D-20 #2860** (owner uac-r2). `Spaarke.Dataverse` is now touched, so re-measure the publish size before the push.
  - Fixes delegated to an implementer (Opus) on `work/r4-014a-record-contact-access`. Commit only; the main session pushes once after a re-check.
- **Gate fixes pushed at `a866dad1be` (2026-11-16):**
  - `QueryStrictAsync<T>` (one shared core with a `strict` flag; one caller); cancel propagates; per-call firm cache; doc remark; the #2851 test is deterministic (failed 3/3 without the pre-create).
  - 12 mock files moved or extended.
  - Tests: service 38, strict client 9, RecordGrants 16, broad suites 15,034 with 1 unrelated load timeout, ArchTests 2,635.
  - Publish size +10,741 B.
  - Scoped Sonnet re-check: no F-class findings. New K items: a firm point read with an empty 2xx body → inactive (shared ISS-026 definition, under-lists); the concurrency test's timing coupling.
  - The PR body was updated with §11 for `QueryStrictAsync`, K1–K5 and the merge order.
  - Sent to uac-r2 for its re-check.
- **uac-r2 re-check PASSED** (`09ce996d15..a866dad1be`). Its gate is cleared, and it gave the go for 014b.
- **MERGED 2026-11-16 13:24Z:** squash `16771d1051`, on the owner's go ("Yes proceed"), with Router and Client lint green. uac-r2 was given the SHA and rebases #2867 (round 2) onto it.
- **NFR-05 deviation** (one DI registration, requested by uac-r2 Q2.8)

## 014b (started 2026-11-16, branch `work/r4-014b-event-forms` from `16771d1051`)
- **Q4's premise doesn't hold (found by the implementer and verified).** The internal assignee write `EventEndpoints.cs:219-232` → `EventDueAssigneeWrite.ApplyAsync` (`:148-174`):
  - is OBO (`IDataverseUserClient`; no app-only implementation), which is barred on the external plane;
  - has no membership upkeep after the write (`MembershipOptions.cs:344` reads `sprk_assignedto` at read time);
  - has different semantics: it allows open work only (`IsOpenWork`, `:139`), forces `statuscode` = Reassigned (`:108`), and writes the reassigned date and reassigned-by.
- **Options:**
  - **(a)** write app-only, reusing `BuildPatchJson` for column parity;
  - **(b)** a plain bind;
  - **(c)** no assignee on PATCH for now.
- **Recommendation: (a)**, with:
  - an explicit `sprk_status` in the same PATCH wins over Reassigned;
  - a reassign is allowed only on open work (400 otherwise);
  - reassigned-by = the caller's contact.

  Create keeps the plain bind (a first assignee isn't a reassign).
- Asked uac-r2 and the owner. The implementer builds the PATCH assignee off (400) until it's decided.
- **uac-r2 (2026-11-16): Q4 premise WITHDRAWN.** `sprk_assignedto` is "who the event is for", not access; nothing grants on it (D1/C9). **No UAC objection to (a).** The owner decides.

  UAC conditions for create's plain bind and the PATCH, all sent to the implementer:
  1. **G5:** check the caller's right through the contact-plane evaluator before any app-only read or write. The open-work status read comes after that check. Denied and unknown are indistinguishable (Q1's table: unknown → 503, can't read → 404, read not edit → 403).
  2. **Assignee re-checked on the server:** in the assignable set AND with effective access to the event (every parent). Otherwise 400.
  3. **Column allow-list:** never ownership, the core-ancestor stamp, `sprk_accesspermission` or `sprk_issecure`.
  4. **Fail closed:** 503 or 404, never a write.
- **Implementer result: local commit `dcbaae5287`** (17 files), not pushed.
  - Routes `GET /events/{id}` (`GetEvent` ~:3081), `PATCH /events/{id}` (`UpdateEvent` ~:3119; the assignee branch returns 400 `assignee_update_unsupported`) and `GET /{root}/{id}/assignable-people` (~:3209).
  - `creatableEventTypes` comes from a memoized AppendTo fan-out (4 at a time; there's no batch API).
  - `canEdit` comes from the same decision as the PATCH.
  - New codes:
    - 400: `type_required`, `type_immutable`, `assignee_not_allowed`, `assignee_update_unsupported`, `due_date_required`, `priority_invalid`, `description_too_long`, `status_invalid`, `name_required`, `update_empty`;
    - 503: `sdap.event.types_unavailable`, `assignees_unavailable`, `unavailable`;
    - 403: `event_update`, `event_assign`.
  - Tests:
    - new contract class 63/63 (52 failed before);
    - access namespaces 17,609;
    - ArchTests 2,635;
    - load timeouts re-run in isolation and green.
  - No CVEs.
  - Deviations:
    - `sprk_event` has no ReadableColumns entry, so it's pinned by an `EventSelect` test;
    - `canCreateTask` now comes from the creatable set;
    - `RecordContactAccessService` is unsealed, with a virtual method, for test stubs.
- **Compatibility fix requested:** the deployed SPA sends no type for "Event", so a blanket `type_required` would break it until 015 ships. Legacy untyped and `"task"` bodies keep working for one release; `type_required` applies to new-shape bodies only.
- **New readers reported to uac-r2** for the #2867 gate decision: `GetEventTypesAsync`, `GetEventByIdAsync`, and the writer `UpdateEventAsync`.
  - **uac-r2's decision: R4 wires nothing.** #2867 adds `DataverseThrottlingHandler` to `ExternalDataService`'s HttpClient registration (one line), and 014b's reads are then gated automatically.
  - The handler holds a permit for one send only, so it can't deadlock with the memo or the fan-out. The probe path stays ungated.
  - It retries GET/HEAD only. A throttled PATCH comes back as 429/503 and must map to a failure (503), never to success. A test for this was requested from the implementer.
  - Registration edits shouldn't conflict (ReadableColumns vs `.AddHttpMessageHandler`); whoever merges second resolves any conflict.
- **Follow-up commit `c28b4b1478`:**
  - **Legacy rule `IsLegacyUntypedCreate` (`ExternalProjectDtos.cs:659`):** a body with none of `eventTypeId`/`sprk_priority`/`sprk_description`/`assignedToContactId` and no `eventType` creates an untyped event, after all the pre-014b checks, with the pre-014b payload. A new-shape body without a type → 400 `type_required`. Both legacy paths are tagged `LEGACY-EVENT-CREATE-R4`, to remove in R5.
  - Assignee root-only comment added.
  - Throttled PATCH: no code change was needed (`UpdateEventAsync` maps non-success responses to `DataverseUnavailableException` → 503). Pinned for 429/503/500/412 → 503 and 404 → the uniform 404, sent once.
  - Tests: forms 80/80; the create-related set 1,584/1,584; ArchTests 2,635. 10 tests failed before the fix.
- **Adversarial pass 2 (Opus)** on `16771d1051..c28b4b1478`: **NOT READY**.
  - **F1:** `GET /events/{id}` skips the invoice-link hiding the list applies (`HideInvoiceLinkedAsync`), so a hidden event is readable by id, and GET and PATCH disagree.
  - **F3:** per-row `canEdit` costs one sequential Dataverse rights probe per event for systemusers (up to 5,000).
  - **F4:** a systemuser with row Read None gets 403 from PATCH, not the uniform 404 (Q1 table).
  - **K1:** unknown vs denied differ in timing and under a chain fault (the delete-own precedent).
  - **K2:** a too-long name becomes a Dataverse 400 → 503. A fix was requested anyway.
  - **K3:** `canCreate` can be true while `creatableEventTypes` is empty. **Task 015 must hide Add event when the list is empty**; from R5, tie `canCreate` to a non-empty set.
  - **K4:** the legacy paths are now stricter, never looser.
  - Verified clean: the allow-list (enforced twice), statecode pairing, type immutability, the assignee Unknown → 503, throttled write → 503, the memo key, no PII on the wire or in logs, and the deployed SPA's compatibility.
  - Fixes (F1, F3, F4, plus K2) sent to the implementer.
- **Pass-2 fixes: local commit `9719d2f594`.**
  - F1: GET runs the list's `HideInvoiceLinkedAsync` (:3203): hidden → uniform 404; fault → 503.
  - F3: row probes run at most 4 in flight (`EventRowProbeConcurrency`) and are memoized per row (`EventRowRightsAsync`). Above 100 rows a systemuser gets `canEdit` false.
  - F4: a `NotReadable` outcome → PATCH 404; the list and GET show `canEdit` false.
  - K2: `sdap.event.name_too_long` above 850 characters, on create and PATCH.
  - Tests: forms 92/92; create/event set 1,596; service + probe 111; ArchTests 2,635. 8 tests failed before the fixes. The timing test passed 5 repeat runs.
  - Scoped Sonnet re-check: **no F-class findings**. Its K items:
    - an unreadable invoice link gives 503 on GET but 404 on PATCH;
    - the create name check runs after the right probe;
    - no GET pin for a systemuser whose row rights are None.
- **uac-r2 review of #2869 @ `9719d2f594`: PASS-WITH-FIXES.** No path lets a caller write what it can't read, and no oracle beyond K1. Required:
  - **F1 (medium):** GET skips the parent-table workforce ceiling `ReadChildrenOf(root, event)`, yet returns `regarding`. Fix: check each parent via `AdmitsAsync`; refusal → uniform 404.
  - **F2:** the parents are checked from `GetDeleteTargetAsync` but served from `GetEventByIdAsync`. Fix: require the parents to set-equal, else 404.
  - **F3:** create's assignee skips the Contact Read ceiling, which makes 400 vs 201 an oracle. Fix: require `(ContactTable, Read)` first → 403 `event_assign`.
  - **F4 (test):** the real `GetEventByIdAsync` withholding is never exercised. Fix: HTTP-handler-double tests, and correct the `EventSelect` doc citation.

  Accepted limits:
  - **K5:** an event on a readable project and an unreadable non-secure matter shows in the project list but 404s on GET. **Decision for 015: open row details from the list row's data; use GET only for deep links.** Noted in the PR.
  - **K6:** a systemuser with row Read None gets GET 200 but PATCH 404. Documented as deliberate: GET follows the parent rule (Q-7), PATCH follows Q1.

  Fixes sent to the implementer, who pushes once they're green.
- **uac-r2 fixes pushed at `67b99e2126`** (`ReadEventsCeiling` per parent; parents set-equal check; `AssignEventContact` before the set lookup; real `GetEventByIdAsync` tests; K6 remarks).
  - 12 new tests; 5 failed before the fixes.
  - Event set 1,608; ceiling families 289; ArchTests 2,635.
  - PR body updated (K5, K6).
  - Sent to uac-r2 for its fix-diff re-check.
- **MERGED 2026-11-16 22:20Z:** #2869, squash `fc9803419c`, on the owner's go ("#2869 yes auto merge"; the PR was already clean, so it was merged directly), with Router and Client lint green. uac-r2 was sent the SHA for the next dev BFF candidate.
- **Owner decision 2026-11-16: PATCH assignee = (a), "match internal reassign".**
  - The write is app-only, reusing `BuildPatchJson` (Reassigned, the reassigned date, reassigned-by = the caller's contact).
  - An explicit status in the same PATCH wins.
  - Open work only (400 otherwise).
  - uac-r2's conditions apply.
  - This is **014c**: branch `work/r4-014c-patch-assignee` from `fc9803419c`. An Opus implementer is running (commit only).
- **014c implementer result: local commit `2d6ac5b312`.**
  - **Internal semantics reproduced:**
    - null = no reassign (there's no clear);
    - the same assignee again still counts as a reassign;
    - a systemuser's reassigned-by = its linked contact, omitted if it has none;
    - open work is judged on the CURRENT status.
  - **Order:**
    1. shape;
    2. readable (404/503);
    3. edit right (G5);
    4. `AssignEventContact` → 403;
    5. one app-only read → 400 `reassign_not_open`;
    6. every parent's Known set (any Unknown → 503, otherwise 400 `assignee_not_allowed`);
    7. write.
  - The columns come from the extracted `EventDueAssigneeWrite.AddReassignColumns` (`BuildPatchJson` calls it; a byte pin confirms the internal output is unchanged). An explicit status overwrites Reassigned.
  - The allow-list gains 3 columns. `assignee_update_unsupported` is removed.
  - Tests: forms 129/129 (29 failed before); event/create/internal set 1,684; ExternalAccess 6,743; ArchTests 2,635; the OBO guard 26.
  - Leftovers: the ledger text for PATCH and the OpenAPI summary.
  - **Publish size:** +273 B vs master `fc9803419c` (191 files each). No vulnerable packages.
  - **Opus adversarial review: no F1/F3/F4.** The ordering, unknowns, oracles, statecode/statuscode, allow-list, internal byte parity and multi-parent checks are all verified clean.
    - **F2** (test gap): a test where one parent is Unknown and the other is Known without the assignee is missing.
    - **K:** open-work race between read and write (same as internal, `If-Match: *`).
    - **K:** `canEdit` is true where a reassign is refused (open work, Contact ceiling). Recommended: add `canReassign`.
    - **K:** a systemuser with an unreadable contact is reassigned without reassigned-by. Decided: → 503 on the external path (per `ContactUnreadable`'s doc).
    - **K:** the app-only bind skips contact AppendTo (cleared by uac-r2).
    - Leftovers: ledger text, OpenAPI summary, dead `AssigneeSent`.
    - **Multi-parent picker note for 015:** `assignable-people` is per root, so a multi-parent event needs the intersection.
  - **Fix round sent:** F2 rows; `canReassign` on the DTO; `ContactUnreadable` → 503; leftovers.
  - **Fixes landed in `32c32cb890`:**
    - `canReassign` = `canEdit` && `IsReassignable` && `AdmitsEventAssignAsync`, using the same helpers as the PATCH;
    - `ContactUnreadable` → 503 on reassign only;
    - Unknown-wins test rows;
    - ledger, OpenAPI, and the dead `AssigneeSent` removed.
  - Tests: forms 139; event/internal set 1,694; ArchTests 2,635.
  - Sonnet re-check: no F-class findings. K: `canReassign` ignores a transient `ContactUnreadable`.
  - **PR #2872 opened** and sent to uac-r2 for review.
  - **uac-r2 review @ `32c32cb890`: PASS, UAC gate cleared.**
    - The check order matches its four conditions.
    - The allow-list grows by exactly 3 columns.
    - Internal output is byte-identical.
    - `canReassign` uses the PATCH predicates.
    - Known limits accepted: open-work race, AppendTo, multi-parent intersection, transient `ContactUnreadable` 503.
    - Its run: 349/349 passed (forms, root-read, EventDueAssignee, TodoScope).
    - Merge on Router + Client lint green, with the owner's go.
  - **Owner go 2026-11-16:** "Yes, merge on green".
  - **Before merging:**
    - Master (with uac-r2 #2867) was merged into the branch → head `39ebf3c523`. The 014c code is unchanged.
    - Build 0 warnings; 188/188 for forms, EventDueAssignee and RecordContactAccess.
    - Office scope run locally: 570 passed, 0 failed.
  - **CI:** the office-scope legs hit their 20-min cap on both heads, which is the known #1474/#1718/#2767. Each leg passed on re-run; final state CLEAN.
  - **MERGED** #2872 → squash `3c9a2ff4e1`. uac-r2 was sent the SHA for its next dev candidate.
- **Task 014 COMPLETE** (014a #2852, 014b #2869, 014c #2872).
  - Temp worktrees `C:\wt14a`/`C:\wt14m` removed.
  - Index row 036's invalid `[superseded]` token was normalized to `[cancelled]` (POML too), so the drift check is clean.
- **uac-r2 re-check PASS; UAC gate cleared for 014b at `67b99e2126`** (its own run: 170/170). Merge on Router + Client lint green, then send it the merge SHA for the next dev BFF candidate. #2867 is in CI; whoever merges second rebuilds.
- **PR #2869 opened 2026-11-16** (head `9719d2f594`, base master `16771d1051`) and sent to uac-r2 for review. Next: uac-r2 OK + CI → merge (owner go) → hand uac-r2 the SHA for the dev BFF deploy → task 015 (SPA). The PATCH-assignee owner decision (a/b/c) is still open. **Publish size:** master `16771d1051` 42,349,704 B vs 014b `c28b4b1478` 42,374,782 B, **+25,078 B** (Compress-Archive, 191 files each, counts match). No CVEs (implementer check).: stated in the PR; the owner is told in this session's report.
