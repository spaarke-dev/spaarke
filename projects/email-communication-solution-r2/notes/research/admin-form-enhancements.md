# Admin Form Enhancements: `sprk_communicationaccount` Main Form

> **Task**: ECS-042
> **Phase**: 5 - Verification and Admin UX (Phase D)
> **Date**: 2026-03-09
> **Status**: Documentation (form enhancement guidance)

---

## Overview

The `sprk_communicationaccount` main form requires enhancements to provide administrators with visibility and control over communication account verification, send quotas, and subscription status. This document provides detailed guidance for implementing these form enhancements in Dataverse.

**Key Principle**: Since form customization is performed in Dataverse UI rather than code, this document serves as:
1. A specification for the form layout and field organization
2. A reference for the back-end endpoints that support form functionality
3. A guide for any web resources or plugins that extend form behavior

---

## Form Enhancement Overview

The main form for `sprk_communicationaccount` will be enhanced with **four new sections** that provide:
- Real-time verification status and verification capability
- Send quota tracking and daily limits
- Subscription status for receive-enabled accounts
- System-managed field visibility for reference

### Form Tab Structure

```
┌─ General (existing tab, unchanged)
├─ Settings (new tab for admin features)
│  ├─ Verification Section (new)
│  ├─ Send Quota Section (new)
│  ├─ Subscription Section (new - conditionally visible)
│  └─ System Fields Section (new)
└─ Other tabs (unchanged)
```

**Rationale**: Group all admin/verification features in a dedicated "Settings" tab to avoid cluttering the main form while keeping all management tools in one location.

---

## Section 1: Verification Status (New Section)

### Purpose
Display mailbox verification status and provide administrators with a button to verify send/read access.

### Fields to Display (Read-Only Unless Noted)

| Field Name | Schema Name | Type | Display Mode | Purpose |
|------------|------------|------|--------------|---------|
| Verification Status | `sprk_verificationstatus` | OptionSet | Read-only | Current verification state (Unverified, Verified, Failed, Expired) |
| Last Verified | `sprk_lastverified` | DateTime | Read-only | Timestamp of most recent verification attempt |
| Verification Message | `sprk_verificationmessage` | Text (multiline) | Read-only | Human-readable status message (error details, expiration info) |

### Layout Recommendation

```
┌─ Verification Section ────────────────────────────────┐
│                                                       │
│  Verification Status: [Verified ✓]                   │
│  Last Verified:      [2026-03-08 14:32:00 UTC]       │
│  Verification Msg:   [Multi-line text area]          │
│                      Account verified successfully.   │
│                      Permissions verified:            │
│                      - Mail.Send: ✓                   │
│                      - Mail.Read: ✓                   │
│                                                       │
│  [Verify Account] (command button)                   │
│                                                       │
└─────────────────────────────────────────────────────┘
```

### Field Configuration Details

#### `sprk_verificationstatus` (OptionSet)
- **Possible Values**:
  - `100000` = Unverified (initial state)
  - `100001` = Verified (all permissions confirmed)
  - `100002` = Failed (verification failed, see message)
  - `100003` = Expired (previous verification > 30 days old)

- **Form Display**: Use status badges or color-coded icons if available in form designer
- **Read-only**: Yes (updated only by verification endpoint)
- **Required**: No (defaults to Unverified)

#### `sprk_lastverified` (DateTime - UTC)
- **Format**: Display as "YYYY-MM-DD HH:MM:SS UTC"
- **Read-only**: Yes (set by verification endpoint)
- **Required**: No (null until first verification)
- **Timezone**: Always store in UTC; display in user's timezone with "UTC" label appended

#### `sprk_verificationmessage` (Text - Multiline)
- **Rows**: 5-8 lines
- **Read-only**: Yes (updated only by verification endpoint)
- **Required**: No (empty until first verification)
- **Format**: Plain text, may include:
  - Success confirmation with permission list
  - Error messages (e.g., "Insufficient permissions: Mail.Send not granted")
  - Expiration warnings (e.g., "Last verified 35 days ago — refresh recommended")

---

## Section 2: Verify Button (Web Resource Command)

