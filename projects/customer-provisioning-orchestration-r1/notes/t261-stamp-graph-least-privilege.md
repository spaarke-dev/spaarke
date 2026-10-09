# T261 — Stamp Graph least privilege (G31)

> **Task**: 261 · **Date**: 2026-10-09 · **Owner instruction**: "ensure this is accurately scoped"
> **Status**: implemented (code + tests); no live action taken. Read-only evidence only (Graph GETs and App Insights queries as `ralph.schroeder@spaarke.com`).
> **Guard**: `tests/Spaarke.ArchTests/TenantIsolation/StampGraphAppRoleEvidenceTests.cs` fails when the catalogs and §7 of this note disagree, or when a role has no evidence row with a Microsoft Learn URL.

## 1. Result

| Identity | Before | After |
|---|---|---|
| **Customer stamp identity** (`mi-spaarke-{customerId}-{env}`, H10 grants in Entra) | 11 Entra roles: `FileStorageContainer.Selected`, `Files.Read.All`, `Files.ReadWrite.All`, `Sites.Read.All`, `Sites.ReadWrite.All`, `User.Read.All`, `Group.Read.All`, `User.ReadWrite.All`, `GroupMember.ReadWrite.All`, `Directory.ReadWrite.All`, `User.Invite.All` | **1 Entra role: `FileStorageContainer.Selected`**. H10 now **removes** every other Graph app role on the stamp identity; H13 T3 fails on any. |
| Stamp identity, Exchange-scoped (H14a, RBAC for Applications, scoped to the customer's group) | `Mail.Read`, `Mail.ReadWrite`, `Mail.Send`, `MailboxSettings.Read` | `Mail.Read`, `Mail.ReadWrite`, `Mail.Send` (`MailboxSettings.Read` has no caller) |
| **L2 control-plane Worker identity** (`sprk-controlplane-{env}-uami`, operator grant) | the BFF stamp catalog (15 roles, incl. all mailbox roles tenant-wide); **missing** `AppRoleAssignment.ReadWrite.All` and `Application.ReadWrite.OwnedBy` | its own evidence-backed catalog, `ControlPlaneGraphAppRoles.cs` (§4) |

The drop is safe for features because **no app-only call on a stamp needs a dropped role** (§2–§3): every SharePoint call is inside an SPE container, the directory writes belong to the demo self-service registration feature (now not registered on stamps), the B2B invitations run as the L2 Worker, and the security pages are platform-operator only.

## 2. Who calls Graph as the stamp identity

- **App-only = the stamp managed identity.** `GraphClientFactory.ForApp()` uses `DefaultAzureCredential` pinned to `Graph:ManagedIdentity:ClientId` when `Graph:ManagedIdentity:Enabled=true` (`src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`: `CreateAppOnlyClient` `:134`, managed-identity branch `:138`, client-id pin `:146`, tenant pin `:166`). H4b writes `Graph__ManagedIdentity__Enabled=true` and the stamp UAMI's client id on every stamp (`scripts/canonical-secret-catalog/manifest.yaml:697-710`). The app-only client is built on **Graph beta** (`GraphClientFactory.cs:204`); delegated clients use v1.0 — this is how live telemetry below separates app-only from OBO calls.
- **Delegated (OBO)** calls run as the signed-in user through the per-customer BFF app registration (§5). They use the user's rights, not the stamp's app roles.
- **CIAM** user creation (`CiamGraphClientFactory.cs:66-133`) uses a separate CIAM-tenant app registration with a Key Vault certificate — not the stamp identity, not in this catalog.
- **L2 Worker as the stamp identity: none.** L2 cannot sign in as the stamp UAMI; it signs in as the customer BFF app registration (MI-FIC) only for Dataverse (H6/H7/H7b). H13's keyless proof calls the stamp BFF's `POST /api/platform/keyless-proof`, whose service list has no Graph entry (`src/server/shared/Contracts/KeylessProofContract.cs:59-70`). Every L2 Graph call runs as the L2 Worker (§4) or the SPE owning app (H0/H8/H13 T6).

## 3. App-only call inventory (stamp BFF)

Learn URL form: `https://learn.microsoft.com/en-us/graph/api/<slug>?view=graph-rest-1.0`. "Live" = dev App Insights `spe-insights-dev-67e2xz`, 30 days to 2026-10-09, beta-path (= app-only) dependency calls by `spaarke-bff-dev`, whose managed identity `mi-bff-api-dev` holds **no `Files.*` or `Sites.*` role** (read live 2026-10-09).

### 3.1 SharePoint Embedded — client from `SpeContainerOwnershipGuard` (gate: always)

| Endpoint (beta) | Method | Caller file:line (BFF `src/server/api/Sprk.Bff.Api/`) | Feature / route | Least-privileged app role (Learn) | Verdict |
|---|---|---|---|---|---|
| `/storage/fileStorage/containers` | POST | `Infrastructure/Graph/ContainerOperations.cs:78`; `Infrastructure/Graph/SpeAdminGraphService.cs:1270` | `POST /api/v1/external-access/provision-project`; `POST /api/spe/containers` | FileStorageContainer.Selected — [filestoragecontainer-post](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-post?view=graph-rest-1.0) | keep FSC.Selected |
| `/storage/fileStorage/containers/{id}` (+`?$select`), `…/drive` | GET | `Infrastructure/Graph/SpeContainerOwnershipGuard.cs:338`; `ContainerOperations.cs:190`; `SpeAdminGraphService.cs:1079,1328,2825` | every ownership check; drive resolve on upload/compose/office routes; `/api/spe/containers/{id}` | FileStorageContainer.Selected — [filestoragecontainer-get](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-get?view=graph-rest-1.0), [filestoragecontainer-get-drive](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-get-drive?view=graph-rest-1.0) | keep. Live: 5,072 OK |
| `/storage/fileStorage/containers?$filter=containerTypeId eq …` | GET | `SpeAdminGraphService.cs:825,1162,4297` | dashboard sync job; `GET /api/spe/containers`; `POST /api/spe/search/containers` (results filtered to owned) | FileStorageContainer.Selected — [filestorage-list-containers](https://learn.microsoft.com/en-us/graph/api/filestorage-list-containers?view=graph-rest-1.0) | keep |
| `/storage/fileStorage/containers/{id}` | PATCH / DELETE | `SpeAdminGraphService.cs:1428,1475,1520,1566,7257`; `ContainerOperations.cs:146`; `SpeContainerOwnershipGuard.cs:312` | `PATCH /api/spe/containers/{id}`, activate/lock/unlock, bulk delete, create clean-up | FileStorageContainer.Selected — [filestoragecontainer-update](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-update?view=graph-rest-1.0), [filestorage-delete-containers](https://learn.microsoft.com/en-us/graph/api/filestorage-delete-containers?view=graph-rest-1.0) | keep. Live: PATCH 30 OK, DELETE 5 OK |
| `/storage/fileStorage/containers/{id}/customProperties` | PATCH | `SpeContainerOwnershipGuard.cs:274-281`; `SpeAdminGraphService.cs:2883,3026`; `Infrastructure/ExternalAccess/SpeContainerMembershipService.cs:713` | ownership marker on create; business-unit stamp; grant markers | FileStorageContainer.Selected — [filestoragecontainer-update-customproperty](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-update-customproperty?view=graph-rest-1.0) | keep |
| `/storage/fileStorage/containers/{id}/permissions[/{pid}]` | GET / POST / PATCH / DELETE | `SpeContainerMembershipService.cs:220,352,581,724,783`; `SpeAdminGraphService.cs:3443,3529,3581,3634`; `Services/Registration/DemoExpirationService.cs:315,343` | external-access revoke/close; membership sync job; office edit access; `/api/spe/containers/{id}/permissions` | FileStorageContainer.Selected — [filestoragecontainer-list-permissions](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-list-permissions?view=graph-rest-1.0), [filestoragecontainer-post-permissions](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-post-permissions?view=graph-rest-1.0), [filestoragecontainer-delete-permissions](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-delete-permissions?view=graph-rest-1.0) | keep |
| `/storage/fileStorage/containers/{id}/recycleBin/items[…]`, `/storage/fileStorage/deletedContainers[…]`, `…/archive`, `…/columns` | GET / POST / DELETE / PATCH | `SpeAdminGraphService.cs:6210,6302,6447-6453,6645,6712,6808,7024,3672,3718,3764,3811` | `/api/spe/recyclebin*`, archive/unarchive, columns | FileStorageContainer.Selected — [filestoragecontainer-list-recyclebinitem](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-list-recyclebinitem?view=graph-rest-1.0), [filestorage-list-deletedcontainers](https://learn.microsoft.com/en-us/graph/api/filestorage-list-deletedcontainers?view=graph-rest-1.0), [filestoragecontainer-restore](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-restore?view=graph-rest-1.0) | keep |
| `/drives/{d}/items/{i}` (+`?$select`), `…/versions`, `…/versions/{v}/content`, `…/thumbnails`, `…/children` | GET | `Infrastructure/Graph/DriveItemOperations.cs:253,324,405,563,654,740,970,1151`; `SpeAdminGraphService.cs:919,3076,3140,3270` | document pointer checks, versions, Compose, office/open links, SPE admin items | Generic table: Files.Read.All; **SPE note on each page: "SharePoint Embedded requires the FileStorageContainer.Selected permission to access the content of the container"** — [driveitem-get](https://learn.microsoft.com/en-us/graph/api/driveitem-get?view=graph-rest-1.0), [driveitem-list-versions](https://learn.microsoft.com/en-us/graph/api/driveitem-list-versions?view=graph-rest-1.0) | keep FSC.Selected; Files.Read.All not needed. Live: GET items 5,879 OK, versions 329 OK |
| `/drives/{d}/items/{i}/content` | GET | `DriveItemOperations.cs:480`; `SpeAdminGraphService.cs:3285` | `GET /api/documents/{id}/content|download|eml-render`; RAG/invoice/attachment jobs | Files.Read.All (generic) / FSC.Selected (SPE note) — [driveitem-get-content](https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0) | keep FSC.Selected. Live: 784 OK |
| `/drives/{d}/root:/{path}:/content`, `/drives/{d}/items/{i}/content` | PUT | `Infrastructure/Graph/UploadSessionManager.cs:68-90,442`; `SpeAdminGraphService.cs:4073` | uploads (OBO routes use the app client after the guard), inbound mail archive, Compose save, office upload | Files.ReadWrite.All (generic) / FSC.Selected (SPE note) — [driveitem-put-content](https://learn.microsoft.com/en-us/graph/api/driveitem-put-content?view=graph-rest-1.0) | keep FSC.Selected. Live: 667 OK |
| `/drives/{d}/root:/{path}:/createUploadSession` | POST | `UploadSessionManager.cs:639`; `SpeAdminGraphService.cs:4122` | `POST /api/obo/records/{e}/{id}/upload-session`; SPE admin large upload | Sites.ReadWrite.All (generic) / FSC.Selected (SPE note) — [driveitem-createuploadsession](https://learn.microsoft.com/en-us/graph/api/driveitem-createuploadsession?view=graph-rest-1.0) | keep FSC.Selected. Live: 1 OK (app-only) |
| `/drives/{d}/items/{i}` | DELETE | `DriveItemOperations.cs:183`; `SpeAdminGraphService.cs:3405` | `DELETE /api/documents/{id}`, rollbacks | Files.ReadWrite.All (generic) / FSC.Selected (SPE note) — [driveitem-delete](https://learn.microsoft.com/en-us/graph/api/driveitem-delete?view=graph-rest-1.0) | keep. Live: 167 OK |
| `/drives/{d}/items/{i}/preview` | POST | `DriveItemOperations.cs:826,905`; `SpeAdminGraphService.cs:3348` | checkout/checkin; `GET /api/documents/{id}/preview-url` | Files.Read.All (generic) / FSC.Selected (SPE note) — [driveitem-preview](https://learn.microsoft.com/en-us/graph/api/driveitem-preview?view=graph-rest-1.0) | keep. Live: 32 OK |
| `/drives/{d}/items/{i}/createLink` | POST | `DriveItemOperations.cs:942`; `SpeAdminGraphService.cs:3211` | `POST /api/documents/{id}/share-link` | Files.ReadWrite.All (generic) / FSC.Selected (SPE note) — [driveitem-createlink](https://learn.microsoft.com/en-us/graph/api/driveitem-createlink?view=graph-rest-1.0) | keep FSC.Selected. Live: 1 call, 403 — also 403 delegated (2/2): sharing, not a Graph role (§8 F5) |
| `/drives/{d}/items/root/delta` | GET | `Infrastructure/Graph/SpeFileStore.cs:461,463,497` | `POST /api/compose/webhooks/spe-doc-changed`, `…/check-changes` | Files.Read.All (no SPE note) — [driveitem-delta](https://learn.microsoft.com/en-us/graph/api/driveitem-delta?view=graph-rest-1.0) | keep FSC.Selected. Live: 15/16 OK app-only |
| `/subscriptions` (resource `drives/{id}/root`) | POST / PATCH | `SpeFileStore.cs:415,432` | `GET /api/compose/documents/{id}` (EnsureSubscription); `SpeWebhookRenewalHostedService` | ODfB driveItem row: Files.Read.All (POST), Files.ReadWrite.All (PATCH) — [subscription-post-subscriptions](https://learn.microsoft.com/en-us/graph/api/subscription-post-subscriptions?view=graph-rest-1.0); SPE webhook page names none — [respond-to-changes-webhooks](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/respond-to-changes-webhooks) | keep FSC.Selected. Live: trace "Created SPE webhook subscription d87b6c61…" (2026-09-02) and renewals through 2026-10-09 (`PATCH /beta/subscriptions/98508b06…` 5×200 today) by an identity with no Files.*/Sites.* role |
| `/search/query` (`entityTypes: driveItem`, `region`) | POST | `SpeAdminGraphService.cs:7432` | `POST /api/spe/search/items` | Files.Read.All — [search-query](https://learn.microsoft.com/en-us/graph/api/search-query?view=graph-rest-1.0); **SPE: "Search supports delegated permissions only"** — [search-containers-files](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/search-containers-files) | **drop; the route answers 501 `spe-search-requires-delegated`** (`SearchItemsEndpoints.NotSupportedForIdentity`, on Graph 401/403). Microsoft: SPE search is delegated-only. A prior live probe (sdap-SPE-admin-app-r2 `notes/search-root-cause.md`, probe G, 2026-08-21) got 200 hits from an app-only token that held `Files.ReadWrite.All`; whether those hits were SPE content is not stated, and a token with only `FileStorageContainer.Selected` was not tried. `Files.Read.All` on every stamp would let each customer's identity search ALL of Spaarke's SharePoint/OneDrive, so it is not kept. No call in 30 days of dev telemetry. Rewrite owner: sdap-SPE-admin-app-r2 (§8 F2) |

### 3.2 Mail — Exchange RBAC for Applications on stamps (H14a), never Entra (gate: always registered; data-gated by `sprk_communicationaccount` rows)

| Endpoint (beta) | Method | Caller file:line | Feature / route | Least-privileged app role (Learn) | Verdict |
|---|---|---|---|---|---|
| `/users/{mbx}/messages/{id}` (+`$expand=attachments`), `…/attachments`, `…/mailFolders/{f}/messages`, `…/messages?$top=1` | GET | `Services/Communication/IncomingCommunicationProcessor.cs:232,927,957`; `Services/Communication/InboundPollingBackupService.cs:193`; `Services/Communication/MailboxVerificationService.cs:220` | incoming-mail job (webhook `POST /api/communications/incoming-webhook`, polling, delta); `POST /api/communications/accounts/{id}/verify` | Mail.Read (bodies/attachments; Mail.ReadBasic.All lacks body) — [message-get](https://learn.microsoft.com/en-us/graph/api/message-get?view=graph-rest-1.0), [message-list-attachments](https://learn.microsoft.com/en-us/graph/api/message-list-attachments?view=graph-rest-1.0) | `Mail.Read` (Exchange-scoped). Live: 15,384 OK |
| `/users/{mbx}/mailFolders/{f}/messages/delta` | GET | `Services/Communication/GraphMailFolderDeltaReader.cs:51,56,92` | `MailboxDeltaReconciliationService` (15 min) | Mail.ReadBasic.All / Mail.Read — [message-delta](https://learn.microsoft.com/en-us/graph/api/message-delta?view=graph-rest-1.0) | `Mail.Read` |
| `/subscriptions` (resource `users/{mbx}/mailFolders/{f}/messages`), `/subscriptions/{id}` | POST / PATCH / DELETE / GET | `Services/Communication/GraphSubscriptionManager.cs:440,467,491,523,561`; `MailboxVerificationService.cs:351`; `IncomingCommunicationProcessor.cs:168` | `GraphSubscriptionManager` (30 min); verify | Mail.Read — [subscription-post-subscriptions](https://learn.microsoft.com/en-us/graph/api/subscription-post-subscriptions?view=graph-rest-1.0), [subscription-list](https://learn.microsoft.com/en-us/graph/api/subscription-list?view=graph-rest-1.0) | `Mail.Read` |
| `/users/{mbx}/messages/{id}` `{isRead:true}` | PATCH | `IncomingCommunicationProcessor.cs:1335` | incoming-mail job | Mail.ReadWrite — [message-update](https://learn.microsoft.com/en-us/graph/api/message-update?view=graph-rest-1.0) | `Mail.ReadWrite`. Live: dev identity lacks it → 3/3 **403** (§8 F4) |
| `/users/{from}/sendMail`, `…/mailFolders/sentitems/messages` | POST / GET | `Services/Communication/Channels/EmailChannelSender.cs:69,221`; `MailboxVerificationService.cs:185` | `POST /api/communications/send|send-bulk` (shared mailbox); registration mail; verify | Mail.Send — [user-sendmail](https://learn.microsoft.com/en-us/graph/api/user-sendmail?view=graph-rest-1.0) | `Mail.Send`. Live: 10 OK |
| `/users/{id}/mailboxSettings` | GET | none (no caller in `src/`) | — | MailboxSettings.Read — [user-get-mailboxsettings](https://learn.microsoft.com/en-us/graph/api/user-get-mailboxsettings?view=graph-rest-1.0) | **drop** `MailboxSettings.Read` |

Exchange RBAC for Applications grants are additive to Entra grants: "the union of an unscoped Mail.Read grant from Microsoft Entra and a resource-scoped Mail.Read grant in Application RBAC results in no effective resource scoping" — [application-rbac](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac). Hence the mailbox roles must never be Entra grants on a stamp (T251, H13 T3).

### 3.3 Directory — demo self-service registration only (gate: the complete `DemoProvisioning` settings, task 261)

| Endpoint (beta) | Method | Caller file:line | Feature / route | Least-privileged app role (Learn) | Verdict |
|---|---|---|---|---|---|
| `/users?$filter=userPrincipalName eq` | GET | `Services/Registration/GraphUserService.cs:104` | `POST /api/registration/requests/{id}/approve` | User.Read.All — [user-list](https://learn.microsoft.com/en-us/graph/api/user-list?view=graph-rest-1.0) | not on stamps |
| `/users` | POST | `GraphUserService.cs:82` | approve | User.Create — [user-post-users](https://learn.microsoft.com/en-us/graph/api/user-post-users?view=graph-rest-1.0) | not on stamps |
| `/users/{id}/assignLicense` (+ `GET ?$select=assignedLicenses`) | POST / GET | `GraphUserService.cs:210,360` | approve | LicenseAssignment.ReadWrite.All — [user-assignlicense](https://learn.microsoft.com/en-us/graph/api/user-assignlicense?view=graph-rest-1.0) | not on stamps |
| `/groups/{gid}/members/$ref`, `/groups/{gid}/members?$filter`, `…/members/{id}/$ref` | POST / GET / DELETE | `GraphUserService.cs:268,384,301` | approve; `DemoExpirationService` (daily) | GroupMember.ReadWrite.All — [group-post-members](https://learn.microsoft.com/en-us/graph/api/group-post-members?view=graph-rest-1.0) | not on stamps |
| `/users/{id}` `{accountEnabled:false}` (+GET) | PATCH / GET | `GraphUserService.cs:332,320` | `DemoExpirationService` | User.EnableDisableAccount.All + User.Read.All — [user-update](https://learn.microsoft.com/en-us/graph/api/user-update?view=graph-rest-1.0) | not on stamps |

Before task 261 these were registered and mapped on every stamp (`Program.cs:133`, `EndpointMappingExtensions.cs`), with no stamp setting for `DemoProvisioning:*` — so the feature could not work on a stamp, and its hosted service `DemoExpirationService` reads `IOptions<DemoProvisioningOptions>.Value` (`[Required]` keys) in its constructor, which throws at host start (§8 F1, fixed). `RegistrationModule.IsDemoProvisioningEnabled` (binds the section and validates every `[Required]` key, license SKUs included; a PARTIAL set disables the feature, fail-closed, instead of crashing host start) now registers `GraphUserService`, `DemoProvisioningService`, `EmailDomainValidator` and `DemoExpirationService`, and maps `/api/registration/*`, only where the full set is present — Spaarke's platform BFF (`spaarke-bff-dev` has all six keys, read live 2026-10-09; no stamp channel writes it, which the ArchTest checks).

### 3.4 Security — platform operator only (gate: `SpeAdmin:PlatformOperatorEnvironment`)

| Endpoint (beta) | Method | Caller file:line | Feature / route | Least-privileged app role (Learn) | Verdict |
|---|---|---|---|---|---|
| `/security/alerts_v2`, `/security/secureScores` | GET | `Infrastructure/Graph/SpeAdminGraphService.cs:7078,7161` | `GET /api/spe/security/alerts|score` (`RequireSpeAdminPlatformOperator`, `Api/SpeAdmin/SecurityEndpoints.cs:97`) | SecurityAlert.Read.All / SecurityEvents.Read.All — [security-list-alerts_v2](https://learn.microsoft.com/en-us/graph/api/security-list-alerts_v2?view=graph-rest-1.0), [security-list-securescores](https://learn.microsoft.com/en-us/graph/api/security-list-securescores?view=graph-rest-1.0) | not on stamps: `Deploy-BffApi.ps1:277-289` sets the marker only for operator environments (today `dev`) |

### 3.5 Calls with no production caller

`ContainerOperations.ListContainersAsUserAsync`, `DriveItemOperations.ListChildrenAsync` (`:86,:95`), `SpeFileStore.DeleteSubscriptionAsync` (`:444`), `UploadSessionManager.UploadChunkAsUserAsync` (`:719`), the app-only container-TYPE wrappers in `SpeAdminGraphService` (container-type operations run delegated only), `AgentTokenService.AcquireGraphTokenAsync` (ISS-017). None needs a role.

## 4. L2 control-plane Worker identity (`ControlPlaneGraphAppRoles.cs`)

All Graph calls the Worker makes as itself (`DefaultAzureCredential(TenantId)`, which resolves to the Worker UAMI — `infrastructure/bicep/modules/controlplane-worker-app-service.bicep:427-428`). Paths under `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/`.

| Role | Calls (handler, file:line) | Learn evidence | Notes |
|---|---|---|---|
| `Application.ReadWrite.OwnedBy` | H3 `POST/PATCH /applications`, `addPassword`, FIC `POST/DELETE`, `POST /servicePrincipals` — `EntraAppReg/GraphAppRegistrationProvisioner.cs:507,518,581,720,812,1057,1082,1299,1307` | [application-update](https://learn.microsoft.com/en-us/graph/api/application-update?view=graph-rest-1.0) (least), [serviceprincipal-post-serviceprincipals](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-post-serviceprincipals?view=graph-rest-1.0) (least), [application-post-applications](https://learn.microsoft.com/en-us/graph/api/application-post-applications?view=graph-rest-1.0) (higher; the least, `AppRegistration.Create`, is not in the permissions reference) | **Was not granted** by any prerequisite. Live check owed: after an app-only create, the Worker is in `owners`. |
| `AppRoleAssignment.ReadWrite.All` | H10 `POST/DELETE /servicePrincipals/{stamp}/appRoleAssignments` (`DataverseAppUserGraphParity/GraphAppRoleRest.cs`); H3 `POST/DELETE /servicePrincipals/{bff}/appRoleAssignedTo` (`GraphAppRegistrationProvisioner.cs:956,965`) | [serviceprincipal-post-approleassignments](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-post-approleassignments?view=graph-rest-1.0) ("AppRoleAssignment.ReadWrite.All and Application.Read.All"; higher: "… and Directory.Read.All"), [serviceprincipal-delete-approleassignments](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-delete-approleassignments?view=graph-rest-1.0) | **Was not granted.** `Application.ReadWrite.All` does not cover Graph app roles ("except those exposed by Microsoft Graph"). Self-escalating: residual risk §6. |
| `Directory.Read.All` | H3 consent `GET /servicePrincipals/{id}/oauth2PermissionGrants` (`EntraAppReg/GraphAdminConsentVerifier.cs:116`); pair for the POSTs above; `GET /servicePrincipals` (H10, H13 T3), `GET /organization` (`IntegrationWiring/ExchangeAdminTokenSource.cs:197`), `GET /users`, `GET /groups/{id}` (H11) | [serviceprincipal-list-oauth2permissiongrants](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-list-oauth2permissiongrants?view=graph-rest-1.0) (least), [organization-list](https://learn.microsoft.com/en-us/graph/api/organization-list?view=graph-rest-1.0) (higher of Organization.Read.All) | Read-only; replaces the Worker's use of `Directory.ReadWrite.All`. |
| `User.Invite.All` | H11 `POST /invitations` — `UserProvisioning/GraphRestB2BInvitationClient.cs:188` | [invitation-post](https://learn.microsoft.com/en-us/graph/api/invitation-post?view=graph-rest-1.0) | Tenant-wide; no narrower Graph form exists. |
| `User.Create` | H11 `POST /users` (NativeAccount preset) — `UserProvisioning/GraphRestUserProvisioner.cs:118` | [user-post-users](https://learn.microsoft.com/en-us/graph/api/user-post-users?view=graph-rest-1.0) (least) | Narrower than `User.ReadWrite.All`. |
| `LicenseAssignment.ReadWrite.All` | H11 `POST /users/{id}/assignLicense` — `GraphRestUserProvisioner.cs:186` | [user-assignlicense](https://learn.microsoft.com/en-us/graph/api/user-assignlicense?view=graph-rest-1.0) (least) | |
| `GroupMember.ReadWrite.All` | H11 `POST /groups/{id}/members/$ref` — `UserProvisioning/GraphRestEnvironmentSecurityGroupClient.cs:110` | [group-post-members](https://learn.microsoft.com/en-us/graph/api/group-post-members?view=graph-rest-1.0) | No owner-scoped member-write permission exists (the permissions reference has no `Group*.OwnedBy`). Cannot add to role-assignable groups without `RoleManagement.ReadWrite.Directory`. |
| `Mail.Read` | H14b `GET/PATCH/POST /subscriptions` on mailbox messages — `IntegrationWiring/GraphRestSubscriptionCreator.cs:106,129,146` | [subscription-post-subscriptions](https://learn.microsoft.com/en-us/graph/api/subscription-post-subscriptions?view=graph-rest-1.0), [subscription-list](https://learn.microsoft.com/en-us/graph/api/subscription-list?view=graph-rest-1.0) | Tenant-wide in Entra (the Worker is not Exchange-scoped). Residual §6. |

Not the Worker's: H0/H8/H13-T6 SPE calls sign in as the SPE owning app (`SpeContainer/SpeConfidentialClientGraphFactory.cs`); Exchange cmdlets use the Exchange admin app (`IntegrationWiring/ExchangeAdminTokenSource.cs:44`).

The Worker holds today (live): the old 15. `Grant-ControlPlaneIdentity.ps1` is add-only, so the next grant ADDS `Application.ReadWrite.OwnedBy`, `AppRoleAssignment.ReadWrite.All`, `Directory.Read.All`, `User.Create`, `LicenseAssignment.ReadWrite.All`; removing what it no longer needs (`Directory.ReadWrite.All`, `User.ReadWrite.All`, `Files.*`, `Sites.*`, `FileStorageContainer.Selected`, `Group.Read.All`, `User.Read.All`, `Mail.ReadWrite`, `Mail.Send`, `MailboxSettings.Read`) is an operator step with the owner's OK (§8 F3).

## 5. Delegated (OBO) calls — the user's rights, not the stamp's app roles

`/me` (`Infrastructure/Graph/UserOperations.cs:32`); `GET /storage/fileStorage/containers/{id}/drive` (`UserOperations.cs:68`); `GET /shares/u!{token}/driveItem` (`DriveItemOperations.cs:1019`); Compose "Path B" drive item GET/content/versions/PUT (`DriveItemOperations.cs:405,480,563,654`, `UploadSessionManager.cs:68,442`); chat export/persist PUT (`UploadSessionManager.cs:68` via `:190`); container types / container-type registrations / owners (`SpeAdminGraphService.cs:4397,4456,4567,5143,5225,5278,5321,5484,5801,2091,2191,2278`, beta for owners); `/users/{upn}` lookup (`SpeAdminGraphService.cs:2232`, beta); `/me/sendMail` (`EmailChannelSender.cs:58`, `Services/Ai/Nodes/SendEmailNodeExecutor.cs:237`); `/me/mailFolders/sentitems/messages` (`EmailChannelSender.cs:210`); `/me/memberOf` (`Services/Ai/Security/PrivilegeGroupResolver.cs:174`); `/me/messages/{id}` (`Services/Office/OfficeEmailEnricher.cs:55,183`). The delegated scopes come from H3's `EntraAppRegPermissionCatalog`; a defect found there is §8 F6.

## 6. Roles Graph cannot narrow further — what else bounds them

| Role | Reach in Entra | What bounds it | Residual risk |
|---|---|---|---|
| `FileStorageContainer.Selected` (stamp) | containers of every container type that grants this app | Container-type application permission (H8 grants per stamp on the one Model 1 type); `SpeContainerOwnershipGuard` + customer marker refuse other customers' containers before any call (T227d, ArchTest `SpeAppOnlyContainerGuardTests`) | Microsoft has no per-container app scoping: a compromised stamp identity (not stamp code) reaches every Model 1 container (known, D28/D29). |
| `Mail.Read`, `Mail.ReadWrite`, `Mail.Send` (stamp) | none in Entra | Exchange RBAC for Applications, management scope = the customer's mail-enabled group (H14a); H13 T3 fails on any Entra mailbox grant | Exchange takes 30 min – 2 h to apply a change. |
| `AppRoleAssignment.ReadWrite.All` (L2) | grant any app role to any principal, itself included | Only the Worker can obtain the token; H10 targets only the stamp managed identity (appId = `miClientId`, type `ManagedIdentity`, checked before any removal) | Effectively tenant-admin-equivalent; Microsoft documents it as self-escalating. Audit `Add app role assignment to service principal` in the Entra audit log. |
| `User.Invite.All`, `GroupMember.ReadWrite.All`, `User.Create`, `LicenseAssignment.ReadWrite.All` (L2) | tenant-wide | H11 only acts on intake users and the intake environment group (`environmentSecurityGroupId`, PRQ-C-10) | No group-owner-scoped Graph permission exists. |
| `Mail.Read` (L2) | every mailbox in the tenant | Used only to create change subscriptions (H14b) | Could be Exchange-scoped like the stamp's; not changed here (§8 F3). |

## 7. Documented sets (machine-read by `StampGraphAppRoleEvidenceTests`)

Stamp identity — `GraphAppRoles.All` = `L2GraphAppRolesRegistry` (value | app-role id | granted through):

```t261-stamp-set
FileStorageContainer.Selected | 40dc41bc-0f7e-42ff-89bd-d9516947e474 | entra
Mail.Read | 810c84a8-4a9e-49e6-bf7d-12d183f40d01 | exchange
Mail.ReadWrite | e2a3a72e-5f79-4c64-b1b1-878b674786c9 | exchange
Mail.Send | b633e1c5-b582-4048-a93e-9f11b44c7e96 | exchange
```

L2 control-plane Worker — `ControlPlaneGraphAppRoles.All`:

```t261-control-plane-set
Application.ReadWrite.OwnedBy | 18a4783c-866b-4cc7-a460-3d5e5662c884
AppRoleAssignment.ReadWrite.All | 06b708a9-e830-4db3-a914-8e69da51d44f
Directory.Read.All | 7ab1d382-f21e-4acd-a863-ba3e13f7da61
User.Invite.All | 09850681-111b-4a89-9bed-3f2cae46d706
User.Create | 4240f680-4f73-4082-a766-aa916a2dc9b3
LicenseAssignment.ReadWrite.All | 5facf0c1-8979-4e95-abcf-ff3d079771c0
GroupMember.ReadWrite.All | dbaae8cf-10b5-4b86-a4a1-f871c94c6695
Mail.Read | 810c84a8-4a9e-49e6-bf7d-12d183f40d01
```

Every GUID was re-read live 2026-10-09 from the Microsoft Graph service principal's `appRoles` (tenant `a221a95e-…`).

### 7.2 Spaarke's own platform BFF identity (`PlatformBffGraphAppRoles.cs`)

The identity that runs the features a stamp does not (demo registration, SPE Admin security pages) — `mi-bff-api-{dev,demo}`. Its own catalog so a platform/demo rebuild cannot silently lose a role it calls with, and so a stamp can never be given these. Each role's call site and Learn URL are the §3 rows named in the table.

```t261-platform-set
FileStorageContainer.Selected | 40dc41bc-0f7e-42ff-89bd-d9516947e474
Mail.Read | 810c84a8-4a9e-49e6-bf7d-12d183f40d01
Mail.ReadWrite | e2a3a72e-5f79-4c64-b1b1-878b674786c9
Mail.Send | b633e1c5-b582-4048-a93e-9f11b44c7e96
User.Create | 4240f680-4f73-4082-a766-aa916a2dc9b3
User.Read.All | df021288-bdef-4463-88db-98f22de89214
LicenseAssignment.ReadWrite.All | 5facf0c1-8979-4e95-abcf-ff3d079771c0
User.EnableDisableAccount.All | 3011c876-62b7-4ada-afa2-506cbbecc68c
GroupMember.ReadWrite.All | dbaae8cf-10b5-4b86-a4a1-f871c94c6695
SecurityAlert.Read.All | 472e4a4d-bb4a-4026-98d1-0b0d74cb74a5
SecurityEvents.Read.All | bf394140-e372-4bf9-a898-299cfc7564e5
```

What the live identities hold versus what the code needs (read-only, 2026-10-09; Learn pages fetched the same day):

| Live role (`mi-bff-api-dev` unless noted) | Code that needs it | Verdict |
|---|---|---|
| `User.ReadWrite.All` | demo registration create / license / disable (§3.3) | broader than needed: Learn least = `User.Create` + `LicenseAssignment.ReadWrite.All` + `User.EnableDisableAccount.All` + `User.Read.All` (the catalog's set). Narrowing is an owner step. |
| `Group.ReadWrite.All` | demo group membership (§3.3) | broader than needed: `GroupMember.ReadWrite.All` (the catalog). Owner step. |
| `SecurityEvents.Read.All` only | alerts_v2 needs `SecurityAlert.Read.All`; secureScores needs `SecurityEvents.Read.All` ([security-list-alerts_v2](https://learn.microsoft.com/en-us/graph/api/security-list-alerts_v2?view=graph-rest-1.0), [security-list-securescores](https://learn.microsoft.com/en-us/graph/api/security-list-securescores?view=graph-rest-1.0)) | alerts_v2 403s today (7/8): the catalog adds `SecurityAlert.Read.All`. |
| `FileStorageContainerTypeReg.Selected` | none found: every container-type registration call is delegated (`SpeAdminGraphService.cs:5143,5321,5484` via `ForUserAsync`; the app-only `…ForConfigAsync` wrappers have no production caller, §3.5) | not in the catalog; held live, no app-only caller — owner may remove. |
| `Mail.Read`, `Mail.Send` (no `Mail.ReadWrite`) | §3.2 | catalog adds `Mail.ReadWrite`: the mark-as-read PATCH 403s today (F4). |

`mi-bff-api-demo` additionally holds `FileStorageContainer.Selected`; the same comparison applies. Nothing here is changed live.

### 7.1 Verdict per role that left the stamp catalog

| Role | Only callers | Verdict |
|---|---|---|
| `Directory.ReadWrite.All` | none on a stamp (registration needs narrower roles anyway) | drop — [permissions-reference](https://learn.microsoft.com/en-us/graph/permissions-reference) |
| `User.ReadWrite.All` | demo registration (§3.3), off on stamps | drop — [user-post-users](https://learn.microsoft.com/en-us/graph/api/user-post-users?view=graph-rest-1.0) |
| `GroupMember.ReadWrite.All` | demo registration (§3.3); H11 runs as the L2 Worker (§4) | drop — [group-post-members](https://learn.microsoft.com/en-us/graph/api/group-post-members?view=graph-rest-1.0) |
| `User.Invite.All` | H11 as the L2 Worker (§4), never the stamp | drop — [invitation-post](https://learn.microsoft.com/en-us/graph/api/invitation-post?view=graph-rest-1.0) |
| `User.Read.All`, `Group.Read.All` | demo registration only (§3.3) | drop — [user-list](https://learn.microsoft.com/en-us/graph/api/user-list?view=graph-rest-1.0) |
| `Files.Read.All`, `Files.ReadWrite.All`, `Sites.Read.All`, `Sites.ReadWrite.All` | none: every app-only file call is in an SPE container (§3.1); app-only SPE search is unsupported | drop — [configure-authentication-authorization](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/configure-authentication-authorization) ("SharePoint Embedded operations through Microsoft Graph require FileStorageContainer.Selected … Use application FileStorageContainer.Selected for app-only access") |
| `MailboxSettings.Read` | none | drop (§3.2) |

## 8. Findings (filed as ISS-018 sub-items unless fixed here)

- **F1 (fixed here)** — every stamp BFF would fail at start: `DemoExpirationService` (hosted) resolves `IOptions<DemoProvisioningOptions>.Value` in its constructor, and no stamp channel writes the `[Required]` `DemoProvisioning:*` keys. Fixed by `RegistrationModule.IsDemoProvisioningEnabled`.
- **F2 (route behaviour fixed here; rewrite owed)** — `POST /api/spe/search/items` (app-only `/search/query`, `SpeAdminGraphService.cs:7432`). Microsoft documents SPE content search as delegated-only; sdap-SPE-admin-app-r2 probe G did get hits from an app-only token holding `Files.ReadWrite.All`, but not shown to be SPE content. Decision (needed → build, else remove; least privilege wins): `Files.Read.All` is not kept. Without it Graph refuses and the route now returns a defined `501` ProblemDetails `spe-search-requires-delegated` instead of a raw 403 or an empty list. The OBO rewrite (or `$filter` enumeration) belongs to **sdap-SPE-admin-app-r2** (ISS-018 F2, #1543).
- **F3** — the L2 Worker identity lacks `AppRoleAssignment.ReadWrite.All` and `Application.ReadWrite.OwnedBy` live → H3 and H10 would 403 at T186. Operator action (owner OK): run `Grant-ControlPlaneIdentity.ps1` against `sprk-controlplane-dev-uami`; then remove the roles §4 lists as no longer needed.
- **F4** — dev (`mi-bff-api-dev`) lacks `Mail.ReadWrite`: inbound mark-as-read `PATCH /users/{mbx}/messages/{id}` returned 403 (3/3, 2026-10-06). Dev security pages: `GET /security/alerts_v2` 7/8 × 403 (holds `SecurityEvents.Read.All`, Learn least for alerts_v2 is `SecurityAlert.Read.All`).
- **F5** — `createLink` 403 app-only and delegated on dev: a sharing setting, not a Graph role. Not investigated further here.
- **F6** — H3's delegated catalog (`EntraAppRegPermissionCatalog.cs`) requests `Files.ReadWrite.All` with id `75359482-…`, which is the APPLICATION role id; the delegated scope id is `863451e7-0667-486c-a5d6-d135439485f0` (read live). It also lacks delegated `FileStorageContainer.Selected` (`085ca537-…`), which every OBO SPE call needs and the dev BFF registration carries. Risk: OBO SPE calls on a provisioned stamp fail at T186.

## 9. Live identities (read-only, 2026-10-09)

Holders of Microsoft Graph app roles in tenant `a221a95e-…` (`GET /servicePrincipals/{graph}/appRoleAssignedTo`):

| Principal | Graph app roles held | H10 target? | What H10 would remove if it reconciled it |
|---|---|---|---|
| `mi-bff-api-dev` (dev BFF UAMI, `9fd47efb-…`) | FileStorageContainer.Selected, FileStorageContainerTypeReg.Selected, Group.ReadWrite.All, Mail.Read, Mail.Send, SecurityEvents.Read.All, User.ReadWrite.All | **no** (hand-provisioned platform BFF; H10 runs only for provisioned stamps) | FileStorageContainerTypeReg.Selected, Group.ReadWrite.All, Mail.Read, Mail.Send, SecurityEvents.Read.All, User.ReadWrite.All |
| `mi-bff-api-demo` (`eaf9591e-…`) | FileStorageContainer.Selected, FileStorageContainerTypeReg.Selected, Group.ReadWrite.All, Mail.Read, Mail.Send, User.ReadWrite.All | no (demo is rebuilt as a stamp later, D27) | all but FileStorageContainer.Selected |
| `sprk-controlplane-dev-uami` (L2 Worker, `38f7693f-…`) | the old 15 | no — L2's own identity; H10 refuses any target whose appId ≠ the run's `miClientId` or that is not a managed identity | — (see §4 / F3) |
| `SDAP-BFF-SPE-API`, `Spaarke BFF API - Demo`, `SDAP-PCF-CLIENT`, `Spaarke SPE Model 1 Owner` | app registrations | no | — |
| any `mi-spaarke-{customerId}-{env}` stamp UAMI | **none exists yet** (no stamp provisioned) | yes, at its first H10 | nothing at first run (H10 granted only the new set) |

So on the live tenant today **H10 removes nothing**: no stamp identity exists. The first stamps (T186) are granted exactly `FileStorageContainer.Selected`.

## 10. What changed

- `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs` — the stamp set (4).
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/RegistrationModule.cs`, `EndpointMappingExtensions.cs` — demo registration gated.
- L2: `L2GraphAppRolesRegistry.cs`, `IGraphAppRolesRegistry.cs`, `IGraphAppRoleGranter.cs` (`RemoveUnexpectedRolesAsync`), `GraphRestAppRoleGranter.cs`, `IGraphAppRoleParityVerifier.cs` (`FindUnexpectedRolesAsync`), `GraphRestAppRoleParityVerifier.cs`, new `GraphAppRoleRest.cs` (shared REST, now paged), `H10DataverseAppUserGraphParityHandler.cs` (steps 12b/13b), `H10Rejections.cs` (3 codes), `E2EAcceptance/GraphAppRoleParityT3Probe.cs`, new `Handlers/ControlPlaneGraphAppRoles.cs`.
- Scripts: `Grant-GraphAppRoles.ps1`, `provisioning/Grant-ControlPlaneIdentity.ps1`, `provisioning-prereqs/prereqs.yaml` (PRQ-E-07, manifest 11).
- Tests: `StampGraphAppRoleEvidenceTests` (ArchTests), `RegistrationModuleGateTests` (BFF unit), `GraphRestAppRoleReconcileTests` + H10 / T3 / H14a / T4 test updates (ControlPlane), nightly parity test, `Prereqs-Recipes.Tests.ps1`.

## 10.1 Review fixes (verifier pass A, 2026-10-09)

- **Replication lag (F1).** H10 re-reads "nothing extra" up to 5 times with a growing delay (`ExtrasRecheckAttempts` / `ExtrasRecheckDelay`). Extras still listed after the call itself removed roles → Resumable `h10-graph-role-extras-unverified` (a healthy stamp is never quarantined for lag); extras the removal pass did not see → QuarantineRequired.
- **Stamps completed before T261 (F2).** The H10 idempotency key is now `appuser-{customerId}-g{fingerprint of the Entra role ids}`: any re-dispatch after a catalog change re-runs the (idempotent) reconcile. The reconciler does not re-dispatch a run already past H10, so for such a stamp use `scripts/provisioning/Remove-StampGraphExtraRoles.ps1` (dry run default, `-Apply` deletes; same target check as H10: appId = the stamp's client id, type ManagedIdentity). No stamp exists yet (§9), so this path is for the future only.
- **Gate on the full required set (F2)**, T3 probe text and an explicit `None`-only pass (F2), fail-closed REST: a 200 without a `value` array, a 51st page, or a `@odata.nextLink` off graph.microsoft.com stops the decision (K1); principal ids are URL-escaped.
- `Grant-GraphAppRoles.ps1` refuses an `mi-spaarke-*` identity unless `-AllowStampPrincipal` (K2).
- **K3 — the old L2 roles stay until ownership is proven.** Do NOT remove the Worker's old roles (`Directory.ReadWrite.All`, `User.ReadWrite.All`, …) until the live check "an app-only `POST /applications` under `Application.ReadWrite.OwnedBy` leaves the Worker as owner" (§11 step 4) has passed; removing first could leave H3 with no working path. Same rule in the POML.
- **K4 — app-only SPE search.** `POST /api/spe/search/items` cannot work with any app role (Learn: delegated only); T261 does not hide that: it is filed as ISS-018 F2 / #1543 for the SPE admin owner (sdap-SPE-admin-app-r2). Dropping `Files.Read.All` removes only the ability to search all of Spaarke's SharePoint, not a working feature (no call in 30 days of dev telemetry).

## 10.2 Review fixes (verifier pass B, 2026-10-09)

- Search route: defined 501 (§3.1, §8 F2). Stale "14/15/11-role" and `MailboxSettings.Read` text fixed in code, tests and the deployment guide (H14a no longer grants `MailboxSettings.Read`; an Exchange admin app that still carries a delegating assignment for it is harmless).
- `Grant-GraphAppRoles.ps1`: `-CatalogPath` is mandatory (no default); the stamp catalog is refused for a non-stamp principal, any other catalog for a stamp principal; platform roles in `PlatformBffGraphAppRoles.cs` (§7.2).
- `EveryRole_HasALearnCitedEvidenceRow` now needs the row in §3 or §4 (not §7.1), a `File.cs:NNN` call site and a `learn.microsoft.com` URL in the same row; the platform set is checked too.
- The `EntityLogicalName` workaround on `DataverseAppUserCreationRequest` is dropped: the work branch renamed the property to `SystemUserAzureActiveDirectoryObjectId`, which the contact guard recognises.

## 10.3 Component justification (root CLAUDE.md §11) for the new surface

| New surface | 1. Existing overlap | 2. Extend instead? | 3. Cost of doing nothing |
|---|---|---|---|
| `ControlPlaneGraphAppRoles.cs` (L2 catalog) | `GraphAppRoles.cs` (BFF stamp set) | No: it was the L2 Worker's list by accident; the two identities need different roles, and L2 cannot reference the BFF (ADR-010). | Shrinking the stamp set would strip H11's `User.Invite.All`; H3/H10 would still 403 for want of `AppRoleAssignment.ReadWrite.All` (F3). |
| `PlatformBffGraphAppRoles.cs` | `GraphAppRoles.cs`, live grants | No: the platform BFF's roles (demo, security pages) must not be in the stamp set, and nothing recorded them. | A demo/platform rebuild loses `User.*`/`Group.*`/`Security*` grants silently (no record exists) and the pages 403. |
| `GraphAppRoleRest.cs`, `RemoveUnexpectedRolesAsync`, `FindUnexpectedRolesAsync`, 3 H10 rejection codes | `GraphRestAppRoleGranter` / `…ParityVerifier` each had a private copy of the REST code | Extended: the two classes now share one file; the new methods live on the existing seams. | Without removal a stamp keeps the roles of an older catalog (G31 unfixed); without the independent re-read H13 T3 cannot prove it. |
| `Remove-StampGraphExtraRoles.ps1` | H10's removal | Same logic as an operator script: the reconciler never re-dispatches H10 for a run already past it. | A stamp provisioned before T261 keeps tenant-wide roles with no remedy short of hand deletes. |
| `RegistrationModule.IsDemoProvisioningEnabled` gate; `SearchItemsEndpoints.NotSupportedForIdentity` | `OnboardingModule.IsEnabled` pattern; `ex.ToProblemDetails` | Extended an existing module / endpoint file, no new route. | Directory-write services registered on every stamp; stamp BFF fails at start (F1); the search route returns a raw 403. |
| `StampGraphAppRoleEvidenceTests` | `SpeAppOnlyContainerGuardTests` pattern | New test, same shape. | A role is added to the catalog (granted to every stamp) with no evidence. |

## 11. Live steps owed (each needs the owner's OK)

1. `Grant-ControlPlaneIdentity.ps1` for `sprk-controlplane-dev-uami` (adds 5 roles incl. `AppRoleAssignment.ReadWrite.All`) — before T186 (F3).
2. Optional: remove from the L2 Worker the roles §4 lists as not needed.
3. At T186: confirm the stamp identity holds exactly `FileStorageContainer.Selected` in Entra, and that compose drive subscriptions, upload sessions and delta work on the stamp (they work on dev with no Files.*/Sites.* role).
4. Verify an app-only `POST /applications` under `Application.ReadWrite.OwnedBy` leaves the Worker as owner (H3's later PATCH/FIC calls depend on it).
