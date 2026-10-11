# Task 034 — External-plane message send endpoint (FR-11)

Date: 2026-10-11. Rigor FULL (tags bff-api, auth, testing, regarding). Implemented by a wave implementer agent in an isolated worktree; the review passes are the main session's.

## What it does

`POST /api/v1/external/{root}/{id}/messages` posts a plain-text message to the thread of one record, as the caller. It sends no email.

- **Group.** It is mapped beside the task 033 GET in `ExternalMessageEndpoints.MapExternalMessageEndpoints`, inside the `/api/v1/external` group. It therefore inherits `ExternalCollaboration` and `CallerPrincipalAuthorizationFilter`. The handler never branches on plane.
- **Roots.** The same table as the read: `projects`, `matters`, `workassignments`, `invoices`, `servicerequests`. Any other root is 404 `EXT_MESSAGES_ROOT_NOT_SUPPORTED`, with nothing read or written. `documents` stays unsupported (task 033 decision 5).
- **Order of checks.** Nothing is read until the request and the sender are valid, and nothing is written until the gate grants Create:
  1. The root lookup.
  2. Attachments are refused with 400 `EXT_MESSAGES_ATTACHMENTS_NOT_SUPPORTED`. This covers any request property whose name contains `attach`, or that is named `file` or `files`.
  3. The body (`sprk_body`) is required, may not be blank, and may be at most 10,000 characters. Otherwise the answer is 400 `EXT_MESSAGES_INVALID_BODY`.
  4. The sender must be attributable (`ExternalDataService.CanStampCreator`). Otherwise the answer is 403 `sdap.external.messages.sender_unattributable`, with no read.
  5. The record gate with Create (`GetRecordRightsAsync`, below).
  6. `CommunicationService.PostToRecordThreadAsync`.
- **Record gate.** The read gate was split into one private evaluation, `EvaluateRecordGateAsync`. `IsRootRecordReadableAsync` (GET) and `GetRecordRightsAsync` (POST) both use it, so the two routes cannot disagree about Read.
  - A record outside the set, an absent record and a caller with no grants all get the same 403, `sdap.external.messages.record_not_accessible`. The POST therefore has no existence oracle either.
  - Once Read passes, POST also needs **Create**:
    - project, matter and work assignment: `CallerPrincipal.RightsOn(root, id)`, the same dispatch the to-do and document routes use;
    - invoice: the union of the caller's rights on the parent roots the gate read (`sprk_matter`, `sprk_project`);
    - service request: Read and Create, because the gate's requester scope is the caller's own request and FR-11 lets the requester answer on it. This matches `RightsForServiceRequestAsync` on the document routes.
  - A caller with Read but no Create (ViewOnly) gets 403 `sdap.access.deny.insufficient_rights`. That caller can already read the thread, so the answer reveals nothing.
- **Write.** `CommunicationService.PostToRecordThreadAsync` is a new public method on the existing service. It extends the pipeline rather than forking it:
  - **The shared filing step.** It builds one `sprk_communication` and files it through the existing `ResolveOutboundOwnerAsync`:
    - the regarding lookup through `RegardingLookupMap`;
    - the FR-26 core-ancestor stamp;
    - the denormalized regarding fields;
    - the owner, which is the record's team. A refusal is a 409 and nothing is written.
  - **The thread.** It joins the record's thread through the existing best-effort `ResolveOutboundMessageThreadAsync`. With no ACS id, the resolver uses the record's default thread (Tier 2).
  - **No send.** It makes no channel dispatch, no Graph call, no ACS call and no OBO. It does not use `HttpContext`; everything is app-only through the BFF's existing Dataverse identity.
- **Row shape.**
  - Type Message.
  - A **contact** author's post is Incoming / Delivered, the same shape `MessagingIngestor` writes for an inbound chat message. A **systemuser** author's post is Outgoing / Send, plus `sprk_sentby`. Both statuses are on the read's allow-list, so the post shows in the task 033 read.
  - `sprk_bodyformat` = PlainText, always.
  - `sprk_isinternalonly` = false, written explicitly. The read hides a row whose flag is unset from every contact.
  - Subject `(No Subject)`. This is the same default as the internal message path.
  - `sprk_from` = the principal's email, when it has one.
  - No `sprk_to`, no Graph or ACS ids, no attachments.