### Purpose
Allow administrators to manually trigger account verification without leaving the form.

### Button Behavior

**Display**:
- Label: "Verify Account" or "Verify Now"
- Icon: Checkmark or shield icon
- Tooltip: "Test send and read permissions for this mailbox"

**Visibility Rules**:
- Always visible (all account types)
- Enabled: When form is not in create mode (account must exist first)

### Endpoint Called

```
POST /api/communications/accounts/{accountId}/verify
Authorization: Bearer {userToken}
Content-Type: application/json

Request Body:
{
  "checkSend": true,
  "checkRead": true
}

Response (200 OK):
{
  "accountId": "{guid}",
  "verificationStatus": "Verified",
  "permissionsSummary": {
    "mailSend": true,
    "mailRead": true
  },
  "message": "Account verified successfully. Permissions verified: Mail.Send ✓, Mail.Read ✓",
  "verifiedAt": "2026-03-08T14:32:00Z"
}

Response (400 Bad Request):
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Verification failed: Mail.Send permission not granted to service principal",
  "instance": "/api/communications/accounts/{accountId}/verify"
}
```

### Implementation Options

#### Option A: Ribbon Command (Recommended)
- **Where**: Command bar at top of form
- **Type**: Ribbon button or custom control action
- **Behavior**:
  1. User clicks "Verify Account" button
  2. JavaScript calls the verification endpoint
  3. Show loading indicator during request
  4. On success: Refresh form, show toast notification
  5. On failure: Display error dialog, log to console

#### Option B: Custom Web Resource Button
- **Location**: Within the Verification Section
- **Type**: Custom HTML5 web resource
- **Benefits**:
  - Embedded in form context
  - More granular control over styling and behavior
  - Can show real-time status updates

**Recommended**: Option A (ribbon command) for simplicity and consistency with Dataverse UI patterns.

### Web Resource Implementation (if Option B chosen)

**File Location**: `src/solutions/CommunicationAccounts/WebResources/VerifyAccountButton.html`

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>Verify Account Button</title>
    <script src="ClientGlobalContext.js.aspx"></script>
    <script src="/WebResources/path/to/spaarke-auth.js"></script>
    <style>
        .verify-button {
            padding: 10px 20px;
            background-color: #0078d4;
            color: white;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            font-weight: 600;
            transition: background-color 0.2s;
        }
        .verify-button:hover {
            background-color: #005a9e;
        }
        .verify-button:disabled {
            background-color: #ccc;
            cursor: not-allowed;
        }
        .verify-button.loading::after {
            content: " ⟳";
            animation: spin 1s linear infinite;
        }
        @keyframes spin {
            100% { transform: rotate(360deg); }
        }
        .status-message {
            margin-top: 10px;
            padding: 10px;
            border-radius: 4px;
        }
        .status-message.success {
            background-color: #d4edda;
            color: #155724;
        }
        .status-message.error {
            background-color: #f8d7da;
            color: #721c24;
        }
    </style>
</head>
<body>
    <button id="verifyBtn" class="verify-button">Verify Account</button>
    <div id="statusMessage"></div>

    <script>
        // Web resource context: Xrm.Page.data.entity.getId() returns account ID
        // Call BFF API to verify account
        document.getElementById('verifyBtn').addEventListener('click', async function() {
            const accountId = Xrm.Page.data.entity.getId().replace('{', '').replace('}', '');
            const btn = this;
            btn.disabled = true;
            btn.classList.add('loading');
            btn.textContent = 'Verifying...';

            try {
                const response = await authenticatedFetch(
                    `${window.BFF_API_BASE}/api/communications/accounts/${accountId}/verify`,
                    {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ checkSend: true, checkRead: true })
                    }
                );

                if (response.ok) {
                    const result = await response.json();
                    showMessage(result.message, 'success');
                    // Refresh form to show updated fields
                    setTimeout(() => Xrm.Page.data.refresh(false), 1000);
                } else {
                    const error = await response.json();
                    showMessage(error.detail || 'Verification failed', 'error');
                }
            } catch (err) {
                showMessage(`Error: ${err.message}`, 'error');
                console.error('Verification error:', err);
            } finally {
                btn.disabled = false;
                btn.classList.remove('loading');
                btn.textContent = 'Verify Account';
            }
        });

        function showMessage(text, type) {
            const msgDiv = document.getElementById('statusMessage');
            msgDiv.textContent = text;
            msgDiv.className = `status-message ${type}`;
        }
    </script>
