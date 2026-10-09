# B2B guest sign-in on Dataverse surfaces — review (2026-10-08, issue #1453)

UAT round 12: a B2B guest (home tenant `bc3aa7f4…`) opened a Spaarke record in spaarkedev1 (Spaarke tenant `a221a95e…`). Every control calling the BFF failed. The sign-in popup showed AADSTS700016 for app `b36e9b91…`, then 401s on `/api/documents/{id}/view-url` and `/api/ai/visualization/related/{id}`. The Office add-in itself works, because it uses a tenant-specific authority.

The review was read-only, in three parts: the `@spaarke/auth` library, every client surface that signs in, and the BFF's handling of guest tokens.

## Cause

1. **`@spaarke/auth` falls back silently to `/organizations`** (`src/client/shared/Spaarke.Auth/src/config.ts:13,51-54`). That happens when the caller passes no `authority` or `tenantId` and `Xrm…organizationSettings.tenantId` is empty. In practice it is often empty: the repo itself records it as "always empty on first load" (`AUTH-AND-BFF-URL-PATTERN.md:289`, `LegalWorkspace/src/main.tsx:50-53`). With `/organizations`, Entra signs the user in to their **home** tenant. For a member, that is Spaarke's tenant, so the defect stayed hidden. For a guest it is their own tenant, where Spaarke's single-tenant apps don't exist.
2. **Consumers discard the tenant they already have.** Nine PCF `authInit.ts` files load `sprk_TenantId` (or receive it) and then don't pass it on, citing a "2026-05-13 popup regression". Three code pages built with `createCodePageAuthInitializer` also omit it.
3. **The 2026-05-13 regression (ef57fc3f35) was a malformed authority** (`https://login.microsoftonline.com/undefined`), not tenant-specific authorities in general. Validating the tenant value prevents it from returning.
4. **The popup's `b36e9b91` is the SpeDocumentViewer PCF's app.** That control is still deployed (1.0.27) and bound to the Document main form, but its source was deleted as an "orphan" in 5b4cca898 (2026-06-22). It cannot be rebuilt from master. In dev1, `sprk_MsalClientId` = `170c98e1…` and `sprk_TenantId` = `a221a95e…`, both set.

## Surfaces that fail for a guest today (authority falls back to `/organizations`)

- **PCFs:** SpeDocumentViewer (no source), RelatedDocumentCount, SemanticSearchControl, CommunicationActions, CommunicationConversationPanel, CommunicationMessageActions, CommunicationTimeline, CommunicationTimelineRegarding, CommunicationAttachments, TrackingFieldTrio.
- **Code pages:** DailyBriefing, EmailPage, CommunicationReconciliation, Reporting, SpeAdminApp, DocumentUploadWizard, FindSimilar, the DocumentRelationshipViewer code page, PlaybookBuilder.

Surfaces that pass a tenant explicitly and should work:
- the DocumentRelationshipViewer PCF;
- CommunicationConnections and RegardingResolver;
- SpaarkeAi, LegalWorkspace and the communication conversation page;
- the Create* / Summarize / Workspace / Playbook / AllDocuments / Notepad / SmartTodo / EventDetailSidePane pages;
- the wizard bootstrap hook;
- the code-pages SemanticSearch and CommunicationPage.

## Related client defects

1. **Duplicate `initAuth` with the same clientId keeps the first provider** (`initAuth.ts:29-55`). A `/organizations` provider can lock out a later, correctly-tenanted one.
2. **Any non-blank `tenantId` is accepted, including `"undefined"` and `"null"`** (`config.ts:100`). The authority is never validated. This is the 2026-05-13 bug class.
3. **`setRuntimeConfig` does not publish `tenantId` as a global** (`createRuntimeConfigStore.ts:230-235`).
4. **Accounts are picked by position (`getAllAccounts()[0]`)** in `BrowserMsalStrategy.ts:37,155,226` and `OfficeNaaStrategy._pickAccount` (`:357`). There is no active-account or tenant match, so a wrong cached account can loop through popups.
5. **`authenticatedFetch` sends requests with no token when acquisition fails.** Each 401 re-runs the silent → popup chain up to three times, and concurrent cold-cache fetches open parallel popups (`authenticatedFetch.ts:35-55`, `InMemoryCache.acquire` `:51-59`).
6. **The `/api/config/client` tenant fallback is rate-limited to 10 per minute per IP** (`RateLimitingModule.cs:96-104`). Staff behind a shared NAT would hit 429s.
7. **Ribbon scripts are hardcoded to dev.** `sprk_registrationribbon.js:35` and `sprk_aichatcontextmap_ribbon.js:23` hardcode the dev client `b36e9b91`, the dev tenant and the spaarkedev1 redirect, so they break in every other environment.
8. **Scopes are inconsistent.** `SDAP.Access` is used in `sprk_emailactions.js:46` and the ribbons; `user_impersonation` everywhere else. The BFF exposes both.
9. **Other surface bugs:**
   - **PlaybookBuilder** sets the wrong global (`__SPAARKE_BFF_BASE_URL__`; the library reads `__SPAARKE_BFF_URL__`) and passes no scope.
   - **The DocumentRelationshipViewer PCF** builds `…microsoftonline.com/` when the tenant is empty.
