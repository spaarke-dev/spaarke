# Live batch A — read-only tests (az / gh)

> Captured 2026-10-09T18:17Z by batch-a-readonly-tests.sh. Read-only; no secret values printed.

## Context
{
  "subscription": "Spaarke Devlopment Environment",
  "tenant": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "user": "ralph.schroeder@spaarke.com"
}

## T-P2-09 — L2 control plane

### 1. App exposing api://spaarke.com/provisioning-controlplane-dev
[
  {
    "appId": "70ba7b19-8969-47e5-a508-efe621dea1a4",
    "name": "spaarke-provisioning-controlplane-dev",
    "roles": [
      "Reader",
      "Operator"
    ]
  }
]
### 2. Who holds its app roles (appRoleAssignedTo)
Principal        AppRoleId
---------------  ------------------------------------
Ralph Schroeder  6433ca3e-739d-4d72-81c8-5feb4c4fe73b
{
  "appRoleAssignmentRequired": false
}
### 3. L2 hosts — settings (names + shape) and /healthz
### 4. Per-customer stamp apps and App Services
ERROR: Invalid jmespath query supplied for `--query`: In function length(), invalid type for value: None, expected one of: ['string', 'array', 'object'], received: "null"
To learn more about --query, please visit: 'https://learn.microsoft.com/cli/azure/query-azure-cli'



## T-P2-10 — delegated consent grants and production SPA site (Spaarke tenant)

### grants by dev add-in (c1258e2d-1688-49d2-ac99-a7485ebd9995)
- resource Microsoft Graph: scope `email profile User.Read`, consentType AllPrincipals
- resource SDAP-BFF-SPE-API: scope `SDAP.Access user_impersonation`, consentType AllPrincipals
### grants by PCF client (170c98e1-d486-4355-bcbe-170454e0207c)
- resource Microsoft Graph: scope `Files.ReadWrite.All Files.ReadWrite.AppFolder FileStorageContainer.Manage.All FileStorageContainer.Selected FileStorageContainerType.Manage.All openid profile offline_access`, consentType AllPrincipals
- resource Office 365 SharePoint Online: scope `Container.Selected`, consentType AllPrincipals
- resource SDAP-BFF-SPE-API: scope `user_impersonation`, consentType AllPrincipals
- resource Spaarke BFF API - Demo: scope `user_impersonation`, consentType AllPrincipals
### grants by spaarke-external-access-SPA (f306885a-8251-492c-8d3e-34d7b476ffd0)
- resource Microsoft Graph: scope `User.Read`, consentType AllPrincipals
- resource SDAP-BFF-SPE-API: scope `access_as_external_user SDAP.Access user_impersonation`, consentType AllPrincipals
### grants by prod add-in (1958aec2-0218-495e-8e3c-37133e9b8357)
- resource Microsoft Graph: scope `email profile User.Read`, consentType AllPrincipals
### dev add-in pre-authorization on 1e40baad: delegated permission ids, and the scope id map
[
  [
    "691ef488-3f34-42d0-a8f6-e71eac633778",
    "18afc847-a660-40b2-8925-747aed613f36"
  ]
]
V
-----------------------
access_as_user
access_as_external_user
SDAP.Access
user_impersonation
### production SPA Static Web App
[
  {
    "customDomains": [
      "external.spaarke.com"
    ],
    "defaultHostname": "orange-stone-09288801e.3.azurestaticapps.net",
    "name": "swa-spaarke-external-spa-prod",
    "rg": "rg-spaarke-shared-prod"
  }
]

## T-P3-01 / T-P3-18 — dev BFF settings (names + shape), production and staging

- deployment slots on spaarke-bff-dev: 

#### spaarke-bff-dev production

- 211 settings in slot; 93 match the filter