</body>
</html>
```

### Form Refresh After Verification

After successful verification, the form must refresh to display updated values:

```javascript
// Option 1: Soft refresh (recommended - preserves user input)
Xrm.Page.data.refresh(false);

// Option 2: Hard refresh (loses unsaved changes)
Xrm.Page.data.refresh(true);
```

---

## Section 3: Send Quota Display (New Section)

### Purpose
Show administrators how many emails have been sent today vs. the daily limit, with the ability to edit the daily limit.

### Fields to Display

| Field Name | Schema Name | Type | Display Mode | Purpose |
|------------|------------|------|--------------|---------|
| Sends Today | `sprk_sendstoday` | Whole Number | Read-only | Count of emails sent from this account today (UTC) |
| Daily Send Limit | `sprk_dailysendlimit` | Whole Number | Editable | Maximum emails allowed per day from this account |

### Layout Recommendation

```
┌─ Send Quota Section ──────────────────────────────┐
│                                                   │
│  Daily Send Quota:                                │
│  ┌────────────────────────────────────────┐       │
│  │ [████████░░░░░░░░░░░░] 42 / 500        │       │
│  └────────────────────────────────────────┘       │
│                                                   │
│  Sends Today (UTC):     [42]  (read-only)         │
│  Daily Send Limit:      [500] (editable)          │
│                                                   │
│  ℹ Resets daily at 00:00 UTC                      │
│  ℹ Limit applies to shared mailbox sends          │
│                                                   │
└───────────────────────────────────────────────────┘
```

### Field Configuration Details

#### `sprk_sendstoday` (Whole Number - Read-Only)
- **Range**: 0–999,999
- **Read-only**: Yes (updated daily by `DailySendCountResetService`)
- **Required**: No (defaults to 0)
- **Purpose**: Track current day's usage
- **Reset Behavior**: Set to 0 each day at midnight UTC via background service (see Appendix: Daily Count Reset Service)
- **Display**: Show with comma-separators (e.g., "1,234")

#### `sprk_dailysendlimit` (Whole Number - Editable)
- **Range**: 1–10,000 (configurable based on mailbox tier)
- **Read-only**: No (administrators set this)
- **Required**: Yes (should have default like 500)
- **Default Value**: 500 (can be adjusted per account type)
- **Purpose**: Quota limit for this specific account
- **Display**: Show with comma-separators; allow inline editing

### Visual Progress Bar (Optional)

If form supports web resources, consider adding a visual progress bar:

```
Sends Today: 42 / 500
[████████░░░░░░░░░░░░░] 8.4%
```

This provides quick visual feedback on remaining quota.

### Notes on Interaction

- **Editable by**: Account owners and administrators with write permissions
- **Editing**: Click the field to edit; changes take effect immediately
- **Reset**: Daily automated reset happens at `2026-03-XX 00:00:00 UTC`
- **Quota Enforcement**: BFF API respects this limit during send operations (see Appendix: Send Quota Validation)

---

## Section 4: Subscription Status Section (Conditional - New Section)

### Purpose
Display Graph subscription details for accounts with inbound monitoring enabled.

### Visibility Rules

**Show this section only when**:
- `sprk_receiveenabled` = Yes (account has inbound monitoring enabled)

**Implementation**: Use a business rule or form JavaScript to toggle section visibility:

```javascript
// Form OnLoad or field OnChange
if (Xrm.Page.getAttribute("sprk_receiveenabled").getValue() === true) {
    Xrm.Page.ui.tabs.get("Settings").sections.get("SubscriptionSection").setVisible(true);
} else {
    Xrm.Page.ui.tabs.get("Settings").sections.get("SubscriptionSection").setVisible(false);
}
```

### Fields to Display (Read-Only)

| Field Name | Schema Name | Type | Display Mode | Purpose |
|------------|------------|------|--------------|---------|
| Subscription ID | `sprk_subscriptionid` | Text | Read-only | Graph subscription resource ID |
| Subscription Expiry | `sprk_subscriptionexpiry` | DateTime | Read-only | Subscription renewal/expiry timestamp (UTC) |
| Graph Message ID | `sprk_graphuserid` (note: this appears to be mislabeled; should verify actual field) | Text | Read-only | Graph subscription identity reference |
| Subscription Status | `sprk_subscriptionstatus` | OptionSet | Read-only | Current lifecycle state |

### Layout Recommendation

```
┌─ Subscription Status (conditional - visible only when receive-enabled)
│                                                   │
│  Subscription ID:    [d29ab01c-...]  (read-only)  │
│  Subscription Expiry: [2026-04-08 14:32:00 UTC]   │
│                                                   │
│  Status:             [Active] (read-only)        │
│                                                   │
│  ℹ Subscriptions are auto-renewed before expiry   │
│  ℹ Contact support if status is "Failed"          │
│                                                   │
└───────────────────────────────────────────────────┘
```

### Field Configuration Details

#### `sprk_subscriptionid` (Text - Read-Only)
- **Format**: UUID (e.g., "d29ab01c-4d26-4a0e-8ae9-97402d4da9a4")
- **Read-only**: Yes (set by `GraphSubscriptionManager` background service)
- **Required**: No (null if no subscription active)
- **Purpose**: Reference to Graph subscription resource
- **Display Hint**: Show as monospace font for readability

#### `sprk_subscriptionexpiry` (DateTime - Read-Only)
- **Format**: "YYYY-MM-DD HH:MM:SS UTC"
- **Read-only**: Yes (managed by `GraphSubscriptionManager`)
- **Required**: No (null until subscription created)
- **Timezone**: Always UTC
- **Purpose**: When subscription will expire (Graph max 3-day lifetime)
- **Visual Indicator** (optional):
  - Green if > 24 hours remaining
  - Amber if 6–24 hours remaining
  - Red if < 6 hours remaining (renewal failed)

#### `sprk_subscriptionstatus` (OptionSet - Read-Only)
- **Possible Values**:
  - `100000` = Active (healthy subscription)
  - `100001` = Renewing (in process of renewing)
  - `100002` = Failed (last renewal failed)
  - `100003` = Expired (subscription not renewed in time)
  - `100004` = Disabled (account disabled monitoring)

- **Read-only**: Yes (managed by background service)
- **Color Coding** (optional):
  - Green for Active
  - Yellow for Renewing
  - Red for Failed/Expired
  - Gray for Disabled

### System Behavior (Background)

**GraphSubscriptionManager Service**:
- Runs as a BackgroundService in the BFF API
- Monitors all receive-enabled accounts
- Creates subscription if missing (and none exists)
- Renews subscription when < 24 hours remaining
- Updates `sprk_subscriptionid`, `sprk_subscriptionexpiry`, `sprk_subscriptionstatus` on Dataverse

See Appendix: Graph Subscription Lifecycle for implementation details.

---

## Section 5: System Fields Reference Section (New Section)

### Purpose
Display system-managed fields as read-only for reference and debugging.

### Fields to Display (All Read-Only)

| Field Name | Schema Name | Type | Purpose |
|------------|------------|------|---------|
| Graph User ID | `sprk_graphuserid` | Text | Unique Graph identifier for the mailbox principal |
| Tenant ID | `sprk_tenantid` | Text | Azure AD tenant ID (multi-tenant reference) |
| Mailbox Type | `sprk_mailboxtype` | OptionSet | Mailbox classification (Shared, User, etc.) |
| Auth Method | `sprk_authmethod` | OptionSet | Authentication approach (App-Only, Delegated, etc.) |

### Layout Recommendation

```
┌─ System Fields (Reference) ──────────────────────┐
│                                                   │
│  Graph User ID:  [{graph-id-guid}] (read-only)   │
│  Tenant ID:      [{tenant-id-guid}] (read-only)   │
│  Mailbox Type:   [Shared Mailbox] (read-only)    │
│  Auth Method:    [App-Only] (read-only)          │
│                                                   │
│  ℹ These fields are system-managed. Do not edit. │
│                                                   │
└───────────────────────────────────────────────────┘
```

### Field Configuration Details

#### `sprk_graphuserid` (Text - Read-Only)
- **Format**: UUID (Graph service principal object ID)
- **Read-only**: Yes (set during account creation)
- **Required**: Yes (must have value for app-only authentication)
- **Purpose**: Unique identity of the mailbox principal in Microsoft Graph
- **Display**: Monospace font

#### `sprk_tenantid` (Text - Read-Only)
- **Format**: UUID (Azure AD tenant ID)
- **Read-only**: Yes (set during account creation)
- **Required**: Yes (required for multi-tenant support)
- **Purpose**: Which tenant this account belongs to
- **Display**: Monospace font
- **Example**: "12345678-1234-1234-1234-123456789012"

#### `sprk_mailboxtype` (OptionSet - Read-Only)
- **Possible Values**:
  - `100000` = Shared Mailbox (Group mailbox)
  - `100001` = User Mailbox (Individual user)
  - `100002` = Resource Mailbox (Room/equipment)

- **Read-only**: Yes
- **Required**: Yes

#### `sprk_authmethod` (OptionSet - Read-Only)
- **Possible Values**:
  - `100000` = App-Only (service principal, client credentials)
  - `100001` = Delegated (user context, OBO flow)
  - `100002` = User+ (delegated with app permissions fallback)

- **Read-only**: Yes
- **Required**: Yes
- **Note**: Form currently shows "Apo-Only" label (typo); correct to "App-Only" in Dataverse

---

## JavaScript Business Rules for Conditional Visibility

To implement conditional visibility for the Subscription Status section, add the following JavaScript to the form:

### Option A: Business Rule (Recommended in Dataverse)

1. **Create Business Rule**:
   - Name: "Show Subscription Section when Receive Enabled"
   - Entity: `sprk_communicationaccount`
   - Scope: Form

2. **Condition**:
   - If `sprk_receiveenabled` Equals `Yes`

3. **Action**:
   - Show Section: `SubscriptionSection` (or the actual schema name of the section)

### Option B: Form OnLoad JavaScript

If using web resources:

```javascript
function OnFormLoad(executionContext) {
    var form = executionContext.getFormContext();
    var receiveEnabled = form.getAttribute("sprk_receiveenabled");

    if (receiveEnabled && receiveEnabled.getValue() === true) {
        form.ui.tabs.get("Settings").sections.get("SubscriptionSection").setVisible(true);
    } else {
        form.ui.tabs.get("Settings").sections.get("SubscriptionSection").setVisible(false);
    }
}

