# Task 062 notes - message sender name falls back to the creating user (person only)

Rigor FULL (tag bff-api), model sonnet/high. Conflict-check: not run as a skill (no live/gh commands in this run); only
CommunicationThreadReadService.cs was edited in src/ (not 063/064/065 files; CommunicationService.cs untouched).

## Change
`CommunicationThreadReadService.ParseMessageRow`:
`SentByName = sentby FV ?? createdbycontact FV ?? (CreatedByIsAPerson(row) ? createdby FV : null)`.
`_createdby_value` AND `$expand=createdby($select=applicationid,accessmode)` are on the four queries that feed `ParseMessageRow` -> `BuildDto` -> SentByName:
- `ReadThreadAsync` (internal thread read)
- `QueryVisibleMessagesAsync` (by-regarding + filtered query)
- `ReadRegardingAppOnlyAsync` (external thread read) and the recent-record notification-feed read (both `ExternalSelect`)
Not added to: the unread scan (`GetUnreadCountAsync`), `ReadVisibleCommunicationAsync`, thread-list reads (no SentByName).
Only the createdby display-name annotation is read; no createdby id/email is mapped to any DTO field; the DTOs are unchanged.
Annotations are already requested by both the impersonated and app-only queries (DataverseWebApiService include-annotations).

## Rework (2026-10-13): createdby is used only when it is a PERSON
Problem: the BFF creates communication rows through the app identity (`IGenericEntityService` = the app-identity
`IDataverseService`, GraphModule.cs:140), so for a BFF-sent message `createdby` is the BFF's APPLICATION USER, and the first-run
fallback would show partners that account's name as the sender. Owner decision: use the createdby name only for a real person
(not an application / non-interactive user); otherwise SentByName stays null.

### Classification method (and why)
Same-read expand: `$expand=createdby($select=applicationid,accessmode)`. No extra round trip, no second query, no per-row fan-out;
the expanded object adds two small columns per row. `CreatedByIsAPerson` is true only when ALL hold:
1. the row has a `createdby` JSON object (the expand came back),
2. `applicationid` is PRESENT and JSON null (an application user has one),
3. `accessmode` is a number that is not 3 (Support User) or 4 (Non-interactive).
Anything else - no expand (e.g. caller has no read on systemuser), expand null, a missing member, a non-number accessmode - is
NOT a person, so the name is dropped (fails toward null). Precedence is unchanged: sprk_sentby, then creator contact, then (person-only) createdby.

Alternatives rejected: a separate `systemusers?$filter=...` lookup (extra query per page, extra failure mode); `accessmode` alone
(an application user is identified by `applicationid`, which is the documented/unambiguous marker; accessmode alone would also
let an application user with an odd mode through); `applicationid` alone (would let a non-interactive service account without an
application id be named).

### Evidence (repo code that uses the same fields; no live access this run)
- `RecordContainerResolver.DocumentPointer.cs` `ReadRowCreatorAsync` (~line 757-820): "The row's creator is `createdby` when that
  is a person (no `applicationid`), else ..." - reads `systemuser.applicationid` of the row's `createdby`; `Guid.Empty` = person.
  Same person-vs-BFF-app-user question, same marker.
- `SecureBuRoleDepthAssertion.cs` `SecureBuRoleDepthCensusBuilder.IsHumanPrincipal`: a human is `!hasApplicationId && !isDisabled
  && !fullName.StartsWith('#') && accessMode is not 3 and not 4` ("3 = Support User, 4 = Non-interactive"). The accessmode rule
  here mirrors it (isdisabled and the `#` name rule are about directory principals, not a historical row creator, and are not read).
- `OfficeEditAccessService.cs:227` and `RecordContainerResolver.cs:2295` already select `accessmode` + `applicationid` on systemuser.
- `$expand=createdby($select=...)` is an established idiom here: `src/solutions/Notepad/src/hooks/useSprkMemoRepository.ts:257`
  (`$expand=createdby($select=fullname,systemuserid)`), which also notes a BFF-created memo has the application as createdby.
- Dataverse Web API: `createdby` is the single-valued navigation property of the systemuser lookup (`_createdby_value` is its
  lookup value), so it can be expanded with `$select`; selected columns are returned as JSON null when empty.

### Tests (all in tests/unit/Sprk.Bff.Api.Tests)
- CommunicationThreadReadServiceTests: person (every interactive mode 0/1/2/5) -> name; application user (applicationid set, mode 4)
  -> null; applicationid set with interactive mode -> null; no applicationid but mode 3/4 -> null; no expand / null expand /
  missing applicationid / missing accessmode / non-numeric accessmode (unclassifiable) -> null; person with no formatted name ->
  null; sprk_sentby and creator-contact precedence (incl. when createdby is the application); DTO JSON never holds the createdby
  id or the app name; query `$select` has `_createdby_value` and `$expand=createdby($select=applicationid,accessmode)`.
- CommunicationByRegardingReadTests: by-regarding query carries `$select` `_createdby_value` + the `$expand`.
- ExternalMessageEndpointsTests / ExternalNotificationFeedTests: the two exact `$select` pins now also pin the `$expand` clause.
- ExternalMessageSendTests: the external read (partner path) - application createdby -> null SenderName; interactive -> name; unclassifiable -> null.

## Risk / follow-ups (not fixed here)
1. LIVE VERIFY (dev, after the next BFF deploy): that the expanded `createdby` comes back as `{ "applicationid": null, "accessmode": 0, ... }`
   for a person and with the BFF's applicationid for a BFF-created row. The rule is strict: if Dataverse omitted a null `applicationid`
   instead of returning it, every createdby would be unclassifiable and the sender would stay null (safe, but the MDA fix would be dead).
   The Notepad hook reads the same expand shape. If a caller lacks read on systemuser the expand yields null -> null name (safe).
2. Data quality: the MDA send path does not stamp sprk_sentby. Should be filed via /project-defer-issue-tracking (not run in this
   no-live-commands session) - please file. With the person-only rule, BFF-created rows with no sprk_sentby / creator contact now show null (by design).
3. `sprk_createdbyperson` (RecordCreatorPerson / RecordCreatorPersonColumn) exists for other entities as "who created it" when the BFF
   creates as the app; not examined for sprk_communication here (CommunicationService.cs is task 065's) - a candidate better source if it is, or becomes, stamped.
4. Publish-size delta: not measured (no new package/DI/endpoint; string constants and one private helper). Reaches dev with the next BFF deploy.

## Review (adversarial pass, 2026-10-14; main session)

No F-class finding. External plane clean (the app-only query passes `$expand` through unchanged), disclosure clean (only the display name reaches a DTO), tests prove the contract.

| Finding | Class | Action |
|---|---|---|
| Rule needs an expanded `applicationid` present and null; no live capture yet | K1 | Live check after the BFF deploy: read one MDA-created row from the dev Web API |
| Internal reads now join `systemuser`; a caller with NO User read at any depth might get an error instead of a null expand (unverified; the claim above that it nulls is unverified) | K2 | Accepted: every standard role, including Spaarke Basic User, carries User read. Verify with a least-privileged role at the live check |
| SYSTEM / INTEGRATION users would be classified as people | K3 | Accepted: no current path creates `sprk_communication` as them |
| Comment named `ClassifyCreatedBy` | K4 | Fixed (`CreatedByIsAPerson`) |
| "MDA send never stamps `sprk_sentby`" to be filed | process | Not filed: task 065 stamps `sprk_sentby` on every BFF send (messages and shared-mailbox emails) |
