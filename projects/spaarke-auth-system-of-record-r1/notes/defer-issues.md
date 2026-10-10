# Defer / Issue Tracking — spaarke-auth-system-of-record-r1

> **Source of truth** for deferred work + issues uncovered by the auth investigation. Each entry has a paired GitHub Issue
> (label `spaarke-auth-system-of-record-r1`, board *Spaarke Core*). Most are routed to the owning project named in **Owner**.
> Existing issues updated instead of duplicated: #1464 (guest Graph mail), #1489 (`/api/api` KPI scripts), #1477 (stale ribbon copies), #1453 (guest sign-in).

---

## Open (in priority order: now > next-round > someday)

### ISS-001 — Email ribbon "Archive Email" posts to a removed BFF route (404 shown as a permission error)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | email-communication / Dataverse web resources |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1556 |

**Description**

On an Email record, the command-bar button "Archive Email" runs `sprk_emailactions.js`, which POSTs `/api/emails/convert-to-document`. That route family was deleted from the BFF, so every click returns 404, and the script shows it as "Email not found or you don't have permission". Confirmed live on dev (x06/x07). The packaged copy on master received only a scope fix; the dead path remains.

**Failure mode**: A user clicks Archive Email and nothing is archived; the message misleads them into thinking it is a permission problem.

**Entry-points**

- `src/client/webresources/js/sprk_emailactions.js:505-520,566-570`
- `src/dataverse/solutions/SpaarkeMaster/Entities/Email/RibbonDiff.xml:4-6,23`
- `projects/spaarke-auth-system-of-record-r1/working/x09d-broken-buttons-and-triage.md §1.1`

**Suggested fix**

Point the button at the current save path (Office/communication archive endpoint), or remove the button. There is no route to remap to as-is.

**Estimated effort**: hours
**Related**: record H-4

---

### ISS-002 — Matter form OnLoad scripts fail: insight card posts a relative URL with no bearer; packaged KPI refresh and finance rollups send no bearer

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | Dataverse web resources (matter KPI / insights) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1557 |

**Description**