function OnReceiveEnabledChange(executionContext) {
    OnFormLoad(executionContext);  // Re-evaluate visibility
}
```

---

## Field Mapping Summary

### System-Managed (Updated by BFF API/Background Services)

| Field | Who Updates | When | Frequency |
|-------|-------------|------|-----------|
| `sprk_verificationstatus` | Verification endpoint | Manual verification | On-demand |
| `sprk_lastverified` | Verification endpoint | After successful verification | On-demand |
| `sprk_verificationmessage` | Verification endpoint | After verification attempt | On-demand |
| `sprk_sendstoday` | Email send pipeline + DailySendCountResetService | Each send + daily reset | Per send, once daily at 00:00 UTC |
| `sprk_subscriptionid` | GraphSubscriptionManager | When subscription created/renewed | Every 3 days (renewal) |
| `sprk_subscriptionexpiry` | GraphSubscriptionManager | When subscription created/renewed | Every 3 days (renewal) |
| `sprk_subscriptionstatus` | GraphSubscriptionManager | During subscription lifecycle | Every 3 days + on failure |

### Admin-Editable

| Field | Who Edits | When | Notes |
|-------|-----------|------|-------|
| `sprk_dailysendlimit` | Account admin | Account setup, quota adjustment | No restrictions |

### Display-Only (Never Modified)

| Field | Source | Purpose |
|-------|--------|---------|
| `sprk_graphuserid` | Account creation | Graph service principal ID |
| `sprk_tenantid` | Account creation | Multi-tenant reference |
| `sprk_mailboxtype` | Account creation | Mailbox classification |
| `sprk_authmethod` | Account creation | Authentication method |

---

## Form Tab Organization Final Structure

```
GENERAL TAB
├─ Account Information Section (existing)
│  ├─ Name
│  ├─ Email Address
│  ├─ Display Name
│  └─ Description
├─ Status Section (existing)
│  ├─ Status
│  ├─ Enabled/Disabled toggle
│  └─ Last Modified

