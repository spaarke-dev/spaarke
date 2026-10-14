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

## Closed

(none)
