# Live Entra / Azure read-out (read-only)

> Captured 2026-10-09T15:26Z by entra-readonly-checks.sh. Read-only; secret values masked.

## 0. Signed-in context
{
  "subscription": "Spaarke Devlopment Environment",
  "tenant": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "user": "ralph.schroeder@spaarke.com"
}

## 1. App registrations (Spaarke workforce tenant)

### A1 `1e40baad-e065-4aea-a8d4-4b7ab273458c` — SDAP-BFF-SPE-API (dev BFF; also Teams/Copilot client)

- displayName: SDAP-BFF-SPE-API
- signInAudience: **AzureADMultipleOrgs**
- identifierUris: ['api://green-dune-0c4f1221e.7.azurestaticapps.net/1e40baad-e065-4aea-a8d4-4b7ab273458c', 'api://auth-3e04ab58-8450-44d6-b95b-daca16b6cbdb/1e40baad-e065-4aea-a8d4-4b7ab273458c', 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c']
- spa redirects: ['brk-multihub://green-dune-0c4f1221e.7.azurestaticapps.net', 'brk-1fec8e78-bce4-4aaf-ab1b-5451cc387264://green-dune-0c4f1221e.7.azurestaticapps.net', 'brk-5e3ce6c0-2b1f-4285-8d4b-75ee78787346://green-dune-0c4f1221e.7.azurestaticapps.net', 'https://green-dune-0c4f1221e.7.azurestaticapps.net', 'https://spaarkedev1.crm.dynamics.com', 'https://spaarkedev1.crm.dynamics.com/webresources/sprk_spaarkeai']
- web redirects: ['https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect', 'https://teams.microsoft.com/api/platform/v1.0/oAuthConsentRedirect', 'https://oauth.pstmn.io/v1/browser-callback', 'https://oauth.pstmn.io/v1/callback']
- publicClient redirects: []
- exposed scopes: [('access_as_user', True), ('access_as_external_user', True), ('SDAP.Access', True), ('user_impersonation', True)]
- preAuthorizedApplications: ['29d9ed98-a469-4536-ade2-f981bc1d605e', '5e3ce6c0-2b1f-4285-8d4b-75ee78787346', '1fec8e78-bce4-4aaf-ab1b-5451cc387264', '170c98e1-d486-4355-bcbe-170454e0207c', 'c1258e2d-1688-49d2-ac99-a7485ebd9995', '04b07795-8ddb-461a-bbee-02f9e1bf7b46', 'f306885a-8251-492c-8d3e-34d7b476ffd0', 'ab3be6b7-f5df-413d-ac2d-abf1e3fd9c0b', 'f257a0a9-1061-4f9b-8918-3ad056fe90db']
- knownClientApplications: ['170c98e1-d486-4355-bcbe-170454e0207c']
- appRoles: ['Admin']
- optionalClaims.accessToken: ['email', 'preferred_username', 'upn', 'acct']; idToken: []
- groupMembershipClaims: None
- requiredResourceAccess (resource -> count): [('00000003-0000-0000-c000-000000000000', 25), ('00000007-0000-0000-c000-000000000000', 1), ('00000003-0000-0ff1-ce00-000000000000', 2)]
- passwordCredentials: 1 secret(s) (names/values not shown); keyCredentials: 0 cert(s)
- service principal: {'appRoleAssignmentRequired': False, 'ownerTenant': 'a221a95e-6abc-4434-aecc-e48338a1b2f2', 'spObjectId': 'd93c832e-9b1d-4ccc-a2a8-9419fbf3fc18', 'type': 'Application'}
- federated credentials: [{'issuer': 'https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0', 'name': 'mi-bff-api-dev-assertion', 'subject': '9fd47efb-7962-492b-ac44-e5ccd0268ebb'}, {'issuer': 'https://token.actions.githubusercontent.com', 'name': 'github-actions-deploy-staging', 'subject': 'repo:spaarke-dev/spaarke:environment:staging'}]

### A2 `170c98e1-d486-4355-bcbe-170454e0207c` — SDAP-PCF-CLIENT (dev code pages/PCF; SPE owning app)

- displayName: SDAP-PCF-CLIENT
- signInAudience: **AzureADMultipleOrgs**
- identifierUris: []
- spa redirects: ['https://spaarke-demo.api.crm.dynamics.com', 'https://spaarke-demo.crm.dynamics.com', 'https://spaarkedev1.api.crm.dynamics.com', 'http://localhost:8181', 'https://spaarkedev1.crm.dynamics.com', 'http://localhost']
- web redirects: ['https://oauth.pstmn.io/v1/callback', 'http://localhost/redirect', 'https://oauth.pstmn.io/v1/browser-callback', 'https://localhost']
- publicClient redirects: []
- exposed scopes: []
- preAuthorizedApplications: []
- knownClientApplications: []
- appRoles: []
- optionalClaims.accessToken: []; idToken: []
- groupMembershipClaims: None
- requiredResourceAccess (resource -> count): [('00000003-0000-0000-c000-000000000000', 11), ('00000003-0000-0ff1-ce00-000000000000', 2), ('1e40baad-e065-4aea-a8d4-4b7ab273458c', 1)]
- passwordCredentials: 2 secret(s) (names/values not shown); keyCredentials: 1 cert(s)
- service principal: {'appRoleAssignmentRequired': False, 'ownerTenant': 'a221a95e-6abc-4434-aecc-e48338a1b2f2', 'spObjectId': 'ab1eae64-ef31-4d72-bcab-62db4610075e', 'type': 'Application'}
- federated credentials: []

### A3 `5175798e-f23e-41c3-b09b-7a90b9218189` — former msal_client (said retired)

- **App object: NOT FOUND in this tenant** (ERROR: Resource '5175798e-f23e-41c3-b09b-7a90b9218189' does not exist or one of its queried reference-property objects are not present.)

- service principal: {'servicePrincipal': 'none in this tenant'}
- federated credentials: []

### A4 `b36e9b91-ee7d-46e6-9f6a-376871cc9d54` — SPE File Viewer PCF (legacy, still in ribbons)

- displayName: SPE File Viewer PCF
- signInAudience: **AzureADMyOrg**
- identifierUris: []
- spa redirects: ['https://spaarke-demo.crm.dynamics.com', 'https://spaarkedev1.crm.dynamics.com']
- web redirects: []
- publicClient redirects: []
- exposed scopes: []
- preAuthorizedApplications: []
- knownClientApplications: []
- appRoles: []
- optionalClaims.accessToken: []; idToken: []
- groupMembershipClaims: None
- requiredResourceAccess (resource -> count): [('1e40baad-e065-4aea-a8d4-4b7ab273458c', 1), ('00000003-0000-0000-c000-000000000000', 1)]
- passwordCredentials: 0 secret(s) (names/values not shown); keyCredentials: 0 cert(s)
- service principal: {'appRoleAssignmentRequired': False, 'ownerTenant': 'a221a95e-6abc-4434-aecc-e48338a1b2f2', 'spObjectId': '7aba1d3f-1a0e-49a8-8798-b74cf238b549', 'type': 'Application'}
- federated credentials: []

### A5 `fd1325aa-a709-4f15-b1f5-600f30d28875` — Spaarke DMS-SPE Dev 1 (said deprecated)

- **App object: NOT FOUND in this tenant** (ERROR: Resource 'fd1325aa-a709-4f15-b1f5-600f30d28875' does not exist or one of its queried reference-property objects are not present.)

- service principal: {'servicePrincipal': 'none in this tenant'}
- federated credentials: []

### A6 `c1258e2d-1688-49d2-ac99-a7485ebd9995` — Office add-in (dev)

- displayName: Spaarke Office Add-in
- signInAudience: **AzureADMyOrg**
- identifierUris: ['api://icy-desert-0bfdbb61e.6.azurestaticapps.net/c1258e2d-1688-49d2-ac99-a7485ebd9995']
- spa redirects: ['https://icy-desert-0bfdbb61e.6.azurestaticapps.net/auth-callback.html', 'brk-multihub://localhost', 'brk-multihub://icy-desert-0bfdbb61e.6.azurestaticapps.net', 'brk-9199bf20-a13f-4107-85dc-02114787ef48://icy-desert-0bfdbb61e.6.azurestaticapps.net', 'https://icy-desert-0bfdbb61e.6.azurestaticapps.net/outlook/taskpane.html', 'https://icy-desert-0bfdbb61e.6.azurestaticapps.net/auth-end.html', 'https://icy-desert-0bfdbb61e.6.azurestaticapps.net/auth-dialog.html', 'https://localhost:3000/auth-dialog.html', 'https://spe-api-dev-67e2xz.azurewebsites.net/office/auth-callback', 'https://localhost:3000/taskpane.html']
- web redirects: []
- publicClient redirects: ['https://login.microsoftonline.com/common/oauth2/nativeclient']
- exposed scopes: []
- preAuthorizedApplications: []
- knownClientApplications: []
- appRoles: []
- optionalClaims.accessToken: []; idToken: ['email', 'upn']
- groupMembershipClaims: None
- requiredResourceAccess (resource -> count): [('1e40baad-e065-4aea-a8d4-4b7ab273458c', 2), ('00000003-0000-0000-c000-000000000000', 3)]
- passwordCredentials: 0 secret(s) (names/values not shown); keyCredentials: 0 cert(s)
- service principal: {'appRoleAssignmentRequired': False, 'ownerTenant': 'a221a95e-6abc-4434-aecc-e48338a1b2f2', 'spObjectId': '53a09090-3305-4a19-baf8-14bff96c3df9', 'type': 'Application'}
- federated credentials: []

### A7 `1958aec2-0218-495e-8e3c-37133e9b8357` — Office add-in (production)

- displayName: Spaarke Office Add-in (Production)
- signInAudience: **AzureADMyOrg**
- identifierUris: []
- spa redirects: ['https://addins.spaarke.com/auth-callback.html', 'brk-multihub://addins.spaarke.com']
- web redirects: []
- publicClient redirects: []
- exposed scopes: []
- preAuthorizedApplications: []
- knownClientApplications: []
- appRoles: []
- optionalClaims.accessToken: []; idToken: []
- groupMembershipClaims: None
- requiredResourceAccess (resource -> count): [('00000003-0000-0000-c000-000000000000', 3)]
- passwordCredentials: 0 secret(s) (names/values not shown); keyCredentials: 0 cert(s)
- service principal: {'appRoleAssignmentRequired': False, 'ownerTenant': 'a221a95e-6abc-4434-aecc-e48338a1b2f2', 'spObjectId': '6fb59227-8bbd-4ba2-ad7a-e24528ca385f', 'type': 'Application'}
- federated credentials: []

### A9 `f257a0a9-1061-4f9b-8918-3ad056fe90db` — Copilot agent / bot

- displayName: Spaarke Copilot Bot Dev
- signInAudience: **AzureADMyOrg**
- identifierUris: []
- spa redirects: []
- web redirects: []
- publicClient redirects: []
- exposed scopes: []
- preAuthorizedApplications: []
- knownClientApplications: []
- appRoles: []
- optionalClaims.accessToken: []; idToken: []
- groupMembershipClaims: None
- requiredResourceAccess (resource -> count): []
- passwordCredentials: 0 secret(s) (names/values not shown); keyCredentials: 0 cert(s)
- service principal: {'servicePrincipal': 'none in this tenant'}
- federated credentials: []

### A14 `da03fe1a-4b1d-4297-a4ce-4b83cae498a9` — demo BFF app

- displayName: Spaarke BFF API - Demo
- signInAudience: **AzureADMyOrg**
- identifierUris: ['api://da03fe1a-4b1d-4297-a4ce-4b83cae498a9']
- spa redirects: []
- web redirects: ['https://spaarke-bff-demo.azurewebsites.net/.auth/login/aad/callback', 'https://spaarke-bff-demo.azurewebsites.net', 'https://localhost']
- publicClient redirects: []
- exposed scopes: [('SDAP.Access', True), ('user_impersonation', True)]
- preAuthorizedApplications: ['04b07795-8ddb-461a-bbee-02f9e1bf7b46']
- knownClientApplications: []
- appRoles: ['Admin']
- optionalClaims.accessToken: ['email', 'preferred_username', 'upn']; idToken: []
- groupMembershipClaims: None
- requiredResourceAccess (resource -> count): [('00000007-0000-0000-c000-000000000000', 1), ('00000003-0000-0000-c000-000000000000', 12), ('00000003-0000-0ff1-ce00-000000000000', 2)]
- passwordCredentials: 2 secret(s) (names/values not shown); keyCredentials: 0 cert(s)
- service principal: {'appRoleAssignmentRequired': False, 'ownerTenant': 'a221a95e-6abc-4434-aecc-e48338a1b2f2', 'spObjectId': 'e7c9e67f-89b5-465f-b3d2-b2385c245ec0', 'type': 'Application'}
- federated credentials: []

### A15 `965a4a01-01e1-442b-97a6-6a98308018b3` — L2 control plane AzureAd ClientId (dev)

- **App object: NOT FOUND in this tenant** (ERROR: Resource '965a4a01-01e1-442b-97a6-6a98308018b3' does not exist or one of its queried reference-property objects are not present.)

- service principal: {'appRoleAssignmentRequired': False, 'ownerTenant': None, 'spObjectId': '38f7693f-e6e2-4a3e-9acf-7f9e29dd4044', 'type': 'ManagedIdentity'}
- federated credentials: []

### A16 `46670ee2-ac0c-44b0-9ac2-d40ae4dcbdd7` — Spaarke Exchange Admin

- displayName: Spaarke Exchange Admin
- signInAudience: **AzureADMyOrg**
- identifierUris: []
- spa redirects: []
- web redirects: []
- publicClient redirects: []
- exposed scopes: []
- preAuthorizedApplications: []
- knownClientApplications: []
- appRoles: []
- optionalClaims.accessToken: []; idToken: []
- groupMembershipClaims: None
- requiredResourceAccess (resource -> count): [('00000002-0000-0ff1-ce00-000000000000', 1)]
- passwordCredentials: 0 secret(s) (names/values not shown); keyCredentials: 0 cert(s)
- service principal: {'appRoleAssignmentRequired': False, 'ownerTenant': 'a221a95e-6abc-4434-aecc-e48338a1b2f2', 'spObjectId': 'b5d396bb-d8a7-4017-875c-d999a4692163', 'type': 'Application'}
- federated credentials: [{'issuer': 'https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0', 'name': 'sprk-controlplane-dev-uami-assertion', 'subject': '38f7693f-e6e2-4a3e-9acf-7f9e29dd4044'}]

### A17 `bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e` — Spaarke SPE Model 1 Owner

- displayName: Spaarke SPE Model 1 Owner
- signInAudience: **AzureADMyOrg**
- identifierUris: []
- spa redirects: []
- web redirects: []
- publicClient redirects: []
- exposed scopes: []
- preAuthorizedApplications: []
- knownClientApplications: []
- appRoles: []
- optionalClaims.accessToken: []; idToken: []
- groupMembershipClaims: None
- requiredResourceAccess (resource -> count): [('00000003-0000-0000-c000-000000000000', 4)]
- passwordCredentials: 0 secret(s) (names/values not shown); keyCredentials: 0 cert(s)
- service principal: {'appRoleAssignmentRequired': False, 'ownerTenant': 'a221a95e-6abc-4434-aecc-e48338a1b2f2', 'spObjectId': '6c1165e2-2193-4f82-ba30-e0f64d5e767e', 'type': 'Application'}
- federated credentials: [{'issuer': 'https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0', 'name': 'sprk-controlplane-dev-uami-assertion', 'subject': '38f7693f-e6e2-4a3e-9acf-7f9e29dd4044'}]

### A20 `8c85a481-f3a0-46de-b84e-3ede8a4d60c3` — github-actions-spe-infrastructure (OIDC)

- displayName: github-actions-spe-infrastructure
- signInAudience: **AzureADMyOrg**
- identifierUris: []
- spa redirects: []
- web redirects: []
- publicClient redirects: []
- exposed scopes: []
- preAuthorizedApplications: []
- knownClientApplications: []
- appRoles: []
- optionalClaims.accessToken: []; idToken: []
- groupMembershipClaims: None
- requiredResourceAccess (resource -> count): []
- passwordCredentials: 1 secret(s) (names/values not shown); keyCredentials: 0 cert(s)
- service principal: {'appRoleAssignmentRequired': False, 'ownerTenant': 'a221a95e-6abc-4434-aecc-e48338a1b2f2', 'spObjectId': '6edc4cee-1837-4d6d-a0c7-a1c8d6ad1c98', 'type': 'Application'}
- federated credentials: [{'issuer': 'https://token.actions.githubusercontent.com', 'name': 'gh-ref-refs-heads-master', 'subject': 'repo:spaarke-dev/spaarke:ref:refs/heads/master'}, {'issuer': 'https://token.actions.githubusercontent.com', 'name': 'spaarke-dev-deploy-promote-production', 'subject': 'repo:spaarke-dev/spaarke:environment:production'}, {'issuer': 'https://token.actions.githubusercontent.com', 'name': 'spaarke-dev-deploy-promote-staging', 'subject': 'repo:spaarke-dev/spaarke:environment:staging'}, {'issuer': 'https://token.actions.githubusercontent.com', 'name': 'spaarke-dev-deploy-promote-dev', 'subject': 'repo:spaarke-dev/spaarke:environment:dev'}]

### A21 `ed44fb14-7d2a-476c-8e1b-7e43e3cfecb0` — unknown (orphan sprk_DocumentDelete.js client)

- **App object: NOT FOUND in this tenant** (ERROR: Resource 'ed44fb14-7d2a-476c-8e1b-7e43e3cfecb0' does not exist or one of its queried reference-property objects are not present.)

- service principal: {'servicePrincipal': 'none in this tenant'}
- federated credentials: []

### A22 `8c60b44e-5fc7-471c-940e-0f77f395c18d` — unknown (orphan sprk_DocumentDelete.js resource)

- **App object: NOT FOUND in this tenant** (ERROR: Resource '8c60b44e-5fc7-471c-940e-0f77f395c18d' does not exist or one of its queried reference-property objects are not present.)

- service principal: {'servicePrincipal': 'none in this tenant'}
- federated credentials: []


### Per-customer BFF apps and prod BFF app (by display name)

Name                  AppId                                 SignInAudience
--------------------  ------------------------------------  ----------------
spaarke-bff-api-prod  92ecc702-d9ae-492d-957e-563244e93d8c  AzureADMyOrg

## 2. Dev BFF App Service auth settings (secret-looking values masked)

- resource group: rg-spaarke-dev

### slot: production

| setting | value |
|---|---|
| `AgentToken__AgentAppId` | `(masked)` |
| `AgentToken__CacheTtlMinutes` | `(masked)` |
| `AgentToken__ClientId` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `AgentToken__CopilotAudience` | `api://auth-3e04ab58-8450-44d6-b95b-daca16b6cbdb/1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `AgentToken__DataverseEnvironmentUrl` | `(masked)` |
| `AgentToken__TenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `AzureAd__Audience` | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `AzureAd__ClientId` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `AzureAd__Instance` | `https://login.microsoftonline.com/` |
| `AzureAd__TenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `AzureAd__ValidAudiences__0` | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `AzureAd__ValidAudiences__1` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `AzureAd__ValidAudiences__2` | `api://green-dune-0c4f1221e.7.azurestaticapps.net/1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `Ciam__Audience` | `4a4d5126-91b0-4865-8e3a-134b7209013e` |
| `Ciam__ClientId` | `4a4d5126-91b0-4865-8e3a-134b7209013e` |
| `Ciam__Domain` | `spaarkeextid.onmicrosoft.com` |
| `Ciam__GraphProvisioner__CertificateName` | `ciam-graph-provisioner-cert` |
| `Ciam__GraphProvisioner__ClientId` | `e63e6eb1-be25-4214-80a8-a6d609034bb9` |
| `Ciam__Instance` | `https://spaarkeextid.ciamlogin.com` |
| `Ciam__TenantId` | `7052feba-bfc4-43e0-b09e-65014b429131` |
| `Communication__WebhookClientState` | `(Key Vault reference)` |
| `Communication__WebhookNotificationUrl` | `https://spaarke-bff-dev.azurewebsites.net/api/communications/incoming-webhook` |
| `Communication__WebhookSigningKey` | `(Key Vault reference)` |
| `Compose__Webhook__ClientState` | `(REDACTED — printed in clear by the first run of the script; plain app setting, not a Key Vault reference)` |
| `Compose__Webhook__NotificationUrl` | `https://spaarke-bff-dev.azurewebsites.net/api/compose/webhooks/spe-doc-changed` |
| `Compose__Webhook__SigningKey` | `(masked)` |
| `Cors__AllowedOrigins__0` | `https://spaarkedev1.crm.dynamics.com` |
| `Cors__AllowedOrigins__1` | `https://spaarkedev1.api.crm.dynamics.com` |
| `Cors__AllowedOrigins__2` | `https://spaarke.powerappsportals.com` |
| `Cors__AllowedOrigins__3` | `https://icy-desert-0bfdbb61e.6.azurestaticapps.net` |
| `Cors__AllowedOrigins__4` | `https://green-dune-0c4f1221e.7.azurestaticapps.net` |
| `Customer__Id` | `spaarke` |
| `Dataverse__ClientId` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `Dataverse__EnvironmentUrl` | `https://spaarkedev1.crm.dynamics.com` |
| `Dataverse__ServiceUrl` | `https://spaarkedev1.crm.dynamics.com` |
| `Dataverse__TenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `EmailProcessing__AutoEnqueueAi` | `false` |
| `EmailProcessing__AutoIndexToRag` | `false` |
| `EmailProcessing__DefaultContainerId` | `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50` |
| `EmailProcessing__EnablePolling` | `true` |
| `EmailProcessing__EnableWebhook` | `true` |
| `EmailProcessing__Enabled` | `true` |
| `EmailProcessing__PollingIntervalMinutes` | `5` |
| `EmailProcessing__WebhookSecret` | `(masked)` |
| `EmailProcessing__WebhookSigningKey` | `(masked)` |
| `Graph__ClientId` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `Graph__Credentials__Order__0` | `ManagedIdentityFederated` |
| `Graph__Credentials__RequireSecretFreeIdentity` | `(masked)` |
| `Graph__ManagedIdentity__ClientId` | `5967251e-171c-46fe-a6c2-ef843c90309d` |
| `Graph__ManagedIdentity__Enabled` | `true` |
| `Graph__Scopes__0` | `https://graph.microsoft.com/.default` |
| `Graph__TenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `ManagedIdentity__ClientId` | `5967251e-171c-46fe-a6c2-ef843c90309d` |
| `PublicConfig__BffUrl` | `https://spaarke-bff-dev.azurewebsites.net` |
| `PublicConfig__MsalClientId` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `PublicConfig__TenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `SpeAdmin__PlatformOperatorEnvironment` | `true` |
| `WorkforceIdentity__CustomerTenantIds__0` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |

### slot: staging

- could not read settings: 

## 3. Managed identities and Graph app-role grants

Name                         Rg                          ClientId                              PrincipalId
---------------------------  --------------------------  ------------------------------------  ------------------------------------
spaarke-bff-identity         SharePointEmbedded          17a74f26-f0d0-4373-a8bf-86b5e9bc01eb  c8cdf6fc-a414-4a5b-981c-006d0d84850f
mi-bff-api-dev               spe-infrastructure-westus2  5967251e-171c-46fe-a6c2-ef843c90309d  9fd47efb-7962-492b-ac44-e5ccd0268ebb
insights-spaarkedev-uami     spe-infrastructure-westus2  583ca2d8-d6c8-4aa2-adaa-ad696f6c4784  9e4261c7-9d3e-4c30-908f-031c5f9ff136
insights-search-deploy-uami  spe-infrastructure-westus2  4ceca18e-c2b9-471b-95c9-02514d649e77  42ffe13e-609c-4539-ab56-a238373b1874
sprk-controlplane-dev-uami   rg-spaarke-platform-dev     965a4a01-01e1-442b-97a6-6a98308018b3  38f7693f-e6e2-4a3e-9acf-7f9e29dd4044
mi-ontology-writer-dev       rg-spaarke-dev              69040982-612e-469e-a85f-26d5172367c5  6cf6d7b6-8dc2-4cfb-a971-649df8605be6

### app-role assignments held by service principal 9fd47efb-7962-492b-ac44-e5ccd0268ebb

Resource         AppRoleId
---------------  ------------------------------------
Microsoft Graph  741f803b-c850-494e-b5df-cde7c675a1ca
Microsoft Graph  bf394140-e372-4bf9-a898-299cfc7564e5
Microsoft Graph  62a82d76-70ea-41e2-9197-370581804d09
Microsoft Graph  2dcc6599-bd30-442b-8f11-90f88ad441dc
Microsoft Graph  810c84a8-4a9e-49e6-bf7d-12d183f40d01
Microsoft Graph  40dc41bc-0f7e-42ff-89bd-d9516947e474
Microsoft Graph  b633e1c5-b582-4048-a93e-9f11b44c7e96

### app-role assignments held by service principal 38f7693f-e6e2-4a3e-9acf-7f9e29dd4044

Resource         AppRoleId
---------------  ------------------------------------
Microsoft Graph  e2a3a72e-5f79-4c64-b1b1-878b674786c9
Microsoft Graph  741f803b-c850-494e-b5df-cde7c675a1ca
Microsoft Graph  5b567255-7703-4780-807c-7be8301ae99b
Microsoft Graph  19dbc75e-c2e2-444c-a770-ec69d8559fc7
Microsoft Graph  40f97065-369a-49f4-947c-6a255697ae91
Microsoft Graph  332a536c-c7ef-4017-ab91-336970924f0d
Microsoft Graph  9492366f-7969-46a4-8d15-ed1a20078fff
Microsoft Graph  09850681-111b-4a89-9bed-3f2cae46d706
Microsoft Graph  75359482-378d-4052-8f01-80520e7db3cd
Microsoft Graph  df021288-bdef-4463-88db-98f22de89214
Microsoft Graph  01d4889c-1287-42c6-ac1f-5d1e02578ef6
Microsoft Graph  810c84a8-4a9e-49e6-bf7d-12d183f40d01
Microsoft Graph  40dc41bc-0f7e-42ff-89bd-d9516947e474
Microsoft Graph  b633e1c5-b582-4048-a93e-9f11b44c7e96
Microsoft Graph  dbaae8cf-10b5-4b86-a4a1-f871c94c6695

## 4. Key Vault spaarke-spekvcert (names only)

### secrets
ai-openai-endpoint
ai-openai-key
ai-search-endpoint
application-insights-key
AzureOpenAI-ApiKey
bff-api-tenant-id
communication-trackingfooter-signingkey
Communication-WebhookClientState
Communication-WebhookSigningKey
DATAVERSE-URL
footer-hmac-key
MANAGED-IDENTITY-CLIENT-ID
office-addin-client-id
Redis-ConnectionString
SPE-ContainerTypeId
spe-owning-app-secret
SPRK-DEV-DATAVERSE-URL
SPRK-MANAGED-IDENTITY-CLIENT-ID
UAMI-ClientId
### deleted secrets
ai-search-key
AiSearch--AdminKey
AzureAISearchApiKey
bff-api-client-secret
BFF-API-ClientSecret
Graph-API-ClientSecret
ServiceBus-ConnectionString
spe-app-cert
spe-app-cert-pass
### certificates
ciam-graph-provisioner-cert

## 5. Tenant e2e89f3a-98bf-4db2-b149-5b3fe72e8fe7 (public OpenID metadata)

{'issuer': None, 'tenant_region_scope': None, 'cloud_instance_name': None, 'error': 'invalid_tenant', 'error_description': "AADSTS90002: Tenant 'e2e89f3a-98bf-4db2-b149-5b3fe72e8fe7' not found. Check to make sure you have the correct tenant ID and are signing into the correct cloud. Check with your subscription administrator, this may happen if there are no active subscriptions for the tenant. Trace ID: 60aff981-5905-4552-a38c-6348ce333800 Correlation ID: 08c30ff2-618e-4e06-9c91-253d0e797dce Timestamp: 2026-10-09 15:33:08Z"}

## 6. Resolved names (follow-up read-only lookups, 2026-10-09)

### Pre-authorized clients on the dev BFF app `1e40baad`
| client id | service principal display name in Spaarke tenant |
|---|---|
| `04b07795-8ddb-461a-bbee-02f9e1bf7b46` | Microsoft Azure CLI |
| `f306885a-8251-492c-8d3e-34d7b476ffd0` | spaarke-external-access-SPA |
| `ab3be6b7-f5df-413d-ac2d-abf1e3fd9c0b` | Microsoft Teams Graph Service |
| `1fec8e78-bce4-4aaf-ab1b-5451cc387264` | (no service principal in tenant; Teams desktop/mobile per provisioning notes) |
| `5e3ce6c0-2b1f-4285-8d4b-75ee78787346` | (no service principal in tenant; Teams web per provisioning notes) |
| `29d9ed98-a469-4536-ade2-f981bc1d605e` | (no service principal in tenant; unidentified) |
| `170c98e1…`, `c1258e2d…`, `f257a0a9…` | SDAP-PCF-CLIENT, Spaarke Office Add-in (dev), Spaarke Copilot Bot Dev (see §1) |

### Microsoft Graph application roles held by managed identities
| identity | Graph application roles |
|---|---|
| `mi-bff-api-dev` (principal `9fd47efb…`, BFF) | User.ReadWrite.All, SecurityEvents.Read.All, Group.ReadWrite.All, FileStorageContainerTypeReg.Selected, Mail.Read, FileStorageContainer.Selected, Mail.Send |
| `sprk-controlplane-dev-uami` (principal `38f7693f…`, L2) | Mail.ReadWrite, User.ReadWrite.All, Group.Read.All, Directory.ReadWrite.All, MailboxSettings.Read, Sites.Read.All, Sites.ReadWrite.All, User.Invite.All, Files.ReadWrite.All, User.Read.All, Files.Read.All, Mail.Read, FileStorageContainer.Selected, Mail.Send, GroupMember.ReadWrite.All |

Open checks (for the record): whether Exchange ApplicationAccessPolicy scopes the Mail.* grants to specific mailboxes, and whether every grant is used by code (least privilege).