SETTINGS TAB (new)
├─ Verification Section (new)
│  ├─ Verification Status (read-only)
│  ├─ Last Verified (read-only)
│  ├─ Verification Message (read-only, multiline)
│  └─ [Verify Account] button
├─ Send Quota Section (new)
│  ├─ Sends Today (read-only)
│  ├─ Daily Send Limit (editable)
│  └─ Info text: "Resets daily at 00:00 UTC"
├─ Subscription Status Section (conditional - visible if receive-enabled)
│  ├─ Subscription ID (read-only)
│  ├─ Subscription Expiry (read-only)
│  ├─ Subscription Status (read-only)
│  └─ Info text: "Auto-renewed before expiry"
└─ System Fields Section (new)
   ├─ Graph User ID (read-only)
   ├─ Tenant ID (read-only)
   ├─ Mailbox Type (read-only)
   ├─ Auth Method (read-only)
   └─ Info text: "System-managed fields"

OTHER TABS (existing, unchanged)
├─ Outbound Configuration
├─ Inbound Configuration
└─ Others as configured
```

---

## Appendix A: Verification Endpoint Specification

### Endpoint

```
POST /api/communications/accounts/{accountId}/verify
Authorization: Bearer {userToken}
Content-Type: application/json
```

### Path Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `accountId` | UUID | Account record ID (sprk_communicationaccountid) |

### Request Body

```json
{
  "checkSend": true,
  "checkRead": true
}
```

### Response (200 OK - Success)

```json
{
  "accountId": "12345678-1234-1234-1234-123456789012",
  "verificationStatus": "Verified",
  "permissionsSummary": {
    "mailSend": true,
    "mailRead": true
  },
  "message": "Account verified successfully.\nPermissions verified:\n- Mail.Send: ✓\n- Mail.Read: ✓",
  "verifiedAt": "2026-03-08T14:32:00Z"
}
```

### Response (400 Bad Request - Verification Failed)

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Verification failed: Mail.Send permission not granted to service principal. Verify app registration has Mail.Send application permission granted in Azure AD.",
  "instance": "/api/communications/accounts/12345678-1234-1234-1234-123456789012/verify"
}
```

