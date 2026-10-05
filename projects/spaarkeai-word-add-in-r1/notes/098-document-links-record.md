# Task 098 - document links open the Spaarke record (UAT round 5)

Owner decision 2026-10-05: a document link in email opens the Spaarke record; external recipients use the external access platform.

## Changes
- `Api/FileAccessEndpoints.cs`: `POST /api/documents/{id}/share-link` refuses SPE-backed documents with **422** `SHARE_LINK_UNSUPPORTED_SPE` (ProblemDetails, `extensions.code`). Share filter still runs first (401/403 unchanged). Graph path + `ResolveShareLinkPolicy` removed from the handler. 422 not 409: permanent property of where the file lives; 409 stays the fixable pointer states.
- Shared composer: `createXrmEmailComposeHandlers` `onResolveShareLink` now builds the record link offline (`buildSpaarkeRecordLink`, app `sprk_MatterManagement`, https org URL only); picked-record URLs use the same builder. Engine: a linked doc with no resolvable link is omitted and the send is refused with `ATTACHMENT_LINK_UNAVAILABLE` on the attachments field. Toggle label: "Link to Spaarke record".
- Placement (CLAUDE.md s10): stays in BFF; removes code, adds none.

## Consumers of onResolveShareLink
EmailPage main.tsx, EmailWorkspaceWidget (SpaarkeAi), TrackingFieldTrio PCF, LegalWorkspace email.registration, SpaarkeAi EmailPerItemCards, DocumentUploadWizard DocumentEmailStep, Communication.Components EmailWorkspace/useEmailComposeActions: all take `composeHandlers.onResolveShareLink` -> now the record link. Office add-ins do not pass it (unaffected; 099's scope).

## Open
- UAC-r2 owns tests/integration/auth/UnifiedAccessControl/ShareLinkAuthorizationTests.cs: 6 tests now fail by design (assert 200 mint): WhenCallerHoldsShare_MintsAnOrganizationScopedLink, WhenAuthorized_AlwaysSetsAnExpiry, WhenExternalRecipientsRequested_MintsAnonymousWithShorterLifetime, WhenAuthorized_ReportsScopeAndExpiryToTheCaller, ShareLinkAnonymousDisabledTests x2. They need rewriting/retiring by UAC-r2. Contract tests: tests/integration/contract/Api/Documents/ShareLinkSpeRefusalContractTests.cs (reuses UAC's ShareLinkTestFixture).
- Dead code now: ShareLinkOptions + config binding, ShareLinkRequest/Response, SpeFileStore.CreateSharingLinkAsUserAsync.

## Main-session scope change (2026-10-05): the route change is NOT shipped

The route half (`POST /api/documents/{id}/share-link` → 422 for SPE files) made **6 tests in
`tests/integration/auth/UnifiedAccessControl/ShareLinkAuthorizationTests.cs` fail** (they assert a 200 mint against a
mocked Graph). That folder is unified-access-control-r2's and this project must not edit it, and the new contract test
reused UAC-r2's `ShareLinkTestFixture`. So the main session **reverted the route change** and kept the composer change:

- **Shipped**: the shared composer's Link inserts the Spaarke record link; no product code calls the share-link route any
  more (consumer list above). This is the owner's decision in full ("the link should just be to open Spaarke record").
- **Not shipped**: the typed refusal on the route. With no caller left, the right long-term move is to **retire** the
  route (and `ShareLinkOptions`, `ShareLinkRequest/Response`, `SpeFileStore.CreateSharingLinkAsUserAsync`) together with
  UAC-r2's tests for it — UAC-r2's call, since it owns the route's authorization and those tests. The reverted diff is
  kept at `notes/098-share-link-route-refusal.patch` for that hand-off.
