# Defer / Issue Tracking — spaarke-SPA-external-access-platform-r3

> **Source of truth** for deferred work and newly found issues in this project. Each entry has a paired GitHub Issue; see the `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label spaarke-SPA-external-access-platform-r3`.
> **CLAUDE.md §11 rule**: every entry names a concrete behaviour or contract that fails without it.

---

## Open (in priority order)

### ISS-001 — AuthorizationModule: Copilot audience merge and failure logging applied once per process, not per options instance

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-10 |
| **Source** | R3 task 010; confirmed by both adversarial review passes |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1623 |

**Description**

The default JwtBearer scheme's Copilot-audience merge and its `CopilotAuth` sign-in-failure logging run in a `PostConfigure` guarded by a static once-per-process flag (`_jwtPostConfigureApplied`), instead of once per options instance.

Any later `JwtBearerOptions("Bearer")` instance silently loses the Copilot audience (Copilot tokens then get 401) and the failure logging. Two ways to get a later instance:
- a second test host in the same process;
- an instance rebuilt after a configuration reload (`JwtBearerConfigureOptions` binds `IConfiguration` with a change token).

This is pre-existing and not caused by R3. R3's CIAM guard is a separate `PostConfigure` without the flag, so it is unaffected.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs:37` (the flag)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs:106-111` (the gated `PostConfigure`)
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/AuthorizationModuleCiamTests.cs` (the harness re-adds the Copilot audience to work around it)

**Suggested fix**

Make the merge idempotent per options instance, for example by checking whether the Copilot audience is already present. Then remove the test-harness workaround.

**Estimated effort**: 2–4 hours, including a test with two hosts in one process.
**Blockers**: none.
**Related**:
- `AuthorizationModule.cs` is shared with unified-access-control-r2.
- A candidate for the auth-system-of-record remediation phase.
- Not yet confirmed: whether a production reload actually rebuilds this instance.

---

### ISS-002 — ChatMessageStreamingContractTests mid-stream error test fails intermittently under full-suite load

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-10 |
| **Source** | R3 task 031 full unit-suite run (19512 passed, 54 skipped, 1 failed) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1633 |

**Description**

`SendMessage_ModelThrowsAfterTwoTokens_EmitsTypingEndThenSafeErrorAndNothingAfter` failed once in a full `dotnet test tests/unit/Sprk.Bff.Api.Tests/` run on the R3 branch. It took 2 min 25 s under load and passes 6/6 alone. Task 031 does not touch AI chat code. A load-dependent contract-test failure fails CI at random. The failure message was not captured, so the cause is unconfirmed.

Second occurrence 2026-10-11, full run after the task 034 merge (`ab4e91011`): 1 failed of 19,735, the same test, 2 min 47 s; it passes 6/6 alone and the merge touched no AI code. The failure message was again not captured (output filtered).

**Entry-points**

- `tests/integration/contract/Api/Ai/ChatMessageStreamingContractTests.cs:194`
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ChatMessageStreamingContractTests"` (passes alone)

**Suggested fix**

Capture the failure with `--logger trx` on a full run. Then check for a fixed SSE read timeout, or shared `_fixture.ChatClient` scripting across parallel tests.

**Estimated effort**: 2–4 hours, mostly reproduction.
**Blockers**: none.
**Related**: AI chat area; not R3 scope.

---

### ISS-003 — Shared sanitizeEmailHtml keeps style positioning and remote images

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-11 |
| **Source** | R3 task 051 adversarial review (F2 on the external surface, K2) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1636 |

**Description**

The shared `sanitizeEmailHtml` keeps the `style` attribute and removes only executable tokens. It also allows `img src` over http(s).
- An inbound email body can therefore use `position:fixed; inset:0; z-index:…` to paint over the whole host surface, for example a fake "sign in again" panel.
- Remote images and CSS `url(...)` load when someone opens the message, which tells the sender the reader's IP and read time.

Consumers: the internal CommunicationTimeline `MessageRow` and ConversationView `MessageBubble`; the Spaarke.Communication.Components email views; and the R3 external SPA Messages section, which external contacts read.

R3 confines HTML bodies in the external SPA with `contain: paint` (task 051 fix round). The other consumers are unchanged.

**Entry-points**

- `src/client/shared/Spaarke.UI.Components/src/utils/sanitizeEmailHtml.ts:131` (`img`), `:148` (`style`)
- `src/client/external-spa/src/components/detail/RecordMessages.tsx` (the R3 containment)

**Suggested fix**

Strip `position`, the `inset` family, `z-index` and `url(` from `style` in the shared util. Add a `blockRemoteImages` option for low-trust consumers.

**Estimated effort**: 4–8 hours.
**Blockers**: none.
**Related**: R3 task 051; ISS-003 is not R3 scope because the util is shared.

---

### ISS-004 — Internal document delete orphans the SPE file when the file delete fails

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-11 |
| **Source** | R3 task 032 implementation (delete-own) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1650 |

**Description**

`DocumentCheckoutService.DeleteAsync`, behind the internal `DELETE /api/documents/{documentId}`, deletes the SharePoint Embedded file first. If that call throws, it logs a warning and deletes the Dataverse row anyway.