### Response (403 Forbidden - Insufficient Permissions)

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.3",
  "title": "Forbidden",
  "status": 403,
  "detail": "You do not have permission to verify this account.",
  "instance": "/api/communications/accounts/12345678-1234-1234-1234-123456789012/verify"
}
```

### Response (404 Not Found)

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404,
  "detail": "Account not found.",
  "instance": "/api/communications/accounts/12345678-1234-1234-1234-123456789012/verify"
}
```

---

## Appendix B: Daily Send Count Reset Service

### Service: `DailySendCountResetService`

**Type**: BackgroundService
**Frequency**: Daily at 00:00 UTC (exact time configurable via `CommunicationOptions.DailyResetUtcHour`)
**Operation**: For each active communication account, reset `sprk_sendstoday` to 0

### Logic

```pseudocode
DailySendCountResetService:OnExecuteAsync():
  EVERY DAY at 00:00 UTC:
    FOR EACH active sprk_communicationaccount record:
      IF sprk_sendenabled == true:
        UPDATE sprk_sendstoday = 0
        LOG: "Reset send count for {accountName}"
      END IF
    END FOR
    SLEEP until next 00:00 UTC
```

### Implementation Notes

- Uses `PeriodicTimer` to schedule daily execution
- Queries only **enabled** accounts (send-enabled == true)
- Updates via Dataverse service client (app-only auth)
- Includes retry logic for transient failures
- Logs all resets for audit trail

