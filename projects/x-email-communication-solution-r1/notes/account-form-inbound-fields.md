# sprk_communicationaccount Form: Inbound Configuration Fields

## Inbound Configuration Section

| Field | Schema Name | Type | Default | Notes |
|-------|-------------|------|---------|-------|
| Receive Enabled | sprk_receiveenabled | Two Option | No | Master toggle for inbound monitoring |
| Monitor Folder | sprk_monitorfolder | Single Line | Inbox | Graph mailFolder name to watch |
| Auto-Create Records | sprk_autocreaterecords | Two Option | No | Auto-create sprk_communication on inbound |

**Placement**: Add section below the existing "Send Configuration" section on the main form tab.

## Graph Integration Section (Read-Only)

| Field | Schema Name | Type | Read-Only | Notes |
|-------|-------------|------|-----------|-------|
| Subscription ID | sprk_subscriptionid | Single Line | Yes | Graph webhook subscription ID |
| Subscription Expiry | sprk_subscriptionexpiry | DateTime | Yes | Auto-renewed by GraphSubscriptionManager |

**Placement**: Below Inbound Configuration section. Both fields are always read-only -- they are managed exclusively by GraphSubscriptionManager (BackgroundService).

## Business Rules

### BR-1: Conditional Visibility for Inbound Fields
- **Condition**: sprk_receiveenabled = No
- **Action**: Hide sprk_monitorfolder and sprk_autocreaterecords
- **Rationale**: Reduces admin confusion when inbound monitoring is disabled

### BR-2: Subscription Fields Always Read-Only
- sprk_subscriptionid and sprk_subscriptionexpiry are locked on the form
- Values are set programmatically by GraphSubscriptionManager
- Admins can view subscription status but cannot edit

## Admin Guidance
- Set sprk_receiveenabled = Yes to enable inbound email monitoring
- Monitor folder defaults to "Inbox"; change only if using a dedicated subfolder
- Subscription fields populate automatically within 30 minutes of enabling receive
- If subscription fields remain empty after 1 hour, check GraphSubscriptionManager logs
