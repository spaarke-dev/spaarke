# Task 062 notes - message sender name falls back to the creating user

Rigor FULL (tag bff-api), model sonnet/high. Conflict-check: not run as a skill (no live/gh commands in this run); only
CommunicationThreadReadService.cs was edited in src/ (not 063/064/065 files).

## Change
`CommunicationThreadReadService.ParseMessageRow`: `SentByName = sentby FV ?? createdbycontact FV ?? createdby FV` (null if none).
`_createdby_value` is added to the three `$select`s that feed `ParseMessageRow` -> `BuildDto` -> SentByName:
- `ReadThreadAsync` (internal thread read)
- `QueryVisibleMessagesAsync` (by-regarding + filtered query)
- `ExternalSelect` (external thread read + notification feed)
Not added to: the unread scan (`GetUnreadCountAsync`), `ReadVisibleCommunicationAsync`, thread-list reads (no SentByName).
Only the createdby display-name annotation is read; no createdby id/email is mapped to any DTO field; the DTOs are unchanged.
Annotations are already requested by both the impersonated and app-only queries (DataverseWebApiService include-annotations).

## Tests
- CommunicationThreadReadServiceTests: createdby-only -> name; sentby > contact > createdby precedence; none -> null; `$select` contains `_createdby_value`.
- CommunicationByRegardingReadTests: by-regarding `$select` contains `_createdby_value`.
- ExternalMessageEndpointsTests / ExternalNotificationFeedTests: the two exact `$select` pins now include `_createdby_value`.
- Before the change: 5 failed / 173 passed (filtered 4 classes). After: 0 failed. Broader (Services.Communication + Api.ExternalAccess + Infrastructure.ExternalAccess): 2649 passed, 0 failed.

## Risk / follow-ups (not fixed here)
1. Rows written by the app identity (BFF app-only create with no sprk_sentby and no createdbycontact, e.g. inbound-email or
   system-generated rows) will now show the app user's display name (createdby) instead of null. The brief's precedence
   specifies this; if undesirable, gate the fallback on Direction == outgoing. Owner call.
2. Data quality: the MDA send path does not stamp sprk_sentby. Should be filed via /project-defer-issue-tracking (not run in this
   no-live-commands session) - please file.
3. Publish-size delta: not measured (no new package/DI/endpoint; three string constants). Reaches dev with the next BFF deploy.