---

## Appendix C: Graph Subscription Lifecycle

### Background Service: `GraphSubscriptionManager`

**Type**: BackgroundService
**Frequency**: Every 1 hour (check all receive-enabled accounts)
**Responsibility**:
1. Create subscriptions for receive-enabled accounts that don't have one
2. Renew subscriptions when < 24 hours remaining
3. Recreate subscriptions that failed
4. Update `sprk_subscriptionid`, `sprk_subscriptionexpiry`, `sprk_subscriptionstatus` on Dataverse

### Lifecycle States

```
[Disabled] ←──────────────────────────────────────→ [Active]
   ▲                                                  ▲
   │ (receive-enabled toggled)                        │
   │                                                  │ (subscription created)
   │                                                  │
   │                  (< 24h remaining)               │
   │                  ↓                               │
   └──────────── [Renewing] ←────────────────────────┘
                    │
                    │ (renewal failed)
                    ▼
               [Failed]
                    │
                    │ (retry next cycle)
                    ▼
                [Active] or [Failed] (cycle continues)
```

### Configuration

| Setting | Default | Purpose |
|---------|---------|---------|
| `CommunicationOptions.SubscriptionCheckIntervalMinutes` | 60 | How often to check all subscriptions |
| `CommunicationOptions.SubscriptionRenewalThresholdHours` | 24 | Renew if this many hours remaining |
| `Graph Subscription Max Lifetime` | 3 days | Graph API maximum subscription lifetime |

---

## Appendix D: Send Quota Validation (BFF API)

### Logic Flow

When user calls `POST /api/communications/send`:

```pseudocode
SendAsync():
  1. Load communicationAccount record from Dataverse
  2. Check: sprk_sendenabled == true
  3. Check: sprk_sendstoday < sprk_dailysendlimit
     IF NOT:
       RETURN 429 Too Many Requests
       message: "Daily send quota exceeded: {sendstoday}/{dailysendlimit}"
  4. Proceed with send
  5. On success, increment sprk_sendstoday
```

### Error Response (429 - Quota Exceeded)

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.4",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Daily send quota exceeded for this account: 500/500",
  "instance": "/api/communications/send"
}
```

---

## Appendix E: Form Business Rules Checklist

| Rule Name | Entity | Scope | Condition | Action |
|-----------|--------|-------|-----------|--------|
| "Show Subscription Section when Receive Enabled" | sprk_communicationaccount | Form | `sprk_receiveenabled` = Yes | Show Section: SubscriptionSection |
| "Make Subscription Fields Read-Only" | sprk_communicationaccount | Form | None (always) | Lock Fields: `sprk_subscriptionid`, `sprk_subscriptionexpiry`, `sprk_subscriptionstatus` |
| "Make Verification Fields Read-Only" | sprk_communicationaccount | Form | None (always) | Lock Fields: `sprk_verificationstatus`, `sprk_lastverified`, `sprk_verificationmessage` |
| "Make Sends Today Read-Only" | sprk_communicationaccount | Form | None (always) | Lock Field: `sprk_sendstoday` |
| "Make System Fields Read-Only" | sprk_communicationaccount | Form | None (always) | Lock Fields: `sprk_graphuserid`, `sprk_tenantid`, `sprk_mailboxtype`, `sprk_authmethod` |

---

## Appendix F: Web Resource Registration (if applicable)

### Solution XML Entry

If implementing the Verify button as a custom web resource, register it in the solution XML:

```xml
<webresources>
  <webresource>
    <name>sprk_VerifyAccountButton</name>
    <displayname>Verify Account Button</displayname>
    <description>Web resource to verify mailbox permissions</description>
    <type>5</type> <!-- HTML -->
    <versionnumber>1.0.0.0</versionnumber>
    <publicname>VerifyAccountButton</publicname>
    <solution>CommunicationAccounts</solution>
  </webresource>
