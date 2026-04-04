# Azure & Entra ID Setup Guide: Self-Service Registration

> **Purpose**: Step-by-step instructions for configuring Azure, Entra ID, Exchange, and Dataverse for the Spaarke Self-Service Registration system in a new environment.
>
> **Audience**: Operations team, DevOps engineers, tenant administrators.
>
> **Companion Guide**: See `SPAARKE-SELF-SERVICE-USER-REGISTRATION.md` for feature overview, architecture, and user-facing documentation.
>
> **Last Updated**: April 4, 2026

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Entra ID App Registration Setup](#entra-id-app-registration-setup)
3. [Entra ID Security Group](#entra-id-security-group)
4. [Conditional Access MFA Exclusion](#conditional-access-mfa-exclusion)
5. [Exchange Application Access Policy](#exchange-application-access-policy)
6. [License Discovery](#license-discovery)
7. [BFF API App Service Configuration](#bff-api-app-service-configuration)
8. [Dataverse Environment Setup](#dataverse-environment-setup)
9. [Website Configuration](#website-configuration)
10. [Complete New Environment Checklist](#complete-new-environment-checklist)

---

## Prerequisites

Before starting, ensure you have:

- **Global Administrator** or **Privileged Role Administrator** access to the Entra ID tenant
- **Exchange Online Administrator** access (for Application Access Policy)
- **Azure subscription** with an existing or planned App Service for the BFF API
- **Dataverse environment** provisioned with the Spaarke solution deployed
- **PowerShell modules installed**:
  - `Az` (Azure PowerShell)
  - `ExchangeOnlineManagement`
  - `Microsoft.Graph` (optional, for scripted Graph operations)

---

## Entra ID App Registration Setup

For each environment (dev, demo, production), the BFF API app registration must be configured with the following. If an app registration already exists for the BFF API, add these settings to it. If starting fresh, create a new app registration first.

### Step 1: API Scopes (Expose an API)

These scopes allow PCF controls and ribbon JavaScript to call the BFF API on behalf of the signed-in user.

1. Navigate to **Entra ID** > **App registrations** > select the BFF API app registration
2. Go to **Expose an API**
3. If no Application ID URI is set, click **Set** and accept the default (`api://{client-id}`) or use a custom URI
4. Click **Add a scope** and create the following scopes:

| Scope Name | Display Name | Who Can Consent | Description |
|-----------|--------------|-----------------|-------------|
| `SDAP.Access` | Access Spaarke SDAP | Admins and users | Allows the application to access Spaarke SDAP APIs on behalf of the signed-in user |
| `user_impersonation` | Access Spaarke API | Admins and users | Legacy/fallback scope for API access |

5. For each scope, set **State** to **Enabled**

### Step 2: Application Permissions (Microsoft Graph)

These are **Application (Role) permissions**, NOT Delegated permissions. The provisioning pipeline uses app-only tokens via `GraphClientFactory.ForApp()` and does not operate in a user context.

1. Go to **API permissions** > **Add a permission** > **Microsoft Graph** > **Application permissions**
2. Add each of the following permissions:

| Permission | Permission ID | Purpose |
|-----------|---------------|---------|
| `User.ReadWrite.All` | `741f803b-c850-494e-b5df-cde7c675a1ca` | Create and disable demo user accounts |
| `GroupMember.ReadWrite.All` | `dbaae8cf-10b5-4b86-a4a1-f871c94c6571` | Add/remove users from the demo security group |
| `Directory.ReadWrite.All` | `06b708a9-e830-4db3-a914-8e69da51d44f` | Assign licenses to demo users |
| `Files.ReadWrite.All` | `75359482-378d-4052-8f01-80520e7db3cd` | SPE container access (may already exist) |
| `Mail.Send` | `40dc41bc-0f7e-42ff-89bd-d9516947e474` | Send welcome and expiration notification emails (may already exist) |

3. After adding all permissions, click **Grant admin consent for {your-tenant-name}**
4. Verify that each permission shows a green checkmark under **Status** indicating "Granted for {tenant}"

> **IMPORTANT**: These MUST be Application permissions, not Delegated. If you accidentally add Delegated permissions, the provisioning pipeline will fail with 403 errors because it uses app-only authentication.

### Step 3: App Roles

The `Admin` app role is used by `RegistrationAuthorizationFilter` to gate the approve/reject endpoints. Only users assigned this role can approve or reject registration requests.

1. Go to **App roles** > **Create app role**
2. Configure:

| Field | Value |
|-------|-------|
| Display name | `Admin` |
| Allowed member types | Users/Groups |
| Value | `Admin` |
| Description | `Administrators who can approve and reject demo registration requests` |
| Enabled | Yes (checked) |

3. Click **Apply**
4. To assign users to this role: Go to **Enterprise applications** > find the BFF API app > **Users and groups** > **Add user/group** > select users > select the `Admin` role > **Assign**

### Step 4: Optional Claims

These claims ensure the BFF API receives user identity information in the access token.

1. Go to **Token configuration** > **Add optional claim**
2. Select **Access token**
3. Add the following claims:
   - `email`
   - `preferred_username`
   - `upn`
4. If prompted to add the required Microsoft Graph permissions for these claims, accept

---

## Entra ID Security Group

The provisioning pipeline automatically adds each approved demo user to this security group. The group is also used for Conditional Access MFA exclusion (next section).

### Step 1: Create the Security Group

1. Navigate to **Entra ID** > **Groups** > **New group**
2. Configure:

| Field | Value |
|-------|-------|
| Group type | Security |
| Group name | `Spaarke Demo Users` |
| Group description | `Auto-managed group for demo users provisioned by the self-service registration system` |
| Microsoft Entra roles can be assigned to the group | No |
| Membership type | Assigned |

3. Click **Create**
4. **Record the Group Object ID** -- you will need this for the BFF appsettings configuration (`DemoProvisioning__DemoUsersGroupId`)

> **NOTE**: If a "Spaarke Demo Users" group already exists in the tenant (from a prior setup), reuse it. Do not create a duplicate.

---

## Conditional Access MFA Exclusion

Demo users should NOT be prompted for MFA. This is achieved by excluding the demo users group from existing MFA-requiring Conditional Access policies.

> **CRITICAL**: Creating a separate "allow" Conditional Access policy does NOT work. Conditional Access is **additive** -- all policies that match a user are applied together. The exclusion MUST be placed on the existing MFA-requiring policy.

### If the Tenant Uses Conditional Access Policies

1. Navigate to **Entra ID** > **Protection** > **Conditional Access** > **Policies**
2. Identify ALL policies that require MFA. Common names include:
   - "Multifactor authentication for all users"
   - "Require MFA for admins"
   - "Require MFA for Azure Management"
   - Any custom policy with **Grant** > **Require multifactor authentication**
3. For EACH policy that includes "All users" in its scope and requires MFA:
   a. Click the policy to edit it
   b. Go to **Users** > **Exclude**
   c. Click **Select excluded users and groups**
   d. Search for and add **Spaarke Demo Users**
   e. Click **Select**, then **Save**
4. Repeat for every MFA-requiring policy that could match demo users

### Verification

1. Go to **What If** (Conditional Access > What If)
2. Select a test demo user (or create a temporary one in the demo group)
3. Select any cloud app
4. Click **What If**
5. Confirm that no MFA-requiring policies are listed in the results

### If the Tenant Uses Security Defaults (Instead of Conditional Access)

Security Defaults enforce MFA for all users and **cannot exclude groups**. You must:

1. Navigate to **Entra ID** > **Properties** > **Manage Security defaults**
2. Set **Security defaults** to **Disabled**
3. Create explicit Conditional Access policies to replace Security Defaults:
   - Policy 1: Require MFA for all users (with Spaarke Demo Users excluded)
   - Policy 2: Require MFA for admin roles (with Spaarke Demo Users excluded)
   - Policy 3: Block legacy authentication (no exclusion needed)
4. Follow the Conditional Access steps above

> **WARNING**: Disabling Security Defaults removes all default protections. Ensure you create replacement Conditional Access policies immediately.

---

## Exchange Application Access Policy

The BFF API sends welcome and expiration notification emails from a shared mailbox. Exchange Application Access Policies restrict which mailboxes the app can send from.

### Step 1: Connect to Exchange Online

```powershell
Connect-ExchangeOnline -UserPrincipalName admin@{your-domain}.com
```

### Step 2: Check for Existing Policy

```powershell
Get-ApplicationAccessPolicy | Format-Table Identity, AppId, ScopeIdentity
```

Look for a policy with the BFF API's Application (Client) ID.

### Step 3a: If a Policy Already Exists for the BFF API App ID

The BFF API already has an Application Access Policy scoped to a mail-enabled security group. Just add the demo mailbox to that group:

```powershell
# Add the demo sender mailbox to the existing security group
Add-DistributionGroupMember -Identity "Spaarke BFF API Senders" -Member "demo@demo.{your-domain}.com"
```

### Step 3b: If NO Policy Exists

Create the mail-enabled security group and the Application Access Policy from scratch:

```powershell
# 1. Create a mail-enabled security group for allowed senders
New-DistributionGroup `
  -Name "Spaarke BFF API Senders" `
  -Type Security `
  -Members "demo@demo.{your-domain}.com"

# 2. Create the Application Access Policy
#    Replace the -AppId value with your BFF API's Application (Client) ID
New-ApplicationAccessPolicy `
  -AppId "{bff-api-client-id}" `
  -PolicyScopeGroupId "Spaarke BFF API Senders" `
  -AccessRight RestrictAccess `
  -Description "Allow BFF API to send email from approved mailboxes"
```

### Step 4: Verify

```powershell
# Test that the BFF API can access the demo mailbox
Test-ApplicationAccessPolicy `
  -Identity "demo@demo.{your-domain}.com" `
  -AppId "{bff-api-client-id}"

# Expected output:
# AccessCheckResult : Granted
```

If the result is `Denied`, verify:
- The mailbox address is correct and active
- The mailbox was added to the security group
- The Application Access Policy AppId matches the BFF API's Client ID
- Wait a few minutes -- policy propagation can take up to 30 minutes

### Step 5: Disconnect

```powershell
Disconnect-ExchangeOnline -Confirm:$false
```

---

## License Discovery

Before configuring the BFF appsettings, discover the SKU IDs for the licenses that will be assigned to demo users. SKU IDs are tenant-specific and must be looked up.

### Run the Discovery Script

```powershell
.\scripts\Get-LicenseSkuIds.ps1
```

This script queries the tenant and outputs the SKU IDs for:

| License Name | SKU Part Number | Purpose |
|-------------|-----------------|---------|
| Microsoft Power Apps Plan 2 Trial | `POWERAPPS_VIRAL` | Required for demo users to access Dataverse model-driven apps |
| Microsoft Fabric Free | `POWER_BI_STANDARD` | Required for embedded analytics/reporting |
| Microsoft Power Automate Free | `FLOW_FREE` | Required for Power Automate flows triggered by demo users |

**Record the three SKU IDs** from the script output. You will need them for the BFF appsettings in the next section.

> **NOTE**: If a license is not available in your tenant, you may need to start a trial or purchase the license first. The provisioning pipeline will fail to assign licenses that do not exist in the tenant.

---

## BFF API App Service Configuration

For each environment, configure the App Service with the demo provisioning settings. These are set as application settings (environment variables) on the Azure App Service.

### Single Environment Example

```bash
az webapp config appsettings set \
  --resource-group "{resource-group-name}" \
  --name "{app-service-name}" \
  --settings \
    "DemoProvisioning__DefaultEnvironment={environment-name}" \
    "DemoProvisioning__AccountDomain=demo.{your-domain}.com" \
    "DemoProvisioning__DemoUsersGroupId={entra-security-group-object-id}" \
    "DemoProvisioning__AdminNotificationEmails__0={admin-email-address}" \
    "DemoProvisioning__Licenses__PowerAppsPlan2TrialSkuId={sku-id-from-discovery}" \
    "DemoProvisioning__Licenses__FabricFreeSkuId={sku-id-from-discovery}" \
    "DemoProvisioning__Licenses__PowerAutomateFreeSkuId={sku-id-from-discovery}" \
    "DemoProvisioning__Environments__0__Name={environment-name}" \
    "DemoProvisioning__Environments__0__DataverseUrl=https://{org}.crm.dynamics.com" \
    "DemoProvisioning__Environments__0__BusinessUnitName=Spaarke Demo" \
    "DemoProvisioning__Environments__0__TeamName=Spaarke Demo" \
    "DemoProvisioning__Environments__0__SpeContainerId={spe-container-guid}" \
    "DemoProvisioning__Environments__0__DefaultDemoDurationDays=14"
```

### Concrete Dev Environment Example

```bash
az webapp config appsettings set \
  --resource-group "spe-infrastructure-westus2" \
  --name "spe-api-dev-67e2xz" \
  --settings \
    "DemoProvisioning__DefaultEnvironment=dev" \
    "DemoProvisioning__AccountDomain=demo.spaarke.com" \
    "DemoProvisioning__DemoUsersGroupId=a1b2c3d4-e5f6-7890-abcd-ef1234567890" \
    "DemoProvisioning__AdminNotificationEmails__0=admin@spaarke.com" \
    "DemoProvisioning__Licenses__PowerAppsPlan2TrialSkuId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" \
    "DemoProvisioning__Licenses__FabricFreeSkuId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" \
    "DemoProvisioning__Licenses__PowerAutomateFreeSkuId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" \
    "DemoProvisioning__Environments__0__Name=dev" \
    "DemoProvisioning__Environments__0__DataverseUrl=https://spaarkedev1.crm.dynamics.com" \
    "DemoProvisioning__Environments__0__BusinessUnitName=Spaarke Demo" \
    "DemoProvisioning__Environments__0__TeamName=Spaarke Demo" \
    "DemoProvisioning__Environments__0__SpeContainerId=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" \
    "DemoProvisioning__Environments__0__DefaultDemoDurationDays=14"
```

### Adding a Second Environment

To add a second environment (e.g., "demo"), use index `__1__` for all `Environments` settings:

```bash
az webapp config appsettings set \
  --resource-group "{resource-group-name}" \
  --name "{app-service-name}" \
  --settings \
    "DemoProvisioning__Environments__1__Name=demo" \
    "DemoProvisioning__Environments__1__DataverseUrl=https://{demo-org}.crm.dynamics.com" \
    "DemoProvisioning__Environments__1__BusinessUnitName=Spaarke Demo" \
    "DemoProvisioning__Environments__1__TeamName=Spaarke Demo" \
    "DemoProvisioning__Environments__1__SpeContainerId={demo-spe-container-guid}" \
    "DemoProvisioning__Environments__1__DefaultDemoDurationDays=14"
```

### Multiple Admin Notification Emails

To notify multiple administrators:

```bash
"DemoProvisioning__AdminNotificationEmails__0=admin1@spaarke.com" \
"DemoProvisioning__AdminNotificationEmails__1=admin2@spaarke.com"
```

---

## Dataverse Environment Setup

Some Dataverse components are prerequisites (manually configured before the registration system), and some are automated by scripts.

### Prerequisites (NOT Automated -- Must Exist Before Setup)

These must be configured manually or via your standard Dataverse deployment process:

1. **Dataverse environment provisioned** with the Spaarke solution deployed
2. **Business Unit**: "Spaarke Demo" created under the root business unit
3. **Owner Team**: "Spaarke Demo" team created (must be **Owner** team, NOT Access team) with the appropriate security role assigned granting access to demo data
4. **SPE container**: SharePoint Embedded container provisioned with sample documents for demo users

### Automated: Create Registration Request Table

Run the schema creation script to create the `sprk_registrationrequest` custom table:

```powershell
.\scripts\Create-RegistrationRequestSchema.ps1 -EnvironmentDomain "{org}.crm.dynamics.com"
```

This creates:
- The `sprk_registrationrequest` entity with all required columns
- Status and status reason option sets
- Lookup relationships

### Manual: Views, Form, and Sitemap

After the table is created, configure the following in the Dataverse Model-Driven App (MDA) designer:

1. **Views** for the `sprk_registrationrequest` table:
   - Active Registration Requests (filtered by status = Pending)
   - All Registration Requests
   - Approved Requests
   - Rejected Requests
2. **Main Form** with relevant fields for admin review
3. **Sitemap entry** in the appropriate model-driven app to expose registration request management

### Deploy Ribbon Buttons

Ribbon buttons enable approve/reject actions directly from the MDA grid and form.

1. Export the `RegistrationRibbons` solution from Dataverse (or create a new unmanaged solution containing the `sprk_registrationrequest` entity)
2. Inject `RibbonDiffXml` using the ribbon-edit skill or Ribbon Workbench
3. Re-import the solution
4. Upload the `sprk_/js/registrationribbon.js` web resource if not already present
5. Publish all customizations

---

## Website Configuration

The registration website (external-facing SPA) needs to know the BFF API URL.

Set the following in the website's hosting environment:

```
BFF_API_URL=https://{bff-app-service-name}.azurewebsites.net
```

> **CRITICAL**: The URL must be the **host only** -- do NOT include `/api` at the end. The application code appends path segments as needed. Including `/api` will cause double-pathing errors (e.g., `https://host.com/api/api/registration`). See the BFF URL normalization pattern for details.

For static site hosting (e.g., Azure Static Web Apps), this is typically set as an environment variable or build-time configuration value.

---

## Complete New Environment Checklist

Use this checklist when setting up the self-service registration system in a brand new environment. Complete each item in order.

### Dataverse Foundation

- [ ] Dataverse environment provisioned with Spaarke solution
- [ ] Business Unit "Spaarke Demo" created
- [ ] Owner Team "Spaarke Demo" created with security role assigned
- [ ] SPE container created and populated with sample documents

### Entra ID Configuration

- [ ] BFF API app registration exists (or create new)
- [ ] API scopes created: `SDAP.Access`, `user_impersonation`
- [ ] Application permissions added: `User.ReadWrite.All`, `GroupMember.ReadWrite.All`, `Directory.ReadWrite.All`, `Files.ReadWrite.All`, `Mail.Send`
- [ ] Admin consent granted for all Application permissions (green checkmarks visible)
- [ ] App Role created: `Admin` (value: `Admin`)
- [ ] Admin users assigned to the `Admin` app role via Enterprise Applications
- [ ] Optional claims configured: `email`, `preferred_username`, `upn`
- [ ] "Spaarke Demo Users" Entra security group created (record Object ID)

### Access Policies

- [ ] Conditional Access: ALL MFA-requiring policies updated with demo group exclusion
- [ ] Conditional Access: Verified via "What If" that demo users are not prompted for MFA
- [ ] Exchange Application Access Policy configured for BFF API to send from demo mailbox
- [ ] Exchange policy verified via `Test-ApplicationAccessPolicy` (result: Granted)

### BFF API Configuration

- [ ] License SKU IDs discovered via `.\scripts\Get-LicenseSkuIds.ps1`
- [ ] All `DemoProvisioning__*` app settings configured on App Service
- [ ] App Service restarted after settings change

### Dataverse Registration Components

- [ ] `sprk_registrationrequest` table created via `Create-RegistrationRequestSchema.ps1`
- [ ] Views created (Active, All, Approved, Rejected)
- [ ] Main form configured for admin review
- [ ] Sitemap entry added to model-driven app
- [ ] Ribbon buttons deployed (approve/reject actions)
- [ ] `sprk_/js/registrationribbon.js` web resource uploaded and published

### Website

- [ ] `BFF_API_URL` configured (host only, no `/api` suffix)
- [ ] Website deployed and accessible

### End-to-End Validation

- [ ] Submit a test registration request via the website
- [ ] Verify the request appears in Dataverse (Active Registration Requests view)
- [ ] Approve the request via the ribbon button
- [ ] Verify demo user account was created in Entra ID
- [ ] Verify user was added to "Spaarke Demo Users" group
- [ ] Verify licenses were assigned to the user
- [ ] Verify welcome email was sent
- [ ] Log in as the demo user and confirm access to the model-driven app
- [ ] Verify no MFA prompt during demo user login

---

## Troubleshooting

### Common Issues

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| 403 on approve/reject endpoint | User not assigned `Admin` app role | Assign role via Enterprise Applications > Users and groups |
| Provisioning fails with "Insufficient privileges" | Application permissions not granted or missing admin consent | Re-grant admin consent; verify green checkmarks |
| Demo user prompted for MFA | Conditional Access exclusion not applied to all policies | Check ALL MFA policies, not just the primary one |
| Email sending fails with 403 | Exchange Application Access Policy missing or not propagated | Verify with `Test-ApplicationAccessPolicy`; wait up to 30 min for propagation |
| License assignment fails | SKU ID incorrect or license unavailable in tenant | Re-run `Get-LicenseSkuIds.ps1`; ensure trial/license is active |
| Double-path errors (`/api/api/...`) | `BFF_API_URL` includes `/api` suffix | Remove `/api` -- use host only |
| User created but cannot access Dataverse | Team membership not assigned, or Business Unit missing | Verify "Spaarke Demo" Owner Team exists with correct security role |

### Useful Diagnostic Commands

```powershell
# Check if app permissions are granted
az ad app permission list --id "{bff-api-client-id}" --output table

# Check group membership
az ad group member list --group "Spaarke Demo Users" --output table

# Check assigned licenses for a user
az ad user show --id "user@demo.spaarke.com" --query "assignedLicenses"

# Check Exchange Application Access Policy
Connect-ExchangeOnline -UserPrincipalName admin@spaarke.com
Get-ApplicationAccessPolicy | Format-List
Test-ApplicationAccessPolicy -Identity "demo@demo.spaarke.com" -AppId "{bff-api-client-id}"
```
