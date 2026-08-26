# U-CB-3 — BFF App Registration Permission Additions (Customer Communication Template)

> **Purpose**: Plain-text operator-facing template to notify a customer that an upcoming Spaarke upgrade adds Microsoft Graph (or other) permissions to the Spaarke BFF multi-tenant app registration and requires the customer's Entra ID Global Administrator to click the admin-consent URL again.
> **Applies when**: A Spaarke release adds one or more delegated or application permissions to the Spaarke BFF multi-tenant app registration in the Spaarke home tenant. Handler H0.5 detects the un-consented delta and surfaces the re-consent flow.
> **Owner**: Spaarke Platform Operations (release manager) + customer's Entra ID Global Administrator (consenting party).
> **Delivery format**: Plain-text markdown — copy into the operator's chosen channel. No HTML, no branded styling. Operator adapts wording per channel norms.
> **Related**: `../version-compatibility-matrix.md` · `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` · `projects/customer-provisioning-orchestration-r1/design.md` §14A.4 U-CB-3 · H0.5 re-consent flow (design.md v3.2)

---

## 1. Summary

Spaarke is preparing an upgrade for your environment (`{customerName}` / `{environmentName}`) that **adds new API permissions to the Spaarke BFF multi-tenant app registration**. Your Entra ID Global Administrator must **re-consent** to the updated permission set before the new features work in your tenant.

New permissions being added in release `{targetBffVersion}`:

- API: `{apiName}` — Permission: `{permissionName}` — Type: `{"delegated" | "application"}` — Purpose: `{shortPurposeDescription}`
- (repeat per new permission)

**This is a breaking change (U-CB-3)** in the sense that until re-consent is granted, the BFF operations requiring the new permission return authorisation errors. All previously-consented permissions continue to function.

## 2. Trigger conditions (why you are receiving this)

You are receiving this notice because ALL of the following are true:

- Release `{targetBffVersion}` adds Graph/API permissions not present in your current consented set.
- Handler H0.5 (Spaarke re-consent detection) has run against your tenant and confirmed the delta.
- The affected permissions require **admin consent** (they cannot be user-consented).

## 3. Customer impact

- **What continues working**: All Spaarke features that use permissions from your currently-consented set continue functioning normally, both before and after the upgrade.
- **What stops working until re-consent**: `{listFeaturesGatedOnNewPermissions}` — these features return HTTP 403 / consent-required errors until re-consent completes.
- **Who needs to act**: Your Entra ID Global Administrator (typically the same person who granted initial consent during onboarding). NOT end users; NOT tenant users with lower privileges.
- **Duration of impact**: From release apply until the Global Admin clicks the admin-consent URL and completes the consent flow (typically <5 minutes of admin's time; consent takes effect immediately across the tenant).

## 4. Timeline

| Milestone | Target date/time (all times {timezone}) |
|---|---|
| This notice sent | `{noticeSentDate}` |
| Consent URL sent to Global Admin | `{consentUrlSentDate}` |
| Recommended consent-completion date (before upgrade apply) | `{recommendedConsentBy}` |
| Upgrade apply (releases new features) | `{applyDate}` |
| **Consent hard deadline** (after which BFF operations start failing) | `{consentHardDeadline}` |
| Post-upgrade verification report | `{verificationReportDate}` |

Best practice: complete re-consent **before** the upgrade apply so no feature interruption is user-visible. Re-consent AFTER apply is acceptable but produces an intermittent-error window whose length equals the time between apply and consent-completion.

## 5. Required customer action

Your Entra ID Global Administrator must:

1. **Receive the consent URL** from `{operatorEmail}`. Spaarke sends the URL directly to `{globalAdminContact}` on `{consentUrlSentDate}`. The URL is of the form `https://login.microsoftonline.com/{customerTenantId}/adminconsent?client_id={spaarkeBffAppId}&redirect_uri={consentRedirectUri}`.
2. **Sign in as Global Administrator** in an Entra ID tenant admin browser session (private/incognito recommended to avoid stale session state).
3. **Click the consent URL** and complete the Microsoft consent screen. Review the requested permissions against §1 of this notice.
4. **Confirm completion** via §6 below. Spaarke also verifies consent grants via `Get-MgOauth2PermissionGrant` and records the result in your ProvisioningRun record.

**If the initial consent URL fails** (e.g. session expired, browser blocked popup), request a fresh URL from `{operatorEmail}`. Consent URLs are stateless and can be regenerated on demand.

## 6. Confirmation of receipt (required)

Please have the Global Administrator reply to `{operatorEmail}` (or acknowledge in `{acknowledgementChannel}`) with:

> "`{customerName}` Entra ID Global Administrator `{globalAdminName}` completed admin consent for the Spaarke BFF app registration (`client_id={spaarkeBffAppId}`) on `{consentCompletionDate}` at `{consentCompletionTime}` `{timezone}`. Permissions consented: as listed in the U-CB-3 notice dated `{noticeSentDate}`."

Spaarke verifies the consent grant server-side and records it in the ProvisioningRun record for the audit trail. **No consent = new features remain unavailable** (the release itself still applies; the newly-gated features simply return authorisation errors).

## 7. Rollback semantics

Re-consent is not itself rollback-eligible — it is additive; withdrawing consent is a Microsoft-side operation the customer controls. However:

1. **New-feature rollback**: if the release must be rolled back (independent of consent), Spaarke reverts the BFF to `{previousBffVersion}`. Previously-consented permissions still cover the reverted BFF; no customer action required. The newly-added permissions remain granted in your tenant but unused (no side effect).
2. **Consent revocation**: if you wish to revoke consent for a specific permission, the customer's Global Admin does so via Entra Portal → Enterprise Applications → Spaarke BFF → Permissions → Remove. Spaarke will detect the revocation on next H0.5 run and re-notify per U-CB-3.
3. **App-registration rollback** (Spaarke-side): if Spaarke needs to remove a permission entirely from the app registration in the Spaarke home tenant, all customer tenants automatically lose that permission — no per-tenant action required.

Full re-consent procedure: `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` → H0.5 Re-consent Flow.

---

*Template last reviewed: 2026-08-17 · Author when editing: Spaarke Platform Operations · Change record: track in git.*