</webresources>
```

Form XML reference:

```xml
<form>
  <tabs>
    <tab name="SettingsTab" label="Settings">
      <sections>
        <section name="VerificationSection" label="Verification Status">
          <controls>
            <control id="sprk_verificationstatus" classid="{...}"/>
            <control id="sprk_lastverified" classid="{...}"/>
            <control id="sprk_verificationmessage" classid="{...}"/>
            <control id="VerifyAccountButtonControl"
                     classid="{...}"
                     webresourcename="sprk_VerifyAccountButton"
                     parameter="accountId={accountId}"/>
          </controls>
        </section>
      </sections>
    </tab>
  </tabs>
</form>
```

---

## Implementation Checklist

- [ ] **Create Settings Tab** on main form
- [ ] **Verification Section**:
  - [ ] Add `sprk_verificationstatus` field (read-only)
  - [ ] Add `sprk_lastverified` field (read-only)
  - [ ] Add `sprk_verificationmessage` field (read-only, 5-8 rows)
  - [ ] Add Verify button (ribbon command or web resource)
  - [ ] Test button calls endpoint and refreshes form

- [ ] **Send Quota Section**:
  - [ ] Add `sprk_sendstoday` field (read-only, show comma-separated)
  - [ ] Add `sprk_dailysendlimit` field (editable)
  - [ ] Add info text about daily reset at UTC
  - [ ] Test quota validation in BFF API

- [ ] **Subscription Section**:
  - [ ] Add `sprk_subscriptionid` field (read-only, monospace)
  - [ ] Add `sprk_subscriptionexpiry` field (read-only)
  - [ ] Add `sprk_subscriptionstatus` field (read-only)
  - [ ] Create business rule for conditional visibility (show if `sprk_receiveenabled` = Yes)
  - [ ] Test visibility toggle

- [ ] **System Fields Section**:
  - [ ] Add `sprk_graphuserid` field (read-only, monospace)
  - [ ] Add `sprk_tenantid` field (read-only, monospace)
  - [ ] Add `sprk_mailboxtype` field (read-only)
  - [ ] Add `sprk_authmethod` field (read-only)
  - [ ] Lock all fields from editing

- [ ] **Business Rules**:
  - [ ] Create rule: "Show Subscription Section when Receive Enabled"
  - [ ] Create rules: Lock all system-managed fields from editing
  - [ ] Test all conditional visibility

- [ ] **Web Resource (if using custom button)**:
  - [ ] Create `VerifyAccountButton.html`
  - [ ] Register in solution
  - [ ] Add to Verification Section
  - [ ] Test button behavior (loading, success, error states)

- [ ] **API Integration**:
  - [ ] Verify `POST /api/communications/accounts/{id}/verify` endpoint exists
  - [ ] Verify endpoint returns correct response format
  - [ ] Test error handling (permissions, account not found, etc.)

- [ ] **Background Services**:
  - [ ] Verify `DailySendCountResetService` is deployed
  - [ ] Verify `GraphSubscriptionManager` is deployed
  - [ ] Monitor logs for daily resets and subscription updates

- [ ] **Testing**:
  - [ ] Open account form, verify all sections display correctly
  - [ ] Test verification button with valid and invalid accounts
  - [ ] Test quota display and daily reset
  - [ ] Test subscription section visibility toggle
  - [ ] Verify all read-only fields cannot be edited
  - [ ] Test on desktop and mobile layouts

---

## Related Documentation

- **BFF API Verification Endpoint**: See `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs`
- **DailySendCountResetService**: See `src/server/api/Sprk.Bff.Api/Services/Communication/DailySendCountResetService.cs`
- **GraphSubscriptionManager**: See `src/server/api/Sprk.Bff.Api/Services/Communication/GraphSubscriptionManager.cs`
- **`sprk_communicationaccount` Schema**: See `docs/data-model/sprk_communicationaccount.md`
- **Form Customization Guide**: See Dataverse help: "Create and design forms"
- **Business Rules**: See Dataverse help: "Create business rules and recommendations"

---

**Last Updated**: 2026-03-09
**Status**: Ready for Implementation
**Owner**: Email Communication Solution R2 Project