10. **Docs and comments contradict the code:**
    - `resolveTenantIdSync.ts:10-17`, `sdap-auth-patterns.md:207`;
    - `types.ts:27` (default scope);
    - `BrowserMsalStrategy.ts:131`;
    - `config.test.ts:20-21,66-78` asserts `/organizations` as correct.
11. **Other controls deployed with no source** (not checked for BFF calls): AnalysisWorkspace, AnalysisBuilder, PlaybookBuilderHost, AssociationResolver, FieldMappingAdmin, the LegalWorkspace PCF, RegardingLink, EventFormController, DueDatesWidget, EventCalendarFilter, EventAutoAssociate. The orphan cleanup's form gate missed at least the Document main form.

## Server (BFF) once a guest holds a token

- **Both failing endpoints work for a guest** provided:
  - the guest has an enabled systemuser whose `azureactivedirectoryobjectid` = their guest `oid`;
  - Dataverse OBO succeeds for them;
  - they hold Read on the document.
- Details: validation is single-tenant on `tid`. The caller is resolved by `oid`. `view-url` is app-only to SPE (task 171). Visualization ignores `?tenantId=` and uses `tid`.
- **No BFF change is needed for #1453 itself.**

### Model 1 guest issues beyond sign-in (separate work, owner decisions)

- **G1 (High).** `scripts/Set-ExternalFlagForB2BGuests.ps1`, which the deployment guide requires (`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md:1051-1056,1284-1296`), sets `sprk_isexternal = true` on every `#EXT#` systemuser. Under Model 1 that is every customer employee. Consequences:
  - refused or stripped from Restricted records (UAC task 114, K1);
  - excluded from internal-only messages;
  - never standing writers of the BU SPE container (`SpeContainerMembershipSync.cs:217-225`), so Office edit is denied.
- **G2 (High).** `WorkforceIdentityOptions.cs:6-9` and the deployment guide (`:669-674`) assume Model 1 staff sign in with the customer's own `tid`. As guests they carry Spaarke's `tid` with `acct = 1`, so `WorkforceMembershipTest.Evaluate` (`:199-230`) denies a guest who is not yet a systemuser on `/api/v1/external`.
- **G3 (Medium).** Guest systemusers are never bound by email to an existing contact (`ContactIdentityBinder.cs:252-257`, `ContactBindingDecision.cs:631-634,762-764`).
- **G4 (High for those features).** Anything that sends or reads mail "as the user" through Graph OBO fails for guests, because the guest has no mailbox in Spaarke's tenant. Affected:
  - `EmailChannelSender.cs:58,210`;
  - `SendEmailNodeExecutor.cs:237`;
  - `/me/todo` sync;
  - `OfficeEmailEnricher` (already worked around client-side, task 116).
- **G5 (Medium, unverified).** `/me/memberOf` through OBO (`PrivilegeGroupResolver.cs:174`) may be blocked by guest directory settings. It fails closed to "no groups".
- **G6 (Medium, unverified).** Office edit for guests depends on SPE guest sharing.
- **G7 (Low).** `TenantAuthorizationFilter.cs:87-99` returns 403 when a `?tenantId=` differs from `tid`.

### Not verified

- the test guest's systemuser, licence and Read rights in spaarkedev1;
- whether Dataverse OBO for the guest has succeeded in logs;
- whether `Set-ExternalFlagForB2BGuests.ps1 -Apply` has already run on dev;
- SPA redirect URIs for `170c98e1` / `1e40baad`;
- whether guests with Basic User can read `environmentvariabledefinition`.