A transient Graph failure (timeout, 429 or 5xx) therefore leaves the file in the container with no `sprk_document` row pointing to it. The user believes the content is gone, but it is still stored, and nothing in the app can find or delete it later.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs:794-820`: the catch-all around `_speFileStore.DeleteFileAsync`.
- The R3 external overload (task 032) keeps the row and returns 503 on a file-delete failure.

**Suggested fix**

- Use the R3 external behaviour on the internal route: keep the row and return 503 on a file-delete failure.
- Treat "file not found" as success.

Needs a decision from the owner of the internal document UX.

**Estimated effort**: 1–2 hours, including tests.
**Blockers**: none.
**Related**: R3 task 032 (FR-26). Out of R3 scope: the internal route is shared.

---

### ISS-005 — Binder-created contacts have no BU-team owner (outside I-6); intake visibility in child BUs

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-12 |
| **Source** | Task 035 scoped re-check K3 (ownership fix `30fc4922a`); confirmed in code and by a read-only dev query |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1673 |

**Description**

The workforce identity binder creates contacts app-only with no `ownerid` (`ContactIdentityStore.CreatePayload`), so each such contact is owned by the BFF application user and sits in that user's business unit (the root BU). Contacts are outside invariant I-6 and the owner census. Task 035 owns an intake request by the default team of the requester contact's BU (owner decision 2026-10-11), so for a customer whose staff sit in a child BU, intakes from binder-created contacts land on the root BU's team and the customer's BU-level users never see them. Dev is unaffected today: 15 of 17 bound contacts are in the root BU "Spaarke", which holds 172 of 174 active users.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactIdentityStore.cs:298-331`
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordOwnershipResolver.cs` (`ResolveOwnerAsync`)
- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` I-6 row
- Dev check: `SELECT bu.name, COUNT(c.contactid) FROM contact c JOIN businessunit bu ON c.owningbusinessunit = bu.businessunitid WHERE c.sprk_externalobjectid IS NOT NULL GROUP BY bu.name`

**Suggested fix** (if known)

