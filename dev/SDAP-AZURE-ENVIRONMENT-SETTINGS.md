# Spaarke Environment Settings

## Dataverse
Environment URL: https://spaarkedev1.crm.dynamics.com
API https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2/
Tenant ID: a221a95e-6abc-4434-aecc-e48338a1b2f2
Object ID: c74ac1af-ff3b-46fb-83e7-3063616e959c
PPAC UI: 0.0.20250908.4-2509.2-prod
PPAC UI location: EUS
PPAC API: 1.2025.904.3
PPAC API location: eus
https://make.preview.powerautomate.com/environments/b5a401dd-b42b-e84a-8cab-2aef8471220d/connections/shared_commondataserviceforapps/d1e4fdf015a0474aadef2a192d01d9a5/details

## Azure
Resource Group: SharePointEmbedded
Subscription Name:Spaarke SPE Subscription 1
Subscription ID: 484bc857-3802-427f-9ea5-ca47b43db0f0

## Spe.Bff.Api web app: spe-api-dev-67e2xz.azurewebsites.net
Client ID: 6bbcfa82-14a0-40b5-8695-a271f4bac521
Object (principal) ID: 56ae2188-c978-4734-ad16-0bc288973f20

## App Registration for: SDAP-BFF-SPE-API (formerly SPE-BFF-API)
Directory (tentant) ID: a221a95e-6abc-4434-aecc-e48338a1b2f2
Application (client) ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
Object ID: c2aab303-50f8-4279-9934-503ab3a4b357
Scopes: api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation
Secret Description: SPE BFF API
Client secret: CBi8Q~v52JqvSMeKb2lIn~8mSjvNQZRu5yIvrcEy
Secret ID: 3d09b386-89f0-41e2-902c-ed38a6ab1646


## App Registration for: Spaarke DSM-SPE Dev 2 
Application (client) ID: 170c98e1-d486-4355-bcbe-170454e0207c
Object ID: f21aa14d-0f0b-46f9-9045-9d5dfef58cf7
Directory (tenant) ID: a221a95e-6abc-4434-aecc-e48338a1b2f2
Supported account types: Multiple organizations
Secret Description: SPE Dev 2 Functions Secret
Client secret: ~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj
Secret ID: 40fcc0c4-4d60-4526-b303-be592f11314e

### Certificate Description: SPECertificate_22Sept2025_1
- Thumbprint: 269691A5A60536050FA76C0163BD4A942ECD724D
Certificate ID: d49a1e6b-a45f-47e2-8532-e8f0791f5273
- "C:\code_files\spaarke spe\spe-solution-2\config\SPECertificate_22Sept2025_1.cer"
- "C:\code_files\spaarke spe\spe-solution-2\config\SPECertificate_22Sept2025_1.crt.pem"
- "C:\code_files\spaarke spe\spe-solution-2\config\SPECertificate_22Sept2025_1.key.pem"
"C:\code_files\spaarke spe\spe-solution-2\config\SPECertificate_22Sept2025_1.key.pkcs8.nopass.pem"
"C:\code_files\spaarke spe\spe-solution-2\config\SPECertificate_22Sept2025_1.full.pem"
"C:\code_files\spaarke spe\spe-solution-2\config\SPECertificate_22Sept2025_1.pfx"
@RachelMary123 , Spaarke

## Key Vault Name: spaarke-spekvcert
Subscription: Spaarke SPE Subscription 1
Resource Group: SharePointEmbedded
Correlation ID: d9b32e04-a6bd-4b92-813b-afa1a16e0725
Resource ID: /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.KeyVault/vaults/spaarke-spekvcert
For the Graph API authentication we need to use the PEM with ----Private Key---- (but not encrypted)

### KeyVaultCertName: "spe-app-cert"
Description: The PFX certificate in base 64
KeyVaultUrl: "https://spaarke-spekvcert.vault.azure.net/"
id: "https://spaarke-spekvcert.vault.azure.net/secrets/spe-app-cert/91eaa0c214a642e9838c9a9100d26788"
file-encoding: "base64"

### KeyVaultName: "spe-app-cert-pass"
Description: the PFX password
ID: https://spaarke-spekvcert.vault.azure.net/secrets/spe-app-cert-pass/6691dc42082c4323a631e4effa8458ac

# SharePoint Embedded
ContainerTypeName   : Spaarke PAYGO 1
ContainerTypeId     : 8a6ce34c-6055-4681-8f87-2f4f9f921c06
OwningApplicationId    : 170c98e1-d486-4355-bcbe-170454e0207c
AzureSubscriptionId    : 484bc857-3802-427f-9ea5-ca47b43db0f0
ResourceGroup          : SharePointEmbedded
Region                 : eastus
Classification         : Standard
CreationDate           : 9/22/2025
ExpiryDate             : -
ApplicationRedirectUrl : https://localhost
IsGovernableByAdmin    : True

## SPO Admin URL: https://spaarke-admin.sharepoint.com

## Container ID:b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50
Container Permission
"user":"displayName": "Ralph Schroeder", "email": "ralph.schroeder@spaarke.com", "id": "c74ac1af-ff3b-46fb-83e7-3063616e959c","userPrincipalName": "ralph.schroeder@spaarke.com"




