# Communication Account Administration Guide

> **Last Updated**: February 22, 2026
> **Purpose**: Step-by-step procedures for managing communication accounts in Dataverse — adding new mailboxes, configuring send/receive settings, running verification, and troubleshooting.
> **Applies To**: Dev environment (`spaarkedev1.crm.dynamics.com`, `spe-api-dev-67e2xz.azurewebsites.net`)

---

## Table of Contents

- [Overview](#overview)
- [How to Add a New Communication Account](#how-to-add-a-new-communication-account)
- [Account Types](#account-types)
- [Field Reference](#field-reference)
- [Verification Status](#verification-status)
- [Subscription Monitoring](#subscription-monitoring)
- [Security Configuration](#security-configuration)
- [Common Scenarios](#common-scenarios)
- [Troubleshooting](#troubleshooting)

---

## Overview

Communication accounts (`sprk_communicationaccount`) are the central configuration point for all email communication in Spaarke. Each account represents a mailbox that the BFF API can send from, receive to, or both. Accounts are managed entirely through the Dataverse model-driven app UI.

**Key Concepts**:
- **Send-enabled accounts** appear in the sender dropdown when composing communications
- **Receive-enabled accounts** are monitored for incoming email via Graph subscription webhooks
- **Account type** determines the authentication method (app-only vs. OBO delegated)
- The BFF queries `sprk_communicationaccount` records at runtime with a 5-minute Redis cache

---

## How to Add a New Communication Account

### Step 1: Create the Account Record in Dataverse

Navigate to the **Communication Accounts** entity in the model-driven app and create a new record.

**Required Fields**:

| Field | Logical Name | What to Enter |
|-------|-------------|---------------|
| Name | `sprk_name` | Human-readable name (e.g., "Central Mailbox", "Billing Department") |
| Email Address | `sprk_emailaddress` | Mailbox email address (e.g., `mailbox-central@spaarke.com`) |
| Display Name | `sprk_displayname` | From header display name (e.g., "Spaarke Legal") |
| Account Type | `sprk_accounttype` | Select: Shared Account, Service Account, or User Account |

**Send Configuration**:

| Field | Logical Name | Notes |
|-------|-------------|-------|
| Send Enableds | `sprk_sendenableds` | Set to **Yes** to allow outbound sending. Note the trailing 's' in the field name. |
| Is Default Sender | `sprk_isdefaultsender` | Set to **Yes** if this is the fallback sender. Only ONE account should be the default. |

**Receive Configuration**:

| Field | Logical Name | Notes |
|-------|-------------|-------|
| Receive Enabled | `sprk_receiveenabled` | Set to **Yes** to monitor this mailbox for incoming email |
| Monitor Folder | `sprk_monitorfolder` | Folder to watch (default: "Inbox"). Leave blank for Inbox. |
| Auto Create Records | `sprk_autocreaterecords` | Set to **Yes** to auto-create `sprk_communication` records for incoming messages |

**Security Configuration**:

| Field | Logical Name | Notes |
|-------|-------------|-------|
| Security Group Id | `sprk_securitygroupid` | Azure AD security group GUID that scopes Exchange access for this mailbox |
| Security Group Name | `sprk_securitygroupname` | Display name of the security group (informational) |

Save the record.

### Step 2: Configure Exchange Application Access Policy

If this is a new mailbox that the BFF API has not previously been granted access to, you must add it to the Exchange Application Access Policy security group.

> **Skip this step** if the mailbox is already a member of the "BFF-Mailbox-Access" security group.

```powershell
# Connect to Exchange Online
Connect-ExchangeOnline

# Add mailbox to the security group
Add-DistributionGroupMember -Identity "BFF-Mailbox-Access" -Member "new-mailbox@spaarke.com"

# Verify membership
Get-DistributionGroupMember -Identity "BFF-Mailbox-Access" | Format-Table DisplayName, PrimarySmtpAddress

# Disconnect
Disconnect-ExchangeOnline -Confirm:$false
```

**Wait at least 30 minutes** for Exchange Online policy propagation before proceeding.

For full Exchange Application Access Policy setup instructions (including creating the security group and policy from scratch), see [COMMUNICATION-DEPLOYMENT-GUIDE.md](COMMUNICATION-DEPLOYMENT-GUIDE.md#exchange-online-application-access-policy-setup).

### Step 3: Run Mailbox Verification

After the Exchange policy has propagated, verify the account's connectivity:

```bash
# Run verification via BFF API
curl -X POST \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/accounts/{account-id}/verify" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json"
```

Replace `{account-id}` with the `sprk_communicationaccountid` GUID from the Dataverse record.

**What verification tests**:
- If send-enabled: sends a test email from the mailbox
- If receive-enabled: reads recent messages from the mailbox

The endpoint updates the following fields on the account record:
- `sprk_verificationstatus` — Verified (100000000), Failed (100000001), or Pending (100000002)
- `sprk_lastverified` — Timestamp of last verification attempt

You can also click the **Verify** button on the Communication Account form in Dataverse.

### Step 4: Monitor Subscription Status (Receive-Enabled Only)

For receive-enabled accounts, the `GraphSubscriptionManager` background service automatically:
- Creates a Graph webhook subscription on startup
- Renews subscriptions every 30 minutes (when expiry < 24 hours)
- Recreates subscriptions that fail or expire

The following fields are auto-populated by the system:

| Field | Logical Name | Description |
|-------|-------------|-------------|
| Subscription Id | `sprk_subscriptionid` | Graph webhook subscription GUID (system-managed) |
| Subscription Expiry | `sprk_subscriptionexpiry` | When the subscription needs renewal (system-managed) |

**No manual action is required** for subscription management. If a subscription fails, the `InboundPollingBackupService` polls every 5 minutes as a fallback.

---

## Account Types

| Value | Label | Auth Method | When to Use |
|-------|-------|-------------|-------------|
| 100000000 | Shared Account | App-only (`GraphClientFactory.ForApp()`) | Most common. Exchange shared mailbox. No license required. Used for firm-wide outbound and inbound monitoring. |
| 100000001 | Service Account | App-only (`GraphClientFactory.ForApp()`) | Dedicated licensed service mailbox. Same auth as Shared Account but has its own Exchange license. |
| 100000002 | User Account | OBO delegated (`GraphClientFactory.ForUserAsync()`) | Individual user's mailbox. User must authenticate and consent. Used for "Send as me" functionality. |

**Choosing the Right Type**:
- For **firm-wide outbound email** (notifications, correspondence): Use **Shared Account**
- For **department-specific mailbox** with its own license: Use **Service Account**
- For **individual user sending** (personal follow-ups): Use **User Account**

---

## Field Reference

### Complete `sprk_communicationaccount` Field List

**Core Identity**:

| Display Name | Logical Name | Type | Required |
|-------------|-------------|------|----------|
| Name | `sprk_name` | Text (850) | Yes |
| Email Address | `sprk_emailaddress` | Text (100) | Yes |
| Display Name | `sprk_displayname` | Text (100) | Yes |
| Account Type | `sprk_accounttype` | Choice | Yes |

**Outbound Configuration**:

| Display Name | Logical Name | Type | Default |
|-------------|-------------|------|---------|
| Send Enableds | `sprk_sendenableds` | Yes/No | No |
| Is Default Sender | `sprk_isdefaultsender` | Yes/No | No |

**Inbound Configuration**:

| Display Name | Logical Name | Type | Default |
|-------------|-------------|------|---------|
| Receive Enabled | `sprk_receiveenabled` | Yes/No | No |
| Monitor Folder | `sprk_monitorfolder` | Text (100) | (blank = Inbox) |
| Auto Create Records | `sprk_autocreaterecords` | Yes/No | No |

**Graph Integration (System-Managed — Do Not Edit)**:

| Display Name | Logical Name | Type |
|-------------|-------------|------|
| Subscription Id | `sprk_subscriptionid` | Text (100) |
| Subscription Expiry | `sprk_subscriptionexpiry` | DateTime |

**Security**:

| Display Name | Logical Name | Type |
|-------------|-------------|------|
| Security Group Id | `sprk_securitygroupid` | Text (100) |
| Security Group Name | `sprk_securitygroupname` | Text (100) |

**Verification (System-Managed)**:

| Display Name | Logical Name | Type |
|-------------|-------------|------|
| Verification Status | `sprk_verificationstatus` | Choice |
| Last Verified | `sprk_lastverified` | DateTime |

> **Important field name note**: The send-enabled field is `sprk_sendenableds` (with a trailing 's'). This is the actual Dataverse logical name. Do not confuse with `sprk_sendenabled` (without the 's').

---

## Verification Status

| Value | Label | Meaning |
|-------|-------|---------|
| 100000000 | Verified | Account connectivity confirmed — send and/or read access works |
| 100000001 | Failed | Verification failed — check permissions and Exchange configuration |
| 100000002 | Pending | Verification in progress or not yet attempted |

**When to Re-verify**:
- After adding a new mailbox to the Exchange security group
- After changing Graph API permissions on the app registration
- After modifying Exchange Application Access Policy
- If subscription creation is failing for a receive-enabled account
- Periodically as part of admin health checks

---

## Subscription Monitoring

### How Graph Subscriptions Work

1. `GraphSubscriptionManager` runs as a `BackgroundService` in the BFF API
2. On startup and every 30 minutes, it queries all receive-enabled accounts
3. For each account without a valid subscription, it creates a new Graph webhook subscription
4. Subscriptions expire after 3 days (Graph API maximum for mail)
5. The manager renews subscriptions when expiry is less than 24 hours away
6. If renewal fails, the subscription is recreated from scratch

### Checking Subscription Health

Open the Communication Account record in Dataverse and check:
- **Subscription Id** (`sprk_subscriptionid`): Should be populated for receive-enabled accounts
- **Subscription Expiry** (`sprk_subscriptionexpiry`): Should be in the future (within 3 days)

If both fields are empty for a receive-enabled account, the `GraphSubscriptionManager` has not yet processed it or is encountering errors. Check the BFF API logs.

### Backup Polling

The `InboundPollingBackupService` runs every 5 minutes as a safety net. For each receive-enabled account, it:
1. Queries Graph for messages since the last poll
2. Skips messages already tracked (checks `sprk_graphmessageid` on existing `sprk_communication` records)
3. Enqueues any missed messages for processing

This ensures no incoming email is lost even if the webhook subscription temporarily fails.

---

## Security Configuration

### Per-Account Security Groups

Each communication account can reference its own Exchange security group via:
- `sprk_securitygroupid` — The Azure AD group GUID
- `sprk_securitygroupname` — Display name (informational only)

This allows different accounts to be scoped to different security groups if needed. However, the most common pattern is a single "BFF-Mailbox-Access" security group containing all mailboxes.

### Graph API Permissions Required

| Permission | Type | Purpose | Required When |
|------------|------|---------|---------------|
| `Mail.Send` | Application | Send email from shared/service mailboxes | Account has `sprk_sendenableds = Yes` and type is Shared or Service |
| `Mail.Read` | Application | Monitor mailbox for incoming email | Account has `sprk_receiveenabled = Yes` |
| `Mail.Send` | Delegated | Send email as the authenticated user | Account type is User Account (OBO flow) |

### Exchange Application Access Policy

The Application Access Policy restricts which mailboxes the BFF API app registration can access. Without this policy, application-level permissions (`Mail.Send`, `Mail.Read`) grant access to ALL mailboxes in the tenant.

See [COMMUNICATION-DEPLOYMENT-GUIDE.md](COMMUNICATION-DEPLOYMENT-GUIDE.md#exchange-online-application-access-policy-setup) for complete setup instructions.

---

## Common Scenarios

### Scenario 1: Add a New Shared Mailbox for Outbound Only

1. Create Exchange shared mailbox in Azure AD (or use existing)
2. Add mailbox to "BFF-Mailbox-Access" security group
3. Wait 30 minutes for Exchange policy propagation
4. Create `sprk_communicationaccount` record:
   - Account Type: Shared Account (100000000)
   - `sprk_sendenableds`: Yes
   - `sprk_receiveenabled`: No
   - `sprk_isdefaultsender`: No (unless replacing the default)
5. Run verification: `POST /api/communications/accounts/{id}/verify`

### Scenario 2: Enable Inbound Monitoring on an Existing Account

1. Open the existing `sprk_communicationaccount` record
2. Set `sprk_receiveenabled` to Yes
3. Set `sprk_autocreaterecords` to Yes
4. Optionally set `sprk_monitorfolder` (default: Inbox)
5. Save the record
6. Wait for the next `GraphSubscriptionManager` cycle (up to 30 minutes)
7. Verify `sprk_subscriptionid` is populated on the record
8. Run verification: `POST /api/communications/accounts/{id}/verify`

### Scenario 3: Configure Individual User Send (OBO)

1. Ensure `Mail.Send` delegated permission is configured on the app registration
2. User must have consented to the application (admin consent or individual consent)
3. Create `sprk_communicationaccount` record:
   - Account Type: User Account (100000002)
   - `sprk_sendenableds`: Yes
   - `sprk_receiveenabled`: No (individual inbound is out of scope)
   - `sprk_isdefaultsender`: No
4. User selects "My Mailbox" send mode on the Communication form
5. BFF uses OBO token to send via `/me/sendMail`

### Scenario 4: Decommission a Communication Account

1. Open the `sprk_communicationaccount` record
2. Set `sprk_sendenableds` to No
3. Set `sprk_receiveenabled` to No
4. The `GraphSubscriptionManager` will stop renewing the webhook subscription
5. Subscription will expire naturally after up to 3 days
6. Optionally deactivate the record (set `statecode` to Inactive)
7. Remove mailbox from "BFF-Mailbox-Access" security group if no longer needed

---

## Troubleshooting

### Subscription Not Creating

**Symptom**: `sprk_subscriptionid` remains empty for a receive-enabled account.

**Possible Causes and Fixes**:

| Cause | Fix |
|-------|-----|
| Missing `Mail.Read` application permission | Grant `Mail.Read` application permission on the app registration + admin consent |
| Exchange Application Access Policy blocking read access | Add mailbox to security group; wait 30 minutes |
| BFF API not running or unhealthy | Check `GET /healthz`; review App Service logs |
| Webhook URL not configured | Set `WebhookNotificationUrl` in `appsettings.json` or App Service config |
| `sprk_receiveenabled` not set to Yes | Update the account record |

### Verification Failing

**Symptom**: `sprk_verificationstatus` = Failed (100000001) after running verify.

**Possible Causes and Fixes**:

| Cause | Fix |
|-------|-----|
| Mailbox not in Exchange security group | Add to "BFF-Mailbox-Access" group; wait 30 minutes |
| Exchange policy not yet propagated | Wait 30 minutes; re-run verification |
| Mailbox does not exist in Azure AD | Verify mailbox exists: `Get-Mailbox -Identity "address@domain.com"` |
| App registration missing permissions | Check `Mail.Send` (Application) and `Mail.Read` (Application) in Azure AD |

### Backup Polling Not Running

**Symptom**: Incoming emails not being processed even when webhook subscription is down.

**Possible Causes and Fixes**:

| Cause | Fix |
|-------|-----|
| `InboundPollingBackupService` not started | Check BFF API health via `/healthz`; review startup logs |
| Account not receive-enabled | Set `sprk_receiveenabled` to Yes |
| Redis cache stale | Clear Redis cache; service will re-query on next cycle |

### OBO Token Errors (Individual User Send)

**Symptom**: "Send as me" fails with 401/403 from Graph API.

**Possible Causes and Fixes**:

| Cause | Fix |
|-------|-----|
| User has not consented to the application | Trigger consent flow or use admin consent |
| Delegated `Mail.Send` permission not configured | Add to app registration; ensure admin consent |
| Token expired or cache miss | Token cache TTL is 55 minutes; user may need to re-authenticate |
| Account type not set to User Account | Verify `sprk_accounttype` = 100000002 (User Account) |

### Send Failing for Shared Mailbox

**Symptom**: Outbound send returns 403 or error for a shared/service account.

**Possible Causes and Fixes**:

| Cause | Fix |
|-------|-----|
| Missing `Mail.Send` application permission | Grant permission + admin consent |
| Mailbox not in Exchange security group | Add to "BFF-Mailbox-Access" group; wait 30 minutes |
| `sprk_sendenableds` not set to Yes | Update the account record |
| Approved sender configuration not found | Ensure account record is active and `sprk_sendenableds = Yes` |
| BFF Redis cache stale (5-min TTL) | Wait for cache expiry or restart API |

---

*Admin guide for Communication Account management. See also: [Deployment Guide](COMMUNICATION-DEPLOYMENT-GUIDE.md) | [User Guide](communication-user-guide.md) | [Data Schema](../data-model/sprk_communication-data-schema.md)*
