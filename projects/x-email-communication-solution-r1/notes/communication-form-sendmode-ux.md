# Communication Form: Send Mode UX Configuration

> **Task**: 062 | **Phase**: 7 (Individual User Outbound)
> **Date**: 2026-02-22

---

## Overview

The `sprk_communication` form compose view needs a "Send From" section so users can choose between sending from a shared mailbox or their own mailbox. The send mode selection drives the `sendMode` field in the BFF `POST /api/communications/send` payload.

**Important**: There is no `sprk_sendmode` Dataverse field. Send mode is a **client-side-only concept** handled entirely by the `sprk_communication_send.js` web resource. The BFF receives the mode via the JSON payload, not from a Dataverse field.

---

## Compose View: "Send From" Section

Add a new section to the **Compose** tab, positioned between the Header and the Association section.

### Section: Send From

**Display Name**: "Send From"
**Collapsible**: No
**Visibility**: Compose mode only (`statuscode == 1`)

| UI Element | Type | Behavior |
|------------|------|----------|
| Send From dropdown | HTML select (injected by JS web resource) | Lists "My Mailbox" + all send-enabled `sprk_communicationaccount` records |
| From field (`sprk_from`) | Text | Auto-populated based on dropdown selection |

### Dropdown Options

The web resource queries `sprk_communicationaccount` on form load:

```
GET /sprk_communicationaccounts?$filter=sprk_sendenableds eq true and statecode eq 0
    &$select=sprk_emailaddress,sprk_displayname,sprk_name,sprk_isdefaultsender
    &$orderby=sprk_isdefaultsender desc,sprk_name asc
```

**Option list built dynamically**:

| Option | Value | Label Example |
|--------|-------|---------------|
| Default shared mailbox | `shared:{emailaddress}` | "Spaarke Central (mailbox-central@spaarke.com)" |
| Other shared mailboxes | `shared:{emailaddress}` | "{displayname} ({emailaddress})" |
| User's own mailbox | `user` | "My Mailbox ({currentUser.email})" |

**Default selection**: The account with `sprk_isdefaultsender == true` (SharedMailbox mode).

---

## Send Mode Behavior

### When Send Mode = SharedMailbox (default)

- `sprk_from` auto-populated with the selected shared mailbox email address
- BFF payload: `{ "sendMode": "sharedMailbox", "fromMailbox": "mailbox-central@spaarke.com" }`
- BFF uses `GraphClientFactory.ForApp()` (application permissions)

### When Send Mode = User ("My Mailbox")

- `sprk_from` auto-populated with current user's primary email (from `Xrm.Utility.getGlobalContext().userSettings.userName` or user record query)
- `sprk_sentby` shows the current user's name after send
- BFF payload: `{ "sendMode": "user" }` (no `fromMailbox` needed)
- BFF uses `GraphClientFactory.ForUserAsync()` (OBO delegated auth)
- Bearer token from Dataverse session passed in `Authorization` header

---

## Read Mode Behavior

After sending, regardless of which send mode was used:
- `sprk_from` displays the actual sender address (set by BFF response `from` field)
- `sprk_sentby` shows the Dataverse user who initiated the send
- The "Send From" dropdown is not visible (only shown in Draft/compose mode)

---

## Business Rules Needed

### BR: Pre-populate From on Mode Change (Client-Side JS)

Implemented in `sprk_communication_send.js`, not as a Dataverse business rule:

1. **On form load (new record)**: Query enabled accounts, build dropdown, select default sender, set `sprk_from` to default shared mailbox email
2. **On dropdown change to shared mailbox**: Set `sprk_from` to selected account's `sprk_emailaddress`
3. **On dropdown change to "My Mailbox"**: Set `sprk_from` to current user's email address

No Dataverse-side business rule is needed because the send mode is transient (not persisted).

---

## Web Resource: How Send Button Reads Send Mode

In `sprk_communication_send.js`, the `_buildRequest` function is updated (Task 061) to include:

```javascript
// Determine send mode from the injected dropdown
var sendFromDropdown = document.getElementById("sprk-send-from-select");
var selectedValue = sendFromDropdown ? sendFromDropdown.value : null;

if (selectedValue === "user") {
    request.sendMode = "user";
    // fromMailbox not needed - BFF resolves from OBO token
} else if (selectedValue && selectedValue.startsWith("shared:")) {
    request.sendMode = "sharedMailbox";
    request.fromMailbox = selectedValue.replace("shared:", "");
} else {
    // Fallback: default shared mailbox mode
    request.sendMode = "sharedMailbox";
}
```

The bearer token is passed in the `Authorization` header (already implemented) to support the OBO flow when `sendMode = "user"`.

---

## References

- **Web resource**: `src/solutions/LegalWorkspace/src/WebResources/sprk_communication_send.js`
- **BFF SendMode enum**: `src/server/api/Sprk.Bff.Api/Services/Communication/Models/SendMode.cs`
- **Form config**: `projects/email-communication-solution-r1/notes/communication-form-config.md`
- **Account admin guide**: `projects/email-communication-solution-r1/notes/communication-account-admin-guide.md`
- **Task 061**: Web resource send mode implementation (dependency)
- **Spec FR-26/FR-27**: Individual user outbound requirements
