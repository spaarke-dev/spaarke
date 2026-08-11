# Task 073 — Manage Access / TrackingFieldTrio PCF — live-UAT punch list (ORDERED)

> Durable record before compaction (2026-08-11). The P2b access-write wave (070 BFF write / 071 grant UI /
> 072 entitlement) is deployed; live UAT surfaced many latent bugs (the grant UI was never opened live in
> teams-app-r1). Work the OPEN items **in order**. PCF deploys to **SPAARKE DEV 1** (spaarkedev1); env label
> shows "SANDBOX" (app banner, not the env name). Current PCF version: **v1.0.16**.

## HOW TO REBUILD + REDEPLOY THE PCF (every change)
```
cd src/client/pcf/TrackingFieldTrio
# bump version in 5 places (ControlManifest.Input.xml, index.ts versionText, Solution/solution.xml,
#   Solution/Controls/.../ControlManifest.xml, Solution/pack.ps1) — use sed, e.g. 1.0.16 -> 1.0.17
npm run build:prod
cp out/controls/bundle.js Solution/Controls/sprk_Spaarke.Controls.TrackingFieldTrio/bundle.js
cd Solution && ./pack.ps1
pac solution import --path bin/TrackingFieldTrioSolution_v<ver>.zip --force-overwrite --publish-changes
# then: git add src/client/pcf/TrackingFieldTrio/ + the shared modal; commit; push
```
Shared modal (`AccessGrantModal`) is in `src/client/shared/Spaarke.UI.Components/src/components/AccessGrantModal/`
and is **bundled into the PCF** by build:prod (via ensure-dist-fresh). The trio's shared core is
`src/client/shared/Spaarke.UI.Components/src/components/TrackingFieldTrio/TrackingFieldTrio.tsx`.

## KEY FACTS (verified this session — do not re-derive)
- **Auth config = Dataverse ENVIRONMENT VARIABLES** (not form fields). The trio (v1.0.15+) reads them via
  `getEnvironmentVariable(webApi, ...)` + `getApiBaseUrl(webApi)` from `../shared/utils/environmentVariables`,
  with manifest-input fallback. Values live on spaarkedev1: `sprk_MsalClientId`=**170c98e1-d486-4355-bcbe-170454e0207c**,
  `sprk_BffApiAppId`=**1e40baad-e065-4aea-a8d4-4b7ab273458c**, `sprk_BffApiBaseUrl`=**https://spaarke-bff-dev.azurewebsites.net/api**.
  Scope built as `api://${bffAppId}/user_impersonation`. (The `4a4d5126` app is the CIAM/external BFF — NOT for the workforce PCF.)
- **Dataverse reads MUST use `_X_value` FK fields + FormattedValue annotations**, NOT `$select`/`$expand` of
  lookup logical names (that 400s) and NOT lowercase nav names (`sprk_contactid` is wrong; the nav property is
  PascalCase `sprk_Contact` per task 070). fetchCandidates/fetchExistingGrants were rewritten this way (v1.0.14).
- **WebAPI feature** must be declared in the manifest (`<feature-usage><uses-feature name="WebAPI" required="true"/>`),
  in BOTH ControlManifest.Input.xml AND the packed Solution ControlManifest.xml (v1.0.13).
- **Xrm side-pane Advanced Lookup vs Fluent modal:** the OOB Xrm lookup (`Xrm.Utility.lookupObjects`) layers IN
  FRONT of a Code Page (e.g. CreateNewMatter wizard) because a Code Page isn't a high-z portal; it lands BEHIND
  the PCF's Fluent Dialog (SprkModal) which portals above the shell. To get "lookup in front of the PCF modal"
  we must lower the Fluent modal z-index during pick OR use an in-app Fluent picker.
- **Organization grant** = firm-scoping metadata only (writes `sprk_Organization` on the grant); access is
  ALWAYS per-contact — an org grant does NOT grant all org members.
- **Standing grant** = `contact.sprk_standinggrant` (global contact flag). Checking it in Manage Access WRITES
  that flag on the Contact record (global, not per-record). A standing-grant contact gets ongoing membership
  (server unions it) so they generally don't need a per-record grant.
- **Access levels** = ViewOnly(100000000) / Collaborate(100000001) / FullAccess(100000002) — the grant's
  `sprk_accesslevel`. Modal now has a level dropdown (v1.0.16). DISTINCT from the record's Access Permission
  (Standard/Limited/Restricted) sharing gate.

## PCF VERSION HISTORY (this UAT)
- v1.0.13 — WebAPI feature declaration (fixed "Failed to load access data" read + email).
- v1.0.14 — reads rewritten to `_X_value` + FormattedValue (fixed matter `$select` 400 + `sprk_contactid` 400).
- v1.0.15 — auth reads env vars (fixed grant-write "Failed to fetch"/"MSAL not configured"); toolbar moved top-right.
- v1.0.16 — Manage Access redesign: "Available Contacts & Organizations" + "Current Access" (20px semibold);
  `+ Contact`/`+ Organization` in the section header; access-level dropdown; Restricted banner light-red with
  new copy; footer Cancel(left)+Save(right).

