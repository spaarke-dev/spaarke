# Communication Service Deployment Guide

> **Last Updated**: February 22, 2026
> **Purpose**: Step-by-step deployment procedures for the Email Communication Service — BFF API, Dataverse web resource, ribbon configuration, and solution import.
> **Applies To**: Dev environment (`spaarkedev1.crm.dynamics.com`, `spe-api-dev-67e2xz.azurewebsites.net`)

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Deployment Overview](#deployment-overview)
- [Step 1: Build and Verify BFF API](#step-1-build-and-verify-bff-api)
- [Step 2: Deploy BFF API to Azure App Service](#step-2-deploy-bff-api-to-azure-app-service)
- [Step 3: Configure Communication Options](#step-3-configure-communication-options)
- [Step 4: Deploy Web Resource to Dataverse](#step-4-deploy-web-resource-to-dataverse)
- [Step 5: Deploy Ribbon Configuration](#step-5-deploy-ribbon-configuration)
- [Step 6: Verify End-to-End](#step-6-verify-end-to-end)
- [Exchange Online Application Access Policy Setup](#exchange-online-application-access-policy-setup)
- [Rollback Procedures](#rollback-procedures)
- [Troubleshooting](#troubleshooting)
- [Phase 6-9 API Endpoints](#phase-6-9-api-endpoints)
- [Phase 6-9 Deployment Sequence](#phase-6-9-deployment-sequence)
- [Phase 6-9 Background Services](#phase-6-9-background-services)
- [Environment-Specific Configuration](#environment-specific-configuration)

---

## Prerequisites

### Tools Required

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 8.0+ | Build BFF API |
| Azure CLI | Latest | Deploy to App Service |
| PAC CLI | Latest | Dataverse solution operations |
| PowerShell | 7.0+ | Script execution |

### Authentication

```bash
# Verify Azure CLI authentication
az account show

# Verify PAC CLI authentication
pac auth list

# If not authenticated:
az login
pac auth create --url https://spaarkedev1.crm.dynamics.com
```

### Critical Rules

- **MUST** deploy web resources BEFORE ribbon configuration (ribbon references web resources)
- **MUST** publish customizations after web resource and ribbon deployment
- **MUST** verify BFF API health check before testing send functionality
- **NEVER** deploy ribbon changes to a solution containing other components (use dedicated ribbon solution)
- **NEVER** remove the `Alt` attribute from ribbon Button elements (causes publish failure)

---

## Deployment Overview

```
Deployment Order (sequential — each step depends on the previous):

  1. Build & verify BFF API locally
     └─> dotnet build, dotnet test

  2. Deploy BFF API to Azure App Service
     └─> dotnet publish → zip → az webapp deploy

  3. Configure Communication options in App Service
     └─> az webapp config appsettings (approved senders, archive container)

  4. Deploy web resource to Dataverse
     └─> Upload sprk_communication_send.js → publish

  5. Deploy ribbon configuration
     └─> Export solution → edit customizations.xml → import → publish

  6. Verify end-to-end
     └─> Open Communication form → click Send → verify email received
```

---

## Step 1: Build and Verify BFF API

### Build

```bash
# From repository root
dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj

# Expected: Build succeeded. 0 Error(s). 0 Warning(s).
```

### Run Tests

```bash
dotnet test tests/

# Verify communication-specific tests pass
# Key test classes:
#   - CommunicationServiceTests
#   - ApprovedSenderValidatorTests
#   - EmlGenerationServiceTests
#   - CommunicationEndpointsTests
#   - CommunicationAuthorizationFilterTests
#   - SendCommunicationToolHandlerTests
```

### Local Verification (Optional)

```bash
# Run API locally
dotnet run --project src/server/api/Sprk.Bff.Api/

# Test health check
curl https://localhost:5001/healthz
# Expected: 200 OK
```

---

## Step 2: Deploy BFF API to Azure App Service

### Publish

```bash
# Create release build
dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj \
  --configuration Release \
  --output ./publish/api

# Create deployment ZIP
cd publish/api
zip -r ../../api-deploy.zip .
cd ../..
```

### Deploy

```bash
# Deploy to App Service
az webapp deploy \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --src-path api-deploy.zip \
  --type zip
```

### Verify

```bash
# Health check (wait 30-60 seconds for startup)
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: 200 OK

# Check App Service logs for startup errors
az webapp log tail \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz
```

---

## Step 3: Configure Communication Options

### App Service Configuration

Set the Communication section in App Service application settings:

```bash
# Set approved senders (JSON array)
az webapp config appsettings set \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --settings Communication__ApprovedSenders__0__Email="mailbox-central@spaarke.com" \
             Communication__ApprovedSenders__0__DisplayName="Spaarke Central" \
             Communication__ApprovedSenders__0__IsDefault="true" \
             Communication__ApprovedSenders__1__Email="noreply@spaarke.com" \
             Communication__ApprovedSenders__1__DisplayName="Spaarke Notifications" \
             Communication__ApprovedSenders__1__IsDefault="false"

# Set default mailbox
az webapp config appsettings set \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --settings Communication__DefaultMailbox="mailbox-central@spaarke.com"

# Set archive container ID (for SPE archival)
az webapp config appsettings set \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --settings Communication__ArchiveContainerId="{spe-container-drive-id}"
```

### Verify Configuration

```bash
# List communication settings
az webapp config appsettings list \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --query "[?starts_with(name, 'Communication')]" \
  --output table
```

### Graph API Permissions Required

Ensure the App Service managed identity (or app registration) has the following permissions, granted by phase:

| Permission | Type | Purpose | Phase |
|------------|------|---------|-------|
| `Mail.Send` | Application | Shared mailbox outbound email | Phase 1-6 |
| `Mail.Read` | Application | Shared mailbox inbound monitoring | Phase 8 |
| `Mail.Send` | Delegated | Individual user send (send-as-self) | Phase 7 |

**Phase 1 (minimum)**: Only `Mail.Send` (Application) is required.

```bash
# Verify Graph permissions via Azure Portal:
# Azure AD → App Registrations → {BFF App} → API Permissions
# Required: Microsoft Graph → Mail.Send (Application) — admin consent granted
```

> **Important**: Application-level `Mail.Send` grants access to send as ANY mailbox in the tenant unless restricted by an Exchange Online Application Access Policy. See [Exchange Online Application Access Policy Setup](#exchange-online-application-access-policy-setup) below.

---

## Step 4: Deploy Web Resource to Dataverse

### Option A: PAC CLI (Recommended)

```bash
# Push web resource directly
pac webresource push \
  --path "src/solutions/LegalWorkspace/src/WebResources/sprk_communication_send.js" \
  --name "sprk_communication_send" \
  --type "JScript"

# Publish customizations
pac solution publish
```

### Option B: Dataverse Web API

If PAC CLI push fails, use the Web API directly:

```bash
# Base64 encode the JavaScript file
$content = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes("src/solutions/LegalWorkspace/src/WebResources/sprk_communication_send.js"))

# Create web resource via Web API
$body = @{
  name = "sprk_communication_send"
  displayname = "Communication Send Button"
  description = "Web resource for the sprk_communication entity Send command bar button."
  webresourcetype = 3  # JScript
  content = $content
  languagecode = 1033
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2/webresourceset" `
  -Method Post `
  -Headers @{ Authorization = "Bearer $token"; "Content-Type" = "application/json" } `
  -Body $body
```

### Verify

```powershell
# Verify web resource exists in Dataverse
pac webresource list | Select-String "sprk_communication_send"
```

---

## Step 5: Deploy Ribbon Configuration

### Prerequisites

- Web resource `sprk_communication_send` MUST be deployed first (Step 4)
- A dedicated ribbon solution MUST exist (e.g., `CommunicationRibbons`)

### Create Dedicated Ribbon Solution (One-Time)

If `CommunicationRibbons` solution doesn't exist:

1. Go to **Power Apps maker portal** → **Solutions**
2. Click **New solution**
   - Display Name: `CommunicationRibbons`
   - Name: `CommunicationRibbons`
   - Publisher: **Spaarke** (default publisher)
   - Version: `1.0.0.0`
3. Add **existing** → **Table** → `sprk_communication` (metadata only, no subcomponents)
4. Add **existing** → **Web resource** → `sprk_communication_send`
5. **Publish** the solution

### Export Solution

```powershell
# Create temp directory
New-Item -ItemType Directory -Force -Path "infrastructure\dataverse\ribbon\temp"

# Export the dedicated ribbon solution
pac solution export `
  --name CommunicationRibbons `
  --path "infrastructure\dataverse\ribbon\temp\CommunicationRibbons.zip" `
  --managed false
```

### Extract and Edit

```powershell
# Extract the solution ZIP
Expand-Archive `
  -Path "infrastructure\dataverse\ribbon\temp\CommunicationRibbons.zip" `
  -DestinationPath "infrastructure\dataverse\ribbon\temp\CommunicationRibbons_extracted" `
  -Force
```

Edit `infrastructure\dataverse\ribbon\temp\CommunicationRibbons_extracted\customizations.xml`:

Locate the `<RibbonDiffXml>` section inside the `<Entity>` element for `sprk_Communication` and replace with:

```xml
<RibbonDiffXml>
  <CustomActions>
    <CustomAction Id="sprk.communication.send.CustomAction"
                  Location="Mscrm.Form.sprk_communication.MainTab.Actions.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="sprk.communication.send.Button"
                Command="sprk.communication.send.Command"
                LabelText="$LocLabels:sprk.communication.send.LabelText"
                Alt="$LocLabels:sprk.communication.send.Alt"
                ToolTipTitle="$LocLabels:sprk.communication.send.ToolTipTitle"
                ToolTipDescription="$LocLabels:sprk.communication.send.ToolTipDescription"
                TemplateAlias="o1"
                ModernImage="Send" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>
  <Templates>
    <RibbonTemplates Id="Mscrm.Templates"></RibbonTemplates>
  </Templates>
  <CommandDefinitions>
    <CommandDefinition Id="sprk.communication.send.Command">
      <EnableRules>
        <EnableRule Id="sprk.communication.isStatusDraft.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_communication_send"
                            FunctionName="Sprk.Communication.Send.sendCommunication">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>
  <RuleDefinitions>
    <TabDisplayRules />
    <DisplayRules />
    <EnableRules>
      <EnableRule Id="sprk.communication.isStatusDraft.EnableRule">
        <CustomRule Library="$webresource:sprk_communication_send"
                    FunctionName="Sprk.Communication.Send.isStatusDraft"
                    Default="false">
          <CrmParameter Value="PrimaryControl" />
        </CustomRule>
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>
  <LocLabels>
    <LocLabel Id="sprk.communication.send.LabelText">
      <Titles>
        <Title description="Send" languagecode="1033" />
      </Titles>
    </LocLabel>
    <LocLabel Id="sprk.communication.send.Alt">
      <Titles>
        <Title description="Send Communication" languagecode="1033" />
      </Titles>
    </LocLabel>
    <LocLabel Id="sprk.communication.send.ToolTipTitle">
      <Titles>
        <Title description="Send Communication" languagecode="1033" />
      </Titles>
    </LocLabel>
    <LocLabel Id="sprk.communication.send.ToolTipDescription">
      <Titles>
        <Title description="Send this communication via email" languagecode="1033" />
      </Titles>
    </LocLabel>
  </LocLabels>
</RibbonDiffXml>
```

### Repack and Import

```powershell
# Repack the solution
Compress-Archive `
  -Path "infrastructure\dataverse\ribbon\temp\CommunicationRibbons_extracted\*" `
  -DestinationPath "infrastructure\dataverse\ribbon\temp\CommunicationRibbons_modified.zip" `
  -Force

# Import with publish
pac solution import `
  --path "infrastructure\dataverse\ribbon\temp\CommunicationRibbons_modified.zip" `
  --publish-changes
```

### Verify Ribbon Deployment

```powershell
# Re-export to confirm ribbon persisted
pac solution export `
  --name CommunicationRibbons `
  --path "infrastructure\dataverse\ribbon\temp\CommunicationRibbons_verify.zip" `
  --managed false

Expand-Archive `
  -Path "infrastructure\dataverse\ribbon\temp\CommunicationRibbons_verify.zip" `
  -DestinationPath "infrastructure\dataverse\ribbon\temp\CommunicationRibbons_verify" `
  -Force

# Check customizations.xml contains the Send button
Select-String -Path "infrastructure\dataverse\ribbon\temp\CommunicationRibbons_verify\customizations.xml" `
  -Pattern "sprk.communication.send"
```

### Clean Up

```powershell
# Archive original export for rollback
New-Item -ItemType Directory -Force -Path "infrastructure\dataverse\ribbon\CommunicationRibbons"
Move-Item `
  "infrastructure\dataverse\ribbon\temp\CommunicationRibbons.zip" `
  "infrastructure\dataverse\ribbon\CommunicationRibbons\CommunicationRibbons_backup_$(Get-Date -Format 'yyyyMMdd_HHmmss').zip"

# Remove temp files
Remove-Item -Path "infrastructure\dataverse\ribbon\temp" -Recurse -Force
```

---

## Step 6: Verify End-to-End

### Checklist

- [ ] BFF API health check returns 200
- [ ] Web resource `sprk_communication_send` is published in Dataverse
- [ ] Send button is visible on `sprk_communication` main form command bar
- [ ] Send button is **enabled** when `statuscode = 1` (Draft)
- [ ] Send button is **disabled** when `statuscode` is any value other than Draft
- [ ] Click Send on a Draft record → email is received by recipients
- [ ] After send: `statuscode` updates to Send (659490002)
- [ ] After send: `sprk_sentat`, `sprk_from`, `sprk_graphmessageid` are populated
- [ ] After send: Send button becomes disabled (no longer Draft)
- [ ] Error scenario: Invalid sender → ProblemDetails error shown on form
- [ ] Error scenario: Missing required fields → validation warning shown

### Manual Test Procedure

1. Navigate to **Communications** entity in the model-driven app
2. Create a new Communication record
3. Fill in:
   - **To**: your test email address
   - **Subject**: "Test Communication"
   - **Body**: "This is a test email from the Communication Service."
4. Click the **Send** button in the command bar
5. Verify:
   - Progress notification appears: "Sending communication..."
   - Success notification appears: "Communication sent successfully."
   - Status changes to **Send**
   - Sent At field is populated
   - Send button becomes disabled
6. Check your email inbox for the test message

---

## Exchange Online Application Access Policy Setup

Application-level Graph permissions (e.g., `Mail.Send`) grant access to **all mailboxes** in the tenant by default. An **Application Access Policy** restricts the BFF API app registration to only the mailboxes it needs.

### Prerequisites

- **Exchange Online PowerShell** module installed (`Install-Module ExchangeOnlineManagement`)
- **Exchange Administrator** or **Global Administrator** role
- App registration Client ID (the `API_APP_ID` used by the BFF API)
- A mail-enabled security group in Exchange Online

### Step 1: Connect to Exchange Online

```powershell
# Install module (one-time)
Install-Module ExchangeOnlineManagement -Scope CurrentUser

# Connect (will prompt for admin credentials)
Connect-ExchangeOnline
```

### Step 2: Create Mail-Enabled Security Group

Create a security group that will contain all mailboxes the BFF API is allowed to access:

```powershell
New-DistributionGroup -Name "BFF-Mailbox-Access" -Type Security
```

> **Note**: If the group already exists, skip this step. You can verify with:
> ```powershell
> Get-DistributionGroup -Identity "BFF-Mailbox-Access"
> ```

### Step 3: Add Mailbox to Security Group

Add the shared mailbox(es) that the BFF API should be allowed to send from:

```powershell
# Add the primary shared mailbox
Add-DistributionGroupMember -Identity "BFF-Mailbox-Access" -Member "mailbox-central@spaarke.com"

# Add additional mailboxes as needed
# Add-DistributionGroupMember -Identity "BFF-Mailbox-Access" -Member "noreply@spaarke.com"
```

Verify membership:

```powershell
Get-DistributionGroupMember -Identity "BFF-Mailbox-Access" | Format-Table DisplayName, PrimarySmtpAddress
```

### Step 4: Create Application Access Policy

Restrict the BFF API app registration to only the mailboxes in the security group:

```powershell
New-ApplicationAccessPolicy `
  -AppId "{API_APP_ID}" `
  -PolicyScopeGroupId "BFF-Mailbox-Access" `
  -AccessRight RestrictAccess `
  -Description "Restrict BFF to approved mailboxes"
```

Replace `{API_APP_ID}` with the actual Client ID of the BFF API app registration.

### Step 5: Verify Policy

Test that the policy is correctly applied:

```powershell
# Should return "Granted" — mailbox is in the security group
Test-ApplicationAccessPolicy -Identity "mailbox-central@spaarke.com" -AppId "{API_APP_ID}"

# Should return "Denied" — random mailbox is NOT in the security group
Test-ApplicationAccessPolicy -Identity "random-user@spaarke.com" -AppId "{API_APP_ID}"
```

### Propagation Warning

> **Exchange Online policies can take up to 30 minutes to propagate.** After creating or modifying an Application Access Policy, wait at least 30 minutes before testing Graph API calls. During this propagation window, the BFF API may still be able to send from unrestricted mailboxes.
>
> If you need to verify propagation status, re-run `Test-ApplicationAccessPolicy` periodically until the expected results are returned.

### Adding New Mailboxes

To grant the BFF API access to a new shared mailbox:

1. **Add the mailbox to the Exchange security group**:
   ```powershell
   Add-DistributionGroupMember -Identity "BFF-Mailbox-Access" -Member "new-mailbox@spaarke.com"
   ```

2. **Create a `sprk_communicationaccount` record in Dataverse** for the new mailbox so it appears in the Communication form sender dropdown.

3. **Update `Communication__ApprovedSenders` in App Service** configuration (or appsettings.json) to include the new mailbox entry:
   ```bash
   az webapp config appsettings set \
     --resource-group spe-infrastructure-westus2 \
     --name spe-api-dev-67e2xz \
     --settings Communication__ApprovedSenders__N__Email="new-mailbox@spaarke.com" \
                Communication__ApprovedSenders__N__DisplayName="New Mailbox Display Name" \
                Communication__ApprovedSenders__N__IsDefault="false"
   ```
   Replace `N` with the next index number.

4. **Wait up to 30 minutes** for the Exchange policy to propagate before testing.

### Graph API Permissions by Phase

| Permission | Type | Purpose | Required From |
|------------|------|---------|---------------|
| `Mail.Send` | Application | Send email from shared mailboxes on behalf of the app | Phase 1 (Phases 1-6) |
| `Mail.Read` | Application | Monitor shared mailbox for inbound email processing | Phase 8 |
| `Mail.Send` | Delegated | Send email as the signed-in user (send-as-self) | Phase 7 |

**Phase 1-6**: Only `Mail.Send` (Application) is required. The Application Access Policy above restricts which mailboxes the app can use.

**Phase 7**: Adds `Mail.Send` (Delegated) for user-context sending. This does NOT require an Application Access Policy because the user authenticates with their own credentials.

**Phase 8**: Adds `Mail.Read` (Application) for inbound email monitoring. The same Application Access Policy restricts which mailboxes the app can read from.

### Disconnecting from Exchange Online

```powershell
Disconnect-ExchangeOnline -Confirm:$false
```

---

## Rollback Procedures

### Rollback BFF API

```bash
# Redeploy previous version
# Option 1: Use deployment slots (if configured)
az webapp deployment slot swap \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --slot staging

# Option 2: Redeploy from previous build artifact
az webapp deploy \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --src-path api-deploy-previous.zip \
  --type zip
```

### Rollback Ribbon

```powershell
# Import the backup solution
pac solution import `
  --path "infrastructure\dataverse\ribbon\CommunicationRibbons\CommunicationRibbons_backup_{timestamp}.zip" `
  --publish-changes
```

### Rollback Web Resource

```powershell
# Re-upload previous version of the web resource
pac webresource push `
  --path "path/to/previous/sprk_communication_send.js" `
  --name "sprk_communication_send" `
  --type "JScript"

pac solution publish
```

---

## Troubleshooting

### BFF API Issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| Health check returns 503 | App Service starting up | Wait 60 seconds and retry |
| Health check returns 500 | Configuration error | Check `az webapp log tail` for startup errors |
| Communication__ApprovedSenders not loaded | Wrong config key format | Use double-underscore notation for nested settings |
| Graph sendMail returns 403 | Missing Mail.Send permission | Grant `Mail.Send` application permission + admin consent |
| Graph sendMail returns 404 | Invalid sender mailbox | Verify mailbox exists in Azure AD / Exchange |

### Dataverse / Ribbon Issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| Send button not visible | Ribbon not published | Run `pac solution publish` |
| Send button always disabled | EnableRule JS error | Check browser console (F12) for errors |
| "Web resource not found" on import | Web resource not deployed | Deploy web resource first (Step 4) |
| Solution import fails with "duplicate ID" | Conflicting ribbon elements | Use unique `sprk.communication.` prefixed IDs |
| Button shows but click does nothing | JS function not found | Verify web resource name matches `$webresource:sprk_communication_send` |

### Network / Auth Issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| Fetch failed / network error | CORS not configured | Add Dataverse origin to BFF CORS settings |
| 401 Unauthorized from BFF | Token not passed | Check `_getAuthToken()` in web resource |
| "Unable to reach communication service" | BFF URL wrong | Verify `sprk_BffApiBaseUrl` environment variable |

---

## Phase 6-9 API Endpoints

Phases 6-9 add the following endpoints to the BFF API. These are in addition to the original Phase 1-5 endpoints (`POST /api/communications/send`, `POST /api/communications/send-bulk`, `GET /api/communications/{id}/status`).

| Endpoint | Method | Auth | Purpose | Phase |
|----------|--------|------|---------|-------|
| `/api/communications/send` | POST | Authenticated | Send email — now supports `sendMode` for shared or individual (OBO) | 7 (updated) |
| `/api/communications/incoming-webhook` | POST | Anonymous | Graph change notification webhook receiver | 8 |
| `/api/communications/accounts/{id}/verify` | POST | Authenticated | Run mailbox verification (test send/read) | 9 |

### Endpoint Details

**POST /api/communications/send** (Phase 7 Update)

The send endpoint now accepts an optional `sendMode` field in the request body:
- `SharedMailbox` (default): Sends from a shared mailbox via app-only auth
- `User`: Sends from the authenticated user's mailbox via OBO delegated auth

**POST /api/communications/incoming-webhook** (Phase 8)

Receives Graph subscription change notifications. This endpoint:
- Handles subscription validation requests (returns `validationToken` as plain text)
- Validates notifications via `clientState` HMAC
- Enqueues `IncomingCommunicationJob` for asynchronous processing
- Does NOT require authentication (Graph sends notifications without a bearer token)

**POST /api/communications/accounts/{id}/verify** (Phase 9)

Tests mailbox connectivity for a communication account:
- If send-enabled: sends a test email from the mailbox
- If receive-enabled: reads recent messages from the mailbox
- Updates `sprk_verificationstatus` and `sprk_lastverified` on the account record
- Returns verification result with details

---

## Phase 6-9 Deployment Sequence

After deploying the original Phases 1-5 components (Steps 1-6 above), follow this sequence for Phases 6-9.

### Step 7: Deploy BFF API with Phase 6-9 Services

Build and deploy the BFF API with all new services. Follow the same process as [Step 1](#step-1-build-and-verify-bff-api) and [Step 2](#step-2-deploy-bff-api-to-azure-app-service).

**New services included in the deployment**:

| Service | Type | Purpose |
|---------|------|---------|
| `CommunicationAccountService` | Singleton | Queries `sprk_communicationaccount` from Dataverse with Redis cache |
| `GraphSubscriptionManager` | BackgroundService | Creates/renews/recreates Graph webhook subscriptions |
| `InboundPollingBackupService` | BackgroundService | Polls for messages missed by webhooks |
| `MailboxVerificationService` | Singleton | Tests mailbox connectivity (send + read) |
| `IncomingCommunicationProcessor` | Job Handler | Processes incoming email notifications into `sprk_communication` records |

### Step 8: Seed Communication Account Records

Create `sprk_communicationaccount` records in Dataverse for each mailbox the BFF API should manage. At minimum, configure the primary shared mailbox:

1. Navigate to **Communication Accounts** in the model-driven app
2. Create a new record:
   - **Name**: "Central Mailbox"
   - **Email Address**: `mailbox-central@spaarke.com`
   - **Display Name**: "Spaarke Legal"
   - **Account Type**: Shared Account (100000000)
   - **Send Enableds** (`sprk_sendenableds`): Yes
   - **Is Default Sender** (`sprk_isdefaultsender`): Yes
   - **Receive Enabled** (`sprk_receiveenabled`): Yes
   - **Auto Create Records** (`sprk_autocreaterecords`): Yes
3. Save the record

For detailed instructions on creating and configuring accounts, see the [Communication Admin Guide](COMMUNICATION-ADMIN-GUIDE.md).

### Step 9: Configure Webhook URL

Set the webhook notification URL in App Service configuration so the `GraphSubscriptionManager` knows where to direct Graph subscriptions:

```bash
az webapp config appsettings set \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --settings Communication__WebhookNotificationUrl="https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/incoming-webhook"
```

### Step 10: Verify Graph Subscriptions

After the BFF API starts, the `GraphSubscriptionManager` automatically creates subscriptions on its first cycle (within 30 minutes of startup).

**Verify subscriptions are created**:
1. Open the `sprk_communicationaccount` record in Dataverse
2. Check that `sprk_subscriptionid` is populated (a GUID)
3. Check that `sprk_subscriptionexpiry` is set to a future date (up to 3 days out)

If these fields are empty after 30 minutes, check the BFF API logs for subscription creation errors.

### Step 11: Run Verification on Each Account

For each communication account, run the verification endpoint:

```bash
# Verify the primary shared mailbox
curl -X POST \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/accounts/{account-id}/verify" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json"
```

Verify that `sprk_verificationstatus` updates to **Verified** (100000000) on the account record.

### Step 12: End-to-End Verification (Phases 6-9)

#### Outbound Shared Mailbox (Phase 6)
- [ ] Communication account record exists for `mailbox-central@spaarke.com`
- [ ] `sprk_sendenableds = Yes` and `sprk_isdefaultsender = Yes`
- [ ] Send a test email via `POST /api/communications/send` — email delivered
- [ ] Sender resolved from `sprk_communicationaccount` (not just appsettings.json)

#### Individual User Send (Phase 7)
- [ ] Send with `sendMode: "User"` — email sent from user's own mailbox
- [ ] `sprk_from` shows user's email address on the communication record
- [ ] Send with `sendMode: "SharedMailbox"` still works as before

#### Inbound Monitoring (Phase 8)
- [ ] `sprk_subscriptionid` populated on receive-enabled accounts
- [ ] Send an email TO `mailbox-central@spaarke.com` from an external address
- [ ] `sprk_communication` record created with `sprk_direction` = Incoming (100000000)
- [ ] Incoming record has `sprk_to`, `sprk_from`, `sprk_subject`, `sprk_body` populated
- [ ] `sprk_graphmessageid` populated on the incoming record
- [ ] Regarding fields are empty (association resolution is out of scope)

#### Verification (Phase 9)
- [ ] `POST /api/communications/accounts/{id}/verify` returns success
- [ ] `sprk_verificationstatus` = Verified (100000000)
- [ ] `sprk_lastverified` updated to current timestamp

---

## Phase 6-9 Background Services

### GraphSubscriptionManager

| Property | Value |
|----------|-------|
| Type | `BackgroundService` (hosted service) |
| Cycle Interval | 30 minutes |
| Startup Behavior | Runs immediately on startup, then every 30 minutes |
| What It Does | Creates, renews, and recreates Graph webhook subscriptions for receive-enabled accounts |
| Subscription Lifetime | Up to 3 days (Graph API maximum for mail) |
| Renewal Threshold | Renews when expiry < 24 hours |
| Dataverse Updates | Writes `sprk_subscriptionid` and `sprk_subscriptionexpiry` on account records |
| Health Check | Included in `/healthz` response |

### InboundPollingBackupService

| Property | Value |
|----------|-------|
| Type | `BackgroundService` (hosted service) |
| Cycle Interval | 5 minutes |
| Startup Behavior | Starts after initial delay, then every 5 minutes |
| What It Does | Polls Graph for messages since last poll for each receive-enabled account |
| Deduplication | Checks `sprk_graphmessageid` on existing records to skip already-tracked messages |
| Resilience | Catches all exceptions per-account to avoid one failure blocking others |
| Health Check | Included in `/healthz` response |

### Monitoring

Both background services log to the standard BFF API logging pipeline. Monitor via:

```bash
# Tail logs for subscription and polling activity
az webapp log tail \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --filter "GraphSubscriptionManager|InboundPollingBackupService"
```

---

## Environment-Specific Configuration

### Dev Environment

| Setting | Value |
|---------|-------|
| BFF API URL | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Dataverse URL | `https://spaarkedev1.crm.dynamics.com` |
| Resource Group | `spe-infrastructure-westus2` |
| App Service | `spe-api-dev-67e2xz` |
| Environment Variable | `sprk_BffApiBaseUrl` |

### Promoting to Higher Environments

When promoting to staging/production:

1. Update `Communication__ApprovedSenders` with environment-specific mailboxes
2. Update `Communication__ArchiveContainerId` with environment SPE container
3. Update `Communication__WebhookNotificationUrl` with environment-specific webhook URL
4. Update `sprk_BffApiBaseUrl` Dataverse environment variable
5. Verify Graph API permissions on target app registration (`Mail.Send` Application, `Mail.Read` Application, `Mail.Send` Delegated)
6. Deploy web resource to target Dataverse environment
7. Deploy ribbon solution to target Dataverse environment
8. Seed `sprk_communicationaccount` records for environment-specific mailboxes
9. Configure Exchange Application Access Policy for the target tenant
10. Wait 30 minutes for Exchange policy propagation
11. Run verification on each account (`POST /api/communications/accounts/{id}/verify`)
12. Run end-to-end verification checklist (Phases 1-5 + Phases 6-9)

---

*Deployment guide for the Email Communication Solution R1. See also: [Architecture](../architecture/communication-service-architecture.md) | [User Guide](communication-user-guide.md) | [Admin Guide](COMMUNICATION-ADMIN-GUIDE.md)*