Decide the owning BU of a workforce contact (a per-stamp customer BU, or the matched systemuser's BU for licensed staff) and bind `ownerid` to that BU's default team in the binder create through `RecordOwnershipResolver`; add the seam to the census.

**Estimated effort**: 1-2 days including the owner decision
**Blockers**: owner decision on contact BU placement; binder code belongs to unified-access-control-r2 (task 141)
**Related**: R3 task 035; #1673

---

### ISS-006 — Dev: SPE JIT-grant revocation sweep fails every 5 minutes (Graph "Invalid hostname for this tenancy")

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday (re-triaged by UAC 2026-10-13: not a security gap) |
| **Filed** | 2026-10-13 |
| **Source** | R3 owner check 3 log review (read-only App Insights query, owner-approved) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1830 |

**Description**

On `spaarke-bff-dev`, `SpeContainerMembershipSync.RemoveRevokedJitGrantsAsync` throws Graph `ODataError` "Invalid hostname for this tenancy" about every 5 minutes: 287 in 24h up to 2026-10-14 01:45 UTC. It fails while reading container markers (`SpeContainerMembershipService.ReadMarkersAsync` → `ReadMarkersCoreAsync`, the `FileStorageContainer` GET). If every sweep fails, a revoked external user may keep SPE container membership, and with it file access. The owner is UAC; this is not R3 code.

**Entry-points**

- KQL (appId `6a76b012-46d9-412f-b4ab-4905658a9559`): `exceptions | where outerMessage contains 'hostname' | summarize count(), max(timestamp) by tostring(details[0].parsedStack[0].method)`
- `src/server/api/Sprk.Bff.Api/Services/Access/SpeContainerMembershipSync.cs`; `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/SpeContainerMembershipService.cs` (~:532-551)

**Suggested fix**: identify the container id the sweep reads (likely a container of another tenant or app, or a stale id). Decide whether one bad row should fail the whole sweep, or be skipped and logged so the remaining revocations still run.

**Estimated effort**: half a day to diagnose.
**Related**: unified-access-control-r2 (SPE membership / JIT grants).

**UAC triage (2026-10-13, verified by R3 against `SpeContainerMembershipSync.cs:356-377`)**: each record is caught separately, so a bad container pointer fails only its own record and the sweep continues. The rate (one per 5-minute pass) matches a single record. JIT grants are made only through the ownership guard, so none can exist on a container outside the tenancy. Fix (UAC, non-urgent): treat this Graph error like the existing `NotOwned` skip (Warning, not failed) and correct that record's pointer.

---

### ISS-007 — Service-request documents are indexed with no parent (not findable from the record)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-14 |
| **Source** | R3 task 063, second adversarial pass |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1839 |

**Description**

A document filed only to a Service Request is indexed with no parent. `DocumentIndexParentResolver` excludes the service-request link by design (`:76-77`), and `IntermediateTypes` (`:67-71`) lists only event and communication. Cross-record search (`scope=all`) drops rows with no parent (`SemanticSearchEndpoints.cs:592-598`), and `scope=entity` fails closed (`:503-505`). The document is reachable only through per-document, caller-trimmed surfaces gated by Read on the `sprk_document` row. No exposure; a findability gap. This is pre-existing, and it now also covers external uploads (task 063).

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Dataverse/DocumentIndexParentResolver.cs:56-95`
- `src/server/api/Sprk.Bff.Api/Api/Ai/SemanticSearchEndpoints.cs:503-505, 592-598`

**Suggested fix**: a service-request hop through its regarding/core stamps (`CoreAncestorResolver.cs:284-293`); re-check secure-record handling; backfill the index.

**Estimated effort**: half a day to a day, plus backfill.
**Blockers**: none.
**Related**: task 063.

---

### ISS-008 — Unused sprk_communication_send web resource maps Message to Email

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday |
| **Filed** | 2026-10-14 |
| **Source** | R3 task 065 trace of the model-driven message send |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1840 |

**Description**

No committed ribbon or form references `Sprk.Communication.Send`; `CommunicationActions/index.ts:8` says that control replaced it. The model-driven Message form sends through `CommunicationMessageActions`. The script's type switch (`:412-423`) sends Message (100000004) as "Email". If it is ever re-wired, a message goes out as a real email and creates a second Email record. As dead code, it misleads anyone tracing the send path.

**Entry-points**

- `src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_communication_send` (`:400-464`)
- `src/client/pcf/CommunicationActions/index.ts:8`

**Suggested fix**: confirm on dev that no command bar references it; remove it from the solution, or map 100000004 to "Message".

**Estimated effort**: 1-2 hours.
**Blockers**: none.
**Related**: task 065.

---

### ISS-009 — External read ignores thread privacy; two writers can put a partner-visible item into a Private thread

| Field | Value |
|---|---|
| **Status** | Done (task 065 fix round 5, commit 05e47af14; merged to R3) |
| **Urgency** | next-round |
| **Filed** | 2026-10-14 |
| **Source** | Task 065 re-check of fix round 3 (adversarial review), "outside the diff". Both paths existed before 065. |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1859 |

**Description**

The external read `CommunicationThreadReadService.FilterExternalMessagesAsync` hides a communication from partners (CIAM and workforce contacts) only on `sprk_isinternalonly`. It never looks at the thread's privacy. Task 065 makes every writer apply a "Private thread means internal" floor at write time, but two older writers still put a `false` row into a thread that may be Private:

- `CommunicationService.PostToRecordThreadAsync` (~:1395) writes `false` for a contact's own post into the record's default thread, even if that thread has been made Private.
- `ThreadResolver` re-file rules can move a `false` email into a record's default thread with no privacy check.

Failure mode: a record's default thread is made Private and a partner posts to it, or an email is re-filed into it. Every other partner with access to that record then sees the item.

There is a related design question for the owner: when a thread becomes Private, should partners stop seeing its earlier messages? Today a privacy change only affects later messages.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadService.cs` `FilterExternalMessagesAsync` (~:549)
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` `PostToRecordThreadAsync` (~:1395)
- `src/server/api/Sprk.Bff.Api/Services/Communication/Threads/ThreadResolver.cs`, the re-file rules
- Shared floor helper: `Services/Communication/CommunicationInternalOnlyFloor.cs` (task 065)

**Suggested fix**: gate the external read on thread privacy as well as the flag (hide any item in a thread not explicitly Open). That one change covers every writer and any later privacy change. Alternatively, apply the 065 floor in the two writers.

**Estimated effort**: 0.5-1 day, plus the owner's answer on retroactive privacy.
**Blockers**: task 065 merged.
**Related**: task 065 (notes/task-065-notes.md), ADR-028 external plane.

---

### ISS-010 — Internal Only is enforced by BFF reads, not by Dataverse security (licensed users flagged external)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-14 |
| **Source** | Task 065 final re-check (adversarial review). |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1864 |

**Description**

Task 065 hides internal-only and Private-thread communications from every external caller on the BFF's own reads:
- lists, counts and the feed;
- attachment text;
- the email AI tools.

A licensed user flagged `sprk_isexternal` (owner 2026-10-14: treated like a partner for Internal Only) still has Dataverse access, and three paths apply Dataverse security only:
- the generic `dataverse.read_query` chat tool (`DataverseReadQueryHandler`), which reads `sprk_communication` under the user's token;
- the model-driven app;
- the Dataverse Web API.

Failure mode: an external licensed user with read on a matter's communications runs `read_query` over `sprk_communication`, or opens the record in the model-driven app, and reads internal-only bodies. Contacts (CIAM and workforce) have no Dataverse access and are not affected.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseReadQueryHandler.cs`
- `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` (~:997, DELETE route: `CanCallerSeeMessageAsync`; needs Dataverse Write, so it adds no capability)
- Shared rule: `Services/Communication/CommunicationOpenThreadGate.cs`, `CommunicationThreadReadService.ReadVisibleCommunicationAsync`

**Quick fix done 2026-10-20 (task 078, owner decision the same day).** For an external or unresolved caller, `dataverse.read_query`, `dataverse.search_data` and `dataverse.describe` now apply the task 065 rule: `sprk_communication` rows are filtered, and other family tables are refused or dropped (`notes/task-078-notes.md`). It ships in the next dev BFF deploy. Still open: the model-driven app, the Web API, `spaarke.grid_overview` (ISS-014), RAG retrieval and chat archive ingest (ISS-016), and partner-visible document copies (ISS-015). Also still open: the write tools `dataverse.update_record` and `create_record`. An external caller with write on a row can set `sprk_isinternalonly` false, repoint `sprk_communicationthread` to an Open thread, or set a thread's `sprk_privacystate` to Open, then read the row. The model-driven app allows the same. Task 078 is read-side only, and its POML forbids changes to other tools.

**Suggested fix**

- Short term: done (task 078, above).
- Real fix: decide whether external licensed users get Dataverse read on communications at all (security role or owner-team design), or whether internal-only rows move under a team or BU they cannot read. Owner and UAC decision.

**Estimated effort**: 0.5 day for the `read_query` guard; the Dataverse-security design is unknown.
**Blockers**: owner/UAC decision on Dataverse access for licensed users flagged external.
**Related**: task 065, ISS-009 (#1859, done), UAC unified-access-control-r2.

---

### ISS-011 — External document lists show documents with no file; download and versions then answer 409

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-14 |
| **Source** | UAC-r2 task 187 live document leg on dev at R3 `4e23edeb2` (passed). Filed by UAC as #1886; tracked here because external documents are R3's surface. |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1886 |

**Description**

Two dev documents (`122f4462…`, `d4300ad0…`) have no SPE pointer. The external document list returns them, but download and versions answer 409 "No file attached", and the SPA gives no hint before the click.

Failure mode: a partner clicks a listed document and gets an error.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/ExternalAccess/ExternalDataService.cs` (DocumentRow / the document DTO)
- The external SPA document grid and its download action (`src/client/external-spa/`)

**Suggested fix**

**Owner decision 2026-10-14:** show the row without a download button. Add `hasFile` to the external document DTO; the SPA hides download and versions when it is false. Rows are not omitted.

**Estimated effort**: hours.
**Blockers**: none.
**Related**: task 066 (download/open only), DEF-003 (preview).

---

### ISS-012 — WorkforceHome scheme tests fail order-dependently in a full unit run (process-wide issuer-validator cache)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-15 |
| **Source** | Full `Sprk.Bff.Api.Tests` run on R3 `9bb7f5724` (R3 + master `f292ad119`), 2026-10-15 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1930 |

**Description**

In one full unit run, six `AuthorizationModuleWorkforceHomeTests` must-reject cases failed in about 1 s each:
- `WrongAudience_IncludingCopilot_Returns401` (3 cases)
- `IssuerTenantMismatch_Returns401`
- `SignedByKeyWhoseIssuerIsAnotherDirectory_Returns401`
- `ExpiredToken_Returns401`

Each asserts which validation exception the WorkforceHome scheme reported.

The same build then passed them alone (43/43), in the auth slice (5,285/5,285) and in two further full runs. The six were not saved to TRX on the failing run.

The test file's own header names the likely cause, `AadIssuerValidator.s_issuerValidators`. Microsoft.Identity.Web keeps it per process and keys it by authority. The WorkforceHome scheme's authority is fixed (`https://login.microsoftonline.com/organizations`, `AuthorizationModule.cs:571-572`), so any test that boots the real app and authenticates through the scheme can create that validator before this class does, without the class's `FakeEntraDiscovery` client.

Failure mode: an intermittent red full run (CI or local), which hides real failures or trains people to ignore this class. The product is not affected.

**Entry-points**

- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/AuthorizationModuleWorkforceHomeTests.cs` (header KNOWN LIMIT; `host.Failures` assertions at :320, :335, :346)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs:555-580` (`AddWorkforceHomeScheme`)
- Reproduce: `dotnet test tests/unit/Sprk.Bff.Api.Tests` repeatedly, with the TRX logger to keep the message

**Suggested fix**

Give the class a `[Collection]` with `DisableParallelization = true`, and make its hosts independent of the process-wide cache. For example, assert the 401 plus a deny reason the test owns rather than the library's exception type, or have each host register its own `IssuerValidator` delegate. Capture the failure message first, to confirm the cause.

**Estimated effort**: hours.
**Blockers**: none.
**Related**: task 045 (the scheme and these tests), task 046.

**Update 2026-10-16 (cause captured):** the same six failed in the full run on `a6d30891a`, and the TRX kept the message this time. Each host's `Failures` held `System.ObjectDisposedException: Cannot access a disposed object. Object name: 'EventLogInternal'` from `Microsoft.Extensions.Logging.EventLog.WindowsEventLog.WriteEntry`. So the immediate cause is the Windows EventLog logger provider writing through an EventLog another host already disposed; the failure is captured as an authentication failure and the assertion on the expected exception type fails. The AadIssuerValidator cache is not the cause. Alone: 37/37 pass. Suggested fix: clear the logging providers in the test host (`builder.Logging.ClearProviders()`, or the EventLog provider only) and/or filter `host.Failures` to authentication exceptions.

---

### DEF-001 — Generate and send the standard NDA from the intake request (separate project)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-12 |
| **Source** | Owner answer 2026-10-12 (intake Q5): NDA covers review and issuing our standard NDA; generating signable documents is a separate project |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1736 |

**Description**

R3's NDA wizard collects an "issue our standard NDA" request (signer name and email, term, governing law, needed-by date). Nothing generates the document from those answers or sends it for signature, so Legal fills every such request by hand.

**Entry-points**

- `notes/intake-form-design.md` §1, §6.1, §6.2
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalIntakeEndpoints.cs`; `sprk_servicerequest.sprk_intakedata`
- Related project: `projects/ai-advanced-capabilities-nda-r1`

**Suggested fix**: a new project for templates, generation, delivery and e-signature.

**Estimated effort**: unknown; needs a design.
**Blockers**: template ownership; e-signature provider.

---

### DEF-002 — Incident alerting for Policy Questions (email to a designated mailbox)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-12 |
| **Source** | Owner answer 2026-10-12 (intake Q3): keep R3 simple; alerting (e.g. email to a designated mailbox) later |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1737 |

**Description**

A Policy Question about something that already happened (breach, bribery, misconduct) waits in the normal service-request queue; nobody handling incidents is alerted.

**Entry-points**

- `notes/intake-form-design.md` §2, §6.1 answer 3
- `Communication:DefaultMailbox` / `ApprovedSenders` (`CommunicationOptionsValidator`)

**Suggested fix**: an incident flag or topic code, and a server-side notification to a per-customer designated mailbox through the approved shared mailbox, with no answer text in the email.

**Estimated effort**: 1-2 days once the per-customer mailbox exists.
**Blockers**: per-customer designated mailbox (#1562).

---

### DEF-003 — In-page document preview in the external SPA (R3 ships download/open only)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-13 |
| **Source** | Owner check 3 (2026-10-13): document detail shows the header but no file; owner decision: download/open is enough for R3, preview in R4 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1828 |

**Description**

An external user (partner or workforce contact) cannot preview a document inside the external SPA; opening one shows only its fields. R3 adds download/open (task 066). No preview component or external preview route exists.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalProjectDataEndpoints.cs:585-620`: the only content route, `GET /api/v1/external/{root}/{id}/documents/{documentId}/content` (app-only byte stream; no signed URL or SPE pointer by design).
- `src/client/external-spa/src/components/detail/RecordDetailFields.tsx:120-134` (fields only); `DocumentLibrary.tsx:720-741` (download helper).

**Suggested fix**: render the byte stream through a blob URL (PDF and images) in the document detail, or add a server preview route; keep the no-signed-URL rule.

**Estimated effort**: 2-4 days, including the file-type decision.
**Blockers**: task 066 first. Target: external access R4.

---

### DEF-004 — Create Invoice (+New) from the external SPA: deferred to the invoice intake project (ontology-r1)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday |
| **Filed** | 2026-10-13 |
| **Source** | Owner check 3 (2026-10-13): the only "+New" wanted is Invoices; it belongs to the larger invoice intake/processing project tied to `spaarke-ontology-platform-r1` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1829 |

**Description**

External users cannot submit an invoice through the portal; invoices are created only internally. The record detail is read-only by design (spec decision 2026-08-12), and the grids' "New" command needs Xrm, so it does nothing in the SPA.

**Entry-points (context for the invoice project)**

- Shared wizard `src/client/shared/Spaarke.UI.Components/src/components/CreateInvoiceWizard/`:
  - `invoiceService.ts` sets `sprk_invoicenumber`, `sprk_name`, `sprk_description`, `sprk_vendororg` and `sprk_invoicedate`;
  - it works through IDataService and the ADR-024 resolver, and its file pipeline dual-binds the file to the Invoice and the host;
  - it has no external-plane (app-only) create path.
- External BFF: invoice document routes only (list, download, upload, versions) under `/api/v1/external/invoices/{id}/documents` (`ExternalProjectDataEndpoints.cs:350-381`), plus message routes. There is no create route.
- Grid "New": `CommandBar.tsx:225-247`; `external-spa/src/widgets/GridWidgetBody.tsx:18-21`.
- R3 rules (project CLAUDE.md): an invoice document upload needs only the invoice's rights (the union of its parents); invoice threads stay any-parent.
- An external create would also need creator stamping (`sprk_createdbycontact`, the task 030 pattern) and an owner-team rule like ISS-005 (#1673).

**Suggested fix**: in the invoice intake project, an app-only, Tier-2-authorized external create route plus an SPA intake form, reusing `invoiceService`'s field manifest.

**Estimated effort**: unknown; it depends on the invoice intake design.
**Blockers**: the invoice intake project; ISS-005 (#1673).

---

### ISS-013 — No stamp sets or checks `removeUnverifiedEmailClaim`, which email-bound registration relies on

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round (before any customer stamp enables A9 workforce sign-in) |
| **Filed** | 2026-10-16 |
| **Source** | Task 047 adversarial review, known limit K1 (`notes/task-047-notes.md:186`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2001 |

**Description**

Task 047's registration endpoint binds a workforce sign-in to an existing contact by the token's `email` claim. That is safe only if `email` is domain-verified. Entra guarantees this only when the app registrations have `authenticationBehaviors.removeUnverifiedEmailClaim = true`. The design (`notes/a9-bff-design.md:77`, `:199`) and the provisioning coordination note (item 6) both say so.

Today nothing sets or checks it on a customer stamp:

- Provisioning has only the coordination note. No handler sets it on the stamp BFF app, and no verification step (H13) reads it back. Checked the provisioning worktree at `241c2a7dc`, 2026-10-16.
- The BFF does not check it at runtime either.

On dev, R3 task 059 sets it in the same step that makes the BFF app multitenant, so dev is not exposed.

Failure mode: suppose a stamp's BFF app or SPA client is provisioned without the flag. A user in a directory listed in that stamp's `WorkforceIdentity:CustomerTenantIds` whose mail attribute holds an unverified-domain address equal to an existing contact's email can register as that contact. That user then sees that contact's records.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalRegistrationEndpoints.cs:253` (`email` claim source)
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactIdentityBinder.cs` (email match to bind)
- `projects/customer-provisioning-orchestration-r1/notes/coordination/2026-10-13-from-spa-r3-adr028-a9-workforce-signin.md` row 6
- `projects/spaarke-SPA-external-access-platform-r3/tasks/059-*.poml` (dev setup)

**Suggested fix**

Provisioning H3 sets the flag on the stamp BFF app (get-before-set, read back), and H13 verifies it on the BFF app and the SPA client. Confirm on Learn whether the property is Graph beta only.

Optionally add defence in depth at runtime, independent of app configuration: request the `xms_edov` optional claim and have registration refuse an email match without it. This is a hypothesis; validate the claim's availability first.

**Estimated effort**: provisioning, hours. Runtime check: a day, including tests.
**Blockers**: none.
**Related**: ADR-028 A9; R3 tasks 047, 059; customer-provisioning-orchestration-r1 (H3, H13).
**Owner**: customer-provisioning-orchestration-r1 task 264 (post-T186 follow-up; accepted 2026-10-16). It first checks Learn for Graph beta-only, then H3 sets with get-before-set and a read-back, and H13 verifies both apps.

---

### ISS-014 — `spaarke.grid_overview` lists internal-only and Private-thread communications to callers flagged external

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round (closes with ISS-010's real fix) |
| **Filed** | 2026-10-20 |
| **Source** | Task 078 inventory (`notes/task-078-notes.md`, "Chat tools that can read them") |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2125 |

**Description**

`GridOverviewHandler` loads a `sprk_gridconfiguration` by the `configId` the model supplies and runs its saved FetchXML under the caller's OBO token (~:269, ~:356). Dataverse security alone decides the rows, and nothing checks whether the caller is external. Task 078 covered `read_query`, `search_data` and `describe`; this tool uses saved FetchXML, a different pattern.

Failure mode: a licensed user flagged external asks for an overview of the Communications/Messages grid (`e1826c4c-9575-f111-ab0e-7ced8ddc4a05`), or of any configuration targeting `sprk_communication*`. The tool returns that grid's columns for internal-only and Private-thread rows.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GridOverviewHandler.cs`
- Reuse: `Services/Ai/Handlers/Dataverse/ExternalCallerCommunicationGuard.cs`, `Services/Communication/CommunicationOpenThreadGate.cs`

**Suggested fix**

For an external caller, refuse a configuration whose target entity is in the `sprk_communication` family (simplest). Alternatively, add the flag and thread conditions to the FetchXML and apply the Open-thread post-filter. Internal callers stay unchanged.

**Estimated effort**: half a day, including tests.
**Blockers**: none.
**Related**: ISS-010 (#1864), tasks 065 and 078.

---

### ISS-015 — Partner document list can show a document copy of an internal-only email once it is filed to the record

| Field | Value |
|---|---|
| **Status** | Open — D7 accepted by the owner (2026-10-20); fix = uac-r2 task 188 (T7 predicate) + word-add-in task 130 (T8 writer gaps); deploy via R3 |
| **Urgency** | next-round |
| **Filed** | 2026-10-20 |
| **Source** | Task 078 review pass 1 (F1) plus a code map; not reproduced live |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2126 |

**Description**

The partner document list (`ExternalDataService.BuildDocumentListUrl` ~:631) and the download, versions and delete routes match only the route parent's typed lookup and the secure-root rule. The external plane never reads `sprk_relatedcommunication`, `sprk_isemailarchive` or `sprk_isinternalonly`.

A freshly archived `.eml` or attachment has no parent lookup, so it is not listed. It becomes visible when it is also filed to a matter, project or work assignment. None of these writers checks internal-only:

| Writer | When |
|---|---|
| `OfficeDocumentPersistence.cs` ~:304/:338 | Outlook save to a record of an email that was also captured as a communication |
| `IncomingCommunicationProcessor.cs` ~:349 | The reverse order: inbound capture links an archive saved earlier |
| `CommunicationService.cs` ~:3010-3029 | Outbound attachment copies of secure source documents; this includes internal-only sends |
| `PUT /api/v1/documents/{id}` | A re-file of an archive or attachment document |

Failure mode: a partner on the matter lists and downloads the `.eml` of an internal-only email that a user also filed to the matter.

**Owner question**

- **(a)** Hide documents whose related communication fails the 065 rule (a list filter and a check on each document route).
- **(b)** Keep showing them, because filing is an explicit act, and document that.

**Estimated effort**: (a) about a day, including tests.
**Blockers**: owner decision.
**Related**: ISS-010, ISS-014, ISS-016, tasks 065 and 078.


**Update 2026-10-20 (owner direction + UAC).** The owner wants this handled with UAC's Manage Access on Documents (#2119), not an R3 column. UAC proposes **#2119 D7**, pending the owner's acceptance (it amends I-14):
- A child can only narrow partner visibility. A document is hidden from partners when its own Access Permission is Restricted, or when it derives from an internal-only communication.
- The derivation is computed at read time through `_sprk_relatedcommunication_value` and the 065 rule, and fails closed.
- One shared predicate in UAC's external read path covers list, download, versions, preview, SPE and the AI tools.
- Wiring happens after #2078. UAC owns the rule and the predicate; R3 reviews its routes.

**Writer gaps found (R3 code map, master `c3ec5d6e5`).** Three paths save a document from an email without the link, so today's 078 guard and D7's derivation both miss it:
1. Outlook add-in email-attachment child docs (`UploadFinalizationWorker.cs:1254/1294`: parent-document lookup only).
2. The add-in single-attachment save (no capture; `OfficeService.cs:506`, `OfficeDocumentPersistence.cs:795`).
3. The add-in `.eml` when the best-effort canonical link misses (`OfficeDocumentPersistence.cs:790-842`).

Fix owners: Office and Communication. Options: set the link in those writers, and/or have the predicate follow `ParentDocumentLookup`.
---

### ISS-016 — RAG retrieval and chat archive ingest return internal-only email content to callers flagged external

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round (closes with ISS-010's real fix) |
| **Filed** | 2026-10-20 |
| **Source** | Task 078 review pass 2 (F4) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2127 |

**Description**

Two AI paths outside the generic Dataverse tools have no externality check:

- **RAG retrieval.** The archived `.eml` and its attachments are RAG-indexed (`IncomingCommunicationProcessor.cs` ~:1387, ~:1145; `CommunicationEnrichmentService.cs` ~:227). No retrieval path in `Services/Ai` checks internal-only or externality.
- **Chat archive ingest.** `POST /sessions/{id}/documents/from-document` (`Api/Ai/ChatDocumentEndpoints.cs` ~:219) accepts a `communicationId`. It checks only document read access, then downloads the full `.eml` app-only into the session.
- **AI analysis rows.** `sprk_analysis` and `sprk_analysisoutput` hold the AI analysis of an archived `.eml`. They have `sprk_documentid` and `sprk_regardingcommunication` lookups, and the generic chat tools still return them. They could be filtered the way task 078 filters `sprk_document`. Found in the 078 fix round, 2026-10-20.

Failure mode: a licensed user flagged external gets internal-only email content through retrieval or by attaching the archive to the chat.

**Suggested fix**

- Ingest: when the caller is external, apply the 065 visibility read, as `EmailDraftToolHandler` does.
- Retrieval: for an external caller, drop chunks whose source document's related communication fails the rule.

**Estimated effort**: 1 to 2 days.
**Blockers**: none.
**Related**: ISS-010, ISS-014, ISS-015, task 078.

---


### ISS-017 — "Could not read your email address" does not tell the customer admin what to fix

| Field | Value |
|---|---|
| **Status** | Open (R4 backlog) |
| **Urgency** | next-round |
| **Filed** | 2026-10-20 |
| **Source** | Owner live check on dev (Dewey Cheatham test user Tracy Cheatham, no Email in Entra) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2155 |

**Description**

A customer employee whose Entra user has no Email gets a token with no `email` claim, so registration answers `registration_email_missing`. The SPA says only "Contact your administrator". Owner fix on dev: set the user's Email in Entra; registration then completed and the contact was created.

**Suggested fix**: R4 copy change in `RegistrationPage.tsx` (`emailMissing`): tell the IT admin to set the Email field on the user's Entra account, then sign in again; update the `registration_email_missing` test. Keep the token-only email rule.

**Estimated effort**: under 0.5 day.
**Blockers**: none.
**Related**: task 047, task 074.

### ISS-018 — Partner plane uses a cached company account → `authority_mismatch`

| Field | Value |
|---|---|
| **Status** | Fixed: PR #2175 (`3531ed65e`), dev SPA run 39536100531 (2026-10-21); owner smoke pending |
| **Urgency** | before 072 |
| **Filed** | 2026-10-20 |
| **Source** | Owner live check on dev SPA `28274e04e` (ralph.schroeder@spaarke.com) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2166 |

**Description**

MSAL's account list is shared across clients in the tab. The CIAM plane has no account filter, so `AuthGuard` treats a cached company account as signed in, and `acquireStandaloneBffToken` can pick it. msal-browser then refuses with `authority_mismatch`. One path: company-account sign-in → "not set up" → "choose a different sign-in" → Partner in the same tab (`handleResetRealm` does not clear cached accounts).

**Suggested fix**: filter the CIAM plane to CIAM-tenant accounts (mirror of `isWorkforceHomeAccount`); tests for both planes. Two top-tier reviews (auth).

**Estimated effort**: under 1 day.
**Blockers**: none. Workaround: new InPrivate window.
**Related**: task 058, task 074, task 070.

### ISS-019 — A licensed user's first sign-in shows a profile form before the account link

| Field | Value |
|---|---|
| **Status** | Open (R4 backlog) |
| **Urgency** | next-round |
| **Filed** | 2026-10-21 |
| **Source** | Owner live check on dev (Ralph Dewey, licensed user, Dewey Cheatham home account) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2192 |

**Description**

1. In a fresh browser the silent link fails (AADSTS50058, no Spaarke session), so the profile form shows.
2. Continue answers 403 `licensed_user_link_required`, and only then does "Link your Spaarke account" appear.
3. The first link popup failed on an Entra page ("not in the Spaarke tenant"); the second worked. The cause is not captured.

**Suggested fix**: tell the gate before the form that the token email belongs to a licensed user (registration already discloses it to the same caller), so the form is skipped. Review the 60 s popup timeout for first-time guest sign-ins. uac-r2 / auth review.

**Estimated effort**: 1 day.
**Blockers**: none.
**Related**: task 074, ISS-017 (#2155).

---

## Closed

(none)

### ISS-020 — Removal sweep leaves no telemetry on a pass with no candidates

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round (before anyone turns `IdentityLink:Reconciliation:RemovalSweepWritesEnabled` on) |
| **Filed** | 2026-10-28 |
| **Source** | Dev rollout of the contact identity hardening, step 3 (`notes/contact-identity-dev-rollout-record.md`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2415 |

**Description**

`ExternalAccessRemovalSweep` logs only per-contact lines and failures, and the `[ID-LINK-RECON] heartbeat` carries no sweep counts. A pass with no candidates therefore leaves no trace. After the `e56a3a51d4` deploy, about 6 hours of runs logged nothing, and only a direct Dataverse count of the two candidate queries (both 0) proved that the sweep had nothing to do. Add a per-pass summary (mode, skipped, candidates, would-mark/marked, deferred, failed), either as its own line or as heartbeat fields. Telemetry only.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/ExternalAccess/ExternalAccessRemovalSweep.cs` (`RunAsync`)
- `src/server/api/Sprk.Bff.Api/Services/ExternalAccess/IdentityLinkReconciliationJob.cs` (`RunRemovalSweepAsync`, heartbeat)

### ISS-021 — The shared DataGrid sorts Money columns as text

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-29 |
| **Source** | R3 task 002 adversarial review B (owner grid columns) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2446 |

**Description**

A Money value reaches the browser as `{ value: n }`, and `DataGrid` `compare` sorts on the formatted label as text ("$1,000.00" before "$200.00"). It affects the Invoices grid's `sprk_totalamount` today, and `sprk_totalamount_base` after task 002. Fix: compare `.value` numerically when both sides carry it. The code is shared UI, so it is filed rather than changed in R3.

**Entry-points**

- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` (`compare`, ~1160-1174)

### ISS-022 — DataGrid Export to Excel writes Money and option-set cells as raw values

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-29 |
| **Source** | R3 task 002 adversarial review B (owner grid columns) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2447 |

**Description**

The CSV export uses the raw value, not `@formattedValues`. A Money cell is exported as `{"value":1234.5}`, and option-set and status cells are exported as raw values. It affects all six external SPA grids. Fix: use `formattedLabelOf` first, and unwrap `{ value }`. The code is shared UI, so it is filed rather than changed in R3.

**Entry-points**

- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/commandBar/csvExport.ts` (~64-78), `commandBar/defaults.ts:228`

### ISS-023 — The profile form shows an email that registration then refuses

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-29 |
| **Source** | R3 task 074 probe 5 (Tony Howe, a Dewey Cheatham member with no email attribute) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2449 |

**Description**

When the home token has no `email` claim, the form's prefill falls back to the username, but registration accepts the `email` claim only. The person fills in a form that can never succeed and then sees "We could not read your email address". Registration refusing the username is correct. Fix: have the `registration_required` refusal on `/me` say whether the email is usable, and show the "contact your administrator" state instead of the form when it is not. Workaround: an administrator sets the account's email attribute.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` (~717-734)
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/WorkforceIdentityOptions.cs` (`TokenEmail`)
- `src/client/external-spa/src/pages/RegistrationPage.tsx`

### ISS-024 — Grant notifier emails a home-linked workforce contact as an external person (DI-137)

| Field | Value |
|---|---|
| **Status** | Open (owner decision: fix in R3 with uac-r2, or R4) |
| **Urgency** | next-round |
| **Filed** | 2026-10-29 (by ontology-r1 as DI-137) |
| **Source** | Ontology task 168 fix round 3, after the R3 review |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2458 |

**Description**

`GrantAccessNotifier.NotifyContactGrantAsync` decides "an internal user represents this contact" from `sprk_primarycontact` and the Spaarke-tenant object id only. A WorkforceHome contact linked to a licensed user by the home pair alone (the link the portal reads first) is therefore emailed a portal notice as an external person, while the portal shows that person the user's view. The notice is the only content involved. Reference fix: read the home link before the external branch, as `AssignTaskRecipientGate.HomeLinkAsync` (commit `bd32be448f`) does. The natural home is uac-r2 193 `ContactUserRepresentation` (DI-136 #2457).

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/GrantAccessNotifier.cs` (`NotifyContactGrantAsync`, `EmailExternalContactGrantAsync` ~270-330)

### ISS-025: Teams mobile layout not optimized (deferred to R4)

| Field | Value |
|---|---|
| **Found** | 2026-10-29, task 071 (owner) |
| **What** | The Teams tab loads in Teams mobile, but its layout is not adapted to the phone surface. |
| **Decision** | Owner: not for this release; it loads. |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2466 |
