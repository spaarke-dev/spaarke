# FR-21: Teams live check (task 071), closed 2026-10-29

> Masked evidence only: no tokens, full ids, emails or UPNs. Dated detail: `task-070-deploy-record.md` (Teams live checks).
> Environment: dev BFF `136568cf70`. Dev SPA from master `96f1f6b675` (run 40989384010; rollback tag `external-spa-dev-rollback-f07ab2ef1`). Dev Teams package v1.2.0, manifest 1.29, no `webApplicationInfo`.

## Result: 8 of 9 runs pass; the ninth was deferred by the owner

| Identity | Teams desktop | Teams web | Teams mobile |
|---|---|---|---|
| Workforce contact (Tracy Cheatham, registered contact, Dewey Cheatham member) | ✅ | ✅ after Teams' warning page ("Show the app anyway") | ✅ loads; layout not phone-optimized (R4 #2466, ISS-025) |
| Licensed user (Ralph Dewey, linked systemuser) | ✅ | ✅ after Teams' warning page | ✅ loads |
| Unlisted directory (Spaarke-tenant user) | ✅ "Your organization is not set up for Spaarke" | ✅ same | not run. The owner deferred mobile ("we will revisit in the future"). The screen comes from `main.tsx` `bootstrapTeams` before any token request, the same code on every client. |

## Sign-in path and claims (masked)

- **Path:**
  - Passing runs show no path on screen. The diagnostic appears only on the guest screen.
  - Before the fix, Teams desktop got as far as the guest check, so a token was acquired. The path was not captured.
  - The guest screen now prints `path=naa|popup acct= home= ext= src=` for future runs.
- **Server evidence** (App Insights, read-only, 2026-10-29 16:00Z to 2026-10-30 03:01Z):
  - 54 × `[WF-AUTH] Resolved home tid=… oid=… to linked systemuser` (Ralph Dewey);
  - 168 × resolutions to a WorkforceHome contact (Tracy Cheatham and the other registered contacts).
  - The only other `[WF-AUTH]` lines: 8 × `Denying request: sdap.access.registration_required`, 16:44–16:51Z. That is the Dewey Cheatham admin before its registration at 16:51:12Z, which is the designed answer for an unregistered caller.
  - No WorkforceHome refusals after the fix.
  - Every accepted token passed the BFF scheme: `tid` in `WorkforceIdentity:CustomerTenantIds`, `acct` = 0, issuer match, `scp` contains `user_impersonation`.

## Defects found and fixed

- **Members shown "This tab can't be used with a guest account" on Teams desktop.** Ralph Dewey on the owner's PC; Tracy Cheatham on a fresh PC, so not a stale cache.
  - Cause: the tab decided "guest" from the host's account description, read from the cached active account.
  - Fix: PR #2461 (`d91ab007b1`, `a60e3de952`). The guest verdict now comes from the issued token's `acct` claim, the one the BFF requires. Every Teams sign-in makes its own account active, and the guest screen shows the masked diagnostic.
  - Adversarial review: no finding that admits a guest.
  - Deployed to dev 2026-10-29 (owner "go 96f1f6b675"). Both members then passed.
- **Known limit (review K1):** the first token after a page load can still come from an account cached earlier in that Teams webview. `src=cache` on the guest screen will show this if it happens.
- **Policy note (review K4):** a cross-tenant *Member* of the customer tenant (`acct` = 0) now reaches the app. The BFF already accepted them.

## Not Spaarke defects (recorded for support)

- **Teams web warning page.** "This app may have issues in the web version of Teams" is shown by Teams before the app loads. "Show the app anyway" opens the tab inside Teams. "Open app in browser tab" opens the manifest `websiteUrl`, the standalone SPA on the browser plane; the earlier "opens in its own browser" runs were this. Public reports tie the page to new Teams web and blocked third-party cookies (OfficeDev/microsoft-teams-library-js#2185). Not confirmed for our case.
- **Teams desktop's own sign-in failing** ("We can't connect you", HTTP 400 at login.microsoftonline.com; "No Network Connection [2603]"). On Tracy's first PC, the Teams client could not sign her in, before Spaarke loads. It worked on another PC.

## Broker pre-authorization question: answered NO

- **On dev the question can't be tested.** The dev BFF app (`1e40baad…`) pre-authorizes Teams desktop/mobile `1fec8e78…`, Teams web `5e3ce6c0…` and the Microsoft Authentication Broker `29d9ed98…` on `access_as_user` and `user_impersonation` (read-only `az ad app show`, owner "ok read"). R3 task 040 added `user_impersonation`.
- **Microsoft's Teams NAA doc** (https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/authentication/nested-authentication, updated 2026-09-30; quotes checked):
  - "You don't need to preauthorize your hosts, such as Teams or Microsoft 365, to call your endpoints."
  - "Authorize Teams client apps" is Required only for traditional Teams Entra SSO; the NAA column is blank.
  - NAA needs only the SPA redirect `brk-multihub://<domain>`.
- **The broker id appears in no Microsoft NAA document.** teams-app-r1's broker pre-authorization did not fix its NAA failure (its own root-cause note).
- **What a stamp needs instead:** consent in the customer tenant for our multitenant client on the API's `user_impersonation` (admin consent), or the API pre-authorizing our own client id.
- **No H3 request to provisioning.** The dev pre-authorizations stay in place: the dev BFF app is shared and other projects may use Teams SSO.
- **Not tested live:** NAA when consent is missing.

## Follow-ups

- R4 #2466: Teams mobile layout.
- The full operating guide is `docs/guides/EXTERNAL-ACCESS-TEAMS-APP-GUIDE.md` (owner 2026-10-29: document the Teams deployment and setup for future reference).
