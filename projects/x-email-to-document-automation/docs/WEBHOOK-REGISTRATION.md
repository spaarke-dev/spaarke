# Email-to-Document Webhook Registration Guide

This guide explains how to register the Dataverse webhook for automatic email-to-document conversion.

## Overview

The email-to-document automation uses a hybrid trigger architecture:
1. **Webhook (Primary)**: Triggered immediately when new emails are created via Server-Side Sync
2. **Polling Backup**: Catches any emails missed by the webhook (runs every 5 minutes)

This document covers the webhook registration.

---

## Option 1: PowerShell Script (Recommended)

### Prerequisites
- PowerShell 5.1 or later
- User with System Administrator or System Customizer role in Dataverse

### Steps

1. Open PowerShell as Administrator

2. Run the registration script:

```powershell
.\scripts\Register-EmailWebhook.ps1 `
    -DataverseUrl "https://spaarkedev1.crm.dynamics.com" `
    -WebhookUrl "https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/emails/webhook-trigger" `
    -WebhookSecret "your-secure-secret-here"
```

3. Sign in when prompted with your Dataverse credentials

4. Verify the output shows successful registration

### Script Parameters

| Parameter | Required | Description |
|-----------|----------|-------------|
| `DataverseUrl` | Yes | Your Dataverse environment URL |
| `WebhookUrl` | Yes | The BFF API webhook endpoint |
| `WebhookSecret` | Yes | Shared secret for HMAC-SHA256 signature validation |
| `ServiceEndpointName` | No | Custom name (default: "Email-to-Document Webhook") |
| `Force` | No | Recreate if already exists |

---

## Option 2: Manual Registration via Power Platform Admin

### Step 1: Register Service Endpoint

1. Navigate to **Power Platform Admin Center** > **Environments** > Select your environment

2. Go to **Settings** > **Customizations** > **Developer Resources**

3. Click **Plug-in Registration Tool** download link and run the tool

4. Connect to your Dataverse environment

5. Click **Register** > **Register New Web Hook**

6. Fill in the details:
   - **Name**: `Email-to-Document Webhook`
   - **Endpoint URL**: `https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/emails/webhook-trigger`
   - **Authentication Type**: `WebhookKey`
   - **Value**: `your-secure-secret-here` (same as EmailProcessing:WebhookSecret in BFF API)

7. Click **Register**

### Step 2: Register Webhook Step

1. In Plugin Registration Tool, expand the newly created webhook

2. Right-click and select **Register New Step**

3. Fill in the details:
   - **Message**: `Create`
   - **Primary Entity**: `email`
   - **Event Pipeline Stage of Execution**: `PostOperation`
   - **Execution Mode**: `Asynchronous`
   - **Name**: `Email-to-Document: Email Create`
   - **Description**: `Triggers email-to-document conversion when a new email is created`

4. Click **Register New Step**

---

## Option 3: Solution Export/Import (CI/CD)

For automated deployments, the webhook registration can be included in a Dataverse solution.

### Export Service Endpoint

The Service Endpoint is stored in the solution's `customizations.xml`:

```xml
<ServiceEndpoints>
  <ServiceEndpoint Name="Email-to-Document Webhook"
                   ServiceEndpointId="{guid}"
                   Contract="Webhook"
                   Url="https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/emails/webhook-trigger"
                   AuthType="WebhookKey"
                   AuthValue="[REPLACE_WITH_SECRET]">
    <Steps>
      <Step Name="Email-to-Document: Email Create"
            MessageName="Create"
            PrimaryEntity="email"
            Stage="PostOperation"
            Mode="Asynchronous" />
    </Steps>
  </ServiceEndpoint>
</ServiceEndpoints>
```

**Note**: The `AuthValue` should be replaced during deployment using environment variables or Key Vault.

---

## BFF API Configuration

After registering the webhook, configure the BFF API:

### Required Settings

Add to `appsettings.json` or environment variables:

```json
{
  "EmailProcessing": {
    "EnableWebhook": true,
    "WebhookSecret": "your-secure-secret-here",
    "DefaultContainerId": "your-spe-container-id",
    "AutoEnqueueAi": true
  }
}
```

### Environment Variables (Production)

```bash
EmailProcessing__EnableWebhook=true
EmailProcessing__WebhookSecret=@Microsoft.KeyVault(SecretUri=https://your-keyvault.vault.azure.net/secrets/email-webhook-secret)
EmailProcessing__DefaultContainerId=your-container-id
```

---

## Verification

### 1. Send a Test Email

- Send or receive an email in Dynamics 365 that will be synced via Server-Side Sync
- Wait for the email to appear in the Dataverse email entity

### 2. Check Webhook Execution

In Dataverse:
1. Go to **Settings** > **System** > **System Jobs**
2. Filter by **System Job Type** = `Webhook`
3. Look for recent jobs with name `Email-to-Document: Email Create`

### 3. Check BFF API Logs

Look for log entries:
```
Received webhook for email {EmailId}, Message=Create, CorrelationId={id}
Submitted job {JobId} for email {EmailId} with IdempotencyKey=Email:{EmailId}:Archive
```

### 4. Check Service Bus Queue

Verify that a job message was enqueued:
- Queue: `sdap-jobs`
- JobType: `ProcessEmailToDocument`

### 5. Check Document Creation

Query Dataverse for the created document:
```
GET /api/data/v9.2/sprk_documents?$filter=sprk_email eq {emailId}
```

---

## Troubleshooting

### Webhook Not Triggering

1. **Check Step is Enabled**: In Plugin Registration Tool, verify the step status is enabled
2. **Check Async Service**: Ensure the async service is running in Dataverse
3. **Check Endpoint URL**: Verify the URL is correct and publicly accessible
4. **Check SSL Certificate**: The endpoint must have a valid SSL certificate

### Signature Validation Failing

1. **Check Secret Match**: Ensure `WebhookSecret` matches in both Dataverse and BFF API
2. **Check Header Name**: Dataverse sends signature in `X-Dataverse-Signature` header
3. **Check Algorithm**: Must be HMAC-SHA256

### Webhook Returning Errors

1. **Check BFF API Logs**: Look for exception details
2. **Check Rate Limiting**: Webhook has its own rate limit policy
3. **Check Service Bus Connection**: Ensure Service Bus is connected

---

## Security Considerations

1. **Use Strong Secrets**: Generate a random 32+ character secret
2. **Rotate Secrets Regularly**: Update both Dataverse and BFF API
3. **Use HTTPS Only**: Never use HTTP for webhook endpoints
4. **Store Secrets Securely**: Use Azure Key Vault in production
5. **Monitor Failures**: Set up alerts for webhook failures

---

## Related Files

| File | Purpose |
|------|---------|
| `scripts/Register-EmailWebhook.ps1` | PowerShell registration script |
| `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` | Webhook endpoint implementation |
| `src/server/api/Sprk.Bff.Api/Configuration/EmailProcessingOptions.cs` | Configuration options |

---

*Last Updated: 2025-12-30*