## OPEN PUNCH LIST — WORK IN ORDER
1. **Email icon → PCF crashes (React #31).** Clicking the email icon blanks the PCF. Error: "Minified React
   error #31 ... object with keys {$$typeof,type,key,ref,props}" (a React element rendered as a child). The
   email flow renders the shared `SendEmailDialog` (`@spaarke/ui-components` EmailComposer, updated by the
   master merge) from `TrackingFieldTrio/index.ts` (~line 743) + an empty-state Dialog. Likely a shared
   EmailComposer/SendEmailDialog incompatibility surfaced by the trio's props (open/onClose/initialTo/
   authenticatedFetch/bffBaseUrl/titleOverride/regarding{entityType,id}/onSent/onError). Diagnose the
   object-as-child; consider an error boundary around the dialogs so a shared-component crash can't blank the PCF.
   - INVESTIGATION SO FAR (2026-08-11): the `regarding` prop shape is FINE — `ISendEmailDialogRegarding` =
     `{entityType, id, name?}`, matches the trio's `{entityType:getHostEntity(), id:recordId}`; SendEmailDialog
     folds it into ADR-024 `associations` (not rendered as a child). So the object-as-child is elsewhere in the
     EmailComposer engine render OR the trio's empty-state Dialog (`index.ts` ~line 763, uses `children:` via
     createElement) — the empty-state path (record with NO emailable members) is the likely trigger for test
     records. NEXT STEPS: (a) reproduce with a non-minified/dev build (or React devtools) to get the real
     component stack for error #31; (b) add a React-16 class ErrorBoundary around BOTH the SendEmailDialog and
     the empty-state Dialog in the trio so a shared-EmailComposer crash degrades gracefully instead of blanking
     the whole PCF; (c) fix the underlying element-as-child once the stack points to it. The email flow was
     never live-tested in teams-app-r1 (like the grant flow), so treat it as latent.
2. **Standing-grant contacts auto-listed in "Current Access".** If a Contact has `sprk_standinggrant=true`
   (e.g. Eyal Iffergan), they should already appear in Current Access for a record (they have standing
   membership). Needs a NEW read: query contacts where `sprk_standinggrant=true` and union them into the
   Current Access list (mark "Standing grant", not per-record-revocable — no accessRecordId). Add a PCF callback
   (e.g. fetchStandingContacts) + merge in the modal; make IAccessGrantRecord.accessRecordId optional or use a
   sentinel + hide Revoke for standing rows.
3. **Reusable PCF title/header (32px, semibold).** The PCF should render its OWN header (title + the person/
   email action icons) at **32px semibold**, matching the other PCFs (CommunicationConversationPanel renders a
   `title` from `context.parameters.title?.raw`). Field-label row (Monitor/HighPriority/AccessPermission)
   separate below. Make it a STANDARD reusable header so PCFs don't re-roll it. Candidates: shared `PaneHeader`
   (semibold title + `rightSlot`, but 40px — needs a 32px variant), `HeaderToolbar`, `RecordHeader`. Add a
   `title` input property to the trio manifest. (Currently the toolbar is absolute top-right of the control box,
   v1.0.15 — not a real header.)
4. **Lookup in front of the open modal (the "standard pattern").** Decide + standardize: (a) lower the SprkModal
   Fluent-Dialog z-index below the Xrm pane during pick (keeps the rich OOB Advanced Lookup — recommended), or
   (b) switch to an in-app Fluent picker (BrowseModal/RecordNavigationModalShell) that stacks natively. Currently
   hide/reopen (v1.0.13). The user wants "modal stays open, pane in front" (like the CreateNewMatter Code Page).
5. **Thin scrollbar standard.** Modal body should use the canonical modern thin scrollbar. Need the exact
   standard (SprkModal has `bodyScroll: 'native'|'arrows'`; ModalScrollArea hides the bar + adds chevrons —
   confirm which is "our standard" before applying).
6. **Save/Cancel semantics.** Footer is Cancel/Save now, but grants commit immediately on "Add" — confirm
   whether the owner wants a staged/transactional model (Save commits pending changes) or Save = close.
7. **`+ Organization` interaction** — org is currently applied as optional firm-scope on the contact grant
   (per #6 answer). Confirm the owner wants it in the "Available" list purely as scope (not a grantee).

## ANSWERS ALREADY GIVEN TO OWNER (this session)
- Org grant = firm scoping only (not all-org-members) — punch #6/#7 above.
- Standing grant = global contact flag; checking it writes the Contact; standing contacts get ongoing membership.
- Access levels = ViewOnly/Collaborate/FullAccess; modal now exposes a picker (v1.0.16).
- CreateNewMatter lookup = OOB Xrm side pane; in front because it's a Code Page (punch #4).

## DEPLOYED STATE
- BFF: spaarke-bff-dev @ HEAD (070 write + 072 entitlement + master), 48.48 MB, healthy. `/me/entitlements` live (401 unauth).
- PCF: TrackingFieldTrio **v1.0.16** on SPAARKE DEV 1.
- external-spa SWA: deployed via `deploy-external-spa.yml` (owner triggered) — the me-client mock→real flip
  (bffApiCall + graceful fallback) is in the build.
- Branch `work/spaarke-SPA-external-access-platform-r2`: pushed (last commit ca7160ded), synced with master.
