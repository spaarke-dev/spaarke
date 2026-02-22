# sprk_communicationaccount Form: Verification UI

## Verification Section Fields

| Field | Schema Name | Type | Read-Only | Notes |
|-------|-------------|------|-----------|-------|
| Verification Status | sprk_verificationstatus | OptionSet | Yes | Set by MailboxVerificationService |
| Last Verified | sprk_lastverified | DateTime | Yes | Timestamp of last verification attempt |

**Placement**: Add section on the main form tab, below Graph Integration section.

## Status Badge Display

| Status Value | Label | Color | Badge Style |
|-------------|-------|-------|-------------|
| 100000000 | Verified | Green | Success |
| 100000001 | Failed | Red | Danger |
| 100000002 | Pending | Yellow | Warning |
| null | Not Verified | Gray | Neutral |

## Verify Command Bar Button

### Configuration
- **Button Label**: "Verify"
- **Icon**: CheckMark (Fluent UI icon)
- **Location**: Form command bar (main form only)
- **Enable Rule**: Record must be saved (has ID); form must not be dirty
- **Display Rule**: Always visible on sprk_communicationaccount form

### Command Definition
- **Action**: Call `sprk_communicationaccount_verify` JS web resource function
- **Endpoint**: `POST /api/communications/accounts/{recordId}/verify`
- **Auth**: Bearer token from Xrm.Utility.getGlobalContext() authentication

### JS Web Resource (sprk_account_verify.js)
1. Get record ID from `Xrm.Page.data.entity.getId()`
2. Call BFF API verify endpoint with bearer token
3. On success: show notification with verified capabilities, refresh form
4. On failure: show error notification with details
5. Auto-refresh sprk_verificationstatus and sprk_lastverified after call

## Business Rules

### BR-1: Warning Banner on Failed Verification
- **Condition**: sprk_verificationstatus = Failed (100000001)
- **Action**: Show form notification (warning level) -- "Mailbox verification failed. Check permissions and re-verify."
- **Trigger**: On form load

### BR-2: Verification Fields Always Read-Only
- sprk_verificationstatus and sprk_lastverified are locked on the form
- Values are set only via the BFF API verification endpoint
