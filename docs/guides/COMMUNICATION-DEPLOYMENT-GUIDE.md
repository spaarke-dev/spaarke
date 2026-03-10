# Communication Service Deployment Guide - Release 2

> **Last Updated**: March 9, 2026
> **Purpose**: Complete deployment guide for the Email Communication Service R2 — covers BFF API, Dataverse configuration, Graph API setup, Server-Side Sync retirement, and multi-tenant deployment considerations.
> **Applies To**: Dev environment and higher (`spaarkedev1.crm.dynamics.com`, `spe-api-dev-67e2xz.azurewebsites.net`)
> **Release**: R2 — Full Graph-based infrastructure (replaces Server-Side Sync)

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Deployment Overview](#deployment-overview)
- [Phase 1-2: Core BFF API and Basic Configuration](#phase-1-2-core-bff-api-and-basic-configuration)
  - [Step 1: Build and Verify BFF API](#step-1-build-and-verify-bff-api)
  - [Step 2: Deploy BFF API to Azure App Service](#step-2-deploy-bff-api-to-azure-app-service)
  - [Step 3: Configure Approved Senders and Archive Storage](#step-3-configure-approved-senders-and-archive-storage)
  - [Step 4: Deploy Dataverse Solution and Web Resources](#step-4-deploy-dataverse-solution-and-web-resources)
  - [Step 5: Deploy Ribbon Configuration](#step-5-deploy-ribbon-configuration)
  - [Step 6: Verify Outbound Send](#step-6-verify-outbound-send)
- [Phase 3: Individual User Send (OBO Delegated Auth)](#phase-3-individual-user-send-obo-delegated-auth)
- [Phase 4-5: Inbound Monitoring and Document Archival](#phase-4-5-inbound-monitoring-and-document-archival)
  - [Step 7: Configure Webhook Notification URL](#step-7-configure-webhook-notification-url)
  - [Step 8: Seed Communication Account Records](#step-8-seed-communication-account-records)
  - [Step 9: Verify Graph Subscriptions](#step-9-verify-graph-subscriptions)
  - [Step 10: Verify Inbound and Archival](#step-10-verify-inbound-and-archival)
- [Phase 6: Server-Side Sync Retirement](#phase-6-server-side-sync-retirement)
- [Exchange Online Application Access Policy Setup](#exchange-online-application-access-policy-setup)
- [Graph API Permissions Required](#graph-api-permissions-required)
- [Multi-Tenant Deployment Considerations](#multi-tenant-deployment-considerations)
- [Rollback Procedures](#rollback-procedures)
- [Troubleshooting](#troubleshooting)
- [Background Services and Monitoring](#background-services-and-monitoring)

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

Release 2 deployment is organized into 6 phases. Each phase builds on the previous and can be deployed independently.

```
Phase 1-2: Core BFF API and Outbound (Foundation)
  1. Build & verify BFF API
  2. Deploy to Azure App Service
  3. Configure appsettings (approved senders, archive storage)
  4. Deploy Dataverse solution + web resources
  5. Deploy ribbon UI
  6. Verify outbound send works

Phase 3: Individual User Send (Optional, Parallel with Inbound)
  - Add OBO delegated permission to app registration
  - Users can select "Send as me" on Communication form
  - Requires user consent via OAuth flow

Phase 4-5: Inbound Monitoring and Archival (Replaces Server-Side Sync)
  - Configure webhook notification URL
  - Seed Communication Account records in Dataverse
  - GraphSubscriptionManager creates subscriptions automatically
  - Verify Graph webhooks + backup polling work
  - Verify email-to-document archival

Phase 6: Server-Side Sync Retirement
  - Disable mailbox configurations in Dataverse
  - Remove email router if using
  - Delete sprk_approvedsender and sprk_emailprocessingrule entities
  - Confirm no legacy email records being created

Dependency Graph:
  Phase 1-2 (foundation)
      ├→ Phase 3 (parallel with 4-5)
      └→ Phase 4-5 (depends on 1-2)
            └→ Phase 6 (depends on 4-5 verified working)
```

---

## Phase 1-2: Core BFF API and Basic Configuration

These steps deploy the foundation: BFF API, basic communication account support, and outbound send.

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

## Step 3: Configure Approved Senders and Archive Storage

### App Service Configuration

Set the Communication section in App Service application settings. These are fallback values used until Communication Accounts are created in Dataverse.

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

---

## Step 4: Deploy Dataverse Solution and Web Resources

### Import Dataverse Solution

The Communication solution (`CommunicationSolution`) includes:
- `sprk_communicationaccount` entity (if not already deployed)
- `sprk_communication` entity enhancements (inbound fields)
- `sprk_document` schema updates (new `sprk_communication` lookup)
- Forms and views for admin UX

```bash
# Import solution (with publish)
pac solution import \
  --path "artifacts/CommunicationSolution.zip" \
  --publish-changes
```

### Deploy Web Resource to Dataverse

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

The Send button on the `sprk_communication` entity form is defined in the `CommunicationRibbons` solution.

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

## Step 6: Verify Outbound Send

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

## Phase 3: Individual User Send (OBO Delegated Auth)

This phase is optional and can be deployed in parallel with Phase 4-5.

### Prerequisites

- Phase 1-2 complete (BFF API deployed)
- `Mail.Send` delegated permission added to app registration in Azure AD
- Users must have consented to the application (via OAuth popup or admin consent)

### Configuration

The system automatically supports OBO send once the delegated permission is granted. On the Communication form, users can select:
- **Send Mode: "Shared Mailbox"** (default) — sends from configured shared mailbox
- **Send Mode: "User"** — sends from their own mailbox (OBO)

### Verification

1. Create a test Communication record
2. Select **"User"** send mode
3. Click **Send**
4. Verify email received from user's mailbox address (not shared mailbox)
5. Check `sprk_from` field on the communication record — should be user's email

---

## Phase 4-5: Inbound Monitoring and Document Archival

These phases add Graph subscription monitoring, backup polling, and email-to-document archival. They replace Server-Side Sync entirely.

### Prerequisites

- Phase 1-2 complete (BFF API deployed, App Service configured)
- Dataverse solution deployed (`sprk_communicationaccount` entity exists)
- `Mail.Read` application permission granted to app registration (in progress via deployment)
- Exchange Application Access Policy configured (see section below)

### Step 7: Configure Webhook Notification URL

The `GraphSubscriptionManager` needs to know where to direct Graph webhook notifications.

```bash
az webapp config appsettings set \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --settings Communication__WebhookNotificationUrl="https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/incoming-webhook" \
                Communication__WebhookSecret="{random-secret-key}"
```

Replace `{random-secret-key}` with a random 32+ character string (used to validate webhook notifications from Graph).

### Step 8: Seed Communication Account Records

Create `sprk_communicationaccount` records in Dataverse for each mailbox:

1. Navigate to **Communication Accounts** in the model-driven app
2. Create a new record for the primary shared mailbox:
   - **Name**: "Central Mailbox"
   - **Email Address**: `mailbox-central@spaarke.com`
   - **Display Name**: "Spaarke Legal"
   - **Account Type**: Shared Account (100000000)
   - **Send Enabled**: Yes
   - **Is Default Sender**: Yes (only one account should be default)
   - **Receive Enabled**: Yes
   - **Monitor Folder**: (leave blank for Inbox)
   - **Auto Create Records**: Yes
   - **Archive Outgoing Opt In**: Yes
   - **Archive Incoming Opt In**: Yes
3. Save the record
4. Click **Verify** to test connectivity

For complete account configuration instructions, see [Communication Admin Guide - How to Add a New Communication Account](COMMUNICATION-ADMIN-GUIDE.md#how-to-add-a-new-communication-account).

### Step 9: Verify Graph Subscriptions

After the BFF API starts, the `GraphSubscriptionManager` automatically creates subscriptions on its first cycle (within 30 minutes of startup).

**Verify subscriptions**:
1. Open the `sprk_communicationaccount` record in Dataverse
2. Check that `sprk_subscriptionid` is populated (a GUID)
3. Check that `sprk_subscriptionexpiry` is set to a future date (within 3 days)

If fields are empty after 30 minutes, check BFF API logs for subscription creation errors:

```bash
az webapp log tail \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --filter "GraphSubscriptionManager"
```

### Step 10: Verify Inbound and Archival

#### Test Inbound Email

1. Send an email to `mailbox-central@spaarke.com` from an external email address
2. Wait up to 60 seconds for webhook notification (or up to 5 minutes if using backup polling)
3. Check **Communications** in Dataverse for a new record with:
   - **Direction**: Incoming (100000000)
   - **From**: Your external email address
   - **To**: `mailbox-central@spaarke.com`
   - **Subject**: Your test subject
   - **Status**: Draft (auto-created records start in draft)

#### Test Document Archival

1. Send an email with attachments to `mailbox-central@spaarke.com`
2. Wait for the communication record to be created (up to 60 seconds)
3. Check **Documents** for:
   - One document with the `.eml` file (email archive)
   - Child documents for each attachment (filtered per `AttachmentFilterService` rules)
4. Click the document to verify:
   - `sprk_communication` lookup points to the incoming communication record
   - Email metadata is populated (from, to, cc, date, subject)
   - File stored in SharePoint Embedded under `/communications/{communicationId}/`

#### Test Archival Opt-Out

1. Open the Communication Account record
2. Set **Archive Incoming Opt In** to **No**
3. Send another test email
4. Wait 60 seconds
5. Verify communication record is created but NO document archive is created (archival skipped)

---

## Phase 6: Server-Side Sync Retirement

Once Phase 4-5 is verified working, retire Server-Side Sync:

### Step 1: Disable Mailbox Configurations

In Dataverse:

```powershell
# Get all mailbox records and deactivate (don't delete to preserve audit trail)
Get-CrmRecords -conn $conn -EntityLogicalName "mailbox" | ForEach-Object {
    Set-CrmRecord -conn $conn -EntityLogicalName "mailbox" -Id $_.Id -Fields @{"statecode" = 1} # 1 = Inactive
}
```

### Step 2: Remove Email Router (if deployed)

If Exchange integrated email (Email Router) is deployed:

```powershell
# Stop email router service
Stop-Service -Name "MSCRMEmailRouter" -Force

# Disable automatic startup
Set-Service -Name "MSCRMEmailRouter" -StartupType Disabled

# (Optional) Uninstall email router service
# Refer to Microsoft Exchange Email Router uninstall documentation
```

### Step 3: Delete Legacy Entities

In the Communication solution, delete:
- `sprk_approvedsender` entity (if it exists)
- `sprk_emailprocessingrule` entity (if it exists)
- Any remaining email processing rules

Publish solution changes.

### Step 4: Confirm No Legacy Email Activities

Verify no new email activities are being created for communications:

```bash
# Query via Dataverse Web API
GET https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2/emails?$filter=createdoffset gt -1 days

# If records exist, check:
# - Are they from communication system? (check createdby)
# - Are they related to sprk_communication records or old email entity?
# Take action to prevent legacy creation
```

### Step 5: Update Documentation and Runbooks

- Remove Server-Side Sync troubleshooting from admin runbooks
- Update any automation that references email activities
- Archive legacy email processing documentation

---

## Graph API Permissions Required

### By Phase

| Permission | Type | Purpose | Phase |
|------------|------|---------|-------|
| `Mail.Send` | Application | Shared mailbox outbound email | Phase 1-2 |
| `Mail.Read` | Application | Shared mailbox inbound monitoring | Phase 4-5 |
| `Mail.Send` | Delegated | Individual user send (OBO) | Phase 3 |

### How to Grant Permissions

**Via Azure Portal**:

1. Go to **Azure AD** → **App Registrations** → Select your BFF API app registration
2. Click **API Permissions** → **Add a permission** → **Microsoft Graph**
3. For each permission:
   - Search for the permission (e.g., "Mail.Send")
   - Select **Application permissions** (for `Mail.Send`/`Mail.Read` app-only) or **Delegated permissions** (for `Mail.Send` delegated)
   - Check the box and click **Add permissions**
4. Click **Grant admin consent for [Tenant]** to grant consent

> **Important**: Application-level `Mail.Send` grants access to send as ANY mailbox in the tenant unless restricted by an Exchange Online Application Access Policy (see next section).

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
New-DistributionGroup -Name "SDAP Mailbox Access" -Type Security
```

> **Note**: If the group already exists, skip this step. You can verify with:
> ```powershell
> Get-DistributionGroup -Identity "SDAP Mailbox Access"
> ```

### Step 3: Add Mailbox to Security Group

Add the shared mailbox(es) that the BFF API should be allowed to send from:

```powershell
# Add the primary shared mailbox
Add-DistributionGroupMember -Identity "SDAP Mailbox Access" -Member "mailbox-central@spaarke.com"

# Add additional mailboxes as needed
# Add-DistributionGroupMember -Identity "SDAP Mailbox Access" -Member "noreply@spaarke.com"
```

Verify membership:

```powershell
Get-DistributionGroupMember -Identity "SDAP Mailbox Access" | Format-Table DisplayName, PrimarySmtpAddress
```

### Step 4: Create Application Access Policy

Restrict the BFF API app registration to only the mailboxes in the security group:

```powershell
New-ApplicationAccessPolicy `
  -AppId "{API_APP_ID}" `
  -PolicyScopeGroupId "SDAP Mailbox Access" `
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
   Add-DistributionGroupMember -Identity "SDAP Mailbox Access" -Member "new-mailbox@spaarke.com"
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

## Multi-Tenant Deployment Considerations

Release 2 is designed for multi-tenant deployment. When promoting to higher environments or other tenants:

### Configuration Checklist

| Setting | Dev Value | What to Change | Why |
|---------|-----------|-----------------|-----|
| `Communication__ApprovedSenders` | `mailbox-central@spaarke.com` | Environment-specific mailboxes | Sender list changes per tenant/environment |
| `Communication__WebhookNotificationUrl` | `https://spe-api-dev-67e2xz...` | Environment-specific webhook URL | BFF API URL differs per environment |
| `Communication__ArchiveContainerId` | Dev SPE container GUID | Production SPE container GUID | Documents archived to environment-specific container |
| `Communication__WebhookSecret` | Dev secret value | New random secret | Different per environment for security |
| `sprk_BffApiBaseUrl` environment variable | Dev BFF URL | Production BFF URL | PCF controls call BFF at different URLs per environment |

### No Tenant-Specific Hardcoding

The codebase must NOT contain:
- Hardcoded tenant IDs (use the claims in the token)
- Hardcoded environment URLs
- Hardcoded mailbox addresses (read from `sprk_communicationaccount`)
- Hardcoded SPE container IDs (read from config)

### Certificate/Secret Management

- **Webhook Secret**: Store in Azure Key Vault per environment; inject via App Service config
- **Graph Client Credentials**: Use managed identity (recommended) or Key Vault-stored secret
- **OBO Token Cache**: Redis automatically partitioned by tenant (via token claims)

### Solution Deployment

When promoting the Communication solution to higher environments:

1. Export solution from dev (unmanaged) or staging (managed)
2. Import to target environment
3. Manually seed `sprk_communicationaccount` records for target environment mailboxes
4. Run verification on each account to confirm connectivity
5. Test end-to-end send, receive, and archive

### Step-by-Step Multi-Environment Promotion

1. **Staging Environment**:
   - Deploy BFF API to staging App Service
   - Update App Service config with staging mailboxes, container IDs, URLs
   - Deploy Communication solution (managed version)
   - Seed accounts for staging mailboxes
   - Verify all 4 phases work (send, individual send, receive, archive)
   - Test with staging Graph subscription

2. **Production Environment**:
   - Deploy BFF API to production App Service
   - Update App Service config with production mailboxes, container IDs, URLs
   - Deploy Communication solution (managed version)
   - Seed accounts for production mailboxes (likely larger list)
   - Run verification on each account
   - Verify inbound before allowing end users to use
   - Monitor logs for any tenant-specific issues

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

## Background Services and Monitoring

The Communication Service includes two background services that run continuously in the BFF API:

| Service | Purpose | Interval | Health Checked |
|---------|---------|----------|----------------|
| `GraphSubscriptionManager` | Creates and renews Graph webhook subscriptions | Every 30 minutes | Yes (in `/healthz`) |
| `InboundPollingBackupService` | Queries for missed email via polling fallback | Every 5 minutes | Yes (in `/healthz`) |

### GraphSubscriptionManager Details

- **Type**: `BackgroundService`
- **Cycle**: Runs every 30 minutes (first run on startup)
- **What It Does**: Creates/renews Graph webhook subscriptions for receive-enabled accounts
- **Subscription Lifetime**: Up to 3 days (Graph API maximum)
- **Renewal Threshold**: Renews when < 24 hours remaining
- **Dataverse Updates**: Writes `sprk_subscriptionid`, `sprk_subscriptionexpiry`, `sprk_subscriptionstatus`
- **Error Handling**: Catches and logs errors per-account to avoid blocking others

### InboundPollingBackupService Details

- **Type**: `BackgroundService`
- **Cycle**: Runs every 5 minutes (after initial 1-minute startup delay)
- **What It Does**: Polls Graph for email received since last poll
- **Deduplication**: Skips messages already processed via `sprk_graphmessageid` check
- **Error Handling**: Catches and logs errors per-account

### Monitoring Background Services

```bash
# Check service health (included in API health check)
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz | jq '.services'

# Tail logs for subscription activity
az webapp log tail \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --filter "GraphSubscriptionManager"

# Tail logs for polling activity
az webapp log tail \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --filter "InboundPollingBackupService"
```

---

*Deployment guide for the Email Communication Solution R2. See also: [Admin Guide](COMMUNICATION-ADMIN-GUIDE.md) | [Architecture](../architecture/communication-service-architecture.md)*