| setting | shape / value |
|---|---|
| `AgentToken__AgentAppId` | GUID `f257a0a9-1061-4f9b-8918-3ad056fe90db` |
| `AgentToken__CacheTtlMinutes` | other, length 2 (value not shown) |
| `AgentToken__ClientId` | GUID `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `AgentToken__CopilotAudience` | other, length 84 (value not shown) |
| `AgentToken__DataverseEnvironmentUrl` | URL `https://spaarkedev1.crm.dynamics.com` |
| `AgentToken__TenantId` | GUID `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `AiSearch__AllowedIndexes__0` | other, length 19 (value not shown) |
| `AiSearch__AllowedIndexes__1` | other, length 23 (value not shown) |
| `AiSearch__AllowedIndexes__2` | other, length 21 (value not shown) |
| `AiSearch__AllowedIndexes__3` | other, length 22 (value not shown) |
| `AiSearch__AllowedIndexes__4` | other, length 22 (value not shown) |
| `AiSearch__AllowedIndexes__5` | other, length 21 (value not shown) |
| `AiSearch__AllowedIndexes__6` | other, length 22 (value not shown) |
| `AiSearch__AllowedIndexes__7` | other, length 27 (value not shown) |
| `AiSearch__DiscoveryIndexName` | other, length 23 (value not shown) |
| `AiSearch__Endpoint` | Key Vault reference (secret name `ai-search-endpoint`) |
| `AiSearch__KnowledgeIndexName` | other, length 19 (value not shown) |
| `AiSearch__ManagedIdentity__Enabled` | bool `true` |
| `AiSearch__ReferencesEndpoint` | Key Vault reference (secret name `ai-search-endpoint`) |
| `AiSearch__SemanticConfigName` | other, length 15 (value not shown) |
| `AzureAd__Audience` | other, length 42 (value not shown) |
| `AzureAd__ClientId` | GUID `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `AzureAd__Instance` | URL `https://login.microsoftonline.com/` |
| `AzureAd__TenantId` | GUID `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `AzureAd__ValidAudiences__0` | other, length 42 (value not shown) |
| `AzureAd__ValidAudiences__1` | GUID (value not shown) |
| `AzureAd__ValidAudiences__2` | other, length 85 (value not shown) |
| `Ciam__Audience` | GUID (value not shown) |
| `Ciam__ClientId` | GUID `4a4d5126-91b0-4865-8e3a-134b7209013e` |
| `Ciam__Domain` | other, length 28 (value not shown) |
| `Ciam__GraphProvisioner__CertificateName` | other, length 27 (value not shown) |
| `Ciam__GraphProvisioner__ClientId` | GUID `e63e6eb1-be25-4214-80a8-a6d609034bb9` |
| `Ciam__Instance` | URL `https://spaarkeextid.ciamlogin.com` |
| `Ciam__TenantId` | GUID `7052feba-bfc4-43e0-b09e-65014b429131` |
| `Communication__Acs__Endpoint` | URL `https://spaarke-acs-dev.unitedstates.communication.azure.com` |
| `Communication__AiClassification__Enabled` | bool `true` |
| `Communication__ApprovedSenders__0__DisplayName` | other, length 15 (value not shown) |
| `Communication__ApprovedSenders__0__Email` | other, length 27 (value not shown) |
| `Communication__ApprovedSenders__0__IsDefault` | bool `true` |
| `Communication__ApprovedSenders__1__DisplayName` | other, length 12 (value not shown) |
| `Communication__ApprovedSenders__1__Email` | other, length 21 (value not shown) |
| `Communication__ApprovedSenders__1__IsDefault` | bool `false` |
| `Communication__ArchiveContainerId` | other, length 66 (value not shown) |
| `Communication__AutoFile__Enabled` | bool `true` |
| `Communication__AutoFile__Threshold` | other, length 4 (value not shown) |
| `Communication__DefaultMailbox` | other, length 27 (value not shown) |
| `Communication__OwnershipHoldAlertUserIds__0` | GUID (value not shown) |
| `Communication__SemanticMatch__Enabled` | bool `true` |
| `Communication__TrackingFooter__Enabled` | bool `true` |
| `Communication__TrackingFooter__SigningKeySecretName` | other, length 39 (value not shown) |
| `Communication__WebhookClientState` | Key Vault reference (secret name `Communication-WebhookClientState`) |
| `Communication__WebhookNotificationUrl` | URL `https://spaarke-bff-dev.azurewebsites.net/api/communications/incoming-webhook` |
| `Communication__WebhookSigningKey` | Key Vault reference (secret name `Communication-WebhookSigningKey`) |
| `Cors__AllowedOrigins__0` | URL `https://spaarkedev1.crm.dynamics.com` |
| `Cors__AllowedOrigins__1` | URL `https://spaarkedev1.api.crm.dynamics.com` |
| `Cors__AllowedOrigins__2` | URL `https://spaarke.powerappsportals.com` |
| `Cors__AllowedOrigins__3` | URL `https://icy-desert-0bfdbb61e.6.azurestaticapps.net` |
| `Cors__AllowedOrigins__4` | URL `https://green-dune-0c4f1221e.7.azurestaticapps.net` |
| `Customer__Id` | other, length 7 (value not shown) |
| `Dataverse__ClientId` | GUID `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `Dataverse__EnvironmentUrl` | URL `https://spaarkedev1.crm.dynamics.com` |
| `Dataverse__ServiceUrl` | URL `https://spaarkedev1.crm.dynamics.com` |
| `Dataverse__TenantId` | GUID `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `ExternalAccess__PortalUrl` | URL `https://green-dune-0c4f1221e.7.azurestaticapps.net` |
| `ExternalAccess__Reconciliation__WritesEnabled` | bool `true` |
| `Graph__ClientId` | GUID `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `Graph__Credentials__Order__0` | other, length 24 (value not shown) |
| `Graph__Credentials__RequireSecretFreeIdentity` | bool `true` |
| `Graph__ManagedIdentity__ClientId` | GUID `5967251e-171c-46fe-a6c2-ef843c90309d` |
| `Graph__ManagedIdentity__Enabled` | bool `true` |
| `Graph__Scopes__0` | URL `https://graph.microsoft.com/.default` |
| `Graph__TenantId` | GUID `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `IdentityLink__Reconciliation__WritesEnabled` | bool `true` |
| `ManagedIdentity__ClientId` | GUID `5967251e-171c-46fe-a6c2-ef843c90309d` |
| `Notifications__SignalR__ConnectionString` | other, length 164 (value not shown) |
| `Notifications__Suggestions__Enabled` | bool `true` |
| `Onboarding__EnableDevBypass` | bool `true` |
| `PowerBi__ClientId` | GUID `67fea25a-a47c-4838-ba1c-a310550ceb5a` |
| `PowerBi__ClientSecret` | other, length 40 (value not shown) |
| `PowerBi__TenantId` | GUID `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `PublicConfig__BffUrl` | URL `https://spaarke-bff-dev.azurewebsites.net` |
| `PublicConfig__MsalClientId` | GUID `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `PublicConfig__TenantId` | GUID `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `Rag__ApiKey` | other, length 44 (value not shown) |
| `Redis__AllowInMemoryFallback` | bool `false` |
| `Redis__Enabled` | bool `true` |
| `Redis__Endpoint` | other, length 51 (value not shown) |
| `Redis__InstanceName` | other, length 8 (value not shown) |
| `Reporting__ModuleEnabled` | bool `true` |
| `ServiceBus__FullyQualifiedNamespace` | other, length 45 (value not shown) |
| `ServiceBus__OfficeQueueName` | other, length 11 (value not shown) |
| `ServiceBus__QueueName` | other, length 9 (value not shown) |
| `WorkforceIdentity__CustomerTenantIds__0` | GUID `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
ERROR: (ResourceGroupNotFound) Resource group 'rg-spaarke-demo' could not be found.
Code: ResourceGroupNotFound
Message: Resource group 'rg-spaarke-demo' could not be found.

## T-P3-07 — permissions, RBAC, federated-credential audiences


### requiredResourceAccess resolved — 1e40baad-e065-4aea-a8d4-4b7ab273458c

- Microsoft Graph: AppRoleAssignment.ReadWrite.All (app), Directory.ReadWrite.All (app), Directory.ReadWrite.All (delegated), FileStorageContainer.Manage.All (delegated), FileStorageContainer.Selected (app), FileStorageContainer.Selected (delegated), FileStorageContainerType.Manage.All (delegated), FileStorageContainerTypeReg.Manage.All (delegated), FileStorageContainerTypeReg.Selected (app), Files.Read.All (delegated), Files.ReadWrite.All (app), Files.ReadWrite.All (delegated), Files.SelectedOperations.Selected (app), Group.ReadWrite.All (app), GroupMember.ReadWrite.All (delegated), Mail.Read (app), Mail.Read (delegated), Mail.Send (app), Mail.Send (delegated), Sites.FullControl.All (delegated), Sites.Read.All (delegated), Sites.ReadWrite.All (delegated), User.Read (delegated), User.ReadWrite.All (app), User.ReadWrite.All (delegated)
- 00000007-0000-0000-c000-000000000000: 1 permission(s) (ids: ['78ce3f0f-a1ce-49c2-8cde-64b5c0896db4'])
- 00000003-0000-0ff1-ce00-000000000000: 2 permission(s) (ids: ['19766c1b-905b-43af-8756-06526ab42875', '4d114b1a-3649-4764-9dfb-be1e236ff371'])

### requiredResourceAccess resolved — 170c98e1-d486-4355-bcbe-170454e0207c

- Microsoft Graph: FileStorageContainer.Manage.All (delegated), FileStorageContainer.Selected (app), FileStorageContainer.Selected (delegated), FileStorageContainerType.Manage.All (delegated), FileStorageContainerTypeReg.Selected (app), Files.ReadWrite.All (app), Files.ReadWrite.All (delegated), Files.ReadWrite.AppFolder (app), Files.ReadWrite.AppFolder (delegated), Files.SelectedOperations.Selected (app), SecurityEvents.Read.All (app)
- 00000003-0000-0ff1-ce00-000000000000: 2 permission(s) (ids: ['19766c1b-905b-43af-8756-06526ab42875', '4d114b1a-3649-4764-9dfb-be1e236ff371'])
- 1e40baad-e065-4aea-a8d4-4b7ab273458c: 1 permission(s) (ids: ['18afc847-a660-40b2-8925-747aed613f36'])

### Azure RBAC held by mi-bff-api-dev (principal 9fd47efb…)
Role                                   Scope
-------------------------------------  --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
Cognitive Services OpenAI User         /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.CognitiveServices/accounts/spaarke-openai-dev
Cognitive Services User                /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.CognitiveServices/accounts/spaarke-openai-dev
Key Vault Secrets User                 /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.KeyVault/vaults/spaarke-spekvcert
Azure Service Bus Data Sender          /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourcegroups/SharePointEmbedded/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev/topics/sprk-membership-changes
Azure Service Bus Data Receiver        /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourcegroups/SharePointEmbedded/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev/topics/sprk-membership-changes/subscriptions/recon-junction-updater
Search Index Data Contributor          /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.Search/searchServices/spaarke-search-dev
Communication and Email Service Owner  /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/rg-spaarke-dev/providers/Microsoft.Communication/CommunicationServices/spaarke-acs-dev
Azure Service Bus Data Sender          /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev
Azure Service Bus Data Receiver        /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev

### Federated credential audiences
- 1e40baad-e065-4aea-a8d4-4b7ab273458c:
[
  {
    "audiences": [
      "api://AzureADTokenExchange"
    ],
    "name": "mi-bff-api-dev-assertion"
  },
  {
    "audiences": [
      "api://AzureADTokenExchange"
    ],
    "name": "github-actions-deploy-staging"
  }
]
- 46670ee2-ac0c-44b0-9ac2-d40ae4dcbdd7:
[
  {
    "audiences": [
      "api://AzureADTokenExchange"
    ],
    "name": "sprk-controlplane-dev-uami-assertion"
  }
]
- bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e:
[
  {
    "audiences": [
      "api://AzureADTokenExchange"
    ],
    "name": "sprk-controlplane-dev-uami-assertion"
  }
]
- 8c85a481-f3a0-46de-b84e-3ede8a4d60c3:
[
  {
    "audiences": [
      "api://AzureADTokenExchange"
    ],
    "name": "gh-ref-refs-heads-master"
  },
  {
    "audiences": [
      "api://AzureADTokenExchange"
    ],
    "name": "spaarke-dev-deploy-promote-production"
  },
  {
    "audiences": [
      "api://AzureADTokenExchange"
    ],
    "name": "spaarke-dev-deploy-promote-staging"
  },
  {
    "audiences": [
      "api://AzureADTokenExchange"
    ],
    "name": "spaarke-dev-deploy-promote-dev"
  }
]

### Graph application roles held vs referenced in code (grep of GraphAppRoles.cs)
"Directory.ReadWrite.All" "Files.Read.All" "Files.ReadWrite.All" "FileStorageContainer.Selected" "Group.Read.All" "GroupMember.ReadWrite.All" "Mail.Read" "Mail.ReadWrite" "Mail.Send" "MailboxSettings.Read" "Sites.Read.All" "Sites.ReadWrite.All" "User.Invite.All" "User.Read.All" "User.ReadWrite.All" 

## T-P3-09 — credential metadata (no values, no hints)

### dev BFF (1e40baad-e065-4aea-a8d4-4b7ab273458c)
- secrets:
Name                         KeyId                                 Start                 End
---------------------------  ------------------------------------  --------------------  --------------------
Dataverse-Checkout-20251218  5938c8a2-2ab5-420c-b7a1-706a2decf131  2025-12-19T03:56:35Z  2027-12-19T03:56:35Z
- certificates:

### PCF client / SPE owning app (170c98e1-d486-4355-bcbe-170454e0207c)
- secrets:
Name                                  KeyId                                 Start                     End
------------------------------------  ------------------------------------  ------------------------  ------------------------
spe-owning-app-secret (SPE Admin R2)  415035ac-a13d-415b-8f73-1927677fa706  2026-08-24T15:01:56Z      2028-08-24T15:01:56Z
SPE Dev 2 Functions Secret            40fcc0c4-4d60-4526-b303-be592f11314e  2025-09-22T17:07:12.861Z  2027-09-22T17:07:12.861Z
- certificates:
Name                            KeyId                                 End
------------------------------  ------------------------------------  --------------------
CN=SDAP-SPE-Owner-Renewed-2026  7db7370c-8f99-4aa5-9d92-20babd8929a4  2027-07-19T21:15:54Z
### GitHub OIDC (8c85a481-f3a0-46de-b84e-3ede8a4d60c3)
- secrets:
Name    KeyId                                 Start                 End
------  ------------------------------------  --------------------  --------------------
rbac    79d3d118-bf33-4c10-90f4-00f3e8e046c4  2025-09-24T14:45:36Z  2026-09-24T14:45:36Z
- certificates:

### demo BFF (da03fe1a-4b1d-4297-a4ce-4b83cae498a9)
- secrets:
Name                      KeyId                                 Start                     End
------------------------  ------------------------------------  ------------------------  ------------------------
Demo BFF API Secret v2    b84820e1-44f3-4915-b18a-26f5ff177a04  2026-03-26T03:37:49Z      2028-03-26T03:37:49Z
SharePointEmbeddedVSCode  361ec674-8d48-4953-9f6f-0e94f3c88a0b  2026-03-25T20:25:23.405Z  2026-05-24T20:25:23.405Z
- certificates:

### ciam-graph-provisioner-cert exportable?
{
  "expires": "2028-07-20T02:18:42+00:00",
  "exportable": true
}

## T-P3-12 — Insights Function shell

{
  "identity": "UserAssigned",
  "state": null,
  "uamis": {
    "/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourcegroups/spe-infrastructure-westus2/providers/Microsoft.ManagedIdentity/userAssignedIdentities/insights-spaarkedev-uami": {
      "clientId": "583ca2d8-d6c8-4aa2-adaa-ad696f6c4784",
      "principalId": "9e4261c7-9d3e-4c30-908f-031c5f9ff136"
    }
  }
}
{
  "allowSharedKeyAccess": true,
  "publicNetworkAccess": "Enabled"
}
### RBAC held by insights-spaarkedev-uami (principal 9e4261c7…)
Role                     Scope
-----------------------  ---------------------------------------------------------------------------------------------------------------------------------------------------------------
Key Vault Secrets User   /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourcegroups/spe-infrastructure-westus2/providers/Microsoft.KeyVault/vaults/sprkspaarkedev-aif-kv
Storage Blob Data Owner  /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourcegroups/spe-infrastructure-westus2/providers/Microsoft.Storage/storageAccounts/insightsspaarkedevstg
### RBAC held by insights-search-deploy-uami (principal 42ffe13e…)


#### insights-spaarkedev-func settings

- 3 settings in slot; 3 match the filter

| setting | shape / value |
|---|---|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | other, length 240 (value not shown) |
| `AZURE_CLIENT_ID` | GUID (value not shown) |
| `AzureWebJobsStorage` | other, length 196 (value not shown) |

## T-P3-16 — GitHub Actions secret and variable NAMES (values are write-only)

AZURE_CLIENT_ID	2026-06-02T01:03:29Z
AZURE_STATIC_WEB_APPS_API_TOKEN	2026-01-21T18:13:35Z
AZURE_SUBSCRIPTION_ID	2025-12-31T02:44:29Z
AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV	2026-07-19T22:23:01Z
AZURE_TENANT_ID	2025-12-31T02:44:27Z
CRITICAL_ALERTS_EMAIL	2026-03-14T02:08:40Z
DEV_APP_NAME	2026-06-05T02:52:38Z
GH_TOKEN_PROJECT	2025-10-01T15:51:06Z
PERFORMANCE_ALERTS_EMAIL	2026-03-14T02:08:40Z
PROD_APP_NAME	2026-06-02T01:25:07Z
STAGING_APP_NAME	2025-12-31T02:44:31Z

PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT	sprkcpartifactsdev	2026-08-21T15:53:46Z
SIDECAR_ACR_LOGIN_SERVER	sprkcontrolplanedevacr.azurecr.io	2026-08-21T15:53:45Z

copilot
dev
prod
production
staging

## T-P1-12 step 4 — App Insights counts, last 7 days

candidates: [
  {
    "appId": "84cc1590-29e2-4b05-abb4-f103a7fc31c6",
    "n": "sprkspaarkedev-aif-insights",
    "rg": "spe-infrastructure-westus2"
  }
]
- `RPA-FALLBACK`: 0
- `[WF-STANDING]`: 0
- `[EFFECTIVE-ACCESS]`: 0
- `Deny veto`: 0

---

## Findings from batch A (reviewed 2026-10-09)

Verdicts against `live/live-test-plan.md`. Every value printed above is an identifier, a URL or a boolean; secrets were masked by shape.

| Test | Result | Finding |
|---|---|---|
| T-P2-09 step 1–2 | **Answered** | The control-plane API audience `api://spaarke.com/provisioning-controlplane-dev` belongs to app **`70ba7b19-8969-47e5-a508-efe621dea1a4` "spaarke-provisioning-controlplane-dev"** (roles Reader, Operator). Only one principal holds a role (an operator user). `appRoleAssignmentRequired` is false, so any user in the tenant can obtain a token; access then depends on the role check in the API. Settles x03 B7. |
| T-P2-09 step 3–4 | **Not run (inconclusive)** | No `sprk-controlplane-dev*` App Service in the current subscription (the hosts may be in another subscription), and the stamp-app query had a JMESPath error. Re-run with the subscription named. |
| T-P2-10 | **Answered** | Admin-consented delegated grants (AllPrincipals): the **dev add-in** has `SDAP.Access user_impersonation` on the dev BFF and `email profile User.Read` on Graph. The **PCF client** has broad Graph delegated scopes (`Files.ReadWrite.All`, `FileStorageContainer.Manage.All`, `FileStorageContainerType.Manage.All` and more), SharePoint `Container.Selected`, and `user_impersonation` on both the dev and demo BFF apps. **`spaarke-external-access-SPA`** has `access_as_external_user SDAP.Access user_impersonation` on the dev BFF. The **production add-in** has Graph only (no BFF grant; pre-authorization on customer BFFs replaces consent). The dev add-in is pre-authorized for two BFF scopes (ids `691ef488…`, `18afc847…`). The **production SPA site exists**: `swa-spaarke-external-spa-prod`, custom domain `external.spaarke.com`, default host `orange-stone-09288801e.3.azurestaticapps.net`. |
| T-P3-18 | **Settled** | **`spaarke-bff-dev` has no deployment slots.** x04 could not read a staging slot because none exists. (A GitHub federated credential for a "staging" environment exists on `1e40baad`; that targets a separate app, not a slot.) |
| T-P3-01 | **Answered, with findings** | **Secrets stored as plain app settings rather than Key Vault references** (values not printed): `PowerBi__ClientSecret` (40 chars), `Rag__ApiKey` (44), `Notifications__SignalR__ConnectionString` (164, contains an access key), plus `Compose__Webhook__ClientState` (x04). This conflicts with CLAUDE.md §9 and ADR-028's secret-free posture. `Onboarding__EnableDevBypass = true` on dev. `AgentToken__AgentAppId = f257a0a9…` (Copilot bot; settles C1 residual). Service Bus uses a namespace with managed identity (no SAS). The webhook signing key references Key Vault secret **`Communication-WebhookSigningKey`**, which is not the name provisioning writes on stamps (x03 H-3). Redis has no in-memory fallback. `Graph__Credentials__RequireSecretFreeIdentity = true`. The demo resource group `rg-spaarke-demo` no longer exists. |
| T-P3-07 | **Answered** | **Requested permissions on the dev BFF app** include `AppRoleAssignment.ReadWrite.All`, `Directory.ReadWrite.All`, `User.ReadWrite.All`, `Group.ReadWrite.All`, `Files.ReadWrite.All`, `Mail.Read`/`Mail.Send` (application) and `Sites.FullControl.All` (delegated). The PCF client requests `SecurityEvents.Read.All` (application), which explains the role the BFF managed identity holds without any code consumer. **Azure RBAC held by `mi-bff-api-dev`**: OpenAI User, Cognitive Services User, Key Vault Secrets User, Service Bus Data Sender/Receiver, Search Index Data Contributor, and **Communication and Email Service Owner** on ACS (the template grants none; Owner is broad). Every federated credential audience is `api://AzureADTokenExchange`. Graph roles referenced by `GraphAppRoles.cs` (15) vs held by the BFF identity (7): see x04 §6. |
| T-P3-09 | **Answered, with findings** | **The dev BFF app holds one live secret `Dataverse-Checkout-20251218`**, valid until 2027-12-19. Its Key Vault copies are deleted and no app setting references it, so it has no known consumer: a removal candidate (x03 F-1). The **PCF client / SPE owning app** has `spe-owning-app-secret` (E-1, valid to 2028-08), `SPE Dev 2 Functions Secret` (consumer unknown, valid to 2027-09) and certificate `CN=SDAP-SPE-Owner-Renewed-2026` (to 2027-07). The **GitHub OIDC app** has an **expired** secret `rbac` (ended 2026-09-24); OIDC uses federated credentials, so the secret is stale. The **demo BFF app** has two secrets, one **expired** 2026-05-24. **`ciam-graph-provisioner-cert` is exportable** (`exportable: true`, expires 2028-07-20). |
| T-P3-12 | **Answered** | The Insights Function uses user-assigned identity `insights-spaarkedev-uami`. Its storage account `insightsspaarkedevstg` has **`allowSharedKeyAccess: true`** and public network access enabled. `AzureWebJobsStorage` is a 196-character plain value, consistent with an account-key connection string. The identity holds Storage Blob Data Owner and Key Vault Secrets User (`sprkspaarkedev-aif-kv`). `insights-search-deploy-uami` holds no roles. |
| T-P3-16 | **Answered** | GitHub secret names: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_STATIC_WEB_APPS_API_TOKEN`, `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV`, `GH_TOKEN_PROJECT`, `DEV_APP_NAME`, `STAGING_APP_NAME`, `PROD_APP_NAME`, two alert e-mails. Variables: provisioning artifacts storage and sidecar ACR. Environments: copilot, dev, prod, production, staging. |
| T-P1-12 step 4 | **Inconclusive** | The App Insights component the query found (`sprkspaarkedev-aif-insights`) is probably not the BFF's own telemetry, so its counts of 0 prove nothing. Re-run against the component named in the BFF's `APPLICATIONINSIGHTS_CONNECTION_STRING`. |