- **Sender.** The sender always comes from the resolved principal. `ExternalDataService.StampCreator` gained an SDK-`Entity` overload: the same decision, the same strip of every creator column on the row, and one stamp. That stamp is `sprk_createdbyperson` for a systemuser, or `sprk_createdbycontact` for a contact. The kind decides, not the plane: a systemuser with a linked contact is a systemuser. The overload runs last, after the owner is applied.
  - `PostExternalMessageRequest` is closed. It binds only `sprk_body`. A sender, from, creator, format or internal-only value in the request does not bind.
  - Extra properties are captured (`[JsonExtensionData]`) only so that attachments can be refused.
- **Response.** 201 with `Location: /api/v1/external/{root}/{id}/messages` and an `ExternalMessageDto` built from the values written. `sendername` is null there, because the display name comes from Dataverse; the thread read returns it.
- **Read change (sender name, review 033 K1/K3).** `ReadRegardingAppOnlyAsync` (the app-only external read only) now also selects `_sprk_createdbycontact_value`.
  - `ParseMessageRow` takes the sender name from `sprk_sentby`'s FormattedValue, and otherwise from the contact creator's FormattedValue.
  - Internal reads do not select the new column, so they are unchanged.

## Decisions

1. **HTML is not sanitized; external posts are stored as plain text** (review 033 K1, the constraint's second option).
   - Whatever the body contains is stored verbatim with `sprk_bodyformat` = PlainText. The format is never taken from the request.
   - Every renderer escapes a plain-text body:
     - `MessageRow.tsx` and `MessageBubble.tsx` render plain text as React text, and sanitize only when `bodyFormat === 'html'`;
     - the model-driven form's `sprk_body` control is the standard multiline text control (`Format=text`, not rich text);
     - the external read returns the format with the body.
   - Rejected alternatives:
     - A server-side HTML sanitizer would add a package (bff-extensions) for content that never needs to be HTML.
     - Rejecting bodies that contain `<` would refuse ordinary text such as "a<b" or "<see attached>".
2. **Direction and status come from the author's kind:**
   - a contact's post is Incoming / Delivered, which the organisation received;
   - a systemuser's post is Outgoing / Send.

   Internal thread UIs align messages by direction, so marking a partner's message as "Outgoing / Sent" would mislabel it. Both values are on the task 033 allow-list.
3. **Create is required to post** (project `CLAUDE.md`: "an upload or create needs Create").
   - The POML's "accessible set" is the Read-bearing set, so a ViewOnly grant is refused with 403 insufficient_rights.
   - An invoice's rights are the union over its parent roots, mirroring the read's OR rule (task 033 K1).
4. **Thread assignment uses the existing message-thread step** (Tier 2 default thread for the record). The internal by-regarding read groups messages through threads, so without this an external post would be missing from the internal view of the record. Assignment is best-effort (NFR-02), as on every other send path.
5. **No member notification here.** `CommunicationArrivedProducer` and the participant index are not invoked. FR-11's "told through the C3 path" is task 036. Recorded here so task 036 decides whether the spine's communication-arrived event should fire for external posts.
6. **Multipart requests.** A `multipart/form-data` request (a file upload) never reaches the handler: the JSON-bound route answers it with 415 from the framework. That is a refusal with no row. Only JSON-carried attachments get the coded 400.

## Escalation check (POML STOP trigger: a post with no Graph send breaks an invariant)

**Not triggered.** I searched for anything that needs a communication to carry a Graph or ACS id or a mailbox:
- `sprk_graphmessageid` is read only as an equality dedupe key (`DataverseServiceClientImpl` :3142/:3247, the inbound processors and the reconciliation services). A null value never matches.
- `EmlGenerationService` already tolerates a null id (`sprk_graphmessageid ?? string.Empty`, :51).
- `MessagingThreadKeyStrategy` returns `Skip` for a missing ACS id, and the resolver then descends to the record's default thread (`ThreadResolver.ResolveViaFallbackLadderAsync`).
- `MessagingIngestor` already writes Message / Incoming / Delivered rows that carry no mailbox.
- No server query filters on `sprk_direction`.

The post has the same shape as an inbound chat message, minus the ACS ids.

## §11 component justification

**`POST /api/v1/external/{root}/{id}/messages`** (a new endpoint in the existing `ExternalMessageEndpoints.cs`)
1. Existing — `POST /api/communications/send` (`CommunicationService.SendAsync` / `SendMessageAsync`). It is internal-plane only and requires a systemuser. It sends email (Graph, either OBO `/me/sendMail` or a shared mailbox) or ACS chat.
2. Extension — the pipeline is extended in place (a new method on `CommunicationService` that reuses the filing, owner and thread steps). The route is new because the external plane needs the `ExternalCollaboration` group, the Tier-2 gate with Create, and the principal-based creator stamp; contacts have no systemuser and no mailbox (#1464).
3. Cost of doing nothing — a granted user cannot send a message on a record or answer a question on their service request (G6, FR-11).

**`CommunicationService.PostToRecordThreadAsync` + `MaxThreadPostBodyLength`** (new public surface on an existing service)
1. Existing — `SendMessageAsync` and `BuildMessageDataverseRecordAsync`. Both dispatch to ACS first and need an `HttpContext` user with a systemuser.
2. Extension — `PostToRecordThreadAsync` is that extension: it reuses `ResolveOutboundOwnerAsync`, `RegardingLookupMap`, `_genericEntityService` and `ResolveOutboundMessageThreadAsync`, and skips only the send. Changing `SendMessageAsync` itself would put a "no send" branch into the ACS path.
3. Cost of doing nothing — the route would need its own `sprk_communication` writer, which is the fork the POML forbids.

**`ThreadPostResult`** (a new result record in `Services/Communication/Models`) — the method's return shape (the created id plus the values written). `SendCommunicationResponse` requires `GraphMessageId` and `From`, which a post does not have.

**`PostExternalMessageRequest`** (a new request DTO) — the closed request shape. That closure is what makes "the sender is never client input" hold.

**`ExternalDataService.StampCreator(Entity, CallerPrincipal)` + `CreatedByContactColumn`** (an overload of the task 030 method)
1. Existing — `StampCreator(IDictionary, CallerPrincipal)`, which handles only the Web API payload shape.
2. Extension — it is an overload of the same method: the same `EnsureCanStampCreator`, the same `NamesCreatorColumn` strip, one stamp. The SDK shape needs a logical name and an `EntityReference`, not a navigation-property bind.
3. Cost of doing nothing — the post would carry no author, so the read would show no sender name for a contact and FR-25 would be broken for messages.

**`ExternalMessageEndpoints.GetRecordRightsAsync` / `EvaluateRecordGateAsync`** — a refactor of the existing gate plus a rights read. The GET's behaviour is unchanged; its 39 endpoint tests pass.

No new DI registration, package, options class, secret or column. ADR-010 is not affected.

## Placement Justification (`.claude/constraints/bff-extensions.md`)

This lives in the BFF:
- in the existing external-access endpoint file, in the existing `/api/v1/external` group;
- in the existing `CommunicationService`.

The external plane's authorization (CallerPrincipal, the module registry) and the communication write pipeline (filing, ownership, threads) both live in the BFF and are composed in-process. This is request-scoped CRUD: no AI, no background work, no new package, no new options or secret.

## Tests

`tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMessageSendTests.cs` has 40 cases.

**How it is driven.** Through `PostMessageCoreAsync`, then the PRODUCTION module registry, then the REAL `CommunicationService.PostToRecordThreadAsync` (real filing step, real creator stamp). Only the I/O boundaries are doubled:
- the Dataverse create, which records the row;
- the gate's record read;
- the ownership double;
- the thread resolver;
- the channel senders, which THROW on any call;
- `ICommunicationDataverseService`, `IDocumentDataverseService` and `ICommunicationEnrichmentService` (all Strict).

The read-back runs the REAL task 033 read (`GetMessagesCoreAsync` → `ReadRegardingAppOnlyAsync` → `CommunicationAccessFilter`) over the created row, as Dataverse would return it.

| Acceptance criterion | Test(s) |
|---|---|
| AC1: accessible record → 201, and the post appears in the thread (task 033 read) | `AccessibleRecord_ContactPost_Is201_OneRowRegardingTheRecord_AndTheThreadReadShowsItWithTheSender` (projects, matters, work assignments): regarding lookup, Message, Delivered/Incoming, PlainText, internal-only false, the team owner, one thread-resolver call with Message and no ACS id, then the read-back shows the message with sender "Pat Partner". Also `SystemUserPost_Is201_Outgoing_SentByTheCaller_AndTheThreadReadShowsTheSender` and `InvoiceUnderAMatterTheCallerCanCreateOn_Is201_RegardingTheInvoice` |
| AC2: outside the set → 403, no row (foreign record, nonexistent id) | `AnotherPartysProject_Is403_AndCreatesNoRow`; `AnotherPartysInvoice_AndANonexistentInvoice_GetTheSame403_AndCreateNoRow`; `NonexistentRecord_Is403_NotA404_AndCreatesNoRow` (5 roots); `CallerWithNoRecordGrants_Is403_WithNoDataverseReadOrWrite` (4 roots) |
| View-only caller (brief) | `ViewOnlyProjectCaller_Is403InsufficientRights_AndCreatesNoRow`; `ReadOnlyMatterOrWorkAssignmentCaller_Is403InsufficientRights_AndCreatesNoRow`; `InvoiceUnderAReadOnlyMatter_Is403InsufficientRights_AndCreatesNoRow` |
| AC3: the sender is the caller even when the request names another | `ContactCaller_RequestNamingAnotherSender_IsStoredWithTheCallersContactStamp_Only`; `SystemUserCaller_RequestNamingAnotherSender_IsStoredWithTheCallersPersonStamp_Only`. The JSON names a contact, a person, `createdby`, `sprk_sentby`, `sprk_from`, `sender`, an HTML format and internal-only true, and is bound with web defaults as the route binds it |
| AC4: no email, no Graph | `APost_NeverReachesAChannelSender_OrAnyIdentityLookup`. Every positive test also asserts `ShouldHaveSentNothing` (throwing Email and Message senders, a Strict identity lookup) |
| AC5: attachments → 400; empty or over-long body → 400 | `ARequestWithAttachments_Is400_BeforeAnyReadOrWrite` (4 shapes); `AnEmptyBody_Is400_AndCreatesNoRow` (null, empty, whitespace); `AMissingRequestBody_Is400_AndCreatesNoRow`; `AnOverLongBody_Is400_AndCreatesNoRow_ButTheLimitItselfIsAccepted` (10,001 → 400; 10,000 → 201) |
| Script-bearing HTML body (brief, review 033 K1) | `AScriptBearingHtmlBody_IsStoredAndReadBackAsPlainText_NeverAsHtml`: stored verbatim, PlainText, read back as PlainText even when the request asks for HTML |
| AC6: the requester posts on their own service request; another caller gets 403 | `Requester_PostsOnTheirOwnServiceRequest_Is201_RegardingTheRequest`; `SomeoneElsesServiceRequest_Is403_AndCreatesNoRow` |
| Unattributable principal (brief) | `AnUnattributablePrincipal_Is403_BeforeAnyReadOrWrite` (no contact and no systemuser; an empty systemuser id) |
| Route mapped and authorized | `ExternalMessageRouteTests` (updated): GET and POST are mapped for all 5 roots, PUT and DELETE are unmapped, and both routes carry authorization metadata |

Tests beyond the stated criteria, one line each:
- `CiamRequester_PartnerSwitchOff_Is403_AndSwitchOn_Posts` — the requester scope honours FR-06's switch (gotcha 2026-10-10: test both settings).
- `UnsupportedRoot_Is404_AndCreatesNoRow` — the root table is shared with the read; documents stay 404.
- `AnOwnerRefusal_Is409_AndCreatesNoRow` — pins that the shared filing step (task 146 owner) runs before the create and that its refusal surfaces.

## Fail-before and seeding proofs

**Against the pre-task source.** The five changed source files were reverted to `HEAD` (`0f0409899`) and `ThreadPostResult.cs` was removed:
- With the new test file present, the test project does not compile: CS0246, `PostExternalMessageRequest` not found.
- With the new test file set aside, the updated route tests run and fail: **6 failed / 39 passed of 45**. These are `MessagesRoute_IsMapped_ForEverySupportedRoot` ×5 (POST unmapped) and `MessagesRoutes_AreGetAndPostOnly_AndEachRequiresAuthorization`.

**Perturbations.** Each was applied in place to the task's code, run against `ExternalMessageSendTests` (40 cases), and then restored from a backup copy; `diff -q` confirmed the files were identical afterwards.

| Perturbation | Result |
|---|---|
| **Seeding proof (POML step 5): Tier-2 gate bypassed.** `var rights = await GetRecordRightsAsync(...)` replaced by `Read \| Create` | **17 failed / 23 passed**: every outside-set case (foreign project; foreign and nonexistent invoice; nonexistent ×5; no grants ×4), the someone-else's-request case, the CIAM switch-off case, the three insufficient-rights cases, and the requester case (no gate read) |
| Create check removed | **3 failed**: the three insufficient-rights cases |
| Attachment refusal removed | **4 failed**: the attachment shapes |
| Body stored as HTML (`BodyFormat.HTML`) | **5 failed**: the script case, AC1 ×3, and the contact sender case |
| Creator stamp removed (`StampCreator(communication, author)`) | **7 failed**: AC1 ×3, both sender cases, both service-request posts |
| `sprk_isinternalonly = false` not written | **5 failed**: the read-back hides the row from the contact (AC1 ×3, script, contact sender) |
| Status `Queued` instead of Delivered/Send | **5 failed**: the read-back drops it (AC1 ×3, systemuser, script) |
| Read: contact creator not used for the sender name | **3 failed**: AC1 ×3 (sender name null) |
| Unattributable refusal removed | **1 failed**: `AnUnattributablePrincipal_Is403_BeforeAnyReadOrWrite` |

Restored: **40 passed, 0 failed**.

## Build, tests, publish size

| Command | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet build tests/integration/Sprk.Bff.Api.IntegrationTests/` | 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~External\|FullyQualifiedName~Communication"` | 3743 passed, 0 failed, 8 skipped |
| `dotnet test tests/Spaarke.ArchTests/` | 940 passed, 0 failed (after the two ledger updates below) |
| Full unit suite `dotnet test tests/unit/Sprk.Bff.Api.Tests/` | 19665 passed, 1 failed, 54 skipped (36 min, machine under load). The one failure, `ChatMessageStreamingContractTests.SendMessage_ModelThrowsAfterTwoTokens_EmitsTypingEndThenSafeErrorAndNothingAfter` (AI chat streaming, untouched by this task), took 1 m 57 s in the run; rerun alone, its class passes 6/6. It is timing-sensitive under load, not a regression. |
| `dotnet list ... package --vulnerable --include-transitive` (BFF) | no vulnerable packages; no package added |

**ArchTests updated (the guards' own maintenance path, not a bump to go green):**
- `RouteAuthorizationGuardTests.Ledger.cs`:
  - the `ExternalMessageEndpoints.cs` GovernedFile reason now names the POST;
  - a new **HandlerDecision** for `POST /api/v1/external/{root}/{id:guid}/messages` — `PostMessageAsync` → `PostMessageCoreAsync` → `GetRecordRightsAsync`, seam `IsRecordAccessible`, verified by the guard;
  - the GET's line citations were refreshed.
  - The endpoint-file count is unchanged (119): no new file.
- `RecordOwnerAssignmentCensusTests.cs`: `CommunicationService.cs` `sprk_communication` creates go from 3 to 4. The new create is `PostToRecordThreadAsync`, which is Routed: ForChild through `ResolveOutboundOwnerAsync`, and a refusal throws before the create.

**Publish size.** This follows `.claude/constraints/azure-deployment.md`, "BFF Publish-Size Per-Task Verification Rule":
- command `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`;
- framework-dependent linux-x64 (the project's RID);
- Release configuration;
- `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`;
- **PDBs included**.

Both sides were fresh `git archive` exports (`src/server`, `config`, `Directory.*.props`, `global.json`) at short paths.

| Build | Path | Files | Zip bytes |
|---|---|---|---|
| origin/master `43b4eae92` | `C:\tmp\m034` | 192 | 38,210,528 |
| this branch (`0f0409899` + the task 034 commit) | `C:\tmp\b034` | 192 | 38,238,121 |
| **Delta** | | | **+27,593 B (+0.03 MB)** (covers the whole R3 branch against master, not only task 034) |

The total is about 36.4 MB, under the 55 MB review threshold and the 60 MB stop. No package was added.

## Deploy precondition (carry to task 038)

The external read now selects `_sprk_createdbycontact_value` on `sprk_communication`. **Task 020's `sprk_createdbycontact` schema must be imported into the target environment BEFORE this BFF build is deployed.**
- If it is not, the external message GET fails: Dataverse returns 400 for an unknown `$select` column, and the endpoint returns its coded 500.
- A contact's POST fails on the unknown attribute as well.

This is the same ordering task 030 already imposes (TASK-INDEX row 038: "020 columns live in dev first"). It now also covers the message read.

## Known limits (not fixed here)

- `sendername` is null in the 201 response. The display name is Dataverse's, from the stamped lookup; the thread read returns it.
- There is no member notification on a post (task 036). The notification spine's communication-arrived event and the participant index are not written for external posts.
- An invoice under two roots: Create on either parent admits a post, mirroring the read's OR rule (task 033 K1).
- A multipart upload is 415 from the framework, not the coded 400.
- (Resolved in the review fix round, item 4.) `PersonBearingWriters` in `RecordOwnerAssignmentCensusTests` did not list `PostToRecordThreadAsync`. The guard did not require it, because the person stamp goes through `ExternalDataService.StampCreator`, not `RecordCreatorPersonColumn` in this file. A reviewer may want it added for completeness.

## Files

- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalMessageEndpoints.cs` — the POST route, `PostMessageCoreAsync`, `GetRecordRightsAsync`, `EvaluateRecordGateAsync` (the shared gate), `NamesAttachment`, and the codes.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/Dtos/ExternalMessageDtos.cs` — `PostExternalMessageRequest`, and the `SenderName` doc.
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` — `PostToRecordThreadAsync`, `MaxThreadPostBodyLength`.
- `src/server/api/Sprk.Bff.Api/Services/Communication/Models/ThreadPostResult.cs` (new).
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadService.cs` — the contact sender name on the app-only read.
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs` — the `StampCreator(Entity, CallerPrincipal)` overload and `CreatedByContactColumn`.
- `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMessageSendTests.cs` (new, 40 cases).
- `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMessageEndpointsTests.cs` — `ExternalMessageRouteTests`: POST is now mapped.
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`, `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs`.

`TASK-INDEX.md`, `current-task.md` and the POML status are left to the main session (wave rule).

## Review fix round

Two adversarial reviews found no F-class defects; this round applies the agreed follow-ups.

### Fixed

1. **Throttle.** `POST /api/v1/external/{root}/{id}/messages` now carries `.RequireRateLimiting("dataverse-query")` (50/min per user, sliding window), an existing policy — no new policy added (CLAUDE.md §11: nothing new to justify). The partition key was checked for a CIAM token: `RateLimitingModule.GetUserId` uses `CallerResolution.ResolveOpaqueCallerKey` (oid in either claim form, then `sub`, then `NameIdentifier`, then the IP), all of which a CIAM token carries. `UseRateLimiter` runs after `UseAuthorization`, and the `ExternalCollaboration` policy's own authentication populates `HttpContext.User`, so the key is the CIAM caller and not a shared IP bucket. `ProducesProblem` now lists 415, 429 and 502 as well. Test: `ExternalMessageRouteTests.PostRoute_IsRateLimited_OnAPerUserPolicy`.
2. **`isMine` (for tasks 037/051).** `ThreadMessageDto` gains `CreatedByContactId` (parsed from `_sprk_createdbycontact_value`; `JsonIgnore`, so the internal wire contract is unchanged). `ExternalMessageDto` gains a boolean `isMine` only — no contact or systemuser id crosses the external surface. Rule (`ExternalMessageEndpoints.IsAuthoredBy`), the same kind decision the creator stamp makes: a systemuser principal owns a message when `SentBy == SystemUserId`; any other principal owns it when `CreatedByContactId == ContactId`. An empty id never matches. The 201 returns `isMine: true`. Tests: own contact post true, another contact's false, a systemuser post false, a contact-plane workforce caller the same; systemuser caller own true, colleague false, contact post false (even one by the caller's linked contact); no ids in the serialized body.
3. **ProblemDetails key.** The `SdapProblemException` pass-through writes `errorCode` (was `code`), like the 400/404/500 paths. `ProducesProblem(502)` added (and 415/429). Test: `AnOwnerRefusal_Is409_AndCreatesNoRow` asserts `errorCode` present and `code` absent.
4. **Census guard.** `PersonBearingWriters` lists `CommunicationService.cs / PostToRecordThreadAsync`; `PersonToken` counts `StampCreator` and `EnsureCanStampCreator`. Must-fire control: with both calls removed from `PostToRecordThreadAsync` the census test fails ("PostToRecordThreadAsync: creates … a row for the external-plane author (StampCreator) without naming them"); restored, 30/30 pass. The removal was not committed.
5. **Direction.** `ResolveOutboundMessageThreadAsync` takes an optional `direction` (default Outgoing, so every SendAsync path is unchanged); `PostToRecordThreadAsync` passes its computed direction, so the resolver request and envelope match the row (Incoming for a contact author, Outgoing for a systemuser). Tests: `ContactPost_ThreadRequestIsIncoming_MatchingTheRow`, `SystemUserPost_ThreadRequestIsOutgoing_MatchingTheRow`.
6. **HTTP-level POST tests.** New `ExternalMessagePostContractTests` (`tests/integration/contract/Api/ExternalAccess/`, on the existing `ExternalAccessContractFixture`): unauthenticated 401; JSON with an `attachments` / `attachmentDocumentIds` / `Files` key is 400 `EXT_MESSAGES_ATTACHMENTS_NOT_SUPPORTED` through the real body binding; `multipart/form-data` is 415 (the framework's answer — the route accepts `application/json` only); an empty JSON body and a blank text are 400 `EXT_MESSAGES_INVALID_BODY`.

### Recorded known limits (owner decisions / follow-ups; not fixed)

- K-dual-root: an invoice post propagates `sprk_regardingproject` to a root the caller may not see, and threads under the matter's default thread (owner decision, tied to the 031 invoice question; intersection is a one-line change in `RightsThroughParents`).
- K-contact author name: it is shown to every external reader of the record (consistent with the 033 decision; diverges from the versions route, which keeps `CreatedByName` null — owner call).
- K-no idempotency: a double POST creates two rows (task 051 disables the send button while in flight).
- K-length is counted in UTF-16 code units.
- K-every external post is named `Message: (No Subject)`.
- K-the 201 body has `sendername: null` and a server-time `createdon` (task 051 re-fetches after a 201).