The Matter main form loads four scripts on open. (1) `matter_insight_onload` / `insightCardMount.ts` POSTs `/api/insights/ask` as a RELATIVE URL (it lands on the Dataverse origin) with no Authorization header. The route exists on the BFF; the call never reaches it. (2) `sprk_matter_kpi_refresh.js` and two `sprk_subgrid_parent_rollup.js` instances (invoice and budget subgrids) are the packaged v1.0.0 copies, which send no credential, so every call is 401. On dev they also build `/api/api/...` (#1489). Confirmed live on dev.

**Failure mode**: Opening any Matter shows no AI insight card, does not refresh KPI grades, and leaves the invoice/budget rollups stale. Console errors on every form load.

**Entry-points**

- `src/dataverse/forms/sprk_matter/insightCardMount.ts:318-334`
- `SpaarkeMaster/WebResources sprk_matter_kpi_refresh.js:269-278`
- `SpaarkeMaster/WebResources sprk_subgrid_parent_rollup.js:271-283`
- `sprk_Matter main FormXml:1453-1467`
- `projects/spaarke-auth-system-of-record-r1/working/x09d-broken-buttons-and-triage.md §1.2-1.5`

**Suggested fix**

Build the insight call with the BFF base URL + `@spaarke/auth` bearer; repackage the fixed source copies of the KPI and rollup scripts; strip a trailing `/api` from `sprk_BffApiBaseUrl` in all six copies (#1489).

**Estimated effort**: 1-2 days
**Related**: record H-4; #1489

---

### ISS-003 — Registration Approve/Reject buttons run the legacy packaged script (legacy client b36e9b91, dev tenant, retired spe-api-dev host)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | Dataverse web resources (registration) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1558 |

**Description**

On Registration Request (form and home grid), "Approve Demo Access" and "Reject Request" run `sprk_/js/registrationribbon.js`, which is byte-identical to the pre-task-123 copy. It signs in with the legacy app `b36e9b91`, scope `SDAP.Access`, a hard-coded dev tenant/redirect, and calls the retired host `spe-api-dev-67e2xz`. The fixed source exists under a different schema name (`sprk_registrationribbon.js`) that no Solution.xml, data.xml or deploy script carries. Confirmed live on dev.

**Failure mode**: Approving or rejecting a demo registration fails on dev and cannot work in any other environment.

**Entry-points**

- `src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_/js/registrationribbon.js:27,35-44,79-93`
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_registrationrequest/RibbonDiff.xml:4-21`
- `projects/spaarke-auth-system-of-record-r1/working/x09d-broken-buttons-and-triage.md §1.6`

**Suggested fix**

Packaging only: put the fixed source content under the deployed schema name `sprk_/js/registrationribbon.js` and redeploy.

**Estimated effort**: hours
**Related**: record H-5 / a02 D-27; #1477

---

### ISS-005 — Provisioning H14b/H14c register webhooks at routes the BFF does not serve; H14b uses the signing key as clientState; H14c registers no step

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | customer-provisioning-orchestration-r1 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1560 |

**Description**

H14b creates Graph subscriptions with notificationUrl `{stamp}/api/webhooks/graph/{module}` and clientState = the `Communication-Webhook-SigningKey` secret. H14c writes a Dataverse serviceendpoint for `{stamp}/api/webhooks/dataverse/communication`. The BFF maps neither route (only `/api/communications/incoming-webhook` and `/api/compose/webhooks/spe-doc-changed`). Graph's create handshake to a 404 should fail H14b and therefore the provisioning run. H14c can never fire: no `sdkmessageprocessingstep` is registered. Even with a correct URL, the BFF compares clientState with the separate `Communication-WebhookClientState` secret. The BFF already creates and renews its own subscriptions per mailbox (`GraphSubscriptionManager`).

**Failure mode**: A Model 1 provisioning run is expected to fail at H14b (not verified live); H14c is dead wiring.

**Entry-points**

- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/IntegrationWiring/H14bGraphWebhookSubHandler.cs:55,159-163,196-197`
- `…/H14cDataverseWebhookSubHandler.cs:45,135-139`
- `…/H14IntegrationWiringHandler.cs:254-302`
- `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs:470`
- `projects/spaarke-auth-system-of-record-r1/working/x09a-mail-and-webhooks.md Q3`

**Suggested fix**

Delete H14b in favour of the BFF's GraphSubscriptionManager (or point it at `/api/communications/incoming-webhook` with the client-state secret); delete H14c or add the step + a BFF route.

**Estimated effort**: 1-2 days
**Related**: record H-3; coordination note 2026-10-09-from-auth-system-of-record-r1-webhooks.md

---

### ISS-007 — Model 1 mailbox setup is manual and undocumented: no provisioning step creates sprk_communicationaccount rows; which mailbox each customer uses is undecided

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | customer-provisioning-orchestration-r1 (+ owner decision) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1562 |

**Description**

A stamp's mail needs three things. Exchange permissions are built: H14a grants the stamp identity Mail.Read/ReadWrite/Send/MailboxSettings.Read via Exchange RBAC for Applications, scoped to one mailbox group. Settings are built. The mailbox records (`sprk_communicationaccount` rows, verified via `POST /api/communications/accounts/{id}/verify`) are created by hand, and no guide says so. Under Model 1 only Spaarke-tenant shared mailboxes are reachable; customer staff mailboxes in their home tenant are not reachable by any Spaarke-tenant identity. The deployment guide is right about H14a and wrong about H14b/H14c.

**Failure mode**: A newly provisioned customer environment has no working inbound or shared-mailbox outbound mail until an operator creates account rows nobody documented.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Communication/ApprovedSenderValidator.cs:164-209`
- `…/GraphSubscriptionManager.cs:424-435`
- `…/IGraphAppRolesRegistry.cs:72-84`
- `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md §7.9`
- `projects/spaarke-auth-system-of-record-r1/working/x09a-mail-and-webhooks.md Q2`

**Suggested fix**

Owner decides the Model 1 mail model (a Spaarke-tenant shared mailbox per customer; how customer mail reaches it). Then add a provisioning step that creates + verifies the account rows, and correct the guide.

**Estimated effort**: 2-3 days + decision
**Related**: record §6; x09a Q2.4

---

### ISS-008 — Member test refuses Model 1 guests without a systemuser; licence-free customer staff cannot use the SPA/Teams under Spaarke-tenant sign-in

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | unified-access-control-r2 / spaarkeai-word-add-in-r1 task 126 (blocks R3) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1563 |

**Description**

Model 1 staff are B2B guests in Spaarke's tenant. Their token (verified live 2026-10-08) is tid = Spaarke, acct = 1, idp = home tenant. A guest WITH a systemuser resolves correctly (systemuser-first). A guest WITHOUT one (a licence-free customer employee, U3) reaches `WorkforceMembershipTest.Evaluate`, which returns ForeignTenant on stamps (Spaarke's tenant can never be in `CustomerTenantIds`) or Guest on dev. The code comment and the deployment guide still assume Model 1 staff sign in with the customer's own tid. The word-add-in note G2 proposed a fix (task 126), blocked on UAC-r2's answer, which has not arrived.

**Failure mode**: Licence-free customer staff cannot sign in to the SPA or Teams tab on any Model 1 stamp.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/WorkforceIdentityOptions.cs:6-9,133-146,199-230`
- `…/WorkforcePrincipalResolver.cs:123-170`
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Models/CustomerWorkforceTenantsRule.cs:13-25`
- `projects/spaarke-auth-system-of-record-r1/working/x09c-membership-and-guests.md Q3-Q4`

**Suggested fix**

Capture the home tenant (`idp`) in WorkforceCallerClaims and add one branch: acct=1 AND tid = stamp tenant AND home tenant in CustomerTenantIds -> Member. Binder and provisioning data unchanged. Owner decides the trusted home-tenant source (idp claim recommended).

**Estimated effort**: 1-2 days
**Related**: record M-2; #1453; word-add-in coordination 2026-10-08-to-uac-r2-model1-guests.md (G2)

---

### ISS-009 — sprk_isexternal has no write path, and Set-ExternalFlagForB2BGuests.ps1 would mark every Model 1 employee external

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | unified-access-control-r2 (+ future user-admin app) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1564 |

**Description**

`systemuser.sprk_isexternal` (default No) governs Restricted-record membership, shares, Office edit, BU container writers and internal-only messages. No BFF or provisioning code writes it; H11 creates guests without it. The only writer is `scripts/Set-ExternalFlagForB2BGuests.ps1`, which flags every `#EXT#` user. Under Model 1 every customer employee is `#EXT#`, so the guide-mandated script would make the customer's whole staff external (word-add-in G1). Live dev: the Model 1 test guest reads No (internal), which is correct for Model 1.

**Failure mode**: Running the documented deployment step on a Model 1 stamp strips customer staff from Restricted records, internal messages and Office edit.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Identity/SystemUserIdentityResolver.cs:302-316`
- `scripts/Set-ExternalFlagForB2BGuests.ps1`
- `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md:1051-1056,1284-1296`
- `projects/spaarke-auth-system-of-record-r1/working/x09c-membership-and-guests.md Q3(a)`

**Suggested fix**

Do not run the script on Model 1 stamps (fix the guide now); set the flag explicitly at user setup (future user-admin app), with Model 1 customer guests = internal.

**Estimated effort**: hours (guide) + design
**Related**: record H-2

---

### ISS-010 — H7b omits Standing Grant Administrators FLS Read and the Access Administrator / Core User privilege edits (standing grants read as not held on new stamps)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | customer-provisioning-orchestration-r1 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1565 |

**Description**

H7b provisions the Secure Record BU/team/role and the BFF-managed FLS memberships, but not FLS Read on `contact.sprk_standinggrant` for the Standing Grant Administrators profile, nor the privilege edits the scripts apply. Live dev: that profile has no members; the BFF reads the field only because its identity is System Administrator.

**Failure mode**: On a freshly provisioned stamp, standing grants for workforce contacts silently read as not held, so those users lose access the grant should give them.

**Entry-points**

- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SecureRecordSetup/SecureRecordSetupProcedure.cs:221,313,527-540,642-682`
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/SubjectStandingGrantReader.cs:164-220`

**Suggested fix**

Add the FLS profile membership and privilege edits to H7b, idempotently.

**Estimated effort**: 1 day
**Related**: record M-17

---

### ISS-011 — External SPA bypasses its own plane seam: upload page uses an OBO route, playbook page an unmapped route; 401/403 on /me/entitlements replaced by mock identities

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | spaarke-SPA-external-access-platform-r3 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1566 |

**Description**

`DocumentUploadPage` calls `PUT /api/obo/records/...` and `PlaybookLibraryPage` calls REST-by-entity `/api/dataverse/{entity}` (never mapped), both through a CIAM singleton that is never initialised. `SemanticSearch` posts to the default-scheme `/api/ai/search` (CIAM -> 401). `/api/v1/external/ai/playbook` is unmapped. `fetchMeEntitlements` turns a thrown 401/403 into the "Sam Rivera" mock identity in production builds.

**Failure mode**: Upload and playbook pages fail for every SPA user; an auth failure is disguised as a fake signed-in user.

**Entry-points**

- `src/client/external-spa/src/pages/DocumentUploadPage.tsx:107-118,186-189`
- `…/PlaybookLibraryPage.tsx:26,63-84`
- `…/SemanticSearch.tsx:483`
- `src/client/external-spa/src/api/me-client.ts:54-73,105-115`

**Suggested fix**

R3 routes these pages through `/api/v1/external/**` via the active plane's acquirer and removes the production mock fallback.

**Estimated effort**: 2-3 days
**Related**: record H-9; ADR-028 R20/R26/R36

---

### ISS-013 — Tier-1 SPA modules: no app roles are defined on BFF registrations, so workforce users get no gated modules; CIAM gets a blanket module

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | spaarke-SPA-external-access-platform-r3 + customer-provisioning-orchestration-r1 (H3 app roles) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1568 |

**Description**

`ModuleEntitlementResolver` gives a workforce caller the modules mapped (`sprk_approlemodulemap`: app role name -> module code, hand-maintained) to the app roles in its token. The dev and prod BFF apps define only `Admin`, and H3 defines only `Provisioning.KeylessProof`, so no token can carry a mapped role (e.g. `FrontDoorUser`). Every workforce user gets `[]` and sees only Quick Start and the ungated Messages widget. CIAM contacts get the blanket `assigned-work` module (owner option B, 2026-08-10), which contradicts ADR-028 A3 :124.

**Failure mode**: R3's workforce features (Front Door wizards, gated widgets) cannot appear for any workforce user on any environment.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ModuleEntitlementResolver.cs:89-135`
- `src/client/external-spa/src/api/me-client.ts:105-115`
- `projects/spaarke-auth-system-of-record-r1/working/x09c-membership-and-guests.md Q2`

**Suggested fix**

Define the module app roles on the BFF registration (H3 for stamps), assign them to the customer's user group, and seed `sprk_approlemodulemap`; amend A3 to record the CIAM blanket as the decision.

**Estimated effort**: 1-2 days + owner decision
**Related**: record M-1

---

### ISS-004 — Remove orphaned web resources: sprk_DocumentDelete.js (clashes with DocumentOperations), sprk_communication_send, UpdateRelated commands

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | Dataverse web resources |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1559 |

**Description**

Three packaged scripts are loaded by nothing. `sprk_DocumentDelete.js` hard-codes a non-existent tenant (AADSTS90002 live) and defines the same `Spaarke.Document.Config` object that the working `sprk_DocumentOperations.js` reads, so any future reference would break Delete. `sprk_communication_send` (own MSAL) is superseded by the CommunicationActions PCF. `sprk_updaterelated_commands.js` hard-codes the retired host and is superseded by the working field-mapping push ribbon.

**Failure mode**: Dead config ships in every SpaarkeMaster import; the DocumentDelete namespace clash would silently break document delete if a form ever loaded it.

**Entry-points**

- `SpaarkeMaster/WebResources/sprk_DocumentDelete.js:34-46`
- `SpaarkeMaster/Other/Solution.xml:392-393`
- `SpaarkeMaster/WebResources/sprk_communication_send:104-106`
- `sprk_updaterelated_commands.js:17`
- `projects/spaarke-auth-system-of-record-r1/working/x09d-broken-buttons-and-triage.md §1.8,§1.10,§1.11`

**Suggested fix**

Remove the three root components from SpaarkeMaster and delete them from environments.

**Estimated effort**: hours
**Related**: record H-5, L-1

---

### ISS-006 — Graph webhook receiver rejects every real Graph notification (requires an HMAC header Graph never sends; no relay exists)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | email-communication-intelligence (+ compose for the SPE receiver) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1561 |

**Description**

`WebhookSignatureFilter` returns 401 when `X-Hub-Signature-256` is absent. Only the validationToken handshake bypasses it. The BFF template itself says the header is for "a signing relay … since Graph itself does not sign bodies", and no relay exists in the repo. So real-time delivery is rejected on every environment, dev included. Inbound email still works because `InboundPollingBackupService` (5 min) and `MailboxDeltaReconciliationService` (15 min) run unconditionally.

**Failure mode**: New inbound email appears up to ~5 minutes late instead of seconds; subscriptions are created and renewed for nothing.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/Filters/WebhookSignatureFilter.cs:82-117`
- `src/server/api/Sprk.Bff.Api/appsettings.template.json:400`
- `src/server/api/Sprk.Bff.Api/Api/ComposeSyncEndpoints.cs:43-47`
- `projects/spaarke-auth-system-of-record-r1/working/x09a-mail-and-webhooks.md Q3.2`

**Suggested fix**

Decide the receiver contract: make the HMAC layer optional for Graph-direct delivery and rely on clientState + the validation handshake (Graph's documented model), or deploy a relay.

**Estimated effort**: 1 day
**Related**: record H-3

---

### ISS-012 — ADR-028 A5 not in force: SPA/Teams system-user record set uses the ADR-034 column approximation (direct shares invisible, BU over-grant)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | unified-access-control-r2 (task 036) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1567 |

**Description**

For a Dataverse user on `/api/v1/external/**`, `AccessibleRecordSetService` composes visible records from `MembershipResolverService` (owner, owning team, own BU, registry "Assigned" columns), not from Dataverse security. It reads no role depth, BU hierarchy or POA shares. `ImpersonatedRootSetSource` (the A5 impersonated read) is built, registered and inert; UAC-r2 task 036 is pending. The owner's own SPA access works because their records match by owner/team/BU.

**Failure mode**: A record shared to a user with Manage Access "+ User" (a POA share) is invisible in the SPA and Teams; a Basic-depth user sees every record in their BU.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipResolverService.cs:689-763,1109-1177`
- `…/Infrastructure/ExternalAccess/AccessibleRecordSetService.cs:1832-2045`
- `…/Infrastructure/DI/ExternalAccessModule.cs:255-273`
- `projects/spaarke-auth-system-of-record-r1/working/x09c-membership-and-guests.md Q1`

**Suggested fix**

Complete UAC-r2 task 036 (consume the impersonated root set), or amend ADR-028 A5 to name the approximation and its limits.

**Estimated effort**: task 036
**Related**: record H-1; ADR-028 R46

---

### ISS-014 — Stamps have no Power BI, RAG API key or SignalR settings in the secret catalog (no real-time notifications, RAG enqueue 401)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | customer-provisioning-orchestration-r1 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1570 |

**Description**

`PowerBi__ClientSecret`, `Rag__ApiKey` and `Notifications__SignalR__ConnectionString` are absent from `scripts/canonical-secret-catalog/manifest.yaml` and from the BFF template. A stamp starts, but SignalR notifications fall back to a null object, `POST /api/ai/rag/enqueue-indexing` returns 401, and reporting cannot embed. On dev these are plain App Service settings, not Key Vault references (x06).

**Failure mode**: Users on a provisioned customer environment get no real-time notifications; bulk RAG indexing callers are refused.

**Entry-points**

- `scripts/canonical-secret-catalog/manifest.yaml`
- `src/server/api/Sprk.Bff.Api/Services/Notifications/SignalRDeliveryService.cs:244-258`
- `projects/spaarke-auth-system-of-record-r1/working/x09b-model1-client-config.md Q4(g)`

**Suggested fix**

Owner decides which features stamps need; add those secrets to the catalog as Key Vault references (or move SignalR to Entra auth).

**Estimated effort**: 1 day
**Related**: record M-10; §13a

---

### ISS-015 — Office add-in auth failures: /api/office/communications turns OBO 401/403 into 500; NAA strategy pops up without a gesture and picks accounts[0]

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | spaarkeai-word-add-in-r1 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1571 |

**Description**

`/api/office/communications/*` converts an OBO 401/403 into 500 "Lookup Failed". `OfficeNaaStrategy` ignores `requireSilentOnly`; the eager startup acquire can open a popup with no user gesture on Office on the web (popup blocked); it picks `accounts[0]` with no tenant filter, which can select the wrong cached account for multi-account guests. Two raw fetch sites lack 401 retry.

**Failure mode**: Intermittent silent sign-in failures on Office on the web; auth problems shown as server errors.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/Office/CommunicationsEndpoints.cs:82-84,267-279`
- `src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts:218-256,357`
- `src/client/office-addins/shared/services/AuthService.ts:100-104`
- `src/client/office-addins/shared/taskpane/App.tsx:515-536,581-585`

**Suggested fix**

Map OBO auth failures to 401/403; honour requireSilentOnly and defer the first acquire to a gesture; pick the account matching the authority tenant.

**Estimated effort**: 1-2 days
**Related**: record M-3, M-15

---

### ISS-016 — Environment defaults point at dev: SpaarkeMaster env-var definitions default to Spaarke dev values; the BFF template lacks the extra token audiences

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | Dataverse solution + BFF configuration |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1573 |

**Description**

The four `sprk_*` env-var definitions (`TenantId`, `MsalClientId`, `BffApiAppId`, `BffApiBaseUrl`) default to Spaarke dev values, so an import without value rows signs users into Spaarke's tenant against the dev BFF. `appsettings.template.json` carries one audience; dev accepts the Teams-SSO and bare-GUID audiences only through hand-set `AzureAd__ValidAudiences__0..2`, so a redeploy from template drops them.

**Failure mode**: A new environment silently talks to the dev BFF; a dev redeploy from template breaks Teams and Copilot sign-in.

**Entry-points**

- `src/dataverse/solutions/SpaarkeMaster/environmentvariabledefinitions/{sprk_TenantId,sprk_MsalClientId,sprk_BffApiAppId,sprk_BffApiBaseUrl}/environmentvariabledefinition.xml:2`
- `src/server/api/Sprk.Bff.Api/appsettings.template.json:37-42`

**Suggested fix**

Remove the dev defaults (fail loudly when unset); move the extra audiences into the template/catalog or retire them with the R3 sign-in change.

**Estimated effort**: 1 day
**Related**: record M-8

---

### ISS-017 — BFF auth pipeline hardening: agent filter checks no audience; one Admin role unlocks admin surfaces with no actor; token logger; CORS origin echo; RAG key chooses tenant

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | BFF platform |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1574 |

**Description**

`AgentAuthorizationFilter` has a TODO for its audience/app-role check, so any default-scheme token with oid+tid passes `/api/agent/*`. One `Admin` app role unlocks `/api/spe/**`, `/api/admin/*`, registration approval (creates Entra users + licences) and RAG admin, with no Dataverse identity or actor recorded. A Warning-level "remove before production" token logger runs on every `/api*` request. The exception handler echoes any Origin as Access-Control-Allow-Origin. `*.powerappsportals.com` is allowed with credentials. The RAG API-key caller chooses the tenant; `TenantAuthorizationFilter` passes when no tenant is named; `MembershipEndpoints` defaults the tenant to "anonymous".

**Failure mode**: No visible break today, but each is an access or tenant-isolation weakness that passes by omission.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/Agent/AgentAuthorizationFilter.cs:80-81`
- `…/Infrastructure/DI/AuthorizationModule.cs:375-383`
- `…/Infrastructure/DI/MiddlewarePipelineExtensions.cs:88-94,121-158`
- `…/Infrastructure/DI/CorsModule.cs:101-116`
- `…/Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs:89-97`
- `…/TenantAuthorizationFilter.cs:79-84`

**Suggested fix**

Fix each in place; most are small.

**Estimated effort**: 2-3 days
**Related**: record M-4, M-5, M-16, L-4

---

### ISS-018 — Authorization consistency: three oid->systemuser resolvers (disabled users), own-flag readers after task 174, shared HttpClient header race on document rights

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | unified-access-control-r2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1576 |

**Description**

`CallerSystemUserResolver` (communications send, thread reads, impersonated reads) applies no `isdisabled` filter, while `SystemUserIdentityResolver` and `MembershipEndpoints` do. Three external-on-Restricted decisions (Office JIT writer, SPE membership sync, secure-child share sync) still read the record's own flag instead of the effective flag. `DataverseAccessDataSource.GetUserAccessAsync` sets the caller's OBO token on a shared HttpClient's default headers on the path that decides document rights. External-surface writes carry no creator attribution.

**Failure mode**: A disabled user can still be resolved and impersonated on the communications path; a child whose effective flag differs from its own can be mis-authorized.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Communication/CallerSystemUserResolver.cs:101-119`
- `…/Services/Documents/OfficeEditAccessService.cs:231-239`
- `…/Services/Access/SpeContainerMembershipSync.cs:442-443`
- `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs:298-300,453-457`

**Suggested fix**

One resolver with the disabled filter; fold the three readers to effective flags; pass the token per request.

**Estimated effort**: 2-3 days
**Related**: record M-13, M-18, M-20, L-5

---

### ISS-019 — Outbound credential hygiene: un-pinned DataverseWebApiService credential, Service Bus SAS, SignalR key, ACS gaps, default order includes ClientSecret

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | BFF platform |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1577 |

**Description**

`DataverseWebApiService` (the impersonation writer) builds its own un-pinned `DefaultAzureCredential` with inverted key precedence; 17 direct `TokenCredential` consumers bypass the MI flag. The template still offers Service Bus as a SAS string; SignalR uses a shared access key; Power BI uses a client secret. ACS: optional `?sig=` on Event Grid ingress, local auth not disabled, no UAMI role on the ACS resource in the stamp template. The credential order still includes ClientSecret when the section is absent.

**Failure mode**: Identity selection depends on environment variables instead of being pinned; key-shaped credentials remain where ADR-028 A4/A6 require MI.

**Entry-points**

- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:56-64,106-117`
- `src/server/api/Sprk.Bff.Api/appsettings.template.json:31-35`
- `…/Services/Notifications/SignalRDeliveryService.cs:246-253`
- `infrastructure/bicep/modules/acs-communication.bicep:70-128`
- `…/Infrastructure/DI/AuthorizationModule.cs:486-490`

**Suggested fix**

Route every consumer through the pinned factory; move SignalR/Service Bus to Entra auth; make ACS signature mandatory and grant the UAMI a role.

**Estimated effort**: 3-5 days
**Related**: record M-6, M-7, M-10

---

### DEF-001 — Reporting app roles (sprk_ReportingAccess/Author/Admin) are defined nowhere, so reporting is 403 for everyone

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | owner: deferred (reporting not implemented) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1569 |

**Description**

`ReportingAuthorizationFilter` requires `roles` to contain `sprk_ReportingAccess` (Author/Admin for more). No registration defines those roles (dev: `Admin` only; H3: KeylessProof only). `SystemAdmin` is likewise undefined. Owner decision 2026-10-09: reporting is not implemented yet; defer.

**Failure mode**: Any reporting caller gets 403 once the module is enabled.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/Reporting/ReportingAuthorizationFilter.cs:80-87,154-169`

**Suggested fix**

When reporting is built: define the roles on the BFF registration (H3) and assign them.

**Estimated effort**: hours
**Related**: record H-8

---

### DEF-002 — Auth hygiene: dead authorization surfaces, client library gaps, packaging and doc drift

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | various |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1579 |

**Description**

Dead or dormant surfaces that would mis-authorize if wired: 23 `can*` policies with a wrong-domain extractor, the deny-all TenantRouting router, `GrantMembershipAsync`, `AgentTokenService`. Library gaps: the 401 retry re-sends the same bearer, `bffApiScope` is not validated, and `initAuth` cannot build the Office strategy. Add-in packaging leftovers; Teams/SPA polish; PCF sign-in precedence drift; L2 startup coupling (`ReservedTenants__*`, T7 guard no-op on a KV reference); the dead bot template; Model 2 not provisionable; registry/doc drift (x02: 162 docs to update, 33 to supersede).

**Failure mode**: No user-visible break today; each is a trap for the next change.

**Entry-points**

- `projects/spaarke-auth-system-of-record-r1/auth-system-of-record.md §11 M-9, M-12, M-14, L-2, L-3, L-6..L-10, L-12, L-16, H-10`
- `projects/spaarke-auth-system-of-record-r1/working/x02-docs-reconciliation.md`

**Suggested fix**

Address with the ADR-028 amendment and doc supersession (project step 4), or as touched.

**Estimated effort**: ongoing
**Related**: record §11, §14

---

### ISS-020 — Dev identity posture cleanup (owner decisions; dev only)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday |
| **Filed** | 2026-10-09 |
| **Source** | Auth system-of-record investigation |
| **Owner** | owner |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1578 |

**Description**

Dev-only findings that do not carry into provisioned stamps (H3 is secret-free and sets its own pre-auth list): the dev BFF app's remaining client secret; Azure CLI and an owner-less SPA client pre-authorized on it; Postman/localhost redirect URIs; four secrets as plain App Service settings (rotate `Compose__Webhook__ClientState`, printed once by a read-only script); the production BFF app as System Administrator in dev Dataverse; the GitHub OIDC app's extra password credential; orphan app users; the unowned `infra/insights` shell (shared-key storage).

**Failure mode**: No functional break; each widens what a stolen developer credential or secret could do on dev.

**Entry-points**

- `projects/spaarke-auth-system-of-record-r1/working/x04-live-entra-readout.md`
- `projects/spaarke-auth-system-of-record-r1/working/x06-live-batch-a.md`
- `projects/spaarke-auth-system-of-record-r1/working/x07-live-batch-a-dataverse.md`
- `projects/spaarke-auth-system-of-record-r1/working/x09b-model1-client-config.md Q4`

**Suggested fix**

Owner reviews and decides each item; each is a live change with a rollback.

**Estimated effort**: hours each
**Related**: record H-11..H-15, M-11, M-19, L-13..L-15

---
